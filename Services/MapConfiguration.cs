using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BlazorStoc.Services;

// Map settings edited by the administrator (Setări → Hartă): the map engine (tile provider) and the pin types of the overlay. They are kept in
// one JSON file (data/map-configuration.json, like the ANAF configuration), so no schema change is needed. Until something is saved, the engine
// comes from the "Map" section of appsettings.json and the pin types are the built-in ones. Nothing typed here is ever written as HTML: colours
// are #rrggbb, glyphs are two plain characters, the attribution is text plus an optional https link that this code turns into markup.

public sealed class MapOperationException(string message) : Exception(message);

public sealed class MapEngineSettings
{
    public string TileUrl { get; set; } = MapOptions.DefaultTileUrl;
    public string AttributionText { get; set; } = "© OpenStreetMap contributors";
    public string AttributionUrl { get; set; } = "https://www.openstreetmap.org/copyright";
    public int MinZoom { get; set; } = 3;
    public int MaxZoom { get; set; } = 19;
    public double CenterLatitude { get; set; } = 45.9;
    public double CenterLongitude { get; set; } = 25.0;
    public int StartZoom { get; set; } = 6;
    // Optional key of the provider, used where the address has {key}; it is never shown again after saving and never written to the journal.
    public string ApiKey { get; set; } = "";

    public MapEngineSettings Clone() => (MapEngineSettings)MemberwiseClone();

    // The settings the page of the map uses: the key is put in the address and the attribution becomes safe markup.
    public MapOptions ToOptions() => new MapOptions
    {
        TileUrl = TileUrl.Replace("{key}", Uri.EscapeDataString(ApiKey ?? ""), StringComparison.Ordinal),
        Attribution = MapEngineRules.AttributionHtml(AttributionText, AttributionUrl),
        MinZoom = MinZoom, MaxZoom = MaxZoom, CenterLatitude = CenterLatitude, CenterLongitude = CenterLongitude, StartZoom = StartZoom
    }.Normalized();

    // The starting values when nothing was saved: what appsettings.json says.
    public static MapEngineSettings FromOptions(MapOptions options)
    {
        var html = options.Attribution ?? "";
        var link = Regex.Match(html, "href=\"(https://[^\"]+)\"");
        var text = html == MapOptions.DefaultAttribution ? "© OpenStreetMap contributors" : WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", ""));
        return new()
        {
            TileUrl = options.TileUrl, AttributionText = text.Trim(), AttributionUrl = link.Success ? link.Groups[1].Value : "",
            MinZoom = options.MinZoom, MaxZoom = options.MaxZoom, CenterLatitude = options.CenterLatitude, CenterLongitude = options.CenterLongitude, StartZoom = options.StartZoom
        };
    }
}

public static class MapEngineRules
{
    public const int MaxTileUrl = 500;
    public const int MaxAttribution = 200;

