namespace BlazorStoc.Services.Update;

/// <summary>One version of Assets/Data/changelog.txt: what it brings, one line per change.</summary>
public sealed record ChangelogVersion(string Version, string Date, IReadOnlyList<string> Items);

/// <summary>
/// The changelog of the update packages: Assets/Data/changelog.txt, embedded in the application ("## version | date", then one "- " line per change, newest
/// version first). The window of Setari -> Actualizare -> Istoric shows the versions an update went through; tools\release.ps1 takes the notes of a package from
/// the same file and refuses a package whose version has no section.
/// </summary>
public static class UpdateChangelog
{
    private static readonly Lazy<IReadOnlyList<ChangelogVersion>> All = new(() => Parse(BlazorStoc.Services.EmbeddedData.ReadText("changelog.txt")));

    public static IReadOnlyList<ChangelogVersion> Versions => All.Value;

    public static IReadOnlyList<ChangelogVersion> Parse(string text)
    {
        var versions = new List<ChangelogVersion>();
        string? version = null, date = "";
        var items = new List<string>();
        void Close()
        {
            if (version is not null) versions.Add(new ChangelogVersion(version, date ?? "", items.ToArray()));
            items = [];
        }
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r').Trim();
            if (line.Length == 0 || line.StartsWith('#') && !line.StartsWith("##", StringComparison.Ordinal)) continue;
            if (line.StartsWith("##", StringComparison.Ordinal))
            {
                Close();
                var heading = line.TrimStart('#').Trim();
                var bar = heading.IndexOf('|');
                version = (bar < 0 ? heading : heading[..bar]).Trim();
                date = bar < 0 ? "" : heading[(bar + 1)..].Trim();
            }
            else if (version is not null && line.StartsWith('-')) items.Add(line.TrimStart('-').Trim());
        }
        Close();
        return versions;
    }

    /// <summary>The versions an update went through, newest first: after "from" up to and including "to" (only "to" when the origin is unknown). Empty when "to" is not in the changelog.</summary>
    public static IReadOnlyList<ChangelogVersion> Between(string? from, string? to) => Between(Versions, from, to);

    public static IReadOnlyList<ChangelogVersion> Between(IReadOnlyList<ChangelogVersion> versions, string? from, string? to)
    {
        if (!Version.TryParse(to, out var upper) || !versions.Any(entry => Version.TryParse(entry.Version, out var known) && known == upper)) return [];
        var lower = Version.TryParse(from, out var parsedFrom) && parsedFrom < upper ? parsedFrom : null;
        return versions
            .Select(entry => (entry, number: Version.TryParse(entry.Version, out var parsed) ? parsed : null))
            .Where(pair => pair.number is not null && pair.number <= upper && (lower is null ? pair.number == upper : pair.number > lower))
            .OrderByDescending(pair => pair.number)
            .Select(pair => pair.entry)
            .ToArray();
    }

    /// <summary>The notes of one version as plain text (what the administrator reads before an update), or null when the version has no section.</summary>
    public static string? NotesFor(string version) => NotesFor(Versions, version);

    public static string? NotesFor(IReadOnlyList<ChangelogVersion> versions, string version)
    {
        var entry = versions.FirstOrDefault(candidate => candidate.Version == version);
        return entry is null || entry.Items.Count == 0 ? null : string.Join('\n', entry.Items.Select(item => "- " + item));
    }
}
