using BlazorStoc.Services;
using MySqlConnector;

namespace BlazorStoc.Modules;

// The registry of the modules and their circuit breakers (singleton). Registration is isolated per module; calls are reported by the guarded
// entry points (see ModuleGuard) and by consumers of a module living in other pages (TryRunAsync).
//
// Circuit breaker: FailureThreshold consecutive faults open the circuit (Disabled) for Cooldown; after it the module is Degraded and the next call
// decides (success closes the circuit, a fault opens it again at once). What counts as a fault: an exception that is neither an expected answer of the
// module (its own operation exception, access denied, cancellation) nor a database outage (the database is reported by /health, not blamed on a module).
public sealed class ModuleRegistry(TimeProvider? clock = null)
{
    public const int FailureThreshold = 3;
    public static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(30);

    private readonly object gate = new();
    private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
    private readonly List<string> order = [];
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private ILogger? logger;

    /// <summary>The registry is created before the host exists; once it does, the logger is attached and the startup failures are written to the log.</summary>
    public void AttachLogger(ILogger attached)
    {
        logger = attached;
        foreach (var info in Snapshot().Where(item => item.Status == ModuleStatus.Disabled))
            logger.LogError("Module {ModuleId} is disabled since startup ({ErrorType}).", info.Id, ReasonOf(info.Id));
    }

    private sealed class Entry(IModule module)
    {
        public IModule Module { get; } = module;
        public ModuleStatus Status = ModuleStatus.Healthy;
        public string? Reason;
        public bool StartupFailed;
        public bool HalfOpen;
        public int Failures;
        public DateTimeOffset OpenUntil;
        public string Origin = "";
        public DateTime SinceUtc;
        // A "Disabled" change was announced and no "Recovered" has followed: the next return to health is announced too.
        public bool Announced;
    }

    // ---- changes (for the journal) ----

    private readonly List<Action<ModuleChange>> subscribers = [];
    private readonly List<ModuleChange> startupChanges = [];

    /// <summary>Raised when a module changes state in a way a screen shows (taken out, back, "try again", circuit opened again); a screen re-reads <see cref="Snapshot"/>.</summary>
    public event Action? Changed;

    private void NotifyChanged()
    {
        var handlers = Changed;
        if (handlers is null) return;
        foreach (var handler in handlers.GetInvocationList().Cast<Action>())
        {
            // A screen that is being torn down must never reach the code that reported the failure.
            try { handler(); }
            catch (Exception exception) { logger?.LogWarning("A listener of the module state failed ({ErrorType}).", exception.GetType().Name); }
        }
    }

    /// <summary>
    /// Tells the subscriber every time a module is taken out of service or comes back (not the first failures: only the change of state). The modules that were already
    /// out at startup (a failed registration, a switch in the configuration) are announced to the first subscriber, since nobody listened when it happened.
    /// </summary>
    public void Subscribe(Action<ModuleChange> handler)
    {
        List<ModuleChange> replay;
        lock (gate)
        {
            subscribers.Add(handler);
            replay = [.. startupChanges];
        }
        foreach (var change in replay) Invoke(handler, change);
    }

    private void Raise(ModuleChange? change)
    {
        if (change is null) return;
        NotifyChanged();
        List<Action<ModuleChange>> targets;
        lock (gate) targets = [.. subscribers];
        foreach (var handler in targets) Invoke(handler, change);
    }

    private void Invoke(Action<ModuleChange> handler, ModuleChange change)
    {
        // A subscriber that fails (the journal while the database is down) must never reach the code that reported the failure.
        try { handler(change); }
        catch (Exception exception) { logger?.LogWarning("A subscriber of the module changes failed ({ErrorType}).", exception.GetType().Name); }
    }

    private void RecordStartupChange(Entry entry)
    {
        var change = new ModuleChange(entry.Module.Id, entry.Module.Name, ModuleChangeKind.Disabled, entry.Reason, entry.Origin, time.GetUtcNow().UtcDateTime);
        lock (gate) startupChanges.Add(change);
    }

    /// <summary>Registers a module: its services are added through <see cref="IModule.Register"/>; a failure disables only this module.</summary>
    public bool Add(IModule module, IServiceCollection services, IConfiguration configuration)
    {
        var entry = new Entry(module);
        // A module is registered in a scratch collection first: a module that fails halfway leaves none of its services behind.
        var scratch = new ServiceCollection();
        // Modules:<id>:Enabled = false switches a module off from the configuration (its services are not registered at all).
        if (configuration.GetValue<bool?>($"Modules:{module.Id}:Enabled") == false)
        {
            entry.Status = ModuleStatus.Disabled;
            entry.StartupFailed = true;
            entry.Reason = "disabled-by-configuration";
            entry.Origin = ModuleOrigins.Configuration;
            entry.SinceUtc = time.GetUtcNow().UtcDateTime;
            AddUnavailable(module, services);
            RecordStartupChange(entry);
            lock (gate)
            {
                entries[module.Id] = entry;
                if (!order.Contains(module.Id)) order.Add(module.Id);
            }
            return false;
        }
        try
        {
            module.Register(scratch, configuration);
            foreach (var descriptor in scratch) services.Add(descriptor);
        }
        catch (Exception exception)
        {
            entry.Status = ModuleStatus.Disabled;
            entry.StartupFailed = true;
            entry.Reason = exception.GetType().Name;
            entry.Origin = ModuleOrigins.Startup;
            entry.SinceUtc = time.GetUtcNow().UtcDateTime;
            AddUnavailable(module, services);
            RecordStartupChange(entry);
        }
        if (entry.SinceUtc == default) entry.SinceUtc = time.GetUtcNow().UtcDateTime;
        lock (gate)
        {
            entries[module.Id] = entry;
            if (!order.Contains(module.Id)) order.Add(module.Id);
        }
        return !entry.StartupFailed;
    }

