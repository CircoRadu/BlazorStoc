using System.IO.Compression;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using MySqlConnector;

namespace BlazorStoc.Services;

// Task 3, subtask 3.4 - "Pasii de restaurare (executati numai dupa confirmare)". Runs after subtask 3.3's popup
// confirms the operator typed the exact word "confirma". Mirrors IDatabaseBackupService's split between a demo
// (SQLite) and a real (MariaDB) implementation, and reuses the same shared operation lock, backup directory,
// package naming and canonical row-hash comparison as Task 2's backup service.
public enum RestoreStage { Locking, SnapshotCurrent, VerifyingPackage, ComparingStructure, ComparingContent, Importing, Done, Failed }

public sealed record RestoreProgress(RestoreStage Stage, string Message);

public sealed record RestoreResult(bool Success, string? ErrorMessage, string? PreRestorePackageFileName)
{
    public static RestoreResult Ok(string preRestorePackageFileName) => new(true, null, preRestorePackageFileName);
    public static RestoreResult Failed(string message, string? preRestorePackageFileName = null) => new(false, message, preRestorePackageFileName);
}

public static class RestoreRules
{
    public const string PackageNotFoundMessage = "Pachetul selectat nu a fost gasit (poate a fost sters intre timp). Actualizeaza lista si reincearca.";
    public const string HashMismatchMessage = "Pachetul de siguranta este corupt sau deteriorat (hash necorespunzator); restaurarea a fost oprita.";
    public const string StructureMismatchMessage = "Pachetul nu poate fi folosit: structura bazei de date difera de baza curenta.";
    public const string IdenticalContentMessage = "Backupul selectat este identic cu baza de date curenta; restaurarea nu este necesara.";
    public const string ImportFailedMessage = "Importul pachetului a esuat; baza de date curenta nu a fost modificata.";
    public const string GenericFailedMessage = "Restaurarea a esuat. Baza de date curenta nu a fost modificata.";
    public const string SuccessMessage = "Restaurarea a fost finalizata cu succes.";
    // Real mode only (Pas 4): the dedicated migrator account/schema-swap prerequisites (docs/CLAUDE_CONECTARE_MARIADB_LOCALA.md,
    // TODO.md Task 3 "Decizii acceptate") are not yet provisioned for cross-schema DDL in this environment.
    public const string MigratorNotConfiguredMessage = "Restaurarea reala necesita contul MariaDB dedicat (schema temporara/comutare), neconfigurat inca pe acest server.";

    public static string StageMessage(RestoreStage stage) => stage switch
    {
        RestoreStage.Locking => "Se blocheaza alte operatii pe baza de date...",
        RestoreStage.SnapshotCurrent => "Se genereaza copia de siguranta a bazei curente...",
        RestoreStage.VerifyingPackage => "Se verifica integritatea pachetului selectat...",
        RestoreStage.ComparingStructure => "Se compara structura bazei de date...",
        RestoreStage.ComparingContent => "Se compara continutul bazei de date...",
        RestoreStage.Importing => "Se importa si se comuta baza de date...",
        RestoreStage.Done => "Restaurare finalizata.",
        RestoreStage.Failed => "Restaurarea a esuat.",
        _ => ""
    };
}

public interface IDatabaseRestoreService
{
    Task<RestoreResult> RestoreAsync(string packageFileName, IProgress<RestoreProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

internal static class RestoreHashing
{
    public static async Task<string> HashFileAsync(string path, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false)).ToLowerInvariant();
    }
}

