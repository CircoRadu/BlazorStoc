using Microsoft.Extensions.Configuration;
using MySqlConnector;
using BlazorStoc.Services;

namespace BlazorStoc.Checks;

// Tags of a subcategory on MariaDB (migration 53): optional On/Off marks on the products, never part of the code, never blocking the stock.
public static partial class MariaExtendedChecks
{
    private static async Task SubcategoryTagsAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe)
    {
        var suffix = Suffix();
        var started = DateTime.UtcNow.AddSeconds(-1);
        var today = DateOnly.FromDateTime(DateTime.Now);
        var user = new TestAccessControl(false, "utilizator.etichete");
        var products = new MariaProductRepository(configuration, admin, audit);
        var tags = new MariaProductTagRepository(configuration, admin, audit);
        var userTags = new MariaProductTagRepository(configuration, user, audit);
        var movements = new MariaStockMovementRepository(configuration, admin, audit);
        var category = $"Ext Tag Cat {suffix}";
        var subcategory = $"Ext Tag Sub {suffix}";
        var otherSubcategory = $"Ext Tag Alta {suffix}";
        await products.CreateCategoryAsync(category);
        await products.CreateSubcategoryAsync(category, subcategory);
        await products.CreateSubcategoryAsync(category, otherSubcategory);
        var group = new ProductGroup(category, subcategory);

        var poe = await tags.AddTagAsync(group, "PoE");
        var audio = await tags.AddTagAsync(group, "Audio");
        var foreign = await tags.AddTagAsync(new ProductGroup(category, otherSubcategory), "Straina");
        Check(poe.Position == 1 && audio.Position == 2 && (await tags.GetTagsAsync()).Count(item => item.Category == category) == 3, "Tags are added to a subcategory in order");
        await Rejects<ProductOperationException>(() => tags.AddTagAsync(group, " poe "), "The same tag twice in a subcategory is refused (case and spaces aside)");
        await Rejects<ProductOperationException>(() => tags.AddTagAsync(group, "   "), "A tag needs a name");
        await Rejects<AccessDeniedException>(() => userTags.RenameTagAsync(poe, "PoE+", "Ext redenumire"), "Only an administrator renames a tag");
        await Rejects<AccessDeniedException>(() => userTags.DeleteTagAsync(poe, "Ext stergere"), "Only an administrator deletes a tag");

        // A product: tags never change its code and never block the stock.
        var camera = await products.CreateAsync(new ProductInput { Name = $"ETI-{suffix}", Category = category, Subcategory = subcategory });
        var plain = await products.CreateAsync(new ProductInput { Name = $"ETI2-{suffix}", Category = category, Subcategory = subcategory });
        Check((await tags.GetProductTagIdsAsync(camera.Id)).Count == 0, "A new product has every tag Off");
        await tags.SetProductTagsAsync(camera.Id, [poe.Id, audio.Id, foreign.Id, poe.Id]);
        var on = await tags.GetProductTagIdsAsync(camera.Id);
        Check(on.Count == 2 && on.Contains(poe.Id) && on.Contains(audio.Id), "Tags of another subcategory are ignored and a tag counts once");
        Check((await products.GetProductAsync(camera.Id))!.Name == $"ETI-{suffix}", "Tags are not part of the product code");
        await movements.CreateAsync(camera.Id, new StockMovementInput { Kind = StockMovementKind.Entry, Date = today, Quantity = 3, Description = "Ext etichete intrare" });
        Check((await products.GetProductAsync(camera.Id))!.Quantity == 3, "A product with tags takes stock entries like any other");
        await tags.SetProductTagsAsync(plain.Id, [poe.Id]);
        var all = await tags.GetAllProductTagsAsync();
        Check(all[camera.Id].Count == 2 && all[plain.Id].SequenceEqual([poe.Id]), "The catalog reads the tags of every product in one call");
        Check((await tags.GetTagsAsync()).Single(item => item.Id == poe.Id).ProductCount == 2, "A tag counts the products that have it On");

        // Journal: one entry per real change, none when nothing changed.
        var before = (await audit.GetEventsAsync()).Count(item => item.TimestampUtc >= started && item.Action == AuditActions.EditProductTags);
        await tags.SetProductTagsAsync(camera.Id, [audio.Id, poe.Id]);
        Check((await audit.GetEventsAsync()).Count(item => item.TimestampUtc >= started && item.Action == AuditActions.EditProductTags) == before, "Saving the same tags writes nothing to the journal");
        await tags.SetProductTagsAsync(camera.Id, [audio.Id]);

        // Rename (administrator, outdated version refused) and delete while products have the tag.
        var renamed = await tags.RenameTagAsync(poe, "PoE+", "Ext redenumire");
        Check(renamed.Name == "PoE+" && renamed.Version == poe.Version + 1 && (await tags.GetTagsAsync()).Any(item => item.Id == poe.Id && item.Name == "PoE+"), "A tag is renamed with a new version");
        await Rejects<ProductOperationException>(() => tags.RenameTagAsync(poe, "Alta", "Ext versiune veche"), "An outdated tag version is refused");
        await Rejects<ProductOperationException>(() => tags.RenameTagAsync(renamed, "Audio", "Ext duplicat"), "A tag cannot take the name of another tag");
        await tags.DeleteTagAsync(renamed, "Ext stergere eticheta");
        Check((await tags.GetTagsAsync()).All(item => item.Id != poe.Id) && (await tags.GetProductTagIdsAsync(plain.Id)).Count == 0 && (await tags.GetProductTagIdsAsync(camera.Id)).SequenceEqual([audio.Id]),
            "Deleting a tag that products have On removes it from them and leaves the rest");

        var events = (await audit.GetEventsAsync()).Where(item => item.TimestampUtc >= started).ToList();
        Check(events.Count(item => item.Action == AuditActions.AddSubcategoryTag) == 3 && events.Any(item => item.Action == AuditActions.RenameSubcategoryTag && item.Motif == "Ext redenumire" && item.Details.Contains("PoE+"))
              && events.Any(item => item.Action == AuditActions.DeleteSubcategoryTag && item.Motif == "Ext stergere eticheta" && item.Details.Contains("Produse care o aveau: 1"))
              && events.Count(item => item.Action == AuditActions.EditProductTags) == 3,
            "The journal names each tag operation");
    }
}
