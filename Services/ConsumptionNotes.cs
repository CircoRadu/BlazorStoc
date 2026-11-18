namespace BlazorStoc.Services;

// Consumption notes (bonuri de consum): a document of ONE project of ONE beneficiary, generated on request (never automatically), with the equipment handed over to the
// project at that moment. A note is not edited: it is generated, viewed / downloaded / printed, or deleted. The older notes of the project stay inactive (traceability) or are
// deleted when the new one is generated; every note, active or not, can be viewed, downloaded, printed or deleted.

// One row of the list of notes (the table of a beneficiary and the Administrare page).
public sealed record ConsumptionNote(int Id, int BeneficiaryId, string BeneficiaryName, string BeneficiaryCui, int ProjectId, string ProjectName,
    DateOnly Date, bool Active, string Operator, DateTime CreatedUtc, int LineCount);

// One line of a note: one exit movement of the project, net of the returns tied to it.
public sealed record ConsumptionNoteLine(int Position, DateOnly MovementDate, int? ProductId, string ProductName, string Subcategory, int Quantity);

public sealed record ConsumptionNoteDetails(ConsumptionNote Note, IReadOnlyList<ConsumptionNoteLine> Lines);

// What the generation did: the new note and what became of the notes the project already had.
public sealed record ConsumptionNoteGenerated(ConsumptionNote Note, int Deactivated, int Deleted);

public sealed class ConsumptionNoteException(string message) : Exception(message);

public interface IConsumptionNoteRepository
{
    // All notes, newest first; a beneficiary and/or a project narrow the list.
    Task<IReadOnlyList<ConsumptionNote>> GetAllAsync(int? beneficiaryId = null, int? projectId = null, CancellationToken cancellationToken = default);
    Task<ConsumptionNoteDetails?> GetAsync(int id, CancellationToken cancellationToken = default);
    // Generates the note of a project with the equipment at this moment. The notes the project had before are deleted (deletePrevious) or become inactive.
    Task<ConsumptionNoteGenerated> GenerateAsync(int projectId, bool deletePrevious, CancellationToken cancellationToken = default);
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
    // The projects of a beneficiary that have equipment handed over (net of returns above zero): only they can get a note, so only they show the button that generates it.
    Task<IReadOnlySet<int>> GetProjectsWithEquipmentAsync(int beneficiaryId, CancellationToken cancellationToken = default);
}

public static class ConsumptionNoteRules
{
    public const string NoEquipmentMessage = "Proiectul nu are echipamente predate: nu se poate genera bonul de consum.";
    public const string ProjectMissingMessage = "Proiectul ales nu mai există. Actualizează pagina.";
    public const string NoteMissingMessage = "Bonul de consum nu mai există. Actualizează pagina.";
    public static string Title(int id) => $"Bon de consum nr. {id}";

    // The rows the list shows for a text typed in the search field: number, date (dd.MM.yyyy), beneficiary, CUI, project, state and operator.
    public static bool Matches(ConsumptionNote note, string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;
        var haystack = string.Join(' ', note.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), StockMovementRules.DisplayDate(note.Date), note.BeneficiaryName,
            note.BeneficiaryCui, note.ProjectName, note.Active ? "activ" : "inactiv", note.Operator);
        // Case- and diacritic-insensitive, every typed word must be found.
        var key = TextNormalization.UniquenessKey(haystack);
        return text.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(word => key.Contains(TextNormalization.UniquenessKey(word), StringComparison.Ordinal));
    }
}
