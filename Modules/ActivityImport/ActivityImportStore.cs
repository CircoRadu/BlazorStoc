using System.Text.Json;
using BlazorStoc.Services;
using MySqlConnector;

namespace BlazorStoc.Modules.ActivityImport;

// The tables the import runs share: the ties between ActivityApp rows and BlazorStoc rows (activity_import_map), the record of each simulation and application
// (activity_import_runs) and the problems left for the administrator (activity_import_issues). Used by the partner import and the ticket import.
internal static class ImportStore
{
    // One run at a time in the application, of any kind of import.
    public static readonly SemaphoreSlim Gate = new(1, 1);

    public static async Task<List<ImportMapEntry>> ReadMapAsync(IConfiguration configuration, CancellationToken cancellationToken)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("SELECT entity, source_id, local_id FROM activity_import_map", connection);
        var result = new List<ImportMapEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(new ImportMapEntry(reader.GetString(0), reader.GetInt64(1), reader.GetInt32(2)));
        return result;
    }

    public static async Task DeleteMapAsync(IConfiguration configuration, string entity, long sourceId, CancellationToken cancellationToken)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("DELETE FROM activity_import_map WHERE entity=@entity AND source_id=@source", connection);
        command.Parameters.AddWithValue("@entity", entity);
        command.Parameters.AddWithValue("@source", sourceId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task WriteMapAsync(IConfiguration configuration, string entity, long sourceId, int localId, string origin, string hash, CancellationToken cancellationToken)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("""
            INSERT INTO activity_import_map (entity, source_id, local_id, origin, content_hash, imported_utc) VALUES (@entity, @source, @local, @origin, @hash, @now)
            ON DUPLICATE KEY UPDATE local_id=@local, origin=@origin, content_hash=@hash, imported_utc=@now
            """, connection);
        command.Parameters.AddWithValue("@entity", entity);
        command.Parameters.AddWithValue("@source", sourceId);
        command.Parameters.AddWithValue("@local", localId);
        command.Parameters.AddWithValue("@origin", origin);
        command.Parameters.AddWithValue("@hash", hash);
        command.Parameters.AddWithValue("@now", DateTime.UtcNow);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<long> StartRunAsync(IConfiguration configuration, string kind, DateTime started, string actor, CancellationToken cancellationToken)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        // A run still marked as running is one that was cut off (the application stopped): the gate allows one at a time.
        await using (var stale = new MySqlCommand("UPDATE activity_import_runs SET status='interrupted', finished_utc=@now WHERE status='running'", connection))
        {
            stale.Parameters.AddWithValue("@now", DateTime.UtcNow);
            await stale.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await using var command = new MySqlCommand("INSERT INTO activity_import_runs (kind, started_utc, started_by, status) VALUES (@kind, @started, @by, 'running')", connection);
        command.Parameters.AddWithValue("@kind", kind);
        command.Parameters.AddWithValue("@started", started);
        command.Parameters.AddWithValue("@by", actor);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return command.LastInsertedId;
    }

    // Not cancellable: the record of a run is written even when the run was cut off.
    public static async Task FinishRunAsync(IConfiguration configuration, long runId, string status, object? report, string? error)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, CancellationToken.None).ConfigureAwait(false);
        await using var command = new MySqlCommand("UPDATE activity_import_runs SET status=@status, finished_utc=@now, report=@report WHERE id=@id", connection);
        command.Parameters.AddWithValue("@status", status);
        command.Parameters.AddWithValue("@now", DateTime.UtcNow);
        command.Parameters.AddWithValue("@report", report is not null ? JsonSerializer.Serialize(report) : error is not null ? JsonSerializer.Serialize(new { error }) : DBNull.Value);
        command.Parameters.AddWithValue("@id", runId);
        await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>The problems of the last application of these entities replace their open ones from earlier runs (a problem that is gone is not shown any more); resolved ones are kept.</summary>
    public static async Task RecordIssuesAsync(IConfiguration configuration, long runId, IReadOnlyList<string> entities, IEnumerable<(string Entity, long SourceId, string Name, string Message)> problems, CancellationToken cancellationToken)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        var names = string.Join(", ", entities.Select((_, index) => $"@entity{index}"));
        await using (var clear = new MySqlCommand($"DELETE FROM activity_import_issues WHERE resolved = 0 AND entity IN ({names})", connection))
        {
            for (var index = 0; index < entities.Count; index++) clear.Parameters.AddWithValue($"@entity{index}", entities[index]);
            await clear.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        foreach (var problem in problems)
        {
            await using var command = new MySqlCommand("INSERT INTO activity_import_issues (run_id, entity, source_id, source_name, message, created_utc) VALUES (@run, @entity, @source, @name, @message, @now)", connection);
            command.Parameters.AddWithValue("@run", runId);
            command.Parameters.AddWithValue("@entity", problem.Entity);
            command.Parameters.AddWithValue("@source", problem.SourceId);
            command.Parameters.AddWithValue("@name", problem.Name.Length > 200 ? problem.Name[..200] : problem.Name);
            command.Parameters.AddWithValue("@message", problem.Message.Length > 500 ? problem.Message[..500] : problem.Message);
            command.Parameters.AddWithValue("@now", DateTime.UtcNow);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
