using Microsoft.Extensions.Configuration;
using MySqlConnector;
using BlazorStoc.Services;

namespace BlazorStoc.Checks;

public static partial class MariaExtendedChecks
{
    // ---- Acte aditionale la contractele de mentenanta ----------------------------------------------------------------------------
    private static async Task ContractAddendaAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe)
    {
        var suffix = Suffix();
        var beneficiaries = new MariaBeneficiaryRepository(configuration, admin, audit);
        var workPoints = new MariaWorkPointRepository(configuration, admin, audit);
        var contracts = new MariaServiceContractRepository(configuration, admin, audit);
        var since = DateTime.UtcNow;
        await Task.Delay(30);
        var owner = await beneficiaries.CreateAsync(Legal($"Ext Acte {suffix} SRL", "RO" + Random.Shared.Next(30000000, 39999999)));
        async Task<ServiceContractAddendum> FreshAddendum(int id) => (await contracts.GetAddendaForBeneficiaryAsync(owner.Id)).Single(item => item.Id == id);
        async Task<int> Events(string action) => (await audit.GetEventsAsync()).Count(item => item.TimestampUtc > since && item.EntityType == AuditEntities.Beneficiary && item.EntityId == owner.Id.ToString() && item.Action == action);
        ServiceContractPointInput Point(WorkPoint point, DateOnly? due = null, int? cycle = null) => new() { WorkPointId = point.Id, NextDue = due, CycleMonths = cycle };
        ServiceContractAddendumInput Addendum(string number, DateOnly date) => new() { Number = number, Date = date };
        DateOnly DueOf(ServiceContractDetails details, WorkPoint point) => details.Points.Single(item => item.Point.WorkPointId == point.Id).Point.NextDue;
        try
        {
            var main = (await workPoints.GetAsync(owner.Id)).Single();
            var depozit = await workPoints.CreateAsync(owner.Id, new WorkPointInput { Name = "Depozit", Address = "Str. Depozitului 1, Cluj" });
            var atelier = await workPoints.CreateAsync(owner.Id, new WorkPointInput { Name = "Atelier", Address = "Str. Atelierului 2, Cluj" });
            var d1 = new DateOnly(2026, 1, 15);
            var d2 = new DateOnly(2026, 2, 20);
            var unit = await ContractTestUnits.DiscreteAsync(configuration, admin, audit);
            var contract = await contracts.CreateAsync(owner.Id, new ServiceContractInput { NumberText = "7/01.01.2026", CycleMonths = 3, ValidUntil = new DateOnly(2026, 12, 31), Points = [Point(main, d1), Point(depozit, d2)] }.Complete(unit));

            // Validity: the addendum stores the old and the new date and changes the contract in the same operation.
            var validity = Addendum("1", new DateOnly(2026, 6, 1)); validity.ChangeValidity = true; validity.NewValidUntil = new DateOnly(2027, 12, 31);
            var afterValidity = await contracts.AddAsync(contract.Contract, validity);
            var aa1 = (await contracts.GetAddendaForBeneficiaryAsync(owner.Id)).Single();
            Check(afterValidity.Contract.ValidUntil == new DateOnly(2027, 12, 31) && afterValidity.Contract.Version == contract.Contract.Version + 1 && aa1.IsApplied && aa1.Label == "AA 1/01.06.2026" &&
                  aa1.PrevValidUntil == new DateOnly(2026, 12, 31) && aa1.NewValidUntil == new DateOnly(2027, 12, 31) && aa1.Kinds.SequenceEqual([ServiceAddendumKinds.Validity]),
                "Addendum: a validity addendum changes the contract and keeps the old and the new date");
            Check(await Events(AuditActions.CreateServiceContractAddendum) == 1, "Addendum: the creation is journaled with its own action");

            // Refusals.
            await Rejects<ServiceContractOperationException>(() => contracts.AddAsync(afterValidity.Contract, validity), "The same addendum number and date is refused");
            var none = Addendum("2", new DateOnly(2026, 6, 2));
            await Rejects<ServiceContractOperationException>(() => contracts.AddAsync(afterValidity.Contract, none), "An addendum that changes nothing is refused");
            var other = Addendum("2", new DateOnly(2026, 6, 2)); other.Other = true;
            await Rejects<ServiceContractOperationException>(() => contracts.AddAsync(afterValidity.Contract, other), "Other clauses need a description");
            var early = Addendum("2", new DateOnly(2026, 6, 2)); early.ChangeValidity = true; early.NewValidUntil = new DateOnly(2025, 1, 1);
            await Rejects<ServiceContractOperationException>(() => contracts.AddAsync(afterValidity.Contract, early), "A validity before the date of the contract is refused");
            var limited = new MariaServiceContractRepository(configuration, new TestAccessControl(false, "limitat", productOperator: false), audit);
            var describedOther = Addendum("2", new DateOnly(2026, 6, 2)); describedOther.Other = true; describedOther.Summary = "x";
            await Rejects<AccessDeniedException>(() => limited.AddAsync(afterValidity.Contract, describedOther), "A user without the maintenance rights cannot add an addendum");
            Check((await contracts.GetAddendaForBeneficiaryAsync(owner.Id)).Count == 1, "Addendum: refused addenda leave nothing behind");

            // Cycle: the points get the proposed due dates (shifted by the difference of the cycles) and the contract the new cycle.
            var cycle = Addendum("2", new DateOnly(2026, 7, 1)); cycle.ChangeCycle = true; cycle.NewCycleMonths = 6;
            cycle.Points = [Point(main, ServiceContractAddendumRules.ProposedDue(d1, 3, 6)), Point(depozit, ServiceContractAddendumRules.ProposedDue(d2, 3, 6))];
            var afterCycle = await contracts.AddAsync(afterValidity.Contract, cycle);
            Check(afterCycle.Contract.CycleMonths == 6 && DueOf(afterCycle, main) == new DateOnly(2026, 4, 15) && DueOf(afterCycle, depozit) == new DateOnly(2026, 5, 20),
                "Addendum: a cycle addendum changes the cycle and the due dates of the points as proposed");
            var aa2 = (await contracts.GetAddendaForBeneficiaryAsync(owner.Id)).Single(item => item.Number == "2");
            Check(aa2.PrevCycleMonths == 3 && aa2.NewCycleMonths == 6 && aa2.PointsBefore.Count == 2 && aa2.PointsAfter.Count == 2, "Addendum: the cycle and the coverage before and after are kept");

            // Points: a point added (with its first due date) and one taken out.
            var points = Addendum("3", new DateOnly(2026, 8, 1)); points.ChangePoints = true;
            points.Points = [Point(main, new DateOnly(2026, 4, 15)), Point(atelier, new DateOnly(2026, 9, 1))];
            var afterPoints = await contracts.AddAsync(afterCycle.Contract, points);
            Check(afterPoints.Points.Select(item => item.Point.WorkPointId).Order().SequenceEqual(new[] { main.Id, atelier.Id }.Order()), "Addendum: a points addendum adds and removes covered work points");
            var aa3 = (await contracts.GetAddendaForBeneficiaryAsync(owner.Id)).Single(item => item.Number == "3");
            Check(aa3.Changes.Contains("puncte: +1 −1"), "Addendum: the list shows what changed (points added and removed)");

            // Cancel: only the last applied one.
            await Rejects<ServiceContractOperationException>(() => contracts.CancelAsync(aa2, "Anulare proba"), "Only the last applied addendum can be cancelled");
            await Rejects<ServiceContractOperationException>(() => contracts.CancelAsync(aa3, ""), "Cancelling an addendum needs a reason");
            var afterCancel3 = await contracts.CancelAsync(aa3, "Anulare proba");
            Check(afterCancel3.Points.Select(item => item.Point.WorkPointId).Order().SequenceEqual(new[] { main.Id, depozit.Id }.Order()) && (await FreshAddendum(aa3.Id)).Status == ServiceAddendumStatus.Cancelled,
                "Addendum: cancelling the last one restores the coverage and marks it Anulat");
            await Rejects<ServiceContractOperationException>(() => contracts.CancelAsync(aa3, "Din nou"), "A cancelled addendum cannot be cancelled again");
            var cancelled2 = await contracts.CancelAsync(await FreshAddendum(aa2.Id), "Anulare proba 2");
            Check(cancelled2.Contract.CycleMonths == 3, "Addendum: after the newer one is cancelled, the next one can be cancelled and the cycle comes back");
            Check(await Events(AuditActions.CancelServiceContractAddendum) == 2, "Addendum: each cancellation is journaled with its own action");

            // The contract changed after the addendum: it can no longer be cancelled.
            var edited = await contracts.UpdateAsync(cancelled2.Contract, new ServiceContractInput { NumberText = cancelled2.Contract.Label, CycleMonths = 3, ValidUntil = new DateOnly(2028, 6, 30),
                Points = [Point(main, DueOf(cancelled2, main)), Point(depozit, DueOf(cancelled2, depozit))] }.Complete(unit));
            await Rejects<ServiceContractOperationException>(() => contracts.CancelAsync(aa1, "Dupa modificare"), "An addendum cannot be cancelled after the contract was changed on the same data");

            // Other clauses only: no effect on the contract; allowed on an Off contract; deletable.
            var off = await contracts.DeactivateAsync(edited.Contract);
            var effectOnOff = Addendum("4", new DateOnly(2026, 9, 1)); effectOnOff.ChangeValidity = true; effectOnOff.NewValidUntil = new DateOnly(2029, 1, 1);
            await Rejects<ServiceContractOperationException>(() => contracts.AddAsync(off.Contract, effectOnOff), "An addendum with effects is refused on an Off contract");
            var clauses = Addendum("4", new DateOnly(2026, 9, 1)); clauses.Other = true; clauses.Summary = "Pretul se indexeaza anual";
            var afterClauses = await contracts.AddAsync(off.Contract, clauses);
            Check(afterClauses.Contract.ValidUntil == off.Contract.ValidUntil && afterClauses.Contract.CycleMonths == off.Contract.CycleMonths && !afterClauses.Contract.IsActive,
                "Addendum: other clauses are only kept, the contract is not changed (even when Off)");
            var aa4 = (await contracts.GetAddendaForBeneficiaryAsync(owner.Id)).Single(item => item.Number == "4");

            // Edit of the descriptive fields.
            var renamed = await contracts.UpdateAsync(aa4, new ServiceContractAddendumEdit { Number = "4A", Date = aa4.Date, EffectiveDate = new DateOnly(2026, 10, 1), Summary = "Pretul se indexeaza anual cu IPC" });
            Check(renamed.Number == "4A" && renamed.Summary.EndsWith("IPC", StringComparison.Ordinal) && await Events(AuditActions.EditServiceContractAddendum) == 1,
                "Addendum: the number, date, effective date and description can be corrected, journaled with its own action");

            // Deleting: not an applied addendum with effects; a cancelled one or one without effect, yes; a contract with addenda is not deletable.
            await Rejects<ServiceContractOperationException>(() => contracts.DeleteAsync(aa1, "Stergere proba"), "An applied addendum with effects cannot be deleted");
            await Rejects<ServiceContractOperationException>(() => contracts.DeleteAsync(afterClauses.Contract, "Stergere proba"), "A contract with addenda cannot be deleted");
            await contracts.DeleteAsync(renamed, "Stergere proba");
            await contracts.DeleteAsync(await FreshAddendum(aa2.Id), "Stergere proba");
            await contracts.DeleteAsync(await FreshAddendum(aa3.Id), "Stergere proba");
            Check((await contracts.GetAddendaForBeneficiaryAsync(owner.Id)).Select(item => item.Number).SequenceEqual(["1"]) && await Events(AuditActions.DeleteServiceContractAddendum) == 3,
                "Addendum: cancelled ones and ones without effect are deleted, journaled with their own action");
        }
        finally
        {
            await using (var cleanup = new MySqlCommand("DELETE a FROM service_contract_addenda a JOIN service_contracts c ON c.id=a.contract_id WHERE c.beneficiary_id=@id", probe)) { cleanup.Parameters.AddWithValue("@id", owner.Id); await cleanup.ExecuteNonQueryAsync(); }
            foreach (var details in await contracts.GetForBeneficiaryAsync(owner.Id)) await contracts.DeleteAsync(details.Contract, "Ext curatare");
            if ((await beneficiaries.GetBeneficiariesAsync()).FirstOrDefault(current => current.Id == owner.Id) is { } live) await beneficiaries.DeleteAsync(live, "Ext curatare");
        }
    }
}
