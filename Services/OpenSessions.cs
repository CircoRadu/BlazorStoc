using System.Collections.Concurrent;
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace BlazorStoc.Services;

// The open interactive sessions (Blazor circuits), for Setari -> Baza de date -> Stare: who is connected, from which address, since when and on which page. The registry lives in
// memory only (it starts empty with the application and keeps nothing after a session closes); the details are shown to administrators only.

/// <summary>One open session. The user, the address and the browser are filled in after the circuit opens (the first by the tracker in the layout, the others by the page itself).</summary>
public sealed record OpenSession(string Id, string? UserName, string? DisplayName, string? Role, string? IpAddress, string? Browser, DateTime StartedUtc, DateTime LastSeenUtc,
    string? Page, bool Connected);

public sealed class OpenSessionRegistry(TimeProvider? clock = null)
{
    private readonly ConcurrentDictionary<string, OpenSession> sessions = new(StringComparer.Ordinal);
    private readonly TimeProvider time = clock ?? TimeProvider.System;

    public int Count => sessions.Count;

    public void Opened(string id)
    {
        var now = time.GetUtcNow().UtcDateTime;
        sessions[id] = new(id, null, null, null, null, null, now, now, null, true);
    }

    public void Closed(string id) { sessions.TryRemove(id, out _); closers.TryRemove(id, out _); }

    public void ConnectionChanged(string id, bool connected) => Update(id, session => session with { Connected = connected });

    public void Identify(string id, string? userName, string? displayName, string? role, string? page) =>
        Update(id, session => session with { UserName = userName, DisplayName = displayName, Role = role, Page = page ?? session.Page });

    public void SetPage(string id, string? page) => Update(id, session => session with { Page = page });

    /// <summary>The address and the browser, reported by the page of the session itself. Accepted only for a session that exists and belongs to the same signed-in user,
    /// so one user cannot write the details of another's session.</summary>
    public bool SetNetwork(string id, string? ipAddress, string? browser, string? signedInUser)
    {
        if (!sessions.TryGetValue(id, out var session)) return false;
        if (session.UserName is null || !string.Equals(session.UserName, signedInUser, StringComparison.Ordinal)) return false;
        Update(id, current => current with { IpAddress = ipAddress, Browser = browser });
        return true;
    }

    // ---- Closing a session from the list ---------------------------------------------------------------------------------------
    // Blazor has no call that ends a circuit from outside: the page of the session (the tracker in the layout) registers a function that sends its browser to the closing address
    // (Startup/SessionEndpoints.cs), which signs that browser out; the circuit ends with the page. The address carries a one-time token, so a link cannot sign anybody out.
    private readonly ConcurrentDictionary<string, Func<string, Task>> closers = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTime> closeTokens = new(StringComparer.Ordinal);
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(2);

    public void RegisterCloser(string id, Func<string, Task> closer) => closers[id] = closer;

    public void UnregisterCloser(string id) => closers.TryRemove(id, out _);

    public bool CanClose(string id) => closers.ContainsKey(id);

    /// <summary>Asks the session to end. False when the session does not exist or cannot be closed from here (its page has not registered).</summary>
    public async Task<bool> RequestCloseAsync(string id)
    {
        if (!closers.TryGetValue(id, out var closer)) return false;
        var now = time.GetUtcNow().UtcDateTime;
        foreach (var old in closeTokens.Where(pair => now - pair.Value > TokenLifetime).Select(pair => pair.Key)) closeTokens.TryRemove(old, out _);
        var token = Guid.NewGuid().ToString("N");
        closeTokens[token] = now;
        await closer(token).ConfigureAwait(false);
        return true;
    }

    public bool ConsumeCloseToken(string? token) =>
        !string.IsNullOrEmpty(token) && closeTokens.TryRemove(token, out var issued) && time.GetUtcNow().UtcDateTime - issued <= TokenLifetime;

    public IReadOnlyList<OpenSession> Snapshot() => [.. sessions.Values.OrderBy(session => session.StartedUtc).ThenBy(session => session.Id, StringComparer.Ordinal)];

    private void Update(string id, Func<OpenSession, OpenSession> change)
    {
        var now = time.GetUtcNow().UtcDateTime;
        while (sessions.TryGetValue(id, out var current))
            if (sessions.TryUpdate(id, change(current) with { LastSeenUtc = now }, current)) return;
    }
}

/// <summary>The identifier of the circuit of the current session, for the components of that session (set when the circuit opens).</summary>
public sealed class CircuitHolder
{
    public string? Id { get; set; }
}

/// <summary>The number of open interactive sessions (Blazor circuits).</summary>
public sealed class CircuitCounter
{
    private int count;
    public int Count => Volatile.Read(ref count);
    internal void Opened() => Interlocked.Increment(ref count);
    internal void Closed() => Interlocked.Decrement(ref count);
}

public sealed class CircuitCountHandler(CircuitCounter counter, OpenSessionRegistry registry, CircuitHolder holder) : CircuitHandler
{
    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        holder.Id = circuit.Id;
        counter.Opened();
        registry.Opened(circuit.Id);
        return Task.CompletedTask;
    }

    public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        counter.Closed();
        registry.Closed(circuit.Id);
        return Task.CompletedTask;
    }

    public override Task OnConnectionDownAsync(Circuit circuit, CancellationToken cancellationToken) { registry.ConnectionChanged(circuit.Id, false); return Task.CompletedTask; }

    public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken) { registry.ConnectionChanged(circuit.Id, true); return Task.CompletedTask; }
}

/// <summary>A short, readable form of the User-Agent header ("Edge · Windows"); the whole header is long and says little to the administrator.</summary>
public static class UserAgentText
{
    public static string Summarize(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent)) return "necunoscut";
        var browser = userAgent switch
        {
            _ when userAgent.Contains("Edg/", StringComparison.Ordinal) || userAgent.Contains("EdgA/", StringComparison.Ordinal) => "Edge",
            _ when userAgent.Contains("OPR/", StringComparison.Ordinal) => "Opera",
            _ when userAgent.Contains("Firefox/", StringComparison.Ordinal) => "Firefox",
            _ when userAgent.Contains("Chrome/", StringComparison.Ordinal) || userAgent.Contains("CriOS/", StringComparison.Ordinal) => "Chrome",
            _ when userAgent.Contains("Safari/", StringComparison.Ordinal) => "Safari",
            _ => "alt browser"
        };
        var system = userAgent switch
        {
            _ when userAgent.Contains("Windows", StringComparison.Ordinal) => "Windows",
            _ when userAgent.Contains("Android", StringComparison.Ordinal) => "Android",
            _ when userAgent.Contains("iPhone", StringComparison.Ordinal) || userAgent.Contains("iPad", StringComparison.Ordinal) => "iOS",
            _ when userAgent.Contains("Mac OS", StringComparison.Ordinal) => "macOS",
            _ when userAgent.Contains("Linux", StringComparison.Ordinal) => "Linux",
            _ => "sistem necunoscut"
        };
        return $"{browser} · {system}";
    }
}
