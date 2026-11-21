namespace BlazorStoc.Services;

/// <summary>
/// The arrangement of the personalised main page while the user edits it: zones side by side (each with a required title, its own number of tiles per row and its own rows), and in each zone an
/// ordered list of rows, each a row of tiles (as many cells as there are tiles per row), a label or a space. The logic of the editor (add, delete, move, swap, change the number of tiles per row,
/// zones, sizes) is here, apart from the page, so that it can be tested. The rows are kept in order; the row index is the place of the row among the rows of its zone, and that is what is saved.
/// An option that was just switched on goes to the buffer (chosen, not placed yet; common to all zones); from there the user drags it to a cell. The tiles of the buffer are saved with row -1
/// (their order in the column) and are not shown on the main page.
/// </summary>
public sealed class DashboardLayout
{
    public const string TilesKind = "tiles";

    public sealed class LayoutRow
    {
        public string Kind { get; init; } = TilesKind;
        public string Text { get; set; } = "";
        public string Color { get; set; } = "";
        /// <summary>The keys of the tiles by column (null = free cell); only for a row of tiles.</summary>
        public string?[] Cells { get; set; } = [];
        public bool IsTiles => Kind == TilesKind;
        public bool IsEmpty => IsTiles && Cells.All(cell => cell is null);
    }

    public sealed class LayoutZone
    {
        public string Title { get; set; } = "";
        public int Columns { get; set; } = MariaUserDashboardRepository.DefaultColumns;
        public string Placement { get; set; } = DashboardZone.Beside;
        /// <summary>The width chosen for this zone in pixels; 0 = the global width of the zones.</summary>
        public int Width { get; set; }
        public List<LayoutRow> Rows { get; } = [];
        public bool HasContent => Rows.Any(row => !row.IsEmpty && row.IsTiles || !row.IsTiles && (row.Kind == DashboardRow.Spacer || row.Text.Length > 0));
        public bool IsEmptyOfTiles => Rows.All(row => !row.IsTiles || row.IsEmpty);
    }

    public List<LayoutZone> Zones { get; } = [];
    /// <summary>The tiles that are chosen but not placed on a cell yet, in the order they were chosen.</summary>
    public List<string> Buffer { get; } = [];
    public DashboardSizes Sizes { get; set; } = DashboardSizes.Standard;

    /// <summary>The number of tiles per row of the first zone (the only one of an arrangement made before the zones).</summary>
    public int Columns => Zones.Count > 0 ? Zones[0].Columns : MariaUserDashboardRepository.DefaultColumns;

    /// <summary>The rows of the first zone (a page with one zone, the common case of the standard arrangement).</summary>
    public List<LayoutRow> Rows => Zones[0].Rows;

