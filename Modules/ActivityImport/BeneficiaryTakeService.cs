using System.Text.RegularExpressions;
using BlazorStoc.Services;

namespace BlazorStoc.Modules.ActivityImport;

// "Preia din ActivityApp" (see IBeneficiaryTakeService). The right is beneficiari.add of the signed-in user; the reading and the writing run as the system in a scope of their own (the user
// may lack the rights of the work points or of the import settings, which the taking needs), and the journal entry carries the name of the user. The stages come from the partner import as
// text; "Beneficiari: 40 din 120…" becomes a percentage.
public sealed partial class BeneficiaryTakeService(IServiceScopeFactory scopes, ModuleRegistry modules, IAccessControl access, IAuditTrail audit) : IBeneficiaryTakeService
{
    private const string Right = "beneficiari.add";

    public async Task<bool> CanTakeAsync(CancellationToken cancellationToken = default)
    {
        if (!modules.IsAvailable(ActivityImportModule.ModuleId)) return false;
        try
        {
            if (!await access.HasAsync(Right, cancellationToken).ConfigureAwait(false)) return false;
            using var scope = scopes.CreateScope();
            var (import, _) = ActivityImportSystem.CreatePartnerServices(scope.ServiceProvider);
            var settings = await import.GetSettingsAsync(cancellationToken).ConfigureAwait(false);
            return settings.Enabled && settings.TakePartners;
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { return false; }
    }

    public async Task<PartnerTakeResult> TakeAsync(IProgress<PartnerTakeProgress>? progress, CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync(Right, cancellationToken).ConfigureAwait(false);
        if (!modules.IsAvailable(ActivityImportModule.ModuleId)) return Failed("Preluarea din ActivityApp nu este disponibilă acum.");
        using var scope = scopes.CreateScope();
        var (import, partners) = ActivityImportSystem.CreatePartnerServices(scope.ServiceProvider);
        var settings = await import.GetSettingsAsync(cancellationToken).ConfigureAwait(false);
        if (!settings.Enabled || !settings.TakePartners) return Failed("Preluarea beneficiarilor din ActivityApp este oprită în setări.");
        var stages = new Progress<string>(text => progress?.Report(Parse(text)));
        progress?.Report(new PartnerTakeProgress("Se pornește preluarea…", 0, 0));
        ImportReport? report;
        try { report = await partners.TakeNewAsync(stages, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is ActivityImportException or ActivitySourceException)
        {
            return Failed(exception.Message);
        }
        if (report is null) return Failed("Altă preluare era în curs și nu s-a încheiat la timp. Încearcă din nou peste câteva minute.");
        var created = report.Items.Where(item => item.Entity == ActivityPartnerPlanner.PartnerEntity && item.Action == nameof(PlanAction.Create)).Select(item => item.Name).ToList();
        var result = new PartnerTakeResult(true, report.Partners.Create, report.Partners.Link, report.Partners.Waiting, report.Partners.Problems, created, null);
        await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.ActivityImport, AuditActions.TakeActivityPartnersManual, "1", "Preluare beneficiari din ActivityApp la cerere",
            AuditDetails.Identification(("Beneficiari creați", result.Created.ToString()), ("Legați de existenți", result.Linked.ToString()), ("În așteptare", result.Waiting.ToString()), ("Probleme", result.Problems.ToString()),
                ("Beneficiari noi", created.Count == 0 ? "-" : string.Join(", ", created.Take(20)) + (created.Count > 20 ? $" și încă {created.Count - 20}" : ""))), "", cancellationToken).ConfigureAwait(false);
        progress?.Report(new PartnerTakeProgress("Preluarea s-a încheiat.", 1, 1));
        return result;
    }

    private static PartnerTakeResult Failed(string message) => new(false, 0, 0, 0, 0, [], message);

    private static PartnerTakeProgress Parse(string text) =>
        CountPattern().Match(text) is { Success: true } match && int.TryParse(match.Groups[1].Value, out var done) && int.TryParse(match.Groups[2].Value, out var total)
            ? new PartnerTakeProgress(text, done, total)
            : new PartnerTakeProgress(text, 0, 0);

    [GeneratedRegex(@"(\d+) din (\d+)(…)?")]
    private static partial Regex CountPattern();
}
