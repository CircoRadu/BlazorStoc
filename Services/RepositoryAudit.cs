namespace BlazorStoc.Services;

internal static class RepositoryAudit
{
    public static async Task<(string Username, string Role)> ActorAsync(IAccessControl? access,
        CancellationToken cancellationToken)
    {
        if (access is null) return ("sistem", AccessRoles.LimitedUser);
        var username = await access.GetUsernameAsync(cancellationToken).ConfigureAwait(false) ?? "necunoscut";
        var role = await access.IsAdministratorAsync(cancellationToken).ConfigureAwait(false)
            ? AccessRoles.Administrator : AccessRoles.LimitedUser;
        return (username, role);
    }
}
