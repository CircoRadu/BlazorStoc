using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using BlazorStoc.Startup;
using Microsoft.AspNetCore.Components;
using BlazorStoc.Components.Shared;
using BlazorStoc.Services;
using Bunit;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace BlazorStoc.Checks;

// Setari -> Baza de date (stage 1): the rules of the typed values, the override file and its place in the configuration, the choice of the port the
// application listens on, the access of the Administrator role and the parts of the service that need no database (refusals, the
// change of the application port, the journal). The paths that open a connection (test, save, state) run against MariaDB in MariaExtendedChecks.
public static class DatabaseSettingsChecks
{
    internal sealed class NoLocks : IOperationLockService
    {
        public Task<IOperationLockHandle?> TryAcquireAsync(string operation, string operatorName, string operatorRole, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The lock must not be taken when only the application port changes.");
        public Task<OperationLockInfo?> GetActiveAsync(CancellationToken cancellationToken = default) => Task.FromResult<OperationLockInfo?>(null);
    }

    private static DatabaseConnectionInput Input(string host = "127.0.0.1", int port = 3307, string user = "blazorstoc_dev", string? password = null,
        string ssl = "Required", int? appPort = null, bool all = false) => new(host, port, user, password, ssl, appPort, all);

    public static async Task RunAsync(Action<bool, string> check)
    {
        Console.WriteLine("=== Setări → Bază de date ===");
        Rules(check);
        OverrideFile(check);
        Hosting(check);
        await ServiceAsync(check);
        DataRoot(check);
        await Sessions(check);
        Accounts(check);
        await GateAsync(check);
        Dialogs(check);
        await DatabaseServerChecks.RunAsync(check);
    }

    private static void Rules(Action<bool, string> check)
    {
        check(DatabaseConnectionRules.Validate(Input()) is null && DatabaseConnectionRules.Validate(Input("db-server.local", 3306, "app", "x", "VerifyFull")) is null,
            "Database settings: ordinary values are accepted");
        check(DatabaseConnectionRules.Validate(Input("")) == DatabaseConnectionRules.HostMessage && DatabaseConnectionRules.Validate(Input("bad host")) == DatabaseConnectionRules.HostMessage
              && DatabaseConnectionRules.Validate(Input("a;b")) == DatabaseConnectionRules.HostMessage && DatabaseConnectionRules.Validate(Input(new string('a', 254))) == DatabaseConnectionRules.HostMessage,
            "Database settings: an empty host, spaces, forbidden characters or an over-long name are refused");
        check(DatabaseConnectionRules.Validate(Input(port: 0)) == DatabaseConnectionRules.PortMessage && DatabaseConnectionRules.Validate(Input(port: 70000)) == DatabaseConnectionRules.PortMessage,
            "Database settings: the port of the database is between 1 and 65535");
        check(DatabaseConnectionRules.Validate(Input(user: " ")) == DatabaseConnectionRules.UserMessage && DatabaseConnectionRules.Validate(Input(password: new string('x', 201))) == DatabaseConnectionRules.PasswordMessage,
            "Database settings: the user is required, the password has a limit");
        check(DatabaseConnectionRules.Validate(Input(ssl: "None")) is null && DatabaseConnectionRules.Validate(Input("localhost", ssl: "Preferred")) is null
              && DatabaseConnectionRules.Validate(Input("10.0.0.5", ssl: "None")) == DatabaseConnectionRules.SslMessage
              && DatabaseConnectionRules.Validate(Input("db.example", ssl: "Preferred")) == DatabaseConnectionRules.SslMessage
              && DatabaseConnectionRules.Validate(Input(ssl: "Sometimes")) == DatabaseConnectionRules.SslUnknownMessage,
            "Database settings: a server on another machine needs an encrypted connection; the SSL mode must be a known one");
        check(DatabaseConnectionRules.Validate(Input(appPort: 80)) == DatabaseConnectionRules.AppPortMessage && DatabaseConnectionRules.Validate(Input(appPort: 5087)) is null
              && DatabaseConnectionRules.Validate(Input(appPort: 65536)) == DatabaseConnectionRules.AppPortMessage,
            "Database settings: the port of the application is between 1024 and 65535, or empty");
        check(DatabaseConnectionRules.IsLoopback("localhost") && DatabaseConnectionRules.IsLoopback("127.0.0.1") && DatabaseConnectionRules.IsLoopback("::1") && DatabaseConnectionRules.IsLoopback("[::1]")
              && !DatabaseConnectionRules.IsLoopback("192.168.1.5") && !DatabaseConnectionRules.IsLoopback("nas"),
            "Database settings: loopback addresses are recognised");
        var builder = DatabaseConnectionRules.TestBuilder("h", 3306, "BlazorStoc", "u", "p", "Required");
        check(!builder.Pooling && !builder.AllowLoadLocalInfile && builder.ConnectionTimeout == 10,
            "Database settings: a test connection uses no pool, no LOAD LOCAL INFILE and a short timeout");
        var described = DatabaseConnectionRules.Describe(new System.Net.Sockets.SocketException(), "db", 3307, "BlazorStoc")
            + DatabaseConnectionRules.Describe(new InvalidOperationException("password=secret123"), "db", 3307, "BlazorStoc");
        check(!described.Contains("secret123") && described.Contains("db:3307"), "Database settings: the description of a failure names the server and never repeats the message of an unknown error");

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Authentication:Password"] = "correct horse battery" }).Build();
        check(DatabaseConnectionRules.PasswordMatches(configuration, "correct horse battery") && !DatabaseConnectionRules.PasswordMatches(configuration, "correct horse batter")
              && !DatabaseConnectionRules.PasswordMatches(configuration, "") && !DatabaseConnectionRules.PasswordMatches(configuration, null),
            "Database settings: the administrator password is compared with the configured one");
    }

