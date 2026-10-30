using System.Text;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using BlazorStoc.Services;

namespace BlazorStoc.Checks;

public static partial class MariaExtendedChecks
{
    private static async Task BeneficiaryFilesAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe)
    {
        var suffix = Suffix();
        var beneficiaries = new MariaBeneficiaryRepository(configuration, admin, audit);
        var workPoints = new MariaWorkPointRepository(configuration, admin, audit);
        var files = new MariaEntityFileStore(configuration, admin, audit);
        var since = DateTime.UtcNow;
        await Task.Delay(30);
        var owner = await beneficiaries.CreateAsync(Legal($"Ext Fisiere benef {suffix} SRL", "RO" + Random.Shared.Next(70000000, 79999999)));
        byte[] Text(string value) => Encoding.UTF8.GetBytes(value);
        async Task<int> Events(string action) => (await audit.GetEventsAsync()).Count(item => item.TimestampUtc > since && item.EntityType == AuditEntities.Beneficiary &&
            item.EntityId == owner.Id.ToString() && item.Action == action);
        try
        {
            var point = (await workPoints.GetAsync(owner.Id)).Single();
            var free = await files.AddAsync(EntityFileOwners.Beneficiary, owner.Id, "oferta semnata.txt", "text/plain", Text("liber"));
            var attached = await files.AddAsync(EntityFileOwners.WorkPoint, point.Id, "plan punct.txt", "text/plain", Text("punct"));
            Check(free.OwnerKind == EntityFileOwners.Beneficiary && await Events(AuditActions.AddAttachedFile) == 2, "A free file is added to the beneficiary and journaled like the others");
            var all = await files.GetForBeneficiaryAsync(owner.Id);
            Check(all.Count == 2 && all.Single(item => item.IsFree).File.Id == free.Id && all.Single(item => !item.IsFree).OwnerName == point.Name &&
                  all.Single(item => !item.IsFree).SectionTitle == "Puncte de lucru", "The table lists the free file and the file of the work point with its section");
            await Rejects<WorkPointOperationException>(() => files.AddAsync(EntityFileOwners.Beneficiary, owner.Id, "oferta semnata.txt", "text/plain", Text("liber")), "The same free file cannot be added twice");
            await Rejects<WorkPointOperationException>(() => files.RenameAsync(free.Id, "oferta semnata.pdf"), "The extension of a file cannot be changed by renaming");
            var renamed = await files.RenameAsync(free.Id, "oferta finala.txt");
            Check(renamed.OriginalName == "oferta finala.txt" && await Events(AuditActions.RenameAttachedFile) == 1, "A free file is renamed (name only) and the rename is journaled");
            Check(FileNameParts.Split("raport final.v2.pdf") == ("raport final.v2", ".pdf") && FileNameParts.Split("fara extensie") == ("fara extensie", ""), "The name is split before the extension only");
            await files.DeleteAsync(free.Id);
            Check((await files.GetForBeneficiaryAsync(owner.Id)).Count == 1 && await Events(AuditActions.DeleteAttachedFile) == 1, "A free file is deleted and the deletion is journaled");
        }
        finally
        {
            foreach (var item in await files.GetForBeneficiaryAsync(owner.Id)) await files.DeleteAsync(item.File.Id);
            if ((await beneficiaries.GetBeneficiariesAsync()).FirstOrDefault(current => current.Id == owner.Id) is { } live) await beneficiaries.DeleteAsync(live, "Ext curatare");
        }
    }
}
