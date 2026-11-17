using System.Globalization;
using System.Text.Json;

namespace BlazorStoc.Services;

// Display settings of one user (PROP-0034). Every value is a percent of the default size (100 = as designed); null = "not set here": the value of the
// level above is used (table settings -> the user's global settings -> the default). The settings are stored in the database per user and loaded when the user opens a page.
public sealed record DisplaySettings(int? TableText = null, int? HeaderText = null, int? Images = null, int? Icons = null, int? Titles = null, int? Tabs = null, int? Menu = null, int? Text = null, int? Buttons = null, int? Fields = null, int? Topbar = null, int? Dialogs = null)
{
    public bool IsEmpty => TableText is null && HeaderText is null && Images is null && Icons is null && Titles is null && Tabs is null && Menu is null && Text is null && Buttons is null && Fields is null && Topbar is null && Dialogs is null;
}

public static class DisplayRules
{
    public const string GlobalScope = "global";
    public const string ProductsCatalogScope = "produse.catalog";
    public const int Min = 100, Max = 500, Default = 100;
    // Predefined values (panel of a table): 100% (as designed) and steps of 25% up to 500%; the global rules use sliders in steps of 5%.
    public static readonly IReadOnlyList<int> Steps = [100, 125, 150, 175, 200, 225, 250, 275, 300, 325, 350, 375, 400, 425, 450, 475, 500];
    public static readonly IReadOnlyList<(string Name, int Percent)> Presets = [("Normal", 100), ("Mare", 125), ("Foarte mare", 200)];

    public static int Clamp(int percent) => Math.Clamp(percent, Min, Max);
    private static int? Clamp(int? percent) => percent is { } value ? Clamp(value) : null;

    public static DisplaySettings Normalize(DisplaySettings settings) => new(Clamp(settings.TableText), Clamp(settings.HeaderText), Clamp(settings.Images), Clamp(settings.Icons), Clamp(settings.Titles), Clamp(settings.Tabs), Clamp(settings.Menu), Clamp(settings.Text), Clamp(settings.Buttons), Clamp(settings.Fields), Clamp(settings.Topbar), Clamp(settings.Dialogs));

    // Field by field: a value set for the table wins, what the table leaves unset comes from the global settings, the rest from the default.
    public static DisplaySettings Merge(DisplaySettings? global, DisplaySettings? table) => new(
        table?.TableText ?? global?.TableText, table?.HeaderText ?? global?.HeaderText, table?.Images ?? global?.Images,
        table?.Icons ?? global?.Icons, table?.Titles ?? global?.Titles, table?.Tabs ?? global?.Tabs, table?.Menu ?? global?.Menu, table?.Text ?? global?.Text, table?.Buttons ?? global?.Buttons, table?.Fields ?? global?.Fields, table?.Topbar ?? global?.Topbar, table?.Dialogs ?? global?.Dialogs);

    // One preset sets every field to the same scale.
    public static DisplaySettings Preset(int percent) => new(percent, percent, percent, percent, percent, percent, percent, percent, percent, percent, percent, percent);

    // The inline style (CSS custom properties, scale factors) put on a container; only the fields that are set, so the default stays the stylesheet's.
    public static string ToStyle(DisplaySettings? settings)
    {
        if (settings is null) return "";
        var parts = new List<string>();
        void Add(string name, int? percent) { if (percent is { } value) parts.Add($"--ui-{name}:{(Clamp(value) / 100d).ToString("0.##", CultureInfo.InvariantCulture)}"); }
        Add("table", settings.TableText); Add("header", settings.HeaderText); Add("images", settings.Images);
        Add("icons", settings.Icons); Add("titles", settings.Titles); Add("tabs", settings.Tabs); Add("menu", settings.Menu); Add("text", settings.Text);
        Add("buttons", settings.Buttons); Add("fields", settings.Fields); Add("topbar", settings.Topbar); Add("dialogs", settings.Dialogs);
        return string.Join(';', parts);
    }

    public static string ToJson(DisplaySettings settings) => JsonSerializer.Serialize(Normalize(settings));

    public static DisplaySettings FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return Normalize(JsonSerializer.Deserialize<DisplaySettings>(json) ?? new()); }
        catch (JsonException) { return new(); }
    }

    public static bool IsValidScope(string scope) => scope.Length is > 0 and <= 80 && scope.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_');
}

public sealed record UserDisplayData(DisplaySettings Global, IReadOnlyDictionary<string, DisplaySettings> Tables)
{
    public static UserDisplayData Empty { get; } = new(new(), new Dictionary<string, DisplaySettings>());
    public DisplaySettings For(string scope) => DisplayRules.Merge(Global, Tables.GetValueOrDefault(scope));
}

// Persistence of the display settings of the signed-in user (no permission key: every user manages their own).
public interface IUserDisplayRepository
{
    Task<UserDisplayData> GetAsync(CancellationToken cancellationToken = default);
    // Saves the settings of a scope ("global" or a table id); empty settings remove the row (back to the level above).
    Task SaveAsync(string scope, DisplaySettings settings, CancellationToken cancellationToken = default);
    Task ClearAllAsync(CancellationToken cancellationToken = default);
}

// Scoped per circuit: the data is read once and kept, a save updates it and raises Changed.
public interface IUserDisplayService
{
    event Action? Changed;
    Task<UserDisplayData> GetAsync(CancellationToken cancellationToken = default);
    Task<string> StyleAsync(string? scope = null, CancellationToken cancellationToken = default);
    Task SaveAsync(string scope, DisplaySettings settings, CancellationToken cancellationToken = default);
    Task ClearAllAsync(CancellationToken cancellationToken = default);
}

public sealed class UserDisplayService(IUserDisplayRepository repository, ILogger<UserDisplayService> logger) : IUserDisplayService
{
    private UserDisplayData? data;
    public event Action? Changed;

    public async Task<UserDisplayData> GetAsync(CancellationToken cancellationToken = default)
    {
        if (data is not null) return data;
        try { data = await repository.GetAsync(cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The display is a convenience: a database problem must not stop the page, the defaults are used.
            logger.LogWarning("Display settings could not be read ({ErrorType}); defaults are used.", exception.GetType().Name);
            return UserDisplayData.Empty;
        }
        return data;
    }

    public async Task<string> StyleAsync(string? scope = null, CancellationToken cancellationToken = default)
    {
        var current = await GetAsync(cancellationToken).ConfigureAwait(false);
        return DisplayRules.ToStyle(scope is null ? current.Global : current.For(scope));
    }

    public async Task SaveAsync(string scope, DisplaySettings settings, CancellationToken cancellationToken = default)
    {
        if (!DisplayRules.IsValidScope(scope)) throw new ArgumentException("Zona setărilor de afișare nu este validă.", nameof(scope));
        var normalized = DisplayRules.Normalize(settings);
        await repository.SaveAsync(scope, normalized, cancellationToken).ConfigureAwait(false);
        var current = await GetAsync(cancellationToken).ConfigureAwait(false);
        var tables = current.Tables.ToDictionary(pair => pair.Key, pair => pair.Value);
        if (scope == DisplayRules.GlobalScope) data = new(normalized, tables);
        else
        {
            if (normalized.IsEmpty) tables.Remove(scope); else tables[scope] = normalized;
            data = new(current.Global, tables);
        }
        Changed?.Invoke();
    }

    public async Task ClearAllAsync(CancellationToken cancellationToken = default)
    {
        await repository.ClearAllAsync(cancellationToken).ConfigureAwait(false);
        data = UserDisplayData.Empty;
        Changed?.Invoke();
    }
}
