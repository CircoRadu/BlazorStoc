namespace BlazorStoc.Services;

// Subtask 3.3 (Task 3): confirmation word for the restore flow, same strict-comparison rule as
// DeleteConfirmationRules ("sterge") but its own word ("confirma") and no reason step - the popup only warns and
// asks for the word, it never collects a motive like the delete flow does.
public static class RestoreConfirmationRules
{
    public const string ConfirmationWord = "confirma";

    public static bool IsConfirmationValid(string? value) =>
        string.Equals(value?.Trim(), ConfirmationWord, StringComparison.Ordinal);
}
