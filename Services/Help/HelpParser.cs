using System.Text.RegularExpressions;

namespace BlazorStoc.Help;

/// <summary>
/// Reads one .help file. Format (line based):
/// <code>
/// # Title
/// Ruta: /produse            (zero or more; patterns like /produse/{id})
/// Tab: Catalog              (optional: the label of the tab the topic describes)
/// Public: da                (optional: readable without signing in)
/// Cautare: extra words      (optional: for the search from the main page)
///
/// Summary paragraphs ([img:file.png | legend] puts a picture)
///
/// ## Elemente
/// ### Name of an area
/// - Element name | kind | description      (kind "iconita:edit" shows the real row-icon "edit")
///   a line that starts with spaces continues the description
///
/// ## Flux: Cum preiau o factura
/// 1. A step
/// Nota: a note
/// </code>
/// In texts **bold** and [[key]] / [[key|text]] (a link to another topic) are understood.
/// </summary>
public static partial class HelpParser
{
    public static readonly Regex KeyPattern = KeyRegex();

    [GeneratedRegex(@"^[a-z0-9]+([.\-][a-z0-9]+)*$")]
    private static partial Regex KeyRegex();

    [GeneratedRegex(@"^(\d+)\.\s+(.*)$")]
    private static partial Regex StepRegex();

    [GeneratedRegex(@"^\[img:\s*([^|\]]+?)\s*(?:\|\s*(.*?))?\s*\]$")]
    private static partial Regex ImageRegex();

    public static HelpParseResult Parse(string key, string text)
    {
        var errors = new List<string>();
        void Error(int line, string message) => errors.Add($"{key}.help, linia {line}: {message}");

        if (!KeyPattern.IsMatch(key)) errors.Add($"{key}.help: numele fisierului trebuie sa fie cu litere mici, cifre, '-' si '.'.");
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').TrimStart('﻿').Split('\n');

        string? title = null;
        var routes = new List<string>();
        string? tab = null;
        var isPublic = false;
        var keywords = "";
        var captures = new List<HelpCapture>();
        var summary = new List<HelpBlock>();
        var areas = new List<HelpArea>();
        var flows = new List<HelpFlow>();

        // The section being read: header (properties), summary, elements, flow.
        var section = "header";
        string? areaName = null;
        List<HelpElement>? areaElements = null;
        string? flowTitle = null;
        List<HelpBlock>? flowBlocks = null;

        void CloseArea()
        {
            if (areaName is not null && areaElements is not null)
            {
                if (areaElements.Count == 0) errors.Add($"{key}.help: zona \"{areaName}\" nu are elemente.");
                else areas.Add(new HelpArea(areaName, areaElements));
            }
            areaName = null; areaElements = null;
        }
        void CloseFlow()
        {
            if (flowTitle is not null && flowBlocks is not null)
            {
                if (flowBlocks.Count(block => block.Kind == HelpBlockKind.Step) < 2) errors.Add($"{key}.help: fluxul \"{flowTitle}\" are mai putin de 2 pasi.");
                else flows.Add(new HelpFlow(flowTitle, flowBlocks));
            }
            flowTitle = null; flowBlocks = null;
        }

        // Appends a text line to the last block (a continuation: the line starts with spaces) or adds a new block.
        static void AddBlock(List<HelpBlock> blocks, HelpBlock block) => blocks.Add(block);

        for (var index = 0; index < lines.Length; index++)
        {
            var number = index + 1;
            var raw = lines[index].TrimEnd();
            var line = raw.Trim();

            if (index == 0 || (title is null && line.Length > 0))
            {
                if (!line.StartsWith("# ")) { Error(number, "primul rand trebuie sa fie titlul: \"# Titlu\"."); continue; }
                title = line[2..].Trim();
                if (title.Length == 0) Error(number, "titlul este gol.");
                continue;
            }

            if (line.StartsWith("## "))
            {
                CloseArea(); CloseFlow();
                var heading = line[3..].Trim();
                if (heading.Equals("Elemente", StringComparison.OrdinalIgnoreCase)) section = "elements";
                else if (heading.StartsWith("Flux:", StringComparison.OrdinalIgnoreCase))
                {
                    section = "flow";
                    flowTitle = heading[5..].Trim();
                    flowBlocks = [];
                    if (flowTitle.Length == 0) Error(number, "fluxul nu are titlu.");
                }
                else { Error(number, $"sectiune necunoscuta \"{heading}\" (sunt: \"## Elemente\", \"## Flux: titlu\")."); section = "unknown"; }
                continue;
            }

            switch (section)
            {
                case "header":
                    if (line.Length == 0) { section = "summary"; break; }
                    var colon = line.IndexOf(':');
                    var property = colon > 0 ? line[..colon].Trim().ToLowerInvariant() : "";
                    var value = colon > 0 ? line[(colon + 1)..].Trim() : "";
                    if (property == "ruta") { if (value.StartsWith('/')) routes.Add(value); else Error(number, "Ruta trebuie sa inceapa cu '/'."); }
                    else if (property == "tab") tab = value;
                    else if (property == "public") isPublic = value.Equals("da", StringComparison.OrdinalIgnoreCase);
                    else if (property == "cautare") keywords = value;
                    else if (property == "captura")
                    {
                        var fields = value.Split('|', 4, StringSplitOptions.TrimEntries);
                        if (fields.Length < 2 || fields[0].Length == 0 || !KeyPattern.IsMatch(fields[0]) || !(fields[1].StartsWith('/') || fields[1].StartsWith("http://127.0.0.1:", StringComparison.Ordinal))) Error(number, "Captura se scrie \"Captura: id | /adresa | pasi (optional) | legenda (optional)\".");
                        else if (captures.Any(capture => capture.Id == fields[0])) Error(number, $"captura \"{fields[0]}\" apare de doua ori.");
                        else captures.Add(new HelpCapture(fields[0], fields[1], fields.Length > 2 ? fields[2].Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) : [], fields.Length > 3 && fields[3].Length > 0 ? fields[3] : null));
                    }
                    else { section = "summary"; goto case "summary"; }
                    break;

                case "summary":
                    if (line.Length == 0) break;
                    summary.Add(ToBlock(line, errors, key, number, allowStep: false));
                    break;

                case "elements":
                    if (line.Length == 0) break;
                    if (line.StartsWith("### "))
                    {
                        CloseArea();
                        areaName = line[4..].Trim();
                        areaElements = [];
                        break;
                    }
                    if (areaElements is null) { Error(number, "elementele se grupeaza sub o zona: \"### Nume zona\"."); break; }
                    if (line.StartsWith("- "))
                    {
                        var parts = line[2..].Split('|', 3, StringSplitOptions.TrimEntries);
                        if (parts.Length < 3 || parts.Any(part => part.Length == 0)) { Error(number, "elementul se scrie \"- Nume | tip | descriere\"."); break; }
                        string? icon = null;
                        var kind = parts[1];
                        if (kind.StartsWith("iconita:", StringComparison.OrdinalIgnoreCase)) { icon = kind[8..].Trim(); kind = "iconiță"; }
                        {
                            var (description, selector, imageId) = SplitOptions(parts[2]);
                            areaElements.Add(new HelpElement(parts[0], kind, icon, description, selector, imageId));
                        }
                    }
                    else if (raw.StartsWith(' ') && areaElements.Count > 0)
                    {
                        var last = areaElements[^1];
                        areaElements[^1] = last with { Text = last.Text + " " + line };
                    }
                    else Error(number, "linie neinteleasa in zona de elemente.");
                    break;

                case "flow":
                    if (line.Length == 0) break;
                    if (flowBlocks is null) break;
                    if (raw.StartsWith(' ') && flowBlocks.Count > 0 && flowBlocks[^1].Kind is HelpBlockKind.Step or HelpBlockKind.Note or HelpBlockKind.Paragraph)
                    {
                        flowBlocks[^1] = flowBlocks[^1] with { Text = flowBlocks[^1].Text + " " + line };
                        break;
                    }
                    AddBlock(flowBlocks, ToBlock(line, errors, key, number, allowStep: true));
                    break;
            }
        }
        CloseArea(); CloseFlow();

