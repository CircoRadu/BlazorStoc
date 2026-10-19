using System.Reflection;
using System.Text.RegularExpressions;
using BlazorStoc.Help;
using Bunit;
using Microsoft.AspNetCore.Components;

namespace BlazorStoc.Checks;

// The in-application help (docs/PROPUNERE_HELP.md): the parser, the catalog (routes, tabs, search), the HTML, and the COVERAGE: a page that has an
// address (@page) must have a topic of its window, or be listed here with the reason. The list of pages still to be written (Pending) only shrinks:
// a page that already has a topic must be taken out of it, and a new page that is in neither list fails the check.
public static class HelpChecks
{
    // Pages that have no window of their own to describe: redirects, the error page and the waiting page of a backup.
    private static readonly HashSet<string> Exempt = new(StringComparer.OrdinalIgnoreCase)
    {
        "/Error", "/intretinere", "/miscari/{}", "/observatii/{}"
    };

    // Windows whose topic is still to be written. Empty now: every page has its topic. A new page may be listed here only for the time between adding it and writing
    // its topic (the check below then asks to take it out as soon as the topic exists).
    private static readonly HashSet<string> Pending = new(StringComparer.OrdinalIgnoreCase)
    {
    };

    private const string Sample = """
        # Fereastra de probă
        Ruta: /proba/{id:int}
        Tab: Prima
        Cautare: cuvinte extra

        Rezumat cu **bold** și <script>alert(1)</script> și legătură [[proba-alta]].

        ## Elemente
        ### Zona unu
        - Câmp | câmp | Descriere
          pe două linii.
        - Editare | iconita:edit | Deschide editorul.

        ## Flux: Cum fac ceva
        1. Primul pas
        2. Al doilea pas
        Nota: atenție.
        """;

    private const string Other = """
        # Altă fereastră
        Ruta: /proba/{id:int}

        Altceva.

        ## Elemente
        ### Z
        - A | câmp | b

        ## Flux: F
        1. a
        2. b
        """;

    public static Task RunAsync(Action<bool, string> check)
    {
        Parser(check);
        Catalog(check);
        Rendering(check);
        Coverage(check);
        ReferencedTopics(check);
        Shots(check);
        ShippedShots(check);
        return Task.CompletedTask;
    }

    private static void Parser(Action<bool, string> check)
    {
        var parsed = HelpParser.Parse("proba-tab", Sample);
        var topic = parsed.Topic;
        check(topic is not null && parsed.Errors.Count == 0, "Help: a complete topic is read without errors" + (parsed.Errors.Count > 0 ? " (" + string.Join("; ", parsed.Errors) + ")" : ""));
        if (topic is null) return;
        check(topic.Title == "Fereastra de probă" && topic.Routes.SequenceEqual(["/proba/{id:int}"]) && topic.Tab == "Prima" && topic.Keywords == "cuvinte extra",
            "Help: title, route, tab and keywords of the header are read");
        check(topic.Areas.Count == 1 && topic.Areas[0].Elements.Count == 2 && topic.Areas[0].Elements[0].Text == "Descriere pe două linii.",
            "Help: an element continues on the next indented line");
        check(topic.Areas[0].Elements[1].Icon == "edit" && topic.Areas[0].Elements[1].Kind == "iconiță", "Help: \"iconita:edit\" is an icon element");
        check(topic.Flows.Count == 1 && topic.Flows[0].Blocks.Count(block => block.Kind == HelpBlockKind.Step) == 2 && topic.Flows[0].Blocks.Any(block => block.Kind == HelpBlockKind.Note),
            "Help: a flow has numbered steps and a note");

        var noFlow = HelpParser.Parse("fara-flux", "# T\n\nRezumat.\n\n## Elemente\n### Z\n- A | b | c\n");
        check(noFlow.Topic is null && noFlow.Errors.Any(error => error.Contains("flux")), "Help: a topic without a work flow is refused");
        var badElement = HelpParser.Parse("element-prost", "# T\n\nRezumat.\n\n## Elemente\n### Z\n- doar nume\n\n## Flux: F\n1. a\n2. b\n");
        check(badElement.Errors.Any(error => error.Contains("Nume | tip | descriere")), "Help: an element without kind and description is refused");
        var shortFlow = HelpParser.Parse("flux-scurt", "# T\n\nRezumat.\n\n## Elemente\n### Z\n- A | b | c\n\n## Flux: F\n1. doar unul\n");
        check(shortFlow.Errors.Any(error => error.Contains("mai putin de 2 pasi")), "Help: a flow needs at least two steps");
        check(!HelpParser.Parse("Cheie Proasta", Sample).Errors.Count.Equals(0), "Help: a key with capitals or spaces is refused");
    }

