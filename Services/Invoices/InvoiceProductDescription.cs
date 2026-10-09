using System.Text.RegularExpressions;

namespace BlazorStoc.Services;

// The description template of a product taken from an invoice: free text with <label> marks, the same way the notification templates
// work. A mark names a label of the template: a used header field (by its label) or the value of a used table column (by the title of the
// column, for example <Preț unitar>); the values of the columns are read, row by row, when an invoice is imported.
public static partial class InvoiceProductDescription
{
    public const int MaxLength = 400;

    [GeneratedRegex("<([^<>\r\n]{1,80})>")]
    private static partial Regex Mark();

    // The label of a table column: the name the column has in the template (the header text of the invoice, or what the user named it), and
    // only when it has none the title of its meaning. The vocabulary of the template is its own, built from what stays active on the page.
    public static string ColumnOwnLabel(DraftColumn column) => column.Label.Trim().Length > 0 ? column.Label.Trim() : InvoiceVocabulary.ColumnTitle(column.Meaning);

    // The label of the product code that the template takes from the name of the product when the table has no column of its own for it.
    public const string CodeLabel = "Cod produs";

    // Descriptions written while the labels of the columns were the general titles of their meanings (<Preț unitar>, or <Valoare Preț unitar>
    // before that) use the name each column has in this template.
    public static string Upgrade(string text, InvoiceTemplateDraft draft)
    {
        var result = text ?? "";
        foreach (var meaning in InvoiceVocabulary.ColumnMeanings.Select(item => item.Key))
        {
            var title = InvoiceVocabulary.ColumnTitle(meaning);
            result = result.Replace("<Valoare " + title + ">", "<" + title + ">", StringComparison.Ordinal);
            var used = draft.Columns.Where(column => column.Use && column.Meaning == meaning).ToList();
            if (used.Count != 1) continue;
            var own = ColumnLabels(draft).FirstOrDefault(item => item.Column == used[0]).Label;
            var taken = draft.Fields.Any(field => field.Use && InvoiceValues.Normalize(InvoiceTemplateDraft.EffectiveLabel(field)) == InvoiceValues.Normalize(title))
                || draft.Columns.Any(column => column != used[0] && column.Use && column.Meaning != InvoiceColumnMeanings.Ignore && InvoiceValues.Normalize(ColumnOwnLabel(column)) == InvoiceValues.Normalize(title));
            if (own is { Length: > 0 } && own != title && !taken) result = result.Replace("<" + title + ">", "<" + own + ">", StringComparison.Ordinal);
        }
        return result;
    }

    // Renaming a label renames its marks in the description of the stock entry (the text follows the name the user gave).
    public static string RenameMark(string text, string oldLabel, string newLabel)
    {
        var key = InvoiceValues.Normalize(oldLabel);
        if (key.Length == 0 || newLabel.Trim().Length == 0) return text ?? "";
        return Mark().Replace(text ?? "", match => InvoiceValues.Normalize(match.Groups[1].Value) == key ? "<" + newLabel.Trim() + ">" : match.Value);
    }

    // Removes the marks of a label from a text (a column or a field that left the template), with the space that was around it.
    public static string RemoveMark(string text, string label)
    {
        var key = InvoiceValues.Normalize(label);
        var result = Mark().Replace(text ?? "", match => InvoiceValues.Normalize(match.Groups[1].Value) == key ? "" : match.Value);
        return Regex.Replace(result, " {2,}", " ").Trim();
    }

    // The labels of the used header fields of the template.
    public static IReadOnlyList<(string Label, string Description)> FieldLabels(InvoiceTemplateDraft draft)
    {
        var result = new List<(string, string)>();
        foreach (var field in draft.Fields.Where(field => field.Use))
        {
            var label = InvoiceTemplateDraft.EffectiveLabel(field);
            if (label.Length > 0 && result.All(item => InvoiceValues.Normalize(item.Item1) != InvoiceValues.Normalize(label))) result.Add((label, "Câmp din antet"));
        }
        return result;
    }

    // The labels of the values of the used table columns ("Valoare Cantitate"...), with the column each one stands for (null for the product
    // code that the template takes from the product name, when the table has no column of its own for it).
    public static IReadOnlyList<(string Label, string Description, DraftColumn? Column)> ColumnLabels(InvoiceTemplateDraft draft)
    {
        var result = new List<(string, string, DraftColumn?)>();
        var fieldKeys = FieldLabels(draft).Select(item => InvoiceValues.Normalize(item.Label)).ToHashSet();
        foreach (var column in draft.Columns.Where(column => column.Use && column.Meaning != InvoiceColumnMeanings.Ignore))
        {
            // The name of the column in the template; when a header field or another column already has it, the name with the title of its meaning.
            var title = InvoiceVocabulary.ColumnTitle(column.Meaning);
            var name = ColumnOwnLabel(column);
            var label = new[] { name, name + " (" + title + ")" }
                .FirstOrDefault(candidate => candidate.Length > 0 && !fieldKeys.Contains(InvoiceValues.Normalize(candidate))
                    && result.All(item => InvoiceValues.Normalize(item.Item1) != InvoiceValues.Normalize(candidate)));
            if (label is null) continue;
            result.Add((label, "Valoarea din coloana „" + name + "” a tabelului (se citește la importul facturii)", column));
        }
        if (draft.NameCodeSeparator.Trim().Length > 0 && result.All(item => item.Item3?.Meaning != InvoiceColumnMeanings.Code))
            result.Add((CodeLabel, "Codul produsului, luat din denumire (se citește la importul facturii)", null));
        return result;
    }

