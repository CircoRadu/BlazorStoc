using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace BlazorStoc.Services;

// Identifiers only: the notification never carries names, texts, passwords or file contents.
public sealed record ChangeNotification(string EntityType, string Action, string EntityId,
    int? ProjectId, int? ObservationId, int? BeneficiaryId, DateTime OccurredUtc);

// Signed-in clients connect to /hubs/changes and receive every change as a "changed" message (TODO Task 8).
// The server-rendered pages of this application share the process with the feed and subscribe to it directly.
[Authorize]
public sealed class ChangesHub : Hub
{
    public const string Path = "/hubs/changes";
    public const string Method = "changed";
}

// Forwards every event of the feed (local and trigger-detected) to the connected SignalR clients.
public sealed class SignalRChangeBroadcaster(IChangeFeed feed, IHubContext<ChangesHub> hub, ILogger<SignalRChangeBroadcaster> logger)
    : IHostedService
{
    private IDisposable? subscription;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        subscription = feed.Subscribe(SendAsync);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        subscription?.Dispose();
        return Task.CompletedTask;
    }

    private async Task SendAsync(ChangeEvent change)
    {
        try
        {
            await hub.Clients.All.SendAsync(ChangesHub.Method, new ChangeNotification(change.EntityType, change.Action, change.EntityId,
                change.ProjectId, change.ObservationId, change.BeneficiaryId, change.OccurredUtc)).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogWarning("A change notification could not be sent to SignalR clients ({ErrorType}).", exception.GetType().Name);
        }
    }
}
