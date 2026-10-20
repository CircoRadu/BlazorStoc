using System.Data;
using MySqlConnector;

namespace BlazorStoc.Services;

// Shared plumbing of the Maria* repositories: an opened connection, a parameterised command and the write transaction
// (SERIALIZABLE, one retry loop on deadlock, rollback on any failure). A repository supplies only what is its own: the exception
// type it reports and how it translates a MySqlException (duplicate key, foreign key) into a message for the user.
internal static class MariaDb
{
    public const string ForbiddenDatabaseMessage = "Modificările sunt permise numai în baza BlazorStoc.";

    public static async Task<MySqlConnection> OpenAsync(IConfiguration configuration, CancellationToken token)
    {
        var connection = DatabaseConnections.Create(configuration);
        try
        {
            await connection.OpenAsync(token).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public static MySqlCommand Command(MySqlConnection connection, MySqlTransaction? transaction, string sql,
        params (string Name, object? Value)[] parameters)
    {
        var command = new MySqlCommand(sql, connection, transaction);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }

    public static MySqlCommand Command(MySqlConnection connection, string sql, params (string Name, object? Value)[] parameters) =>
        Command(connection, null, sql, parameters);

    // `forbidden` builds the repository's own exception for a write against a database other than BlazorStoc; `translate` (optional)
    // runs after the rollback and returns the exception to throw instead of the MySqlException, or null to let it through.
    public static Task<T> WriteAsync<T>(IConfiguration configuration, Func<MySqlConnection, MySqlTransaction, Task<T>> action,
        Func<string, Exception> forbidden, Func<MySqlException, CancellationToken, Task<Exception?>>? translate, CancellationToken token,
        string forbiddenMessage = ForbiddenDatabaseMessage) =>
        MariaTransactions.RetryOnDeadlockAsync(() => WriteOnceAsync(configuration, action, forbidden, translate, forbiddenMessage, token), token);

    public static Task<T> WriteAsync<T>(IConfiguration configuration, Func<MySqlConnection, MySqlTransaction, Task<T>> action,
        Func<string, Exception> forbidden, Func<MySqlException, Exception?> translate, CancellationToken token,
        string forbiddenMessage = ForbiddenDatabaseMessage) =>
        WriteAsync(configuration, action, forbidden, (exception, _) => Task.FromResult(translate(exception)), token, forbiddenMessage);

    public static Task<T> WriteAsync<T>(IConfiguration configuration, Func<MySqlConnection, MySqlTransaction, Task<T>> action,
        Func<string, Exception> forbidden, CancellationToken token, string forbiddenMessage = ForbiddenDatabaseMessage) =>
        WriteAsync(configuration, action, forbidden, (Func<MySqlException, CancellationToken, Task<Exception?>>?)null, token, forbiddenMessage);
    private static async Task<T> WriteOnceAsync<T>(IConfiguration configuration, Func<MySqlConnection, MySqlTransaction, Task<T>> action,
        Func<string, Exception> forbidden, Func<MySqlException, CancellationToken, Task<Exception?>>? translate, string forbiddenMessage,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!MariaDatabaseGuard.IsAllowedDatabase(configuration)) throw forbidden(forbiddenMessage);
        await using var connection = await OpenAsync(configuration, token).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, token).ConfigureAwait(false);
        try
        {
            var result = await action(connection, transaction).ConfigureAwait(false);
            await transaction.CommitAsync(token).ConfigureAwait(false);
            return result;
        }
        catch (Exception exception)
        {
            if (transaction.Connection is not null) await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            if (translate is not null && exception is MySqlException mySql && await translate(mySql, token).ConfigureAwait(false) is { } translated)
                throw translated;
            throw;
        }
    }
}