    private static void OverrideFile(Action<bool, string> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "blazorstoc-dbsettings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var keys = Path.Combine(directory, "keys");
            var path = Path.Combine(directory, "override.json");
            var provider = DataProtectionProvider.Create(new DirectoryInfo(keys), options => options.SetApplicationName("BlazorStoc"));
            var protector = provider.CreateProtector(DatabaseOverrideFile.ProtectorPurpose);
            check(DatabaseOverrideFile.Read(path) is null && DatabaseOverrideFile.ReadRaw(path) is null, "Override file: missing = no override");

            DatabaseOverrideFile.Write(path, new("db2", 3310, "app2", "Required", protector.Protect("Sup3r-Secret-Pw"), 5099, true));
            var text = File.ReadAllText(path);
            check(!text.Contains("Sup3r-Secret-Pw") && text.Contains("db2") && !File.Exists(path + ".tmp"),
                "Override file: written atomically, the password is not in clear");
            var read = DatabaseOverrideFile.Read(path);
            check(read is { Host: "db2", Port: 3310, User: "app2", AppPort: 5099, AppBindAll: true } && protector.Unprotect(read.PasswordProtected!) == "Sup3r-Secret-Pw",
                "Override file: read back, the password decrypts");

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Database:Host"] = "base-host", ["Database:Port"] = "3307", ["Database:User"] = "base", ["Database:Password"] = "base-pw", ["Database:Name"] = "BlazorStoc" })
                .AddDatabaseOverride(path, keys).Build();
            check(configuration["Database:Host"] == "db2" && configuration.GetValue<int>("Database:Port") == 3310 && configuration["Database:User"] == "app2"
                  && configuration["Database:Password"] == "Sup3r-Secret-Pw" && configuration["Database:Name"] == "BlazorStoc" && configuration.GetValue("Database:OverrideActive", false),
                "Override file: its values win over the base configuration, the rest stays");
            check(configuration.GetValue<int?>("Hosting:AppPort") == 5099 && configuration.GetValue("Hosting:AppBindAll", false), "Override file: the application port reaches the configuration");

            // Only the port of the application: the database keys stay those of the base.
            DatabaseOverrideFile.Write(path, new(null, null, null, null, null, 5100, false));
            (configuration as IConfigurationRoot)!.Reload();
            check(configuration["Database:Host"] == "base-host" && configuration["Database:Password"] == "base-pw" && configuration.GetValue<int?>("Hosting:AppPort") == 5100,
                "Override file: a file with the application port only leaves the database configuration alone");

            // Keys changed: the password cannot be read, so the database part is ignored (the application still starts) and the problem is reported.
            var other = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(directory, "other-keys")), options => options.SetApplicationName("BlazorStoc")).CreateProtector(DatabaseOverrideFile.ProtectorPurpose);
            DatabaseOverrideFile.Write(path, new("db3", 3311, "app3", "Required", other.Protect("x"), null, null));
            (configuration as IConfigurationRoot)!.Reload();
            check(configuration["Database:Host"] == "base-host" && configuration["Database:OverrideProblem"] is { Length: > 0 } && !configuration.GetValue("Database:OverrideActive", false),
                "Override file: a password that cannot be decrypted disables the database part and reports it");

            File.WriteAllText(path, "{ not json");
            (configuration as IConfigurationRoot)!.Reload();
            check(DatabaseOverrideFile.Read(path) is null && configuration["Database:Host"] == "base-host", "Override file: a damaged file is ignored");

