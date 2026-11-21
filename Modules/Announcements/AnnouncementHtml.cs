using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace BlazorStoc.Modules.Announcements;

// The body of an announcement reaches every user, so it is rebuilt on the server from an allow-list before it is saved: only the tags below, only the attributes below, no scripts, no event
// attributes, no javascript: addresses. Everything else is dropped (the text of an unknown tag stays, the content of script/style/iframe etc. goes with the tag). The output is rebuilt
// from tokens, never a copy of the input, so a trick in the markup cannot survive.
public static partial class AnnouncementHtml
{
    private static readonly HashSet<string> Tags = new(StringComparer.Ordinal)
    {
        "p", "br", "div", "span", "strong", "b", "em", "i", "u", "s", "h1", "h2", "h3", "h4", "ul", "ol", "li", "a", "blockquote", "hr", "img"
    };
    private static readonly HashSet<string> VoidTags = new(StringComparer.Ordinal) { "br", "hr", "img" };
    // The tags whose whole content is removed with the tag.
    private static readonly HashSet<string> DropWithContent = new(StringComparer.Ordinal)
    {
        "script", "style", "iframe", "object", "embed", "template", "noscript", "svg", "math", "form", "textarea", "title", "head", "link", "meta", "base", "frame", "frameset", "applet", "select", "button", "audio", "video", "canvas"
    };

    [GeneratedRegex(@"^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$")]
    private static partial Regex HexColor();
    [GeneratedRegex(@"^rgb\(\s*\d{1,3}\s*,\s*\d{1,3}\s*,\s*\d{1,3}\s*\)$", RegexOptions.IgnoreCase)]
    private static partial Regex RgbColor();
    [GeneratedRegex(@"^/media/anunturi/(\d{1,9})$")]
    private static partial Regex ImagePath();

