namespace BlazorStoc.Services;

// The reason of an edit offered by the forms of existing objects: the summary of the changes the user made in the form (one change on each
// line, "Field: old → new"), or a text the user writes (ChangeReasonField chooses between them with radio buttons). The summary is built
// again from the form at every render, so it follows the user's work and drops a change that was put back to its original value.
public static class ChangeReasonSummary
{
    public const int ValueLength = 40;
    public const string NoChangesMessage = "Nu ai făcut nicio modificare.";

    public static string Short(string? value)
    {
        var text = SaveSummary.Display(value);
        return text.Length <= ValueLength ? text : string.Concat(text.AsSpan(0, ValueLength), "…");
    }

    public static string Build(IEnumerable<AuditChange> changes)
    {
        var lines = changes.Where(change => !string.Equals((change.Before ?? "").Trim(), (change.After ?? "").Trim(), StringComparison.Ordinal))
            .Select(change => $"{change.Field}: {Short(change.Before)} → {Short(change.After)}");
        var text = string.Join("\n", lines);
        return text.Length <= ChangeReasonRules.MaximumLength ? text : string.Concat(text.AsSpan(0, ChangeReasonRules.MaximumLength - 1), "…");
    }

    // The reason that is saved: the generated summary or the written text, as chosen.
    public static string Chosen(bool auto, string summary, string written) => auto ? summary : written;

    public static string? ValidationError(bool auto, string summary, string written) =>
        auto && summary.Length == 0 ? NoChangesMessage : ChangeReasonRules.ValidationError(Chosen(auto, summary, written));
}
