using System.Text.Json;
using BlazorStoc.Services;
using MySqlConnector;

namespace BlazorStoc.Modules.ActivityImport;

// Stage 2 of the import: the partners of ActivityApp become beneficiaries and their work points become work points of those beneficiaries (the tickets come in stage 3).
// A simulation reads ActivityApp and BlazorStoc and reports what an application would do, without writing anything. The application does it through the ordinary repositories of
// beneficiaries and work points (so every rule of BlazorStoc holds and every creation is in the journal under the administrator who ran the import), one item at a time: an item that
// is refused becomes a problem for the administrator and the run goes on; a run that stops leaves what it did, which the tie table (activity_import_map) remembers, so the
// next run takes up where it was. Only the administrator (permission "setari-import-activityapp.run").

public sealed record ImportReportItem(string Entity, long SourceId, string Name, string Action, string Message);

public sealed record ImportReport(string Kind, DateTime AtUtc, string By, bool Applied, PlanCounts Partners, PlanCounts Points, IReadOnlyList<ImportReportItem> Items, bool Truncated, string? Error = null, int AnafFilled = 0, int AnafUnverified = 0);

public sealed record ImportRunInfo(long Id, string Kind, DateTime StartedUtc, DateTime? FinishedUtc, string StartedBy, string Status, ImportReport? Report);

public sealed record ImportIssue(long Id, long RunId, string Entity, long SourceId, string SourceName, string Message, DateTime CreatedUtc);

public interface IActivityPartnerImportService
{
    /// <summary>Reads ActivityApp and BlazorStoc and reports what the application would do. Writes nothing but the record of the run.</summary>
    Task<ImportReport> SimulateAsync(CancellationToken cancellationToken = default);
    /// <summary>Creates and ties what the simulation shows. Needs the import to be switched on.</summary>
    Task<ImportReport> ApplyAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default);
    /// <summary>
    /// Takes only these partners of ActivityApp (the clients of the tickets read) and their work points, like <see cref="ApplyAsync"/> but without the run gate and without the record of a run: the
    /// caller (the ticket import) already holds the gate. Needs the import to be switched on.
    /// </summary>
    Task<ImportReport> ApplyForAsync(IReadOnlySet<long> partnerIds, CancellationToken cancellationToken = default);
    Task<ImportRunInfo?> GetLastRunAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ImportIssue>> GetOpenIssuesAsync(CancellationToken cancellationToken = default);
    Task ResolveIssueAsync(long issueId, CancellationToken cancellationToken = default);
}

