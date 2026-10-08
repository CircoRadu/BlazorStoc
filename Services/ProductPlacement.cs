namespace BlazorStoc.Services;

// What was handed over to a beneficiary (and project, when the exit names one) of a product: the exits to the beneficiary minus the returns tied to them.
public sealed record ProductDelivery(int? BeneficiaryId, string BeneficiaryName, int? ProjectId, string? ProjectName, int Exited, int Returned)
{
    public int Net => Exited - Returned;
}

// Read only data for the card "Unde sunt bucatile" of a product (the warehouse and vehicle quantities already come from the stock movement repository).
public interface IProductPlacementReader
{
    Task<IReadOnlyList<ProductDelivery>> GetDeliveriesAsync(int productId, CancellationToken cancellationToken = default);
}

public static class ProductPlacementRules
{
    // Only what is still at the beneficiary counts; the biggest quantities first.
    public static IReadOnlyList<ProductDelivery> StillDelivered(IEnumerable<ProductDelivery> deliveries) =>
        deliveries.Where(item => item.Net > 0).OrderByDescending(item => item.Net).ThenBy(item => item.BeneficiaryName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.ProjectName, StringComparer.CurrentCultureIgnoreCase).ToArray();
}
