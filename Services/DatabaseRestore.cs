using System.IO.Compression;
using System.Security.Cryptography;
using MySqlConnector;

namespace BlazorStoc.Services;

// Task 3, subtask 3.4 - "Pasii de restaurare (executati numai dupa confirmare)". Runs after subtask 3.3's popup
// confirms the operator typed the exact word "confirma". The MariaDB implementation reuses the same shared operation lock, backup directory,
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
    public const string MigratorNotConfiguredMessage = "Restaurarea reala necesita contul MariaDB dedicat de restaurare (schema temporara/comutare), neconfigurat inca pe acest server.";

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
        // This flow holds the lock: its own data access must pass the write freeze that refuses every other session.
        using var maintenanceScope = MaintenanceGate.EnterOwnerScope();

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
            await using (var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false))
            {
                live = await CanonicalRowHasher.ComputeAsync(connection, tables, cancellationToken).ConfigureAwait(false);
            }
            if (string.Equals(live.OverallHash, manifest.CanonicalHash, StringComparison.Ordinal))
                return RestoreResult.Failed(RestoreRules.IdenticalContentMessage, preRestoreFileName);

            // Pas 4 - import intr-o schema temporara si comutare atomica prin RENAME TABLE. Necesita contul dedicat
            // cu drepturi CREATE/DROP SCHEMA si RENAME TABLE peste schema vie, "_bak" si "_old" (TODO.md, Task 3,
            // "Decizii acceptate"); acel cont (blazorstoc_restore, Database:RestoreUser/RestorePassword, local-secrets/restore-account.private.json)
            // nu e configurat - se opreste clar aici in loc sa incerce DDL fara drepturi.
            progress?.Report(new(RestoreStage.Importing, RestoreRules.StageMessage(RestoreStage.Importing)));
            if (!MariaRestoreAccount.IsConfigured(configuration))
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

