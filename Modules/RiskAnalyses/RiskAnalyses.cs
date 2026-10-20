using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace BlazorStoc.Services;

// A security risk analysis of a work point (risk_analyses, migration 39). Number is the registration number (a revision keeps it; a new
// analysis has a new one). InitialDate is the date it was first registered and never changes by renewing; LastRenewalDate starts equal to it and
// follows the newest renewal. The expiry date is derived (LastRenewalDate + ValidityMonths), never stored. IsActive is the On/Off switch: a work
// point has at most one active analysis and any number of Off ones (its previous analyses).
public sealed record RiskAnalysis(int Id, int BeneficiaryId, int WorkPointId, string Number, string Author, DateOnly InitialDate,
    DateOnly LastRenewalDate, int ValidityMonths, bool IsActive, string Notes, long Version)
{
    public DateOnly ExpiryDate => RiskAnalysisRules.ExpiryDate(LastRenewalDate, ValidityMonths);
}

// One renewal of an analysis (risk_analysis_renewals): the date it was renewed on and the previous last-renewal date it replaced.
public sealed record RiskAnalysisRenewal(int Id, int RiskAnalysisId, DateOnly RenewalDate, DateOnly PreviousRenewalDate, string RecordedBy,
    DateTime RecordedUtc, string Notes);

// An analysis with the names it is shown with and its renewals (newest first).
public sealed record RiskAnalysisView(RiskAnalysis Analysis, string BeneficiaryName, string WorkPointName, string WorkPointAddress,
    bool WorkPointIsPrimary, IReadOnlyList<RiskAnalysisRenewal> Renewals);

public sealed class RiskAnalysisOperationException(string message) : Exception(message);

public sealed class RiskAnalysisInput : IValidatableObject
{
    public const int NotesMaximumLength = 1000;

    public int WorkPointId { get; set; }

    [StringLength(RiskAnalysisRules.NumberMaximumLength, ErrorMessage = "Numărul de înregistrare poate avea cel mult 30 de caractere.")]
    public string Number { get; set; } = "";

    [StringLength(RiskAnalysisRules.AuthorMaximumLength, ErrorMessage = "Numele persoanei care a întocmit analiza poate avea cel mult 120 de caractere.")]
    public string Author { get; set; } = "";

    public DateOnly? InitialDate { get; set; }
    public int ValidityMonths { get; set; } = RiskAnalysisRules.DefaultValidityMonths;

    [StringLength(NotesMaximumLength, ErrorMessage = "Observațiile pot avea cel mult 1000 de caractere.")]
    public string Notes { get; set; } = "";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (WorkPointId <= 0) yield return new("Alege punctul de lucru.", [nameof(WorkPointId)]);
        if (TextNormalization.ForObjectNameOrCode(Number).Length == 0) yield return new("Completează numărul de înregistrare.", [nameof(Number)]);
        if (TextNormalization.ForObjectNameOrCode(Author).Length == 0) yield return new("Completează numele persoanei care a întocmit analiza.", [nameof(Author)]);
        if (InitialDate is not { } initial) yield return new("Completează data înregistrării.", [nameof(InitialDate)]);
        else if (!ServiceContractRules.InRange(initial)) yield return new("Data înregistrării nu este o dată validă.", [nameof(InitialDate)]);
        if (ValidityMonths is < RiskAnalysisRules.MinimumValidityMonths or > RiskAnalysisRules.MaximumValidityMonths)
            yield return new($"Valabilitatea trebuie să fie între {RiskAnalysisRules.MinimumValidityMonths} și {RiskAnalysisRules.MaximumValidityMonths} de luni.", [nameof(ValidityMonths)]);
    }

    /// <summary>A normalized copy (number in capitals, text trimmed); throws the validation messages when the input is invalid.</summary>
    public RiskAnalysisInput Validated()
    {
        var normalized = new RiskAnalysisInput
        {
            WorkPointId = WorkPointId,
            Number = TextNormalization.ForObjectNameOrCode(Number).ToUpperInvariant(),
            Author = TextNormalization.ForObjectNameOrCode(Author),
            InitialDate = InitialDate,
            ValidityMonths = ValidityMonths,
            Notes = TextNormalization.ForStorage((Notes ?? "").Replace("\r\n", "\n"))
        };
        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(normalized, new ValidationContext(normalized), results, true))
            throw new RiskAnalysisOperationException(string.Join(" ", results.Select(result => result.ErrorMessage).Distinct()));
        return normalized;
    }

    public static RiskAnalysisInput From(RiskAnalysis value) => new()
    {
        WorkPointId = value.WorkPointId, Number = value.Number, Author = value.Author, InitialDate = value.InitialDate,
        ValidityMonths = value.ValidityMonths, Notes = value.Notes
    };
}

public sealed class RiskAnalysisRenewalInput
{
    public DateOnly? Date { get; set; }

    [StringLength(RiskAnalysisInput.NotesMaximumLength, ErrorMessage = "Observațiile pot avea cel mult 1000 de caractere.")]
    public string Notes { get; set; } = "";
}

