using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlazorStoc.Services;

// ---- the saved template (stored as JSON; every position is a FRACTION of the page, so it does not depend on the resolution or on the
// scan scale) ----

public sealed record InvoiceTemplateAnchor(string Text, int Page, double X, double Y, string Group);

public static class InvoiceAnchorGroups
{
    public const string Page = "page";
    public const string Table = "table";
}

// Where a field's value is read: "right" = the words to the right of its label on the same line, "below" = the run under the label,
// "region" = a rectangle kept at the same distance from the label (or a fixed rectangle when the field has no label, a hand-drawn one).
// X/Y/Width/Height is the value rectangle where it was made; LabelX/LabelY is the top-left of the label (fractions of the page).
public static class InvoiceFieldModes
{
    public const string Right = "right";
    public const string Below = "below";
    public const string Region = "region";
}

public sealed record InvoiceTemplateField(string Id, string Meaning, string Name, bool Use, int Page, double X, double Y, double Width, double Height,
    string LabelText, string Kind, bool Manual, string Mode = InvoiceFieldModes.Region, double LabelX = 0, double LabelY = 0);

// Top/Bottom: the data zone of the column (fractions of the page height, 0 = automatic: from the header to the end of the rows), with the text of
// the element right above (TopAnchor) and right below (BottomAnchor) the zone: in another file the zone is found from those elements, never from
// fixed coordinates. BottomPage: the page of the bottom anchor (0 = the header page).
public sealed record InvoiceTemplateColumn(string Id, string Label, string Meaning, bool Use, double Left, double Right, string RowMapping, bool Manual, string HeaderText = "",
    double Top = 0, double Bottom = 0, string TopAnchor = "", string BottomAnchor = "", int BottomPage = 0,
    double CellTop = 0, double CellBottom = 0, bool ZoneDrawn = true);

// BodyBottom: where the rows of the table ended on the header page in the file the template was made from (fraction of the page; 0 = not known,
// templates saved before it existed): the columns are drawn down to it when the template is shown without having been read.
public sealed record InvoiceTemplateTable(int HeaderPage, double HeaderTop, double HeaderBottom, string RowSplit, bool HasIndexColumn,
    string NameCodeSeparator, IReadOnlyList<InvoiceTemplateColumn> Columns, double BodyBottom = 0);

public sealed record InvoiceTemplateDefinition(int Schema, string SourceKind, double PageWidth, double PageHeight,
    IReadOnlyList<InvoiceTemplateAnchor> Anchors, IReadOnlyList<InvoiceTemplateField> Fields, InvoiceTemplateTable? Table, string ProductDescription = "")
{
    public const int CurrentSchema = 1;
    public int UsedFieldCount => Fields.Count(field => field.Use);
    public int UsedColumnCount => Table?.Columns.Count(column => column.Use && column.Meaning != InvoiceColumnMeanings.Ignore) ?? 0;
}

public static class InvoiceTemplateJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.Never };

    public static string Serialize(InvoiceTemplateDefinition definition) => JsonSerializer.Serialize(definition, Options);

    public static InvoiceTemplateDefinition Deserialize(string json)
    {
        var definition = JsonSerializer.Deserialize<InvoiceTemplateDefinition>(json, Options)
            ?? throw new InvalidOperationException("Șablonul de factură salvat nu poate fi citit.");
        if (definition.Schema != InvoiceTemplateDefinition.CurrentSchema)
            throw new InvalidOperationException("Șablonul de factură a fost salvat cu o versiune necunoscută a formatului.");
        return definition;
    }
}

// ---- the draft the user edits in the interactive template: positions in POINTS of the analysed file ----

public sealed class DraftField
{
    public string Id { get; set; } = "";
    public string Meaning { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Use { get; set; }
    public int Page { get; set; } = 1;
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public string LabelText { get; set; } = "";
    public string Value { get; set; } = "";
    public string Kind { get; set; } = "text";
    public double Confidence { get; set; }
    public string Section { get; set; } = "";
    public bool Manual { get; set; }
    public string Mode { get; set; } = InvoiceFieldModes.Region;
    public double LabelX { get; set; }
    public double LabelY { get; set; }
}

public sealed class DraftColumn
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    // The text of the table header this column was made from: what ties it to the header of a file (Label is only the name shown).
    public string HeaderText { get; set; } = "";
    public string Meaning { get; set; } = InvoiceColumnMeanings.Ignore;
    public bool Use { get; set; }
    public double Left { get; set; }
    public double Right { get; set; }
    public string RowMapping { get; set; } = InvoiceRowMapping.Band;
    public bool Manual { get; set; }
    // The data zone of the column (points on the header page; 0 = automatic) and the text of the element right above / below it (what it is anchored to).
    public double Top { get; set; }
    public double Bottom { get; set; }
    public string TopAnchor { get; set; } = "";
    public string BottomAnchor { get; set; } = "";
    public int BottomPage { get; set; }
    // The cell of the table header the column is made from: its width is the width of the column (CellTop/CellBottom = its extent, 0 = the header band of
    // the table). The zone of the column (Top/Bottom) must be drawn too: a column whose zone was not drawn is a problem that stops the save.
    public double CellTop { get; set; }
    public double CellBottom { get; set; }
    public bool ZoneDrawn { get; set; } = true;
}

public sealed class InvoiceTemplateDraft
{
    public string SourceKind { get; set; } = InvoiceSources.Text;
    public List<DraftField> Fields { get; set; } = [];
    public bool HasTable { get; set; }
    public int HeaderPage { get; set; } = 1;
    public double HeaderTop { get; set; }
    public double HeaderBottom { get; set; }
    public string RowSplit { get; set; } = InvoiceRowSplit.Top;
    public bool HasIndexColumn { get; set; }
    public string NameCodeSeparator { get; set; } = "";
    // The foot of the table body on the header page, in points (0 = not known): kept with the template, set when it is saved.
    public double BodyBottom { get; set; }
    public List<DraftColumn> Columns { get; set; } = [];
    // The words that identify the layout (kept from the analysed file; they are what a later invoice is matched with).
    public List<InvoiceTemplateAnchor> Anchors { get; set; } = [];
    public string SupplierName { get; set; } = "";
    public string SupplierCui { get; set; } = "";
    // How a product taken from an invoice is described: text with <label> marks (see InvoiceProductDescription).
    public string ProductDescription { get; set; } = "";

