using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using MySqlConnector;

namespace BlazorStoc.Services;

public sealed record ArchiveFileRecord(
    string RelationType,
    string OriginalRelationId,
    string LiveRelativePath,
    string ArchiveRelativePath,
    string ContentType,
    string FileName,
    long ByteLength,
    string ContentHash,
    DateTime ArchivedUtc);

internal static class ArchiveJson
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    internal static string? ProtectedData(ArchiveSnapshot snapshot) => snapshot.ProtectedValues.Count == 0
        ? null
        : JsonSerializer.Serialize(snapshot.ProtectedValues.ToDictionary(value => value.Name, value => value.Hash,
            StringComparer.Ordinal), Options);

    internal static T Entity<T>(ArchiveSnapshot snapshot) =>
        JsonSerializer.Deserialize<T>(snapshot.DataJson, Options)
        ?? throw new ArchiveContractException("Datele obiectului arhivat nu pot fi reconstruite.");
}

internal static class SqliteArchivePersistence
{
    public static async Task InsertAsync(SqliteConnection connection, SqliteTransaction transaction,
        ArchiveOperation operation, IReadOnlyList<ArchiveFileRecord> files, CancellationToken token)
    {
        var request = operation.Request;
        var snapshot = request.Snapshot;
        await using (var command = SqliteLocalStore.Command(connection, transaction, """
            INSERT INTO archive_operations
                (id,entity_type,original_id,original_version,deleted_utc,actor_username,actor_role,motif,target,details,data_json,protected_data_json)
            VALUES(@id,@entity,@originalId,@version,@deleted,@actor,@role,@motif,@target,@details,@data,@protected)
            """, ("@id", operation.Id.ToString("D")), ("@entity", snapshot.EntityType),
            ("@originalId", snapshot.OriginalId), ("@version", snapshot.Version),
            ("@deleted", operation.TimestampUtc.ToString("O")), ("@actor", operation.ActorUsername),
            ("@role", operation.ActorRole), ("@motif", request.Motif), ("@target", request.Target),
            ("@details", request.Details), ("@data", snapshot.DataJson),
            ("@protected", ArchiveJson.ProtectedData(snapshot))))
            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);

        await InsertTypedEntityAsync(connection, transaction, operation, token).ConfigureAwait(false);
        foreach (var relation in snapshot.Relations)
        {
            await using var command = SqliteLocalStore.Command(connection, transaction, """
                INSERT INTO archive_relations(archive_id,relation_type,original_relation_id,data_json)
                VALUES(@archiveId,@type,@originalId,@data)
                """, ("@archiveId", operation.Id.ToString("D")), ("@type", relation.RelationType),
                ("@originalId", relation.RelationId), ("@data", relation.DataJson));
            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }

