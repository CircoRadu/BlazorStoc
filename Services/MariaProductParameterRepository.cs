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
                   v.id,v.value,(SELECT COUNT(*) FROM product_parameter_values pv WHERE pv.value_id=v.id),sp.family_id
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
            var sorted = current.Kind switch
            {
                ParameterKind.Number => values.OrderBy(value => decimal.Parse(value.Value, CultureInfo.InvariantCulture)).ToList(),
                ParameterKind.Interval => values.OrderBy(value => ProductParameterRules.IntervalParts(value.Value)?.Start ?? decimal.MaxValue)
                    .ThenBy(value => ProductParameterRules.IntervalParts(value.Value)?.End ?? decimal.MaxValue).ToList(),
                _ => values.OrderBy(value => value.Value, StringComparer.CurrentCultureIgnoreCase).ToList()
            };
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
                    (ParameterKind)reader.GetInt32(5), reader.GetInt32(6), reader.GetInt64(7), [], reader.IsDBNull(11) ? null : checked((int)reader.GetInt64(11)));
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
        var cleanUnit = ProductParameterRules.UsesUnit(kind) ? ProductParameterRules.Unit(unit) : string.Empty;
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
                var parameterId = checked((int)insert.LastInsertedId);
                var values = new List<ParameterListValue>();
                if (kind == ParameterKind.Fixed)
                {
                    // The only value of a fixed parameter is its own name.
                    await using var fixedValue = Command(connection, transaction, "INSERT INTO parameter_values(parameter_id,value,normalized_value) VALUES(@id,@value,@key)",
                        ("@id", parameterId), ("@value", cleanName), ("@key", ProductParameterRules.Key(cleanName)));
                    await fixedValue.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    values.Add(new ParameterListValue(checked((int)fixedValue.LastInsertedId), cleanName, 0));
                }
                return (new SubcategoryParameter(parameterId, group.Category, group.Subcategory, cleanName, cleanUnit, kind, next, 0, values), subcategoryId);
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

    public Task UpdateParameterAsync(SubcategoryParameter original, string name, string unit, string reason, CancellationToken cancellationToken = default) =>
        UpdateParameterAsync(original, name, unit, original.Kind, reason, cancellationToken);

    public async Task UpdateParameterAsync(SubcategoryParameter original, string name, string unit, ParameterKind kind, string reason, CancellationToken cancellationToken = default)
    {
        await EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var cleanName = ProductParameterRules.Name(name);
        var cleanUnit = ProductParameterRules.UsesUnit(kind) ? ProductParameterRules.Unit(unit) : string.Empty;
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
                if (current.Kind != kind && (current.Kind == ParameterKind.Fixed || kind == ParameterKind.Fixed)) throw new ProductOperationException(ProductParameterRules.FixedKindChangeMessage);
                if (current.Kind == ParameterKind.Fixed && !string.Equals(current.Name, cleanName, StringComparison.Ordinal))
                {
                    if (current.ProductCount > 0) throw new ProductOperationException(ProductParameterRules.FixedRenameUsedMessage);
                    await using var renameValue = Command(connection, transaction, "UPDATE parameter_values SET value=@value,normalized_value=@key WHERE parameter_id=@id",
                        ("@value", cleanName), ("@key", ProductParameterRules.Key(cleanName)), ("@id", original.Id));
                    await renameValue.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
                if (current.Kind != kind) await EnsureValuesFitKindAsync(connection, transaction, original.Id, cleanName, kind, cancellationToken).ConfigureAwait(false);
                await using (var exists = Command(connection, transaction, "SELECT 1 FROM subcategory_parameters WHERE subcategory_id=@sub AND normalized_name=@key AND id<>@id",
                                 ("@sub", sub), ("@key", TextNormalization.UniquenessKey(cleanName)), ("@id", original.Id)))
                    if (await exists.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null)
                        throw new ProductOperationException($"Subcategoria «{original.Subcategory}» are deja un parametru «{cleanName}».");
                await using var update = Command(connection, transaction,
                    "UPDATE subcategory_parameters SET name=@name,normalized_name=@key,unit=@unit,kind=@kind,version=version+1 WHERE id=@id",
                    ("@name", cleanName), ("@key", TextNormalization.UniquenessKey(cleanName)), ("@unit", cleanUnit), ("@kind", (int)kind), ("@id", original.Id));
                await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                return sub;
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (MySqlException exception) when (exception.Number == 1062)
        {
            throw new ProductOperationException($"Subcategoria «{original.Subcategory}» are deja un parametru «{cleanName}».");
        }
        await AuditRecorder.RecordEditAsync(auditTrail, accessControl, AuditEntities.Subcategory, subcategoryId.ToString(CultureInfo.InvariantCulture),
            Target(original.Category, original.Subcategory), [new("Denumire parametru", original.Name, cleanName), new("Unitate", original.Unit, cleanUnit), new("Tip parametru", KindLabel(original.Kind), KindLabel(kind))],
            motif, cancellationToken, AuditActions.EditSubcategoryParameter).ConfigureAwait(false);
    }

    // A parameter changes kind only when each of its values is already written the way the new kind stores it (so no product name changes):
    // anything fits a text, "2.8" fits a number, "2.8-12" fits an interval. The values that do not fit are listed, to be corrected first.
    private static async Task EnsureValuesFitKindAsync(MySqlConnection connection, MySqlTransaction transaction, int parameterId, string name, ParameterKind kind, CancellationToken token)
    {
        var misfits = new List<string>();
        await using (var command = Command(connection, transaction, "SELECT value FROM parameter_values WHERE parameter_id=@id ORDER BY value", ("@id", parameterId)))
        await using (var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                var value = reader.GetString(0);
                try { if (!string.Equals(ProductParameterRules.Value(kind, name, value), value, StringComparison.Ordinal)) misfits.Add(value); }
                catch (ProductOperationException) { misfits.Add(value); }
            }
        if (misfits.Count > 0)
            throw new ProductOperationException($"Tipul nu se poate schimba în «{KindLabel(kind)}»: valorile {string.Join(", ", misfits.Take(8).Select(value => $"«{value}»"))}{(misfits.Count > 8 ? " și altele" : "")} nu se potrivesc. Șterge-le sau corectează-le întâi.");
    }

    // The products that carry a value of the parameter lose it, so the value leaves their code; names that would clash are listed and nothing is deleted.
    private static async Task<ParameterDeletionPreview> ComputeDeletionAsync(MySqlConnection connection, MySqlTransaction? transaction, int parameterId, bool lockRows, CancellationToken token)
    {
        var products = new List<(int Id, string Name, int Quantity, string BaseModel)>();
        await using (var select = Command(connection, transaction, $"""
            SELECT p.id,p.name,p.quantity,COALESCE(p.base_model,p.name) FROM product_parameter_values pv
            INNER JOIN products p ON p.id=pv.product_id WHERE pv.parameter_id=@parameter ORDER BY p.name{(lockRows ? " FOR UPDATE" : "")}
            """, ("@parameter", parameterId)))
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
                SELECT sp.unit,v.value FROM product_parameter_values pv
                INNER JOIN subcategory_parameters sp ON sp.id=pv.parameter_id INNER JOIN parameter_values v ON v.id=pv.value_id
                WHERE pv.product_id=@id AND pv.parameter_id<>@parameter
                ORDER BY COALESCE((SELECT MIN(m.position) FROM subcategory_parameters m WHERE m.family_id=sp.family_id),sp.position),sp.position,sp.id
                """, ("@id", product.Id), ("@parameter", parameterId)))
            await using (var reader = await values.ExecuteReaderAsync(token).ConfigureAwait(false))
                while (await reader.ReadAsync(token).ConfigureAwait(false)) displays.Add(ProductParameterRules.Display(reader.GetString(0), reader.GetString(1)));
            var newName = ProductParameterRules.ComposeName(product.BaseModel, displays);
            var newKey = TextNormalization.UniquenessKey(newName);
            await using (var taken = Command(connection, transaction, "SELECT name FROM products WHERE normalized_name=@key AND id<>@id LIMIT 1", ("@key", newKey), ("@id", product.Id)))
                if (await taken.ExecuteScalarAsync(token).ConfigureAwait(false) is string clash && !products.Any(other => other.Name == clash)) conflicts.Add($"{newName} (există deja ca «{clash}»)");
            if (!seen.Add(newKey)) conflicts.Add($"{newName} (rezultă pentru două produse)");
            affected.Add(new ParameterAffectedProduct(product.Id, product.Name, newName, product.Quantity));
        }
        return new ParameterDeletionPreview(affected, conflicts);
    }

    public async Task<ParameterDeletionPreview> PreviewParameterDeletionAsync(SubcategoryParameter parameter, CancellationToken cancellationToken = default)
    {
        await EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        return await ComputeDeletionAsync(connection, null, parameter.Id, false, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteParameterAsync(SubcategoryParameter original, string reason, CancellationToken cancellationToken = default)
    {
        await EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var motif = ProductGroupManagementRules.Reason(reason);
        var (subcategoryId, valueCount, removal) = await WriteAsync(async (connection, transaction) =>
        {
            var (current, sub) = await LockParameterAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false);
            if (current.Version != original.Version) throw new ProductOperationException("Parametrul a fost modificat între timp. Actualizează lista și reia operația.");
            var deletion = await ComputeDeletionAsync(connection, transaction, original.Id, true, cancellationToken).ConfigureAwait(false);
            if (deletion.Conflicts.Count > 0) throw new ProductOperationException(ProductParameterRules.ConflictMessage(deletion.Conflicts));
            foreach (var product in deletion.Affected)
            {
                await using var rename = Command(connection, transaction, "UPDATE products SET name=@name,normalized_name=@key,version=version+1 WHERE id=@id",
                    ("@name", product.NewName), ("@key", TextNormalization.UniquenessKey(product.NewName)), ("@id", product.ProductId));
                await rename.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            await using (var drop = Command(connection, transaction, "DELETE FROM product_parameter_values WHERE parameter_id=@id", ("@id", original.Id)))
                await drop.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            int values;
            await using (var count = Command(connection, transaction, "SELECT COUNT(*) FROM parameter_values WHERE parameter_id=@id", ("@id", original.Id)))
                values = Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
            long? familyId = null;
            await using (var family = Command(connection, transaction, "SELECT family_id FROM subcategory_parameters WHERE id=@id", ("@id", original.Id)))
                if (await family.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is long inFamily) familyId = inFamily;
            await using (var delete = Command(connection, transaction, "DELETE FROM subcategory_parameters WHERE id=@id", ("@id", original.Id)))
                await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            if (familyId is { } leaving)
            {
                // A family keeps at least two parameters: when the deleted one leaves a single member, the family is undone.
                await using var left = Command(connection, transaction, "SELECT COUNT(*) FROM subcategory_parameters WHERE family_id=@id", ("@id", leaving));
                if (Convert.ToInt32(await left.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture) < ProductParameterRules.FamilyMinimumMembers)
                {
                    var remaining = new List<int>();
                    await using (var rest = Command(connection, transaction, "SELECT id FROM subcategory_parameters WHERE family_id=@id", ("@id", leaving)))
                    await using (var restReader = await rest.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                        while (await restReader.ReadAsync(cancellationToken).ConfigureAwait(false)) remaining.Add(checked((int)restReader.GetInt64(0)));
                    await EnsureFixedUnusedAsync(connection, transaction, remaining, cancellationToken).ConfigureAwait(false);
                    await using (var release = Command(connection, transaction, "UPDATE subcategory_parameters SET family_id=NULL,version=version+1 WHERE family_id=@id", ("@id", leaving)))
                        await release.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    await using var undo = Command(connection, transaction, "DELETE FROM parameter_families WHERE id=@id", ("@id", leaving));
                    await undo.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            return (sub, values, deletion);
        }, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.Subcategory, AuditActions.DeleteSubcategoryParameter,
            subcategoryId.ToString(CultureInfo.InvariantCulture), Target(original.Category, original.Subcategory),
            AuditDetails.Identification(("Parametru", original.Name), ("Valori șterse", valueCount.ToString(CultureInfo.InvariantCulture)), ("Produse actualizate", removal.Affected.Count.ToString(CultureInfo.InvariantCulture))), motif, cancellationToken).ConfigureAwait(false);
        foreach (var product in removal.Affected)
            await AuditRecorder.RecordEditAsync(auditTrail, accessControl, AuditEntities.Product, product.ProductId.ToString(CultureInfo.InvariantCulture),
                product.NewName, [new(ProductCode.Label, product.OldName, product.NewName)], motif, cancellationToken, AuditActions.RenameProductByParameter).ConfigureAwait(false);
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
                if (parameter.Kind == ParameterKind.Fixed) throw new ProductOperationException(ProductParameterRules.FixedValueMessage);
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
            if (row.Kind == ParameterKind.Fixed) throw new ProductOperationException(ProductParameterRules.FixedValueMessage);
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

    // ---- families of parameters ---------------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<ParameterFamily>> GetFamiliesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, null, """
            SELECT f.id,c.name,s.name,f.name,f.position,f.version,sp.id
            FROM parameter_families f
            INNER JOIN subcategories s ON s.id=f.subcategory_id INNER JOIN categories c ON c.id=s.category_id
            LEFT JOIN subcategory_parameters sp ON sp.family_id=f.id
            ORDER BY c.name,s.name,f.position,f.id,sp.position,sp.id
            """);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<ParameterFamily>();
        ParameterFamily? current = null;
        var members = new List<int>();
        void Close() { if (current is not null) result.Add(current with { ParameterIds = members.ToList() }); members.Clear(); }
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var id = checked((int)reader.GetInt64(0));
            if (current is null || current.Id != id)
            {
                Close();
                current = new ParameterFamily(id, reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt32(4), reader.GetInt64(5), []);
            }
            if (!reader.IsDBNull(6)) members.Add(checked((int)reader.GetInt64(6)));
        }
        Close();
        return result;
    }

    private sealed record FamilyMember(int Id, string Name, int? FamilyId, int SubcategoryId);

    private static (string Sql, (string, object?)[] Parameters) InList(IReadOnlyList<int> ids, string prefix)
    {
        var names = ids.Select((_, index) => $"@{prefix}{index}").ToArray();
        return (string.Join(",", names), ids.Select((id, index) => ($"@{prefix}{index}", (object?)id)).ToArray());
    }

    private static async Task<List<FamilyMember>> LockMembersAsync(MySqlConnection connection, MySqlTransaction transaction, IReadOnlyList<int> ids, CancellationToken token)
    {
        var (list, parameters) = InList(ids, "m");
        var members = new List<FamilyMember>();
        await using var command = Command(connection, transaction, $"SELECT id,name,family_id,subcategory_id FROM subcategory_parameters WHERE id IN ({list}) ORDER BY position,id FOR UPDATE", parameters);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
            members.Add(new FamilyMember(checked((int)reader.GetInt64(0)), reader.GetString(1), reader.IsDBNull(2) ? null : checked((int)reader.GetInt64(2)), checked((int)reader.GetInt64(3))));
        return members;
    }

    // Products of the subcategory that already have values for two or more of the members (a family allows one): the grouping is refused until they are corrected.
    private static async Task<List<string>> ConflictingProductsAsync(MySqlConnection connection, MySqlTransaction transaction, IReadOnlyList<int> memberIds, CancellationToken token)
    {
        var (list, parameters) = InList(memberIds, "c");
        var names = new List<string>();
        await using var command = Command(connection, transaction, $"""
            SELECT p.name FROM products p INNER JOIN product_parameter_values pv ON pv.product_id=p.id
            WHERE pv.parameter_id IN ({list}) GROUP BY p.id,p.name HAVING COUNT(*)>1 ORDER BY p.name LIMIT 10
            """, parameters);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false)) names.Add(reader.GetString(0));
        return names;
    }

    // Products of the subcategory with no value for any of the members: they become incomplete when the members stand alone or are grouped differently.
    private static async Task<int> ProductsWithoutAnyAsync(MySqlConnection connection, MySqlTransaction transaction, int subcategoryId, IReadOnlyList<int> memberIds, CancellationToken token)
    {
        var (list, parameters) = InList(memberIds, "w");
        await using var command = Command(connection, transaction, $"""
            SELECT COUNT(*) FROM products p WHERE p.subcategory_id=@sub
            AND NOT EXISTS(SELECT 1 FROM product_parameter_values pv WHERE pv.product_id=p.id AND pv.parameter_id IN ({list}))
            """, [("@sub", subcategoryId), .. parameters]);
        return Convert.ToInt32(await command.ExecuteScalarAsync(token).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    // A fixed parameter works only inside a family: while products use it, it cannot be taken out of its family (its value would drop out of their codes).
    private static async Task EnsureFixedUnusedAsync(MySqlConnection connection, MySqlTransaction transaction, IReadOnlyList<int> ids, CancellationToken token)
    {
        if (ids.Count == 0) return;
        var (list, parameters) = InList(ids, "x");
        await using var command = Command(connection, transaction, $"""
            SELECT sp.name FROM subcategory_parameters sp WHERE sp.id IN ({list}) AND sp.kind=@fixed
            AND EXISTS(SELECT 1 FROM product_parameter_values pv WHERE pv.parameter_id=sp.id) LIMIT 1
            """, [("@fixed", (int)ParameterKind.Fixed), .. parameters]);
        if (await command.ExecuteScalarAsync(token).ConfigureAwait(false) is string name)
            throw new ProductOperationException(string.Format(CultureInfo.InvariantCulture, ProductParameterRules.FixedLeaveFamilyMessage, name));
    }

    private static string MemberNames(IEnumerable<FamilyMember> members) => string.Join(", ", members.Select(member => member.Name));

    public async Task<ParameterFamily> CreateFamilyAsync(ProductGroup group, string name, IReadOnlyList<int> parameterIds, CancellationToken cancellationToken = default)
    {
        await EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var cleanName = ProductParameterRules.FamilyName(name);
        var ids = parameterIds.Distinct().ToList();
        if (ids.Count < ProductParameterRules.FamilyMinimumMembers) throw new ProductOperationException(ProductParameterRules.FamilyTooSmallMessage);
        (ParameterFamily Family, int SubcategoryId, string Members, int Incomplete) result;
        try
        {
            result = await WriteAsync(async (connection, transaction) =>
            {
                var subcategoryId = await FindSubcategoryAsync(connection, transaction, group, cancellationToken).ConfigureAwait(false);
                var members = await LockMembersAsync(connection, transaction, ids, cancellationToken).ConfigureAwait(false);
                if (members.Count != ids.Count || members.Any(member => member.SubcategoryId != subcategoryId))
                    throw new ProductOperationException("Un parametru ales nu mai există sau nu aparține subcategoriei. Actualizează lista și reia operația.");
                if (members.FirstOrDefault(member => member.FamilyId is not null) is { } taken)
                    throw new ProductOperationException($"Parametrul «{taken.Name}» face deja parte dintr-o familie. Scoate-l mai întâi din ea.");
                await using (var exists = Command(connection, transaction, "SELECT 1 FROM parameter_families WHERE subcategory_id=@sub AND normalized_name=@key",
                                 ("@sub", subcategoryId), ("@key", TextNormalization.UniquenessKey(cleanName))))
                    if (await exists.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null)
                        throw new ProductOperationException($"Subcategoria «{group.Subcategory}» are deja o familie «{cleanName}».");
                if (await ConflictingProductsAsync(connection, transaction, ids, cancellationToken).ConfigureAwait(false) is { Count: > 0 } conflicts)
                    throw new ProductOperationException(ProductParameterRules.FamilyConflictMessage(cleanName, conflicts));
                var incomplete = await ProductsWithoutAnyAsync(connection, transaction, subcategoryId, ids, cancellationToken).ConfigureAwait(false);
                await using var position = Command(connection, transaction, "SELECT COALESCE(MAX(position),0)+1 FROM parameter_families WHERE subcategory_id=@sub", ("@sub", subcategoryId));
                var next = Convert.ToInt32(await position.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
                await using var insert = Command(connection, transaction, "INSERT INTO parameter_families(subcategory_id,name,normalized_name,position,version) VALUES(@sub,@name,@key,@position,0)",
                    ("@sub", subcategoryId), ("@name", cleanName), ("@key", TextNormalization.UniquenessKey(cleanName)), ("@position", next));
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                var familyId = checked((int)insert.LastInsertedId);
                var (list, parameters) = InList(ids, "u");
                await using var update = Command(connection, transaction, $"UPDATE subcategory_parameters SET family_id=@family,version=version+1 WHERE id IN ({list})", [("@family", familyId), .. parameters]);
                await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                return (new ParameterFamily(familyId, group.Category, group.Subcategory, cleanName, next, 0, members.Select(member => member.Id).ToList()), subcategoryId, MemberNames(members), incomplete);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (MySqlException exception) when (exception.Number == 1062)
        {
            throw new ProductOperationException($"Subcategoria «{group.Subcategory}» are deja o familie «{cleanName}».");
        }
        await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.Subcategory, AuditActions.AddParameterFamily,
            result.SubcategoryId.ToString(CultureInfo.InvariantCulture), Target(group), AuditDetails.Identification(
                ("Familie", cleanName), ("Parametri", result.Members), ("Produse devenite incomplete", result.Incomplete.ToString(CultureInfo.InvariantCulture))), string.Empty, cancellationToken).ConfigureAwait(false);
        return result.Family;
    }

    public async Task<ParameterFamily> UpdateFamilyAsync(ParameterFamily original, string name, IReadOnlyList<int> parameterIds, string reason, CancellationToken cancellationToken = default)
    {
        await EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var cleanName = ProductParameterRules.FamilyName(name);
        var motif = ProductGroupManagementRules.Reason(reason);
        var ids = parameterIds.Distinct().ToList();
        if (ids.Count < ProductParameterRules.FamilyMinimumMembers) throw new ProductOperationException(ProductParameterRules.FamilyTooSmallMessage);
        (ParameterFamily Family, int SubcategoryId, string Before, string After, int Incomplete) result;
        try
        {
            result = await WriteAsync(async (connection, transaction) =>
            {
                int subcategoryId; long version; string currentName;
                await using (var lockFamily = Command(connection, transaction, "SELECT subcategory_id,version,name FROM parameter_families WHERE id=@id FOR UPDATE", ("@id", original.Id)))
                await using (var reader = await lockFamily.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) throw new ProductOperationException("Familia nu mai există. Actualizează lista și reia operația.");
                    (subcategoryId, version, currentName) = (checked((int)reader.GetInt64(0)), reader.GetInt64(1), reader.GetString(2));
                }
                if (version != original.Version) throw new ProductOperationException("Familia a fost modificată între timp. Actualizează lista și reia operația.");
                var current = new List<FamilyMember>();
                await using (var currentMembers = Command(connection, transaction, "SELECT id,name,family_id,subcategory_id FROM subcategory_parameters WHERE family_id=@id ORDER BY position,id FOR UPDATE", ("@id", original.Id)))
                await using (var reader = await currentMembers.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                        current.Add(new FamilyMember(checked((int)reader.GetInt64(0)), reader.GetString(1), original.Id, subcategoryId));
                var chosen = await LockMembersAsync(connection, transaction, ids, cancellationToken).ConfigureAwait(false);
                if (chosen.Count != ids.Count || chosen.Any(member => member.SubcategoryId != subcategoryId))
                    throw new ProductOperationException("Un parametru ales nu mai există sau nu aparține subcategoriei. Actualizează lista și reia operația.");
                if (chosen.FirstOrDefault(member => member.FamilyId is not null && member.FamilyId != original.Id) is { } taken)
                    throw new ProductOperationException($"Parametrul «{taken.Name}» face deja parte din altă familie. Scoate-l mai întâi din ea.");
                await using (var exists = Command(connection, transaction, "SELECT 1 FROM parameter_families WHERE subcategory_id=@sub AND normalized_name=@key AND id<>@id",
                                 ("@sub", subcategoryId), ("@key", TextNormalization.UniquenessKey(cleanName)), ("@id", original.Id)))
                    if (await exists.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null)
                        throw new ProductOperationException($"Subcategoria «{original.Subcategory}» are deja o familie «{cleanName}».");
                if (await ConflictingProductsAsync(connection, transaction, ids, cancellationToken).ConfigureAwait(false) is { Count: > 0 } conflicts)
                    throw new ProductOperationException(ProductParameterRules.FamilyConflictMessage(cleanName, conflicts));
                if (current.Select(member => member.Id).Order().SequenceEqual(ids.Order()) && string.Equals(currentName, cleanName, StringComparison.Ordinal))
                    throw new ProductOperationException("Nu ai modificat nimic.");
                var removed = current.Where(member => !ids.Contains(member.Id)).Select(member => member.Id).ToList();
                if (removed.Count > 0)
                {
                    await EnsureFixedUnusedAsync(connection, transaction, removed, cancellationToken).ConfigureAwait(false);
                    var (list, parameters) = InList(removed, "r");
                    await using var release = Command(connection, transaction, $"UPDATE subcategory_parameters SET family_id=NULL,version=version+1 WHERE id IN ({list})", parameters);
                    await release.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
                var added = ids.Where(id => current.All(member => member.Id != id)).ToList();
                if (added.Count > 0)
                {
                    var (list, parameters) = InList(added, "a");
                    await using var attach = Command(connection, transaction, $"UPDATE subcategory_parameters SET family_id=@family,version=version+1 WHERE id IN ({list})", [("@family", original.Id), .. parameters]);
                    await attach.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
                await using var rename = Command(connection, transaction, "UPDATE parameter_families SET name=@name,normalized_name=@key,version=version+1 WHERE id=@id",
                    ("@name", cleanName), ("@key", TextNormalization.UniquenessKey(cleanName)), ("@id", original.Id));
                await rename.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                // Products that had none of the new family members, or that now miss a parameter that left the family, are incomplete.
                var incomplete = await ProductsWithoutAnyAsync(connection, transaction, subcategoryId, ids, cancellationToken).ConfigureAwait(false);
                return (original with { Name = cleanName, Version = original.Version + 1, ParameterIds = ids }, subcategoryId, MemberNames(current), MemberNames(chosen), incomplete);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (MySqlException exception) when (exception.Number == 1062)
        {
            throw new ProductOperationException($"Subcategoria «{original.Subcategory}» are deja o familie «{cleanName}».");
        }
        await AuditRecorder.RecordEditAsync(auditTrail, accessControl, AuditEntities.Subcategory, result.SubcategoryId.ToString(CultureInfo.InvariantCulture),
            Target(original.Category, original.Subcategory), [new("Denumire familie", original.Name, cleanName), new("Parametri", result.Before, result.After),
                new("Produse incomplete după modificare", string.Empty, result.Incomplete.ToString(CultureInfo.InvariantCulture))],
            motif, cancellationToken, AuditActions.EditParameterFamily).ConfigureAwait(false);
        return result.Family;
    }

    public async Task DeleteFamilyAsync(ParameterFamily original, string reason, CancellationToken cancellationToken = default)
    {
        await EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var motif = ProductGroupManagementRules.Reason(reason);
        var (subcategoryId, members, incomplete) = await WriteAsync(async (connection, transaction) =>
        {
            int sub; long version;
            await using (var lockFamily = Command(connection, transaction, "SELECT subcategory_id,version FROM parameter_families WHERE id=@id FOR UPDATE", ("@id", original.Id)))
            await using (var reader = await lockFamily.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) throw new ProductOperationException("Familia nu mai există. Actualizează lista și reia operația.");
                (sub, version) = (checked((int)reader.GetInt64(0)), reader.GetInt64(1));
            }
            if (version != original.Version) throw new ProductOperationException("Familia a fost modificată între timp. Actualizează lista și reia operația.");
            var current = new List<FamilyMember>();
            await using (var select = Command(connection, transaction, "SELECT id,name FROM subcategory_parameters WHERE family_id=@id ORDER BY position,id FOR UPDATE", ("@id", original.Id)))
            await using (var reader = await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) current.Add(new FamilyMember(checked((int)reader.GetInt64(0)), reader.GetString(1), original.Id, sub));
            // After ungrouping every member is required on its own: the products that miss at least one of them are incomplete.
            var missing = 0;
            if (current.Count > 0)
            {
                await EnsureFixedUnusedAsync(connection, transaction, current.Select(member => member.Id).ToList(), cancellationToken).ConfigureAwait(false);
                var (list, parameters) = InList(current.Select(member => member.Id).ToList(), "d");
                await using var count = Command(connection, transaction, $"""
                    SELECT COUNT(*) FROM products p WHERE p.subcategory_id=@sub AND EXISTS(
                        SELECT 1 FROM subcategory_parameters sp WHERE sp.id IN ({list}) AND sp.kind<>4
                        AND NOT EXISTS(SELECT 1 FROM product_parameter_values pv WHERE pv.product_id=p.id AND pv.parameter_id=sp.id))
                    """, [("@sub", sub), .. parameters]);
                missing = Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
            }
            await using (var release = Command(connection, transaction, "UPDATE subcategory_parameters SET family_id=NULL,version=version+1 WHERE family_id=@id", ("@id", original.Id)))
                await release.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await using var delete = Command(connection, transaction, "DELETE FROM parameter_families WHERE id=@id", ("@id", original.Id));
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return (sub, MemberNames(current), missing);
        }, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.Subcategory, AuditActions.DeleteParameterFamily,
            subcategoryId.ToString(CultureInfo.InvariantCulture), Target(original.Category, original.Subcategory),
            AuditDetails.Identification(("Familie", original.Name), ("Parametri desfăcuți", members), ("Produse devenite incomplete", incomplete.ToString(CultureInfo.InvariantCulture))), motif, cancellationToken).ConfigureAwait(false);
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
            SELECT p.id FROM products p WHERE (
                EXISTS(SELECT 1 FROM subcategory_parameters sp WHERE sp.subcategory_id=p.subcategory_id AND sp.family_id IS NULL AND sp.kind<>4
                       AND NOT EXISTS(SELECT 1 FROM product_parameter_values pv WHERE pv.product_id=p.id AND pv.parameter_id=sp.id))
                OR EXISTS(SELECT 1 FROM parameter_families f WHERE f.subcategory_id=p.subcategory_id
                       AND (SELECT COUNT(*) FROM product_parameter_values pv INNER JOIN subcategory_parameters m ON m.id=pv.parameter_id
                            WHERE pv.product_id=p.id AND m.family_id=f.id)<>1))
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

    public async Task<IReadOnlyDictionary<int, IReadOnlyList<ProductParameterChoice>>> GetAllChoicesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, null, "SELECT product_id,parameter_id,value_id FROM product_parameter_values");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var byProduct = new Dictionary<int, List<ProductParameterChoice>>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var productId = checked((int)reader.GetInt64(0));
            if (!byProduct.TryGetValue(productId, out var list)) byProduct[productId] = list = [];
            list.Add(new ProductParameterChoice(checked((int)reader.GetInt64(1)), checked((int)reader.GetInt64(2))));
        }
        return byProduct.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<ProductParameterChoice>)pair.Value);
    }

    // ---- helpers ------------------------------------------------------------------------------------------------------------

    private sealed record LockedParameter(string Name, string Unit, ParameterKind Kind, long Version, int ProductCount, string Target);
    private sealed record ValueRow(string Value, int ParameterId, string ParameterName, string Unit, ParameterKind Kind, int SubcategoryId, string Target);
    private sealed record ComputedChange(ParameterValueChange Change, int SubcategoryId, string Target, string ParameterName, string Unit);

    private static string Target(ProductGroup group) => Target(group.Category, group.Subcategory);
    private static string Target(string category, string subcategory) => $"{category} / {subcategory}";
    private static string KindLabel(ParameterKind kind) => ProductParameterRules.KindLabel(kind);

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
        if (row.Kind == ParameterKind.Fixed) throw new ProductOperationException(ProductParameterRules.FixedValueMessage);
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
                WHERE pv.product_id=@id
                ORDER BY COALESCE((SELECT MIN(m.position) FROM subcategory_parameters m WHERE m.family_id=sp.family_id),sp.position),sp.position,sp.id
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

    // Parameters and their values belong to editing the categories.
    private Task EnsureOperatorAsync(CancellationToken token) => accessControl?.EnsureAsync("categorii.edit", token) ?? Task.CompletedTask;
    private Task EnsureAdministratorAsync(CancellationToken token) => accessControl?.EnsureAdministratorAsync(token) ?? Task.CompletedTask;
}
