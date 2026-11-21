using BlazorStoc.Components.Shared;
using Bunit;
using Microsoft.AspNetCore.Components;

namespace BlazorStoc.Checks;

// The shared pager of the tables (Components/Shared/Paged.razor): rows per page with a choice, hidden for short lists, "Toate" shows everything.
public static class PagedChecks
{
    public static Task RunAsync(Action<bool, string> check)
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var items = Enumerable.Range(1, 60).ToList();
        RenderFragment<IReadOnlyList<int>> rows = page => builder => { foreach (var item in page) { builder.OpenElement(0, "p"); builder.AddAttribute(1, "class", "row"); builder.AddContent(2, item); builder.CloseElement(); } };
        var cut = context.Render<Paged<int>>(parameters => parameters.Add(p => p.Items, items).Add(p => p.Id, "t-size").Add(p => p.ChildContent, rows));
        check(cut.FindAll("p.row").Count == 25 && cut.FindAll("#t-size").Count == 1, "Paged: a long list shows the first 25 rows and the page size selector");
        cut.Find("#t-size").Change("10");
        check(cut.FindAll("p.row").Count == 10 && cut.Markup.Contains("1–10 din 60", StringComparison.Ordinal), "Paged: the number of rows per page can be chosen");
        cut.Find("button[aria-label='Pagina următoare']").Click();
        check(cut.Find("p.row").TextContent == "11", "Paged: the next page shows the next rows");
        cut.Find("#t-size").Change("0");
        check(cut.FindAll("p.row").Count == 60, "Paged: \"Toate\" shows every row");
        var small = context.Render<Paged<int>>(parameters => parameters.Add(p => p.Items, items.Take(8)).Add(p => p.Id, "s-size").Add(p => p.ChildContent, rows));
        check(small.FindAll("p.row").Count == 8 && small.FindAll("#s-size").Count == 0, "Paged: a short list has no pager");
        // Ordering from the headers (SortTh) and what the table remembers while the browser tab stays open.
        var names = new[] { "Zeta", "Ștefan", "alfa", "", "Beta" };
        var sorts = new Dictionary<string, Func<string, IComparable?>> { ["name"] = item => item, ["len"] = item => item.Length };
        RenderFragment<IReadOnlyList<string>> sortable = page => builder =>
        {
            builder.OpenComponent<SortTh>(0); builder.AddAttribute(1, "Key", "name"); builder.AddAttribute(2, "ChildContent", (RenderFragment)(inner => inner.AddContent(0, "NUME"))); builder.CloseComponent();
            builder.OpenComponent<SortTh>(3); builder.AddAttribute(4, "Key", "none"); builder.AddAttribute(5, "ChildContent", (RenderFragment)(inner => inner.AddContent(0, "FARA"))); builder.CloseComponent();
            foreach (var item in page) { builder.OpenElement(6, "p"); builder.AddAttribute(7, "class", "row"); builder.AddContent(8, item); builder.CloseElement(); }
        };
        string Order(IRenderedComponent<Paged<string>> view) => string.Join("|", view.FindAll("p.row").Select(row => row.TextContent));
        var sorted = context.Render<Paged<string>>(parameters => parameters.Add(p => p.Items, names).Add(p => p.Id, "o-size").Add(p => p.Sorts, sorts).Add(p => p.ChildContent, sortable));
        check(Order(sorted) == "Zeta|Ștefan|alfa||Beta" && sorted.FindAll("button.sort-header").Count == 1 && sorted.FindAll("th").Count == 2, "Paged: the order is the original one until a header is clicked, and only the columns with a sort have a header button");
        sorted.Find("button.sort-header").Click();
        check(Order(sorted) == "alfa|Beta|Ștefan|Zeta|" && sorted.Find("th[aria-sort]").GetAttribute("aria-sort") == "ascending", "Paged: a click on a header orders ascending, without regard to case or diacritics, and the empty values stay last");
        sorted.Find("button.sort-header").Click();
        check(Order(sorted) == "Zeta|Ștefan|Beta|alfa|" && sorted.Find("th[aria-sort]").GetAttribute("aria-sort") == "descending", "Paged: a second click orders descending, the empty value still last");
        sorted.Find("button.sort-header").Click();
        check(Order(sorted) == "Zeta|Ștefan|alfa||Beta" && sorted.Find("th[aria-sort]").GetAttribute("aria-sort") == "none", "Paged: a third click goes back to the original order");
        sorted.Find("button.sort-header").Click();
        check(context.JSInterop.Invocations.Any(call => call.Identifier == "blazorTableState.set" && call.Arguments[1]?.ToString()?.Contains("\"Sort\":\"name\"", StringComparison.Ordinal) == true), "Paged: the order chosen is written to the tab's session storage for this table");
        using var second = new BunitContext();
        second.JSInterop.Mode = JSRuntimeMode.Loose;
        second.JSInterop.Setup<string?>("blazorTableState.get", _ => true).SetResult("{\"Size\":10,\"Sort\":\"name\",\"Descending\":true}");
        var restored = second.Render<Paged<string>>(parameters => parameters.Add(p => p.Items, names).Add(p => p.Id, "o-size").Add(p => p.Sorts, sorts).Add(p => p.ChildContent, sortable));
        check(Order(restored) == "Zeta|Ștefan|Beta|alfa|", "Paged: the order kept in the tab is applied when the table is shown again");
        var wide = second.Render<Paged<int>>(parameters => parameters.Add(p => p.Items, items).Add(p => p.Id, "o2-size").Add(p => p.ChildContent, rows));
        check(wide.FindAll("p.row").Count == 10, "Paged: the page size kept in the tab is applied when the table is shown again");
        return Task.CompletedTask;
    }
}