// The dedicated restore account (blazorstoc_restore, local-secrets/create-restore-account.sql): ALL on "<db>_bak" and
// "<db>_old", ALTER/DROP/CREATE/INSERT/TRIGGER on the live schema, nothing global. Deliberately NOT the migrator account.
// Credentials come from Database:RestoreUser / Database:RestorePassword (private file, never appsettings.json).
public static class MariaRestoreAccount
{
    public static bool IsConfigured(IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(configuration["Database:RestoreUser"]) && !string.IsNullOrWhiteSpace(configuration["Database:RestorePassword"]);
}

// Prepares a mariadb-dump file for import by an account that is not the dump's original definer, and for a swap that
// cannot move tables carrying triggers between schemas (MariaDB/MySQL refuse RENAME TABLE across schemas for such
// tables: "Trigger in wrong schema"):
//  - every DEFINER clause is removed (importing a foreign DEFINER needs the global SET USER privilege, which the
//    restore account does not have; the triggers then belong to the account that creates them);
//  - the trigger blocks (mariadb-dump writes each as SET ... / DELIMITER ;; / CREATE TRIGGER ... / DELIMITER ; / SET ...)
//    go to a separate file, so the tables can be imported and swapped without triggers and the triggers created in the
//    live schema afterwards.
public static class RestoreDumpSplitter
{
    private static readonly System.Text.RegularExpressions.Regex DumpDefiner =
        new(@"/\*!\d+\s+DEFINER\s*=\s*`[^`]*`@`[^`]*`\s*\*/\s?", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex PlainDefiner =
        new(@"DEFINER\s*=\s*`[^`]*`@`[^`]*`\s*", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>Removes the versioned-comment DEFINER clause mariadb-dump writes ("/*!50017 DEFINER=`u`@`h`*/").</summary>
    public static string StripDumpDefiner(string line) => DumpDefiner.Replace(line, string.Empty);

    /// <summary>Removes a plain DEFINER clause, as returned by SHOW CREATE TRIGGER.</summary>
    public static string StripPlainDefiner(string statement) => PlainDefiner.Replace(statement, string.Empty);

    /// <summary>Writes the dump without trigger blocks to <paramref name="mainPath"/> and the trigger blocks to
    /// <paramref name="triggersPath"/>; returns the number of triggers found.</summary>
    public static async Task<int> SplitAsync(string dumpPath, string mainPath, string triggersPath, CancellationToken token)
    {
        var encoding = new System.Text.UTF8Encoding(false);
        var triggerCount = 0;
        await using var main = new StreamWriter(mainPath, false, encoding);
        await using var triggers = new StreamWriter(triggersPath, false, encoding);
        await triggers.WriteLineAsync("SET NAMES utf8mb4;").ConfigureAwait(false);
        using var reader = new StreamReader(dumpPath, System.Text.Encoding.UTF8);
        List<string>? block = null;
        while (await reader.ReadLineAsync(token).ConfigureAwait(false) is { } rawLine)
        {
            var line = StripDumpDefiner(rawLine);
            if (block is null)
            {
                if (line.StartsWith("/*!50003 SET @saved_cs_client", StringComparison.Ordinal)) block = [line];
                else await main.WriteLineAsync(line).ConfigureAwait(false);
                continue;
            }
            block.Add(line);
            if (!line.StartsWith("/*!50003 SET collation_connection", StringComparison.Ordinal) ||
                !line.Contains("@saved_col_connection", StringComparison.Ordinal)) continue;
            var isTrigger = block.Any(item => item.Contains("/*!50003 TRIGGER", StringComparison.Ordinal));
            var target = isTrigger ? triggers : main;
            foreach (var item in block) await target.WriteLineAsync(item).ConfigureAwait(false);
            if (isTrigger) triggerCount++;
            block = null;
        }
        if (block is not null) foreach (var item in block) await main.WriteLineAsync(item).ConfigureAwait(false);
        return triggerCount;
    }
}

// Pas 4 (MariaDB): unpacks dump.sql, imports its tables into a fresh "<db>_bak" schema with the mariadb.exe client
// (the CLI, not mariadb-dump - same distribution), verifies the import with CanonicalRowHasher against the package's
// table list, then swaps the schemas with one RENAME TABLE statement covering every table (live -> "_old", "_bak" ->
// live), which MariaDB executes as a single atomic operation. Because tables with triggers cannot be renamed across
// schemas, the live triggers are dropped right before the RENAME and the package's triggers are created in the live
// schema right after it (RestoreDumpSplitter); that short window is the one non-atomic part. The swap set is every
// base table of the live schema plus every table of the package, so tables added by later migrations do not stay
// behind pointing at renamed parents (a table absent from an older package is recreated by the schema migration at
// the next start). Any failure before the RENAME drops the incomplete "_bak" schema and restores the live triggers;
// "_old" is left in place on success, for manual cleanup later. Not yet exercised against a live MariaDB server.
internal static class MariaSchemaSwap
{
    private sealed record CapturedTrigger(string Name, string SqlMode, string Statement);

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
            var mainPath = Path.Combine(extractDir, "tables.sql");
            var triggersPath = Path.Combine(extractDir, "triggers.sql");
            var triggerCount = await RestoreDumpSplitter.SplitAsync(dumpPath, mainPath, triggersPath, token).ConfigureAwait(false);

            credentialsFile = await WriteCredentialsFileAsync(configuration, token).ConfigureAwait(false);

            await using (var restore = CreateConnection(configuration, database: null))
            {
                await restore.OpenAsync(token).ConfigureAwait(false);
                await ExecuteAsync(restore, $"DROP SCHEMA IF EXISTS `{stagingSchema}`", token).ConfigureAwait(false);
                await ExecuteAsync(restore, $"CREATE SCHEMA `{stagingSchema}` CHARACTER SET utf8mb4 COLLATE utf8mb4_nopad_bin", token).ConfigureAwait(false);
            }

            var imported = await RunMariaClientAsync(configuration, credentialsFile, stagingSchema, mainPath, token).ConfigureAwait(false);
            if (!imported) { await DropSchemaAsync(configuration, stagingSchema, token).ConfigureAwait(false); return false; }

            // Verify the staging import matches the package's manifest before touching the live schema.
            CanonicalSnapshot stagingSnapshot;
            await using (var stagingConnection = CreateConnection(configuration, stagingSchema))
            {
                await stagingConnection.OpenAsync(token).ConfigureAwait(false);
                stagingSnapshot = await CanonicalRowHasher.ComputeAsync(stagingConnection, tables, token).ConfigureAwait(false);
            }
            if (stagingSnapshot.Tables.Count != tables.Count)
            {
                await DropSchemaAsync(configuration, stagingSchema, token).ConfigureAwait(false);
                return false;
            }

            await using var connection = CreateConnection(configuration, database: null);
            await connection.OpenAsync(token).ConfigureAwait(false);
            var liveTables = await ListTablesAsync(connection, liveSchema, token).ConfigureAwait(false);
            var stagingTables = await ListTablesAsync(connection, stagingSchema, token).ConfigureAwait(false);
            var liveTriggers = await CaptureTriggersAsync(connection, liveSchema, token).ConfigureAwait(false);

            await ExecuteAsync(connection, $"DROP SCHEMA IF EXISTS `{oldSchema}`", token).ConfigureAwait(false);
            await ExecuteAsync(connection, $"CREATE SCHEMA `{oldSchema}` CHARACTER SET utf8mb4 COLLATE utf8mb4_nopad_bin", token).ConfigureAwait(false);

            var swapped = false;
            try
            {
                foreach (var trigger in liveTriggers)
                    await ExecuteAsync(connection, $"DROP TRIGGER IF EXISTS `{liveSchema}`.`{trigger.Name}`", token).ConfigureAwait(false);
                // Single RENAME TABLE across the three schemas - MariaDB performs it as one atomic operation.
                var renamePairs = liveTables.Select(table => $"`{liveSchema}`.`{table}` TO `{oldSchema}`.`{table}`")
                    .Concat(stagingTables.Select(table => $"`{stagingSchema}`.`{table}` TO `{liveSchema}`.`{table}`"));
                await ExecuteAsync(connection, $"RENAME TABLE {string.Join(", ", renamePairs)}", token, 60).ConfigureAwait(false);
                swapped = true;
                // "_bak" is empty now (all its tables were moved to the live schema): no reason to keep it.
                await ExecuteAsync(connection, $"DROP SCHEMA IF EXISTS `{stagingSchema}`", token).ConfigureAwait(false);
                if (triggerCount > 0 &&
                    !await RunMariaClientAsync(configuration, credentialsFile, liveSchema, triggersPath, token).ConfigureAwait(false))
                    throw new InvalidOperationException("Triggerele pachetului nu au putut fi create in schema vie dupa comutare.");
                return true;
            }
            catch
            {
                try
                {
                    if (!swapped) await RecreateTriggersAsync(configuration, liveSchema, liveTriggers, token).ConfigureAwait(false);
                    else if (triggerCount > 0) await RunMariaClientAsync(configuration, credentialsFile, liveSchema, triggersPath, token).ConfigureAwait(false);
                }
                catch (Exception) { /* best effort; the original failure below is what gets reported */ }
                throw;
            }
        }
        finally
        {
            try { Directory.Delete(extractDir, true); } catch (IOException) { }
            if (credentialsFile is not null) { try { File.Delete(credentialsFile); } catch (IOException) { } }
        }
    }