            var before = File.ReadAllBytes(path);
            DatabaseOverrideFile.Write(path, new("x", 1, "y", "Required", null, null, null));
            DatabaseOverrideFile.Restore(path, before);
            check(File.ReadAllBytes(path).SequenceEqual(before), "Override file: Restore puts the previous content back");
            DatabaseOverrideFile.Restore(path, null);
            check(!File.Exists(path), "Override file: Restore with nothing removes the file");
        }
        finally { Directory.Delete(directory, true); }
    }

    // The folder of the data (App:DataRoot) and the configuration the worker processes build: they must see what the administrator saved in the page.
    private static void DataRoot(Action<bool, string> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "blazorstoc-dataroot-" + Guid.NewGuid().ToString("N"));
        try
        {
            var configured = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["App:DataRoot"] = directory }).Build();
            check(BlazorStoc.Startup.AppDataRoot.Resolve(configured) == Path.GetFullPath(directory)
                  && BlazorStoc.Startup.AppDataRoot.Combine(configured, "assets", "x") == Path.Combine(Path.GetFullPath(directory), "assets", "x") && !BlazorStoc.Startup.AppDataRoot.UsesLegacyFolder(configured),
                "Data root: App:DataRoot decides the folder of the data");
            check(DatabaseOverrideFile.PathFor(configured) == Path.Combine(Path.GetFullPath(directory), "database-connection.override.json")
                  && DatabaseOverrideFile.KeysPath(configured) == Path.Combine(Path.GetFullPath(directory), "assets", "data-protection-keys")
                  && MariaAssetPaths.DatabaseBackups(configured) == Path.Combine(Path.GetFullPath(directory), "assets", "database-backups"),
                "Data root: the override file, the keys and the packages all follow it");
            Directory.CreateDirectory(directory);
            DatabaseOverrideFile.Write(DatabaseOverrideFile.PathFor(configured), new DatabaseOverrideData("db.example.test", 4406, null, null, null, null, null));
            var worker = BlazorStoc.Modules.Backup.BackupWorker.BuildConfiguration([$"--App:DataRoot={directory}"]);
            check(worker["Database:Host"] == "db.example.test" && worker["Database:Port"] == "4406",
                "Data root: the backup worker builds its configuration with the override file, so it works with what the administrator saved");
        }
        finally { try { Directory.Delete(directory, true); } catch (IOException) { } }
    }

    // Setari -> Baza de date -> Conexiune -> Conturile bazei: the catalog, the passwords, the rights read from SHOW GRANTS, the file kept by the administrator, the override file.
    private static void Accounts(Action<bool, string> check)
    {
        var all = DatabaseAccounts.All;
        check(all.Count == 4 && all.Select(account => account.Key).SequenceEqual(["app", "migrator", "backup", "restore"]) && all.Select(account => account.User).Distinct().Count() == 4
              && all.All(account => account.User.StartsWith("ESPGestiuneStoc_", StringComparison.Ordinal) && DatabaseAccounts.IsSafeUserName(account.User) && DatabaseAccounts.IsSafeUserName(account.LegacyUser))
              && all.Select(account => account.DefaultPassword).Distinct().Count() == 4 && all.All(account => DatabaseAccounts.ValidatePassword(account.DefaultPassword) is null),
            "Database accounts: the catalog has the four predefined accounts with fixed names, different default passwords of enough length");
        check(DatabaseAccounts.Find("app")!.UserKey == "Database:User" && DatabaseAccounts.Find("backup")!.PasswordKey == "Database:BackupPassword" && DatabaseAccounts.Find("RESTORE")!.UserKey == "Database:RestoreUser" && DatabaseAccounts.Find("x") is null,
            "Database accounts: every account is read from its own configuration keys");
        check(DatabaseAccounts.ValidatePassword("scurta") == DatabaseAccounts.TooShortMessage && DatabaseAccounts.ValidatePassword(new string('a', 101)) == DatabaseAccounts.TooLongMessage
              && DatabaseAccounts.ValidatePassword("o parola destul de lunga") is null && DatabaseAccounts.ValidatePassword("parola\ncu control") == DatabaseAccounts.ControlCharactersMessage,
            "Database accounts: a password has 16 to 100 characters and no control characters");
        var generated = Enumerable.Range(0, 50).Select(_ => DatabaseAccounts.GeneratePassword()).ToList();
        check(generated.Distinct().Count() == 50 && generated.All(item => item.Length == 24 && DatabaseAccounts.ValidatePassword(item) is null && item.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.')),
            "Database accounts: a generated password is long, different every time and has nothing a shell or SQL treats specially");

        var app = DatabaseAccounts.Find("app")!;
        var restore = DatabaseAccounts.Find("restore")!;
        var good = DatabaseAccounts.CompareGrants(app, ["GRANT SELECT, INSERT, UPDATE, DELETE ON `BlazorStoc`.* TO `ESPGestiuneStoc_app`@`127.0.0.1` IDENTIFIED BY PASSWORD '*ABC' REQUIRE SSL", "GRANT USAGE ON *.* TO `ESPGestiuneStoc_app`@`127.0.0.1` REQUIRE SSL"]);
        var lacking = DatabaseAccounts.CompareGrants(app, ["GRANT SELECT, INSERT ON `BlazorStoc`.* TO `u`@`h`"]);
        var excess = DatabaseAccounts.CompareGrants(app, ["GRANT SELECT, INSERT, UPDATE, DELETE, DROP ON `BlazorStoc`.* TO `u`@`h`", "GRANT SELECT ON `mysql`.* TO `u`@`h`", "GRANT FILE ON *.* TO `u`@`h`"]);
        check(good.Matches && !lacking.Matches && lacking.Missing.SequenceEqual(["UPDATE pe BlazorStoc", "DELETE pe BlazorStoc"]) && lacking.Extra.Count == 0
              && excess.Missing.Count == 0 && excess.Extra.Contains("DROP pe BlazorStoc") && excess.Extra.Contains("SELECT pe mysql") && excess.Extra.Contains("FILE pe *.*"),
            "Database accounts: the rights read from SHOW GRANTS are compared with the catalog (missing, in excess, on another schema, global)");
        var restoreGrants = DatabaseAccounts.CompareGrants(restore, ["GRANT ALL PRIVILEGES ON `BlazorStoc\\_bak`.* TO `r`@`h`", "GRANT ALL PRIVILEGES ON `BlazorStoc\\_old`.* TO `r`@`h`",
            "GRANT SELECT, INSERT, ALTER, CREATE, DROP, TRIGGER ON `BlazorStoc`.* TO `r`@`h`"]);
        check(restoreGrants.Matches, "Database accounts: ALL PRIVILEGES and the escaped underscore of a schema name are understood");

        // Rights on single tables (what a migration gives to a table it creates) are fine when the catalog gives the right on the schema; development databases are not judged.
        var tables = DatabaseAccounts.CompareGrants(app, ["GRANT SELECT, INSERT, UPDATE, DELETE ON `blazorstoc`.* TO `u`@`h`", "GRANT SELECT, INSERT ON `blazorstoc`.`backup_settings` TO `u`@`h`",
            "GRANT SELECT, INSERT, UPDATE, DELETE, ALTER ON `blazorstoc_test`.* TO `u`@`h`", "GRANT SELECT ON `blazorstoc_demo`.`products` TO `u`@`h`"]);
        var tablesExcess = DatabaseAccounts.CompareGrants(app, ["GRANT SELECT, INSERT, UPDATE, DELETE ON `blazorstoc`.* TO `u`@`h`", "GRANT DROP ON `blazorstoc`.`products` TO `u`@`h`"]);
        check(tables.Matches && !tablesExcess.Matches && tablesExcess.Extra.SequenceEqual(["DROP pe blazorstoc.products"]) && DatabaseAccounts.DevelopmentDatabases.Contains("blazorstoc_test"),
            "Database accounts: a right on a table the catalog gives on the schema, and any right on a development database, are not reported; another right on a table is");

        var content = new DatabaseConfigurationFile.Content(new DateTime(2026, 10, 20, 8, 0, 0, DateTimeKind.Utc), "ana", new("127.0.0.1", 3307, "BlazorStoc", "Required"),
            [new("app", "ESPGestiuneStoc_app", "Accesul aplicației la date", "parola-aplicatie-123"), new("backup", "ESPGestiuneStoc_backup", "Copiile de siguranță", "parola-backup-456")], new("127.0.0.1", 3307, "root", "parola-root-789"));
        var file = DatabaseConfigurationFile.Encrypt(content, "o fraza lunga si secreta");
        var text = System.Text.Encoding.UTF8.GetString(file);
        check(!text.Contains("parola-", StringComparison.Ordinal) && !text.Contains("root", StringComparison.Ordinal) && !text.Contains("ESPGestiuneStoc", StringComparison.Ordinal),
            "Database configuration file: nothing of it (no password, no account name, no root) is readable without the pass phrase");
        var back = DatabaseConfigurationFile.Decrypt(file, "o fraza lunga si secreta");
        check(back.Accounts.Count == 2 && back.Accounts[0].Password == "parola-aplicatie-123" && back.Administration is { User: "root", Password: "parola-root-789" } && back.Server.Port == 3307 && back.CreatedBy == "ana",
            "Database configuration file: with the pass phrase it gives back the accounts, the server and the administration account");
        check(Throws<DatabaseSettingsException>(() => { DatabaseConfigurationFile.Decrypt(file, "alta fraza, la fel de lunga"); return Task.CompletedTask; }).GetAwaiter().GetResult()
              && Throws<DatabaseSettingsException>(() => { DatabaseConfigurationFile.Decrypt([.. file.Take(file.Length - 10), 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20], "o fraza lunga si secreta"); return Task.CompletedTask; }).GetAwaiter().GetResult()
              && Throws<DatabaseSettingsException>(() => { DatabaseConfigurationFile.Decrypt(System.Text.Encoding.UTF8.GetBytes("nu e fisier"), "o fraza lunga si secreta"); return Task.CompletedTask; }).GetAwaiter().GetResult()
              && DatabaseConfigurationFile.ValidatePassphrase("scurta") == DatabaseConfigurationFile.PassphraseTooShortMessage && DatabaseConfigurationFile.ValidatePassphrase("o fraza lunga si secreta") is null,
            "Database configuration file: a wrong pass phrase, a damaged file or another kind of file is refused, and the pass phrase needs 12 characters");
        check(DatabaseConfigurationFile.FileNameFor(new DateTime(2026, 10, 20, 8, 5, 0, DateTimeKind.Utc)).EndsWith(".bsconfig", StringComparison.Ordinal), "Database configuration file: the file has its own extension");

        // The override file: the passwords saved for the accounts reach the configuration (and a pending one is kept apart); the application account saved there wins over the connection form.
        var directory = Path.Combine(Path.GetTempPath(), "blazorstoc-accounts-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "override.json");
            var keys = Path.Combine(directory, "keys");
            var protection = Microsoft.AspNetCore.DataProtection.DataProtectionProvider.Create(new DirectoryInfo(keys), options => options.SetApplicationName("BlazorStoc"));
            var protector = Microsoft.AspNetCore.DataProtection.DataProtectionCommonExtensions.CreateProtector(protection, DatabaseOverrideFile.ProtectorPurpose);
            var changed = new DateTime(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc);
            DatabaseOverrideFile.Write(path, new DatabaseOverrideData(null, null, null, null, null, null, null, new Dictionary<string, AccountOverride>
            {
                ["backup"] = new("ESPGestiuneStoc_backup", protector.Protect("parola-backup-noua-1234"), null, changed),
                ["app"] = new(null, protector.Protect("parola-app-noua-12345"), protector.Protect("parola-app-in-asteptare"), changed)
            }));
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Database:Password"] = "din-fisierul-privat", ["Database:BackupPassword"] = "din-fisierul-privat" }).AddDatabaseOverride(path, keys).Build();
            check(configuration["Database:BackupPassword"] == "parola-backup-noua-1234" && configuration["Database:BackupUser"] == "ESPGestiuneStoc_backup" && configuration["Database:Password"] == "parola-app-noua-12345"
                  && configuration["Database:Accounts:app:PendingPassword"] == "parola-app-in-asteptare" && configuration["Database:Accounts:backup:ChangedUtc"] is { Length: > 0 },
                "Database accounts: the passwords saved for the accounts win over the private files, a pending one is kept apart and the date of the change is known");
            check(DatabaseOverrideFile.Read(path)!.Accounts!.Count == 2 && !File.ReadAllText(path).Contains("parola-", StringComparison.Ordinal), "Database accounts: the override file holds the passwords only protected");
        }
        finally { try { Directory.Delete(directory, true); } catch (IOException) { } }
    }

    internal sealed class FakeAccounts(bool anyDefault, bool fails = false, IReadOnlyList<DatabaseAccountState>? states = null) : IDatabaseAccountService
    {
        public bool HasAdministrationAccount => true;
        public Task<bool> AnyDefaultPasswordAsync(CancellationToken cancellationToken = default) => fails ? Task.FromException<bool>(new InvalidOperationException("down")) : Task.FromResult(anyDefault);
        public Task<IReadOnlyList<DatabaseAccountState>> GetStatesAsync(CancellationToken cancellationToken = default) => states is null ? throw new NotSupportedException() : Task.FromResult(states);
        public Task<DatabaseAccountsResult> VerifyAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DatabaseAccountsResult> ChangePasswordsAsync(IReadOnlyDictionary<string, string> newPasswords, string administratorPassword, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DatabaseAccountsResult> ResetAsync(IReadOnlyDictionary<string, string>? passwords, bool useDefaults, string administratorPassword, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DatabaseConfigurationExport> ExportAsync(string passphrase, bool includeAdministration, string administratorPassword, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DatabaseAccountsResult> ImportAsync(byte[] file, string passphrase, string administratorPassword, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RecoverPendingAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeSettings(string? refusal = null) : IDatabaseSettingsService
    {
        public int Saves { get; private set; }
        public string? LastPassword { get; private set; }
        public Task<DatabaseConnectionSettings> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(new DatabaseConnectionSettings("127.0.0.1", 3307, "BlazorStoc", "u", "Required", true, null, false, false, null, "x.json"));
        public Task<DatabaseTestResult> TestAsync(DatabaseConnectionInput input, CancellationToken cancellationToken = default) => Task.FromResult(new DatabaseTestResult([]));
        public Task<DatabaseSaveResult> SaveAsync(DatabaseConnectionInput input, string administratorPassword, CancellationToken cancellationToken = default)
        {
            LastPassword = administratorPassword;
            if (refusal is not null) throw new DatabaseSettingsException(refusal);
            Saves++;
            return Task.FromResult(new DatabaseSaveResult(true, "Salvat."));
        }
        public Task<DatabaseSaveResult> ResetToBaseAsync(string administratorPassword, CancellationToken cancellationToken = default) => Task.FromResult(new DatabaseSaveResult(true, "Revenit."));
        public Task ApplyServerPortAsync(int port, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    // The password of the administrator and the pass phrases are asked in a window when the operation is started, not in fields that stay on the page; what is typed twice says
    // at once whether the two coincide.
    private static void Dialogs(Action<bool, string> check)
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var confirmed = 0; var cancelled = 0; string? typed = null;
        var dialog = context.Render<AdminOperationDialog>(parameters => parameters.Add(item => item.Title, "Confirmă ceva").Add(item => item.Intro, "Explicație").Add(item => item.ConfirmText, "Fă-o")
            .Add(item => item.Confirmed, EventCallback.Factory.Create(new object(), () => confirmed++)).Add(item => item.Cancelled, EventCallback.Factory.Create(new object(), () => cancelled++))
            .Add(item => item.AdminPasswordChanged, EventCallback.Factory.Create<string?>(new object(), (string? value) => typed = value)));
        check(dialog.Find("h2").TextContent == "Confirmă ceva" && dialog.FindAll("#admin-operation-password").Count == 1 && dialog.FindAll("button.help-icon").Count == 0,
            "Windows with a password: the window asks for the password of the administrator and, being a confirmation, has no help button of its own");
        dialog.Find("#admin-operation-password").Input("parola");
        dialog.Find("button.primary").Click();
        dialog.Find("#admin-operation-password").KeyDown(new KeyboardEventArgs { Key = "Enter" });
        dialog.Find(".delete-overlay").KeyDown(new KeyboardEventArgs { Key = "Escape" });
        check(typed == "parola" && confirmed == 2 && cancelled == 1, "Windows with a password: the button and Enter confirm, Escape cancels, and the password typed reaches the page");
        var disabled = context.Render<AdminOperationDialog>(parameters => parameters.Add(item => item.Title, "T").Add(item => item.ConfirmDisabled, true).Add(item => item.Error, "Parola nu este corectă.").Add(item => item.Help, (Microsoft.AspNetCore.Components.RenderFragment)(builder => { builder.OpenComponent<HelpButton>(0); builder.AddAttribute(1, "Topic", "setari-baza-conturi-operatii"); builder.AddAttribute(2, "About", "Operațiile"); builder.CloseComponent(); })));
        check(disabled.Find("button.primary").HasAttribute("disabled") && disabled.Markup.Contains("Parola nu este corectă.", StringComparison.Ordinal) && disabled.FindAll("button.help-icon").Count == 1,
            "Windows with a password: the confirmation can be disabled, the refusal of the service is shown inside and a help topic gives the help button");

        IReadOnlyList<DatabaseAccountState> states = [.. DatabaseAccounts.All.Select(account => new DatabaseAccountState(account.Key, account.Purpose, account.User, account.User, true, true, null, false, [], [], null))];
        using var panelContext = new BunitContext();
        panelContext.JSInterop.Mode = JSRuntimeMode.Loose;
        panelContext.Services.AddLogging();
        panelContext.Services.AddSingleton<IDatabaseAccountService>(new FakeAccounts(false, states: states));
        var panel = panelContext.Render<DatabaseAccountsPanel>();
        check(panel.FindAll("#accounts-admin-password").Count == 0 && panel.FindAll("#admin-operation-password").Count == 0 && !panel.Markup.Contains("Parola contului tău de administrator", StringComparison.Ordinal)
              && panel.FindAll("tbody tr").Count == 4 && panel.Markup.Contains("conectat", StringComparison.Ordinal),
            "Windows with a password: the page of the accounts has no field for the password of the administrator or for a pass phrase until an operation is started");
        panel.FindAll("button").First(button => button.TextContent.Trim() == "Exportă configurarea").Click();
        check(panel.FindAll("#admin-operation-password").Count == 1 && panel.FindAll("#accounts-passphrase").Count == 1 && panel.FindAll("button.help-icon").Count == 1
              && panel.Find(".admin-operation-dialog button.primary").HasAttribute("disabled"),
            "Windows with a password: starting an export opens the window with the pass phrase twice and the password of the administrator, and the export waits for them");
        panel.Find("#accounts-passphrase").Input("o fraza lunga si secreta");
        panel.Find("#accounts-passphrase-2").Input("o fraza lunga si secret");
        check(panel.Markup.Contains("Frazele-parolă nu coincid.", StringComparison.Ordinal) && panel.Find(".admin-operation-dialog button.primary").HasAttribute("disabled")
              && panel.Find(".password-match").ClassList.Contains("bad"), "Windows with a password: while the pass phrase is repeated the page says the two do not coincide and the export stays closed");
        panel.Find("#accounts-passphrase-2").Input("o fraza lunga si secreta");
        check(panel.Markup.Contains("Frazele-parolă coincid.", StringComparison.Ordinal) && !panel.Find(".admin-operation-dialog button.primary").HasAttribute("disabled") && panel.Find(".password-match").ClassList.Contains("ok"),
            "Windows with a password: when the two pass phrases coincide the page says so and the export can go on");
        panel.FindAll(".admin-operation-dialog button").First(button => button.TextContent.Trim() == "Renunță").Click();
        var connectionContext = new BunitContext();
        connectionContext.JSInterop.Mode = JSRuntimeMode.Loose;
        connectionContext.Services.AddLogging();
        var settings = new FakeSettings("Parola contului de administrator nu este corectă.");
        connectionContext.Services.AddSingleton<IDatabaseSettingsService>(settings);
        var connection = connectionContext.Render<DatabaseConnectionPanel>();
        check(connection.FindAll("#db-admin-password").Count == 0 && connection.FindAll("#admin-operation-password").Count == 0 && !connection.Markup.Contains("Confirmare", StringComparison.Ordinal),
            "Windows with a password: the connection page has no field for the password of the administrator until the save is started");
        connection.FindAll("button.primary").First(button => button.TextContent.Trim() == "Salvează și aplică").Click();
        check(connection.FindAll(".admin-operation-dialog").Count == 1 && connection.FindAll("#admin-operation-password").Count == 1 && connection.FindAll(".admin-operation-dialog button.help-icon").Count == 0,
            "Windows with a password: saving the connection opens a window that asks for the password of the administrator (a confirmation, without help button)");
        connection.Find("#admin-operation-password").Input("gresita");
        connection.Find(".admin-operation-dialog button.primary").Click();
        check(connection.FindAll(".admin-operation-dialog").Count == 1 && connection.Markup.Contains("nu este corectă", StringComparison.Ordinal) && settings.LastPassword == "gresita" && settings.Saves == 0,
            "Windows with a password: a refusal of the service stays inside the window, which stays open for another try");
        var accepted = new FakeSettings();
        using var acceptedContext = new BunitContext();
        acceptedContext.JSInterop.Mode = JSRuntimeMode.Loose;
        acceptedContext.Services.AddLogging();
        acceptedContext.Services.AddSingleton<IDatabaseSettingsService>(accepted);
        var accepting = acceptedContext.Render<DatabaseConnectionPanel>();
        accepting.FindAll("button.primary").First(button => button.TextContent.Trim() == "Salvează și aplică").Click();
        accepting.Find("#admin-operation-password").Input("buna");
        accepting.Find(".admin-operation-dialog button.primary").Click();
        check(accepting.FindAll(".admin-operation-dialog").Count == 0 && accepted.Saves == 1 && accepted.LastPassword == "buna" && accepting.Markup.Contains("Salvat.", StringComparison.Ordinal),
            "Windows with a password: when the save goes through the window closes and the page shows the result");
        connectionContext.Dispose();
        panel.FindAll(".row-icon.edit")[0].Click();
        panel.Find("#accounts-new-password").Input("o parola noua de 24 de caractere");
        panel.Find("#accounts-new-password-2").Input("alta parola noua de 24 de caractere");
        check(panel.Markup.Contains("Parolele nu coincid.", StringComparison.Ordinal) && panel.Find(".admin-operation-dialog button.primary").HasAttribute("disabled")
              && panel.FindAll("#accounts-passphrase").Count == 0, "Windows with a password: a new password asked twice is checked the same way, in its own window");
    }

    // While an account still has its default password the administrator is taken to the settings; nobody else, no other kind of request and no other state is held.
    private static async Task GateAsync(Action<bool, string> check)
    {
        check(DefaultPasswordGate.IsAllowedPath("/setari") && DefaultPasswordGate.IsAllowedPath("/Setari/") && DefaultPasswordGate.IsAllowedPath("/Account/Login") && DefaultPasswordGate.IsAllowedPath("/_blazor/negotiate")
              && DefaultPasswordGate.IsAllowedPath("/app.css") && DefaultPasswordGate.IsAllowedPath("/ajutor/fereastra") && DefaultPasswordGate.IsAllowedPath("/api/session/ping")
              && !DefaultPasswordGate.IsAllowedPath("/") && !DefaultPasswordGate.IsAllowedPath("/produse") && !DefaultPasswordGate.IsAllowedPath("/inventar") && !DefaultPasswordGate.IsAllowedPath(null) && !DefaultPasswordGate.IsAllowedPath("/setari-altceva"),
            "Default password gate: the settings, the sign-in, the help, the static files and the connection of the pages stay open; the other pages do not");

        async Task<(int Status, string? Location, bool Reached)> Call(bool anyDefault, string role, string path, string method = "GET", string accept = "text/html", bool fails = false, bool authenticated = true)
        {
            var services = new ServiceCollection().AddSingleton<IDatabaseAccountService>(new FakeAccounts(anyDefault, fails)).BuildServiceProvider();
            var reached = false;
            var builder = new ApplicationBuilder(services);
            builder.UseDefaultPasswordGate();
            builder.Run(_ => { reached = true; return Task.CompletedTask; });
            var pipeline = builder.Build();
            var context = new DefaultHttpContext { RequestServices = services };
            context.Request.Method = method;
            context.Request.Path = path;
            context.Request.Headers.Accept = accept;
            if (authenticated) context.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity([new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "ana"), new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, role)], "test"));
            await pipeline!(context);
            return (context.Response.StatusCode, context.Response.Headers.Location.ToString(), reached);
        }
        var held = await Call(true, AccessRoles.Administrator, "/produse");
        check(held.Status == 302 && held.Location == DefaultPasswordGate.TargetUrl && !held.Reached, "Default password gate: an administrator who asks for a page is taken to the connection settings while a default password is in use");
        var free = await Call(false, AccessRoles.Administrator, "/produse");
        var other = await Call(true, AccessRoles.LimitedUser, "/produse");
        var ajax = await Call(true, AccessRoles.Administrator, "/produse", method: "POST");
        var json = await Call(true, AccessRoles.Administrator, "/produse", accept: "application/json");
        var open = await Call(true, AccessRoles.Administrator, "/setari");
        var anonymous = await Call(true, AccessRoles.Administrator, "/produse", authenticated: false);
        check(free.Reached && other.Reached && ajax.Reached && json.Reached && open.Reached && anonymous.Reached,
            "Default password gate: nothing is held without a default password, for a user who is not an administrator, for a request that is not a page, on the settings, or without a sign-in");
        var down = await Call(true, AccessRoles.Administrator, "/produse", fails: true);
        check(down.Reached, "Default password gate: a check that cannot be done (the database does not answer) never locks the administrator out");
    }

    // Setari -> Baza de date -> Stare: the open sessions with their details.
    private static async Task Sessions(Action<bool, string> check)
    {
        var registry = new OpenSessionRegistry();
        registry.Opened("c1"); registry.Opened("c2");
        check(registry.Count == 2 && registry.Snapshot().All(session => session.UserName is null && session.Connected), "Open sessions: a circuit is listed as soon as it opens, without a user");
        registry.Identify("c1", "ana", "Ana Pop", "Administrator", "/produse");
        registry.Identify("c2", "ion", "Ion Ene", "Utilizator", "/setari");
        check(registry.SetNetwork("c1", "192.168.1.20", "Edge · Windows", "ana") && registry.Snapshot().First(session => session.Id == "c1") is { IpAddress: "192.168.1.20", Browser: "Edge · Windows", Page: "/produse" },
            "Open sessions: the address and the browser are kept for the user the session belongs to");
        check(!registry.SetNetwork("c2", "10.0.0.9", "Chrome · Windows", "ana") && !registry.SetNetwork("nu-exista", "10.0.0.9", "x", "ana") && registry.Snapshot().First(session => session.Id == "c2").IpAddress is null,
            "Open sessions: another user, or a session that does not exist, cannot write the details");
        registry.SetPage("c2", "/inventar");
        registry.ConnectionChanged("c2", false);
        check(registry.Snapshot().First(session => session.Id == "c2") is { Page: "/inventar", Connected: false }, "Open sessions: the page and the state of the connection follow the session");
        registry.Closed("c1");
        check(registry.Count == 1 && registry.Snapshot()[0].Id == "c2", "Open sessions: a closed session leaves the list");
        check(UserAgentText.Summarize("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36 Edg/130.0") == "Edge · Windows"
              && UserAgentText.Summarize("Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 Chrome/130.0 Mobile Safari/537.36") == "Chrome · Android" && UserAgentText.Summarize("") == "necunoscut",
            "Open sessions: the browser is shown in a short form");

        // Closing a session: the page of the session registers what sends its browser to the closing address; the address works once, with the token given at that moment.
        string? receivedToken = null;
        registry.Identify("c2", "ion", "Ion Ene", "Utilizator", "/inventar");
        check(!registry.CanClose("c2") && !await registry.RequestCloseAsync("c2"), "Open sessions: a session whose page has not registered cannot be closed from the list");
        registry.RegisterCloser("c2", token => { receivedToken = token; return Task.CompletedTask; });
        check(registry.CanClose("c2") && await registry.RequestCloseAsync("c2") && receivedToken is { Length: 32 }, "Open sessions: closing a session gives its page a one-time token");
        check(registry.ConsumeCloseToken(receivedToken) && !registry.ConsumeCloseToken(receivedToken) && !registry.ConsumeCloseToken("altceva") && !registry.ConsumeCloseToken(null),
            "Open sessions: the token of the closing address works once and only the one given");
        var audit = new TestAuditTrail();
        var closer = new OpenSessionRegistry();
        closer.Opened("a1"); closer.Opened("a2");
        closer.Identify("a1", "ana", "Ana", "Administrator", "/setari"); closer.Identify("a2", "ion", "Ion", "Utilizator", "/produse");
        closer.SetNetwork("a2", "10.1.1.7", "Chrome · Windows", "ion");
        var closedBy = 0;
        closer.RegisterCloser("a2", _ => { closedBy++; return Task.CompletedTask; });
        var statusAdmin = new DatabaseStatusService(new ConfigurationBuilder().Build(), new TestAccessControl(true, "ana"), null, AppHostingInfo.None, new CircuitCounter(), null, null, closer, audit);
        var ownRefused = await Throws<DatabaseSettingsException>(() => statusAdmin.CloseSessionAsync("a1", "a1"));
        var closedOk = await statusAdmin.CloseSessionAsync("a2", "a1");
        var missing = await statusAdmin.CloseSessionAsync("nu-exista", "a1");
        check(ownRefused && closedOk && closedBy == 1 && !missing, "Open sessions: an administrator closes the session of another user, not the own one, and a missing session is reported");
        check(audit.Entries.Count == 1 && audit.Entries[0].Action == AuditActions.CloseOpenSession && audit.Entries[0].Details.Contains("ion", StringComparison.Ordinal) && audit.Entries[0].Details.Contains("10.1.1.7", StringComparison.Ordinal),
            "Open sessions: closing a session is journaled with the user, the address and the browser");
        check(closer.Snapshot().All(session => session.Id != "a2") && (await statusAdmin.ReadApplicationAsync()) is { Sessions: 1, DisconnectedSessions: 0 },
            "Open sessions: a closed session leaves the list and the count at once, without waiting for its circuit to end");
        closer.ConnectionChanged("a1", false);
        check((await statusAdmin.ReadApplicationAsync()) is { Sessions: 0, DisconnectedSessions: 1 }, "Open sessions: the count is of the connected sessions; the ones waiting to reconnect are counted apart");
        closer.ConnectionChanged("a1", true);
        var statusLimited = new DatabaseStatusService(new ConfigurationBuilder().Build(), new TestAccessControl(false, "ion"), null, AppHostingInfo.None, new CircuitCounter(), null, null, closer, audit);
        check(await Throws<AccessDeniedException>(() => statusLimited.CloseSessionAsync("a1", null)) && await Throws<AccessDeniedException>(() => statusLimited.ReadSessionsAsync()),
            "Open sessions: only an administrator sees the list and closes sessions");

        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var now = DateTime.UtcNow;
        IReadOnlyList<OpenSession> list =
        [
            new("c1", "ana", "Ana Pop", "Administrator", "192.168.1.20", "Edge · Windows", now.AddMinutes(-5), now, "/setari", true),
            new("c2", null, null, null, null, null, now, now, null, false)
        ];
        var dialog = context.Render<OpenSessionsDialog>(parameters => parameters.Add(item => item.Sessions, list).Add(item => item.CurrentId, "c1"));
        var text = dialog.Markup;
        check(dialog.FindAll("tbody tr").Count == 2 && text.Contains("192.168.1.20", StringComparison.Ordinal) && text.Contains("Edge · Windows", StringComparison.Ordinal) && text.Contains("/setari", StringComparison.Ordinal)
              && dialog.FindAll("tr.own-session").Count == 1 && text.Contains("sesiunea ta", StringComparison.Ordinal) && text.Contains("neautentificat", StringComparison.Ordinal) && text.Contains("deconectată", StringComparison.Ordinal),
            "Open sessions: the window lists user, address, browser, page, marks the own session and the ones not signed in or disconnected");
        string? closeRequested = null;
        var withClose = context.Render<OpenSessionsDialog>(parameters => parameters.Add(item => item.Sessions, list).Add(item => item.CurrentId, "c1")
            .Add(item => item.CloseSession, EventCallback.Factory.Create<string>(new object(), (string id) => closeRequested = id)));
        var icons = withClose.FindAll("button.row-icon.close");
        check(icons.Count == 2 && icons[0].HasAttribute("disabled") && !icons[1].HasAttribute("disabled"), "Open sessions: every row has a close icon, disabled for the own session");
        icons[1].Click();
        check(withClose.Markup.Contains("Confirmare închidere sesiune", StringComparison.Ordinal) && closeRequested is null, "Open sessions: closing asks for confirmation first");
        withClose.FindAll(".confirm-bar button").First(button => button.TextContent.Contains("Închide sesiunea", StringComparison.Ordinal)).Click();
        check(closeRequested == "c2" && !withClose.Markup.Contains("Confirmare închidere sesiune", StringComparison.Ordinal), "Open sessions: after the confirmation the page is asked to close that session");
        var empty = context.Render<OpenSessionsDialog>(parameters => parameters.Add(item => item.Sessions, []));
        check(empty.Markup.Contains("Nicio sesiune deschisă", StringComparison.Ordinal), "Open sessions: an empty list says so");
    }

    private static void Hosting(Action<bool, string> check)
    {
        IConfiguration Config(params (string, string)[] values) => new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(pair => pair.Item1, pair => (string?)pair.Item2)).Build();
        var none = AppHosting.Resolve(Config(), _ => true);
        check(none.ConfiguredPort is null && none.AppliedUrls is null && none.Note is null, "Application port: nothing saved = the host keeps its own addresses");
        var applied = AppHosting.Resolve(Config(("Hosting:AppPort", "5099")), _ => true);
        check(applied.AppliedUrls == "http://127.0.0.1:5099" && applied.ConfiguredPort == 5099 && applied.Note is null, "Application port: a saved port is applied on the local interface");
        var all = AppHosting.Resolve(Config(("Hosting:AppPort", "5099"), ("Hosting:AppBindAll", "true")), _ => true);
        check(all.AppliedUrls == "http://0.0.0.0:5099" && all.BindAll, "Application port: all interfaces only when chosen");
        var imposed = AppHosting.Resolve(Config(("Hosting:AppPort", "5099"), ("urls", "http://127.0.0.1:5087")), _ => true);
        check(imposed.AppliedUrls is null && imposed.Note is { Length: > 0 }, "Application port: addresses imposed at start win, and the page says so");
        var busy = AppHosting.Resolve(Config(("Hosting:AppPort", "5099")), _ => false);
        check(busy.AppliedUrls is null && busy.Note is { Length: > 0 } && busy.ConfiguredPort == 5099, "Application port: a busy port is not used, and the page says so");
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var taken = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        check(!AppHosting.CanBind(taken), "Application port: CanBind refuses a port that is in use");
        listener.Stop();
        check(AppHosting.CanBind(taken), "Application port: CanBind accepts a free port");
    }

    private sealed class FakeAccountAuthenticator(string username, string password) : IUserAuthenticator
    {
        public Task<AuthenticationResult> AuthenticateAsync(string user, string typed, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AuthenticationResult(user == username && typed == password ? AuthenticationStatus.Success : AuthenticationStatus.InvalidCredentials));
    }

    private static async Task ServiceAsync(Action<bool, string> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "blazorstoc-dbsettings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "override.json");
            var values = new Dictionary<string, string?>
            {
                ["Database:Host"] = "127.0.0.1", ["Database:Port"] = "1", ["Database:Name"] = "BlazorStoc", ["Database:User"] = "u", ["Database:Password"] = "pw-base",
                ["Database:OverridePath"] = path, ["Authentication:Password"] = "administrator-password"
            };
            var keys = Path.Combine(directory, "keys");
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).AddDatabaseOverride(path, keys).Build();
            var protection = DataProtectionProvider.Create(new DirectoryInfo(keys), options => options.SetApplicationName("BlazorStoc"));
            var audit = new TestAuditTrail();
            IDatabaseSettingsService Create(IAccessControl access, Func<int, bool>? canBind = null) =>
                new DatabaseSettingsService(configuration, protection, access, audit, new NoLocks(), AppHostingInfo.None, NullLogger<DatabaseSettingsService>.Instance, canBind ?? (_ => true));

            // Access: the Administrator role (decision 4); a user without it is refused everywhere.
            var limited = Create(new TestAccessControl(false, "operator"));
            check(await Throws<AccessDeniedException>(() => limited.GetAsync()) && await Throws<AccessDeniedException>(() => limited.TestAsync(Input()))
                  && await Throws<AccessDeniedException>(() => limited.SaveAsync(Input(appPort: 5099), "administrator-password"))
                  && await Throws<AccessDeniedException>(() => limited.ResetToBaseAsync("administrator-password")),
                "Database settings: a limited user cannot read, test, save or reset the connection");
            check(!File.Exists(path), "Database settings: a refused request writes nothing");

            var admin = Create(new TestAccessControl(true, "admin"));
            var settings = await admin.GetAsync();
            check(settings.Host == "127.0.0.1" && settings.Port == 1 && settings.User == "u" && settings.HasPassword && !settings.OverrideActive && settings.OverridePath == path,
                "Database settings: an administrator reads the values in force (without the password)");

            check(await Throws<DatabaseSettingsException>(() => admin.SaveAsync(Input(appPort: 5099), ""), DatabaseConnectionRules.AdminPasswordRequiredMessage),
                "Database settings: saving needs the password of the administrator");
            check(await Throws<DatabaseSettingsException>(() => admin.SaveAsync(Input(appPort: 5099), "wrong"), DatabaseConnectionRules.AdminPasswordMessage) && !File.Exists(path),
                "Database settings: a wrong administrator password saves nothing");
            for (var attempt = 0; attempt < 6; attempt++) await Throws<DatabaseSettingsException>(() => admin.SaveAsync(Input(appPort: 5099), "wrong"));
            check(await Throws<DatabaseSettingsException>(() => admin.SaveAsync(Input(appPort: 5099), "administrator-password"), DatabaseConnectionRules.TooManyAttemptsMessage),
                "Database settings: after several wrong passwords even the right one is refused for a minute");
            AdminPasswordGate.Succeeded("admin");

            check(await Throws<DatabaseSettingsException>(() => admin.SaveAsync(Input(host: "bad host"), "administrator-password"), DatabaseConnectionRules.HostMessage),
                "Database settings: invalid values are refused before anything is tried");

            var noChange = await admin.SaveAsync(Input(port: 1, user: "u"), "administrator-password");
            check(noChange.Success && !File.Exists(path) && audit.Entries.Count == 0, "Database settings: saving the values already in force changes and journals nothing");

            // An administrator from the database confirms with the password of the own account, not with the one of the private file.
            var accountAdmin = new DatabaseSettingsService(configuration, protection, new TestAccessControl(true, "contabil"), audit, new NoLocks(), AppHostingInfo.None,
                NullLogger<DatabaseSettingsService>.Instance, null, new FakeAccountAuthenticator("contabil", "contabil-pw"));
            check(await Throws<DatabaseSettingsException>(() => accountAdmin.SaveAsync(Input(port: 1, user: "u"), "administrator-password"), DatabaseConnectionRules.AdminPasswordMessage),
                "Database settings: an administrator from the database cannot confirm with the password of the private file");
            check((await accountAdmin.SaveAsync(Input(port: 1, user: "u"), "contabil-pw")).Success,
                "Database settings: an administrator from the database confirms with the password of the own account");

            check(await Throws<DatabaseSettingsException>(() => Create(new TestAccessControl(true, "admin"), _ => false)
                    .SaveAsync(Input(port: 1, user: "u", appPort: 5099), "administrator-password"), "ocupat"),
                "Database settings: a busy application port is refused");

            // Only the application port changes: no connection is opened, no lock is taken, the file and the journal are written.
            var portOnly = await admin.SaveAsync(Input(port: 1, user: "u", appPort: 5099, all: true), "administrator-password");
            var file = DatabaseOverrideFile.Read(path);
            check(portOnly is { Success: true, RestartNeeded: true } && file is { AppPort: 5099, AppBindAll: true, Host: null, PasswordProtected: null },
                "Database settings: changing only the application port writes only that, and says a restart is needed");
            var entry = audit.Entries.SingleOrDefault();
            check(entry is { EntityType: AuditEntities.Database, Action: AuditActions.SetAppPort, ActorUsername: "admin" } && entry.Details.Contains("5099") && entry.Details.Contains("toate interfețele"),
                "Database settings: the port change is journaled with the old and the new value");

            // The port the application already listens on is not "busy" for itself (a server whose start script set the port can still choose the interface); another busy port stays refused.
            var listening = new DatabaseSettingsService(configuration, protection, new TestAccessControl(true, "admin"), audit, new NoLocks(), new AppHostingInfo(null, true, "http://0.0.0.0:5098", null),
                NullLogger<DatabaseSettingsService>.Instance, _ => false);
            check((await listening.SaveAsync(Input(port: 1, user: "u", appPort: 5098, all: true), "administrator-password")).Success,
                "Database settings: the port the application already listens on can be saved even though it is taken (by the application itself)");
            check(await Throws<DatabaseSettingsException>(() => listening.SaveAsync(Input(port: 1, user: "u", appPort: 5097, all: true), "administrator-password"), "ocupat"),
                "Database settings: another taken port is still refused");
            await admin.SaveAsync(Input(port: 1, user: "u", appPort: 5099, all: true), "administrator-password");
            audit.Entries.Clear();

            // The unreachable server: the test says so, and a save with a changed connection is refused with nothing written.
            var test = await admin.TestAsync(Input(port: 1));
            check(!test.Ok && test.Steps[0] is { Name: "Conectare", Level: DatabaseStepLevel.Error } && !test.Steps[0].Detail.Contains("pw-base"),
                "Database settings: the test of an unreachable server fails at the connection and never shows the password");
            check(test.Steps[0].Detail.Contains("nu răspunde la 127.0.0.1:1", StringComparison.Ordinal) && !test.Steps[0].Detail.Contains("cod ", StringComparison.Ordinal),
                "Database settings: a port where nothing listens is explained with the address and what to check, not with an error code");
            var before = File.ReadAllBytes(path);
            var refused = await admin.SaveAsync(Input(port: 2, user: "u", password: "new-secret-pw", appPort: 5099, all: true), "administrator-password");
            check(refused is { Success: false } && File.ReadAllBytes(path).SequenceEqual(before) && refused.Message.Contains("nimic nu a fost salvat"),
                "Database settings: a connection that cannot be tested is not saved");
            check(audit.Entries.All(item => !item.Details.Contains("new-secret-pw") && !item.Details.Contains("pw-base") && !item.Target.Contains("pw-base")),
                "Database settings: no journal entry contains a password");
            check(audit.Entries.Any(item => item.Action == AuditActions.TestDatabaseConnection), "Database settings: the test is journaled");

            // Reset to the base configuration takes the same lock as a change (the success path runs on MariaDB).
            var reset = await Throws<InvalidOperationException>(() => admin.ResetToBaseAsync("administrator-password"));
            check(reset, "Database settings: reset takes the same lock as a change (checked with a lock service that refuses)");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static async Task<bool> Throws<TException>(Func<Task> action, string? messagePart = null) where TException : Exception
    {
        try { await action(); return false; }
        catch (TException exception) { return messagePart is null || exception.Message.Contains(messagePart, StringComparison.Ordinal); }
    }
}
