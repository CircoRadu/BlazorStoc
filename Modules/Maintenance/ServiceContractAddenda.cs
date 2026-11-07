using System.ComponentModel.DataAnnotations;
using System.Globalization;
using BlazorStoc.Modules;

namespace BlazorStoc.Services;

// Acte aditionale la contractele de mentenanta (service_contract_addenda). An addendum changes an existing contract at a date, under a number of its own ("AA 2/15.03.2026").
// What the application keeps about a contract can change through it: the validity, the cycle and the covered work points (the kinds "valabilitate", "ciclicitate", "puncte");
// the kind "altele" is only a description of other clauses. The effect is applied to the contract in the same transaction that stores the addendum, together with the values
// before and after, so the addendum can be cancelled (the last applied one) and the history stays readable. No file is attached to an addendum.

public static class ServiceAddendumKinds
{
    public const string Validity = "valabilitate", Cycle = "ciclicitate", Reference = "referinta", Components = "componente", Points = "puncte", Other = "altele";
    public static readonly IReadOnlyList<string> All = [Validity, Cycle, Reference, Components, Points, Other];

    public static string Label(string kind) => kind switch
    {
        Validity => "Valabilitate", Cycle => "Ciclicitate", Reference => "Data de referință", Components => "Componente", Points => "Puncte acoperite", Other => "Alte clauze", _ => kind
    };

    public static string Join(IEnumerable<string> kinds) => string.Join(',', All.Where(kinds.Contains));
    public static IReadOnlyList<string> Split(string text) => [.. All.Where(kind => text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains(kind))];
    /// <summary>Whether the kinds change something the application keeps (anything but the other clauses).</summary>
    public static bool HasEffect(IEnumerable<string> kinds) => kinds.Any(kind => kind != Other);
}

public static class ServiceAddendumStatus
{
    public const string Applied = "Aplicat", Cancelled = "Anulat";
}

// One covered work point as it was before or after the addendum.
public sealed record AddendumPointSnapshot(int WorkPointId, string WorkPointName, int? CycleMonths, DateOnly NextDue);

public sealed record ServiceContractAddendum(int Id, int ContractId, string Number, DateOnly Date, DateOnly? EffectiveDate, IReadOnlyList<string> Kinds, string Summary,
    DateOnly? PrevValidUntil, DateOnly? NewValidUntil, int? PrevCycleMonths, int? NewCycleMonths,
    IReadOnlyList<AddendumPointSnapshot> PointsBefore, IReadOnlyList<AddendumPointSnapshot> PointsAfter,
    string Status, string CreatedBy, DateTime CreatedUtc, string CancelledBy, DateTime? CancelledUtc, string CancelReason, long Version,
    DateOnly? ReferenceBefore = null, DateOnly? ReferenceAfter = null, IReadOnlyList<ServiceContractUnitInput>? UnitsBefore = null, IReadOnlyList<ServiceContractUnitInput>? UnitsAfter = null)
{
    public string Label => $"AA {Number}/{StockMovementRules.DisplayDate(Date)}";
    public bool IsApplied => Status == ServiceAddendumStatus.Applied;
    public bool HasEffect => ServiceAddendumKinds.HasEffect(Kinds);

    /// <summary>A short text of what the addendum changed ("valabilitate până la 31.12.2027; ciclicitate 6 luni; puncte +1 −0").</summary>
    public string Changes
    {
        get
        {
            var parts = new List<string>();
            if (Kinds.Contains(ServiceAddendumKinds.Validity)) parts.Add($"valabilitate: {ServiceContractRules.ValidUntilText(PrevValidUntil)} → {ServiceContractRules.ValidUntilText(NewValidUntil)}");
            if (Kinds.Contains(ServiceAddendumKinds.Cycle) && PrevCycleMonths is { } before && NewCycleMonths is { } after) parts.Add($"ciclicitate: {ServiceContractRules.CycleText(before)} → {ServiceContractRules.CycleText(after)}");
            if (Kinds.Contains(ServiceAddendumKinds.Reference)) parts.Add($"data de referință: {ServiceContractRules.ReferenceDateText(ReferenceBefore)} → {ServiceContractRules.ReferenceDateText(ReferenceAfter)}");
            if (Kinds.Contains(ServiceAddendumKinds.Components))
            {
                var unitsOld = UnitsBefore ?? [];
                var unitsNew = UnitsAfter ?? [];
                var addedUnits = unitsNew.Count(unit => unitsOld.All(old => old.Key != unit.Key));
                var removedUnits = unitsOld.Count(old => unitsNew.All(unit => unit.Key != old.Key));
                parts.Add($"componente: +{addedUnits} −{removedUnits}");
            }
            if (Kinds.Contains(ServiceAddendumKinds.Points))
            {
                var added = PointsAfter.Count(point => PointsBefore.All(old => old.WorkPointId != point.WorkPointId));
                var removed = PointsBefore.Count(old => PointsAfter.All(point => point.WorkPointId != old.WorkPointId));
                parts.Add($"puncte: +{added} −{removed}");
            }
            if (Kinds.Contains(ServiceAddendumKinds.Other) && Summary.Length > 0) parts.Add("alte clauze");
            return parts.Count == 0 ? "—" : string.Join("; ", parts);
        }
    }
}

