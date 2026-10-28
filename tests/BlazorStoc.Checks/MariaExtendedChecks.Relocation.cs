using Microsoft.Extensions.Configuration;
using BlazorStoc.Services;

namespace BlazorStoc.Checks;

public static partial class MariaExtendedChecks
{
    // The relocation service on the real database: the preview is a rolled-back run (nothing changes), the apply merges the namesakes (values united, links of the products repointed),
    // the products that lose their completeness are listed and need an explicit acceptance, a family keeps one member per product, incompatible namesakes block.
    private static async Task DefinitionRelocationAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit)
    {
        var suffix = Suffix();
        var today = DateOnly.FromDateTime(DateTime.Now);
        var products = new MariaProductRepository(configuration, admin);
        var parameters = new MariaProductParameterRepository(configuration, admin);   // not journaled: other sections count these events
        var tags = new MariaProductTagRepository(configuration, admin);
        var movements = new MariaStockMovementRepository(configuration, admin);
        var relocation = new MariaDefinitionRelocation(configuration, admin, audit);
        var userRelocation = new MariaDefinitionRelocation(configuration, new TestAccessControl(false, "utilizator.mutare"), audit);
        var started = DateTime.UtcNow.AddSeconds(-1);
        var category = $"Ext Mutare Cat {suffix}";
        var rootName = $"Ext Mutare Radacina {suffix}";
        await products.CreateCategoryAsync(category);
        var root = await products.CreateSubcategoryAsync(category, rootName);
        var c1 = await products.CreateChildAsync(root, $"Ext Mutare C1 {suffix}", false);
        var c2 = await products.CreateChildAsync(root, $"Ext Mutare C2 {suffix}", false);
        var c3 = await products.CreateChildAsync(root, $"Ext Mutare C3 {suffix}", false);

        // Two branches define the same parameter with partly different values; a third has none.
        var lens1 = await parameters.AddParameterAsync(c1, "Lentila", "mm", ParameterKind.Number);
        var v28 = await parameters.AddValueAsync(lens1.Id, "2.8");
        var v4 = await parameters.AddValueAsync(lens1.Id, "4");
        var lens2 = await parameters.AddParameterAsync(c2, "Lentila", "mm", ParameterKind.Number);
        var w28 = await parameters.AddValueAsync(lens2.Id, "2.8");
        var w6 = await parameters.AddValueAsync(lens2.Id, "6");
        ProductInput Input(ProductGroup group, string model, SubcategoryParameter? parameter = null, int valueId = 0) => new()
        {
            Name = model, BaseModel = model, Category = category, Subcategory = group.Subcategory,
            Parameters = parameter is null ? [] : [new ProductParameterChoice(parameter.Id, valueId)]
        };
        var p1 = await products.CreateAsync(Input(c1, $"MU1-{suffix}", lens1, v4.Id));
        var p2 = await products.CreateAsync(Input(c2, $"MU2-{suffix}", lens2, w6.Id));
        var p3 = await products.CreateAsync(Input(c3, $"MU3-{suffix}"));
        var poe = await tags.AddTagAsync(c1, "PoE mutare");
        await tags.SetProductTagsAsync(p1.Id, [poe.Id]);

        // The namesake in the sibling branch needs a decision; nothing is applied meanwhile.
        var ask = new RelocationRequest(root, [new DefinitionRef(DefinitionKind.Parameter, lens1.Id)]);
        var undecided = await relocation.PreviewAsync(ask);
        Check(!undecided.CanApply && undecided.NeedsDecision && undecided.Items.Single().Action == RelocationAction.NeedsDecision, "Relocation: a namesake in a sibling branch is reported as needing a decision");
        await Rejects<ProductOperationException>(() => relocation.ApplyAsync(ask, "Ext mutare fara decizie"), "Relocation: an undecided clash is not applied");
        await Rejects<AccessDeniedException>(() => userRelocation.PreviewAsync(ask), "Relocation: only an administrator relocates definitions");

        // The preview runs the real merge and rolls it back: it lists the product that becomes incomplete and leaves the database as it was.
        var confirmed = new RelocationRequest(root, [new DefinitionRef(DefinitionKind.Parameter, lens1.Id)], [new DefinitionRef(DefinitionKind.Parameter, lens1.Id)]);
        var preview = await relocation.PreviewAsync(confirmed);
        Check(preview.CanApply && preview.Items.Single().Action == RelocationAction.Absorb && preview.NeedsConfirmation && preview.Incomplete.Select(item => item.ProductId).SequenceEqual([p3.Id])
              && preview.Items.Single().Values.Count == 2 && preview.Items.Single().Values.Any(value => value.Value == "2.8 mm" && value.ExistsAtSurvivor),
            "Relocation: the preview lists the merged values and the product of the third branch that would become incomplete");
        var afterPreview = (await parameters.GetParametersAsync()).Where(item => item.Category == category && item.Name == "Lentila").ToList();
        Check(afterPreview.Count == 2 && afterPreview.All(item => item.Subcategory != rootName) && !(await parameters.GetIncompleteProductIdsAsync()).Contains(p3.Id),
            "Relocation: the preview changes nothing");

        // The incomplete products must be accepted explicitly.
        await Rejects<ProductOperationException>(() => relocation.ApplyAsync(confirmed, "Ext mutare neacceptata"), "Relocation: products that become incomplete must be accepted");
        Check((await parameters.GetParametersAsync()).Count(item => item.Category == category && item.Name == "Lentila") == 2, "Relocation: a refused apply leaves the definitions as they were");
        var applied = await relocation.ApplyAsync(confirmed with { AcceptIncomplete = true }, "Ext mutare lentila");
        var merged = (await parameters.GetParametersAsync()).Where(item => item.Category == category && item.Name == "Lentila").ToList();
        Check(merged.Count == 1 && merged[0].Subcategory == rootName && merged[0].Id == lens1.Id && merged[0].Values.Select(value => value.Value).Order().SequenceEqual(["2.8", "4", "6"]) && applied.Incomplete.Count == 1,
            "Relocation: the parameter moved to the parent absorbed the namesake of the sibling and united the values");
        var state1 = await parameters.GetProductStateAsync(p1.Id);
        var state2 = await parameters.GetProductStateAsync(p2.Id);
        var six = merged[0].Values.Single(value => value.Value == "6").Id;
        Check(state1.Choices.Single().ParameterId == lens1.Id && state1.Choices.Single().ValueId == v4.Id && state2.Choices.Single().ParameterId == lens1.Id && state2.Choices.Single().ValueId == six
              && (await products.GetProductAsync(p2.Id))!.Name == p2.Name,
            "Relocation: the products of the absorbed parameter keep their values, now linked to the surviving parameter, and keep their codes");
        var incomplete = await parameters.GetIncompleteProductIdsAsync();
        Check(incomplete.Contains(p3.Id) && !incomplete.Contains(p1.Id) && !incomplete.Contains(p2.Id), "Relocation: only the product without a value for the raised parameter is incomplete");
        var missing = await parameters.GetMissingAsync(p3);
        var refused = await Rejects<StockMovementOperationException>(() => movements.CreateAsync(p3.Id,
            new StockMovementInput { Kind = StockMovementKind.Entry, Date = today, Quantity = 1, Description = "Ext mutare blocat" }), "Relocation: the incomplete product takes no stock entry");
        Check(missing.Count == 1 && missing[0].Contains("Lentila", StringComparison.Ordinal) && refused!.Message.Contains("Lentila", StringComparison.Ordinal), "Relocation: the refusal names the raised parameter");
        var usage = await parameters.GetUsageAsync([lens1.Id]);
        Check(usage.Any(item => item.Action == ParameterUsageActions.Merged && item.ProductName == p2.Name) && usage.Any(item => item.Action == ParameterUsageActions.Relocated && item.Node == $"{category} / {rootName}"),
            "Relocation: the usage log names the merge and the new place");
        var events = (await audit.GetEventsAsync()).Where(item => item.TimestampUtc >= started).ToList();
        Check(events.Any(item => item.Action == AuditActions.MergeDefinitions && item.Motif == "Ext mutare lentila" && item.Details.Contains("Lentila", StringComparison.Ordinal)), "Relocation: the journal names the merge");

        // A tag with no namesake moves with its On/Off.
        var tagMove = await relocation.ApplyAsync(new RelocationRequest(root, [new DefinitionRef(DefinitionKind.Tag, poe.Id)]), "Ext mutare eticheta");
        Check(tagMove.Items.Single().Action == RelocationAction.Move && (await tags.GetTagsAsync()).Single(item => item.Id == poe.Id).Subcategory == rootName
              && (await tags.GetProductTagIdsAsync(p1.Id)).SequenceEqual([poe.Id]),
            "Relocation: a tag moves to the parent and the products keep it On");

        // Another unit blocks.
        var size2 = await parameters.AddParameterAsync(c2, "Marime mutare", "cm", ParameterKind.Number);
        var size3 = await parameters.AddParameterAsync(c3, "Marime mutare", "mm", ParameterKind.Number);
        var blocked = await relocation.PreviewAsync(new RelocationRequest(root, [new DefinitionRef(DefinitionKind.Parameter, size2.Id)], [new DefinitionRef(DefinitionKind.Parameter, size2.Id)]));
        Check(!blocked.CanApply && blocked.Blocks.Single().Contains("altă unitate", StringComparison.Ordinal) && size3.Id > 0, "Relocation: a namesake with another unit blocks the relocation");
        await Rejects<ProductOperationException>(() => relocation.ApplyAsync(new RelocationRequest(root, [new DefinitionRef(DefinitionKind.Parameter, size2.Id)], [new DefinitionRef(DefinitionKind.Parameter, size2.Id)]), "Ext mutare blocata"),
            "Relocation: a blocked relocation is not applied");

        // Families: the family of one child moves to the parent and absorbs the family of the same content in the other child; every product keeps exactly one member.
        var yName = $"Ext Mutare Y {suffix}";
        var y = await products.CreateSubcategoryAsync(category, yName);
        var d1 = await products.CreateChildAsync(y, $"Ext Mutare D1 {suffix}", false);
        var d2 = await products.CreateChildAsync(y, $"Ext Mutare D2 {suffix}", false);
        var wired1 = await parameters.AddParameterAsync(d1, "Cablat mutare", "", ParameterKind.Text);
        var wireless1 = await parameters.AddParameterAsync(d1, "Wireless mutare", "", ParameterKind.Text);
        var wired2 = await parameters.AddParameterAsync(d2, "Cablat mutare", "", ParameterKind.Text);
        var wireless2 = await parameters.AddParameterAsync(d2, "Wireless mutare", "", ParameterKind.Text);
        var yes1 = await parameters.AddValueAsync(wired1.Id, "da");
        var yes2 = await parameters.AddValueAsync(wired2.Id, "da");
        var no2 = await parameters.AddValueAsync(wired2.Id, "nu");
        await parameters.AddValueAsync(wireless1.Id, "da");
        await parameters.AddValueAsync(wireless2.Id, "da");
        var family1 = await parameters.CreateFamilyAsync(d1, "Conectare mutare", [wired1.Id, wireless1.Id]);
        await parameters.CreateFamilyAsync(d2, "Conectare mutare", [wired2.Id, wireless2.Id]);
        var q1 = await products.CreateAsync(Input(d1, $"MQ1-{suffix}", wired1, yes1.Id));
        var q2 = await products.CreateAsync(Input(d2, $"MQ2-{suffix}", wired2, no2.Id));
        var q3 = await products.CreateAsync(Input(d2, $"MQ3-{suffix}", wired2, yes2.Id));
        var familyRequest = new RelocationRequest(y, [new DefinitionRef(DefinitionKind.Family, family1.Id)], [new DefinitionRef(DefinitionKind.Family, family1.Id)]);
        var familyPreview = await relocation.PreviewAsync(familyRequest);
        Check(familyPreview.CanApply && familyPreview.Items.Single().Action == RelocationAction.Absorb && !familyPreview.NeedsConfirmation, "Relocation: the family preview absorbs the family of the other child and leaves no product incomplete");
        await relocation.ApplyAsync(familyRequest, "Ext mutare familie");
        var families = (await parameters.GetFamiliesAsync()).Where(item => item.Category == category && item.Name == "Conectare mutare").ToList();
        var cablat = (await parameters.GetParametersAsync()).Where(item => item.Category == category && item.Name == "Cablat mutare").ToList();
        Check(families.Count == 1 && families[0].Subcategory == yName && families[0].ParameterIds.Count == 2 && cablat.Count == 1 && cablat[0].Subcategory == yName
              && cablat[0].Values.Select(value => value.Value).Order().SequenceEqual(["da", "nu"]),
            "Relocation: one family and one member of each name remain at the parent, with the values united");
        var stateQ2 = await parameters.GetProductStateAsync(q2.Id);
        var incompleteAfter = await parameters.GetIncompleteProductIdsAsync();
        Check(stateQ2.Choices.Single().ParameterId == cablat[0].Id && stateQ2.Choices.Single().ValueId == cablat[0].Values.Single(value => value.Value == "nu").Id
              && !incompleteAfter.Contains(q1.Id) && !incompleteAfter.Contains(q2.Id) && !incompleteAfter.Contains(q3.Id),
            "Relocation: the products of the absorbed family keep their member and value, and all stay complete");
        Check((await parameters.GetParametersAsync()).All(item => item.Id != wired2.Id && item.Id != wireless2.Id) && (await products.GetProductAsync(q2.Id))!.Name == q2.Name,
            "Relocation: the absorbed members are gone and the product codes are unchanged");
    }
}
