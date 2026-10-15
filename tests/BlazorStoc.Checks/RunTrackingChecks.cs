using BlazorStoc.Components.Shared;
using BlazorStoc.Modules;
using BlazorStoc.Pages.Account;
using BlazorStoc.Services;
using BlazorStoc.Startup;
using Bunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace BlazorStoc.Checks;

// What the journal of activity learns about the modules and about the application itself: the changes of state of the modules, the retry of a module by the
// administrator, an unexpected stop of the application found at the next start (marker file), and the retries while the journal cannot be written.
public static class RunTrackingChecks
{
    private sealed class FakeModule(string id, bool failRegister = false) : IModule
    {
        public string Id => id;
        public string Name => "Modul " + id;
        public Version Version { get; } = new(1, 0, 0);
        public IReadOnlyList<ModuleNavItem> Nav => [];
        public void Register(IServiceCollection services, IConfiguration configuration) { if (failRegister) throw new InvalidOperationException("register"); }
    }

    // A journal that refuses the first FailTimes writes (the database is down, a backup holds it) and then accepts them.
    private sealed class FlakyAuditTrail(int failTimes) : IAuditTrail
    {
        public int Calls;
        public List<AuditWrite> Entries { get; } = [];
        public Task RecordAsync(AuditWrite entry, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Calls <= failTimes) return Task.FromException(new InvalidOperationException("journal down"));
            lock (Entries) Entries.Add(entry);
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<AuditEvent>> GetEventsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AuditEvent>>([]);
        public Task<AuditPage> QueryAsync(AuditQuery query, int page, int pageSize, int? window = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AuditSummary> SummaryAsync(DateTime todayStartUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<string, DateTime>> RemovalTimesAsync(IEnumerable<AuditEvent> events, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static IConfiguration Empty() => new ConfigurationBuilder().Build();

    private static async Task<bool> WaitAsync(Func<bool> condition, int milliseconds = 4000)
    {
        for (var waited = 0; waited < milliseconds; waited += 50)
        {
            if (condition()) return true;
            await Task.Delay(50);
        }
        return condition();
    }

    public static async Task RunAsync(Action<bool, string> check)
    {
        Constants(check);
        await ModuleChangesAsync(check);
        await RetryAsync(check);
        await MarkerAsync(check);
        await ServiceAsync(check);
        await ScreensAsync(check);
        await LoginAsync(check);
    }

    private static void Constants(Action<bool, string> check)
    {
        var actions = AuditFilterOptions.Actions.Select(item => item.Value).ToHashSet();
        check(new[] { AuditActions.ModuleDisabled, AuditActions.ModuleRecovered, AuditActions.ModuleRetry, AuditActions.ApplicationUnexpectedStop }.All(actions.Contains),
            "Journal: the actions for modules and for the unexpected stop of the application are in the filter of the journal page");
        var entities = AuditFilterOptions.Entities.Select(item => item.Value).ToHashSet();
        check(entities.Contains(AuditEntities.Module) && entities.Contains(AuditEntities.Application), "Journal: the entities Modul and Aplicatie are in the filter of the journal page");
        check(AuditActions.ModuleDisabled != AuditActions.ModuleRecovered && AuditActions.ModuleRecovered != AuditActions.ModuleRetry, "Journal: each operation has its own exact action (no generic Editare)");
    }

    // ---- the changes of state of the modules ----

    private static async Task ModuleChangesAsync(Action<bool, string> check)
    {
        var clock = new ManualTimeProvider();
        var registry = new ModuleRegistry(clock);
        registry.Add(new FakeModule("m"), new ServiceCollection(), Empty());
        var audit = new TestAuditTrail();
        var journal = new ModuleJournal(registry, audit, NullLogger<ModuleJournal>.Instance) { RetryDelay = TimeSpan.FromMilliseconds(20) };
        journal.Start();
        var fault = new InvalidOperationException("boom");

        registry.ReportFailure("m", fault);
        registry.ReportFailure("m", fault);
        await Task.Delay(150);
        check(audit.Entries.Count == 0, "Journal: failures that have not taken the module out are not written (only the change of state is)");
        registry.ReportFailure("m", fault);
        var opened = await WaitAsync(() => audit.Entries.Count == 1);
        var entry = opened ? audit.Entries[0] : null;
        check(entry is { Action: AuditActions.ModuleDisabled, EntityType: AuditEntities.Module, EntityId: "m", Target: "Modul m", ActorUsername: "sistem", ActorRole: "Sistem" }
            && entry.Details.Contains(ModuleOrigins.RepeatedFailures) && entry.Details.Contains("InvalidOperationException"),
            "Journal: a module taken out after repeated failures is written as \"Oprire modul\" by the system, with the cause and the type of the error");
        clock.Advance(ModuleRegistry.Cooldown + TimeSpan.FromSeconds(1));
        registry.ReportFailure("m", fault);   // a fault right after the cooldown: the same outage
        await Task.Delay(150);
        check(audit.Entries.Count == 1, "Journal: the circuit that opens again right after the cooldown is the same outage (no second entry)");
        clock.Advance(ModuleRegistry.Cooldown + TimeSpan.FromSeconds(1));
        registry.ReportSuccess("m");
        var recovered = await WaitAsync(() => audit.Entries.Count == 2);
        check(recovered && audit.Entries[1].Action == AuditActions.ModuleRecovered && audit.Entries[1].EntityId == "m", "Journal: the return of the module is written as \"Revenire modul\"");
        registry.ReportSuccess("m");
        registry.ReportSuccess("m");
        await Task.Delay(100);
        check(audit.Entries.Count == 2, "Journal: a module that is healthy and keeps answering writes nothing");
        registry.ReportFailure("m", fault); registry.ReportFailure("m", fault); registry.ReportFailure("m", fault);
        check(await WaitAsync(() => audit.Entries.Count == 3) && audit.Entries[2].Action == AuditActions.ModuleDisabled, "Journal: a second outage after a recovery is a new entry");

        var info = registry.Snapshot()[0];
        check(info.Status == ModuleStatus.Disabled && info.Origin == ModuleOrigins.RepeatedFailures && info.Reason == "InvalidOperationException" && info.Failures >= 3 && info.SinceUtc != default,
            "Journal: the status screen has the cause, the type of the error, the number of failures and since when the module is out");
        registry.ReportSuccess("m");
        check(registry.Snapshot()[0].Status == ModuleStatus.Disabled, "Journal: a late success while the circuit is open changes nothing");

        // The modules that were out at startup are announced to the first subscriber.
        var startup = new ModuleRegistry(clock);
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Modules:off:Enabled"] = "false" }).Build();
        startup.Add(new FakeModule("broken", failRegister: true), services, configuration);
        startup.Add(new FakeModule("off"), services, configuration);
        startup.Add(new FakeModule("fine"), services, configuration);
        var startupAudit = new TestAuditTrail();
        new ModuleJournal(startup, startupAudit, NullLogger<ModuleJournal>.Instance).Start();
        var announced = await WaitAsync(() => startupAudit.Entries.Count == 2);
        check(announced && startupAudit.Entries.Any(e => e.EntityId == "broken" && e.Details.Contains(ModuleOrigins.Startup))
            && startupAudit.Entries.Any(e => e.EntityId == "off" && e.Details.Contains(ModuleOrigins.Configuration)) && startupAudit.Entries.All(e => e.Action == AuditActions.ModuleDisabled),
            "Journal: a module that did not start, and one switched off in the configuration, are written when the journal starts listening (nobody listened when it happened)");
        var snapshot = startup.Snapshot();
        check(snapshot.Single(m => m.Id == "broken").Origin == ModuleOrigins.Startup && snapshot.Single(m => m.Id == "off").Origin == ModuleOrigins.Configuration && snapshot.Single(m => m.Id == "fine").Origin is null,
            "Journal: the status screen says why each module that is out is out");

        // A subscriber that fails does not disturb the code that reported the failure.
        var failing = new ModuleRegistry(clock);
        failing.Add(new FakeModule("x"), new ServiceCollection(), Empty());
        failing.Subscribe(_ => throw new InvalidOperationException("subscriber"));
        var survived = true;
        try { for (var i = 0; i < 3; i++) failing.ReportFailure("x", fault); } catch (Exception) { survived = false; }
        check(survived && failing.StatusOf("x") == ModuleStatus.Disabled, "Journal: a subscriber that throws never reaches the code that reports a module failure");

        // The journal cannot be written for a while: retried until it can.
        var flakyRegistry = new ModuleRegistry(clock);
        flakyRegistry.Add(new FakeModule("f"), new ServiceCollection(), Empty());
        var flaky = new FlakyAuditTrail(2);
        new ModuleJournal(flakyRegistry, flaky, NullLogger<ModuleJournal>.Instance) { RetryDelay = TimeSpan.FromMilliseconds(20) }.Start();
        for (var i = 0; i < 3; i++) flakyRegistry.ReportFailure("f", fault);
        check(await WaitAsync(() => flaky.Entries.Count == 1) && flaky.Calls == 3, $"Journal: an entry that the journal refuses twice (database down) is written at the third attempt ({flaky.Calls} calls)");
        var never = new FlakyAuditTrail(int.MaxValue);
        var written = await JournalWithRetry.WriteAsync(never, new AuditWrite("sistem", "Sistem", AuditEntities.Module, AuditActions.ModuleDisabled, "x", "y"), NullLogger.Instance, TimeSpan.FromMilliseconds(5), 4);
        check(!written && never.Calls == 4, "Journal: after the attempts are used up the entry is dropped (and logged), nothing is thrown");
    }

    // ---- the retry of the administrator ----

    private static Task RetryAsync(Action<bool, string> check)
    {
        var clock = new ManualTimeProvider();
        var registry = new ModuleRegistry(clock);
        registry.Add(new FakeModule("m"), new ServiceCollection(), Empty());
        registry.Add(new FakeModule("broken", failRegister: true), new ServiceCollection(), Empty());
        check(!registry.Retry("m"), "Retry: a healthy module has nothing to retry");
        for (var i = 0; i < 3; i++) registry.ReportFailure("m", new InvalidOperationException());
        check(registry.StatusOf("m") == ModuleStatus.Disabled && !registry.IsAvailable("m"), "Retry: the module is out");
        check(registry.Retry("m") && registry.StatusOf("m") == ModuleStatus.Degraded && registry.IsAvailable("m"), "Retry: \"try again\" lets the next call through at once, without waiting for the cooldown");
        registry.ReportFailure("m", new InvalidOperationException());
        check(registry.StatusOf("m") == ModuleStatus.Disabled, "Retry: if the module still fails, the circuit opens again at once");
        check(!registry.Retry("broken") && registry.StatusOf("broken") == ModuleStatus.Disabled, "Retry: a module that did not start needs a restart of the application, not a retry");
        check(!registry.Retry("unknown"), "Retry: an unknown module has nothing to retry");
        return Task.CompletedTask;
    }

    // ---- the marker file ----

    private static async Task MarkerAsync(Action<bool, string> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "blazorstoc-marker-" + Guid.NewGuid().ToString("N"));
        try
        {
            var clock = new ManualTimeProvider();
            var marker = new RunMarker(Path.Combine(directory, "run.json"), clock);
            check(marker.ReadPrevious() is null, "Marker: no marker, no previous run to report (a first start)");
            marker.Start();
            check(File.Exists(marker.Path), "Marker: the marker is written at start (in a folder that did not exist)");
            var started = clock.GetUtcNow().UtcDateTime;
            clock.Advance(TimeSpan.FromMinutes(7));
            marker.Heartbeat(started);
            var previous = new RunMarker(marker.Path, clock).ReadPrevious();
            check(previous is { StartedUtc: { } s, LastActivityUtc: { } l, ProcessId: { } p } && s == started && l == started.AddMinutes(7) && p == Environment.ProcessId,
                "Marker: a marker found at the next start gives when the run started, its last sign of life and its process");
            marker.Stop();
            check(!File.Exists(marker.Path) && new RunMarker(marker.Path, clock).ReadPrevious() is null, "Marker: a normal stop removes the marker (the next start reports nothing)");

            File.WriteAllText(marker.Path, "{ this is not json");
            var damaged = new RunMarker(marker.Path, clock).ReadPrevious();
            check(damaged is { StartedUtc: null, LastActivityUtc: null }, "Marker: a damaged marker still means an unexpected stop, with unknown times (no exception)");
            check(RunTrackingService.Entry(damaged!).Details.Contains("necunoscute"), "Marker: the journal entry for a damaged marker says the details are unknown");
            File.Delete(marker.Path);

            marker.Start();
            File.WriteAllText(marker.ExitPath, "{\"exitCode\":-1,\"reason\":\"exited with code -1\",\"atUtc\":\"2026-10-15T10:00:00Z\"}");
            var withExit = new RunMarker(marker.Path, clock).ReadPrevious();
            check(withExit?.ExitInfo is { } info && info.Contains("exited with code -1") && info.Contains("cod de ieșire -1") && !File.Exists(marker.ExitPath),
                "Marker: the supervisor's note says why the run ended, and is used up once read");
            var entry = RunTrackingService.Entry(withExit!);
            check(entry is { Action: AuditActions.ApplicationUnexpectedStop, EntityType: AuditEntities.Application, ActorUsername: "sistem", ActorRole: "Sistem" }
                && entry.Details.Contains("Ultima activitate") && entry.Details.Contains("Pornită la") && entry.Details.Contains("Cauză") && entry.Details.Contains("Proces"),
                "Marker: the journal entry has the last activity, the start, the process and the cause, written by the system");
            marker.Stop();

            // an unwritable place never stops the application
            var blocked = new RunMarker(Path.Combine(directory, "no\0good", "run.json"), clock);
            try { blocked.Start(); blocked.Heartbeat(started); blocked.Stop(); check(true, "Marker: a marker that cannot be written (bad path) never stops the application"); }
            catch (Exception exception) { check(false, "Marker: a marker that cannot be written never stops the application (" + exception.GetType().Name + ")"); }

            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ASPNETCORE_URLS"] = "http://127.0.0.1:5087" }).Build();
            var defaultPath = RunMarker.DefaultPath(configuration);
            check(Path.GetFileName(defaultPath) == "run-http---127-0-0-1-5087.json", "Marker: by default there is one marker per address the application listens on (" + Path.GetFileName(defaultPath) + ")");
            var configured = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["App:RunMarkerPath"] = "C:\\x\\run.json" }).Build();
            check(RunMarker.DefaultPath(configured) == "C:\\x\\run.json", "Marker: App:RunMarkerPath (set by the supervisor) decides where the marker is");
        }
        finally { try { Directory.Delete(directory, recursive: true); } catch (IOException) { } }
        await Task.CompletedTask;
    }

