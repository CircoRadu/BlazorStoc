using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.RegularExpressions;

namespace BlazorStoc.Services;

// A maintenance contract of a beneficiary (service_contracts). The number is kept structured (number + date) and shown as
// "26/23.09.2025". IsActive is the On/Off switch: Off keeps everything (coverage, due dates, history) but the points of the contract
// stop producing due dates. ValidUntil (optional) only drives the "Expirat" chip and, later, the expiry notification.
public sealed record ServiceContract(int Id, int BeneficiaryId, string Number, DateOnly Date, int CycleMonths,
    DateOnly? ValidUntil, bool IsActive, string Notes, long Version)
{
    public string Label => ServiceContractNumber.Format(Number, Date);
}

// The coverage of a work point by a contract (service_contract_points). CycleMonths null = inherits the cycle of the contract;
// NextDue is the current due date of the next maintenance visit (stored, the single field that tells when it is due).
public sealed record ServiceContractPoint(int Id, int ContractId, int WorkPointId, int? CycleMonths, DateOnly NextDue, long Version);

public sealed record ServiceContractPointView(ServiceContractPoint Point, string WorkPointName, string WorkPointAddress, bool WorkPointIsPrimary);

public sealed record ServiceContractDetails(ServiceContract Contract, IReadOnlyList<ServiceContractPointView> Points)
{
    /// <summary>The earliest due date among the covered points (null when the contract covers no point).</summary>
    public DateOnly? NextDue => Points.Count == 0 ? null : Points.Min(point => point.Point.NextDue);

    public int EffectiveCycle(ServiceContractPoint point) => ServiceDueRules.EffectiveCycle(point.CycleMonths, Contract.CycleMonths);
}

// A work point of the beneficiary that is covered by another ACTIVE contract (a point is in at most one active contract).
public sealed record ServiceContractConflict(int WorkPointId, string WorkPointName, int ContractId, string ContractLabel);

// What the reactivation dialog needs: the points with their current due dates and the ones now taken by another active contract.
public sealed record ServiceContractActivationPlan(ServiceContractDetails Details, IReadOnlyList<ServiceContractConflict> Conflicts);

// One work point of a contract in the form. NextDue is the first visit (a new point) or the current due date (a point already
// covered); MoveFromOtherContract takes the point from the active contract that covers it (its due date and individual cycle are kept).
public sealed class ServiceContractPointInput
{
    public int WorkPointId { get; set; }
    public DateOnly? NextDue { get; set; }
    public int? CycleMonths { get; set; }
    public bool MoveFromOtherContract { get; set; }
}

public sealed class ServiceContractInput : IValidatableObject
{
    public const int NotesMaximumLength = 1000;

    // "26/23.09.2025", "26 / 23.09.2025" or "26 din 23.09.2025" (see ServiceContractNumber.TryParse).
    [StringLength(60, ErrorMessage = "Numărul și data contractului sunt prea lungi.")]
    public string NumberText { get; set; } = "";

    public DateOnly? ValidUntil { get; set; }
    public int CycleMonths { get; set; } = 3;

    [StringLength(NotesMaximumLength, ErrorMessage = "Observațiile pot avea cel mult 1000 de caractere.")]
    public string Notes { get; set; } = "";

    public List<ServiceContractPointInput> Points { get; set; } = [];

