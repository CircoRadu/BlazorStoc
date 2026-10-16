using System.Globalization;
using BlazorStoc.Services;
using BlazorStoc.Components.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Microsoft.Extensions.Logging;

namespace BlazorStoc.Components.Shared;

public partial class InvoiceTemplateWorkbench
{
    [Parameter] public int? EditTemplateId { get; set; }
    [Parameter] public string? EditTemplateName { get; set; }

    // Changes each time the user asks to edit a template (even the same one again), which is what loads its model.
    [Parameter] public int EditRequest { get; set; }

    [Parameter] public EventCallback Saved { get; set; }

    // The template that was just saved (created or replaced), for a page that goes on with it (the invoice pickup applies it to its file).
    [Parameter] public EventCallback<InvoiceTemplateRecord> SavedRecord { get; set; }

    // A file already read by the page that shows this editor (the invoice pickup in a window): the template is made on that file, without
    // uploading it again. The page owns the session, so it is not dropped when the editor is saved, closed or removed.
    [Parameter] public InvoiceAnalysisSession? ExistingSession { get; set; }

    private bool Embedded => ExistingSession is not null;

    // Raised when the editing of a saved template is closed without saving (the page then removes the "Editare șablon" sub-tab).
    [Parameter] public EventCallback Closed { get; set; }

    private readonly CancellationTokenSource lifetime = new();
    private DotNetObjectReference<InvoiceTemplateWorkbench>? reference;
    private InvoiceAnalysisSession? session;
    private InvoiceTemplateDraft? draft;
    private InvoiceExtraction? preview;
    private IReadOnlyList<InvoiceTemplateSuggestion> suggestions = [];
    private InvoiceTemplateRecord? existing;
    private List<string> problems = [];
    private string? error, notice;
    private string templateName = "", supplierName = "", supplierCui = "", drawMode = "";
    private string selectedId = "";
    private char selectedKind = ' ';
    private int currentPage = 1;
    private int drawCounter;
    private bool busy, saving, showAllFields;
    private char? decimalHint;
    private InvoicePageData CurrentPageData => session!.Document.Pages.FirstOrDefault(page => page.Number == currentPage) ?? session.Document.Pages[0];
    private string SourceText => session!.Analysis.Source == InvoiceSources.Ocr ? "prin recunoașterea imaginii (OCR)" : "din textul PDF-ului";
    private double TableLeft => draft!.Columns.Count == 0 ? 0 : draft.Columns.Min(column => column.Left);
    private double TableRight => draft!.Columns.Count == 0 ? 0 : draft.Columns.Max(column => column.Right);
    private const string HeaderBandId = "band";

    private IReadOnlyList<InvoiceColumn> PreviewColumns => (preview?.Columns ?? [])
        .Where(column => column.Meaning != InvoiceColumnMeanings.Ignore && column.Meaning != InvoiceColumnMeanings.Index).OrderBy(column => column.Left)
        .Concat(draft?.NameCodeSeparator.Length > 0 && !(preview?.Columns.Any(column => column.Meaning == InvoiceColumnMeanings.Code) ?? false)
            ? [new InvoiceColumn("code", "Cod", InvoiceColumnMeanings.Code, 0, 0)] : []).ToList();

    private string SumNote
    {
        get
        {
            if (preview is null) return "";
            var valueColumn = preview.Columns.FirstOrDefault(column => column.Meaning == InvoiceColumnMeanings.Value);
            var total = preview.Fields.FirstOrDefault(field => field.Meaning == InvoiceFieldMeanings.TotalNet);
            if (valueColumn is null || total is null) return "";
            var sum = preview.Rows.Sum(row => InvoiceValues.ParseNumber(row.Cells.GetValueOrDefault(valueColumn.Id, ""), decimalHint) ?? 0);
            var expected = InvoiceValues.ParseNumber(total.Value, decimalHint);
            if (expected is null) return "";
            return Math.Abs(sum - expected.Value) <= 0.05m
                ? $"Suma valorilor ({sum.ToString("0.00", CultureInfo.InvariantCulture)}) se potrivește cu totalul fără TVA."
                : $"⚠ Suma valorilor ({sum.ToString("0.00", CultureInfo.InvariantCulture)}) diferă de totalul fără TVA ({expected.Value.ToString("0.00", CultureInfo.InvariantCulture)}): verifică rândurile sau coloanele.";
        }
    }

    // Identifies this instance for invoice-template.js: the generation and the edit sub-tab are both on the page.
    private readonly string ownerId = Guid.NewGuid().ToString("N");

    private bool IsEditMode => EditTemplateId is not null;
    private UnsavedChangesTracker? tracker;
    private bool rebaseAfterRender;

    // The tax id read from the analysed invoice against the one the template is tied to.
    private string? CuiMismatch
    {
        get
        {
            var template = InvoiceValues.NormalizeCui(supplierCui);
            var found = InvoiceValues.NormalizeCui(session?.Analysis.SupplierCui);
            return template.Length > 0 && found.Length > 0 && template != found
                ? $"CUI/CIF-ul furnizorului din factură ({found}) diferă de cel al șablonului ({template}): verifică dacă este furnizorul potrivit."
                : null;
        }
    }

