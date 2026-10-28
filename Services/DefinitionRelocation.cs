namespace BlazorStoc.Services;

// The shared service that relocates parameters, families and tags inside the tree (stages 5 to 7 of the catalog work): "take a child's definitions into the parent", a move
// that keeps the inherited definitions, a change of level. It moves the owner of a definition to a target node (or category) and merges it with the definitions of the same
// name it meets there: values are united, the links of the products are repointed, a family keeps exactly one active member per product. The plan is computed by the pure
// RelocationPlanner on a snapshot of the definitions; the preview is the real execution inside a transaction that is rolled back, so what it shows is what the apply does.
public enum DefinitionKind { Parameter = 1, Family = 2, Tag = 3 }

public sealed record DefinitionRef(DefinitionKind Kind, int Id);

// Target: a subcategory, or a category (empty Subcategory). ConfirmedMerges: the definitions the user agreed to merge with the ones of the same name they meet (a clash with no
// decision is reported, never merged silently). AcceptIncomplete: the products that become incomplete (a parameter raised to a place where they have no value) are accepted.
public sealed record RelocationRequest(ProductGroup Target, IReadOnlyList<DefinitionRef> Definitions, IReadOnlyList<DefinitionRef>? ConfirmedMerges = null, bool AcceptIncomplete = false);

public enum RelocationAction
{
    // The definition changes owner; no definition of the same name is met.
    Move = 1,
    // The definition is absorbed by an existing one of the same name at the target or above it.
    Merge = 2,
    // The definition moves to the target and the definitions of the same name below it (in the sibling branches) are absorbed by it.
    Absorb = 3,
    // A definition of the same name is met and no decision was given yet.
    NeedsDecision = 4,
    // The relocation cannot be done (see the note).
    Blocked = 5
}

public sealed record ValueMapping(string Value, bool ExistsAtSurvivor, int Products);

public sealed record RelocationItem(DefinitionRef Ref, string Name, RelocationAction Action, string From, string To, IReadOnlyList<string> Absorbs,
    IReadOnlyList<ValueMapping> Values, int Products, string Note);

public sealed record IncompleteProduct(int ProductId, string Name);

public sealed record RelocationPreview(IReadOnlyList<RelocationItem> Items, IReadOnlyList<string> Blocks, IReadOnlyList<IncompleteProduct> Incomplete)
{
    public bool NeedsDecision => Items.Any(item => item.Action == RelocationAction.NeedsDecision);
    // Nothing blocks the relocation and every clash has a decision (the incomplete products still need the confirmation).
    public bool CanApply => Blocks.Count == 0 && !NeedsDecision && Items.All(item => item.Action != RelocationAction.Blocked) && Items.Count > 0;
    public bool NeedsConfirmation => Incomplete.Count > 0;
}

public interface IDefinitionRelocation
{
    Task<RelocationPreview> PreviewAsync(RelocationRequest request, CancellationToken cancellationToken = default);
    // Administrators only; throws when the preview has blocks, an undecided clash, or incomplete products that were not accepted.
    Task<RelocationPreview> ApplyAsync(RelocationRequest request, string reason, CancellationToken cancellationToken = default);
}

public static class RelocationMessages
{
    public const string NothingToDo = "Alege cel puțin o definiție de mutat.";
    public static string Undecided(string name) => $"«{name}» există deja la destinație sau sub ea: alege îmbinarea sau renunță la mutare.";
    public static string NotAccepted(int count) => $"{count} produse devin incomplete și rămân blocate la mișcări de stoc până li se aleg valorile. Confirmă explicit mutarea.";
}

internal sealed record SnapValue(int Id, string Value, string Key, int Products);
internal sealed record SnapParameter(int Id, string Name, string Key, string Unit, ParameterKind Kind, int Owner, int? FamilyId, bool Archived, int Products, IReadOnlyList<SnapValue> Values);
internal sealed record SnapFamily(int Id, string Name, string Key, int Owner, bool Archived);
internal sealed record SnapTag(int Id, string Name, string Key, int Owner, bool Archived, int Products);

// Owners are numbers: a subcategory id, or the negated id of a category.
internal sealed record DefSnapshot(IReadOnlyDictionary<int, int?> Parents, IReadOnlyDictionary<int, int> CategoryOfSubcategory, IReadOnlyDictionary<int, string> OwnerNames,
    IReadOnlyList<SnapParameter> Parameters, IReadOnlyList<SnapFamily> Families, IReadOnlyList<SnapTag> Tags)
{
    public string Name(int owner) => OwnerNames.TryGetValue(owner, out var name) ? (owner < 0 ? $"categoria {name}" : $"„{name}”") : owner.ToString(System.Globalization.CultureInfo.InvariantCulture);

    // The owner and everything above it: the ancestors and the category.
    public HashSet<int> Above(int owner) => owner < 0 ? [owner] : [.. SubcategoryAncestry.Chain(Parents, owner), -CategoryOfSubcategory[owner]];

    // Everything below the owner: the subtree of a subcategory, or every subcategory of a category.
    public HashSet<int> Below(int owner) => owner < 0
        ? [.. CategoryOfSubcategory.Where(pair => pair.Value == -owner).Select(pair => pair.Key)]
        : [.. SubcategoryAncestry.Descendants(Parents, owner)];
}

