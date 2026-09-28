using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using BlazorStoc.Services;

namespace BlazorStoc.Checks;

// Subtask 2.11: real integration checks against the isolated blazorstoc_test MariaDB database (see
// local-secrets/test-database.private.json - same schema and 18 triggers as the real BlazorStoc delivery
// database, but empty and safe to write to). Every check here opens a genuine MySqlConnection and drives the
// Maria* repositories with the app's own input/validation types - no mocks, no stand-ins for MySqlConnector.
//
// IMPORTANT FINDING kept close to the code it documents: MariaProductRepository.WriteAsync,
// MariaBeneficiaryRepository.WriteAsync, MariaVehicleRepository.WriteAsync, MariaStockMovementRepository.WriteAsync
// and MariaUserRepository.WriteAsync each start with
//     if (!string.Equals(configuration["Database:Name"] ?? "BlazorStoc", "BlazorStoc", StringComparison.Ordinal))
//         throw new ...OperationException("Modificările sunt permise numai în baza BlazorStoc. ...");
// This ordinal-exact guard was written to keep writes off the legacy-schema database (Program.cs's own
// "wrongDatabase" regression test exercises it with Database:Name="stocesp") - but it also rejects the isolated
// integration-test database this very subtask introduces (Database:Name="blazorstoc_test"), and it runs before
// any SQL is issued. As written, Create/Update/Delete on those five repositories can never reach blazorstoc_test.
// I attempted the minimal fix (accept any name starting with "BlazorStoc", so "stocesp" stays rejected but
// "blazorstoc_test" is accepted) but the harness's safety classifier blocked that edit as weakening a database
// write guard, so it needs a human decision rather than an autonomous one - see the final report handed back for
// this task. This suite does not hide the resulting gap: every blocked write path is exercised for real, the
// exact rejection is caught and reported as "BUG-BLOCKED" (not a false PASS), and every check that is NOT subject
// to that guard (all reads, MariaProjectRepository, MariaProductLockRepository, MariaUserRepository.AuthenticateAsync)
// is exercised in full against the real database.
public static class MariaIntegrationChecks
{
    private const string WriteGuardMarker = "numai în baza BlazorStoc";

    public static async Task RunAsync(IConfiguration configuration)
    {
        Console.WriteLine("=== Subtask 2.11: MariaDB integration checks (blazorstoc_test) ===");

        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
            Console.WriteLine("PASS: " + message);
        }
        var blocked = new List<string>();
        void Blocked(string section, string message)
        {
            blocked.Add(section);
            Console.WriteLine($"BUG-BLOCKED [{section}]: {message}");
        }
        static bool IsWriteGuardBug(Exception exception) => exception.Message.Contains(WriteGuardMarker, StringComparison.Ordinal);

        var admin = new TestAccessControl(true, "integration.tester");
        var audit = new MariaAuditTrail(configuration);
        var environment = new TestWebHostEnvironment(Path.GetTempPath());

        await using var probe = await OpenRawAsync(configuration);
        await CheckConnectivityAsync(probe, Check);

        // Raw-SQL seeds used only where a repository's own write path is blocked by the guard bug above, so a
        // dependent section (product locks, projects) can still be exercised for real against a real FK target.
        // Never touches anything but blazorstoc_test; every seeded row is removed in the cleanup pass below.
        var seedCategoryId = 0L;
        var seedSubcategoryId = 0L;
        var seedProductId = 0L;
        var seedBeneficiaryId = 0L;

        try
        {
            await CheckProductsAsync(configuration, admin, audit, Check, Blocked, IsWriteGuardBug, probe);
            await CheckBeneficiariesAsync(configuration, admin, audit, Check, Blocked, IsWriteGuardBug);
            await CheckVehiclesAsync(configuration, admin, audit, Check, Blocked, IsWriteGuardBug);
            await CheckStockMovementsAsync(configuration, admin, audit, Check, Blocked, IsWriteGuardBug);
            await CheckUsersAsync(configuration, admin, audit, Check, Blocked, IsWriteGuardBug);

            (seedCategoryId, seedSubcategoryId, seedProductId) = await SeedProductAsync(probe, "Integrare Test Blocare Categorie",
                "Integrare Test Blocare Subcategorie", "Integrare Test Blocare Produs");
            await CheckProductLocksAsync(configuration, admin, Check, probe, seedProductId);

            seedBeneficiaryId = await SeedBeneficiaryAsync(probe, "Integrare Test Proiect SRL", "RO19998877");
            await CheckProjectsAsync(configuration, admin, audit, environment, Check, probe, seedBeneficiaryId);
        }
        finally
        {
            await CleanupSeedsAsync(probe, seedProductId, seedSubcategoryId, seedCategoryId, seedBeneficiaryId);
        }

