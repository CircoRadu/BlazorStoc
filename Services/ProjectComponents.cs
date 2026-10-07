namespace BlazorStoc.Services;

// The components of a project: the systems it is made of (types from the nomenclator "Tipuri de sisteme"), chosen when the project is created and
// added or taken out later. A component has a simple state; taking it out archives it with a reason (it is never deleted: the offer and lines that
// later tasks tie to it are kept) and it can be reactivated. Operations are recorded in the journal under the project.
public enum ComponentState { Offered = 1, InProgress = 2, Delivered = 3, Closed = 4 }

public sealed record ProjectComponent(int Id, int ProjectId, int SystemTypeId, string SystemTypeName, ComponentState State, bool Archived,
    string? ArchiveReason, string? ArchivedUtc, long Version);

public class ProjectComponentException(string message) : Exception(message);

// An exit to a project tied to a component: what is still at the beneficiary (exit minus returns) and has not been cleared yet.
public sealed record ComponentExit(int MovementId, int ProductId, string ProductName, DateOnly Date, string? Reference, int Quantity, int Remaining);

// How an exit left on a component that is taken out is cleared: back into the warehouse (a return tied to the exit), left at the beneficiary (handed over for good),
// moved to another component of the project, or consumed.
public enum ComponentExitAction { ReturnToWarehouse = 1, LeftAtBeneficiary = 2, MoveToComponent = 3, Consumed = 4 }
public sealed record ComponentExitResolution(int MovementId, ComponentExitAction Action, int? TargetComponentId = null);

// A component (of any project) that has a product in its offer: what an entry can be tied to.
public sealed record OfferComponent(int ComponentId, int ProjectId, string ProjectName, string Name);

// A component the product of an exit can be tied to; InOffer = the product is in the latest offer of the component.
public sealed record ComponentChoice(int ComponentId, int SystemTypeId, string Name, bool InOffer);

// Net handed-over quantity per component and product (ComponentId null = not tied yet; OutsideOffer = marked "in afara ofertei").
public sealed record ComponentNet(int? ComponentId, int? SystemTypeId, bool OutsideOffer, int ProductId, string ProductName, int Net);

// Taking a component out is refused while exits tied to it are not cleared; the exits are carried for the clearing dialog.
public sealed class ProjectComponentHasExitsException(IReadOnlyList<ComponentExit> exits) : ProjectComponentException(ProjectComponentRules.HasExitsMessage)
{
    public IReadOnlyList<ComponentExit> Exits { get; } = exits;
}

public static class ProjectComponentRules
{
    public const string StaleMessage = "Componenta a fost modificată între timp. Actualizează pagina și reia operația.";
    public const string UnknownTypeMessage = "Tipul de sistem ales nu există sau este dezactivat.";
    public const string ArchivedExistsMessage = "Componenta există arhivată în acest proiect: reactiveaz-o.";
    public const string AlreadyExistsMessage = "Proiectul are deja această componentă.";
    public const string ArchivedStateMessage = "O componentă arhivată nu își poate schimba starea; reactiveaz-o mai întâi.";
    public const string NotArchivedMessage = "Componenta nu este arhivată.";
    public const string HasExitsMessage = "Componenta are ieșiri legate de ea. Lămurește fiecare produs (retur, rămas la beneficiar, mutat pe altă componentă sau consumat) înainte de a o scoate.";
    public const string ExitNotPendingMessage = "Una dintre ieșiri nu mai are cantitate de lămurit pe această componentă. Actualizează pagina.";
    public const string TargetInvalidMessage = "Alege o altă componentă activă a aceluiași proiect.";
    public const string AlreadyArchivedMessage = "Componenta este deja arhivată.";

    public static string StateLabel(ComponentState state) => state switch
    {
        ComponentState.Offered => "Ofertată",
        ComponentState.InProgress => "În execuție",
        ComponentState.Delivered => "Predată",
        ComponentState.Closed => "Închisă",
        _ => state.ToString()
    };
}

public interface IProjectComponentRepository
{
    // The components of the project by the order of the nomenclator; archived ones only when asked.
    Task<IReadOnlyList<ProjectComponent>> GetForProjectAsync(int projectId, bool includeArchived = false, CancellationToken cancellationToken = default);
    // Adds active types to the project (state Offered); one journal event per component. A type already there is refused.
    Task<IReadOnlyList<ProjectComponent>> AddAsync(int projectId, IReadOnlyCollection<int> systemTypeIds, CancellationToken cancellationToken = default);
    Task<ProjectComponent> SetStateAsync(ProjectComponent component, ComponentState state, CancellationToken cancellationToken = default);
    Task<ProjectComponent> ArchiveAsync(ProjectComponent component, string reason, CancellationToken cancellationToken = default);
    // The active components of the project a product can be tied to (the ones with the product in their offer first).
    Task<IReadOnlyList<ComponentChoice>> GetChoicesAsync(int projectId, int productId, CancellationToken cancellationToken = default);
    // The exits tied to the component that still have a quantity at the beneficiary and are not cleared (what blocks taking the component out).
    Task<IReadOnlyList<ComponentExit>> GetPendingExitsAsync(int componentId, CancellationToken cancellationToken = default);
    // Clears exits of a component (one action per exit, one journal event each).
    Task ResolveExitsAsync(ProjectComponent component, IReadOnlyList<ComponentExitResolution> resolutions, CancellationToken cancellationToken = default);
    // Handed-over quantity (exits minus returns) per component and product for the project.
    Task<IReadOnlyList<ComponentNet>> GetNetByComponentAsync(int projectId, CancellationToken cancellationToken = default);
    // Pieces that entries tied to a component of the project brought, per system type and product.
    Task<IReadOnlyDictionary<(int SystemTypeId, int ProductId), int>> GetReceivedAsync(int projectId, CancellationToken cancellationToken = default);
    // The active components of every project that have the product in the latest revision of their offer.
    Task<IReadOnlyList<OfferComponent>> GetComponentsWithProductAsync(int productId, CancellationToken cancellationToken = default);
    Task<ProjectComponent> ReactivateAsync(ProjectComponent component, CancellationToken cancellationToken = default);
}
