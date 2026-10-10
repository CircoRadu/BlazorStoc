using System.Data;
using MySqlConnector;
using static BlazorStoc.Services.MariaDb;

namespace BlazorStoc.Services;

// MariaDB mode: the invoices of suppliers (`supplier_invoices`, migration 13). An invoice is recorded by the invoice pickup, before the
// entries that refer to it; the same number of the same supplier is recorded once.
public sealed class MariaSupplierInvoiceRepository(
    IConfiguration configuration,
    IAccessControl? accessControl = null,
    IAuditTrail? auditTrail = null) : ISupplierInvoiceRepository
{

    public async Task<IReadOnlyList<SupplierInvoice>> GetForSupplierAsync(int supplierId, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
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

    public async Task<IReadOnlyList<SupplierInvoice>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, null, """
            SELECT i.id,i.supplier_id,s.name,i.`number`,i.issue_date,i.created_by,i.created_utc,
                   (SELECT COUNT(*) FROM stock_movements m WHERE m.invoice_id=i.id)
            FROM supplier_invoices i INNER JOIN suppliers s ON s.id=i.supplier_id
            ORDER BY i.issue_date DESC, i.id DESC
            """);
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
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
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
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
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
        }, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.SupplierInvoice, AuditActions.RecordSupplierInvoice,
            invoice.Id.ToString(), SupplierInvoiceRules.Target(invoice.SupplierName, invoice.Number), SupplierInvoiceRules.Identification(invoice), string.Empty,
            cancellationToken).ConfigureAwait(false);
        return invoice;
    }

    public async Task<SupplierInvoice> UpdateAsync(SupplierInvoice original, SupplierInvoiceInput input, string reason, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var motif = ChangeReasonRules.Normalize(reason);
        if (ChangeReasonRules.ValidationError(motif) is { } reasonError) throw new SupplierInvoiceOperationException(reasonError);
        var value = SupplierInvoiceRules.Validated(input);
        var updated = await WriteAsync(async (connection, transaction) =>
        {
            await using (var row = Command(connection, transaction, "SELECT supplier_id,`number`,issue_date FROM supplier_invoices WHERE id=@id FOR UPDATE", ("@id", original.Id)))
            await using (var reader = await row.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false) || checked((int)reader.GetInt64(0)) != original.SupplierId
                    || reader.GetString(1) != original.Number || StockMovementRules.ParseStorageDate(reader.GetString(2)) != original.Date)
                    throw new SupplierInvoiceOperationException(SupplierInvoiceRules.StaleMessage);
            }
            string supplierName;
            await using (var supplier = Command(connection, transaction, "SELECT name FROM suppliers WHERE id=@id FOR UPDATE", ("@id", value.SupplierId!.Value)))
                supplierName = await supplier.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string
                    ?? throw new SupplierInvoiceOperationException(SupplierInvoiceRules.SupplierMissingMessage);
            await using (var duplicate = Command(connection, transaction,
                "SELECT issue_date FROM supplier_invoices WHERE supplier_id=@supplier AND normalized_number=@key AND id<>@id LIMIT 1",
                ("@supplier", value.SupplierId.Value), ("@key", SupplierInvoiceRules.NumberKey(value.Number)), ("@id", original.Id)))
                if (await duplicate.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is string issued)
                    throw new SupplierInvoiceOperationException(SupplierInvoiceRules.DuplicateMessage(supplierName, value.Number, StockMovementRules.ParseStorageDate(issued)));
            await using var update = Command(connection, transaction,
                "UPDATE supplier_invoices SET supplier_id=@supplier,`number`=@number,normalized_number=@key,issue_date=@date WHERE id=@id",
                ("@supplier", value.SupplierId.Value), ("@number", value.Number), ("@key", SupplierInvoiceRules.NumberKey(value.Number)),
                ("@date", SupplierInvoiceRules.StorageDate(value.Date!.Value)), ("@id", original.Id));
            await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return original with { SupplierId = value.SupplierId.Value, SupplierName = supplierName, Number = value.Number, Date = value.Date.Value };
        }, cancellationToken).ConfigureAwait(false);
        var target = SupplierInvoiceRules.Target(updated.SupplierName, updated.Number);
        foreach (var (action, field) in new[] { (AuditActions.EditSupplierInvoiceNumber, "Număr factură"), (AuditActions.EditSupplierInvoiceDate, "Data emiterii"), (AuditActions.MoveSupplierInvoice, "Furnizor") })
        {
            var changes = SupplierInvoiceRules.Changes(original, updated).Where(change => change.Field == field).ToArray();
            if (changes.Any(change => change.Before != change.After))
                await AuditRecorder.RecordEditAsync(auditTrail, accessControl, AuditEntities.SupplierInvoice, updated.Id.ToString(), target, changes, motif, cancellationToken, action).ConfigureAwait(false);
        }
        return updated;
    }

    public async Task DeleteAsync(SupplierInvoice original, string reason, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var motif = ChangeReasonRules.Normalize(reason);
        if (ChangeReasonRules.ValidationError(motif) is { } reasonError) throw new SupplierInvoiceOperationException(reasonError);
        await WriteAsync(async (connection, transaction) =>
        {
            await using (var row = Command(connection, transaction, "SELECT supplier_id,`number`,issue_date FROM supplier_invoices WHERE id=@id FOR UPDATE", ("@id", original.Id)))
            await using (var reader = await row.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false) || checked((int)reader.GetInt64(0)) != original.SupplierId
                    || reader.GetString(1) != original.Number || StockMovementRules.ParseStorageDate(reader.GetString(2)) != original.Date)
                    throw new SupplierInvoiceOperationException(SupplierInvoiceRules.StaleMessage);
            }
            await using (var count = Command(connection, transaction, "SELECT COUNT(*) FROM stock_movements WHERE invoice_id=@id", ("@id", original.Id)))
                if (Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) is var movements and > 0)
                    throw new SupplierInvoiceOperationException(SupplierInvoiceRules.DeleteBlockedMessage(movements));
            await using var delete = Command(connection, transaction, "DELETE FROM supplier_invoices WHERE id=@id", ("@id", original.Id));
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordDeleteAsync(auditTrail, accessControl, AuditEntities.SupplierInvoice, original.Id.ToString(),
            SupplierInvoiceRules.Target(original.SupplierName, original.Number), SupplierInvoiceRules.Identification(original), motif, cancellationToken).ConfigureAwait(false);
    }

    private Task<T> WriteAsync<T>(Func<MySqlConnection, MySqlTransaction, Task<T>> action, CancellationToken token) =>
        MariaDb.WriteAsync(configuration, action, message => new SupplierInvoiceOperationException(message), Translate, token);

    private static Exception? Translate(MySqlException exception) => exception.Number switch
    {
        1062 => new SupplierInvoiceOperationException("Factura cu acest număr a fost preluată între timp pentru același furnizor."),
        1452 => new SupplierInvoiceOperationException(SupplierInvoiceRules.SupplierMissingMessage),
        _ => null
    };

    private Task EnsureOperatorAsync(CancellationToken token) => accessControl?.EnsureProductOperatorAsync(token) ?? Task.CompletedTask;
}
