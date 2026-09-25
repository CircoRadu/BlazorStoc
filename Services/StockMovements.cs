using System.Globalization;
using System.Text.Json.Serialization;

namespace BlazorStoc.Services;

// Values match the legacy io.io_tip_actiune column: 1 = entry, 0 = exit.
public enum StockMovementKind { Exit = 0, Entry = 1 }

public sealed record StockMovement(int Id, int ProductId, StockMovementKind Kind, int Quantity, DateOnly Date,
    string Description, int? BeneficiaryId, string? BeneficiaryName, int? ProjectId, string? ProjectName,
    string Operator, long Version, DateTime CreatedUtc, DateTime UpdatedUtc, bool Modified = false)
{
    // Signed effect on the product stock: entries add, exits subtract.
    [JsonIgnore] public int Effect => StockMovementRules.Effect(Kind, Quantity);
}

public sealed class StockMovementInput
{
    public StockMovementKind Kind { get; set; } = StockMovementKind.Entry;
    public DateOnly? Date { get; set; }
    public int? Quantity { get; set; }
    public string Description { get; set; } = "";
    public int? BeneficiaryId { get; set; }
    public int? ProjectId { get; set; }
    public string Reason { get; set; } = "";

    public static StockMovementInput From(StockMovement movement) => new()
    {
        Kind = movement.Kind, Date = movement.Date, Quantity = movement.Quantity, Description = movement.Description,
        BeneficiaryId = movement.BeneficiaryId, ProjectId = movement.ProjectId
    };
}

public sealed record StockMovementQuery(StockMovementKind? Kind = null, bool Descending = false, int Page = 1, int PageSize = 10);
public sealed record StockMovementPage(IReadOnlyList<StockMovement> Items, int TotalCount, int Stock, bool AnyModified);
public sealed record StockMovementResult(StockMovement Movement, int Stock);
public sealed record StockMovementHistoryEntry(int Id, int MovementId, string Actor, DateTime TimestampUtc, string Changes,
    int StockCorrection, string Reason);
// Contract consumed by the project "Echipamente" page (TODO Task 2 / Subtask 2.4).
public sealed record ProjectStockMovement(int Id, int ProductId, string ProductCode, int Quantity, DateOnly Date, string Operator);

public sealed class StockMovementOperationException(string message) : Exception(message);

public interface IStockMovementRepository
{
    Task<StockMovementPage> GetPageAsync(int productId, StockMovementQuery query, CancellationToken cancellationToken = default);
    Task<StockMovement?> GetAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StockMovementHistoryEntry>> GetHistoryAsync(int movementId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProjectStockMovement>> GetForProjectAsync(int projectId, CancellationToken cancellationToken = default);
    Task<StockMovementResult> CreateAsync(int productId, StockMovementInput input, CancellationToken cancellationToken = default);
    Task<StockMovementResult> UpdateAsync(StockMovement original, StockMovementInput input, CancellationToken cancellationToken = default);
    Task<int> DeleteAsync(StockMovement original, string reason, CancellationToken cancellationToken = default);
}

public static class StockMovementRules
{
    public const int MaxQuantity = 100_000;
    public const int MaxDescriptionLength = 500;
    public const string DescriptionRequiredMessage = "Completează câmpul «Descriere».";
    public const string StaleMessage = "Mișcarea a fost modificată sau ștearsă între timp. Actualizează pagina și reia operația.";
    public static readonly DateOnly EarliestDate = new(1990, 1, 1);
    public static readonly DateOnly LatestDate = new(2100, 12, 31);

