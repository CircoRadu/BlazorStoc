using System.ComponentModel.DataAnnotations;

namespace BlazorStoc.Services;

public enum ServiceInterventionKind { Maintenance, OnDemand }

// How the next due date of a maintenance intervention was chosen: from the date it was performed, from the planned due date it closed,
// or set by the operator.
public enum ServiceNextDueBasis { FromPerformed, FromPlanned, Chosen }

// A row of the register (service_interventions). The work point name/address and the contract label are snapshots taken when it was
// recorded, so the register stays faithful when they are edited later. A maintenance intervention that moved the due date of its point
// keeps the due it closed (PlannedDue), how the next one was chosen (Basis) and the due it set (NextDueSet); one entered with a date
// older than the latest maintenance intervention of the point moves nothing and has all three empty. An on-demand intervention has no
// contract and never touches the due dates.
public sealed record ServiceIntervention(int Id, ServiceInterventionKind Kind, int BeneficiaryId, int WorkPointId, int? ContractId,
    string WorkPointName, string WorkPointAddress, string? ContractLabel, DateOnly PerformedOn, DateOnly? PlannedDue, ServiceNextDueBasis? Basis,
    DateOnly? NextDueSet, string Notes, string RecordedBy, DateTime RecordedUtc, long Version)
{
    public bool IsMaintenance => Kind == ServiceInterventionKind.Maintenance;
    /// <summary>Whether this intervention moved the due date of its point.</summary>
    public bool MovesDue => Basis is not null;
}

// One of the three ways to choose the next due date (enabled or not, with its date when it has one).
public sealed record ServiceDueOption(ServiceNextDueBasis Basis, DateOnly? Date, bool Enabled, string Label, string? Reason);

public sealed class ServiceInterventionInput : IValidatableObject
{
    public const int NotesMaximumLength = 2000;

    public ServiceInterventionKind Kind { get; set; } = ServiceInterventionKind.Maintenance;
    public int WorkPointId { get; set; }
    public DateOnly? PerformedOn { get; set; }

    [StringLength(NotesMaximumLength, ErrorMessage = "Observațiile pot avea cel mult 2000 de caractere.")]
    public string Notes { get; set; } = "";

    // Maintenance only: how the next due date is chosen (ChosenDue is used with Chosen).
    public ServiceNextDueBasis Basis { get; set; } = ServiceNextDueBasis.FromPerformed;
    public DateOnly? ChosenDue { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (WorkPointId <= 0) yield return new("Alege punctul de lucru.", [nameof(WorkPointId)]);
        if (PerformedOn is null) yield return new("Completează data efectuării.", [nameof(PerformedOn)]);
        else if (!ServiceContractRules.InRange(PerformedOn.Value)) yield return new("Data efectuării nu este validă.", [nameof(PerformedOn)]);
        if (ChosenDue is { } chosen && !ServiceContractRules.InRange(chosen)) yield return new("Scadența aleasă nu este o dată validă.", [nameof(ChosenDue)]);
    }

    public ServiceInterventionInput Validated()
    {
        var normalized = new ServiceInterventionInput
        {
            Kind = Kind, WorkPointId = WorkPointId, PerformedOn = PerformedOn, Basis = Basis, ChosenDue = ChosenDue,
            Notes = TextNormalization.ForStorage((Notes ?? "").Replace("\r\n", "\n"))
        };
        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(normalized, new ValidationContext(normalized), results, true))
            throw new ServiceInterventionOperationException(string.Join(" ", results.Select(result => result.ErrorMessage).Distinct()));
        return normalized;
    }

    public static ServiceInterventionInput From(ServiceIntervention value) => new()
    {
        Kind = value.Kind, WorkPointId = value.WorkPointId, PerformedOn = value.PerformedOn, Notes = value.Notes,
        Basis = value.Basis ?? ServiceNextDueBasis.FromPerformed, ChosenDue = value.Basis == ServiceNextDueBasis.Chosen ? value.NextDueSet : null
    };
}

public sealed class ServiceInterventionOperationException(string message) : Exception(message);

public sealed record ServiceInterventionSaved(ServiceIntervention Intervention, DateOnly? PreviousDue, DateOnly? NewDue);

public sealed record ServiceInterventionQuery(int? BeneficiaryId = null, string? Text = null, DateOnly? From = null, DateOnly? To = null,
    ServiceInterventionKind? Kind = null, int Page = 1, int PageSize = 25);

public sealed record ServiceInterventionRow(ServiceIntervention Intervention, string BeneficiaryName, int PhotoCount);

public sealed record ServiceInterventionPage(IReadOnlyList<ServiceInterventionRow> Items, int TotalCount);

