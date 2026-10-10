using System.Data;
using System.Globalization;
using MySqlConnector;
using static BlazorStoc.Services.MariaDb;

namespace BlazorStoc.Services;

// Required parameters of the subcategories and their values (tables of migration 37). Every write runs in one serializable transaction and is
// journaled with its own action; the product operators add parameters and values and delete the values nobody uses, the administrators edit
// and delete parameters and change a value that products already use (the names of those products follow).
public sealed class MariaProductParameterRepository(IConfiguration configuration, IAccessControl? accessControl = null, IAuditTrail? auditTrail = null)
    : IProductParameterRepository
{

    public async Task<IReadOnlyList<SubcategoryParameter>> GetParametersAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("""
            SELECT sp.id,c.name,s.name,sp.name,sp.unit,sp.kind,sp.position,sp.version,
                   v.id,v.value,(SELECT COUNT(*) FROM product_parameter_values pv WHERE pv.value_id=v.id)
            FROM subcategory_parameters sp
            INNER JOIN subcategories s ON s.id=sp.subcategory_id
            INNER JOIN categories c ON c.id=s.category_id
            LEFT JOIN parameter_values v ON v.parameter_id=sp.id
            ORDER BY c.name,s.name,sp.position,sp.id,v.id
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<SubcategoryParameter>();
        var values = new List<ParameterListValue>();
        SubcategoryParameter? current = null;
        void Close()
        {
            if (current is null) return;
            var sorted = current.Kind == ParameterKind.Number
                ? values.OrderBy(value => decimal.Parse(value.Value, CultureInfo.InvariantCulture)).ToList()
                : values.OrderBy(value => value.Value, StringComparer.CurrentCultureIgnoreCase).ToList();
            result.Add(current with { Values = sorted });
            values = [];
        }
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var id = checked((int)reader.GetInt64(0));
            if (current is null || current.Id != id)
            {
                Close();
                current = new SubcategoryParameter(id, reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
                    (ParameterKind)reader.GetInt32(5), reader.GetInt32(6), reader.GetInt64(7), []);
            }
            if (!reader.IsDBNull(8)) values.Add(new ParameterListValue(checked((int)reader.GetInt64(8)), reader.GetString(9), checked((int)reader.GetInt64(10))));
        }
        Close();
        return result;
    }

    public async Task<SubcategoryParameter> AddParameterAsync(ProductGroup group, string name, string unit, ParameterKind kind, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        var cleanName = ProductParameterRules.Name(name);
        var cleanUnit = kind == ParameterKind.Number ? ProductParameterRules.Unit(unit) : string.Empty;
        if (!Enum.IsDefined(kind)) throw new ProductOperationException("Alege tipul parametrului.");
        (SubcategoryParameter Parameter, int SubcategoryId) result;
        try
        {
            result = await WriteAsync(async (connection, transaction) =>
            {
                var subcategoryId = await FindSubcategoryAsync(connection, transaction, group, cancellationToken).ConfigureAwait(false);
                await using (var exists = Command(connection, transaction, "SELECT 1 FROM subcategory_parameters WHERE subcategory_id=@sub AND normalized_name=@key",
                                 ("@sub", subcategoryId), ("@key", TextNormalization.UniquenessKey(cleanName))))
                    if (await exists.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null)
                        throw new ProductOperationException($"Subcategoria «{group.Subcategory}» are deja un parametru «{cleanName}».");
                await using var position = Command(connection, transaction, "SELECT COALESCE(MAX(position),0)+1 FROM subcategory_parameters WHERE subcategory_id=@sub", ("@sub", subcategoryId));
                var next = Convert.ToInt32(await position.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
                await using var insert = Command(connection, transaction, """
                    INSERT INTO subcategory_parameters(subcategory_id,name,normalized_name,unit,kind,position,version)
                    VALUES(@sub,@name,@key,@unit,@kind,@position,0)
                    """, ("@sub", subcategoryId), ("@name", cleanName), ("@key", TextNormalization.UniquenessKey(cleanName)), ("@unit", cleanUnit),
                    ("@kind", (int)kind), ("@position", next));
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                return (new SubcategoryParameter(checked((int)insert.LastInsertedId), group.Category, group.Subcategory, cleanName, cleanUnit, kind, next, 0, []), subcategoryId);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (MySqlException exception) when (exception.Number == 1062)
        {
            throw new ProductOperationException($"Subcategoria «{group.Subcategory}» are deja un parametru «{cleanName}».");
        }
        await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.Subcategory, AuditActions.AddSubcategoryParameter,
            result.SubcategoryId.ToString(CultureInfo.InvariantCulture), Target(group), AuditDetails.Identification(
                ("Parametru", cleanName), ("Tip", KindLabel(kind)), ("Unitate", cleanUnit.Length == 0 ? "—" : cleanUnit)), string.Empty, cancellationToken).ConfigureAwait(false);
        return result.Parameter;
    }

    public async Task UpdateParameterAsync(SubcategoryParameter original, string name, string unit, string reason, CancellationToken cancellationToken = default)
    {
        await EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var cleanName = ProductParameterRules.Name(name);
        var cleanUnit = original.Kind == ParameterKind.Number ? ProductParameterRules.Unit(unit) : string.Empty;
        var motif = ProductGroupManagementRules.Reason(reason);
        int subcategoryId;
        try
        {
            subcategoryId = await WriteAsync(async (connection, transaction) =>
            {
                var (current, sub) = await LockParameterAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false);
                if (current.Version != original.Version) throw new ProductOperationException("Parametrul a fost modificat între timp. Actualizează lista și reia operația.");
                if (!string.Equals(current.Unit, cleanUnit, StringComparison.Ordinal) && current.ProductCount > 0)
                    throw new ProductOperationException("Unitatea de măsură face parte din codurile produselor și nu se mai schimbă după ce parametrul are produse.");
                await using (var exists = Command(connection, transaction, "SELECT 1 FROM subcategory_parameters WHERE subcategory_id=@sub AND normalized_name=@key AND id<>@id",
                                 ("@sub", sub), ("@key", TextNormalization.UniquenessKey(cleanName)), ("@id", original.Id)))
                    if (await exists.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null)
                        throw new ProductOperationException($"Subcategoria «{original.Subcategory}» are deja un parametru «{cleanName}».");
                await using var update = Command(connection, transaction,
                    "UPDATE subcategory_parameters SET name=@name,normalized_name=@key,unit=@unit,version=version+1 WHERE id=@id",
                    ("@name", cleanName), ("@key", TextNormalization.UniquenessKey(cleanName)), ("@unit", cleanUnit), ("@id", original.Id));
                await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                return sub;
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (MySqlException exception) when (exception.Number == 1062)
        {
            throw new ProductOperationException($"Subcategoria «{original.Subcategory}» are deja un parametru «{cleanName}».");
        }
        await AuditRecorder.RecordEditAsync(auditTrail, accessControl, AuditEntities.Subcategory, subcategoryId.ToString(CultureInfo.InvariantCulture),
            Target(original.Category, original.Subcategory), [new("Denumire parametru", original.Name, cleanName), new("Unitate", original.Unit, cleanUnit)],
            motif, cancellationToken, AuditActions.EditSubcategoryParameter).ConfigureAwait(false);
    }

    public async Task DeleteParameterAsync(SubcategoryParameter original, string reason, CancellationToken cancellationToken = default)
    {
        await EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var motif = ProductGroupManagementRules.Reason(reason);
        var (subcategoryId, valueCount) = await WriteAsync(async (connection, transaction) =>
        {
            var (current, sub) = await LockParameterAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false);
            if (current.Version != original.Version) throw new ProductOperationException("Parametrul a fost modificat între timp. Actualizează lista și reia operația.");
            await using (var products = Command(connection, transaction, "SELECT COUNT(*) FROM products WHERE subcategory_id=@sub", ("@sub", sub)))
                if (Convert.ToInt64(await products.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture) > 0)
                    throw new ProductOperationException("Subcategoria are produse: parametrul lor obligatoriu nu se mai șterge.");
            int values;
            await using (var count = Command(connection, transaction, "SELECT COUNT(*) FROM parameter_values WHERE parameter_id=@id", ("@id", original.Id)))
                values = Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
            await using var delete = Command(connection, transaction, "DELETE FROM subcategory_parameters WHERE id=@id", ("@id", original.Id));
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return (sub, values);
        }, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.Subcategory, AuditActions.DeleteSubcategoryParameter,
            subcategoryId.ToString(CultureInfo.InvariantCulture), Target(original.Category, original.Subcategory),
            AuditDetails.Identification(("Parametru", original.Name), ("Valori șterse", valueCount.ToString(CultureInfo.InvariantCulture))), motif, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ParameterListValue> AddValueAsync(int parameterId, string value, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        (ParameterListValue Value, int SubcategoryId, string Target, string Parameter, string Unit) result;
        try
        {
            result = await WriteAsync(async (connection, transaction) =>
            {
                var (parameter, sub) = await LockParameterAsync(connection, transaction, parameterId, cancellationToken).ConfigureAwait(false);
                var clean = ProductParameterRules.Value(parameter.Kind, parameter.Name, value);
                var key = ProductParameterRules.Key(clean);
                await using (var exists = Command(connection, transaction, "SELECT value FROM parameter_values WHERE parameter_id=@id AND normalized_value=@key",
                                 ("@id", parameterId), ("@key", key)))
                    if (await exists.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is string existing)
                        throw new ProductOperationException($"Valoarea «{existing}» există deja la parametrul «{parameter.Name}». Alege-o din listă.");
                await using var insert = Command(connection, transaction, "INSERT INTO parameter_values(parameter_id,value,normalized_value) VALUES(@id,@value,@key)",
                    ("@id", parameterId), ("@value", clean), ("@key", key));
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                return (new ParameterListValue(checked((int)insert.LastInsertedId), clean, 0), sub, parameter.Target, parameter.Name, parameter.Unit);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (MySqlException exception) when (exception.Number == 1062)
        {
            throw new ProductOperationException("Valoarea există deja la acest parametru. Alege-o din listă.");
        }
        await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.Subcategory, AuditActions.AddParameterValue,
            result.SubcategoryId.ToString(CultureInfo.InvariantCulture), result.Target,
            AuditDetails.Identification(("Parametru", result.Parameter), ("Valoare", ProductParameterRules.Display(result.Unit, result.Value.Value))), string.Empty, cancellationToken).ConfigureAwait(false);
        return result.Value;
    }

    public async Task DeleteValueAsync(int valueId, string reason, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        var motif = ProductGroupManagementRules.Reason(reason);
        var (subcategoryId, target, parameter, display) = await WriteAsync(async (connection, transaction) =>
        {
            var row = await ReadValueAsync(connection, transaction, valueId, true, cancellationToken).ConfigureAwait(false);
            await using (var used = Command(connection, transaction, "SELECT COUNT(*) FROM product_parameter_values WHERE value_id=@id", ("@id", valueId)))
                if (Convert.ToInt64(await used.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture) > 0)
                    throw new ProductOperationException(ProductParameterRules.ValueUsedMessage);
            await using var delete = Command(connection, transaction, "DELETE FROM parameter_values WHERE id=@id", ("@id", valueId));
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return (row.SubcategoryId, row.Target, row.ParameterName, ProductParameterRules.Display(row.Unit, row.Value));
        }, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.Subcategory, AuditActions.DeleteParameterValue,
            subcategoryId.ToString(CultureInfo.InvariantCulture), target, AuditDetails.Identification(("Parametru", parameter), ("Valoare", display)), motif, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ParameterValueChange> PreviewValueChangeAsync(int valueId, string newValue, CancellationToken cancellationToken = default)
    {
        await EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        return (await ComputeChangeAsync(connection, null, valueId, newValue, cancellationToken).ConfigureAwait(false)).Change;
    }

    public async Task<ParameterValueChange> ChangeValueAsync(int valueId, string newValue, string reason, CancellationToken cancellationToken = default)
    {
        await EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var motif = ProductGroupManagementRules.Reason(reason);
        var computed = await WriteAsync(async (connection, transaction) =>
        {
            var result = await ComputeChangeAsync(connection, transaction, valueId, newValue, cancellationToken, lockRows: true).ConfigureAwait(false);
            var change = result.Change;
            if (change.Conflicts.Count > 0) throw new ProductOperationException(ProductParameterRules.ConflictMessage(change.Conflicts));
            if (!change.CanApply) throw new ProductOperationException("Valoarea nu s-a schimbat.");
            await using (var update = Command(connection, transaction, "UPDATE parameter_values SET value=@value,normalized_value=@key WHERE id=@id",
                             ("@value", change.NewValue), ("@key", ProductParameterRules.Key(change.NewValue)), ("@id", valueId)))
                await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            foreach (var product in change.Affected)
            {
                await using var rename = Command(connection, transaction,
                    "UPDATE products SET name=@name,normalized_name=@key,version=version+1 WHERE id=@id",
                    ("@name", product.NewName), ("@key", TextNormalization.UniquenessKey(product.NewName)), ("@id", product.ProductId));
                await rename.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            return result;
        }, cancellationToken).ConfigureAwait(false);
        var applied = computed.Change;
        await AuditRecorder.RecordEditAsync(auditTrail, accessControl, AuditEntities.Subcategory, computed.SubcategoryId.ToString(CultureInfo.InvariantCulture),
            computed.Target, [new($"Valoare «{computed.ParameterName}»", ProductParameterRules.Display(computed.Unit, applied.OldValue), ProductParameterRules.Display(computed.Unit, applied.NewValue))],
            motif, cancellationToken, AuditActions.EditParameterValue).ConfigureAwait(false);
        foreach (var product in applied.Affected)
            await AuditRecorder.RecordEditAsync(auditTrail, accessControl, AuditEntities.Product, product.ProductId.ToString(CultureInfo.InvariantCulture),
                product.NewName, [new(ProductCode.Label, product.OldName, product.NewName)], motif, cancellationToken, AuditActions.RenameProductByParameter).ConfigureAwait(false);
        return applied;
    }

    public async Task<ProductParameterState> GetProductStateAsync(int productId, CancellationToken cancellationToken = default)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        string model;
        await using (var select = Command(connection, null, "SELECT COALESCE(base_model,'') FROM products WHERE id=@id", ("@id", productId)))
            model = await select.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string ?? string.Empty;
        var choices = new List<ProductParameterChoice>();
        await using var command = Command(connection, null, "SELECT parameter_id,value_id FROM product_parameter_values WHERE product_id=@id", ("@id", productId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            choices.Add(new ProductParameterChoice(checked((int)reader.GetInt64(0)), checked((int)reader.GetInt64(1))));
        return new ProductParameterState(model, choices);
    }

    public async Task<IReadOnlySet<int>> GetIncompleteProductIdsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, null, """
            SELECT DISTINCT p.id FROM products p INNER JOIN subcategory_parameters sp ON sp.subcategory_id=p.subcategory_id
            WHERE NOT EXISTS(SELECT 1 FROM product_parameter_values pv WHERE pv.product_id=p.id AND pv.parameter_id=sp.id)
            """);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var ids = new HashSet<int>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) ids.Add(checked((int)reader.GetInt64(0)));
        return ids;
    }

    public async Task<IReadOnlyDictionary<int, string>> GetBaseModelsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, null, "SELECT id,base_model FROM products WHERE base_model IS NOT NULL AND base_model<>''");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var models = new Dictionary<int, string>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) models[checked((int)reader.GetInt64(0))] = reader.GetString(1);
        return models;
    }

    // ---- helpers ------------------------------------------------------------------------------------------------------------

    private sealed record LockedParameter(string Name, string Unit, ParameterKind Kind, long Version, int ProductCount, string Target);
    private sealed record ValueRow(string Value, int ParameterId, string ParameterName, string Unit, ParameterKind Kind, int SubcategoryId, string Target);
    private sealed record ComputedChange(ParameterValueChange Change, int SubcategoryId, string Target, string ParameterName, string Unit);

    private static string Target(ProductGroup group) => Target(group.Category, group.Subcategory);
    private static string Target(string category, string subcategory) => $"{category} / {subcategory}";
    private static string KindLabel(ParameterKind kind) => kind == ParameterKind.Number ? "Număr" : "Text liber";

    private static async Task<int> FindSubcategoryAsync(MySqlConnection connection, MySqlTransaction transaction, ProductGroup group, CancellationToken token)
    {
        await using var command = Command(connection, transaction, """
            SELECT s.id FROM subcategories s INNER JOIN categories c ON c.id=s.category_id
            WHERE s.normalized_name=@sub AND c.normalized_name=@cat
            """, ("@sub", TextNormalization.UniquenessKey(group.Subcategory)), ("@cat", TextNormalization.UniquenessKey(group.Category)));
        return await command.ExecuteScalarAsync(token).ConfigureAwait(false) is long id
            ? checked((int)id)
            : throw new ProductOperationException("Subcategoria nu mai există. Actualizează lista și reia operația.");
    }

    private static async Task<(LockedParameter Parameter, int SubcategoryId)> LockParameterAsync(MySqlConnection connection, MySqlTransaction transaction, int id, CancellationToken token)
    {
        await using var command = Command(connection, transaction, """
            SELECT sp.name,sp.unit,sp.kind,sp.version,sp.subcategory_id,c.name,s.name,
                   (SELECT COUNT(*) FROM product_parameter_values pv WHERE pv.parameter_id=sp.id)
            FROM subcategory_parameters sp INNER JOIN subcategories s ON s.id=sp.subcategory_id INNER JOIN categories c ON c.id=s.category_id
            WHERE sp.id=@id FOR UPDATE
            """, ("@id", id));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
            throw new ProductOperationException("Parametrul nu mai există. Actualizează lista și reia operația.");
        return (new LockedParameter(reader.GetString(0), reader.GetString(1), (ParameterKind)reader.GetInt32(2), reader.GetInt64(3),
            checked((int)reader.GetInt64(7)), Target(reader.GetString(5), reader.GetString(6))), checked((int)reader.GetInt64(4)));
    }

    private static async Task<ValueRow> ReadValueAsync(MySqlConnection connection, MySqlTransaction? transaction, int valueId, bool lockRow, CancellationToken token)
    {
        await using var command = Command(connection, transaction, $"""
            SELECT v.value,sp.id,sp.name,sp.unit,sp.kind,s.id,c.name,s.name
            FROM parameter_values v INNER JOIN subcategory_parameters sp ON sp.id=v.parameter_id
            INNER JOIN subcategories s ON s.id=sp.subcategory_id INNER JOIN categories c ON c.id=s.category_id
            WHERE v.id=@id{(lockRow ? " FOR UPDATE" : "")}
            """, ("@id", valueId));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
            throw new ProductOperationException("Valoarea nu mai există. Actualizează lista și reia operația.");
        return new ValueRow(reader.GetString(0), checked((int)reader.GetInt64(1)), reader.GetString(2), reader.GetString(3), (ParameterKind)reader.GetInt32(4),
            checked((int)reader.GetInt64(5)), Target(reader.GetString(6), reader.GetString(7)));
    }

    // The products that use the value, with the name each would get, and the new names that other products already have.
    private static async Task<ComputedChange> ComputeChangeAsync(MySqlConnection connection, MySqlTransaction? transaction, int valueId, string newRaw,
        CancellationToken token, bool lockRows = false)
    {
        var row = await ReadValueAsync(connection, transaction, valueId, lockRows, token).ConfigureAwait(false);
        var newValue = ProductParameterRules.Value(row.Kind, row.ParameterName, newRaw);
        var key = ProductParameterRules.Key(newValue);
        await using (var duplicate = Command(connection, transaction, "SELECT value FROM parameter_values WHERE parameter_id=@parameter AND normalized_value=@key AND id<>@id",
                         ("@parameter", row.ParameterId), ("@key", key), ("@id", valueId)))
            if (await duplicate.ExecuteScalarAsync(token).ConfigureAwait(false) is string existing)
                throw new ProductOperationException($"Valoarea «{existing}» există deja la parametrul «{row.ParameterName}». Combinarea variantelor nu este disponibilă.");

        var products = new List<(int Id, string Name, int Quantity, string BaseModel)>();
        await using (var select = Command(connection, transaction, $"""
            SELECT p.id,p.name,p.quantity,COALESCE(p.base_model,p.name) FROM product_parameter_values pv
            INNER JOIN products p ON p.id=pv.product_id WHERE pv.value_id=@id ORDER BY p.name{(lockRows ? " FOR UPDATE" : "")}
            """, ("@id", valueId)))
        await using (var reader = await select.ExecuteReaderAsync(token).ConfigureAwait(false))
            while (await reader.ReadAsync(token).ConfigureAwait(false))
                products.Add((checked((int)reader.GetInt64(0)), reader.GetString(1), checked((int)reader.GetInt64(2)), reader.GetString(3)));

        var affected = new List<ParameterAffectedProduct>();
        var conflicts = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var product in products)
        {
            var displays = new List<string>();
            await using (var values = Command(connection, transaction, """
                SELECT sp.unit,v.value,v.id FROM product_parameter_values pv
                INNER JOIN subcategory_parameters sp ON sp.id=pv.parameter_id INNER JOIN parameter_values v ON v.id=pv.value_id
                WHERE pv.product_id=@id ORDER BY sp.position,sp.id
                """, ("@id", product.Id)))
            await using (var reader = await values.ExecuteReaderAsync(token).ConfigureAwait(false))
                while (await reader.ReadAsync(token).ConfigureAwait(false))
                    displays.Add(ProductParameterRules.Display(reader.GetString(0), reader.GetInt64(2) == valueId ? newValue : reader.GetString(1)));
            var newName = ProductParameterRules.ComposeName(product.BaseModel, displays);
            if (newName.Length > ProductParameterRules.ProductNameMaximumLength) { conflicts.Add($"{newName} (peste {ProductParameterRules.ProductNameMaximumLength} de caractere)"); continue; }
            var newKey = TextNormalization.UniquenessKey(newName);
            await using (var taken = Command(connection, transaction, "SELECT name FROM products WHERE normalized_name=@key AND id<>@id LIMIT 1", ("@key", newKey), ("@id", product.Id)))
                if (await taken.ExecuteScalarAsync(token).ConfigureAwait(false) is string clash) conflicts.Add($"{newName} (există deja ca «{clash}»)");
            if (!seen.Add(newKey)) conflicts.Add($"{newName} (rezultă pentru două produse)");
            affected.Add(new ParameterAffectedProduct(product.Id, product.Name, newName, product.Quantity));
        }
        return new ComputedChange(new ParameterValueChange(row.Value, newValue, affected, conflicts), row.SubcategoryId, row.Target, row.ParameterName, row.Unit);
    }

    private Task<T> WriteAsync<T>(Func<MySqlConnection, MySqlTransaction, Task<T>> action, CancellationToken token) =>
        MariaDb.WriteAsync(configuration, action, message => new ProductOperationException(message), token,
            "Modificările sunt permise numai în baza BlazorStoc. Verifică numele bazei configurate.");

    private Task EnsureOperatorAsync(CancellationToken token) => accessControl?.EnsureProductOperatorAsync(token) ?? Task.CompletedTask;
    private Task EnsureAdministratorAsync(CancellationToken token) => accessControl?.EnsureAdministratorAsync(token) ?? Task.CompletedTask;
}
