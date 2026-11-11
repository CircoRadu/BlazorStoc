using System.Collections.Concurrent;
using BlazorStoc.Services;
using Microsoft.AspNetCore.DataProtection;

namespace BlazorStoc.Modules.EspImport;

// The stock import from the old ESPStoc (Setari -> Preluare stoc ESPStoc, administrator only): analysis of an export (nothing is written), settling of the parameters of the products that go in
// subcategories with parameters, and the application, which is the point zero of the stock keeping (EspPointZero.cs).

/// <summary>One choice made in the screen for a product: the slot, the parameter (a member of the family) and the value (an existing one, or a typed new one).</summary>
public sealed record EspPick(int SlotIndex, int ParameterId, int? ValueId, string? Text);

public sealed record EspApplyResult(bool Success, bool Warnings, string Status, IReadOnlyList<string> Log, string Message);

public interface IEspImportService
{
    /// <summary>Saves an uploaded export in a temporary file (deleted after the analysis) and returns its path.</summary>
    Task<string> SaveUploadAsync(Stream body, string fileName, CancellationToken cancellationToken = default);
    /// <summary>Reads the export and builds the plan against the catalog of BlazorStoc. Replaces the analysis of the user. A temporary file is deleted after reading.</summary>
    Task<EspAnalysis> AnalyzeAsync(string path, string sourceName, bool deleteAfter, CancellationToken cancellationToken = default);
    Task<EspAnalysis?> GetCurrentAsync(CancellationToken cancellationToken = default);
    Task DiscardAsync(CancellationToken cancellationToken = default);
    /// <summary>Reads the catalog again (after parameters were added or changed) and recomputes the plan, keeping the choices made.</summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);
    Task SetOmitAsync(int oldId, bool omit, CancellationToken cancellationToken = default);
    Task ChooseAsync(int oldId, string baseModel, IReadOnlyList<EspPick> picks, CancellationToken cancellationToken = default);
    /// <summary>The product is taken without parameters: frozen in BlazorStoc (no movement) until it is completed.</summary>
    Task SetFrozenAsync(int oldId, bool frozen, CancellationToken cancellationToken = default);
    /// <summary>Takes frozen every product whose parameters are not settled; returns how many.</summary>
    Task<int> FreezeUnresolvedAsync(CancellationToken cancellationToken = default);
    Task<EspPointZeroPreview> PreviewAsync(CancellationToken cancellationToken = default);
    /// <summary>The state of the point zero operation (null = never started).</summary>
    Task<EspPointZeroState?> GetStateAsync(CancellationToken cancellationToken = default);
    Task<EspApplyResult> ApplyAsync(string confirmation, IProgress<string>? progress, CancellationToken cancellationToken = default);
}

/// <summary>The analysis of each user, kept while the application runs (the export is read once; a page reload finds it again).</summary>
public sealed class EspAnalysisStore
{
    private readonly ConcurrentDictionary<string, EspAnalysis> analyses = new(StringComparer.Ordinal);
    public EspAnalysis? Get(string user) => analyses.TryGetValue(user, out var analysis) ? analysis : null;
    public void Set(string user, EspAnalysis analysis) => analyses[user] = analysis;
    public void Remove(string user) => analyses.TryRemove(user, out _);

    // One point zero operation at a time in the whole application.
    private int running;
    public bool IsRunning => Volatile.Read(ref running) == 1;
    public bool TryBegin() => Interlocked.CompareExchange(ref running, 1, 0) == 0;
    public void End() => Volatile.Write(ref running, 0);
}

