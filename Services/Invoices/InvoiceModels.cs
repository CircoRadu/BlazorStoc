namespace BlazorStoc.Services;

// The invoice template engine works on words with coordinates, whatever their source (text layer of the PDF or OCR).
// Coordinates are in PDF points (1/72 inch), origin at the top-left corner of the UPRIGHT page: the page rotation (/Rotate)
// and, for scans, the skew and orientation are already normalised when a word is created, so no later step deals with them.
public static class InvoiceSources
{
    public const string Text = "text";
    public const string Ocr = "ocr";
}

public sealed record InvoiceWord(int Page, string Text, double X, double Y, double Width, double Height, int Order, bool Bold = false, double Confidence = 1.0)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public double CenterX => X + Width / 2;
    public double CenterY => Y + Height / 2;
}

// A ruled line drawn on the page (a table border), in page points: a vertical rule is at x = Position and spans y From..To, a horizontal
// one is at y = Position and spans x From..To. Found in scans (OpenCV); a hint for the table's columns, never required.
public sealed record InvoiceRule(bool Vertical, double Position, double From, double To);

// OcrSkew (degrees) and OcrTurn (0, 90, 180, 270 clockwise): how the scan was straightened and turned to get the frame its words are in, so that the same picture
// can be made again (the cells of the table are read again from it).
public sealed record InvoicePageData(int Number, double Width, double Height, string Source, IReadOnlyList<InvoiceWord> Words, IReadOnlyList<InvoiceRule>? Rules = null,
    double OcrSkew = 0, int OcrTurn = 0);

public sealed record InvoiceDocument(IReadOnlyList<InvoicePageData> Pages)
{
    public IEnumerable<InvoiceWord> AllWords => Pages.SelectMany(page => page.Words);
}

// A rectangle on a page, in points.
public sealed record InvoiceBox(int Page, double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
}

public enum InvoiceValueKind { Text, Number, Date }

// One label/value pair found in the part of the invoice that is not the line table (supplier, buyer, number, date, totals ...).
// Meaning is a key of InvoiceVocabulary.FieldMeanings suggested from the label and the section ("" when nothing is known);
// Confidence: 1 = the label is in the dictionary, 0.6 = it ends with a colon, 0.3 = only its position says it is a label.
public sealed record InvoiceHeaderField(string Id, string Label, string Value, InvoiceBox LabelBox, InvoiceBox ValueBox,
    string Meaning, string Section, double Confidence, InvoiceValueKind ValueKind);

// One column of the line table. Left/Right are the column edges in points (the same on every page of the table). Meaning is a key
// of InvoiceVocabulary.ColumnMeanings. RowMapping: "band" = a value belongs to the row whose band it is in; "sequence" = the k-th
// value of the column belongs to the k-th row (for columns whose text is vertically offset from its row).
// ZoneTop / ZoneBottom (points, 0 = none): where the data of the column starts and ends, found in the file from the elements the template
// anchors them to (the text above and below the zone); ZoneBottomPage is the page the bottom anchor was found on (0 = the header page).
public sealed record InvoiceColumn(string Id, string Label, string Meaning, double Left, double Right, string RowMapping = InvoiceRowMapping.Band,
    double ZoneTop = 0, double ZoneBottom = 0, int ZoneBottomPage = 0);

public static class InvoiceRowMapping
{
    public const string Band = "band";
    public const string Sequence = "sequence";
}

// Top/Bottom: the vertical extent of the row on its page, in points (for drawing it over the page picture).
public sealed record InvoiceTableRow(int Page, int? Number, IReadOnlyDictionary<string, string> Cells, IReadOnlyList<string> Flags, double Top = 0, double Bottom = 0);

// The line table: where its header is (first page), its columns and the rows read with them. HasIndexColumn says that the rows
// were told apart by a running number ("Nr. crt.", "Linia"); otherwise by their values.
// ByStructure: the header was not recognised by its labels (the table was found from its running numbers), so the meanings of the
// columns are guesses or unknown and the user is asked to check them.
public sealed record InvoiceTable(int HeaderPage, double HeaderTop, double HeaderBottom, IReadOnlyList<InvoiceColumn> Columns,
    IReadOnlyList<InvoiceTableRow> Rows, bool HasIndexColumn, double Confidence, bool ByStructure = false, string RowSplit = InvoiceRowSplit.Top);

public sealed record InvoiceAnalysis(IReadOnlyList<InvoicePageData> Pages, IReadOnlyList<InvoiceHeaderField> Fields, InvoiceTable? Table,
    IReadOnlyList<string> Warnings)
{
    public string SupplierName => Fields.FirstOrDefault(field => field.Meaning == InvoiceFieldMeanings.SupplierName)?.Value ?? "";
    public string SupplierCui => InvoiceValues.NormalizeCui(Fields.FirstOrDefault(field => field.Meaning == InvoiceFieldMeanings.SupplierCui)?.Value);
    public string Source => Pages.Count == 0 ? InvoiceSources.Text : Pages.Any(page => page.Source == InvoiceSources.Ocr) ? InvoiceSources.Ocr : InvoiceSources.Text;
}

public static class InvoiceFieldMeanings
{
    public const string SupplierName = "supplier.name";
    public const string SupplierCui = "supplier.cui";
    public const string SupplierRegistry = "supplier.registry";
    public const string InvoiceNumber = "invoice.number";
    public const string InvoiceDate = "invoice.date";
    public const string DueDate = "invoice.dueDate";
    public const string Currency = "invoice.currency";
    public const string TotalNet = "invoice.totalNet";
    public const string TotalVat = "invoice.totalVat";
    public const string Total = "invoice.total";
    public const string OrderNumber = "invoice.orderNumber";
    public const string Custom = "custom";
}

public static class InvoiceColumnMeanings
{
    public const string Index = "index";
    public const string Code = "code";
    public const string Name = "name";
    public const string Unit = "unit";
    public const string Quantity = "quantity";
    public const string UnitPrice = "unitPrice";
    public const string VatRate = "vatRate";
    public const string VatAmount = "vatAmount";
    public const string Value = "value";
    public const string ValueWithVat = "valueWithVat";
    public const string Discount = "discount";
    public const string Currency = "currency";
    // A column with a name of its own that no other meaning describes ("Taxa verde"): used, it is offered as a label under that name.
    public const string Other = "other";
    public const string Ignore = "ignore";
}
