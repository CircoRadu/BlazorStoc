namespace BlazorStoc.Services;

// What a project holds and what happened to it: the history of the movements tied to a project (exits to it and returns from it, in one list) and, for a product, the
// projects that hold it (where a return can be made). The quantity of a product at a project is always computed from the movements, never stored.
public sealed record ProjectMovementEntry(int Id, DateOnly Date, bool IsReturn, int ProductId, string ProductName, string Subcategory, int Quantity, string Operator, string Description, bool Voided);

// A project that holds a product: the quantity left there (exits minus returns), for the tab Retur of the product page.
public sealed record ProjectProductNet(int BeneficiaryId, string BeneficiaryName, int ProjectId, string ProjectName, int Net);

public interface IProjectEquipmentRepository
{
    // Exits to the project and returns from it (returns tied to an exit of the project and returns from the project), newest first; a voided exit is kept, marked.
    Task<IReadOnlyList<ProjectMovementEntry>> GetHistoryAsync(int projectId, CancellationToken cancellationToken = default);
    // The projects where the product still has something to return (net above zero), by beneficiary and project name.
    Task<IReadOnlyList<ProjectProductNet>> GetProductProjectsAsync(int productId, CancellationToken cancellationToken = default);
}

public static class ProjectEquipmentRules
{
    public static string KindLabel(bool isReturn) => isReturn ? "Retur" : "Ieșire";
}