internal abstract record RelocationOp;
internal sealed record MoveOp(DefinitionKind Kind, int Id, int ToOwner) : RelocationOp;
internal sealed record MergeParameterOp(int AbsorbedId, int SurvivorId) : RelocationOp;
internal sealed record MergeTagOp(int AbsorbedId, int SurvivorId) : RelocationOp;
// A member of an absorbed family joins the surviving family (and takes its owner).
internal sealed record JoinFamilyOp(int ParameterId, int FamilyId, int ToOwner) : RelocationOp;
internal sealed record DropFamilyOp(int FamilyId) : RelocationOp;

internal sealed record RelocationPlan(IReadOnlyList<RelocationItem> Items, IReadOnlyList<string> Blocks, IReadOnlyList<RelocationOp> Operations);

internal sealed class RelocationPlanner
{
    private readonly DefSnapshot snapshot;
    private readonly int target;
    private readonly HashSet<int> above, below;
    private readonly HashSet<DefinitionRef> confirmed;
    private readonly HashSet<int> movingParameters = [], movingFamilies = [], movingTags = [];
    private readonly List<RelocationItem> items = [];
    private readonly List<string> blocks = [];
    private readonly List<RelocationOp> operations = [];

    private RelocationPlanner(DefSnapshot snapshot, RelocationRequest request, int target)
    {
        this.snapshot = snapshot;
        this.target = target;
        above = snapshot.Above(target);
        below = snapshot.Below(target);
        confirmed = [.. request.ConfirmedMerges ?? []];
        foreach (var reference in request.Definitions)
            switch (reference.Kind)
            {
                case DefinitionKind.Parameter: movingParameters.Add(reference.Id); break;
                case DefinitionKind.Tag: movingTags.Add(reference.Id); break;
                case DefinitionKind.Family:
                    movingFamilies.Add(reference.Id);
                    foreach (var member in snapshot.Parameters.Where(item => item.FamilyId == reference.Id)) movingParameters.Add(member.Id);
                    break;
            }
    }

    public static RelocationPlan Plan(DefSnapshot snapshot, RelocationRequest request, int target)
    {
        var planner = new RelocationPlanner(snapshot, request, target);
        if (request.Definitions.Count == 0) planner.blocks.Add(RelocationMessages.NothingToDo);
        foreach (var reference in request.Definitions.Distinct())
            switch (reference.Kind)
            {
                case DefinitionKind.Parameter: planner.PlanParameter(reference); break;
                case DefinitionKind.Tag: planner.PlanTag(reference); break;
                case DefinitionKind.Family: planner.PlanFamily(reference); break;
            }
        planner.CheckDuplicatesInRequest(request);
        return new RelocationPlan(planner.items, planner.blocks, planner.operations);
    }

    // Two requested definitions of the same kind and name would meet each other at the target.
    private void CheckDuplicatesInRequest(RelocationRequest request)
    {
        foreach (var group in request.Definitions.Where(item => item.Kind == DefinitionKind.Parameter).Select(item => snapshot.Parameters.FirstOrDefault(parameter => parameter.Id == item.Id))
                     .OfType<SnapParameter>().GroupBy(parameter => parameter.Key).Where(group => group.Count() > 1))
            blocks.Add($"Parametrul «{group.First().Name}» apare de două ori în cerere.");
        foreach (var group in request.Definitions.Where(item => item.Kind == DefinitionKind.Tag).Select(item => snapshot.Tags.FirstOrDefault(tag => tag.Id == item.Id))
                     .OfType<SnapTag>().GroupBy(tag => tag.Key).Where(group => group.Count() > 1))
            blocks.Add($"Eticheta «{group.First().Name}» apare de două ori în cerere.");
    }

    private void Block(DefinitionRef reference, string name, string from, string note)
    {
        items.Add(new RelocationItem(reference, name, RelocationAction.Blocked, from, snapshot.Name(target), [], [], 0, note));
        blocks.Add(note);
    }

    private bool InScope(int owner) => above.Contains(owner) || below.Contains(owner);

    // ---- parameters ----------------------------------------------------------------------------------------------------------

    private List<SnapParameter> ClashesOf(SnapParameter parameter, ISet<int> ignore) =>
        snapshot.Parameters.Where(other => other.Id != parameter.Id && other.Key == parameter.Key && !movingParameters.Contains(other.Id) && !ignore.Contains(other.Id) && InScope(other.Owner)).ToList();

    private static string Incompatible(SnapParameter moved, SnapParameter other, string where) =>
        moved.Kind != other.Kind
            ? $"Parametrul «{moved.Name}» are tip diferit față de cel de la {where}; redenumește unul dintre ele."
            : $"Parametrul «{moved.Name}» are altă unitate de măsură ({(moved.Unit.Length == 0 ? "fără" : moved.Unit)}) față de cel de la {where} ({(other.Unit.Length == 0 ? "fără" : other.Unit)}); redenumește unul dintre ele.";

    private static bool Compatible(SnapParameter first, SnapParameter second) => first.Kind == second.Kind && first.Unit == second.Unit;

    private List<ValueMapping> MapValues(IEnumerable<SnapParameter> absorbed, SnapParameter survivor)
    {
        var known = survivor.Values.Select(value => value.Key).ToHashSet();
        var mapping = new List<ValueMapping>();
        foreach (var parameter in absorbed)
            foreach (var value in parameter.Values)
            {
                var exists = known.Contains(value.Key);
                if (!exists) known.Add(value.Key);
                mapping.Add(new ValueMapping(ProductParameterRules.Display(parameter.Unit, value.Value), exists, value.Products));
            }
        return mapping;
    }