    public static DashboardLayout From(DashboardState state)
    {
        var layout = new DashboardLayout { Sizes = state.SizeSettings.Valid() };
        foreach (var zone in state.ZoneList.OrderBy(zone => zone.Index).Take(DashboardZone.MaxZones))
            layout.Zones.Add(new LayoutZone { Title = zone.Title, Columns = MariaUserDashboardRepository.ValidColumns(zone.Columns), Placement = DashboardZone.ValidPlacement(zone.Placement), Width = DashboardZone.ValidWidth(zone.Width) });
        var known = state.ZoneList.OrderBy(zone => zone.Index).Take(DashboardZone.MaxZones).Select(zone => zone.Index).ToList();
        int ZoneOf(int saved) { var at = known.IndexOf(saved); return at >= 0 ? at : 0; }
        for (var z = 0; z < layout.Zones.Count; z++)
        {
            var zone = layout.Zones[z];
            var saved = known[z];
            var tiles = state.Tiles.Where(tile => tile.Row >= 0 && ZoneOf(tile.Zone) == z && (tile.Zone == saved || known.IndexOf(tile.Zone) < 0)).ToList();
            var rows = state.Rows.Where(row => ZoneOf(row.Zone) == z && (row.Zone == saved || known.IndexOf(row.Zone) < 0)).ToList();
            var last = Math.Max(tiles.Select(tile => tile.Row).DefaultIfEmpty(-1).Max(), rows.Select(row => row.Index).DefaultIfEmpty(-1).Max());
            for (var index = 0; index <= last && index <= MariaUserDashboardRepository.MaxRows; index++)
            {
                var special = rows.FirstOrDefault(row => row.Index == index);
                zone.Rows.Add(special is null ? layout.NewTilesRow(zone) : new LayoutRow { Kind = special.Kind, Text = special.Text, Color = special.Color });
            }
            foreach (var tile in tiles.OrderBy(tile => tile.Row).ThenBy(tile => tile.Col))
            {
                if (layout.Contains(tile.Key)) continue;
                if (tile.Row >= zone.Rows.Count || !zone.Rows[tile.Row].IsTiles) { layout.PlaceFrom(tile.Key, z, tile.Row); continue; }
                var cells = zone.Rows[tile.Row].Cells;
                if (tile.Col >= 0 && tile.Col < cells.Length && cells[tile.Col] is null) cells[tile.Col] = tile.Key; else layout.PlaceFrom(tile.Key, z, tile.Row);
            }
        }
        foreach (var tile in state.Tiles.Where(tile => tile.Row < 0).OrderBy(tile => tile.Col)) if (!layout.Contains(tile.Key)) layout.Buffer.Add(tile.Key);
        return layout;
    }

    /// <summary>The title of every zone is filled in (required); the message of the first problem, or null.</summary>
    public string? Validate()
    {
        if (Zones.Count == 0) return "Dashboard-ul are nevoie de cel puțin o zonă.";
        for (var z = 0; z < Zones.Count; z++)
        {
            var title = Zones[z].Title.Trim();
            if (title.Length == 0) return $"Zona {z + 1} nu are titlu; titlul fiecărei zone este obligatoriu.";
            if (title.Length > DashboardZone.MaxTitle) return $"Titlul zonei {z + 1} are cel mult {DashboardZone.MaxTitle} de caractere.";
        }
        return null;
    }

    /// <summary>What is saved: the zones, the tiles with their zone, row and column, and the rows of labels and spaces; the empty rows of tiles at the end of a zone are dropped.</summary>
    public DashboardState ToState()
    {
        var tiles = new List<DashboardTile>();
        var rows = new List<DashboardRow>();
        var zones = new List<DashboardZone>();
        for (var z = 0; z < Zones.Count; z++)
        {
            var zone = Zones[z];
            zones.Add(new DashboardZone(z, zone.Title.Trim(), zone.Columns, z == 0 ? DashboardZone.Beside : DashboardZone.ValidPlacement(zone.Placement), DashboardZone.ValidWidth(zone.Width)));
            var count = zone.Rows.Count;
            while (count > 0 && zone.Rows[count - 1].IsEmpty) count--;
            for (var index = 0; index < count; index++)
            {
                var row = zone.Rows[index];
                if (!row.IsTiles) { rows.Add(new DashboardRow(index, row.Kind, row.Text, row.Color, z)); continue; }
                for (var col = 0; col < row.Cells.Length; col++) if (row.Cells[col] is { } key) tiles.Add(new DashboardTile(key, index, col, z));
            }
        }
        for (var index = 0; index < Buffer.Count; index++) tiles.Add(new DashboardTile(Buffer[index], -1, index));
        return new DashboardState(Columns, tiles, rows, true, zones, Sizes);
    }

    /// <summary>The tiles placed on cells (the ones the main page shows).</summary>
    public IEnumerable<string> GridKeys => Zones.SelectMany(zone => zone.Rows).Where(row => row.IsTiles).SelectMany(row => row.Cells).OfType<string>();

    /// <summary>Every tile chosen: placed or waiting in the buffer.</summary>
    public IEnumerable<string> Keys => GridKeys.Concat(Buffer);

