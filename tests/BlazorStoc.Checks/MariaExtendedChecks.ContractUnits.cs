using Microsoft.Extensions.Configuration;
using MySqlConnector;
using BlazorStoc.Services;

namespace BlazorStoc.Checks;

public static partial class MariaExtendedChecks
{
    // ---- Contracte de mentenanta cu componente: referinta, unitati (familii / componente), inghetare ------------------------------
    private static async Task ContractUnitsAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe)
    {
        var suffix = Suffix();
        var beneficiaries = new MariaBeneficiaryRepository(configuration, admin, audit);
        var workPoints = new MariaWorkPointRepository(configuration, admin, audit);
        var contracts = new MariaServiceContractRepository(configuration, admin, audit);
        var interventions = new MariaServiceInterventionRepository(configuration, admin, audit);
        var catalogRepository = new MariaMaintenanceComponentRepository(configuration, admin, audit);
        var notifications = new MariaMaintenanceNotificationReader(configuration);
        var since = DateTime.UtcNow;
        await Task.Delay(30);
        var owner = await beneficiaries.CreateAsync(Legal($"Ext Unitati {suffix} SRL", "RO" + Random.Shared.Next(40000000, 49999999)));
        async Task<int> Events(string action) => (await audit.GetEventsAsync()).Count(item => item.TimestampUtc > since && item.EntityType == AuditEntities.Beneficiary && item.EntityId == owner.Id.ToString() && item.Action == action);
        async Task<string> EventDetails(string action) => string.Join(" | ", (await audit.GetEventsAsync()).Where(item => item.TimestampUtc > since && item.EntityType == AuditEntities.Beneficiary && item.EntityId == owner.Id.ToString() && item.Action == action).Select(item => item.Details));
        async Task<ServiceContractDetails> Fresh(int contractId) => (await contracts.GetForBeneficiaryAsync(owner.Id)).Single(item => item.Contract.Id == contractId);
        async Task<long> Count(string sql, params (string, object)[] parameters) => await ScalarLongAsync(probe, sql, parameters);
        ServiceContractPointInput Point(WorkPoint point, DateOnly? due = null) => new() { WorkPointId = point.Id, NextDue = due };
        string Missing(ServiceContractDetails details) => ServiceContractFreeze.Summary(ServiceContractFreeze.Missing(details, unit => unit.FamilyId is null ? $"componenta {unit.ComponentId}" : $"familia {unit.FamilyId}"));
        var family = await catalogRepository.CreateFamilyAsync("Securitate " + suffix);
        var efractie = await catalogRepository.CreateComponentAsync("Efractie " + suffix, family.Id);
        var tvci = await catalogRepository.CreateComponentAsync("TVCI " + suffix, family.Id);
        var incendiu = await catalogRepository.CreateComponentAsync("Incendiu " + suffix, null);
        var acces = await catalogRepository.CreateComponentAsync("Acces " + suffix, null);
        var inactive = await catalogRepository.CreateComponentAsync("Inactiva " + suffix, null);
        inactive = await catalogRepository.SetComponentActiveAsync(inactive, false);
        var today = DateOnly.FromDateTime(DateTime.Now);
        try
        {
            var main = (await workPoints.GetAsync(owner.Id)).Single();
            var depozit = await workPoints.CreateAsync(owner.Id, new WorkPointInput { Name = "Depozit", Address = "Str. Depozitului 1, Cluj" });
            var atelier = await workPoints.CreateAsync(owner.Id, new WorkPointInput { Name = "Atelier", Address = "Str. Atelierului 2, Cluj" });
            var reference = new DateOnly(2026, 1, 10);
            ServiceContractUnitInput FamilyUnit(int? cycle = null, DateOnly? unitReference = null, bool efractieOn = true, bool tvciOn = true) => new()
            {
                FamilyId = family.Id, CycleMonths = cycle, ReferenceDate = unitReference,
                Members = [new() { ComponentId = efractie.Id, IsActive = efractieOn }, new() { ComponentId = tvci.Id, IsActive = tvciOn }]
            };
            ServiceContractInput Input(string number, ServiceContractPointInput[] points, int? cycle, params ServiceContractUnitInput[] units) =>
                new() { NumberText = number, CycleMonths = cycle, ReferenceDate = reference, Points = [.. points], Units = [.. units] };

            // Validation: the reference date is mandatory; a family or a component only once; a member of a family only through the family; an inactive component is refused.
            var noReference = Input("1/10.01.2026", [Point(main, today.AddDays(30))], 3, new ServiceContractUnitInput { ComponentId = incendiu.Id });
            noReference.ReferenceDate = null;
            await Rejects<ServiceContractOperationException>(() => contracts.CreateAsync(owner.Id, noReference), "Contract: the reference date is mandatory");
            await Rejects<ServiceContractOperationException>(() => contracts.CreateAsync(owner.Id, Input("1/10.01.2026", [Point(main, today.AddDays(30))], 3,
                new ServiceContractUnitInput { ComponentId = incendiu.Id }, new ServiceContractUnitInput { ComponentId = incendiu.Id })), "Contract: a component can appear only once");
            await Rejects<ServiceContractOperationException>(() => contracts.CreateAsync(owner.Id, Input("1/10.01.2026", [Point(main, today.AddDays(30))], 3,
                new ServiceContractUnitInput { ComponentId = efractie.Id })), "Contract: a component of a family cannot be added as a discrete component");
            await Rejects<ServiceContractOperationException>(() => contracts.CreateAsync(owner.Id, Input("1/10.01.2026", [Point(main, today.AddDays(30))], 3,
                new ServiceContractUnitInput { ComponentId = inactive.Id })), "Contract: an inactive component cannot be added");
            await Rejects<ServiceContractOperationException>(() => contracts.CreateAsync(owner.Id, Input("1/10.01.2026", [Point(main, today.AddDays(30))], 3,
                new ServiceContractUnitInput { FamilyId = family.Id, Members = [new() { ComponentId = incendiu.Id }] })), "Contract: an element that is not of the family is refused");
            Check((await contracts.GetForBeneficiaryAsync(owner.Id)).Count == 0, "Contract: nothing is saved when the units are refused");

            // A complete contract: a family (one element Off, cycle of the family) and a discrete component (the global cycle); not frozen, units and members stored.
            var c1 = await contracts.CreateAsync(owner.Id, Input("1/10.01.2026", [Point(main, today.AddDays(30)), Point(depozit, today.AddDays(40))], 6,
                FamilyUnit(cycle: 3, efractieOn: false), new ServiceContractUnitInput { ComponentId = incendiu.Id, ReferenceDate = new DateOnly(2026, 2, 1) }));
            Check(!c1.Contract.IsFrozen && c1.Contract.ReferenceDate == reference && c1.Contract.CycleMonths == 6 && c1.Units.Count == 2, "Contract: a complete contract is not frozen and keeps its reference date, cycle and units");
            var familyUnit = c1.Units.Single(unit => unit.FamilyId == family.Id);
            var discreteUnit = c1.Units.Single(unit => unit.ComponentId == incendiu.Id);
            Check(familyUnit.CycleMonths == 3 && familyUnit.ReferenceDate is null && familyUnit.Members.Count == 2 && !familyUnit.Members.Single(member => member.ComponentId == efractie.Id).IsActive &&
                  familyUnit.Members.Single(member => member.ComponentId == tvci.Id).IsActive && familyUnit.Covers && discreteUnit.CycleMonths is null && discreteUnit.ReferenceDate == new DateOnly(2026, 2, 1) && discreteUnit.Members.Count == 0,
                "Contract: the family keeps its own cycle and its elements On/Off; the discrete component keeps its own reference date and uses the global cycle");
            var createDetails = await EventDetails(AuditActions.CreateServiceContract);
            Check(await Events(AuditActions.CreateServiceContract) == 1 && createDetails.Contains("Componente", StringComparison.Ordinal) && createDetails.Contains("Securitate " + suffix, StringComparison.Ordinal) &&
                  createDetails.Contains("(Off)", StringComparison.Ordinal), "Contract with units: the creation is journaled with the units and the elements that are Off");
            Check((await contracts.GetDueListAsync(true)).Count(row => row.Contract.Id == c1.Contract.Id) == 2 && (await notifications.GetDueAsync()).Count(item => item.BeneficiaryId == owner.Id) == 4,
                "Frozen: a complete contract produces due dates (list per point, notifications per point and unit)");
            Check(await Count("SELECT CAST(COLUMN_DEFAULT AS SIGNED) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='service_contracts' AND COLUMN_NAME='is_frozen'") == 0,
                "Frozen: the column is_frozen has the default 0 after the migration (the existing contracts were frozen by the migration itself)");

            // Frozen: no points, no unit On, no cycle for a unit, a family with every element Off.
            var noPoints = await contracts.CreateAsync(owner.Id, Input("2/10.01.2026", [], 6, new ServiceContractUnitInput { ComponentId = acces.Id }));
            Check(noPoints.Contract.IsFrozen && Missing(noPoints).Contains(ServiceContractFreeze.NoPoints, StringComparison.Ordinal), "Frozen: a contract without work points is frozen and says what is missing");
            var emptyFamily = await contracts.CreateAsync(owner.Id, Input("3/10.01.2026", [], 6, FamilyUnit(efractieOn: false, tvciOn: false)));
            Check(emptyFamily.Contract.IsFrozen && Missing(emptyFamily).Contains("nu are nicio componentă activă", StringComparison.Ordinal) && Missing(emptyFamily).Contains(ServiceContractFreeze.NoUnits, StringComparison.Ordinal),
                "Frozen: a family with every element Off covers nothing");
            var c4 = await contracts.CreateAsync(owner.Id, Input("4/10.01.2026", [Point(atelier, today.AddDays(20))], null, new ServiceContractUnitInput { ComponentId = acces.Id }));
            Check(c4.Contract.IsFrozen && c4.Contract.CycleMonths is null && Missing(c4).StartsWith("Ciclicitatea", StringComparison.Ordinal), "Frozen: a unit without cycle (and no global cycle) freezes the contract");
            Check((await contracts.GetDueListAsync(true)).All(row => row.Contract.Id != c4.Contract.Id) && (await notifications.GetDueAsync()).All(item => item.WorkPoint != "Atelier"),
                "Frozen: a frozen contract with points produces no due dates (list and notifications)");

            // An intervention under a frozen contract is recorded under it, but the due date does not move.
            var dueBefore = c4.Points.Single().Point.NextDue;
            var saved = await interventions.RecordAsync(owner.Id, new ServiceInterventionInput { Kind = ServiceInterventionKind.Maintenance, WorkPointId = atelier.Id, PerformedOn = today, Notes = "Verificare sub contract inghetat" });
            Check(saved.Intervention.ContractId == c4.Contract.Id && saved.NewDue is null && (await Fresh(c4.Contract.Id)).Points.Single().Point.NextDue == dueBefore,
                "Frozen: an intervention under a frozen contract is recorded but does not move the due date");

            // Completing the data unfreezes: the unit gets its own cycle; the contract enters the due list and the journal has the unfreeze with its own action.
            var edit = ServiceContractInput.From(c4);
            edit.Units[0].CycleMonths = 4;
            var c4Complete = await contracts.UpdateAsync(c4.Contract, edit);
            Check(!c4Complete.Contract.IsFrozen && (await contracts.GetDueListAsync(true)).Count(row => row.Contract.Id == c4.Contract.Id) == 1 && await Events(AuditActions.UnfreezeServiceContract) == 1 &&
                  await Events(AuditActions.EditContractComponents) == 1, "Frozen: completing the cycle of the unit unfreezes the contract (journal: unfreeze and the change of the components)");
            var freezeAgain = ServiceContractInput.From(c4Complete);
            freezeAgain.Units[0].IsActive = false;
            var c4Frozen = await contracts.UpdateAsync(c4Complete.Contract, freezeAgain);
            Check(c4Frozen.Contract.IsFrozen && Missing(c4Frozen).Contains(ServiceContractFreeze.NoUnits, StringComparison.Ordinal) && await Events(AuditActions.FreezeServiceContract) == 1 &&
                  (await EventDetails(AuditActions.FreezeServiceContract)).Contains("Lipsă", StringComparison.Ordinal), "Frozen: switching the only unit Off freezes the contract again (journal: freeze with what is missing)");

            // A contract that existed before the migration (no reference date, frozen): it cannot be saved without the reference date, and it works once the date is given.
            await using (var legacy = new MySqlCommand("UPDATE service_contracts SET reference_date=NULL, is_frozen=1 WHERE id=@id", probe)) { legacy.Parameters.AddWithValue("@id", c1.Contract.Id); await legacy.ExecuteNonQueryAsync(); }
            var legacyDetails = await Fresh(c1.Contract.Id);
            await Rejects<ServiceContractOperationException>(() => contracts.UpdateAsync(legacyDetails.Contract, ServiceContractInput.From(legacyDetails)), "Frozen: a contract without reference date cannot be saved until the date is given");
            var legacyInput = ServiceContractInput.From(legacyDetails);
            legacyInput.ReferenceDate = reference;
            var legacyFixed = await contracts.UpdateAsync(legacyDetails.Contract, legacyInput);
            Check(!legacyFixed.Contract.IsFrozen && legacyFixed.Contract.ReferenceDate == reference && await Events(AuditActions.EditContractReferenceDate) == 1,
                "Frozen: giving the reference date to a contract frozen by the migration unfreezes it (journal: change of the reference date)");

            // Editing the units: ids stay, the changes are journaled exactly; only a change of order is not journaled.
            var familyId = legacyFixed.Units.Single(unit => unit.FamilyId == family.Id).Id;
            var changeUnits = ServiceContractInput.From(legacyFixed);
            var changedFamily = changeUnits.Units.Single(unit => unit.FamilyId == family.Id);
            changedFamily.CycleMonths = 4;
            changedFamily.ReferenceDate = new DateOnly(2026, 3, 1);
            changedFamily.Members.Single(member => member.ComponentId == efractie.Id).IsActive = true;
            var afterUnits = await contracts.UpdateAsync(legacyFixed.Contract, changeUnits);
            var changedUnit = afterUnits.Units.Single(unit => unit.FamilyId == family.Id);
            var unitDetails = await EventDetails(AuditActions.EditContractComponents);
            Check(changedUnit.Id == familyId && changedUnit.CycleMonths == 4 && changedUnit.ReferenceDate == new DateOnly(2026, 3, 1) && changedUnit.Members.All(member => member.IsActive) &&
                  unitDetails.Contains("ciclicitate", StringComparison.Ordinal) && unitDetails.Contains("referință", StringComparison.Ordinal) && unitDetails.Contains("pornit (On)", StringComparison.Ordinal),
                "Units: the cycle, the reference date and the elements of a family are changed in place and journaled one by one");
            var eventsBeforeReorder = await Events(AuditActions.EditContractComponents);
            var reorder = ServiceContractInput.From(afterUnits);
            reorder.Units.Reverse();
            var reordered = await contracts.UpdateAsync(afterUnits.Contract, reorder);
            Check(reordered.Units[0].ComponentId == incendiu.Id && await Events(AuditActions.EditContractComponents) == eventsBeforeReorder, "Units: a change of order is stored but not journaled");
            var removeUnit = ServiceContractInput.From(reordered);
            removeUnit.Units.RemoveAll(unit => unit.ComponentId == incendiu.Id);
            var afterRemove = await contracts.UpdateAsync(reordered.Contract, removeUnit);
            Check(afterRemove.Units.Count == 1 && afterRemove.Units[0].FamilyId == family.Id && (await EventDetails(AuditActions.EditContractComponents)).Contains("scos: Incendiu " + suffix, StringComparison.Ordinal),
                "Units: a unit taken out of the contract is deleted and journaled");

            // The tab of the notifications: the frozen contracts with their beneficiary and what is missing.
            var frozen = await contracts.GetFrozenAsync();
            Check(frozen.Count(item => item.BeneficiaryId == owner.Id) == 3 && frozen.All(item => item.Details.Contract.IsFrozen) && await contracts.CountFrozenAsync() >= 3,
                "Frozen: the list of incomplete contracts has every frozen contract with its units, and the count follows");

            // Deleting a contract archives its units with it.
            var unitIds = afterRemove.Units.Select(unit => unit.Id).ToList();
            await contracts.DeleteAsync(afterRemove.Contract, "Stergere proba unitati");
            Check(await Count("SELECT COUNT(*) FROM service_contract_units WHERE contract_id=@id", ("@id", afterRemove.Contract.Id)) == 0 &&
                  await Count("SELECT COUNT(*) FROM archive_relations WHERE relation_type=@type AND original_relation_id=@id", ("@type", ArchiveRequests.ServiceContractUnitRelation), ("@id", unitIds[0].ToString())) == 1 &&
                  await Count("SELECT COUNT(*) FROM archive_service_contracts WHERE original_id=@id AND reference_date IS NOT NULL", ("@id", afterRemove.Contract.Id)) == 1,
                "Units: deleting a contract deletes its units and archives them with the contract (with its reference date)");
        }
        finally
        {
            foreach (var intervention in await interventions.GetForBeneficiaryAsync(owner.Id)) await interventions.DeleteAsync(intervention, "Ext curatare");
            foreach (var details in await contracts.GetForBeneficiaryAsync(owner.Id)) await contracts.DeleteAsync(details.Contract, "Ext curatare");
            if ((await beneficiaries.GetBeneficiariesAsync()).FirstOrDefault(current => current.Id == owner.Id) is { } live) await beneficiaries.DeleteAsync(live, "Ext curatare");
        }
    }
}