    // Every label that can be put in the description: the header fields, then the values of the table columns.
    public static IReadOnlyList<(string Label, string Description)> Labels(InvoiceTemplateDraft draft) =>
        [.. FieldLabels(draft), .. ColumnLabels(draft).Select(item => (item.Label, item.Description))];

    // The text a new template starts with: "<Valoare Cod produs> <Valoare Preț unitar> <Număr factură> din data <Data emiterii> <Furnizor — denumire>",
    // built from the labels the template really has (the title of the meaning, or the name the user gave the field); a part that the template
    // does not have is left out, so that the text never holds a mark the template does not know.
    public static string Default(InvoiceTemplateDraft draft)
    {
        string? MarkOf(string meaning) => draft.Fields.FirstOrDefault(field => field.Use && field.Meaning == meaning) is { } found ? "<" + InvoiceTemplateDraft.EffectiveLabel(found) + ">" : null;
        var columns = ColumnLabels(draft);
        var parts = new List<string>();
        foreach (var meaning in new[] { InvoiceColumnMeanings.Code, InvoiceColumnMeanings.UnitPrice })
            if (columns.FirstOrDefault(item => item.Column is null ? meaning == InvoiceColumnMeanings.Code : item.Column.Meaning == meaning) is { Label: { } label }) parts.Add("<" + label + ">");
        if (MarkOf(InvoiceFieldMeanings.InvoiceNumber) is { } number) parts.Add(number);
        if (MarkOf(InvoiceFieldMeanings.InvoiceDate) is { } date) parts.Add("din data " + date);
        if (MarkOf(InvoiceFieldMeanings.SupplierName) is { } supplier) parts.Add(supplier);
        return string.Join(" ", parts);
    }

    // ---- arithmetic: ADUNARE{<a> + <b>}, SCADERE{<a> - <b>}, INMULTIRE{<a> * <b>}, IMPARTIRE{<a> / <b>} are replaced by the result on the values of
    // the labels inside them; the symbol of the operation stands between the labels (it may be left out when typing) ----

    public const string AddOperation = "ADUNARE";

    // The operations: name, symbol put between the labels, the symbols that are accepted for it, the least and the most labels it takes.
    public sealed record ArithmeticOperation(string Name, string Symbol, string Accepted, int Min, int Max, string Title);

    public static readonly IReadOnlyList<ArithmeticOperation> Operations =
    [
        new("ADUNARE", "+", "+", 2, int.MaxValue, "Adunare"),
        new("SCADERE", "-", "-−–", 2, int.MaxValue, "Scădere (prima valoare minus celelalte)"),
        new("INMULTIRE", "*", "*×", 2, int.MaxValue, "Înmulțire"),
        new("IMPARTIRE", "/", "/÷", 2, 2, "Împărțire (exact două etichete)")
    ];

    private static readonly string OperationNames = string.Join("|", Operations.Select(item => item.Name));

    [GeneratedRegex("(ADUNARE|SCADERE|INMULTIRE|IMPARTIRE)\\{([^{}]*)\\}")]
    private static partial Regex Operation();

    private static ArithmeticOperation OperationOf(string name) => Operations.First(item => item.Name == name);

    // The result of one operation on the marks inside it; null when there are too few or too many marks, a mark has no value or its value is
    // not a number, or a division is by zero (the operation is then left as written).
    private static string? Calculate(string name, string inside, Func<string, string?> value)
    {
        var operation = OperationOf(name);
        var marks = Mark().Matches(inside);
        if (marks.Count < operation.Min || marks.Count > operation.Max) return null;
        var numbers = new List<decimal>();
        foreach (Match mark in marks)
        {
            if (value(mark.Groups[1].Value.Trim()) is not { } text || InvoiceValues.ParseNumber(text) is not { } number) return null;
            numbers.Add(number);
        }
        decimal result = numbers[0];
        try
        {
            foreach (var number in numbers.Skip(1))
            {
                switch (name)
                {
                    case "ADUNARE": result += number; break;
                    case "SCADERE": result -= number; break;
                    case "INMULTIRE": result *= number; break;
                    default:
                        if (number == 0) return null;
                        result /= number;
                        break;
                }
            }
        }
        catch (OverflowException) { return null; }
        return result.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    }

