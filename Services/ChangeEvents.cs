using System.Globalization;
using Microsoft.Data.Sqlite;
using MySqlConnector;

namespace BlazorStoc.Services;

// Task 8: changes made by any application (including external ones that write straight to the database) are recorded
// by database triggers into the small `change_events` table, and a background relay publishes them to the same
// IChangeFeed the pages already subscribe to. Only identifiers are stored: never names, texts, passwords or files.

public sealed record StoredChange(long Id, string EntityType, string Action, string EntityId,
    int? ProjectId, int? ObservationId, int? BeneficiaryId, DateTime CreatedUtc);

public interface IChangeEventSource
{
    // Creates the event table and its triggers when they are missing (idempotent, safe to call repeatedly).
    Task EnsureAsync(CancellationToken cancellationToken);
    Task<long> LatestIdAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<StoredChange>> ReadAfterAsync(long afterId, int limit, CancellationToken cancellationToken);
    Task PurgeAsync(long throughId, DateTime olderThanUtc, CancellationToken cancellationToken);
}

// Which tables are watched and how their rows map onto event columns. Column names are per database engine.
public sealed record WatchedTable(string Table, string EntityType, string IdColumn,
    string? ProjectColumn = null, string? ObservationColumn = null, string? BeneficiaryColumn = null,
    // A table that only changes another entity's data (stock movements change a product) always reports an edit.
    bool AlwaysEdit = false);

public static class ChangeEventTriggers
{
    public const string EventTable = "change_events";

    public static readonly IReadOnlyList<WatchedTable> Sqlite =
    [
        new("products", AuditEntities.Product, "id"),
        // Stock movements change the product's stock and movement list: reported as a change of that product.
        new("stock_movements", AuditEntities.Product, "product_id", AlwaysEdit: true),
        new("web_users", AuditEntities.User, "id"),
        new("projects", AuditEntities.Project, "id", ProjectColumn: "id", BeneficiaryColumn: "beneficiary_id"),
        new("project_observations", AuditEntities.ProjectObservation, "id", ProjectColumn: "project_id", ObservationColumn: "id"),
        new("project_observation_files", AuditEntities.ProjectObservationFile, "id", ObservationColumn: "observation_id")
    ];

    public static readonly IReadOnlyList<WatchedTable> Maria =
    [
        new("produs", AuditEntities.Product, "id_produs"),
        new("io", AuditEntities.Product, "id_produs", AlwaysEdit: true),
        new("web_user", AuditEntities.User, "id_web_user"),
        new("project", AuditEntities.Project, "id_project", ProjectColumn: "id_project", BeneficiaryColumn: "id_beneficiar"),
        new("project_observation", AuditEntities.ProjectObservation, "id_observation", ProjectColumn: "id_project", ObservationColumn: "id_observation"),
        new("project_observation_file", AuditEntities.ProjectObservationFile, "id_file", ObservationColumn: "id_observation")
    ];

    private static readonly (string Suffix, string Timing, string Row, string Action)[] Operations =
    [
        ("i", "INSERT", "NEW", AuditActions.Create),
        ("u", "UPDATE", "NEW", AuditActions.Edit),
        ("d", "DELETE", "OLD", AuditActions.Delete)
    ];

    public static string TriggerName(WatchedTable table, string suffix) => $"trg_chg_{table.Table}_{suffix}";

    // Adding, editing or removing a stock movement is reported as an edit of its product, so the product page
    // refreshes the same way whatever happened to the movement.
    private static string ActionFor(WatchedTable table, string action) => table.AlwaysEdit ? AuditActions.Edit : action;

    // Body shared by both engines: the INSERT into the event table. `now` is the engine's UTC timestamp expression.
    private static string InsertStatement(WatchedTable table, string row, string action, string now)
    {
        static string Column(string row, string? column) => column is null ? "NULL" : $"{row}.{column}";
        return $"INSERT INTO {EventTable}(entity_type,action,entity_id,project_id,observation_id,beneficiary_id,created_utc) " +
               $"VALUES('{table.EntityType}','{ActionFor(table, action)}',CAST({row}.{table.IdColumn} AS CHAR)," +
               $"{Column(row, table.ProjectColumn)},{Column(row, table.ObservationColumn)},{Column(row, table.BeneficiaryColumn)},{now})";
    }

    public static string SqliteSchema()
    {
        var sql = new System.Text.StringBuilder();
        sql.AppendLine($"""
            CREATE TABLE IF NOT EXISTS {EventTable} (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                entity_type TEXT NOT NULL,
                action TEXT NOT NULL,
                entity_id TEXT NOT NULL,
                project_id INTEGER NULL,
                observation_id INTEGER NULL,
                beneficiary_id INTEGER NULL,
                created_utc TEXT NOT NULL
            );
            """);
        foreach (var table in Sqlite)
            foreach (var (suffix, timing, row, action) in Operations)
                sql.AppendLine($"CREATE TRIGGER IF NOT EXISTS {TriggerName(table, suffix)} AFTER {timing} ON {table.Table} " +
                               $"BEGIN {InsertStatement(table, row, action, "strftime('%Y-%m-%dT%H:%M:%fZ','now')")}; END;");
        return sql.ToString();
    }

