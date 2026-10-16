namespace BlazorStoc.Services;

// The tile provider of the maintenance map, configured apart from the code of the markers (section "Map" of the configuration; a key,
// if a provider needs one, goes in the private configuration, never in a versioned file). Only the tiles of the visible area are asked
// for by the browser; no beneficiary or contract data is ever put in a tile address. The attribution of the provider stays visible.
public sealed class MapOptions
{
    public const string SectionName = "Map";
    public const string DefaultTileUrl = "https://tile.openstreetmap.org/{z}/{x}/{y}.png";
    public const string DefaultAttribution = "&copy; <a href=\"https://www.openstreetmap.org/copyright\" target=\"_blank\" rel=\"noopener\">OpenStreetMap</a> contributors";

    public string TileUrl { get; set; } = DefaultTileUrl;
    public string Attribution { get; set; } = DefaultAttribution;
    public int MinZoom { get; set; } = 3;
    public int MaxZoom { get; set; } = 19;
    // Where the map starts when nothing is shown yet (Romania).
    public double CenterLatitude { get; set; } = 45.9;
    public double CenterLongitude { get; set; } = 25.0;
    public int StartZoom { get; set; } = 6;

    // The provider used when the configured values are unusable: a tile address must be https and contain the three coordinates.
    public MapOptions Normalized()
    {
        var url = Uri.TryCreate(TileUrl?.Replace("{z}", "0").Replace("{x}", "0").Replace("{y}", "0").Replace("{s}", "a").Replace("{r}", ""), UriKind.Absolute, out var parsed) &&
                  parsed.Scheme == Uri.UriSchemeHttps && TileUrl!.Contains("{z}") && TileUrl.Contains("{x}") && TileUrl.Contains("{y}") ? TileUrl : DefaultTileUrl;
        var min = Math.Clamp(MinZoom, 0, 18);
        return new()
        {
            TileUrl = url, Attribution = string.IsNullOrWhiteSpace(Attribution) ? DefaultAttribution : Attribution,
            MinZoom = min, MaxZoom = Math.Clamp(MaxZoom, min, 20),
            CenterLatitude = Math.Clamp(CenterLatitude, -90, 90), CenterLongitude = Math.Clamp(CenterLongitude, -180, 180), StartZoom = Math.Clamp(StartZoom, 0, 20)
        };
    }
}