    // With ruled lines a header cell is the rectangle the rules frame: the header spans the inside of that rectangle (between the rule right above
    // and the rule right below its words, kept RuleInset away from them), never the lines themselves. Without such rules the extent of its words.
    public static (double Top, double Bottom) HeaderInterior(InvoicePageData? page, double top, double bottom, double left, double right)
    {
        if (page?.Rules is not { Count: > 0 } rules) return (top, bottom);
        var horizontals = rules.Where(rule => !rule.Vertical && InvoiceLayout.Overlap(left, right, rule.From, rule.To) >= 0.6 * (right - left)).Select(rule => rule.Position).OrderBy(position => position).ToList();
        var above = horizontals.Where(position => position <= top + 1 && top - position <= 14).DefaultIfEmpty(double.NaN).Max();
        var below = horizontals.Where(position => position >= bottom - 1 && position - bottom <= 14).DefaultIfEmpty(double.NaN).Min();
        if (double.IsNaN(above) || double.IsNaN(below)) return (top, bottom);
        return (above + InvoiceTableReader.RuleInset, below - InvoiceTableReader.RuleInset);
    }

    // Initial draft = what the analysis proposes: recognised fields and columns are used, the rest is offered but unused.
    public static InvoiceTemplateDraft FromAnalysis(InvoiceAnalysis analysis)
    {
        var draft = new InvoiceTemplateDraft { SourceKind = analysis.Source, SupplierName = analysis.SupplierName, SupplierCui = analysis.SupplierCui };
        foreach (var field in analysis.Fields)
            draft.Fields.Add(new DraftField
            {
                Id = field.Id, Meaning = field.Meaning, Name = OwnName(field),
                Use = field.Meaning.Length > 0 && field.Meaning != InvoiceFieldMeanings.Custom && !InvoiceVocabulary.IsExtraPartyAttribute(field.Meaning),
                Page = field.ValueBox.Page, X = field.ValueBox.X, Y = field.ValueBox.Y, Width = field.ValueBox.Width, Height = field.ValueBox.Height,
                LabelText = field.Label, Value = field.Value, Kind = KindName(field.ValueKind), Confidence = field.Confidence, Section = field.Section,
                LabelX = field.LabelBox.X, LabelY = field.LabelBox.Y,
                Mode = field.ValueBox.Y >= field.LabelBox.Bottom - 0.3 * field.LabelBox.Height && field.ValueBox.X < field.LabelBox.Right + 2
                    ? InvoiceFieldModes.Below : InvoiceFieldModes.Right
            });
        if (analysis.Table is { } table)
        {
            draft.HasTable = true;
            draft.HeaderPage = table.HeaderPage;
            (draft.HeaderTop, draft.HeaderBottom) = HeaderInterior(analysis.Pages.FirstOrDefault(item => item.Number == table.HeaderPage), table.HeaderTop, table.HeaderBottom, table.Columns.Min(column => column.Left), table.Columns.Max(column => column.Right));
            draft.RowSplit = table.RowSplit;
            draft.HasIndexColumn = table.HasIndexColumn;
            draft.Columns = table.Columns.Select(column => new DraftColumn
            {
                Id = column.Id, Label = column.Label, HeaderText = column.Label, Meaning = column.Meaning == InvoiceColumnMeanings.Ignore ? InvoiceColumnMeanings.Other : column.Meaning, Use = column.Meaning != InvoiceColumnMeanings.Ignore,
                Left = column.Left, Right = column.Right, RowMapping = column.RowMapping
            }).ToList();
        }
        // Two used fields with the same label in the file ("Data" twice) get different labels.
        foreach (var field in draft.Fields.Where(item => item.Use))
            if (draft.Fields.TakeWhile(item => item != field).Any(other => other.Use && InvoiceValues.Normalize(EffectiveLabel(other)) == InvoiceValues.Normalize(EffectiveLabel(field)))) field.Name = draft.FreeLabelFor(field);
        draft.Anchors = InvoiceTemplateAnchors.From(analysis);
        draft.ProductDescription = InvoiceProductDescription.Default(draft);
        return draft;
    }

    // The label of a field found by the analysis: the text the file itself has beside it (what the template is made of), and only when the file has
    // none (a supplier or a customer found by its place on the page) the title of its meaning.
    private static string OwnName(InvoiceHeaderField field)
    {
        var own = field.Label.Trim().TrimEnd(':', ' ');
        return own.Length > 0 ? own : field.Meaning.Length > 0 ? InvoiceVocabulary.FieldTitle(field.Meaning) : "";
    }

    public static string KindName(InvoiceValueKind kind) => kind switch { InvoiceValueKind.Number => "number", InvoiceValueKind.Date => "date", _ => "text" };

    // Problems that stop the draft from being saved as a template (empty = acceptable).
    // The label a field is known by: its own name, else the title of its meaning.
    public static string EffectiveLabel(DraftField field) =>
        field.Name.Trim().Length > 0 ? field.Name.Trim() : field.Meaning.Length > 0 && field.Meaning != InvoiceFieldMeanings.Custom ? InvoiceVocabulary.FieldTitle(field.Meaning) : "";

    // The other fields that already carry the same label or the same meaning as this one (what the user is warned about).
    public IEnumerable<DraftField> SameLabelAs(DraftField field)
    {
        var key = InvoiceValues.Normalize(EffectiveLabel(field));
        var meaning = field.Meaning.Length > 0 && field.Meaning != InvoiceFieldMeanings.Custom ? field.Meaning : "";
        return Fields.Where(other => other != field && ((meaning.Length > 0 && other.Meaning == meaning) || (key.Length > 0 && InvoiceValues.Normalize(EffectiveLabel(other)) == key)));
    }

