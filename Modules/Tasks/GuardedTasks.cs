using BlazorStoc.Services;

namespace BlazorStoc.Modules.Tasks;

// The entry points of the tasks module behind the circuit breaker of the registry: every call is reported (success or fault); while the circuit is open a call fails at once
// with ModuleUnavailableException. The answers of the module (UserTaskOperationException, access denied, refused files) are not faults.
internal sealed class GuardedUserTaskRepository(IUserTaskRepository inner, ModuleRegistry modules) : IUserTaskRepository
{
    private const string Id = TasksModule.ModuleId;

    public Task<IReadOnlyList<UserTask>> GetAllAsync(CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.GetAllAsync(cancellationToken));

    public Task<IReadOnlyList<UserTask>> GetForBeneficiaryAsync(int beneficiaryId, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.GetForBeneficiaryAsync(beneficiaryId, cancellationToken));

    public Task<IReadOnlyList<UserTask>> GetMineAsync(CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.GetMineAsync(cancellationToken));

    public Task<UserTask?> GetAsync(int id, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.GetAsync(id, cancellationToken));

    public Task<IReadOnlyList<UserTaskUser>> GetAssignableUsersAsync(CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.GetAssignableUsersAsync(cancellationToken));

    public Task<UserTask> CreateAsync(UserTaskInput input, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.CreateAsync(input, cancellationToken));

    public Task<UserTask> UpdateAsync(UserTask original, UserTaskInput input, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.UpdateAsync(original, input, cancellationToken));

    public Task DeleteAsync(UserTask original, string reason, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.DeleteAsync(original, reason, cancellationToken));

    public Task<UserTask> SetStatusAsync(UserTask original, UserTaskStatus status, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.SetStatusAsync(original, status, cancellationToken));

    public Task<UserTask> AcceptAsync(UserTask original, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.AcceptAsync(original, cancellationToken));

    public Task<int> GetWarningDaysAsync(CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.GetWarningDaysAsync(cancellationToken));

    public Task SetWarningDaysAsync(int days, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.SetWarningDaysAsync(days, cancellationToken));
}

internal sealed class GuardedUserTaskFiles(IUserTaskFiles inner, ModuleRegistry modules) : IUserTaskFiles
{
    private const string Id = TasksModule.ModuleId;

    public Task<IReadOnlyList<EntityFile>> GetAsync(int taskId, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.GetAsync(taskId, cancellationToken));

    public Task<EntityFileContent?> GetContentAsync(int fileId, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.GetContentAsync(fileId, cancellationToken));

    public Task<EntityFile> AddAsync(int taskId, string originalName, string declaredContentType, byte[] content, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.AddAsync(taskId, originalName, declaredContentType, content, cancellationToken));

    public Task DeleteAsync(int fileId, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.DeleteAsync(fileId, cancellationToken));
}

// The stand-ins of the services while the module is switched off or did not start: every call says the module is out.
internal sealed class UnavailableUserTaskRepository : IUserTaskRepository
{
    private static Task<T> Out<T>() => Task.FromException<T>(new ModuleUnavailableException(TasksModule.ModuleId));

    public Task<IReadOnlyList<UserTask>> GetAllAsync(CancellationToken cancellationToken = default) => Out<IReadOnlyList<UserTask>>();
    public Task<IReadOnlyList<UserTask>> GetForBeneficiaryAsync(int beneficiaryId, CancellationToken cancellationToken = default) => Out<IReadOnlyList<UserTask>>();
    public Task<IReadOnlyList<UserTask>> GetMineAsync(CancellationToken cancellationToken = default) => Out<IReadOnlyList<UserTask>>();
    public Task<UserTask?> GetAsync(int id, CancellationToken cancellationToken = default) => Out<UserTask?>();
    public Task<IReadOnlyList<UserTaskUser>> GetAssignableUsersAsync(CancellationToken cancellationToken = default) => Out<IReadOnlyList<UserTaskUser>>();
    public Task<UserTask> CreateAsync(UserTaskInput input, CancellationToken cancellationToken = default) => Out<UserTask>();
    public Task<UserTask> UpdateAsync(UserTask original, UserTaskInput input, CancellationToken cancellationToken = default) => Out<UserTask>();
    public Task DeleteAsync(UserTask original, string reason, CancellationToken cancellationToken = default) => Out<bool>();
    public Task<UserTask> SetStatusAsync(UserTask original, UserTaskStatus status, CancellationToken cancellationToken = default) => Out<UserTask>();
    public Task<UserTask> AcceptAsync(UserTask original, CancellationToken cancellationToken = default) => Out<UserTask>();
    public Task<int> GetWarningDaysAsync(CancellationToken cancellationToken = default) => Out<int>();
    public Task SetWarningDaysAsync(int days, CancellationToken cancellationToken = default) => Out<bool>();
}

internal sealed class UnavailableUserTaskFiles : IUserTaskFiles
{
    private static Task<T> Out<T>() => Task.FromException<T>(new ModuleUnavailableException(TasksModule.ModuleId));

    public Task<IReadOnlyList<EntityFile>> GetAsync(int taskId, CancellationToken cancellationToken = default) => Out<IReadOnlyList<EntityFile>>();
    public Task<EntityFileContent?> GetContentAsync(int fileId, CancellationToken cancellationToken = default) => Out<EntityFileContent?>();
    public Task<EntityFile> AddAsync(int taskId, string originalName, string declaredContentType, byte[] content, CancellationToken cancellationToken = default) => Out<EntityFile>();
    public Task DeleteAsync(int fileId, CancellationToken cancellationToken = default) => Out<bool>();
}
