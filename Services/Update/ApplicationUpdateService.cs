using System.Security.Cryptography;

namespace BlazorStoc.Services.Update;

/// <summary>An answer the administrator can act on (no right, no installation, a package that cannot be installed, an update already waiting).</summary>
public sealed class UpdateOperationException(string message) : Exception(message);

/// <summary>What Setari -> Actualizare shows: the versions, the update that is waiting or running, the last results.</summary>
public sealed record UpdateOverview(bool Managed, string Running, string? Current, string? Previous, IReadOnlyList<string> Installed, UpdateStatusDocument? Status,
    IReadOnlyList<UpdateHistoryDocument> History, UpdateAnnouncement? Scheduled, string? LastError, UpdateHistoryDocument? UnacknowledgedFailure);

/// <summary>
/// Setari -> Actualizare (docs/PROPUNERE_ACTUALIZARE.md, section 4): take a package from the administrator, have the updater check it (the application cannot: it has no right to the
/// list of trusted signers and none to change the installation), ask for a confirmation with a reason, tell the users, and hand the update to the updater. The updater does the work;
/// this class only writes requests into updates\staged and reads what the updater wrote into state\.
/// </summary>
public sealed class ApplicationUpdateService(UpdateInstallation installation, IUpdateLauncher launcher, UpdateCoordinator coordinator, UpdateJournal journal, IAccessControl access,
    IAuditTrail audit, TimeProvider clock)
{
    public const string ViewPermission = "setari-actualizare.view";
    public const string ApplyPermission = "setari-actualizare.apply";
    public const long MaximumPackageBytes = 1024L * 1024 * 1024;
    public const string EntityName = "Aplicația BlazorStoc";

    public TimeSpan CheckTimeout { get; init; } = TimeSpan.FromMinutes(3);
    public TimeSpan CheckPoll { get; init; } = TimeSpan.FromMilliseconds(400);
    public TimeSpan Notice { get; init; } = UpdateCoordinator.DefaultNotice;

    public UpdateOverview Overview()
    {
        var managed = installation.IsManaged;
        return new UpdateOverview(managed, AppVersion.Current, managed ? installation.Current : null, managed ? installation.Previous : null, managed ? installation.InstalledVersions() : [],
            managed ? installation.Status() : null, managed ? installation.History() : [], coordinator.Current, coordinator.LastError, journal.UnacknowledgedFailure());
    }

    /// <summary>True while the updater works on an update (its status is not "done" and was written in the last hour).</summary>
    public bool UpdaterIsWorking() => installation.IsManaged && installation.Status() is { Phase: not "done" } status && clock.GetUtcNow().UtcDateTime - status.UpdatedUtc < TimeSpan.FromHours(1);

    /// <summary>Stores the uploaded package in updates\staged and has the updater check it; returns its report (which may say the package cannot be installed).</summary>
    public async Task<PackageCheckDocument> UploadAndCheckAsync(Stream package, string fileName, CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync(ApplyPermission, cancellationToken).ConfigureAwait(false);
        EnsureManaged();
        if (coordinator.Current is not null || UpdaterIsWorking()) throw new UpdateOperationException("O actualizare este deja programată sau rulează: așteaptă să se termine.");
        if (!fileName.EndsWith(".bsu", StringComparison.OrdinalIgnoreCase)) throw new UpdateOperationException("Alege un pachet de actualizare (fișier .bsu).");
        var actor = await access.GetUsernameAsync(cancellationToken).ConfigureAwait(false) ?? "necunoscut";

        Directory.CreateDirectory(installation.StagedFolder);
        RemoveEarlierUploads();
        var id = Guid.NewGuid().ToString("N");
        var name = $"upload-{id}.bsu";
        var path = Path.Combine(installation.StagedFolder, name);
        var sha = await StoreAsync(package, path, cancellationToken).ConfigureAwait(false);

        var request = new UpdateRequestDocument(id, UpdateRequestKinds.Check, name, null, actor, "verificare", clock.GetUtcNow().UtcDateTime);
        await WriteRequestAsync(request, cancellationToken).ConfigureAwait(false);
        if (await launcher.StartAsync(cancellationToken).ConfigureAwait(false) is { } error)
        {
            Discard(id, name);
            throw new UpdateOperationException(error);
        }

        var deadline = clock.GetUtcNow().UtcDateTime + CheckTimeout;
        while (true)
        {
            if (installation.Check(id) is { } report)
            {
                if (!string.Equals(report.Sha256, sha, StringComparison.OrdinalIgnoreCase))
                    throw new UpdateOperationException("Pachetul s-a schimbat în timpul verificării. Încarcă-l din nou.");
                return report;
            }
            if (clock.GetUtcNow().UtcDateTime >= deadline)
            {
                Discard(id, name);
                throw new UpdateOperationException("Updater-ul nu a răspuns la verificarea pachetului. Verifică sarcina programată a Updater-ului sau folosește installerul pe server.");
            }
            await Task.Delay(CheckPoll, clock, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The administrator confirms, with a reason. The journal gets "Actualizare aplicație X -> Y" under his name now; the users are told; the updater is asked after the notice time.
    /// </summary>
    public async Task<UpdateAnnouncement> ConfirmAsync(string checkId, string reason, CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync(ApplyPermission, cancellationToken).ConfigureAwait(false);
        EnsureManaged();
        if (ChangeReasonRules.ValidationError(reason) is { } reasonError) throw new UpdateOperationException(reasonError);
        reason = ChangeReasonRules.Normalize(reason);
        var report = installation.Check(checkId) ?? throw new UpdateOperationException("Verificarea pachetului nu mai există: încarcă pachetul din nou.");
        if (!report.CanInstall || report.Version is null) throw new UpdateOperationException("Pachetul nu poate fi instalat: " + (report.Detail ?? "verificarea a găsit probleme."));
        if (report.Running != installation.Current) throw new UpdateOperationException("Versiunea instalată s-a schimbat de la verificare: încarcă pachetul din nou.");
        if (!File.Exists(Path.Combine(installation.StagedFolder, report.Package))) throw new UpdateOperationException("Pachetul nu mai este pe server: încarcă-l din nou.");
        if (coordinator.Current is not null || UpdaterIsWorking()) throw new UpdateOperationException("O actualizare este deja programată sau rulează.");

        var actor = await access.GetUsernameAsync(cancellationToken).ConfigureAwait(false) ?? "necunoscut";
        var from = report.Running ?? AppVersion.Current;
        var details = AuditDetails.Identification(("De la", from), ("La", report.Version), ("Pachet SHA-256", report.Sha256), ("Semnat de", Short(report.Signer)),
            ("Migrări noi ale bazei de date", report.NewMigrations?.ToString() ?? "necunoscut"), ("Pornește în", $"{(int)Notice.TotalSeconds} s"));
        await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.Application, AuditActions.ApplicationUpdate, string.Empty, EntityName, details, reason, cancellationToken).ConfigureAwait(false);

        var request = new UpdateRequestDocument(report.Id, UpdateRequestKinds.Apply, report.Package, report.Sha256, actor, reason, clock.GetUtcNow().UtcDateTime);
        if (!coordinator.TrySchedule(request, from, report.Version, Notice))
        {
            await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.Application, AuditActions.ApplicationUpdateCancelled, string.Empty, EntityName,
                AuditDetails.Identification(("De la", from), ("La", report.Version), ("Motiv anulare", "altă actualizare era deja programată")), reason, CancellationToken.None).ConfigureAwait(false);
            throw new UpdateOperationException("O actualizare este deja programată sau rulează.");
        }
        return coordinator.Current!;
    }

    /// <summary>Gives up an update that waits for its hour. Nothing was started yet.</summary>
    public async Task<bool> CancelAsync(CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync(ApplyPermission, cancellationToken).ConfigureAwait(false);
        if (coordinator.Current is not { Launched: false } waiting) return false;
        if (!coordinator.Cancel()) return false;
        await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.Application, AuditActions.ApplicationUpdateCancelled, string.Empty, EntityName,
            AuditDetails.Identification(("De la", waiting.From), ("La", waiting.To), ("Motiv anulare", "anulată de administrator înainte de pornire")), "Anulare de către administrator", cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>The package that was checked is not wanted: removed from the server.</summary>
    public async Task DiscardAsync(string checkId, CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync(ApplyPermission, cancellationToken).ConfigureAwait(false);
        EnsureManaged();
        if (installation.Check(checkId) is { } report) Discard(report.Id, report.Package);
    }

    /// <summary>The administrator took note of a failed update: the banner goes away and the journal says who and when.</summary>
    public async Task AcknowledgeAsync(CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync(ApplyPermission, cancellationToken).ConfigureAwait(false);
        if (journal.UnacknowledgedFailure() is not { } failure) return;
        await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.Application, AuditActions.ApplicationUpdateAcknowledged, string.Empty, EntityName,
            AuditDetails.Identification(("De la", failure.From ?? "-"), ("La", failure.To), ("Rezultat", failure.Message), ("Ora rezultatului", UpdateJournal.Local(failure.AtUtc))),
            "Luare la cunoștință a unei actualizări eșuate", cancellationToken).ConfigureAwait(false);
        journal.Acknowledge(failure.AtUtc);
        coordinator.Notify();
    }

    // ---- helpers -----------------------------------------------------------------------------------------------------------------------------------

    private void EnsureManaged()
    {
        if (!installation.IsManaged)
            throw new UpdateOperationException("Aplicația nu rulează dintr-o instalare făcută de Updater (de exemplu mediul de dezvoltare), deci nu se poate actualiza singură. Folosește installerul de actualizare pe server.");
    }

    private async Task WriteRequestAsync(UpdateRequestDocument request, CancellationToken cancellationToken)
    {
        var path = installation.RequestPath(request.Id);
        await File.WriteAllTextAsync(path + ".tmp", System.Text.Json.JsonSerializer.Serialize(request, UpdateJson.Options), cancellationToken).ConfigureAwait(false);
        File.Move(path + ".tmp", path, overwrite: true);
    }

    // The package is written beside its final name and renamed when it is whole; its SHA-256 is computed on the way (what the administrator confirms is this exact file).
    private async Task<string> StoreAsync(Stream source, string path, CancellationToken cancellationToken)
    {
        var partial = path + ".partial";
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long total = 0;
            var first = true;
            await using (var target = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    if (first)
                    {
                        first = false;
                        if (read < 4 || buffer[0] != 'P' || buffer[1] != 'K') throw new UpdateOperationException("Fișierul nu este un pachet de actualizare (nu este o arhivă .bsu).");
                    }
                    total += read;
                    if (total > MaximumPackageBytes) throw new UpdateOperationException("Pachetul depășește 1 GB.");
                    hash.AppendData(buffer, 0, read);
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
            }
            if (total == 0) throw new UpdateOperationException("Fișierul este gol.");
            File.Move(partial, path, overwrite: true);
            return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        }
        catch
        {
            try { File.Delete(partial); } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }

    // Only one uploaded package is kept: a new upload replaces the earlier one (and its request and report, if the updater has not removed them).
    private void RemoveEarlierUploads()
    {
        foreach (var file in Directory.EnumerateFiles(installation.StagedFolder, "upload-*").Concat(Directory.EnumerateFiles(installation.StagedFolder, "request-*.json")))
        {
            try { File.Delete(file); } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    private void Discard(string id, string package)
    {
        foreach (var file in new[] { Path.Combine(installation.StagedFolder, package), Path.Combine(installation.StagedFolder, package + ".partial"), installation.RequestPath(id) })
        {
            try { if (File.Exists(file)) File.Delete(file); } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    public static string Short(string? fingerprint) => string.IsNullOrEmpty(fingerprint) ? "-" : fingerprint.Length > 16 ? fingerprint[..16] : fingerprint;
}
