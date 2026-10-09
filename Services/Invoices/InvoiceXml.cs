using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace BlazorStoc.Services;

// An XML invoice (for example the electronic invoice, UBL) is read directly, without OCR, with a mapping that says where each value is. A path is
// a chain of element names separated by "/" (the prefix of a name, as in cbc:ID, is ignored), written from the root element (its own name may be left out);
// "//Name" looks for Name at any depth, "*" stands for any element, "@attribute" at the end takes an attribute, and "a | b" takes the first of the paths
// that has a value. The paths of the line values are relative to a line element.
public sealed record InvoiceXmlMapping(string Number, string Date, string SupplierName, string SupplierCui, string Currency, string Total,
    string Lines, string Code, string Name, string Quantity, string Unit, string UnitPrice, string Value)
{
    // The standard UBL 2.1 invoice (also the Romanian e-Factura): the template proposed when the user makes one and the reading used when none is saved.
    public static readonly InvoiceXmlMapping Ubl = new(
        Number: "ID",
        Date: "IssueDate",
        SupplierName: "AccountingSupplierParty/Party/PartyLegalEntity/RegistrationName | AccountingSupplierParty/Party/PartyName/Name",
        SupplierCui: "AccountingSupplierParty/Party/PartyTaxScheme/CompanyID | AccountingSupplierParty/Party/PartyLegalEntity/CompanyID",
        Currency: "DocumentCurrencyCode",
        Total: "LegalMonetaryTotal/PayableAmount",
        Lines: "InvoiceLine | CreditNoteLine",
        Code: "Item/SellersItemIdentification/ID | Item/StandardItemIdentification/ID",
        Name: "Item/Name | Item/Description",
        Quantity: "InvoicedQuantity | CreditedQuantity",
        Unit: "InvoicedQuantity/@unitCode | CreditedQuantity/@unitCode",
        UnitPrice: "Price/PriceAmount",
        Value: "LineExtensionAmount");
}

public static class InvoiceXmlRules
{
    public const long MaxFileSizeBytes = 5 * 1024 * 1024;
    public const int MaxLines = 2000;
    public const string NotXmlMessage = "Fișierul trebuie să fie un XML (.xml).";
    public const string TooLargeMessage = "Fișierul XML depășește dimensiunea maximă acceptată (5 MB).";
    public const string InvalidMessage = "Fișierul nu este un XML valid și nu poate fi citit.";
    public const string NotInvoiceMessage = "Fișierul XML nu pare o factură: nu s-a găsit niciun element cu număr, dată sau linii după șablon.";
    public const string InvalidZipMessage = "Arhiva ZIP nu poate fi citită.";
    public const string NoInvoiceInZipMessage = "Arhiva ZIP nu conține o factură XML (e-Factura UBL): fișierul cu semnătura nu poate fi citit ca factură.";
    public const int MaxZipEntries = 50;
    public const string NoLinesMessage = "Șablonul XML nu a găsit nicio linie de factură în fișier. Verifică calea liniilor sau alege alt șablon.";
    public const string MappingRequiredMessage = "Șablonul XML trebuie să aibă cel puțin calea numărului facturii, a datei, a liniilor, a denumirii sau codului produsului și a cantității.";