// Demo mode: the backup package is a full SQLite file (backup.sqlite inside the zip), so every step of the TODO's
// Pas 1-4 maps directly onto it - no staging schema is needed the way MariaDB's raw SQL dump needs one (see
// MariaDatabaseRestoreService). Pas 4 uses SqliteConnection.BackupDatabase the same way Task 2's backup service
// does, just in the opposite direction (backup file -> live connection).
public sealed class SqliteDatabaseRestoreService(SqliteLocalStore store, IConfiguration configuration,
    IAccessControl access, IOperationLockService locks, IDatabaseBackupService backups, IAuditTrail? auditTrail,
    ILogger<SqliteDatabaseRestoreService> logger) : IDatabaseRestoreService
{
    private readonly string backupDirectory = Path.GetFullPath(configuration["App:BackupFilesPath"] ?? Path.Combine("data", "database-backups"));

    public async Task<RestoreResult> RestoreAsync(string packageFileName, IProgress<RestoreProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (packageFileName != Path.GetFileName(packageFileName) || packageFileName.Length == 0)
            return RestoreResult.Failed(RestoreRules.PackageNotFoundMessage);
        var packagePath = Path.Combine(backupDirectory, packageFileName);
        if (!File.Exists(packagePath)) return RestoreResult.Failed(RestoreRules.PackageNotFoundMessage);

        var operatorName = await access.GetUsernameAsync(cancellationToken).ConfigureAwait(false) ?? "necunoscut";
        var operatorRole = await access.IsAdministratorAsync(cancellationToken).ConfigureAwait(false) ? AccessRoles.Administrator : AccessRoles.LimitedUser;

        progress?.Report(new(RestoreStage.Locking, RestoreRules.StageMessage(RestoreStage.Locking)));
        await using var handle = await locks.TryAcquireAsync("restore", operatorName, operatorRole, cancellationToken).ConfigureAwait(false);
        if (handle is null) return RestoreResult.Failed(OperationLockRules.HeldMessage);

        string? preRestoreFileName = null;
        var extractDir = Path.Combine(backupDirectory, $".tmp-{Guid.NewGuid():N}");
        try
        {
            // Pas 0 - blocare (deja activa mai sus) si snapshot curent, folosind acelasi mecanism ca la Task 2.
            // Foloseste lacatul deja detinut (existingLock) in loc sa incerce sa il acquire-a din nou - un al doilea
            // TryAcquireAsync pe acelasi fisier de lacat, cat timp handle-ul curent e inca deschis, ar esua mereu.
            progress?.Report(new(RestoreStage.SnapshotCurrent, RestoreRules.StageMessage(RestoreStage.SnapshotCurrent)));
            var snapshot = await backups.CreateBackupAsync(BackupKind.PreRestore, null, cancellationToken, handle).ConfigureAwait(false);
            if (!snapshot.Success) return RestoreResult.Failed(snapshot.ErrorMessage ?? RestoreRules.GenericFailedMessage);
            preRestoreFileName = snapshot.PackageFileName;

            // Pas 1 - integritatea backupului ales.
            progress?.Report(new(RestoreStage.VerifyingPackage, RestoreRules.StageMessage(RestoreStage.VerifyingPackage)));
            var manifest = await BackupPackageStore.TryReadManifestAsync(packagePath, cancellationToken).ConfigureAwait(false);
            if (manifest is null) return RestoreResult.Failed(RestoreRules.HashMismatchMessage, preRestoreFileName);
            var sidecarPath = packagePath + ".sha256";
            if (!File.Exists(sidecarPath)) return RestoreResult.Failed(RestoreRules.HashMismatchMessage, preRestoreFileName);
            var expectedArchiveHash = (await File.ReadAllTextAsync(sidecarPath, cancellationToken).ConfigureAwait(false)).Trim();
            var actualArchiveHash = await RestoreHashing.HashFileAsync(packagePath, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(expectedArchiveHash, actualArchiveHash, StringComparison.OrdinalIgnoreCase))
                return RestoreResult.Failed(RestoreRules.HashMismatchMessage, preRestoreFileName);

            Directory.CreateDirectory(extractDir);
            ZipFile.ExtractToDirectory(packagePath, extractDir);
            var extractedDb = Path.Combine(extractDir, "backup.sqlite");
            if (!File.Exists(extractedDb)) return RestoreResult.Failed(RestoreRules.HashMismatchMessage, preRestoreFileName);
            var extractedDbHash = await RestoreHashing.HashFileAsync(extractedDb, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(extractedDbHash, manifest.ContentSha256, StringComparison.OrdinalIgnoreCase))
                return RestoreResult.Failed(RestoreRules.HashMismatchMessage, preRestoreFileName);

            await using var backupConnection = new SqliteConnection($"Data Source={extractedDb};Pooling=False;Mode=ReadOnly");
            await backupConnection.OpenAsync(cancellationToken).ConfigureAwait(false);
            var backupSchema = await SqliteSchemaHasher.GetSchemaAsync(backupConnection, cancellationToken).ConfigureAwait(false);

            // Pas 2 - diferente de structura.
            progress?.Report(new(RestoreStage.ComparingStructure, RestoreRules.StageMessage(RestoreStage.ComparingStructure)));
            Dictionary<string, List<CanonicalColumn>> liveSchema;
            await using (var liveConnection = await store.OpenConnectionAsync(cancellationToken).ConfigureAwait(false))
                liveSchema = await SqliteSchemaHasher.GetSchemaAsync(liveConnection, cancellationToken).ConfigureAwait(false);
            if (!SqliteSchemaHasher.SchemasMatch(backupSchema, liveSchema))
                return RestoreResult.Failed(RestoreRules.StructureMismatchMessage, preRestoreFileName);

            // Pas 3 - diferente de continut (aceeasi metoda canonica tip-si-hash folosita la backup/migrare).
            progress?.Report(new(RestoreStage.ComparingContent, RestoreRules.StageMessage(RestoreStage.ComparingContent)));
            var backupContentHash = await SqliteSchemaHasher.ComputeContentHashAsync(backupConnection, backupSchema, cancellationToken).ConfigureAwait(false);
            string liveContentHash;
            await using (var liveConnection = await store.OpenConnectionAsync(cancellationToken).ConfigureAwait(false))
                liveContentHash = await SqliteSchemaHasher.ComputeContentHashAsync(liveConnection, liveSchema, cancellationToken).ConfigureAwait(false);
            if (string.Equals(backupContentHash, liveContentHash, StringComparison.Ordinal))
                return RestoreResult.Failed(RestoreRules.IdenticalContentMessage, preRestoreFileName);

            // Pas 4 - import si comutare. Pentru SQLite pachetul e deja un fisier de baza complet, asa ca
            // "importul" e o copiere directa peste conexiunea vie prin API-ul de backup SQLite (aceeasi metoda ca
            // la generarea copiei de siguranta, doar in sens invers) - nu exista scheme separate de comutat ca la
            // MariaDB; plasa de siguranta suplimentara e chiar pachetul pre-restaurare generat la Pas 0 (nestersibil).
            progress?.Report(new(RestoreStage.Importing, RestoreRules.StageMessage(RestoreStage.Importing)));
            await using (var source = new SqliteConnection($"Data Source={extractedDb};Pooling=False;Mode=ReadOnly"))
            await using (var destination = await store.OpenConnectionAsync(cancellationToken).ConfigureAwait(false))
            {
                await source.OpenAsync(cancellationToken).ConfigureAwait(false);
                source.BackupDatabase(destination);
            }

            await AuditRecorder.RecordRestoreAsync(auditTrail, access, AuditEntities.DatabaseBackup, packageFileName,
                $"Pachet folosit: {packageFileName}; pachet pre-restaurare: {preRestoreFileName}", cancellationToken).ConfigureAwait(false);

            progress?.Report(new(RestoreStage.Done, RestoreRules.StageMessage(RestoreStage.Done)));
            return RestoreResult.Ok(preRestoreFileName!);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Database restore (demo) failed ({ErrorType}).", exception.GetType().Name);
            progress?.Report(new(RestoreStage.Failed, RestoreRules.StageMessage(RestoreStage.Failed)));
            return RestoreResult.Failed(RestoreRules.GenericFailedMessage, preRestoreFileName);
        }
        finally
        {
            try { if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true); } catch (IOException) { }
        }
    }
}

