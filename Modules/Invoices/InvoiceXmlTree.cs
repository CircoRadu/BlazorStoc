using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace BlazorStoc.Services;

// One element (or attribute) of the example XML as the visual linking shows it. Path is written from the root element; LinePath is relative to the first
// invoice line (null outside the lines): the value paths of a line are relative to a line element.
public sealed record InvoiceXmlTreeNode(XObject Source, string Name, string Value, bool IsAttribute, string Path, string? LinePath, bool InLine,
    IReadOnlyList<InvoiceXmlTreeNode> Children, int SimilarSiblings);

// The template elements an XML file can be linked to, in the order shown to the user.
public sealed record InvoiceXmlTarget(string Id, string Label, bool Required, bool IsLine, bool IsContainer, string Help);

public static class InvoiceXmlTree
{
    public const int MaxNodes = 800;

    public static readonly IReadOnlyList<InvoiceXmlTarget> Targets =
    [
        new("number", "Numărul facturii", true, false, false, ""),
        new("date", "Data facturii", true, false, false, ""),
        new("supplierName", "Furnizorul (denumire)", false, false, false, ""),
        new("supplierCui", "CUI-ul furnizorului", false, false, false, ""),
        new("currency", "Moneda", false, false, false, ""),
        new("total", "Totalul facturii", false, false, false, ""),
        new("lines", "O linie de factură", true, true, true, "Alege elementul care se repetă pentru fiecare produs al facturii."),
        new("code", "Codul intern al furnizorului", false, true, false, "Codul pe care furnizorul îl dă produsului; poate fi diferit de codul de produs (de exemplu la GS), care poate fi scris în denumire."),
        new("name", "Denumirea produsului", false, true, false, ""),
        new("quantity", "Cantitatea", true, true, false, ""),
        new("unit", "Unitatea de măsură", false, true, false, ""),
        new("unitPrice", "Prețul unitar", false, true, false, ""),
        new("value", "Valoarea liniei", false, true, false, "")
    ];

    public static string PathOf(InvoiceXmlMapping mapping, string id) => id switch
    {
        "number" => mapping.Number, "date" => mapping.Date, "supplierName" => mapping.SupplierName, "supplierCui" => mapping.SupplierCui, "currency" => mapping.Currency,
        "total" => mapping.Total, "lines" => mapping.Lines, "code" => mapping.Code, "name" => mapping.Name, "quantity" => mapping.Quantity, "unit" => mapping.Unit,
        "unitPrice" => mapping.UnitPrice, _ => mapping.Value
    };

    public static InvoiceXmlMapping With(InvoiceXmlMapping mapping, string id, string path) => id switch
    {
        "number" => mapping with { Number = path }, "date" => mapping with { Date = path }, "supplierName" => mapping with { SupplierName = path },
        "supplierCui" => mapping with { SupplierCui = path }, "currency" => mapping with { Currency = path }, "total" => mapping with { Total = path },
        "lines" => mapping with { Lines = path }, "code" => mapping with { Code = path }, "name" => mapping with { Name = path },
        "quantity" => mapping with { Quantity = path }, "unit" => mapping with { Unit = path }, "unitPrice" => mapping with { UnitPrice = path },
        _ => mapping with { Value = path }
    };

    // The tree of the example file, with the first line element as the base of the line paths. Elements that repeat (the other lines, the other taxes) are shown once
    // and counted, so that the picture of one invoice stays small.
    public static InvoiceXmlTreeNode? Build(XDocument document, InvoiceXmlMapping mapping)
    {
        var root = document.Root;
        if (root is null) return null;
        var line = InvoiceXmlReader.LineElements(document, mapping.Lines).FirstOrDefault();
        var budget = MaxNodes;
        return Make(root, root, line, null, false, ref budget, 1);
    }

    private static InvoiceXmlTreeNode Make(XElement element, XElement root, XElement? line, string? parentPath, bool parentInLine, ref int budget, int similar)
    {
        budget--;
        var isRoot = parentPath is null;
        var path = isRoot ? "" : (parentPath.Length == 0 ? "" : parentPath + "/") + element.Name.LocalName;
        var inLine = parentInLine || (line is not null && element == line);
        var children = new List<InvoiceXmlTreeNode>();
        foreach (var attribute in element.Attributes().Where(item => !item.IsNamespaceDeclaration))
        {
            if (budget <= 0) break;
            budget--;
            children.Add(new InvoiceXmlTreeNode(attribute, "@" + attribute.Name.LocalName, attribute.Value.Trim(), true,
                (path.Length == 0 ? "" : path + "/") + "@" + attribute.Name.LocalName, LinePathOf(attribute, line), inLine, [], 1));
        }
        foreach (var group in element.Elements().GroupBy(child => child.Name.LocalName))
        {
            var all = group.ToList();
            // Of repeated elements the first is shown; the line elements are the exception: the one that is the base of the paths comes first.
            var first = line is not null && all.Contains(line) ? line : all[0];
            if (budget <= 0) break;
            children.Add(Make(first, root, line, path, inLine, ref budget, all.Count));
        }
        var text = element.HasElements ? "" : Regex.Replace(element.Value, @"\s+", " ").Trim();
        return new InvoiceXmlTreeNode(element, element.Name.LocalName, text, false, path, LinePathOf(element, line), inLine, children, similar - 1);
    }

    // The path of a node relative to the line element (null when the node is not inside it).
    private static string? LinePathOf(XObject node, XElement? line)
    {
        if (line is null) return null;
        var names = new List<string>();
        XObject? current = node;
        if (current is XAttribute attribute) { names.Add("@" + attribute.Name.LocalName); current = attribute.Parent; }
        while (current is XElement element && element != line)
        {
            names.Add(element.Name.LocalName);
            current = element.Parent;
        }
        if (current != line) return null;
        names.Reverse();
        return names.Count == 0 ? "." : string.Join("/", names);
    }

    // A new link replaces the path; when the chosen path was already one of the alternatives it only goes first and the others stay.
    public static string Link(string? current, string chosen)
    {
        var alternatives = InvoiceXmlRules.Alternatives(current).ToList();
        if (!alternatives.Contains(chosen, StringComparer.Ordinal)) return chosen;
        alternatives.Remove(chosen);
        alternatives.Insert(0, chosen);
        return string.Join(" | ", alternatives);
    }

    // The node a target of the mapping reads from in the example file (null when its paths find nothing).
    public static IReadOnlyDictionary<string, XObject> Linked(XDocument document, InvoiceXmlMapping mapping)
    {
        var result = new Dictionary<string, XObject>();
        var root = document.Root;
        if (root is null) return result;
        var line = InvoiceXmlReader.LineElements(document, mapping.Lines).FirstOrDefault();
        foreach (var target in Targets)
        {
            if (target.IsContainer) { if (line is not null) result[target.Id] = line; continue; }
            var context = target.IsLine ? line : root;
            if (context is null) continue;
            if (InvoiceXmlReader.ResolveNode(context, root, PathOf(mapping, target.Id)) is { } node) result[target.Id] = node;
        }
        return result;
    }
}
