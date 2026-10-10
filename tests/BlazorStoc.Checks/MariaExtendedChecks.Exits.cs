using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using BlazorStoc.Services;

namespace BlazorStoc.Checks;

public static partial class MariaExtendedChecks
{
    private static async Task FreeEntriesAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe)
    {
        var suffix = Suffix();
        var category = $"Ext Libere Cat {suffix}";
        var subcategory = $"Ext Libere Sub {suffix}";
        var products = new MariaProductRepository(configuration, admin, audit);
        var movements = new MariaStockMovementRepository(configuration, admin, audit);
        var suppliers = new MariaSupplierRepository(configuration, admin, audit);
        var invoices = new MariaSupplierInvoiceRepository(configuration, admin, audit);
        var normalUser = new TestAccessControl(false, "utilizator.normal");
        var asUser = new MariaStockMovementRepository(configuration, normalUser, audit);
        var started = DateTime.UtcNow.AddSeconds(-1);
        var today = DateOnly.FromDateTime(DateTime.Now);
        await products.CreateCategoryAsync(category);
        await products.CreateSubcategoryAsync(category, subcategory);
        var p1 = await products.CreateAsync(new ProductInput { Name = $"Ext Libere 1 {suffix}", Category = category, Subcategory = subcategory });
        var p2 = await products.CreateAsync(new ProductInput { Name = $"Ext Libere 2 {suffix}", Category = category, Subcategory = subcategory });
        var supplier = await suppliers.CreateAsync(new SupplierInput { Name = $"Furnizor Liber {suffix}", Cui = SupplierChecks.ValidCui(Random.Shared.Next(1_000_000, 9_999_999)) });
        var other = await suppliers.CreateAsync(new SupplierInput { Name = $"Alt Furnizor Liber {suffix}", Cui = SupplierChecks.ValidCui(Random.Shared.Next(1_000_000, 9_999_999)) });
        var supplierIds = new List<int> { supplier.Id, other.Id };
        var p3 = await products.CreateAsync(new ProductInput { Name = $"Ext Libere 3 {suffix}", Category = category, Subcategory = subcategory });
        try
        {
            StockMovementInput Entry(int quantity, string description, DateOnly? date = null) => new() { Kind = StockMovementKind.Entry, Date = date ?? today, Quantity = quantity, Description = description };

            // Free entries: the reason is recorded; an awaited invoice needs its supplier, the other reasons do not.
            var noSupplier = Entry(2, "Ext fara furnizor"); noSupplier.FreeType = FreeEntryType.AwaitedInvoice;
            await Rejects<StockMovementOperationException>(() => movements.CreateAsync(p1.Id, noSupplier), "An awaited invoice without its supplier is refused");
            var awaitedInput = Entry(2, "Ext factura asteptata", today.AddDays(-20)); awaitedInput.FreeType = FreeEntryType.AwaitedInvoice; awaitedInput.FreeSupplierId = supplier.Id; awaitedInput.Reference = "Aviz 77";
            var awaited = await movements.CreateAsync(p1.Id, awaitedInput);
            var donationInput = Entry(3, "Ext donatie"); donationInput.FreeType = FreeEntryType.Donation;
            var donation = await movements.CreateAsync(p1.Id, donationInput);
            var reread = await movements.GetAsync(awaited.Movement.Id);
            Check(reread is { FreeType: FreeEntryType.AwaitedInvoice, IsAwaitingInvoice: true, Reference: "Aviz 77" } && reread.FreeSupplierId == supplier.Id && reread.FreeSupplierName == supplier.Name && donation.Movement.FreeType == FreeEntryType.Donation && donation.Movement.FreeSupplierId is null && donation.Stock == 5,
                "A free entry keeps its reason, reference and supplier (required for an awaited invoice, optional for the other reasons)");
            var freeEvents = (await audit.GetEventsAsync()).Where(item => item.TimestampUtc >= started && item.EntityType == AuditEntities.StockMovement && item.Action == AuditActions.RecordFreeEntry).ToList();
            Check(freeEvents.Any(item => item.EntityId == awaited.Movement.Id.ToString() && item.Details.Contains("Tip intrare: Achiziție fără factură", StringComparison.Ordinal) && item.Details.Contains(supplier.Name, StringComparison.Ordinal) && item.Details.Contains("Referință: Aviz 77", StringComparison.Ordinal)),
                "The journal names the operation \"Intrare liberă înregistrată\" with the reason, the supplier and the reference");
            Check((await suppliers.GetSuppliersAsync()).Single(item => item.Id == supplier.Id) is { MovementCount: 1, InUse: true }, "A free entry with a supplier ties the supplier (it cannot be deleted)");

            // The list of free entries and its filters.
            var awaitedList = await movements.GetFreeEntriesAsync(new FreeEntryQuery(FreeEntryType.AwaitedInvoice, supplier.Id));
            var old = await movements.GetFreeEntriesAsync(new FreeEntryQuery(OlderThanDays: 14));
            var recent = await movements.GetFreeEntriesAsync(new FreeEntryQuery(FreeEntryType.Donation));
            Check(awaitedList.Count == 1 && awaitedList[0].MovementId == awaited.Movement.Id && awaitedList[0].SupplierName == supplier.Name && old.Any(item => item.MovementId == awaited.Movement.Id) && old.All(item => item.MovementId != donation.Movement.Id) && recent.Any(item => item.MovementId == donation.Movement.Id),
                "Free entries are listed with filters by reason, supplier and age");

            // Notification: one per group (supplier + date), the object closes when the group is tied to an invoice.
            var reader = new MariaAwaitedEntryReader(configuration);
            var second = Entry(4, "Ext factura asteptata 2", today.AddDays(-20)); second.FreeType = FreeEntryType.AwaitedInvoice; second.FreeSupplierId = supplier.Id;
            var awaited2 = await movements.CreateAsync(p2.Id, second);
            var third = Entry(1, "Ext factura asteptata 3", today.AddDays(-3)); third.FreeType = FreeEntryType.AwaitedInvoice; third.FreeSupplierId = supplier.Id;
            var awaited3 = await movements.CreateAsync(p2.Id, third);
            var source = new AwaitedInvoiceSource(reader);
            var mine = (await source.GetInstancesAsync()).Where(item => item.Values[AwaitedInvoiceSource.SupplierName] == supplier.Name).ToList();
            Check(mine.Count == 2 && mine.Single(item => item.Values[AwaitedInvoiceSource.ProductsName] == "2").Expiry == today.AddDays(-20 + AwaitedInvoiceSource.InvoiceWaitDays) && mine.Select(item => item.ObjectId).Distinct().Count() == 2 && mine.All(item => item.Url!.Contains($"furnizor={supplier.Id}", StringComparison.Ordinal)),
                "One notification per group (supplier + date of the entry), not per product, dated at the entry plus the waiting term");

            // Invoice entries: repeated product and overrun are refused unless the user gives a reason.
            var invoice = await invoices.CreateAsync(new SupplierInvoiceInput { SupplierId = supplier.Id, Number = $"FL {suffix}", Date = today.AddDays(-1) });
            var firstLine = Entry(3, "Ext linie 1"); firstLine.InvoiceId = invoice.Id; firstLine.InvoiceQuantity = 5;
            await movements.CreateAsync(p1.Id, firstLine);
            var repeat = Entry(3, "Ext linie 1 din nou"); repeat.InvoiceId = invoice.Id;
            var warning = await Rejects<InvoiceEntryWarningException>(() => movements.CreateAsync(p1.Id, repeat), "The same product taken again from the same invoice is refused without a reason");
            Check(warning is { ExistingQuantity: 3, Exceeds: true } && warning.Message.Contains("deja preluat", StringComparison.Ordinal), "The warning says what was already taken and that the invoice quantity is exceeded");
            var stockBefore = (await movements.GetPageAsync(p1.Id, new StockMovementQuery())).Stock;
            Check(stockBefore == 8, "The refused entry leaves the stock unchanged");
            repeat.DuplicateReason = "Doua colete la receptie";
            var confirmed = await movements.CreateAsync(p1.Id, repeat);
            Check(confirmed.Stock == 11, "With a reason the repeated entry is added");
            var dupEvent = (await audit.GetEventsAsync()).FirstOrDefault(item => item.TimestampUtc >= started && item.Action == AuditActions.DuplicateEntryOnInvoice && item.EntityId == confirmed.Movement.Id.ToString());
            Check(dupEvent is not null && dupEvent.Motif == "Doua colete la receptie", "The journal names \"Intrare dublată pe factură (confirmat)\" with the reason");

            // Tying free entries to an invoice: administrator only, stock unchanged, one event for each entry.
            var stockBeforeLink = (await movements.GetPageAsync(p1.Id, new StockMovementQuery())).Stock;
            await Rejects<AccessDeniedException>(() => asUser.AttachToInvoiceAsync([awaited.Movement], invoice.Id, "Motiv de test"), "A user without the administrator role cannot tie an entry to an invoice");
            var attached = await movements.AttachToInvoiceAsync([awaited.Movement, awaited2.Movement], invoice.Id, "Factura a sosit");
            var afterLink = await movements.GetAsync(awaited.Movement.Id);
            Check(attached.Count == 2 && afterLink is { InvoiceId: var linkedInvoice, FreeType: null, FreeSupplierId: null, Modified: true } && linkedInvoice == invoice.Id && (await movements.GetPageAsync(p1.Id, new StockMovementQuery())).Stock == stockBeforeLink,
                "Free entries are tied to an invoice in one operation; the stock does not change and the free reason is cleared");
            var history = await movements.GetHistoryAsync(awaited.Movement.Id);
            Check(history.Count == 1 && history[0].StockCorrection == 0 && history[0].Reason == "Factura a sosit" && history[0].Changes.Contains("Factură: — →", StringComparison.Ordinal), "The history of the entry shows the tie with its reason and no stock correction");
            var linkEvents = (await audit.GetEventsAsync()).Where(item => item.TimestampUtc >= started && item.Action == AuditActions.AttachEntryToInvoice).ToList();
            Check(linkEvents.Count == 2 && linkEvents.All(item => item.Motif == "Factura a sosit" && item.Details.Contains($"FL {suffix}", StringComparison.Ordinal)), "The journal names \"Atașare intrare la factură\" once for each entry, with the reason");
            await Rejects<StockMovementOperationException>(() => movements.AttachToInvoiceAsync([afterLink!], invoice.Id, "Motiv de test"), "An entry already tied to an invoice cannot be tied again");
            Check((await source.GetInstancesAsync()).Count(item => item.Values[AwaitedInvoiceSource.SupplierName] == supplier.Name) == 1, "When every entry of a group is tied to an invoice its notification object disappears");

            // The pickup (an operator) ties the entry it matched.
            var viaPickup = await asUser.AttachToInvoiceAsync([awaited3.Movement], invoice.Id, "Preluare factură: potrivire automată", true);
            Check(viaPickup.Single().InvoiceId == invoice.Id && (await source.GetInstancesAsync()).All(item => item.Values[AwaitedInvoiceSource.SupplierName] != supplier.Name), "The invoice pickup ties a matched free entry as an operator; the last group disappears");

            // Freeing an entry from its invoice.
            await Rejects<AccessDeniedException>(() => asUser.DetachFromInvoiceAsync(viaPickup.Single(), "Motiv de test"), "A user without the administrator role cannot free an entry from its invoice");
            var detached = await movements.DetachFromInvoiceAsync((await movements.GetAsync(awaited3.Movement.Id))!, "Atasata din greseala");
            Check(detached.InvoiceId is null && (await invoices.GetEntriesAsync(invoice.Id)).All(item => item.MovementId != awaited3.Movement.Id) && (await audit.GetEventsAsync()).Any(item => item.TimestampUtc >= started && item.Action == AuditActions.DetachEntryFromInvoice && item.EntityId == awaited3.Movement.Id.ToString() && item.Motif == "Atasata din greseala"),
                "An entry is freed from its invoice (journal \"Detașare intrare de la factură\" with the reason)");

            // Negative stock is an operating error: an exit above the stock is allowed and marked; the next entry needs the real quantity (or zero).
            var overExit = await movements.CreateAsync(p3.Id, new StockMovementInput { Kind = StockMovementKind.Exit, Date = today, Quantity = 3, Description = "Ext iesire peste stoc", Destination = ExitDestination.GenericSale });
            var overPage = await movements.GetPageAsync(p3.Id, new StockMovementQuery(StockMovementKind.Exit, OverStockOnly: true));
            Check(overExit.Stock == -3 && overPage.Items.Count == 1 && overPage.OverStockIds!.Contains(overExit.Movement.Id) && (await audit.GetEventsAsync()).Any(item => item.TimestampUtc >= started && item.Action == AuditActions.GenericSale && item.Details.Contains("Peste stoc: 3") && item.EntityId == overExit.Movement.Id.ToString()),
                "An exit above the stock is allowed, marked \"peste stoc\" and journaled");
            var fixedToReal = await movements.RegularizeNegativeStockAsync(p3.Id, 2, "Test regularizare");
            Check(fixedToReal.Stock == 2 && fixedToReal.Movement is { FreeType: FreeEntryType.Adjustment, Quantity: 5 } && (await audit.GetEventsAsync()).Any(item => item.TimestampUtc >= started && item.Action == AuditActions.RegularizeNegativeStock && item.EntityId == fixedToReal.Movement.Id.ToString() && item.Details.Contains("Stoc înainte: -3", StringComparison.Ordinal)),
                "Regularizing a negative stock brings the warehouse to the real quantity with a correction entry and a journal event");
            await Rejects<StockMovementOperationException>(() => movements.RegularizeNegativeStockAsync(p3.Id, 2, "din nou"), "A stock that is no longer negative is not regularized");
            await movements.CreateAsync(p3.Id, new StockMovementInput { Kind = StockMovementKind.Exit, Date = today, Quantity = 4, Description = "Ext iesire a doua", Destination = ExitDestination.GenericSale });
            Check((await movements.RegularizeNegativeStockAsync(p3.Id, 0, "Test pe zero")).Stock == 0, "\"Set to zero\" brings a negative stock to exactly zero");

            // Matching of a free entry with an invoice line: same product and quantity, within 30 days, the same supplier first.
            var pool = new List<FreeEntry>
            {
                new(1, 10, "P", 5, today.AddDays(-5), FreeEntryType.AwaitedInvoice, 7, "A", null, "d", "u"),
                new(2, 10, "P", 5, today.AddDays(-9), FreeEntryType.AwaitedInvoice, 8, "B", null, "d", "u"),
                new(3, 10, "P", 6, today.AddDays(-2), FreeEntryType.AwaitedInvoice, 8, "B", null, "d", "u"),
                new(4, 11, "Q", 5, today.AddDays(-45), FreeEntryType.Other, null, null, null, "d", "u")
            };
            Check(FreeEntryMatching.Find(pool, 10, 5, today, 8)?.MovementId == 2 && FreeEntryMatching.Find(pool, 10, 5, today, null)?.MovementId == 1 && FreeEntryMatching.Find(pool, 10, 5, today, 8, new HashSet<int> { 2 })?.MovementId == 1
                  && FreeEntryMatching.Find(pool, 10, 7, today, 8) is null && FreeEntryMatching.Find(pool, 11, 5, today, null) is null,
                "A free entry matches an invoice line by product and quantity within 30 days, the entries of the same supplier first, each used once");
        }
        finally
        {
            await DeleteMovementsNewestFirstAsync(movements, p1.Id);
            await DeleteMovementsNewestFirstAsync(movements, p2.Id);
            await DeleteMovementsNewestFirstAsync(movements, p3.Id);
            await products.DeleteAsync((await products.GetProductAsync(p3.Id))!, "Ext curatare");
            foreach (var id in supplierIds)
            {
                await ExecuteAsync(probe, "DELETE FROM supplier_invoices WHERE supplier_id=@id", ("@id", id));
                await ExecuteAsync(probe, "DELETE FROM suppliers WHERE id=@id", ("@id", id));
            }
            await products.DeleteAsync((await products.GetProductAsync(p1.Id))!, "Ext curatare");
            await products.DeleteAsync((await products.GetProductAsync(p2.Id))!, "Ext curatare");
            await ExecuteAsync(probe, "DELETE FROM subcategories WHERE name=@n", ("@n", subcategory));
            await ExecuteAsync(probe, "DELETE FROM categories WHERE name=@n", ("@n", category));
        }
    }

    private static async Task UsageScenariosAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe)
    {
        var suffix = Suffix();
        var category = $"Ext Scen Cat {suffix}";
        var subcategory = $"Ext Scen Sub {suffix}";
        var products = new MariaProductRepository(configuration, admin, audit);
        var movements = new MariaStockMovementRepository(configuration, admin, audit);
        var suppliers = new MariaSupplierRepository(configuration, admin, audit);
        var invoices = new MariaSupplierInvoiceRepository(configuration, admin, audit);
        var vehicles = new MariaVehicleRepository(configuration, admin, audit);
        var beneficiaries = new MariaBeneficiaryRepository(configuration, admin, audit);
        var projects = new MariaProjectRepository(configuration, new TestWebHostEnvironment(Path.GetTempPath()), admin, audit);
        var today = DateOnly.FromDateTime(DateTime.Now);
        await products.CreateCategoryAsync(category);
        await products.CreateSubcategoryAsync(category, subcategory);
        var pa = await products.CreateAsync(new ProductInput { Name = $"Ext Scen A {suffix}", Category = category, Subcategory = subcategory });
        var pb = await products.CreateAsync(new ProductInput { Name = $"Ext Scen B {suffix}", Category = category, Subcategory = subcategory });
        var pc = await products.CreateAsync(new ProductInput { Name = $"Ext Scen C {suffix}", Category = category, Subcategory = subcategory });
        var supplier = await suppliers.CreateAsync(new SupplierInput { Name = $"Furnizor Scen {suffix}", Cui = SupplierChecks.ValidCui(Random.Shared.Next(1_000_000, 9_999_999)) });
        var car = await vehicles.CreateAsync(new VehicleInput { PlateNumber = "TS-93-" + Letters(), Description = "Ext scenarii", ItpExpiry = Expiry, InsuranceExpiry = Expiry, RovinietaExpiry = Expiry });
        var beneficiary = await beneficiaries.CreateAsync(Legal($"Ext Scen Beneficiar {suffix} SRL", "RO" + Random.Shared.Next(60000000, 69999999)));
        var project = await projects.CreateAsync(new ProjectInput { BeneficiaryId = beneficiary.Id, Name = $"Ext Scen Proiect {suffix}" });
        try
        {
            StockMovementInput In(int quantity, string text, DateOnly? date = null) => new() { Kind = StockMovementKind.Entry, Date = date ?? today, Quantity = quantity, Description = text };
            StockMovementInput Out(int quantity, string text, ExitDestination destination, Action<StockMovementInput>? change = null)
            { var input = new StockMovementInput { Kind = StockMovementKind.Exit, Date = today, Quantity = quantity, Description = text, Destination = destination }; change?.Invoke(input); return input; }
            async Task<int> Stock(int id) => (await movements.GetPageAsync(id, new StockMovementQuery())).Stock;

            // 1, 2, 6: an invoice taken automatically (quantities known), the rest typed by hand on the same invoice; a typed new invoice works the same way.
            var invoice = await invoices.CreateAsync(new SupplierInvoiceInput { SupplierId = supplier.Id, Number = $"SC {suffix}", Date = today.AddDays(-1) });
            var auto = In(6, "Ext linie automata"); auto.InvoiceId = invoice.Id; auto.InvoiceQuantity = 6;
            await movements.CreateAsync(pa.Id, auto);
            var again = await invoices.FindAsync(supplier.Id, $" sc{suffix} ");
            var manual = In(2, "Ext linie manuala"); manual.InvoiceId = again!.Id;
            await movements.CreateAsync(pb.Id, manual);
            var lines = await ScalarLongAsync(probe, "SELECT COUNT(*) FROM supplier_invoice_lines WHERE invoice_id=@id AND product_id=@p AND quantity=6", ("@id", invoice.Id), ("@p", pa.Id));
            Check(again.Id == invoice.Id && (await invoices.GetEntriesAsync(invoice.Id)).Count == 2 && lines == 1 && await Stock(pa.Id) == 6 && await Stock(pb.Id) == 2,
                "An invoice taken partly automatically and finished by hand stays one invoice with all its entries and the invoiced quantity of the automatic line");
            var typed = await invoices.CreateAsync(new SupplierInvoiceInput { SupplierId = supplier.Id, Number = $"MAN {suffix}", Date = today });
            var typedEntry = In(1, "Ext factura tastata"); typedEntry.InvoiceId = typed.Id;
            Check((await movements.CreateAsync(pc.Id, typedEntry)).Movement.InvoiceNumber == $"MAN {suffix}", "An invoice typed by hand takes entries like an automatic one");

            // 10: exit to a beneficiary with a project; 11/12/14: to a vehicle, used from it, returned; the warehouse note counts only the warehouse.
            var toProject = await movements.CreateAsync(pa.Id, Out(1, "Ext iesire beneficiar", ExitDestination.Beneficiary, input => { input.BeneficiaryId = beneficiary.Id; input.ProjectId = project.Id; }));
            Check(toProject.Stock == 5 && toProject.Movement.ProjectName == project.Name && (await movements.GetForProjectAsync(project.Id)).Count == 1, "An exit to a beneficiary with a project lowers the stock and shows on the project");
            await movements.CreateAsync(pa.Id, Out(3, "Ext spre masina", ExitDestination.Vehicle, input => input.VehicleId = car.Id));
            var afterTransfer = await movements.GetPageAsync(pa.Id, new StockMovementQuery());
            Check(afterTransfer.Stock == 5 && afterTransfer.InVehicles == 3 && StockMovementRules.WarehouseStock(afterTransfer.Stock, afterTransfer.InVehicles) == 2,
                "A transfer into a vehicle keeps the total stock; the warehouse holds the rest");
            var useFromCar = await movements.CreateAsync(pa.Id, Out(1, "Ext folosit din masina", ExitDestination.GenericSale, input => input.SourceVehicleId = car.Id));
            var returned = await movements.TransferFromVehicleAsync(new VehicleTransfer(car.Id, null));
            Check(useFromCar.Stock == 4 && returned.Count == 1 && returned[0].Quantity == 2 && (await movements.GetPageAsync(pa.Id, new StockMovementQuery())).InVehicles == 0, "Use from a vehicle lowers the stock; returning the rest puts it back in the warehouse");

            // 13, 15, 20: an exit above the warehouse stock is allowed; a backdated entry heals its mark; vehicles count in the total, not in the warehouse.
            var oldExit = await movements.CreateAsync(pb.Id, Out(5, "Ext iesire neoperata", ExitDestination.GenericSale, input => input.Date = today.AddDays(-3)));
            Check(oldExit.Stock == -3 && (await movements.GetPageAsync(pb.Id, new StockMovementQuery(StockMovementKind.Exit))).OverStockIds!.Contains(oldExit.Movement.Id), "An exit above the stock is accepted and marked");
            await movements.CreateAsync(pb.Id, In(10, "Ext intrare neoperata cu data reala", today.AddDays(-10)));
            Check(!(await movements.GetPageAsync(pb.Id, new StockMovementQuery(StockMovementKind.Exit))).OverStockIds!.Contains(oldExit.Movement.Id) && await Stock(pb.Id) == 7,
                "An entry recorded later with its real (earlier) date removes the \"peste stoc\" mark of the exit");
            var vehicleHeld = await movements.CreateAsync(pb.Id, Out(2, "Ext in masina", ExitDestination.Vehicle, input => input.VehicleId = car.Id));
            var overWarehouse = await movements.CreateAsync(pb.Id, Out(6, "Ext peste depozit", ExitDestination.GenericSale));
            var overPage = await movements.GetPageAsync(pb.Id, new StockMovementQuery(StockMovementKind.Exit));
            Check(vehicleHeld.Stock == 7 && overWarehouse.Stock == 1 && overPage.InVehicles == 2 && overPage.OverStockIds!.Contains(overWarehouse.Movement.Id),
                "An exit above what the warehouse holds is marked even when the vehicles make the total look sufficient");
            await movements.TransferFromVehicleAsync(new VehicleTransfer(car.Id, null));

            // 16-22: regularization of a negative stock before the next entry.
            await movements.CreateAsync(pc.Id, Out(4, "Ext iesire uitata", ExitDestination.GenericSale));
            var carry = await movements.CreateAsync(pc.Id, Out(1, "Ext inca o iesire", ExitDestination.GenericSale));
            Check(carry.Stock == -4, "Two exits above the stock leave a negative stock");
            await movements.CreateAsync(pc.Id, Out(2, "Ext spre masina", ExitDestination.Vehicle, input => input.VehicleId = car.Id));   // the vehicle holds 2: totals include it
            var fixedWithVehicle = await movements.RegularizeNegativeStockAsync(pc.Id, 3, "Test cu masina");
            var paged = await movements.GetPageAsync(pc.Id, new StockMovementQuery());
            Check(fixedWithVehicle.Stock == 3 + 2 && StockMovementRules.WarehouseStock(paged.Stock, paged.InVehicles) == 3, "The real quantity is the warehouse's: the stock total adds what vehicles hold");
            await movements.TransferFromVehicleAsync(new VehicleTransfer(car.Id, null));
            // Two sessions regularizing the same negative stock at once: exactly one succeeds.
            await movements.CreateAsync(pc.Id, Out(20, "Ext iar peste stoc", ExitDestination.GenericSale));
            var race = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
            {
                try { await new MariaStockMovementRepository(configuration, admin, audit).RegularizeNegativeStockAsync(pc.Id, 1, "Test paralel"); return true; }
                catch (StockMovementOperationException) { return false; }
            }));
            Check(race.Count(item => item) == 1 && await Stock(pc.Id) == 1, "Two sessions regularizing the same negative stock: one correction, stock at the real quantity");
            var afterFix = await movements.CreateAsync(pc.Id, In(5, "Ext intrare dupa regularizare"));
            Check(afterFix.Stock == 6, "After regularization the next entry adds to the real quantity");

            // 23: parallel exits of the same product from independent sessions never lose a decrement.
            var before = await Stock(pa.Id);
            await Task.WhenAll(Enumerable.Range(0, 4).Select(index => new MariaStockMovementRepository(configuration, admin, audit)
                .CreateAsync(pa.Id, Out(1, $"Ext paralel {index}", ExitDestination.GenericSale, input => input.DuplicateReason = "Test iesiri paralele"))));
            Check(await Stock(pa.Id) == before - 4, "Four parallel exits lower the stock by exactly four");

            // 24: deleting an entry that was tied to an invoice keeps the invoice and lowers the stock.
            var tied = (await movements.GetPageAsync(pb.Id, new StockMovementQuery(StockMovementKind.Entry, PageSize: 50))).Items.Single(item => item.InvoiceId == invoice.Id);
            var stockBeforeDelete = await Stock(pb.Id);
            await movements.DeleteAsync(tied, "Ext stergere intrare cu factura");
            Check(await Stock(pb.Id) == stockBeforeDelete - tied.Quantity && (await invoices.FindAsync(supplier.Id, $"SC {suffix}")) is not null && (await invoices.GetEntriesAsync(invoice.Id)).All(entry => entry.MovementId != tied.Id),
                "Deleting an entry tied to an invoice lowers the stock and leaves the invoice");
        }
        finally
        {
            foreach (var id in new[] { pa.Id, pb.Id, pc.Id }) await DeleteMovementsNewestFirstAsync(movements, id);
            await ExecuteAsync(probe, "DELETE FROM supplier_invoices WHERE supplier_id=@id", ("@id", supplier.Id));
            await ExecuteAsync(probe, "DELETE FROM suppliers WHERE id=@id", ("@id", supplier.Id));
            await projects.DeleteAsync(project, "Ext curatare");
            await beneficiaries.DeleteAsync((await beneficiaries.GetBeneficiariesAsync()).First(item => item.Id == beneficiary.Id), "Ext curatare");
            await vehicles.DeleteAsync((await vehicles.GetVehiclesAsync()).First(item => item.Id == car.Id), "Ext curatare");
            foreach (var id in new[] { pa.Id, pb.Id, pc.Id }) await products.DeleteAsync((await products.GetProductAsync(id))!, "Ext curatare");
            await ExecuteAsync(probe, "DELETE FROM subcategories WHERE name=@n", ("@n", subcategory));
            await ExecuteAsync(probe, "DELETE FROM categories WHERE name=@n", ("@n", category));
        }
    }

    private static async Task ExitFlowAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe)
    {
        var suffix = Suffix();
        var started = DateTime.UtcNow.AddSeconds(-1);
        var category = $"Ext Out Cat {suffix}";
        var subcategory = $"Ext Out Sub {suffix}";
        var products = new MariaProductRepository(configuration, admin, audit);
        var movements = new MariaStockMovementRepository(configuration, admin, audit);
        var vehicles = new MariaVehicleRepository(configuration, admin, audit);
        var beneficiaries = new MariaBeneficiaryRepository(configuration, admin, audit);
        var today = DateOnly.FromDateTime(DateTime.Now);
        await products.CreateCategoryAsync(category);
        await products.CreateSubcategoryAsync(category, subcategory);
        var product = await products.CreateAsync(new ProductInput { Name = $"Ext Out A {suffix}", Category = category, Subcategory = subcategory });
        var beneficiary = await beneficiaries.CreateAsync(Legal($"Ext Out Beneficiar {suffix} SRL", "RO" + Random.Shared.Next(60000000, 69999999)));
        var car = await vehicles.CreateAsync(new VehicleInput { PlateNumber = "TS-94-" + Letters(), Description = "Ext iesiri", ItpExpiry = Expiry, InsuranceExpiry = Expiry, RovinietaExpiry = Expiry });
        var car2 = await vehicles.CreateAsync(new VehicleInput { PlateNumber = "TS-95-" + Letters(), Description = "Ext iesiri 2", ItpExpiry = Expiry, InsuranceExpiry = Expiry, RovinietaExpiry = Expiry });
        try
        {
            StockMovementInput Out(int quantity, string text, ExitDestination destination, Action<StockMovementInput>? change = null)
            { var input = new StockMovementInput { Kind = StockMovementKind.Exit, Date = today, Quantity = quantity, Description = text, Destination = destination }; change?.Invoke(input); return input; }
            async Task<bool> Journaled(string action, int id, string? detail = null) => (await audit.GetEventsAsync()).Any(item => item.TimestampUtc >= started && item.Action == action
                && item.EntityId == id.ToString() && (detail is null || item.Details.Contains(detail)));
            await movements.CreateAsync(product.Id, new StockMovementInput { Kind = StockMovementKind.Entry, Date = today.AddDays(-2), Quantity = 40, Description = "Ext stoc initial" });

            // The journal names the destination of every exit.
            var toBeneficiary = await movements.CreateAsync(product.Id, Out(1, "Ext spre beneficiar", ExitDestination.Beneficiary, input => { input.BeneficiaryId = beneficiary.Id; input.Reference = "Aviz 77"; }));
            var toVehicle = await movements.CreateAsync(product.Id, Out(5, "Ext spre masina", ExitDestination.Vehicle, input => input.VehicleId = car.Id));
            var sale = await movements.CreateAsync(product.Id, Out(2, "Ext vanzare", ExitDestination.GenericSale));
            var correction = await movements.CreateAsync(product.Id, Out(3, "Ext corectie", ExitDestination.StockCorrection));
            Check(await Journaled(AuditActions.ExitToBeneficiary, toBeneficiary.Movement.Id, "Aviz 77") && await Journaled(AuditActions.ExitToVehicle, toVehicle.Movement.Id)
                  && await Journaled(AuditActions.GenericSale, sale.Movement.Id) && await Journaled(AuditActions.StockCorrection, correction.Movement.Id),
                "The journal names each exit by its destination (beneficiary, vehicle, generic sale, stock correction) and shows the reference");

            // The reference of an exit is kept, shown and editable.
            var stored = (await movements.GetAsync(toBeneficiary.Movement.Id))!;
            var edited = StockMovementInput.From(stored); edited.Reference = "Aviz 78"; edited.Reason = "Ext corectare referinta";
            var afterEdit = await movements.UpdateAsync(stored, edited);
            Check(stored.Reference == "Aviz 77" && afterEdit.Movement.Reference == "Aviz 78" && (await movements.GetAsync(stored.Id))!.Reference == "Aviz 78", "An exit keeps its reference and the reference can be corrected");

            // Filters on exits.
            var all = await movements.GetPageAsync(product.Id, new StockMovementQuery(StockMovementKind.Exit, PageSize: 50));
            var byDestination = await movements.GetPageAsync(product.Id, new StockMovementQuery(StockMovementKind.Exit, PageSize: 50, Destination: ExitDestination.GenericSale));
            var byBeneficiary = await movements.GetPageAsync(product.Id, new StockMovementQuery(StockMovementKind.Exit, PageSize: 50, BeneficiaryId: beneficiary.Id));
            var byVehicle = await movements.GetPageAsync(product.Id, new StockMovementQuery(StockMovementKind.Exit, PageSize: 50, VehicleId: car.Id));
            Check(all.Items.Count == 4 && byDestination.Items.Single().Id == sale.Movement.Id && byBeneficiary.Items.Single().Id == toBeneficiary.Movement.Id && byVehicle.Items.Single().Id == toVehicle.Movement.Id
                  && all.ExitBeneficiaries!.Single().Id == beneficiary.Id && all.ExitVehicles!.Single().Id == car.Id,
                "The exits can be filtered by destination, beneficiary and vehicle; the page lists the beneficiaries and vehicles to choose from");

            // The same exit twice the same day asks for a reason.
            var first = Out(4, "Ext iesire repetata", ExitDestination.Beneficiary, input => input.BeneficiaryId = beneficiary.Id);
            await movements.CreateAsync(product.Id, first);
            var warning = await Rejects<DuplicateExitWarningException>(() => movements.CreateAsync(product.Id, Out(4, "Ext iesire repetata", ExitDestination.Beneficiary, input => input.BeneficiaryId = beneficiary.Id)), "The same exit repeated the same day is refused without a reason");
            var different = await movements.CreateAsync(product.Id, Out(4, "Ext alta cantitate decat cea repetata", ExitDestination.GenericSale));
            var confirmed = await movements.CreateAsync(product.Id, Out(4, "Ext iesire repetata cu motiv", ExitDestination.Beneficiary, input => { input.BeneficiaryId = beneficiary.Id; input.DuplicateReason = "Doua livrari diferite"; }));
            Check(warning is not null && different.Movement.Id > 0 && await Journaled(AuditActions.DuplicateExit, confirmed.Movement.Id), "A repeated exit is added with a reason and journaled as confirmed; a different destination is not a repeat");

            // Using from a vehicle above what it holds: allowed (use), marked, journaled; moves and returns stay checked.
            var held = (await movements.GetVehicleStocksAsync(product.Id)).Single(item => item.VehicleId == car.Id).Quantity;
            var overUse = await movements.CreateAsync(product.Id, Out(held + 3, "Ext folosit peste cat era in masina", ExitDestination.GenericSale, input => input.SourceVehicleId = car.Id));
            var page = await movements.GetPageAsync(product.Id, new StockMovementQuery(StockMovementKind.Exit, PageSize: 50, OverStockOnly: true));
            Check(page.Items.Count == 1 && page.OverStockIds!.Contains(overUse.Movement.Id) && await Journaled(AuditActions.GenericSale, overUse.Movement.Id, "Peste stoc: 3")
                  && (await movements.GetVehicleStocksAsync(product.Id)).All(item => item.VehicleId != car.Id),
                "Using more from a vehicle than it holds is recorded, marked \"peste stoc\" and journaled; the vehicle page lists only positive quantities");
            await Rejects<StockMovementOperationException>(() => movements.TransferFromVehicleAsync(new VehicleTransfer(car.Id, car2.Id, [new VehicleTransferLine(product.Id, 1)])), "A transfer out of a vehicle that holds nothing stays refused");
            await Rejects<StockMovementOperationException>(() => movements.CreateAsync(product.Id, Out(1, "Ext restituire fara nimic", ExitDestination.WarehouseReturn, input => input.SourceVehicleId = car.Id)), "A return from a vehicle that holds nothing stays refused");
            // Another transfer into the vehicle and out of it is not blocked by the earlier overrun.
            await movements.CreateAsync(product.Id, Out(2, "Ext din nou spre masina", ExitDestination.Vehicle, input => input.VehicleId = car2.Id));
            Check((await movements.TransferFromVehicleAsync(new VehicleTransfer(car2.Id, null))).Count == 1, "A negative vehicle quantity does not block other moves of the product");
        }
        finally
        {
            await DeleteMovementsNewestFirstAsync(movements, product.Id);
            await vehicles.DeleteAsync((await vehicles.GetVehiclesAsync()).First(item => item.Id == car.Id), "Ext curatare");
            await vehicles.DeleteAsync((await vehicles.GetVehiclesAsync()).First(item => item.Id == car2.Id), "Ext curatare");
            await beneficiaries.DeleteAsync((await beneficiaries.GetBeneficiariesAsync()).First(item => item.Id == beneficiary.Id), "Ext curatare");
            await products.DeleteAsync((await products.GetProductAsync(product.Id))!, "Ext curatare");
            await ExecuteAsync(probe, "DELETE FROM subcategories WHERE name=@n", ("@n", subcategory));
            await ExecuteAsync(probe, "DELETE FROM categories WHERE name=@n", ("@n", category));
        }
    }

    private static async Task ToRegularizeAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe)
    {
        var suffix = Suffix();
        var started = DateTime.UtcNow.AddSeconds(-1);
        var category = $"Ext Reg Cat {suffix}";
        var subcategory = $"Ext Reg Sub {suffix}";
        var products = new MariaProductRepository(configuration, admin, audit);
        var movements = new MariaStockMovementRepository(configuration, admin, audit);
        var today = DateOnly.FromDateTime(DateTime.Now);
        await products.CreateCategoryAsync(category);
        await products.CreateSubcategoryAsync(category, subcategory);
        var product = await products.CreateAsync(new ProductInput { Name = $"Ext Reg A {suffix}", Category = category, Subcategory = subcategory });
        try
        {
            StockMovementInput Out(int quantity, OverStockCause? cause) =>
                new() { Kind = StockMovementKind.Exit, Date = today, Quantity = quantity, Description = $"Ext reg iesire {quantity}", Destination = ExitDestination.GenericSale, OverStockCause = cause };
            await movements.CreateAsync(product.Id, new StockMovementInput { Kind = StockMovementKind.Entry, Date = today.AddDays(-20), Quantity = 5, Description = "Ext reg stoc initial" });

            // 1. The cause is kept and journaled for an exit over the stock, and dropped for an exit that is not over it.
            var within = await movements.CreateAsync(product.Id, Out(2, OverStockCause.WrongStock));
            var over = await movements.CreateAsync(product.Id, Out(6, OverStockCause.UnrecordedEntry));
            Check(within.Movement.OverStockCause is null && (await movements.GetAsync(within.Movement.Id))!.OverStockCause is null
                  && over.Movement.OverStockCause == OverStockCause.UnrecordedEntry && (await movements.GetAsync(over.Movement.Id))!.OverStockCause == OverStockCause.UnrecordedEntry
                  && (await audit.GetEventsAsync()).Any(item => item.TimestampUtc >= started && item.EntityId == over.Movement.Id.ToString() && item.Details.Contains("Cauza peste stoc: Intrare neoperată")),
                "The cause of an exit over the stock is saved and journaled; an exit within the stock keeps none");

            // 2. The product is listed, with the cause and the date of the first uncovered exit; the filters narrow the list.
            var listed = (await movements.GetToRegularizeAsync()).SingleOrDefault(item => item.ProductId == product.Id);
            var wrongCause = await movements.GetToRegularizeAsync(new RegularizationQuery(Cause: OverStockCause.WrongStock));
            var rightCause = await movements.GetToRegularizeAsync(new RegularizationQuery(Cause: OverStockCause.UnrecordedEntry, OlderThanDays: 0));
            Check(listed is { Stock: -3, CanRegularize: true, OverStockExits: 1, Cause: OverStockCause.UnrecordedEntry } && listed.Since == today
                  && wrongCause.All(item => item.ProductId != product.Id) && rightCause.Any(item => item.ProductId == product.Id),
                "A product with negative stock after an exit over the stock is listed with its cause; the cause filter narrows the list");

            // 3. The notification source sees it while negative.
            var source = new OverStockSource(new MariaOverStockReader(configuration));
            Check((await source.GetInstancesAsync()).Any(item => item.ObjectId == product.Id && item.Expiry == today.AddDays(OverStockSource.RegularizeWithinDays)),
                "The notification source reports the product while its stock is negative");

            // 4. Regularizing from the list closes the case.
            await movements.RegularizeNegativeStockAsync(product.Id, 0, "Ext reg din lista");
            Check((await movements.GetToRegularizeAsync()).All(item => item.ProductId != product.Id) && (await source.GetInstancesAsync()).All(item => item.ObjectId != product.Id),
                "After the regularization the product leaves the list and the notification source");
        }
        finally
        {
            await DeleteMovementsNewestFirstAsync(movements, product.Id);
            await products.DeleteAsync((await products.GetProductAsync(product.Id))!, "Ext curatare");
            await ExecuteAsync(probe, "DELETE FROM subcategories WHERE name=@n", ("@n", subcategory));
            await ExecuteAsync(probe, "DELETE FROM categories WHERE name=@n", ("@n", category));
        }
    }

    private static async Task ExitOperationAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe)
    {
        var suffix = Suffix();
        var started = DateTime.UtcNow.AddSeconds(-1);
        var category = $"Ext Op Cat {suffix}";
        var subcategory = $"Ext Op Sub {suffix}";
        var products = new MariaProductRepository(configuration, admin, audit);
        var movements = new MariaStockMovementRepository(configuration, admin, audit);
        var vehicles = new MariaVehicleRepository(configuration, admin, audit);
        var today = DateOnly.FromDateTime(DateTime.Now);
        await products.CreateCategoryAsync(category);
        await products.CreateSubcategoryAsync(category, subcategory);
        var first = await products.CreateAsync(new ProductInput { Name = $"Ext Op A {suffix}", Category = category, Subcategory = subcategory });
        var second = await products.CreateAsync(new ProductInput { Name = $"Ext Op B {suffix}", Category = category, Subcategory = subcategory });
        var car = await vehicles.CreateAsync(new VehicleInput { PlateNumber = "TS-96-" + Letters(), Description = "Ext operatie", ItpExpiry = Expiry, InsuranceExpiry = Expiry, RovinietaExpiry = Expiry });
        try
        {
            ExitOperationLine Line(int productId, int quantity, string text, Action<StockMovementInput>? change = null)
            {
                var input = new StockMovementInput { Kind = StockMovementKind.Exit, Date = today, Quantity = quantity, Description = text, Destination = ExitDestination.GenericSale, Reference = "Aviz op" };
                change?.Invoke(input);
                return new ExitOperationLine(productId, input);
            }
            await movements.CreateAsync(first.Id, new StockMovementInput { Kind = StockMovementKind.Entry, Date = today.AddDays(-2), Quantity = 10, Description = "Ext op stoc A" });
            await movements.CreateAsync(second.Id, new StockMovementInput { Kind = StockMovementKind.Entry, Date = today.AddDays(-2), Quantity = 10, Description = "Ext op stoc B" });

            // 1. One operation groups the lines (the id of the first exit), each exit is journaled with it; a single exit is an operation of one line.
            var result = await movements.CreateExitOperationAsync([Line(first.Id, 3, "Ext linia 1"), Line(second.Id, 4, "Ext linia 2")]);
            var single = await movements.CreateAsync(first.Id, new StockMovementInput { Kind = StockMovementKind.Exit, Date = today, Quantity = 1, Description = "Ext iesire simpla", Destination = ExitDestination.StockCorrection });
            var events = await audit.GetEventsAsync();
            Check(result.Movements.Count == 2 && result.OperationId == result.Movements[0].Movement.Id && result.Movements.All(item => item.Movement.OperationId == result.OperationId)
                  && single.Movement.OperationId == single.Movement.Id && (await movements.GetAsync(result.Movements[1].Movement.Id))!.OperationId == result.OperationId
                  && result.Movements.All(item => events.Any(entry => entry.TimestampUtc >= started && entry.Action == AuditActions.GenericSale && entry.EntityId == item.Movement.Id.ToString() && entry.Details.Contains($"Operație: #{result.OperationId}"))),
                "An exit operation gives its lines one operation id and each exit is journaled with it; a single exit is an operation of one line");

            // 2. All or nothing: a refused line (a return from a vehicle holding nothing) saves none of the lines.
            var stockBefore = (await products.GetProductAsync(first.Id))!.Quantity;
            var countBefore = (await movements.GetPageAsync(first.Id, new StockMovementQuery(PageSize: 50))).TotalCount;
            var missing = await Rejects<StockMovementOperationException>(() => movements.CreateExitOperationAsync([Line(first.Id, 2, "Ext linia buna"), Line(int.MaxValue, 1, "Ext produs inexistent")]),
                "An operation with a missing product is refused");
            Check(missing is not null && missing.Message.StartsWith("Linia 2") && (await products.GetProductAsync(first.Id))!.Quantity == stockBefore
                  && (await movements.GetPageAsync(first.Id, new StockMovementQuery(PageSize: 50))).TotalCount == countBefore,
                "A refused line names its number and none of the lines is saved");

            // 3. The preview reports the warnings of each line (part over the stock, repeated exit) and saves nothing.
            await movements.CreateExitOperationAsync([Line(second.Id, 1, "Ext iesire repetata in operatie")]);
            var countB = (await movements.GetPageAsync(second.Id, new StockMovementQuery(PageSize: 50))).TotalCount;
            var stockB = (await products.GetProductAsync(second.Id))!.Quantity;
            var previewLines = await movements.PreviewExitOperationAsync([Line(first.Id, 50, "Ext peste stoc"), Line(second.Id, 1, "Ext iesire repetata in operatie")]);
            Check(previewLines.Count == 2 && previewLines[0].OverStock > 0 && !previewLines[0].Duplicate && previewLines[1].Duplicate
                  && (await movements.GetPageAsync(second.Id, new StockMovementQuery(PageSize: 50))).TotalCount == countB && (await products.GetProductAsync(second.Id))!.Quantity == stockB,
                "The preview reports the part over the stock and the repeated exit of each line and saves nothing");

            // 4. A repeated line needs a reason (nothing is saved without it); with it the operation is saved and the line over the stock is marked.
            var warning = await Rejects<DuplicateExitWarningException>(() => movements.CreateExitOperationAsync(
                [Line(first.Id, 50, "Ext peste stoc"), Line(second.Id, 1, "Ext iesire repetata in operatie")]), "A repeated line without a reason is refused");
            var confirmedOp = await movements.CreateExitOperationAsync([Line(first.Id, 50, "Ext peste stoc", input => input.OverStockCause = OverStockCause.WrongStock),
                Line(second.Id, 1, "Ext iesire repetata in operatie", input => input.DuplicateReason = "Doua livrari diferite")]);
            var overPage = await movements.GetPageAsync(first.Id, new StockMovementQuery(StockMovementKind.Exit, PageSize: 50, OverStockOnly: true));
            Check(warning is not null && warning.Message.StartsWith("Linia 2") && confirmedOp.Movements.Count == 2
                  && overPage.Items.Any(item => item.Id == confirmedOp.Movements[0].Movement.Id && item.OverStockCause == OverStockCause.WrongStock),
                "A repeated line is saved with its reason and the line over the stock is marked with its cause");
        }
        finally
        {
            await DeleteMovementsNewestFirstAsync(movements, first.Id);
            await DeleteMovementsNewestFirstAsync(movements, second.Id);
            await vehicles.DeleteAsync((await vehicles.GetVehiclesAsync()).First(item => item.Id == car.Id), "Ext curatare");
            await products.DeleteAsync((await products.GetProductAsync(first.Id))!, "Ext curatare");
            await products.DeleteAsync((await products.GetProductAsync(second.Id))!, "Ext curatare");
            await ExecuteAsync(probe, "DELETE FROM subcategories WHERE name=@n", ("@n", subcategory));
            await ExecuteAsync(probe, "DELETE FROM categories WHERE name=@n", ("@n", category));
        }
    }

    private static async Task StornoReturnNoteAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe)
    {
        var suffix = Suffix();
        var started = DateTime.UtcNow.AddSeconds(-1);
        var category = $"Ext Sto Cat {suffix}";
        var subcategory = $"Ext Sto Sub {suffix}";
        var products = new MariaProductRepository(configuration, admin, audit);
        var movements = new MariaStockMovementRepository(configuration, admin, audit);
        var beneficiaries = new MariaBeneficiaryRepository(configuration, admin, audit);
        var projects = new MariaProjectRepository(configuration, new TestWebHostEnvironment(Path.GetTempPath()), admin, audit);
        var today = DateOnly.FromDateTime(DateTime.Now);
        await products.CreateCategoryAsync(category);
        await products.CreateSubcategoryAsync(category, subcategory);
        var first = await products.CreateAsync(new ProductInput { Name = $"Ext Sto A {suffix}", Category = category, Subcategory = subcategory });
        var second = await products.CreateAsync(new ProductInput { Name = $"Ext Sto B {suffix}", Category = category, Subcategory = subcategory });
        var beneficiary = await beneficiaries.CreateAsync(Legal($"Ext Sto Beneficiar {suffix} SRL", "RO" + Random.Shared.Next(60000000, 69999999)));
        var project = await projects.CreateAsync(new ProjectInput { BeneficiaryId = beneficiary.Id, Name = $"Ext Sto Proiect {suffix}" });
        try
        {
            ExitOperationLine Line(int productId, int quantity, string text) => new(productId, new StockMovementInput
            {
                Kind = StockMovementKind.Exit, Date = today, Quantity = quantity, Description = text, Destination = ExitDestination.Beneficiary,
                BeneficiaryId = beneficiary.Id, ProjectId = project.Id, Reference = "Aviz sto"
            });
            async Task<int> Stock(int id) => (await products.GetProductAsync(id))!.Quantity;
            await movements.CreateAsync(first.Id, new StockMovementInput { Kind = StockMovementKind.Entry, Date = today.AddDays(-2), Quantity = 10, Description = "Ext sto stoc A" });
            await movements.CreateAsync(second.Id, new StockMovementInput { Kind = StockMovementKind.Entry, Date = today.AddDays(-2), Quantity = 10, Description = "Ext sto stoc B" });

            // 1. Storno: the exits stay as a trace (marked), the stock comes back, it is journaled, and a voided exit can no longer be changed.
            var voidedOperation = await movements.CreateExitOperationAsync([Line(first.Id, 5, "Ext linia 1"), Line(second.Id, 3, "Ext linia 2")]);
            var stockAfterExit = (await Stock(first.Id), await Stock(second.Id));
            await movements.VoidExitOperationAsync(voidedOperation.OperationId, "Ext operatie gresita");
            var trace = (await movements.GetAsync(voidedOperation.Movements[0].Movement.Id))!;
            var voidRejected = await Rejects<StockMovementOperationException>(() => movements.VoidExitOperationAsync(voidedOperation.OperationId, "Ext a doua oara"), "A voided operation cannot be voided again");
            var editRejected = await Rejects<StockMovementOperationException>(() => movements.UpdateAsync(trace, StockMovementInput.From(trace)), "A voided exit cannot be edited");
            Check(stockAfterExit == (5, 7) && await Stock(first.Id) == 10 && await Stock(second.Id) == 10 && trace.IsVoided && trace.VoidReason == "Ext operatie gresita"
                  && voidRejected is not null && editRejected is not null
                  && (await audit.GetEventsAsync()).Any(item => item.TimestampUtc >= started && item.Action == AuditActions.VoidExitOperation && item.Details.Contains($"#{voidedOperation.OperationId}")),
                "A storno cancels all exits of the operation, restores the stock, keeps the exits as a trace and is journaled");

            // 2. Partial returns tied to an exit; more than what is left is refused; an operation with returns cannot be voided.
            var operation = await movements.CreateExitOperationAsync([Line(first.Id, 5, "Ext livrare")]);
            var exitId = operation.Movements[0].Movement.Id;
            StockMovementInput Return(int quantity) => new() { Kind = StockMovementKind.Entry, Date = today, Quantity = quantity, Description = "Ext retur", FreeType = FreeEntryType.FromBeneficiary, ReturnOfMovementId = exitId };
            var firstReturn = await movements.CreateAsync(first.Id, Return(2));
            var tooMuch = await Rejects<StockMovementOperationException>(() => movements.CreateAsync(first.Id, Return(4)), "A return above what is left is refused");
            var secondReturn = await movements.CreateAsync(first.Id, Return(3));
            var voidWithReturns = await Rejects<StockMovementOperationException>(() => movements.VoidExitOperationAsync(operation.OperationId, "Ext cu retururi"), "An operation with returns cannot be voided");
            var returnable = await movements.GetReturnableExitsAsync(first.Id);
            Check(firstReturn.Movement.ReturnOfMovementId == exitId && secondReturn.Movement.ReturnOfMovementId == exitId && tooMuch is not null && voidWithReturns is not null
                  && await Stock(first.Id) == 10 && returnable.All(item => item.MovementId != exitId)
                  && (await audit.GetEventsAsync()).Any(item => item.TimestampUtc >= started && item.Action == AuditActions.ReturnFromBeneficiary && item.EntityId == firstReturn.Movement.Id.ToString()),
                "A partial return is tied to its exit, the quantity left is enforced, the stock follows and the return is journaled");

            // 3. Net consumption of the project and of the beneficiary: exits minus returns (the voided operation does not count).
            await movements.CreateExitOperationAsync([Line(second.Id, 4, "Ext livrare B")]);
            var byProject = await movements.GetNetConsumptionAsync(project.Id, null);
            var byBeneficiary = await movements.GetNetConsumptionAsync(null, beneficiary.Id);
            Check(byProject.Count == 2 && byProject.Single(item => item.ProductId == first.Id) is { Exited: 5, Returned: 5, Net: 0 } && byProject.Single(item => item.ProductId == second.Id) is { Exited: 4, Returned: 0, Net: 4 }
                  && byBeneficiary.Sum(item => item.Net) == 4,
                "The net consumption of a project and of a beneficiary is exits minus returns, without voided operations");

            // 4. The consumption note: the operation as printed (recipient, project, reference, products) in a PDF.
            var details = (await movements.GetOperationAsync(operation.OperationId))!;
            var pdf = new ConsumptionNotePdfWriter().Write(details, DateTime.Now);
            using var document = UglyToad.PdfPig.PdfDocument.Open(pdf);
            var text = string.Join(" ", document.GetPages().SelectMany(page => page.GetWords()).Select(word => word.Text));
            Check(details.Lines.Count == 1 && details.Reference == "Aviz sto" && pdf.Length > 1000 && text.Contains("Bon de consum") && text.Contains($"Ext Sto Beneficiar {suffix}")
                  && text.Contains($"Ext Sto Proiect {suffix}") && text.Contains("Aviz sto") && text.Contains($"Ext Sto A {suffix}") && text.Contains("semnătură"),
                "The consumption note lists the recipient, project, reference and products and has room for the signatures");
        }
        finally
        {
            await ExecuteAsync(probe, "DELETE h FROM stock_movement_history h INNER JOIN stock_movements m ON m.id=h.movement_id WHERE m.product_id IN (@a,@b)", ("@a", first.Id), ("@b", second.Id));
            await ExecuteAsync(probe, "DELETE FROM stock_movements WHERE product_id IN (@a,@b)", ("@a", first.Id), ("@b", second.Id));
            await ExecuteAsync(probe, "UPDATE products SET quantity=0 WHERE id IN (@a,@b)", ("@a", first.Id), ("@b", second.Id));
            await projects.DeleteAsync(project, "Ext curatare");
            await beneficiaries.DeleteAsync((await beneficiaries.GetBeneficiariesAsync()).First(item => item.Id == beneficiary.Id), "Ext curatare");
            await products.DeleteAsync((await products.GetProductAsync(first.Id))!, "Ext curatare");
            await products.DeleteAsync((await products.GetProductAsync(second.Id))!, "Ext curatare");
            await ExecuteAsync(probe, "DELETE FROM subcategories WHERE name=@n", ("@n", subcategory));
            await ExecuteAsync(probe, "DELETE FROM categories WHERE name=@n", ("@n", category));
        }
    }

    private static async Task ReservationsAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe)
    {
        var suffix = Suffix();
        var started = DateTime.UtcNow.AddSeconds(-1);
        var category = $"Ext Rez Cat {suffix}";
        var subcategory = $"Ext Rez Sub {suffix}";
        var products = new MariaProductRepository(configuration, admin, audit);
        var movements = new MariaStockMovementRepository(configuration, admin, audit);
        var reservations = new MariaReservationRepository(configuration, admin, audit);
        var components = new MariaProjectComponentRepository(configuration, admin, audit, movements);
        var types = new MariaSystemTypeRepository(configuration, admin, audit);
        var beneficiaries = new MariaBeneficiaryRepository(configuration, admin, audit);
        var projects = new MariaProjectRepository(configuration, new TestWebHostEnvironment(Path.GetTempPath()), admin, audit);
        var today = DateOnly.FromDateTime(DateTime.Now);
        await products.CreateCategoryAsync(category);
        await products.CreateSubcategoryAsync(category, subcategory);
        var product = await products.CreateAsync(new ProductInput { Name = $"Ext Rez Produs {suffix}", Category = category, Subcategory = subcategory });
        var beneficiary = await beneficiaries.CreateAsync(Legal($"Ext Rez Client {suffix} SRL", "RO" + Random.Shared.Next(60000000, 69999999)));
        var first = await projects.CreateAsync(new ProjectInput { BeneficiaryId = beneficiary.Id, Name = $"Ext Rez Proiect A {suffix}" });
        var second = await projects.CreateAsync(new ProjectInput { BeneficiaryId = beneficiary.Id, Name = $"Ext Rez Proiect B {suffix}" });
        var type = await types.CreateAsync($"Ext Rez Sistem {suffix}");
        try
        {
            async Task<bool> Journaled(string action) => (await audit.GetEventsAsync()).Any(item => item.TimestampUtc >= started && item.Action == action);
            async Task<int> Stock() => (await products.GetProductAsync(product.Id))!.Quantity;
            StockMovementInput Exit(int quantity, int? project, Action<StockMovementInput>? change = null)
            {
                var input = new StockMovementInput
                {
                    Kind = StockMovementKind.Exit, Date = today, Quantity = quantity, Description = "Ext rezervare iesire", Destination = ExitDestination.Beneficiary,
                    BeneficiaryId = beneficiary.Id, ProjectId = project, Reference = "Aviz rez"
                };
                change?.Invoke(input);
                return input;
            }
            await movements.CreateAsync(product.Id, new StockMovementInput { Kind = StockMovementKind.Entry, Date = today.AddDays(-1), Quantity = 10, Description = "Ext rezervare stoc" });

            // 1. A reservation does not change the stock; it lowers the free stock; more than the free stock is refused ("cat se poate" reduces it); it is journaled.
            var reservedFirst = await reservations.ReserveAsync(first.Id, null, product.Id, 4);
            var tooMuch = await Rejects<ReservationException>(() => reservations.ReserveAsync(second.Id, null, product.Id, 7), "A reservation above the free stock is refused");
            var reservedSecond = await reservations.ReserveAsync(second.Id, null, product.Id, 9, capToFree: true);
            var holders = await reservations.GetHoldersAsync(product.Id);
            Check(reservedFirst.Quantity == 4 && tooMuch is not null && reservedSecond.Quantity == 6 && await Stock() == 10 && holders.Sum(item => item.Quantity) == 10
                  && ReservationRules.Free(10, 10) == 0 && ReservationRules.Free(-3, 2) == 0 && await Journaled(AuditActions.ReserveStock),
                "A reservation does not change the stock, lowers the free stock, is capped to it when asked and is journaled");
            await reservations.ReduceAsync(reservedSecond.Id, 1, "Ext ajustare");
            var noReason = await Rejects<ReservationException>(() => reservations.ReduceAsync(reservedSecond.Id, 1, " "), "Lowering a reservation needs a reason");

            // 2. An exit for the project consumes its own reservation first (no warning, the others stay untouched), journaled.
            await movements.CreateAsync(product.Id, Exit(3, first.Id));
            var afterOwn = await reservations.GetForProjectAsync(first.Id);
            Check(noReason is not null && afterOwn.Sum(item => item.Quantity) == 1 && (await reservations.GetForProjectAsync(second.Id)).Sum(item => item.Quantity) == 5 && await Stock() == 7
                  && await Journaled(AuditActions.ReservationConsumed) && await Journaled(AuditActions.ReduceReservation),
                "An exit for the project consumes its own reservation first, without a warning, and it is journaled");
            var placement = await new MariaProductPlacementReader(configuration, admin).GetDeliveriesAsync(product.Id);
            Check(placement.Count == 1 && placement[0].Net == 3 && placement[0].ProjectId == first.Id && placement[0].BeneficiaryId == beneficiary.Id,
                "The card of the pieces lists what was handed over to the beneficiary and project (exits minus returns)");

            // 3. An exit by someone with no reservation that would take reserved pieces is warned (nothing is blocked): continue untouched, or lower a reservation with a reason.
            var warning = await Rejects<ReservationWarningException>(() => movements.CreateAsync(product.Id, Exit(3, null)), "An exit taking reserved pieces is warned");
            await movements.CreateAsync(product.Id, Exit(3, null, input => input.ReservationAck = true));
            var untouched = (await reservations.GetHoldersAsync(product.Id)).Sum(item => item.Quantity);
            await movements.CreateAsync(product.Id, Exit(2, null, input => { input.ReduceReservationProjectId = second.Id; input.ReduceReservationQuantity = 1; input.ReduceReservationReason = "Ext client prioritar"; }));
            var noReduceReason = await Rejects<StockMovementOperationException>(() => movements.CreateAsync(product.Id, Exit(1, null, input => { input.ReduceReservationProjectId = second.Id; })), "Lowering a reservation at an exit needs a reason");
            Check(warning is { Excess: > 0 } && warning.Holders.Count == 2 && untouched == 6 && (await reservations.GetForProjectAsync(second.Id)).Sum(item => item.Quantity) == 4
                  && noReduceReason is not null && await Stock() == 2 && await Journaled(AuditActions.ReservationReducedByExit),
                "An exit that takes reserved pieces warns with the holders, can continue untouched or lower a reservation with a reason, and nothing is blocked");

            // 4. Taking a component out releases the reservations held for it (journaled).
            var component = (await components.AddAsync(first.Id, [type.Id])).Single();
            await movements.CreateAsync(product.Id, new StockMovementInput { Kind = StockMovementKind.Entry, Date = today, Quantity = 5, Description = "Ext rezervare stoc 2" });
            await reservations.ReserveAsync(first.Id, component.Id, product.Id, 2);
            await components.ArchiveAsync(component, "Ext scoatere cu rezervare");
            Check((await reservations.GetForProjectAsync(first.Id)).All(item => item.ComponentId != component.Id) && await Journaled(AuditActions.ReservationReleasedByComponent),
                "Taking a component out releases the reservations held for it and journals it");

            // 5. Two simultaneous exits for the same product are serialized: both succeed (acknowledged) and the stock is exact.
            var before = await Stock();
            await Task.WhenAll(movements.CreateAsync(product.Id, Exit(4, null, input => input.ReservationAck = true)), movements.CreateAsync(product.Id, Exit(1, null, input => input.ReservationAck = true)));
            Check(await Stock() == before - 5 && (await reservations.GetHoldersAsync(product.Id)).All(item => item.Quantity > 0), "Two simultaneous exits of the same product are serialized and the stock is exact");
        }
        finally
        {
            await ExecuteAsync(probe, "DELETE FROM stock_movements WHERE product_id=@id", ("@id", product.Id));
            await ExecuteAsync(probe, "UPDATE products SET quantity=0 WHERE id=@id", ("@id", product.Id));
            await ExecuteAsync(probe, "DELETE FROM project_reservations WHERE product_id=@id", ("@id", product.Id));
            await projects.DeleteAsync((await projects.GetAsync(first.Id))!, "Ext curatare");
            await projects.DeleteAsync((await projects.GetAsync(second.Id))!, "Ext curatare");
            await beneficiaries.DeleteAsync((await beneficiaries.GetBeneficiariesAsync()).First(item => item.Id == beneficiary.Id), "Ext curatare");
            await ExecuteAsync(probe, "DELETE FROM system_types WHERE id=@id", ("@id", type.Id));
            await products.DeleteAsync((await products.GetProductAsync(product.Id))!, "Ext curatare");
            await ExecuteAsync(probe, "DELETE FROM subcategories WHERE name=@n", ("@n", subcategory));
            await ExecuteAsync(probe, "DELETE FROM categories WHERE name=@n", ("@n", category));
        }
    }
}