    private static async Task ExecuteAsync(MySqlConnection connection, string sql, CancellationToken token, int timeoutSeconds = 30)
    {
        await using var command = new MySqlCommand(sql, connection) { CommandTimeout = timeoutSeconds };
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    private static async Task<List<string>> ListTablesAsync(MySqlConnection connection, string schema, CancellationToken token)
    {
        await using var command = new MySqlCommand(
            "SELECT TABLE_NAME FROM information_schema.TABLES WHERE TABLE_SCHEMA=@schema AND TABLE_TYPE='BASE TABLE' ORDER BY TABLE_NAME", connection);
        command.Parameters.AddWithValue("@schema", schema);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        var result = new List<string>();
        while (await reader.ReadAsync(token).ConfigureAwait(false)) result.Add(reader.GetString(0));
        return result;
    }

    // The live triggers as they are now (definer removed), so a failure before the RENAME can put them back.
    private static async Task<List<CapturedTrigger>> CaptureTriggersAsync(MySqlConnection connection, string schema, CancellationToken token)
    {
        var names = new List<string>();
        await using (var command = new MySqlCommand("SELECT TRIGGER_NAME FROM information_schema.TRIGGERS WHERE TRIGGER_SCHEMA=@schema", connection))
        {
            command.Parameters.AddWithValue("@schema", schema);
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false)) names.Add(reader.GetString(0));
        }
        var result = new List<CapturedTrigger>();
        foreach (var name in names)
        {
            await using var show = new MySqlCommand($"SHOW CREATE TRIGGER `{schema}`.`{name}`", connection);
            await using var reader = await show.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (await reader.ReadAsync(token).ConfigureAwait(false))
                result.Add(new(name, reader.GetString(1), RestoreDumpSplitter.StripPlainDefiner(reader.GetString(2))));
        }
        return result;
    }

