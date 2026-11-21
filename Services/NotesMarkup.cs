using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;

namespace BlazorStoc.Services;

/// <summary>
/// The one bit of markup of the notes of an intervention taken from a ticket (BUG-0023): a label written between double asterisks is drawn bold where the notes are shown read-only; everywhere else
/// (tables, tooltips) the asterisks are taken out and in the editing field the text is as stored.
/// </summary>
public static partial class NotesMarkup
{
    [GeneratedRegex(@"\*\*([^*\r\n]+?)\*\*")]
    private static partial Regex BoldPattern();

    /// <summary>A label as the notes write it (any asterisk of the label itself is dropped, so it cannot break the pairs).</summary>
    public static string Bold(string label) => "**" + label.Replace("*", "") + "**";

    /// <summary>The notes without the markup, for a table cell or a tooltip.</summary>
    public static string Plain(string? text) => BoldPattern().Replace(text ?? "", "$1");

    /// <summary>The notes for a read-only view: encoded text with the labels bold. Line breaks are kept by the style of the element (<c>white-space: pre-wrap</c>).</summary>
    public static MarkupString Html(string? text) => new(BoldPattern().Replace(WebUtility.HtmlEncode(text ?? ""), "<strong>$1</strong>"));
}
