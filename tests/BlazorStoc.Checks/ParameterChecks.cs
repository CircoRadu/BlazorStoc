using BlazorStoc.Services;

namespace BlazorStoc.Checks;

// Required parameters of the subcategories: the interval kind (start and end numbers) and, later, the families of parameters (exactly one active member per product).
public static class ParameterChecks
{
    public static Task RunAsync(Action<bool, string> check)
    {
        IntervalRules(check);
        FamilyRules(check);
        return Task.CompletedTask;
    }

    private static SubcategoryParameter Parameter(int id, string name, int position, int? familyId = null) =>
        new(id, "Camere", "Camere IP", name, "mm", ParameterKind.Number, position, 0, [], familyId);

    private static ParameterFamily Family(int id, string name) => new(id, "Camere", "Camere IP", name, 1, 0, []);

    // The slots of a subcategory: a family takes the place of its first member; a product needs exactly one active member per family.
    private static void FamilyRules(Action<bool, string> check)
    {
        var fixedLens = Parameter(1, "Fixa", 1, 7);
        var color = Parameter(2, "Culoare", 2);
        var varifocal = Parameter(3, "Varifocala", 3, 7);
        var lens = Family(7, "Lentila");
        var slots = ProductParameterRules.Slots([varifocal, color, fixedLens], [lens]);
        check(slots.Count == 2 && slots[0].Family?.Id == 7 && slots[0].Members.Select(member => member.Id).SequenceEqual([1, 3]) && slots[1].Members[0].Id == 2 && slots[0].Position == 1,
            "A family is one slot at the position of its first member; a parameter of its own is a slot of its own");
        check(ProductParameterRules.Slots([fixedLens, color, varifocal], []).Count == 3, "Without families every parameter is its own slot");

        check(ProductParameterRules.Problems(slots, new HashSet<int> { 1, 2 }).Count == 0 && ProductParameterRules.Problems(slots, new HashSet<int> { 3, 2 }).Count == 0, "One member of the family and the other parameters make a product complete");
        var none = ProductParameterRules.Problems(slots, new HashSet<int> { 2 });
        check(none.Count == 1 && none[0].Contains("Lentila") && none[0].Contains("alege una dintre: Fixa, Varifocala"), "A family with no active member is reported with the choices");
        var both = ProductParameterRules.Problems(slots, new HashSet<int> { 1, 3, 2 });
        check(both.Count == 1 && both[0].Contains("un singur parametru") && both[0].Contains("Fixa și Varifocala"), "A family with two active members is reported");
        var missingOwn = ProductParameterRules.Problems(slots, new HashSet<int> { 1 });
        check(missingOwn.Count == 1 && missingOwn[0] == "Culoare", "A parameter of its own without a value is reported by name");

        var order = ProductParameterRules.CodeOrder(slots, new HashSet<int> { 3, 2 });
        check(order.Select(item => item.Id).SequenceEqual([3, 2]) && ProductParameterRules.CodeOrder(slots, new HashSet<int> { 1, 2 }).Select(item => item.Id).SequenceEqual([1, 2]),
            "The code has the active member at the place of the family, whichever member it is");
        check(Refused(() => ProductParameterRules.FamilyName("  ")) is not null, "A family name is required");
        check(Refused(() => ProductParameterRules.FamilyName(new string('x', 80))) is not null, "A family name is limited to 50 characters");
        check(ProductParameterRules.FamilyName(" Lentila  mare ") == "Lentila mare", "A family name is trimmed and its inner spaces are collapsed");
        check(ProductParameterRules.FamilyTooSmallMessage.Contains("cel puțin 2") && ProductParameterRules.FamilyConflictMessage("Lentila", ["A", "B"]).Contains("A, B"), "The messages of a too small family and of a conflict say what to do");
    }

    private static string? Refused(Func<string> action)
    {
        try { action(); return null; }
        catch (ProductOperationException exception) { return exception.Message; }
    }

    private static void IntervalRules(Action<bool, string> check)
    {
        var kind = ParameterKind.Interval;
        check(ProductParameterRules.Value(kind, "Lentilă", "2,8-12") == "2.8-12" && ProductParameterRules.Value(kind, "Lentilă", " 2.80 – 12.0 ") == "2.8-12" &&
              ProductParameterRules.Value(kind, "Lentilă", "2,8..12") == "2.8-12", "An interval is written the same way whatever the user typed (comma or dot, spaces, en dash)");
        check(ProductParameterRules.Value(kind, "Temperatură", "-5--2") == "-5--2" && ProductParameterRules.Value(kind, "Temperatură", "-5 - 20") == "-5-20", "Negative ends are accepted");
        check(Refused(() => ProductParameterRules.Value(kind, "Lentilă", "12-2.8")) is { } reversed && reversed.Contains("mai mic"), "The start must be below the end");
        check(Refused(() => ProductParameterRules.Value(kind, "Lentilă", "4-4")) is not null, "An interval with equal ends is refused (a single number is a Number value)");
        check(Refused(() => ProductParameterRules.Value(kind, "Lentilă", "2,8")) is { } single && single.Contains("interval"), "A single number is not an interval");
        check(Refused(() => ProductParameterRules.Value(kind, "Lentilă", "-12")) is not null && Refused(() => ProductParameterRules.Value(kind, "Lentilă", "2,8-")) is not null && Refused(() => ProductParameterRules.Value(kind, "Lentilă", "abc-def")) is not null,
            "Incomplete or unreadable intervals are refused");
        check(Refused(() => ProductParameterRules.Value(ParameterKind.Number, "Lentilă", "2.8-12")) is not null, "A Number parameter still refuses an interval");
        check(ProductParameterRules.IntervalParts("2.8-12") == (2.8m, 12m) && ProductParameterRules.IntervalParts("-5--2") == (-5m, -2m) && ProductParameterRules.IntervalParts("alb") is null, "The two numbers of a stored interval can be read back");
        check(ProductParameterRules.UsesUnit(ParameterKind.Interval) && ProductParameterRules.UsesUnit(ParameterKind.Number) && !ProductParameterRules.UsesUnit(ParameterKind.Text), "Intervals and numbers have a unit, texts do not");
        check(ProductParameterRules.Display("mm", "2.8-12") == "2.8-12 mm" && ProductParameterRules.KindLabel(kind) == "Interval", "An interval shows with its unit and has its own label");
    }
}