    private static async Task RecreateTriggersAsync(IConfiguration configuration, string schema,
        IReadOnlyList<CapturedTrigger> triggers, CancellationToken token)
    {
        await using var connection = CreateConnection(configuration, schema);
        await connection.OpenAsync(token).ConfigureAwait(false);
        foreach (var trigger in triggers)
        {
            await using (var mode = new MySqlCommand("SET SESSION sql_mode=@mode", connection)) { mode.Parameters.AddWithValue("@mode", trigger.SqlMode); await mode.ExecuteNonQueryAsync(token).ConfigureAwait(false); }
            await ExecuteAsync(connection, trigger.Statement, token).ConfigureAwait(false);
        }
    }

    private static async Task DropSchemaAsync(IConfiguration configuration, string schema, CancellationToken token)
    {
        await using var connection = CreateConnection(configuration, database: null);
        await connection.OpenAsync(token).ConfigureAwait(false);
        await ExecuteAsync(connection, $"DROP SCHEMA IF EXISTS `{schema}`", token).ConfigureAwait(false);
    }

    // Same shape as DatabaseConnections.Create, but for the dedicated restore account and (when database is null)
    // without selecting a fixed schema, since Pas 4 needs to CREATE/DROP schemas at the server level.
    private static MySqlConnection CreateConnection(IConfiguration configuration, string? database)
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = configuration["Database:Host"] ?? "127.0.0.1",
            Port = configuration.GetValue<uint>("Database:Port", 3307),
            UserID = configuration["Database:RestoreUser"] ?? string.Empty,
            Password = configuration["Database:RestorePassword"],
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

    private static async Task<string> WriteCredentialsFileAsync(IConfiguration configuration, CancellationToken token)
    {
        var path = Path.Combine(Path.GetTempPath(), $".blazorstoc-restore-{Guid.NewGuid():N}.cnf");
        var sslMode = configuration["Database:SslMode"] ?? "Required";
        var content = $"[client]\nhost={configuration["Database:Host"] ?? "127.0.0.1"}\n" +
            $"port={configuration.GetValue("Database:Port", 3307)}\nuser={configuration["Database:RestoreUser"]}\n" +
            $"password={configuration["Database:RestorePassword"]}\n{MariaClientSsl.Options(sslMode)}";
        await File.WriteAllTextAsync(path, content, token).ConfigureAwait(false);
        return path;
    }

    private static async Task<bool> RunMariaClientAsync(IConfiguration configuration, string credentialsFile,
        string database, string sqlPath, CancellationToken token)
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
        startInfo.ArgumentList.Add("--default-character-set=utf8mb4");
        startInfo.ArgumentList.Add(database);

        using var process = new System.Diagnostics.Process { StartInfo = startInfo };
        process.Start();
        await using (var sqlStream = new FileStream(sqlPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            await sqlStream.CopyToAsync(process.StandardInput.BaseStream, token).ConfigureAwait(false);
        process.StandardInput.Close();
        var errorTask = process.StandardError.ReadToEndAsync(token);
        await Task.WhenAll(errorTask, process.WaitForExitAsync(token)).ConfigureAwait(false);
        return process.ExitCode == 0;
    }
}