/// <summary>The form of a new addendum. Points holds the WHOLE desired coverage when the kind "ciclicitate" or "puncte" is chosen (the repository diffs it against the current one).</summary>
public sealed class ServiceContractAddendumInput : IValidatableObject
{
    public const int SummaryMaximumLength = 1000;

    public string Number { get; set; } = "";
    public DateOnly? Date { get; set; }
    public DateOnly? EffectiveDate { get; set; }
    public bool ChangeValidity { get; set; }
    public DateOnly? NewValidUntil { get; set; }
    public bool ChangeCycle { get; set; }
    public int NewCycleMonths { get; set; } = 3;
    public bool ChangePoints { get; set; }
    // The reference date the act declares (otherwise the one of the contract stays).
    public bool ChangeReference { get; set; }
    public DateOnly? NewReferenceDate { get; set; }
    // The WHOLE set of families / components after the act (taken from the contract with On/Off, plus the ones the act adds); the cycle and the reference date of a unit are global (null) or declared.
    public bool ChangeComponents { get; set; }
    public List<ServiceContractUnitInput> Units { get; set; } = [];
    // Components of interventions of the beneficiary that had none (checked while the act is made): intervention id -> the components it covered.
    public List<AddendumInterventionComponents> InterventionComponents { get; set; } = [];
    public bool Other { get; set; }
    [StringLength(SummaryMaximumLength, ErrorMessage = "Descrierea poate avea cel mult 1000 de caractere.")]
    public string Summary { get; set; } = "";
    public List<ServiceContractPointInput> Points { get; set; } = [];

    public IReadOnlyList<string> Kinds => [.. new[]
    {
        ChangeValidity ? ServiceAddendumKinds.Validity : null, ChangeCycle ? ServiceAddendumKinds.Cycle : null,
        ChangeReference ? ServiceAddendumKinds.Reference : null, ChangeComponents ? ServiceAddendumKinds.Components : null,
        ChangePoints ? ServiceAddendumKinds.Points : null, Other ? ServiceAddendumKinds.Other : null
    }.OfType<string>()];

