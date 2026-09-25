using System.Globalization;
using MySqlConnector;

namespace BlazorStoc.Services;

// Task 9: a short lease lock taken when a product enters edit mode. One row per product; every state change is a
// single atomic statement executed by the database with the database's own UTC clock, and no transaction or row
// lock stays open while the form is used. The product version check remains the final protection against
// overwriting a change made after a lock expired or was taken over.

public sealed record ProductLock(int ProductId, string Owner, string SessionId, DateTime AcquiredUtc,
    DateTime RenewedUtc, DateTime ExpiresUtc, int RemainingSeconds);

// Acquired: this session now holds the lock. Changed: the lock was newly taken (not merely renewed).
public sealed record LockAttempt(bool Acquired, bool Changed, ProductLock? Lock);

public interface IProductLockRepository
{
    // Takes the lock, or renews it when the same session already holds it (entering edit mode).
    Task<LockAttempt> AcquireAsync(int productId, string sessionId, CancellationToken cancellationToken = default);
    // Heartbeat: extends the lease only while this session still holds the lock. A lock released by an administrator or
    // taken over by another session is never silently re-acquired; Acquired is false and the editor is told it was lost.
    Task<LockAttempt> RenewAsync(int productId, string sessionId, CancellationToken cancellationToken = default);
    Task<ProductLock?> GetAsync(int productId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProductLock>> GetActiveAsync(CancellationToken cancellationToken = default);
    // True when this session held the lock and it was released.
    Task<bool> ReleaseAsync(int productId, string sessionId, CancellationToken cancellationToken = default);
    // Administrators only, with a mandatory reason; returns the lock that was removed (null when there was none).
    Task<ProductLock?> ForceReleaseAsync(int productId, string reason, CancellationToken cancellationToken = default);
}

public sealed class ProductLockException(string message) : Exception(message);

public static class ProductLockRules
{
    // Expires 90 seconds after the last renewal; the page renews every 30 seconds while the form is open, so a
    // closed browser or a lost connection frees the product within 1–2 minutes.
    public const int LeaseSeconds = 90;
    public const int HeartbeatSeconds = 30;

    public static string HeldMessage(ProductLock held) =>
        $"Produsul este editat de {held.Owner} din {held.AcquiredUtc.ToLocalTime():HH:mm}. Îl poți consulta, dar nu îl poți edita până când este eliberat.";

