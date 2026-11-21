using static BlazorStoc.Services.MariaDb;

namespace BlazorStoc.Services;

/// <summary>What a tile of the main page is: an entry of the main menu (a page) or a main action of a category (a dialog that opens on a page, for example "Adaugă produs").</summary>
public enum MenuEntryKind { Menu, Action }

/// <summary>
/// One option that can become a tile of the personalised main page, with its global icon (the same icon wherever the option appears). A menu entry opens a page; an action opens a page
/// with the query "actiune=..." (or the older "adauga=1") that opens the dialog of the action. AdminOnly: the entry belongs to the administrator alone (no permission key for it).
/// </summary>
public sealed record MenuEntry(string Key, string Group, string Title, string Href, string Icon, string Permission, string ModuleId = "", MenuEntryKind Kind = MenuEntryKind.Menu, bool AdminOnly = false)
{
    /// <summary>The title of a tile: the route in the menu, for example "Produse - Stoc".</summary>
    public string Route => string.Equals(Group, Title, StringComparison.Ordinal) ? Title : $"{Group} - {Title}";
    public string Policy => "perm:" + Permission;
    public bool IsAction => Kind == MenuEntryKind.Action;
}

/// <summary>The options of the main page, each with an icon of its own: the entries of the main menu, the pages of Setări and the main actions of the categories.</summary>
public static class MenuCatalog
{
    /// <summary>The query that tells a page which action to open (for example /vehicule?actiune=adauga-vehicul).</summary>
    public const string ActionQuery = "actiune";

