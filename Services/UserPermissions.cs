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
    private DateTimeOffset loadedAt = DateTimeOffset.MinValue;

    public void Invalidate() { lock (gate) { byUser = []; loadedAt = DateTimeOffset.MinValue; } }

    public async Task<IReadOnlySet<string>> GetKeysAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        if (user.Identity?.IsAuthenticated != true) return None;
        // The administrator of the configuration (no row in web_users) has no user id: full access.
        if (!int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return user.IsInRole(AccessRoles.Administrator) ? Everything : FromRole(user);
        lock (gate)
        {
            if (time.GetUtcNow() - loadedAt < Lifetime && byUser.TryGetValue(userId, out var cached)) return cached;
        }
        IReadOnlySet<string> keys;
        try { keys = await LoadAsync(userId, cancellationToken).ConfigureAwait(false) ?? FromRole(user); }
        catch (MySqlException) { keys = FromRole(user); }   // schema not migrated yet: the rights of the role, as before user types existed
        lock (gate)
        {
            if (time.GetUtcNow() - loadedAt >= Lifetime) { byUser = []; loadedAt = time.GetUtcNow(); }
            byUser[userId] = keys;
        }
        return keys;
    }

    private static IReadOnlySet<string> FromRole(ClaimsPrincipal user) => user.IsInRole(AccessRoles.Administrator) ? Everything
        : user.IsInRole(AccessRoles.LimitedUser) ? new HashSet<string>(Permissions.DefaultUserKeys, StringComparer.Ordinal) : None;

    private async Task<IReadOnlySet<string>?> LoadAsync(int userId, CancellationToken cancellationToken)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = MariaDb.Command(connection, null, """
            SELECT t.id, t.name, t.is_system FROM web_users u INNER JOIN user_types t ON t.id = u.user_type_id WHERE u.id=@id
            """, ("@id", userId));
        long typeId; bool administrator;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
            typeId = reader.GetInt64(0);
            administrator = reader.GetBoolean(2) && string.Equals(reader.GetString(1), AccessRoles.Administrator, StringComparison.Ordinal);
        }
        if (administrator) return Everything;
        await using var keys = MariaDb.Command(connection, null, "SELECT permission_key FROM user_type_permissions WHERE user_type_id=@id", ("@id", typeId));
        var set = new HashSet<string>(StringComparer.Ordinal);
        await using var keyReader = await keys.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await keyReader.ReadAsync(cancellationToken).ConfigureAwait(false)) set.Add(keyReader.GetString(0));
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