    // A free label built from this one ("Număr factură (2)").
    public string FreeLabelFor(DraftField field)
    {
        var baseLabel = EffectiveLabel(field);
        if (baseLabel.Length == 0) baseLabel = "Câmp";
        for (var number = 2; ; number++)
        {
            var candidate = $"{baseLabel} ({number})";
            var key = InvoiceValues.Normalize(candidate);
            if (!Fields.Any(other => other != field && InvoiceValues.Normalize(EffectiveLabel(other)) == key)) return candidate;
        }
    }

    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        var usedFields = Fields.Where(field => field.Use).ToList();
        var usedColumns = Columns.Where(column => column.Use && column.Meaning != InvoiceColumnMeanings.Ignore).ToList();
        if (usedFields.Count == 0 && usedColumns.Count == 0) problems.Add("Marchează cel puțin un câmp sau o coloană folosită la import.");
        foreach (var group in usedFields.Where(field => field.Meaning.Length > 0 && field.Meaning != InvoiceFieldMeanings.Custom).GroupBy(field => field.Meaning).Where(group => group.Count() > 1))
            problems.Add("Câmpurile " + string.Join(", ", group.Select(field => "„" + EffectiveLabel(field) + "”")) + " sunt citite cu același rol; folosește numai unul dintre ele.");
        foreach (var field in usedFields.Where(field => field.Meaning.Length == 0 || field.Meaning == InvoiceFieldMeanings.Custom))
            if (field.Name.Trim().Length == 0) problems.Add("Un câmp fără sens propriu are nevoie de o etichetă.");
        // Two used fields never share a label, whatever their roles: the labels are what the description of the stock entry and the page refer to.
        foreach (var group in usedFields.Where(field => EffectiveLabel(field).Length > 0).GroupBy(field => InvoiceValues.Normalize(EffectiveLabel(field)))
                     .Where(group => group.Count() > 1 && !group.All(field => field.Meaning.Length > 0 && field.Meaning != InvoiceFieldMeanings.Custom && field.Meaning == group.First().Meaning)))   // one role twice is reported above
            problems.Add($"Eticheta „{EffectiveLabel(group.First())}” este folosită la mai multe câmpuri; dă-le etichete diferite.");
        foreach (var group in usedColumns.Where(column => column.Label.Trim().Length > 0).GroupBy(column => InvoiceValues.Normalize(column.Label)).Where(group => group.Count() > 1))
            problems.Add($"Eticheta „{group.First().Label.Trim()}” este folosită la mai multe coloane; dă-le etichete diferite.");
        foreach (var group in usedColumns.Where(column => column.Meaning != InvoiceColumnMeanings.Other).GroupBy(column => column.Meaning).Where(group => group.Count() > 1))
            problems.Add("Coloanele " + string.Join(", ", group.Select(column => "„" + column.Label + "”")) + " sunt citite cu același rol; folosește numai una dintre ele.");
        if (usedColumns.Count > 0 && !usedColumns.Any(column => column.Meaning is InvoiceColumnMeanings.Name or InvoiceColumnMeanings.Code))
            problems.Add("Tabelul are nevoie de o coloană cu denumirea sau cu codul produsului.");
        foreach (var column in Columns.Where(column => !column.ZoneDrawn))
            problems.Add($"Celula de antet „{(column.Label.Length > 0 ? column.Label : "fără denumire")}” nu are zona coloanei desenată pe pagină (desenează zona sub celulă sau șterge celula).");
        problems.AddRange(InvoiceProductDescription.OperationProblems(ProductDescription));
        if (ProductDescription.Length > InvoiceProductDescription.MaxLength) problems.Add($"Descrierea intrării în stoc a produsului poate avea cel mult {InvoiceProductDescription.MaxLength} de caractere.");
        foreach (var mark in InvoiceProductDescription.UnknownMarks(ProductDescription, InvoiceProductDescription.Labels(this).Select(item => item.Label)))
            problems.Add($"Descrierea intrării în stoc conține marcajul <{mark}>, care nu este o etichetă activă în șablon.");
        return problems;
    }

    // The definition to be saved: positions become fractions of the page the element is on.
    public InvoiceTemplateDefinition ToDefinition(InvoiceDocument document)
    {
        InvoicePageData PageOf(int number) => document.Pages.FirstOrDefault(page => page.Number == number) ?? document.Pages[0];
        var first = document.Pages[0];
        var fields = Fields.Select(field =>
        {
            var page = PageOf(field.Page);
            return new InvoiceTemplateField(field.Id, field.Meaning, field.Name, field.Use, field.Page, field.X / page.Width, field.Y / page.Height,
                field.Width / page.Width, field.Height / page.Height, field.LabelText, field.Kind, field.Manual, field.Mode, field.LabelX / page.Width, field.LabelY / page.Height);
        }).ToList();
        InvoiceTemplateTable? table = null;
        if (HasTable && Columns.Count > 0)
        {
            var page = PageOf(HeaderPage);
            table = new InvoiceTemplateTable(HeaderPage, HeaderTop / page.Height, HeaderBottom / page.Height, RowSplit, HasIndexColumn, NameCodeSeparator.Trim(),
                Columns.OrderBy(column => column.Left).Select(column => new InvoiceTemplateColumn(column.Id, column.Label, column.Meaning, column.Use,
                    column.Left / page.Width, column.Right / page.Width, column.RowMapping, column.Manual, column.HeaderText,
                    column.Top / page.Height, column.Bottom > 0 ? column.Bottom / PageOf(column.BottomPage > 0 ? column.BottomPage : HeaderPage).Height : 0, column.TopAnchor, column.BottomAnchor, column.BottomPage,
                    column.CellTop / page.Height, column.CellBottom / page.Height, column.ZoneDrawn)).ToList(), BodyBottom / page.Height);
        }
        return new InvoiceTemplateDefinition(InvoiceTemplateDefinition.CurrentSchema, SourceKind, first.Width, first.Height, Anchors, fields, table, ProductDescription.Trim());
    }

    // The draft of a saved template exactly as it was saved, shown on a file: every position is the saved fraction of the page (no alignment
    // with the file's words, no search of the table header in it), so that editing a template changes only the elements of the template and
    // never runs the template through the analysis of the file. Aligning it with the file is the user's choice (FromDefinition, "Analizează fișierul").
    public static InvoiceTemplateDraft FromSaved(InvoiceTemplateDefinition definition, InvoiceDocument document, string supplierName, string supplierCui)
    {
        var draft = new InvoiceTemplateDraft { SourceKind = definition.SourceKind, Anchors = [.. definition.Anchors], SupplierName = supplierName, SupplierCui = supplierCui, ProductDescription = definition.ProductDescription ?? "" };
        var none = new InvoiceAlignment(0, 0, 0, 0, 0, 0);
        InvoicePageData PageOf(int number) => document.Pages.FirstOrDefault(page => page.Number == number) ?? document.Pages[0];
        foreach (var field in definition.Fields)
        {
            var page = PageOf(field.Page);
            draft.Fields.Add(new DraftField
            {
                Id = field.Id, Meaning = field.Meaning, Name = field.Name, Use = field.Use, Page = page.Number,
                X = field.X * page.Width, Y = field.Y * page.Height, Width = field.Width * page.Width, Height = field.Height * page.Height,
                LabelText = field.LabelText, Kind = field.Kind, Manual = field.Manual, Value = InvoiceTemplateEngine.ReadField(field, document, none).Value, Mode = field.Mode,
                LabelX = field.LabelX * page.Width, LabelY = field.LabelY * page.Height
            });
        }
        if (definition.Table is { } table)
        {
            var page = PageOf(table.HeaderPage);
            draft.HasTable = true;
            draft.HeaderPage = page.Number;
            draft.HeaderTop = table.HeaderTop * page.Height;
            draft.HeaderBottom = table.HeaderBottom * page.Height;
            draft.RowSplit = table.RowSplit;
            draft.HasIndexColumn = table.HasIndexColumn;
            draft.NameCodeSeparator = table.NameCodeSeparator;
            draft.BodyBottom = table.BodyBottom * page.Height;
            draft.Columns = table.Columns.Select(column => new DraftColumn
            {
                Id = column.Id, Label = column.Label, HeaderText = column.HeaderText.Length > 0 ? column.HeaderText : column.Label, Meaning = column.Meaning, Use = column.Use,
                Left = column.Left * page.Width, Right = column.Right * page.Width, RowMapping = column.RowMapping, Manual = column.Manual,
                Top = column.Top * page.Height, Bottom = column.Bottom > 0 ? column.Bottom * PageOf(column.BottomPage > 0 ? column.BottomPage : table.HeaderPage).Height : 0,
                TopAnchor = column.TopAnchor, BottomAnchor = column.BottomAnchor, BottomPage = column.BottomPage,
                CellTop = column.CellTop * page.Height, CellBottom = column.CellBottom * page.Height, ZoneDrawn = column.ZoneDrawn
            }).ToList();
        }
        draft.ProductDescription = InvoiceProductDescription.Upgrade(draft.ProductDescription, draft);
        return draft;
    }

    // The draft of a saved template, positioned on a file: the saved positions are moved by the offset that aligns the template's
    // anchors with the words of this file, so the regions land where this file has the same labels.
    public static InvoiceTemplateDraft FromDefinition(InvoiceTemplateDefinition definition, InvoiceDocument document, string supplierName, string supplierCui)
    {
        var draft = new InvoiceTemplateDraft { SourceKind = definition.SourceKind, Anchors = [.. definition.Anchors], SupplierName = supplierName, SupplierCui = supplierCui, ProductDescription = definition.ProductDescription ?? "" };
        var alignment = InvoiceTemplateEngine.Align(definition, document);
        InvoicePageData PageOf(int number) => document.Pages.FirstOrDefault(page => page.Number == number) ?? document.Pages[0];
        foreach (var field in definition.Fields)
        {
            var page = PageOf(field.Page);
            var read = InvoiceTemplateEngine.ReadField(field, document, alignment);
            draft.Fields.Add(new DraftField
            {
                Id = field.Id, Meaning = field.Meaning, Name = field.Name, Use = field.Use, Page = page.Number,
                X = read.Box?.X ?? field.X * page.Width + alignment.PageDx, Y = read.Box?.Y ?? field.Y * page.Height + alignment.PageDy,
                Width = read.Box?.Width ?? field.Width * page.Width, Height = read.Box?.Height ?? field.Height * page.Height,
                LabelText = field.LabelText, Kind = field.Kind, Manual = field.Manual, Value = read.Value, Mode = field.Mode,
                LabelX = read.Label?.X ?? field.LabelX * page.Width + alignment.PageDx, LabelY = read.Label?.Y ?? field.LabelY * page.Height + alignment.PageDy
            });
        }
        if (definition.Table is { } table)
        {
            var page = PageOf(table.HeaderPage);
            // The columns are placed as the engine places them when it reads this file (the header found in the file when its labels are the
            // template's), each keeping the meaning and the "used" choice of the template.
            var (geometry, headerBottom) = InvoiceTemplateEngine.TableGeometry(table, page, alignment, document);
            draft.HasTable = true;
            draft.HeaderPage = page.Number;
            draft.HeaderBottom = headerBottom;
            draft.HeaderTop = headerBottom - (table.HeaderBottom - table.HeaderTop) * page.Height;
            draft.RowSplit = table.RowSplit;
            draft.HasIndexColumn = table.HasIndexColumn;
            draft.NameCodeSeparator = table.NameCodeSeparator;
            draft.BodyBottom = table.BodyBottom > 0 ? table.BodyBottom * page.Height + (headerBottom - table.HeaderBottom * page.Height) : 0;
            draft.Columns = geometry.Select(column =>
            {
                var own = table.Columns.FirstOrDefault(item => item.Id == column.Id);
                return new DraftColumn
                {
                    Id = column.Id, Label = own?.Label ?? column.Label, HeaderText = own is null ? column.Label : own.HeaderText.Length > 0 ? own.HeaderText : own.Label, Meaning = own?.Meaning ?? InvoiceColumnMeanings.Other, Use = own?.Use ?? false,
                    Left = column.Left, Right = column.Right, RowMapping = column.RowMapping, Manual = own?.Manual ?? false,
                    Top = column.ZoneTop > 0 ? column.ZoneTop : own is { Top: > 0 } ? own.Top * page.Height + alignment.TableDy : 0,
                    Bottom = column.ZoneBottom > 0 ? column.ZoneBottom : own is { Bottom: > 0 } ? own.Bottom * PageOf(own.BottomPage > 0 ? own.BottomPage : table.HeaderPage).Height + alignment.TableDy : 0,
                    TopAnchor = own?.TopAnchor ?? "", BottomAnchor = own?.BottomAnchor ?? "", BottomPage = own?.BottomPage ?? 0,
                    CellTop = own is { CellTop: > 0 } ? own.CellTop * page.Height + (headerBottom - table.HeaderBottom * page.Height) : 0,
                    CellBottom = own is { CellBottom: > 0 } ? own.CellBottom * page.Height + (headerBottom - table.HeaderBottom * page.Height) : 0, ZoneDrawn = own?.ZoneDrawn ?? true
                };
            }).ToList();
        }
        draft.ProductDescription = InvoiceProductDescription.Upgrade(draft.ProductDescription, draft);
        return draft;
    }
}

