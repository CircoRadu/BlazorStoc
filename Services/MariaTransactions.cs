using MySqlConnector;

namespace BlazorStoc.Services;

// SERIALIZABLE turns every read into a shared lock, so two sessions that touch the same rows (for example two saves of the
// same unique value) can deadlock; InnoDB then rolls one transaction back completely. Nothing of it was applied, so it is
// simply run again a few times before the error is allowed to reach the caller.
internal static class MariaTransactions
{
    public const int MaxAttempts = 4;

    public static async Task<T> RetryOnDeadlockAsync<T>(Func<Task<T>> operation, CancellationToken token)
    {
        for (var attempt = 1; ; attempt++)
        {
            try { return await operation().ConfigureAwait(false); }
            catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.LockDeadlock && attempt < MaxAttempts)
            {
                await Task.Delay(Random.Shared.Next(20, 80) * attempt, token).ConfigureAwait(false);
            }
        }
    }
}
