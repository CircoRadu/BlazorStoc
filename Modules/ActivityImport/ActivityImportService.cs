using System.Text.Json;
using BlazorStoc.Services;
using Microsoft.AspNetCore.DataProtection;
using MySqlConnector;

namespace BlazorStoc.Modules.ActivityImport;

// Setari -> Preluare ActivityApp: the connection of the read-only ActivityApp account, the ticket types the administrator links to service and maintenance, and the state of the
// structure of ActivityApp. Administrator only (permission module "setari-import-activityapp", reserved to the administrator). Every change and every test is journaled by name; the
// password is stored encrypted, never shown again and never written to the journal.

public sealed class ActivityImportException(string message) : Exception(message), IExpectedAnswer;

public static class ActivityDestination
{
    public const string OnDemand = "OnDemand";
    public const string Maintenance = "Maintenance";
    public static bool IsKnown(string value) => value is OnDemand or Maintenance;
    public static string Label(string value) => value == Maintenance ? "de mentenanță" : "la cerere";
}

public sealed record ActivityImportSettings(bool Enabled, string Server, int Port, string Database, string User, bool HasPassword, DateOnly? SinceDate, int WindowFromHour, int WindowToHour,
    int PageSize, bool FillEmptyFields, DateTime? LastTestUtc, bool? LastTestOk, string LastTestMessage, StructureStatus StructureStatus, string StructureFingerprint,
    IReadOnlyList<StructureIssue> StructureIssues, DateTime? StructureCheckedUtc, string UpdatedBy, DateTime? UpdatedUtc)
{
    /// <summary>The date from which tickets are taken: the one chosen, else 1 January of the current year.</summary>
    public DateOnly EffectiveSince(DateOnly today) => SinceDate ?? new DateOnly(today.Year, 1, 1);

    /// <summary>The periodic synchronization (migration 47): on or off, every how many minutes, and how the last attempt went.</summary>
    public bool SyncEnabled { get; init; }
    public int SyncIntervalMinutes { get; init; } = ActivityImportRules.DefaultSyncMinutes;
    public DateTime? LastSyncUtc { get; init; }
    public bool? LastSyncOk { get; init; }
    public string LastSyncMessage { get; init; } = "";
    public int SyncFailures { get; init; }
    /// <summary>How the scheduled synchronization is paced (migration 49): "interval" (every SyncIntervalMinutes, inside the time window) or "times" (at the fixed hours of SyncTimes, "HH:mm" separated by comma).</summary>
    public string SyncMode { get; init; } = ActivitySyncSchedule.IntervalMode;
    /// <summary>The latest modification date of a closing event seen by the last successful ticket import (migration 50); null reads everything.</summary>
    public DateTime? SourceWatermark { get; init; }
    public string SyncTimes { get; init; } = "";
}

/// <summary>What the administrator typed. A null or empty <see cref="Password"/> keeps the stored one.</summary>
public sealed record ActivityImportInput(bool Enabled, string Server, int Port, string Database, string User, string? Password, DateOnly? SinceDate, int WindowFromHour, int WindowToHour,
    int PageSize, bool FillEmptyFields, bool SyncEnabled = false, int SyncIntervalMinutes = ActivityImportRules.DefaultSyncMinutes,
    string SyncMode = ActivitySyncSchedule.IntervalMode, string SyncTimes = "");

public sealed record ActivityTestResult(bool Ok, string Message, StructureStatus Structure, bool WritableAccount, bool StructureChanged);

public sealed record ActivityTypeItem(long Id, string Name, string Category, bool? ContractService, bool? AutoEvent, int? Resolved, int? Open, int? Deleted, bool Selected, string Destination, string State);

public sealed record ActivityTypesView(IReadOnlyList<ActivityTypeItem> Items, StructureStatus Structure, string? SourceError);

/// <summary>A choice in the list of types: the name is the one the administrator saw (it becomes the name seen at the choice).</summary>
public sealed record ActivityTypeChoice(long Id, string Name, bool Selected, string Destination);

public static class ActivityImportRules
{
    public const string StateNew = "nou, neselectat";
    public const string StateChanged = "schimbat în sursă";
    public const string StateMissing = "lipsește din sursă";
    public const string StateSelected = "selectat";
    public const int DefaultSyncMinutes = 60;
    public const string NoTypesMessage = "Nu este ales niciun tip de tichet. Alege tipurile în secțiunea 2.";

