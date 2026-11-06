namespace BlazorStoc.Services;

/// <summary>"BlazorStoc.dll --reset-authenticator nume": removes the authenticator and the recovery codes of an account from the server itself (the only way back when nobody can sign in with a code).
/// Exit codes: 0 reset (or the account had none), 4 no such account, 5 no name given, 1 other failure.</summary>
public static class AuthenticatorReset
{
    public static async Task<int> RunAsync(IServiceProvider services, IConfiguration configuration, string[] args, TextWriter output, CancellationToken cancellationToken = default)
    {
        var index = Array.IndexOf(args, "--reset-authenticator");
        var name = index >= 0 && index + 1 < args.Length ? args[index + 1].Trim() : "";
        if (name.Length == 0 || name.StartsWith("--")) { output.WriteLine("AUTH-INVALID Write the user name after --reset-authenticator."); return 5; }
        using var scope = services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ITwoFactorStore>();
        int? id = null;
        if (string.Equals(name, configuration["Authentication:Username"] ?? "admin", StringComparison.OrdinalIgnoreCase)) id = 0;
        else
        {
            await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
            await using var command = MariaDb.Command(connection, "SELECT id FROM web_users WHERE normalized_username=@name LIMIT 1", ("@name", TextNormalization.UniquenessKey(name)));
            if (await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is { } found) id = Convert.ToInt32(found);
        }
        if (id is null) { output.WriteLine($"AUTH-NOUSER No account named '{name}'."); return 4; }
        var removed = await store.RevokeAsync(id.Value, cancellationToken).ConfigureAwait(false);
        try
        {
            if (removed && scope.ServiceProvider.GetService<IAuditTrail>() is { } trail)
                await trail.RecordAsync(new("sistem", AccessRoles.Administrator, AuditEntities.User, AuditActions.RevokeTwoFactor, name,
                    "Autentificatorul a fost șters de pe server cu comanda --reset-authenticator.", string.Empty, string.Empty), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { output.WriteLine("AUTH-WARN The journal entry could not be written."); }
        output.WriteLine(removed ? $"AUTH-OK The authenticator of '{name}' was removed." : $"AUTH-OK '{name}' had no authenticator.");
        return 0;
    }
}