public interface IServiceInterventionRepository
{
    /// <summary>Every intervention of the beneficiary, newest first.</summary>
    Task<IReadOnlyList<ServiceIntervention>> GetForBeneficiaryAsync(int beneficiaryId, CancellationToken cancellationToken = default);
    /// <summary>The register across beneficiaries, filtered (beneficiary, text in the beneficiary/work point/contract, period, kind), newest first.</summary>
    Task<ServiceInterventionPage> GetPageAsync(ServiceInterventionQuery query, CancellationToken cancellationToken = default);
    /// <summary>Records an intervention. Maintenance needs a work point under an active contract and moves its due date (unless a more
    /// recent maintenance intervention of the point exists); on demand takes any work point of the beneficiary and moves nothing.</summary>
    Task<ServiceInterventionSaved> RecordAsync(int beneficiaryId, ServiceInterventionInput input, CancellationToken cancellationToken = default);
    /// <summary>Notes always; the date and the next due of the latest due-moving maintenance intervention (the choice is reopened); the date of an on-demand one.</summary>
    Task<ServiceInterventionSaved> UpdateAsync(ServiceIntervention original, ServiceInterventionInput input, CancellationToken cancellationToken = default);
    /// <summary>Archived deletion with its photos. Deleting the latest due-moving maintenance intervention returns the due date to the one it closed.</summary>
    Task DeleteAsync(ServiceIntervention original, string reason, CancellationToken cancellationToken = default);
}

public static class ServiceInterventionRules
{
    public const string GeneratedEditReason = "Editare intervenție";

    public static char KindCode(ServiceInterventionKind kind) => kind == ServiceInterventionKind.Maintenance ? 'M' : 'C';
    public static ServiceInterventionKind ParseKind(string code) => code == "M" ? ServiceInterventionKind.Maintenance : ServiceInterventionKind.OnDemand;

    public static char BasisCode(ServiceNextDueBasis basis) => basis switch { ServiceNextDueBasis.FromPerformed => 'E', ServiceNextDueBasis.FromPlanned => 'P', _ => 'O' };
    public static ServiceNextDueBasis? ParseBasis(string? code) => code switch
    {
        "E" => ServiceNextDueBasis.FromPerformed, "P" => ServiceNextDueBasis.FromPlanned, "O" => ServiceNextDueBasis.Chosen, _ => null
    };

    public static string KindLabel(ServiceInterventionKind kind) => kind == ServiceInterventionKind.Maintenance ? "Mentenanță" : "La cerere";
    public static string KindText(ServiceInterventionKind kind) => kind == ServiceInterventionKind.Maintenance ? "de mentenanță" : "la cerere";

    public static string BasisLabel(ServiceNextDueBasis basis) => basis switch
    {
        ServiceNextDueBasis.FromPerformed => "din data efectuării",
        ServiceNextDueBasis.FromPlanned => "din data planificată",
        _ => "stabilită de operator"
    };

    /// <summary>The three ways to choose the next due date. Each of the first two is the given date plus the cycle and must fall after the
    /// date performed (the planned one does not when the visit came more than a cycle late); the third is typed by the operator.</summary>
    public static IReadOnlyList<ServiceDueOption> Options(DateOnly performedOn, DateOnly plannedDue, int cycleMonths)
    {
        var fromPerformed = performedOn.AddMonths(cycleMonths);
        var fromPlanned = plannedDue.AddMonths(cycleMonths);
        var plannedUsable = fromPlanned > performedOn;
        return
        [
            new(ServiceNextDueBasis.FromPerformed, fromPerformed, true, "Din data efectuării", null),
            new(ServiceNextDueBasis.FromPlanned, fromPlanned, plannedUsable, "Din data planificată",
                plannedUsable ? null : "Nu se poate alege: data planificată plus ciclicitatea cade înainte de data efectuării sau în aceeași zi (vizită făcută cu mai mult de un ciclu de întârziere)."),
            new(ServiceNextDueBasis.Chosen, null, true, "Stabilită de mine", null)
        ];
    }

    /// <summary>The due date the choice gives (null with the message in error when it cannot be used).</summary>
    public static DateOnly? ResolveDue(ServiceNextDueBasis basis, DateOnly performedOn, DateOnly plannedDue, int cycleMonths, DateOnly? chosen, out string? error)
    {
        error = null;
        DateOnly? due;
        if (basis == ServiceNextDueBasis.Chosen)
        {
            due = chosen;
            if (due is null) { error = "Completează scadența următoare."; return null; }
        }
        else due = basis == ServiceNextDueBasis.FromPerformed ? performedOn.AddMonths(cycleMonths) : plannedDue.AddMonths(cycleMonths);
        if (due <= performedOn)
        {
            error = basis == ServiceNextDueBasis.Chosen ? "Scadența următoare trebuie să fie după data efectuării."
                : "Scadența calculată din data planificată cade înainte de data efectuării sau în aceeași zi; alege altă variantă.";
            return null;
        }
        if (!ServiceContractRules.InRange(due.Value)) { error = "Scadența următoare nu este o dată validă."; return null; }
        return due;
    }

