namespace BlazorStoc.Services;

// Change-event contract for projects, observations and observation files (TODO Task 2 / Subtask 2.5).
// Task 8 adds further sources (database triggers, SignalR) that publish to this same feed; pages only subscribe.
// Events carry identifiers only: no names, texts or file contents.
public sealed record ChangeEvent(string EntityType, string Action, string EntityId, Guid Origin, DateTime OccurredUtc,
    int? ProjectId = null, int? ObservationId = null, int? BeneficiaryId = null);

// Identifies the session (DI scope) that produced a change, so a page can ignore its own events.
public sealed class ChangeOrigin
{
    public Guid Id { get; } = Guid.NewGuid();
}

public interface IChangeFeed
{
    void Publish(ChangeEvent change);
    IDisposable Subscribe(Func<ChangeEvent, Task> handler);
}

// Remembers the changes published directly by sessions (they carry an origin) so the copy of the same change that a
// database trigger records a moment later is not announced a second time (Task 8).
public interface ILocalChangeLedger
{
    // True (and the entry is used up) when a matching local change was published recently.
    bool TryConsumeLocal(string entityType, string action, string entityId);
}

public sealed class InProcessChangeFeed(ILogger<InProcessChangeFeed>? logger = null, TimeProvider? timeProvider = null) : IChangeFeed, ILocalChangeLedger
{
    private static readonly TimeSpan LedgerLifetime = TimeSpan.FromSeconds(30);
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly object gate = new();
    private List<Func<ChangeEvent, Task>> handlers = [];
    private readonly List<(string Type, string Action, string Id, DateTimeOffset At)> ledger = [];

    public void Publish(ChangeEvent change)
    {
        Func<ChangeEvent, Task>[] snapshot;
        lock (gate)
        {
            snapshot = [.. handlers];
            if (change.Origin != Guid.Empty)
            {
                var now = clock.GetUtcNow();
                ledger.RemoveAll(entry => now - entry.At > LedgerLifetime);
                ledger.Add((change.EntityType, change.Action, change.EntityId, now));
            }
        }
        foreach (var handler in snapshot) _ = DeliverAsync(handler, change);
    }

    public bool TryConsumeLocal(string entityType, string action, string entityId)
    {
        lock (gate)
        {
            var now = clock.GetUtcNow();
            ledger.RemoveAll(entry => now - entry.At > LedgerLifetime);
            var index = ledger.FindIndex(entry => entry.Type == entityType && entry.Action == action && entry.Id == entityId);
            if (index < 0) return false;
            ledger.RemoveAt(index);
            return true;
        }
    }

    public IDisposable Subscribe(Func<ChangeEvent, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (gate) handlers = [.. handlers, handler];
        return new Subscription(this, handler);
    }

    // A failing subscriber never affects the operation that produced the event or the other subscribers.
    private async Task DeliverAsync(Func<ChangeEvent, Task> handler, ChangeEvent change)
    {
        try { await handler(change).ConfigureAwait(false); }
        catch (Exception exception) { logger?.LogWarning("A change subscriber failed ({ErrorType}).", exception.GetType().Name); }
    }

    private void Unsubscribe(Func<ChangeEvent, Task> handler)
    {
        lock (gate) handlers = handlers.Where(existing => existing != handler).ToList();
    }

    private sealed class Subscription(InProcessChangeFeed feed, Func<ChangeEvent, Task> handler) : IDisposable
    {
        private int disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0) feed.Unsubscribe(handler);
        }
    }
}

// Decides which pages a change concerns; keeps the filtering out of the components.
public static class ProjectChanges
{
    public static bool AffectsBeneficiary(ChangeEvent change, int beneficiaryId) =>
        change.EntityType == AuditEntities.Project && change.BeneficiaryId == beneficiaryId;

    // observationIds are the observations currently listed on the page; a file event whose observation is unknown
    // (removal by file id) is treated as relevant, since a refresh is cheap and never destructive.
    public static bool AffectsProject(ChangeEvent change, int projectId, IReadOnlyCollection<int> observationIds) => change.EntityType switch
    {
        AuditEntities.Project or AuditEntities.ProjectObservation => change.ProjectId == projectId,
        AuditEntities.ProjectObservationFile => change.ObservationId is null || observationIds.Contains(change.ObservationId.Value),
        _ => false
    };

    public static bool AffectsObservation(ChangeEvent change, int projectId, int observationId) => change.EntityType switch
    {
        AuditEntities.Project => change.ProjectId == projectId && change.Action == AuditActions.Delete,
        AuditEntities.ProjectObservation => change.ObservationId == observationId,
        AuditEntities.ProjectObservationFile => change.ObservationId is null || change.ObservationId == observationId,
        _ => false
    };

    public static bool IsDeletionOf(ChangeEvent change, string entityType, int id) =>
        change.EntityType == entityType && change.Action == AuditActions.Delete && change.EntityId == id.ToString();
}

