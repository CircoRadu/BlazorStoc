using System.IO.Compression;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using BlazorStoc.Modules.BugReports;
using BlazorStoc.Services;

namespace BlazorStoc.Checks;

public static partial class MariaExtendedChecks
{
    // ---- Raportare erori ------------------------------------------------------------------------------------------------------
    private static async Task BugReportsAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe)
    {
        var started = DateTime.UtcNow.AddSeconds(-1);
        // A tiny valid PNG (1x1) so the picture is accepted by the content sniffing.
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
        var author = new MariaBugReportService(configuration, new TestAccessControl(false, "proba.raport"), audit);
        var other = new MariaBugReportService(configuration, new TestAccessControl(false, "proba.altul"), audit);
        var boss = new MariaBugReportService(configuration, admin, audit);
        try
        {
            var report = await author.CreateAsync(new BugReportInput("Proba raport", "Pasi, asteptat, intamplat", BugReportRules.SeverityBlocking, "/produse", "Produse",
                "{\"url\":\"/produse\"}", [new BugReportImage(BugReportRules.KindScreenshot, "pagina.png", png), new BugReportImage(BugReportRules.KindPasted, "lipit.png", png)]));
            Check(report.Number == BugReportRules.Number(report.Id) && report.State == BugReportRules.StateNew && report.FileCount == 2, "Bug report: saved with its number, the state Nou and its two pictures");
            var refusedEmpty = await Rejects<BugReportException>(() => author.CreateAsync(new BugReportInput(" ", "x", BugReportRules.SeverityMinor, "", "", "", [])), "A report without a title is refused");
            var refusedFile = await Rejects<BugReportException>(() => author.CreateAsync(new BugReportInput("t", "d", BugReportRules.SeverityMinor, "", "", "", [new BugReportImage(BugReportRules.KindFile, "a.txt", "nu e imagine"u8.ToArray())])), "A file that is not a picture is refused");
            var refusedSeverity = await Rejects<BugReportException>(() => author.CreateAsync(new BugReportInput("t", "d", "Altceva", "", "", "", [])), "An unknown severity is refused");
            Check(refusedEmpty is not null && refusedFile is not null && refusedSeverity is not null, "Bug report: a missing title, a non-picture and an unknown severity are refused");

            var mine = await author.GetMineAsync();
            var others = await other.GetMineAsync();
            Check(mine.Any(item => item.Id == report.Id) && others.All(item => item.Id != report.Id), "Bug report: a user sees only the reports of their own");
            var deniedList = await Rejects<AccessDeniedException>(() => author.QueryAsync(new BugReportQuery(), 1, 10), "An ordinary user cannot list every report");
            var deniedFiles = await Rejects<AccessDeniedException>(() => other.GetFilesAsync(report.Id), "Another user cannot read the pictures of a report");
            var deniedState = await Rejects<AccessDeniedException>(() => author.ChangeStateAsync(report.Id, BugReportRules.StateResolved, ""), "An ordinary user cannot change the state");
            var deniedExport = await Rejects<AccessDeniedException>(() => author.ExportAsync([report.Id]), "An ordinary user cannot export");
            Check(deniedList is not null && deniedFiles is not null && deniedState is not null && deniedExport is not null, "Bug report: listing, reading others' pictures, changing the state and exporting are for the administrator");

            var files = await author.GetFilesAsync(report.Id);
            Check(files.Count == 2 && (await author.GetFileContentAsync(files[0].Id))?.Content.SequenceEqual(png) == true, "Bug report: the author reads back their pictures byte for byte");

            var page = await boss.QueryAsync(new BugReportQuery(State: BugReportRules.StateNew, Severity: BugReportRules.SeverityBlocking, User: "proba.rap"), 1, 10);
            Check(page.Items.Any(item => item.Id == report.Id), "Bug report: the administrator filters by state, severity and author");
            var openBefore = await boss.CountOpenAsync();
            Check(openBefore >= 1 && await author.CountOpenAsync() == 0, "Bug report: the badge of the tab counts the reports not resolved or rejected, for who may see them all (an ordinary user gets 0)");
            var changed = await boss.ChangeStateAsync(report.Id, BugReportRules.StateResolved, "Rezolvat in proba");
            Check(changed.State == BugReportRules.StateResolved && changed.ResolvedUtc is not null && changed.AdminNote == "Rezolvat in proba", "Bug report: the administrator changes the state with a note (Rezolvat records the time)");

            Check(await boss.CountOpenAsync() == openBefore - 1, "Bug report: a resolved report leaves the count of the badge");
            var closedPage = await boss.QueryAsync(new BugReportQuery(User: "proba.rap", Closed: true), 1, 10);
            var openPage = await boss.QueryAsync(new BugReportQuery(User: "proba.rap", Closed: false), 1, 10);
            Check(closedPage.Items.Any(item => item.Id == report.Id) && openPage.Items.All(item => item.Id != report.Id), "Bug report: the tabs split resolved/rejected reports from the unresolved ones");

            var export = await boss.ExportAsync([report.Id]);
            using (var zip = new ZipArchive(new MemoryStream(export.Content)))
            {
                var names = zip.Entries.Select(entry => entry.FullName).ToList();
                Check(export.FileName == report.Number + ".zip" && names.Contains(report.Number + "/raport.json") && names.Contains(report.Number + "/raport.txt") && names.Count(name => name.EndsWith(".png", StringComparison.Ordinal)) == 2,
                    "Bug report: the ZIP holds raport.json, raport.txt and both pictures");
            }

            var edited = await boss.UpdateContentAsync(report.Id, "Titlu corectat", "Descriere corectata", [new BugReportImage(BugReportRules.KindFile, "extra.png", png)]);
            Check(edited.Title == "Titlu corectat" && edited.Description == "Descriere corectata" && (await author.GetFilesAsync(report.Id)).Count == 3, "Bug report: the administrator corrects the title and the description and adds a picture (the pictures already there stay)");
            Check(await Rejects<AccessDeniedException>(() => author.UpdateContentAsync(report.Id, "x", "y", []), "An ordinary user cannot edit the content of a report") is not null, "Bug report: editing the content is for the administrator");
            var events = (await audit.GetEventsAsync()).Where(item => item.TimestampUtc >= started && item.EntityType == AuditEntities.BugReport).Select(item => item.Action).ToList();
            Check(events.Contains(AuditActions.CreateBugReport) && events.Contains(AuditActions.ChangeBugReportState) && events.Contains(AuditActions.EditBugReportContent) && events.Contains(AuditActions.ExportBugReport), "Bug report: creation, state change and export are journaled with their own actions");

            await boss.DeleteAsync(report.Id);
            await using var left = new MySqlCommand("SELECT (SELECT COUNT(*) FROM bug_reports WHERE id=@id) + (SELECT COUNT(*) FROM bug_report_files WHERE report_id=@id)", probe);
            left.Parameters.AddWithValue("@id", report.Id);
            Check(Convert.ToInt32(await left.ExecuteScalarAsync()) == 0, "Bug report: deleting a report removes its pictures too");
            Check((await audit.GetEventsAsync()).Any(item => item.TimestampUtc >= started && item.Action == AuditActions.DeleteBugReport), "Bug report: the deletion is journaled with its own action");
            var second = await author.CreateAsync(new BugReportInput("Proba stergere totala", "Descriere", BugReportRules.SeverityImportant, "/produse", "Produse", "{}", [new BugReportImage(BugReportRules.KindFile, "x.png", png)]));
            var third = await author.CreateAsync(new BugReportInput("Proba ramane deschis", "Descriere", BugReportRules.SeverityImportant, "/produse", "Produse", "{}", []));
            await boss.ChangeStateAsync(second.Id, BugReportRules.StateRejected, "");
            var deniedAll = await Rejects<AccessDeniedException>(() => author.DeleteClosedAsync(), "An ordinary user cannot delete all the resolved reports");
            var removed = await boss.DeleteClosedAsync();
            Check(deniedAll is not null && removed >= 1, "Bug report: the administrator deletes every resolved or rejected report at once, the unresolved ones stay");
            await using var leftAll = new MySqlCommand("SELECT (SELECT COUNT(*) FROM bug_reports WHERE id=@id) + (SELECT COUNT(*) FROM bug_report_files WHERE report_id=@id)", probe);
            leftAll.Parameters.AddWithValue("@id", second.Id);
            await using var stays = new MySqlCommand("SELECT COUNT(*) FROM bug_reports WHERE id=@id", probe);
            stays.Parameters.AddWithValue("@id", third.Id);
            Check(Convert.ToInt32(await stays.ExecuteScalarAsync()) == 1, "Bug report: the bulk deletion leaves the unresolved reports");
            Check(Convert.ToInt32(await leftAll.ExecuteScalarAsync()) == 0 && (await audit.GetEventsAsync()).Any(item => item.TimestampUtc >= started && item.Action == AuditActions.DeleteAllClosedBugReports), "Bug report: the bulk deletion removes the pictures too and is journaled with its own action");
        }
        finally { await using var cleanup = new MySqlCommand("DELETE FROM bug_reports WHERE user_name IN ('proba.raport','proba.altul')", probe); await cleanup.ExecuteNonQueryAsync(); }
    }
}
