using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using MySqlConnector;

namespace BlazorStoc.Services;

// Task 2, subtasks 2.1/2.2 - "Copie de siguranta a bazei de date la preluarea situatiei de inventar". See TODO.md's
// "Decizie tehnica - mecanism de backup/restaurare" for the full design this implements.
public enum BackupKind
{
    InventoryPickup,
    // Not produced yet (Task 3); the label already matches the TODO's naming model for when restore is built.
    PreRestore
}

public enum BackupStage { Locking, CheckingSpace, Exporting, Verifying, Saving, Done, Failed }

public sealed record BackupProgress(BackupStage Stage, string Message);

public sealed record BackupResult(bool Success, string? PackagePath, string? PackageFileName, string? ErrorMessage)
{
    public static BackupResult Ok(string path, string fileName) => new(true, path, fileName, null);
    public static BackupResult Failed(string message) => new(false, null, null, message);
}

public sealed record BackupManifest(DateTime CreatedAtUtc, string OperatorName, string OperatorRole, string Kind,
    int TableCount, IReadOnlyDictionary<string, int> RowCounts, string SchemaVersion, string ContentSha256, string CanonicalHash);

public static class BackupRules
{
    public const string DiskSpaceMessage = "Spatiu insuficient pe disc pentru copia de siguranta. Elibereaza spatiu si reincearca.";
    public const string DumpFailedMessage = "Copia de siguranta nu a putut fi generata (eroare la exportul bazei de date).";
    public const string VerificationFailedMessage = "Copia de siguranta nu a putut fi verificata; nu a fost salvata.";
    public const string GenericFailedMessage = "Copia de siguranta a esuat. Preluarea inventarului nu a fost confirmata.";

    public static string KindLabel(BackupKind kind) => kind switch
    {
        BackupKind.InventoryPickup => "Copie siguranta preluare inventar",
        BackupKind.PreRestore => "Copie siguranta baza de date inainte restaurare baza de date folosind backup preluare inventar",
        _ => "Copie siguranta"
    };

    public static string StageMessage(BackupStage stage) => stage switch
    {
        BackupStage.Locking => "Se blocheaza alte operatii pe baza de date...",
        BackupStage.CheckingSpace => "Se verifica spatiul disponibil pe disc...",
        BackupStage.Exporting => "Se exporta baza de date...",
        BackupStage.Verifying => "Se verifica integritatea copiei de siguranta...",
        BackupStage.Saving => "Se salveaza pachetul de siguranta...",
        BackupStage.Done => "Copie de siguranta finalizata.",
        BackupStage.Failed => "Copia de siguranta a esuat.",
        _ => ""
    };
}

public static class BackupNaming
{
    private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();

    // Model from TODO.md subtask 2.3: "Copie siguranta preluare inventar data ora user_level user_name", date/time
    // in the project's dd.MM.yyyy HH:mm display format, transposed into a valid Windows file name (no ':', no
    // diacritics) and with the operator's role and name.
    public static string BuildFileName(BackupKind kind, DateTime timestampLocal, string operatorRole, string operatorName)
    {
        var stamp = timestampLocal.ToString("dd.MM.yyyy HH-mm-ss", CultureInfo.InvariantCulture);
        return $"{BackupRules.KindLabel(kind)} {stamp} {SanitizeToken(operatorRole)} {SanitizeToken(operatorName)}.zip";
    }

    public static string SanitizeToken(string? value)
    {
        var text = TextNormalization.ForStorage(value);
        var builder = new StringBuilder(text.Length);
        foreach (var character in text)
            builder.Append(Array.IndexOf(InvalidFileNameChars, character) >= 0 || character is '.' or ',' ? ' ' : character);
        var normalized = string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length == 0 ? "necunoscut" : normalized;
    }

    // The model's timestamp has second resolution; two backups triggered by the same operator within the same
    // second (an automated retry, a double click racing the disabled-button state) would otherwise collide. Never
    // overwrites an existing package - appends " (2)", " (3)", ... like Windows Explorer does for a duplicate name.
    public static string ResolveUniquePath(string directory, string fileName)
    {
        var candidate = Path.Combine(directory, fileName);
        if (!File.Exists(candidate)) return candidate;
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var suffix = 2; ; suffix++)
        {
            candidate = Path.Combine(directory, $"{stem} ({suffix}){extension}");
            if (!File.Exists(candidate)) return candidate;
        }
    }
}