        foreach (var file in files)
        {
            await using var command = SqliteLocalStore.Command(connection, transaction, """
                INSERT INTO archive_files
                    (archive_id,relation_type,original_relation_id,live_relative_path,archive_relative_path,
                     content_type,file_name,byte_length,content_hash,archived_utc)
                VALUES(@archiveId,@type,@originalId,@livePath,@archivePath,@contentType,@fileName,@length,@hash,@archived)
                """, ("@archiveId", operation.Id.ToString("D")), ("@type", file.RelationType),
                ("@originalId", file.OriginalRelationId), ("@livePath", file.LiveRelativePath),
                ("@archivePath", file.ArchiveRelativePath), ("@contentType", file.ContentType),
                ("@fileName", file.FileName), ("@length", file.ByteLength), ("@hash", file.ContentHash),
                ("@archived", file.ArchivedUtc.ToString("O")));
            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }
    }

    private static async Task InsertTypedEntityAsync(SqliteConnection connection, SqliteTransaction transaction,
        ArchiveOperation operation, CancellationToken token)
    {
        var snapshot = operation.Request.Snapshot;
        SqliteCommand command;
        switch (snapshot.EntityType)
        {
            case AuditEntities.Product:
                var product = ArchiveJson.Entity<Product>(snapshot);
                command = SqliteLocalStore.Command(connection, transaction, """
                    INSERT INTO archive_products
                        (archive_id,original_id,category,subcategory,name,description,quantity,version)
                    VALUES(@archiveId,@id,@category,@subcategory,@name,@description,@quantity,@version)
                    """, ("@archiveId", operation.Id.ToString("D")), ("@id", product.Id),
                    ("@category", product.Category), ("@subcategory", product.Subcategory), ("@name", product.Name),
                    ("@description", product.Description), ("@quantity", product.Quantity), ("@version", product.Version));
                break;
            case AuditEntities.Beneficiary:
                var beneficiary = ArchiveJson.Entity<Beneficiary>(snapshot);
                command = SqliteLocalStore.Command(connection, transaction, """
                    INSERT INTO archive_beneficiaries(archive_id,original_id,name,cui,version)
                    VALUES(@archiveId,@id,@name,@cui,@version)
                    """, ("@archiveId", operation.Id.ToString("D")), ("@id", beneficiary.Id),
                    ("@name", beneficiary.Name), ("@cui", beneficiary.Cui), ("@version", beneficiary.Version));
                break;
            case AuditEntities.User:
                var user = ArchiveJson.Entity<WebUser>(snapshot);
                var passwordHash = snapshot.ProtectedValues.SingleOrDefault(value =>
                    string.Equals(value.Name, "PasswordHash", StringComparison.Ordinal))?.Hash
                    ?? throw new ArchiveContractException("Hash-ul parolei lipsește din arhiva protejată a utilizatorului.");
                command = SqliteLocalStore.Command(connection, transaction, """
                    INSERT INTO archive_web_users
                        (archive_id,original_id,username,display_name,role,is_active,version,password_hash)
                    VALUES(@archiveId,@id,@username,@displayName,@role,@active,@version,@passwordHash)
                    """, ("@archiveId", operation.Id.ToString("D")), ("@id", user.Id),
                    ("@username", user.Username), ("@displayName", user.DisplayName), ("@role", user.Role),
                    ("@active", user.IsActive ? 1 : 0), ("@version", user.Version), ("@passwordHash", passwordHash));
                break;
            case AuditEntities.Project:
                var project = ArchiveJson.Entity<Project>(snapshot);
                command = SqliteLocalStore.Command(connection, transaction, """
                    INSERT INTO archive_projects(archive_id,original_id,beneficiary_id,name,observations,version)
                    VALUES(@archiveId,@id,@beneficiary,@name,@observations,@version)
                    """, ("@archiveId", operation.Id.ToString("D")), ("@id", project.Id),
                    ("@beneficiary", project.BeneficiaryId), ("@name", project.Name),
                    ("@observations", project.Observations), ("@version", project.Version));
                break;
            case AuditEntities.ProjectObservation:
                var observation = ArchiveJson.Entity<ProjectObservation>(snapshot);
                command = SqliteLocalStore.Command(connection, transaction, """
                    INSERT INTO archive_project_observations(archive_id,original_id,project_id,name,content,author,version)
                    VALUES(@archiveId,@id,@project,@name,@content,@author,@version)
                    """, ("@archiveId", operation.Id.ToString("D")), ("@id", observation.Id),
                    ("@project", observation.ProjectId), ("@name", observation.Name), ("@content", observation.Content),
                    ("@author", observation.Author), ("@version", observation.Version));
                break;
            case AuditEntities.ProjectObservationFile:
                var observationFile = ArchiveJson.Entity<ProjectObservationFile>(snapshot);
                command = SqliteLocalStore.Command(connection, transaction, """
                    INSERT INTO archive_project_observation_files
                        (archive_id,original_id,observation_id,original_name,content_type,byte_length,sha256,author,uploaded_utc)
                    VALUES(@archiveId,@id,@observation,@name,@contentType,@length,@hash,@author,@uploaded)
                    """, ("@archiveId", operation.Id.ToString("D")), ("@id", observationFile.Id),
                    ("@observation", observationFile.ObservationId), ("@name", observationFile.OriginalName),
                    ("@contentType", observationFile.ContentType), ("@length", observationFile.SizeBytes),
                    ("@hash", observationFile.Sha256), ("@author", observationFile.Author),
                    ("@uploaded", observationFile.UploadedAtUtc.ToString("O")));
                break;
            default:
                throw new ArchiveContractException(
                    $"Tipul «{snapshot.EntityType}» este înregistrat, dar nu are mapare SQLite pentru tabela sa archive_*.");
        }
        await using (command) await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }
}

