using System.Text.Json;
using BlazorStoc.Services;

namespace BlazorStoc.Modules.Backup;

// The contract between the application and its backup worker. The application starts "BlazorStoc --backup-worker [its own command-line arguments]" and talks to it
// over the standard streams, line by line, in JSON:
//   application -> worker (stdin):  one line with the request, then optionally the line "cancel";
//   worker -> application (stdout): "progress" lines while it works, then exactly one of "result" (the answer of the service), "answer" (a refusal that is not a
//   failure: the database or the share said no) or "cancelled". A worker that dies, hangs or prints no closing line has failed.
// Nothing secret is written to disk by this channel: the request may carry the password typed for the NAS test, and it only ever travels over the pipe.

internal static class BackupOperations
{
    public const string Backup = "backup";
    public const string Restore = "restore";
    public const string NasTest = "nas-test";
    public const string NasCopyMissing = "nas-copy-missing";
    public const string NasCopyAfterBackup = "nas-copy-after-backup";
    public const string NasList = "nas-list";
    public const string NasFetch = "nas-fetch";
}

internal static class BackupLines
{
    public const string Progress = "progress";
    public const string Result = "result";
    public const string Answer = "answer";
    public const string Cancelled = "cancelled";
    public const string CancelCommand = "cancel";
}

/// <summary>
/// What the application asks of the worker. The operator is the user the application already checked the right of (or "sistem" for the schedule): the worker acts
/// and journals as that user. Fields not used by an operation stay null.
/// </summary>
internal sealed record BackupJobRequest(string Operation, string OperatorName, bool OperatorIsAdministrator, string? Kind = null, string? Package = null, NasBackupInput? Nas = null);

internal sealed record BackupProgressLine(string Type, string? Stage, string? Message);

/// <summary>The answer of the service when it did not throw, as the closing "result" line: the payload is the JSON of the result record of the operation.</summary>
internal sealed record BackupResultLine(string Type, JsonElement Payload);

/// <summary>The "answer" line: the worker finished normally but the operation was refused by the database, the share or the lock (Kind says which).</summary>
internal sealed record BackupAnswerLine(string Type, string Kind, string Message)
{
    public const string Nas = "nas";
    public const string Database = "database";
    public const string Maintenance = "maintenance";
}

/// <summary>The worker did not deliver: it died, hung past the time limit, or left no usable answer. A failure of the module (counted by the circuit breaker).</summary>
public sealed class BackupWorkerException(string message, Exception? inner = null) : Exception(message, inner), IBelongsToModule
{
    public string ModuleId => BackupWorkerRules.ModuleId;
}

/// <summary>The worker said no (database not answering, share refused): an answer for the user, never a failure of the module.</summary>
public sealed class BackupOperationException(string kind, string message) : Exception(message), IExpectedAnswer
{
    public string Kind { get; } = kind;
}

public static class BackupWorkerRules
{
    public const string ModuleId = "backup";
    public const string UnavailableMessage = "Copiile de siguranta si restaurarea sunt indisponibile momentan. Reincearca peste cateva momente.";
    public const string WorkerFailedMessage = "Procesul de backup s-a oprit fara sa termine. Reincearca; daca se repeta, anunta administratorul.";
    public const string Argument = "--backup-worker";
    public const string WorkerTimeoutMessage = "Operatia a durat prea mult si a fost oprita. Reincearca; daca se repeta, anunta administratorul.";
}

internal static class BackupJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json, Options) ?? throw new JsonException("empty document");
    public static T Read<T>(JsonElement element) => element.Deserialize<T>(Options) ?? throw new JsonException("empty document");
}

/// <summary>
/// The user the work is done for, as the worker sees it. The application checked what the user may do before it started the worker (the worker has no session to
/// ask), so the worker answers yes to the permissions; it only carries the name and the role to the journal and to the file names.
/// </summary>
internal sealed class DelegatedAccess(string userName, bool administrator) : IAccessControl
{
    public Task<bool> IsAdministratorAsync(CancellationToken cancellationToken = default) => Task.FromResult(administrator);
    public Task<bool> CanManageProductsAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task<bool> CanManageBeneficiariesAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task<string?> GetUsernameAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(userName);
    public Task EnsureAdministratorAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task EnsureProductOperatorAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task EnsureBeneficiaryOperatorAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<bool> HasAsync(string permission, CancellationToken cancellationToken = default) => Task.FromResult(true);
}
