using BlazorStoc.Modules;
using BlazorStoc.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;

namespace BlazorStoc.Checks;

public static partial class MariaExtendedChecks
{
    // ---- Modules stopped by hand: saved in module_state, put back at the next start, journal ---------------------------------------
    private static async Task ModuleStateAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe)
    {
        var store = new MariaModuleStateStore(configuration);
        var id = "ext-modul-" + Suffix();
        var at = new DateTime(2026, 10, 19, 9, 30, 0, DateTimeKind.Utc);
        try
        {
            await store.SaveStoppedAsync(new StoppedModule(id, "verificare retea", "admin.ext", at));
            var rows = await store.ReadStoppedAsync();
            Check(rows.Any(row => row.ModuleId == id && row.Reason == "verificare retea" && row.StoppedBy == "admin.ext" && row.StoppedUtc == at && row.StoppedUtc.Kind == DateTimeKind.Utc),
                "A stopped module is saved with its reason, who and when (UTC)");
            await store.SaveStoppedAsync(new StoppedModule(id, "alt motiv", "alt.admin", at.AddHours(1)));
            var again = (await store.ReadStoppedAsync()).Where(row => row.ModuleId == id).ToList();
            Check(again.Count == 1 && again[0].Reason == "alt motiv" && again[0].StoppedBy == "alt.admin", "Saving the same module again replaces its row (one row per module)");

            // The restart: a new registry reads the row back and the module stays out (a module of the registry with the same id).
            var registry = new ModuleRegistry();
            registry.Add(new ExtModule(id), new Microsoft.Extensions.DependencyInjection.ServiceCollection(), new ConfigurationBuilder().Build());
            var restorer = new ModuleStateRestorer(registry, store, NullLogger<ModuleStateRestorer>.Instance);
            Check(await restorer.RestoreAsync() && registry.IsManuallyStopped(id) && !registry.IsAvailable(id), "After a restart the module saved as stopped stays stopped");

            var control = new ModuleControl(registry, store, admin, audit, TimeProvider.System);
            Check(await control.StartAsync(id) == ModuleToggleResult.Done && registry.IsAvailable(id) && !(await store.ReadStoppedAsync()).Any(row => row.ModuleId == id),
                "Starting the module removes its saved stop");
            Check(await control.StopAsync(id, "motiv scris in baza de test") == ModuleToggleResult.Done && (await store.ReadStoppedAsync()).Any(row => row.ModuleId == id), "Stopping by hand saves the stop in the database");
            var entries = await audit.QueryAsync(new AuditQuery(Text: id, Entity: AuditEntities.Module), 1, 20);
            Check(entries.Events.Any(entry => entry.Action == AuditActions.ModuleManualStop && entry.EntityId == id && entry.Motif == "motiv scris in baza de test" && entry.ActorUsername == "integration.tester")
                && entries.Events.Any(entry => entry.Action == AuditActions.ModuleManualStart && entry.EntityId == id), "The journal in the database has the stop (with its reason) and the start, under the administrator's name");
        }
        finally
        {
            await store.ClearStoppedAsync(id);
        }
        Check(!(await store.ReadStoppedAsync()).Any(row => row.ModuleId == id), "The saved stop is gone after clean-up");
    }

    private sealed class ExtModule(string id) : IModule
    {
        public string Id => id;
        public string Name => "Modul " + id;
        public Version Version { get; } = new(1, 0, 0);
        public IReadOnlyList<ModuleNavItem> Nav => [];
        public void Register(Microsoft.Extensions.DependencyInjection.IServiceCollection services, IConfiguration configuration) { }
    }
}