internal static class MariaArchivePersistence
{
    public static async Task InsertAsync(MySqlConnection connection, MySqlTransaction transaction,
        ArchiveOperation operation, IReadOnlyList<ArchiveFileRecord> files, CancellationToken token)
    {
        var request = operation.Request;
        var snapshot = request.Snapshot;
        await ExecuteAsync(connection, transaction, """
            INSERT INTO archive_operations
                (id,entity_type,original_id,original_version,deleted_utc,actor_username,actor_role,motif,target,details,data_json,protected_data_json)
            VALUES(@id,@entity,@originalId,@version,@deleted,@actor,@role,@motif,@target,@details,@data,@protected)
            """, token, ("@id", operation.Id.ToString("D")), ("@entity", snapshot.EntityType),
            ("@originalId", snapshot.OriginalId), ("@version", snapshot.Version), ("@deleted", operation.TimestampUtc),
            ("@actor", operation.ActorUsername), ("@role", operation.ActorRole), ("@motif", request.Motif),
            ("@target", request.Target), ("@details", request.Details), ("@data", snapshot.DataJson),
            ("@protected", ArchiveJson.ProtectedData(snapshot))).ConfigureAwait(false);

        await InsertTypedEntityAsync(connection, transaction, operation, token).ConfigureAwait(false);
        foreach (var relation in snapshot.Relations)
            await ExecuteAsync(connection, transaction, """
                INSERT INTO archive_relations(archive_id,relation_type,original_relation_id,data_json)
                VALUES(@archiveId,@type,@originalId,@data)
                """, token, ("@archiveId", operation.Id.ToString("D")), ("@type", relation.RelationType),
                ("@originalId", relation.RelationId), ("@data", relation.DataJson)).ConfigureAwait(false);

        foreach (var file in files)
            await ExecuteAsync(connection, transaction, """
                INSERT INTO archive_files
                    (archive_id,relation_type,original_relation_id,live_relative_path,archive_relative_path,
                     content_type,file_name,byte_length,content_hash,archived_utc)
                VALUES(@archiveId,@type,@originalId,@livePath,@archivePath,@contentType,@fileName,@length,@hash,@archived)
                """, token, ("@archiveId", operation.Id.ToString("D")), ("@type", file.RelationType),
                ("@originalId", file.OriginalRelationId), ("@livePath", file.LiveRelativePath),
                ("@archivePath", file.ArchiveRelativePath), ("@contentType", file.ContentType),
                ("@fileName", file.FileName), ("@length", file.ByteLength), ("@hash", file.ContentHash),
                ("@archived", file.ArchivedUtc)).ConfigureAwait(false);
    }

    public static Task InsertAuditAsync(MySqlConnection connection, MySqlTransaction transaction,
        ArchiveOperation operation, CancellationToken token) => ExecuteAsync(connection, transaction, """
            INSERT INTO audit_events
                (id,timestamp_utc,actor_username,actor_role,entity_type,action,target,details,motif,entity_id,archive_operation_id)
            VALUES(@id,@timestamp,@actor,@role,@entity,@action,@target,@details,@motif,@entityId,@archiveOperationId)
            """, token, ("@id", Guid.NewGuid().ToString("D")), ("@timestamp", operation.TimestampUtc),
            ("@actor", operation.ActorUsername), ("@role", operation.ActorRole),
            ("@entity", operation.Request.Snapshot.EntityType), ("@action", AuditActions.Delete),
            ("@target", operation.Request.Target), ("@details", operation.Request.Details),
            ("@motif", operation.Request.Motif), ("@entityId", operation.Request.Snapshot.OriginalId),
            ("@archiveOperationId", operation.Id.ToString("D")));

