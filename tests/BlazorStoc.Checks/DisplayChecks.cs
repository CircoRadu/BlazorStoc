using BlazorStoc.Services;

namespace BlazorStoc.Checks;

// Display settings of a user (PROP-0034): the merge of the global and the table settings, the limits, the CSS and the JSON kept in the database.
public static class DisplayChecks
{
    public static void Run(Action<bool, string> check)
    {
        var global = new DisplaySettings(TableText: 120, HeaderText: 110, Images: 100);
        var table = new DisplaySettings(TableText: 90, Icons: 130);
        var merged = DisplayRules.Merge(global, table);
        check(merged.TableText == 90, "Display: a value set for the table wins over the global one");
        check(merged.HeaderText == 110 && merged.Images == 100, "Display: what the table leaves unset comes from the global settings");
        check(merged.Icons == 130 && merged.Titles is null, "Display: a table value without a global one stays, an unset field stays unset (default)");
        check(DisplayRules.Merge(null, null).IsEmpty && DisplayRules.Merge(global, null) == global, "Display: without table settings the global ones apply");

        var wild = DisplayRules.Normalize(new DisplaySettings(TableText: 10, HeaderText: 999, Images: 100));
        check(wild.TableText == DisplayRules.Min && wild.HeaderText == DisplayRules.Max && wild.Images == 100 && wild.Icons is null, "Display: values are kept between the limits");

        check(DisplayRules.ToStyle(null) == "" && DisplayRules.ToStyle(new()) == "", "Display: no settings, no style (the stylesheet default stays)");
        check(DisplayRules.ToStyle(new DisplaySettings(TableText: 120, Icons: 90)) == "--ui-table:1.2;--ui-icons:0.9", "Display: the style has one scale factor per set field, with a dot");
        check(DisplayRules.ToStyle(DisplayRules.Preset(100)).Split(';').Length == 6, "Display: a preset sets all six fields");

        var json = DisplayRules.ToJson(global);
        check(DisplayRules.FromJson(json) == global, "Display: the JSON kept in the database reads back the same settings");
        check(DisplayRules.FromJson("not json").IsEmpty && DisplayRules.FromJson("").IsEmpty && DisplayRules.FromJson(null).IsEmpty, "Display: a damaged or empty text means no settings");
        check(DisplayRules.FromJson("{\"TableText\":5000}").TableText == DisplayRules.Max, "Display: a hand-edited value is brought back between the limits");

        check(DisplayRules.IsValidScope("produse.catalog") && DisplayRules.IsValidScope("global") && !DisplayRules.IsValidScope("") && !DisplayRules.IsValidScope("a b") && !DisplayRules.IsValidScope(new string('a', 81)), "Display: a scope is a short id of letters, digits, dot, dash or underscore");

        var data = new UserDisplayData(global, new Dictionary<string, DisplaySettings> { [DisplayRules.ProductsCatalogScope] = table });
        check(data.For(DisplayRules.ProductsCatalogScope).TableText == 90 && data.For("alt.tabel").TableText == 120, "Display: a table without settings of its own uses the global ones");
    }
}