// Publishes after the wrapped repository has committed; nothing is published when an operation throws.
public sealed class ChangeNotifyingProjectRepository(IProjectRepository inner, IChangeFeed feed, ChangeOrigin origin) : IProjectRepository
{
    public Task<IReadOnlyList<Project>> GetForBeneficiaryAsync(int beneficiaryId, CancellationToken cancellationToken = default) =>
        inner.GetForBeneficiaryAsync(beneficiaryId, cancellationToken);
    public Task<Project?> GetAsync(int id, CancellationToken cancellationToken = default) => inner.GetAsync(id, cancellationToken);
    public Task<IReadOnlyList<ProjectObservation>> GetObservationsAsync(int projectId, CancellationToken cancellationToken = default) =>
        inner.GetObservationsAsync(projectId, cancellationToken);
    public Task<ProjectObservation?> GetObservationAsync(int id, CancellationToken cancellationToken = default) =>
        inner.GetObservationAsync(id, cancellationToken);

    public async Task<Project> CreateAsync(ProjectInput input, CancellationToken cancellationToken = default)
    {
        var project = await inner.CreateAsync(input, cancellationToken).ConfigureAwait(false);
        PublishProject(AuditActions.Create, project.Id, project.BeneficiaryId);
        return project;
    }

    public async Task<Project> UpdateAsync(Project original, ProjectInput input, CancellationToken cancellationToken = default)
    {
        var updated = await inner.UpdateAsync(original, input, cancellationToken).ConfigureAwait(false);
        PublishProject(AuditActions.Edit, updated.Id, updated.BeneficiaryId);
        // A project moved to another beneficiary also disappears from the previous beneficiary's list.
        if (original.BeneficiaryId != updated.BeneficiaryId) PublishProject(AuditActions.Edit, updated.Id, original.BeneficiaryId);
        return updated;
    }

    public async Task DeleteAsync(Project original, string reason, CancellationToken cancellationToken = default)
    {
        await inner.DeleteAsync(original, reason, cancellationToken).ConfigureAwait(false);
        PublishProject(AuditActions.Delete, original.Id, original.BeneficiaryId);
    }

    public async Task<ProjectObservation> CreateObservationAsync(int projectId, ProjectObservationInput input, string author,
        CancellationToken cancellationToken = default)
    {
        var observation = await inner.CreateObservationAsync(projectId, input, author, cancellationToken).ConfigureAwait(false);
        PublishObservation(AuditActions.Create, observation);
        return observation;
    }

    public async Task<ProjectObservation> UpdateObservationAsync(ProjectObservation original, ProjectObservationInput input,
        CancellationToken cancellationToken = default)
    {
        var updated = await inner.UpdateObservationAsync(original, input, cancellationToken).ConfigureAwait(false);
        PublishObservation(AuditActions.Edit, updated);
        return updated;
    }

    public async Task DeleteObservationAsync(ProjectObservation original, string reason, CancellationToken cancellationToken = default)
    {
        await inner.DeleteObservationAsync(original, reason, cancellationToken).ConfigureAwait(false);
        PublishObservation(AuditActions.Delete, original);
    }

    private void PublishProject(string action, int projectId, int beneficiaryId) =>
        feed.Publish(new(AuditEntities.Project, action, projectId.ToString(), origin.Id, DateTime.UtcNow,
            ProjectId: projectId, BeneficiaryId: beneficiaryId));

    private void PublishObservation(string action, ProjectObservation observation) =>
        feed.Publish(new(AuditEntities.ProjectObservation, action, observation.Id.ToString(), origin.Id, DateTime.UtcNow,
            ProjectId: observation.ProjectId, ObservationId: observation.Id));
}

public sealed class ChangeNotifyingProjectFileStore(IProjectFileStore inner, IChangeFeed feed, ChangeOrigin origin) : IProjectFileStore
{
    public Task<IReadOnlyList<ProjectObservationFile>> GetFilesAsync(int observationId, CancellationToken cancellationToken = default) =>
        inner.GetFilesAsync(observationId, cancellationToken);
    public Task<ProjectFileContent?> GetContentAsync(int fileId, CancellationToken cancellationToken = default) =>
        inner.GetContentAsync(fileId, cancellationToken);

    public async Task<ProjectObservationFile> SaveAsync(int observationId, string originalName, string declaredContentType,
        byte[] content, string author, CancellationToken cancellationToken = default)
    {
        var file = await inner.SaveAsync(observationId, originalName, declaredContentType, content, author, cancellationToken).ConfigureAwait(false);
        feed.Publish(new(AuditEntities.ProjectObservationFile, AuditActions.Create, file.Id.ToString(), origin.Id, DateTime.UtcNow,
            ObservationId: file.ObservationId));
        return file;
    }

    public async Task DeleteAsync(int fileId, string reason, CancellationToken cancellationToken = default)
    {
        await inner.DeleteAsync(fileId, reason, cancellationToken).ConfigureAwait(false);
        // Removal is addressed by file id only, so the observation is not known here (see ProjectChanges).
        feed.Publish(new(AuditEntities.ProjectObservationFile, AuditActions.Delete, fileId.ToString(), origin.Id, DateTime.UtcNow));
    }
}