// Pure schema/content comparison helpers for the SQLite (demo-mode) restore path - no database access beyond the
// two SqliteConnection instances the caller already opened (backup file, live database). Reuses CanonicalColumn/
// CanonicalRowHasher.HashRow from Task 2 (Services/CanonicalRowHasher.cs) so the canonicalization rules (NULL/
// integer/real/blob/text prefixes) stay identical between the MariaDB and SQLite comparison paths.
internal static class SqliteSchemaHasher
{
    public static async Task<Dictionary<string, List<CanonicalColumn>>> GetSchemaAsync(SqliteConnection connection, CancellationToken token)
    {
        var tables = await GetTableNamesAsync(connection, token).ConfigureAwait(false);
        var schema = new Dictionary<string, List<CanonicalColumn>>(StringComparer.Ordinal);
        foreach (var table in tables) schema[table] = await GetColumnsAsync(connection, table, token).ConfigureAwait(false);
        return schema;
    }

    public static bool SchemasMatch(Dictionary<string, List<CanonicalColumn>> left, Dictionary<string, List<CanonicalColumn>> right)
    {
        if (!left.Keys.OrderBy(name => name, StringComparer.Ordinal).SequenceEqual(right.Keys.OrderBy(name => name, StringComparer.Ordinal)))
            return false;
        foreach (var (table, columns) in left)
        {
            var other = right[table];
            if (columns.Count != other.Count) return false;
            for (var i = 0; i < columns.Count; i++)
                if (columns[i].Name != other[i].Name || columns[i].Prefix != other[i].Prefix) return false;
        }
        return true;
    }

