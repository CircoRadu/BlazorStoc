using MySqlConnector;

namespace BlazorStoc.Services;

// The internal code a supplier gives a product on its invoices ("INT-0042") linked to the product of the catalog (table `supplier_product_codes`, migration 36).
// It is only a proposal for the next invoices of that supplier: one row per supplier and code, the last choice of the user wins, and both the
// linking and a change of the link are in the audit trail with the product before and after. The row goes with the supplier or the product.
public interface ISupplierProductCodes
{
    // The links of a supplier: the key of the code (see SupplierProductCodeRules.Key) -> the product.
    Task<IReadOnlyDictionary<string, int>> GetAsync(int supplierId, CancellationToken cancellationToken = default);

    // Links the code of the supplier to the product; false when the link was already so (nothing is written).
    Task<bool> LinkAsync(int supplierId, string code, int productId, CancellationToken cancellationToken = default);
}

public static class SupplierProductCodeRules
{
    public const int MaxCodeLength = 100;
    public const int MinKeyLength = 2;

    // Letters and digits only, without diacritics, upper case: "INT-0042" and "int 0042" are the same code.
    public static string Key(string? code) => InvoiceProductMatcher.Compact(code);

    // A code that is too short or too long is not worth a link (a one-letter code stands for nothing).
    public static bool IsLinkable(string? code) => Key(code).Length >= MinKeyLength && (code ?? "").Trim().Length <= MaxCodeLength;
}

public sealed class MariaSupplierProductCodes(IConfiguration configuration, IAccessControl? accessControl = null, IAuditTrail? auditTrail = null) : ISupplierProductCodes
{
    private MySqlConnection CreateConnection() => DatabaseConnections.Create(configuration);

    public async Task<IReadOnlyDictionary<string, int>> GetAsync(int supplierId, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureProductOperatorAsync(cancellationToken).ConfigureAwait(false);
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("SELECT code_key, product_id FROM supplier_product_codes WHERE supplier_id=@supplier", connection);
        command.Parameters.AddWithValue("@supplier", supplierId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result[reader.GetString(0)] = checked((int)reader.GetInt64(1));
        return result;
    }

    public async Task<bool> LinkAsync(int supplierId, string code, int productId, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureProductOperatorAsync(cancellationToken).ConfigureAwait(false);
        if (!SupplierProductCodeRules.IsLinkable(code) || !MariaDatabaseGuard.IsAllowedDatabase(configuration)) return false;
        var text = code.Trim();
        var key = SupplierProductCodeRules.Key(text);
        var actor = await RepositoryAudit.ActorAsync(accessControl, cancellationToken).ConfigureAwait(false);
        var now = MariaTimeText.Format(MariaTimeText.Now());
        string supplierName, newName;
        string? oldName = null;
        await using (var connection = CreateConnection())
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            supplierName = await Scalar(connection, "SELECT name FROM suppliers WHERE id=@id", supplierId, cancellationToken).ConfigureAwait(false) ?? "";
            newName = await Scalar(connection, "SELECT name FROM products WHERE id=@id", productId, cancellationToken).ConfigureAwait(false) ?? "";
            if (supplierName.Length == 0 || newName.Length == 0) return false;
            long? existingProduct = null;
            await using (var read = new MySqlCommand("SELECT product_id FROM supplier_product_codes WHERE supplier_id=@supplier AND code_key=@key", connection))
            {
                read.Parameters.AddWithValue("@supplier", supplierId);
                read.Parameters.AddWithValue("@key", key);
                if (await read.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is { } value and not DBNull) existingProduct = Convert.ToInt64(value);
            }
            if (existingProduct == productId) return false;
            if (existingProduct is { } old) oldName = await Scalar(connection, "SELECT name FROM products WHERE id=@id", old, cancellationToken).ConfigureAwait(false) ?? "";
            await using var write = new MySqlCommand(existingProduct is null
                ? "INSERT INTO supplier_product_codes (supplier_id, code_key, code, product_id, created_by, created_utc, updated_by, updated_utc) VALUES (@supplier, @key, @code, @product, @by, @now, @by, @now)"
                : "UPDATE supplier_product_codes SET product_id=@product, code=@code, updated_by=@by, updated_utc=@now WHERE supplier_id=@supplier AND code_key=@key", connection);
            write.Parameters.AddWithValue("@supplier", supplierId);
            write.Parameters.AddWithValue("@key", key);
            write.Parameters.AddWithValue("@code", text);
            write.Parameters.AddWithValue("@product", productId);
            write.Parameters.AddWithValue("@by", actor.Username);
            write.Parameters.AddWithValue("@now", now);
            await write.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await AuditRecorder.RecordEditAsync(auditTrail, accessControl, AuditEntities.Supplier, supplierId.ToString(), supplierName,
            [new AuditChange($"Produs pentru codul furnizorului „{text}”", oldName ?? "—", newName)], string.Empty, cancellationToken,
            oldName is null ? AuditActions.LinkSupplierProductCode : AuditActions.ChangeSupplierProductCode).ConfigureAwait(false);
        return true;
    }

    private static async Task<string?> Scalar(MySqlConnection connection, string sql, long id, CancellationToken cancellationToken)
    {
        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", id);
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
    }
}