    public static string? PerformedOnError(DateOnly performedOn, DateOnly today) =>
        performedOn > today ? "Data efectuării nu poate fi în viitor." : null;

    /// <summary>The maintenance intervention that determines the due date of a point: the due-moving one with the latest date performed (then the newest).</summary>
    public static ServiceIntervention? LatestMoving(IEnumerable<ServiceIntervention> interventions, int workPointId) => interventions
        .Where(item => item.WorkPointId == workPointId && item.IsMaintenance && item.MovesDue)
        .OrderByDescending(item => item.PerformedOn).ThenByDescending(item => item.Id).FirstOrDefault();

    /// <summary>A new maintenance intervention moves the due date unless one with a later date performed already exists for the point.</summary>
    public static bool Moves(DateOnly performedOn, DateOnly? latestMovingPerformedOn) => latestMovingPerformedOn is null || performedOn >= latestMovingPerformedOn;

    public static ServiceInterventionOperationException Changed() =>
        new("Intervenția s-a schimbat între timp. Actualizează pagina și reia operația.");

    public static ServiceInterventionOperationException WorkPointNotOfBeneficiary() =>
        new("Punctul de lucru ales nu aparține beneficiarului sau nu mai există. Actualizează pagina.");

    public static ServiceInterventionOperationException NotCovered(string workPointName) =>
        new($"Punctul de lucru «{workPointName}» nu este acoperit de un contract activ; pentru el se poate înregistra numai o intervenție la cerere.");

    public static ServiceInterventionOperationException DueMovedMeanwhile() =>
        new("Scadența punctului a fost modificată între timp. Actualizează pagina și reia operația.");

    public static ServiceInterventionOperationException OnlyLatestCorrectable() =>
        new("Data și scadența se pot corecta numai la ultima intervenție de mentenanță a punctului; pentru celelalte se pot schimba doar observațiile.");

    public static ServiceInterventionOperationException BeforePrevious(DateOnly previous) =>
        new($"Data efectuării nu poate fi anterioară intervenției de mentenanță precedente a punctului ({StockMovementRules.DisplayDate(previous)}).");

    public static ServiceInterventionOperationException BeneficiaryMissing() =>
        new("Beneficiarul nu mai există. Actualizează lista.");

    public static ServiceContractOperationException ContractHasInterventions(int count) =>
        new(count == 1
            ? "Contractul are o intervenție de mentenanță și nu poate fi șters. Poți să-l dezactivezi (Off)."
            : $"Contractul are {count} intervenții de mentenanță și nu poate fi șters. Poți să-l dezactivezi (Off).");

    public static WorkPointOperationException WorkPointHasInterventions(int count) =>
        new(count == 1
            ? "Punctul de lucru are o intervenție în registru și nu poate fi șters."
            : $"Punctul de lucru are {count} intervenții în registru și nu poate fi șters.");

    public static BeneficiaryOperationException BeneficiaryHasInterventions(int count) =>
        new(count == 1
            ? "Beneficiarul are o intervenție în registru și nu poate fi șters."
            : $"Beneficiarul are {count} intervenții în registru și nu poate fi șters.");

    public static string Target(ServiceIntervention value, string beneficiaryName) =>
        $"#{value.BeneficiaryId} · {beneficiaryName} · intervenție {KindText(value.Kind)} «{value.WorkPointName}» {StockMovementRules.DisplayDate(value.PerformedOn)}";

    public static string DueText(DateOnly? value) => value is { } date ? StockMovementRules.DisplayDate(date) : "nemodificată";

    public static string Identification(ServiceIntervention value) => AuditDetails.Identification(new (string Field, string Value)[]
    {
        ("Fel", KindLabel(value.Kind)), ("Punct de lucru", value.WorkPointName), ("Adresă", value.WorkPointAddress), ("Contract", value.ContractLabel ?? string.Empty),
        ("Efectuată la", StockMovementRules.DisplayDate(value.PerformedOn)), ("Observații", value.Notes)
    }.Where(item => item.Value.Length > 0).ToArray());

    public static IReadOnlyList<AuditChange> Changes(ServiceIntervention before, ServiceIntervention after) =>
    [
        new("Efectuată la", StockMovementRules.DisplayDate(before.PerformedOn), StockMovementRules.DisplayDate(after.PerformedOn)),
        new("Varianta scadenței", before.Basis is { } oldBasis ? BasisLabel(oldBasis) : "-", after.Basis is { } newBasis ? BasisLabel(newBasis) : "-"),
        new("Scadența stabilită", DueText(before.NextDueSet), DueText(after.NextDueSet)),
        new("Observații", before.Notes, after.Notes)
    ];

    public static void CheckCurrent(ServiceIntervention? current, ServiceIntervention original)
    {
        if (current is null || current != original) throw Changed();
    }
}
