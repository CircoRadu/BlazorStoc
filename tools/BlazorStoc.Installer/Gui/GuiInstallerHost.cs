using System.Diagnostics;
using System.Text;
using BlazorStoc.Updater;

namespace BlazorStoc.Installer.Gui;

/// <summary>
/// The machine for <see cref="InstallerRunner"/> when the installer shows its window: everything about the machine is the console host's (dotnet, runtimes, signatures, work folders, the
/// registry); what is said goes to the log of the window, and the updater runs with its output read line by line into that log (there is no console to inherit).
/// </summary>
public sealed class GuiInstallerHost(ConsoleInstallerHost machine, Action<string> log) : IInstallerHost
{
    public string ExePath => machine.ExePath;
    public bool IsAdministrator => machine.IsAdministrator;
    // The window already asked everything it needs: nothing is asked again at a console that does not exist.
    public bool Interactive => false;
    public string? RegisteredInstallRoot => machine.RegisteredInstallRoot;
    public string? DefaultInstallRoot => machine.DefaultInstallRoot;
    public AdministratorAccount? AskAdministrator() => null;
    public bool VisualCppRuntimeInstalled => machine.VisualCppRuntimeInstalled;

    public void Info(string message) => log(message);
    public void Error(string message) => log("EROARE: " + message);
    public string? ReadLine() => null;
    public int? Elevate(IReadOnlyList<string> arguments) => null;   // the window is elevated when it opens
    public string? FindDotnet(string? requested) => machine.FindDotnet(requested);
    public bool IsSignedByMicrosoft(string file) => machine.IsSignedByMicrosoft(file);
    public Task<string?> RuntimeProblemAsync(string dotnet, CancellationToken cancellationToken) => machine.RuntimeProblemAsync(dotnet, cancellationToken);
    public string CreateWorkFolder() => machine.CreateWorkFolder();
    public void DeleteWorkFolder(string folder) => machine.DeleteWorkFolder(folder);

    public async Task<int> RunPrerequisiteAsync(string file, IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
        await machine.RunPrerequisiteAsync(file, arguments, cancellationToken).ConfigureAwait(false);

    public async Task<int> RunUpdaterAsync(string dotnet, string updaterDll, IReadOnlyList<string> arguments, CancellationToken cancellationToken, IReadOnlyDictionary<string, string>? environment = null)
    {
        var start = new ProcessStartInfo(dotnet)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        start.ArgumentList.Add(updaterDll);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        foreach (var pair in environment ?? new Dictionary<string, string>()) start.Environment[pair.Key] = pair.Value;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("dotnet nu a putut fi pornit.");
        process.StandardInput.Close();   // nobody can answer a question here: the updater then takes its refusing answer
        var output = PumpAsync(process.StandardOutput, false);
        var error = PumpAsync(process.StandardError, true);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        await Task.WhenAll(output, error).ConfigureAwait(false);
        return process.ExitCode;
    }

    private async Task PumpAsync(StreamReader reader, bool isError)
    {
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
            if (line.Length > 0) log(isError ? "EROARE: " + line : line);
    }
}