        Console.WriteLine(blocked.Count == 0
            ? "=== MariaDB integration checks: every section exercised the real repository write paths against blazorstoc_test. ==="
            : $"=== MariaDB integration checks finished; {blocked.Count} section(s) blocked by the Database:Name write-guard bug: {string.Join(", ", blocked)}. See the header comment and the final report for the fix that needs sign-off. ===");
    }

    // ---- Connectivity -------------------------------------------------------------------------------------

    private static async Task CheckConnectivityAsync(MySqlConnection probe, Action<bool, string> check)
    {
        Console.WriteLine("--- Connectivity ---");
        string? cipher = null;
        await using (var command = new MySqlCommand("SHOW STATUS LIKE 'Ssl_cipher'", probe))
        await using (var reader = await command.ExecuteReaderAsync().ConfigureAwait(false))
            if (await reader.ReadAsync().ConfigureAwait(false)) cipher = reader.GetString(1);
        check(!string.IsNullOrEmpty(cipher), $"Connection to blazorstoc_test negotiates TLS (cipher: {cipher})");

        var tables = new List<string>();
        await using (var command = new MySqlCommand(
            "SELECT table_name FROM information_schema.tables WHERE table_schema=DATABASE()", probe))
        await using (var reader = await command.ExecuteReaderAsync().ConfigureAwait(false))
            while (await reader.ReadAsync().ConfigureAwait(false)) tables.Add(reader.GetString(0));
        Console.WriteLine($"INFO: {tables.Count} tables present in blazorstoc_test: {string.Join(", ", tables.OrderBy(name => name, StringComparer.Ordinal))}");
        string[] expected =
        [
            "products", "categories", "subcategories", "beneficiaries", "vehicles", "stock_movements",
            "stock_movement_history", "web_users", "projects", "project_observations", "project_observation_files",
            "product_locks", "audit_events", "change_events"
        ];
        var missing = expected.Where(name => !tables.Contains(name, StringComparer.OrdinalIgnoreCase)).ToList();
        check(missing.Count == 0,
            missing.Count == 0
                ? $"All {expected.Length} core tables used by this suite exist in blazorstoc_test ({tables.Count} tables total, informational)"
                : "Missing expected tables in blazorstoc_test: " + string.Join(", ", missing));
    }

    // ---- Products / categories / subcategories -------------------------------------------------------------

    private static async Task CheckProductsAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit,
        Action<bool, string> check, Action<string, string> blocked, Func<Exception, bool> isWriteGuardBug, MySqlConnection probe)
    {
        Console.WriteLine("--- Products / categories / subcategories (MariaProductRepository) ---");
        var repository = new MariaProductRepository(configuration, admin, audit);
        var groupsBeforeWrite = await repository.GetGroupsAsync().ConfigureAwait(false);
        check(groupsBeforeWrite is not null, "GetGroupsAsync executes against blazorstoc_test (reads are not subject to the write guard)");

        const string category = "Integrare Test Categorie";
        const string subcategory = "Integrare Test Subcategorie";
        try
        {
            await repository.CreateCategoryAsync(category).ConfigureAwait(false);
        }
        catch (ProductOperationException exception) when (isWriteGuardBug(exception))
        {
            blocked("Products", exception.Message);
            return;
        }

        try
        {
            await repository.CreateSubcategoryAsync(category, subcategory).ConfigureAwait(false);
            var product = await repository.CreateAsync(new ProductInput
            {
                Name = "Integrare Test Produs", Category = category, Subcategory = subcategory,
                Description = "Produs creat de verificarile de integrare Subtask 2.11"
            }).ConfigureAwait(false);
            check(product.Id > 0 && product.Quantity == 0, "A new product is created with stock zero");

            var reread = await repository.GetProductAsync(product.Id).ConfigureAwait(false);
            check(reread is not null && reread.Name == product.Name && reread.Category == category && reread.Version == product.Version,
                "The created product is read back with the same name, category and version");

            var edit = ProductInput.From(product);
            edit.Description = "Descriere actualizata de verificarile de integrare";
            edit.Reason = "Verificare integrare Subtask 2.11";
            var updated = await repository.UpdateAsync(product, edit).ConfigureAwait(false);
            check(updated.Version == product.Version + 1 && updated.Description == edit.Description,
                "Updating the product increments its optimistic-concurrency version");

            var staleEdit = ProductInput.From(product);
            staleEdit.Reason = "Motiv editare stale";
            try
            {
                await repository.UpdateAsync(product, staleEdit).ConfigureAwait(false);
                throw new Exception("A stale product update (outdated version) was not rejected");
            }
            catch (ProductOperationException) { Console.WriteLine("PASS: A stale product update (outdated version) is rejected"); }

            await repository.DeleteAsync(updated, "Curatare test integrare Subtask 2.11").ConfigureAwait(false);
            var afterDelete = await repository.GetProductAsync(product.Id).ConfigureAwait(false);
            check(afterDelete is null, "The deleted product is gone from blazorstoc_test");

            var auditCount = await ScalarLongAsync(probe,
                "SELECT COUNT(*) FROM audit_events WHERE entity_type=@type AND entity_id=@id",
                ("@type", AuditEntities.Product), ("@id", product.Id.ToString())).ConfigureAwait(false);
            check(auditCount >= 3, $"audit_events recorded {auditCount} entries (create/edit/delete) for product #{product.Id}");

            foreach (var action in new[] { AuditActions.Create, AuditActions.Edit, AuditActions.Delete })
            {
                var changeCount = await ScalarLongAsync(probe,
                    "SELECT COUNT(*) FROM change_events WHERE entity_type=@type AND entity_id=@id AND action=@action",
                    ("@type", AuditEntities.Product), ("@id", product.Id.ToString()), ("@action", action)).ConfigureAwait(false);
                check(changeCount >= 1, $"The change_events trigger recorded a '{action}' row for product #{product.Id}");
            }
        }
        finally
        {
            await using var cleanup = await OpenRawAsync(configuration).ConfigureAwait(false);
            await ExecuteAsync(cleanup, "DELETE FROM products WHERE name=@name", ("@name", "Integrare Test Produs")).ConfigureAwait(false);
            await ExecuteAsync(cleanup, "DELETE FROM subcategories WHERE name=@name", ("@name", subcategory)).ConfigureAwait(false);
            await ExecuteAsync(cleanup, "DELETE FROM categories WHERE name=@name", ("@name", category)).ConfigureAwait(false);
        }
    }

    // ---- Beneficiaries --------------------------------------------------------------------------------------

    private static async Task CheckBeneficiariesAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit,
        Action<bool, string> check, Action<string, string> blocked, Func<Exception, bool> isWriteGuardBug)
    {
        Console.WriteLine("--- Beneficiaries (MariaBeneficiaryRepository) ---");
        var repository = new MariaBeneficiaryRepository(configuration, admin, audit);
        try
        {
            var beneficiary = await repository.CreateAsync(new BeneficiaryInput { Name = "Integrare Test Beneficiar SRL", Cui = "RO19998811" })
                .ConfigureAwait(false);
            try
            {
                check(beneficiary.Id > 0, "A new beneficiary is created in blazorstoc_test");
                try
                {
                    await repository.CreateAsync(new BeneficiaryInput { Name = "Alt Nume SRL", Cui = "RO19998811" }).ConfigureAwait(false);
                    throw new Exception("A duplicate CUI was accepted");
                }
                catch (BeneficiaryOperationException) { Console.WriteLine("PASS: A duplicate beneficiary CUI is rejected"); }
            }
            finally
            {
                await repository.DeleteAsync(beneficiary, "Curatare test integrare Subtask 2.11").ConfigureAwait(false);
                var beneficiaries = await repository.GetBeneficiariesAsync().ConfigureAwait(false);
                check(!beneficiaries.Any(item => item.Id == beneficiary.Id), "The deleted beneficiary is gone from blazorstoc_test");
            }
        }
        catch (BeneficiaryOperationException exception) when (isWriteGuardBug(exception))
        {
            blocked("Beneficiaries", exception.Message);
        }
    }

    // ---- Vehicles ---------------------------------------------------------------------------------------------

    private static async Task CheckVehiclesAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit,
        Action<bool, string> check, Action<string, string> blocked, Func<Exception, bool> isWriteGuardBug)
    {
        Console.WriteLine("--- Vehicles (MariaVehicleRepository) ---");
        var repository = new MariaVehicleRepository(configuration, admin, audit);
        try
        {
            var vehicle = await repository.CreateAsync(new VehicleInput { PlateNumber = "TS-99-ZZZ", Description = "Vehicul verificare integrare" })
                .ConfigureAwait(false);
            try
            {
                check(vehicle.Id > 0 && vehicle.PlateNumber == "TS-99-ZZZ", "A new vehicle is created with a valid plate number");
                try
                {
                    await repository.CreateAsync(new VehicleInput { PlateNumber = "ts99zzz", Description = "Duplicat" }).ConfigureAwait(false);
                    throw new Exception("A duplicate plate number was accepted");
                }
                catch (VehicleOperationException) { Console.WriteLine("PASS: A duplicate vehicle plate number is rejected"); }
            }
            finally
            {
                await repository.DeleteAsync(vehicle, "Curatare test integrare Subtask 2.11").ConfigureAwait(false);
                var vehicles = await repository.GetVehiclesAsync().ConfigureAwait(false);
                check(!vehicles.Any(item => item.Id == vehicle.Id), "The deleted vehicle is gone from blazorstoc_test");
            }
        }
        catch (VehicleOperationException exception) when (isWriteGuardBug(exception))
        {
            blocked("Vehicles", exception.Message);
        }
    }

    // ---- Stock movements --------------------------------------------------------------------------------------

    private static async Task CheckStockMovementsAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit,
        Action<bool, string> check, Action<string, string> blocked, Func<Exception, bool> isWriteGuardBug)
    {
        Console.WriteLine("--- Stock movements (MariaStockMovementRepository) ---");
        var products = new MariaProductRepository(configuration, admin, audit);
        var vehicles = new MariaVehicleRepository(configuration, admin, audit);
        var movements = new MariaStockMovementRepository(configuration, admin, audit);
        const string category = "Integrare Test Miscari Categorie";
        const string subcategory = "Integrare Test Miscari Subcategorie";
        Product? product = null;
        Vehicle? vehicle = null;
        var createdMovementIds = new List<int>();
        try
        {
            await products.CreateCategoryAsync(category).ConfigureAwait(false);
            await products.CreateSubcategoryAsync(category, subcategory).ConfigureAwait(false);
            product = await products.CreateAsync(new ProductInput
            {
                Name = "Integrare Test Produs Miscari", Category = category, Subcategory = subcategory,
                Description = "Produs pentru verificarea miscarilor de stoc, Subtask 2.11"
            }).ConfigureAwait(false);
            vehicle = await vehicles.CreateAsync(new VehicleInput { PlateNumber = "TS-99-MIS", Description = "Vehicul verificare miscari" })
                .ConfigureAwait(false);

            var entry = await movements.CreateAsync(product.Id, new StockMovementInput
            {
                Kind = StockMovementKind.Entry, Date = DateOnly.FromDateTime(DateTime.Now), Quantity = 10,
                Description = "Intrare verificare integrare Subtask 2.11"
            }).ConfigureAwait(false);
            createdMovementIds.Add(entry.Movement.Id);
            check(entry.Stock == 10, "An Entry movement raises the product's total stock to 10");

            var exitToVehicle = await movements.CreateAsync(product.Id, new StockMovementInput
            {
                Kind = StockMovementKind.Exit, Date = DateOnly.FromDateTime(DateTime.Now), Quantity = 4,
                Destination = ExitDestination.Vehicle, VehicleId = vehicle.Id,
                Description = "Iesire spre vehicul, verificare integrare Subtask 2.11"
            }).ConfigureAwait(false);
            createdMovementIds.Add(exitToVehicle.Movement.Id);
            check(exitToVehicle.Stock == 10, "Total stock is unchanged by an exit to a vehicle (still 10)");

            var vehicleStocks = await movements.GetVehicleStocksAsync(product.Id).ConfigureAwait(false);
            var thisVehicleStock = vehicleStocks.SingleOrDefault(item => item.VehicleId == vehicle.Id);
            check(thisVehicleStock?.Quantity == 4, "The vehicle now holds 4 units, warehouse split reflects the transfer");

            // The negative-stock guard (EnsureVehicleStocksNotNegativeAsync) only ever fires for a movement that
            // REDUCES what a vehicle holds (kind=Exit with SourceVehicleId - a withdrawal from the vehicle, e.g. a
            // WarehouseReturn), never for one that adds to it (Destination=Vehicle only ever increases the
            // vehicle's total, so it can never go negative and the guard is a no-op there by design - the app
            // deliberately allows overall/warehouse stock to go negative elsewhere, flagged rather than blocked,
            // see "Negative stock is kept and flagged" in the default suite). Exercise the guard on the path it
            // actually protects: returning more than the vehicle holds (4) back to the warehouse.
            try
            {
                await movements.CreateAsync(product.Id, new StockMovementInput
                {
                    Kind = StockMovementKind.Exit, Date = DateOnly.FromDateTime(DateTime.Now), Quantity = 100,
                    Destination = ExitDestination.WarehouseReturn, SourceVehicleId = vehicle.Id,
                    Description = "Restituire peste stocul din vehicul, verificare integrare Subtask 2.11"
                }).ConfigureAwait(false);
                throw new Exception("A warehouse return larger than the vehicle's stock was accepted");
            }
            catch (StockMovementOperationException) { Console.WriteLine("PASS: A warehouse return larger than the vehicle's stock is rejected (negative-stock guard)"); }
        }
        catch (Exception exception) when (isWriteGuardBug(exception))
        {
            blocked("StockMovements", exception.Message);
        }
        finally
        {
            // Movements must be removed before the product/vehicle they reference (FK ON DELETE RESTRICT).
            foreach (var id in createdMovementIds)
            {
                var stored = await movements.GetAsync(id).ConfigureAwait(false);
                if (stored is not null) await movements.DeleteAsync(stored, "Curatare test integrare Subtask 2.11").ConfigureAwait(false);
            }
            if (vehicle is not null) await vehicles.DeleteAsync(vehicle, "Curatare test integrare Subtask 2.11").ConfigureAwait(false);
            if (product is not null) await products.DeleteAsync(product, "Curatare test integrare Subtask 2.11").ConfigureAwait(false);
            await using var cleanup = await OpenRawAsync(configuration).ConfigureAwait(false);
            await ExecuteAsync(cleanup, "DELETE FROM subcategories WHERE name=@name", ("@name", subcategory)).ConfigureAwait(false);
            await ExecuteAsync(cleanup, "DELETE FROM categories WHERE name=@name", ("@name", category)).ConfigureAwait(false);
        }
    }

    // ---- Users --------------------------------------------------------------------------------------------------

    private static async Task CheckUsersAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit,
        Action<bool, string> check, Action<string, string> blocked, Func<Exception, bool> isWriteGuardBug)
    {
        Console.WriteLine("--- Users (MariaUserRepository) ---");
        var repository = new MariaUserRepository(configuration, admin, audit);

        // AuthenticateAsync only reads - unaffected by the write guard, so it is exercised fully against a
        // username that cannot exist yet in the freshly-emptied blazorstoc_test.
        var missing = await repository.AuthenticateAsync("utilizator.inexistent.integrare", "orice-parola-oarecare").ConfigureAwait(false);
        check(missing.Status == AuthenticationStatus.InvalidCredentials, "Authenticating a nonexistent username returns InvalidCredentials");

        try
        {
            var user = await repository.CreateAsync(new WebUserInput
            {
                Username = "integrare.test", DisplayName = "Integrare Test", Role = AccessRoles.LimitedUser,
                Password = "parola-integrare-test-123"
            }).ConfigureAwait(false);
            try
            {
                check(user.Id > 0 && user.IsActive, "A new user is created active in blazorstoc_test");
                var ok = await repository.AuthenticateAsync("integrare.test", "parola-integrare-test-123").ConfigureAwait(false);
                check(ok.Status == AuthenticationStatus.Success && ok.User?.Id == user.Id, "Authenticating with the right password succeeds");
                var wrong = await repository.AuthenticateAsync("integrare.test", "parola-gresita-oarecare").ConfigureAwait(false);
                check(wrong.Status == AuthenticationStatus.InvalidCredentials, "Authenticating with the wrong password is rejected");
                try
                {
                    await repository.CreateAsync(new WebUserInput
                    {
                        Username = "INTEGRARE.TEST", DisplayName = "Duplicat", Role = AccessRoles.LimitedUser,
                        Password = "parola-integrare-test-123"
                    }).ConfigureAwait(false);
                    throw new Exception("A duplicate username was accepted");
                }
                catch (UserOperationException) { Console.WriteLine("PASS: A duplicate username is rejected"); }
            }
            finally
            {
                await repository.DeleteAsync(user, "Curatare test integrare Subtask 2.11").ConfigureAwait(false);
                var users = await repository.GetUsersAsync().ConfigureAwait(false);
                check(!users.Any(item => item.Id == user.Id), "The deleted user is gone from blazorstoc_test");
            }
        }
        catch (UserOperationException exception) when (isWriteGuardBug(exception))
        {
            blocked("Users", exception.Message);
        }
    }

    // ---- Projects (no write guard - exercised in full) -----------------------------------------------------------

    private static async Task CheckProjectsAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit,
        IWebHostEnvironment environment, Action<bool, string> check, MySqlConnection probe, long beneficiaryId)
    {
        Console.WriteLine("--- Projects (MariaProjectRepository - no write guard) ---");
        var repository = new MariaProjectRepository(configuration, environment, admin, audit);
        var project = await repository.CreateAsync(new ProjectInput
        {
            BeneficiaryId = checked((int)beneficiaryId), Name = "Integrare Test Proiect", Observations = "Observatii initiale"
        }).ConfigureAwait(false);
        check(project.Id > 0 && project.BeneficiaryId == beneficiaryId, "A new project is created for the seeded beneficiary in blazorstoc_test");

        var observation = await repository.CreateObservationAsync(project.Id,
            new ProjectObservationInput { Name = "Observatie integrare", Content = "Continut observatie de test" },
            "integrare.tester").ConfigureAwait(false);
        check(observation.Id > 0 && observation.ProjectId == project.Id, "An observation is added to the new project");

        var observations = await repository.GetObservationsAsync(project.Id).ConfigureAwait(false);
        check(observations.Any(item => item.Id == observation.Id), "The added observation is read back from the project");

        await repository.DeleteObservationAsync(observation, "Curatare test integrare Subtask 2.11").ConfigureAwait(false);
        await repository.DeleteAsync(project, "Curatare test integrare Subtask 2.11").ConfigureAwait(false);
        var afterDelete = await repository.GetAsync(project.Id).ConfigureAwait(false);
        check(afterDelete is null, "The deleted project is gone from blazorstoc_test");

        var auditCount = await ScalarLongAsync(probe,
            "SELECT COUNT(*) FROM audit_events WHERE entity_type=@type AND entity_id=@id",
            ("@type", AuditEntities.Project), ("@id", project.Id.ToString())).ConfigureAwait(false);
        check(auditCount >= 1, $"audit_events recorded {auditCount} entries for project #{project.Id}");

        // A15 regression coverage: the project's own archive row exists, but archive_relations has none for it -
        // the observation was already archived and deleted independently by DeleteObservationAsync above, so by
        // the time DeleteAsync(project) reads the project's observations there are none left to attach - and no
        // row survives in the live tables.
        var archiveProjectsCount = await ScalarLongAsync(probe,
            "SELECT COUNT(*) FROM archive_projects WHERE original_id=@id", ("@id", project.Id)).ConfigureAwait(false);
        check(archiveProjectsCount == 1, $"archive_projects has exactly one row for the deleted project #{project.Id}");
        var remainingProjectRows = await ScalarLongAsync(probe, "SELECT COUNT(*) FROM projects WHERE id=@id", ("@id", project.Id)).ConfigureAwait(false);
        check(remainingProjectRows == 0, $"No row survives in projects for the deleted project #{project.Id}");
        var remainingObservationRows = await ScalarLongAsync(probe, "SELECT COUNT(*) FROM project_observations WHERE project_id=@id", ("@id", project.Id)).ConfigureAwait(false);
        check(remainingObservationRows == 0, $"No row survives in project_observations for the deleted project #{project.Id}");
    }

    // ---- Product locks (no write guard - exercised in full) -------------------------------------------------------

    private static async Task CheckProductLocksAsync(IConfiguration configuration, IAccessControl admin,
        Action<bool, string> check, MySqlConnection probe, long productId)
    {
        Console.WriteLine("--- Product locks (MariaProductLockRepository - no write guard) ---");
        var repository = new MariaProductLockRepository(configuration, admin);
        var sessionId = Guid.NewGuid().ToString("N");
        var attempt = await repository.AcquireAsync(checked((int)productId), sessionId).ConfigureAwait(false);
        check(attempt is { Acquired: true, Changed: true }, "Acquiring a free product lock succeeds and reports it as newly taken");

        var held = await repository.GetAsync(checked((int)productId)).ConfigureAwait(false);
        check(held is not null && held.SessionId == sessionId && held.Owner == "integration.tester",
            "GetAsync reflects the lock just acquired, with its owner and session");

        var renewed = await repository.RenewAsync(checked((int)productId), sessionId).ConfigureAwait(false);
        check(renewed is { Acquired: true, Changed: false }, "Renewing the lock from the same session succeeds without changing ownership");

        var released = await repository.ReleaseAsync(checked((int)productId), sessionId).ConfigureAwait(false);
        check(released, "Releasing the lock from the owning session succeeds");

        var afterRelease = await repository.GetAsync(checked((int)productId)).ConfigureAwait(false);
        check(afterRelease is null, "GetAsync no longer reports the lock after it was released");
    }

    // ---- Raw-SQL seeds (only for FK targets whose own repository write path is blocked by the guard bug) --------

    private static async Task<(long CategoryId, long SubcategoryId, long ProductId)> SeedProductAsync(MySqlConnection probe,
        string category, string subcategory, string product)
    {
        var categoryId = await InsertAsync(probe,
            "INSERT INTO categories(name,normalized_name) VALUES(@name,@key)",
            ("@name", category), ("@key", TextNormalization.UniquenessKey(category))).ConfigureAwait(false);
        var subcategoryId = await InsertAsync(probe,
            "INSERT INTO subcategories(category_id,name,normalized_name) VALUES(@category,@name,@key)",
            ("@category", categoryId), ("@name", subcategory), ("@key", TextNormalization.UniquenessKey(subcategory))).ConfigureAwait(false);
        var productId = await InsertAsync(probe, """
            INSERT INTO products(category_id,subcategory_id,name,normalized_name,description,quantity,version)
            VALUES(@category,@subcategory,@name,@key,@description,0,0)
            """, ("@category", categoryId), ("@subcategory", subcategoryId), ("@name", product),
            ("@key", TextNormalization.UniquenessKey(product)), ("@description", "Seed pentru testarea blocarilor de produs")).ConfigureAwait(false);
        return (categoryId, subcategoryId, productId);
    }

    private static async Task<long> SeedBeneficiaryAsync(MySqlConnection probe, string name, string cui) =>
        await InsertAsync(probe,
            "INSERT INTO beneficiaries(name,normalized_name,cui,normalized_cui,version) VALUES(@name,@nameKey,@cui,@cuiKey,0)",
            ("@name", name), ("@nameKey", TextNormalization.UniquenessKey(name)), ("@cui", cui),
            ("@cuiKey", TextNormalization.UniquenessKey(cui))).ConfigureAwait(false);

    private static async Task CleanupSeedsAsync(MySqlConnection probe, long productId, long subcategoryId, long categoryId, long beneficiaryId)
    {
        if (productId > 0) await ExecuteAsync(probe, "DELETE FROM product_locks WHERE product_id=@id", ("@id", productId)).ConfigureAwait(false);
        if (productId > 0) await ExecuteAsync(probe, "DELETE FROM products WHERE id=@id", ("@id", productId)).ConfigureAwait(false);
        if (subcategoryId > 0) await ExecuteAsync(probe, "DELETE FROM subcategories WHERE id=@id", ("@id", subcategoryId)).ConfigureAwait(false);
        if (categoryId > 0) await ExecuteAsync(probe, "DELETE FROM categories WHERE id=@id", ("@id", categoryId)).ConfigureAwait(false);
        if (beneficiaryId > 0) await ExecuteAsync(probe, "DELETE FROM beneficiaries WHERE id=@id", ("@id", beneficiaryId)).ConfigureAwait(false);
    }

    // ---- Small raw-SQL helpers -------------------------------------------------------------------------------

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

    private static async Task<long> InsertAsync(MySqlConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = new MySqlCommand(sql, connection);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        return command.LastInsertedId;
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
