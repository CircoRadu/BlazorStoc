using System.Data;
using MySqlConnector;

namespace BlazorStoc.Services;

// Subtask 2.x (Task 2): rewritten against the REAL, already-populated MariaDB schema
// (Livrare-DDL-MariaDB\schema-mariadb.sql) - tables `projects` (id,beneficiary_id,name,normalized_name,
// observations,version,created_utc,updated_utc), `project_observations`
// (id,project_id,name,content,author,version,created_utc,updated_utc), `project_observation_files`. The legacy
// `project`/`project_observation`/`project_observation_file` tables created lazily by the old MariaDB module are
// gone; no CREATE/ALTER is ever issued here (the runtime account `blazorstoc_dev` has only
// SELECT/INSERT/UPDATE/DELETE). Every "_utc" column is TEXT, never a native DATETIME - read/write it exclusively
// through MariaTimeText (see that file for why).
//
// NOTE: the real `projects` table has a foreign key to `beneficiaries` (id,name,...). `MariaBeneficiaryRepository`
// was converted to the same real `beneficiaries` table in this same cycle (Task 2 subtask 2.3), so the beneficiary
// lookups here agree with it.
public sealed class MariaProjectRepository(IConfiguration configuration, IWebHostEnvironment environment,
    IAccessControl? accessControl = null, IAuditTrail? auditTrail = null, IArchiveService? archiveService = null,
    IProjectFileStore? fileStore = null) : IProjectRepository
{
    private readonly IArchiveService archiver = archiveService ?? new ArchiveService(accessControl);
    private readonly IProjectFileStore files = fileStore ?? new MariaProjectFileStore(environment, configuration, accessControl, archiveService);
    private readonly string rootPath = MariaProjectFileStore.RootPath(environment, configuration);
    private readonly string archivePath = MariaProjectFileStore.ArchivePath(environment, configuration);
    private MySqlConnection CreateConnection() => DatabaseConnections.Create(configuration);

    public async Task<IReadOnlyList<Project>> GetForBeneficiaryAsync(int beneficiaryId, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, null, """
            SELECT id,beneficiary_id,name,observations,version,created_utc,updated_utc
            FROM projects WHERE beneficiary_id=@beneficiary ORDER BY name,id
            """, ("@beneficiary", beneficiaryId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<Project>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(Read(reader));
        return result;
    }

    public async Task<Project?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await GetAsync(connection, null, id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Project> CreateAsync(ProjectInput input, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken);
        var value = input.Validated();
        var nowUtc = MariaTimeText.Now(); // A15 fix: match Format/Parse precision so CheckCurrent equality survives a round trip
        try
        {
            var (project, beneficiaryName) = await WriteAsync(async (connection, transaction) =>
            {
                var beneficiaryName = await GetBeneficiaryNameAsync(connection, transaction, value.BeneficiaryId, cancellationToken).ConfigureAwait(false);
                ProjectRules.CheckBeneficiaryExists(beneficiaryName is null ? null : new Beneficiary(value.BeneficiaryId, beneficiaryName, ""));
                await EnsureUniqueNameAsync(connection, transaction, value.BeneficiaryId, value.Name, null, beneficiaryName!, cancellationToken).ConfigureAwait(false);
                await using var command = Command(connection, transaction, """
                    INSERT INTO projects(beneficiary_id,name,normalized_name,observations,version,created_utc,updated_utc)
                    VALUES(@beneficiary,@name,@normalized,@observations,0,@created,@updated)
                    """, ("@beneficiary", value.BeneficiaryId), ("@name", value.Name),
                    ("@normalized", ProjectRules.NormalizedName(value.Name)), ("@observations", value.Observations),
                    ("@created", MariaTimeText.Format(nowUtc)), ("@updated", MariaTimeText.Format(nowUtc)));
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                return (ProjectRules.Create(checked((int)command.LastInsertedId), value, nowUtc), beneficiaryName!);
            }, cancellationToken).ConfigureAwait(false);
            await AuditRecorder.RecordCreateAsync(auditTrail, accessControl, AuditEntities.Project, project.Id.ToString(), project.Name,
                AuditDetails.Identification(("Denumire", project.Name), ("Beneficiar", beneficiaryName), ("Observații", project.Observations)),
                cancellationToken).ConfigureAwait(false);
            return project;
        }
        catch (MySqlException exception) when (IsProjectNameConflict(exception))
        {
            throw new ProjectOperationException(ProjectRules.ConcurrentDuplicateMessage);
        }
    }

    public async Task<Project> UpdateAsync(Project original, ProjectInput input, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken);
        var value = input.Validated(true);
        var nowUtc = MariaTimeText.Now(); // A15 fix: match Format/Parse precision so CheckCurrent equality survives a round trip
        Project updated;
        try
        {
            updated = await WriteAsync(async (connection, transaction) =>
            {
                var current = await GetLockedAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false);
                ProjectRules.CheckCurrent(current, original);
                ProjectRules.CheckBeneficiaryUnchanged(original, value.BeneficiaryId);
                var beneficiaryName = await GetBeneficiaryNameAsync(connection, transaction, value.BeneficiaryId, cancellationToken).ConfigureAwait(false);
                ProjectRules.CheckBeneficiaryExists(beneficiaryName is null ? null : new Beneficiary(value.BeneficiaryId, beneficiaryName, ""));
                await EnsureUniqueNameAsync(connection, transaction, value.BeneficiaryId, value.Name, original.Id, beneficiaryName!, cancellationToken).ConfigureAwait(false);
                var updated = ProjectRules.Edited(original, value, nowUtc);
                await using var command = Command(connection, transaction, """
                    UPDATE projects SET beneficiary_id=@beneficiary,name=@name,normalized_name=@normalized,
                        observations=@observations,version=@version,updated_utc=@updated
                    WHERE id=@id AND version=@oldVersion
                    """, ("@beneficiary", value.BeneficiaryId), ("@name", value.Name),
                    ("@normalized", ProjectRules.NormalizedName(value.Name)), ("@observations", value.Observations),
                    ("@version", updated.Version), ("@updated", MariaTimeText.Format(nowUtc)),
                    ("@id", original.Id), ("@oldVersion", original.Version));
                if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                    throw new ProjectOperationException("Proiectul s-a schimbat între timp. Actualizează pagina.");
                return updated;
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (MySqlException exception) when (IsProjectNameConflict(exception))
        {
            throw new ProjectOperationException(ProjectRules.ConcurrentDuplicateMessage);
        }
        await AuditRecorder.RecordEditAsync(auditTrail, accessControl, AuditEntities.Project, updated.Id.ToString(), updated.Name,
            [new("Denumire", original.Name, updated.Name),
             new("Beneficiar", original.BeneficiaryId.ToString(), updated.BeneficiaryId.ToString()),
             new("Observații", original.Observations, updated.Observations)],
            value.Reason, cancellationToken).ConfigureAwait(false);
        return updated;
    }

    public async Task DeleteAsync(Project original, string reason, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken);
        var motif = ChangeReasonRules.Normalize(reason);
        if (ChangeReasonRules.ValidationError(motif) is { } reasonError) throw new ProjectOperationException(reasonError);
        var observations = await GetObservationsAsync(original.Id, cancellationToken).ConfigureAwait(false);
        var allFiles = new List<ProjectObservationFile>();
        foreach (var observation in observations)
            allFiles.AddRange(await files.GetFilesAsync(observation.Id, cancellationToken).ConfigureAwait(false));
        string beneficiaryName;
        await using (var connection = CreateConnection())
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            beneficiaryName = await GetBeneficiaryNameAsync(connection, null, original.BeneficiaryId, cancellationToken).ConfigureAwait(false) ?? "";
        }

        await archiver.ExecuteAsync(ArchiveRequests.Project(original, beneficiaryName, observations, allFiles, motif),
            async (operation, token) =>
        {
            var prepared = await PreparedFilesAsync(allFiles, operation, token).ConfigureAwait(false);
            try
            {
                await WriteAsync(async (connection, transaction) =>
                {
                    var current = await GetLockedAsync(connection, transaction, original.Id, token).ConfigureAwait(false);
                    ProjectRules.CheckCurrent(current, original);
                    await using (var stock = Command(connection, transaction,
                        "SELECT EXISTS(SELECT 1 FROM stock_movements WHERE project_id=@id)", ("@id", original.Id)))
                        if (Convert.ToBoolean(await stock.ExecuteScalarAsync(token).ConfigureAwait(false)))
                            throw new ProjectOperationException("Proiectul are mișcări de stoc asociate și nu poate fi șters. Istoricul trebuie păstrat.");
                    await MariaArchivePersistence.InsertAsync(connection, transaction, operation, prepared, token).ConfigureAwait(false);
                    await using (var deleteFiles = Command(connection, transaction,
                        "DELETE FROM project_observation_files WHERE observation_id IN (SELECT id FROM project_observations WHERE project_id=@id)",
                        ("@id", original.Id)))
                        await deleteFiles.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    await using (var deleteObservations = Command(connection, transaction,
                        "DELETE FROM project_observations WHERE project_id=@id", ("@id", original.Id)))
                        await deleteObservations.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    await using var deleteProject = Command(connection, transaction,
                        "DELETE FROM projects WHERE id=@id AND version=@version", ("@id", original.Id), ("@version", original.Version));
                    if (await deleteProject.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
                        throw new ProjectOperationException("Proiectul s-a schimbat între timp. Actualizează pagina.");
                    await MariaArchivePersistence.InsertAuditAsync(connection, transaction, operation, token).ConfigureAwait(false);
                    return true;
                }, token).ConfigureAwait(false);
                foreach (var file in prepared)
                    await ArchiveFileSafety.CompleteAsync(rootPath, archivePath, file, token).ConfigureAwait(false);
            }
            catch
            {
                foreach (var file in prepared) ArchiveFileSafety.Rollback(archivePath, file);
                throw;
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ProjectObservation>> GetObservationsAsync(int projectId, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, null, """
            SELECT id,project_id,name,content,author,version,created_utc,updated_utc
            FROM project_observations WHERE project_id=@project ORDER BY created_utc DESC,id DESC
            """, ("@project", projectId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<ProjectObservation>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(ReadObservation(reader));
        return result;
    }

    public async Task<ProjectObservation?> GetObservationAsync(int id, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await GetObservationAsync(connection, null, id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProjectObservation> CreateObservationAsync(int projectId, ProjectObservationInput input, string author,
        CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken);
        var value = input.Validated();
        var nowUtc = MariaTimeText.Now(); // A15 fix: match Format/Parse precision so CheckCurrent equality survives a round trip
        var (observation, projectName) = await WriteAsync(async (connection, transaction) =>
        {
            var projectName = await GetProjectNameAsync(connection, transaction, projectId, cancellationToken).ConfigureAwait(false)
                ?? throw new ProjectOperationException("Proiectul nu mai există.");
            await using var command = Command(connection, transaction, """
                INSERT INTO project_observations(project_id,name,content,author,version,created_utc,updated_utc)
                VALUES(@project,@name,@content,@author,0,@created,@updated)
                """, ("@project", projectId), ("@name", value.Name), ("@content", value.Content), ("@author", author),
                ("@created", MariaTimeText.Format(nowUtc)), ("@updated", MariaTimeText.Format(nowUtc)));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return (ProjectRules.CreateObservation(checked((int)command.LastInsertedId), projectId, value, author, nowUtc), projectName);
        }, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordCreateAsync(auditTrail, accessControl, AuditEntities.ProjectObservation, observation.Id.ToString(),
            observation.Name, AuditDetails.Identification(("Denumire", observation.Name), ("Proiect", projectName), ("Autor", observation.Author)),
            cancellationToken).ConfigureAwait(false);
        return observation;
    }

    public async Task<ProjectObservation> UpdateObservationAsync(ProjectObservation original, ProjectObservationInput input,
        CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken);
        var value = input.Validated(true);
        var nowUtc = MariaTimeText.Now(); // A15 fix: match Format/Parse precision so CheckCurrent equality survives a round trip
        var updated = await WriteAsync(async (connection, transaction) =>
        {
            var current = await GetObservationLockedAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false);
            ProjectRules.CheckCurrent(current, original);
            var updated = ProjectRules.EditedObservation(original, value, nowUtc);
            await using var command = Command(connection, transaction, """
                UPDATE project_observations SET name=@name,content=@content,version=@version,updated_utc=@updated
                WHERE id=@id AND version=@oldVersion
                """, ("@name", value.Name), ("@content", value.Content), ("@version", updated.Version),
                ("@updated", MariaTimeText.Format(nowUtc)), ("@id", original.Id), ("@oldVersion", original.Version));
            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw new ProjectOperationException("Observația s-a schimbat între timp. Actualizează pagina.");
            return updated;
        }, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordEditAsync(auditTrail, accessControl, AuditEntities.ProjectObservation, updated.Id.ToString(), updated.Name,
            [new("Denumire", original.Name, updated.Name), new("Conținut", original.Content, updated.Content)],
            value.Reason, cancellationToken).ConfigureAwait(false);
        return updated;
    }

    public async Task DeleteObservationAsync(ProjectObservation original, string reason, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken);
        var motif = ChangeReasonRules.Normalize(reason);
        if (ChangeReasonRules.ValidationError(motif) is { } reasonError) throw new ProjectOperationException(reasonError);
        var observationFiles = await files.GetFilesAsync(original.Id, cancellationToken).ConfigureAwait(false);
        string? projectName;
        await using (var connection = CreateConnection())
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            projectName = await GetProjectNameAsync(connection, null, original.ProjectId, cancellationToken).ConfigureAwait(false);
        }

        await archiver.ExecuteAsync(ArchiveRequests.ProjectObservation(original, projectName ?? "", observationFiles, motif),
            async (operation, token) =>
        {
            var prepared = await PreparedFilesAsync(observationFiles, operation, token).ConfigureAwait(false);
            try
            {
                await WriteAsync(async (connection, transaction) =>
                {
                    var current = await GetObservationLockedAsync(connection, transaction, original.Id, token).ConfigureAwait(false);
                    ProjectRules.CheckCurrent(current, original);
                    await MariaArchivePersistence.InsertAsync(connection, transaction, operation, prepared, token).ConfigureAwait(false);
                    await using (var deleteFiles = Command(connection, transaction,
                        "DELETE FROM project_observation_files WHERE observation_id=@id", ("@id", original.Id)))
                        await deleteFiles.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    await using var delete = Command(connection, transaction,
                        "DELETE FROM project_observations WHERE id=@id AND version=@version", ("@id", original.Id), ("@version", original.Version));
                    if (await delete.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
                        throw new ProjectOperationException("Observația s-a schimbat între timp. Actualizează pagina.");
                    await MariaArchivePersistence.InsertAuditAsync(connection, transaction, operation, token).ConfigureAwait(false);
                    return true;
                }, token).ConfigureAwait(false);
                foreach (var file in prepared)
                    await ArchiveFileSafety.CompleteAsync(rootPath, archivePath, file, token).ConfigureAwait(false);
            }
            catch
            {
                foreach (var file in prepared) ArchiveFileSafety.Rollback(archivePath, file);
                throw;
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<ArchiveFileRecord>> PreparedFilesAsync(IEnumerable<ProjectObservationFile> observationFiles,
        ArchiveOperation operation, CancellationToken token)
    {
        var prepared = new List<ArchiveFileRecord>();
        foreach (var file in observationFiles)
            prepared.Add(await ArchiveFileSafety.PrepareAsync(rootPath, archivePath, file.StoredName, operation,
                AuditEntities.ProjectObservationFile, file.Id.ToString(), file.ContentType, file.OriginalName,
                file.SizeBytes, token).ConfigureAwait(false));
        return prepared;
    }

    private Task<T> WriteAsync<T>(Func<MySqlConnection, MySqlTransaction, Task<T>> action, CancellationToken token) =>
        MariaTransactions.RetryOnDeadlockAsync(() => WriteOnceAsync(action, token), token);

    private async Task<T> WriteOnceAsync<T>(Func<MySqlConnection, MySqlTransaction, Task<T>> action, CancellationToken token)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(token).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, token).ConfigureAwait(false);
        try
        {
            var result = await action(connection, transaction).ConfigureAwait(false);
            await transaction.CommitAsync(token).ConfigureAwait(false);
            return result;
        }
        catch
        {
            if (transaction.Connection is not null) await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<Project?> GetAsync(MySqlConnection connection, MySqlTransaction? transaction, int id, CancellationToken token)
    {
        await using var command = Command(connection, transaction,
            "SELECT id,beneficiary_id,name,observations,version,created_utc,updated_utc FROM projects WHERE id=@id",
            ("@id", id));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        return await reader.ReadAsync(token).ConfigureAwait(false) ? Read(reader) : null;
    }

    private static async Task<Project?> GetLockedAsync(MySqlConnection connection, MySqlTransaction transaction, int id, CancellationToken token)
    {
        await using var command = Command(connection, transaction,
            "SELECT id,beneficiary_id,name,observations,version,created_utc,updated_utc FROM projects WHERE id=@id FOR UPDATE",
            ("@id", id));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        return await reader.ReadAsync(token).ConfigureAwait(false) ? Read(reader) : null;
    }

    private static async Task<ProjectObservation?> GetObservationAsync(MySqlConnection connection, MySqlTransaction? transaction, int id, CancellationToken token)
    {
        await using var command = Command(connection, transaction,
            "SELECT id,project_id,name,content,author,version,created_utc,updated_utc FROM project_observations WHERE id=@id",
            ("@id", id));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        return await reader.ReadAsync(token).ConfigureAwait(false) ? ReadObservation(reader) : null;
    }

    private static async Task<ProjectObservation?> GetObservationLockedAsync(MySqlConnection connection, MySqlTransaction transaction, int id, CancellationToken token)
    {
        await using var command = Command(connection, transaction,
            "SELECT id,project_id,name,content,author,version,created_utc,updated_utc FROM project_observations WHERE id=@id FOR UPDATE",
            ("@id", id));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        return await reader.ReadAsync(token).ConfigureAwait(false) ? ReadObservation(reader) : null;
    }

    // `projects.beneficiary_id` references the real `beneficiaries` table (id,name,...) - see the class-level note.
    private static async Task<string?> GetBeneficiaryNameAsync(MySqlConnection connection, MySqlTransaction? transaction, int beneficiaryId, CancellationToken token)
    {
        await using var command = Command(connection, transaction,
            "SELECT name FROM beneficiaries WHERE id=@id", ("@id", beneficiaryId));
        return await command.ExecuteScalarAsync(token).ConfigureAwait(false) as string;
    }

    private static async Task<string?> GetProjectNameAsync(MySqlConnection connection, MySqlTransaction? transaction, int projectId, CancellationToken token)
    {
        await using var command = Command(connection, transaction, "SELECT name FROM projects WHERE id=@id", ("@id", projectId));
        return await command.ExecuteScalarAsync(token).ConfigureAwait(false) as string;
    }

    private static async Task EnsureUniqueNameAsync(MySqlConnection connection, MySqlTransaction transaction, int beneficiaryId,
        string name, int? excludedId, string beneficiaryName, CancellationToken token)
    {
        await using var command = Command(connection, transaction, """
            SELECT name FROM projects WHERE beneficiary_id=@beneficiary AND normalized_name=@normalized
                AND (@id IS NULL OR id<>@id) LIMIT 1 FOR UPDATE
            """, ("@beneficiary", beneficiaryId), ("@normalized", ProjectRules.NormalizedName(name)),
            ("@id", excludedId is null ? DBNull.Value : excludedId.Value));
        if (await command.ExecuteScalarAsync(token).ConfigureAwait(false) is string existingName)
            throw new ProjectOperationException(ProjectRules.DuplicateMessage(existingName, beneficiaryName));
    }

    private static Project Read(MySqlDataReader reader) => new(reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2),
        reader.GetString(3), reader.GetInt64(4), MariaTimeText.Parse(reader.GetString(5)), MariaTimeText.Parse(reader.GetString(6)));

    private static ProjectObservation ReadObservation(MySqlDataReader reader) => new(reader.GetInt32(0), reader.GetInt32(1),
        reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetInt64(5),
        MariaTimeText.Parse(reader.GetString(6)), MariaTimeText.Parse(reader.GetString(7)));

    // The pre-check in EnsureUniqueNameAsync (locking read inside the same SERIALIZABLE transaction) should make
    // this unreachable in practice, but a concurrent insert racing the lock acquisition still hits
    // uq_projects_1(beneficiary_id,normalized_name); translate it to the same Romanian message Sqlite uses.
    private static bool IsProjectNameConflict(MySqlException exception) =>
        exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry &&
        exception.Message.Contains("projects", StringComparison.OrdinalIgnoreCase);

    private static MySqlCommand Command(MySqlConnection connection, MySqlTransaction? transaction, string sql, params (string Name, object Value)[] parameters)
    {
        var command = new MySqlCommand(sql, connection, transaction);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return command;
    }

    private Task EnsureOperatorAsync(CancellationToken token) => accessControl?.EnsureBeneficiaryOperatorAsync(token) ?? Task.CompletedTask;
}