// What the analysis list of a beneficiary / the global page need from the store.
public interface IRiskAnalysisRepository
{
    /// <summary>Every analysis of the beneficiary (On and Off) with its renewals, newest registration first.</summary>
    Task<IReadOnlyList<RiskAnalysisView>> GetForBeneficiaryAsync(int beneficiaryId, CancellationToken cancellationToken = default);
    /// <summary>Every analysis of every beneficiary, earliest expiry first; the Off ones only with includeOff.</summary>
    Task<IReadOnlyList<RiskAnalysisView>> GetAllAsync(bool includeOff, CancellationToken cancellationToken = default);
    /// <summary>A new analysis is On and its last renewal is the registration date. When the work point already has an active analysis,
    /// it is refused unless deactivateExisting is set (the old one then becomes Off in the same transaction).</summary>
    Task<RiskAnalysisView> CreateAsync(int beneficiaryId, RiskAnalysisInput input, bool deactivateExisting, CancellationToken cancellationToken = default);
    /// <summary>Changes number, author, validity, notes and (while there is no renewal) the registration date; the work point stays.</summary>
    Task<RiskAnalysisView> UpdateAsync(RiskAnalysis original, RiskAnalysisInput input, CancellationToken cancellationToken = default);
    /// <summary>Records a renewal (a revision under the same number): the date becomes the last renewal and the expiry moves.</summary>
    Task<RiskAnalysisView> RenewAsync(RiskAnalysis original, RiskAnalysisRenewalInput input, CancellationToken cancellationToken = default);
    /// <summary>Takes back the newest renewal (an entry mistake): the previous last-renewal date is restored.</summary>
    Task<RiskAnalysisView> UndoLastRenewalAsync(RiskAnalysis original, CancellationToken cancellationToken = default);
    /// <summary>Off to On; refused while another analysis of the work point is active.</summary>
    Task<RiskAnalysisView> ActivateAsync(RiskAnalysis original, CancellationToken cancellationToken = default);
    Task<RiskAnalysisView> DeactivateAsync(RiskAnalysis original, CancellationToken cancellationToken = default);
    /// <summary>Archived deletion of the analysis with its renewals.</summary>
    Task DeleteAsync(RiskAnalysis original, string reason, CancellationToken cancellationToken = default);
}

public enum RiskAnalysisState { Valid, ExpiresSoon, Expired, Off }

public static class RiskAnalysisRules
{
    public const int NumberMaximumLength = 30;
    public const int AuthorMaximumLength = 120;
    public const int DefaultValidityMonths = 36;
    public const int MinimumValidityMonths = 1;
    public const int MaximumValidityMonths = 120;
    // The days before the expiry within which the state is "Expira curand" when the notification template of the source has no other value.
    public const int DefaultThresholdDays = 60;
    public const string GeneratedEditReason = "Editare analiză de risc";
    public const string GeneratedRenewalReason = "Reînnoire analiză de risc";
    public const string GeneratedUndoReason = "Anulare reînnoire analiză de risc";
    public const string GeneratedActivationReason = "Activare analiză de risc";
    public const string GeneratedDeactivationReason = "Dezactivare analiză de risc";
    public const string GeneratedReplaceReason = "Analiză de risc nouă pentru același punct de lucru";

    // The expiry: the last renewal plus the validity (AddMonths cuts to the end of a shorter month: 31.01 + 1 month = 28/29.02).
    public static DateOnly ExpiryDate(DateOnly lastRenewal, int validityMonths) => lastRenewal.AddMonths(validityMonths);

    public static RiskAnalysisState State(RiskAnalysis analysis, DateOnly today, int thresholdDays = DefaultThresholdDays)
    {
        if (!analysis.IsActive) return RiskAnalysisState.Off;
        var expiry = analysis.ExpiryDate;
        return expiry < today ? RiskAnalysisState.Expired
            : expiry.DayNumber - today.DayNumber <= thresholdDays ? RiskAnalysisState.ExpiresSoon
            : RiskAnalysisState.Valid;
    }

    public static string StateLabel(RiskAnalysisState state) => state switch
    {
        RiskAnalysisState.Expired => "Expirată",
        RiskAnalysisState.ExpiresSoon => "Expiră curând",
        RiskAnalysisState.Off => "Off",
        _ => "Valabilă"
    };

    public static string StateCssClass(RiskAnalysisState state) => state switch
    {
        RiskAnalysisState.Expired => "due-chip due-overdue",
        RiskAnalysisState.ExpiresSoon => "due-chip due-soon",
        RiskAnalysisState.Off => "contract-state off",
        _ => "due-chip due-ok"
    };

    // "Expira in 20 de zile" / "Expirata de 3 zile": the text next to the expiry date.
    public static string ExpiryText(RiskAnalysis analysis, DateOnly today)
    {
        var days = analysis.ExpiryDate.DayNumber - today.DayNumber;
        return days < 0 ? (days == -1 ? "Expirată de o zi" : $"Expirată de {-days} zile")
            : days == 0 ? "Expiră astăzi" : days == 1 ? "Expiră mâine" : $"Expiră în {days} zile";
    }

