using BlazorStoc.Components.Shared;
using BlazorStoc.Services;
using Bunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace BlazorStoc.Checks;

// Setari -> Baza de date -> Conexiune -> Serverul bazei de date and Aplicatia (Services/DatabaseServer.cs): the lines of my.ini, the rules of when the server can be controlled
// from the page, the sequences of stop / start / restart / change of the port with a fake Windows service (including the way back when the new port does not work and the
// port that is taken by another program), the restart of the application and the two cards. The real service is checked on this computer (docs/TESTE_RAMASE.md).
public static class DatabaseServerChecks
{
    private const string Ini = "[client]\r\nport=3306\r\n\r\n[mysqld]\r\nbasedir=C:/Dev/Maria\r\nport=3307\r\nbind-address=127.0.0.1\r\nlog-error=C:/Dev/Maria/server-error.log\r\n";

    private sealed class FakeServices(string iniPath, string imagePath, ServerServiceStatus start = ServerServiceStatus.Running) : IServerServices
    {
        public ServerServiceStatus State { get; private set; } = start;
        public List<string> Calls { get; } = [];
        public int? Listening { get; private set; }
        public HashSet<int> NeverStartsOn { get; } = [];
        public ServerServiceStatus Status(string name) => name == "BlazorStocMariaDB" ? State : ServerServiceStatus.NotFound;
        public string? ImagePath(string name) => name == "BlazorStocMariaDB" ? imagePath : null;
        public Task StartAsync(string name, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Calls.Add("start");
            State = ServerServiceStatus.Running;
            var port = MyIni.ReadPort(File.ReadAllText(iniPath))!.Value;
            Listening = NeverStartsOn.Contains(port) ? null : port;
            return Task.CompletedTask;
        }
        public Task StopAsync(string name, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Calls.Add("stop");
            State = ServerServiceStatus.Stopped;
            Listening = null;
            return Task.CompletedTask;
        }
        public void Reach(int port) => Listening = port;
    }

    private sealed class FakePorts : IPortInspector
    {
        public Dictionary<int, PortOccupant> Taken { get; } = [];
        public PortOccupant? Occupant(int port) => Taken.GetValueOrDefault(port);
        public bool IsFree(int port) => !Taken.ContainsKey(port);
        public int? FindFree(int start, params int[] avoid) { for (var port = start; port < start + 50; port++) if (!Taken.ContainsKey(port) && !avoid.Contains(port)) return port; return null; }
    }

    private sealed class FakeRestart(bool supported) : IApplicationRestart
    {
        public int Requests { get; private set; }
        public bool Supported => supported;
        public void Request(string reason) => Requests++;
    }

    private sealed class FakeLocks : IOperationLockService
    {
        public bool Held { get; set; }
        public List<string> Operations { get; } = [];
        private sealed class Handle : IOperationLockHandle { public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
        public Task<IOperationLockHandle?> TryAcquireAsync(string operation, string operatorName, string operatorRole, CancellationToken cancellationToken = default)
        {
            Operations.Add(operation);
            return Task.FromResult<IOperationLockHandle?>(Held ? null : new Handle());
        }
        public Task<OperationLockInfo?> GetActiveAsync(CancellationToken cancellationToken = default) => Task.FromResult<OperationLockInfo?>(null);
    }

    private sealed class RecordingSettings : IDatabaseSettingsService
    {
        public List<int> AppliedPorts { get; } = [];
        public Task<DatabaseConnectionSettings> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(new DatabaseConnectionSettings("127.0.0.1", 3307, "BlazorStoc", "u", "Required", true, 5087, false, false, null, "x.json"));
        public Task<DatabaseTestResult> TestAsync(DatabaseConnectionInput input, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DatabaseSaveResult> SaveAsync(DatabaseConnectionInput input, string administratorPassword, CancellationToken cancellationToken = default) => Task.FromResult(new DatabaseSaveResult(true, "Salvat."));
        public Task<DatabaseSaveResult> ResetToBaseAsync(string administratorPassword, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ApplyServerPortAsync(int port, CancellationToken cancellationToken = default) { AppliedPorts.Add(port); return Task.CompletedTask; }
    }

    private sealed class Setup : IDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "blazorstoc-dbserver-" + Guid.NewGuid().ToString("N"));
        public string IniPath { get; }
        public FakeServices Services { get; }
        public FakePorts Ports { get; } = new();
        public FakeRestart Restart { get; }
        public FakeLocks Locks { get; } = new();
        public RecordingSettings Settings { get; } = new();
        public TestAuditTrail Audit { get; } = new();
        public IDatabaseServerService Server { get; }