    public static string AttributionHtml(string? text, string? url)
    {
        var shown = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(text) ? "© OpenStreetMap contributors" : text.Trim());
        return IsHttps(url) ? $"<a href=\"{WebUtility.HtmlEncode(new Uri(url!.Trim()).AbsoluteUri)}\" target=\"_blank\" rel=\"noopener\">{shown}</a>" : shown;
    }

    public static bool IsHttps(string? url) =>
        !string.IsNullOrWhiteSpace(url) && Uri.TryCreate(url.Trim(), UriKind.Absolute, out var parsed) && parsed.Scheme == Uri.UriSchemeHttps && parsed.UserInfo.Length == 0;

    // null = valid. The address must be https, hold {z}, {x}, {y} (and {key} only with a key), have no spaces and no credentials.
    public static string? Validate(MapEngineSettings settings)
    {
        var url = settings.TileUrl?.Trim() ?? "";
        if (url.Length == 0) return "Completează adresa dalelor.";
        if (url.Length > MaxTileUrl) return $"Adresa dalelor poate avea cel mult {MaxTileUrl} de caractere.";
        if (url.Any(char.IsWhiteSpace)) return "Adresa dalelor nu poate conține spații.";
        if (!url.Contains("{z}") || !url.Contains("{x}") || !url.Contains("{y}")) return "Adresa dalelor trebuie să conțină {z}, {x} și {y}.";
        var probe = url.Replace("{z}", "0").Replace("{x}", "0").Replace("{y}", "0").Replace("{s}", "a").Replace("{r}", "").Replace("{key}", "k");
        if (!Uri.TryCreate(probe, UriKind.Absolute, out var parsed) || parsed.Scheme != Uri.UriSchemeHttps) return "Adresa dalelor trebuie să înceapă cu https:// și să fie o adresă validă.";
        if (parsed.UserInfo.Length > 0) return "Adresa dalelor nu poate conține nume de utilizator sau parolă; folosește câmpul „Cheie furnizor”.";
        if (url.Contains("{key}") && string.IsNullOrWhiteSpace(settings.ApiKey)) return "Adresa conține {key}: completează câmpul „Cheie furnizor”.";
        if (settings.ApiKey?.Length > 200 || settings.ApiKey?.Any(char.IsControl) == true) return "Cheia furnizorului nu este validă.";
        var text = settings.AttributionText?.Trim() ?? "";
        if (text.Length == 0) return "Completează textul atribuirii (furnizorii de hărți o cer).";
        if (text.Length > MaxAttribution || text.Any(char.IsControl)) return $"Textul atribuirii poate avea cel mult {MaxAttribution} de caractere, fără caractere de control.";
        if (!string.IsNullOrWhiteSpace(settings.AttributionUrl) && !IsHttps(settings.AttributionUrl)) return "Adresa atribuirii trebuie să fie o adresă https validă (sau să rămână necompletată).";
        if (settings.MinZoom is < 0 or > 18) return "Zoom-ul minim trebuie să fie între 0 și 18.";
        if (settings.MaxZoom < settings.MinZoom || settings.MaxZoom > 20) return "Zoom-ul maxim trebuie să fie între zoom-ul minim și 20.";
        if (settings.StartZoom < settings.MinZoom || settings.StartZoom > settings.MaxZoom) return "Zoom-ul de pornire trebuie să fie între zoom-ul minim și cel maxim.";
        if (settings.CenterLatitude is < -90 or > 90 || settings.CenterLongitude is < -180 or > 180 || double.IsNaN(settings.CenterLatitude) || double.IsNaN(settings.CenterLongitude))
            return "Centrul hărții trebuie să aibă latitudinea între -90 și 90 și longitudinea între -180 și 180.";
        return null;
    }

    public static MapEngineSettings Normalize(MapEngineSettings settings)
    {
        var copy = settings.Clone();
        copy.TileUrl = (copy.TileUrl ?? "").Trim();
        copy.AttributionText = (copy.AttributionText ?? "").Trim();
        copy.AttributionUrl = IsHttps(copy.AttributionUrl) ? new Uri(copy.AttributionUrl!.Trim()).AbsoluteUri : "";
        copy.ApiKey = (copy.ApiKey ?? "").Trim();
        return copy;
    }

    public static IEnumerable<AuditChange> Changes(MapEngineSettings before, MapEngineSettings after)
    {
        static string Number(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
        yield return new("Adresa dalelor", before.TileUrl, after.TileUrl);
        yield return new("Text atribuire", before.AttributionText, after.AttributionText);
        yield return new("Adresa atribuirii", before.AttributionUrl, after.AttributionUrl);
        yield return new("Zoom minim", before.MinZoom.ToString(CultureInfo.InvariantCulture), after.MinZoom.ToString(CultureInfo.InvariantCulture));
        yield return new("Zoom maxim", before.MaxZoom.ToString(CultureInfo.InvariantCulture), after.MaxZoom.ToString(CultureInfo.InvariantCulture));
        yield return new("Zoom de pornire", before.StartZoom.ToString(CultureInfo.InvariantCulture), after.StartZoom.ToString(CultureInfo.InvariantCulture));
        yield return new("Latitudine centru", Number(before.CenterLatitude), Number(after.CenterLatitude));
        yield return new("Longitudine centru", Number(before.CenterLongitude), Number(after.CenterLongitude));
        // The key itself never appears in the journal or on screen.
        yield return new("Cheie furnizor", before.ApiKey.Length == 0 ? "necompletată" : "completată", before.ApiKey == after.ApiKey ? (after.ApiKey.Length == 0 ? "necompletată" : "completată") : (after.ApiKey.Length == 0 ? "ștearsă" : "schimbată"));
    }
}

