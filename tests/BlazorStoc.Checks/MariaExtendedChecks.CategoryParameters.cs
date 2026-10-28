using Microsoft.Extensions.Configuration;
using MySqlConnector;
using BlazorStoc.Services;

namespace BlazorStoc.Checks;

public static partial class MariaExtendedChecks
{
    // Parameters, families and tags owned by a category (migration 56): every subcategory under it inherits them, products need their values, names stay unique
    // across the category and its tree, archiving works for them and a branch with inherited category definitions does not move to another category.
    private static async Task CategoryParametersAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit)
    {
        var suffix = Suffix();
        var today = DateOnly.FromDateTime(DateTime.Now);
        var products = new MariaProductRepository(configuration, admin);
        var parameters = new MariaProductParameterRepository(configuration, admin);   // not journaled: other sections count these events
        var tags = new MariaProductTagRepository(configuration, admin);
        var movements = new MariaStockMovementRepository(configuration, admin);
        var category = $"Ext CatPar Cat {suffix}";
        var otherCategory = $"Ext CatPar Alta {suffix}";
        var leafName = $"Ext CatPar Frunza {suffix}";
        var otherLeaf = $"Ext CatPar Frunza2 {suffix}";
        await products.CreateCategoryAsync(category);
        await products.CreateCategoryAsync(otherCategory);
        var leaf = await products.CreateSubcategoryAsync(category, leafName);
        var second = await products.CreateSubcategoryAsync(category, otherLeaf);
        var categoryGroup = new ProductGroup(category, "");

        // A product created before the category had parameters.
        var early = await products.CreateAsync(new ProductInput { Name = $"CP0-{suffix}", Category = category, Subcategory = leafName });
        var colour = await parameters.AddParameterAsync(categoryGroup, "Culoare", "", ParameterKind.Text);
        var white = await parameters.AddValueAsync(colour.Id, "alb");
        var black = await parameters.AddValueAsync(colour.Id, "negru");
        Check(colour.Subcategory == "" && (await parameters.GetParametersAsync()).Single(item => item.Id == colour.Id) is { Subcategory: "", Category: var owner } && owner == category,
            "A parameter of a category is listed with the category and no subcategory");
        Check((await parameters.GetIncompleteProductIdsAsync()).Contains(early.Id), "A product of a subcategory becomes incomplete when its category gets a parameter");
        var missing = await parameters.GetMissingAsync(early);
        Check(missing.Count == 1 && missing[0].Contains("Culoare", StringComparison.Ordinal), "The missing parameters of a product include those of its category");
        var blocked = await Rejects<StockMovementOperationException>(() => movements.CreateAsync(early.Id,
            new StockMovementInput { Kind = StockMovementKind.Entry, Date = today, Quantity = 1, Description = "Ext categorie blocat" }), "An incomplete product takes no stock entry");
        Check(blocked!.Message.Contains("Culoare", StringComparison.Ordinal), "The refusal names the parameter of the category");

        // Names are unique across the category and everything under it.
        await Rejects<ProductOperationException>(() => parameters.AddParameterAsync(leaf, " culoare ", "", ParameterKind.Text), "A subcategory cannot repeat a parameter of its category");
        var size = await parameters.AddParameterAsync(leaf, "Marime", "", ParameterKind.Text);
        await Rejects<ProductOperationException>(() => parameters.AddParameterAsync(categoryGroup, "marime", "", ParameterKind.Text), "A category cannot take the name of a parameter of a subcategory under it");
        var sizeM = await parameters.AddValueAsync(size.Id, "M");
        var sizeNow = (await parameters.GetParametersAsync()).Single(item => item.Id == size.Id);
        await Rejects<ProductOperationException>(() => parameters.UpdateParameterAsync(sizeNow, "Culoare", "", "Ext nume dublu"), "A parameter cannot be renamed like one of its category");

        // The product is completed with the values of the category and of its subcategory; the code starts with the category's parameter.
        var edit = new ProductInput { Name = early.Name, BaseModel = $"CP0-{suffix}", Category = category, Subcategory = leafName, Reason = "Ext completare categorie",
            Parameters = [new ProductParameterChoice(size.Id, sizeM.Id), new ProductParameterChoice(colour.Id, white.Id)] };
        var completed = await products.UpdateAsync(early, edit);
        Check(completed.Name == $"CP0-{suffix} - alb - M" && !(await parameters.GetIncompleteProductIdsAsync()).Contains(early.Id), "The code puts the category's values first and the product is complete");
        await movements.CreateAsync(early.Id, new StockMovementInput { Kind = StockMovementKind.Entry, Date = today, Quantity = 2, Description = "Ext categorie intrare" });
        var otherLeafProduct = await Rejects<ProductOperationException>(() => products.CreateAsync(new ProductInput { Name = $"CP1-{suffix}", BaseModel = $"CP1-{suffix}", Category = category, Subcategory = otherLeaf }),
            "A product of another subcategory of the category needs the category's parameter too");
        Check(otherLeafProduct!.Message.Contains("Culoare", StringComparison.Ordinal), "The refusal names the category parameter");

        // A family of the category groups its parameters; a product has exactly one member.
        var wired = await parameters.AddParameterAsync(categoryGroup, "Cablat", "", ParameterKind.Text);
        var wireless = await parameters.AddParameterAsync(categoryGroup, "Wireless", "", ParameterKind.Text);
        await Rejects<ProductOperationException>(() => parameters.CreateFamilyAsync(leaf, "Mixt", [wired.Id, size.Id]), "A family does not mix parameters of a category and of a subcategory");
        var family = await parameters.CreateFamilyAsync(categoryGroup, "Conectare", [wired.Id, wireless.Id]);
        Check((await parameters.GetFamiliesAsync()).Single(item => item.Id == family.Id) is { Subcategory: "" } && (await parameters.GetIncompleteProductIdsAsync()).Contains(early.Id),
            "A family of a category is listed with the category and makes the products without a member incomplete");
        await parameters.AddValueAsync(wired.Id, "da");
        var wiredNow = (await parameters.GetParametersAsync()).Single(item => item.Id == wired.Id);
        var fixedEdit = new ProductInput { Name = completed.Name, BaseModel = $"CP0-{suffix}", Category = category, Subcategory = leafName, Reason = "Ext familie categorie",
            Parameters = [new ProductParameterChoice(size.Id, sizeM.Id), new ProductParameterChoice(colour.Id, white.Id), new ProductParameterChoice(wired.Id, wiredNow.Values[0].Id)] };
        var withFamily = await products.UpdateAsync(completed, fixedEdit);
        Check(!(await parameters.GetIncompleteProductIdsAsync()).Contains(early.Id) && withFamily.Name.Contains("da", StringComparison.Ordinal), "One member of the category family completes the product");

        // Tags of a category reach every product under it.
        var promoted = await tags.AddTagAsync(categoryGroup, "Promotie");
        await Rejects<ProductOperationException>(() => tags.AddTagAsync(leaf, "promotie"), "A subcategory cannot repeat a tag of its category");
        await tags.SetProductTagsAsync(early.Id, [promoted.Id]);
        Check((await tags.GetProductTagIdsAsync(early.Id)).SequenceEqual([promoted.Id]) && (await tags.GetTagsAsync()).Single(item => item.Id == promoted.Id) is { Subcategory: "" },
            "A product can have a tag of its category On");

        // Archive: the value of the category parameter moves into the models and comes back.
        var colourNow = (await parameters.GetParametersAsync()).Single(item => item.Id == colour.Id);
        var preview = await parameters.PreviewParameterArchiveAsync(colourNow);
        Check(preview.Affected.Count == 1 && preview.CanApply, "Archiving a category parameter lists the products of its whole tree");
        var codeBefore = (await products.GetProductAsync(early.Id))!.Name;
        await parameters.ArchiveParameterAsync(colourNow, "Ext arhivare culoare categorie");
        var afterArchive = (await products.GetProductAsync(early.Id))!;
        Check(afterArchive.Name.StartsWith($"CP0-{suffix} - alb", StringComparison.Ordinal) && afterArchive.Name.Contains("M", StringComparison.Ordinal) && !(await parameters.GetIncompleteProductIdsAsync()).Contains(early.Id),
            "Archiving the category parameter bakes its value into the model and leaves the product complete");
        var archivedColour = (await parameters.GetArchivedAsync()).Parameters.Single(item => item.Id == colour.Id);
        Check(archivedColour.Subcategory == "" && archivedColour.ProductCount == 1, "The archive lists the category parameter with its products");
        var restore = await parameters.PreviewParameterRestoreAsync(archivedColour);
        Check(restore.CanApplyAsIs && restore.Items.Single().NewName == codeBefore, "The restore proposes the former code");
        await parameters.RestoreParameterAsync(archivedColour, new Dictionary<int, string>(), "Ext restaurare culoare categorie");
        Check((await products.GetProductAsync(early.Id))!.Name == codeBefore && (await parameters.GetParametersAsync()).Any(item => item.Id == colour.Id), "Restoring brings back the code and the parameter");

        // Another category's tree is unaffected, and a branch does not move between categories while a category has its own definitions.
        await products.CreateSubcategoryAsync(otherCategory, $"Ext CatPar Sub Alta {suffix}");
        await Rejects<ProductOperationException>(() => products.MoveAsync(second, null, otherCategory, 0), "A branch does not move to another category while the categories have definitions of their own");
        var plainProduct = await products.CreateAsync(new ProductInput { Name = $"CP9-{suffix}", Category = otherCategory, Subcategory = $"Ext CatPar Sub Alta {suffix}" });
        Check(!(await parameters.GetIncompleteProductIdsAsync()).Contains(plainProduct.Id), "A product of a category without parameters stays complete");

        // The usage log names the category as the place.
        var usage = await parameters.GetUsageAsync([colour.Id]);
        Check(usage.Count > 0 && usage.All(item => item.Node == category), "The usage log names the category as the place of a category parameter");
    }
}