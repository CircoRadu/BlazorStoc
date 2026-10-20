using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using MySqlConnector;
using BlazorStoc.Services;

namespace BlazorStoc.Checks;

public static partial class MariaExtendedChecks
{
    // The repository of user types, the permissions of the users of a type and the policies built from the keys.
    private static async Task UserTypeRepositoryAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe, long administrator, long limited)
    {
        var permissions = new MariaUserPermissions(configuration);
        var repository = new MariaUserTypeRepository(configuration, admin, audit, permissions);
        var users = new MariaUserRepository(configuration, admin, audit, null, permissions);
        var since = DateTime.UtcNow;
        await Task.Delay(30);
        async Task<int> Events(string action) => (await audit.GetEventsAsync()).Count(item => item.TimestampUtc > since && item.EntityType == AuditEntities.UserType && item.Action == action);
        var name = "Ext Tip " + Suffix();
        UserTypeDetail? type = null;
        WebUser? user = null;
        try
        {
            type = await repository.CreateAsync(new UserTypeInput { Name = name, Description = "Magaziner de proba", Keys = ["stoc.intrare", "furnizori.view", "nu.exista"] });
            Check(type.Keys.SetEquals(["stoc.view", "stoc.intrare", "furnizori.view"]) && !type.Summary.IsSystem && type.Summary.UserCount == 0 && type.Summary.Version == 0,
                "A new type keeps the known keys and adds viewing to a module whose action is granted");
            Check(await Events(AuditActions.CreateUserType) == 1 && await Events(AuditActions.GrantPermission) == 1, "Creating a type is journaled, with one consolidated event for all the permissions granted");
            await Rejects<UserOperationException>(() => repository.CreateAsync(new UserTypeInput { Name = name.ToUpperInvariant() }), "A second type with the same name (any case) is rejected");
            await Rejects<UserOperationException>(() => repository.CreateAsync(new UserTypeInput { Name = "x" }), "A type name needs at least two characters");

            // A user of this type has exactly the keys of the type, and a change of the type applies at once.
            user = await users.CreateAsync(new WebUserInput { Username = "ext.tipuri." + Suffix(), DisplayName = "Ext Tipuri", TypeId = type.Id, Password = "parola-ext-tipuri-1" });
            Check(user.Role == AccessRoles.LimitedUser && user.TypeId == type.Id && user.TypeName == name, "A user of a custom type has the role Utilizator and the name of the type");
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Role, user.Role)], "test"));
            var keys = await permissions.GetKeysAsync(principal);
            Check(keys.SetEquals(type.Keys), "The permissions of a signed-in user are the keys of its type");
            var edit = UserTypeInput.From(type); edit.Keys.Remove("furnizori.view"); edit.Keys.Add("beneficiari.view"); edit.Description = "Alta descriere"; edit.Reason = "Ext modificare drepturi";
            var updated = await repository.UpdateAsync(type, edit);
            Check(updated.Keys.SetEquals(["stoc.view", "stoc.intrare", "beneficiari.view"]) && updated.Summary.Version == 1 && updated.Summary.UserCount == 1,
                "Updating a type changes its keys and version");
            var grants = (await audit.GetEventsAsync()).Where(item => item.TimestampUtc > since && item.EntityType == AuditEntities.UserType && item.Action == AuditActions.GrantPermission).ToArray();
            var revokes = (await audit.GetEventsAsync()).Where(item => item.TimestampUtc > since && item.EntityType == AuditEntities.UserType && item.Action == AuditActions.RevokePermission).ToArray();
            Check(revokes.Length == 1 && grants.Length == 2 && await Events(AuditActions.EditUserTypeDescription) == 1 && await Events(AuditActions.RenameUserType) == 0,
                "The permissions granted by one save are one journal event, those revoked another, and the description a third");
            Check(revokes[0].Details.Contains("Număr permisiuni: 1") && revokes[0].Details.Contains("furnizori.view") &&
                  grants.Any(item => item.Details.Contains("Număr permisiuni: 1") && item.Details.Contains("beneficiari.view")) &&
                  grants.Any(item => item.Details.Contains("Număr permisiuni: 3") && item.Details.Contains("stoc.intrare") && item.Details.Contains("furnizori.view")),
                "A consolidated permission event counts and names every permission it covers");
            keys = await permissions.GetKeysAsync(principal);
            Check(keys.SetEquals(updated.Keys), "A change of the type applies at once to the users of the type (no new sign-in)");
            await Rejects<UserOperationException>(() => repository.UpdateAsync(type, edit), "A stale type edit is rejected");
            type = updated;

            // Guards.
            await Rejects<UserOperationException>(() => repository.DeleteAsync(type, "Ext stergere tip"), "A type with users cannot be deleted");
            var system = (await repository.GetAsync(administrator))!;
            await Rejects<UserOperationException>(() => repository.UpdateAsync(system, UserTypeInput.From(system)), "The Administrator type cannot be edited");
            await Rejects<UserOperationException>(() => repository.DeleteAsync(system, "Ext stergere sistem"), "A system type cannot be deleted");
            var limitedType = (await repository.GetAsync(limited))!;
            var rename = UserTypeInput.From(limitedType); rename.Name = "Altceva";
            await Rejects<UserOperationException>(() => repository.UpdateAsync(limitedType, rename), "A system type cannot be renamed");
            await Rejects<AccessDeniedException>(() => new MariaUserTypeRepository(configuration, new TestAccessControl(false, "simplu"), audit).CreateAsync(new UserTypeInput { Name = "Neautorizat " + Suffix() }),
                "A user without the permission cannot create a type");

            // A user moved to another type: the change is journaled by its own action; the admin type gives every key.
            var move = WebUserInput.From(user); move.TypeId = administrator; move.Reason = "Ext mutare tip";
            user = await users.UpdateAsync(user, move);
            // The session of the user still carries the role it was opened with (the role of the old type) until the next sign-in: the keys reserved to the administrator
            // (ANAF, backup, modules) are honoured only for the Administrator ROLE, so the old session gets every grantable key and a new sign-in gets all of them.
            var movedKeys = await permissions.GetKeysAsync(principal);
            var refreshed = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Role, user.Role)], "test"));
            var refreshedKeys = await permissions.GetKeysAsync(refreshed);
            Check(user.Role == AccessRoles.Administrator && refreshedKeys.Count == Permissions.AllKeys.Count,
                $"A user moved to the Administrator type has the role and, at the next sign-in, every key (role {user.Role}, {refreshedKeys.Count} of {Permissions.AllKeys.Count} keys)");
            Check(movedKeys.SetEquals(Permissions.GrantableKeys),
                $"Until that sign-in the session keeps its old role: every grantable key but none of the administrator-only ones ({movedKeys.Count} of {Permissions.GrantableKeys.Count})");
            Check((await audit.GetEventsAsync()).Count(item => item.TimestampUtc > since && item.EntityType == AuditEntities.User && item.Action == AuditActions.ChangeUserType) == 1,
                "Moving a user to another type is the journal action \"Schimbare tip utilizator\"");
            var back = WebUserInput.From(user); back.TypeId = type.Id; back.Reason = "Ext revenire tip";
            user = await users.UpdateAsync(user, back);

            // Policies are created from the keys.
            var provider = new PermissionPolicyProvider(Options.Create(new AuthorizationOptions()));
            Check(await provider.GetPolicyAsync(PermissionPolicy.For("stoc.view", "furnizori.view")) is { Requirements.Count: 2 } && await provider.GetPolicyAsync("perm:nu.exista") is null &&
                  await provider.GetPolicyAsync(PermissionPolicy.For("stoc.view")) is not null, "Policies \"perm:<key>\" exist for the catalog keys only");
        }
        finally
        {
            if (user is not null) await users.DeleteAsync((await users.GetUsersAsync()).First(item => item.Id == user.Id), "Ext curatare");
            if (type is not null)
            {
                await repository.DeleteAsync((await repository.GetAsync(type.Id))!, "Ext curatare tip");
                Check(await Events(AuditActions.DeleteUserType) == 1 && await ScalarLongAsync(probe, "SELECT COUNT(*) FROM user_types WHERE id=@id", ("@id", type.Id)) == 0,
                    "A type without users is deleted and the deletion is journaled");
            }
        }
    }
}
