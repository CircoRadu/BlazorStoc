using Microsoft.AspNetCore.DataProtection;

namespace BlazorStoc.Services;

/// <summary>
/// The backup, the NAS copier and the restoration for one actor (the signed-in user, or "sistem" for the schedule), the way the application runs them: in a worker
/// process (the backup module) or inside the application (<see cref="InProcessBackupOperations"/>). The pages get their services from here through the scope, and
/// the daily schedule asks for its own, so both always use the same way of working.
/// </summary>
public interface IBackupOperations
{
    /// <summary>The backup, followed (when the NAS copy is on) by the copy of the new package.</summary>
    IDatabaseBackupService Backups(IAccessControl actor);
    INasBackupCopier Copier(IAccessControl actor);
    IDatabaseRestoreService Restore(IAccessControl actor);
}

/// <summary>The backup services as classes of the application itself (Backup:Mode = InProcess: a way back, and what the in-memory checks and the tools use).</summary>
public sealed class InProcessBackupOperations(IConfiguration configuration, IDataProtectionProvider protection, IOperationLockService locks, IAuditTrail audit,
    ILoggerFactory loggers) : IBackupOperations
{
    public INasBackupCopier Copier(IAccessControl actor) => new NasBackupCopier(configuration, protection, actor, audit, loggers.CreateLogger<NasBackupCopier>());

    public IDatabaseBackupService Backups(IAccessControl actor) => new NasCopyingBackupService(
        new MariaDatabaseBackupService(configuration, actor, locks, audit, loggers.CreateLogger<MariaDatabaseBackupService>()), Copier(actor), loggers.CreateLogger<NasCopyingBackupService>());

    public IDatabaseRestoreService Restore(IAccessControl actor) =>
        new MariaDatabaseRestoreService(configuration, actor, locks, Backups(actor), audit, loggers.CreateLogger<MariaDatabaseRestoreService>());
}
