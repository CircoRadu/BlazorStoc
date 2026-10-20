using System.Globalization;
using BlazorStoc.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Microsoft.Extensions.Logging;

namespace BlazorStoc.Components.Pages;

public partial class InvoicePickup
{
    private static readonly string[] StepTitles = ["Citirea facturii", "Potrivirea produselor", "Confirmare"];
    private int step = 1;
    private bool showBlockers, showFinalizeBlockers;
    private bool NextBlocked => (step == 1 && !CanLeaveStep1) || (step == 2 && !CanLeaveStep2);
    private bool CanLeaveStep1 => session is not null && reading is not null && rows.Count - excluded.Count > 0;
    private bool CanLeaveStep2 => !step2Loading && step2Error is null && TakenRows.Any(item => item.ProductId is not null || item.Staged is not null) && HeaderConfirmed;

    // ---- step 3: summary of the choices, then the only place where anything is saved ----
    private DateOnly? newDate = StockMovementRules.Today, existingDate = StockMovementRules.Today;

    private bool finalizing, finished;
    private string? finalizeError, finalizeNotice;
    private List<PickRow> TakenRows => pick.Where(item => !IsOff(item)).ToList();
    private List<PickRow> NewLines => pick.Where(item => !IsOff(item) && item.Staged is not null).ToList();
    private List<PickRow> ExistingLines => pick.Where(item => !IsOff(item) && item.Staged is null && item.ProductId is not null).ToList();
    private bool CanFinalize => !finished && NewLines.Concat(ExistingLines).Any(line => !Skipped(line)) && supplierId is not null && invoiceNumber.Trim().Length > 0 && invoiceDate is not null;

    // ---- the invoice and its supplier: recorded with the first entry, every entry is tied to it ----
    private IReadOnlyList<Supplier> supplierList = [];

    private int? supplierId;
    private string invoiceNumber = "";
    private DateOnly? invoiceDate;
    private SupplierInvoice? createdInvoice, existingInvoice;
    private IReadOnlyList<InvoiceEntry> takenEntries = [];
    private IReadOnlyList<SupplierInvoice> similarInvoices = [];
    private bool NumberFromOcr => session?.Analysis.Source == InvoiceSources.Ocr;

    // The number, the supplier's CUI and the date are checked by the user in step 2, each on its own: they start as "neverificat" and any edit
    // of one of them puts it back there. Step 3 is reached only when all three are confirmed (and valid).
    private string supplierCuiText = "";

    private bool numberVerified, cuiVerified, dateVerified;
    private bool NumberValid => invoiceNumber.Trim().Length > 0;
    private bool DateValid => invoiceDate is not null;
    private Supplier? ResolvedSupplier => supplierList.FirstOrDefault(supplier => supplier.Id == supplierId);

    // An XML invoice is read exactly: its number, date and supplier need no confirmation by the user (they are confirmed by being read from the file).
    private bool VerifyNeeded => session?.IsXml != true;

    private bool HeaderConfirmed => (!VerifyNeeded || (numberVerified && cuiVerified && dateVerified)) && NumberValid && DateValid && ResolvedSupplier is not null;
    private string? supplierError;
    private string invoiceSupplierCui = "", invoiceSupplierName = "";
    private Guid? headerPrefilledFor;
    private bool addingSupplier, addFromInvoice;
    private int addSupplierKey;

    // Once the invoice is recorded the choice is fixed: a retry of the rows that failed goes to the same invoice.
    private bool InvoiceLocked => finalizing || finished || createdInvoice is not null;

    private bool supplierByName;
    private Supplier? similarSupplier;
    private SupplierRecognition? recognition;
    private SupplierRecognition? initialRecognition;   // what the recognition proposed when the invoice was read (kept when the user edits the CUI)
    private int? autoTemplateId;
    private bool templateChanged;
    private string aliasNotice = "";

    // The name read from the invoice, when the supplier was chosen another way (CUI or by hand) and the name is not yet one of its names: the
    // user can keep it as an alias, so the next invoice with that writing is recognised by name.
    private string? AliasOffer =>
        ResolvedSupplier is { } supplier && invoiceSupplierName.Trim() is { Length: > 0 } read && !supplierByName
        && SupplierRules.AliasProblem(read, supplier, supplierList) is null ? read : null;