    public static async Task<string> ComputeContentHashAsync(SqliteConnection connection,
        Dictionary<string, List<CanonicalColumn>> schema, CancellationToken token)
    {
        var tableHashes = new List<string>();
        foreach (var table in schema.Keys.OrderBy(name => name, StringComparer.Ordinal))
        {
            var columns = schema[table];
            if (columns.Count == 0) { tableHashes.Add($"{table}:0:{HashLines([])}"); continue; }
            var columnList = string.Join(',', columns.Select(column => Quote(column.Name)));
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT {columnList} FROM {Quote(table)} ORDER BY {columnList}";
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            var rowHashes = new List<string>();
            var values = new object?[columns.Count];
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                for (var i = 0; i < columns.Count; i++) values[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                rowHashes.Add(CanonicalRowHasher.HashRow(columns, values));
            }
            tableHashes.Add($"{table}:{rowHashes.Count}:{HashLines(rowHashes)}");
        }
        return HashLines(tableHashes);
    }

    private static async Task<List<string>> GetTableNamesAsync(SqliteConnection connection, CancellationToken token)
    {
        var tables = new List<string>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false)) tables.Add(reader.GetString(0));
        return tables;
    }

    private static async Task<List<CanonicalColumn>> GetColumnsAsync(SqliteConnection connection, string table, CancellationToken token)
    {
        var columns = new List<CanonicalColumn>();
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({Quote(table)})";
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
            columns.Add(new CanonicalColumn(reader.GetString(1), DeclaredTypePrefix(reader.IsDBNull(2) ? "" : reader.GetString(2))));
        return columns;
    }

    // SQLite's own type-affinity rules (https://sqlite.org/datatype3.html section 3.1), simplified to the same
    // four-way prefix CanonicalRowHasher already uses for MariaDB columns.
    private static char DeclaredTypePrefix(string declaredType)
    {
        var upper = declaredType.ToUpperInvariant();
        if (upper.Contains("INT")) return 'I';
        if (upper.Contains("BLOB") || upper.Length == 0) return 'B';
        if (upper.Contains("REAL") || upper.Contains("FLOA") || upper.Contains("DOUB")) return 'R';
        if (upper.Contains("NUMERIC") || upper.Contains("DECIMAL")) return 'R';
        return 'T';
    }

    private static string HashLines(IEnumerable<string> lines) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join('\n', lines)))).ToLowerInvariant();

    private static string Quote(string identifier) => $"\"{identifier.Replace("\"", "\"\"")}\"";
}