    private void PlanParameter(DefinitionRef reference)
    {
        var parameter = snapshot.Parameters.FirstOrDefault(item => item.Id == reference.Id);
        if (parameter is null) { Block(reference, "?", "", "Un parametru ales nu mai există. Actualizează lista și reia operația."); return; }
        var from = snapshot.Name(parameter.Owner);
        if (parameter.Archived) { Block(reference, parameter.Name, from, $"Parametrul «{parameter.Name}» este arhivat: restaurează-l sau șterge-l înainte."); return; }
        if (parameter.FamilyId is not null) { Block(reference, parameter.Name, from, $"Parametrul «{parameter.Name}» face parte dintr-o familie: mută familia, nu parametrul."); return; }
        if (parameter.Owner == target) { Block(reference, parameter.Name, from, $"Parametrul «{parameter.Name}» aparține deja nodului țintă."); return; }
        PlanSingleParameter(reference, parameter, from, new HashSet<int>(), addItem: true);
    }

    // Moves or merges a standalone parameter; returns false when it is blocked. Used for parameters of the request and, with addItem false, for the members of a family.
    private bool PlanSingleParameter(DefinitionRef reference, SnapParameter parameter, string from, ISet<int> ignore, bool addItem)
    {
        var clashes = ClashesOf(parameter, ignore);
        var archivedClash = clashes.FirstOrDefault(item => item.Archived);
        if (archivedClash is not null)
        {
            Block(reference, parameter.Name, from, $"Parametrul «{parameter.Name}» există arhivat la {snapshot.Name(archivedClash.Owner)}: restaurează-l sau șterge-l înainte.");
            return false;
        }
        foreach (var clash in clashes)
        {
            if (!Compatible(parameter, clash)) { Block(reference, parameter.Name, from, Incompatible(parameter, clash, snapshot.Name(clash.Owner))); return false; }
            if (clash.FamilyId is not null)
            {
                Block(reference, parameter.Name, from, $"Parametrul «{parameter.Name}» există la {snapshot.Name(clash.Owner)} într-o familie, iar aici este separat (familie parțială); redenumește unul dintre ele.");
                return false;
            }
        }
        var aboveClash = clashes.FirstOrDefault(item => above.Contains(item.Owner));
        var belowClashes = clashes.Where(item => below.Contains(item.Owner)).ToList();
        var to = snapshot.Name(target);
        if (clashes.Count == 0)
        {
            operations.Add(new MoveOp(DefinitionKind.Parameter, parameter.Id, target));
            if (addItem) items.Add(new RelocationItem(reference, parameter.Name, RelocationAction.Move, from, to, [], [], parameter.Products, "Se mută cu valorile și legăturile produselor."));
            return true;
        }
        if (!confirmed.Contains(reference))
        {
            items.Add(new RelocationItem(reference, parameter.Name, RelocationAction.NeedsDecision, from, to, clashes.Select(item => $"{snapshot.Name(item.Owner)}: {item.Name}").ToList(), [], parameter.Products,
                RelocationMessages.Undecided(parameter.Name)));
            return true;
        }
        if (aboveClash is not null)
        {
            operations.Add(new MergeParameterOp(parameter.Id, aboveClash.Id));
            foreach (var other in belowClashes) operations.Add(new MergeParameterOp(other.Id, aboveClash.Id));
            if (addItem)
                items.Add(new RelocationItem(reference, parameter.Name, RelocationAction.Merge, from, snapshot.Name(aboveClash.Owner), belowClashes.Select(item => $"{snapshot.Name(item.Owner)}: {item.Name}").ToList(),
                    MapValues([parameter, .. belowClashes], aboveClash), parameter.Products + belowClashes.Sum(item => item.Products),
                    $"Se îmbină cu parametrul de la {snapshot.Name(aboveClash.Owner)}; valorile se unesc, produsele își păstrează valorile."));
            return true;
        }
        operations.Add(new MoveOp(DefinitionKind.Parameter, parameter.Id, target));
        foreach (var other in belowClashes) operations.Add(new MergeParameterOp(other.Id, parameter.Id));
        if (addItem)
            items.Add(new RelocationItem(reference, parameter.Name, RelocationAction.Absorb, from, to, belowClashes.Select(item => $"{snapshot.Name(item.Owner)}: {item.Name}").ToList(),
                MapValues(belowClashes, parameter), parameter.Products + belowClashes.Sum(item => item.Products),
                "Se mută, iar parametrele cu același nume din ramurile de sub nod se îmbină în el."));
        return true;
    }

    // ---- tags ----------------------------------------------------------------------------------------------------------------

