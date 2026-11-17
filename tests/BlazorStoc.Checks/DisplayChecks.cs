using BlazorStoc.Services;

namespace BlazorStoc.Checks;

// Display settings of a user (PROP-0034): the merge of the global and the table settings, the limits, the CSS and the JSON kept in the database,
// and the registry of global UI classes in wwwroot/app.css through which every scale reaches the interface.
public static class DisplayChecks
{
    public static void Run(Action<bool, string> check)
    {
        var global = new DisplaySettings(TableText: 120, HeaderText: 110, Images: 100);
        var table = new DisplaySettings(TableText: 150, Icons: 130);
        var merged = DisplayRules.Merge(global, table);
        check(merged.TableText == 150, "Display: a value set for the table wins over the global one");
        check(merged.HeaderText == 110 && merged.Images == 100, "Display: what the table leaves unset comes from the global settings");
        check(merged.Icons == 130 && merged.Titles is null, "Display: a table value without a global one stays, an unset field stays unset (default)");
        check(DisplayRules.Merge(null, null).IsEmpty && DisplayRules.Merge(global, null) == global, "Display: without table settings the global ones apply");
        check(DisplayRules.Merge(new DisplaySettings(Buttons: 150, Dialogs: 200), new DisplaySettings(Fields: 125)) is { Buttons: 150, Dialogs: 200, Fields: 125 }, "Display: the buttons, fields, top bar and dialogs scales merge like the others");

        var wild = DisplayRules.Normalize(new DisplaySettings(TableText: 10, HeaderText: 999, Images: 100));
        check(wild.TableText == DisplayRules.Min && wild.HeaderText == DisplayRules.Max && wild.Images == 100 && wild.Icons is null, "Display: values are kept between the limits");
        check(DisplayRules.Min == 100 && DisplayRules.Max == 500 && DisplayRules.Steps[0] == 100 && DisplayRules.Steps[^1] == 500 && DisplayRules.Presets.All(preset => DisplayRules.Steps.Contains(preset.Percent)), "Display: the scales run from 100% to 500% and every preset is one of the steps");
        check(DisplayRules.Normalize(new DisplaySettings(Images: 300, TableText: 999)) is { Images: 300, TableText: 500 }, "Display: every field accepts values up to 500% and is brought back to the limit above it");

        check(DisplayRules.ToStyle(null) == "" && DisplayRules.ToStyle(new()) == "", "Display: no settings, no style (the stylesheet default stays)");
        check(DisplayRules.ToStyle(new DisplaySettings(TableText: 120, Icons: 150)) == "--ui-table:1.2;--ui-icons:1.5", "Display: the style has one scale factor per set field, with a dot");
        check(DisplayRules.ToStyle(new DisplaySettings(Menu: 150, Text: 125)) == "--ui-menu:1.5;--ui-text:1.25", "Display: the menu and text scales have their own factors");
        check(DisplayRules.ToStyle(new DisplaySettings(Buttons: 110, Fields: 120, Topbar: 130, Dialogs: 140)) == "--ui-buttons:1.1;--ui-fields:1.2;--ui-topbar:1.3;--ui-dialogs:1.4", "Display: buttons, fields, top bar and dialogs have their own factors");
        check(DisplayRules.ToStyle(DisplayRules.Preset(100)).Split(';').Length == 12 && !new DisplaySettings(Dialogs: 100).IsEmpty, "Display: a preset sets all twelve fields");
        check(DisplayRules.ToStyle(new DisplaySettings(Images: 250)) == "--ui-images:2.5" && DisplayRules.FromJson(DisplayRules.ToJson(new DisplaySettings(Images: 250))).Images == 250, "Display: a step above 100% reaches the style and the database unchanged");

        var json = DisplayRules.ToJson(global);
        check(DisplayRules.FromJson(json) == global, "Display: the JSON kept in the database reads back the same settings");
        check(DisplayRules.FromJson("not json").IsEmpty && DisplayRules.FromJson("").IsEmpty && DisplayRules.FromJson(null).IsEmpty, "Display: a damaged or empty text means no settings");
        check(DisplayRules.FromJson("{\"TableText\":5000}").TableText == DisplayRules.Max, "Display: a hand-edited value is brought back between the limits");

        check(DisplayRules.IsValidScope("produse.catalog") && DisplayRules.IsValidScope("global") && DisplayRules.IsValidScope("t.jurnal.istoric-operatii") && !DisplayRules.IsValidScope("") && !DisplayRules.IsValidScope("a b") && !DisplayRules.IsValidScope(new string('a', 81)), "Display: a scope is a short id of letters, digits, dot, dash or underscore");

        var data = new UserDisplayData(global, new Dictionary<string, DisplaySettings> { [DisplayRules.ProductsCatalogScope] = table });
        check(data.For(DisplayRules.ProductsCatalogScope).TableText == 150 && data.For("alt.tabel").TableText == 120, "Display: a table without settings of its own uses the global ones");

        // The registry: every scale the settings can emit is applied by a rule of the stylesheet, and every kind of element has its global class (new elements only need the class).
        var css = StyleSheet();
        if (css is null) { check(true, "Display: the stylesheet was not found next to the tests (registry check skipped)"); return; }
        foreach (var name in new[] { "titles", "text", "tabs", "table", "header", "images", "icons", "buttons", "fields", "topbar", "dialogs", "menu" })
            check(css.Contains($"var(--ui-{name},", StringComparison.Ordinal), $"Display: the stylesheet applies the scale --ui-{name}");
        foreach (var cssClass in new[] { "ui-scale-title", "ui-scale-text", "ui-scale-tab", "ui-scale-cell", "ui-scale-header", "ui-scale-button", "ui-scale-field", "ui-scale-topbar", "ui-scale-dialog", "ui-scale-menu", "ui-scale-image", "ui-scale-icon" })
            check(css.Contains("." + cssClass, StringComparison.Ordinal), $"Display: the global class .{cssClass} exists in the registry of the stylesheet");
    }

    private static string? StyleSheet()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "wwwroot", "app.css");
            if (File.Exists(path) && File.Exists(Path.Combine(directory.FullName, "BlazorStoc.csproj"))) return File.ReadAllText(path);
        }
        return null;
    }
}