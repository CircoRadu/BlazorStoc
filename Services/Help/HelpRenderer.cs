using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace BlazorStoc.Help;

/// <summary>
/// The HTML of a topic, for the help window (wwwroot/help.js puts it in a native dialog). Everything from the files is encoded first, so a text cannot bring markup.
/// The flows are native details/summary sections (the "+" of the title is drawn by the CSS), so nothing here needs a Blazor circuit: the same
/// markup works in the pages rendered without interactivity, in the pop-out windows and in the pages outside Blazor.
/// </summary>
public static partial class HelpRenderer
{
    private static string E(string? text) => WebUtility.HtmlEncode(text ?? "");

    [GeneratedRegex(@"\*\*(.+?)\*\*")]
    private static partial Regex BoldRegex();

    [GeneratedRegex(@"\[\[([a-z0-9.\-]+)(?:\|([^\]]*))?\]\]")]
    private static partial Regex LinkRegex();

    /// <summary>A text of a file as HTML: **bold** and links to other topics.</summary>
    public static string Inline(string text, HelpCatalog catalog)
    {
        var html = E(text);
        html = BoldRegex().Replace(html, "<strong>$1</strong>");
        return LinkRegex().Replace(html, match =>
        {
            var key = match.Groups[1].Value;
            var label = match.Groups[2].Success && match.Groups[2].Value.Length > 0 ? match.Groups[2].Value : catalog.Get(key)?.Title is { } title ? E(title) : key;
            return $"<button type=\"button\" class=\"help-link\" data-help-topic=\"{key}\">{label}</button>";
        });
    }

    public static string Topic(HelpTopic topic, HelpCatalog catalog, IReadOnlyList<HelpTopic>? others = null)
    {
        var html = new StringBuilder();
        html.Append($"<article class=\"help-topic\" data-help-key=\"{E(topic.Key)}\"><h2 class=\"help-title\">{E(topic.Title)}</h2>");
        foreach (var block in topic.Summary) Block(html, block, catalog);

        if (others is { Count: > 0 })
        {
            html.Append("<p class=\"help-others\"><span>Alte secțiuni ale ferestrei:</span> ");
            foreach (var other in others)
                html.Append($"<button type=\"button\" class=\"help-link\" data-help-topic=\"{E(other.Key)}\">{E(other.Tab ?? other.Title)}</button> ");
            html.Append("</p>");
        }

        var flows = topic.Flows;
        html.Append($"<section class=\"help-flows\" aria-label=\"Fluxuri de lucru\"><h3>Fluxuri de lucru <span class=\"count-badge\">{flows.Count}</span></h3>");
        foreach (var flow in flows)
        {
            html.Append($"<details class=\"help-flow\"><summary><span class=\"help-plus\" aria-hidden=\"true\"></span>{E(flow.Title)}</summary><div class=\"help-flow-body\">");
            var steps = false;
            foreach (var block in flow.Blocks)
            {
                if (block.Kind == HelpBlockKind.Step) { if (!steps) { html.Append("<ol class=\"help-steps\">"); steps = true; } html.Append($"<li>{Inline(block.Text, catalog)}</li>"); continue; }
                if (steps) { html.Append("</ol>"); steps = false; }
                Block(html, block, catalog);
            }
            if (steps) html.Append("</ol>");
            html.Append("</div></details>");
        }
        html.Append("</section>");

        var count = topic.Areas.Sum(area => area.Elements.Count);
        html.Append($"<section class=\"help-elements\" aria-label=\"Elementele ferestrei\"><h3>Elementele ferestrei <span class=\"count-badge\">{count}</span></h3>");
        foreach (var area in topic.Areas)
        {
            html.Append($"<h4>{E(area.Name)}</h4><dl class=\"help-list\">");
            foreach (var element in area.Elements)
            {
                html.Append("<div class=\"help-element\"><dt>");
                if (element.Icon is { Length: > 0 } icon) html.Append($"<span class=\"row-icon {E(icon)} help-static\" aria-hidden=\"true\"></span>");
                html.Append($"<span class=\"help-name\">{E(element.Name)}</span><small class=\"help-kind\">{E(element.Kind)}</small></dt><dd>{Inline(element.Text, catalog)}</dd></div>");
            }
            html.Append("</dl>");
        }
        html.Append("</section></article>");
        return html.ToString();
    }

    private static void Block(StringBuilder html, HelpBlock block, HelpCatalog catalog)
    {
        switch (block.Kind)
        {
            case HelpBlockKind.Image:
                var legend = block.Caption is { Length: > 0 } ? $"<figcaption>{E(block.Caption)}</figcaption>" : "";
                html.Append($"<figure class=\"help-figure\"><img src=\"/ajutor/imagine/{Uri.EscapeDataString(block.Text)}\" alt=\"{E(block.Caption ?? block.Text)}\" loading=\"lazy\" />{legend}</figure>");
                break;
            case HelpBlockKind.Note:
                html.Append($"<p class=\"help-note\"><strong>Notă:</strong> {Inline(block.Text, catalog)}</p>");
                break;
            default:
                html.Append($"<p>{Inline(block.Text, catalog)}</p>");
                break;
        }
    }

    /// <summary>The window of a page or dialog that has no topic (yet): said plainly, the rest of the help stays usable.</summary>
    public static string Missing(string where) =>
        $"<article class=\"help-topic\"><h2 class=\"help-title\">Ajutor indisponibil</h2><p>Pentru această fereastră nu există încă o descriere. Reîncearcă după următoarea actualizare a aplicației.</p><p class=\"help-note\"><small>{E(where)}</small></p></article>";

    public static string Results(IReadOnlyList<HelpTopic> found, string query)
    {
        if (found.Count == 0) return $"<p class=\"help-empty\">Nu am găsit nimic pentru „{E(query)}”.</p>";
        var html = new StringBuilder($"<ul class=\"help-results\" aria-label=\"Rezultatele căutării\">");
        foreach (var topic in found)
        {
            var snippet = topic.Summary.FirstOrDefault(block => block.Kind == HelpBlockKind.Paragraph)?.Text ?? "";
            snippet = BoldRegex().Replace(snippet, "$1");
            snippet = LinkRegex().Replace(snippet, match => match.Groups[2].Success ? match.Groups[2].Value : match.Groups[1].Value);
            if (snippet.Length > 140) snippet = snippet[..140].TrimEnd() + "…";
            html.Append($"<li><button type=\"button\" class=\"help-link\" data-help-topic=\"{E(topic.Key)}\">{E(topic.Title)}</button><small>{E(snippet)}</small></li>");
        }
        return html.Append("</ul>").ToString();
    }
}
