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
        return Task.CompletedTask;
    }
}
