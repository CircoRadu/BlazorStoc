using System.Data;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace BlazorStoc.Services;

public sealed class SqliteProjectFileStore(SqliteLocalStore store, IAccessControl? accessControl = null,
    IArchiveService? archiveService = null) : IProjectFileStore
{
    private readonly IArchiveService archiver = archiveService ?? new ArchiveService(accessControl);
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<IReadOnlyList<ProjectObservationFile>> GetFilesAsync(int observationId, CancellationToken cancellationToken = default)
    {
        await using var connection = await store.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = SqliteLocalStore.Command(connection, null, """
            SELECT id,observation_id,original_name,relative_path,content_type,byte_length,sha256,author,uploaded_utc
            FROM project_observation_files WHERE observation_id=@observation ORDER BY uploaded_utc,id
            """, ("@observation", observationId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<ProjectObservationFile>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(Read(reader));
        return result;
    }

    public async Task<ProjectFileContent?> GetContentAsync(int fileId, CancellationToken cancellationToken = default)
    {
        await using var connection = await store.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = SqliteLocalStore.Command(connection, null,
            "SELECT relative_path,content_type,original_name FROM project_observation_files WHERE id=@id", ("@id", fileId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        var path = Resolve(reader.GetString(0));
        if (!File.Exists(path)) return null;
        var content = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        return new(content, reader.GetString(1), reader.GetString(2));
    }

    public async Task<ProjectObservationFile> SaveAsync(int observationId, string originalName, string declaredContentType,
        byte[] content, string author, CancellationToken cancellationToken = default)
    {
        var contentType = ProjectFileRules.DetectContentType(content, declaredContentType, originalName);
        var hash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        var prepared = ProjectFileRules.Create(0, observationId, originalName, contentType, content.LongLength, hash, author, DateTime.UtcNow);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(store.ProjectFilesPath);
            var destination = Resolve(prepared.StoredName);
            await using var connection = await store.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using (var count = SqliteLocalStore.Command(connection, null,
                "SELECT COUNT(*) FROM project_observation_files WHERE observation_id=@observation", ("@observation", observationId)))
                if (Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) >= ProjectFileRules.MaximumFilesPerObservation)
                    throw new ProjectOperationException($"O observație poate avea cel mult {ProjectFileRules.MaximumFilesPerObservation} de fișiere.");
            await File.WriteAllBytesAsync(destination, content, cancellationToken).ConfigureAwait(false);
            try
            {
                await using var insert = SqliteLocalStore.Command(connection, null, """
                    INSERT INTO project_observation_files
                        (observation_id,relative_path,original_name,content_type,byte_length,sha256,author,uploaded_utc)
                    VALUES(@observation,@path,@name,@contentType,@length,@hash,@author,@uploaded);
                    SELECT last_insert_rowid();
                    """, ("@observation", observationId), ("@path", prepared.StoredName), ("@name", prepared.OriginalName),
                    ("@contentType", contentType), ("@length", prepared.SizeBytes), ("@hash", hash), ("@author", prepared.Author),
                    ("@uploaded", prepared.UploadedAtUtc.ToString("O")));
                var id = checked((int)(long)(await insert.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!);
                return prepared with { Id = id };
            }
            catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
            {
                File.Delete(destination);
                throw new ProjectOperationException("Observația nu mai există. Actualizează pagina înainte să reîncerci încărcarea.");
            }
            catch
            {
                if (File.Exists(destination)) File.Delete(destination);
                throw;
            }
        }
        finally { gate.Release(); }
    }

    public async Task DeleteAsync(int fileId, string reason, CancellationToken cancellationToken = default)
    {
        var motif = ChangeReasonRules.Normalize(reason);
        if (ChangeReasonRules.ValidationError(motif) is { } reasonError) throw new ProjectOperationException(reasonError);
        await using var lookup = await store.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = SqliteLocalStore.Command(lookup, null, """
            SELECT id,observation_id,original_name,relative_path,content_type,byte_length,sha256,author,uploaded_utc
            FROM project_observation_files WHERE id=@id
            """, ("@id", fileId));
        ProjectObservationFile? file;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            file = await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? Read(reader) : null;
        if (file is null) throw new ProjectOperationException("Fișierul a fost eliminat deja. Actualizează pagina.");

        await archiver.ExecuteAsync(ArchiveRequests.ProjectObservationFile(file, motif), async (operation, token) =>
        {
            await gate.WaitAsync(token).ConfigureAwait(false);
            ArchiveFileRecord? prepared = null;
            try
            {
                prepared = await ArchiveFileSafety.PrepareAsync(store.ProjectFilesPath, store.ArchiveFilesPath,
                    file.StoredName, operation, AuditEntities.ProjectObservationFile, file.Id.ToString(),
                    file.ContentType, file.OriginalName, file.SizeBytes, token).ConfigureAwait(false);
                await using var connection = await store.OpenConnectionAsync(token).ConfigureAwait(false);
                await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, token).ConfigureAwait(false);
                try
                {
                    await SqliteArchivePersistence.InsertAsync(connection, transaction, operation, [prepared], token).ConfigureAwait(false);
                    await using var delete = SqliteLocalStore.Command(connection, transaction,
                        "DELETE FROM project_observation_files WHERE id=@id", ("@id", file.Id));
                    if (await delete.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
                        throw new ProjectOperationException("Fișierul a fost eliminat deja. Actualizează pagina.");
                    await SqliteLocalStore.InsertAuditAsync(connection, transaction, new(operation.ActorUsername, operation.ActorRole,
                        AuditEntities.ProjectObservationFile, AuditActions.Delete, operation.Request.Target, operation.Request.Details,
                        operation.Request.Motif, file.Id.ToString(), operation.Id), token).ConfigureAwait(false);
                    await transaction.CommitAsync(token).ConfigureAwait(false);
                }
                catch
                {
                    await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                    throw;
                }
                await ArchiveFileSafety.CompleteAsync(store.ProjectFilesPath, store.ArchiveFilesPath, prepared, token).ConfigureAwait(false);
            }
            catch
            {
                ArchiveFileSafety.Rollback(store.ArchiveFilesPath, prepared);
                throw;
            }
            finally { gate.Release(); }
        }, cancellationToken).ConfigureAwait(false);
    }

    private string Resolve(string relativePath)
    {
        var root = Path.GetFullPath(store.ProjectFilesPath) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(store.ProjectFilesPath, relativePath));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new ProjectOperationException("Calea fișierului nu este validă.");
        return path;
    }

    private static ProjectObservationFile Read(SqliteDataReader reader) => new(reader.GetInt32(0), reader.GetInt32(1),
        reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetInt64(5), reader.GetString(6),
        reader.GetString(7), SqliteProjectRepository.ReadUtc(reader, 8));
}
