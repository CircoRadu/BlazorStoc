using BlazorStoc.Services;

namespace BlazorStoc.Modules.Journal;

// The activity journal module: the two pages that read the journal (the latest events and the complete journal, with filters, search, paging and the export), and
// the reader behind the circuit breaker. WRITING to the journal is not here: every part of the application records its operations through IAuditTrail (the core),
// so a journal page that fails never stops an operation from being recorded.
public sealed class JournalModule : IModule
{
    public const string ModuleId = "jurnal";

    public string Id => ModuleId;
    public string Name => "Jurnal de activitate";
    public Version Version { get; } = new(1, 0, 0);
    // The menu entry sits after the notifications (MainLayout), shown only while the module is available.
    public IReadOnlyList<ModuleNavItem> Nav { get; } = [];

    public void Register(IServiceCollection services, IConfiguration configuration) =>
        services.AddScoped<IAuditReader>(provider => new GuardedAuditReader(provider.GetRequiredService<IAuditTrail>(), provider.GetRequiredService<ModuleRegistry>()));

    // Stand-in used while the module is out (nothing outside the two pages injects the reader, but a page created by hand must not fail to resolve it).
    public void RegisterUnavailable(IServiceCollection services) => services.AddScoped<IAuditReader, UnavailableAuditReader>();
}
