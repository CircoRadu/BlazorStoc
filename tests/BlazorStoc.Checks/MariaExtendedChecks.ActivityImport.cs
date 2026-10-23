using BlazorStoc.Modules.ActivityImport;
using BlazorStoc.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using MySqlConnector;

namespace BlazorStoc.Checks;

public static partial class MariaExtendedChecks
{
    private sealed class FakeActivitySource : IActivitySource
    {
        public ActivityPing Ping = new(true, "ok", "15.0.2000", false);
        public IReadOnlyList<ActivityColumn> Columns = [];
        public IReadOnlyList<ActivityTypeRow> Types = [];
        public Exception? Failure;
        public int Calls;
        public ActivityConnection? LastConnection;
        public IReadOnlyList<ActivityPartner> Partners = [];
        public IReadOnlyList<ActivityWorkPointRow> Points = [];
        public IReadOnlyList<ActivityTicket> TicketRows = [];
        public StructureReport? LastTicketStructure;
        // The IDs that still exist in ActivityApp, per kind of row; a kind not listed here: every ID exists.
        public Dictionary<string, HashSet<long>> Live = [];

        public Task<IReadOnlySet<long>> ReadLiveIdsAsync(ActivityConnection connection, StructureReport structure, string kind, IReadOnlyList<long> ids, CancellationToken cancellationToken)
        {
            Calls++;
            if (Failure is not null) return Task.FromException<IReadOnlySet<long>>(Failure);
            return Task.FromResult<IReadOnlySet<long>>(Live.TryGetValue(kind, out var live) ? ids.Where(live.Contains).ToHashSet() : ids.ToHashSet());
        }

        public Task<IReadOnlyList<ActivityTicket>> ReadTicketsAsync(ActivityConnection connection, StructureReport structure, IReadOnlyList<long> typeIds, DateTime since, DateTime? modifiedSince, int pageSize, CancellationToken cancellationToken)
        {
            Calls++; LastTicketStructure = structure;
            return Failure is null
                ? Task.FromResult<IReadOnlyList<ActivityTicket>>([.. TicketRows.Where(ticket => typeIds.Contains(ticket.TypeId) && (ticket.TicketDate ?? DateTime.MinValue) >= since && (modifiedSince is null || ticket.EventModified is null || ticket.EventModified >= modifiedSince))])
                : Task.FromException<IReadOnlyList<ActivityTicket>>(Failure);
        }

        public Task<IReadOnlyList<ActivityPartner>> ReadPartnersAsync(ActivityConnection connection, StructureReport structure, int pageSize, CancellationToken cancellationToken)
        {
            Calls++;
            return Failure is null ? Task.FromResult(Partners) : Task.FromException<IReadOnlyList<ActivityPartner>>(Failure);
        }
        public Task<IReadOnlyList<ActivityWorkPointRow>> ReadWorkPointsAsync(ActivityConnection connection, StructureReport structure, int pageSize, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Points);
        }

