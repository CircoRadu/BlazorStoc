using BlazorStoc.Services;
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
            ?? AppDataRoot.Combine(builder.Configuration, "application-connection.private.json");
        builder.Configuration.AddJsonFile(path, optional: true, reloadOnChange: false);
        return path;
    }

    // The application file and the files of the dedicated accounts, as Program.cs and the worker processes load them. Returns the path of the application file.
    public static string AddPrivateAccountFiles(this WebApplicationBuilder builder)
    {
        var applicationFile = builder.AddApplicationFile();
        builder.AddAccountFile("Migrator", "migration-account.private.json", applicationFile);   // schema migrations; without it the schema is only checked, never changed
        builder.AddAccountFile("Backup", "backup-account.private.json", applicationFile);        // mariadb-dump only; without it the dump runs as the application account
        builder.AddAccountFile("Restore", "restore-account.private.json", applicationFile);      // schema swap of the restoration; without it a real restoration stops before changing anything
        return applicationFile;
    }

    // Everything that decides how a process reaches the database, in the one order every process uses: appsettings and the environment, the private files of the accounts,
    // then the override file of Setari -> Baza de date LAST, so what the administrator saved wins. The application and its worker processes (backup, restoration) call this,
    // so a changed host, port or password is seen by all of them. Returns the path of the application file.
    public static string AddEffectiveConfiguration(this WebApplicationBuilder builder)
    {
        var applicationFile = builder.AddPrivateAccountFiles();
        builder.AddFirstRunAccounts();
        builder.Configuration.AddDatabaseOverride(DatabaseOverrideFile.PathFor(builder.Configuration), DatabaseOverrideFile.KeysPath(builder.Configuration));
        return applicationFile;
    }

    // The first start with no configuration at all (no password for the application account in a private file, in the environment or anywhere else): the predefined accounts
    // are used with the passwords the installation gave them (Assets/Data/database-accounts.json), so the application can reach the database; the administrator is
    // asked to change them (Setari -> Baza de date). With any configuration present nothing is added, so an installation that works keeps working as it is.
    public static void AddFirstRunAccounts(this WebApplicationBuilder builder)
    {
        if (!string.IsNullOrEmpty(builder.Configuration[DatabaseAccounts.Find(DatabaseAccounts.ApplicationKey)!.PasswordKey])) return;
        var values = new Dictionary<string, string?>();
        foreach (var account in DatabaseAccounts.All)
        {
            values[account.UserKey] = account.User;
            values[account.PasswordKey] = account.DefaultPassword;
        }
        builder.Configuration.AddInMemoryCollection(values);
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
        // A file this process may not read (on a server the file of the migrator account is kept for the administrators and the updater only) is an account that is not
        // configured here, not a reason for the application not to start.
        string text;
        try { text = File.ReadAllText(path); }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException) { return; }
        using var file = JsonDocument.Parse(text);
        if (!file.RootElement.TryGetProperty("Database", out var section)) return;
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"Database:{account}User"] = section.TryGetProperty("User", out var user) ? user.GetString() : null,
            [$"Database:{account}Password"] = section.TryGetProperty("Password", out var password) ? password.GetString() : null
        });
    }
}
