using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.RegularExpressions;

namespace BlazorStoc.Services;

// A maintenance contract of a beneficiary (service_contracts). The number is kept structured (number + date) and shown as
// "26/23.09.2025". IsActive is the On/Off switch: Off keeps everything (coverage, due dates, history) but the points of the contract
// stop producing due dates. ValidUntil (optional) only drives the "Expirat" chip and, later, the expiry notification.
public sealed record ServiceContract(int Id, int BeneficiaryId, string Number, DateOnly Date, int? CycleMonths,
    DateOnly? ValidUntil, bool IsActive, string Notes, long Version, DateOnly? ReferenceDate = null, bool IsFrozen = false)
{
    public string Label => ServiceContractNumber.Format(Number, Date);
}

// A UNIT of a contract: a maintenance family (FamilyId; its members are switched On/Off one by one) or a discrete component (ComponentId). Cycle and reference date are global
// (null here = the ones of the contract) or given for the unit; the members of a family never have their own. A unit that is Off covers nothing.
public sealed record ServiceContractUnitMember(int ComponentId, bool IsActive);

public sealed record ServiceContractUnit(int Id, int ContractId, int? FamilyId, int? ComponentId, int? CycleMonths, DateOnly? ReferenceDate, bool IsActive, int Position,
    IReadOnlyList<ServiceContractUnitMember> Members)
{
    public bool IsFamily => FamilyId is not null;
    public int? CatalogId => FamilyId ?? ComponentId;
    /// <summary>A unit takes part in the contract when it is On and, for a family, at least one member is On.</summary>
    public bool Covers => IsActive && (!IsFamily || Members.Any(member => member.IsActive));
}

// The coverage of a work point by a contract (service_contract_points). CycleMonths null = inherits the cycle of the contract;
// NextDue is the current due date of the next maintenance visit (stored, the single field that tells when it is due).
public sealed record ServiceContractPoint(int Id, int ContractId, int WorkPointId, int? CycleMonths, DateOnly NextDue, long Version);

public sealed record ServiceContractPointView(ServiceContractPoint Point, string WorkPointName, string WorkPointAddress, bool WorkPointIsPrimary);

// One covered work point on the "Scadente" list of /mentenanta, with its beneficiary and contract.
// Latitude/Longitude are those of the work point (both or none); LastIntervention is the date of its latest maintenance intervention.
public sealed record ServiceDueRow(int BeneficiaryId, string BeneficiaryName, ServiceContract Contract, ServiceContractPointView Point,
    decimal? Latitude = null, decimal? Longitude = null, DateOnly? LastIntervention = null, IReadOnlyList<ServiceUnitDue>? UnitDueList = null)
{
    /// <summary>The due date of each unit (family / component) at this point; Point.Point.NextDue is the earliest of them.</summary>
    public IReadOnlyList<ServiceUnitDue> UnitDues => UnitDueList ?? [];
    public bool HasCoordinates => Latitude is not null && Longitude is not null && !(Latitude == 0 && Longitude == 0);
}

// The due date of one unit at one covered point (service_contract_unit_dues).
public sealed record ServiceUnitDue(int PointId, int UnitId, int? FamilyId, int? ComponentId, DateOnly NextDue);

