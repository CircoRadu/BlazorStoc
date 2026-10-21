using System.Runtime.InteropServices;
using BlazorStoc.Updater;

namespace BlazorStoc.Installer.Gui;

/// <summary>
/// The window mode of the launcher: a double click on the file (no arguments, a console of its own) or --gui. The window asks for administrator rights when it opens (Windows asks once),
/// then collects the answers and runs the same <see cref="InstallerRunner"/> the console runs. --gui-preview shows the window with a made-up installation, for looking at it (nothing is
/// installed). Options given next to --gui (for example --install-root, --port, --no-service) fill the window or go on to the installer.
/// </summary>
public static class GuiProgram
{
    public static bool Wanted(string[] args, ConsoleInstallerHost host) =>
        args.Contains("--gui", StringComparer.OrdinalIgnoreCase) || args.Contains("--gui-preview", StringComparer.OrdinalIgnoreCase) || args.Length == 0 && host.OwnsConsole();

    public static int Run(string[] args, ConsoleInstallerHost machine)
    {
        var preview = args.Contains("--gui-preview", StringComparer.OrdinalIgnoreCase);
        var presets = args.Where(argument => !argument.Equals("--gui", StringComparison.OrdinalIgnoreCase) && !argument.Equals("--gui-preview", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (machine.OwnsConsole()) FreeConsole();   // the console Windows opened for a double click is not wanted behind the window

        if (!preview && !machine.IsAdministrator)
        {
            // The window is always elevated: everything it does needs it. Windows asks once; the elevated copy shows the window.
            return machine.Elevate(["--gui", .. presets]) ?? InstallerExit.NotAdministrator;
        }

        var code = 0;
        var thread = new Thread(() => code = ShowWindow(presets, preview, machine));
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return code;
    }

    private static int ShowWindow(string[] presets, bool preview, ConsoleInstallerHost machine)
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        InstallerPayload? payload = null;
        try { payload = InstallerPayload.TryOpen(machine.ExePath); }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(exception.Message, "Instalare BlazorStoc", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return InstallerExit.DamagedPayload;
        }
        if (payload is null && !preview)
        {
            MessageBox.Show("Acest fișier nu conține un pachet. Folosește fișierul făcut de release.ps1: BlazorStoc-Actualizare-<versiune>.exe.", "Instalare BlazorStoc", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return InstallerExit.NoPayload;
        }
        using (payload)
        {
            var version = payload?.Info.Version ?? "0.0.0";
            var root = presets.SkipWhile(argument => !argument.Equals("--install-root", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault() ?? machine.RegisteredInstallRoot;
            string? current = null;
            if (root is not null && File.Exists(Path.Combine(root, "updater", "settings.json"))) current = new InstallLayout(root).Current ?? "-";
            else if (root is not null && machine.RegisteredInstallRoot is null) root = null;
            var state = new ServerState(version, current is null ? null : root, current is "-" ? null : current, InstallerArguments.DefaultInstallRoot);
            // An installation made but without a version (init without install) is a new installation for the window: the version is installed in the same folder.
            if (current is "-") state = state with { InstallRoot = root, CurrentVersion = null };
            InstallAction action = preview ? PreviewAsync : async (arguments, log, token) => await new InstallerRunner(new GuiInstallerHost(machine, log)).RunAsync(arguments, token).ConfigureAwait(false);
            using var form = new InstallerForm(state, action, presets);
            Application.Run(form);
            return form.ExitCode ?? 0;
        }
    }

    // What the window shows with --gui-preview: a made-up run (nothing is installed).
    private static async Task<int> PreviewAsync(IReadOnlyList<string> arguments, Action<string> log, CancellationToken cancellationToken)
    {
        log("Previzualizare: nu se instalează nimic. Argumentele installerului ar fi:");
        log("  " + string.Join(' ', arguments.Select(argument => argument.Contains(' ') ? $"\"{argument}\"" : argument)));
        foreach (var line in new[] { "Application port: 5087 is taken on this server, 5089 is free and will be used.", "Database port: 3307 (free).", "Installing dotnet-runtime-9.0.20-win-x64.exe (needed by the application; this takes a minute) ...",
                     "[database] ready: MariaDB answers on 127.0.0.1:3307", "Done: BlazorStoc 0.5.0 is installed and answering.", "Administrator account made in the users of the application." })
        {
            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            log(line);
        }
        return 0;
    }

    [DllImport("kernel32.dll")]
    private static extern bool FreeConsole();
}
