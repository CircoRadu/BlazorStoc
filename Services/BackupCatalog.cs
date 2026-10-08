using System.Globalization;
using System.Text.RegularExpressions;

namespace BlazorStoc.Services;

/// <summary>One row of the table of available backups: a package on the server, on the NAS, or on both (matched by file name).</summary>
public sealed record BackupCatalogRow(string FileName, BackupKind Kind, long SizeBytes, DateTime CreatedAtUtc, string OperatorName, string OperatorRole,
    bool OnLocal, bool OnNas, bool NasSizeDiffers, string TimeSource = TrustedClock.SourceVerified)
{
    /// <summary>Only a local inventory-pickup package, or one with an unverified time, can be deleted from the application; nothing is ever deleted on the NAS.</summary>
    public bool CanDelete => OnLocal && (Kind == BackupKind.InventoryPickup || TimeSource == TrustedClock.SourceUnverified);
    public bool NasOnly => OnNas && !OnLocal;
}

public static class BackupCatalog
{
    private static readonly Regex NameShape = new(@"^(?<label>.+?) (?<stamp>\d{2}\.\d{2}\.\d{4} \d{2}-\d{2}-\d{2}) (?<role>\S+) (?<name>.+)\.zip$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>The local packages and, when the share could be read, the ones on the NAS. A file of the share that is not named like a package of the application is ignored.</summary>
    public static IReadOnlyList<BackupCatalogRow> Merge(IReadOnlyList<BackupPackage> local, IReadOnlyList<NasShareFile>? share)
    {
        var rows = new List<BackupCatalogRow>();
        var nas = (share ?? []).ToDictionary(file => file.FileName, StringComparer.Ordinal);
        foreach (var package in local)
        {
            nas.TryGetValue(package.FileName, out var remote);
            rows.Add(new(package.FileName, package.Kind, package.SizeBytes, package.CreatedAtUtc, package.OperatorName, package.OperatorRole, true, remote is not null,
                remote is not null && remote.SizeBytes != package.SizeBytes, package.TimeSource));
        }
        var known = local.Select(package => package.FileName).ToHashSet(StringComparer.Ordinal);
        foreach (var file in nas.Values.Where(file => !known.Contains(file.FileName)))
            if (FromName(file) is { } row) rows.Add(row);
        return [.. rows.OrderByDescending(row => row.CreatedAtUtc)];
    }

    /// <summary>What the file name of a package says (kind, time, operator); the package itself is not opened.</summary>
    internal static BackupCatalogRow? FromName(NasShareFile file)
    {
        var match = NameShape.Match(file.FileName);
        if (!match.Success) return null;
        BackupKind? kind = null;
        foreach (var candidate in new[] { BackupKind.PreRestore, BackupKind.Scheduled, BackupKind.Manual, BackupKind.InventoryPickup })
            if (match.Groups["label"].Value == BackupRules.KindLabel(candidate)) { kind = candidate; break; }
        if (kind is null) return null;
        var created = DateTime.TryParseExact(match.Groups["stamp"].Value, "dd.MM.yyyy HH-mm-ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)
            ? DateTime.SpecifyKind(local, DateTimeKind.Local).ToUniversalTime() : file.ModifiedUtc;
        return new(file.FileName, kind.Value, file.SizeBytes, created, match.Groups["name"].Value, match.Groups["role"].Value, false, true, false);
    }
}