    public const string MariaTableSql = $"""
        CREATE TABLE IF NOT EXISTS {EventTable} (
            id BIGINT NOT NULL AUTO_INCREMENT,
            entity_type VARCHAR(40) NOT NULL,
            action VARCHAR(20) NOT NULL,
            entity_id VARCHAR(40) NOT NULL,
            project_id INT NULL,
            observation_id INT NULL,
            beneficiary_id INT NULL,
            created_utc DATETIME(6) NOT NULL,
            PRIMARY KEY(id),
            INDEX ix_change_events_created(created_utc)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """;

    public static string MariaTriggerSql(WatchedTable table, string suffix) =>
        Operations.Where(operation => operation.Suffix == suffix).Select(operation =>
            $"CREATE TRIGGER {TriggerName(table, suffix)} AFTER {operation.Timing} ON {table.Table} FOR EACH ROW " +
            InsertStatement(table, operation.Row, operation.Action, "UTC_TIMESTAMP(6)")).Single();

    public static IEnumerable<string> Suffixes => Operations.Select(operation => operation.Suffix);
}

public sealed class SqliteChangeEventSource(SqliteLocalStore store) : IChangeEventSource
{
    // The triggers are created together with the rest of the local schema (SqliteLocalStore.InitializeAsync).
    public async Task EnsureAsync(CancellationToken cancellationToken)
    {
        await using var connection = await store.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<long> LatestIdAsync(CancellationToken cancellationToken)
    {
        await using var connection = await store.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = SqliteLocalStore.Command(connection, null,
            $"SELECT COALESCE(MAX(id), 0) FROM {ChangeEventTriggers.EventTable}");
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    public async Task<IReadOnlyList<StoredChange>> ReadAfterAsync(long afterId, int limit, CancellationToken cancellationToken)
    {
        await using var connection = await store.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = SqliteLocalStore.Command(connection, null, $"""
            SELECT id,entity_type,action,entity_id,project_id,observation_id,beneficiary_id,created_utc
            FROM {ChangeEventTriggers.EventTable} WHERE id>@after ORDER BY id LIMIT @limit
            """, ("@after", afterId), ("@limit", limit));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<StoredChange>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4), reader.IsDBNull(5) ? null : reader.GetInt32(5),
                reader.IsDBNull(6) ? null : reader.GetInt32(6),
                DateTime.Parse(reader.GetString(7), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)));
        return result;
    }

    public async Task PurgeAsync(long throughId, DateTime olderThanUtc, CancellationToken cancellationToken)
    {
        await using var connection = await store.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = SqliteLocalStore.Command(connection, null,
            $"DELETE FROM {ChangeEventTriggers.EventTable} WHERE id<=@through AND created_utc<@older",
            ("@through", throughId), ("@older", olderThanUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture)));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}

