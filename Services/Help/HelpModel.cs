namespace BlazorStoc.Help;

// The in-application help (docs/PROPUNERE_HELP.md): one topic per window (page, tab, editor or dialog), written as a text file
// Assets/Help/<key>.help (embedded in the assembly, so the text changes with a rebuild).

public enum HelpBlockKind { Paragraph, Step, Note, Image }

/// <summary>A paragraph, a numbered step, a note or a picture of a topic or of a flow ("Caption" is the legend of a picture).</summary>
public sealed record HelpBlock(HelpBlockKind Kind, string Text, string? Caption = null);

/// <summary>
/// An element of the window. "Icon" is the name of a row-icon (the real icon is shown next to the description). "Selector" ("sel:" in the file) says where the
/// element is on the page when its name is not enough (a CSS selector or text:"..."); "ImageId" ("img:") says which screenshot of the topic shows it
/// (the first one when absent).
/// </summary>
public sealed record HelpElement(string Name, string Kind, string? Icon, string Text, string? Selector = null, string? ImageId = null);

public sealed record HelpArea(string Name, IReadOnlyList<HelpElement> Elements);

/// <summary>A work flow ("Cum preiau o factură"): a collapsible section with steps.</summary>
public sealed record HelpFlow(string Title, IReadOnlyList<HelpBlock> Blocks);

/// <summary>
/// A screenshot of the window ("Captura:" in the file): "Url" is opened by tools/help-capture, "Steps" are what it does before the shot (a window opened by a
/// click), "Caption" is the legend. The picture is Assets/Help/img/&lt;key&gt;.&lt;id&gt;.jpg and the places of the elements on it &lt;key&gt;.&lt;id&gt;.json.
/// </summary>
public sealed record HelpCapture(string Id, string Url, IReadOnlyList<string> Steps, string? Caption);

/// <param name="Key">The name of the file without extension: stable, lower case, letters, digits, '-' and '.'.</param>
/// <param name="Routes">Addresses of the pages this topic describes (patterns like /produse/{id}); empty for a dialog or an editor, opened by its key.</param>
/// <param name="Tab">The label of the tab this topic describes (the topic of the page without tab is the general one).</param>
/// <param name="Public">Readable without signing in (the pages outside the application: Login, AccessDenied).</param>
public sealed record HelpTopic(
    string Key, string Title, IReadOnlyList<string> Routes, string? Tab, bool Public, string Keywords,
    IReadOnlyList<HelpBlock> Summary, IReadOnlyList<HelpArea> Areas, IReadOnlyList<HelpFlow> Flows, IReadOnlyList<HelpCapture> Captures)
{
    /// <summary>The elements in the order of the file: the number of an element (its place in this list) is what the map of a screenshot refers to.</summary>
    public IReadOnlyList<HelpElement> AllElements => Areas.SelectMany(area => area.Elements).ToList();
}

public sealed record HelpParseResult(HelpTopic? Topic, IReadOnlyList<string> Errors);

/// <summary>A screenshot of a topic: the capture, the name of the picture and the places of the elements on it (null before the map exists).</summary>
public sealed record HelpShot(HelpCapture Capture, string ImageName, HelpShotMap? Map);

/// <summary>
/// Where each element of a topic is on its screenshot: "Rects" maps the number of an element (its place in <see cref="HelpTopic.AllElements"/>) to its
/// boxes (x, y, width, height in pixels of the picture; an element that appears several times, like an icon on every row, has several boxes).
/// </summary>
public sealed record HelpShotMap(int Width, int Height, IReadOnlyDictionary<int, IReadOnlyList<int[]>> Rects)
{
    public static HelpShotMap? Read(string json, out string error)
    {
        error = "";
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            var root = document.RootElement;
            var width = root.GetProperty("w").GetInt32();
            var height = root.GetProperty("h").GetInt32();
            if (width <= 0 || height <= 0) { error = "dimensiuni negative"; return null; }
            var rects = new Dictionary<int, IReadOnlyList<int[]>>();
            foreach (var property in root.GetProperty("rects").EnumerateObject())
            {
                var boxes = new List<int[]>();
                foreach (var box in property.Value.EnumerateArray())
                {
                    var numbers = box.EnumerateArray().Select(item => (int)Math.Round(item.GetDouble())).ToArray();
                    if (numbers.Length != 4) { error = "un dreptunghi nu are 4 numere"; return null; }
                    boxes.Add(numbers);
                }
                rects[int.Parse(property.Name, System.Globalization.CultureInfo.InvariantCulture)] = boxes;
            }
            return new HelpShotMap(width, height, rects);
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            error = exception.Message;
            return null;
        }
    }
}
