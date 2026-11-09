namespace BlazorStoc.Services;

// The pictures attached to a report of an error or to a proposal (a capture of the page, a picture pasted with Ctrl+V, a chosen file): shared rules, so the two modules do not keep a copy each.
public sealed record ReportImage(string Kind, string FileName, byte[] Content);

public static class ReportImageRules
{
    public const int MaxImages = 12, MaxImageBytes = 5 * 1024 * 1024;
    public const string KindScreenshot = "screenshot", KindPasted = "pasted", KindFile = "file";

    /// <summary>The content type is taken from the bytes (a picture), never from what the browser declared.</summary>
    public static string? DetectImageType(byte[] bytes)
    {
        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) return "image/png";
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return "image/jpeg";
        if (bytes.Length >= 6 && bytes[0] == 'G' && bytes[1] == 'I' && bytes[2] == 'F') return "image/gif";
        if (bytes.Length >= 12 && bytes[0] == 'R' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == 'F' && bytes[8] == 'W' && bytes[9] == 'E' && bytes[10] == 'B' && bytes[11] == 'P') return "image/webp";
        return null;
    }

    public static string Extension(string contentType) => contentType switch { "image/png" => ".png", "image/jpeg" => ".jpg", "image/gif" => ".gif", "image/webp" => ".webp", _ => ".bin" };

    public static string SafeName(string? name, string fallback)
    {
        var cleaned = new string((name ?? "").Where(c => !Path.GetInvalidFileNameChars().Contains(c) && c != ' ').ToArray());
        return cleaned.Length == 0 ? fallback : cleaned.Length > 80 ? cleaned[..80] : cleaned;
    }
}
