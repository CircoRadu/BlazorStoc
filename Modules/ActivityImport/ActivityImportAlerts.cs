using BlazorStoc.Services;
using MySqlConnector;

namespace BlazorStoc.Modules.ActivityImport;

// The notification for the administrator (Setari -> Notificari, like every other source: it gets its starting template automatically). One source, "Preluare ActivityApp cu probleme",
// with one instance for each condition that lasts: the synchronization failing (three attempts in a row), the structure of ActivityApp changed, open problems waiting, ticket types that are
// new or changed in ActivityApp. Each condition starts on the day it began and goes away by itself when it is gone (the engine then closes the notification).

public sealed record ActivityImportAlertState(DateTime? FailingSince, string FailureMessage, int Failures, DateTime? StructureSince, int StructureIssues, DateTime? IssuesSince, int OpenIssues, DateTime? TypesSince);

public interface IActivityImportAlertReader
{
    Task<ActivityImportAlertState> GetAsync(CancellationToken cancellationToken = default);
}

public sealed class MariaActivityImportAlertReader(IConfiguration configuration) : IActivityImportAlertReader
{
    public async Task<ActivityImportAlertState> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        long openIssues;
        await using (var count = new MySqlCommand("SELECT COUNT(*) FROM activity_import_issues WHERE resolved = 0", connection)) openIssues = Convert.ToInt64(await count.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        await using var command = new MySqlCommand("SELECT alert_failing_since, last_sync_message, sync_failures, alert_structure_since, structure_report, alert_issues_since, alert_types_since FROM activity_import_settings WHERE id = 1", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return new ActivityImportAlertState(null, "", 0, null, 0, null, 0, null);
        DateTime? At(int ordinal) => reader.IsDBNull(ordinal) ? null : DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc);
        var report = reader.IsDBNull(4) ? "" : reader.GetString(4);
        var structureIssues = report.Length == 0 ? 0 : System.Text.Json.JsonSerializer.Deserialize<List<StructureIssue>>(report)?.Count(issue => issue.Required) ?? 0;
        return new ActivityImportAlertState(At(0), reader.GetString(1), reader.GetInt32(2), At(3), structureIssues, At(5), (int)openIssues, At(6));
    }
}

public sealed class ActivityImportAlertSource(IActivityImportAlertReader reader, string key = ActivityImportAlertSource.SourceKey) : IExpirySource
{
    public const string SourceKey = "sistem.preluare-activityapp";
    public const string ProblemName = "problema preluare";
    public const string DetailsName = "detalii preluare";
    public const int FailingId = 1, StructureId = 2, IssuesId = 3, TypesId = 4;
    public string Key => key;
    public string Category => "Sistem";
    public string EventName => "Preluare ActivityApp cu probleme";
    public string DateLabel => "constatării problemei";
    public int DefaultThresholdDays => 0;
    public string RemovedReason => "Problema preluării din ActivityApp a dispărut.";
    public string DefaultSubject => "Preluare ActivityApp: <problema preluare>";
    public string DefaultBody => "<detalii preluare> Vezi Setări → Preluare ActivityApp.";
    public IReadOnlyList<ExpiryPlaceholder> Placeholders { get; } =
    [
        new(ProblemName, "Ce nu merge la preluarea din ActivityApp", "sincronizarea eșuează"),
        new(DetailsName, "Detalii despre problemă", "Ultimele 3 sincronizări au eșuat: ActivityApp nu a răspuns la timp.")
    ];

    public string DateChangedReason(DateOnly from, DateOnly to, ExpiryInstance current) =>
        $"Problema s-a reluat: constatarea s-a mutat de la {StockMovementRules.DisplayDate(from)} la {StockMovementRules.DisplayDate(to)}.";

    private static DateOnly Day(DateTime utc) => DateOnly.FromDateTime(utc.ToLocalTime());

    /// <summary>The conditions that last, as instances (one per condition).</summary>
    public static IReadOnlyList<ExpiryInstance> Instances(ActivityImportAlertState state)
    {
        const string url = "/setari?tab=preluare-activityapp";
        var result = new List<ExpiryInstance>();
        ExpiryInstance Make(int id, string label, DateTime since, string problem, string details) =>
            new(id, label, Day(since), new Dictionary<string, string> { [ProblemName] = problem, [DetailsName] = details }, url);
        if (state.FailingSince is { } failing)
            result.Add(Make(FailingId, "Sincronizarea cu ActivityApp", failing, "sincronizarea eșuează",
                $"Ultimele {Math.Max(state.Failures, ActivitySyncSchedule.FailuresBeforeAlert)} sincronizări au eșuat. Cauza: {(state.FailureMessage.Length > 0 ? state.FailureMessage : "necunoscută")}"));
        if (state.StructureSince is { } structure)
            result.Add(Make(StructureId, "Structura ActivityApp", structure, "structura ActivityApp s-a schimbat",
                $"După o actualizare a ActivityApp, {(state.StructureIssues > 0 ? state.StructureIssues + " coloane necesare lipsesc sau au alt tip" : "structura nu mai corespunde")}; datele afectate nu se preiau. Poate fi nevoie de o actualizare a modulului de preluare."));
        if (state.IssuesSince is { } issues && state.OpenIssues > 0)
            result.Add(Make(IssuesId, "Probleme de preluare", issues, "probleme de preluare deschise", $"Sunt {state.OpenIssues} probleme de preluare deschise (elemente care nu s-au putut prelua sau au fost șterse în ActivityApp)."));
        if (state.TypesSince is { } types)
            result.Add(Make(TypesId, "Tipuri de tichete", types, "tipuri de tichete noi sau schimbate", "ActivityApp are tipuri de tichete noi sau cu alt nume decât cele alese; tipurile schimbate nu se preiau până le confirmi."));
        return result;
    }

    public async Task<IReadOnlyList<ExpiryInstance>> GetInstancesAsync(CancellationToken cancellationToken = default) =>
        Instances(await reader.GetAsync(cancellationToken).ConfigureAwait(false));
}
