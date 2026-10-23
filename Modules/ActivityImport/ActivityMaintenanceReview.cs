using BlazorStoc.Services;
using MySqlConnector;

namespace BlazorStoc.Modules.ActivityImport;

// A ticket whose closing text mentions a maintenance keyword (a word found exactly, or one that only resembles it: tickets are also closed from the mobile application) at a point under an
// active maintenance contract is added at once to the interventions of the beneficiary, on demand, and proposed to the operator: a text with the word may say that the maintenance could NOT be
// done ("mentenanța nu s-a putut face pentru că..."), which no program can read. The administrator is told by a notification and, in Setari -> Preluare ActivityApp, says for each ticket:
//   - the maintenance was done: the intervention becomes a maintenance one (the next due date moves by the cycle of the contract);
//   - it was not (or not the maintenance): it stays on demand;
//   - discard: the intervention is deleted and the ticket is never taken again (it is marked in the database so that a new synchronization does not read it; it can be reactivated).
// A decision is kept and never asked again.

public sealed record ActivityReviewItem(long Id, long TicketId, string TicketNumber, string Title, string Beneficiary, string WorkPoint, string Resolution, string Keyword, string Found,
    DateOnly PerformedOn, DateTime CreatedUtc, string Kind = "approximate", bool InRegister = false);

/// <summary>What the operator decides about a ticket proposed for validation.</summary>
public enum ReviewDecision { Maintenance, OnDemand, Discard }

/// <summary>A ticket discarded by the operator (never read again unless it is reactivated).</summary>
public sealed record ActivityDiscardedTicket(long TicketId, string TicketNumber, string Title, string DiscardedBy, DateTime DiscardedUtc, bool CanReactivate);

public interface IActivityReviewService
{
    /// <summary>The tickets waiting for the decision, oldest first.</summary>
    Task<IReadOnlyList<ActivityReviewItem>> GetPendingAsync(CancellationToken cancellationToken = default);
    /// <summary>The decision on one ticket. Journaled by name; a decision already taken is an <see cref="ActivityImportException"/>.</summary>
    Task DecideAsync(long id, ReviewDecision decision, CancellationToken cancellationToken = default);
    /// <summary>The tickets discarded, newest first.</summary>
    Task<IReadOnlyList<ActivityDiscardedTicket>> GetDiscardedAsync(CancellationToken cancellationToken = default);
    /// <summary>A discarded ticket is taken again by the next synchronization.</summary>
    Task ReactivateAsync(long ticketId, CancellationToken cancellationToken = default);
}

/// <summary>What the import keeps about the tickets to validate (the decisions are read by the planner; the proposals are written by the application of the tickets).</summary>
public static class ActivityReviewStore
{
    public const string Pending = "pending", Confirmed = "confirmed", Rejected = "rejected", Discarded = "discarded";

