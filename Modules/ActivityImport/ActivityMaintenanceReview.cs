using BlazorStoc.Services;
using MySqlConnector;

namespace BlazorStoc.Modules.ActivityImport;

// The queue of the events to validate: EVERY resolved ticket of the chosen types waits here (nothing is recorded by itself). The ticket shows what it looks like (a type taken as maintenance, a
// maintenance keyword in what was done - exact, or one that only resembles it, as tickets are also closed from the mobile application - or a plain service). An operator (the administrator, or who
// holds the permission to validate tickets) opens it, takes it as it is, or edits it first (category, work point, date, notes) and takes it; only then the intervention is recorded: maintenance (even
// when the point has no active contract yet: it then has no contract and moves no due date) or a service on demand. Or discards it: the ticket is marked in the database and never read again
// (it can be reactivated). A decision is kept and never asked again.

public sealed record ActivityReviewItem(long Id, long TicketId, string TicketNumber, string Title, string Beneficiary, string WorkPoint, string Resolution, string Keyword, string Found,
    DateOnly PerformedOn, DateTime CreatedUtc, string Kind = "approximate", bool InRegister = false);

/// <summary>What the operator decides about a ticket in the queue (taking it as it is: maintenance or service on demand, or discarding it).</summary>
public enum ReviewDecision { Maintenance, OnDemand, Discard }

/// <summary>The category an operator gives a ticket when taking it.</summary>
public enum ReviewCategory { Maintenance, Service }

public sealed record ReviewWorkPoint(int Id, string Name, string Address);

/// <summary>Everything the popup of a ticket shows and the editor starts from: the ticket as read from ActivityApp, the points of the beneficiary, the notes each category would get.</summary>
public sealed record ActivityReviewDetail(ActivityReviewItem Item, int BeneficiaryId, int WorkPointId, TicketSource Source, IReadOnlyList<ReviewWorkPoint> WorkPoints, bool Covered,
    string NotesService, string NotesMaintenance, ReviewCategory Proposed, bool Legacy);

/// <summary>An edit + take: what the operator chose for the intervention that is recorded.</summary>
public sealed record ReviewTakeInput(ReviewCategory Category, int WorkPointId, DateOnly PerformedOn, string? Notes);

/// <summary>A ticket discarded by the operator (never read again unless it is reactivated).</summary>
public sealed record ActivityDiscardedTicket(long TicketId, string TicketNumber, string Title, string DiscardedBy, DateTime DiscardedUtc, bool CanReactivate);

public interface IActivityReviewService
{
    /// <summary>The tickets waiting for the decision, oldest first.</summary>
    Task<IReadOnlyList<ActivityReviewItem>> GetPendingAsync(CancellationToken cancellationToken = default);
    /// <summary>One ticket of the queue with what the popup needs.</summary>
    Task<ActivityReviewDetail> GetDetailAsync(long id, CancellationToken cancellationToken = default);
    /// <summary>"Preluare" as it is (maintenance or service on demand) or a discard. Journaled by name; a decision already taken is an <see cref="ActivityImportException"/>.</summary>
    Task DecideAsync(long id, ReviewDecision decision, CancellationToken cancellationToken = default);
    /// <summary>Edit + take: records the intervention with what the operator chose.</summary>
    Task TakeAsync(long id, ReviewTakeInput input, CancellationToken cancellationToken = default);
    /// <summary>The tickets discarded, newest first.</summary>
    Task<IReadOnlyList<ActivityDiscardedTicket>> GetDiscardedAsync(CancellationToken cancellationToken = default);
    /// <summary>A discarded ticket is taken again by the next synchronization.</summary>
    Task ReactivateAsync(long ticketId, CancellationToken cancellationToken = default);
}

/// <summary>What the import keeps about the tickets to validate (the proposals are written by the application of the tickets).</summary>
public static class ActivityReviewStore
{
    public const string Pending = "pending", Confirmed = "confirmed", Rejected = "rejected", Discarded = "discarded";

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

