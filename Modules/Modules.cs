namespace BlazorStoc.Modules;

// The module contract of the application. A module is a self-contained part (pages, services, notification sources, menu entry) that registers itself
// through IModule, is tracked by the ModuleRegistry and can fail without stopping the rest: a module that does not start is Disabled (no menu entry,
// its pages show a notice), a module whose calls keep failing is taken out for a cooldown by the circuit breaker, and the other modules go on.

public enum ModuleStatus
{
    /// <summary>Registered and answering.</summary>
    Healthy,
    /// <summary>Recent failures, still tried (the next call decides: success = Healthy, failure = Disabled again).</summary>
    Degraded,
    /// <summary>Not started, or taken out after repeated failures until the cooldown ends.</summary>
    Disabled
}

/// <summary>A menu entry of a module; shown only to a user holding the "view" permission of <see cref="PermissionModule"/>.</summary>
public sealed record ModuleNavItem(string Href, string Label, string Icon, string PermissionModule);

public interface IModule
{
    /// <summary>Stable identifier, also used in <see cref="ModuleAttribute"/> and <see cref="ModuleGate"/> (the permission module key is its own).</summary>
    string Id { get; }
    string Name { get; }
    Version Version { get; }
    IReadOnlyList<ModuleNavItem> Nav { get; }
    /// <summary>Registers the services of the module. An exception here disables the module; the other modules are not affected.</summary>
    void Register(IServiceCollection services, IConfiguration configuration);
    /// <summary>
    /// Registers the notification sources (IExpirySource) of the module. Called where the sources are registered, because the registration
    /// order of the sources is the order they are evaluated and listed in.
    /// </summary>
    void RegisterNotificationSources(IServiceCollection services) { }
    /// <summary>
    /// Registered instead of <see cref="Register"/> when the module is switched off or did not start: the services that pages of other modules inject
    /// (for example a repository) as stand-ins whose every call throws <see cref="ModuleUnavailableException"/>. Without them such a page would fail to
    /// be created at all, and the failure of one module would reach the others.
    /// </summary>
    void RegisterUnavailable(IServiceCollection services) { }
}

/// <summary>Marks a routable page as belonging to a module: while the module is Disabled the router shows <see cref="ModuleUnavailable"/> instead of the page.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ModuleAttribute(string moduleId) : Attribute
{
    public string ModuleId { get; } = moduleId;
}

/// <summary>
/// Marks an exception that is an answer of a module for the user (a refused file, a validation message), not a failure of the module: the circuit
/// breaker does not count it. The operation exceptions named *OperationException are recognised without it.
/// </summary>
public interface IExpectedAnswer { }

/// <summary>Thrown by the guarded entry points of a module while its circuit is open.</summary>
public sealed class ModuleUnavailableException(string moduleId)
    : Exception($"Modulul «{moduleId}» este indisponibil momentan.")
{
    public string ModuleId { get; } = moduleId;
}

/// <summary>
/// A module as the status screen shows it: Since is when it entered its present status (UTC); Reason is the type of the failure (never shown to ordinary users);
/// Origin is one of <see cref="ModuleOrigins"/>; Failures is the number of consecutive failures counted by the circuit breaker.
/// </summary>
public sealed record ModuleInfo(string Id, string Name, string Version, ModuleStatus Status, DateTime SinceUtc = default, string? Reason = null,
    string? Origin = null, int Failures = 0);

/// <summary>Why a module is out (shown to the administrator).</summary>
public static class ModuleOrigins
{
    public const string Configuration = "oprit din configurare";
    public const string Startup = "nu a pornit";
    public const string RepeatedFailures = "erori repetate";
}

public enum ModuleChangeKind { Disabled, Recovered }

/// <summary>A module taken out of service or back in: what the journal records (see <see cref="ModuleRegistry.Subscribe"/>).</summary>
public sealed record ModuleChange(string Id, string Name, ModuleChangeKind Kind, string? Reason, string Origin, DateTime AtUtc);
