using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using MySqlConnector;
using static BlazorStoc.Services.MariaDb;

namespace BlazorStoc.Services;

/// <summary>RFC 6238 time-based one-time passwords (Google/Microsoft Authenticator, Aegis...): 6 digits, 30 seconds, HMAC-SHA1.</summary>
public static class Totp
{
    public const int StepSeconds = 30, Digits = 6, SecretBytes = 20;
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static byte[] NewSecret() => RandomNumberGenerator.GetBytes(SecretBytes);

    public static string ToBase32(byte[] data)
    {
        var result = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;
        foreach (var value in data)
        {
            buffer = (buffer << 8) | value; bits += 8;
            while (bits >= 5) { result.Append(Alphabet[(buffer >> (bits - 5)) & 31]); bits -= 5; }
        }
        if (bits > 0) result.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        return result.ToString();
    }

    public static byte[]? FromBase32(string text)
    {
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var raw in text.Where(character => character is not (' ' or '-' or '=')))
        {
            var index = Alphabet.IndexOf(char.ToUpperInvariant(raw));
            if (index < 0) return null;
            buffer = (buffer << 5) | index; bits += 5;
            if (bits >= 8) { bytes.Add((byte)((buffer >> (bits - 8)) & 255)); bits -= 8; }
        }
        return bytes.ToArray();
    }

    public static long StepOf(DateTimeOffset moment) => moment.ToUnixTimeSeconds() / StepSeconds;

    public static string Compute(byte[] secret, long step)
    {
        Span<byte> counter = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(counter, step);
        var hash = HMACSHA1.HashData(secret, counter);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Accepts the code of the current step and of one step before or after (clock drift). A step at or before <paramref name="lastUsedStep"/> is refused, so a code cannot be reused.</summary>
    public static bool Verify(byte[] secret, string? code, DateTimeOffset now, long lastUsedStep, out long matchedStep)
    {
        matchedStep = 0;
        var typed = new string((code ?? "").Where(char.IsDigit).ToArray());
        if (typed.Length != Digits) return false;
        var current = StepOf(now);
        for (var step = current - 1; step <= current + 1; step++)
        {
            if (step <= lastUsedStep) continue;
            if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Compute(secret, step)), Encoding.ASCII.GetBytes(typed))) { matchedStep = step; return true; }
        }
        return false;
    }

    public static string ProvisioningUri(string issuer, string account, string secretBase32) =>
        $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(account)}?secret={secretBase32}&issuer={Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits={Digits}&period={StepSeconds}";

    /// <summary>Ten single-use codes of the form XXXXX-XXXXX (no look-alike characters).</summary>
    public static IReadOnlyList<string> NewRecoveryCodes(int count = 10)
    {
        const string letters = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var codes = new List<string>();
        for (var i = 0; i < count; i++)
        {
            var chars = new char[11];
            for (var j = 0; j < 11; j++) chars[j] = j == 5 ? '-' : letters[RandomNumberGenerator.GetInt32(letters.Length)];
            codes.Add(new string(chars));
        }
        return codes;
    }

    public static string HashRecoveryCode(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(NormalizeRecoveryCode(code))));

    public static string NormalizeRecoveryCode(string code) => new string((code ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
}

/// <summary>Setari -> Autentificare: when a code from the authenticator application is needed (never, outside the internal network, always) and which addresses are the internal network.</summary>
public enum TwoFactorMode { Off, External, Everyone }

public sealed record AccessSecurityOptions(TwoFactorMode Mode, IReadOnlyList<string> InternalNetworks)
{
    public static AccessSecurityOptions Off { get; } = new(TwoFactorMode.Off, []);
}

public static class AccessSecurityRules
{
    public const string FileName = "access-security.json";

    /// <summary>When no network is written, the private ranges and the computer itself are the internal network.</summary>
    public static readonly string[] DefaultNetworks = ["127.0.0.0/8", "::1/128", "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "169.254.0.0/16", "fc00::/7", "fe80::/10"];

    public static string PathFor(IConfiguration configuration) =>
        configuration["Security:AccessPath"] is { Length: > 0 } configured ? configured : BlazorStoc.Startup.AppDataRoot.Combine(configuration, FileName);

    public static AccessSecurityOptions Read(IConfiguration configuration)
    {
        var section = configuration.GetSection("Security:Access");
        var networks = section.GetSection("InternalNetworks").GetChildren().Select(item => item.Value).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!.Trim()).ToArray();
        var mode = Enum.TryParse<TwoFactorMode>(section["Mode"], true, out var parsed) ? parsed
            : section.GetValue("ExternalTwoFactor", false) ? TwoFactorMode.External : TwoFactorMode.Off;   // the first version of the file only had the on/off flag for the outside
        return new(mode, networks);
    }

    public static string? Validate(AccessSecurityOptions options)
    {
        foreach (var entry in options.InternalNetworks) if (ReverseProxyRules.ValidateEntry(entry) is { } problem) return problem;
        return null;
    }

    public static void Write(string path, AccessSecurityOptions options)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(new { Security = new { Access = new { Mode = options.Mode.ToString(), options.InternalNetworks } } }, new JsonSerializerOptions { WriteIndented = true });
        DatabaseOverrideFile.WriteRaw(path, json);
    }

    /// <summary>True when the address belongs to the internal network. An unknown address (null) is external. The computer itself (loopback) is always internal.</summary>
    public static bool IsInternal(IPAddress? address, AccessSecurityOptions options)
    {
        if (address is null) return false;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return true;
        var entries = options.InternalNetworks.Count == 0 ? DefaultNetworks : options.InternalNetworks.ToArray();
        foreach (var entry in entries)
        {
            var slash = entry.IndexOf('/');
            if (!IPAddress.TryParse(slash < 0 ? entry : entry[..slash], out var prefix)) continue;
            if (prefix.IsIPv4MappedToIPv6) prefix = prefix.MapToIPv4();
            if (prefix.AddressFamily != address.AddressFamily) continue;
            var length = prefix.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128;
            if (slash >= 0 && !int.TryParse(entry[(slash + 1)..], out length)) continue;
            if (Matches(address.GetAddressBytes(), prefix.GetAddressBytes(), length)) return true;
        }
        return false;
    }

    private static bool Matches(byte[] address, byte[] prefix, int bits)
    {
        var full = bits / 8;
        for (var i = 0; i < full && i < address.Length; i++) if (address[i] != prefix[i]) return false;
        var rest = bits % 8;
        if (rest == 0 || full >= address.Length) return true;
        var mask = (byte)(0xFF << (8 - rest));
        return (address[full] & mask) == (prefix[full] & mask);
    }

    /// <summary>The code is needed when the mode asks it for everybody, or for the clients outside the internal network.</summary>
    public static bool IsCodeRequired(IConfiguration configuration, IPAddress? client)
    {
        var options = Read(configuration);
        return options.Mode switch { TwoFactorMode.Everyone => true, TwoFactorMode.External => !IsInternal(client, options), _ => false };
    }

    /// <summary>True only for a request made on the server itself: a loopback address and no sign of a proxy in front (a proxy on the same computer would otherwise look local).</summary>
    public static bool IsServerLocal(IPAddress? remote, Microsoft.AspNetCore.Http.IHeaderDictionary? headers)
    {
        if (remote is null || !IPAddress.IsLoopback(remote.IsIPv4MappedToIPv6 ? remote.MapToIPv4() : remote)) return false;
        return headers is null || (!headers.ContainsKey("X-Forwarded-For") && !headers.ContainsKey("X-Original-For") && !headers.ContainsKey("Forwarded"));
    }
}

