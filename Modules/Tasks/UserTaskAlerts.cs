using BlazorStoc.Services;

namespace BlazorStoc.Modules.Tasks;

/// <summary>
/// What needs the attention of the signed-in user about their tasks: the alerts (not taken over, past the deadline, due soon) and how many tasks were not taken over yet.
/// HasUser is false for an account without tasks (the administrator of the settings file) and while the module is out.
/// </summary>
public sealed record UserTaskAlertSummary(bool HasUser, IReadOnlyList<UserTaskAlert> Alerts, int NotAccepted, int WarningDays)
{
    public int Total => Alerts.Count;
    public static UserTaskAlertSummary Empty { get; } = new(false, [], 0, UserTaskRules.DefaultWarningDays);
}

/// <summary>The alerts of the signed-in user, for the menu of the account, the triangle of the menu and the tab "Taskuri" of the Notifications page (nothing is stored: computed from the tasks).</summary>
public interface IUserTaskAlerts
{
    Task<UserTaskAlertSummary> GetAsync(CancellationToken cancellationToken = default);
    /// <summary>Raised (from any thread) when a task changed in this application; whoever shows a count reads it again.</summary>
    event Action? Changed;
}

internal sealed class UserTaskAlerts : IUserTaskAlerts, IDisposable
{
    private readonly IUserTaskRepository tasks;
    private readonly IAccessControl access;
    private readonly TimeProvider clock;
    private readonly IDisposable subscription;

    public UserTaskAlerts(IUserTaskRepository tasks, IAccessControl access, IChangeFeed feed, TimeProvider? clock = null)
    {
        this.tasks = tasks;
        this.access = access;
        this.clock = clock ?? TimeProvider.System;
        subscription = feed.Subscribe(change =>
        {
            if (change.EntityType == AuditEntities.UserTask) Changed?.Invoke();
            return Task.CompletedTask;
        });
    }

    public event Action? Changed;

    public async Task<UserTaskAlertSummary> GetAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (await access.GetUserIdAsync(cancellationToken).ConfigureAwait(false) is null) return UserTaskAlertSummary.Empty;
            var mine = await tasks.GetMineAsync(cancellationToken).ConfigureAwait(false);
            var days = await tasks.GetWarningDaysAsync(cancellationToken).ConfigureAwait(false);
            var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
            return new(true, UserTaskRules.Alerts(mine, today, days), mine.Count(task => task.IsActive && !task.IsAccepted), days);
        }
        catch (ModuleUnavailableException) { return UserTaskAlertSummary.Empty; }
        catch (Exception exception) when (exception is not OperationCanceledException) { return UserTaskAlertSummary.Empty; }
    }

    public void Dispose() => subscription.Dispose();
}

internal sealed class UnavailableUserTaskAlerts : IUserTaskAlerts
{
    public Task<UserTaskAlertSummary> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(UserTaskAlertSummary.Empty);
    public event Action? Changed { add { } remove { } }
}