    public static int Effect(StockMovementKind kind, int quantity) => kind == StockMovementKind.Entry ? quantity : -quantity;
    public static string KindLabel(StockMovementKind kind) => kind == StockMovementKind.Entry ? "Intrare" : "Ieșire";
    public static string DisplayDate(DateOnly date) => date.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
    public static string StorageDate(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public static DateOnly ParseStorageDate(string value) => DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
    public static DateOnly ParseLegacyDate(string value) => DateOnly.ParseExact(value.Trim(), "dd-MM-yyyy", CultureInfo.InvariantCulture);

    public static StockMovementInput Validated(StockMovementInput input, StockMovementKind kind, bool isEdit)
    {
        var errors = new List<string>();
        var description = TextNormalization.ForStorage(input.Description ?? string.Empty);
        if (input.Date is not { } date || date < EarliestDate || date > LatestDate) errors.Add("Alege o dată validă pentru mișcare.");
        if (input.Quantity is not { } quantity || quantity < 1) errors.Add("Introdu o cantitate întreagă mai mare decât zero.");
        else if (quantity > MaxQuantity) errors.Add($"Cantitatea poate fi cel mult {MaxQuantity:N0}.");
        if (description.Length == 0) errors.Add(DescriptionRequiredMessage);
        else if (description.Length > MaxDescriptionLength) errors.Add($"Descrierea poate avea cel mult {MaxDescriptionLength} de caractere.");
        var beneficiaryId = input.BeneficiaryId;
        var projectId = input.ProjectId;
        if (kind == StockMovementKind.Entry)
        {
            if (beneficiaryId is not null || projectId is not null)
                errors.Add("Beneficiarul și proiectul se pot alege numai la ieșire.");
        }
        else
        {
            if (beneficiaryId is <= 0) errors.Add("Alege un beneficiar din listă sau debifează opțiunea beneficiar.");
            if (projectId is not null && beneficiaryId is null) errors.Add("Alege beneficiarul înainte de proiect.");
            if (projectId is <= 0) errors.Add("Alege un proiect din listă sau debifează opțiunea proiect.");
        }
        var reason = ChangeReasonRules.Normalize(input.Reason);
        if (isEdit && ChangeReasonRules.ValidationError(reason) is { } reasonError) errors.Add(reasonError);
        if (errors.Count > 0) throw new StockMovementOperationException(string.Join(" ", errors));
        return new StockMovementInput
        {
            Kind = kind, Date = input.Date, Quantity = input.Quantity, Description = description,
            BeneficiaryId = beneficiaryId, ProjectId = projectId, Reason = reason
        };
    }

    public static void CheckCurrent(StockMovement? current, StockMovement original)
    {
        if (current is null || current.Version != original.Version) throw new StockMovementOperationException(StaleMessage);
    }

    public static string Target(string productCode) => productCode;

    public static string AuditIdentification(StockMovement movement, string productCode)
    {
        var values = new List<(string Field, string Value)>
        {
            (ProductCode.Label, productCode), ("Tip", KindLabel(movement.Kind)), ("Cantitate", movement.Quantity.ToString(CultureInfo.InvariantCulture)),
            ("Data", DisplayDate(movement.Date)), ("Descriere", movement.Description)
        };
        if (movement.BeneficiaryName is not null) values.Add(("Beneficiar", movement.BeneficiaryName));
        if (movement.ProjectName is not null) values.Add(("Proiect", movement.ProjectName));
        return AuditDetails.Identification(values.ToArray());
    }

    public static AuditChange[] AuditChanges(StockMovement before, StockMovement after) =>
    [
        new("Data", DisplayDate(before.Date), DisplayDate(after.Date)),
        new("Cantitate", before.Quantity.ToString(CultureInfo.InvariantCulture), after.Quantity.ToString(CultureInfo.InvariantCulture)),
        new("Descriere", before.Description, after.Description),
        new("Beneficiar", before.BeneficiaryName ?? "—", after.BeneficiaryName ?? "—"),
        new("Proiect", before.ProjectName ?? "—", after.ProjectName ?? "—")
    ];

    public static bool HasChanges(StockMovement before, StockMovement after) =>
        AuditChanges(before, after).Any(change => !string.Equals(change.Before, change.After, StringComparison.Ordinal));

    // Text kept in the history: changed values, followed by the stock correction.
    public static string HistorySummary(StockMovement before, StockMovement after, int correction)
    {
        var changes = AuditDetails.Changes(AuditChanges(before, after));
        var sign = correction > 0 ? "+" : string.Empty;
        return $"{changes}; Corecție stoc: {sign}{correction}";
    }
}

public static class StockMovementNavigation
{
    public static string ProductUrl(int productId) => $"/produse/{productId}/miscari";
    public static string MovementUrl(int movementId) => $"/miscari/{movementId}";
}
