using BlazorStoc.Services;
using Microsoft.AspNetCore.DataProtection;
using MySqlConnector;

namespace BlazorStoc.Modules.Backup;

// The application side of the backup: the same interfaces the pages and the schedule already use (IDatabaseBackupService, IDatabaseRestoreService, INasBackupCopier),
// served by a worker process through the circuit breaker of the "backup" module. A worker that dies or hangs becomes the "failed" result the pages already show;
// after repeated failures the module is taken out for a while and the pages say that backups are unavailable. Nothing else is affected.
//
// What stays in the application: the right to do the operation (checked before the worker is started), the list and the deletion of the packages on the server
// (small reads and deletions, with their journal entry), the settings of the schedule and of the share (database rows).

internal static class BackupGuard
{
    /// <summary>Runs the operation of the module; a worker that failed or an open circuit becomes the result <paramref name="failed"/> builds from the message.</summary>
    public static async Task<T> RunAsync<T>(ModuleRegistry modules, Func<Task<T>> operation, Func<string, T> failed)
    {
        try { return await modules.RunAsync(BackupWorkerRules.ModuleId, operation).ConfigureAwait(false); }
        catch (BackupWorkerException exception) { return failed(exception.Message); }
        catch (ModuleUnavailableException) { return failed(BackupWorkerRules.UnavailableMessage); }
    }

    /// <summary>An answer of the worker that is not a result (the share or the database refused) as the exception the in-process code would have thrown.</summary>
    public static Exception Translate(BackupOperationException exception) => exception.Kind switch
    {
        BackupAnswerLine.Nas => new NasBackupException(exception.Message),
        BackupAnswerLine.Maintenance => new MaintenanceInProgressException(exception.Message),
        _ => exception
    };

    public static async Task<(string Name, bool Administrator)> OperatorAsync(IAccessControl access, CancellationToken cancellationToken) =>
        (await access.GetUsernameAsync(cancellationToken).ConfigureAwait(false) ?? "necunoscut", await access.IsAdministratorAsync(cancellationToken).ConfigureAwait(false));

    public static Action<string, string>? Relay<TProgress, TStage>(IProgress<TProgress>? progress, Func<TStage, string, TProgress> make) where TStage : struct, Enum =>
        progress is null ? null : (stage, message) => { if (Enum.TryParse<TStage>(stage, out var parsed)) progress.Report(make(parsed, message)); };
}

internal sealed class OutOfProcessBackupService(BackupWorkerRunner runner, ModuleRegistry modules, IAccessControl access, IConfiguration configuration, IAuditTrail? audit) : IDatabaseBackupService
{
    private readonly string directory = MariaAssetPaths.DatabaseBackups(configuration);

    public async Task<BackupResult> CreateBackupAsync(BackupKind kind, IProgress<BackupProgress>? progress = null, CancellationToken cancellationToken = default,
        IOperationLockHandle? existingLock = null)
    {
        // A lock held by the caller cannot be handed to another process: the only caller that holds one is the restoration, which runs whole in its own worker.
        if (existingLock is not null) throw new NotSupportedException("Un lacat tinut de aplicatie nu poate fi dat procesului de backup.");
        var (name, administrator) = await BackupGuard.OperatorAsync(access, cancellationToken).ConfigureAwait(false);
        var request = new BackupJobRequest(BackupOperations.Backup, name, administrator, Kind: kind.ToString());
        try
        {
            return await BackupGuard.RunAsync(modules, () => runner.RunAsync<BackupResult>(request, BackupGuard.Relay<BackupProgress, BackupStage>(progress, (stage, message) => new BackupProgress(stage, message)), cancellationToken),
                message => BackupResult.Failed(message)).ConfigureAwait(false);
        }
        catch (BackupOperationException exception) { return BackupResult.Failed(BackupRules.GenericFailedMessage + (exception.Kind == BackupAnswerLine.Database ? " Baza de date nu a raspuns." : "")); }
    }