    public static readonly IReadOnlyList<MenuEntry> Entries =
    [
        new("produse.stoc", "Produse", "Stoc", "/produse", "▦", "stoc.view"),
        new("produse.preluare-factura", "Produse", "Preluare factură", "/produse/preluare-factura", "⇩", "preluare-factura.view", "preluare-factura"),
        new("produse.iesire-multipla", "Produse", "Ieșire multiplă", "/iesiri/multipla", "⇧", "iesire-multipla.view"),
        new("produse.export-consum", "Produse", "Export consum", "/consum", "⇪", "export-consum.view", "situatie-proiect"),
        new("produse.bon-consum", "Produse", "Generează bon de consum", "/consum/bon", "▤", "stoc.view"),
        new("produse.adauga", "Produse", "Adaugă produs", "/produse?actiune=adauga-produs", "✚", "produse.add", Kind: MenuEntryKind.Action),
        new("inventar.situatie", "Inventar", "Generare situație inventar", "/inventar", "▥", "inventar.view", "inventar"),
        new("inventar.preluare", "Inventar", "Preluare inventar", "/inventar/preluare", "✓", "inventar-preluare.view", "inventar"),
        new("inventar.ocr", "Inventar", "Învățare OCR", "/inventar/invatare-ocr", "◉", "inventar-preluare.view", "inventar"),
        new("administrare.categorii", "Administrare", "Categorii", "/categorii", "☰", "categorii.view"),
        new("administrare.beneficiari", "Administrare", "Beneficiari", "/beneficiari", "♧", "beneficiari.view"),
        new("administrare.bonuri-consum", "Administrare", "Bonuri de consum", "/bonuri-consum", "▧", "stoc.view"),
        new("administrare.furnizori", "Administrare", "Furnizori", "/furnizori", "⌂", "furnizori.view", "furnizori"),
        new("administrare.facturi", "Administrare", "Facturi", "/facturi", "▨", "facturi.view", "furnizori"),
        new("administrare.vehicule", "Administrare", "Vehicule", "/vehicule", "⛟", "vehicule.view", "vehicule"),
        new("administrare.taskuri", "Administrare", "Taskuri utilizator", "/taskuri/adauga", "☑", "taskuri.view"),
        new("administrare.elcomtur", "Administrare", "Preluare Elcomtur", "/preluare-elcomtur", "⇲", "elcomtur.import", "elcomtur"),
        new("administrare.nomenclator", "Administrare", "Nomenclator", "/nomenclator", "≣", "nomenclator.view"),
        new("administrare.adauga-beneficiar", "Administrare", "Adaugă beneficiar", "/beneficiari?adauga=1", "✦", "beneficiari.add", Kind: MenuEntryKind.Action),
        new("administrare.adauga-furnizor", "Administrare", "Adaugă furnizor", "/furnizori?adauga=1", "❖", "furnizori.add", "furnizori", MenuEntryKind.Action),
        new("administrare.adauga-vehicul", "Administrare", "Adaugă vehicul", "/vehicule?actiune=adauga-vehicul", "◈", "vehicule.add", "vehicule", MenuEntryKind.Action),
        new("administrare.adauga-categorie", "Administrare", "Adaugă categorie", "/categorii?actiune=adauga-categorie", "◆", "categorii.add", Kind: MenuEntryKind.Action),
        new("administrare.adauga-proiect", "Administrare", "Adaugă proiect", "/beneficiari?actiune=adauga-proiect", "✱", "beneficiari.add", Kind: MenuEntryKind.Action),
        new("administrare.adauga-punct", "Administrare", "Adaugă punct de lucru", "/beneficiari?actiune=adauga-punct", "◇", "beneficiari.edit", Kind: MenuEntryKind.Action),
        new("oferte", "Oferte", "Oferte", "/oferte/preluare", "▤", "oferte.view", "oferte"),
        // An action of the category (it opens the page that takes an offer over), not a child of the menu: the menu has the single entry "Oferte". Its own icon keeps the tile recognisable.
        new("oferte.preluare", "Oferte", "Preluare ofertă", "/oferte/preluare", "⇓", "oferte.view", "oferte", MenuEntryKind.Action),
        new("mentenanta", "Mentenanță", "Mentenanță", "/mentenanta", "⚒", "mentenanta.view", "mentenanta"),
        new("mentenanta.adauga-contract", "Mentenanță", "Adaugă contract de mentenanță", "/beneficiari?actiune=adauga-contract", "✜", "mentenanta.add", "mentenanta", MenuEntryKind.Action),
        new("analize-risc", "Analize de risc", "Analize de risc", "/analize-risc", "⚠", "analize-risc.view", "analize-risc"),
        new("analize-risc.adauga-analiza", "Analize de risc", "Adaugă analiză de risc", "/beneficiari?actiune=adauga-analiza", "✧", "analize-risc.add", "analize-risc", MenuEntryKind.Action),
        new("notificari", "Notificări", "Notificări", "/notificari", "✉", "notificari.view", "notificari"),
        new("jurnal", "Jurnal activitate", "Jurnal activitate", "/jurnal", "≡", "jurnal.view", "jurnal"),
        // The pages (tabs) of Setări: the same icons as the tiles of the settings page.
        new("setari.anaf", "Setări", "ANAF", "/setari?tab=anaf", "⌕", "setari-anaf.view"),
        new("setari.preluare-activityapp", "Setări", "Preluare date ActivityApp", "/setari?tab=preluare-activityapp", "⇅", "setari-import-activityapp.view"),
        new("setari.preluare-elcomtur", "Setări", "Preluare date Elcomtur", "/setari?tab=preluare-elcomtur", "⇩", "setari-import-elcomtur.view"),
        new("setari.preluare-espstoc", "Setări", "Preluare stoc ESPStoc", "/setari?tab=preluare-espstoc", "⇆", "setari-import-espstoc.view"),
        new("setari.notificari", "Setări", "Notificări", "/setari?tab=notificari", "✉", "setari-notificari.view"),
        new("setari.harta", "Setări", "Hartă", "/setari?tab=harta", "⌖", "setari-harta.view"),
        new("setari.facturi", "Setări", "Facturi", "/setari?tab=facturi", "▤", "setari-facturi.view"),
        new("setari.backup", "Setări", "Backup", "/setari?tab=backup", "⤓", "setari-backup.view"),
        new("setari.jurnal", "Setări", "Jurnal activitate", "/setari?tab=jurnal", "≡", "setari-jurnal.view"),
        new("setari.dezvoltare", "Setări", "Dezvoltare", "/setari?tab=dezvoltare", "⚑", "setari-bug-raport.view"),
        new("setari.anunturi", "Setări", "Anunțuri", "/setari?tab=anunturi", "✉", "setari-anunturi.view"),
        new("setari.catalog", "Setări", "Catalog", "/setari?tab=catalog", "⊞", "setari-catalog", AdminOnly: true),
        new("setari.module", "Setări", "Module", "/setari?tab=module", "▦", "setari-module.view"),
        new("setari.actualizare", "Setări", "Actualizare", "/setari?tab=actualizare", "↻", "setari-actualizare.view"),
        new("setari.utilizatori", "Setări", "Utilizatori", "/setari?tab=utilizatori", "♙", "utilizatori.view"),
        new("setari.autentificare", "Setări", "Autentificare", "/setari?tab=autentificare", "⚿", "setari-autentificare", AdminOnly: true),
        new("setari.baza-de-date", "Setări", "Bază de date", "/setari?tab=baza-de-date", "▥", "setari-baza-de-date", AdminOnly: true),
    ];

