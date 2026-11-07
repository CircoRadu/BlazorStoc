using Microsoft.Extensions.Configuration;
using MySqlConnector;
using BlazorStoc.Services;

namespace BlazorStoc.Checks;

public static partial class MariaExtendedChecks
{
    // ---- Scadente pe componenta, interventii cu componente, acte aditionale cu componente si referinta ---------------------------
    private static async Task ContractUnitDuesAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe)
    {
        var suffix = Suffix();
        var beneficiaries = new MariaBeneficiaryRepository(configuration, admin, audit);
        var workPoints = new MariaWorkPointRepository(configuration, admin, audit);
        var contracts = new MariaServiceContractRepository(configuration, admin, audit);
        var interventions = new MariaServiceInterventionRepository(configuration, admin, audit);
        var catalogRepository = new MariaMaintenanceComponentRepository(configuration, admin, audit);
        var owner = await beneficiaries.CreateAsync(Legal($"Ext Scadente unitati {suffix} SRL", "RO" + Random.Shared.Next(50000000, 59999999)));
        async Task<ServiceContractDetails> Fresh(int contractId) => (await contracts.GetForBeneficiaryAsync(owner.Id)).Single(item => item.Contract.Id == contractId);
        async Task<long> Count(string sql, params (string, object)[] parameters) => await ScalarLongAsync(probe, sql, parameters);
        ServiceContractPointInput Point(WorkPoint point, DateOnly? due = null) => new() { WorkPointId = point.Id, NextDue = due };
        var family = await catalogRepository.CreateFamilyAsync("Securitate " + suffix);
        var efractie = await catalogRepository.CreateComponentAsync("Efractie " + suffix, family.Id);
        var tvci = await catalogRepository.CreateComponentAsync("TVCI " + suffix, family.Id);
        var incendiu = await catalogRepository.CreateComponentAsync("Incendiu " + suffix, null);
        var acces = await catalogRepository.CreateComponentAsync("Acces " + suffix, null);
        var other = await catalogRepository.CreateComponentAsync("Altceva " + suffix, null);
        var today = DateOnly.FromDateTime(DateTime.Now);
        try
        {
            var main = (await workPoints.GetAsync(owner.Id)).Single();
            var depozit = await workPoints.CreateAsync(owner.Id, new WorkPointInput { Name = "Depozit", Address = "Str. Depozitului 1, Cluj" });
            var reference = today.AddMonths(-2);
            var firstDue = today.AddDays(-10); // overdue: the date of the point can be before the visit
            var contract = await contracts.CreateAsync(owner.Id, new ServiceContractInput
            {
                NumberText = "9/10.01.2026", ReferenceDate = reference, Points = [Point(main, firstDue)],
                Units =
                [
                    new ServiceContractUnitInput { FamilyId = family.Id, CycleMonths = 3, Members = [new() { ComponentId = efractie.Id }, new() { ComponentId = tvci.Id }] },
                    new ServiceContractUnitInput { ComponentId = incendiu.Id, CycleMonths = 6 }
                ]
            });
            var familyUnit = contract.Units.Single(unit => unit.FamilyId == family.Id);
            var fireUnit = contract.Units.Single(unit => unit.ComponentId == incendiu.Id);
            Check(!contract.Contract.IsFrozen && contract.Dues.Count == 2 && contract.Dues.All(due => due.NextDue == firstDue) && contract.Points.Single().Point.NextDue == firstDue,
                "Dues: a point of a complete contract has one due date per unit (the date of the first visit) and its own date is the earliest");
            var listed = (await contracts.GetDueListAsync(true)).Single(row => row.Contract.Id == contract.Contract.Id);
            Check(listed.UnitDues.Count == 2 && listed.Point.Point.NextDue == firstDue, "Dues: the due list carries the dates per unit");

            // A unit added later gets reference date + its cycle; a unit switched Off loses its date; the date of the point stays the earliest.
            var addUnit = ServiceContractInput.From(contract);
            addUnit.Units.Add(new ServiceContractUnitInput { ComponentId = acces.Id, CycleMonths = 1 });
            var withAcces = await contracts.UpdateAsync(contract.Contract, addUnit);
            var accesUnit = withAcces.Units.Single(unit => unit.ComponentId == acces.Id);
            var accesDue = withAcces.Dues.Single(due => due.UnitId == accesUnit.Id).NextDue;
            Check(withAcces.Dues.Count == 3 && accesDue == reference.AddMonths(1) && withAcces.Points.Single().Point.NextDue == (accesDue < firstDue ? accesDue : firstDue),
                "Dues: a unit added to a running point gets reference date + cycle, and the point takes the earliest date");
            var offUnit = ServiceContractInput.From(withAcces);
            offUnit.Units.Single(unit => unit.ComponentId == acces.Id).IsActive = false;
            var withoutAcces = await contracts.UpdateAsync(withAcces.Contract, offUnit);
            Check(withoutAcces.Dues.Count == 2 && withoutAcces.Points.Single().Point.NextDue == firstDue, "Dues: a unit switched Off loses its date and the point goes back to the earliest of the others");

            // The components offered for the point: the ones the contract covers (required); a point without a contract gets the nomenclator (not required).
            var choice = await interventions.GetComponentChoiceAsync(owner.Id, main.Id);
            Check(choice.Required && choice.Options.Select(option => option.ComponentId).Order().SequenceEqual(new[] { efractie.Id, tvci.Id, incendiu.Id }.Order()) && choice.Options.All(option => option.ComponentId != acces.Id),
                "Intervention: under a complete contract the components offered are the ones it covers (an element or unit that is Off is not offered)");
            var noContract = await interventions.GetComponentChoiceAsync(owner.Id, depozit.Id);
            Check(!noContract.Required && noContract.Options.Any(option => option.ComponentId == acces.Id) && noContract.Options.Count >= 5, "Intervention: a point without a contract offers the whole nomenclator, not required");

            // An intervention moves only the units that cover the chosen components.
            await Rejects<ServiceInterventionOperationException>(() => interventions.RecordAsync(owner.Id, new ServiceInterventionInput { WorkPointId = main.Id, PerformedOn = today, ComponentIds = [other.Id] }),
                "Intervention: a component the contract does not cover is refused");
            var fireVisit = await interventions.RecordAsync(owner.Id, new ServiceInterventionInput { WorkPointId = main.Id, PerformedOn = today, ComponentIds = [incendiu.Id], Notes = "Verificare incendiu" });
            var afterFire = await Fresh(contract.Contract.Id);
            var dues = afterFire.Dues.ToDictionary(due => due.UnitId, due => due.NextDue);
            Check(dues[fireUnit.Id] == today.AddMonths(6) && dues[familyUnit.Id] == firstDue && afterFire.Points.Single().Point.NextDue == firstDue && fireVisit.PreviousDue == firstDue && fireVisit.NewDue == today.AddMonths(6),
                "Intervention: only the unit of the chosen component moves (from the date performed + its cycle); the other keeps its date, so the date of the point does not move (the intervention keeps the dates of the unit it moved)");
            var components = await interventions.GetComponentsAsync([fireVisit.Intervention.Id]);
            Check(components.TryGetValue(fireVisit.Intervention.Id, out var saved) && saved.SequenceEqual([incendiu.Id]), "Intervention: the components covered are stored with it");
            var tvciVisit = await interventions.RecordAsync(owner.Id, new ServiceInterventionInput { WorkPointId = main.Id, PerformedOn = today, ComponentIds = [tvci.Id] });
            var afterTvci = await Fresh(contract.Contract.Id);
            var duesTvci = afterTvci.Dues.ToDictionary(due => due.UnitId, due => due.NextDue);
            Check(duesTvci[familyUnit.Id] == today.AddMonths(3) && duesTvci[fireUnit.Id] == today.AddMonths(6) && afterTvci.Points.Single().Point.NextDue == today.AddMonths(3),
                "Intervention: a component of a family moves the unit of the family (its cycle), and the point takes the new earliest date");

            // Correcting the date and deleting give back exactly the dates of the units the intervention moved.
            var corrected = ServiceInterventionInput.From(tvciVisit.Intervention);
            corrected.Basis = ServiceNextDueBasis.Chosen;
            corrected.ChosenDue = today.AddMonths(2);
            await interventions.UpdateAsync(tvciVisit.Intervention, corrected);
            var afterCorrection = await Fresh(contract.Contract.Id);
            Check(afterCorrection.Dues.Single(due => due.UnitId == familyUnit.Id).NextDue == today.AddMonths(2) && afterCorrection.Dues.Single(due => due.UnitId == fireUnit.Id).NextDue == today.AddMonths(6) &&
                  afterCorrection.Points.Single().Point.NextDue == today.AddMonths(2), "Intervention: correcting the next date changes only the dates of the units it moved");
            var latest = (await interventions.GetForBeneficiaryAsync(owner.Id)).Single(item => item.Id == tvciVisit.Intervention.Id);
            await interventions.DeleteAsync(latest, "Stergere proba scadente");
            var afterDelete = await Fresh(contract.Contract.Id);
            Check(afterDelete.Dues.Single(due => due.UnitId == familyUnit.Id).NextDue == firstDue && afterDelete.Dues.Single(due => due.UnitId == fireUnit.Id).NextDue == today.AddMonths(6) &&
                  afterDelete.Points.Single().Point.NextDue == firstDue, "Intervention: deleting the latest one gives back the dates of the units it moved");

            // An act changes the reference date and the components: units On/Off and new ones, dates proposed by the caller, snapshots kept, cancellation puts the units and the reference back.
            var legacyVisit = await interventions.RecordAsync(owner.Id, new ServiceInterventionInput { WorkPointId = main.Id, PerformedOn = today.AddDays(-1) });
            Check(legacyVisit.Intervention.Id > 0, "Act: an intervention recorded without components exists to be verified");
            var actUnits = ServiceContractInput.From(await Fresh(contract.Contract.Id)).Units;
            actUnits.Single(unit => unit.ComponentId == incendiu.Id).IsActive = false;
            actUnits.Single(unit => unit.FamilyId == family.Id).Members.Single(member => member.ComponentId == efractie.Id).IsActive = false;
            var accesInAct = actUnits.Single(unit => unit.ComponentId == acces.Id);
            accesInAct.IsActive = true;
            accesInAct.CycleMonths = 2;
            var newReference = today.AddMonths(-1);
            var current = await Fresh(contract.Contract.Id);
            var addendumDate = today;
            var act = new ServiceContractAddendumInput
            {
                Number = "3", Date = addendumDate, ChangeReference = true, NewReferenceDate = newReference, ChangeComponents = true, Units = actUnits,
                Points = [Point(main, today.AddMonths(1))],
                InterventionComponents = [new AddendumInterventionComponents { InterventionId = legacyVisit.Intervention.Id, ComponentIds = [tvci.Id] }]
            };
            var afterAct = await contracts.AddAsync(current.Contract, act);
            var actUnitsNow = afterAct.Units;
            Check(afterAct.Contract.ReferenceDate == newReference && actUnitsNow.Count(unit => unit.Covers) == 2 && !actUnitsNow.Single(unit => unit.ComponentId == incendiu.Id).IsActive &&
                  !actUnitsNow.Single(unit => unit.FamilyId == family.Id).Members.Single(member => member.ComponentId == efractie.Id).IsActive && actUnitsNow.Any(unit => unit.ComponentId == acces.Id && unit.CycleMonths == 2),
                "Act: the reference date, the units On/Off and the added component are applied to the contract");
            Check(afterAct.Dues.Count == 2 && afterAct.Dues.All(due => due.UnitId != fireUnit.Id) && afterAct.Dues.Any(due => due.UnitId == actUnitsNow.Single(unit => unit.ComponentId == acces.Id).Id) &&
                  (await interventions.GetComponentsAsync([legacyVisit.Intervention.Id]))[legacyVisit.Intervention.Id].SequenceEqual([tvci.Id]), "Act: the dates follow the units that cover and the components of the verified intervention are stored");
            var addenda = await contracts.GetAddendaForBeneficiaryAsync(owner.Id);
            var stored = addenda.Single();
            Check(stored.Kinds.Contains(ServiceAddendumKinds.Reference) && stored.Kinds.Contains(ServiceAddendumKinds.Components) && stored.ReferenceBefore == reference && stored.ReferenceAfter == newReference &&
                  stored.UnitsBefore!.Count == 3 && stored.UnitsAfter!.Count == 3 && stored.Changes.Contains("componente", StringComparison.Ordinal) && stored.Changes.Contains("data de referință", StringComparison.Ordinal),
                "Act: the reference date and the units before and after are kept with the act");
            var cancelled = await contracts.CancelAsync(stored, "Anulare proba");
            Check(cancelled.Contract.ReferenceDate == reference && cancelled.Units.Count(unit => unit.Covers) == 2 && !cancelled.Units.Single(unit => unit.ComponentId == acces.Id).IsActive && cancelled.Units.Single(unit => unit.ComponentId == incendiu.Id).IsActive &&
                  cancelled.Units.Single(unit => unit.FamilyId == family.Id).Members.All(member => member.IsActive) && cancelled.Dues.Any(due => due.UnitId == fireUnit.Id),
                "Act: the cancellation puts the reference date and the units back");

            // A contract changed after the act cannot have it cancelled.
            var second = await contracts.AddAsync(cancelled.Contract, new ServiceContractAddendumInput { Number = "4", Date = today, ChangeReference = true, NewReferenceDate = today.AddMonths(-3), Points = [Point(main, today.AddMonths(1))] });
            var edited = ServiceContractInput.From(second);
            edited.Units.Single(unit => unit.ComponentId == incendiu.Id).CycleMonths = 5;
            await contracts.UpdateAsync(second.Contract, edited);
            var secondAddendum = (await contracts.GetAddendaForBeneficiaryAsync(owner.Id)).Single(item => item.Number == "4");
            Check(secondAddendum.Kinds.SequenceEqual([ServiceAddendumKinds.Reference]) && secondAddendum.ReferenceAfter == today.AddMonths(-3), "Act: a reference date act keeps its date");
        }
        finally
        {
            await using (var cleanup = new MySqlCommand("DELETE a FROM service_contract_addenda a JOIN service_contracts c ON c.id=a.contract_id WHERE c.beneficiary_id=@id", probe)) { cleanup.Parameters.AddWithValue("@id", owner.Id); await cleanup.ExecuteNonQueryAsync(); }
            foreach (var intervention in await interventions.GetForBeneficiaryAsync(owner.Id)) await interventions.DeleteAsync(intervention, "Ext curatare");
            foreach (var details in await contracts.GetForBeneficiaryAsync(owner.Id)) await contracts.DeleteAsync(details.Contract, "Ext curatare");
            if ((await beneficiaries.GetBeneficiariesAsync()).FirstOrDefault(current => current.Id == owner.Id) is { } live) await beneficiaries.DeleteAsync(live, "Ext curatare");
        }
    }
}
