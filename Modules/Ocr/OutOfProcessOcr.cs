using BlazorStoc.Services;

namespace BlazorStoc.Modules.Ocr;

// The application side of the OCR: the same interfaces the pages already use (IInvoicePdfReader, IInvoiceNumberRereader, IInventoryPickupOcrService), served by
// a worker process through the circuit breaker of the "ocr" module. A worker that dies or hangs becomes the message the pages already show for a file that
// could not be read; after repeated failures the module is taken out for a while and the pages say that recognition is unavailable. Nothing else is affected.

internal static class OcrGuard
{
    public static async Task<T> RunAsync<T>(ModuleRegistry modules, Func<Task<T>> operation, Func<string, Exception> toException)
    {
        try { return await modules.RunAsync(OcrRules.ModuleId, operation).ConfigureAwait(false); }
        catch (OcrWorkerException exception) { throw toException(exception.Message); }
        catch (ModuleUnavailableException) { throw toException(OcrRules.UnavailableMessage); }
    }
}

internal sealed class OutOfProcessInvoicePdfReader(OcrWorkerRunner runner, ModuleRegistry modules, IWebHostEnvironmentTessdataPath tessdata) : IInvoicePdfReader, IInvoiceNumberRereader
{
    private static Exception Failed(string message) => new InvoiceAnalysisException(message);

    public async Task<InvoiceReadResult> ReadAsync(Stream pdfStream, CancellationToken cancellationToken = default)
    {
        using var buffered = new MemoryStream();
        await pdfStream.CopyToAsync(buffered, cancellationToken).ConfigureAwait(false);
        // The other refusals are the worker's (same messages), but a file over the limit is not worth a process.
        if (buffered.Length > InvoiceAnalysisRules.MaxFileSizeBytes) throw new InvoiceAnalysisException(InvoiceAnalysisRules.TooLargeMessage);
        var bytes = buffered.ToArray();
        var (document, previews) = await OcrGuard.RunAsync(modules, () => runner.RunAsync(OcrOperations.InvoiceRead, tessdata.Path, bytes, null, folder =>
        {
            var read = OcrJson.Read<InvoiceDocument>(File.ReadAllText(Path.Combine(folder, OcrFiles.Result)));
            var pictures = new List<byte[]>();
            for (var index = 0; File.Exists(Path.Combine(folder, OcrFiles.Preview(index))); index++) pictures.Add(File.ReadAllBytes(Path.Combine(folder, OcrFiles.Preview(index))));
            return (read, pictures);
        }, cancellationToken), Failed).ConfigureAwait(false);
        return new InvoiceReadResult(document, previews, bytes);
    }

    public async Task<InvoiceReadResult> RereadNumbersAsync(InvoiceReadResult read, CancellationToken cancellationToken = default)
    {
        // Same precondition as the engine's: only a document with OCR pages has numbers to read again (no process for a text-layer PDF).
        if (read.SourcePdf is null || read.Document.Pages.All(page => page.Source != InvoiceSources.Ocr)) return read;
        var json = OcrJson.Write(read.Document);
        var document = await OcrGuard.RunAsync(modules, () => runner.RunAsync(OcrOperations.InvoiceReread, tessdata.Path, read.SourcePdf, json,
            folder => OcrJson.Read<InvoiceDocument>(File.ReadAllText(Path.Combine(folder, OcrFiles.Result))), cancellationToken), Failed).ConfigureAwait(false);
        return read with { Document = document };
    }
}

internal sealed class OutOfProcessInventoryPickupOcrService(OcrWorkerRunner runner, ModuleRegistry modules, IWebHostEnvironmentTessdataPath tessdata) : IInventoryPickupOcrService
{
    public async Task<InventoryPickupScanResult> ScanAsync(Stream pdfStream, CancellationToken cancellationToken = default)
    {
        using var buffered = new MemoryStream();
        await pdfStream.CopyToAsync(buffered, cancellationToken).ConfigureAwait(false);
        if (buffered.Length > InventoryPickupOcrRules.MaxFileSizeBytes) throw new InventoryPickupOcrException(InventoryPickupOcrRules.TooLargeMessage);
        var bytes = buffered.ToArray();
        return await OcrGuard.RunAsync(modules, () => runner.RunAsync(OcrOperations.InventoryScan, tessdata.Path, bytes, null,
            folder => OcrJson.Read<InventoryPickupScanResult>(File.ReadAllText(Path.Combine(folder, OcrFiles.Result))), cancellationToken),
            message => new InventoryPickupOcrException(message)).ConfigureAwait(false);
    }
}

// Stand-ins while the module is switched off or did not start: the pages that inject the readers are still created and say that recognition is out.
internal sealed class UnavailableInvoicePdfReader : IInvoicePdfReader
{
    public Task<InvoiceReadResult> ReadAsync(Stream pdfStream, CancellationToken cancellationToken = default) =>
        Task.FromException<InvoiceReadResult>(new InvoiceAnalysisException(OcrRules.UnavailableMessage));
}

internal sealed class UnavailableInventoryPickupOcrService : IInventoryPickupOcrService
{
    public Task<InventoryPickupScanResult> ScanAsync(Stream pdfStream, CancellationToken cancellationToken = default) =>
        Task.FromException<InventoryPickupScanResult>(new InventoryPickupOcrException(OcrRules.UnavailableMessage));
}
