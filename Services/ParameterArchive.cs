using System.Globalization;
using MySqlConnector;
using static BlazorStoc.Services.MariaDb;

namespace BlazorStoc.Services;

// Archiving of parameters and families (migration 56). A definition that products use is never deleted: it is archived. Archiving a parameter moves the text
// of its value into the model of every product that carries it (the product is renamed: "<model> - <value>" becomes the new model, the link is kept with
// archived=1 and baked_text=<value>), so the code of the product still says what it was. Restoring reverses it: the text leaves the model (when it is still
// at the end of it) and the parameter is required again. A definition no product links to any more can be deleted for good; one that products still link
// to can only have the restore given up after the retention period, which leaves the products named as they are.
public sealed record ArchivedParameter(int Id, string Category, string Subcategory, string Name, string Unit, ParameterKind Kind, long Version, int? FamilyId,
    string? FamilyName, string ArchivedUtc, string ArchivedBy, int ProductCount, IReadOnlyList<string> Values)
{
    // No product links to it any more: it can be deleted for good.
    public bool Unused => ProductCount == 0;
}

public sealed record ArchivedFamily(int Id, string Category, string Subcategory, string Name, long Version, string ArchivedUtc, string ArchivedBy,
    IReadOnlyList<int> ParameterIds, int ProductCount)
{
    public bool Unused => ProductCount == 0;
}

public sealed record ArchivedParameters(IReadOnlyList<ArchivedParameter> Parameters, IReadOnlyList<ArchivedFamily> Families);

// What archiving does: the products renamed (the value moves into their model) and the new names that would collide (then nothing is archived).
// Unused = no product has a value of it: the choice is between archiving anyway and deleting it for good.
public sealed record ParameterArchivePreview(IReadOnlyList<ParameterAffectedProduct> Affected, IReadOnlyList<string> Conflicts)
{
    public bool Unused => Affected.Count == 0;
    public bool CanApply => Conflicts.Count == 0;
}

public enum RestoreProblem
{
    None = 0,
    // The text baked into the model is no longer at the end of it (the model was edited since): the model is kept, the value is added again.
    BakedTextMissing = 1,
    // The name the product would get belongs to another product.
    NameClash = 2,
    // The name would be longer than a product name can be.
    NameTooLong = 3,
    // The product left the subcategory the parameter belongs to: only giving up the restore removes its link.
    OutOfScope = 4
}

// One product a restore touches. ProposedBaseModel and NewName are the automatic proposal (already free of clashes); Problem says why the product needs a decision.
public sealed record ParameterRestoreItem(int ProductId, string OldName, string BaseModel, string ProposedBaseModel, string NewName, RestoreProblem Problem);

public sealed record ParameterRestorePreview(IReadOnlyList<ParameterRestoreItem> Items)
{
    public IReadOnlyList<ParameterRestoreItem> Problems => Items.Where(item => item.Problem != RestoreProblem.None).ToList();
    public bool Blocked => Items.Any(item => item.Problem == RestoreProblem.OutOfScope);
    public bool CanApplyAsIs => Items.All(item => item.Problem == RestoreProblem.None);
}

// One row of "where it was used": kept without foreign keys, so it survives the deletion of the products and of the definition.
public sealed record ParameterUsageEntry(long Id, int ParameterId, string ParameterName, string Value, int? ProductId, string ProductName, string Node,
    string Action, string Actor, string Reason, string OccurredUtc);

public static class ParameterUsageActions
{
    public const string Applied = "aplicare";
    public const string Removed = "eliminare";
    public const string Archived = "arhivare";
    public const string Restored = "restaurare";
    public const string GivenUp = "renuntare";
    public const string ProductDeleted = "stergere produs";
    public const string Deleted = "stergere definitie";
    public const string Merged = "imbinare";
    public const string Relocated = "mutare";
}

public static class ParameterArchiveRules
{
    public const int MinRetentionDays = 30;
    public const int MaxRetentionDays = 360;
    public const int DefaultRetentionDays = 30;
    public static readonly string RetentionRangeMessage = $"Perioada de păstrare trebuie să fie între {MinRetentionDays} și {MaxRetentionDays} de zile.";
    public static bool ValidRetention(int days) => days is >= MinRetentionDays and <= MaxRetentionDays;

    public const string FamilyMemberMessage = "Parametrul face parte dintr-o familie. Arhivează familia întreagă sau scoate parametrul din familie.";
    public const string UsedCannotDeleteMessage = "Definiția este folosită de produse și nu se mai șterge. Arhiveaz-o: produsele își păstrează codul.";
    public const string AlreadyArchivedMessage = "Definiția este deja arhivată. Actualizează lista și reia operația.";
    public const string NotArchivedMessage = "Definiția nu este arhivată. Actualizează lista și reia operația.";
    public const string StillLinkedMessage = "Definiția mai are produse legate. Restaureaz-o sau așteaptă perioada de păstrare ca să renunți la restaurare.";

    // The first day the restore may be given up for good (the archive date plus the retention period).
    public static DateTime GiveUpAllowedFrom(DateTime archivedUtc, int retentionDays) => archivedUtc.AddDays(retentionDays);

    public static string GiveUpTooEarlyMessage(DateTime allowedFrom) =>
        $"Renunțarea la restaurare este permisă abia din {allowedFrom.ToLocalTime():dd.MM.yyyy}, după perioada de păstrare setată în Catalog.";

    // The new name of a product whose model changes: model first, then the chosen values.
    public static string NewName(string baseModel, IEnumerable<string> displays) => ProductParameterRules.ComposeName(baseModel, displays);

    // The model without the text baked into it, or null when the text is not at its end.
    public static string? StripBaked(string baseModel, string bakedText)
    {
        var tail = ProductParameterRules.Separator + bakedText;
        return baseModel.EndsWith(tail, StringComparison.Ordinal) && baseModel.Length > tail.Length ? baseModel[..^tail.Length] : null;
    }

