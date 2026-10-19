using BlazorStoc.Services;

namespace BlazorStoc.Modules.ProjectReports;

// The project situation and the consumption export: the situation of a project by components (needed, delivered, stock, deficit, purchase list, PDF and CSV),
// the equipment taken out for a project and the consumption export by beneficiary / project / period. Nothing else depends on it: the project, its components,
// the reservations, the exits and the stock alerts stay in the core and, while the module is out, only lose the column / link / suggestion that comes from here
// (the deficit notification is then unreadable for a while and closes nothing).
public sealed class ProjectReportsModule : IModule
{
    public const string ModuleId = "situatie-proiect";

    public string Id => ModuleId;
    public string Name => "Situația proiectului și export consum";
    public Version Version { get; } = new(1, 0, 0);
    // The links are in the stock menu ("Export consum"), on the main page and on the project page, each shown only while the module is available.
    public IReadOnlyList<ModuleNavItem> Nav { get; } = [];

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IProjectSituationReader>(provider => new GuardedProjectSituationReader(
            ActivatorUtilities.CreateInstance<MariaProjectSituationReader>(provider), provider.GetRequiredService<ModuleRegistry>()));
        services.AddSingleton<IProjectSituationPdfWriter, ProjectSituationPdfWriter>();
        services.AddScoped<IConsumptionReader>(provider => new GuardedConsumptionReader(
            ActivatorUtilities.CreateInstance<MariaConsumptionReader>(provider), provider.GetRequiredService<ModuleRegistry>()));
    }

    // Stand-ins used while the module is out: the components of a project, the reserve suggestions and the deficit notification still inject the reader.
    public void RegisterUnavailable(IServiceCollection services)
    {
        services.AddScoped<IProjectSituationReader, UnavailableProjectSituationReader>();
        services.AddScoped<IConsumptionReader, UnavailableConsumptionReader>();
        // The export endpoint takes the writer as a parameter: it must exist to be mapped (the reader is what says the module is out).
        services.AddSingleton<IProjectSituationPdfWriter, ProjectSituationPdfWriter>();
    }
}