    // The stand-ins of a module that is out (see IModule.RegisterUnavailable); registered in a scratch collection like the module itself.
    private void AddUnavailable(IModule module, IServiceCollection services)
    {
        var scratch = new ServiceCollection();
        try
        {
            module.RegisterUnavailable(scratch);
            foreach (var descriptor in scratch) services.Add(descriptor);
        }
        catch (Exception exception) { logger?.LogError("Module {ModuleId} could not register its stand-ins ({ErrorType}).", module.Id, exception.GetType().Name); }
    }

    /// <summary>Registers the notification sources of every module that started; a failing module only loses its own sources.</summary>
    public void AddNotificationSources(IServiceCollection services)
    {
        foreach (var id in Ids())
        {
            Entry entry;
            lock (gate) entry = entries[id];
            if (entry.StartupFailed) continue;
            var scratch = new ServiceCollection();
            try
            {
                entry.Module.RegisterNotificationSources(scratch);
                foreach (var descriptor in scratch) services.Add(descriptor);
            }
            catch (Exception exception)
            {
                lock (gate)
                {
                    entry.Status = ModuleStatus.Disabled; entry.StartupFailed = true; entry.Reason = exception.GetType().Name;
                    entry.Origin = ModuleOrigins.Startup; entry.SinceUtc = time.GetUtcNow().UtcDateTime;
                }
                RecordStartupChange(entry);
            }
        }
    }

    private List<string> Ids() { lock (gate) return [.. order]; }

    public IReadOnlyList<IModule> Modules
    {
        get { lock (gate) return [.. order.Select(id => entries[id].Module)]; }
    }

    public IReadOnlyList<ModuleInfo> Snapshot()
    {
        lock (gate)
            return [.. order.Select(id =>
            {
                var entry = entries[id];
                var status = Current(entry);
                return new ModuleInfo(id, entry.Module.Name, entry.Module.Version.ToString(), status, entry.SinceUtc,
                    status == ModuleStatus.Healthy ? null : entry.Reason, status == ModuleStatus.Healthy ? null : entry.Origin, entry.Failures);
            })];
    }

    /// <summary>The status of a module now (an open circuit whose cooldown ended is Degraded). An unknown module is reported as Disabled.</summary>
    public ModuleStatus StatusOf(string moduleId)
    {
        lock (gate) return entries.TryGetValue(moduleId, out var entry) ? Current(entry) : ModuleStatus.Disabled;
    }

    /// <summary>The reason of a Disabled/Degraded module, for the journal and the logs only (it is not shown to users).</summary>
    public string? ReasonOf(string moduleId)
    {
        lock (gate) return entries.TryGetValue(moduleId, out var entry) ? entry.Reason : null;
    }

    public bool IsAvailable(string moduleId) => StatusOf(moduleId) != ModuleStatus.Disabled;

    /// <summary>The menu entries of the modules that are available now, in registration order.</summary>
    public IReadOnlyList<ModuleNavItem> NavItems()
    {
        lock (gate) return [.. order.Where(id => Current(entries[id]) != ModuleStatus.Disabled).SelectMany(id => entries[id].Module.Nav)];
    }

    /// <summary>Throws <see cref="ModuleUnavailableException"/> while the circuit of the module is open.</summary>
    public void EnsureAvailable(string moduleId)
    {
        if (!IsAvailable(moduleId)) throw new ModuleUnavailableException(moduleId);
    }

    public void ReportSuccess(string moduleId)
    {
        ModuleChange? change = null;
        lock (gate)
        {
            if (!entries.TryGetValue(moduleId, out var entry) || entry.StartupFailed) return;
            // A call that was already running when the circuit opened does not close it; only the cooldown does.
            if (Current(entry) == ModuleStatus.Disabled) return;
            var wasOut = entry.Announced;
            entry.Failures = 0;
            entry.HalfOpen = false;
            if (entry.Status != ModuleStatus.Healthy) entry.SinceUtc = time.GetUtcNow().UtcDateTime;
            entry.Status = ModuleStatus.Healthy;
            entry.Reason = null;
            entry.Origin = "";
            if (wasOut)
            {
                entry.Announced = false;
                change = new ModuleChange(entry.Module.Id, entry.Module.Name, ModuleChangeKind.Recovered, null, "", time.GetUtcNow().UtcDateTime);
            }
        }
        Raise(change);
    }

