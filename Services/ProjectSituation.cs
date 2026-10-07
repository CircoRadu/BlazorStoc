using System.Globalization;
using System.Text;

namespace BlazorStoc.Services;

// "Situatia proiectului": per component and offer line, what the offer needs, what was handed over (net of returns), what the stock holds and what is
// still missing, plus the purchase list grouped by supplier. Prices are ignored. Exits are not tied to components yet, so the handed-over pieces of a
// product are spread over the lines that need that product (in the order of the components and of the lines); what is left over is "in afara ofertei".

public enum SituationLineState { Covered = 1, Partial = 2, Missing = 3, OutOfStock = 4 }

// A line of the latest revision of an offer of the project; ProductId is null for a line "de achizitionat" (no product of the catalog yet).
public sealed record SituationInputLine(int? SystemTypeId, string ComponentName, string Section, string Name, string Unit, decimal Quantity, bool InStock, int? ProductId, string? ProductName);

public sealed record SituationInput(
    IReadOnlyList<SituationInputLine> Lines,
    IReadOnlyDictionary<int, int> Delivered,
    IReadOnlyDictionary<int, string> ProductNames,
    IReadOnlyDictionary<int, int> Stock,
    IReadOnlyDictionary<int, string> LastSupplier)
{
    // Handed-over pieces already tied to a component (system type, product) and those marked "in afara ofertei"; Delivered keeps only what is not tied yet.
    public IReadOnlyDictionary<(int SystemTypeId, int ProductId), int> DeliveredByComponent { get; init; } = new Dictionary<(int, int), int>();
    public IReadOnlyDictionary<int, int> OutsideDelivered { get; init; } = new Dictionary<int, int>();
    // What this project reserved per product, what every project reserved per product (this one included), and what entries tied to a component brought (system type, product).
    public IReadOnlyDictionary<int, int> ProjectReserved { get; init; } = new Dictionary<int, int>();
    public IReadOnlyDictionary<int, int> TotalReserved { get; init; } = new Dictionary<int, int>();
    public IReadOnlyDictionary<(int SystemTypeId, int ProductId), int> Received { get; init; } = new Dictionary<(int, int), int>();
}

// Reserved = the part of FromStock that is reserved for this project; Received = pieces that entries tied to the component brought for this line (information only).
public sealed record SituationLine(string Section, string Name, string Unit, decimal Quantity, bool InStock, int? ProductId, decimal Delivered, decimal FromStock, decimal Deficit,
    SituationLineState State, decimal Reserved = 0, decimal Received = 0);

public sealed record SituationComponent(int? SystemTypeId, string Name, IReadOnlyList<SituationLine> Lines)
{
    // Only the lines in pieces count in "predat X din Y" (meters and hours are outside the stock).
    public decimal Needed => Lines.Where(line => line.InStock).Sum(line => line.Quantity);
    public decimal Delivered => Lines.Where(line => line.InStock).Sum(line => line.Delivered);
}

public sealed record OutsideOfferItem(int ProductId, string ProductName, int Quantity);
public sealed record PurchaseItem(string Name, string Unit, decimal Quantity);
public sealed record PurchaseGroup(string Supplier, IReadOnlyList<PurchaseItem> Items);

public sealed record ProjectSituation(int ProjectId, string ProjectName, IReadOnlyList<SituationComponent> Components, IReadOnlyList<OutsideOfferItem> OutsideOffer,
    IReadOnlyList<PurchaseGroup> Purchase)
{
    public bool IsEmpty => Components.Count == 0 && OutsideOffer.Count == 0;
}

// After an entry: a project whose need for the product is covered by free stock but not reserved yet (Quantity = what to reserve, never above the free stock).
public sealed record ReserveSuggestion(int ProjectId, string ProjectName, int? ComponentId, int ProductId, string ProductName, int Quantity);

public interface IProjectSituationReader
{
    Task<ProjectSituation> GetAsync(int projectId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReserveSuggestion>> GetReserveSuggestionsAsync(int productId, CancellationToken cancellationToken = default);
}

public static class ProjectSituationRules
{
    public const string UnknownSupplier = "Furnizor necunoscut";
    public const string NoComponent = "Fără componentă";

