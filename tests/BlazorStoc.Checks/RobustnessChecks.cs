using BlazorStoc.Services;
using Microsoft.Extensions.Logging;

namespace BlazorStoc.Checks;

// Robustness: the rolling file log (written, errors with their stack trace, old files deleted) and the helper for tasks nobody awaits.
public static class RobustnessChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        Console.WriteLine("=== Robustness ===");
        var directory = Path.Combine(Path.GetTempPath(), "blazorstoc-log-check-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            var old = Path.Combine(directory, $"blazorstoc-{DateTime.Now.AddDays(-30):yyyyMMdd}.log");
            var recent = Path.Combine(directory, $"blazorstoc-{DateTime.Now.AddDays(-2):yyyyMMdd}.log");
            File.WriteAllText(old, "old"); File.WriteAllText(recent, "recent");
            using var provider = new RollingFileLoggerProvider(directory, 14);
            var logger = provider.CreateLogger("Check");
            logger.LogInformation("an information line that is not kept");
            logger.LogError(new InvalidOperationException("boom"), "something failed");
            var today = Path.Combine(directory, $"blazorstoc-{DateTime.Now:yyyyMMdd}.log");
            var text = File.Exists(today) ? File.ReadAllText(today) : "";
            check(text.Contains("[Error] Check: something failed") && text.Contains("InvalidOperationException: boom") && !text.Contains("not kept"),
                "Robustness: the file log keeps warnings and errors with the stack trace and leaves out information lines");
            check(!File.Exists(old) && File.Exists(recent), "Robustness: log files older than the retention are deleted, recent ones are kept");

        // Every page wraps its content in the page boundary (an exception of one of its components must not close the circuit), and no page
        // starts a task without awaiting it through "_ =" (the helper logs the failure).
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "BlazorStoc.csproj"))) root = Path.GetDirectoryName(root);
        check(root is not null, "Robustness: the project folder is found for the page scan");
        if (root is not null)
        {
            var pages = Directory.GetFiles(Path.Combine(root, "Components", "Pages"), "*.razor")
                .Select(file => (file, text: File.ReadAllText(file))).Where(item => item.text.Contains("\n@page ") || item.text.StartsWith("@page ") || item.text.Contains("\n@page\t")).ToList();
            var without = pages.Where(item => !item.text.Contains("<PageBoundary>") || !item.text.Contains("</PageBoundary>")).Select(item => Path.GetFileName(item.file)).ToList();
            check(pages.Count >= 30 && without.Count == 0, $"Robustness: every page is wrapped in the page boundary ({pages.Count} pages; missing: {string.Join(", ", without)})");
            var loose = Directory.GetFiles(Path.Combine(root, "Components"), "*.razor", SearchOption.AllDirectories)
                .Where(file => File.ReadAllText(file).Contains("        _ = ") && File.ReadAllText(file).Contains("PeriodicallyAsync()")).Select(Path.GetFileName).ToList();
            check(loose.Count == 0, $"Robustness: periodic page tasks are started through FireAndForget ({string.Join(", ", loose)})");
        }

            var faulty = Task.Run(async () => { await Task.Yield(); throw new InvalidOperationException("background failure"); });
            faulty.FireAndForget(logger, "check task");
            for (var i = 0; i < 50 && !File.ReadAllText(today).Contains("background failure"); i++) await Task.Delay(50);
            check(File.ReadAllText(today).Contains("Background task failed: check task") && File.ReadAllText(today).Contains("background failure"),
                "Robustness: an exception in a task started without awaiting it is logged instead of lost");
            var before = File.ReadAllText(today);
            Task.FromCanceled(new CancellationToken(true)).FireAndForget(logger, "cancelled task");
            await Task.Delay(100);
            check(File.ReadAllText(today) == before, "Robustness: the cancellation of a background task is not logged as an error");
        }
        finally { try { Directory.Delete(directory, true); } catch (IOException) { } }
    }
}
