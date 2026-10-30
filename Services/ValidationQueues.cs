namespace BlazorStoc.Services;

// What waits for a human decision from ActivityApp: the tickets proposed as maintenance and the beneficiaries to verify. Each count is given only to who may validate that kind (the
// administrator, or an operator with "mentenanta.validare" / "beneficiari.validare"); the Notifications page shows banners and red tabs from it.
public sealed record ValidationQueueCounts(int Tickets, int Beneficiaries, bool CanTickets, bool CanBeneficiaries)
{
    public static ValidationQueueCounts Empty { get; } = new(0, 0, false, false);
    public bool AnyWaiting => (CanTickets && Tickets > 0) || (CanBeneficiaries && Beneficiaries > 0);
}

public interface IValidationQueues
{
    Task<ValidationQueueCounts> GetAsync(CancellationToken cancellationToken = default);
}