    /// <summary>The coverage (points, with the proposed dates) travels with the act when it changes the cycle, the reference date, the components or the points.</summary>
    public bool CarriesPoints => ChangeCycle || ChangePoints || ChangeComponents || ChangeReference;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var number = TextNormalization.ForObjectNameOrCode(Number ?? "");
        if (number.Length == 0 || number.Length > ServiceContractNumber.NumberMaximumLength)
            yield return new($"Numărul actului adițional trebuie să aibă între 1 și {ServiceContractNumber.NumberMaximumLength} de caractere.", [nameof(Number)]);
        if (Date is not { } date || !ServiceContractRules.InRange(date)) yield return new("Completează data actului adițional (o dată validă).", [nameof(Date)]);
        if (EffectiveDate is { } effective && !ServiceContractRules.InRange(effective)) yield return new("Data de la care se aplică actul nu este validă.", [nameof(EffectiveDate)]);
        if (Kinds.Count == 0) yield return new("Alege ce modifică actul: valabilitate, ciclicitate, data de referință, componente, puncte acoperite sau alte clauze.", [nameof(Kinds)]);
        if (ChangeReference && (NewReferenceDate is not { } reference || !ServiceContractRules.InRange(reference))) yield return new("Completează noua dată de referință (o dată validă).", [nameof(NewReferenceDate)]);
        if (ChangeComponents)
        {
            foreach (var unit in Units)
            {
                if ((unit.FamilyId is null) == (unit.ComponentId is null)) yield return new("O componentă a contractului este fie o familie, fie o componentă discretă.", [nameof(Units)]);
                if (unit.CycleMonths is { } unitCycle && unitCycle is < ServiceContractRules.MinimumCycle or > ServiceContractRules.MaximumCycle)
                    yield return new($"Ciclicitatea unei componente trebuie să fie între {ServiceContractRules.MinimumCycle} și {ServiceContractRules.MaximumCycle} luni.", [nameof(Units)]);
                if (unit.ReferenceDate is { } unitReference && !ServiceContractRules.InRange(unitReference)) yield return new("Data de referință a unei componente nu este validă.", [nameof(Units)]);
            }
            if (Units.GroupBy(unit => unit.Key).Any(group => group.Count() > 1)) yield return new("O familie sau o componentă poate apărea o singură dată în contract.", [nameof(Units)]);
        }
        if (ChangeValidity && NewValidUntil is { } validUntil && !ServiceContractRules.InRange(validUntil)) yield return new("Noua dată de expirare nu este validă.", [nameof(NewValidUntil)]);
        if (ChangeCycle && NewCycleMonths is < ServiceContractRules.MinimumCycle or > ServiceContractRules.MaximumCycle)
            yield return new($"Ciclicitatea trebuie să fie între {ServiceContractRules.MinimumCycle} și {ServiceContractRules.MaximumCycle} luni.", [nameof(NewCycleMonths)]);
        if (Other && TextNormalization.ForStorage(Summary ?? "").Length == 0) yield return new("Descrie celelalte clauze ale actului.", [nameof(Summary)]);
        if (CarriesPoints)
        {
            if (Points.GroupBy(point => point.WorkPointId).Any(group => group.Count() > 1)) yield return new("Un punct de lucru poate apărea o singură dată în contract.", [nameof(Points)]);
            foreach (var point in Points)
            {
                if (point.CycleMonths is { } cycle && cycle is < ServiceContractRules.MinimumCycle or > ServiceContractRules.MaximumCycle)
                    yield return new($"Ciclicitatea individuală trebuie să fie între {ServiceContractRules.MinimumCycle} și {ServiceContractRules.MaximumCycle} luni.", [nameof(Points)]);
                if (point.NextDue is null && !point.MoveFromOtherContract) yield return new("Completează scadența pentru fiecare punct de lucru din contract.", [nameof(Points)]);
                else if (point.NextDue is { } due && !ServiceContractRules.InRange(due)) yield return new("Una dintre scadențe nu este o dată validă.", [nameof(Points)]);
            }
        }
    }

    /// <summary>The normalized copy used by the repository (throws an operation exception with the messages of the failed checks).</summary>
    public ServiceContractAddendumInput Validated()
    {
        var copy = new ServiceContractAddendumInput
        {
            Number = TextNormalization.ForObjectNameOrCode(Number ?? "").ToUpperInvariant(), Date = Date, EffectiveDate = EffectiveDate, ChangeValidity = ChangeValidity, NewValidUntil = NewValidUntil,
            ChangeCycle = ChangeCycle, NewCycleMonths = NewCycleMonths, ChangePoints = ChangePoints, Other = Other,
            ChangeReference = ChangeReference, NewReferenceDate = NewReferenceDate, ChangeComponents = ChangeComponents,
            Units = Units.Select(unit => new ServiceContractUnitInput
            {
                FamilyId = unit.FamilyId, ComponentId = unit.ComponentId, CycleMonths = unit.CycleMonths, ReferenceDate = unit.ReferenceDate, IsActive = unit.IsActive,
                Members = [.. unit.Members.Select(member => new ServiceContractUnitMemberInput { ComponentId = member.ComponentId, IsActive = member.IsActive })]
            }).ToList(),
            InterventionComponents = InterventionComponents.Select(item => new AddendumInterventionComponents { InterventionId = item.InterventionId, ComponentIds = [.. item.ComponentIds.Distinct()] }).ToList(),
            Summary = TextNormalization.ForStorage((Summary ?? "").Replace("\r\n", "\n")),
            Points = Points.Select(point => new ServiceContractPointInput { WorkPointId = point.WorkPointId, NextDue = point.NextDue, CycleMonths = point.CycleMonths, MoveFromOtherContract = point.MoveFromOtherContract }).ToList()
        };
        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(copy, new ValidationContext(copy), results, true))
            throw new ServiceContractOperationException(string.Join(" ", results.Select(result => result.ErrorMessage).Distinct()));
        return copy;
    }
}

