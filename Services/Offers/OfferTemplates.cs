using System.Globalization;
using System.Text;
using System.Text.Json;

namespace BlazorStoc.Services;

// Module "Oferte": templates of offer sheets (devize-oferta in .xlsx). A template says where the data of an offer sits, by the labels of the sheet
// and not by fixed rows: the offer header (number, title, category, beneficiary) is read next to or below its label, the table is found by the labels
// of its header row (repeated in every section: Echipamente, Manopera, Cheltuieli), the columns are chosen by letter, the sections to import are
// chosen by name, and rows such as the totals are ignored. Prices and VAT are not read. The unit is read only to tell the lines in pieces (stock)
// from the others (meters, hours...): there is no nomenclator of units, stock keeps only pieces.

public sealed class OfferHeaderField
{
    public const string Right = "right";
    public const string Below = "below";
    public const string NumberKey = "number", TitleKey = "title", CategoryKey = "category", BeneficiaryKey = "beneficiary";
    public string Key { get; set; } = "";
    // The text of the label cell (compared without case, diacritics or punctuation).
    public string Label { get; set; } = "";
    // Where the value is: to the right of the label (first filled cell) or in the cell below it.
    public string Position { get; set; } = Right;
}

public sealed class OfferSectionRule
{
    public string Name { get; set; } = "";
    public bool Import { get; set; }
}

public sealed class OfferTemplateDefinition
{
    public string? Sheet { get; set; }
    // Column letters of the table (Name, Unit and Quantity are required) and the label each has in the header row of the table.
    public string NumberColumn { get; set; } = "";
    public string TypeColumn { get; set; } = "";
    public string NameColumn { get; set; } = "";
    public string UnitColumn { get; set; } = "";
    public string QuantityColumn { get; set; } = "";
    public Dictionary<string, string> ColumnLabels { get; set; } = [];
    public List<OfferHeaderField> HeaderFields { get; set; } = [];
    public List<OfferSectionRule> Sections { get; set; } = [];
    // A row with a cell starting with one of these texts is not a line (totals, "Fara TVA").
    public List<string> IgnoreRows { get; set; } = ["Total", "Fără TVA"];

    public string Serialize() => JsonSerializer.Serialize(this);
    public static OfferTemplateDefinition? Deserialize(string json)
    {
        try { return JsonSerializer.Deserialize<OfferTemplateDefinition>(json); } catch (JsonException) { return null; }
    }
    public OfferTemplateDefinition Clone() => Deserialize(Serialize())!;
}

public sealed record OfferLine(int Row, string Number, string ProductType, string Name, string Unit, decimal Quantity, bool InStock);
public sealed record OfferSection(string Name, bool Known, bool Import, IReadOnlyList<OfferLine> Lines);
// What a template reads from an offer sheet.
public sealed record OfferReading(IReadOnlyDictionary<string, string> Fields, IReadOnlyList<OfferSection> Sections, IReadOnlyList<string> Problems)
{
    public string Field(string key) => Fields.GetValueOrDefault(key, string.Empty);
    // Lines of the sections to import that are pieces (the ones stock keeps).
    public IEnumerable<OfferLine> StockLines => Sections.Where(section => section.Import).SelectMany(section => section.Lines).Where(line => line.InStock);
}

public static class OfferRules
{
    public const int NameMaximumLength = 120;
    public static readonly string[] ColumnKeys = ["number", "type", "name", "unit", "quantity"];

    // The identity of a text: without case, diacritics and anything that is not a letter or a digit.
    public static string Key(string? text) => SystemTypeRules.Key(text);

    public static bool IsPiece(string? unit)
    {
        var key = Key(unit);
        return key.StartsWith("buc", StringComparison.Ordinal) || key is "set" or "pcs" or "pc";
    }

    public static string ColumnOf(OfferTemplateDefinition definition, string key) => key switch
    {
        "number" => definition.NumberColumn, "type" => definition.TypeColumn, "name" => definition.NameColumn,
        "unit" => definition.UnitColumn, "quantity" => definition.QuantityColumn, _ => ""
    };

