namespace BlazorStoc.Services;

// Rezervari pe proiect: a reservation holds pieces of a product for a project (optionally for one of its components) WITHOUT changing the stock. It lowers the
// free stock = stock - the sum of the remaining reservations (never below zero; a negative stock has no free stock). Nothing is ever blocked: an exit for the
// project consumes its own reservation first; the rest is compared with the free stock and, when it would touch pieces reserved by other projects, the user is
// warned and chooses (continue without touching them, lower the reservation of a chosen project with a short reason, or give up).

public sealed record Reservation(int Id, int ProjectId, string ProjectName, int? ComponentId, string? ComponentName, int ProductId, string ProductName, int Quantity);

// A project that holds pieces of a product (what a warning shows).
public sealed record ReservationHolder(int ProjectId, string ProjectName, int Quantity);

// An exit would take `Excess` pieces that other projects reserved.
public sealed class ReservationWarningException(int excess, IReadOnlyList<ReservationHolder> holders)
    : StockMovementOperationException(ReservationRules.WarningMessage(excess, holders))
{
    public int Excess { get; } = excess;
    public IReadOnlyList<ReservationHolder> Holders { get; } = holders;
}

public enum ReservationChoiceMode { None = 0, Continue = 1, Reduce = 2 }

// What the user chose in the warning of an exit that takes reserved pieces; applied to the exit input before it is sent again.
public sealed class ReservationChoice
{
    public ReservationChoiceMode Mode { get; set; }
    public int? ProjectId { get; set; }
    public int? Quantity { get; set; }
    public string Reason { get; set; } = "";

    // Null when the choice is complete, otherwise what is missing.
    public string? Validate() => Mode switch
    {
        ReservationChoiceMode.Continue => null,
        ReservationChoiceMode.Reduce when ProjectId is null => "Alege proiectul a cărui rezervare se scade.",
        ReservationChoiceMode.Reduce when Quantity is not > 0 => ReservationRules.QuantityMessage,
        ReservationChoiceMode.Reduce => ReservationRules.ReasonError(Reason) is { Length: > 0 } problem ? problem : null,
        _ => "Alege ce se întâmplă cu rezervările: continuă sau scade rezervarea unui proiect."
    };

    public void ApplyTo(StockMovementInput input)
    {
        input.ReservationAck = Mode == ReservationChoiceMode.Continue;
        input.ReduceReservationProjectId = Mode == ReservationChoiceMode.Reduce ? ProjectId : null;
        input.ReduceReservationQuantity = Mode == ReservationChoiceMode.Reduce ? Quantity : null;
        input.ReduceReservationReason = Mode == ReservationChoiceMode.Reduce ? Reason : "";
    }
}

public static class ReservationChoiceExtensions
{
    // Applies the chosen handling to an exit input (only when saving; the preview leaves the exit undecided).
    public static StockMovementInput WithReservation(this StockMovementInput input, ReservationChoice choice, bool apply)
    {
        if (apply) choice.ApplyTo(input);
        return input;
    }
}

public class ReservationException(string message) : Exception(message);

public static class ReservationRules
{
    public const string QuantityMessage = "Cantitatea rezervată trebuie să fie un număr întreg pozitiv.";
    public const string ProjectMissingMessage = "Proiectul nu mai există.";
    public const string ComponentInvalidMessage = "Componenta aleasă nu aparține proiectului sau este scoasă din proiect.";
    public const string ProductMissingMessage = "Produsul nu mai există.";
    public const string ReservationMissingMessage = "Rezervarea nu mai există. Actualizează pagina.";
    public const string ReduceTooMuchMessage = "Nu poți scădea mai mult decât este rezervat.";
    public const string ReasonMessage = "Completează motivul scăderii rezervării.";
    public const int ReasonMaximumLength = 200;

    public static string NotEnoughFreeMessage(int free) => $"Stocul liber este insuficient: se pot rezerva cel mult {free} buc.";

    // Free stock: what the warehouse holds minus every reservation, never below zero (a negative stock has nothing free).
    public static int Free(int stock, int reservedTotal) => Math.Max(0, Math.Max(0, stock) - Math.Max(0, reservedTotal));

    // Pieces of OTHER projects' reservations an exit of `quantity` would take: the exit first consumes `own` (the reservation of its own project), the rest comes
    // from the free stock, and only what is left beyond that touches reservations of the others (limited by what the stock physically holds).
    public static int Touched(int quantity, int stock, int reservedTotal, int own)
    {
        var consumed = Math.Min(quantity, Math.Max(0, own));
        var physical = Math.Max(0, stock);
        var free = Math.Max(0, physical - Math.Max(0, reservedTotal));
        return Math.Max(0, Math.Min(quantity - consumed, Math.Max(0, physical - consumed)) - free);
    }

    public static string WarningMessage(int excess, IReadOnlyList<ReservationHolder> holders) =>
        $"Ieșirea ia {excess} buc. rezervate de alte proiecte ({string.Join(", ", holders.Select(item => $"{item.ProjectName}: {item.Quantity} buc."))}).";

    public static string ReasonError(string? reason)
    {
        var text = (reason ?? string.Empty).Trim();
        return text.Length == 0 ? ReasonMessage : text.Length > ReasonMaximumLength ? $"Motivul poate avea cel mult {ReasonMaximumLength} de caractere." : string.Empty;
    }
}

public interface IReservationRepository
{
    // The reservations of a project (every product, every component).
    Task<IReadOnlyList<Reservation>> GetForProjectAsync(int projectId, CancellationToken cancellationToken = default);
    // The projects that hold pieces of a product, with the total reserved.
    Task<IReadOnlyList<ReservationHolder>> GetHoldersAsync(int productId, CancellationToken cancellationToken = default);
    // Reserves pieces from the free stock (adds to the reservation of the same project, product and component); more than the free stock is refused, or reduced to it when capToFree ("Rezerva cat se poate").
    Task<Reservation> ReserveAsync(int projectId, int? componentId, int productId, int quantity, CancellationToken cancellationToken = default, bool capToFree = false);
    // Lowers a reservation (all of it = released) with a short reason; each has its own journal action.
    Task ReduceAsync(int reservationId, int quantity, string reason, CancellationToken cancellationToken = default);
}
