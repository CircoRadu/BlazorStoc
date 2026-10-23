using System.Globalization;
using System.Text;

namespace BlazorStoc.Modules.ActivityImport;

// The words that show a ticket also held maintenance. They are set for each ticket type: when what was done in the ticket (the resolution of the closing event) contains one of them the
// ticket is also the maintenance of the point, and when the beneficiary has an active maintenance contract covering it, it is recorded as a maintenance intervention (which moves the next
// due date). Tickets are also closed from the mobile application, where words come out incomplete or misspelled, so there are two levels: an EXACT match (the word, as a part of the text, so
// "mentenan" finds "mentenanța" and "mentenante") counts at once; an APPROXIMATE one (a typing mistake, a missing or doubled letter, a word cut short, a space inside the word) is only proposed
// to the administrator, who confirms or rejects it. Case, punctuation and diacritics never matter, and digits or symbols typed in place of a letter (a phone keyboard) count as mistakes.

public enum KeywordMatchKind { None, Exact, Approximate }

/// <summary>What a search of the text found: the keyword (as typed in the settings) and, for an approximate match, how the text wrote it.</summary>
public sealed record KeywordMatch(KeywordMatchKind Kind, string Word = "", string Found = "")
{
    public static KeywordMatch None { get; } = new(KeywordMatchKind.None);
}

public static class ActivityKeywords
{
    public const int MaximumKeywords = 20;
    public const int MinimumLength = 3;
    public const int MaximumLength = 60;
    public const int MaximumTotal = 500;
    /// <summary>A word of fewer letters than this is only matched exactly (a mistake in a short word could be another word).</summary>
    public const int MinimumFuzzyLength = 5;