// Estimates required free disk space from the largest package already on disk (or a conservative fallback for the
// very first backup); the temporary export file and the final archive briefly coexist, hence the margin factor.
public static class BackupDiskSpace
{
    private const long FallbackBytes = 20_000_000;
    private const double MarginFactor = 4.0;

    public static long EstimateRequiredBytes(string backupDirectory)
    {
        if (!Directory.Exists(backupDirectory)) return (long)(FallbackBytes * MarginFactor);
        var largest = Directory.EnumerateFiles(backupDirectory, "*.zip")
            .Select(path => new FileInfo(path).Length).DefaultIfEmpty(FallbackBytes).Max();
        return (long)(Math.Max(largest, FallbackBytes) * MarginFactor);
    }

    public static bool HasEnoughFreeSpace(string backupDirectory, out long availableBytes, out long requiredBytes)
    {
        requiredBytes = EstimateRequiredBytes(backupDirectory);
        var root = Path.GetPathRoot(Path.GetFullPath(backupDirectory));
        availableBytes = root is null ? long.MaxValue : new DriveInfo(root).AvailableFreeSpace;
        return availableBytes >= requiredBytes;
    }
}

// Subtask 3.1 (Task 3): one row of the restore page's package table.
public sealed record BackupPackage(string FileName, BackupKind Kind, long SizeBytes, DateTime CreatedAtUtc,
    string OperatorName, string OperatorRole, int TableCount)
{
    // Subtask 3.2: only an "InventoryPickup" package may ever be deleted - enforced here (used by the page to hide
    // the button) and again, independently, inside DeletePackageAsync (never trust the UI alone for this refusal).
    public bool CanDelete => Kind == BackupKind.InventoryPickup;
}

public sealed record BackupDeleteResult(bool Success, string? ErrorMessage)
{
    public static BackupDeleteResult Ok() => new(true, null);
    public static BackupDeleteResult Failed(string message) => new(false, message);
}

public interface IDatabaseBackupService
{
    Task<BackupResult> CreateBackupAsync(BackupKind kind, IProgress<BackupProgress>? progress = null,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BackupPackage>> ListPackagesAsync(CancellationToken cancellationToken = default);
    Task<BackupDeleteResult> DeletePackageAsync(string fileName, string reason, CancellationToken cancellationToken = default);
}

// Subtask 3.1/3.2 (Task 3): shared between the demo and real backup services - listing and deleting packages reads
// only the .zip/manifest.json files on disk, identically regardless of which database engine produced them.
internal static class BackupPackageStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<IReadOnlyList<BackupPackage>> ListAsync(string directory, CancellationToken token)
    {
        if (!Directory.Exists(directory)) return [];
        var packages = new List<BackupPackage>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.zip"))
        {
            var fileName = Path.GetFileName(path);
            if (fileName.StartsWith(".tmp-", StringComparison.Ordinal)) continue;
            var manifest = await TryReadManifestAsync(path, token).ConfigureAwait(false);
            if (manifest is null) continue;
            var kind = Enum.TryParse<BackupKind>(manifest.Kind, out var parsedKind) ? parsedKind : BackupKind.InventoryPickup;
            packages.Add(new BackupPackage(fileName, kind, new FileInfo(path).Length, manifest.CreatedAtUtc,
                manifest.OperatorName, manifest.OperatorRole, manifest.TableCount));
        }
        return packages.OrderByDescending(package => package.CreatedAtUtc).ToList();
    }

