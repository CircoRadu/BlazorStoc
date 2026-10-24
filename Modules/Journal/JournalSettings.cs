using BlazorStoc.Services;
using MySqlConnector;

namespace BlazorStoc.Modules.Journal;

// Setari -> Jurnal activitate: how many of the most recent events the page Jurnal activitate works on by default. The administrator sets it; nothing is deleted from the
// journal (the complete journal shows every event whatever the number is).

public static class JournalDisplayRules
{
    /// <summary>The smallest number of recent events the page can be set to.</summary>
    public const int MinRecentWindow = 50;
    /// <summary>What the page works on until the administrator changes it (what it always showed).</summary>
    public const int DefaultRecentWindow = AuditQueryRules.RecentWindow;
    /// <summary>The largest number: one query never returns more rows than this (<see cref="AuditQueryRules.MaxRows"/>).</summary>
    public const int MaxRecentWindow = AuditQueryRules.MaxRows;
    public static readonly string RangeMessage = $"Numărul de evenimente afișate trebuie să fie între {MinRecentWindow:N0} și {MaxRecentWindow:N0}.";

    public static bool Valid(int value) => value is >= MinRecentWindow and <= MaxRecentWindow;
}

public interface IJournalSettings
{
    /// <summary>The number of recent events the journal page works on (the default when nothing is saved).</summary>
    Task<int> GetRecentWindowAsync(CancellationToken cancellationToken = default);
    Task SaveRecentWindowAsync(int value, CancellationToken cancellationToken = default);
}

public sealed class MariaJournalSettings(IConfiguration configuration, IAccessControl access, IAuditTrail audit) : IJournalSettings
{
    // The page reads the number at every refresh (every 15 seconds): a short cache keeps that from touching the database each time; a save clears it.
    private static readonly object Gate = new();
    private static (int Value, DateTime At)? cached;
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);

    public async Task<int> GetRecentWindowAsync(CancellationToken cancellationToken = default)
    {
        lock (Gate) if (cached is { } hit && DateTime.UtcNow - hit.At < Lifetime) return hit.Value;
        var value = await ReadAsync(cancellationToken).ConfigureAwait(false);
        lock (Gate) cached = (value, DateTime.UtcNow);
        return value;
    }

    private async Task<int> ReadAsync(CancellationToken cancellationToken)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("SELECT recent_window FROM journal_settings WHERE id=1", connection);
        var stored = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return stored is int value && JournalDisplayRules.Valid(value) ? value : JournalDisplayRules.DefaultRecentWindow;
    }

    public async Task SaveRecentWindowAsync(int value, CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync("setari-jurnal.edit", cancellationToken).ConfigureAwait(false);
        if (!JournalDisplayRules.Valid(value)) throw new JournalSettingsException(JournalDisplayRules.RangeMessage);
        var old = await ReadAsync(cancellationToken).ConfigureAwait(false);
        await using (var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false))
        {
            await using var command = new MySqlCommand("""
                INSERT INTO journal_settings (id,recent_window,updated_by,updated_utc) VALUES (1,@value,@by,@now)
                ON DUPLICATE KEY UPDATE recent_window=@value,updated_by=@by,updated_utc=@now
                """, connection);
            command.Parameters.AddWithValue("@value", value);
            command.Parameters.AddWithValue("@by", await access.GetUsernameAsync(cancellationToken).ConfigureAwait(false) ?? "");
            command.Parameters.AddWithValue("@now", MariaTimeText.Format(DateTime.UtcNow));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        lock (Gate) cached = (value, DateTime.UtcNow);
        if (old != value)
            await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.JournalSettings, AuditActions.SetJournalDisplay, "", "Setări jurnal activitate",
                AuditDetails.Changes([new AuditChange("Evenimente afișate implicit în Jurnal activitate", $"{old:N0}", $"{value:N0}")]), "", cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>An answer of the journal settings for the user (a number out of range), not a failure of the module.</summary>
public sealed class JournalSettingsException(string message) : Exception(message), IExpectedAnswer;

// The stand-in while the module is out: reading gives the default, saving says the module is out.
internal sealed class UnavailableJournalSettings : IJournalSettings
{
    public Task<int> GetRecentWindowAsync(CancellationToken cancellationToken = default) => Task.FromResult(JournalDisplayRules.DefaultRecentWindow);
    public Task SaveRecentWindowAsync(int value, CancellationToken cancellationToken = default) => Task.FromException(new ModuleUnavailableException(JournalModule.ModuleId));
}
