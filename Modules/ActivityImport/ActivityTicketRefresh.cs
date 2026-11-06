using BlazorStoc.Services;
using MySqlConnector;

namespace BlazorStoc.Modules.ActivityImport;

// Setari -> Preluare ActivityApp, sectiunea 6: when the settings of the fields ask for something the tickets already kept do not hold (a piece switched on, an extra field added), those tickets
// are read again from ActivityApp (by their ids), the copy kept is renewed and, for the tickets that were taken, the source record of the intervention is written again. The notes of an
// intervention are rewritten only when they still are exactly what the import wrote (the lines of the ticket and, after them, the remark about maintenance): a text an operator edited is
// left as it is and counted. What a ticket held that is now switched off is not deleted, only hidden.

/// <summary>What a refresh did. NotesEdited: notes an operator changed (or that cannot be recognised), left as they were.</summary>
public sealed record TicketRefreshReport(int Outdated, int Updated, int NotFound, int SourceRecords, int NotesUpdated, int NotesEdited, IReadOnlyList<string> Problems);

public interface IActivityTicketRefreshService
{
    /// <summary>How many tickets kept lack something the settings of the fields ask for (not counting the ones ActivityApp no longer has).</summary>
    Task<int> CountOutdatedAsync(CancellationToken cancellationToken = default);
    /// <summary>Reads again the tickets that lack something and renews them (and the source records and notes of the interventions taken). Needs the right to run the import.</summary>
    Task<TicketRefreshReport> RefreshAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default);
}