    /// <summary>The tiles of the standard main page (the one a user sees until they arrange their own), in its order: the personalisation starts from them.</summary>
    public static readonly IReadOnlyList<string> StandardDashboardKeys =
    [
        "produse.stoc", "produse.preluare-factura", "produse.iesire-multipla", "produse.export-consum", "inventar.preluare", "administrare.categorii", "inventar.situatie", "administrare.beneficiari",
        "administrare.furnizori", "administrare.facturi", "oferte", "administrare.vehicule", "mentenanta", "analize-risc", "notificari", "setari.utilizatori", "administrare.nomenclator", "jurnal"
    ];

    private static readonly Dictionary<string, MenuEntry> ByKey = Entries.ToDictionary(entry => entry.Key, StringComparer.Ordinal);

    public static MenuEntry? Find(string key) => ByKey.GetValueOrDefault(key);

    /// <summary>The icon of an entry (the same one in the side menu and on the tiles of the dashboard).</summary>
    public static string IconOf(string key) => ByKey.TryGetValue(key, out var entry) ? entry.Icon : "";

    /// <summary>The categories (the Group of the entries) in the order of the main menu.</summary>
    public static IReadOnlyList<string> Groups { get; } = [.. Entries.Select(entry => entry.Group).Distinct(StringComparer.Ordinal)];
}

/// <summary>The place of a tile in the layout of the main page (row and column from 0; the row index counts every row: tiles, labels and spaces). Row -1: the tile is chosen but not placed (the buffer of the editor), Col is its order there; the main page does not show it.</summary>
public sealed record DashboardTile(string Key, int Row, int Col, int Zone = 0);

/// <summary>A zone of the main page: zones stand side by side, each with a required title and its own grid (Columns tiles per row); Index = the order from left to right.</summary>
public sealed record DashboardZone(int Index, string Title, int Columns, string Placement = DashboardZone.Beside)
{
    public const string DefaultTitle = "Dashboard";
    public const int MaxTitle = 60, MaxZones = 6;
    // Where a zone stands: beside = next to the zone before it (the first zone is always beside); center, left and right = on a new row of the page, aligned that way, as wide as its tiles need.
    public const string Beside = "beside", Center = "center", Left = "left", Right = "right";
    public static readonly IReadOnlyList<string> Placements = [Beside, Center, Left, Right];
    public static string ValidPlacement(string? value) => value is Center or Left or Right ? value : Beside;
    public static string PlacementLabel(string placement) => placement switch { Center => "Pe rând nou, centrată", Left => "Pe rând nou, la stânga", Right => "Pe rând nou, la dreapta", _ => "Lângă zona dinainte" };
}

/// <summary>The sizes the user chose for every tile and every space of the page (0 = the standard look): the widest a tile may be, its height, and the height of the spaces between rows.</summary>
public sealed record DashboardSizes(int TileWidth, int TileHeight, int RowGap)
{
    public const int MinWidth = 140, MaxWidth = 600, MinHeight = 56, MaxHeight = 240, MinGap = 5, MaxGap = 60, DefaultGap = 15;
    public static DashboardSizes Standard { get; } = new(0, 0, 0);
    public int GapOrDefault => RowGap > 0 ? RowGap : DefaultGap;
    public DashboardSizes Valid() => new(TileWidth <= 0 ? 0 : Math.Clamp(TileWidth, MinWidth, MaxWidth), TileHeight <= 0 ? 0 : Math.Clamp(TileHeight, MinHeight, MaxHeight), RowGap <= 0 ? 0 : Math.Clamp(RowGap, MinGap, MaxGap));
    public string Text => (TileWidth > 0 ? ";tw=" + TileWidth : "") + (TileHeight > 0 ? ";th=" + TileHeight : "") + (RowGap > 0 ? ";gap=" + RowGap : "");
}