    public bool InBuffer(string key) => Buffer.Contains(key);

    public bool Contains(string key) => Find(key) is not null || Buffer.Contains(key);

    public (int Zone, int Row, int Col)? Find(string key)
    {
        for (var z = 0; z < Zones.Count; z++)
            for (var row = 0; row < Zones[z].Rows.Count; row++)
            {
                if (!Zones[z].Rows[row].IsTiles) continue;
                var col = Array.IndexOf(Zones[z].Rows[row].Cells, key);
                if (col >= 0) return (z, row, col);
            }
        return null;
    }

    public string? KeyAt(int zone, int row, int col) => InZone(zone) && row >= 0 && row < Zones[zone].Rows.Count && Zones[zone].Rows[row].IsTiles && col >= 0 && col < Zones[zone].Rows[row].Cells.Length ? Zones[zone].Rows[row].Cells[col] : null;

    /// <summary>A tile chosen on the list of options goes to the buffer; the user drags it to a cell from there.</summary>
    public bool AddTile(string key)
    {
        if (Contains(key)) return false;
        Buffer.Add(key);
        return true;
    }

    /// <summary>A tile placed on the first free cell of the rows of tiles of the first zone (a new row of tiles when none is free): the tiles of the standard page the editor starts from.</summary>
    public bool PlaceTile(string key)
    {
        if (Contains(key)) return false;
        return PlaceFrom(key, 0, 0);
    }

    public bool RemoveTile(string key)
    {
        if (Buffer.Remove(key)) return true;
        if (Find(key) is not { } place) return false;
        Zones[place.Zone].Rows[place.Row].Cells[place.Col] = null;
        return true;
    }

    /// <summary>A placed tile goes back to the buffer.</summary>
    public bool MoveToBuffer(string key)
    {
        if (Find(key) is not { } place) return false;
        Zones[place.Zone].Rows[place.Row].Cells[place.Col] = null;
        Buffer.Add(key);
        return true;
    }

    /// <summary>
    /// Moves a tile to a cell of a row of tiles of a zone; on a taken cell the two tiles change places (a tile that comes from the buffer sends the one it replaces to the buffer). A negative zone or
    /// row is the buffer: the tile goes back there.
    /// </summary>
    public bool MoveTile(string key, int zone, int row, int col)
    {
        if (zone < 0 || row < 0) return MoveToBuffer(key);
        if (!InZone(zone) || row >= Zones[zone].Rows.Count || !Zones[zone].Rows[row].IsTiles || col < 0 || col >= Zones[zone].Columns) return false;
        var cells = Zones[zone].Rows[row].Cells;
        if (Buffer.Contains(key))
        {
            var replaced = cells[col];
            Buffer.Remove(key);
            cells[col] = key;
            if (replaced is not null) Buffer.Add(replaced);
            return true;
        }
        if (Find(key) is not { } from || (zone, row, col) == from) return false;
        var other = cells[col];
        cells[col] = key;
        Zones[from.Zone].Rows[from.Row].Cells[from.Col] = other;
        return true;
    }

    /// <summary>Inserts a row at a place of a zone (0 = first, Count = last): a row of tiles, a label or a space.</summary>
    public bool AddRow(int zone, string kind, int at)
    {
        if (!InZone(zone) || Zones[zone].Rows.Count >= MariaUserDashboardRepository.MaxRows) return false;
        var row = kind == TilesKind ? NewTilesRow(Zones[zone]) : new LayoutRow { Kind = kind };
        Zones[zone].Rows.Insert(Math.Clamp(at, 0, Zones[zone].Rows.Count), row);
        return true;
    }

    /// <summary>Deletes a label, a space or an empty row of tiles; a row with tiles is not deleted (the tiles are removed first).</summary>
    public bool RemoveRow(int zone, int index)
    {
        if (!InZone(zone) || index < 0 || index >= Zones[zone].Rows.Count || (Zones[zone].Rows[index].IsTiles && !Zones[zone].Rows[index].IsEmpty)) return false;
        Zones[zone].Rows.RemoveAt(index);
        return true;
    }

