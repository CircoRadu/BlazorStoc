using System.Text;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using BlazorStoc.Modules.Tasks;
using BlazorStoc.Services;

namespace BlazorStoc.Checks;

// A signed-in account with an identifier and some permission keys (the administrator of the settings file has no identifier).
sealed class TaskTestAccess(int? userId, string username, bool administrator, params string[] keys) : IAccessControl
{
    public Task<bool> IsAdministratorAsync(CancellationToken cancellationToken = default) => Task.FromResult(administrator);
    public Task<bool> CanManageProductsAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task<bool> CanManageBeneficiariesAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task<string?> GetUsernameAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(username);
    public Task<int?> GetUserIdAsync(CancellationToken cancellationToken = default) => Task.FromResult(userId);
    public Task EnsureAdministratorAsync(CancellationToken cancellationToken = default) => administrator ? Task.CompletedTask : Task.FromException(new AccessDeniedException("Test access denied"));
    public Task EnsureProductOperatorAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task EnsureBeneficiaryOperatorAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<bool> HasAsync(string permission, CancellationToken cancellationToken = default) => Task.FromResult(administrator || keys.Contains(permission));
}

public static partial class MariaExtendedChecks
{
    private static async Task UserTasksAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe)
    {
        var suffix = Suffix();
        var beneficiaries = new MariaBeneficiaryRepository(configuration, admin, audit);
        var users = new MariaUserRepository(configuration, admin, audit);
        var since = DateTime.UtcNow;
        await Task.Delay(30);
        var owner = await beneficiaries.CreateAsync(Legal($"Ext Taskuri benef {suffix} SRL", "RO" + Random.Shared.Next(60000000, 69999999)));
        var userA = await users.CreateAsync(new WebUserInput { Username = "ext.ta." + suffix, DisplayName = "Ext Task A", Role = AccessRoles.LimitedUser, Password = "Parola-ext-veche-123!" });
        var userB = await users.CreateAsync(new WebUserInput { Username = "ext.tb." + suffix, DisplayName = "Ext Task B", Role = AccessRoles.LimitedUser, Password = "Parola-ext-veche-123!" });
        var adminAccess = new TaskTestAccess(null, "integration.tester", true);
        var accessA = new TaskTestAccess(userA.Id, userA.Username, false);
        var accessB = new TaskTestAccess(userB.Id, userB.Username, false);
        MariaUserTaskRepository RepositoryOf(IAccessControl access) => new(configuration, access, audit);
        MariaUserTaskFileStore FilesOf(IAccessControl access) => new(configuration, access, audit, RepositoryOf(access));
        var tasks = RepositoryOf(adminAccess);
        byte[] Text(string value) => Encoding.UTF8.GetBytes(value);
        async Task<int> Events(int taskId, string action) => (await audit.GetEventsAsync()).Count(item => item.TimestampUtc > since && item.EntityType == AuditEntities.UserTask &&
            item.EntityId == taskId.ToString() && item.Action == action);
        var created = new List<int>();
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            var task = await tasks.CreateAsync(new UserTaskInput { BeneficiaryId = owner.Id, AssignedUserId = userA.Id, Title = "  Ext verificare tablou  ", Description = "Ext de verificat tabloul electric", DueDate = today.AddDays(2) });
            created.Add(task.Id);
            Check(task.Title == "Ext verificare tablou" && task.Status == UserTaskStatus.Future && !task.IsAccepted && task.IsActive && task.CreatedBy == "integration.tester" && task.AssignedUserName == "Ext Task A" &&
                  task.BeneficiaryId == owner.Id && task.DueDate == today.AddDays(2) && task.FileCount == 0, "A task is created for one user, not taken over, active, with the title trimmed and the author recorded");
            Check(await Events(task.Id, AuditActions.CreateUserTask) == 1, "The creation of a task is journaled with its own action");
            await Rejects<UserTaskOperationException>(() => tasks.CreateAsync(new UserTaskInput { BeneficiaryId = owner.Id, AssignedUserId = userA.Id, Title = "  ", Description = "x" }), "A task needs a title");
            await Rejects<UserTaskOperationException>(() => tasks.CreateAsync(new UserTaskInput { BeneficiaryId = owner.Id, AssignedUserId = userA.Id, Title = "t", Description = "" }), "A task needs a description");
            await Rejects<UserTaskOperationException>(() => tasks.CreateAsync(new UserTaskInput { BeneficiaryId = owner.Id + 900000, AssignedUserId = userA.Id, Title = "t", Description = "d" }), "The beneficiary of a task must exist");
            await Rejects<UserTaskOperationException>(() => tasks.CreateAsync(new UserTaskInput { BeneficiaryId = owner.Id, AssignedUserId = userA.Id + 900000, Title = "t", Description = "d" }), "The user of a task must exist");

            // ---- Access: administrator keys and the user the task is addressed to ----
            var asA = RepositoryOf(accessA);
            var asB = RepositoryOf(accessB);
            Check((await asA.GetMineAsync()).Any(item => item.Id == task.Id) && !(await asB.GetMineAsync()).Any(item => item.Id == task.Id), "A user sees the tasks addressed to them and no others");
            Check(await asB.GetAsync(task.Id) is null && await asA.GetAsync(task.Id) is { } seen && seen.Id == task.Id, "The task of another user is not found; one's own is read");
            await Rejects<AccessDeniedException>(() => asA.GetAllAsync(), "Without taskuri.view the list of all tasks is refused");
            await Rejects<AccessDeniedException>(() => asA.CreateAsync(new UserTaskInput { BeneficiaryId = owner.Id, AssignedUserId = userA.Id, Title = "t", Description = "d" }), "A user cannot give tasks");
            await Rejects<AccessDeniedException>(() => asA.UpdateAsync(task, UserTaskInput.From(task)), "A user cannot edit the task");
            await Rejects<AccessDeniedException>(() => asA.DeleteAsync(task, "Ext motiv"), "A user cannot delete the task");
            Check((await RepositoryOf(new TaskTestAccess(null, "viewer", false, "taskuri.view")).GetAllAsync()).Any(item => item.Id == task.Id), "With taskuri.view the tasks of everybody are listed");

            // ---- Taking over and status (by the user) ----
            await Rejects<AccessDeniedException>(() => asB.AcceptAsync(task), "Only the user of the task takes it over");
            await Rejects<AccessDeniedException>(() => asB.SetStatusAsync(task, UserTaskStatus.InProgress), "Another user cannot change the status");
            var accepted = await asA.AcceptAsync(task);
            Check(accepted.IsAccepted && accepted.AcceptedBy == userA.Username && await Events(task.Id, AuditActions.AcceptUserTask) == 1, "The user takes the task over and it is journaled");
            Check((await asA.AcceptAsync(accepted)).Version == accepted.Version && await Events(task.Id, AuditActions.AcceptUserTask) == 1, "Taking over twice changes nothing");
            var working = await asA.SetStatusAsync(accepted, UserTaskStatus.InProgress);
            Check(working.Status == UserTaskStatus.InProgress && working.IsActive && await Events(task.Id, AuditActions.ChangeUserTaskStatus) == 1, "The user moves the task to \"În lucru\" (journaled, still active)");
            await Rejects<UserTaskOperationException>(() => asA.SetStatusAsync(accepted, UserTaskStatus.Done), "A stale task cannot be changed");
            var done = await asA.SetStatusAsync(working, UserTaskStatus.Done);
            Check(done.Status == UserTaskStatus.Done && !done.IsActive && done.FinishedUtc is not null, "Finishing a task stores the finish time and takes it out of the active ones");
            var reopened = await tasks.SetStatusAsync(done, UserTaskStatus.InProgress);
            Check(reopened.Status == UserTaskStatus.InProgress && reopened.FinishedUtc is null && reopened.IsAccepted, "The administrator reopens a finished task; the finish time is cleared");

            // ---- Files ----
            var adminFilesStore = FilesOf(adminAccess);
            var filesA = FilesOf(accessA);
            var filesB = FilesOf(accessB);
            var fromAdmin = await adminFilesStore.AddAsync(task.Id, "instructiuni.txt", "text/plain", Text("de la administrator"));
            Check(fromAdmin.OwnerKind == EntityFileOwners.UserTask && await Events(task.Id, AuditActions.AddUserTaskFile) == 1, "The administrator attaches a file to the task (journaled)");
            await Rejects<WorkPointOperationException>(() => adminFilesStore.AddAsync(task.Id, "instructiuni.txt", "text/plain", Text("de la administrator")), "The same file cannot be attached twice");
            await Rejects<AccessDeniedException>(() => filesB.GetAsync(task.Id), "A user who is not the one of the task cannot read its files");
            await Rejects<AccessDeniedException>(() => filesB.AddAsync(task.Id, "intrus.txt", "text/plain", Text("x")), "A user who is not the one of the task cannot attach files");
            Check((await filesA.GetAsync(task.Id)).Count == 1 && (await filesA.GetContentAsync(fromAdmin.Id)) is { } content && Encoding.UTF8.GetString(content.Content) == "de la administrator", "The user of the task reads and downloads its files");
            var fromUser = await filesA.AddAsync(task.Id, "raport.txt", "text/plain", Text("de la utilizator"));
            Check(fromUser.UploadedBy == userA.Username && (await tasks.GetAsync(task.Id))!.FileCount == 2, "The user attaches a file (counted on the task)");
            await Rejects<AccessDeniedException>(() => filesA.DeleteAsync(fromAdmin.Id), "The user cannot delete a file added by the administrator");
            await filesA.DeleteAsync(fromUser.Id);
            Check((await filesA.GetAsync(task.Id)).Count == 1 && await Events(task.Id, AuditActions.DeleteUserTaskFile) == 1, "The user deletes the file they added (journaled)");
            var anyType = await filesA.AddAsync(task.Id, "program.exe", "application/x-msdownload", new byte[] { 0x4D, 0x5A, 1, 2, 3 });
            Check(anyType.ContentType == UserTaskFileRules.Unknown && !UserTaskFileRules.CanShowInline(anyType.ContentType) && UserTaskFileRules.CanShowInline("application/pdf"),
                "A file of a task may be of any type; an unknown type is stored as a download-only file");
            await Rejects<UserTaskOperationException>(() => filesA.AddAsync(task.Id, "gol.bin", "application/octet-stream", []), "An empty file is refused");
            await filesA.DeleteAsync(anyType.Id);
            var asNewFile =await filesA.AddAsync(task.Id, "alt.txt", "text/plain", Text("alt fisier"));
            await adminFilesStore.DeleteAsync(asNewFile.Id);
            Check((await adminFilesStore.GetAsync(task.Id)).Count == 1, "The administrator deletes any file");
            Check(!(await new MariaEntityFileStore(configuration, admin, audit).GetForBeneficiaryAsync(owner.Id)).Any(), "The files of a task never appear among the files of the beneficiary");

            // ---- Edit by the administrator: one journal event per kind of change; moving makes the task \"not taken over\" again ----
            var current = (await tasks.GetAsync(task.Id))!;
            var edit = UserTaskInput.From(current);
            edit.Title = "Ext verificare tablou nou"; edit.DueDate = today.AddDays(10); edit.AssignedUserId = userB.Id; edit.Status = UserTaskStatus.Future;
            var moved = await tasks.UpdateAsync(current, edit);
            Check(moved.AssignedUserId == userB.Id && !moved.IsAccepted && moved.Status == UserTaskStatus.Future && moved.Title == "Ext verificare tablou nou" && moved.DueDate == today.AddDays(10), "Editing moves the task to another user and it is \"not taken over\" again");
            Check(await Events(task.Id, AuditActions.EditUserTask) == 1 && await Events(task.Id, AuditActions.EditUserTaskDueDate) == 1 && await Events(task.Id, AuditActions.MoveUserTask) == 1 &&
                  await Events(task.Id, AuditActions.ChangeUserTaskStatus) == 4, "Each kind of change (title, deadline, user, status) is a journal event of its own");
            await Rejects<UserTaskOperationException>(() => tasks.UpdateAsync(current, edit), "A stale edit is refused");
            Check(!(await asA.GetMineAsync()).Any(item => item.Id == task.Id) && (await asB.GetMineAsync()).Any(item => item.Id == task.Id), "After the move the task is on the dashboard of the new user only");
            var addedByNewUser = await filesB.AddAsync(task.Id, "b.txt", "text/plain", Text("fisier b"));
            Check((await asB.GetAsync(task.Id))!.IsAccepted && await Events(task.Id, AuditActions.AcceptUserTask) == 2 && addedByNewUser.UploadedBy == userB.Username, "A user who attaches a file to a new task takes it over too");

            // ---- Alerts (rules) ----
            var alertTasks = new[]
            {
                moved with { Id = 1, Status = UserTaskStatus.Future, AcceptedUtc = null, DueDate = null },                         // not taken over
                moved with { Id = 2, Status = UserTaskStatus.InProgress, AcceptedUtc = DateTime.UtcNow, DueDate = today.AddDays(-1) }, // overdue
                moved with { Id = 3, Status = UserTaskStatus.InProgress, AcceptedUtc = DateTime.UtcNow, DueDate = today.AddDays(3) },  // due soon (3 days)
                moved with { Id = 4, Status = UserTaskStatus.InProgress, AcceptedUtc = DateTime.UtcNow, DueDate = today.AddDays(4) },  // not yet
                moved with { Id = 5, Status = UserTaskStatus.Done, AcceptedUtc = null, DueDate = today.AddDays(-5) }                    // finished: no alert
            };
            Check(UserTaskRules.Alerts(alertTasks, today, 3).Select(alert => alert.Task.Id).SequenceEqual([2, 1, 3]), "Alerts: overdue first, then not taken over, then due within the warning days; finished and distant ones do not warn");

            // ---- Warning days (global setting) ----
            Check(await tasks.GetWarningDaysAsync() == UserTaskRules.DefaultWarningDays, "The warning period is 3 days until it is changed");
            await Rejects<AccessDeniedException>(() => asA.SetWarningDaysAsync(5), "Only who may edit the notification settings changes the warning period");
            await Rejects<UserTaskOperationException>(() => tasks.SetWarningDaysAsync(0), "A warning period of 0 days is refused");
            await tasks.SetWarningDaysAsync(5);
            Check(await asA.GetWarningDaysAsync() == 5 && (await audit.GetEventsAsync()).Count(item => item.TimestampUtc > since && item.Action == AuditActions.EditTaskWarningDays) == 1, "The warning period is saved and any user reads it");

            // ---- Beneficiary deletion and task deletion ----
            await Rejects<BeneficiaryOperationException>(() => beneficiaries.DeleteAsync(owner, "Ext motiv stergere"), "A beneficiary with tasks cannot be deleted");
            current = (await tasks.GetAsync(task.Id))!;
            await Rejects<UserTaskOperationException>(() => tasks.DeleteAsync(current, ""), "Deleting a task needs a reason");
            await tasks.DeleteAsync(current, "Ext stergere task");
            created.Remove(task.Id);
            await using (var count = new MySqlCommand("SELECT COUNT(*) FROM entity_files WHERE owner_kind='task' AND owner_id=@id", probe))
            {
                count.Parameters.AddWithValue("@id", task.Id);
                Check(Convert.ToInt32(await count.ExecuteScalarAsync()) == 0 && await tasks.GetAsync(task.Id) is null, "Deleting a task deletes its files too");
            }
            Check(await Events(task.Id, AuditActions.DeleteUserTask) == 1 && AuditActions.IsDeletion(AuditActions.DeleteUserTask) && AuditActions.IsCreateOrEdit(AuditActions.EditUserTask), "The deletion is journaled and its object no longer links to a page");
        }
        finally
        {
            await using (var settings = new MySqlCommand("DELETE FROM task_settings WHERE setting_key=@key", probe))
            {
                settings.Parameters.AddWithValue("@key", UserTaskRules.SettingWarningDays);
                await settings.ExecuteNonQueryAsync();
            }
            foreach (var id in created)
                if (await tasks.GetAsync(id) is { } left) await tasks.DeleteAsync(left, "Ext curatare");
            foreach (var user in new[] { userA, userB })
                if ((await users.GetUsersAsync()).FirstOrDefault(item => item.Id == user.Id) is { } live) await users.DeleteAsync(live, "Ext curatare");
            if ((await beneficiaries.GetBeneficiariesAsync()).FirstOrDefault(current => current.Id == owner.Id) is { } liveBeneficiary) await beneficiaries.DeleteAsync(liveBeneficiary, "Ext curatare");
        }
    }
}
