using BlazorStoc.Modules.Tasks;
using BlazorStoc.Services;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorStoc.Checks;

// The tables of the administrator (Taskuri utilizator): the tab of the tasks nobody took over, a tab for every user who has tasks (numbered active tasks, finished ones below, the switch that hides
// them), the handles of the order only with the right to edit and without filters. The repository is a fake (the real one is checked against MariaDB in the section "User tasks").
public static class UserTaskTabsChecks
{
    private sealed class FakeTasks(List<UserTask> items) : IUserTaskRepository
    {
        public List<(int TaskId, int Position)> Moves { get; } = [];
        public List<UserTask> Items { get; } = items;
        public Task<IReadOnlyList<UserTask>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<UserTask>>([.. Items]);
        public Task<IReadOnlyList<UserTask>> GetForBeneficiaryAsync(int beneficiaryId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<UserTask>>([]);
        public Task<IReadOnlyList<UserTask>> GetMineAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<UserTask>>([]);
        public Task<UserTask?> GetAsync(int id, CancellationToken cancellationToken = default) => Task.FromResult(Items.FirstOrDefault(item => item.Id == id));
        public Task<IReadOnlyList<UserTaskUser>> GetAssignableUsersAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<UserTaskUser>>([]);
        public Task<UserTask> CreateAsync(UserTaskInput input, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<UserTask> UpdateAsync(UserTask original, UserTaskInput input, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(UserTask original, string reason, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<UserTask> SetStatusAsync(UserTask original, UserTaskStatus status, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<UserTask> AcceptAsync(UserTask original, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> GetWarningDaysAsync(CancellationToken cancellationToken = default) => Task.FromResult(3);
        public Task SetWarningDaysAsync(int days, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<UserTask> SetNoteAsync(UserTask original, string note, CancellationToken cancellationToken = default)
        {
            var updated = original with { ProgressNote = note.Trim(), Version = original.Version + 1 };
            Items[Items.FindIndex(item => item.Id == original.Id)] = updated;
            Notes.Add((original.Id, updated.ProgressNote));
            return Task.FromResult(updated);
        }
        public List<(int TaskId, string Note)> Notes { get; } = [];
        public Task MoveInListAsync(UserTask task, int newPosition, CancellationToken cancellationToken = default) { Moves.Add((task.Id, newPosition)); return Task.CompletedTask; }
        public Task<IReadOnlyList<UserTaskHistoryEntry>> GetHistoryAsync(int taskId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<UserTaskHistoryEntry>>(
        [new(new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc), "admin", AuditActions.CreateUserTask, ""), new(new DateTime(2026, 11, 5, 10, 0, 0, DateTimeKind.Utc), "ana", AuditActions.ChangeUserTaskStatus, "Stare: În lucru → Finalizat")]);
    }

    private sealed class NoFiles : IUserTaskFiles
    {
        public Task<IReadOnlyList<EntityFile>> GetAsync(int taskId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<EntityFile>>([]);
        public Task<EntityFileContent?> GetContentAsync(int fileId, CancellationToken cancellationToken = default) => Task.FromResult<EntityFileContent?>(null);
        public Task<EntityFile> AddAsync(int taskId, string originalName, string declaredContentType, byte[] content, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(int fileId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static UserTask Item(int id, int user, string userName, string title, UserTaskStatus status, int order, bool accepted, string note = "") =>
        new(id, 1, "Beneficiar", user, userName, true, title, "Descriere " + title, status, null, new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc), "admin",
            accepted ? new DateTime(2026, 11, 2, 10, 0, 0, DateTimeKind.Utc) : null, accepted ? userName : null, status == UserTaskStatus.Done ? new DateTime(2026, 11, 5, 10, 0, 0, DateTimeKind.Utc) : null, 0, 0, UserTaskPriority.Low, note, order);

    private static IRenderedComponent<UserTaskTabs> Render(BunitContext context, FakeTasks tasks, bool canEdit)
    {
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddLogging();
        context.Services.AddSingleton<IUserTaskRepository>(tasks);
        context.Services.AddSingleton<IAccessControl>(new TaskTestAccess(null, "admin", canEdit));
        context.Services.AddSingleton<IChangeFeed>(new InProcessChangeFeed());
        context.Services.AddSingleton(new ChangeOrigin());
        var cut = context.Render<UserTaskTabs>();
        cut.WaitForAssertion(() => { if (cut.FindAll("#tab-tasks-unaccepted").Count == 0 || cut.FindAll(".spinner").Count > 0) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
        return cut;
    }

    public static Task RunAsync(Action<bool, string> check)
    {
        var tasks = new FakeTasks(
        [
            Item(1, 10, "Ana", "Proiect tehnic", UserTaskStatus.InProgress, 2, true),
            Item(2, 10, "Ana", "Verificare tablou", UserTaskStatus.Future, 1, true),
            Item(3, 10, "Ana", "Depunere dosar", UserTaskStatus.Done, 0, true, "Trimis la politie la 12.11.2026"),
            Item(4, 20, "Bogdan", "Montaj camere", UserTaskStatus.Future, 1, false),
            Item(5, 30, "Cezar", "Task vechi", UserTaskStatus.Done, 0, true)
        ]);
        using var context = new BunitContext();
        var cut = Render(context, tasks, canEdit: true);
        var tabs = cut.FindAll("[role=tab]").Select(tab => tab.TextContent.Trim()).ToList();
        check(tabs.Count == 4 && tabs[0].StartsWith("Nepreluate", StringComparison.Ordinal) && tabs[1].StartsWith("Ana", StringComparison.Ordinal) && tabs[2].StartsWith("Bogdan", StringComparison.Ordinal) && tabs[3].StartsWith("Cezar", StringComparison.Ordinal),
            "Task tabs: the tab of the tasks nobody took over, then a tab for every user who has tasks (also one who has only finished tasks)");
        check(cut.Find("#tab-tasks-unaccepted .count-badge").TextContent == "1" && cut.Find("#tab-tasks-user-10 .count-badge").TextContent == "2" && cut.Find("#tab-tasks-user-20").ClassList.Contains("needs-attention") && !cut.Find("#tab-tasks-user-10").ClassList.Contains("needs-attention"),
            "Task tabs: each tab shows its number of active tasks, and a tab with a task nobody took over asks for attention");
        check(cut.FindAll("#tasks-unaccepted-table tbody tr").Count == 1 && cut.Find("#tasks-unaccepted-table").TextContent.Contains("Montaj camere", StringComparison.Ordinal) && cut.FindAll("[data-task-handle]").Count == 0,
            "Task tabs: the first tab lists only the active tasks nobody took over, without the handles of the order");

        cut.Find("#tab-tasks-user-10").Click();
        var rows = cut.FindAll("#tasks-user-table tbody tr");
        var numbers = cut.FindAll("#tasks-user-table td.task-number").Select(cell => cell.TextContent.Trim()).ToList();
        check(rows.Count == 4 && rows[0].TextContent.Contains("Verificare tablou", StringComparison.Ordinal) && rows[1].TextContent.Contains("Proiect tehnic", StringComparison.Ordinal) && rows[2].ClassList.Contains("task-done-separator") && rows[3].TextContent.Contains("Depunere dosar", StringComparison.Ordinal)
              && numbers.SequenceEqual(["1", "2", "—"]), "Task tabs: a user's active tasks follow the order arranged for them (numbered), the finished ones come below, outside the numbering");
        check(rows[3].TextContent.Contains("Trimis la politie la 12.11.2026", StringComparison.Ordinal) && rows[3].QuerySelectorAll("button.row-icon.edit").Length == 1,
            "Task tabs: the stage written in the note shows under the title and a finished task can still be edited");
        check(cut.FindAll("[data-task-handle]").Count == 2 && cut.FindAll("tr[data-task-index]").Count == 2, "Task tabs: the active tasks of the user have the handle of the order (not the finished one)");

        cut.Find("#tasks-hide-done").Change(true);
        check(cut.FindAll("#tasks-user-table tbody tr").Count == 2 && !cut.Find("#tasks-user-table").TextContent.Contains("Depunere dosar", StringComparison.Ordinal), "Task tabs: the switch hides the finished tasks of the user");
        cut.Find("#tasks-hide-done").Change(false);

        cut.Find("[data-task-handle]").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "ArrowDown" });
        check(tasks.Moves.SequenceEqual([(2, 2)]), "Task tabs: the arrow down on the handle of the first task asks to move it to the second place of the list of its user");

        cut.Find("input[aria-label='Caută taskuri']").Input("tablou");
        check(cut.FindAll("[data-task-handle]").Count == 0 && cut.FindAll("#task-order-note").Count == 1, "Task tabs: with a search active the handles are hidden and the note says how to arrange the order");
        cut.Find("input[aria-label='Caută taskuri']").Input("");

        tasks.Items.RemoveAll(item => item.AssignedUserId == 20);
        cut.InvokeAsync(() => cut.Instance.RefreshAsync()).GetAwaiter().GetResult();
        cut.WaitForAssertion(() => { if (cut.FindAll("#tab-tasks-user-20").Count != 0) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
        check(cut.FindAll("[role=tab]").Count == 3 && cut.Find("#tab-tasks-unaccepted .count-badge").TextContent == "0", "Task tabs: the tab of a user without tasks goes away");

        using var reader = new BunitContext();
        var readOnly = Render(reader, new FakeTasks([Item(1, 10, "Ana", "Un task", UserTaskStatus.Future, 1, true), Item(2, 10, "Ana", "Alt task", UserTaskStatus.Future, 2, true)]), canEdit: false);
        readOnly.Find("#tab-tasks-user-10").Click();
        check(readOnly.FindAll("[data-task-handle]").Count == 0 && readOnly.FindAll("td.task-number").Count == 2 && readOnly.FindAll("button.row-icon.edit").Count == 0, "Task tabs: without the right to edit the tasks are numbered but cannot be arranged or edited");

        // The details of a finished task: the stage is written in the note (saved with its own button) and the journal of the task opens on demand.
        using var details = new BunitContext();
        var noteTasks = new FakeTasks([Item(3, 10, "Ana", "Depunere dosar", UserTaskStatus.Done, 0, true, "Trimis la politie")]);
        details.JSInterop.Mode = JSRuntimeMode.Loose;
        details.Services.AddLogging();
        details.Services.AddSingleton<IUserTaskRepository>(noteTasks);
        details.Services.AddSingleton<IUserTaskFiles>(new NoFiles());
        details.Services.AddSingleton<IAccessControl>(new TaskTestAccess(null, "admin", true));
        var dialog = details.Render<TaskDetailsDialog>(parameters => parameters.Add(p => p.TaskId, 3));
        dialog.WaitForAssertion(() => { if (dialog.FindAll("#task-note-view").Count == 0) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
        check(dialog.Find("#task-note-view").TextContent.Contains("Trimis la politie", StringComparison.Ordinal) && dialog.FindAll("#task-history-list").Count == 0, "Task details: the stage written in the note is shown, the journal of the task is folded");
        dialog.Find("#task-history-toggle").Click();
        var lines = dialog.FindAll("#task-history-list li").Select(line => line.TextContent).ToList();
        check(lines.Count == 2 && lines[0].Contains("Creat de admin", StringComparison.Ordinal) && lines[1].Contains("Finalizat de ana", StringComparison.Ordinal) && lines[1].Contains("05.11.2026", StringComparison.Ordinal), "Task details: the journal of the task opens with who did what and when, oldest first");
        dialog.FindAll("button").First(button => button.TextContent.Trim() == "Editează").Click();
        dialog.Find("#task-details-note").Input("Trimis la politie la 12.11.2026");
        dialog.Find("#task-note-save").Click();
        check(noteTasks.Notes.SequenceEqual([(3, "Trimis la politie la 12.11.2026")]) && noteTasks.Items[0].Status == UserTaskStatus.Done, "Task details: the stage of a finished task is saved with its own button and the task stays finished");
        return System.Threading.Tasks.Task.CompletedTask;
    }
}
