using System.Globalization;
using System.Text;

namespace BlazorStoc.Services;

public static class TextNormalization
{
    public static string ForStorage(string? value)
    {
        var text = (value ?? "").Trim().Normalize(NormalizationForm.FormD);
        var result = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category is not UnicodeCategory.NonSpacingMark and not UnicodeCategory.SpacingCombiningMark and not UnicodeCategory.EnclosingMark)
                result.Append(character);
        }
        return result.ToString().Normalize(NormalizationForm.FormC);
    }

    public static string ForObjectNameOrCode(string? value)
    {
        var text = ForStorage(value);
        var result = new StringBuilder(text.Length);
        var previousWasSpace = false;
        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                if (!previousWasSpace) result.Append(' ');
                previousWasSpace = true;
                continue;
            }

            result.Append(character);
            previousWasSpace = false;
        }
        return result.ToString();
    }

    public static string UniquenessKey(string? value) => ForObjectNameOrCode(value).ToUpperInvariant();

    public static bool SameUniqueValue(string? left, string? right) =>
        string.Equals(UniquenessKey(left), UniquenessKey(right), StringComparison.Ordinal);
}