    public static string ValidityText(int months) => months == 1 ? "1 lună" : $"{months.ToString(CultureInfo.InvariantCulture)} luni";

    // A renewal must be later than the current last renewal and not in the future.
    public static string? RenewalDateError(DateOnly? date, DateOnly lastRenewal, DateOnly today)
    {
        if (date is not { } value) return "Completează data reînnoirii.";
        if (!ServiceContractRules.InRange(value)) return "Data reînnoirii nu este o dată validă.";
        if (value <= lastRenewal) return $"Data reînnoirii trebuie să fie după ultima reînnoire ({StockMovementRules.DisplayDate(lastRenewal)}).";
        if (value > today) return "Data reînnoirii nu poate fi în viitor.";
        return null;
    }

    // The registration date cannot be in the future.
    public static string? InitialDateError(DateOnly date, DateOnly today) => date > today ? "Data înregistrării nu poate fi în viitor." : null;

    public static RiskAnalysisOperationException Changed() =>
        new("Analiza de risc s-a schimbat între timp. Actualizează pagina și reia operația.");

    public static RiskAnalysisOperationException DuplicateNumber(string number) =>
        new($"Beneficiarul are deja o analiză de risc cu numărul de înregistrare {number}. Pentru o revizie a analizei existente folosește «Reînnoiește».");

    public static RiskAnalysisOperationException ActiveExists(string workPointName, string number) =>
        new($"Punctul de lucru «{workPointName}» are deja o analiză de risc activă (nr. {number}). Dezactiveaz-o mai întâi sau alege dezactivarea ei la salvare.");

    public static RiskAnalysisOperationException WorkPointNotOfBeneficiary() =>
        new("Punctul de lucru ales nu aparține beneficiarului sau nu mai există. Actualizează pagina.");

    public static RiskAnalysisOperationException BeneficiaryMissing() =>
        new("Beneficiarul nu mai există. Actualizează lista.");

    public static RiskAnalysisOperationException InitialDateLocked() =>
        new("Data înregistrării nu se mai poate modifica după ce analiza a fost reînnoită. Anulează mai întâi reînnoirile.");

    public static RiskAnalysisOperationException NoRenewal() =>
        new("Analiza nu are nicio reînnoire de anulat.");

    public static WorkPointOperationException WorkPointHasAnalysis(string number) =>
        new($"Punctul de lucru are analiza de risc nr. {number} și nu poate fi șters. Șterge mai întâi analiza de risc.");

    public static BeneficiaryOperationException BeneficiaryHasAnalyses(int count) =>
        new(count == 1
            ? "Beneficiarul are o analiză de risc și nu poate fi șters. Șterge mai întâi analiza."
            : $"Beneficiarul are {count} analize de risc și nu poate fi șters. Șterge mai întâi analizele.");

    public static string Target(int beneficiaryId, string beneficiaryName, string number) =>
        $"#{beneficiaryId} · {beneficiaryName} · analiză de risc «{number}»";

    public static string Identification(RiskAnalysis value, string workPointName) => AuditDetails.Identification(new (string Field, string Value)[]
    {
        ("Punct de lucru", workPointName), ("Număr înregistrare", value.Number), ("Întocmită de", value.Author),
        ("Data înregistrării", StockMovementRules.DisplayDate(value.InitialDate)), ("Valabilitate", ValidityText(value.ValidityMonths)),
        ("Expiră", StockMovementRules.DisplayDate(value.ExpiryDate)), ("Observații", value.Notes)
    }.Where(item => item.Value.Length > 0).ToArray());

    public static IReadOnlyList<AuditChange> Changes(RiskAnalysis before, RiskAnalysis after) =>
    [
        new("Număr înregistrare", before.Number, after.Number), new("Întocmită de", before.Author, after.Author),
        new("Data înregistrării", StockMovementRules.DisplayDate(before.InitialDate), StockMovementRules.DisplayDate(after.InitialDate)),
        new("Valabilitate", ValidityText(before.ValidityMonths), ValidityText(after.ValidityMonths)),
        new("Expiră", StockMovementRules.DisplayDate(before.ExpiryDate), StockMovementRules.DisplayDate(after.ExpiryDate)),
        new("Observații", before.Notes, after.Notes)
    ];

    // The journal names the exact operation: an edit that changes only the validity has its own name.
    public static string EditAction(RiskAnalysis before, RiskAnalysis after)
    {
        var validityChanged = before.ValidityMonths != after.ValidityMonths;
        var othersChanged = before.Number != after.Number || before.Author != after.Author || before.InitialDate != after.InitialDate || before.Notes != after.Notes;
        return validityChanged && !othersChanged ? AuditActions.EditRiskAnalysisValidity : AuditActions.EditRiskAnalysis;
    }

    public static void CheckCurrent(RiskAnalysis? current, RiskAnalysis original)
    {
        if (current is null || current != original) throw Changed();
    }
}
