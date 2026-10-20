using BlazorStoc.Services;

namespace BlazorStoc.Modules.ProjectObservations;

// The project observations module: the notes of a project with their attached files (the page of an observation, the editor, the link from the audit journal and the list on
// the project page), behind the circuit breaker. The project itself, its components and its deletion stay in the core: deleting a project archives its observations and files
// in the same transaction, so the whole repository and file store keep reading and writing them (IProjectObservationRepository and IObservationFiles are the part the
// module's pages use).
public sealed class ProjectObservationsModule : IModule
{
    public const string ModuleId = "observatii-proiect";

    public string Id => ModuleId;
    public string Name => "Observații proiect";
    public Version Version { get; } = new(1, 0, 0);
    // No menu entry: the observations are listed on the project page, in a gate of the module.
    public IReadOnlyList<ModuleNavItem> Nav { get; } = [];

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IProjectObservationRepository>(provider => new GuardedProjectObservationRepository(
            provider.GetRequiredService<IProjectRepository>(), provider.GetRequiredService<ModuleRegistry>()));
        services.AddScoped<IObservationFiles>(provider => new GuardedObservationFiles(
            provider.GetRequiredService<IProjectFileStore>(), provider.GetRequiredService<ModuleRegistry>()));
    }

    // Stand-ins used while the module is out: a page created by hand must still resolve what it injects.
    public void RegisterUnavailable(IServiceCollection services)
    {
        services.AddScoped<IProjectObservationRepository, UnavailableProjectObservationRepository>();
        services.AddScoped<IObservationFiles, UnavailableObservationFiles>();
    }
}
