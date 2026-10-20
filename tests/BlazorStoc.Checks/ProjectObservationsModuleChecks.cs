using BlazorStoc.Modules;
using BlazorStoc.Modules.ProjectObservations;
using BlazorStoc.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorStoc.Checks;

// The project observations module (the notes of a project and their files): the guarded repository and file store behind the circuit breaker (a refused input, a version
// conflict and access denied are answers, not faults), the registration over the whole project repository and file store, the stand-ins while it is out, the pages marked
// with the module and the parts of the project page that go away with it. Deleting a project (which archives its observations) stays in the core.
public static class ProjectObservationsModuleChecks
{
    private static IConfiguration Empty() => new ConfigurationBuilder().Build();

    private sealed class ThrowingProjects : IProjectRepository
    {
        public int Calls;
        public Exception? Failure = new InvalidOperationException("db down");
        private Task<T> Fail<T>() { Calls++; return Failure is null ? Task.FromResult(default(T)!) : Task.FromException<T>(Failure); }
        public Task<IReadOnlyList<Project>> GetForBeneficiaryAsync(int beneficiaryId, CancellationToken cancellationToken = default) => Fail<IReadOnlyList<Project>>();
        public Task<Project?> GetAsync(int id, CancellationToken cancellationToken = default) => Fail<Project?>();
        public Task<Project> CreateAsync(ProjectInput input, CancellationToken cancellationToken = default) => Fail<Project>();
        public Task<Project> UpdateAsync(Project original, ProjectInput input, CancellationToken cancellationToken = default) => Fail<Project>();
        public Task DeleteAsync(Project original, string reason, CancellationToken cancellationToken = default) => Fail<bool>();
        public Task<IReadOnlyList<ProjectObservation>> GetObservationsAsync(int projectId, CancellationToken cancellationToken = default) => Fail<IReadOnlyList<ProjectObservation>>();
        public Task<ProjectObservation?> GetObservationAsync(int id, CancellationToken cancellationToken = default) => Fail<ProjectObservation?>();
        public Task<ProjectObservation> CreateObservationAsync(int projectId, ProjectObservationInput input, string author, CancellationToken cancellationToken = default) => Fail<ProjectObservation>();
        public Task<ProjectObservation> UpdateObservationAsync(ProjectObservation original, ProjectObservationInput input, CancellationToken cancellationToken = default) => Fail<ProjectObservation>();
        public Task DeleteObservationAsync(ProjectObservation original, string reason, CancellationToken cancellationToken = default) => Fail<bool>();
    }

    private sealed class ThrowingFiles : IProjectFileStore
    {
        public int Calls;
        public Exception? Failure = new InvalidOperationException("disk down");
        private Task<T> Fail<T>() { Calls++; return Failure is null ? Task.FromResult(default(T)!) : Task.FromException<T>(Failure); }
        public Task<IReadOnlyList<ProjectObservationFile>> GetFilesAsync(int observationId, CancellationToken cancellationToken = default) => Fail<IReadOnlyList<ProjectObservationFile>>();
        public Task<ProjectFileContent?> GetContentAsync(int fileId, CancellationToken cancellationToken = default) => Fail<ProjectFileContent?>();
        public Task<ProjectObservationFile> SaveAsync(int observationId, string originalName, string declaredContentType, byte[] content, string author, CancellationToken cancellationToken = default) => Fail<ProjectObservationFile>();
        public Task DeleteAsync(int fileId, string reason, CancellationToken cancellationToken = default) => Fail<bool>();
    }

    public static async Task RunAsync(Action<bool, string> check)
    {
        await GuardedAsync(check);
        await WiringAsync(check);
        Architecture(check);
    }

    private static async Task<int> RefusedAsync(params Func<Task>[] calls)
    {
        var refused = 0;
        foreach (var call in calls)
        {
            try { await call(); } catch (ModuleUnavailableException) { refused++; }
        }
        return refused;
    }

