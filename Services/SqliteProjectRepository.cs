using System.Data;
using Microsoft.Data.Sqlite;

namespace BlazorStoc.Services;

public sealed class SqliteProjectRepository(SqliteLocalStore store, IAccessControl? accessControl = null,
    IArchiveService? archiveService = null, IProjectFileStore? fileStore = null) : IProjectRepository
{
    private readonly IArchiveService archiver = archiveService ?? new ArchiveService(accessControl);
    private readonly IProjectFileStore files = fileStore ?? new SqliteProjectFileStore(store);

    public async Task<IReadOnlyList<Project>> GetForBeneficiaryAsync(int beneficiaryId, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await store.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = SqliteLocalStore.Command(connection, null, """
            SELECT id,beneficiary_id,name,observations,version,created_utc,updated_utc
            FROM projects WHERE beneficiary_id=@beneficiary ORDER BY name,id
            """, ("@beneficiary", beneficiaryId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<Project>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(ReadProject(reader));
        return result;
    }

    public async Task<Project?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await store.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await GetAsync(connection, null, id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Project> CreateAsync(ProjectInput input, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        var value = input.Validated();
        var actor = await SqliteRepositoryAudit.ActorAsync(accessControl, cancellationToken).ConfigureAwait(false);
        var nowUtc = DateTime.UtcNow;
        await using var connection = await store.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        try
        {
            var beneficiaryName = await GetBeneficiaryNameAsync(connection, transaction, value.BeneficiaryId, cancellationToken).ConfigureAwait(false);
            ProjectRules.CheckBeneficiaryExists(beneficiaryName is null ? null : new Beneficiary(value.BeneficiaryId, beneficiaryName, ""));
            await EnsureUniqueNameAsync(connection, transaction, value.BeneficiaryId, value.Name, null, beneficiaryName!, cancellationToken).ConfigureAwait(false);
            await using var insert = SqliteLocalStore.Command(connection, transaction, """
                INSERT INTO projects(beneficiary_id,name,normalized_name,observations,version,created_utc,updated_utc)
                VALUES(@beneficiary,@name,@normalized,@observations,0,@created,@updated);
                SELECT last_insert_rowid();
                """, ("@beneficiary", value.BeneficiaryId), ("@name", value.Name),
                ("@normalized", ProjectRules.NormalizedName(value.Name)), ("@observations", value.Observations),
                ("@created", nowUtc.ToString("O")), ("@updated", nowUtc.ToString("O")));
            var id = checked((int)(long)(await insert.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!);
            var project = ProjectRules.Create(id, value, nowUtc);
            await SqliteLocalStore.InsertAuditAsync(connection, transaction, new(actor.Username, actor.Role,
                AuditEntities.Project, AuditActions.Create, project.Name,
                AuditDetails.Identification(("Denumire", project.Name), ("Beneficiar", beneficiaryName!),
                    ("Observații", project.Observations)), string.Empty, project.Id.ToString()), cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return project;
        }
        catch (SqliteException exception) when (IsProjectNameConflict(exception))
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw new ProjectOperationException(ProjectRules.ConcurrentDuplicateMessage);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<Project> UpdateAsync(Project original, ProjectInput input, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        var value = input.Validated(true);
        var actor = await SqliteRepositoryAudit.ActorAsync(accessControl, cancellationToken).ConfigureAwait(false);
        var nowUtc = DateTime.UtcNow;
        await using var connection = await store.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        try
        {
            ProjectRules.CheckCurrent(await GetAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false), original);
            var beneficiaryName = await GetBeneficiaryNameAsync(connection, transaction, value.BeneficiaryId, cancellationToken).ConfigureAwait(false);
            ProjectRules.CheckBeneficiaryExists(beneficiaryName is null ? null : new Beneficiary(value.BeneficiaryId, beneficiaryName, ""));
            await EnsureUniqueNameAsync(connection, transaction, value.BeneficiaryId, value.Name, original.Id, beneficiaryName!, cancellationToken).ConfigureAwait(false);
            var updated = ProjectRules.Edited(original, value, nowUtc);
            await using var update = SqliteLocalStore.Command(connection, transaction, """
                UPDATE projects SET beneficiary_id=@beneficiary,name=@name,normalized_name=@normalized,
                    observations=@observations,version=@version,updated_utc=@updated
                WHERE id=@id AND version=@oldVersion
                """, ("@beneficiary", value.BeneficiaryId), ("@name", value.Name),
                ("@normalized", ProjectRules.NormalizedName(value.Name)), ("@observations", value.Observations),
                ("@version", updated.Version), ("@updated", nowUtc.ToString("O")),
                ("@id", original.Id), ("@oldVersion", original.Version));
            if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw new ProjectOperationException("Proiectul s-a schimbat între timp. Actualizează pagina.");
            await SqliteLocalStore.InsertAuditAsync(connection, transaction, new(actor.Username, actor.Role,
                AuditEntities.Project, AuditActions.Edit, updated.Name,
                AuditDetails.Changes(
                    new AuditChange("Denumire", original.Name, updated.Name),
                    new AuditChange("Beneficiar", original.BeneficiaryId.ToString(), updated.BeneficiaryId.ToString()),
                    new AuditChange("Observații", original.Observations, updated.Observations)),
                value.Reason, updated.Id.ToString()), cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return updated;
        }
        catch (SqliteException exception) when (IsProjectNameConflict(exception))
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw new ProjectOperationException(ProjectRules.ConcurrentDuplicateMessage);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task DeleteAsync(Project original, string reason, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        var motif = ChangeReasonRules.Normalize(reason);
        if (ChangeReasonRules.ValidationError(motif) is { } reasonError) throw new ProjectOperationException(reasonError);
        var beneficiaryName = await LoadBeneficiaryNameAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new ProjectOperationException("Beneficiarul proiectului nu mai există.");
        var observations = await GetObservationsAsync(original.Id, cancellationToken).ConfigureAwait(false);
        var allFiles = new List<ProjectObservationFile>();
        foreach (var observation in observations)
            allFiles.AddRange(await files.GetFilesAsync(observation.Id, cancellationToken).ConfigureAwait(false));

        async Task<string?> LoadBeneficiaryNameAsync(CancellationToken token)
        {
            await using var connection = await store.OpenConnectionAsync(token).ConfigureAwait(false);
            return await GetBeneficiaryNameAsync(connection, null, original.BeneficiaryId, token).ConfigureAwait(false);
        }

        await archiver.ExecuteAsync(ArchiveRequests.Project(original, beneficiaryName, observations, allFiles, motif),
            async (operation, token) =>
        {
            var preparedFiles = new List<ArchiveFileRecord>();
            try
            {
                foreach (var file in allFiles)
                    preparedFiles.Add(await ArchiveFileSafety.PrepareAsync(store.ProjectFilesPath, store.ArchiveFilesPath,
                        RelativePathFor(file), operation, AuditEntities.ProjectObservationFile, file.Id.ToString(),
                        file.ContentType, file.OriginalName, file.SizeBytes, token).ConfigureAwait(false));

                await using var connection = await store.OpenConnectionAsync(token).ConfigureAwait(false);
                await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, token).ConfigureAwait(false);
                try
                {
                    ProjectRules.CheckCurrent(await GetAsync(connection, transaction, original.Id, token).ConfigureAwait(false), original);
                    await using (var stock = SqliteLocalStore.Command(connection, transaction,
                        "SELECT EXISTS(SELECT 1 FROM stock_movements WHERE project_id=@id)", ("@id", original.Id)))
                        if (Convert.ToBoolean(await stock.ExecuteScalarAsync(token).ConfigureAwait(false)))
                            throw new ProjectOperationException("Proiectul are mișcări de stoc asociate și nu poate fi șters. Istoricul trebuie păstrat.");
                    await SqliteArchivePersistence.InsertAsync(connection, transaction, operation, preparedFiles, token).ConfigureAwait(false);
                    await using (var deleteFiles = SqliteLocalStore.Command(connection, transaction,
                        "DELETE FROM project_observation_files WHERE observation_id IN (SELECT id FROM project_observations WHERE project_id=@id)",
                        ("@id", original.Id)))
                        await deleteFiles.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    await using (var deleteObservations = SqliteLocalStore.Command(connection, transaction,
                        "DELETE FROM project_observations WHERE project_id=@id", ("@id", original.Id)))
                        await deleteObservations.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    await using var deleteProject = SqliteLocalStore.Command(connection, transaction,
                        "DELETE FROM projects WHERE id=@id AND version=@version", ("@id", original.Id), ("@version", original.Version));
                    if (await deleteProject.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
                        throw new ProjectOperationException("Proiectul s-a schimbat între timp. Actualizează pagina.");
                    await SqliteLocalStore.InsertAuditAsync(connection, transaction, new(operation.ActorUsername, operation.ActorRole,
                        AuditEntities.Project, AuditActions.Delete, operation.Request.Target, operation.Request.Details,
                        operation.Request.Motif, original.Id.ToString(), operation.Id), token).ConfigureAwait(false);
                    await transaction.CommitAsync(token).ConfigureAwait(false);
                }
                catch
                {
                    await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                    throw;
                }
                foreach (var file in preparedFiles) await ArchiveFileSafety.CompleteAsync(store.ProjectFilesPath, store.ArchiveFilesPath, file, token).ConfigureAwait(false);
            }
            catch
            {
                foreach (var file in preparedFiles) ArchiveFileSafety.Rollback(store.ArchiveFilesPath, file);
                throw;
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ProjectObservation>> GetObservationsAsync(int projectId, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await store.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = SqliteLocalStore.Command(connection, null, """
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
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await store.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await GetObservationAsync(connection, null, id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProjectObservation> CreateObservationAsync(int projectId, ProjectObservationInput input, string author,
        CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        var value = input.Validated();
        var actor = await SqliteRepositoryAudit.ActorAsync(accessControl, cancellationToken).ConfigureAwait(false);
        var nowUtc = DateTime.UtcNow;
        await using var connection = await store.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        try
        {
            var projectName = await GetProjectNameAsync(connection, transaction, projectId, cancellationToken).ConfigureAwait(false)
                ?? throw new ProjectOperationException("Proiectul nu mai există.");
            await using var insert = SqliteLocalStore.Command(connection, transaction, """
                INSERT INTO project_observations(project_id,name,content,author,version,created_utc,updated_utc)
                VALUES(@project,@name,@content,@author,0,@created,@updated);
                SELECT last_insert_rowid();
                """, ("@project", projectId), ("@name", value.Name), ("@content", value.Content), ("@author", author),
                ("@created", nowUtc.ToString("O")), ("@updated", nowUtc.ToString("O")));
            var id = checked((int)(long)(await insert.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!);
            var observation = ProjectRules.CreateObservation(id, projectId, value, author, nowUtc);
            await SqliteLocalStore.InsertAuditAsync(connection, transaction, new(actor.Username, actor.Role,
                AuditEntities.ProjectObservation, AuditActions.Create, observation.Name,
                AuditDetails.Identification(("Denumire", observation.Name), ("Proiect", projectName), ("Autor", observation.Author)),
                string.Empty, observation.Id.ToString()), cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return observation;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<ProjectObservation> UpdateObservationAsync(ProjectObservation original, ProjectObservationInput input,
        CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        var value = input.Validated(true);
        var actor = await SqliteRepositoryAudit.ActorAsync(accessControl, cancellationToken).ConfigureAwait(false);
        var nowUtc = DateTime.UtcNow;
        await using var connection = await store.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        try
        {
            ProjectRules.CheckCurrent(await GetObservationAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false), original);
            var updated = ProjectRules.EditedObservation(original, value, nowUtc);
            await using var update = SqliteLocalStore.Command(connection, transaction, """
                UPDATE project_observations SET name=@name,content=@content,version=@version,updated_utc=@updated
                WHERE id=@id AND version=@oldVersion
                """, ("@name", value.Name), ("@content", value.Content), ("@version", updated.Version),
                ("@updated", nowUtc.ToString("O")), ("@id", original.Id), ("@oldVersion", original.Version));
            if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw new ProjectOperationException("Observația s-a schimbat între timp. Actualizează pagina.");
            await SqliteLocalStore.InsertAuditAsync(connection, transaction, new(actor.Username, actor.Role,
                AuditEntities.ProjectObservation, AuditActions.Edit, updated.Name,
                AuditDetails.Changes(new AuditChange("Denumire", original.Name, updated.Name),
                    new AuditChange("Conținut", original.Content, updated.Content)),
                value.Reason, updated.Id.ToString()), cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return updated;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task DeleteObservationAsync(ProjectObservation original, string reason, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        var motif = ChangeReasonRules.Normalize(reason);
        if (ChangeReasonRules.ValidationError(motif) is { } reasonError) throw new ProjectOperationException(reasonError);
        var projectName = await LoadProjectNameAsync(cancellationToken).ConfigureAwait(false) ?? "";
        var observationFiles = await files.GetFilesAsync(original.Id, cancellationToken).ConfigureAwait(false);

        async Task<string?> LoadProjectNameAsync(CancellationToken token)
        {
            await using var connection = await store.OpenConnectionAsync(token).ConfigureAwait(false);
            return await GetProjectNameAsync(connection, null, original.ProjectId, token).ConfigureAwait(false);
        }

        await archiver.ExecuteAsync(ArchiveRequests.ProjectObservation(original, projectName, observationFiles, motif),
            async (operation, token) =>
        {
            var preparedFiles = new List<ArchiveFileRecord>();
            try
            {
                foreach (var file in observationFiles)
                    preparedFiles.Add(await ArchiveFileSafety.PrepareAsync(store.ProjectFilesPath, store.ArchiveFilesPath,
                        RelativePathFor(file), operation, AuditEntities.ProjectObservationFile, file.Id.ToString(),
                        file.ContentType, file.OriginalName, file.SizeBytes, token).ConfigureAwait(false));

                await using var connection = await store.OpenConnectionAsync(token).ConfigureAwait(false);
                await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, token).ConfigureAwait(false);
                try
                {
                    ProjectRules.CheckCurrent(await GetObservationAsync(connection, transaction, original.Id, token).ConfigureAwait(false), original);
                    await SqliteArchivePersistence.InsertAsync(connection, transaction, operation, preparedFiles, token).ConfigureAwait(false);
                    await using (var deleteFiles = SqliteLocalStore.Command(connection, transaction,
                        "DELETE FROM project_observation_files WHERE observation_id=@id", ("@id", original.Id)))
                        await deleteFiles.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    await using var delete = SqliteLocalStore.Command(connection, transaction,
                        "DELETE FROM project_observations WHERE id=@id AND version=@version", ("@id", original.Id), ("@version", original.Version));
                    if (await delete.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
                        throw new ProjectOperationException("Observația s-a schimbat între timp. Actualizează pagina.");
                    await SqliteLocalStore.InsertAuditAsync(connection, transaction, new(operation.ActorUsername, operation.ActorRole,
                        AuditEntities.ProjectObservation, AuditActions.Delete, operation.Request.Target, operation.Request.Details,
                        operation.Request.Motif, original.Id.ToString(), operation.Id), token).ConfigureAwait(false);
                    await transaction.CommitAsync(token).ConfigureAwait(false);
                }
                catch
                {
                    await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                    throw;
                }
                foreach (var file in preparedFiles) await ArchiveFileSafety.CompleteAsync(store.ProjectFilesPath, store.ArchiveFilesPath, file, token).ConfigureAwait(false);
            }
            catch
            {
                foreach (var file in preparedFiles) ArchiveFileSafety.Rollback(store.ArchiveFilesPath, file);
                throw;
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    internal static string RelativePathFor(ProjectObservationFile file) => file.StoredName;

    private static async Task<Project?> GetAsync(SqliteConnection connection, SqliteTransaction? transaction, int id, CancellationToken token)
    {
        await using var command = SqliteLocalStore.Command(connection, transaction, """
            SELECT id,beneficiary_id,name,observations,version,created_utc,updated_utc FROM projects WHERE id=@id
            """, ("@id", id));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        return await reader.ReadAsync(token).ConfigureAwait(false) ? ReadProject(reader) : null;
    }

    private static async Task<ProjectObservation?> GetObservationAsync(SqliteConnection connection, SqliteTransaction? transaction, int id, CancellationToken token)
    {
        await using var command = SqliteLocalStore.Command(connection, transaction, """
            SELECT id,project_id,name,content,author,version,created_utc,updated_utc FROM project_observations WHERE id=@id
            """, ("@id", id));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        return await reader.ReadAsync(token).ConfigureAwait(false) ? ReadObservation(reader) : null;
    }

    private static async Task EnsureUniqueNameAsync(SqliteConnection connection, SqliteTransaction transaction, int beneficiaryId,
        string name, int? excludedId, string beneficiaryName, CancellationToken token)
    {
        await using var command = SqliteLocalStore.Command(connection, transaction, """
            SELECT name FROM projects
            WHERE beneficiary_id=@beneficiary AND normalized_name=@normalized AND (@id IS NULL OR id<>@id) LIMIT 1
            """, ("@beneficiary", beneficiaryId), ("@normalized", ProjectRules.NormalizedName(name)), ("@id", excludedId));
        if (await command.ExecuteScalarAsync(token).ConfigureAwait(false) is string existingName)
            throw new ProjectOperationException(ProjectRules.DuplicateMessage(existingName, beneficiaryName));
    }

    private static async Task<string?> GetBeneficiaryNameAsync(SqliteConnection connection, SqliteTransaction? transaction, int beneficiaryId, CancellationToken token)
    {
        await using var command = SqliteLocalStore.Command(connection, transaction,
            "SELECT name FROM beneficiaries WHERE id=@id", ("@id", beneficiaryId));
        return await command.ExecuteScalarAsync(token).ConfigureAwait(false) as string;
    }

    private static async Task<string?> GetProjectNameAsync(SqliteConnection connection, SqliteTransaction? transaction, int projectId, CancellationToken token)
    {
        await using var command = SqliteLocalStore.Command(connection, transaction,
            "SELECT name FROM projects WHERE id=@id", ("@id", projectId));
        return await command.ExecuteScalarAsync(token).ConfigureAwait(false) as string;
    }

    private static Project ReadProject(SqliteDataReader reader) => new(reader.GetInt32(0), reader.GetInt32(1),
        reader.GetString(2), reader.GetString(3), reader.GetInt64(4), ReadUtc(reader, 5), ReadUtc(reader, 6));

    private static ProjectObservation ReadObservation(SqliteDataReader reader) => new(reader.GetInt32(0), reader.GetInt32(1),
        reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetInt64(5), ReadUtc(reader, 6), ReadUtc(reader, 7));

    internal static DateTime ReadUtc(SqliteDataReader reader, int ordinal)
    {
        var value = DateTime.Parse(reader.GetString(ordinal), null, System.Globalization.DateTimeStyles.RoundtripKind);
        return value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
    }

    private static bool IsProjectNameConflict(SqliteException exception) =>
        exception.SqliteErrorCode == 19 && exception.Message.Contains("projects", StringComparison.OrdinalIgnoreCase);

    private Task EnsureOperatorAsync(CancellationToken token) =>
        accessControl?.EnsureBeneficiaryOperatorAsync(token) ?? Task.CompletedTask;
}