    private static void Catalog(Action<bool, string> check)
    {
        var catalog = HelpCatalog.FromSources([("proba-tab", Sample), ("proba-alta", Other), ("proba-dublura", Other)]);
        check(catalog.Errors.Any(error => error.Contains("este deja descrisa")), "Help: two topics for the same address and tab are refused");

        var good = HelpCatalog.FromSources([("proba-tab", Sample.Replace("Tab: Prima", "Tab: Prima (2)")), ("proba-alta", Other)]);
        check(good.Errors.Count == 0, "Help: a valid set of topics has no errors" + (good.Errors.Count > 0 ? " (" + string.Join("; ", good.Errors) + ")" : ""));
        check(good.Resolve("/proba/7").Topic?.Key == "proba-alta", "Help: the page without tab gets its general topic");
        check(good.Resolve("/Proba/7/?x=1", ["Prima (3)"]).Topic?.Key == "proba-tab", "Help: the topic of the selected tab is chosen (address and label compared without case, query and number of the label)");
        check(good.Resolve("/proba/7", ["Alta"]).Topic?.Key == "proba-alta" && good.Resolve("/proba/7", ["Prima"]).Others.Single().Key == "proba-alta",
            "Help: an unknown tab falls back to the general topic; the other topics of the page are offered as links");
        check(good.Resolve("/proba/7", ["Prima 12"]).Topic?.Key == "proba-tab", "Help: the count that follows a tab label (\"Prima 12\", \"Prima (3)\") is not part of its name");
        check(good.Resolve("/proba/abc").Topic is null && good.Resolve("/nimic").Topic is null, "Help: a route parameter {id:int} does not accept text; an unknown address has no topic");
        check(good.Search("legatura").Count == 1 && good.Search("fereastra proba").Count == 1 && good.Search("zzz").Count == 0, "Help: the search needs every word");

        var broken = HelpCatalog.FromSources([("proba-tab", Sample)]);
        check(broken.Errors.Any(error => error.Contains("[[proba-alta]]")), "Help: a link to a topic that does not exist is an error");
        var picture = HelpCatalog.FromSources([("p", "# P\nRuta: /p\n\nRezumat.\n[img:lipsa.png | legenda]\n\n## Elemente\n### Z\n- A | b | c\n\n## Flux: F\n1. a\n2. b\n")]);
        check(picture.Errors.Any(error => error.Contains("lipsa.png")), "Help: a picture that does not exist is an error");
        check(HelpCatalog.Fold("Ieșire Țară ŞtîrbA") == "iesire tara stirba", "Help: the search ignores diacritics and case");
    }

    private static void Rendering(Action<bool, string> check)
    {
        var catalog = HelpCatalog.FromSources([("proba-tab", Sample), ("proba-alta", Other)]);
        var html = HelpRenderer.Topic(catalog.Get("proba-tab")!, catalog, [catalog.Get("proba-alta")!]);
        check(!html.Contains("<script", StringComparison.OrdinalIgnoreCase) && html.Contains("&lt;script&gt;"), "Help: the text of a file cannot bring markup (it is encoded)");
        check(html.Contains("<strong>bold</strong>") && html.Contains("data-help-topic=\"proba-alta\""), "Help: **bold** and [[links]] are rendered");
        check(html.Contains("<details class=\"help-flow\"") && html.Contains("help-plus") && html.Contains("Cum fac ceva"), "Help: a flow is a collapsible section with the \"+\" icon");
        check(html.Contains("row-icon edit help-static") && html.Contains("Elementele ferestrei"), "Help: an icon element shows the real icon");
        check(HelpRenderer.Missing("/x").Contains("Ajutor indisponibil"), "Help: a window without topic says so plainly");

        using var context = new Bunit.BunitContext();
        var button = context.Render<BlazorStoc.Components.Shared.HelpButton>(parameters => parameters.Add(p => p.Topic, "produse-istoric-modificari").Add(p => p.About, "Istoricul modific0103rilor")).Find("button.help-icon");
        check(button.GetAttribute("data-help-topic") == "produse-istoric-modificari" && button.GetAttribute("aria-label") == "Ajutor: Istoricul modific0103rilor" && button.GetAttribute("type") == "button",
            "Help: the help button of a dialog carries the key of its topic and a label with the name of the window");
    }