/// <summary>The components of an intervention of the beneficiary that had none, given while an act is made (the act verifies what the interventions covered).</summary>
public sealed class AddendumInterventionComponents
{
    public int InterventionId { get; set; }
    public List<int> ComponentIds { get; set; } = [];
}

/// <summary>The fields of an addendum that can still be corrected after it was applied (they do not change the contract).</summary>
public sealed class ServiceContractAddendumEdit
{
    public string Number { get; set; } = "";
    public DateOnly? Date { get; set; }
    public DateOnly? EffectiveDate { get; set; }
    public string Summary { get; set; } = "";
}

public static class ServiceContractAddendumRules
{
    public static ServiceContractOperationException Duplicate(string label) => new($"Contractul are deja un act adițional {label}.");
    public static ServiceContractOperationException Changed() => new("Actul adițional s-a schimbat între timp. Actualizează pagina și reia operația.");
    public static ServiceContractOperationException ContractOff() =>
        new("Contractul este oprit (Off): un act adițional care modifică valabilitatea, ciclicitatea sau punctele se poate adăuga doar la un contract activ. Activează contractul sau adaugă un act cu alte clauze.");
    public static ServiceContractOperationException NotLatest() => new("Se poate anula doar ultimul act adițional aplicat al contractului. Anulează mai întâi actele mai noi.");
    public static ServiceContractOperationException ContractMovedOn() =>
        new("Contractul a fost modificat după acest act (valabilitate, ciclicitate, referință, componente sau puncte), deci actul nu mai poate fi anulat fără să se piardă acele modificări.");
    public static ServiceContractOperationException NothingToCancel() => new("Actul adițional este deja anulat.");
    public static ServiceContractOperationException NotDeletable() => new("Se pot șterge doar actele anulate sau cele fără efect asupra contractului (alte clauze). Anulează mai întâi actul.");
    public static ServiceContractOperationException ContractHasAddenda(int count) => new(count == 1
        ? "Contractul are un act adițional și nu poate fi șters. Șterge mai întâi actul (anulează-l dacă a modificat contractul)."
        : $"Contractul are {count} acte adiționale și nu poate fi șters. Șterge mai întâi actele (anulează-le dacă au modificat contractul).");

    /// <summary>The due date proposed for a unit at a point: the last intervention that covered it (else the reference date) plus the cycle.</summary>
    public static DateOnly ProposedUnitDue(DateOnly? lastCovering, DateOnly reference, int cycleMonths) => (lastCovering ?? reference).AddMonths(cycleMonths);

    /// <summary>The due date proposed for a point when a change of cycle shifts it: the stored date moves by the difference of the cycles.</summary>
    public static DateOnly ProposedDue(DateOnly currentDue, int oldCycleMonths, int newCycleMonths) => currentDue.AddMonths(newCycleMonths - oldCycleMonths);

