using MySqlConnector;
using static BlazorStoc.Services.MariaDb;

namespace BlazorStoc.Services;

// The required parameters of the subcategory, applied inside the transaction that creates or updates a product.
public sealed partial class MariaProductRepository
{
    private sealed record ParameterRow(int ParameterId, int ValueId, string Name, string Display);

    private sealed record ParameterResolution(string Name, string? BaseModel, IReadOnlyList<ParameterRow> Rows)
    {
        public string Summary => ProductParameterRules.Summary(Rows.Select(row => (row.Name, row.Display)));
    }

    // Without parameters on the subcategory the name stays what the user typed; with them the name is composed from the model and the values.
    private static async Task<ParameterResolution> ResolveParametersAsync(MySqlConnection connection, MySqlTransaction transaction,
        int categoryId, int subcategoryId, string subcategory, ProductInput value, CancellationToken token)
    {
        // The parameters and families of the category (migration 56), of the subcategory and of every ancestor (migration 54): the category's come first.
        var chainList = SubcategoryAncestry.InList(await SubcategoryAncestry.ChainAsync(connection, transaction, subcategoryId, token).ConfigureAwait(false));
        var definitions = new List<(int Id, string Name, string Unit, int? FamilyId, int Position, ParameterKind Kind)>();
        await using (var select = Command(connection, transaction,
            $"SELECT id,name,unit,family_id,position,kind FROM subcategory_parameters WHERE {DefinitionOwners.ChainSql(chainList)} AND archived_utc IS NULL ORDER BY (category_id IS NULL),FIELD(subcategory_id,{chainList}),position,id", ("@chainCategory", categoryId)))
        await using (var reader = await select.ExecuteReaderAsync(token).ConfigureAwait(false))
            while (await reader.ReadAsync(token).ConfigureAwait(false))
                definitions.Add((checked((int)reader.GetInt64(0)), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : checked((int)reader.GetInt64(3)), reader.GetInt32(4), (ParameterKind)reader.GetInt32(5)));
        if (definitions.Count == 0) return new ParameterResolution(value.Name, null, []);
        var families = new List<ParameterFamily>();
        await using (var selectFamilies = Command(connection, transaction, $"SELECT id,name FROM parameter_families WHERE {DefinitionOwners.ChainSql(chainList)} AND archived_utc IS NULL", ("@chainCategory", categoryId)))
        await using (var reader = await selectFamilies.ExecuteReaderAsync(token).ConfigureAwait(false))
            while (await reader.ReadAsync(token).ConfigureAwait(false))
                families.Add(new ParameterFamily(checked((int)reader.GetInt64(0)), "", subcategory, reader.GetString(1), 0, 0, []));

        // The position is the place in the chain (ancestors first, then the own parameters). Every parameter needs a value and every family exactly one of its members; the value of a family goes at the place of its first member.
        var slots = ProductParameterRules.Slots(definitions.Select((item, index) => new SubcategoryParameter(item.Id, "", subcategory, item.Name, item.Unit, item.Kind, index, 0, [], item.FamilyId)), families);
        if (slots.Count == 0) return new ParameterResolution(value.Name, null, []);
        var chosenIds = value.Parameters.Select(item => item.ParameterId).Where(id => definitions.Any(item => item.Id == id)).ToHashSet();
        var baseModel = value.BaseModel.Length == 0
            ? throw new ProductOperationException(ProductParameterRules.RequiredMessage(subcategory, slots.Select(slot => slot.Name)))
            : ProductParameterRules.BaseModel(value.BaseModel);
        if (ProductParameterRules.Problems(slots, chosenIds) is { Count: > 0 } problems)
            throw new ProductOperationException(ProductParameterRules.RequiredMessage(subcategory, problems));
        var rows = new List<ParameterRow>();
        foreach (var parameter in ProductParameterRules.CodeOrder(slots, chosenIds))
        {
            var (id, name) = (parameter.Id, parameter.Name);
            var unit = definitions.First(item => item.Id == id).Unit;
            var choice = value.Parameters.First(item => item.ParameterId == id);
            await using var selectValue = Command(connection, transaction,
                "SELECT value FROM parameter_values WHERE id=@value AND parameter_id=@parameter", ("@value", choice.ValueId), ("@parameter", id));
            var text = await selectValue.ExecuteScalarAsync(token).ConfigureAwait(false) as string
                ?? throw new ProductOperationException($"Valoarea aleasă pentru «{name}» nu mai există. Actualizează lista și reia salvarea.");
            rows.Add(new ParameterRow(id, choice.ValueId, name, ProductParameterRules.Display(unit, text)));
        }
        var composed = ProductParameterRules.CheckNameLength(ProductParameterRules.ComposeName(baseModel, rows.Select(row => row.Display)));
        return new ParameterResolution(composed, baseModel, rows);
    }

