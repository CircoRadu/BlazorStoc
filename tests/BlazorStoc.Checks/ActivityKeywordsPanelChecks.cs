using BlazorStoc.Components.Pages;
using BlazorStoc.Components.Shared;
using BlazorStoc.Modules;
using BlazorStoc.Modules.ActivityImport;
using BlazorStoc.Services;
using Bunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorStoc.Checks;

// Nomenclator -> Cuvinte cheie mentenanță: the list of the words (add, change, activate, delete, the text to try) and the page Nomenclator with its two tabs. The service is a fake (the real
// one is checked against MariaDB).
public static class ActivityKeywordsPanelChecks
{
    private sealed class FakeKeywords : IActivityKeywordService
    {
        public List<ActivityKeyword> Items = [new(1, "mentenanță", true, "admin", 0), new(2, "revizie", false, "admin", 0)];
        public int Deleted;
        public Task<IReadOnlyList<ActivityKeyword>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ActivityKeyword>>([.. Items]);
        public Task<ActivityKeyword> AddAsync(string keyword, CancellationToken cancellationToken = default)
        {
            if (ActivityKeywordRules.Validate(keyword, out var cleaned) is { } error) throw new ActivityImportException(error);
            var item = new ActivityKeyword(Items.Max(entry => entry.Id) + 1, cleaned, true, "admin", 0);
            Items.Add(item);
            return Task.FromResult(item);
        }
        public Task<ActivityKeyword> RenameAsync(ActivityKeyword original, string keyword, CancellationToken cancellationToken = default)
        {
            var item = original with { Keyword = keyword, Version = original.Version + 1 };
            Items[Items.FindIndex(entry => entry.Id == original.Id)] = item;
            return Task.FromResult(item);
        }
        public Task<ActivityKeyword> SetActiveAsync(ActivityKeyword original, bool active, CancellationToken cancellationToken = default)
        {
            var item = original with { Active = active, Version = original.Version + 1 };
            Items[Items.FindIndex(entry => entry.Id == original.Id)] = item;
            return Task.FromResult(item);
        }
        public Task DeleteAsync(ActivityKeyword original, CancellationToken cancellationToken = default) { Deleted++; Items.RemoveAll(entry => entry.Id == original.Id); return Task.CompletedTask; }
    }