    public static string? ForceReleaseReasonError(string? reason) => ChangeReasonRules.ValidationError(reason);
}

public static class ChangeEntities
{
    // Feed event announcing that a product lock was taken or released; EntityId is the product's identifier.
    public const string ProductLock = "BlocareProdus";
}

internal static class ProductLockAudit
{
    public static Task RecordForcedReleaseAsync(IAuditTrail? trail, IAccessControl? access, ProductLock removed, string productCode,
        string reason, CancellationToken cancellationToken) => AuditRecorder.RecordUnlockAsync(trail, access,
        removed.ProductId.ToString(CultureInfo.InvariantCulture), productCode,
        $"Blocare eliberată forțat; editor: {removed.Owner}, din {removed.AcquiredUtc.ToLocalTime():dd-MM-yyyy HH:mm}", reason, cancellationToken);
}

public sealed class SqliteProductLockRepository(SqliteLocalStore store, IAccessControl? accessControl = null,
    IAuditTrail? auditTrail = null) : IProductLockRepository
{
    private const string Now = "strftime('%Y-%m-%dT%H:%M:%fZ','now')";
    private static readonly string Expiry = $"strftime('%Y-%m-%dT%H:%M:%fZ','now','+{ProductLockRules.LeaseSeconds} seconds')";
    private const string Columns = "product_id,owner_username,session_id,acquired_utc,renewed_utc,expires_utc," +
        "CAST(ROUND((julianday(expires_utc)-julianday('now'))*86400) AS INTEGER)";

    public async Task<LockAttempt> RenewAsync(int productId, string sessionId, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureProductOperatorAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await store.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await ExecuteAsync(connection, $"UPDATE product_locks SET renewed_utc={Now},expires_utc={Expiry} WHERE product_id=@p AND session_id=@s",
                cancellationToken, ("@p", productId), ("@s", sessionId)).ConfigureAwait(false) > 0
            ? new(true, false, await ReadAsync(connection, productId, cancellationToken).ConfigureAwait(false))
            : new(false, false, await ReadAsync(connection, productId, cancellationToken).ConfigureAwait(false));
    }

    public async Task<LockAttempt> AcquireAsync(int productId, string sessionId, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureProductOperatorAsync(cancellationToken).ConfigureAwait(false);
        var owner = accessControl is null ? "sistem" : await accessControl.GetUsernameAsync(cancellationToken).ConfigureAwait(false) ?? "necunoscut";
        await using var connection = await store.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        // 1. Renewal by the session that already holds the lock (also when it lapsed but nobody took it meanwhile).
        if (await ExecuteAsync(connection, $"UPDATE product_locks SET renewed_utc={Now},expires_utc={Expiry} WHERE product_id=@p AND session_id=@s",
                cancellationToken, ("@p", productId), ("@s", sessionId)).ConfigureAwait(false) > 0)
            return new(true, false, await ReadAsync(connection, productId, cancellationToken).ConfigureAwait(false));
        // 2. Taking over a lock whose lease has expired.
        if (await ExecuteAsync(connection, $"""
                UPDATE product_locks SET owner_username=@o,session_id=@s,acquired_utc={Now},renewed_utc={Now},expires_utc={Expiry}
                WHERE product_id=@p AND expires_utc<={Now}
                """, cancellationToken, ("@p", productId), ("@s", sessionId), ("@o", owner)).ConfigureAwait(false) > 0)
            return new(true, true, await ReadAsync(connection, productId, cancellationToken).ConfigureAwait(false));
        // 3. First lock for this product; the primary key lets exactly one of two simultaneous requests insert.
        if (await ExecuteAsync(connection, $"""
                INSERT OR IGNORE INTO product_locks(product_id,owner_username,session_id,acquired_utc,renewed_utc,expires_utc)
                SELECT @p,@o,@s,{Now},{Now},{Expiry} WHERE EXISTS(SELECT 1 FROM products WHERE id=@p)
                """, cancellationToken, ("@p", productId), ("@s", sessionId), ("@o", owner)).ConfigureAwait(false) > 0)
            return new(true, true, await ReadAsync(connection, productId, cancellationToken).ConfigureAwait(false));
        return new(false, false, await ReadAsync(connection, productId, cancellationToken).ConfigureAwait(false));
    }

    public async Task<ProductLock?> GetAsync(int productId, CancellationToken cancellationToken = default)
    {
        await using var connection = await store.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await ReadAsync(connection, productId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ProductLock>> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await store.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = SqliteLocalStore.Command(connection, null,
            $"SELECT {Columns} FROM product_locks WHERE expires_utc>{Now} ORDER BY product_id");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<ProductLock>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(Map(reader));
        return result;
    }

    public async Task<bool> ReleaseAsync(int productId, string sessionId, CancellationToken cancellationToken = default)
    {
        await using var connection = await store.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await ExecuteAsync(connection, "DELETE FROM product_locks WHERE product_id=@p AND session_id=@s",
            cancellationToken, ("@p", productId), ("@s", sessionId)).ConfigureAwait(false) > 0;
    }

    public async Task<ProductLock?> ForceReleaseAsync(int productId, string reason, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var motif = ChangeReasonRules.Normalize(reason);
        if (ProductLockRules.ForceReleaseReasonError(motif) is { } error) throw new ProductLockException(error);
        await using var connection = await store.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var held = await ReadAsync(connection, productId, cancellationToken).ConfigureAwait(false);
        if (held is null) return null;
        // Deleting by owner session as well makes a lock taken over in the meantime survive a stale request.
        if (await ExecuteAsync(connection, "DELETE FROM product_locks WHERE product_id=@p AND session_id=@s",
                cancellationToken, ("@p", productId), ("@s", held.SessionId)).ConfigureAwait(false) == 0) return null;
        string code;
        await using (var name = SqliteLocalStore.Command(connection, null, "SELECT name FROM products WHERE id=@p", ("@p", productId)))
            code = await name.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string ?? productId.ToString(CultureInfo.InvariantCulture);
        await ProductLockAudit.RecordForcedReleaseAsync(auditTrail, accessControl, held, code, motif, cancellationToken).ConfigureAwait(false);
        return held;
    }

    private static async Task<int> ExecuteAsync(Microsoft.Data.Sqlite.SqliteConnection connection, string sql,
        CancellationToken token, params (string Name, object? Value)[] parameters)
    {
        await using var command = SqliteLocalStore.Command(connection, null, sql, parameters);
        return await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    private static async Task<ProductLock?> ReadAsync(Microsoft.Data.Sqlite.SqliteConnection connection, int productId, CancellationToken token)
    {
        await using var command = SqliteLocalStore.Command(connection, null,
            $"SELECT {Columns} FROM product_locks WHERE product_id=@p AND expires_utc>{Now}", ("@p", productId));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        return await reader.ReadAsync(token).ConfigureAwait(false) ? Map(reader) : null;
    }

    private static ProductLock Map(Microsoft.Data.Sqlite.SqliteDataReader reader) =>
        new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), ParseUtc(reader.GetString(3)),
            ParseUtc(reader.GetString(4)), ParseUtc(reader.GetString(5)), Math.Max(0, reader.GetInt32(6)));

    private static DateTime ParseUtc(string value) =>
        DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}

// MariaDB: table `product_lock`, created on first use. Every statement is atomic and uses UTC_TIMESTAMP(6) of the server.
public sealed class MariaProductLockRepository(IConfiguration configuration, IAccessControl? accessControl = null,
    IAuditTrail? auditTrail = null) : IProductLockRepository
{
    private static readonly SemaphoreSlim SchemaGate = new(1, 1);
    private static volatile bool schemaReady;
    private const string Columns = "id_produs,owner_username,session_id,acquired_utc,renewed_utc,expires_utc,TIMESTAMPDIFF(SECOND,UTC_TIMESTAMP(6),expires_utc)";
    private static readonly string Expiry = $"DATE_ADD(UTC_TIMESTAMP(6), INTERVAL {ProductLockRules.LeaseSeconds} SECOND)";

    public async Task<LockAttempt> RenewAsync(int productId, string sessionId, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureProductOperatorAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        return await ExecuteAsync(connection, $"UPDATE product_lock SET renewed_utc=UTC_TIMESTAMP(6),expires_utc={Expiry} WHERE id_produs=@p AND session_id=@s",
                cancellationToken, ("@p", productId), ("@s", sessionId)).ConfigureAwait(false) > 0
            ? new(true, false, await ReadAsync(connection, productId, cancellationToken).ConfigureAwait(false))
            : new(false, false, await ReadAsync(connection, productId, cancellationToken).ConfigureAwait(false));
    }

    public async Task<LockAttempt> AcquireAsync(int productId, string sessionId, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureProductOperatorAsync(cancellationToken).ConfigureAwait(false);
        var owner = accessControl is null ? "sistem" : await accessControl.GetUsernameAsync(cancellationToken).ConfigureAwait(false) ?? "necunoscut";
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);

        if (await ExecuteAsync(connection, $"UPDATE product_lock SET renewed_utc=UTC_TIMESTAMP(6),expires_utc={Expiry} WHERE id_produs=@p AND session_id=@s",
                cancellationToken, ("@p", productId), ("@s", sessionId)).ConfigureAwait(false) > 0)
            return new(true, false, await ReadAsync(connection, productId, cancellationToken).ConfigureAwait(false));
        if (await ExecuteAsync(connection, $"""
                UPDATE product_lock SET owner_username=@o,session_id=@s,acquired_utc=UTC_TIMESTAMP(6),renewed_utc=UTC_TIMESTAMP(6),expires_utc={Expiry}
                WHERE id_produs=@p AND expires_utc<=UTC_TIMESTAMP(6)
                """, cancellationToken, ("@p", productId), ("@s", sessionId), ("@o", owner)).ConfigureAwait(false) > 0)
            return new(true, true, await ReadAsync(connection, productId, cancellationToken).ConfigureAwait(false));
        if (await ExecuteAsync(connection, $"""
                INSERT IGNORE INTO product_lock(id_produs,owner_username,session_id,acquired_utc,renewed_utc,expires_utc)
                SELECT @p,@o,@s,UTC_TIMESTAMP(6),UTC_TIMESTAMP(6),{Expiry} FROM produs WHERE id_produs=@p
                """, cancellationToken, ("@p", productId), ("@s", sessionId), ("@o", owner)).ConfigureAwait(false) > 0)
            return new(true, true, await ReadAsync(connection, productId, cancellationToken).ConfigureAwait(false));
        return new(false, false, await ReadAsync(connection, productId, cancellationToken).ConfigureAwait(false));
    }