    // Whether the CUI of the supplier chosen from the register is written in the invoice (the text read from the file, or by OCR).
    private bool? CuiInInvoice
    {
        get
        {
            if (ResolvedSupplier is not { } supplier || session is null) return null;
            if (session.IsXml) return SupplierRules.CuiDigits(session.SupplierCuiFromXml) is { Length: > 0 } fileDigits ? fileDigits == SupplierRules.CuiDigits(supplier.Cui) : null;
            BuildCuiSearchText();
            var key = supplier.IsExternal ? SupplierRules.CompactKey(supplier.Cui) : SupplierRules.CuiDigits(supplier.Cui);
            if (key.Length == 0) return null;
            return cuiSearchText.Length > 0 && cuiSearchText.Contains(key, StringComparison.Ordinal);
        }
    }

    private Guid? cuiSearchFor;
    private string cuiSearchText = "";

    // ---- pictures of the regions the values were read from (the user compares them with what was read) ----
    private readonly Dictionary<string, string?> regionPictures = [];

    private string? QuantityColumnId => reading?.Extraction.Columns.FirstOrDefault(column => column.Meaning == InvoiceColumnMeanings.Quantity)?.Id;

    // ---- step 2: the rows taken in step 1, editable, matched with the catalog ----
    private sealed class PickRow
    {
        public required Dictionary<string, string> Cells { get; init; }
        // The index of the row among the rows read in step 1: the choice "taken / not taken" (excluded) is shared by steps 1 and 2.
        public int RowIndex { get; set; }
        // The row of step 1 this one was made from (as read, before any correction): going back to step 1 and forward again keeps
        // the work done on the rows that are still taken (corrections, chosen or prepared products, descriptions).
        public string SourceKey { get; init; } = "";
        // Where the row was read on the page (points; Top = Bottom = 0 when unknown): the picture shown with a warning.
        public int Page { get; init; }
        public double Top { get; init; }
        public double Bottom { get; init; }
        public InvoiceProductMatch Match { get; set; } = new("", null, []);
        public int? ProductId { get; set; }
        public StagedProduct? Staged { get; set; }
        // Step 3: the description of the stock entry (from the template, editable) and what happened when it was saved.
        public string EntryDescription { get; set; } = "";
        // The component of a project whose offer this entry supplies (optional; proposed when the product is in a single component).
        public int? ComponentId { get; set; }
        public bool DescriptionEdited { get; set; }
        public int? CreatedProductId { get; set; }
        // The product was already taken from this invoice: it is skipped unless the user chooses to take it again.
        public bool RetakeAnyway { get; set; }
        // Negative stock of the product: the real quantity (or zero) the user stated, and whether the correction entry was already made.
        public NegativeStockResolution Neg { get; } = new();
        public bool Regularized { get; set; }
        // A free entry found for this row: tied to the invoice (stock unchanged) instead of adding a second entry.
        public bool LinkCandidate { get; set; } = true;
        public bool Done { get; set; }
        public string? Result { get; set; }
    }

    private List<PickRow> pick = [];
    private IReadOnlyList<FreeEntry> freeEntries = [];

    // Free entries that match a row (each proposed once); recomputed when a row, the supplier or the invoice date changes.
    private Dictionary<PickRow, FreeEntry> Candidates
    {
        get
        {
            var result = new Dictionary<PickRow, FreeEntry>();
            if (freeEntries.Count == 0) return result;
            var used = new HashSet<int>();
            foreach (var line in ExistingLines.Where(line => !line.Done && !Skipped(line)))
                if (line.ProductId is { } productId && QuantityOf(line).Quantity is { } quantity
                    && FreeEntryMatching.Find(freeEntries, productId, quantity, invoiceDate ?? StockMovementRules.Today, supplierId, used) is { } found)
                { result[line] = found; used.Add(found.MovementId); }
            return result;
        }
    }