    public Task<IReadOnlyList<BackupPackage>> ListPackagesAsync(CancellationToken cancellationToken = default) => BackupPackageStore.ListAsync(directory, cancellationToken);

    public Task<BackupDeleteResult> DeletePackageAsync(string fileName, string reason, CancellationToken cancellationToken = default) =>
        BackupPackageStore.DeleteAsync(directory, fileName, reason, audit, access, cancellationToken);
}

internal sealed class OutOfProcessRestoreService(BackupWorkerRunner runner, ModuleRegistry modules, IAccessControl access) : IDatabaseRestoreService
{
    public async Task<RestoreResult> RestoreAsync(string packageFileName, IProgress<RestoreProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (packageFileName != Path.GetFileName(packageFileName) || packageFileName.Length == 0) return RestoreResult.Failed(RestoreRules.PackageNotFoundMessage);
        var (name, administrator) = await BackupGuard.OperatorAsync(access, cancellationToken).ConfigureAwait(false);
        var request = new BackupJobRequest(BackupOperations.Restore, name, administrator, Package: packageFileName);
        try
        {
            return await BackupGuard.RunAsync(modules, () => runner.RunAsync<RestoreResult>(request, BackupGuard.Relay<RestoreProgress, RestoreStage>(progress, (stage, message) => new RestoreProgress(stage, message)), cancellationToken),
                message => RestoreResult.Failed(message)).ConfigureAwait(false);
        }
        catch (BackupOperationException) { return RestoreResult.Failed(RestoreRules.GenericFailedMessage); }
    }
}

internal sealed class OutOfProcessNasCopier(BackupWorkerRunner runner, ModuleRegistry modules, IAccessControl access, IConfiguration configuration, ILogger logger) : INasBackupCopier
{
    private async Task<T> RunAsync<T>(string operation, Func<BackupJobRequest, BackupJobRequest> shape, Func<string, T> failed, CancellationToken cancellationToken)
    {
        var (name, administrator) = await BackupGuard.OperatorAsync(access, cancellationToken).ConfigureAwait(false);
        var request = shape(new BackupJobRequest(operation, name, administrator));
        try { return await BackupGuard.RunAsync(modules, () => runner.RunAsync<T>(request, null, cancellationToken), failed).ConfigureAwait(false); }
        catch (BackupOperationException exception) { throw BackupGuard.Translate(exception); }
    }

    public async Task<NasCopyResult> TestAsync(NasBackupInput input, CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync("setari-backup.edit", cancellationToken).ConfigureAwait(false);
        return await RunAsync(BackupOperations.NasTest, request => request with { Nas = input }, message => new NasCopyResult(false, 0, 0, 0, message), cancellationToken).ConfigureAwait(false);
    }

    public async Task<NasCopyResult> CopyMissingAsync(CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync("setari-backup.edit", cancellationToken).ConfigureAwait(false);
        return await RunAsync(BackupOperations.NasCopyMissing, request => request, message => new NasCopyResult(false, 0, 0, 0, message), cancellationToken).ConfigureAwait(false);
    }

    public Task<NasCopyResult> CopyAfterBackupAsync(string? onlyPackage, CancellationToken cancellationToken = default) =>
        RunAsync(BackupOperations.NasCopyAfterBackup, request => request with { Package = onlyPackage }, message => new NasCopyResult(false, 0, 0, 0, message), cancellationToken);

    public async Task<NasShareListing> ListShareAsync(CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync("setari-backup.edit", cancellationToken).ConfigureAwait(false);
        return await RunAsync(BackupOperations.NasList, request => request, message => new NasShareListing(false, message, []), cancellationToken).ConfigureAwait(false);
    }

    public async Task<NasCopyResult> FetchAsync(string fileName, CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync("setari-backup.edit", cancellationToken).ConfigureAwait(false);
        return await RunAsync(BackupOperations.NasFetch, request => request with { Package = fileName }, message => new NasCopyResult(false, 0, 0, 1, message), cancellationToken).ConfigureAwait(false);
    }