/// <summary>Counts wrong attempts per key and locks the key for 10 minutes after 5 of them; every further lock doubles the time (up to 24 hours). A key that is left alone for 30 minutes starts over.</summary>
public sealed class FailureThrottle<TKey> where TKey : notnull
{
    public const int MaxFailures = 5;
    public static readonly TimeSpan BaseLock = TimeSpan.FromMinutes(10), MaxLock = TimeSpan.FromHours(24), Forget = TimeSpan.FromMinutes(30);
    private sealed record State(int Failures, int Level, DateTimeOffset LockedUntil, DateTimeOffset Last);
    private readonly ConcurrentDictionary<TKey, State> states = new();

    public DateTimeOffset? LockedUntil(TKey key) => states.TryGetValue(key, out var state) && state.LockedUntil > DateTimeOffset.UtcNow ? state.LockedUntil : null;

    public void Fail(TKey key)
    {
        var now = DateTimeOffset.UtcNow;
        states.AddOrUpdate(key, _ => Next(new(0, 0, DateTimeOffset.MinValue, now), now), (_, old) =>
            old.LockedUntil > now ? old : Next(now - old.Last > Forget ? new(0, 0, DateTimeOffset.MinValue, now) : old, now));
    }

    private static State Next(State old, DateTimeOffset now)
    {
        var failures = old.Failures + 1;
        if (failures < MaxFailures) return new(failures, old.Level, DateTimeOffset.MinValue, now);
        var level = old.Level + 1;
        var duration = TimeSpan.FromTicks(Math.Min(MaxLock.Ticks, BaseLock.Ticks * (1L << Math.Min(level - 1, 20))));
        return new(0, level, now + duration, now);
    }