    /// <summary>Moves a whole row (with its tiles, label or space) so that it ends at the place "to" of the same zone.</summary>
    public bool MoveRow(int zone, int from, int to)
    {
        if (!InZone(zone)) return false;
        var rows = Zones[zone].Rows;
        if (from < 0 || from >= rows.Count || to < 0 || to >= rows.Count || from == to) return false;
        var row = rows[from];
        rows.RemoveAt(from);
        rows.Insert(to, row);
        return true;
    }

    /// <summary>
    /// Moves a whole row to another zone, at the place "to" (the row that was there and the ones after it move down). The cells of a row of tiles are fitted to the number of tiles per row of the
    /// new zone: the tiles that no longer have a cell go to the first free cell from there down.
    /// </summary>
    public bool MoveRowToZone(int fromZone, int from, int toZone, int to)
    {
        if (fromZone == toZone) return MoveRow(fromZone, from, to);
        if (!InZone(fromZone) || !InZone(toZone) || from < 0 || from >= Zones[fromZone].Rows.Count || Zones[toZone].Rows.Count >= MariaUserDashboardRepository.MaxRows) return false;
        var row = Zones[fromZone].Rows[from];
        Zones[fromZone].Rows.RemoveAt(from);
        var target = Zones[toZone];
        var at = Math.Clamp(to, 0, target.Rows.Count);
        var overflow = new List<string>();
        if (row.IsTiles)
        {
            var cells = new string?[target.Columns];
            for (var col = 0; col < row.Cells.Length; col++)
            {
                if (row.Cells[col] is not { } key) continue;
                if (col < cells.Length) cells[col] = key; else overflow.Add(key);
            }
            row.Cells = cells;
        }
        target.Rows.Insert(at, row);
        foreach (var key in overflow) PlaceFrom(key, toZone, at);
        return true;
    }

    /// <summary>A new number of tiles per row of a zone; the tiles of the cells that no longer exist go to the first free cell from their row down, in their order (new rows of tiles are made when needed).</summary>
    public void SetColumns(int zone, int columns)
    {
        if (!InZone(zone)) return;
        columns = MariaUserDashboardRepository.ValidColumns(columns);
        var target = Zones[zone];
        if (columns == target.Columns) return;
        var overflow = new List<(int Row, string Key)>();
        for (var index = 0; index < target.Rows.Count; index++)
        {
            if (!target.Rows[index].IsTiles) continue;
            var old = target.Rows[index].Cells;
            for (var col = columns; col < old.Length; col++) if (old[col] is { } key) overflow.Add((index, key));
            var cells = new string?[columns];
            Array.Copy(old, cells, Math.Min(old.Length, columns));
            target.Rows[index].Cells = cells;
        }
        target.Columns = columns;
        foreach (var (row, key) in overflow) PlaceFrom(key, zone, row);
    }

    /// <summary>A new zone at the end (with a title, which is required, and the default number of tiles per row).</summary>
    public bool AddZone(string title)
    {
        if (Zones.Count >= DashboardZone.MaxZones) return false;
        Zones.Add(new LayoutZone { Title = title.Trim(), Columns = Zones.Count > 0 ? Zones[^1].Columns : MariaUserDashboardRepository.DefaultColumns });
        return true;
    }

    /// <summary>Where a zone stands (DashboardZone.Beside, Center, Left or Right); the first zone is always beside.</summary>
    public bool SetPlacement(int zone, string? placement)
    {
        if (!InZone(zone)) return false;
        Zones[zone].Placement = zone == 0 ? DashboardZone.Beside : DashboardZone.ValidPlacement(placement);
        return true;
    }

