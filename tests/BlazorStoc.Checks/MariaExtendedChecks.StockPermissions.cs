using Microsoft.Extensions.Configuration;
using MySqlConnector;
using BlazorStoc.Services;

namespace BlazorStoc.Checks;

// A user who has only some of the stock permissions: every kind of movement asks for its own key.
sealed class KeyAccessControl(string username, params string[] keys) : IAccessControl
{
    public Task<bool> IsAdministratorAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
    public Task<bool> CanManageProductsAsync(CancellationToken cancellationToken = default) => Task.FromResult(keys.Length > 0);
    public Task<bool> CanManageBeneficiariesAsync(CancellationToken cancellationToken = default) => Task.FromResult(keys.Length > 0);
    public Task<string?> GetUsernameAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(username);
    public Task EnsureAdministratorAsync(CancellationToken cancellationToken = default) => Task.FromException(new AccessDeniedException("Test access denied"));
    public Task EnsureProductOperatorAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task EnsureBeneficiaryOperatorAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<bool> HasAsync(string permission, CancellationToken cancellationToken = default) => Task.FromResult(keys.Contains(permission));
}

public static partial class MariaExtendedChecks
{
    private static async Task StockPermissionsAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit)
    {
        var suffix = Suffix();
        var category = $"Ext Drepturi Cat {suffix}";
        var subcategory = $"Ext Drepturi Sub {suffix}";
        var products = new MariaProductRepository(configuration, admin, audit);
        await products.CreateCategoryAsync(category);
        await products.CreateSubcategoryAsync(category, subcategory);
        var product = await products.CreateAsync(new ProductInput { Name = $"Ext Drepturi {suffix}", Category = category, Subcategory = subcategory });
        var today = DateOnly.FromDateTime(DateTime.Now);
        var entry = new StockMovementInput { Kind = StockMovementKind.Entry, Date = today, Quantity = 5, Description = "Ext intrare drepturi" };
        var exit = new StockMovementInput { Kind = StockMovementKind.Exit, Date = today, Quantity = 1, Description = "Ext iesire drepturi" };
        MariaStockMovementRepository As(params string[] keys) => new(configuration, new KeyAccessControl("ext.drepturi", keys), audit);

        var adminMovements = new MariaStockMovementRepository(configuration, admin, audit);
        var seeded = await adminMovements.CreateAsync(product.Id, entry);
        await Rejects<AccessDeniedException>(() => As("stoc.modificare").CreateAsync(product.Id, entry), "Only \"Modificare miscare\": a new entry is refused");
        await Rejects<AccessDeniedException>(() => As("stoc.modificare").CreateAsync(product.Id, exit), "Only \"Modificare miscare\": a new exit is refused");
        await Rejects<AccessDeniedException>(() => As("stoc.intrare").CreateAsync(product.Id, exit), "Only \"Intrare in stoc\": an exit is refused");
        await Rejects<AccessDeniedException>(() => As("stoc.iesire").CreateAsync(product.Id, entry), "Only \"Iesire din stoc\": an entry is refused");
        var created = await As("stoc.intrare").CreateAsync(product.Id, entry);
        Check(created.Movement.Quantity == 5, "With \"Intrare in stoc\" a free entry is made");

        var edited = StockMovementInput.From(created.Movement); edited.Description = "Ext modificat drepturi"; edited.Reason = "Ext corectie";
        await Rejects<AccessDeniedException>(() => As("stoc.intrare", "stoc.iesire").UpdateAsync(created.Movement, edited), "Without \"Modificare miscare\" a movement cannot be edited");
        await Rejects<AccessDeniedException>(() => As("stoc.intrare", "stoc.iesire").DeleteAsync(created.Movement, "Ext stergere drepturi"), "Without \"Modificare miscare\" a movement cannot be deleted");
        var updated = await As("stoc.modificare").UpdateAsync(created.Movement, edited);
        Check(updated.Movement.Description == "Ext modificat drepturi", "With \"Modificare miscare\" a movement is edited");
        await Rejects<AccessDeniedException>(() => As("stoc.intrare").VoidExitOperationAsync(1, "Ext stornare drepturi"), "Without \"Stornare / returnare\" an exit operation cannot be voided");
        await Rejects<AccessDeniedException>(() => As().GetPageAsync(product.Id, new StockMovementQuery()), "Without any stock or related view key the movements cannot be read");
        Check((await As("stoc.view").GetPageAsync(product.Id, new StockMovementQuery())).Stock == 10 && seeded.Movement.Id > 0, "With \"Vizualizare stoc\" the movements are read");
        // The consumption note of a generic sale (no beneficiary, no project, no reference) is produced like any other.
        var genericSale = await adminMovements.CreateAsync(product.Id, new StockMovementInput { Kind = StockMovementKind.Exit, Date = today, Quantity = 1, Description = "Ext vanzare generica", Destination = ExitDestination.GenericSale });
        var saleOperation = await adminMovements.GetOperationAsync(genericSale.Movement.OperationId!.Value);
        Check(saleOperation is not null && new ConsumptionNotePdfWriter().Write(saleOperation, DateTime.Now).Length > 500, "The consumption note of a generic sale can be read and written");

        // Parameters of a subcategory belong to editing the categories: adding or deleting alone is not enough.
        var group = (await products.GetGroupsAsync()).Single(item => item.Category == category && item.Subcategory == subcategory);
        MariaProductParameterRepository Parameters(params string[] keys) => new(configuration, new KeyAccessControl("ext.drepturi", keys), audit);
        await Rejects<AccessDeniedException>(() => Parameters("categorii.add", "categorii.delete").AddParameterAsync(group, "Culoare", "", ParameterKind.Text),
            "Without \"Editare\" on the categories a parameter cannot be added");
        var parameter = await Parameters("categorii.edit").AddParameterAsync(group, "Culoare", "", ParameterKind.Text);
        await Rejects<AccessDeniedException>(() => Parameters("categorii.add", "categorii.delete").AddValueAsync(parameter.Id, "Rosu"), "Without \"Editare\" on the categories a parameter value cannot be added");
        var value = await Parameters("categorii.edit").AddValueAsync(parameter.Id, "Rosu");
        await Rejects<AccessDeniedException>(() => Parameters("categorii.delete").DeleteValueAsync(value.Id, "Ext stergere valoare"), "Without \"Editare\" on the categories a parameter value cannot be deleted");

        // The minimum stock and the reservations have their own keys.
        MariaProductMinStockRepository Minimum(params string[] keys) => new(configuration, new KeyAccessControl("ext.drepturi", keys), audit);
        await Rejects<AccessDeniedException>(() => Minimum("stoc.intrare", "stoc.view").SetAsync(product.Id, 3), "Without \"Setare stoc minim\" the minimum stock cannot be set");
        await Minimum("stoc.minim").SetAsync(product.Id, 3);
        Check(await Minimum("stoc.view").GetAsync(product.Id) == 3, "With \"Setare stoc minim\" the minimum is set, and \"Vizualizare stoc\" reads it");
        await Rejects<AccessDeniedException>(() => Minimum("stoc.intrare").RemoveAsync(product.Id), "Without \"Setare stoc minim\" the minimum stock cannot be removed");
        await Minimum("stoc.minim").RemoveAsync(product.Id);
        await Rejects<AccessDeniedException>(() => new MariaReservationRepository(configuration, new KeyAccessControl("ext.drepturi", "stoc.intrare", "stoc.view")).ReserveAsync(1, null, product.Id, 1),
            "Without \"Rezervare produse pentru proiect\" nothing can be reserved");
    }
}
