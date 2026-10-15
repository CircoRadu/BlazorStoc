using BlazorStoc.Services;

namespace BlazorStoc.Modules.Ocr;

// The worker side: reads one job folder, does the work with the same engine classes the application would use in-process, writes the result and returns.
// Anything that is not an answer of the engine (a native crash, an out-of-memory, an unexpected exception) ends the process with a failing exit code and no result.
public static class OcrWorker
{
    public static async Task<int> RunAsync(string jobFolder)
    {
        try
        {
            var request = OcrJson.Read<OcrJobRequest>(await File.ReadAllTextAsync(Path.Combine(jobFolder, OcrFiles.Request)).ConfigureAwait(false));
            var tessdata = new FixedTessdataPath(request.TessdataPath);
            var pdfBytes = await File.ReadAllBytesAsync(Path.Combine(jobFolder, OcrFiles.Input)).ConfigureAwait(false);
            switch (request.Operation)
            {
                case OcrOperations.InvoiceRead:
                {
                    var read = await new InvoicePdfReader(tessdata).ReadAsync(new MemoryStream(pdfBytes)).ConfigureAwait(false);
                    for (var index = 0; index < read.PagePreviews.Count; index++)
                        await File.WriteAllBytesAsync(Path.Combine(jobFolder, OcrFiles.Preview(index)), read.PagePreviews[index]).ConfigureAwait(false);
                    await WriteResultAsync(jobFolder, read.Document).ConfigureAwait(false);
                    break;
                }
                case OcrOperations.InvoiceReread:
                {
                    var document = OcrJson.Read<InvoiceDocument>(await File.ReadAllTextAsync(Path.Combine(jobFolder, OcrFiles.Document)).ConfigureAwait(false));
                    var reread = await new InvoicePdfReader(tessdata).RereadNumbersAsync(new InvoiceReadResult(document, [], pdfBytes)).ConfigureAwait(false);
                    await WriteResultAsync(jobFolder, reread.Document).ConfigureAwait(false);
                    break;
                }
                case OcrOperations.InventoryScan:
                {
                    using var service = new InventoryPickupOcrService(tessdata);
                    var scan = await service.ScanAsync(new MemoryStream(pdfBytes)).ConfigureAwait(false);
                    await WriteResultAsync(jobFolder, scan).ConfigureAwait(false);
                    break;
                }
                default:
                    Console.Error.WriteLine($"Unknown OCR operation: {request.Operation}");
                    return 2;
            }
            return 0;
        }
        catch (InvoiceAnalysisException exception) { return await WriteErrorAsync(jobFolder, OcrJobError.Invoice, exception.Message).ConfigureAwait(false); }
        catch (InventoryPickupOcrException exception) { return await WriteErrorAsync(jobFolder, OcrJobError.Inventory, exception.Message).ConfigureAwait(false); }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"{exception.GetType().Name}: {exception.Message}");
            return 1;
        }
    }

    // The result is written under a temporary name and moved into place, so a worker killed while writing never leaves a half result.
    private static async Task WriteResultAsync<T>(string jobFolder, T value)
    {
        var temporary = Path.Combine(jobFolder, OcrFiles.Result + ".tmp");
        await File.WriteAllTextAsync(temporary, OcrJson.Write(value)).ConfigureAwait(false);
        File.Move(temporary, Path.Combine(jobFolder, OcrFiles.Result), overwrite: true);
    }

    private static async Task<int> WriteErrorAsync(string jobFolder, string kind, string message)
    {
        await File.WriteAllTextAsync(Path.Combine(jobFolder, OcrFiles.Refusal), OcrJson.Write(new OcrJobError(kind, message))).ConfigureAwait(false);
        return 0;
    }
}