/// <summary>A row of the main page that holds no tiles: a label (text with an optional colour) or a space of fixed height. Index = the place among all the rows.</summary>
public sealed record DashboardRow(int Index, string Kind, string Text, string Color, int Zone = 0)
{
    public const string Label = "label", Spacer = "spacer";
    public const int MaxText = 80;
}

/// <summary>
/// What the user arranged: how many tiles stand on a row, the tiles and the rows of labels and spaces. Custom: the user chose their own main page; with no tiles it is an empty page (their choice),
/// without Custom the user has not arranged anything and the standard main page is shown.
/// </summary>
public sealed record DashboardState(int Columns, IReadOnlyList<DashboardTile> Tiles, IReadOnlyList<DashboardRow> Rows, bool Custom = true, IReadOnlyList<DashboardZone>? Zones = null, DashboardSizes? Sizes = null)
{
    /// <summary>The zones from left to right; none saved (an arrangement from before the zones) = one zone named Dashboard with Columns tiles per row.</summary>
    public IReadOnlyList<DashboardZone> ZoneList => Zones is { Count: > 0 } ? Zones : [new DashboardZone(0, DashboardZone.DefaultTitle, Columns)];
    public DashboardSizes SizeSettings => Sizes ?? DashboardSizes.Standard;
    /// <summary>Nothing arranged: the standard main page.</summary>
    public static DashboardState Empty { get; } = new(MariaUserDashboardRepository.DefaultColumns, [], [], false);
}

/// <summary>Persistence of the main page arranged by the signed-in user (no permission key: every user manages their own page).</summary>
public interface IUserDashboardRepository
{
    /// <summary>The arrangement of the user; no tiles = the standard main page.</summary>
    Task<DashboardState> GetAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(DashboardState state, CancellationToken cancellationToken = default);
}

// Tables `user_dashboard_entries` (migrations 94 and 95: the tiles with their row and column) and `user_dashboard_rows` (migration 96: the labels and the spaces); the number of tiles on a row is kept
// with the display settings of the user (user_display_settings, scope "dashboard", text "columns=N"). Only the signed-in user's own rows are read or written.
public sealed class MariaUserDashboardRepository(IConfiguration configuration, IAccessControl? accessControl = null) : IUserDashboardRepository
{
    public const int DefaultColumns = 4, MinColumns = 2, MaxColumns = 8;
    public const int MaxRows = 60;
    private const string SettingsScope = "dashboard";

    public static int ValidColumns(int value) => Math.Clamp(value, MinColumns, MaxColumns);

    /// <summary>A colour of a label: #rrggbb, or empty for the colour of the theme.</summary>
    public static string ValidColor(string? value) => value is { Length: 7 } && value[0] == '#' && value.Skip(1).All(Uri.IsHexDigit) ? value.ToLowerInvariant() : "";

