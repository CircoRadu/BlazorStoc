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
}

public sealed class CurrentUserAccess(AuthenticationStateProvider authenticationStateProvider) : IAccessControl
{
    public async Task<bool> IsAdministratorAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = await authenticationStateProvider.GetAuthenticationStateAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return state.User.IsInRole(AccessRoles.Administrator);
    }

    public async Task<bool> CanManageProductsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = await authenticationStateProvider.GetAuthenticationStateAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return state.User.Identity?.IsAuthenticated == true &&
            (state.User.IsInRole(AccessRoles.Administrator) || state.User.IsInRole(AccessRoles.LimitedUser));
    }

    public Task<bool> CanManageBeneficiariesAsync(CancellationToken cancellationToken = default) =>
        CanManageProductsAsync(cancellationToken);

    public async Task<string?> GetUsernameAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = await authenticationStateProvider.GetAuthenticationStateAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return state.User.Identity?.Name;
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
