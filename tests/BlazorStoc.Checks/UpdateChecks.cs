using System.Text.RegularExpressions;
using BlazorStoc.Services;
using BlazorStoc.Startup;
using Microsoft.Extensions.Configuration;

namespace BlazorStoc.Checks;

// Stage 1 of the application update (docs/PROPUNERE_ACTUALIZARE.md): the version of the application, the ledger of the applied migrations (what a run applies, the checksums,
// the drift of an edited migration) and the data files kept outside the folder of the code. No database: the ledger against MariaDB is in MariaExtendedChecks (Schema ledger).
public static class UpdateChecks
{
    public static void Run(Action<bool, string> check)
    {
        Version(check);
        MigrationFiles(check);
        Planning(check);
        Drift(check);
        DataOutsideCode(check);
    }

    private static void Version(Action<bool, string> check) =>
        check(Regex.IsMatch(AppVersion.Current, @"^\d+\.\d+\.\d+$"), $"Update: the version of the application is major.minor.patch without build metadata (\"{AppVersion.Current}\")");

    private static void MigrationFiles(Action<bool, string> check)
    {
        var all = MariaSchemaMigrations.All;
        check(all.Select(migration => migration.Version).SequenceEqual(Enumerable.Range(1, all.Count)), "Update: the migrations are numbered 1..N with no gap and no repeat (a gap hides a missing file)");
        check(all.All(migration => migration.ExpectedColumns.Count > 0), "Update: every migration lists at least one column it brings (the ledger and the schema check rely on it)");
        check(all.All(migration => Regex.IsMatch(migration.Checksum, "^[0-9a-f]{64}$")) && all.Select(migration => migration.Checksum).Distinct().Count() == all.Count,
            "Update: every migration has its own SHA-256 checksum");
        var ledgerMigration = all.SingleOrDefault(migration => migration.Version == 43);
        check(ledgerMigration is not null && ledgerMigration.ExpectedColumns.Any(column => column.Table == "schema_migrations" && column.Column == "checksum"),
            "Update: migration 43 creates the ledger table schema_migrations");
        const string text = "-- version: 900\n-- description: probe\n-- column: t.c\n-- statement\nSELECT 1\n";
        check(MariaSchemaMigrations.Parse("probe", text).Checksum == MariaSchemaMigrations.Parse("probe", text.Replace("\n", "\r\n")).Checksum
            && MariaSchemaMigrations.Parse("probe", text).Checksum != MariaSchemaMigrations.Parse("probe", text.Replace("SELECT 1", "SELECT 2")).Checksum,
            "Update: the checksum ignores the line ends of the file (Git) but sees a change of the text");
    }

    private static IReadOnlyDictionary<int, SchemaLedgerRow> LedgerOf(IEnumerable<MariaMigration> migrations) =>
        migrations.ToDictionary(migration => migration.Version, migration =>
            new SchemaLedgerRow(migration.Version, migration.Description, migration.Checksum, MariaSchemaLedger.BaselineOrigin, DateTime.UtcNow, "0.0.1"));

    private static void Planning(Action<bool, string> check)
    {
        var all = MariaSchemaMigrations.All;
        var last = all[^1];
        var withoutLast = LedgerOf(all.Where(migration => migration.Version != last.Version));
        var lastColumns = last.ExpectedColumns.Select(column => $"{column.Table}.{column.Column}").ToArray();

        var older = MariaSchemaMigrator.Plan(all, null, lastColumns);
        check(older.Baseline && older.ToRun.Count == all.Count && older.ToRecord.Count == all.Count, "Update: a database without the ledger runs every migration once and records them as baseline");
        var empty = MariaSchemaMigrator.Plan(all, new Dictionary<int, SchemaLedgerRow>(), []);
        check(empty.Baseline && empty.ToRun.Count == all.Count, "Update: an empty ledger (first schema) is also a baseline");
        var current = MariaSchemaMigrator.Plan(all, LedgerOf(all), []);
        check(!current.Baseline && current.ToRun.Count == 0 && current.ToRecord.Count == 0, "Update: with the ledger complete and no column missing nothing runs");
        var newer = MariaSchemaMigrator.Plan(all, withoutLast, lastColumns);
        check(!newer.Baseline && newer.ToRun.Select(migration => migration.Version).SequenceEqual([last.Version]) && newer.ToRecord.Count == 1,
            "Update: after an update only the migration the ledger does not have runs, and it is recorded as applied (not baseline)");
        var pendingNoColumns = MariaSchemaMigrator.Plan(all, withoutLast, []);
        check(pendingNoColumns.ToRun.Count == 1, "Update: a pending migration runs even when its columns already exist (idempotent statements)");
        var lost = MariaSchemaMigrator.Plan(all, LedgerOf(all), ["products.some_column_the_ledger_remembers"]);
        check(lost.ToRun.Count == all.Count && lost.ToRecord.Count == 0, "Update: a column missing that no pending migration brings repairs the schema by running the whole list, with nothing new to record");
    }

    private static void Drift(Action<bool, string> check)
    {
        var all = MariaSchemaMigrations.All;
        var ledger = LedgerOf(all).ToDictionary(pair => pair.Key, pair => pair.Value);
        check(MariaSchemaLedger.Drift(ledger, all).Count == 0, "Update: a ledger that matches the files has no drift");
        var edited = all.First(migration => migration.Version == 5);
        ledger[5] = ledger[5] with { Checksum = new string('0', 64) };
        var drift = MariaSchemaLedger.Drift(ledger, all);
        check(drift.Count == 1 && drift[0].StartsWith("5: ", StringComparison.Ordinal) && drift[0].Contains(edited.Description, StringComparison.Ordinal),
            "Update: an applied migration whose file changed is reported as drift with its version");
        ledger.Remove(5);
        check(MariaSchemaLedger.Drift(ledger, all).Count == 0, "Update: a migration the ledger does not have yet is not drift (it is pending)");
    }

    private static void DataOutsideCode(Action<bool, string> check)
    {
        var folder = Path.Combine(Path.GetTempPath(), "blazorstoc-upd-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(folder, "root");
        var legacy = Path.Combine(folder, "code", "data");
        try
        {
            Directory.CreateDirectory(legacy);
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [AppDataRoot.Setting] = root }).Build();
            var target = Path.Combine(root, "data", "probe.json");

            check(AppDataRoot.DataFile(configuration, "probe.json", legacy) == target && !File.Exists(target) && !Directory.Exists(root),
                "Update: with no file anywhere the settings file is at <data root>\\data and nothing is created by asking");
            File.WriteAllText(Path.Combine(legacy, "probe.json"), "{\"old\":1}");
            check(AppDataRoot.DataFile(configuration, "probe.json", legacy) == target && File.ReadAllText(target) == "{\"old\":1}" && File.Exists(Path.Combine(legacy, "probe.json")),
                "Update: the file of the earlier place is copied to the data root once and stays where it was");
            File.WriteAllText(target, "{\"new\":2}");
            check(AppDataRoot.DataFile(configuration, "probe.json", legacy) == target && File.ReadAllText(target) == "{\"new\":2}",
                "Update: a file already in the data root is never overwritten by the old copy");
            check(AppDataRoot.DefaultLogFolder(configuration) == Path.Combine(root, "logs"), "Update: the log folder is, by default, <data root>\\logs");
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }
}
