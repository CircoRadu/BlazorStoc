namespace BlazorStoc.Services;

// The exit form of a product (Intrări/ieșiri) survives a trip to "Adaugă beneficiar" / "Adaugă proiect": the page stores
// the typed values here, navigates to the add form and, when the user comes back (saved or cancelled), takes them again.
// One scoped instance per session (circuit), like UnsavedChanges: it lives as long as the user's Blazor connection, so a
// reloaded browser tab loses it and the page then says the form could not be restored.
public sealed record ExitFormSnapshot(StockMovementInput Form, bool UseProject, string? Suggestion, int? NewBeneficiaryId, int? NewProjectId);

public sealed class ExitFormDraft
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);
    private readonly object gate = new();
    private int? productId;
    private ExitFormSnapshot? snapshot;
    private DateTime storedUtc;

    // Replaceable clock (tests).
    public Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

    public void Store(int forProductId, StockMovementInput form, bool useProject, string? suggestion)
    {
        lock (gate)
        {
            productId = forProductId;
            snapshot = new ExitFormSnapshot(form.Clone(), useProject, suggestion, null, null);
            storedUtc = UtcNow();
        }
    }

    public bool HasDraftFor(int forProductId)
    {
        lock (gate) return Current(forProductId) is not null;
    }

    // The beneficiary / project just created by the user in the add form; selected when the exit form is restored.
    public void SetNewBeneficiary(int id)
    {
        lock (gate) if (snapshot is not null) snapshot = snapshot with { NewBeneficiaryId = id, NewProjectId = null };
    }

    public void SetNewProject(int id)
    {
        lock (gate) if (snapshot is not null) snapshot = snapshot with { NewProjectId = id };
    }

    // Returns the stored form for the product (once) and forgets it; null when there is none, it belongs to another
    // product or it is older than the lifetime.
    public ExitFormSnapshot? Take(int forProductId)
    {
        lock (gate)
        {
            var current = Current(forProductId);
            productId = null; snapshot = null;
            return current is null ? null : current with { Form = current.Form.Clone() };
        }
    }

    public void Clear()
    {
        lock (gate) { productId = null; snapshot = null; }
    }

    private ExitFormSnapshot? Current(int forProductId) =>
        snapshot is not null && productId == forProductId && UtcNow() - storedUtc <= Lifetime ? snapshot : null;
}

// One selectable entry of the combobox (Id = the identifier reported back; Search = extra text that also matches).
public sealed record SelectOption(int Id, string Text, string Search = "");

public static class SearchableSelectRules
{
    // Case- and diacritic-insensitive "contains" on the text and the extra search text; an empty query keeps every option.
    public static IReadOnlyList<SelectOption> Filter(IReadOnlyList<SelectOption> options, string? query)
    {
        var key = TextNormalization.UniquenessKey(query);
        if (key.Length == 0) return options;
        return options.Where(option => TextNormalization.UniquenessKey($"{option.Text} {option.Search}").Contains(key, StringComparison.Ordinal)).ToArray();
    }
}
