using MySqlConnector;
using static BlazorStoc.Services.MariaDb;

namespace BlazorStoc.Services;

// MariaDB mode: `work_point_attached` is created by migration 88 (the repository never alters the schema). The name is unique inside the work point
// (name_key); the address is never taken from the parent; the database itself refuses a row with neither address nor coordinates.
public sealed class MariaAttachedWorkPointRepository(
    IConfiguration configuration,
    IAccessControl? accessControl = null,
    IAuditTrail? auditTrail = null,
    IArchiveService? archiveService = null) : IAttachedWorkPointRepository
{
    internal const string Columns = "a.id, a.work_point_id, a.name, a.address, a.phone, a.contact_person, a.version, a.latitude, a.longitude";
    private readonly IArchiveService archiver = archiveService ?? new ArchiveService(accessControl);

    public async Task<IReadOnlyList<AttachedWorkPoint>> GetAsync(int workPointId, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAnyPermissionAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        return await ReadAsync(connection, null, "a.work_point_id=@id", cancellationToken, ("@id", workPointId)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AttachedWorkPoint>> GetForBeneficiaryAsync(int beneficiaryId, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAnyPermissionAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        return await ReadForBeneficiaryAsync(connection, null, beneficiaryId, cancellationToken).ConfigureAwait(false);
    }

    internal static Task<IReadOnlyList<AttachedWorkPoint>> ReadForBeneficiaryAsync(MySqlConnection connection, MySqlTransaction? transaction, int beneficiaryId, CancellationToken token) =>
        ReadAsync(connection, transaction, "a.work_point_id IN (SELECT id FROM beneficiary_work_points WHERE beneficiary_id=@id)", token, ("@id", beneficiaryId));

    internal static async Task<IReadOnlyList<AttachedWorkPoint>> ReadAsync(MySqlConnection connection, MySqlTransaction? transaction, string where, CancellationToken token, params (string, object?)[] parameters)
    {
        var result = new List<AttachedWorkPoint>();
        await using var command = Command(connection, transaction, $"SELECT {Columns} FROM work_point_attached a WHERE {where} ORDER BY a.work_point_id, a.name, a.id", parameters);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false)) result.Add(Read(reader));
        return result;
    }

    public async Task<IReadOnlyList<PlaceSearchEntry>> GetSearchEntriesAsync(CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAnyPermissionAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        var result = new List<PlaceSearchEntry>();
        // The main work point has the address and the phone of the beneficiary (searched already): it is looked in only by a name or a contact somebody gave it.
        await using var command = Command(connection, null, $"""
            SELECT w.beneficiary_id, 0, w.name, '', IF(w.is_primary=1, '', w.address), IF(w.is_primary=1, '', w.phone), w.contact_person
            FROM beneficiary_work_points w
            WHERE NOT (w.is_primary=1 AND w.name=@primary AND w.contact_person='')
            UNION ALL
            SELECT w.beneficiary_id, 1, a.name, w.name, a.address, a.phone, a.contact_person
            FROM work_point_attached a JOIN beneficiary_work_points w ON w.id=a.work_point_id
            """, ("@primary", WorkPointRules.PrimaryName));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new PlaceSearchEntry(checked((int)reader.GetInt64(0)), Convert.ToInt32(reader.GetValue(1)) == 1, reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6)));
        return result;
    }

    public async Task<IReadOnlyList<AttachedWorkPointPlace>> GetAllPlacesAsync(CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAnyPermissionAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        var result = new List<AttachedWorkPointPlace>();
        await using var command = Command(connection, null, $"""
            SELECT {Columns}, w.beneficiary_id, b.name, w.name
            FROM work_point_attached a JOIN beneficiary_work_points w ON w.id=a.work_point_id JOIN beneficiaries b ON b.id=w.beneficiary_id
            ORDER BY b.name, w.name, a.name, a.id
            """);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new AttachedWorkPointPlace(Read(reader), checked((int)reader.GetInt64(9)), reader.GetString(10), reader.GetString(11)));
        return result;
    }

    public async Task<AttachedWorkPoint> CreateAsync(WorkPoint parent, AttachedWorkPointInput input, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAsync("beneficiari.edit", cancellationToken).ConfigureAwait(false);
        if (parent.Id == 0) throw new WorkPointOperationException("Punctul de lucru nu a fost încă creat. Actualizează pagina.");
        var value = input.Validated();
        var (point, ownerName, parentName) = await WriteAsync(async (connection, transaction) =>
        {
            var (ownerId, ownerName, parentName) = await LockParentAsync(connection, transaction, parent.Id, cancellationToken).ConfigureAwait(false);
            await EnsureUniqueAsync(connection, transaction, parent.Id, value.Name, null, cancellationToken).ConfigureAwait(false);
            await using var command = Command(connection, transaction, """
                INSERT INTO work_point_attached (work_point_id, name, name_key, address, phone, contact_person, latitude, longitude, version)
                VALUES (@workPoint, @name, @key, @address, @phone, @contact, @latitude, @longitude, 0)
                """, Fields(parent.Id, value));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return (Build(checked((int)command.LastInsertedId), parent.Id, value, 0), ownerName, parentName);
        }, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.Beneficiary, AuditActions.CreateAttachedWorkPoint, parent.BeneficiaryId.ToString(),
            AttachedWorkPointRules.Target(parent.BeneficiaryId, ownerName, parentName, point.Name),
            AuditDetails.Changes(new AuditChange("Punct arondat adăugat", string.Empty, AttachedWorkPointRules.Identification(point))), string.Empty,
            cancellationToken).ConfigureAwait(false);
        return point;
    }

    public async Task<AttachedWorkPoint> UpdateAsync(AttachedWorkPoint original, AttachedWorkPointInput input, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAsync("beneficiari.edit", cancellationToken).ConfigureAwait(false);
        var value = input.Validated();
        var (point, beneficiaryId, ownerName, parentName) = await WriteAsync(async (connection, transaction) =>
        {
            AttachedWorkPointRules.CheckCurrent(await GetLockedAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false), original);
            var (ownerId, ownerName, parentName) = await LockParentAsync(connection, transaction, original.WorkPointId, cancellationToken).ConfigureAwait(false);
            await EnsureUniqueAsync(connection, transaction, original.WorkPointId, value.Name, original.Id, cancellationToken).ConfigureAwait(false);
            var version = checked(original.Version + 1);
            await using var command = Command(connection, transaction, """
                UPDATE work_point_attached SET name=@name, name_key=@key, address=@address, phone=@phone, contact_person=@contact,
                    latitude=@latitude, longitude=@longitude, version=@version WHERE id=@id AND version=@oldVersion
                """, [.. Fields(original.WorkPointId, value), ("@version", version), ("@id", original.Id), ("@oldVersion", original.Version)]);
            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1) throw AttachedWorkPointRules.Changed();
            return (Build(original.Id, original.WorkPointId, value, version), ownerId, ownerName, parentName);
        }, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordEditAsync(auditTrail, accessControl, AuditEntities.Beneficiary, beneficiaryId.ToString(),
            AttachedWorkPointRules.Target(beneficiaryId, ownerName, parentName, point.Name),
            AttachedWorkPointRules.Changes(original, point), AttachedWorkPointRules.GeneratedEditReason, cancellationToken,
            AuditActions.EditAttachedWorkPoint).ConfigureAwait(false);
        return point;
    }

    public async Task DeleteAsync(AttachedWorkPoint original, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAsync("beneficiari.edit", cancellationToken).ConfigureAwait(false);
        int beneficiaryId;
        string ownerName, parentName;
        await using (var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false))
            (beneficiaryId, ownerName, parentName) = await ReadParentAsync(connection, null, original.WorkPointId, false, cancellationToken).ConfigureAwait(false);
        await archiver.ExecuteAsync(ArchiveRequests.AttachedWorkPoint(original, beneficiaryId, ownerName, parentName, AttachedWorkPointRules.DeleteReason), async (operation, token) =>
        {
            await WriteAsync(async (connection, transaction) =>
            {
                AttachedWorkPointRules.CheckCurrent(await GetLockedAsync(connection, transaction, original.Id, token).ConfigureAwait(false), original);
                await MariaArchivePersistence.InsertAsync(connection, transaction, operation, [], token).ConfigureAwait(false);
                await using var command = Command(connection, transaction, "DELETE FROM work_point_attached WHERE id=@id AND version=@version",
                    ("@id", original.Id), ("@version", original.Version));
                if (await command.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1) throw AttachedWorkPointRules.Changed();
                await MariaArchivePersistence.InsertAuditAsync(connection, transaction, operation, token).ConfigureAwait(false);
                return true;
            }, token).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    private Task<T> WriteAsync<T>(Func<MySqlConnection, MySqlTransaction, Task<T>> action, CancellationToken token) =>
        MariaDb.WriteAsync(configuration, action, message => new WorkPointOperationException(message), Translate, token);

    private static Exception? Translate(MySqlException exception) => exception.Number switch
    {
        // A concurrent save passed the check above and hit uq_work_point_attached_name.
        1062 => new WorkPointOperationException("Există deja un punct arondat cu acest nume la acest punct de lucru."),
        // ck_work_point_attached_where / _coordinates: the database refuses a row without address and coordinates.
        4025 or 3819 => new WorkPointOperationException(AttachedWorkPointRules.NeedsLocationMessage),
        _ => null
    };

    private static async Task<AttachedWorkPoint?> GetLockedAsync(MySqlConnection connection, MySqlTransaction transaction, int id, CancellationToken token)
    {
        await using var command = Command(connection, transaction, $"SELECT {Columns} FROM work_point_attached a WHERE a.id=@id FOR UPDATE", ("@id", id));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        return await reader.ReadAsync(token).ConfigureAwait(false) ? Read(reader) : null;
    }

    private static Task<(int BeneficiaryId, string BeneficiaryName, string WorkPointName)> LockParentAsync(MySqlConnection connection, MySqlTransaction transaction, int workPointId, CancellationToken token) =>
        ReadParentAsync(connection, transaction, workPointId, true, token);

    private static async Task<(int BeneficiaryId, string BeneficiaryName, string WorkPointName)> ReadParentAsync(MySqlConnection connection, MySqlTransaction? transaction, int workPointId, bool forUpdate, CancellationToken token)
    {
        await using var command = Command(connection, transaction, $"""
            SELECT w.beneficiary_id, b.name, w.name FROM beneficiary_work_points w JOIN beneficiaries b ON b.id=w.beneficiary_id WHERE w.id=@id{(forUpdate ? " FOR UPDATE" : "")}
            """, ("@id", workPointId));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false)) throw new WorkPointOperationException("Punctul de lucru nu mai există. Actualizează pagina.");
        return (checked((int)reader.GetInt64(0)), reader.GetString(1), reader.GetString(2));
    }

    private static async Task EnsureUniqueAsync(MySqlConnection connection, MySqlTransaction transaction, int workPointId, string name, int? excludedId, CancellationToken token)
    {
        await using var command = Command(connection, transaction,
            "SELECT name FROM work_point_attached WHERE work_point_id=@workPoint AND name_key=@key AND (@id IS NULL OR id<>@id) LIMIT 1",
            ("@workPoint", workPointId), ("@key", AttachedWorkPointRules.NameKey(name)), ("@id", excludedId is null ? DBNull.Value : excludedId.Value));
        if (await command.ExecuteScalarAsync(token).ConfigureAwait(false) is string existing) throw AttachedWorkPointRules.DuplicateName(existing);
    }

    internal static AttachedWorkPoint Read(MySqlDataReader reader) =>
        new(checked((int)reader.GetInt64(0)), checked((int)reader.GetInt64(1)), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5),
            reader.GetInt64(6), reader.IsDBNull(7) ? null : reader.GetDecimal(7), reader.IsDBNull(8) ? null : reader.GetDecimal(8));

    private static AttachedWorkPoint Build(int id, int workPointId, AttachedWorkPointInput value, long version) =>
        new(id, workPointId, value.Name, value.Address, value.Phone, value.ContactPerson, version, value.Latitude, value.Longitude);

    private static (string, object?)[] Fields(int workPointId, AttachedWorkPointInput value) =>
    [
        ("@workPoint", workPointId), ("@name", value.Name), ("@key", AttachedWorkPointRules.NameKey(value.Name)), ("@address", value.Address),
        ("@phone", value.Phone), ("@contact", value.ContactPerson),
        ("@latitude", value.Latitude is { } latitude ? latitude : DBNull.Value), ("@longitude", value.Longitude is { } longitude ? longitude : DBNull.Value)
    ];
}
