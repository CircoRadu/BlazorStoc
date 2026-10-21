using System.Text.Json;

namespace BlazorStoc.Services;

/// <summary>
/// "BlazorStoc.dll --create-administrator": the installer makes the first administrator account of the application in the users table (web_users). The details come as one JSON line on
/// the standard input ({ "username", "displayName", "password" }), never on the command line. The account is made by the same repository the Utilizatori page uses (same rules for the
/// name and the password, same password hash, the system type "Administrator"), the journal records it as made by the system.
/// Exit codes: 0 made, 4 a user with that name exists already (nothing changed), 5 the details are not valid (the reason is printed), 1 other failure.
/// </summary>
public static class AdministratorBootstrap
{
    public const int Created = 0;
    public const int Failed = 1;
    public const int AlreadyExists = 4;
    public const int Invalid = 5;

    private sealed record Request(string? Username, string? DisplayName, string? Password);

    public static async Task<int> RunAsync(IServiceProvider services, IConfiguration configuration, TextReader input, TextWriter output, CancellationToken cancellationToken = default)
    {
        Request? request;
        try { request = JsonSerializer.Deserialize<Request>(await input.ReadLineAsync(cancellationToken).ConfigureAwait(false) ?? "", new JsonSerializerOptions(JsonSerializerDefaults.Web)); }
        catch (JsonException) { request = null; }
        if (request is null || string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrEmpty(request.Password))
        {
            output.WriteLine("ADMIN-INVALID The details of the account were not received.");
            return Invalid;
        }
        // The name of the administrator of the configuration is checked at sign-in before the users of the database: a user with that name could never sign in.
        if (string.Equals(request.Username.Trim(), configuration["Authentication:Username"] ?? "admin", StringComparison.OrdinalIgnoreCase))
        {
            output.WriteLine("ADMIN-INVALID The user name is reserved for the administrator of the configuration: choose another.");
            return Invalid;
        }
        try
        {
            await UserTypeSeeder.EnsureAsync(configuration, cancellationToken).ConfigureAwait(false);
            using var scope = services.CreateScope();
            var trail = scope.ServiceProvider.GetService<IAuditTrail>();
            // The repository gets no journal: the creation is recorded below under its own action, once.
            var repository = new MariaUserRepository(configuration);
            var existing = (await repository.GetUsersAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(user => string.Equals(user.Username, request.Username.Trim(), StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                output.WriteLine($"ADMIN-EXISTS A user named {existing.Username} exists already (role {existing.Role}); nothing was changed.");
                return AlreadyExists;
            }
            var created = await repository.CreateAsync(new WebUserInput
            {
                Username = request.Username.Trim(), DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? request.Username.Trim() : request.DisplayName.Trim(),
                Role = AccessRoles.Administrator, Password = request.Password
            }, cancellationToken).ConfigureAwait(false);
            await AuditRecorder.RecordActionAsync(trail, null, AuditEntities.User, AuditActions.CreateInstallerAdministrator, created.Id.ToString(), $"#{created.Id} · {created.Username}",
                AuditDetails.Identification(("Nume utilizator", created.Username), ("Nume afișat", created.DisplayName), ("Rol", created.Role), ("Tip utilizator", created.TypeName), ("Stare", "activ")),
                "Instalarea aplicației", cancellationToken).ConfigureAwait(false);
            output.WriteLine($"ADMIN-CREATED {created.Username} ({created.Role})");
            return Created;
        }
        catch (UserOperationException exception)
        {
            output.WriteLine("ADMIN-INVALID " + exception.Message);
            return Invalid;
        }
    }
}