    // ---- the screens of the administrator ----

    private static async Task ScreensAsync(Action<bool, string> check)
    {
        var clock = new ManualTimeProvider();
        var registry = new ModuleRegistry(clock);
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Modules:off:Enabled"] = "false" }).Build();
        registry.Add(new FakeModule("fine"), services, Empty());
        registry.Add(new FakeModule("failing"), services, Empty());
        registry.Add(new FakeModule("broken", failRegister: true), services, Empty());
        registry.Add(new FakeModule("off"), services, configuration);
        for (var i = 0; i < 3; i++) registry.ReportFailure("failing", new InvalidOperationException());
        var audit = new TestAuditTrail();

        using var admin = new BunitContext();
        admin.Services.AddSingleton(registry);
        admin.Services.AddSingleton<IAccessControl>(new TestAccessControl(true, "admin.demo"));
        admin.Services.AddSingleton<IAuditTrail>(audit);
        admin.Services.AddSingleton<TimeProvider>(clock);
        var panel = admin.Render<ModulesPanel>();
        check(panel.FindAll("tbody tr").Count == 4 && panel.Markup.Contains("Modul fine") && panel.Markup.Contains("Modul failing"), "Screen: the status screen lists every module");
        var rows = panel.FindAll("tbody tr").Select(row => row.TextContent).ToList();
        check(rows[0].Contains("Funcționează") && rows[1].Contains("Oprit") && rows[1].Contains(ModuleOrigins.RepeatedFailures) && rows[1].Contains("InvalidOperationException")
            && rows[2].Contains(ModuleOrigins.Startup) && rows[3].Contains(ModuleOrigins.Configuration), "Screen: each module shows its state and, when it is out, why (repeated failures with the type of the error, not started, switched off)");
        check(panel.FindAll("button.row-icon.renew").Count == 1 && panel.Find("button.row-icon.renew").GetAttribute("aria-label")!.Contains("Modul failing"),
            "Screen: only a module taken out for repeated failures has the \"try again\" icon (the others need a restart of the application)");
        panel.Find("button.row-icon.renew").Click();
        check(registry.StatusOf("failing") == ModuleStatus.Degraded && panel.Markup.Contains("va fi reîncercat") && panel.FindAll("button.row-icon.renew").Count == 0,
            "Screen: \"try again\" lets the module through at once and the screen says so");
        check(audit.Entries.Count(entry => entry.Action == AuditActions.ModuleRetry) == 1
            && audit.Entries.Single(entry => entry.Action == AuditActions.ModuleRetry) is { EntityType: AuditEntities.Module, EntityId: "failing", ActorUsername: "admin.demo" },
            "Screen: the retry is written to the journal under the administrator's name with its own action");

        var banner = admin.Render<ModuleAlertBanner>();
        check(banner.FindAll(".module-alert").Count == 1 && banner.Markup.Contains("Modul broken") && banner.Markup.Contains("Modul off") && !banner.Markup.Contains("Modul failing")
            && banner.Find("a").GetAttribute("href") == "/setari?tab=module", "Screen: the administrator sees a banner naming the modules that are out, linking to the status screen");

        using var ordinary = new BunitContext();
        ordinary.Services.AddSingleton(registry);
        ordinary.Services.AddSingleton<IAccessControl>(new TestAccessControl(false, "operator"));
        ordinary.Services.AddSingleton<IAuditTrail>(new TestAuditTrail());
        ordinary.Services.AddSingleton<TimeProvider>(clock);
        check(ordinary.Render<ModuleAlertBanner>().FindAll(".module-alert").Count == 0, "Screen: an ordinary user never sees the administrator's banner");
        check(ordinary.Render<ModulesPanel>().FindAll("button.row-icon.renew").Count == 0, "Screen: without the permission the status screen has no \"try again\"");

        var healthy = new ModuleRegistry(clock);
        healthy.Add(new FakeModule("a"), new ServiceCollection(), Empty());
        using var quiet = new BunitContext();
        quiet.Services.AddSingleton(healthy);
        quiet.Services.AddSingleton<IAccessControl>(new TestAccessControl(true, "admin.demo"));
        check(quiet.Render<ModuleAlertBanner>().FindAll(".module-alert").Count == 0, "Screen: with every module working there is no banner");

        check(Permissions.IsKnown("setari-module.view") && Permissions.IsKnown("setari-module.retry") && Permissions.IsAdminOnly("setari-module.retry") && !Permissions.GrantableKeys.Any(key => key.StartsWith("setari-module.")),
            "Screen: the permission of the status screen is reserved for the administrator and never granted to another user type");
        await Task.CompletedTask;
    }

