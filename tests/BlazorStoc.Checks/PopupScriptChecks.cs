using System.Text.RegularExpressions;
using BlazorStoc.Components.Shared;
using BlazorStoc.Modules;
using BlazorStoc.Modules.BugReports;
using BlazorStoc.Modules.Proposals;
using BlazorStoc.Services;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorStoc.Checks;

// The windows over a page (Components/Shared/AppDialog.razor). The bar above a window is declarative: the frame reserves its place, the buttons come from the actions registered by the modules, and
// no script watches the page (in 0.5.60-0.5.62 an observer on the whole page looped without end while a popup was open: 237,000 changes in 3 seconds, found 10.11.2026).
public static class PopupScriptChecks
{
    private static string? Root()
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "BlazorStoc.csproj"))) root = Path.GetDirectoryName(root.TrimEnd(Path.DirectorySeparatorChar));
        return root;
    }

    private static (ModuleRegistry Registry, IReadOnlyList<DialogBarAction> Actions) Registered()
    {
        var registry = new ModuleRegistry();
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        registry.Add(new BugReportsModule(), services, configuration);
        registry.Add(new ProposalsModule(), services, configuration);
        return (registry, services.Where(item => item.ServiceType == typeof(DialogBarAction)).Select(item => (DialogBarAction)item.ImplementationInstance!).ToList());
    }

    private static BunitContext Context(ModuleRegistry registry, IReadOnlyList<DialogBarAction> actions)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton(registry);
        foreach (var action in actions) context.Services.AddSingleton(action);
        return context;
    }

    private static string Labels(IRenderedComponent<AppDialog> cut) => string.Join("|", cut.FindAll("[data-dialog-action]").Select(item => item.GetAttribute("data-dialog-action")));

    public static Task RunAsync(Action<bool, string> check)
    {
        var root = Root();
        check(root is not null, "AppDialog: the project folder is found");
        if (root is null) return Task.CompletedTask;

        // ---- the scripts: no observer of the page, no floating bar ----
        foreach (var name in new[] { "bug-report.js", "app-dialog.js" })
        {
            var script = File.ReadAllText(Path.Combine(root, "wwwroot", name));
            check(!script.Contains("MutationObserver", StringComparison.Ordinal) && !script.Contains("makeBar", StringComparison.Ordinal) && !script.Contains("reposition", StringComparison.Ordinal) && !script.Contains("moveHeadingClose", StringComparison.Ordinal),
                $"AppDialog: {name} has no observer of the page and no code that places the bar (the bar is declarative)");
        }

        // ---- the buttons of the bar follow the state of the modules, also in a window that is already open ----
        var (registry, actions) = Registered();
        using (var context = Context(registry, actions))
        {
            var cut = context.Render<AppDialog>(p => p.Add(x => x.Title, "Titlu").Add(x => x.ChildContent, (RenderFragment)(b => b.AddContent(0, "corp"))));
            check(Labels(cut) == "bug-report|proposal", "AppDialog: the bar shows the report button then the proposal button (registered by BugReports and Proposals, in their order)");
            registry.Stop(ProposalsModule.ModuleId, "test", "test");
            cut.WaitForAssertion(() => { if (Labels(cut) != "bug-report") throw new InvalidOperationException(Labels(cut)); });
            check(Labels(cut) == "bug-report", "AppDialog: a module stopped in Setari -> Module takes its button out of the windows that are open");
            registry.Stop(BugReportsModule.ModuleId, "test", "test");
            cut.WaitForAssertion(() => { if (Labels(cut) != "") throw new InvalidOperationException(Labels(cut)); });
            check(cut.FindAll("[data-app-dialog-close]").Count == 1, "AppDialog: with every module out only the close button is left in the bar");
            registry.Start(ProposalsModule.ModuleId);
            registry.Start(BugReportsModule.ModuleId);
            cut.WaitForAssertion(() => { if (Labels(cut) != "bug-report|proposal") throw new InvalidOperationException(Labels(cut)); });
            check(true, "AppDialog: the buttons come back when the modules start again");
            var bug = cut.Find("[data-dialog-action=bug-report]");
            check(bug.HasAttribute("data-bug-open") && bug.HasAttribute("data-bug-popup") && bug.GetAttribute("aria-label") == "Raportează o eroare în această fereastră", "AppDialog: the report button carries the attributes the capture script reads and its aria-label");
        }

        // ---- the close button calls OnClose; no OnClose = it presses the closing button of the window (script) ----
        {
            var (reg, acts) = Registered();
            using var context = Context(reg, acts);
            var closed = 0;
            var cut = context.Render<AppDialog>(p => p.Add(x => x.Title, "T").Add(x => x.OnClose, EventCallback.Factory.Create(new object(), () => closed++)));
            cut.Find("[data-app-dialog-close]").Click();
            check(closed == 1 && !cut.Find("[data-app-dialog-close]").HasAttribute("data-app-dialog-proxy"), "AppDialog: the close button calls OnClose (the flow of the Inchide button of the window)");
            var plain = context.Render<AppDialog>(p => p.Add(x => x.Title, "T"));
            check(plain.Find("[data-app-dialog-close]").HasAttribute("data-app-dialog-proxy"), "AppDialog: without OnClose the close button is a proxy of the closing button the window has (wwwroot/app-dialog.js)");
            var none = context.Render<AppDialog>(p => p.Add(x => x.ShowClose, false).Add(x => x.ShowModuleActions, false));
            check(none.FindAll(".app-dialog-bar button").Count == 0, "AppDialog: ShowClose=false and ShowModuleActions=false leave an empty (but reserved) bar");
        }

        // ---- the structure is the same at every size: backdrop > frame > bar, then panel (head, body, foot) ----
        {
            var (reg, acts) = Registered();
            using var context = Context(reg, acts);
            var shapes = new List<string>();
            foreach (var size in new[] { AppDialogSize.Auto, AppDialogSize.S, AppDialogSize.M, AppDialogSize.L, AppDialogSize.XL })
            {
                var cut = context.Render<AppDialog>(p => p.Add(x => x.Title, "Titlu").Add(x => x.HelpTopic, "ajutor").Add(x => x.Size, size).Add(x => x.Footer, (RenderFragment)(b => b.AddContent(0, "subsol")))
                    .Add(x => x.ChildContent, (RenderFragment)(b => b.AddContent(0, "corp"))));
                var frame = cut.Find("[data-app-dialog-frame]");
                var children = frame.Children.Select(item => item.ClassList.Contains("app-dialog-bar") ? "bar" : item.ClassList.Contains("app-dialog-panel") ? "panel" : "?").ToList();
                var panelParts = cut.Find(".app-dialog-panel").Children.Select(item => item.ClassList.Contains("app-dialog-head") ? "head" : item.ClassList.Contains("app-dialog-body") ? "body" : item.ClassList.Contains("app-dialog-foot") ? "foot" : "?").ToList();
                shapes.Add(string.Join(",", children) + "/" + string.Join(",", panelParts));
                if (size == AppDialogSize.Auto) check(cut.Find(".app-dialog-head [data-help-topic]").GetAttribute("data-help-topic") == "ajutor" && cut.Find("[role=dialog]").GetAttribute("aria-labelledby") == cut.Find(".app-dialog-head h2").Id, "AppDialog: the head has the title and the help button; the panel is labelled by the title");
                check(cut.FindAll(".app-dialog-panel .app-dialog-bar").Count == 0, $"AppDialog: the bar is outside the panel, never over its head ({size})");
            }
            check(shapes.Distinct().Count() == 1 && shapes[0] == "bar,panel/head,body,foot", "AppDialog: the structure (bar above the panel; head, body, foot) is identical for every size");
        }

        // ---- narrow screens: round icons with the symbol centred (the stylesheet) ----
        {
            var css = File.ReadAllText(Path.Combine(root, "wwwroot", "app.css"));
            var narrow = Regex.Match(css, @"@media \(max-width: 720px\) \{[^@]*?\.app-dialog-action \{([^}]*)\}[\s\S]*?\.app-dialog-action-icon \{([^}]*)\}[\s\S]*?\.app-dialog-action-label \{([^}]*)\}");
            check(narrow.Success && narrow.Groups[1].Value.Contains("border-radius:50%") && narrow.Groups[1].Value.Contains("width:32px") && narrow.Groups[1].Value.Contains("height:32px") && narrow.Groups[1].Value.Contains("justify-content:center")
                  && narrow.Groups[3].Value.Contains("clip:rect"), "AppDialog: under 720 px the buttons of the bar are round icons (same size and shape as the red close button), the label stays for screen readers");
            check(Regex.IsMatch(css, @"\.app-dialog-bar \{[^}]*height:56px"), "AppDialog: the bar has a reserved height in the structure of the frame");
        }

        // ---- no window written by hand outside AppDialog ----
        var forbidden = new[] { "delete-overlay", "invoice-modal-backdrop", "modal-backdrop", "detail-overlay", "role=\"dialog\"", "role=\"alertdialog\"", "<dialog" };
        var offenders = new List<string>();
        foreach (var folder in new[] { "Components", "Modules", "Pages" }.Select(name => Path.Combine(root, name)).Where(Directory.Exists))
            foreach (var file in Directory.EnumerateFiles(folder, "*.razor", SearchOption.AllDirectories))
            {
                if (Path.GetFileName(file) == "AppDialog.razor") continue;   // the one justified place: the component itself
                var text = File.ReadAllText(file);
                var hits = forbidden.Where(token => text.Contains(token, StringComparison.Ordinal)).ToList();
                if (hits.Count > 0) offenders.Add(Path.GetFileName(file) + " (" + string.Join(", ", hits) + ")");
            }
        check(offenders.Count == 0, "AppDialog: every window over a page is an AppDialog (no delete-overlay, role=dialog, <dialog> or modal backdrop written by hand outside Components/Shared/AppDialog.razor)" + (offenders.Count > 0 ? ": " + string.Join("; ", offenders) : ""));
        return Task.CompletedTask;
    }
}