// MariaDB: the event table and the triggers are created by the application on first use, and re-checked periodically
// because some watched tables (projects, web users) are created lazily by their own modules. The database account
// needs CREATE and TRIGGER privileges; without them the relay logs a warning and the periodic page sync remains.
public sealed class MariaChangeEventSource(IConfiguration configuration) : IChangeEventSource
{
    public async Task EnsureAsync(CancellationToken cancellationToken)
    {
        await using var connection = DatabaseConnections.Create(configuration);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using (var table = new MySqlCommand(ChangeEventTriggers.MariaTableSql, connection))
            await table.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        foreach (var watched in ChangeEventTriggers.Maria)
        {
            if (!await TableExistsAsync(connection, watched.Table, cancellationToken).ConfigureAwait(false)) continue;
            foreach (var suffix in ChangeEventTriggers.Suffixes)
            {
                if (await TriggerExistsAsync(connection, ChangeEventTriggers.TriggerName(watched, suffix), cancellationToken).ConfigureAwait(false)) continue;
                await using var create = new MySqlCommand(ChangeEventTriggers.MariaTriggerSql(watched, suffix), connection);
                await create.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task<bool> TableExistsAsync(MySqlConnection connection, string table, CancellationToken token)
    {
        await using var command = new MySqlCommand(
            "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME=@name", connection);
        command.Parameters.AddWithValue("@name", table);
        return Convert.ToInt32(await command.ExecuteScalarAsync(token).ConfigureAwait(false), CultureInfo.InvariantCulture) > 0;
    }

    private static async Task<bool> TriggerExistsAsync(MySqlConnection connection, string trigger, CancellationToken token)
    {
        await using var command = new MySqlCommand(
            "SELECT COUNT(*) FROM information_schema.TRIGGERS WHERE TRIGGER_SCHEMA=DATABASE() AND TRIGGER_NAME=@name", connection);
        command.Parameters.AddWithValue("@name", trigger);
        return Convert.ToInt32(await command.ExecuteScalarAsync(token).ConfigureAwait(false), CultureInfo.InvariantCulture) > 0;
    }

    public async Task<long> LatestIdAsync(CancellationToken cancellationToken)
    {
        await using var connection = DatabaseConnections.Create(configuration);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand($"SELECT COALESCE(MAX(id),0) FROM {ChangeEventTriggers.EventTable}", connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    public async Task<IReadOnlyList<StoredChange>> ReadAfterAsync(long afterId, int limit, CancellationToken cancellationToken)
    {
        await using var connection = DatabaseConnections.Create(configuration);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand($"""
            SELECT id,entity_type,action,entity_id,project_id,observation_id,beneficiary_id,created_utc
            FROM {ChangeEventTriggers.EventTable} WHERE id>@after ORDER BY id LIMIT @limit
            """, connection);
        command.Parameters.AddWithValue("@after", afterId);
        command.Parameters.AddWithValue("@limit", limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<StoredChange>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4), reader.IsDBNull(5) ? null : reader.GetInt32(5),
                reader.IsDBNull(6) ? null : reader.GetInt32(6), DateTime.SpecifyKind(reader.GetDateTime(7), DateTimeKind.Utc)));
        return result;
    }

    public async Task PurgeAsync(long throughId, DateTime olderThanUtc, CancellationToken cancellationToken)
    {
        await using var connection = DatabaseConnections.Create(configuration);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand(
            $"DELETE FROM {ChangeEventTriggers.EventTable} WHERE id<=@through AND created_utc<@older", connection);
        command.Parameters.AddWithValue("@through", throughId);
        command.Parameters.AddWithValue("@older", olderThanUtc);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}

// Reads new events and publishes each of them exactly once to the in-process feed.
//  - The cursor only moves forward, so an event is never published twice (idempotent processing).
//  - An event is held back for a short grace period so that the change published directly by the session that made
//    it (which carries its origin) reaches the ledger first and the trigger's copy of the same change is dropped.
//  - A failing read is logged and retried on the next tick; the pages' periodic synchronisation stays as the fallback.
public sealed class ChangeEventRelay(IChangeEventSource source, IChangeFeed feed, ILogger<ChangeEventRelay> logger,
    IConfiguration? configuration = null, TimeProvider? timeProvider = null) : BackgroundService
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly TimeSpan pollInterval = TimeSpan.FromMilliseconds(Math.Max(100, configuration?.GetValue("Sync:PollMilliseconds", 1000) ?? 1000));
    private readonly TimeSpan grace = TimeSpan.FromMilliseconds(Math.Max(0, configuration?.GetValue("Sync:GraceMilliseconds", 1500) ?? 1500));
    private readonly TimeSpan retention = TimeSpan.FromHours(24);
    private readonly TimeSpan ensureInterval = TimeSpan.FromMinutes(5);
    private readonly Dictionary<long, DateTimeOffset> firstSeen = [];
    private long cursor;
    private bool started;
    private DateTimeOffset nextEnsure, nextPurge;
    private DateTimeOffset lastWarning;

    public long Cursor => cursor;

    // Publishes the events whose grace period is over and returns how many were published (used directly by tests).
    public async Task<int> PollOnceAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        if (!started)
        {
            await source.EnsureAsync(cancellationToken).ConfigureAwait(false);
            cursor = await source.LatestIdAsync(cancellationToken).ConfigureAwait(false);
            started = true;
            nextEnsure = now + ensureInterval;
            nextPurge = now + TimeSpan.FromMinutes(1);
            return 0;
        }
        if (now >= nextEnsure)
        {
            nextEnsure = now + ensureInterval;
            await source.EnsureAsync(cancellationToken).ConfigureAwait(false);
        }

        var published = 0;
        var batch = await source.ReadAfterAsync(cursor, 200, cancellationToken).ConfigureAwait(false);
        // Every event of the batch gets its arrival time first, so the grace period counts from when it was first read.
        foreach (var stored in batch) firstSeen.TryAdd(stored.Id, now);
        foreach (var stored in batch)
        {
            if (now - firstSeen[stored.Id] < grace) break;
            cursor = stored.Id;
            firstSeen.Remove(stored.Id);
            if (feed is ILocalChangeLedger ledger && ledger.TryConsumeLocal(stored.EntityType, stored.Action, stored.EntityId)) continue;
            feed.Publish(new(stored.EntityType, stored.Action, stored.EntityId, Guid.Empty, stored.CreatedUtc,
                stored.ProjectId, stored.ObservationId, stored.BeneficiaryId));
            published++;
        }

        if (now >= nextPurge)
        {
            nextPurge = now + TimeSpan.FromHours(1);
            await source.PurgeAsync(cursor, now.UtcDateTime - retention, cancellationToken).ConfigureAwait(false);
        }
        return published;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await PollOnceAsync(stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                // At most one warning per minute; the source is retried on the next tick.
                if (clock.GetUtcNow() - lastWarning > TimeSpan.FromMinutes(1))
                {
                    lastWarning = clock.GetUtcNow();
                    logger.LogWarning("Change events could not be read ({ErrorType}); pages fall back to periodic refresh.", exception.GetType().Name);
                }
            }
            try { await Task.Delay(pollInterval, clock, stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }
}
