using BlazorStoc.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace BlazorStoc.Checks;

// Setari -> Baza de date (stage 1): the rules of the typed values, the override file and its place in the configuration, the choice of the port the
// application listens on, the access reserved to the configured administrator and the parts of the service that need no database (refusals, the
// change of the application port, the journal). The paths that open a connection (test, save, state) run against MariaDB in MariaExtendedChecks.
public static class DatabaseSettingsChecks
{
    private sealed class NoLocks : IOperationLockService
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

            // Access: only the configured administrator. A database user with the Administrator role is refused everywhere.
            var databaseAdministrator = Create(new TestAccessControl(true, "contabil", configuredAdministrator: false));
            var limited = Create(new TestAccessControl(false, "operator"));
            foreach (var (label, service) in new[] { ("an administrator from the database", databaseAdministrator), ("a limited user", limited) })
            {
                check(await Throws<AccessDeniedException>(() => service.GetAsync()) && await Throws<AccessDeniedException>(() => service.TestAsync(Input()))
                      && await Throws<AccessDeniedException>(() => service.SaveAsync(Input(appPort: 5099), "administrator-password"))
                      && await Throws<AccessDeniedException>(() => service.ResetToBaseAsync("administrator-password")),
                    $"Database settings: {label} cannot read, test, save or reset the connection");
            }
            check(!File.Exists(path), "Database settings: a refused request writes nothing");

            var admin = Create(new TestAccessControl(true, "admin", configuredAdministrator: true));
            var settings = await admin.GetAsync();
            check(settings.Host == "127.0.0.1" && settings.Port == 1 && settings.User == "u" && settings.HasPassword && !settings.OverrideActive && settings.OverridePath == path,
                "Database settings: the configured administrator reads the values in force (without the password)");

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

            check(await Throws<DatabaseSettingsException>(() => Create(new TestAccessControl(true, "admin", configuredAdministrator: true), _ => false)
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

            // The unreachable server: the test says so, and a save with a changed connection is refused with nothing written.
            var test = await admin.TestAsync(Input(port: 1));
            check(!test.Ok && test.Steps[0] is { Name: "Conectare", Level: DatabaseStepLevel.Error } && !test.Steps[0].Detail.Contains("pw-base"),
                "Database settings: the test of an unreachable server fails at the connection and never shows the password");
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