// A kind of pin of the overlay. Fill types colour the marker (one per point: the due state); badge types are the small round marks on its
// corner (the state of the contract term, or a custom rule). Built-in types cannot be deleted or change their rule; custom types are badges
// with a rule chosen from a short fixed list.
public sealed class MapPinType
{
    public const string KindFill = "fill";
    public const string KindBadge = "badge";
    public const string RuleContractExpired = "expired";
    public const string RuleContractExpiring = "expiring";
    public const string RuleNoIntervention = "no-intervention";

    public int Id { get; set; }
    public string Key { get; set; } = "";
    public string Kind { get; set; } = KindBadge;
    public string Rule { get; set; } = "";
    public int Months { get; set; } = 12;
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#7a4fb5";
    public string Glyph { get; set; } = "";
    public bool Active { get; set; } = true;
    public int Order { get; set; }
    public bool BuiltIn { get; set; }

    public bool IsFill => Kind == KindFill;
    public string Foreground => MapPinRules.Foreground(Color);
    public MapPinType Clone() => (MapPinType)MemberwiseClone();
}

public static class MapPinRules
{
    public const int MaxCustom = 3;
    // Built-in types have the ids 1-6; the ids of custom types start here (so a journal link never confuses the two).
    public const int FirstCustomId = 1000;
    public const int MaxName = 60;
    public const int MinMonths = 1;
    public const int MaxMonths = 120;

    private static readonly Regex ColorPattern = new("^#[0-9a-fA-F]{6}$", RegexOptions.Compiled);

    public static IReadOnlyList<MapPinType> Defaults() =>
    [
        Built(1, MapPinType.KindFill, "overdue", "Scadență depășită", "#c0392b", "!"),
        Built(2, MapPinType.KindFill, "soon", "În curând", "#d99a1f", "◔"),
        Built(3, MapPinType.KindFill, "ok", "La zi", "#2f8f5b", "✓"),
        Built(4, MapPinType.KindFill, "off", "Contract Off", "#8a969a", "–"),
        Built(5, MapPinType.KindBadge, MaintenanceMapRules.BadgeExpired, "Contract expirat", "#5b0f0f", "E", MapPinType.RuleContractExpired),
        Built(6, MapPinType.KindBadge, MaintenanceMapRules.BadgeSoon, "Contract expiră în curând", "#e67e22", "!", MapPinType.RuleContractExpiring)
    ];

    private static MapPinType Built(int order, string kind, string key, string name, string color, string glyph, string rule = "") =>
        new() { Id = order, Key = key, Kind = kind, Rule = rule, Name = name, Color = color, Glyph = glyph, Order = order, BuiltIn = true };