    /// <summary>
    /// Lets the next call of a module that was taken out for repeated failures through at once, without waiting for the cooldown (the administrator's "try again").
    /// A module that did not start, or is switched off in the configuration, needs a restart of the application: false.
    /// </summary>
    public bool Retry(string moduleId)
    {
        lock (gate)
        {
            if (!entries.TryGetValue(moduleId, out var entry) || entry.StartupFailed || Current(entry) != ModuleStatus.Disabled) return false;
            entry.OpenUntil = time.GetUtcNow();
            Current(entry);
        }
        NotifyChanged();
        return true;
    }

    public void ReportFailure(string moduleId, Exception exception)
    {
        if (!IsFault(exception)) return;
        if (exception is IBelongsToModule owner && owner.ModuleId != moduleId) return;
        var opened = false;
        ModuleChange? change = null;
        lock (gate)
        {
            if (!entries.TryGetValue(moduleId, out var entry) || entry.StartupFailed) return;
            Current(entry);
            entry.Failures++;
            entry.Reason = exception.GetType().Name;
            // After the cooldown (half-open) one fault is enough to open the circuit again.
            if (entry.Failures >= FailureThreshold || entry.HalfOpen)
            {
                entry.HalfOpen = false;
                entry.Status = ModuleStatus.Disabled;
                entry.OpenUntil = time.GetUtcNow() + Cooldown;
                opened = true;
                entry.Origin = ModuleOrigins.RepeatedFailures;
                entry.SinceUtc = time.GetUtcNow().UtcDateTime;
                // Announced once per outage: a circuit that opens again right after the cooldown (a fault while half-open) is the same outage.
                if (!entry.Announced)
                {
                    entry.Announced = true;
                    change = new ModuleChange(entry.Module.Id, entry.Module.Name, ModuleChangeKind.Disabled, entry.Reason, ModuleOrigins.RepeatedFailures, entry.SinceUtc);
                }
            }
            else if (entry.Status == ModuleStatus.Healthy) { entry.Status = ModuleStatus.Degraded; entry.SinceUtc = time.GetUtcNow().UtcDateTime; }
        }
        Raise(change);
        if (opened && change is null) NotifyChanged();
        if (opened) logger?.LogError("Module {ModuleId} is taken out for {Seconds} s after repeated failures ({ErrorType}).", moduleId, (int)Cooldown.TotalSeconds, exception.GetType().Name);
        else logger?.LogWarning("Module {ModuleId} call failed ({ErrorType}).", moduleId, exception.GetType().Name);
    }

    /// <summary>What counts as a failure of the module (see the class comment).</summary>
    public static bool IsFault(Exception exception) =>
        exception is not (OperationCanceledException or AccessDeniedException or ModuleUnavailableException or MySqlException or TimeoutException) &&
        !IsExpectedAnswer(exception);

    // The operation exceptions of the modules are answers for the user (validation, conflict), not failures: by convention a type of the application named
    // *OperationException (System.InvalidOperationException is a programming error and does count).
    private static bool IsExpectedAnswer(Exception exception) =>
        exception is IExpectedAnswer ||
        exception.GetType().Name.EndsWith("OperationException", StringComparison.Ordinal) && exception.GetType().Namespace?.StartsWith("BlazorStoc", StringComparison.Ordinal) == true;

    private ModuleStatus Current(Entry entry)
    {
        if (entry.Status == ModuleStatus.Disabled && !entry.StartupFailed && time.GetUtcNow() >= entry.OpenUntil)
        {
            // Cooldown over: let the next call through (half-open).
            entry.Status = ModuleStatus.Degraded;
            entry.HalfOpen = true;
        }
        return entry.Status;
    }

    /// <summary>
    /// Runs an operation of a module for a consumer that is not part of it (another page): a failing or unavailable module gives the fallback
    /// instead of an exception (cancellation and access denied still propagate), so the consumer page loads without it.
    /// </summary>
    public async Task<T> TryRunAsync<T>(string moduleId, Func<Task<T>> operation, T fallback)
    {
        if (!IsAvailable(moduleId)) return fallback;
        try
        {
            var result = await operation().ConfigureAwait(false);
            ReportSuccess(moduleId);
            return result;
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or AccessDeniedException))
        {
            ReportFailure(moduleId, exception);
            return fallback;
        }
    }

    /// <summary>The guarded call of a module's own operation: the circuit decides, the result is reported, the exception goes on to the caller.</summary>
    public async Task<T> RunAsync<T>(string moduleId, Func<Task<T>> operation)
    {
        EnsureAvailable(moduleId);
        try
        {
            var result = await operation().ConfigureAwait(false);
            ReportSuccess(moduleId);
            return result;
        }
        catch (Exception exception)
        {
            ReportFailure(moduleId, exception);
            throw;
        }
    }

    public async Task RunAsync(string moduleId, Func<Task> operation) =>
        await RunAsync<object?>(moduleId, async () => { await operation().ConfigureAwait(false); return null; }).ConfigureAwait(false);
}