    /// <summary>The text as it is compared: lower case, no diacritics, every run of anything that is not a letter or a digit (punctuation, white space) as one space.</summary>
    public static string Normalize(string? text)
    {
        var decomposed = (text ?? "").Normalize(NormalizationForm.FormD);
        var result = new StringBuilder(decomposed.Length);
        var space = true;
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c)) { result.Append(char.ToLowerInvariant(c)); space = false; }
            else if (!space) { result.Append(' '); space = true; }
        }
        return result.ToString().TrimEnd();
    }

    /// <summary>The words of what was typed (separated by comma, semicolon or new line), trimmed, without repeats (compared normalized); the reason when they are not valid.</summary>
    public static IReadOnlyList<string> Parse(string? text, out string? error)
    {
        error = null;
        var words = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var part in (text ?? "").Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var word = string.Join(' ', part.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (word.Length == 0) continue;
            var normalized = Normalize(word);
            if (normalized.Length < MinimumLength) { error = $"Cuvântul cheie „{word}” este prea scurt (cel puțin {MinimumLength} litere)."; return []; }
            if (word.Length > MaximumLength) { error = $"Un cuvânt cheie poate avea cel mult {MaximumLength} de caractere."; return []; }
            if (seen.Add(normalized)) words.Add(word);
        }
        if (words.Count > MaximumKeywords) { error = $"Cel mult {MaximumKeywords} de cuvinte cheie pe tip."; return []; }
        if (Join(words).Length > MaximumTotal) { error = "Cuvintele cheie sunt prea lungi (cel mult 500 de caractere în total)."; return []; }
        return words;
    }

    public static string Join(IEnumerable<string> words) => string.Join(", ", words);

    /// <summary>The first word found exactly in the text (as typed), or null.</summary>
    public static string? Find(string? text, IReadOnlyList<string>? words)
    {
        if (words is null || words.Count == 0) return null;
        var haystack = Normalize(text);
        return haystack.Length == 0 ? null : words.FirstOrDefault(word => haystack.Contains(Normalize(word), StringComparison.Ordinal));
    }

    /// <summary>The most mistakes accepted in a word of this many letters.</summary>
    public static int MaximumDistance(int length) => length < MinimumFuzzyLength ? 0 : length <= 7 ? 1 : length <= 11 ? 2 : 3;

    /// <summary>
    /// An exact match when any of the words is in the text; otherwise an approximate one when some run of up to (words of the keyword + 1) consecutive words of the text, written together
    /// (so a space inside the word does not matter), is within the allowed mistakes of the keyword, or is the beginning of it (a word cut short, at least 60% of it).
    /// </summary>
    public static KeywordMatch Match(string? text, IReadOnlyList<string>? words)
    {
        if (words is null || words.Count == 0) return KeywordMatch.None;
        var plain = Normalize(text);
        if (plain.Length == 0) return KeywordMatch.None;
        if (Find(text, words) is { } exact) return new(KeywordMatchKind.Exact, exact, exact);
        // The approximate search works on the words as written (split by white space only): on a phone keyboard a letter is often replaced by a digit or a symbol next to it ("m3ntenan@a"),
        // so each run of words is also read with the symbols dropped and with digits and symbols turned into the letters they resemble.
        var tokens = Fold(text).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        foreach (var word in words)
        {
            var key = Normalize(word).Replace(" ", "", StringComparison.Ordinal);
            if (key.Length < MinimumFuzzyLength) continue;
            var keyTokens = Normalize(word).Split(' ').Length;
            for (var size = 1; size <= keyTokens + 1 && size <= tokens.Length; size++)
                for (var start = 0; start + size <= tokens.Length; start++)
                {
                    var window = tokens.Skip(start).Take(size).ToList();
                    if (Variants(string.Concat(window)).Any(variant => IsApproximate(variant, key))) return new(KeywordMatchKind.Approximate, word, string.Join(' ', window));
                }
        }        return KeywordMatch.None;
    }

    /// <summary>Lower case without diacritics; everything else (digits, symbols, spaces) as it is.</summary>
    private static string Fold(string? text)
    {
        var result = new StringBuilder();
        foreach (var c in (text ?? "").Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) result.Append(char.ToLowerInvariant(c));
        return result.ToString();
    }

    // The letters a digit or a symbol of a phone keyboard may stand for (1 may be an i or an l).
    private static readonly Dictionary<char, char[]> Lookalikes = new()
    {
        ['0'] = ['o'], ['1'] = ['i', 'l'], ['!'] = ['i'], ['|'] = ['l'], ['3'] = ['e'], ['€'] = ['e'], ['4'] = ['a'], ['@'] = ['a'], ['5'] = ['s'], ['$'] = ['s'], ['7'] = ['t'], ['8'] = ['b'], ['9'] = ['g']
    };

    /// <summary>The ways a written word can be read: only its letters and digits, and with each digit or symbol turned into the letter it resembles (or dropped when it resembles none).</summary>
    private static IEnumerable<string> Variants(string written)
    {
        var variants = new HashSet<string>(StringComparer.Ordinal) { new string(written.Where(char.IsLetterOrDigit).ToArray()) };
        foreach (var index in new[] { 0, 1 })
        {
            var text = new StringBuilder();
            foreach (var c in written)
                if (Lookalikes.TryGetValue(c, out var letters)) text.Append(letters[Math.Min(index, letters.Length - 1)]);
                else if (char.IsLetter(c)) text.Append(c);
            variants.Add(text.ToString());
        }
        return variants.Where(variant => variant.Length > 0);
    }

    private static bool IsApproximate(string written, string key)
    {
        if (written.Length < MinimumFuzzyLength - 1) return false;
        var allowed = MaximumDistance(key.Length);
        if (Math.Abs(written.Length - key.Length) <= allowed && Distance(written, key) <= allowed) return true;
        // A word cut short: the beginning of the keyword (with at most one mistake), at least 60% of it.
        if (written.Length < key.Length && written.Length >= MinimumFuzzyLength && written.Length * 100 >= key.Length * 60)
            return Distance(written, key[..written.Length]) <= 1;
        return false;
    }

    /// <summary>Edit distance counting an insertion, a deletion, a changed letter and two neighbouring letters swapped as one mistake each.</summary>
    public static int Distance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1]) d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
            }
        return d[a.Length, b.Length];
    }
}
