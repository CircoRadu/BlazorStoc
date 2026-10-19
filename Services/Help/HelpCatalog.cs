using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace BlazorStoc.Help;

/// <summary>The topics of the help: loaded once from the resources of the assembly (BlazorStoc.Help.*.help and BlazorStoc.HelpImg.*), checked at load.</summary>
public sealed partial class HelpCatalog
{
    public const string TopicPrefix = "BlazorStoc.Help.";
    public const string ImagePrefix = "BlazorStoc.HelpImg.";

    private static readonly Lazy<HelpCatalog> DefaultCatalog = new(() => FromAssembly(typeof(HelpCatalog).Assembly));
    public static HelpCatalog Default => DefaultCatalog.Value;

    private readonly Dictionary<string, HelpTopic> topics;
    private readonly Dictionary<string, Func<byte[]>> images;

    public IReadOnlyDictionary<string, HelpTopic> Topics => topics;
    public IReadOnlyCollection<string> ImageNames => images.Keys;
    /// <summary>What is wrong in the files (a missing section, a link to a topic that does not exist...). Empty when the help is valid.</summary>
    public IReadOnlyList<string> Errors { get; }

    private HelpCatalog(Dictionary<string, HelpTopic> topics, Dictionary<string, Func<byte[]>> images, List<string> errors)
    {
        this.topics = topics;
        this.images = images;
        Errors = errors;
    }