    // Dark or white text, whichever reads better on the colour (WCAG contrast ratio).
    public static string Foreground(string color)
    {
        if (!ColorPattern.IsMatch(color ?? "")) return "#ffffff";
        double Channel(int start)
        {
            var value = int.Parse(color.AsSpan(start, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            return value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }
        var luminance = 0.2126 * Channel(1) + 0.7152 * Channel(3) + 0.0722 * Channel(5);
        // The text colour with the higher contrast ratio against the pin colour (dark text is about 0.022 in luminance).
        return (luminance + 0.05) / 0.072 > 1.05 / (luminance + 0.05) ? "#1d2b2e" : "#ffffff";
    }

    // The stored types merged over the built-in ones (a built-in type that was never edited, or is missing from the file, is the default).
    public static IReadOnlyList<MapPinType> Effective(IEnumerable<MapPinType>? stored)
    {
        var saved = (stored ?? []).ToList();
        var result = new List<MapPinType>();
        foreach (var builtIn in Defaults())
        {
            var match = saved.FirstOrDefault(item => item.BuiltIn && item.Kind == builtIn.Kind && item.Key == builtIn.Key);
            if (match is null) { result.Add(builtIn); continue; }
            var merged = builtIn.Clone();
            merged.Name = Safe(match.Name, builtIn.Name); merged.Color = ColorPattern.IsMatch(match.Color ?? "") ? match.Color!.ToLowerInvariant() : builtIn.Color;
            merged.Glyph = GlyphOk(match.Glyph) ? match.Glyph ?? "" : builtIn.Glyph; merged.Order = match.Order; merged.Active = builtIn.IsFill || match.Active;
            result.Add(merged);
        }
        result.AddRange(saved.Where(item => !item.BuiltIn && item.Id >= FirstCustomId && item.Rule == MapPinType.RuleNoIntervention).Take(MaxCustom));
        return result.OrderBy(item => item.IsFill ? 0 : 1).ThenBy(item => item.Order).ThenBy(item => item.Id).ToList();
    }

    private static string Safe(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    public static bool GlyphOk(string? glyph) =>
        glyph is null || (glyph.Length <= 2 && glyph.All(ch => !char.IsControl(ch) && "<>&\"'\\".IndexOf(ch) < 0));

    public static string RuleText(MapPinType type) => type.Rule switch
    {
        MapPinType.RuleContractExpired => "contractul a expirat",
        MapPinType.RuleContractExpiring => "contractul expiră în curând (pragul șablonului „Expirare contract”)",
        MapPinType.RuleNoIntervention => $"nicio intervenție de mentenanță de cel puțin {type.Months} {(type.Months == 1 ? "lună" : "luni")} (sau niciodată)",
        _ => ""
    };

    // null = valid. "others" are the other pin types (the one being edited excluded).
    public static string? Validate(MapPinType type, IEnumerable<MapPinType> others)
    {
        var name = type.Name?.Trim() ?? "";
        if (name.Length == 0) return "Completează denumirea tipului de pin.";
        if (name.Length > MaxName || name.Any(char.IsControl)) return $"Denumirea poate avea cel mult {MaxName} de caractere.";
        if (!ColorPattern.IsMatch(type.Color ?? "")) return "Alege o culoare validă (#rrggbb).";
        if (!GlyphOk(type.Glyph)) return "Simbolul poate avea cel mult 2 caractere și nu poate conține < > & \" ' sau \\.";
        var rest = others.ToList();
        if (rest.Any(item => string.Equals(item.Name.Trim(), name, StringComparison.CurrentCultureIgnoreCase))) return $"Există deja un tip de pin numit „{name}”.";
        if (type.IsFill && rest.Any(item => item.IsFill && string.Equals(item.Color, type.Color, StringComparison.OrdinalIgnoreCase)))
            return $"Culoarea {type.Color} este folosită deja de alt tip de umplere („{rest.First(item => item.IsFill && string.Equals(item.Color, type.Color, StringComparison.OrdinalIgnoreCase)).Name}”): starea scadenței s-ar confunda.";
        if (!type.BuiltIn)
        {
            if (type.Rule != MapPinType.RuleNoIntervention) return "Alege regula tipului de pin.";
            if (type.Months is < MinMonths or > MaxMonths) return $"Numărul de luni trebuie să fie între {MinMonths} și {MaxMonths}.";
            if (type.Id == 0 && rest.Count(item => !item.BuiltIn) >= MaxCustom) return $"Pot exista cel mult {MaxCustom} tipuri de pin proprii; șterge unul înainte să adaugi altul.";
        }
        if (type.Order is < 0 or > 1000) return "Ordinea trebuie să fie între 0 și 1000.";
        return null;
    }

    public static MapPinType Normalize(MapPinType type)
    {
        var copy = type.Clone();
        copy.Name = (copy.Name ?? "").Trim();
        copy.Color = (copy.Color ?? "").Trim().ToLowerInvariant();
        copy.Glyph = copy.Glyph ?? "";
        return copy;
    }

    public static IEnumerable<AuditChange> Changes(MapPinType before, MapPinType after)
    {
        string YesNo(bool value) => value ? "Da" : "Nu";
        yield return new("Denumire", before.Name, after.Name);
        yield return new("Culoare", before.Color, after.Color);
        yield return new("Simbol", before.Glyph.Length == 0 ? "(fără)" : before.Glyph, after.Glyph.Length == 0 ? "(fără)" : after.Glyph);
        yield return new("Activ", YesNo(before.Active), YesNo(after.Active));
        yield return new("Ordine", before.Order.ToString(CultureInfo.InvariantCulture), after.Order.ToString(CultureInfo.InvariantCulture));
        if (!before.BuiltIn) yield return new("Luni fără intervenție", before.Months.ToString(CultureInfo.InvariantCulture), after.Months.ToString(CultureInfo.InvariantCulture));
    }

    // The custom badges that a point carries now (its contract must be followed, that is On).
    public static IReadOnlyList<string> ExtraBadges(ServiceDueRow row, DateOnly today, IReadOnlyList<MapPinType>? pinTypes)
    {
        if (!row.Contract.IsActive || pinTypes is null) return [];
        return pinTypes.Where(type => !type.BuiltIn && type.Active && type.Rule == MapPinType.RuleNoIntervention &&
                (row.LastIntervention is not { } last || last.AddMonths(type.Months) <= today)).Select(type => type.Key).ToList();
    }
}

public sealed class MapConfiguration
{
    public MapEngineSettings? Engine { get; set; }
    public List<MapPinType> PinTypes { get; set; } = [];
    public long Version { get; set; }
    public int NextId { get; set; } = MapPinRules.FirstCustomId;
}

// What the map page needs: the engine as usable options and the active-and-inactive pin types (the page hides the inactive ones).
public sealed record MapView(MapOptions Engine, IReadOnlyList<MapPinType> PinTypes, long Version)
{
    public IReadOnlyList<MapPinType> ActiveTypes => PinTypes.Where(type => type.Active).ToList();
    public MapPinType? Find(string kind, string key) => PinTypes.FirstOrDefault(type => type.Kind == kind && type.Key == key);
}

public sealed record MapSettingsState(MapEngineSettings Engine, bool EngineSaved, MapEngineSettings Defaults, IReadOnlyList<MapPinType> PinTypes, long Version);

public interface IMapConfigurationService
{
    Task<MapView> GetViewAsync(CancellationToken cancellationToken = default);
    Task<MapSettingsState> GetStateAsync(CancellationToken cancellationToken = default);
    Task SaveEngineAsync(MapEngineSettings input, long expectedVersion, CancellationToken cancellationToken = default);
    Task ResetEngineAsync(long expectedVersion, CancellationToken cancellationToken = default);
    Task<MapPinType> SavePinTypeAsync(MapPinType input, long expectedVersion, CancellationToken cancellationToken = default);
    Task DeletePinTypeAsync(int id, string reason, long expectedVersion, CancellationToken cancellationToken = default);
}

public sealed class MapConfigStore(IConfiguration configuration)
{
    public SemaphoreSlim FileGate { get; } = new(1, 1);
    private readonly string path = Path.GetFullPath(configuration["Map:ConfigurationPath"] ?? Path.Combine("data", "map-configuration.json"));
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public async Task<MapConfiguration> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return new();
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<MapConfiguration>(stream, Options, cancellationToken) ?? new();
        }
        catch (JsonException) { return new(); }
    }

    public async Task WriteAsync(MapConfiguration state, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(state, Options), new UTF8Encoding(false), cancellationToken);
        File.Move(temp, path, true);
    }
}

