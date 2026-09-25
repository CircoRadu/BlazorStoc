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
