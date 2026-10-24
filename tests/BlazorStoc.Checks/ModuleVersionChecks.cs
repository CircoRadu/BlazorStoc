using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BlazorStoc.Checks;

// Versions of the modules (Modules/<Dir>/<Name>Module.cs, Version) are kept in the ledger Modules/module-versions.json: per module directory its version, the hash of
// its sources when that version was set and the history. A module whose sources changed without a new version, or whose version in the code differs from the ledger,
// fails here; tools/module-version.ps1 sets the new version (-Module <Dir> -Bump major|minor|patch -Note "...") and records the hash.
public static class ModuleVersionChecks
{
    public static Task RunAsync(Action<bool, string> check)
    {
        var root = FindApplicationRoot();
        var ledgerPath = root is null ? null : Path.Combine(root, "Modules", "module-versions.json");
        check(ledgerPath is not null && File.Exists(ledgerPath), "Module versions: the ledger Modules/module-versions.json exists");
        if (ledgerPath is null || !File.Exists(ledgerPath)) return Task.CompletedTask;

        using var ledger = JsonDocument.Parse(File.ReadAllText(ledgerPath));
        var modules = ledger.RootElement.GetProperty("modules");
        var directories = Directory.GetDirectories(Path.Combine(root!, "Modules")).Where(directory => Directory.GetFiles(directory, "*Module.cs").Length == 1).ToList();
        check(directories.Count > 0, "Module versions: the module directories are found");

        var notRecorded = new List<string>();
        var changed = new List<string>();
        var mismatched = new List<string>();
        foreach (var directory in directories)
        {
            var name = Path.GetFileName(directory);
            if (!modules.TryGetProperty(name, out var entry)) { notRecorded.Add(name); continue; }
            if (entry.GetProperty("hash").GetString() != HashOf(directory)) changed.Add(name);
            var code = CodeVersion(Directory.GetFiles(directory, "*Module.cs")[0]);
            var history = entry.GetProperty("history");
            var last = history.GetArrayLength() > 0 ? history[history.GetArrayLength() - 1].GetProperty("version").GetString() : null;
            if (code != entry.GetProperty("version").GetString() || last != code) mismatched.Add($"{name} (code {code}, ledger {entry.GetProperty("version").GetString()})");
        }
        var fix = " - run tools/module-version.ps1 -Module <Dir> -Bump minor|patch -Note \"what changed\"";
        check(notRecorded.Count == 0, "Module versions: every module is in the ledger" + (notRecorded.Count > 0 ? ": " + string.Join(", ", notRecorded) + " (tools/module-version.ps1 -Module <Dir> -Init)" : ""));
        check(changed.Count == 0, "Module versions: a module whose sources changed has a new version" + (changed.Count > 0 ? ": " + string.Join(", ", changed) + fix : ""));
        check(mismatched.Count == 0, "Module versions: the version in the code is the one of the ledger" + (mismatched.Count > 0 ? ": " + string.Join(", ", mismatched) : ""));
        var unknown = modules.EnumerateObject().Select(item => item.Name).Where(name => directories.All(directory => Path.GetFileName(directory) != name)).ToList();
        check(unknown.Count == 0, "Module versions: the ledger names only existing modules" + (unknown.Count > 0 ? ": " + string.Join(", ", unknown) : ""));
        return Task.CompletedTask;
    }

    // The version written in the Version property of the module class: new(1, 2, 3) -> "1.2.3".
    private static string CodeVersion(string moduleFile)
    {
        var match = Regex.Match(File.ReadAllText(moduleFile), @"Version\s+Version\s*\{\s*get;\s*\}\s*=\s*new\((\d+),\s*(\d+),\s*(\d+)\)");
        return match.Success ? $"{match.Groups[1].Value}.{match.Groups[2].Value}.{match.Groups[3].Value}" : "?";
    }

    // SHA-256 over the files of the directory (without bin and obj), by relative path in ordinal order: "path\n" + content with line ends normalised to LF + "\n".
    // tools/module-version.ps1 computes the same value.
    public static string HashOf(string directory)
    {
        var files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(directory, file).Replace('\\', '/'))
            .Where(path => !path.StartsWith("bin/", StringComparison.Ordinal) && !path.StartsWith("obj/", StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal).ToList();
        using var sha = SHA256.Create();
        var builder = new StringBuilder();
        foreach (var path in files)
        {
            builder.Append(path).Append('\n');
            builder.Append(File.ReadAllText(Path.Combine(directory, path), new UTF8Encoding(false)).Replace("\r\n", "\n")).Append('\n');
        }
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }

    private static string? FindApplicationRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "BlazorStoc.csproj"))) return directory.FullName;
        return null;
    }
}
