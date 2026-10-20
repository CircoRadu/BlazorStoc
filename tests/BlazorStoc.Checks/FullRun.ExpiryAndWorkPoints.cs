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
    // Lines 1981-2127 of the former Program.cs.
    internal static async Task ExpiryAndWorkPointsAsync(string[] args)
    {

        // ---- Expiry notifications: rules that need no database ----
        {
            var pureSource = new TestExpirySource("test.pure", []);
            var okTemplate = ExpiryTemplateRules.Validated(new NotificationTemplateInput { SourceKey = "test.pure", Subject = "  Expirare <eveniment> <obiect> ", Body = "La <data expirare> (<zile ramase> zile) pentru <obiect>.", ThresholdDays = 30 }, pureSource);
            Check(okTemplate.Subject == "Expirare <eveniment> <obiect>" && okTemplate.SourceKey == "test.pure", "A valid notification template is accepted and trimmed");
            void TemplateRejected(NotificationTemplateInput candidate, IExpirySource? source, string contains, string message)
            {
                try { ExpiryTemplateRules.Validated(candidate, source); throw new Exception(message + " (accepted)"); }
                catch (NotificationOperationException exception) { Check(exception.Message.Contains(contains, StringComparison.Ordinal), message); }
            }
            TemplateRejected(new() { SourceKey = "test.pure", Subject = "S <nu exista>", Body = "T", ThresholdDays = 5 }, pureSource, "<nu exista>", "An unknown placeholder in the subject is rejected and named");
            TemplateRejected(new() { SourceKey = "test.pure", Subject = "S", Body = "T <alt marcaj>", ThresholdDays = 5 }, pureSource, "<alt marcaj>", "An unknown placeholder in the text is rejected and named");
            TemplateRejected(new() { SourceKey = "test.pure", Subject = "S", Body = "T", ThresholdDays = 0 }, pureSource, "între 1 și", "A threshold of zero days is rejected");
            TemplateRejected(new() { SourceKey = "test.pure", Subject = "S", Body = "T", ThresholdDays = ExpiryTemplateRules.MaxThreshold + 1 }, pureSource, "între 1 și", "A threshold above the limit is rejected");
            TemplateRejected(new() { SourceKey = "test.pure", Subject = " ", Body = "T", ThresholdDays = 5 }, pureSource, "subiectul", "An empty subject is rejected");
            TemplateRejected(new() { SourceKey = "test.pure", Subject = "S", Body = new string('x', ExpiryTemplateRules.MaxBody + 1), ThresholdDays = 5 }, pureSource, "cel mult", "An oversize text is rejected");
            TemplateRejected(new() { SourceKey = "x", Subject = "S", Body = "T", ThresholdDays = 5 }, null, "categoria", "A template without a known source is rejected");
            var vehicleSource = new VehicleExpirySource(null!, VehicleExpiryKind.Rovinieta, ExpirySourceKeys.VehicleRovinieta, "Rovinietă");
            ExpiryTemplateRules.Validated(new NotificationTemplateInput { SourceKey = vehicleSource.Key, Subject = ExpiryTemplateRules.DefaultSubject, Body = ExpiryTemplateRules.DefaultBody, ThresholdDays = 30 }, vehicleSource);
            Check(true, "The default example subject and text only use placeholders of the vehicle sources");

            var renderToday = new DateOnly(2026, 9, 25);
            var instance = new ExpiryInstance(7, "TS-01-ABC · Dacia", new DateOnly(2026, 10, 15), new Dictionary<string, string> { ["obiect"] = "TS-01-ABC" });
            Check(ExpiryTemplateRules.Render("<eveniment> <obiect> <data expirare> <zile ramase> <necunoscut>", "ITP", instance, renderToday) == "ITP TS-01-ABC 15.10.2026 20 <necunoscut>",
                "Placeholders are replaced with the object values, the date as dd.mm.yyyy and the days left");
            Check(ExpiryTemplateRules.RenderSample("<obiect> <zile ramase>", pureSource) == "Obiect test 30", "The preview uses the sample value of each placeholder");
            Check(ExpiryTemplateRules.MaxSnoozeDays(new DateOnly(2026, 10, 15), renderToday) == 18, "The reminder limit is the days left minus two (20 - 2 = 18)");
            Check(ExpiryTemplateRules.MaxSnoozeDays(new DateOnly(2026, 9, 27), renderToday) == 0 && ExpiryTemplateRules.MaxSnoozeDays(new DateOnly(2026, 9, 26), renderToday) == 0,
                "With two days or less left there is no room for a reminder");

            var open = new ExpiryNotification(1, 1, "test.pure", 7, new DateOnly(2026, 10, 15), DateTime.UtcNow, null, null, null, null, 0);
            Check(open.IsAlert(renderToday), "A notification nobody took over warns");
            var taken = open with { AcknowledgedBy = "ana", AcknowledgedUtc = DateTime.UtcNow };
            Check(!taken.IsAlert(renderToday), "A notification that was taken over stops warning");
            var snoozed = taken with { SnoozeUntil = renderToday.AddDays(3), SnoozeDays = 3 };
            Check(!snoozed.IsAlert(renderToday) && !snoozed.IsAlert(renderToday.AddDays(2)) && snoozed.IsAlert(renderToday.AddDays(3)) && snoozed.IsAlert(renderToday.AddDays(4)),
                "A reminder warns again exactly when its period ends");
            Check(ExpiryTemplateRules.MaxSnoozeDays(new DateOnly(2026, 9, 24), renderToday) == 30 && ExpiryTemplateRules.MaxSnoozeDays(new DateOnly(2026, 9, 1), renderToday) == 30 &&
                  ExpiryTemplateRules.MaxSnoozeDays(new DateOnly(2026, 9, 25), renderToday) == 0,
                "An overdue notification may be postponed by up to 30 days (the days-left rule would be negative); on the due date itself there is no room");
            Check(ExpiryTemplateRules.SuggestedSnoozeDays(new DateOnly(2026, 9, 24), renderToday) == 7 && ExpiryTemplateRules.SuggestedSnoozeDays(new DateOnly(2026, 10, 15), renderToday) == 1,
                "The reminder field starts with 7 days for an overdue notification and with 1 otherwise");
            var overdueInstance = new ExpiryInstance(7, "TS-01-ABC", new DateOnly(2026, 9, 22), new Dictionary<string, string>());
            Check(ExpiryTemplateRules.Render("<zile ramase>|<zile depasire>", "ITP", overdueInstance, renderToday) == "0|3" &&
                  ExpiryTemplateRules.Render("<zile ramase>|<zile depasire>", "ITP", instance, renderToday) == "20|0",
                "The days left never print below zero; the days overdue print the overdue days (0 before the date)");
            Check(ExpiryTemplateRules.AllPlaceholders(pureSource).Any(item => item.Name == ExpiryTemplateRules.DaysOverdueName), "The days-overdue placeholder is offered to every source");
            var resolvedRow = open with { ResolvedBy = "ana", ResolvedUtc = DateTime.UtcNow, ResolvedReason = "test" };
            Check(resolvedRow.IsResolved && !resolvedRow.IsAlert(renderToday) && !open.IsResolved, "A resolved notification never warns");
            Check(AuditActions.IsCreateOrEdit(AuditActions.ResolveNotification) && AuditActions.IsCreateOrEdit(AuditActions.AutoResolveNotification) && AuditActions.IsCreateOrEdit(AuditActions.ReopenNotification) &&
                  AuditActions.ResolveNotification != AuditActions.AutoResolveNotification, "The resolution operations are named exactly and keep their link to the notifications page")
            ;
            Check(ExpiryTemplateRules.DuplicateActive("Sablon").Message.Contains("șablon activ") && ExpiryTemplateRules.DuplicateActive("Sablon").Message.Contains("Sablon"),
                "The refusal of a second active template names the existing one");

            Check(AuditActions.IsCreateOrEdit(AuditActions.CreateNotificationTemplate) && AuditActions.IsCreateOrEdit(AuditActions.EditNotificationTemplate) &&
                  AuditActions.IsCreateOrEdit(AuditActions.AcknowledgeNotification) && AuditActions.IsCreateOrEdit(AuditActions.SnoozeNotification) && AuditActions.IsCreateOrEdit(AuditActions.NotificationCreated) &&
                  !AuditActions.IsCreateOrEdit(AuditActions.DeleteNotificationTemplate), "The notification operations keep their link to the object, except the deletion");
            var snoozeEvent = new AuditEvent(Guid.NewGuid(), DateTime.UtcNow, "ana", AccessRoles.LimitedUser, AuditEntities.Notification, AuditActions.SnoozeNotification, "ITP · TS-01-ABC", "d", "", "5");
            var templateEvent = snoozeEvent with { EntityType = AuditEntities.NotificationTemplate, Action = AuditActions.CreateNotificationTemplate };
            Check(AuditNavigation.TargetUrl(snoozeEvent) == "/notificari" && AuditNavigation.TargetUrl(templateEvent) == "/setari?tab=notificari&subtab=templates&sablon=5", "Journal entries of notifications link to the notifications page and of templates to their Settings row");
            Check(AuditNavigation.TargetUrl(templateEvent with { EntityId = "7" }) == "/setari?tab=notificari&subtab=templates&sablon=7" &&
                  AuditNavigation.TargetUrl(templateEvent with { EntityId = "7", Action = AuditActions.EditNotificationTemplate }) == "/setari?tab=notificari&subtab=templates&sablon=7",
                "A journal entry of a template links to that template in Settings, not to the ANAF tab");

            // The clean-up of old resolved notifications: period limits, limit date, texts, and when the daily run is due.
            Check(NotificationPurgeRules.IsValidMonths(1) && NotificationPurgeRules.IsValidMonths(60) && !NotificationPurgeRules.IsValidMonths(0) && !NotificationPurgeRules.IsValidMonths(61) &&
                  NotificationPurgeRules.DefaultMonths == 12 && !NotificationSettings.Default.PurgeEnabled && NotificationSettings.Default.PurgeMonths == 12,
                "The clean-up period is 1 to 60 months, 12 by default, and the switch starts off");
            Check(NotificationPurgeRules.Cutoff(new DateOnly(2026, 9, 30), 12) == new DateOnly(2025, 9, 30) && NotificationPurgeRules.Cutoff(new DateOnly(2026, 3, 31), 1) == new DateOnly(2026, 2, 28),
                "The limit date is the given number of months before today (the last day of a shorter month is used)");
            Check(NotificationPurgeRules.RemovedText(3, new DateOnly(2025, 9, 30)) == "au fost eliminate din baza de date 3 notificări rezolvate mai vechi de 30.09.2025" &&
                  NotificationPurgeRules.RemovedText(1, new DateOnly(2025, 9, 30)) == "a fost eliminată din baza de date 1 notificare rezolvată mai veche de 30.09.2025" &&
                  NotificationPurgeRules.WillRemoveText(1, new DateOnly(2025, 9, 30)).StartsWith("va fi eliminată") &&
                  NotificationPurgeRules.WillRemoveText(2, new DateOnly(2025, 9, 30)) == "vor fi eliminate din baza de date 2 notificări rezolvate mai vechi de 30.09.2025",
                "The journal and the confirmation say how many notifications go and the limit date, as dd.mm.yyyy");
            var purgeZone = TimeZoneInfo.Utc;
            var purgeNow = new DateTime(2026, 9, 30, 10, 0, 0, DateTimeKind.Utc);
            var purgeOn = new NotificationSettings(true, 12, null, 0);
            Check(NotificationPurgeRules.IsDue(purgeOn, new DateOnly(2026, 9, 30), purgeZone) &&
                  !NotificationPurgeRules.IsDue(purgeOn with { LastPurgeUtc = purgeNow }, new DateOnly(2026, 9, 30), purgeZone) &&
                  NotificationPurgeRules.IsDue(purgeOn with { LastPurgeUtc = purgeNow }, new DateOnly(2026, 10, 1), purgeZone) &&
                  !NotificationPurgeRules.IsDue(purgeOn with { PurgeEnabled = false }, new DateOnly(2026, 9, 30), purgeZone),
                "The daily clean-up is due when the switch is on and it has not run today");
            Check(NotificationPurgeRules.Changes(NotificationSettings.Default, true, 12).Single().Field == "Ștergerea notificărilor rezolvate" &&
                  NotificationPurgeRules.Changes(NotificationSettings.Default, false, 12).Count() == 0 &&
                  NotificationPurgeRules.Changes(purgeOn, true, 3).Single() is { Field: "Vechime (luni)", Before: "12", After: "3" },
                "The journal records the switch and the period with the old and new value, and nothing when nothing changed");
            Check(AuditActions.IsCreateOrEdit(AuditActions.EditNotificationSettings) && !AuditActions.IsCreateOrEdit(AuditActions.PurgeResolvedNotifications) &&
                  AuditActions.EditNotificationSettings != AuditActions.PurgeResolvedNotifications,
                "The setting change and the clean-up are named exactly; the clean-up has no object to link to");
            Check(AuditNavigation.TargetUrl(snoozeEvent with { EntityType = AuditEntities.NotificationSettings, Action = AuditActions.EditNotificationSettings, EntityId = "1" }) == "/setari?tab=notificari&subtab=settings",
                "A journal entry of the clean-up setting links to the notification settings sub-tab");
        }

        // Extended work points: coordinates, journal names, photo rules, schema and archive registry.
        {
            static string Coordinates(string text) => WorkPointCoordinates.TryParse(text, out var latitude, out var longitude, out _) ? $"{latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}|{longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}" : "error";
            Check(Coordinates("45.7489, 21.2087") == "45.7489|21.2087" && Coordinates("45.7489 21.2087") == "45.7489|21.2087" && Coordinates("45,7489 21,2087") == "45.7489|21.2087" &&
                  Coordinates("45,7489; 21,2087") == "45.7489|21.2087" && Coordinates("-33.8688,151.2093") == "-33.8688|151.2093" && Coordinates("45.12345678, 21.1") == "45.123457|21.1",
                "Coordinates are read as pasted from a map (dot or comma decimals, comma, space or semicolon between) and rounded to 6 decimals");
            Check(Coordinates("") == "error" && Coordinates("abc") == "error" && Coordinates("45.7") == "error" && Coordinates("45.7, 21.2, 3") == "error" && Coordinates("90.1, 10") == "error" &&
                  Coordinates("-91, 10") == "error" && Coordinates("10, 180.5") == "error" && Coordinates("90, 180") == "90|180" && Coordinates("-90, -180") == "-90|-180",
                "Unreadable coordinates and values out of range (latitude -90..90, longitude -180..180) are refused");
            Check(WorkPointCoordinates.Format(45.7489m, 21.2087m) == "45.7489, 21.2087" && WorkPointCoordinates.Format(null, null) == "" && WorkPointCoordinates.Format(45.75m, null) == "",
                "Coordinates are shown as \"latitude, longitude\", empty when the point has none");
            var withCoordinates = new WorkPointInput { Name = "A", Address = "Str. X 1", UseCoordinates = true, CoordinatesText = "45,5 21,5" }.Validated();
            var withoutCoordinates = new WorkPointInput { Name = "A", Address = "Str. X 1", UseCoordinates = false, CoordinatesText = "45,5 21,5" }.Validated();
            Check(withCoordinates.Latitude == 45.5m && withCoordinates.Longitude == 21.5m && withoutCoordinates.Latitude is null && withoutCoordinates.Longitude is null && withoutCoordinates.CoordinatesText == "",
                "With the switch off the coordinates are dropped; with it on both values are kept");
            var pointBefore = new WorkPoint(5, 1, "Depozit", "Str. X 1", "0722333444", "Ion", 0, false, "text", 45.5m, 21.5m);
            Check(WorkPointRules.EditAction(pointBefore, pointBefore with { Description = "nou" }) == AuditActions.EditWorkPointDescription &&
                  WorkPointRules.EditAction(pointBefore, pointBefore with { Latitude = 46m }) == AuditActions.EditWorkPointCoordinates &&
                  WorkPointRules.EditAction(pointBefore, pointBefore with { Latitude = null, Longitude = null }) == AuditActions.EditWorkPointCoordinates &&
                  WorkPointRules.EditAction(pointBefore, pointBefore with { Description = "nou", Latitude = 46m }) == AuditActions.EditWorkPoint &&
                  WorkPointRules.EditAction(pointBefore, pointBefore with { Name = "Alt nume" }) == AuditActions.EditWorkPoint &&
                  WorkPointRules.EditAction(pointBefore, pointBefore with { Name = "Alt nume", Description = "nou" }) == AuditActions.EditWorkPoint,
                "The journal names an edit of only the description, of only the coordinates, or any other edit exactly");
            Check(new[] { AuditActions.CreateWorkPoint, AuditActions.EditWorkPoint, AuditActions.EditWorkPointDescription, AuditActions.EditWorkPointCoordinates, AuditActions.AddWorkPointPhoto }.All(AuditActions.IsCreateOrEdit) &&
                  new[] { AuditActions.CreateWorkPoint, AuditActions.EditWorkPoint, AuditActions.EditWorkPointDescription, AuditActions.EditWorkPointCoordinates, AuditActions.AddWorkPointPhoto }.Distinct().Count() == 5,
                "The work point operations are named apart and keep their link to the beneficiary page");
            byte[] Image(params byte[] head) => [.. head, .. new byte[40]];
            static bool Throws<T>(Action action) where T : Exception { try { action(); return false; } catch (T) { return true; } }
            Check(ServicePhotoRules.DetectContentType(Image(137, 80, 78, 71, 13, 10, 26, 10)) == "image/png" && ServicePhotoRules.DetectContentType(Image(255, 216, 255, 224)) == "image/jpeg" &&
                  ServicePhotoRules.DetectContentType(Image(71, 73, 70, 56, 57, 97)) == "image/gif" &&
                  ServicePhotoRules.DetectContentType([.. "RIFF"u8.ToArray(), 0, 0, 0, 0, .. "WEBP"u8.ToArray(), 0, 0]) == "image/webp",
                "Photos are recognised as PNG, JPEG, GIF or WebP by their bytes");
            Check(Throws<WorkPointOperationException>(() => ServicePhotoRules.DetectContentType("text"u8)) && Throws<WorkPointOperationException>(() => ServicePhotoRules.DetectContentType(ReadOnlySpan<byte>.Empty)) &&
                  Throws<WorkPointOperationException>(() => ServicePhotoRules.DetectContentType(new byte[ServicePhotoRules.MaximumBytes + 1])),
                "A file that is not an image, an empty file and one above 10 MB are refused");
            Check(ServicePhotoRules.SafeOriginalName(@"C:\poze\Intrare.PNG") == "Intrare.PNG" && ServicePhotoRules.NormalizeCaption("  Intrare   principala ") == "Intrare principala" &&
                  Throws<WorkPointOperationException>(() => ServicePhotoRules.NormalizeCaption(new string('x', 201))) &&
                  ServicePhotoRules.NewStoredName("image/png").EndsWith(".png") && ServicePhotoRules.NewStoredName("image/png") != ServicePhotoRules.NewStoredName("image/png") &&
                  ServicePhotoRules.Hash([1, 2, 3]).Length == 64,
                "Photo names are cleaned of path segments, captions are normalised and limited, stored names are generated");
            var migration7 = MariaSchemaMigrations.All.Single(m => m.Version == 7);
            Check(new[] { "description", "is_primary", "latitude", "longitude", "primary_beneficiary_id" }.All(column => migration7.ExpectedColumns.Contains(("beneficiary_work_points", column))) &&
                  migration7.Statements.Any(sql => sql.Contains("uq_work_points_primary")) && migration7.Statements.Any(sql => sql.Contains("ck_work_points_coordinates")) &&
                  migration7.Statements.Any(sql => sql.Contains("CREATE TABLE IF NOT EXISTS `service_photos`") && sql.Contains("ck_service_photos_owner")) &&
                  migration7.Statements.Any(sql => sql.Contains("CREATE TABLE IF NOT EXISTS `archive_work_points`")) && migration7.Statements.Any(sql => sql.Contains("CREATE TABLE IF NOT EXISTS `archive_service_photos`")),
                "MariaDB migration 7 adds the work point columns, one main point per beneficiary, the coordinate check, the photos table and the two archive tables");
            Check(ArchiveSchemaRegistry.All.Any(schema => schema.EntityType == AuditEntities.WorkPoint && schema.TableName == "archive_work_points" && schema.SupportsFiles) &&
                  ArchiveSchemaRegistry.All.Any(schema => schema.EntityType == AuditEntities.ServicePhoto && schema.TableName == "archive_service_photos" && schema.SupportsFiles),
                "The work points and their photos are registered for archiving");
            var archivedPoint = ArchiveRequests.WorkPoint(pointBefore, "Beneficiar SRL", [new ServicePhoto(9, 5, null, "a.png", "x.png", "image/png", 10, new string('a', 64), "", "ana", DateTime.UtcNow)], "Motiv");
            Check(archivedPoint.Snapshot.EntityType == AuditEntities.WorkPoint && archivedPoint.Snapshot.Relations.Count == 1 && archivedPoint.Snapshot.Relations[0].RelationType == AuditEntities.ServicePhoto,
                "The archive request of a work point carries its photos as relations");
        }
    }
}
