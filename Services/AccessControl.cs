using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace BlazorStoc.Services;

public static class AccessRoles
{
    public const string Administrator = "Administrator";
    public const string LimitedUser = "Utilizator";
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Administrator, LimitedUser
    };
}

public interface IAccessControl
{
    Task<bool> IsAdministratorAsync(CancellationToken cancellationToken = default);
    Task<bool> CanManageProductsAsync(CancellationToken cancellationToken = default);
    Task<bool> CanManageBeneficiariesAsync(CancellationToken cancellationToken = default);
    Task<string?> GetUsernameAsync(CancellationToken cancellationToken = default);
    Task EnsureAdministratorAsync(CancellationToken cancellationToken = default);
    Task EnsureProductOperatorAsync(CancellationToken cancellationToken = default);
    Task EnsureBeneficiaryOperatorAsync(CancellationToken cancellationToken = default);

    // A permission of the catalog (Services/Permissions.cs), e.g. "produse.edit". The default is what a user had before user types existed
    // (the administrator everything, an operator the default keys of "Utilizator"); CurrentUserAccess answers from the user type.
    async Task<bool> HasAsync(string permission, CancellationToken cancellationToken = default) =>
        await IsAdministratorAsync(cancellationToken).ConfigureAwait(false) ||
        (Permissions.DefaultUserKeys.Contains(permission) && await CanManageProductsAsync(cancellationToken).ConfigureAwait(false));

    async Task EnsureAsync(string permission, CancellationToken cancellationToken = default)
    {
        if (!await HasAsync(permission, cancellationToken).ConfigureAwait(false))
            throw new AccessDeniedException("Nu ai dreptul necesar pentru această operație.");
    }

    async Task<bool> HasAnyAsync(IEnumerable<string> permissions, CancellationToken cancellationToken = default)
    {
        foreach (var permission in permissions)
            if (await HasAsync(permission, cancellationToken).ConfigureAwait(false)) return true;
        return false;
    }

    async Task EnsureAnyAsync(IEnumerable<string> permissions, CancellationToken cancellationToken = default)
    {
        if (!await HasAnyAsync(permissions, cancellationToken).ConfigureAwait(false))
            throw new AccessDeniedException("Nu ai dreptul necesar pentru această operație.");
    }

    // Reading reference data (beneficiaries, suppliers, vehicles, projects): any signed-in user whose type has at least one permission.
    Task EnsureAnyPermissionAsync(CancellationToken cancellationToken = default) => EnsureProductOperatorAsync(cancellationToken);

    Task<bool> HasModuleWriteAsync(string module, CancellationToken cancellationToken = default) => HasAnyAsync(Permissions.WriteKeys(module), cancellationToken);

    // Write access to a module: at least one of its actions other than viewing (adding, editing, deleting, exporting, its special operations).
    Task EnsureModuleAsync(string module, CancellationToken cancellationToken = default) => EnsureAnyAsync(Permissions.WriteKeys(module), cancellationToken);
}

public sealed class CurrentUserAccess(AuthenticationStateProvider authenticationStateProvider, IUserPermissions? permissions = null,
    IHttpContextAccessor? httpContextAccessor = null) : IAccessControl
{
    // The signed-in user: from the circuit of a page; for an endpoint (the PDF of a consumption note, the CSV exports, the pictures) there is no circuit and the
    // authentication state provider refuses to answer, so the user of the request is taken.
    private async Task<ClaimsPrincipal> CurrentUserAsync()
    {
        try { return (await authenticationStateProvider.GetAuthenticationStateAsync()).User; }
        catch (InvalidOperationException) when (httpContextAccessor?.HttpContext is { } context) { return context.User; }
    }

    public async Task<bool> HasAsync(string permission, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await CurrentUserAsync();
        if (permissions is null) return user.IsInRole(AccessRoles.Administrator) || (Permissions.DefaultUserKeys.Contains(permission) && user.IsInRole(AccessRoles.LimitedUser));
        return (await permissions.GetKeysAsync(user, cancellationToken)).Contains(permission);
    }

    public async Task EnsureAsync(string permission, CancellationToken cancellationToken = default)
    {
        if (!await HasAsync(permission, cancellationToken))
            throw new AccessDeniedException("Nu ai dreptul necesar pentru această operație.");
    }

    public async Task<bool> IsAdministratorAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await CurrentUserAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return user.IsInRole(AccessRoles.Administrator);
    }

    // Legacy "operator" check: a signed-in user whose type has at least one permission. Each module checks its own keys (EnsureModuleAsync).
    public async Task<bool> CanManageProductsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await CurrentUserAsync();
        cancellationToken.ThrowIfCancellationRequested();
        if (user.Identity?.IsAuthenticated != true) return false;
        if (permissions is null) return user.IsInRole(AccessRoles.Administrator) || user.IsInRole(AccessRoles.LimitedUser);
        return (await permissions.GetKeysAsync(user, cancellationToken)).Count > 0;
    }

    public Task<bool> CanManageBeneficiariesAsync(CancellationToken cancellationToken = default) =>
        CanManageProductsAsync(cancellationToken);

    public async Task<string?> GetUsernameAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await CurrentUserAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return user.Identity?.Name;
    }

    public async Task EnsureAdministratorAsync(CancellationToken cancellationToken = default)
    {
        if (!await IsAdministratorAsync(cancellationToken))
            throw new AccessDeniedException("Operația este permisă numai administratorilor.");
    }

    public async Task EnsureProductOperatorAsync(CancellationToken cancellationToken = default)
    {
        if (!await CanManageProductsAsync(cancellationToken))
            throw new AccessDeniedException("Operația este permisă numai utilizatorilor autentificați.");
    }

    public async Task EnsureBeneficiaryOperatorAsync(CancellationToken cancellationToken = default)
    {
        if (!await CanManageBeneficiariesAsync(cancellationToken))
            throw new AccessDeniedException("Operația este permisă numai utilizatorilor autentificați.");
    }
}

public sealed class AccessDeniedException(string message) : Exception(message);