    private IReadOnlyList<Product> catalog = [];
    private IReadOnlyList<ProductGroup> groups = [];
    private bool step2Loading;
    private string? step2Error;
    private PickRow? newProductFor;
    private int newProductKey;

    // The invoice itself, in a window of its own (opened by the link of step 2): the pages of the file under pickup.
    private string ViewerUrl => $"/produse/preluare-factura/factura/{session!.Id}";

    // The decimal separator the invoice uses for its own numbers, found once per file.
    private char? numberHint;

    private Guid? numberHintFor;

    private char? NumberHint
    {
        get
        {
            if (session!.IsXml) return '.';   // the numbers of an XML invoice are written with a point
            if (numberHintFor != session!.Id) { numberHint = InvoiceValues.DecimalStyle(session.Document.AllWords.Select(word => word.Text)); numberHintFor = session.Id; }
            return numberHint;
        }
    }

    private bool loading = true, busy, addMode;
    private string? loadError, error, notice, templateInfo;
    private string selectedId = "";
    private int currentPage = 1, separatorCounter;
    private InvoiceAnalysisSession? session;
    private InvoiceTemplateRecord? chosen;
    private IReadOnlyList<InvoiceTemplateRecord> allTemplates = [];
    private bool creatingTemplate;
    private IReadOnlyList<InvoiceTemplateSuggestion> suggestions = [];
    private bool isEFacturaPdf;
    private double? ChosenScore => chosen is null ? null : suggestions.FirstOrDefault(item => item.Template.Info.Id == chosen.Info.Id)?.Match.Score;
    private InvoicePickupReading? reading;
    private List<InvoiceSeparator> separators = [];
    private IReadOnlyList<InvoiceTableRow> rows = [];
    private InvoiceTemplateDraft? overlay;
    private bool showFields = true, showHeader = true, showColumns = true;
    private readonly HashSet<int> excluded = [];
    private DotNetObjectReference<InvoicePickup>? reference;
    private readonly CancellationTokenSource lifetime = new();
    private InvoicePageData CurrentPageData => session!.Document.Pages.FirstOrDefault(page => page.Number == currentPage) ?? session.Document.Pages[0];

    // The columns the template reads with a meaning, plus the product code when it was split off the name.
    private IEnumerable<ShownColumn> ShownColumns
    {
        get
        {
            var columns = reading!.Extraction.Columns.Where(column => ShowUnnamedColumns || column.Meaning != InvoiceColumnMeanings.Ignore).ToList();
            foreach (var column in columns) yield return new ShownColumn(column.Id, column.Label.Length > 0 ? column.Label : InvoiceVocabulary.ColumnTitle(column.Meaning), column.Meaning);
            if (columns.All(column => column.Id != "code") && rows.Any(row => row.Cells.ContainsKey("code"))) yield return new ShownColumn("code", InvoiceVocabulary.ColumnTitle(InvoiceColumnMeanings.Code), InvoiceColumnMeanings.Code);
        }
    }

    // The supplier named by the file itself (name and tax id), for the warning that no template exists for it.
    private string SupplierText => session is null ? "" : string.Join(" · ", new[] { session.Analysis.SupplierName, session.Analysis.SupplierCui.Length > 0 ? "CUI " + session.Analysis.SupplierCui : "" }.Where(part => part.Length > 0));

    // ---- step 1: which rows stand for products already taken from this invoice (the same warning as in steps 2 and 3, so the rows are chosen knowing it) ----
    private Dictionary<int, (string Product, int Quantity)> takenPreview = [];

    private bool takenChecking;
    private int takenPreviewVersion;
    private int? takenPreviewSupplier;
    private readonly SemaphoreSlim takenPreviewGate = new(1, 1);

    // Rows already switched off because their product was taken before: done once per row, so a row the user switched on again stays on.
    private readonly HashSet<string> takenAutoDone = [];

    // A reading made by the engine itself (no saved template) shows every column it found, also those whose label it did not recognise (the values of those
    // columns are read too; they are only without a meaning until the label is recognised or named). A saved template keeps hiding the columns it does not use.
    private bool ShowUnnamedColumns => chosen is null;
}
