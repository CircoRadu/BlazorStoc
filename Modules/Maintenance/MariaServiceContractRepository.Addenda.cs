using System.Globalization;
using System.Text.Json;
using MySqlConnector;
using static BlazorStoc.Services.MariaDb;

namespace BlazorStoc.Services;

// The addenda of the maintenance contracts (service_contract_addenda, migration 74). Like every write of the contracts, an operation locks the beneficiary first, then the contract
// and its coverage rows; the effect of an addendum on the contract (validity, cycle, coverage) and the row of the addendum are written in ONE transaction, the journal entries after it.
public sealed partial class MariaServiceContractRepository
{
    private const string AddendumColumns = """
        a.id, a.contract_id, a.addendum_number, a.addendum_date, a.effective_date, a.kinds, a.summary, a.prev_valid_until, a.new_valid_until, a.prev_cycle_months, a.new_cycle_months,
        a.points_before, a.points_after, a.status, a.created_by, a.created_utc, a.cancelled_by, a.cancelled_utc, a.cancel_reason, a.version,
        a.reference_before, a.reference_after, a.units_before, a.units_after
        """;

    private static readonly JsonSerializerOptions SnapshotJson = new();

    private static IReadOnlyList<AddendumPointSnapshot> Snapshot(IEnumerable<ServiceContractPointView> points) =>
        [.. points.Select(point => new AddendumPointSnapshot(point.Point.WorkPointId, point.WorkPointName, point.Point.CycleMonths, point.Point.NextDue))];