    private int loadedRequest;
    private byte[]? xmlSample;
    private string xmlSampleName = "";
    private int xmlSampleKey;

    // True once the file has been analysed. A new template starts from the analysis of its file; editing a saved template does NOT analyse the
    // file (the template is shown exactly as saved, on its model or on the file uploaded for it): the analysis runs only when the user asks
    // for it with the "Analizează fișierul" button (AnalyzeFileAsync).
    private bool analyzed;

    // ---- the supplier register: the template is tied to a supplier of the database, whose name it carries ----
    private IReadOnlyList<Supplier> registry = [];

    private bool registryLoaded, addingSupplier;
    private int addSupplierKey;

    // The supplier of the database for the tax id in the form; when it is not there (not read, or misread) the name read from the invoice is
    // tried, with the legal form ignored (SC / S.C. / SRL / S.R.L.).
    private Supplier? RegistrySupplier
    {
        get
        {
            var digits = SupplierRules.CuiDigits(supplierCui);
            var compact = SupplierRules.CompactKey(supplierCui);
            return registry.FirstOrDefault(item => item.IsExternal ? compact.Length > 0 && SupplierRules.CompactKey(item.Cui) == compact : digits.Length > 0 && SupplierRules.CuiDigits(item.Cui) == digits);
        }
    }

    // A new template takes the supplier of the database when the invoice names it (by CUI or, failing that, by name): its name and tax id.
    private Supplier? similarSupplier;

    // A new template is named "<supplier> - pdf <date>" as long as the user has not written another name (the supplier's name alone, as older versions proposed, counts as not written).
    private string lastSuggestedName = "";

    // Recomputes what the template reads from the file (the same code that will read later invoices) and the list of problems.
    // The field marked as the supplier's tax id feeds the CUI/CIF of the template to be saved; when that field is removed the CUI is emptied.
    private bool cuiFieldSeen;

    private const int HistoryLimit = 100;
    private readonly List<HistoryEntry> undoStack = [];
    private readonly List<HistoryEntry> redoStack = [];
    private string? lastSnapshot;
    private bool replaying;
    private static readonly System.Text.Json.JsonSerializerOptions SnapshotOptions = new();

    // The fields sharing the label of the field being edited: shown in red on the page while the popup is open.
    private HashSet<string> ConflictIds => popupOpen && PopupField() is { } edited && InvoiceTemplateDraft.EffectiveLabel(edited).Length > 0
        ? [.. draft!.SameLabelAs(edited).Select(item => item.Id)] : [];

    private MarkedTextField? descriptionField;
    private static readonly int[] OperationHues = [272, 24, 200, 330, 150, 52, 100, 240];

    private string OperationColor
    {
        get
        {
            var used = InvoiceProductDescription.ColumnLabels(draft!).Where(item => item.Column is not null).Select(item => ColumnHue(item.Column!)).ToList();
            static int Distance(int a, int b) { var d = Math.Abs(a - b) % 360; return d > 180 ? 360 - d : d; }
            var best = OperationHues[0];
            var bestDistance = -1;
            foreach (var hue in OperationHues)
            {
                var distance = used.Count == 0 ? 360 : used.Min(other => Distance(other, hue));
                if (distance >= 30) { best = hue; break; }
                if (distance > bestDistance) { bestDistance = distance; best = hue; }
            }
            return $"hsl({best} 70% 38%)";
        }
    }

    private string OperationChipStyle => $"color: {OperationColor}; border-color: {OperationColor}";
    private bool popupOpen;
    private readonly HashSet<string> multiIds = [];

    // A click closes the window only when the button was pressed AND released on the backdrop: pressing inside (selecting text in a field) and
    // releasing outside must leave it open.
    private bool downOnBackdrop;

    // Resize handles: a field has all eight, a column the four on its sides (left and right set its width, top and bottom where its data starts and ends).
    private static IEnumerable<(string Name, double X, double Y)> HandlesOf(EditBox box)
    {
        var right = box.X + box.W; var bottom = box.Y + box.H; var midX = box.X + box.W / 2; var midY = box.Y + box.H / 2;
        yield return ("n", midX, box.Y);
        yield return ("s", midX, bottom);
        // The width of a column is the width of its header cell: the zone of a column and the header band have only the top and bottom handles.
        if (box.Kind is not ('f' or 'h')) yield break;
        yield return ("w", box.X, midY);
        yield return ("e", right, midY);
        yield return ("nw", box.X, box.Y);
        yield return ("ne", right, box.Y);
        yield return ("sw", box.X, bottom);
        yield return ("se", right, bottom);
    }

    // ---- "Salvează ca șablon nou": the name of the new template must differ from the one being edited ----
    private bool showSaveAs;

    private string saveAsName = "", saveAsError = "";
}
