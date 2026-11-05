using System.Globalization;
using BlazorStoc.Services;
using Microsoft.AspNetCore.DataProtection;
using MySqlConnector;

namespace BlazorStoc.Modules.Elcomtur;

// The notifications of the automatic import of the Elcomtur backup (Setari -> Notificari, like every other source: it gets its starting template automatically). One source, with one
// instance for each situation that lasts:
//  1. "Backup Elcomtur lipsa": the import is on and no new backup was imported for the alert threshold (days); closed by the next import (the date moves).
//  2. "Preluare eșuata": the last check failed (cause in the text); closed by the next successful check.
//  3. "Backup preluat": the last import succeeded; stays in the list for a few days, then closes by itself.

public sealed record ElcomturAutoAlertState(ElcomturAutoSettings Settings, DateTime? LastAnyImportUtc);

public interface IElcomturAutoAlertReader
{
    Task<ElcomturAutoAlertState> GetAsync(CancellationToken cancellationToken = default);
}

public sealed class MariaElcomturAutoAlertReader(IConfiguration configuration, IDataProtectionProvider protection) : IElcomturAutoAlertReader
{
    public async Task<ElcomturAutoAlertState> GetAsync(CancellationToken cancellationToken = default)
    {
        var (settings, _) = await new MariaElcomturAutoStore(configuration, protection).ReadAsync(cancellationToken).ConfigureAwait(false);
        // The last import of any kind (also a manual one from the Elcomtur page): the balances carry the time of the import they came from.
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("SELECT MAX(imported_utc) FROM elcomtur_balances", connection);
        var text = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        return new ElcomturAutoAlertState(settings, MariaTimeText.ParseOrNull(text));
    }
}

public sealed class ElcomturAutoAlertSource(IElcomturAutoAlertReader reader, string key = ElcomturAutoAlertSource.SourceKey) : IExpirySource
{
    public const string SourceKey = "sistem.preluare-elcomtur";
    public const string ProblemName = "situatie elcomtur";
    public const string DetailsName = "detalii elcomtur";
    public const int MissingId = 1, FailedId = 2, DoneId = 3;
    public string Key => key;
    public string Category => "Sistem";
    public string EventName => "Preluare automată backup Elcomtur";
    public string DateLabel => "constatării";
    public int DefaultThresholdDays => 0;
    public string RemovedReason => "Situația preluării automate Elcomtur s-a schimbat.";
    public string DefaultSubject => "Elcomtur: <situatie elcomtur>";
    public string DefaultBody => "<detalii elcomtur> Vezi Setări → Preluare Elcomtur.";
    public IReadOnlyList<ExpiryPlaceholder> Placeholders { get; } =
    [
        new(ProblemName, "Ce s-a întâmplat cu preluarea automată (backup lipsă, preluare eșuată, backup preluat)", "backup lipsă"),
        new(DetailsName, "Detalii: cauza, fișierul, data, rezultatul", "Nu a apărut un backup nou de 4 zile. Ultima preluare: 01.11.2026 04:00.")
    ];

    public Task<string> RemovedReasonAsync(ExpiryNotification notification, CancellationToken cancellationToken = default) => Task.FromResult(notification.ObjectId switch
    {
        MissingId => "A fost preluat un backup Elcomtur nou sau preluarea automată a fost oprită.",
        FailedId => "O verificare ulterioară a reușit sau preluarea automată a fost oprită.",
        DoneId => $"Informarea s-a încheiat: au trecut {ElcomturAutoRules.SuccessNoticeDays} zile de la preluare sau a urmat o preluare nouă.",
        _ => RemovedReason
    });

    public string DateChangedReason(DateOnly from, DateOnly to, ExpiryInstance current) => current.ObjectId switch
    {
        MissingId => $"A fost preluat un backup nou: termenul s-a mutat de la {StockMovementRules.DisplayDate(from)} la {StockMovementRules.DisplayDate(to)}.",
        DoneId => $"A fost preluat un backup mai nou: data s-a mutat de la {StockMovementRules.DisplayDate(from)} la {StockMovementRules.DisplayDate(to)}.",
        _ => $"Problema s-a reluat: constatarea s-a mutat de la {StockMovementRules.DisplayDate(from)} la {StockMovementRules.DisplayDate(to)}."
    };

    private static DateOnly Day(DateTime utc) => DateOnly.FromDateTime(utc.ToLocalTime());