    public static async Task<BackupDeleteResult> DeleteAsync(string directory, string fileName, string reason,
        IAuditTrail? auditTrail, IAccessControl access, CancellationToken token)
    {
        // fileName always comes from a package this method itself listed (the page never lets an operator type an
        // arbitrary name), but this guard costs nothing and rules out escaping the backup directory outright.
        if (fileName != Path.GetFileName(fileName) || fileName.Length == 0)
            return BackupDeleteResult.Failed("Pachet invalid.");
        var path = Path.Combine(directory, fileName);
        if (!File.Exists(path)) return BackupDeleteResult.Failed("Pachetul nu a fost gasit (poate a fost deja sters).");
        var manifest = await TryReadManifestAsync(path, token).ConfigureAwait(false);
        var kind = manifest is not null && Enum.TryParse<BackupKind>(manifest.Kind, out var parsedKind) ? parsedKind : BackupKind.InventoryPickup;
        if (kind != BackupKind.InventoryPickup)
            return BackupDeleteResult.Failed("Pachetele generate automat inainte de o restaurare nu pot fi sterse.");

        // The dialog (DeleteConfirmationDialog, subtask 3.2) already resolved the default/custom choice into a
        // single reason string before calling here; this only guards against a caller that skips the dialog.
        var motif = ChangeReasonRules.Normalize(reason);
        if (ChangeReasonRules.ValidationError(motif) is { } error) return BackupDeleteResult.Failed(error);

        File.Delete(path);
        try { File.Delete(path + ".sha256"); } catch (IOException) { }

        await AuditRecorder.RecordDeleteAsync(auditTrail, access, AuditEntities.DatabaseBackup, string.Empty, fileName,
            $"Pachet: {fileName}", motif, token).ConfigureAwait(false);
        return BackupDeleteResult.Ok();
    }

    private static async Task<BackupManifest?> TryReadManifestAsync(string zipPath, CancellationToken token)
    {
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            var entry = archive.GetEntry("manifest.json");
            if (entry is null) return null;
            using var stream = entry.Open();
            return await JsonSerializer.DeserializeAsync<BackupManifest>(stream, JsonOptions, token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException or IOException)
        {
            return null;
        }
    }
}

file static class BackupManifestIo
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static async Task WriteAsync(string path, BackupManifest manifest, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(stream, manifest, JsonOptions, token).ConfigureAwait(false);
    }

    public static async Task<string> HashFileAsync(string path, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false)).ToLowerInvariant();
    }
}