public sealed class MapConfigurationService(MapConfigStore store, Microsoft.Extensions.Options.IOptions<MapOptions> defaults, IAccessControl access, IAuditTrail auditTrail) : IMapConfigurationService
{
    private MapEngineSettings Defaults => MapEngineSettings.FromOptions(defaults.Value.Normalized());

    public async Task<MapView> GetViewAsync(CancellationToken cancellationToken = default)
    {
        MapConfiguration state;
        await store.FileGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { state = await store.ReadAsync(cancellationToken).ConfigureAwait(false); }
        finally { store.FileGate.Release(); }
        var engine = state.Engine is { } saved && MapEngineRules.Validate(saved) is null ? saved.ToOptions() : defaults.Value.Normalized();
        return new(engine, MapPinRules.Effective(state.PinTypes), state.Version);
    }

    public async Task<MapSettingsState> GetStateAsync(CancellationToken cancellationToken = default)
    {
        await access.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        await store.FileGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await store.ReadAsync(cancellationToken).ConfigureAwait(false);
            return new(state.Engine?.Clone() ?? Defaults, state.Engine is not null, Defaults, MapPinRules.Effective(state.PinTypes), state.Version);
        }
        finally { store.FileGate.Release(); }
    }

    private static void CheckVersion(MapConfiguration state, long expected)
    {
        if (state.Version != expected) throw new MapOperationException("Setările hărții au fost modificate între timp (de alt administrator). Reîncarcă pagina și reia modificarea.");
    }

    public async Task SaveEngineAsync(MapEngineSettings input, long expectedVersion, CancellationToken cancellationToken = default)
    {
        await access.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var value = MapEngineRules.Normalize(input);
        if (MapEngineRules.Validate(value) is { } problem) throw new MapOperationException(problem);
        await store.FileGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await store.ReadAsync(cancellationToken).ConfigureAwait(false);
            CheckVersion(state, expectedVersion);
            var before = state.Engine ?? Defaults;
            var changes = MapEngineRules.Changes(before, value).Where(change => change.Before != change.After).ToArray();
            if (changes.Length == 0) throw new MapOperationException("Nu ai modificat nimic.");
            state.Engine = value; state.Version++;
            await store.WriteAsync(state, cancellationToken).ConfigureAwait(false);
            await AuditRecorder.RecordEditAsync(auditTrail, access, AuditEntities.MapEngine, "1", "Furnizor hartă", changes, "Modificare din Setări → Hartă → Motor hartă",
                cancellationToken, AuditActions.EditMapEngine).ConfigureAwait(false);
        }
        finally { store.FileGate.Release(); }
    }

    public async Task ResetEngineAsync(long expectedVersion, CancellationToken cancellationToken = default)
    {
        await access.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        await store.FileGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await store.ReadAsync(cancellationToken).ConfigureAwait(false);
            CheckVersion(state, expectedVersion);
            if (state.Engine is null) throw new MapOperationException("Se folosesc deja setările din configurația aplicației.");
            var changes = MapEngineRules.Changes(state.Engine, Defaults).Where(change => change.Before != change.After).ToArray();
            state.Engine = null; state.Version++;
            await store.WriteAsync(state, cancellationToken).ConfigureAwait(false);
            await AuditRecorder.RecordActionAsync(auditTrail, access, AuditEntities.MapEngine, AuditActions.ResetMapEngine, "1", "Furnizor hartă",
                changes.Length == 0 ? "Revenire la setările din configurația aplicației (aceleași valori)." : AuditDetails.Changes(changes), "Revenire la setările din configurația aplicației", cancellationToken).ConfigureAwait(false);
        }
        finally { store.FileGate.Release(); }
    }

    public async Task<MapPinType> SavePinTypeAsync(MapPinType input, long expectedVersion, CancellationToken cancellationToken = default)
    {
        await access.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var value = MapPinRules.Normalize(input);
        await store.FileGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await store.ReadAsync(cancellationToken).ConfigureAwait(false);
            CheckVersion(state, expectedVersion);
            var all = MapPinRules.Effective(state.PinTypes).ToList();
            var existing = value.Id == 0 ? null : all.FirstOrDefault(item => item.Id == value.Id);
            if (value.Id != 0 && existing is null) throw new MapOperationException("Tipul de pin nu mai există (a fost șters între timp).");
            if (existing is not null)
            {
                // What cannot be edited stays as it is, whatever the form sent.
                value.Kind = existing.Kind; value.Key = existing.Key; value.BuiltIn = existing.BuiltIn;
                if (existing.BuiltIn) { value.Rule = existing.Rule; value.Months = existing.Months; if (existing.IsFill) value.Active = true; }
            }
            else { value.Kind = MapPinType.KindBadge; value.Rule = MapPinType.RuleNoIntervention; value.BuiltIn = false; }
            if (MapPinRules.Validate(value, all.Where(item => item.Id != value.Id)) is { } problem) throw new MapOperationException(problem);
            var stored = state.PinTypes.Where(item => item.BuiltIn || item.Id >= MapPinRules.FirstCustomId).ToList();
            if (existing is null)
            {
                value.Id = Math.Max(state.NextId, stored.Where(item => item.Id >= MapPinRules.FirstCustomId).Select(item => item.Id + 1).DefaultIfEmpty(MapPinRules.FirstCustomId).Max());
                value.Key = "c" + value.Id.ToString(CultureInfo.InvariantCulture);
                state.NextId = value.Id + 1;
                stored.Add(value);
                state.PinTypes = stored; state.Version++;
                await store.WriteAsync(state, cancellationToken).ConfigureAwait(false);
                await AuditRecorder.RecordActionAsync(auditTrail, access, AuditEntities.MapPinType, AuditActions.CreateMapPinType, value.Id.ToString(CultureInfo.InvariantCulture),
                    "Tip pin: " + value.Name, $"Denumire: {value.Name}; culoare: {value.Color}; simbol: {(value.Glyph.Length == 0 ? "(fără)" : value.Glyph)}; regulă: {MapPinRules.RuleText(value)}; activ: {(value.Active ? "Da" : "Nu")}; ordine: {value.Order}",
                    "Adăugare din Setări → Hartă → Overlay", cancellationToken).ConfigureAwait(false);
                return value;
            }
            var changes = MapPinRules.Changes(existing, value).Where(change => change.Before != change.After).ToArray();
            if (changes.Length == 0) throw new MapOperationException("Nu ai modificat nimic.");
            var position = stored.FindIndex(item => item.Kind == value.Kind && item.Key == value.Key);
            if (position >= 0) stored[position] = value; else stored.Add(value);
            state.PinTypes = stored; state.Version++;
            await store.WriteAsync(state, cancellationToken).ConfigureAwait(false);
            await AuditRecorder.RecordEditAsync(auditTrail, access, AuditEntities.MapPinType, value.Id.ToString(CultureInfo.InvariantCulture), "Tip pin: " + existing.Name, changes,
                "Modificare din Setări → Hartă → Overlay", cancellationToken, AuditActions.EditMapPinType).ConfigureAwait(false);
            return value;
        }
        finally { store.FileGate.Release(); }
    }

    public async Task DeletePinTypeAsync(int id, string reason, long expectedVersion, CancellationToken cancellationToken = default)
    {
        await access.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        if (ChangeReasonRules.ValidationError(ChangeReasonRules.Normalize(reason)) is { } reasonProblem) throw new MapOperationException(reasonProblem);
        await store.FileGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await store.ReadAsync(cancellationToken).ConfigureAwait(false);
            CheckVersion(state, expectedVersion);
            var target = state.PinTypes.FirstOrDefault(item => item.Id == id && !item.BuiltIn && id >= MapPinRules.FirstCustomId)
                ?? throw new MapOperationException("Tipul de pin nu există sau este un tip predefinit (se poate dezactiva, nu șterge).");
            state.PinTypes = state.PinTypes.Where(item => item != target).ToList(); state.Version++;
            await store.WriteAsync(state, cancellationToken).ConfigureAwait(false);
            await AuditRecorder.RecordActionAsync(auditTrail, access, AuditEntities.MapPinType, AuditActions.DeleteMapPinType, id.ToString(CultureInfo.InvariantCulture),
                "Tip pin: " + target.Name, $"Denumire: {target.Name}; culoare: {target.Color}; regulă: {MapPinRules.RuleText(target)}", ChangeReasonRules.Normalize(reason), cancellationToken).ConfigureAwait(false);
        }
        finally { store.FileGate.Release(); }
    }
}
