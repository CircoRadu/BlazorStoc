using BlazorStoc.Services;

namespace BlazorStoc.Checks;

// Setari -> Baza de date -> Spatiu: folder arithmetic (nested folders left out, missing folder is null) and the collapsed-by-default sections
// of the database and ANAF settings pages.
public static class DiskUsageChecks
{
    public static void Run(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "diskusage-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "inner"));
            File.WriteAllBytes(Path.Combine(root, "a.bin"), new byte[100]);
            File.WriteAllBytes(Path.Combine(root, "inner", "b.bin"), new byte[40]);
            var all = DiskUsageMeasure.Folder(root, []);
            check(all is { Bytes: 140, Files: 2 }, "Disk usage: a folder is measured with its subfolders");
            var without = DiskUsageMeasure.Folder(root, [Path.Combine(root, "inner")]);
            check(without is { Bytes: 100, Files: 1 }, "Disk usage: a nested category folder is left out of the wider one, so nothing is counted twice");
            check(DiskUsageMeasure.Folder(Path.Combine(root, "missing"), []) is null, "Disk usage: a missing folder is reported as not existing");
        }
        finally { try { Directory.Delete(root, recursive: true); } catch (IOException) { } }

        var settings = File.ReadAllText(Path.Combine(Root(), "Components", "Pages", "Settings.razor"));
        check(settings.Contains("(DatabaseSpaceSubtab, \"Spațiu\")") && settings.Contains("<DiskUsagePanel />"), "Disk usage: the database settings have the Spatiu tab");
        var status = File.ReadAllText(Path.Combine(Root(), "Components", "Shared", "DatabaseStatusPanel.razor"));
        check(status.Contains("<details class=\"db-collapse\">") && !status.Contains("<details class=\"db-collapse\" open"), "Database page: the sections of the Stare tab are collapsed by default");
        foreach (var panel in new[] { Path.Combine("Modules", "Proposals", "ProposalsPanel.razor"), Path.Combine("Components", "Shared", "BugReportsPanel.razor") })
        {
            var text = File.ReadAllText(Path.Combine(Root(), panel));
            check(text.Contains("new LiveRefresh(ChangeFeed") && text.Contains("StartLive();") && text.Contains("live?.Dispose();"), $"PROP-0003: {Path.GetFileName(panel)} refreshes itself on the change feed");
        }
        check(settings.Contains("aria-labelledby=\"subtab-@DatabaseConnectionSubtab\" class=\"db-stack\"") && File.ReadAllText(Path.Combine(Root(), "wwwroot", "app.css")).Contains(".db-stack > .catalog { margin:12px 0 0 !important;"), "BUG-0009/0011: the sections of Conexiune are stacked without gaps");
        var dialogJs = File.ReadAllText(Path.Combine(Root(), "wwwroot", "app-dialog.js"));
        var dialogRazor = File.ReadAllText(Path.Combine(Root(), "Components", "Shared", "AppDialog.razor"));
        check(dialogRazor.Contains("data-app-dialog-resize") && dialogJs.Contains("data-app-dialog-resize") && dialogJs.Contains(".app-dialog-head") && !dialogJs.Contains("MutationObserver"), "PROP-0007: windows move by the head and resize by the bottom-right handle only, without observers");
        var diskPanel = File.ReadAllText(Path.Combine(Root(), "Components", "Shared", "DiskUsagePanel.razor"));
        check(diskPanel.Contains("id=\"disk-total\"") && File.ReadAllText(Path.Combine(Root(), "Services", "DiskUsage.cs")).Contains("PathBytes"), "BUG-0013: the total of the application and the figure of the partition come from the same per-folder sizes");
        var annotateJs = File.ReadAllText(Path.Combine(Root(), "wwwroot", "image-annotate.js"));
        check(annotateJs.Contains("MAX_ZONES = 10") && annotateJs.Contains("annotator-remove") && !annotateJs.Contains("MutationObserver") && File.ReadAllText(Path.Combine(Root(), "Components", "Shared", "BugReportDialog.razor")).Contains("<ImageAnnotator") && File.ReadAllText(Path.Combine(Root(), "Modules", "Proposals", "ProposalDialog.razor")).Contains("<ImageAnnotator"), "PROP-0009: both forms mark zones (at most 10, deletable only) in the floating window of the picture");
        foreach (var panel in new[] { Path.Combine("Modules", "Proposals", "ProposalsPanel.razor"), Path.Combine("Components", "Shared", "BugReportsPanel.razor") })
        {
            var text = File.ReadAllText(Path.Combine(Root(), panel));
            check(text.Contains("row-icon edit") && text.Contains("row-icon export") && text.Contains("<AppDialog Size=\"AppDialogSize.L\"") && text.Contains("-detail-edit") && text.Contains("-detail-export") && !text.Contains("<section class=\"catalog bug-detail\""), $"PROP-0012: {Path.GetFileName(panel)} opens the detail in a popup with view and edit modes, and has edit and export icons on the rows");
        }
        foreach (var panel in new[] { Path.Combine("Modules", "Proposals", "ProposalsPanel.razor"), Path.Combine("Components", "Shared", "BugReportsPanel.razor") })
        {
            var text = File.ReadAllText(Path.Combine(Root(), panel));
            check(text.Contains("-detail-description") && text.Contains("UpdateContentAsync") && text.Contains("OnAddFilesAsync") && text.Contains("-detail-title-input"), $"BUG-0014: {Path.GetFileName(panel)} edits the title and the description and adds pictures in the edit mode");
        }
        var dialogCss = File.ReadAllText(Path.Combine(Root(), "wwwroot", "app.css"));
        check(dialogCss.Contains(".app-dialog-frame:has(> .app-dialog-panel.app-dialog-l) { width:min(960px, 100%); }") && dialogCss.Contains(".app-dialog-panel > .app-dialog-head { align-items:center") && dialogCss.Contains(".bug-detail:not(.app-dialog-panel)"), "BUG-0014: the frame has the width of the window so the bar sits on the panel edge, the head is a visible title bar, the detail panel has no stray margin");
        check(dialogCss.Contains(".bug-filters { margin-bottom:12px; padding:18px 16px 0; }"), "The filters of the lists of reports and proposals have room under the tabs and from the edge of the section");
        foreach (var panel2 in new[] { Path.Combine("Modules", "Proposals", "ProposalsPanel.razor"), Path.Combine("Components", "Shared", "BugReportsPanel.razor") })
        {
            var text2 = File.ReadAllText(Path.Combine(Root(), panel2));
            check(text2.Contains("-delete-all\"") && text2.Contains("DeleteClosedAsync") && text2.Contains("nu se arhivează"), $"{Path.GetFileName(panel2)}: the table of the resolved ones has Delete all, confirmed, with an honest warning (deleted from the server, not archived)");
        }
        check(File.ReadAllText(Path.Combine(Root(), "Components", "Shared", "UsersPanel.razor")).Contains("SetDeveloperAsync") && File.ReadAllText(Path.Combine(Root(), "Components", "Shared", "DevelopersPanel.razor")).Contains("developers-table") && File.ReadAllText(Path.Combine(Root(), "Components", "Pages", "UserDetail.razor")).Contains("user-developer-switch") && File.ReadAllText(Path.Combine(Root(), "Components", "Pages", "Settings.razor")).Contains("<DevelopersPanel />"), "PROP-0013: the switch on the row and on the page of the account and the table of the developers");
        check(File.ReadAllText(Path.Combine(Root(), "Modules", "Notifications", "Notifications.razor")).Contains("<BugAlertsTab") && File.ReadAllText(Path.Combine(Root(), "Modules", "BugReports", "BugAlertsTab.razor")).Contains("setari-bug-raport.view") && File.ReadAllText(Path.Combine(Root(), "Modules", "Proposals", "ProposalAlertsTab.razor")).Contains("setari-propuneri.view"), "PROP-0013: the tabs Bugs and Propuneri of Notificari are shown to the developers only");
        var anaf = File.ReadAllText(Path.Combine(Root(), "Modules", "Anaf", "AnafConfigurator.razor"));
        check(anaf.Split("<details class=\"anaf-section anaf-collapse\">").Length == 6 && !anaf.Contains("<fieldset"), "ANAF page: the five sections are collapsed by default");
    }

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "BlazorStoc.csproj"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Project root not found");
    }
}