public sealed class MariaActivityTicketRefreshService(IConfiguration configuration, IActivityImportService import, IActivitySource source, IAccessControl access, IAuditTrail audit,
    IOnDemandInterventionRecorder? interventions = null) : IActivityTicketRefreshService
{
    private const string View = "setari-import-activityapp.view", Run = "setari-import-activityapp.run", Gone = "gone";

    private sealed record Row(long TicketId, string State, string TicketRead, int? InterventionId, string? SourceRead, string NotesItems, int? BeneficiaryId, string NotesLabels = "");

    private static bool IsGone(string read) => ActivityFieldMap.Split(read).Contains(Gone);

    // Lacks something it can still get: it was read before the settings asked for it and ActivityApp still has it.
    private static bool TicketLacks(ActivityFieldMap map, Row row) => !IsGone(row.TicketRead) && ActivityTicketFields.Missing(map, row.TicketRead).Count > 0;

    // The source record lacks something while the ticket kept has it (or is gone for good): the record is written again from the copy kept.
    private static bool SourceLacks(ActivityFieldMap map, Row row) =>
        row.InterventionId is not null && row.BeneficiaryId is not null && row.SourceRead is not null && !IsGone(row.SourceRead) && ActivityTicketFields.Missing(map, row.SourceRead).Count > 0
        && (IsGone(row.TicketRead) || ActivityTicketFields.Missing(map, row.TicketRead).Count == 0);

    private async Task<IReadOnlyList<Row>> ReadRowsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("""
            SELECT t.ticket_id, t.state, t.fields_read, s.intervention_id, s.fields_read, s.notes_items, i.beneficiary_id, s.notes_labels
            FROM activity_import_tickets t
            LEFT JOIN service_intervention_source s ON s.source_ticket_id = t.ticket_id
            LEFT JOIN service_interventions i ON i.id = s.intervention_id
            ORDER BY t.ticket_id
            """, connection);
        var rows = new List<Row>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            rows.Add(new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : Convert.ToInt32(reader.GetValue(3)), reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? "" : reader.GetString(5), reader.IsDBNull(6) ? null : Convert.ToInt32(reader.GetValue(6)), reader.IsDBNull(7) ? "" : reader.GetString(7)));
        return rows;
    }

    public async Task<int> CountOutdatedAsync(CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync(View, cancellationToken).ConfigureAwait(false);
        var map = await ActivityFieldSettingsStore.ReadAsync(configuration, cancellationToken).ConfigureAwait(false);
        return (await ReadRowsAsync(cancellationToken).ConfigureAwait(false)).Count(row => TicketLacks(map, row) || SourceLacks(map, row));
    }

    public async Task<TicketRefreshReport> RefreshAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync(Run, cancellationToken).ConfigureAwait(false);
        if (!MariaDatabaseGuard.IsAllowedDatabase(configuration)) throw new ActivityImportException("Modificările sunt permise numai în baza BlazorStoc. Verifică numele bazei configurate.");
        if (!await ImportStore.Gate.WaitAsync(0, cancellationToken).ConfigureAwait(false)) throw new ActivityImportException("O altă rulare a importului este în desfășurare. Așteaptă să se termine.");
        try
        {
            var map = await ActivityFieldSettingsStore.ReadAsync(configuration, cancellationToken).ConfigureAwait(false);
            var rows = await ReadRowsAsync(cancellationToken).ConfigureAwait(false);
            var targets = rows.Where(row => TicketLacks(map, row) || SourceLacks(map, row)).ToList();
            if (targets.Count == 0) return new TicketRefreshReport(0, 0, 0, 0, 0, 0, []);
            // Read again what lacks something, in one go.
            var fresh = new Dictionary<long, ActivityTicket>();
            var needRead = targets.Where(row => TicketLacks(map, row)).Select(row => row.TicketId).ToList();
            if (needRead.Count > 0)
            {
                progress?.Report($"Se citesc din ActivityApp {needRead.Count} tichete…");
                var connection = await import.GetConnectionAsync(cancellationToken).ConfigureAwait(false)
                    ?? throw new ActivityImportException("Conexiunea la ActivityApp nu este completată (server, cont, parolă). Salveaz-o în setări.");
                var settings = await import.GetSettingsAsync(cancellationToken).ConfigureAwait(false);
                var structure = ActivityStructureChecker.Check(await source.ReadStructureAsync(connection, cancellationToken).ConfigureAwait(false), null, map);
                foreach (var entity in new[] { ActivityContract.Tickets, ActivityContract.Events })
                    if (structure.IsBlocked(entity)) throw new ActivityImportException($"Structura ActivityApp s-a schimbat: „{entity}” nu poate fi citit (vezi raportul de structură din setări).");
                if (structure.Issues.FirstOrDefault(issue => issue.Problem.StartsWith("coloana aleasă", StringComparison.Ordinal)) is { } chosenIssue)
                    throw new ActivityImportException($"Corespondența câmpurilor: {chosenIssue.Problem} (tabela {chosenIssue.Table}). Alege altă coloană în setări, secțiunea 6.");
                foreach (var ticket in await source.ReadTicketsByIdAsync(connection, structure, needRead, settings.PageSize, cancellationToken).ConfigureAwait(false))
                    fresh[ticket.Id] = ActivityTicketFields.Collect(ticket, map);
            }
            int updated = 0, notFound = 0, records = 0, notesUpdated = 0, notesEdited = 0, done = 0;
            var problems = new List<string>();
            foreach (var row in targets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++done % 20 == 0) progress?.Report($"Tichete actualizate: {done} din {targets.Count}…");
                var before = await ActivityTicketStore.FindAsync(configuration, row.TicketId, cancellationToken).ConfigureAwait(false);
                if (before is null) continue;
                var current = before.Ticket;
                if (TicketLacks(map, row))
                {
                    if (fresh.TryGetValue(row.TicketId, out var renewed))
                    {
                        await ActivityTicketStore.SaveAsync(configuration, renewed, before.State, before.InterventionId, cancellationToken).ConfigureAwait(false);
                        current = renewed;
                        updated++;
                    }
                    else
                    {
                        // ActivityApp no longer has it (deleted there): what is kept stays, and it is not asked for again.
                        current = before.Ticket with { FieldsRead = before.Ticket.FieldsRead + "," + Gone };
                        await ActivityTicketStore.SaveAsync(configuration, current, before.State, before.InterventionId, cancellationToken).ConfigureAwait(false);
                        notFound++;
                    }
                }
                if (row.InterventionId is { } interventionId && row.BeneficiaryId is not null && row.SourceRead is not null)
                {
                    try
                    {
                        var result = await RenewInterventionAsync(row, interventionId, before.Ticket, current, map, cancellationToken).ConfigureAwait(false);
                        records++;
                        if (result == NotesResult.Updated) notesUpdated++;
                        else if (result == NotesResult.Edited) notesEdited++;
                    }
                    catch (Exception exception) when (exception is MySqlException or OnDemandInterventionOperationException or ModuleUnavailableException)
                    {
                        problems.Add($"Tichetul {current.Number}: {(exception is MySqlException ? "eroare la scrierea înregistrării sursei" : exception.Message)}");
                    }
                }
            }
            var report = new TicketRefreshReport(targets.Count, updated, notFound, records, notesUpdated, notesEdited, problems);
            await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.ActivityImport, AuditActions.RefreshActivityTickets, "", "Actualizare tichete ActivityApp preluate", AuditDetails.Identification(
                ("Tichete care lipseau de câmpuri", targets.Count.ToString()), ("Citite din nou", updated.ToString()), ("Negăsite în ActivityApp", notFound.ToString()), ("Înregistrări sursă rescrise", records.ToString()),
                ("Observații actualizate", notesUpdated.ToString()), ("Observații modificate de operator, lăsate neatinse", notesEdited.ToString()), ("Probleme", problems.Count.ToString())), "", cancellationToken).ConfigureAwait(false);
            return report;
        }
        finally { ImportStore.Gate.Release(); }
    }

    private enum NotesResult { Unchanged, Updated, Edited }

    // The source record is written again from the ticket kept (with the settings in force) and the notes follow when they are still the generated ones.
    private async Task<NotesResult> RenewInterventionAsync(Row row, int interventionId, ActivityTicket before, ActivityTicket current, ActivityFieldMap map, CancellationToken cancellationToken)
    {
        var newSource = ActivityTicketPlanner.SourceOf(current, map);
        var newPrefix = ActivityTicketPlanner.NotesPrefix(newSource.Number, newSource.Title, ActivityTicketPlanner.NoteLines(newSource, map).Select(line => (line.Label, line.Value)));
        var oldPrefix = OldPrefix(row, before, map);
        var result = NotesResult.Unchanged;
        var notesDone = oldPrefix is not null && string.Equals(TextNormalization.ForStorage(oldPrefix.Replace("\r\n", "\n")), TextNormalization.ForStorage(newPrefix.Replace("\r\n", "\n")), StringComparison.Ordinal);
        if (!notesDone && interventions is not null && row.BeneficiaryId is { } beneficiary)
        {
            var notes = await interventions.GetNotesAsync(beneficiary, interventionId, cancellationToken).ConfigureAwait(false);
            var rebuilt = notes is null || oldPrefix is null ? null : ActivityTicketPlanner.RebuildNotes(notes, oldPrefix, newPrefix);
            if (rebuilt is null) result = NotesResult.Edited;
            else if (rebuilt != notes && await interventions.UpdateNotesAsync(beneficiary, interventionId, notes!, rebuilt, cancellationToken).ConfigureAwait(false)) { result = NotesResult.Updated; notesDone = true; }
            else if (rebuilt != notes) result = NotesResult.Edited;
            else notesDone = true;
        }
        await ActivityTicketSourceStore.WriteAsync(configuration, interventionId, current, map, cancellationToken, updateNotesItems: notesDone).ConfigureAwait(false);
        return result;
    }

    // What the import wrote at the start of the notes, rebuilt from the copy kept before the refresh and the list of what went into the notes then (null when it cannot be rebuilt: an extra
    // field that is no longer in the settings has no name to look for).
    private static string? OldPrefix(Row row, ActivityTicket before, ActivityFieldMap map)
    {
        var raw = ActivityTicketPlanner.SourceOf(before);
        var lines = new List<(string, string)>();
        // The names written in front of the lines then (kept with the source record); without them the usual names.
        var written = new Dictionary<string, string>();
        if (row.NotesLabels.Length > 0) { try { written = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(row.NotesLabels) ?? written; } catch (System.Text.Json.JsonException) { } }
        string LabelOf(string key, string usual) => written.TryGetValue(key, out var kept) ? kept : usual;
        foreach (var key in row.NotesItems.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            (string Label, string Value)? line = key switch
            {
                ActivityFieldMap.Problem => (LabelOf(key, ActivityFieldMap.DefaultNoteLabel(key)), raw.Problem),
                ActivityFieldMap.Resolution => (LabelOf(key, ActivityFieldMap.DefaultNoteLabel(key)), raw.Resolution),
                ActivityFieldMap.Report => (LabelOf(key, ActivityFieldMap.DefaultNoteLabel(key)), raw.Report),
                ActivityFieldMap.Executor => (LabelOf(key, ActivityFieldMap.DefaultNoteLabel(key)), raw.Executor),
                _ => (written.TryGetValue(key, out var keptLabel) ? keptLabel : map.ExtraOf(key)?.Label) is { } extraLabel ? (extraLabel, raw.Extras is not null && raw.Extras.TryGetValue(key, out var value) ? value : "") : null
            };
            if (line is null) return null;
            if (line.Value.Value.Length > 0) lines.Add(line.Value);
        }
        return ActivityTicketPlanner.NotesPrefix(raw.Number, raw.Title, lines);
    }
}

internal sealed class GuardedActivityTicketRefreshService(IActivityTicketRefreshService inner, ModuleRegistry modules) : IActivityTicketRefreshService
{
    private const string Id = ActivityImportModule.ModuleId;
    public Task<int> CountOutdatedAsync(CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.CountOutdatedAsync(cancellationToken));
    public Task<TicketRefreshReport> RefreshAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.RefreshAsync(progress, cancellationToken));
}

internal sealed class UnavailableActivityTicketRefreshService : IActivityTicketRefreshService
{
    private static Task<T> Out<T>() => Task.FromException<T>(new ModuleUnavailableException(ActivityImportModule.ModuleId));
    public Task<int> CountOutdatedAsync(CancellationToken cancellationToken = default) => Out<int>();
    public Task<TicketRefreshReport> RefreshAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default) => Out<TicketRefreshReport>();
}