    /// <summary>The situations that last today, as instances.</summary>
    public static IReadOnlyList<ExpiryInstance> Instances(ElcomturAutoAlertState state, DateOnly today)
    {
        const string url = "/setari?tab=preluare-elcomtur";
        var settings = state.Settings;
        var result = new List<ExpiryInstance>();
        ExpiryInstance Make(int id, string label, DateOnly date, string problem, string details) =>
            new(id, label, date, new Dictionary<string, string> { [ProblemName] = problem, [DetailsName] = details }, url);
        if (settings.Enabled)
        {
            // The last import of any kind counts; before any, the day the automatic import was set up.
            var last = Latest(settings.LastImportUtc, state.LastAnyImportUtc);
            var since = last ?? settings.UpdatedUtc;
            result.Add(Make(MissingId, "Backup-ul aplicației Elcomtur", Day(since).AddDays(settings.AlertDays), "backup lipsă",
                $"În directorul {settings.Path} nu a apărut un backup nou al aplicației Elcomtur (mască {settings.Mask}) de {settings.AlertDays} {(settings.AlertDays == 1 ? "zi" : "zile")}. " +
                (last is null ? "Nu s-a preluat niciun backup până acum." : $"Ultima preluare: {ElcomturAutoRules.Describe(last)}.")));
            if (settings.FailingSinceUtc is { } failing)
                result.Add(Make(FailedId, "Preluarea automată Elcomtur", Day(failing), "preluare eșuată",
                    $"Ultima verificare ({ElcomturAutoRules.Describe(settings.LastCheckUtc)}) a eșuat: {(settings.LastMessage.Length > 0 ? settings.LastMessage : "cauză necunoscută")}"));
        }
        if (settings.LastImportUtc is { } imported && today.DayNumber - Day(imported).DayNumber <= ElcomturAutoRules.SuccessNoticeDays)
            result.Add(Make(DoneId, "Backup Elcomtur preluat", Day(imported), "backup preluat",
                $"Backup-ul {settings.LastFileName} a fost preluat la {ElcomturAutoRules.Describe(imported)} pentru {settings.FirmLabel}, date din {(settings.LastDataDate is { } data ? StockMovementRules.DisplayDate(data) : "—")}: {settings.LastSummary}."));
        return result;
    }

    private static DateTime? Latest(DateTime? first, DateTime? second) => first is null ? second : second is null ? first : first > second ? first : second;

    public async Task<IReadOnlyList<ExpiryInstance>> GetInstancesAsync(CancellationToken cancellationToken = default) =>
        Instances(await reader.GetAsync(cancellationToken).ConfigureAwait(false), DateOnly.FromDateTime(DateTime.Now));

    /// <summary>The notification of a lasting problem as it reads now: the read-only model shown in Settings.</summary>
    public static (string Subject, string Body) Preview(ElcomturAutoAlertState state, DateOnly today)
    {
        var instance = Instances(state, today).FirstOrDefault(item => item.ObjectId == MissingId);
        if (instance is null) return ("", "");
        var source = new ElcomturAutoAlertSource(null!);
        string Fill(string text) => instance.Values.Aggregate(text, (current, pair) => current.Replace("<" + pair.Key + ">", pair.Value, StringComparison.Ordinal));
        return (Fill(source.DefaultSubject), Fill(source.DefaultBody));
    }
}

/// <summary>
/// The scheduled check: once a minute the worker asks whether the check is due (the import is on, it is 04:00 or later and no scheduled attempt was made today) and runs it as the system
/// (no user). It waits while the module is out. Nothing here can take the application down: a failure is recorded by the check itself, anything unexpected is written to the log.
/// </summary>
public sealed class ElcomturAutoWorker(IServiceScopeFactory scopes, ModuleRegistry modules, ILogger<ElcomturAutoWorker> logger, TimeProvider? clock = null) : BackgroundService
{
    private readonly TimeProvider time = clock ?? TimeProvider.System;

    public TimeSpan Tick { get; init; } = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Let the application finish starting (the migrations, the first requests) before the first check.
            await Task.Delay(TimeSpan.FromSeconds(45), time, stoppingToken).ConfigureAwait(false);
            using var timer = new PeriodicTimer(Tick, time);
            do { await CheckAsync(stoppingToken).ConfigureAwait(false); }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>One check; returns the result of the run, or null (nothing due, module out, or an unexpected failure that was logged).</summary>
    public async Task<ElcomturAutoResult?> CheckAsync(CancellationToken cancellationToken)
    {
        if (!modules.IsAvailable(ElcomturModule.ModuleId)) return null;
        try
        {
            using var scope = scopes.CreateScope();
            var provider = scope.ServiceProvider;
            var configuration = provider.GetRequiredService<IConfiguration>();
            var audit = provider.GetRequiredService<IAuditTrail>();
            var system = new SystemAccess();
            var service = new ElcomturAutoService(configuration, provider.GetRequiredService<IDataProtectionProvider>(),
                new MariaBeneficiaryRepository(configuration, system, audit), new MariaElcomturBalanceRepository(configuration, system), system, audit,
                provider.GetRequiredService<ILogger<ElcomturAutoService>>(), time);
            return await service.RunScheduledAsync(time.GetLocalNow().DateTime, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            // The database may be busy (a backup), the module may be switched off between the check and the call: a line, and the next minute.
            logger.LogWarning("Elcomtur automatic import check failed ({ErrorType}).", exception.GetType().Name);
            return null;
        }
    }
}