    public static string? Validate(ActivityImportInput input)
    {
        if (input.Server.Length > 200 || input.Server.IndexOfAny([';', '=', ' ', '\'', '"']) >= 0) return "Adresa serverului nu este validă (fără spații sau caractere speciale).";
        if (input.Port is < 1 or > 65535) return "Portul trebuie să fie între 1 și 65535.";
        if (input.Database.Length is 0 or > 128 || input.Database.Any(c => !(char.IsLetterOrDigit(c) || c is '_' or '-' or '.'))) return "Numele bazei de date nu este valid (litere, cifre, _ - .).";
        if (input.User.Length > 128 || input.User.IndexOfAny([';', '=', '\'', '"']) >= 0) return "Numele contului nu este valid.";
        if (input.Password is { Length: > 200 }) return "Parola este prea lungă.";
        if (input.User.Trim().Equals("sa", StringComparison.OrdinalIgnoreCase)) return "Folosește un cont dedicat doar pentru citire, nu contul „sa”.";
        if (input.WindowFromHour is < 0 or > 23 || input.WindowToHour is < 1 or > 24 || input.WindowFromHour == input.WindowToHour) return "Fereastra orară trebuie să aibă începutul între 0 și 23, sfârșitul între 1 și 24 și să nu fie goală.";
        if (input.PageSize is < 50 or > 1000) return "Mărimea paginii trebuie să fie între 50 și 1000 de rânduri.";
        if (input.SinceDate is { } since && since.Year < 2000) return "Data de la care se preiau tichetele nu este validă.";
        if (input.SyncIntervalMinutes is < 5 or > 1440) return "Intervalul sincronizării trebuie să fie între 5 și 1440 de minute.";
        if (input.SyncMode is not (ActivitySyncSchedule.IntervalMode or ActivitySyncSchedule.TimesMode)) return "Modul sincronizării nu este valid.";
        if (input.SyncMode == ActivitySyncSchedule.TimesMode)
        {
            var times = ActivitySyncSchedule.ParseTimes(input.SyncTimes, out var timesError);
            if (timesError is not null) return timesError;
            if (times.Count == 0) return "Scrie cel puțin o oră fixă (de exemplu 08:00).";
        }
        return null;
    }

    public static string? Validate(IEnumerable<ActivityTypeChoice> choices)
    {
        foreach (var choice in choices)
            if (choice.Selected && !ActivityDestination.IsKnown(choice.Destination)) return "Destinația unui tip de tichet nu este validă.";
        return null;
    }
}

public interface IActivityImportService
{
    Task<ActivityImportSettings> GetSettingsAsync(CancellationToken cancellationToken = default);
    Task SaveSettingsAsync(ActivityImportInput input, CancellationToken cancellationToken = default);
    /// <summary>Connects with the typed values (an empty password uses the stored one) and checks the structure; the outcome is remembered and journaled. Nothing is imported.</summary>
    Task<ActivityTestResult> TestAsync(ActivityImportInput input, CancellationToken cancellationToken = default);
    /// <summary>The types of ActivityApp (read now, with the stored connection) merged with the administrator's choices.</summary>
    Task<ActivityTypesView> GetTypesAsync(CancellationToken cancellationToken = default);
    /// <summary>The choices saved, as they were last seen (no reading of ActivityApp, so no counts): what the panel shows when it opens.</summary>
    Task<ActivityTypesView> GetSavedTypesAsync(CancellationToken cancellationToken = default);
    Task SaveTypesAsync(IReadOnlyList<ActivityTypeChoice> choices, CancellationToken cancellationToken = default);
    /// <summary>The stored connection (with the decrypted password) for the import run, or null when it is not complete. Needs the right to run the import.</summary>
    Task<ActivityConnection?> GetConnectionAsync(CancellationToken cancellationToken = default);
    /// <summary>The stored correspondence of the ticket texts to ActivityApp columns (empty = every field uses its default column).</summary>
    Task<ActivityFieldMap> GetFieldMapAsync(CancellationToken cancellationToken = default);
    /// <summary>Every mappable field with the column in use and the text columns ActivityApp has now (read with the stored connection).</summary>
    Task<ActivityFieldMapView> GetFieldChoicesAsync(CancellationToken cancellationToken = default) => GetFieldChoicesAsync(true, cancellationToken);
    /// <summary>With readSource false the saved choice is returned without asking ActivityApp (no list of available columns), so the page can show it at once.</summary>
    Task<ActivityFieldMapView> GetFieldChoicesAsync(bool readSource, CancellationToken cancellationToken = default);
    /// <summary>Saves the chosen columns (key "Table.Column" of the contract, value the real column; the default or an empty value clears the choice). Journaled.</summary>
    Task SaveFieldMapAsync(IReadOnlyDictionary<string, string> chosen, CancellationToken cancellationToken = default);
}

public sealed class MariaActivityImportService(IConfiguration configuration, IDataProtectionProvider protection, IActivitySource source, IAccessControl access, IAuditTrail audit) : IActivityImportService
{
    private const string Purpose = "BlazorStoc.ActivityImport.Password";
    private const string Edit = "setari-import-activityapp.edit";
    private const string View = "setari-import-activityapp.view";
    private readonly IDataProtector protector = protection.CreateProtector(Purpose);

