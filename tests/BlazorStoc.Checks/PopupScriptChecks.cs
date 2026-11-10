namespace BlazorStoc.Checks;

// The script of the bar of the popups (wwwroot/bug-report.js) watches the whole page and places the bar again at every change. classList.add on a class the element already has still writes the
// attribute: the observer saw a change, placed the bar, wrote it again... without end (237,000 changes in 3 seconds, 6-second freezes, lost connections) while a popup was open (found 10.11.2026).
public static class PopupScriptChecks
{
    public static Task RunAsync(Action<bool, string> check)
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "BlazorStoc.csproj"))) root = Path.GetDirectoryName(root.TrimEnd(Path.DirectorySeparatorChar));
        check(root is not null, "Popup script: the project folder is found");
        if (root is null) return Task.CompletedTask;
        var script = File.ReadAllText(Path.Combine(root, "wwwroot", "bug-report.js"));
        check(script.Contains("if (!button.classList.contains(\"popup-close-moved\")) button.classList.add(\"popup-close-moved\");", StringComparison.Ordinal)
              && !script.Contains("for (const button of wanted) button.classList.add(", StringComparison.Ordinal),
            "Popup script: the class of the moved close button is written only when the button does not have it (otherwise the observer of the bar loops without end while a popup is open)");
        return Task.CompletedTask;
    }
}
