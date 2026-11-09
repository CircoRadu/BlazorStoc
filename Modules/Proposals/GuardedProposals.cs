using BlazorStoc.Services;

namespace BlazorStoc.Modules.Proposals;

// The service behind the circuit breaker of the registry; the answers of the module (ProposalException) and the refusals are not faults.
internal sealed class GuardedProposalService(IProposalService inner, ModuleRegistry modules) : IProposalService
{
    private const string Id = ProposalsModule.ModuleId;
    public Task<Proposal> CreateAsync(ProposalInput input, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.CreateAsync(input, cancellationToken));
    public Task<IReadOnlyList<Proposal>> GetMineAsync(CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.GetMineAsync(cancellationToken));
    public Task<IReadOnlyList<Proposal>> GetBoardAsync(string? text, bool onlyOpen, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.GetBoardAsync(text, onlyOpen, cancellationToken));
    public Task<Proposal?> GetAsync(int id, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.GetAsync(id, cancellationToken));
    public Task<ProposalPage> QueryAsync(ProposalQuery query, int page, int pageSize, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.QueryAsync(query, page, pageSize, cancellationToken));
    public Task<IReadOnlyList<ProposalNote>> GetNotesAsync(int proposalId, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.GetNotesAsync(proposalId, cancellationToken));
    public Task<ProposalNote> AddNoteAsync(int proposalId, string body, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.AddNoteAsync(proposalId, body, cancellationToken));
    public Task<IReadOnlyList<ProposalFile>> GetFilesAsync(int proposalId, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.GetFilesAsync(proposalId, cancellationToken));
    public Task<ProposalFileContent?> GetFileContentAsync(int fileId, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.GetFileContentAsync(fileId, cancellationToken));
    public Task<Proposal> ChangeStateAsync(int proposalId, string state, string implementedVersion, string note, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.ChangeStateAsync(proposalId, state, implementedVersion, note, cancellationToken));
    public Task DeleteAsync(int proposalId, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.DeleteAsync(proposalId, cancellationToken));
    public Task<ProposalExportResult> ExportAsync(IReadOnlyList<int> proposalIds, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.ExportAsync(proposalIds, cancellationToken));
    public Task MarkSeenAsync(int proposalId, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.MarkSeenAsync(proposalId, cancellationToken));
    public Task<IReadOnlyList<ProposalAlert>> GetAlertsAsync(CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.GetAlertsAsync(cancellationToken));
}

// The stand-in while the module is switched off or did not start: every call says the module is out (the alerts are simply empty).
internal sealed class UnavailableProposalService : IProposalService
{
    private static Task<T> Out<T>() => Task.FromException<T>(new ModuleUnavailableException(ProposalsModule.ModuleId));
    public Task<Proposal> CreateAsync(ProposalInput input, CancellationToken cancellationToken = default) => Out<Proposal>();
    public Task<IReadOnlyList<Proposal>> GetMineAsync(CancellationToken cancellationToken = default) => Out<IReadOnlyList<Proposal>>();
    public Task<IReadOnlyList<Proposal>> GetBoardAsync(string? text, bool onlyOpen, CancellationToken cancellationToken = default) => Out<IReadOnlyList<Proposal>>();
    public Task<Proposal?> GetAsync(int id, CancellationToken cancellationToken = default) => Out<Proposal?>();
    public Task<ProposalPage> QueryAsync(ProposalQuery query, int page, int pageSize, CancellationToken cancellationToken = default) => Out<ProposalPage>();
    public Task<IReadOnlyList<ProposalNote>> GetNotesAsync(int proposalId, CancellationToken cancellationToken = default) => Out<IReadOnlyList<ProposalNote>>();
    public Task<ProposalNote> AddNoteAsync(int proposalId, string body, CancellationToken cancellationToken = default) => Out<ProposalNote>();
    public Task<IReadOnlyList<ProposalFile>> GetFilesAsync(int proposalId, CancellationToken cancellationToken = default) => Out<IReadOnlyList<ProposalFile>>();
    public Task<ProposalFileContent?> GetFileContentAsync(int fileId, CancellationToken cancellationToken = default) => Out<ProposalFileContent?>();
    public Task<Proposal> ChangeStateAsync(int proposalId, string state, string implementedVersion, string note, CancellationToken cancellationToken = default) => Out<Proposal>();
    public Task DeleteAsync(int proposalId, CancellationToken cancellationToken = default) => Task.FromException(new ModuleUnavailableException(ProposalsModule.ModuleId));
    public Task<ProposalExportResult> ExportAsync(IReadOnlyList<int> proposalIds, CancellationToken cancellationToken = default) => Out<ProposalExportResult>();
    public Task MarkSeenAsync(int proposalId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<IReadOnlyList<ProposalAlert>> GetAlertsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ProposalAlert>>([]);
}