        if (title is null) errors.Add($"{key}.help: lipseste titlul.");
        if (summary.Count == 0 || !summary.Any(block => block.Kind == HelpBlockKind.Paragraph)) errors.Add($"{key}.help: lipseste rezumatul (un paragraf dupa antet).");
        if (areas.Count == 0) errors.Add($"{key}.help: lipsesc elementele ferestrei (\"## Elemente\").");
        if (flows.Count == 0) errors.Add($"{key}.help: lipseste fluxul de operare (\"## Flux: ...\").");
        if (tab is not null && routes.Count == 0) errors.Add($"{key}.help: Tab fara Ruta.");

        return errors.Count > 0 || title is null
            ? new HelpParseResult(null, errors)
            : new HelpParseResult(new HelpTopic(key, title, routes, tab, isPublic, keywords, summary, areas, flows, captures), errors);
    }

    [GeneratedRegex(@"^(.*?)\s*\|\s*(sel|img):\s*([^|]+?)\s*$")]
    private static partial Regex OptionRegex();

    // "description | sel: .css | img: id": the optional tail of an element line.
    private static (string Text, string? Selector, string? ImageId) SplitOptions(string text)
    {
        string? selector = null, imageId = null;
        while (OptionRegex().Match(text) is { Success: true } match)
        {
            if (match.Groups[2].Value == "sel") selector ??= match.Groups[3].Value; else imageId ??= match.Groups[3].Value;
            text = match.Groups[1].Value;
        }
        return (text, selector, imageId);
    }

    private static HelpBlock ToBlock(string line, List<string> errors, string key, int number, bool allowStep)
    {
        var image = ImageRegex().Match(line);
        if (image.Success) return new HelpBlock(HelpBlockKind.Image, image.Groups[1].Value, image.Groups[2].Success ? image.Groups[2].Value : null);
        if (allowStep && StepRegex().Match(line) is { Success: true } step) return new HelpBlock(HelpBlockKind.Step, step.Groups[2].Value);
        if (line.StartsWith("Nota:", StringComparison.OrdinalIgnoreCase)) return new HelpBlock(HelpBlockKind.Note, line[5..].Trim());
        return new HelpBlock(HelpBlockKind.Paragraph, line);
    }
}
