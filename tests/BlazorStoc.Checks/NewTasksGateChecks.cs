using BlazorStoc.Components.Shared;
using BlazorStoc.Modules;
using BlazorStoc.Modules.Tasks;
using BlazorStoc.Services;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorStoc.Checks;

// The window of the new tasks (NewTasksGate in the layout): lists the tasks not taken over, cannot be closed until every one is taken over, opens again when the administrator adds a task while
// the session is open, has the red bar and keeps the circuit on its page (ForcedDialogGate). The repository is a fake (the real one is checked against MariaDB in the section "User tasks").
public static class NewTasksGateChecks
{
    private sealed class FakeMine : IUserTaskRepository
    {
        public List<UserTask> Items { get; } = [];
        public Task<IReadOnlyList<UserTask>> GetMineAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<UserTask>>([.. Items]);
        public Task<UserTask> AcceptAsync(UserTask original, CancellationToken cancellationToken = default)
        {
            var updated = original with { AcceptedUtc = DateTime.UtcNow, AcceptedBy = "ana", Version = original.Version + 1 };
            Items[Items.FindIndex(item => item.Id == original.Id)] = updated;
            return Task.FromResult(updated);
        }
        public Task<IReadOnlyList<UserTask>> GetAllAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<UserTask>> GetForBeneficiaryAsync(int beneficiaryId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<UserTask?> GetAsync(int id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<UserTaskUser>> GetAssignableUsersAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<UserTask> CreateAsync(UserTaskInput input, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<UserTask> UpdateAsync(UserTask original, UserTaskInput input, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(UserTask original, string reason, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<UserTask> SetStatusAsync(UserTask original, UserTaskStatus status, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> GetWarningDaysAsync(CancellationToken cancellationToken = default) => Task.FromResult(3);
        public Task SetWarningDaysAsync(int days, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<UserTask> SetNoteAsync(UserTask original, string note, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task MoveInListAsync(UserTask task, int newPosition, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<UserTaskHistoryEntry>> GetHistoryAsync(int taskId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeAlerts : IUserTaskAlerts
    {
        public Task<UserTaskAlertSummary> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(UserTaskAlertSummary.Empty);
        public event Action? Changed;
        public void Raise() => Changed?.Invoke();
    }

    private static UserTask Item(int id, string title, bool accepted) =>
        new(id, 1, "Beneficiar", 7, "ana", true, title, "Descriere " + title, UserTaskStatus.Future, null, new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc), "admin",
            accepted ? new DateTime(2026, 11, 2, 10, 0, 0, DateTimeKind.Utc) : null, accepted ? "ana" : null, null, 0, 0, UserTaskPriority.Low);

    private static BunitContext Context(FakeMine tasks, FakeAlerts alerts, ForcedDialogGate gate, int? userId)
    {
        var registry = new ModuleRegistry();
        registry.Add(new TasksModule(), new ServiceCollection(), new ConfigurationBuilder().Build());
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddLogging();
        context.Services.AddSingleton(registry);
        context.Services.AddSingleton<IUserTaskRepository>(tasks);
        context.Services.AddSingleton<IUserTaskAlerts>(alerts);
        context.Services.AddSingleton<IAccessControl>(new TaskTestAccess(userId, "ana", false));
        context.Services.AddSingleton(gate);
        return context;
    }

    public static Task RunAsync(Action<bool, string> check)
    {
        // ---- two tasks waiting: the red window lists them, cannot be closed, the guard is on ----
        var tasks = new FakeMine();
        tasks.Items.AddRange([Item(1, "Primul", false), Item(2, "Al doilea", false), Item(3, "Deja preluat", true)]);
        var alerts = new FakeAlerts();
        var gate = new ForcedDialogGate();
        using var context = Context(tasks, alerts, gate, 7);
        var cut = context.Render<NewTasksGate>();
        check(cut.FindAll("#new-tasks-gate-table tbody tr").Count == 2 && !cut.Markup.Contains("Deja preluat", StringComparison.Ordinal), "New tasks window: lists only the tasks not taken over yet");
        check(cut.FindAll(".app-dialog-tone-alert").Count == 1, "New tasks window: has the red bar (tone alert)");
        check(cut.FindAll("[data-locked]").Count == 1 && cut.FindAll("[data-app-dialog-close]").Count == 0, "New tasks window: locked (no Escape / backdrop) and without the round close button");
        check(cut.Find("#new-tasks-gate-close").HasAttribute("disabled") && gate.Active, "New tasks window: \"Inchide\" is off and the navigation guard is on while tasks wait");

        // ---- taking over one task keeps it locked, the last one frees it ----
        cut.Find("#new-tasks-gate-table tbody tr button.row-icon").Click();
        cut.WaitForAssertion(() => { if (cut.FindAll("#new-tasks-gate-table tbody tr").Count != 1) throw new InvalidOperationException("one left expected"); });
        check(cut.Find("#new-tasks-gate-close").HasAttribute("disabled") && gate.Active, "New tasks window: with one task left the window stays locked");
        cut.Find("#new-tasks-gate-accept-all").Click();
        cut.WaitForAssertion(() => { if (cut.FindAll("#new-tasks-gate-close").Count == 0 || cut.Find("#new-tasks-gate-close").HasAttribute("disabled")) throw new InvalidOperationException("close expected"); });
        check(!gate.Active && tasks.Items.All(item => item.IsAccepted), "New tasks window: after taking over all, \"Inchide\" is on and the navigation guard is off");
        cut.Find("#new-tasks-gate-close").Click();
        check(cut.FindAll(".app-dialog-backdrop").Count == 0, "New tasks window: \"Inchide\" closes it once everything is taken over");

        // ---- the administrator adds a task while the session is open: the window opens by itself ----
        tasks.Items.Add(Item(4, "Nou din sesiune", false));
        alerts.Raise();
        cut.WaitForAssertion(() => { if (cut.FindAll("#new-tasks-gate-table tbody tr").Count != 1) throw new InvalidOperationException("window expected"); });
        check(gate.Active && cut.Markup.Contains("Nou din sesiune", StringComparison.Ordinal), "New tasks window: a task added while the session is open opens the window at once and locks it");

        // ---- an account without tasks (the administrator of the settings file) never sees it ----
        var other = new FakeMine();
        other.Items.Add(Item(9, "Altcuiva", false));
        var otherGate = new ForcedDialogGate();
        using var anonymous = Context(other, new FakeAlerts(), otherGate, null);
        var none = anonymous.Render<NewTasksGate>();
        check(none.FindAll(".app-dialog-backdrop").Count == 0 && !otherGate.Active, "New tasks window: an account that has no user row (administrator of the settings file) is never held");

        // ---- the structure: AppDialog Locked takes out the close button and marks the window; the guard is in the circuit; the script keeps Escape and the focus ----
        using var plain = new BunitContext();
        plain.JSInterop.Mode = JSRuntimeMode.Loose;
        var locked = plain.Render<AppDialog>(p => p.Add(x => x.Title, "T").Add(x => x.Locked, true).Add(x => x.Tone, "alert"));
        check(locked.FindAll("[data-app-dialog-close]").Count == 0 && locked.FindAll("[data-locked]").Count == 1 && locked.FindAll(".app-dialog-tone-alert").Count == 1, "AppDialog: Locked hides the close button and marks the window; Tone alert gives the red bar");
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "BlazorStoc.csproj"))) root = Path.GetDirectoryName(root);
        if (root is not null)
        {
            var script = File.ReadAllText(Path.Combine(root, "wwwroot", "app-dialog.js"));
            var tracker = File.ReadAllText(Path.Combine(root, "Components", "Shared", "SessionTracker.razor"));
            var layout = File.ReadAllText(Path.Combine(root, "Components", "Layout", "MainLayout.razor"));
            check(script.Contains("data-locked", StringComparison.Ordinal) && script.Contains("focusin", StringComparison.Ordinal), "New tasks window: the script ignores Escape and keeps the focus inside a locked window");
            check(tracker.Contains("ForcedGate.Active", StringComparison.Ordinal) && tracker.Contains("PreventNavigation", StringComparison.Ordinal), "New tasks window: the navigation guard of the circuit stops every move while the window is locked");
            check(layout.Contains("NewTasksGate", StringComparison.Ordinal), "New tasks window: it is in the layout, so it is over every page");
        }
        return Task.CompletedTask;
    }
}
