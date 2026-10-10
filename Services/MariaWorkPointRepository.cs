using System.Data;
using MySqlConnector;
using static BlazorStoc.Services.MariaDb;

namespace BlazorStoc.Services;

// MariaDB mode: `beneficiary_work_points` is created by migration 2 and extended by migration 7 (MariaSchemaMigrations); like the
// other repositories this one never alters the schema. The main work point of a beneficiary is a real row (is_primary = 1), created
// with the beneficiary (MariaBeneficiaryRepository) or by WorkPointBackfill for the beneficiaries that existed before.
public sealed class MariaWorkPointRepository(
    IConfiguration configuration,
    IAccessControl? accessControl = null,
    IAuditTrail? auditTrail = null,
    IArchiveService? archiveService = null) : IWorkPointRepository
{
    internal const string Columns = "id, beneficiary_id, name, address, phone, contact_person, version, is_primary, description, latitude, longitude";
    private readonly IArchiveService archiver = archiveService ?? new ArchiveService(accessControl);

    public async Task<IReadOnlyList<WorkPoint>> GetAsync(int beneficiaryId, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAnyPermissionAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        var result = new List<WorkPoint>();
        await using (var command = Command(connection, null,
            $"SELECT {Columns} FROM beneficiary_work_points WHERE beneficiary_id=@id ORDER BY is_primary DESC, name, id", ("@id", beneficiaryId)))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(Read(reader));
        if (result.Count == 0 || !result[0].IsPrimary)
        {
            // Not backfilled yet: the main one is shown as derived from the beneficiary (read-only, Id 0).
            await using var owner = Command(connection, null, "SELECT address, phone FROM beneficiaries WHERE id=@id", ("@id", beneficiaryId));
            await using var reader = await owner.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                result.Insert(0, new WorkPoint(0, beneficiaryId, WorkPointRules.PrimaryName, reader.GetString(0), reader.GetString(1), IsPrimary: true));
        }
        return result;
    }

    public async Task<WorkPoint> CreateAsync(int beneficiaryId, WorkPointInput input, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAsync("beneficiari.edit", cancellationToken).ConfigureAwait(false);
        var value = input.Validated();
        var (workPoint, ownerName) = await WriteAsync(async (connection, transaction) =>
        {
            var owner = await GetOwnerAsync(connection, transaction, beneficiaryId, cancellationToken).ConfigureAwait(false);
            await EnsureUniqueAsync(connection, transaction, owner, value, null, cancellationToken).ConfigureAwait(false);
            await using var command = Command(connection, transaction, """
                INSERT INTO beneficiary_work_points (beneficiary_id, name, address, normalized_address, phone, contact_person, description, latitude, longitude, is_primary, version)
                VALUES (@beneficiaryId, @name, @address, @normalizedAddress, @phone, @contactPerson, @description, @latitude, @longitude, 0, 0)
                """, Fields(beneficiaryId, value));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return (Build(checked((int)command.LastInsertedId), beneficiaryId, value, 0, false), owner.Name);
        }, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.Beneficiary, AuditActions.CreateWorkPoint, beneficiaryId.ToString(),
            WorkPointRules.Target(beneficiaryId, ownerName, workPoint.Name),
            AuditDetails.Changes(new AuditChange("Punct de lucru adăugat", string.Empty, WorkPointRules.Identification(workPoint))), string.Empty,
            cancellationToken).ConfigureAwait(false);
        return workPoint;
    }

    public async Task<WorkPoint> UpdateAsync(WorkPoint original, WorkPointInput input, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAsync("beneficiari.edit", cancellationToken).ConfigureAwait(false);
        if (original.Id == 0) throw new WorkPointOperationException("Punctul de lucru principal nu a fost încă creat. Actualizează pagina.");
        // The address and the phone of the main work point follow the beneficiary: whatever the form holds is replaced by what is stored.
        if (original.IsPrimary) { input.Address = original.Address; input.Phone = original.Phone; }
        var value = input.Validated();
        var (workPoint, ownerName) = await WriteAsync(async (connection, transaction) =>
        {
            WorkPointRules.CheckCurrent(await GetLockedAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false), original);
            var owner = await GetOwnerAsync(connection, transaction, original.BeneficiaryId, cancellationToken).ConfigureAwait(false);
            if (!original.IsPrimary) await EnsureUniqueAsync(connection, transaction, owner, value, original.Id, cancellationToken).ConfigureAwait(false);
            var version = checked(original.Version + 1);
            await using var command = Command(connection, transaction, """
                UPDATE beneficiary_work_points SET name=@name, address=@address, normalized_address=@normalizedAddress,
                    phone=@phone, contact_person=@contactPerson, description=@description, latitude=@latitude, longitude=@longitude,
                    version=@version WHERE id=@id AND version=@oldVersion
                """, [.. Fields(original.BeneficiaryId, value), ("@version", version), ("@id", original.Id), ("@oldVersion", original.Version)]);
            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1) throw WorkPointRules.Changed();
            return (Build(original.Id, original.BeneficiaryId, value, version, original.IsPrimary), owner.Name);
        }, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordEditAsync(auditTrail, accessControl, AuditEntities.Beneficiary, original.BeneficiaryId.ToString(),
            WorkPointRules.Target(original.BeneficiaryId, ownerName, workPoint.Name),
            WorkPointRules.Changes(original, workPoint), WorkPointRules.GeneratedEditReason, cancellationToken,
            WorkPointRules.EditAction(original, workPoint)).ConfigureAwait(false);
        return workPoint;
    }

    // Archived deletion: the row goes to archive_work_points, its photos to archive_service_photos with their files moved to the
    // archive directory, all in one database transaction (the files are copied first and the live ones removed after the commit).
    public async Task DeleteAsync(WorkPoint original, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAsync("beneficiari.edit", cancellationToken).ConfigureAwait(false);
        if (original.IsPrimary) throw WorkPointRules.PrimaryNotDeletable();
        string ownerName;
        IReadOnlyList<ServicePhoto> photos;
        await using (var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false))
        {
            await using var owner = Command(connection, null, "SELECT name FROM beneficiaries WHERE id=@id", ("@id", original.BeneficiaryId));
            ownerName = await owner.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string
                ?? throw new WorkPointOperationException("Beneficiarul nu mai există. Actualizează lista.");
            photos = await ServicePhotoArchive.ReadAsync(connection, null, "work_point_id=@id", cancellationToken, ("@id", original.Id)).ConfigureAwait(false);
            // A work point covered by a contract (On or Off) cannot be deleted: it must be taken out of the contract first.
            if (await MariaServiceContractRepository.CoverageLabelAsync(connection, null, original.Id, cancellationToken).ConfigureAwait(false) is { } coveredBy)
                throw ServiceContractRules.WorkPointCovered(coveredBy);
            if (await MariaRiskAnalysisRepository.NumberForWorkPointAsync(connection, null, original.Id, cancellationToken).ConfigureAwait(false) is { } analysisNumber)
                throw RiskAnalysisRules.WorkPointHasAnalysis(analysisNumber);
            if (await MariaServiceInterventionRepository.CountAsync(connection, null, "work_point_id", original.Id, cancellationToken).ConfigureAwait(false) is var interventions and > 0)
                throw ServiceInterventionRules.WorkPointHasInterventions(interventions);
        }
        var liveRoot = MariaAssetPaths.ServicePhotos(configuration);
        var archiveRoot = MariaAssetPaths.ArchiveFiles(configuration);
        await archiver.ExecuteAsync(ArchiveRequests.WorkPoint(original, ownerName, photos, WorkPointRules.DeleteReason), async (operation, token) =>
        {
            var prepared = await ServicePhotoArchive.PrepareAsync(liveRoot, archiveRoot, photos, operation, token).ConfigureAwait(false);
            try
            {
                await WriteAsync(async (connection, transaction) =>
                {
                    WorkPointRules.CheckCurrent(await GetLockedAsync(connection, transaction, original.Id, token).ConfigureAwait(false), original);
                    if (await MariaServiceContractRepository.CoverageLabelAsync(connection, transaction, original.Id, token).ConfigureAwait(false) is { } coveredNow)
                        throw ServiceContractRules.WorkPointCovered(coveredNow);
                    if (await MariaRiskAnalysisRepository.NumberForWorkPointAsync(connection, transaction, original.Id, token).ConfigureAwait(false) is { } analysisNow)
                        throw RiskAnalysisRules.WorkPointHasAnalysis(analysisNow);
                    if (await MariaServiceInterventionRepository.CountAsync(connection, transaction, "work_point_id", original.Id, token).ConfigureAwait(false) is var interventionsNow and > 0)
                        throw ServiceInterventionRules.WorkPointHasInterventions(interventionsNow);
                    var current = await ServicePhotoArchive.ReadAsync(connection, transaction, "work_point_id=@id", token, ("@id", original.Id)).ConfigureAwait(false);
                    if (!current.Select(photo => photo.Id).Order().SequenceEqual(photos.Select(photo => photo.Id).Order())) throw WorkPointRules.Changed();
                    await MariaArchivePersistence.InsertAsync(connection, transaction, operation, prepared, token).ConfigureAwait(false);
                    await using (var deletePhotos = Command(connection, transaction, "DELETE FROM service_photos WHERE work_point_id=@id", ("@id", original.Id)))
                        await deletePhotos.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    await using var command = Command(connection, transaction,
                        "DELETE FROM beneficiary_work_points WHERE id=@id AND version=@version", ("@id", original.Id), ("@version", original.Version));
                    if (await command.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1) throw WorkPointRules.Changed();
                    await MariaArchivePersistence.InsertAuditAsync(connection, transaction, operation, token).ConfigureAwait(false);
                    return true;
                }, token).ConfigureAwait(false);
            }
            catch
            {
                ServicePhotoArchive.Rollback(archiveRoot, prepared);
                throw;
            }
            await ServicePhotoArchive.CompleteAsync(liveRoot, archiveRoot, prepared, token).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    private Task<T> WriteAsync<T>(Func<MySqlConnection, MySqlTransaction, Task<T>> action, CancellationToken token) =>
        MariaDb.WriteAsync(configuration, action, message => new WorkPointOperationException(message), Translate, token);

    private static Exception? Translate(MySqlException exception) => exception.Number switch
    {
        1451 => new WorkPointOperationException("Punctul de lucru este acoperit de un contract sau are date asociate și nu poate fi șters. Scoate-l mai întâi din contract."),
        // A concurrent save passed the check above and hit uq_beneficiary_work_points_0.
        1062 => new WorkPointOperationException("Există deja un punct de lucru cu această adresă."),
        _ => null
    };

    internal static async Task<WorkPoint?> GetLockedAsync(MySqlConnection connection, MySqlTransaction transaction, int id, CancellationToken token)
    {
        await using var command = Command(connection, transaction,
            $"SELECT {Columns} FROM beneficiary_work_points WHERE id=@id FOR UPDATE", ("@id", id));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        return await reader.ReadAsync(token).ConfigureAwait(false) ? Read(reader) : null;
    }

    private static async Task<(int Id, string Name, string Address)> GetOwnerAsync(MySqlConnection connection,
        MySqlTransaction transaction, int beneficiaryId, CancellationToken token)
    {
        await using var command = Command(connection, transaction,
            "SELECT name, address FROM beneficiaries WHERE id=@id FOR UPDATE", ("@id", beneficiaryId));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
            throw new WorkPointOperationException("Beneficiarul nu mai există. Actualizează lista.");
        return (beneficiaryId, reader.GetString(0), reader.GetString(1));
    }

    private static async Task EnsureUniqueAsync(MySqlConnection connection, MySqlTransaction transaction,
        (int Id, string Name, string Address) owner, WorkPointInput value, int? excludedId, CancellationToken token)
    {
        var key = AddressNormalization.Key(value.Address);
        if (AddressNormalization.Key(owner.Address) == key) throw WorkPointRules.DuplicateAddress(WorkPointRules.PrimaryName);
        await using var command = Command(connection, transaction, """
            SELECT name FROM beneficiary_work_points
            WHERE beneficiary_id=@beneficiaryId AND normalized_address=@normalizedAddress AND (@id IS NULL OR id<>@id) LIMIT 1
            """, ("@beneficiaryId", owner.Id), ("@normalizedAddress", key), ("@id", excludedId is null ? DBNull.Value : excludedId.Value));
        if (await command.ExecuteScalarAsync(token).ConfigureAwait(false) is string existingName)
            throw WorkPointRules.DuplicateAddress(existingName);
    }

    internal static WorkPoint Read(MySqlDataReader reader) =>
        new(checked((int)reader.GetInt64(0)), checked((int)reader.GetInt64(1)), reader.GetString(2), reader.GetString(3),
            reader.GetString(4), reader.GetString(5), reader.GetInt64(6), reader.GetInt32(7) != 0, reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetDecimal(9), reader.IsDBNull(10) ? null : reader.GetDecimal(10));

    private static WorkPoint Build(int id, int beneficiaryId, WorkPointInput value, long version, bool isPrimary) =>
        new(id, beneficiaryId, value.Name, value.Address, value.Phone, value.ContactPerson, version, isPrimary, value.Description, value.Latitude, value.Longitude);

    private static (string, object?)[] Fields(int beneficiaryId, WorkPointInput value) =>
    [
        ("@beneficiaryId", beneficiaryId), ("@name", value.Name), ("@address", value.Address),
        ("@normalizedAddress", AddressNormalization.Key(value.Address)), ("@phone", value.Phone),
        ("@contactPerson", value.ContactPerson), ("@description", value.Description),
        ("@latitude", value.Latitude is { } latitude ? latitude : DBNull.Value), ("@longitude", value.Longitude is { } longitude ? longitude : DBNull.Value)
    ];

}

// Creates the main work point row of every beneficiary that lacks one (the beneficiaries that existed before migration 7), with the
// application account and idempotently, at startup. The normalized address is computed in C#, which is why this is not part of the
// DDL migration. An additional work point whose address equals the beneficiary's (the rule forbids it, but old data could hold one)
// is promoted to main instead of creating a duplicate row.
public static class WorkPointBackfill
{
    public sealed record Result(int Created, int Promoted, int EmptyAddress);

    public static async Task<Result> EnsurePrimariesAsync(IConfiguration configuration, CancellationToken cancellationToken = default)
    {
        if (!MariaDatabaseGuard.IsAllowedDatabase(configuration)) return new(0, 0, 0);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        var missing = new List<(int Id, string Address, string Phone)>();
        await using (var select = new MySqlCommand("""
            SELECT b.id, b.address, b.phone FROM beneficiaries b
            WHERE NOT EXISTS (SELECT 1 FROM beneficiary_work_points w WHERE w.beneficiary_id=b.id AND w.is_primary=1)
            """, connection))
        await using (var reader = await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                missing.Add((checked((int)reader.GetInt64(0)), reader.GetString(1), reader.GetString(2)));
        int created = 0, promoted = 0, empty = 0;
        foreach (var (id, address, phone) in missing)
        {
            if (address.Trim().Length == 0) empty++;
            var key = AddressNormalization.Key(address);
            await using var promote = new MySqlCommand("""
                UPDATE beneficiary_work_points SET is_primary=1, address=@address, phone=@phone, version=version+1
                WHERE beneficiary_id=@id AND is_primary=0 AND normalized_address=@key
                """, connection);
            promote.Parameters.AddWithValue("@id", id); promote.Parameters.AddWithValue("@address", address);
            promote.Parameters.AddWithValue("@phone", phone); promote.Parameters.AddWithValue("@key", key);
            if (await promote.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0) { promoted++; continue; }
            await using var insert = new MySqlCommand("""
                INSERT IGNORE INTO beneficiary_work_points (beneficiary_id, name, address, normalized_address, phone, is_primary, version)
                VALUES (@id, @name, @address, @key, @phone, 1, 0)
                """, connection);
            insert.Parameters.AddWithValue("@id", id); insert.Parameters.AddWithValue("@name", WorkPointRules.PrimaryName);
            insert.Parameters.AddWithValue("@address", address); insert.Parameters.AddWithValue("@key", key); insert.Parameters.AddWithValue("@phone", phone);
            if (await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0) created++;
        }
        return new(created, promoted, empty);
    }
}
