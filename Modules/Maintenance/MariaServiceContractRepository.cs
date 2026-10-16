using System.Data;
using MySqlConnector;
using static BlazorStoc.Services.MariaDb;

namespace BlazorStoc.Services;

// MariaDB mode: `service_contracts` and `service_contract_points` are created by migration 8 (MariaSchemaMigrations); like the other
// repositories this one never alters the schema. Every write locks the row of the beneficiary first (contracts, points and moves
// never cross beneficiaries), so two sessions working on the contracts of one beneficiary are serialized; the unique key on
// service_contract_points.active_work_point_id (one ACTIVE contract per work point) stays as the last line of defence.
public sealed class MariaServiceContractRepository(
    IConfiguration configuration,
    IAccessControl? accessControl = null,
    IAuditTrail? auditTrail = null,
    IArchiveService? archiveService = null) : IServiceContractRepository
{
    private const string ContractColumns = "id, beneficiary_id, contract_number, contract_date, cycle_months, valid_until, is_active, notes, version";
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
        return contracts.Select(contract => new ServiceContractDetails(contract, points.Where(point => point.Point.ContractId == contract.Id).ToArray())).ToArray();
    }

    public async Task<IReadOnlyList<ServiceDueRow>> GetDueListAsync(bool includeOff, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAnyPermissionAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, null, """
            SELECT c.id, c.beneficiary_id, c.contract_number, c.contract_date, c.cycle_months, c.valid_until, c.is_active, c.notes, c.version,
                   p.id, p.contract_id, p.work_point_id, p.cycle_months, p.next_due, p.version, w.name, w.address, w.is_primary, b.name,
                   w.latitude, w.longitude,
                   (SELECT MAX(i.performed_on) FROM service_interventions i WHERE i.work_point_id = p.work_point_id AND i.kind = 'M')
            FROM service_contract_points p
            JOIN service_contracts c ON c.id = p.contract_id
            JOIN beneficiary_work_points w ON w.id = p.work_point_id
            JOIN beneficiaries b ON b.id = c.beneficiary_id
            WHERE c.is_active = 1 OR @includeOff = 1
            ORDER BY p.next_due, b.name, w.name, p.id
            """, ("@includeOff", includeOff ? 1 : 0));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var rows = new List<ServiceDueRow>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            rows.Add(new(checked((int)reader.GetInt64(1)), reader.GetString(18), ReadContract(reader),
                new(new(checked((int)reader.GetInt64(9)), checked((int)reader.GetInt64(10)), checked((int)reader.GetInt64(11)),
                        reader.IsDBNull(12) ? null : reader.GetInt32(12), ReadDate(reader, 13), reader.GetInt64(14)),
                    reader.GetString(15), reader.GetString(16), reader.GetInt32(17) != 0),
                reader.IsDBNull(19) ? null : reader.GetDecimal(19), reader.IsDBNull(20) ? null : reader.GetDecimal(20), reader.IsDBNull(21) ? null : ReadDate(reader, 21)));
        return rows;
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
                INSERT INTO service_contracts (beneficiary_id, contract_number, contract_date, cycle_months, valid_until, is_active, notes, version)
                VALUES (@beneficiaryId, @number, @date, @cycle, @validUntil, 1, @notes, 0)
                """, ("@beneficiaryId", beneficiaryId), ("@number", value.Number), ("@date", SqlDate(value.Date)), ("@cycle", value.CycleMonths),
                ("@validUntil", value.ValidUntil is { } validUntil ? SqlDate(validUntil) : null), ("@notes", value.Notes));
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            var contract = new ServiceContract(checked((int)insert.LastInsertedId), beneficiaryId, value.Number, value.Date, value.CycleMonths,
                value.ValidUntil, true, value.Notes, 0);
            var target = ServiceContractRules.Target(beneficiaryId, ownerName, label);
            pending.Add(new(AuditActions.CreateServiceContract, target, ServiceContractRules.Identification(contract), string.Empty));
            await ApplyPointsAsync(connection, transaction, contract, target, value.Points, [], pending, cancellationToken).ConfigureAwait(false);
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
            var after = original with { Number = value.Number, Date = value.Date, CycleMonths = value.CycleMonths, ValidUntil = value.ValidUntil, Notes = value.Notes };
            var target = ServiceContractRules.Target(original.BeneficiaryId, ownerName, after.Label);
            var changed = after != original;
            if (changed)
                pending.Add(new(ServiceContractRules.EditAction(original, after), target,
                    AuditDetails.Changes([.. ServiceContractRules.Changes(original, after)]), ServiceContractRules.GeneratedEditReason));
            var coverageChanged = await ApplyPointsAsync(connection, transaction, after, target, value.Points, current, pending, cancellationToken).ConfigureAwait(false);
            if (changed || coverageChanged)
            {
                await using var update = Command(connection, transaction, """
                    UPDATE service_contracts SET contract_number=@number, contract_date=@date, cycle_months=@cycle, valid_until=@validUntil, notes=@notes, version=@version
                    WHERE id=@id AND version=@oldVersion
                    """, ("@number", after.Number), ("@date", SqlDate(after.Date)), ("@cycle", after.CycleMonths),
                    ("@validUntil", after.ValidUntil is { } validUntil ? SqlDate(validUntil) : null), ("@notes", after.Notes),
                    ("@version", checked(original.Version + 1)), ("@id", original.Id), ("@oldVersion", original.Version));
                if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1) throw ServiceContractRules.Changed();
            }
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
                summary.Add(due == row.Point.NextDue
                    ? $"{row.WorkPointName} ({StockMovementRules.DisplayDate(due)})"
                    : $"{row.WorkPointName} ({StockMovementRules.DisplayDate(row.Point.NextDue)} → {StockMovementRules.DisplayDate(due)})");
                if (due != row.Point.NextDue)
                    pending.Add(new(AuditActions.RescheduleMaintenance, target, PointDetails(row.WorkPointName, original.Label, null) + "; " +
                        AuditDetails.Changes(new AuditChange("Scadență", StockMovementRules.DisplayDate(row.Point.NextDue), StockMovementRules.DisplayDate(due))) +
                        "; Motiv: reactivare contract", ServiceContractRules.GeneratedActivationReason));
            }
            await SetActiveAsync(connection, transaction, original, true, cancellationToken).ConfigureAwait(false);
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
        return new(contract, await ReadPointsAsync(connection, transaction, contractId, token).ConfigureAwait(false));
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
        new(checked((int)reader.GetInt64(0)), checked((int)reader.GetInt64(1)), reader.GetString(2), ReadDate(reader, 3), reader.GetInt32(4),
            reader.IsDBNull(5) ? null : ReadDate(reader, 5), reader.GetInt32(6) != 0, reader.GetString(7), reader.GetInt64(8));

    private static ServiceContractPointView ReadPoint(MySqlDataReader reader) =>
        new(new(checked((int)reader.GetInt64(0)), checked((int)reader.GetInt64(1)), checked((int)reader.GetInt64(2)),
                reader.IsDBNull(3) ? null : reader.GetInt32(3), ReadDate(reader, 4), reader.GetInt64(5)),
            reader.GetString(6), reader.GetString(7), reader.GetInt32(8) != 0);

    private static DateOnly ReadDate(MySqlDataReader reader, int ordinal) => DateOnly.FromDateTime(reader.GetDateTime(ordinal));

    private static DateTime SqlDate(DateOnly date) => date.ToDateTime(TimeOnly.MinValue);

}