// The words that identify a layout: the labels of the recognised fields, the headings of the two sides and the words of the table header.
public static class InvoiceTemplateAnchors
{
    private const int MaxAnchors = 60;

    public static List<InvoiceTemplateAnchor> From(InvoiceAnalysis analysis)
    {
        var anchors = new List<InvoiceTemplateAnchor>();
        var seen = new HashSet<string>();
        void Add(InvoiceWord word, InvoicePageData page, string group)
        {
            var text = InvoiceValues.Normalize(word.Text);
            if (text.Length < 3 || text.All(char.IsAsciiDigit)) return;
            if (!seen.Add($"{group}|{text}|{Math.Round(word.X / 4)}|{Math.Round(word.Y / 4)}")) return;
            anchors.Add(new InvoiceTemplateAnchor(text, page.Number, word.X / page.Width, word.Y / page.Height, group));
        }
        foreach (var page in analysis.Pages)
        {
            if (analysis.Table is { } table && page.Number == table.HeaderPage)
            {
                var left = table.Columns.Min(column => column.Left) - 4;
                var right = table.Columns.Max(column => column.Right) + 4;
                foreach (var word in page.Words.Where(word => word.CenterY >= table.HeaderTop - 0.5 && word.CenterY <= table.HeaderBottom + 0.5 && word.CenterX >= left && word.CenterX <= right))
                    Add(word, page, InvoiceAnchorGroups.Table);
            }
            foreach (var field in analysis.Fields.Where(field => field.Confidence >= 0.85 && field.LabelBox.Page == page.Number))
                foreach (var word in page.Words.Where(word => word.CenterX >= field.LabelBox.X - 0.5 && word.CenterX <= field.LabelBox.Right + 0.5 &&
                                                              word.CenterY >= field.LabelBox.Y - 0.5 && word.CenterY <= field.LabelBox.Bottom + 0.5))
                    Add(word, page, InvoiceAnchorGroups.Page);
            var lines = InvoiceLayout.BuildLines(page.Words);
            for (var index = 0; index < lines.Count; index++)
                foreach (var segment in InvoiceLayout.Segments(lines[index], index))
                    if (InvoiceVocabulary.MatchSection(segment.Text) is not null)
                        foreach (var word in segment.Words) Add(word, page, InvoiceAnchorGroups.Page);
        }
        return anchors.OrderByDescending(anchor => anchor.Text.Length).Take(MaxAnchors).OrderBy(anchor => anchor.Page).ThenBy(anchor => anchor.Y).ThenBy(anchor => anchor.X).ToList();
    }
}

