using System.Text.Json;

namespace BlazorStoc.Updater;

/// <summary>How the engine behaves; the checks shorten the waits and fake the free space. HealthTimeout: how long the new version has to answer /health with its own version.</summary>
public sealed record UpdateOptions(TimeSpan HealthTimeout, TimeSpan HealthInterval, int KeepReleases = 3, Func<long>? FreeSpace = null, Func<DateTime>? UtcNow = null,
    string Platform = "win-x64")
{
    public static UpdateOptions Default => new(TimeSpan.FromSeconds(120), TimeSpan.FromSeconds(2));
}

/// <summary>
/// The installation of a package (docs/PROPUNERE_ACTUALIZARE.md, section 4), the same for the application's own upload and for the installer an administrator runs by hand:
/// verify, check compatibility and room, extract beside the running version, stop, back up the database with the running version, switch current.txt, migrate with the new
/// version, start, wait until /health answers with the new version. Whatever fails after the switch puts the old version back (and the database, when the migrations had been
/// started) and starts it again. Before the stop nothing of the running installation is touched, so a refusal costs no downtime.
/// </summary>
public sealed class UpdateEngine(InstallLayout layout, IApplicationHost host, IReleaseTools tools, IUpdateLog log, UpdateOptions options)
{
    private const long ReserveBytes = 256L * 1024 * 1024;

    private DateTime Now => (options.UtcNow ?? (() => DateTime.UtcNow))();