    /// <summary>The width of one zone in pixels: its own, else the global width of the zones; 0 = automatic (no width chosen).</summary>
    public int EffectiveWidth(LayoutZone zone) => zone.Width > 0 ? DashboardZone.ValidWidth(zone.Width) : Sizes.ZoneWidth;

    public bool SetZoneWidth(int zone, int width)
    {
        if (!InZone(zone)) return false;
        Zones[zone].Width = DashboardZone.ValidWidth(width);
        return true;
    }

    /// <summary>The zones grouped in bands, one band for each row of the page: the first zone starts the first band and every zone that is not "beside" starts a new one; the zones beside follow in the band of the zone before them.</summary>
    public IReadOnlyList<IReadOnlyList<int>> Bands()
    {
        var bands = new List<IReadOnlyList<int>>();
        List<int>? current = null;
        for (var z = 0; z < Zones.Count; z++)
        {
            if (current is null || (z > 0 && DashboardZone.ValidPlacement(Zones[z].Placement) != DashboardZone.Beside)) { current = []; bands.Add(current); }
            current.Add(z);
        }
        return bands;
    }

    /// <summary>The alignment of the zones of a band: the placement of its first zone; the first band fills the page, unless a width is chosen for a zone of it (then its zones are as wide as chosen and centred).</summary>
    public string BandAlign(IReadOnlyList<int> band) =>
        band.Count == 0 ? DashboardZone.Beside
        : band[0] == 0 ? (band.Any(z => EffectiveWidth(Zones[z]) > 0) ? DashboardZone.Center : DashboardZone.Beside)
        : DashboardZone.ValidPlacement(Zones[band[0]].Placement);

    public bool SetZoneTitle(int zone, string? title)
    {
        if (!InZone(zone)) return false;
        Zones[zone].Title = (title ?? "").Trim();
        return true;
    }

    /// <summary>
    /// Deletes a zone: its tiles go to the buffer; the last zone is not deleted (the page always has one). The labels and the spaces of the zone are deleted with it.
    /// </summary>
    public bool RemoveZone(int zone)
    {
        if (!InZone(zone) || Zones.Count <= 1) return false;
        foreach (var key in Zones[zone].Rows.Where(row => row.IsTiles).SelectMany(row => row.Cells).OfType<string>().ToList()) Buffer.Add(key);
        Zones.RemoveAt(zone);
        return true;
    }

    /// <summary>Moves a whole zone (with its rows and tiles) so that it ends at the place "to".</summary>
    public bool MoveZone(int from, int to)
    {
        if (from < 0 || from >= Zones.Count || to < 0 || to >= Zones.Count || from == to) return false;
        var zone = Zones[from];
        Zones.RemoveAt(from);
        Zones.Insert(to, zone);
        return true;
    }

    /// <summary>Scoate toate tile-urile, etichetele si spatiile; zonele (cu titlurile si numarul de coloane) raman.</summary>
    public void Clear()
    {
        Buffer.Clear();
        foreach (var zone in Zones) zone.Rows.Clear();
    }

    private bool InZone(int zone) => zone >= 0 && zone < Zones.Count;

    private LayoutRow NewTilesRow(LayoutZone zone) => new() { Kind = TilesKind, Cells = new string?[zone.Columns] };

    // The first free cell of a row of tiles of the zone at or after the row; a new row of tiles right after the last row searched when none is free.
    private bool PlaceFrom(string key, int zone, int firstRow)
    {
        if (!InZone(zone)) return false;
        var rows = Zones[zone].Rows;
        for (var row = Math.Max(firstRow, 0); row < rows.Count; row++)
        {
            if (!rows[row].IsTiles) continue;
            var free = Array.IndexOf(rows[row].Cells, null);
            if (free < 0) continue;
            rows[row].Cells[free] = key;
            return true;
        }
        if (rows.Count >= MariaUserDashboardRepository.MaxRows) return false;
        var created = NewTilesRow(Zones[zone]);
        created.Cells[0] = key;
        rows.Add(created);
        return true;
    }
}
