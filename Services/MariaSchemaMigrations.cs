using System.Reflection;
using MySqlConnector;

namespace BlazorStoc.Services;

// MariaDB schema changes. The application account (blazorstoc_dev) has no DDL rights; the dedicated account
// blazorstoc_migrator (CREATE/ALTER/INDEX/DROP/REFERENCES on the BlazorStoc schema only, no data rights, Subtask 2.9)
// applies them. Every statement is idempotent (ADD COLUMN IF NOT EXISTS), so no version table is needed - the
// migrator cannot write to app_metadata - and running the list twice, or on a database that already has the
// changes, does nothing. A change is a new file Assets/Migrations/NNN.sql (embedded in the assembly) AND an entry in
// database/mariadb/schema-mariadb.sql (fresh installs).
public sealed record MariaMigration(int Version, string Description, IReadOnlyList<string> Statements,
    IReadOnlyList<(string Table, string Column)> ExpectedColumns);

// The migrations are data, one file each, read in the order of their version. File format (lines before the first "-- statement" other
// than the keys below, such as "-- note:", are explanations and are ignored):
//   -- version: 38
//   -- description: text shown in the log
//   -- column: table.column          (one per column the code expects to exist after the migration)
//   -- statement                     (starts a statement; its text runs to the next "-- statement" or the end of the file)
//   ALTER TABLE ...
public static class MariaSchemaMigrations
{
    public static readonly IReadOnlyList<MariaMigration> All = Load();

    private static IReadOnlyList<MariaMigration> Load()
    {
        var assembly = typeof(MariaSchemaMigrations).Assembly;
        return assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith("BlazorStoc.Migrations.", StringComparison.Ordinal) && name.EndsWith(".sql", StringComparison.Ordinal))
            .Select(name =>
            {
                using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!, System.Text.Encoding.UTF8);
                return Parse(name, reader.ReadToEnd());
            })
            .OrderBy(migration => migration.Version)
            .ToArray();
    }

    internal static MariaMigration Parse(string source, string text)
    {
        int? version = null;
        string? description = null;
        var columns = new List<(string Table, string Column)>();
        var statements = new List<string>();
        System.Text.StringBuilder? statement = null;
        void Close()
        {
            if (statement is not null) statements.Add(statement.ToString().Trim('\n'));
            statement = null;
        }
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (line == "-- statement") { Close(); statement = new(); continue; }
            if (statement is not null) { statement.Append(line).Append('\n'); continue; }
            if (line.StartsWith("-- version:", StringComparison.Ordinal)) version = int.Parse(line["-- version:".Length..].Trim());
            else if (line.StartsWith("-- description:", StringComparison.Ordinal)) description = line["-- description:".Length..].Trim();
            else if (line.StartsWith("-- column:", StringComparison.Ordinal))
            {
                var parts = line["-- column:".Length..].Trim().Split('.');
                if (parts.Length != 2) throw new InvalidOperationException($"Coloană nevalidă în {source}: „{line}”.");
                columns.Add((parts[0], parts[1]));
            }
        }
        Close();
        if (version is null || description is null || statements.Count == 0)
            throw new InvalidOperationException($"Migrarea {source} nu are versiune, descriere sau instrucțiuni.");
        return new MariaMigration(version.Value, description, statements, columns);
    }
}

public sealed record MariaMigrationReport(bool MigratorConfigured, IReadOnlyList<string> Applied, IReadOnlyList<string> MissingColumns);

public sealed class MariaSchemaMigrator(IConfiguration configuration, ILogger<MariaSchemaMigrator> logger)
{
    public bool MigratorConfigured =>
        !string.IsNullOrWhiteSpace(configuration["Database:MigratorUser"]) && !string.IsNullOrWhiteSpace(configuration["Database:MigratorPassword"]);

    /// <summary>Columns the code expects that the live schema does not have yet (checked with the application account).</summary>
    public async Task<IReadOnlyList<string>> FindMissingColumnsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var command = new MySqlCommand("""
            SELECT TABLE_NAME, COLUMN_NAME FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE()
            """, connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) existing.Add($"{reader.GetString(0)}.{reader.GetString(1)}");
        return MariaSchemaMigrations.All.SelectMany(migration => migration.ExpectedColumns)
            .Select(column => $"{column.Table}.{column.Column}").Where(column => !existing.Contains(column)).ToArray();
    }

    /// <summary>Applies every migration with the migrator account, then re-checks the schema with the application account.</summary>
    public async Task<MariaMigrationReport> MigrateAsync(CancellationToken cancellationToken = default)
    {
        if (!MariaDatabaseGuard.IsAllowedDatabase(configuration))
            throw new InvalidOperationException("Migrarea schemei este permisa numai pe baza BlazorStoc.");
        if (!MigratorConfigured) return new(false, [], await FindMissingColumnsAsync(cancellationToken).ConfigureAwait(false));
        var applied = new List<string>();
        var before = await FindMissingColumnsAsync(cancellationToken).ConfigureAwait(false);
        if (before.Count > 0)
        {
            await using var connection = DatabaseConnections.CreateMigrator(configuration);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            foreach (var migration in MariaSchemaMigrations.All)
            {
                foreach (var statement in migration.Statements)
                {
                    await using var command = new MySqlCommand(statement, connection) { CommandTimeout = 120 };
                    await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
                applied.Add($"{migration.Version}: {migration.Description}");
                logger.LogInformation("MariaDB schema migration {Version} applied ({Description}).", migration.Version, migration.Description);
            }
        }
        await NormalizeBeneficiaryKeysAsync(cancellationToken).ConfigureAwait(false);
        await LinkTemplateSuppliersAsync(cancellationToken).ConfigureAwait(false);
        return new(true, applied, await FindMissingColumnsAsync(cancellationToken).ConfigureAwait(false));
    }

    // Templates of a supplier already in the register are linked to it by id (migration 14 adds the column; the migrator account has no UPDATE
    // right, so the link is written with the application account). Idempotent.
    private async Task LinkTemplateSuppliersAsync(CancellationToken cancellationToken)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("""
            UPDATE `invoice_templates` t INNER JOIN `suppliers` s ON s.normalized_cui COLLATE utf8mb4_nopad_bin = t.supplier_cui COLLATE utf8mb4_nopad_bin AND s.country = 'RO'
            SET t.supplier_id = s.id WHERE t.supplier_id IS NULL
            """, connection) { CommandTimeout = 60 };
        try { await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false); }
        catch (MySqlException exception) when (exception.Number == 1054) { }   // the column is not there yet (migration not applied)
    }

    // The key of a legal-person beneficiary is the CUI digits ("RO123" and "123" are one). Rows saved with the old key are rewritten with the
    // application account (the migrator account has no UPDATE right); IGNORE keeps the old key of a pair that would collide. Idempotent.
    private async Task NormalizeBeneficiaryKeysAsync(CancellationToken cancellationToken)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("""
            UPDATE IGNORE `beneficiaries`
            SET `normalized_cui` = TRIM(LEADING '0' FROM IF(`normalized_cui` LIKE 'RO%', SUBSTRING(`normalized_cui`, 3), `normalized_cui`))
            WHERE `normalized_cui` REGEXP '^(RO)?0*[0-9]+$' AND (`normalized_cui` LIKE 'RO%' OR `normalized_cui` LIKE '0%')
            """, connection) { CommandTimeout = 60 };
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}