    public async Task<DashboardState> GetAsync(CancellationToken cancellationToken = default)
    {
        if (await UserIdAsync(cancellationToken).ConfigureAwait(false) is not { } userId) return DashboardState.Empty;
        await using var connection = await OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        var columns = DefaultColumns;
        var custom = false;
        int tileWidth = 0, tileHeight = 0, rowGap = 0;
        await using (var command = Command(connection, "SELECT settings FROM user_display_settings WHERE user_id=@user AND scope=@scope", ("@user", userId), ("@scope", SettingsScope)))
        {
            // "columns=N" and, once the user saved an arrangement (even an empty one), ";custom=1".
            var text = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string ?? "";
            foreach (var part in text.Split(';'))
            {
                if (part.StartsWith("columns=", StringComparison.Ordinal) && int.TryParse(part["columns=".Length..], out var value)) columns = ValidColumns(value);
                else if (part == "custom=1") custom = true;
                else if (part.StartsWith("tw=", StringComparison.Ordinal) && int.TryParse(part[3..], out var width)) tileWidth = width;
                else if (part.StartsWith("th=", StringComparison.Ordinal) && int.TryParse(part[3..], out var height)) tileHeight = height;
                else if (part.StartsWith("gap=", StringComparison.Ordinal) && int.TryParse(part[4..], out var gap)) rowGap = gap;
            }
        }
        var tiles = new List<DashboardTile>();
        await using (var command = Command(connection, "SELECT entry_key, grid_row, grid_col, zone_index FROM user_dashboard_entries WHERE user_id=@user ORDER BY zone_index, grid_row, grid_col, position, entry_key", ("@user", userId)))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) if (MenuCatalog.Find(reader.GetString(0)) is not null) tiles.Add(new DashboardTile(reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3)));
        // Tiles saved before the grid existed all sit at (0, 0): place them from their order, DefaultColumns per row.
        if (tiles.Count > 1 && tiles.Select(tile => (tile.Zone, tile.Row, tile.Col)).Distinct().Count() != tiles.Count) tiles = tiles.Select((tile, index) => tile with { Row = index / DefaultColumns, Col = index % DefaultColumns }).ToList();
        var rows = new List<DashboardRow>();
        await using (var command = Command(connection, "SELECT row_index, kind, text, color, zone_index FROM user_dashboard_rows WHERE user_id=@user ORDER BY zone_index, row_index", ("@user", userId)))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) rows.Add(new DashboardRow(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt32(4)));
        var zones = new List<DashboardZone>();
        await using (var command = Command(connection, "SELECT zone_index, title, columns_count, placement FROM user_dashboard_zones WHERE user_id=@user ORDER BY zone_index", ("@user", userId)))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) zones.Add(new DashboardZone(reader.GetInt32(0), reader.GetString(1), ValidColumns(reader.GetInt32(2)), reader.GetInt32(0) == 0 ? DashboardZone.Beside : DashboardZone.ValidPlacement(reader.GetString(3))));
        // An arrangement saved before the flag existed has tiles or rows: it is the user's own.
        return new DashboardState(columns, tiles, rows, custom || tiles.Count > 0 || rows.Count > 0, zones, new DashboardSizes(tileWidth, tileHeight, rowGap).Valid());
    }

    public async Task SaveAsync(DashboardState state, CancellationToken cancellationToken = default)
    {
        var userId = await UserIdAsync(cancellationToken).ConfigureAwait(false) ?? throw new AccessDeniedException("Dashboard-ul se personalizează numai pentru un utilizator autentificat.");
        var columns = ValidColumns(state.Columns);
        var zones = state.ZoneList.OrderBy(zone => zone.Index).Select((zone, order) => (Old: zone.Index, Zone: new DashboardZone(order, (zone.Title ?? "").Trim(), ValidColumns(zone.Columns), order == 0 ? DashboardZone.Beside : DashboardZone.ValidPlacement(zone.Placement)))).ToList();
        if (zones.Count > DashboardZone.MaxZones) throw new InvalidOperationException("Dashboard-ul poate avea cel mult " + DashboardZone.MaxZones + " zone.");
        if (state.Custom && zones.Any(item => item.Zone.Title.Length == 0 || item.Zone.Title.Length > DashboardZone.MaxTitle)) throw new InvalidOperationException("Fiecare zonă are un titlu obligatoriu (cel mult " + DashboardZone.MaxTitle + " de caractere).");
        var zoneColumns = zones.ToDictionary(item => item.Old, item => item.Zone.Columns);
        var zoneOrder = zones.ToDictionary(item => item.Old, item => item.Zone.Index);
        var tiles = state.Tiles.Where(tile => state.Custom && MenuCatalog.Find(tile.Key) is not null && (tile.Row < 0 || zoneOrder.ContainsKey(tile.Zone))).GroupBy(tile => tile.Key, StringComparer.Ordinal).Select(group => group.First())
            .Select(tile => tile.Row < 0 ? tile with { Row = -1, Zone = 0, Col = Math.Clamp(tile.Col, 0, MaxRows) } : tile with { Zone = zoneOrder[tile.Zone], Row = Math.Clamp(tile.Row, 0, MaxRows), Col = Math.Clamp(tile.Col, 0, zoneColumns[tile.Zone] - 1) }).ToList();
        if (tiles.Select(tile => (tile.Zone, tile.Row, tile.Col)).Distinct().Count() != tiles.Count) throw new InvalidOperationException("Două tile-uri nu pot ocupa aceeași celulă.");
        var rows = state.Rows.Where(row => state.Custom && row.Kind is DashboardRow.Label or DashboardRow.Spacer && row.Index is >= 0 and <= MaxRows && zoneOrder.ContainsKey(row.Zone))
            .Select(row => row with { Zone = zoneOrder[row.Zone], Text = row.Kind == DashboardRow.Label ? (row.Text ?? "").Trim()[..Math.Min((row.Text ?? "").Trim().Length, DashboardRow.MaxText)] : "", Color = row.Kind is DashboardRow.Label or DashboardRow.Spacer ? ValidColor(row.Color) : "" })
            .GroupBy(row => (row.Zone, row.Index)).Select(group => group.First()).ToList();
        if (rows.Any(row => tiles.Any(tile => tile.Zone == row.Zone && tile.Row == row.Index))) throw new InvalidOperationException("Un rând de etichetă sau de spațiu nu poate avea tile-uri.");
        var sizes = (state.Sizes ?? DashboardSizes.Standard).Valid();
        await using var connection = await OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var table in new[] { "user_dashboard_entries", "user_dashboard_rows", "user_dashboard_zones" })
            await using (var delete = Command(connection, $"DELETE FROM {table} WHERE user_id=@user", ("@user", userId))) { delete.Transaction = transaction; await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false); }
        var now = MariaTimeText.Format(MariaTimeText.Now());
        if (state.Custom)
            foreach (var (_, zone) in zones)
            {
                await using var insert = Command(connection, "INSERT INTO user_dashboard_zones (user_id, zone_index, title, columns_count, placement, updated_utc) VALUES (@user, @zone, @title, @columns, @placement, @now)",
                    ("@user", userId), ("@zone", zone.Index), ("@title", zone.Title), ("@columns", zone.Columns), ("@placement", zone.Placement), ("@now", now));
                insert.Transaction = transaction;
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        var ordered = tiles.OrderBy(tile => tile.Zone).ThenBy(tile => tile.Row).ThenBy(tile => tile.Col).ToList();
        for (var index = 0; index < ordered.Count; index++)
        {
            await using var insert = Command(connection, "INSERT INTO user_dashboard_entries (user_id, entry_key, position, grid_row, grid_col, zone_index, updated_utc) VALUES (@user, @key, @position, @row, @col, @zone, @now)",
                ("@user", userId), ("@key", ordered[index].Key), ("@position", index), ("@row", ordered[index].Row), ("@col", ordered[index].Col), ("@zone", ordered[index].Zone), ("@now", now));
            insert.Transaction = transaction;
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        foreach (var row in rows)
        {
            await using var insert = Command(connection, "INSERT INTO user_dashboard_rows (user_id, row_index, zone_index, kind, text, color, updated_utc) VALUES (@user, @row, @zone, @kind, @text, @color, @now)",
                ("@user", userId), ("@row", row.Index), ("@zone", row.Zone), ("@kind", row.Kind), ("@text", row.Text), ("@color", row.Color), ("@now", now));
            insert.Transaction = transaction;
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await using (var setting = Command(connection, "INSERT INTO user_display_settings (user_id, scope, settings, updated_utc) VALUES (@user, @scope, @settings, @now) ON DUPLICATE KEY UPDATE settings=@settings, updated_utc=@now",
            ("@user", userId), ("@scope", SettingsScope), ("@settings", "columns=" + columns + (state.Custom ? ";custom=1" : "") + sizes.Text), ("@now", now)))
        {
            setting.Transaction = transaction;
            await setting.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<int?> UserIdAsync(CancellationToken token) => accessControl is null ? null : await accessControl.GetUserIdAsync(token).ConfigureAwait(false);
}
