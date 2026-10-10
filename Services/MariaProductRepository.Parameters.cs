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
        int subcategoryId, string subcategory, ProductInput value, CancellationToken token)
    {
        var definitions = new List<(int Id, string Name, string Unit)>();
        await using (var select = Command(connection, transaction,
            "SELECT id,name,unit FROM subcategory_parameters WHERE subcategory_id=@sub ORDER BY position,id", ("@sub", subcategoryId)))
        await using (var reader = await select.ExecuteReaderAsync(token).ConfigureAwait(false))
            while (await reader.ReadAsync(token).ConfigureAwait(false))
                definitions.Add((checked((int)reader.GetInt64(0)), reader.GetString(1), reader.GetString(2)));
        if (definitions.Count == 0) return new ParameterResolution(value.Name, null, []);

        var baseModel = value.BaseModel.Length == 0
            ? throw new ProductOperationException(ProductParameterRules.RequiredMessage(subcategory, definitions.Select(item => item.Name)))
            : ProductParameterRules.BaseModel(value.BaseModel);
        var rows = new List<ParameterRow>();
        foreach (var (id, name, unit) in definitions)
        {
            var choice = value.Parameters.FirstOrDefault(item => item.ParameterId == id)
                ?? throw new ProductOperationException(ProductParameterRules.RequiredMessage(subcategory, definitions.Select(item => item.Name)));
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
        ParameterResolution resolution, CancellationToken token)
    {
        await using (var clear = Command(connection, transaction, "DELETE FROM product_parameter_values WHERE product_id=@id", ("@id", productId)))
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
    }

    // "Lentilă: 2.8 mm; Culoare: alb" - what the product has now, for the journal.
    private static async Task<string> ParameterSummaryAsync(MySqlConnection connection, MySqlTransaction transaction, int productId, CancellationToken token)
    {
        await using var command = Command(connection, transaction, """
            SELECT sp.name,sp.unit,v.value FROM product_parameter_values pv
            INNER JOIN subcategory_parameters sp ON sp.id=pv.parameter_id
            INNER JOIN parameter_values v ON v.id=pv.value_id
            WHERE pv.product_id=@id ORDER BY sp.position,sp.id
            """, ("@id", productId));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        var values = new List<(string, string)>();
        while (await reader.ReadAsync(token).ConfigureAwait(false))
            values.Add((reader.GetString(0), ProductParameterRules.Display(reader.GetString(1), reader.GetString(2))));
        return ProductParameterRules.Summary(values);
    }
}