    // ---- the sign-in page ----

    private sealed class FakeAuthenticator(Func<AuthenticationResult> answer) : IUserAuthenticator
    {
        public Task<AuthenticationResult> AuthenticateAsync(string username, string password, CancellationToken cancellationToken = default) =>
            Task.FromResult(answer());
    }

    private static async Task<string?> SignInErrorAsync(Func<AuthenticationResult> answer)
    {
        var model = new LoginModel(Empty(), new FakeAuthenticator(answer), new TestAuditTrail(), NullLogger<LoginModel>.Instance)
        {
            Username = "someone", Password = "whatever",
            PageContext = new Microsoft.AspNetCore.Mvc.RazorPages.PageContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() }
        };
        await model.OnPostAsync();
        return model.Error;
    }

    private static async Task LoginAsync(Action<bool, string> check)
    {
        check(await SignInErrorAsync(() => new AuthenticationResult(AuthenticationStatus.InvalidCredentials)) == LoginMessages.WrongCredentials, "Sign-in: a wrong password says so");
        foreach (var failure in new Exception[] { new MaintenanceInProgressException("backup"), new TimeoutException(), new IOException("socket"), new System.Net.Sockets.SocketException() })
        {
            var message = await SignInErrorAsync(() => throw failure);
            check(message == LoginMessages.DatabaseUnavailable && message != LoginMessages.WrongCredentials,
                $"Sign-in: {failure.GetType().Name} (the database does not answer or a backup holds it) is not reported as a wrong password");
        }
        check(await SignInErrorAsync(() => throw new InvalidOperationException("bug")) == LoginMessages.WrongCredentials, "Sign-in: an unexpected error keeps the generic message (nothing is revealed about it)");
        check(LoginMessages.DatabaseUnavailable.Contains("backup") && LoginMessages.DatabaseUnavailable.Contains("Nu este o problemă a contului"), "Sign-in: the message tells the user it is not his account and to try again in a moment");
    }

