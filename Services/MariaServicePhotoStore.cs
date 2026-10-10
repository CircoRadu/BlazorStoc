using System.Data;
using MySqlConnector;
using static BlazorStoc.Services.MariaDb;

namespace BlazorStoc.Services;

// Rows of `service_photos` (migration 7) and files under the service-photos asset directory. Every "_utc" column is text: read
// and written only through MariaTimeText. Deleting a photo, a work point or a beneficiary moves the files to the archive
// directory together with archive rows (ArchiveFileSafety), like the project files.
public sealed class MariaServicePhotoStore(IConfiguration configuration, IAccessControl? accessControl = null,
    IAuditTrail? auditTrail = null, IArchiveService? archiveService = null) : IServicePhotoStore
{
    private readonly IArchiveService archiver = archiveService ?? new ArchiveService(accessControl);
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string rootPath = MariaAssetPaths.ServicePhotos(configuration);
    private readonly string archivePath = MariaAssetPaths.ArchiveFiles(configuration);


    public async Task<IReadOnlyList<ServicePhoto>> GetForWorkPointAsync(int workPointId, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAnyPermissionAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        return await ServicePhotoArchive.ReadAsync(connection, null, "work_point_id=@id", cancellationToken, ("@id", workPointId)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<int, int>> CountsForBeneficiaryAsync(int beneficiaryId, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAnyPermissionAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("""
            SELECT p.work_point_id, COUNT(*) FROM service_photos p JOIN beneficiary_work_points w ON w.id=p.work_point_id
            WHERE w.beneficiary_id=@id GROUP BY p.work_point_id
            """, connection);
        command.Parameters.AddWithValue("@id", beneficiaryId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new Dictionary<int, int>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result[checked((int)reader.GetInt64(0))] = checked((int)reader.GetInt64(1));
        return result;
    }

    public async Task<ServicePhotoContent?> GetContentAsync(int photoId, CancellationToken cancellationToken = default)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        var photo = (await ServicePhotoArchive.ReadAsync(connection, null, "id=@id", cancellationToken, ("@id", photoId)).ConfigureAwait(false)).FirstOrDefault();
        if (photo is null) return null;
        var path = Resolve(photo.StoredName);
        return File.Exists(path) ? new(await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false), photo.ContentType, photo.OriginalName) : null;
    }

    public async Task<IReadOnlyList<ServicePhoto>> GetForInterventionAsync(int interventionId, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAnyPermissionAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        return await ServicePhotoArchive.ReadAsync(connection, null, "intervention_id=@id", cancellationToken, ("@id", interventionId)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<int, int>> CountsForInterventionsAsync(int beneficiaryId, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAnyPermissionAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("""
            SELECT p.intervention_id, COUNT(*) FROM service_photos p JOIN service_interventions i ON i.id=p.intervention_id
            WHERE i.beneficiary_id=@id GROUP BY p.intervention_id
            """, connection);
        command.Parameters.AddWithValue("@id", beneficiaryId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new Dictionary<int, int>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result[checked((int)reader.GetInt64(0))] = checked((int)reader.GetInt64(1));
        return result;
    }

    public Task<ServicePhoto> AddToWorkPointAsync(int workPointId, string originalName, byte[] content, string caption, CancellationToken cancellationToken = default) =>
        AddAsync(false, workPointId, originalName, content, caption, cancellationToken);

    public Task<ServicePhoto> AddToInterventionAsync(int interventionId, string originalName, byte[] content, string caption, CancellationToken cancellationToken = default) =>
        AddAsync(true, interventionId, originalName, content, caption, cancellationToken);

    // A photo belongs to exactly one work point or one intervention (the table has a CHECK); the rest of the handling is the same.
    private async Task<ServicePhoto> AddAsync(bool onIntervention, int ownerId, string originalName, byte[] content, string caption, CancellationToken cancellationToken)
    {
        if (accessControl is not null) await accessControl.EnsureAsync("mentenanta.edit", cancellationToken).ConfigureAwait(false);
        if (!MariaDatabaseGuard.IsAllowedDatabase(configuration))
            throw new WorkPointOperationException("Modificările sunt permise numai în baza BlazorStoc.");
        var contentType = ServicePhotoRules.DetectContentType(content);
        var displayName = ServicePhotoRules.SafeOriginalName(originalName);
        var normalizedCaption = ServicePhotoRules.NormalizeCaption(caption);
        var hash = ServicePhotoRules.Hash(content);
        var author = (accessControl is null ? null : await accessControl.GetUsernameAsync(cancellationToken).ConfigureAwait(false)) ?? "necunoscut";
        var storedName = ServicePhotoRules.NewStoredName(contentType);
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
                ServicePhoto saved; string pointName; int beneficiaryId;
                try
                {
                    // The work point row is locked, so two uploads of the same point are counted one after the other.
                    var ownerColumn = onIntervention ? "intervention_id" : "work_point_id";
                    await using (var lookup = new MySqlCommand(onIntervention
                        ? "SELECT work_point_name, beneficiary_id FROM service_interventions WHERE id=@id FOR UPDATE"
                        : "SELECT name, beneficiary_id FROM beneficiary_work_points WHERE id=@id FOR UPDATE", connection, transaction))
                    {
                        lookup.Parameters.AddWithValue("@id", ownerId);
                        await using var reader = await lookup.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                            throw new WorkPointOperationException(onIntervention
                                ? "Intervenția nu mai există. Actualizează pagina înainte să reîncerci încărcarea."
                                : "Punctul de lucru nu mai există. Actualizează pagina înainte să reîncerci încărcarea.");
                        (pointName, beneficiaryId) = (reader.GetString(0), checked((int)reader.GetInt64(1)));
                    }
                    await using (var count = new MySqlCommand($"SELECT COUNT(*) FROM service_photos WHERE {ownerColumn}=@id", connection, transaction))
                    {
                        count.Parameters.AddWithValue("@id", ownerId);
                        if (Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) >= ServicePhotoRules.MaximumPerOwner)
                            throw ServicePhotoRules.LimitReached();
                    }
                    var uploaded = MariaTimeText.Now();
                    await using var insert = new MySqlCommand($"""
                        INSERT INTO service_photos ({ownerColumn}, relative_path, original_name, content_type, byte_length, sha256, caption, uploaded_by, uploaded_utc)
                        VALUES (@point, @path, @name, @type, @length, @hash, @caption, @author, @uploaded)
                        """, connection, transaction);
                    foreach (var (name, value) in new (string, object)[] { ("@point", ownerId), ("@path", storedName), ("@name", displayName), ("@type", contentType),
                                 ("@length", content.LongLength), ("@hash", hash), ("@caption", normalizedCaption), ("@author", author), ("@uploaded", MariaTimeText.Format(uploaded)) })
                        insert.Parameters.AddWithValue(name, value);
                    try { await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false); }
                    catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry) { throw ServicePhotoRules.Duplicate(); }
                    saved = new(checked((int)insert.LastInsertedId), onIntervention ? null : ownerId, onIntervention ? ownerId : null, displayName, storedName, contentType, content.LongLength, hash, normalizedCaption, author, uploaded);
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                    throw;
                }
                await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.Beneficiary,
                    onIntervention ? AuditActions.AddInterventionPhoto : AuditActions.AddWorkPointPhoto, beneficiaryId.ToString(),
                    onIntervention ? $"#{beneficiaryId} · intervenție la punctul de lucru «{pointName}»" : $"#{beneficiaryId} · punct de lucru «{pointName}»",
                    AuditDetails.Identification(("Nume fișier", saved.OriginalName), ("Punct de lucru", pointName), ("Legendă", saved.Caption),
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

    public async Task DeleteAsync(int photoId, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAsync("mentenanta.edit", cancellationToken).ConfigureAwait(false);
        ServicePhoto? photo; string pointName;
        await using (var lookupConnection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false))
        {
            photo = (await ServicePhotoArchive.ReadAsync(lookupConnection, null, "id=@id", cancellationToken, ("@id", photoId)).ConfigureAwait(false)).FirstOrDefault();
            if (photo is null) throw new WorkPointOperationException("Fotografia a fost eliminată deja. Actualizează pagina.");
            await using var name = new MySqlCommand(photo.InterventionId is null
                ? "SELECT name FROM beneficiary_work_points WHERE id=@id" : "SELECT work_point_name FROM service_interventions WHERE id=@id", lookupConnection);
            name.Parameters.AddWithValue("@id", photo.WorkPointId ?? photo.InterventionId ?? 0);
            pointName = await name.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string ?? string.Empty;
        }
        await archiver.ExecuteAsync(ArchiveRequests.ServicePhoto(photo, pointName, WorkPointRules.PhotoDeleteReason), async (operation, token) =>
        {
            await gate.WaitAsync(token).ConfigureAwait(false);
            IReadOnlyList<ArchiveFileRecord> prepared = [];
            try
            {
                prepared = await ServicePhotoArchive.PrepareAsync(rootPath, archivePath, [photo], operation, token).ConfigureAwait(false);
                await using var connection = await MariaDb.OpenAsync(configuration, token).ConfigureAwait(false);
                await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, token).ConfigureAwait(false);
                try
                {
                    await MariaArchivePersistence.InsertAsync(connection, transaction, operation, prepared, token).ConfigureAwait(false);
                    await using var delete = new MySqlCommand("DELETE FROM service_photos WHERE id=@id", connection, transaction);
                    delete.Parameters.AddWithValue("@id", photo.Id);
                    if (await delete.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
                        throw new WorkPointOperationException("Fotografia a fost eliminată deja. Actualizează pagina.");
                    await MariaArchivePersistence.InsertAuditAsync(connection, transaction, operation, token).ConfigureAwait(false);
                    await transaction.CommitAsync(token).ConfigureAwait(false);
                }
                catch
                {
                    await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                    throw;
                }
                await ServicePhotoArchive.CompleteAsync(rootPath, archivePath, prepared, token).ConfigureAwait(false);
            }
            catch
            {
                ServicePhotoArchive.Rollback(archivePath, prepared);
                throw;
            }
            finally { gate.Release(); }
        }, cancellationToken).ConfigureAwait(false);
    }

    private string Resolve(string relativePath)
    {
        var root = rootPath + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(rootPath, relativePath));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new WorkPointOperationException("Calea fotografiei nu este validă.");
        return path;
    }

}

// Shared by the photo store and the repositories that delete work points or beneficiaries (they archive the photos of what they delete).
internal static class ServicePhotoArchive
{
    public const string Columns = "id, work_point_id, intervention_id, relative_path, original_name, content_type, byte_length, sha256, caption, uploaded_by, uploaded_utc";

    public static async Task<IReadOnlyList<ServicePhoto>> ReadAsync(MySqlConnection connection, MySqlTransaction? transaction, string where,
        CancellationToken token, params (string Name, object Value)[] parameters)
    {
        await using var command = new MySqlCommand($"SELECT {Columns} FROM service_photos WHERE {where} ORDER BY uploaded_utc, id", connection, transaction);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        var result = new List<ServicePhoto>();
        while (await reader.ReadAsync(token).ConfigureAwait(false))
            result.Add(new(checked((int)reader.GetInt64(0)), reader.IsDBNull(1) ? null : checked((int)reader.GetInt64(1)),
                reader.IsDBNull(2) ? null : checked((int)reader.GetInt64(2)), reader.GetString(4), reader.GetString(3), reader.GetString(5),
                reader.GetInt64(6), reader.GetString(7), reader.GetString(8), reader.GetString(9), MariaTimeText.Parse(reader.GetString(10))));
        return result;
    }

    // Copies the live files to the archive directory (verified by size and hash). A failure removes the copies made so far.
    public static async Task<IReadOnlyList<ArchiveFileRecord>> PrepareAsync(string liveRoot, string archiveRoot, IEnumerable<ServicePhoto> photos,
        ArchiveOperation operation, CancellationToken token)
    {
        var prepared = new List<ArchiveFileRecord>();
        try
        {
            foreach (var photo in photos)
            {
                // A photo whose file is already gone still has its row archived (nothing to copy).
                if (!File.Exists(Path.Combine(liveRoot, photo.StoredName))) continue;
                prepared.Add(await ArchiveFileSafety.PrepareAsync(liveRoot, archiveRoot, photo.StoredName, operation, AuditEntities.ServicePhoto,
                    photo.Id.ToString(), photo.ContentType, photo.OriginalName, photo.SizeBytes, token).ConfigureAwait(false));
            }
            return prepared;
        }
        catch
        {
            Rollback(archiveRoot, prepared);
            throw;
        }
    }

    public static void Rollback(string archiveRoot, IEnumerable<ArchiveFileRecord> files)
    {
        foreach (var file in files) ArchiveFileSafety.Rollback(archiveRoot, file);
    }

    public static async Task CompleteAsync(string liveRoot, string archiveRoot, IEnumerable<ArchiveFileRecord> files, CancellationToken token)
    {
        foreach (var file in files) await ArchiveFileSafety.CompleteAsync(liveRoot, archiveRoot, file, token).ConfigureAwait(false);
    }
}
