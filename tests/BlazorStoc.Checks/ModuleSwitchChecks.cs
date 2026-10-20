using BlazorStoc.Components.Shared;
using BlazorStoc.Modules;
using BlazorStoc.Services;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace BlazorStoc.Checks;

// The administrator's stop / start of a module (Setari -> Module): the registry (stop at once, no circuit breaker, start from scratch), the saved state that survives a restart,
// the journal under the administrator's name, the screen with the reason asked before a stop, and the grey tile of the main page.
public static class ModuleSwitchChecks
{
    private sealed class FakeModule(string id, bool failRegister = false) : IModule
    {
        public string Id => id;
        public string Name => "Modul " + id;
        public Version Version { get; } = new(1, 0, 0);
        public IReadOnlyList<ModuleNavItem> Nav => [new($"/{id}", id, "x", id)];
        public void Register(IServiceCollection services, IConfiguration configuration) { if (failRegister) throw new InvalidOperationException("register"); }
    }

    private sealed class MemoryStore : IModuleStateStore
    {
        public Dictionary<string, StoppedModule> Rows { get; } = [];
        public bool Down;
        public Task<IReadOnlyList<StoppedModule>> ReadStoppedAsync(CancellationToken cancellationToken = default) =>
            Down ? Task.FromException<IReadOnlyList<StoppedModule>>(new InvalidOperationException("down")) : Task.FromResult<IReadOnlyList<StoppedModule>>([.. Rows.Values]);
        public Task SaveStoppedAsync(StoppedModule stopped, CancellationToken cancellationToken = default)
        {
            if (Down) return Task.FromException(new InvalidOperationException("down"));
            Rows[stopped.ModuleId] = stopped;
            return Task.CompletedTask;
        }
        public Task ClearStoppedAsync(string moduleId, CancellationToken cancellationToken = default)
        {
            if (Down) return Task.FromException(new InvalidOperationException("down"));
            Rows.Remove(moduleId);
            return Task.CompletedTask;
        }
    }

