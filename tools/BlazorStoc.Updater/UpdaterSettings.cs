using System.Text.Json;

namespace BlazorStoc.Updater;

/// <summary>
/// updater/settings.json of an installation. Port: the port the application listens on when its page has saved none (the one given to the supervisor); a port saved from Setari -> Baza de
/// date -> Conexiune is read from the data root and tried first. MariaRoot: where the MariaDB distribution is (its tools are found for the backup and the supervisor); null = the
/// application finds its tools by itself. Environment: variables given to every process the updater starts for the application (backup, migrations, restoration).
/// </summary>
public sealed record UpdaterSettings(int Port = 5087, string? MariaRoot = null, int HealthTimeoutSeconds = 120, int KeepReleases = 3, string Dotnet = "dotnet",
    string? SupervisorScript = null, Dictionary<string, string>? Environment = null)
{
    public static UpdaterSettings Load(InstallLayout layout)
    {
        if (!File.Exists(layout.SettingsFile)) return new UpdaterSettings();
        try { return JsonSerializer.Deserialize<UpdaterSettings>(File.ReadAllText(layout.SettingsFile), InstallLayout.Json) ?? new UpdaterSettings(); }
        catch (JsonException) { throw new InvalidOperationException($"{layout.SettingsFile} cannot be read."); }
    }

    public void Save(InstallLayout layout)
    {
        Directory.CreateDirectory(layout.UpdaterFolder);
        File.WriteAllText(layout.SettingsFile, JsonSerializer.Serialize(this, InstallLayout.Json));
    }

    /// <summary>The data root of the application the way it decides it (App__DataRoot, else the folder of the machine): where the saved port and the data are.</summary>
    public static string DataRoot() =>
        System.Environment.GetEnvironmentVariable("App__DataRoot") is { Length: > 0 } configured ? Path.GetFullPath(configured)
            : Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.CommonApplicationData), "BlazorStoc");

    /// <summary>The ports to ask /health on: the one saved from the application's own page first (when there is one), then the configured one.</summary>
    public IReadOnlyList<int> Ports()
    {
        var ports = new List<int>();
        try
        {
            var file = Path.Combine(DataRoot(), "database-connection.override.json");
            if (File.Exists(file) && JsonDocument.Parse(File.ReadAllText(file)).RootElement.TryGetProperty("appPort", out var saved) && saved.TryGetInt32(out var port) && port > 0) ports.Add(port);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException) { }
        if (!ports.Contains(Port)) ports.Add(Port);
        return ports;
    }
}
