using BlazorStoc.Modules.Elcomtur;
using BlazorStoc.Services;
using Microsoft.Extensions.Configuration;
using MySqlConnector;

namespace BlazorStoc.Checks;

public static partial class MariaExtendedChecks
{
    private static async Task ElcomturAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe)
    {
        _ = probe;
        var since = DateTime.UtcNow;
        await Task.Delay(30);
        async Task<int> Events() => (await audit.GetEventsAsync()).Count(item => item.TimestampUtc > since && item.EntityType == AuditEntities.Elcomtur && item.Action == AuditActions.ImportElcomtur);
        var beneficiaries = new MariaBeneficiaryRepository(configuration, admin, audit);
        var number = Random.Shared.Next(70000000, 79999999);
        var owner = await beneficiaries.CreateAsync(Legal($"Ext Elcomtur {Suffix()} SRL", "RO" + number));
        var balances = new MariaElcomturBalanceRepository(configuration, admin);
        var service = new ElcomturImportService(beneficiaries, balances, admin, audit);
        var path = ElcomturChecks.CreateArchive(new DateOnly(2026, 10, 30), cui: number.ToString());
        string? id = null;
        try
        {
            await using (var stream = File.OpenRead(path)) id = await service.UploadAsync(stream, "backup.elc");
            Check((await service.GetFirmsAsync(id)).SequenceEqual(["firma1"]), "Elcomtur: an uploaded backup is checked and its firms are listed");
            var simulation = await service.RunAsync(id, "firma1", apply: false);
            Check(!simulation.Applied && simulation.Matched >= 1 && await balances.GetAsync(owner.Id) is null && await Events() == 0, "Elcomtur: the simulation matches the beneficiary by CUI and writes nothing (no balance, nothing in the journal)");
            var applied = await service.RunAsync(id, "firma1", apply: true);
            var saved = await balances.GetAsync(owner.Id);
            Check(applied.Applied && saved is not null && saved.Balance == 11950m && saved.Firm == "firma1" && saved.DataDate == new DateOnly(2026, 10, 30) && saved.Invoices.Select(item => item.Number).SequenceEqual(["0012263", "0012372"])
                && saved.Invoices[0].Rest == 1000m && saved.Invoices[1].Paid == 5435.05m,
                "Elcomtur: the application saves the balance of the beneficiary with its unpaid invoices (the one paid in part with its rest)");
            Check((await balances.GetOwedAsync()).Any(item => item.BeneficiaryId == owner.Id) && (await balances.GetInfoAsync()) is { Firm: "firma1" } info && info.DataDate == new DateOnly(2026, 10, 30) && info.Beneficiaries >= 1,
                "Elcomtur: the beneficiary with a positive balance is in the list of the owed ones, and the last import is told (date of the backup, firm)");
            Check(await balances.CountOwedAsync() == (await balances.GetOwedAsync()).Count && await balances.CountOwedAsync() >= 1, "Elcomtur: the number on the tab of the Notifications page is the number of beneficiaries with a positive balance");
            Check(await Events() == 1, "Elcomtur: the journal names the operation \"Preluare date Elcomtur\"");
            await service.DiscardAsync(id);
            await Rejects<ElcomturOperationException>(() => service.RunAsync(id, "firma1", apply: false), "Elcomtur: an uploaded file that was discarded is refused with a message");
            id = null;
            var notArchive = new MemoryStream("nu e o arhiva"u8.ToArray());
            await Rejects<ElcomturOperationException>(() => service.UploadAsync(notArchive, "x.elc"), "Elcomtur: a file that is not an Elcomtur archive is refused at the upload");
            await beneficiaries.DeleteAsync(owner, "Test Elcomtur");
            Check(await balances.GetAsync(owner.Id) is null, "Elcomtur: deleting a beneficiary removes its balance and invoices");
            owner = null!;
        }
        finally
        {
            File.Delete(path);
            if (id is not null) await service.DiscardAsync(id);
            if (owner is not null) { try { await beneficiaries.DeleteAsync(owner, "Curatare test Elcomtur"); } catch (Exception) { } }
        }
    }
}