        public Setup(bool administrator = true, bool local = true, int connectionPort = 3307, bool restartSupported = true, ServerServiceStatus state = ServerServiceStatus.Running, string ini = Ini)
        {
            System.IO.Directory.CreateDirectory(Directory);
            IniPath = Path.Combine(Directory, "my.ini");
            File.WriteAllText(IniPath, ini);
            Services = new(IniPath, $"\"C:\\Dev\\Maria\\bin\\mariadbd.exe\" \"--defaults-file={IniPath}\" \"BlazorStocMariaDB\"", state);
            if (state == ServerServiceStatus.Running) Services.Reach(MyIni.ReadPort(ini) ?? 3307);
            Restart = new(restartSupported);
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Host"] = "127.0.0.1", ["Database:Port"] = connectionPort.ToString(), ["Database:Name"] = "BlazorStoc", ["Database:User"] = "u", ["Database:Password"] = "pw",
                ["Database:AdminConfigPath"] = Path.Combine(Directory, "admin.private.cnf"), ["Authentication:Password"] = "administrator-password", ["Hosting:AppPort"] = "5087",
            }).Build();
            Server = new DatabaseServerService(configuration, new TestAccessControl(administrator, "admin"), Audit, Locks, Settings, Services, Ports, Restart, NullLogger<DatabaseServerService>.Instance,
                verify: (_, port, _) => Task.FromResult<string?>(Services.Listening == port ? null : "Serverul nu răspunde."), isLocalHost: _ => local, readyTimeout: TimeSpan.FromMilliseconds(200),
                reach: (_, port, _) => Task.FromResult(Services.Listening == port));
        }

