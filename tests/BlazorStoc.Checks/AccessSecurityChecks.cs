using System.Net;
using System.Text;
using BlazorStoc.Services;
using Microsoft.Extensions.Configuration;

namespace BlazorStoc.Checks;

// Setari -> Autentificare: the codes of the authenticator application (RFC 6238 test values), the recovery codes, the internal network and the settings file.
public static class AccessSecurityChecks
{
    public static void Run(Action<bool, string> check)
    {
        Console.WriteLine("=== Autentificare in doi pasi ===");
        var rfcSecret = Encoding.ASCII.GetBytes("12345678901234567890");
        check(Totp.Compute(rfcSecret, 59 / 30) == "287082" && Totp.Compute(rfcSecret, 1111111109 / 30) == "081804" && Totp.Compute(rfcSecret, 2000000000 / 30) == "279037",
            "2FA: the codes match the RFC 6238 test values");
        var secret = Totp.NewSecret();
        check(Totp.FromBase32(Totp.ToBase32(secret))!.SequenceEqual(secret) && Totp.ToBase32(rfcSecret) == "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", "2FA: base 32 round trip and the known secret text");
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        var step = Totp.StepOf(now);
        check(Totp.Verify(secret, Totp.Compute(secret, step), now, 0, out var matched) && matched == step, "2FA: the code of the current step is accepted");
        check(Totp.Verify(secret, Totp.Compute(secret, step - 1), now, 0, out _) && Totp.Verify(secret, Totp.Compute(secret, step + 1), now, 0, out _), "2FA: one step of clock drift is accepted");
        check(!Totp.Verify(secret, Totp.Compute(secret, step - 3), now, 0, out _) && !Totp.Verify(secret, "12", now, 0, out _) && !Totp.Verify(secret, "", now, 0, out _), "2FA: an old, short or empty code is refused");
        check(!Totp.Verify(secret, Totp.Compute(secret, step), now, step, out _), "2FA: a code already used (step not after the last one) is refused");
        var codes = Totp.NewRecoveryCodes();
        check(codes.Count == 10 && codes.Distinct().Count() == 10 && codes.All(code => code.Length == 11 && code[5] == '-'), "2FA: ten different recovery codes of the form XXXXX-XXXXX");
        check(Totp.HashRecoveryCode(codes[0]) == Totp.HashRecoveryCode(codes[0].ToLowerInvariant().Replace("-", " ")) && Totp.HashRecoveryCode(codes[0]) != Totp.HashRecoveryCode(codes[1]),
            "2FA: a recovery code is matched regardless of case and separator; different codes have different hashes");
        check(Totp.ProvisioningUri("BlazorStoc", "ana popescu", "ABC").StartsWith("otpauth://totp/BlazorStoc:ana%20popescu?secret=ABC&issuer=BlazorStoc"), "2FA: the QR link carries the issuer, the account and the secret");

        var defaults = AccessSecurityOptions.Off;
        bool Internal(string address, AccessSecurityOptions? options = null) => AccessSecurityRules.IsInternal(IPAddress.Parse(address), options ?? defaults);
        check(Internal("192.168.1.20") && Internal("10.4.5.6") && Internal("172.20.0.9") && Internal("127.0.0.1") && Internal("::1") && Internal("::ffff:192.168.0.8"),
            "Internal network: private ranges, the computer itself and mapped IPv4 are internal by default");
        check(!Internal("86.120.4.4") && !Internal("172.32.0.1") && !Internal("8.8.8.8") && !Internal("2a02:2f0e::1"), "Internal network: public addresses are external by default");
        check(!AccessSecurityRules.IsInternal(null, defaults), "Internal network: an unknown address is external");
        var custom = new AccessSecurityOptions(TwoFactorMode.External, ["203.0.113.0/24", "198.51.100.7"]);
        check(Internal("203.0.113.200", custom) && Internal("198.51.100.7", custom) && !Internal("198.51.100.8", custom) && !Internal("192.168.1.5", custom) && Internal("127.0.0.1", custom),
            "Internal network: a written list replaces the defaults (loopback stays internal)");

        var directory = Path.Combine(Path.GetTempPath(), "bs-access-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, AccessSecurityRules.FileName);
            AccessSecurityRules.Write(path, new(TwoFactorMode.External, ["10.1.0.0/16", "192.168.5.0/24"]));
            var configuration = new ConfigurationBuilder().AddJsonFile(path).Build();
            var read = AccessSecurityRules.Read(configuration);
            check(read.Mode == TwoFactorMode.External && read.InternalNetworks.SequenceEqual(["10.1.0.0/16", "192.168.5.0/24"]), "Access settings: the file is read back as written");
            check(AccessSecurityRules.IsCodeRequired(configuration, IPAddress.Parse("86.120.4.4")) && !AccessSecurityRules.IsCodeRequired(configuration, IPAddress.Parse("10.1.2.3")) && !AccessSecurityRules.IsCodeRequired(configuration, IPAddress.Parse("192.168.5.9")),
                "Access settings: outside the listed network the second step is needed, inside it is not");
            check(AccessSecurityRules.IsCodeRequired(configuration, IPAddress.Parse("192.168.1.9")), "Access settings: a private address outside the written network is external");
            var off = new ConfigurationBuilder().Build();
            check(!AccessSecurityRules.IsCodeRequired(off, IPAddress.Parse("86.120.4.4")), "Access settings: switched off (nothing saved) nobody is asked for a code");
        }
        finally { Directory.Delete(directory, true); }
        check(AccessSecurityRules.Validate(new(TwoFactorMode.External, ["10.0.0.0/33"])) is not null && AccessSecurityRules.Validate(new(TwoFactorMode.External, ["abc"])) is not null && AccessSecurityRules.Validate(new(TwoFactorMode.External, ["10.0.0.0/8"])) is null,
            "Access settings: a wrong network or address is refused");
        check(new[] { AuditActions.SetAccessSecurity, AuditActions.ProvisionTwoFactor, AuditActions.ConfirmTwoFactor, AuditActions.RevokeTwoFactor, AuditActions.SuspendTwoFactor, AuditActions.ResumeTwoFactor, AuditActions.DeleteTwoFactor, AuditActions.RegenerateRecoveryCodes, AuditActions.UnlockTwoFactor }.Distinct().Count() == 9,
            "2FA: the journal names nine different operations (settings, associate, confirm, revoke, suspend, resume, delete, recovery codes, unlock)");

        // The three modes, the first version of the file, and the server-only rule for the administrators.
        var directory2 = Path.Combine(Path.GetTempPath(), "bs-access2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory2);
        try
        {
            ConfigurationRoot Make(string json) { var path = Path.Combine(directory2, Guid.NewGuid().ToString("N") + ".json"); File.WriteAllText(path, json); return (ConfigurationRoot)new ConfigurationBuilder().AddJsonFile(path).Build(); }
            var everyone = Make("{\"Security\":{\"Access\":{\"Mode\":\"Everyone\"}}}");
            check(AccessSecurityRules.IsCodeRequired(everyone, IPAddress.Parse("192.168.1.5")) && AccessSecurityRules.IsCodeRequired(everyone, IPAddress.Parse("127.0.0.1")) && AccessSecurityRules.IsCodeRequired(everyone, IPAddress.Parse("86.120.4.4")),
                "Access settings: the mode 'for everybody' asks the code from every address, even on the server");
            var legacy = Make("{\"Security\":{\"Access\":{\"ExternalTwoFactor\":true}}}");
            check(AccessSecurityRules.Read(legacy).Mode == TwoFactorMode.External, "Access settings: the first version of the file (a flag for the outside) still reads as 'only outside'");
        }
        finally { Directory.Delete(directory2, true); }
        var plain = new Microsoft.AspNetCore.Http.HeaderDictionary();
        var proxied = new Microsoft.AspNetCore.Http.HeaderDictionary { ["X-Forwarded-For"] = "86.120.4.4" };
        check(AccessSecurityRules.IsServerLocal(IPAddress.Loopback, plain) && AccessSecurityRules.IsServerLocal(IPAddress.IPv6Loopback, plain) && AccessSecurityRules.IsServerLocal(IPAddress.Parse("::ffff:127.0.0.1"), plain),
            "Server only: a loopback request without proxy headers is the server itself");
        check(!AccessSecurityRules.IsServerLocal(IPAddress.Parse("192.168.1.5"), plain) && !AccessSecurityRules.IsServerLocal(null, plain) && !AccessSecurityRules.IsServerLocal(IPAddress.Loopback, proxied),
            "Server only: a LAN address, an unknown address or a loopback request carrying proxy headers is not the server");

        // The authenticator of an administrator's account changes only on the server; the accounts of ordinary users from anywhere.
        AuthenticatorRow Row(string role) => new(5, "x", "X", role, true, new(false, false));
        bool May(string role, string address, bool proxied)
        {
            var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
            context.Connection.RemoteIpAddress = IPAddress.Parse(address);
            if (proxied) context.Request.Headers["X-Forwarded-For"] = "86.120.4.4";
            var service = new AccessSecuritySettingsService(new ConfigurationBuilder().Build(), null!, null!, null!, null!,
                new Microsoft.AspNetCore.Http.HttpContextAccessor { HttpContext = context }, Microsoft.Extensions.Logging.Abstractions.NullLogger<AccessSecuritySettingsService>.Instance);
            return service.MayManage(Row(role));
        }
        check(May(AccessRoles.Administrator, "127.0.0.1", false) && !May(AccessRoles.Administrator, "192.168.1.9", false) && !May(AccessRoles.Administrator, "127.0.0.1", true),
            "Server only: an administrator's authenticator changes only from the server (not from the LAN, not through a proxy on the server)");
        check(May(AccessRoles.LimitedUser, "192.168.1.9", false) && May(AccessRoles.LimitedUser, "86.120.4.4", false),
            "Server only: the authenticator of an ordinary user can be changed from any address");
        var noContext = new AccessSecuritySettingsService(new ConfigurationBuilder().Build(), null!, null!, null!, null!, new Microsoft.AspNetCore.Http.HttpContextAccessor(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AccessSecuritySettingsService>.Instance);
        check(!noContext.MayManage(Row(AccessRoles.Administrator)) && noContext.MayManage(Row(AccessRoles.LimitedUser)), "Server only: without a known request an administrator's authenticator is not changed");

        // The menu of the signed-in user: the administrator of the configuration cannot change a password written in the settings file from there.
        var configured = new OwnAccountService(new ConfigurationBuilder().Build(), new TestAccessControl(true, "admin"), null!, Microsoft.Extensions.Logging.Abstractions.NullLogger<OwnAccountService>.Instance);
        check(!configured.CanChangePasswordAsync().GetAwaiter().GetResult() && !configured.ChangePasswordAsync("x", "parola-noua-12345", "parola-noua-12345").GetAwaiter().GetResult().Success,
            "Own password: the administrator of the configuration is told to use the settings file, nothing is changed");

        // The conditions of a password are data: five lines, the same keys the page of the first sign-in checks in the browser, all met by a good password and each missing one named.
        check(PasswordPolicy.Rules.Select(rule => rule.Key).SequenceEqual(["length", "lower", "upper", "digit", "special"]) && PasswordPolicy.Rules.All(rule => rule.Test("Ab1!cdef"))
              && PasswordPolicy.Rules.Count(rule => !rule.Test("abcdefgh")) == 3 && PasswordPolicy.Validate("abcdefgh") is { } missingThree && missingThree.Contains("o literă mare") && missingThree.Contains("o cifră") && missingThree.Contains("un caracter special"),
            "Password rules: five conditions as data, each missing one is named");

        var throttle = new FailureThrottle<string>();
        for (var i = 0; i < 4; i++) throttle.Fail("ana");
        check(throttle.LockedUntil("ana") is null, "Lock: four wrong attempts do not lock yet");
        throttle.Fail("ana");
        var first = throttle.LockedUntil("ana");
        check(first is not null && first.Value - DateTimeOffset.UtcNow > TimeSpan.FromMinutes(9) && throttle.LockedUntil("ion") is null, "Lock: the fifth wrong attempt locks that name for about 10 minutes, nobody else");
        throttle.Clear("ana");
        check(throttle.LockedUntil("ana") is null, "Lock: a right sign-in clears the lock");
    }
}
