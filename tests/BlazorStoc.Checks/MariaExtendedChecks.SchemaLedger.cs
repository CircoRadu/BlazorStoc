using BlazorStoc.Services;
using Microsoft.Extensions.Configuration;
using MySqlConnector;

namespace BlazorStoc.Checks;

public static partial class MariaExtendedChecks
{
    // ---- Ledger of the applied migrations (schema_migrations, migration 43): rows written with the application account -------------------
    // The migrator itself is not run here (it is allowed on the BlazorStoc database only); what it decides is in UpdateChecks (Plan). A probe migration with a
    // high version number is recorded and removed, so the real rows of the test database are never touched.
    private static async Task SchemaLedgerAsync(IConfiguration configuration, MySqlConnection probe)
    {
        var version = 9000 + Random.Shared.Next(1, 900);
        var probeChecksum = new string('a', 64);
        var migration = new MariaMigration(version, "probe " + Suffix(), ["SELECT 1"], [("probe", "probe")], probeChecksum);
        var at = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);
        try
        {
            var start = await MariaSchemaLedger.ReadAsync(configuration);
            Check(start is not null, "The ledger table exists (migration 43 is applied to the test database)");

            await MariaSchemaLedger.RecordAsync(configuration, [migration], MariaSchemaLedger.AppliedOrigin, at);
            var rows = await MariaSchemaLedger.ReadAsync(configuration);
            var row = rows!.GetValueOrDefault(version);
            Check(row is not null && row.Description == migration.Description && row.Checksum == probeChecksum && row.Origin == MariaSchemaLedger.AppliedOrigin
                && row.AppliedUtc == at && row.AppliedUtc.Kind == DateTimeKind.Utc && row.AppliedByVersion == AppVersion.Current,
                "A recorded migration keeps its version, description, checksum, origin, moment (UTC) and the version of the application that applied it");

            await MariaSchemaLedger.RecordAsync(configuration, [migration with { Checksum = new string('b', 64) }], MariaSchemaLedger.BaselineOrigin, at.AddHours(1));
            var again = (await MariaSchemaLedger.ReadAsync(configuration))![version];
            Check(again.Checksum == probeChecksum && again.Origin == MariaSchemaLedger.AppliedOrigin && again.AppliedUtc == at,
                "Recording a version that is already in the ledger changes nothing (the first row stays)");

            var drift = MariaSchemaLedger.Drift((await MariaSchemaLedger.ReadAsync(configuration))!, [migration with { Checksum = new string('b', 64) }]);
            Check(drift.Count == 1 && drift[0].StartsWith($"{version}: ", StringComparison.Ordinal), "A migration file edited after it was recorded shows as drift");
            Check(await MariaSchemaLedger.CurrentVersionAsync(configuration) >= version, "The version of the schema is the highest migration in the ledger");
        }
        finally
        {
            await using var delete = probe.CreateCommand();
            delete.CommandText = "DELETE FROM schema_migrations WHERE version = @version";
            delete.Parameters.AddWithValue("@version", version);
            await delete.ExecuteNonQueryAsync();
        }
        Check(!(await MariaSchemaLedger.ReadAsync(configuration))!.ContainsKey(version), "The probe row is gone after clean-up");
    }
}