    private void PlanTag(DefinitionRef reference)
    {
        var tag = snapshot.Tags.FirstOrDefault(item => item.Id == reference.Id);
        if (tag is null) { Block(reference, "?", "", "O etichetă aleasă nu mai există. Actualizează lista și reia operația."); return; }
        var from = snapshot.Name(tag.Owner);
        if (tag.Archived) { Block(reference, tag.Name, from, $"Eticheta «{tag.Name}» este arhivată: restaurează-o sau șterge-o înainte."); return; }
        if (tag.Owner == target) { Block(reference, tag.Name, from, $"Eticheta «{tag.Name}» aparține deja nodului țintă."); return; }
        var clashes = snapshot.Tags.Where(other => other.Id != tag.Id && other.Key == tag.Key && !movingTags.Contains(other.Id) && InScope(other.Owner)).ToList();
        if (clashes.FirstOrDefault(item => item.Archived) is { } archived)
        {
            Block(reference, tag.Name, from, $"Eticheta «{tag.Name}» există arhivată la {snapshot.Name(archived.Owner)}: restaurează-o sau șterge-o înainte.");
            return;
        }
        var to = snapshot.Name(target);
        if (clashes.Count == 0)
        {
            operations.Add(new MoveOp(DefinitionKind.Tag, tag.Id, target));
            items.Add(new RelocationItem(reference, tag.Name, RelocationAction.Move, from, to, [], [], tag.Products, "Se mută cu On/Off-ul produselor."));
            return;
        }
        if (!confirmed.Contains(reference))
        {
            items.Add(new RelocationItem(reference, tag.Name, RelocationAction.NeedsDecision, from, to, clashes.Select(item => $"{snapshot.Name(item.Owner)}: {item.Name}").ToList(), [], tag.Products, RelocationMessages.Undecided(tag.Name)));
            return;
        }
        var aboveClash = clashes.FirstOrDefault(item => above.Contains(item.Owner));
        var belowClashes = clashes.Where(item => below.Contains(item.Owner)).ToList();
        if (aboveClash is not null)
        {
            operations.Add(new MergeTagOp(tag.Id, aboveClash.Id));
            foreach (var other in belowClashes) operations.Add(new MergeTagOp(other.Id, aboveClash.Id));
            items.Add(new RelocationItem(reference, tag.Name, RelocationAction.Merge, from, snapshot.Name(aboveClash.Owner), belowClashes.Select(item => $"{snapshot.Name(item.Owner)}: {item.Name}").ToList(), [],
                tag.Products + belowClashes.Sum(item => item.Products), $"Se îmbină cu eticheta de la {snapshot.Name(aboveClash.Owner)}; produsele care o au On o păstrează."));
            return;
        }
        operations.Add(new MoveOp(DefinitionKind.Tag, tag.Id, target));
        foreach (var other in belowClashes) operations.Add(new MergeTagOp(other.Id, tag.Id));
        items.Add(new RelocationItem(reference, tag.Name, RelocationAction.Absorb, from, to, belowClashes.Select(item => $"{snapshot.Name(item.Owner)}: {item.Name}").ToList(), [],
            tag.Products + belowClashes.Sum(item => item.Products), "Se mută, iar etichetele cu același nume din ramurile de sub nod se îmbină în ea."));
    }

    // ---- families -------------------------------------------------------------------------------------------------------------

