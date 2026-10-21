using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace BlazorStoc.Updater;

/// <summary>
/// The real <see cref="IReleaseTools"/>: every operation is "dotnet BlazorStoc.dll &lt;option&gt;" of the version that has to do it, in the folder of that version (the configuration files of
/// the application are looked up from there). The backup and the restoration talk to the application's own backup worker over the standard streams (Modules/Backup/BackupJob.cs).
/// The migrator account's file stays in private/ and is given only to the migration process (Database__MigratorPrivateConfigPath).
/// </summary>
public sealed class ProcessReleaseTools(InstallLayout layout, UpdaterSettings settings, IUpdateLog log) : IReleaseTools
{
    private static readonly TimeSpan MigrationTimeout = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan BackupTimeout = TimeSpan.FromMinutes(60);
    private static readonly TimeSpan RestoreTimeout = TimeSpan.FromMinutes(60);

    public async Task<SchemaStatus?> SchemaStatusAsync(string appFolder, CancellationToken cancellationToken)
    {
        var (code, output, _) = await RunAsync(appFolder, ["--schema-status"], withMigrator: false, null, TimeSpan.FromMinutes(3), cancellationToken).ConfigureAwait(false);
        var line = output.Split('\n').Select(row => row.Trim()).FirstOrDefault(row => row.StartsWith("SCHEMA-STATUS ", StringComparison.Ordinal));
        if (code != 0 || line is null) return null;
        try { return JsonSerializer.Deserialize<SchemaStatus>(line["SCHEMA-STATUS ".Length..], InstallLayout.Json); }
        catch (JsonException) { return null; }
    }

    public async Task MigrateAsync(string appFolder, CancellationToken cancellationToken)
    {
        if (!File.Exists(layout.MigrationAccountFile) && !File.Exists(Path.Combine(SettingsDataRoot(), "migration-account.private.json")))
            throw new UpdateStepException($"The account that changes the schema is not configured: {layout.MigrationAccountFile} is missing.");
        log.Info("Applying the migrations (--migrate-schema).");
        var (code, output, error) = await RunAsync(appFolder, ["--migrate-schema"], withMigrator: true, null, MigrationTimeout, cancellationToken).ConfigureAwait(false);
        log.Info(Tail(output));
        if (code == 3) throw new UpdateStepException("The schema is current but an applied migration was edited after it was applied (drift): see the log of the application.");
        if (code != 0) throw new UpdateStepException($"The migrations did not complete (exit code {code}): {Tail(error.Length > 0 ? error : output)}");
    }

    public async Task<string> BackupAsync(string appFolder, string actor, CancellationToken cancellationToken)
    {
        // The running version makes the backup (its tables, its schema). A version older than the kind "PreUpdate" does not know it: then the backup is the manual kind.
        var attempt = await WorkerAsync(appFolder, new { operation = "backup", operatorName = actor, operatorIsAdministrator = true, kind = "PreUpdate" }, BackupTimeout, cancellationToken).ConfigureAwait(false);
        if (attempt.Result is null && attempt.Error.Contains("PreUpdate", StringComparison.Ordinal))
        {
            log.Warn("The running version does not know the kind of backup \"before update\": making a manual one.");
            attempt = await WorkerAsync(appFolder, new { operation = "backup", operatorName = actor, operatorIsAdministrator = true, kind = "Manual" }, BackupTimeout, cancellationToken).ConfigureAwait(false);
        }
        if (attempt.Result is { } result && result.TryGetProperty("success", out var success) && success.GetBoolean() && result.TryGetProperty("packageFileName", out var name) && name.GetString() is { Length: > 0 } fileName)
            return fileName;
        throw new UpdateStepException("The backup of the database was not made: " + (attempt.Result is { } failed && failed.TryGetProperty("errorMessage", out var message) ? message.GetString() : attempt.Error.Length > 0 ? Tail(attempt.Error) : "no answer from the backup worker."));
    }

    public async Task RestoreAsync(string appFolder, string packageFileName, string actor, CancellationToken cancellationToken)
    {
        var attempt = await WorkerAsync(appFolder, new { operation = "restore", operatorName = actor, operatorIsAdministrator = true, package = packageFileName }, RestoreTimeout, cancellationToken).ConfigureAwait(false);
        if (attempt.Result is { } result && result.TryGetProperty("success", out var success) && success.GetBoolean()) return;
        throw new UpdateStepException("The restoration of the database failed: " + (attempt.Result is { } failed && failed.TryGetProperty("errorMessage", out var message) ? message.GetString() : Tail(attempt.Error)));
    }

