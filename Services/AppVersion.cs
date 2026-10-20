using System.Reflection;

namespace BlazorStoc.Services;

/// <summary>
/// The version of the application as a whole (the &lt;Version&gt; of the project, set by tools\release.ps1 for a release), not the version of one module
/// (IModule.Version). Shown in Setari -> Module, in /health and in the schema ledger (schema_migrations.applied_by_version).
/// </summary>
public static class AppVersion
{
    /// <summary>"0.5.0": the informational version without the build metadata that follows "+".</summary>
    public static string Current { get; } = Read();

    private static string Read()
    {
        var assembly = typeof(AppVersion).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var text = informational ?? assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        var plus = text.IndexOf('+', StringComparison.Ordinal);
        return plus > 0 ? text[..plus] : text;
    }
}