    // Filled by Validated().
    public string Number { get; set; } = "";
    public DateOnly Date { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        DateOnly? date = null;
        if (!ServiceContractNumber.TryParse(NumberText, out _, out var parsedDate, out var numberError))
            yield return new(numberError!, [nameof(NumberText)]);
        else date = parsedDate;
        if (CycleMonths is < ServiceContractRules.MinimumCycle or > ServiceContractRules.MaximumCycle)
            yield return new($"Ciclicitatea trebuie să fie între {ServiceContractRules.MinimumCycle} și {ServiceContractRules.MaximumCycle} luni.", [nameof(CycleMonths)]);
        if (ValidUntil is { } validUntil)
        {
            if (!ServiceContractRules.InRange(validUntil)) yield return new("Data de expirare nu este validă.", [nameof(ValidUntil)]);
            else if (date is { } contractDate && validUntil < contractDate)
                yield return new("Data de expirare nu poate fi anterioară datei contractului.", [nameof(ValidUntil)]);
        }
        if (Points.GroupBy(point => point.WorkPointId).Any(group => group.Count() > 1))
            yield return new("Un punct de lucru poate apărea o singură dată în contract.", [nameof(Points)]);
        foreach (var point in Points)
        {
            if (point.CycleMonths is { } cycle && cycle is < ServiceContractRules.MinimumCycle or > ServiceContractRules.MaximumCycle)
                yield return new($"Ciclicitatea individuală trebuie să fie între {ServiceContractRules.MinimumCycle} și {ServiceContractRules.MaximumCycle} luni.", [nameof(Points)]);
            if (point.NextDue is null && !point.MoveFromOtherContract)
                yield return new("Completează data primei intervenții pentru fiecare punct de lucru din contract.", [nameof(Points)]);
            else if (point.NextDue is { } due && !ServiceContractRules.InRange(due))
                yield return new("Una dintre datele primei intervenții nu este validă.", [nameof(Points)]);
        }
    }

    public ServiceContractInput Validated()
    {
        var normalized = new ServiceContractInput
        {
            NumberText = TextNormalization.ForObjectNameOrCode(NumberText),
            ValidUntil = ValidUntil,
            CycleMonths = CycleMonths,
            Notes = TextNormalization.ForStorage((Notes ?? "").Replace("\r\n", "\n")),
            Points = Points.Select(point => new ServiceContractPointInput
            {
                WorkPointId = point.WorkPointId, NextDue = point.NextDue, CycleMonths = point.CycleMonths, MoveFromOtherContract = point.MoveFromOtherContract
            }).ToList()
        };
        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(normalized, new ValidationContext(normalized), results, true))
            throw new ServiceContractOperationException(string.Join(" ", results.Select(result => result.ErrorMessage).Distinct()));
        ServiceContractNumber.TryParse(normalized.NumberText, out var number, out var date, out _);
        normalized.Number = number;
        normalized.Date = date;
        return normalized;
    }

    public static ServiceContractInput From(ServiceContractDetails value) => new()
    {
        NumberText = value.Contract.Label, ValidUntil = value.Contract.ValidUntil, CycleMonths = value.Contract.CycleMonths, Notes = value.Contract.Notes,
        Points = value.Points.Select(point => new ServiceContractPointInput
        {
            WorkPointId = point.Point.WorkPointId, NextDue = point.Point.NextDue, CycleMonths = point.Point.CycleMonths
        }).ToList()
    };
}

// What the reactivation dialog returns: the new due dates and whether the points now in another active contract are taken out.
public sealed record ServiceContractActivationChoice(IReadOnlyList<ServiceContractReschedule> Reschedules, bool RemoveConflicting);

// The new due date of one point chosen in the reactivation dialog.
public sealed record ServiceContractReschedule(int WorkPointId, DateOnly NextDue);

public sealed class ServiceContractOperationException(string message) : Exception(message);

// "26/23.09.2025": the number and the date of the contract. Accepts "26/23.09.2025", "26 / 23.09.2025" and "26 din 23.09.2025".
public static class ServiceContractNumber
{
    public const int NumberMaximumLength = 30;