public sealed record ServiceContractDetails(ServiceContract Contract, IReadOnlyList<ServiceContractPointView> Points, IReadOnlyList<ServiceContractUnit>? UnitList = null,
    IReadOnlyList<ServiceUnitDue>? DueList = null)
{
    public IReadOnlyList<ServiceUnitDue> Dues => DueList ?? [];
    public IReadOnlyList<ServiceContractUnit> Units => UnitList ?? [];

    /// <summary>The earliest due date among the covered points (null when the contract covers no point).</summary>
    public DateOnly? NextDue => Points.Count == 0 ? null : Points.Min(point => point.Point.NextDue);

    public int? EffectiveCycle(ServiceContractPoint point) => ServiceDueRules.EffectiveCycle(point.CycleMonths, Contract.CycleMonths);
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

// One unit of the contract in the form. A family lists all its members with their On/Off; a discrete component has no members.
public sealed class ServiceContractUnitMemberInput
{
    public int ComponentId { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class ServiceContractUnitInput
{
    public int? FamilyId { get; set; }
    public int? ComponentId { get; set; }
    public int? CycleMonths { get; set; }
    public DateOnly? ReferenceDate { get; set; }
    public bool IsActive { get; set; } = true;
    public List<ServiceContractUnitMemberInput> Members { get; set; } = [];

    public string Key => FamilyId is { } family ? $"F{family}" : $"C{ComponentId}";
}

public sealed class ServiceContractInput : IValidatableObject
{
    public const int NotesMaximumLength = 1000;

    // "26/23.09.2025", "26 / 23.09.2025" or "26 din 23.09.2025" (see ServiceContractNumber.TryParse).
    [StringLength(60, ErrorMessage = "Numărul și data contractului sunt prea lungi.")]
    public string NumberText { get; set; } = "";

    public DateOnly? ValidUntil { get; set; }
    // The global cycle is optional (each unit may carry its own); the reference date is the day the cycle is counted from and is mandatory.
    public int? CycleMonths { get; set; }
    public DateOnly? ReferenceDate { get; set; }
    public List<ServiceContractUnitInput> Units { get; set; } = [];

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
        if (CycleMonths is { } globalCycle && globalCycle is < ServiceContractRules.MinimumCycle or > ServiceContractRules.MaximumCycle)
            yield return new($"Ciclicitatea trebuie să fie între {ServiceContractRules.MinimumCycle} și {ServiceContractRules.MaximumCycle} luni.", [nameof(CycleMonths)]);
        if (ReferenceDate is not { } referenceDate) yield return new("Completează data de referință a contractului (data de la care se numără ciclul).", [nameof(ReferenceDate)]);
        else if (!ServiceContractRules.InRange(referenceDate)) yield return new("Data de referință nu este validă.", [nameof(ReferenceDate)]);
        foreach (var unit in Units)
        {
            if ((unit.FamilyId is null) == (unit.ComponentId is null)) yield return new("O componentă a contractului este fie o familie, fie o componentă discretă.", [nameof(Units)]);
            if (unit.CycleMonths is { } unitCycle && unitCycle is < ServiceContractRules.MinimumCycle or > ServiceContractRules.MaximumCycle)
                yield return new($"Ciclicitatea unei componente trebuie să fie între {ServiceContractRules.MinimumCycle} și {ServiceContractRules.MaximumCycle} luni.", [nameof(Units)]);
            if (unit.ReferenceDate is { } unitReference && !ServiceContractRules.InRange(unitReference))
                yield return new("Data de referință a unei componente nu este validă.", [nameof(Units)]);
            if (unit.FamilyId is null && unit.Members.Count > 0) yield return new("O componentă discretă nu are elemente.", [nameof(Units)]);
            if (unit.Members.GroupBy(member => member.ComponentId).Any(group => group.Count() > 1)) yield return new("Un element apare de două ori în aceeași familie.", [nameof(Units)]);
        }
        if (Units.GroupBy(unit => unit.Key).Any(group => group.Count() > 1))
            yield return new("O familie sau o componentă poate apărea o singură dată în contract.", [nameof(Units)]);
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
            ReferenceDate = ReferenceDate,
            Units = Units.Select(unit => new ServiceContractUnitInput
            {
                FamilyId = unit.FamilyId, ComponentId = unit.ComponentId, CycleMonths = unit.CycleMonths, ReferenceDate = unit.ReferenceDate, IsActive = unit.IsActive,
                Members = unit.Members.Select(member => new ServiceContractUnitMemberInput { ComponentId = member.ComponentId, IsActive = member.IsActive }).ToList()
            }).ToList(),
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
        NumberText = value.Contract.Label, ValidUntil = value.Contract.ValidUntil, CycleMonths = value.Contract.CycleMonths, ReferenceDate = value.Contract.ReferenceDate,
        Notes = value.Contract.Notes,
        Units = value.Units.Select(unit => new ServiceContractUnitInput
        {
            FamilyId = unit.FamilyId, ComponentId = unit.ComponentId, CycleMonths = unit.CycleMonths, ReferenceDate = unit.ReferenceDate, IsActive = unit.IsActive,
            Members = unit.Members.Select(member => new ServiceContractUnitMemberInput { ComponentId = member.ComponentId, IsActive = member.IsActive }).ToList()
        }).ToList(),
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

public enum ServiceExpiryState { NoTerm, Valid, ExpiresSoon, Expired }

// The displayed state of a due date (derived, never stored): Depasita when the date is before today, In curand within the threshold.
// The threshold is the one of the active notification template of the maintenance source (mentenanta.scadenta); this is the default used when it has none.
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

    public static int? EffectiveCycle(int? individualCycle, int? contractCycle) => individualCycle ?? contractCycle;

    public static bool IsExpired(ServiceContract contract, DateOnly today) => contract.ValidUntil is { } validUntil && validUntil < today;

    // The state of the contract term, shown next to (never instead of) the due state: an expired contract that is still On keeps producing due dates.
    public static ServiceExpiryState ExpiryState(ServiceContract contract, DateOnly today, int thresholdDays = DefaultThresholdDays) =>
        contract.ValidUntil is not { } validUntil ? ServiceExpiryState.NoTerm
        : validUntil < today ? ServiceExpiryState.Expired
        : validUntil.DayNumber - today.DayNumber <= thresholdDays ? ServiceExpiryState.ExpiresSoon
        : ServiceExpiryState.Valid;

    public static string ExpiryText(ServiceContract contract, DateOnly today, int thresholdDays = DefaultThresholdDays)
    {
        if (contract.ValidUntil is not { } validUntil) return "Fără termen";
        var days = validUntil.DayNumber - today.DayNumber;
        return ExpiryState(contract, today, thresholdDays) switch
        {
            ServiceExpiryState.Expired => days == -1 ? "Expirat de o zi" : $"Expirat de {-days} zile",
            ServiceExpiryState.ExpiresSoon => days == 0 ? "Expiră astăzi" : days == 1 ? "Expiră mâine" : $"Expiră în {days} zile",
            _ => $"Valabil până la {StockMovementRules.DisplayDate(validUntil)}"
        };
    }
}

// A frozen contract (incomplete data) with the name of its beneficiary, for the "Contracte incomplete" tab of the notifications.
public sealed record ServiceContractFrozenItem(int BeneficiaryId, string BeneficiaryName, ServiceContractDetails Details);

public interface IServiceContractRepository
{
    /// <summary>Every frozen contract (On or Off) with its units and points, by beneficiary.</summary>
    Task<IReadOnlyList<ServiceContractFrozenItem>> GetFrozenAsync(CancellationToken cancellationToken = default);
    /// <summary>The number of frozen contracts that are On (the red number of the notifications tab).</summary>
    Task<int> CountFrozenAsync(CancellationToken cancellationToken = default);
    /// <summary>Every contract of the beneficiary (On and Off) with its covered work points, newest contract date first.</summary>
    Task<IReadOnlyList<ServiceContractDetails>> GetForBeneficiaryAsync(int beneficiaryId, CancellationToken cancellationToken = default);
    /// <summary>Every covered work point of every beneficiary, earliest due date first; contracts that are Off only with includeOff.</summary>
    Task<IReadOnlyList<ServiceDueRow>> GetDueListAsync(bool includeOff, CancellationToken cancellationToken = default);
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

    public static string CycleText(int? months) => months is { } value ? CycleText(value) : "nestabilită";

    public static string PointCycleText(int? individual, int? contractCycle) =>
        individual is { } value ? $"{CycleText(value)} (individuală)" : contractCycle is { } cycle ? $"{CycleText(cycle)} (din contract)" : "nestabilită";

    public static string ReferenceDateText(DateOnly? value) => value is { } date ? StockMovementRules.DisplayDate(date) : "nestabilită";

    public static string Identification(ServiceContract value) => AuditDetails.Identification(new (string Field, string Value)[]
    {
        ("Contract", value.Label), ("Ciclicitate", CycleText(value.CycleMonths)), ("Data de referință", ReferenceDateText(value.ReferenceDate)),
        ("Expiră", value.ValidUntil is { } validUntil ? StockMovementRules.DisplayDate(validUntil) : "fără termen"), ("Observații", value.Notes)
    }.Where(item => item.Value.Length > 0).ToArray());

    public static IReadOnlyList<AuditChange> Changes(ServiceContract before, ServiceContract after) =>
    [
        new("Număr și dată", before.Label, after.Label), new("Ciclicitate", CycleText(before.CycleMonths), CycleText(after.CycleMonths)),
        new("Data de referință", ReferenceDateText(before.ReferenceDate), ReferenceDateText(after.ReferenceDate)),
        new("Expiră", ValidUntilText(before.ValidUntil), ValidUntilText(after.ValidUntil)), new("Observații", before.Notes, after.Notes)
    ];

    public static string ValidUntilText(DateOnly? value) => value is { } date ? StockMovementRules.DisplayDate(date) : "fără termen";

    // The journal names the exact operation: an edit that changes only the expiry date, or only the cycle, has its own name.
    public static string EditAction(ServiceContract before, ServiceContract after)
    {
        var expiryChanged = before.ValidUntil != after.ValidUntil;
        var cycleChanged = before.CycleMonths != after.CycleMonths;
        var referenceChanged = before.ReferenceDate != after.ReferenceDate;
        var othersChanged = before.Number != after.Number || before.Date != after.Date || before.Notes != after.Notes;
        if (referenceChanged && !othersChanged && !expiryChanged && !cycleChanged) return AuditActions.EditContractReferenceDate;
        if (othersChanged || (expiryChanged && cycleChanged) || (!expiryChanged && !cycleChanged)) return AuditActions.EditServiceContract;
        return expiryChanged ? AuditActions.EditServiceContractExpiry : AuditActions.EditMaintenanceCycle;
    }

    public static void CheckCurrent(ServiceContract? current, ServiceContract original)
    {
        if (current is null || current != original) throw Changed();
    }
}
