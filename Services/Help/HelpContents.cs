using System.Net;
using System.Text;

namespace BlazorStoc.Help;

// The table of contents of the unified help (menu entry "Ajutor" and the tile of the main page): every topic of the catalog under the area of the application it
// belongs to, so that the whole help is readable from one place (the topic of the open window stays reachable from the help icon of the top bar).
public static class HelpContents
{
    /// <summary>The areas in the order they are listed, each with the prefixes of the topic keys that belong to it; a topic that matches none goes to <see cref="Other"/>.</summary>
    public static readonly (string Title, string[] Prefixes)[] Areas =
    [
        ("Început și acces", ["acasa", "autentificare", "acces-refuzat"]),
        ("Produse și stoc", ["produse", "iesire-multipla", "export-consum"]),
        ("Inventar", ["inventar"]),
        ("Furnizori și facturi", ["furnizori", "facturi", "preluare-factura"]),
        ("Oferte", ["oferte"]),
        ("Proiecte, beneficiari și puncte de lucru", ["proiecte", "beneficiari", "puncte-de-lucru"]),
        ("Mentenanță", ["mentenanta"]),
        ("Analize de risc", ["analize-risc"]),
        ("Vehicule", ["vehicule"]),
        ("Notificări și jurnal", ["notificari", "jurnal"]),
        ("Categorii și nomenclator", ["categorii", "nomenclator"]),
        ("Setări și utilizatori", ["setari", "utilizatori"])
    ];

    public const string Other = "Alte ferestre";

    /// <summary>The area of a topic key: the key is the prefix itself or continues it after a '-' (so "produse-detaliu" is in "produse", not "proiecte").</summary>
    public static string AreaOf(string key)
    {
        foreach (var (title, prefixes) in Areas)
            if (prefixes.Any(prefix => key == prefix || key.StartsWith(prefix + "-", StringComparison.Ordinal))) return title;
        return Other;
    }

    /// <summary>The topics grouped by area, in the order of <see cref="Areas"/>; inside an area the pages (topics with an address) first, then the dialogs and editors, each by title.</summary>
    public static IReadOnlyList<(string Title, IReadOnlyList<HelpTopic> Topics)> Group(IEnumerable<HelpTopic> topics)
    {
        var byArea = topics.GroupBy(topic => AreaOf(topic.Key)).ToDictionary(group => group.Key, group => group.ToList());
        var order = Areas.Select(area => area.Title).Append(Other);
        return [.. order.Where(byArea.ContainsKey).Select(title => (title,
            (IReadOnlyList<HelpTopic>)[.. byArea[title].OrderBy(topic => topic.Routes.Count == 0 ? 1 : 0).ThenBy(topic => topic.Title, StringComparer.CurrentCultureIgnoreCase)]))];
    }

    public static string Render(IEnumerable<HelpTopic> topics)
    {
        var html = new StringBuilder("<article class=\"help-topic help-contents\"><h2 class=\"help-title\">Cuprinsul ajutorului</h2>");
        html.Append("<p>Toate ferestrele aplicației, pe secțiuni. Caută în câmpul de mai sus sau alege o fereastră din listă; „Înapoi” te readuce aici. Iconița de ajutor din bara de sus descrie direct fereastra în care ești.</p>");
        foreach (var (title, group) in Group(topics))
        {
            html.Append($"<details class=\"help-group\"><summary>{WebUtility.HtmlEncode(title)} <span class=\"count-badge\">{group.Count}</span></summary><ul class=\"help-results\">");
            foreach (var topic in group)
                html.Append($"<li><button type=\"button\" class=\"help-link\" data-help-topic=\"{WebUtility.HtmlEncode(topic.Key)}\">{WebUtility.HtmlEncode(topic.Title)}</button></li>");
            html.Append("</ul></details>");
        }
        return html.Append("</article>").ToString();
    }
}