// Real mode. Pas 1-3 (hash/structure/content) only ever read the live database (SELECT, information_schema),
// which the ordinary application account (blazorstoc_dev) already can do. Pas 4 (import into a temporary schema
// and RENAME TABLE swap) needs CREATE SCHEMA/DROP SCHEMA and cross-schema RENAME TABLE rights that TODO.md's Task
// 3 "Decizii acceptate" assigns to a dedicated account (built on top of blazorstoc_migrator, docs/PROJECT_STATE.md
// subtask 2.9) - that account's cross-schema grants have not been provisioned in this environment (blazorstoc_migrator
// today only has rights scoped to the single BlazorStoc schema, confirmed when it was created). Pas 1-3 therefore
// run for real against a live MariaDB instance; Pas 4 fails clearly (MigratorNotConfiguredMessage) instead of
// attempting DDL the account cannot perform, rather than leaving the operator with an opaque MySQL permission
// error. None of this class has been exercised against a live MariaDB server in this environment (no local
// instance available here - see docs/TESTE_RAMASE.md); it follows the same design as the already-verified
// MariaDatabaseBackupService and CanonicalRowHasher.
public sealed class MariaDatabaseRestoreService(IConfiguration configuration, IAccessControl access,
    IOperationLockService locks, IDatabaseBackupService backups, IAuditTrail? auditTrail,
    ILogger<MariaDatabaseRestoreService> logger) : IDatabaseRestoreService
{
    private readonly string backupDirectory = MariaAssetPaths.DatabaseBackups(configuration);

    public async Task<RestoreResult> RestoreAsync(string packageFileName, IProgress<RestoreProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (packageFileName != Path.GetFileName(packageFileName) || packageFileName.Length == 0)
            return RestoreResult.Failed(RestoreRules.PackageNotFoundMessage);
        var packagePath = Path.Combine(backupDirectory, packageFileName);
        if (!File.Exists(packagePath)) return RestoreResult.Failed(RestoreRules.PackageNotFoundMessage);
        if (!MariaDatabaseGuard.IsAllowedDatabase(configuration)) return RestoreResult.Failed(RestoreRules.GenericFailedMessage);

        var operatorName = await access.GetUsernameAsync(cancellationToken).ConfigureAwait(false) ?? "necunoscut";
        var operatorRole = await access.IsAdministratorAsync(cancellationToken).ConfigureAwait(false) ? AccessRoles.Administrator : AccessRoles.LimitedUser;

        progress?.Report(new(RestoreStage.Locking, RestoreRules.StageMessage(RestoreStage.Locking)));
        await using var handle = await locks.TryAcquireAsync("restore", operatorName, operatorRole, cancellationToken).ConfigureAwait(false);
        if (handle is null) return RestoreResult.Failed(OperationLockRules.HeldMessage);

        string? preRestoreFileName = null;
        try
        {
            // Pas 0.
            progress?.Report(new(RestoreStage.SnapshotCurrent, RestoreRules.StageMessage(RestoreStage.SnapshotCurrent)));
            var snapshot = await backups.CreateBackupAsync(BackupKind.PreRestore, null, cancellationToken, handle).ConfigureAwait(false);
            if (!snapshot.Success) return RestoreResult.Failed(snapshot.ErrorMessage ?? RestoreRules.GenericFailedMessage);
            preRestoreFileName = snapshot.PackageFileName;

            // Pas 1 - hash-ul arhivei si al dumpului fata de manifest.
            progress?.Report(new(RestoreStage.VerifyingPackage, RestoreRules.StageMessage(RestoreStage.VerifyingPackage)));
            var manifest = await BackupPackageStore.TryReadManifestAsync(packagePath, cancellationToken).ConfigureAwait(false);
            if (manifest is null) return RestoreResult.Failed(RestoreRules.HashMismatchMessage, preRestoreFileName);
            var sidecarPath = packagePath + ".sha256";
            if (!File.Exists(sidecarPath)) return RestoreResult.Failed(RestoreRules.HashMismatchMessage, preRestoreFileName);
            var expectedArchiveHash = (await File.ReadAllTextAsync(sidecarPath, cancellationToken).ConfigureAwait(false)).Trim();
            var actualArchiveHash = await RestoreHashing.HashFileAsync(packagePath, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(expectedArchiveHash, actualArchiveHash, StringComparison.OrdinalIgnoreCase))
                return RestoreResult.Failed(RestoreRules.HashMismatchMessage, preRestoreFileName);

            // Pas 2 - structura live, comparata cu setul de tabele descris in manifest (numele coloanelor/tipurilor
            // exacte ale pachetului nu sunt derivabile fara sa parseze textul SQL brut al dumpului - vezi comentariul
            // clasei; manifestul contine deja lista tabelelor/numarul de randuri asteptate la momentul backupului).
            progress?.Report(new(RestoreStage.ComparingStructure, RestoreRules.StageMessage(RestoreStage.ComparingStructure)));
            var tables = MariaArchiveSchema.RequiredTables.ToArray();
            if (!new HashSet<string>(manifest.RowCounts.Keys, StringComparer.OrdinalIgnoreCase).SetEquals(tables))
                return RestoreResult.Failed(RestoreRules.StructureMismatchMessage, preRestoreFileName);

            // Pas 3 - continut, cu aceeasi comparatie canonica tip-si-hash folosita la backup (Services/CanonicalRowHasher.cs).
            progress?.Report(new(RestoreStage.ComparingContent, RestoreRules.StageMessage(RestoreStage.ComparingContent)));
            CanonicalSnapshot live;
            await using (var connection = DatabaseConnections.Create(configuration))
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                live = await CanonicalRowHasher.ComputeAsync(connection, tables, cancellationToken).ConfigureAwait(false);
            }
            if (string.Equals(live.OverallHash, manifest.CanonicalHash, StringComparison.Ordinal))
                return RestoreResult.Failed(RestoreRules.IdenticalContentMessage, preRestoreFileName);

            // Pas 4 - import intr-o schema temporara si comutare atomica prin RENAME TABLE. Necesita contul dedicat
            // cu drepturi CREATE/DROP SCHEMA si RENAME TABLE peste schema vie, "_bak" si "_old" (TODO.md, Task 3,
            // "Decizii acceptate"); acel cont nu e configurat in acest mediu (Database:MigratorUser/MigratorPassword
            // lipsesc din configuratie) - se opreste clar aici in loc sa incerce DDL fara drepturi.
            progress?.Report(new(RestoreStage.Importing, RestoreRules.StageMessage(RestoreStage.Importing)));
            if (string.IsNullOrWhiteSpace(configuration["Database:MigratorUser"]) || string.IsNullOrWhiteSpace(configuration["Database:MigratorPassword"]))
                return RestoreResult.Failed(RestoreRules.MigratorNotConfiguredMessage, preRestoreFileName);

            var swapped = await MariaSchemaSwap.ImportAndSwapAsync(configuration, packagePath, tables, cancellationToken).ConfigureAwait(false);
            if (!swapped) return RestoreResult.Failed(RestoreRules.ImportFailedMessage, preRestoreFileName);

            await AuditRecorder.RecordRestoreAsync(auditTrail, access, AuditEntities.DatabaseBackup, packageFileName,
                $"Pachet folosit: {packageFileName}; pachet pre-restaurare: {preRestoreFileName}", cancellationToken).ConfigureAwait(false);

            progress?.Report(new(RestoreStage.Done, RestoreRules.StageMessage(RestoreStage.Done)));
            return RestoreResult.Ok(preRestoreFileName!);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Database restore failed ({ErrorType}).", exception.GetType().Name);
            progress?.Report(new(RestoreStage.Failed, RestoreRules.StageMessage(RestoreStage.Failed)));
            return RestoreResult.Failed(RestoreRules.GenericFailedMessage, preRestoreFileName);
        }
    }
}

