namespace BlazorStoc.Services;

// Keeps a page in step with changes made by other sessions or applications (TODO Task 8).
//  - An event that affects the page refreshes only the data the page shows, after a short pause that merges bursts.
//  - A form or dialog that is open is never replaced: the page shows a notice instead and the user reloads on purpose.
//  - A slow periodic refresh is the fallback if a notification was lost; it also picks up a pending change once the
//    form has been closed.
public sealed class LiveRefresh : IDisposable
{
    private readonly Guid ownOrigin;
    private readonly Func<ChangeEvent, bool> affects;
    private readonly Func<Task> refreshAsync;
    private readonly Func<bool> isBusy;
    private readonly Func<Task> changedAsync;
    private readonly TimeSpan debounce;
    private readonly IDisposable subscription;
    private readonly CancellationTokenSource lifetime = new();
    private readonly object gate = new();
    private CancellationTokenSource? scheduled;

    public LiveRefresh(IChangeFeed feed, Guid ownOrigin, Func<ChangeEvent, bool> affects, Func<Task> refreshAsync,
        Func<bool> isBusy, Func<Task> changedAsync, TimeSpan? debounce = null, TimeSpan? fallbackInterval = null)
    {
        this.ownOrigin = ownOrigin;
        this.affects = affects;
        this.refreshAsync = refreshAsync;
        this.isBusy = isBusy;
        this.changedAsync = changedAsync;
        this.debounce = debounce ?? TimeSpan.FromMilliseconds(300);
        subscription = feed.Subscribe(OnChangeAsync);
        if (fallbackInterval is { } interval && interval > TimeSpan.Zero) _ = FallbackAsync(interval);
    }

    // True while a change arrived that could not be applied because a form or dialog is open.
    public bool HasPendingChange { get; private set; }

    // The "Reîncarcă" action of the notice: the user chose to refresh the data behind the open form.
    public async Task ReloadPendingAsync()
    {
        HasPendingChange = false;
        await RunRefreshAsync().ConfigureAwait(false);
    }

    private Task OnChangeAsync(ChangeEvent change)
    {
        if (lifetime.IsCancellationRequested || change.Origin == ownOrigin || !affects(change)) return Task.CompletedTask;
        if (isBusy())
        {
            HasPendingChange = true;
            return changedAsync();
        }
        CancellationTokenSource source;
        lock (gate)
        {
            scheduled?.Cancel();
            scheduled = source = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        }
        _ = RefreshAfterPauseAsync(source);
        return Task.CompletedTask;
    }

    private async Task RefreshAfterPauseAsync(CancellationTokenSource source)
    {
        try
        {
            await Task.Delay(debounce, source.Token).ConfigureAwait(false);
            if (isBusy()) { HasPendingChange = true; await changedAsync().ConfigureAwait(false); return; }
            HasPendingChange = false;
            await RunRefreshAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
    }

    private async Task FallbackAsync(TimeSpan interval)
    {
        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(lifetime.Token).ConfigureAwait(false))
            {
                if (isBusy()) continue;
                HasPendingChange = false;
                await RunRefreshAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
    }

    // A failing refresh never breaks the page: the next event or the periodic tick tries again.
    private async Task RunRefreshAsync()
    {
        try { await refreshAsync().ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (Exception) { }
    }

    public void Dispose()
    {
        lifetime.Cancel();
        subscription.Dispose();
        lock (gate) scheduled?.Cancel();
        lifetime.Dispose();
    }
}
