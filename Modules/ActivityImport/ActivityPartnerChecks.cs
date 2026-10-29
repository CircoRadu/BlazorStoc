using BlazorStoc.Services;
using MySqlConnector;

namespace BlazorStoc.Modules.ActivityImport;

// The queue of the partners of ActivityApp that need a human look (migration 50): a company is taken even when ANAF cannot answer or does not know its CUI, or its CUI has the wrong form; it is
// then "to verify" and the tickets of its client wait (activity_import_tickets) until it is clarified. The queue is read on the Beneficiari page (through IBeneficiaryCheckService) and by the
// notification for the administrator. A partner whose check is discarded is never taken again, nor are its tickets.

public static class PartnerCheckStatus
{
    public const string Pending = "pending", Resolved = "resolved", Discarded = "discarded";
}

internal sealed record PartnerCheckRow(long Id, long PartnerId, int? BeneficiaryId, string Name, string Cui, string Reason, string Status, DateTime CreatedUtc, string Note = "");

internal static class ActivityPartnerCheckStore
{
    private const string Columns = "id, partner_id, beneficiary_id, name, cui_as_typed, reason, status, created_utc, note";

    private static PartnerCheckRow Read(MySqlDataReader reader) => new(reader.GetInt64(0), reader.GetInt64(1), reader.IsDBNull(2) ? null : reader.GetInt32(2), reader.GetString(3), reader.GetString(4),
        reader.GetString(5), reader.GetString(6), DateTime.SpecifyKind(reader.GetDateTime(7), DateTimeKind.Utc), reader.GetString(8));

    public static async Task<IReadOnlyList<PartnerCheckRow>> ReadAsync(IConfiguration configuration, string? status, CancellationToken cancellationToken)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand($"SELECT {Columns} FROM activity_import_partner_checks {(status is null ? "" : "WHERE status=@status")} ORDER BY created_utc, id", connection);
        if (status is not null) command.Parameters.AddWithValue("@status", status);
        var result = new List<PartnerCheckRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(Read(reader));
        return result;
    }

    public static async Task<PartnerCheckRow?> FindAsync(IConfiguration configuration, long id, CancellationToken cancellationToken)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand($"SELECT {Columns} FROM activity_import_partner_checks WHERE id=@id", connection);
        command.Parameters.AddWithValue("@id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? Read(reader) : null;
    }

    /// <summary>Puts the partner in the queue (or refreshes its row while it is pending; a resolved or discarded row stays as it is).</summary>
    public static async Task UpsertAsync(IConfiguration configuration, long partnerId, string name, string cui, string reason, int? beneficiaryId, CancellationToken cancellationToken, string detail = "")
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("""
            INSERT INTO activity_import_partner_checks (partner_id, name, cui_as_typed, reason, status, beneficiary_id, note, created_utc, last_check_utc) VALUES (@partner, @name, @cui, @reason, 'pending', @beneficiary, @detail, @now, @now)
            ON DUPLICATE KEY UPDATE name=@name, cui_as_typed=@cui, reason=IF(status='pending', @reason, reason), note=IF(status='pending', @detail, note), beneficiary_id=COALESCE(@beneficiary, beneficiary_id), last_check_utc=@now
            """, connection);
        command.Parameters.AddWithValue("@detail", detail.Length > 480 ? detail[..480] : detail);
        command.Parameters.AddWithValue("@partner", partnerId);
        command.Parameters.AddWithValue("@name", name.Length > 300 ? name[..300] : name);
        command.Parameters.AddWithValue("@cui", cui.Length > 100 ? cui[..100] : cui);
        command.Parameters.AddWithValue("@reason", reason);
        command.Parameters.AddWithValue("@beneficiary", (object?)beneficiaryId ?? DBNull.Value);
        command.Parameters.AddWithValue("@now", DateTime.UtcNow);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task SetReasonAsync(IConfiguration configuration, long id, string reason, CancellationToken cancellationToken)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("UPDATE activity_import_partner_checks SET reason=@reason, last_check_utc=@now WHERE id=@id AND status='pending'", connection);
        command.Parameters.AddWithValue("@reason", reason);
        command.Parameters.AddWithValue("@now", DateTime.UtcNow);
        command.Parameters.AddWithValue("@id", id);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Closes a pending row of the partner (resolved or discarded). Returns whether a pending row was closed.</summary>
    public static async Task<bool> CloseAsync(IConfiguration configuration, long partnerId, string status, string by, string note, CancellationToken cancellationToken)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("UPDATE activity_import_partner_checks SET status=@status, decided_by=@by, decided_utc=@now, note=@note WHERE partner_id=@partner AND status='pending'", connection);
        command.Parameters.AddWithValue("@status", status);
        command.Parameters.AddWithValue("@by", by);
        command.Parameters.AddWithValue("@note", note.Length > 500 ? note[..500] : note);
        command.Parameters.AddWithValue("@now", DateTime.UtcNow);
        command.Parameters.AddWithValue("@partner", partnerId);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
    }
}