    // A path that cannot be followed is told at once, not when a file is read with it.
    private static readonly Regex Step = new(@"^(\.|(?:[A-Za-z_][\w.\-]*:)?(?:[A-Za-z_][\w.\-]*|\*)|@(?:[A-Za-z_][\w.\-]*:)?[A-Za-z_][\w.\-]*)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IEnumerable<string> Alternatives(string? path) =>
        (path ?? "").Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    public static string? PathError(string label, string? path)
    {
        foreach (var alternative in Alternatives(path))
        {
            var steps = alternative.TrimStart('/').Split('/', StringSplitOptions.TrimEntries);
            for (var index = 0; index < steps.Length; index++)
            {
                var step = steps[index];
                if (step.Length == 0 || !Step.IsMatch(step)) return $"Calea „{alternative}” ({label}) nu este validă: folosește nume de elemente separate prin „/”, eventual „@atribut” la sfârșit.";
                if (step.StartsWith('@') && index != steps.Length - 1) return $"Calea „{alternative}” ({label}): atributul poate fi doar ultimul element al căii.";
            }
        }
        return null;
    }

    // The message for the first thing wrong with the mapping, or null when it can be saved.
    public static string? Validate(InvoiceXmlMapping? mapping)
    {
        if (mapping is null) return MappingRequiredMessage;
        if (!Alternatives(mapping.Number).Any() || !Alternatives(mapping.Date).Any() || !Alternatives(mapping.Lines).Any()
            || !Alternatives(mapping.Quantity).Any() || (!Alternatives(mapping.Name).Any() && !Alternatives(mapping.Code).Any()))
            return MappingRequiredMessage;
        foreach (var (label, path) in Paths(mapping))
            if (PathError(label, path) is { } error) return error;
        return null;
    }

    public static IEnumerable<(string Label, string Path)> Paths(InvoiceXmlMapping mapping)
    {
        yield return ("numărul facturii", mapping.Number);
        yield return ("data", mapping.Date);
        yield return ("furnizorul", mapping.SupplierName);
        yield return ("CUI-ul furnizorului", mapping.SupplierCui);
        yield return ("moneda", mapping.Currency);
        yield return ("totalul", mapping.Total);
        yield return ("liniile", mapping.Lines);
        yield return ("codul produsului", mapping.Code);
        yield return ("denumirea", mapping.Name);
        yield return ("cantitatea", mapping.Quantity);
        yield return ("unitatea de măsură", mapping.Unit);
        yield return ("prețul unitar", mapping.UnitPrice);
        yield return ("valoarea liniei", mapping.Value);
    }

    public static InvoiceXmlMapping Clean(InvoiceXmlMapping mapping)
    {
        static string One(string? path) => string.Join(" | ", Alternatives(path));
        return new InvoiceXmlMapping(One(mapping.Number), One(mapping.Date), One(mapping.SupplierName), One(mapping.SupplierCui), One(mapping.Currency), One(mapping.Total),
            One(mapping.Lines), One(mapping.Code), One(mapping.Name), One(mapping.Quantity), One(mapping.Unit), One(mapping.UnitPrice), One(mapping.Value));
    }
}

public static class InvoiceXmlReader
{
    // Ids of the columns the reading gives (the same as the meanings, so that the pickup treats them as the columns of a template).
    private static readonly (string Id, string Meaning, string Label)[] ColumnDefinitions =
    [
        ("code", InvoiceColumnMeanings.Code, "Cod"), ("name", InvoiceColumnMeanings.Name, "Denumire"), ("quantity", InvoiceColumnMeanings.Quantity, "Cantitate"),
        ("unit", InvoiceColumnMeanings.Unit, "UM"), ("unitPrice", InvoiceColumnMeanings.UnitPrice, "Preț unitar"), ("value", InvoiceColumnMeanings.Value, "Valoare")
    ];

    // A file from e-Factura may come as a ZIP with the invoice and its signature (both XML): the entry whose root is an invoice (or a credit note) is the one
    // that is read. Nothing is extracted to disk and every entry is read up to the size limit only. A file that is not a ZIP is returned as it is.
    public static (byte[] Xml, string Name) Unpack(byte[] content, string fileName)
    {
        var isZip = content.Length >= 4 && content[0] == 'P' && content[1] == 'K' && content[2] is 3 or 5 or 7;
        if (!isZip) return (content, fileName);
        try
        {
            using var archive = new System.IO.Compression.ZipArchive(new MemoryStream(content), System.IO.Compression.ZipArchiveMode.Read);
            foreach (var entry in archive.Entries.Take(InvoiceXmlRules.MaxZipEntries))
            {
                var name = Path.GetFileName(entry.FullName);
                if (name.Length == 0 || !name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) || entry.Length > InvoiceXmlRules.MaxFileSizeBytes) continue;
                using var memory = new MemoryStream();
                using (var stream = entry.Open())
                {
                    var buffer = new byte[81920];
                    int read;
                    while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        memory.Write(buffer, 0, read);
                        if (memory.Length > InvoiceXmlRules.MaxFileSizeBytes) break;
                    }
                }
                if (memory.Length > InvoiceXmlRules.MaxFileSizeBytes) continue;
                var bytes = memory.ToArray();
                try
                {
                    if (Parse(bytes).Root?.Name.LocalName is "Invoice" or "CreditNote") return (bytes, name);
                }
                catch (InvoiceAnalysisException) { /* the signature or another file: the next entry */ }
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or NotSupportedException or IOException)
        {
            throw new InvoiceAnalysisException(InvoiceXmlRules.InvalidZipMessage);
        }
        throw new InvoiceAnalysisException(InvoiceXmlRules.NoInvoiceInZipMessage);
    }