// ---- applying a template to a file ----

public sealed record InvoiceExtractedField(string Id, string Meaning, string Name, string Value, string Kind, bool Found);

public sealed record InvoiceExtraction(IReadOnlyList<InvoiceExtractedField> Fields, IReadOnlyList<InvoiceColumn> Columns,
    IReadOnlyList<InvoiceTableRow> Rows, IReadOnlyList<string> Warnings);

public sealed record InvoiceAlignment(double PageDx, double PageDy, double TableDx, double TableDy, int MatchedAnchors, int TotalAnchors)
{
    public double Score => TotalAnchors == 0 ? 0 : MatchedAnchors / (double)TotalAnchors;
}

public sealed record InvoiceTemplateMatch(double Score, bool SupplierMatch, InvoiceAlignment Alignment);

public static class InvoiceTemplateEngine
{
    // Anchors are searched near where the template expects them: a label further than this share of the page is another word.
    private const double SearchRadius = 0.08;
    // Share of a template's anchors that must be found for the file to count as having the template's layout.
    private const double LayoutTrustedScore = 0.85;

    // The offset that puts the template's anchors on the same words of the file: the median shift of the anchors found (separately for
    // the page and for the table header, whose height above it varies with the number of address lines).
    public static InvoiceAlignment Align(InvoiceTemplateDefinition definition, InvoiceDocument document)
    {
        var pageShifts = new List<(double Dx, double Dy)>();
        var tableShifts = new List<(double Dx, double Dy)>();
        var matched = 0;
        foreach (var anchor in definition.Anchors)
        {
            var page = document.Pages.FirstOrDefault(item => item.Number == anchor.Page);
            if (page is null) continue;
            var expectedX = anchor.X * page.Width;
            var expectedY = anchor.Y * page.Height;
            InvoiceWord? best = null;
            var bestDistance = double.MaxValue;
            foreach (var word in page.Words)
            {
                if (InvoiceValues.Normalize(word.Text) != anchor.Text) continue;
                var dx = Math.Abs(word.X - expectedX) / page.Width;
                var dy = Math.Abs(word.Y - expectedY) / page.Height;
                if (dx > SearchRadius || dy > SearchRadius * 2) continue;
                var distance = dx + dy;
                if (distance < bestDistance) { bestDistance = distance; best = word; }
            }
            if (best is null) continue;
            matched++;
            (anchor.Group == InvoiceAnchorGroups.Table ? tableShifts : pageShifts).Add((best.X - expectedX, best.Y - expectedY));
        }
        static double Median(IEnumerable<double> values) => TextLine.Median(values);
        var pageDx = pageShifts.Count > 0 ? Median(pageShifts.Select(shift => shift.Dx)) : 0;
        var pageDy = pageShifts.Count > 0 ? Median(pageShifts.Select(shift => shift.Dy)) : 0;
        var tableDx = tableShifts.Count > 0 ? Median(tableShifts.Select(shift => shift.Dx)) : pageDx;
        var tableDy = tableShifts.Count > 0 ? Median(tableShifts.Select(shift => shift.Dy)) : pageDy;
        return new InvoiceAlignment(pageDx, pageDy, tableDx, tableDy, matched, definition.Anchors.Count);
    }