    private sealed class FakeSystemTypes : ISystemTypeRepository
    {
        public Task<IReadOnlyList<SystemType>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<SystemType>>([new SystemType(1, "TVCI", true, 1, ["CCTV"], 0)]);
        public Task<SystemType?> FindAsync(string text, CancellationToken cancellationToken = default) => Task.FromResult<SystemType?>(null);
        public Task<SystemType> CreateAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SystemType> RenameAsync(SystemType original, string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SystemType> SetActiveAsync(SystemType original, bool active, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SystemType> MoveAsync(SystemType original, int direction, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SystemType> AddAliasAsync(SystemType type, string alias, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SystemType> RemoveAliasAsync(SystemType type, string alias, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    public static Task RunAsync(Action<bool, string> check)
    {
        var keywords = new FakeKeywords();
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddLogging();
        context.Services.AddSingleton<IActivityKeywordService>(keywords);
        var cut = context.Render<ActivityKeywordsPanel>();
        cut.WaitForAssertion(() => { if (cut.FindAll("#keywords-table tbody tr").Count == 0) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
        check(cut.FindAll("#keywords-table tbody tr").Count == 2 && cut.Find("#keyword-1").TextContent.Contains("mentenanță", StringComparison.Ordinal) && cut.Find("#keyword-2").ClassList.Contains("inactive-row"),
            "Maintenance keywords panel: the words are listed, an inactive one is shown as such");
        check(cut.Markup.Contains("Nu contează literele mari sau mici", StringComparison.Ordinal) && cut.Markup.Contains("notificare", StringComparison.Ordinal), "Maintenance keywords panel: the page explains what the words do, what does not matter and that a resembling word asks for validation");
        cut.Find("#keyword-new").Input("verificare periodica");
        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Adaugă cuvântul").Click();
        cut.WaitForAssertion(() => { if (cut.FindAll("#keywords-table tbody tr").Count != 3) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
        check(keywords.Items.Any(item => item.Keyword == "verificare periodica") && cut.Markup.Contains("Cuvântul cheie a fost adăugat", StringComparison.Ordinal), "Maintenance keywords panel: a word is added and shown");
        cut.Find("#keyword-new").Input("ab");
        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Adaugă cuvântul").Click();
        cut.WaitForAssertion(() => { if (!cut.Markup.Contains("prea scurt", StringComparison.Ordinal)) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
        check(keywords.Items.Count == 3, "Maintenance keywords panel: a refused word shows the reason and is not added");
        cut.Find("#keyword-2 input[role=switch]").Change(true);
        cut.WaitForAssertion(() => { if (!keywords.Items.Single(item => item.Id == 2).Active) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
        check(keywords.Items.Single(item => item.Id == 2).Active, "Maintenance keywords panel: the switch activates a word");
        cut.Find("#keyword-1 button[title='Șterge mentenanță']").Click();
        check(cut.Markup.Contains("Ștergi „mentenanță”?", StringComparison.Ordinal) && keywords.Deleted == 0, "Maintenance keywords panel: deleting asks for a confirmation first");
        cut.Find("#keyword-1 button[title='Da, șterge']").Click();
        cut.WaitForAssertion(() => { if (cut.FindAll("#keyword-1").Count != 0) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
        check(keywords.Deleted == 1, "Maintenance keywords panel: after the confirmation the word is deleted");
        keywords.Items = [new(1, "mentenanță", true, "admin", 0)];
        cut = context.Render<ActivityKeywordsPanel>();
        cut.WaitForAssertion(() => { if (cut.FindAll("#keywords-table").Count == 0) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
        cut.Find("#keyword-try").Input("am facut MENTENANTA anuala");
        check(cut.Find("#keyword-try-result").TextContent.Contains("Cuvânt găsit", StringComparison.Ordinal), "Maintenance keywords panel: the text to try shows an exact word");
        cut.Find("#keyword-try").Input("am facut m3ntenan@a");
        check(cut.Find("#keyword-try-result").TextContent.Contains("Seamănă cu „mentenanță”", StringComparison.Ordinal) && cut.Find("#keyword-try-result").TextContent.Contains("m3ntenan@a", StringComparison.Ordinal),
            "Maintenance keywords panel: the text to try shows a resembling word, with how it was written (digits and symbols too)");
        cut.Find("#keyword-try").Input("am schimbat routerul");
        check(cut.Find("#keyword-try-result").TextContent.Contains("Niciun cuvânt cheie", StringComparison.Ordinal), "Maintenance keywords panel: the text to try without a word says it is taken on demand");

        // The page: three tabs, the types of systems first, the keywords in the second, the risk analyzers in the third.
        var registry = new ModuleRegistry();
        registry.Add(new ActivityImportModule(), new ServiceCollection(), new ConfigurationBuilder().Build());
        using var page = new BunitContext();
        page.JSInterop.Mode = JSRuntimeMode.Loose;
        page.Services.AddLogging();
        page.Services.AddSingleton(registry);
        page.Services.AddSingleton<IActivityKeywordService>(new FakeKeywords());
        page.Services.AddSingleton<ISystemTypeRepository>(new FakeSystemTypes());
        var nomenclator = page.Render<SystemTypes>();
        nomenclator.WaitForAssertion(() => { if (nomenclator.FindAll("table.system-types").Count == 0) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
        check(nomenclator.FindAll("[role=tab]").Select(tab => tab.TextContent.Trim()).SequenceEqual(["Tipuri de sisteme", "Cuvinte cheie mentenanță", "Analizor de risc"]) && nomenclator.Find("#tab-sisteme").GetAttribute("aria-selected") == "true"
            && nomenclator.Markup.Contains("TVCI", StringComparison.Ordinal) && nomenclator.FindAll("#keywords-table").Count == 0, "Nomenclator page: the types of systems are the first tab and the keywords are not shown yet");
        nomenclator.Find("#tab-cuvinte-mentenanta").Click();
        nomenclator.WaitForAssertion(() => { if (nomenclator.FindAll("#keywords-table").Count == 0) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
        check(nomenclator.Find("#tab-cuvinte-mentenanta").GetAttribute("aria-selected") == "true" && nomenclator.FindAll("table.system-types:not(#keywords-table)").Count == 0 && nomenclator.Markup.Contains("mentenanță", StringComparison.Ordinal),
            "Nomenclator page: the second tab shows the maintenance keywords in place of the types of systems");
        return Task.CompletedTask;
    }
}