    // The model with the text of a value baked into it.
    public static string Bake(string baseModel, string display) => baseModel + ProductParameterRules.Separator + display;
}

// The usage log (table parameter_usage_log): written inside the transaction of the change it describes.
internal static class ParameterUsageLog
{
    public static async Task WriteAsync(MySqlConnection connection, MySqlTransaction transaction, int parameterId, string parameterName, string value,
        int? productId, string productName, string node, string action, string actor, string reason, CancellationToken token)
    {
        await using var insert = Command(connection, transaction, """
            INSERT INTO parameter_usage_log(definition_kind,definition_id,parameter_name,value_text,product_id,product_name,node_path,action,actor,reason,occurred_utc)
            VALUES('parameter',@id,@name,@value,@product,@productName,@node,@action,@actor,@reason,@now)
            """, ("@id", parameterId), ("@name", parameterName), ("@value", value), ("@product", productId is null ? DBNull.Value : productId),
            ("@productName", productName), ("@node", node), ("@action", action), ("@actor", actor), ("@reason", Cut(reason, 500)), ("@now", MariaTimeText.Format(DateTime.UtcNow)));
        await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    // One link of a product to a value (applied or removed when the product is saved).
    public static async Task WriteLinkAsync(MySqlConnection connection, MySqlTransaction transaction, int productId, int parameterId, int valueId, string action, string actor, CancellationToken token)
    {
        await using var insert = Command(connection, transaction, """
            INSERT INTO parameter_usage_log(definition_kind,definition_id,parameter_name,value_text,product_id,product_name,node_path,action,actor,reason,occurred_utc)
            SELECT 'parameter',sp.id,sp.name,v.value,p.id,p.name,CASE WHEN s.id IS NULL THEN c.name ELSE CONCAT(c.name,' / ',s.name) END,@action,@actor,'',@now
            FROM subcategory_parameters sp INNER JOIN parameter_values v ON v.id=@value INNER JOIN products p ON p.id=@product
            LEFT JOIN subcategories s ON s.id=sp.subcategory_id INNER JOIN categories c ON c.id=COALESCE(s.category_id,sp.category_id)
            WHERE sp.id=@parameter
            """, ("@product", productId), ("@parameter", parameterId), ("@value", valueId), ("@action", action), ("@actor", actor), ("@now", MariaTimeText.Format(DateTime.UtcNow)));
        await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    // The products linked to a parameter that moves to another owner (absorbed = survivor) or is merged into another one: the new place goes to the log.
    public static async Task WriteRelocationAsync(MySqlConnection connection, MySqlTransaction transaction, int absorbedId, int survivorId, string action, string actor, string reason, CancellationToken token,
        IReadOnlyList<int>? subtree = null)
    {
        await using var insert = Command(connection, transaction, $"""
            INSERT INTO parameter_usage_log(definition_kind,definition_id,parameter_name,value_text,product_id,product_name,node_path,action,actor,reason,occurred_utc)
            SELECT 'parameter',sv.id,sv.name,CASE WHEN pv.archived=1 THEN COALESCE(pv.baked_text,v.value) ELSE v.value END,p.id,p.name,
                   CASE WHEN s.id IS NULL THEN c.name ELSE CONCAT(c.name,' / ',s.name) END,@action,@actor,@reason,@now
            FROM product_parameter_values pv
            INNER JOIN products p ON p.id=pv.product_id INNER JOIN parameter_values v ON v.id=pv.value_id
            INNER JOIN subcategory_parameters sv ON sv.id=@survivor LEFT JOIN subcategories s ON s.id=sv.subcategory_id INNER JOIN categories c ON c.id=COALESCE(s.category_id,sv.category_id)
            WHERE pv.parameter_id=@absorbed{(subtree is null ? "" : $" AND p.subcategory_id IN ({string.Join(",", subtree)})")}
            """, ("@absorbed", absorbedId), ("@survivor", survivorId), ("@action", action), ("@actor", actor), ("@reason", Cut(reason, 500)), ("@now", MariaTimeText.Format(DateTime.UtcNow)));
        await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    // The links of a product that is being deleted: the history of where each parameter was used keeps the product.
    public static async Task WriteProductDeletionAsync(MySqlConnection connection, MySqlTransaction transaction, int productId, string actor, string reason, CancellationToken token)
    {
        await using var insert = Command(connection, transaction, """
            INSERT INTO parameter_usage_log(definition_kind,definition_id,parameter_name,value_text,product_id,product_name,node_path,action,actor,reason,occurred_utc)
            SELECT 'parameter',sp.id,sp.name,CASE WHEN pv.archived=1 THEN COALESCE(pv.baked_text,v.value) ELSE v.value END,p.id,p.name,
                   CASE WHEN s.id IS NULL THEN c.name ELSE CONCAT(c.name,' / ',s.name) END,@action,@actor,@reason,@now
            FROM product_parameter_values pv
            INNER JOIN products p ON p.id=pv.product_id INNER JOIN subcategory_parameters sp ON sp.id=pv.parameter_id
            INNER JOIN parameter_values v ON v.id=pv.value_id
            LEFT JOIN subcategories s ON s.id=sp.subcategory_id INNER JOIN categories c ON c.id=COALESCE(s.category_id,sp.category_id)
            WHERE pv.product_id=@product
            """, ("@product", productId), ("@action", ParameterUsageActions.ProductDeleted), ("@actor", actor), ("@reason", Cut(reason, 500)),
            ("@now", MariaTimeText.Format(DateTime.UtcNow)));
        await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    private static string Cut(string text, int length) => text.Length <= length ? text : text[..length];
}
