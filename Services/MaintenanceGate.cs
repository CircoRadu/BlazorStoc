namespace BlazorStoc.Services;

// Raised for any data access made by a session other than the one running a backup/restore while the shared operation
// lock (Services/OperationLock.cs) is held.
public sealed class MaintenanceInProgressException(string message) : Exception(message);

// The write freeze of a backup export / database restoration. While the shared operation lock is active, every other
// connection to the application's data is refused at the two central places that open one (DatabaseConnections.Create
// for MariaDB, SqliteLocalStore.OpenConnectionAsync for the demo database), so no session - and no request that slipped
// in before its page was redirected - can write (or read a half-swapped schema) while the snapshot is taken or the
// schemas are swapped. Only the operation that holds the lock passes: it marks its own asynchronous flow with
// EnterOwnerScope right after acquiring the lock.
//
// The lock file is the source of truth (it also covers a second application process on the same database); its state
// is cached for a second so that the check stays cheap, and dropped at once whenever THIS process takes or releases
// the lock. Not configured (no path) = no restriction, which is the state of every test that does not set it up.
public static class MaintenanceGate
{
    public const string BlockedMessage =
        "Backup sau restaurare in curs. Datele sunt temporar indisponibile; reincearca peste cateva secunde.";

    private const int CacheMilliseconds = 1000;
    private static readonly AsyncLocal<int> OwnerDepth = new();
    private static readonly object Sync = new();
    private static string? lockFilePath;
    private static OperationLockInfo? cached;
    private static long cachedAtTicks;

    public static void Configure(string? path)
    {
        lock (Sync) { lockFilePath = path; cached = null; cachedAtTicks = 0; }
    }

    // Called by the lock service whenever this process creates or removes the lock file.
    public static void Invalidate()
    {
        lock (Sync) cachedAtTicks = 0;
    }

    // The active (non-expired) operation, or null.
    public static OperationLockInfo? Active
    {
        get
        {
            string? path;
            lock (Sync)
            {
                path = lockFilePath;
                if (path is null) return null;
                if (cachedAtTicks != 0 && Environment.TickCount64 - cachedAtTicks < CacheMilliseconds) return cached;
            }
            var read = FileOperationLockService.ReadActive(path);
            lock (Sync)
            {
                if (!string.Equals(path, lockFilePath, StringComparison.Ordinal)) return null;
                cached = read;
                cachedAtTicks = Environment.TickCount64 == 0 ? 1 : Environment.TickCount64;
                return read;
            }
        }
    }

    public static bool IsOwner => OwnerDepth.Value > 0;

    // Marks the current asynchronous flow (and everything it calls) as the operation that holds the lock. Must be
    // called from the operation's own method, not from a helper that returns before the work is done.
    public static IDisposable EnterOwnerScope()
    {
        OwnerDepth.Value++;
        return new Scope();
    }

    public static void ThrowIfBlocked()
    {
        if (OwnerDepth.Value > 0) return;
        if (Active is not null) throw new MaintenanceInProgressException(BlockedMessage);
    }

    private sealed class Scope : IDisposable
    {
        private int disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0) OwnerDepth.Value--;
        }
    }
}
