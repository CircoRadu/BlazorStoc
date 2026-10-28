using Microsoft.Extensions.Configuration;
using BlazorStoc.Services;

namespace BlazorStoc.Checks;

public static partial class MariaExtendedChecks
{
    // A branch that moves keeps the definitions it inherited (stage 6): they are copied onto the moved node (or merged into the namesakes of the new ancestors), the products keep their
    // values, the products that lack what the new ancestors define are listed and need an acceptance, and the move says first what it will do.
    private static async Task BranchMoveKeepsDefinitionsAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit)
    {
        var suffix = Suffix();
        var since = DateTime.UtcNow.AddSeconds(-1);
        var products = new MariaProductRepository(configuration, admin, audit);
        var parameters = new MariaProductParameterRepository(configuration, admin);   // not journaled: other sections count these events
        var tags = new MariaProductTagRepository(configuration, admin);
        var movements = new MariaStockMovementRepository(configuration, admin);
        var today = DateOnly.FromDateTime(DateTime.Now);
        var catA = $"Ext Cop Cat A {suffix}";
        var catB = $"Ext Cop Cat B {suffix}";
        await products.CreateCategoryAsync(catA);
        await products.CreateCategoryAsync(catB);
        async Task<List<SubcategoryNode>> Tree(string category) => (await products.GetTreeAsync()).Where(item => item.Category == category).ToList();
        ProductInput Input(ProductGroup group, string model, params (SubcategoryParameter Parameter, int ValueId)[] choices) => new()
        {
            Name = model, BaseModel = model, Category = group.Category, Subcategory = group.Subcategory,
            Parameters = choices.Select(choice => new ProductParameterChoice(choice.Parameter.Id, choice.ValueId)).ToList()
        };

        // The old parent R defines a lens and a tag; its children K1 and K2 have products with them. The new parent T defines a colour the products of K1 will lack.
        var r = await products.CreateSubcategoryAsync(catA, $"Ext Cop R {suffix}");
        var t = await products.CreateSubcategoryAsync(catA, $"Ext Cop T {suffix}");
        var lens = await parameters.AddParameterAsync(r, "Lentila cop", "mm", ParameterKind.Number);
        var v28 = await parameters.AddValueAsync(lens.Id, "2.8");
        var v4 = await parameters.AddValueAsync(lens.Id, "4");
        var poe = await tags.AddTagAsync(r, "PoE cop");
        var colour = await parameters.AddParameterAsync(t, "Culoare cop", "", ParameterKind.Text);
        var k1 = await products.CreateChildAsync(r, $"Ext Cop K1 {suffix}", false);
        var k2 = await products.CreateChildAsync(r, $"Ext Cop K2 {suffix}", false);
        var p1 = await products.CreateAsync(Input(k1, $"COP1-{suffix}", (lens, v4.Id)));
        var p2 = await products.CreateAsync(Input(k2, $"COP2-{suffix}", (lens, v28.Id)));
        await tags.SetProductTagsAsync(p1.Id, [poe.Id]);

        // The plain move refuses and asks for the verification; the preview says what happens and changes nothing.
        await Rejects<MoveNeedsVerificationException>(() => products.MoveAsync(k1, t, catA, 0), "A move that changes what a branch inherits asks for the verification");
        var preview = await products.PreviewMoveAsync(k1, t, catA, 0, null);
        Check(!preview.Plain && preview.CanApply && preview.Items.Count == 2 && preview.Items.All(item => item.Action == BranchMoveAction.Copy)
              && preview.Incomplete.Select(item => item.ProductId).SequenceEqual([p1.Id]),
            "Branch move: the preview copies the lens and the tag onto the node and lists the product that lacks the colour of the new parent");
        Check((await Tree(catA)).Single(item => item.Name == k1.Subcategory).Parent == r.Subcategory && (await parameters.GetParametersAsync()).Count(item => item.Category == catA && item.Name == "Lentila cop") == 1,
            "Branch move: the preview changes nothing");
        await Rejects<ProductOperationException>(() => products.MoveAsync(k1, t, catA, 0, new BranchMoveChoices()), "Branch move: products that become incomplete must be accepted");
        Check((await Tree(catA)).Single(item => item.Name == k1.Subcategory).Parent == r.Subcategory, "Branch move: a refused move changes nothing");
        var moved = await products.MoveAsync(k1, t, catA, 0, new BranchMoveChoices([], true, "Ext mutare cu definitii"));
        Check(!moved.OnlyReordered && moved.IncompleteProducts == 1 && (await Tree(catA)).Single(item => item.Name == k1.Subcategory).Parent == t.Subcategory, "Branch move: the accepted move is applied");

        var lensNow = (await parameters.GetParametersAsync()).Where(item => item.Category == catA && item.Name == "Lentila cop").OrderBy(item => item.Subcategory).ToList();
        var copy = lensNow.Single(item => item.Subcategory == k1.Subcategory);
        Check(lensNow.Count == 2 && lensNow.Any(item => item.Id == lens.Id && item.Subcategory == r.Subcategory) && copy.Id != lens.Id && copy.Unit == "mm" && copy.Values.Select(value => value.Value).Order().SequenceEqual(["2.8", "4"]),
            "Branch move: the moved node got a copy of the lens with its values, the old parent keeps its own");
        var state1 = await parameters.GetProductStateAsync(p1.Id);
        var state2 = await parameters.GetProductStateAsync(p2.Id);
        Check(state1.Choices.Single().ParameterId == copy.Id && copy.Values.Single(value => value.Id == state1.Choices.Single().ValueId).Value == "4" && state2.Choices.Single().ParameterId == lens.Id
              && (await products.GetProductAsync(p1.Id))!.Name == p1.Name,
            "Branch move: the product of the moved branch points to the copy with its value and code, the sibling's product is untouched");
        var tagsNow = (await tags.GetTagsAsync()).Where(item => item.Category == catA && item.Name == "PoE cop").ToList();
        Check(tagsNow.Count == 2 && (await tags.GetProductTagIdsAsync(p1.Id)).SequenceEqual([tagsNow.Single(item => item.Subcategory == k1.Subcategory).Id]) && tagsNow.Single(item => item.Id == poe.Id).Subcategory == r.Subcategory,
            "Branch move: the tag is copied onto the node and the product keeps it On");
        var incomplete = await parameters.GetIncompleteProductIdsAsync();
        var refused = await Rejects<StockMovementOperationException>(() => movements.CreateAsync(p1.Id, new StockMovementInput { Kind = StockMovementKind.Entry, Date = today, Quantity = 1, Description = "Ext mutare ramura" }),
            "Branch move: the product that lacks the colour of the new parent takes no stock entry");
        Check(incomplete.Contains(p1.Id) && !incomplete.Contains(p2.Id) && refused!.Message.Contains("Culoare cop", StringComparison.Ordinal), "Branch move: only the product that lacks the inherited colour is incomplete");

        // A namesake at the new parent: the copy is merged into it after the user decides.
        var t2 = await products.CreateSubcategoryAsync(catA, $"Ext Cop T2 {suffix}");
        var lens2 = await parameters.AddParameterAsync(t2, "Lentila cop", "mm", ParameterKind.Number);
        await parameters.AddValueAsync(lens2.Id, "2.8");
        var undecided = await products.PreviewMoveAsync(k2, t2, catA, 0, null);
        Check(undecided.NeedsDecision && !undecided.CanApply && undecided.Items.Single(item => item.Ref.Kind == DefinitionKind.Parameter).Action == BranchMoveAction.NeedsDecision, "Branch move: a namesake at the new parent needs a decision");
        await Rejects<ProductOperationException>(() => products.MoveAsync(k2, t2, catA, 0, new BranchMoveChoices()), "Branch move: an undecided namesake is not applied");
        var lensRef = new DefinitionRef(DefinitionKind.Parameter, lens.Id);
        await products.MoveAsync(k2, t2, catA, 0, new BranchMoveChoices([lensRef], true, "Ext mutare cu imbinare"));
        var lens2Now = (await parameters.GetParametersAsync()).Single(item => item.Id == lens2.Id);
        var state2After = await parameters.GetProductStateAsync(p2.Id);
        Check(state2After.Choices.Single().ParameterId == lens2.Id && lens2Now.Values.Single(value => value.Id == state2After.Choices.Single().ValueId).Value == "2.8"
              && (await parameters.GetParametersAsync()).Count(item => item.Subcategory == k2.Subcategory) == 0 && (await parameters.GetParametersAsync()).Any(item => item.Id == lens.Id),
            "Branch move: the merged product points to the namesake of the new parent; no copy is left on the node and the old parent keeps its lens");

        // A definition of the branch that repeats the new parent's.
        var t3 = await products.CreateSubcategoryAsync(catA, $"Ext Cop T3 {suffix}");
        var colour3 = await parameters.AddParameterAsync(t3, "Marime cop", "", ParameterKind.Text);
        var k3 = await products.CreateSubcategoryAsync(catA, $"Ext Cop K3 {suffix}");
        var ownColour = await parameters.AddParameterAsync(k3, "marime COP", "", ParameterKind.Text);
        var red = await parameters.AddValueAsync(ownColour.Id, "mare");
        var p3 = await products.CreateAsync(Input(k3, $"COP3-{suffix}", (ownColour, red.Id)));
        var own = await products.PreviewMoveAsync(k3, t3, catA, 0, null);
        Check(own.NeedsDecision && own.Items.Single().Action == BranchMoveAction.NeedsDecision, "Branch move: an own parameter that repeats the new parent's needs a decision");
        await Rejects<ProductOperationException>(() => products.MoveAsync(k3, t3, catA, 0), "Branch move: the plain move of a branch with a repeated own parameter asks for the verification");
        var ownRef = new DefinitionRef(DefinitionKind.Parameter, ownColour.Id);
        await products.MoveAsync(k3, t3, catA, 0, new BranchMoveChoices([ownRef], true, "Ext imbinare proprie"));
        var colour3Now = (await parameters.GetParametersAsync()).Single(item => item.Id == colour3.Id);
        var state3 = await parameters.GetProductStateAsync(p3.Id);
        Check((await parameters.GetParametersAsync()).All(item => item.Id != ownColour.Id) && state3.Choices.Single().ParameterId == colour3.Id && colour3Now.Values.Single(value => value.Id == state3.Choices.Single().ValueId).Value == "mare"
              && !(await parameters.GetIncompleteProductIdsAsync()).Contains(p3.Id),
            "Branch move: the own parameter merged into the new parent's, with its value kept and the product complete");

        // To another category: what the old category defined is copied onto the node, the products change category with it.
        var guarantee = await parameters.AddParameterAsync(new ProductGroup(catB, ""), "Garantie cop", "", ParameterKind.Text);
        var twoYears = await parameters.AddValueAsync(guarantee.Id, "2 ani");
        var leafB = await products.CreateSubcategoryAsync(catB, $"Ext Cop LB {suffix}");
        var p4 = await products.CreateAsync(Input(leafB, $"COP4-{suffix}", (guarantee, twoYears.Id)));
        var intoA = await products.PreviewMoveAsync(leafB, null, catA, int.MaxValue, null);
        Check(intoA.CanApply && intoA.Items.Any(item => item.Name == "Garantie cop" && item.Action == BranchMoveAction.Copy), "Branch move: the preview of a move to another category copies the old category's parameter onto the node");
        await products.MoveAsync(leafB, null, catA, int.MaxValue, new BranchMoveChoices([], true, "Ext mutare categorie"));
        var guaranteeNow = (await parameters.GetParametersAsync()).Single(item => item.Category == catA && item.Name == "Garantie cop");
        var state4 = await parameters.GetProductStateAsync(p4.Id);
        Check(guaranteeNow.Subcategory == leafB.Subcategory && state4.Choices.Single().ParameterId == guaranteeNow.Id && (await products.GetProductAsync(p4.Id))!.Category == catA
              && (await parameters.GetParametersAsync()).Any(item => item.Id == guarantee.Id && item.Subcategory == ""),
            "Branch move: across categories the product keeps the guarantee through a copy on the node, and the old category keeps its own");

        var events = (await audit.GetEventsAsync()).Where(item => item.TimestampUtc >= since && item.Action == AuditActions.MoveSubcategory).ToList();
        Check(events.Any(item => item.Motif == "Ext mutare cu definitii" && item.Details.Contains("Definiții copiate pe nod: 2", StringComparison.Ordinal) && item.Details.Contains("Produse devenite incomplete: 1", StringComparison.Ordinal))
              && events.Any(item => item.Motif == "Ext mutare cu imbinare" && item.Details.Contains("Definiții îmbinate: 1", StringComparison.Ordinal)),
            "Branch move: the journal names the definitions copied and merged and the products left incomplete, with the reason given");
    }
}
