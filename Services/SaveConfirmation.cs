namespace BlazorStoc.Services;

// Builds the field summary shown before a save that modifies existing data (Task 5).
// Only fields whose value really changes are listed; secrets such as passwords are never passed in as values.
public static class SaveSummary
{
    public const string Empty = "(gol)";
    public const int MaximumValueLength = 300;

    public static IReadOnlyList<AuditChange> Changed(params AuditChange[] changes) =>
        changes.Where(change => !string.Equals(change.Before, change.After, StringComparison.Ordinal))
            .Select(change => new AuditChange(change.Field, Display(change.Before), Display(change.After))).ToArray();

    public static string Display(string? value)
    {
        var text = value?.Trim() ?? string.Empty;
        if (text.Length == 0) return Empty;
        return text.Length <= MaximumValueLength ? text : string.Concat(text.AsSpan(0, MaximumValueLength), "…");
    }
}
