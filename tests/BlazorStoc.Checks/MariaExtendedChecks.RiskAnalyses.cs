using Microsoft.Extensions.Configuration;
using MySqlConnector;
using BlazorStoc.Services;

namespace BlazorStoc.Checks;

public static partial class MariaExtendedChecks
{
    // ---- Risk analyses -------------------------------------------------------------------------------------------------------------
    private static async Task RiskAnalysesAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe)
    {
        var suffix = Suffix();
        var clock = new ManualTimeProvider();
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var beneficiaries = new MariaBeneficiaryRepository(configuration, admin, audit);
        var workPoints = new MariaWorkPointRepository(configuration, admin, audit);
        var analyses = new MariaRiskAnalysisRepository(configuration, admin, audit, null, clock);
        var repository = new MariaExpiryNotificationRepository(configuration);
        // Own key: the real template of the risk analysis source (if an administrator made it) is not touched.
        var source = new RiskAnalysisExpirySource(new MariaRiskAnalysisNotificationReader(configuration), "analiza-risc.expirare.e" + suffix);
        var boss = new ExpiryNotificationService(repository, [source], admin, audit, clock);
        async Task<long> Count(string sql, params (string, object)[] parameters) => await ScalarLongAsync(probe, sql, parameters);
        RiskAnalysisInput Input(WorkPoint point, string number, DateOnly initial, int months = 36) =>
            new() { WorkPointId = point.Id, Number = number, Author = "Ion Popescu", InitialDate = initial, ValidityMonths = months };
        var since = DateTime.UtcNow;
        await Task.Delay(30);
        var owner = await beneficiaries.CreateAsync(Legal($"Ext Analize risc {suffix} SRL", "RO" + Random.Shared.Next(40000000, 49999999)));
        var stranger = await beneficiaries.CreateAsync(Legal($"Ext Analize risc strain {suffix} SRL", "RO" + Random.Shared.Next(50000000, 59999999)));
        async Task<int> Events(string action) => (await audit.GetEventsAsync()).Count(item => item.TimestampUtc > since && item.EntityType == AuditEntities.Beneficiary &&
            item.EntityId == owner.Id.ToString() && item.Action == action);
        async Task<RiskAnalysisView> Fresh(int id) => (await analyses.GetForBeneficiaryAsync(owner.Id)).Single(item => item.Analysis.Id == id);
        async Task<IReadOnlyList<ExpiryNotification>> Mine(int analysisId) => (await repository.GetNotificationsAsync())
            .Where(item => item.SourceKey == source.Key && item.ObjectId == analysisId).ToList();
        NotificationTemplate? template = null;
        try
        {
            var main = (await workPoints.GetAsync(owner.Id)).Single();
            var depozit = await workPoints.CreateAsync(owner.Id, new WorkPointInput { Name = "Depozit", Address = "Str. Depozitului 1, Cluj" });
            var foreignPoint = (await workPoints.GetAsync(stranger.Id)).Single();
            var initial = today.AddMonths(-35);

            // Rules: the expiry is the last renewal plus the validity (cut to the end of a shorter month).
            Check(RiskAnalysisRules.ExpiryDate(new DateOnly(2023, 1, 31), 1) == new DateOnly(2023, 2, 28) && RiskAnalysisRules.ExpiryDate(new DateOnly(2023, 3, 12), 36) == new DateOnly(2026, 3, 12) &&
                  RiskAnalysisRules.DefaultValidityMonths == 36, "The expiry is the last renewal plus the validity in months (default 36), cut to the end of a shorter month");

            // A new analysis is On, its last renewal is the registration date, the number is normalized, the active key equals the point.
            var a1 = await analyses.CreateAsync(owner.Id, Input(main, " ar-14 ", initial), false);
            Check(a1.Analysis.IsActive && a1.Analysis.Number == "AR-14" && a1.Analysis.LastRenewalDate == initial && a1.Analysis.InitialDate == initial && a1.Analysis.Version == 0 &&
                  a1.Analysis.ExpiryDate == initial.AddMonths(36) && a1.Renewals.Count == 0 && a1.WorkPointName == main.Name && a1.BeneficiaryName == owner.Name &&
                  await Count("SELECT COUNT(*) FROM risk_analyses WHERE id=@id AND active_work_point_id=work_point_id", ("@id", a1.Analysis.Id)) == 1,
                "A new risk analysis is On, its last renewal is the registration date and the expiry follows from it");
            Check(await Events(AuditActions.CreateRiskAnalysis) == 1, "The journal names the operation \"Adăugare analiză de risc\"");

            // Refusals: one active per point, unique number per beneficiary, other beneficiary's point, invalid values.
            var second = await Rejects<RiskAnalysisOperationException>(() => analyses.CreateAsync(owner.Id, Input(main, "AR-15", initial), false), "A work point cannot have two active analyses");
            Check(second!.Message.Contains("AR-14"), "The refusal names the active analysis of the point");
            await Rejects<RiskAnalysisOperationException>(() => analyses.CreateAsync(owner.Id, Input(depozit, "ar-14", initial), false), "The registration number is unique for a beneficiary (a revision is a renewal)");
            await Rejects<RiskAnalysisOperationException>(() => analyses.CreateAsync(owner.Id, Input(foreignPoint, "AR-20", initial), false), "An analysis cannot be made for a work point of another beneficiary");
            await Rejects<RiskAnalysisOperationException>(() => analyses.CreateAsync(owner.Id, Input(depozit, "AR-21", today.AddDays(1)), false), "The registration date cannot be in the future");
            await Rejects<RiskAnalysisOperationException>(() => analyses.CreateAsync(owner.Id, Input(depozit, "AR-22", initial, 0), false), "The validity must be at least one month");
            await Rejects<RiskAnalysisOperationException>(() => analyses.CreateAsync(owner.Id, Input(depozit, "AR-23", initial, 121), false), "The validity cannot exceed 120 months");
            var noAuthor = Input(depozit, "AR-24", initial); noAuthor.Author = "  ";
            await Rejects<RiskAnalysisOperationException>(() => analyses.CreateAsync(owner.Id, noAuthor, false), "The author is required");
            Check((await analyses.GetForBeneficiaryAsync(owner.Id)).Count == 1, "Refused analyses leave nothing behind");
            await Rejects<MySqlException>(() => ExecuteAsync(probe, "INSERT INTO risk_analyses (beneficiary_id, work_point_id, registration_number, author, initial_date, last_renewal_date, validity_months, active_work_point_id) VALUES (@b, @w, 'DB-1', 'x', '2025-01-01', '2025-01-01', 36, @w)",
                ("@b", owner.Id), ("@w", main.Id)), "The database refuses a second active analysis for a work point (unique active key)");

            // The notification: one per active analysis, dated at the expiry, with the placeholders of the source.
            Check(source.Category == "Analize de risc" && source.DefaultThresholdDays == 60 && ExpiryTemplateRules.UnknownPlaceholders(source.DefaultSubject + source.DefaultBody, source).Count == 0,
                "The source is in the category \"Analize de risc\", proposes 60 days and its texts use only its own placeholders");
            template = await boss.CreateTemplateAsync(new NotificationTemplateInput { SourceKey = source.Key, Subject = source.DefaultSubject, Body = source.DefaultBody, ThresholdDays = 60 });
            await boss.EvaluateAsync();
            var view = (await boss.GetViewsAsync()).SingleOrDefault(item => item.Template.Id == template.Id && item.Notification.ObjectId == a1.Analysis.Id);
            Check(view is not null && view.Notification.ExpiryDate == a1.Analysis.ExpiryDate && view.Url == $"/beneficiari/{owner.Id}" && view.ObjectLabel == $"{owner.Name} · {main.Name} · Analiză de risc AR-14" &&
                  view.Body.Contains("nr. AR-14") && view.Body.Contains("Ion Popescu") && view.Body.Contains(StockMovementRules.DisplayDate(a1.Analysis.ExpiryDate)) && view.Body.Contains(StockMovementRules.DisplayDate(initial)),
                "An active analysis inside the period gets a notification with its number, author, dates and the link to the beneficiary");

            // A renewal: later than the last one, not in the future; it moves the expiry, writes the history and closes the old notification.
            await Rejects<RiskAnalysisOperationException>(() => analyses.RenewAsync(a1.Analysis, new RiskAnalysisRenewalInput { Date = initial }), "A renewal must be after the last renewal");
            await Rejects<RiskAnalysisOperationException>(() => analyses.RenewAsync(a1.Analysis, new RiskAnalysisRenewalInput { Date = today.AddDays(1) }), "A renewal cannot be in the future");
            await Rejects<RiskAnalysisOperationException>(() => analyses.RenewAsync(a1.Analysis, new RiskAnalysisRenewalInput()), "A renewal needs a date");
            var r1Date = today.AddDays(-20);
            var renewed = await analyses.RenewAsync(a1.Analysis, new RiskAnalysisRenewalInput { Date = r1Date, Notes = "Revizie" });
            Check(renewed.Analysis.LastRenewalDate == r1Date && renewed.Analysis.InitialDate == initial && renewed.Analysis.Number == "AR-14" && renewed.Analysis.Version == 1 &&
                  renewed.Analysis.ExpiryDate == r1Date.AddMonths(36) && renewed.Renewals.Count == 1 && renewed.Renewals[0].RenewalDate == r1Date && renewed.Renewals[0].PreviousRenewalDate == initial &&
                  renewed.Renewals[0].Notes == "Revizie" && renewed.Renewals[0].RecordedBy.Length > 0,
                "A renewal keeps the registration date and number, moves the last renewal and the expiry, and is written in the history");
            Check(await Events(AuditActions.RenewRiskAnalysis) == 1, "The journal names the operation \"Reînnoire analiză de risc\"");
            await Rejects<RiskAnalysisOperationException>(() => analyses.RenewAsync(a1.Analysis, new RiskAnalysisRenewalInput { Date = today }), "A renewal from an outdated version is refused");
            await boss.EvaluateAsync();
            var oldNotification = (await Mine(a1.Analysis.Id)).Single(item => item.ExpiryDate == a1.Analysis.ExpiryDate);
            Check(oldNotification.IsResolved && oldNotification.ResolvedAutomatically && oldNotification.ResolvedBy == "sistem" &&
                  oldNotification.ResolvedReason == $"Data expirării analizei de risc s-a modificat de la {StockMovementRules.DisplayDate(a1.Analysis.ExpiryDate)} la {StockMovementRules.DisplayDate(renewed.Analysis.ExpiryDate)} (ultima reînnoire: {StockMovementRules.DisplayDate(r1Date)}).",
                "A renewal closes the notification with the old and new expiry dates and the date of the renewal");

            // Taking back the newest renewal restores the previous last renewal and reopens the notification the system had closed.
            var undone = await analyses.UndoLastRenewalAsync(renewed.Analysis);
            Check(undone.Analysis.LastRenewalDate == initial && undone.Renewals.Count == 0 && undone.Analysis.ExpiryDate == a1.Analysis.ExpiryDate && await Events(AuditActions.UndoRiskAnalysisRenewal) == 1,
                "Undoing the newest renewal restores the previous last renewal and the journal names \"Anulare reînnoire analiză de risc\"");
            await Rejects<RiskAnalysisOperationException>(() => analyses.UndoLastRenewalAsync(undone.Analysis), "An analysis without renewals has nothing to undo");
            await boss.EvaluateAsync();
            Check(!(await Mine(a1.Analysis.Id)).Single(item => item.Id == oldNotification.Id).IsResolved, "When the expiry comes back, the notification the system had closed is reopened");

            // Two renewals: the history is newest first and only the newest can be undone.
            var step1 = await analyses.RenewAsync(undone.Analysis, new RiskAnalysisRenewalInput { Date = r1Date });
            var step2 = await analyses.RenewAsync(step1.Analysis, new RiskAnalysisRenewalInput { Date = today });
            Check(step2.Renewals.Select(item => item.RenewalDate).SequenceEqual([today, r1Date]) && step2.Renewals[0].PreviousRenewalDate == r1Date && step2.Analysis.ExpiryDate == today.AddMonths(36),
                "The history keeps every renewal, newest first, each with the date it replaced");
            Check((await analyses.UndoLastRenewalAsync(step2.Analysis)).Analysis.LastRenewalDate == r1Date, "Undoing takes back only the newest renewal");

            // Editing: validity alone has its own journal name; the registration date is locked after a renewal; the number stays unique.
            var current = await Fresh(a1.Analysis.Id);
            var validityEdit = RiskAnalysisInput.From(current.Analysis); validityEdit.ValidityMonths = 48;
            var edited = await analyses.UpdateAsync(current.Analysis, validityEdit);
            Check(edited.Analysis.ValidityMonths == 48 && edited.Analysis.ExpiryDate == r1Date.AddMonths(48) && edited.Analysis.Version == current.Analysis.Version + 1 && await Events(AuditActions.EditRiskAnalysisValidity) == 1,
                "Changing only the validity moves the expiry and is journaled as \"Modificare valabilitate analiză de risc\"");
            var dateEdit = RiskAnalysisInput.From(edited.Analysis); dateEdit.InitialDate = initial.AddDays(1);
            await Rejects<RiskAnalysisOperationException>(() => analyses.UpdateAsync(edited.Analysis, dateEdit), "The registration date cannot change after a renewal");
            var textEdit = RiskAnalysisInput.From(edited.Analysis); textEdit.Author = "Maria Ionescu"; textEdit.Notes = "Nota";
            var edited2 = await analyses.UpdateAsync(edited.Analysis, textEdit);
            Check(edited2.Analysis.Author == "Maria Ionescu" && edited2.Analysis.Notes == "Nota" && await Events(AuditActions.EditRiskAnalysis) == 1, "Changing the author or notes is journaled as \"Modificare analiză de risc\"");
            await Rejects<RiskAnalysisOperationException>(() => analyses.UpdateAsync(current.Analysis, textEdit), "An edit from an outdated version is refused");

            // Replacing: a new analysis for the same point turns the old one Off in the same transaction; an Off analysis cannot be reactivated beside an active one.
            var a2 = await analyses.CreateAsync(owner.Id, Input(main, "AR-15", today), true);
            var a1Off = await Fresh(a1.Analysis.Id);
            Check(a2.Analysis.IsActive && !a1Off.Analysis.IsActive && await Count("SELECT COUNT(*) FROM risk_analyses WHERE work_point_id=@w AND active_work_point_id IS NOT NULL", ("@w", main.Id)) == 1 &&
                  await Events(AuditActions.DeactivateRiskAnalysis) == 1, "A new analysis can replace the active one of the point (old Off, new On, journaled)");
            await Rejects<RiskAnalysisOperationException>(() => analyses.ActivateAsync(a1Off.Analysis), "An Off analysis cannot be activated while another one of the point is active");
            await Rejects<RiskAnalysisOperationException>(() => analyses.RenewAsync(a1Off.Analysis, new RiskAnalysisRenewalInput { Date = today }), "An Off analysis cannot be renewed");
            await boss.EvaluateAsync();
            var closedByOff = (await Mine(a1.Analysis.Id)).OrderByDescending(item => item.Id).First();
            Check(closedByOff.IsResolved && closedByOff.ResolvedAutomatically, "Switching an analysis Off closes its notification");
            await analyses.DeactivateAsync(a2.Analysis);
            var a1On = await analyses.ActivateAsync((await Fresh(a1.Analysis.Id)).Analysis);
            Check(a1On.Analysis.IsActive && !(await Fresh(a2.Analysis.Id)).Analysis.IsActive && await Events(AuditActions.ActivateRiskAnalysis) == 1, "After the other one is Off, the earlier analysis can be activated again (journaled)");

            // Guards: a work point or a beneficiary with an analysis cannot be deleted.
            var a3 = await analyses.CreateAsync(owner.Id, Input(depozit, "AR-30", today.AddMonths(-2)), false);
            await analyses.RenewAsync(a3.Analysis, new RiskAnalysisRenewalInput { Date = today.AddDays(-1) });
            var pointGuard = await Rejects<WorkPointOperationException>(() => workPoints.DeleteAsync(depozit), "A work point with a risk analysis cannot be deleted");
            Check(pointGuard!.Message.Contains("AR-30"), "The refusal names the analysis of the work point");
            await Rejects<BeneficiaryOperationException>(async () => await beneficiaries.DeleteAsync((await beneficiaries.GetBeneficiariesAsync()).Single(item => item.Id == owner.Id), "Ext curatare"), "A beneficiary with risk analyses cannot be deleted");

            // Deleting archives the analysis with its renewals (as relations) and needs a reason.
            var a3Fresh = await Fresh(a3.Analysis.Id);
            var renewalId = a3Fresh.Renewals.Single().Id;
            await Rejects<RiskAnalysisOperationException>(() => analyses.DeleteAsync(a3Fresh.Analysis, ""), "Deleting an analysis needs a reason");
            await Rejects<RiskAnalysisOperationException>(() => analyses.DeleteAsync(a3.Analysis, "Ext curatare"), "An analysis cannot be deleted from an outdated version");
            await analyses.DeleteAsync(a3Fresh.Analysis, "Ext curatare");
            Check(await Count("SELECT COUNT(*) FROM risk_analyses WHERE id=@id", ("@id", a3.Analysis.Id)) == 0 && await Count("SELECT COUNT(*) FROM risk_analysis_renewals WHERE risk_analysis_id=@id", ("@id", a3.Analysis.Id)) == 0 &&
                  await Count("SELECT COUNT(*) FROM archive_risk_analyses WHERE original_id=@id AND registration_number='AR-30'", ("@id", a3.Analysis.Id)) == 1 &&
                  await Count("SELECT COUNT(*) FROM archive_relations WHERE relation_type=@type AND original_relation_id=@id", ("@type", ArchiveRequests.RiskAnalysisRenewalRelation), ("@id", renewalId.ToString())) == 1,
                "Deleting an analysis archives it with its renewals and removes it from the live tables");
            Check((await audit.GetEventsAsync()).Any(item => item.Action == AuditActions.Delete && item.EntityType == AuditEntities.RiskAnalysis && item.EntityId == a3.Analysis.Id.ToString() &&
                  item.Motif == "Ext curatare" && item.Target.Contains("AR-30")), "The deletion of an analysis is journaled with its reason");
            await workPoints.DeleteAsync(depozit);
            Check(await Count("SELECT COUNT(*) FROM beneficiary_work_points WHERE id=@id", ("@id", depozit.Id)) == 0, "Once its analysis is deleted the work point can be deleted");

            // The global list: earliest expiry first, Off ones only when asked.
            var all = (await analyses.GetAllAsync(true)).Where(item => item.Analysis.BeneficiaryId == owner.Id).ToList();
            var activeOnly = (await analyses.GetAllAsync(false)).Where(item => item.Analysis.BeneficiaryId == owner.Id).ToList();
            Check(all.Count == 2 && activeOnly.Count == 1 && activeOnly[0].Analysis.Id == a1.Analysis.Id, "The list of all analyses hides the Off ones unless they are asked for");
        }
        finally
        {
            if (template is not null)
                try { await boss.DeleteTemplateAsync((await repository.GetTemplatesAsync()).Single(item => item.Id == template.Id), "Ext curatare"); } catch (InvalidOperationException) { }
            foreach (var item in new[] { owner, stranger })
            {
                foreach (var view in await analyses.GetForBeneficiaryAsync(item.Id)) await analyses.DeleteAsync(view.Analysis, "Ext curatare");
                if ((await beneficiaries.GetBeneficiariesAsync()).FirstOrDefault(current => current.Id == item.Id) is { } live) await beneficiaries.DeleteAsync(live, "Ext curatare");
            }
        }
    }
}
