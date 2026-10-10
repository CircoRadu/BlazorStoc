#nullable enable
#pragma warning disable CS1998, CS8600, CS8601, CS8602, CS8603, CS8604, CS8605, CS8618, CS8619, CS8620, CS8625, CS8629, CS8714
using BlazorStoc.Checks;
using BlazorStoc.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PdfSharp.Pdf.IO;
using System.IO.Compression;
using System.Text.Json;

public static partial class FullRun
{
    // Lines 1823-1978 of the former Program.cs.
    internal static async Task BackupAndMaintenanceGateAsync(string[] args)
    {

        // Domain logic (matching, diffing, applying) is tested against fake repositories, independent of the real OCR
        // pipeline above, so these checks stay meaningful even if the sample scan or the embedded model ever change.
        {
            IReadOnlyList<Product> catalog =
            [
                new(1, "Consumabile", "Fixare", "Surub autoforant 4,8 x 25 test", "", 96),
                new(2, "Scule de mana", "Strangere", "Set chei combinate", "", 7),
                new(3, "Masurare", "Nivelare", "Nivela cu bula 60 cm", "", 5),
            ];
            IReadOnlyDictionary<int, int> noVehicleStock = new Dictionary<int, int>();
            var builder = new InventoryPickupBuilder(new FakeInventoryProductRepository(catalog, []), new FakeInventoryPickupMovementRepository(noVehicleStock));

            var exactMatch = await builder.BuildAsync([new InventoryPickupScanRow(1, "Surub autoforant 4,8 x 25 test", 105, false)]);
            Check(exactMatch.Lines.Single().ProductId == 1 && exactMatch.Lines.Single().Difference == 9,
                "An exact name match computes the difference against the current warehouse stock");

            var noDifference = await builder.BuildAsync([new InventoryPickupScanRow(1, "Nivela cu bula 60 cm", 5, false)]);
            Check(noDifference.Lines.Count == 0, "A recognized value equal to the current stock produces no line to act on");

            var uncertainNoDifference = await builder.BuildAsync([new InventoryPickupScanRow(1, "Nivela cu bula 60 cm", 5, true)]);
            Check(uncertainNoDifference.Lines.Count == 1, "An uncertain row is shown for review even when its (possibly wrong) value matches the current stock");

            var fuzzyMatch = await builder.BuildAsync([new InventoryPickupScanRow(1, "Suruh autoforant 4,8 x 25 test", 100, false)]);
            Check(fuzzyMatch.Lines.SingleOrDefault()?.ProductId == 1, "A one-character OCR misread still matches its product by a close, unambiguous name");

            var notFound = await builder.BuildAsync([new InventoryPickupScanRow(1, "Produs care nu exista", 3, false)]);
            Check(notFound.Lines.Count == 0 && notFound.NotFound.Single().RawCode == "Produs care nu exista",
                "A code with no close match in the catalogue is reported separately, not silently matched to something else");

            var pickupRepository = new FakeInventoryPickupMovementRepository(noVehicleStock);
            var applier = new InventoryPickupApplier(pickupRepository, NullLogger<InventoryPickupApplier>.Instance);
            var toApply = new[]
            {
                new InventoryPickupLine(1, "Surub autoforant 4,8 x 25 test", "Surub autoforant 4,8 x 25 test", "Consumabile", "Fixare", 96, 105, false),
                new InventoryPickupLine(2, "Set chei combinate", "Set chei combinate", "Scule de mana", "Strangere", 7, 2, false),
            };
            var applyResult = await applier.ApplyAsync(toApply);
            Check(applyResult.Applied.Count == 2 && applyResult.Failed.Count == 0, "Every selected line with a difference is applied");
            Check(pickupRepository.Created[0].Input.Kind == StockMovementKind.Entry && pickupRepository.Created[0].Input.Quantity == 9,
                "A surplus (real value above current stock) is recorded as an Entry");
            Check(pickupRepository.Created[1].Input.Kind == StockMovementKind.Exit && pickupRepository.Created[1].Input.Destination == ExitDestination.StockCorrection
                  && pickupRepository.Created[1].Input.Quantity == 5, "A shortfall is recorded as an Exit with the existing StockCorrection destination, not a new movement type");

            var failingRepository = new FakeInventoryPickupMovementRepository(noVehicleStock, failProductIds: new HashSet<int> { 2 });
            var partialApplier = new InventoryPickupApplier(failingRepository, NullLogger<InventoryPickupApplier>.Instance);
            var partialResult = await partialApplier.ApplyAsync(toApply);
            Check(partialResult.Applied.Count == 1 && partialResult.Failed.Count == 1,
                "A failure applying one line does not prevent the others from being applied, and is reported back explicitly");
        }

        // Subtask 1.4 (Task 2): pure-logic checks for the database backup mechanism that need no MariaDB connection -
        // package naming, the canonical row-hash building blocks, and the file-based operation lock's heartbeat/expiry.
        Check(BackupNaming.BuildFileName(BackupKind.InventoryPickup, new DateTime(2026, 9, 29, 14, 5, 30), AccessRoles.Administrator, "Ionescu Ștefan")
                == "Copie siguranta preluare inventar 29.09.2026 14-05-30 Administrator Ionescu Stefan.zip",
            "Backup file names follow the required model, in dd.MM.yyyy HH-mm-ss local time, without diacritics");
        Check(BackupNaming.SanitizeToken("a/b:c*d") == "a b c d", "Filesystem-invalid characters in operator name/role become spaces, never break the file name");
        Check(BackupNaming.SanitizeToken("  ") == "necunoscut", "An empty operator token still produces a valid, non-empty file name segment");

        Check(CanonicalRowHasher.CanonicalizeValue(null, 'I') == "NULL", "A null value canonicalizes the same regardless of column type");
        Check(CanonicalRowHasher.CanonicalizeValue(42L, 'I') == "I:42" && CanonicalRowHasher.CanonicalizeValue(42, 'I') == "I:42",
            "Integer values canonicalize identically whether read back as int or long");
        Check(CanonicalRowHasher.CanonicalizeValue(3.5m, 'R') == "R:3.5", "Decimal values canonicalize with an invariant-culture textual form");
        Check(CanonicalRowHasher.CanonicalizeValue("abc", 'T') == "T:abc", "Text values keep a distinct prefix from numeric ones");
        Check(CanonicalRowHasher.PrefixForDataType("int") == 'I' && CanonicalRowHasher.PrefixForDataType("bigint") == 'I' &&
              CanonicalRowHasher.PrefixForDataType("decimal") == 'R' && CanonicalRowHasher.PrefixForDataType("varchar") == 'T' &&
              CanonicalRowHasher.PrefixForDataType("longtext") == 'T' && CanonicalRowHasher.PrefixForDataType("blob") == 'B',
            "Every MariaDB column type used by the migrated schema maps to the expected canonical prefix");
        var sameRowColumns = new[] { new CanonicalColumn("id", 'I'), new CanonicalColumn("name", 'T') };
        var hashA = CanonicalRowHasher.HashRow(sameRowColumns, [1, "Test"]);
        var hashB = CanonicalRowHasher.HashRow(sameRowColumns, [1, "Test"]);
        var hashC = CanonicalRowHasher.HashRow(sameRowColumns, [1, "Different"]);
        Check(hashA == hashB, "The same row always canonicalizes to the same hash");
        Check(hashA != hashC, "A single changed column value changes the row's hash");

        await RunOperationLockChecksAsync();
        async Task RunOperationLockChecksAsync()
        {
            var lockPath = Path.Combine(Path.GetTempPath(), $"blazorstoc-lock-check-{Guid.NewGuid():N}", "operation.lock.json");
            var service = new FileOperationLockService(lockPath);
            try
            {
                var firstHandle = await service.TryAcquireAsync("backup:test", "ionescu", AccessRoles.Administrator);
                Check(firstHandle is not null, "Acquiring a free lock succeeds");
                var secondHandle = await service.TryAcquireAsync("backup:test", "popescu", AccessRoles.LimitedUser);
                Check(secondHandle is null, "A second concurrent request is refused while the lock is held");
                var active = await service.GetActiveAsync();
                Check(active is { OperatorName: "ionescu" }, "The active lock reports the operator that holds it");
                await firstHandle!.DisposeAsync();
                Check(await service.GetActiveAsync() is null, "Releasing the lock (disposing the handle) clears it immediately");
                var thirdHandle = await service.TryAcquireAsync("backup:test", "vasilescu", AccessRoles.Administrator);
                Check(thirdHandle is not null, "The lock can be acquired again once released");
                await thirdHandle!.DisposeAsync();
            }
            finally { try { Directory.Delete(Path.GetDirectoryName(lockPath)!, true); } catch (IOException) { } }
        }

        // Write freeze of a backup/restore (MaintenanceGate): while the shared operation lock is held, every session but the
        // operation itself is refused at the data-access layer; a page navigation is sent to the waiting page.
        await RunMaintenanceGateChecksAsync();
        async Task RunMaintenanceGateChecksAsync()
        {
            // The MariaDB chokepoint (DatabaseConnections.Create, internal): refused for a non-owner while the lock is held, allowed
            // otherwise and for the owner. Creating the connection object opens nothing, so no database is needed.
            {
                var mariaGateRoot = Path.Combine(Path.GetTempPath(), $"blazorstoc-gate-maria-{Guid.NewGuid():N}");
                Directory.CreateDirectory(mariaGateRoot);
                var mariaGateLockPath = Path.Combine(mariaGateRoot, "operation.lock.json");
                var mariaGateLocks = new FileOperationLockService(mariaGateLockPath);
                var createConnection = typeof(FileOperationLockService).Assembly.GetType("BlazorStoc.Services.DatabaseConnections")!
                    .GetMethod("Create", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!;
                var mariaGateConfiguration = new ConfigurationBuilder().Build();
                string TryCreate()
                {
                    try { using var connection = (IDisposable)createConnection.Invoke(null, [mariaGateConfiguration])!; return "created"; }
                    catch (System.Reflection.TargetInvocationException exception) when (exception.InnerException is MaintenanceInProgressException) { return "blocked"; }
                }
                try
                {
                    MaintenanceGate.Configure(mariaGateLockPath);
                    Check(TryCreate() == "created", "MariaDB connections are created normally while no operation runs");
                    var mariaHeld = await mariaGateLocks.TryAcquireAsync("backup:test", "administrator", AccessRoles.Administrator);
                    Check(TryCreate() == "blocked", "A MariaDB connection is refused for a non-owner while an operation holds the lock");
                    using (MaintenanceGate.EnterOwnerScope())
                        Check(TryCreate() == "created", "The operation that holds the lock still gets its own MariaDB connections");
                    await mariaHeld!.DisposeAsync();
                    Check(TryCreate() == "created", "MariaDB connections are created again once the operation ends");
                }
                finally
                {
                    MaintenanceGate.Configure(null);
                    try { Directory.Delete(mariaGateRoot, true); } catch (IOException) { }
                }
            }

            // Page navigations while an operation runs go to the waiting page; the running page's own traffic does not.
            var activeOperation = new OperationLockInfo("backup:test", "administrator", AccessRoles.Administrator, DateTime.UtcNow, DateTime.UtcNow);
            static Microsoft.AspNetCore.Http.HttpRequest PageRequest(string path, string method = "GET", string accept = "text/html,application/xhtml+xml")
            {
                var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
                context.Request.Method = method;
                context.Request.Path = path;
                context.Request.QueryString = new Microsoft.AspNetCore.Http.QueryString("?categorie=Scule");
                context.Request.Headers.Accept = accept;
                return context.Request;
            }
            Check(MaintenanceSupport.ShouldRedirect(PageRequest("/produse"), activeOperation), "A page navigation during an operation is redirected");
            Check(!MaintenanceSupport.ShouldRedirect(PageRequest("/produse"), null), "Nothing is redirected when no operation runs");
            Check(!MaintenanceSupport.ShouldRedirect(PageRequest("/produse", method: "POST"), activeOperation), "Only GET navigations are redirected");
            Check(!MaintenanceSupport.ShouldRedirect(PageRequest("/produse", accept: "application/json"), activeOperation), "A request that does not ask for HTML is not redirected");
            foreach (var passThrough in new[] { "/intretinere", "/api/maintenance", "/Account/Login", "/_blazor", "/_framework/blazor.web.js", "/hubs/changes", "/app.css", "/media/products/3" })
                Check(!MaintenanceSupport.ShouldRedirect(PageRequest(passThrough), activeOperation), $"{passThrough} is never redirected to the waiting page");
            Check(MaintenanceSupport.WaitingUrl(PageRequest("/produse")) == "/intretinere?returnUrl=%2Fproduse%3Fcategorie%3DScule",
                "The waiting page URL remembers where to come back to");
            Check(MaintenanceSupport.SafeReturnUrl("/produse?categorie=Scule") == "/produse?categorie=Scule" &&
                  MaintenanceSupport.SafeReturnUrl("//evil.example") == "/" && MaintenanceSupport.SafeReturnUrl("https://evil.example") == "/" &&
                  MaintenanceSupport.SafeReturnUrl("/intretinere?returnUrl=/x") == "/" && MaintenanceSupport.SafeReturnUrl(null) == "/",
                "The return address is always a local path, never another site or the waiting page itself");
        }
    }
}
