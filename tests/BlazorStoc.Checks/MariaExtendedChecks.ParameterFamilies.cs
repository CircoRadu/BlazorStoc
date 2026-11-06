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

        // A parameter that products carry is never deleted: it is archived, the value moves into the model of each product (so two products that differ only by it
        // do not clash), and restoring brings the names back. An unused one is deleted for good.
        var size = await parameters.AddParameterAsync(group, "Marime", "", ParameterKind.Text);
        var sizeL = await parameters.AddValueAsync(size.Id, "L");
        var sizeM = await parameters.AddValueAsync(size.Id, "M");
        var delL = await products.CreateAsync(Input($"DEL-{suffix}", (color, white.Id), (fixedLens, f28.Id), (size, sizeL.Id)));
        var delM = await products.CreateAsync(Input($"DEL-{suffix}", (color, white.Id), (fixedLens, f28.Id), (size, sizeM.Id)));
        var sizeNow = (await parameters.GetParametersAsync()).Single(item => item.Id == size.Id);
        await Rejects<ProductOperationException>(() => parameters.DeleteParameterAsync(sizeNow, "Ext sterge cu produse"), "A parameter that products carry is not deleted");
        await Rejects<AccessDeniedException>(() => userParameters.ArchiveParameterAsync(sizeNow, "Ext arhivare"), "Only an administrator archives a parameter");
        var archivePreview = await parameters.PreviewParameterArchiveAsync(sizeNow);
        Check(archivePreview.Affected.Count == 2 && archivePreview.Conflicts.Count == 0 && archivePreview.CanApply && !archivePreview.Unused
              && archivePreview.Affected.All(item => item.NewName.StartsWith($"DEL-{suffix} - ", StringComparison.Ordinal) && item.NewName != item.OldName),
            "The archive preview lists the products renamed, none clashing: the value moves into their model");
        await parameters.ArchiveParameterAsync(sizeNow, "Ext arhivare marime");
        var archivedDelL = (await products.GetProductAsync(delL.Id))!;
        Check(archivedDelL.Name == $"DEL-{suffix} - L - 2.8 mm - alb" && (await products.GetProductAsync(delM.Id))!.Name == $"DEL-{suffix} - M - 2.8 mm - alb" && archivedDelL.Version > delL.Version,
            "Archiving a parameter bakes its value into the model of every product that carries it");
        Check((await parameters.GetParametersAsync()).All(item => item.Id != size.Id) && !(await parameters.GetIncompleteProductIdsAsync()).Contains(delL.Id),
            "An archived parameter leaves the live ones and no product becomes incomplete");
        var archived = await parameters.GetArchivedAsync();
        var archivedSize = archived.Parameters.Single(item => item.Id == size.Id);
        Check(archivedSize.ProductCount == 2 && !archivedSize.Unused && archivedSize.Values.Count == 2, "The archive lists the parameter with the products that still link to it and its values");
        await Rejects<ProductOperationException>(() => parameters.ArchiveParameterAsync(sizeNow, "Ext arhivare din nou"), "An archived parameter is not archived twice");
        await Rejects<ProductOperationException>(() => parameters.DeleteArchivedParameterAsync(archivedSize, "Ext sterge folosit"), "An archived parameter that products link to is not deleted");
        await Rejects<ProductOperationException>(() => parameters.GiveUpParameterRestoreAsync(archivedSize, "Ext renuntare devreme"), "The restore is not given up before the retention period passes");
        var state = await parameters.GetProductStateAsync(delL.Id);
        var saveInput = Input(state.BaseModel, (color, white.Id), (fixedLens, f28.Id)); saveInput.Reason = "Ext salvare cu parametru arhivat";
        var savedL = await products.UpdateAsync(archivedDelL, saveInput);
        Check(savedL.Name == archivedDelL.Name && (await parameters.GetArchivedAsync()).Parameters.Single(item => item.Id == size.Id).ProductCount == 2,
            "Saving a product keeps the links of its archived parameters");

        // Restore: one product's model was edited by hand, so the baked text is no longer at its end: that product needs a decision.
        await using (var edit = new MySqlCommand("UPDATE products SET base_model=@model WHERE id=@id", probe))
        {
            edit.Parameters.AddWithValue("@model", $"DEL-{suffix} - M - editat"); edit.Parameters.AddWithValue("@id", delM.Id);
            await edit.ExecuteNonQueryAsync();
        }
        var restorePreview = await parameters.PreviewParameterRestoreAsync(archivedSize);
        Check(restorePreview.Items.Count == 2 && restorePreview.Problems.Count == 1 && restorePreview.Problems[0].ProductId == delM.Id && restorePreview.Problems[0].Problem == RestoreProblem.BakedTextMissing
              && restorePreview.Items.Single(item => item.ProductId == delL.Id).NewName == delL.Name && !restorePreview.CanApplyAsIs,
            "The restore preview proposes the original name and lists the product whose model no longer ends with the baked text");
        await Rejects<ProductOperationException>(() => parameters.RestoreParameterAsync(archivedSize, new Dictionary<int, string>(), "Ext restaurare fara decizie"), "A restore with an unresolved product is refused");
        await parameters.RestoreParameterAsync(archivedSize, new Dictionary<int, string> { [delM.Id] = $"DEL-{suffix}" }, "Ext restaurare marime");
        Check((await products.GetProductAsync(delL.Id))!.Name == delL.Name && (await products.GetProductAsync(delM.Id))!.Name == delM.Name
              && (await parameters.GetParametersAsync()).Single(item => item.Id == size.Id).ProductCount == 2 && (await parameters.GetArchivedAsync()).Parameters.All(item => item.Id != size.Id),
            "Restoring the parameter gives the products their names back, with the model chosen for the one that needed it");
        var usage = await parameters.GetUsageAsync([size.Id]);
        Check(usage.Count(item => item.Action == ParameterUsageActions.Archived) == 2 && usage.Count(item => item.Action == ParameterUsageActions.Restored) == 2
              && usage.Any(item => item.Action == ParameterUsageActions.Applied && item.ProductName == delL.Name && item.Value == "L"),
            "The usage log keeps where the parameter was applied, archived and restored");

        // Archived once more, the products are deleted one by one: after the last one the parameter is unused and is deleted for good (its history stays in the log).
        sizeNow = (await parameters.GetParametersAsync()).Single(item => item.Id == size.Id);
        await parameters.ArchiveParameterAsync(sizeNow, "Ext arhivare a doua");
        await products.DeleteAsync((await products.GetProductAsync(delM.Id))!, "Ext curatare");
        archivedSize = (await parameters.GetArchivedAsync()).Parameters.Single(item => item.Id == size.Id);
        Check(archivedSize.ProductCount == 1, "Deleting a product removes its link to an archived parameter");
        await products.DeleteAsync((await products.GetProductAsync(delL.Id))!, "Ext curatare");
        archivedSize = (await parameters.GetArchivedAsync()).Parameters.Single(item => item.Id == size.Id);
        Check(archivedSize.Unused && (await parameters.GetUsageAsync([size.Id])).Count(item => item.Action == ParameterUsageActions.ProductDeleted) == 2,
            "An archived parameter whose products are all deleted becomes unused, with the deletions kept in the usage log");
        await parameters.DeleteArchivedParameterAsync(archivedSize, "Ext stergere nefolosit");
        Check((await parameters.GetArchivedAsync()).Parameters.All(item => item.Id != size.Id)
              && (await parameters.GetUsageAsync([size.Id])).Any(item => item.Action == ParameterUsageActions.Deleted),
            "An unused archived parameter is deleted for good and its history stays");

        // A family is archived with its members and restored with them; after the retention period the restore can be given up and the unused family deleted.
        var settings = new MariaCatalogSettings(configuration, admin, audit);
        Check(await settings.GetRestoreRetentionDaysAsync() == ParameterArchiveRules.DefaultRetentionDays, "The retention period is 30 days until the administrator changes it");
        await Rejects<ProductOperationException>(() => settings.SaveRestoreRetentionDaysAsync(10), "A retention period under 30 days is refused");
        await Rejects<ProductOperationException>(() => settings.SaveRestoreRetentionDaysAsync(400), "A retention period over 360 days is refused");
        await Rejects<AccessDeniedException>(() => new MariaCatalogSettings(configuration, user, audit).SaveRestoreRetentionDaysAsync(60), "Only an administrator sets the retention period");
        await settings.SaveRestoreRetentionDaysAsync(45);
        Check(await settings.GetRestoreRetentionDaysAsync() == 45 && (await settings.GetMaxSubcategoryDepthAsync()) >= 1, "The retention period is saved without touching the depth setting");
        var famSubcategory = $"Ext Fam Arh {suffix}";
        await products.CreateSubcategoryAsync(category, famSubcategory);
        var famGroup = new ProductGroup(category, famSubcategory);
        var left2 = await parameters.AddParameterAsync(famGroup, "Stanga", "", ParameterKind.Text);
        var right2 = await parameters.AddParameterAsync(famGroup, "Dreapta", "", ParameterKind.Text);
        var valueX = await parameters.AddValueAsync(left2.Id, "x");
        var valueY = await parameters.AddValueAsync(right2.Id, "y");
        var partFamily = await parameters.CreateFamilyAsync(famGroup, "Parte", [left2.Id, right2.Id]);
        ProductInput FamilyInput(string model, SubcategoryParameter parameter, int valueId) => new()
        {
            Name = model, BaseModel = model, Category = category, Subcategory = famSubcategory, Parameters = [new ProductParameterChoice(parameter.Id, valueId)]
        };
        var famA = await products.CreateAsync(FamilyInput($"FA-{suffix}", left2, valueX.Id));
        var famB = await products.CreateAsync(FamilyInput($"FB-{suffix}", right2, valueY.Id));
        var partNow = (await parameters.GetFamiliesAsync()).Single(item => item.Id == partFamily.Id);
        await Rejects<ProductOperationException>(() => parameters.ArchiveParameterAsync((parameters.GetParametersAsync().Result).Single(item => item.Id == left2.Id), "Ext membru"), "A member of a family is not archived alone");
        Check((await parameters.PreviewFamilyArchiveAsync(partNow)).Affected.Count == 2, "The family archive preview lists the products of every member");
        await parameters.ArchiveFamilyAsync(partNow, "Ext arhivare familie");
        var archivedFamilies = await parameters.GetArchivedAsync();
        Check((await parameters.GetFamiliesAsync()).All(item => item.Id != partFamily.Id) && archivedFamilies.Families.Single(item => item.Id == partFamily.Id) is { ProductCount: 2 } archivedPart
              && archivedPart.ParameterIds.Count == 2 && !(await parameters.GetIncompleteProductIdsAsync()).Contains(famA.Id) && (await products.GetProductAsync(famA.Id))!.Name == $"FA-{suffix} - x",
            "Archiving a family archives its members, leaves the products complete and keeps their codes");
        var restoreFamily = archivedFamilies.Families.Single(item => item.Id == partFamily.Id);
        var familyRestorePreview = await parameters.PreviewFamilyRestoreAsync(restoreFamily);
        Check(familyRestorePreview.Items.Count == 2 && familyRestorePreview.CanApplyAsIs, "The family restore preview covers the products of every member");
        await parameters.RestoreFamilyAsync(restoreFamily, new Dictionary<int, string>(), "Ext restaurare familie");
        Check((await parameters.GetFamiliesAsync()).Any(item => item.Id == partFamily.Id) && (await parameters.GetArchivedAsync()).Families.All(item => item.Id != partFamily.Id)
              && (await products.GetProductAsync(famA.Id))!.Name == $"FA-{suffix} - x", "Restoring a family brings back the family, its members and the product codes");
        partNow = (await parameters.GetFamiliesAsync()).Single(item => item.Id == partFamily.Id);
        await parameters.ArchiveFamilyAsync(partNow, "Ext arhivare familie a doua");
        await using (var age = new MySqlCommand("UPDATE subcategory_parameters SET archived_utc='2020-01-01T00:00:00.000Z' WHERE family_id=@id", probe))
        {
            age.Parameters.AddWithValue("@id", partFamily.Id);
            await age.ExecuteNonQueryAsync();
        }
        restoreFamily = (await parameters.GetArchivedAsync()).Families.Single(item => item.Id == partFamily.Id);
        await Rejects<ProductOperationException>(() => parameters.DeleteArchivedFamilyAsync(restoreFamily, "Ext sterge familie folosita"), "An archived family whose products still link to it is not deleted");
        await parameters.GiveUpFamilyRestoreAsync(restoreFamily, "Ext renuntare familie");
        restoreFamily = (await parameters.GetArchivedAsync()).Families.Single(item => item.Id == partFamily.Id);
        Check(restoreFamily.Unused && (await products.GetProductAsync(famA.Id))!.Name == $"FA-{suffix} - x" && (await products.GetProductAsync(famB.Id))!.Name == $"FB-{suffix} - y",
            "After the retention period giving up the restore leaves the products named as they are and the family unused");
        Check((await parameters.GetUsageAsync([left2.Id, right2.Id])).Count(item => item.Action == ParameterUsageActions.GivenUp) == 2, "Giving up is kept in the usage log, product by product");
        await parameters.DeleteArchivedFamilyAsync(restoreFamily, "Ext stergere familie nefolosita");
        Check((await parameters.GetArchivedAsync()).Families.All(item => item.Id != partFamily.Id) && (await parameters.GetArchivedAsync()).Parameters.All(item => item.FamilyId != partFamily.Id),
            "An unused archived family is deleted for good with its members");
        await settings.SaveRestoreRetentionDaysAsync(ParameterArchiveRules.DefaultRetentionDays);

        var events = (await audit.GetEventsAsync()).Where(item => item.TimestampUtc >= started).ToList();
        Check(events.Count(item => item.Action == AuditActions.AddParameterFamily) == 5 && events.Any(item => item.Action == AuditActions.EditParameterFamily && item.Motif == "Ext redenumire familie" && item.Details.Contains("Tip lentila"))
              && events.Any(item => item.Action == AuditActions.DeleteParameterFamily && item.Motif == "Ext desfacere" && item.Details.Contains("Produse devenite incomplete: 2")),
            "The journal names each family operation: added, edited, ungrouped, with the products that became incomplete");
        Check(events.Any(item => item.Action == AuditActions.ArchiveSubcategoryParameter && item.Motif == "Ext arhivare marime" && item.Details.Contains("Produse redenumite: 2"))
              && events.Any(item => item.Action == AuditActions.RestoreSubcategoryParameter && item.Motif == "Ext restaurare marime") && events.Any(item => item.Action == AuditActions.DeleteUnusedParameter && item.Motif == "Ext stergere nefolosit")
              && events.Any(item => item.Action == AuditActions.ArchiveParameterFamily && item.Motif == "Ext arhivare familie") && events.Any(item => item.Action == AuditActions.RestoreParameterFamily && item.Motif == "Ext restaurare familie")
              && events.Any(item => item.Action == AuditActions.GiveUpFamilyRestore && item.Motif == "Ext renuntare familie") && events.Any(item => item.Action == AuditActions.DeleteUnusedFamily && item.Motif == "Ext stergere familie nefolosita")
              && events.Any(item => item.Action == AuditActions.SetRestoreRetention),
            "The journal names each archive operation: archive, restore, give up, delete unused and the retention setting");
    }
}
