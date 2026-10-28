using BlazorStoc.Services;

namespace BlazorStoc.Checks;

// The plan of the relocation service (Services/DefinitionRelocation.cs) on hand-made snapshots: what moves, what merges, what needs a decision and what is blocked.
public static class DefinitionRelocationPlannerChecks
{
    // One category (owner -1) with a root X (1), two children C (2) and S (3) under it, and another root Y (4) with a child D (5).
    private static readonly int Category = -1, X = 1, C = 2, S = 3, Y = 4, D = 5;

    private static DefSnapshot Snapshot(IEnumerable<SnapParameter>? parameters = null, IEnumerable<SnapFamily>? families = null, IEnumerable<SnapTag>? tags = null) => new(
        new Dictionary<int, int?> { [X] = null, [C] = X, [S] = X, [Y] = null, [D] = Y },
        new Dictionary<int, int> { [X] = 1, [C] = 1, [S] = 1, [Y] = 1, [D] = 1 },
        new Dictionary<int, string> { [Category] = "Cat", [X] = "X", [C] = "C", [S] = "S", [Y] = "Y", [D] = "D" },
        [.. parameters ?? []], [.. families ?? []], [.. tags ?? []]);

    private static SnapParameter P(int id, string name, int owner, ParameterKind kind = ParameterKind.Number, string unit = "mm", int? family = null, bool archived = false, int products = 1, params string[] values) =>
        new(id, name, ProductParameterRules.Key(name), unit, kind, owner, family, archived, products,
            [.. values.Select((value, index) => new SnapValue(id * 100 + index, value, ProductParameterRules.Key(value), 1))]);

    private static SnapFamily F(int id, string name, int owner, bool archived = false) => new(id, name, ProductParameterRules.Key(name), owner, archived);
    private static SnapTag T(int id, string name, int owner, bool archived = false, int products = 1) => new(id, name, ProductParameterRules.Key(name), owner, archived, products);

    private static RelocationPlan Plan(DefSnapshot snapshot, int target, IEnumerable<DefinitionRef> definitions, params DefinitionRef[] confirmed) =>
        RelocationPlanner.Plan(snapshot, new RelocationRequest(new ProductGroup("Cat", ""), [.. definitions], confirmed), target);

    private static DefinitionRef Param(int id) => new(DefinitionKind.Parameter, id);
    private static DefinitionRef Family(int id) => new(DefinitionKind.Family, id);
    private static DefinitionRef Tag(int id) => new(DefinitionKind.Tag, id);

