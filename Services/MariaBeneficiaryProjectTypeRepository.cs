using System.Globalization;
using MySqlConnector;
using static BlazorStoc.Services.MariaDb;

namespace BlazorStoc.Services;

// MariaDB mode: `beneficiary_project_types` (migration 91) and `projects.mandatory_type_id`. Reading needs a product operator, changing needs an administrator; every change is checked
// against the version read and has its own journal action. Adding or activating a type makes the project it asks for at every beneficiary that does not have it.
public sealed class MariaBeneficiaryProjectTypeRepository(
    IConfiguration configuration,
    IAccessControl? accessControl = null,
    IAuditTrail? auditTrail = null) : IBeneficiaryProjectTypeRepository
{
    public async Task<IReadOnlyList<BeneficiaryProjectType>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        return await ReadAllAsync(connection, null, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<List<BeneficiaryProjectType>> ReadAllAsync(MySqlConnection connection, MySqlTransaction? transaction, CancellationToken token)
    {
        var result = new List<BeneficiaryProjectType>();
        await using var command = Command(connection, transaction, "SELECT id,name,active,sort_order,version FROM beneficiary_project_types ORDER BY sort_order,id");
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
            result.Add(new BeneficiaryProjectType(checked((int)reader.GetInt64(0)), reader.GetString(1), Convert.ToBoolean(reader.GetValue(2)), Convert.ToInt32(reader.GetValue(3)), reader.GetInt64(4)));
        return result;
    }

    // The projects the ACTIVE types ask for, made where they are missing (one beneficiary, or all when beneficiaryId is null). A project that already has the name of a type (made by hand)
    // is taken over (marked) instead of made twice. Returns how many projects were made. Idempotent: running it again changes nothing.
    internal static async Task<int> EnsureAsync(MySqlConnection connection, MySqlTransaction? transaction, int? beneficiaryId, CancellationToken token)
    {
        var made = 0;
        var now = MariaTimeText.Format(DateTime.UtcNow);
        foreach (var type in (await ReadAllAsync(connection, transaction, token).ConfigureAwait(false)).Where(type => type.Active))
        {
            var normalized = ProjectRules.NormalizedName(type.Name);
            await using (var link = Command(connection, transaction,
                "UPDATE projects SET mandatory_type_id=@type WHERE mandatory_type_id IS NULL AND normalized_name=@normalized AND (@beneficiary IS NULL OR beneficiary_id=@beneficiary)",
                ("@type", type.Id), ("@normalized", normalized), ("@beneficiary", beneficiaryId)))
                await link.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            await using var insert = Command(connection, transaction, """
                INSERT INTO projects(beneficiary_id,name,normalized_name,observations,version,created_utc,updated_utc,mandatory_type_id)
                SELECT b.id,@name,@normalized,'',0,@now,@now,@type FROM beneficiaries b
                WHERE (@beneficiary IS NULL OR b.id=@beneficiary)
                  AND NOT EXISTS (SELECT 1 FROM projects p WHERE p.beneficiary_id=b.id AND (p.mandatory_type_id=@type OR p.normalized_name=@normalized))
                """, ("@name", type.Name), ("@normalized", normalized), ("@now", now), ("@type", type.Id), ("@beneficiary", beneficiaryId));
            made += await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }
        return made;
    }

    // The first type of the list, made by the application (a migration account only changes the structure) when there is none yet.
    public const string DefaultTypeName = "Reparații sisteme existente";

    internal static async Task SeedAsync(MySqlConnection connection, MySqlTransaction? transaction, CancellationToken token)
    {
        if ((await ReadAllAsync(connection, transaction, token).ConfigureAwait(false)).Count > 0) return;
        var now = MariaTimeText.Format(DateTime.UtcNow);
        await using var insert = Command(connection, transaction,
            "INSERT IGNORE INTO beneficiary_project_types(name,name_key,active,sort_order,version,created_utc,updated_utc) VALUES(@name,@key,1,1,0,@now,@now)",
            ("@name", DefaultTypeName), ("@key", BeneficiaryProjectTypeRules.Key(DefaultTypeName)), ("@now", now));
        await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    public async Task<int> EnsureProjectsAsync(CancellationToken cancellationToken = default)
    {
        var made = await WriteAsync(async (connection, transaction) =>
        {
            await SeedAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            return await EnsureAsync(connection, transaction, null, cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
        if (made > 0)
            await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.BeneficiaryProjectType, AuditActions.EnsureMandatoryProjects, "0", "Proiecte obligatorii",
                AuditDetails.Identification(("Proiecte create", made.ToString(CultureInfo.InvariantCulture))), string.Empty, cancellationToken).ConfigureAwait(false);
        return made;
    }

    public async Task<BeneficiaryProjectType> CreateAsync(string name, CancellationToken cancellationToken = default)
    {
        await EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var text = BeneficiaryProjectTypeRules.Clean(name);
        var (id, made) = await WriteAsync(async (connection, transaction) =>
        {
            var all = await ReadAllAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            if (BeneficiaryProjectTypeRules.NameProblem(text, all) is { } problem) throw new BeneficiaryProjectTypeOperationException(problem);
            var now = MariaTimeText.Format(DateTime.UtcNow);
            await using var insert = Command(connection, transaction, """
                INSERT INTO beneficiary_project_types(name,name_key,active,sort_order,version,created_utc,updated_utc) VALUES(@name,@key,1,@order,0,@now,@now)
                """, ("@name", text), ("@key", BeneficiaryProjectTypeRules.Key(text)), ("@order", all.Count == 0 ? 1 : all.Max(type => type.SortOrder) + 1), ("@now", now));
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            var created = checked((int)insert.LastInsertedId);
            return (created, await EnsureAsync(connection, transaction, null, cancellationToken).ConfigureAwait(false));
        }, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.BeneficiaryProjectType, AuditActions.AddMandatoryProjectType, id.ToString(), text,
            AuditDetails.Identification(("Denumire", text), ("Proiecte create la beneficiari", made.ToString(CultureInfo.InvariantCulture))), string.Empty, cancellationToken).ConfigureAwait(false);
        return await GetAsync(id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<BeneficiaryProjectType> RenameAsync(BeneficiaryProjectType original, string name, CancellationToken cancellationToken = default)
    {
        await EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var text = BeneficiaryProjectTypeRules.Clean(name);
        var current = await WriteAsync(async (connection, transaction) =>
        {
            var all = await ReadAllAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            var type = CheckCurrent(all, original);
            if (text == type.Name) throw new BeneficiaryProjectTypeOperationException(BeneficiaryProjectTypeRules.NoChangeMessage);
            if (BeneficiaryProjectTypeRules.NameProblem(text, all, type.Id) is { } problem) throw new BeneficiaryProjectTypeOperationException(problem);
            await UpdateAsync(connection, transaction, type, "name=@name,name_key=@key", cancellationToken, ("@name", text), ("@key", BeneficiaryProjectTypeRules.Key(text))).ConfigureAwait(false);
            return type;
        }, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordEditAsync(auditTrail, accessControl, AuditEntities.BeneficiaryProjectType, current.Id.ToString(), text,
            [new AuditChange("Denumire", current.Name, text)], string.Empty, cancellationToken, AuditActions.RenameMandatoryProjectType).ConfigureAwait(false);
        return await GetAsync(current.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<BeneficiaryProjectType> SetActiveAsync(BeneficiaryProjectType original, bool active, CancellationToken cancellationToken = default)
    {
        await EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var (current, made) = await WriteAsync(async (connection, transaction) =>
        {
            var type = CheckCurrent(await ReadAllAsync(connection, transaction, cancellationToken).ConfigureAwait(false), original);
            if (type.Active == active) return (type, 0);
            await UpdateAsync(connection, transaction, type, "active=@active", cancellationToken, ("@active", active ? 1 : 0)).ConfigureAwait(false);
            return (type, active ? await EnsureAsync(connection, transaction, null, cancellationToken).ConfigureAwait(false) : 0);
        }, cancellationToken).ConfigureAwait(false);
        if (current.Active != active)
            await AuditRecorder.RecordEditAsync(auditTrail, accessControl, AuditEntities.BeneficiaryProjectType, current.Id.ToString(), current.Name,
                Changes(current, active, made),
                string.Empty, cancellationToken, active ? AuditActions.ActivateMandatoryProjectType : AuditActions.DeactivateMandatoryProjectType).ConfigureAwait(false);
        return await GetAsync(current.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<BeneficiaryProjectType> MoveAsync(BeneficiaryProjectType original, int direction, CancellationToken cancellationToken = default)
    {
        await EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var moved = await WriteAsync(async (connection, transaction) =>
        {
            var all = await ReadAllAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            var type = CheckCurrent(all, original);
            var index = all.FindIndex(item => item.Id == type.Id);
            var neighbourIndex = index + Math.Sign(direction);
            if (neighbourIndex < 0 || neighbourIndex >= all.Count) return (Type: type, Changed: false, From: 0, To: 0);
            // The two types swap places: orders are renumbered 1..n so equal or missing numbers never matter.
            var order = all.Select(item => item.Id).ToList();
            (order[index], order[neighbourIndex]) = (order[neighbourIndex], order[index]);
            for (var position = 0; position < order.Count; position++)
                await using (var update = Command(connection, transaction, "UPDATE beneficiary_project_types SET sort_order=@order,version=version+1,updated_utc=@now WHERE id=@id",
                    ("@order", position + 1), ("@id", order[position]), ("@now", MariaTimeText.Format(DateTime.UtcNow))))
                    await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return (Type: type, Changed: true, From: index + 1, To: neighbourIndex + 1);
        }, cancellationToken).ConfigureAwait(false);
        if (moved.Changed)
            await AuditRecorder.RecordEditAsync(auditTrail, accessControl, AuditEntities.BeneficiaryProjectType, moved.Type.Id.ToString(), moved.Type.Name,
                [new AuditChange("Poziție", moved.From.ToString(CultureInfo.InvariantCulture), moved.To.ToString(CultureInfo.InvariantCulture))], string.Empty, cancellationToken,
                AuditActions.MoveMandatoryProjectType).ConfigureAwait(false);
        return await GetAsync(moved.Type.Id, cancellationToken).ConfigureAwait(false);
    }

    private static List<AuditChange> Changes(BeneficiaryProjectType current, bool active, int made)
    {
        var changes = new List<AuditChange> { new("Stare", current.Active ? "Activ" : "Inactiv", active ? "Activ" : "Inactiv") };
        if (made > 0) changes.Add(new AuditChange("Proiecte create la beneficiari", "0", made.ToString(CultureInfo.InvariantCulture)));
        return changes;
    }

    private async Task<BeneficiaryProjectType> GetAsync(int id, CancellationToken token)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, token).ConfigureAwait(false);
        return (await ReadAllAsync(connection, null, token).ConfigureAwait(false)).FirstOrDefault(type => type.Id == id)
               ?? throw new BeneficiaryProjectTypeOperationException(BeneficiaryProjectTypeRules.StaleMessage);
    }

    // The version read by the page must still be the stored one.
    private static BeneficiaryProjectType CheckCurrent(IEnumerable<BeneficiaryProjectType> all, BeneficiaryProjectType original)
    {
        var current = all.FirstOrDefault(type => type.Id == original.Id);
        if (current is null || current.Version != original.Version) throw new BeneficiaryProjectTypeOperationException(BeneficiaryProjectTypeRules.StaleMessage);
        return current;
    }

    private static async Task UpdateAsync(MySqlConnection connection, MySqlTransaction transaction, BeneficiaryProjectType type, string assignments, CancellationToken token,
        params (string Name, object? Value)[] parameters)
    {
        var all = parameters.Concat([("@id", (object)type.Id), ("@version", (object)type.Version), ("@now", (object)MariaTimeText.Format(DateTime.UtcNow))]).ToArray();
        await using var update = Command(connection, transaction,
            $"UPDATE beneficiary_project_types SET {(assignments.Length > 0 ? assignments + "," : "")}version=version+1,updated_utc=@now WHERE id=@id AND version=@version", all);
        if (await update.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1) throw new BeneficiaryProjectTypeOperationException(BeneficiaryProjectTypeRules.StaleMessage);
    }

    private Task<T> WriteAsync<T>(Func<MySqlConnection, MySqlTransaction, Task<T>> action, CancellationToken token) =>
        MariaDb.WriteAsync(configuration, action, message => new BeneficiaryProjectTypeOperationException(message),
            exception => exception.Number == 1062 ? new BeneficiaryProjectTypeOperationException(BeneficiaryProjectTypeRules.DuplicateMessage) : null, token,
            "Modificările sunt permise numai în baza BlazorStoc. Verifică numele bazei configurate.");
    private Task EnsureOperatorAsync(CancellationToken token) => accessControl?.EnsureProductOperatorAsync(token) ?? Task.CompletedTask;
    private Task EnsureAdministratorAsync(CancellationToken token) => accessControl?.EnsureAdministratorAsync(token) ?? Task.CompletedTask;
}