        public string CurrentIni => File.ReadAllText(IniPath);
        public void Dispose() { try { System.IO.Directory.Delete(Directory, true); } catch (IOException) { } }
    }

    public static async Task RunAsync(Action<bool, string> check)
    {
        Console.WriteLine("=== Setări → Bază de date → Serverul bazei și aplicația ===");
        IniFile(check);
        Rules(check);
        await SequencesAsync(check);
        Hosting(check);
        Cards(check);
    }

    private static void IniFile(Action<bool, string> check)
    {
        check(MyIni.ReadPort(Ini) == 3307, "my.ini: the port is read from the server section, not from the client one");
        check(MyIni.ReadPort("[mysqld]\nport = 4000 # comment\n") == 4000 && MyIni.ReadPort("[client]\nport=1\n") is null, "my.ini: spaces and a trailing comment are accepted, no server port reads as null");
        var changed = MyIni.WithPort(Ini, 3320);
        check(changed.Contains("port=3320\r\n", StringComparison.Ordinal) && changed.Contains("[client]\r\nport=3306", StringComparison.Ordinal) && MyIni.ReadPort(changed) == 3320
              && changed.Replace("3320", "3307", StringComparison.Ordinal) == Ini, "my.ini: only the line of the server section changes and nothing else, with the line endings kept");
        var added = MyIni.WithPort("[mysqld]\nbasedir=x\n", 3330);
        check(MyIni.ReadPort(added) == 3330 && added.StartsWith("[mysqld]\nport=3330\n", StringComparison.Ordinal), "my.ini: a missing port is added under the server section");
        check(MyIni.ReadPort(MyIni.WithPort("", 3331)) == 3331, "my.ini: an empty file gets a server section with the port");
        check(MyIni.DefaultsFile("\"C:\\Dev\\Maria\\bin\\mariadbd.exe\" \"--defaults-file=C:\\Dev\\My Maria\\my.ini\" \"BlazorStocMariaDB\"") == "C:\\Dev\\My Maria\\my.ini"
              && MyIni.DefaultsFile("C:\\m\\mariadbd.exe --defaults-file=C:\\m\\my.ini Svc") == "C:\\m\\my.ini" && MyIni.DefaultsFile("C:\\m\\mariadbd.exe Svc") is null && MyIni.DefaultsFile(null) is null,
            "my.ini: the file is found in the command line of the service (quoted or not), or reported as missing");
        check(MyIni.ReadValue(Ini, "log-error") == "C:/Dev/Maria/server-error.log" && MyIni.ReadValue(Ini, "nothing") is null, "my.ini: other values of the server section are read (the error log)");
    }

    private static void Rules(Action<bool, string> check)
    {
        var ports = new FakePorts();
        ports.Taken[4000] = new(77, "chrome");
        check(DatabaseServerRules.ValidatePort(80, 3307, 5087, ports) is { } && DatabaseServerRules.ValidatePort(70000, 3307, 5087, ports) is { }, "Server port: outside 1024-65535 is refused");
        check(DatabaseServerRules.ValidatePort(3307, 3307, 5087, ports) is { } && DatabaseServerRules.ValidatePort(5087, 3307, 5087, ports) is { } m && m.Contains("aplicației", StringComparison.Ordinal),
            "Server port: the current port and the port of the application are refused");
        check(DatabaseServerRules.ValidatePort(4000, 3307, 5087, ports) is { } taken && taken.Contains("chrome", StringComparison.Ordinal) && taken.Contains("77", StringComparison.Ordinal),
            "Server port: a port taken by another program is refused and the program is named");
        check(DatabaseServerRules.ValidatePort(3308, 3307, 5087, ports) is null, "Server port: a free port is accepted");
        check(DatabaseServerRules.WhyNotControllable(true, "127.0.0.1", "S", ServerServiceStatus.Running, 3307, 3307) is null, "Server control: local, service found, same port = controllable");
        check(DatabaseServerRules.WhyNotControllable(false, "db.example", "S", ServerServiceStatus.NotFound, null, 3307) is { } remote && remote.Contains("alt calculator", StringComparison.Ordinal) && remote.Contains("db.example", StringComparison.Ordinal),
            "Server control: a server on another computer is not controlled, and the page says where to do it");
        check(DatabaseServerRules.WhyNotControllable(true, "127.0.0.1", "S", ServerServiceStatus.NotFound, null, 3307) is { } none && none.Contains("install-mariadb-service", StringComparison.Ordinal),
            "Server control: no service on this computer = the page says how to install it");
        check(DatabaseServerRules.WhyNotControllable(true, "127.0.0.1", "S", ServerServiceStatus.Running, null, 3307) is { } && DatabaseServerRules.WhyNotControllable(true, "127.0.0.1", "S", ServerServiceStatus.Running, 3310, 3307) is { } other
              && other.Contains("3310", StringComparison.Ordinal) && other.Contains("alt server", StringComparison.Ordinal), "Server control: a service without my.ini, or on another port than the connection, is not the server in use");
    }

    private static async Task SequencesAsync(Action<bool, string> check)
    {
        using (var setup = new Setup())
        {
            var state = await setup.Server.GetStateAsync();
            check(state.CanControl && state.Service == ServerServiceStatus.Running && state.IniPort == 3307 && state.ConnectionPort == 3307 && state.IniPath == setup.IniPath && state.ApplicationRestartSupported,
                "Server state: a local running service on the connection port is controllable, with the port read from its my.ini");
        }
        using (var setup = new Setup(administrator: false))
        {
            check(await Throws<AccessDeniedException>(() => setup.Server.GetStateAsync()) && await Throws<AccessDeniedException>(() => setup.Server.StopAsync("administrator-password"))
                  && await Throws<AccessDeniedException>(() => setup.Server.ChangePortAsync(3308, "administrator-password")) && setup.Services.Calls.Count == 0,
                "Server control: a user without the Administrator role cannot read the state or do anything");
        }
        using (var setup = new Setup())
        {
            check(await Throws<DatabaseSettingsException>(() => setup.Server.StopAsync("gresita"), DatabaseConnectionRules.AdminPasswordMessage) && setup.Services.Calls.Count == 0,
                "Server control: a wrong administrator password stops nothing");
            AdminPasswordGate.Succeeded("admin");
            var stopped = await setup.Server.StopAsync("administrator-password");
            check(stopped.Success && setup.Services.Calls.SequenceEqual(["stop"]) && setup.Locks.Operations.Count == 1
                  && setup.Audit.Entries.Single() is { Action: AuditActions.StopDatabaseServer } stop && stop.Details.Contains("BlazorStocMariaDB", StringComparison.Ordinal),
                "Server control: stop takes the maintenance lock, stops the service and is journalled with the exact operation");
            var again = await setup.Server.StopAsync("administrator-password");
            check(again.Success && again.Message.Contains("deja oprit", StringComparison.Ordinal) && setup.Services.Calls.Count == 1, "Server control: stopping a stopped server does nothing");
            var started = await setup.Server.StartAsync("administrator-password");
            check(started.Success && setup.Services.Calls.SequenceEqual(["stop", "start"]) && setup.Audit.Entries[^1].Action == AuditActions.StartDatabaseServer, "Server control: start starts the service and waits for the server to answer");
            var restarted = await setup.Server.RestartAsync("administrator-password");
            check(restarted.Success && setup.Services.Calls.SequenceEqual(["stop", "start", "stop", "start"]) && setup.Audit.Entries[^1].Action == AuditActions.RestartDatabaseServer, "Server control: restart stops and starts");
            setup.Locks.Held = true;
            check(await Throws<DatabaseSettingsException>(() => setup.Server.StopAsync("administrator-password"), DatabaseServerRules.LockHeldMessage), "Server control: refused while a backup, restore or another operation holds the lock");
        }
        using (var setup = new Setup(local: false))
        {
            var state = await setup.Server.GetStateAsync();
            check(!state.CanControl && state.Reason is { Length: > 0 } && await Throws<DatabaseSettingsException>(() => setup.Server.StopAsync("administrator-password"), "alt calculator")
                  && await Throws<DatabaseSettingsException>(() => setup.Server.ChangePortAsync(3308, "administrator-password"), "alt calculator") && setup.Services.Calls.Count == 0 && setup.CurrentIni == Ini,
                "Server control: a remote server is only reported, nothing is stopped and no file is changed");
        }
        using (var setup = new Setup(connectionPort: 3999))
        {
            var state = await setup.Server.GetStateAsync();
            check(!state.CanControl && state.Reason!.Contains("alt server", StringComparison.Ordinal), "Server control: a connection on another port than the service's is not controlled");
        }

        // A stopped server whose port is taken: the program is named, a free port is proposed and the start is refused with the same words.
        using (var setup = new Setup(state: ServerServiceStatus.Stopped))
        {
            setup.Ports.Taken[3307] = new(4242, "postgres");
            setup.Ports.Taken[3308] = new(4243, "other");
            setup.Services.Reach(3307);   // the other program answers on the port: it connects, but it is not the server
            var state = await setup.Server.GetStateAsync();
            check(!state.Reachable && state.Occupant is { ProcessName: "postgres", ProcessId: 4242 } && state.SuggestedPort == 3309 && state.PortProblem!.Contains("postgres", StringComparison.Ordinal) && state.PortProblem.Contains("3309", StringComparison.Ordinal),
                "Occupied port: the program on the port is named and the first free port is proposed");
            AdminPasswordGate.Succeeded("admin");
            check(await Throws<DatabaseSettingsException>(() => setup.Server.StartAsync("administrator-password"), "postgres") && setup.Services.Calls.Count == 0, "Occupied port: the start is refused and says who holds the port");
            // ...and the way out: change the port, which also starts the server.
            var moved = await setup.Server.ChangePortAsync(3309, "administrator-password");
            check(moved.Success && MyIni.ReadPort(setup.CurrentIni) == 3309 && setup.Services.Calls.SequenceEqual(["start"]) && setup.Settings.AppliedPorts.SequenceEqual([3309]),
                "Occupied port: changing the port starts the stopped server on the new port, without a stop, and the application follows it");
        }

        // The change of the port: success.
        using (var setup = new Setup())
        {
            AdminPasswordGate.Succeeded("admin");
            File.WriteAllText(Path.Combine(setup.Directory, "admin.private.cnf"), "[client]\nhost=127.0.0.1\nport=3307\nuser=root\npassword=x\n");
            var result = await setup.Server.ChangePortAsync(3320, "administrator-password");
            check(result.Success && !result.RolledBack && MyIni.ReadPort(setup.CurrentIni) == 3320 && setup.Services.Calls.SequenceEqual(["stop", "start"]) && setup.Settings.AppliedPorts.SequenceEqual([3320]),
                "Server port: the new port is written to my.ini, the server restarts on it and the connection of the application follows");
            check(Directory.GetFiles(setup.Directory, "my.ini.bak-*").Length == 1 && File.ReadAllText(Directory.GetFiles(setup.Directory, "my.ini.bak-*")[0]) == Ini, "Server port: a copy of the old my.ini is kept next to it");
            check(DatabaseAdministrationAccount.Read(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Database:AdminConfigPath"] = Path.Combine(setup.Directory, "admin.private.cnf") }).Build())?.Port == 3320,
                "Server port: the file of the administration account follows the port, so the account operations keep working");
            check(setup.Audit.Entries.Single(entry => entry.Action == AuditActions.ChangeDatabaseServerPort).Details.Contains("3307", StringComparison.Ordinal) && setup.Audit.Entries.Single().Details.Contains("3320", StringComparison.Ordinal),
                "Server port: the journal names the exact operation with the old and the new port");
        }

        // Refusals before anything is touched.
        using (var setup = new Setup())
        {
            setup.Ports.Taken[3350] = new(9, "nginx");
            AdminPasswordGate.Succeeded("admin");
            check(await Throws<DatabaseSettingsException>(() => setup.Server.ChangePortAsync(3350, "administrator-password"), "nginx") && await Throws<DatabaseSettingsException>(() => setup.Server.ChangePortAsync(5087, "administrator-password"), "portul aplicației")
                  && await Throws<DatabaseSettingsException>(() => setup.Server.ChangePortAsync(3307, "administrator-password")) && setup.Services.Calls.Count == 0 && setup.CurrentIni == Ini && setup.Settings.AppliedPorts.Count == 0,
                "Server port: a taken port, the port of the application and the same port are refused before my.ini or the server are touched");
        }

        // The new port does not work: everything goes back and the old port serves again.
        using (var setup = new Setup())
        {
            AdminPasswordGate.Succeeded("admin");
            setup.Services.NeverStartsOn.Add(3321);
            var result = await setup.Server.ChangePortAsync(3321, "administrator-password");
            check(!result.Success && result.RolledBack && MyIni.ReadPort(setup.CurrentIni) == 3307 && setup.CurrentIni == Ini && setup.Services.State == ServerServiceStatus.Running && setup.Services.Listening == 3307
                  && setup.Settings.AppliedPorts.Count == 0 && setup.Services.Calls.SequenceEqual(["stop", "start", "stop", "start"]),
                "Server port: when the server does not answer on the new port, my.ini is put back, the server runs again on the old port and the connection is not changed");
            check(setup.Audit.Entries.Single() is { Action: AuditActions.ChangeDatabaseServerPort } failed && failed.Details.Contains("eșuat", StringComparison.Ordinal), "Server port: the failed attempt is journalled too");
        }

        // The application.
        using (var setup = new Setup(restartSupported: true))
        {
            AdminPasswordGate.Succeeded("admin");
            check(await Throws<DatabaseSettingsException>(() => setup.Server.RestartApplicationAsync("gresita")) && setup.Restart.Requests == 0, "Application restart: a wrong administrator password restarts nothing");
            AdminPasswordGate.Succeeded("admin");
            var result = await setup.Server.RestartApplicationAsync("administrator-password");
            check(result.Success && setup.Restart.Requests == 1 && setup.Audit.Entries.Single().Action == AuditActions.RestartApplication, "Application restart: confirmed, requested once and journalled with the exact operation");
        }
        using (var setup = new Setup(restartSupported: false))
        {
            AdminPasswordGate.Succeeded("admin");
            var result = await setup.Server.RestartApplicationAsync("administrator-password");
            check(!result.Success && setup.Restart.Requests == 0 && result.Message.Contains("supervizor", StringComparison.Ordinal) && setup.Audit.Entries.Count == 0,
                "Application restart: without a supervisor or service nothing is restarted and the page says why");
        }
        AdminPasswordGate.Succeeded("admin");
    }

    private static void Hosting(Action<bool, string> check)
    {
        IConfiguration Config(params (string, string)[] values) => new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(pair => pair.Item1, pair => (string?)pair.Item2)).Build();
        var proposed = AppHosting.Resolve(Config(("Hosting:DefaultPort", "5087")), _ => true);
        check(proposed.AppliedUrls == "http://127.0.0.1:5087" && proposed.ConfiguredPort is null, "Application port: the port a start script proposes is used when none was saved from the page");
        var saved = AppHosting.Resolve(Config(("Hosting:DefaultPort", "5087"), ("Hosting:AppPort", "5099")), _ => true);
        check(saved.AppliedUrls == "http://127.0.0.1:5099" && saved.ConfiguredPort == 5099 && saved.Note is null, "Application port: a port saved from the page wins over the one a script proposes (the script no longer imposes the address)");
        var taken = AppHosting.Resolve(Config(("Hosting:DefaultPort", "5087"), ("Hosting:AppPort", "5099")), port => port != 5099);
        check(taken.AppliedUrls == "http://127.0.0.1:5087" && taken.Note!.Contains("5087", StringComparison.Ordinal), "Application port: a saved port that is taken falls back to the proposed one, and the page says so");
        var imposed = AppHosting.Resolve(Config(("Hosting:DefaultPort", "5087"), ("Hosting:AppPort", "5099"), ("urls", "http://127.0.0.1:5000")), _ => true);
        check(imposed.AppliedUrls is null && imposed.Note is { Length: > 0 }, "Application port: addresses imposed with --urls or ASPNETCORE_URLS still win");
    }

    private sealed class FakeServerService(DatabaseServerState state) : IDatabaseServerService
    {
        public List<string> Calls { get; } = [];
        public Task<DatabaseServerState> GetStateAsync(CancellationToken cancellationToken = default) => Task.FromResult(state);
        public Task<DatabaseSaveResult> StopAsync(string administratorPassword, CancellationToken cancellationToken = default) { Calls.Add("stop:" + administratorPassword); return Task.FromResult(new DatabaseSaveResult(true, "Oprit.")); }
        public Task<DatabaseSaveResult> StartAsync(string administratorPassword, CancellationToken cancellationToken = default) { Calls.Add("start"); return Task.FromResult(new DatabaseSaveResult(true, "Pornit.")); }
        public Task<DatabaseSaveResult> RestartAsync(string administratorPassword, CancellationToken cancellationToken = default) { Calls.Add("restart"); return Task.FromResult(new DatabaseSaveResult(true, "Repornit.")); }
        public Task<DatabaseSaveResult> ChangePortAsync(int port, string administratorPassword, CancellationToken cancellationToken = default) { Calls.Add("port:" + port); return Task.FromResult(new DatabaseSaveResult(true, "Port schimbat.")); }
        public Task<DatabaseSaveResult> RestartApplicationAsync(string administratorPassword, CancellationToken cancellationToken = default) { Calls.Add("app:" + administratorPassword); return Task.FromResult(new DatabaseSaveResult(true, "Repornește.")); }
    }

    private static BunitContext Context(IDatabaseServerService server)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddLogging();
        context.Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        context.Services.AddSingleton(server);
        context.Services.AddSingleton<IDatabaseSettingsService>(new RecordingSettings());
        return context;
    }

    private static void Cards(Action<bool, string> check)
    {
        var running = new DatabaseServerState("BlazorStocMariaDB", ServerServiceStatus.Running, true, "C:\\m\\my.ini", 3307, true, null, true, null, null, null, true, 3307);
        var server = new FakeServerService(running);
        using (var context = Context(server))
        {
            var card = context.Render<DatabaseServerPanel>();
            var buttons = card.FindAll(".database-server .editor-actions button").Select(button => button.TextContent.Trim()).ToArray();
            check(card.Markup.Contains("BlazorStocMariaDB", StringComparison.Ordinal) && card.Markup.Contains("pornit", StringComparison.Ordinal) && buttons.Contains("Repornește serverul") && buttons.Contains("Oprește serverul")
                  && buttons.Contains("Schimbă portul serverului") && !buttons.Contains("Pornește serverul"), "Server card: a running server shows its state and the buttons restart, stop and change port");
            card.FindAll(".database-server button").First(button => button.TextContent.Trim() == "Oprește serverul").Click();
            var stop = card.Find(".admin-operation-dialog button.primary");
            check(card.FindAll("#server-stop-word").Count == 1 && stop.HasAttribute("disabled") && card.FindAll(".admin-operation-dialog button.help-icon").Count == 1,
                "Server card: the stop window asks for the word and the password, has the help button and stays disabled until the word is typed");
            card.Find("#server-stop-word").Input(DatabaseServerRules.StopWord);
            card.Find("#admin-operation-password").Input("parola");
            card.Find(".admin-operation-dialog button.primary").Click();
            check(server.Calls.SequenceEqual(["stop:parola"]) && card.FindAll(".admin-operation-dialog").Count == 0 && card.Markup.Contains("Oprit.", StringComparison.Ordinal), "Server card: with the word and the password the server is stopped and the window closes");
            card.FindAll(".database-server button").First(button => button.TextContent.Trim() == "Schimbă portul serverului").Click();
            check(card.Find("#server-new-port").GetAttribute("value") == "3308", "Server card: the port window proposes the next port");
            card.Find("#server-new-port").Change("3400");
            card.Find("#admin-operation-password").Input("parola");
            card.Find(".admin-operation-dialog button.primary").Click();
            check(server.Calls.Contains("port:3400"), "Server card: the port typed in the window is the one sent to the service");
        }
        var remote = new DatabaseServerState("BlazorStocMariaDB", ServerServiceStatus.NotFound, false, null, null, false, "Serverul bazei de date este pe alt calculator (db.example).", true, null, null, null, true, 3307);
        using (var context = Context(new FakeServerService(remote)))
        {
            var card = context.Render<DatabaseServerPanel>();
            check(card.FindAll(".database-server .editor-actions button").Count == 0 && card.Markup.Contains("alt calculator", StringComparison.Ordinal), "Server card: a server that cannot be controlled shows the reason and no buttons");
        }
        var occupied = new DatabaseServerState("BlazorStocMariaDB", ServerServiceStatus.Stopped, true, "C:\\m\\my.ini", 3307, true, null, false, new(4242, "postgres"), "Portul 3307 este ocupat de postgres (PID 4242), de aceea serverul nu poate porni. Schimbă portul serverului (propus: 3308).", 3308, true, 3307);
        using (var context = Context(new FakeServerService(occupied)))
        {
            var card = context.Render<DatabaseServerPanel>();
            check(card.Markup.Contains("postgres", StringComparison.Ordinal) && card.FindAll(".database-server .editor-actions button").First(button => button.TextContent.Trim() == "Pornește serverul").HasAttribute("disabled"),
                "Server card: a stopped server whose port is taken names the program and cannot be started");
        }
        using (var context = Context(server))
        {
            var card = context.Render<ApplicationPanel>();
            check(card.Find("#app-port").GetAttribute("value") == "5087" && card.Markup.Contains("Salvează și repornește aplicația", StringComparison.Ordinal) && card.Markup.Contains("Repornește aplicația", StringComparison.Ordinal),
                "Application card: the saved port is shown and, when a restart is possible, the buttons save-and-restart and restart");
            card.FindAll(".application-hosting button").First(button => button.TextContent.Trim() == "Repornește aplicația").Click();
            card.Find("#admin-operation-password").Input("parola");
            card.Find(".admin-operation-dialog button.primary").Click();
            check(server.Calls.Contains("app:parola") && card.Markup.Contains("Repornește.", StringComparison.Ordinal), "Application card: the restart is confirmed with the password and asked from the service");
            card.Find("#app-port").Change("5099");
            card.FindAll(".application-hosting button").First(button => button.TextContent.Trim() == "Salvează și repornește aplicația").Click();
            card.Find("#admin-operation-password").Input("alta-parola");
            card.Find(".admin-operation-dialog button.primary").Click();
            check(server.Calls.Contains("app:alta-parola"), "Application card: save-and-restart sends the same password to the restart (reading the saved settings again does not empty it)");
        }
        var noRestart = new FakeServerService(running with { ApplicationRestartSupported = false });
        using (var context = Context(noRestart))
        {
            var card = context.Render<ApplicationPanel>();
            check(!card.Markup.Contains("Repornește aplicația", StringComparison.Ordinal) && card.Markup.Contains("Salvează portul", StringComparison.Ordinal) && card.Markup.Contains("următoarea pornire", StringComparison.Ordinal),
                "Application card: without a supervisor or service there is only save, and the card says the port applies at the next start");
        }
    }

    private static async Task<bool> Throws<TException>(Func<Task> action, string? messagePart = null) where TException : Exception
    {
        try { await action(); return false; }
        catch (TException exception) { return messagePart is null || exception.Message.Contains(messagePart, StringComparison.Ordinal); }
    }
}