        public Task<ActivityPing> PingAsync(ActivityConnection connection, CancellationToken cancellationToken) { Calls++; LastConnection = connection; return Task.FromResult(Ping); }
        public Task<IReadOnlyList<ActivityColumn>> ReadStructureAsync(ActivityConnection connection, CancellationToken cancellationToken)
        {
            Calls++; LastConnection = connection;
            return Failure is null ? Task.FromResult(Columns) : Task.FromException<IReadOnlyList<ActivityColumn>>(Failure);
        }
        public Task<IReadOnlyList<ActivityTypeRow>> ReadTypesAsync(ActivityConnection connection, StructureReport structure, CancellationToken cancellationToken) { Calls++; return Task.FromResult(Types); }
    }

    // ---- Preluare ActivityApp, stage 1: settings (encrypted password), test, structure state, ticket types, journal, administrator only ------------------
    private static async Task ActivityImportAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe)
    {
        var protection = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(Path.GetTempPath(), "blazorstoc-ai-keys-" + Guid.NewGuid().ToString("N"))), options => options.SetApplicationName("BlazorStoc"));
        var started = DateTime.UtcNow.AddSeconds(-2);
        var fake = new FakeActivitySource();
        var service = new MariaActivityImportService(configuration, protection, fake, admin, audit);
        var password = "Parola-" + Suffix() + ";x=1";
        var marker = "SERVER-" + Suffix();
        var columns = ActivityContract.Tables.SelectMany(table => table.Columns.Select(column => new ActivityColumn(table.Name, column.Name,
            column.Kind switch { ColumnKind.Number => "bigint", ColumnKind.Text => "nvarchar", ColumnKind.Date => "datetime", _ => "bit" }))).ToList();
        try
        {
            await Exec(probe, "DELETE FROM activity_import_types");
            await Exec(probe, "DELETE FROM activity_import_settings");
            var empty = await service.GetSettingsAsync();
            Check(!empty.Enabled && empty.Port == 1433 && empty.Database == "TIPActivity" && !empty.HasPassword && empty.StructureStatus == StructureStatus.Unchecked, "Activity import: before any save the settings are the defaults (off, port 1433, TIPActivity, no password)");

            var input = new ActivityImportInput(true, marker, 1433, "TIPActivity", "citire", password, null, 22, 6, 300, true);
            await service.SaveSettingsAsync(input);
            var saved = await service.GetSettingsAsync();
            var raw = await Scalar<string>(probe, "SELECT password_protected FROM activity_import_settings WHERE id = 1");
            Check(saved.Enabled && saved.Server == marker && saved.User == "citire" && saved.HasPassword && saved.WindowFromHour == 22 && saved.WindowToHour == 6 && saved.PageSize == 300 && saved.FillEmptyFields && saved.UpdatedBy == "integration.tester",
                "Activity import: the settings are saved and read back with who saved them");
            Check(raw is { Length: > 0 } && !raw.Contains(password, StringComparison.Ordinal) && !raw.Contains("Parola", StringComparison.Ordinal), "Activity import: the password is stored encrypted, never in clear");

            await service.SaveSettingsAsync(input with { Password = null, PageSize = 250 });
            var kept = await service.TestAsync(input with { Password = null });
            Check((await service.GetSettingsAsync()).PageSize == 250 && fake.LastConnection?.Password == password, "Activity import: leaving the password empty keeps the stored one (the test connects with it)");

            fake.Columns = columns;
            var ok = await service.TestAsync(input with { Password = null });
            var afterOk = await service.GetSettingsAsync();
            Check(ok.Ok && ok.Structure == StructureStatus.Ok && afterOk.LastTestOk == true && afterOk.StructureStatus == StructureStatus.Ok && afterOk.StructureFingerprint.Length == 64 && afterOk.StructureIssues.Count == 0 && afterOk.LastTestUtc is not null,
                "Activity import: a successful test remembers the result and the fingerprint of the structure");

            fake.Columns = [.. columns.Where(column => column is not { Table: "Tickete", Column: "IDEveniment" })];
            var changed = await service.TestAsync(input with { Password = null });
            var afterChange = await service.GetSettingsAsync();
            Check(changed.Ok && changed.StructureChanged && changed.Structure == StructureStatus.Changed && afterChange.StructureStatus == StructureStatus.Changed
                && afterChange.StructureIssues.Any(issue => issue is { Table: "Tickete", Column: "IDEveniment", Required: true }) && afterChange.StructureFingerprint != afterOk.StructureFingerprint,
                "Activity import: a change of structure is detected (fingerprint), stored with the exact column and reported, without failing");

            fake.Ping = new ActivityPing(false, "Autentificarea a fost refuzată.");
            var refused = await service.TestAsync(input with { Password = null });
            var afterRefused = await service.GetSettingsAsync();
            Check(!refused.Ok && afterRefused.LastTestOk == false && afterRefused.StructureStatus == StructureStatus.Changed, "Activity import: a refused connection is an answer: it is remembered and the last known structure stays");

            fake.Ping = new ActivityPing(true, "ok", "15.0", true);
            fake.Columns = columns;
            var writable = await service.TestAsync(input with { Password = null });
            Check(writable.WritableAccount && writable.Message.Contains("drepturi de scriere", StringComparison.Ordinal), "Activity import: an account that is able to write is warned about in the test result");

            fake.Types = [new(4, "Service", "S", true, false, 293, 79, 6), new(13, "Mentenanță", "M", true, false, 171, 91, 12), new(25, "Intervenție la cerere", "I", false, false, 239, 105, 15)];
            var view = await service.GetTypesAsync();
            Check(view.SourceError is null && view.Items.Count == 3 && view.Items.All(item => !item.Selected && item.State == ""), "Activity import: the types are read from the source and none is selected at first");
            await service.SaveTypesAsync([new(4, "Service", true, ActivityDestination.OnDemand), new(13, "Mentenanță", true, ActivityDestination.Maintenance), new(25, "Intervenție la cerere", false, ActivityDestination.OnDemand)]);
            var chosen = await service.GetTypesAsync();
            Check(chosen.Items.Single(item => item.Id == 4) is { Selected: true, Destination: ActivityDestination.OnDemand } && chosen.Items.Single(item => item.Id == 13) is { Selected: true, Destination: ActivityDestination.Maintenance }
                && chosen.Items.Single(item => item.Id == 25) is { Selected: false, State: ActivityImportRules.StateNew }, "Activity import: the chosen types and their destinations are saved by the ID of the type; a type not chosen shows as new");

            fake.Types = [new(4, "Service tehnic", "S", true, false, 293, 79, 6), fake.Types[1], fake.Types[2]];
            var renamed = await service.GetTypesAsync();
            Check(renamed.Items.Single(item => item.Id == 4) is { Selected: false, State: ActivityImportRules.StateChanged }, "Activity import: a type renamed in ActivityApp is \"changed in source\" and drops out of the choice until confirmed");
            await service.SaveTypesAsync([new(4, "Service tehnic", true, ActivityDestination.OnDemand), new(13, "Mentenanță", true, ActivityDestination.Maintenance), new(25, "Intervenție la cerere", false, ActivityDestination.OnDemand)]);
            Check((await service.GetTypesAsync()).Items.Single(item => item.Id == 4) is { Selected: true, State: ActivityImportRules.StateSelected }, "Activity import: saving the list again confirms the new name");

            await service.SaveTypesAsync([new(4, "Service tehnic", false, ActivityDestination.OnDemand), new(13, "Mentenanță", true, ActivityDestination.Maintenance), new(25, "Intervenție la cerere", false, ActivityDestination.OnDemand)]);
            var removed = await Scalar<long>(probe, "SELECT COUNT(*) FROM activity_import_types");
            Check(removed == 1, "Activity import: a type taken out of the choice is removed from it (nothing else is touched)");

            fake.Failure = new ActivitySourceException("ActivityApp nu a răspuns la timp.");
            var down = await service.GetTypesAsync();
            Check(down.SourceError is not null && down.Items.Any(item => item.Id == 13 && item.State == ActivityImportRules.StateMissing), "Activity import: with the source down the list says so and still shows the saved choice");
            fake.Failure = null;

            var events = (await audit.QueryAsync(new AuditQuery(Entity: AuditEntities.ActivityImport), 1, 100)).Events.Where(entry => entry.TimestampUtc >= started).ToList();
            var settingsEvents = events.Where(entry => entry.Action == AuditActions.SetActivityImport).ToList();
            Check(events.Any(entry => entry.Action == AuditActions.SetActivityImportTypes && entry.Details.Contains("Mentenanță", StringComparison.Ordinal)) && events.Count(entry => entry.Action == AuditActions.TestActivityImport) >= 4
                && settingsEvents.Count == 2 && settingsEvents.Any(entry => entry.Details.Contains("schimbată", StringComparison.Ordinal)) && !events.Any(entry => entry.Details.Contains(password, StringComparison.Ordinal) || entry.Details.Contains("Parola-", StringComparison.Ordinal)),
                "Activity import: every save, test and choice of types is journaled by its exact action; the password appears nowhere in the journal");

            var operatorAccess = new TestAccessControl(false, "operator.ext");
            var denied = new MariaActivityImportService(configuration, protection, fake, operatorAccess, audit);
            var refusals = 0;
            foreach (var operation in new Func<Task>[] { () => denied.GetSettingsAsync(), () => denied.SaveSettingsAsync(input), () => denied.TestAsync(input), () => denied.GetTypesAsync(), () => denied.SaveTypesAsync([]) })
                try { await operation(); } catch (AccessDeniedException) { refusals++; }
            Check(refusals == 5, "Activity import: a user who is not the administrator can neither read nor change the settings, test the connection or choose the types");

            var invalid = 0;
            foreach (var bad in new[] { input with { User = "sa" }, input with { Port = 0 }, input with { Server = "x;Password=y" } })
                try { await service.SaveSettingsAsync(bad); } catch (ActivityImportException) { invalid++; }
            try { await service.SaveSettingsAsync(input with { Enabled = true, Password = null, Server = "" }); } catch (ActivityImportException) { invalid++; }
            Check(invalid == 4 && (await service.GetSettingsAsync()).Server == marker, "Activity import: invalid settings (account sa, port, server with separators, enabled without a server) are refused and change nothing");
        }
        finally
        {
            await Exec(probe, "DELETE FROM activity_import_types");
            await Exec(probe, "DELETE FROM activity_import_settings");
        }
    }

    private static async Task Exec(MySqlConnection connection, string sql)
    {
        await using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<T> Scalar<T>(MySqlConnection connection, string sql)
    {
        await using var command = new MySqlCommand(sql, connection);
        return (T)Convert.ChangeType(await command.ExecuteScalarAsync() ?? default(T)!, typeof(T));
    }
}