    public void Clear(TKey key) => states.TryRemove(key, out _);
}

public sealed record TwoFactorStatus(bool Exists, bool Confirmed, bool Suspended = false, string? ConfirmedUtc = null, string? LastUsedUtc = null, string? LastUsedIp = null,
    string? EnrolledBy = null, string? ConfirmedBy = null, int RecoveryLeft = 0, DateTimeOffset? LockedUntil = null)
{
    public bool Active => Confirmed && !Suspended;
}

public enum TwoFactorCheck { Valid, ValidRecoveryCode, Wrong, LockedOut, NotEnrolled, Suspended }

public interface ITwoFactorStore
{
    Task<TwoFactorStatus> GetStatusAsync(int userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<int, TwoFactorStatus>> GetStatusesAsync(CancellationToken cancellationToken = default);
    /// <summary>The secret (base 32) of an enrollment started and not yet confirmed, so the QR code can be shown again; null otherwise.</summary>
    Task<string?> GetPendingSecretAsync(int userId, CancellationToken cancellationToken = default);
    /// <summary>The administrator starts the enrollment: a new secret (base 32), pending until a first valid code confirms it. Null when a confirmed authenticator exists: it must be revoked first.</summary>
    Task<string?> ProvisionAsync(int userId, string by, CancellationToken cancellationToken = default);
    /// <summary>Confirms the secret with a first valid code and returns the recovery codes (shown once); null when the code is wrong.</summary>
    Task<IReadOnlyList<string>?> ConfirmAsync(int userId, string code, string by, CancellationToken cancellationToken = default);
    /// <summary>A valid authenticator code, or an unspent recovery code (spent by this call). Wrong attempts are counted per account.</summary>
    Task<TwoFactorCheck> VerifyAsync(int userId, string code, string? clientAddress = null, CancellationToken cancellationToken = default);
    /// <summary>Removes the authenticator and its recovery codes.</summary>
    Task<bool> RevokeAsync(int userId, CancellationToken cancellationToken = default);
    Task<bool> SetSuspendedAsync(int userId, bool suspended, CancellationToken cancellationToken = default);
    /// <summary>Ten new recovery codes (the old ones stop working); null when there is no confirmed authenticator.</summary>
    Task<IReadOnlyList<string>?> RegenerateRecoveryCodesAsync(int userId, CancellationToken cancellationToken = default);
    void Unlock(int userId);
    /// <summary>The first half of changing the phone: a new secret (base 32) waits next to the old one, which keeps working. Null when there is no confirmed authenticator.</summary>
    Task<string?> BeginReplacementAsync(int userId, CancellationToken cancellationToken = default);
    /// <summary>The second half: a valid code from the new phone makes the new secret the only one (the old one stops working) and gives new recovery codes; null when the code is wrong or nothing waits.</summary>
    Task<IReadOnlyList<string>?> ConfirmReplacementAsync(int userId, string code, string by, CancellationToken cancellationToken = default);
    /// <summary>Drops the waiting secret (the dialog was closed): nothing changes.</summary>
    Task CancelReplacementAsync(int userId, CancellationToken cancellationToken = default);
}

/// <summary>The statements that must run inside the transaction of the user repository (delete the user, make it inactive).</summary>
internal static class TwoFactorSql
{
    public static async Task DeleteForUserAsync(MySqlConnection connection, MySqlTransaction transaction, int userId, CancellationToken cancellationToken)
    {
        await using (var totp = Command(connection, transaction, "DELETE FROM web_user_totp WHERE user_id=@id", ("@id", userId))) await totp.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await using (var codes = Command(connection, transaction, "DELETE FROM web_user_recovery_codes WHERE user_id=@id", ("@id", userId))) await codes.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task SuspendForUserAsync(MySqlConnection connection, MySqlTransaction transaction, int userId, CancellationToken cancellationToken)
    {
        await using var command = Command(connection, transaction, "UPDATE web_user_totp SET suspended_utc=@now WHERE user_id=@id AND suspended_utc IS NULL", ("@now", DateTime.UtcNow.ToString("O")), ("@id", userId));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}

public sealed class MariaTwoFactorStore(IConfiguration configuration, IDataProtectionProvider protection) : ITwoFactorStore
{
    private static readonly FailureThrottle<int> Throttle = new();
    private readonly IDataProtector protector = protection.CreateProtector("BlazorStoc.TwoFactor.Secret");

    public void Unlock(int userId) => Throttle.Clear(userId);

    private const string StatusColumns = """
        t.user_id,t.confirmed_utc,t.suspended_utc,t.last_used_utc,t.last_used_ip,t.enrolled_by,t.confirmed_by,
        (SELECT COUNT(*) FROM web_user_recovery_codes r WHERE r.user_id=t.user_id AND r.used_utc IS NULL)
        """;

    private static (int Id, TwoFactorStatus Status) ReadStatus(System.Data.Common.DbDataReader reader)
    {
        string? Text(int index) => reader.IsDBNull(index) ? null : reader.GetString(index);
        var id = reader.GetInt32(0);
        return (id, new(true, !reader.IsDBNull(1), !reader.IsDBNull(2), Text(1), Text(3), Text(4), Text(5), Text(6), Convert.ToInt32(reader.GetValue(7)), Throttle.LockedUntil(id)));
    }

    public async Task<TwoFactorStatus> GetStatusAsync(int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, $"SELECT {StatusColumns} FROM web_user_totp t WHERE t.user_id=@id", ("@id", userId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadStatus(reader).Status : new(false, false, LockedUntil: Throttle.LockedUntil(userId));
    }

    public async Task<IReadOnlyDictionary<int, TwoFactorStatus>> GetStatusesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, $"SELECT {StatusColumns} FROM web_user_totp t");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new Dictionary<int, TwoFactorStatus>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) { var (id, status) = ReadStatus(reader); result[id] = status; }
        return result;
    }

