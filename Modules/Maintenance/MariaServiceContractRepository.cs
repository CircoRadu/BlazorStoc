using System.Data;
using MySqlConnector;
using static BlazorStoc.Services.MariaDb;

namespace BlazorStoc.Services;

// MariaDB mode: `service_contracts` and `service_contract_points` are created by migration 8 (MariaSchemaMigrations); like the other
// repositories this one never alters the schema. Every write locks the row of the beneficiary first (contracts, points and moves
// never cross beneficiaries), so two sessions working on the contracts of one beneficiary are serialized; the unique key on
// service_contract_points.active_work_point_id (one ACTIVE contract per work point) stays as the last line of defence.
public sealed partial class MariaServiceContractRepository(
    IConfiguration configuration,
    IAccessControl? accessControl = null,
    IAuditTrail? auditTrail = null,
    IArchiveService? archiveService = null) : IServiceContractRepository, IServiceContractAddendumRepository
{
    private const string ContractColumns = "id, beneficiary_id, contract_number, contract_date, cycle_months, valid_until, is_active, notes, version, reference_date, is_frozen";
    private const string PointSelect = """
        SELECT p.id, p.contract_id, p.work_point_id, p.cycle_months, p.next_due, p.version, w.name, w.address, w.is_primary
        FROM service_contract_points p JOIN beneficiary_work_points w ON w.id = p.work_point_id
        """;
    private readonly IArchiveService archiver = archiveService ?? new ArchiveService(accessControl);

    // One journal entry produced by an operation; written after the transaction commits, under the beneficiary the contract belongs to.
    private sealed record Pending(string Action, string Target, string Details, string Motif);

    public async Task<IReadOnlyList<ServiceContractDetails>> GetForBeneficiaryAsync(int beneficiaryId, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAnyPermissionAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        var contracts = new List<ServiceContract>();
        await using (var command = Command(connection, null,
            $"SELECT {ContractColumns} FROM service_contracts WHERE beneficiary_id=@id ORDER BY contract_date DESC, id DESC", ("@id", beneficiaryId)))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) contracts.Add(ReadContract(reader));
        var points = new List<ServiceContractPointView>();
        await using (var command = Command(connection, null, PointSelect + " JOIN service_contracts c ON c.id = p.contract_id WHERE c.beneficiary_id=@id ORDER BY w.is_primary DESC, w.name, p.id", ("@id", beneficiaryId)))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) points.Add(ReadPoint(reader));
        var units = await ReadUnitsAsync(connection, null, "contract_id IN (SELECT id FROM service_contracts WHERE beneficiary_id=@id)", [("@id", beneficiaryId)], cancellationToken).ConfigureAwait(false);
        var dues = await ReadDuesAsync(connection, null, "d.point_id IN (SELECT p.id FROM service_contract_points p JOIN service_contracts c ON c.id = p.contract_id WHERE c.beneficiary_id=@id)", [("@id", beneficiaryId)], cancellationToken).ConfigureAwait(false);
        return contracts.Select(contract =>
        {
            var contractPoints = points.Where(point => point.Point.ContractId == contract.Id).ToArray();
            var pointIds = contractPoints.Select(point => point.Point.Id).ToHashSet();
            return new ServiceContractDetails(contract, contractPoints, units.Where(unit => unit.ContractId == contract.Id).ToArray(), dues.Where(due => pointIds.Contains(due.PointId)).ToArray());
        }).ToArray();
    }

    public async Task<IReadOnlyList<ServiceDueRow>> GetDueListAsync(bool includeOff, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAnyPermissionAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, null, """
            SELECT c.id, c.beneficiary_id, c.contract_number, c.contract_date, c.cycle_months, c.valid_until, c.is_active, c.notes, c.version, c.reference_date, c.is_frozen,
                   p.id, p.contract_id, p.work_point_id, p.cycle_months, p.next_due, p.version, w.name, w.address, w.is_primary, b.name,
                   w.latitude, w.longitude,
                   (SELECT MAX(i.performed_on) FROM service_interventions i WHERE i.work_point_id = p.work_point_id AND i.kind = 'M')
            FROM service_contract_points p
            JOIN service_contracts c ON c.id = p.contract_id
            JOIN beneficiary_work_points w ON w.id = p.work_point_id
            JOIN beneficiaries b ON b.id = c.beneficiary_id
            WHERE c.is_frozen = 0 AND (c.is_active = 1 OR @includeOff = 1)
            ORDER BY p.next_due, b.name, w.name, p.id
            """, ("@includeOff", includeOff ? 1 : 0));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var rows = new List<ServiceDueRow>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            rows.Add(new(checked((int)reader.GetInt64(1)), reader.GetString(20), ReadContract(reader),
                new(new(checked((int)reader.GetInt64(11)), checked((int)reader.GetInt64(12)), checked((int)reader.GetInt64(13)),
                        reader.IsDBNull(14) ? null : reader.GetInt32(14), ReadDate(reader, 15), reader.GetInt64(16)),
                    reader.GetString(17), reader.GetString(18), reader.GetInt32(19) != 0),
                reader.IsDBNull(21) ? null : reader.GetDecimal(21), reader.IsDBNull(22) ? null : reader.GetDecimal(22), reader.IsDBNull(23) ? null : ReadDate(reader, 23)));
        await reader.CloseAsync().ConfigureAwait(false);
        var dues = await ReadDuesAsync(connection, null, "1=1", [], cancellationToken).ConfigureAwait(false);
        return [.. rows.Select(row => row with { UnitDueList = dues.Where(due => due.PointId == row.Point.Point.Id).ToArray() })];
    }

    public async Task<IReadOnlyList<ServiceContractFrozenItem>> GetFrozenAsync(CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAnyPermissionAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        var contracts = new List<(ServiceContract Contract, string Beneficiary)>();
        await using (var command = Command(connection, null,
            $"SELECT {string.Join(", ", ContractColumns.Split(", ").Select(column => "c." + column))}, b.name FROM service_contracts c JOIN beneficiaries b ON b.id = c.beneficiary_id WHERE c.is_frozen = 1 ORDER BY c.is_active DESC, b.name, c.contract_date DESC, c.id DESC"))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) contracts.Add((ReadContract(reader), reader.GetString(11)));
        if (contracts.Count == 0) return [];
        var ids = string.Join(",", contracts.Select(item => item.Contract.Id));
        var points = new List<ServiceContractPointView>();
        await using (var command = Command(connection, null, PointSelect + $" WHERE p.contract_id IN ({ids}) ORDER BY w.is_primary DESC, w.name, p.id"))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) points.Add(ReadPoint(reader));
        var units = await ReadUnitsAsync(connection, null, $"contract_id IN ({ids})", [], cancellationToken).ConfigureAwait(false);
        return [.. contracts.Select(item => new ServiceContractFrozenItem(item.Contract.BeneficiaryId, item.Beneficiary,
            new(item.Contract, points.Where(point => point.Point.ContractId == item.Contract.Id).ToArray(), units.Where(unit => unit.ContractId == item.Contract.Id).ToArray())))];
    }

    public async Task<int> CountFrozenAsync(CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAnyPermissionAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, null, "SELECT COUNT(*) FROM service_contracts WHERE is_frozen = 1 AND is_active = 1");
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
    }

    public async Task<ServiceContractDetails> CreateAsync(int beneficiaryId, ServiceContractInput input, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAsync("mentenanta.add", cancellationToken).ConfigureAwait(false);
        var value = input.Validated();
        var pending = new List<Pending>();
        var details = await WriteAsync(async (connection, transaction) =>
        {
            pending.Clear();
            var ownerName = await LockBeneficiaryAsync(connection, transaction, beneficiaryId, cancellationToken).ConfigureAwait(false);
            var label = ServiceContractNumber.Format(value.Number, value.Date);
            await EnsureUniqueNumberAsync(connection, transaction, beneficiaryId, value.Number, value.Date, null, label, cancellationToken).ConfigureAwait(false);
            await using var insert = Command(connection, transaction, """
                INSERT INTO service_contracts (beneficiary_id, contract_number, contract_date, cycle_months, valid_until, is_active, notes, version, reference_date, is_frozen, missing_info)
                VALUES (@beneficiaryId, @number, @date, @cycle, @validUntil, 1, @notes, 0, @reference, 1, '')
                """, ("@beneficiaryId", beneficiaryId), ("@number", value.Number), ("@date", SqlDate(value.Date)), ("@cycle", value.CycleMonths),
                ("@validUntil", value.ValidUntil is { } validUntil ? SqlDate(validUntil) : null), ("@notes", value.Notes),
                ("@reference", value.ReferenceDate is { } reference ? SqlDate(reference) : null));
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            var contract = new ServiceContract(checked((int)insert.LastInsertedId), beneficiaryId, value.Number, value.Date, value.CycleMonths,
                value.ValidUntil, true, value.Notes, 0, value.ReferenceDate, true);
            var target = ServiceContractRules.Target(beneficiaryId, ownerName, label);
            var catalog = await LoadCatalogAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            var createEntry = new Pending(AuditActions.CreateServiceContract, target, ServiceContractRules.Identification(contract), string.Empty);
            pending.Add(createEntry);
            await ApplyUnitsAsync(connection, transaction, contract.Id, target, value.Units, [], catalog, null, cancellationToken).ConfigureAwait(false);
            await ApplyPointsAsync(connection, transaction, contract, target, value.Points, [], pending, cancellationToken).ConfigureAwait(false);
            var state = await RefreshFrozenAsync(connection, transaction, contract, catalog, cancellationToken, store: true).ConfigureAwait(false);
            await SyncDuesAsync(connection, transaction, contract.Id, cancellationToken).ConfigureAwait(false);
            pending[0] = createEntry with { Details = createEntry.Details + await UnitsSummaryAsync(connection, transaction, contract.Id, catalog, cancellationToken).ConfigureAwait(false) +
                (state.Frozen ? "; Stare: înghețat (lipsă: " + ServiceContractFreeze.Summary(state.Missing) + ")" : string.Empty) };
            return await LoadDetailsAsync(connection, transaction, contract.Id, cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
        await RecordAsync(beneficiaryId, pending, cancellationToken).ConfigureAwait(false);
        return details;
    }

    public async Task<ServiceContractDetails> UpdateAsync(ServiceContract original, ServiceContractInput input, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAsync("mentenanta.edit", cancellationToken).ConfigureAwait(false);
        var value = input.Validated();
        var pending = new List<Pending>();
        var details = await WriteAsync(async (connection, transaction) =>
        {
            pending.Clear();
            var ownerName = await LockBeneficiaryAsync(connection, transaction, original.BeneficiaryId, cancellationToken).ConfigureAwait(false);
            ServiceContractRules.CheckCurrent(await GetLockedAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false), original);
            var label = ServiceContractNumber.Format(value.Number, value.Date);
            await EnsureUniqueNumberAsync(connection, transaction, original.BeneficiaryId, value.Number, value.Date, original.Id, label, cancellationToken).ConfigureAwait(false);
            var current = await LockPointsAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false);
            var currentUnits = await ReadUnitsAsync(connection, transaction, "contract_id=@id FOR UPDATE", [("@id", original.Id)], cancellationToken).ConfigureAwait(false);
            var catalog = await LoadCatalogAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            var after = original with { Number = value.Number, Date = value.Date, CycleMonths = value.CycleMonths, ValidUntil = value.ValidUntil, Notes = value.Notes, ReferenceDate = value.ReferenceDate };
            var target = ServiceContractRules.Target(original.BeneficiaryId, ownerName, after.Label);
            var changed = after != original;
            if (changed)
                pending.Add(new(ServiceContractRules.EditAction(original, after), target,
                    AuditDetails.Changes([.. ServiceContractRules.Changes(original, after)]), ServiceContractRules.GeneratedEditReason));
            var unitsChanged = await ApplyUnitsAsync(connection, transaction, original.Id, target, value.Units, currentUnits, catalog, pending, cancellationToken).ConfigureAwait(false);
            var coverageChanged = await ApplyPointsAsync(connection, transaction, after, target, value.Points, current, pending, cancellationToken).ConfigureAwait(false);
            if (changed || coverageChanged || unitsChanged)
            {
                var state = await RefreshFrozenAsync(connection, transaction, after, catalog, cancellationToken).ConfigureAwait(false);
                if (state.Frozen != original.IsFrozen)
                    pending.Add(new(state.Frozen ? AuditActions.FreezeServiceContract : AuditActions.UnfreezeServiceContract, target,
                        AuditDetails.Changes(new AuditChange("Stare", original.IsFrozen ? "Înghețat" : "Activ", state.Frozen ? "Înghețat" : "Activ")) +
                        (state.Frozen ? "; Lipsă: " + ServiceContractFreeze.Summary(state.Missing) : string.Empty),
                        state.Frozen ? "Date incomplete în contract" : "Datele contractului au fost completate"));
                await using var update = Command(connection, transaction, """
                    UPDATE service_contracts SET contract_number=@number, contract_date=@date, cycle_months=@cycle, valid_until=@validUntil, notes=@notes, version=@version,
                        reference_date=@reference, is_frozen=@frozen, missing_info=@missing
                    WHERE id=@id AND version=@oldVersion
                    """, ("@number", after.Number), ("@date", SqlDate(after.Date)), ("@cycle", after.CycleMonths),
                    ("@validUntil", after.ValidUntil is { } validUntil ? SqlDate(validUntil) : null), ("@notes", after.Notes),
                    ("@reference", after.ReferenceDate is { } reference ? SqlDate(reference) : null), ("@frozen", state.Frozen ? 1 : 0),
                    ("@missing", ServiceContractFreeze.Stored(state.Missing)),
                    ("@version", checked(original.Version + 1)), ("@id", original.Id), ("@oldVersion", original.Version));
                if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1) throw ServiceContractRules.Changed();
            }
            await SyncDuesAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false);
            return await LoadDetailsAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
        await RecordAsync(original.BeneficiaryId, pending, cancellationToken).ConfigureAwait(false);
        return details;
    }

    public async Task<ServiceContractActivationPlan> PrepareActivationAsync(int contractId, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAnyPermissionAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        var details = await LoadDetailsAsync(connection, null, contractId, cancellationToken).ConfigureAwait(false);
        var conflicts = new List<ServiceContractConflict>();
        foreach (var point in details.Points)
            if (await FindActiveCoverageAsync(connection, null, point.Point.WorkPointId, details.Contract.Id, false, cancellationToken).ConfigureAwait(false) is { } other)
                conflicts.Add(new(point.Point.WorkPointId, point.WorkPointName, other.ContractId, other.Label));
        return new(details, conflicts);
    }

    public async Task<ServiceContractDetails> ActivateAsync(ServiceContract original, IReadOnlyList<ServiceContractReschedule> reschedules,
        bool removeConflicting, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAsync("mentenanta.edit", cancellationToken).ConfigureAwait(false);
        foreach (var reschedule in reschedules)
            if (!ServiceContractRules.InRange(reschedule.NextDue)) throw new ServiceContractOperationException("Una dintre noile scadențe nu este o dată validă.");
        var pending = new List<Pending>();
        var details = await WriteAsync(async (connection, transaction) =>
        {
            pending.Clear();
            var ownerName = await LockBeneficiaryAsync(connection, transaction, original.BeneficiaryId, cancellationToken).ConfigureAwait(false);
            var current = await GetLockedAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false);
            ServiceContractRules.CheckCurrent(current, original);
            if (original.IsActive) throw ServiceContractRules.Changed();
            var target = ServiceContractRules.Target(original.BeneficiaryId, ownerName, original.Label);
            var rows = (await LockPointsAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false)).ToList();
            var conflicts = new List<ServiceContractConflict>();
            foreach (var row in rows)
                if (await FindActiveCoverageAsync(connection, transaction, row.Point.WorkPointId, original.Id, true, cancellationToken).ConfigureAwait(false) is { } other)
                    conflicts.Add(new(row.Point.WorkPointId, row.WorkPointName, other.ContractId, other.Label));
            if (conflicts.Count > 0 && !removeConflicting) throw ServiceContractRules.ActivationBlocked(conflicts);
            foreach (var conflict in conflicts)
            {
                var row = rows.Single(item => item.Point.WorkPointId == conflict.WorkPointId);
                await DeleteRowAsync(connection, transaction, row.Point.Id, cancellationToken).ConfigureAwait(false);
                rows.Remove(row);
                pending.Add(new(AuditActions.RemoveContractPoint, target, PointDetails(row.WorkPointName, original.Label, row.Point.NextDue) +
                    $"; Motiv: punctul este în contractul activ {conflict.ContractLabel}", ServiceContractRules.GeneratedActivationReason));
            }
            var byWorkPoint = reschedules.GroupBy(item => item.WorkPointId).ToDictionary(group => group.Key, group => group.Last().NextDue);
            var summary = new List<string>();
            foreach (var row in rows)
            {
                var due = byWorkPoint.TryGetValue(row.Point.WorkPointId, out var chosen) ? chosen : row.Point.NextDue;
                await using var update = Command(connection, transaction,
                    "UPDATE service_contract_points SET active_work_point_id=work_point_id, next_due=@due, version=version+1 WHERE id=@id",
                    ("@due", SqlDate(due)), ("@id", row.Point.Id));
                await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                await using (var resetDues = Command(connection, transaction, "UPDATE service_contract_unit_dues SET next_due=@due, version=version+1 WHERE point_id=@id", ("@due", SqlDate(due)), ("@id", row.Point.Id)))
                    await resetDues.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                summary.Add(due == row.Point.NextDue
                    ? $"{row.WorkPointName} ({StockMovementRules.DisplayDate(due)})"
                    : $"{row.WorkPointName} ({StockMovementRules.DisplayDate(row.Point.NextDue)} → {StockMovementRules.DisplayDate(due)})");
                if (due != row.Point.NextDue)
                    pending.Add(new(AuditActions.RescheduleMaintenance, target, PointDetails(row.WorkPointName, original.Label, null) + "; " +
                        AuditDetails.Changes(new AuditChange("Scadență", StockMovementRules.DisplayDate(row.Point.NextDue), StockMovementRules.DisplayDate(due))) +
                        "; Motiv: reactivare contract", ServiceContractRules.GeneratedActivationReason));
            }
            await SetActiveAsync(connection, transaction, original, true, cancellationToken).ConfigureAwait(false);
            await SyncDuesAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false);
            pending.Insert(0, new(AuditActions.ActivateServiceContract, target, AuditDetails.Changes(new AuditChange("Stare", "Off", "On")) +
                (summary.Count > 0 ? "; Puncte: " + string.Join(", ", summary) : string.Empty), ServiceContractRules.GeneratedActivationReason));
            return await LoadDetailsAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
        await RecordAsync(original.BeneficiaryId, pending, cancellationToken).ConfigureAwait(false);
        return details;
    }

    public async Task<ServiceContractDetails> DeactivateAsync(ServiceContract original, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAsync("mentenanta.edit", cancellationToken).ConfigureAwait(false);
        var pending = new List<Pending>();
        var details = await WriteAsync(async (connection, transaction) =>
        {
            pending.Clear();
            var ownerName = await LockBeneficiaryAsync(connection, transaction, original.BeneficiaryId, cancellationToken).ConfigureAwait(false);
            ServiceContractRules.CheckCurrent(await GetLockedAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false), original);
            if (!original.IsActive) throw ServiceContractRules.Changed();
            await LockPointsAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false);
            await using (var release = Command(connection, transaction,
                "UPDATE service_contract_points SET active_work_point_id=NULL WHERE contract_id=@id", ("@id", original.Id)))
                await release.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await SetActiveAsync(connection, transaction, original, false, cancellationToken).ConfigureAwait(false);
            pending.Add(new(AuditActions.DeactivateServiceContract, ServiceContractRules.Target(original.BeneficiaryId, ownerName, original.Label),
                AuditDetails.Changes(new AuditChange("Stare", "On", "Off")), ServiceContractRules.GeneratedDeactivationReason));
            return await LoadDetailsAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
        await RecordAsync(original.BeneficiaryId, pending, cancellationToken).ConfigureAwait(false);
        return details;
    }

    // Archived deletion: the contract goes to archive_service_contracts and its coverage to archive_relations, in one transaction.
    public async Task DeleteAsync(ServiceContract original, string reason, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAsync("mentenanta.delete", cancellationToken).ConfigureAwait(false);
        var motif = ChangeReasonRules.Normalize(reason);
        if (ChangeReasonRules.ValidationError(motif) is { } reasonError) throw new ServiceContractOperationException(reasonError);
        ServiceContractDetails details;
        string ownerName;
        await using (var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false))
        {
            details = await LoadDetailsAsync(connection, null, original.Id, cancellationToken).ConfigureAwait(false);
            if (await MariaServiceInterventionRepository.CountAsync(connection, null, "contract_id", original.Id, cancellationToken).ConfigureAwait(false) is var interventions and > 0)
                throw ServiceInterventionRules.ContractHasInterventions(interventions);
            await using var owner = Command(connection, null, "SELECT name FROM beneficiaries WHERE id=@id", ("@id", original.BeneficiaryId));
            ownerName = await owner.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string ?? throw ServiceContractRules.BeneficiaryMissing();
            await using var addenda = Command(connection, null, "SELECT COUNT(*) FROM service_contract_addenda WHERE contract_id=@id", ("@id", original.Id));
            if (Convert.ToInt32(await addenda.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) is var addendaCount and > 0) throw ServiceContractAddendumRules.ContractHasAddenda(addendaCount);
        }
        if (details.Contract != original) throw ServiceContractRules.Changed();
        await archiver.ExecuteAsync(ArchiveRequests.ServiceContract(details, ownerName, motif), async (operation, token) =>
        {
            await WriteAsync(async (connection, transaction) =>
            {
                await LockBeneficiaryAsync(connection, transaction, original.BeneficiaryId, token).ConfigureAwait(false);
                ServiceContractRules.CheckCurrent(await GetLockedAsync(connection, transaction, original.Id, token).ConfigureAwait(false), original);
                if (await MariaServiceInterventionRepository.CountAsync(connection, transaction, "contract_id", original.Id, token).ConfigureAwait(false) is var interventionsNow and > 0)
                    throw ServiceInterventionRules.ContractHasInterventions(interventionsNow);
                var current = await LockPointsAsync(connection, transaction, original.Id, token).ConfigureAwait(false);
                if (!current.Select(row => row.Point.Id).Order().SequenceEqual(details.Points.Select(row => row.Point.Id).Order())) throw ServiceContractRules.Changed();
                await MariaArchivePersistence.InsertAsync(connection, transaction, operation, [], token).ConfigureAwait(false);
                await using (var deletePoints = Command(connection, transaction, "DELETE FROM service_contract_points WHERE contract_id=@id", ("@id", original.Id)))
                    await deletePoints.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                await using (var deleteUnits = Command(connection, transaction, "DELETE FROM service_contract_units WHERE contract_id=@id", ("@id", original.Id)))
                    await deleteUnits.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                await using var command = Command(connection, transaction,
                    "DELETE FROM service_contracts WHERE id=@id AND version=@version", ("@id", original.Id), ("@version", original.Version));
                if (await command.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1) throw ServiceContractRules.Changed();
                await MariaArchivePersistence.InsertAuditAsync(connection, transaction, operation, token).ConfigureAwait(false);
                return true;
            }, token).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    // Brings the stored coverage of the contract to the desired one (add, move in, remove, change cycle or due date) and queues a
    // journal entry per operation. `current` holds the rows already in the contract (locked). Returns whether anything changed.
    private async Task<bool> ApplyPointsAsync(MySqlConnection connection, MySqlTransaction transaction, ServiceContract contract, string target,
        IReadOnlyList<ServiceContractPointInput> desired, IReadOnlyList<ServiceContractPointView> current, List<Pending> pending, CancellationToken token)
    {
        var changed = false;
        var currentByWorkPoint = current.ToDictionary(row => row.Point.WorkPointId);
        foreach (var wanted in desired)
        {
            if (currentByWorkPoint.TryGetValue(wanted.WorkPointId, out var row))
            {
                var cycleChanged = wanted.CycleMonths != row.Point.CycleMonths;
                var due = wanted.NextDue ?? row.Point.NextDue;
                var dueChanged = due != row.Point.NextDue;
                if (!cycleChanged && !dueChanged) continue;
                await using var update = Command(connection, transaction,
                    "UPDATE service_contract_points SET cycle_months=@cycle, next_due=@due, version=version+1 WHERE id=@id AND version=@version",
                    ("@cycle", wanted.CycleMonths), ("@due", SqlDate(due)), ("@id", row.Point.Id), ("@version", row.Point.Version));
                if (await update.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1) throw ServiceContractRules.Changed();
                changed = true;
                // An explicit due date for the point is the date of every unit that covers it.
                if (dueChanged)
                    await using (var setDues = Command(connection, transaction, "UPDATE service_contract_unit_dues SET next_due=@due, version=version+1 WHERE point_id=@id", ("@due", SqlDate(due)), ("@id", row.Point.Id)))
                        await setDues.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                if (cycleChanged)
                    pending.Add(new(AuditActions.EditMaintenanceCycle, target, PointDetails(row.WorkPointName, contract.Label, null) + "; " +
                        AuditDetails.Changes(new AuditChange("Ciclicitate", ServiceContractRules.PointCycleText(row.Point.CycleMonths, contract.CycleMonths),
                            ServiceContractRules.PointCycleText(wanted.CycleMonths, contract.CycleMonths))), ServiceContractRules.GeneratedEditReason));
                if (dueChanged)
                    pending.Add(new(AuditActions.RescheduleMaintenance, target, PointDetails(row.WorkPointName, contract.Label, null) + "; " +
                        AuditDetails.Changes(new AuditChange("Scadență", StockMovementRules.DisplayDate(row.Point.NextDue), StockMovementRules.DisplayDate(due))),
                        ServiceContractRules.GeneratedEditReason));
                continue;
            }

            string? workPointName = null;
            await using (var lookup = Command(connection, transaction,
                "SELECT name FROM beneficiary_work_points WHERE id=@id AND beneficiary_id=@beneficiaryId", ("@id", wanted.WorkPointId), ("@beneficiaryId", contract.BeneficiaryId)))
                workPointName = await lookup.ExecuteScalarAsync(token).ConfigureAwait(false) as string;
            if (workPointName is null) throw ServiceContractRules.WorkPointNotOfBeneficiary();

            if (contract.IsActive &&
                await FindActiveCoverageAsync(connection, transaction, wanted.WorkPointId, contract.Id, true, token).ConfigureAwait(false) is { } other)
            {
                if (!wanted.MoveFromOtherContract) throw ServiceContractRules.PointTaken(workPointName, other.Label);
                // The coverage row moves as it is (due date, individual cycle and identity are kept); both contracts change version.
                await using (var move = Command(connection, transaction,
                    "UPDATE service_contract_points SET contract_id=@contract, version=version+1 WHERE id=@id", ("@contract", contract.Id), ("@id", other.PointId)))
                    await move.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                await using (var bump = Command(connection, transaction, "UPDATE service_contracts SET version=version+1 WHERE id=@id", ("@id", other.ContractId)))
                    await bump.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                // The dates per unit belonged to the units of the other contract: they are made again from the units of this one (the date of the point is kept).
                await using (var dropDues = Command(connection, transaction, "DELETE FROM service_contract_unit_dues WHERE point_id=@id", ("@id", other.PointId)))
                    await dropDues.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                changed = true;
                pending.Add(new(AuditActions.MoveContractPoint, target, AuditDetails.Identification(("Punct de lucru", workPointName), ("Din contractul", other.Label),
                    ("În contractul", contract.Label), ("Scadență păstrată", StockMovementRules.DisplayDate(other.NextDue))), string.Empty));
                continue;
            }

            if (wanted.NextDue is not { } firstDue) throw new ServiceContractOperationException("Completează data primei intervenții pentru fiecare punct de lucru din contract.");
            await using (var insert = Command(connection, transaction, """
                INSERT INTO service_contract_points (contract_id, work_point_id, active_work_point_id, cycle_months, next_due, version)
                VALUES (@contract, @workPoint, @active, @cycle, @due, 0)
                """, ("@contract", contract.Id), ("@workPoint", wanted.WorkPointId), ("@active", contract.IsActive ? wanted.WorkPointId : null),
                ("@cycle", wanted.CycleMonths), ("@due", SqlDate(firstDue))))
                await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            changed = true;
            pending.Add(new(AuditActions.AddContractPoint, target, PointDetails(workPointName, contract.Label, firstDue) +
                $"; Ciclicitate: {ServiceContractRules.PointCycleText(wanted.CycleMonths, contract.CycleMonths)}", string.Empty));
        }

        var desiredIds = desired.Select(item => item.WorkPointId).ToHashSet();
        foreach (var row in current.Where(item => !desiredIds.Contains(item.Point.WorkPointId)))
        {
            await DeleteRowAsync(connection, transaction, row.Point.Id, token).ConfigureAwait(false);
            changed = true;
            pending.Add(new(AuditActions.RemoveContractPoint, target, PointDetails(row.WorkPointName, contract.Label, row.Point.NextDue), ServiceContractRules.GeneratedEditReason));
        }
        return changed;
    }

    private static string PointDetails(string workPointName, string contractLabel, DateOnly? nextDue) => AuditDetails.Identification(
        new (string Field, string Value)[]
        {
            ("Punct de lucru", workPointName), ("Contract", contractLabel),
            ("Scadență", nextDue is { } due ? StockMovementRules.DisplayDate(due) : string.Empty)
        }.Where(item => item.Value.Length > 0).ToArray());

    private static async Task DeleteRowAsync(MySqlConnection connection, MySqlTransaction transaction, int pointId, CancellationToken token)
    {
        await using var delete = Command(connection, transaction, "DELETE FROM service_contract_points WHERE id=@id", ("@id", pointId));
        await delete.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    private static async Task SetActiveAsync(MySqlConnection connection, MySqlTransaction transaction, ServiceContract original, bool active, CancellationToken token)
    {
        await using var update = Command(connection, transaction,
            "UPDATE service_contracts SET is_active=@active, version=@version WHERE id=@id AND version=@oldVersion",
            ("@active", active ? 1 : 0), ("@version", checked(original.Version + 1)), ("@id", original.Id), ("@oldVersion", original.Version));
        if (await update.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1) throw ServiceContractRules.Changed();
    }

    private sealed record ActiveCoverage(int PointId, int ContractId, string Label, DateOnly NextDue);

    // The coverage row of the work point in an ACTIVE contract other than `exceptContractId` (null when there is none).
    private static async Task<ActiveCoverage?> FindActiveCoverageAsync(MySqlConnection connection, MySqlTransaction? transaction, int workPointId,
        int exceptContractId, bool forUpdate, CancellationToken token)
    {
        await using var command = Command(connection, transaction, """
            SELECT p.id, p.contract_id, c.contract_number, c.contract_date, p.next_due
            FROM service_contract_points p JOIN service_contracts c ON c.id = p.contract_id
            WHERE p.active_work_point_id=@workPoint AND p.contract_id<>@contract
            """ + (forUpdate ? " FOR UPDATE" : string.Empty), ("@workPoint", workPointId), ("@contract", exceptContractId));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        return await reader.ReadAsync(token).ConfigureAwait(false)
            ? new(checked((int)reader.GetInt64(0)), checked((int)reader.GetInt64(1)),
                ServiceContractNumber.Format(reader.GetString(2), ReadDate(reader, 3)), ReadDate(reader, 4))
            : null;
    }

    /// <summary>The contract that covers a work point (an active one first), as a label; null when none does. Used to refuse deleting a covered point.</summary>
    internal static async Task<string?> CoverageLabelAsync(MySqlConnection connection, MySqlTransaction? transaction, int workPointId, CancellationToken token)
    {
        await using var command = Command(connection, transaction, """
            SELECT c.contract_number, c.contract_date
            FROM service_contract_points p JOIN service_contracts c ON c.id = p.contract_id
            WHERE p.work_point_id=@id ORDER BY c.is_active DESC, c.contract_date DESC, c.id DESC LIMIT 1
            """, ("@id", workPointId));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        return await reader.ReadAsync(token).ConfigureAwait(false) ? ServiceContractNumber.Format(reader.GetString(0), ReadDate(reader, 1)) : null;
    }

    /// <summary>The number of contracts of a beneficiary (contracts, On or Off, keep it from being deleted).</summary>
    internal static async Task<int> CountForBeneficiaryAsync(MySqlConnection connection, MySqlTransaction? transaction, int beneficiaryId, CancellationToken token)
    {
        await using var command = Command(connection, transaction, "SELECT COUNT(*) FROM service_contracts WHERE beneficiary_id=@id", ("@id", beneficiaryId));
        return Convert.ToInt32(await command.ExecuteScalarAsync(token).ConfigureAwait(false));
    }

    private static async Task EnsureUniqueNumberAsync(MySqlConnection connection, MySqlTransaction transaction, int beneficiaryId, string number,
        DateOnly date, int? excludedId, string label, CancellationToken token)
    {
        await using var command = Command(connection, transaction, """
            SELECT id FROM service_contracts WHERE beneficiary_id=@beneficiaryId AND contract_number=@number AND contract_date=@date AND (@id IS NULL OR id<>@id) LIMIT 1
            """, ("@beneficiaryId", beneficiaryId), ("@number", number), ("@date", SqlDate(date)), ("@id", excludedId));
        if (await command.ExecuteScalarAsync(token).ConfigureAwait(false) is not null) throw ServiceContractRules.Duplicate(label);
    }

    private static async Task<string> LockBeneficiaryAsync(MySqlConnection connection, MySqlTransaction transaction, int beneficiaryId, CancellationToken token)
    {
        await using var command = Command(connection, transaction, "SELECT name FROM beneficiaries WHERE id=@id FOR UPDATE", ("@id", beneficiaryId));
        return await command.ExecuteScalarAsync(token).ConfigureAwait(false) as string ?? throw ServiceContractRules.BeneficiaryMissing();
    }

    private static async Task<ServiceContract?> GetLockedAsync(MySqlConnection connection, MySqlTransaction transaction, int id, CancellationToken token)
    {
        await using var command = Command(connection, transaction, $"SELECT {ContractColumns} FROM service_contracts WHERE id=@id FOR UPDATE", ("@id", id));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        return await reader.ReadAsync(token).ConfigureAwait(false) ? ReadContract(reader) : null;
    }

    // Locks the coverage rows of the contract, then reads them with the names of their work points.
    private static async Task<IReadOnlyList<ServiceContractPointView>> LockPointsAsync(MySqlConnection connection, MySqlTransaction transaction, int contractId, CancellationToken token)
    {
        await using (var lockCommand = Command(connection, transaction, "SELECT id FROM service_contract_points WHERE contract_id=@id FOR UPDATE", ("@id", contractId)))
        await using (var lockReader = await lockCommand.ExecuteReaderAsync(token).ConfigureAwait(false))
            while (await lockReader.ReadAsync(token).ConfigureAwait(false)) { }
        return await ReadPointsAsync(connection, transaction, contractId, token).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<ServiceContractPointView>> ReadPointsAsync(MySqlConnection connection, MySqlTransaction? transaction, int contractId, CancellationToken token)
    {
        var points = new List<ServiceContractPointView>();
        await using var command = Command(connection, transaction, PointSelect + " WHERE p.contract_id=@id ORDER BY w.is_primary DESC, w.name, p.id", ("@id", contractId));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false)) points.Add(ReadPoint(reader));
        return points;
    }

    private static async Task<ServiceContractDetails> LoadDetailsAsync(MySqlConnection connection, MySqlTransaction? transaction, int contractId, CancellationToken token)
    {
        ServiceContract? contract;
        await using (var command = Command(connection, transaction, $"SELECT {ContractColumns} FROM service_contracts WHERE id=@id", ("@id", contractId)))
        await using (var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            contract = await reader.ReadAsync(token).ConfigureAwait(false) ? ReadContract(reader) : null;
        if (contract is null) throw ServiceContractRules.Changed();
        return new(contract, await ReadPointsAsync(connection, transaction, contractId, token).ConfigureAwait(false),
            await ReadUnitsAsync(connection, transaction, "contract_id=@id", [("@id", contractId)], token).ConfigureAwait(false),
            await ReadDuesAsync(connection, transaction, "d.point_id IN (SELECT id FROM service_contract_points WHERE contract_id=@id)", [("@id", contractId)], token).ConfigureAwait(false));
    }

    private async Task RecordAsync(int beneficiaryId, IEnumerable<Pending> pending, CancellationToken token)
    {
        foreach (var item in pending)
            await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.Beneficiary, item.Action, beneficiaryId.ToString(),
                item.Target, item.Details, item.Motif, token).ConfigureAwait(false);
    }

    private Task<T> WriteAsync<T>(Func<MySqlConnection, MySqlTransaction, Task<T>> action, CancellationToken token) =>
        MariaDb.WriteAsync(configuration, action, message => new ServiceContractOperationException(message), Translate, token);

    // A concurrent operation passed the checks and hit a unique key (the checks run under the beneficiary lock, so this is rare).
    private static Exception? Translate(MySqlException exception) => exception.Number switch
    {
        1062 => exception.Message.Contains("uq_service_contracts_number", StringComparison.Ordinal)
            ? new ServiceContractOperationException("Beneficiarul are deja un contract cu acest număr și această dată.")
            : new ServiceContractOperationException("Un punct de lucru din contract este deja într-un contract activ sau în acest contract. Actualizează pagina și reia operația."),
        1451 => new ServiceContractOperationException("Contractul nu poate fi șters: are date asociate."),
        _ => null
    };

    private static ServiceContract ReadContract(MySqlDataReader reader) =>
        new(checked((int)reader.GetInt64(0)), checked((int)reader.GetInt64(1)), reader.GetString(2), ReadDate(reader, 3), reader.IsDBNull(4) ? null : reader.GetInt32(4),
            reader.IsDBNull(5) ? null : ReadDate(reader, 5), reader.GetInt32(6) != 0, reader.GetString(7), reader.GetInt64(8),
            reader.IsDBNull(9) ? null : ReadDate(reader, 9), reader.GetInt32(10) != 0);

    private static ServiceContractPointView ReadPoint(MySqlDataReader reader) =>
        new(new(checked((int)reader.GetInt64(0)), checked((int)reader.GetInt64(1)), checked((int)reader.GetInt64(2)),
                reader.IsDBNull(3) ? null : reader.GetInt32(3), ReadDate(reader, 4), reader.GetInt64(5)),
            reader.GetString(6), reader.GetString(7), reader.GetInt32(8) != 0);

    private static DateOnly ReadDate(MySqlDataReader reader, int ordinal) => DateOnly.FromDateTime(reader.GetDateTime(ordinal));

    private static DateTime SqlDate(DateOnly date) => date.ToDateTime(TimeOnly.MinValue);

    // ---- units (families / discrete components) of a contract ----

    private sealed record CatalogNames(IReadOnlyDictionary<int, (string Name, bool Active)> Families, IReadOnlyDictionary<int, (string Name, bool Active, int? FamilyId)> Components);

    private static async Task<CatalogNames> LoadCatalogAsync(MySqlConnection connection, MySqlTransaction? transaction, CancellationToken token)
    {
        var families = new Dictionary<int, (string, bool)>();
        var components = new Dictionary<int, (string, bool, int?)>();
        await using (var command = Command(connection, transaction, "SELECT id, name, active FROM maintenance_families"))
        await using (var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            while (await reader.ReadAsync(token).ConfigureAwait(false)) families[checked((int)reader.GetInt64(0))] = (reader.GetString(1), reader.GetInt32(2) != 0);
        await using (var command = Command(connection, transaction, "SELECT id, name, active, family_id FROM maintenance_components"))
        await using (var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            while (await reader.ReadAsync(token).ConfigureAwait(false))
                components[checked((int)reader.GetInt64(0))] = (reader.GetString(1), reader.GetInt32(2) != 0, reader.IsDBNull(3) ? null : checked((int)reader.GetInt64(3)));
        return new(families, components);
    }

    private static string FamilyName(CatalogNames catalog, int id) => catalog.Families.TryGetValue(id, out var family) ? family.Name : $"familia #{id}";
    private static string ComponentName(CatalogNames catalog, int id) => catalog.Components.TryGetValue(id, out var component) ? component.Name : $"componenta #{id}";
    private static string UnitName(CatalogNames catalog, ServiceContractUnit unit) => unit.FamilyId is { } family ? FamilyName(catalog, family) : ComponentName(catalog, unit.ComponentId ?? 0);

    // Reads the units that match the condition (a WHERE clause on service_contract_units, optionally ending in FOR UPDATE) with their members.
    private static async Task<IReadOnlyList<ServiceContractUnit>> ReadUnitsAsync(MySqlConnection connection, MySqlTransaction? transaction, string condition,
        (string Name, object? Value)[] parameters, CancellationToken token)
    {
        var rows = new List<(int Id, int ContractId, int? FamilyId, int? ComponentId, int? Cycle, DateOnly? Reference, bool Active, int Position)>();
        await using (var command = Command(connection, transaction,
            "SELECT id, contract_id, family_id, component_id, cycle_months, reference_date, is_active, position FROM service_contract_units WHERE " + condition, parameters))
        await using (var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            while (await reader.ReadAsync(token).ConfigureAwait(false))
                rows.Add((checked((int)reader.GetInt64(0)), checked((int)reader.GetInt64(1)), reader.IsDBNull(2) ? null : checked((int)reader.GetInt64(2)),
                    reader.IsDBNull(3) ? null : checked((int)reader.GetInt64(3)), reader.IsDBNull(4) ? null : reader.GetInt32(4),
                    reader.IsDBNull(5) ? null : ReadDate(reader, 5), reader.GetInt32(6) != 0, reader.GetInt32(7)));
        var ids = rows.Select(row => row.Id).ToHashSet();
        var members = new Dictionary<int, List<ServiceContractUnitMember>>();
        if (ids.Count > 0)
        {
            await using var command = Command(connection, transaction, $"SELECT unit_id, component_id, is_active FROM service_contract_unit_members WHERE unit_id IN ({string.Join(",", ids)}) ORDER BY unit_id, component_id");
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                var unitId = checked((int)reader.GetInt64(0));
                if (!members.TryGetValue(unitId, out var list)) members[unitId] = list = [];
                list.Add(new(checked((int)reader.GetInt64(1)), reader.GetInt32(2) != 0));
            }
        }
        return [.. rows.OrderBy(row => row.Position).ThenBy(row => row.Id).Select(row => new ServiceContractUnit(row.Id, row.ContractId, row.FamilyId, row.ComponentId, row.Cycle,
            row.Reference, row.Active, row.Position, members.TryGetValue(row.Id, out var list) ? list : []))];
    }

    private static string UnitKey(ServiceContractUnit unit) => unit.FamilyId is { } family ? $"F{family}" : $"C{unit.ComponentId}";

    private static string DescribeUnit(CatalogNames catalog, string name, bool isFamily, IEnumerable<ServiceContractUnitMember> members, int? cycle, DateOnly? reference, bool active)
    {
        var parts = new List<string>();
        if (isFamily) parts.Add("elemente: " + string.Join(", ", members.Select(member => ComponentName(catalog, member.ComponentId) + (member.IsActive ? string.Empty : " (Off)"))));
        if (cycle is { } months) parts.Add("ciclicitate " + ServiceContractRules.CycleText(months));
        if (reference is { } date) parts.Add("referință " + StockMovementRules.DisplayDate(date));
        if (!active) parts.Add("Off");
        return parts.Count == 0 ? name : $"{name} ({string.Join("; ", parts)})";
    }

    // Brings the stored units of the contract to the desired ones (add, change, remove) and queues one journal entry with the exact changes. Returns whether anything changed.
    private async Task<bool> ApplyUnitsAsync(MySqlConnection connection, MySqlTransaction transaction, int contractId, string target, IReadOnlyList<ServiceContractUnitInput> desired,
        IReadOnlyList<ServiceContractUnit> current, CatalogNames catalog, List<Pending>? pending, CancellationToken token)
    {
        var changes = new List<string>();
        var currentByKey = current.ToDictionary(UnitKey);
        var position = 0;
        foreach (var wanted in desired)
        {
            position++;
            string name;
            List<ServiceContractUnitMember> members = [];
            if (wanted.FamilyId is { } familyId)
            {
                if (!catalog.Families.TryGetValue(familyId, out var family)) throw new ServiceContractOperationException("Familia aleasă nu mai există. Actualizează pagina.");
                name = family.Name;
                var memberIds = catalog.Components.Where(item => item.Value.FamilyId == familyId).Select(item => item.Key).Order().ToList();
                if (wanted.Members.Any(member => !memberIds.Contains(member.ComponentId)))
                    throw new ServiceContractOperationException($"Un element ales nu face parte din familia «{name}». Actualizează pagina.");
                members = [.. memberIds.Select(id => new ServiceContractUnitMember(id, wanted.Members.FirstOrDefault(member => member.ComponentId == id)?.IsActive ?? true))];
            }
            else
            {
                var componentId = wanted.ComponentId ?? 0;
                if (!catalog.Components.TryGetValue(componentId, out var component)) throw new ServiceContractOperationException("Componenta aleasă nu mai există. Actualizează pagina.");
                name = component.Name;
                if (component.FamilyId is { } owner)
                    throw new ServiceContractOperationException($"Componenta «{name}» face parte din familia «{FamilyName(catalog, owner)}»: adaugă familia și oprește elementele care nu se aplică.");
            }

            if (!currentByKey.Remove(wanted.Key, out var existing))
            {
                var active = wanted.FamilyId is { } fid ? catalog.Families[fid].Active : catalog.Components[wanted.ComponentId ?? 0].Active;
                if (!active) throw new ServiceContractOperationException($"«{name}» este inactivă în nomenclator: activeaz-o sau alege altă componentă.");
                long unitId;
                await using (var insert = Command(connection, transaction, """
                    INSERT INTO service_contract_units (contract_id, family_id, component_id, cycle_months, reference_date, is_active, position, version)
                    VALUES (@contract, @family, @component, @cycle, @reference, @active, @position, 0)
                    """, ("@contract", contractId), ("@family", wanted.FamilyId), ("@component", wanted.ComponentId), ("@cycle", wanted.CycleMonths),
                    ("@reference", wanted.ReferenceDate is { } date ? SqlDate(date) : null), ("@active", wanted.IsActive ? 1 : 0), ("@position", position)))
                {
                    await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    unitId = insert.LastInsertedId;
                }
                foreach (var member in members)
                    await using (var insertMember = Command(connection, transaction, "INSERT INTO service_contract_unit_members (unit_id, component_id, is_active) VALUES (@unit, @component, @active)",
                        ("@unit", unitId), ("@component", member.ComponentId), ("@active", member.IsActive ? 1 : 0)))
                        await insertMember.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                changes.Add("adăugat: " + DescribeUnit(catalog, name, wanted.FamilyId is not null, members, wanted.CycleMonths, wanted.ReferenceDate, wanted.IsActive));
                continue;
            }

            var unitChanges = new List<string>();
            if (existing.IsActive != wanted.IsActive) unitChanges.Add(wanted.IsActive ? "pornită (On)" : "oprită (Off)");
            if (existing.CycleMonths != wanted.CycleMonths)
                unitChanges.Add($"ciclicitate {(existing.CycleMonths is { } before ? ServiceContractRules.CycleText(before) : "globală")} → {(wanted.CycleMonths is { } now ? ServiceContractRules.CycleText(now) : "globală")}");
            if (existing.ReferenceDate != wanted.ReferenceDate)
                unitChanges.Add($"referință {(existing.ReferenceDate is { } oldDate ? StockMovementRules.DisplayDate(oldDate) : "globală")} → {(wanted.ReferenceDate is { } newDate ? StockMovementRules.DisplayDate(newDate) : "globală")}");
            var existingMembers = existing.Members.ToDictionary(member => member.ComponentId);
            foreach (var member in members)
            {
                if (!existingMembers.Remove(member.ComponentId, out var stored))
                {
                    await using var insertMember = Command(connection, transaction, "INSERT INTO service_contract_unit_members (unit_id, component_id, is_active) VALUES (@unit, @component, @active)",
                        ("@unit", existing.Id), ("@component", member.ComponentId), ("@active", member.IsActive ? 1 : 0));
                    await insertMember.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    unitChanges.Add($"element nou {ComponentName(catalog, member.ComponentId)}{(member.IsActive ? string.Empty : " (Off)")}");
                }
                else if (stored.IsActive != member.IsActive)
                {
                    await using var updateMember = Command(connection, transaction, "UPDATE service_contract_unit_members SET is_active=@active WHERE unit_id=@unit AND component_id=@component",
                        ("@active", member.IsActive ? 1 : 0), ("@unit", existing.Id), ("@component", member.ComponentId));
                    await updateMember.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    unitChanges.Add($"{ComponentName(catalog, member.ComponentId)} {(member.IsActive ? "pornit (On)" : "oprit (Off)")}");
                }
            }
            foreach (var leftover in existingMembers.Values)
                await using (var deleteMember = Command(connection, transaction, "DELETE FROM service_contract_unit_members WHERE unit_id=@unit AND component_id=@component",
                    ("@unit", existing.Id), ("@component", leftover.ComponentId)))
                    await deleteMember.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            if (unitChanges.Count > 0 || existing.Position != position)
            {
                await using var update = Command(connection, transaction, "UPDATE service_contract_units SET cycle_months=@cycle, reference_date=@reference, is_active=@active, position=@position, version=version+1 WHERE id=@id",
                    ("@cycle", wanted.CycleMonths), ("@reference", wanted.ReferenceDate is { } date ? SqlDate(date) : null), ("@active", wanted.IsActive ? 1 : 0), ("@position", position), ("@id", existing.Id));
                await update.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }
            if (unitChanges.Count > 0) changes.Add($"{name}: {string.Join(", ", unitChanges)}");
        }
        foreach (var removed in currentByKey.Values)
        {
            await using var delete = Command(connection, transaction, "DELETE FROM service_contract_units WHERE id=@id", ("@id", removed.Id));
            await delete.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            changes.Add("scos: " + UnitName(catalog, removed));
        }
        if (changes.Count > 0 && pending is not null)
            pending.Add(new(AuditActions.EditContractComponents, target, "Componente: " + string.Join("; ", changes), ServiceContractRules.GeneratedEditReason));
        return changes.Count > 0;
    }

    private static async Task<string> UnitsSummaryAsync(MySqlConnection connection, MySqlTransaction transaction, int contractId, CatalogNames catalog, CancellationToken token)
    {
        var units = await ReadUnitsAsync(connection, transaction, "contract_id=@id", [("@id", contractId)], token).ConfigureAwait(false);
        return units.Count == 0 ? string.Empty
            : "; Componente: " + string.Join("; ", units.Select(unit => DescribeUnit(catalog, UnitName(catalog, unit), unit.IsFamily, unit.Members, unit.CycleMonths, unit.ReferenceDate, unit.IsActive)));
    }

    private sealed record FrozenState(bool Frozen, IReadOnlyList<string> Missing);

    // What is missing for the contract (after the units and the points were written in this transaction): the caller stores is_frozen / missing_info with its own UPDATE.
    private static async Task<FrozenState> RefreshFrozenAsync(MySqlConnection connection, MySqlTransaction transaction, ServiceContract contract, CatalogNames catalog, CancellationToken token, bool store = false)
    {
        var units = await ReadUnitsAsync(connection, transaction, "contract_id=@id", [("@id", contract.Id)], token).ConfigureAwait(false);
        await using var count = Command(connection, transaction, "SELECT COUNT(*) FROM service_contract_points WHERE contract_id=@id", ("@id", contract.Id));
        var points = Convert.ToInt32(await count.ExecuteScalarAsync(token).ConfigureAwait(false));
        var missing = ServiceContractFreeze.Missing(contract.ReferenceDate, contract.CycleMonths, units, points, unit => UnitName(catalog, unit));
        if (store)
        {
            // A contract just inserted: the frozen flag was written provisionally; the final state is stored here.
            await using var update = Command(connection, transaction, "UPDATE service_contracts SET is_frozen=@frozen, missing_info=@missing WHERE id=@id",
                ("@frozen", missing.Count > 0 ? 1 : 0), ("@missing", ServiceContractFreeze.Stored(missing)), ("@id", contract.Id));
            await update.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }
        return new(missing.Count > 0, missing);
    }

    // ---- due dates per point and unit ----

    private static async Task<IReadOnlyList<ServiceUnitDue>> ReadDuesAsync(MySqlConnection connection, MySqlTransaction? transaction, string condition,
        (string Name, object? Value)[] parameters, CancellationToken token)
    {
        var rows = new List<ServiceUnitDue>();
        await using var command = Command(connection, transaction, """
            SELECT d.point_id, d.unit_id, u.family_id, u.component_id, d.next_due
            FROM service_contract_unit_dues d JOIN service_contract_units u ON u.id = d.unit_id
            WHERE
            """ + " " + condition + " ORDER BY d.next_due, d.point_id, u.position, d.unit_id", parameters);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
            rows.Add(new(checked((int)reader.GetInt64(0)), checked((int)reader.GetInt64(1)), reader.IsDBNull(2) ? null : checked((int)reader.GetInt64(2)),
                reader.IsDBNull(3) ? null : checked((int)reader.GetInt64(3)), ReadDate(reader, 4)));
        return rows;
    }

    // Brings the due dates of a COMPLETE contract to one per covered point and covering unit: a unit that no longer covers loses its dates; a pair without a date gets one (the date of
    // the point the first time the point has dates, later reference date + cycle of the unit); the date of the point is the earliest of its units. A frozen contract is left as it is.
    internal static async Task SyncDuesAsync(MySqlConnection connection, MySqlTransaction transaction, int contractId, CancellationToken token)
    {
        var contract = await GetLockedAsync(connection, transaction, contractId, token).ConfigureAwait(false);
        if (contract is null || contract.IsFrozen) return;
        var points = await ReadPointsAsync(connection, transaction, contractId, token).ConfigureAwait(false);
        var covering = (await ReadUnitsAsync(connection, transaction, "contract_id=@id", [("@id", contractId)], token).ConfigureAwait(false)).Where(unit => unit.Covers).ToList();
        var coveringIds = covering.Select(unit => unit.Id).ToHashSet();
        var existing = (await ReadDuesAsync(connection, transaction, "d.point_id IN (SELECT id FROM service_contract_points WHERE contract_id=@id)", [("@id", contractId)], token).ConfigureAwait(false)).ToList();
        foreach (var stale in existing.Where(due => !coveringIds.Contains(due.UnitId)).ToList())
        {
            await using var delete = Command(connection, transaction, "DELETE FROM service_contract_unit_dues WHERE point_id=@point AND unit_id=@unit", ("@point", stale.PointId), ("@unit", stale.UnitId));
            await delete.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }
        foreach (var view in points)
        {
            var point = view.Point;
            var hadDates = existing.Any(due => due.PointId == point.Id);
            var dates = existing.Where(due => due.PointId == point.Id && coveringIds.Contains(due.UnitId)).Select(due => (due.UnitId, due.NextDue)).ToList();
            foreach (var unit in covering.Where(unit => dates.All(item => item.UnitId != unit.Id)))
            {
                var reference = ServiceContractFreeze.Reference(unit, contract.ReferenceDate);
                var cycle = ServiceContractFreeze.Cycle(unit, contract.CycleMonths);
                var due = !hadDates || reference is null || cycle is null ? point.NextDue : reference.Value.AddMonths(cycle.Value);
                await using var insert = Command(connection, transaction, "INSERT INTO service_contract_unit_dues (point_id, unit_id, next_due, version) VALUES (@point, @unit, @due, 0)",
                    ("@point", point.Id), ("@unit", unit.Id), ("@due", SqlDate(due)));
                await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                dates.Add((unit.Id, due));
            }
            if (dates.Count == 0) continue;
            var earliest = dates.Min(item => item.NextDue);
            if (earliest != point.NextDue)
            {
                await using var update = Command(connection, transaction, "UPDATE service_contract_points SET next_due=@due, version=version+1 WHERE id=@id", ("@due", SqlDate(earliest)), ("@id", point.Id));
                await update.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }
        }
    }
}
