using BlazorStoc.Services;

namespace BlazorStoc.Modules.Elcomtur;

// The import of the Elcomtur backup: the file is uploaded by the browser with one HTTP request (it has hundreds of MB), kept in a temporary folder under an id, read for the chosen firm
// (simulation: nothing is written; application: the balances of the beneficiaries are replaced) and deleted afterwards. Needs "elcomtur.import".
public sealed class ElcomturImportService(IBeneficiaryRepository beneficiaries, IElcomturBalanceRepository balances, IAccessControl? accessControl = null, IAuditTrail? auditTrail = null) : IElcomturImportService
{
    public const string ImportKey = "elcomtur.import";
    // The largest backup accepted (the archive of the test firm is 340 MB).
    public const long MaximumBytes = 3L * 1024 * 1024 * 1024;
    private static readonly TimeSpan KeepFor = TimeSpan.FromHours(6);

    private static string Folder => Path.Combine(Path.GetTempPath(), "BlazorStoc-elcomtur");

    private static string PathOf(string fileId)
    {
        if (fileId.Length != 32 || !fileId.All(Uri.IsHexDigit)) throw new ElcomturOperationException("Fișierul încărcat nu mai există. Încarcă-l din nou.");
        var path = Path.Combine(Folder, fileId + ".elc");
        return File.Exists(path) ? path : throw new ElcomturOperationException("Fișierul încărcat nu mai există. Încarcă-l din nou.");
    }

    public async Task<string> UploadAsync(Stream body, string fileName, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAsync(ImportKey, cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(Folder);
        foreach (var old in Directory.EnumerateFiles(Folder, "*.elc").Where(file => DateTime.UtcNow - File.GetLastWriteTimeUtc(file) > KeepFor))
            try { File.Delete(old); } catch (IOException) { }
        var id = Guid.NewGuid().ToString("N");
        var path = Path.Combine(Folder, id + ".elc");
        try
        {
            long total = 0;
            await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true))
            {
                var buffer = new byte[1 << 20];
                int read;
                while ((read = await body.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    total += read;
                    if (total > MaximumBytes) throw new ElcomturOperationException("Fișierul este prea mare pentru preluare.");
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
            }
            if (total == 0) throw new ElcomturOperationException("Fișierul încărcat este gol.");
            if (ElcomturArchive.Firms(path).Count == 0) throw new ElcomturOperationException("Arhiva nu conține nicio firmă cu tabelele Elcomtur necesare (clienți, facturi emise, casă).");
            return id;
        }
        catch
        {
            try { File.Delete(path); } catch (IOException) { }
            throw;
        }
    }

    public async Task<IReadOnlyList<string>> GetFirmsAsync(string fileId, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAsync(ImportKey, cancellationToken).ConfigureAwait(false);
        var path = PathOf(fileId);
        return await Task.Run(() => ElcomturArchive.Firms(path), cancellationToken).ConfigureAwait(false);
    }

    public async Task<ElcomturReport> RunAsync(string fileId, string firm, bool apply, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAsync(ImportKey, cancellationToken).ConfigureAwait(false);
        var path = PathOf(fileId);
        var data = await Task.Run(() => ElcomturArchive.Read(path, firm), cancellationToken).ConfigureAwait(false);
        var plan = ElcomturPlanner.Plan(data, await beneficiaries.GetBeneficiariesAsync(cancellationToken).ConfigureAwait(false));
        if (!apply) return plan.Report;
        await balances.ReplaceAsync(data.Firm, data.DataDate, plan.Balances, cancellationToken).ConfigureAwait(false);
        var report = plan.Report with { Applied = true };
        await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.Elcomtur, AuditActions.ImportElcomtur, data.Firm, $"Elcomtur {data.Firm}",
            AuditDetails.Identification(("Firmă", data.Firm), ("Data backup-ului", StockMovementRules.DisplayDate(data.DataDate)), ("Beneficiari cu balanță", report.Matched.ToString()),
                ("Cu plăți restante", report.MatchedOwed.ToString()), ("Total plăți restante", report.OwedTotal.ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("ro-RO"))),
                ("Clienți fără beneficiar (cu sold pozitiv)", report.UnmatchedOwed.ToString())), string.Empty, cancellationToken).ConfigureAwait(false);
        return report;
    }

    public async Task DiscardAsync(string fileId, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAsync(ImportKey, cancellationToken).ConfigureAwait(false);
        if (fileId.Length != 32 || !fileId.All(Uri.IsHexDigit)) return;
        var path = Path.Combine(Folder, fileId + ".elc");
        try { File.Delete(path); } catch (IOException) { }
    }
}