    private static async Task GuardedAsync(Action<bool, string> check)
    {
        var clock = new ManualTimeProvider();
        var registry = new ModuleRegistry(clock);
        registry.Add(new ProjectObservationsModule(), new ServiceCollection(), Empty());
        var projects = new ThrowingProjects();
        var files = new ThrowingFiles();
        var observations = new GuardedProjectObservationRepository(projects, registry);
        var observationFiles = new GuardedObservationFiles(files, registry);
        var observation = default(ProjectObservation)!;
        var input = new ProjectObservationInput();
        for (var i = 0; i < ModuleRegistry.FailureThreshold; i++)
        {
            try { await observations.GetObservationsAsync(1); } catch (InvalidOperationException) { }
        }
        check(registry.StatusOf(ProjectObservationsModule.ModuleId) == ModuleStatus.Disabled && projects.Calls == ModuleRegistry.FailureThreshold,
            "Project observations: repeated faults of the repository open the circuit of the module");
        check(await RefusedAsync(() => observations.GetObservationsAsync(1), () => observations.GetObservationAsync(1), () => observations.CreateObservationAsync(1, input, "a"),
                  () => observations.UpdateObservationAsync(observation, input), () => observations.DeleteObservationAsync(observation, "motiv")) == 5
              && projects.Calls == ModuleRegistry.FailureThreshold,
            "Project observations: with the circuit open all five entry points of the repository refuse at once and the repository is not called");
        check(await RefusedAsync(() => observationFiles.GetFilesAsync(1), () => observationFiles.GetContentAsync(1), () => observationFiles.SaveAsync(1, "a.pdf", "application/pdf", [], "a"),
                  () => observationFiles.DeleteAsync(1, "motiv")) == 4 && files.Calls == 0,
            "Project observations: the files share the circuit of the module and the file store is not called while it is open");

        clock.Advance(ModuleRegistry.Cooldown + TimeSpan.FromSeconds(1));
        projects.Failure = null;
        await observations.GetObservationsAsync(1);
        check(registry.StatusOf(ProjectObservationsModule.ModuleId) == ModuleStatus.Healthy, "Project observations: after the cooldown the first successful call brings the module back");

        foreach (Exception answer in new Exception[] { new ProjectOperationException("version changed"), new AccessDeniedException("no") })
        {
            projects.Failure = answer;
            for (var i = 0; i < 10; i++) { try { await observations.UpdateObservationAsync(observation, input); } catch (Exception ex) when (ex.GetType() == answer.GetType()) { } }
        }
        check(registry.StatusOf(ProjectObservationsModule.ModuleId) == ModuleStatus.Healthy,
            "Project observations: a refused input or version conflict (ProjectOperationException) and access denied reach the page unchanged and never disable the module");

        files.Failure = new IOException("disk down");
        for (var i = 0; i < ModuleRegistry.FailureThreshold; i++) { try { await observationFiles.GetFilesAsync(1); } catch (IOException) { } }
        check(registry.StatusOf(ProjectObservationsModule.ModuleId) == ModuleStatus.Disabled, "Project observations: repeated faults of the file store open the same circuit");
    }