// Demo mode (App:DemoMode=true) has no MariaDB to dump; the backup here is an online copy of the local SQLite file
// (SqliteConnection.BackupDatabase, safe to run against a live connection - never a raw file copy of a database
// that might be mid-write) plus the same manifest/zip/hash shape as the real mode, so the eventual restore page
// (Task 3) can treat both the same way. The canonical row-hash comparison in "Decizie tehnica" is written against
// MariaDB's information_schema (CanonicalRowHasher) and does not apply here; SQLite's own online-backup consistency
// stands in for it in demo mode.
public sealed class SqliteDatabaseBackupService(SqliteLocalStore store, IConfiguration configuration,
    IAccessControl access, IOperationLockService locks, IAuditTrail? auditTrail, ILogger<SqliteDatabaseBackupService> logger)
    : IDatabaseBackupService
{
    private readonly string backupDirectory = Path.GetFullPath(configuration["App:BackupFilesPath"] ?? Path.Combine("data", "database-backups"));

    public async Task<BackupResult> CreateBackupAsync(BackupKind kind, IProgress<BackupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(backupDirectory);
        var operatorName = await access.GetUsernameAsync(cancellationToken).ConfigureAwait(false) ?? "necunoscut";
        var operatorRole = await access.IsAdministratorAsync(cancellationToken).ConfigureAwait(false) ? AccessRoles.Administrator : AccessRoles.LimitedUser;

        progress?.Report(new(BackupStage.Locking, BackupRules.StageMessage(BackupStage.Locking)));
        await using var handle = await locks.TryAcquireAsync($"backup:{kind}", operatorName, operatorRole, cancellationToken).ConfigureAwait(false);
        if (handle is null) return BackupResult.Failed(OperationLockRules.HeldMessage);

        var tempDb = Path.Combine(backupDirectory, $".tmp-{Guid.NewGuid():N}.sqlite");
        var tempZip = Path.Combine(backupDirectory, $".tmp-{Guid.NewGuid():N}.zip");
        try
        {
            progress?.Report(new(BackupStage.CheckingSpace, BackupRules.StageMessage(BackupStage.CheckingSpace)));
            if (!BackupDiskSpace.HasEnoughFreeSpace(backupDirectory, out _, out _))
                return BackupResult.Failed(BackupRules.DiskSpaceMessage);

            progress?.Report(new(BackupStage.Exporting, BackupRules.StageMessage(BackupStage.Exporting)));
            // Pooling=False: Microsoft.Data.Sqlite pools connections by default, so a disposed pooled connection
            // keeps the underlying file handle open for reuse - hashing tempDb right afterwards would then fail
            // with "file in use". This one-off destination connection is never reused, so pooling only gets in the way.
            await using (var source = await store.OpenConnectionAsync(cancellationToken).ConfigureAwait(false))
            await using (var destination = new SqliteConnection($"Data Source={tempDb};Pooling=False"))
            {
                destination.Open();
                source.BackupDatabase(destination);
            }

            var rowCounts = await CountRowsAsync(tempDb, cancellationToken).ConfigureAwait(false);
            var contentHash = await BackupManifestIo.HashFileAsync(tempDb, cancellationToken).ConfigureAwait(false);

            progress?.Report(new(BackupStage.Verifying, BackupRules.StageMessage(BackupStage.Verifying)));
            if (rowCounts.Count == 0 || new FileInfo(tempDb).Length == 0) return BackupResult.Failed(BackupRules.VerificationFailedMessage);

            var manifest = new BackupManifest(DateTime.UtcNow, operatorName, operatorRole, kind.ToString(),
                rowCounts.Count, rowCounts, "SQLite (demo)", contentHash, string.Empty);

            progress?.Report(new(BackupStage.Saving, BackupRules.StageMessage(BackupStage.Saving)));
            var tempManifest = tempDb + ".manifest.json";
            await BackupManifestIo.WriteAsync(tempManifest, manifest, cancellationToken).ConfigureAwait(false);
            using (var archive = ZipFile.Open(tempZip, ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(tempDb, "backup.sqlite", CompressionLevel.Optimal);
                archive.CreateEntryFromFile(tempManifest, "manifest.json", CompressionLevel.Optimal);
            }
            File.Delete(tempManifest);

            var fileName = BackupNaming.BuildFileName(kind, DateTime.Now, operatorRole, operatorName);
            var finalPath = BackupNaming.ResolveUniquePath(backupDirectory, fileName);
            fileName = Path.GetFileName(finalPath);
            File.Move(tempZip, finalPath);
            var archiveHash = await BackupManifestIo.HashFileAsync(finalPath, cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(finalPath + ".sha256", archiveHash, cancellationToken).ConfigureAwait(false);

            await AuditRecorder.RecordGenerateAsync(auditTrail, access, AuditEntities.DatabaseBackup, fileName,
                $"Pachet: {fileName}; tabele: {rowCounts.Count}", cancellationToken).ConfigureAwait(false);

            progress?.Report(new(BackupStage.Done, BackupRules.StageMessage(BackupStage.Done)));
            return BackupResult.Ok(finalPath, fileName);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Database backup (demo) failed ({ErrorType}).", exception.GetType().Name);
            progress?.Report(new(BackupStage.Failed, BackupRules.StageMessage(BackupStage.Failed)));
            return BackupResult.Failed(BackupRules.GenericFailedMessage);
        }
        finally
        {
            TryDelete(tempDb);
            TryDelete(tempZip);
        }
    }

    public Task<IReadOnlyList<BackupPackage>> ListPackagesAsync(CancellationToken cancellationToken = default) =>
        BackupPackageStore.ListAsync(backupDirectory, cancellationToken);

    public Task<BackupDeleteResult> DeletePackageAsync(string fileName, string reason, CancellationToken cancellationToken = default) =>
        BackupPackageStore.DeleteAsync(backupDirectory, fileName, reason, auditTrail, access, cancellationToken);

    private static async Task<Dictionary<string, int>> CountRowsAsync(string dbPath, CancellationToken token)
    {
        await using var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        await connection.OpenAsync(token).ConfigureAwait(false);
        var tables = new List<string>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'";
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false)) tables.Add(reader.GetString(0));
        }
        var counts = new Dictionary<string, int>();
        foreach (var table in tables)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM \"{table.Replace("\"", "\"\"")}\"";
            counts[table] = Convert.ToInt32(await command.ExecuteScalarAsync(token).ConfigureAwait(false), CultureInfo.InvariantCulture);
        }
        return counts;
    }

    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } }
}

