using System.Globalization;

namespace BlazorStoc.Services;

// The maintenance map is a read-only view over the due list (one row per work point covered by a contract): a marker is a work point with
// its next due date. The fill of the marker shows only the due state (overdue / soon / on time; grey for a contract switched Off), the badge
// only the state of the contract term (expired / expiring soon); the two never share a colour. The data leaves this class as plain fields:
// nothing typed by users is turned into HTML.

// What the script draws. Id is the coverage (service_contract_points.id).
public sealed record MapMarker(int Id, double Lat, double Lng, string Fill, string Contract, string Title, IReadOnlyList<string>? Extra = null);

public static class MaintenanceMapRules
{
    public const string FillOverdue = "overdue";
    public const string FillSoon = "soon";
    public const string FillOk = "ok";
    public const string FillOff = "off";
    public const string BadgeExpired = "expired";
    public const string BadgeSoon = "soon";

    // The separate window (opened by data-map-popout in wwwroot/map-window.js; the fixed name reuses one window) and the pace of the
    // automatic refresh, which runs only while the map is visible.
    public const string WindowUrl = "/mentenanta/harta/fereastra";
    public const string WindowName = "blazorstoc-harta";
    public const int AutoRefreshSeconds = 30;

    // How many rows of the fresh list are new or differ from the shown ones (compared by the coverage id and by value).
    public static int ChangedRows(IReadOnlyList<ServiceDueRow> shown, IReadOnlyList<ServiceDueRow> fresh)
    {
        var known = shown.ToDictionary(row => row.Point.Point.Id);
        return fresh.Count(row => !known.TryGetValue(row.Point.Point.Id, out var before) || before != row);
    }

    public static string Fill(ServiceDueRow row, DateOnly today, int dueThresholdDays)
    {
        if (!row.Contract.IsActive) return FillOff;
        return ServiceDueRules.State(row.Point.Point.NextDue, today, dueThresholdDays) switch
        {
            ServiceDueState.Overdue => FillOverdue,
            ServiceDueState.DueSoon => FillSoon,
            _ => FillOk
        };
    }

    // Empty for a contract with no term, a valid one, or one switched Off (it is not followed).
    public static string Badge(ServiceDueRow row, DateOnly today, int expiryThresholdDays)
    {
        if (!row.Contract.IsActive) return string.Empty;
        return ServiceDueRules.ExpiryState(row.Contract, today, expiryThresholdDays) switch
        {
            ServiceExpiryState.Expired => BadgeExpired,
            ServiceExpiryState.ExpiresSoon => BadgeSoon,
            _ => string.Empty
        };
    }

    public static string Title(ServiceDueRow row) => $"{row.BeneficiaryName} · {row.Point.WorkPointName}";

    // Rows without coordinates cannot be placed: they are listed apart, to be completed on the work point.
    public static MapMarker? Marker(ServiceDueRow row, DateOnly today, int dueThresholdDays, int expiryThresholdDays, IReadOnlyList<MapPinType>? pinTypes = null) =>
        row.Latitude is { } latitude && row.Longitude is { } longitude
            ? new(row.Point.Point.Id, (double)latitude, (double)longitude, Fill(row, today, dueThresholdDays), Badge(row, today, expiryThresholdDays), Title(row), MapPinRules.ExtraBadges(row, today, pinTypes))
            : null;

    // Points of the same building (same coordinates): shown together in the panel of the selected one.
    public static IReadOnlyList<ServiceDueRow> SameLocation(IEnumerable<ServiceDueRow> rows, ServiceDueRow selected) =>
        selected.HasCoordinates
            ? rows.Where(row => row.Point.Point.Id != selected.Point.Point.Id && row.Latitude == selected.Latitude && row.Longitude == selected.Longitude).ToList()
            : [];

    public static string CoordinatesText(ServiceDueRow row) => row is { Latitude: { } latitude, Longitude: { } longitude }
        ? string.Create(CultureInfo.InvariantCulture, $"{latitude:0.######}, {longitude:0.######}") : string.Empty;

    // The link that opens the intervention dialog of a work point on the page of its beneficiary.
    public static string AddInterventionUrl(int beneficiaryId, int workPointId) => $"/beneficiari/{beneficiaryId}?{AddInterventionQuery}={workPointId}";
    public const string AddInterventionQuery = "adauga-interventie";
}