    // How well a saved template fits a file: the share of its anchors found, and whether the supplier's tax id is in the file.
    public static InvoiceTemplateMatch Match(InvoiceTemplateDefinition definition, string supplierCui, InvoiceDocument document)
    {
        var alignment = Align(definition, document);
        var cui = InvoiceValues.NormalizeCui(supplierCui);
        var supplier = cui.Length > 0 && document.AllWords.Any(word => word.Text.Contains(cui, StringComparison.Ordinal));
        return new InvoiceTemplateMatch(Math.Round(alignment.Score, 2), supplier, alignment);
    }

    public sealed record FieldRead(InvoiceBox? Box, string Value, InvoiceBox? Label);

    // Reads one field of a template from a file. A field with a label is looked for by its label (the nearest occurrence of the label's words
    // to where the template had it) and its value is taken next to it, so a label that moved down two lines, or a longer value, still reads
    // right; a field without a label, or one whose label is not in the file, is read from its region moved by the alignment.
    public static FieldRead ReadField(InvoiceTemplateField field, InvoiceDocument document, InvoiceAlignment alignment)
    {
        var page = document.Pages.FirstOrDefault(item => item.Number == field.Page) ?? document.Pages[0];
        var shifted = new InvoiceBox(page.Number, field.X * page.Width + alignment.PageDx, field.Y * page.Height + alignment.PageDy, field.Width * page.Width, field.Height * page.Height);
        if (!field.Manual && field.LabelText.Length > 0)
        {
            var label = FindLabel(page, field.LabelText, field.LabelX * page.Width + alignment.PageDx, field.LabelY * page.Height + alignment.PageDy);
            if (label is not null)
            {
                switch (field.Mode)
                {
                    case InvoiceFieldModes.Right:
                        if (WordsRightOf(page, label) is { Count: > 0 } right) return new FieldRead(InvoiceLayout.Union(right), InvoiceLayout.TextOf(right), label);
                        return new FieldRead(null, "", label);
                    case InvoiceFieldModes.Below:
                        if (WordsBelow(page, label, field.Kind == "number") is { Count: > 0 } below) return new FieldRead(InvoiceLayout.Union(below), InvoiceLayout.TextOf(below), label);
                        return new FieldRead(null, "", label);
                    default:
                        var region = new InvoiceBox(page.Number, label.X + (field.X - field.LabelX) * page.Width, label.Y + (field.Y - field.LabelY) * page.Height,
                            field.Width * page.Width, field.Height * page.Height);
                        var text = ReadRegion(document, page.Number, region.X, region.Y, region.Width, region.Height);
                        return new FieldRead(region, text, label);
                }
            }
        }
        // A field with a label whose label is not in the file is read from its fixed region only when the file has the template's layout
        // (nearly all its anchors found, as for a scan of the same supplier's form); on another layout a fixed region would read whatever
        // text happens to be there.
        if (!field.Manual && field.LabelText.Length > 0 && alignment.Score < LayoutTrustedScore) return new FieldRead(shifted, "", null);
        var value = ReadRegion(document, page.Number, shifted.X, shifted.Y, shifted.Width, shifted.Height);
        return new FieldRead(shifted, value, null);
    }

    // The nearest occurrence, to the expected position, of the label's words one after another on a line.
    internal static InvoiceBox? FindLabel(InvoicePageData page, string labelText, double expectedX, double expectedY)
    {
        var wanted = InvoiceVocabulary.Tokens(labelText);
        if (wanted.Length == 0) return null;
        InvoiceBox? best = null;
        var bestDistance = double.MaxValue;
        foreach (var line in InvoiceLayout.BuildLines(page.Words))
        {
            var tokens = new List<string>();
            var owners = new List<int>();
            for (var index = 0; index < line.Words.Count; index++)
                foreach (var token in InvoiceVocabulary.Tokens(line.Words[index].Text)) { tokens.Add(token); owners.Add(index); }
            for (var start = 0; start + wanted.Length <= tokens.Count; start++)
            {
                var match = true;
                for (var offset = 0; offset < wanted.Length && match; offset++) match = tokens[start + offset] == wanted[offset];
                if (!match) continue;
                var words = line.Words.Skip(owners[start]).Take(owners[start + wanted.Length - 1] - owners[start] + 1).ToList();
                var box = InvoiceLayout.Union(words);
                var distance = Math.Abs(box.X - expectedX) / page.Width + Math.Abs(box.Y - expectedY) / page.Height * 1.5;
                if (distance < bestDistance && Math.Abs(box.X - expectedX) <= 0.25 * page.Width && Math.Abs(box.Y - expectedY) <= 0.3 * page.Height) { bestDistance = distance; best = box; }
            }
        }
        return best;
    }

    // The words after a label on its line: the nearest one and those that follow it with word-sized gaps.
    private static List<InvoiceWord> WordsRightOf(InvoicePageData page, InvoiceBox label)
    {
        var centre = label.Y + label.Height / 2;
        var line = page.Words.Where(word => word.X >= label.Right - 0.5 && Math.Abs(word.CenterY - centre) <= 0.55 * Math.Max(1, label.Height)).OrderBy(word => word.X).ToList();
        var result = new List<InvoiceWord>();
        foreach (var word in line)
        {
            if (result.Count == 0) { if (word.X - label.Right > 60 * Math.Max(1, label.Height)) break; }
            else if (word.X - result[^1].Right > 1.5 * Math.Max(1, label.Height)) break;
            result.Add(word);
        }
        return result;
    }

    // The run under a label (the totals block): on the nearest of the next lines that has a run overlapping the label horizontally.
    private static List<InvoiceWord> WordsBelow(InvoicePageData page, InvoiceBox label, bool numeric)
    {
        var lines = InvoiceLayout.BuildLines(page.Words.Where(word => word.CenterY > label.Bottom - 0.3 * label.Height)).Take(4).ToList();
        for (var index = 0; index < lines.Count; index++)
        {
            if (lines[index].Y - (label.Y + label.Height / 2) > 4.5 * Math.Max(1, label.Height)) break;
            foreach (var segment in InvoiceLayout.Segments(lines[index], index))
            {
                if (InvoiceLayout.Overlap(label.X, label.Right, segment.X, segment.Right) <= 0) continue;
                if (numeric && InvoiceValues.ParseNumber(segment.Text) is null) continue;
                return segment.Words;
            }
        }
        return [];
    }

