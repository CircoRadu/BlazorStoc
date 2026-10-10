using System.Data;
using MySqlConnector;
using static BlazorStoc.Services.MariaDb;

namespace BlazorStoc.Services;

// MariaDB mode: `service_interventions` is created by migration 9 (MariaSchemaMigrations); like the other repositories this one never
// alters the schema. Every write locks the row of the beneficiary first (the same lock the contract repository takes), so recording an
// intervention and changing the contracts of one beneficiary are serialized; a maintenance intervention also locks the coverage row of
// its point and moves its due date in the same transaction.
public sealed class MariaServiceInterventionRepository(
    IConfiguration configuration,
    IAccessControl? accessControl = null,
    IAuditTrail? auditTrail = null,
    IArchiveService? archiveService = null,
    TimeProvider? timeProvider = null) : IServiceInterventionRepository
{
    private const string Columns = "id, kind, beneficiary_id, work_point_id, contract_id, work_point_name, work_point_address, contract_label, " +
        "performed_on, planned_due, next_due_basis, next_due_set, notes, recorded_by, recorded_utc, version";
    private readonly IArchiveService archiver = archiveService ?? new ArchiveService(accessControl);
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private DateOnly Today => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    private sealed record Pending(string Action, string Target, string Details, string Motif);

    // The coverage row of a work point in an ACTIVE contract, with the contract fields the due date needs.
    private sealed record Coverage(int PointId, int ContractId, int? PointCycle, DateOnly NextDue, long PointVersion, string ContractNumber, DateOnly ContractDate, int ContractCycle)
    {
        public string Label => ServiceContractNumber.Format(ContractNumber, ContractDate);
        public int Cycle => PointCycle ?? ContractCycle;
    }

    public async Task<IReadOnlyList<ServiceIntervention>> GetForBeneficiaryAsync(int beneficiaryId, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, null,
            $"SELECT {Columns} FROM service_interventions WHERE beneficiary_id=@id ORDER BY performed_on DESC, id DESC", ("@id", beneficiaryId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<ServiceIntervention>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(Read(reader));
        return result;
    }

    public async Task<ServiceInterventionPage> GetPageAsync(ServiceInterventionQuery query, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        var where = new List<string>();
        var parameters = new List<(string, object?)>();
        if (query.BeneficiaryId is { } beneficiaryId) { where.Add("i.beneficiary_id=@beneficiary"); parameters.Add(("@beneficiary", beneficiaryId)); }
        if (query.Kind is { } kind) { where.Add("i.kind=@kind"); parameters.Add(("@kind", ServiceInterventionRules.KindCode(kind).ToString())); }
        if (query.From is { } from) { where.Add("i.performed_on>=@from"); parameters.Add(("@from", from.ToDateTime(TimeOnly.MinValue))); }
        if (query.To is { } to) { where.Add("i.performed_on<=@to"); parameters.Add(("@to", to.ToDateTime(TimeOnly.MinValue))); }
        if (!string.IsNullOrWhiteSpace(query.Text))
        {
            // The tables use a binary collation: compare case- and accent-insensitively for the search.
            where.Add("(b.name COLLATE utf8mb4_general_ci LIKE @text OR i.work_point_name COLLATE utf8mb4_general_ci LIKE @text OR " +
                "i.work_point_address COLLATE utf8mb4_general_ci LIKE @text OR i.contract_label COLLATE utf8mb4_general_ci LIKE @text)");
            parameters.Add(("@text", "%" + query.Text.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%"));
        }
        var filter = where.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", where);
        var size = Math.Clamp(query.PageSize, 1, 200);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        int total;
        await using (var count = Command(connection, null, "SELECT COUNT(*) FROM service_interventions i JOIN beneficiaries b ON b.id=i.beneficiary_id" + filter, [.. parameters]))
            total = Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        var prefixed = string.Join(", ", Columns.Split(", ").Select(column => "i." + column));
        var items = new List<ServiceInterventionRow>();
        await using (var command = Command(connection, null, $"""
            SELECT {prefixed}, b.name, (SELECT COUNT(*) FROM service_photos p WHERE p.intervention_id=i.id)
            FROM service_interventions i JOIN beneficiaries b ON b.id=i.beneficiary_id{filter}
            ORDER BY i.performed_on DESC, i.id DESC LIMIT {size} OFFSET {(Math.Max(1, query.Page) - 1) * size}
            """, [.. parameters]))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                items.Add(new(Read(reader), reader.GetString(16), checked((int)reader.GetInt64(17))));
        return new(items, total);
    }

    public async Task<ServiceInterventionSaved> RecordAsync(int beneficiaryId, ServiceInterventionInput input, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        var value = input.Validated();
        var performedOn = value.PerformedOn!.Value;
        if (ServiceInterventionRules.PerformedOnError(performedOn, Today) is { } dateError) throw new ServiceInterventionOperationException(dateError);
        var actor = (accessControl is null ? null : await accessControl.GetUsernameAsync(cancellationToken).ConfigureAwait(false)) ?? (accessControl is null ? "sistem" : "necunoscut");
        var recorded = MariaTimeText.Parse(MariaTimeText.Format(clock.GetUtcNow().UtcDateTime));
        var pending = new List<Pending>();
        var saved = await WriteAsync(async (connection, transaction) =>
        {
            pending.Clear();
            var ownerName = await LockBeneficiaryAsync(connection, transaction, beneficiaryId, cancellationToken).ConfigureAwait(false);
            string workPointName, workPointAddress;
            await using (var lookup = Command(connection, transaction, "SELECT name, address FROM beneficiary_work_points WHERE id=@id AND beneficiary_id=@beneficiary",
                ("@id", value.WorkPointId), ("@beneficiary", beneficiaryId)))
            await using (var reader = await lookup.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) throw ServiceInterventionRules.WorkPointNotOfBeneficiary();
                (workPointName, workPointAddress) = (reader.GetString(0), reader.GetString(1));
            }
            int? contractId = null;
            string? contractLabel = null;
            DateOnly? plannedDue = null, newDue = null;
            ServiceNextDueBasis? basis = null;
            if (value.Kind == ServiceInterventionKind.Maintenance)
            {
                var coverage = await LockCoverageAsync(connection, transaction, value.WorkPointId, beneficiaryId, cancellationToken).ConfigureAwait(false)
                    ?? throw ServiceInterventionRules.NotCovered(workPointName);
                (contractId, contractLabel) = (coverage.ContractId, coverage.Label);
                var latest = await LatestMovingAsync(connection, transaction, value.WorkPointId, null, cancellationToken).ConfigureAwait(false);
                if (ServiceInterventionRules.Moves(performedOn, latest?.PerformedOn))
                {
                    plannedDue = coverage.NextDue;
                    basis = value.Basis;
                    newDue = ServiceInterventionRules.ResolveDue(value.Basis, performedOn, coverage.NextDue, coverage.Cycle, value.ChosenDue, out var dueError)
                        ?? throw new ServiceInterventionOperationException(dueError!);
                    await SetDueAsync(connection, transaction, coverage.PointId, coverage.PointVersion, coverage.ContractId, newDue.Value, cancellationToken).ConfigureAwait(false);
                }
            }
            await using var insert = Command(connection, transaction, """
                INSERT INTO service_interventions (kind, beneficiary_id, work_point_id, contract_id, work_point_name, work_point_address, contract_label,
                    performed_on, planned_due, next_due_basis, next_due_set, notes, recorded_by, recorded_utc, version)
                VALUES (@kind, @beneficiary, @workPoint, @contract, @pointName, @pointAddress, @contractLabel, @performed, @planned, @basis, @dueSet, @notes, @recordedBy, @recorded, 0)
                """, ("@kind", ServiceInterventionRules.KindCode(value.Kind).ToString()), ("@beneficiary", beneficiaryId), ("@workPoint", value.WorkPointId),
                ("@contract", contractId), ("@pointName", workPointName), ("@pointAddress", workPointAddress), ("@contractLabel", contractLabel),
                ("@performed", SqlDate(performedOn)), ("@planned", plannedDue is { } planned ? SqlDate(planned) : null),
                ("@basis", basis is { } chosen ? ServiceInterventionRules.BasisCode(chosen).ToString() : null),
                ("@dueSet", newDue is { } set ? SqlDate(set) : null), ("@notes", value.Notes), ("@recordedBy", actor), ("@recorded", MariaTimeText.Format(recorded)));
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            var intervention = new ServiceIntervention(checked((int)insert.LastInsertedId), value.Kind, beneficiaryId, value.WorkPointId, contractId, workPointName,
                workPointAddress, contractLabel, performedOn, plannedDue, basis, newDue, value.Notes, actor, recorded, 0);
            var details = ServiceInterventionRules.Identification(intervention);
            if (value.Kind == ServiceInterventionKind.Maintenance)
                details += basis is { } used
                    ? $"; Varianta scadenței: {ServiceInterventionRules.BasisLabel(used)}; Scadența: {StockMovementRules.DisplayDate(plannedDue!.Value)} → {StockMovementRules.DisplayDate(newDue!.Value)}"
                    : "; Nu modifică scadența (există o intervenție de mentenanță mai recentă pentru punct)";
            pending.Add(new(value.Kind == ServiceInterventionKind.Maintenance ? AuditActions.RecordMaintenance : AuditActions.RecordOnDemand,
                ServiceInterventionRules.Target(intervention, ownerName), details, string.Empty));
            return new ServiceInterventionSaved(intervention, plannedDue, newDue);
        }, cancellationToken).ConfigureAwait(false);
        await WriteJournalAsync(beneficiaryId, pending, cancellationToken).ConfigureAwait(false);
        return saved;
    }

    public async Task<ServiceInterventionSaved> UpdateAsync(ServiceIntervention original, ServiceInterventionInput input, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        var value = input.Validated();
        var performedOn = value.PerformedOn!.Value;
        var pending = new List<Pending>();
        var saved = await WriteAsync(async (connection, transaction) =>
        {
            pending.Clear();
            var ownerName = await LockBeneficiaryAsync(connection, transaction, original.BeneficiaryId, cancellationToken).ConfigureAwait(false);
            ServiceInterventionRules.CheckCurrent(await GetLockedAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false), original);
            var dateChanged = performedOn != original.PerformedOn;
            var basisChanged = original.MovesDue && (value.Basis != original.Basis || (value.Basis == ServiceNextDueBasis.Chosen && value.ChosenDue != original.NextDueSet));
            if (dateChanged && ServiceInterventionRules.PerformedOnError(performedOn, Today) is { } dateError) throw new ServiceInterventionOperationException(dateError);
            var after = original with { PerformedOn = performedOn, Notes = value.Notes };
            DateOnly? newDue = original.NextDueSet;
            if (original.IsMaintenance && (dateChanged || basisChanged))
            {
                // Only the latest due-moving maintenance intervention of the point can be corrected (its choice is reopened).
                if (!original.MovesDue || await LatestMovingAsync(connection, transaction, original.WorkPointId, null, cancellationToken).ConfigureAwait(false) is not { } latest || latest.Id != original.Id)
                    throw ServiceInterventionRules.OnlyLatestCorrectable();
                if (await LatestMovingAsync(connection, transaction, original.WorkPointId, original.Id, cancellationToken).ConfigureAwait(false) is { } previous && performedOn < previous.PerformedOn)
                    throw ServiceInterventionRules.BeforePrevious(previous.PerformedOn);
                var row = await LockCoverageRowAsync(connection, transaction, original.ContractId!.Value, original.WorkPointId, cancellationToken).ConfigureAwait(false);
                if (row is null || row.NextDue != original.NextDueSet) throw ServiceInterventionRules.DueMovedMeanwhile();
                newDue = ServiceInterventionRules.ResolveDue(value.Basis, performedOn, original.PlannedDue!.Value, row.Cycle, value.ChosenDue, out var dueError)
                    ?? throw new ServiceInterventionOperationException(dueError!);
                await SetDueAsync(connection, transaction, row.PointId, row.PointVersion, row.ContractId, newDue.Value, cancellationToken).ConfigureAwait(false);
                after = after with { Basis = value.Basis, NextDueSet = newDue };
            }
            if (after == original) return new ServiceInterventionSaved(original, original.NextDueSet, original.NextDueSet);
            after = after with { Version = checked(original.Version + 1) };
            await using var update = Command(connection, transaction, """
                UPDATE service_interventions SET performed_on=@performed, next_due_basis=@basis, next_due_set=@dueSet, notes=@notes, version=@version
                WHERE id=@id AND version=@oldVersion
                """, ("@performed", SqlDate(after.PerformedOn)), ("@basis", after.Basis is { } basis ? ServiceInterventionRules.BasisCode(basis).ToString() : null),
                ("@dueSet", after.NextDueSet is { } set ? SqlDate(set) : null), ("@notes", after.Notes), ("@version", after.Version), ("@id", original.Id), ("@oldVersion", original.Version));
            if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1) throw ServiceInterventionRules.Changed();
            var details = AuditDetails.Changes([.. ServiceInterventionRules.Changes(original, after)]);
            pending.Add(new(original.IsMaintenance ? AuditActions.EditMaintenanceIntervention : AuditActions.EditOnDemandIntervention,
                ServiceInterventionRules.Target(after, ownerName), details, ServiceInterventionRules.GeneratedEditReason));
            return new ServiceInterventionSaved(after, original.NextDueSet, after.NextDueSet);
        }, cancellationToken).ConfigureAwait(false);
        await WriteJournalAsync(original.BeneficiaryId, pending, cancellationToken).ConfigureAwait(false);
        return saved;
    }

    // Archived deletion: the row goes to archive_service_interventions, its photos to archive_service_photos with their files moved to the
    // archive directory, all in one database transaction. The latest due-moving maintenance intervention gives the due date back.
    public async Task DeleteAsync(ServiceIntervention original, string reason, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        var motif = ChangeReasonRules.Normalize(reason);
        if (ChangeReasonRules.ValidationError(motif) is { } reasonError) throw new ServiceInterventionOperationException(reasonError);
        string ownerName;
        IReadOnlyList<ServicePhoto> photos;
        await using (var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false))
        {
            await using var owner = Command(connection, null, "SELECT name FROM beneficiaries WHERE id=@id", ("@id", original.BeneficiaryId));
            ownerName = await owner.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string ?? throw ServiceInterventionRules.BeneficiaryMissing();
            ServiceInterventionRules.CheckCurrent(await GetLockedAsync(connection, null, original.Id, cancellationToken, false).ConfigureAwait(false), original);
            photos = await ServicePhotoArchive.ReadAsync(connection, null, "intervention_id=@id", cancellationToken, ("@id", original.Id)).ConfigureAwait(false);
        }
        var liveRoot = MariaAssetPaths.ServicePhotos(configuration);
        var archiveRoot = MariaAssetPaths.ArchiveFiles(configuration);
        await archiver.ExecuteAsync(ArchiveRequests.ServiceIntervention(original, ownerName, photos, motif), async (operation, token) =>
        {
            var prepared = await ServicePhotoArchive.PrepareAsync(liveRoot, archiveRoot, photos, operation, token).ConfigureAwait(false);
            try
            {
                await WriteAsync(async (connection, transaction) =>
                {
                    await LockBeneficiaryAsync(connection, transaction, original.BeneficiaryId, token).ConfigureAwait(false);
                    ServiceInterventionRules.CheckCurrent(await GetLockedAsync(connection, transaction, original.Id, token).ConfigureAwait(false), original);
                    var current = await ServicePhotoArchive.ReadAsync(connection, transaction, "intervention_id=@id", token, ("@id", original.Id)).ConfigureAwait(false);
                    if (!current.Select(photo => photo.Id).Order().SequenceEqual(photos.Select(photo => photo.Id).Order())) throw ServiceInterventionRules.Changed();
                    if (original.MovesDue && await LatestMovingAsync(connection, transaction, original.WorkPointId, null, token).ConfigureAwait(false) is { } latest && latest.Id == original.Id &&
                        await LockCoverageRowAsync(connection, transaction, original.ContractId!.Value, original.WorkPointId, token).ConfigureAwait(false) is { } row && row.NextDue == original.NextDueSet)
                        await SetDueAsync(connection, transaction, row.PointId, row.PointVersion, row.ContractId, original.PlannedDue!.Value, token).ConfigureAwait(false);
                    await MariaArchivePersistence.InsertAsync(connection, transaction, operation, prepared, token).ConfigureAwait(false);
                    await using (var deletePhotos = Command(connection, transaction, "DELETE FROM service_photos WHERE intervention_id=@id", ("@id", original.Id)))
                        await deletePhotos.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    await using var command = Command(connection, transaction, "DELETE FROM service_interventions WHERE id=@id AND version=@version",
                        ("@id", original.Id), ("@version", original.Version));
                    if (await command.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1) throw ServiceInterventionRules.Changed();
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

    /// <summary>The number of interventions that reference a contract, a work point or a beneficiary (they keep it from being deleted).</summary>
    internal static async Task<int> CountAsync(MySqlConnection connection, MySqlTransaction? transaction, string column, int id, CancellationToken token)
    {
        if (column is not ("contract_id" or "work_point_id" or "beneficiary_id")) throw new ArgumentOutOfRangeException(nameof(column));
        await using var command = Command(connection, transaction, $"SELECT COUNT(*) FROM service_interventions WHERE {column}=@id", ("@id", id));
        return Convert.ToInt32(await command.ExecuteScalarAsync(token).ConfigureAwait(false));
    }

    private static async Task SetDueAsync(MySqlConnection connection, MySqlTransaction transaction, int pointId, long pointVersion, int contractId, DateOnly due, CancellationToken token)
    {
        await using (var update = Command(connection, transaction, "UPDATE service_contract_points SET next_due=@due, version=version+1 WHERE id=@id AND version=@version",
            ("@due", SqlDate(due)), ("@id", pointId), ("@version", pointVersion)))
            if (await update.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1) throw ServiceInterventionRules.DueMovedMeanwhile();
        // The coverage of the contract changed: an editor opened on the contract becomes outdated.
        await using var bump = Command(connection, transaction, "UPDATE service_contracts SET version=version+1 WHERE id=@id", ("@id", contractId));
        await bump.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    private const string CoverageSelect = """
        SELECT p.id, p.contract_id, p.cycle_months, p.next_due, p.version, c.contract_number, c.contract_date, c.cycle_months
        FROM service_contract_points p JOIN service_contracts c ON c.id = p.contract_id
        """;

    private static Coverage ReadCoverage(MySqlDataReader reader) =>
        new(checked((int)reader.GetInt64(0)), checked((int)reader.GetInt64(1)), reader.IsDBNull(2) ? null : reader.GetInt32(2), ReadDate(reader, 3), reader.GetInt64(4),
            reader.GetString(5), ReadDate(reader, 6), reader.GetInt32(7));

    // The coverage of the work point in its active contract (locked); null when no active contract covers it.
    private static async Task<Coverage?> LockCoverageAsync(MySqlConnection connection, MySqlTransaction transaction, int workPointId, int beneficiaryId, CancellationToken token)
    {
        await using var command = Command(connection, transaction, CoverageSelect + " WHERE p.active_work_point_id=@workPoint AND c.beneficiary_id=@beneficiary FOR UPDATE",
            ("@workPoint", workPointId), ("@beneficiary", beneficiaryId));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        return await reader.ReadAsync(token).ConfigureAwait(false) ? ReadCoverage(reader) : null;
    }

    // The coverage row of a work point in a given contract (locked), active or not.
    private static async Task<Coverage?> LockCoverageRowAsync(MySqlConnection connection, MySqlTransaction transaction, int contractId, int workPointId, CancellationToken token)
    {
        await using var command = Command(connection, transaction, CoverageSelect + " WHERE p.contract_id=@contract AND p.work_point_id=@workPoint FOR UPDATE",
            ("@contract", contractId), ("@workPoint", workPointId));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        return await reader.ReadAsync(token).ConfigureAwait(false) ? ReadCoverage(reader) : null;
    }

    private sealed record Latest(int Id, DateOnly PerformedOn);

    // The due-moving maintenance intervention of the point with the latest date performed (then the newest), optionally leaving one out.
    private static async Task<Latest?> LatestMovingAsync(MySqlConnection connection, MySqlTransaction transaction, int workPointId, int? exceptId, CancellationToken token)
    {
        await using var command = Command(connection, transaction, """
            SELECT id, performed_on FROM service_interventions
            WHERE work_point_id=@workPoint AND kind='M' AND next_due_basis IS NOT NULL AND (@except IS NULL OR id<>@except)
            ORDER BY performed_on DESC, id DESC LIMIT 1 FOR UPDATE
            """, ("@workPoint", workPointId), ("@except", exceptId));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        return await reader.ReadAsync(token).ConfigureAwait(false) ? new(checked((int)reader.GetInt64(0)), ReadDate(reader, 1)) : null;
    }

    private static async Task<string> LockBeneficiaryAsync(MySqlConnection connection, MySqlTransaction transaction, int beneficiaryId, CancellationToken token)
    {
        await using var command = Command(connection, transaction, "SELECT name FROM beneficiaries WHERE id=@id FOR UPDATE", ("@id", beneficiaryId));
        return await command.ExecuteScalarAsync(token).ConfigureAwait(false) as string ?? throw ServiceInterventionRules.BeneficiaryMissing();
    }

    private static async Task<ServiceIntervention?> GetLockedAsync(MySqlConnection connection, MySqlTransaction? transaction, int id, CancellationToken token, bool forUpdate = true)
    {
        await using var command = Command(connection, transaction,
            $"SELECT {Columns} FROM service_interventions WHERE id=@id" + (forUpdate ? " FOR UPDATE" : string.Empty), ("@id", id));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        return await reader.ReadAsync(token).ConfigureAwait(false) ? Read(reader) : null;
    }

    private async Task WriteJournalAsync(int beneficiaryId, IEnumerable<Pending> pending, CancellationToken token)
    {
        foreach (var item in pending)
            await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.Beneficiary, item.Action, beneficiaryId.ToString(),
                item.Target, item.Details, item.Motif, token).ConfigureAwait(false);
    }

    private Task<T> WriteAsync<T>(Func<MySqlConnection, MySqlTransaction, Task<T>> action, CancellationToken token) =>
        MariaDb.WriteAsync(configuration, action, message => new ServiceInterventionOperationException(message),
            exception => exception.Number == 1451 ? new ServiceInterventionOperationException("Intervenția nu poate fi ștearsă: are date asociate.") : null, token);

    private static ServiceIntervention Read(MySqlDataReader reader) =>
        new(checked((int)reader.GetInt64(0)), ServiceInterventionRules.ParseKind(reader.GetString(1)), checked((int)reader.GetInt64(2)), checked((int)reader.GetInt64(3)),
            reader.IsDBNull(4) ? null : checked((int)reader.GetInt64(4)), reader.GetString(5), reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7),
            ReadDate(reader, 8), reader.IsDBNull(9) ? null : ReadDate(reader, 9), reader.IsDBNull(10) ? null : ServiceInterventionRules.ParseBasis(reader.GetString(10)),
            reader.IsDBNull(11) ? null : ReadDate(reader, 11), reader.GetString(12), reader.GetString(13), MariaTimeText.Parse(reader.GetString(14)), reader.GetInt64(15));

    private static DateOnly ReadDate(MySqlDataReader reader, int ordinal) => DateOnly.FromDateTime(reader.GetDateTime(ordinal));

    private static DateTime SqlDate(DateOnly date) => date.ToDateTime(TimeOnly.MinValue);

    private Task EnsureOperatorAsync(CancellationToken token) => accessControl?.EnsureBeneficiaryOperatorAsync(token) ?? Task.CompletedTask;
}
