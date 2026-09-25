namespace BlazorStoc.Services;

// State of the project list on a beneficiary page (filter text and page). It lives in the address of the page
// (replaced in place, so the browser's Back button restores it) and is remembered per session so that the
// "back" links of the project pages return to the same view (TODO Task 2 / Subtask 2.3).
public sealed record BeneficiaryProjectListState(string Query, int Page)
{
    public const string QueryParameter = "q";
    public const string PageParameter = "pagina";
    public static BeneficiaryProjectListState Default { get; } = new("", 1);

    public static BeneficiaryProjectListState From(string? query, int? page) =>
        new((query ?? "").Trim(), Math.Max(1, page ?? 1));

    public string QueryString()
    {
        var parts = new List<string>();
        if (Query.Length > 0) parts.Add($"{QueryParameter}={Uri.EscapeDataString(Query)}");
        if (Page > 1) parts.Add($"{PageParameter}={Page}");
        return parts.Count == 0 ? "" : "?" + string.Join('&', parts);
    }

    public string Url(int beneficiaryId) => BeneficiaryUrl(beneficiaryId) + QueryString();

    public static string BeneficiaryUrl(int beneficiaryId) => $"/beneficiari/{beneficiaryId}";
}

// Filters, page size and page of the audit journal (TODO Task 3). They live in the address of the journal (replaced in
// place, so the browser's Back button from an object page restores them). Only values that differ from the defaults
// are written; unknown or malformed values fall back to the defaults.
public sealed record AuditListState(string Query, string Entity, string Action, string Actor, string? DateKey, int PageSize, int Page)
{
    public const int DefaultPageSize = 10;
    public static readonly IReadOnlyList<int> PageSizes = [10, 20, 50, 0];
    public static AuditListState Default { get; } = new("", "", "", "", null, DefaultPageSize, 1);

    public static AuditListState From(string? query, string? entity, string? action, string? actor, string? dateKey,
        int? pageSize, int? page) =>
        new((query ?? "").Trim(), (entity ?? "").Trim(), (action ?? "").Trim(), (actor ?? "").Trim(),
            ValidDateKey(dateKey), pageSize is { } size && PageSizes.Contains(size) ? size : DefaultPageSize, Math.Max(1, page ?? 1));

    public static string? ValidDateKey(string? value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var date) ? date.ToString("yyyy-MM-dd") : null;

    public static string DateLabel(string dateKey) =>
        DateOnly.ParseExact(dateKey, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture).ToString("dd.MM.yyyy");

    public string Url()
    {
        var parts = new List<string>();
        void Add(string name, string value) { if (value.Length > 0) parts.Add($"{name}={Uri.EscapeDataString(value)}"); }
        Add("q", Query);
        Add("tip", Entity);
        Add("operatie", Action);
        Add("operator", Actor);
        Add("data", DateKey ?? "");
        if (PageSize != DefaultPageSize) parts.Add($"pe-pagina={PageSize}");
        if (Page > 1) parts.Add($"pagina={Page}");
        return "/jurnal" + (parts.Count == 0 ? "" : "?" + string.Join('&', parts));
    }
}

public sealed class ListNavigationContext
{
    private readonly Dictionary<int, BeneficiaryProjectListState> beneficiaryLists = new();

    public void RememberBeneficiaryProjects(int beneficiaryId, BeneficiaryProjectListState state)
    {
        if (state == BeneficiaryProjectListState.Default) beneficiaryLists.Remove(beneficiaryId);
        else beneficiaryLists[beneficiaryId] = state;
    }

    // Address of the beneficiary page as the user last left it; the plain address when nothing was filtered or paged.
    public string BeneficiaryUrl(int beneficiaryId) =>
        (beneficiaryLists.TryGetValue(beneficiaryId, out var state) ? state : BeneficiaryProjectListState.Default).Url(beneficiaryId);
}