    public static string ReadRegion(InvoiceDocument document, int pageNumber, double x, double y, double width, double height)
    {
        var page = document.Pages.FirstOrDefault(item => item.Number == pageNumber);
        if (page is null) return "";
        var words = page.Words.Where(word => word.CenterX >= x - 0.5 && word.CenterX <= x + width + 0.5 && word.CenterY >= y - 0.5 && word.CenterY <= y + height + 0.5).ToList();
        return words.Count == 0 ? "" : InvoiceLayout.TextOf(words);
    }

    // Reads a file with a template: the value of every used field (from its region, moved by the alignment) and the rows of the table.
    public static InvoiceExtraction Apply(InvoiceTemplateDefinition definition, InvoiceDocument document)
    {
        var alignment = Align(definition, document);
        var warnings = new List<string>();
        if (definition.Anchors.Count > 0 && alignment.Score < 0.4)
            warnings.Add("Fișierul seamănă puțin cu șablonul: doar " + alignment.MatchedAnchors + " din " + alignment.TotalAnchors + " repere au fost găsite.");
        var hint = InvoiceValues.DecimalStyle(document.AllWords.Select(word => word.Text));
        InvoicePageData PageOf(int number) => document.Pages.FirstOrDefault(page => page.Number == number) ?? document.Pages[0];

        var fields = new List<InvoiceExtractedField>();
        foreach (var field in definition.Fields.Where(field => field.Use))
        {
            var value = ReadField(field, document, alignment).Value;
            fields.Add(new InvoiceExtractedField(field.Id, field.Meaning, field.Name, value, field.Kind, value.Length > 0));
        }

        IReadOnlyList<InvoiceColumn> columns = [];
        IReadOnlyList<InvoiceTableRow> rows = [];
        if (definition.Table is { } table)
        {
            var page = PageOf(table.HeaderPage);
            var (tableColumns, headerBottom) = TableGeometry(table, page, alignment, document);
            var read = InvoiceTableReader.ReadRows(document, tableColumns, page.Number, headerBottom, hint, table.RowSplit);
            // The columns keep the positions of the template; only the rows come from this file.
            columns = tableColumns;
            rows = read.Rows;
            if (rows.Count == 0) warnings.Add("Nu s-au putut citi rânduri sub antetul tabelului.");
            if (table.NameCodeSeparator.Length > 0) rows = SplitNameCode(rows, columns, table.NameCodeSeparator);
        }
        return new InvoiceExtraction(fields, columns, rows, warnings);
    }

    // Where the table is in this file. The meaning of each column (and whether it is used, how it is read) comes from the template; the
    // geometry from the file itself when its header is recognised and its labels are the template's: a supplier's invoices differ in
    // how wide the columns are and how far down the table starts, so a header found in the file places the columns better than the
    // positions of the file the template was made from. Columns that are not found keep the template's positions moved by the alignment.
    // ---- the data zone of a column: anchored to the elements above and below it, not to coordinates ----

    // The text runs of a page that belong to a column, top to bottom. The runs are those of the whole lines (so they are the same whatever the
    // width given to the column); strict = the centre of the run is inside the column (where an anchor is chosen), else it is enough that the run
    // overlaps the column (where an anchor is looked for: the header of another file may give the column another width).
    internal static List<Segment> ColumnSegments(InvoicePageData page, double left, double right, bool strict = false) =>
        InvoiceLayout.BuildLines(page.Words).SelectMany((line, index) => InvoiceLayout.Segments(line, index))
            .Where(segment => strict ? (segment.X + segment.Right) / 2 >= left - 3 && (segment.X + segment.Right) / 2 <= right + 3 : InvoiceLayout.Overlap(segment.X, segment.Right, left, right) > 0)
            .OrderBy(segment => segment.CenterY).ToList();

    // The text of the element right above a horizontal line of the column (empty when there is none), and the one right below it.
    public static string AnchorAbove(InvoicePageData page, double left, double right, double y) =>
        ColumnSegments(page, left, right, strict: true).Where(segment => segment.Box.Y + segment.Box.Height <= y + 2).OrderByDescending(segment => segment.Box.Y + segment.Box.Height).FirstOrDefault()?.Text ?? "";

    public static string AnchorBelow(InvoicePageData page, double left, double right, double y) =>
        ColumnSegments(page, left, right, strict: true).Where(segment => segment.Box.Y >= y - 2).OrderBy(segment => segment.Box.Y).FirstOrDefault()?.Text ?? "";

    // Where the data of a column starts and ends in this file, found from the text of the element above (the zone starts under it) and
    // below (the zone ends over it); 0 = not anchored, or the element is not in this file (the zone is then automatic, never a fixed position).
    internal static (double Top, double Bottom, int BottomPage) ResolveZone(InvoiceTemplateColumn column, InvoiceDocument document, int headerPage, double left, double right,
        double expectedTop, double expectedBottom)
    {
        double top = 0, bottom = 0;
        var bottomPage = 0;
        if (column.TopAnchor.Length > 0 && document.Pages.FirstOrDefault(page => page.Number == headerPage) is { } first)
        {
            var key = InvoiceValues.Normalize(column.TopAnchor);
            var found = ColumnSegments(first, left, right).Where(segment => InvoiceValues.Normalize(segment.Text) == key)
                .OrderBy(segment => Math.Abs(segment.Box.Y + segment.Box.Height - expectedTop)).FirstOrDefault();
            if (found is not null) top = found.Box.Y + found.Box.Height;
        }
        if (column.BottomAnchor.Length > 0)
        {
            var key = InvoiceValues.Normalize(column.BottomAnchor);
            var preferred = column.BottomPage > 0 ? column.BottomPage : headerPage;
            foreach (var page in document.Pages.Where(page => page.Number >= headerPage).OrderBy(page => page.Number == preferred ? 0 : 1).ThenBy(page => page.Number))
            {
                var found = ColumnSegments(page, left, right).Where(segment => InvoiceValues.Normalize(segment.Text) == key)
                    .OrderBy(segment => Math.Abs(segment.Box.Y - expectedBottom)).FirstOrDefault();
                if (found is null) continue;
                bottom = found.Box.Y;
                bottomPage = page.Number == headerPage ? 0 : page.Number;
                break;
            }
        }
        return (top, bottom, bottomPage);
    }