    private static IReadOnlyList<AddendumPointSnapshot> ReadSnapshot(MySqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal)) return [];
        try { return JsonSerializer.Deserialize<List<AddendumPointSnapshot>>(reader.GetString(ordinal), SnapshotJson) ?? []; }
        catch (JsonException) { return []; }
    }

    private static IReadOnlyList<ServiceContractUnitInput>? ReadUnitSnapshot(MySqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal)) return null;
        try { return JsonSerializer.Deserialize<List<ServiceContractUnitInput>>(reader.GetString(ordinal), SnapshotJson); }
        catch (JsonException) { return null; }
    }

    private static List<ServiceContractUnitInput> UnitInputsOf(IEnumerable<ServiceContractUnit> units) => [.. units.Select(unit => new ServiceContractUnitInput
    {
        FamilyId = unit.FamilyId, ComponentId = unit.ComponentId, CycleMonths = unit.CycleMonths, ReferenceDate = unit.ReferenceDate, IsActive = unit.IsActive,
        Members = [.. unit.Members.Select(member => new ServiceContractUnitMemberInput { ComponentId = member.ComponentId, IsActive = member.IsActive })]
    })];

    private static bool SameUnits(IReadOnlyList<ServiceContractUnitInput> left, IReadOnlyList<ServiceContractUnitInput> right)
    {
        if (left.Count != right.Count) return false;
        var rightByKey = right.ToDictionary(unit => unit.Key);
        foreach (var unit in left)
        {
            if (!rightByKey.TryGetValue(unit.Key, out var other)) return false;
            if (unit.IsActive != other.IsActive || unit.CycleMonths != other.CycleMonths || unit.ReferenceDate != other.ReferenceDate) return false;
            var members = unit.Members.OrderBy(member => member.ComponentId).Select(member => (member.ComponentId, member.IsActive));
            if (!members.SequenceEqual(other.Members.OrderBy(member => member.ComponentId).Select(member => (member.ComponentId, member.IsActive)))) return false;
        }
        return true;
    }

    // The act verifies what the interventions of the beneficiary covered: the components of an intervention that had none are stored with the act.
    private static async Task<int> AssignInterventionComponentsAsync(MySqlConnection connection, MySqlTransaction transaction, int beneficiaryId, IReadOnlyList<AddendumInterventionComponents> items, CancellationToken token)
    {
        var count = 0;
        foreach (var item in items.Where(item => item.ComponentIds.Count > 0))
        {
            await using (var check = Command(connection, transaction, "SELECT beneficiary_id, kind FROM service_interventions WHERE id=@id FOR UPDATE", ("@id", item.InterventionId)))
            await using (var reader = await check.ExecuteReaderAsync(token).ConfigureAwait(false))
            {
                if (!await reader.ReadAsync(token).ConfigureAwait(false) || reader.GetInt64(0) != beneficiaryId || reader.GetString(1) != "M")
                    throw new ServiceContractOperationException("O intervenție aleasă nu este o intervenție de mentenanță a beneficiarului. Actualizează pagina.");
            }
            await using (var existing = Command(connection, transaction, "SELECT COUNT(*) FROM service_intervention_components WHERE intervention_id=@id", ("@id", item.InterventionId)))
                if (Convert.ToInt32(await existing.ExecuteScalarAsync(token).ConfigureAwait(false)) > 0) continue;
            await using (var known = Command(connection, transaction, $"SELECT COUNT(*) FROM maintenance_components WHERE id IN ({string.Join(",", item.ComponentIds)})"))
                if (Convert.ToInt32(await known.ExecuteScalarAsync(token).ConfigureAwait(false)) != item.ComponentIds.Count)
                    throw new ServiceContractOperationException("O componentă aleasă pentru o intervenție nu mai există în nomenclator. Actualizează pagina.");
            foreach (var componentId in item.ComponentIds)
                await using (var insert = Command(connection, transaction, "INSERT INTO service_intervention_components (intervention_id, component_id) VALUES (@intervention, @component)",
                    ("@intervention", item.InterventionId), ("@component", componentId)))
                    await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            count++;
        }
        return count;
    }

    private static ServiceContractAddendum ReadAddendum(MySqlDataReader reader) => new(
        checked((int)reader.GetInt64(0)), checked((int)reader.GetInt64(1)), reader.GetString(2), ReadDate(reader, 3), reader.IsDBNull(4) ? null : ReadDate(reader, 4),
        ServiceAddendumKinds.Split(reader.GetString(5)), reader.GetString(6), reader.IsDBNull(7) ? null : ReadDate(reader, 7), reader.IsDBNull(8) ? null : ReadDate(reader, 8),
        reader.IsDBNull(9) ? null : reader.GetInt32(9), reader.IsDBNull(10) ? null : reader.GetInt32(10), ReadSnapshot(reader, 11), ReadSnapshot(reader, 12),
        reader.GetString(13), reader.GetString(14), MariaTimeText.Parse(reader.GetString(15)), reader.GetString(16),
        reader.GetString(17).Length == 0 ? null : MariaTimeText.Parse(reader.GetString(17)), reader.GetString(18), reader.GetInt64(19),
        reader.IsDBNull(20) ? null : ReadDate(reader, 20), reader.IsDBNull(21) ? null : ReadDate(reader, 21), ReadUnitSnapshot(reader, 22), ReadUnitSnapshot(reader, 23));

    private static async Task<ServiceContractAddendum?> GetAddendumLockedAsync(MySqlConnection connection, MySqlTransaction transaction, int id, CancellationToken token)
    {
        await using var command = Command(connection, transaction, $"SELECT {AddendumColumns} FROM service_contract_addenda a WHERE a.id=@id FOR UPDATE", ("@id", id));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        return await reader.ReadAsync(token).ConfigureAwait(false) ? ReadAddendum(reader) : null;
    }

    private static async Task<int> BeneficiaryOfContractAsync(MySqlConnection connection, int contractId, CancellationToken token)
    {
        await using var command = Command(connection, null, "SELECT beneficiary_id FROM service_contracts WHERE id=@id", ("@id", contractId));
        return await command.ExecuteScalarAsync(token).ConfigureAwait(false) is long id ? checked((int)id) : throw ServiceContractAddendumRules.Changed();
    }

    private static async Task EnsureUniqueAddendumAsync(MySqlConnection connection, MySqlTransaction transaction, int contractId, string number, DateOnly date, int? excludedId, string label, CancellationToken token)
    {
        await using var command = Command(connection, transaction, """
            SELECT id FROM service_contract_addenda WHERE contract_id=@contract AND addendum_number=@number AND addendum_date=@date AND (@id IS NULL OR id<>@id) LIMIT 1
            """, ("@contract", contractId), ("@number", number), ("@date", SqlDate(date)), ("@id", excludedId));
        if (await command.ExecuteScalarAsync(token).ConfigureAwait(false) is not null) throw ServiceContractAddendumRules.Duplicate(label);
    }

    private async Task<string> UserAsync(CancellationToken token) => accessControl is null ? "" : await accessControl.GetUsernameAsync(token).ConfigureAwait(false) ?? "";

    public async Task<IReadOnlyList<ServiceContractAddendum>> GetAddendaForBeneficiaryAsync(int beneficiaryId, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAnyPermissionAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, null, $"""
            SELECT {AddendumColumns} FROM service_contract_addenda a JOIN service_contracts c ON c.id = a.contract_id
            WHERE c.beneficiary_id=@id ORDER BY a.addendum_date DESC, a.id DESC
            """, ("@id", beneficiaryId));
        var items = new List<ServiceContractAddendum>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) items.Add(ReadAddendum(reader));
        return items;
    }

    public async Task<ServiceContractDetails> AddAsync(ServiceContract original, ServiceContractAddendumInput input, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAsync("mentenanta.edit", cancellationToken).ConfigureAwait(false);
        var value = input.Validated();
        var user = await UserAsync(cancellationToken).ConfigureAwait(false);
        var pending = new List<Pending>();
        var details = await WriteAsync(async (connection, transaction) =>
        {
            pending.Clear();
            var ownerName = await LockBeneficiaryAsync(connection, transaction, original.BeneficiaryId, cancellationToken).ConfigureAwait(false);
            ServiceContractRules.CheckCurrent(await GetLockedAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false), original);
            var kinds = value.Kinds;
            if (ServiceAddendumKinds.HasEffect(kinds) && !original.IsActive) throw ServiceContractAddendumRules.ContractOff();
            var date = value.Date!.Value;
            var label = $"AA {value.Number}/{StockMovementRules.DisplayDate(date)}";
            await EnsureUniqueAddendumAsync(connection, transaction, original.Id, value.Number, date, null, label, cancellationToken).ConfigureAwait(false);

            var currentPoints = await LockPointsAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false);
            var currentUnits = await ReadUnitsAsync(connection, transaction, "contract_id=@id FOR UPDATE", [("@id", original.Id)], cancellationToken).ConfigureAwait(false);
            var catalog = await LoadCatalogAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            var unitsBefore = UnitInputsOf(currentUnits);
            var before = Snapshot(currentPoints);
            var after = original;
            if (value.ChangeValidity)
            {
                if (value.NewValidUntil is { } validUntil && validUntil < original.Date)
                    throw new ServiceContractOperationException("Noua dată de expirare nu poate fi anterioară datei contractului.");
                after = after with { ValidUntil = value.NewValidUntil };
            }
            if (value.ChangeCycle) after = after with { CycleMonths = value.NewCycleMonths };
            if (value.ChangeReference) after = after with { ReferenceDate = value.NewReferenceDate };
            var contractTarget = ServiceContractRules.Target(original.BeneficiaryId, ownerName, original.Label);
            var unitsChanged = false;
            if (value.ChangeComponents)
                unitsChanged = await ApplyUnitsAsync(connection, transaction, original.Id, contractTarget, value.Units, currentUnits, catalog, pending, cancellationToken).ConfigureAwait(false);
            var verified = await AssignInterventionComponentsAsync(connection, transaction, original.BeneficiaryId, value.InterventionComponents, cancellationToken).ConfigureAwait(false);
            var pointsChanged = false;
            if (value.CarriesPoints)
                pointsChanged = await ApplyPointsAsync(connection, transaction, after, contractTarget, value.Points, currentPoints, pending, cancellationToken).ConfigureAwait(false);
            if (after != original || pointsChanged || unitsChanged)
            {
                var state = await RefreshFrozenAsync(connection, transaction, after, catalog, cancellationToken).ConfigureAwait(false);
                if (state.Frozen != original.IsFrozen)
                    pending.Add(new(state.Frozen ? AuditActions.FreezeServiceContract : AuditActions.UnfreezeServiceContract, contractTarget,
                        AuditDetails.Changes(new AuditChange("Stare", original.IsFrozen ? "Înghețat" : "Activ", state.Frozen ? "Înghețat" : "Activ")) +
                        (state.Frozen ? "; Lipsă: " + ServiceContractFreeze.Summary(state.Missing) : string.Empty),
                        state.Frozen ? "Date incomplete în contract" : "Datele contractului au fost completate"));
                await using var update = Command(connection, transaction,
                    "UPDATE service_contracts SET cycle_months=@cycle, valid_until=@validUntil, reference_date=@reference, is_frozen=@frozen, missing_info=@missing, version=@version WHERE id=@id AND version=@oldVersion",
                    ("@cycle", after.CycleMonths), ("@validUntil", after.ValidUntil is { } until ? SqlDate(until) : null),
                    ("@reference", after.ReferenceDate is { } reference ? SqlDate(reference) : null), ("@frozen", state.Frozen ? 1 : 0), ("@missing", ServiceContractFreeze.Stored(state.Missing)),
                    ("@version", checked(original.Version + 1)), ("@id", original.Id), ("@oldVersion", original.Version));
                if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1) throw ServiceContractRules.Changed();
            }
            await SyncDuesAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false);
            var afterPoints = Snapshot(await ReadPointsAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false));
            var unitsAfter = UnitInputsOf(await ReadUnitsAsync(connection, transaction, "contract_id=@id", [("@id", original.Id)], cancellationToken).ConfigureAwait(false));
            var kindsText = ServiceAddendumKinds.Join(kinds);
            await using (var insert = Command(connection, transaction, """
                INSERT INTO service_contract_addenda (contract_id, addendum_number, addendum_date, effective_date, kinds, summary, prev_valid_until, new_valid_until,
                    prev_cycle_months, new_cycle_months, points_before, points_after, status, created_by, created_utc, version, reference_before, reference_after, units_before, units_after)
                VALUES (@contract, @number, @date, @effective, @kinds, @summary, @prevValid, @newValid, @prevCycle, @newCycle, @before, @after, 'Aplicat', @by, @now, 0, @refBefore, @refAfter, @unitsBefore, @unitsAfter)
                """, ("@contract", original.Id), ("@number", value.Number), ("@date", SqlDate(date)), ("@effective", value.EffectiveDate is { } effective ? SqlDate(effective) : null),
                ("@kinds", kindsText), ("@summary", value.Summary),
                ("@prevValid", value.ChangeValidity && original.ValidUntil is { } pv ? SqlDate(pv) : null), ("@newValid", value.ChangeValidity && value.NewValidUntil is { } nv ? SqlDate(nv) : null),
                ("@prevCycle", value.ChangeCycle ? original.CycleMonths : null), ("@newCycle", value.ChangeCycle ? value.NewCycleMonths : null),
                ("@before", value.CarriesPoints ? JsonSerializer.Serialize(before, SnapshotJson) : null),
                ("@after", value.CarriesPoints ? JsonSerializer.Serialize(afterPoints, SnapshotJson) : null),
                ("@refBefore", value.ChangeReference && original.ReferenceDate is { } rb ? SqlDate(rb) : null), ("@refAfter", value.ChangeReference && value.NewReferenceDate is { } ra ? SqlDate(ra) : null),
                ("@unitsBefore", value.ChangeComponents ? JsonSerializer.Serialize(unitsBefore, SnapshotJson) : null), ("@unitsAfter", value.ChangeComponents ? JsonSerializer.Serialize(unitsAfter, SnapshotJson) : null),
                ("@by", user), ("@now", MariaTimeText.Format(DateTime.UtcNow))))
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            var parts = new List<string> { AuditDetails.Identification(("Act adițional", label), ("Efect de la", value.EffectiveDate is { } eff ? StockMovementRules.DisplayDate(eff) : StockMovementRules.DisplayDate(date)),
                ("Modifică", string.Join(", ", kinds.Select(ServiceAddendumKinds.Label)))) };
            var changes = new List<AuditChange>();
            if (value.ChangeValidity) changes.Add(new("Expiră", ServiceContractRules.ValidUntilText(original.ValidUntil), ServiceContractRules.ValidUntilText(after.ValidUntil)));
            if (value.ChangeCycle) changes.Add(new("Ciclicitate contract", ServiceContractRules.CycleText(original.CycleMonths), ServiceContractRules.CycleText(after.CycleMonths)));
            if (value.ChangeReference) changes.Add(new("Data de referință", ServiceContractRules.ReferenceDateText(original.ReferenceDate), ServiceContractRules.ReferenceDateText(after.ReferenceDate)));
            if (changes.Count > 0) parts.Add(AuditDetails.Changes([.. changes]));
            if (value.ChangeComponents)
            {
                var addedUnits = unitsAfter.Where(unit => unitsBefore.All(old => old.Key != unit.Key)).Select(unit => unit.Key).Count();
                var removedUnits = unitsBefore.Where(old => unitsAfter.All(unit => unit.Key != old.Key)).Select(old => old.Key).Count();
                parts.Add($"Componente: +{addedUnits} −{removedUnits}{(unitsChanged ? " (detalii în operația «Modificare componente contract mentenanță»)" : string.Empty)}");
            }
            if (verified > 0) parts.Add($"Componente completate la {verified} intervenții ale beneficiarului");
            if (value.ChangePoints)
            {
                var added = afterPoints.Where(point => before.All(old => old.WorkPointId != point.WorkPointId)).Select(point => point.WorkPointName).ToArray();
                var removed = before.Where(old => afterPoints.All(point => point.WorkPointId != old.WorkPointId)).Select(old => old.WorkPointName).ToArray();
                parts.Add($"Puncte adăugate: {(added.Length == 0 ? "—" : string.Join(", ", added))}; Puncte scoase: {(removed.Length == 0 ? "—" : string.Join(", ", removed))}");
            }
            if (value.Other) parts.Add("Alte clauze: " + value.Summary);
            else if (value.Summary.Length > 0) parts.Add("Descriere: " + value.Summary);
            pending.Insert(0, new(AuditActions.CreateServiceContractAddendum, ServiceContractAddendumRules.Target(original.BeneficiaryId, ownerName, original.Label, label), string.Join("; ", parts), string.Empty));
            return await LoadDetailsAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
        await RecordAsync(original.BeneficiaryId, pending, cancellationToken).ConfigureAwait(false);
        return details;
    }

    public async Task<ServiceContractAddendum> UpdateAsync(ServiceContractAddendum original, ServiceContractAddendumEdit edit, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAsync("mentenanta.edit", cancellationToken).ConfigureAwait(false);
        var number = TextNormalization.ForObjectNameOrCode(edit.Number ?? "").ToUpperInvariant();
        if (number.Length == 0 || number.Length > ServiceContractNumber.NumberMaximumLength)
            throw new ServiceContractOperationException($"Numărul actului adițional trebuie să aibă între 1 și {ServiceContractNumber.NumberMaximumLength} de caractere.");
        if (edit.Date is not { } date || !ServiceContractRules.InRange(date)) throw new ServiceContractOperationException("Completează data actului adițional (o dată validă).");
        if (edit.EffectiveDate is { } effective && !ServiceContractRules.InRange(effective)) throw new ServiceContractOperationException("Data de la care se aplică actul nu este validă.");
        var summary = TextNormalization.ForStorage((edit.Summary ?? "").Replace("\r\n", "\n"));
        if (summary.Length > ServiceContractAddendumInput.SummaryMaximumLength) throw new ServiceContractOperationException("Descrierea poate avea cel mult 1000 de caractere.");
        if (original.Kinds.Contains(ServiceAddendumKinds.Other) && summary.Length == 0) throw new ServiceContractOperationException("Descrie celelalte clauze ale actului.");
        int beneficiaryId;
        await using (var lookup = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false))
            beneficiaryId = await BeneficiaryOfContractAsync(lookup, original.ContractId, cancellationToken).ConfigureAwait(false);
        var pending = new List<Pending>();
        var updated = await WriteAsync(async (connection, transaction) =>
        {
            pending.Clear();
            var ownerName = await LockBeneficiaryAsync(connection, transaction, beneficiaryId, cancellationToken).ConfigureAwait(false);
            var current = await GetAddendumLockedAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false);
            if (current is null || current.Version != original.Version) throw ServiceContractAddendumRules.Changed();
            var contract = await GetLockedAsync(connection, transaction, original.ContractId, cancellationToken).ConfigureAwait(false) ?? throw ServiceContractRules.Changed();
            var label = $"AA {number}/{StockMovementRules.DisplayDate(date)}";
            await EnsureUniqueAddendumAsync(connection, transaction, current.ContractId, number, date, current.Id, label, cancellationToken).ConfigureAwait(false);
            var after = current with { Number = number, Date = date, EffectiveDate = edit.EffectiveDate, Summary = summary };
            if (after == current) return current;
            await using var update = Command(connection, transaction, """
                UPDATE service_contract_addenda SET addendum_number=@number, addendum_date=@date, effective_date=@effective, summary=@summary, version=version+1 WHERE id=@id AND version=@version
                """, ("@number", number), ("@date", SqlDate(date)), ("@effective", edit.EffectiveDate is { } eff ? SqlDate(eff) : null), ("@summary", summary), ("@id", current.Id), ("@version", current.Version));
            if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1) throw ServiceContractAddendumRules.Changed();
            static string Day(DateOnly? day) => day is { } value ? StockMovementRules.DisplayDate(value) : "—";
            pending.Add(new(AuditActions.EditServiceContractAddendum, ServiceContractAddendumRules.Target(beneficiaryId, ownerName, contract.Label, after.Label),
                AuditDetails.Changes(new AuditChange("Număr și dată act", current.Label, after.Label), new AuditChange("Efect de la", Day(current.EffectiveDate), Day(after.EffectiveDate)),
                    new AuditChange("Descriere", current.Summary, after.Summary)), "Editare act adițional contract"));
            return after with { Version = current.Version + 1 };
        }, cancellationToken).ConfigureAwait(false);
        await RecordAsync(beneficiaryId, pending, cancellationToken).ConfigureAwait(false);
        return updated;
    }

    public async Task<ServiceContractDetails> CancelAsync(ServiceContractAddendum original, string reason, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAsync("mentenanta.delete", cancellationToken).ConfigureAwait(false);
        var motif = ChangeReasonRules.Normalize(reason);
        if (ChangeReasonRules.ValidationError(motif) is { } reasonError) throw new ServiceContractOperationException(reasonError);
        var user = await UserAsync(cancellationToken).ConfigureAwait(false);
        int beneficiaryId;
        await using (var lookup = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false))
            beneficiaryId = await BeneficiaryOfContractAsync(lookup, original.ContractId, cancellationToken).ConfigureAwait(false);
        var pending = new List<Pending>();
        var details = await WriteAsync(async (connection, transaction) =>
        {
            pending.Clear();
            var ownerName = await LockBeneficiaryAsync(connection, transaction, beneficiaryId, cancellationToken).ConfigureAwait(false);
            var contract = await GetLockedAsync(connection, transaction, original.ContractId, cancellationToken).ConfigureAwait(false) ?? throw ServiceContractRules.Changed();
            var current = await GetAddendumLockedAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false);
            if (current is null || current.Version != original.Version) throw ServiceContractAddendumRules.Changed();
            if (!current.IsApplied) throw ServiceContractAddendumRules.NothingToCancel();
            await using (var newer = Command(connection, transaction,
                "SELECT COUNT(*) FROM service_contract_addenda WHERE contract_id=@contract AND status='Aplicat' AND id>@id", ("@contract", current.ContractId), ("@id", current.Id)))
                if (Convert.ToInt32(await newer.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) > 0) throw ServiceContractAddendumRules.NotLatest();

            var kinds = current.Kinds;
            var currentPoints = await LockPointsAsync(connection, transaction, contract.Id, cancellationToken).ConfigureAwait(false);
            var currentUnits = await ReadUnitsAsync(connection, transaction, "contract_id=@id FOR UPDATE", [("@id", contract.Id)], cancellationToken).ConfigureAwait(false);
            var catalog = await LoadCatalogAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            if (kinds.Contains(ServiceAddendumKinds.Reference) && contract.ReferenceDate != current.ReferenceAfter) throw ServiceContractAddendumRules.ContractMovedOn();
            if (kinds.Contains(ServiceAddendumKinds.Components) && !SameUnits(UnitInputsOf(currentUnits), current.UnitsAfter ?? [])) throw ServiceContractAddendumRules.ContractMovedOn();
            if (kinds.Contains(ServiceAddendumKinds.Validity) && contract.ValidUntil != current.NewValidUntil) throw ServiceContractAddendumRules.ContractMovedOn();
            if (kinds.Contains(ServiceAddendumKinds.Cycle) && contract.CycleMonths != current.NewCycleMonths) throw ServiceContractAddendumRules.ContractMovedOn();
            var shapeNow = currentPoints.Select(point => (point.Point.WorkPointId, point.Point.CycleMonths)).Order().ToList();
            var shapeAfter = current.PointsAfter.Select(point => (point.WorkPointId, point.CycleMonths)).Order().ToList();
            var pointsTouched = kinds.Contains(ServiceAddendumKinds.Cycle) || kinds.Contains(ServiceAddendumKinds.Points) || kinds.Contains(ServiceAddendumKinds.Components) || kinds.Contains(ServiceAddendumKinds.Reference);
            if (pointsTouched && !shapeNow.SequenceEqual(shapeAfter)) throw ServiceContractAddendumRules.ContractMovedOn();

            var restored = contract;
            if (kinds.Contains(ServiceAddendumKinds.Validity)) restored = restored with { ValidUntil = current.PrevValidUntil };
            if (kinds.Contains(ServiceAddendumKinds.Cycle) && current.PrevCycleMonths is { } previousCycle) restored = restored with { CycleMonths = previousCycle };
            if (kinds.Contains(ServiceAddendumKinds.Reference)) restored = restored with { ReferenceDate = current.ReferenceBefore };
            var contractTarget = ServiceContractRules.Target(beneficiaryId, ownerName, contract.Label);
            var unitsRestored = false;
            if (kinds.Contains(ServiceAddendumKinds.Components))
                unitsRestored = await ApplyUnitsAsync(connection, transaction, contract.Id, contractTarget, current.UnitsBefore ?? [], currentUnits, catalog, pending, cancellationToken).ConfigureAwait(false);
            var pointsChanged = false;
            if (pointsTouched)
            {
                // The points come back as they were (membership and individual cycles); the due dates of the points that stayed are NOT put back (visits may have moved them since).
                var dueNow = currentPoints.ToDictionary(point => point.Point.WorkPointId, point => point.Point.NextDue);
                var desired = current.PointsBefore.Select(point => new ServiceContractPointInput
                {
                    WorkPointId = point.WorkPointId, CycleMonths = point.CycleMonths, NextDue = dueNow.TryGetValue(point.WorkPointId, out var due) ? due : point.NextDue
                }).ToList();
                pointsChanged = await ApplyPointsAsync(connection, transaction, restored, contractTarget, desired, currentPoints, pending, cancellationToken).ConfigureAwait(false);
            }
            if (restored != contract || pointsChanged || unitsRestored)
            {
                var state = await RefreshFrozenAsync(connection, transaction, restored, catalog, cancellationToken).ConfigureAwait(false);
                if (state.Frozen != contract.IsFrozen)
                    pending.Add(new(state.Frozen ? AuditActions.FreezeServiceContract : AuditActions.UnfreezeServiceContract, contractTarget,
                        AuditDetails.Changes(new AuditChange("Stare", contract.IsFrozen ? "Înghețat" : "Activ", state.Frozen ? "Înghețat" : "Activ")) +
                        (state.Frozen ? "; Lipsă: " + ServiceContractFreeze.Summary(state.Missing) : string.Empty),
                        state.Frozen ? "Date incomplete în contract" : "Datele contractului au fost completate"));
                await using var update = Command(connection, transaction,
                    "UPDATE service_contracts SET cycle_months=@cycle, valid_until=@validUntil, reference_date=@reference, is_frozen=@frozen, missing_info=@missing, version=@version WHERE id=@id AND version=@oldVersion",
                    ("@cycle", restored.CycleMonths), ("@validUntil", restored.ValidUntil is { } until ? SqlDate(until) : null),
                    ("@reference", restored.ReferenceDate is { } reference ? SqlDate(reference) : null), ("@frozen", state.Frozen ? 1 : 0), ("@missing", ServiceContractFreeze.Stored(state.Missing)),
                    ("@version", checked(contract.Version + 1)), ("@id", contract.Id), ("@oldVersion", contract.Version));
                if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1) throw ServiceContractRules.Changed();
            }
            await SyncDuesAsync(connection, transaction, contract.Id, cancellationToken).ConfigureAwait(false);
            await using (var cancel = Command(connection, transaction, """
                UPDATE service_contract_addenda SET status='Anulat', cancelled_by=@by, cancelled_utc=@now, cancel_reason=@reason, version=version+1 WHERE id=@id AND version=@version
                """, ("@by", user), ("@now", MariaTimeText.Format(DateTime.UtcNow)), ("@reason", motif), ("@id", current.Id), ("@version", current.Version)))
                if (await cancel.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1) throw ServiceContractAddendumRules.Changed();

            var changes = new List<AuditChange>();
            if (kinds.Contains(ServiceAddendumKinds.Validity)) changes.Add(new("Expiră", ServiceContractRules.ValidUntilText(contract.ValidUntil), ServiceContractRules.ValidUntilText(restored.ValidUntil)));
            if (kinds.Contains(ServiceAddendumKinds.Cycle)) changes.Add(new("Ciclicitate contract", ServiceContractRules.CycleText(contract.CycleMonths), ServiceContractRules.CycleText(restored.CycleMonths)));
            if (kinds.Contains(ServiceAddendumKinds.Reference)) changes.Add(new("Data de referință", ServiceContractRules.ReferenceDateText(contract.ReferenceDate), ServiceContractRules.ReferenceDateText(restored.ReferenceDate)));
            var parts = new List<string> { AuditDetails.Identification(("Act adițional", current.Label), ("Stare", "Aplicat → Anulat")) };
            if (changes.Count > 0) parts.Add(AuditDetails.Changes([.. changes]));
            if (kinds.Contains(ServiceAddendumKinds.Points)) parts.Add("Punctele contractului au fost readuse la situația dinainte de act");
            if (kinds.Contains(ServiceAddendumKinds.Components)) parts.Add("Componentele contractului au fost readuse la situația dinainte de act");
            pending.Insert(0, new(AuditActions.CancelServiceContractAddendum, ServiceContractAddendumRules.Target(beneficiaryId, ownerName, contract.Label, current.Label), string.Join("; ", parts), motif));
            return await LoadDetailsAsync(connection, transaction, contract.Id, cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
        await RecordAsync(beneficiaryId, pending, cancellationToken).ConfigureAwait(false);
        return details;
    }

    public async Task DeleteAsync(ServiceContractAddendum original, string reason, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAsync("mentenanta.delete", cancellationToken).ConfigureAwait(false);
        var motif = ChangeReasonRules.Normalize(reason);
        if (ChangeReasonRules.ValidationError(motif) is { } reasonError) throw new ServiceContractOperationException(reasonError);
        int beneficiaryId;
        await using (var lookup = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false))
            beneficiaryId = await BeneficiaryOfContractAsync(lookup, original.ContractId, cancellationToken).ConfigureAwait(false);
        var pending = new List<Pending>();
        await WriteAsync(async (connection, transaction) =>
        {
            pending.Clear();
            var ownerName = await LockBeneficiaryAsync(connection, transaction, beneficiaryId, cancellationToken).ConfigureAwait(false);
            var contract = await GetLockedAsync(connection, transaction, original.ContractId, cancellationToken).ConfigureAwait(false) ?? throw ServiceContractRules.Changed();
            var current = await GetAddendumLockedAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false);
            if (current is null || current.Version != original.Version) throw ServiceContractAddendumRules.Changed();
            if (current.IsApplied && current.HasEffect) throw ServiceContractAddendumRules.NotDeletable();
            await using var delete = Command(connection, transaction, "DELETE FROM service_contract_addenda WHERE id=@id AND version=@version", ("@id", current.Id), ("@version", current.Version));
            if (await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1) throw ServiceContractAddendumRules.Changed();
            pending.Add(new(AuditActions.DeleteServiceContractAddendum, ServiceContractAddendumRules.Target(beneficiaryId, ownerName, contract.Label, current.Label),
                AuditDetails.Identification(("Act adițional", current.Label), ("Stare", current.Status), ("Modifica", string.Join(", ", current.Kinds.Select(ServiceAddendumKinds.Label))),
                    ("Efecte", current.Changes), ("Descriere", current.Summary)), motif));
            return true;
        }, cancellationToken).ConfigureAwait(false);
        await RecordAsync(beneficiaryId, pending, cancellationToken).ConfigureAwait(false);
    }
}
