using BlazorStoc.Services;
using MySqlConnector;

namespace BlazorStoc.Modules.ActivityImport;

// What the operator decided for a ticket (maintenance or service on demand) is kept here (activity_ticket_outcomes) and the ticket is finished as far as the import is concerned: it leaves the
// queue and is never proposed again, whether or not the maintenance module is running. The intervention (and with it the due dates of the contract) belongs to the maintenance module: it
// records the pending outcomes in the order of the dates performed - at once when the module is running, at the first run after it was switched on again otherwise (TicketOutcomeWorker in
// Modules/Maintenance). The import never needs the maintenance module to take a ticket.

/// <summary>A decided ticket whose intervention is recorded (or still to be recorded) in the maintenance register.</summary>
public sealed record TicketOutcome(long Id, long TicketId, int BeneficiaryId, int WorkPointId, bool Maintenance, DateOnly PerformedOn, string Notes, IReadOnlyList<int> ComponentIds, string DecidedBy);

/// <summary>How many outcomes wait for the maintenance register and how many it refused.</summary>
public sealed record TicketOutcomeCounts(int Pending, int Failed);

/// <summary>The state of the outcome of one ticket.</summary>
public sealed record TicketOutcomeState(string Status, int? InterventionId, string Error);

public static class TicketOutcomeStates
{
    public const string Pending = "pending", Applying = "applying", Applied = "applied", Failed = "failed";
}

public static class ActivityTicketOutcomeStore
{
    // A claim not finished after this long (the application stopped in the middle) is taken again.
    private static readonly TimeSpan ClaimTimeout = TimeSpan.FromMinutes(10);

    public static async Task AddAsync(IConfiguration configuration, TicketOutcome outcome, CancellationToken cancellationToken)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("""
            INSERT INTO activity_ticket_outcomes (ticket_id, beneficiary_id, work_point_id, category, performed_on, notes, component_ids, status, decided_by, decided_utc)
            VALUES (@ticket, @beneficiary, @point, @category, @performed, @notes, @components, 'pending', @by, @now)
            ON DUPLICATE KEY UPDATE beneficiary_id=@beneficiary, work_point_id=@point, category=@category, performed_on=@performed, notes=@notes, component_ids=@components,
                status=IF(status='applied', status, 'pending'), error='', decided_by=@by, decided_utc=@now
            """, connection);
        command.Parameters.AddWithValue("@ticket", outcome.TicketId);
        command.Parameters.AddWithValue("@beneficiary", outcome.BeneficiaryId);
        command.Parameters.AddWithValue("@point", outcome.WorkPointId);
        command.Parameters.AddWithValue("@category", outcome.Maintenance ? "M" : "S");
        command.Parameters.AddWithValue("@performed", outcome.PerformedOn.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("@notes", outcome.Notes);
        command.Parameters.AddWithValue("@components", string.Join(',', outcome.ComponentIds));
        command.Parameters.AddWithValue("@by", outcome.DecidedBy);
        command.Parameters.AddWithValue("@now", DateTime.UtcNow);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<TicketOutcomeState?> FindAsync(IConfiguration configuration, long ticketId, CancellationToken cancellationToken)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("SELECT status, intervention_id, error FROM activity_ticket_outcomes WHERE ticket_id=@ticket", connection);
        command.Parameters.AddWithValue("@ticket", ticketId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? new(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetInt32(1), reader.GetString(2)) : null;
    }

    public static async Task<TicketOutcomeCounts> CountAsync(IConfiguration configuration, CancellationToken cancellationToken)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("SELECT COALESCE(SUM(status IN ('pending','applying')), 0), COALESCE(SUM(status='failed'), 0) FROM activity_ticket_outcomes", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        return new((int)reader.GetDecimal(0), (int)reader.GetDecimal(1));
    }

    /// <summary>The outcomes to record, in the order of the dates performed (the register moves a due date only for the latest maintenance of a point, so the order of the dates is the right one).</summary>
    internal static async Task<IReadOnlyList<TicketOutcome>> ReadPendingAsync(IConfiguration configuration, CancellationToken cancellationToken)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("""
            SELECT id, ticket_id, beneficiary_id, work_point_id, category, performed_on, notes, component_ids, decided_by
            FROM activity_ticket_outcomes
            WHERE status='pending' OR (status='applying' AND claimed_utc < @stale)
            ORDER BY performed_on, id
            """, connection);
        command.Parameters.AddWithValue("@stale", DateTime.UtcNow - ClaimTimeout);
        var items = new List<TicketOutcome>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            items.Add(new(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetString(4) == "M", DateOnly.FromDateTime(reader.GetDateTime(5)), reader.GetString(6),
                [.. reader.GetString(7).Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse)], reader.GetString(8)));
        return items;
    }

    internal static async Task<bool> ClaimAsync(IConfiguration configuration, long id, CancellationToken cancellationToken)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("UPDATE activity_ticket_outcomes SET status='applying', claimed_utc=@now WHERE id=@id AND (status='pending' OR (status='applying' AND claimed_utc < @stale))", connection);
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@now", DateTime.UtcNow);
        command.Parameters.AddWithValue("@stale", DateTime.UtcNow - ClaimTimeout);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    internal static async Task SetAsync(IConfiguration configuration, long id, string status, int? interventionId, string error, CancellationToken cancellationToken)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("UPDATE activity_ticket_outcomes SET status=@status, intervention_id=COALESCE(@intervention, intervention_id), error=@error, claimed_utc=NULL, applied_utc=IF(@status='applied', @now, applied_utc) WHERE id=@id", connection);
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@status", status);
        command.Parameters.AddWithValue("@intervention", (object?)interventionId ?? DBNull.Value);
        command.Parameters.AddWithValue("@error", error.Length > 500 ? error[..500] : error);
        command.Parameters.AddWithValue("@now", DateTime.UtcNow);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Records the pending outcomes in the maintenance register through the recorder it is given (the signed-in user's, or the system's in the background).</summary>
