using MySqlConnector;

namespace BlazorStoc.Services;

// Gathers what the situation of a project needs: the lines of the latest revision of each offer of the project (for the components that are not taken
// out), the net consumption of the project, the stock of the products and the last supplier each product was received from (invoice or free entry).
// Read only; needs a product operator like the offers and the exits.
public sealed class MariaProjectSituationReader(
    IConfiguration configuration,
    IProjectRepository projects,
    IProjectComponentRepository components,
    IOfferRepository offers,
    IStockMovementRepository movements,
    IProductRepository products,
    IReservationRepository reservations,
    IAccessControl? accessControl = null) : IProjectSituationReader
{
    public async Task<ProjectSituation> GetAsync(int projectId, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAsync("beneficiari.view", cancellationToken).ConfigureAwait(false);
        var project = await projects.GetAsync(projectId, cancellationToken).ConfigureAwait(false) ?? throw new OfferException(OfferMessages.ProjectMissing);
        var all = await components.GetForProjectAsync(projectId, true, cancellationToken).ConfigureAwait(false);
        var archived = all.Where(component => component.Archived).Select(component => component.SystemTypeId).ToHashSet();

        // The latest revision of each offer number; an offer of a component that was taken out is not part of the situation.
        var latest = (await offers.GetForProjectAsync(projectId, cancellationToken).ConfigureAwait(false))
            .GroupBy(offer => OfferRules.Key(offer.Number)).Select(group => group.OrderByDescending(offer => offer.Revision).First())
            .Where(offer => offer.SystemTypeId is null || !archived.Contains(offer.SystemTypeId.Value))
            .OrderBy(offer => offer.SystemTypeName ?? "￿", StringComparer.CurrentCultureIgnoreCase).ThenBy(offer => offer.Number, StringComparer.Ordinal).ToList();
        var lines = new List<SituationInputLine>();
        foreach (var offer in latest)
            foreach (var line in await offers.GetLinesAsync(offer.Id, cancellationToken).ConfigureAwait(false))
                lines.Add(new SituationInputLine(offer.SystemTypeId, offer.SystemTypeName ?? ProjectSituationRules.NoComponent, line.Section, line.Name, line.Unit, line.Quantity,
                    line.InStock, line.ProductId, line.ProductName));

        var names = new Dictionary<int, string>();
        var delivered = new Dictionary<int, int>();
        var byComponent = new Dictionary<(int, int), int>();
        var outsideDelivered = new Dictionary<int, int>();
        foreach (var item in await components.GetNetByComponentAsync(projectId, cancellationToken).ConfigureAwait(false))
        {
            names[item.ProductId] = item.ProductName;
            if (item.Net <= 0) continue;
            if (item.OutsideOffer) outsideDelivered[item.ProductId] = outsideDelivered.GetValueOrDefault(item.ProductId) + item.Net;
            else if (item.SystemTypeId is { } typeId) byComponent[(typeId, item.ProductId)] = byComponent.GetValueOrDefault((typeId, item.ProductId)) + item.Net;
            else delivered[item.ProductId] = delivered.GetValueOrDefault(item.ProductId) + item.Net;
        }
        foreach (var line in lines.Where(line => line.ProductId is not null && line.ProductName is not null)) names[line.ProductId!.Value] = line.ProductName!;

        var ids = lines.Where(line => line is { InStock: true, ProductId: not null }).Select(line => line.ProductId!.Value).Distinct().ToList();
        var stock = new Dictionary<int, int>();
        foreach (var id in ids)
            if (await products.GetProductAsync(id, cancellationToken).ConfigureAwait(false) is { } product) stock[id] = product.Quantity;
        var suppliers = await LastSuppliersAsync(ids, cancellationToken).ConfigureAwait(false);
        var projectReserved = new Dictionary<int, int>();
        foreach (var reservation in await reservations.GetForProjectAsync(projectId, cancellationToken).ConfigureAwait(false))
            projectReserved[reservation.ProductId] = projectReserved.GetValueOrDefault(reservation.ProductId) + reservation.Quantity;
        var totalReserved = new Dictionary<int, int>();
        foreach (var id in ids) totalReserved[id] = (await reservations.GetHoldersAsync(id, cancellationToken).ConfigureAwait(false)).Sum(holder => holder.Quantity);
        var received = await components.GetReceivedAsync(projectId, cancellationToken).ConfigureAwait(false);
        return ProjectSituationRules.Compute(projectId, project.Name, new SituationInput(lines, delivered, names, stock, suppliers) { DeliveredByComponent = byComponent, OutsideDelivered = outsideDelivered, ProjectReserved = projectReserved, TotalReserved = totalReserved, Received = received });
    }

    public async Task<IReadOnlyList<ReserveSuggestion>> GetReserveSuggestionsAsync(int productId, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAsync("beneficiari.view", cancellationToken).ConfigureAwait(false);
        var product = await products.GetProductAsync(productId, cancellationToken).ConfigureAwait(false);
        if (product is null) return [];
        var free = ReservationRules.Free(product.Quantity, (await reservations.GetHoldersAsync(productId, cancellationToken).ConfigureAwait(false)).Sum(holder => holder.Quantity));
        var result = new List<ReserveSuggestion>();
        foreach (var group in (await components.GetComponentsWithProductAsync(productId, cancellationToken).ConfigureAwait(false)).GroupBy(item => item.ProjectId))
        {
            if (free <= 0) break;
            var situation = await GetAsync(group.Key, cancellationToken).ConfigureAwait(false);
            var active = await components.GetForProjectAsync(group.Key, false, cancellationToken).ConfigureAwait(false);
            foreach (var component in situation.Components)
            {
                // Covered by stock but not by the reservation of the project: the part that can be reserved now.
                var unreserved = component.Lines.Where(line => line is { InStock: true } && line.ProductId == productId).Sum(line => line.FromStock - line.Reserved);
                var quantity = (int)Math.Min(unreserved, free);
                if (quantity <= 0) continue;
                result.Add(new ReserveSuggestion(group.Key, situation.ProjectName, active.FirstOrDefault(item => item.SystemTypeId == component.SystemTypeId)?.Id, productId, product.Name, quantity));
                free -= quantity;
                if (free <= 0) break;
            }
        }
        return result;
    }

    private async Task<IReadOnlyDictionary<int, string>> LastSuppliersAsync(List<int> productIds, CancellationToken cancellationToken)
    {
        var result = new Dictionary<int, string>();
        if (productIds.Count == 0) return result;
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        // The newest entry of each product that names a supplier (through its invoice, or free): by date, then by id.
        await using var command = new MySqlCommand($"""
            SELECT m.product_id,COALESCE(su.name,fs.name) FROM stock_movements m
            LEFT JOIN supplier_invoices si ON si.id=m.invoice_id LEFT JOIN suppliers su ON su.id=si.supplier_id LEFT JOIN suppliers fs ON fs.id=m.free_supplier_id
            WHERE m.kind=1 AND m.voided_utc IS NULL AND m.product_id IN ({string.Join(",", productIds)}) AND COALESCE(su.name,fs.name) IS NOT NULL
            ORDER BY m.id
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result[checked((int)reader.GetInt64(0))] = reader.GetString(1);
        return result;
    }
}
