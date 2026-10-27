using Microsoft.Extensions.Configuration;
using MySqlConnector;
using BlazorStoc.Services;

namespace BlazorStoc.Checks;

// Interval parameters and families of parameters on MariaDB (migration 52): a family groups at least two parameters; a product has exactly one active member per
// family; the grouping is refused while a product already has values for two members; ungrouping and deleting a member keep the rules.
public static partial class MariaExtendedChecks
{
    private static async Task ParameterFamiliesAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe)
    {
        var suffix = Suffix();
        var started = DateTime.UtcNow.AddSeconds(-1);
        var today = DateOnly.FromDateTime(DateTime.Now);
        var user = new TestAccessControl(false, "utilizator.familii");
        var products = new MariaProductRepository(configuration, admin, audit);
        var parameters = new MariaProductParameterRepository(configuration, admin, audit);
        var userParameters = new MariaProductParameterRepository(configuration, user, audit);
        var movements = new MariaStockMovementRepository(configuration, admin, audit);
        var category = $"Ext Fam Cat {suffix}";
        var subcategory = $"Ext Fam Sub {suffix}";
        await products.CreateCategoryAsync(category);
        await products.CreateSubcategoryAsync(category, subcategory);
        var group = new ProductGroup(category, subcategory);

        // Parameters: a fixed lens (numbers), a varifocal lens (intervals) and a colour of its own.
        var fixedLens = await parameters.AddParameterAsync(group, "Fixa", "mm", ParameterKind.Number);
        var varifocal = await parameters.AddParameterAsync(group, "Varifocala", "mm", ParameterKind.Interval);
        var color = await parameters.AddParameterAsync(group, "Culoare", "", ParameterKind.Text);
        Check(varifocal.Kind == ParameterKind.Interval && varifocal.Unit == "mm" && color.Unit == "", "An interval parameter has a unit like a number; a text has none");
        var f28 = await parameters.AddValueAsync(fixedLens.Id, "2,8");
        var f4 = await parameters.AddValueAsync(fixedLens.Id, "4");
        var i212 = await parameters.AddValueAsync(varifocal.Id, "2,8 - 12");
        var i2710 = await parameters.AddValueAsync(varifocal.Id, "2.7..13.5");
        var white = await parameters.AddValueAsync(color.Id, "alb");
        Check(i212.Value == "2.8-12" && i2710.Value == "2.7-13.5" && f4.Value == "4", "Interval values are stored as start-end");
        await Rejects<ProductOperationException>(() => parameters.AddValueAsync(varifocal.Id, "2.80-12.0"), "The same interval written differently is a duplicate");
        await Rejects<ProductOperationException>(() => parameters.AddValueAsync(varifocal.Id, "12-2.8"), "An interval whose start is above its end is refused");
        await Rejects<ProductOperationException>(() => parameters.AddValueAsync(varifocal.Id, "5"), "A single number is not an interval");
        await Rejects<ProductOperationException>(() => parameters.UpdateParameterAsync(color, "Culoare", "", ParameterKind.Number, "Verificare schimbare tip"), "A text with non-numeric values cannot become a number");
        await parameters.UpdateParameterAsync(fixedLens, "Fixa", "mm", ParameterKind.Text, "Verificare schimbare tip");
        var asText = (await parameters.GetParametersAsync()).Single(item => item.Id == fixedLens.Id);
        Check(asText.Kind == ParameterKind.Text && asText.Unit == "", "The kind of a parameter can change (a number becomes a text and loses its unit)");
        await parameters.UpdateParameterAsync(asText, "Fixa", "mm", ParameterKind.Number, "Verificare schimbare tip");
        fixedLens = (await parameters.GetParametersAsync()).Single(item => item.Id == fixedLens.Id);
        Check(fixedLens.Kind == ParameterKind.Number && fixedLens.Unit == "mm", "A text whose values are numbers becomes a number again");
        var listed = (await parameters.GetParametersAsync()).Single(item => item.Id == varifocal.Id).Values.Select(item => item.Value).ToList();
        Check(listed.SequenceEqual(["2.7-13.5", "2.8-12"]), "Interval values are listed by start, then end");

        // Before any family every parameter is required: a product with both lenses is allowed (and is later a conflict).
        ProductInput Input(string model, params (SubcategoryParameter Parameter, int ValueId)[] choices) => new()
        {
            Name = model, BaseModel = model, Category = category, Subcategory = subcategory,
            Parameters = choices.Select(choice => new ProductParameterChoice(choice.Parameter.Id, choice.ValueId)).ToList()
        };
        var both = await products.CreateAsync(Input($"DUBLA-{suffix}", (fixedLens, f28.Id), (varifocal, i212.Id), (color, white.Id)));
        Check(both.Name == $"DUBLA-{suffix} - 2.8 mm - 2.8-12 mm - alb", "Without a family all parameters are in the code, in order");

        // Creating a family: at least two parameters, administrators only, not while a product has two of the members.
        await Rejects<ProductOperationException>(() => parameters.CreateFamilyAsync(group, "Lentila", [fixedLens.Id]), "A family needs at least two parameters");
        await Rejects<AccessDeniedException>(() => userParameters.CreateFamilyAsync(group, "Lentila", [fixedLens.Id, varifocal.Id]), "Only an administrator creates a family");
        var clash = await Rejects<ProductOperationException>(() => parameters.CreateFamilyAsync(group, "Lentila", [fixedLens.Id, varifocal.Id]), "A family is refused while a product has values for two of its members");
        Check(clash!.Message.Contains($"DUBLA-{suffix}", StringComparison.Ordinal), "The refusal lists the products to correct");
        await products.DeleteAsync((await products.GetProductAsync(both.Id))!, "Ext curatare conflict");
        var family = await parameters.CreateFamilyAsync(group, "Lentila", [fixedLens.Id, varifocal.Id]);
        var familyParameters = (await parameters.GetParametersAsync()).Where(item => item.Category == category && item.Subcategory == subcategory).ToList();
        Check(family.ParameterIds.Count == 2 && familyParameters.Count(item => item.FamilyId == family.Id) == 2 && familyParameters.Single(item => item.Id == color.Id).FamilyId is null,
            "The family groups its two parameters; the colour stays on its own");
        await Rejects<ProductOperationException>(() => parameters.CreateFamilyAsync(group, "Alta", [fixedLens.Id, color.Id]), "A parameter belongs to at most one family");
        Check((await parameters.GetFamiliesAsync()).Single(item => item.Id == family.Id).ParameterIds.Count == 2, "The family and its members can be read back");

        // A product has exactly one active member of the family: none or two are refused, one is composed in the place of the family.
        await Rejects<ProductOperationException>(() => products.CreateAsync(Input($"F0-{suffix}", (color, white.Id))), "A product with no member of the family is refused");
        await Rejects<ProductOperationException>(() => products.CreateAsync(Input($"F2-{suffix}", (fixedLens, f28.Id), (varifocal, i212.Id), (color, white.Id))), "A product with two members of the family is refused");
        var fixedCam = await products.CreateAsync(Input($"CAM-{suffix}", (color, white.Id), (fixedLens, f28.Id)));
        var varCam = await products.CreateAsync(Input($"CAM-{suffix}", (varifocal, i212.Id), (color, white.Id)));
        Check(fixedCam.Name == $"CAM-{suffix} - 2.8 mm - alb" && varCam.Name == $"CAM-{suffix} - 2.8-12 mm - alb", "The family takes the place of its first member in the code, whichever member is active");
        Check((await parameters.GetParametersAsync()).Single(item => item.Id == varifocal.Id).Values.Single(item => item.Id == i212.Id).ProductCount == 1, "The list of values counts the products of an interval");
        var allChoices = await parameters.GetAllChoicesAsync();
        Check(allChoices.TryGetValue(varCam.Id, out var varChoices) && varChoices.Any(item => item.ValueId == i212.Id) && varChoices.Count == 2, "The catalog tags read every product's chosen values in one call");
        await movements.CreateAsync(fixedCam.Id, new StockMovementInput { Kind = StockMovementKind.Entry, Date = today, Quantity = 2, Description = "Ext familie intrare" });
        Check(!(await parameters.GetIncompleteProductIdsAsync()).Contains(fixedCam.Id) && !(await parameters.GetIncompleteProductIdsAsync()).Contains(varCam.Id), "Products with one active member are complete");

        // Editing the family: the members change, never below two; ungrouping makes every member required on its own.
        await Rejects<ProductOperationException>(() => parameters.UpdateFamilyAsync(family, "Lentila", [fixedLens.Id], "Ext prea putini"), "A family cannot be reduced below two parameters");
        await Rejects<ProductOperationException>(() => parameters.UpdateFamilyAsync(family, "Lentila", [fixedLens.Id, varifocal.Id], "Ext fara modificare"), "A save without changes is refused");
        var renamedFamily = await parameters.UpdateFamilyAsync(family, "Tip lentila", [fixedLens.Id, varifocal.Id], "Ext redenumire familie");
        Check(renamedFamily.Name == "Tip lentila" && renamedFamily.Version == family.Version + 1, "A family is renamed with a new version");
        await Rejects<ProductOperationException>(() => parameters.UpdateFamilyAsync(family, "Tip lentila", [fixedLens.Id, varifocal.Id], "Ext versiune veche"), "An outdated family version is refused");
        await parameters.DeleteFamilyAsync(renamedFamily, "Ext desfacere");
        var incompleteAfter = await parameters.GetIncompleteProductIdsAsync();
        Check(incompleteAfter.Contains(fixedCam.Id) && incompleteAfter.Contains(varCam.Id) && (await parameters.GetFamiliesAsync()).All(item => item.Id != family.Id),
            "Ungrouping makes each member required again: products with only one of them become incomplete");
        var blocked = await Rejects<StockMovementOperationException>(() => movements.CreateAsync(fixedCam.Id,
            new StockMovementInput { Kind = StockMovementKind.Entry, Date = today, Quantity = 1, Description = "Ext familie blocat" }), "An incomplete product takes no stock entry");
        Check(blocked!.Message.Contains("Varifocala", StringComparison.Ordinal), "The refusal names the parameter that is missing");

        // Regrouping works again (no product has two members), and deleting a member of a two-member family undoes the family.
        var again = await parameters.CreateFamilyAsync(group, "Lentila", [fixedLens.Id, varifocal.Id]);
        Check(!(await parameters.GetIncompleteProductIdsAsync()).Contains(fixedCam.Id), "Grouping again completes the products with one active member");
        var varifocalNow = (await parameters.GetParametersAsync()).Single(item => item.Id == varifocal.Id);
        Check((await parameters.PreviewParameterDeletionAsync(varifocalNow)).Affected.Count == 1, "Deleting a family member shows the one product that carries its value");
        var emptySubcategory = $"Ext Fam Gol {suffix}";
        await products.CreateSubcategoryAsync(category, emptySubcategory);
        var emptyGroup = new ProductGroup(category, emptySubcategory);
        var left = await parameters.AddParameterAsync(emptyGroup, "Stanga", "", ParameterKind.Text);
        var right = await parameters.AddParameterAsync(emptyGroup, "Dreapta", "", ParameterKind.Text);
        var pair = await parameters.CreateFamilyAsync(emptyGroup, "Parte", [left.Id, right.Id]);
        await parameters.DeleteParameterAsync((await parameters.GetParametersAsync()).Single(item => item.Id == right.Id), "Ext sterge membru");
        Check((await parameters.GetFamiliesAsync()).All(item => item.Id != pair.Id) && (await parameters.GetParametersAsync()).Single(item => item.Id == left.Id).FamilyId is null,
            "Deleting a member that leaves one parameter undoes the family");

        // A fixed parameter (one value, its own name) works only inside a family: it gives the family its value without any list to extend.
        var fixedSubcategory = $"Ext Fix Sub {suffix}";
        await products.CreateSubcategoryAsync(category, fixedSubcategory);
        var fixedGroup = new ProductGroup(category, fixedSubcategory);
        var fisheye = await parameters.AddParameterAsync(fixedGroup, "Fisheye", "mm", ParameterKind.Fixed);
        Check(fisheye.Kind == ParameterKind.Fixed && fisheye.Unit == "" && fisheye.Values.Count == 1 && fisheye.Values[0].Value == "Fisheye", "A fixed parameter has no unit and one value, its own name");
        var plain = await products.CreateAsync(new ProductInput { Name = $"SIMPLU-{suffix}", Category = category, Subcategory = fixedSubcategory });
        Check(plain.Name == $"SIMPLU-{suffix}" && !(await parameters.GetIncompleteProductIdsAsync()).Contains(plain.Id), "A standalone fixed parameter is not required: the product keeps the code typed");
        await Rejects<ProductOperationException>(() => parameters.AddValueAsync(fisheye.Id, "Altceva"), "A fixed parameter takes no other value");
        await Rejects<ProductOperationException>(() => parameters.DeleteValueAsync(fisheye.Values[0].Id, "Ext valoare fixa"), "The value of a fixed parameter is not deleted");
        await Rejects<ProductOperationException>(() => parameters.ChangeValueAsync(fisheye.Values[0].Id, "Altceva", "Ext valoare fixa"), "The value of a fixed parameter is not changed");
        await Rejects<ProductOperationException>(() => parameters.UpdateParameterAsync(fisheye, "Fisheye", "", ParameterKind.Text, "Ext tip fix"), "A fixed parameter does not change its kind");
        var normalLens = await parameters.AddParameterAsync(fixedGroup, "Normala", "mm", ParameterKind.Number);
        await Rejects<ProductOperationException>(() => parameters.UpdateParameterAsync(normalLens, "Normala", "", ParameterKind.Fixed, "Ext tip fix"), "No other kind becomes fixed");
        var normal28 = await parameters.AddValueAsync(normalLens.Id, "2,8");
        var lensFamily = await parameters.CreateFamilyAsync(fixedGroup, "Lentila", [fisheye.Id, normalLens.Id]);
        await Rejects<ProductOperationException>(() => products.CreateAsync(new ProductInput { Name = $"FX0-{suffix}", BaseModel = $"FX0-{suffix}", Category = category, Subcategory = fixedSubcategory }), "A product of a family needs one of its members, the fixed one included");
        var fisheyeCam = await products.CreateAsync(new ProductInput
        {
            Name = $"FX-{suffix}", BaseModel = $"FX-{suffix}", Category = category, Subcategory = fixedSubcategory, Parameters = [new ProductParameterChoice(fisheye.Id, fisheye.Values[0].Id)]
        });
        var normalCam = await products.CreateAsync(new ProductInput
        {
            Name = $"FX-{suffix}", BaseModel = $"FX-{suffix}", Category = category, Subcategory = fixedSubcategory, Parameters = [new ProductParameterChoice(normalLens.Id, normal28.Id)]
        });
        Check(fisheyeCam.Name == $"FX-{suffix} - Fisheye" && normalCam.Name == $"FX-{suffix} - 2.8 mm", "Choosing the fixed member puts its name in the code");
        Check(!(await parameters.GetIncompleteProductIdsAsync()).Contains(fisheyeCam.Id) && (await ((IProductParameterRepository)parameters).GetMissingAsync(fisheyeCam)).Count == 0, "A product with the fixed member is complete");
        Check((await parameters.GetParametersAsync()).Single(item => item.Id == fisheye.Id).ProductCount == 1, "The fixed value counts its products");
        var fisheyeUsed = (await parameters.GetParametersAsync()).Single(item => item.Id == fisheye.Id);
        await Rejects<ProductOperationException>(() => parameters.UpdateParameterAsync(fisheyeUsed, "Fisheye 2", "", "Ext redenumire"), "A fixed parameter used by products is not renamed");
        await Rejects<ProductOperationException>(() => parameters.DeleteFamilyAsync(lensFamily, "Ext desfacere fixa"), "A family with a fixed member used by products is not ungrouped");
        var other = await parameters.AddParameterAsync(fixedGroup, "Alta", "", ParameterKind.Text);
        await Rejects<ProductOperationException>(() => parameters.UpdateFamilyAsync(lensFamily, "Lentila", [normalLens.Id, other.Id], "Ext scoate fixa"), "A used fixed member does not leave the family");

        // Deleting a parameter that products carry: they lose the value and the code is rewritten without it; two equal codes block everything.
        var size = await parameters.AddParameterAsync(group, "Marime", "", ParameterKind.Text);
        var sizeL = await parameters.AddValueAsync(size.Id, "L");
        var sizeM = await parameters.AddValueAsync(size.Id, "M");
        var delL = await products.CreateAsync(Input($"DEL-{suffix}", (color, white.Id), (fixedLens, f28.Id), (size, sizeL.Id)));
        var delM = await products.CreateAsync(Input($"DEL-{suffix}", (color, white.Id), (fixedLens, f28.Id), (size, sizeM.Id)));
        var sizeNow = (await parameters.GetParametersAsync()).Single(item => item.Id == size.Id);
        var clashPreview = await parameters.PreviewParameterDeletionAsync(sizeNow);
        Check(clashPreview.Affected.Count == 2 && clashPreview.Conflicts.Count > 0 && !clashPreview.CanApply, "The preview lists the products that lose the value and the codes that would become equal");
        await Rejects<ProductOperationException>(() => parameters.DeleteParameterAsync(sizeNow, "Ext sterge cu produse"), "A deletion that would give two products the same code is refused");
        Check((await products.GetProductAsync(delL.Id))!.Name == delL.Name && (await parameters.GetParametersAsync()).Any(item => item.Id == size.Id), "A refused deletion leaves the parameter and the products as they were");
        await products.DeleteAsync((await products.GetProductAsync(delM.Id))!, "Ext curatare");
        await parameters.DeleteParameterAsync(sizeNow, "Ext sterge cu produse");
        var renamedByDeletion = (await products.GetProductAsync(delL.Id))!;
        Check(renamedByDeletion.Name == $"DEL-{suffix} - 2.8 mm - alb" && renamedByDeletion.Version > delL.Version && (await parameters.GetParametersAsync()).All(item => item.Id != size.Id),
            "Deleting a parameter with products removes its value from their code and keeps their stock and version history");

        var events = (await audit.GetEventsAsync()).Where(item => item.TimestampUtc >= started).ToList();
        Check(events.Count(item => item.Action == AuditActions.AddParameterFamily) == 4 && events.Any(item => item.Action == AuditActions.EditParameterFamily && item.Motif == "Ext redenumire familie" && item.Details.Contains("Tip lentila"))
              && events.Any(item => item.Action == AuditActions.DeleteParameterFamily && item.Motif == "Ext desfacere" && item.Details.Contains("Produse devenite incomplete: 2")),
            "The journal names each family operation: added, edited, ungrouped, with the products that became incomplete");
    }
}