    public static string Target(int beneficiaryId, string beneficiaryName, string contractLabel, string addendumLabel) =>
        $"#{beneficiaryId} · {beneficiaryName} · contract mentenanță «{contractLabel}» · act adițional «{addendumLabel}»";
}

public interface IServiceContractAddendumRepository
{
    /// <summary>Every addendum of the contracts of the beneficiary, newest first.</summary>
    Task<IReadOnlyList<ServiceContractAddendum>> GetAddendaForBeneficiaryAsync(int beneficiaryId, CancellationToken cancellationToken = default);
    /// <summary>Stores the addendum and applies its effect to the contract (validity, cycle, coverage) in one transaction; returns the contract as it is now.</summary>
    Task<ServiceContractDetails> AddAsync(ServiceContract contract, ServiceContractAddendumInput input, CancellationToken cancellationToken = default);
    /// <summary>Corrects the number, the date, the effective date and the description of an addendum (the effect on the contract is not touched).</summary>
    Task<ServiceContractAddendum> UpdateAsync(ServiceContractAddendum original, ServiceContractAddendumEdit edit, CancellationToken cancellationToken = default);
    /// <summary>Cancels the last applied addendum of the contract and restores the validity, the cycle and the coverage it changed.</summary>
    Task<ServiceContractDetails> CancelAsync(ServiceContractAddendum original, string reason, CancellationToken cancellationToken = default);
    /// <summary>Deletes a cancelled addendum or one without effect on the contract (the journal keeps what it contained).</summary>
    Task DeleteAsync(ServiceContractAddendum original, string reason, CancellationToken cancellationToken = default);
}

internal sealed class GuardedServiceContractAddendumRepository(IServiceContractAddendumRepository inner, ModuleRegistry modules) : IServiceContractAddendumRepository
{
    private const string Id = BlazorStoc.Modules.Maintenance.MaintenanceModule.ModuleId;
    public Task<IReadOnlyList<ServiceContractAddendum>> GetAddendaForBeneficiaryAsync(int beneficiaryId, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.GetAddendaForBeneficiaryAsync(beneficiaryId, cancellationToken));
    public Task<ServiceContractDetails> AddAsync(ServiceContract contract, ServiceContractAddendumInput input, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.AddAsync(contract, input, cancellationToken));
    public Task<ServiceContractAddendum> UpdateAsync(ServiceContractAddendum original, ServiceContractAddendumEdit edit, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.UpdateAsync(original, edit, cancellationToken));
    public Task<ServiceContractDetails> CancelAsync(ServiceContractAddendum original, string reason, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.CancelAsync(original, reason, cancellationToken));
    public Task DeleteAsync(ServiceContractAddendum original, string reason, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.DeleteAsync(original, reason, cancellationToken));
}

internal sealed class UnavailableServiceContractAddendumRepository : IServiceContractAddendumRepository
{
    private static Task<T> Out<T>() => Task.FromException<T>(new ModuleUnavailableException(BlazorStoc.Modules.Maintenance.MaintenanceModule.ModuleId));
    public Task<IReadOnlyList<ServiceContractAddendum>> GetAddendaForBeneficiaryAsync(int beneficiaryId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ServiceContractAddendum>>([]);
    public Task<ServiceContractDetails> AddAsync(ServiceContract contract, ServiceContractAddendumInput input, CancellationToken cancellationToken = default) => Out<ServiceContractDetails>();
    public Task<ServiceContractAddendum> UpdateAsync(ServiceContractAddendum original, ServiceContractAddendumEdit edit, CancellationToken cancellationToken = default) => Out<ServiceContractAddendum>();
    public Task<ServiceContractDetails> CancelAsync(ServiceContractAddendum original, string reason, CancellationToken cancellationToken = default) => Out<ServiceContractDetails>();
    public Task DeleteAsync(ServiceContractAddendum original, string reason, CancellationToken cancellationToken = default) => Task.FromException(new ModuleUnavailableException(BlazorStoc.Modules.Maintenance.MaintenanceModule.ModuleId));
}
