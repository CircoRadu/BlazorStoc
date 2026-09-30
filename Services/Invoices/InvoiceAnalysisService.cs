namespace BlazorStoc.Services;

// One uploaded file under analysis. The file itself is never written to disk: the words and the page pictures stay in memory for the time
// the template is being made, and go away when the template is saved or abandoned, or when the session expires.
public sealed class InvoiceAnalysisSession
{
    public required Guid Id { get; init; }
    public required string Owner { get; init; }
    public required string FileName { get; init; }
    public required InvoiceDocument Document { get; init; }
    public required IReadOnlyList<byte[]> Previews { get; init; }
    public required InvoiceAnalysis Analysis { get; init; }
    public DateTime LastUsedUtc { get; set; } = DateTime.UtcNow;
}

public interface IInvoiceAnalysisStore
{
    InvoiceAnalysisSession Add(string owner, string fileName, InvoiceReadResult read, InvoiceAnalysis analysis);
    InvoiceAnalysisSession? Get(Guid id, string owner);
    void Remove(Guid id);
    int Count { get; }
}

public sealed class InvoiceAnalysisStore(TimeProvider clock) : IInvoiceAnalysisStore
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);
    public const int MaxSessionsPerOwner = 3;
    private readonly object gate = new();
    private readonly Dictionary<Guid, InvoiceAnalysisSession> sessions = [];

    public int Count { get { lock (gate) { Purge(); return sessions.Count; } } }

    public InvoiceAnalysisSession Add(string owner, string fileName, InvoiceReadResult read, InvoiceAnalysis analysis)
    {
        var session = new InvoiceAnalysisSession
        {
            Id = Guid.NewGuid(), Owner = owner, FileName = fileName, Document = read.Document, Previews = read.PagePreviews, Analysis = analysis,
            LastUsedUtc = clock.GetUtcNow().UtcDateTime
        };
        lock (gate)
        {
            Purge();
            // The oldest sessions of the same user are dropped so that an abandoned upload does not sit in memory.
            foreach (var old in sessions.Values.Where(item => item.Owner == owner).OrderByDescending(item => item.LastUsedUtc).Skip(MaxSessionsPerOwner - 1).ToList())
                sessions.Remove(old.Id);
            sessions[session.Id] = session;
        }
        return session;
    }

    public InvoiceAnalysisSession? Get(Guid id, string owner)
    {
        lock (gate)
        {
            Purge();
            if (!sessions.TryGetValue(id, out var session) || session.Owner != owner) return null;
            session.LastUsedUtc = clock.GetUtcNow().UtcDateTime;
            return session;
        }
    }

    public void Remove(Guid id)
    {
        lock (gate) sessions.Remove(id);
    }

    private void Purge()
    {
        var limit = clock.GetUtcNow().UtcDateTime - Lifetime;
        foreach (var expired in sessions.Values.Where(item => item.LastUsedUtc < limit).ToList()) sessions.Remove(expired.Id);
    }
}

public interface IInvoiceAnalysisService
{
    Task<InvoiceAnalysisSession> AnalyzeAsync(Stream pdf, string fileName, CancellationToken cancellationToken = default);
    InvoiceAnalysisSession? Get(Guid id);
    void Discard(Guid id);
}

// Reads an uploaded PDF, proposes a template for it and keeps the session for its owner (the signed-in administrator).
public sealed class InvoiceAnalysisService(IInvoicePdfReader reader, IInvoiceAnalysisStore store, IAccessControl access) : IInvoiceAnalysisService
{
    private string? owner;

    public async Task<InvoiceAnalysisSession> AnalyzeAsync(Stream pdf, string fileName, CancellationToken cancellationToken = default)
    {
        await access.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        owner = await access.GetUsernameAsync(cancellationToken).ConfigureAwait(false) ?? "necunoscut";
        var read = await reader.ReadAsync(pdf, cancellationToken).ConfigureAwait(false);
        var analysis = InvoiceAnalyzer.Analyze(read.Document);
        return store.Add(owner, fileName, read, analysis);
    }

    public InvoiceAnalysisSession? Get(Guid id) => owner is null ? null : store.Get(id, owner);

    public void Discard(Guid id)
    {
        if (owner is not null && store.Get(id, owner) is not null) store.Remove(id);
    }
}

// The saved templates that fit an analysed file, best first: those of the supplier whose tax id is in the file, then the ones whose
// layout (the words they were made of) is found in it.
public sealed record InvoiceTemplateSuggestion(InvoiceTemplateRecord Template, InvoiceTemplateMatch Match);

public static class InvoiceTemplateSuggestions
{
    public const double MinLayoutScore = 0.5;

    public static IReadOnlyList<InvoiceTemplateSuggestion> Rank(IEnumerable<InvoiceTemplateRecord> templates, InvoiceDocument document) =>
        templates.Select(template => new InvoiceTemplateSuggestion(template, InvoiceTemplateEngine.Match(template.Definition, template.Info.SupplierCui, document)))
            .Where(item => item.Match.SupplierMatch || item.Match.Score >= MinLayoutScore)
            .OrderByDescending(item => item.Match.SupplierMatch).ThenByDescending(item => item.Match.Score).ThenBy(item => item.Template.Info.Name).ToList();
}
