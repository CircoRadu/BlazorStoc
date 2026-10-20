using System.Globalization;
using BlazorStoc.Services;

namespace BlazorStoc.Modules;

// The administrator's switch of a module (Setari -> Module): stop / start by hand. One operation = permission, saved state (so it survives a restart), the registry (at
// once, no restart) and the journal under the administrator's name with its own exact action. If a later step fails the earlier ones are undone, so the screen, the
// saved state and the journal never disagree.
public sealed class ModuleControl(ModuleRegistry registry, IModuleStateStore store, IAccessControl access, IAuditTrail audit, TimeProvider clock)
{
    public const string Permission = "setari-module.toggle";

    /// <summary>The reason of a stop is mandatory and follows the rule of every reason in the application (at most 500 characters).</summary>
    public static string? ReasonError(string? reason) => ChangeReasonRules.ValidationError(reason);

    public async Task<ModuleToggleResult> StopAsync(string moduleId, string reason, CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync(Permission, cancellationToken).ConfigureAwait(false);
        if (ReasonError(reason) is { } error) throw new ModuleOperationException(error);
        reason = ChangeReasonRules.Normalize(reason);
        var module = registry.Snapshot().FirstOrDefault(item => item.Id == moduleId);
        if (module is null) return ModuleToggleResult.Unknown;
        if (ModuleRegistry.Protected.Contains(moduleId)) return ModuleToggleResult.Protected;
        if (!registry.CanToggle(moduleId) || registry.IsManuallyStopped(moduleId)) return ModuleToggleResult.NoChange;
        var user = await access.GetUsernameAsync(cancellationToken).ConfigureAwait(false) ?? "necunoscut";
        var at = clock.GetUtcNow().UtcDateTime;
        await store.SaveStoppedAsync(new StoppedModule(moduleId, reason, user, at), cancellationToken).ConfigureAwait(false);
        var result = registry.Stop(moduleId, reason, user);
        if (result != ModuleToggleResult.Done)
        {
            await store.ClearStoppedAsync(moduleId, CancellationToken.None).ConfigureAwait(false);
            return result;
        }
        try
        {
            await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.Module, AuditActions.ModuleManualStop, moduleId, module.Name,
                AuditDetails.Changes(new AuditChange("Stare", StateText(module), "Oprit de administrator")), reason, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            registry.Start(moduleId);
            try { await store.ClearStoppedAsync(moduleId, CancellationToken.None).ConfigureAwait(false); } catch (Exception) { }
            throw;
        }
        return ModuleToggleResult.Done;
    }

    public async Task<ModuleToggleResult> StartAsync(string moduleId, CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync(Permission, cancellationToken).ConfigureAwait(false);
        var module = registry.Snapshot().FirstOrDefault(item => item.Id == moduleId);
        if (module is null) return ModuleToggleResult.Unknown;
        if (!registry.CanToggle(moduleId)) return ModuleToggleResult.NeedsRestart;
        if (!registry.IsManuallyStopped(moduleId)) return ModuleToggleResult.NoChange;
        var previous = new StoppedModule(moduleId, module.Reason ?? "", module.StoppedBy ?? "necunoscut", module.SinceUtc);
        await store.ClearStoppedAsync(moduleId, cancellationToken).ConfigureAwait(false);
        var result = registry.Start(moduleId);
        if (result != ModuleToggleResult.Done)
        {
            await store.SaveStoppedAsync(previous, CancellationToken.None).ConfigureAwait(false);
            return result;
        }
        try
        {
            await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.Module, AuditActions.ModuleManualStart, moduleId, module.Name,
                AuditDetails.Changes(new AuditChange("Stare", "Oprit de administrator", "Funcționează"))
                + $"; Oprit de: {previous.StoppedBy}; Oprit din: {Local(previous.StoppedUtc)}; Durată oprire: {Duration(clock.GetUtcNow().UtcDateTime - previous.StoppedUtc)}; Motiv oprire: {previous.Reason}",
                "Pornire manuală de către administrator", cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            registry.ApplyStopped([previous]);
            try { await store.SaveStoppedAsync(previous, CancellationToken.None).ConfigureAwait(false); } catch (Exception) { }
            throw;
        }
        return ModuleToggleResult.Done;
    }

    private static string StateText(ModuleInfo module) => module.Status switch
    {
        ModuleStatus.Healthy => "Funcționează",
        ModuleStatus.Degraded => "Cu erori",
        _ => $"Oprit ({module.Origin})"
    };

    private static string Local(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);

    /// <summary>"2 zile 3 h 5 min" / "5 min": how long a module stayed stopped.</summary>
    public static string Duration(TimeSpan span)
    {
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        var parts = new List<string>();
        if (span.Days > 0) parts.Add(span.Days == 1 ? "1 zi" : $"{span.Days} zile");
        if (span.Hours > 0) parts.Add($"{span.Hours} h");
        if (span.Minutes > 0 || parts.Count == 0) parts.Add($"{span.Minutes} min");
        return string.Join(" ", parts);
    }
}