    private static readonly Regex Pattern = new(@"^\s*(?<number>.+?)\s*(?:/|\bdin\b)\s*(?<date>\d{1,2}\.\d{1,2}\.\d{4})\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static bool TryParse(string? text, out string number, out DateOnly date, out string? error)
    {
        number = "";
        date = default;
        error = null;
        var match = Pattern.Match(text ?? "");
        if (!match.Success)
        {
            error = "Scrie numărul și data contractului ca «număr/zz.ll.aaaa», de exemplu 26/23.09.2025.";
            return false;
        }
        var value = TextNormalization.ForObjectNameOrCode(match.Groups["number"].Value).ToUpperInvariant();
        if (value.Length == 0 || value.Length > NumberMaximumLength || value.Any(char.IsControl))
        {
            error = $"Numărul contractului trebuie să aibă între 1 și {NumberMaximumLength} de caractere.";
            return false;
        }
        if (!DateOnly.TryParseExact(match.Groups["date"].Value, ["d.M.yyyy", "dd.MM.yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out date) ||
            !ServiceContractRules.InRange(date))
        {
            error = "Data contractului nu este o dată validă (zz.ll.aaaa).";
            return false;
        }
        number = value;
        return true;
    }

    public static string Format(string number, DateOnly date) => $"{number}/{StockMovementRules.DisplayDate(date)}";
}

public enum ServiceDueState { OnTime, DueSoon, Overdue }

// The displayed state of a due date (derived, never stored): Depasita when the date is before today, In curand within the threshold.
// The threshold will follow the active notification template of the maintenance source; until that source exists it is the default.
public static class ServiceDueRules
{
    public const int DefaultThresholdDays = 30;

    public static ServiceDueState State(DateOnly nextDue, DateOnly today, int thresholdDays = DefaultThresholdDays) =>
        nextDue < today ? ServiceDueState.Overdue
        : nextDue.DayNumber - today.DayNumber <= thresholdDays ? ServiceDueState.DueSoon
        : ServiceDueState.OnTime;

    public static string Label(ServiceDueState state) => state switch
    {
        ServiceDueState.Overdue => "Depășită",
        ServiceDueState.DueSoon => "În curând",
        _ => "La zi"
    };

    public static string CssClass(ServiceDueState state) => state switch
    {
        ServiceDueState.Overdue => "due-overdue",
        ServiceDueState.DueSoon => "due-soon",
        _ => "due-ok"
    };

    public static int EffectiveCycle(int? individualCycle, int contractCycle) => individualCycle ?? contractCycle;

    public static bool IsExpired(ServiceContract contract, DateOnly today) => contract.ValidUntil is { } validUntil && validUntil < today;
}

public interface IServiceContractRepository
{
    /// <summary>Every contract of the beneficiary (On and Off) with its covered work points, newest contract date first.</summary>
    Task<IReadOnlyList<ServiceContractDetails>> GetForBeneficiaryAsync(int beneficiaryId, CancellationToken cancellationToken = default);
    /// <summary>A new contract is On. A point already covered by another active contract is taken only with MoveFromOtherContract.</summary>
    Task<ServiceContractDetails> CreateAsync(int beneficiaryId, ServiceContractInput input, CancellationToken cancellationToken = default);
    /// <summary>Changes the fields of the contract and its coverage (points added, removed, moved in, cycle or due date changed).</summary>
    Task<ServiceContractDetails> UpdateAsync(ServiceContract original, ServiceContractInput input, CancellationToken cancellationToken = default);
    Task<ServiceContractActivationPlan> PrepareActivationAsync(int contractId, CancellationToken cancellationToken = default);
    /// <summary>Off to On. The chosen due dates replace the stored ones; points now in another active contract stop the activation
    /// unless removeConflicting is set (they are then taken out of this contract).</summary>
    Task<ServiceContractDetails> ActivateAsync(ServiceContract original, IReadOnlyList<ServiceContractReschedule> reschedules,
        bool removeConflicting, CancellationToken cancellationToken = default);
    /// <summary>On to Off: nothing is deleted, the points stop producing due dates.</summary>
    Task<ServiceContractDetails> DeactivateAsync(ServiceContract original, CancellationToken cancellationToken = default);
    /// <summary>Archived deletion of the contract with its coverage (allowed only while it has no interventions).</summary>
    Task DeleteAsync(ServiceContract original, string reason, CancellationToken cancellationToken = default);
}

public static class ServiceContractRules
{
    public const int MinimumCycle = 1;
    public const int MaximumCycle = 12;
    public static readonly DateOnly MinimumDate = new(2000, 1, 1);
    public static readonly DateOnly MaximumDate = new(2100, 12, 31);
    // Edits need no reason from the user; the journal receives this generated one.
    public const string GeneratedEditReason = "Editare contract de mentenanță";
    public const string GeneratedActivationReason = "Activare contract de mentenanță";
    public const string GeneratedDeactivationReason = "Dezactivare contract de mentenanță";

    public static bool InRange(DateOnly date) => date >= MinimumDate && date <= MaximumDate;

    public static ServiceContractOperationException Changed() =>
        new("Contractul s-a schimbat între timp. Actualizează pagina și reia operația.");

    public static ServiceContractOperationException Duplicate(string label) =>
        new($"Beneficiarul are deja un contract cu numărul și data {label}.");

    public static ServiceContractOperationException PointTaken(string workPointName, string contractLabel) =>
        new($"Punctul de lucru «{workPointName}» este deja în contractul activ {contractLabel}. Folosește «Mută aici» sau scoate-l mai întâi din acel contract.");

    public static ServiceContractOperationException ActivationBlocked(IEnumerable<ServiceContractConflict> conflicts) =>
        new("Contractul nu poate fi activat: " + string.Join("; ", conflicts.Select(conflict =>
            $"punctul «{conflict.WorkPointName}» este în contractul activ {conflict.ContractLabel}")) + ". Scoate punctele din contractul reactivat sau din celălalt contract.");

    public static ServiceContractOperationException WorkPointNotOfBeneficiary() =>
        new("Punctul de lucru ales nu aparține beneficiarului sau nu mai există. Actualizează pagina.");

    public static ServiceContractOperationException BeneficiaryMissing() =>
        new("Beneficiarul nu mai există. Actualizează lista.");

    public static WorkPointOperationException WorkPointCovered(string contractLabel) =>
        new($"Punctul de lucru este acoperit de contractul {contractLabel} și nu poate fi șters. Scoate-l mai întâi din contract.");

    public static BeneficiaryOperationException BeneficiaryHasContracts(int count) =>
        new(count == 1
            ? "Beneficiarul are un contract de mentenanță și nu poate fi șters. Șterge mai întâi contractul."
            : $"Beneficiarul are {count} contracte de mentenanță și nu poate fi șters. Șterge mai întâi contractele.");

    public static string Target(int beneficiaryId, string beneficiaryName, string contractLabel) =>
        $"#{beneficiaryId} · {beneficiaryName} · contract mentenanță «{contractLabel}»";

    public static string CycleText(int months) => months == 1 ? "1 lună" : $"{months} luni";

    public static string PointCycleText(int? individual, int contractCycle) =>
        individual is { } value ? $"{CycleText(value)} (individuală)" : $"{CycleText(contractCycle)} (din contract)";

    public static string Identification(ServiceContract value) => AuditDetails.Identification(new (string Field, string Value)[]
    {
        ("Contract", value.Label), ("Ciclicitate", CycleText(value.CycleMonths)),
        ("Expiră", value.ValidUntil is { } validUntil ? StockMovementRules.DisplayDate(validUntil) : "fără termen"), ("Observații", value.Notes)
    }.Where(item => item.Value.Length > 0).ToArray());

    public static IReadOnlyList<AuditChange> Changes(ServiceContract before, ServiceContract after) =>
    [
        new("Număr și dată", before.Label, after.Label), new("Ciclicitate", CycleText(before.CycleMonths), CycleText(after.CycleMonths)),
        new("Expiră", ValidUntilText(before.ValidUntil), ValidUntilText(after.ValidUntil)), new("Observații", before.Notes, after.Notes)
    ];

    public static string ValidUntilText(DateOnly? value) => value is { } date ? StockMovementRules.DisplayDate(date) : "fără termen";

    // The journal names the exact operation: an edit that changes only the expiry date, or only the cycle, has its own name.
    public static string EditAction(ServiceContract before, ServiceContract after)
    {
        var expiryChanged = before.ValidUntil != after.ValidUntil;
        var cycleChanged = before.CycleMonths != after.CycleMonths;
        var othersChanged = before.Number != after.Number || before.Date != after.Date || before.Notes != after.Notes;
        if (othersChanged || (expiryChanged && cycleChanged) || (!expiryChanged && !cycleChanged)) return AuditActions.EditServiceContract;
        return expiryChanged ? AuditActions.EditServiceContractExpiry : AuditActions.EditMaintenanceCycle;
    }

    public static void CheckCurrent(ServiceContract? current, ServiceContract original)
    {
        if (current is null || current != original) throw Changed();
    }
}
