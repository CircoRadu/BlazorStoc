using System.Globalization;
using BlazorStoc.Services;

namespace BlazorStoc.Modules.Proposals;

// Propuneri de modificari si functionalitati noi: every signed-in user proposes a change or a new feature from the menu entry under "Raporteaza o eroare" (kind, the page it is about,
// title, what is wanted and why, the importance for the author, pictures); every signed-in user sees the proposals and their answers, the author and the administrator can answer.
// The administrator (Setari -> Dezvoltare -> Propuneri) follows them, changes their state and exports them as a ZIP the browser downloads. Nothing is sent anywhere.

public static class ProposalRules
{
    public const string KindChange = "Modificare", KindFeature = "Funcționalitate nouă", KindOther = "Altceva";
    public static readonly IReadOnlyList<string> Kinds = [KindChange, KindFeature, KindOther];

    public const string ImportanceHigh = "Mare", ImportanceMedium = "Medie", ImportanceLow = "Mică";
    public static readonly IReadOnlyList<string> Importances = [ImportanceHigh, ImportanceMedium, ImportanceLow];

    public const string StateNew = "Nouă", StateAnalysis = "În analiză", StateAccepted = "Acceptată", StatePlanned = "Planificată", StateImplemented = "Implementată", StatePostponed = "Amânată", StateRejected = "Respinsă";
    public static readonly IReadOnlyList<string> States = [StateNew, StateAnalysis, StateAccepted, StatePlanned, StateImplemented, StatePostponed, StateRejected];
    /// <summary>The states that close a proposal (the second tab of the administrator view).</summary>
    public static readonly IReadOnlyList<string> ClosedStates = [StateImplemented, StatePostponed, StateRejected];
    public static bool IsClosed(string state) => ClosedStates.Contains(state);

    public const int MaxTitle = 200, MaxDescription = 8000, MaxBenefit = 4000, MaxNote = 4000, MaxVersion = 40;

    public static string Number(int id) => "PROP-" + id.ToString("0000", CultureInfo.InvariantCulture);

    public static string Display(DateTime utc) => utc.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
}

public sealed record Proposal(int Id, string Number, string Kind, string AreaRoute, string AreaTitle, string Title, string Description, string Benefit, string WantedImportance, string State,
    string UserName, string UserRole, string AppVersion, string ImplementedVersion, DateTime CreatedUtc, string UpdatedBy, DateTime? UpdatedUtc, DateTime? DecidedUtc, DateTime? AuthorSeenUtc,
    int FileCount, int NoteCount);

public sealed record ProposalNote(int Id, int ProposalId, string Author, string AuthorRole, string Body, DateTime CreatedUtc);

public sealed record ProposalFile(int Id, int ProposalId, string Kind, string FileName, string ContentType, int SizeBytes, DateTime CreatedUtc);

public sealed record ProposalFileContent(byte[] Content, string ContentType, string FileName);

public sealed record ProposalInput(string Kind, string AreaRoute, string AreaTitle, string Title, string Description, string Benefit, string WantedImportance, IReadOnlyList<ReportImage> Images);

public sealed record ProposalQuery(string? State = null, string? Kind = null, string? Importance = null, string? User = null, DateOnly? From = null, DateOnly? To = null, bool? Closed = null, string? Text = null);

public sealed record ProposalPage(IReadOnlyList<Proposal> Items, int Total);

public sealed record ProposalExportResult(string FileName, byte[] Content, int Proposals);

/// <summary>What needs the attention of a user about proposals: a new proposal (administrator), or a change of state / a new answer on one's own proposal.</summary>
public sealed record ProposalAlert(Proposal Proposal, string Reason);

/// <summary>An answer of the module for the user (a missing title, a picture that is not one), not a failure of the module.</summary>
public sealed class ProposalException(string message) : Exception(message), IExpectedAnswer;

public interface IProposalService
{
    /// <summary>Any signed-in user. Saves the proposal with its pictures and records it in the journal.</summary>
    Task<Proposal> CreateAsync(ProposalInput input, CancellationToken cancellationToken = default);
    /// <summary>The proposals of the signed-in user, the newest first.</summary>
    Task<IReadOnlyList<Proposal>> GetMineAsync(CancellationToken cancellationToken = default);
    /// <summary>Any signed-in user: every proposal (text only), the newest first; <paramref name="text"/> filters by title and description (also used to show similar proposals while writing).</summary>
    Task<IReadOnlyList<Proposal>> GetBoardAsync(string? text, bool onlyOpen, CancellationToken cancellationToken = default);
    Task<Proposal?> GetAsync(int id, CancellationToken cancellationToken = default);
    /// <summary>Administrator (setari-propuneri.view): the proposals that match the filters, the newest first.</summary>
    Task<ProposalPage> QueryAsync(ProposalQuery query, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProposalNote>> GetNotesAsync(int proposalId, CancellationToken cancellationToken = default);
    /// <summary>The author of the proposal and the administrator (setari-propuneri.edit) add an answer.</summary>
    Task<ProposalNote> AddNoteAsync(int proposalId, string body, CancellationToken cancellationToken = default);
    /// <summary>The pictures of a proposal; the author and the administrator only.</summary>
    Task<IReadOnlyList<ProposalFile>> GetFilesAsync(int proposalId, CancellationToken cancellationToken = default);
    Task<ProposalFileContent?> GetFileContentAsync(int fileId, CancellationToken cancellationToken = default);
    /// <summary>Administrator (setari-propuneri.edit). A note, when given, is added to the thread.</summary>
    Task<Proposal> ChangeStateAsync(int proposalId, string state, string implementedVersion, string note, CancellationToken cancellationToken = default);
    /// <summary>Administrator (setari-propuneri.edit): corrects the title, the description and the reason of a proposal and adds more pictures (the ones already there stay). Journaled as its own action.</summary>
    Task<Proposal> UpdateContentAsync(int proposalId, string title, string description, string benefit, IReadOnlyList<ReportImage> addedImages, CancellationToken cancellationToken = default);
    Task DeleteAsync(int proposalId, CancellationToken cancellationToken = default);
    /// <summary>Administrator (setari-propuneri.edit): deletes every closed proposal (Implementată, Amânată, Respinsă) with answers and pictures; returns how many. One journal line.</summary>
    Task<int> DeleteClosedAsync(CancellationToken cancellationToken = default);
    /// <summary>Administrator (setari-propuneri.export): the proposals as a ZIP (a folder for each, an index.csv when there are several).</summary>
    Task<ProposalExportResult> ExportAsync(IReadOnlyList<int> proposalIds, CancellationToken cancellationToken = default);
    /// <summary>The author opens one's own proposal: what changed on it is no longer announced.</summary>
    Task MarkSeenAsync(int proposalId, CancellationToken cancellationToken = default);
    /// <summary>The alerts of the signed-in user (the tab "Propuneri" of Notificari): new proposals for the administrator, changes and answers on one's own proposals for the author.</summary>
    Task<IReadOnlyList<ProposalAlert>> GetAlertsAsync(CancellationToken cancellationToken = default);
    /// <summary>How many proposals are not closed (the badge of the tab Propuneri of Setari -> Dezvoltare); 0 without the right to see the proposals of everybody.</summary>
    Task<int> CountOpenAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
}