public sealed class MariaActivityPartnerImportService(IConfiguration configuration, IActivityImportService import, IActivitySource source, IBeneficiaryRepository beneficiaries,
    IWorkPointRepository workPoints, IAccessControl access, IAuditTrail audit, ICompanyDataLookup? lookup = null) : IActivityPartnerImportService
{
    private const string Run = "setari-import-activityapp.run";
    private const string View = "setari-import-activityapp.view";
    private const int MaximumItems = 500;
    // ANAF answers about one company a second: a run asks for at most this many (the rest are asked in the next run).
    private const int MaximumAnafLookups = 300;
    // One run at a time in the application: two administrators cannot start the same import together.
    private static SemaphoreSlim Gate => ImportStore.Gate;

    private sealed record Loaded(StructureReport Structure, IReadOnlyList<PartnerPlan> Partners, IReadOnlyList<PointPlan> Points, IReadOnlyDictionary<int, IReadOnlyList<WorkPoint>> Locals);

    public Task<ImportReport> SimulateAsync(CancellationToken cancellationToken = default) => ExecuteAsync(false, null, cancellationToken);

    public Task<ImportReport> ApplyAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default) => ExecuteAsync(true, progress, cancellationToken);

    private async Task<ImportReport> ExecuteAsync(bool apply, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        await access.EnsureAsync(Run, cancellationToken).ConfigureAwait(false);
        if (!MariaDatabaseGuard.IsAllowedDatabase(configuration)) throw new ActivityImportException("Modificările sunt permise numai în baza BlazorStoc. Verifică numele bazei configurate.");
        if (!await Gate.WaitAsync(0, cancellationToken).ConfigureAwait(false)) throw new ActivityImportException("O altă rulare a importului este în desfășurare. Așteaptă să se termine.");
        try
        {
            var actor = (await RepositoryAudit.ActorAsync(access, cancellationToken).ConfigureAwait(false)).Username;
            var started = DateTime.UtcNow;
            var kind = apply ? "apply" : "simulation";
            var settings = await import.GetSettingsAsync(cancellationToken).ConfigureAwait(false);
            if (apply && !settings.Enabled) throw new ActivityImportException("Preluarea este inactivă. Activeaz-o în setări înainte de aplicare.");
            var runId = await ImportStore.StartRunAsync(configuration, kind, started, actor, cancellationToken).ConfigureAwait(false);
            ImportReport report;
            try
            {
                progress?.Report("Se citește ActivityApp…");
                var loaded = await LoadAsync(settings.PageSize, null, cancellationToken).ConfigureAwait(false);
                report = apply
                    ? await ApplyPlanAsync(loaded, runId, actor, started, progress, cancellationToken).ConfigureAwait(false)
                    : Report(kind, started, actor, false, loaded.Partners.Select(plan => (plan.Action, Item(plan))), loaded.Points.Select(plan => (plan.Action, Item(plan))), null)
                        with { AnafFilled = loaded.Partners.Count(plan => plan.Action == PlanAction.Create && plan.Message.Contains(ActivityPartnerPlanner.AnafFilledPrefix, StringComparison.Ordinal)),
                               AnafUnverified = loaded.Partners.Count(plan => plan.Action == PlanAction.Create && plan.Check.Length > 0) };
            }
            catch (Exception exception)
            {
                await ImportStore.FinishRunAsync(configuration, runId, exception is OperationCanceledException ? "interrupted" : "failed", null, exception is OperationCanceledException ? "Rularea a fost întreruptă." : exception is ActivityImportException or ActivitySourceException ? exception.Message : "Eroare internă (" + exception.GetType().Name + ").").ConfigureAwait(false);
                throw;
            }
            if (apply) await ReverifyAsync(actor, cancellationToken).ConfigureAwait(false);
            await ImportStore.FinishRunAsync(configuration, runId, "completed", report, null).ConfigureAwait(false);
            await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.ActivityImport, apply ? AuditActions.ImportActivityPartners : AuditActions.SimulateActivityImport, runId.ToString(),
                apply ? "Import beneficiari și puncte de lucru" : "Simulare import beneficiari și puncte de lucru", Summary(report), "", cancellationToken).ConfigureAwait(false);
            return report;
        }
        finally { Gate.Release(); }
    }

    public async Task<ImportReport> ApplyForAsync(IReadOnlySet<long> partnerIds, CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync(Run, cancellationToken).ConfigureAwait(false);
        if (!MariaDatabaseGuard.IsAllowedDatabase(configuration)) throw new ActivityImportException("Modificările sunt permise numai în baza BlazorStoc. Verifică numele bazei configurate.");
        var actor = (await RepositoryAudit.ActorAsync(access, cancellationToken).ConfigureAwait(false)).Username;
        var started = DateTime.UtcNow;
        var settings = await import.GetSettingsAsync(cancellationToken).ConfigureAwait(false);
        if (!settings.Enabled) throw new ActivityImportException("Preluarea este inactivă. Activeaz-o în setări înainte de aplicare.");
        var loaded = await LoadAsync(settings.PageSize, partnerIds, cancellationToken).ConfigureAwait(false);
        var report = await ApplyPlanAsync(loaded, 0, actor, started, null, cancellationToken).ConfigureAwait(false);
        await ReverifyAsync(actor, cancellationToken).ConfigureAwait(false);
        return report;
    }

    // The beneficiaries taken while ANAF could not answer are asked again at every run: when ANAF confirms the CUI the beneficiary is marked as verified and its tickets are taken; when ANAF
    // says it does not know the CUI the reason of the check changes. Not more than the cap of lookups of a run.
    private async Task ReverifyAsync(string actor, CancellationToken cancellationToken)
    {
        if (lookup is null) return;
        var pending = (await ActivityPartnerCheckStore.ReadAsync(configuration, PartnerCheckStatus.Pending, cancellationToken).ConfigureAwait(false))
            .Where(row => row.Reason == BeneficiaryCheckReasons.AnafUnavailable && row.BeneficiaryId is not null).Take(MaximumAnafLookups).ToList();
        if (pending.Count == 0) return;
        var checks = new MariaBeneficiaryCheckService(configuration, access, audit, beneficiaries, lookup);
        foreach (var row in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var digits = SupplierRules.CuiDigits(row.Cui);
            if (digits.Length == 0) continue;
            CompanyLookupResult answer;
            try { answer = await lookup.LookupAsync(digits, cancellationToken).ConfigureAwait(false); }
            catch (Exception exception) when (exception is not OperationCanceledException) { break; }
            if (answer.Unavailable) break;
            if (answer.NotFound) await ActivityPartnerCheckStore.SetReasonAsync(configuration, row.Id, BeneficiaryCheckReasons.CuiUnknown, cancellationToken).ConfigureAwait(false);
            else await checks.MarkVerifiedAsync(row, actor, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string Summary(ImportReport report) => AuditDetails.Identification(
        ("Beneficiari", $"{report.Partners.Total}: {report.Partners.Create} creați, {report.Partners.Link} legați de existenți, {report.Partners.AlreadyLinked} deja legați, {report.Partners.Problems} probleme" + (report.AnafFilled > 0 ? $", {report.AnafFilled} completați din ANAF" : "") + (report.AnafUnverified > 0 ? $", {report.AnafUnverified} neverificați în ANAF" : "")),
        ("Puncte de lucru", $"{report.Points.Total}: {report.Points.Create} create, {report.Points.Link} legate, {report.Points.AlreadyLinked} deja legate, {report.Points.Problems} probleme, {report.Points.Skipped} ignorate"));

    private static ImportReportItem Item(PartnerPlan plan) => new(ActivityPartnerPlanner.PartnerEntity, plan.SourceId, plan.Name, plan.Action.ToString(), plan.Message);
    private static ImportReportItem Item(PointPlan plan) => new(ActivityPartnerPlanner.PointEntity, plan.SourceId, plan.Name, plan.Action.ToString(), plan.Message);

    private static ImportReport Report(string kind, DateTime at, string by, bool applied, IEnumerable<(PlanAction Action, ImportReportItem Item)> partners, IEnumerable<(PlanAction Action, ImportReportItem Item)> points, string? error)
    {
        var partnerList = partners.ToList();
        var pointList = points.ToList();
        var items = partnerList.Concat(pointList).Where(entry => entry.Action is PlanAction.Problem or PlanAction.Skipped).Select(entry => entry.Item).ToList();
        return new ImportReport(kind, at, by, applied, PlanCounts.Of(partnerList.Select(entry => entry.Action)), PlanCounts.Of(pointList.Select(entry => entry.Action)),
            [.. items.Take(MaximumItems)], items.Count > MaximumItems, error);
    }

    private async Task<Loaded> LoadAsync(int pageSize, IReadOnlySet<long>? only, CancellationToken cancellationToken)
    {
        var connection = await import.GetConnectionAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new ActivityImportException("Conexiunea la ActivityApp nu este completată (server, cont, parolă). Salveaz-o în setări.");
        var structure = ActivityStructureChecker.Check(await source.ReadStructureAsync(connection, cancellationToken).ConfigureAwait(false));
        foreach (var entity in new[] { ActivityContract.Partners, ActivityContract.WorkPoints })
            if (structure.IsBlocked(entity)) throw new ActivityImportException($"Structura ActivityApp s-a schimbat: „{entity}” nu poate fi preluat (vezi raportul de structură din setări).");
        // No client to take: the structure of the tables was still checked (a change stops the run), nothing else is read.
        if (only is { Count: 0 }) return new Loaded(structure, [], [], new Dictionary<int, IReadOnlyList<WorkPoint>>());
        var partners = await source.ReadPartnersAsync(connection, structure, pageSize, cancellationToken).ConfigureAwait(false);
        var points = await source.ReadWorkPointsAsync(connection, structure, pageSize, cancellationToken).ConfigureAwait(false);
        var existing = await beneficiaries.GetBeneficiariesAsync(cancellationToken).ConfigureAwait(false);
        var map = await ImportStore.ReadMapAsync(configuration, cancellationToken).ConfigureAwait(false);
        // Only the asked partners (the clients of the tickets) are verified and taken; the plan is made over all of them so that a CUI repeated in ActivityApp is still seen as repeated.
        var partnerPlans = ActivityPartnerPlanner.PlanPartners(partners, existing, map, await LookupAnafAsync(ActivityPartnerPlanner.NeedsAnaf(partners, existing, map, only), cancellationToken).ConfigureAwait(false));
        if (only is not null)
        {
            partnerPlans = [.. partnerPlans.Where(plan => only.Contains(plan.SourceId))];
            points = [.. points.Where(point => only.Contains(point.PartnerId))];
        }
        var locals = new Dictionary<int, IReadOnlyList<WorkPoint>>();
        foreach (var id in partnerPlans.Where(plan => plan.LocalId is not null).Select(plan => plan.LocalId!.Value).Distinct())
            locals[id] = await workPoints.GetAsync(id, cancellationToken).ConfigureAwait(false);
        var pointPlans = ActivityPartnerPlanner.PlanPoints(points, partnerPlans, map, locals);
        return new Loaded(structure, partnerPlans, pointPlans, locals);
    }

    // The companies that lack the address or the phone in ActivityApp are asked of ANAF (the same lookup as the form of a beneficiary). An answer that cannot be had is kept as the reason
    // of the problem; when ANAF is out, the rest are not asked.
    private async Task<IReadOnlyDictionary<string, CompanyData>> LookupAnafAsync(IReadOnlyList<string> cuis, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, CompanyData>(StringComparer.Ordinal);
        if (lookup is null || cuis.Count == 0) return result;
        string? outage = null;
        foreach (var cui in cuis.Take(MaximumAnafLookups))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (outage is not null) { result[cui] = new CompanyData("", "", outage); continue; }
            try
            {
                var answer = await lookup.LookupAsync(cui, cancellationToken).ConfigureAwait(false);
                result[cui] = new CompanyData(answer.Address, answer.Phone, answer.Failure, answer.NotFound);
                if (answer.Unavailable) outage = answer.Failure ?? "ANAF nu a răspuns";
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                outage = "interogarea ANAF a eșuat";
                result[cui] = new CompanyData("", "", outage);
            }
        }
        return result;
    }

    private async Task<ImportReport> ApplyPlanAsync(Loaded loaded, long runId, string actor, DateTime started, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var partnerResults = new List<(PartnerPlan Plan, PlanAction Action, string Message, int? Local)>();
        var mainOf = new Dictionary<long, int?>();
        var done = 0;
        foreach (var plan in loaded.Partners)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++done % 20 == 0) progress?.Report($"Beneficiari: {done} din {loaded.Partners.Count}…");
            switch (plan.Action)
            {
                case PlanAction.Create:
                    try
                    {
                        var created = await beneficiaries.CreateAsync(plan.Input!, cancellationToken).ConfigureAwait(false);
                        var main = (await workPoints.GetAsync(created.Id, cancellationToken).ConfigureAwait(false)).FirstOrDefault(point => point.IsPrimary);
                        mainOf[plan.SourceId] = main?.Id;
                        await ImportStore.WriteMapAsync(configuration, ActivityPartnerPlanner.PartnerEntity, plan.SourceId, created.Id, "created", plan.Hash, cancellationToken).ConfigureAwait(false);
                        if (plan.Check.Length > 0) await ActivityPartnerCheckStore.UpsertAsync(configuration, plan.SourceId, plan.Name, plan.CuiTyped, plan.Check, created.Id, cancellationToken).ConfigureAwait(false);
                        else await ActivityPartnerCheckStore.CloseAsync(configuration, plan.SourceId, PartnerCheckStatus.Resolved, actor, "preluat verificat", cancellationToken).ConfigureAwait(false);
                        partnerResults.Add((plan, PlanAction.Create, "", created.Id));
                    }
                    catch (BeneficiaryOperationException exception) { partnerResults.Add((plan, PlanAction.Problem, exception.Message, null)); }
                    break;
                case PlanAction.Link:
                    await ImportStore.WriteMapAsync(configuration, ActivityPartnerPlanner.PartnerEntity, plan.SourceId, plan.LocalId!.Value, "linked", plan.Hash, cancellationToken).ConfigureAwait(false);
                    partnerResults.Add((plan, PlanAction.Link, plan.Message, plan.LocalId));
                    break;
                case PlanAction.Problem when plan.Check.Length > 0:
                    // A company that cannot be made a beneficiary yet (no address): it waits in the queue of the partners to verify, with its tickets.
                    await ActivityPartnerCheckStore.UpsertAsync(configuration, plan.SourceId, plan.Name, plan.CuiTyped, plan.Check, null, cancellationToken).ConfigureAwait(false);
                    partnerResults.Add((plan, plan.Action, plan.Message, plan.LocalId));
                    break;
                case PlanAction.Skipped when plan.Message == ActivityPartnerPlanner.PersonNotTaken:
                    // A person is never taken: the tie (with no beneficiary) tells the ticket import that its tickets are not to be taken either.
                    await ImportStore.WriteMapAsync(configuration, ActivityPartnerPlanner.PartnerEntity, plan.SourceId, 0, "ignored", plan.Hash, cancellationToken).ConfigureAwait(false);
                    partnerResults.Add((plan, plan.Action, plan.Message, plan.LocalId));
                    break;
                default:
                    partnerResults.Add((plan, plan.Action, plan.Message, plan.LocalId));
                    break;
            }
        }
        var partnerLocal = partnerResults.Where(result => result.Local is not null).ToDictionary(result => result.Plan.SourceId, result => result.Local!.Value);
        var failedPartners = partnerResults.Where(result => result.Action == PlanAction.Problem).Select(result => result.Plan.SourceId).ToHashSet();
        var pointResults = new List<(PointPlan Plan, PlanAction Action, string Message)>();
        done = 0;
        foreach (var plan in loaded.Points)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++done % 20 == 0) progress?.Report($"Puncte de lucru: {done} din {loaded.Points.Count}…");
            if (failedPartners.Contains(plan.PartnerId)) { pointResults.Add((plan, PlanAction.Skipped, "Beneficiarul nu a putut fi creat; punctul se preia după ce se rezolvă.")); continue; }
            switch (plan.Action)
            {
                case PlanAction.Create:
                    try
                    {
                        var created = await workPoints.CreateAsync(partnerLocal[plan.PartnerId], plan.Input!, cancellationToken).ConfigureAwait(false);
                        await ImportStore.WriteMapAsync(configuration, ActivityPartnerPlanner.PointEntity, plan.SourceId, created.Id, "created", plan.Hash, cancellationToken).ConfigureAwait(false);
                        pointResults.Add((plan, PlanAction.Create, ""));
                    }
                    catch (WorkPointOperationException exception) { pointResults.Add((plan, PlanAction.Problem, exception.Message)); }
                    break;
                case PlanAction.Link:
                    var target = plan.LocalId ?? (mainOf.TryGetValue(plan.PartnerId, out var main) ? main : null);
                    if (target is null) { pointResults.Add((plan, PlanAction.Problem, "Punctul principal al beneficiarului nu a fost găsit.")); break; }
                    await ImportStore.WriteMapAsync(configuration, ActivityPartnerPlanner.PointEntity, plan.SourceId, target.Value, "linked", plan.Hash, cancellationToken).ConfigureAwait(false);
                    pointResults.Add((plan, PlanAction.Link, plan.Message));
                    break;
                default:
                    pointResults.Add((plan, plan.Action, plan.Message));
                    break;
            }
        }
        var report = Report("apply", started, actor, true,
            partnerResults.Select(result => (result.Action, new ImportReportItem(ActivityPartnerPlanner.PartnerEntity, result.Plan.SourceId, result.Plan.Name, result.Action.ToString(), result.Message))),
            pointResults.Select(result => (result.Action, new ImportReportItem(ActivityPartnerPlanner.PointEntity, result.Plan.SourceId, result.Plan.Name, result.Action.ToString(), result.Message))), null);
        report = report with
        {
            AnafFilled = partnerResults.Count(result => result.Action == PlanAction.Create && result.Plan.Message.Contains(ActivityPartnerPlanner.AnafFilledPrefix, StringComparison.Ordinal)),
            AnafUnverified = partnerResults.Count(result => result.Action == PlanAction.Create && result.Plan.Check.Length > 0)
        };
        // A run for a few partners (the clients of the tickets) must not clear the open problems of the others.
        if (runId > 0) await ImportStore.RecordIssuesAsync(configuration, runId, [ActivityPartnerPlanner.PartnerEntity, ActivityPartnerPlanner.PointEntity], partnerResults.Where(result => result.Action == PlanAction.Problem).Select(result => (ActivityPartnerPlanner.PartnerEntity, result.Plan.SourceId, result.Plan.Name, result.Message))
            .Concat(pointResults.Where(result => result.Action == PlanAction.Problem).Select(result => (ActivityPartnerPlanner.PointEntity, result.Plan.SourceId, result.Plan.Name, result.Message))), cancellationToken).ConfigureAwait(false);
        return report;
    }

    public async Task<ImportRunInfo?> GetLastRunAsync(CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync(View, cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("SELECT id, kind, started_utc, finished_utc, started_by, status, report FROM activity_import_runs ORDER BY id DESC LIMIT 1", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        ImportReport? report = null;
        if (!reader.IsDBNull(6))
            try { report = JsonSerializer.Deserialize<ImportReport>(reader.GetString(6)); } catch (JsonException) { report = null; }
        return new ImportRunInfo(reader.GetInt64(0), reader.GetString(1), DateTime.SpecifyKind(reader.GetDateTime(2), DateTimeKind.Utc),
            reader.IsDBNull(3) ? null : DateTime.SpecifyKind(reader.GetDateTime(3), DateTimeKind.Utc), reader.GetString(4), reader.GetString(5), report?.Kind is null ? null : report);
    }

    public async Task<IReadOnlyList<ImportIssue>> GetOpenIssuesAsync(CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync(View, cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("SELECT id, run_id, entity, source_id, source_name, message, created_utc FROM activity_import_issues WHERE resolved = 0 ORDER BY entity, source_id LIMIT 500", connection);
        var result = new List<ImportIssue>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new ImportIssue(reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), reader.GetInt64(3), reader.GetString(4), reader.GetString(5), DateTime.SpecifyKind(reader.GetDateTime(6), DateTimeKind.Utc)));
        return result;
    }

    public async Task ResolveIssueAsync(long issueId, CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync(Run, cancellationToken).ConfigureAwait(false);
        var actor = (await RepositoryAudit.ActorAsync(access, cancellationToken).ConfigureAwait(false)).Username;
        string? entity = null, name = null, message = null;
        await using (var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false))
        {
            await using (var read = new MySqlCommand("SELECT entity, source_name, message FROM activity_import_issues WHERE id=@id AND resolved=0", connection))
            {
                read.Parameters.AddWithValue("@id", issueId);
                await using var reader = await read.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) (entity, name, message) = (reader.GetString(0), reader.GetString(1), reader.GetString(2));
            }
            if (entity is null) throw new ActivityImportException("Problema nu mai există sau a fost deja rezolvată.");
            await using var update = new MySqlCommand("UPDATE activity_import_issues SET resolved=1, resolved_by=@by, resolved_utc=@now WHERE id=@id AND resolved=0", connection);
            update.Parameters.AddWithValue("@by", actor);
            update.Parameters.AddWithValue("@now", DateTime.UtcNow);
            update.Parameters.AddWithValue("@id", issueId);
            await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.ActivityImport, AuditActions.ResolveActivityImportIssue, issueId.ToString(), $"{(entity == ActivityPartnerPlanner.PartnerEntity ? "Partener" : "Punct de lucru")} «{name}»",
            AuditDetails.Identification(("Problemă", message ?? "")), "", cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class GuardedActivityPartnerImportService(IActivityPartnerImportService inner, ModuleRegistry modules) : IActivityPartnerImportService
{
    private const string Id = ActivityImportModule.ModuleId;
    public Task<ImportReport> SimulateAsync(CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.SimulateAsync(cancellationToken));
    public Task<ImportReport> ApplyAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.ApplyAsync(progress, cancellationToken));
    public Task<ImportReport> ApplyForAsync(IReadOnlySet<long> partnerIds, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.ApplyForAsync(partnerIds, cancellationToken));
    public Task<ImportRunInfo?> GetLastRunAsync(CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.GetLastRunAsync(cancellationToken));
    public Task<IReadOnlyList<ImportIssue>> GetOpenIssuesAsync(CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.GetOpenIssuesAsync(cancellationToken));
    public Task ResolveIssueAsync(long issueId, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.ResolveIssueAsync(issueId, cancellationToken));
}

internal sealed class UnavailableActivityPartnerImportService : IActivityPartnerImportService
{
    private static Task<T> Out<T>() => Task.FromException<T>(new ModuleUnavailableException(ActivityImportModule.ModuleId));
    public Task<ImportReport> SimulateAsync(CancellationToken cancellationToken = default) => Out<ImportReport>();
    public Task<ImportReport> ApplyAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default) => Out<ImportReport>();
    public Task<ImportReport> ApplyForAsync(IReadOnlySet<long> partnerIds, CancellationToken cancellationToken = default) => Out<ImportReport>();
    public Task<ImportRunInfo?> GetLastRunAsync(CancellationToken cancellationToken = default) => Out<ImportRunInfo?>();
    public Task<IReadOnlyList<ImportIssue>> GetOpenIssuesAsync(CancellationToken cancellationToken = default) => Out<IReadOnlyList<ImportIssue>>();
    public Task ResolveIssueAsync(long issueId, CancellationToken cancellationToken = default) => Out<object>();
}
