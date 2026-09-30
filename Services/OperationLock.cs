using System.Text.Json;

namespace BlazorStoc.Services;

// Task 2 "Decizie tehnica - mecanism de backup/restaurare": a lock shared by backup and (later) restore, so only
// one such operation runs at a time. The TODO text describes it as "persistent in baza de date", but the migrated
// MariaDB schema has no table for it and the application account (blazorstoc_dev) has no DDL rights to add one
// (MariaArchiveSchema.RequiredTables; docs/CLAUDE_CONECTARE_MARIADB_LOCALA.md) - creating one needs the separate
// blazorstoc_migrator account, in a dedicated migration step, not something this cycle does on its own. This keeps
// the same properties (heartbeat, automatic expiry, orphan recognized and released with a log line, release
// always in a finally) as a small JSON file next to the backup packages instead of a database row.
public sealed record OperationLockInfo(string Operation, string OperatorName, string OperatorRole, DateTime AcquiredUtc, DateTime HeartbeatUtc);

public interface IOperationLockHandle : IAsyncDisposable;

public interface IOperationLockService
{
    // Null means another operation already holds the (non-expired) lock.
    Task<IOperationLockHandle?> TryAcquireAsync(string operation, string operatorName, string operatorRole,
        CancellationToken cancellationToken = default);
    Task<OperationLockInfo?> GetActiveAsync(CancellationToken cancellationToken = default);
}

public static class OperationLockRules
{
    // The export itself (mariadb-dump on the current, small database) takes seconds, not minutes; a 120s lease
    // renewed every 20s gives ample margin while still freeing a crashed operation quickly.
    public const int LeaseSeconds = 120;
    public const int HeartbeatSeconds = 20;

    public const string HeldMessage = "O alta operatie de backup sau restaurare este deja in curs. Reincearca mai tarziu.";
}

public sealed class FileOperationLockService(string lockFilePath, IAuditTrail? auditTrail = null,
    ILogger<FileOperationLockService>? logger = null) : IOperationLockService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim gate = new(1, 1);

    private sealed record LockFile(string Operation, string OperatorName, string OperatorRole, int ProcessId,
        DateTime AcquiredUtc, DateTime HeartbeatUtc);

    public async Task<IOperationLockHandle?> TryAcquireAsync(string operation, string operatorName, string operatorRole,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(lockFilePath)!);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = DateTime.UtcNow;
            var existing = await ReadAsync(cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                if (now - existing.HeartbeatUtc <= TimeSpan.FromSeconds(OperationLockRules.LeaseSeconds)) return null;
                logger?.LogWarning("Lacat de operatie orfan ({Operation}, operator anterior {Operator}) eliberat automat dupa expirare.",
                    existing.Operation, existing.OperatorName);
                if (auditTrail is not null)
                    await auditTrail.RecordAsync(new("sistem", AccessRoles.Administrator, AuditEntities.DatabaseBackup,
                        AuditActions.Unlock, existing.Operation,
                        $"Lacat orfan eliberat automat (operator anterior: {existing.OperatorName}, de la {existing.AcquiredUtc:dd.MM.yyyy HH:mm} UTC).",
                        string.Empty, string.Empty, null), cancellationToken).ConfigureAwait(false);
                TryDelete();
            }
            var entry = new LockFile(operation, operatorName, operatorRole, Environment.ProcessId, now, now);
            try
            {
                await using var stream = new FileStream(lockFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await JsonSerializer.SerializeAsync(stream, entry, JsonOptions, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException)
            {
                // Another request won the race to create the file first.
                return null;
            }
            MaintenanceGate.Invalidate();
            return new Handle(this, entry);
        }
        finally { gate.Release(); }
    }

    public async Task<OperationLockInfo?> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        var existing = await ReadAsync(cancellationToken).ConfigureAwait(false);
        if (existing is null) return null;
        if (DateTime.UtcNow - existing.HeartbeatUtc > TimeSpan.FromSeconds(OperationLockRules.LeaseSeconds)) return null;
        return new(existing.Operation, existing.OperatorName, existing.OperatorRole, existing.AcquiredUtc, existing.HeartbeatUtc);
    }

    // Synchronous read for MaintenanceGate (called from places that open a database connection and cannot await):
    // the active, non-expired operation recorded in the lock file at the given path, or null.
    internal static OperationLockInfo? ReadActive(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var existing = JsonSerializer.Deserialize<LockFile>(stream, JsonOptions);
            if (existing is null) return null;
            if (DateTime.UtcNow - existing.HeartbeatUtc > TimeSpan.FromSeconds(OperationLockRules.LeaseSeconds)) return null;
            return new(existing.Operation, existing.OperatorName, existing.OperatorRole, existing.AcquiredUtc, existing.HeartbeatUtc);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private async Task RenewAsync(LockFile entry, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var renewed = entry with { HeartbeatUtc = DateTime.UtcNow };
            await using var stream = new FileStream(lockFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
            await JsonSerializer.SerializeAsync(stream, renewed, JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger?.LogWarning("Reimprospatarea lacatului de operatie a esuat ({ErrorType}).", exception.GetType().Name);
        }
        finally { gate.Release(); }
    }

    private async Task<LockFile?> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(lockFilePath)) return null;
        try
        {
            await using var stream = new FileStream(lockFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return await JsonSerializer.DeserializeAsync<LockFile>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            return null;
        }
    }

    private void TryDelete()
    {
        try { File.Delete(lockFilePath); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger?.LogWarning("Stergerea fisierului de lacat a esuat ({ErrorType}).", exception.GetType().Name);
        }
        MaintenanceGate.Invalidate();
    }

    private sealed class Handle : IOperationLockHandle
    {
        private readonly FileOperationLockService owner;
        private readonly LockFile entry;
        private readonly CancellationTokenSource cts = new();
        private readonly Task heartbeatLoop;

        public Handle(FileOperationLockService owner, LockFile entry)
        {
            this.owner = owner;
            this.entry = entry;
            heartbeatLoop = RunHeartbeatAsync(cts.Token);
        }

        private async Task RunHeartbeatAsync(CancellationToken token)
        {
            try
            {
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(OperationLockRules.HeartbeatSeconds));
                while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
                    await owner.RenewAsync(entry, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
        }

        public async ValueTask DisposeAsync()
        {
            cts.Cancel();
            try { await heartbeatLoop.ConfigureAwait(false); } catch (OperationCanceledException) { }
            cts.Dispose();
            owner.TryDelete();
        }
    }
}