    public async Task<ActivityImportSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync(View, cancellationToken).ConfigureAwait(false);
        return (await ReadAsync(cancellationToken).ConfigureAwait(false)).Settings;
    }

    private async Task<(ActivityImportSettings Settings, string? Password)> ReadAsync(CancellationToken cancellationToken)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("""
            SELECT enabled, server_name, server_port, database_name, username, password_protected, since_date, window_from_hour, window_to_hour, page_size, fill_empty_fields,
                   last_test_utc, last_test_ok, last_test_message, structure_status, structure_fingerprint, structure_report, structure_checked_utc, updated_by, updated_utc,
                   sync_enabled, sync_interval_minutes, last_sync_utc, last_sync_ok, last_sync_message, sync_failures, sync_mode, sync_times, source_watermark
            FROM activity_import_settings WHERE id = 1
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return (new ActivityImportSettings(false, "", 1433, "TIPActivity", "", false, null, 0, 24, 200, false, null, null, "", StructureStatus.Unchecked, "", [], null, "", null), null);
        var protectedPassword = reader.IsDBNull(5) ? null : reader.GetString(5);
        string? password = null;
        // Keys lost or changed (another machine, a restored database): the password has to be typed again.
        if (protectedPassword is not null) try { password = protector.Unprotect(protectedPassword); } catch (System.Security.Cryptography.CryptographicException) { password = null; }
        var issues = ParseIssues(reader.IsDBNull(16) ? null : reader.GetString(16));
        var status = Enum.TryParse<StructureStatus>(reader.GetString(14), out var parsed) ? parsed : StructureStatus.Unchecked;
        var settings = new ActivityImportSettings(reader.GetBoolean(0), reader.GetString(1), reader.GetInt32(2), reader.GetString(3), reader.GetString(4), password is not null,
            reader.IsDBNull(6) ? null : DateOnly.FromDateTime(reader.GetDateTime(6)), reader.GetInt32(7), reader.GetInt32(8), reader.GetInt32(9), reader.GetBoolean(10),
            reader.IsDBNull(11) ? null : DateTime.SpecifyKind(reader.GetDateTime(11), DateTimeKind.Utc), reader.IsDBNull(12) ? null : reader.GetBoolean(12), reader.GetString(13), status,
            reader.GetString(15), issues, reader.IsDBNull(17) ? null : DateTime.SpecifyKind(reader.GetDateTime(17), DateTimeKind.Utc), reader.GetString(18),
            reader.IsDBNull(19) ? null : DateTime.SpecifyKind(reader.GetDateTime(19), DateTimeKind.Utc))
        {
            SyncEnabled = reader.GetBoolean(20), SyncIntervalMinutes = reader.GetInt32(21), LastSyncUtc = reader.IsDBNull(22) ? null : DateTime.SpecifyKind(reader.GetDateTime(22), DateTimeKind.Utc),
            LastSyncOk = reader.IsDBNull(23) ? null : reader.GetBoolean(23), LastSyncMessage = reader.GetString(24), SyncFailures = reader.GetInt32(25),
            SyncMode = reader.GetString(26), SyncTimes = reader.GetString(27), SourceWatermark = reader.IsDBNull(28) ? null : reader.GetDateTime(28)
        };
        return (settings, password);
    }

    private static IReadOnlyList<StructureIssue> ParseIssues(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<List<StructureIssue>>(json) ?? []; } catch (JsonException) { return []; }
    }

    private void EnsureAllowedDatabase()
    {
        if (!MariaDatabaseGuard.IsAllowedDatabase(configuration)) throw new ActivityImportException("Modificările sunt permise numai în baza BlazorStoc. Verifică numele bazei configurate.");
    }

    public async Task SaveSettingsAsync(ActivityImportInput input, CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync(Edit, cancellationToken).ConfigureAwait(false);
        input = Normalize(input);
        if (ActivityImportRules.Validate(input) is { } error) throw new ActivityImportException(error);
        EnsureAllowedDatabase();
        var (old, oldPassword) = await ReadAsync(cancellationToken).ConfigureAwait(false);
        var passwordChanged = !string.IsNullOrEmpty(input.Password);
        if (input.Enabled && (input.Server.Length == 0 || input.User.Length == 0 || (!passwordChanged && oldPassword is null)))
            throw new ActivityImportException("Pentru a activa preluarea completează serverul, contul și parola.");
        var actor = (await RepositoryAudit.ActorAsync(access, cancellationToken).ConfigureAwait(false)).Username;
        await using (var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false))
        {
            await using var command = new MySqlCommand("""
                INSERT INTO activity_import_settings (id, enabled, server_name, server_port, database_name, username, password_protected, since_date, window_from_hour, window_to_hour,
                    page_size, fill_empty_fields, sync_enabled, sync_interval_minutes, sync_mode, sync_times, updated_by, updated_utc)
                VALUES (1, @enabled, @server, @port, @database, @user, @pass, @since, @from, @to, @page, @fill, @sync, @interval, @mode, @times, @by, @now)
                ON DUPLICATE KEY UPDATE enabled=@enabled, server_name=@server, server_port=@port, database_name=@database, username=@user,
                    password_protected=IF(@changed=1, @pass, password_protected), since_date=@since, window_from_hour=@from, window_to_hour=@to, page_size=@page,
                    fill_empty_fields=@fill, sync_enabled=@sync, sync_interval_minutes=@interval, sync_mode=@mode, sync_times=@times, updated_by=@by, updated_utc=@now
                """, connection);
            command.Parameters.AddWithValue("@enabled", input.Enabled);
            command.Parameters.AddWithValue("@server", input.Server);
            command.Parameters.AddWithValue("@port", input.Port);
            command.Parameters.AddWithValue("@database", input.Database);
            command.Parameters.AddWithValue("@user", input.User);
            command.Parameters.AddWithValue("@pass", passwordChanged ? protector.Protect(input.Password!) : DBNull.Value);
            command.Parameters.AddWithValue("@changed", passwordChanged ? 1 : 0);
            command.Parameters.AddWithValue("@since", input.SinceDate is { } since ? since.ToDateTime(TimeOnly.MinValue) : DBNull.Value);
            command.Parameters.AddWithValue("@from", input.WindowFromHour);
            command.Parameters.AddWithValue("@to", input.WindowToHour);
            command.Parameters.AddWithValue("@page", input.PageSize);
            command.Parameters.AddWithValue("@fill", input.FillEmptyFields);
            command.Parameters.AddWithValue("@sync", input.SyncEnabled);
            command.Parameters.AddWithValue("@interval", input.SyncIntervalMinutes);
            command.Parameters.AddWithValue("@mode", input.SyncMode);
            command.Parameters.AddWithValue("@times", ActivitySyncSchedule.FormatTimes(ActivitySyncSchedule.ParseTimes(input.SyncTimes, out _)));
            command.Parameters.AddWithValue("@by", actor);
            command.Parameters.AddWithValue("@now", DateTime.UtcNow);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        var today = DateOnly.FromDateTime(DateTime.Now);
        var changes = new[]
        {
            new AuditChange("Preluare activă", Flag(old.Enabled), Flag(input.Enabled)),
            new AuditChange("Server ActivityApp", Show(old.Server), Show(input.Server)),
            new AuditChange("Port", old.Port.ToString(), input.Port.ToString()),
            new AuditChange("Bază de date", Show(old.Database), Show(input.Database)),
            new AuditChange("Cont de citire", Show(old.User), Show(input.User)),
            new AuditChange("Parolă cont", old.HasPassword ? "setată" : "—", passwordChanged ? "schimbată" : old.HasPassword ? "setată" : "—"),
            new AuditChange("Tichete de la data", old.EffectiveSince(today).ToString("dd.MM.yyyy"), (input.SinceDate ?? new DateOnly(today.Year, 1, 1)).ToString("dd.MM.yyyy")),
            new AuditChange("Fereastra orară", $"{old.WindowFromHour}-{old.WindowToHour}", $"{input.WindowFromHour}-{input.WindowToHour}"),
            new AuditChange("Mărime pagină", old.PageSize.ToString(), input.PageSize.ToString()),
            new AuditChange("Completare câmpuri goale", Flag(old.FillEmptyFields), Flag(input.FillEmptyFields)),
            new AuditChange("Sincronizare periodică", Flag(old.SyncEnabled), Flag(input.SyncEnabled)),
            new AuditChange("Interval sincronizare (minute)", old.SyncIntervalMinutes.ToString(), input.SyncIntervalMinutes.ToString()),
            new AuditChange("Mod sincronizare", ActivitySyncSchedule.ModeLabel(old.SyncMode), ActivitySyncSchedule.ModeLabel(input.SyncMode)),
            new AuditChange("Ore fixe sincronizare", Show(old.SyncTimes), Show(ActivitySyncSchedule.FormatTimes(ActivitySyncSchedule.ParseTimes(input.SyncTimes, out _))))
        };
        await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.ActivityImport, AuditActions.SetActivityImport, "", "Setări preluare ActivityApp",
            AuditDetails.Changes(changes), "", cancellationToken).ConfigureAwait(false);
    }

    public async Task<ActivityTestResult> TestAsync(ActivityImportInput input, CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync(Edit, cancellationToken).ConfigureAwait(false);
        input = Normalize(input);
        if (ActivityImportRules.Validate(input) is { } error) throw new ActivityImportException(error);
        var (stored, storedPassword) = await ReadAsync(cancellationToken).ConfigureAwait(false);
        var password = string.IsNullOrEmpty(input.Password) ? storedPassword : input.Password;
        if (input.Server.Length == 0 || input.User.Length == 0 || string.IsNullOrEmpty(password)) throw new ActivityImportException("Completează serverul, contul și parola (sau salvează-le mai întâi).");
        var connection = new ActivityConnection(input.Server, input.Port, input.Database, input.User, password);
        ActivityTestResult result;
        StructureReport? report = null;
        var ping = await source.PingAsync(connection, cancellationToken).ConfigureAwait(false);
        if (!ping.Ok) result = new ActivityTestResult(false, ping.Message, StructureStatus.Unchecked, false, false);
        else
        {
            try
            {
                report = ActivityStructureChecker.Check(await source.ReadStructureAsync(connection, cancellationToken).ConfigureAwait(false));
                var changed = stored.StructureFingerprint.Length > 0 && stored.StructureFingerprint != report.Fingerprint;
                result = new ActivityTestResult(true, TestMessage(ping, report, changed), report.Status, ping.WritableAccount, changed);
            }
            catch (ActivitySourceException exception) { result = new ActivityTestResult(false, exception.Message, StructureStatus.Unchecked, ping.WritableAccount, false); }
        }
        await RecordTestAsync(result, report, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.ActivityImport, AuditActions.TestActivityImport, "", "Testare conexiune ActivityApp",
            AuditDetails.Identification(("Server", $"{input.Server}:{input.Port}"), ("Bază de date", input.Database), ("Cont", input.User), ("Rezultat", result.Ok ? "reușit" : result.Message),
                ("Structură", StructureText(result.Structure))), "", cancellationToken).ConfigureAwait(false);
        return result;
    }

    internal static string TestMessage(ActivityPing ping, StructureReport report, bool changed)
    {
        var text = $"Conexiunea funcționează ({ping.ServerVersion}). Structura ActivityApp: {StructureText(report.Status)}.";
        if (changed) text += " Structura s-a schimbat față de ultima verificare.";
        if (ping.WritableAccount) text += " Atenție: contul are drepturi de scriere în ActivityApp; folosește un cont doar cu citire.";
        return text;
    }

    public static string StructureText(StructureStatus status) => status switch
    {
        StructureStatus.Ok => "corespunde",
        StructureStatus.Partial => "parțial (lipsesc câmpuri opționale)",
        StructureStatus.Changed => "schimbată (unele date nu pot fi preluate)",
        _ => "neverificată"
    };

    private async Task RecordTestAsync(ActivityTestResult result, StructureReport? report, CancellationToken cancellationToken)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        // The row is created by the first save of the settings; a test before that is not remembered.
        await using var command = new MySqlCommand("""
            UPDATE activity_import_settings SET last_test_utc=@now, last_test_ok=@ok, last_test_message=@message,
                structure_status=IF(@hasReport=1, @status, structure_status), structure_fingerprint=IF(@hasReport=1, @fingerprint, structure_fingerprint),
                structure_report=IF(@hasReport=1, @report, structure_report), structure_checked_utc=IF(@hasReport=1, @now, structure_checked_utc)
            WHERE id = 1
            """, connection);
        command.Parameters.AddWithValue("@now", DateTime.UtcNow);
        command.Parameters.AddWithValue("@ok", result.Ok);
        command.Parameters.AddWithValue("@message", result.Message.Length > 480 ? result.Message[..480] : result.Message);
        command.Parameters.AddWithValue("@hasReport", report is null ? 0 : 1);
        command.Parameters.AddWithValue("@status", report?.Status.ToString() ?? "");
        command.Parameters.AddWithValue("@fingerprint", report?.Fingerprint ?? "");
        command.Parameters.AddWithValue("@report", report is null ? DBNull.Value : JsonSerializer.Serialize(report.Issues));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ActivityTypesView> GetTypesAsync(CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync(View, cancellationToken).ConfigureAwait(false);
        var (settings, password) = await ReadAsync(cancellationToken).ConfigureAwait(false);
        var chosen = await ReadChosenAsync(cancellationToken).ConfigureAwait(false);
        if (settings.Server.Length == 0 || settings.User.Length == 0 || password is null)
            return new ActivityTypesView(Merge([], chosen), StructureStatus.Unchecked, "Completează și salvează mai întâi conexiunea la ActivityApp.");
        var connection = new ActivityConnection(settings.Server, settings.Port, settings.Database, settings.User, password);
        try
        {
            var report = ActivityStructureChecker.Check(await source.ReadStructureAsync(connection, cancellationToken).ConfigureAwait(false));
            var rows = await source.ReadTypesAsync(connection, report, cancellationToken).ConfigureAwait(false);
            if (report.Fingerprint != settings.StructureFingerprint) await SaveStructureAsync(report, cancellationToken).ConfigureAwait(false);
            return new ActivityTypesView(Merge(rows, chosen), report.Status, null);
        }
        catch (ActivitySourceException exception) { return new ActivityTypesView(Merge([], chosen), settings.StructureStatus, exception.Message); }
    }

    public async Task<ActivityTypesView> GetSavedTypesAsync(CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync(View, cancellationToken).ConfigureAwait(false);
        var (settings, _) = await ReadAsync(cancellationToken).ConfigureAwait(false);
        var chosen = await ReadChosenAsync(cancellationToken).ConfigureAwait(false);
        return new ActivityTypesView([.. chosen.Select(item => new ActivityTypeItem(item.Id, item.Name, "", null, null, null, null, null, item.Active, item.Destination, item.Active ? ActivityImportRules.StateSelected : ""))],
            settings.StructureStatus, null);
    }

    private async Task SaveStructureAsync(StructureReport report, CancellationToken cancellationToken)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("UPDATE activity_import_settings SET structure_status=@status, structure_fingerprint=@fingerprint, structure_report=@report, structure_checked_utc=@now WHERE id = 1", connection);
        command.Parameters.AddWithValue("@status", report.Status.ToString());
        command.Parameters.AddWithValue("@fingerprint", report.Fingerprint);
        command.Parameters.AddWithValue("@report", JsonSerializer.Serialize(report.Issues));
        command.Parameters.AddWithValue("@now", DateTime.UtcNow);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private sealed record Chosen(long Id, string Name, bool Active, string Destination);

    private async Task<List<Chosen>> ReadChosenAsync(CancellationToken cancellationToken)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("SELECT source_type_id, source_name, active, destination FROM activity_import_types ORDER BY source_type_id", connection);
        var result = new List<Chosen>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(new Chosen(reader.GetInt64(0), reader.GetString(1), reader.GetBoolean(2), reader.GetString(3)));
        return result;
    }

    // The stored choice is kept by the ID of the type; a type that has a different name in ActivityApp is "changed" and stays out of the import until the administrator confirms it
    // by saving the list again (the name then seen becomes the stored one); a type that is no longer there is shown with the name it had.
    internal static IReadOnlyList<ActivityTypeItem> Merge(IReadOnlyList<ActivityTypeRow> rows, IReadOnlyList<(long Id, string Name, bool Active, string Destination)> chosen)
    {
        var map = chosen.ToDictionary(item => item.Id);
        var anyChosen = chosen.Any(item => item.Active);
        var items = new List<ActivityTypeItem>();
        foreach (var row in rows)
        {
            map.TryGetValue(row.Id, out var pick);
            var selected = pick.Active && pick.Name == row.Name;
            var state = pick.Name is null ? (anyChosen ? ActivityImportRules.StateNew : "") : pick.Name != row.Name ? ActivityImportRules.StateChanged : pick.Active ? ActivityImportRules.StateSelected : "";
            items.Add(new ActivityTypeItem(row.Id, row.Name, row.Category, row.ContractService, row.AutoEvent, row.Resolved, row.Open, row.Deleted, selected,
                pick.Destination ?? ActivityDestination.OnDemand, state));
        }
        var present = rows.Select(row => row.Id).ToHashSet();
        foreach (var pick in chosen.Where(item => !present.Contains(item.Id)))
            items.Add(new ActivityTypeItem(pick.Id, pick.Name, "", null, null, null, null, null, false, pick.Destination, ActivityImportRules.StateMissing));
        return items;
    }

    private static IReadOnlyList<ActivityTypeItem> Merge(IReadOnlyList<ActivityTypeRow> rows, List<Chosen> chosen) =>
        Merge(rows, [.. chosen.Select(item => (item.Id, item.Name, item.Active, item.Destination))]);

    public async Task SaveTypesAsync(IReadOnlyList<ActivityTypeChoice> choices, CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync(Edit, cancellationToken).ConfigureAwait(false);
        if (ActivityImportRules.Validate(choices) is { } error) throw new ActivityImportException(error);
        EnsureAllowedDatabase();
        var old = await ReadChosenAsync(cancellationToken).ConfigureAwait(false);
        var actor = (await RepositoryAudit.ActorAsync(access, cancellationToken).ConfigureAwait(false)).Username;
        var selected = choices.Where(choice => choice.Selected).GroupBy(choice => choice.Id).Select(group => group.First()).ToList();
        var oldById = old.ToDictionary(item => item.Id);
        await using (var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false))
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            // A type that is in the list but not ticked is removed from the choice (what was imported stays; nothing is deleted here). A stored type that is missing from the
            // list (it is no longer in ActivityApp) is kept as it is.
            var listed = choices.Select(choice => choice.Id).ToHashSet();
            foreach (var choice in choices.Where(choice => !choice.Selected && oldById.ContainsKey(choice.Id)))
            {
                await using var delete = new MySqlCommand("DELETE FROM activity_import_types WHERE source_type_id = @id", connection, transaction);
                delete.Parameters.AddWithValue("@id", choice.Id);
                await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            foreach (var choice in selected)
            {
                var unchanged = oldById.TryGetValue(choice.Id, out var before) && before.Name == choice.Name && before.Destination == choice.Destination && before.Active;
                if (unchanged) continue;
                await using var upsert = new MySqlCommand("""
                    INSERT INTO activity_import_types (source_type_id, source_name, active, destination, chosen_by, chosen_utc) VALUES (@id, @name, 1, @destination, @by, @now)
                    ON DUPLICATE KEY UPDATE source_name=@name, active=1, destination=@destination, chosen_by=@by, chosen_utc=@now
                    """, connection, transaction);
                upsert.Parameters.AddWithValue("@id", choice.Id);
                upsert.Parameters.AddWithValue("@name", choice.Name.Length > 200 ? choice.Name[..200] : choice.Name);
                upsert.Parameters.AddWithValue("@destination", choice.Destination);
                upsert.Parameters.AddWithValue("@by", actor);
                upsert.Parameters.AddWithValue("@now", DateTime.UtcNow);
                await upsert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        var after = await ReadChosenAsync(cancellationToken).ConfigureAwait(false);
        var before2 = Describe(old);
        var after2 = Describe(after);
        if (before2 != after2)
            await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.ActivityImport, AuditActions.SetActivityImportTypes, "", "Tipuri de tichete preluate din ActivityApp",
                AuditDetails.Changes(new AuditChange("Tipuri preluate", before2, after2)), "", cancellationToken).ConfigureAwait(false);
    }

    private static string Describe(IEnumerable<Chosen> chosen)
    {
        var text = string.Join(", ", chosen.Where(item => item.Active).OrderBy(item => item.Id).Select(item => $"{item.Id} {item.Name} ({ActivityDestination.Label(item.Destination)})"));
        return text.Length == 0 ? "—" : text;
    }

    public async Task<ActivityConnection?> GetConnectionAsync(CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync("setari-import-activityapp.run", cancellationToken).ConfigureAwait(false);
        var (settings, password) = await ReadAsync(cancellationToken).ConfigureAwait(false);
        return settings.Server.Length == 0 || settings.User.Length == 0 || password is null ? null : new ActivityConnection(settings.Server, settings.Port, settings.Database, settings.User, password);
    }

    public async Task<ActivityFieldMap> GetFieldMapAsync(CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync(View, cancellationToken).ConfigureAwait(false);
        return await ReadFieldMapAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<ActivityFieldMap> ReadFieldMapAsync(CancellationToken cancellationToken)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("SELECT field_map FROM activity_import_settings WHERE id = 1", connection);
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is string json ? ActivityFieldMap.FromJson(json) : ActivityFieldMap.Empty;
    }

    public async Task<ActivityFieldMapView> GetFieldChoicesAsync(bool readSource, CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync(View, cancellationToken).ConfigureAwait(false);
        var map = await ReadFieldMapAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<ActivityFieldChoice> Offline() => [.. ActivityFieldMap.Fields.Select(field => new ActivityFieldChoice(field, map.ColumnOf(field), [], false))];
        if (!readSource) return new ActivityFieldMapView(Offline(), null);
        var (settings, password) = await ReadAsync(cancellationToken).ConfigureAwait(false);
        if (settings.Server.Length == 0 || settings.User.Length == 0 || password is null)
            return new ActivityFieldMapView(Offline(), "Completează și salvează mai întâi conexiunea la ActivityApp.");
        try
        {
            var actual = await source.ReadStructureAsync(new ActivityConnection(settings.Server, settings.Port, settings.Database, settings.User, password), cancellationToken).ConfigureAwait(false);
            return new ActivityFieldMapView([.. ActivityFieldMap.Fields.Select(field =>
            {
                var available = ActivityFieldMap.TextColumns(actual, field.Table);
                var current = map.ColumnOf(field);
                return new ActivityFieldChoice(field, current, available, !available.Contains(current, StringComparer.OrdinalIgnoreCase));
            })], null);
        }
        catch (ActivitySourceException exception) { return new ActivityFieldMapView(Offline(), exception.Message); }
    }

    public async Task SaveFieldMapAsync(IReadOnlyDictionary<string, string> chosen, CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync(Edit, cancellationToken).ConfigureAwait(false);
        var map = ActivityFieldMap.Create(chosen, out var error) ?? throw new ActivityImportException(error ?? "Corespondența câmpurilor nu este validă.");
        EnsureAllowedDatabase();
        var before = await ReadFieldMapAsync(cancellationToken).ConfigureAwait(false);
        if (before.ToJson() == map.ToJson()) return;
        var actor = (await RepositoryAudit.ActorAsync(access, cancellationToken).ConfigureAwait(false)).Username;
        await using (var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false))
        {
            // The row exists after the first save of the connection; before that there is nothing to map.
            await using var command = new MySqlCommand("UPDATE activity_import_settings SET field_map=@map, updated_by=@by, updated_utc=@now WHERE id = 1", connection);
            command.Parameters.AddWithValue("@map", map.Columns.Count == 0 ? DBNull.Value : map.ToJson());
            command.Parameters.AddWithValue("@by", actor);
            command.Parameters.AddWithValue("@now", DateTime.UtcNow);
            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 0) throw new ActivityImportException("Salvează mai întâi conexiunea la ActivityApp.");
        }
        await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.ActivityImport, AuditActions.SetActivityFieldMap, "", "Corespondența câmpurilor ActivityApp",
            AuditDetails.Changes(new AuditChange("Câmpuri citite din altă coloană", before.Describe(), map.Describe())), "", cancellationToken).ConfigureAwait(false);
    }

    private static ActivityImportInput Normalize(ActivityImportInput input) => input with
    {
        Server = input.Server.Trim(), Database = input.Database.Trim(), User = input.User.Trim(), Password = string.IsNullOrEmpty(input.Password) ? null : input.Password
    };

    private static string Show(string value) => value.Length == 0 ? "—" : value;
    private static string Flag(bool value) => value ? "da" : "nu";
}

/// <summary>Stand-in while the module is out: the settings tab says so through the module gate; nothing else injects the service.</summary>
internal sealed class UnavailableActivityImportService : IActivityImportService
{
    private static Task<T> Out<T>() => Task.FromException<T>(new ModuleUnavailableException(ActivityImportModule.ModuleId));
    public Task<ActivityImportSettings> GetSettingsAsync(CancellationToken cancellationToken = default) => Out<ActivityImportSettings>();
    public Task SaveSettingsAsync(ActivityImportInput input, CancellationToken cancellationToken = default) => Out<object>();
    public Task<ActivityTestResult> TestAsync(ActivityImportInput input, CancellationToken cancellationToken = default) => Out<ActivityTestResult>();
    public Task<ActivityTypesView> GetTypesAsync(CancellationToken cancellationToken = default) => Out<ActivityTypesView>();
    public Task<ActivityTypesView> GetSavedTypesAsync(CancellationToken cancellationToken = default) => Out<ActivityTypesView>();
    public Task SaveTypesAsync(IReadOnlyList<ActivityTypeChoice> choices, CancellationToken cancellationToken = default) => Out<object>();
    public Task<ActivityConnection?> GetConnectionAsync(CancellationToken cancellationToken = default) => Out<ActivityConnection?>();
    public Task<ActivityFieldMap> GetFieldMapAsync(CancellationToken cancellationToken = default) => Out<ActivityFieldMap>();
    public Task<ActivityFieldMapView> GetFieldChoicesAsync(bool readSource, CancellationToken cancellationToken = default) => Out<ActivityFieldMapView>();
    public Task SaveFieldMapAsync(IReadOnlyDictionary<string, string> chosen, CancellationToken cancellationToken = default) => Out<object>();
}

/// <summary>The entry points of the module behind the circuit breaker of the registry (a source that does not answer is an expected answer, not a fault).</summary>
internal sealed class GuardedActivityImportService(IActivityImportService inner, ModuleRegistry modules) : IActivityImportService
{
    private const string Id = ActivityImportModule.ModuleId;
    public Task<ActivityImportSettings> GetSettingsAsync(CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.GetSettingsAsync(cancellationToken));
    public Task SaveSettingsAsync(ActivityImportInput input, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.SaveSettingsAsync(input, cancellationToken));
    public Task<ActivityTestResult> TestAsync(ActivityImportInput input, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.TestAsync(input, cancellationToken));
    public Task<ActivityTypesView> GetTypesAsync(CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.GetTypesAsync(cancellationToken));
    public Task<ActivityTypesView> GetSavedTypesAsync(CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.GetSavedTypesAsync(cancellationToken));
    public Task SaveTypesAsync(IReadOnlyList<ActivityTypeChoice> choices, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.SaveTypesAsync(choices, cancellationToken));
    public Task<ActivityConnection?> GetConnectionAsync(CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.GetConnectionAsync(cancellationToken));
    public Task<ActivityFieldMap> GetFieldMapAsync(CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.GetFieldMapAsync(cancellationToken));
    public Task<ActivityFieldMapView> GetFieldChoicesAsync(bool readSource, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.GetFieldChoicesAsync(readSource, cancellationToken));
    public Task SaveFieldMapAsync(IReadOnlyDictionary<string, string> chosen, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.SaveFieldMapAsync(chosen, cancellationToken));
}