// Real mode: exports with mariadb-dump (the same distribution as mariadbd.exe, docs/CLAUDE_CONECTARE_MARIADB_LOCALA.md
// section 3), verifies the database did not change during the export window with CanonicalRowHasher, and only then
// saves the package. The application account (blazorstoc_dev) has SELECT/INSERT/UPDATE/DELETE only - no DDL and no
// way to stop foreign connections - so a concurrent write from outside the application is not physically prevented;
// --single-transaction still gives a consistent MVCC snapshot, and this is the same accepted residual risk TODO.md's
// Task 3 risk list already documents for that scenario.
public sealed class MariaDatabaseBackupService(IConfiguration configuration, IAccessControl access,
    IOperationLockService locks, IAuditTrail? auditTrail, ILogger<MariaDatabaseBackupService> logger) : IDatabaseBackupService
{
    private readonly string backupDirectory = MariaAssetPaths.DatabaseBackups(configuration);

    public async Task<BackupResult> CreateBackupAsync(BackupKind kind, IProgress<BackupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(backupDirectory);
        var operatorName = await access.GetUsernameAsync(cancellationToken).ConfigureAwait(false) ?? "necunoscut";
        var operatorRole = await access.IsAdministratorAsync(cancellationToken).ConfigureAwait(false) ? AccessRoles.Administrator : AccessRoles.LimitedUser;

        progress?.Report(new(BackupStage.Locking, BackupRules.StageMessage(BackupStage.Locking)));
        await using var handle = await locks.TryAcquireAsync($"backup:{kind}", operatorName, operatorRole, cancellationToken).ConfigureAwait(false);
        if (handle is null) return BackupResult.Failed(OperationLockRules.HeldMessage);

        var tempSql = Path.Combine(backupDirectory, $".tmp-{Guid.NewGuid():N}.sql");
        var tempZip = Path.Combine(backupDirectory, $".tmp-{Guid.NewGuid():N}.zip");
        string? tempCredentialsFile = null;
        try
        {
            progress?.Report(new(BackupStage.CheckingSpace, BackupRules.StageMessage(BackupStage.CheckingSpace)));
            if (!BackupDiskSpace.HasEnoughFreeSpace(backupDirectory, out _, out _))
                return BackupResult.Failed(BackupRules.DiskSpaceMessage);
            if (!MariaDatabaseGuard.IsAllowedDatabase(configuration))
                return BackupResult.Failed(BackupRules.GenericFailedMessage);

            var tables = MariaArchiveSchema.RequiredTables.ToArray();
            progress?.Report(new(BackupStage.Exporting, BackupRules.StageMessage(BackupStage.Exporting)));
            CanonicalSnapshot before, after;
            await using (var connection = DatabaseConnections.Create(configuration))
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                before = await CanonicalRowHasher.ComputeAsync(connection, tables, cancellationToken).ConfigureAwait(false);
            }

            tempCredentialsFile = await WriteCredentialsFileAsync(configuration, cancellationToken).ConfigureAwait(false);
            await RunMariaDumpAsync(configuration, tempCredentialsFile, tempSql, cancellationToken).ConfigureAwait(false);

            await using (var connection = DatabaseConnections.Create(configuration))
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                after = await CanonicalRowHasher.ComputeAsync(connection, tables, cancellationToken).ConfigureAwait(false);
            }

            progress?.Report(new(BackupStage.Verifying, BackupRules.StageMessage(BackupStage.Verifying)));
            if (!File.Exists(tempSql) || new FileInfo(tempSql).Length == 0) return BackupResult.Failed(BackupRules.DumpFailedMessage);
            // The dump window must be stable: a difference here means another connection (outside the app, since
            // the app itself is idle during this call) wrote to the database while it was being exported.
            if (!string.Equals(before.OverallHash, after.OverallHash, StringComparison.Ordinal))
                return BackupResult.Failed(BackupRules.VerificationFailedMessage);

            var dumpHash = await BackupManifestIo.HashFileAsync(tempSql, cancellationToken).ConfigureAwait(false);
            var rowCounts = after.Tables.ToDictionary(table => table.Table, table => table.RowCount);
            var manifest = new BackupManifest(DateTime.UtcNow, operatorName, operatorRole, kind.ToString(),
                after.Tables.Count, rowCounts, $"{after.Tables.Count} tabele (MariaDB 11.4.13)", dumpHash, after.OverallHash);

            progress?.Report(new(BackupStage.Saving, BackupRules.StageMessage(BackupStage.Saving)));
            var tempManifest = tempSql + ".manifest.json";
            await BackupManifestIo.WriteAsync(tempManifest, manifest, cancellationToken).ConfigureAwait(false);
            using (var archive = ZipFile.Open(tempZip, ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(tempSql, "dump.sql", CompressionLevel.Optimal);
                archive.CreateEntryFromFile(tempManifest, "manifest.json", CompressionLevel.Optimal);
            }
            File.Delete(tempManifest);

            var fileName = BackupNaming.BuildFileName(kind, DateTime.Now, operatorRole, operatorName);
            var finalPath = BackupNaming.ResolveUniquePath(backupDirectory, fileName);
            fileName = Path.GetFileName(finalPath);
            File.Move(tempZip, finalPath);
            var archiveHash = await BackupManifestIo.HashFileAsync(finalPath, cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(finalPath + ".sha256", archiveHash, cancellationToken).ConfigureAwait(false);

            await AuditRecorder.RecordGenerateAsync(auditTrail, access, AuditEntities.DatabaseBackup, fileName,
                $"Pachet: {fileName}; tabele: {after.Tables.Count}; hash canonic: {after.OverallHash[..12]}", cancellationToken).ConfigureAwait(false);

            progress?.Report(new(BackupStage.Done, BackupRules.StageMessage(BackupStage.Done)));
            return BackupResult.Ok(finalPath, fileName);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Database backup failed ({ErrorType}).", exception.GetType().Name);
            progress?.Report(new(BackupStage.Failed, BackupRules.StageMessage(BackupStage.Failed)));
            return BackupResult.Failed(exception is InvalidOperationException ? BackupRules.DumpFailedMessage : BackupRules.GenericFailedMessage);
        }
        finally
        {
            TryDelete(tempSql);
            TryDelete(tempZip);
            if (tempCredentialsFile is not null) TryDelete(tempCredentialsFile);
        }
    }

    public Task<IReadOnlyList<BackupPackage>> ListPackagesAsync(CancellationToken cancellationToken = default) =>
        BackupPackageStore.ListAsync(backupDirectory, cancellationToken);

    public Task<BackupDeleteResult> DeletePackageAsync(string fileName, string reason, CancellationToken cancellationToken = default) =>
        BackupPackageStore.DeleteAsync(backupDirectory, fileName, reason, auditTrail, access, cancellationToken);

    // A "defaults-extra-file" keeps the account password out of the process command line (visible to any other
    // process listing) and out of environment variables; it is written to a private temp path and deleted in the
    // outer finally, regardless of outcome.
    private static async Task<string> WriteCredentialsFileAsync(IConfiguration configuration, CancellationToken token)
    {
        var path = Path.Combine(Path.GetTempPath(), $".blazorstoc-dump-{Guid.NewGuid():N}.cnf");
        var sslMode = configuration["Database:SslMode"] ?? "Required";
        var content = $"[client]\nhost={configuration["Database:Host"] ?? "127.0.0.1"}\n" +
            $"port={configuration.GetValue("Database:Port", 3307)}\nuser={configuration["Database:User"] ?? "blazorstoc_dev"}\n" +
            $"password={configuration["Database:Password"]}\nssl-mode={sslMode.ToUpperInvariant()}\n";
        await File.WriteAllTextAsync(path, content, token).ConfigureAwait(false);
        return path;
    }

    private static async Task RunMariaDumpAsync(IConfiguration configuration, string credentialsFile, string outputPath, CancellationToken token)
    {
        var executable = MariaAssetPaths.MariaDumpExecutable(configuration);
        if (!File.Exists(executable))
            throw new InvalidOperationException($"Utilitarul mariadb-dump nu a fost gasit la calea configurata ({executable}).");
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add($"--defaults-extra-file={credentialsFile}");
        startInfo.ArgumentList.Add("--single-transaction");
        startInfo.ArgumentList.Add("--routines");
        startInfo.ArgumentList.Add("--triggers");
        startInfo.ArgumentList.Add("--hex-blob");
        startInfo.ArgumentList.Add(configuration["Database:Name"] ?? "BlazorStoc");

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        await using var outputFile = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        var copyTask = process.StandardOutput.BaseStream.CopyToAsync(outputFile, token);
        var errorTask = process.StandardError.ReadToEndAsync(token);
        await Task.WhenAll(copyTask, errorTask, process.WaitForExitAsync(token)).ConfigureAwait(false);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"mariadb-dump a esuat (cod {process.ExitCode}): {errorTask.Result}");
    }

    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } }
}