    public static string? Problem(string name, OfferTemplateDefinition definition, IEnumerable<(int Id, string Name)> others, int? ignoreId = null)
    {
        var text = SystemTypeRules.Clean(name);
        if (Key(text).Length == 0) return "Completează denumirea șablonului.";
        if (text.Length > NameMaximumLength) return $"Denumirea poate avea cel mult {NameMaximumLength} de caractere.";
        if (others.Any(other => other.Id != ignoreId && Key(other.Name) == Key(text))) return "Există deja un șablon cu această denumire.";
        foreach (var key in new[] { "name", "unit", "quantity" })
            if (OfferTemplateEngine.ColumnIndex(ColumnOf(definition, key)) == 0) return $"Alege coloana pentru {ColumnLabel(key)}.";
        var used = ColumnKeys.Select(key => OfferTemplateEngine.ColumnIndex(ColumnOf(definition, key))).Where(index => index > 0).ToList();
        if (used.Count != used.Distinct().Count()) return "Aceeași coloană nu poate fi aleasă pentru două câmpuri.";
        foreach (var key in new[] { "name", "unit", "quantity" })
            if (!definition.ColumnLabels.TryGetValue(key, out var label) || Key(label).Length == 0)
                return $"Completează eticheta coloanei {ColumnLabel(key)} din antetul tabelului (cu ea se găsește tabelul).";
        if (definition.HeaderFields.Any(field => Key(field.Label).Length == 0)) return "Un câmp de antet nu are eticheta completată.";
        return null;
    }

    public static string ColumnLabel(string key) => key switch
    {
        "number" => "Nr.", "type" => "Tip produs", "name" => "Denumire", "unit" => "Unitate de măsură", "quantity" => "Cantitate", _ => key
    };

    public static string FieldLabel(string key) => key switch
    {
        OfferHeaderField.NumberKey => "Număr ofertă", OfferHeaderField.TitleKey => "Titlu", OfferHeaderField.CategoryKey => "Categoria (sistem)",
        OfferHeaderField.BeneficiaryKey => "Beneficiar", _ => key
    };

    // A short text of what a definition holds, for the journal (before -> after).
    public static string Describe(OfferTemplateDefinition definition, string part) => part switch
    {
        "sheet" => string.IsNullOrWhiteSpace(definition.Sheet) ? "prima foaie" : definition.Sheet!,
        "columns" => string.Join(", ", ColumnKeys.Where(key => ColumnOf(definition, key).Length > 0).Select(key => $"{ColumnLabel(key)}={ColumnOf(definition, key).ToUpperInvariant()}")),
        "labels" => string.Join(", ", ColumnKeys.Where(key => definition.ColumnLabels.ContainsKey(key)).Select(key => $"{ColumnLabel(key)}=\"{definition.ColumnLabels[key]}\"")),
        "header" => string.Join(", ", definition.HeaderFields.Select(field => $"{FieldLabel(field.Key)}=\"{field.Label}\" ({(field.Position == OfferHeaderField.Below ? "dedesubt" : "la dreapta")})")),
        "sections" => string.Join(", ", definition.Sections.Select(section => $"{section.Name}={(section.Import ? "da" : "nu")}")),
        "ignore" => string.Join(", ", definition.IgnoreRows),
        _ => ""
    };

    public static readonly string[] DescribedParts = ["sheet", "columns", "labels", "header", "sections", "ignore"];

    public static string PartName(string part) => part switch
    {
        "sheet" => "Foaia", "columns" => "Coloane", "labels" => "Etichete coloane", "header" => "Câmpuri antet", "sections" => "Secțiuni importate", "ignore" => "Rânduri ignorate", _ => part
    };
}

public static class OfferTemplateEngine
{
    public static int ColumnIndex(string? letters) => XlsxReader.ColumnIndex(letters);

