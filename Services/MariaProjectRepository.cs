using System.Data;
using MySqlConnector;

namespace BlazorStoc.Services;

public sealed class MariaProjectRepository(IConfiguration configuration, IWebHostEnvironment environment,
    IAccessControl? accessControl = null, IAuditTrail? auditTrail = null, IArchiveService? archiveService = null,
    IProjectFileStore? fileStore = null) : IProjectRepository
{
    private static readonly SemaphoreSlim SchemaGate = new(1, 1);
    private static bool schemaReady;
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
        await EnsureSchemaAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("""
            SELECT id_project,id_beneficiar,name,observations,version,created_utc,updated_utc
            FROM project WHERE id_beneficiar=@beneficiary ORDER BY name,id_project
            """, connection);
        command.Parameters.AddWithValue("@beneficiary", beneficiaryId);
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
        await EnsureSchemaAsync(connection, cancellationToken).ConfigureAwait(false);
        return await GetAsync(connection, null, id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Project> CreateAsync(ProjectInput input, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken);
        var value = input.Validated();
        var nowUtc = DateTime.UtcNow;
        var (project, beneficiaryName) = await WriteAsync(async (connection, transaction) =>
        {
            var beneficiaryName = await GetBeneficiaryNameAsync(connection, transaction, value.BeneficiaryId, cancellationToken).ConfigureAwait(false);
            ProjectRules.CheckBeneficiaryExists(beneficiaryName is null ? null : new Beneficiary(value.BeneficiaryId, beneficiaryName, ""));
            await EnsureUniqueNameAsync(connection, transaction, value.BeneficiaryId, value.Name, null, cancellationToken).ConfigureAwait(false);
            await using var command = Command(connection, transaction, """
                INSERT INTO project(id_beneficiar,name,normalized_name,observations,version,created_utc,updated_utc)
                VALUES(@beneficiary,@name,@normalized,@observations,0,@created,@updated)
                """, ("@beneficiary", value.BeneficiaryId), ("@name", value.Name),
                ("@normalized", ProjectRules.NormalizedName(value.Name)), ("@observations", value.Observations),
                ("@created", nowUtc), ("@updated", nowUtc));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return (ProjectRules.Create(checked((int)command.LastInsertedId), value, nowUtc), beneficiaryName!);
        }, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordCreateAsync(auditTrail, accessControl, AuditEntities.Project, project.Id.ToString(), project.Name,
            AuditDetails.Identification(("Denumire", project.Name), ("Beneficiar", beneficiaryName), ("Observații", project.Observations)),
            cancellationToken).ConfigureAwait(false);
        return project;
    }

    public async Task<Project> UpdateAsync(Project original, ProjectInput input, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken);
        var value = input.Validated(true);
        var nowUtc = DateTime.UtcNow;
        var updated = await WriteAsync(async (connection, transaction) =>
        {
            var current = await GetLockedAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false);
            ProjectRules.CheckCurrent(current, original);
            var beneficiaryName = await GetBeneficiaryNameAsync(connection, transaction, value.BeneficiaryId, cancellationToken).ConfigureAwait(false);
            ProjectRules.CheckBeneficiaryExists(beneficiaryName is null ? null : new Beneficiary(value.BeneficiaryId, beneficiaryName, ""));
            await EnsureUniqueNameAsync(connection, transaction, value.BeneficiaryId, value.Name, original.Id, cancellationToken).ConfigureAwait(false);
            var updated = ProjectRules.Edited(original, value, nowUtc);
            await using var command = Command(connection, transaction, """
                UPDATE project SET id_beneficiar=@beneficiary,name=@name,normalized_name=@normalized,
                    observations=@observations,version=@version,updated_utc=@updated
                WHERE id_project=@id AND version=@oldVersion
                """, ("@beneficiary", value.BeneficiaryId), ("@name", value.Name),
                ("@normalized", ProjectRules.NormalizedName(value.Name)), ("@observations", value.Observations),
                ("@version", updated.Version), ("@updated", nowUtc), ("@id", original.Id), ("@oldVersion", original.Version));
            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw new ProjectOperationException("Proiectul s-a schimbat între timp. Actualizează pagina.");
            return updated;
        }, cancellationToken).ConfigureAwait(false);
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
                    if (await MariaStockMovementRepository.ProjectHasMovementsAsync(connection, transaction, original.Id, token).ConfigureAwait(false))
                        throw new ProjectOperationException("Proiectul are mișcări de stoc asociate și nu poate fi șters. Istoricul trebuie păstrat.");
                    await MariaArchivePersistence.InsertAsync(connection, transaction, operation, prepared, token).ConfigureAwait(false);
                    await using (var deleteFiles = Command(connection, transaction,
                        "DELETE FROM project_observation_file WHERE id_observation IN (SELECT id_observation FROM project_observation WHERE id_project=@id)",
                        ("@id", original.Id)))
                        await deleteFiles.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    await using (var deleteObservations = Command(connection, transaction,
                        "DELETE FROM project_observation WHERE id_project=@id", ("@id", original.Id)))
                        await deleteObservations.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    await using var deleteProject = Command(connection, transaction,
                        "DELETE FROM project WHERE id_project=@id AND version=@version", ("@id", original.Id), ("@version", original.Version));
                    if (await deleteProject.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
                        throw new ProjectOperationException("Proiectul s-a schimbat între timp. Actualizează pagina.");
                    await MariaArchivePersistence.InsertAuditAsync(connection, transaction, operation, token).ConfigureAwait(false);
                    return true;
                }, token).ConfigureAwait(false);
                foreach (var file in prepared)
                    await ArchiveFileSafety.CompleteAsync(rootPath,
                        archivePath, file, token).ConfigureAwait(false);
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
        await EnsureSchemaAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("""
            SELECT id_observation,id_project,name,content,author,version,created_utc,updated_utc
            FROM project_observation WHERE id_project=@project ORDER BY created_utc DESC,id_observation DESC
            """, connection);
        command.Parameters.AddWithValue("@project", projectId);
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
        await EnsureSchemaAsync(connection, cancellationToken).ConfigureAwait(false);
        return await GetObservationAsync(connection, null, id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProjectObservation> CreateObservationAsync(int projectId, ProjectObservationInput input, string author,
        CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken);
        var value = input.Validated();
        var nowUtc = DateTime.UtcNow;
        var (observation, projectName) = await WriteAsync(async (connection, transaction) =>
        {
            var projectName = await GetProjectNameAsync(connection, transaction, projectId, cancellationToken).ConfigureAwait(false)
                ?? throw new ProjectOperationException("Proiectul nu mai există.");
            await using var command = Command(connection, transaction, """
                INSERT INTO project_observation(id_project,name,content,author,version,created_utc,updated_utc)
                VALUES(@project,@name,@content,@author,0,@created,@updated)
                """, ("@project", projectId), ("@name", value.Name), ("@content", value.Content), ("@author", author),
                ("@created", nowUtc), ("@updated", nowUtc));
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
        var nowUtc = DateTime.UtcNow;
        var updated = await WriteAsync(async (connection, transaction) =>
        {
            var current = await GetObservationLockedAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false);
            ProjectRules.CheckCurrent(current, original);
            var updated = ProjectRules.EditedObservation(original, value, nowUtc);
            await using var command = Command(connection, transaction, """
                UPDATE project_observation SET name=@name,content=@content,version=@version,updated_utc=@updated
                WHERE id_observation=@id AND version=@oldVersion
                """, ("@name", value.Name), ("@content", value.Content), ("@version", updated.Version),
                ("@updated", nowUtc), ("@id", original.Id), ("@oldVersion", original.Version));
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
                        "DELETE FROM project_observation_file WHERE id_observation=@id", ("@id", original.Id)))
                        await deleteFiles.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    await using var delete = Command(connection, transaction,
                        "DELETE FROM project_observation WHERE id_observation=@id AND version=@version", ("@id", original.Id), ("@version", original.Version));
                    if (await delete.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
                        throw new ProjectOperationException("Observația s-a schimbat între timp. Actualizează pagina.");
                    await MariaArchivePersistence.InsertAuditAsync(connection, transaction, operation, token).ConfigureAwait(false);
                    return true;
                }, token).ConfigureAwait(false);
                foreach (var file in prepared)
                    await ArchiveFileSafety.CompleteAsync(rootPath,
                        archivePath, file, token).ConfigureAwait(false);
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
            prepared.Add(await ArchiveFileSafety.PrepareAsync(rootPath,
                archivePath, file.StoredName, operation,
                AuditEntities.ProjectObservationFile, file.Id.ToString(), file.ContentType, file.OriginalName,
                file.SizeBytes, token).ConfigureAwait(false));
        return prepared;
    }

    private async Task<T> WriteAsync<T>(Func<MySqlConnection, MySqlTransaction, Task<T>> action, CancellationToken token)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(token).ConfigureAwait(false);
        await EnsureSchemaAsync(connection, token).ConfigureAwait(false);
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

    private async Task EnsureSchemaAsync(MySqlConnection connection, CancellationToken token)
    {
        if (schemaReady) return;
        await SchemaGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (schemaReady) return;
            foreach (var statement in SchemaStatements)
            {
                await using var command = new MySqlCommand(statement, connection);
                await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }
            schemaReady = true;
        }
        finally { SchemaGate.Release(); }
    }

    internal static readonly string[] SchemaStatements =
    [
        """
        CREATE TABLE IF NOT EXISTS project (
            id_project INT NOT NULL AUTO_INCREMENT,
            id_beneficiar INT NOT NULL,
            name VARCHAR(200) NOT NULL,
            normalized_name VARCHAR(200) NOT NULL,
            observations TEXT NOT NULL,
            version BIGINT UNSIGNED NOT NULL DEFAULT 0,
            created_utc DATETIME(6) NOT NULL,
            updated_utc DATETIME(6) NOT NULL,
            PRIMARY KEY(id_project),
            UNIQUE KEY ux_project_beneficiary_name(id_beneficiar,normalized_name),
            CONSTRAINT fk_project_beneficiar FOREIGN KEY(id_beneficiar) REFERENCES beneficiar(id_beneficiar) ON DELETE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS project_observation (
            id_observation INT NOT NULL AUTO_INCREMENT,
            id_project INT NOT NULL,
            name VARCHAR(200) NOT NULL,
            content TEXT NOT NULL,
            author VARCHAR(191) NOT NULL,
            version BIGINT UNSIGNED NOT NULL DEFAULT 0,
            created_utc DATETIME(6) NOT NULL,
            updated_utc DATETIME(6) NOT NULL,
            PRIMARY KEY(id_observation),
            INDEX ix_project_observation_project(id_project,created_utc),
            CONSTRAINT fk_observation_project FOREIGN KEY(id_project) REFERENCES project(id_project) ON DELETE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS project_observation_file (
            id_file INT NOT NULL AUTO_INCREMENT,
            id_observation INT NOT NULL,
            relative_path VARCHAR(255) NOT NULL,
            original_name VARCHAR(255) NOT NULL,
            content_type VARCHAR(255) NOT NULL,
            byte_length BIGINT UNSIGNED NOT NULL,
            sha256 CHAR(64) NOT NULL,
            author VARCHAR(191) NOT NULL,
            uploaded_utc DATETIME(6) NOT NULL,
            PRIMARY KEY(id_file),
            INDEX ix_project_observation_file_observation(id_observation),
            CONSTRAINT fk_file_observation FOREIGN KEY(id_observation) REFERENCES project_observation(id_observation) ON DELETE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """
    ];

    private static async Task<Project?> GetAsync(MySqlConnection connection, MySqlTransaction? transaction, int id, CancellationToken token)
    {
        await using var command = Command(connection, transaction,
            "SELECT id_project,id_beneficiar,name,observations,version,created_utc,updated_utc FROM project WHERE id_project=@id",
            ("@id", id));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        return await reader.ReadAsync(token).ConfigureAwait(false) ? Read(reader) : null;
    }

    private static async Task<Project?> GetLockedAsync(MySqlConnection connection, MySqlTransaction transaction, int id, CancellationToken token)
    {
        await using var command = Command(connection, transaction,
            "SELECT id_project,id_beneficiar,name,observations,version,created_utc,updated_utc FROM project WHERE id_project=@id FOR UPDATE",
            ("@id", id));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        return await reader.ReadAsync(token).ConfigureAwait(false) ? Read(reader) : null;
    }

    private static async Task<ProjectObservation?> GetObservationAsync(MySqlConnection connection, MySqlTransaction? transaction, int id, CancellationToken token)
    {
        await using var command = Command(connection, transaction,
            "SELECT id_observation,id_project,name,content,author,version,created_utc,updated_utc FROM project_observation WHERE id_observation=@id",
            ("@id", id));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        return await reader.ReadAsync(token).ConfigureAwait(false) ? ReadObservation(reader) : null;
    }

    private static async Task<ProjectObservation?> GetObservationLockedAsync(MySqlConnection connection, MySqlTransaction transaction, int id, CancellationToken token)
    {
        await using var command = Command(connection, transaction,
            "SELECT id_observation,id_project,name,content,author,version,created_utc,updated_utc FROM project_observation WHERE id_observation=@id FOR UPDATE",
            ("@id", id));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        return await reader.ReadAsync(token).ConfigureAwait(false) ? ReadObservation(reader) : null;
    }

    private static async Task<string?> GetBeneficiaryNameAsync(MySqlConnection connection, MySqlTransaction? transaction, int beneficiaryId, CancellationToken token)
    {
        await using var command = Command(connection, transaction,
            "SELECT beneficiar_denumire FROM beneficiar WHERE id_beneficiar=@id", ("@id", beneficiaryId));
        return await command.ExecuteScalarAsync(token).ConfigureAwait(false) as string;
    }

    private static async Task<string?> GetProjectNameAsync(MySqlConnection connection, MySqlTransaction? transaction, int projectId, CancellationToken token)
    {
        await using var command = Command(connection, transaction, "SELECT name FROM project WHERE id_project=@id", ("@id", projectId));
        return await command.ExecuteScalarAsync(token).ConfigureAwait(false) as string;
    }

    private static async Task EnsureUniqueNameAsync(MySqlConnection connection, MySqlTransaction transaction, int beneficiaryId,
        string name, int? excludedId, CancellationToken token)
    {
        await using var command = Command(connection, transaction, """
            SELECT name FROM project WHERE id_beneficiar=@beneficiary AND normalized_name=@normalized
                AND (@id IS NULL OR id_project<>@id) LIMIT 1 FOR UPDATE
            """, ("@beneficiary", beneficiaryId), ("@normalized", ProjectRules.NormalizedName(name)),
            ("@id", excludedId is null ? DBNull.Value : excludedId.Value));
        if (await command.ExecuteScalarAsync(token).ConfigureAwait(false) is string existingName)
        {
            var beneficiaryName = await GetBeneficiaryNameAsync(connection, transaction, beneficiaryId, token).ConfigureAwait(false) ?? "";
            throw new ProjectOperationException(ProjectRules.DuplicateMessage(existingName, beneficiaryName));
        }
    }

    private static Project Read(MySqlDataReader reader) => new(reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2),
        reader.GetString(3), reader.GetInt64(4), DateTime.SpecifyKind(reader.GetDateTime(5), DateTimeKind.Utc),
        DateTime.SpecifyKind(reader.GetDateTime(6), DateTimeKind.Utc));

    private static ProjectObservation ReadObservation(MySqlDataReader reader) => new(reader.GetInt32(0), reader.GetInt32(1),
        reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetInt64(5),
        DateTime.SpecifyKind(reader.GetDateTime(6), DateTimeKind.Utc), DateTime.SpecifyKind(reader.GetDateTime(7), DateTimeKind.Utc));

    private static MySqlCommand Command(MySqlConnection connection, MySqlTransaction? transaction, string sql, params (string Name, object Value)[] parameters)
    {
        var command = new MySqlCommand(sql, connection, transaction);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return command;
    }

    private Task EnsureOperatorAsync(CancellationToken token) => accessControl?.EnsureBeneficiaryOperatorAsync(token) ?? Task.CompletedTask;
}
