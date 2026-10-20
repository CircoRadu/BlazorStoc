namespace BlazorStoc.Startup;

/// <summary>
/// The folder that holds what the application keeps on disk outside the repository: the database packages and the other assets, the keys that protect the
/// saved passwords, the override file of Setari -> Baza de date and the files of the private accounts. App:DataRoot decides; without it the folder of the
/// machine (%ProgramData%\BlazorStoc, the same for every user and for a Windows service) is used once it exists, and the per-user folder of the earlier
/// versions (%LOCALAPPDATA%\BlazorStoc-MariaDB) while it has not been moved (tools\migrate-data-root.ps1). A service runs under an account that has no
/// profile of the user, so the per-user folder is wrong for it; the machine folder is the same for the application, its worker processes and the scripts.
/// </summary>
public static class AppDataRoot
{
    public const string Setting = "App:DataRoot";

    public static string MachineFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BlazorStoc");

    public static string LegacyUserFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BlazorStoc-MariaDB");

    public static string Resolve(IConfiguration configuration)
    {
        if (configuration[Setting] is { Length: > 0 } configured) return Path.GetFullPath(configured);
        return MachineFolderIsInUse() || !Directory.Exists(LegacyUserFolder) ? MachineFolder : LegacyUserFolder;
    }

    /// <summary>True while the data are still in the per-user folder: the application works as before and the start-up says to move them.</summary>
    public static bool UsesLegacyFolder(IConfiguration configuration) =>
        configuration[Setting] is not { Length: > 0 } && !MachineFolderIsInUse() && Directory.Exists(LegacyUserFolder);

    public static string Combine(IConfiguration configuration, params string[] parts) => Path.Combine([Resolve(configuration), .. parts]);

    /// <summary>
    /// A settings file the application keeps as JSON (the ANAF and map configurations): &lt;data root&gt;\data\&lt;name&gt;, outside the folder of the code, so that an update
    /// of the application (a new folder of code, see docs/PROPUNERE_ACTUALIZARE.md) never leaves it behind. The earlier versions kept it in the "data" folder of the working
    /// directory: while the new file does not exist, that one is copied (not moved - the old file stays as a copy) the first time; if the copy fails the old file keeps being used.
    /// </summary>
    public static string DataFile(IConfiguration configuration, string name, string legacyFolder = "data")
    {
        var path = Combine(configuration, "data", name);
        if (File.Exists(path)) return path;
        var legacy = Path.GetFullPath(Path.Combine(legacyFolder, name));
        if (!File.Exists(legacy)) return path;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.Copy(legacy, path);
            return path;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return legacy; }
    }

    /// <summary>The folder of the rolling file log when Logging:File:Directory does not say otherwise: &lt;data root&gt;\logs (it survives an update of the code folder).</summary>
    public static string DefaultLogFolder(IConfiguration configuration) => Combine(configuration, "logs");

    // The machine folder counts once it has its assets folder (made by the move script or by the first run on a new machine), so an empty folder
    // that something else created does not take the application away from the data it already has.
    private static bool MachineFolderIsInUse() => Directory.Exists(Path.Combine(MachineFolder, "assets"));
}