    // The columns with the data zone of each one found in this file.
    private static List<InvoiceColumn> WithZones(List<InvoiceColumn> columns, InvoiceTemplateTable table, InvoicePageData page, InvoiceDocument? document, InvoiceAlignment alignment)
    {
        if (document is null || !table.Columns.Any(column => column.TopAnchor.Length > 0 || column.BottomAnchor.Length > 0)) return columns;
        return columns.Select(column =>
        {
            if (table.Columns.FirstOrDefault(item => item.Id == column.Id) is not { } own || (own.TopAnchor.Length == 0 && own.BottomAnchor.Length == 0)) return column;
            var (top, bottom, bottomPage) = ResolveZone(own, document, page.Number, column.Left, column.Right, own.Top * page.Height + alignment.TableDy, own.Bottom * page.Height + alignment.TableDy);
            return column with { ZoneTop = top, ZoneBottom = bottom, ZoneBottomPage = bottomPage };
        }).ToList();
    }

    internal static (List<InvoiceColumn> Columns, double HeaderBottom) TableGeometry(InvoiceTemplateTable table, InvoicePageData page, InvoiceAlignment alignment, InvoiceDocument? document = null)
    {
        var (columns, headerBottom) = TableGeometryCore(table, page, alignment);
        return (WithZones(columns, table, page, document, alignment), headerBottom);
    }

    internal static (List<InvoiceColumn> Columns, double HeaderBottom) TableGeometryCore(InvoiceTemplateTable table, InvoicePageData page, InvoiceAlignment alignment)
    {
        var fallback = table.Columns.Select(column => new InvoiceColumn(column.Id, column.Label, column.Use ? column.Meaning : InvoiceColumnMeanings.Ignore,
            column.Left * page.Width + alignment.TableDx, column.Right * page.Width + alignment.TableDx, column.RowMapping)).ToList();
        var fallbackBottom = table.HeaderBottom * page.Height + alignment.TableDy;
        var detected = InvoiceTableReader.FindHeader(page);
        if (detected is null) return (fallback, fallbackBottom);

        var cells = detected.Cells.ToList();
        var used = new HashSet<int>();
        var matches = new Dictionary<string, HeaderCell>();
        foreach (var column in table.Columns)
        {
            var key = InvoiceValues.Normalize(column.HeaderText.Length > 0 ? column.HeaderText : column.Label);
            var index = key.Length == 0 ? -1 : cells.FindIndex(cell => !used.Contains(cells.IndexOf(cell)) && InvoiceValues.Normalize(cell.Label) == key);
            if (index < 0) continue;
            used.Add(index);
            matches[column.Id] = cells[index];
        }
        var meaningful = table.Columns.Count(column => column.Use && column.Meaning != InvoiceColumnMeanings.Ignore);
        var matchedMeaningful = table.Columns.Count(column => column.Use && column.Meaning != InvoiceColumnMeanings.Ignore && matches.ContainsKey(column.Id));
        if (matchedMeaningful < Math.Max(2, (meaningful + 1) / 2)) return (fallback, fallbackBottom);

        var dx = TextLine.Median(table.Columns.Where(column => matches.ContainsKey(column.Id)).Select(column => matches[column.Id].Left - column.Left * page.Width));
        var columns = new List<InvoiceColumn>();
        foreach (var column in table.Columns)
        {
            var meaning = column.Use ? column.Meaning : InvoiceColumnMeanings.Ignore;
            if (matches.TryGetValue(column.Id, out var cell)) columns.Add(new InvoiceColumn(column.Id, column.Label, meaning, cell.Left, cell.Right, column.RowMapping));
            else columns.Add(new InvoiceColumn(column.Id, column.Label, meaning, column.Left * page.Width + dx, column.Right * page.Width + dx, column.RowMapping));
        }
        // Header cells the template does not know: kept as unused columns so that their text does not land in a neighbour.
        for (var index = 0; index < cells.Count; index++)
            if (!used.Contains(index)) columns.Add(new InvoiceColumn("x" + (index + 1).ToString(CultureInfo.InvariantCulture), cells[index].Label, InvoiceColumnMeanings.Ignore, cells[index].Left, cells[index].Right));
        return (columns.OrderBy(column => column.Left).ToList(), detected.Bottom);
    }

    // "GS0012991 ; ALIMENTATOR ..." in the name column: the text before the separator is the product code, the rest the name. The code goes
    // to the code column when the template has one, else the row gets a code cell of its own (column id "code").
    internal static IReadOnlyList<InvoiceTableRow> SplitNameCode(IReadOnlyList<InvoiceTableRow> rows, IReadOnlyList<InvoiceColumn> columns, string separator)
    {
        var name = columns.FirstOrDefault(column => column.Meaning == InvoiceColumnMeanings.Name);
        if (name is null) return rows;
        var codeColumn = columns.FirstOrDefault(column => column.Meaning == InvoiceColumnMeanings.Code)?.Id ?? "code";
        var result = new List<InvoiceTableRow>();
        foreach (var row in rows)
        {
            var text = row.Cells[name.Id];
            var index = text.IndexOf(separator, StringComparison.Ordinal);
            if (index <= 0) { result.Add(row); continue; }
            var cells = new Dictionary<string, string>(row.Cells) { [name.Id] = text[(index + separator.Length)..].Trim(), [codeColumn] = text[..index].Trim() };
            result.Add(row with { Cells = cells });
        }
        return result;
    }

    // Summary of a value for display/import: dates as dd.MM.yyyy, numbers with the document's separators removed, else the text as is.
    public static string Display(string value, string kind, char? hint = null)
    {
        if (kind == "date" && InvoiceValues.ParseDate(value) is { } date) return date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        if (kind == "number" && InvoiceValues.ParseNumber(value, hint) is { } number) return number.ToString("0.########", CultureInfo.InvariantCulture);
        return value;
    }
}