public sealed class MariaEspImportService(IConfiguration configuration, IAccessControl access, IAuditTrail? audit, IProductRepository products, IProductParameterRepository parameters,
    IStockMovementRepository movements, IProductImageStore images, IBackupOperations backupOperations, IOperationLockService locks, IDataProtectionProvider protection,
    ILoggerFactory loggers, EspAnalysisStore store) : IEspImportService
{
    private const string View = "setari-import-espstoc.view";
    private const string Run = "setari-import-espstoc.run";
    public const string ConfirmationText = "PUNCT ZERO";
    private readonly EspPointZeroStore point = new(configuration);
    private readonly ILogger logger = loggers.CreateLogger<MariaEspImportService>();

    private async Task<string> UserAsync(CancellationToken token) => await access.GetUsernameAsync(token).ConfigureAwait(false) ?? "necunoscut";

    public async Task<string> SaveUploadAsync(Stream body, string fileName, CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync(Run, cancellationToken).ConfigureAwait(false);
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension is not (".sql" or ".zip")) throw new EspImportOperationException("Se acceptă un fișier .sql sau un .zip care conține exportul .sql.");
        var folder = Path.Combine(Path.GetTempPath(), "blazorstoc-espimport");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, Guid.NewGuid().ToString("N") + extension);
        await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true))
            await body.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
        return path;
    }

    private async Task<EspCatalog> LoadCatalogAsync(CancellationToken token) => new()
    {
        Products = await products.GetProductsAsync(token).ConfigureAwait(false),
        Groups = await products.GetGroupsAsync(token).ConfigureAwait(false),
        Parameters = await parameters.GetParametersAsync(token).ConfigureAwait(false),
        Families = await parameters.GetFamiliesAsync(token).ConfigureAwait(false),
        BaseModels = await parameters.GetBaseModelsAsync(token).ConfigureAwait(false),
        Choices = await parameters.GetAllChoicesAsync(token).ConfigureAwait(false),
        Incomplete = await parameters.GetIncompleteProductIdsAsync(token).ConfigureAwait(false)
    };

    public async Task<EspAnalysis> AnalyzeAsync(string path, string sourceName, bool deleteAfter, CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync(Run, cancellationToken).ConfigureAwait(false);
        var user = await UserAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await point.ReadStateAsync(cancellationToken).ConfigureAwait(false);
            if (EspPointZeroStatus.Finished(state?.Status))
                throw new EspImportOperationException($"Punctul zero al gestiunii a fost deja stabilit la {state!.FinishedUtc?.ToLocalTime():dd.MM.yyyy HH:mm}. Un nou import se poate face numai după restaurarea unui pachet de siguranță.");
            var export = await EspExportReader.ReadAsync(path, sourceName, cancellationToken).ConfigureAwait(false);
            var catalog = await LoadCatalogAsync(cancellationToken).ConfigureAwait(false);
            var analysis = EspPlanner.Build(export, catalog, Guid.NewGuid().ToString("N"), DateTime.UtcNow);
            store.Set(user, analysis);
            await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.EspImport, AuditActions.EspAnalyze, "", sourceName,
                AuditDetails.Identification(("Fișier", sourceName), ("Produse cu stoc în export", export.Products.Count.ToString()), ("Produse fără stoc (nu se preiau)", export.ZeroProducts.ToString()),
                    ("Bucăți în export", export.Products.Sum(product => product.Quantity).ToString())), "", cancellationToken).ConfigureAwait(false);
            return analysis;
        }
        finally
        {
            if (deleteAfter) { try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }
    }

    public async Task<EspAnalysis?> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync(View, cancellationToken).ConfigureAwait(false);
        return store.Get(await UserAsync(cancellationToken).ConfigureAwait(false));
    }

    public async Task DiscardAsync(CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync(Run, cancellationToken).ConfigureAwait(false);
        store.Remove(await UserAsync(cancellationToken).ConfigureAwait(false));
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync(Run, cancellationToken).ConfigureAwait(false);
        var analysis = store.Get(await UserAsync(cancellationToken).ConfigureAwait(false)) ?? throw new EspImportOperationException("Nu există o analiză. Alege un export.");
        analysis.Catalog = await LoadCatalogAsync(cancellationToken).ConfigureAwait(false);
        Recompute(analysis, keepChoices: true);
    }

    // The targets, slots and existing products are computed again from the live catalog; the choices made by the user (and the omitted products) are kept when they still fit.
    private static void Recompute(EspAnalysis analysis, bool keepChoices)
    {
        var previous = analysis.Items.ToDictionary(item => item.OldId, item => (item.BaseModel, item.Choices, item.Proposed, item.Omit, item.Frozen));
        var rebuilt = EspPlanner.Build(analysis.Export, analysis.Catalog, analysis.Id, analysis.CreatedUtc);
        foreach (var item in rebuilt.Items)
        {
            if (keepChoices && previous.TryGetValue(item.OldId, out var old) && !old.Proposed && old.Choices.Count > 0 && item.Slots.Count > 0)
            {
                item.BaseModel = old.BaseModel;
                item.Choices = old.Choices.Where(pair => pair.Key < item.Slots.Count && item.Slots[pair.Key].Members.Any(member => member.Id == pair.Value.ParameterId)).ToDictionary(pair => pair.Key, pair => pair.Value);
                item.Proposed = false;
            }
            if (keepChoices && previous.TryGetValue(item.OldId, out var omitted)) { item.Omit = omitted.Omit; item.Frozen = omitted.Frozen; }
        }
        foreach (var item in rebuilt.Items) EspPlanner.Evaluate(item, analysis.Catalog);
        EspPlanner.CheckDuplicates(rebuilt.Items);
        analysis.Items.Clear();
        analysis.Items.AddRange(rebuilt.Items);
    }

    private async Task<(EspAnalysis Analysis, EspPlanItem Item)> FindAsync(int oldId, CancellationToken token)
    {
        await access.EnsureAsync(Run, token).ConfigureAwait(false);
        var analysis = store.Get(await UserAsync(token).ConfigureAwait(false)) ?? throw new EspImportOperationException("Nu există o analiză. Alege un export.");
        return (analysis, analysis.Items.FirstOrDefault(item => item.OldId == oldId) ?? throw new EspImportOperationException("Produsul nu mai este în analiză."));
    }

    private static void Reevaluate(EspAnalysis analysis)
    {
        foreach (var item in analysis.Items) EspPlanner.Evaluate(item, analysis.Catalog);
        EspPlanner.CheckDuplicates(analysis.Items);
    }

    public async Task SetOmitAsync(int oldId, bool omit, CancellationToken cancellationToken = default)
    {
        var (analysis, item) = await FindAsync(oldId, cancellationToken).ConfigureAwait(false);
        item.Omit = omit;
        Reevaluate(analysis);
    }

    public async Task ChooseAsync(int oldId, string baseModel, IReadOnlyList<EspPick> picks, CancellationToken cancellationToken = default)
    {
        var (analysis, item) = await FindAsync(oldId, cancellationToken).ConfigureAwait(false);
        item.BaseModel = baseModel;
        item.Choices = [];
        foreach (var pick in picks)
        {
            if (pick.SlotIndex < 0 || pick.SlotIndex >= item.Slots.Count || item.Slots[pick.SlotIndex].Members.FirstOrDefault(member => member.Id == pick.ParameterId) is not { } parameter) continue;
            if (pick.ValueId is { } valueId && parameter.Values.Any(value => value.Id == valueId)) { item.Choices[pick.SlotIndex] = new(parameter.Id, valueId, null); continue; }
            if (string.IsNullOrWhiteSpace(pick.Text)) continue;
            try
            {
                var text = ProductParameterRules.Value(parameter.Kind, parameter.Name, pick.Text);
                var existing = parameter.Values.FirstOrDefault(value => string.Equals(value.Value, text, StringComparison.Ordinal));
                item.Choices[pick.SlotIndex] = existing is null ? new(parameter.Id, null, text) : new(parameter.Id, existing.Id, null);
            }
            catch (ProductOperationException exception) { throw new EspImportOperationException(exception.Message); }
        }
        item.Proposed = false;
        item.SuggestNote = "";
        item.Frozen = false;
        Reevaluate(analysis);
    }

    public async Task SetFrozenAsync(int oldId, bool frozen, CancellationToken cancellationToken = default)
    {
        var (analysis, item) = await FindAsync(oldId, cancellationToken).ConfigureAwait(false);
        item.Frozen = frozen;
        Reevaluate(analysis);
    }

    public async Task<int> FreezeUnresolvedAsync(CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync(Run, cancellationToken).ConfigureAwait(false);
        var analysis = store.Get(await UserAsync(cancellationToken).ConfigureAwait(false)) ?? throw new EspImportOperationException("Nu există o analiză. Alege un export.");
        var count = 0;
        foreach (var item in analysis.Items.Where(item => item.HasProblems && item.Slots.Count > 0 && !item.Frozen)) { item.Frozen = true; count++; }
        Reevaluate(analysis);
        return count;
    }

    // ---- the backup packages

    private string BackupDirectory => MariaAssetPaths.DatabaseBackups(configuration);

    private async Task<IReadOnlyList<EspBackupFile>> LocalPackagesAsync(CancellationToken token) =>
        [.. (await BackupPackageStore.ListAsync(BackupDirectory, token).ConfigureAwait(false)).OrderBy(package => package.CreatedAtUtc).Select(package => new EspBackupFile(package.FileName, package.CreatedAtUtc, package.SizeBytes))];

    private NasBackupCopier Nas(IAccessControl actor) => new(configuration, protection, actor, audit, loggers.CreateLogger<NasBackupCopier>());

    private async Task<(IReadOnlyList<EspBackupFile> Files, string? Note)> NasPackagesAsync(CancellationToken token)
    {
        try
        {
            var listing = await Nas(access).ListShareAsync(token).WaitAsync(TimeSpan.FromSeconds(60), token).ConfigureAwait(false);
            if (!listing.Ok) return ([], string.IsNullOrWhiteSpace(listing.Message) ? "NAS-ul nu este folosit." : listing.Message);
            return ([.. listing.Files.OrderBy(file => file.ModifiedUtc).Select(file => new EspBackupFile(file.FileName, file.ModifiedUtc, file.SizeBytes))], null);
        }
        catch (TimeoutException) { return ([], "NAS-ul nu a răspuns în 60 de secunde; copiile de pe NAS nu se pot șterge acum."); }
        catch (Exception exception) when (exception is not OperationCanceledException) { return ([], "NAS-ul nu a putut fi citit: " + exception.Message); }
    }

    public async Task<EspPointZeroPreview> PreviewAsync(CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync(View, cancellationToken).ConfigureAwait(false);
        var (counts, pieces, withStock, events, first, last) = await point.CountAsync(cancellationToken).ConfigureAwait(false);
        var local = await LocalPackagesAsync(cancellationToken).ConfigureAwait(false);
        var (nas, note) = await NasPackagesAsync(cancellationToken).ConfigureAwait(false);
        return new(counts, pieces, withStock, events, first, last, local, nas, note, await point.ReadStateAsync(cancellationToken).ConfigureAwait(false));
    }

    public async Task<EspPointZeroState?> GetStateAsync(CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync(View, cancellationToken).ConfigureAwait(false);
        var state = await point.ReadStateAsync(cancellationToken).ConfigureAwait(false);
        if (state is not null) state.Running = store.IsRunning;
        return state;
    }

    // ---- the application

    public async Task<EspApplyResult> ApplyAsync(string confirmation, IProgress<string>? progress, CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync(Run, cancellationToken).ConfigureAwait(false);
        await access.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        if (!string.Equals(confirmation?.Trim(), ConfirmationText, StringComparison.Ordinal)) throw new EspImportOperationException($"Scrie {ConfirmationText} pentru a confirma.");
        if (!store.TryBegin()) throw new EspImportOperationException("O operațiune de punct zero rulează deja. Așteaptă să se încheie.");
        try { return await ApplyCoreAsync(progress).ConfigureAwait(false); }
        finally { store.End(); }
    }

    private async Task<EspApplyResult> ApplyCoreAsync(IProgress<string>? progress)
    {
        var cancellationToken = CancellationToken.None;
        var user = await UserAsync(cancellationToken).ConfigureAwait(false);
        var analysis = store.Get(user) ?? throw new EspImportOperationException("Nu există o analiză. Alege un export.");
        var log = new List<string>();
        EspPointZeroState? state = null;
        // The log goes with the state (saved at each step), so the page that finds the operation running or finished again (the other sessions are sent to the maintenance page while the
        // operation holds the lock) shows what was done.
        void Say(string message)
        {
            log.Add($"{DateTime.Now:HH:mm:ss} {message}");
            if (state is not null) state.Log = [.. log];
            try { progress?.Report(message); } catch (Exception exception) when (exception is ObjectDisposedException or InvalidOperationException) { }
        }

        state = await point.ReadStateAsync(cancellationToken).ConfigureAwait(false);
        if (EspPointZeroStatus.Finished(state?.Status)) throw new EspImportOperationException("Punctul zero al gestiunii a fost deja stabilit.");
        var resume = state is { Status: EspPointZeroStatus.Deleted or EspPointZeroStatus.ImportErrors or EspPointZeroStatus.VerifyFailed or EspPointZeroStatus.Verified };

        // The operation runs to its end even when the browser closes: only the stages before the first change are cancellable.
        var token = CancellationToken.None;
        try
        {
            Say("Se citește catalogul curent.");
            analysis.Catalog = await LoadCatalogAsync(token).ConfigureAwait(false);
            Recompute(analysis, keepChoices: true);
            if (!analysis.CanApply) throw new EspImportOperationException("Planul are încă probleme nerezolvate sau nu are niciun produs de preluat.");
            var role = await access.IsAdministratorAsync(token).ConfigureAwait(false) ? AccessRoles.Administrator : AccessRoles.LimitedUser;

            if (!resume)
            {
                state = new EspPointZeroState { Status = EspPointZeroStatus.Started, StartedUtc = DateTime.UtcNow, Operator = user, Source = analysis.SourceName, Products = analysis.TakenProducts, Pieces = analysis.TakenPieces };
                state.LocalToDelete = [.. (await LocalPackagesAsync(token).ConfigureAwait(false)).Select(file => file.Name)];
                var (nasFiles, nasNote) = await NasPackagesAsync(token).ConfigureAwait(false);
                state.NasToDelete = [.. nasFiles.Select(file => file.Name)];
                if (nasNote is not null) Say("NAS: " + nasNote);
                Say($"Se șterg la sfârșit {state.LocalToDelete.Count} pachete locale și {state.NasToDelete.Count} de pe NAS.");
                Say("Se face pachetul de siguranță înainte de orice ștergere.");
                var safety = await backupOperations.Backups(access).CreateBackupAsync(BackupKind.Manual, new Progress<BackupProgress>(item => Say(item.Message)), token).ConfigureAwait(false);
                if (!safety.Success) throw new EspImportOperationException("Pachetul de siguranță nu s-a putut face, deci nu s-a schimbat nimic: " + safety.ErrorMessage);
                state.SafetyPackage = safety.PackageFileName;
                // The safety package is on the share too when the NAS copy is on (the copy follows every backup); a share that is not used has nothing to delete.
                if (safety.PackageFileName is not null) { state.LocalToDelete.Add(safety.PackageFileName); if (nasNote is null) state.NasToDelete.Add(safety.PackageFileName); }
                await point.WriteStateAsync(state, token).ConfigureAwait(false);
                Say("Pachetul de siguranță este gata: " + safety.PackageFileName);
            }
            else Say($"Se reia operațiunea începută la {state!.StartedUtc.ToLocalTime():dd.MM.yyyy HH:mm} (stare: {state.Status}).");

            var verified = await LockedPhaseAsync(analysis, state!, resume, user, role, Say, token).ConfigureAwait(false);
            if (!verified) return new(false, false, state!.Status, log, "Preluarea nu s-a încheiat curat. Vezi jurnalul operațiunii; operațiunea se poate relua după corectare sau se poate restaura pachetul de siguranță.");

            // Out of the lock: the new package is made by the backup process, which takes the lock itself.
            var warnings = new List<string>();
            Say("Se face pachetul nou, „punct de pornire”.");
            var start = await backupOperations.Backups(access).CreateBackupAsync(BackupKind.Manual, new Progress<BackupProgress>(item => Say(item.Message)), token).ConfigureAwait(false);
            long journalDeleted = 0;
            int localDeleted = 0, nasDeleted = 0;
            if (!start.Success)
            {
                warnings.Add("Pachetul „punct de pornire” nu s-a putut face: " + start.ErrorMessage + " Copiile vechi NU au fost șterse.");
                Say(warnings[^1]);
            }
            else
            {
                state!.StartPackage = start.PackageFileName;
                Say("Se șterg copiile de siguranță vechi.");
                foreach (var name in state.LocalToDelete.Where(name => name != start.PackageFileName))
                {
                    try
                    {
                        var path = Path.Combine(BackupDirectory, name);
                        if (name == Path.GetFileName(name) && File.Exists(path)) { File.Delete(path); localDeleted++; }
                        if (File.Exists(path + ".sha256")) File.Delete(path + ".sha256");
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { warnings.Add($"Pachetul {name} nu a putut fi șters: {exception.Message}"); }
                }
                if (state.NasToDelete.Count > 0)
                {
                    try
                    {
                        var result = await Nas(access).DeleteFilesAsync([.. state.NasToDelete.Where(name => name != start.PackageFileName)], token).WaitAsync(TimeSpan.FromSeconds(120), token).ConfigureAwait(false);
                        nasDeleted = result.Copied;
                        if (!result.Ok) warnings.Add("Pe NAS: " + result.Message + " Fișierele rămase se șterg de mână.");
                    }
                    catch (TimeoutException) { warnings.Add("NAS-ul nu a răspuns la ștergere; copiile de pe NAS rămân."); }
                    catch (Exception exception) when (exception is not OperationCanceledException) { warnings.Add("Pe NAS: " + exception.Message); }
                }
                Say($"Șterse: {localDeleted} pachete locale, {nasDeleted} fișiere pe NAS.");
            }

            Say("Se golește jurnalul de acțiuni.");
            journalDeleted = await point.PurgeJournalAsync(token).ConfigureAwait(false);
            state!.FinishedUtc = DateTime.UtcNow;
            state.Status = warnings.Count == 0 ? EspPointZeroStatus.Done : EspPointZeroStatus.DoneWithWarnings;
            foreach (var warning in warnings) Say("ATENȚIE: " + warning);
            Say(state.Status == EspPointZeroStatus.Done ? "Punctul zero este stabilit." : "Punctul zero este stabilit, cu avertismente.");
            await point.WriteStateAsync(state, token).ConfigureAwait(false);
            await WriteJournalAsync(state, analysis, journalDeleted, localDeleted, nasDeleted, warnings, token).ConfigureAwait(false);
            store.Remove(user);
            return new(true, warnings.Count > 0, state.Status, log, warnings.Count == 0 ? "Punctul zero este stabilit." : "Punctul zero este stabilit, cu avertismente.");
        }
        catch (OperationCanceledException) { throw; }
        catch (EspImportOperationException) { throw; }
        catch (Exception exception) when (exception is not MaintenanceInProgressException)
        {
            logger.LogError("The stock import stopped ({ErrorType}).", exception.GetType().Name);
            await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.EspImport, AuditActions.EspFail, "", analysis.SourceName,
                AuditDetails.Identification(("Eroare", exception.GetType().Name + ": " + exception.Message)), "", CancellationToken.None).ConfigureAwait(false);
            throw new EspImportOperationException("Operațiunea s-a oprit: " + exception.Message);
        }
    }

    // The part that holds the shared operation lock (every other session is refused meanwhile): deletion, import, verification.
    private async Task<bool> LockedPhaseAsync(EspAnalysis analysis, EspPointZeroState state, bool resume, string user, string role, Action<string> say, CancellationToken token)
    {
        await using var handle = await locks.TryAcquireAsync("punct-zero", user, role, token).ConfigureAwait(false);
        if (handle is null) throw new EspImportOperationException(OperationLockRules.HeldMessage);
        using var maintenance = MaintenanceGate.EnterOwnerScope();

        if (!resume)
        {
            say("Se șterg mișcările, facturile preluate, ofertele și rezervările și se resetează cantitățile.");
            state.Deleted = await point.DeleteAsync(token).ConfigureAwait(false);
            state.Status = EspPointZeroStatus.Deleted;
            await point.WriteStateAsync(state, token).ConfigureAwait(false);
            say($"Șterse: {state.Deleted.GetValueOrDefault("stock_movements")} mișcări, {state.Deleted.GetValueOrDefault("supplier_invoices")} facturi, {state.Deleted.GetValueOrDefault("offers")} oferte, {state.Deleted.GetValueOrDefault("project_reservations")} rezervări.");
        }

        say("Se preiau produsele, imaginile și stocul inițial.");
        await point.WriteStateAsync(state, token).ConfigureAwait(false);
        var failures = await ImportAsync(analysis, say, token).ConfigureAwait(false);
        if (failures.Count > 0)
        {
            state.Status = EspPointZeroStatus.ImportErrors;
            foreach (var failure in failures.Take(40)) say("EȘEC: " + failure);
            await point.WriteStateAsync(state, token).ConfigureAwait(false);
            return false;
        }

        var (positive, pieces, movementCount) = await point.VerifyAsync(token).ConfigureAwait(false);
        say($"Verificare: {positive} produse cu stoc ({analysis.TakenProducts} așteptate), {pieces} bucăți ({analysis.TakenPieces} așteptate), {movementCount} mișcări ({analysis.TakenProducts} așteptate).");
        if (positive != analysis.TakenProducts || pieces != analysis.TakenPieces || movementCount != analysis.TakenProducts)
        {
            state.Status = EspPointZeroStatus.VerifyFailed;
            await point.WriteStateAsync(state, token).ConfigureAwait(false);
            return false;
        }
        state.Status = EspPointZeroStatus.Verified;
        await point.WriteStateAsync(state, token).ConfigureAwait(false);
        return true;
    }

    private async Task<List<string>> ImportAsync(EspAnalysis analysis, Action<string> say, CancellationToken token)
    {
        var failures = new List<string>();
        var taken = analysis.Items.Where(item => item.IsTaken).ToList();
        var groups = await products.GetGroupsAsync(token).ConfigureAwait(false);
        var haveCategories = groups.Select(group => EspCatalog.Key(group.Category)).ToHashSet();
        var haveGroups = groups.Where(group => group.Subcategory.Length > 0).Select(group => (EspCatalog.Key(group.Category), EspCatalog.Key(group.Subcategory))).ToHashSet();
        foreach (var category in taken.Select(item => item.TargetCategory).Distinct())
            if (haveCategories.Add(EspCatalog.Key(category))) { await products.CreateCategoryAsync(category, token).ConfigureAwait(false); say($"Categorie nouă: {category}"); }
        foreach (var (category, subcategory) in taken.Select(item => (item.TargetCategory, item.TargetSubcategory)).Distinct())
            if (haveGroups.Add((EspCatalog.Key(category), EspCatalog.Key(subcategory)))) { await products.CreateSubcategoryAsync(category, subcategory, token).ConfigureAwait(false); say($"Subcategorie nouă: {subcategory}"); }

        // New parameter values are added once to the list of their parameter.
        var added = new Dictionary<(int Parameter, string Text), int>();
        foreach (var item in taken)
            foreach (var index in item.Choices.Keys.ToList())
                if (item.Choices[index] is { ValueId: null, NewValue: { } text } choice)
                {
                    if (!added.TryGetValue((choice.ParameterId, text), out var valueId))
                    {
                        valueId = (await parameters.AddValueAsync(choice.ParameterId, text, token).ConfigureAwait(false)).Id;
                        added[(choice.ParameterId, text)] = valueId;
                    }
                    item.Choices[index] = new(choice.ParameterId, valueId, null);
                }

        var description = $"Punct zero - preluare din ESPStoc ({analysis.SourceName})";
        var done = 0;
        foreach (var item in taken)
        {
            try
            {
                Product product;
                if (item.Existing is not null)
                {
                    product = await products.GetProductAsync(item.Existing.Id, token).ConfigureAwait(false) ?? throw new ProductOperationException("Produsul nu mai există.");
                    if (item.Completes && !item.Frozen)
                    {
                        // The existing product lacks its required parameters: they are written to it first (its code becomes "model - value"), then it can receive the quantity.
                        var input = new ProductInput
                        {
                            Name = item.FinalCode, Description = product.Description, Category = product.Category, Subcategory = product.Subcategory, BaseModel = item.BaseModel,
                            Parameters = [.. EspPlanner.CodeOrder(item).Select(pair => new ProductParameterChoice(pair.Parameter.Id, pair.Choice.ValueId!.Value))],
                            Reason = "Completare parametri la preluarea stocului din ESPStoc"
                        };
                        product = await products.UpdateAsync(product, input, token).ConfigureAwait(false);
                    }
                }
                else
                {
                    var input = new ProductInput { Name = item.FinalCode, Description = item.Description, Category = item.TargetCategory, Subcategory = item.TargetSubcategory };
                    if (item.IsFrozen) input.AllowIncomplete = true;
                    else if (item.Slots.Count > 0)
                    {
                        input.BaseModel = item.BaseModel;
                        input.Parameters = [.. EspPlanner.CodeOrder(item).Select(pair => new ProductParameterChoice(pair.Parameter.Id, pair.Choice.ValueId!.Value))];
                    }
                    product = await products.CreateAsync(input, token).ConfigureAwait(false);
                }
                if (analysis.Export.Images.TryGetValue(item.OldId, out var bytes) && !await images.ExistsAsync(product.Id, token).ConfigureAwait(false))
                {
                    try { await images.SaveAsync(product.Id, new ProductImageData(bytes, ProductImageRules.DetectContentType(bytes), $"P{item.OldId:0000}.png"), token).ConfigureAwait(false); }
                    catch (ProductImageException exception) { say($"Imagine nepreluată la {item.Code}: {exception.Message}"); }
                }
                var missing = item.Quantity - product.Quantity;
                if (missing > 0)
                    await movements.CreateAsync(product.Id, new StockMovementInput
                    {
                        Kind = StockMovementKind.Entry, Date = StockMovementRules.Today, Quantity = missing, Description = description,
                        FreeType = FreeEntryType.Adjustment, Reference = $"ESPStoc P{item.OldId:0000}", SkipParameterCheck = item.IsFrozen
                    }, token).ConfigureAwait(false);
                else if (missing < 0) throw new ProductOperationException($"Produsul are {product.Quantity} bucăți, mai mult decât exportul ({item.Quantity}).");
                done++;
                if (done % 50 == 0) say($"Preluate {done} din {taken.Count} produse.");
            }
            catch (Exception exception) when (exception is ProductOperationException or StockMovementOperationException or ProductImageException or ArgumentException)
            {
                failures.Add($"{item.Code}: {exception.Message}");
            }
        }
        say($"Preluate {done} din {taken.Count} produse.");
        return failures;
    }

    // The events of the new journal: the first ones, written after it was emptied.
    private async Task WriteJournalAsync(EspPointZeroState state, EspAnalysis analysis, long journalDeleted, int localDeleted, int nasDeleted, List<string> warnings, CancellationToken token)
    {
        string N(string key) => state.Deleted.GetValueOrDefault(key).ToString();
        async Task Event(string action, string target, params (string, string)[] details) =>
            await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.EspImport, action, "", target, AuditDetails.Identification(details), "", token).ConfigureAwait(false);
        await Event(AuditActions.EspStart, analysis.SourceName, ("Export", analysis.SourceName), ("Început", state.StartedUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm")), ("Pachet de siguranță", state.SafetyPackage ?? "-"));
        await Event(AuditActions.EspDeleteMovements, "Mișcări de stoc", ("Șterse", N("stock_movements")), ("Corecții șterse", N("stock_movement_history")), ("Mișcări arhivate șterse", N("archive_stock_movements")));
        await Event(AuditActions.EspDeleteInvoices, "Facturi preluate", ("Facturi", N("supplier_invoices")), ("Linii", N("supplier_invoice_lines")), ("Recunoașteri", N("supplier_recognitions")));
        await Event(AuditActions.EspDeleteOffers, "Oferte", ("Oferte", N("offers")), ("Linii", N("offer_lines")));
        await Event(AuditActions.EspDeleteReservations, "Rezervări pe proiecte", ("Rezervări", N("project_reservations")));
        await Event(AuditActions.EspResetQuantities, "Cantități", ("Produse resetate la 0", N("products")), ("Notificări de stoc închise", N("notifications")));
        await Event(AuditActions.EspImportStock, analysis.SourceName, ("Produse", analysis.TakenProducts.ToString()), ("Bucăți", analysis.TakenPieces.ToString()), ("Produse noi", analysis.NewProducts.ToString()),
            ("Produse existente", analysis.ExistingProducts.ToString()), ("Produse preluate înghețate (fără parametri)", analysis.FrozenProducts.ToString()), ("Categorii noi", analysis.NewCategories.ToString()), ("Subcategorii noi", analysis.NewSubcategories.ToString()));
        await Event(AuditActions.EspDeleteBackups, "Copii de siguranță", ("Pachete locale șterse", localDeleted.ToString()), ("Fișiere șterse pe NAS", nasDeleted.ToString()), ("Pachet nou", state.StartPackage ?? "-"));
        await Event(AuditActions.EspPurgeJournal, "Jurnal de acțiuni", ("Evenimente șterse", journalDeleted.ToString()));
        await Event(AuditActions.EspFinish, analysis.SourceName, ("Stare", state.Status), ("Avertismente", warnings.Count == 0 ? "niciunul" : string.Join(" | ", warnings)));
    }
}
