using System.Data;
using MySqlConnector;

namespace BlazorStoc.Services;

// MariaDB mode: the invoices of suppliers (`supplier_invoices`, migration 13). An invoice is recorded by the invoice pickup, before the
// entries that refer to it; the same number of the same supplier is recorded once.
public sealed class MariaSupplierInvoiceRepository(
    IConfiguration configuration,
    IAccessControl? accessControl = null,
    IAuditTrail? auditTrail = null) : ISupplierInvoiceRepository
{
    private MySqlConnection CreateConnection() => DatabaseConnections.Create(configuration);

    public async Task<IReadOnlyList<SupplierInvoice>> GetForSupplierAsync(int supplierId, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, null, """
            SELECT i.id,i.supplier_id,s.name,i.`number`,i.issue_date,i.created_by,i.created_utc,
                   (SELECT COUNT(*) FROM stock_movements m WHERE m.invoice_id=i.id)
            FROM supplier_invoices i INNER JOIN suppliers s ON s.id=i.supplier_id
            WHERE i.supplier_id=@id ORDER BY i.issue_date DESC, i.id DESC
            """, ("@id", supplierId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<SupplierInvoice>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new(checked((int)reader.GetInt64(0)), checked((int)reader.GetInt64(1)), reader.GetString(2), reader.GetString(3),
                StockMovementRules.ParseStorageDate(reader.GetString(4)), reader.GetString(5), MariaTimeText.Parse(reader.GetString(6)),
                Convert.ToInt32(reader.GetValue(7))));
        return result;
    }

    public async Task<SupplierInvoice?> FindAsync(int supplierId, string number, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, null, """
            SELECT i.id,i.supplier_id,s.name,i.`number`,i.issue_date,i.created_by,i.created_utc,
                   (SELECT COUNT(*) FROM stock_movements m WHERE m.invoice_id=i.id)
            FROM supplier_invoices i INNER JOIN suppliers s ON s.id=i.supplier_id
            WHERE i.supplier_id=@supplier AND i.normalized_number=@key
            """, ("@supplier", supplierId), ("@key", SupplierInvoiceRules.NumberKey(number)));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new(checked((int)reader.GetInt64(0)), checked((int)reader.GetInt64(1)), reader.GetString(2), reader.GetString(3),
                StockMovementRules.ParseStorageDate(reader.GetString(4)), reader.GetString(5), MariaTimeText.Parse(reader.GetString(6)), Convert.ToInt32(reader.GetValue(7)))
            : null;
    }

    public async Task<IReadOnlyList<InvoiceEntry>> GetEntriesAsync(int invoiceId, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, null, """
            SELECT m.id,m.product_id,p.name,m.quantity,m.movement_date FROM stock_movements m INNER JOIN products p ON p.id=m.product_id
            WHERE m.invoice_id=@id ORDER BY m.id
            """, ("@id", invoiceId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<InvoiceEntry>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new(checked((int)reader.GetInt64(0)), checked((int)reader.GetInt64(1)), reader.GetString(2), checked((int)reader.GetInt64(3)),
                StockMovementRules.ParseStorageDate(reader.GetString(4))));
        return result;
    }

    public async Task<SupplierInvoice> CreateAsync(SupplierInvoiceInput input, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        var value = SupplierInvoiceRules.Validated(input);
        var actor = await RepositoryAudit.ActorAsync(accessControl, cancellationToken).ConfigureAwait(false);
        var now = MariaTimeText.Now();
        var invoice = await WriteAsync(async (connection, transaction) =>
        {
            string supplierName;
            await using (var supplier = Command(connection, transaction, "SELECT name FROM suppliers WHERE id=@id FOR UPDATE", ("@id", value.SupplierId!.Value)))
                supplierName = await supplier.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string
                    ?? throw new SupplierInvoiceOperationException(SupplierInvoiceRules.SupplierMissingMessage);
            await using (var duplicate = Command(connection, transaction,
                "SELECT issue_date FROM supplier_invoices WHERE supplier_id=@supplier AND normalized_number=@key LIMIT 1",
                ("@supplier", value.SupplierId.Value), ("@key", SupplierInvoiceRules.NumberKey(value.Number))))
                if (await duplicate.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is string issued)
                    throw new SupplierInvoiceOperationException(SupplierInvoiceRules.DuplicateMessage(supplierName, value.Number, StockMovementRules.ParseStorageDate(issued)));
            await using var insert = Command(connection, transaction, """
                INSERT INTO supplier_invoices (supplier_id, `number`, normalized_number, issue_date, created_by, created_utc)
                VALUES (@supplier, @number, @key, @date, @by, @created)
                """, ("@supplier", value.SupplierId.Value), ("@number", value.Number), ("@key", SupplierInvoiceRules.NumberKey(value.Number)),
                ("@date", SupplierInvoiceRules.StorageDate(value.Date!.Value)), ("@by", actor.Username), ("@created", MariaTimeText.Format(now)));
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return new SupplierInvoice(checked((int)insert.LastInsertedId), value.SupplierId.Value, supplierName, value.Number, value.Date.Value, actor.Username, now);
        }, cancellationToken, value).ConfigureAwait(false);
        await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.SupplierInvoice, AuditActions.RecordSupplierInvoice,
            invoice.Id.ToString(), SupplierInvoiceRules.Target(invoice.SupplierName, invoice.Number), SupplierInvoiceRules.Identification(invoice), string.Empty,
            cancellationToken).ConfigureAwait(false);
        return invoice;
    }

    private Task<T> WriteAsync<T>(Func<MySqlConnection, MySqlTransaction, Task<T>> action, CancellationToken token, SupplierInvoiceInput value) =>
        MariaTransactions.RetryOnDeadlockAsync(() => WriteOnceAsync(action, token, value), token);

    private async Task<T> WriteOnceAsync<T>(Func<MySqlConnection, MySqlTransaction, Task<T>> action, CancellationToken token, SupplierInvoiceInput value)
    {
        if (!MariaDatabaseGuard.IsAllowedDatabase(configuration))
            throw new SupplierInvoiceOperationException("Modificările sunt permise numai în baza BlazorStoc.");
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
            if (exception is MySqlException { Number: 1062 })
                throw new SupplierInvoiceOperationException("Factura cu acest număr a fost preluată între timp pentru același furnizor.");
            if (exception is MySqlException { Number: 1452 })
                throw new SupplierInvoiceOperationException(SupplierInvoiceRules.SupplierMissingMessage);
            throw;
        }
    }

    private static MySqlCommand Command(MySqlConnection connection, MySqlTransaction? transaction, string sql, params (string Name, object Value)[] parameters)
    {
        var command = new MySqlCommand(sql, connection, transaction);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return command;
    }

    private Task EnsureOperatorAsync(CancellationToken token) => accessControl?.EnsureProductOperatorAsync(token) ?? Task.CompletedTask;
}