    public async Task<UpdateResult> InstallAsync(InstallRequest request, CancellationToken cancellationToken = default)
    {
        layout.EnsureFolders();
        // Another update holds the lock: this one says so and writes nothing (the state and the history belong to the one that is running).
        if (!TryTakeLock(out var guard, out var busy)) return busy!;
        using var _ = guard;
        var started = Now;
        var from = InstalledCurrent();
        var to = "?";
        string? signer = null;
        try
        {
            ReportInterruptedEarlierUpdate();
            Phase("verifying", from, to, request, started, "Checking the package");
            var trusted = new TrustedKeys(layout.TrustedKeysFile);
            var verification = await PackageVerifier.VerifyAsync(request.PackagePath, trusted.Fingerprints(), cancellationToken).ConfigureAwait(false);
            if (!verification.Ok) throw new UpdateRefusedException("The package is not valid: " + string.Join(" ", verification.Problems));
            var manifest = verification.Manifest!;
            signer = verification.SignerFingerprint!;
            to = manifest.Version;
            if (verification.Trusted == false) TrustSigner(request, signer, to);

            if (PreflightProblem(manifest, from, request.Reinstall, request.AllowDowngrade) is { } problem) throw new UpdateRefusedException(problem);

            CleanLeftovers();
            Phase("extracting", from, to, request, started, "Unpacking the new version beside the running one");
            var partial = layout.ReleaseFolder(to) + ".partial";
            await PackageExtractor.ExtractAsync(request.PackagePath, manifest, partial, cancellationToken).ConfigureAwait(false);
            log.Info($"Version {to} unpacked ({manifest.Files.Count} files); the running version {from ?? "(none)"} has not been touched.");

            var status = await tools.SchemaStatusAsync(Path.Combine(partial, "app"), cancellationToken).ConfigureAwait(false)
                ?? throw new UpdateRefusedException("The new version could not read the state of the database (is the database running? is the account of the application configured?). Nothing was changed.");
            if (status.LedgerVersion > status.CodeVersion)
                throw new UpdateRefusedException($"The database is at schema {status.LedgerVersion}, newer than the {status.CodeVersion} of version {to}. Install a newer version; nothing was changed.");
            var willMigrate = status.Pending > 0 || status.MissingColumns.Count > 0;
            log.Info(willMigrate ? $"The update changes the schema ({status.Pending} migration(s) pending)." : "The update does not change the schema.");

            return await SwitchAsync(request, manifest, from, signer, partial, willMigrate, started, cancellationToken).ConfigureAwait(false);
        }
        catch (UpdateRefusedException refused)
        {
            log.Warn("Refused: " + refused.Message);
            CleanLeftovers();
            return Finish(UpdateOutcome.Refused, from, to, null, refused.Message, request, signer, started);
        }
        catch (Exception exception) when (exception is UpdateStepException or IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
        {
            // Before the stop: the extraction or a check failed; the leftovers are removed and the running version never noticed.
            log.Error("The update stopped before the running version was touched: " + exception.Message);
            CleanLeftovers();
            return Finish(UpdateOutcome.Refused, from, to, null, "Nothing was changed: " + exception.Message, request, signer, started);
        }
    }

    // ---- the part that changes the running installation --------------------------------------------------------------------------------------------------

    private async Task<UpdateResult> SwitchAsync(InstallRequest request, PackageManifest manifest, string? from, string signer, string partial, bool willMigrate, DateTime started,
        CancellationToken cancellationToken)
    {
        var to = manifest.Version;
        var final = layout.ReleaseFolder(to);
        string? aside = null, backup = null;
        var migrationStarted = false;
        var previousBefore = layout.Previous;
        try
        {
            Phase("stopping", from, to, request, started, "Stopping the application");
            await host.StopAsync(cancellationToken).ConfigureAwait(false);

            if (from is not null)
            {
                Phase("backup", from, to, request, started, "Backing up the database");
                backup = await tools.BackupAsync(layout.AppFolder(from), request.Actor, cancellationToken).ConfigureAwait(false);
                log.Info($"Backup of the database before the update: {backup}");
            }
            else log.Warn("There is no working version to make the backup with (first installation, or the installed version is damaged); the database was not backed up.");

            Phase("switching", from, to, request, started, "Switching to the new version", backup);
            if (Directory.Exists(final)) { aside = final + ".old-" + Now.ToString("yyyyMMddHHmmss"); MoveDirectory(final, aside); }
            MoveDirectory(partial, final);
            if (from is not null && from != to) layout.SetPrevious(from);
            layout.SetCurrent(to);
            log.Info($"current.txt now says {to} (it said {from ?? "nothing"}).");

            if (willMigrate)
            {
                Phase("migrating", from, to, request, started, "Applying the migrations of the database", backup);
                migrationStarted = true;
                await tools.MigrateAsync(layout.AppFolder(to), cancellationToken).ConfigureAwait(false);
            }

            Phase("starting", from, to, request, started, "Starting the new version", backup);
            await host.StartAsync(cancellationToken).ConfigureAwait(false);

            Phase("health", from, to, request, started, "Waiting for the new version to answer", backup);
            if (!await WaitHealthyAsync(to, cancellationToken).ConfigureAwait(false))
                throw new UpdateStepException($"Version {to} did not answer /health with its own version within {options.HealthTimeout.TotalSeconds:0} s.");
        }
        catch (Exception exception) when (exception is UpdateStepException or IOException or UnauthorizedAccessException or OperationCanceledException or InvalidOperationException)
        {
            log.Error("The update failed: " + exception.Message);
            return await RollBackAsync(request, from, to, signer, backup, migrationStarted, aside, previousBefore, exception.Message, started).ConfigureAwait(false);
        }

        if (aside is not null) TryDelete(aside);
        ApplyRetention();
        var message = $"Updated {from ?? "(nothing)"} -> {to}. The new version answers.";
        log.Info(message);
        return Finish(UpdateOutcome.Succeeded, from, to, backup, message, request, signer, started);
    }

    private async Task<UpdateResult> RollBackAsync(InstallRequest request, string? from, string to, string? signer, string? backup, bool migrationStarted, string? aside, string? previousBefore,
        string failure, DateTime started)
    {
        Phase("rolling-back", from, to, request, started, "Putting the old version back: " + failure, backup);
        try { await host.StopAsync(CancellationToken.None).ConfigureAwait(false); }
        catch (Exception exception) when (exception is UpdateStepException or IOException) { log.Warn("The new version could not be stopped cleanly: " + exception.Message); }

        var final = layout.ReleaseFolder(to);
        try
        {
            if (aside is not null && Directory.Exists(aside)) { if (Directory.Exists(final)) Directory.Delete(final, true); MoveDirectory(aside, final); }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { log.Error("The replaced folder could not be put back: " + exception.Message); }

        if (from is null)
        {
            if (File.Exists(layout.CurrentFile)) File.Delete(layout.CurrentFile);
            var first = $"The first installation of {to} failed and there is no earlier version to go back to: {failure}";
            log.Error(first);
            return Finish(UpdateOutcome.Failed, null, to, backup, first, request, signer, started);
        }

        layout.SetCurrent(from);
        layout.SetPrevious(previousBefore);
        log.Info($"current.txt says {from} again.");

        if (migrationStarted && backup is not null)
        {
            try
            {
                log.Info("The migrations had been started: restoring the database from the backup made a moment ago (nothing was written since).");
                await tools.RestoreAsync(layout.AppFolder(from), backup, request.Actor, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is UpdateStepException or IOException)
            {
                log.Error("The database could not be restored automatically: " + exception.Message + " The backup is " + backup + ".");
            }
        }

        try
        {
            await host.StartAsync(CancellationToken.None).ConfigureAwait(false);
            if (await WaitHealthyAsync(from, CancellationToken.None).ConfigureAwait(false))
            {
                var done = $"The update to {to} failed ({failure}); version {from} is back and answers.";
                log.Warn(done);
                return Finish(UpdateOutcome.RolledBack, from, to, backup, done, request, signer, started);
            }
        }
        catch (Exception exception) when (exception is UpdateStepException or IOException) { log.Error("The old version could not be started: " + exception.Message); }

        var critical = $"The update to {to} failed ({failure}) and version {from} does not answer either. The application is down: see the logs, then run: install <package of the version that worked> --reinstall (or: rollback).";
        log.Error(critical);
        return Finish(UpdateOutcome.Failed, from, to, backup, critical, request, signer, started);
    }

    // ---- going back by hand (the installer's --rollback) --------------------------------------------------------------------------------------------------

    /// <summary>Goes back to the version before the current one: the application is stopped, the pointers swapped, the database restored when a package is named, the application started.</summary>
    public async Task<UpdateResult> RollbackAsync(string actor, string reason, string? restoreFromBackup = null, CancellationToken cancellationToken = default)
    {
        layout.EnsureFolders();
        if (!TryTakeLock(out var guard, out var busy)) return busy!;
        using var _ = guard;
        var started = Now;
        var from = InstalledCurrent();
        var request = new InstallRequest("", actor, reason, Kind: UpdateKind.Rollback);
        var target = layout.Previous;
        if (from is null || target is null || !layout.HasRelease(target))
        {
            var message = "There is no earlier version to go back to.";
            log.Warn(message);
            return Finish(UpdateOutcome.Refused, from, target ?? "?", null, message, request, null, started);
        }
        try
        {
            Phase("rolling-back", from, target, request, started, "Going back by hand");
            await host.StopAsync(cancellationToken).ConfigureAwait(false);
            if (restoreFromBackup is not null)
            {
                log.Info($"Restoring the database from {restoreFromBackup}.");
                await tools.RestoreAsync(layout.AppFolder(target), restoreFromBackup, actor, cancellationToken).ConfigureAwait(false);
            }
            layout.SetPrevious(from);
            layout.SetCurrent(target);
            await host.StartAsync(cancellationToken).ConfigureAwait(false);
            if (await WaitHealthyAsync(target, cancellationToken).ConfigureAwait(false))
            {
                var done = $"Back to {target} (was {from}). The application answers.";
                log.Info(done);
                return Finish(UpdateOutcome.RolledBack, from, target, null, done, request, null, started);
            }
            throw new UpdateStepException($"Version {target} did not answer /health with its own version.");
        }
        catch (Exception exception) when (exception is UpdateStepException or IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            var failed = $"Going back to {target} failed: {exception.Message}";
            log.Error(failed);
            // The pointer was not moved yet (the restoration of the database failed before it): the version that ran is untouched, so it is started again instead of leaving the application down.
            if (layout.Current == from && from is not null)
            {
                try
                {
                    await host.StartAsync(CancellationToken.None).ConfigureAwait(false);
                    failed += $" Version {from} was started again.";
                    log.Warn($"Version {from} was started again.");
                }
                catch (Exception restart) when (restart is UpdateStepException or IOException) { log.Error($"Version {from} could not be started again: {restart.Message}"); }
            }
            return Finish(UpdateOutcome.Failed, from, target, null, failed, request, null, started);
        }
    }

    // ---- helpers -----------------------------------------------------------------------------------------------------------------------------------------

    private string? InstalledCurrent() => layout.Current is { } current && layout.HasRelease(current) ? current : null;

    /// <summary>
    /// Why this package cannot be installed over the running version, or null when nothing stands in the way: the platform, the order of the versions, the oldest version it can be
    /// installed over, and the room on the disk. The page of the application asks the same question before it offers the button (the request file "check"), so the page and the
    /// installation never disagree.
    /// </summary>
    public string? PreflightProblem(PackageManifest manifest, string? from, bool reinstall = false, bool allowDowngrade = false)
    {
        if (!string.Equals(manifest.Platform, options.Platform, StringComparison.OrdinalIgnoreCase))
            return $"The package is for {manifest.Platform}, this server is {options.Platform}.";
        // A version whose files are damaged is not "running" (from is null) but current.txt still names it: putting that same version again is the repair.
        if (reinstall && from != manifest.Version && layout.Current != manifest.Version) return $"--reinstall puts the same version again; {from ?? "nothing"} is installed, the package is {manifest.Version}.";
        if (from is not null)
        {
            var order = Version.Parse(manifest.Version).CompareTo(Version.Parse(from));
            if (order == 0 && !reinstall) return $"Version {from} is already installed (use --reinstall to put it again).";
            if (order < 0 && !allowDowngrade) return $"The package ({manifest.Version}) is older than the installed version ({from}); a downgrade needs --allow-downgrade.";
            if (manifest.MinFromVersion is { } minimum && Version.Parse(from) < Version.Parse(minimum) && !reinstall)
                return $"This package can be installed over {minimum} or newer; {from} is installed. Install the intermediate version first.";
        }
        var needed = manifest.Files.Sum(file => file.Size) + ReserveBytes;
        var free = (options.FreeSpace ?? DefaultFreeSpace)();
        return free < needed ? $"There is not enough room on the disk of {layout.Root}: {needed / 1048576} MB are needed, {free / 1048576} MB are free." : null;
    }

    /// <summary>Records a request that is refused before anything is looked at (its package is not the file that was confirmed): status and history, no lock taken over another update.</summary>
    public UpdateResult Reject(InstallRequest request, string message)
    {
        layout.EnsureFolders();
        if (!TryTakeLock(out var guard, out var busy)) return busy!;
        using var _ = guard;
        return Finish(UpdateOutcome.Refused, InstalledCurrent(), "?", null, message, request, null, Now);
    }

    /// <summary>The installed version that really is on disk (a pointer that names a missing folder is not trusted).</summary>
    public string? RunningVersion() => InstalledCurrent();

    private void TrustSigner(InstallRequest request, string signer, string version)
    {
        var confirmed = (request.TrustFingerprint is { } given && TrustedKeys.Normalise(given) == signer) || request.ConfirmUnknownSigner?.Invoke(signer) == true;
        if (!confirmed)
            throw new UpdateRefusedException($"The package is signed with a key this server does not know ({SigningKeyStore.Display(signer)}). An administrator must confirm the fingerprint at the installer (compare it with the one printed on the build machine).");
        new TrustedKeys(layout.TrustedKeysFile).Add(signer, request.Actor, $"confirmed at the installation of {version}", Now);
        log.Info($"The signer {SigningKeyStore.Display(signer)} was added to the trusted keys by {request.Actor}.");
    }

    private async Task<bool> WaitHealthyAsync(string version, CancellationToken cancellationToken)
    {
        var deadline = Now + options.HealthTimeout;
        while (true)
        {
            var answer = await host.ProbeAsync(cancellationToken).ConfigureAwait(false);
            if (answer.IsWell(version)) return true;
            if (Now >= deadline) { log.Warn($"Last answer of /health: reachable {answer.Reachable}, status {answer.StatusCode}, version {answer.Version ?? "-"} (expected {version})."); return false; }
            await Task.Delay(options.HealthInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private void ApplyRetention()
    {
        var keep = new HashSet<string>(StringComparer.Ordinal);
        if (layout.Current is { } current) keep.Add(current);
        if (layout.Previous is { } previous) keep.Add(previous);
        foreach (var version in layout.InstalledVersions().Take(options.KeepReleases)) keep.Add(version);
        foreach (var version in layout.InstalledVersions().Where(version => !keep.Contains(version))) TryDelete(layout.ReleaseFolder(version));
    }

    private void CleanLeftovers()
    {
        if (!Directory.Exists(layout.ReleasesFolder)) return;
        foreach (var folder in Directory.EnumerateDirectories(layout.ReleasesFolder).Where(folder => folder.EndsWith(".partial", StringComparison.Ordinal) || folder.Contains(".old-", StringComparison.Ordinal)))
            TryDelete(folder);
    }

    /// <summary>
    /// Renames a folder. A folder that was just written or run from can be held for a moment by something that is not ours (a virus scanner, the search indexer, a synchronisation
    /// client): Windows then refuses the rename with "access denied", and it passes. Tried again for about ten seconds before it is a failure.
    /// </summary>
    private void MoveDirectory(string source, string destination)
    {
        for (var attempt = 1; ; attempt++)
        {
            try { Directory.Move(source, destination); return; }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException && attempt < 20)
            {
                if (attempt == 1) log.Warn($"{Path.GetFileName(source)} cannot be renamed yet ({exception.Message}); trying again for a few seconds.");
                Thread.Sleep(500);
            }
        }
    }

    private void TryDelete(string folder)
    {
        try { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { log.Warn($"{folder} could not be removed ({exception.Message}); it is left for the next update."); }
    }

    private static long DefaultFreeSpaceOf(string root) => new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root))!).AvailableFreeSpace;
    private long DefaultFreeSpace() => DefaultFreeSpaceOf(layout.Root);

    private bool TryTakeLock(out FileStream? guard, out UpdateResult? busy)
    {
        try
        {
            guard = new FileStream(layout.LockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            busy = null;
            return true;
        }
        catch (IOException)
        {
            const string message = "Another update is running on this installation.";
            log.Warn("Refused: " + message);
            guard = null;
            busy = new UpdateResult(UpdateOutcome.Refused, InstalledCurrent(), "?", null, message);
            return false;
        }
    }

    private void ReportInterruptedEarlierUpdate()
    {
        var last = ReadStatus();
        if (last is { Phase: not "done" }) log.Warn($"The previous update ({last.From} -> {last.To}) did not finish: it stopped at \"{last.Phase}\". The pointer is consistent (current.txt says {layout.Current ?? "nothing"}); this update starts again from a clean state.");
    }

    // ---- state and history -------------------------------------------------------------------------------------------------------------------------------

    public UpdateStatus? ReadStatus()
    {
        try { return File.Exists(layout.StatusFile) ? JsonSerializer.Deserialize<UpdateStatus>(File.ReadAllText(layout.StatusFile), InstallLayout.Json) : null; }
        catch (JsonException) { return null; }
    }

    private void Phase(string phase, string? from, string to, InstallRequest request, DateTime started, string message, string? backup = null)
    {
        log.Info($"[{phase}] {message}");
        WriteStatus(new UpdateStatus(phase, from, to, request.Actor, request.Reason, started, Now, null, message, backup));
    }

    private UpdateResult Finish(UpdateOutcome outcome, string? from, string to, string? backup, string message, InstallRequest request, string? signer, DateTime started)
    {
        WriteStatus(new UpdateStatus("done", from, to, request.Actor, request.Reason, started, Now, outcome.ToString(), message, backup));
        try
        {
            Directory.CreateDirectory(layout.StateFolder);
            var entry = new UpdateHistoryEntry(Now, from, to, outcome.ToString(), request.Actor, request.Reason, signer, backup, (Now - started).TotalSeconds, message,
                request.PackageSha256, request.Via, request.Kind);
            File.AppendAllText(layout.HistoryFile, JsonSerializer.Serialize(entry, new JsonSerializerOptions(JsonSerializerDefaults.Web)) + Environment.NewLine);
        }
        catch (IOException) { log.Warn("The history could not be written."); }
        return new UpdateResult(outcome, from, to, backup, message);
    }

    private void WriteStatus(UpdateStatus status)
    {
        try
        {
            Directory.CreateDirectory(layout.StateFolder);
            var temporary = layout.StatusFile + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(status, InstallLayout.Json));
            File.Move(temporary, layout.StatusFile, overwrite: true);
        }
        catch (IOException) { /* the state is information; a reader holding the file at this moment must not stop an update */ }
    }
}
