using Microsoft.Extensions.Configuration;
using BlazorStoc.Services;

namespace BlazorStoc.Checks;

public static partial class MariaExtendedChecks
{
    // A leaf becomes a category (stage 7): the category takes the leaf's name, the leaf stays under it as its only subcategory under a new name, the products, the parameters and the tags
    // stay on it, what it inherited is copied onto it, and nothing is written before the name and the checks pass.
    private static async Task LeafPromotionAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit)
    {
        var suffix = Suffix();
        var since = DateTime.UtcNow.AddSeconds(-1);
        var products = new MariaProductRepository(configuration, admin, audit);
        var parameters = new MariaProductParameterRepository(configuration, admin);   // not journaled: other sections count these events
        var tags = new MariaProductTagRepository(configuration, admin);
        var catA = $"Ext Prm Cat {suffix}";
        await products.CreateCategoryAsync(catA);
        async Task<List<string>> Categories() => (await products.GetGroupsAsync()).Select(group => group.Category).Distinct().ToList();
        ProductInput Input(ProductGroup group, string model, params (SubcategoryParameter Parameter, int ValueId)[] choices) => new()
        {
            Name = model, BaseModel = model, Category = group.Category, Subcategory = group.Subcategory,
            Parameters = choices.Select(choice => new ProductParameterChoice(choice.Parameter.Id, choice.ValueId)).ToList()
        };

        var parent = await products.CreateSubcategoryAsync(catA, $"Ext Prm P {suffix}");
        var lens = await parameters.AddParameterAsync(parent, "Lentila prm", "mm", ParameterKind.Number);
        var v4 = await parameters.AddValueAsync(lens.Id, "4");
        var poe = await tags.AddTagAsync(parent, "PoE prm");
        var leaf = await products.CreateChildAsync(parent, $"Ext Prm Leaf {suffix}", false);
        var sibling = await products.CreateChildAsync(parent, $"Ext Prm Sibling {suffix}", false);
        var product = await products.CreateAsync(Input(leaf, $"PRM1-{suffix}", (lens, v4.Id)));
        await tags.SetProductTagsAsync(product.Id, [poe.Id]);
        var newName = $"Ext Prm General {suffix}";
        var categoriesBefore = await Categories();
        var place = categoriesBefore.IndexOf(catA) + 1;

        // The names and the leaf are checked before anything is written.
        var same = await Rejects<ProductOperationException>(() => products.PreviewPromotionAsync(leaf, leaf.Subcategory, place, null), "Promotion: the subcategory cannot keep the name the category takes");
        Check(same!.Message.Contains("alt nume", StringComparison.Ordinal), "Promotion: the message explains that the subcategory needs another name");
        await Rejects<ProductOperationException>(() => products.PreviewPromotionAsync(leaf, "  ", place, null), "Promotion: an empty name is refused");
        await Rejects<ProductOperationException>(() => products.PreviewPromotionAsync(leaf, sibling.Subcategory, place, null), "Promotion: a name already used by a subcategory is refused");
        await Rejects<ProductOperationException>(() => products.PreviewPromotionAsync(leaf, catA, place, null), "Promotion: a name already used by a category is refused");
        var notLeaf = await Rejects<ProductOperationException>(() => products.PreviewPromotionAsync(parent, newName, place, null), "Promotion: a subcategory with children cannot become a category");
        Check(notLeaf!.Message == SubcategoryTreeRules.PromoteLeafOnlyMessage, "Promotion: the message says that only leaves become categories");

        // The preview is a rolled-back run: it lists the lens and the tag copied onto the node and changes nothing.
        var preview = await products.PreviewPromotionAsync(leaf, newName, place, null);
        Check(preview.CanApply && preview.Items.Count == 2 && preview.Items.All(item => item.Action == BranchMoveAction.Copy) && preview.Incomplete.Count == 0,
            "Promotion: the preview copies the inherited lens and tag onto the node and leaves no product incomplete");
        var treeBefore = await products.GetTreeAsync();
        Check(treeBefore.Any(node => node.Category == catA && node.Name == leaf.Subcategory) && !(await Categories()).Contains(leaf.Subcategory) && !treeBefore.Any(node => node.Name == newName),
            "Promotion: the preview changes nothing");

        var done = await products.PromoteAsync(leaf, newName, place, new BranchMoveChoices([], true, "Ext promovare frunza"));
        var tree = await products.GetTreeAsync();
        var promoted = tree.Single(node => node.Name == newName);
        var categories = await Categories();
        Check(promoted.Category == leaf.Subcategory && promoted.Parent is null && promoted.Depth == 0 && promoted.IsLeaf && !tree.Any(node => node.Name == leaf.Subcategory) && done.NodesMoved == 1,
            "Promotion: the leaf is now the only subcategory of a category that has its old name");
        Check(categories.IndexOf(leaf.Subcategory) == place && categories.Count == categoriesBefore.Count + 1, "Promotion: the new category is at the wanted place among the categories");
        var moved = (await products.GetProductAsync(product.Id))!;
        Check(moved.Category == leaf.Subcategory && moved.Subcategory == newName && tree.Any(node => node.Category == catA && node.Name == parent.Subcategory) && tree.Any(node => node.Name == sibling.Subcategory),
            "Promotion: the product follows the leaf, the old parent and its other children stay");
        var lensNow = (await parameters.GetParametersAsync()).Where(item => item.Name == "Lentila prm" && (item.Category == catA || item.Category == leaf.Subcategory)).ToList();
        var copy = lensNow.Single(item => item.Subcategory == newName);
        var state = await parameters.GetProductStateAsync(product.Id);
        Check(lensNow.Count == 2 && lensNow.Any(item => item.Id == lens.Id && item.Subcategory == parent.Subcategory) && state.Choices.Single().ParameterId == copy.Id
              && copy.Values.Single(value => value.Id == state.Choices.Single().ValueId).Value == "4",
            "Promotion: the product keeps its lens value through a copy on the node and the old parent keeps its own");
        var tagsNow = (await tags.GetTagsAsync()).Where(item => item.Name == "PoE prm" && (item.Category == catA || item.Category == leaf.Subcategory)).ToList();
        Check(tagsNow.Count == 2 && (await tags.GetProductTagIdsAsync(product.Id)).SequenceEqual([tagsNow.Single(item => item.Subcategory == newName).Id]), "Promotion: the tag is copied onto the node and the product keeps it On");

        var events = (await audit.GetEventsAsync()).Where(item => item.TimestampUtc >= since && item.Action == AuditActions.PromoteSubcategory).ToList();
        Check(events.Any(item => item.Motif == "Ext promovare frunza" && item.Details.Contains($"Subcategorie nouă: {newName}", StringComparison.Ordinal) && item.Details.Contains($"Categorie nouă: {leaf.Subcategory}", StringComparison.Ordinal)
                                 && item.Details.Contains("Produse rămase pe subcategorie: 1", StringComparison.Ordinal) && item.Details.Contains("Definiții copiate pe nod: 2", StringComparison.Ordinal)),
            "Promotion: the journal names the old and the new names, the products that stayed and the definitions copied, with the reason given");
    }
}
