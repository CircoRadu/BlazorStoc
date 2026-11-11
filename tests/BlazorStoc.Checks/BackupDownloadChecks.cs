using System.IO.Compression;
using BlazorStoc.Services;
using Microsoft.Extensions.Configuration;

namespace BlazorStoc.Checks;

// The download of a backup package (PROP-0020): which names are accepted, a package alone is sent as it is, a package with its checksum file comes as one archive, only who may change
// the backup settings can download, and every download is journaled with its own action.
public static class BackupDownloadChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        check(BackupDownloadRules.IsPackageName("backup-2026-11-11.zip") && !BackupDownloadRules.IsPackageName("../x.zip") && !BackupDownloadRules.IsPackageName(".tmp-x.zip")
              && !BackupDownloadRules.IsPackageName("backup.sql") && !BackupDownloadRules.IsPackageName("a/b.zip") && !BackupDownloadRules.IsPackageName(""),
            "Backup download: only the name of a .zip package of the backup folder is accepted (no path, no temporary file)");
        check(BackupDownloadRules.Url("backup 1.zip") == "/api/backup/backup%201.zip" && BackupDownloadRules.ArchiveName("backup-1.zip") == "backup-1-cu-suma-de-control.zip", "Backup download: the address is escaped and the archive is named after the package");

        var directory = Path.Combine(Path.GetTempPath(), "blazorstoc-download-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Database:MariaBackupFilesPath"] = directory }).Build();
            var audit = new TestAuditTrail();
            var bytes = new byte[4096];
            Random.Shared.NextBytes(bytes);
            await File.WriteAllBytesAsync(Path.Combine(directory, "alone.zip"), bytes);
            await File.WriteAllBytesAsync(Path.Combine(directory, "paired.zip"), bytes);
            await File.WriteAllTextAsync(Path.Combine(directory, "paired.zip.sha256"), "abc123");
            var service = new BackupDownloadService(configuration, new TestAccessControl(true, "admin"), audit);

            await using (var alone = await service.OpenAsync("alone.zip"))
            {
                using var copy = new MemoryStream();
                await alone.Content.CopyToAsync(copy);
                check(alone.DownloadName == "alone.zip" && !alone.Archived && alone.ContentType == "application/zip" && copy.ToArray().SequenceEqual(bytes), "Backup download: a package without a checksum file is sent as it is");
            }
            string[] entries;
            byte[] inside;
            string archiveName;
            await using (var paired = await service.OpenAsync("paired.zip"))
            {
                archiveName = paired.DownloadName;
                using var archive = new ZipArchive(paired.Content, ZipArchiveMode.Read);
                entries = [.. archive.Entries.Select(entry => entry.FullName).Order()];
                using var package = archive.GetEntry("paired.zip")!.Open();
                using var copy = new MemoryStream();
                await package.CopyToAsync(copy);
                inside = copy.ToArray();
            }
            check(archiveName == "paired-cu-suma-de-control.zip" && entries.SequenceEqual(["paired.zip", "paired.zip.sha256"]) && inside.SequenceEqual(bytes), "Backup download: a package with its checksum file comes as one archive holding both files");
            check(!Directory.EnumerateFiles(Path.GetTempPath(), "blazorstoc-download-*.zip").Any(), "Backup download: the temporary archive is deleted when the download ends");
            check(audit.Entries.Count(entry => entry.Action == AuditActions.DownloadBackup && entry.EntityType == AuditEntities.DatabaseBackup) == 2
                  && audit.Entries.Any(entry => entry.Action == AuditActions.DownloadBackup && entry.Details.Contains("paired.zip", StringComparison.Ordinal) && entry.Details.Contains("da", StringComparison.Ordinal)),
                "Backup download: each download is journaled as \"Descărcare pachet backup\" with the package and whether it was an archive");

            var refused = false;
            try { await new BackupDownloadService(configuration, new TestAccessControl(false, "user"), audit).OpenAsync("alone.zip"); }
            catch (AccessDeniedException) { refused = true; }
            var missing = false;
            try { await service.OpenAsync("missing.zip"); }
            catch (BackupDownloadException) { missing = true; }
            var bad = false;
            try { await service.OpenAsync("../alone.zip"); }
            catch (BackupDownloadException) { bad = true; }
            check(refused && missing && bad && audit.Entries.Count(entry => entry.Action == AuditActions.DownloadBackup) == 2, "Backup download: a user without the right, a missing package and a bad name are refused and nothing is journaled");
        }
        finally { try { Directory.Delete(directory, recursive: true); } catch (IOException) { } }
    }
}
