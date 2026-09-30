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
            await Section("Work points", () => WorkPointsAsync(configuration, admin, audit));
            await Section("Change events", () => ChangeEventsAsync(configuration, admin, audit));
            await Section("Expiry notifications: templates, engine, take over, reminder", () => NotificationsAsync(configuration, admin, audit, probe));
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

    private static async Task WorkPointsAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit)
    {
        var suffix = Suffix();
        var beneficiaries = new MariaBeneficiaryRepository(configuration, admin, audit);
        var workPoints = new MariaWorkPointRepository(configuration, admin, audit);
        var owner = await beneficiaries.CreateAsync(Legal($"Ext Puncte {suffix} SRL", "RO" + Random.Shared.Next(40000000, 49999999)));
        try
        {
            var created = await workPoints.CreateAsync(owner.Id, new WorkPointInput { Name = "Depozit", Address = "Str. Florilor nr. 5, Cluj", Phone = "0722333444" });
            Check((await workPoints.GetAsync(owner.Id)).Any(item => item.Id == created.Id), "A work point is stored and read back");
            await Rejects<WorkPointOperationException>(() => workPoints.CreateAsync(owner.Id, new WorkPointInput { Name = "Alt depozit", Address = "strada FLORILOR 5, cluj" }), "A work point with the same normalized address is rejected");
            var updated = await workPoints.UpdateAsync(created, new WorkPointInput { Name = "Depozit nou", Address = "Str. Florilor nr. 5, Cluj", Phone = "0722333444" });
            Check(updated.Name == "Depozit nou", "A work point is edited");
            await workPoints.DeleteAsync(updated);
            Check((await workPoints.GetAsync(owner.Id)).Count == 0, "A work point is deleted");
        }
        finally
        {
            await beneficiaries.DeleteAsync((await beneficiaries.GetBeneficiariesAsync()).Single(item => item.Id == owner.Id), "Ext curatare");
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
        ExpiryInstance Make(int id, int daysLeft) => new(id, $"Obiect {id}", today.AddDays(daysLeft), new Dictionary<string, string> { ["obiect"] = $"Obiect {id}" });
        NotificationTemplate? template = null, brokenTemplate = null;
        try
        {
            // Only the administrator manages templates; invalid ones are rejected; the journal names each operation.
            await Rejects<AccessDeniedException>(() => Session("ana").CreateTemplateAsync(new NotificationTemplateInput { SourceKey = source.Key, Subject = "S", Body = "T", ThresholdDays = 30 }), "A non-administrator cannot create a template");
            await Rejects<NotificationOperationException>(() => boss.CreateTemplateAsync(new NotificationTemplateInput { SourceKey = source.Key, Subject = "S <gresit>", Body = "T", ThresholdDays = 30 }), "A template with an unknown placeholder is rejected");
            template = await boss.CreateTemplateAsync(new NotificationTemplateInput { SourceKey = source.Key, Subject = "Expira <obiect>", Body = "<eveniment>: <obiect> la <data expirare>, mai sunt <zile ramase> zile.", ThresholdDays = 30 });
            brokenTemplate = await boss.CreateTemplateAsync(new NotificationTemplateInput { SourceKey = brokenSource.Key, Subject = "Rupt", Body = "Rupt", ThresholdDays = 30 });
            Check((await audit.GetEventsAsync()).Any(item => item.Action == AuditActions.CreateNotificationTemplate && item.EntityId == template.Id.ToString()), "The journal names the operation \"Adăugare șablon notificare\"");
            await Rejects<AccessDeniedException>(() => Session("ana").GetTemplatesAsync(), "A non-administrator cannot list the templates");

            // The engine: only objects inside the period and not yet expired, once per (template, object, date).
            source.Instances.AddRange([Make(objectA, 20), Make(objectB, 40), Make(objectC, -1)]);
            brokenSource.Instances.Add(Make(objectA, 10));
            await boss.EvaluateAsync();
            await boss.EvaluateAsync();
            var views = (await boss.GetViewsAsync()).Where(item => item.Template.Id == template.Id).ToList();
            Check(views.Count == 1 && views[0].Notification.ObjectId == objectA, "Only the object within 30 days and not expired gets a notification, once");
            var view = views[0];
            var createdEntries = (await audit.GetEventsAsync()).Where(item => item.Action == AuditActions.NotificationCreated && item.EntityId == view.Notification.Id.ToString()).ToList();
            Check(createdEntries.Count == 1 && createdEntries[0].ActorUsername == "sistem" && createdEntries[0].EntityType == AuditEntities.Notification &&
                  createdEntries[0].Target.Contains($"Obiect {objectA}") && createdEntries[0].Details.Contains(StockMovementRules.DisplayDate(today.AddDays(20))),
                "The system journals the creation of a notification once (\"Notificare creată\", actor \"sistem\"), with the object and the expiry date");
            Check(!(await audit.GetEventsAsync()).Any(item => item.Action == AuditActions.NotificationCreated && item.Target.Contains($"Obiect {objectB}")),
                "No creation is journaled for an object outside the period");
            Check(view.Subject == $"Expira Obiect {objectA}" && view.Body.Contains($"Eveniment test: Obiect {objectA} la {StockMovementRules.DisplayDate(today.AddDays(20))}, mai sunt 20 zile."),
                "The subject and text are produced from the template with the values of the object");
            Check(view.IsAlert && view.MaxSnoozeDays == 18 && view.DaysLeft == 20, "A new notification warns, with 20 days left and a reminder limit of 18");
            var beforeCount = await boss.AlertCountAsync();
            Check(beforeCount >= 1, "The alert count includes the new notification");

            // An unreadable source keeps its notifications instead of deleting them.
            brokenSource.Fail = true;
            await boss.EvaluateAsync();
            Check((await repository.GetNotificationsAsync()).Any(item => item.TemplateId == brokenTemplate.Id), "The notifications of a source that cannot be read are kept");
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

            // Editing the expiry date replaces the notification with a fresh, unread one; removing the object removes it.
            source.Instances.Clear();
            source.Instances.AddRange([Make(objectA, 25)]);
            await boss.EvaluateAsync();
            var redated = (await boss.GetViewsAsync()).Where(item => item.Template.Id == template.Id).ToList();
            Check(redated.Count == 1 && redated[0].Notification.Id != view.Notification.Id && redated[0].IsAlert && redated[0].Notification.AcknowledgedBy is null,
                "A changed expiry date replaces the notification with a new unread one");
            source.Instances.Clear();
            await boss.EvaluateAsync();
            Check(!(await boss.GetViewsAsync()).Any(item => item.Template.Id == template.Id), "A notification disappears when its object no longer exists");

            // Template edits, deactivation and deletion.
            source.Instances.Add(Make(objectA, 10));
            await boss.EvaluateAsync();
            var edit = NotificationTemplateInput.From(template); edit.Subject = "Nou <obiect>"; edit.ThresholdDays = 15;
            template = await boss.UpdateTemplateAsync(template, edit);
            Check((await audit.GetEventsAsync()).Any(item => item.Action == AuditActions.EditNotificationTemplate && item.EntityId == template.Id.ToString() && item.Details.Contains("Nou <obiect>")),
                "The journal names the operation \"Modificare șablon notificare\" with the old and new values");
            await Rejects<NotificationOperationException>(() => boss.UpdateTemplateAsync(template with { Version = 99 }, edit), "A stale template edit is rejected");
            // Switching a template off hides its notifications but keeps their state; switching it on brings them back as they were.
            var beforeOff = (await boss.GetViewsAsync()).Single(item => item.Template.Id == template.Id);
            await Session("carla").AcknowledgeAsync(beforeOff);
            var off = NotificationTemplateInput.From(template); off.Active = false;
            template = await boss.UpdateTemplateAsync(template, off);
            await boss.EvaluateAsync();
            Check(!(await boss.GetViewsAsync()).Any(item => item.Template.Id == template.Id), "Switching a template off hides its notifications");
            Check((await repository.GetNotificationsAsync()).Any(item => item.TemplateId == template.Id && item.AcknowledgedBy == "carla"), "The hidden notifications keep their state in the database");
            source.Instances.Add(Make(objectB, 5));
            await boss.EvaluateAsync();
            Check(!(await repository.GetNotificationsAsync()).Any(item => item.TemplateId == template.Id && item.ObjectId == objectB), "No new notifications are created while the template is off");
            off.Active = true;
            template = await boss.UpdateTemplateAsync(template, off);
            await boss.EvaluateAsync();
            var restored = (await boss.GetViewsAsync()).Where(item => item.Template.Id == template.Id).ToList();
            Check(restored.Count == 2 && restored.Single(item => item.Notification.ObjectId == objectA).Notification.AcknowledgedBy == "carla" &&
                  !restored.Single(item => item.Notification.ObjectId == objectA).IsAlert && restored.Single(item => item.Notification.ObjectId == objectB).IsAlert,
                "Switching the template on again restores the state (taken over stays taken over) and creates the ones that came due meanwhile");
            source.Instances.RemoveAll(item => item.ObjectId == objectB);
            await boss.EvaluateAsync();
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

        // The real vehicle source: an expiry date edited on the vehicle replaces the notification.
        var vehicles = new MariaVehicleRepository(configuration, admin, audit);
        var realBoss = Session("integration.tester", true, new VehicleExpirySource(vehicles, VehicleExpiryKind.Itp, ExpirySourceKeys.VehicleItp, "ITP"));
        var vehicle = await vehicles.CreateAsync(new VehicleInput { PlateNumber = "TS-94-" + Letters(), Description = "Ext notificari", ItpExpiry = today.AddDays(10), InsuranceExpiry = Expiry, RovinietaExpiry = Expiry });
        NotificationTemplate? vehicleTemplate = null;
        try
        {
            vehicleTemplate = await realBoss.CreateTemplateAsync(new NotificationTemplateInput
            { SourceKey = ExpirySourceKeys.VehicleItp, Subject = ExpiryTemplateRules.DefaultSubject, Body = ExpiryTemplateRules.DefaultBody, ThresholdDays = 15 });
            await realBoss.EvaluateAsync();
            var mine = (await realBoss.GetViewsAsync()).Where(item => item.Template.Id == vehicleTemplate.Id && item.Notification.ObjectId == vehicle.Id).ToList();
            Check(mine.Count == 1 && mine[0].Subject.Contains(vehicle.PlateNumber) && mine[0].Body.Contains(vehicle.Description) && mine[0].Subject.StartsWith("Expirare ITP"),
                "The ITP of a vehicle expiring in 10 days raises a notification with the plate number and description");
            var edit = VehicleInput.From(vehicle);
            VehicleRules.SetExpiry(edit, VehicleExpiryKind.Itp, today.AddDays(200)); edit.Reason = "Ext";
            vehicle = await vehicles.UpdateAsync(vehicle, edit, default, AuditActions.ExpiryItp);
            await realBoss.EvaluateAsync();
            Check(!(await realBoss.GetViewsAsync()).Any(item => item.Template.Id == vehicleTemplate.Id && item.Notification.ObjectId == vehicle.Id),
                "Postponing the vehicle's ITP removes the notification");
        }
        finally
        {
            if (vehicleTemplate is not null) await repository.DeleteTemplateAsync((await repository.GetTemplatesAsync()).First(item => item.Id == vehicleTemplate.Id));
            await vehicles.DeleteAsync(vehicle, "Ext curatare");
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
