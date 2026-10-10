using MySqlConnector;
using static BlazorStoc.Services.MariaDb;

namespace BlazorStoc.Services;

// MariaDB mode: `risk_analyses` and `risk_analysis_renewals` are created by migration 39 (MariaSchemaMigrations); like the other repositories
// this one never alters the schema. Every write locks the row of the beneficiary first (the same lock the contract and intervention repositories
// take), so two sessions working on the analyses of one beneficiary are serialized; the unique key on risk_analyses.active_work_point_id
// (one ACTIVE analysis per work point) stays as the last line of defence.
public sealed class MariaRiskAnalysisRepository(
    IConfiguration configuration,
    IAccessControl? accessControl = null,
    IAuditTrail? auditTrail = null,
    IArchiveService? archiveService = null,
    TimeProvider? timeProvider = null) : IRiskAnalysisRepository
{
    private const string AnalysisColumns = "a.id, a.beneficiary_id, a.work_point_id, a.registration_number, a.author, a.initial_date, a.last_renewal_date, " +
        "a.validity_months, a.is_active, a.notes, a.version";
    private const string ViewFrom = """
        FROM risk_analyses a JOIN beneficiaries b ON b.id = a.beneficiary_id JOIN beneficiary_work_points w ON w.id = a.work_point_id
        """;
    private const string RenewalColumns = "r.id, r.risk_analysis_id, r.renewal_date, r.previous_renewal_date, r.recorded_by, r.recorded_utc, r.notes";
    private readonly IArchiveService archiver = archiveService ?? new ArchiveService(accessControl);
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private DateOnly Today => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    // One journal entry produced by an operation; written after the transaction commits, under the beneficiary the analysis belongs to.
    private sealed record Pending(string Action, string Target, string Details, string Motif);

    public async Task<IReadOnlyList<RiskAnalysisView>> GetForBeneficiaryAsync(int beneficiaryId, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        return (await ReadViewsAsync(connection, null, "WHERE a.beneficiary_id=@id", [("@id", beneficiaryId)], cancellationToken).ConfigureAwait(false))
            .OrderByDescending(view => view.Analysis.IsActive).ThenByDescending(view => view.Analysis.InitialDate).ThenByDescending(view => view.Analysis.Id).ToArray();
    }

    public async Task<IReadOnlyList<RiskAnalysisView>> GetAllAsync(bool includeOff, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        return (await ReadViewsAsync(connection, null, includeOff ? "WHERE 1=1" : "WHERE a.is_active=1", [], cancellationToken).ConfigureAwait(false))
            .OrderByDescending(view => view.Analysis.IsActive).ThenBy(view => view.Analysis.ExpiryDate).ThenBy(view => view.BeneficiaryName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(view => view.Analysis.Id).ToArray();
    }

    public async Task<RiskAnalysisView> CreateAsync(int beneficiaryId, RiskAnalysisInput input, bool deactivateExisting, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        var value = input.Validated();
        var initial = value.InitialDate!.Value;
        if (RiskAnalysisRules.InitialDateError(initial, Today) is { } dateError) throw new RiskAnalysisOperationException(dateError);
        var pending = new List<Pending>();
        var view = await WriteAsync(async (connection, transaction) =>
        {
            pending.Clear();
            var ownerName = await LockBeneficiaryAsync(connection, transaction, beneficiaryId, cancellationToken).ConfigureAwait(false);
            var workPointName = await WorkPointNameAsync(connection, transaction, beneficiaryId, value.WorkPointId, cancellationToken).ConfigureAwait(false);
            await EnsureUniqueNumberAsync(connection, transaction, beneficiaryId, value.Number, null, cancellationToken).ConfigureAwait(false);
            if (await FindActiveAsync(connection, transaction, value.WorkPointId, 0, cancellationToken).ConfigureAwait(false) is { } other)
            {
                if (!deactivateExisting) throw RiskAnalysisRules.ActiveExists(workPointName, other.Number);
                await SetActiveAsync(connection, transaction, other, false, cancellationToken).ConfigureAwait(false);
                pending.Add(new(AuditActions.DeactivateRiskAnalysis, RiskAnalysisRules.Target(beneficiaryId, ownerName, other.Number),
                    AuditDetails.Changes(new AuditChange("Stare", "On", "Off")) + $"; Punct de lucru: {workPointName}; Înlocuită de analiza nr. {value.Number}",
                    RiskAnalysisRules.GeneratedReplaceReason));
            }
            await using var insert = Command(connection, transaction, """
                INSERT INTO risk_analyses (beneficiary_id, work_point_id, registration_number, author, initial_date, last_renewal_date, validity_months,
                                           is_active, active_work_point_id, notes, version)
                VALUES (@beneficiary, @workPoint, @number, @author, @initial, @initial, @validity, 1, @workPoint, @notes, 0)
                """, ("@beneficiary", beneficiaryId), ("@workPoint", value.WorkPointId), ("@number", value.Number), ("@author", value.Author),
                ("@initial", SqlDate(initial)), ("@validity", value.ValidityMonths), ("@notes", value.Notes));
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            var created = new RiskAnalysis(checked((int)insert.LastInsertedId), beneficiaryId, value.WorkPointId, value.Number, value.Author, initial, initial,
                value.ValidityMonths, true, value.Notes, 0);
            pending.Add(new(AuditActions.CreateRiskAnalysis, RiskAnalysisRules.Target(beneficiaryId, ownerName, created.Number),
                RiskAnalysisRules.Identification(created, workPointName), string.Empty));
            return await LoadViewAsync(connection, transaction, created.Id, cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
        await RecordAsync(beneficiaryId, pending, cancellationToken).ConfigureAwait(false);
        return view;
    }

    public async Task<RiskAnalysisView> UpdateAsync(RiskAnalysis original, RiskAnalysisInput input, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        var value = input.Validated();
        var initial = value.InitialDate!.Value;
        var pending = new List<Pending>();
        var view = await WriteAsync(async (connection, transaction) =>
        {
            pending.Clear();
            var ownerName = await LockBeneficiaryAsync(connection, transaction, original.BeneficiaryId, cancellationToken).ConfigureAwait(false);
            RiskAnalysisRules.CheckCurrent(await GetLockedAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false), original);
            await EnsureUniqueNumberAsync(connection, transaction, original.BeneficiaryId, value.Number, original.Id, cancellationToken).ConfigureAwait(false);
            if (initial != original.InitialDate)
            {
                if (await RenewalCountAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false) > 0) throw RiskAnalysisRules.InitialDateLocked();
                if (RiskAnalysisRules.InitialDateError(initial, Today) is { } dateError) throw new RiskAnalysisOperationException(dateError);
            }
            // Without renewals the last renewal is the registration date, so it follows a changed registration date.
            var after = original with
            {
                Number = value.Number, Author = value.Author, InitialDate = initial, ValidityMonths = value.ValidityMonths, Notes = value.Notes,
                LastRenewalDate = initial != original.InitialDate ? initial : original.LastRenewalDate
            };
            if (after != original)
            {
                await using var update = Command(connection, transaction, """
                    UPDATE risk_analyses SET registration_number=@number, author=@author, initial_date=@initial, last_renewal_date=@renewal,
                                             validity_months=@validity, notes=@notes, version=@version
                    WHERE id=@id AND version=@oldVersion
                    """, ("@number", after.Number), ("@author", after.Author), ("@initial", SqlDate(after.InitialDate)), ("@renewal", SqlDate(after.LastRenewalDate)),
                    ("@validity", after.ValidityMonths), ("@notes", after.Notes), ("@version", checked(original.Version + 1)),
                    ("@id", original.Id), ("@oldVersion", original.Version));
                if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1) throw RiskAnalysisRules.Changed();
                var workPointName = await WorkPointNameAsync(connection, transaction, original.BeneficiaryId, original.WorkPointId, cancellationToken).ConfigureAwait(false);
                pending.Add(new(RiskAnalysisRules.EditAction(original, after), RiskAnalysisRules.Target(original.BeneficiaryId, ownerName, after.Number),
                    $"Punct de lucru: {workPointName}; " + AuditDetails.Changes([.. RiskAnalysisRules.Changes(original, after)]), RiskAnalysisRules.GeneratedEditReason));
            }
            return await LoadViewAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
        await RecordAsync(original.BeneficiaryId, pending, cancellationToken).ConfigureAwait(false);
        return view;
    }

    public async Task<RiskAnalysisView> RenewAsync(RiskAnalysis original, RiskAnalysisRenewalInput input, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        if (RiskAnalysisRules.RenewalDateError(input.Date, original.LastRenewalDate, Today) is { } dateError) throw new RiskAnalysisOperationException(dateError);
        var date = input.Date!.Value;
        var notes = TextNormalization.ForStorage((input.Notes ?? "").Replace("\r\n", "\n"));
        if (notes.Length > RiskAnalysisInput.NotesMaximumLength) throw new RiskAnalysisOperationException("Observațiile pot avea cel mult 1000 de caractere.");
        var actor = await ActorAsync(cancellationToken).ConfigureAwait(false);
        var recorded = MariaTimeText.Format(clock.GetUtcNow().UtcDateTime);
        var pending = new List<Pending>();
        var view = await WriteAsync(async (connection, transaction) =>
        {
            pending.Clear();
            var ownerName = await LockBeneficiaryAsync(connection, transaction, original.BeneficiaryId, cancellationToken).ConfigureAwait(false);
            var current = await GetLockedAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false);
            RiskAnalysisRules.CheckCurrent(current, original);
            if (!original.IsActive) throw new RiskAnalysisOperationException("Analiza este oprită (Off) și nu poate fi reînnoită. Activeaz-o mai întâi.");
            await using (var insert = Command(connection, transaction, """
                INSERT INTO risk_analysis_renewals (risk_analysis_id, renewal_date, previous_renewal_date, recorded_by, recorded_utc, notes)
                VALUES (@analysis, @date, @previous, @actor, @recorded, @notes)
                """, ("@analysis", original.Id), ("@date", SqlDate(date)), ("@previous", SqlDate(original.LastRenewalDate)), ("@actor", actor),
                ("@recorded", recorded), ("@notes", notes)))
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            var after = original with { LastRenewalDate = date };
            await SetLastRenewalAsync(connection, transaction, original, date, cancellationToken).ConfigureAwait(false);
            var workPointName = await WorkPointNameAsync(connection, transaction, original.BeneficiaryId, original.WorkPointId, cancellationToken).ConfigureAwait(false);
            pending.Add(new(AuditActions.RenewRiskAnalysis, RiskAnalysisRules.Target(original.BeneficiaryId, ownerName, original.Number),
                $"Punct de lucru: {workPointName}; " + AuditDetails.Changes(
                    new AuditChange("Ultima reînnoire", StockMovementRules.DisplayDate(original.LastRenewalDate), StockMovementRules.DisplayDate(after.LastRenewalDate)),
                    new AuditChange("Expiră", StockMovementRules.DisplayDate(original.ExpiryDate), StockMovementRules.DisplayDate(after.ExpiryDate))) +
                    (notes.Length > 0 ? $"; Observații: {notes}" : string.Empty), RiskAnalysisRules.GeneratedRenewalReason));
            return await LoadViewAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
        await RecordAsync(original.BeneficiaryId, pending, cancellationToken).ConfigureAwait(false);
        return view;
    }

    public async Task<RiskAnalysisView> UndoLastRenewalAsync(RiskAnalysis original, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        var pending = new List<Pending>();
        var view = await WriteAsync(async (connection, transaction) =>
        {
            pending.Clear();
            var ownerName = await LockBeneficiaryAsync(connection, transaction, original.BeneficiaryId, cancellationToken).ConfigureAwait(false);
            RiskAnalysisRules.CheckCurrent(await GetLockedAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false), original);
            RiskAnalysisRenewal? last = null;
            await using (var newest = Command(connection, transaction, $"""
                SELECT {RenewalColumns} FROM risk_analysis_renewals r WHERE r.risk_analysis_id=@id ORDER BY r.renewal_date DESC, r.id DESC LIMIT 1 FOR UPDATE
                """, ("@id", original.Id)))
            await using (var reader = await newest.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) last = ReadRenewal(reader);
            if (last is null) throw RiskAnalysisRules.NoRenewal();
            await using (var delete = Command(connection, transaction, "DELETE FROM risk_analysis_renewals WHERE id=@id", ("@id", last.Id)))
                await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            var after = original with { LastRenewalDate = last.PreviousRenewalDate };
            await SetLastRenewalAsync(connection, transaction, original, last.PreviousRenewalDate, cancellationToken).ConfigureAwait(false);
            var workPointName = await WorkPointNameAsync(connection, transaction, original.BeneficiaryId, original.WorkPointId, cancellationToken).ConfigureAwait(false);
            pending.Add(new(AuditActions.UndoRiskAnalysisRenewal, RiskAnalysisRules.Target(original.BeneficiaryId, ownerName, original.Number),
                $"Punct de lucru: {workPointName}; " + AuditDetails.Changes(
                    new AuditChange("Ultima reînnoire", StockMovementRules.DisplayDate(original.LastRenewalDate), StockMovementRules.DisplayDate(after.LastRenewalDate)),
                    new AuditChange("Expiră", StockMovementRules.DisplayDate(original.ExpiryDate), StockMovementRules.DisplayDate(after.ExpiryDate))),
                RiskAnalysisRules.GeneratedUndoReason));
            return await LoadViewAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
        await RecordAsync(original.BeneficiaryId, pending, cancellationToken).ConfigureAwait(false);
        return view;
    }

    public async Task<RiskAnalysisView> ActivateAsync(RiskAnalysis original, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        var pending = new List<Pending>();
        var view = await WriteAsync(async (connection, transaction) =>
        {
            pending.Clear();
            var ownerName = await LockBeneficiaryAsync(connection, transaction, original.BeneficiaryId, cancellationToken).ConfigureAwait(false);
            RiskAnalysisRules.CheckCurrent(await GetLockedAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false), original);
            if (original.IsActive) throw RiskAnalysisRules.Changed();
            var workPointName = await WorkPointNameAsync(connection, transaction, original.BeneficiaryId, original.WorkPointId, cancellationToken).ConfigureAwait(false);
            if (await FindActiveAsync(connection, transaction, original.WorkPointId, original.Id, cancellationToken).ConfigureAwait(false) is { } other)
                throw RiskAnalysisRules.ActiveExists(workPointName, other.Number);
            await SetActiveAsync(connection, transaction, original, true, cancellationToken).ConfigureAwait(false);
            pending.Add(new(AuditActions.ActivateRiskAnalysis, RiskAnalysisRules.Target(original.BeneficiaryId, ownerName, original.Number),
                AuditDetails.Changes(new AuditChange("Stare", "Off", "On")) + $"; Punct de lucru: {workPointName}", RiskAnalysisRules.GeneratedActivationReason));
            return await LoadViewAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
        await RecordAsync(original.BeneficiaryId, pending, cancellationToken).ConfigureAwait(false);
        return view;
    }

    public async Task<RiskAnalysisView> DeactivateAsync(RiskAnalysis original, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        var pending = new List<Pending>();
        var view = await WriteAsync(async (connection, transaction) =>
        {
            pending.Clear();
            var ownerName = await LockBeneficiaryAsync(connection, transaction, original.BeneficiaryId, cancellationToken).ConfigureAwait(false);
            RiskAnalysisRules.CheckCurrent(await GetLockedAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false), original);
            if (!original.IsActive) throw RiskAnalysisRules.Changed();
            var workPointName = await WorkPointNameAsync(connection, transaction, original.BeneficiaryId, original.WorkPointId, cancellationToken).ConfigureAwait(false);
            await SetActiveAsync(connection, transaction, original, false, cancellationToken).ConfigureAwait(false);
            pending.Add(new(AuditActions.DeactivateRiskAnalysis, RiskAnalysisRules.Target(original.BeneficiaryId, ownerName, original.Number),
                AuditDetails.Changes(new AuditChange("Stare", "On", "Off")) + $"; Punct de lucru: {workPointName}", RiskAnalysisRules.GeneratedDeactivationReason));
            return await LoadViewAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
        await RecordAsync(original.BeneficiaryId, pending, cancellationToken).ConfigureAwait(false);
        return view;
    }

    // Archived deletion: the analysis goes to archive_risk_analyses and its renewals to archive_relations, in one transaction.
    public async Task DeleteAsync(RiskAnalysis original, string reason, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        var motif = ChangeReasonRules.Normalize(reason);
        if (ChangeReasonRules.ValidationError(motif) is { } reasonError) throw new RiskAnalysisOperationException(reasonError);
        RiskAnalysisView view;
        await using (var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false))
            view = await LoadViewAsync(connection, null, original.Id, cancellationToken).ConfigureAwait(false);
        if (view.Analysis != original) throw RiskAnalysisRules.Changed();
        await archiver.ExecuteAsync(ArchiveRequests.RiskAnalysis(view, motif), async (operation, token) =>
        {
            await WriteAsync(async (connection, transaction) =>
            {
                await LockBeneficiaryAsync(connection, transaction, original.BeneficiaryId, token).ConfigureAwait(false);
                RiskAnalysisRules.CheckCurrent(await GetLockedAsync(connection, transaction, original.Id, token).ConfigureAwait(false), original);
                var renewalIds = new List<int>();
                await using (var lockRenewals = Command(connection, transaction, "SELECT id FROM risk_analysis_renewals WHERE risk_analysis_id=@id FOR UPDATE", ("@id", original.Id)))
                await using (var lockReader = await lockRenewals.ExecuteReaderAsync(token).ConfigureAwait(false))
                    while (await lockReader.ReadAsync(token).ConfigureAwait(false)) renewalIds.Add(checked((int)lockReader.GetInt64(0)));
                if (!renewalIds.Order().SequenceEqual(view.Renewals.Select(item => item.Id).Order())) throw RiskAnalysisRules.Changed();
                await MariaArchivePersistence.InsertAsync(connection, transaction, operation, [], token).ConfigureAwait(false);
                await using (var deleteRenewals = Command(connection, transaction, "DELETE FROM risk_analysis_renewals WHERE risk_analysis_id=@id", ("@id", original.Id)))
                    await deleteRenewals.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                await using var command = Command(connection, transaction,
                    "DELETE FROM risk_analyses WHERE id=@id AND version=@version", ("@id", original.Id), ("@version", original.Version));
                if (await command.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1) throw RiskAnalysisRules.Changed();
                await MariaArchivePersistence.InsertAuditAsync(connection, transaction, operation, token).ConfigureAwait(false);
                return true;
            }, token).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The number of the analysis of a work point (an active one first), null when it has none. Used to refuse deleting such a point.</summary>
    internal static async Task<string?> NumberForWorkPointAsync(MySqlConnection connection, MySqlTransaction? transaction, int workPointId, CancellationToken token)
    {
        await using var command = Command(connection, transaction,
            "SELECT registration_number FROM risk_analyses WHERE work_point_id=@id ORDER BY is_active DESC, initial_date DESC, id DESC LIMIT 1", ("@id", workPointId));
        return await command.ExecuteScalarAsync(token).ConfigureAwait(false) as string;
    }

    /// <summary>The number of analyses of a beneficiary (On or Off); they keep it from being deleted.</summary>
    internal static async Task<int> CountForBeneficiaryAsync(MySqlConnection connection, MySqlTransaction? transaction, int beneficiaryId, CancellationToken token)
    {
        await using var command = Command(connection, transaction, "SELECT COUNT(*) FROM risk_analyses WHERE beneficiary_id=@id", ("@id", beneficiaryId));
        return Convert.ToInt32(await command.ExecuteScalarAsync(token).ConfigureAwait(false));
    }

    private static async Task<string> WorkPointNameAsync(MySqlConnection connection, MySqlTransaction transaction, int beneficiaryId, int workPointId, CancellationToken token)
    {
        await using var command = Command(connection, transaction, "SELECT name FROM beneficiary_work_points WHERE id=@id AND beneficiary_id=@beneficiary",
            ("@id", workPointId), ("@beneficiary", beneficiaryId));
        return await command.ExecuteScalarAsync(token).ConfigureAwait(false) as string ?? throw RiskAnalysisRules.WorkPointNotOfBeneficiary();
    }

    // The ACTIVE analysis of the work point other than `exceptId` (locked), null when there is none.
    private static async Task<RiskAnalysis?> FindActiveAsync(MySqlConnection connection, MySqlTransaction transaction, int workPointId, int exceptId, CancellationToken token)
    {
        await using var command = Command(connection, transaction,
            $"SELECT {AnalysisColumns} FROM risk_analyses a WHERE a.active_work_point_id=@workPoint AND a.id<>@id FOR UPDATE", ("@workPoint", workPointId), ("@id", exceptId));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        return await reader.ReadAsync(token).ConfigureAwait(false) ? ReadAnalysis(reader) : null;
    }

    private static async Task SetActiveAsync(MySqlConnection connection, MySqlTransaction transaction, RiskAnalysis original, bool active, CancellationToken token)
    {
        await using var update = Command(connection, transaction,
            "UPDATE risk_analyses SET is_active=@active, active_work_point_id=@activePoint, version=@version WHERE id=@id AND version=@oldVersion",
            ("@active", active ? 1 : 0), ("@activePoint", active ? original.WorkPointId : null), ("@version", checked(original.Version + 1)),
            ("@id", original.Id), ("@oldVersion", original.Version));
        if (await update.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1) throw RiskAnalysisRules.Changed();
    }

    private static async Task SetLastRenewalAsync(MySqlConnection connection, MySqlTransaction transaction, RiskAnalysis original, DateOnly date, CancellationToken token)
    {
        await using var update = Command(connection, transaction,
            "UPDATE risk_analyses SET last_renewal_date=@date, version=@version WHERE id=@id AND version=@oldVersion",
            ("@date", SqlDate(date)), ("@version", checked(original.Version + 1)), ("@id", original.Id), ("@oldVersion", original.Version));
        if (await update.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1) throw RiskAnalysisRules.Changed();
    }

    private static async Task<int> RenewalCountAsync(MySqlConnection connection, MySqlTransaction transaction, int analysisId, CancellationToken token)
    {
        await using var command = Command(connection, transaction, "SELECT COUNT(*) FROM risk_analysis_renewals WHERE risk_analysis_id=@id", ("@id", analysisId));
        return Convert.ToInt32(await command.ExecuteScalarAsync(token).ConfigureAwait(false));
    }

    private static async Task EnsureUniqueNumberAsync(MySqlConnection connection, MySqlTransaction transaction, int beneficiaryId, string number, int? excludedId, CancellationToken token)
    {
        await using var command = Command(connection, transaction,
            "SELECT id FROM risk_analyses WHERE beneficiary_id=@beneficiary AND registration_number=@number AND (@id IS NULL OR id<>@id) LIMIT 1",
            ("@beneficiary", beneficiaryId), ("@number", number), ("@id", excludedId));
        if (await command.ExecuteScalarAsync(token).ConfigureAwait(false) is not null) throw RiskAnalysisRules.DuplicateNumber(number);
    }

    private static async Task<string> LockBeneficiaryAsync(MySqlConnection connection, MySqlTransaction transaction, int beneficiaryId, CancellationToken token)
    {
        await using var command = Command(connection, transaction, "SELECT name FROM beneficiaries WHERE id=@id FOR UPDATE", ("@id", beneficiaryId));
        return await command.ExecuteScalarAsync(token).ConfigureAwait(false) as string ?? throw RiskAnalysisRules.BeneficiaryMissing();
    }

    private static async Task<RiskAnalysis?> GetLockedAsync(MySqlConnection connection, MySqlTransaction transaction, int id, CancellationToken token)
    {
        await using var command = Command(connection, transaction, $"SELECT {AnalysisColumns} FROM risk_analyses a WHERE a.id=@id FOR UPDATE", ("@id", id));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        return await reader.ReadAsync(token).ConfigureAwait(false) ? ReadAnalysis(reader) : null;
    }

    private static async Task<RiskAnalysisView> LoadViewAsync(MySqlConnection connection, MySqlTransaction? transaction, int id, CancellationToken token)
    {
        var views = await ReadViewsAsync(connection, transaction, "WHERE a.id=@id", [("@id", id)], token).ConfigureAwait(false);
        return views.Count == 1 ? views[0] : throw RiskAnalysisRules.Changed();
    }

    private static async Task<IReadOnlyList<RiskAnalysisView>> ReadViewsAsync(MySqlConnection connection, MySqlTransaction? transaction, string where,
        (string Name, object? Value)[] parameters, CancellationToken token)
    {
        var analyses = new List<(RiskAnalysis Analysis, string Beneficiary, string WorkPoint, string Address, bool Primary)>();
        await using (var command = Command(connection, transaction, $"SELECT {AnalysisColumns}, b.name, w.name, w.address, w.is_primary {ViewFrom} {where}", parameters))
        await using (var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            while (await reader.ReadAsync(token).ConfigureAwait(false))
                analyses.Add((ReadAnalysis(reader), reader.GetString(11), reader.GetString(12), reader.GetString(13), reader.GetInt32(14) != 0));
        var renewals = new List<RiskAnalysisRenewal>();
        await using (var command = Command(connection, transaction,
            $"SELECT {RenewalColumns} FROM risk_analysis_renewals r WHERE r.risk_analysis_id IN (SELECT a.id {ViewFrom} {where}) ORDER BY r.renewal_date DESC, r.id DESC", parameters))
        await using (var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            while (await reader.ReadAsync(token).ConfigureAwait(false)) renewals.Add(ReadRenewal(reader));
        return analyses.Select(item => new RiskAnalysisView(item.Analysis, item.Beneficiary, item.WorkPoint, item.Address, item.Primary,
            renewals.Where(renewal => renewal.RiskAnalysisId == item.Analysis.Id).ToArray())).ToArray();
    }

    private async Task<string> ActorAsync(CancellationToken token) =>
        (accessControl is null ? null : await accessControl.GetUsernameAsync(token).ConfigureAwait(false)) ?? (accessControl is null ? "sistem" : "necunoscut");

    private async Task RecordAsync(int beneficiaryId, IEnumerable<Pending> pending, CancellationToken token)
    {
        foreach (var item in pending)
            await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.Beneficiary, item.Action, beneficiaryId.ToString(),
                item.Target, item.Details, item.Motif, token).ConfigureAwait(false);
    }

    private Task<T> WriteAsync<T>(Func<MySqlConnection, MySqlTransaction, Task<T>> action, CancellationToken token) =>
        MariaDb.WriteAsync(configuration, action, message => new RiskAnalysisOperationException(message), Translate, token);

    // A concurrent operation passed the checks and hit a unique key (the checks run under the beneficiary lock, so this is rare).
    private static Exception? Translate(MySqlException exception) => exception.Number switch
    {
        1062 => exception.Message.Contains("uq_risk_analyses_number", StringComparison.Ordinal)
            ? new RiskAnalysisOperationException("Beneficiarul are deja o analiză de risc cu acest număr de înregistrare.")
            : new RiskAnalysisOperationException("Punctul de lucru are deja o analiză de risc activă. Actualizează pagina și reia operația."),
        1451 => new RiskAnalysisOperationException("Analiza de risc nu poate fi ștearsă: are date asociate."),
        _ => null
    };

    private static RiskAnalysis ReadAnalysis(MySqlDataReader reader) =>
        new(checked((int)reader.GetInt64(0)), checked((int)reader.GetInt64(1)), checked((int)reader.GetInt64(2)), reader.GetString(3), reader.GetString(4),
            ReadDate(reader, 5), ReadDate(reader, 6), reader.GetInt32(7), reader.GetInt32(8) != 0, reader.GetString(9), reader.GetInt64(10));

    private static RiskAnalysisRenewal ReadRenewal(MySqlDataReader reader) =>
        new(checked((int)reader.GetInt64(0)), checked((int)reader.GetInt64(1)), ReadDate(reader, 2), ReadDate(reader, 3), reader.GetString(4),
            MariaTimeText.Parse(reader.GetString(5)), reader.GetString(6));

    private static DateOnly ReadDate(MySqlDataReader reader, int ordinal) => DateOnly.FromDateTime(reader.GetDateTime(ordinal));

    private static DateTime SqlDate(DateOnly date) => date.ToDateTime(TimeOnly.MinValue);

    private Task EnsureOperatorAsync(CancellationToken token) => accessControl?.EnsureBeneficiaryOperatorAsync(token) ?? Task.CompletedTask;
}