    // ---- the service ----

    private static async Task ServiceAsync(Action<bool, string> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "blazorstoc-marker-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(directory, "run.json");
            var audit = new TestAuditTrail();
            var first = new RunTrackingService(new RunMarker(path), audit, NullLogger<RunTrackingService>.Instance) { RetryDelay = TimeSpan.FromMilliseconds(20) };
            await first.StartAsync(CancellationToken.None);
            check(await WaitAsync(() => File.Exists(path)) && audit.Entries.Count == 0, "Service: the first start writes the marker and reports nothing");
            await first.StopAsync(CancellationToken.None);
            check(!File.Exists(path), "Service: a normal stop of the application removes the marker");

            // a run that is killed: nobody removes the marker; the next start reports it
            var killed = new RunTrackingService(new RunMarker(path), audit, NullLogger<RunTrackingService>.Instance) { RetryDelay = TimeSpan.FromMilliseconds(20) };
            await killed.StartAsync(CancellationToken.None);
            await WaitAsync(() => File.Exists(path));
            // (no StopAsync: the process is gone)
            var flaky = new FlakyAuditTrail(2);
            var next = new RunTrackingService(new RunMarker(path), flaky, NullLogger<RunTrackingService>.Instance) { RetryDelay = TimeSpan.FromMilliseconds(20) };
            await next.StartAsync(CancellationToken.None);
            check(await WaitAsync(() => flaky.Entries.Count == 1) && flaky.Entries[0].Action == AuditActions.ApplicationUnexpectedStop && flaky.Calls == 3,
                $"Service: the start after a killed run writes \"Oprire neașteptată aplicație\" (retried while the journal was down: {flaky.Calls} calls)");
            check(File.Exists(path), "Service: the new run has its own marker");
            await next.StopAsync(CancellationToken.None);
            await killed.StopAsync(CancellationToken.None);
            check(!File.Exists(path), "Service: after the new run stops normally nothing is left");

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IAuditTrail>(new TestAuditTrail());
            services.AddSingleton(new ModuleRegistry());
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["App:RunMarkerPath"] = path }).Build();
            services.AddRunTracking(configuration);
            using var provider = services.BuildServiceProvider();
            check(provider.GetRequiredService<RunMarker>().Path == path && provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<RunTrackingService>().Any()
                && provider.GetRequiredService<ModuleJournal>() is not null, "Service: the application registers the marker, the tracking service and the module journal");
        }
        finally { try { Directory.Delete(directory, recursive: true); } catch (IOException) { } }
    }
}
