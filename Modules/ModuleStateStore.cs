using BlazorStoc.Services;
using MySqlConnector;

namespace BlazorStoc.Modules;

// The modules the administrator stopped by hand (Setari -> Module) are kept in the table module_state (migration 42): one row per stopped module, deleted when the
// module is started again. At the next start of the application ModuleStateRestorer reads the rows back and the modules stay out until they are started by hand.

public interface IModuleStateStore
{
    Task<IReadOnlyList<StoppedModule>> ReadStoppedAsync(CancellationToken cancellationToken = default);
    Task SaveStoppedAsync(StoppedModule stopped, CancellationToken cancellationToken = default);
    Task ClearStoppedAsync(string moduleId, CancellationToken cancellationToken = default);
}

/// <summary>An answer of the module switch for the administrator (an empty reason, a module that cannot be stopped): not a failure of any module.</summary>
public sealed class ModuleOperationException(string message) : Exception(message);

public sealed class MariaModuleStateStore(IConfiguration configuration) : IModuleStateStore
{
    public async Task<IReadOnlyList<StoppedModule>> ReadStoppedAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("SELECT module_id, reason, stopped_by, stopped_utc FROM module_state ORDER BY stopped_utc", connection);
        var result = new List<StoppedModule>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new StoppedModule(reader.GetString(0), reader.GetString(1), reader.GetString(2), DateTime.SpecifyKind(reader.GetDateTime(3), DateTimeKind.Utc)));
        return result;
    }

    public Task SaveStoppedAsync(StoppedModule stopped, CancellationToken cancellationToken = default) =>
        MariaDb.WriteAsync(configuration, async (connection, transaction) =>
        {
            await using var command = new MySqlCommand("""
                INSERT INTO module_state (module_id, reason, stopped_by, stopped_utc) VALUES (@id, @reason, @by, @at)
                ON DUPLICATE KEY UPDATE reason = VALUES(reason), stopped_by = VALUES(stopped_by), stopped_utc = VALUES(stopped_utc)
                """, connection, transaction);
            command.Parameters.AddWithValue("@id", stopped.ModuleId);
            command.Parameters.AddWithValue("@reason", stopped.Reason);
            command.Parameters.AddWithValue("@by", stopped.StoppedBy);
            command.Parameters.AddWithValue("@at", stopped.StoppedUtc);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }, message => new ModuleOperationException(message), cancellationToken);

    public Task ClearStoppedAsync(string moduleId, CancellationToken cancellationToken = default) =>
        MariaDb.WriteAsync(configuration, async (connection, transaction) =>
        {
            await using var command = new MySqlCommand("DELETE FROM module_state WHERE module_id = @id", connection, transaction);
            command.Parameters.AddWithValue("@id", moduleId);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }, message => new ModuleOperationException(message), cancellationToken);
}

/// <summary>
/// At startup puts back the stops the administrator made before the restart. The database may not answer yet (or the migration may not have run): the attempt is repeated
/// in the background until it works, so a module the administrator stopped does not stay running just because the database was late. Never throws.
/// </summary>
public sealed class ModuleStateRestorer(ModuleRegistry registry, IModuleStateStore store, ILogger<ModuleStateRestorer> logger) : BackgroundService
{
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (await RestoreAsync(stoppingToken).ConfigureAwait(false)) return;
        try
        {
            using var timer = new PeriodicTimer(RetryDelay);
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                if (await RestoreAsync(stoppingToken).ConfigureAwait(false)) return;
        }
        catch (OperationCanceledException) { }
    }

    private bool warned;

    /// <summary>One attempt: true when the saved stops were read and applied.</summary>
    public async Task<bool> RestoreAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            registry.ApplyStopped(await store.ReadStoppedAsync(cancellationToken).ConfigureAwait(false));
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (!warned)
                logger.LogWarning("The modules stopped by the administrator could not be read yet ({ErrorType}); the attempt is repeated until the database answers.", exception.GetType().Name);
            warned = true;
            return false;
        }
    }
}
