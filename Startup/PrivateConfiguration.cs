using System.Text.Json;

namespace BlazorStoc.Startup;

// Passwords of the MariaDB accounts are never put in appsettings.json or in Git: they are read, optionally, from private JSON files outside
// the repository (see docs/CLAUDE_CONECTARE_MARIADB_LOCALA.md). The application account's file has the same keys as the "Database" section of
// appsettings.json and overrides them; it may also hold an "Authentication": { "Username", "Password" } section (loaded by the same
// AddJsonFile, no separate code), so the application starts with no password in appsettings.json or in the process arguments.
internal static class PrivateConfiguration
{
    // The application account's file, optional (the passwords can also come from environment variables). Database:PrivateConfigPath
    // (environment variable Database__PrivateConfigPath) changes the default location without changing the mechanism. Returns the path used.
    public static string AddApplicationFile(this WebApplicationBuilder builder)
    {
        var path = builder.Configuration["Database:PrivateConfigPath"]
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BlazorStoc-MariaDB", "application-connection.private.json");
        builder.Configuration.AddJsonFile(path, optional: true, reloadOnChange: false);
        return path;
    }

    // A dedicated account (blazorstoc_migrator, blazorstoc_backup, blazorstoc_restore) has a private file of the same shape as the application
    // one ({ "Database": { "User", "Password" } }) but must NOT override the application account, so its values are mapped explicitly to
    // Database:{account}User / Database:{account}Password. Looked up (unless Database:{account}PrivateConfigPath says otherwise) next to the
    // application file, then in the project's local-secrets folder. Optional: without it the account is simply not configured.
    public static void AddAccountFile(this WebApplicationBuilder builder, string account, string fileName, string applicationFilePath)
    {
        var path = builder.Configuration[$"Database:{account}PrivateConfigPath"] is { Length: > 0 } configured
            ? configured
            : new[]
                {
                    Path.Combine(Path.GetDirectoryName(applicationFilePath) ?? "", fileName),
                    Path.Combine(builder.Environment.ContentRootPath, "local-secrets", fileName)
                }.FirstOrDefault(File.Exists);
        if (path is null || !File.Exists(path)) return;
        using var file = JsonDocument.Parse(File.ReadAllText(path));
        if (!file.RootElement.TryGetProperty("Database", out var section)) return;
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"Database:{account}User"] = section.TryGetProperty("User", out var user) ? user.GetString() : null,
            [$"Database:{account}Password"] = section.TryGetProperty("Password", out var password) ? password.GetString() : null
        });
    }
}
