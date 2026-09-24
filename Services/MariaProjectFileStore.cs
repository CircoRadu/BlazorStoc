using System.Security.Cryptography;
using MySqlConnector;

namespace BlazorStoc.Services;

public sealed class MariaProjectFileStore(IWebHostEnvironment environment, IConfiguration configuration,
    IAccessControl? accessControl = null, IArchiveService? archiveService = null) : IProjectFileStore
{
    private readonly IArchiveService archiver = archiveService ?? new ArchiveService(accessControl);
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string rootPath = RootPath(environment, configuration);
    private readonly string archivePath = ArchivePath(environment, configuration);

    public static string RootPath(IWebHostEnvironment environment, IConfiguration configuration) =>
        Path.GetFullPath(configuration["App:ProjectFilesPath"] ?? Path.Combine(environment.ContentRootPath, "data", "project-files"));
    public static string ArchivePath(IWebHostEnvironment environment, IConfiguration configuration) =>
        Path.GetFullPath(configuration["App:ArchiveFilesPath"] ?? Path.Combine(environment.ContentRootPath, "data", "archive", "files"));

    private MySqlConnection CreateConnection() => DatabaseConnections.Create(configuration);

    public async Task<IReadOnlyList<ProjectObservationFile>> GetFilesAsync(int observationId, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("""
            SELECT id_file,id_observation,original_name,relative_path,content_type,byte_length,sha256,author,uploaded_utc
            FROM project_observation_file WHERE id_observation=@observation ORDER BY uploaded_utc,id_file
            """, connection);
        command.Parameters.AddWithValue("@observation", observationId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<ProjectObservationFile>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(Read(reader));
        return result;
    }

    public async Task<ProjectFileContent?> GetContentAsync(int fileId, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand(
            "SELECT relative_path,content_type,original_name FROM project_observation_file WHERE id_file=@id", connection);
        command.Parameters.AddWithValue("@id", fileId);
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
            await using (var count = new MySqlCommand("SELECT COUNT(*) FROM project_observation_file WHERE id_observation=@observation", connection))
            {
                count.Parameters.AddWithValue("@observation", observationId);
                if (Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) >= ProjectFileRules.MaximumFilesPerObservation)
                    throw new ProjectOperationException($"O observație poate avea cel mult {ProjectFileRules.MaximumFilesPerObservation} de fișiere.");
            }
            await File.WriteAllBytesAsync(destination, content, cancellationToken).ConfigureAwait(false);
            try
            {
                await using var insert = new MySqlCommand("""
                    INSERT INTO project_observation_file
                        (id_observation,relative_path,original_name,content_type,byte_length,sha256,author,uploaded_utc)
                    VALUES(@observation,@path,@name,@contentType,@length,@hash,@author,@uploaded)
                    """, connection);
                insert.Parameters.AddWithValue("@observation", observationId);
                insert.Parameters.AddWithValue("@path", prepared.StoredName);
                insert.Parameters.AddWithValue("@name", prepared.OriginalName);
                insert.Parameters.AddWithValue("@contentType", contentType);
                insert.Parameters.AddWithValue("@length", prepared.SizeBytes);
                insert.Parameters.AddWithValue("@hash", hash);
                insert.Parameters.AddWithValue("@author", prepared.Author);
                insert.Parameters.AddWithValue("@uploaded", prepared.UploadedAtUtc);
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                return prepared with { Id = checked((int)insert.LastInsertedId) };
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
        await using var lookup = new MySqlCommand("""
            SELECT id_file,id_observation,original_name,relative_path,content_type,byte_length,sha256,author,uploaded_utc
            FROM project_observation_file WHERE id_file=@id
            """, lookupConnection);
        lookup.Parameters.AddWithValue("@id", fileId);
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
                await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, token).ConfigureAwait(false);
                try
                {
                    await MariaArchivePersistence.InsertAsync(connection, transaction, operation, [prepared], token).ConfigureAwait(false);
                    await using var delete = new MySqlCommand("DELETE FROM project_observation_file WHERE id_file=@id", connection, transaction);
                    delete.Parameters.AddWithValue("@id", file.Id);
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
        reader.GetString(7), DateTime.SpecifyKind(reader.GetDateTime(8), DateTimeKind.Utc));
}