    private static async Task InsertTypedEntityAsync(MySqlConnection connection, MySqlTransaction transaction,
        ArchiveOperation operation, CancellationToken token)
    {
        var snapshot = operation.Request.Snapshot;
        switch (snapshot.EntityType)
        {
            case AuditEntities.Product:
                var product = ArchiveJson.Entity<Product>(snapshot);
                await ExecuteAsync(connection, transaction, """
                    INSERT INTO archive_products
                        (archive_id,original_id,category,subcategory,name,description,quantity,version)
                    VALUES(@archiveId,@id,@category,@subcategory,@name,@description,@quantity,@version)
                    """, token, ("@archiveId", operation.Id.ToString("D")), ("@id", product.Id),
                    ("@category", product.Category), ("@subcategory", product.Subcategory), ("@name", product.Name),
                    ("@description", product.Description), ("@quantity", product.Quantity), ("@version", product.Version)).ConfigureAwait(false);
                break;
            case AuditEntities.Beneficiary:
                var beneficiary = ArchiveJson.Entity<Beneficiary>(snapshot);
                await ExecuteAsync(connection, transaction, """
                    INSERT INTO archive_beneficiaries(archive_id,original_id,name,cui,version)
                    VALUES(@archiveId,@id,@name,@cui,@version)
                    """, token, ("@archiveId", operation.Id.ToString("D")), ("@id", beneficiary.Id),
                    ("@name", beneficiary.Name), ("@cui", beneficiary.Cui), ("@version", beneficiary.Version)).ConfigureAwait(false);
                break;
            case AuditEntities.User:
                var user = ArchiveJson.Entity<WebUser>(snapshot);
                var passwordHash = snapshot.ProtectedValues.SingleOrDefault(value =>
                    string.Equals(value.Name, "PasswordHash", StringComparison.Ordinal))?.Hash
                    ?? throw new ArchiveContractException("Hash-ul parolei lipsește din arhiva protejată a utilizatorului.");
                await ExecuteAsync(connection, transaction, """
                    INSERT INTO archive_web_users
                        (archive_id,original_id,username,display_name,role,is_active,version,password_hash)
                    VALUES(@archiveId,@id,@username,@displayName,@role,@active,@version,@passwordHash)
                    """, token, ("@archiveId", operation.Id.ToString("D")), ("@id", user.Id),
                    ("@username", user.Username), ("@displayName", user.DisplayName), ("@role", user.Role),
                    ("@active", user.IsActive), ("@version", user.Version), ("@passwordHash", passwordHash)).ConfigureAwait(false);
                break;
            case AuditEntities.Project:
                var project = ArchiveJson.Entity<Project>(snapshot);
                await ExecuteAsync(connection, transaction, """
                    INSERT INTO archive_projects(archive_id,original_id,beneficiary_id,name,observations,version)
                    VALUES(@archiveId,@id,@beneficiary,@name,@observations,@version)
                    """, token, ("@archiveId", operation.Id.ToString("D")), ("@id", project.Id),
                    ("@beneficiary", project.BeneficiaryId), ("@name", project.Name),
                    ("@observations", project.Observations), ("@version", project.Version)).ConfigureAwait(false);
                break;
            case AuditEntities.ProjectObservation:
                var observation = ArchiveJson.Entity<ProjectObservation>(snapshot);
                await ExecuteAsync(connection, transaction, """
                    INSERT INTO archive_project_observations(archive_id,original_id,project_id,name,content,author,version)
                    VALUES(@archiveId,@id,@project,@name,@content,@author,@version)
                    """, token, ("@archiveId", operation.Id.ToString("D")), ("@id", observation.Id),
                    ("@project", observation.ProjectId), ("@name", observation.Name), ("@content", observation.Content),
                    ("@author", observation.Author), ("@version", observation.Version)).ConfigureAwait(false);
                break;
            case AuditEntities.ProjectObservationFile:
                var observationFile = ArchiveJson.Entity<ProjectObservationFile>(snapshot);
                await ExecuteAsync(connection, transaction, """
                    INSERT INTO archive_project_observation_files
                        (archive_id,original_id,observation_id,original_name,content_type,byte_length,sha256,author,uploaded_utc)
                    VALUES(@archiveId,@id,@observation,@name,@contentType,@length,@hash,@author,@uploaded)
                    """, token, ("@archiveId", operation.Id.ToString("D")), ("@id", observationFile.Id),
                    ("@observation", observationFile.ObservationId), ("@name", observationFile.OriginalName),
                    ("@contentType", observationFile.ContentType), ("@length", observationFile.SizeBytes),
                    ("@hash", observationFile.Sha256), ("@author", observationFile.Author),
                    ("@uploaded", observationFile.UploadedAtUtc)).ConfigureAwait(false);
                break;
            default:
                throw new ArchiveContractException(
                    $"Tipul «{snapshot.EntityType}» este înregistrat, dar nu are mapare MariaDB pentru tabela sa archive_*.");
        }
    }

    private static async Task ExecuteAsync(MySqlConnection connection, MySqlTransaction transaction, string sql,
        CancellationToken token, params (string Name, object? Value)[] parameters)
    {
        await using var command = new MySqlCommand(sql, connection, transaction);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }
}

internal static class ArchiveFileSafety
{
    private sealed record CleanupMarker(string LiveRelativePath);