    public static HelpCatalog FromAssembly(Assembly assembly)
    {
        var sources = new List<(string Key, string Text)>();
        var images = new Dictionary<string, Func<byte[]>>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (name.StartsWith(TopicPrefix, StringComparison.Ordinal) && name.EndsWith(".help", StringComparison.Ordinal))
            {
                using var stream = assembly.GetManifestResourceStream(name)!;
                using var reader = new StreamReader(stream, Encoding.UTF8);
                sources.Add((name[TopicPrefix.Length..^5], reader.ReadToEnd()));
            }
            else if (name.StartsWith(ImagePrefix, StringComparison.Ordinal))
            {
                var resource = name;
                images[name[ImagePrefix.Length..]] = () =>
                {
                    using var stream = assembly.GetManifestResourceStream(resource)!;
                    using var memory = new MemoryStream();
                    stream.CopyTo(memory);
                    return memory.ToArray();
                };
            }
        }
        return FromSources(sources, images);
    }

    public static HelpCatalog FromSources(IEnumerable<(string Key, string Text)> sources, IReadOnlyDictionary<string, Func<byte[]>>? images = null)
    {
        var errors = new List<string>();
        var topics = new Dictionary<string, HelpTopic>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, text) in sources.OrderBy(source => source.Key, StringComparer.Ordinal))
        {
            var result = HelpParser.Parse(key, text);
            errors.AddRange(result.Errors);
            if (result.Topic is not null) topics[key] = result.Topic;
        }

        var pictures = new Dictionary<string, Func<byte[]>>(images ?? new Dictionary<string, Func<byte[]>>(), StringComparer.OrdinalIgnoreCase);
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var topic in topics.Values)
        {
            foreach (var route in topic.Routes)
            {
                var id = Normalize(route) + "|" + (topic.Tab?.Trim().ToLowerInvariant() ?? "");
                if (seen.TryGetValue(id, out var other)) errors.Add($"{topic.Key}.help: ruta {route} (tab \"{topic.Tab}\") este deja descrisa de {other}.");
                else seen[id] = topic.Key;
            }
            var texts = topic.Summary.Concat(topic.Flows.SelectMany(flow => flow.Blocks)).ToList();
            foreach (var block in texts.Where(block => block.Kind == HelpBlockKind.Image))
                if (!pictures.ContainsKey(block.Text)) errors.Add($"{topic.Key}.help: imaginea {block.Text} nu exista in Assets/Help/img.");
            var all = texts.Select(block => block.Text).Concat(topic.Areas.SelectMany(area => area.Elements).Select(element => element.Text));
            foreach (var link in all.SelectMany(text => LinkRegex().Matches(text).Select(match => match.Groups[1].Value)).Distinct())
                if (!topics.ContainsKey(link)) errors.Add($"{topic.Key}.help: legatura [[{link}]] indica un subiect care nu exista.");
        }
        return new HelpCatalog(topics, pictures, errors);
    }

    [GeneratedRegex(@"\[\[([a-z0-9.\-]+)(?:\|[^\]]*)?\]\]")]
    public static partial Regex LinkRegex();

    public byte[]? Image(string name) => images.TryGetValue(name, out var read) ? read() : null;

    public HelpTopic? Get(string key) => topics.TryGetValue(key, out var topic) ? topic : null;

    /// <summary>The path of an address in the form compared with the routes of the topics: lower case, no query, no final '/'.</summary>
    public static string Normalize(string path)
    {
        var end = path.IndexOfAny(['?', '#']);
        if (end >= 0) path = path[..end];
        path = path.Trim().ToLowerInvariant();
        if (path.Length > 1) path = path.TrimEnd('/');
        return path.Length == 0 ? "/" : path;
    }

    /// <summary>
    /// The topic of a window: of the page at "path", and of its tab when one of the labels in "tabs" (the selected tabs, the outer one first) has a topic;
    /// otherwise the general topic of the page. "Others" are the other topics of the same page (the tabs), for the links of the help window.
    /// </summary>
    public (HelpTopic? Topic, IReadOnlyList<HelpTopic> Others) Resolve(string path, IReadOnlyList<string>? tabs = null)
    {
        var target = Normalize(path).Split('/', StringSplitOptions.RemoveEmptyEntries);
        var candidates = new List<(HelpTopic Topic, int Literals)>();
        foreach (var topic in topics.Values)
        {
            var best = -1;
            foreach (var route in topic.Routes)
            {
                var pattern = Normalize(route).Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (pattern.Length != target.Length) continue;
                var literals = 0;
                var ok = true;
                for (var i = 0; i < pattern.Length && ok; i++)
                {
                    if (pattern[i].StartsWith('{')) { ok = SegmentFits(pattern[i], target[i]); continue; }
                    if (pattern[i] == target[i]) literals++; else ok = false;
                }
                if (ok) best = Math.Max(best, literals);
            }
            if (best >= 0) candidates.Add((topic, best));
        }
        if (candidates.Count == 0) return (null, []);
        var top = candidates.Max(candidate => candidate.Literals);
        var page = candidates.Where(candidate => candidate.Literals == top).Select(candidate => candidate.Topic).ToList();

        HelpTopic? chosen = null;
        foreach (var label in (tabs ?? []).Reverse())
        {
            var clean = CleanTab(label);
            chosen = page.FirstOrDefault(topic => topic.Tab is not null && string.Equals(CleanTab(topic.Tab), clean, StringComparison.OrdinalIgnoreCase));
            if (chosen is not null) break;
        }
        chosen ??= page.FirstOrDefault(topic => topic.Tab is null) ?? page[0];
        return (chosen, page.Where(topic => topic != chosen).OrderBy(topic => topic.Tab is null ? 0 : 1).ThenBy(topic => topic.Title, StringComparer.CurrentCultureIgnoreCase).ToList());
    }

    // A parameter of a route accepts what its constraint accepts ({id:int} only digits, {id:guid} only a guid), so "/produse/{id:int}" is not the page "/produse/preluare-factura".
    private static bool SegmentFits(string parameter, string value)
    {
        var colon = parameter.IndexOf(':');
        var constraint = colon > 0 ? parameter[(colon + 1)..].TrimEnd('}') : "";
        return constraint switch
        {
            "int" or "long" => value.Length > 0 && value.All(char.IsAsciiDigit),
            "guid" => Guid.TryParse(value, out _),
            _ => value.Length > 0
        };
    }

    // "De regularizat (3)" and "Registru interventii 12" lose their number: the count of a tab label is not part of its name.
    private static string CleanTab(string label) => Regex.Replace(label.Trim(), @"\s*\(?\d+\)?\s*$", "").Trim();

    /// <summary>Every word of the query must appear in the topic (title, keywords, elements, flows, texts); the title counts most.</summary>
    public IReadOnlyList<HelpTopic> Search(string query, int limit = 20)
    {
        var words = Fold(query).Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return [];
        var scored = new List<(HelpTopic Topic, int Score)>();
        foreach (var topic in topics.Values)
        {
            var title = Fold(topic.Title + " " + topic.Tab);
            var keywords = Fold(topic.Keywords);
            var elements = Fold(string.Join(' ', topic.Areas.SelectMany(area => area.Elements).Select(element => element.Name)) + " " + string.Join(' ', topic.Flows.Select(flow => flow.Title)));
            var body = Fold(string.Join(' ', topic.Summary.Select(block => block.Text)
                .Concat(topic.Areas.SelectMany(area => area.Elements).Select(element => element.Text))
                .Concat(topic.Flows.SelectMany(flow => flow.Blocks).Select(block => block.Text))));
            var score = 0;
            foreach (var word in words)
            {
                var wordScore = (title.Contains(word) ? 10 : 0) + (keywords.Contains(word) ? 6 : 0) + (elements.Contains(word) ? 4 : 0) + (body.Contains(word) ? 1 : 0);
                if (wordScore == 0) { score = 0; break; }
                score += wordScore;
            }
            if (score > 0) scored.Add((topic, score));
        }
        return scored.OrderByDescending(item => item.Score).ThenBy(item => item.Topic.Title, StringComparer.CurrentCultureIgnoreCase).Take(limit).Select(item => item.Topic).ToList();
    }

    /// <summary>Lower case without diacritics, for the search.</summary>
    public static string Fold(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark) builder.Append(character);
        return builder.ToString().ToLowerInvariant().Replace('ț', 't').Replace('ș', 's');
    }
}
