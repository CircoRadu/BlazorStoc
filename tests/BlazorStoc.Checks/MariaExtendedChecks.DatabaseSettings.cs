using Microsoft.Extensions.Configuration;
using MySqlConnector;
using BlazorStoc.Services;

namespace BlazorStoc.Checks;

public static partial class MariaExtendedChecks
{
    // ---- Setari -> Baza de date -------------------------------------------------------------------------------------------------

    private static async Task DatabaseSettingsAsync(IConfiguration configuration, IAuditTrail audit, MySqlConnection probe)
    {
        var started = DateTime.UtcNow.AddSeconds(-1);
        var admin = new TestAccessControl(true, "integration.tester");
        var status = new DatabaseStatusService(configuration, admin, null, AppHostingInfo.None, new CircuitCounter());
        var read = await status.ReadDatabaseAsync();
        Check(read.Reachable && read.Version.Length > 0 && read.TableCount > 0 && read.RunsOnThisMachine && read.LatencyMs >= 0 && read.CurrentUser.Length > 0,
            "Database status: the real server answers with version, tables and the account in use");
        Check(!read.Version.Contains(configuration["Database:Password"] ?? "") && (await Rejects<AccessDeniedException>(() =>
            new DatabaseStatusService(configuration, new TestAccessControl(false, "limited"), null, AppHostingInfo.None, new CircuitCounter()).ReadDatabaseAsync(), "A limited user cannot read the status")) is not null,
            "Database status: only administrators read it");

        var protection = Microsoft.AspNetCore.DataProtection.DataProtectionProvider.Create("BlazorStoc.Checks");
        var service = new DatabaseSettingsService(configuration, protection, admin, audit, new DatabaseSettingsChecks.NoLocks(), AppHostingInfo.None,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DatabaseSettingsService>.Instance);
        var host = configuration["Database:Host"] ?? "127.0.0.1";
        var port = configuration.GetValue("Database:Port", 3307);
        var user = configuration["Database:User"] ?? "";
        var ssl = configuration["Database:SslMode"] ?? "Required";
        var good = await service.TestAsync(new(host, port, user, null, ssl, null, false));
        Check(good.Ok, "Database settings: the test with the values in force succeeds on the real server");
        const string wrongPassword = "Wrong-Pass-For-Test-7";
        var bad = await service.TestAsync(new(host, port, user, wrongPassword, ssl, null, false));
        Check(!bad.Ok && bad.Steps.All(step => !step.Detail.Contains(wrongPassword)), "Database settings: a wrong password fails the test and is never shown");
        var events = (await audit.GetEventsAsync()).Where(item => item.TimestampUtc >= started && item.Action == AuditActions.TestDatabaseConnection).ToList();
        Check(events.Count >= 2 && events.All(item => !item.Details.Contains(wrongPassword) && !item.Details.Contains(configuration["Database:Password"] ?? "")),
            "Database settings: the tests are journaled without any password");
    }

}