    public static void Run(Action<bool, string> check)
    {
        // A parameter with no namesake moves up.
        var plan = Plan(Snapshot([P(10, "Lentila", C, values: ["2.8"])]), X, [Param(10)]);
        check(plan.Blocks.Count == 0 && plan.Items.Single().Action == RelocationAction.Move && plan.Operations.SequenceEqual([new MoveOp(DefinitionKind.Parameter, 10, X)]),
            "Relocation plan: a parameter with no namesake simply moves to the target");

        // A namesake in a sibling branch needs a decision; confirmed, the moved one absorbs it.
        var sibling = Snapshot([P(10, "Lentila", C, values: ["2.8", "4"]), P(11, "lentila", S, values: ["2.8", "6"])]);
        plan = Plan(sibling, X, [Param(10)]);
        check(plan.Items.Single().Action == RelocationAction.NeedsDecision && plan.Operations.Count == 0 && plan.Items.Single().Absorbs.Single().Contains("S", StringComparison.Ordinal),
            "Relocation plan: a namesake in a sibling branch is reported and nothing is planned until the user decides");
        plan = Plan(sibling, X, [Param(10)], Param(10));
        var absorb = plan.Items.Single();
        check(plan.Blocks.Count == 0 && absorb.Action == RelocationAction.Absorb && plan.Operations.SequenceEqual([new MoveOp(DefinitionKind.Parameter, 10, X), new MergeParameterOp(11, 10)])
              && absorb.Values.Count == 2 && absorb.Values.Single(value => value.Value == "2.8 mm").ExistsAtSurvivor && !absorb.Values.Single(value => value.Value == "6 mm").ExistsAtSurvivor,
            "Relocation plan: a confirmed namesake below is absorbed after the move and the value mapping says which values already exist");

        // A namesake at the target (or above) absorbs the moved parameter, and the namesakes of the sibling branches with it.
        var above = Snapshot([P(10, "Lentila", C, values: ["2.8"]), P(12, "Lentila", X, values: ["2.8", "8"]), P(11, "Lentila", S, values: ["9"])]);
        check(Plan(above, X, [Param(10)]).Items.Single().Action == RelocationAction.NeedsDecision, "Relocation plan: a namesake at the target also needs a decision");
        plan = Plan(above, X, [Param(10)], Param(10));
        check(plan.Items.Single().Action == RelocationAction.Merge && plan.Operations.SequenceEqual([new MergeParameterOp(10, 12), new MergeParameterOp(11, 12)]) && plan.Items.Single().To == "„X”",
            "Relocation plan: a confirmed namesake at the target survives and absorbs the moved parameter and the ones below");
        var categoryLevel = Snapshot([P(10, "Lentila", C, values: ["2.8"]), P(13, "Lentila", Category, values: ["2.8"])]);
        plan = Plan(categoryLevel, X, [Param(10)], Param(10));
        check(plan.Items.Single().Action == RelocationAction.Merge && plan.Items.Single().To == "categoria Cat" && plan.Operations.SequenceEqual([new MergeParameterOp(10, 13)]),
            "Relocation plan: a namesake owned by the category survives too (it is above every node)");

        // A different kind or unit blocks; an archived namesake blocks; a parameter in a family is not moved alone.
        check(Plan(Snapshot([P(10, "Lentila", C), P(11, "Lentila", S, ParameterKind.Text, "")]), X, [Param(10)], Param(10)).Blocks.Single().Contains("tip diferit", StringComparison.Ordinal),
            "Relocation plan: a namesake of another kind blocks the relocation");
        check(Plan(Snapshot([P(10, "Lentila", C), P(11, "Lentila", S, unit: "cm")]), X, [Param(10)], Param(10)).Blocks.Single().Contains("altă unitate", StringComparison.Ordinal),
            "Relocation plan: a namesake with another unit blocks the relocation (the same names and kinds are not enough)");
        check(Plan(Snapshot([P(10, "Lentila", C), P(11, "Lentila", S, archived: true)]), X, [Param(10)], Param(10)).Blocks.Single().Contains("arhivat", StringComparison.Ordinal),
            "Relocation plan: an archived namesake blocks until it is restored or deleted");
        check(Plan(Snapshot([P(10, "Lentila", C, family: 50), P(14, "Alta", C, family: 50)], [F(50, "Fam", C)]), X, [Param(10)]).Blocks.Single().Contains("familie", StringComparison.Ordinal),
            "Relocation plan: a family member is not moved alone");
        check(Plan(Snapshot([P(10, "Lentila", X)]), X, [Param(10)]).Blocks.Single().Contains("deja", StringComparison.Ordinal), "Relocation plan: a definition already owned by the target is refused");
        check(Plan(Snapshot([P(10, "Lentila", C), P(11, "Lentila", S)]), X, [Param(10), Param(11)]).Blocks.Any(block => block.Contains("de două ori", StringComparison.Ordinal)),
            "Relocation plan: two requested parameters with the same name are refused");
        check(Plan(Snapshot(), X, []).Blocks.Single() == RelocationMessages.NothingToDo, "Relocation plan: an empty request is refused");

        // Tags move and merge by name.
        var tags = Snapshot(tags: [T(20, "PoE", C), T(21, "poe", S), T(22, "Audio", C)]);
        plan = Plan(tags, X, [Tag(22)]);
        check(plan.Items.Single().Action == RelocationAction.Move && plan.Operations.SequenceEqual([new MoveOp(DefinitionKind.Tag, 22, X)]), "Relocation plan: a tag with no namesake moves");
        plan = Plan(tags, X, [Tag(20)], Tag(20));
        check(plan.Items.Single().Action == RelocationAction.Absorb && plan.Operations.SequenceEqual([new MoveOp(DefinitionKind.Tag, 20, X), new MergeTagOp(21, 20)]),
            "Relocation plan: a confirmed tag namesake in a sibling branch is merged into the moved tag");
        check(Plan(Snapshot(tags: [T(20, "PoE", C), T(21, "PoE", S, archived: true)]), X, [Tag(20)], Tag(20)).Blocks.Single().Contains("arhivată", StringComparison.Ordinal), "Relocation plan: an archived tag namesake blocks");

        // Families: a clean move, a partial family, and a merge with the family of the target.
        var members = new[] { P(30, "Cablat", C, ParameterKind.Text, "", 60, values: ["da"]), P(31, "Wireless", C, ParameterKind.Text, "", 60, values: ["da"]) };
        plan = Plan(Snapshot(members, [F(60, "Conectare", C)]), X, [Family(60)]);
        check(plan.Blocks.Count == 0 && plan.Items.Single().Action == RelocationAction.Move && plan.Operations.SequenceEqual([new MoveOp(DefinitionKind.Family, 60, X)]), "Relocation plan: a family with no namesake moves with its members");
        var partial = Snapshot([.. members, P(32, "Cablat", S, ParameterKind.Text, "", values: ["da"])], [F(60, "Conectare", C)]);
        check(Plan(partial, X, [Family(60)], Family(60)).Blocks.Single().Contains("familie parțială", StringComparison.Ordinal), "Relocation plan: a member whose namesake is a separate parameter blocks (partial family)");
        var theirs = new[] { P(40, "Cablat", X, ParameterKind.Text, "", 70, values: ["da", "nu"]), P(41, "Wireless", X, ParameterKind.Text, "", 70, values: ["da"]), P(42, "Fibra", X, ParameterKind.Text, "", 70, values: ["da"]) };
        var familyMerge = Snapshot([.. members, .. theirs], [F(60, "Conectare", C), F(70, "Conectare", X)]);
        check(Plan(familyMerge, X, [Family(60)]).Items.Single().Action == RelocationAction.NeedsDecision, "Relocation plan: a family namesake at the target needs a decision");
        plan = Plan(familyMerge, X, [Family(60)], Family(60));
        check(plan.Blocks.Count == 0 && plan.Items.Single().Action == RelocationAction.Merge && plan.Operations.SequenceEqual([new MergeParameterOp(30, 40), new MergeParameterOp(31, 41), new DropFamilyOp(60)])
              && plan.Items.Single().Values.Any(value => value.Value == "da" && value.ExistsAtSurvivor),
            "Relocation plan: a confirmed family merge pairs the members by name, merges their values and drops the absorbed family");
        var extra = Snapshot([.. members, P(32, "Fibra", C, ParameterKind.Text, "", 60, values: ["da"]), .. theirs.Take(2)], [F(60, "Conectare", C), F(70, "Conectare", X)]);
        plan = Plan(extra, X, [Family(60)], Family(60));
        check(plan.Operations.OfType<JoinFamilyOp>().Single() is { ParameterId: 32, FamilyId: 70, ToOwner: 1 }, "Relocation plan: a member with no pair joins the surviving family and takes its owner");
        var taken = Snapshot([.. members, P(32, "Fibra", C, ParameterKind.Text, "", 60, values: ["da"]), .. theirs.Take(2), P(43, "Fibra", S, ParameterKind.Text, "", values: ["da"])], [F(60, "Conectare", C), F(70, "Conectare", X)]);
        check(Plan(taken, X, [Family(60)], Family(60)).Blocks.Single().Contains("Fibra", StringComparison.Ordinal), "Relocation plan: a member joining a family cannot repeat a name that exists in the new scope");
        var otherContent = Snapshot([.. members, P(50, "Alt", X, ParameterKind.Text, "", 70, values: ["da"]), P(51, "Altul", X, ParameterKind.Text, "", 70, values: ["da"])], [F(60, "Conectare", C), F(70, "Conectare", X)]);
        check(Plan(otherContent, X, [Family(60)], Family(60)).Blocks.Single().Contains("alți parametri", StringComparison.Ordinal), "Relocation plan: a family of the same name with other parameters blocks");
        var kindClash = Snapshot([.. members, P(40, "Cablat", X, ParameterKind.Number, "mm", 70, values: ["1"]), P(41, "Wireless", X, ParameterKind.Text, "", 70)], [F(60, "Conectare", C), F(70, "Conectare", X)]);
        check(Plan(kindClash, X, [Family(60)], Family(60)).Blocks.Single().Contains("tip diferit", StringComparison.Ordinal), "Relocation plan: members of different kinds block the family merge");

        // A category as target: its namesakes are above, everything under it is below.
        plan = Plan(Snapshot([P(10, "Lentila", C, values: ["2.8"]), P(11, "Lentila", D, values: ["4"])]), Category, [Param(10)], Param(10));
        check(plan.Items.Single().Action == RelocationAction.Absorb && plan.Operations.SequenceEqual([new MoveOp(DefinitionKind.Parameter, 10, Category), new MergeParameterOp(11, 10)]),
            "Relocation plan: moving to a category absorbs the namesakes of every subcategory under it, in any branch");
    }

