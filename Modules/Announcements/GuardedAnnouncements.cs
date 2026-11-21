using BlazorStoc.Services;

namespace BlazorStoc.Modules.Announcements;

// The service behind the circuit breaker of the registry; the answers of the module (AnnouncementException) and the refusals are not faults.
internal sealed class GuardedAnnouncementService(IAnnouncementService inner, ModuleRegistry modules) : IAnnouncementService
{
    private const string Id = AnnouncementsModule.ModuleId;
    public Task<IReadOnlyList<AnnouncementView>> GetPendingAsync(CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.GetPendingAsync(cancellationToken));
    public Task MarkSeenAsync(int announcementId, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.MarkSeenAsync(announcementId, cancellationToken));
    public Task AcknowledgeAsync(int announcementId, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.AcknowledgeAsync(announcementId, cancellationToken));
    public Task<bool> GetHideNormalAsync(CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.GetHideNormalAsync(cancellationToken));
    public Task SetHideNormalAsync(bool hide, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.SetHideNormalAsync(hide, cancellationToken));
    public Task<AnnouncementImageContent?> GetImageAsync(int imageId, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.GetImageAsync(imageId, cancellationToken));
    public Task<IReadOnlyList<AnnouncementRow>> ListAsync(CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.ListAsync(cancellationToken));
    public Task<IReadOnlyList<AnnouncementSeen>> GetSeenByAsync(int id, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.GetSeenByAsync(id, cancellationToken));
    public Task<AnnouncementDetail?> GetAsync(int id, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.GetAsync(id, cancellationToken));
    public Task<int> SaveAsync(AnnouncementInput input, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.SaveAsync(input, cancellationToken));
    public Task<int> PublishAsync(AnnouncementInput input, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.PublishAsync(input, cancellationToken));
    public Task ActivateAsync(int id, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.ActivateAsync(id, cancellationToken));
    public Task ArchiveAsync(int id, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.ArchiveAsync(id, cancellationToken));
    public Task DeleteAsync(int id, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.DeleteAsync(id, cancellationToken));
    public Task<AnnouncementUploaded> UploadImageAsync(ReportImage image, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.UploadImageAsync(image, cancellationToken));
    public Task<IReadOnlyList<AnnouncementUserChoice>> GetUserChoicesAsync(CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.GetUserChoicesAsync(cancellationToken));
    public Task<AnnouncementImportResult> ImportZipAsync(Stream zip, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.ImportZipAsync(zip, cancellationToken));
    public Task<AnnouncementZip> ExportZipAsync(int id, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.ExportZipAsync(id, cancellationToken));
}

// The stand-in while the module is switched off or did not start: every call says the module is out (the pending list is simply empty).
internal sealed class UnavailableAnnouncementService : IAnnouncementService
{
    private static Task<T> Out<T>() => Task.FromException<T>(new ModuleUnavailableException(AnnouncementsModule.ModuleId));
    private static Task Out() => Task.FromException(new ModuleUnavailableException(AnnouncementsModule.ModuleId));
    public Task<IReadOnlyList<AnnouncementView>> GetPendingAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AnnouncementView>>([]);
    public Task MarkSeenAsync(int announcementId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task AcknowledgeAsync(int announcementId, CancellationToken cancellationToken = default) => Out();
    public Task<bool> GetHideNormalAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
    public Task SetHideNormalAsync(bool hide, CancellationToken cancellationToken = default) => Out();
    public Task<AnnouncementImageContent?> GetImageAsync(int imageId, CancellationToken cancellationToken = default) => Out<AnnouncementImageContent?>();
    public Task<IReadOnlyList<AnnouncementRow>> ListAsync(CancellationToken cancellationToken = default) => Out<IReadOnlyList<AnnouncementRow>>();
    public Task<IReadOnlyList<AnnouncementSeen>> GetSeenByAsync(int id, CancellationToken cancellationToken = default) => Out<IReadOnlyList<AnnouncementSeen>>();
    public Task<AnnouncementDetail?> GetAsync(int id, CancellationToken cancellationToken = default) => Out<AnnouncementDetail?>();
    public Task<int> SaveAsync(AnnouncementInput input, CancellationToken cancellationToken = default) => Out<int>();
    public Task<int> PublishAsync(AnnouncementInput input, CancellationToken cancellationToken = default) => Out<int>();
    public Task ActivateAsync(int id, CancellationToken cancellationToken = default) => Out();
    public Task ArchiveAsync(int id, CancellationToken cancellationToken = default) => Out();
    public Task DeleteAsync(int id, CancellationToken cancellationToken = default) => Out();
    public Task<AnnouncementUploaded> UploadImageAsync(ReportImage image, CancellationToken cancellationToken = default) => Out<AnnouncementUploaded>();
    public Task<IReadOnlyList<AnnouncementUserChoice>> GetUserChoicesAsync(CancellationToken cancellationToken = default) => Out<IReadOnlyList<AnnouncementUserChoice>>();
    public Task<AnnouncementImportResult> ImportZipAsync(Stream zip, CancellationToken cancellationToken = default) => Out<AnnouncementImportResult>();
    public Task<AnnouncementZip> ExportZipAsync(int id, CancellationToken cancellationToken = default) => Out<AnnouncementZip>();
}