    // What is wrong with the arithmetic operations of a text (empty = acceptable): an operation that is not closed, one with too few or too many
    // labels (a division takes exactly two), or with anything but labels, spaces and its own symbol inside.
    public static IReadOnlyList<string> OperationProblems(string template)
    {
        var text = template ?? "";
        var problems = new List<string>();
        var opened = Regex.Matches(text, "(" + OperationNames + ")\\{").Count;
        var closed = Operation().Matches(text);
        if (opened != closed.Count) problems.Add("O operatie aritmetica nu este inchisa corect (lipseste „}” sau are acolade in interior).");
        foreach (Match match in closed)
        {
            var operation = OperationOf(match.Groups[1].Value);
            var inside = match.Groups[2].Value;
            var count = Mark().Matches(inside).Count;
            if (count < operation.Min) problems.Add($"Operatia {operation.Name}{{}} are nevoie de cel putin {operation.Min} etichete.");
            if (count > operation.Max) problems.Add($"Operatia {operation.Name}{{}} accepta cel mult {operation.Max} etichete.");
            if (Mark().Replace(inside, "").Any(letter => !char.IsWhiteSpace(letter) && !operation.Accepted.Contains(letter)))
                problems.Add($"Operatia {operation.Name}{{}} accepta numai etichete, spatii si simbolul „{operation.Symbol}” in interior.");
        }
        return problems.Distinct().ToList();
    }

    // The text with every operation and every mark replaced by its value (value returns null for a label it does not know: the mark is left as written).
    public static string Render(string template, Func<string, string?> value) =>
        Mark().Replace(Operation().Replace(template ?? "", match => Calculate(match.Groups[1].Value, match.Groups[2].Value, value) ?? match.Value), match => value(match.Groups[1].Value.Trim()) ?? match.Value).Trim();

    // The marks of the text that are not labels of the template.
    public static IReadOnlyList<string> UnknownMarks(string template, IEnumerable<string> labels)
    {
        var known = labels.Select(InvoiceValues.Normalize).ToHashSet();
        return Mark().Matches(template ?? "").Select(match => match.Groups[1].Value.Trim()).Where(mark => !known.Contains(InvoiceValues.Normalize(mark))).Distinct().ToList();
    }

    // The description of the stock entry of one row of an invoice, as the template says: header fields from what the template read in the file,
    // the values of the columns from the cells of the row (as edited), the product code from the code cell or the start of the name. A label the
    // row has no value for is left as written.
    public static string RenderRow(InvoiceTemplateDraft draft, IReadOnlyList<InvoiceExtractedField> fields, IReadOnlyDictionary<string, string> cells, char? decimalHint)
    {
        var columns = ColumnLabels(draft);
        string? Lookup(string label)
        {
            var key = InvoiceValues.Normalize(label);
            if (fields.FirstOrDefault(field => field.Found && InvoiceValues.Normalize(field.Name) == key) is { } found)
                return InvoiceTemplateEngine.Display(found.Value, found.Kind, decimalHint);
            if (columns.FirstOrDefault(item => InvoiceValues.Normalize(item.Label) == key) is not { Label: not null } column) return null;
            if (column.Column is null)   // the product code taken from the name
                return cells.TryGetValue("code", out var code) && code.Length > 0 ? code : null;
            return cells.TryGetValue(column.Column.Id, out var cell) && cell.Length > 0 ? cell : null;
        }
        return Render(draft.ProductDescription, Lookup);
    }

    // A readable sample for the preview: field values from what the template reads, the first table row for the values of the columns (a
    // note in ‹ › when the template has not been read yet, or the table has no such column or no row).
    public static string RenderSample(InvoiceTemplateDraft draft, InvoiceExtraction? preview)
    {
        string? Lookup(string label)
        {
            var key = InvoiceValues.Normalize(label);
            if (preview?.Fields.FirstOrDefault(field => field.Found && InvoiceValues.Normalize(field.Name) == key) is { } found) return found.Value;
            if (!ColumnLabels(draft).Any(item => InvoiceValues.Normalize(item.Label) == key)) return null;
            var note = "‹" + label.ToLowerInvariant() + "›";
            if (preview is null || preview.Rows.FirstOrDefault() is not { } row) return note;
            var cellId = ColumnLabels(draft).FirstOrDefault(item => InvoiceValues.Normalize(item.Label) == key).Column?.Id ?? "code";
            return cellId is not null && row.Cells.TryGetValue(cellId, out var cell) && cell.Length > 0 ? cell : note;
        }
        return Render(draft.ProductDescription, Lookup);
    }
}
