using System.Data;
using System.Globalization;
using MySqlConnector;
using BlazorStoc.Services;
using static BlazorStoc.Services.MariaDb;

namespace BlazorStoc.Modules.Tasks;

// The files of a task: rows of `entity_files` with owner_kind 'task' under the same asset directory as the other attached files (so the backup carries them), checked and stored
// like them (EntityFileRules). The general store (MariaEntityFileStore) does not know this kind, so the files of a task never appear in "Fișiere beneficiar".
// Access: the user the task is addressed to, or whoever holds the keys of the tasks (view to read, add or edit to attach, edit to delete any file); the user deletes only what they uploaded.
public sealed class MariaUserTaskFileStore(IConfiguration configuration, IAccessControl? accessControl = null, IAuditTrail? auditTrail = null,
    IUserTaskRepository? tasks = null, IChangeFeed? changeFeed = null, ChangeOrigin? origin = null) : IUserTaskFiles
{
    public const string OwnerKind = EntityFileOwners.UserTask;
    private const string Columns = "id, owner_kind, owner_id, relative_path, original_name, content_type, byte_length, sha256, uploaded_by, uploaded_utc";
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string rootPath = MariaAssetPaths.EntityFiles(configuration);

    public async Task<IReadOnlyList<EntityFile>> GetAsync(int taskId, CancellationToken cancellationToken = default)
    {
        await EnsureAccessAsync(taskId, ["taskuri.view"], cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        return await ReadAsync(connection, "owner_kind='task' AND owner_id=@id", cancellationToken, ("@id", taskId)).ConfigureAwait(false);
    }

    public async Task<EntityFileContent?> GetContentAsync(int fileId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        var file = (await ReadAsync(connection, "id=@id AND owner_kind='task'", cancellationToken, ("@id", fileId)).ConfigureAwait(false)).FirstOrDefault();
        if (file is null) return null;
        await EnsureAccessAsync(file.OwnerId, ["taskuri.view"], cancellationToken).ConfigureAwait(false);
        var path = Resolve(file.StoredName);
        return File.Exists(path) ? new(await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false), file.ContentType, file.OriginalName) : null;
    }

    public async Task<EntityFile> AddAsync(int taskId, string originalName, string declaredContentType, byte[] content, CancellationToken cancellationToken = default)
    {
        await EnsureAccessAsync(taskId, ["taskuri.add", "taskuri.edit"], cancellationToken).ConfigureAwait(false);
        if (!MariaDatabaseGuard.IsAllowedDatabase(configuration)) throw new UserTaskOperationException(MariaDb.ForbiddenDatabaseMessage);
        var contentType = UserTaskFileRules.ContentType(content, declaredContentType, originalName);
        var displayName = EntityFileRules.SafeOriginalName(originalName);
        var hash = ServicePhotoRules.Hash(content);
        var author = (accessControl is null ? null : await accessControl.GetUsernameAsync(cancellationToken).ConfigureAwait(false)) ?? "necunoscut";
        var userId = accessControl is null ? null : await accessControl.GetUserIdAsync(cancellationToken).ConfigureAwait(false);
        var storedName = ProjectFileRules.NewStoredName(Path.GetExtension(displayName));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(rootPath);
            var destination = Resolve(storedName);
            await File.WriteAllBytesAsync(destination, content, cancellationToken).ConfigureAwait(false);
            try
            {
                await using var connection = await OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
                await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
                EntityFile saved; string title; string beneficiaryName; bool takenOver = false; int beneficiaryId;
                try
                {
                    await using (var lookup = Command(connection, transaction, """
                        SELECT t.title, b.name, t.beneficiary_id FROM beneficiary_tasks t JOIN beneficiaries b ON b.id=t.beneficiary_id WHERE t.id=@id FOR UPDATE
                        """, ("@id", taskId)))
                    await using (var reader = await lookup.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                    {
                        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                            throw new UserTaskOperationException("Taskul nu mai există. Actualizează pagina înainte să reîncerci încărcarea.");
                        (title, beneficiaryName, beneficiaryId) = (reader.GetString(0), reader.GetString(1), checked((int)reader.GetInt64(2)));
                    }
                    await using (var count = Command(connection, transaction, "SELECT COUNT(*) FROM entity_files WHERE owner_kind='task' AND owner_id=@id", ("@id", taskId)))
                        if (Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) >= EntityFileRules.MaximumPerOwner)
                            throw EntityFileRules.LimitReached();
                    var uploaded = MariaTimeText.Now();
                    await using var insert = Command(connection, transaction, """
                        INSERT INTO entity_files (owner_kind, owner_id, relative_path, original_name, content_type, byte_length, sha256, uploaded_by, uploaded_utc)
                        VALUES ('task', @owner, @path, @name, @type, @length, @hash, @author, @uploaded)
                        """, ("@owner", taskId), ("@path", storedName), ("@name", displayName), ("@type", contentType), ("@length", content.LongLength), ("@hash", hash),
                        ("@author", author), ("@uploaded", MariaTimeText.Format(uploaded)));
                    try { await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false); }
                    catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry) { throw EntityFileRules.Duplicate(); }
                    saved = new(checked((int)insert.LastInsertedId), OwnerKind, taskId, displayName, storedName, contentType, content.LongLength, hash, author, uploaded);
                    // The user who adds a file to their task takes it over too.
                    if (userId is { } id) takenOver = await MariaUserTaskRepository.TakeOverAsync(connection, transaction, taskId, id, author, cancellationToken).ConfigureAwait(false);
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                    throw;
                }
                var target = $"#{taskId} · {title} · {beneficiaryName}";
                await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.UserTask, AuditActions.AddUserTaskFile, taskId.ToString(CultureInfo.InvariantCulture), target,
                    AuditDetails.Identification(("Nume fișier", saved.OriginalName), ("Dimensiune (octeți)", saved.SizeBytes.ToString(CultureInfo.InvariantCulture)), ("Autor încărcare", saved.UploadedBy)),
                    string.Empty, cancellationToken).ConfigureAwait(false);
                if (takenOver)
                    await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.UserTask, AuditActions.AcceptUserTask, taskId.ToString(CultureInfo.InvariantCulture), target,
                        AuditDetails.Changes(new AuditChange("Preluare", "nepreluat", "preluat de " + author + " (preluat prin adăugarea unui fișier)")), string.Empty, cancellationToken).ConfigureAwait(false);
                changeFeed?.Publish(new ChangeEvent(AuditEntities.UserTask, AuditActions.AddUserTaskFile, taskId.ToString(CultureInfo.InvariantCulture), origin?.Id ?? Guid.Empty, DateTime.UtcNow, BeneficiaryId: beneficiaryId));
                return saved;
            }
            catch
            {
                if (File.Exists(destination)) File.Delete(destination);
                throw;
            }
        }
        finally { gate.Release(); }
    }

    public async Task DeleteAsync(int fileId, CancellationToken cancellationToken = default)
    {
        EntityFile? file; string title = "", beneficiaryName = ""; int beneficiaryId = 0;
        await using (var connection = await OpenAsync(configuration, cancellationToken).ConfigureAwait(false))
        {
            file = (await ReadAsync(connection, "id=@id AND owner_kind='task'", cancellationToken, ("@id", fileId)).ConfigureAwait(false)).FirstOrDefault();
            if (file is null) throw new UserTaskOperationException("Fișierul a fost șters deja. Actualizează pagina.");
            await using var lookup = Command(connection, "SELECT t.title, b.name, t.beneficiary_id FROM beneficiary_tasks t JOIN beneficiaries b ON b.id=t.beneficiary_id WHERE t.id=@id", ("@id", file.OwnerId));
            await using var reader = await lookup.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) (title, beneficiaryName, beneficiaryId) = (reader.GetString(0), reader.GetString(1), checked((int)reader.GetInt64(2)));
        }
        // Whoever holds taskuri.edit deletes any file; the user of the task only the files they uploaded themselves (never those of the administrator).
        var canEditAll = accessControl is null || await accessControl.HasAsync("taskuri.edit", cancellationToken).ConfigureAwait(false);
        if (!canEditAll)
        {
            await EnsureAccessAsync(file.OwnerId, [], cancellationToken).ConfigureAwait(false);
            var me = accessControl is null ? null : await accessControl.GetUsernameAsync(cancellationToken).ConfigureAwait(false);
            if (!string.Equals(file.UploadedBy, me, StringComparison.Ordinal))
                throw new AccessDeniedException("Poți șterge numai fișierele adăugate de tine.");
        }
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
            await using var delete = Command(connection, "DELETE FROM entity_files WHERE id=@id AND owner_kind='task'", ("@id", fileId));
            if (await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw new UserTaskOperationException("Fișierul a fost șters deja. Actualizează pagina.");
            var path = Resolve(file.StoredName);
            if (File.Exists(path)) File.Delete(path);
        }
        finally { gate.Release(); }
        await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.UserTask, AuditActions.DeleteUserTaskFile, file.OwnerId.ToString(CultureInfo.InvariantCulture),
            $"#{file.OwnerId} · {title} · {beneficiaryName}",
            AuditDetails.Identification(("Nume fișier", file.OriginalName), ("Dimensiune (octeți)", file.SizeBytes.ToString(CultureInfo.InvariantCulture)), ("Autor încărcare", file.UploadedBy)),
            string.Empty, cancellationToken).ConfigureAwait(false);
        changeFeed?.Publish(new ChangeEvent(AuditEntities.UserTask, AuditActions.DeleteUserTaskFile, file.OwnerId.ToString(CultureInfo.InvariantCulture), origin?.Id ?? Guid.Empty, DateTime.UtcNow, BeneficiaryId: beneficiaryId));
    }

    // The user the task is addressed to, or a holder of one of the keys, may act; anyone else is refused (the task is not even confirmed to exist).
    private async Task EnsureAccessAsync(int taskId, IReadOnlyList<string> keys, CancellationToken token)
    {
        if (accessControl is null) return;
        if (tasks is not null)
        {
            if (await tasks.GetAsync(taskId, token).ConfigureAwait(false) is { } task && await accessControl.GetUserIdAsync(token).ConfigureAwait(false) == task.AssignedUserId) return;
        }
        else if (await IsAssigneeAsync(taskId, token).ConfigureAwait(false)) return;
        foreach (var key in keys)
            if (await accessControl.HasAsync(key, token).ConfigureAwait(false)) return;
        throw new AccessDeniedException("Nu ai dreptul necesar pentru această operație.");
    }

    private async Task<bool> IsAssigneeAsync(int taskId, CancellationToken token)
    {
        var userId = accessControl is null ? null : await accessControl.GetUserIdAsync(token).ConfigureAwait(false);
        if (userId is null) return false;
        await using var connection = await OpenAsync(configuration, token).ConfigureAwait(false);
        await using var command = Command(connection, "SELECT COUNT(*) FROM beneficiary_tasks WHERE id=@id AND assigned_user_id=@user", ("@id", taskId), ("@user", userId));
        return Convert.ToInt32(await command.ExecuteScalarAsync(token).ConfigureAwait(false)) > 0;
    }

    private string Resolve(string storedName)
    {
        var path = Path.GetFullPath(Path.Combine(rootPath, storedName));
        if (!path.StartsWith(Path.GetFullPath(rootPath) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new UserTaskOperationException("Calea fișierului nu este validă.");
        return path;
    }

    private static async Task<IReadOnlyList<EntityFile>> ReadAsync(MySqlConnection connection, string where, CancellationToken token, params (string Name, object Value)[] parameters)
    {
        await using var command = new MySqlCommand($"SELECT {Columns} FROM entity_files WHERE {where} ORDER BY uploaded_utc, id", connection);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        var result = new List<EntityFile>();
        while (await reader.ReadAsync(token).ConfigureAwait(false))
            result.Add(new(checked((int)reader.GetInt64(0)), reader.GetString(1), checked((int)reader.GetInt64(2)), reader.GetString(4), reader.GetString(3), reader.GetString(5),
                reader.GetInt64(6), reader.GetString(7), reader.GetString(8), MariaTimeText.Parse(reader.GetString(9))));
        return result;
    }
}