// Pas 4 (MariaDB): unpacks dump.sql, imports it into a fresh "<db>_bak" schema with the mariadb.exe client (the
// CLI, not mariadb-dump - same distribution, docs/CLAUDE_CONECTARE_MARIADB_LOCALA.md section 3), verifies the
// import with CanonicalRowHasher against the same table list, then swaps schemas with one RENAME TABLE statement
// covering every table (live -> "_old", "_bak" -> live), which MariaDB executes as a single atomic operation. Any
// failure drops the incomplete "_bak" schema and leaves the live schema untouched; "_old" is left in place on
// success, exactly as TODO.md's Task 3 specifies, for manual cleanup later. Not executed in this environment - see
// the class-level comment on MariaDatabaseRestoreService.
internal static class MariaSchemaSwap
{
    public static async Task<bool> ImportAndSwapAsync(IConfiguration configuration, string packagePath,
        IReadOnlyList<string> tables, CancellationToken token)
    {
        var liveSchema = configuration["Database:Name"] ?? "BlazorStoc";
        var stagingSchema = $"{liveSchema}_bak";
        var oldSchema = $"{liveSchema}_old";
        var extractDir = Path.Combine(Path.GetTempPath(), $"blazorstoc-restore-{Guid.NewGuid():N}");
        Directory.CreateDirectory(extractDir);
        string? credentialsFile = null;
        try
        {
            ZipFile.ExtractToDirectory(packagePath, extractDir);
            var dumpPath = Path.Combine(extractDir, "dump.sql");
            if (!File.Exists(dumpPath)) return false;

            credentialsFile = await WriteMigratorCredentialsFileAsync(configuration, token).ConfigureAwait(false);

            await using (var migrator = CreateMigratorConnection(configuration, database: null))
            {
                await migrator.OpenAsync(token).ConfigureAwait(false);
                await using var drop = new MySqlCommand($"DROP SCHEMA IF EXISTS `{stagingSchema}`", migrator);
                await drop.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                await using var create = new MySqlCommand($"CREATE SCHEMA `{stagingSchema}` CHARACTER SET utf8mb4 COLLATE utf8mb4_nopad_bin", migrator);
                await create.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }

            var imported = await RunMariaClientAsync(configuration, credentialsFile, stagingSchema, dumpPath, token).ConfigureAwait(false);
            if (!imported) { await DropSchemaAsync(configuration, stagingSchema, token).ConfigureAwait(false); return false; }

            // Verify the staging import matches the package's manifest before touching the live schema.
            CanonicalSnapshot stagingSnapshot;
            await using (var stagingConnection = CreateMigratorConnection(configuration, stagingSchema))
            {
                await stagingConnection.OpenAsync(token).ConfigureAwait(false);
                stagingSnapshot = await CanonicalRowHasher.ComputeAsync(stagingConnection, tables, token).ConfigureAwait(false);
            }
            if (stagingSnapshot.Tables.Count != tables.Count)
            {
                await DropSchemaAsync(configuration, stagingSchema, token).ConfigureAwait(false);
                return false;
            }

            // Single RENAME TABLE across all three schemas - MariaDB performs this as one atomic operation, never
            // leaving an intermediate state visible to any other connection.
            await using (var migrator = CreateMigratorConnection(configuration, database: null))
            {
                await migrator.OpenAsync(token).ConfigureAwait(false);
                await using var dropOld = new MySqlCommand($"DROP SCHEMA IF EXISTS `{oldSchema}`", migrator);
                await dropOld.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                await using var createOld = new MySqlCommand($"CREATE SCHEMA `{oldSchema}` CHARACTER SET utf8mb4 COLLATE utf8mb4_nopad_bin", migrator);
                await createOld.ExecuteNonQueryAsync(token).ConfigureAwait(false);

                var renamePairs = tables.Select(table =>
                    $"`{liveSchema}`.`{table}` TO `{oldSchema}`.`{table}`, `{stagingSchema}`.`{table}` TO `{liveSchema}`.`{table}`");
                await using var rename = new MySqlCommand($"RENAME TABLE {string.Join(", ", renamePairs)}", migrator);
                rename.CommandTimeout = 60;
                await rename.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }
            return true;
        }
        finally
        {
            try { Directory.Delete(extractDir, true); } catch (IOException) { }
            if (credentialsFile is not null) { try { File.Delete(credentialsFile); } catch (IOException) { } }
        }
    }

