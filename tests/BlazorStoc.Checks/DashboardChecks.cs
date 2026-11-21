using BlazorStoc.Services;

namespace BlazorStoc.Checks;

// The personalised main page without a server: the zones side by side (required titles, own grid), the rows, the tiles, the buffer, the sizes of the sliders and the state that is saved.
public static class DashboardChecks
{
    public static Task RunAsync(Action<bool, string> check)
    {
        Console.WriteLine("=== Dashboard ===");
        var keys = MenuCatalog.Entries.Take(8).Select(entry => entry.Key).ToArray();

        // A page saved before the zones is one zone named Dashboard with its tiles per row, rows and tiles where they were.
        var legacy = DashboardLayout.From(new DashboardState(3, [new DashboardTile(keys[0], 0, 0), new DashboardTile(keys[1], 0, 2), new DashboardTile(keys[2], 2, 1)], [new DashboardRow(1, DashboardRow.Label, "Titlu", "#112233")]));
        check(legacy.Zones.Count == 1 && legacy.Zones[0].Title == DashboardZone.DefaultTitle && legacy.Zones[0].Columns == 3 && legacy.Zones[0].Rows.Count == 3 && legacy.KeyAt(0, 0, 2) == keys[1] && legacy.KeyAt(0, 2, 1) == keys[2] && legacy.Zones[0].Rows[1].Text == "Titlu",
            "Dashboard zones: an arrangement saved before the zones is one zone named Dashboard with the same tiles, rows and columns");

        // Two zones, each with its own columns and rows; the tiles of a zone stay in it.
        var layout = DashboardLayout.From(DashboardState.Empty);
        layout.Zones[0].Title = "Vanzari";
        // Zones on several rows: a zone placed on a new row starts a band of its own, aligned centre, left or right; the first zone is always beside.
        var placed = DashboardLayout.From(DashboardState.Empty);
        placed.AddZone("B"); placed.AddZone("C"); placed.AddZone("D");
        check(placed.SetPlacement(2, DashboardZone.Center) && placed.SetPlacement(0, DashboardZone.Right) && placed.Zones[0].Placement == DashboardZone.Beside && !placed.SetPlacement(9, DashboardZone.Left), "Dashboard zones: the first zone is always beside and a zone that does not exist is refused");
        var bands = placed.Bands();
        check(bands.Count == 2 && bands[0].SequenceEqual([0, 1]) && bands[1].SequenceEqual([2, 3]) && placed.BandAlign(bands[0]) == DashboardZone.Beside && placed.BandAlign(bands[1]) == DashboardZone.Center, "Dashboard zones: a zone on a new row starts a band that the zones beside it follow");
        placed.SetPlacement(3, DashboardZone.Left); placed.SetPlacement(1, "oriunde");
        check(placed.Bands().Count == 3 && placed.Zones[1].Placement == DashboardZone.Beside && placed.BandAlign(placed.Bands()[2]) == DashboardZone.Left, "Dashboard zones: an unknown placement means beside; every zone on a new row has its own band");
        var roundTrip = DashboardLayout.From(placed.ToState());
        check(roundTrip.Zones.Select(zone => zone.Placement).SequenceEqual([DashboardZone.Beside, DashboardZone.Beside, DashboardZone.Center, DashboardZone.Left]), "Dashboard zones: the placement of every zone is kept in the state that is saved");
        // The width of the zones: one for each zone over the global one; a zone without its own width follows the global width; none = automatic.
        var widths = DashboardLayout.From(DashboardState.Empty);
        widths.AddZone("B");
        check(widths.EffectiveWidth(widths.Zones[0]) == 0 && widths.BandAlign(widths.Bands()[0]) == DashboardZone.Beside, "Dashboard zones: with no width chosen the zones are automatic and the first row fills the page");
        widths.Sizes = widths.Sizes with { ZoneWidth = 700 };
        check(widths.EffectiveWidth(widths.Zones[0]) == 700 && widths.EffectiveWidth(widths.Zones[1]) == 700 && widths.BandAlign(widths.Bands()[0]) == DashboardZone.Center, "Dashboard zones: the global width reaches every zone that has no width of its own, and the row is centred");
        check(widths.SetZoneWidth(1, 520) && widths.EffectiveWidth(widths.Zones[1]) == 520 && widths.EffectiveWidth(widths.Zones[0]) == 700 && !widths.SetZoneWidth(7, 500), "Dashboard zones: the width of one zone overrides the global width and only that zone changes");
        check(widths.SetZoneWidth(1, 10) && widths.Zones[1].Width == DashboardZone.MinWidth && widths.SetZoneWidth(1, 99999) && widths.Zones[1].Width == DashboardZone.MaxWidth && widths.SetZoneWidth(1, 0) && widths.EffectiveWidth(widths.Zones[1]) == 700, "Dashboard zones: a width is kept between the minimum and the maximum and 0 gives the zone back to the global width");
        widths.SetZoneWidth(0, 640); widths.Sizes = widths.Sizes with { ZoneWidth = 0 };
        var widthsBack = DashboardLayout.From(widths.ToState());
        check(widthsBack.Zones[0].Width == 640 && widthsBack.Zones[1].Width == 0 && widthsBack.EffectiveWidth(widthsBack.Zones[1]) == 0 && new DashboardSizes(0, 0, 0, 800).Text.Contains(";zw=800") && new DashboardSizes(0, 0, 0, 50).Valid().ZoneWidth == DashboardZone.MinWidth, "Dashboard zones: the widths are kept in the state that is saved, the global one in the sizes text");
        check(DashboardLayout.From(new DashboardState(4, [], [], true, [new DashboardZone(0, "A", 4), new DashboardZone(1, "B", 4)])).Zones[1].Placement == DashboardZone.Beside, "Dashboard zones: an arrangement saved before the placement has every zone beside");
        check(layout.AddZone("Stoc") && layout.Zones.Count == 2 && layout.Zones[1].Title == "Stoc", "Dashboard zones: a zone is added at the end with its title");
        layout.SetColumns(1, 2);
        check(layout.Zones[0].Columns == 4 && layout.Zones[1].Columns == 2, "Dashboard zones: every zone has its own number of tiles per row");
        layout.AddRow(0, DashboardLayout.TilesKind, 0);
        layout.AddRow(1, DashboardLayout.TilesKind, 0);
        layout.AddTile(keys[0]); layout.AddTile(keys[1]); layout.AddTile(keys[2]);
        check(layout.Buffer.Count == 3 && layout.MoveTile(keys[0], 0, 0, 3) && layout.KeyAt(0, 0, 3) == keys[0] && layout.Buffer.Count == 2, "Dashboard zones: a tile of the buffer goes to a cell of the first zone");
        check(layout.MoveTile(keys[1], 1, 0, 1) && layout.KeyAt(1, 0, 1) == keys[1], "Dashboard zones: a tile goes to a cell of another zone");
        check(!layout.MoveTile(keys[2], 1, 0, 2) && layout.KeyAt(1, 0, 2) is null, "Dashboard zones: a cell beyond the columns of the zone does not exist");
        check(layout.MoveTile(keys[0], 1, 0, 1) && layout.KeyAt(1, 0, 1) == keys[0] && layout.KeyAt(0, 0, 3) == keys[1], "Dashboard zones: a tile dropped on a tile of another zone changes places with it");
        check(layout.Find(keys[0]) == (1, 0, 1) && layout.GridKeys.Count() == 2, "Dashboard zones: a tile is in one place only");
        check(layout.MoveTile(keys[0], -1, -1, 0) && layout.Buffer.Contains(keys[0]) && layout.Find(keys[0]) is null, "Dashboard zones: a tile dropped on the buffer waits there");

        // Rows move inside a zone and to another zone (fitted to the columns of the target).
        layout.MoveTile(keys[0], 0, 0, 0);
        layout.AddRow(0, DashboardRow.Label, 1);
        layout.AddRow(0, DashboardRow.Spacer, 2);
        check(layout.Zones[0].Rows.Count == 3 && layout.MoveRow(0, 2, 0) && layout.Zones[0].Rows[0].Kind == DashboardRow.Spacer, "Dashboard zones: a row moves to another place of its zone");
        layout.MoveRow(0, 0, 2);
        layout.MoveTile(keys[2], 0, 0, 3);
        check(layout.MoveRowToZone(0, 0, 1, 1) && layout.Zones[0].Rows.Count == 2 && layout.Zones[1].Rows.Count == 2 && layout.Zones[1].Rows[1].Cells.Length == 2, "Dashboard zones: a row moves to another zone and takes the columns of that zone");
        check(layout.Keys.Count() == 3 && layout.Keys.Distinct().Count() == 3, "Dashboard zones: no tile is lost when a row with tiles changes zone");
        check(!layout.MoveRowToZone(0, 9, 1, 0) && !layout.MoveRowToZone(5, 0, 1, 0), "Dashboard zones: a row that does not exist does not move");

        // Zones: order, deletion (tiles to the buffer), at least one zone, at most MaxZones.
        var before = layout.Zones.Select(zone => zone.Title).ToList();
        check(layout.MoveZone(1, 0) && layout.Zones[0].Title == before[1] && layout.Zones[1].Title == before[0], "Dashboard zones: a zone moves to another place, with its rows and tiles");
        var tilesInZone = layout.Zones[0].Rows.Where(row => row.IsTiles).SelectMany(row => row.Cells).OfType<string>().Count();
        var waiting = layout.Buffer.Count;
        check(layout.RemoveZone(0) && layout.Zones.Count == 1 && layout.Buffer.Count == waiting + tilesInZone, "Dashboard zones: a deleted zone sends its tiles to the buffer");
        check(!layout.RemoveZone(0) && layout.Zones.Count == 1, "Dashboard zones: the last zone is not deleted");
        while (layout.Zones.Count < DashboardZone.MaxZones) layout.AddZone("Zona " + layout.Zones.Count);
        check(!layout.AddZone("In plus") && layout.Zones.Count == DashboardZone.MaxZones, "Dashboard zones: a page has at most " + DashboardZone.MaxZones + " zones");

        // The title is required.
        var untitled = DashboardLayout.From(DashboardState.Empty);
        untitled.SetZoneTitle(0, "   ");
        check(untitled.Validate() is { Length: > 0 }, "Dashboard zones: a zone without a title is refused");
        untitled.SetZoneTitle(0, new string('x', DashboardZone.MaxTitle + 1));
        check(untitled.Validate() is { Length: > 0 }, "Dashboard zones: a title longer than the limit is refused");
        untitled.SetZoneTitle(0, "  Acasa  ");
        check(untitled.Validate() is null && untitled.ToState().ZoneList[0].Title == "Acasa", "Dashboard zones: a title is trimmed and accepted");

        // Fewer columns: the tiles of the cells that disappear go to free cells of the same zone.
        var narrow = DashboardLayout.From(DashboardState.Empty);
        narrow.AddZone("B");
        narrow.AddRow(0, DashboardLayout.TilesKind, 0);
        foreach (var key in keys.Take(4)) narrow.AddTile(key);
        for (var col = 0; col < 4; col++) narrow.MoveTile(keys[col], 0, 0, col);
        narrow.SetColumns(0, 2);
        check(narrow.Zones[0].Columns == 2 && narrow.Zones[0].Rows.Count == 2 && narrow.Keys.Count() == 4 && narrow.Zones[1].Rows.Count == 0, "Dashboard zones: fewer columns keep the tiles in the same zone, on new rows");

        // What is saved: zones with their order, tiles with their zone, the buffer as row -1, the empty rows at the end of a zone dropped, and the sizes.
        var saved = new DashboardLayout();
        saved.Zones.Add(new DashboardLayout.LayoutZone { Title = "Unu", Columns = 3 });
        saved.Zones.Add(new DashboardLayout.LayoutZone { Title = "Doi", Columns = 2 });
        saved.AddRow(0, DashboardLayout.TilesKind, 0); saved.AddRow(0, DashboardRow.Spacer, 1); saved.AddRow(0, DashboardLayout.TilesKind, 2);
        saved.AddRow(1, DashboardLayout.TilesKind, 0);
        saved.AddTile(keys[0]); saved.AddTile(keys[1]); saved.AddTile(keys[2]);
        saved.MoveTile(keys[0], 0, 0, 1); saved.MoveTile(keys[1], 1, 0, 0);
        saved.Sizes = new DashboardSizes(300, 100, 30);
        var state = saved.ToState();
        check(state.ZoneList.Count == 2 && state.ZoneList[1].Title == "Doi" && state.Tiles.Any(tile => tile.Key == keys[0] && tile.Zone == 0 && tile.Row == 0 && tile.Col == 1) && state.Tiles.Any(tile => tile.Key == keys[1] && tile.Zone == 1 && tile.Row == 0 && tile.Col == 0) &&
              state.Tiles.Any(tile => tile.Key == keys[2] && tile.Row == -1) && state.Rows.Count == 1 && state.Rows[0].Zone == 0 && state.Rows[0].Index == 1 && state.SizeSettings == new DashboardSizes(300, 100, 30),
            "Dashboard zones: the state to save has the zones, the tiles with their zone, the buffer, the rows and the sizes");
        var back = DashboardLayout.From(state);
        check(back.Zones.Count == 2 && back.KeyAt(0, 0, 1) == keys[0] && back.KeyAt(1, 0, 0) == keys[1] && back.Buffer.SequenceEqual([keys[2]]) && back.Zones[0].Rows.Count == 2 && back.Sizes == new DashboardSizes(300, 100, 30),
            "Dashboard zones: the saved state gives the same arrangement back");

        // Sizes: 0 = the standard look; others are held between their limits; the text kept with the display settings has only the chosen ones.
        var valid = new DashboardSizes(5000, 10, 500).Valid();
        check(valid == new DashboardSizes(DashboardSizes.MaxWidth, DashboardSizes.MinHeight, DashboardSizes.MaxGap) && DashboardSizes.Standard.Valid() == DashboardSizes.Standard && DashboardSizes.Standard.GapOrDefault == DashboardSizes.DefaultGap && DashboardSizes.Standard.Text.Length == 0,
            "Dashboard sizes: held between the limits of the sliders, 0 is the standard look and the standard gap is 15 px");

        // The stylesheet and the pages: the frame with rounded corners and 3D shadow of the zones, the shadow of the tiles, the sliders.
        var root = FindRoot();
        if (root is null) { check(false, "Dashboard zones: the project folder is found"); return Task.CompletedTask; }
        var css = File.ReadAllText(Path.Combine(root, "wwwroot", "app.css"));
        check(css.Contains(".dash-zone, .dashboard-zonebox {") && css.Contains("border-radius:18px") && css.Contains(".dash-zone .main-menu-grid > a") && css.Contains("box-shadow") && css.Contains("--dash-th") && css.Contains("--dash-gap") && css.Contains("flex-wrap:wrap; align-items:stretch") && css.Contains(".dash-tile-head") && css.Contains(".dash-tile-body"),
            "Dashboard zones: the stylesheet frames every zone (rounded corners, 3D shadow), gives the tiles a shadow and reads the sizes of the sliders");
        var page = File.ReadAllText(Path.Combine(root, "Components", "Pages", "Dashboard.razor"));
        var editor = File.ReadAllText(Path.Combine(root, "Components", "Pages", "DashboardSettingsPage.razor"));
        check(page.Contains("dash-zone-title") && page.Contains("dash-tile-head") && page.Contains("Category=\"@entry.Group\"") && page.Contains("var(--dash-tw, 1fr)") && editor.Contains("data-zone-handle") && editor.Contains("type=\"range\"") && editor.Contains("dashboard-zone-title") && editor.Contains("required"),
            "Dashboard zones: the main page shows the title of every zone and the editor has the zone handle, the required title and the sliders");
        // The dashboard inherits the display settings: its band follows the header scale and the icon scale, its text the text scale, and it has the gear of the tables (text, header, icons).
        var gearScript = File.ReadAllText(Path.Combine(root, "wwwroot", "display-settings.js"));
        var gearHost = File.ReadAllText(Path.Combine(root, "Components", "Shared", "TableGearHost.razor"));
        check(css.Contains("zoom:var(--ui-header, 1)") && css.Contains("var(--ui-icons, 1) / var(--ui-header, 1)") && page.Contains("data-gear-target") && gearScript.Contains("[data-gear-target]") && gearHost.Contains("IsDashboard") && gearHost.Contains("table.Text"),
            "Dashboard display settings: the band of a tile follows the header and icon scales, the dashboard has the gear of the tables with text, header and icons");
        var script = File.ReadAllText(Path.Combine(root, "wwwroot", "dashboard-dnd.js"));
        check(script.Contains("DropZone") && script.Contains("DropRow") && script.Contains("data-zonebox"), "Dashboard zones: the drag and drop script moves tiles, rows and zones");
        return Task.CompletedTask;
    }

    private static string? FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "BlazorStoc.csproj"))) return directory.FullName;
        return null;
    }
}
