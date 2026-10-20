using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using BlazorStoc.Services;

namespace BlazorStoc.Checks;

public static partial class MariaExtendedChecks
{
    private static string MariaTimeTextForTest(DateTime utc) => utc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture);

    // ---- Work points ---------------------------------------------------------------------------------------------------------------
    private static async Task WorkPointsAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe, string assets)
    {
        var suffix = Suffix();
        var beneficiaries = new MariaBeneficiaryRepository(configuration, admin, audit);
        var workPoints = new MariaWorkPointRepository(configuration, admin, audit);
        var photos = new MariaServicePhotoStore(configuration, admin, audit);
        var liveRoot = Path.Combine(assets, "service-photos");
        var archiveRoot = Path.Combine(assets, "archive-files");
        byte[] Png(int size = 300) { var bytes = new byte[size]; Random.Shared.NextBytes(bytes); new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(bytes, 0); return bytes; }
        async Task<long> Count(string sql, params (string, object)[] parameters) => await ScalarLongAsync(probe, sql, parameters);
        var since = DateTime.UtcNow;
        await Task.Delay(30);
        var owner = await beneficiaries.CreateAsync(Legal($"Ext Puncte {suffix} SRL", "RO" + Random.Shared.Next(40000000, 49999999)));
        var extra = new List<Beneficiary>();
        var ownerDeleted = false;
        try
        {
            // The main work point is a real row, created with the beneficiary, one per beneficiary (also enforced by the database).
            var main = (await workPoints.GetAsync(owner.Id)).Single();
            Check(main.IsPrimary && main.Id > 0 && main.Name == WorkPointRules.PrimaryName && main.Address == owner.Address && main.Phone == owner.Phone,
                "A new beneficiary gets its main work point as a real row, with the address and phone of the beneficiary");
            await Rejects<MySqlException>(() => ExecuteAsync(probe, "INSERT INTO beneficiary_work_points (beneficiary_id, name, address, normalized_address, is_primary) VALUES (@b, 'x', 'Alta adresa', 'ALTA ADRESA', 1)", ("@b", owner.Id)),
                "The database refuses a second main work point for the same beneficiary");
            await Rejects<WorkPointOperationException>(() => workPoints.DeleteAsync(main), "The main work point cannot be deleted on its own");

            // It follows the beneficiary (address, phone) and only then changes its version.
            var edit = BeneficiaryInput.From(owner); edit.Address = "Bulevardul Nou 7, Cluj"; edit.Phone = "0722999888"; edit.Reason = "Ext punct principal";
            var editedOwner = await beneficiaries.UpdateAsync(owner, edit);
            main = (await workPoints.GetAsync(owner.Id)).Single();
            Check(main.Address == editedOwner.Address && main.Phone == editedOwner.Phone && main.Version == 1, "Editing the beneficiary moves its main work point along (address and phone)");
            var reasonOnly = BeneficiaryInput.From(editedOwner); reasonOnly.Reason = "Ext fara schimbari";
            editedOwner = await beneficiaries.UpdateAsync(editedOwner, reasonOnly);
            Check((await workPoints.GetAsync(owner.Id)).Single().Version == 1, "An edit that leaves the address and phone as they were does not touch the main work point");

            // An additional point: description, optional coordinates (both or neither), unique address.
            var created = await workPoints.CreateAsync(owner.Id, new WorkPointInput { Name = "Depozit", Address = "Str. Florilor nr. 5, Cluj", Phone = "0722333444",
                Description = "Depozit cu doua usi.\nAcces din curte.", UseCoordinates = true, CoordinatesText = "45,7489 21,2087" });
            Check(created.Description.Contains("Acces din curte") && created.Latitude == 45.7489m && created.Longitude == 21.2087m && !created.IsPrimary,
                "A work point keeps its description and the coordinates pasted with a decimal comma");
            var all = await workPoints.GetAsync(owner.Id);
            Check(all.Count == 2 && all[0].IsPrimary && all[1] == created, "The list has the main work point first and the stored point reads back identical");
            await Rejects<WorkPointOperationException>(() => workPoints.CreateAsync(owner.Id, new WorkPointInput { Name = "Alt depozit", Address = "strada FLORILOR 5, cluj" }), "A work point with the same normalized address is rejected");
            await Rejects<WorkPointOperationException>(() => workPoints.CreateAsync(owner.Id, new WorkPointInput { Name = "Ca sediul", Address = editedOwner.Address }), "A work point with the address of the main one is rejected");
            await Rejects<WorkPointOperationException>(() => workPoints.CreateAsync(owner.Id, new WorkPointInput { Name = "Coord gresite", Address = "Alta 1, Cluj", UseCoordinates = true, CoordinatesText = "abc" }), "Unreadable coordinates are rejected");
            await Rejects<WorkPointOperationException>(() => workPoints.CreateAsync(owner.Id, new WorkPointInput { Name = "Coord in afara", Address = "Alta 2, Cluj", UseCoordinates = true, CoordinatesText = "95, 10" }), "Coordinates out of range are rejected");
            var noCoordinates = await workPoints.CreateAsync(owner.Id, new WorkPointInput { Name = "Fara coordonate", Address = "Alta 3, Cluj", UseCoordinates = false, CoordinatesText = "12, 34" });
            Check(!noCoordinates.HasCoordinates, "With the switch off a point is stored without coordinates, whatever the field holds");
            await Rejects<MySqlException>(() => ExecuteAsync(probe, "UPDATE beneficiary_work_points SET latitude=1 WHERE id=@id", ("@id", noCoordinates.Id)), "The database refuses coordinates with only one of the two values");
            var clash = BeneficiaryInput.From(editedOwner); clash.Address = created.Address; clash.Reason = "Ext adresa ocupata";
            await Rejects<BeneficiaryOperationException>(() => beneficiaries.UpdateAsync(editedOwner, clash), "A beneficiary cannot take the address of one of its additional work points");
            await workPoints.DeleteAsync(noCoordinates);
            Check(await Count("SELECT COUNT(*) FROM archive_work_points WHERE original_id=@id", ("@id", noCoordinates.Id)) == 1 && await Count("SELECT COUNT(*) FROM beneficiary_work_points WHERE id=@id", ("@id", noCoordinates.Id)) == 0,
                "Deleting a work point archives it");

            // The journal names each kind of change exactly.
            var description = WorkPointInput.From(created); description.Description = "Descriere noua";
            var afterDescription = await workPoints.UpdateAsync(created, description);
            var coordinates = WorkPointInput.From(afterDescription); coordinates.CoordinatesText = "45.75, 21.21";
            var afterCoordinates = await workPoints.UpdateAsync(afterDescription, coordinates);
            var switchOff = WorkPointInput.From(afterCoordinates); switchOff.UseCoordinates = false;
            var afterOff = await workPoints.UpdateAsync(afterCoordinates, switchOff);
            Check(!afterOff.HasCoordinates && afterCoordinates.Latitude == 45.75m, "The coordinates can be changed and switched off");
            var mixed = WorkPointInput.From(afterOff); mixed.Name = "Depozit central"; mixed.Description = "Alt text";
            var point = await workPoints.UpdateAsync(afterOff, mixed);
            var events = (await audit.GetEventsAsync()).Where(item => item.TimestampUtc > since && item.EntityType == AuditEntities.Beneficiary && item.EntityId == owner.Id.ToString()).ToList();
            Check(events.Any(item => item.Action == AuditActions.CreateWorkPoint) && events.Count(item => item.Action == AuditActions.EditWorkPointDescription) == 1 &&
                  events.Count(item => item.Action == AuditActions.EditWorkPointCoordinates) == 2 && events.Any(item => item.Action == AuditActions.EditWorkPoint && item.Details.Contains("Depozit central")),
                "The journal names \"Adăugare punct de lucru\", \"Modificare descriere punct de lucru\", \"Modificare coordonate punct de lucru\" and \"Modificare punct de lucru\"");

            // The main point: name, description, coordinates and contact are edited; address and phone stay those of the beneficiary.
            var mainInput = WorkPointInput.From(main); mainInput.Address = "Ignorata 1"; mainInput.Phone = "0700000000"; mainInput.Name = "Sediu";
            mainInput.Description = "Sediul firmei"; mainInput.UseCoordinates = true; mainInput.CoordinatesText = "46.77, 23.59";
            var updatedMain = await workPoints.UpdateAsync(main, mainInput);
            Check(updatedMain.IsPrimary && updatedMain.Name == "Sediu" && updatedMain.Address == main.Address && updatedMain.Phone == main.Phone && updatedMain.HasCoordinates && updatedMain.Description == "Sediul firmei",
                "The main work point takes a name, a description and coordinates, but keeps the address and phone of the beneficiary");
            var again = BeneficiaryInput.From(editedOwner); again.Phone = "0722111000"; again.Reason = "Ext telefon nou";
            editedOwner = await beneficiaries.UpdateAsync(editedOwner, again);
            var followed = (await workPoints.GetAsync(owner.Id)).First();
            Check(followed.Name == "Sediu" && followed.Phone == editedOwner.Phone && followed.HasCoordinates, "A later beneficiary edit updates only the address and phone of the main point");

            // Photos: files on disk, rows in service_photos; images only, no duplicates, a limit of 20 per point.
            var first = Png();
            var photo = await photos.AddToWorkPointAsync(point.Id, @"..\..\Poza intrare.png", first, "Intrare");
            Check(photo.OriginalName == "Poza intrare.png" && photo.ContentType == "image/png" && photo.Caption == "Intrare" && photo.UploadedBy == "integration.tester" &&
                  File.Exists(Path.Combine(liveRoot, photo.StoredName)) && photo.StoredName.EndsWith(".png"),
                "A photo is stored on disk under a generated name, with the original name cleaned of path segments");
            Check((await photos.GetContentAsync(photo.Id))!.Content.SequenceEqual(first) && (await photos.GetForWorkPointAsync(point.Id)).Single().Id == photo.Id &&
                  (await photos.CountsForBeneficiaryAsync(owner.Id))[point.Id] == 1, "The photo is read back with its content, in the list and in the counts");
            await Rejects<WorkPointOperationException>(() => photos.AddToWorkPointAsync(point.Id, "a.png", first, ""), "The same photo cannot be added twice to a work point");
            await Rejects<WorkPointOperationException>(() => photos.AddToWorkPointAsync(point.Id, "nota.png", System.Text.Encoding.UTF8.GetBytes("nu este o imagine"), ""), "A file that is not an image is rejected");
            var big = Png((int)ServicePhotoRules.MaximumBytes + 1);
            await Rejects<WorkPointOperationException>(() => photos.AddToWorkPointAsync(point.Id, "mare.png", big, ""), "A photo above 10 MB is rejected");
            await Rejects<WorkPointOperationException>(() => photos.AddToWorkPointAsync(point.Id + 1_000_000, "x.png", Png(), ""), "A photo cannot be added to a work point that does not exist");
            Check((await audit.GetEventsAsync()).Any(item => item.Action == AuditActions.AddWorkPointPhoto && item.EntityId == owner.Id.ToString() && item.Details.Contains("Poza intrare.png")),
                "The journal names the operation \"Adăugare fotografie punct de lucru\"");
            for (var index = 1; index < ServicePhotoRules.MaximumPerOwner; index++) await photos.AddToWorkPointAsync(point.Id, $"poza{index}.png", Png(), "");
            await Rejects<WorkPointOperationException>(() => photos.AddToWorkPointAsync(point.Id, "prea-multe.png", Png(), ""), "A work point cannot have more than 20 photos");

            // Deleting a photo archives it: the row, an archive row and the file moved to the archive directory.
            await photos.DeleteAsync(photo.Id);
            Check(await Count("SELECT COUNT(*) FROM service_photos WHERE id=@id", ("@id", photo.Id)) == 0 && await Count("SELECT COUNT(*) FROM archive_service_photos WHERE original_id=@id", ("@id", photo.Id)) == 1 &&
                  await Count("SELECT COUNT(*) FROM archive_files WHERE relation_type=@type AND original_relation_id=@id", ("@type", AuditEntities.ServicePhoto), ("@id", photo.Id.ToString())) == 1 &&
                  !File.Exists(Path.Combine(liveRoot, photo.StoredName)) && Directory.EnumerateFiles(archiveRoot, "Poza intrare.png", SearchOption.AllDirectories).Any(),
                "Deleting a photo archives it (row, archive row, file moved to the archive directory)");
            Check((await audit.GetEventsAsync()).Any(item => item.Action == AuditActions.Delete && item.EntityType == AuditEntities.ServicePhoto && item.EntityId == photo.Id.ToString()), "The deletion of a photo is journaled");
            await Rejects<WorkPointOperationException>(() => photos.DeleteAsync(photo.Id), "A photo already deleted cannot be deleted again");

            // Deleting a work point archives it together with its photos.
            var remaining = (await photos.GetForWorkPointAsync(point.Id)).ToList();
            await workPoints.DeleteAsync(point);
            Check(remaining.Count == ServicePhotoRules.MaximumPerOwner - 1 && await Count("SELECT COUNT(*) FROM archive_work_points WHERE original_id=@id", ("@id", point.Id)) == 1 &&
                  await Count("SELECT COUNT(*) FROM service_photos WHERE work_point_id=@id", ("@id", point.Id)) == 0 &&
                  await Count("SELECT COUNT(*) FROM archive_files f JOIN archive_operations o ON o.id=f.archive_id WHERE o.entity_type=@type AND o.original_id=@id", ("@type", AuditEntities.WorkPoint), ("@id", point.Id.ToString())) == remaining.Count &&
                  remaining.All(item => !File.Exists(Path.Combine(liveRoot, item.StoredName))),
                "Deleting a work point archives it with all its photos (files moved to the archive directory)");

            // Deleting the beneficiary archives its work points, main one included, and their photos.
            var lastPoint = await workPoints.CreateAsync(owner.Id, new WorkPointInput { Name = "Ultimul", Address = "Ultima 9, Cluj" });
            var lastPhoto = await photos.AddToWorkPointAsync(lastPoint.Id, "ultima.png", Png(), "");
            var mainId = (await workPoints.GetAsync(owner.Id)).First().Id;
            await beneficiaries.DeleteAsync((await beneficiaries.GetBeneficiariesAsync()).Single(item => item.Id == owner.Id), "Ext curatare");
            ownerDeleted = true;
            Check(await Count("SELECT COUNT(*) FROM beneficiary_work_points WHERE beneficiary_id=@id", ("@id", owner.Id)) == 0 && await Count("SELECT COUNT(*) FROM service_photos WHERE id=@id", ("@id", lastPhoto.Id)) == 0 &&
                  await Count("SELECT COUNT(*) FROM archive_relations WHERE relation_type=@type AND original_relation_id IN (@a, @b)", ("@type", AuditEntities.WorkPoint), ("@a", mainId.ToString()), ("@b", lastPoint.Id.ToString())) == 2 &&
                  await Count("SELECT COUNT(*) FROM archive_files WHERE relation_type=@type AND original_relation_id=@id", ("@type", AuditEntities.ServicePhoto), ("@id", lastPhoto.Id.ToString())) == 1 &&
                  !File.Exists(Path.Combine(liveRoot, lastPhoto.StoredName)),
                "Deleting the beneficiary archives its work points (the main one too) and moves their photo files to the archive directory");

            // The main work point of a beneficiary that has none (created before the migration) is made at startup, idempotently.
            var older = await beneficiaries.CreateAsync(Legal($"Ext Puncte vechi {suffix} SRL", "RO" + Random.Shared.Next(50000000, 59999999)));
            extra.Add(older);
            await ExecuteAsync(probe, "DELETE FROM beneficiary_work_points WHERE beneficiary_id=@id AND is_primary=1", ("@id", older.Id));
            var derived = (await workPoints.GetAsync(older.Id)).Single();
            Check(derived.Id == 0 && derived.IsPrimary && derived.Address == older.Address, "Without a stored row the main work point is shown as derived from the beneficiary (read-only)");
            var first1 = await WorkPointBackfill.EnsurePrimariesAsync(configuration);
            var stored = (await workPoints.GetAsync(older.Id)).Single();
            Check(first1.Created >= 1 && stored.Id > 0 && stored.IsPrimary && stored.Name == WorkPointRules.PrimaryName && stored.Address == older.Address && stored.Phone == older.Phone, "The backfill creates the missing main work point");
            var second = await WorkPointBackfill.EnsurePrimariesAsync(configuration);
            Check(second.Created == 0 && second.Promoted == 0, "Running the backfill again creates nothing");
            var promotable = await beneficiaries.CreateAsync(Legal($"Ext Puncte promovat {suffix} SRL", "RO" + Random.Shared.Next(60000000, 69999999)));
            extra.Add(promotable);
            await ExecuteAsync(probe, "DELETE FROM beneficiary_work_points WHERE beneficiary_id=@id AND is_primary=1", ("@id", promotable.Id));
            await ExecuteAsync(probe, "INSERT INTO beneficiary_work_points (beneficiary_id, name, address, normalized_address, phone, is_primary) VALUES (@id, 'Vechi', @address, @key, '', 0)",
                ("@id", promotable.Id), ("@address", promotable.Address), ("@key", AddressNormalization.Key(promotable.Address)));
            var promotion = await WorkPointBackfill.EnsurePrimariesAsync(configuration);
            var promoted = (await workPoints.GetAsync(promotable.Id)).Single();
            Check(promotion.Promoted >= 1 && promoted.IsPrimary && promoted.Name == "Vechi", "An additional point with the address of the beneficiary is promoted to main instead of duplicated");
        }
        finally
        {
            foreach (var item in extra.Cast<Beneficiary?>().Prepend(ownerDeleted ? null : owner))
                if (item is not null && (await beneficiaries.GetBeneficiariesAsync()).FirstOrDefault(current => current.Id == item.Id) is { } live)
                    await beneficiaries.DeleteAsync(live, "Ext curatare");
        }
    }

    // ---- Maintenance contracts -----------------------------------------------------------------------------------------------------
    private static async Task ServiceContractsAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe)
    {
        var suffix = Suffix();
        var beneficiaries = new MariaBeneficiaryRepository(configuration, admin, audit);
        var workPoints = new MariaWorkPointRepository(configuration, admin, audit);
        var contracts = new MariaServiceContractRepository(configuration, admin, audit);
        async Task<long> Count(string sql, params (string, object)[] parameters) => await ScalarLongAsync(probe, sql, parameters);
        ServiceContractInput Input(string number, params ServiceContractPointInput[] points) => new() { NumberText = number, CycleMonths = 3, Points = [.. points] };
        ServiceContractPointInput Point(WorkPoint point, DateOnly? due = null, int? cycle = null, bool move = false) => new() { WorkPointId = point.Id, NextDue = due, CycleMonths = cycle, MoveFromOtherContract = move };
        async Task<ServiceContractDetails> Fresh(int beneficiaryId, int contractId) => (await contracts.GetForBeneficiaryAsync(beneficiaryId)).Single(item => item.Contract.Id == contractId);
        var since = DateTime.UtcNow;
        await Task.Delay(30);
        var owner = await beneficiaries.CreateAsync(Legal($"Ext Contracte {suffix} SRL", "RO" + Random.Shared.Next(10000000, 19999999)));
        var stranger = await beneficiaries.CreateAsync(Legal($"Ext Contracte strain {suffix} SRL", "RO" + Random.Shared.Next(20000000, 29999999)));
        async Task<int> Events(string action) => (await audit.GetEventsAsync()).Count(item => item.TimestampUtc > since && item.EntityType == AuditEntities.Beneficiary &&
            item.EntityId == owner.Id.ToString() && item.Action == action);
        var ownerDeleted = false;
        try
        {
            var main = (await workPoints.GetAsync(owner.Id)).Single();
            var depozit = await workPoints.CreateAsync(owner.Id, new WorkPointInput { Name = "Depozit", Address = "Str. Depozitului 1, Cluj" });
            var atelier = await workPoints.CreateAsync(owner.Id, new WorkPointInput { Name = "Atelier", Address = "Str. Atelierului 2, Cluj" });
            var foreignPoint = (await workPoints.GetAsync(stranger.Id)).Single();
            var d1 = new DateOnly(2025, 10, 15);
            var d2 = new DateOnly(2025, 10, 20);

            // A contract is created On with its points: first due dates, an individual cycle, the key of the active coverage.
            var c1 = await contracts.CreateAsync(owner.Id, new ServiceContractInput { NumberText = "26 din 23.09.2025", CycleMonths = 3, ValidUntil = new DateOnly(2026, 9, 23),
                Notes = "Contract test", Points = [Point(main, d1), Point(depozit, d2, 6)] });
            var mainRow = c1.Points.Single(item => item.Point.WorkPointId == main.Id).Point;
            Check(c1.Contract.IsActive && c1.Contract.Version == 0 && c1.Contract.Label == "26/23.09.2025" && c1.Contract.CycleMonths == 3 && c1.Contract.ValidUntil == new DateOnly(2026, 9, 23) &&
                  c1.Points.Count == 2 && mainRow.NextDue == d1 && mainRow.CycleMonths is null && c1.Points.Single(item => item.Point.WorkPointId == depozit.Id).Point.CycleMonths == 6 && c1.NextDue == d1,
                "A contract is created On with its points, their first due dates and an individual cycle");
            Check(await Count("SELECT COUNT(*) FROM service_contract_points WHERE contract_id=@id AND active_work_point_id=work_point_id", ("@id", c1.Contract.Id)) == 2 &&
                  (await Fresh(owner.Id, c1.Contract.Id)).Contract == c1.Contract, "The points of an active contract carry the active key and the contract reads back identical");
            Check(c1.Points.Select(item => item.WorkPointName).SequenceEqual([WorkPointRules.PrimaryName, "Depozit"]) || c1.Points.Select(item => item.WorkPointName).SequenceEqual(["Depozit", WorkPointRules.PrimaryName]),
                "The coverage carries the names of the work points");

            // Number/date: unique per beneficiary; a contract covers only the work points of its own beneficiary.
            await Rejects<ServiceContractOperationException>(() => contracts.CreateAsync(owner.Id, Input("26/23.09.2025")), "The same number and date cannot be used twice for a beneficiary");
            await Rejects<ServiceContractOperationException>(() => contracts.CreateAsync(stranger.Id, Input("26/23.09.2025", Point(main, d1))), "A contract cannot cover a work point of another beneficiary");
            Check((await contracts.GetForBeneficiaryAsync(stranger.Id)).Count == 0, "A rejected contract leaves nothing behind");
            var strangers = await contracts.CreateAsync(stranger.Id, Input("26/23.09.2025", Point(foreignPoint, d1)));
            Check(strangers.Contract.Label == c1.Contract.Label && strangers.Contract.Id != c1.Contract.Id, "The same number and date is allowed for another beneficiary");
            await Rejects<ServiceContractOperationException>(() => contracts.CreateAsync(owner.Id, Input("27/01.10.2025", Point(main, d1))), "A point in an active contract cannot be added to another active contract");
            var taken = await Rejects<ServiceContractOperationException>(() => contracts.CreateAsync(owner.Id, Input("27/01.10.2025", Point(atelier, d1), Point(main, d1))), "The refusal names the active contract that holds the point");
            Check(taken!.Message.Contains("26/23.09.2025") && (await contracts.GetForBeneficiaryAsync(owner.Id)).Count == 1 && await Count("SELECT COUNT(*) FROM service_contract_points WHERE work_point_id=@id", ("@id", atelier.Id)) == 0,
                "A refused contract is rolled back completely (the other points were not covered either)");

            // "Muta aici": the coverage row moves with its due date, individual cycle and identity; both contracts change version.
            var c2 = await contracts.CreateAsync(owner.Id, Input("27/01.10.2025", Point(main, move: true), Point(atelier, d2)));
            var movedRow = c2.Points.Single(item => item.Point.WorkPointId == main.Id).Point;
            var c1AfterMove = await Fresh(owner.Id, c1.Contract.Id);
            Check(movedRow.Id == mainRow.Id && movedRow.NextDue == d1 && movedRow.CycleMonths is null && movedRow.Version == mainRow.Version + 1 && c2.Points.Count == 2 &&
                  c1AfterMove.Points.Count == 1 && c1AfterMove.Points[0].Point.WorkPointId == depozit.Id && c1AfterMove.Contract.Version == 1,
                "Moving a point takes its coverage row to the new contract with its due date; the old contract changes version");
            Check((await audit.GetEventsAsync()).Any(item => item.TimestampUtc > since && item.Action == AuditActions.MoveContractPoint && item.EntityId == owner.Id.ToString() &&
                  item.Details.Contains("26/23.09.2025") && item.Details.Contains("27/01.10.2025") && item.Details.Contains("15.10.2025")),
                "The journal names the operation \"Mutare punct de lucru în alt contract\" with both contracts and the kept due date");

            // The database is the last line of defence: one active contract per work point, and the key can only equal the point.
            await Rejects<MySqlException>(() => ExecuteAsync(probe, "INSERT INTO service_contract_points (contract_id, work_point_id, active_work_point_id, next_due) VALUES (@c, @w, @w, '2026-01-01')",
                ("@c", c1.Contract.Id), ("@w", main.Id)), "The database refuses a work point in two active contracts (unique active key)");
            await Rejects<MySqlException>(() => ExecuteAsync(probe, "INSERT INTO service_contract_points (contract_id, work_point_id, active_work_point_id, next_due) VALUES (@c, @w, @other, '2026-01-01')",
                ("@c", c1.Contract.Id), ("@w", atelier.Id), ("@other", depozit.Id + 500000)), "The database refuses an active key that differs from the work point");
            await Rejects<MySqlException>(() => ExecuteAsync(probe, "UPDATE service_contracts SET cycle_months=13 WHERE id=@id", ("@id", c1.Contract.Id)), "The database refuses a cycle outside 1 to 12 months");
            await Rejects<MySqlException>(() => ExecuteAsync(probe, "UPDATE service_contracts SET valid_until='2020-01-01' WHERE id=@id", ("@id", c1.Contract.Id)), "The database refuses an expiry date before the contract date");

            // Edits: the journal names the exact operation; an edit on an outdated version is rejected; an edit that changes nothing is not saved.
            var edit = ServiceContractInput.From(c1AfterMove); edit.ValidUntil = new DateOnly(2027, 9, 23);
            var c1v2 = await contracts.UpdateAsync(c1AfterMove.Contract, edit);
            await Rejects<ServiceContractOperationException>(() => contracts.UpdateAsync(c1AfterMove.Contract, edit), "An edit made on an outdated version is rejected");
            Check(c1v2.Contract.Version == 2 && c1v2.Contract.ValidUntil == new DateOnly(2027, 9, 23), "Changing the expiry date increments the version");
            edit = ServiceContractInput.From(c1v2); edit.CycleMonths = 4;
            var c1v3 = await contracts.UpdateAsync(c1v2.Contract, edit);
            edit = ServiceContractInput.From(c1v3); edit.Points[0].NextDue = new DateOnly(2025, 11, 3); edit.Points[0].CycleMonths = null;
            var c1v4 = await contracts.UpdateAsync(c1v3.Contract, edit);
            var depozitRow = c1v4.Points.Single().Point;
            Check(c1v3.Contract.CycleMonths == 4 && c1v4.Contract.Version == 4 && depozitRow.NextDue == new DateOnly(2025, 11, 3) && depozitRow.CycleMonths is null && depozitRow.Version == 1,
                "The cycle of the contract and the due date and cycle of a point are changed (the point version increments)");
            edit = ServiceContractInput.From(c1v4); edit.Notes = "Observatie noua";
            var c1v5 = await contracts.UpdateAsync(c1v4.Contract, edit);
            var unchanged = await contracts.UpdateAsync(c1v5.Contract, ServiceContractInput.From(c1v5));
            Check(c1v5.Contract.Version == 5 && unchanged.Contract.Version == 5 && (await Fresh(owner.Id, c1.Contract.Id)).Contract == c1v5.Contract, "An edit that changes nothing does not change the version");
            Check(await Events(AuditActions.EditServiceContractExpiry) == 1 && await Events(AuditActions.EditMaintenanceCycle) == 2 && await Events(AuditActions.RescheduleMaintenance) == 1 &&
                  await Events(AuditActions.EditServiceContract) == 1,
                "The journal names \"Modificare expirare contract mentenanță\", \"Modificare ciclicitate mentenanță\" (contract and point), \"Reprogramare intervenție mentenanță\" and \"Modificare contract mentenanță\"");
            Check((await audit.GetEventsAsync()).Any(item => item.Action == AuditActions.EditServiceContractExpiry && item.EntityId == owner.Id.ToString() && item.Details.Contains("23.09.2026") && item.Details.Contains("23.09.2027")),
                "The details of the expiry edit hold the old and the new date");

            // Off keeps everything and frees the points; another contract can then take them.
            var off = await contracts.DeactivateAsync(c1v5.Contract);
            Check(!off.Contract.IsActive && off.Contract.Version == 6 && off.Points.Single().Point.NextDue == new DateOnly(2025, 11, 3) &&
                  await Count("SELECT COUNT(*) FROM service_contract_points WHERE contract_id=@id AND active_work_point_id IS NOT NULL", ("@id", c1.Contract.Id)) == 0,
                "Switching a contract Off keeps its points and due dates and releases the active key");
            await Rejects<ServiceContractOperationException>(() => contracts.DeactivateAsync(c1v5.Contract), "A contract already switched Off (outdated version) cannot be deactivated again");
            var c3 = await contracts.CreateAsync(owner.Id, Input("28/01.11.2025", Point(depozit, d1)));
            Check(c3.Points.Single().Point.WorkPointId == depozit.Id && await Count("SELECT COUNT(*) FROM service_contract_points WHERE work_point_id=@id", ("@id", depozit.Id)) == 2,
                "A work point can be in an Off contract (history) and in an active contract at the same time");

            // Reactivation: a conflict stops it (or takes the point out); the due dates chosen replace the stored ones.
            var plan = await contracts.PrepareActivationAsync(off.Contract.Id);
            Check(plan.Conflicts.Single().WorkPointId == depozit.Id && plan.Conflicts.Single().ContractLabel == "28/01.11.2025" && plan.Details.Points.Count == 1,
                "The activation plan lists the points now in another active contract");
            var blocked = await Rejects<ServiceContractOperationException>(() => contracts.ActivateAsync(off.Contract, [], false), "A contract with a point in another active contract cannot be activated");
            Check(blocked!.Message.Contains("28/01.11.2025") && !(await Fresh(owner.Id, c1.Contract.Id)).Contract.IsActive, "The refusal names the other contract and the contract stays Off");
            var c3Empty = ServiceContractInput.From(c3); c3Empty.Points.Clear();
            var c3v1 = await contracts.UpdateAsync(c3.Contract, c3Empty);
            Check(c3v1.Points.Count == 0 && c3v1.NextDue is null, "A point is taken out of a contract (the contract can be left with no point)");
            var reactivated = await contracts.ActivateAsync(off.Contract, [new(depozit.Id, new DateOnly(2026, 2, 1))], false);
            Check(reactivated.Contract.IsActive && reactivated.Contract.Version == 7 && reactivated.Points.Single().Point.NextDue == new DateOnly(2026, 2, 1) &&
                  await Count("SELECT COUNT(*) FROM service_contract_points WHERE contract_id=@id AND active_work_point_id=work_point_id", ("@id", c1.Contract.Id)) == 1,
                "Reactivating a contract applies the chosen due date and restores the active key");
            await Rejects<ServiceContractOperationException>(() => contracts.ActivateAsync(off.Contract, [], false), "An outdated activation is rejected");
            var offAgain = await contracts.DeactivateAsync(reactivated.Contract);
            var c3Again = ServiceContractInput.From(c3v1); c3Again.Points.Add(Point(depozit, d1));
            var c3v2 = await contracts.UpdateAsync(c3v1.Contract, c3Again);
            var forced = await contracts.ActivateAsync(offAgain.Contract, [], true);
            Check(forced.Contract.IsActive && forced.Points.Count == 0 && (await Fresh(owner.Id, c3.Contract.Id)).Points.Single().Point.WorkPointId == depozit.Id,
                "Activating with the conflicting points taken out leaves them in the contract that holds them");
            Check(await Events(AuditActions.CreateServiceContract) == 3 && await Events(AuditActions.AddContractPoint) == 5 && await Events(AuditActions.RemoveContractPoint) == 2 &&
                  await Events(AuditActions.ActivateServiceContract) == 2 && await Events(AuditActions.DeactivateServiceContract) == 2 && await Events(AuditActions.RescheduleMaintenance) == 2,
                "The journal names each operation: add contract, add and remove point, activate, deactivate, reschedule");
            Check((await audit.GetEventsAsync()).Any(item => item.Action == AuditActions.ActivateServiceContract && item.Details.Contains("Stare: Off → On") && item.Details.Contains("03.11.2025") && item.Details.Contains("01.02.2026")),
                "The activation entry lists the points with their old and new due dates");

            // Two sessions at once: exactly one contract takes a free work point; exactly one of two edits of the same version wins.
            var contested = await workPoints.CreateAsync(owner.Id, new WorkPointInput { Name = "Disputat", Address = "Str. Disputei 3, Cluj" });
            var takers = await Task.WhenAll(new[] { "31/01.12.2025", "32/01.12.2025" }.Select(async number =>
            {
                try { await new MariaServiceContractRepository(configuration, admin, audit).CreateAsync(owner.Id, Input(number, Point(contested, d1))); return true; }
                catch (ServiceContractOperationException) { return false; }
            }));
            Check(takers.Count(item => item) == 1 && await Count("SELECT COUNT(*) FROM service_contract_points WHERE work_point_id=@id AND active_work_point_id IS NOT NULL", ("@id", contested.Id)) == 1,
                "Two concurrent contracts cannot both take the same work point (exactly one succeeds)");
            var toEdit = await Fresh(owner.Id, c3.Contract.Id);
            var writers = await Task.WhenAll(new[] { "prima", "a doua" }.Select(async note =>
            {
                var concurrent = ServiceContractInput.From(toEdit); concurrent.Notes = note;
                try { await new MariaServiceContractRepository(configuration, admin, audit).UpdateAsync(toEdit.Contract, concurrent); return true; }
                catch (ServiceContractOperationException) { return false; }
            }));
            Check(writers.Count(item => item) == 1 && (await Fresh(owner.Id, c3.Contract.Id)).Contract.Version == toEdit.Contract.Version + 1, "Two concurrent edits of the same version: exactly one is saved");

            // A covered work point and a beneficiary with contracts cannot be deleted; a contract is deleted with a reason, archived with its coverage.
            var covered = await Rejects<WorkPointOperationException>(() => workPoints.DeleteAsync(atelier), "A work point covered by a contract cannot be deleted");
            Check(covered!.Message.Contains("27/01.10.2025"), "The refusal names the contract that covers the point");
            await Rejects<BeneficiaryOperationException>(() => beneficiaries.DeleteAsync(owner, "Ext cu contracte"), "A beneficiary with contracts cannot be deleted");
            var c2Fresh = await Fresh(owner.Id, c2.Contract.Id);
            var c2Edit = ServiceContractInput.From(c2Fresh); c2Edit.Points.RemoveAll(item => item.WorkPointId == atelier.Id);
            var c2v1 = await contracts.UpdateAsync(c2Fresh.Contract, c2Edit);
            await workPoints.DeleteAsync(atelier);
            Check(c2v1.Points.Single().Point.WorkPointId == main.Id && await Count("SELECT COUNT(*) FROM archive_work_points WHERE original_id=@id", ("@id", atelier.Id)) == 1 && await Events(AuditActions.RemoveContractPoint) == 3,
                "After the point is taken out of its contract it can be deleted (archived)");
            await Rejects<ServiceContractOperationException>(() => contracts.DeleteAsync(c2v1.Contract, ""), "Deleting a contract needs a reason");
            await Rejects<ServiceContractOperationException>(() => contracts.DeleteAsync(c2Fresh.Contract, "Ext curatare"), "A contract cannot be deleted from an outdated version");
            await contracts.DeleteAsync(c2v1.Contract, "Ext curatare");
            Check(await Count("SELECT COUNT(*) FROM service_contracts WHERE id=@id", ("@id", c2.Contract.Id)) == 0 && await Count("SELECT COUNT(*) FROM service_contract_points WHERE contract_id=@id", ("@id", c2.Contract.Id)) == 0 &&
                  await Count("SELECT COUNT(*) FROM archive_service_contracts WHERE original_id=@id AND is_active=1", ("@id", c2.Contract.Id)) == 1 &&
                  await Count("SELECT COUNT(*) FROM archive_relations WHERE relation_type=@type AND original_relation_id=@id", ("@type", ArchiveRequests.ServiceContractPointRelation), ("@id", movedRow.Id.ToString())) == 1,
                "Deleting a contract archives it (row and coverage) and removes it from the live tables");
            Check((await audit.GetEventsAsync()).Any(item => item.Action == AuditActions.Delete && item.EntityType == AuditEntities.ServiceContract && item.EntityId == c2.Contract.Id.ToString() &&
                  item.Motif == "Ext curatare" && item.Target.Contains("27/01.10.2025")), "The deletion of a contract is journaled with its reason");
            var deletedMain = (await workPoints.GetAsync(owner.Id)).First(item => item.IsPrimary);
            Check(deletedMain.Id == main.Id, "The main work point is left in place when its contract is deleted");

            // Cleanup through the repositories: every contract of both beneficiaries, then the beneficiaries (with their archived work points).
            foreach (var beneficiaryId in new[] { owner.Id, stranger.Id })
                foreach (var details in await contracts.GetForBeneficiaryAsync(beneficiaryId))
                    await contracts.DeleteAsync(details.Contract, "Ext curatare");
            Check((await contracts.GetForBeneficiaryAsync(owner.Id)).Count == 0 && await Count("SELECT COUNT(*) FROM archive_service_contracts WHERE beneficiary_id=@id", ("@id", owner.Id)) == 4,
                "Every contract of the beneficiary was archived (four in all)");
            await beneficiaries.DeleteAsync((await beneficiaries.GetBeneficiariesAsync()).Single(item => item.Id == owner.Id), "Ext curatare");
            ownerDeleted = true;
            Check(await Count("SELECT COUNT(*) FROM beneficiary_work_points WHERE beneficiary_id=@id", ("@id", owner.Id)) == 0, "After its contracts are gone the beneficiary is deleted with its work points");
        }
        finally
        {
            foreach (var item in new[] { ownerDeleted ? null : owner, stranger })
            {
                if (item is null) continue;
                foreach (var details in await contracts.GetForBeneficiaryAsync(item.Id))
                    await contracts.DeleteAsync(details.Contract, "Ext curatare");
                if ((await beneficiaries.GetBeneficiariesAsync()).FirstOrDefault(current => current.Id == item.Id) is { } live)
                    await beneficiaries.DeleteAsync(live, "Ext curatare");
            }
        }
    }

    // ---- Register of interventions ---------------------------------------------------------------------------------------------------
    private static async Task ServiceInterventionsAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe, string assets)
    {
        var suffix = Suffix();
        var beneficiaries = new MariaBeneficiaryRepository(configuration, admin, audit);
        var workPoints = new MariaWorkPointRepository(configuration, admin, audit);
        var contracts = new MariaServiceContractRepository(configuration, admin, audit);
        var interventions = new MariaServiceInterventionRepository(configuration, admin, audit);
        var photos = new MariaServicePhotoStore(configuration, admin, audit);
        var liveRoot = Path.Combine(assets, "service-photos");
        async Task<long> Count(string sql, params (string, object)[] parameters) => await ScalarLongAsync(probe, sql, parameters);
        byte[] Png(int size = 300) { var bytes = new byte[size]; Random.Shared.NextBytes(bytes); new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(bytes, 0); return bytes; }
        ServiceInterventionInput Maintenance(WorkPoint point, DateOnly performed, ServiceNextDueBasis basis = ServiceNextDueBasis.FromPerformed, DateOnly? chosen = null, string notes = "") =>
            new() { Kind = ServiceInterventionKind.Maintenance, WorkPointId = point.Id, PerformedOn = performed, Basis = basis, ChosenDue = chosen, Notes = notes };
        ServiceInterventionInput OnDemand(WorkPoint point, DateOnly performed, string notes = "") =>
            new() { Kind = ServiceInterventionKind.OnDemand, WorkPointId = point.Id, PerformedOn = performed, Notes = notes };
        async Task<ServiceContractDetails> Contract(int beneficiaryId, int contractId) => (await contracts.GetForBeneficiaryAsync(beneficiaryId)).Single(item => item.Contract.Id == contractId);
        async Task<DateOnly> Due(int beneficiaryId, int contractId, int workPointId) => (await Contract(beneficiaryId, contractId)).Points.Single(item => item.Point.WorkPointId == workPointId).Point.NextDue;
        var since = DateTime.UtcNow;
        await Task.Delay(30);
        var owner = await beneficiaries.CreateAsync(Legal($"Ext Interventii {suffix} SRL", "RO" + Random.Shared.Next(30000000, 39999999)));
        var stranger = await beneficiaries.CreateAsync(Legal($"Ext Interventii strain {suffix} SRL", "RO" + Random.Shared.Next(90000000, 99999999)));
        async Task<int> Events(string action) => (await audit.GetEventsAsync()).Count(item => item.TimestampUtc > since && item.EntityType == AuditEntities.Beneficiary &&
            item.EntityId == owner.Id.ToString() && item.Action == action);
        var ownerDeleted = false;
        try
        {
            var main = (await workPoints.GetAsync(owner.Id)).Single();
            var depozit = await workPoints.CreateAsync(owner.Id, new WorkPointInput { Name = "Depozit", Address = "Str. Depozitului 1, Cluj" });
            var atelier = await workPoints.CreateAsync(owner.Id, new WorkPointInput { Name = "Atelier", Address = "Str. Atelierului 2, Cluj" });
            var concurrent = await workPoints.CreateAsync(owner.Id, new WorkPointInput { Name = "Concurent", Address = "Str. Concurentei 4, Cluj" });
            var foreignPoint = (await workPoints.GetAsync(stranger.Id)).Single();
            var today = DateOnly.FromDateTime(DateTime.Now);
            var c1 = await contracts.CreateAsync(owner.Id, new ServiceContractInput { NumberText = "26/23.09.2025", CycleMonths = 3,
                Points = [new() { WorkPointId = main.Id, NextDue = new DateOnly(2025, 10, 15) }, new() { WorkPointId = depozit.Id, NextDue = new DateOnly(2025, 10, 20), CycleMonths = 6 }] });

            // Maintenance under an active contract moves the due date; the choice of the next due date is E / P / O.
            var i1 = await interventions.RecordAsync(owner.Id, Maintenance(main, new DateOnly(2025, 10, 18), notes: "Prima vizita"));
            Check(i1.Intervention.Kind == ServiceInterventionKind.Maintenance && i1.Intervention.PlannedDue == new DateOnly(2025, 10, 15) && i1.NewDue == new DateOnly(2026, 1, 18) &&
                  i1.Intervention.Basis == ServiceNextDueBasis.FromPerformed && i1.Intervention.NextDueSet == i1.NewDue && i1.Intervention.ContractLabel == "26/23.09.2025" &&
                  i1.Intervention.ContractId == c1.Contract.Id && i1.Intervention.WorkPointName == main.Name && i1.Intervention.WorkPointAddress == main.Address &&
                  i1.Intervention.RecordedBy == "integration.tester" && i1.Intervention.Version == 0 && i1.Intervention.MovesDue,
                "A maintenance intervention closes the planned due date and sets the next one from the date performed, keeping snapshots of the point and the contract");
            Check(await Due(owner.Id, c1.Contract.Id, main.Id) == new DateOnly(2026, 1, 18) && (await Contract(owner.Id, c1.Contract.Id)).Contract.Version == c1.Contract.Version + 1,
                "The coverage of the point carries the new due date and the contract changes version");
            var onDemand = await interventions.RecordAsync(owner.Id, OnDemand(main, new DateOnly(2026, 3, 12), "Interventie la cerere"));
            Check(onDemand.Intervention.ContractId is null && onDemand.Intervention.ContractLabel is null && onDemand.Intervention.PlannedDue is null && onDemand.Intervention.Basis is null && !onDemand.Intervention.MovesDue &&
                  onDemand.NewDue is null && await Due(owner.Id, c1.Contract.Id, main.Id) == new DateOnly(2026, 1, 18),
                "An on-demand intervention has no contract and does not touch the due date");
            var i3 = await interventions.RecordAsync(owner.Id, Maintenance(main, new DateOnly(2026, 2, 5), ServiceNextDueBasis.FromPlanned));
            Check(i3.Intervention.PlannedDue == new DateOnly(2026, 1, 18) && i3.NewDue == new DateOnly(2026, 4, 18) && i3.Intervention.Basis == ServiceNextDueBasis.FromPlanned,
                "The next due date can be counted from the planned date (a late visit does not move the series)");
            var d1 = await interventions.RecordAsync(owner.Id, Maintenance(depozit, new DateOnly(2025, 11, 5), ServiceNextDueBasis.Chosen, new DateOnly(2026, 6, 2)));
            var d2 = await interventions.RecordAsync(owner.Id, Maintenance(depozit, new DateOnly(2026, 6, 10)));
            Check(d1.Intervention.PlannedDue == new DateOnly(2025, 10, 20) && d1.NewDue == new DateOnly(2026, 6, 2) && d2.Intervention.PlannedDue == new DateOnly(2026, 6, 2) && d2.NewDue == new DateOnly(2026, 12, 10),
                "A date chosen by the operator is used as is, and the individual cycle of the point (6 months) is the one applied");

            // The choice must give a date after the date performed.
            await Rejects<ServiceInterventionOperationException>(() => interventions.RecordAsync(owner.Id, Maintenance(main, new DateOnly(2026, 9, 20), ServiceNextDueBasis.FromPlanned)),
                "The planned-date variant is refused when the visit came more than a cycle late");
            await Rejects<ServiceInterventionOperationException>(() => interventions.RecordAsync(owner.Id, Maintenance(main, new DateOnly(2026, 9, 20), ServiceNextDueBasis.Chosen)), "A chosen due date is required");
            await Rejects<ServiceInterventionOperationException>(() => interventions.RecordAsync(owner.Id, Maintenance(main, new DateOnly(2026, 9, 20), ServiceNextDueBasis.Chosen, new DateOnly(2026, 9, 20))),
                "A chosen due date must be after the date performed");
            Check(await Due(owner.Id, c1.Contract.Id, main.Id) == new DateOnly(2026, 4, 18), "A refused intervention changes nothing");
            var i4 = await interventions.RecordAsync(owner.Id, Maintenance(main, new DateOnly(2026, 9, 20)));
            Check(i4.Intervention.PlannedDue == new DateOnly(2026, 4, 18) && i4.NewDue == new DateOnly(2026, 12, 20), "From the date performed: 20.09.2026 + 3 months = 20.12.2026");

            // Only the latest maintenance intervention of the point decides the due date: an older one moves nothing.
            var older = await interventions.RecordAsync(owner.Id, Maintenance(main, new DateOnly(2026, 1, 1), ServiceNextDueBasis.Chosen, new DateOnly(2030, 1, 1)));
            Check(older.Intervention.Basis is null && older.Intervention.PlannedDue is null && older.Intervention.NextDueSet is null && !older.Intervention.MovesDue && older.NewDue is null &&
                  older.Intervention.ContractId == c1.Contract.Id && await Due(owner.Id, c1.Contract.Id, main.Id) == new DateOnly(2026, 12, 20),
                "An intervention older than the latest maintenance one of the point moves nothing (the choice is ignored)");

            // Rules on the point and the date.
            await Rejects<ServiceInterventionOperationException>(() => interventions.RecordAsync(owner.Id, Maintenance(atelier, new DateOnly(2026, 5, 1))), "Maintenance needs a work point under an active contract");
            var atelierCall = await interventions.RecordAsync(owner.Id, OnDemand(atelier, new DateOnly(2026, 5, 1), "Fara contract"));
            Check(atelierCall.Intervention.WorkPointId == atelier.Id && atelierCall.Intervention.ContractId is null, "An on-demand intervention is allowed on a work point outside any contract");
            await Rejects<ServiceInterventionOperationException>(() => interventions.RecordAsync(owner.Id, Maintenance(main, today.AddDays(1))), "The date performed cannot be in the future");
            await Rejects<ServiceInterventionOperationException>(() => interventions.RecordAsync(owner.Id, OnDemand(foreignPoint, new DateOnly(2026, 5, 1))), "An intervention cannot be recorded on a work point of another beneficiary");
            await Rejects<ServiceInterventionOperationException>(() => interventions.RecordAsync(owner.Id, new ServiceInterventionInput { WorkPointId = main.Id }), "The date performed is required");

            // The journal names each operation with the chosen variant and the old and new due dates.
            Check(await Events(AuditActions.RecordMaintenance) == 6 && await Events(AuditActions.RecordOnDemand) == 2 &&
                  (await audit.GetEventsAsync()).Any(item => item.TimestampUtc > since && item.Action == AuditActions.RecordMaintenance && item.EntityId == owner.Id.ToString() &&
                      item.Details.Contains("din data planificată") && item.Details.Contains("18.01.2026 → 18.04.2026")) &&
                  (await audit.GetEventsAsync()).Any(item => item.TimestampUtc > since && item.Action == AuditActions.RecordMaintenance && item.Details.Contains("Nu modifică scadența")),
                "The journal names \"Înregistrare intervenție mentenanță\" (variant and due dates) and \"Înregistrare intervenție la cerere\"");

            // The database is the last line of defence for the shape of a row.
            async Task<int> RawInsert(string values) => await ExecuteAsync(probe, "INSERT INTO service_interventions (kind, beneficiary_id, work_point_id, contract_id, work_point_name, work_point_address, contract_label, performed_on, planned_due, next_due_basis, next_due_set, recorded_by, recorded_utc) VALUES " + values,
                ("@b", owner.Id), ("@w", main.Id), ("@c", c1.Contract.Id));
            await Rejects<MySqlException>(() => RawInsert("('X', @b, @w, @c, 'x', 'y', 'z', '2026-01-01', NULL, NULL, NULL, 't', '2026-01-01T00:00:00.000Z')"), "The database refuses an unknown kind");
            await Rejects<MySqlException>(() => RawInsert("('C', @b, @w, @c, 'x', 'y', NULL, '2026-01-01', NULL, NULL, NULL, 't', '2026-01-01T00:00:00.000Z')"), "The database refuses an on-demand intervention with a contract");
            await Rejects<MySqlException>(() => RawInsert("('M', @b, @w, @c, 'x', 'y', 'z', '2026-01-01', '2025-12-01', NULL, NULL, 't', '2026-01-01T00:00:00.000Z')"), "The database refuses a due date closed without a choice");
            await Rejects<MySqlException>(() => RawInsert("('M', @b, @w, @c, 'x', 'y', 'z', '2026-01-01', '2025-12-01', 'E', '2026-01-01', 't', '2026-01-01T00:00:00.000Z')"), "The database refuses a next due date not after the date performed");
            await Rejects<MySqlException>(() => ExecuteAsync(probe, "INSERT INTO service_photos (intervention_id, relative_path, original_name, content_type, byte_length, sha256, uploaded_by, uploaded_utc) VALUES (@i, 'a', 'a', 'image/png', 1, 'h', 't', 'x')", ("@i", 999999999)),
                "The database refuses a photo of an intervention that does not exist");

            // Corrections: notes always; the date and the choice only for the latest due-moving maintenance intervention.
            var olderNotes = ServiceInterventionInput.From(older.Intervention); olderNotes.Notes = "Observatie corectata";
            var olderSaved = await interventions.UpdateAsync(older.Intervention, olderNotes);
            Check(olderSaved.Intervention.Notes == "Observatie corectata" && olderSaved.Intervention.Version == 1 && await Due(owner.Id, c1.Contract.Id, main.Id) == new DateOnly(2026, 12, 20),
                "The notes of any intervention can be corrected");
            var olderDate = ServiceInterventionInput.From(olderSaved.Intervention); olderDate.PerformedOn = new DateOnly(2026, 1, 2);
            await Rejects<ServiceInterventionOperationException>(() => interventions.UpdateAsync(olderSaved.Intervention, olderDate), "The date of an intervention that is not the latest maintenance one cannot be changed");
            var latestEdit = ServiceInterventionInput.From(i4.Intervention); latestEdit.PerformedOn = new DateOnly(2026, 2, 1);
            await Rejects<ServiceInterventionOperationException>(() => interventions.UpdateAsync(i4.Intervention, latestEdit), "The date cannot be moved before the previous maintenance intervention of the point");
            latestEdit.PerformedOn = new DateOnly(2026, 9, 22);
            var i4Saved = await interventions.UpdateAsync(i4.Intervention, latestEdit);
            Check(i4Saved.Intervention.PerformedOn == new DateOnly(2026, 9, 22) && i4Saved.NewDue == new DateOnly(2026, 12, 22) && i4Saved.PreviousDue == new DateOnly(2026, 12, 20) &&
                  i4Saved.Intervention.PlannedDue == new DateOnly(2026, 4, 18) && await Due(owner.Id, c1.Contract.Id, main.Id) == new DateOnly(2026, 12, 22),
                "Correcting the date of the latest maintenance intervention reopens the choice and moves the due date");
            await Rejects<ServiceInterventionOperationException>(() => interventions.UpdateAsync(i4.Intervention, latestEdit), "A correction made on an outdated version is rejected");
            var latestChosen = ServiceInterventionInput.From(i4Saved.Intervention); latestChosen.Basis = ServiceNextDueBasis.Chosen; latestChosen.ChosenDue = new DateOnly(2027, 1, 15);
            var i4Chosen = await interventions.UpdateAsync(i4Saved.Intervention, latestChosen);
            Check(i4Chosen.Intervention.Basis == ServiceNextDueBasis.Chosen && await Due(owner.Id, c1.Contract.Id, main.Id) == new DateOnly(2027, 1, 15), "The choice of the next due date can be changed on the latest intervention");
            var onDemandEdit = ServiceInterventionInput.From(onDemand.Intervention); onDemandEdit.PerformedOn = new DateOnly(2026, 3, 13); onDemandEdit.Notes = "Cerere corectata";
            var onDemandSaved = await interventions.UpdateAsync(onDemand.Intervention, onDemandEdit);
            Check(onDemandSaved.Intervention.PerformedOn == new DateOnly(2026, 3, 13) && onDemandSaved.NewDue is null && await Due(owner.Id, c1.Contract.Id, main.Id) == new DateOnly(2027, 1, 15),
                "The date and the notes of an on-demand intervention can be corrected without touching the due date");
            var noChange = await interventions.UpdateAsync(onDemandSaved.Intervention, ServiceInterventionInput.From(onDemandSaved.Intervention));
            Check(noChange.Intervention.Version == onDemandSaved.Intervention.Version, "A correction that changes nothing is not saved");
            Check(await Events(AuditActions.EditMaintenanceIntervention) == 3 && await Events(AuditActions.EditOnDemandIntervention) == 1 &&
                  (await audit.GetEventsAsync()).Any(item => item.TimestampUtc > since && item.Action == AuditActions.EditMaintenanceIntervention && item.Details.Contains("Efectuată la: 20.09.2026 → 22.09.2026")),
                "The journal names \"Modificare intervenție mentenanță\" and \"Modificare intervenție la cerere\" with the old and the new values");

            // Photos of an intervention (files on disk, rows in service_photos, archived when deleted).
            var content = Png();
            var photo = await photos.AddToInterventionAsync(i4Chosen.Intervention.Id, "Filtru curatat.png", content, "Filtru");
            Check(photo.InterventionId == i4.Intervention.Id && photo.WorkPointId is null && File.Exists(Path.Combine(liveRoot, photo.StoredName)) &&
                  (await photos.GetForInterventionAsync(i4.Intervention.Id)).Single().Id == photo.Id && (await photos.CountsForInterventionsAsync(owner.Id))[i4.Intervention.Id] == 1,
                "A photo is added to an intervention and listed with it");
            await Rejects<WorkPointOperationException>(() => photos.AddToInterventionAsync(i4.Intervention.Id, "alta.png", content, ""), "The same photo cannot be added twice to an intervention");
            await Rejects<WorkPointOperationException>(() => photos.AddToInterventionAsync(i4.Intervention.Id + 1_000_000, "x.png", Png(), ""), "A photo cannot be added to an intervention that does not exist");
            Check(await Events(AuditActions.AddInterventionPhoto) == 1, "The journal names \"Adăugare fotografie intervenție\"");
            var extraPhoto = await photos.AddToInterventionAsync(i4.Intervention.Id, "Dupa.png", Png(), "");
            await photos.DeleteAsync(extraPhoto.Id);
            Check(await Count("SELECT COUNT(*) FROM archive_service_photos WHERE original_id=@id", ("@id", extraPhoto.Id)) == 1 && !File.Exists(Path.Combine(liveRoot, extraPhoto.StoredName)), "Deleting the photo of an intervention archives it");

            // Register queries: filters, order and paging.
            var all = await interventions.GetForBeneficiaryAsync(owner.Id);
            Check(all.Count == 8 && all.Zip(all.Skip(1)).All(pair => pair.First.PerformedOn >= pair.Second.PerformedOn), "The interventions of a beneficiary are listed newest first");
            var page = await interventions.GetPageAsync(new(BeneficiaryId: owner.Id));
            Check(page.TotalCount == 8 && page.Items.Count == 8 && page.Items.All(item => item.BeneficiaryName == owner.Name) && page.Items.Single(item => item.Intervention.Id == i4.Intervention.Id).PhotoCount == 1,
                "The register lists the interventions with the beneficiary and the number of photos");
            Check((await interventions.GetPageAsync(new(BeneficiaryId: owner.Id, Kind: ServiceInterventionKind.OnDemand))).TotalCount == 2 &&
                  (await interventions.GetPageAsync(new(BeneficiaryId: owner.Id, Kind: ServiceInterventionKind.Maintenance))).TotalCount == 6, "The register filters by kind");
            Check((await interventions.GetPageAsync(new(BeneficiaryId: owner.Id, From: new DateOnly(2026, 2, 1), To: new DateOnly(2026, 3, 31)))).TotalCount == 2 &&
                  (await interventions.GetPageAsync(new(BeneficiaryId: owner.Id, Text: "deposit"))).TotalCount == 0 && (await interventions.GetPageAsync(new(BeneficiaryId: owner.Id, Text: "DEPOZIT"))).TotalCount == 2 &&
                  (await interventions.GetPageAsync(new(Text: "26/23.09.2025", BeneficiaryId: owner.Id))).TotalCount == 6, "The register filters by period and by text (beneficiary, work point, contract), ignoring case");
            var firstPage = await interventions.GetPageAsync(new(BeneficiaryId: owner.Id, Page: 1, PageSize: 3));
            var secondPage = await interventions.GetPageAsync(new(BeneficiaryId: owner.Id, Page: 2, PageSize: 3));
            Check(firstPage.Items.Count == 3 && secondPage.Items.Count == 3 && firstPage.TotalCount == 8 && firstPage.Items.Select(item => item.Intervention.Id).Intersect(secondPage.Items.Select(item => item.Intervention.Id)).Count() == 0,
                "The register is paged");
            var dueList = await contracts.GetDueListAsync(false);
            Check(dueList.Where(row => row.BeneficiaryId == owner.Id).Select(row => row.Point.WorkPointName).Order().SequenceEqual(new[] { depozit.Name, WorkPointRules.PrimaryName }.Order()) &&
                  dueList.Zip(dueList.Skip(1)).All(pair => pair.First.Point.Point.NextDue <= pair.Second.Point.Point.NextDue),
                "The due list has every point of the active contracts, earliest due date first");
            var depozitInput = WorkPointInput.From(depozit); depozitInput.UseCoordinates = true; depozitInput.CoordinatesText = "45.7489, 21.2087";
            await workPoints.UpdateAsync(depozit, depozitInput);
            var mapRows = (await contracts.GetDueListAsync(false)).Where(row => row.BeneficiaryId == owner.Id).ToList();
            var depozitRow = mapRows.Single(row => row.Point.WorkPointName == depozit.Name);
            var mainRow = mapRows.Single(row => row.Point.WorkPointName == main.Name);
            Check(depozitRow.HasCoordinates && depozitRow.Latitude == 45.7489m && depozitRow.Longitude == 21.2087m && !mainRow.HasCoordinates && mainRow.Latitude is null &&
                  depozitRow.LastIntervention == new DateOnly(2026, 6, 10) && mainRow.LastIntervention == new DateOnly(2026, 9, 22),
                "The due list carries the coordinates of the work point (none when it has none) and the date of its latest maintenance intervention, for the map");

            // Two sessions at once on the same point: they are serialized and the last date performed decides the due date.
            var c1Fresh = await Contract(owner.Id, c1.Contract.Id);
            var withConcurrent = ServiceContractInput.From(c1Fresh); withConcurrent.Points.Add(new() { WorkPointId = concurrent.Id, NextDue = new DateOnly(2026, 8, 1) });
            await contracts.UpdateAsync(c1Fresh.Contract, withConcurrent);
            var both = await Task.WhenAll(new[] { new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 20) }.Select(day =>
                new MariaServiceInterventionRepository(configuration, admin, audit).RecordAsync(owner.Id, Maintenance(concurrent, day))));
            var moving = both.Where(item => item.Intervention.MovesDue).ToList();
            Check(await Due(owner.Id, c1.Contract.Id, concurrent.Id) == new DateOnly(2026, 11, 20) && moving.Count is 1 or 2 &&
                  (moving.Count == 1 || moving.Any(first => moving.Any(second => second.Intervention.PlannedDue == first.NewDue))),
                "Two simultaneous maintenance interventions on one point are serialized (the latest date performed decides the due date)");

            // Deleting: the due date goes back to the one the intervention closed when it is the latest one.
            var beforeDelete = await Due(owner.Id, c1.Contract.Id, main.Id);
            await Rejects<ServiceInterventionOperationException>(() => interventions.DeleteAsync(i4Chosen.Intervention, ""), "Deleting an intervention needs a reason");
            await Rejects<ServiceInterventionOperationException>(() => interventions.DeleteAsync(i4.Intervention, "Ext curatare"), "An intervention cannot be deleted from an outdated version");
            await interventions.DeleteAsync(i4Chosen.Intervention, "Ext curatare");
            Check(beforeDelete == new DateOnly(2027, 1, 15) && await Due(owner.Id, c1.Contract.Id, main.Id) == new DateOnly(2026, 4, 18), "Deleting the latest maintenance intervention brings the due date back to the one it closed");
            Check(await Count("SELECT COUNT(*) FROM service_interventions WHERE id=@id", ("@id", i4.Intervention.Id)) == 0 && await Count("SELECT COUNT(*) FROM archive_service_interventions WHERE original_id=@id AND kind='M' AND next_due_basis='O'", ("@id", i4.Intervention.Id)) == 1 &&
                  await Count("SELECT COUNT(*) FROM archive_files f JOIN archive_operations o ON o.id=f.archive_id WHERE o.entity_type=@type AND o.original_id=@id", ("@type", AuditEntities.ServiceIntervention), ("@id", i4.Intervention.Id.ToString())) == 1 &&
                  !File.Exists(Path.Combine(liveRoot, photo.StoredName)) && await Count("SELECT COUNT(*) FROM service_photos WHERE intervention_id=@id", ("@id", i4.Intervention.Id)) == 0,
                "Deleting an intervention archives it with its photo (the file moves to the archive directory)");
            Check((await audit.GetEventsAsync()).Any(item => item.Action == AuditActions.Delete && item.EntityType == AuditEntities.ServiceIntervention && item.EntityId == i4.Intervention.Id.ToString() &&
                  item.Motif == "Ext curatare" && item.Target.Contains("de mentenanță")), "The deletion of an intervention is journaled with its reason and kind");
            var main2 = await Contract(owner.Id, c1.Contract.Id);
            var reschedule = ServiceContractInput.From(main2); reschedule.Points.Single(item => item.WorkPointId == main.Id).NextDue = new DateOnly(2026, 6, 1);
            await contracts.UpdateAsync(main2.Contract, reschedule);
            var staleEdit = ServiceInterventionInput.From(i3.Intervention); staleEdit.PerformedOn = new DateOnly(2026, 2, 6);
            await Rejects<ServiceInterventionOperationException>(() => interventions.UpdateAsync(i3.Intervention, staleEdit), "A correction is refused when the due date of the point was changed by hand meanwhile");
            await interventions.DeleteAsync(i3.Intervention, "Ext curatare");
            Check(await Due(owner.Id, c1.Contract.Id, main.Id) == new DateOnly(2026, 6, 1), "A due date changed by hand meanwhile is not overwritten when the intervention is deleted");

            // What the register protects: a contract, a work point and a beneficiary with interventions cannot be deleted.
            var contractBlocked = await Rejects<ServiceContractOperationException>(() => contracts.DeleteAsync(c1.Contract, "Ext cu interventii"), "A contract with interventions cannot be deleted");
            Check(contractBlocked!.Message.Contains("intervenții") || contractBlocked.Message.Contains("interven"), "The refusal says the contract has interventions");
            var pointBlocked = await Rejects<WorkPointOperationException>(() => workPoints.DeleteAsync(atelier), "A work point with interventions cannot be deleted");
            Check(pointBlocked!.Message.Contains("interven"), "The refusal says the work point has interventions");
            await Rejects<BeneficiaryOperationException>(() => beneficiaries.DeleteAsync(owner, "Ext cu interventii"), "A beneficiary with interventions cannot be deleted");

            // Cleanup through the repositories: every intervention, the contracts, then the beneficiaries.
            foreach (var item in await interventions.GetForBeneficiaryAsync(owner.Id)) await interventions.DeleteAsync(item, "Ext curatare");
            Check((await interventions.GetForBeneficiaryAsync(owner.Id)).Count == 0 && await Count("SELECT COUNT(*) FROM archive_service_interventions WHERE beneficiary_id=@id", ("@id", owner.Id)) >= 8,
                "Every intervention of the beneficiary was archived");
            foreach (var details in await contracts.GetForBeneficiaryAsync(owner.Id)) await contracts.DeleteAsync(details.Contract, "Ext curatare");
            await beneficiaries.DeleteAsync((await beneficiaries.GetBeneficiariesAsync()).Single(item => item.Id == owner.Id), "Ext curatare");
            ownerDeleted = true;
            Check(await Count("SELECT COUNT(*) FROM beneficiary_work_points WHERE beneficiary_id=@id", ("@id", owner.Id)) == 0, "After its interventions and contracts are gone the beneficiary is deleted");
        }
        finally
        {
            foreach (var item in new[] { ownerDeleted ? null : owner, stranger })
            {
                if (item is null) continue;
                foreach (var intervention in await interventions.GetForBeneficiaryAsync(item.Id)) await interventions.DeleteAsync(intervention, "Ext curatare");
                foreach (var details in await contracts.GetForBeneficiaryAsync(item.Id)) await contracts.DeleteAsync(details.Contract, "Ext curatare");
                if ((await beneficiaries.GetBeneficiariesAsync()).FirstOrDefault(current => current.Id == item.Id) is { } live)
                    await beneficiaries.DeleteAsync(live, "Ext curatare");
            }
        }
    }

    // ---- Notification sources of the maintenance contracts -------------------------------------------------------------------------------
    private static async Task MaintenanceNotificationsAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe)
    {
        var suffix = Suffix();
        var clock = new ManualTimeProvider();
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var beneficiaries = new MariaBeneficiaryRepository(configuration, admin, audit);
        var workPoints = new MariaWorkPointRepository(configuration, admin, audit);
        var contracts = new MariaServiceContractRepository(configuration, admin, audit);
        var interventions = new MariaServiceInterventionRepository(configuration, admin, audit);
        var repository = new MariaExpiryNotificationRepository(configuration);
        var reader = new MariaMaintenanceNotificationReader(configuration);
        // Own keys: the real templates of the maintenance sources (if an administrator made them) are not touched.
        var dueSource = new MaintenanceDueSource(reader, "mentenanta.scadenta.e" + suffix);
        var expirySource = new ContractExpirySource(reader, "contract.expirare.e" + suffix);
        ExpiryNotificationService Session(string user, bool administrator) =>
            new(repository, [dueSource, expirySource], administrator ? admin : new TestAccessControl(false, user), audit, clock);
        var boss = Session("integration.tester", true);
        var since = DateTime.UtcNow;
        await Task.Delay(30);
        var owner = await beneficiaries.CreateAsync(Legal($"Ext Notif Mentenanta {suffix} SRL", "RO" + Random.Shared.Next(30000000, 39999999)));
        NotificationTemplate? dueTemplate = null, expiryTemplate = null;
        async Task<IReadOnlyList<ExpiryNotification>> Mine(string sourceKey, int? objectId = null) => (await repository.GetNotificationsAsync())
            .Where(item => item.SourceKey == sourceKey && (objectId is null || item.ObjectId == objectId)).ToList();
        try
        {
            var main = (await workPoints.GetAsync(owner.Id)).Single();
            var depozit = await workPoints.CreateAsync(owner.Id, new WorkPointInput { Name = "Depozit", Address = "Str. Depozitului 1, Cluj" });
            var c1 = await contracts.CreateAsync(owner.Id, new ServiceContractInput { NumberText = "26/23.09.2025", CycleMonths = 3, ValidUntil = today.AddDays(20),
                Points = [new() { WorkPointId = main.Id, NextDue = today.AddDays(10) }, new() { WorkPointId = depozit.Id, NextDue = today.AddDays(100) }] });
            var coverageMain = c1.Points.Single(item => item.Point.WorkPointId == main.Id).Point.Id;
            var coverageDepozit = c1.Points.Single(item => item.Point.WorkPointId == depozit.Id).Point.Id;

            // The templates of the two sources (same rules as the others: administrator only, one active per event); the form proposes a text.
            Check(dueSource.Category == "Mentenanță" && dueSource.EventName == "Intervenție de mentenanță" && expirySource.Category == "Mentenanță" && expirySource.EventName == "Expirare contract" &&
                  ExpiryTemplateRules.UnknownPlaceholders(dueSource.DefaultSubject + dueSource.DefaultBody, dueSource).Count == 0 &&
                  ExpiryTemplateRules.UnknownPlaceholders(expirySource.DefaultSubject + expirySource.DefaultBody, expirySource).Count == 0,
                "Both sources are in the category \"Mentenanță\" and their proposed texts use only their own placeholders");
            dueTemplate = await boss.CreateTemplateAsync(new NotificationTemplateInput { SourceKey = dueSource.Key, Subject = dueSource.DefaultSubject, Body = dueSource.DefaultBody, ThresholdDays = 30 });
            expiryTemplate = await boss.CreateTemplateAsync(new NotificationTemplateInput { SourceKey = expirySource.Key, Subject = expirySource.DefaultSubject, Body = expirySource.DefaultBody, ThresholdDays = 60 });

            // The evaluation needs no operator of beneficiaries (it runs for whoever opens the application first).
            await Session("ana", false).EvaluateAsync();
            var dueViews = (await boss.GetViewsAsync()).Where(item => item.Template.Id == dueTemplate.Id && item.ObjectLabel.Contains(owner.Name)).ToList();
            var dueMain = dueViews.SingleOrDefault(item => item.Notification.ObjectId == coverageMain);
            Check(dueViews.Count == 1 && dueMain is not null && dueMain.Notification.ExpiryDate == today.AddDays(10) && dueMain.ObjectLabel == $"{owner.Name} · {main.Name}" && dueMain.Url == $"/beneficiari/{owner.Id}" &&
                  dueMain.Subject == $"Scadență mentenanță – {owner.Name}, {main.Name}" && dueMain.Body.Contains(main.Address) && dueMain.Body.Contains("26/23.09.2025") &&
                  dueMain.Body.Contains(StockMovementRules.DisplayDate(today.AddDays(10))) && dueMain.Body.Contains("Ultima intervenție de mentenanță: nicio intervenție"),
                "A point of an active contract inside the period gets a notification with the beneficiary, point, address, contract and last intervention; the one outside the period does not");
            var expiryViews = (await boss.GetViewsAsync()).Where(item => item.Template.Id == expiryTemplate.Id && item.ObjectLabel.Contains(owner.Name)).ToList();
            Check(expiryViews.Count == 1 && expiryViews[0].Notification.ObjectId == c1.Contract.Id && expiryViews[0].Notification.ExpiryDate == today.AddDays(20) && expiryViews[0].ObjectLabel == $"{owner.Name} · Contract 26/23.09.2025" &&
                  expiryViews[0].Body == $"Contractul de mentenanță 26/23.09.2025 al beneficiarului {owner.Name} expiră la data de {StockMovementRules.DisplayDate(today.AddDays(20))} (zile rămase: 20; zile de depășire: 0).",
                "An active contract with an expiry date inside the period gets a notification with the beneficiary, the contract and the date");
            Check(await boss.GetThresholdDaysAsync(dueSource.Key, 30) == 30 && await Session("ana", false).GetThresholdDaysAsync(expirySource.Key, 30) == 60 && await boss.GetThresholdDaysAsync("fara.sablon." + suffix, 17) == 17,
                "The \"soon\" threshold of the pages is the one of the active template of the source (any user reads it), or the default when there is none");

            // On-demand interventions neither produce nor close a notification; a maintenance one moves the due date and closes the old notification.
            await interventions.RecordAsync(owner.Id, new ServiceInterventionInput { Kind = ServiceInterventionKind.OnDemand, WorkPointId = main.Id, PerformedOn = new DateOnly(2026, 9, 20), Notes = "La cerere" });
            await boss.EvaluateAsync();
            Check((await Mine(dueSource.Key, coverageMain)).Single().IsResolved == false && (await Mine(dueSource.Key)).Count(item => item.ObjectId == coverageMain) == 1,
                "An on-demand intervention does not close the notification");
            var visit = await interventions.RecordAsync(owner.Id, new ServiceInterventionInput { Kind = ServiceInterventionKind.Maintenance, WorkPointId = main.Id, PerformedOn = new DateOnly(2026, 9, 20),
                Basis = ServiceNextDueBasis.Chosen, ChosenDue = today.AddDays(200) });
            await boss.EvaluateAsync();
            var closed = (await Mine(dueSource.Key, coverageMain)).Single(item => item.ExpiryDate == today.AddDays(10));
            Check(closed.IsResolved && closed.ResolvedAutomatically && closed.ResolvedBy == "sistem" &&
                  closed.ResolvedReason == $"Scadența intervenției de mentenanță s-a modificat de la {StockMovementRules.DisplayDate(today.AddDays(10))} la {StockMovementRules.DisplayDate(today.AddDays(200))} (ultima intervenție de mentenanță: 20.09.2026)." &&
                  (await Mine(dueSource.Key, coverageMain)).Count == 1,
                "A maintenance intervention moves the due date: the old notification is resolved by the system with the old and new dates and the date of the intervention, and no new one appears outside the period");
            Check((await audit.GetEventsAsync()).Any(item => item.TimestampUtc > since && item.Action == AuditActions.AutoResolveNotification && item.ActorUsername == "sistem" && item.EntityId == closed.Id.ToString()),
                "The journal names \"Rezolvare automată notificare\"");

            // Deleting that intervention brings the old due date back: the automatically resolved notification is reopened.
            await interventions.DeleteAsync(visit.Intervention, "Ext curatare");
            await boss.EvaluateAsync();
            var reopened = (await Mine(dueSource.Key, coverageMain)).Single();
            Check(!reopened.IsResolved && reopened.Id == closed.Id && (await audit.GetEventsAsync()).Any(item => item.TimestampUtc > since && item.Action == AuditActions.ReopenNotification && item.EntityId == closed.Id.ToString()),
                "When the due date comes back, the notification that the system had closed is reopened");

            // The expiry date of the contract: extended (closes), brought back (reopens).
            var expiryOld = (await Mine(expirySource.Key, c1.Contract.Id)).Single();
            var extended = ServiceContractInput.From(await ContractOf(c1.Contract.Id)); extended.ValidUntil = today.AddDays(400);
            await contracts.UpdateAsync((await ContractOf(c1.Contract.Id)).Contract, extended);
            await boss.EvaluateAsync();
            var expiryClosed = (await Mine(expirySource.Key, c1.Contract.Id)).Single(item => item.Id == expiryOld.Id);
            Check(expiryClosed.IsResolved && expiryClosed.ResolvedAutomatically && expiryClosed.ResolvedReason == $"Data expirării contractului s-a modificat de la {StockMovementRules.DisplayDate(today.AddDays(20))} la {StockMovementRules.DisplayDate(today.AddDays(400))}.",
                "Extending the contract closes the expiry notification with the old and new dates");
            var back = ServiceContractInput.From(await ContractOf(c1.Contract.Id)); back.ValidUntil = today.AddDays(20);
            await contracts.UpdateAsync((await ContractOf(c1.Contract.Id)).Contract, back);
            await boss.EvaluateAsync();
            Check(!(await Mine(expirySource.Key, c1.Contract.Id)).Single(item => item.Id == expiryOld.Id).IsResolved, "Bringing the expiry date back reopens the notification");

            // A point rescheduled into the period gets its notification; the contract switched Off closes both kinds.
            var rescheduled = ServiceContractInput.From(await ContractOf(c1.Contract.Id)); rescheduled.Points.Single(item => item.WorkPointId == depozit.Id).NextDue = today.AddDays(7);
            await contracts.UpdateAsync((await ContractOf(c1.Contract.Id)).Contract, rescheduled);
            await boss.EvaluateAsync();
            Check((await Mine(dueSource.Key, coverageDepozit)).Single(item => item.ExpiryDate == today.AddDays(7)) is { IsResolved: false }, "A point rescheduled inside the period gets a notification");
            await contracts.DeactivateAsync((await ContractOf(c1.Contract.Id)).Contract);
            await boss.EvaluateAsync();
            var offDue = (await Mine(dueSource.Key, coverageDepozit)).Single(item => item.ExpiryDate == today.AddDays(7));
            var offExpiry = (await Mine(expirySource.Key, c1.Contract.Id)).Single(item => item.Id == expiryOld.Id);
            Check(offDue.IsResolved && offDue.ResolvedAutomatically && offDue.ResolvedReason == dueSource.RemovedReason && (await Mine(dueSource.Key, coverageMain)).Single().IsResolved &&
                  offExpiry.IsResolved && offExpiry.ResolvedAutomatically && offExpiry.ResolvedReason == expirySource.RemovedReason,
                "Switching the contract Off closes its notifications (the points' and the expiry's) with the reason written by the source");

            // The threshold follows the template: edited it changes, the source gives the default again once the template is gone.
            var edited = NotificationTemplateInput.From(dueTemplate); edited.ThresholdDays = 45;
            dueTemplate = await boss.UpdateTemplateAsync(dueTemplate, edited);
            Check(await boss.GetThresholdDaysAsync(dueSource.Key, 30) == 45, "Editing the template changes the threshold the pages use");
            async Task<ServiceContractDetails> ContractOf(int contractId) => (await contracts.GetForBeneficiaryAsync(owner.Id)).Single(item => item.Contract.Id == contractId);
        }
        finally
        {
            foreach (var template in new[] { dueTemplate, expiryTemplate })
                if (template is not null)
                    try { await boss.DeleteTemplateAsync((await repository.GetTemplatesAsync()).Single(item => item.Id == template.Id), "Ext curatare"); } catch (InvalidOperationException) { }
            foreach (var intervention in await interventions.GetForBeneficiaryAsync(owner.Id)) await interventions.DeleteAsync(intervention, "Ext curatare");
            foreach (var details in await contracts.GetForBeneficiaryAsync(owner.Id)) await contracts.DeleteAsync(details.Contract, "Ext curatare");
            if ((await beneficiaries.GetBeneficiariesAsync()).FirstOrDefault(current => current.Id == owner.Id) is { } live) await beneficiaries.DeleteAsync(live, "Ext curatare");
        }
    }
}