    private void PlanFamily(DefinitionRef reference)
    {
        var family = snapshot.Families.FirstOrDefault(item => item.Id == reference.Id);
        if (family is null) { Block(reference, "?", "", "O familie aleasă nu mai există. Actualizează lista și reia operația."); return; }
        var from = snapshot.Name(family.Owner);
        if (family.Archived) { Block(reference, family.Name, from, $"Familia «{family.Name}» este arhivată: restaurează-o sau șterge-o înainte."); return; }
        if (family.Owner == target) { Block(reference, family.Name, from, $"Familia «{family.Name}» aparține deja nodului țintă."); return; }
        var members = snapshot.Parameters.Where(item => item.FamilyId == family.Id).ToList();
        var to = snapshot.Name(target);
        var memberIds = members.Select(item => item.Id).ToHashSet();

        // What the family meets: parameters of its members' names and families of its own name, at the target, above it and below it.
        var memberClashes = new List<(SnapParameter Member, SnapParameter Other)>();
        foreach (var member in members)
            foreach (var other in ClashesOf(member, memberIds)) memberClashes.Add((member, other));
        var familyClashes = snapshot.Families.Where(other => other.Id != family.Id && other.Key == family.Key && !movingFamilies.Contains(other.Id) && InScope(other.Owner)).ToList();

        if (memberClashes.Any(item => item.Other.Archived))
        {
            var archived = memberClashes.First(item => item.Other.Archived);
            Block(reference, family.Name, from, $"Parametrul «{archived.Member.Name}» există arhivat la {snapshot.Name(archived.Other.Owner)}: restaurează-l sau șterge-l înainte.");
            return;
        }
        if (familyClashes.Any(item => item.Archived))
        {
            Block(reference, family.Name, from, $"Familia «{family.Name}» există arhivată la {snapshot.Name(familyClashes.First(item => item.Archived).Owner)}: restaurează-o sau șterge-o înainte.");
            return;
        }
        foreach (var (member, other) in memberClashes)
        {
            if (!Compatible(member, other)) { Block(reference, family.Name, from, Incompatible(member, other, snapshot.Name(other.Owner))); return; }
            if (other.FamilyId is null)
            {
                Block(reference, family.Name, from, $"Parametrul «{member.Name}» al familiei există la {snapshot.Name(other.Owner)} ca parametru separat (familie parțială); redenumește unul dintre ele.");
                return;
            }
        }
        var otherFamilies = memberClashes.Select(item => item.Other.FamilyId!.Value).Concat(familyClashes.Select(item => item.Id)).Distinct()
            .Select(id => snapshot.Families.First(item => item.Id == id)).ToList();
        var aboveFamilies = otherFamilies.Where(item => above.Contains(item.Owner)).ToList();
        var belowFamilies = otherFamilies.Where(item => below.Contains(item.Owner)).ToList();
        if (aboveFamilies.Count > 1)
        {
            Block(reference, family.Name, from, $"Familia «{family.Name}» se potrivește cu mai multe familii de la destinație sau de deasupra ei; rezolvă mai întâi suprapunerea.");
            return;
        }
        // A family of the same name whose parameters do not meet any of ours has other content: merging it would change what a product must choose.
        if (familyClashes.FirstOrDefault(item => memberClashes.All(pair => pair.Other.FamilyId != item.Id)) is { } stranger)
        {
            Block(reference, family.Name, from, $"La {snapshot.Name(stranger.Owner)} există o familie cu același nume, dar cu alți parametri; redenumește una dintre ele.");
            return;
        }
        if (otherFamilies.Count == 0)
        {
            operations.Add(new MoveOp(DefinitionKind.Family, family.Id, target));
            items.Add(new RelocationItem(reference, family.Name, RelocationAction.Move, from, to, [], [], members.Sum(item => item.Products), "Familia se mută cu parametrii și valorile ei."));
            return;
        }
        if (!confirmed.Contains(reference))
        {
            items.Add(new RelocationItem(reference, family.Name, RelocationAction.NeedsDecision, from, to, otherFamilies.Select(item => $"{snapshot.Name(item.Owner)}: {item.Name}").ToList(), [], members.Sum(item => item.Products),
                RelocationMessages.Undecided(family.Name)));
            return;
        }
        var survivor = aboveFamilies.FirstOrDefault();
        var absorbedFamilies = survivor is null ? belowFamilies : [.. belowFamilies];
        var survivorOwner = survivor?.Owner ?? target;
        if (survivor is null) operations.Add(new MoveOp(DefinitionKind.Family, family.Id, target));
        var survivorId = survivor?.Id ?? family.Id;
        var survivorMembers = (survivor is null ? members : snapshot.Parameters.Where(item => item.FamilyId == survivor.Id).ToList()).ToList();
        var sources = new List<(SnapFamily Family, List<SnapParameter> Members)>();
        if (survivor is not null) sources.Add((family, members));
        foreach (var absorbed in absorbedFamilies) sources.Add((absorbed, snapshot.Parameters.Where(item => item.FamilyId == absorbed.Id).ToList()));
        var absorbedParameters = new List<SnapParameter>();
        var ignore = new HashSet<int>(sources.SelectMany(source => source.Members.Select(member => member.Id)).Concat(survivorMembers.Select(member => member.Id)));
        foreach (var (source, sourceMembers) in sources)
        {
            foreach (var member in sourceMembers)
            {
                var match = survivorMembers.FirstOrDefault(item => item.Key == member.Key && item.Id != member.Id);
                if (match is not null)
                {
                    if (!Compatible(member, match)) { Block(reference, family.Name, from, Incompatible(member, match, snapshot.Name(match.Owner))); return; }
                    operations.Add(new MergeParameterOp(member.Id, match.Id));
                    absorbedParameters.Add(member);
                    continue;
                }
                // The member joins the surviving family and takes its owner: its name must be free in the new scope.
                if (snapshot.Parameters.FirstOrDefault(item => item.Id != member.Id && item.Key == member.Key && !ignore.Contains(item.Id) && !movingParameters.Contains(item.Id) && InScope(item.Owner)) is { } taken)
                {
                    Block(reference, family.Name, from, $"Parametrul «{member.Name}» ar ajunge în familia «{(survivor ?? family).Name}», dar există deja la {snapshot.Name(taken.Owner)}; redenumește unul dintre ele.");
                    return;
                }
                operations.Add(new JoinFamilyOp(member.Id, survivorId, survivorOwner));
                survivorMembers.Add(member with { FamilyId = survivorId });
            }
            if (source.Id != survivorId) operations.Add(new DropFamilyOp(source.Id));
        }
        items.Add(new RelocationItem(reference, family.Name, survivor is null ? RelocationAction.Absorb : RelocationAction.Merge, from, survivor is null ? to : snapshot.Name(survivor.Owner),
            absorbedFamilies.Select(item => $"{snapshot.Name(item.Owner)}: {item.Name}").ToList(), MapFamilyValues(absorbedParameters, survivorMembers), members.Sum(item => item.Products) + absorbedFamilies.Sum(item => snapshot.Parameters.Where(p => p.FamilyId == item.Id).Sum(p => p.Products)),
            survivor is null ? "Familia se mută, iar familiile cu același conținut din ramurile de sub nod se îmbină în ea." : "Familia se îmbină cu cea de la destinație: parametrii cu același nume se unesc, ceilalți intră în familie."));
    }

    private List<ValueMapping> MapFamilyValues(IEnumerable<SnapParameter> absorbed, IEnumerable<SnapParameter> survivors)
    {
        var mapping = new List<ValueMapping>();
        foreach (var parameter in absorbed)
        {
            var survivor = survivors.FirstOrDefault(item => item.Key == parameter.Key && item.Id != parameter.Id);
            if (survivor is not null) mapping.AddRange(MapValues([parameter], survivor));
        }
        return mapping;
    }
}

// ---- moving a branch while keeping its definitions (stage 6) ----------------------------------------------------------------------
// A branch that moves leaves the ancestors it inherited definitions from. Those definitions stay with the old parent (its other children still use them), so each is copied
// onto the moved node (and the products of the branch are relinked to the copy); when a definition of the same name is met at the new ancestors the copy is merged into it instead.
// A definition of the branch itself that repeats one of the new ancestors is merged into the ancestor's. The new ancestors' definitions the products lack make them incomplete.
public sealed class MoveNeedsVerificationException() : ProductOperationException("Mutarea schimbă parametrii, familiile sau etichetele moștenite de ramură: verifică și confirmă efectul.");

