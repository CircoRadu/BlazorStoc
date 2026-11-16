using static BlazorStoc.Services.MariaDb;

namespace BlazorStoc.Services;

// Table `user_display_settings` (migration 90): one row per user and scope, JSON text in `settings`. Only the signed-in user's own rows are read or written.
public sealed class MariaUserDisplayRepository(IConfiguration configuration, IAccessControl? accessControl = null) : IUserDisplayRepository
{
    public async Task<UserDisplayData> GetAsync(CancellationToken cancellationToken = default)
    {
        if (await UserIdAsync(cancellationToken).ConfigureAwait(false) is not { } userId) return UserDisplayData.Empty;
        await using var connection = await OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, "SELECT scope, settings FROM user_display_settings WHERE user_id=@user", ("@user", userId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var global = new DisplaySettings();
        var tables = new Dictionary<string, DisplaySettings>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var scope = reader.GetString(0);
            var settings = DisplayRules.FromJson(reader.GetString(1));
            if (scope == DisplayRules.GlobalScope) global = settings; else if (!settings.IsEmpty) tables[scope] = settings;
        }
        return new(global, tables);
    }

    public async Task SaveAsync(string scope, DisplaySettings settings, CancellationToken cancellationToken = default)
    {
        if (!DisplayRules.IsValidScope(scope)) throw new ArgumentException("Zona setÄƒrilor de afiÈ™are nu este validÄƒ.", nameof(scope));
        var userId = await UserIdAsync(cancellationToken).ConfigureAwait(false) ?? throw new AccessDeniedException("SetÄƒrile de afiÈ™are se salveazÄƒ numai pentru un utilizator autentificat.");
        await using var connection = await OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        if (settings.IsEmpty)
        {
            await using var delete = Command(connection, "DELETE FROM user_display_settings WHERE user_id=@user AND scope=@scope", ("@user", userId), ("@scope", scope));
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return;
        }
        await using var command = Command(connection, """
            INSERT INTO user_display_settings (user_id, scope, settings, updated_utc) VALUES (@user, @scope, @settings, @now)
            ON DUPLICATE KEY UPDATE settings=VALUES(settings), updated_utc=VALUES(updated_utc)
            """, ("@user", userId), ("@scope", scope), ("@settings", DisplayRules.ToJson(settings)), ("@now", MariaTimeText.Format(MariaTimeText.Now())));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ClearAllAsync(CancellationToken cancellationToken = default)
    {
        var userId = await UserIdAsync(cancellationToken).ConfigureAwait(false) ?? throw new AccessDeniedException("SetÄƒrile de afiÈ™are se È™terg numai pentru un utilizator autentificat.");
        await using var connection = await OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, "DELETE FROM user_display_settings WHERE user_id=@user", ("@user", userId));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<int?> UserIdAsync(CancellationToken token) => accessControl is null ? null : await accessControl.GetUserIdAsync(token).ConfigureAwait(false);
}
