using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace BlazorStoc.Updater;

/// <summary>
/// The scheduled task through which the application starts the updater (tools/updater-task.ps1, registered by "init" as an administrator). Its name is derived from the folder of the
/// installation, so the application finds it with nothing but the folder it runs from; the same rule is in the application (Services/Update/UpdateInstallation.cs) and a check
/// compares the two.
/// </summary>
public static class UpdaterTask
{
    public static string NameFor(string installRoot)
    {
        var normalised = Path.GetFullPath(installRoot).TrimEnd('\\', '/').ToLowerInvariant();
        return "BlazorStocUpdater-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalised)))[..8].ToLowerInvariant();
    }

    /// <summary>The dotnet that runs this updater (the task must use the same), else the one on the path.</summary>
    public static string DotnetPath(UpdaterSettings settings)
    {
        var own = Environment.ProcessPath;
        return own is not null && Path.GetFileNameWithoutExtension(own).Equals("dotnet", StringComparison.OrdinalIgnoreCase) ? own : settings.Dotnet;
    }

    public static Task<string> RegisterAsync(InstallLayout layout, UpdaterSettings settings, CancellationToken cancellationToken = default) =>
        RunAsync(layout, ["-Action", "register", "-TaskName", NameFor(layout.Root), "-InstallRoot", layout.Root, "-Dotnet", DotnetPath(settings),
            "-UpdaterDll", Path.Combine(layout.UpdaterFolder, "BlazorStoc.Updater.dll")], cancellationToken);

    public static Task<string> RemoveAsync(InstallLayout layout, CancellationToken cancellationToken = default) =>
        RunAsync(layout, ["-Action", "remove", "-TaskName", NameFor(layout.Root)], cancellationToken);

    public static Task<string> StatusAsync(InstallLayout layout, CancellationToken cancellationToken = default) =>
        RunAsync(layout, ["-Action", "status", "-TaskName", NameFor(layout.Root)], cancellationToken);

    private static async Task<string> RunAsync(InstallLayout layout, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var script = Path.Combine(layout.UpdaterFolder, "updater-task.ps1");
        if (!File.Exists(script)) throw new InvalidOperationException($"{script} is missing (run \"init\" first).");
        var start = new ProcessStartInfo("powershell.exe") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script }) start.ArgumentList.Add(argument);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("powershell.exe could not be started.");
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var text = (await output.ConfigureAwait(false) + await error.ConfigureAwait(false)).Trim();
        if (process.ExitCode != 0) throw new InvalidOperationException($"The scheduled task could not be changed: {text}");
        return text;
    }
}