public sealed class MariaBeneficiaryCheckService(IConfiguration configuration, IAccessControl access, IAuditTrail audit, IBeneficiaryRepository beneficiaries, ICompanyDataLookup? lookup = null, IActivityPartnerImportService? partners = null) : IBeneficiaryCheckService
{
    private const string View = "beneficiari.view";
    private const string Edit = "beneficiari.edit";

    public async Task<IReadOnlyList<BeneficiaryCheck>> GetPendingAsync(CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync(View, cancellationToken).ConfigureAwait(false);
        return await ReadPendingAsync(configuration, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<IReadOnlyList<BeneficiaryCheck>> ReadPendingAsync(IConfiguration configuration, CancellationToken cancellationToken)
    {
        var rows = await ActivityPartnerCheckStore.ReadAsync(configuration, PartnerCheckStatus.Pending, cancellationToken).ConfigureAwait(false);
        var waiting = new Dictionary<long, int>();
        await using (var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false))
        {
            await using var command = new MySqlCommand("SELECT partner_id, COUNT(*) FROM activity_import_tickets WHERE state='waiting_beneficiary' GROUP BY partner_id", connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) waiting[reader.GetInt64(0)] = (int)reader.GetInt64(1);
        }
        return [.. rows.Select(row => new BeneficiaryCheck(row.Id, row.PartnerId, row.BeneficiaryId, row.Name, row.Cui, row.Reason,
            BeneficiaryCheckReasons.Text(row.Reason) + (row.Note.Length > 0 && row.Reason is BeneficiaryCheckReasons.InvalidData or BeneficiaryCheckReasons.RepeatedIdentity ? $" ({row.Note})" : ""), row.CreatedUtc, waiting.GetValueOrDefault(row.PartnerId)))];
    }

    public async Task<string> RetryAsync(long id, CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync(Edit, cancellationToken).ConfigureAwait(false);
        var row = await ActivityPartnerCheckStore.FindAsync(configuration, id, cancellationToken).ConfigureAwait(false);
        if (row is null || row.Status != PartnerCheckStatus.Pending) throw new ActivityImportException("Beneficiarul nu mai este de verificat. Actualizează lista.");
        if (row.BeneficiaryId is null) return row.Reason == BeneficiaryCheckReasons.AwaitingAcceptance ? "Partenerul așteaptă acceptarea: folosește „Acceptă”, nu verificarea în ANAF." : "Partenerul nu are încă un beneficiar în BlazorStoc: corectează datele în ActivityApp (adresă, CUI sau nume), iar sincronizarea următoare îl preia singură.";
        if (lookup is null) throw new ActivityImportException("Verificarea în ANAF nu este disponibilă (modulul ANAF este oprit).");
        var digits = SupplierRules.CuiDigits(row.Cui);
        if (!BeneficiaryRules.CuiPattern.IsMatch(BeneficiaryRules.CompactValue(row.Cui)) || digits.Length == 0)
            return "CUI-ul are o formă invalidă: corectează-l în fișa beneficiarului, apoi acceptă beneficiarul.";
        var answer = await lookup.LookupAsync(digits, cancellationToken).ConfigureAwait(false);
        if (answer.Unavailable) return $"ANAF nu a putut răspunde: {answer.Failure}";
        var actor = (await RepositoryAudit.ActorAsync(access, cancellationToken).ConfigureAwait(false)).Username;
        if (answer.NotFound)
        {
            await ActivityPartnerCheckStore.SetReasonAsync(configuration, id, BeneficiaryCheckReasons.CuiUnknown, cancellationToken).ConfigureAwait(false);
            return "ANAF nu cunoaște CUI-ul. Corectează-l sau acceptă beneficiarul.";
        }
        await MarkVerifiedAsync(row, actor, cancellationToken).ConfigureAwait(false);
        await AuditAsync(AuditActions.RetryActivityPartnerCheck, row, "ANAF a confirmat CUI-ul: beneficiarul este verificat", cancellationToken).ConfigureAwait(false);
        return "ANAF a confirmat CUI-ul: beneficiarul este verificat; tichetele lui se preiau la următoarea sincronizare.";
    }

    // The beneficiary made by the import is marked as verified in ANAF and the row is closed.
    internal async Task MarkVerifiedAsync(PartnerCheckRow row, string actor, CancellationToken cancellationToken)
    {
        if (row.BeneficiaryId is { } beneficiaryId && (await beneficiaries.GetBeneficiariesAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(item => item.Id == beneficiaryId) is { IsIndividual: false } beneficiary && !beneficiary.AnafVerified)
        {
            var input = BeneficiaryInput.From(beneficiary);
            input.AnafVerified = true;
            input.Reason = "Verificat în ANAF după preluarea din ActivityApp.";
            try { await beneficiaries.UpdateAsync(beneficiary, input, cancellationToken).ConfigureAwait(false); }
            catch (BeneficiaryOperationException) { /* the data cannot be saved as it is (for instance the CUI has the wrong form): the row is closed all the same, the operator fixes the data in the form */ }
        }
        await ActivityPartnerCheckStore.CloseAsync(configuration, row.PartnerId, PartnerCheckStatus.Resolved, actor, "verificat în ANAF", cancellationToken).ConfigureAwait(false);
    }

    public async Task AcceptAsync(long id, CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync(Edit, cancellationToken).ConfigureAwait(false);
        var row = await ActivityPartnerCheckStore.FindAsync(configuration, id, cancellationToken).ConfigureAwait(false);
        if (row is null || row.Status != PartnerCheckStatus.Pending) throw new ActivityImportException("Beneficiarul nu mai este de verificat. Actualizează lista.");
        if (row.BeneficiaryId is null && row.Reason == BeneficiaryCheckReasons.AwaitingAcceptance && partners is not null)
        {
            await partners.AcceptPartnerAsync(row.PartnerId, cancellationToken).ConfigureAwait(false);
            await AuditAsync(AuditActions.AcceptActivityPartnerCheck, row, "Beneficiar acceptat manual și creat din ActivityApp", cancellationToken).ConfigureAwait(false);
            return;
        }
        if (row.BeneficiaryId is null) throw new ActivityImportException("Partenerul nu are încă un beneficiar în BlazorStoc (lipsește adresa în ActivityApp): se completează în ActivityApp sau se discardează.");
        var actor = (await RepositoryAudit.ActorAsync(access, cancellationToken).ConfigureAwait(false)).Username;
        await ActivityPartnerCheckStore.CloseAsync(configuration, row.PartnerId, PartnerCheckStatus.Resolved, actor, "acceptat manual", cancellationToken).ConfigureAwait(false);
        await AuditAsync(AuditActions.AcceptActivityPartnerCheck, row, "Beneficiar acceptat manual (verificat de operator)", cancellationToken).ConfigureAwait(false);
    }

    public async Task DiscardAsync(long id, CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync(Edit, cancellationToken).ConfigureAwait(false);
        var row = await ActivityPartnerCheckStore.FindAsync(configuration, id, cancellationToken).ConfigureAwait(false);
        if (row is null || row.Status != PartnerCheckStatus.Pending) throw new ActivityImportException("Beneficiarul nu mai este de verificat. Actualizează lista.");
        var actor = (await RepositoryAudit.ActorAsync(access, cancellationToken).ConfigureAwait(false)).Username;
        await ActivityPartnerCheckStore.CloseAsync(configuration, row.PartnerId, PartnerCheckStatus.Discarded, actor, "discardat de operator", cancellationToken).ConfigureAwait(false);
        // The tickets that waited for this client are discarded for good.
        await using (var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false))
        {
            await using var command = new MySqlCommand("UPDATE activity_import_tickets SET state='discarded', decided_by=@by, decided_utc=@now, updated_utc=@now WHERE partner_id=@partner AND state='waiting_beneficiary'", connection);
            command.Parameters.AddWithValue("@by", actor);
            command.Parameters.AddWithValue("@now", DateTime.UtcNow);
            command.Parameters.AddWithValue("@partner", row.PartnerId);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await AuditAsync(AuditActions.DiscardActivityPartner, row, "Partener ActivityApp discardat: nici el, nici tichetele lui nu se mai preiau", cancellationToken).ConfigureAwait(false);
    }

    private Task AuditAsync(string action, PartnerCheckRow row, string description, CancellationToken cancellationToken) =>
        AuditRecorder.RecordActionAsync(audit, access, AuditEntities.ActivityImport, action, row.Id.ToString(), $"{description}: «{row.Name}»",
            AuditDetails.Identification(("Partener ActivityApp", row.PartnerId.ToString()), ("Denumire", row.Name), ("CUI", row.Cui), ("Motiv", BeneficiaryCheckReasons.Text(row.Reason))), "", cancellationToken);
}

/// <summary>The notification for the administrator: one instance for all the beneficiaries to verify together (their number, the reasons and the tickets that wait), on the day the oldest was found; it goes away by itself when the queue is empty.</summary>
public sealed class ActivityPartnerCheckSource(IConfiguration configuration) : IExpirySource
{
    public const string SourceKey = "sistem.beneficiari-activityapp-de-verificat";
    public const string NameKey = "beneficiar de verificat";
    public const string DetailsKey = "detalii verificare";
    public string Key => SourceKey;
    public string Category => "Sistem";
    public string EventName => "Beneficiari din ActivityApp de verificat";
    public string DateLabel => "constatării";
    public int DefaultThresholdDays => 0;
    public string RemovedReason => "Toți beneficiarii au fost clarificați sau discardați.";
    public string DefaultSubject => "Verifică beneficiarii din ActivityApp: <beneficiar de verificat>";
    public string DefaultBody => "<detalii verificare> Vezi Beneficiari → De verificat.";
    public IReadOnlyList<ExpiryPlaceholder> Placeholders { get; } =
    [
        new(NameKey, "Câți beneficiari preluați din ActivityApp trebuie verificați", "3 beneficiari"),
        new(DetailsKey, "Motivele și câte tichete așteaptă", "2 — adresă lipsă, 1 — ANAF nu a putut răspunde; 5 tichete așteaptă clarificarea.")
    ];

    public string DateChangedReason(DateOnly from, DateOnly to, ExpiryInstance current) =>
        $"Constatarea s-a mutat de la {StockMovementRules.DisplayDate(from)} la {StockMovementRules.DisplayDate(to)}.";

    public const int AggregateId = 1;

    public static IReadOnlyList<ExpiryInstance> Instances(IReadOnlyList<BeneficiaryCheck> items)
    {
        if (items.Count == 0) return [];
        var reasons = string.Join(", ", items.GroupBy(item => item.Reason).OrderByDescending(group => group.Count()).ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => $"{group.Count()} — {BeneficiaryCheckReasons.Text(group.Key)}"));
        var waiting = items.Sum(item => item.WaitingTickets);
        var tickets = waiting == 0 ? "niciun tichet nu așteaptă" : waiting == 1 ? "un tichet așteaptă" : $"{waiting} tichete așteaptă";
        var count = items.Count == 1 ? "un beneficiar" : $"{items.Count} beneficiari";
        return [new ExpiryInstance(AggregateId, "Beneficiari din ActivityApp de verificat", DateOnly.FromDateTime(items.Min(item => item.CreatedUtc).ToLocalTime()),
            new Dictionary<string, string> { [NameKey] = count, [DetailsKey] = $"{char.ToUpperInvariant(reasons[0])}{reasons[1..]}; {tickets} clarificarea." }, "/beneficiari?tab=de-verificat")];
    }

    public async Task<IReadOnlyList<ExpiryInstance>> GetInstancesAsync(CancellationToken cancellationToken = default) =>
        Instances(await MariaBeneficiaryCheckService.ReadPendingAsync(configuration, cancellationToken).ConfigureAwait(false));
}

internal sealed class GuardedBeneficiaryCheckService(IBeneficiaryCheckService inner, ModuleRegistry modules) : IBeneficiaryCheckService
{
    private const string Id = ActivityImportModule.ModuleId;
    public Task<IReadOnlyList<BeneficiaryCheck>> GetPendingAsync(CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.GetPendingAsync(cancellationToken));
    public Task<string> RetryAsync(long id, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.RetryAsync(id, cancellationToken));
    public Task AcceptAsync(long id, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.AcceptAsync(id, cancellationToken));
    public Task DiscardAsync(long id, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.DiscardAsync(id, cancellationToken));
}

/// <summary>While the module is out the queue is empty (nothing to verify is shown) and the operations say the module is out.</summary>
internal sealed class UnavailableBeneficiaryCheckService : IBeneficiaryCheckService
{
    public Task<IReadOnlyList<BeneficiaryCheck>> GetPendingAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<BeneficiaryCheck>>([]);
    public Task<string> RetryAsync(long id, CancellationToken cancellationToken = default) => Task.FromException<string>(new ModuleUnavailableException(ActivityImportModule.ModuleId));
    public Task AcceptAsync(long id, CancellationToken cancellationToken = default) => Task.FromException(new ModuleUnavailableException(ActivityImportModule.ModuleId));
    public Task DiscardAsync(long id, CancellationToken cancellationToken = default) => Task.FromException(new ModuleUnavailableException(ActivityImportModule.ModuleId));
}
