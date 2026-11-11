using Microsoft.Extensions.Configuration;
using MySqlConnector;
using BlazorStoc.Services;

namespace BlazorStoc.Checks;

public static partial class MariaExtendedChecks
{
    // ---- Attached work points (components of a work point) -------------------------------------------------------------------------
    private static async Task AttachedWorkPointsAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe)
    {
        var suffix = Suffix();
        var beneficiaries = new MariaBeneficiaryRepository(configuration, admin, audit);
        var workPoints = new MariaWorkPointRepository(configuration, admin, audit);
        var attached = new MariaAttachedWorkPointRepository(configuration, admin, audit);
        async Task<long> Count(string sql, params (string, object)[] parameters) => await ScalarLongAsync(probe, sql, parameters);
        var since = DateTime.UtcNow;
        await Task.Delay(30);
        var owner = await beneficiaries.CreateAsync(Legal($"Ext Arondate {suffix} SRL", "RO" + Random.Shared.Next(50000000, 59999999)));
        async Task<int> Events(string action, string entity = AuditEntities.Beneficiary) => (await audit.GetEventsAsync()).Count(item => item.TimestampUtc > since && item.EntityType == entity && item.EntityId == owner.Id.ToString() && item.Action == action);
        var ownerDeleted = false;
        try
        {
            var school = await workPoints.CreateAsync(owner.Id, new WorkPointInput { Name = "Scoala Lunca", Address = "Strada Scolii 1, Lunca" });
            var depot = await workPoints.CreateAsync(owner.Id, new WorkPointInput { Name = "Depozit", Address = "Strada Depozitului 3, Lunca" });
            await Rejects<WorkPointOperationException>(() => attached.CreateAsync(school, new AttachedWorkPointInput { Name = "Fara locatie" }), "An attached work point without address and coordinates is refused");
            await Rejects<WorkPointOperationException>(() => attached.CreateAsync(school, new AttachedWorkPointInput { Address = "Str. A 1" }), "An attached work point without a name is refused");

            var kindergarten = await attached.CreateAsync(school, new AttachedWorkPointInput { Name = "Gradinita", Address = "Strada Florilor 5, Lunca", Phone = "0744 123 456", ContactPerson = "Ana Pop" });
            var building = await attached.CreateAsync(school, new AttachedWorkPointInput { Name = "Cladire invatamant primar", UseCoordinates = true, CoordinatesText = "45.7489, 21.2087" });
            Check(kindergarten.Id > 0 && kindergarten.Address == "Strada Florilor 5, Lunca" && kindergarten.Phone == "0744123456" && !kindergarten.HasCoordinates
                  && building.Address.Length == 0 && building.HasCoordinates && building.Latitude == 45.7489m && building.WorkPointId == school.Id,
                "Attached work points are saved with what they have: an address (no coordinates) or only coordinates (the address stays empty, it is not taken from the parent)");
            await Rejects<WorkPointOperationException>(() => attached.CreateAsync(school, new AttachedWorkPointInput { Name = " gradinita ", Address = "Alta 1" }), "Two attached work points of the same work point cannot have the same name");
            var sameNameElsewhere = await attached.CreateAsync(depot, new AttachedWorkPointInput { Name = "Gradinita", Address = "Strada Depozitului 3, corp B" });
            Check(sameNameElsewhere.WorkPointId == depot.Id, "The same name can be used under another work point");
            await Rejects<MySqlException>(() => ExecuteAsync(probe, "INSERT INTO work_point_attached (work_point_id, name, name_key, address) VALUES (@w, 'Fara', 'FARA', '')", ("@w", school.Id)), "The database itself refuses an attached work point with neither address nor coordinates");
            await Rejects<MySqlException>(() => ExecuteAsync(probe, "UPDATE work_point_attached SET latitude=1 WHERE id=@id", ("@id", kindergarten.Id)), "The database refuses coordinates with only one of the two values");

            var phoneEdit = AttachedWorkPointInput.From(kindergarten); phoneEdit.Phone = "0722333444";
            var edited = await attached.UpdateAsync(kindergarten, phoneEdit);
            Check(edited.Phone == "0722333444" && edited.Version == kindergarten.Version + 1, "Editing an attached work point changes it and its version");
            await Rejects<WorkPointOperationException>(() => attached.UpdateAsync(kindergarten, phoneEdit), "Editing from a stale copy is refused");
            var stripped = AttachedWorkPointInput.From(edited); stripped.Address = ""; stripped.UseCoordinates = false; stripped.CoordinatesText = "";
            await Rejects<WorkPointOperationException>(() => attached.UpdateAsync(edited, stripped), "Deleting the last address or coordinates of an attached work point is refused");
            var coordinatesAdded = AttachedWorkPointInput.From(edited); coordinatesAdded.UseCoordinates = true; coordinatesAdded.CoordinatesText = "45.75, 21.21"; coordinatesAdded.Address = "";
            var swapped = await attached.UpdateAsync(edited, coordinatesAdded);
            Check(swapped.Address.Length == 0 && swapped.HasCoordinates, "The address can be deleted once the coordinates are there");
            await Rejects<WorkPointOperationException>(() => attached.UpdateAsync(swapped, new AttachedWorkPointInput { Name = "CLADIRE INVATAMANT PRIMAR", UseCoordinates = true, CoordinatesText = "45.75, 21.21" }), "An edit cannot take the name of another attached work point of the same work point");

            var ofSchool = await attached.GetAsync(school.Id);
            var ofBeneficiary = await attached.GetForBeneficiaryAsync(owner.Id);
            Check(ofSchool.Count == 2 && ofSchool[0].Name == "Cladire invatamant primar" && ofBeneficiary.Count == 3, "Attached work points are listed per work point and per beneficiary, by name");
            var places = (await attached.GetAllPlacesAsync()).Where(place => place.BeneficiaryId == owner.Id).ToList();
            Check(places.Count == 3 && places.Any(place => place.Title == $"{owner.Name} · Scoala Lunca · Gradinita"), "The places for the map and the search carry the names of their parents");
            var entries = (await attached.GetSearchEntriesAsync()).Where(entry => entry.BeneficiaryId == owner.Id).ToList();
            Check(entries.Count(entry => entry.Attached) == 3 && entries.Any(entry => !entry.Attached && entry.Name == "Scoala Lunca") && entries.All(entry => entry.Name != WorkPointRules.PrimaryName)
                  && BeneficiarySearch.Match(await beneficiaries.GetBeneficiariesAsync(), "gradinita", BeneficiarySearchMode.Extended, entries).Any(match => match.Beneficiary.Id == owner.Id && match.Reasons.Any(reason => reason.StartsWith("punct arondat: Gradinita", StringComparison.Ordinal))),
                "The search data holds the work points (not the unnamed main one) and the attached ones, and a search by 'gradinita' finds the beneficiary through them");
            Check(await Events(AuditActions.CreateAttachedWorkPoint) == 3 && await Events(AuditActions.EditAttachedWorkPoint) == 2, "Adding and editing attached work points are journaled under the beneficiary with their own action names");

            await attached.DeleteAsync(sameNameElsewhere);
            Check(await Count("SELECT COUNT(*) FROM archive_work_point_attached WHERE original_id=@id", ("@id", sameNameElsewhere.Id)) == 1 && await Count("SELECT COUNT(*) FROM work_point_attached WHERE id=@id", ("@id", sameNameElsewhere.Id)) == 0
                  && (await audit.GetEventsAsync()).Count(item => item.TimestampUtc > since && item.EntityType == AuditEntities.AttachedWorkPoint && item.Action == AuditActions.Delete && item.EntityId == sameNameElsewhere.Id.ToString()) == 1, "Deleting an attached work point archives it and journals the deletion");
            await Rejects<WorkPointOperationException>(() => attached.DeleteAsync(sameNameElsewhere), "An attached work point deleted already cannot be deleted again");

            // A work point with attached work points can be deleted: they are archived with it (as relations) and removed before it.
            await workPoints.DeleteAsync(school);
            Check(await Count("SELECT COUNT(*) FROM work_point_attached WHERE work_point_id=@w", ("@w", school.Id)) == 0 && await Count("SELECT COUNT(*) FROM archive_work_points WHERE original_id=@id", ("@id", school.Id)) == 1
                  && await Count("SELECT COUNT(*) FROM archive_relations WHERE relation_type=@type AND original_relation_id IN (@a, @b)", ("@type", AttachedWorkPointRules.RelationType), ("@a", swapped.Id.ToString()), ("@b", building.Id.ToString())) == 2,
                "Deleting a work point archives its attached work points with it and leaves none behind");

            // And so can a beneficiary.
            var kept = await attached.CreateAsync(depot, new AttachedWorkPointInput { Name = "Corp C", Address = "Strada Depozitului 3, corp C" });
            var liveOwner = (await beneficiaries.GetBeneficiariesAsync()).Single(item => item.Id == owner.Id);
            await beneficiaries.DeleteAsync(liveOwner, "Ext curatare arondate");
            ownerDeleted = true;
            Check(await Count("SELECT COUNT(*) FROM work_point_attached WHERE id=@id", ("@id", kept.Id)) == 0 && await Count("SELECT COUNT(*) FROM archive_relations WHERE relation_type=@type AND original_relation_id=@id", ("@type", AttachedWorkPointRules.RelationType), ("@id", kept.Id.ToString())) == 1,
                "Deleting a beneficiary archives the attached work points of its work points with it");
        }
        finally
        {
            if (!ownerDeleted && (await beneficiaries.GetBeneficiariesAsync()).FirstOrDefault(current => current.Id == owner.Id) is { } live)
            {
                foreach (var item in await attached.GetForBeneficiaryAsync(owner.Id)) await attached.DeleteAsync(item);
                foreach (var point in (await workPoints.GetAsync(owner.Id)).Where(point => !point.IsPrimary)) await workPoints.DeleteAsync(point);
                await beneficiaries.DeleteAsync(live, "Ext curatare");
            }
        }
    }
}