    public static string StateLabel(SituationLineState state) => state switch
    {
        SituationLineState.Covered => "Acoperit",
        SituationLineState.Partial => "Parțial",
        SituationLineState.Missing => "Lipsă",
        _ => "În afara stocului"
    };

    private sealed class Work(SituationInputLine source)
    {
        public SituationInputLine Source { get; } = source;
        public decimal Delivered, FromStock, Reserved, Received;
    }

    public static ProjectSituation Compute(int projectId, string projectName, SituationInput input)
    {
        var work = input.Lines.Select(line => new Work(line)).ToList();
        var deliveredPool = input.Delivered.Where(pair => pair.Value > 0).ToDictionary(pair => pair.Key, pair => (decimal)pair.Value);
        var outsidePool = input.OutsideDelivered.Where(pair => pair.Value > 0).ToDictionary(pair => pair.Key, pair => (decimal)pair.Value);

        // 0. What was handed over for a component fills the lines of that component; a surplus counts as outside the offer.
        var componentPool = input.DeliveredByComponent.Where(pair => pair.Value > 0).ToDictionary(pair => pair.Key, pair => (decimal)pair.Value);
        foreach (var item in work.Where(item => item.Source is { InStock: true, ProductId: not null, SystemTypeId: not null }))
        {
            var key = (item.Source.SystemTypeId!.Value, item.Source.ProductId!.Value);
            if (!componentPool.TryGetValue(key, out var pool) || pool <= 0) continue;
            item.Delivered = Math.Min(item.Source.Quantity, pool);
            componentPool[key] = pool - item.Delivered;
        }
        foreach (var (key, left) in componentPool.Where(pair => pair.Value > 0))
            outsidePool[key.ProductId] = outsidePool.GetValueOrDefault(key.ProductId) + left;

        // 1. What was handed over without a component fills the lines that still need the product, in order.
        foreach (var item in work.Where(item => item.Source is { InStock: true, ProductId: not null }))
        {
            var id = item.Source.ProductId!.Value;
            if (!deliveredPool.TryGetValue(id, out var pool) || pool <= 0) continue;
            var take = Math.Min(item.Source.Quantity - item.Delivered, pool);
            item.Delivered += take;
            deliveredPool[id] = pool - take;
        }
        // 2. The stock (never below zero) covers what is still missing, in the same order.
        // The project can count on its own reservation plus the free stock (what the others did not reserve).
        var stockPool = new Dictionary<int, decimal>();
        var reservePool = new Dictionary<int, decimal>();
        foreach (var item in work.Where(item => item.Source is { InStock: true, ProductId: not null }))
        {
            var id = item.Source.ProductId!.Value;
            if (!stockPool.TryGetValue(id, out var pool))
            {
                var physical = Math.Max(0, input.Stock.GetValueOrDefault(id));
                var own = Math.Max(0, input.ProjectReserved.GetValueOrDefault(id));
                var free = Math.Max(0, physical - Math.Max(own, input.TotalReserved.GetValueOrDefault(id)));
                pool = Math.Min(physical, own + free);
                reservePool[id] = Math.Min(own, pool);
            }
            item.FromStock = Math.Min(item.Source.Quantity - item.Delivered, pool);
            stockPool[id] = pool - item.FromStock;
            item.Reserved = Math.Min(item.FromStock, reservePool[id]);
            reservePool[id] -= item.Reserved;
        }
        // Entries tied to a component are shown against the lines of that component (information only).
        var receivedPool = input.Received.Where(pair => pair.Value > 0).ToDictionary(pair => pair.Key, pair => (decimal)pair.Value);
        foreach (var item in work.Where(item => item.Source is { InStock: true, ProductId: not null, SystemTypeId: not null }))
        {
            var key = (item.Source.SystemTypeId!.Value, item.Source.ProductId!.Value);
            if (!receivedPool.TryGetValue(key, out var pool) || pool <= 0) continue;
            item.Received = Math.Min(item.Source.Quantity, pool);
            receivedPool[key] = pool - item.Received;
        }

        SituationLine ToLine(Work item)
        {
            var source = item.Source;
            var deficit = source.InStock ? source.Quantity - item.Delivered - item.FromStock : 0;
            var state = !source.InStock ? SituationLineState.OutOfStock
                : deficit <= 0 ? SituationLineState.Covered
                : item.Delivered + item.FromStock > 0 ? SituationLineState.Partial : SituationLineState.Missing;
            return new SituationLine(source.Section, source.Name, source.Unit, source.Quantity, source.InStock, source.ProductId, item.Delivered, item.FromStock, deficit, state, item.Reserved, item.Received);
        }

        var components = new List<SituationComponent>();
        foreach (var group in work.GroupBy(item => (item.Source.SystemTypeId, item.Source.ComponentName)))
            components.Add(new SituationComponent(group.Key.SystemTypeId, group.Key.ComponentName, [.. group.Select(ToLine)]));

        foreach (var (id, left) in deliveredPool.Where(pair => pair.Value > 0)) outsidePool[id] = outsidePool.GetValueOrDefault(id) + left;
        var outside = outsidePool.Where(pair => pair.Value > 0)
            .Select(pair => new OutsideOfferItem(pair.Key, input.ProductNames.GetValueOrDefault(pair.Key) ?? $"#{pair.Key}", (int)pair.Value))
            .OrderBy(item => item.ProductName, StringComparer.CurrentCultureIgnoreCase).ToList();

        // The purchase list: the deficit per product (lines without a product stay separate), grouped by the last supplier of the product.
        var purchase = new Dictionary<string, Dictionary<string, (string Unit, decimal Quantity)>>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var line in components.SelectMany(component => component.Lines).Where(line => line.Deficit > 0))
        {
            var supplier = line.ProductId is { } id && input.LastSupplier.TryGetValue(id, out var name) ? name : UnknownSupplier;
            var label = line.ProductId is { } productId ? input.ProductNames.GetValueOrDefault(productId) ?? line.Name : FirstLine(line.Name);
            if (!purchase.TryGetValue(supplier, out var items)) purchase[supplier] = items = new(StringComparer.CurrentCultureIgnoreCase);
            items[label] = items.TryGetValue(label, out var existing) ? (existing.Unit, existing.Quantity + line.Deficit) : (line.Unit, line.Deficit);
        }
        var groups = purchase
            .OrderBy(pair => pair.Key == UnknownSupplier ? 1 : 0).ThenBy(pair => pair.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(pair => new PurchaseGroup(pair.Key, [.. pair.Value.OrderBy(item => item.Key, StringComparer.CurrentCultureIgnoreCase).Select(item => new PurchaseItem(item.Key, item.Value.Unit, item.Value.Quantity))]))
            .ToList();
        return new ProjectSituation(projectId, projectName, components, outside, groups);
    }

