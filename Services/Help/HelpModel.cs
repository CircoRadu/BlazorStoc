namespace BlazorStoc.Help;

// The in-application help (docs/PROPUNERE_HELP.md): one topic per window (page, tab, editor or dialog), written as a text file
// Assets/Help/<key>.help (embedded in the assembly, so the text changes with a rebuild).

public enum HelpBlockKind { Paragraph, Step, Note, Image }

/// <summary>A paragraph, a numbered step, a note or a picture of a topic or of a flow ("Caption" is the legend of a picture).</summary>
public sealed record HelpBlock(HelpBlockKind Kind, string Text, string? Caption = null);

/// <summary>An element of the window. "Icon" is the name of a row-icon (the real icon is shown next to the description).</summary>
public sealed record HelpElement(string Name, string Kind, string? Icon, string Text);

public sealed record HelpArea(string Name, IReadOnlyList<HelpElement> Elements);

/// <summary>A work flow ("Cum preiau o factură"): a collapsible section with steps.</summary>
public sealed record HelpFlow(string Title, IReadOnlyList<HelpBlock> Blocks);

/// <param name="Key">The name of the file without extension: stable, lower case, letters, digits, '-' and '.'.</param>
/// <param name="Routes">Addresses of the pages this topic describes (patterns like /produse/{id}); empty for a dialog or an editor, opened by its key.</param>
/// <param name="Tab">The label of the tab this topic describes (the topic of the page without tab is the general one).</param>
/// <param name="Public">Readable without signing in (the pages outside the application: Login, AccessDenied).</param>
public sealed record HelpTopic(
    string Key, string Title, IReadOnlyList<string> Routes, string? Tab, bool Public, string Keywords,
    IReadOnlyList<HelpBlock> Summary, IReadOnlyList<HelpArea> Areas, IReadOnlyList<HelpFlow> Flows);

public sealed record HelpParseResult(HelpTopic? Topic, IReadOnlyList<string> Errors);
