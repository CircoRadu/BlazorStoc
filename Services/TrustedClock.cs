using System.Net;
using System.Net.Sockets;

namespace BlazorStoc.Services;

// "Now" matters for the backups: the date of a package, when the daily backup is due, which packages are old enough to be removed. If someone sets the server
// date for another application running there, those decisions go wrong. So the server clock is compared with the internet time: NTP servers chosen in
// Settings -> Backup (two that answer must agree), then the Date header of an HTTPS reply as a last resort.
//   - a removal of old packages waits while the clock cannot be trusted (tolerance Backup:TimeCheck:ToleranceMinutes, 5 by default);
//   - a backup takes the internet time when the clock differs by more than Backup:TimeCheck:BackupToleranceMinutes (15 by default), and when the time cannot be
//     checked at all the package is marked "ora neverificata" (never removed automatically).
// Other configuration (Backup:TimeCheck): HttpUrls.

public sealed record ClockCheck(bool Trusted, DateTime? ReferenceUtc, TimeSpan? Skew, string Message);

public sealed record TimeCheckSettings(bool Enabled, IReadOnlyList<string> Servers);

public sealed record TimeProbeEntry(string Host, DateTime? Utc, DateTime ServerUtcAtReply);

public static class TrustedClock
{
    public const string SourceVerified = "Verified", SourceInternet = "Internet", SourceUnverified = "Unverified";
    public const int DefaultToleranceMinutes = 5, DefaultBackupToleranceMinutes = 15;
    /// <summary>Two servers that answer must give the same offset to the server clock within this time.</summary>
    public static readonly TimeSpan AgreementTolerance = TimeSpan.FromMinutes(1);
    public static readonly IReadOnlyList<string> DefaultServers = ["time.cloudflare.com", "pool.ntp.org", "time.windows.com"];
    private static readonly string[] DefaultHttpUrls = ["https://www.cloudflare.com/", "https://www.microsoft.com/"];
    private static readonly DateTime NtpEpoch = new(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>The decision for one reference time (null = could not be read) against the server clock.</summary>
    public static ClockCheck Evaluate(DateTime serverUtc, DateTime? referenceUtc, TimeSpan tolerance)
    {
        if (referenceUtc is not { } reference)
            return new(false, null, null, "Ora serverului nu a putut fi verificată pe internet, deci ștergerea automată a pachetelor vechi este amânată.");
        var skew = serverUtc - reference;
        if (skew.Duration() > tolerance)
            return new(false, reference, skew, $"Ceasul serverului diferă cu {Math.Round(skew.Duration().TotalMinutes)} min de ora de pe internet (se acceptă {tolerance.TotalMinutes:0}), deci ștergerea automată a pachetelor vechi este amânată până la corectarea lui.");
        return new(true, reference, skew, "");
    }

    /// <summary>The decision for the answers received (server clock and reference read at the same moment): none = unverifiable, two that disagree = unverifiable.</summary>
    public static ClockCheck Decide(IReadOnlyList<(DateTime ServerUtc, DateTime ReferenceUtc)> answers, TimeSpan tolerance)
    {
        if (answers.Count == 0) return Evaluate(DateTime.UtcNow, null, tolerance);
        if (answers.Count > 1 && (answers[0].ServerUtc - answers[0].ReferenceUtc - (answers[1].ServerUtc - answers[1].ReferenceUtc)).Duration() > AgreementTolerance)
            return new(false, null, null, "Serverele de timp consultate dau ore diferite între ele, deci ora serverului nu poate fi verificată și ștergerea automată a pachetelor vechi este amânată.");
        return Evaluate(answers[0].ServerUtc, answers[0].ReferenceUtc, tolerance);
    }

    /// <summary>The time and its source for something created now: the server clock when it is trusted, the internet time when the clock is wrong, the server clock marked unverified when nothing could be checked.</summary>
    public static (DateTime Utc, string Source) Stamp(DateTime serverNowUtc, ClockCheck check) =>
        check.Trusted ? (serverNowUtc, SourceVerified)
        : check is { Skew: { } skew, ReferenceUtc: not null } ? (serverNowUtc - skew, SourceInternet)
        : (serverNowUtc, SourceUnverified);

    public static async Task<ClockCheck> CheckAsync(IConfiguration configuration, TimeCheckSettings settings, CancellationToken cancellationToken, bool forBackup = false, TimeSpan? overall = null)
    {
        if (!settings.Enabled) return new(true, null, null, "");
        var section = configuration.GetSection("Backup:TimeCheck");
        var key = forBackup ? "BackupToleranceMinutes" : "ToleranceMinutes";
        var tolerance = TimeSpan.FromMinutes(int.TryParse(section[key], out var minutes) && minutes is >= 1 and <= 1440 ? minutes : forBackup ? DefaultBackupToleranceMinutes : DefaultToleranceMinutes);
        var urls = section.GetSection("HttpUrls").Get<string[]>() is { Length: > 0 } configuredUrls ? configuredUrls : DefaultHttpUrls;
        var servers = settings.Servers.Count > 0 ? settings.Servers : DefaultServers;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (overall is { } limit) budget.CancelAfter(limit);
        var answers = new List<(DateTime ServerUtc, DateTime ReferenceUtc)>();
        try
        {
            foreach (var host in servers)
            {
                if (await QueryNtpAsync(host, budget.Token).ConfigureAwait(false) is { } reply) answers.Add((reply.ServerUtc, reply.Utc));
                if (answers.Count == 2) break;
            }
            if (answers.Count == 0)
                foreach (var url in urls)
                {
                    var date = await QueryHttpDateAsync(url, budget.Token).ConfigureAwait(false);
                    if (date is { } reference) { answers.Add((DateTime.UtcNow, reference)); break; }
                }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { /* the time budget ran out: decide with what came back */ }
        return Decide(answers, tolerance);
    }

    /// <summary>Every server asked at once, for the "Verifică acum" button: what each one answered (null = no answer).</summary>
    public static async Task<IReadOnlyList<TimeProbeEntry>> ProbeAsync(IReadOnlyList<string> servers, CancellationToken cancellationToken)
    {
        var tasks = servers.Select(async host =>
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var reply = await QueryNtpAsync(host, timeout.Token).ConfigureAwait(false);
                return new TimeProbeEntry(host, reply?.Utc, reply?.ServerUtc ?? DateTime.UtcNow);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return new TimeProbeEntry(host, null, DateTime.UtcNow); }
        });
        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <summary>The transmit timestamp (bytes 40-47) of an NTP reply; null for a reply that is too short or has no time.</summary>
    public static DateTime? ParseNtpReply(byte[] reply)
    {
        if (reply.Length < 48) return null;
        var seconds = ((ulong)reply[40] << 24) | ((ulong)reply[41] << 16) | ((ulong)reply[42] << 8) | reply[43];
        return seconds == 0 ? null : NtpEpoch.AddSeconds(seconds);
    }

    private static async Task<(DateTime Utc, DateTime ServerUtc)?> QueryNtpAsync(string host, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            using var client = new UdpClient();
            var request = new byte[48];
            request[0] = 0x1B; // version 3, client mode
            await client.SendAsync(request, request.Length, host, 123).ConfigureAwait(false);
            var result = await client.ReceiveAsync(timeout.Token).ConfigureAwait(false);
            var serverUtc = DateTime.UtcNow;
            return ParseNtpReply(result.Buffer) is { } utc ? (utc, serverUtc) : null;
        }
        catch (Exception exception) when (exception is SocketException or OperationCanceledException or ObjectDisposedException or InvalidOperationException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }
    }

    private static async Task<DateTime?> QueryHttpDateAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, url), HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            return response.Headers.Date?.UtcDateTime;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or OperationCanceledException or InvalidOperationException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }
    }
}
