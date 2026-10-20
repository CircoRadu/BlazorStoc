using System.Text.Json;
using BlazorStoc.Services;

namespace BlazorStoc.Modules.Ocr;

// The contract between the application and its OCR worker. A job is a folder: the application writes request.json, input.pdf (and document.json for a
// re-reading), starts "BlazorStoc --ocr-worker <folder>" and reads what the worker left in it: result.json (+ preview-N.png) or refuz.json.
// Everything is plain JSON/PNG, so a worker that dies halfway leaves nothing the application would take for a result.

internal static class OcrOperations
{
    public const string InvoiceRead = "invoice-read";
    public const string InvoiceReread = "invoice-reread";
    public const string InventoryScan = "inventory-scan";
}

internal static class OcrFiles
{
    public const string Request = "request.json";
    public const string Input = "input.pdf";
    public const string Document = "document.json";
    public const string Result = "result.json";
    public const string Refusal = "refuz.json";
    public static string Preview(int index) => $"preview-{index}.png";
}

internal sealed record OcrJobRequest(string Operation, string TessdataPath);

/// <summary>An answer of the engine for the user (a file that is not a PDF, too large...): the worker finished normally; Kind tells which exception to raise.</summary>
internal sealed record OcrJobError(string Kind, string Message)
{
    public const string Invoice = "invoice-analysis";
    public const string Inventory = "inventory-pickup";
}

/// <summary>The worker did not deliver: it died, hung past the time limit, or left no usable result. A failure of the module (counted by the circuit breaker).</summary>
public sealed class OcrWorkerException(string message, Exception? inner = null) : Exception(message, inner), BlazorStoc.Modules.IBelongsToModule
{
    public string ModuleId => OcrRules.ModuleId;
}

public static class OcrRules
{
    public const string CrashedMessage = "Citirea fisierului a esuat: procesul de recunoastere s-a oprit. Reincearca; daca se repeta, anunta administratorul.";
    public const string TimeoutMessage = "Citirea fisierului a durat prea mult si a fost oprita. Incearca un fisier mai mic sau reincearca.";
    public const string UnavailableMessage = "Recunoasterea documentelor este indisponibila momentan. Reincearca peste cateva momente.";
    public const string ModuleId = "ocr";
}

internal static class OcrJson
{
    // Get-only members (AllWords, Right, ...) are computed from the others and are not written; records are rebuilt through their constructors.
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { IgnoreReadOnlyProperties = true, WriteIndented = false };

    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json, Options) ?? throw new JsonException("empty document");
}

/// <summary>The Tessdata folder given by path (the worker has no web host to ask).</summary>
internal sealed class FixedTessdataPath(string path) : IWebHostEnvironmentTessdataPath
{
    public string Path { get; } = path;
}
