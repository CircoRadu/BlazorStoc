using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using BlazorStoc.Services;

namespace BlazorStoc.Checks;

// Task 2 (after the SQLite removal): the scenarios that used to run against the local SQLite database, rebuilt on the
// isolated blazorstoc_test MariaDB database. Every section creates its own uniquely named data, removes it through the
// repositories (so the archive path is exercised too) and reports its own failure without stopping the others.
public static class MariaExtendedChecks
{
    private static readonly DateOnly? Expiry = new DateOnly(2027, 3, 15);
    private static int failures;

    public static async Task RunAsync(IConfiguration baseConfiguration)
    {
        Console.WriteLine("=== Task 2: extended MariaDB checks (blazorstoc_test) ===");
        var assets = Path.Combine(Path.GetTempPath(), "blazorstoc-ext-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(assets);
        var configuration = new ConfigurationBuilder().AddConfiguration(baseConfiguration)
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Database:MariaAssetsRoot"] = assets }).Build();
        var admin = new TestAccessControl(true, "integration.tester");
        var audit = new MariaAuditTrail(configuration);
        await using var probe = await OpenRawAsync(configuration);
        try
        {
            await Section("Products: concurrency, stock, groups", () => ProductsAsync(configuration, admin, audit, probe));
            await Section("Beneficiaries: duplicates, reasons, archive", () => BeneficiariesAsync(configuration, admin, audit, probe));
            await Section("Users: reasons, password change, archive", () => UsersAsync(configuration, admin, audit, probe));
            await Section("Projects: concurrency, versions, files", () => ProjectsAsync(configuration, admin, audit, probe, assets));
            await Section("Stock movements: dates, concurrency, history, vehicles", () => MovementsAsync(configuration, admin, audit, probe));
            await Section("Vehicles: concurrency, expiry journal, archive", () => VehiclesAsync(configuration, admin, audit, probe));
            await Section("Product locks: contention, takeover, forced release", () => LocksAsync(configuration, admin, audit, probe));
            await Section("Work points: main point, description, coordinates, photos, archive, backfill", () => WorkPointsAsync(configuration, admin, audit, probe, assets));
            await Section("Maintenance contracts: coverage, one active contract per point, On/Off, moves, archive", () => ServiceContractsAsync(configuration, admin, audit, probe));
            await Section("Change events", () => ChangeEventsAsync(configuration, admin, audit));
            await Section("Expiry notifications: templates, engine, take over, reminder", () => NotificationsAsync(configuration, admin, audit, probe));
            await Section("Notification settings: clean-up of old resolved notifications", () => NotificationSettingsAsync(configuration, admin, audit, probe));
        }
        finally
        {
            try { Directory.Delete(assets, true); } catch (IOException) { }
        }
        if (failures > 0) throw new Exception($"{failures} extended MariaDB section(s) failed.");
        Console.WriteLine("=== Extended MariaDB checks: all sections passed. ===");
    }

    private static async Task Section(string name, Func<Task> body)
    {
        Console.WriteLine("--- " + name + " ---");
        try { await body(); }
        catch (Exception exception)
        {
            failures++;
            Console.WriteLine($"FAIL [{name}]: {exception.GetType().Name}: {exception.Message}");
            Console.WriteLine(exception.StackTrace);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("Check failed: " + message);
        Console.WriteLine("PASS: " + message);
    }

    private static async Task<T?> Rejects<T>(Func<Task> operation, string message) where T : Exception
    {
        try { await operation(); }
        catch (T exception) { Console.WriteLine("PASS: " + message); return exception; }
        throw new Exception("Check failed (not rejected): " + message);
    }

    private static string Suffix() => Guid.NewGuid().ToString("N")[..8];

    private static string Letters() => new(Enumerable.Range(0, 3).Select(_ => (char)('A' + Random.Shared.Next(26))).ToArray());

    // ---- Products ------------------------------------------------------------------------------------------------

    private static async Task ProductsAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe)
    {
        var suffix = Suffix();
        var category = $"Ext Categorie {suffix}";
        var subcategory = $"Ext Subcategorie {suffix}";
        var products = new MariaProductRepository(configuration, admin, audit);
        var movements = new MariaStockMovementRepository(configuration, admin, audit);
        await products.CreateCategoryAsync(category);
        await products.CreateSubcategoryAsync(category, subcategory);
        var movementIds = new List<int>();
        Product? main = null;
        try
        {
            main = await products.CreateAsync(new ProductInput { Name = $"Ext Produs {suffix}", Category = category, Subcategory = subcategory });

            // Two sessions save the same normalized product code: exactly one wins.
            var sameName = $"Ext Cod Dublu {suffix}";
            var attempts = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
            {
                try { return await new MariaProductRepository(configuration, admin, audit).CreateAsync(new ProductInput { Name = sameName, Category = category, Subcategory = subcategory }); }
                catch (ProductOperationException) { return null; }
            }));
            Check(attempts.Count(item => item is not null) == 1, "Two concurrent sessions cannot save the same normalized product code");
            var winner = attempts.Single(item => item is not null)!;

            // Two sessions delete the same snapshot: exactly one wins.
            var deletions = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
            {
                try { await new MariaProductRepository(configuration, admin, audit).DeleteAsync(winner, "Ext stergere concurenta"); return true; }
                catch (ProductOperationException) { return false; }
            }));
            Check(deletions.Count(item => item) == 1, "Two concurrent deletions of the same product: exactly one succeeds");
            Check(await products.GetProductAsync(winner.Id) is null, "The concurrently deleted product is gone");

            var noReason = ProductInput.From(main); noReason.Description = "Fara motiv";
            await Rejects<ProductOperationException>(() => products.UpdateAsync(main, noReason), "A product edit without a reason is rejected");
            var auditBefore = (await audit.GetEventsAsync()).Count;
            Check((await audit.GetEventsAsync()).Count == auditBefore, "A rejected edit writes nothing to the journal");

            // Stock changed after the form was opened is preserved by an edit and blocks the deletion.
            var entry = await movements.CreateAsync(main.Id, new StockMovementInput { Kind = StockMovementKind.Entry, Date = DateOnly.FromDateTime(DateTime.Now), Quantity = 7, Description = "Ext intrare" });
            movementIds.Add(entry.Movement.Id);
            var edit = ProductInput.From(main); edit.Description = "Descriere noua"; edit.Reason = "Ext editare";
            var edited = await new MariaProductRepository(configuration, admin, audit).UpdateAsync(await products.GetProductAsync(main.Id) ?? main, edit);
            Check(edited.Quantity == 7, "A product edit does not overwrite a stock changed by a movement");
            await Rejects<ProductOperationException>(() => products.DeleteAsync(edited with { Quantity = 0 }, "Ext"), "Deletion is blocked by the current stock, not by a stale snapshot");

            // Empty groups survive the deletion of their last product; renaming updates the products.
            var groupOnly = await products.CreateAsync(new ProductInput { Name = $"Ext Ultimul {suffix}", Category = category, Subcategory = subcategory });
            await products.DeleteAsync(groupOnly, "Ext ultimul produs");
            Check((await products.GetGroupsAsync()).Contains(new ProductGroup(category, subcategory)), "The catalogue group is kept after deleting a product");
            var renamed = $"Ext Categorie Noua {suffix}";
            await products.RenameCategoryAsync(category, renamed, "Ext redenumire");
            category = renamed;
            Check((await products.GetProductAsync(main.Id))?.Category == renamed, "Renaming a category updates the associated products");
            var neighbour = $"Ext Categorie Vecina {suffix}";
            await products.CreateCategoryAsync(neighbour);
            try
            {
                await Rejects<ProductOperationException>(() => products.RenameCategoryAsync(renamed, neighbour.ToUpperInvariant(), "Duplicat"), "Renaming a category onto another existing normalized name is rejected");
            }
            finally { await ExecuteAsync(probe, "DELETE FROM categories WHERE name=@n", ("@n", neighbour)); }
            var archived = await ScalarLongAsync(probe, "SELECT COUNT(*) FROM archive_products WHERE original_id=@id", ("@id", winner.Id));
            Check(archived == 1, "The deleted product is archived exactly once");
        }
        finally
        {
            foreach (var id in movementIds)
                if (await movements.GetAsync(id) is { } stored) await movements.DeleteAsync(stored, "Ext curatare");
            if (main is not null && await products.GetProductAsync(main.Id) is { } current) await products.DeleteAsync(current, "Ext curatare");
            await ExecuteAsync(probe, "DELETE FROM subcategories WHERE name=@n", ("@n", subcategory));
            await ExecuteAsync(probe, "DELETE FROM categories WHERE name IN (@a,@b)", ("@a", $"Ext Categorie {suffix}"), ("@b", category));
        }
    }

    // ---- Beneficiaries ---------------------------------------------------------------------------------------------

    private static BeneficiaryInput Legal(string name, string cui) => new() { Name = name, Cui = cui, Address = "Strada Test 1, Bucuresti", Phone = "0721000111" };

    private static async Task BeneficiariesAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe)
    {
        var suffix = Suffix();
        var cuiA = "RO" + Random.Shared.Next(70000000, 79999999);
        var cuiB = "RO" + Random.Shared.Next(80000000, 89999999);
        var beneficiaries = new MariaBeneficiaryRepository(configuration, admin, audit);
        var projects = new MariaProjectRepository(configuration, new TestWebHostEnvironment(Path.GetTempPath()), admin, audit);
        var a = await beneficiaries.CreateAsync(Legal($"Ext Beneficiar A {suffix} SRL", cuiA));
        var b = await beneficiaries.CreateAsync(Legal($"Ext Beneficiar B {suffix} SRL", cuiB));
        Project? project = null;
        try
        {
            var duplicate = await Rejects<BeneficiaryOperationException>(() => beneficiaries.CreateAsync(Legal("Alt Nume SRL", cuiA)), "A duplicate CUI is rejected on creation");
            Check(duplicate!.Message.Contains(a.Name), "The duplicate CUI message names the existing beneficiary");

            var before = (await audit.GetEventsAsync()).Count;
            var toDuplicate = BeneficiaryInput.From(b); toDuplicate.Cui = cuiA; toDuplicate.Reason = "Ext";
            var onEdit = await Rejects<BeneficiaryOperationException>(() => beneficiaries.UpdateAsync(b, toDuplicate), "Editing onto an existing CUI is rejected");
            Check(onEdit!.Message.Contains(a.Name), "The edit rejection names the CUI owner");
            Check((await audit.GetEventsAsync()).Count == before, "A rejected beneficiary edit leaves the journal unchanged");
            var keepsOwn = BeneficiaryInput.From(b); keepsOwn.Reason = "Ext pastreaza CUI";
            b = await beneficiaries.UpdateAsync(b, keepsOwn);
            Check(b.Version == 1, "An edit that keeps its own CUI is accepted and increments the version");
            var noReason = BeneficiaryInput.From(b); noReason.Address = "Alta adresa 5";
            await Rejects<BeneficiaryOperationException>(() => beneficiaries.UpdateAsync(b, noReason), "A beneficiary edit without a reason is rejected");
            await Rejects<BeneficiaryOperationException>(() => beneficiaries.DeleteAsync(b, " "), "A beneficiary deletion without a reason is rejected");

            project = await projects.CreateAsync(new ProjectInput { BeneficiaryId = a.Id, Name = $"Ext Proiect Viu {suffix}" });
            await Rejects<BeneficiaryOperationException>(() => beneficiaries.DeleteAsync(a, "Ext"), "Deleting a beneficiary is blocked while a live project exists");
            await projects.DeleteAsync(project, "Ext curatare proiect"); project = null;
            await beneficiaries.DeleteAsync(a, "Ext stergere");
            Check(await ScalarLongAsync(probe, "SELECT COUNT(*) FROM archive_beneficiaries WHERE original_id=@id", ("@id", a.Id)) == 1, "The deleted beneficiary is archived exactly once");
            Check(!(await beneficiaries.GetBeneficiariesAsync()).Any(item => item.Id == a.Id), "The deleted beneficiary is gone from the live table");
        }
        finally
        {
            if (project is not null) await projects.DeleteAsync(project, "Ext curatare");
            foreach (var item in (await beneficiaries.GetBeneficiariesAsync()).Where(item => item.Id == a.Id || item.Id == b.Id))
                await beneficiaries.DeleteAsync(item, "Ext curatare");
        }
    }

    // ---- Users -------------------------------------------------------------------------------------------------------

    private static async Task UsersAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe)
    {
        var name = "ext." + Suffix();
        var users = new MariaUserRepository(configuration, admin, audit);
        var user = await users.CreateAsync(new WebUserInput { Username = name, DisplayName = "Ext Utilizator", Role = AccessRoles.LimitedUser, Password = "parola-ext-veche-123" });
        try
        {
            var noReason = WebUserInput.From(user); noReason.DisplayName = "Fara motiv";
            await Rejects<UserOperationException>(() => users.UpdateAsync(user, noReason), "A user edit without a reason is rejected");
            var edit = WebUserInput.From(user); edit.DisplayName = "Ext Editat"; edit.Password = "parola-ext-noua-456"; edit.Reason = "Ext schimbare parola";
            var edited = await users.UpdateAsync(user, edit);
            Check((await users.AuthenticateAsync(name, "parola-ext-noua-456")).Status == AuthenticationStatus.Success, "An edited user authenticates with the new password");
            Check((await users.AuthenticateAsync(name, "parola-ext-veche-123")).Status == AuthenticationStatus.InvalidCredentials, "The old password no longer works");
            await Rejects<UserOperationException>(() => users.UpdateAsync(user, WebUserInput.From(user)), "A stale user edit is rejected");
            user = edited;
            var inactive = WebUserInput.From(user); inactive.IsActive = false; inactive.Reason = "Ext dezactivare";
            user = await users.UpdateAsync(user, inactive);
            Check((await users.AuthenticateAsync(name, "parola-ext-noua-456")).Status == AuthenticationStatus.Inactive, "A deactivated user is reported as inactive");
            await users.DeleteAsync(user, "Ext stergere");
            Check(await ScalarLongAsync(probe, "SELECT COUNT(*) FROM archive_web_users WHERE original_id=@id", ("@id", user.Id)) == 1, "The deleted user is archived exactly once");
            user = null!;
        }
        finally
        {
            if (user is not null) await users.DeleteAsync((await users.GetUsersAsync()).First(item => item.Id == user.Id), "Ext curatare");
        }
    }

    // ---- Projects ------------------------------------------------------------------------------------------------------

    private static async Task ProjectsAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe, string assets)
    {
        var suffix = Suffix();
        var environment = new TestWebHostEnvironment(assets);
        var beneficiaries = new MariaBeneficiaryRepository(configuration, admin, audit);
        var one = await beneficiaries.CreateAsync(Legal($"Ext Proiect Ben 1 {suffix} SRL", "RO" + Random.Shared.Next(60000000, 69999999)));
        var two = await beneficiaries.CreateAsync(Legal($"Ext Proiect Ben 2 {suffix} SRL", "RO" + Random.Shared.Next(50000000, 59999999)));
        var projects = new MariaProjectRepository(configuration, environment, admin, audit);
        var files = new MariaProjectFileStore(environment, configuration, admin, null, audit);
        Project? project = null;
        try
        {
            var name = $"Ext Proiect {suffix}";
            var attempts = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
            {
                try { return await new MariaProjectRepository(configuration, environment, admin, audit).CreateAsync(new ProjectInput { BeneficiaryId = one.Id, Name = name }); }
                catch (ProjectOperationException) { return null; }
            }));
            Check(attempts.Count(item => item is not null) == 1, "Two concurrent sessions cannot create the same project name for one beneficiary");
            project = attempts.Single(item => item is not null)!;
            var duplicate = await Rejects<ProjectOperationException>(() => projects.CreateAsync(new ProjectInput { BeneficiaryId = one.Id, Name = name.ToUpperInvariant() }), "A duplicate project name is rejected");
            Check(duplicate!.Message.Contains(one.Name), "The duplicate project message names the beneficiary");
            var other = await projects.CreateAsync(new ProjectInput { BeneficiaryId = two.Id, Name = name });
            await projects.DeleteAsync(other, "Ext acelasi nume alt beneficiar");
            Check(true, "The same project name is allowed for a different beneficiary");

            var noReason = ProjectInput.From(project); noReason.Name = name + " v2";
            await Rejects<ProjectOperationException>(() => projects.UpdateAsync(project, noReason), "A project edit without a reason is rejected");
            var edit = ProjectInput.From(project); edit.Name = name + " v2"; edit.Reason = "Ext redenumire";
            var edited = await projects.UpdateAsync(project, edit);
            Check(edited.Version == project.Version + 1, "A project edit increments the version");
            await Rejects<ProjectOperationException>(() => projects.UpdateAsync(project, edit), "A stale project edit is rejected");
            var move = ProjectInput.From(edited); move.BeneficiaryId = two.Id; move.Reason = "Ext mutare";
            var auditBefore = (await audit.GetEventsAsync()).Count;
            await Rejects<ProjectOperationException>(() => projects.UpdateAsync(edited, move), "Moving a project to another beneficiary is rejected");
            Check((await audit.GetEventsAsync()).Count == auditBefore, "The rejected move leaves the journal unchanged");
            project = edited;

            var observation = await projects.CreateObservationAsync(project.Id, new ProjectObservationInput { Name = "Ext observatie", Content = "Continut" }, "ext.tester");
            var obsEdit = ProjectObservationInput.From(observation); obsEdit.Content = "Continut nou";
            await Rejects<ProjectOperationException>(() => projects.UpdateObservationAsync(observation, obsEdit), "An observation edit without a reason is rejected");
            obsEdit.Reason = "Ext observatie editata";
            var obsEdited = await projects.UpdateObservationAsync(observation, obsEdit);
            Check(obsEdited.Version == observation.Version + 1, "An observation edit increments the version");

            var bytes = "continut fisier ext"u8.ToArray();
            var file = await files.SaveAsync(obsEdited.Id, "plan.txt", "text/plain", bytes, "ext.tester");
            var content = await files.GetContentAsync(file.Id);
            Check(content is not null && content.Content.SequenceEqual(bytes), "An observation file is stored and read back byte for byte");
            Check(file.Sha256.Length == 64, "The stored file has a SHA-256 hash");
            await Rejects<ProjectOperationException>(() => files.SaveAsync(987654, "orfan.txt", "text/plain", "x"u8.ToArray(), "ext.tester"), "A file for a missing observation is rejected");
            await files.DeleteAsync(file.Id, "Ext stergere fisier");
            Check((await files.GetFilesAsync(obsEdited.Id)).Count == 0 && await files.GetContentAsync(file.Id) is null, "A deleted observation file is cleared from the live store");
            Check(await ScalarLongAsync(probe, "SELECT COUNT(*) FROM archive_project_observation_files WHERE original_id=@id", ("@id", file.Id)) == 1, "The deleted file is archived exactly once");

            await projects.DeleteObservationAsync(obsEdited, "Ext curatare observatie");
            await projects.DeleteAsync(project, "Ext stergere proiect"); project = null;
            Check(await ScalarLongAsync(probe, "SELECT COUNT(*) FROM archive_projects WHERE original_id=@id", ("@id", edited.Id)) == 1, "The deleted project is archived exactly once");
        }
        finally
        {
            if (project is not null) await projects.DeleteAsync((await projects.GetAsync(project.Id))!, "Ext curatare");
            foreach (var item in (await beneficiaries.GetBeneficiariesAsync()).Where(item => item.Id == one.Id || item.Id == two.Id))
                await beneficiaries.DeleteAsync(item, "Ext curatare");
        }
    }

    // ---- Stock movements & vehicles ----------------------------------------------------------------------------------------

    private static async Task MovementsAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe)
    {
        var suffix = Suffix();
        var category = $"Ext Miscari Cat {suffix}";
        var subcategory = $"Ext Miscari Sub {suffix}";
        var products = new MariaProductRepository(configuration, admin, audit);
        var vehicles = new MariaVehicleRepository(configuration, admin, audit);
        var movements = new MariaStockMovementRepository(configuration, admin, audit);
        await products.CreateCategoryAsync(category);
        await products.CreateSubcategoryAsync(category, subcategory);
        var product = await products.CreateAsync(new ProductInput { Name = $"Ext Miscari {suffix}", Category = category, Subcategory = subcategory });
        var carOne = await vehicles.CreateAsync(new VehicleInput { PlateNumber = "TS-91-" + Letters(), Description = "Ext masina 1", ItpExpiry = Expiry, InsuranceExpiry = Expiry, RovinietaExpiry = Expiry });
        var carTwo = await vehicles.CreateAsync(new VehicleInput { PlateNumber = "TS-92-" + Letters(), Description = "Ext masina 2", ItpExpiry = Expiry, InsuranceExpiry = Expiry, RovinietaExpiry = Expiry });
        var today = DateOnly.FromDateTime(DateTime.Now);
        try
        {
            await Rejects<StockMovementOperationException>(() => movements.CreateAsync(product.Id, new StockMovementInput
            { Kind = StockMovementKind.Entry, Date = today.AddDays(30), Quantity = 1, Description = "Ext viitor" }), "A movement dated in the future is rejected");
            Check((await movements.GetPageAsync(product.Id, new StockMovementQuery())).Stock == 0, "The rejected movement leaves the stock unchanged");

            // Parallel entries from independent sessions all count.
            await Task.WhenAll(Enumerable.Range(0, 8).Select(index => new MariaStockMovementRepository(configuration, admin, audit)
                .CreateAsync(product.Id, new StockMovementInput { Kind = StockMovementKind.Entry, Date = today, Quantity = 1, Description = $"Ext paralel {index}" })));
            var page = await movements.GetPageAsync(product.Id, new StockMovementQuery(PageSize: 50));
            Check(page.Stock == 8 && page.TotalCount == 8, "Eight parallel entries produce stock 8 and eight movements");

            var toCar = await movements.CreateAsync(product.Id, new StockMovementInput
            { Kind = StockMovementKind.Exit, Date = today, Quantity = 6, Destination = ExitDestination.Vehicle, VehicleId = carOne.Id, Description = "Ext spre masina" });
            var edit = StockMovementInput.From(toCar.Movement); edit.Description = "Ext spre masina corectat"; edit.Reason = "Ext corectie";
            var edited = await movements.UpdateAsync(toCar.Movement, edit);
            Check((await movements.GetHistoryAsync(edited.Movement.Id)).Count >= 1, "Editing a movement records its history");

            var move = await movements.TransferFromVehicleAsync(new VehicleTransfer(carOne.Id, carTwo.Id, [new VehicleTransferLine(product.Id, 4)]));
            Check(move.Count == 1, "A vehicle to vehicle transfer creates one movement per product");
            var stocks = await movements.GetVehicleStocksAsync(product.Id);
            Check(stocks.Single(item => item.VehicleId == carOne.Id).Quantity == 2 && stocks.Single(item => item.VehicleId == carTwo.Id).Quantity == 4,
                "After the transfer the vehicles hold 2 and 4 pieces");
            Check((await movements.GetVehicleEquipmentAsync(carTwo.Id)).Single().Quantity == 4, "The vehicle page equipment reflects the transfer");

            // Two sessions try to return more than the vehicle holds: never both.
            var returns = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
            {
                try { await new MariaStockMovementRepository(configuration, admin, audit).TransferFromVehicleAsync(new VehicleTransfer(carTwo.Id, null, [new VehicleTransferLine(product.Id, 3)])); return true; }
                catch (StockMovementOperationException) { return false; }
            }));
            Check(returns.Count(item => item) == 1, "Two concurrent returns of 3 from a vehicle holding 4: exactly one succeeds");
            Check((await movements.GetVehicleStocksAsync(product.Id)).Single(item => item.VehicleId == carTwo.Id).Quantity == 1, "The vehicle keeps the remaining piece");

            await Rejects<VehicleOperationException>(() => vehicles.DeleteAsync(carOne, "Ext"), "A vehicle used by movements cannot be deleted");
            var count = (await movements.GetMovementCountsByVehicleAsync())[carOne.Id];
            Check(count >= 1, "The movement counter per vehicle sees the vehicle's movements");

            await DeleteMovementsNewestFirstAsync(movements, product.Id);
            Check(await ScalarLongAsync(probe, "SELECT COUNT(*) FROM archive_stock_movements WHERE original_id=@id", ("@id", edited.Movement.Id)) == 1, "A deleted movement is archived exactly once");
        }
        finally
        {
            await DeleteMovementsNewestFirstAsync(movements, product.Id);
            await vehicles.DeleteAsync(carOne, "Ext curatare");
            await vehicles.DeleteAsync(carTwo, "Ext curatare");
            await products.DeleteAsync((await products.GetProductAsync(product.Id))!, "Ext curatare");
            await ExecuteAsync(probe, "DELETE FROM subcategories WHERE name=@n", ("@n", subcategory));
            await ExecuteAsync(probe, "DELETE FROM categories WHERE name=@n", ("@n", category));
        }
    }

    // Undoing movements newest first keeps every vehicle's stock non-negative at each step.
    private static async Task DeleteMovementsNewestFirstAsync(IStockMovementRepository movements, int productId)
    {
        var items = (await movements.GetPageAsync(productId, new StockMovementQuery(PageSize: 500))).Items.OrderByDescending(item => item.Id).ToList();
        foreach (var item in items) await movements.DeleteAsync(item, "Ext curatare");
    }

    private static async Task VehiclesAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe)
    {
        var plate = "TS-93-" + Letters();
        var vehicles = new MariaVehicleRepository(configuration, admin, audit);
        var attempts = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            try { return await new MariaVehicleRepository(configuration, admin, audit).CreateAsync(new VehicleInput { PlateNumber = plate, Description = "Ext", ItpExpiry = Expiry, InsuranceExpiry = Expiry, RovinietaExpiry = Expiry }); }
            catch (VehicleOperationException) { return null; }
        }));
        Check(attempts.Count(item => item is not null) == 1, "Two concurrent sessions cannot save the same registration number");
        var vehicle = attempts.Single(item => item is not null)!;
        try
        {
            var edit = VehicleInput.From(vehicle);
            VehicleRules.SetExpiry(edit, VehicleExpiryKind.Itp, new DateOnly(2028, 1, 10));
            edit.Reason = "Modificare data expirare ITP";
            var edited = await vehicles.UpdateAsync(vehicle, edit, default, AuditActions.ExpiryItp);
            Check(edited.ItpExpiry == new DateOnly(2028, 1, 10) && edited.Version == vehicle.Version + 1, "The ITP expiry is changed and the version increments");
            var events = (await audit.GetEventsAsync()).Where(item => item.EntityType == AuditEntities.Vehicle && item.Action == AuditActions.ExpiryItp).ToList();
            Check(events.Count >= 1, "The journal names the exact operation \"Modificare expirare ITP\"");
            await Rejects<VehicleOperationException>(() => vehicles.UpdateAsync(vehicle, edit), "A stale vehicle edit is rejected");
            vehicle = edited;
        }
        finally
        {
            await vehicles.DeleteAsync(vehicle, "Ext curatare");
        }
        Check(await ScalarLongAsync(probe, "SELECT COUNT(*) FROM archive_vehicles WHERE original_id=@id", ("@id", vehicle.Id)) == 1, "The deleted vehicle is archived exactly once");
    }

    // ---- Product locks ---------------------------------------------------------------------------------------------------------

    private static async Task LocksAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe)
    {
        var suffix = Suffix();
        var category = $"Ext Blocari Cat {suffix}";
        var subcategory = $"Ext Blocari Sub {suffix}";
        var products = new MariaProductRepository(configuration, admin, audit);
        await products.CreateCategoryAsync(category);
        await products.CreateSubcategoryAsync(category, subcategory);
        var product = await products.CreateAsync(new ProductInput { Name = $"Ext Blocari {suffix}", Category = category, Subcategory = subcategory });
        var ana = new MariaProductLockRepository(configuration, new TestAccessControl(false, "ana"), audit);
        var bob = new MariaProductLockRepository(configuration, new TestAccessControl(false, "bob"), audit);
        var boss = new MariaProductLockRepository(configuration, admin, audit);
        try
        {
            var first = await ana.AcquireAsync(product.Id, "sesiune-ana");
            Check(first is { Acquired: true, Changed: true }, "The first session takes the lock");
            var second = await bob.AcquireAsync(product.Id, "sesiune-bob");
            Check(second is { Acquired: false } && second.Lock?.Owner == "ana", "A second session is refused and told who holds the lock");
            Check((await bob.RenewAsync(product.Id, "sesiune-bob")).Acquired == false, "A session that does not hold the lock cannot renew it");
            Check((await ana.GetActiveAsync()).Any(item => item.ProductId == product.Id), "The active locks list contains the lock");

            await Rejects<Exception>(() => ana.ForceReleaseAsync(product.Id, "Ext"), "A non-administrator cannot force-release a lock");
            await Rejects<Exception>(() => boss.ForceReleaseAsync(product.Id, " "), "A forced release requires a reason");
            var removed = await boss.ForceReleaseAsync(product.Id, "Ext deblocare fortata");
            Check(removed?.Owner == "ana", "The administrator force-releases the lock and gets the removed lock back");
            Check((await audit.GetEventsAsync()).Any(item => item.Action == AuditActions.Unlock && item.EntityId == product.Id.ToString()), "The forced release is journaled as \"Deblocare\"");
            Check((await ana.RenewAsync(product.Id, "sesiune-ana")).Acquired == false, "A lock released by an administrator is never silently re-acquired by the old holder");

            await ana.AcquireAsync(product.Id, "sesiune-ana");
            await ExecuteAsync(probe, "UPDATE product_locks SET expires_utc=@past WHERE product_id=@p", ("@past", MariaTimeTextForTest(DateTime.UtcNow.AddSeconds(-5))), ("@p", product.Id));
            var takeover = await bob.AcquireAsync(product.Id, "sesiune-bob");
            Check(takeover.Acquired && takeover.Lock?.Owner == "bob", "An expired lock is taken over by another session");

            await products.DeleteAsync((await products.GetProductAsync(product.Id))!, "Ext curatare");
            Check(await ScalarLongAsync(probe, "SELECT COUNT(*) FROM product_locks WHERE product_id=@p", ("@p", product.Id)) == 0, "Deleting the product removes its lock");
        }
        finally
        {
            if (await products.GetProductAsync(product.Id) is { } left) await products.DeleteAsync(left, "Ext curatare");
            await ExecuteAsync(probe, "DELETE FROM subcategories WHERE name=@n", ("@n", subcategory));
            await ExecuteAsync(probe, "DELETE FROM categories WHERE name=@n", ("@n", category));
        }
    }

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

    // ---- Change events ---------------------------------------------------------------------------------------------------------------

    private static async Task ChangeEventsAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit)
    {
        var suffix = Suffix();
        var source = new MariaChangeEventSource(configuration);
        await source.EnsureAsync(default);
        var start = await source.LatestIdAsync(default);
        var category = $"Ext Evenimente Cat {suffix}";
        var subcategory = $"Ext Evenimente Sub {suffix}";
        var products = new MariaProductRepository(configuration, admin, audit);
        await products.CreateCategoryAsync(category);
        await products.CreateSubcategoryAsync(category, subcategory);
        var product = await products.CreateAsync(new ProductInput { Name = $"Ext Evenimente {suffix}", Category = category, Subcategory = subcategory });
        var edit = ProductInput.From(product); edit.Description = "schimbat"; edit.Reason = "Ext";
        product = await products.UpdateAsync(product, edit);
        await products.DeleteAsync(product, "Ext curatare");
        await using (var raw = await OpenRawAsync(configuration))
        {
            await ExecuteAsync(raw, "DELETE FROM subcategories WHERE name=@n", ("@n", subcategory));
            await ExecuteAsync(raw, "DELETE FROM categories WHERE name=@n", ("@n", category));
        }
        var recorded = await source.ReadAfterAsync(start, 1000, default);
        var mine = recorded.Where(item => item.EntityType == AuditEntities.Product && item.EntityId == product.Id.ToString()).Select(item => item.Action).ToList();
        Check(mine.Contains(AuditActions.Create) && mine.Contains(AuditActions.Edit) && mine.Contains(AuditActions.Delete), "Create, edit and delete of a product are recorded as change events");
        Check(recorded.Zip(recorded.Skip(1)).All(pair => pair.First.Id < pair.Second.Id) && recorded.All(item => item.CreatedUtc.Kind == DateTimeKind.Utc), "Events are ordered by identifier and timestamped in UTC");
        Check(recorded.All(item => item.EntityId.All(char.IsDigit)), "Events carry only identifiers, never names or texts");
        var latest = recorded[^1].Id;
        await source.PurgeAsync(latest, DateTime.UtcNow.AddDays(-1), default);
        Check((await source.ReadAfterAsync(start, 1000, default)).Count == recorded.Count, "Purging keeps events newer than the retention period");
        await source.PurgeAsync(latest, DateTime.UtcNow.AddMinutes(1), default);
        Check((await source.ReadAfterAsync(start, 1000, default)).Count == 0, "Purging removes processed events older than the cutoff");
    }


    // ---- Expiry notifications -----------------------------------------------------------------------------------------------------

    private static async Task NotificationsAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe)
    {
        var suffix = Suffix();
        var clock = new ManualTimeProvider();
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var repository = new MariaExpiryNotificationRepository(configuration);
        var source = new TestExpirySource("test.ext." + suffix, []);
        var brokenSource = new TestExpirySource("test.broken." + suffix, []);
        ExpiryNotificationService Session(string user, bool administrator = false, params IExpirySource[] sources) =>
            new(repository, sources.Length > 0 ? sources : [source, brokenSource], administrator ? admin : new TestAccessControl(false, user), audit, clock);
        var boss = Session("integration.tester", true);
        var objectA = Random.Shared.Next(1000, 9999999);
        var objectB = objectA + 1;
        var objectC = objectA + 2;
        ExpiryInstance Make(int id, int daysLeft) => new(id, $"Obiect {id}", today.AddDays(daysLeft), new Dictionary<string, string> { ["obiect"] = $"Obiect {id}" }, $"/obiect/{id}");
        async Task<IReadOnlyList<ExpiryNotification>> Rows(NotificationTemplate of) => (await repository.GetNotificationsAsync()).Where(item => item.TemplateId == of.Id).ToList();
        NotificationTemplate? template = null, brokenTemplate = null;
        try
        {
            // Only the administrator manages templates; invalid ones are rejected; the journal names each operation.
            await Rejects<AccessDeniedException>(() => Session("ana").CreateTemplateAsync(new NotificationTemplateInput { SourceKey = source.Key, Subject = "S", Body = "T", ThresholdDays = 30 }), "A non-administrator cannot create a template");
            await Rejects<NotificationOperationException>(() => boss.CreateTemplateAsync(new NotificationTemplateInput { SourceKey = source.Key, Subject = "S <gresit>", Body = "T", ThresholdDays = 30 }), "A template with an unknown placeholder is rejected");
            template = await boss.CreateTemplateAsync(new NotificationTemplateInput { SourceKey = source.Key, Subject = "Expira <obiect>", Body = "<eveniment>: <obiect> la <data expirare>, mai sunt <zile ramase> zile, depasit cu <zile depasire> zile.", ThresholdDays = 30 });
            brokenTemplate = await boss.CreateTemplateAsync(new NotificationTemplateInput { SourceKey = brokenSource.Key, Subject = "Rupt", Body = "Rupt", ThresholdDays = 30 });
            Check((await audit.GetEventsAsync()).Any(item => item.Action == AuditActions.CreateNotificationTemplate && item.EntityId == template.Id.ToString()), "The journal names the operation \"Adăugare șablon notificare\"");
            await Rejects<AccessDeniedException>(() => Session("ana").GetTemplatesAsync(), "A non-administrator cannot list the templates");

            // One active template per event: a second active one is refused (service and database), a switched-off one is allowed.
            await Rejects<NotificationOperationException>(() => boss.CreateTemplateAsync(new NotificationTemplateInput { SourceKey = source.Key, Subject = "Al doilea", Body = "T", ThresholdDays = 7 }), "A second active template for the same event is refused");
            var spare = await boss.CreateTemplateAsync(new NotificationTemplateInput { SourceKey = source.Key, Subject = "Rezerva", Body = "T", ThresholdDays = 7, Active = false });
            var activate = NotificationTemplateInput.From(spare); activate.Active = true;
            await Rejects<NotificationOperationException>(() => boss.UpdateTemplateAsync(spare, activate), "Switching a second template on while another is active is refused");
            await Rejects<NotificationOperationException>(() => repository.CreateTemplateAsync(new NotificationTemplateInput { SourceKey = source.Key, Subject = "Direct", Body = "T", ThresholdDays = 3 }), "The database itself refuses two active templates for one event");
            await boss.DeleteTemplateAsync(spare, "Ext curatare rezerva");

            // The engine: every object inside the period gets a notification - also after the date, there is no lower limit -
            // once per event; the overdue ones come first.
            source.Instances.AddRange([Make(objectA, 20), Make(objectB, 40), Make(objectC, -3)]);
            brokenSource.Instances.Add(Make(objectA, 10));
            await boss.EvaluateAsync();
            await boss.EvaluateAsync();
            var views = (await boss.GetViewsAsync()).Where(item => item.Template.Id == template.Id).ToList();
            Check(views.Count == 2 && views.Any(item => item.Notification.ObjectId == objectA) && views.Any(item => item.Notification.ObjectId == objectC) && views.All(item => item.Notification.ObjectId != objectB),
                "The objects within 30 days, the overdue one included, get a notification each, once; the one outside the period does not");
            Check(views[0].Notification.ObjectId == objectC && views[0].IsOverdue && views[0].DaysLeft == -3 && !views[1].IsOverdue, "The overdue notification is listed first");
            Check(views[0].Body.Contains("mai sunt 0 zile, depasit cu 3 zile") && views[1].Body.Contains("mai sunt 20 zile, depasit cu 0 zile") && views[0].Url == $"/obiect/{objectC}",
                "The text never prints negative days, reports the days overdue, and the notification carries the link of its object");
            Check(views[0].MaxSnoozeDays == 30 && views[1].MaxSnoozeDays == 18, "An overdue notification may be postponed by up to 30 days, an upcoming one by the days left minus two");
            var view = views.Single(item => item.Notification.ObjectId == objectA);
            var overdueView = views.Single(item => item.Notification.ObjectId == objectC);
            var createdEntries = (await audit.GetEventsAsync()).Where(item => item.Action == AuditActions.NotificationCreated && item.EntityId == view.Notification.Id.ToString()).ToList();
            Check(createdEntries.Count == 1 && createdEntries[0].ActorUsername == "sistem" && createdEntries[0].EntityType == AuditEntities.Notification &&
                  createdEntries[0].Target.Contains($"Obiect {objectA}") && createdEntries[0].Details.Contains(StockMovementRules.DisplayDate(today.AddDays(20))),
                "The system journals the creation of a notification once (\"Notificare creată\", actor \"sistem\"), with the object and the expiry date");
            Check(!(await audit.GetEventsAsync()).Any(item => item.Action == AuditActions.NotificationCreated && item.Target.Contains($"Obiect {objectB}")),
                "No creation is journaled for an object outside the period");
            Check(view.Subject == $"Expira Obiect {objectA}" && view.Body.Contains($"Eveniment test: Obiect {objectA} la {StockMovementRules.DisplayDate(today.AddDays(20))}"),
                "The subject and text are produced from the template with the values of the object");
            Check(view.IsAlert && view.DaysLeft == 20, "A new notification warns");
            Check(await boss.AlertCountAsync() >= 2, "The alert count includes the new notifications");

            // An unreadable source keeps its notifications instead of closing them.
            brokenSource.Fail = true;
            await boss.EvaluateAsync();
            Check((await repository.GetNotificationsAsync()).Any(item => item.TemplateId == brokenTemplate.Id && !item.IsResolved), "The notifications of a source that cannot be read are kept, unresolved");
            Check((await Session("integration.tester", true).GetViewsAsync()).Any(item => item.Template.Id == brokenTemplate.Id && item.Subject == "Rupt" && item.ObjectLabel.Contains($"Obiect {objectA}")),
                "They stay in the list, shown from the values stored when they were created");
            brokenSource.Fail = false;

            // Two users take it over at the same moment: exactly one succeeds.
            var takers = await Task.WhenAll(new[] { "ana", "bob" }.Select(async user =>
            {
                try { await Session(user).AcknowledgeAsync(view); return (user, message: (string?)null); }
                catch (NotificationOperationException exception) { return (user, message: exception.Message); }
            }));
            Check(takers.Count(item => item.message is null) == 1, "Two users taking over the same notification: exactly one succeeds");
            var winner = takers.Single(item => item.message is null).user;
            Check(takers.Single(item => item.message is not null).message!.Contains($"preluată de {winner}"), "The other user is told who took the notification over");
            var taken = (await boss.GetViewsAsync()).Single(item => item.Notification.Id == view.Notification.Id);
            Check(!taken.IsAlert && taken.Notification.AcknowledgedBy == winner, "A taken over notification stays in the list but stops warning");
            Check((await audit.GetEventsAsync()).Any(item => item.Action == AuditActions.AcknowledgeNotification && item.ActorUsername == winner && item.EntityId == view.Notification.Id.ToString()),
                "The journal names the operation \"Preluare notificare\" and the user who took it over");

            // Reminder: within 1..(days left - 2), warns again when the period ends, never while it runs.
            await Rejects<NotificationOperationException>(() => Session("ana").SnoozeAsync(taken, 0), "A reminder of zero days is rejected");
            await Rejects<NotificationOperationException>(() => Session("ana").SnoozeAsync(taken, 19), "A reminder longer than the days left minus two is rejected");
            var snoozed = await Session("ana").SnoozeAsync(taken, 5);
            Check(snoozed.SnoozeUntil == today.AddDays(5), "A reminder of 5 days is stored until the computed date");
            Check(!(await boss.GetViewsAsync()).Single(item => item.Notification.Id == view.Notification.Id).IsAlert, "During the reminder the notification does not warn");
            Check((await audit.GetEventsAsync()).Any(item => item.Action == AuditActions.SnoozeNotification && item.ActorUsername == "ana" && item.Details.Contains("5 zile")),
                "The journal names the operation \"Amânare notificare\" with the number of days");
            clock.Advance(TimeSpan.FromDays(5));
            var again = (await boss.GetViewsAsync()).Single(item => item.Notification.Id == view.Notification.Id);
            Check(again.IsAlert && again.DaysLeft == 15 && again.MaxSnoozeDays == 13, "When the reminder period ends the notification warns again, with a new limit");
            await Rejects<NotificationOperationException>(() => Session("bob").SnoozeAsync(taken, 3), "An outdated view of the notification cannot be used for a reminder");
            clock.Advance(TimeSpan.FromDays(-5));

            // An overdue notification: a reminder of 1..30 days; taken over or postponed it stays overdue (red) and in the list.
            await Rejects<NotificationOperationException>(() => Session("ana").SnoozeAsync(overdueView, 31), "A reminder longer than 30 days is rejected for an overdue notification");
            var overdueSnoozed = await Session("ana").SnoozeAsync(overdueView, 7);
            Check(overdueSnoozed.SnoozeUntil == today.AddDays(7), "An overdue notification can be postponed by a chosen number of days (up to 30)");
            var overdueAfter = (await boss.GetViewsAsync()).Single(item => item.Notification.Id == overdueView.Notification.Id);
            Check(overdueAfter.IsOverdue && !overdueAfter.IsAlert && (await boss.GetViewsAsync()).First(item => item.Template.Id == template.Id).Notification.Id == overdueView.Notification.Id,
                "A postponed overdue notification stops warning but stays overdue and stays first in the list");

            // Manual resolution: one user wins, the notification moves to the resolved list with its texts and is never recreated.
            var resolvedNow = await Session("carla").ResolveAsync(overdueAfter);
            Check(resolvedNow.IsResolved && resolvedNow.ResolvedBy == "carla" && !resolvedNow.ResolvedAutomatically, "A user can mark a notification as resolved");
            await Rejects<NotificationOperationException>(() => Session("bob").ResolveAsync(overdueAfter), "Resolving the same notification twice is refused");
            var resolvedList = await boss.GetResolvedViewsAsync();
            var resolvedItem = resolvedList.Single(item => item.Notification.Id == overdueView.Notification.Id);
            Check(resolvedItem.Subject == $"Expira Obiect {objectC}" && resolvedItem.ObjectLabel == $"Obiect {objectC}" && resolvedItem.SourceText.Contains("Eveniment test") && resolvedItem.Notification.ResolvedReason == ExpiryNotificationService.ManualResolutionReason,
                "A resolved notification keeps the subject, object and source it had, and the reason");
            Check(!(await boss.GetViewsAsync()).Any(item => item.Notification.Id == overdueView.Notification.Id), "A resolved notification leaves the list of active notifications");
            Check((await audit.GetEventsAsync()).Any(item => item.Action == AuditActions.ResolveNotification && item.ActorUsername == "carla" && item.EntityId == overdueView.Notification.Id.ToString()),
                "The journal names the operation \"Rezolvare notificare\" and the user");
            await Rejects<NotificationOperationException>(() => Session("ana").AcknowledgeAsync(overdueAfter), "A resolved notification can no longer be taken over");
            await boss.EvaluateAsync();
            Check((await Rows(template)).Count(item => item.ObjectId == objectC) == 1 && (await Rows(template)).Single(item => item.ObjectId == objectC).IsResolved,
                "A resolved notification is not created again for the same date");

            // Editing the date of an object closes its notification automatically, with the reason, and raises a new unread one.
            source.Instances.RemoveAll(item => item.ObjectId == objectA);
            source.Instances.Add(Make(objectA, 25));
            await boss.EvaluateAsync();
            var redated = (await boss.GetViewsAsync()).Where(item => item.Template.Id == template.Id).ToList();
            Check(redated.Count == 1 && redated[0].Notification.Id != view.Notification.Id && redated[0].IsAlert && redated[0].Notification.AcknowledgedBy is null && redated[0].DaysLeft == 25,
                "A changed date raises a new unread notification for the new date");
            var autoClosed = (await boss.GetResolvedViewsAsync()).Single(item => item.Notification.Id == view.Notification.Id);
            Check(autoClosed.Notification.ResolvedAutomatically && autoClosed.Notification.ResolvedBy == "sistem" &&
                  autoClosed.Notification.ResolvedReason!.Contains(StockMovementRules.DisplayDate(today.AddDays(20))) && autoClosed.Notification.ResolvedReason.Contains(StockMovementRules.DisplayDate(today.AddDays(25))),
                "The old notification is closed by the system with the reason naming the old and the new date");
            Check((await audit.GetEventsAsync()).Any(item => item.Action == AuditActions.AutoResolveNotification && item.ActorUsername == "sistem" && item.EntityId == view.Notification.Id.ToString() && item.Details.Contains("Motiv")),
                "The journal names the operation \"Rezolvare automată notificare\", actor \"sistem\"");

            // The automatic closing is reversible: the date returns, the same notification comes back unread. A manual one does not.
            source.Instances.RemoveAll(item => item.ObjectId == objectA);
            source.Instances.Add(Make(objectA, 20));
            await boss.EvaluateAsync();
            var reopened = (await boss.GetViewsAsync()).Where(item => item.Template.Id == template.Id).ToList();
            Check(reopened.Count == 1 && reopened[0].Notification.Id == view.Notification.Id && reopened[0].IsAlert && reopened[0].Notification.AcknowledgedBy is null && !reopened[0].Notification.IsResolved,
                "When the date returns, the automatically closed notification is reopened, unread; the one resolved by a user stays resolved");
            Check((await audit.GetEventsAsync()).Any(item => item.Action == AuditActions.ReopenNotification && item.EntityId == view.Notification.Id.ToString()),
                "The journal names the operation \"Redeschidere automată notificare\"");

            // An object that disappears closes its notification too (nothing is deleted).
            source.Instances.Clear();
            await boss.EvaluateAsync();
            Check(!(await boss.GetViewsAsync()).Any(item => item.Template.Id == template.Id), "A notification leaves the active list when its object no longer exists");
            Check((await boss.GetResolvedViewsAsync()).Any(item => item.Notification.Id == view.Notification.Id && item.Notification.ResolvedAutomatically && item.Notification.ResolvedReason!.Contains("nu mai este urmărit")),
                "It is closed by the system with the reason, not deleted");

            // Template switched off: the existing notifications stay in the list (they are resolved only by resolving); none are created meanwhile.
            source.Instances.Add(Make(objectA, 10));
            await boss.EvaluateAsync();
            var beforeOff = (await boss.GetViewsAsync()).Single(item => item.Template.Id == template.Id);
            await Session("carla").AcknowledgeAsync(beforeOff);
            var edit = NotificationTemplateInput.From(template); edit.Subject = "Nou <obiect>"; edit.ThresholdDays = 15;
            template = await boss.UpdateTemplateAsync(template, edit);
            Check((await audit.GetEventsAsync()).Any(item => item.Action == AuditActions.EditNotificationTemplate && item.EntityId == template.Id.ToString() && item.Details.Contains("Nou <obiect>")),
                "The journal names the operation \"Modificare șablon notificare\" with the old and new values");
            await Rejects<NotificationOperationException>(() => boss.UpdateTemplateAsync(template with { Version = 99 }, edit), "A stale template edit is rejected");
            var off = NotificationTemplateInput.From(template); off.Active = false;
            template = await boss.UpdateTemplateAsync(template, off);
            await boss.EvaluateAsync();
            var whileOff = (await boss.GetViewsAsync()).Where(item => item.Template.Id == template.Id).ToList();
            Check(whileOff.Count == 1 && whileOff[0].Notification.AcknowledgedBy == "carla", "Switching a template off does not hide its notifications, and they keep their state");
            source.Instances.Add(Make(objectB, 5));
            await boss.EvaluateAsync();
            Check(!(await Rows(template)).Any(item => item.ObjectId == objectB), "No new notifications are created while the template is off");
            off.Active = true;
            template = await boss.UpdateTemplateAsync(template, off);
            await boss.EvaluateAsync();
            var restored = (await boss.GetViewsAsync()).Where(item => item.Template.Id == template.Id).ToList();
            Check(restored.Count == 2 && restored.Single(item => item.Notification.ObjectId == objectA).Notification.AcknowledgedBy == "carla" && restored.Single(item => item.Notification.ObjectId == objectB).IsAlert,
                "Switching the template on again keeps the state (taken over stays taken over) and creates the ones that came due meanwhile");

            // Deleting a template: it needs a reason, reports the unresolved notifications and takes all its notifications away.
            var open = await boss.CountOpenNotificationsAsync(template);
            Check(open == 2, "The deletion dialog can report how many unresolved notifications the template has");
            await Rejects<NotificationOperationException>(() => boss.DeleteTemplateAsync(template, " "), "Deleting a template requires a reason");
            var templateId = template.Id;
            await boss.DeleteTemplateAsync(template, "Ext curatare sablon");
            template = null;
            Check(await ScalarLongAsync(probe, "SELECT COUNT(*) FROM expiry_notifications WHERE template_id=@id", ("@id", templateId)) == 0, "Deleting a template removes its notifications");
            Check((await audit.GetEventsAsync()).Any(item => item.Action == AuditActions.DeleteNotificationTemplate && item.EntityId == templateId.ToString()), "The journal names the operation \"Ștergere șablon notificare\"");
        }
        finally
        {
            foreach (var item in new[] { template, brokenTemplate })
                if (item is not null && (await repository.GetTemplatesAsync()).FirstOrDefault(current => current.Id == item.Id) is { } current)
                    await repository.DeleteTemplateAsync(current);
        }

        // The real vehicle source: an expiry date edited on the vehicle closes the notification, with the reason naming the change.
        var vehicles = new MariaVehicleRepository(configuration, admin, audit);
        var realBoss = Session("integration.tester", true, new VehicleExpirySource(vehicles, VehicleExpiryKind.Itp, ExpirySourceKeys.VehicleItp, "ITP"));
        var vehicle = await vehicles.CreateAsync(new VehicleInput { PlateNumber = "TS-94-" + Letters(), Description = "Ext notificari", ItpExpiry = today.AddDays(10), InsuranceExpiry = Expiry, RovinietaExpiry = Expiry });
        NotificationTemplate? vehicleTemplate = null;
        try
        {
            // The isolated test database is not shared with anyone: a template left by a manual session would make the single active template rule refuse this one.
            foreach (var leftover in (await repository.GetTemplatesAsync()).Where(item => item.SourceKey == ExpirySourceKeys.VehicleItp).ToList())
                await repository.DeleteTemplateAsync(leftover);
            {
                vehicleTemplate = await realBoss.CreateTemplateAsync(new NotificationTemplateInput
                { SourceKey = ExpirySourceKeys.VehicleItp, Subject = ExpiryTemplateRules.DefaultSubject, Body = ExpiryTemplateRules.DefaultBody, ThresholdDays = 15 });
                await realBoss.EvaluateAsync();
                var mine = (await realBoss.GetViewsAsync()).Where(item => item.Notification.ObjectId == vehicle.Id && item.Notification.SourceKey == ExpirySourceKeys.VehicleItp).ToList();
                Check(mine.Count == 1 && mine[0].Subject.Contains(vehicle.PlateNumber) && mine[0].Body.Contains(vehicle.Description) && mine[0].Subject.StartsWith("Expirare ITP") && mine[0].Url == $"/vehicule/{vehicle.Id}",
                    "The ITP of a vehicle expiring in 10 days raises a notification with the plate number, description and the link of the vehicle");
                var edit = VehicleInput.From(vehicle);
                VehicleRules.SetExpiry(edit, VehicleExpiryKind.Itp, today.AddDays(200)); edit.Reason = "Ext";
                vehicle = await vehicles.UpdateAsync(vehicle, edit, default, AuditActions.ExpiryItp);
                await realBoss.EvaluateAsync();
                Check(!(await realBoss.GetViewsAsync()).Any(item => item.Notification.ObjectId == vehicle.Id && item.Notification.SourceKey == ExpirySourceKeys.VehicleItp),
                    "Postponing the vehicle's ITP takes the notification out of the active list");
                var closed = (await realBoss.GetResolvedViewsAsync()).Single(item => item.Notification.ObjectId == vehicle.Id && item.Notification.SourceKey == ExpirySourceKeys.VehicleItp);
                Check(closed.Notification.ResolvedReason!.Contains("ITP") && closed.Notification.ResolvedReason.Contains(StockMovementRules.DisplayDate(today.AddDays(10))) &&
                      closed.Notification.ResolvedReason.Contains(StockMovementRules.DisplayDate(today.AddDays(200))) && closed.Subject.Contains(vehicle.PlateNumber),
                    "The resolved notification names the change: the ITP date from the old to the new value");
            }
        }
        finally
        {
            if (vehicleTemplate is not null) await repository.DeleteTemplateAsync((await repository.GetTemplatesAsync()).First(item => item.Id == vehicleTemplate.Id));
            await vehicles.DeleteAsync(vehicle, "Ext curatare");
        }
    }

    // ---- Clean-up of old resolved notifications ---------------------------------------------------------------------------------

    private static async Task NotificationSettingsAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe)
    {
        var suffix = Suffix();
        var clock = new ManualTimeProvider();
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var repository = new MariaExpiryNotificationRepository(configuration);
        var source = new TestExpirySource("test.purge." + suffix, []);
        ExpiryNotificationService Session(string user, bool administrator = false) =>
            new(repository, [source], administrator ? admin : new TestAccessControl(false, user), audit, clock);
        var boss = Session("integration.tester", true);
        var first = Random.Shared.Next(1000, 9999000);
        int P1 = first, P2 = first + 1, P3 = first + 2, P4 = first + 3, P5 = first + 4, P6 = first + 5, P7 = first + 6;
        ExpiryInstance Make(int id, int daysLeft) => new(id, $"Obiect {id}", today.AddDays(daysLeft), new Dictionary<string, string> { ["obiect"] = $"Obiect {id}" }, $"/obiect/{id}");
        NotificationTemplate? template = null;
        async Task<ExpiryNotification?> Row(int objectId) =>
            (await repository.GetNotificationsAsync()).FirstOrDefault(item => item.TemplateId == template!.Id && item.ObjectId == objectId);
        async Task Age(int objectId, int months) =>
            await ExecuteAsync(probe, "UPDATE expiry_notifications SET resolved_utc=@t WHERE template_id=@template AND object_id=@object",
                ("@t", MariaTimeTextForTest(clock.GetUtcNow().UtcDateTime.AddMonths(-months))), ("@template", template!.Id), ("@object", objectId));
        void Redate(int objectId, int daysLeft) { source.Instances.RemoveAll(item => item.ObjectId == objectId); source.Instances.Add(Make(objectId, daysLeft)); }
        int Mine(NotificationPurgePlan plan) => plan.Removable.Count(item => item.TemplateId == template!.Id);
        await ExecuteAsync(probe, "DELETE FROM notification_settings");
        try
        {
            // The setting: defaults until saved, administrator only, validated, and nothing stored when nothing changed.
            var initial = await boss.GetSettingsAsync();
            Check(!initial.PurgeEnabled && initial.PurgeMonths == NotificationPurgeRules.DefaultMonths && initial.LastPurgeUtc is null && initial.Version < 0,
                "Until saved, the clean-up setting is off with the default period of 12 months");
            await Rejects<AccessDeniedException>(() => Session("ana").GetSettingsAsync(), "A non-administrator cannot read the clean-up setting");
            await Rejects<AccessDeniedException>(() => Session("ana").SaveSettingsAsync(initial, true, 12), "A non-administrator cannot change the clean-up setting");
            await Rejects<AccessDeniedException>(() => Session("ana").PreviewPurgeAsync(12), "A non-administrator cannot preview a clean-up");
            await Rejects<NotificationOperationException>(() => boss.SaveSettingsAsync(initial, true, 0), "A period below 1 month is rejected");
            await Rejects<NotificationOperationException>(() => boss.SaveSettingsAsync(initial, true, NotificationPurgeRules.MaxMonths + 1), "A period above 60 months is rejected");
            var unchanged = await boss.SaveSettingsAsync(initial, false, NotificationPurgeRules.DefaultMonths);
            Check(unchanged.Settings.Version < 0 && unchanged.Removed == 0 && await ScalarLongAsync(probe, "SELECT COUNT(*) FROM notification_settings") == 0,
                "Saving the setting unchanged stores and journals nothing");

            // Data: five resolved notifications and an open one. P1 manual and still current, P2 manual whose date changed since,
            // P3 closed by the system, P4 open, P5 manual with a changed date but resolved only two months ago.
            template = await boss.CreateTemplateAsync(new NotificationTemplateInput { SourceKey = source.Key, Subject = "Expira <obiect>", Body = "<obiect>", ThresholdDays = 30 });
            source.Instances.AddRange([Make(P1, 10), Make(P2, 11), Make(P3, 12), Make(P4, 13), Make(P5, 14)]);
            await boss.EvaluateAsync();
            var carla = Session("carla");
            foreach (var view in (await boss.GetViewsAsync()).Where(item => item.Template.Id == template.Id && item.Notification.ObjectId is var o && (o == P1 || o == P2 || o == P5)).ToList())
                await carla.ResolveAsync(view);
            Redate(P2, 60); Redate(P5, 60); Redate(P3, 60);
            await boss.EvaluateAsync();
            Check((await Row(P3))!.ResolvedAutomatically && (await Row(P1))!.IsResolved && !(await Row(P4))!.IsResolved, "The data is set: manual, automatic and open notifications");
            await Age(P1, 14); await Age(P2, 14); await Age(P3, 14); await Age(P5, 2);

            // What a clean-up removes: old resolved ones, except a manual one whose event is still current (it would be created again),
            // and never on a guess (a source that cannot be read keeps its manual ones).
            var plan = await boss.PreviewPurgeAsync(12);
            Check(plan.Cutoff == today.AddMonths(-12) && Mine(plan) == 2 && plan.Removable.Any(item => item.ObjectId == P2) && plan.Removable.Any(item => item.ObjectId == P3),
                "A 12-month clean-up would remove the old manual one whose date changed and the old automatic one");
            Check(!plan.Removable.Any(item => item.TemplateId == template.Id && (item.ObjectId == P1 || item.ObjectId == P4 || item.ObjectId == P5)),
                "It keeps the manual one whose event is current, the open one and the one resolved recently");
            source.Fail = true;
            Check(Mine(await boss.PreviewPurgeAsync(12)) == 1, "When the source cannot be read, the manual notifications are kept (only the automatic one goes)");
            source.Fail = false;
            Check(Mine(await boss.PreviewPurgeAsync(1)) == 3, "A shorter period adds the manual one resolved two months ago");

            // Switching on removes at once, journals both the setting and the removal (actor: the administrator), and stores the run.
            var since = DateTime.UtcNow; await Task.Delay(30);
            var on = await boss.SaveSettingsAsync(initial, true, 12);
            Check(on.Settings.PurgeEnabled && on.Settings.PurgeMonths == 12 && on.Settings.Version >= 0 && on.Removed >= 2 && on.Cutoff == today.AddMonths(-12) &&
                  on.Settings.LastPurgeUtc == clock.GetUtcNow().UtcDateTime,
                "Switching the clean-up on removes the old resolved notifications at once and stores the moment of the run");
            Check(await Row(P2) is null && await Row(P3) is null && await Row(P1) is { IsResolved: true } && await Row(P4) is { IsResolved: false } && await Row(P5) is { IsResolved: true },
                "Only the announced notifications were deleted from the database");
            var afterOn = (await audit.GetEventsAsync()).Where(item => item.TimestampUtc > since).ToList();
            var settingsEvent = afterOn.Single(item => item.Action == AuditActions.EditNotificationSettings);
            Check(settingsEvent.EntityType == AuditEntities.NotificationSettings && settingsEvent.Details.Contains("oprită → activă"), "The journal names the operation \"Modificare setări curățare notificări\" with the old and new value");
            var purgeEvent = afterOn.Single(item => item.Action == AuditActions.PurgeResolvedNotifications);
            Check(purgeEvent.Details == NotificationPurgeRules.RemovedText(on.Removed, today.AddMonths(-12)) && purgeEvent.Details.StartsWith("au fost eliminate din baza de date") && purgeEvent.ActorUsername != "sistem",
                "The journal names the operation \"Curățare notificări rezolvate\" with the number removed and the limit date");
            await Rejects<NotificationOperationException>(() => boss.SaveSettingsAsync(initial, true, 6), "A stale view of the setting cannot overwrite it");

            // Shortening the period while it is on removes more; a change that removes nothing journals only the setting.
            var shortened = await boss.PreviewPurgeAsync(1);
            Check(Mine(shortened) == 1 && shortened.Removable.Any(item => item.ObjectId == P5), "Shortening to 1 month would remove the notification resolved two months ago");
            since = DateTime.UtcNow; await Task.Delay(30);
            var longer = await boss.SaveSettingsAsync(on.Settings, true, 24);
            Check(longer.Removed == 0 && !(await audit.GetEventsAsync()).Any(item => item.TimestampUtc > since && item.Action == AuditActions.PurgeResolvedNotifications),
                "Nothing is journaled as removed when a clean-up removes nothing");
            var shorter = await boss.SaveSettingsAsync(longer.Settings, true, 1);
            Check(shorter.Removed >= 1 && await Row(P5) is null && await Row(P1) is { IsResolved: true }, "Shortening the period deletes what became old enough, at once");

            // Switching off stops it: nothing is deleted any more, not even by the daily run.
            var off = await boss.SaveSettingsAsync(shorter.Settings, false, 12);
            Check(!off.Settings.PurgeEnabled && off.Removed == 0, "Switching the clean-up off removes nothing");
            source.Instances.AddRange([Make(P6, 5), Make(P7, 6)]);
            await boss.EvaluateAsync();
            Redate(P6, 60); Redate(P7, 60);
            await boss.EvaluateAsync();
            await Age(P6, 14); await Age(P7, 14);
            await boss.EvaluateAsync();
            Check(await Row(P6) is not null && await Row(P7) is not null, "With the switch off, the daily run deletes nothing");

            // The daily run: with the switch on, together with the evaluation, once a day, journaled by the system.
            await repository.SaveSettingsAsync(off.Settings, true, 12, null);
            since = DateTime.UtcNow; await Task.Delay(30);
            await boss.EvaluateAsync();
            var day1 = (await audit.GetEventsAsync()).Where(item => item.TimestampUtc > since && item.Action == AuditActions.PurgeResolvedNotifications).ToList();
            Check(await Row(P6) is null && await Row(P7) is null && (await repository.GetSettingsAsync()).LastPurgeUtc == clock.GetUtcNow().UtcDateTime,
                "With the switch on, the evaluation deletes the old resolved notifications and stores the day of the run");
            Check(day1.Count == 1 && day1[0].ActorUsername == "sistem" && day1[0].Details.StartsWith("au fost eliminate din baza de date 2 notificări rezolvate mai vechi de "),
                "The system journals the daily run (\"Curățare notificări rezolvate\", actor \"sistem\")");
            Redate(P6, 5);
            await boss.EvaluateAsync();
            Redate(P6, 60);
            await boss.EvaluateAsync();
            await Age(P6, 14);
            await boss.EvaluateAsync();
            Check(await Row(P6) is not null, "The daily run happens once a day: a second evaluation the same day deletes nothing");
            clock.Advance(TimeSpan.FromDays(1));
            await Age(P6, 14);
            var rowBefore = await Row(P6);
            await boss.EvaluateAsync();
            Check(rowBefore is not null && await Row(P6) is null, "The next day the run deletes again");
            since = DateTime.UtcNow; await Task.Delay(30);
            clock.Advance(TimeSpan.FromDays(1));
            await boss.EvaluateAsync();
            Check(!(await audit.GetEventsAsync()).Any(item => item.TimestampUtc > since && item.Action == AuditActions.PurgeResolvedNotifications), "A run that deletes nothing writes nothing to the journal");
        }
        finally
        {
            if (template is not null && (await repository.GetTemplatesAsync()).FirstOrDefault(item => item.Id == template.Id) is { } current)
                await repository.DeleteTemplateAsync(current);
            await ExecuteAsync(probe, "DELETE FROM notification_settings");
        }
    }

    // ---- Raw helpers -----------------------------------------------------------------------------------------------------------------

    private static async Task<MySqlConnection> OpenRawAsync(IConfiguration configuration)
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = configuration["Database:Host"] ?? "127.0.0.1",
            Port = configuration.GetValue<uint>("Database:Port", 3307),
            Database = configuration["Database:Name"] ?? "blazorstoc_test",
            UserID = configuration["Database:User"] ?? "",
            Password = configuration["Database:Password"],
            SslMode = Enum.Parse<MySqlSslMode>(configuration["Database:SslMode"] ?? "Required", true),
            CharacterSet = configuration["Database:CharSet"] ?? "utf8mb4",
            ConnectionTimeout = 10,
            DefaultCommandTimeout = 20
        };
        var connection = new MySqlConnection(builder.ConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        return connection;
    }

    private static async Task<int> ExecuteAsync(MySqlConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = new MySqlCommand(sql, connection);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static async Task<long> ScalarLongAsync(MySqlConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = new MySqlCommand(sql, connection);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return Convert.ToInt64(await command.ExecuteScalarAsync().ConfigureAwait(false));
    }
}