    /// <summary>Takes out of the queue the tickets proposed as service without any keyword (an earlier rule put them there): they are not tickets to validate. Returns how many.</summary>
    public static async Task<int> RemoveUnmatchedServiceAsync(IConfiguration configuration, CancellationToken cancellationToken)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var tickets = new MySqlCommand("""
            DELETE t FROM activity_import_tickets t JOIN activity_import_suggestions s ON s.ticket_id = t.ticket_id
            WHERE s.status = 'pending' AND s.kind = 'service' AND s.intervention_id IS NULL AND t.state = 'pending_approval'
            """, connection);
        await tickets.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await using var suggestions = new MySqlCommand("DELETE FROM activity_import_suggestions WHERE status = 'pending' AND kind = 'service' AND intervention_id IS NULL", connection);
        return await suggestions.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string Cut(string value, int length) => value.Length > length ? value[..length] : value;
}

public sealed class MariaActivityReviewService(IConfiguration configuration, IAccessControl access, IAuditTrail audit, IOnDemandInterventionRecorder? interventions = null) : IActivityReviewService
{
    // The administrator (Setari -> Preluare ActivityApp) or an operator who holds the permission to validate tickets (Mentenanta -> Validare tichete ActivityApp).
    private static readonly string[] ViewKeys = ["setari-import-activityapp.view", "mentenanta.validare"];
    private static readonly string[] DecideKeys = ["setari-import-activityapp.run", "mentenanta.validare"];