    // ---- moving a branch while keeping what it inherits (stage 6) ----
    private static BranchMovePlan Branch(DefSnapshot snapshot, int node, int? newParent, int newCategory, params DefinitionRef[] confirmed) =>
        BranchMovePlanner.Plan(snapshot, node, newParent, newCategory, confirmed);

    public static void RunBranch(Action<bool, string> check)
    {
        // C (under X) moves under Y: what X defines is copied onto C, what Y defines is gained.
        var lostParameter = Snapshot([P(10, "Lentila", X, values: ["2.8"])]);
        var plan = Branch(lostParameter, C, Y, 1);
        check(plan.Blocks.Count == 0 && plan.Items.Single().Action == BranchMoveAction.Copy && plan.Operations.SequenceEqual([new CopyParameterOp(10, C)]),
            "Branch move plan: an inherited parameter the branch leaves is copied onto the moved node");
        var staying = Branch(Snapshot([P(10, "Lentila", Category, values: ["2.8"])]), C, Y, 1);
        check(staying.Items.Count == 0 && staying.Operations.Count == 0, "Branch move plan: a definition of the category the branch stays in is still inherited and is not copied");
        var clash = Snapshot([P(10, "Lentila", X, values: ["2.8", "4"]), P(11, "Lentila", Y, values: ["2.8", "6"])]);
        plan = Branch(clash, C, Y, 1);
        check(plan.Items.Single().Action == BranchMoveAction.NeedsDecision && plan.Operations.Count == 0, "Branch move plan: a namesake at the new ancestors needs a decision");
        plan = Branch(clash, C, Y, 1, Param(10));
        check(plan.Items.Single().Action == BranchMoveAction.CopyMerged && plan.Operations.SequenceEqual([new RelinkParameterOp(10, 11)])
              && plan.Items.Single().Values.Any(value => value.Value == "2.8 mm" && value.ExistsAtSurvivor) && plan.Items.Single().Values.Any(value => value.Value == "4 mm" && !value.ExistsAtSurvivor),
            "Branch move plan: a confirmed namesake takes the products of the branch over and unites the values");
        check(Branch(Snapshot([P(10, "Lentila", X), P(11, "Lentila", Y, unit: "cm")]), C, Y, 1, Param(10)).Blocks.Single().Contains("tip sau unitate", StringComparison.Ordinal), "Branch move plan: a namesake with another unit blocks");
        check(Branch(Snapshot([P(10, "Lentila", X), P(11, "Lentila", Y, archived: true)]), C, Y, 1, Param(10)).Blocks.Single().Contains("arhivat", StringComparison.Ordinal), "Branch move plan: an archived namesake blocks");
        check(Branch(Snapshot([P(10, "Lentila", X, archived: true)]), C, Y, 1).Items.Count == 0, "Branch move plan: an archived definition of a parent the branch leaves is not copied (the products check is made on the real data)");

        var tags = Snapshot(tags: [T(20, "PoE", X), T(21, "Audio", X), T(22, "audio", Y)]);
        plan = Branch(tags, C, Y, 1);
        check(plan.Items.Single(item => item.Name == "PoE").Action == BranchMoveAction.Copy && plan.Items.Single(item => item.Name == "Audio").Action == BranchMoveAction.NeedsDecision && plan.Operations.SequenceEqual([new CopyTagOp(20, C)]),
            "Branch move plan: tags are copied, or need a decision when the new ancestors have the same tag");
        plan = Branch(tags, C, Y, 1, Tag(21));
        check(plan.Operations.SequenceEqual([new CopyTagOp(20, C), new RelinkTagOp(21, 22)]), "Branch move plan: a confirmed tag namesake takes the On/Off of the branch over");

        var members = new[] { P(30, "Cablat", X, ParameterKind.Text, "", 60), P(31, "Wireless", X, ParameterKind.Text, "", 60) };
        plan = Branch(Snapshot(members, [F(60, "Conectare", X)]), C, Y, 1);
        check(plan.Items.Single().Action == BranchMoveAction.Copy && plan.Operations.SequenceEqual([new CopyFamilyOp(60, C)]), "Branch move plan: an inherited family is copied with its members");
        check(Branch(Snapshot(members, [F(60, "Conectare", X), F(70, "Conectare", Y)]), C, Y, 1).Blocks.Single().Contains("moștenită", StringComparison.Ordinal), "Branch move plan: a family namesake at the new ancestors blocks");
        check(Branch(Snapshot([.. members, P(32, "Cablat", Y, ParameterKind.Text, "")], [F(60, "Conectare", X)]), C, Y, 1).Blocks.Single().Contains("Cablat", StringComparison.Ordinal), "Branch move plan: a family member that exists at the new ancestors blocks");

        // A definition of the branch that repeats one of the new ancestors.
        var own = Snapshot([P(40, "Culoare", C, ParameterKind.Text, ""), P(41, "Culoare", Y, ParameterKind.Text, "")]);
        plan = Branch(own, C, Y, 1);
        check(plan.Items.Single().Action == BranchMoveAction.NeedsDecision, "Branch move plan: an own parameter that repeats a new ancestor needs a decision");
        plan = Branch(own, C, Y, 1, Param(40));
        check(plan.Items.Single().Action == BranchMoveAction.OwnMerged && plan.Operations.SequenceEqual([new MergeParameterOp(40, 41)]), "Branch move plan: a confirmed own parameter merges into the one of the new ancestors");



        // Leaving the category: its definitions are lost, those of the new category are gained.
        var categories = new DefSnapshot(
            new Dictionary<int, int?> { [X] = null, [C] = X, [Y] = null, [D] = Y },
            new Dictionary<int, int> { [X] = 1, [C] = 1, [Y] = 2, [D] = 2 },
            new Dictionary<int, string> { [-1] = "Cat", [-2] = "Alta", [X] = "X", [C] = "C", [Y] = "Y", [D] = "D" },
            [P(60, "Garantie", -1, ParameterKind.Text, "", values: ["2 ani"]), P(61, "Lentila", X, values: ["2.8"]), P(62, "Garantie", -2, ParameterKind.Text, "", values: ["1 an"])], [], []);
        plan = BranchMovePlanner.Plan(categories, C, Y, 2, [new DefinitionRef(DefinitionKind.Parameter, 60)]);
        check(plan.Items.Single(item => item.Ref.Id == 60).Action == BranchMoveAction.CopyMerged && plan.Items.Single(item => item.Ref.Id == 61).Action == BranchMoveAction.Copy
              && plan.Operations.SequenceEqual(new RelocationOp[] { new RelinkParameterOp(60, 62), new CopyParameterOp(61, C) }),
            "Branch move plan: moving to another category merges the category's parameter into the namesake of the new category and copies what the old parent defined");
    }}