    /// <summary>The decisions taken, by ticket (a pending or a discarded one is not a decision: a discarded ticket is left out by its own state).</summary>
    public static async Task<IReadOnlyDictionary<long, KeywordDecision>> DecisionsAsync(IConfiguration configuration, CancellationToken cancellationToken)
    {
        var result = new Dictionary<long, KeywordDecision>();
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("SELECT ticket_id, status FROM activity_import_suggestions WHERE status IN ('confirmed', 'rejected')", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result[reader.GetInt64(0)] = reader.GetString(1) == Confirmed ? KeywordDecision.Confirmed : KeywordDecision.Rejected;
        return result;
    }

    /// <summary>Stores the proposal for a ticket (once: a ticket already proposed or decided is left as it is, only its intervention is completed). Returns whether a row was written.</summary>
    public static async Task<bool> AddAsync(IConfiguration configuration, TicketPlan plan, int? interventionId, CancellationToken cancellationToken)
    {
        var source = plan.Source!;
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("""
            INSERT INTO activity_import_suggestions (ticket_id, type_id, beneficiary_id, work_point_id, ticket_number, title, resolution, keyword, found, performed_on, status, created_utc, intervention_id, kind)
            VALUES (@ticket, @type, @beneficiary, @point, @number, @title, @resolution, @keyword, @found, @performed, 'pending', @now, @intervention, @kind)
            ON DUPLICATE KEY UPDATE intervention_id = COALESCE(intervention_id, @intervention)
            """, connection);
        command.Parameters.AddWithValue("@ticket", plan.SourceId);
        command.Parameters.AddWithValue("@type", source.TypeId);
        command.Parameters.AddWithValue("@beneficiary", plan.BeneficiaryId!.Value);
        command.Parameters.AddWithValue("@point", plan.WorkPointId!.Value);
        command.Parameters.AddWithValue("@number", Cut(source.Number, 100));
        command.Parameters.AddWithValue("@title", Cut(source.Title, 500));
        command.Parameters.AddWithValue("@resolution", source.Resolution);
        command.Parameters.AddWithValue("@keyword", Cut(plan.Keyword.Length > 0 ? plan.Keyword : plan.Approximate, 100));
        command.Parameters.AddWithValue("@found", Cut(plan.Found, 200));
        command.Parameters.AddWithValue("@intervention", (object?)interventionId ?? DBNull.Value);
        command.Parameters.AddWithValue("@kind", plan.MatchKind.Length > 0 ? plan.MatchKind : "approximate");
        command.Parameters.AddWithValue("@performed", plan.PerformedOn!.Value.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("@now", DateTime.UtcNow);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
    }

    private static string Cut(string value, int length) => value.Length > length ? value[..length] : value;
}

public sealed class MariaActivityReviewService(IConfiguration configuration, IAccessControl access, IAuditTrail audit, IOnDemandInterventionRecorder? interventions = null) : IActivityReviewService
{
    public async Task<IReadOnlyList<ActivityReviewItem>> GetPendingAsync(CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync("setari-import-activityapp.view", cancellationToken).ConfigureAwait(false);
        return await ReadPendingAsync(configuration, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<IReadOnlyList<ActivityReviewItem>> ReadPendingAsync(IConfiguration configuration, CancellationToken cancellationToken)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("""
            SELECT s.id, s.ticket_id, s.ticket_number, s.title, COALESCE(b.name, ''), COALESCE(w.name, ''), COALESCE(s.resolution, ''), s.keyword, s.found, s.performed_on, s.created_utc, s.kind, s.intervention_id
            FROM activity_import_suggestions s
            LEFT JOIN beneficiaries b ON b.id = s.beneficiary_id
            LEFT JOIN beneficiary_work_points w ON w.id = s.work_point_id
            WHERE s.status = 'pending' ORDER BY s.created_utc, s.id
            """, connection);
        var items = new List<ActivityReviewItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            items.Add(new(reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7),
                reader.GetString(8), DateOnly.FromDateTime(reader.GetDateTime(9)), DateTime.SpecifyKind(reader.GetDateTime(10), DateTimeKind.Utc), reader.GetString(11), !reader.IsDBNull(12)));
        return items;
    }

    private sealed record Row(long Id, long TicketId, int BeneficiaryId, int WorkPointId, DateOnly PerformedOn, string Keyword, int? InterventionId, string Status);

    private async Task<Row?> FindAsync(long id, CancellationToken cancellationToken)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("SELECT id, ticket_id, beneficiary_id, work_point_id, performed_on, keyword, intervention_id, status FROM activity_import_suggestions WHERE id=@id", connection);
        command.Parameters.AddWithValue("@id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new Row(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt32(2), reader.GetInt32(3), DateOnly.FromDateTime(reader.GetDateTime(4)), reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetInt32(6), reader.GetString(7))
            : null;
    }

    private async Task ExecuteAsync(string sql, Action<MySqlParameterCollection> bind, CancellationToken cancellationToken)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand(sql, connection);
        bind(command.Parameters);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DecideAsync(long id, ReviewDecision decision, CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync("setari-import-activityapp.run", cancellationToken).ConfigureAwait(false);
        if (!MariaDatabaseGuard.IsAllowedDatabase(configuration)) throw new ActivityImportException("Modificările sunt permise numai în baza BlazorStoc. Verifică numele bazei configurate.");
        var actor = (await RepositoryAudit.ActorAsync(access, cancellationToken).ConfigureAwait(false)).Username;
        var item = (await ReadPendingAsync(configuration, cancellationToken).ConfigureAwait(false)).FirstOrDefault(entry => entry.Id == id);
        var row = await FindAsync(id, cancellationToken).ConfigureAwait(false);
        if (item is null || row is null || row.Status != ActivityReviewStore.Pending) throw new ActivityImportException("Tichetul nu mai așteaptă validarea (a fost decis între timp). Actualizează lista.");
        var outcome = decision switch { ReviewDecision.Maintenance => ActivityReviewStore.Confirmed, ReviewDecision.OnDemand => ActivityReviewStore.Rejected, _ => ActivityReviewStore.Discarded };
        try
        {
            switch (decision)
            {
                case ReviewDecision.Maintenance when row.InterventionId is { } onDemand:
                    await ConvertToMaintenanceAsync(row, item, onDemand, cancellationToken).ConfigureAwait(false);
                    break;
                case ReviewDecision.Discard:
                    if (row.InterventionId is { } created && interventions is not null) await interventions.DeleteAsync(row.BeneficiaryId, created, "Discard tichet ActivityApp.", cancellationToken).ConfigureAwait(false);
                    await ExecuteAsync("DELETE FROM service_intervention_source WHERE source_ticket_id=@ticket", p => p.AddWithValue("@ticket", row.TicketId), cancellationToken).ConfigureAwait(false);
                    await ExecuteAsync("DELETE FROM activity_import_map WHERE entity='ticket' AND source_id=@ticket", p => p.AddWithValue("@ticket", row.TicketId), cancellationToken).ConfigureAwait(false);
                    await ActivityTicketStore.DiscardAsync(configuration, row.TicketId, actor, cancellationToken).ConfigureAwait(false);
                    break;
            }
        }
        catch (ModuleUnavailableException) { throw new ActivityImportException("Modulul Mentenanță este oprit: decizia se poate lua după ce pornește."); }
        catch (OnDemandInterventionOperationException exception) { throw new ActivityImportException(exception.Message); }
        await ExecuteAsync("UPDATE activity_import_suggestions SET status=@status, decided_by=@by, decided_utc=@now WHERE id=@id AND status='pending'", p =>
        {
            p.AddWithValue("@status", outcome);
            p.AddWithValue("@by", actor);
            p.AddWithValue("@now", DateTime.UtcNow);
            p.AddWithValue("@id", id);
        }, cancellationToken).ConfigureAwait(false);
        var (action, title, text) = decision switch
        {
            ReviewDecision.Maintenance => (AuditActions.DecideActivityMaintenance, "Validare tichet ActivityApp ca mentenanță", "este mentenanță (intervenția devine mentenanță)"),
            ReviewDecision.OnDemand => (AuditActions.DecideActivityMaintenance, "Validare tichet ActivityApp: nu este mentenanță", "nu este mentenanță (rămâne la cerere)"),
            _ => (AuditActions.DiscardActivityTicket, "Discard tichet ActivityApp", "discard (intervenția se șterge, tichetul nu se mai preia)")
        };
        await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.ActivityImport, action, id.ToString(), title,
            AuditDetails.Identification(("Tichet", $"{item.TicketNumber} {item.Title}".Trim()), ("Beneficiar", item.Beneficiary.Length > 0 ? item.Beneficiary : "—"), ("Cuvânt cheie", item.Keyword),
                ("Scris în tichet", item.Found), ("Decizie", text)), "", cancellationToken).ConfigureAwait(false);
    }

    // The on-demand intervention made at once becomes a maintenance one: the maintenance is recorded (the next due date moves by the cycle of the contract), the ticket is tied to it, and the
    // on-demand one is deleted. When the register refuses (the contract expired since), nothing changes and the operator is told.
    private async Task ConvertToMaintenanceAsync(Row row, ActivityReviewItem item, int onDemandId, CancellationToken cancellationToken)
    {
        if (interventions is null) throw new ActivityImportException("Modulul Mentenanță nu este disponibil.");
        var stored = await ActivityTicketStore.FindAsync(configuration, row.TicketId, cancellationToken).ConfigureAwait(false);
        var (problem, resolution) = stored is null ? ("", item.Resolution) : ActivityTicketPlanner.SplitReport(stored.Ticket.Problem, stored.Ticket.Report);
        var notes = ActivityTicketPlanner.Notes(item.TicketNumber, item.Title, problem, resolution, stored?.Ticket.Executor ?? "", row.Keyword, true);
        var maintenanceId = await interventions.RecordMaintenanceAsync(row.BeneficiaryId, row.WorkPointId, row.PerformedOn, notes, cancellationToken).ConfigureAwait(false);
        await ExecuteAsync("UPDATE service_intervention_source SET intervention_id=@new WHERE intervention_id=@old", p => { p.AddWithValue("@new", maintenanceId); p.AddWithValue("@old", onDemandId); }, cancellationToken).ConfigureAwait(false);
        await ExecuteAsync("UPDATE activity_import_map SET local_id=@new WHERE entity='ticket' AND source_id=@ticket", p => { p.AddWithValue("@new", maintenanceId); p.AddWithValue("@ticket", row.TicketId); }, cancellationToken).ConfigureAwait(false);
        await ExecuteAsync("UPDATE activity_import_tickets SET intervention_id=@new, updated_utc=@now WHERE ticket_id=@ticket", p => { p.AddWithValue("@new", maintenanceId); p.AddWithValue("@now", DateTime.UtcNow); p.AddWithValue("@ticket", row.TicketId); }, cancellationToken).ConfigureAwait(false);
        await ExecuteAsync("UPDATE activity_import_suggestions SET intervention_id=@new WHERE id=@id", p => { p.AddWithValue("@new", maintenanceId); p.AddWithValue("@id", row.Id); }, cancellationToken).ConfigureAwait(false);
        await interventions.DeleteAsync(row.BeneficiaryId, onDemandId, "Înlocuită cu mentenanța validată de operator (tichet ActivityApp).", cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ActivityDiscardedTicket>> GetDiscardedAsync(CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync("setari-import-activityapp.view", cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("SELECT ticket_id, ticket_number, title, COALESCE(decided_by, ''), COALESCE(decided_utc, updated_utc), type_id FROM activity_import_tickets WHERE state='discarded' ORDER BY decided_utc DESC, ticket_id DESC LIMIT 200", connection);
        var items = new List<ActivityDiscardedTicket>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            items.Add(new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), DateTime.SpecifyKind(reader.GetDateTime(4), DateTimeKind.Utc), reader.GetInt64(5) != 0));
        return items;
    }

    public async Task ReactivateAsync(long ticketId, CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync("setari-import-activityapp.run", cancellationToken).ConfigureAwait(false);
        if (!MariaDatabaseGuard.IsAllowedDatabase(configuration)) throw new ActivityImportException("Modificările sunt permise numai în baza BlazorStoc. Verifică numele bazei configurate.");
        var actor = (await RepositoryAudit.ActorAsync(access, cancellationToken).ConfigureAwait(false)).Username;
        var stored = await ActivityTicketStore.FindAsync(configuration, ticketId, cancellationToken).ConfigureAwait(false);
        if (stored is null || stored.State != TicketStates.Discarded) throw new ActivityImportException("Tichetul nu mai este discardat. Actualizează lista.");
        await ActivityTicketStore.ReactivateAsync(configuration, ticketId, cancellationToken).ConfigureAwait(false);
        await ExecuteAsync("DELETE FROM activity_import_suggestions WHERE ticket_id=@ticket AND status='discarded'", p => p.AddWithValue("@ticket", ticketId), cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.ActivityImport, AuditActions.ReactivateActivityTicket, ticketId.ToString(), "Reactivare tichet ActivityApp discardat",
            AuditDetails.Identification(("Tichet", $"{stored.Ticket.Number} {stored.Ticket.Title}".Trim()), ("Reactivat de", actor)), "", cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>The notification for the administrator: one instance for each ticket waiting for validation, on the day it was found; it goes away by itself when he decides.</summary>
public sealed class ActivityReviewSource(IConfiguration configuration, string key = ActivityReviewSource.SourceKey) : IExpirySource
{
    public const string SourceKey = "sistem.mentenanta-activityapp-de-validat";
    public const string TicketName = "tichet de validat";
    public const string DetailsName = "detalii validare";
    public string Key => key;
    public string Category => "Sistem";
    public string EventName => "Eveniment ActivityApp de validat ca mentenanță";
    public string DateLabel => "constatării";
    public int DefaultThresholdDays => 0;
    public string RemovedReason => "Evenimentul a fost validat, respins sau discardat.";
    public string DefaultSubject => "Validează mentenanța: <tichet de validat>";
    public string DefaultBody => "<detalii validare> Vezi Setări → Preluare ActivityApp, „Evenimente de validat”.";
    public IReadOnlyList<ExpiryPlaceholder> Placeholders { get; } =
    [
        new(TicketName, "Tichetul din ActivityApp care așteaptă validarea", "T-1042 Revizie centrală"),
        new(DetailsName, "Ce cuvânt cheie s-a găsit în tichet și la ce beneficiar", "La beneficiarul Alfa SRL, în tichet scrie «mentenanță» (cuvânt cheie «mentenanță»).")
    ];

    public string DateChangedReason(DateOnly from, DateOnly to, ExpiryInstance current) =>
        $"Constatarea s-a mutat de la {StockMovementRules.DisplayDate(from)} la {StockMovementRules.DisplayDate(to)}.";

    public static IReadOnlyList<ExpiryInstance> Instances(IReadOnlyList<ActivityReviewItem> items) =>
        [.. items.Select(item => new ExpiryInstance(checked((int)item.Id), $"Tichet {item.TicketNumber} {item.Title}".Trim(), DateOnly.FromDateTime(item.CreatedUtc.ToLocalTime()),
            new Dictionary<string, string>
            {
                [TicketName] = $"{item.TicketNumber} {item.Title}".Trim(),
                [DetailsName] = item.Kind == "exact"
                    ? $"La beneficiarul {(item.Beneficiary.Length > 0 ? item.Beneficiary : "—")}, în tichet scrie «{item.Found}»: verifică dacă mentenanța s-a făcut cu adevărat."
                    : $"La beneficiarul {(item.Beneficiary.Length > 0 ? item.Beneficiary : "—")}, în tichet scrie «{item.Found}» (seamănă cu «{item.Keyword}»): verifică dacă este mentenanță."
            }, "/setari?tab=preluare-activityapp"))];

    public async Task<IReadOnlyList<ExpiryInstance>> GetInstancesAsync(CancellationToken cancellationToken = default) =>
        Instances(await MariaActivityReviewService.ReadPendingAsync(configuration, cancellationToken).ConfigureAwait(false));
}

internal sealed class GuardedActivityReviewService(IActivityReviewService inner, ModuleRegistry modules) : IActivityReviewService
{
    private const string Id = ActivityImportModule.ModuleId;
    public Task<IReadOnlyList<ActivityReviewItem>> GetPendingAsync(CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.GetPendingAsync(cancellationToken));
    public Task DecideAsync(long id, ReviewDecision decision, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.DecideAsync(id, decision, cancellationToken));
    public Task<IReadOnlyList<ActivityDiscardedTicket>> GetDiscardedAsync(CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.GetDiscardedAsync(cancellationToken));
    public Task ReactivateAsync(long ticketId, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.ReactivateAsync(ticketId, cancellationToken));
}

internal sealed class UnavailableActivityReviewService : IActivityReviewService
{
    public Task<IReadOnlyList<ActivityReviewItem>> GetPendingAsync(CancellationToken cancellationToken = default) => Task.FromException<IReadOnlyList<ActivityReviewItem>>(new ModuleUnavailableException(ActivityImportModule.ModuleId));
    public Task DecideAsync(long id, ReviewDecision decision, CancellationToken cancellationToken = default) => Task.FromException(new ModuleUnavailableException(ActivityImportModule.ModuleId));
    public Task<IReadOnlyList<ActivityDiscardedTicket>> GetDiscardedAsync(CancellationToken cancellationToken = default) => Task.FromException<IReadOnlyList<ActivityDiscardedTicket>>(new ModuleUnavailableException(ActivityImportModule.ModuleId));
    public Task ReactivateAsync(long ticketId, CancellationToken cancellationToken = default) => Task.FromException(new ModuleUnavailableException(ActivityImportModule.ModuleId));
}
