using System.Data;
using MySqlConnector;

namespace BlazorStoc.Services;

// Rows of `entity_files` (migration 58) and files under the entity-files asset directory. Every "_utc" column is text: read
// and written only through MariaTimeText. The owner rows are not linked by a foreign key (four kinds of owner share the table).
public sealed class MariaEntityFileStore(IConfiguration configuration, IAccessControl? accessControl = null,
    IAuditTrail? auditTrail = null) : IEntityFileStore
{
    private const string Columns = "id, owner_kind, owner_id, relative_path, original_name, content_type, byte_length, sha256, uploaded_by, uploaded_utc";
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string rootPath = MariaAssetPaths.EntityFiles(configuration);

    // Table, display-name column and permissions of each kind of owner.
    private static (string Table, string NameSql, string EditPermission, string ViewPermission, string BeneficiarySql) Owner(string kind) => kind switch
    {
        EntityFileOwners.WorkPoint => ("beneficiary_work_points", "name", "mentenanta.edit", "mentenanta.view", "beneficiary_id"),
        EntityFileOwners.Contract => ("service_contracts", "contract_number", "mentenanta.edit", "mentenanta.view", "beneficiary_id"),
        EntityFileOwners.Intervention => ("service_interventions", "work_point_name", "mentenanta.edit", "mentenanta.view", "beneficiary_id"),
        EntityFileOwners.Beneficiary => ("beneficiaries", "name", "beneficiari.edit", "beneficiari.view", "id"),
        EntityFileOwners.Risk => ("risk_analyses", "registration_number", "analize-risc.edit", "analize-risc.view", "beneficiary_id"),
        _ => throw new WorkPointOperationException("Tipul obiectului nu este valid.")
    };

    public async Task<IReadOnlyList<EntityFile>> GetAsync(string ownerKind, int ownerId, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAsync(Owner(ownerKind).ViewPermission, cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        return await ReadAsync(connection, "owner_kind=@kind AND owner_id=@id", cancellationToken, ("@kind", ownerKind), ("@id", ownerId)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<BeneficiaryFileItem>> GetForBeneficiaryAsync(int beneficiaryId, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAsync("beneficiari.view", cancellationToken).ConfigureAwait(false);
        var result = new List<BeneficiaryFileItem>();
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        foreach (var kind in new[] { EntityFileOwners.Beneficiary, EntityFileOwners.WorkPoint, EntityFileOwners.Contract, EntityFileOwners.Intervention, EntityFileOwners.Risk })
        {
            var owner = Owner(kind);
            // A kind the user may not see (maintenance, risk analyses) is left out of the list.
            if (accessControl is not null && !await accessControl.HasAsync(owner.ViewPermission, cancellationToken).ConfigureAwait(false)) continue;
            var names = new Dictionary<int, string>();
            await using (var lookup = new MySqlCommand($"SELECT id, {owner.NameSql} FROM {owner.Table} WHERE {owner.BeneficiarySql}=@id", connection))
            {
                lookup.Parameters.AddWithValue("@id", beneficiaryId);
                await using var reader = await lookup.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) names[checked((int)reader.GetInt64(0))] = reader.GetString(1);
            }
            if (names.Count == 0) continue;
            var files = await ReadAsync(connection, $"owner_kind=@kind AND owner_id IN ({string.Join(',', names.Keys)})", cancellationToken, ("@kind", kind)).ConfigureAwait(false);
            result.AddRange(files.Select(file => new BeneficiaryFileItem(file, kind == EntityFileOwners.Beneficiary ? "" : names[file.OwnerId])));
        }
        return [.. result.OrderBy(item => item.File.UploadedUtc).ThenBy(item => item.File.Id)];
    }

    public async Task<EntityFileContent?> GetContentAsync(int fileId, CancellationToken cancellationToken = default)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        var file = (await ReadAsync(connection, "id=@id", cancellationToken, ("@id", fileId)).ConfigureAwait(false)).FirstOrDefault();
        if (file is null) return null;
        if (accessControl is not null) await accessControl.EnsureAsync(Owner(file.OwnerKind).ViewPermission, cancellationToken).ConfigureAwait(false);
        var path = Resolve(file.StoredName);
        return File.Exists(path) ? new(await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false), file.ContentType, file.OriginalName) : null;
    }

    public async Task<EntityFile> AddAsync(string ownerKind, int ownerId, string originalName, string declaredContentType, byte[] content, CancellationToken cancellationToken = default)
    {
        var owner = Owner(ownerKind);
        if (accessControl is not null) await accessControl.EnsureAsync(owner.EditPermission, cancellationToken).ConfigureAwait(false);
        if (!MariaDatabaseGuard.IsAllowedDatabase(configuration))
            throw new WorkPointOperationException("Modificările sunt permise numai în baza BlazorStoc.");
        var contentType = EntityFileRules.DetectContentType(content, declaredContentType, originalName);
        var displayName = EntityFileRules.SafeOriginalName(originalName);
        var hash = ServicePhotoRules.Hash(content);
        var author = (accessControl is null ? null : await accessControl.GetUsernameAsync(cancellationToken).ConfigureAwait(false)) ?? "necunoscut";
        var storedName = ProjectFileRules.NewStoredName(Path.GetExtension(displayName));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(rootPath);
            var destination = Resolve(storedName);
            await File.WriteAllBytesAsync(destination, content, cancellationToken).ConfigureAwait(false);
            try
            {
                await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
                await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
                EntityFile saved; string ownerName; int beneficiaryId;
                try
                {
                    // The owner row is locked, so two uploads to the same object are counted one after the other.
                    await using (var lookup = new MySqlCommand($"SELECT {owner.NameSql}, {owner.BeneficiarySql} FROM {owner.Table} WHERE id=@id FOR UPDATE", connection, transaction))
                    {
                        lookup.Parameters.AddWithValue("@id", ownerId);
                        await using var reader = await lookup.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                            throw new WorkPointOperationException("Obiectul nu mai există. Actualizează pagina înainte să reîncerci încărcarea.");
                        (ownerName, beneficiaryId) = (reader.GetString(0), checked((int)reader.GetInt64(1)));
                    }
                    await using (var count = new MySqlCommand("SELECT COUNT(*) FROM entity_files WHERE owner_kind=@kind AND owner_id=@id", connection, transaction))
                    {
                        count.Parameters.AddWithValue("@kind", ownerKind);
                        count.Parameters.AddWithValue("@id", ownerId);
                        if (Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) >= EntityFileRules.MaximumPerOwner)
                            throw EntityFileRules.LimitReached();
                    }
                    var uploaded = MariaTimeText.Now();
                    await using var insert = new MySqlCommand("""
                        INSERT INTO entity_files (owner_kind, owner_id, relative_path, original_name, content_type, byte_length, sha256, uploaded_by, uploaded_utc)
                        VALUES (@kind, @owner, @path, @name, @type, @length, @hash, @author, @uploaded)
                        """, connection, transaction);
                    foreach (var (name, value) in new (string, object)[] { ("@kind", ownerKind), ("@owner", ownerId), ("@path", storedName), ("@name", displayName),
                                 ("@type", contentType), ("@length", content.LongLength), ("@hash", hash), ("@author", author), ("@uploaded", MariaTimeText.Format(uploaded)) })
                        insert.Parameters.AddWithValue(name, value);
                    try { await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false); }
                    catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry) { throw EntityFileRules.Duplicate(); }
                    saved = new(checked((int)insert.LastInsertedId), ownerKind, ownerId, displayName, storedName, contentType, content.LongLength, hash, author, uploaded);
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                    throw;
                }
                await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.Beneficiary, AuditActions.AddAttachedFile, beneficiaryId.ToString(),
                    $"#{beneficiaryId} · {EntityFileOwners.Label(ownerKind)} «{ownerName}»",
                    AuditDetails.Identification(("Nume fișier", saved.OriginalName), ("Atașat la", $"{EntityFileOwners.Label(ownerKind)} «{ownerName}»"),
                        ("Dimensiune (octeți)", saved.SizeBytes.ToString(System.Globalization.CultureInfo.InvariantCulture)), ("Autor", saved.UploadedBy)),
                    string.Empty, cancellationToken).ConfigureAwait(false);
                return saved;
            }
            catch
            {
                if (File.Exists(destination)) File.Delete(destination);
                throw;
            }
        }
        finally { gate.Release(); }
    }

    public async Task<EntityFile> RenameAsync(int fileId, string newName, CancellationToken cancellationToken = default)
    {
        var displayName = EntityFileRules.SafeOriginalName(newName);
        EntityFile? file; string ownerName = ""; int beneficiaryId = 0;
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        file = (await ReadAsync(connection, "id=@id", cancellationToken, ("@id", fileId)).ConfigureAwait(false)).FirstOrDefault();
        if (file is null) throw new WorkPointOperationException("Fișierul a fost șters deja. Actualizează pagina.");
        var owner = Owner(file.OwnerKind);
        if (accessControl is not null) await accessControl.EnsureAsync(owner.EditPermission, cancellationToken).ConfigureAwait(false);
        if (!MariaDatabaseGuard.IsAllowedDatabase(configuration))
            throw new WorkPointOperationException("Modificările sunt permise numai în baza BlazorStoc.");
        if (!string.Equals(Path.GetExtension(displayName), Path.GetExtension(file.OriginalName), StringComparison.OrdinalIgnoreCase))
            throw new WorkPointOperationException($"Extensia fișierului nu se schimbă (rămâne «{Path.GetExtension(file.OriginalName)}»).");
        if (string.Equals(displayName, file.OriginalName, StringComparison.Ordinal)) return file;
        await using (var lookup = new MySqlCommand($"SELECT {owner.NameSql}, {owner.BeneficiarySql} FROM {owner.Table} WHERE id=@id", connection))
        {
            lookup.Parameters.AddWithValue("@id", file.OwnerId);
            await using var reader = await lookup.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) (ownerName, beneficiaryId) = (reader.GetString(0), checked((int)reader.GetInt64(1)));
        }
        await using (var update = new MySqlCommand("UPDATE entity_files SET original_name=@name WHERE id=@id", connection))
        {
            update.Parameters.AddWithValue("@name", displayName);
            update.Parameters.AddWithValue("@id", fileId);
            if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw new WorkPointOperationException("Fișierul a fost șters deja. Actualizează pagina.");
        }
        if (beneficiaryId > 0)
            await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.Beneficiary, AuditActions.RenameAttachedFile, beneficiaryId.ToString(),
                $"#{beneficiaryId} · {EntityFileOwners.Label(file.OwnerKind)} «{ownerName}»",
                AuditDetails.Changes(new AuditChange("Nume fișier", file.OriginalName, displayName)), string.Empty, cancellationToken).ConfigureAwait(false);
        return file with { OriginalName = displayName };
    }

    public async Task DeleteAsync(int fileId, CancellationToken cancellationToken = default)
    {
        EntityFile? file; string ownerName = ""; int beneficiaryId = 0;
        await using (var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false))
        {
            file = (await ReadAsync(connection, "id=@id", cancellationToken, ("@id", fileId)).ConfigureAwait(false)).FirstOrDefault();
            if (file is null) throw new WorkPointOperationException("Fișierul a fost șters deja. Actualizează pagina.");
            var owner = Owner(file.OwnerKind);
            if (accessControl is not null) await accessControl.EnsureAsync(owner.EditPermission, cancellationToken).ConfigureAwait(false);
            await using var lookup = new MySqlCommand($"SELECT {owner.NameSql}, {owner.BeneficiarySql} FROM {owner.Table} WHERE id=@id", connection);
            lookup.Parameters.AddWithValue("@id", file.OwnerId);
            await using var reader = await lookup.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) (ownerName, beneficiaryId) = (reader.GetString(0), checked((int)reader.GetInt64(1)));
        }
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
            await using var delete = new MySqlCommand("DELETE FROM entity_files WHERE id=@id", connection);
            delete.Parameters.AddWithValue("@id", fileId);
            if (await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw new WorkPointOperationException("Fișierul a fost șters deja. Actualizează pagina.");
            var path = Resolve(file.StoredName);
            if (File.Exists(path)) File.Delete(path);
        }
        finally { gate.Release(); }
        if (beneficiaryId > 0)
            await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.Beneficiary, AuditActions.DeleteAttachedFile, beneficiaryId.ToString(),
                $"#{beneficiaryId} · {EntityFileOwners.Label(file.OwnerKind)} «{ownerName}»",
                AuditDetails.Identification(("Nume fișier", file.OriginalName), ("Atașat la", $"{EntityFileOwners.Label(file.OwnerKind)} «{ownerName}»"),
                    ("Dimensiune (octeți)", file.SizeBytes.ToString(System.Globalization.CultureInfo.InvariantCulture)), ("Autor încărcare", file.UploadedBy)),
                string.Empty, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<EntityFile>> ReadAsync(MySqlConnection connection, string where, CancellationToken token, params (string Name, object Value)[] parameters)
    {
        await using var command = new MySqlCommand($"SELECT {Columns} FROM entity_files WHERE {where} ORDER BY uploaded_utc, id", connection);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        var result = new List<EntityFile>();
        while (await reader.ReadAsync(token).ConfigureAwait(false))
            result.Add(new(checked((int)reader.GetInt64(0)), reader.GetString(1), checked((int)reader.GetInt64(2)), reader.GetString(4), reader.GetString(3),
                reader.GetString(5), reader.GetInt64(6), reader.GetString(7), reader.GetString(8), MariaTimeText.Parse(reader.GetString(9))));
        return result;
    }

    private string Resolve(string relativePath)
    {
        var root = rootPath + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(rootPath, relativePath));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new WorkPointOperationException("Calea fișierului nu este validă.");
        return path;
    }
}
