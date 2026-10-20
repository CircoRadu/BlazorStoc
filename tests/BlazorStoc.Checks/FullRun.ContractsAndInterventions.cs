#nullable enable
#pragma warning disable CS1998, CS8600, CS8601, CS8602, CS8603, CS8604, CS8605, CS8618, CS8619, CS8620, CS8625, CS8629, CS8714
using BlazorStoc.Checks;
using BlazorStoc.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PdfSharp.Pdf.IO;
using System.IO.Compression;
using System.Text.Json;

public static partial class FullRun
{
    // Lines 2131-2311 of the former Program.cs.
    internal static async Task ContractsAndInterventionsAsync(string[] args)
    {

        // Maintenance contracts (pure rules): the number/date field, the input validation, the derived due state, the exact journal
        // operation of an edit, the registry entries and migration 8.
        {
            static string Parsed(string text) => ServiceContractNumber.TryParse(text, out var number, out var date, out _) ? $"{number}|{date:yyyy-MM-dd}" : "error";
            static bool Throws<T>(Action action) where T : Exception { try { action(); return false; } catch (T) { return true; } }
            Check(Parsed("26/23.09.2025") == "26|2025-09-23" && Parsed("26 / 23.09.2025") == "26|2025-09-23" && Parsed("26 din 23.09.2025") == "26|2025-09-23" &&
                  Parsed(" 26   DIN   3.9.2025 ") == "26|2025-09-03" && Parsed("ab-7/01.02.2026") == "AB-7|2026-02-01" && Parsed("12/A/23.09.2025") == "12/A|2025-09-23",
                "The contract number field accepts 26/23.09.2025, 26 / 23.09.2025 and 26 din 23.09.2025 (number in capitals, date read as zz.ll.aaaa)");
            Check(Parsed("26") == "error" && Parsed("26/32.13.2025") == "error" && Parsed("/23.09.2025") == "error" && Parsed("26/23-09-2025") == "error" &&
                  Parsed("26/23.09.1999") == "error" && Parsed(new string('9', 31) + "/23.09.2025") == "error" && Parsed("") == "error" && Parsed("26din23.09.2025") == "error",
                "A number without a date, an impossible date, an empty number, a date before 2000 or a number over 30 characters is refused");
            Check(ServiceContractNumber.Format("26", new DateOnly(2025, 9, 23)) == "26/23.09.2025" &&
                  new ServiceContract(1, 1, "26", new DateOnly(2025, 9, 23), 3, null, true, "", 0).Label == "26/23.09.2025",
                "The contract is shown as number/dd.MM.yyyy");

            static ServiceContractInput Contract(Action<ServiceContractInput>? change = null)
            {
                var value = new ServiceContractInput { NumberText = "26 din 23.09.2025", CycleMonths = 3, Points = [new() { WorkPointId = 5, NextDue = new DateOnly(2025, 10, 15) }] };
                change?.Invoke(value);
                return value;
            }
            var valid = Contract().Validated();
            Check(valid.Number == "26" && valid.Date == new DateOnly(2025, 9, 23) && valid.CycleMonths == 3 && valid.Points.Count == 1,
                "A valid contract input is normalized (number and date read from the field)");
            Check(Throws<ServiceContractOperationException>(() => Contract(value => value.CycleMonths = 0).Validated()) && Throws<ServiceContractOperationException>(() => Contract(value => value.CycleMonths = 13).Validated()) &&
                  Contract(value => value.CycleMonths = 12).Validated().CycleMonths == 12 && Contract(value => value.CycleMonths = 1).Validated().CycleMonths == 1,
                "The cycle is 1 to 12 months");
            Check(Throws<ServiceContractOperationException>(() => Contract(value => value.ValidUntil = new DateOnly(2025, 9, 22)).Validated()) &&
                  Contract(value => value.ValidUntil = new DateOnly(2025, 9, 23)).Validated().ValidUntil == new DateOnly(2025, 9, 23) &&
                  Contract(value => value.ValidUntil = new DateOnly(2027, 1, 1)).Validated().ValidUntil == new DateOnly(2027, 1, 1) && Contract().Validated().ValidUntil is null,
                "The expiry date cannot precede the contract date; without it the contract has no term");
            Check(Throws<ServiceContractOperationException>(() => Contract(value => value.Points.Add(new() { WorkPointId = 5, NextDue = new DateOnly(2025, 11, 1) })).Validated()) &&
                  Throws<ServiceContractOperationException>(() => Contract(value => value.Points[0].NextDue = null).Validated()) &&
                  Throws<ServiceContractOperationException>(() => Contract(value => value.Points[0].CycleMonths = 13).Validated()) &&
                  Throws<ServiceContractOperationException>(() => Contract(value => value.Notes = new string('x', 1001)).Validated()) &&
                  Contract(value => value.Points[0] = new() { WorkPointId = 5, MoveFromOtherContract = true }).Validated().Points[0].MoveFromOtherContract &&
                  Contract(value => value.Points.Clear()).Validated().Points.Count == 0,
                "A point appears once, needs its first due date (unless it is moved in) and a cycle of 1 to 12; notes are limited; a contract may have no point");

            var today = new DateOnly(2026, 9, 30);
            Check(ServiceDueRules.State(today.AddDays(-1), today) == ServiceDueState.Overdue && ServiceDueRules.State(today, today) == ServiceDueState.DueSoon &&
                  ServiceDueRules.State(today.AddDays(30), today) == ServiceDueState.DueSoon && ServiceDueRules.State(today.AddDays(31), today) == ServiceDueState.OnTime &&
                  ServiceDueRules.State(today.AddDays(10), today, 7) == ServiceDueState.OnTime && ServiceDueRules.State(today.AddDays(7), today, 7) == ServiceDueState.DueSoon,
                "The displayed state is overdue before today, soon within the threshold (30 days by default) and on time otherwise");
            var contractRecord = new ServiceContract(1, 1, "26", new DateOnly(2025, 9, 23), 3, new DateOnly(2026, 9, 29), true, "", 0);
            Check(ServiceDueRules.EffectiveCycle(null, 3) == 3 && ServiceDueRules.EffectiveCycle(6, 3) == 6 && ServiceDueRules.IsExpired(contractRecord, today) &&
                  !ServiceDueRules.IsExpired(contractRecord with { ValidUntil = today }, today) && !ServiceDueRules.IsExpired(contractRecord with { ValidUntil = null }, today),
                "A point inherits the cycle of the contract unless it has its own; a contract is expired the day after its expiry date");
            var twoPoints = new ServiceContractDetails(contractRecord, [
                new(new(1, 1, 5, null, new DateOnly(2026, 1, 20), 0), "Sediu", "Str. A 1", true), new(new(2, 1, 6, 6, new DateOnly(2026, 1, 10), 0), "Depozit", "Str. B 2", false)]);
            Check(twoPoints.NextDue == new DateOnly(2026, 1, 10) && new ServiceContractDetails(contractRecord, []).NextDue is null &&
                  twoPoints.EffectiveCycle(twoPoints.Points[0].Point) == 3 && twoPoints.EffectiveCycle(twoPoints.Points[1].Point) == 6,
                "The next due date of a contract is the earliest among its points");

            Check(ServiceContractRules.EditAction(contractRecord, contractRecord with { ValidUntil = new DateOnly(2027, 9, 29) }) == AuditActions.EditServiceContractExpiry &&
                  ServiceContractRules.EditAction(contractRecord, contractRecord with { ValidUntil = null }) == AuditActions.EditServiceContractExpiry &&
                  ServiceContractRules.EditAction(contractRecord, contractRecord with { CycleMonths = 6 }) == AuditActions.EditMaintenanceCycle &&
                  ServiceContractRules.EditAction(contractRecord, contractRecord with { CycleMonths = 6, ValidUntil = null }) == AuditActions.EditServiceContract &&
                  ServiceContractRules.EditAction(contractRecord, contractRecord with { Notes = "x" }) == AuditActions.EditServiceContract &&
                  ServiceContractRules.EditAction(contractRecord, contractRecord with { Number = "27" }) == AuditActions.EditServiceContract &&
                  ServiceContractRules.EditAction(contractRecord, contractRecord with { Number = "27", ValidUntil = null }) == AuditActions.EditServiceContract,
                "The journal names the exact edit: only the expiry date, only the cycle, or the contract in general");
            var contractActions = new[] { AuditActions.CreateServiceContract, AuditActions.EditServiceContract, AuditActions.EditServiceContractExpiry, AuditActions.ActivateServiceContract,
                AuditActions.DeactivateServiceContract, AuditActions.AddContractPoint, AuditActions.RemoveContractPoint, AuditActions.EditMaintenanceCycle, AuditActions.RescheduleMaintenance,
                AuditActions.MoveContractPoint };
            Check(contractActions.Distinct().Count() == 10 && contractActions.All(AuditActions.IsCreateOrEdit) && contractActions.All(action => action != AuditActions.Create && action != AuditActions.Edit),
                "Each maintenance contract operation has its own journal action, linked to the beneficiary page");
            Check(ServiceContractRules.Changes(contractRecord, contractRecord with { ValidUntil = new DateOnly(2027, 9, 29) }).Any(change => change.Field == "Expiră" && change.Before == "29.09.2026" && change.After == "29.09.2027") &&
                  AuditDetails.Changes([.. ServiceContractRules.Changes(contractRecord, contractRecord with { ValidUntil = null })]) == "Expiră: 29.09.2026 → fără termen",
                "The journal details hold the old and the new value in dd.MM.yyyy");

            Check(ArchiveSchemaRegistry.All.Any(schema => schema.EntityType == AuditEntities.ServiceContract && schema.TableName == "archive_service_contracts" && schema.SupportsRelations),
                "The maintenance contracts are registered for archiving");
            var archivedContract = ArchiveRequests.ServiceContract(twoPoints, "Beneficiar SRL", "Motiv");
            Check(archivedContract.Snapshot.EntityType == AuditEntities.ServiceContract && archivedContract.Snapshot.Relations.Count == 2 &&
                  archivedContract.Snapshot.Relations.All(relation => relation.RelationType == ArchiveRequests.ServiceContractPointRelation) && archivedContract.Target.Contains("26/23.09.2025"),
                "The archive request of a contract carries its coverage as relations");

            var migration8 = MariaSchemaMigrations.All.Single(m => m.Version == 8);
            Check(new[] { "contract_number", "contract_date", "cycle_months", "valid_until", "is_active", "notes" }.All(column => migration8.ExpectedColumns.Contains(("service_contracts", column))) &&
                  new[] { "contract_id", "work_point_id", "active_work_point_id", "cycle_months", "next_due" }.All(column => migration8.ExpectedColumns.Contains(("service_contract_points", column))) &&
                  migration8.Statements.Any(sql => sql.Contains("uq_service_contracts_number") && sql.Contains("ck_service_contracts_cycle") && sql.Contains("ck_service_contracts_valid_until")) &&
                  migration8.Statements.Any(sql => sql.Contains("uq_service_contract_points_active") && sql.Contains("uq_service_contract_points_pair") && sql.Contains("ON DELETE RESTRICT")) &&
                  migration8.Statements.Any(sql => sql.Contains("CREATE TABLE IF NOT EXISTS `archive_service_contracts`")),
                "MariaDB migration 8 adds the contracts, their coverage with the one-active-contract key and the archive table");
        }

        // Register of interventions (pure rules): the three ways to choose the next due date, the rule of the latest intervention, the input
        // validation, the exact journal operations, the registry entry and migration 9.
        {
            static bool Throws<T>(Action action) where T : Exception { try { action(); return false; } catch (T) { return true; } }
            var performed = new DateOnly(2026, 1, 18);
            var planned = new DateOnly(2026, 1, 15);
            var options = ServiceInterventionRules.Options(performed, planned, 3);
            Check(options.Count == 3 && options[0].Basis == ServiceNextDueBasis.FromPerformed && options[0].Date == new DateOnly(2026, 4, 18) && options[0].Enabled &&
                  options[1].Basis == ServiceNextDueBasis.FromPlanned && options[1].Date == new DateOnly(2026, 4, 15) && options[1].Enabled &&
                  options[2].Basis == ServiceNextDueBasis.Chosen && options[2].Date is null && options[2].Enabled,
                "The three variants: from the date performed, from the planned date, chosen by the operator");
            var late = ServiceInterventionRules.Options(new DateOnly(2026, 6, 1), new DateOnly(2026, 1, 15), 3);
            Check(late[0].Enabled && !late[1].Enabled && late[1].Reason is not null && late[2].Enabled &&
                  !ServiceInterventionRules.Options(new DateOnly(2026, 4, 15), planned, 3)[1].Enabled,
                "The planned-date variant is disabled (with its reason) when planned date plus cycle is not after the date performed");
            Check(ServiceInterventionRules.Options(new DateOnly(2026, 11, 30), planned, 3)[0].Date == new DateOnly(2027, 2, 28) &&
                  ServiceInterventionRules.Options(new DateOnly(2026, 8, 31), planned, 6)[0].Date == new DateOnly(2027, 2, 28),
                "A month-end date moves to the last day of a shorter month");
            Check(ServiceInterventionRules.ResolveDue(ServiceNextDueBasis.FromPerformed, performed, planned, 3, null, out var error) == new DateOnly(2026, 4, 18) && error is null &&
                  ServiceInterventionRules.ResolveDue(ServiceNextDueBasis.FromPlanned, performed, planned, 3, null, out error) == new DateOnly(2026, 4, 15) &&
                  ServiceInterventionRules.ResolveDue(ServiceNextDueBasis.Chosen, performed, planned, 3, new DateOnly(2026, 6, 2), out error) == new DateOnly(2026, 6, 2) &&
                  ServiceInterventionRules.ResolveDue(ServiceNextDueBasis.Chosen, performed, planned, 3, null, out error) is null && error is not null &&
                  ServiceInterventionRules.ResolveDue(ServiceNextDueBasis.Chosen, performed, planned, 3, performed, out error) is null && error is not null &&
                  ServiceInterventionRules.ResolveDue(ServiceNextDueBasis.FromPlanned, new DateOnly(2026, 6, 1), planned, 3, null, out error) is null && error is not null,
                "The chosen date must exist and be after the date performed; a planned-date result not after it is refused");
            Check(ServiceInterventionRules.PerformedOnError(new DateOnly(2026, 9, 30), new DateOnly(2026, 9, 30)) is null &&
                  ServiceInterventionRules.PerformedOnError(new DateOnly(2026, 10, 1), new DateOnly(2026, 9, 30)) is not null,
                "The date performed cannot be in the future");

            ServiceIntervention Row(int id, ServiceInterventionKind kind, int point, DateOnly on, ServiceNextDueBasis? basis) => new(id, kind, 1, point, kind == ServiceInterventionKind.Maintenance ? 7 : null,
                "Sediu", "Str. A 1", kind == ServiceInterventionKind.Maintenance ? "26/23.09.2025" : null, on, basis is null ? null : new DateOnly(2025, 12, 1), basis, basis is null ? null : on.AddMonths(3), "", "ana", DateTime.UtcNow, 0);
            var register = new[]
            {
                Row(1, ServiceInterventionKind.Maintenance, 5, new DateOnly(2026, 1, 10), ServiceNextDueBasis.FromPerformed),
                Row(2, ServiceInterventionKind.Maintenance, 5, new DateOnly(2026, 4, 12), ServiceNextDueBasis.FromPlanned),
                Row(3, ServiceInterventionKind.Maintenance, 5, new DateOnly(2026, 2, 1), null),
                Row(4, ServiceInterventionKind.OnDemand, 5, new DateOnly(2026, 8, 1), null),
                Row(5, ServiceInterventionKind.Maintenance, 6, new DateOnly(2026, 3, 1), ServiceNextDueBasis.Chosen)
            };
            Check(ServiceInterventionRules.LatestMoving(register, 5)?.Id == 2 && ServiceInterventionRules.LatestMoving(register, 6)?.Id == 5 && ServiceInterventionRules.LatestMoving(register, 9) is null &&
                  !register[2].MovesDue && !register[3].MovesDue && register[1].MovesDue,
                "The latest due-moving maintenance intervention of a point is the one with the latest date performed (on-demand and non-moving ones do not count)");
            Check(ServiceInterventionRules.Moves(new DateOnly(2026, 4, 12), new DateOnly(2026, 4, 12)) && ServiceInterventionRules.Moves(new DateOnly(2026, 5, 1), new DateOnly(2026, 4, 12)) &&
                  !ServiceInterventionRules.Moves(new DateOnly(2026, 4, 11), new DateOnly(2026, 4, 12)) && ServiceInterventionRules.Moves(new DateOnly(2020, 1, 1), null),
                "A new maintenance intervention moves the due date unless a later one already exists");

            Check(ServiceInterventionRules.KindCode(ServiceInterventionKind.Maintenance) == 'M' && ServiceInterventionRules.KindCode(ServiceInterventionKind.OnDemand) == 'C' &&
                  ServiceInterventionRules.ParseKind("M") == ServiceInterventionKind.Maintenance && ServiceInterventionRules.ParseKind("C") == ServiceInterventionKind.OnDemand &&
                  ServiceInterventionRules.ParseBasis("E") == ServiceNextDueBasis.FromPerformed && ServiceInterventionRules.ParseBasis("P") == ServiceNextDueBasis.FromPlanned &&
                  ServiceInterventionRules.ParseBasis("O") == ServiceNextDueBasis.Chosen && ServiceInterventionRules.ParseBasis(null) is null &&
                  new[] { ServiceNextDueBasis.FromPerformed, ServiceNextDueBasis.FromPlanned, ServiceNextDueBasis.Chosen }.Select(ServiceInterventionRules.BasisCode).Distinct().Count() == 3,
                "The kind and the choice are stored as one-letter codes and read back");

            static ServiceInterventionInput Input(Action<ServiceInterventionInput>? change = null)
            {
                var value = new ServiceInterventionInput { WorkPointId = 5, PerformedOn = new DateOnly(2026, 1, 18), Notes = "  Filtre schimbate  " };
                change?.Invoke(value);
                return value;
            }
            Check(Input().Validated().Notes == "Filtre schimbate" && Input().Validated().Kind == ServiceInterventionKind.Maintenance &&
                  Throws<ServiceInterventionOperationException>(() => Input(value => value.WorkPointId = 0).Validated()) &&
                  Throws<ServiceInterventionOperationException>(() => Input(value => value.PerformedOn = null).Validated()) &&
                  Throws<ServiceInterventionOperationException>(() => Input(value => value.PerformedOn = new DateOnly(1999, 1, 1)).Validated()) &&
                  Throws<ServiceInterventionOperationException>(() => Input(value => value.Notes = new string('x', 2001)).Validated()) &&
                  Input(value => value.Notes = new string('x', 2000)).Validated().Notes.Length == 2000,
                "A valid intervention input is normalized; the point and the date are required and the notes are limited to 2000 characters");
            var fromRow = ServiceInterventionInput.From(register[4]);
            Check(fromRow.Basis == ServiceNextDueBasis.Chosen && fromRow.ChosenDue == register[4].NextDueSet && ServiceInterventionInput.From(register[3]).Basis == ServiceNextDueBasis.FromPerformed &&
                  ServiceInterventionInput.From(register[3]).ChosenDue is null,
                "The correction form starts from the recorded choice");

            var interventionActions = new[] { AuditActions.RecordMaintenance, AuditActions.EditMaintenanceIntervention, AuditActions.RecordOnDemand, AuditActions.EditOnDemandIntervention, AuditActions.AddInterventionPhoto };
            Check(interventionActions.Distinct().Count() == 5 && interventionActions.All(AuditActions.IsCreateOrEdit) && interventionActions.All(action => action != AuditActions.Create && action != AuditActions.Edit),
                "Each intervention operation has its own journal action, linked to the beneficiary page");
            Check(ServiceInterventionRules.Changes(register[0], register[0] with { PerformedOn = new DateOnly(2026, 1, 12) }).Any(change => change.Field == "Efectuată la" && change.Before == "10.01.2026" && change.After == "12.01.2026") &&
                  ServiceInterventionRules.Target(register[3], "Beneficiar SRL").Contains("la cerere") && ServiceInterventionRules.Target(register[0], "Beneficiar SRL").Contains("de mentenanță") &&
                  ServiceInterventionRules.Identification(register[0]).Contains("10.01.2026"),
                "The journal details hold the old and the new value in dd.MM.yyyy and the kind of the intervention");

            Check(ArchiveSchemaRegistry.All.Any(schema => schema.EntityType == AuditEntities.ServiceIntervention && schema.TableName == "archive_service_interventions" && schema.SupportsRelations && schema.SupportsFiles),
                "The interventions are registered for archiving (with their photos and files)");
            var archivedIntervention = ArchiveRequests.ServiceIntervention(register[0], "Beneficiar SRL",
                [new ServicePhoto(9, null, 1, "a.png", "x.png", "image/png", 10, new string('a', 64), "", "ana", DateTime.UtcNow)], "Motiv");
            Check(archivedIntervention.Snapshot.EntityType == AuditEntities.ServiceIntervention && archivedIntervention.Snapshot.Relations.Count == 1 &&
                  archivedIntervention.Snapshot.Relations[0].RelationType == AuditEntities.ServicePhoto && archivedIntervention.Target.Contains("de mentenanță"),
                "The archive request of an intervention carries its photos as relations");

            var migration9 = MariaSchemaMigrations.All.Single(m => m.Version == 9);
            Check(new[] { "kind", "beneficiary_id", "work_point_id", "contract_id", "work_point_name", "contract_label", "performed_on", "planned_due", "next_due_basis", "next_due_set", "notes", "version" }
                      .All(column => migration9.ExpectedColumns.Contains(("service_interventions", column))) &&
                  migration9.ExpectedColumns.Contains(("archive_service_interventions", "next_due_set")) &&
                  migration9.Statements.Any(sql => sql.Contains("ck_service_interventions_kind") && sql.Contains("ck_service_interventions_due") && sql.Contains("ck_service_interventions_next_due") && sql.Contains("ON DELETE RESTRICT")) &&
                  migration9.Statements.Any(sql => sql.Contains("fk_service_photos_intervention")) &&
                  migration9.Statements.Any(sql => sql.Contains("CREATE TABLE IF NOT EXISTS `archive_service_interventions`")),
                "MariaDB migration 9 adds the register with its constraints, the photo link and the archive table");
        }
    }
}
