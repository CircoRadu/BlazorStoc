using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace BlazorStoc.Updater;

/// <summary>The real machine for <see cref="InstallerRunner"/>: the console, Windows (administrator check and elevation) and the dotnet that runs the updater.</summary>
public sealed class ConsoleInstallerHost : IInstallerHost
{
    private bool elevatedChild;

    public string ExePath { get; } = Environment.ProcessPath ?? throw new InvalidOperationException("The path of this program is not known.");
    public bool IsAdministrator => Acl.IsAdministrator();
    public bool Interactive => !Console.IsInputRedirected;
    public string? RegisteredInstallRoot => Environment.GetEnvironmentVariable("BLAZORSTOC_INSTALL_ROOT") is { Length: > 0 } fromEnvironment ? fromEnvironment : new RegistryInstallLocation().Read();

    public string? DefaultInstallRoot => InstallerArguments.DefaultInstallRoot;

    public AdministratorAccount? AskAdministrator() => Interactive ? new AdministratorPrompt(() => Console.ReadKey(intercept: true), Console.Out).Ask() : null;

    public void Info(string message) => Console.Out.WriteLine(message);
    public void Error(string message) => Console.Error.WriteLine(message);
    public string? ReadLine() => Console.ReadLine();

    public int? Elevate(IReadOnlyList<string> arguments)
    {
        try
        {
            var start = new ProcessStartInfo(ExePath) { UseShellExecute = true, Verb = "runas", Arguments = string.Join(' ', arguments.Select(Quote)), WorkingDirectory = Environment.CurrentDirectory };
            using var process = Process.Start(start);
            if (process is null) return null;
            elevatedChild = true;
            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Win32Exception) { return null; }
    }

    public string? FindDotnet(string? requested)
    {
        if (requested is not null) return File.Exists(requested) ? Path.GetFullPath(requested) : null;
        var candidates = new List<string>();
        if (Environment.GetEnvironmentVariable("DOTNET_ROOT") is { Length: > 0 } root) candidates.Add(Path.Combine(root, "dotnet.exe"));
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe"));
        candidates.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).Select(folder => Path.Combine(folder.Trim('"'), "dotnet.exe")));
        return candidates.FirstOrDefault(File.Exists);
    }

    public async Task<string?> RuntimeProblemAsync(string dotnet, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(dotnet, "--list-runtimes") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        try
        {
            using var process = Process.Start(start) ?? throw new InvalidOperationException("dotnet could not be started.");
            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var missing = new[] { "Microsoft.NETCore.App", "Microsoft.AspNetCore.App" }.Where(runtime => !output.Contains(runtime + " 9.", StringComparison.Ordinal)).ToArray();
            return missing.Length == 0 ? null
                : $"The .NET 9 runtime ({string.Join(" and ", missing)} 9.x) is not installed on this machine; the application and this updater need it. Install it and run the installer again.";
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException) { return $"{dotnet} could not be run: {exception.Message}"; }
    }

    public bool VisualCppRuntimeInstalled
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return true;
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64");
            return key?.GetValue("Installed") is 1 && key.GetValue("Major") is int major && major >= 14;
        }
    }

    public bool IsSignedByMicrosoft(string file)
    {
        // Authenticode: the signature must be valid and the signer Microsoft Corporation (Get-AuthenticodeSignature does the checking Windows itself does).
        var start = new ProcessStartInfo("powershell.exe") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        // The path goes in the environment: arguments after -Command are part of the command text, not $args.
        start.Environment["BSU_PREREQUISITE"] = file;
        foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", "$s = Get-AuthenticodeSignature -LiteralPath $env:BSU_PREREQUISITE; if ($s.Status -eq 'Valid' -and $s.SignerCertificate.Subject -match 'O=Microsoft Corporation') { 'SIGNED-BY-MICROSOFT' }" }) start.ArgumentList.Add(argument);
        try
        {
            using var process = Process.Start(start);
            if (process is null) return false;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return output.Contains("SIGNED-BY-MICROSOFT", StringComparison.Ordinal);
        }
        catch (Win32Exception) { return false; }
    }

    public async Task<int> RunPrerequisiteAsync(string file, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(file) { UseShellExecute = false };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"{Path.GetFileName(file)} could not be started.");
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return process.ExitCode;
    }

    public string CreateWorkFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "blazorstoc-installer-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(folder);
        // What is run from here runs with the rights of an administrator: no other user may put a file in it.
        Acl.RestrictToAdministrators(folder);
        return folder;
    }

    public void DeleteWorkFolder(string folder)
    {
        try { Directory.Delete(folder, recursive: true); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { Error($"The temporary folder {folder} could not be removed: {exception.Message}"); }
    }

    public async Task<int> RunUpdaterAsync(string dotnet, string updaterDll, IReadOnlyList<string> arguments, CancellationToken cancellationToken, IReadOnlyDictionary<string, string>? environment = null)
    {
        // No redirection: the updater writes to this console and asks its question (an unknown signer) on it.
        var start = new ProcessStartInfo(dotnet) { UseShellExecute = false };
        start.ArgumentList.Add(updaterDll);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        foreach (var pair in environment ?? new Dictionary<string, string>()) start.Environment[pair.Key] = pair.Value;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("dotnet could not be started.");
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return process.ExitCode;
    }

    /// <summary>When Windows opened this console for the installer (a double click) the window would close with the result in it: wait for Enter. Not after the elevated copy ran (it waited itself).</summary>
    public void PauseIfAlone()
    {
        if (elevatedChild || !Interactive || !OperatingSystem.IsWindows() || !OwnsConsole()) return;
        Console.Out.WriteLine();
        Console.Out.Write("Press Enter to close this window. ");
        Console.ReadLine();
    }

    /// <summary>True when this process is the only one on its console: Windows opened the console for it (a double click), no shell is waiting behind it.</summary>
    public bool OwnsConsole()
    {
        if (!OperatingSystem.IsWindows()) return false;
        var processes = new uint[4];
        return GetConsoleProcessList(processes, (uint)processes.Length) == 1;
    }

    // One argument the way CommandLineToArgvW reads it back.
    private static string Quote(string argument)
    {
        if (argument.Length > 0 && argument.IndexOfAny([' ', '\t', '"']) < 0) return argument;
        var text = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var character in argument)
        {
            if (character == '\\') { backslashes++; continue; }
            if (character == '"') text.Append('\\', backslashes * 2 + 1).Append('"');
            else text.Append('\\', backslashes).Append(character);
            backslashes = 0;
        }
        return text.Append('\\', backslashes * 2).Append('"').ToString();
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetConsoleProcessList(uint[] processList, uint processCount);
}