    private sealed class RefusingAudit : IAuditTrail
    {
        public Task RecordAsync(AuditWrite entry, CancellationToken cancellationToken = default) => Task.FromException(new InvalidOperationException("journal down"));
        public Task<IReadOnlyList<AuditEvent>> GetEventsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AuditEvent>>([]);
        public Task<AuditPage> QueryAsync(AuditQuery query, int page, int pageSize, int? window = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AuditSummary> SummaryAsync(DateTime todayStartUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<string, DateTime>> RemovalTimesAsync(IEnumerable<AuditEvent> events, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static ModuleRegistry NewRegistry(ManualTimeProvider clock)
    {
        var registry = new ModuleRegistry(clock);
        var services = new ServiceCollection();
        var empty = new ConfigurationBuilder().Build();
        foreach (var id in new[] { "alpha", "beta", "jurnal" }) registry.Add(new FakeModule(id), services, empty);
        registry.Add(new FakeModule("broken", failRegister: true), services, empty);
        return registry;
    }

    public static async Task RunAsync(Action<bool, string> check)
    {
        await RegistryAsync(check);
        await ControlAsync(check);
        await RestoreAsync(check);
        await ScreenAsync(check);
        TileAndText(check);
        await SchedulerAsync(check);
    }

    // The daily backup waits quietly while the backup module is out: the tick returns before it looks at the clock, the settings or the operations (all of which are absent here).
    private static async Task SchedulerAsync(Action<bool, string> check)
    {
        var clock = new ManualTimeProvider();
        var registry = new ModuleRegistry(clock);
        registry.Add(new FakeModule("backup"), new ServiceCollection(), new ConfigurationBuilder().Build());
        registry.Stop("backup", "pauza", "admin.demo");
        var scheduler = new BackupScheduler(new ConfigurationBuilder().Build(), new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider(), null!, new TestAuditTrail(),
            NullLoggerFactory.Instance, clock, null, registry);
        var tick = typeof(BackupScheduler).GetMethod("TickAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var finished = false;
        try { await ((Task)tick.Invoke(scheduler, [NullLogger.Instance, CancellationToken.None])!).WaitAsync(TimeSpan.FromSeconds(2)); finished = true; }
        catch (Exception) { }
        check(finished, "Scheduler: while the backup module is stopped by the administrator the scheduled tick does nothing (no clock check, no backup, no clean-up)");
    }

    private static async Task RegistryAsync(Action<bool, string> check)
    {
        var clock = new ManualTimeProvider();
        var registry = NewRegistry(clock);
        var changes = new List<ModuleChange>();
        registry.Subscribe(changes.Add);
        var changed = 0;
        registry.Changed += () => changed++;
        var startupChanges = changes.Count;

        check(registry.Stop("alpha", "mentenanta baza", "admin.demo") == ModuleToggleResult.Done && registry.StatusOf("alpha") == ModuleStatus.Disabled && !registry.IsAvailable("alpha"),
            "Switch: a module stopped by hand is out at once");
        var info = registry.Snapshot().Single(item => item.Id == "alpha");
        check(info.Origin == ModuleOrigins.Manual && info.Reason == "mentenanta baza" && info.StoppedBy == "admin.demo" && info.SinceUtc == clock.GetUtcNow().UtcDateTime,
            "Switch: the screen learns why, by whom and since when the module is stopped");
        check(!registry.NavItems().Any(item => item.Href == "/alpha") && registry.NavItems().Any(item => item.Href == "/beta"), "Switch: a stopped module leaves the menu, the others stay");
        check(Throws<ModuleUnavailableException>(() => registry.EnsureAvailable("alpha")), "Switch: the guarded entry points of a stopped module refuse the call");
        check(changed == 1 && changes.Count == startupChanges, "Switch: the screens are told (Changed) and no \"system\" change is written to the journal (the administrator's entry is)");

        registry.ReportFailure("alpha", new InvalidOperationException());
        registry.ReportSuccess("alpha");
        check(registry.IsManuallyStopped("alpha") && registry.Snapshot().Single(item => item.Id == "alpha").Failures == 0 && !registry.Retry("alpha"),
            "Switch: failures and successes of calls that were already running do not change a stopped module and \"try again\" does not apply");
        clock.Advance(ModuleRegistry.Cooldown + TimeSpan.FromMinutes(5));
        check(registry.StatusOf("alpha") == ModuleStatus.Disabled, "Switch: the cooldown of the circuit breaker does not bring a stopped module back");
        check(registry.Stop("alpha", "x", "admin.demo") == ModuleToggleResult.NoChange, "Switch: stopping a stopped module changes nothing");

        check(registry.Start("alpha") == ModuleToggleResult.Done && registry.StatusOf("alpha") == ModuleStatus.Healthy && registry.IsAvailable("alpha")
            && registry.Snapshot().Single(item => item.Id == "alpha").Origin is null, "Switch: started by hand, the module is healthy again");
        check(registry.Start("alpha") == ModuleToggleResult.NoChange, "Switch: starting a module that was not stopped by hand changes nothing");

        for (var i = 0; i < ModuleRegistry.FailureThreshold; i++) registry.ReportFailure("beta", new InvalidOperationException());
        check(registry.StatusOf("beta") == ModuleStatus.Disabled, "Switch: (setup) a module is out after repeated failures");
        check(registry.Stop("beta", "oprit", "admin.demo") == ModuleToggleResult.Done && !registry.Retry("beta") && registry.Snapshot().Single(item => item.Id == "beta").Origin == ModuleOrigins.Manual,
            "Switch: a module out for repeated failures can be stopped by hand; from then on the stop decides");
        check(registry.Start("beta") == ModuleToggleResult.Done && registry.StatusOf("beta") == ModuleStatus.Healthy && registry.Snapshot().Single(item => item.Id == "beta").Failures == 0,
            "Switch: starting by hand begins the circuit from scratch (no failures counted)");

        check(registry.Stop("jurnal", "x", "admin.demo") == ModuleToggleResult.Protected && registry.IsAvailable("jurnal") && !registry.CanToggle("jurnal"), "Switch: the journal cannot be stopped");
        check(registry.Stop("nu-exista", "x", "admin.demo") == ModuleToggleResult.Unknown && registry.Start("nu-exista") == ModuleToggleResult.Unknown, "Switch: an unknown module is refused");
        check(registry.Stop("broken", "x", "admin.demo") == ModuleToggleResult.NoChange && registry.Start("broken") == ModuleToggleResult.NeedsRestart && !registry.CanToggle("broken"),
            "Switch: a module that did not start cannot be switched by hand (its services are not registered: restart of the application)");

        var applied = registry.ApplyStopped([new("alpha", "salvat", "admin.demo", clock.GetUtcNow().UtcDateTime.AddHours(-3)), new("broken", "x", "a", DateTime.UtcNow),
            new("jurnal", "x", "a", DateTime.UtcNow), new("fantoma", "x", "a", DateTime.UtcNow)]);
        check(applied == 1 && registry.IsManuallyStopped("alpha") && registry.Snapshot().Single(item => item.Id == "alpha").SinceUtc == clock.GetUtcNow().UtcDateTime.AddHours(-3) && !registry.IsManuallyStopped("jurnal"),
            "Switch: the saved stops are put back after a restart (since the original time); the journal, a module that did not start and unknown modules are ignored");
        check(ModuleRegistry.IsFault(new ModuleOperationException("x")) == false, "Switch: the answers of the switch are not failures of a module");
        await Task.CompletedTask;
    }

    private static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); return false; }
        catch (T) { return true; }
    }

    private static async Task<bool> ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); return false; }
        catch (T) { return true; }
    }

    private static async Task ControlAsync(Action<bool, string> check)
    {
        var clock = new ManualTimeProvider();
        var registry = NewRegistry(clock);
        var store = new MemoryStore();
        var audit = new TestAuditTrail();
        var control = new ModuleControl(registry, store, new TestAccessControl(true, "admin.demo"), audit, clock);

        check(await ThrowsAsync<ModuleOperationException>(() => control.StopAsync("alpha", "   ")) && !registry.IsManuallyStopped("alpha") && store.Rows.Count == 0 && audit.Entries.Count == 0,
            "Switch: a stop without a reason is refused and nothing changes");
        check(await control.StopAsync("alpha", "  verificare retea  ") == ModuleToggleResult.Done && registry.IsManuallyStopped("alpha"), "Switch: the administrator stops a module with a reason");
        check(store.Rows.TryGetValue("alpha", out var row) && row.Reason == "verificare retea" && row.StoppedBy == "admin.demo" && row.StoppedUtc == clock.GetUtcNow().UtcDateTime,
            "Switch: the stop is saved (reason, who, when) so that it survives a restart");
        var stop = audit.Entries.SingleOrDefault(entry => entry.Action == AuditActions.ModuleManualStop);
        check(stop is { EntityType: AuditEntities.Module, EntityId: "alpha", ActorUsername: "admin.demo", Motif: "verificare retea" } && stop.Details.Contains("Funcționează") && stop.Details.Contains("Oprit de administrator"),
            "Switch: the journal has \"Oprire manuală modul\" under the administrator's name, with the old and new state and the reason");
        check(await control.StopAsync("jurnal", "x") == ModuleToggleResult.Protected && audit.Entries.Count == 1, "Switch: the journal module is refused and nothing is written");

        clock.Advance(TimeSpan.FromHours(2) + TimeSpan.FromMinutes(5));
        check(await control.StartAsync("alpha") == ModuleToggleResult.Done && registry.StatusOf("alpha") == ModuleStatus.Healthy && store.Rows.Count == 0, "Switch: the administrator starts the module; the saved stop is removed");
        var start = audit.Entries.SingleOrDefault(entry => entry.Action == AuditActions.ModuleManualStart);
        check(start is { EntityType: AuditEntities.Module, EntityId: "alpha", ActorUsername: "admin.demo" } && start.Details.Contains("2 h 5 min") && start.Details.Contains("verificare retea") && start.Details.Contains("admin.demo")
            && !string.IsNullOrWhiteSpace(start.Motif), "Switch: the journal has \"Pornire manuală modul\" with how long the module was stopped, who stopped it and why (the reason of the start is generated)");
        check(await control.StartAsync("alpha") == ModuleToggleResult.NoChange && audit.Entries.Count == 2, "Switch: starting a module that is running writes nothing");

        var ordinary = new ModuleControl(registry, store, new TestAccessControl(false, "operator"), audit, clock);
        check(await ThrowsAsync<AccessDeniedException>(() => ordinary.StopAsync("alpha", "x")) && await ThrowsAsync<AccessDeniedException>(() => ordinary.StartAsync("alpha")) && !registry.IsManuallyStopped("alpha"),
            "Switch: without the permission nothing is stopped or started");

        store.Down = true;
        check(await ThrowsAsync<InvalidOperationException>(() => control.StopAsync("alpha", "baza oprita")) && !registry.IsManuallyStopped("alpha") && audit.Entries.Count == 2,
            "Switch: when the state cannot be saved (the database does not answer) the module is not stopped and nothing is written to the journal");
        store.Down = false;

        var refusing = new ModuleControl(registry, store, new TestAccessControl(true, "admin.demo"), new RefusingAudit(), clock);
        check(await ThrowsAsync<InvalidOperationException>(() => refusing.StopAsync("alpha", "fara jurnal")) && !registry.IsManuallyStopped("alpha") && store.Rows.Count == 0,
            "Switch: when the journal cannot be written the stop is undone (the screen, the saved state and the journal agree)");
        await control.StopAsync("beta", "iar oprit");
        check(await ThrowsAsync<InvalidOperationException>(() => new ModuleControl(registry, store, new TestAccessControl(true, "admin.demo"), new RefusingAudit(), clock).StartAsync("beta"))
            && registry.IsManuallyStopped("beta") && store.Rows.ContainsKey("beta"), "Switch: when the journal cannot be written the start is undone too (the module stays stopped)");
        check(ModuleControl.Duration(TimeSpan.FromMinutes(5)) == "5 min" && ModuleControl.Duration(TimeSpan.FromHours(26) + TimeSpan.FromMinutes(3)) == "1 zi 2 h 3 min" && ModuleControl.Duration(TimeSpan.FromSeconds(10)) == "0 min",
            "Switch: the duration of a stop is written in days, hours and minutes");
        check(new[] { AuditActions.ModuleManualStop, AuditActions.ModuleManualStart }.All(AuditFilterOptions.Actions.Select(item => item.Value).Contains)
            && AuditActions.ModuleManualStop != AuditActions.ModuleManualStart && AuditActions.ModuleManualStop != AuditActions.ModuleDisabled,
            "Switch: the two actions have their own exact names and are in the filter of the journal page");
        check(Permissions.IsKnown("setari-module.toggle") && Permissions.IsAdminOnly("setari-module.toggle") && !Permissions.GrantableKeys.Contains("setari-module.toggle"),
            "Switch: the permission to stop / start is reserved for the administrator");
    }

    private static async Task RestoreAsync(Action<bool, string> check)
    {
        var clock = new ManualTimeProvider();
        var registry = NewRegistry(clock);
        var store = new MemoryStore { Down = true };
        store.Rows["alpha"] = new("alpha", "salvat", "admin.demo", clock.GetUtcNow().UtcDateTime.AddDays(-1));
        var restorer = new ModuleStateRestorer(registry, store, NullLogger<ModuleStateRestorer>.Instance) { RetryDelay = TimeSpan.FromMilliseconds(40) };
        check(!await restorer.RestoreAsync() && !registry.IsManuallyStopped("alpha"), "Restart: while the database does not answer the attempt fails quietly and the module is not touched yet");
        using var cancel = new CancellationTokenSource();
        await restorer.StartAsync(cancel.Token);
        await Task.Delay(150);
        check(!registry.IsManuallyStopped("alpha"), "Restart: the attempts go on in the background while the database is down");
        store.Down = false;
        var restored = false;
        for (var waited = 0; waited < 3000 && !restored; waited += 50) { restored = registry.IsManuallyStopped("alpha"); if (!restored) await Task.Delay(50); }
        check(restored && !registry.IsAvailable("alpha"), "Restart: as soon as the database answers the module stays stopped, as the administrator left it");
        await restorer.StopAsync(CancellationToken.None);
        restorer.Dispose();
    }

    private static async Task ScreenAsync(Action<bool, string> check)
    {
        var clock = new ManualTimeProvider();
        var registry = NewRegistry(clock);
        var store = new MemoryStore();
        var audit = new TestAuditTrail();
        var access = new TestAccessControl(true, "admin.demo");
        using var context = new BunitContext();
        context.Services.AddSingleton(registry);
        context.Services.AddSingleton<IAccessControl>(access);
        context.Services.AddSingleton<IAuditTrail>(audit);
        context.Services.AddSingleton<TimeProvider>(clock);
        context.Services.AddSingleton(new ModuleControl(registry, store, access, audit, clock));
        var panel = context.Render<ModulesPanel>();
        check(panel.FindAll("tbody tr").Count == 4 && panel.FindAll("tbody input[role=switch]").Count == 4, "Screen: every module has its switch");
        check(panel.Find("#module-switch-jurnal").HasAttribute("disabled") && panel.Find("#module-switch-broken").HasAttribute("disabled") && !panel.Find("#module-switch-alpha").HasAttribute("disabled"),
            "Screen: the journal and a module that did not start have the switch disabled; the others can be switched");
        check(panel.FindAll(".module-stop-confirm").Count == 0, "Screen: no confirmation until a stop is asked for");

        panel.Find("#module-switch-alpha").Change(false);
        check(panel.FindAll(".module-stop-confirm").Count == 1 && panel.Find(".module-stop-confirm").TextContent.Contains("Modul alpha") && !registry.IsManuallyStopped("alpha"),
            "Screen: turning a switch off asks for the reason first and stops nothing yet");
        panel.Find(".module-stop-confirm button.danger").Click();
        check(registry.IsAvailable("alpha") && panel.Markup.Contains("validation-message"), "Screen: without a reason the module is not stopped and the field says why");
        panel.Find(".module-stop-confirm button.act-close").Click();
        check(panel.FindAll(".module-stop-confirm").Count == 0 && registry.IsAvailable("alpha") && panel.Find("#module-switch-alpha").HasAttribute("checked"), "Screen: giving up leaves the module running and the switch on");

        panel.Find("#module-switch-alpha").Change(false);
        panel.Find("#module-stop-reason").Input("test din ecran");
        panel.Find(".module-stop-confirm button.danger").Click();
        check(registry.IsManuallyStopped("alpha") && store.Rows.ContainsKey("alpha") && audit.Entries.Any(entry => entry.Action == AuditActions.ModuleManualStop && entry.Motif == "test din ecran"),
            "Screen: with a reason the module is stopped, saved and written to the journal");
        var row = panel.FindAll("tbody tr")[0].TextContent;
        check(row.Contains("Oprit") && row.Contains(ModuleOrigins.Manual) && row.Contains("admin.demo") && row.Contains("test din ecran") && !panel.Find("#module-switch-alpha").HasAttribute("checked")
            && panel.FindAll("tbody tr")[0].QuerySelectorAll("button.row-icon.renew").Length == 0, "Screen: the row shows \"stopped by the administrator\", who and why; the switch is off and there is no \"try again\"");
        check(panel.Markup.Contains("a fost oprit"), "Screen: the screen confirms the stop");

        panel.Find("#module-switch-alpha").Change(true);
        check(registry.StatusOf("alpha") == ModuleStatus.Healthy && store.Rows.Count == 0 && audit.Entries.Any(entry => entry.Action == AuditActions.ModuleManualStart) && panel.Markup.Contains("a fost pornit"),
            "Screen: turning the switch on starts the module at once and the saved stop is removed");

        // A second administrator's screen follows the change without a reload.
        var other = context.Render<ModulesPanel>();
        registry.Stop("beta", "din alta sesiune", "alt.admin");
        try { other.WaitForState(() => other.Markup.Contains("alt.admin"), TimeSpan.FromSeconds(3)); } catch (Exception) { }
        check(other.Markup.Contains("alt.admin"), "Screen: a screen that is already open follows a stop made elsewhere");
        registry.Start("beta");

        using var denied = new BunitContext();
        denied.Services.AddSingleton(registry);
        denied.Services.AddSingleton<IAccessControl>(new TestAccessControl(false, "operator"));
        denied.Services.AddSingleton<IAuditTrail>(audit);
        denied.Services.AddSingleton<TimeProvider>(clock);
        denied.Services.AddSingleton(new ModuleControl(registry, store, new TestAccessControl(false, "operator"), audit, clock));
        check(denied.Render<ModulesPanel>().FindAll("tbody input[role=switch]:not([disabled])").Count == 0, "Screen: without the permission every switch is disabled");
        await Task.CompletedTask;
    }

    private static void TileAndText(Action<bool, string> check)
    {
        var clock = new ManualTimeProvider();
        var registry = NewRegistry(clock);
        using var context = new BunitContext();
        var authorization = context.AddAuthorization();
        authorization.SetAuthorized("admin.demo");
        authorization.SetPolicies("perm:x.view");
        context.Services.AddSingleton(registry);
        var tile = context.Render<ModuleTile>(parameters => parameters
            .Add(p => p.ModuleId, "alpha").Add(p => p.Policy, "perm:x.view").Add(p => p.Href, "/alpha").Add(p => p.Icon, "▤").Add(p => p.Title, "Sectiunea alpha").Add(p => p.Description, "Descriere alpha"));
        check(tile.FindAll("a[href='/alpha']").Count == 1 && tile.FindAll(".main-menu-off").Count == 0, "Tile: a module that runs has its usual link on the main page");
        registry.Stop("alpha", "pauza", "admin.demo");
        try { tile.WaitForState(() => tile.FindAll(".main-menu-off").Count == 1, TimeSpan.FromSeconds(3)); } catch (Exception) { }
        check(tile.FindAll(".main-menu-off").Count == 1 && tile.FindAll("a").Count == 0 && tile.Markup.Contains("Sectiunea alpha") && tile.Markup.Contains("Oprit temporar de administrator")
            && tile.Find(".main-menu-off").GetAttribute("aria-disabled") == "true" && !tile.Markup.Contains("pauza"),
            "Tile: a module stopped by the administrator stays on the main page, grey and without a link, with the words \"Oprit temporar de administrator\" (the reason is not shown to users)");
        registry.Start("alpha");
        try { tile.WaitForState(() => tile.FindAll("a[href='/alpha']").Count == 1, TimeSpan.FromSeconds(3)); } catch (Exception) { }
        check(tile.FindAll("a[href='/alpha']").Count == 1, "Tile: the link is back as soon as the module is started, without a reload");
        for (var i = 0; i < ModuleRegistry.FailureThreshold; i++) registry.ReportFailure("alpha", new InvalidOperationException());
        try { tile.WaitForState(() => tile.FindAll(".main-menu-off").Count == 1, TimeSpan.FromSeconds(3)); } catch (Exception) { }
        check(tile.FindAll(".main-menu-off").Count == 1 && tile.Markup.Contains("Indisponibil momentan"), "Tile: a module out after repeated failures is grey too, with its own words");

        var unavailable = context.Render<ModuleUnavailable>(parameters => parameters.Add(p => p.ModuleId, "alpha"));
        check(unavailable.Markup.Contains("Modul indisponibil"), "Notice: a module out for failures keeps the usual notice");
        registry.Start("beta");
        registry.Stop("beta", "motiv intern", "admin.demo");
        var stopped = context.Render<ModuleUnavailable>(parameters => parameters.Add(p => p.ModuleId, "beta"));
        check(stopped.Markup.Contains("Modul oprit temporar") && stopped.Markup.Contains("oprită de administrator") && !stopped.Markup.Contains("motiv intern"),
            "Notice: the page of a module stopped by the administrator says so, without the reason");
    }

    /// <summary>A switch over a memory store, for the other screens' checks that render the panel.</summary>
    internal static ModuleControl NewControl(ModuleRegistry registry, IAccessControl access, IAuditTrail audit, TimeProvider clock) =>
        new(registry, new MemoryStore(), access, audit, clock);
}
