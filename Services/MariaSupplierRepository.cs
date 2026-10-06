using System.Data;
using MySqlConnector;

namespace BlazorStoc.Services;

// MariaDB mode: the register of suppliers (`suppliers`, migration 13). Every authenticated user can read, add and edit; only an
// administrator deletes, and only a supplier nothing refers to (no invoice, no stock entry through an invoice, no invoice template).
public sealed class MariaSupplierRepository(
    IConfiguration configuration,
    IAccessControl? accessControl = null,
    IAuditTrail? auditTrail = null,
    IArchiveService? archiveService = null) : ISupplierRepository
{
    private readonly IArchiveService archiver = archiveService ?? new ArchiveService(accessControl);
    private MySqlConnection CreateConnection() => DatabaseConnections.Create(configuration);

    // The columns of a supplier and, after them, what refers to it (entries through its invoices, invoices, templates by CUI).
    private const string Select = """
        SELECT s.id,s.name,s.cui,s.country,s.version,s.address,s.phone,s.registry_number,s.postal_code,s.caen_code,s.`source`,s.verified_utc,
               (SELECT COUNT(*) FROM stock_movements m INNER JOIN supplier_invoices i ON i.id=m.invoice_id WHERE i.supplier_id=s.id),
               (SELECT COUNT(*) FROM supplier_invoices i WHERE i.supplier_id=s.id),
               (SELECT COUNT(*) FROM invoice_templates t WHERE t.supplier_cui=s.normalized_cui AND s.country='RO')
        FROM suppliers s
        """;

    public async Task<IReadOnlyList<Supplier>> GetSuppliersAsync(CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, null, $"{Select} ORDER BY s.name, s.id");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<Supplier>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(Read(reader));
        return result;
    }

    public async Task<Supplier?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await GetAsync(connection, null, id, false, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Supplier> CreateAsync(SupplierInput input, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        var value = input.Validated();
        var actor = await RepositoryAudit.ActorAsync(accessControl, cancellationToken).ConfigureAwait(false);
        var now = MariaTimeText.Now();   // the precision the database keeps, so what is returned equals what is read back later
        var nowText = MariaTimeText.Format(now);
        // The time of the ANAF reading is kept for data that came from ANAF (unchanged or edited afterwards).
        DateTime? verified = value.Source is SupplierSources.Anaf or SupplierSources.AnafEdited ? now : null;
        var supplier = await WriteAsync(async (connection, transaction) =>
        {
            await EnsureUniqueAsync(connection, transaction, value, null, cancellationToken).ConfigureAwait(false);
            await using var command = Command(connection, transaction, """
                INSERT INTO suppliers (name, cui, normalized_cui, country, address, phone, registry_number, postal_code, caen_code, `source`,
                    verified_utc, created_by, created_utc, version)
                VALUES (@name, @cui, @key, @country, @address, @phone, @registryNumber, @postalCode, @caenCode, @source,
                    @verified, @createdBy, @created, 0)
                """, [.. Fields(value), ("@verified", verified is { } at ? MariaTimeText.Format(at) : ""), ("@createdBy", actor.Username), ("@created", nowText)]);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return Build(checked((int)command.LastInsertedId), value, 0, verified);
        }, cancellationToken, SupplierRules.IdentityKey(value.Country, value.Cui), null).ConfigureAwait(false);
        await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.Supplier, AuditActions.CreateSupplier, supplier.Id.ToString(),
            SupplierRules.Target(supplier), SupplierRules.Identification(supplier), string.Empty, cancellationToken).ConfigureAwait(false);
        return supplier;
    }

    public async Task<Supplier> UpdateAsync(Supplier original, SupplierInput input, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        var value = input.Validated(true);
        var now = MariaTimeText.Now();
        var supplier = await WriteAsync(async (connection, transaction) =>
        {
            var current = await GetAsync(connection, transaction, original.Id, true, cancellationToken).ConfigureAwait(false);
            SupplierRules.CheckCurrent(current, original);
            // The tax id is the identity: while invoices, entries or templates refer to the supplier it stays as it is.
            if (current!.InUse && SupplierRules.IdentityKey(value.Country, value.Cui) != SupplierRules.IdentityKey(current.Country, current.Cui))
                throw new SupplierOperationException(SupplierRules.CuiLockedMessage);
            await EnsureUniqueAsync(connection, transaction, value, original.Id, cancellationToken).ConfigureAwait(false);
            var version = checked(current.Version + 1);
            var verified = value.AnafRecheck ? now : current.VerifiedUtc;
            await using var command = Command(connection, transaction, """
                UPDATE suppliers SET name=@name, cui=@cui, normalized_cui=@key, country=@country, address=@address, phone=@phone,
                    registry_number=@registryNumber, postal_code=@postalCode, caen_code=@caenCode, `source`=@source,
                    verified_utc=@verified, version=@version
                WHERE id=@id AND version=@oldVersion
                """, [.. Fields(value), ("@verified", verified is { } at ? MariaTimeText.Format(at) : ""), ("@version", version),
                    ("@id", original.Id), ("@oldVersion", current.Version)]);
            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw new SupplierOperationException(SupplierRules.StaleMessage);
            return Build(original.Id, value, version, verified) with { MovementCount = current.MovementCount, InvoiceCount = current.InvoiceCount, TemplateCount = current.TemplateCount };
        }, cancellationToken, SupplierRules.IdentityKey(value.Country, value.Cui), original.Id).ConfigureAwait(false);
        var changes = SupplierRules.Changes(original, supplier)
            .Append(new AuditChange("Ultima verificare ANAF", SupplierRules.DisplayTime(original.VerifiedUtc), SupplierRules.DisplayTime(supplier.VerifiedUtc)));
        await AuditRecorder.RecordEditAsync(auditTrail, accessControl, AuditEntities.Supplier, supplier.Id.ToString(), SupplierRules.Target(supplier),
            changes, value.Reason, cancellationToken, value.AnafRecheck ? AuditActions.RecheckSupplier : AuditActions.EditSupplier).ConfigureAwait(false);
        return supplier;
    }

    public async Task DeleteAsync(Supplier original, string reason, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var motif = ChangeReasonRules.Normalize(reason);
        if (ChangeReasonRules.ValidationError(motif) is { } reasonError) throw new SupplierOperationException(reasonError);
        await archiver.ExecuteAsync(ArchiveRequests.Supplier(original, motif), async (operation, token) =>
        {
            await WriteAsync(async (connection, transaction) =>
            {
                var current = await GetAsync(connection, transaction, original.Id, true, token).ConfigureAwait(false);
                SupplierRules.CheckCurrent(current, original);
                // Checked here, under the row lock, so an invoice recorded a moment ago is seen too.
                if (current!.InUse)
                    throw new SupplierOperationException(SupplierRules.DeleteBlockedMessage(current.InvoiceCount, current.MovementCount, current.TemplateCount));
                await MariaArchivePersistence.InsertAsync(connection, transaction, operation, [], token).ConfigureAwait(false);
                await using var delete = Command(connection, transaction, "DELETE FROM suppliers WHERE id=@id AND version=@version",
                    ("@id", original.Id), ("@version", original.Version));
                if (await delete.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1) throw new SupplierOperationException(SupplierRules.StaleMessage);
                await MariaArchivePersistence.InsertAuditAsync(connection, transaction, operation, token).ConfigureAwait(false);
                return true;
            }, token, null, null).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    // Locks only the supplier row (FOR UPDATE) when written to; the counts are read with it, under the same transaction.
    private static async Task<Supplier?> GetAsync(MySqlConnection connection, MySqlTransaction? transaction, int id, bool forUpdate, CancellationToken token)
    {
        if (forUpdate)
        {
            await using var lockCommand = Command(connection, transaction, "SELECT id FROM suppliers WHERE id=@id FOR UPDATE", ("@id", id));
            if (await lockCommand.ExecuteScalarAsync(token).ConfigureAwait(false) is null) return null;
        }
        await using var command = Command(connection, transaction, $"{Select} WHERE s.id=@id", ("@id", id));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        return await reader.ReadAsync(token).ConfigureAwait(false) ? Read(reader) : null;
    }

    private static Supplier Read(MySqlDataReader reader)
    {
        var verified = reader.GetString(11);
        return new(checked((int)reader.GetInt64(0)), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt64(4),
            reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetString(8), reader.GetString(9), reader.GetString(10),
            verified.Length == 0 ? null : MariaTimeText.Parse(verified),
            Convert.ToInt32(reader.GetValue(12)), Convert.ToInt32(reader.GetValue(13)), Convert.ToInt32(reader.GetValue(14)));
    }

    private static Supplier Build(int id, SupplierInput value, long version, DateTime? verified) => new(id, value.Name, value.Cui, value.Country, version,
        value.Address, value.Phone, value.RegistryNumber, value.PostalCode, value.CaenCode, value.Source, verified);

    private static (string, object)[] Fields(SupplierInput value) =>
    [
        ("@name", value.Name), ("@cui", value.Cui), ("@key", SupplierRules.IdentityKey(value.Country, value.Cui)), ("@country", value.Country),
        ("@address", value.Address), ("@phone", value.Phone), ("@registryNumber", value.RegistryNumber), ("@postalCode", value.PostalCode),
        ("@caenCode", value.CaenCode), ("@source", value.Source)
    ];

    private static async Task EnsureUniqueAsync(MySqlConnection connection, MySqlTransaction transaction, SupplierInput value, int? excludedId, CancellationToken token)
    {
        if (await ExistingNameAsync(connection, transaction, SupplierRules.IdentityKey(value.Country, value.Cui), excludedId, token).ConfigureAwait(false) is { } existing)
            throw new SupplierOperationException(SupplierRules.DuplicateMessage(existing));
    }

    private static async Task<string?> ExistingNameAsync(MySqlConnection connection, MySqlTransaction? transaction, string key, int? excludedId, CancellationToken token)
    {
        await using var command = Command(connection, transaction, "SELECT name FROM suppliers WHERE normalized_cui=@key AND (@id IS NULL OR id<>@id) LIMIT 1",
            ("@key", key), ("@id", excludedId is null ? DBNull.Value : excludedId.Value));
        return await command.ExecuteScalarAsync(token).ConfigureAwait(false) as string;
    }

    private Task<T> WriteAsync<T>(Func<MySqlConnection, MySqlTransaction, Task<T>> action, CancellationToken token, string? duplicateKey, int? excludedId) =>
        MariaTransactions.RetryOnDeadlockAsync(() => WriteOnceAsync(action, token, duplicateKey, excludedId), token);

    private async Task<T> WriteOnceAsync<T>(Func<MySqlConnection, MySqlTransaction, Task<T>> action, CancellationToken token, string? duplicateKey, int? excludedId)
    {
        if (!MariaDatabaseGuard.IsAllowedDatabase(configuration))
            throw new SupplierOperationException("Modificările sunt permise numai în baza BlazorStoc.");
        await using var connection = CreateConnection();
        await connection.OpenAsync(token).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, token).ConfigureAwait(false);
        try
        {
            var result = await action(connection, transaction).ConfigureAwait(false);
            await transaction.CommitAsync(token).ConfigureAwait(false);
            return result;
        }
        catch (Exception exception)
        {
            if (transaction.Connection is not null) await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            // A concurrent save passed the check and hit the unique key: report the stored supplier (read after the rollback).
            if (duplicateKey is not null && exception is MySqlException { Number: 1062 })
                throw new SupplierOperationException(SupplierRules.DuplicateMessage(await ConcurrentNameAsync(duplicateKey, excludedId, token).ConfigureAwait(false)));
            throw;
        }
    }

    private async Task<string?> ConcurrentNameAsync(string key, int? excludedId, CancellationToken token)
    {
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(token).ConfigureAwait(false);
            return await ExistingNameAsync(connection, null, key, excludedId, token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { return null; }
    }

    private static MySqlCommand Command(MySqlConnection connection, MySqlTransaction? transaction, string sql, params (string Name, object Value)[] parameters)
    {
        var command = new MySqlCommand(sql, connection, transaction);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return command;
    }

    private Task EnsureOperatorAsync(CancellationToken token) => accessControl?.EnsureProductOperatorAsync(token) ?? Task.CompletedTask;
}
