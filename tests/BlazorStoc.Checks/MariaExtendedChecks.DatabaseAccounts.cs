using BlazorStoc.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;

namespace BlazorStoc.Checks;

public static partial class MariaExtendedChecks
{
    // ---- Setari -> Baza de date -> Conturile bazei ----------------------------------------------------------------------------------
    // A throw-away account is made with the account that administers the server (admin.private.cnf), put in a catalog of one account that reads the keys of the backup
    // account, and taken through a change of password, an interrupted change, a reset, an export and an import; the real accounts of the application are never touched.

    private static async Task DatabaseAccountsAsync(IConfiguration baseConfiguration, IAuditTrail audit)
    {
        var adminPath = Environment.GetEnvironmentVariable("MARIA_ADMIN_CNF") is { Length: > 0 } fromEnvironment ? fromEnvironment : @"C:\Dev\BlazorStoc-MariaDB\admin.private.cnf";
        if (!File.Exists(adminPath)) { Console.WriteLine("  (skipped: no admin.private.cnf; set MARIA_ADMIN_CNF)"); return; }
        var directory = Path.Combine(Path.GetTempPath(), "blazorstoc-accounts-it-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var user = "bs_chk_" + Guid.NewGuid().ToString("N")[..10];
        const string defaultPassword = "Default-Password-For-Check-2026";
        var definition = new DatabaseAccountDefinition("backup", user, user, "Backup", "Cont de verificare", defaultPassword, [new AccountGrant("blazorstoc_test", ["SELECT"])]);
        var path = Path.Combine(directory, "override.json");
        var keys = Path.Combine(directory, "keys");
        var values = new Dictionary<string, string?>
        {
            ["Database:Host"] = baseConfiguration["Database:Host"], ["Database:Port"] = baseConfiguration["Database:Port"], ["Database:SslMode"] = baseConfiguration["Database:SslMode"] ?? "Required",
            ["Database:Name"] = baseConfiguration["Database:Name"], ["Database:BackupUser"] = user, ["Database:BackupPassword"] = defaultPassword, ["Database:OverridePath"] = path,
            ["Authentication:Username"] = "admin", ["Authentication:Password"] = "administrator-password", ["Database:AdminConfigPath"] = adminPath
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).AddDatabaseOverride(path, keys).Build();
        var protection = DataProtectionProvider.Create(new DirectoryInfo(keys), options => options.SetApplicationName("BlazorStoc"));
        var admin = DatabaseAdministrationAccount.Read(configuration)!;
        var started = DateTime.UtcNow.AddSeconds(-1);

        async Task AsRoot(string sql, params (string Name, object Value)[] parameters)
        {
            await using var connection = new MySqlConnection(new MySqlConnectionStringBuilder { Server = admin.Host, Port = (uint)admin.Port, UserID = admin.User, Password = admin.Password, SslMode = MySqlSslMode.Preferred, Pooling = false }.ConnectionString);
            await connection.OpenAsync();
            await using var command = new MySqlCommand(sql, connection);
            foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
            await command.ExecuteNonQueryAsync();
        }
        async Task<bool> Connects(string password)
        {
            try
            {
                await using var connection = new MySqlConnection(new MySqlConnectionStringBuilder { Server = admin.Host, Port = (uint)admin.Port, UserID = user, Password = password, SslMode = MySqlSslMode.Required, Pooling = false }.ConnectionString);
                await connection.OpenAsync();
                return true;
            }
            catch (MySqlException) { return false; }
        }
        DatabaseAccountService NewService(IConfiguration configurationToUse, bool administrator = true) => new(configurationToUse, protection, new TestAccessControl(administrator, "admin"), audit,
            new FileOperationLockService(Path.Combine(directory, "lock.json"), audit, NullLogger<FileOperationLockService>.Instance), NullLogger<DatabaseAccountService>.Instance, null, [definition]);

        try
        {
            await AsRoot($"CREATE USER '{user}'@'127.0.0.1' IDENTIFIED BY @p REQUIRE SSL", ("@p", defaultPassword));
            await AsRoot($"GRANT SELECT ON `blazorstoc\\_test`.* TO '{user}'@'127.0.0.1'");
            var service = NewService(configuration);

            var first = (await service.GetStatesAsync()).Single();
            Check(first.Connects && first.RightsMatch && first.UsesDefaultPassword && first.User == user && !first.Healthy,
                "Database accounts: a new account connects, has exactly its rights and is reported as still on the default password");
            Check((await Rejects<AccessDeniedException>(() => NewService(configuration, administrator: false).GetStatesAsync(), "A limited user cannot read the accounts")) is not null, "Database accounts: only administrators read the accounts");
            Check(await service.AnyDefaultPasswordAsync(), "Database accounts: the check for the default password finds it");

            // A change of password: refused with a wrong confirmation or a short password, done with the right one.
            var newPassword = DatabaseAccounts.GeneratePassword();
            var refusedConfirmation = await Rejects<DatabaseSettingsException>(() => service.ChangePasswordsAsync(new Dictionary<string, string> { ["backup"] = newPassword }, "gresita"), "A wrong confirmation changes nothing");
            var refusedShort = await Rejects<DatabaseSettingsException>(() => service.ChangePasswordsAsync(new Dictionary<string, string> { ["backup"] = "scurta" }, "administrator-password"), "A short password changes nothing");
            Check(refusedConfirmation is not null && refusedShort is not null && await Connects(defaultPassword) && !File.Exists(path), "Database accounts: a wrong confirmation or a short password changes nothing, on the server or in the file");
            AdminPasswordGate.Succeeded("admin");
            var changed = await service.ChangePasswordsAsync(new Dictionary<string, string> { ["backup"] = newPassword }, "administrator-password");
            Check(changed.Success && changed.Changes.Single().Success && await Connects(newPassword) && !await Connects(defaultPassword) && configuration["Database:BackupPassword"] == newPassword,
                "Database accounts: the password is changed on the server and in the configuration, and the old one no longer works");
            var after = (await service.GetStatesAsync()).Single();
            Check(after.Connects && !after.UsesDefaultPassword && after.Healthy && after.ChangedUtc is { } at && at > started && !await service.AnyDefaultPasswordAsync(),
                "Database accounts: after the change the account connects, is no longer on the default password and its date of change is known");
            Check(!File.ReadAllText(path).Contains(newPassword, StringComparison.Ordinal) && DatabaseOverrideFile.Read(path)!.Accounts!["backup"].PendingPasswordProtected is null,
                "Database accounts: the override file holds the new password only protected and no password in waiting is left");
            var same = await service.ChangePasswordsAsync(new Dictionary<string, string> { ["backup"] = newPassword }, "administrator-password");
            Check(!same.Success && same.Changes.Single().Message.Contains("aceeași", StringComparison.Ordinal) && await Connects(newPassword), "Database accounts: the same password again is not a change");

            // An interrupted change: the server already has the password that was written as pending; the next start puts it in use.
            var interrupted = DatabaseAccounts.GeneratePassword();
            await AsRoot($"ALTER USER '{user}'@'127.0.0.1' IDENTIFIED BY @p", ("@p", interrupted));
            var protector = protection.CreateProtector(DatabaseOverrideFile.ProtectorPurpose);
            var file = DatabaseOverrideFile.Read(path)!;
            var map = new Dictionary<string, AccountOverride>(file.Accounts!) { ["backup"] = file.Accounts!["backup"] with { PendingPasswordProtected = protector.Protect(interrupted) } };
            DatabaseOverrideFile.Write(path, file with { Accounts = map });
            configuration.Reload();
            Check(!(await service.GetStatesAsync()).Single().Connects, "Database accounts: the interruption is seen: the password in use is refused by the server");
            await service.RecoverPendingAsync();
            Check(configuration["Database:BackupPassword"] == interrupted && (await service.GetStatesAsync()).Single().Connects && DatabaseOverrideFile.Read(path)!.Accounts!["backup"].PendingPasswordProtected is null,
                "Database accounts: an interrupted change is completed at the next start: the new password is put in use and the pending one is dropped");

            // The reset: the account is missing, then has a right in excess; the administration account puts it back and says what it did.
            await AsRoot($"DROP USER '{user}'@'127.0.0.1'");
            var missing = (await service.GetStatesAsync()).Single();
            Check(!missing.Connects, "Database accounts: a missing account does not connect");
            AdminPasswordGate.Succeeded("admin");
            var chosen = DatabaseAccounts.GeneratePassword();
            var reset = await service.ResetAsync(new Dictionary<string, string> { ["backup"] = chosen }, useDefaults: false, "administrator-password");
            Check(reset.Success && reset.Changes.Single().Success && await Connects(chosen) && configuration["Database:BackupPassword"] == chosen && reset.States.Single().Healthy,
                "Database accounts: the reset recreates a missing account with the rights of the catalog and the chosen password, and the connection is checked");
            await AsRoot($"GRANT INSERT ON `blazorstoc\\_test`.* TO '{user}'@'127.0.0.1'");
            var excess = (await service.GetStatesAsync()).Single();
            Check(excess.Connects && excess.ExtraRights.Contains("INSERT pe blazorstoc_test") && !excess.Healthy, "Database accounts: a right in excess is reported");
            AdminPasswordGate.Succeeded("admin");
            var back = await service.ResetAsync(null, useDefaults: false, "administrator-password");
            var generatedPassword = configuration["Database:BackupPassword"]!;
            Check(back.Success && back.States.Single().RightsMatch && generatedPassword != chosen && await Connects(generatedPassword) && generatedPassword.Length == 24,
                "Database accounts: the reset takes the rights in excess away and, without a chosen password, generates a new one");
            AdminPasswordGate.Succeeded("admin");
            var withDefaults = await service.ResetAsync(null, useDefaults: true, "administrator-password");
            Check(withDefaults.Success && await Connects(defaultPassword) && (await service.GetStatesAsync()).Single().UsesDefaultPassword && await service.AnyDefaultPasswordAsync(),
                "Database accounts: the reset can put the default password back, which the check for the default password sees");
            AdminPasswordGate.Succeeded("admin");
            var chosenAgain = DatabaseAccounts.GeneratePassword();
            Check((await service.ChangePasswordsAsync(new Dictionary<string, string> { ["backup"] = chosenAgain }, "administrator-password")).Success, "Database accounts: the account can be changed again after a reset");

            // Export and import: the file holds the account and the administration account, readable only with the pass phrase; the import puts the password back after the file of the override was lost.
            AdminPasswordGate.Succeeded("admin");
            var exported = await service.ExportAsync("o fraza lunga si secreta", includeAdministration: true, "administrator-password");
            var decrypted = DatabaseConfigurationFile.Decrypt(exported.Content, "o fraza lunga si secreta");
            Check(exported.IncludesAdministration && decrypted.Accounts.Single() is { Password: var exportedPassword, User: var exportedUser } && exportedPassword == chosenAgain && exportedUser == user
                  && decrypted.Administration is { Password: var rootPassword } && rootPassword == admin.Password && exported.FileName.EndsWith(".bsconfig", StringComparison.Ordinal),
                "Database accounts: the export holds the account and the administration account with their passwords, encrypted with the pass phrase");
            AdminPasswordGate.Succeeded("admin");
            var withoutAdmin = await service.ExportAsync("o fraza lunga si secreta", includeAdministration: false, "administrator-password");
            Check(!withoutAdmin.IncludesAdministration && DatabaseConfigurationFile.Decrypt(withoutAdmin.Content, "o fraza lunga si secreta").Administration is null, "Database accounts: the export can leave the administration account out");
            Check(await Rejects<DatabaseSettingsException>(() => service.ExportAsync("scurta", true, "administrator-password"), "a short pass phrase") is not null
                  && await Rejects<DatabaseSettingsException>(() => service.ExportAsync("o fraza lunga si secreta", true, "gresita"), "a wrong confirmation") is not null,
                "Database accounts: an export needs a pass phrase of 12 characters and the confirmation of the administrator");
            File.Delete(path);
            configuration.Reload();
            Check(configuration["Database:BackupPassword"] == defaultPassword && !(await service.GetStatesAsync()).Single().Connects, "Database accounts: with the override file lost the application falls back to a password the server refuses");
            AdminPasswordGate.Succeeded("admin");
            var imported = await service.ImportAsync(exported.Content, "o fraza lunga si secreta", "administrator-password");
            Check(imported.Success && configuration["Database:BackupPassword"] == chosenAgain && (await service.GetStatesAsync()).Single().Connects,
                "Database accounts: the import puts the password of the file back and the account connects again");
            Check(await Rejects<DatabaseSettingsException>(() => service.ImportAsync(exported.Content, "alta fraza, la fel de lunga", "administrator-password"), "a wrong pass phrase") is not null,
                "Database accounts: an import with a wrong pass phrase changes nothing");

            var events = (await audit.GetEventsAsync()).Where(item => item.TimestampUtc >= started && item.Action is
                AuditActions.ChangeDatabaseAccountPassword or AuditActions.ResetDatabaseAccounts or AuditActions.ExportDatabaseConfiguration or AuditActions.ImportDatabaseConfiguration or AuditActions.VerifyDatabaseAccounts).ToList();
            Check(events.Count >= 5 && events.All(item => !item.Details.Contains(newPassword, StringComparison.Ordinal) && !item.Details.Contains(chosen, StringComparison.Ordinal) && !item.Details.Contains(chosenAgain, StringComparison.Ordinal)
                      && !item.Details.Contains(admin.Password, StringComparison.Ordinal) && !item.Details.Contains(generatedPassword, StringComparison.Ordinal)),
                "Database accounts: every change, reset, export and import is journaled and no journal entry holds a password");
        }
        finally
        {
            try { await AsRoot($"DROP USER IF EXISTS '{user}'@'127.0.0.1'"); } catch (MySqlException) { }
            try { Directory.Delete(directory, true); } catch (IOException) { }
        }
    }
}