    // The screenshots (Captura:, the table of elements, the map of the places, the frame of a chosen row).
    private const string WithShot = """
        # Cu captură
        Ruta: /cu
        Captura: pagina | /cu | click Salvează; wait 300 | Pagina cu date
        Captura: alta | /cu | | A doua

        Rezumat.

        ## Elemente
        ### Zona
        - Salvează | buton | Salvează. | sel: text:"Salvează" | img: alta
        - Caută | câmp | Caută.
        - Nu e pe poză | câmp | Lipsește de pe captură.
        - Editare | iconita:edit | Edit.

        ## Flux: F
        1. a
        2. b
        """;

    private static void Shots(Action<bool, string> check)
    {
        var parsed = HelpParser.Parse("cu-captura", WithShot);
        var topic = parsed.Topic;
        check(topic is not null && parsed.Errors.Count == 0, "Help: a topic with captures is read" + (parsed.Errors.Count > 0 ? " (" + string.Join("; ", parsed.Errors) + ")" : ""));
        if (topic is null) return;
        check(topic.Captures.Count == 2 && topic.Captures[0].Id == "pagina" && topic.Captures[0].Url == "/cu" && topic.Captures[0].Steps.SequenceEqual(["click Salvează", "wait 300"]) && topic.Captures[0].Caption == "Pagina cu date",
            "Help: \"Captura: id | /adresa | pasi | legenda\" is read (steps separated by \";\")");
        check(topic.AllElements[0].Selector == "text:\"Salvează\"" && topic.AllElements[0].ImageId == "alta" && topic.AllElements[0].Text == "Salvează." && topic.AllElements[1].Selector is null,
            "Help: an element can say where it is (sel:) and on which capture (img:) without changing its description");
        check(HelpParser.Parse("fara-adresa", WithShot.Replace("| /cu |", "| cu |")).Errors.Any(error => error.Contains("Captura se scrie")), "Help: a capture without an address that starts with \"/\" is refused");
        check(HelpParser.Parse("dubla", WithShot.Replace("Captura: alta", "Captura: pagina")).Errors.Any(error => error.Contains("de doua ori")), "Help: two captures with the same id are refused");

        var jpeg = new Func<byte[]>(() => [0xFF, 0xD8, 0xFF]);
        Func<byte[]> Json(string text) => () => System.Text.Encoding.UTF8.GetBytes(text);
        var images = new Dictionary<string, Func<byte[]>>
        {
            ["cu-captura.pagina.jpg"] = jpeg,
            ["cu-captura.pagina.json"] = Json("{\"w\":1920,\"h\":1080,\"rects\":{\"1\":[[100,50,200,40]],\"3\":[[10,10,32,32],[10,60,32,32]]}}"),
            ["cu-captura.alta.jpg"] = jpeg,
            ["cu-captura.alta.json"] = Json("{\"w\":1920,\"h\":900,\"rects\":{\"0\":[[5,5,80,30]]}}")
        };
        var catalog = HelpCatalog.FromSources([("cu-captura", WithShot)], images);
        check(catalog.Errors.Count == 0, "Help: pictures with valid maps are accepted" + (catalog.Errors.Count > 0 ? " (" + string.Join("; ", catalog.Errors) + ")" : ""));
        var html = HelpRenderer.Topic(catalog.Get("cu-captura")!, catalog);
        check(html.Contains("<figure class=\"help-shot\" data-shot=\"pagina\"") && html.Contains("/ajutor/imagine/cu-captura.pagina.jpg") && html.Contains("data-help-zoom"), "Help: the picture of a capture is shown in the elements section, with the zoom button");
        check(html.Contains("<table class=\"help-table\"") && !html.Contains("<dl") && html.Contains("help-zone"), "Help: the elements are a table, grouped by zone rows (no list of definitions)");
        check(html.Split("help-pickable").Length - 1 == 3, "Help: only the rows whose element is on a picture can be chosen (3 of 4 here)");
        check(Regex.IsMatch(html, "data-n=\"0\"[^>]*data-help-pick data-shot=\"alta\"") && Regex.IsMatch(html, "data-n=\"1\"[^>]*data-help-pick data-shot=\"pagina\""), "Help: a row points to the picture named by img: (the first capture otherwise)");
        check(!Regex.IsMatch(html, "data-n=\"2\"[^>]*data-help-pick"), "Help: a row whose element is not on the picture has no frame to show");
        check(html.Contains("data-help-locate") && html.Contains("Arată pe pagină") && html.Contains("data-help-sel=\"text:&quot;Salvează&quot;\""), "Help: every row has the \"view on page\" icon, with the selector of the element");
        check(!html.Contains("help-badge") && !html.Contains("help-number"), "Help: no numbers or badges on the picture");

        var orphan = HelpCatalog.FromSources([("cu-captura", WithShot)], new Dictionary<string, Func<byte[]>> { ["cu-captura.pagina.jpg"] = jpeg, ["cu-captura.pagina.json"] = Json("{\"w\":1920,\"h\":1080,\"rects\":{\"9\":[[1,1,5,5]]}}"), ["alt-topic.x.jpg"] = jpeg });
        check(orphan.Errors.Any(error => error.Contains("elementul 9")) && orphan.Errors.Any(error => error.Contains("alt-topic.x.jpg")), "Help: a map that names an element the topic does not have, and a picture of no topic, are errors");
        var outside = HelpCatalog.FromSources([("cu-captura", WithShot)], new Dictionary<string, Func<byte[]>> { ["cu-captura.pagina.jpg"] = jpeg, ["cu-captura.pagina.json"] = Json("{\"w\":100,\"h\":100,\"rects\":{\"0\":[[90,90,50,50]]}}") });
        check(outside.Errors.Any(error => error.Contains("in afara imaginii")), "Help: a box outside the picture is an error");
        var noPicture = HelpCatalog.FromSources([("cu-captura", WithShot)]);
        check(noPicture.Errors.Count == 0 && !HelpRenderer.Topic(noPicture.Get("cu-captura")!, noPicture).Contains("help-shot"), "Help: a capture whose picture is not generated yet shows nothing (and is not an error)");
        check(HelpShotMap.Read("{ nu e json", out var failure) is null && failure.Length > 0, "Help: a map that is not JSON is refused");
    }

