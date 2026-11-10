using BlazorStoc.Services;

namespace BlazorStoc.Modules.Tasks;

// The tasks module: tasks given by an administrator to a user on a beneficiary (title, description, optional deadline, status, files), the administrator tables and the page of
// the tasks of the signed-in user. Spec: docs/PROPUNERE_TASKURI_BENEFICIAR.md.
public sealed class TasksModule : IModule
{
    public const string ModuleId = "taskuri";

    public string Id => ModuleId;
    public string Name => "Taskuri utilizatori";
    public Version Version { get; } = new(1, 2, 2);
    // The administrator entry sits in the Administration menu and the user's page in the account menu (both in a gate of the module); no item of its own here.
    public IReadOnlyList<ModuleNavItem> Nav { get; } = [];

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IUserTaskRepository>(provider => new GuardedUserTaskRepository(
            ActivatorUtilities.CreateInstance<MariaUserTaskRepository>(provider), provider.GetRequiredService<ModuleRegistry>()));
        services.AddScoped<IUserTaskFiles>(provider => new GuardedUserTaskFiles(
            ActivatorUtilities.CreateInstance<MariaUserTaskFileStore>(provider, provider.GetRequiredService<IUserTaskRepository>()), provider.GetRequiredService<ModuleRegistry>()));
        services.AddScoped<IUserTaskAlerts, UserTaskAlerts>();
    }

    // Stand-ins used while the module is out: a page created by hand must still resolve what it injects.
    public void RegisterUnavailable(IServiceCollection services)
    {
        services.AddScoped<IUserTaskRepository, UnavailableUserTaskRepository>();
        services.AddScoped<IUserTaskFiles, UnavailableUserTaskFiles>();
        services.AddScoped<IUserTaskAlerts, UnavailableUserTaskAlerts>();
    }
}
