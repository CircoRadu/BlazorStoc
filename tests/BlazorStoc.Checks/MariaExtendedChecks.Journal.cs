using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using BlazorStoc.Services;

namespace BlazorStoc.Checks;

public static partial class MariaExtendedChecks
{
    // ---- Backup NAS settings --------------------------------------------------------------------------------------------------
    private static async Task NasBackupSettingsAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe)
    {
        var started = DateTime.UtcNow.AddSeconds(-1);
        var protection = Microsoft.AspNetCore.DataProtection.DataProtectionProvider.Create("BlazorStoc.Checks");
        var store = new MariaNasBackupStore(configuration, protection, admin, audit);
        var backupStore = new MariaBackupSettingsStore(configuration, admin, audit);
        const string secret = "S3cret-Nas-Pass-9";
        await using (var cleanup = new MySqlCommand("DELETE FROM backup_nas_settings WHERE id=1; DELETE FROM backup_settings WHERE id=1", probe)) await cleanup.ExecuteNonQueryAsync();
        try
        {
            Check((await store.GetAsync()).HasPassword == false && (await store.GetAsync()).Path == "", "NAS backup settings: nothing is configured at first");
            var alertState = await new MariaBackupAlertReader(configuration).GetAsync();
            Check(!alertState.NasCopyEnabled && alertState.MaxAgeDays == BackupAlertRules.BackupMaxAgeDays && alertState.FirstUseUtc <= DateTime.UtcNow.AddSeconds(1),
                "Backup notifications: the reader works on the real database (NAS copy off, default age while nothing is configured)");
            await store.SaveAsync(new(@"\\nas\share\folder\", "espstoc", secret, true));
            var saved = await store.GetAsync();
            await using var raw = new MySqlCommand("SELECT password_protected FROM backup_nas_settings WHERE id=1", probe);
            var stored = (string?)await raw.ExecuteScalarAsync();
            Check(saved.Path == @"\\nas\share\folder" && saved.UserName == "espstoc" && saved.HasPassword && saved.CopyEnabled && stored is { Length: > 20 } && !stored.Contains(secret),
                "NAS backup settings: path, account and the copy switch are saved, the password only encrypted");
            var credentials = await new MariaNasBackupStore(configuration, protection).CredentialsAsync(default);
            Check(credentials is not null && credentials.Password == secret && credentials.UserName == "espstoc", "NAS backup settings: the stored password decrypts for the copy");
            await store.SaveAsync(new(@"\\nas\share", "espstoc", null, false));
            var kept = await new MariaNasBackupStore(configuration, protection).CredentialsAsync(default);
            Check(kept?.Password == secret && !(await store.GetAsync()).CopyEnabled, "NAS backup settings: an empty password on save keeps the stored one");
            var denied = await Rejects<AccessDeniedException>(() => new MariaNasBackupStore(configuration, protection, new TestAccessControl(false, "limited")).GetAsync(), "A limited user cannot read the settings");
            var invalid = await Rejects<NasBackupException>(() => store.SaveAsync(new(@"C:\x", "u", null, true)), "A local path is refused");
            var wrongKeys = await new MariaNasBackupStore(configuration, Microsoft.AspNetCore.DataProtection.DataProtectionProvider.Create("Other.Keys")).ReadAsync(default);
            Check(denied is not null && invalid is not null && !wrongKeys.Settings.HasPassword, "NAS backup settings: only administrators, a local path is refused, a password that cannot be decrypted asks to be typed again");
            var events = (await audit.GetEventsAsync()).Where(item => item.TimestampUtc >= started && item.Action == AuditActions.SetNasBackup).ToList();
            Check(events.Count >= 2 && events.All(item => !item.Details.Contains(secret)) && events.Any(item => item.Details.Contains("Parola NAS: — → schimbată")),
                "NAS backup settings: every change is journaled, the password only as changed, never its value");

            // The backup settings (Settings -> Backup): schedule, age for the notification, retention of old packages.
            async Task<string?> ScheduledDate() { await using var query = new MySqlCommand("SELECT last_scheduled_date FROM backup_settings WHERE id=1", probe); return await query.ExecuteScalarAsync() as string; }
            Check(!(await backupStore.GetAsync()).ScheduleEnabled && (await backupStore.GetAsync()).MaxAgeDays == 2 && !(await backupStore.GetAsync()).RetentionEnabled, "Backup settings: defaults while nothing is saved (no schedule, 2 days, no clean-up)");
            await backupStore.SaveAsync(new(true, "00:00", 2, false, 30));
            var pastTime = await ScheduledDate();
            await backupStore.SaveAsync(new(true, "23:59", 2, false, 30));
            var futureTime = await ScheduledDate();
            Check(pastTime == DateTime.Now.ToString("yyyy-MM-dd") && futureTime is null,
                "Backup settings: a new schedule time already past waits for tomorrow, a time still ahead runs today (the earlier run of the day does not block it)");
            await backupStore.SaveAsync(new(true, "23:59", 7, true, 14));
            var withAge = await backupStore.GetAsync();
            var badAge = await Rejects<NasBackupException>(() => backupStore.SaveAsync(new(true, "23:59", 0, false, 30)), "An age below one day is refused");
            var tooOld = await Rejects<NasBackupException>(() => backupStore.SaveAsync(new(true, "23:59", 31, false, 30)), "An age above thirty days is refused");
            var badRetention = await Rejects<NasBackupException>(() => backupStore.SaveAsync(new(true, "23:59", 2, true, 3)), "A retention below seven days is refused");
            await new MariaBackupSettingsStore(configuration).RecordBackupErrorAsync("cauza de proba", default);
            var withError = await new MariaBackupAlertReader(configuration).GetAsync();
            Check(withAge.MaxAgeDays == 7 && withAge.RetentionEnabled && withAge.RetentionDays == 14 && badAge is not null && tooOld is not null && badRetention is not null
                  && withError.MaxAgeDays == 7 && withError.LastError == "cauza de proba" && withError.LastErrorUtc is not null,
                "Backup settings: age (1-30 days) and retention (7+ days) are configurable, the last backup failure is kept for the notification text");
            await backupStore.SaveAsync(new(true, "23:59", 7, true, 14, true, "time.google.com, ntp.firma.local"));
            var withNtp = await backupStore.GetAsync();
            var badNtp = await Rejects<NasBackupException>(() => backupStore.SaveAsync(new(true, "23:59", 7, true, 14, true, "https://pool.ntp.org")), "A web address is refused as an NTP server");
            var system = new MariaBackupSettingsStore(configuration);
            await system.RecordClockAsync(new ClockCheck(false, DateTime.UtcNow, TimeSpan.FromDays(30), "decalat"), default);
            var clockIssue = await new MariaBackupAlertReader(configuration).GetAsync();
            await system.RecordClockAsync(new ClockCheck(false, null, null, "fara internet"), default);
            var stillIssue = await new MariaBackupAlertReader(configuration).GetAsync();
            await system.RecordClockAsync(new ClockCheck(true, DateTime.UtcNow, TimeSpan.Zero, ""), default);
            var clearedIssue = await new MariaBackupAlertReader(configuration).GetAsync();
            Check(withNtp.TimeCheckEnabled && withNtp.NtpServers == "time.google.com, ntp.firma.local" && badNtp is not null
                  && clockIssue.ClockIssueUtc is not null && clockIssue.ClockSkewMinutes == 43200 && stillIssue.ClockIssueUtc is not null && clearedIssue.ClockIssueUtc is null,
                "Backup settings: NTP servers are saved and validated; a wrong clock is recorded, an unverifiable check leaves it as it was, a right clock clears it");
            await backupStore.SaveAsync(new(true, "23:59", 2, false, 30, true, "", 1 | 8));
            var withDays = await backupStore.GetAsync();
            var effectiveAge = (await new MariaBackupAlertReader(configuration).GetAsync()).MaxAgeDays;
            var noDays = await Rejects<NasBackupException>(() => backupStore.SaveAsync(new(true, "23:59", 2, false, 30, true, "", 0)), "A scheduled backup with no day is refused");
            Check(withDays.ScheduleDays == 9 && effectiveAge == 5 && noDays is not null, "Backup settings: the weekdays are kept (Monday and Thursday), and the missing-backup age follows the longest stretch (4 days + 1)");
            await backupStore.SaveAsync(new(true, "23:59", 7, true, 14));
            var deniedBackup = await Rejects<AccessDeniedException>(() => new MariaBackupSettingsStore(configuration, new TestAccessControl(false, "limited")).GetAsync(), "A limited user cannot read the backup settings");
            var backupEvents = (await audit.GetEventsAsync()).Where(item => item.TimestampUtc >= started && item.Action == AuditActions.SetBackupSettings).ToList();
            Check(deniedBackup is not null && backupEvents.Count >= 3 && backupEvents.Any(item => item.Details.Contains("Ștergere pachete vechi")) && backupEvents.Any(item => item.Details.Contains("Vechime maximă backup")),
                "Backup settings: administrators only, every change is journaled with its own action");
        }
        finally { await using var cleanup = new MySqlCommand("DELETE FROM backup_nas_settings WHERE id=1; DELETE FROM backup_settings WHERE id=1", probe); await cleanup.ExecuteNonQueryAsync(); }
    }

    private static async Task DefaultTemplatesAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe)
    {
        var started = DateTime.UtcNow.AddSeconds(-1);
        var repository = new MariaExpiryNotificationRepository(configuration);
        var reader = new MariaBackupAlertReader(configuration);
        IExpirySource[] sources = [new BackupMissingSource(reader, "sistem.proba-a"), new NasCopyMissingSource(reader, "sistem.proba-b")];
        async Task Clean()
        {
            foreach (var template in (await repository.GetTemplatesAsync()).Where(item => item.SourceKey.StartsWith("sistem.proba-", StringComparison.Ordinal))) await repository.DeleteTemplateAsync(template);
            await using var seeds = new MySqlCommand("DELETE FROM notification_template_seeds WHERE source_key LIKE 'sistem.proba-%'", probe); await seeds.ExecuteNonQueryAsync();
        }
        await Clean();
        try
        {
            var first = await DefaultNotificationTemplates.SeedAsync(configuration, repository, sources, audit, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
            var made = (await repository.GetTemplatesAsync()).Where(item => item.SourceKey.StartsWith("sistem.proba-", StringComparison.Ordinal)).ToList();
            Check(first == 2 && made.Count == 2 && made.All(item => item.Active && item.ThresholdDays >= 1 && item.Subject.Length > 0 && item.Body.Length > 0),
                "Default templates: every event without a template gets an active one from its own default text");
            var again = await DefaultNotificationTemplates.SeedAsync(configuration, repository, sources, audit, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
            await repository.DeleteTemplateAsync(made[0]);
            var afterDelete = await DefaultNotificationTemplates.SeedAsync(configuration, repository, sources, audit, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
            Check(again == 0 && afterDelete == 0 && (await repository.GetTemplatesAsync()).Count(item => item.SourceKey.StartsWith("sistem.proba-", StringComparison.Ordinal)) == 1,
                "Default templates: made only once, a template the administrator deleted is not made again");
            var events = (await audit.GetEventsAsync()).Count(item => item.TimestampUtc >= started && item.Action == AuditActions.CreateNotificationTemplate);
            Check(events >= 2, "Default templates: each one is journaled as a template creation");
        }
        finally { await Clean(); }
    }

    // ---- Change events ---------------------------------------------------------------------------------------------------------------
    private static async Task AuditQueryAsync(IConfiguration configuration, IAuditTrail audit, MySqlConnection probe)
    {
        // The plain journal page: no filters, the latest 500 events (and the whole journal), then the removal times of that page.
        var plain = await audit.QueryAsync(new(), 1, 10, AuditQueryRules.RecentWindow);
        var plainAll = await audit.QueryAsync(new(), 1, 25);
        _ = await audit.RemovalTimesAsync(plain.Events);
        _ = await audit.RemovalTimesAsync(plainAll.Events);
        Check(plain.Events.Count == Math.Min(10, plain.Total) && plainAll.Total == plainAll.JournalTotal, "The unfiltered journal page is answered by the server");
        var suffix = Suffix();
        var target = $"Ext Jurnal {suffix}";
        var before = await audit.SummaryAsync(DateTime.UtcNow.Date);
        var start = DateTime.UtcNow.AddMinutes(-1);
        for (var index = 1; index <= 7; index++)
            await audit.RecordAsync(new($"Ext.Ana{suffix}", AccessRoles.Administrator, index % 2 == 0 ? AuditEntities.Beneficiary : AuditEntities.Product,
                index % 3 == 0 ? AuditActions.Delete : AuditActions.Edit, $"{target} #{index}", index == 5 ? $"100% sigur_{suffix}" : $"detaliu {index}", "motiv", (900000 + index).ToString()));
        await audit.RecordAsync(new($"ext.ana{suffix}".ToUpperInvariant(), AccessRoles.LimitedUser, AuditEntities.Product, AuditActions.Create, $"{target} #8", "d8", "", "900008"));
        try
        {
            var all = await audit.QueryAsync(new(Text: suffix), 1, 3);
            Check(all.Total == 8 && all.Events.Count == 3 && all.PageSize == 3 && all.Page == 1 && all.JournalTotal >= 8, "The server counts the matches and returns only one page");
            Check(all.Events.Zip(all.Events.Skip(1)).All(pair => pair.First.TimestampUtc >= pair.Second.TimestampUtc), "The page is newest first");
            var last = await audit.QueryAsync(new(Text: suffix), 99, 3);
            Check(last.Page == 3 && last.Events.Count == 2, "A page past the end is brought back to the last page");
            var everything = await audit.QueryAsync(new(Text: suffix), 1, 0);
            Check(everything.Events.Count == 8 && everything.PageSize == 0, "Page size 0 returns every match");
            Check((await audit.QueryAsync(new(Text: suffix, Actor: $"EXT.ANA{suffix}"), 1, 50)).Total == 8, "The operator filter ignores letter case");
            Check((await audit.QueryAsync(new(Text: suffix, Entity: AuditEntities.Beneficiary), 1, 50)).Total == 3, "The type filter is applied on the server");
            Check((await audit.QueryAsync(new(Text: suffix, Action: AuditActions.Delete), 1, 50)).Total == 2 && (await audit.QueryAsync(new(Text: suffix, Action: AuditActions.Edit), 1, 50)).Total == 5,
                "The operation filter is applied on the server");
            Check((await audit.QueryAsync(new(Text: "100%"), 1, 50)).Events.Count(entry => entry.Target.StartsWith(target)) == 1 &&
                  (await audit.QueryAsync(new(Text: $"100% sigur_{suffix}"), 1, 50)).Total == 1 && (await audit.QueryAsync(new(Text: $"sigur_{suffix.ToUpperInvariant()}"), 1, 50)).Total == 1,
                "The search text is literal (% and _ are not wildcards) and ignores letter case");
            Check((await audit.QueryAsync(new(Text: suffix, FromUtc: start, ToUtc: DateTime.UtcNow.AddMinutes(1)), 1, 50)).Total == 8 &&
                  (await audit.QueryAsync(new(Text: suffix, FromUtc: DateTime.UtcNow.AddMinutes(1)), 1, 50)).Total == 0 &&
                  (await audit.QueryAsync(new(Text: suffix, ToUtc: start), 1, 50)).Total == 0, "The date range includes its start and excludes its end");
            var window = await audit.QueryAsync(new(Text: suffix), 1, 50, window: 3);
            Check(window.Total == 3 && window.JournalTotal >= 8, "The window limits the search to the latest events of the whole journal");
            Check((await audit.QueryAsync(new(Text: suffix), 1, 50, window: AuditQueryRules.RecentWindow)).Total == 8, "The default window of 500 covers the recent events");
            var deleted = all.Events.Concat(everything.Events).First(entry => entry.Action == AuditActions.Delete);
            var removals = await audit.RemovalTimesAsync(everything.Events);
            Check(removals.ContainsKey(AuditNavigation.ObjectKey(deleted.EntityType, deleted.EntityId)) && removals.Count == 2, "The removal times are asked for the objects of the page only");
            var summary = await audit.SummaryAsync(DateTime.UtcNow.Date);
            Check(summary.Total == before.Total + 8 && summary.Today >= before.Today + 8 && summary.Actors >= before.Actors, "The summary counts the journal on the server");
        }
        finally
        {
            try { await ExecuteAsync(probe, "DELETE FROM audit_events WHERE target LIKE @t", ("@t", "Ext Jurnal %")); } catch (MySqlException) { }
        }
    }

    private static async Task ChangeEventsAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit)
    {
        var suffix = Suffix();
        var source = new MariaChangeEventSource(configuration);
        await source.EnsureAsync(default);
        var start = await source.LatestIdAsync(default);
        var category = $"Ext Evenimente Cat {suffix}";
        var subcategory = $"Ext Evenimente Sub {suffix}";
        var products = new MariaProductRepository(configuration, admin, audit);
        await products.CreateCategoryAsync(category);
        await products.CreateSubcategoryAsync(category, subcategory);
        var product = await products.CreateAsync(new ProductInput { Name = $"Ext Evenimente {suffix}", Category = category, Subcategory = subcategory });
        var edit = ProductInput.From(product); edit.Description = "schimbat"; edit.Reason = "Ext";
        product = await products.UpdateAsync(product, edit);
        await products.DeleteAsync(product, "Ext curatare");
        await using (var raw = await OpenRawAsync(configuration))
        {
            await ExecuteAsync(raw, "DELETE FROM subcategories WHERE name=@n", ("@n", subcategory));
            await ExecuteAsync(raw, "DELETE FROM categories WHERE name=@n", ("@n", category));
        }
        var recorded = await source.ReadAfterAsync(start, 1000, default);
        var mine = recorded.Where(item => item.EntityType == AuditEntities.Product && item.EntityId == product.Id.ToString()).Select(item => item.Action).ToList();
        Check(mine.Contains(AuditActions.Create) && mine.Contains(AuditActions.Edit) && mine.Contains(AuditActions.Delete), "Create, edit and delete of a product are recorded as change events");
        Check(recorded.Zip(recorded.Skip(1)).All(pair => pair.First.Id < pair.Second.Id) && recorded.All(item => item.CreatedUtc.Kind == DateTimeKind.Utc), "Events are ordered by identifier and timestamped in UTC");
        Check(recorded.All(item => item.EntityId.All(char.IsDigit)), "Events carry only identifiers, never names or texts");
        var latest = recorded[^1].Id;
        await source.PurgeAsync(latest, DateTime.UtcNow.AddDays(-1), default);
        Check((await source.ReadAfterAsync(start, 1000, default)).Count == recorded.Count, "Purging keeps events newer than the retention period");
        await source.PurgeAsync(latest, DateTime.UtcNow.AddMinutes(1), default);
        Check((await source.ReadAfterAsync(start, 1000, default)).Count == 0, "Purging removes processed events older than the cutoff");
    }

    // ---- Expiry notifications -----------------------------------------------------------------------------------------------------
    private static async Task NotificationsAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe)
    {
        var suffix = Suffix();
        var clock = new ManualTimeProvider();
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var repository = new MariaExpiryNotificationRepository(configuration);
        var source = new TestExpirySource("test.ext." + suffix, []);
        var brokenSource = new TestExpirySource("test.broken." + suffix, []);
        ExpiryNotificationService Session(string user, bool administrator = false, params IExpirySource[] sources) =>
            new(repository, sources.Length > 0 ? sources : [source, brokenSource], administrator ? admin : new TestAccessControl(false, user), audit, clock);
        var boss = Session("integration.tester", true);
        var objectA = Random.Shared.Next(1000, 9999999);
        var objectB = objectA + 1;
        var objectC = objectA + 2;
        ExpiryInstance Make(int id, int daysLeft) => new(id, $"Obiect {id}", today.AddDays(daysLeft), new Dictionary<string, string> { ["obiect"] = $"Obiect {id}" }, $"/obiect/{id}");
        async Task<IReadOnlyList<ExpiryNotification>> Rows(NotificationTemplate of) => (await repository.GetNotificationsAsync()).Where(item => item.TemplateId == of.Id).ToList();
        NotificationTemplate? template = null, brokenTemplate = null;
        try
        {
            // Only the administrator manages templates; invalid ones are rejected; the journal names each operation.
            await Rejects<AccessDeniedException>(() => Session("ana").CreateTemplateAsync(new NotificationTemplateInput { SourceKey = source.Key, Subject = "S", Body = "T", ThresholdDays = 30 }), "A non-administrator cannot create a template");
            await Rejects<NotificationOperationException>(() => boss.CreateTemplateAsync(new NotificationTemplateInput { SourceKey = source.Key, Subject = "S <gresit>", Body = "T", ThresholdDays = 30 }), "A template with an unknown placeholder is rejected");
            template = await boss.CreateTemplateAsync(new NotificationTemplateInput { SourceKey = source.Key, Subject = "Expira <obiect>", Body = "<eveniment>: <obiect> la <data expirare>, mai sunt <zile ramase> zile, depasit cu <zile depasire> zile.", ThresholdDays = 30 });
            brokenTemplate = await boss.CreateTemplateAsync(new NotificationTemplateInput { SourceKey = brokenSource.Key, Subject = "Rupt", Body = "Rupt", ThresholdDays = 30 });
            Check((await audit.GetEventsAsync()).Any(item => item.Action == AuditActions.CreateNotificationTemplate && item.EntityId == template.Id.ToString()), "The journal names the operation \"Adăugare șablon notificare\"");
            await Rejects<AccessDeniedException>(() => Session("ana").GetTemplatesAsync(), "A non-administrator cannot list the templates");

            // One active template per event: a second active one is refused (service and database), a switched-off one is allowed.
            await Rejects<NotificationOperationException>(() => boss.CreateTemplateAsync(new NotificationTemplateInput { SourceKey = source.Key, Subject = "Al doilea", Body = "T", ThresholdDays = 7 }), "A second active template for the same event is refused");
            var spare = await boss.CreateTemplateAsync(new NotificationTemplateInput { SourceKey = source.Key, Subject = "Rezerva", Body = "T", ThresholdDays = 7, Active = false });
            var activate = NotificationTemplateInput.From(spare); activate.Active = true;
            await Rejects<NotificationOperationException>(() => boss.UpdateTemplateAsync(spare, activate), "Switching a second template on while another is active is refused");
            await Rejects<NotificationOperationException>(() => repository.CreateTemplateAsync(new NotificationTemplateInput { SourceKey = source.Key, Subject = "Direct", Body = "T", ThresholdDays = 3 }), "The database itself refuses two active templates for one event");
            await boss.DeleteTemplateAsync(spare, "Ext curatare rezerva");

            // The engine: every object inside the period gets a notification - also after the date, there is no lower limit -
            // once per event; the overdue ones come first.
            source.Instances.AddRange([Make(objectA, 20), Make(objectB, 40), Make(objectC, -3)]);
            brokenSource.Instances.Add(Make(objectA, 10));
            await boss.EvaluateAsync();
            await boss.EvaluateAsync();
            var views = (await boss.GetViewsAsync()).Where(item => item.Template.Id == template.Id).ToList();
            Check(views.Count == 2 && views.Any(item => item.Notification.ObjectId == objectA) && views.Any(item => item.Notification.ObjectId == objectC) && views.All(item => item.Notification.ObjectId != objectB),
                "The objects within 30 days, the overdue one included, get a notification each, once; the one outside the period does not");
            Check(views[0].Notification.ObjectId == objectC && views[0].IsOverdue && views[0].DaysLeft == -3 && !views[1].IsOverdue, "The overdue notification is listed first");
            Check(views[0].Body.Contains("mai sunt 0 zile, depasit cu 3 zile") && views[1].Body.Contains("mai sunt 20 zile, depasit cu 0 zile") && views[0].Url == $"/obiect/{objectC}",
                "The text never prints negative days, reports the days overdue, and the notification carries the link of its object");
            Check(views[0].MaxSnoozeDays == 30 && views[1].MaxSnoozeDays == 18, "An overdue notification may be postponed by up to 30 days, an upcoming one by the days left minus two");
            var view = views.Single(item => item.Notification.ObjectId == objectA);
            var overdueView = views.Single(item => item.Notification.ObjectId == objectC);
            var createdEntries = (await audit.GetEventsAsync()).Where(item => item.Action == AuditActions.NotificationCreated && item.EntityId == view.Notification.Id.ToString()).ToList();
            Check(createdEntries.Count == 1 && createdEntries[0].ActorUsername == "sistem" && createdEntries[0].EntityType == AuditEntities.Notification &&
                  createdEntries[0].Target.Contains($"Obiect {objectA}") && createdEntries[0].Details.Contains(StockMovementRules.DisplayDate(today.AddDays(20))),
                "The system journals the creation of a notification once (\"Notificare creată\", actor \"sistem\"), with the object and the expiry date");
            Check(!(await audit.GetEventsAsync()).Any(item => item.Action == AuditActions.NotificationCreated && item.Target.Contains($"Obiect {objectB}")),
                "No creation is journaled for an object outside the period");
            Check(view.Subject == $"Expira Obiect {objectA}" && view.Body.Contains($"Eveniment test: Obiect {objectA} la {StockMovementRules.DisplayDate(today.AddDays(20))}"),
                "The subject and text are produced from the template with the values of the object");
            Check(view.IsAlert && view.DaysLeft == 20, "A new notification warns");
            Check(await boss.AlertCountAsync() >= 2, "The alert count includes the new notifications");

            // An unreadable source keeps its notifications instead of closing them.
            brokenSource.Fail = true;
            await boss.EvaluateAsync();
            Check((await repository.GetNotificationsAsync()).Any(item => item.TemplateId == brokenTemplate.Id && !item.IsResolved), "The notifications of a source that cannot be read are kept, unresolved");
            Check((await Session("integration.tester", true).GetViewsAsync()).Any(item => item.Template.Id == brokenTemplate.Id && item.Subject == "Rupt" && item.ObjectLabel.Contains($"Obiect {objectA}")),
                "They stay in the list, shown from the values stored when they were created");
            brokenSource.Fail = false;

            // Two users take it over at the same moment: exactly one succeeds.
            var takers = await Task.WhenAll(new[] { "ana", "bob" }.Select(async user =>
            {
                try { await Session(user).AcknowledgeAsync(view); return (user, message: (string?)null); }
                catch (NotificationOperationException exception) { return (user, message: exception.Message); }
            }));
            Check(takers.Count(item => item.message is null) == 1, "Two users taking over the same notification: exactly one succeeds");
            var winner = takers.Single(item => item.message is null).user;
            Check(takers.Single(item => item.message is not null).message!.Contains($"preluată de {winner}"), "The other user is told who took the notification over");
            var taken = (await boss.GetViewsAsync()).Single(item => item.Notification.Id == view.Notification.Id);
            Check(!taken.IsAlert && taken.Notification.AcknowledgedBy == winner, "A taken over notification stays in the list but stops warning");
            Check((await audit.GetEventsAsync()).Any(item => item.Action == AuditActions.AcknowledgeNotification && item.ActorUsername == winner && item.EntityId == view.Notification.Id.ToString()),
                "The journal names the operation \"Preluare notificare\" and the user who took it over");

            // Reminder: within 1..(days left - 2), warns again when the period ends, never while it runs.
            await Rejects<NotificationOperationException>(() => Session("ana").SnoozeAsync(taken, 0), "A reminder of zero days is rejected");
            await Rejects<NotificationOperationException>(() => Session("ana").SnoozeAsync(taken, 19), "A reminder longer than the days left minus two is rejected");
            var snoozed = await Session("ana").SnoozeAsync(taken, 5);
            Check(snoozed.SnoozeUntil == today.AddDays(5), "A reminder of 5 days is stored until the computed date");
            Check(!(await boss.GetViewsAsync()).Single(item => item.Notification.Id == view.Notification.Id).IsAlert, "During the reminder the notification does not warn");
            Check((await audit.GetEventsAsync()).Any(item => item.Action == AuditActions.SnoozeNotification && item.ActorUsername == "ana" && item.Details.Contains("5 zile")),
                "The journal names the operation \"Amânare notificare\" with the number of days");
            clock.Advance(TimeSpan.FromDays(5));
            var again = (await boss.GetViewsAsync()).Single(item => item.Notification.Id == view.Notification.Id);
            Check(again.IsAlert && again.DaysLeft == 15 && again.MaxSnoozeDays == 13, "When the reminder period ends the notification warns again, with a new limit");
            await Rejects<NotificationOperationException>(() => Session("bob").SnoozeAsync(taken, 3), "An outdated view of the notification cannot be used for a reminder");
            clock.Advance(TimeSpan.FromDays(-5));

            // An overdue notification: a reminder of 1..30 days; taken over or postponed it stays overdue (red) and in the list.
            await Rejects<NotificationOperationException>(() => Session("ana").SnoozeAsync(overdueView, 31), "A reminder longer than 30 days is rejected for an overdue notification");
            var overdueSnoozed = await Session("ana").SnoozeAsync(overdueView, 7);
            Check(overdueSnoozed.SnoozeUntil == today.AddDays(7), "An overdue notification can be postponed by a chosen number of days (up to 30)");
            var overdueAfter = (await boss.GetViewsAsync()).Single(item => item.Notification.Id == overdueView.Notification.Id);
            Check(overdueAfter.IsOverdue && !overdueAfter.IsAlert && (await boss.GetViewsAsync()).First(item => item.Template.Id == template.Id).Notification.Id == overdueView.Notification.Id,
                "A postponed overdue notification stops warning but stays overdue and stays first in the list");

            // Manual resolution: one user wins, the notification moves to the resolved list with its texts and is never recreated.
            var resolvedNow = await Session("carla").ResolveAsync(overdueAfter);
            Check(resolvedNow.IsResolved && resolvedNow.ResolvedBy == "carla" && !resolvedNow.ResolvedAutomatically, "A user can mark a notification as resolved");
            await Rejects<NotificationOperationException>(() => Session("bob").ResolveAsync(overdueAfter), "Resolving the same notification twice is refused");
            var resolvedList = await boss.GetResolvedViewsAsync();
            var resolvedItem = resolvedList.Single(item => item.Notification.Id == overdueView.Notification.Id);
            Check(resolvedItem.Subject == $"Expira Obiect {objectC}" && resolvedItem.ObjectLabel == $"Obiect {objectC}" && resolvedItem.SourceText.Contains("Eveniment test") && resolvedItem.Notification.ResolvedReason == ExpiryNotificationService.ManualResolutionReason,
                "A resolved notification keeps the subject, object and source it had, and the reason");
            Check(!(await boss.GetViewsAsync()).Any(item => item.Notification.Id == overdueView.Notification.Id), "A resolved notification leaves the list of active notifications");
            Check((await audit.GetEventsAsync()).Any(item => item.Action == AuditActions.ResolveNotification && item.ActorUsername == "carla" && item.EntityId == overdueView.Notification.Id.ToString()),
                "The journal names the operation \"Rezolvare notificare\" and the user");
            await Rejects<NotificationOperationException>(() => Session("ana").AcknowledgeAsync(overdueAfter), "A resolved notification can no longer be taken over");
            await boss.EvaluateAsync();
            Check((await Rows(template)).Count(item => item.ObjectId == objectC) == 1 && (await Rows(template)).Single(item => item.ObjectId == objectC).IsResolved,
                "A resolved notification is not created again for the same date");

            // Editing the date of an object closes its notification automatically, with the reason, and raises a new unread one.
            source.Instances.RemoveAll(item => item.ObjectId == objectA);
            source.Instances.Add(Make(objectA, 25));
            await boss.EvaluateAsync();
            var redated = (await boss.GetViewsAsync()).Where(item => item.Template.Id == template.Id).ToList();
            Check(redated.Count == 1 && redated[0].Notification.Id != view.Notification.Id && redated[0].IsAlert && redated[0].Notification.AcknowledgedBy is null && redated[0].DaysLeft == 25,
                "A changed date raises a new unread notification for the new date");
            var autoClosed = (await boss.GetResolvedViewsAsync()).Single(item => item.Notification.Id == view.Notification.Id);
            Check(autoClosed.Notification.ResolvedAutomatically && autoClosed.Notification.ResolvedBy == "sistem" &&
                  autoClosed.Notification.ResolvedReason!.Contains(StockMovementRules.DisplayDate(today.AddDays(20))) && autoClosed.Notification.ResolvedReason.Contains(StockMovementRules.DisplayDate(today.AddDays(25))),
                "The old notification is closed by the system with the reason naming the old and the new date");
            Check((await audit.GetEventsAsync()).Any(item => item.Action == AuditActions.AutoResolveNotification && item.ActorUsername == "sistem" && item.EntityId == view.Notification.Id.ToString() && item.Details.Contains("Motiv")),
                "The journal names the operation \"Rezolvare automată notificare\", actor \"sistem\"");

            // The automatic closing is reversible: the date returns, the same notification comes back unread. A manual one does not.
            source.Instances.RemoveAll(item => item.ObjectId == objectA);
            source.Instances.Add(Make(objectA, 20));
            await boss.EvaluateAsync();
            var reopened = (await boss.GetViewsAsync()).Where(item => item.Template.Id == template.Id).ToList();
            Check(reopened.Count == 1 && reopened[0].Notification.Id == view.Notification.Id && reopened[0].IsAlert && reopened[0].Notification.AcknowledgedBy is null && !reopened[0].Notification.IsResolved,
                "When the date returns, the automatically closed notification is reopened, unread; the one resolved by a user stays resolved");
            Check((await audit.GetEventsAsync()).Any(item => item.Action == AuditActions.ReopenNotification && item.EntityId == view.Notification.Id.ToString()),
                "The journal names the operation \"Redeschidere automată notificare\"");

            // An object that disappears closes its notification too (nothing is deleted).
            source.Instances.Clear();
            await boss.EvaluateAsync();
            Check(!(await boss.GetViewsAsync()).Any(item => item.Template.Id == template.Id), "A notification leaves the active list when its object no longer exists");
            Check((await boss.GetResolvedViewsAsync()).Any(item => item.Notification.Id == view.Notification.Id && item.Notification.ResolvedAutomatically && item.Notification.ResolvedReason!.Contains("nu mai este urmărit")),
                "It is closed by the system with the reason, not deleted");

            // Template switched off: the existing notifications stay in the list (they are resolved only by resolving); none are created meanwhile.
            source.Instances.Add(Make(objectA, 10));
            await boss.EvaluateAsync();
            var beforeOff = (await boss.GetViewsAsync()).Single(item => item.Template.Id == template.Id);
            await Session("carla").AcknowledgeAsync(beforeOff);
            var edit = NotificationTemplateInput.From(template); edit.Subject = "Nou <obiect>"; edit.ThresholdDays = 15;
            template = await boss.UpdateTemplateAsync(template, edit);
            Check((await audit.GetEventsAsync()).Any(item => item.Action == AuditActions.EditNotificationTemplate && item.EntityId == template.Id.ToString() && item.Details.Contains("Nou <obiect>")),
                "The journal names the operation \"Modificare șablon notificare\" with the old and new values");
            await Rejects<NotificationOperationException>(() => boss.UpdateTemplateAsync(template with { Version = 99 }, edit), "A stale template edit is rejected");
            var off = NotificationTemplateInput.From(template); off.Active = false;
            template = await boss.UpdateTemplateAsync(template, off);
            await boss.EvaluateAsync();
            var whileOff = (await boss.GetViewsAsync()).Where(item => item.Template.Id == template.Id).ToList();
            Check(whileOff.Count == 1 && whileOff[0].Notification.AcknowledgedBy == "carla", "Switching a template off does not hide its notifications, and they keep their state");
            source.Instances.Add(Make(objectB, 5));
            await boss.EvaluateAsync();
            Check(!(await Rows(template)).Any(item => item.ObjectId == objectB), "No new notifications are created while the template is off");
            off.Active = true;
            template = await boss.UpdateTemplateAsync(template, off);
            await boss.EvaluateAsync();
            var restored = (await boss.GetViewsAsync()).Where(item => item.Template.Id == template.Id).ToList();
            Check(restored.Count == 2 && restored.Single(item => item.Notification.ObjectId == objectA).Notification.AcknowledgedBy == "carla" && restored.Single(item => item.Notification.ObjectId == objectB).IsAlert,
                "Switching the template on again keeps the state (taken over stays taken over) and creates the ones that came due meanwhile");

            // Deleting a template: it needs a reason, reports the unresolved notifications and takes all its notifications away.
            var open = await boss.CountOpenNotificationsAsync(template);
            Check(open == 2, "The deletion dialog can report how many unresolved notifications the template has");
            await Rejects<NotificationOperationException>(() => boss.DeleteTemplateAsync(template, " "), "Deleting a template requires a reason");
            var templateId = template.Id;
            await boss.DeleteTemplateAsync(template, "Ext curatare sablon");
            template = null;
            Check(await ScalarLongAsync(probe, "SELECT COUNT(*) FROM expiry_notifications WHERE template_id=@id", ("@id", templateId)) == 0, "Deleting a template removes its notifications");
            Check((await audit.GetEventsAsync()).Any(item => item.Action == AuditActions.DeleteNotificationTemplate && item.EntityId == templateId.ToString()), "The journal names the operation \"Ștergere șablon notificare\"");
        }
        finally
        {
            foreach (var item in new[] { template, brokenTemplate })
                if (item is not null && (await repository.GetTemplatesAsync()).FirstOrDefault(current => current.Id == item.Id) is { } current)
                    await repository.DeleteTemplateAsync(current);
        }

        // The real vehicle source: an expiry date edited on the vehicle closes the notification, with the reason naming the change.
        var vehicles = new MariaVehicleRepository(configuration, admin, audit);
        var realBoss = Session("integration.tester", true, new VehicleExpirySource(vehicles, VehicleExpiryKind.Itp, ExpirySourceKeys.VehicleItp, "ITP"));
        var vehicle = await vehicles.CreateAsync(new VehicleInput { PlateNumber = "TS-94-" + Letters(), Description = "Ext notificari", ItpExpiry = today.AddDays(10), InsuranceExpiry = Expiry, RovinietaExpiry = Expiry });
        NotificationTemplate? vehicleTemplate = null;
        try
        {
            // The isolated test database is not shared with anyone: a template left by a manual session would make the single active template rule refuse this one.
            foreach (var leftover in (await repository.GetTemplatesAsync()).Where(item => item.SourceKey == ExpirySourceKeys.VehicleItp).ToList())
                await repository.DeleteTemplateAsync(leftover);
            {
                vehicleTemplate = await realBoss.CreateTemplateAsync(new NotificationTemplateInput
                { SourceKey = ExpirySourceKeys.VehicleItp, Subject = ExpiryTemplateRules.DefaultSubject, Body = ExpiryTemplateRules.DefaultBody, ThresholdDays = 15 });
                await realBoss.EvaluateAsync();
                var mine = (await realBoss.GetViewsAsync()).Where(item => item.Notification.ObjectId == vehicle.Id && item.Notification.SourceKey == ExpirySourceKeys.VehicleItp).ToList();
                Check(mine.Count == 1 && mine[0].Subject.Contains(vehicle.PlateNumber) && mine[0].Body.Contains(vehicle.Description) && mine[0].Subject.StartsWith("Expirare ITP") && mine[0].Url == $"/vehicule/{vehicle.Id}",
                    "The ITP of a vehicle expiring in 10 days raises a notification with the plate number, description and the link of the vehicle");
                var edit = VehicleInput.From(vehicle);
                VehicleRules.SetExpiry(edit, VehicleExpiryKind.Itp, today.AddDays(200)); edit.Reason = "Ext";
                vehicle = await vehicles.UpdateAsync(vehicle, edit, default, AuditActions.ExpiryItp);
                await realBoss.EvaluateAsync();
                Check(!(await realBoss.GetViewsAsync()).Any(item => item.Notification.ObjectId == vehicle.Id && item.Notification.SourceKey == ExpirySourceKeys.VehicleItp),
                    "Postponing the vehicle's ITP takes the notification out of the active list");
                var closed = (await realBoss.GetResolvedViewsAsync()).Single(item => item.Notification.ObjectId == vehicle.Id && item.Notification.SourceKey == ExpirySourceKeys.VehicleItp);
                Check(closed.Notification.ResolvedReason!.Contains("ITP") && closed.Notification.ResolvedReason.Contains(StockMovementRules.DisplayDate(today.AddDays(10))) &&
                      closed.Notification.ResolvedReason.Contains(StockMovementRules.DisplayDate(today.AddDays(200))) && closed.Subject.Contains(vehicle.PlateNumber),
                    "The resolved notification names the change: the ITP date from the old to the new value");
            }
        }
        finally
        {
            if (vehicleTemplate is not null) await repository.DeleteTemplateAsync((await repository.GetTemplatesAsync()).First(item => item.Id == vehicleTemplate.Id));
            await vehicles.DeleteAsync(vehicle, "Ext curatare");
        }
    }

    // ---- Clean-up of old resolved notifications ---------------------------------------------------------------------------------
    private static async Task NotificationSettingsAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe)
    {
        var suffix = Suffix();
        var clock = new ManualTimeProvider();
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var repository = new MariaExpiryNotificationRepository(configuration);
        var source = new TestExpirySource("test.purge." + suffix, []);
        ExpiryNotificationService Session(string user, bool administrator = false) =>
            new(repository, [source], administrator ? admin : new TestAccessControl(false, user), audit, clock);
        var boss = Session("integration.tester", true);
        var first = Random.Shared.Next(1000, 9999000);
        int P1 = first, P2 = first + 1, P3 = first + 2, P4 = first + 3, P5 = first + 4, P6 = first + 5, P7 = first + 6;
        ExpiryInstance Make(int id, int daysLeft) => new(id, $"Obiect {id}", today.AddDays(daysLeft), new Dictionary<string, string> { ["obiect"] = $"Obiect {id}" }, $"/obiect/{id}");
        NotificationTemplate? template = null;
        async Task<ExpiryNotification?> Row(int objectId) =>
            (await repository.GetNotificationsAsync()).FirstOrDefault(item => item.TemplateId == template!.Id && item.ObjectId == objectId);
        async Task Age(int objectId, int months) =>
            await ExecuteAsync(probe, "UPDATE expiry_notifications SET resolved_utc=@t WHERE template_id=@template AND object_id=@object",
                ("@t", MariaTimeTextForTest(clock.GetUtcNow().UtcDateTime.AddMonths(-months))), ("@template", template!.Id), ("@object", objectId));
        void Redate(int objectId, int daysLeft) { source.Instances.RemoveAll(item => item.ObjectId == objectId); source.Instances.Add(Make(objectId, daysLeft)); }
        int Mine(NotificationPurgePlan plan) => plan.Removable.Count(item => item.TemplateId == template!.Id);
        await ExecuteAsync(probe, "DELETE FROM notification_settings");
        try
        {
            // The setting: defaults until saved, administrator only, validated, and nothing stored when nothing changed.
            var initial = await boss.GetSettingsAsync();
            Check(!initial.PurgeEnabled && initial.PurgeMonths == NotificationPurgeRules.DefaultMonths && initial.LastPurgeUtc is null && initial.Version < 0,
                "Until saved, the clean-up setting is off with the default period of 12 months");
            await Rejects<AccessDeniedException>(() => Session("ana").GetSettingsAsync(), "A non-administrator cannot read the clean-up setting");
            await Rejects<AccessDeniedException>(() => Session("ana").SaveSettingsAsync(initial, true, 12), "A non-administrator cannot change the clean-up setting");
            await Rejects<AccessDeniedException>(() => Session("ana").PreviewPurgeAsync(12), "A non-administrator cannot preview a clean-up");
            await Rejects<NotificationOperationException>(() => boss.SaveSettingsAsync(initial, true, 0), "A period below 1 month is rejected");
            await Rejects<NotificationOperationException>(() => boss.SaveSettingsAsync(initial, true, NotificationPurgeRules.MaxMonths + 1), "A period above 60 months is rejected");
            var unchanged = await boss.SaveSettingsAsync(initial, false, NotificationPurgeRules.DefaultMonths);
            Check(unchanged.Settings.Version < 0 && unchanged.Removed == 0 && await ScalarLongAsync(probe, "SELECT COUNT(*) FROM notification_settings") == 0,
                "Saving the setting unchanged stores and journals nothing");

            // Data: five resolved notifications and an open one. P1 manual and still current, P2 manual whose date changed since,
            // P3 closed by the system, P4 open, P5 manual with a changed date but resolved only two months ago.
            template = await boss.CreateTemplateAsync(new NotificationTemplateInput { SourceKey = source.Key, Subject = "Expira <obiect>", Body = "<obiect>", ThresholdDays = 30 });
            source.Instances.AddRange([Make(P1, 10), Make(P2, 11), Make(P3, 12), Make(P4, 13), Make(P5, 14)]);
            await boss.EvaluateAsync();
            var carla = Session("carla");
            foreach (var view in (await boss.GetViewsAsync()).Where(item => item.Template.Id == template.Id && item.Notification.ObjectId is var o && (o == P1 || o == P2 || o == P5)).ToList())
                await carla.ResolveAsync(view);
            Redate(P2, 60); Redate(P5, 60); Redate(P3, 60);
            await boss.EvaluateAsync();
            Check((await Row(P3))!.ResolvedAutomatically && (await Row(P1))!.IsResolved && !(await Row(P4))!.IsResolved, "The data is set: manual, automatic and open notifications");
            await Age(P1, 14); await Age(P2, 14); await Age(P3, 14); await Age(P5, 2);

            // What a clean-up removes: old resolved ones, except a manual one whose event is still current (it would be created again),
            // and never on a guess (a source that cannot be read keeps its manual ones).
            var plan = await boss.PreviewPurgeAsync(12);
            Check(plan.Cutoff == today.AddMonths(-12) && Mine(plan) == 2 && plan.Removable.Any(item => item.ObjectId == P2) && plan.Removable.Any(item => item.ObjectId == P3),
                "A 12-month clean-up would remove the old manual one whose date changed and the old automatic one");
            Check(!plan.Removable.Any(item => item.TemplateId == template.Id && (item.ObjectId == P1 || item.ObjectId == P4 || item.ObjectId == P5)),
                "It keeps the manual one whose event is current, the open one and the one resolved recently");
            source.Fail = true;
            Check(Mine(await boss.PreviewPurgeAsync(12)) == 1, "When the source cannot be read, the manual notifications are kept (only the automatic one goes)");
            source.Fail = false;
            Check(Mine(await boss.PreviewPurgeAsync(1)) == 3, "A shorter period adds the manual one resolved two months ago");

            // Switching on removes at once, journals both the setting and the removal (actor: the administrator), and stores the run.
            var since = DateTime.UtcNow; await Task.Delay(30);
            var on = await boss.SaveSettingsAsync(initial, true, 12);
            Check(on.Settings.PurgeEnabled && on.Settings.PurgeMonths == 12 && on.Settings.Version >= 0 && on.Removed >= 2 && on.Cutoff == today.AddMonths(-12) &&
                  on.Settings.LastPurgeUtc == clock.GetUtcNow().UtcDateTime,
                "Switching the clean-up on removes the old resolved notifications at once and stores the moment of the run");
            Check(await Row(P2) is null && await Row(P3) is null && await Row(P1) is { IsResolved: true } && await Row(P4) is { IsResolved: false } && await Row(P5) is { IsResolved: true },
                "Only the announced notifications were deleted from the database");
            var afterOn = (await audit.GetEventsAsync()).Where(item => item.TimestampUtc > since).ToList();
            var settingsEvent = afterOn.Single(item => item.Action == AuditActions.EditNotificationSettings);
            Check(settingsEvent.EntityType == AuditEntities.NotificationSettings && settingsEvent.Details.Contains("oprită → activă"), "The journal names the operation \"Modificare setări curățare notificări\" with the old and new value");
            var purgeEvent = afterOn.Single(item => item.Action == AuditActions.PurgeResolvedNotifications);
            Check(purgeEvent.Details == NotificationPurgeRules.RemovedText(on.Removed, today.AddMonths(-12)) && purgeEvent.Details.StartsWith("au fost eliminate din baza de date") && purgeEvent.ActorUsername != "sistem",
                "The journal names the operation \"Curățare notificări rezolvate\" with the number removed and the limit date");
            await Rejects<NotificationOperationException>(() => boss.SaveSettingsAsync(initial, true, 6), "A stale view of the setting cannot overwrite it");

            // Shortening the period while it is on removes more; a change that removes nothing journals only the setting.
            var shortened = await boss.PreviewPurgeAsync(1);
            Check(Mine(shortened) == 1 && shortened.Removable.Any(item => item.ObjectId == P5), "Shortening to 1 month would remove the notification resolved two months ago");
            since = DateTime.UtcNow; await Task.Delay(30);
            var longer = await boss.SaveSettingsAsync(on.Settings, true, 24);
            Check(longer.Removed == 0 && !(await audit.GetEventsAsync()).Any(item => item.TimestampUtc > since && item.Action == AuditActions.PurgeResolvedNotifications),
                "Nothing is journaled as removed when a clean-up removes nothing");
            var shorter = await boss.SaveSettingsAsync(longer.Settings, true, 1);
            Check(shorter.Removed >= 1 && await Row(P5) is null && await Row(P1) is { IsResolved: true }, "Shortening the period deletes what became old enough, at once");

            // Switching off stops it: nothing is deleted any more, not even by the daily run.
            var off = await boss.SaveSettingsAsync(shorter.Settings, false, 12);
            Check(!off.Settings.PurgeEnabled && off.Removed == 0, "Switching the clean-up off removes nothing");
            source.Instances.AddRange([Make(P6, 5), Make(P7, 6)]);
            await boss.EvaluateAsync();
            Redate(P6, 60); Redate(P7, 60);
            await boss.EvaluateAsync();
            await Age(P6, 14); await Age(P7, 14);
            await boss.EvaluateAsync();
            Check(await Row(P6) is not null && await Row(P7) is not null, "With the switch off, the daily run deletes nothing");

            // The daily run: with the switch on, together with the evaluation, once a day, journaled by the system.
            await repository.SaveSettingsAsync(off.Settings, true, 12, null);
            since = DateTime.UtcNow; await Task.Delay(30);
            await boss.EvaluateAsync();
            var day1 = (await audit.GetEventsAsync()).Where(item => item.TimestampUtc > since && item.Action == AuditActions.PurgeResolvedNotifications).ToList();
            Check(await Row(P6) is null && await Row(P7) is null && (await repository.GetSettingsAsync()).LastPurgeUtc == clock.GetUtcNow().UtcDateTime,
                "With the switch on, the evaluation deletes the old resolved notifications and stores the day of the run");
            Check(day1.Count == 1 && day1[0].ActorUsername == "sistem" && day1[0].Details.StartsWith("au fost eliminate din baza de date 2 notificări rezolvate mai vechi de "),
                "The system journals the daily run (\"Curățare notificări rezolvate\", actor \"sistem\")");
            Redate(P6, 5);
            await boss.EvaluateAsync();
            Redate(P6, 60);
            await boss.EvaluateAsync();
            await Age(P6, 14);
            await boss.EvaluateAsync();
            Check(await Row(P6) is not null, "The daily run happens once a day: a second evaluation the same day deletes nothing");
            clock.Advance(TimeSpan.FromDays(1));
            await Age(P6, 14);
            var rowBefore = await Row(P6);
            await boss.EvaluateAsync();
            Check(rowBefore is not null && await Row(P6) is null, "The next day the run deletes again");
            since = DateTime.UtcNow; await Task.Delay(30);
            clock.Advance(TimeSpan.FromDays(1));
            await boss.EvaluateAsync();
            Check(!(await audit.GetEventsAsync()).Any(item => item.TimestampUtc > since && item.Action == AuditActions.PurgeResolvedNotifications), "A run that deletes nothing writes nothing to the journal");
        }
        finally
        {
            if (template is not null && (await repository.GetTemplatesAsync()).FirstOrDefault(item => item.Id == template.Id) is { } current)
                await repository.DeleteTemplateAsync(current);
            await ExecuteAsync(probe, "DELETE FROM notification_settings");
        }
    }
}
