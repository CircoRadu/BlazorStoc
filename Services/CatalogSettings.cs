using System.Globalization;
using MySqlConnector;
using static BlazorStoc.Services.MariaDb;

namespace BlazorStoc.Services;

// Setari -> Catalog (administrator only): the largest number of subcategory levels of the tree under a category. 1 means subcategories without children;
// the tree never gets deeper than this and the value is never lowered below the depth the tree already has.
public static class CatalogSettingsRules
{
    public const int MinSubcategoryDepth = 1;
    public const int MaxSubcategoryDepth = 15;
    public const int DefaultSubcategoryDepth = 10;
    public static readonly string RangeMessage = $"Adâncimea maximă trebuie să fie între {MinSubcategoryDepth} și {MaxSubcategoryDepth} niveluri.";
    public static string BelowCurrentMessage(int current) => $"Arborele de subcategorii are deja {current} niveluri. Adâncimea maximă nu poate fi mai mică.";
    public static bool Valid(int value) => value is >= MinSubcategoryDepth and <= MaxSubcategoryDepth;
}

public interface ICatalogSettings
{
    // The largest number of subcategory levels (the default when nothing is saved).
    Task<int> GetMaxSubcategoryDepthAsync(CancellationToken cancellationToken = default);
    // Administrator only; returns the depth of the existing tree through the exception when the value is lower than it.
    Task SaveMaxSubcategoryDepthAsync(int value, CancellationToken cancellationToken = default);
    // Days an archived parameter, family or tag waits before its restore can be given up for good (30 to 360, 30 until the administrator changes it).
    Task<int> GetRestoreRetentionDaysAsync(CancellationToken cancellationToken = default) => Task.FromResult(ParameterArchiveRules.DefaultRetentionDays);
    Task SaveRestoreRetentionDaysAsync(int value, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

public sealed class MariaCatalogSettings(IConfiguration configuration, IAccessControl? access = null, IAuditTrail? audit = null) : ICatalogSettings
{
    public async Task<int> GetMaxSubcategoryDepthAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        return await ReadAsync(connection, null, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<int> ReadAsync(MySqlConnection connection, MySqlTransaction? transaction, CancellationToken token)
    {
        await using var command = Command(connection, transaction, "SELECT max_subcategory_depth FROM catalog_settings WHERE id=1");
        return await command.ExecuteScalarAsync(token).ConfigureAwait(false) is int value && CatalogSettingsRules.Valid(value) ? value : CatalogSettingsRules.DefaultSubcategoryDepth;
    }

    public async Task<int> GetRestoreRetentionDaysAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        return await ReadRetentionAsync(connection, null, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<int> ReadRetentionAsync(MySqlConnection connection, MySqlTransaction? transaction, CancellationToken token)
    {
        await using var command = Command(connection, transaction, "SELECT restore_retention_days FROM catalog_settings WHERE id=1");
        return await command.ExecuteScalarAsync(token).ConfigureAwait(false) is int value && ParameterArchiveRules.ValidRetention(value) ? value : ParameterArchiveRules.DefaultRetentionDays;
    }

    public async Task SaveRestoreRetentionDaysAsync(int value, CancellationToken cancellationToken = default)
    {
        if (access is not null) await access.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        if (!ParameterArchiveRules.ValidRetention(value)) throw new ProductOperationException(ParameterArchiveRules.RetentionRangeMessage);
        var by = access is null ? "" : await access.GetUsernameAsync(cancellationToken).ConfigureAwait(false) ?? "";
        var old = await MariaDb.WriteAsync(configuration, async (connection, transaction) =>
        {
            var before = await ReadRetentionAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            await using var save = Command(connection, transaction, """
                INSERT INTO catalog_settings(id,max_subcategory_depth,restore_retention_days,updated_by,updated_utc) VALUES(1,@depth,@value,@by,@now)
                ON DUPLICATE KEY UPDATE restore_retention_days=@value,updated_by=@by,updated_utc=@now
                """, ("@depth", CatalogSettingsRules.DefaultSubcategoryDepth), ("@value", value), ("@by", by), ("@now", MariaTimeText.Format(DateTime.UtcNow)));
            await save.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return before;
        }, message => new ProductOperationException(message), cancellationToken,
            "Modificările sunt permise numai în baza BlazorStoc. Verifică numele bazei configurate.").ConfigureAwait(false);
        if (old != value)
            await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.Subcategory, AuditActions.SetRestoreRetention, "", "Setări catalog",
                AuditDetails.Changes([new AuditChange("Perioadă de păstrare pentru restaurare (zile)", old.ToString(CultureInfo.InvariantCulture), value.ToString(CultureInfo.InvariantCulture))]), "", cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveMaxSubcategoryDepthAsync(int value, CancellationToken cancellationToken = default)
    {
        if (access is not null) await access.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        if (!CatalogSettingsRules.Valid(value)) throw new ProductOperationException(CatalogSettingsRules.RangeMessage);
        var old = await MariaDb.WriteAsync(configuration, async (connection, transaction) =>
        {
            var parents = await SubcategoryAncestry.ParentsAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            var current = parents.Keys.Select(id => SubcategoryAncestry.Chain(parents, id).Count).DefaultIfEmpty(0).Max();
            if (value < current) throw new ProductOperationException(CatalogSettingsRules.BelowCurrentMessage(current));
            var before = await ReadAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            await using var save = Command(connection, transaction, """
                INSERT INTO catalog_settings(id,max_subcategory_depth,updated_by,updated_utc) VALUES(1,@value,@by,@now)
                ON DUPLICATE KEY UPDATE max_subcategory_depth=@value,updated_by=@by,updated_utc=@now
                """, ("@value", value), ("@by", access is null ? "" : await access.GetUsernameAsync(cancellationToken).ConfigureAwait(false) ?? ""),
                ("@now", MariaTimeText.Format(DateTime.UtcNow)));
            await save.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return before;
        }, message => new ProductOperationException(message), cancellationToken,
            "Modificările sunt permise numai în baza BlazorStoc. Verifică numele bazei configurate.").ConfigureAwait(false);
        if (old != value)
            await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.Subcategory, AuditActions.SetMaxSubcategoryDepth, "", "Setări catalog",
                AuditDetails.Changes([new AuditChange("Adâncime maximă subcategorii", old.ToString(CultureInfo.InvariantCulture), value.ToString(CultureInfo.InvariantCulture))]), "", cancellationToken).ConfigureAwait(false);
    }
}
