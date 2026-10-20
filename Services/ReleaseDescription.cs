using System.Text.Json;

namespace BlazorStoc.Services;

/// <summary>
/// What a release says about itself, printed by "BlazorStoc.dll --describe-release" (no web host, no database) so that tools\release.ps1 and the updater take the
/// version, the schema version and the migrations with their checksums from the application itself and not from a copy: the manifest of a package
/// (tools/BlazorStoc.Updater, docs/PROPUNERE_ACTUALIZARE.md) lists exactly these migrations.
/// </summary>
public static class ReleaseDescription
{
    public const string Argument = "--describe-release";

    public static string ToJson() => JsonSerializer.Serialize(new
    {
        version = AppVersion.Current,
        schemaVersion = MariaSchemaMigrations.All.Max(migration => migration.Version),
        migrations = MariaSchemaMigrations.All.Select(migration => new { version = migration.Version, description = migration.Description, checksum = migration.Checksum })
    }, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

    public static int Print()
    {
        Console.Out.Write(ToJson());
        return 0;
    }
}
