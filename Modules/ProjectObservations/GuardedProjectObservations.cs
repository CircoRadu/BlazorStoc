using BlazorStoc.Services;

namespace BlazorStoc.Modules.ProjectObservations;

// The observations of a project behind the circuit breaker of the registry: every call is reported (success or fault); while the circuit is open a call fails at once with
// ModuleUnavailableException. A refused input, a version conflict (ProjectOperationException) and access denied are answers, not faults.
internal sealed class GuardedProjectObservationRepository(IProjectObservationRepository inner, ModuleRegistry modules) : IProjectObservationRepository
{
    private const string Id = ProjectObservationsModule.ModuleId;

    public Task<IReadOnlyList<ProjectObservation>> GetObservationsAsync(int projectId, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.GetObservationsAsync(projectId, cancellationToken));

    public Task<ProjectObservation?> GetObservationAsync(int id, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.GetObservationAsync(id, cancellationToken));

    public Task<ProjectObservation> CreateObservationAsync(int projectId, ProjectObservationInput input, string author, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.CreateObservationAsync(projectId, input, author, cancellationToken));

    public Task<ProjectObservation> UpdateObservationAsync(ProjectObservation original, ProjectObservationInput input, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.UpdateObservationAsync(original, input, cancellationToken));

    public Task DeleteObservationAsync(ProjectObservation original, string reason, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.DeleteObservationAsync(original, reason, cancellationToken));
}

internal sealed class GuardedObservationFiles(IObservationFiles inner, ModuleRegistry modules) : IObservationFiles
{
    private const string Id = ProjectObservationsModule.ModuleId;

    public Task<IReadOnlyList<ProjectObservationFile>> GetFilesAsync(int observationId, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.GetFilesAsync(observationId, cancellationToken));

    public Task<ProjectFileContent?> GetContentAsync(int fileId, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.GetContentAsync(fileId, cancellationToken));

    public Task<ProjectObservationFile> SaveAsync(int observationId, string originalName, string declaredContentType, byte[] content, string author, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.SaveAsync(observationId, originalName, declaredContentType, content, author, cancellationToken));

    public Task DeleteAsync(int fileId, string reason, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.DeleteAsync(fileId, reason, cancellationToken));
}

// The stand-ins while the module is switched off or did not start: every call says the module is out.
internal sealed class UnavailableProjectObservationRepository : IProjectObservationRepository
{
    private static Task<T> Out<T>() => Task.FromException<T>(new ModuleUnavailableException(ProjectObservationsModule.ModuleId));

    public Task<IReadOnlyList<ProjectObservation>> GetObservationsAsync(int projectId, CancellationToken cancellationToken = default) => Out<IReadOnlyList<ProjectObservation>>();
    public Task<ProjectObservation?> GetObservationAsync(int id, CancellationToken cancellationToken = default) => Out<ProjectObservation?>();
    public Task<ProjectObservation> CreateObservationAsync(int projectId, ProjectObservationInput input, string author, CancellationToken cancellationToken = default) => Out<ProjectObservation>();
    public Task<ProjectObservation> UpdateObservationAsync(ProjectObservation original, ProjectObservationInput input, CancellationToken cancellationToken = default) => Out<ProjectObservation>();
    public Task DeleteObservationAsync(ProjectObservation original, string reason, CancellationToken cancellationToken = default) => Out<bool>();
}

internal sealed class UnavailableObservationFiles : IObservationFiles
{
    private static Task<T> Out<T>() => Task.FromException<T>(new ModuleUnavailableException(ProjectObservationsModule.ModuleId));

    public Task<IReadOnlyList<ProjectObservationFile>> GetFilesAsync(int observationId, CancellationToken cancellationToken = default) => Out<IReadOnlyList<ProjectObservationFile>>();
    public Task<ProjectFileContent?> GetContentAsync(int fileId, CancellationToken cancellationToken = default) => Out<ProjectFileContent?>();
    public Task<ProjectObservationFile> SaveAsync(int observationId, string originalName, string declaredContentType, byte[] content, string author, CancellationToken cancellationToken = default) => Out<ProjectObservationFile>();
    public Task DeleteAsync(int fileId, string reason, CancellationToken cancellationToken = default) => Out<bool>();
}
