using System.Data;
using System.Security.Cryptography;
using MySqlConnector;

namespace BlazorStoc.Services;

// Subtask 2.x (Task 2): rewritten against the REAL, already-populated MariaDB schema
// (Livrare-DDL-MariaDB\schema-mariadb.sql) - table `project_observation_files`, columns
// (id,observation_id,relative_path,original_name,content_type,byte_length,sha256,author,uploaded_utc). The
// legacy `project_observation_file`/`id_observation`/`id_file` table created lazily by the old MariDB module is
// gone; no CREATE/ALTER is ever issued here (the runtime account has no DDL rights). Every "_utc" column is TEXT,
// never a native DATETIME - read/write it exclusively through MariaTimeText (see that file for why).
public sealed class MariaProjectFileStore(IWebHostEnvironment environment, IConfiguration configuration,
    IAccessControl? accessControl = null, IArchiveService? archiveService = null, IAuditTrail? auditTrail = null) : IProjectFileStore
{
    private readonly IArchiveService archiver = archiveService ?? new ArchiveService(accessControl);
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string rootPath = RootPath(environment, configuration);
    private readonly string archivePath = ArchivePath(environment, configuration);

    // Directories come from the shared MariaAssetPaths helper (Services/MariaTimeText.cs) - same archive directory
    // as FileProductImageStore, so the two MariaDB-mode file stores never disagree about where archived files
    // live. Never falls back to a SQLite-mode directory (App:ProjectFilesPath / App:ArchiveFilesPath).
    public static string RootPath(IWebHostEnvironment environment, IConfiguration configuration) => MariaAssetPaths.ProjectFiles(configuration);

    public static string ArchivePath(IWebHostEnvironment environment, IConfiguration configuration) => MariaAssetPaths.ArchiveFiles(configuration);

    private MySqlConnection CreateConnection() => DatabaseConnections.Create(configuration);

    public async Task<IReadOnlyList<ProjectObservationFile>> GetFilesAsync(int observationId, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, null, """
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
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, null,
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
            Directory.CreateDirectory(rootPath);
            var destination = Resolve(prepared.StoredName);
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using (var count = Command(connection, null,
                "SELECT COUNT(*) FROM project_observation_files WHERE observation_id=@observation", ("@observation", observationId)))
                if (Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) >= ProjectFileRules.MaximumFilesPerObservation)
                    throw new ProjectOperationException($"O observație poate avea cel mult {ProjectFileRules.MaximumFilesPerObservation} de fișiere.");
            await File.WriteAllBytesAsync(destination, content, cancellationToken).ConfigureAwait(false);
            try
            {
                await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
                string observationName;
                int id;
                try
                {
                    await using (var lookup = Command(connection, transaction,
                        "SELECT name FROM project_observations WHERE id=@observation FOR UPDATE", ("@observation", observationId)))
                        observationName = await lookup.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string
                            ?? throw new ProjectOperationException("Observația nu mai există. Actualizează pagina înainte să reîncerci încărcarea.");
                    await using var insert = Command(connection, transaction, """
                        INSERT INTO project_observation_files
                            (observation_id,relative_path,original_name,content_type,byte_length,sha256,author,uploaded_utc)
                        VALUES(@observation,@path,@name,@contentType,@length,@hash,@author,@uploaded)
                        """, ("@observation", observationId), ("@path", prepared.StoredName), ("@name", prepared.OriginalName),
                        ("@contentType", contentType), ("@length", prepared.SizeBytes), ("@hash", hash), ("@author", prepared.Author),
                        ("@uploaded", MariaTimeText.Format(prepared.UploadedAtUtc)));
                    await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    id = checked((int)insert.LastInsertedId);
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                    throw;
                }
                var saved = prepared with { Id = id };
                await AuditRecorder.RecordCreateAsync(auditTrail, accessControl, AuditEntities.ProjectObservationFile, saved.Id.ToString(),
                    saved.OriginalName, AuditDetails.Identification(("Nume fișier", saved.OriginalName), ("Observație", observationName),
                        ("Tip", contentType), ("Dimensiune (octeți)", saved.SizeBytes.ToString()), ("Autor", saved.Author)),
                    cancellationToken).ConfigureAwait(false);
                return saved;
            }
            catch (MySqlException exception) when (exception.ErrorCode is MySqlErrorCode.NoReferencedRow or MySqlErrorCode.NoReferencedRow2
                or MySqlErrorCode.RowIsReferenced or MySqlErrorCode.RowIsReferenced2)
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
        await using var lookupConnection = CreateConnection();
        await lookupConnection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var lookup = Command(lookupConnection, null, """
            SELECT id,observation_id,original_name,relative_path,content_type,byte_length,sha256,author,uploaded_utc
            FROM project_observation_files WHERE id=@id
            """, ("@id", fileId));
        ProjectObservationFile? file;
        await using (var reader = await lookup.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            file = await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? Read(reader) : null;
        if (file is null) throw new ProjectOperationException("Fișierul a fost eliminat deja. Actualizează pagina.");

        await archiver.ExecuteAsync(ArchiveRequests.ProjectObservationFile(file, motif), async (operation, token) =>
        {
            await gate.WaitAsync(token).ConfigureAwait(false);
            ArchiveFileRecord? prepared = null;
            try
            {
                prepared = await ArchiveFileSafety.PrepareAsync(rootPath, archivePath,
                    file.StoredName, operation, AuditEntities.ProjectObservationFile, file.Id.ToString(),
                    file.ContentType, file.OriginalName, file.SizeBytes, token).ConfigureAwait(false);
                await using var connection = CreateConnection();
                await connection.OpenAsync(token).ConfigureAwait(false);
                await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, token).ConfigureAwait(false);
                try
                {
                    await MariaArchivePersistence.InsertAsync(connection, transaction, operation, [prepared], token).ConfigureAwait(false);
                    await using var delete = Command(connection, transaction, "DELETE FROM project_observation_files WHERE id=@id", ("@id", file.Id));
                    if (await delete.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
                        throw new ProjectOperationException("Fișierul a fost eliminat deja. Actualizează pagina.");
                    await MariaArchivePersistence.InsertAuditAsync(connection, transaction, operation, token).ConfigureAwait(false);
                    await transaction.CommitAsync(token).ConfigureAwait(false);
                }
                catch
                {
                    await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                    throw;
                }
                await ArchiveFileSafety.CompleteAsync(rootPath, archivePath, prepared, token).ConfigureAwait(false);
            }
            catch
            {
                ArchiveFileSafety.Rollback(archivePath, prepared);
                throw;
            }
            finally { gate.Release(); }
        }, cancellationToken).ConfigureAwait(false);
    }

    private string Resolve(string relativePath)
    {
        var root = rootPath + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(rootPath, relativePath));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new ProjectOperationException("Calea fișierului nu este validă.");
        return path;
    }

    private static ProjectObservationFile Read(MySqlDataReader reader) => new(reader.GetInt32(0), reader.GetInt32(1),
        reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetInt64(5), reader.GetString(6),
        reader.GetString(7), MariaTimeText.Parse(reader.GetString(8)));

    private static MySqlCommand Command(MySqlConnection connection, MySqlTransaction? transaction, string sql,
        params (string Name, object Value)[] parameters)
    {
        var command = new MySqlCommand(sql, connection, transaction);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return command;
    }
}
