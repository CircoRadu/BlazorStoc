namespace BlazorStoc.Services;

// Copies a stream in blocks and reports the share copied (an uploaded file, whose size is known): the progress bar of the offer pages.
public static class StreamProgress
{
    public static async Task CopyAsync(Stream source, Stream destination, long total, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long copied = 0;
        progress?.Report(0);
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            copied += read;
            if (total > 0) progress?.Report((int)Math.Min(100, copied * 100 / total));
        }
        progress?.Report(100);
    }
}
