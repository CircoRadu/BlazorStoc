using Microsoft.Extensions.Configuration;
using MySqlConnector;
using BlazorStoc.Services;

namespace BlazorStoc.Checks;

public static partial class MariaExtendedChecks
{
    // ---- User types: system types, catalog permissions, assignment ----------------------------------------------------------------
    private static async Task UserTypesAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe)
    {
        await UserTypeSeeder.EnsureAsync(configuration);
        async Task<long> TypeId(string name) => await ScalarLongAsync(probe, "SELECT id FROM user_types WHERE name=@n AND is_system=1", ("@n", name));
        async Task<long> KeyCount(long id) => await ScalarLongAsync(probe, "SELECT COUNT(*) FROM user_type_permissions WHERE user_type_id=@id", ("@id", id));
        var administrator = await TypeId(AccessRoles.Administrator);
        var limited = await TypeId(AccessRoles.LimitedUser);
        Check(administrator > 0 && limited > 0 && administrator != limited, "The two system types exist (Administrator, Utilizator)");
        Check(await KeyCount(administrator) == Permissions.AllKeys.Count, "The Administrator type has every key of the catalog");

        // Seeding again changes nothing, and a permission the administrator removed from "Utilizator" stays removed.
        var removed = Permissions.DefaultUserKeys[0];
        await using (var ensure = new MySqlCommand("INSERT IGNORE INTO user_type_permissions(user_type_id,permission_key) VALUES(@id,@key)", probe))
        {
            ensure.Parameters.AddWithValue("@id", limited); ensure.Parameters.AddWithValue("@key", removed);
            await ensure.ExecuteNonQueryAsync();
        }
        var before = await KeyCount(limited);
        await using (var delete = new MySqlCommand("DELETE FROM user_type_permissions WHERE user_type_id=@id AND permission_key=@key", probe))
        {
            delete.Parameters.AddWithValue("@id", limited); delete.Parameters.AddWithValue("@key", removed);
            await delete.ExecuteNonQueryAsync();
        }
        await UserTypeSeeder.EnsureAsync(configuration);
        Check(await KeyCount(limited) == before - 1, "Seeding again does not give back a permission removed from a system type");
        await using (var restore = new MySqlCommand("INSERT INTO user_type_permissions(user_type_id,permission_key) VALUES(@id,@key)", probe))
        {
            restore.Parameters.AddWithValue("@id", limited); restore.Parameters.AddWithValue("@key", removed);
            await restore.ExecuteNonQueryAsync();
        }
        Check(await ScalarLongAsync(probe, "SELECT COUNT(*) FROM user_type_permissions WHERE user_type_id=@id AND permission_key LIKE 'utilizatori.%'", ("@id", limited)) == 0 &&
              await ScalarLongAsync(probe, "SELECT COUNT(*) FROM user_type_permissions WHERE user_type_id=@id AND permission_key='setari-facturi.add'", ("@id", limited)) == 1,
            "\"Utilizator\" has no user administration and can still create invoice templates (the current rights)");

        // A new user, and one whose role changes, get the matching system type.
        var users = new MariaUserRepository(configuration, admin, audit);
        var user = await users.CreateAsync(new WebUserInput { Username = "ext.tip." + Suffix(), DisplayName = "Ext Tip", Role = AccessRoles.LimitedUser, Password = "parola-ext-tip-123" });
        try
        {
            Check(await ScalarLongAsync(probe, "SELECT user_type_id FROM web_users WHERE id=@id", ("@id", user.Id)) == limited, "A new user gets the system type of its role");
            var edit = WebUserInput.From(user); edit.TypeId = administrator; edit.Reason = "Ext schimbare tip";
            user = await users.UpdateAsync(user, edit);
            Check(await ScalarLongAsync(probe, "SELECT user_type_id FROM web_users WHERE id=@id", ("@id", user.Id)) == administrator, "Changing the role changes the user type");
        }
        finally
        {
            await users.DeleteAsync((await users.GetUsersAsync()).First(item => item.Id == user.Id), "Ext curatare");
        }
        // The old key "produse.iesiri" (stock movements inside the product module) becomes the four keys of the module "stoc".
        await using (var old = new MySqlCommand("INSERT IGNORE INTO user_type_permissions(user_type_id,permission_key) VALUES(@id,'produse.iesiri')", probe))
        {
            old.Parameters.AddWithValue("@id", limited);
            await old.ExecuteNonQueryAsync();
        }
        await UserTypeSeeder.EnsureAsync(configuration);
        Check(await ScalarLongAsync(probe, "SELECT COUNT(*) FROM user_type_permissions WHERE permission_key='produse.iesiri'") == 0 &&
              await ScalarLongAsync(probe, "SELECT COUNT(*) FROM user_type_permissions WHERE user_type_id=@id AND permission_key IN ('stoc.intrare','stoc.iesire','stoc.modificare','stoc.stornare')", ("@id", limited)) == 4,
            "The old key produse.iesiri is replaced by the stock keys of the module Stoc");
        Check(!Permissions.AllKeys.Contains("produse.iesiri") && Permissions.Modules.Single(module => module.Id == "stoc").Actions.Count == 7 &&
              Permissions.Modules.Single(module => module.Id == "produse").Actions.Single(action => action.Id == Permissions.Add).Label == "Adaugare produs",
            "The product module (the catalog) is separate from the stock movements and its actions name the product");
        await UserTypeRepositoryAsync(configuration, admin, audit, probe, administrator, limited);
    }
}