    public static XDocument Parse(byte[] content)
    {
        if (content.Length > InvoiceXmlRules.MaxFileSizeBytes) throw new InvoiceAnalysisException(InvoiceXmlRules.TooLargeMessage);
        // No DTD and no external resource: a file under pickup is data, never a request for the server to read anything.
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersFromEntities = 0, MaxCharactersInDocument = InvoiceXmlRules.MaxFileSizeBytes * 2 };
        try
        {
            using var stream = new MemoryStream(content);
            using var reader = XmlReader.Create(stream, settings);
            return XDocument.Load(reader);
        }
        catch (Exception exception) when (exception is XmlException or InvalidOperationException or ArgumentException)
        {
            throw new InvoiceAnalysisException(InvoiceXmlRules.InvalidMessage);
        }
    }

    // The supplier named by the file, read with the standard UBL paths (used to find the template that belongs to it).
    public static (string Name, string Cui) Supplier(XDocument document)
    {
        var root = document.Root;
        if (root is null) return ("", "");
        return (First(root, root, InvoiceXmlMapping.Ubl.SupplierName), InvoiceValues.NormalizeCui(First(root, root, InvoiceXmlMapping.Ubl.SupplierCui)));
    }

    // Reads the file with the mapping: the header fields (as a template reads them), the columns and the lines. A file without lines gives no rows and a warning.
    public static InvoiceExtraction Read(XDocument document, InvoiceXmlMapping mapping)
    {
        var root = document.Root ?? throw new InvoiceAnalysisException(InvoiceXmlRules.InvalidMessage);
        var fields = new List<InvoiceExtractedField>();
        void Field(string id, string meaning, string name, string value, string kind) => fields.Add(new InvoiceExtractedField(id, meaning, name, value, kind, value.Length > 0));
        var number = First(root, root, mapping.Number);
        var date = DisplayDate(First(root, root, mapping.Date));
        Field("number", InvoiceFieldMeanings.InvoiceNumber, "Număr factură", number, "text");
        Field("date", InvoiceFieldMeanings.InvoiceDate, "Data facturii", date, "date");
        Field("supplierName", InvoiceFieldMeanings.SupplierName, "Furnizor", First(root, root, mapping.SupplierName), "text");
        Field("supplierCui", InvoiceFieldMeanings.SupplierCui, "CUI furnizor", InvoiceValues.CleanCui(First(root, root, mapping.SupplierCui)), "text");
        Field("currency", InvoiceFieldMeanings.Currency, "Moneda", First(root, root, mapping.Currency), "text");
        Field("total", InvoiceFieldMeanings.Total, "Total factură", First(root, root, mapping.Total), "number");

        var used = ColumnDefinitions.Where(column => Has(mapping, column.Id)).ToList();
        var columns = used.Select(column => new InvoiceColumn(column.Id, column.Label, column.Meaning, 0, 0)).ToList();
        var rows = new List<InvoiceTableRow>();
        var warnings = new List<string>();
        var lines = Select(root, root, mapping.Lines).Take(InvoiceXmlRules.MaxLines + 1).ToList();
        if (lines.Count > InvoiceXmlRules.MaxLines)
        {
            warnings.Add($"Fișierul are peste {InvoiceXmlRules.MaxLines} de linii: se citesc doar primele.");
            lines = lines.Take(InvoiceXmlRules.MaxLines).ToList();
        }
        foreach (var line in lines)
        {
            var cells = new Dictionary<string, string>();
            foreach (var column in used) cells[column.Id] = CleanValue(column.Id, First(line, root, PathOf(mapping, column.Id)));
            if (cells.Values.All(value => value.Length == 0)) continue;
            var flags = new List<string>();
            if (columns.Any(column => column.Meaning == InvoiceColumnMeanings.Quantity) && cells.GetValueOrDefault("quantity", "").Length == 0) flags.Add("fără cantitate");
            if (cells.GetValueOrDefault("name", "").Length == 0 && cells.GetValueOrDefault("code", "").Length == 0) flags.Add("fără denumire și cod");
            rows.Add(new InvoiceTableRow(1, rows.Count + 1, cells, flags));
        }
        if (rows.Count == 0) warnings.Add(InvoiceXmlRules.NoLinesMessage);
        if (number.Length == 0 && date.Length == 0 && rows.Count == 0) warnings.Insert(0, InvoiceXmlRules.NotInvoiceMessage);
        return new InvoiceExtraction(fields, columns, rows, warnings);
    }

    // The line elements the path of the lines reaches (the base of the paths of the line values).
    public static IReadOnlyList<XElement> LineElements(XDocument document, string? path) =>
        document.Root is { } root ? [.. Select(root, root, path)] : [];

    // The node (element or attribute) that gives the value for a path: the first node with a value among the alternatives.
    public static XObject? ResolveNode(XElement context, XElement root, string? path)
    {
        foreach (var alternative in InvoiceXmlRules.Alternatives(path))
            foreach (var node in Walk(context, root, alternative))
            {
                var text = node switch { XAttribute attribute => attribute.Value, XElement element => element.Value, _ => "" };
                if (text.Trim().Length > 0) return node;
            }
        return null;
    }

    private static bool Has(InvoiceXmlMapping mapping, string id) => InvoiceXmlRules.Alternatives(PathOf(mapping, id)).Any();

    private static string PathOf(InvoiceXmlMapping mapping, string id) => id switch
    {
        "code" => mapping.Code, "name" => mapping.Name, "quantity" => mapping.Quantity, "unit" => mapping.Unit, "unitPrice" => mapping.UnitPrice, _ => mapping.Value
    };

    private static string CleanValue(string columnId, string value) => columnId switch
    {
        "unit" => InvoiceUnitCodes.Display(value),
        "quantity" => TrimZeros(value, 0),
        "unitPrice" or "value" => TrimZeros(value, 2),
        _ => value
    };

    // UBL writes numbers with a point and a fixed number of decimals ("10.000000" is ten): shown without the zeros that say nothing (at least minDecimals decimals are kept).
    private static string TrimZeros(string value, int minDecimals)
    {
        if (!decimal.TryParse(value.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var number)) return value;
        var text = number.ToString("0.############################", CultureInfo.InvariantCulture);
        var decimals = text.Contains('.') ? text.Length - text.IndexOf('.') - 1 : 0;
        return decimals >= minDecimals ? text : number.ToString("F" + minDecimals, CultureInfo.InvariantCulture);
    }

    // 2026-10-05 -> 05.10.2026 (the form the application shows and reads); anything else is left as the file has it.
    private static string DisplayDate(string value) =>
        DateOnly.TryParseExact(value.Length >= 10 ? value[..10] : value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? StockMovementRules.DisplayDate(date) : value;

    // The first value that is not empty among the alternative paths.
    private static string First(XElement context, XElement root, string? path)
    {
        foreach (var alternative in InvoiceXmlRules.Alternatives(path))
            foreach (var found in Values(context, root, alternative))
                if (found.Length > 0) return found;
        return "";
    }

    private static IEnumerable<XElement> Select(XElement context, XElement root, string? path)
    {
        var seen = new HashSet<XElement>();
        foreach (var alternative in InvoiceXmlRules.Alternatives(path))
            foreach (var element in Walk(context, root, alternative).OfType<XElement>())
                if (seen.Add(element)) yield return element;
    }

    private static IEnumerable<string> Values(XElement context, XElement root, string path)
    {
        foreach (var node in Walk(context, root, path))
        {
            var text = node switch { XAttribute attribute => attribute.Value, XElement element => element.Value, _ => "" };
            yield return Regex.Replace(text, @"\s+", " ").Trim();
        }
    }

    private static string Local(string step) => step.Contains(':') ? step[(step.IndexOf(':') + 1)..] : step;

    // The nodes (elements, or attributes for the last step) a path reaches from the context element.
    private static IEnumerable<XObject> Walk(XElement context, XElement root, string path)
    {
        var steps = path.Split('/', StringSplitOptions.TrimEntries).ToList();
        IEnumerable<XElement> current;
        if (path.StartsWith("//", StringComparison.Ordinal))
        {
            steps = path.TrimStart('/').Split('/', StringSplitOptions.TrimEntries).ToList();
            var first = Local(steps[0]);
            current = context.DescendantsAndSelf().Where(element => first == "*" || element.Name.LocalName == first);
            steps.RemoveAt(0);
        }
        else
        {
            if (path.StartsWith('/')) steps.RemoveAll(step => step.Length == 0);
            current = [context];
            // The name of the root element may start a path written from the root ("Invoice/ID").
            if (context == root && steps.Count > 0 && !steps[0].StartsWith('@') && Local(steps[0]) == root.Name.LocalName) steps.RemoveAt(0);
        }
        for (var index = 0; index < steps.Count; index++)
        {
            var step = steps[index];
            if (step.StartsWith('@'))
            {
                var name = Local(step[1..]);
                return current.Select(element => element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == name)).OfType<XAttribute>();
            }
            if (step == ".") continue;
            var local = Local(step);
            current = current.SelectMany(element => element.Elements().Where(child => local == "*" || child.Name.LocalName == local)).ToList();
        }
        return current;
    }
}
