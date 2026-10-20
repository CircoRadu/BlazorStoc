using MySqlConnector;

namespace BlazorStoc.Services;

// Creates the two system user types and gives existing users their type. The migrator account has no data rights (see migration 40), so this runs
// at startup with the application account. Idempotent: it adds only what is missing and never changes a permission the administrator edited later,
// except that the type "Administrator" always has every key of the catalog (a new module reaches it automatically).
public static class UserTypeSeeder
{
    public static async Task EnsureAsync(IConfiguration configuration, CancellationToken cancellationToken = default)
    {
        if (!MariaDatabaseGuard.IsAllowedDatabase(configuration)) return;
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var (administratorId, _) = await EnsureTypeAsync(connection, transaction, AccessRoles.Administrator,
                "Acces complet la toate modulele si la administrarea sistemului.", cancellationToken).ConfigureAwait(false);
            var (userId, userCreated) = await EnsureTypeAsync(connection, transaction, AccessRoles.LimitedUser,
                "Lucru curent in module, fara administrarea sistemului.", cancellationToken).ConfigureAwait(false);
            foreach (var key in Permissions.AllKeys) await AddPermissionAsync(connection, transaction, administratorId, key, cancellationToken).ConfigureAwait(false);
            if (userCreated)
                foreach (var key in Permissions.DefaultUserKeys) await AddPermissionAsync(connection, transaction, userId, key, cancellationToken).ConfigureAwait(false);
            // 10.10.2026: stock movements got a module of their own ("produse.iesiri" became the four "stoc.*" keys).
            foreach (var key in new[] { "stoc.intrare", "stoc.iesire", "stoc.modificare", "stoc.stornare" })
            {
                await using var split = new MySqlCommand("""
                    INSERT IGNORE INTO user_type_permissions(user_type_id,permission_key)
                    SELECT user_type_id, @key FROM user_type_permissions WHERE permission_key='produse.iesiri'
                    """, connection, transaction);
                split.Parameters.AddWithValue("@key", key);
                await split.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            await using (var drop = new MySqlCommand("DELETE FROM user_type_permissions WHERE permission_key='produse.iesiri'", connection, transaction))
                await drop.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            // The stock page needs "stoc.view" (it used to be "produse.view", which no longer exists).
            await using (var view = new MySqlCommand("""
                INSERT IGNORE INTO user_type_permissions(user_type_id,permission_key)
                SELECT user_type_id, 'stoc.view' FROM user_type_permissions WHERE permission_key='produse.view'
                """, connection, transaction))
                await view.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await using (var dropView = new MySqlCommand("DELETE FROM user_type_permissions WHERE permission_key='produse.view'", connection, transaction))
                await dropView.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            // Keys that are not in the catalog any more (removed actions) are dropped from every type.
            await using (var orphans = new MySqlCommand("DELETE FROM user_type_permissions WHERE permission_key NOT IN (" +
                string.Join(",", Permissions.AllKeys.Select((_, index) => "@k" + index)) + ")", connection, transaction))
            {
                for (var index = 0; index < Permissions.AllKeys.Count; index++) orphans.Parameters.AddWithValue("@k" + index, Permissions.AllKeys[index]);
                await orphans.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            await using var assign = new MySqlCommand("""
                UPDATE web_users u INNER JOIN user_types t ON t.name = u.role COLLATE utf8mb4_nopad_bin AND t.is_system = 1
                SET u.user_type_id = t.id WHERE u.user_type_id IS NULL
                """, connection, transaction);
            await assign.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (MySqlException exception) when (exception.Number is 1146 or 1054)
        {
            // Migration 40 is not applied yet (no migrator account configured): nothing to seed.
        }
    }

    private static async Task<(long Id, bool Created)> EnsureTypeAsync(MySqlConnection connection, MySqlTransaction transaction, string name,
        string description, CancellationToken cancellationToken)
    {
        var key = TextNormalization.UniquenessKey(name);
        await using var insert = new MySqlCommand("""
            INSERT IGNORE INTO user_types(name,normalized_name,description,is_system,version) VALUES(@name,@key,@description,1,0)
            """, connection, transaction);
        insert.Parameters.AddWithValue("@name", name); insert.Parameters.AddWithValue("@key", key); insert.Parameters.AddWithValue("@description", description);
        var created = await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
        await using var select = new MySqlCommand("SELECT id FROM user_types WHERE normalized_name=@key", connection, transaction);
        select.Parameters.AddWithValue("@key", key);
        return (Convert.ToInt64(await select.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)), created);
    }

    private static async Task AddPermissionAsync(MySqlConnection connection, MySqlTransaction transaction, long typeId, string key, CancellationToken cancellationToken)
    {
        await using var command = new MySqlCommand("INSERT IGNORE INTO user_type_permissions(user_type_id,permission_key) VALUES(@id,@key)", connection, transaction);
        command.Parameters.AddWithValue("@id", typeId); command.Parameters.AddWithValue("@key", key);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
