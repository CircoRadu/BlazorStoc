using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace BlazorStoc.Services;

// What a signed-in user may do, resolved from the user type (user_types / user_type_permissions) and not from the cookie, so a change of a type or of
// the type of a user applies at the next request. The type "Administrator" always has every key of the catalog. The resolved sets are cached for a
// short time and cleared by Invalidate() whenever a type or a user is saved.
public interface IUserPermissions
{
    Task<IReadOnlySet<string>> GetKeysAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default);
    void Invalidate();
}

public sealed class MariaUserPermissions(IConfiguration configuration, TimeProvider? clock = null) : IUserPermissions
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);
    private static readonly IReadOnlySet<string> None = new HashSet<string>();
    private static readonly IReadOnlySet<string> Everything = new HashSet<string>(Permissions.AllKeys, StringComparer.Ordinal);
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private readonly object gate = new();
    private Dictionary<int, IReadOnlySet<string>> byUser = [];
    private readonly Dictionary<int, IReadOnlySet<string>> lastKnown = [];   // survives Lifetime; used only while a backup/restore keeps the database closed
    private DateTimeOffset loadedAt = DateTimeOffset.MinValue;

    public void Invalidate() { lock (gate) { byUser = []; lastKnown.Clear(); loadedAt = DateTimeOffset.MinValue; } }

    public async Task<IReadOnlySet<string>> GetKeysAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        if (user.Identity?.IsAuthenticated != true) return None;
        // The administrator of the configuration (no row in web_users) has no user id: full access.
        if (!int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return user.IsInRole(AccessRoles.Administrator) ? Everything : FromRole(user);
        lock (gate)
        {
            if (time.GetUtcNow() - loadedAt < Lifetime && byUser.TryGetValue(userId, out var cached)) return ForRole(user, cached);
        }
        IReadOnlySet<string> keys;
        try { keys = await LoadAsync(userId, cancellationToken).ConfigureAwait(false) ?? FromRole(user); }
        catch (MySqlException) { keys = FromRole(user); }   // schema not migrated yet: the rights of the role, as before user types existed
        catch (MaintenanceInProgressException)
        {
            // A backup/restore holds the data: the waiting page (and the pages already open) must still be drawn, so the last known keys of the user (the rights of the
            // role when there are none) answer, and nothing is cached: the next request after the operation reads the database again.
            lock (gate) { if (lastKnown.TryGetValue(userId, out var known)) return ForRole(user, known); }
            return FromRole(user);
        }
        lock (gate)
        {
            if (time.GetUtcNow() - loadedAt >= Lifetime) { byUser = []; loadedAt = time.GetUtcNow(); }
            // What the type grants is cached as it is; the role of the session decides what of it is honoured (below), so an older session of the same user, still carrying the
            // role it was opened with, never takes keys away from (or gives them to) a session opened after the user was moved to another type.
            byUser[userId] = keys;
            lastKnown[userId] = keys;
        }
        return ForRole(user, keys);
    }

    // The keys reserved to the administrator (ANAF, backup, modules) are honoured only for the Administrator role (rows saved before those modules became
    // administrator-only may still hold them).
    private static IReadOnlySet<string> ForRole(ClaimsPrincipal user, IReadOnlySet<string> keys) =>
        user.IsInRole(AccessRoles.Administrator) || !keys.Any(key => Permissions.IsAdminOnly(key) && !Permissions.IsDeveloperOnly(key)) ? keys
            : keys.Where(key => !Permissions.IsAdminOnly(key) || Permissions.IsDeveloperOnly(key)).ToHashSet(StringComparer.Ordinal);   // the keys of a developer survive: the flag is the right, not the role

    private static IReadOnlySet<string> FromRole(ClaimsPrincipal user) => user.IsInRole(AccessRoles.Administrator) ? Everything
        : user.IsInRole(AccessRoles.LimitedUser) ? new HashSet<string>(Permissions.DefaultUserKeys, StringComparer.Ordinal) : None;

    private async Task<IReadOnlySet<string>?> LoadAsync(int userId, CancellationToken cancellationToken)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = MariaDb.Command(connection, null, """
            SELECT t.id, t.name, t.is_system, u.is_developer FROM web_users u INNER JOIN user_types t ON t.id = u.user_type_id WHERE u.id=@id
            """, ("@id", userId));
        long typeId; bool administrator, developer;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
            typeId = reader.GetInt64(0);
            administrator = reader.GetBoolean(2) && string.Equals(reader.GetString(1), AccessRoles.Administrator, StringComparison.Ordinal);
            developer = reader.GetBoolean(3);
        }
        // The tile Dezvoltare of Setari and the tabs Bugs and Propuneri of Notificari (the keys of the reports and proposals) belong to the accounts marked as developers, administrators too.
        if (administrator) return developer ? Everything : Everything.Where(key => !Permissions.IsDeveloperOnly(key)).ToHashSet(StringComparer.Ordinal);
        await using var keys = MariaDb.Command(connection, null, "SELECT permission_key FROM user_type_permissions WHERE user_type_id=@id", ("@id", typeId));
        var set = new HashSet<string>(StringComparer.Ordinal);
        await using var keyReader = await keys.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await keyReader.ReadAsync(cancellationToken).ConfigureAwait(false)) { var key = keyReader.GetString(0); if (developer || !Permissions.IsDeveloperOnly(key)) set.Add(key); }
        // A developer sees the reports and the proposals whatever the type of the account (a user of a limited type too).
        if (developer) foreach (var key in Everything.Where(Permissions.IsDeveloperOnly)) set.Add(key);
        return set;
    }
}

// Policies named "perm:<key>" (the user has the permission) and "perm:<key1>|<key2>" (the user has at least one of them), created on demand
// so a page says [Authorize(Policy = PermissionPolicy.For("stoc.view"))] and the menu says <AuthorizeView Policy="perm:stoc.view">.
public static class PermissionPolicy
{
    public const string Prefix = "perm:";
    public static string For(params string[] keys) => Prefix + string.Join('|', keys);
}

public sealed class PermissionRequirement(IReadOnlyList<string> anyOf) : IAuthorizationRequirement
{
    public IReadOnlyList<string> AnyOf { get; } = anyOf;
}

public sealed class PermissionHandler(IUserPermissions permissions) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var keys = await permissions.GetKeysAsync(context.User).ConfigureAwait(false);
        if (requirement.AnyOf.Any(keys.Contains)) context.Succeed(requirement);
    }
}

public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(PermissionPolicy.Prefix, StringComparison.Ordinal)) return await base.GetPolicyAsync(policyName).ConfigureAwait(false);
        var keys = policyName[PermissionPolicy.Prefix.Length..].Split('|', StringSplitOptions.RemoveEmptyEntries);
        if (keys.Length == 0 || !keys.All(Permissions.IsKnown)) return null;   // an unknown key is a mistake in the code: the policy does not exist
        return new AuthorizationPolicyBuilder().RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(keys)).Build();
    }
}