    /// <summary>The identifiers of the pictures of the announcement a (cleaned) body shows.</summary>
    public static IReadOnlyList<int> ImageIds(string cleanHtml) =>
        [.. ImageSrc().Matches(cleanHtml).Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)).Distinct()];

    [GeneratedRegex(@"src=""/media/anunturi/(\d{1,9})""")]
    private static partial Regex ImageSrc();

    /// <summary>Plain text of a cleaned body (to tell an empty body from one with text).</summary>
    public static string PlainText(string cleanHtml) => WebUtility.HtmlDecode(Regex.Replace(cleanHtml, "<[^>]*>", " ")).Trim();

    /// <summary>Replaces the picture addresses a ZIP uses (imagini/x.png) before cleaning; mapper returns the final address or null (the picture is dropped).</summary>
    public static string Clean(string? html, Func<string, string?>? imageResolver = null)
    {
        if (string.IsNullOrEmpty(html)) return "";
        var output = new StringBuilder(html.Length);
        var open = new Stack<string>();
        var index = 0;
        string? dropUntil = null;
        var dropDepth = 0;
        while (index < html.Length)
        {
            var lt = html.IndexOf('<', index);
            if (lt < 0) { if (dropUntil is null) AppendText(output, html[index..]); break; }
            if (lt > index && dropUntil is null) AppendText(output, html[index..lt]);
            // A comment or a processing instruction / doctype is dropped.
            if (string.CompareOrdinal(html, lt, "<!--", 0, 4) == 0)
            {
                var endComment = html.IndexOf("-->", lt + 4, StringComparison.Ordinal);
                index = endComment < 0 ? html.Length : endComment + 3;
                continue;
            }
            var closeIndex = FindTagEnd(html, lt);
            if (closeIndex < 0) { if (dropUntil is null) AppendText(output, "<"); index = lt + 1; continue; }
            var inner = html.Substring(lt + 1, closeIndex - lt - 1);
            index = closeIndex + 1;
            if (inner.Length > 0 && inner[0] is '!' or '?') continue;
            // A '<' that does not start a tag ("1 < 2") is text.
            if (inner.Length == 0 || !(char.IsLetter(inner[0]) || inner[0] == '/')) { if (dropUntil is null) AppendText(output, "<"); index = lt + 1; continue; }
            var closing = inner[0] == '/';
            var rest = closing ? inner[1..] : inner;
            var nameEnd = 0;
            while (nameEnd < rest.Length && (char.IsLetterOrDigit(rest[nameEnd]) || rest[nameEnd] == '-')) nameEnd++;
            var name = rest[..nameEnd].ToLowerInvariant();
            if (name.Length == 0) continue;
            if (dropUntil is not null)
            {
                if (name == dropUntil) { if (closing) { if (--dropDepth <= 0) dropUntil = null; } else if (!rest.EndsWith('/')) dropDepth++; }
                continue;
            }
            if (DropWithContent.Contains(name))
            {
                if (!closing && !rest.TrimEnd().EndsWith('/')) { dropUntil = name; dropDepth = 1; }
                continue;
            }
            if (!Tags.Contains(name)) continue;
            if (closing)
            {
                if (VoidTags.Contains(name) || !open.Contains(name)) continue;
                while (open.Count > 0)
                {
                    var top = open.Pop();
                    output.Append("</").Append(top).Append('>');
                    if (top == name) break;
                }
                continue;
            }
            var attributes = ParseAttributes(rest[nameEnd..]);
            if (name == "img")
            {
                var src = attributes.GetValueOrDefault("src");
                if (src is not null && imageResolver is not null) src = imageResolver(src);
                if (src is null || !ImagePath().IsMatch(src)) continue;
                output.Append("<img src=\"").Append(src).Append('"');
                var alt = attributes.GetValueOrDefault("alt");
                output.Append(" alt=\"").Append(alt is null ? "" : Escape(Limit(alt, 200))).Append("\">");
                continue;
            }
            output.Append('<').Append(name);
            if (name == "a")
            {
                var href = attributes.GetValueOrDefault("href");
                if (href is not null && SafeHref(href) is { } safe)
                    output.Append(" href=\"").Append(Escape(safe)).Append("\" target=\"_blank\" rel=\"noopener noreferrer\"");
            }
            else if (name is "span" or "div" or "p" or "h1" or "h2" or "h3" or "h4" or "li" or "blockquote" or "ul" or "ol" or "strong" or "b" or "em" or "i" or "u" or "s")
            {
                if (attributes.GetValueOrDefault("style") is { } style && SafeStyle(style, name) is { Length: > 0 } cleanStyle)
                    output.Append(" style=\"").Append(cleanStyle).Append('"');
            }
            output.Append('>');
            if (!VoidTags.Contains(name)) open.Push(name);
        }
        while (open.Count > 0) output.Append("</").Append(open.Pop()).Append('>');
        return output.ToString();
    }

    private static string? SafeHref(string href)
    {
        var value = Regex.Replace(WebUtility.HtmlDecode(href), @"[\u0000- \u007f]", "");
        if (value.Length == 0 || value.Length > 2000) return null;
        return value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || value.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
            ? WebUtility.HtmlDecode(href).Trim() : null;
    }

    // Only color and text-align survive, with values checked one by one.
    private static string SafeStyle(string style, string tag)
    {
        var kept = new List<string>();
        foreach (var part in WebUtility.HtmlDecode(style).Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split(':', 2);
            if (pair.Length != 2) continue;
            var key = pair[0].Trim().ToLowerInvariant();
            var value = pair[1].Trim();
            if (key == "color" && (HexColor().IsMatch(value) || RgbColor().IsMatch(value))) kept.Add("color:" + value);
            else if (key == "text-align" && value.ToLowerInvariant() is "left" or "center" or "right" or "justify" && tag is "p" or "div" or "h1" or "h2" or "h3" or "h4" or "li" or "blockquote") kept.Add("text-align:" + value.ToLowerInvariant());
        }
        return string.Join(';', kept);
    }

    // The end of a tag: the first '>' that is not inside a quoted attribute value.
    private static int FindTagEnd(string html, int start)
    {
        char quote = '\0';
        for (var i = start + 1; i < html.Length; i++)
        {
            var c = html[i];
            if (quote != '\0') { if (c == quote) quote = '\0'; }
            else if (c is '"' or '\'') quote = c;
            else if (c == '>') return i;
            else if (c == '<' && i > start + 1) return -1;
        }
        return -1;
    }

    private static Dictionary<string, string> ParseAttributes(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var i = 0;
        while (i < text.Length)
        {
            while (i < text.Length && (char.IsWhiteSpace(text[i]) || text[i] == '/')) i++;
            var start = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] != '=' && text[i] != '/') i++;
            var key = text[start..i].ToLowerInvariant();
            while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
            var value = "";
            if (i < text.Length && text[i] == '=')
            {
                i++;
                while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
                if (i < text.Length && text[i] is '"' or '\'')
                {
                    var quote = text[i++];
                    var valueStart = i;
                    while (i < text.Length && text[i] != quote) i++;
                    value = text[valueStart..Math.Min(i, text.Length)];
                    if (i < text.Length) i++;
                }
                else
                {
                    var valueStart = i;
                    while (i < text.Length && !char.IsWhiteSpace(text[i])) i++;
                    value = text[valueStart..i];
                }
            }
            if (key.Length > 0 && !result.ContainsKey(key)) result[key] = WebUtility.HtmlDecode(value);
        }
        return result;
    }

    private static void AppendText(StringBuilder output, string text) => output.Append(Escape(WebUtility.HtmlDecode(text)));
    private static string Escape(string text) => text.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal).Replace("\"", "&quot;", StringComparison.Ordinal);
    private static string Limit(string text, int max) => text.Length > max ? text[..max] : text;
}