public sealed record BranchMoveChoices(IReadOnlyList<DefinitionRef>? ConfirmedMerges = null, bool AcceptIncomplete = false, string Reason = "");

public enum BranchMoveAction
{
    // The definition (inherited from a parent the branch leaves) is copied onto the moved node, with its values; the products of the branch point to the copy.
    Copy = 1,
    // The inherited definition is merged into the one of the same name met at the new ancestors.
    CopyMerged = 2,
    // A definition of the branch is merged into the one of the same name at the new ancestors.
    OwnMerged = 3,
    NeedsDecision = 4,
    Blocked = 5
}

public sealed record BranchMoveItem(DefinitionRef Ref, string Name, BranchMoveAction Action, string From, string To, IReadOnlyList<ValueMapping> Values, string Note);

public sealed record BranchMovePreview(IReadOnlyList<BranchMoveItem> Items, IReadOnlyList<string> Blocks, IReadOnlyList<IncompleteProduct> Incomplete)
{
    public bool NeedsDecision => Items.Any(item => item.Action == BranchMoveAction.NeedsDecision);
    public bool CanApply => Blocks.Count == 0 && !NeedsDecision && Items.All(item => item.Action != BranchMoveAction.Blocked);
    public bool NeedsConfirmation => Incomplete.Count > 0;
    // Nothing about the definitions changes: the move is the plain one the drag started.
    public bool Plain => Items.Count == 0 && Blocks.Count == 0 && Incomplete.Count == 0;
}

internal sealed record CopyParameterOp(int SourceId, int NodeId) : RelocationOp;
internal sealed record CopyFamilyOp(int SourceId, int NodeId) : RelocationOp;
internal sealed record RelinkParameterOp(int SourceId, int SurvivorId) : RelocationOp;
internal sealed record CopyTagOp(int SourceId, int NodeId) : RelocationOp;
internal sealed record RelinkTagOp(int SourceId, int SurvivorId) : RelocationOp;

internal sealed record BranchMovePlan(IReadOnlyList<BranchMoveItem> Items, IReadOnlyList<string> Blocks, IReadOnlyList<RelocationOp> Operations);