    // Topics whose window cannot be photographed with the demo data. Empty now (the ANAF differences dialog is photographed with the CUI of Electric Standard Prest Simeria, which ANAF knows).
    // The list only shrinks: a topic that gets a "Captura:" must be taken out, a new topic without capture must be added (or get one).
    private static readonly HashSet<string> WithoutCapture = new(StringComparer.OrdinalIgnoreCase)
    {
    };

    // The pictures shipped with the topics (tools/help-capture.mjs): every "Captura:" has its picture and map, the maps point to real elements, the size stays small.
    private static void ShippedShots(Action<bool, string> check)
    {
        var catalog = HelpCatalog.Default;
        var missing = catalog.Topics.Values.Where(topic => topic.Captures.Count == 0 && !WithoutCapture.Contains(topic.Key)).Select(topic => topic.Key).ToList();
        check(missing.Count == 0, "Help: every topic has a capture of its window (or is listed as not photographable)" + (missing.Count > 0 ? ": " + string.Join(", ", missing) : ""));
        var listedButHave = WithoutCapture.Where(key => catalog.Get(key) is { Captures.Count: > 0 }).ToList();
        check(listedButHave.Count == 0, "Help: a topic that has a capture is taken out of the \"without capture\" list" + (listedButHave.Count > 0 ? ": " + string.Join(", ", listedButHave) : ""));
        var unknown = WithoutCapture.Where(key => catalog.Get(key) is null).ToList();
        check(unknown.Count == 0, "Help: the \"without capture\" list names only topics that exist" + (unknown.Count > 0 ? ": " + string.Join(", ", unknown) : ""));

        var notGenerated = catalog.Topics.Values.SelectMany(topic => topic.Captures.Where(capture => catalog.Shots(topic).All(shot => shot.Capture.Id != capture.Id)).Select(capture => topic.Key + "." + capture.Id)).ToList();
        check(notGenerated.Count == 0, "Help: every capture line has its picture (run tools/help-capture.mjs)" + (notGenerated.Count > 0 ? ": " + string.Join(", ", notGenerated.Take(8)) : ""));
        var noMap = catalog.Topics.Values.SelectMany(topic => catalog.Shots(topic).Where(shot => shot.Map is null || shot.Map.Rects.Count == 0).Select(shot => topic.Key + "." + shot.Capture.Id)).ToList();
        check(noMap.Count == 0, "Help: every picture has a map with at least one element on it" + (noMap.Count > 0 ? ": " + string.Join(", ", noMap.Take(8)) : ""));
        check(catalog.Errors.Count == 0, "Help: the shipped topics, pictures and maps have no errors" + (catalog.Errors.Count > 0 ? " (" + string.Join("; ", catalog.Errors.Take(5)) + ")" : ""));
        var bytes = catalog.ImageNames.Where(name => name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)).Sum(name => (long)catalog.Image(name)!.Length);
        check(bytes < 12L * 1024 * 1024, $"Help: the screenshots stay under 12 MB ({bytes / 1024 / 1024.0:0.0} MB)");
        var rows = HelpRenderer.Topic(catalog.Get("produse-catalog")!, catalog);
        check(rows.Contains("help-pickable") && rows.Contains("<figure class=\"help-shot\""), "Help: the real topic of the product catalog shows its picture and pickable rows");
    }

    // The topics shipped with the application.
    private static void Coverage(Action<bool, string> check)
    {
        var catalog = HelpCatalog.Default;
        check(catalog.Errors.Count == 0, "Help: the topics of the application (Assets/Help) have no errors" + (catalog.Errors.Count > 0 ? ": " + string.Join(" | ", catalog.Errors.Take(5)) : ""));

        // Every icon shown in a topic is a row-icon that exists in the stylesheet (a name that does not exist would show an empty square).
        var root = FindRoot();
        if (root is not null && File.Exists(Path.Combine(root, "wwwroot", "app.css")))
        {
            var css = File.ReadAllText(Path.Combine(root, "wwwroot", "app.css"));
            var missingIcons = catalog.Topics.Values.SelectMany(topic => topic.Areas).SelectMany(area => area.Elements).Where(element => element.Icon is not null)
                .Select(element => element.Icon!).Distinct().Where(icon => !(css.Contains(".row-icon." + icon + " ") || css.Contains(".row-icon." + icon + "{") || css.Contains(".row-icon." + icon + ":"))).ToList();
            check(missingIcons.Count == 0, "Help: every icon named in a topic exists in app.css " + (missingIcons.Count > 0 ? ": " + string.Join(", ", missingIcons) : ""));
        }

        var shapes = typeof(HelpCatalog).Assembly.GetTypes()
            .Where(type => typeof(IComponent).IsAssignableFrom(type))
            .SelectMany(type => type.GetCustomAttributes<RouteAttribute>(false).Select(route => route.Template))
            .Select(Shape).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(shape => shape, StringComparer.OrdinalIgnoreCase).ToList();
        var documented = catalog.Topics.Values.SelectMany(topic => topic.Routes).Select(Shape).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // The Razor Pages outside Blazor (Login, AccessDenied) have no [Route]: their addresses are listed here.
        var outsideBlazor = new[] { "/Account/Login", "/Account/AccessDenied" };

        var missing = shapes.Where(shape => !documented.Contains(shape) && !Exempt.Contains(shape) && !Pending.Contains(shape)).ToList();
        check(missing.Count == 0, "Help: every page with an address has a topic (or is on the list of pages still to be written)" + (missing.Count > 0 ? ": " + string.Join(", ", missing) : ""));
        var finished = Pending.Where(shape => documented.Contains(shape)).ToList();
        check(finished.Count == 0, "Help: a page that has its topic is taken off the list of pages still to be written" + (finished.Count > 0 ? ": " + string.Join(", ", finished) : ""));
        var unknown = Pending.Concat(Exempt).Where(shape => !shapes.Contains(shape, StringComparer.OrdinalIgnoreCase)).ToList();
        check(unknown.Count == 0, "Help: the lists of exempt/pending pages name only existing pages" + (unknown.Count > 0 ? ": " + string.Join(", ", unknown) : ""));
        check(outsideBlazor.All(path => catalog.Resolve(path).Topic?.Public == true), "Help: Login and AccessDenied have public topics (readable without signing in)");
        check(catalog.Topics.Values.Where(topic => topic.Public).All(topic => topic.Routes.All(route => outsideBlazor.Contains(route, StringComparer.OrdinalIgnoreCase))),
            "Help: only the pages outside Blazor are public");
    }

    // A route as a shape: the names of the parameters do not matter ("/produse/{Id:int}" = "/produse/{x:int}").
    private static string Shape(string template) => Regex.Replace(template, @"\{[^}]*\}", "{}");

    // Every button of a dialog that opens a topic (HelpButton Topic="...", data-help-topic="...") points to a topic that exists; a topic with no
    // address (a dialog or editor) is used by at least one such button or link.
    private static void ReferencedTopics(Action<bool, string> check)
    {
        var root = FindRoot();
        if (root is null) { check(true, "Help: the source files were not found next to the checks (references of the dialogs not scanned)"); return; }
        var catalog = HelpCatalog.Default;
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var wrong = new List<string>();
        foreach (var file in new[] { "Components", "Modules", "Pages" }.Select(folder => Path.Combine(root, folder)).Where(Directory.Exists).SelectMany(folder => Directory.EnumerateFiles(folder, "*.razor", SearchOption.AllDirectories)))
        {
            foreach (Match match in Regex.Matches(File.ReadAllText(file), @"<HelpButton\s[^>]*Topic=""([^""]+)"""))
            {
                used.Add(match.Groups[1].Value);
                if (catalog.Get(match.Groups[1].Value) is null) wrong.Add($"{Path.GetFileName(file)} -> {match.Groups[1].Value}");
            }
        }
        check(wrong.Count == 0, "Help: every help button of a dialog points to an existing topic" + (wrong.Count > 0 ? ": " + string.Join(", ", wrong) : ""));
        // Every file that draws a window over the page (class delete-dialog, delete-overlay or invoice-modal) has a help button, unless it is a file that only asks
        // for a confirmation or that only wraps a form which has its own help button (listed here, with the reason).
        var dialogsWithoutHelp = new List<string>();
        foreach (var file in new[] { "Components", "Modules", "Pages" }.Select(folder => Path.Combine(root, folder)).Where(Directory.Exists).SelectMany(folder => Directory.EnumerateFiles(folder, "*.razor", SearchOption.AllDirectories)))
        {
            var text = File.ReadAllText(file);
            if (!Regex.IsMatch(text, @"class=""(delete-dialog|invoice-modal|delete-overlay)")) continue;
            if (!text.Contains("<HelpButton", StringComparison.Ordinal) && !DialogFilesWithoutHelp.Contains(Path.GetFileName(file))) dialogsWithoutHelp.Add(Path.GetFileName(file));
        }
        check(dialogsWithoutHelp.Count == 0, "Help: every file with a window over the page has a help button (or is a confirmation / a wrapper of a form that has one)" + (dialogsWithoutHelp.Count > 0 ? ": " + string.Join(", ", dialogsWithoutHelp) : ""));
        var linked = catalog.Topics.Values.SelectMany(topic => topic.Summary.Select(block => block.Text)
            .Concat(topic.Areas.SelectMany(area => area.Elements).Select(element => element.Text))
            .Concat(topic.Flows.SelectMany(flow => flow.Blocks).Select(block => block.Text)))
            .SelectMany(text => HelpCatalog.LinkRegex().Matches(text).Select(match => match.Groups[1].Value));
        used.UnionWith(linked);
        var orphans = catalog.Topics.Values.Where(topic => topic.Routes.Count == 0 && !used.Contains(topic.Key)).Select(topic => topic.Key).ToList();
        check(orphans.Count == 0, "Help: every topic without an address (dialog, editor) is opened by a help button or a link" + (orphans.Count > 0 ? ": " + string.Join(", ", orphans) : ""));
    }

    // Files with a window over the page that need no help button of their own: confirmations (they explain themselves) and files that only wrap a form with its own help button.
    private static readonly HashSet<string> DialogFilesWithoutHelp = new(StringComparer.OrdinalIgnoreCase)
    {
        "MainLayout.razor", "DeleteConfirmationDialog.razor", "SaveConfirmationDialog.razor", "UnsavedChangesDialog.razor", "RestoreConfirmationDialog.razor", "ForceUnlockDialog.razor",
        "ParameterValueEditDialog.razor", "BackupSettingsPanel.razor", "ProjectComponentsSection.razor",
        "Home.razor", "InvoiceTemplatesList.razor", "OfferPickup.razor"
    };

    private static string? FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BlazorStoc.csproj"))) directory = directory.Parent;
        return directory?.FullName;
    }
}
