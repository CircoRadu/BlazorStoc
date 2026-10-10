using System.Data;
using MySqlConnector;
using static BlazorStoc.Services.MariaDb;

namespace BlazorStoc.Services;

// MariaDB mode: `beneficiaries` is the real, already-populated table (migration\schema-mariadb.sql), created and
// owned outside the app - the runtime account has no DDL rights, so this repository never creates or alters it.
public sealed class MariaBeneficiaryRepository(
    IConfiguration configuration,
    IAccessControl? accessControl = null,
    IAuditTrail? auditTrail = null,
    IArchiveService? archiveService = null) : IBeneficiaryRepository
{
    private readonly IArchiveService archiver = archiveService ?? new ArchiveService(accessControl);

    public async Task<IReadOnlyList<Beneficiary>> GetBeneficiariesAsync(CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAnyPermissionAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("""
            SELECT id, name, cui, version, kind, address, phone, registry_number, postal_code, caen_code, anaf_verified FROM beneficiaries ORDER BY name, id
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<Beneficiary>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(Read(reader));
        return result;
    }

    public async Task<Beneficiary> CreateAsync(BeneficiaryInput input, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAsync("beneficiari.add", cancellationToken).ConfigureAwait(false);
        var value = input.Validated();
        var beneficiary = await WriteAsync(async (connection, transaction) =>
        {
            await EnsureUniqueAsync(connection, transaction, value, null, cancellationToken).ConfigureAwait(false);
            await using var command = Command(connection, transaction, """
                INSERT INTO beneficiaries (name, normalized_name, cui, normalized_cui, kind, address, phone, registry_number,
                    postal_code, caen_code, anaf_verified, version)
                VALUES (@name, @normalizedName, @cui, @normalizedCui, @kind, @address, @phone, @registryNumber,
                    @postalCode, @caenCode, @anafVerified, 0)
                """, Fields(value));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            var createdId = checked((int)command.LastInsertedId);
            await InsertPrimaryWorkPointAsync(connection, transaction, createdId, value, cancellationToken).ConfigureAwait(false);
            return Build(createdId, value, 0);
        }, cancellationToken, value).ConfigureAwait(false);
        await AuditRecorder.RecordCreateAsync(auditTrail, accessControl, AuditEntities.Beneficiary, beneficiary.Id.ToString(),
            $"#{beneficiary.Id} · {beneficiary.Name}", BeneficiaryRules.Identification(beneficiary), cancellationToken).ConfigureAwait(false);
        return beneficiary;
    }

    public async Task<Beneficiary> UpdateAsync(Beneficiary original, BeneficiaryInput input, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAsync("beneficiari.edit", cancellationToken).ConfigureAwait(false);
        var value = input.Validated(true);
        var beneficiary = await WriteAsync(async (connection, transaction) =>
        {
            BeneficiaryRules.CheckCurrent(await GetLockedAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false), original);
            await EnsureUniqueAsync(connection, transaction, value, original.Id, cancellationToken).ConfigureAwait(false);
            await using (var clash = Command(connection, transaction, """
                SELECT name FROM beneficiary_work_points WHERE beneficiary_id=@id AND is_primary=0 AND normalized_address=@key LIMIT 1
                """, ("@id", original.Id), ("@key", AddressNormalization.Key(value.Address))))
                if (await clash.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is string workPointName)
                    throw WorkPointRules.BeneficiaryAddressTaken(workPointName);
            var version = checked(original.Version + 1);
            await using var command = Command(connection, transaction, """
                UPDATE beneficiaries SET name=@name, normalized_name=@normalizedName, cui=@cui,
                    normalized_cui=@normalizedCui, kind=@kind, address=@address, phone=@phone, registry_number=@registryNumber,
                    postal_code=@postalCode, caen_code=@caenCode, anaf_verified=@anafVerified,
                    version=@version WHERE id=@id AND version=@oldVersion
                """, [.. Fields(value), ("@version", version), ("@id", original.Id), ("@oldVersion", original.Version)]);
            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw new BeneficiaryOperationException("Beneficiarul s-a schimbat între timp. Actualizează lista.");
            await SyncPrimaryWorkPointAsync(connection, transaction, original.Id, value, cancellationToken).ConfigureAwait(false);
            return Build(original.Id, value, version);
        }, cancellationToken, value, original.Id).ConfigureAwait(false);
        await AuditRecorder.RecordEditAsync(auditTrail, accessControl, AuditEntities.Beneficiary, beneficiary.Id.ToString(),
            $"#{beneficiary.Id} · {beneficiary.Name}",
            BeneficiaryRules.Changes(original, beneficiary), value.Reason, cancellationToken).ConfigureAwait(false);
        return beneficiary;
    }

    public async Task DeleteAsync(Beneficiary original, string reason, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAsync("beneficiari.delete", cancellationToken).ConfigureAwait(false);
        var motif = ChangeReasonRules.Normalize(reason);
        if (ChangeReasonRules.ValidationError(motif) is { } reasonError) throw new BeneficiaryOperationException(reasonError);
        // The work points of the beneficiary and their photos are archived with it (rows as relations, photo files moved to the
        // archive directory); read before the archive operation starts and re-checked inside the transaction.
        var workPoints = new List<WorkPoint>();
        IReadOnlyList<ServicePhoto> photos;
        await using (var readConnection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false))
        {
            await using (var command = Command(readConnection, null,
                $"SELECT {MariaWorkPointRepository.Columns} FROM beneficiary_work_points WHERE beneficiary_id=@id ORDER BY id", ("@id", original.Id)))
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) workPoints.Add(MariaWorkPointRepository.Read(reader));
            photos = await ServicePhotoArchive.ReadAsync(readConnection, null,
                "work_point_id IN (SELECT id FROM beneficiary_work_points WHERE beneficiary_id=@id)", cancellationToken, ("@id", original.Id)).ConfigureAwait(false);
            if (await MariaServiceContractRepository.CountForBeneficiaryAsync(readConnection, null, original.Id, cancellationToken).ConfigureAwait(false) is var contracts and > 0)
                throw ServiceContractRules.BeneficiaryHasContracts(contracts);
            if (await MariaRiskAnalysisRepository.CountForBeneficiaryAsync(readConnection, null, original.Id, cancellationToken).ConfigureAwait(false) is var analyses and > 0)
                throw RiskAnalysisRules.BeneficiaryHasAnalyses(analyses);
            if (await MariaServiceInterventionRepository.CountAsync(readConnection, null, "beneficiary_id", original.Id, cancellationToken).ConfigureAwait(false) is var interventions and > 0)
                throw ServiceInterventionRules.BeneficiaryHasInterventions(interventions);
        }
        var liveRoot = MariaAssetPaths.ServicePhotos(configuration);
        var archiveRoot = MariaAssetPaths.ArchiveFiles(configuration);
        await archiver.ExecuteAsync(ArchiveRequests.Beneficiary(original, motif, workPoints, photos), async (operation, token) =>
        {
            var prepared = await ServicePhotoArchive.PrepareAsync(liveRoot, archiveRoot, photos, operation, token).ConfigureAwait(false);
            try
            {
                await WriteAsync(async (connection, transaction) =>
                {
                    BeneficiaryRules.CheckCurrent(await GetLockedAsync(connection, transaction, original.Id, token).ConfigureAwait(false), original);
                    await using (var relations = Command(connection, transaction,
                        "SELECT EXISTS(SELECT 1 FROM stock_movements WHERE beneficiary_id=@id)", ("@id", original.Id)))
                        BeneficiaryRules.CheckDelete(Convert.ToBoolean(await relations.ExecuteScalarAsync(token).ConfigureAwait(false)));
                    await using (var projects = Command(connection, transaction,
                        "SELECT COUNT(*) FROM projects WHERE beneficiary_id=@id", ("@id", original.Id)))
                        BeneficiaryRules.CheckNoLiveProjects(Convert.ToInt32(await projects.ExecuteScalarAsync(token).ConfigureAwait(false)));
                    if (await MariaServiceContractRepository.CountForBeneficiaryAsync(connection, transaction, original.Id, token).ConfigureAwait(false) is var contractCount and > 0)
                        throw ServiceContractRules.BeneficiaryHasContracts(contractCount);
                    if (await MariaRiskAnalysisRepository.CountForBeneficiaryAsync(connection, transaction, original.Id, token).ConfigureAwait(false) is var analysisCount and > 0)
                        throw RiskAnalysisRules.BeneficiaryHasAnalyses(analysisCount);
                    if (await MariaServiceInterventionRepository.CountAsync(connection, transaction, "beneficiary_id", original.Id, token).ConfigureAwait(false) is var interventionCount and > 0)
                        throw ServiceInterventionRules.BeneficiaryHasInterventions(interventionCount);
                    var currentPhotos = await ServicePhotoArchive.ReadAsync(connection, transaction,
                        "work_point_id IN (SELECT id FROM beneficiary_work_points WHERE beneficiary_id=@id)", token, ("@id", original.Id)).ConfigureAwait(false);
                    if (!currentPhotos.Select(photo => photo.Id).Order().SequenceEqual(photos.Select(photo => photo.Id).Order()))
                        throw new BeneficiaryOperationException("Beneficiarul s-a schimbat între timp. Actualizează lista.");
                    await MariaArchivePersistence.InsertAsync(connection, transaction, operation, prepared, token).ConfigureAwait(false);
                    await using (var deletePhotos = Command(connection, transaction,
                        "DELETE FROM service_photos WHERE work_point_id IN (SELECT id FROM beneficiary_work_points WHERE beneficiary_id=@id)", ("@id", original.Id)))
                        await deletePhotos.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    await using (var deletePoints = Command(connection, transaction,
                        "DELETE FROM beneficiary_work_points WHERE beneficiary_id=@id", ("@id", original.Id)))
                        await deletePoints.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    await using var command = Command(connection, transaction,
                        "DELETE FROM beneficiaries WHERE id=@id AND version=@version", ("@id", original.Id), ("@version", original.Version));
                    if (await command.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
                        throw new BeneficiaryOperationException("Beneficiarul s-a schimbat între timp. Actualizează lista.");
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

    // The main work point is a real row, created with the beneficiary and kept in step with its address and phone.
    private static async Task InsertPrimaryWorkPointAsync(MySqlConnection connection, MySqlTransaction transaction, int beneficiaryId,
        BeneficiaryInput value, CancellationToken token)
    {
        await using var insert = Command(connection, transaction, """
            INSERT INTO beneficiary_work_points (beneficiary_id, name, address, normalized_address, phone, is_primary, version)
            VALUES (@id, @name, @address, @key, @phone, 1, 0)
            """, ("@id", beneficiaryId), ("@name", WorkPointRules.PrimaryName), ("@address", value.Address),
            ("@key", AddressNormalization.Key(value.Address)), ("@phone", value.Phone));
        await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    private static async Task SyncPrimaryWorkPointAsync(MySqlConnection connection, MySqlTransaction transaction, int beneficiaryId,
        BeneficiaryInput value, CancellationToken token)
    {
        await using var update = Command(connection, transaction, """
            UPDATE beneficiary_work_points SET version=IF(address<>@address OR phone<>@phone, version+1, version),
                address=@address, normalized_address=@key, phone=@phone
            WHERE beneficiary_id=@id AND is_primary=1
            """, ("@id", beneficiaryId), ("@address", value.Address), ("@key", AddressNormalization.Key(value.Address)), ("@phone", value.Phone));
        if (await update.ExecuteNonQueryAsync(token).ConfigureAwait(false) == 0)
            await InsertPrimaryWorkPointAsync(connection, transaction, beneficiaryId, value, token).ConfigureAwait(false);
    }

    private Task<T> WriteAsync<T>(Func<MySqlConnection, MySqlTransaction, Task<T>> action, CancellationToken token,
        BeneficiaryInput? savedValue = null, int? savedId = null) =>
        MariaDb.WriteAsync(configuration, action, message => new BeneficiaryOperationException(message), async (exception, cancellation) =>
            savedValue is not null && exception.Number == 1062 ? await ConcurrentDuplicateAsync(savedValue, savedId, cancellation).ConfigureAwait(false) : null,
            token);

    private static async Task<Beneficiary?> GetLockedAsync(MySqlConnection connection, MySqlTransaction transaction, int id, CancellationToken token)
    {
        await using var command = Command(connection, transaction,
            "SELECT id, name, cui, version, kind, address, phone, registry_number, postal_code, caen_code, anaf_verified FROM beneficiaries WHERE id=@id FOR UPDATE", ("@id", id));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        return await reader.ReadAsync(token).ConfigureAwait(false) ? Read(reader) : null;
    }

    private static Beneficiary Read(MySqlDataReader reader) =>
        new(checked((int)reader.GetInt64(0)), reader.GetString(1), reader.GetString(2), reader.GetInt64(3), reader.GetString(4),
            reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetString(8), reader.GetString(9),
            reader.GetInt64(10) != 0);

    private static Beneficiary Build(int id, BeneficiaryInput value, long version) => new(id, value.Name, value.Cui, version,
        value.Kind, value.Address, value.Phone, value.RegistryNumber, value.PostalCode, value.CaenCode, value.AnafVerified);

    private static (string, object?)[] Fields(BeneficiaryInput value) =>
    [
        ("@name", value.Name), ("@normalizedName", TextNormalization.UniquenessKey(value.Name)), ("@cui", value.Cui),
        ("@normalizedCui", BeneficiaryRules.IdentityKey(value)), ("@kind", value.Kind), ("@address", value.Address),
        ("@phone", value.Phone), ("@registryNumber", value.RegistryNumber), ("@postalCode", value.PostalCode),
        ("@caenCode", value.CaenCode), ("@anafVerified", value.AnafVerified ? 1 : 0)
    ];

    private static async Task EnsureUniqueAsync(MySqlConnection connection, MySqlTransaction transaction,
        BeneficiaryInput value, int? excludedId, CancellationToken token)
    {
        await using (var cui = Command(connection, transaction, """
            SELECT name FROM beneficiaries
            WHERE normalized_cui=@normalized AND (@id IS NULL OR id<>@id) LIMIT 1
            """, ("@normalized", BeneficiaryRules.IdentityKey(value)),
            ("@id", excludedId is null ? DBNull.Value : excludedId.Value)))
        {
            if (await cui.ExecuteScalarAsync(token).ConfigureAwait(false) is string existingName)
                throw BeneficiaryRules.DuplicateIdentity(value, existingName);
        }
        await using var name = Command(connection, transaction, """
            SELECT name, cui FROM beneficiaries
            WHERE normalized_name=@normalized AND (@id IS NULL OR id<>@id) LIMIT 1
            """, ("@normalized", TextNormalization.UniquenessKey(value.Name)),
            ("@id", excludedId is null ? DBNull.Value : excludedId.Value));
        await using var reader = await name.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (await reader.ReadAsync(token).ConfigureAwait(false))
            throw new BeneficiaryOperationException(BeneficiaryRules.DuplicateNameMessage(reader.GetString(0), reader.GetString(1)));
    }

    // A concurrent save can pass the checks above and still hit uq_beneficiaries_0/uq_beneficiaries_1; report the
    // stored row after rollback, checking CUI first (same precedence as EnsureUniqueAsync).
    private async Task<BeneficiaryOperationException> ConcurrentDuplicateAsync(BeneficiaryInput value, int? excludedId, CancellationToken token)
    {
        try
        {
            await using var connection = await MariaDb.OpenAsync(configuration, token).ConfigureAwait(false);
            await using (var cui = Command(connection, null, """
                SELECT name FROM beneficiaries
                WHERE normalized_cui=@normalized AND (@id IS NULL OR id<>@id) LIMIT 1
                """, ("@normalized", BeneficiaryRules.IdentityKey(value)),
                ("@id", excludedId is null ? DBNull.Value : excludedId.Value)))
            {
                if (await cui.ExecuteScalarAsync(token).ConfigureAwait(false) is string existingName)
                    return BeneficiaryRules.DuplicateIdentity(value, existingName);
            }
            await using var name = Command(connection, null, """
                SELECT name, cui FROM beneficiaries
                WHERE normalized_name=@normalized AND (@id IS NULL OR id<>@id) LIMIT 1
                """, ("@normalized", TextNormalization.UniquenessKey(value.Name)),
                ("@id", excludedId is null ? DBNull.Value : excludedId.Value));
            await using var reader = await name.ExecuteReaderAsync(token).ConfigureAwait(false);
            return await reader.ReadAsync(token).ConfigureAwait(false)
                ? new(BeneficiaryRules.DuplicateNameMessage(reader.GetString(0), reader.GetString(1)))
                : BeneficiaryRules.DuplicateIdentity(value, null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return BeneficiaryRules.DuplicateIdentity(value, null);
        }
    }

}