    public async Task<ProductLock?> GetAsync(int productId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        return await ReadAsync(connection, productId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ProductLock>> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand($"SELECT {Columns} FROM product_lock WHERE expires_utc>UTC_TIMESTAMP(6) ORDER BY id_produs", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<ProductLock>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(Map(reader));
        return result;
    }

    public async Task<bool> ReleaseAsync(int productId, string sessionId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        return await ExecuteAsync(connection, "DELETE FROM product_lock WHERE id_produs=@p AND session_id=@s",
            cancellationToken, ("@p", productId), ("@s", sessionId)).ConfigureAwait(false) > 0;
    }

    public async Task<ProductLock?> ForceReleaseAsync(int productId, string reason, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var motif = ChangeReasonRules.Normalize(reason);
        if (ProductLockRules.ForceReleaseReasonError(motif) is { } error) throw new ProductLockException(error);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var held = await ReadAsync(connection, productId, cancellationToken).ConfigureAwait(false);
        if (held is null) return null;
        if (await ExecuteAsync(connection, "DELETE FROM product_lock WHERE id_produs=@p AND session_id=@s",
                cancellationToken, ("@p", productId), ("@s", held.SessionId)).ConfigureAwait(false) == 0) return null;
        string code;
        await using (var name = new MySqlCommand("SELECT produs_denumire FROM produs WHERE id_produs=@p", connection))
        {
            name.Parameters.AddWithValue("@p", productId);
            code = await name.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string ?? productId.ToString(CultureInfo.InvariantCulture);
        }
        await ProductLockAudit.RecordForcedReleaseAsync(auditTrail, accessControl, held, code, motif, cancellationToken).ConfigureAwait(false);
        return held;
    }

    private async Task<MySqlConnection> OpenAsync(CancellationToken token)
    {
        var connection = DatabaseConnections.Create(configuration);
        try
        {
            await connection.OpenAsync(token).ConfigureAwait(false);
            if (!schemaReady)
            {
                await SchemaGate.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    if (!schemaReady)
                    {
                        await using var create = new MySqlCommand("""
                            CREATE TABLE IF NOT EXISTS product_lock (
                                id_produs INT NOT NULL,
                                owner_username VARCHAR(191) NOT NULL,
                                session_id VARCHAR(64) NOT NULL,
                                acquired_utc DATETIME(6) NOT NULL,
                                renewed_utc DATETIME(6) NOT NULL,
                                expires_utc DATETIME(6) NOT NULL,
                                PRIMARY KEY(id_produs),
                                INDEX ix_product_lock_expires(expires_utc)
                            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
                            """, connection);
                        await create.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                        schemaReady = true;
                    }
                }
                finally { SchemaGate.Release(); }
            }
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<int> ExecuteAsync(MySqlConnection connection, string sql, CancellationToken token,
        params (string Name, object Value)[] parameters)
    {
        await using var command = new MySqlCommand(sql, connection);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    private static async Task<ProductLock?> ReadAsync(MySqlConnection connection, int productId, CancellationToken token)
    {
        await using var command = new MySqlCommand($"SELECT {Columns} FROM product_lock WHERE id_produs=@p AND expires_utc>UTC_TIMESTAMP(6)", connection);
        command.Parameters.AddWithValue("@p", productId);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        return await reader.ReadAsync(token).ConfigureAwait(false) ? Map(reader) : null;
    }

    private static ProductLock Map(MySqlDataReader reader) =>
        new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), DateTime.SpecifyKind(reader.GetDateTime(3), DateTimeKind.Utc),
            DateTime.SpecifyKind(reader.GetDateTime(4), DateTimeKind.Utc), DateTime.SpecifyKind(reader.GetDateTime(5), DateTimeKind.Utc),
            Math.Max(0, Convert.ToInt32(reader.GetValue(6), CultureInfo.InvariantCulture)));
}

// Announces lock changes to the other sessions (so a waiting user is told at once) after the change has succeeded.
public sealed class ChangeNotifyingProductLockRepository(IProductLockRepository inner, IChangeFeed feed, ChangeOrigin origin) : IProductLockRepository
{
    public async Task<LockAttempt> AcquireAsync(int productId, string sessionId, CancellationToken cancellationToken = default)
    {
        var attempt = await inner.AcquireAsync(productId, sessionId, cancellationToken).ConfigureAwait(false);
        if (attempt is { Acquired: true, Changed: true }) Publish(AuditActions.Create, productId);
        return attempt;
    }

    public Task<LockAttempt> RenewAsync(int productId, string sessionId, CancellationToken cancellationToken = default) =>
        inner.RenewAsync(productId, sessionId, cancellationToken);
    public Task<ProductLock?> GetAsync(int productId, CancellationToken cancellationToken = default) => inner.GetAsync(productId, cancellationToken);
    public Task<IReadOnlyList<ProductLock>> GetActiveAsync(CancellationToken cancellationToken = default) => inner.GetActiveAsync(cancellationToken);

    public async Task<bool> ReleaseAsync(int productId, string sessionId, CancellationToken cancellationToken = default)
    {
        var released = await inner.ReleaseAsync(productId, sessionId, cancellationToken).ConfigureAwait(false);
        if (released) Publish(AuditActions.Delete, productId);
        return released;
    }

    public async Task<ProductLock?> ForceReleaseAsync(int productId, string reason, CancellationToken cancellationToken = default)
    {
        var removed = await inner.ForceReleaseAsync(productId, reason, cancellationToken).ConfigureAwait(false);
        if (removed is not null) Publish(AuditActions.Delete, productId);
        return removed;
    }

    private void Publish(string action, int productId) =>
        feed.Publish(new(ChangeEntities.ProductLock, action, productId.ToString(CultureInfo.InvariantCulture), origin.Id, DateTime.UtcNow));
}