    private static async Task WiringAsync(Action<bool, string> check)
    {
        var registry = new ModuleRegistry();
        var services = new ServiceCollection();
        services.AddSingleton<ModuleRegistry>(registry);
        services.AddSingleton<IProjectRepository, ThrowingProjects>();
        services.AddSingleton<IProjectFileStore, ThrowingFiles>();
        registry.Add(new ProjectObservationsModule(), services, Empty());
        using (var provider = services.BuildServiceProvider())
        using (var scope = provider.CreateScope())
        {
            var repository = scope.ServiceProvider.GetService<IProjectObservationRepository>();
            var files = scope.ServiceProvider.GetService<IObservationFiles>();
            check(repository?.GetType().Name == "GuardedProjectObservationRepository" && files?.GetType().Name == "GuardedObservationFiles",
                "Project observations: the module registers the guarded repository and file store over the application's project repository and file store");
        }
        var module = new ProjectObservationsModule();
        check(module.Id == "observatii-proiect" && module.Nav.Count == 0, "Project observations: the module id is observatii-proiect and it adds no menu entry of its own (the list is on the project page)");
        check(typeof(IProjectObservationRepository).IsAssignableFrom(typeof(IProjectRepository)) && typeof(IObservationFiles).IsAssignableFrom(typeof(IProjectFileStore)),
            "Project observations: the whole project repository and file store are the observation reader/writer, so the deletion of a project and every writer are unchanged");

        var pageNames = new[] { "ProjectObservationPage", "ProjectObservationRedirect" };
        var pages = typeof(ModuleAttribute).Assembly.GetTypes().Where(type => type.Namespace == "BlazorStoc.Components.Pages" && pageNames.Contains(type.Name)).ToList();
        check(pages.Count == 2 && pages.All(page => page.GetCustomAttributes(typeof(ModuleAttribute), false).Cast<ModuleAttribute>().SingleOrDefault()?.ModuleId == module.Id),
            "Project observations: the page of an observation and the link resolving an observation id are marked with the module, so the router replaces them while it is out");

        var offRegistry = new ModuleRegistry();
        var offServices = new ServiceCollection();
        var offConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Modules:observatii-proiect:Enabled"] = "false" }).Build();
        offRegistry.Add(new ProjectObservationsModule(), offServices, offConfiguration);
        using (var provider = offServices.BuildServiceProvider())
        using (var scope = provider.CreateScope())
        {
            var repository = scope.ServiceProvider.GetService<IProjectObservationRepository>();
            var files = scope.ServiceProvider.GetService<IObservationFiles>();
            var refused = 0;
            try { if (repository is not null) await repository.GetObservationsAsync(1); } catch (ModuleUnavailableException) { refused++; }
            try { if (files is not null) await files.GetContentAsync(1); } catch (ModuleUnavailableException) { refused++; }
            check(offRegistry.StatusOf("observatii-proiect") == ModuleStatus.Disabled && refused == 2,
                "Project observations: with the module switched off the repository and the file store still resolve and every call says the module is out");
        }

        var application = new ModuleRegistry();
        var applicationServices = new ServiceCollection();
        BlazorStoc.Startup.ServiceRegistration.AddModules(applicationServices, Empty(), application);
        check(application.StatusOf("observatii-proiect") == ModuleStatus.Healthy && applicationServices.Count(d => d.ServiceType == typeof(IProjectObservationRepository)) == 1
              && applicationServices.Count(d => d.ServiceType == typeof(IObservationFiles)) == 1,
            "Project observations: the application registers the module, once");
    }

    // The sources of the module live in Modules/ProjectObservations; the parts of the project page and the file download go away or answer 503 with it.
    private static void Architecture(Action<bool, string> check)
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "BlazorStoc.csproj"))) root = Path.GetDirectoryName(root.TrimEnd(Path.DirectorySeparatorChar));
        if (root is null) { check(false, "Project observations architecture: the project folder is found"); return; }
        var folder = Path.Combine(root, "Modules", "ProjectObservations");
        check(new[] { "ProjectObservationPage.razor", "ProjectObservationEditor.razor", "ProjectObservationRedirect.razor" }
                  .All(name => File.Exists(Path.Combine(folder, name)) && !File.Exists(Path.Combine(root, "Components", "Pages", name))),
            "Architecture: the project observations module lives in Modules/ProjectObservations (the pages are not left in Components/Pages)");
        string Read(params string[] path) => File.ReadAllText(Path.Combine([root, .. path]));
        var project = Read("Components", "Pages", "ProjectPage.razor");
        check(project.Contains("ModuleGate ModuleId=\"observatii-proiect\"") && project.Contains("@inject IProjectObservationRepository Observations") && project.Contains("@inject IObservationFiles FileStore"),
            "Project observations: the add button and editor of the project page are in a gate of the module and the list is read through the module's guarded reader");
        check(project.Contains("Modules.IsAvailable(\"observatii-proiect\")") && project.Contains("observationsFailed"),
            "Project observations: the project page shows the project even when the observations are out or fail to load");
        var program = Read("Program.cs");
        check(program.Contains("/media/project-files/{fileId:int}\", async (int fileId, IObservationFiles files") && program.Contains("catch (BlazorStoc.Modules.ModuleUnavailableException) { return Results.StatusCode(StatusCodes.Status503ServiceUnavailable); }"),
            "Project observations: the download of an attached file answers 503 while the module is out");
        var repositorySource = Read("Services", "MariaProjectRepository.cs");
        check(!repositorySource.Contains("IObservationFiles") && !repositorySource.Contains("Guarded"),
            "Project observations: the deletion of a project reads the observations from the core, not through the module");
    }
}