    // A small write of the application's own account (the note shown in the backup notification): no reason for a process.
    public async Task RecordBackupFailureAsync(string message, CancellationToken cancellationToken = default)
    {
        try { await new MariaBackupSettingsStore(configuration).RecordBackupErrorAsync(message, cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) when (exception is MySqlException) { logger.LogWarning("Backup failure could not be saved ({ErrorType}).", exception.GetType().Name); }
    }
}

internal sealed class OutOfProcessBackupOperations(BackupWorkerRunner runner, ModuleRegistry modules, IConfiguration configuration, IAuditTrail audit, ILoggerFactory loggers) : IBackupOperations
{
    public IDatabaseBackupService Backups(IAccessControl actor) => new NasCopyingBackupService(
        new OutOfProcessBackupService(runner, modules, actor, configuration, audit), Copier(actor), loggers.CreateLogger<NasCopyingBackupService>());

    public INasBackupCopier Copier(IAccessControl actor) => new OutOfProcessNasCopier(runner, modules, actor, configuration, loggers.CreateLogger("BlazorStoc.Backup"));

    public IDatabaseRestoreService Restore(IAccessControl actor) => new OutOfProcessRestoreService(runner, modules, actor);
}

// Stand-ins while the module is switched off or did not start: the pages that inject the services are still created and say that backups are unavailable.
internal sealed class UnavailableBackupOperations : IBackupOperations
{
    public IDatabaseBackupService Backups(IAccessControl actor) => new UnavailableBackupService();
    public INasBackupCopier Copier(IAccessControl actor) => new UnavailableNasCopier();
    public IDatabaseRestoreService Restore(IAccessControl actor) => new UnavailableRestoreService();
}

internal sealed class UnavailableBackupService : IDatabaseBackupService
{
    public Task<BackupResult> CreateBackupAsync(BackupKind kind, IProgress<BackupProgress>? progress = null, CancellationToken cancellationToken = default, IOperationLockHandle? existingLock = null) =>
        Task.FromResult(BackupResult.Failed(BackupWorkerRules.UnavailableMessage));

    public Task<IReadOnlyList<BackupPackage>> ListPackagesAsync(CancellationToken cancellationToken = default) =>
        Task.FromException<IReadOnlyList<BackupPackage>>(new ModuleUnavailableException(BackupWorkerRules.ModuleId));

    public Task<BackupDeleteResult> DeletePackageAsync(string fileName, string reason, CancellationToken cancellationToken = default) =>
        Task.FromResult(BackupDeleteResult.Failed(BackupWorkerRules.UnavailableMessage));
}

internal sealed class UnavailableRestoreService : IDatabaseRestoreService
{
    public Task<RestoreResult> RestoreAsync(string packageFileName, IProgress<RestoreProgress>? progress = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(RestoreResult.Failed(BackupWorkerRules.UnavailableMessage));
}

internal sealed class UnavailableNasCopier : INasBackupCopier
{
    private static NasCopyResult Unavailable => new(false, 0, 0, 0, BackupWorkerRules.UnavailableMessage);

    public Task<NasCopyResult> TestAsync(NasBackupInput input, CancellationToken cancellationToken = default) => Task.FromResult(Unavailable);
    public Task<NasCopyResult> CopyMissingAsync(CancellationToken cancellationToken = default) => Task.FromResult(Unavailable);
    public Task<NasCopyResult> CopyAfterBackupAsync(string? onlyPackage, CancellationToken cancellationToken = default) => Task.FromResult(Unavailable);
    public Task<NasShareListing> ListShareAsync(CancellationToken cancellationToken = default) => Task.FromResult(new NasShareListing(false, BackupWorkerRules.UnavailableMessage, []));
    public Task<NasCopyResult> FetchAsync(string fileName, CancellationToken cancellationToken = default) => Task.FromResult(Unavailable);
    public Task RecordBackupFailureAsync(string message, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