    public async Task<IReadOnlyList<ActivityReviewItem>> GetPendingAsync(CancellationToken cancellationToken = default)
    {
        await access.EnsureAnyAsync(ViewKeys, cancellationToken).ConfigureAwait(false);
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

    private sealed record Row(long Id, long TicketId, int BeneficiaryId, int WorkPointId, DateOnly PerformedOn, string Keyword, int? InterventionId, string Status, string Kind);

    private async Task<Row?> FindAsync(long id, CancellationToken cancellationToken)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("SELECT id, ticket_id, beneficiary_id, work_point_id, performed_on, keyword, intervention_id, status, kind FROM activity_import_suggestions WHERE id=@id", connection);
        command.Parameters.AddWithValue("@id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new Row(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt32(2), reader.GetInt32(3), DateOnly.FromDateTime(reader.GetDateTime(4)), reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetInt32(6), reader.GetString(7), reader.GetString(8))
            : null;
    }

    private async Task ExecuteAsync(string sql, Action<MySqlParameterCollection> bind, CancellationToken cancellationToken)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand(sql, connection);
        bind(command.Parameters);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ActivityReviewDetail> GetDetailAsync(long id, CancellationToken cancellationToken = default)
    {
        await access.EnsureAnyAsync(ViewKeys, cancellationToken).ConfigureAwait(false);
        var item = (await ReadPendingAsync(configuration, cancellationToken).ConfigureAwait(false)).FirstOrDefault(entry => entry.Id == id)
            ?? throw new ActivityImportException("Tichetul nu mai așteaptă validarea (a fost decis între timp). Actualizează lista.");
        var row = await FindAsync(id, cancellationToken).ConfigureAwait(false) ?? throw new ActivityImportException("Tichetul nu mai așteaptă validarea. Actualizează lista.");
        var stored = await ActivityTicketStore.FindAsync(configuration, row.TicketId, cancellationToken).ConfigureAwait(false);
        var source = stored is null ? new TicketSource(row.TicketId, 0, 0, item.TicketNumber, item.Title, "", item.Resolution, item.Resolution, "") : ActivityTicketPlanner.SourceOf(stored.Ticket);
        var points = new List<ReviewWorkPoint>();
        await using (var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false))
        await using (var command = new MySqlCommand("SELECT id, name, address FROM beneficiary_work_points WHERE beneficiary_id=@id ORDER BY is_primary DESC, name", connection))
        {
            command.Parameters.AddWithValue("@id", row.BeneficiaryId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) points.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2)));
        }
        var covered = false;
        try { covered = interventions is not null && await interventions.IsCoveredAsync(row.BeneficiaryId, row.WorkPointId, cancellationToken).ConfigureAwait(false); }
        catch (ModuleUnavailableException) { }
        var keyword = item.Keyword;
        return new ActivityReviewDetail(item, row.BeneficiaryId, row.WorkPointId, source, points, covered,
            ActivityTicketPlanner.Notes(source.Number, source.Title, source.Problem, source.Resolution, source.Executor, keyword, false),
            ActivityTicketPlanner.Notes(source.Number, source.Title, source.Problem, source.Resolution, source.Executor, keyword, true, !covered),
            item.Kind == ActivityTicketPlanner.ServiceKind ? ReviewCategory.Service : ReviewCategory.Maintenance, row.InterventionId is not null);
    }

    // "Preluare" as it is: the category chosen, everything else as the ticket proposes it. A discard asks nothing more.
    public async Task DecideAsync(long id, ReviewDecision decision, CancellationToken cancellationToken = default)
    {
        if (decision == ReviewDecision.Discard) { await DiscardAsync(id, cancellationToken).ConfigureAwait(false); return; }
        var detail = await GetDetailAsync(id, cancellationToken).ConfigureAwait(false);
        var category = decision == ReviewDecision.Maintenance ? ReviewCategory.Maintenance : ReviewCategory.Service;
        await TakeAsync(id, new ReviewTakeInput(category, detail.WorkPointId, detail.Item.PerformedOn, category == ReviewCategory.Maintenance ? detail.NotesMaintenance : detail.NotesService), cancellationToken).ConfigureAwait(false);
    }

    public async Task TakeAsync(long id, ReviewTakeInput input, CancellationToken cancellationToken = default)
    {
        await access.EnsureAnyAsync(DecideKeys, cancellationToken).ConfigureAwait(false);
        if (!MariaDatabaseGuard.IsAllowedDatabase(configuration)) throw new ActivityImportException("Modificările sunt permise numai în baza BlazorStoc. Verifică numele bazei configurate.");
        if (interventions is null) throw new ActivityImportException("Modulul Mentenanță nu este disponibil.");
        var actor = (await RepositoryAudit.ActorAsync(access, cancellationToken).ConfigureAwait(false)).Username;
        var item = (await ReadPendingAsync(configuration, cancellationToken).ConfigureAwait(false)).FirstOrDefault(entry => entry.Id == id);
        var row = await FindAsync(id, cancellationToken).ConfigureAwait(false);
        if (item is null || row is null || row.Status != ActivityReviewStore.Pending) throw new ActivityImportException("Tichetul nu mai așteaptă validarea (a fost decis între timp). Actualizează lista.");
        var notes = TextNormalization.ForStorage((input.Notes ?? "").Replace("\r\n", "\n"));
        if (notes.Length > OnDemandInterventionRules.NotesMaximumLength) throw new ActivityImportException($"Observațiile pot avea cel mult {OnDemandInterventionRules.NotesMaximumLength} de caractere.");
        var maintenance = input.Category == ReviewCategory.Maintenance;
        var edited = input.WorkPointId != row.WorkPointId || input.PerformedOn != row.PerformedOn;
        int interventionId;
        try
        {
            if (row.InterventionId is { } existing)
            {
                // A ticket proposed before the approval queue existed already has its on-demand intervention: the maintenance replaces it, the service keeps it.
                interventionId = maintenance ? await ConvertToMaintenanceAsync(row, item, existing, cancellationToken).ConfigureAwait(false) : existing;
            }
            else
            {
                interventionId = maintenance
                    ? await interventions.RecordMaintenanceUncoveredAsync(row.BeneficiaryId, input.WorkPointId, input.PerformedOn, notes, cancellationToken).ConfigureAwait(false)
                    : await interventions.RecordAsync(row.BeneficiaryId, input.WorkPointId, input.PerformedOn, notes, cancellationToken).ConfigureAwait(false);
                var stored = await ActivityTicketStore.FindAsync(configuration, row.TicketId, cancellationToken).ConfigureAwait(false);
                if (stored is not null)
                {
                    await WriteSourceAsync(interventionId, ActivityTicketPlanner.SourceOf(stored.Ticket), cancellationToken).ConfigureAwait(false);
                    await ActivityTicketStore.SaveAsync(configuration, stored.Ticket, TicketStates.Imported, interventionId, cancellationToken).ConfigureAwait(false);
                    await ImportStore.WriteMapAsync(configuration, ActivityTicketPlanner.TicketEntity, row.TicketId, interventionId, "created", ActivityTicketPlanner.HashOf(stored.Ticket), cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (ModuleUnavailableException) { throw new ActivityImportException("Modulul Mentenanță este oprit: tichetul se poate prelua după ce pornește."); }
        catch (OnDemandInterventionOperationException exception) { throw new ActivityImportException(exception.Message); }
        await ExecuteAsync("UPDATE activity_import_suggestions SET status=@status, decided_by=@by, decided_utc=@now, intervention_id=@intervention WHERE id=@id AND status='pending'", p =>
        {
            p.AddWithValue("@status", maintenance ? ActivityReviewStore.Confirmed : ActivityReviewStore.Rejected);
            p.AddWithValue("@by", actor);
            p.AddWithValue("@now", DateTime.UtcNow);
            p.AddWithValue("@intervention", interventionId);
            p.AddWithValue("@id", id);
        }, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.ActivityImport, AuditActions.DecideActivityMaintenance, id.ToString(),
            maintenance ? "Preluare tichet ActivityApp ca mentenanță" : "Preluare tichet ActivityApp ca service la cerere",
            AuditDetails.Identification(("Tichet", $"{item.TicketNumber} {item.Title}".Trim()), ("Beneficiar", item.Beneficiary.Length > 0 ? item.Beneficiary : "—"), ("Propus ca", item.Kind == ActivityTicketPlanner.ServiceKind ? "service" : "mentenanță"),
                ("Decizie", maintenance ? "este mentenanță (intervenția devine mentenanță)" : "este service la cerere"), ("Modificat de operator", edited || notes.Length == 0 ? "da" : "nu")), "", cancellationToken).ConfigureAwait(false);
    }

    public async Task DiscardAsync(long id, CancellationToken cancellationToken = default)
    {
        await access.EnsureAnyAsync(DecideKeys, cancellationToken).ConfigureAwait(false);
        if (!MariaDatabaseGuard.IsAllowedDatabase(configuration)) throw new ActivityImportException("Modificările sunt permise numai în baza BlazorStoc. Verifică numele bazei configurate.");
        var actor = (await RepositoryAudit.ActorAsync(access, cancellationToken).ConfigureAwait(false)).Username;
        var item = (await ReadPendingAsync(configuration, cancellationToken).ConfigureAwait(false)).FirstOrDefault(entry => entry.Id == id);
        var row = await FindAsync(id, cancellationToken).ConfigureAwait(false);
        if (item is null || row is null || row.Status != ActivityReviewStore.Pending) throw new ActivityImportException("Tichetul nu mai așteaptă validarea (a fost decis între timp). Actualizează lista.");
        try
        {
            if (row.InterventionId is { } created && interventions is not null) await interventions.DeleteAsync(row.BeneficiaryId, created, "Discard tichet ActivityApp.", cancellationToken).ConfigureAwait(false);
        }
        catch (ModuleUnavailableException) { throw new ActivityImportException("Modulul Mentenanță este oprit: decizia se poate lua după ce pornește."); }
        catch (OnDemandInterventionOperationException exception) { throw new ActivityImportException(exception.Message); }
        await ExecuteAsync("DELETE FROM service_intervention_source WHERE source_ticket_id=@ticket", p => p.AddWithValue("@ticket", row.TicketId), cancellationToken).ConfigureAwait(false);
        await ExecuteAsync("DELETE FROM activity_import_map WHERE entity='ticket' AND source_id=@ticket", p => p.AddWithValue("@ticket", row.TicketId), cancellationToken).ConfigureAwait(false);
        await ActivityTicketStore.DiscardAsync(configuration, row.TicketId, actor, cancellationToken).ConfigureAwait(false);
        await ExecuteAsync("UPDATE activity_import_suggestions SET status=@status, decided_by=@by, decided_utc=@now WHERE id=@id AND status='pending'", p =>
        {
            p.AddWithValue("@status", ActivityReviewStore.Discarded);
            p.AddWithValue("@by", actor);
            p.AddWithValue("@now", DateTime.UtcNow);
            p.AddWithValue("@id", id);
        }, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.ActivityImport, AuditActions.DiscardActivityTicket, id.ToString(), "Discard tichet ActivityApp",
            AuditDetails.Identification(("Tichet", $"{item.TicketNumber} {item.Title}".Trim()), ("Beneficiar", item.Beneficiary.Length > 0 ? item.Beneficiary : "—"), ("Propus ca", item.Kind == ActivityTicketPlanner.ServiceKind ? "service" : "mentenanță"),
                ("Decizie", "discard (tichetul nu se mai preia)")), "", cancellationToken).ConfigureAwait(false);
    }

    // A ticket proposed before the queue (its on-demand intervention is already in the register) is confirmed as maintenance: the maintenance is recorded (the next due date moves by the cycle of
    // the contract when there is one), the ticket is tied to it, and the on-demand one is deleted.
    private async Task<int> ConvertToMaintenanceAsync(Row row, ActivityReviewItem item, int onDemandId, CancellationToken cancellationToken)
    {
        var stored = await ActivityTicketStore.FindAsync(configuration, row.TicketId, cancellationToken).ConfigureAwait(false);
        var (problem, resolution) = stored is null ? ("", item.Resolution) : ActivityTicketPlanner.SplitReport(stored.Ticket.Problem, stored.Ticket.Report);
        var covered = await interventions!.IsCoveredAsync(row.BeneficiaryId, row.WorkPointId, cancellationToken).ConfigureAwait(false);
        var notes = ActivityTicketPlanner.Notes(item.TicketNumber, item.Title, problem, resolution, stored?.Ticket.Executor ?? "", row.Keyword, true, !covered);
        var maintenanceId = await interventions.RecordMaintenanceUncoveredAsync(row.BeneficiaryId, row.WorkPointId, row.PerformedOn, notes, cancellationToken).ConfigureAwait(false);
        await ExecuteAsync("UPDATE service_intervention_source SET intervention_id=@new WHERE intervention_id=@old", p => { p.AddWithValue("@new", maintenanceId); p.AddWithValue("@old", onDemandId); }, cancellationToken).ConfigureAwait(false);
        await ExecuteAsync("UPDATE activity_import_map SET local_id=@new WHERE entity='ticket' AND source_id=@ticket", p => { p.AddWithValue("@new", maintenanceId); p.AddWithValue("@ticket", row.TicketId); }, cancellationToken).ConfigureAwait(false);
        await ExecuteAsync("UPDATE activity_import_tickets SET intervention_id=@new, updated_utc=@now WHERE ticket_id=@ticket", p => { p.AddWithValue("@new", maintenanceId); p.AddWithValue("@now", DateTime.UtcNow); p.AddWithValue("@ticket", row.TicketId); }, cancellationToken).ConfigureAwait(false);
        await interventions.DeleteAsync(row.BeneficiaryId, onDemandId, "Înlocuită cu mentenanța validată de operator (tichet ActivityApp).", cancellationToken).ConfigureAwait(false);
        return maintenanceId;
    }

    // The detail of the source of the intervention: the problem, the resolution, the original report and the executor, kept apart from the notes.
    private async Task WriteSourceAsync(int interventionId, TicketSource source, CancellationToken cancellationToken)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("""
            INSERT INTO service_intervention_source (intervention_id, source_ticket_id, source_type_id, source_event_id, ticket_number, title, problem, resolution, report, executor, imported_utc)
            VALUES (@intervention, @ticket, @type, @event, @number, @title, @problem, @resolution, @report, @executor, @now)
            ON DUPLICATE KEY UPDATE intervention_id=@intervention, source_type_id=@type, source_event_id=@event, ticket_number=@number, title=@title, problem=@problem, resolution=@resolution,
                report=@report, executor=@executor, imported_utc=@now
            """, connection);
        command.Parameters.AddWithValue("@intervention", interventionId);
        command.Parameters.AddWithValue("@ticket", source.TicketId);
        command.Parameters.AddWithValue("@type", source.TypeId);
        command.Parameters.AddWithValue("@event", source.EventId);
        command.Parameters.AddWithValue("@number", Cut(source.Number, 100));
        command.Parameters.AddWithValue("@title", Cut(source.Title, 500));
        command.Parameters.AddWithValue("@problem", source.Problem);
        command.Parameters.AddWithValue("@resolution", source.Resolution);
        command.Parameters.AddWithValue("@report", source.Report);
        command.Parameters.AddWithValue("@executor", Cut(source.Executor, 200));
        command.Parameters.AddWithValue("@now", DateTime.UtcNow);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string Cut(string value, int length) => value.Length > length ? value[..length] : value;

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

/// <summary>The notification for the events waiting for validation (administrator and operators with the permission to validate tickets): ONE aggregated instance, on the day of the oldest event; it goes away by itself when the last one is decided.</summary>
public sealed class ActivityReviewSource(IConfiguration configuration, string key = ActivityReviewSource.SourceKey) : IExpirySource
{
    public const string SourceKey = "sistem.mentenanta-activityapp-de-validat";
    public const string TicketName = "tichet de validat";
    public const string DetailsName = "detalii validare";
    public string Key => key;
    public string Category => "Sistem";
    public string EventName => "Evenimente ActivityApp de validat ca mentenanță";
    public string DateLabel => "constatării";
    public int DefaultThresholdDays => 0;
    public string RemovedReason => "Evenimentul a fost validat, respins sau discardat.";
    public string DefaultSubject => "Validează mentenanța: <tichet de validat>";
    public string DefaultBody => "<detalii validare> Vezi Mentenanță, secțiunea „Evenimente de validat”.";
    public IReadOnlyList<ExpiryPlaceholder> Placeholders { get; } =
    [
        new(TicketName, "Tichetul din ActivityApp care așteaptă validarea (sau numărul lor, dacă sunt mai multe)", "3 tichete"),
        new(DetailsName, "Câte evenimente așteaptă și de unde vin (sau, pentru unul singur, ce s-a găsit în tichet și la ce beneficiar)", "3 evenimente ActivityApp așteaptă validarea (1 de tip mentenanță, 2 cu cuvânt cheie în text); cel mai vechi este din 12.03.2026.")
    ];

    public string DateChangedReason(DateOnly from, DateOnly to, ExpiryInstance current) =>
        $"Cel mai vechi eveniment de validat s-a mutat de la {StockMovementRules.DisplayDate(from)} la {StockMovementRules.DisplayDate(to)}.";

    /// <summary>The URL of the notification: the validation list in the maintenance page (an operator with the permission to validate tickets has no access to the settings).</summary>
    public const string Url = "/mentenanta?tab=validare";
    public const int AggregateId = 1;

    /// <summary>One notification for every event waiting (not one per ticket): it keeps the day of the oldest one and goes away when the last is decided.</summary>
    public static IReadOnlyList<ExpiryInstance> Instances(IReadOnlyList<ActivityReviewItem> items)
    {
        if (items.Count == 0) return [];
        var oldest = items.Min(item => item.CreatedUtc);
        var oldestDay = DateOnly.FromDateTime(oldest.ToLocalTime());
        var service = items.Count(item => item.Kind == ActivityTicketPlanner.ServiceKind);
        var single = items[0];
        var ticket = items.Count == 1 ? $"{single.TicketNumber} {single.Title}".Trim() : $"{items.Count} tichete";
        var details = items.Count == 1
            ? single.Kind switch
            {
                ActivityTicketPlanner.ServiceKind => $"La beneficiarul {(single.Beneficiary.Length > 0 ? single.Beneficiary : "—")}, un tichet de service așteaptă validarea: preia-l ca service sau ca mentenanță.",
                "type" => $"La beneficiarul {(single.Beneficiary.Length > 0 ? single.Beneficiary : "—")}, tichetul este de un tip de mentenanță: verifică dacă mentenanța s-a făcut cu adevărat.",
                "exact" => $"La beneficiarul {(single.Beneficiary.Length > 0 ? single.Beneficiary : "—")}, în tichet scrie «{single.Found}»: verifică dacă mentenanța s-a făcut cu adevărat.",
                _ => $"La beneficiarul {(single.Beneficiary.Length > 0 ? single.Beneficiary : "—")}, în tichet scrie «{single.Found}» (seamănă cu «{single.Keyword}»): verifică dacă este mentenanță."
            }
            : $"{items.Count} evenimente ActivityApp așteaptă validarea ({items.Count - service} propuse ca mentenanță, {service} ca service); cel mai vechi este din {StockMovementRules.DisplayDate(oldestDay)}.";
        return [new ExpiryInstance(AggregateId, items.Count == 1 ? $"Tichet {ticket}" : $"{items.Count} evenimente ActivityApp de validat", oldestDay,
            new Dictionary<string, string> { [TicketName] = ticket, [DetailsName] = details }, Url)];
    }

    public async Task<IReadOnlyList<ExpiryInstance>> GetInstancesAsync(CancellationToken cancellationToken = default) =>
        Instances(await MariaActivityReviewService.ReadPendingAsync(configuration, cancellationToken).ConfigureAwait(false));
}

internal sealed class GuardedActivityReviewService(IActivityReviewService inner, ModuleRegistry modules) : IActivityReviewService
{
    private const string Id = ActivityImportModule.ModuleId;
    public Task<IReadOnlyList<ActivityReviewItem>> GetPendingAsync(CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.GetPendingAsync(cancellationToken));
    public Task<ActivityReviewDetail> GetDetailAsync(long id, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.GetDetailAsync(id, cancellationToken));
    public Task DecideAsync(long id, ReviewDecision decision, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.DecideAsync(id, decision, cancellationToken));
    public Task TakeAsync(long id, ReviewTakeInput input, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.TakeAsync(id, input, cancellationToken));
    public Task<IReadOnlyList<ActivityDiscardedTicket>> GetDiscardedAsync(CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.GetDiscardedAsync(cancellationToken));
    public Task ReactivateAsync(long ticketId, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.ReactivateAsync(ticketId, cancellationToken));
}

internal sealed class UnavailableActivityReviewService : IActivityReviewService
{
    public Task<IReadOnlyList<ActivityReviewItem>> GetPendingAsync(CancellationToken cancellationToken = default) => Task.FromException<IReadOnlyList<ActivityReviewItem>>(new ModuleUnavailableException(ActivityImportModule.ModuleId));
    public Task<ActivityReviewDetail> GetDetailAsync(long id, CancellationToken cancellationToken = default) => Task.FromException<ActivityReviewDetail>(new ModuleUnavailableException(ActivityImportModule.ModuleId));
    public Task DecideAsync(long id, ReviewDecision decision, CancellationToken cancellationToken = default) => Task.FromException(new ModuleUnavailableException(ActivityImportModule.ModuleId));
    public Task TakeAsync(long id, ReviewTakeInput input, CancellationToken cancellationToken = default) => Task.FromException(new ModuleUnavailableException(ActivityImportModule.ModuleId));
    public Task<IReadOnlyList<ActivityDiscardedTicket>> GetDiscardedAsync(CancellationToken cancellationToken = default) => Task.FromException<IReadOnlyList<ActivityDiscardedTicket>>(new ModuleUnavailableException(ActivityImportModule.ModuleId));
    public Task ReactivateAsync(long ticketId, CancellationToken cancellationToken = default) => Task.FromException(new ModuleUnavailableException(ActivityImportModule.ModuleId));
}
