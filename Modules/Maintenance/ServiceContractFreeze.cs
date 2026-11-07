namespace BlazorStoc.Services;

// A maintenance contract whose data is incomplete is FROZEN: it produces no due dates (list, map, notifications) and its interventions do not move dates until the missing
// information is filled in. The rules are the same for the form (live preview), the repository (is_frozen / missing_info are stored at every change) and the notification tab.
public static class ServiceContractFreeze
{
    public const string NoUnits = "Nicio componentă sau familie activă în contract";
    public const string NoPoints = "Niciun punct de lucru acoperit";

    public static int? Cycle(ServiceContractUnit unit, int? contractCycle) => unit.CycleMonths ?? contractCycle;

    public static DateOnly? Reference(ServiceContractUnit unit, DateOnly? contractReference) => unit.ReferenceDate ?? contractReference;

    /// <summary>What is missing for the contract to work: empty = complete. nameOf gives the name of a unit (family or component) for the messages.</summary>
    public static IReadOnlyList<string> Missing(DateOnly? contractReference, int? contractCycle, IReadOnlyList<ServiceContractUnit> units, int pointCount, Func<ServiceContractUnit, string> nameOf)
    {
        var missing = new List<string>();
        var active = units.Where(unit => unit.IsActive).ToList();
        foreach (var family in active.Where(unit => unit.IsFamily && !unit.Members.Any(member => member.IsActive)))
            missing.Add($"Familia «{nameOf(family)}» nu are nicio componentă activă");
        var covering = active.Where(unit => unit.Covers).ToList();
        if (covering.Count == 0) missing.Add(NoUnits);
        if (pointCount == 0) missing.Add(NoPoints);
        if (contractReference is null)
        {
            var names = covering.Where(unit => unit.ReferenceDate is null).Select(nameOf).ToList();
            missing.Add(names.Count > 0 ? $"Data de referință ({string.Join(", ", names)})" : "Data de referință");
        }
        if (contractCycle is null)
        {
            var names = covering.Where(unit => unit.CycleMonths is null).Select(nameOf).ToList();
            if (names.Count > 0) missing.Add($"Ciclicitatea ({string.Join(", ", names)})");
        }
        return missing;
    }

    public static IReadOnlyList<string> Missing(ServiceContractDetails details, Func<ServiceContractUnit, string> nameOf) =>
        Missing(details.Contract.ReferenceDate, details.Contract.CycleMonths, details.Units, details.Points.Count, nameOf);

    /// <summary>The units of a form as units of a contract (id 0), to preview the frozen state before saving.</summary>
    public static IReadOnlyList<ServiceContractUnit> FromInput(IEnumerable<ServiceContractUnitInput> units) =>
        [.. units.Select((unit, index) => new ServiceContractUnit(0, 0, unit.FamilyId, unit.ComponentId, unit.CycleMonths, unit.ReferenceDate, unit.IsActive, index,
            [.. unit.Members.Select(member => new ServiceContractUnitMember(member.ComponentId, member.IsActive))]))];

    public static string Summary(IReadOnlyList<string> missing) => string.Join("; ", missing);

    /// <summary>The stored text is limited to the width of the column.</summary>
    public static string Stored(IReadOnlyList<string> missing)
    {
        var text = Summary(missing);
        return text.Length <= 500 ? text : text[..499] + "…";
    }
}