public static class TicketOutcomeApplication
{
    /// <summary>Applies every pending outcome in the order of the dates performed; returns how many were recorded. While the maintenance module is out (<see cref="ModuleUnavailableException"/>) it stops and
    /// leaves the rest pending; a refusal of the register marks only that outcome as failed (with the reason) and goes on.</summary>
    public static async Task<int> ApplyPendingAsync(IConfiguration configuration, IOnDemandInterventionRecorder recorder, CancellationToken cancellationToken)
    {
        var applied = 0;
        foreach (var outcome in await ActivityTicketOutcomeStore.ReadPendingAsync(configuration, cancellationToken).ConfigureAwait(false))
        {
            if (!await ActivityTicketOutcomeStore.ClaimAsync(configuration, outcome.Id, cancellationToken).ConfigureAwait(false)) continue;
            int interventionId;
            try
            {
                interventionId = outcome.Maintenance
                    ? await recorder.RecordMaintenanceUncoveredAsync(outcome.BeneficiaryId, outcome.WorkPointId, outcome.PerformedOn, outcome.Notes, outcome.ComponentIds, cancellationToken).ConfigureAwait(false)
                    : await recorder.RecordAsync(outcome.BeneficiaryId, outcome.WorkPointId, outcome.PerformedOn, outcome.Notes, cancellationToken).ConfigureAwait(false);
            }
            catch (ModuleUnavailableException)
            {
                await ActivityTicketOutcomeStore.SetAsync(configuration, outcome.Id, TicketOutcomeStates.Pending, null, "", CancellationToken.None).ConfigureAwait(false);
                return applied;
            }
            catch (OnDemandInterventionOperationException exception)
            {
                await ActivityTicketOutcomeStore.SetAsync(configuration, outcome.Id, TicketOutcomeStates.Failed, null, exception.Message, CancellationToken.None).ConfigureAwait(false);
                continue;
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                await ActivityTicketOutcomeStore.SetAsync(configuration, outcome.Id, TicketOutcomeStates.Pending, null, "", CancellationToken.None).ConfigureAwait(false);
                throw;
            }
            await FinishAsync(configuration, outcome, interventionId, cancellationToken).ConfigureAwait(false);
            await ActivityTicketOutcomeStore.SetAsync(configuration, outcome.Id, TicketOutcomeStates.Applied, interventionId, "", CancellationToken.None).ConfigureAwait(false);
            applied++;
        }
        return applied;
    }

    // The ticket is tied to its intervention: the record of what it was taken from, the link kept with the ticket and the local id of the map.
    private static async Task FinishAsync(IConfiguration configuration, TicketOutcome outcome, int interventionId, CancellationToken cancellationToken)
    {
        var stored = await ActivityTicketStore.FindAsync(configuration, outcome.TicketId, cancellationToken).ConfigureAwait(false);
        if (stored is not null)
        {
            var map = await ActivityFieldSettingsStore.ReadAsync(configuration, cancellationToken).ConfigureAwait(false);
            await ActivityTicketSourceStore.WriteAsync(configuration, interventionId, stored.Ticket, map, cancellationToken).ConfigureAwait(false);
            await ActivityTicketStore.SaveAsync(configuration, stored.Ticket, TicketStates.Imported, interventionId, cancellationToken).ConfigureAwait(false);
        }
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("UPDATE activity_import_map SET local_id=@intervention WHERE entity='ticket' AND source_id=@ticket", connection);
        command.Parameters.AddWithValue("@intervention", interventionId);
        command.Parameters.AddWithValue("@ticket", outcome.TicketId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>The processor the maintenance module runs (see <see cref="ITicketOutcomeProcessor"/>).</summary>
internal sealed class TicketOutcomeProcessor(IConfiguration configuration) : ITicketOutcomeProcessor
{
    public Task<int> ApplyPendingAsync(IOnDemandInterventionRecorder recorder, CancellationToken cancellationToken = default) =>
        TicketOutcomeApplication.ApplyPendingAsync(configuration, recorder, cancellationToken);
}