    // ---- processes ----------------------------------------------------------------------------------------------------------------------------------------

    private sealed record WorkerAttempt(JsonElement? Result, string Error);

    private async Task<WorkerAttempt> WorkerAsync(string appFolder, object request, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var (exitCode, output, error) = await RunAsync(appFolder, ["--backup-worker"], withMigrator: false, JsonSerializer.Serialize(request, new JsonSerializerOptions(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull }), timeout, cancellationToken).ConfigureAwait(false);
        JsonElement? result = null;
        string? refusal = null;
        foreach (var line in output.Split('\n').Select(row => row.Trim().TrimStart('﻿')).Where(row => row.StartsWith('{')))
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                var type = document.RootElement.TryGetProperty("type", out var t) ? t.GetString() : null;
                if (type == "result") result = document.RootElement.GetProperty("payload").Clone();
                else if (type == "answer") refusal = document.RootElement.TryGetProperty("message", out var m) ? m.GetString() : "refused";
                else if (type == "progress" && document.RootElement.TryGetProperty("message", out var p)) log.Info("  " + p.GetString());
            }
            catch (JsonException) { }
        }
        return new WorkerAttempt(result, refusal ?? (error.Trim().Length > 0 ? error : $"the worker ended with code {exitCode} and no answer: {Tail(output)}"));
    }

    private async Task<(int Code, string Output, string Error)> RunAsync(string appFolder, IReadOnlyList<string> arguments, bool withMigrator, string? stdin, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var dll = Path.Combine(appFolder, "BlazorStoc.dll");
        if (!File.Exists(dll)) throw new UpdateStepException($"{dll} is missing.");
        var start = new ProcessStartInfo(settings.Dotnet)
        {
            WorkingDirectory = appFolder, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = stdin is not null, UseShellExecute = false, CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        start.ArgumentList.Add(dll);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        // An ordinary run of the application starts a web host: nothing from the environment of the supervisor is needed, but the settings of the installation are.
        foreach (var pair in settings.Environment ?? []) start.Environment[pair.Key] = pair.Value;
        if (settings.MariaRoot is { } maria && Directory.GetDirectories(maria, "mariadb-*").FirstOrDefault() is { } distribution && Path.Combine(distribution, "bin") is var bin && File.Exists(Path.Combine(bin, "mariadb-dump.exe")))
        {
            start.Environment["Database__MariaDumpExecutablePath"] = Path.Combine(bin, "mariadb-dump.exe");
            start.Environment["Database__MariaClientExecutablePath"] = Path.Combine(bin, "mariadb.exe");
        }
        if (withMigrator && File.Exists(layout.MigrationAccountFile)) start.Environment["Database__MigratorPrivateConfigPath"] = layout.MigrationAccountFile;

        using var process = Process.Start(start) ?? throw new UpdateStepException("dotnet could not be started.");
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        if (stdin is not null)
        {
            await process.StandardInput.WriteLineAsync(stdin.AsMemory(), cancellationToken).ConfigureAwait(false);
            await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
            // The input stays OPEN until the worker ends: for the worker the end of its input means "the application is gone", and a backup is then cancelled (Modules/Backup/BackupWorker.cs).
        }
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);
        try { await process.WaitForExitAsync(limit.Token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            if (cancellationToken.IsCancellationRequested) throw;
            throw new UpdateStepException($"{string.Join(' ', arguments)} did not finish within {timeout.TotalMinutes:0} minutes.");
        }
        return (process.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
    }

    /// <summary>Makes the administrator account with the installed application (BlazorStoc.dll --create-administrator, the details as one JSON line on its input). Returns the exit code of the application (0 made, 4 exists, 5 not valid) and its line.</summary>
    public async Task<(int Code, string Message)> CreateAdministratorAsync(string appFolder, string username, string displayName, string password, CancellationToken cancellationToken)
    {
        var request = JsonSerializer.Serialize(new { username, displayName, password });
        var (code, output, error) = await RunAsync(appFolder, ["--create-administrator"], withMigrator: false, request, TimeSpan.FromMinutes(3), cancellationToken).ConfigureAwait(false);
        var line = output.Split('\n').Select(row => row.Trim()).FirstOrDefault(row => row.StartsWith("ADMIN-", StringComparison.Ordinal));
        return (code, line ?? (code == 0 ? "The administrator account was made." : $"The administrator account was not made: {Tail(error.Length > 0 ? error : output)}"));
    }

    private static string SettingsDataRoot() => UpdaterSettings.DataRoot();

    private static string Tail(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= 1200 ? trimmed : "..." + trimmed[^1200..];
    }
}