    private static async Task StoreParametersAsync(MySqlConnection connection, MySqlTransaction transaction, int productId,
        ParameterResolution resolution, string actor, CancellationToken token)
    {
        // The links of archived parameters stay: they hold the value baked into the model of the product (migration 56).
        var before = new HashSet<(int Parameter, int Value)>();
        await using (var existing = Command(connection, transaction, "SELECT parameter_id,value_id FROM product_parameter_values WHERE product_id=@id AND archived=0", ("@id", productId)))
        await using (var reader = await existing.ExecuteReaderAsync(token).ConfigureAwait(false))
            while (await reader.ReadAsync(token).ConfigureAwait(false)) before.Add((checked((int)reader.GetInt64(0)), checked((int)reader.GetInt64(1))));
        await using (var clear = Command(connection, transaction, "DELETE FROM product_parameter_values WHERE product_id=@id AND archived=0", ("@id", productId)))
            await clear.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        await using (var model = Command(connection, transaction, "UPDATE products SET base_model=@model WHERE id=@id",
                         ("@model", resolution.BaseModel is null ? DBNull.Value : resolution.BaseModel), ("@id", productId)))
            await model.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        foreach (var row in resolution.Rows)
        {
            await using var insert = Command(connection, transaction,
                "INSERT INTO product_parameter_values(product_id,parameter_id,value_id) VALUES(@product,@parameter,@value)",
                ("@product", productId), ("@parameter", row.ParameterId), ("@value", row.ValueId));
            await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }
        // The usage log keeps the links that appeared and disappeared with this save (an unchanged link writes nothing).
        var after = resolution.Rows.Select(row => (row.ParameterId, row.ValueId)).ToHashSet();
        foreach (var (parameter, value) in before.Where(link => !after.Contains(link)))
            await ParameterUsageLog.WriteLinkAsync(connection, transaction, productId, parameter, value, ParameterUsageActions.Removed, actor, token).ConfigureAwait(false);
        foreach (var (parameter, value) in after.Where(link => !before.Contains(link)))
            await ParameterUsageLog.WriteLinkAsync(connection, transaction, productId, parameter, value, ParameterUsageActions.Applied, actor, token).ConfigureAwait(false);
    }

    // "Lentilă: 2.8 mm; Culoare: alb" - what the product has now, for the journal.
    private static async Task<string> ParameterSummaryAsync(MySqlConnection connection, MySqlTransaction transaction, int productId, CancellationToken token)
    {
        await using var command = Command(connection, transaction, """
            SELECT sp.name,sp.unit,v.value FROM product_parameter_values pv
            INNER JOIN subcategory_parameters sp ON sp.id=pv.parameter_id
            INNER JOIN parameter_values v ON v.id=pv.value_id
            WHERE pv.product_id=@id AND pv.archived=0 ORDER BY sp.position,sp.id
            """, ("@id", productId));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        var values = new List<(string, string)>();
        while (await reader.ReadAsync(token).ConfigureAwait(false))
            values.Add((reader.GetString(0), ProductParameterRules.Display(reader.GetString(1), reader.GetString(2))));
        return ProductParameterRules.Summary(values);
    }
}
