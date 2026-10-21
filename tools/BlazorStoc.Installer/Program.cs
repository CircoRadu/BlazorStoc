using BlazorStoc.Updater;

// BlazorStoc-Actualizare-<version>.exe: the launcher; the package and the updater are appended to this file (InstallerPayload). --help lists what it can do.
var host = new ConsoleInstallerHost();
// A double click (no arguments, a console of its own) or --gui: the window. Anything else is the console installer.
if (BlazorStoc.Installer.Gui.GuiProgram.Wanted(args, host)) return BlazorStoc.Installer.Gui.GuiProgram.Run(args, host);
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
int code;
try { code = await new InstallerRunner(host).RunAsync(args, cancellation.Token); }
catch (OperationCanceledException) { host.Error("Interrupted."); code = InstallerExit.Cancelled; }
catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException)
{
    host.Error($"The installer stopped: {exception.Message}");
    code = 1;
}
if (!args.Contains("--no-pause", StringComparer.OrdinalIgnoreCase)) host.PauseIfAlone();
return code;