    private async Task<(byte[]? Secret, long LastStep, bool Confirmed, bool Suspended)> ReadSecretAsync(MySqlConnection connection, int userId, CancellationToken cancellationToken)
    {
        await using var command = Command(connection, "SELECT secret_protected,last_step,confirmed_utc,suspended_utc FROM web_user_totp WHERE user_id=@id", ("@id", userId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return (null, 0, false, false);
        return (Totp.FromBase32(protector.Unprotect(reader.GetString(0))), reader.GetInt64(1), !reader.IsDBNull(2), !reader.IsDBNull(3));
    }

    public async Task<string?> GetPendingSecretAsync(int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        var (secret, _, confirmed, _) = await ReadSecretAsync(connection, userId, cancellationToken).ConfigureAwait(false);
        return secret is null || confirmed ? null : Totp.ToBase32(secret);
    }

    public async Task<string?> ProvisionAsync(int userId, string by, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        var (existing, _, confirmed, _) = await ReadSecretAsync(connection, userId, cancellationToken).ConfigureAwait(false);
        if (confirmed) return null;
        var secret = Totp.NewSecret();
        await using var command = Command(connection, """
            INSERT INTO web_user_totp(user_id,secret_protected,created_utc,confirmed_utc,last_step,suspended_utc,last_used_utc,last_used_ip,enrolled_by,confirmed_by)
            VALUES(@id,@secret,@now,NULL,0,NULL,NULL,NULL,@by,NULL)
            ON DUPLICATE KEY UPDATE secret_protected=@secret,created_utc=@now,confirmed_utc=NULL,last_step=0,suspended_utc=NULL,last_used_utc=NULL,last_used_ip=NULL,enrolled_by=@by,confirmed_by=NULL
            """, ("@id", userId), ("@secret", protector.Protect(Totp.ToBase32(secret))), ("@now", DateTime.UtcNow.ToString("O")), ("@by", by));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        Throttle.Clear(userId);
        return Totp.ToBase32(secret);
    }

    public async Task<IReadOnlyList<string>?> ConfirmAsync(int userId, string code, string by, CancellationToken cancellationToken = default)
    {
        if (Throttle.LockedUntil(userId) is not null) return null;
        await using var connection = await OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        var (secret, lastStep, confirmed, _) = await ReadSecretAsync(connection, userId, cancellationToken).ConfigureAwait(false);
        if (secret is null || confirmed) return null;
        if (!Totp.Verify(secret, code, DateTimeOffset.UtcNow, lastStep, out var step)) { Throttle.Fail(userId); return null; }
        Throttle.Clear(userId);
        var codes = Totp.NewRecoveryCodes();
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (var update = Command(connection, transaction, "UPDATE web_user_totp SET confirmed_utc=@now,last_step=@step,confirmed_by=@by WHERE user_id=@id", ("@now", DateTime.UtcNow.ToString("O")), ("@step", step), ("@by", by), ("@id", userId)))
            await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await InsertRecoveryCodesAsync(connection, transaction, userId, codes, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return codes;
    }

    private static async Task InsertRecoveryCodesAsync(MySqlConnection connection, MySqlTransaction transaction, int userId, IReadOnlyList<string> codes, CancellationToken cancellationToken)
    {
        await using (var clear = Command(connection, transaction, "DELETE FROM web_user_recovery_codes WHERE user_id=@id", ("@id", userId))) await clear.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        foreach (var recovery in codes)
        {
            await using var insert = Command(connection, transaction, "INSERT INTO web_user_recovery_codes(user_id,code_hash) VALUES(@id,@hash)", ("@id", userId), ("@hash", Totp.HashRecoveryCode(recovery)));
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<TwoFactorCheck> VerifyAsync(int userId, string code, string? clientAddress = null, CancellationToken cancellationToken = default)
    {
        if (Throttle.LockedUntil(userId) is not null) return TwoFactorCheck.LockedOut;
        await using var connection = await OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        var (secret, lastStep, confirmed, suspended) = await ReadSecretAsync(connection, userId, cancellationToken).ConfigureAwait(false);
        if (secret is null || !confirmed) return TwoFactorCheck.NotEnrolled;
        if (suspended) return TwoFactorCheck.Suspended;
        var result = TwoFactorCheck.Wrong;
        if (Totp.Verify(secret, code, DateTimeOffset.UtcNow, lastStep, out var step))
        {
            // The step is only moved forward: two sign-ins with the same code cannot both pass.
            await using var move = Command(connection, "UPDATE web_user_totp SET last_step=@step WHERE user_id=@id AND last_step<@step", ("@step", step), ("@id", userId));
            if (await move.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1) result = TwoFactorCheck.Valid;
        }
        else if (Totp.NormalizeRecoveryCode(code).Length == 10)
        {
            await using var spend = Command(connection, "UPDATE web_user_recovery_codes SET used_utc=@now WHERE user_id=@id AND code_hash=@hash AND used_utc IS NULL",
                ("@now", DateTime.UtcNow.ToString("O")), ("@id", userId), ("@hash", Totp.HashRecoveryCode(code)));
            if (await spend.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1) result = TwoFactorCheck.ValidRecoveryCode;
        }
        if (result == TwoFactorCheck.Wrong) { Throttle.Fail(userId); return result; }
        Throttle.Clear(userId);
        await using var used = Command(connection, "UPDATE web_user_totp SET last_used_utc=@now,last_used_ip=@ip WHERE user_id=@id",
            ("@now", DateTime.UtcNow.ToString("O")), ("@ip", clientAddress is { Length: > 64 } ? clientAddress[..64] : clientAddress), ("@id", userId));
        await used.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<bool> RevokeAsync(int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        int removed;
        await using (var delete = Command(connection, transaction, "DELETE FROM web_user_totp WHERE user_id=@id", ("@id", userId))) removed = await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await using (var codes = Command(connection, transaction, "DELETE FROM web_user_recovery_codes WHERE user_id=@id", ("@id", userId))) await codes.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        Throttle.Clear(userId);
        return removed > 0;
    }

    public async Task<bool> SetSuspendedAsync(int userId, bool suspended, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, suspended
            ? "UPDATE web_user_totp SET suspended_utc=@now WHERE user_id=@id AND suspended_utc IS NULL"
            : "UPDATE web_user_totp SET suspended_utc=NULL WHERE user_id=@id AND suspended_utc IS NOT NULL", ("@now", DateTime.UtcNow.ToString("O")), ("@id", userId));
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
    }

    public async Task<string?> BeginReplacementAsync(int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        var (secret, _, confirmed, _) = await ReadSecretAsync(connection, userId, cancellationToken).ConfigureAwait(false);
        if (secret is null || !confirmed) return null;
        var fresh = Totp.ToBase32(Totp.NewSecret());
        await using var command = Command(connection, "UPDATE web_user_totp SET pending_secret_protected=@secret WHERE user_id=@id", ("@secret", protector.Protect(fresh)), ("@id", userId));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return fresh;
    }

    public async Task<IReadOnlyList<string>?> ConfirmReplacementAsync(int userId, string code, string by, CancellationToken cancellationToken = default)
    {
        if (Throttle.LockedUntil(userId) is not null) return null;
        await using var connection = await OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        string? waiting;
        await using (var read = Command(connection, "SELECT pending_secret_protected FROM web_user_totp WHERE user_id=@id AND confirmed_utc IS NOT NULL", ("@id", userId)))
            waiting = await read.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        var secret = waiting is null ? null : Totp.FromBase32(protector.Unprotect(waiting));
        if (secret is null) return null;
        if (!Totp.Verify(secret, code, DateTimeOffset.UtcNow, 0, out var step)) { Throttle.Fail(userId); return null; }
        Throttle.Clear(userId);
        var codes = Totp.NewRecoveryCodes();
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (var swap = Command(connection, transaction, """
            UPDATE web_user_totp SET secret_protected=pending_secret_protected,pending_secret_protected=NULL,last_step=@step,confirmed_by=@by,confirmed_utc=@now,created_utc=@now
            WHERE user_id=@id AND pending_secret_protected=@waiting
            """, ("@step", step), ("@by", by), ("@now", DateTime.UtcNow.ToString("O")), ("@id", userId), ("@waiting", waiting)))
        {
            if (await swap.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1) return null;
        }
        await InsertRecoveryCodesAsync(connection, transaction, userId, codes, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return codes;
    }

    public async Task CancelReplacementAsync(int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, "UPDATE web_user_totp SET pending_secret_protected=NULL WHERE user_id=@id", ("@id", userId));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>?> RegenerateRecoveryCodesAsync(int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        var (secret, _, confirmed, _) = await ReadSecretAsync(connection, userId, cancellationToken).ConfigureAwait(false);
        if (secret is null || !confirmed) return null;
        var codes = Totp.NewRecoveryCodes();
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await InsertRecoveryCodesAsync(connection, transaction, userId, codes, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return codes;
    }
}
