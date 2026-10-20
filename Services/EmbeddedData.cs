using System.Reflection;

namespace BlazorStoc.Services;

// Data files kept next to the code in Assets/Data and embedded in the assembly (see the EmbeddedResource item in the project file), so they
// travel with the application and need no path at run time.
internal static class EmbeddedData
{
    public static string ReadText(string fileName)
    {
        var assembly = typeof(EmbeddedData).Assembly;
        using var stream = assembly.GetManifestResourceStream($"BlazorStoc.Data.{fileName}")
            ?? throw new InvalidOperationException($"Resursa încorporată BlazorStoc.Data.{fileName} lipsește din aplicație.");
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        return reader.ReadToEnd();
    }

    // "key TAB text" per line; empty lines and lines starting with # are ignored; a repeated key keeps the last text. Keys ignore case.
    public static Dictionary<string, string> ReadTable(string fileName)
    {
        var table = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in ReadText(fileName).Split('\n'))
        {
            var text = line.TrimEnd('\r');
            if (text.Length == 0 || text[0] == '#') continue;
            var tab = text.IndexOf('\t');
            if (tab <= 0) throw new InvalidOperationException($"Linie nevalidă în {fileName}: „{text}”.");
            table[text[..tab]] = text[(tab + 1)..];
        }
        return table;
    }
}