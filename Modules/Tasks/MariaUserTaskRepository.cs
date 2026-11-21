using System.Globalization;
using MySqlConnector;
using BlazorStoc.Services;
using static BlazorStoc.Services.MariaDb;

namespace BlazorStoc.Modules.Tasks;

// Table `beneficiary_tasks` (migration 81) and `task_settings`. The "_utc" columns are text, read and written only through MariaTimeText. Access: the administrator side needs
// the keys taskuri.view/add/edit/delete; the user side (own tasks) needs no key: a task is read, moved to another status or taken over by the user it is addressed to only.
public sealed class MariaUserTaskRepository(IConfiguration configuration, IAccessControl? accessControl = null, IAuditTrail? auditTrail = null,
    IChangeFeed? changeFeed = null, ChangeOrigin? origin = null) : IUserTaskRepository
{
    private const string Select = """
        SELECT t.id, t.beneficiary_id, b.name, t.assigned_user_id, u.username, u.display_name, u.is_active, t.title, t.description, t.status, t.due_date,
               t.created_utc, t.created_by, t.accepted_utc, t.accepted_by, t.finished_utc, t.version,
               (SELECT COUNT(*) FROM entity_files f WHERE f.owner_kind='task' AND f.owner_id=t.id), t.priority, COALESCE(t.progress_note, ''), t.sort_order
        FROM beneficiary_tasks t
        JOIN beneficiaries b ON b.id=t.beneficiary_id
        JOIN web_users u ON u.id=t.assigned_user_id
        """;

    public async Task<IReadOnlyList<UserTask>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await EnsureAsync("taskuri.view", cancellationToken).ConfigureAwait(false);
        await BackfillOrderAsync(cancellationToken).ConfigureAwait(false);
        return await ReadListAsync("", [], cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<UserTask>> GetForBeneficiaryAsync(int beneficiaryId, CancellationToken cancellationToken = default)
    {
        await EnsureAsync("taskuri.view", cancellationToken).ConfigureAwait(false);
        return await ReadListAsync("WHERE t.beneficiary_id=@id", [("@id", beneficiaryId)], cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<UserTask>> GetMineAsync(CancellationToken cancellationToken = default)
    {
        if (await CurrentUserIdAsync(cancellationToken).ConfigureAwait(false) is not { } userId) return [];
        await BackfillOrderAsync(cancellationToken).ConfigureAwait(false);
        return await ReadListAsync("WHERE t.assigned_user_id=@user", [("@user", userId)], cancellationToken).ConfigureAwait(false);
    }

    public async Task<UserTask?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        var task = (await ReadListAsync("WHERE t.id=@id", [("@id", id)], cancellationToken).ConfigureAwait(false)).FirstOrDefault();
        if (task is null) return null;
        return await CanSeeAsync(task, cancellationToken).ConfigureAwait(false) ? task : null;
    }

    public async Task<IReadOnlyList<UserTaskUser>> GetAssignableUsersAsync(CancellationToken cancellationToken = default)
    {
        await EnsureAnyAsync(["taskuri.view", "taskuri.add", "taskuri.edit"], cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, "SELECT id, username, display_name, is_active FROM web_users ORDER BY display_name, username");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<UserTaskUser>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new(checked((int)reader.GetInt64(0)), reader.GetString(1), reader.GetString(2), Convert.ToInt64(reader.GetValue(3)) != 0));
        return result;
    }

    public async Task<UserTask> CreateAsync(UserTaskInput input, CancellationToken cancellationToken = default)
    {
        await EnsureAsync("taskuri.add", cancellationToken).ConfigureAwait(false);
        await BackfillOrderAsync(cancellationToken).ConfigureAwait(false);
        var value = input.Validated();
        var author = await UsernameAsync(cancellationToken).ConfigureAwait(false);
        var id = await WriteAsync(async (connection, transaction) =>
        {
            await CheckBeneficiaryAsync(connection, transaction, value.BeneficiaryId, cancellationToken).ConfigureAwait(false);
            await CheckUserAsync(connection, transaction, value.AssignedUserId, cancellationToken).ConfigureAwait(false);
            var now = MariaTimeText.Now();
            var order = value.Status == UserTaskStatus.Done ? 0 : await NextOrderAsync(connection, transaction, value.AssignedUserId, cancellationToken).ConfigureAwait(false);
            await using var command = Command(connection, transaction, """
                INSERT INTO beneficiary_tasks (beneficiary_id, assigned_user_id, title, description, status, due_date, priority, sort_order, created_utc, created_by, finished_utc, version)
                VALUES (@beneficiary, @user, @title, @description, @status, @due, @priority, @order, @created, @author, @finished, 0)
                """, ("@beneficiary", value.BeneficiaryId), ("@user", value.AssignedUserId), ("@title", value.Title), ("@description", value.Description),
                ("@status", (int)value.Status), ("@due", SqlDate(value.DueDate)), ("@priority", (int)value.Priority), ("@order", order), ("@created", MariaTimeText.Format(now)), ("@author", author),
                ("@finished", value.Status == UserTaskStatus.Done ? MariaTimeText.Format(now) : null));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return checked((int)command.LastInsertedId);
        }, cancellationToken).ConfigureAwait(false);
        var task = await LoadAsync(id, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.UserTask, AuditActions.CreateUserTask, id.ToString(CultureInfo.InvariantCulture),
            UserTaskRules.Target(task), UserTaskRules.Identification(task), string.Empty, cancellationToken).ConfigureAwait(false);
        Publish(AuditActions.CreateUserTask, task);
        return task;
    }

    public async Task<UserTask> UpdateAsync(UserTask original, UserTaskInput input, CancellationToken cancellationToken = default)
    {
        await EnsureAsync("taskuri.edit", cancellationToken).ConfigureAwait(false);
        // The beneficiary of a task does not change.
        input.BeneficiaryId = original.BeneficiaryId;
        var value = input.Validated();
        await WriteAsync(async (connection, transaction) =>
        {
            UserTaskRules.CheckCurrent(await LockVersionAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false), original);
            var moved = value.AssignedUserId != original.AssignedUserId;
            if (moved) await CheckUserAsync(connection, transaction, value.AssignedUserId, cancellationToken).ConfigureAwait(false);
            var wasDone = original.Status == UserTaskStatus.Done;
            var isDone = value.Status == UserTaskStatus.Done;
            // A finished task has no place in the list; one that becomes active again, or goes to another user, takes the last place of that user's list.
            var newOrder = isDone ? 0 : moved || wasDone ? await NextOrderAsync(connection, transaction, value.AssignedUserId, cancellationToken).ConfigureAwait(false) : -1;
            await using var command = Command(connection, transaction, """
                UPDATE beneficiary_tasks SET assigned_user_id=@user, title=@title, description=@description, status=@status, due_date=@due, priority=@priority,
                    accepted_utc=IF(@moved, NULL, accepted_utc), accepted_by=IF(@moved, NULL, accepted_by),
                    sort_order=IF(@order >= 0, @order, sort_order),
                    finished_utc=@finished, version=version+1
                WHERE id=@id AND version=@version
                """, ("@user", value.AssignedUserId), ("@title", value.Title), ("@description", value.Description), ("@status", (int)value.Status),
                ("@due", SqlDate(value.DueDate)), ("@priority", (int)value.Priority), ("@moved", moved ? 1 : 0), ("@order", newOrder), ("@id", original.Id), ("@version", original.Version),
                ("@finished", isDone ? (wasDone && original.FinishedUtc is { } kept ? MariaTimeText.Format(kept) : MariaTimeText.Format(MariaTimeText.Now())) : null));
            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1) throw new UserTaskOperationException(UserTaskRules.ConcurrentMessage);
            if (!wasDone && (isDone || moved)) await RenumberAsync(connection, transaction, original.AssignedUserId, cancellationToken).ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);
        var updated = await LoadAsync(original.Id, cancellationToken).ConfigureAwait(false);
        await RecordEditsAsync(original, updated, cancellationToken).ConfigureAwait(false);
        Publish(AuditActions.EditUserTask, updated);
        return updated;
    }

    public async Task DeleteAsync(UserTask original, string reason, CancellationToken cancellationToken = default)
    {
        await EnsureAsync("taskuri.delete", cancellationToken).ConfigureAwait(false);
        var motif = ChangeReasonRules.Normalize(reason);
        if (ChangeReasonRules.ValidationError(motif) is { } reasonError) throw new UserTaskOperationException(reasonError);
        var storedNames = new List<string>();
        var fileNames = new List<string>();
        await WriteAsync(async (connection, transaction) =>
        {
            UserTaskRules.CheckCurrent(await LockVersionAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false), original);
            await using (var files = Command(connection, transaction, "SELECT relative_path, original_name FROM entity_files WHERE owner_kind='task' AND owner_id=@id", ("@id", original.Id)))
            await using (var reader = await files.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) { storedNames.Add(reader.GetString(0)); fileNames.Add(reader.GetString(1)); }
            await using (var deleteFiles = Command(connection, transaction, "DELETE FROM entity_files WHERE owner_kind='task' AND owner_id=@id", ("@id", original.Id)))
                await deleteFiles.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await using var delete = Command(connection, transaction, "DELETE FROM beneficiary_tasks WHERE id=@id AND version=@version", ("@id", original.Id), ("@version", original.Version));
            if (await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1) throw new UserTaskOperationException(UserTaskRules.ConcurrentMessage);
            await RenumberAsync(connection, transaction, original.AssignedUserId, cancellationToken).ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);
        var root = MariaAssetPaths.EntityFiles(configuration);
        foreach (var stored in storedNames)
        {
            try { var path = Path.Combine(root, stored); if (File.Exists(path)) File.Delete(path); }
            catch (IOException) { /* the row is gone; an orphan file is harmless */ }
        }
        var details = UserTaskRules.Identification(original) + (fileNames.Count == 0 ? "" : "; Fișiere șterse: " + string.Join(", ", fileNames));
        await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.UserTask, AuditActions.DeleteUserTask, original.Id.ToString(CultureInfo.InvariantCulture),
            UserTaskRules.Target(original), details, motif, cancellationToken).ConfigureAwait(false);
        Publish(AuditActions.DeleteUserTask, original);
    }

    public async Task<UserTask> SetStatusAsync(UserTask original, UserTaskStatus status, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(status)) throw new UserTaskOperationException("Starea taskului nu este validă.");
        var userId = await CurrentUserIdAsync(cancellationToken).ConfigureAwait(false);
        var isAssignee = userId == original.AssignedUserId;
        if (!isAssignee) await EnsureAsync("taskuri.edit", cancellationToken).ConfigureAwait(false);
        var actor = await UsernameAsync(cancellationToken).ConfigureAwait(false);
        var takenOver = false;
        await WriteAsync(async (connection, transaction) =>
        {
            UserTaskRules.CheckCurrent(await LockVersionAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false), original);
            takenOver = isAssignee && !original.IsAccepted;
            var finished = status == UserTaskStatus.Done
                ? (original.Status == UserTaskStatus.Done && original.FinishedUtc is { } kept ? MariaTimeText.Format(kept) : MariaTimeText.Format(MariaTimeText.Now()))
                : null;
            var wasDone = original.Status == UserTaskStatus.Done;
            var isDone = status == UserTaskStatus.Done;
            var newOrder = isDone ? 0 : wasDone ? await NextOrderAsync(connection, transaction, original.AssignedUserId, cancellationToken).ConfigureAwait(false) : -1;
            await using var command = Command(connection, transaction, """
                UPDATE beneficiary_tasks SET status=@status, finished_utc=@finished, sort_order=IF(@order >= 0, @order, sort_order),
                    accepted_utc=IF(@take, @now, accepted_utc), accepted_by=IF(@take, @actor, accepted_by), version=version+1
                WHERE id=@id AND version=@version
                """, ("@status", (int)status), ("@finished", finished), ("@order", newOrder), ("@take", takenOver ? 1 : 0), ("@now", MariaTimeText.Format(MariaTimeText.Now())),
                ("@actor", actor), ("@id", original.Id), ("@version", original.Version));
            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1) throw new UserTaskOperationException(UserTaskRules.ConcurrentMessage);
            if (!wasDone && isDone) await RenumberAsync(connection, transaction, original.AssignedUserId, cancellationToken).ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);
        var updated = await LoadAsync(original.Id, cancellationToken).ConfigureAwait(false);
        if (original.Status != status)
            await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.UserTask, AuditActions.ChangeUserTaskStatus, original.Id.ToString(CultureInfo.InvariantCulture),
                UserTaskRules.Target(updated), AuditDetails.Changes(new AuditChange("Stare", UserTaskRules.StatusLabel(original.Status), UserTaskRules.StatusLabel(status))),
                string.Empty, cancellationToken).ConfigureAwait(false);
        if (takenOver) await RecordAcceptAsync(updated, "preluat prin schimbarea stării", cancellationToken).ConfigureAwait(false);
        Publish(AuditActions.ChangeUserTaskStatus, updated);
        return updated;
    }

    public async Task<UserTask> AcceptAsync(UserTask original, CancellationToken cancellationToken = default)
    {
        var userId = await CurrentUserIdAsync(cancellationToken).ConfigureAwait(false);
        if (userId != original.AssignedUserId) throw new AccessDeniedException("Numai utilizatorul căruia îi este dat taskul îl poate prelua.");
        var actor = await UsernameAsync(cancellationToken).ConfigureAwait(false);
        var current = await LoadAsync(original.Id, cancellationToken).ConfigureAwait(false);
        if (current.AssignedUserId != userId) throw new AccessDeniedException("Numai utilizatorul căruia îi este dat taskul îl poate prelua.");
        if (current.IsAccepted) return current;
        await WriteAsync(async (connection, transaction) =>
        {
            await using var command = Command(connection, transaction, """
                UPDATE beneficiary_tasks SET accepted_utc=@now, accepted_by=@actor, version=version+1
                WHERE id=@id AND assigned_user_id=@user AND accepted_utc IS NULL
                """, ("@now", MariaTimeText.Format(MariaTimeText.Now())), ("@actor", actor), ("@id", original.Id), ("@user", userId));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);
        var updated = await LoadAsync(original.Id, cancellationToken).ConfigureAwait(false);
        await RecordAcceptAsync(updated, "preluat de utilizator", cancellationToken).ConfigureAwait(false);
        Publish(AuditActions.AcceptUserTask, updated);
        return updated;
    }

    public async Task<int> GetWarningDaysAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, "SELECT setting_value FROM task_settings WHERE setting_key=@key", ("@key", UserTaskRules.SettingWarningDays));
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is string text && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var days)
            && UserTaskRules.WarningDaysError(days).Length == 0 ? days : UserTaskRules.DefaultWarningDays;
    }

    public async Task SetWarningDaysAsync(int days, CancellationToken cancellationToken = default)
    {
        await EnsureAsync("setari-notificari.edit", cancellationToken).ConfigureAwait(false);
        if (UserTaskRules.WarningDaysError(days) is { Length: > 0 } error) throw new UserTaskOperationException(error);
        var before = await GetWarningDaysAsync(cancellationToken).ConfigureAwait(false);
        await WriteAsync(async (connection, transaction) =>
        {
            await using var command = Command(connection, transaction, """
                INSERT INTO task_settings (setting_key, setting_value, version) VALUES (@key, @value, 0)
                ON DUPLICATE KEY UPDATE setting_value=VALUES(setting_value), version=version+1
                """, ("@key", UserTaskRules.SettingWarningDays), ("@value", days.ToString(CultureInfo.InvariantCulture)));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);
        if (before != days)
            await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.UserTask, AuditActions.EditTaskWarningDays, string.Empty, "Zile de avertizare pentru taskuri",
                AuditDetails.Changes(new AuditChange("Zile înainte de termen", before.ToString(CultureInfo.InvariantCulture), days.ToString(CultureInfo.InvariantCulture))),
                string.Empty, cancellationToken).ConfigureAwait(false);
    }

    public async Task<UserTask> SetNoteAsync(UserTask original, string note, CancellationToken cancellationToken = default)
    {
        var userId = await CurrentUserIdAsync(cancellationToken).ConfigureAwait(false);
        var isAssignee = userId == original.AssignedUserId;
        if (!isAssignee) await EnsureAsync("taskuri.edit", cancellationToken).ConfigureAwait(false);
        var text = (note ?? "").Replace("\r\n", "\n").Trim();
        if (text.Length > UserTaskRules.NoteMaxLength) throw new UserTaskOperationException($"Stadiul poate avea cel mult {UserTaskRules.NoteMaxLength} de caractere.");
        var actor = await UsernameAsync(cancellationToken).ConfigureAwait(false);
        var takenOver = false;
        await WriteAsync(async (connection, transaction) =>
        {
            UserTaskRules.CheckCurrent(await LockVersionAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false), original);
            takenOver = isAssignee && !original.IsAccepted && original.IsActive;
            await using var command = Command(connection, transaction, """
                UPDATE beneficiary_tasks SET progress_note=@note,
                    accepted_utc=IF(@take, @now, accepted_utc), accepted_by=IF(@take, @actor, accepted_by), version=version+1
                WHERE id=@id AND version=@version
                """, ("@note", text), ("@take", takenOver ? 1 : 0), ("@now", MariaTimeText.Format(MariaTimeText.Now())), ("@actor", actor), ("@id", original.Id), ("@version", original.Version));
            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1) throw new UserTaskOperationException(UserTaskRules.ConcurrentMessage);
            return true;
        }, cancellationToken).ConfigureAwait(false);
        var updated = await LoadAsync(original.Id, cancellationToken).ConfigureAwait(false);
        if (original.ProgressNote != updated.ProgressNote)
            await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.UserTask, AuditActions.EditUserTaskNote, original.Id.ToString(CultureInfo.InvariantCulture),
                UserTaskRules.Target(updated), AuditDetails.Changes(new AuditChange("Stadiu", original.ProgressNote.Length == 0 ? "—" : original.ProgressNote, updated.ProgressNote.Length == 0 ? "—" : updated.ProgressNote)),
                string.Empty, cancellationToken).ConfigureAwait(false);
        if (takenOver) await RecordAcceptAsync(updated, "preluat prin scrierea stadiului", cancellationToken).ConfigureAwait(false);
        Publish(AuditActions.EditUserTaskNote, updated);
        return updated;
    }

    public async Task MoveInListAsync(UserTask task, int newPosition, CancellationToken cancellationToken = default)
    {
        await EnsureAsync("taskuri.edit", cancellationToken).ConfigureAwait(false);
        await BackfillOrderAsync(cancellationToken).ConfigureAwait(false);
        var oldPosition = 0;
        var target = newPosition;
        await WriteAsync(async (connection, transaction) =>
        {
            var ids = new List<int>();
            await using (var read = Command(connection, transaction, "SELECT id FROM beneficiary_tasks WHERE assigned_user_id=@user AND status<>@done ORDER BY sort_order, id FOR UPDATE", ("@user", task.AssignedUserId), ("@done", (int)UserTaskStatus.Done)))
            await using (var reader = await read.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) ids.Add(checked((int)reader.GetInt64(0)));
            oldPosition = ids.IndexOf(task.Id) + 1;
            if (oldPosition == 0) throw new UserTaskOperationException(UserTaskRules.ConcurrentMessage);
            target = Math.Clamp(newPosition, 1, ids.Count);
            ids.RemoveAt(oldPosition - 1);
            ids.Insert(target - 1, task.Id);
            for (var index = 0; index < ids.Count; index++)
            {
                await using var update = Command(connection, transaction, "UPDATE beneficiary_tasks SET sort_order=@order WHERE id=@id AND sort_order<>@order", ("@order", index + 1), ("@id", ids[index]));
                await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            return true;
        }, cancellationToken).ConfigureAwait(false);
        if (oldPosition == target) return;
        var current = await LoadAsync(task.Id, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.UserTask, AuditActions.ReorderUserTasks, task.Id.ToString(CultureInfo.InvariantCulture),
            UserTaskRules.Target(current), AuditDetails.Changes(new AuditChange("Poziție în lista utilizatorului " + current.AssignedUserName, oldPosition.ToString(CultureInfo.InvariantCulture), target.ToString(CultureInfo.InvariantCulture))),
            string.Empty, cancellationToken).ConfigureAwait(false);
        Publish(AuditActions.ReorderUserTasks, current);
    }

    public async Task<IReadOnlyList<UserTaskHistoryEntry>> GetHistoryAsync(int taskId, CancellationToken cancellationToken = default)
    {
        // The task is read with the rule of the task itself (its user, or taskuri.view): the journal page is not needed.
        if (await GetAsync(taskId, cancellationToken).ConfigureAwait(false) is null) return [];
        await using var connection = await OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, "SELECT timestamp_utc, actor_username, action, details FROM audit_events WHERE entity_type=@entity AND entity_id=@id ORDER BY timestamp_utc, id", ("@entity", AuditEntities.UserTask), ("@id", taskId.ToString(CultureInfo.InvariantCulture)));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<UserTaskHistoryEntry>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new(MariaTimeText.Parse(reader.GetString(0)), reader.GetString(1), AuditActions.Normalize(reader.GetString(2)), reader.GetString(3)));
        return result;
    }

    // The tasks that existed before the order was introduced (migration 85, whose account has no data rights) are numbered once per run of the application: after the numbers already given, by
    // deadline and then by date of creation. Cheap and harmless when nothing is left to number.
    private static volatile bool orderBackfilled;

    private async Task BackfillOrderAsync(CancellationToken token)
    {
        if (orderBackfilled) return;
        await using var connection = await OpenAsync(configuration, token).ConfigureAwait(false);
        await using var command = Command(connection, null, """
            UPDATE beneficiary_tasks t
            JOIN (SELECT id, ROW_NUMBER() OVER (PARTITION BY assigned_user_id ORDER BY (due_date IS NULL), due_date, created_utc DESC, id) AS rn FROM beneficiary_tasks WHERE status<>@done AND sort_order=0) x ON x.id=t.id
            JOIN (SELECT assigned_user_id, MAX(sort_order) AS mx FROM beneficiary_tasks WHERE status<>@done GROUP BY assigned_user_id) m ON m.assigned_user_id=t.assigned_user_id
            SET t.sort_order=x.rn+m.mx
            """, ("@done", (int)UserTaskStatus.Done));
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        orderBackfilled = true;
    }

    // The place after the last active task of the user.
    private static async Task<int> NextOrderAsync(MySqlConnection connection, MySqlTransaction transaction, int userId, CancellationToken token)
    {
        await using var command = Command(connection, transaction, "SELECT COALESCE(MAX(sort_order), 0) + 1 FROM beneficiary_tasks WHERE assigned_user_id=@user AND status<>@done", ("@user", userId), ("@done", (int)UserTaskStatus.Done));
        return Convert.ToInt32(await command.ExecuteScalarAsync(token).ConfigureAwait(false));
    }

    // The active tasks of the user are numbered 1, 2, 3... again, in their order (after one left the list).
    private static async Task RenumberAsync(MySqlConnection connection, MySqlTransaction transaction, int userId, CancellationToken token)
    {
        await using var command = Command(connection, transaction, """
            UPDATE beneficiary_tasks t JOIN (SELECT id, ROW_NUMBER() OVER (ORDER BY sort_order, id) AS rn FROM beneficiary_tasks WHERE assigned_user_id=@user AND status<>@done) x ON x.id=t.id
            SET t.sort_order=x.rn WHERE t.sort_order<>x.rn
            """, ("@user", userId), ("@done", (int)UserTaskStatus.Done));
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    // ---- Used by the file store (same module): a user who adds a file to the task takes it over too.
    internal static async Task<bool> TakeOverAsync(MySqlConnection connection, MySqlTransaction transaction, int taskId, int userId, string actor, CancellationToken token)
    {
        await using var command = Command(connection, transaction, """
            UPDATE beneficiary_tasks SET accepted_utc=@now, accepted_by=@actor, version=version+1
            WHERE id=@id AND assigned_user_id=@user AND accepted_utc IS NULL
            """, ("@now", MariaTimeText.Format(MariaTimeText.Now())), ("@actor", actor), ("@id", taskId), ("@user", userId));
        return await command.ExecuteNonQueryAsync(token).ConfigureAwait(false) == 1;
    }

    internal async Task RecordAcceptAsync(UserTask task, string how, CancellationToken token) =>
        await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.UserTask, AuditActions.AcceptUserTask, task.Id.ToString(CultureInfo.InvariantCulture),
            UserTaskRules.Target(task), AuditDetails.Changes(new AuditChange("Preluare", "nepreluat", "preluat de " + (task.AcceptedBy ?? "") + " (" + how + ")")), string.Empty, token).ConfigureAwait(false);

    // One journal event per kind of change, so each operation is named exactly.
    private async Task RecordEditsAsync(UserTask before, UserTask after, CancellationToken token)
    {
        var target = UserTaskRules.Target(after);
        var id = after.Id.ToString(CultureInfo.InvariantCulture);
        async Task Record(string action, params AuditChange[] changes)
        {
            if (AuditDetails.Changes(changes).Length == 0) return;
            await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.UserTask, action, id, target, AuditDetails.Changes(changes), string.Empty, token).ConfigureAwait(false);
        }
        await Record(AuditActions.EditUserTask, new("Titlu", before.Title, after.Title), new("Descriere", before.Description, after.Description)).ConfigureAwait(false);
        await Record(AuditActions.EditUserTaskDueDate, new AuditChange("Termen", UserTaskRules.DueText(before.DueDate), UserTaskRules.DueText(after.DueDate))).ConfigureAwait(false);
        await Record(AuditActions.EditUserTaskPriority, new AuditChange("Prioritate", UserTaskRules.PriorityLabel(before.Priority), UserTaskRules.PriorityLabel(after.Priority))).ConfigureAwait(false);
        await Record(AuditActions.MoveUserTask, new AuditChange("Utilizator", before.AssignedUserName, after.AssignedUserName)).ConfigureAwait(false);
        await Record(AuditActions.ChangeUserTaskStatus, new AuditChange("Stare", UserTaskRules.StatusLabel(before.Status), UserTaskRules.StatusLabel(after.Status))).ConfigureAwait(false);
    }

    internal void Publish(string action, UserTask task) =>
        changeFeed?.Publish(new ChangeEvent(AuditEntities.UserTask, action, task.Id.ToString(CultureInfo.InvariantCulture), origin?.Id ?? Guid.Empty, DateTime.UtcNow, BeneficiaryId: task.BeneficiaryId));

    private async Task<UserTask> LoadAsync(int id, CancellationToken token) =>
        (await ReadListAsync("WHERE t.id=@id", [("@id", id)], token).ConfigureAwait(false)).FirstOrDefault()
        ?? throw new UserTaskOperationException("Taskul nu mai există. Actualizează lista.");

    private async Task<IReadOnlyList<UserTask>> ReadListAsync(string where, (string, object?)[] parameters, CancellationToken token)
    {
        await using var connection = await OpenAsync(configuration, token).ConfigureAwait(false);
        await using var command = Command(connection, null, $"{Select} {where} ORDER BY t.created_utc DESC, t.id DESC", parameters);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        var result = new List<UserTask>();
        while (await reader.ReadAsync(token).ConfigureAwait(false)) result.Add(ReadTask(reader));
        return result;
    }

    private static UserTask ReadTask(MySqlDataReader reader)
    {
        var display = reader.GetString(5).Trim();
        return new(checked((int)reader.GetInt64(0)), checked((int)reader.GetInt64(1)), reader.GetString(2), checked((int)reader.GetInt64(3)),
            display.Length > 0 ? display : reader.GetString(4), Convert.ToInt64(reader.GetValue(6)) != 0, reader.GetString(7), reader.GetString(8),
            (UserTaskStatus)Convert.ToInt32(reader.GetValue(9)), reader.IsDBNull(10) ? null : DateOnly.FromDateTime(reader.GetDateTime(10)),
            MariaTimeText.Parse(reader.GetString(11)), reader.GetString(12),
            MariaTimeText.ParseOrNull(reader.IsDBNull(13) ? null : reader.GetString(13)), reader.IsDBNull(14) ? null : reader.GetString(14),
            MariaTimeText.ParseOrNull(reader.IsDBNull(15) ? null : reader.GetString(15)), Convert.ToInt64(reader.GetValue(16)), Convert.ToInt32(reader.GetValue(17)), (UserTaskPriority)Convert.ToInt32(reader.GetValue(18)), reader.GetString(19), Convert.ToInt32(reader.GetValue(20)));
    }

    private static async Task<long> LockVersionAsync(MySqlConnection connection, MySqlTransaction transaction, int id, CancellationToken token)
    {
        await using var command = Command(connection, transaction, "SELECT version FROM beneficiary_tasks WHERE id=@id FOR UPDATE", ("@id", id));
        return await command.ExecuteScalarAsync(token).ConfigureAwait(false) is { } value
            ? Convert.ToInt64(value) : throw new UserTaskOperationException("Taskul nu mai există. Actualizează lista.");
    }

    private static async Task CheckBeneficiaryAsync(MySqlConnection connection, MySqlTransaction transaction, int id, CancellationToken token)
    {
        await using var command = Command(connection, transaction, "SELECT COUNT(*) FROM beneficiaries WHERE id=@id", ("@id", id));
        if (Convert.ToInt32(await command.ExecuteScalarAsync(token).ConfigureAwait(false)) == 0)
            throw new UserTaskOperationException("Beneficiarul nu mai există. Actualizează lista.");
    }

    private static async Task CheckUserAsync(MySqlConnection connection, MySqlTransaction transaction, int id, CancellationToken token)
    {
        await using var command = Command(connection, transaction, "SELECT is_active FROM web_users WHERE id=@id", ("@id", id));
        var active = await command.ExecuteScalarAsync(token).ConfigureAwait(false);
        if (active is null) throw new UserTaskOperationException("Utilizatorul nu mai există. Actualizează lista.");
        if (Convert.ToInt64(active) == 0) throw new UserTaskOperationException("Utilizatorul este dezactivat și nu poate primi taskuri.");
    }

    private static object SqlDate(DateOnly? date) => date is { } value ? value.ToDateTime(TimeOnly.MinValue) : DBNull.Value;

    private Task<T> WriteAsync<T>(Func<MySqlConnection, MySqlTransaction, Task<T>> action, CancellationToken token) =>
        MariaDb.WriteAsync(configuration, action, message => new UserTaskOperationException(message), token);

    private async Task<bool> CanSeeAsync(UserTask task, CancellationToken token)
    {
        if (accessControl is null) return true;
        if (await accessControl.GetUserIdAsync(token).ConfigureAwait(false) == task.AssignedUserId) return true;
        return await accessControl.HasAsync("taskuri.view", token).ConfigureAwait(false);
    }

    private async Task<int?> CurrentUserIdAsync(CancellationToken token) => accessControl is null ? null : await accessControl.GetUserIdAsync(token).ConfigureAwait(false);

    private async Task<string> UsernameAsync(CancellationToken token) =>
        (accessControl is null ? null : await accessControl.GetUsernameAsync(token).ConfigureAwait(false)) ?? "necunoscut";

    private async Task EnsureAsync(string key, CancellationToken token)
    {
        if (accessControl is not null) await accessControl.EnsureAsync(key, token).ConfigureAwait(false);
    }

    private async Task EnsureAnyAsync(IEnumerable<string> keys, CancellationToken token)
    {
        if (accessControl is not null) await accessControl.EnsureAnyAsync(keys, token).ConfigureAwait(false);
    }
}