    private static async Task DropSchemaAsync(IConfiguration configuration, string schema, CancellationToken token)
    {
        await using var migrator = CreateMigratorConnection(configuration, database: null);
        await migrator.OpenAsync(token).ConfigureAwait(false);
        await using var drop = new MySqlCommand($"DROP SCHEMA IF EXISTS `{schema}`", migrator);
        await drop.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    // Same shape as DatabaseConnections.Create, but for the dedicated migrator account and (when database is null)
    // without selecting a fixed schema, since Pas 4 needs to CREATE/DROP schemas at the server level.
    private static MySqlConnection CreateMigratorConnection(IConfiguration configuration, string? database)
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = configuration["Database:Host"] ?? "127.0.0.1",
            Port = configuration.GetValue<uint>("Database:Port", 3307),
            UserID = configuration["Database:MigratorUser"] ?? string.Empty,
            Password = configuration["Database:MigratorPassword"],
            SslMode = Enum.Parse<MySqlSslMode>(configuration["Database:SslMode"] ?? "Required", true),
            CharacterSet = configuration["Database:CharSet"] ?? "utf8mb4",
            ConnectionTimeout = 10,
            DefaultCommandTimeout = 60,
            MaximumPoolSize = 5,
            MinimumPoolSize = 0,
            AllowLoadLocalInfile = false
        };
        if (database is not null) builder.Database = database;
        return new MySqlConnection(builder.ConnectionString);
    }

    private static async Task<string> WriteMigratorCredentialsFileAsync(IConfiguration configuration, CancellationToken token)
    {
        var path = Path.Combine(Path.GetTempPath(), $".blazorstoc-restore-{Guid.NewGuid():N}.cnf");
        var sslMode = configuration["Database:SslMode"] ?? "Required";
        var content = $"[client]\nhost={configuration["Database:Host"] ?? "127.0.0.1"}\n" +
            $"port={configuration.GetValue("Database:Port", 3307)}\nuser={configuration["Database:MigratorUser"]}\n" +
            $"password={configuration["Database:MigratorPassword"]}\nssl-mode={sslMode.ToUpperInvariant()}\n";
        await File.WriteAllTextAsync(path, content, token).ConfigureAwait(false);
        return path;
    }

    private static async Task<bool> RunMariaClientAsync(IConfiguration configuration, string credentialsFile,
        string database, string dumpPath, CancellationToken token)
    {
        var executable = MariaAssetPaths.MariaClientExecutable(configuration);
        if (!File.Exists(executable)) return false;
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add($"--defaults-extra-file={credentialsFile}");
        startInfo.ArgumentList.Add(database);

        using var process = new System.Diagnostics.Process { StartInfo = startInfo };
        process.Start();
        await using (var dumpStream = new FileStream(dumpPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            await dumpStream.CopyToAsync(process.StandardInput.BaseStream, token).ConfigureAwait(false);
        process.StandardInput.Close();
        var errorTask = process.StandardError.ReadToEndAsync(token);
        await Task.WhenAll(errorTask, process.WaitForExitAsync(token)).ConfigureAwait(false);
        return process.ExitCode == 0;
    }
}