    private static string OneLine(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    // The label of a cell and the value it introduces.
    private static bool LabelMatches(string cellText, string label)
    {
        var key = OfferRules.Key(cellText);
        var wanted = OfferRules.Key(label);
        return wanted.Length > 0 && key.StartsWith(wanted, StringComparison.Ordinal);
    }

    // The value of a header field: the first filled cell to the right of the label (after the cells the label itself spans), or the cell below it.
    public static string ReadField(XlsxSheet sheet, OfferHeaderField field)
    {
        for (var row = 1; row <= sheet.LastRow; row++)
            for (var column = 1; column <= sheet.LastColumn; column++)
            {
                var text = sheet.Text(row, column);
                if (text.Length == 0 || !LabelMatches(text, field.Label)) continue;
                var merge = sheet.MergeAt(row, column);
                if (field.Position == OfferHeaderField.Below) return OneLine(sheet.Text(row + 1, column));
                for (var next = (merge?.LastColumn ?? column) + 1; next <= sheet.LastColumn; next++)
                    if (sheet.Text(row, next).Trim().Length > 0) return OneLine(sheet.Text(row, next));
                return string.Empty;
            }
        return string.Empty;
    }

    // The rows that are the header of a table: every mapped column carries its label.
    public static List<int> HeaderRows(XlsxSheet sheet, OfferTemplateDefinition definition)
    {
        var labelled = OfferRules.ColumnKeys
            .Select(key => (Column: ColumnIndex(OfferRules.ColumnOf(definition, key)), Label: OfferRules.Key(definition.ColumnLabels.GetValueOrDefault(key))))
            .Where(item => item.Column > 0 && item.Label.Length > 0).ToList();
        var rows = new List<int>();
        if (labelled.Count == 0) return rows;
        for (var row = 1; row <= sheet.LastRow; row++)
            if (labelled.All(item => OfferRules.Key(sheet.Text(row, item.Column)).Contains(item.Label, StringComparison.Ordinal))) rows.Add(row);
        return rows;
    }

    // A row with a single filled cell that is followed by a header row: the title of a section.
    private static string? SectionTitle(XlsxSheet sheet, int row)
    {
        if (row < 1) return null;
        var filled = Enumerable.Range(1, sheet.LastColumn).Where(column => sheet.Text(row, column).Trim().Length > 0).ToList();
        return filled.Count == 1 ? OneLine(sheet.Text(row, filled[0])) : null;
    }

    public static OfferReading Apply(OfferTemplateDefinition definition, XlsxWorkbook workbook)
    {
        var problems = new List<string>();
        var sheet = workbook.Sheet(definition.Sheet);
        if (sheet is null)
            return new OfferReading(new Dictionary<string, string>(), [], [$"Foaia „{definition.Sheet}” nu există în fișier."]);
        var fields = definition.HeaderFields.ToDictionary(field => field.Key, field => ReadField(sheet, field));
        var headers = HeaderRows(sheet, definition);
        if (headers.Count == 0) problems.Add("Nu s-a găsit tabelul: nicio linie nu are etichetele coloanelor din șablon.");
        var nameColumn = ColumnIndex(definition.NameColumn);
        var unitColumn = ColumnIndex(definition.UnitColumn);
        var quantityColumn = ColumnIndex(definition.QuantityColumn);
        var sections = new List<OfferSection>();
        for (var index = 0; index < headers.Count; index++)
        {
            var header = headers[index];
            var title = SectionTitle(sheet, header - 1) ?? $"Secțiunea {index + 1}";
            var end = index + 1 < headers.Count ? headers[index + 1] - 1 : sheet.LastRow;
            // The title row of the next section is not part of this one.
            if (index + 1 < headers.Count && SectionTitle(sheet, headers[index + 1] - 1) is not null) end--;
            var lines = new List<OfferLine>();
            for (var row = header + 1; row <= end; row++)
            {
                if (IgnoredRow(sheet, row, definition)) continue;
                var name = sheet.Text(row, nameColumn).Replace("\r\n", "\n").Replace('\r', '\n').Trim();
                var quantity = QuantityOf(sheet, row, quantityColumn);
                if (name.Length == 0 || quantity is not > 0) continue;
                var unit = OneLine(sheet.Text(row, unitColumn));
                lines.Add(new OfferLine(row, OneLine(sheet.Text(row, ColumnIndex(definition.NumberColumn))), OneLine(sheet.Text(row, ColumnIndex(definition.TypeColumn))),
                    name, unit, quantity.Value, OfferRules.IsPiece(unit)));
            }
            var rule = definition.Sections.FirstOrDefault(item => OfferRules.Key(item.Name) == OfferRules.Key(title));
            sections.Add(new OfferSection(title, rule is not null, rule?.Import ?? false, lines));
            if (rule is null) problems.Add($"Secțiunea „{title}” nu este în șablon: nu se importă.");
        }
        return new OfferReading(fields, sections, problems);
    }

    private static bool IgnoredRow(XlsxSheet sheet, int row, OfferTemplateDefinition definition)
    {
        foreach (var prefix in definition.IgnoreRows)
        {
            var wanted = Tokens(prefix);
            if (wanted.Count == 0) continue;
            for (var column = 1; column <= sheet.LastColumn; column++)
            {
                var words = Tokens(sheet.Text(row, column));
                if (words.Count >= wanted.Count && wanted.Select((word, index) => words[index] == word).All(same => same)) return true;
            }
        }
        return false;
    }

    // The words of a text without case, diacritics and punctuation ("Total echipamente:" starts with "Total", "Totalizator" does not).
    private static List<string> Tokens(string text) =>
        [.. text.Split(TokenSeparators, StringSplitOptions.RemoveEmptyEntries).Select(OfferRules.Key).Where(word => word.Length > 0)];
    private static readonly char[] TokenSeparators = [' ', (char)9, (char)13, (char)10, ':', ';', ',', '.', '-', '(', ')'];

    private static decimal? QuantityOf(XlsxSheet sheet, int row, int column)
    {
        if (column == 0) return null;
        if (sheet.Number(row, column) is { } number) return (decimal)number;
        var text = sheet.Text(row, column).Trim().Replace(',', '.');
        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    // How well an offer fits a template: the share of the labels it looks for (columns of the table, header fields) that the sheet has.
    public static double Score(OfferTemplateDefinition definition, XlsxWorkbook workbook)
    {
        var sheet = workbook.Sheet(definition.Sheet);
        if (sheet is null) return 0;
        var total = 1 + definition.HeaderFields.Count;
        var found = HeaderRows(sheet, definition).Count > 0 ? 1 : 0;
        foreach (var field in definition.HeaderFields)
            for (var row = 1; row <= sheet.LastRow; row++)
                if (Enumerable.Range(1, sheet.LastColumn).Any(column => LabelMatches(sheet.Text(row, column), field.Label))) { found++; break; }
        return (double)found / total;
    }

    // The active template that fits the offer completely (the same labels), or none.
    public static T? Choose<T>(IEnumerable<T> templates, Func<T, OfferTemplateDefinition> definition, Func<T, bool> active, XlsxWorkbook workbook) where T : class =>
        templates.Where(active).Select(template => (Template: template, Score: Score(definition(template), workbook))).Where(item => item.Score >= 0.999)
            .Select(item => item.Template).FirstOrDefault();

    // The text a column has in the header row of the table of a sample sheet (proposed as its label when the column is chosen).
    public static string LabelAt(XlsxSheet sheet, int column)
    {
        var headers = HeaderRows(sheet, Suggest(sheet));
        return headers.Count == 0 ? string.Empty : OneLine(sheet.Text(headers[0], column));
    }

    // A first draft of a template read from a sample sheet: the columns by the usual labels, the header fields next to their labels, the sections.
    public static OfferTemplateDefinition Suggest(XlsxSheet sheet)
    {
        var definition = new OfferTemplateDefinition { Sheet = sheet.Name };
        (string Key, string[] Words)[] vocabulary =
        [
            ("number", ["nr", "nrcrt"]), ("type", ["tipprodus"]), ("name", ["denumire", "denumireaproduselor", "denumireaprodusului"]),
            ("unit", ["unitate", "um", "unitatemasura"]), ("quantity", ["cantitate", "cant"])
        ];
        int headerRow = 0;
        for (var row = 1; row <= sheet.LastRow && headerRow == 0; row++)
        {
            var found = new Dictionary<string, (int Column, string Text)>();
            for (var column = 1; column <= sheet.LastColumn; column++)
            {
                var key = OfferRules.Key(sheet.Text(row, column));
                if (key.Length == 0) continue;
                foreach (var (name, words) in vocabulary)
                    if (!found.ContainsKey(name) && words.Any(word => key == word || (word.Length > 3 && key.StartsWith(word, StringComparison.Ordinal)))) found[name] = (column, OneLine(sheet.Text(row, column)));
            }
            if (found.ContainsKey("name") && found.ContainsKey("unit") && found.ContainsKey("quantity"))
            {
                headerRow = row;
                foreach (var (key, (column, text)) in found)
                {
                    definition.ColumnLabels[key] = text;
                    switch (key)
                    {
                        case "number": definition.NumberColumn = XlsxReader.ColumnLetter(column); break;
                        case "type": definition.TypeColumn = XlsxReader.ColumnLetter(column); break;
                        case "name": definition.NameColumn = XlsxReader.ColumnLetter(column); break;
                        case "unit": definition.UnitColumn = XlsxReader.ColumnLetter(column); break;
                        case "quantity": definition.QuantityColumn = XlsxReader.ColumnLetter(column); break;
                    }
                }
            }
        }
        (string Key, string Label)[] labels = [(OfferHeaderField.NumberKey, "Oferta Nr"), (OfferHeaderField.CategoryKey, "Categoria"), (OfferHeaderField.BeneficiaryKey, "Beneficiar")];
        foreach (var (key, label) in labels)
        {
            var field = new OfferHeaderField { Key = key, Label = label, Position = OfferHeaderField.Right };
            if (ReadField(sheet, field).Length > 0) definition.HeaderFields.Add(field);
            if (key == OfferHeaderField.NumberKey && definition.HeaderFields.Any(item => item.Key == key))
            {
                // The title of the offer sits under the number, in a cell of its own.
                var below = new OfferHeaderField { Key = OfferHeaderField.TitleKey, Label = label, Position = OfferHeaderField.Below };
                if (ReadField(sheet, below).Length > 0) definition.HeaderFields.Add(below);
            }
        }
        if (definition.NameColumn.Length > 0)
            foreach (var header in HeaderRows(sheet, definition))
                if (SectionTitle(sheet, header - 1) is { } title && !definition.Sections.Any(item => OfferRules.Key(item.Name) == OfferRules.Key(title)))
                    definition.Sections.Add(new OfferSectionRule { Name = title, Import = DefaultImport(title) });
        return definition;
    }

    // Equipment is imported; labour, services and expenses are not goods in stock.
    private static bool DefaultImport(string title)
    {
        var key = OfferRules.Key(title);
        return !(key.StartsWith("manoper", StringComparison.Ordinal) || key.StartsWith("chelt", StringComparison.Ordinal) || key.StartsWith("servic", StringComparison.Ordinal));
    }
}

// A saved template.
public sealed record OfferTemplateRecord(int Id, string Name, bool Active, OfferTemplateDefinition Definition, long Version, string UpdatedBy, string UpdatedUtc);

public class OfferTemplateException(string message) : Exception(message);

public interface IOfferTemplateRepository
{
    Task<IReadOnlyList<OfferTemplateRecord>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<OfferTemplateRecord> CreateAsync(string name, OfferTemplateDefinition definition, CancellationToken cancellationToken = default);
    Task<OfferTemplateRecord> SaveAsync(OfferTemplateRecord original, string name, OfferTemplateDefinition definition, CancellationToken cancellationToken = default);
    Task<OfferTemplateRecord> SetActiveAsync(OfferTemplateRecord original, bool active, CancellationToken cancellationToken = default);
    // Administrator only.
    Task DeleteAsync(OfferTemplateRecord original, CancellationToken cancellationToken = default);
}

public static class OfferNavigation
{
    public const string TemplatesUrl = "/oferte/sabloane";
    public static string TemplateUrl(int id) => $"{TemplatesUrl}?sablon={id}";
}