    public static string FirstLine(string text) => text.Split('\n')[0].Trim();

    public static string Format(decimal quantity) => quantity.ToString("0.###", CultureInfo.InvariantCulture);

    // CSV (semicolon, UTF-8 with BOM added by the caller): one row per offer line, then the leftovers and the purchase list.
    public static string ToCsv(ProjectSituation situation)
    {
        var text = new StringBuilder();
        static string Cell(string value) => value.Contains(';') || value.Contains('"') || value.Contains('\n') ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
        void Row(params string[] cells) => text.Append(string.Join(';', cells.Select(Cell))).Append("\r\n");
        Row("Proiect", situation.ProjectName);
        Row();
        Row("Componenta", "Sectiune", "Denumire", "UM", "Necesar", "Predat", "Din stoc", "Din care rezervat", "Intrat pentru oferta", "Deficit", "Stare");
        foreach (var component in situation.Components)
            foreach (var line in component.Lines)
                Row(component.Name, line.Section, FirstLine(line.Name), line.Unit, Format(line.Quantity), Format(line.Delivered), Format(line.FromStock), Format(line.Reserved), Format(line.Received), Format(line.Deficit), StateLabel(line.State));
        if (situation.OutsideOffer.Count > 0)
        {
            Row();
            Row("In afara ofertei", "Cantitate predata");
            foreach (var item in situation.OutsideOffer) Row(item.ProductName, item.Quantity.ToString(CultureInfo.InvariantCulture));
        }
        Row();
        Row("Lista de achizitie", "Furnizor", "Denumire", "UM", "Cantitate");
        foreach (var group in situation.Purchase)
            foreach (var item in group.Items) Row("", group.Supplier, item.Name, item.Unit, Format(item.Quantity));
        return text.ToString();
    }
}