    public static async Task<ArchiveFileRecord> PrepareAsync(string liveRoot, string archiveRoot,
        string liveRelativePath, ArchiveOperation operation, string relationType, string relationId,
        string contentType, string fileName, long expectedLength, CancellationToken token)
    {
        var livePath = ResolveRelative(liveRoot, liveRelativePath);
        if (!File.Exists(livePath)) throw new ArchiveContractException("Fișierul asociat nu mai există în zona live.");
        var safeFileName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeFileName)) throw new ArchiveContractException("Numele fișierului asociat nu este valid.");
        var entityFolder = string.Concat(operation.Request.Snapshot.EntityType.Select(character =>
            char.IsLetterOrDigit(character) ? character : '_'));
        var archiveRelativePath = Path.Combine(entityFolder, operation.Request.Snapshot.OriginalId,
            operation.Id.ToString("D"), safeFileName);
        var archivePath = ResolveRelative(archiveRoot, archiveRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);
        await RetryPendingCleanupAsync(liveRoot, archiveRoot, token).ConfigureAwait(false);
        try
        {
            await using (var source = new FileStream(livePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                             81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var destination = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
                await source.CopyToAsync(destination, token).ConfigureAwait(false);

            var sourceInfo = new FileInfo(livePath);
            var archiveInfo = new FileInfo(archivePath);
            if (sourceInfo.Length != expectedLength || archiveInfo.Length != expectedLength)
                throw new ArchiveContractException("Dimensiunea copiei arhivate nu corespunde fișierului live.");
            var sourceHash = await HashAsync(livePath, token).ConfigureAwait(false);
            var archiveHash = await HashAsync(archivePath, token).ConfigureAwait(false);
            if (!CryptographicOperations.FixedTimeEquals(sourceHash, archiveHash))
                throw new ArchiveContractException("Hash-ul copiei arhivate nu corespunde fișierului live.");
            return new(relationType, relationId, NormalizeRelative(liveRelativePath),
                NormalizeRelative(archiveRelativePath), contentType, safeFileName, expectedLength,
                Convert.ToHexString(sourceHash), operation.TimestampUtc);
        }
        catch
        {
            if (File.Exists(archivePath)) File.Delete(archivePath);
            throw;
        }
    }

    public static void Rollback(string archiveRoot, ArchiveFileRecord? file)
    {
        if (file is null) return;
        var path = ResolveRelative(archiveRoot, file.ArchiveRelativePath);
        if (File.Exists(path)) File.Delete(path);
    }

    public static async Task CompleteAsync(string liveRoot, string archiveRoot, ArchiveFileRecord? file,
        CancellationToken token)
    {
        if (file is null) return;
        var livePath = ResolveRelative(liveRoot, file.LiveRelativePath);
        try
        {
            if (File.Exists(livePath)) File.Delete(livePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            var archivePath = ResolveRelative(archiveRoot, file.ArchiveRelativePath);
            var marker = archivePath + ".cleanup-pending.json";
            await File.WriteAllTextAsync(marker, JsonSerializer.Serialize(new CleanupMarker(file.LiveRelativePath),
                ArchiveJson.Options), token).ConfigureAwait(false);
        }
    }

    private static async Task RetryPendingCleanupAsync(string liveRoot, string archiveRoot, CancellationToken token)
    {
        if (!Directory.Exists(archiveRoot)) return;
        foreach (var markerPath in Directory.EnumerateFiles(archiveRoot, "*.cleanup-pending.json", SearchOption.AllDirectories))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var marker = JsonSerializer.Deserialize<CleanupMarker>(
                    await File.ReadAllTextAsync(markerPath, token).ConfigureAwait(false), ArchiveJson.Options);
                if (marker is null) continue;
                var livePath = ResolveRelative(liveRoot, marker.LiveRelativePath);
                if (File.Exists(livePath)) File.Delete(livePath);
                File.Delete(markerPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                // Marker remains durable for the next archive attempt.
            }
        }
    }

    private static async Task<byte[]> HashAsync(string path, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await SHA256.HashDataAsync(stream, token).ConfigureAwait(false);
    }

    private static string ResolveRelative(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            throw new ArchiveContractException("Calea fișierului asociat trebuie să fie relativă.");
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                             + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath));
        if (!path.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            throw new ArchiveContractException("Calea fișierului asociat iese din directorul controlat.");
        return path;
    }

    private static string NormalizeRelative(string value) => value.Replace(Path.DirectorySeparatorChar, '/');
}