internal static class BranchMovePlanner
{
    // node: the subcategory that moves; newParent: its new parent (null = first level); newCategory: the category it ends in.
    public static BranchMovePlan Plan(DefSnapshot snapshot, int node, int? newParent, int newCategory, IReadOnlyList<DefinitionRef>? confirmedMerges)
    {
        var confirmed = (confirmedMerges ?? []).ToHashSet();
        var items = new List<BranchMoveItem>();
        var blocks = new List<string>();
        var operations = new List<RelocationOp>();
        var subtree = new HashSet<int>([node, .. SubcategoryAncestry.Descendants(snapshot.Parents, node)]);
        var oldOwners = new HashSet<int>([.. SubcategoryAncestry.Chain(snapshot.Parents, node).Where(id => id != node), -snapshot.CategoryOfSubcategory[node]]);
        var newOwners = new HashSet<int>([.. newParent is { } parent ? SubcategoryAncestry.Chain(snapshot.Parents, parent) : [], -newCategory]);
        var lost = oldOwners.Where(owner => !newOwners.Contains(owner)).ToHashSet();
        var gained = newOwners.Where(owner => !oldOwners.Contains(owner)).ToHashSet();
        var from = snapshot.Name(node);
        var to = newParent is { } p ? snapshot.Name(p) : snapshot.Name(-newCategory);

        var gainedParameters = snapshot.Parameters.Where(item => gained.Contains(item.Owner)).ToList();
        var gainedTags = snapshot.Tags.Where(item => gained.Contains(item.Owner)).ToList();
        var gainedFamilies = snapshot.Families.Where(item => gained.Contains(item.Owner)).ToList();

        void Block(DefinitionRef reference, string name, string where, string note)
        {
            items.Add(new BranchMoveItem(reference, name, BranchMoveAction.Blocked, where, to, [], note));
            blocks.Add(note);
        }

        List<ValueMapping> Map(SnapParameter source, SnapParameter survivor)
        {
            var known = survivor.Values.Select(value => value.Key).ToHashSet();
            return source.Values.Select(value => new ValueMapping(ProductParameterRules.Display(source.Unit, value.Value), known.Contains(value.Key), value.Products)).ToList();
        }

        // The inherited definitions the branch leaves: copied onto the node, or merged into a namesake of the new ancestors.
        foreach (var family in snapshot.Families.Where(item => lost.Contains(item.Owner) && !item.Archived))
        {
            var reference = new DefinitionRef(DefinitionKind.Family, family.Id);
            var members = snapshot.Parameters.Where(item => item.FamilyId == family.Id).ToList();
            if (gainedFamilies.FirstOrDefault(item => item.Key == family.Key) is { } sameFamily)
            { Block(reference, family.Name, snapshot.Name(family.Owner), $"Familia «{family.Name}» moștenită există și la noii părinți ({snapshot.Name(sameFamily.Owner)}); redenumește una dintre ele înainte de mutare."); continue; }
            if (members.FirstOrDefault(member => gainedParameters.Any(other => other.Key == member.Key)) is { } clashing)
            { Block(reference, family.Name, snapshot.Name(family.Owner), $"Parametrul «{clashing.Name}» al familiei moștenite există și la noii părinți; redenumește unul dintre ele înainte de mutare."); continue; }
            operations.Add(new CopyFamilyOp(family.Id, node));
            items.Add(new BranchMoveItem(reference, family.Name, BranchMoveAction.Copy, snapshot.Name(family.Owner), from, [], "Familia se copiază pe nodul mutat, cu parametrii și valorile ei; produsele ramurii o păstrează."));
        }
        foreach (var parameter in snapshot.Parameters.Where(item => lost.Contains(item.Owner) && !item.Archived && item.FamilyId is null))
        {
            var reference = new DefinitionRef(DefinitionKind.Parameter, parameter.Id);
            var clash = gainedParameters.FirstOrDefault(item => item.Key == parameter.Key);
            if (clash is null)
            {
                operations.Add(new CopyParameterOp(parameter.Id, node));
                items.Add(new BranchMoveItem(reference, parameter.Name, BranchMoveAction.Copy, snapshot.Name(parameter.Owner), from, [], "Parametrul se copiază pe nodul mutat, cu valorile lui; produsele ramurii păstrează valorile."));
                continue;
            }
            if (clash.Archived) { Block(reference, parameter.Name, snapshot.Name(parameter.Owner), $"Parametrul «{parameter.Name}» există arhivat la {snapshot.Name(clash.Owner)}: restaurează-l sau șterge-l înainte."); continue; }
            if (clash.Kind != parameter.Kind || clash.Unit != parameter.Unit) { Block(reference, parameter.Name, snapshot.Name(parameter.Owner), $"Parametrul «{parameter.Name}» moștenit diferă ca tip sau unitate de cel de la {snapshot.Name(clash.Owner)}; redenumește unul dintre ele."); continue; }
            if (clash.FamilyId is not null) { Block(reference, parameter.Name, snapshot.Name(parameter.Owner), $"Parametrul «{parameter.Name}» există la {snapshot.Name(clash.Owner)} într-o familie (familie parțială); redenumește unul dintre ele."); continue; }
            if (!confirmed.Contains(reference))
            { items.Add(new BranchMoveItem(reference, parameter.Name, BranchMoveAction.NeedsDecision, snapshot.Name(parameter.Owner), snapshot.Name(clash.Owner), [], $"Parametrul «{parameter.Name}» există și la noii părinți: îmbin-o sau renunță la mutare.")); continue; }
            operations.Add(new RelinkParameterOp(parameter.Id, clash.Id));
            items.Add(new BranchMoveItem(reference, parameter.Name, BranchMoveAction.CopyMerged, snapshot.Name(parameter.Owner), snapshot.Name(clash.Owner), Map(parameter, clash), "Produsele ramurii trec la parametrul de la noii părinți; valorile se unesc."));
        }
        foreach (var tag in snapshot.Tags.Where(item => lost.Contains(item.Owner) && !item.Archived))
        {
            var reference = new DefinitionRef(DefinitionKind.Tag, tag.Id);
            var clash = gainedTags.FirstOrDefault(item => item.Key == tag.Key);
            if (clash is null)
            {
                operations.Add(new CopyTagOp(tag.Id, node));
                items.Add(new BranchMoveItem(reference, tag.Name, BranchMoveAction.Copy, snapshot.Name(tag.Owner), from, [], "Eticheta se copiază pe nodul mutat; produsele ramurii o păstrează."));
                continue;
            }
            if (clash.Archived) { Block(reference, tag.Name, snapshot.Name(tag.Owner), $"Eticheta «{tag.Name}» există arhivată la {snapshot.Name(clash.Owner)}: restaurează-o sau șterge-o înainte."); continue; }
            if (!confirmed.Contains(reference))
            { items.Add(new BranchMoveItem(reference, tag.Name, BranchMoveAction.NeedsDecision, snapshot.Name(tag.Owner), snapshot.Name(clash.Owner), [], $"Eticheta «{tag.Name}» există și la noii părinți: îmbin-o sau renunță la mutare.")); continue; }
            operations.Add(new RelinkTagOp(tag.Id, clash.Id));
            items.Add(new BranchMoveItem(reference, tag.Name, BranchMoveAction.CopyMerged, snapshot.Name(tag.Owner), snapshot.Name(clash.Owner), [], "Produsele ramurii cu eticheta On trec la eticheta de la noii părinți."));
        }

        // The families of the branch that repeat a family of the new ancestors (what a branch gets back when it returns under the parent its copies came from): the user decides to merge them,
        // the parameters of the same name are merged one into the other, the others join the family above.
        var mergedFamilyMembers = new HashSet<int>();
        foreach (var family in snapshot.Families.Where(item => subtree.Contains(item.Owner) && !item.Archived))
        {
            if (gainedFamilies.FirstOrDefault(item => item.Key == family.Key) is not { } survivor) continue;
            var reference = new DefinitionRef(DefinitionKind.Family, family.Id);
            var members = snapshot.Parameters.Where(item => item.FamilyId == family.Id).ToList();
            foreach (var member in members) mergedFamilyMembers.Add(member.Id);
            if (survivor.Archived) { Block(reference, family.Name, snapshot.Name(family.Owner), $"Familia «{family.Name}» există arhivată la {snapshot.Name(survivor.Owner)}: restaurează-o sau șterge-o înainte."); continue; }
            var survivorMembers = snapshot.Parameters.Where(item => item.FamilyId == survivor.Id).ToList();
            var familyOperations = new List<RelocationOp>();
            var mappings = new List<ValueMapping>();
            var failed = false;
            foreach (var member in members)
            {
                var match = survivorMembers.FirstOrDefault(item => item.Key == member.Key);
                if (match is not null)
                {
                    if (match.Archived || member.Archived || match.Kind != member.Kind || match.Unit != member.Unit)
                    { Block(reference, family.Name, snapshot.Name(family.Owner), $"Parametrul «{member.Name}» al familiei «{family.Name}» diferă ca tip sau unitate de cel de la {snapshot.Name(match.Owner)} (sau este arhivat); redenumește unul dintre ele."); failed = true; break; }
                    familyOperations.Add(new MergeParameterOp(member.Id, match.Id));
                    mappings.AddRange(Map(member, match));
                    continue;
                }
                if (gainedParameters.FirstOrDefault(item => item.Key == member.Key) is { } taken)
                { Block(reference, family.Name, snapshot.Name(family.Owner), $"Parametrul «{member.Name}» al familiei «{family.Name}» ar intra în familia de la {snapshot.Name(survivor.Owner)}, dar există deja la {snapshot.Name(taken.Owner)}; redenumește unul dintre ele."); failed = true; break; }
                familyOperations.Add(new JoinFamilyOp(member.Id, survivor.Id, survivor.Owner));
            }
            if (failed) continue;
            if (!confirmed.Contains(reference))
            { items.Add(new BranchMoveItem(reference, family.Name, BranchMoveAction.NeedsDecision, snapshot.Name(family.Owner), snapshot.Name(survivor.Owner), [], $"Familia «{family.Name}» a ramurii există și la noii părinți: îmbin-o sau renunță la mutare.")); continue; }
            operations.AddRange(familyOperations);
            operations.Add(new DropFamilyOp(family.Id));
            items.Add(new BranchMoveItem(reference, family.Name, BranchMoveAction.OwnMerged, snapshot.Name(family.Owner), snapshot.Name(survivor.Owner), mappings, "Familia ramurii se îmbină cu cea de la noii părinți: parametrii cu același nume se unesc, ceilalți intră în familie."));
        }

        // The definitions of the branch that repeat the ones the new ancestors have.
        foreach (var parameter in snapshot.Parameters.Where(item => subtree.Contains(item.Owner) && !item.Archived))
        {
            if (mergedFamilyMembers.Contains(parameter.Id)) continue;
            if (gainedParameters.FirstOrDefault(item => item.Key == parameter.Key) is not { } clash) continue;
            var reference = new DefinitionRef(DefinitionKind.Parameter, parameter.Id);
            if (parameter.FamilyId is not null) { Block(reference, parameter.Name, snapshot.Name(parameter.Owner), $"Parametrul «{parameter.Name}» face parte dintr-o familie a ramurii și există la {snapshot.Name(clash.Owner)}; redenumește unul dintre ele."); continue; }
            if (clash.Archived) { Block(reference, parameter.Name, snapshot.Name(parameter.Owner), $"Parametrul «{parameter.Name}» există arhivat la {snapshot.Name(clash.Owner)}: restaurează-l sau șterge-l înainte."); continue; }
            if (clash.Kind != parameter.Kind || clash.Unit != parameter.Unit || clash.FamilyId is not null)
            { Block(reference, parameter.Name, snapshot.Name(parameter.Owner), $"Parametrul «{parameter.Name}» al ramurii diferă ca tip sau unitate de cel de la {snapshot.Name(clash.Owner)} (sau acela e într-o familie); redenumește unul dintre ele."); continue; }
            if (!confirmed.Contains(reference))
            { items.Add(new BranchMoveItem(reference, parameter.Name, BranchMoveAction.NeedsDecision, snapshot.Name(parameter.Owner), snapshot.Name(clash.Owner), [], $"Parametrul «{parameter.Name}» al ramurii există și la noii părinți: îmbin-o sau renunță la mutare.")); continue; }
            operations.Add(new MergeParameterOp(parameter.Id, clash.Id));
            items.Add(new BranchMoveItem(reference, parameter.Name, BranchMoveAction.OwnMerged, snapshot.Name(parameter.Owner), snapshot.Name(clash.Owner), Map(parameter, clash), "Parametrul ramurii se îmbină cu cel de la noii părinți; valorile se unesc."));
        }

        foreach (var tag in snapshot.Tags.Where(item => subtree.Contains(item.Owner) && !item.Archived))
        {
            if (gainedTags.FirstOrDefault(item => item.Key == tag.Key) is not { } clash) continue;
            var reference = new DefinitionRef(DefinitionKind.Tag, tag.Id);
            if (clash.Archived) { Block(reference, tag.Name, snapshot.Name(tag.Owner), $"Eticheta «{tag.Name}» există arhivată la {snapshot.Name(clash.Owner)}: restaurează-o sau șterge-o înainte."); continue; }
            if (!confirmed.Contains(reference))
            { items.Add(new BranchMoveItem(reference, tag.Name, BranchMoveAction.NeedsDecision, snapshot.Name(tag.Owner), snapshot.Name(clash.Owner), [], $"Eticheta «{tag.Name}» a ramurii există și la noii părinți: îmbin-o sau renunță la mutare.")); continue; }
            operations.Add(new MergeTagOp(tag.Id, clash.Id));
            items.Add(new BranchMoveItem(reference, tag.Name, BranchMoveAction.OwnMerged, snapshot.Name(tag.Owner), snapshot.Name(clash.Owner), [], "Eticheta ramurii se îmbină cu cea de la noii părinți."));
        }
        return new BranchMovePlan(items, blocks, operations);
    }
}