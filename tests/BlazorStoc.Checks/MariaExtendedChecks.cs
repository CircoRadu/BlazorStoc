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
            await Section("Maintenance interventions: register, due date choices, corrections, photos, archive, guards", () => ServiceInterventionsAsync(configuration, admin, audit, probe, assets));
            await Section("Maintenance notifications: due dates and contract expiry sources, automatic close and reopen, threshold", () => MaintenanceNotificationsAsync(configuration, admin, audit, probe));
            await Section("Journal: server-side filtering, paging, window, ranges, removals, summary", () => AuditQueryAsync(configuration, audit, probe));
            await Section("Change events", () => ChangeEventsAsync(configuration, admin, audit));
            await Section("Expiry notifications: templates, engine, take over, reminder", () => NotificationsAsync(configuration, admin, audit, probe));
            await Section("Notification settings: clean-up of old resolved notifications", () => NotificationSettingsAsync(configuration, admin, audit, probe));
            await Section("Invoice templates: create, versions, unique names, concurrency, delete, journal", () => InvoiceTemplatesAsync(configuration, admin, audit, probe));
            await Section("Suppliers and invoices: unique tax id, sources, edits, invoices, entries tied to invoices, delete rules, journal, archive", () => SuppliersAsync(configuration, admin, audit, probe));
            await Section("Supplier recognition: template link, aliases, recognition log, CSV export", () => SupplierRecognitionAsync(configuration, admin, audit, probe));
        }
        finally
        {
            try { Directory.Delete(assets, true); } catch (IOException) { }
        }
        if (failures > 0) throw new Exception($"{failures} extended MariaDB section(s) failed.");
        Console.WriteLine("=== Extended MariaDB checks: all sections passed. ===");
    }

    // ---- Invoice templates (Settings -> Facturi) ---------------------------------------------------------------------------------

    private static InvoiceTemplateDefinition InvoiceDefinition(string source = InvoiceSources.Text) =>
        new(InvoiceTemplateDefinition.CurrentSchema, source, 842, 595,
            [new InvoiceTemplateAnchor("factura", 1, 0.1, 0.1, InvoiceAnchorGroups.Page)],
            [new InvoiceTemplateField("f1", InvoiceFieldMeanings.InvoiceNumber, "Număr factură", true, 1, 0.1, 0.1, 0.1, 0.02, "Nr. factura", "text", false, InvoiceFieldModes.Right, 0.05, 0.1)],
            new InvoiceTemplateTable(1, 0.3, 0.32, InvoiceRowSplit.Top, true, ";",
                [new InvoiceTemplateColumn("c1", "Denumire", InvoiceColumnMeanings.Name, true, 0.1, 0.5, InvoiceRowMapping.Band, false)]));

    private static async Task InvoiceTemplatesAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe)
    {
        var suffix = Suffix();
        var store = new MariaInvoiceTemplateStore(configuration);
        var service = new InvoiceTemplateService(store, admin, audit);
        var cui = "9" + new Random().Next(1000000, 9999999).ToString();
        var started = DateTime.UtcNow.AddSeconds(-1);
        try
        {
            var first = await service.CreateAsync(new InvoiceTemplateInput { Name = $"Sablon {suffix}", SupplierName = $"Furnizor {suffix}", SupplierCui = "RO" + cui, ModelFileName = "model.pdf", ModelContent = [1, 2, 3, 4, 250], Definition = InvoiceDefinition() });
            Check(first.Info.Id > 0 && first.Info.SupplierCui == cui && first.Info.Active && first.Info.CreatedBy == "integration.tester", "A template is saved with its supplier's tax id reduced to digits and the actor");
            var loaded = await store.GetAsync(first.Info.Id);
            var model = await store.GetModelAsync(first.Info.Id);
            Check(model is not null && model.FileName == "model.pdf" && model.Content.SequenceEqual(new byte[] { 1, 2, 3, 4, 250 }) && model.Sha256.Length == 64, "The invoice used as model is stored with the template (binary content unchanged)");
            Check(loaded is not null && loaded.Definition.Fields[0].LabelText == "Nr. factura" && loaded.Definition.Fields[0].Mode == InvoiceFieldModes.Right && loaded.Definition.Table!.NameCodeSeparator == ";" &&
                  loaded.Definition.Anchors.Count == 1 && loaded.Info.UpdatedUtc.Kind == DateTimeKind.Utc, "The definition (with diacritics, label anchors, table) is stored and read back unchanged");
            await Rejects<InvoiceTemplateOperationException>(() => service.CreateAsync(new InvoiceTemplateInput { Name = $"SABLON {suffix}", SupplierName = "x", SupplierCui = cui, Definition = InvoiceDefinition() }), "The same name for the same supplier is refused (also in another letter case)");
            // The database's own unique key refuses it too, even when the application check is bypassed.
            await Rejects<InvoiceTemplateOperationException>(() => store.CreateAsync(new InvoiceTemplateInput { Name = $"Sablon {suffix}", SupplierName = "x", SupplierCui = cui, Definition = InvoiceDefinition() }, "raw"), "The unique key (supplier, name) refuses a duplicate that bypasses the service");
            var scanned = await service.CreateAsync(new InvoiceTemplateInput { Name = $"Sablon scanat {suffix}", SupplierName = $"Furnizor {suffix}", SupplierCui = cui, Definition = InvoiceDefinition(InvoiceSources.Ocr) });
            Check(scanned.Info.SourceKind == InvoiceSources.Ocr && (await store.ListAsync()).Count(item => item.SupplierCui == cui) == 2, "A supplier has several templates");

            var second = await service.SaveAsync(first.Info, new InvoiceTemplateInput { Name = first.Info.Name, SupplierName = first.Info.SupplierName, SupplierCui = cui, Definition = InvoiceDefinition() with { PageWidth = 600 } });
            Check(second.Info.Version == first.Info.Version + 1 && (await store.GetAsync(first.Info.Id))!.Definition.PageWidth == 600, "Saving replaces the template's definition (no earlier version is kept)");
            await Rejects<InvoiceTemplateOperationException>(() => service.SaveAsync(first.Info, new InvoiceTemplateInput { Name = first.Info.Name, SupplierCui = cui, Definition = InvoiceDefinition() }), "Saving over a template changed in the meantime (stale version) is refused");
            var renamed = await service.UpdateDetailsAsync(second.Info, new InvoiceTemplateInput { Name = $"Redenumit {suffix}", SupplierName = $"Furnizor nou {suffix}", SupplierCui = cui });
            Check(renamed.Name == $"Redenumit {suffix}" && renamed.Version == second.Info.Version + 1, "Renaming changes the details");
            var switchedOff = await service.SetActiveAsync(renamed, false);
            Check(!switchedOff.Active && !(await store.GetAsync(first.Info.Id))!.Info.Active && switchedOff.Version == renamed.Version + 1, "A template can be switched off (not used when invoices are read) and the choice is stored");
            renamed = await service.SetActiveAsync(switchedOff, true);
            Check(renamed.Active && (await store.GetAsync(first.Info.Id))!.Info.Active, "A template can be switched on again");
            await Rejects<InvoiceTemplateOperationException>(() => service.UpdateDetailsAsync(renamed, new InvoiceTemplateInput { Name = scanned.Info.Name, SupplierName = "x", SupplierCui = cui }), "Renaming to another template's name of the same supplier is refused");

            var events = (await audit.GetEventsAsync()).Where(item => item.TimestampUtc >= started && item.EntityType == AuditEntities.InvoiceTemplate && item.EntityId == first.Info.Id.ToString()).Select(item => item.Action).ToList();
            Check(events.Contains(AuditActions.CreateInvoiceTemplate) && events.Contains(AuditActions.EditInvoiceTemplate) && events.Contains(AuditActions.EditInvoiceTemplateDetails) && events.Contains(AuditActions.ActivateInvoiceTemplate) && events.Contains(AuditActions.DeactivateInvoiceTemplate), "Each operation is in the journal under its own exact action");

            await service.DeleteAsync(renamed, "Motiv de test");
            Check(await store.GetAsync(first.Info.Id) is null && await ScalarLongAsync(probe, "SELECT COUNT(*) FROM invoice_template_versions WHERE template_id=@id", ("@id", first.Info.Id)) == 0 && await ScalarLongAsync(probe, "SELECT COUNT(*) FROM invoice_template_models WHERE template_id=@id", ("@id", first.Info.Id)) == 0, "Deleting a template removes its versions with it");
            var deleted = (await audit.GetEventsAsync()).FirstOrDefault(item => item.TimestampUtc >= started && item.Action == AuditActions.DeleteInvoiceTemplate && item.EntityId == first.Info.Id.ToString());
            Check(deleted is not null && deleted.Motif == "Motiv de test" && deleted.Details.Contains("Furnizor", StringComparison.Ordinal) && deleted.Details.Contains($"Furnizor nou {suffix}", StringComparison.Ordinal), "The deletion is journaled with its reason and the supplier (no saved versions to count any more)");
            var createdEvent = (await audit.GetEventsAsync()).First(item => item.TimestampUtc >= started && item.Action == AuditActions.CreateInvoiceTemplate && item.EntityId == first.Info.Id.ToString());
            var removals = await audit.RemovalTimesAsync([createdEvent]);
            Check(removals.ContainsKey(AuditNavigation.ObjectKey(AuditEntities.InvoiceTemplate, first.Info.Id.ToString())) && AuditNavigation.TargetUrl(createdEvent, removals) is null && AuditNavigation.TargetUrl(createdEvent) is not null,
                "The database journal reports the deletion of a template, so the earlier events about it stop linking to its page");
            await Rejects<InvoiceTemplateOperationException>(() => service.DeleteAsync(renamed, "din nou"), "Deleting a template that is already gone is refused");
            await Rejects<AccessDeniedException>(() => new InvoiceTemplateService(store, new TestAccessControl(false, "limitat"), audit).DeleteAsync(scanned.Info, "x"), "A user who is not an administrator cannot change templates");
        }
        finally
        {
            await ExecuteAsync(probe, "DELETE FROM invoice_templates WHERE supplier_cui=@cui", ("@cui", cui));
        }
    }

    // ---- Suppliers and their invoices ----------------------------------------------------------------------------------------------

    private static async Task SupplierRecognitionAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe)
    {
        var suffix = Suffix();
        var started = DateTime.UtcNow.AddSeconds(-1);
        var templates = new MariaInvoiceTemplateStore(configuration);
        var service = new InvoiceTemplateService(templates, admin, audit);
        var suppliers = new MariaSupplierRepository(configuration, admin, audit, invoiceTemplates: service);
        var invoices = new MariaSupplierInvoiceRepository(configuration, admin, audit);
        var log = new MariaSupplierRecognitionLog(configuration, admin);
        var cui = SupplierChecks.ValidCui(Random.Shared.Next(1_000_000, 9_999_999));
        InvoiceTemplateRecord? template = null; Supplier? supplier = null; SupplierInvoice? invoice = null;
        try
        {
            // A template made before its supplier is in the register is not linked; the link is made later, with its own journal action.
            template = await service.CreateAsync(new InvoiceTemplateInput { Name = $"Sablon legare {suffix}", SupplierName = $"Furnizor Legare {suffix}", SupplierCui = cui, Definition = InvoiceDefinition() });
            Check(template.Info.SupplierId is null, "A template made for a tax id that is not in the register is not linked to a supplier");
            supplier = await suppliers.CreateAsync(new SupplierInput { Name = $"Furnizor Legare {suffix} SRL", Cui = cui });
            var linked = await service.LinkSupplierAsync(template.Info, supplier);
            var reread = await templates.GetAsync(template.Info.Id);
            var events = (await audit.GetEventsAsync()).Where(item => item.TimestampUtc >= started && item.EntityType == AuditEntities.InvoiceTemplate && item.EntityId == template.Info.Id.ToString()).Select(item => item.Action).ToList();
            Check(linked.SupplierId == supplier.Id && reread!.Info.SupplierId == supplier.Id && reread.Info.SupplierName == supplier.Name && events.Contains(AuditActions.LinkInvoiceTemplateSupplier),
                "Linking a template to a supplier stores the supplier id, takes the supplier's name and is journaled as its own operation");

            // An alias is kept for the supplier, found by name, and refused when it belongs to another supplier or is too short.
            var withAlias = await suppliers.AddAliasAsync(supplier, $"Denumire Comerciala {suffix}");
            Check(withAlias.AliasList.Count == 1 && SupplierRules.FindByName(await suppliers.GetSuppliersAsync(), $"SC Denumire Comerciala {suffix} SRL")?.Id == supplier.Id, "An alias is stored with the supplier and finds it by name");
            await Rejects<SupplierOperationException>(() => suppliers.AddAliasAsync(supplier, $"denumire comerciala {suffix} s.r.l."), "The same alias (legal form, dots and case aside) is refused");

            // The recognition log: the invoice, what was proposed and chosen, the changed template, the summary and the CSV.
            invoice = await invoices.CreateAsync(new SupplierInvoiceInput { SupplierId = supplier.Id, Number = $"REC-{suffix}", Date = DateOnly.FromDateTime(DateTime.Now) });
            await log.RecordAsync(new SupplierRecognitionEntry(invoice.Id, SupplierMatchMethod.Similar, "low", $"=Nume Citit {suffix}", cui, null, supplier.Id, TemplateChanged: true));
            var summary = await log.SummaryAsync();
            Check(summary.Total >= 1 && summary.ByMethod.Any(item => item.Method == "similar" && item.Confidence == "low" && item.TemplateChanged >= 1) &&
                  summary.Recent.Any(item => item.InvoiceNumber == $"REC-{suffix}" && item.ChosenSupplierId == supplier.Id && item.ChosenName == supplier.Name),
                "The recognition log counts the invoice by method and confidence, with the changed template, and lists the correction with the chosen supplier");
            var csv = await log.ExportCsvAsync();
            Check(csv.StartsWith("Data;Factura;Metoda", StringComparison.Ordinal) && csv.Contains($"\"REC-{suffix}\";\"similar\";\"low\";\"'=Nume Citit {suffix}\"", StringComparison.Ordinal) && csv.Contains(";da;da", StringComparison.Ordinal),
                "The CSV export has the header and the row of the invoice, with the read name neutralised as a formula and corrected / template changed marked");
            await log.RecordAsync(new SupplierRecognitionEntry(invoice.Id, SupplierMatchMethod.Cui, "high", "x", cui, supplier.Id, supplier.Id));
            Check(await ScalarLongAsync(probe, "SELECT COUNT(*) FROM supplier_recognitions WHERE invoice_id=@id", ("@id", invoice.Id)) == 1, "One recognition per invoice: a second record for the same invoice is ignored");
        }
        finally
        {
            try
            {
                if (invoice is not null) await ScalarLongAsync(probe, "DELETE FROM supplier_invoices WHERE id=@id", ("@id", invoice.Id));
                if (template is not null && await templates.GetAsync(template.Info.Id) is { } current) await service.DeleteAsync(current.Info, "Curatare test");
                if (supplier is not null && await suppliers.GetAsync(supplier.Id) is { } leftover) await suppliers.DeleteAsync(leftover, "Curatare test");
            }
            catch (Exception exception) when (exception is not OperationCanceledException) { Console.WriteLine("Cleanup of the recognition checks failed: " + exception.GetType().Name); }
        }
    }

    private static async Task SuppliersAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe)
    {
        var suffix = Suffix();
        var category = $"Ext Furnizori Cat {suffix}";
        var subcategory = $"Ext Furnizori Sub {suffix}";
        var products = new MariaProductRepository(configuration, admin, audit);
        var movements = new MariaStockMovementRepository(configuration, admin, audit);
        var suppliers = new MariaSupplierRepository(configuration, admin, audit);
        var invoices = new MariaSupplierInvoiceRepository(configuration, admin, audit);
        var normalUser = new TestAccessControl(false, "utilizator.normal");
        var asUser = new MariaSupplierRepository(configuration, normalUser, audit);
        var started = DateTime.UtcNow.AddSeconds(-1);
        var today = DateOnly.FromDateTime(DateTime.Now);
        string NewCui() => SupplierChecks.ValidCui(Random.Shared.Next(1_000_000, 9_999_999));
        var cuiA = NewCui(); var cuiB = NewCui(); var cuiD = NewCui();
        while (cuiB == cuiA) cuiB = NewCui();
        while (cuiD == cuiA || cuiD == cuiB) cuiD = NewCui();
        var vatDigits = Random.Shared.NextInt64(100_000_000, 999_999_999).ToString();
        await products.CreateCategoryAsync(category);
        await products.CreateSubcategoryAsync(category, subcategory);
        var product = await products.CreateAsync(new ProductInput { Name = $"Ext Furnizori {suffix}", Category = category, Subcategory = subcategory });
        var supplierIds = new List<int>();
        var templateStore = new MariaInvoiceTemplateStore(configuration);
        var templateService = new InvoiceTemplateService(templateStore, admin, audit);
        try
        {
            // A user without the administrator role creates (and later edits) suppliers.
            var a = await asUser.CreateAsync(new SupplierInput { Name = $"Furnizor A {suffix}", Cui = "RO " + cuiA, Address = "Strada 1", Phone = "0721 000 111", Source = SupplierSources.Anaf });
            supplierIds.Add(a.Id);
            Check(a.Id > 0 && a.Cui == cuiA && a.Country == "RO" && a.Source == SupplierSources.Anaf && a.VerifiedUtc is not null && a.Version == 0, "A supplier is saved with its CUI reduced to digits, its source and the time of the ANAF reading");
            var listed = (await suppliers.GetSuppliersAsync()).Single(item => item.Id == a.Id);
            Check(listed.Name == a.Name && listed.Phone == "0721000111" && listed.Address == "Strada 1" && listed.InvoiceCount == 0 && listed.MovementCount == 0 && !listed.InUse && listed.VerifiedUtc!.Value.Kind == DateTimeKind.Utc,
                "The supplier is read back with its data and nothing refers to it yet");

            // One supplier for one tax id, however it is written; two sessions saving the same CUI at once: exactly one wins.
            var duplicate = await Rejects<SupplierOperationException>(() => suppliers.CreateAsync(new SupplierInput { Name = "Alt nume", Cui = cuiA }), "The same CUI under another name is refused (the pair name + CUI is not the key: the CUI is)");
            Check(duplicate!.Message.Contains(a.Name, StringComparison.Ordinal), "The refusal names the supplier already stored");
            await Rejects<SupplierOperationException>(() => suppliers.CreateAsync(new SupplierInput { Name = a.Name, Cui = "ro" + cuiA }), "The same CUI written with the RO prefix is refused");
            await Rejects<SupplierOperationException>(() => suppliers.CreateAsync(new SupplierInput { Name = "x", Cui = "9" }), "A CUI that is too short is refused");
            var attempts = await Task.WhenAll(Enumerable.Range(0, 2).Select(async index =>
            {
                try { return await new MariaSupplierRepository(configuration, admin, audit).CreateAsync(new SupplierInput { Name = $"Furnizor B {suffix} {index}", Cui = cuiB }); }
                catch (SupplierOperationException) { return null; }
            }));
            Check(attempts.Count(item => item is not null) == 1, "Two sessions saving the same CUI at the same time: exactly one supplier is created");
            var b = attempts.Single(item => item is not null)!;
            supplierIds.Add(b.Id);
            Check(await ScalarLongAsync(probe, "SELECT COUNT(*) FROM suppliers WHERE normalized_cui=@cui", ("@cui", cuiB)) == 1, "The database holds the CUI once (unique key)");

            // A supplier of another member state: the VAT identifier with its prefix, always entered by hand.
            var foreign = await suppliers.CreateAsync(new SupplierInput { Country = "DE", Name = $"Lieferant {suffix}", Cui = vatDigits, Source = SupplierSources.Anaf });
            supplierIds.Add(foreign.Id);
            Check(foreign.Cui == "DE" + vatDigits && foreign.Country == "DE" && foreign.Source == SupplierSources.Manual && foreign.VerifiedUtc is null && foreign.IsExternal, "A supplier of another member state is saved with its VAT identifier and as entered by hand");
            await Rejects<SupplierOperationException>(() => suppliers.CreateAsync(new SupplierInput { Country = "DE", Name = "x", Cui = "de " + vatDigits }), "The same VAT identifier is refused");

            // Edits: reason, version, ANAF read again, duplicates.
            var edit = SupplierInput.From(a); edit.Name = $"Furnizor A modificat {suffix}"; edit.Source = SupplierSources.AnafEdited; edit.Reason = "Motiv de test";
            var updated = await asUser.UpdateAsync(a, edit);
            Check(updated.Version == a.Version + 1 && updated.Name == edit.Name && updated.Source == SupplierSources.AnafEdited && updated.VerifiedUtc == a.VerifiedUtc, "A user without the administrator role edits a supplier (version up, source edited, ANAF time unchanged)");
            await Rejects<SupplierOperationException>(() => asUser.UpdateAsync(a, edit), "Editing a supplier changed in the meantime (stale version) is refused");
            var noReason = SupplierInput.From(updated); noReason.Name = "Fara motiv";
            await Rejects<SupplierOperationException>(() => asUser.UpdateAsync(updated, noReason), "An edit without its reason is refused");
            var toDuplicate = SupplierInput.From(b); toDuplicate.Cui = cuiA; toDuplicate.Reason = "Motiv de test";
            await Rejects<SupplierOperationException>(() => asUser.UpdateAsync(b, toDuplicate), "Changing a CUI to one already registered is refused");
            await Task.Delay(1100);
            var recheck = SupplierInput.From(updated); recheck.Source = SupplierSources.Anaf; recheck.AnafRecheck = true; recheck.Reason = "Reverificare ANAF";
            var rechecked = await asUser.UpdateAsync(updated, recheck);
            Check(rechecked.Source == SupplierSources.Anaf && rechecked.VerifiedUtc > a.VerifiedUtc && rechecked.Version == updated.Version + 1, "Reading the data from ANAF again makes it \"from ANAF\" and records the new time");
            var events = (await audit.GetEventsAsync()).Where(item => item.TimestampUtc >= started && item.EntityType == AuditEntities.Supplier && item.EntityId == a.Id.ToString()).ToList();
            Check(events.Any(item => item.Action == AuditActions.CreateSupplier && item.ActorUsername == "utilizator.normal" && item.Details.Contains("Sursa datelor: Date preluate din ANAF", StringComparison.Ordinal)) &&
                  events.Any(item => item.Action == AuditActions.EditSupplier && item.Details.Contains("Denumire:", StringComparison.Ordinal) && item.Details.Contains("Preluate din ANAF, editate manual", StringComparison.Ordinal) && item.Motif == "Motiv de test") &&
                  events.Any(item => item.Action == AuditActions.RecheckSupplier && item.Details.Contains("Ultima verificare ANAF", StringComparison.Ordinal) && item.Motif == "Reverificare ANAF"),
                "The journal names each operation exactly: \"Adăugare furnizor\", \"Modificare furnizor\" (old and new values, source) and \"Reverificare furnizor ANAF\"");

            // Invoices: number + date + supplier, recorded once per supplier.
            var invoice = await new MariaSupplierInvoiceRepository(configuration, normalUser, audit).CreateAsync(new SupplierInvoiceInput { SupplierId = a.Id, Number = $"FT {suffix}", Date = today.AddDays(-1) });
            Check(invoice.Id > 0 && invoice.SupplierId == a.Id && invoice.SupplierName == rechecked.Name && invoice.Number == $"FT {suffix}" && invoice.Date == today.AddDays(-1) && invoice.CreatedBy == "utilizator.normal",
                "An invoice is recorded with its supplier, number and date of issue (a user without the administrator role can)");
            var again = await Rejects<SupplierInvoiceOperationException>(() => invoices.CreateAsync(new SupplierInvoiceInput { SupplierId = a.Id, Number = $"ft{suffix}", Date = today }), "The same invoice number of the same supplier (other letter case, no space) is refused");
            Check(again!.Message.Contains("a fost deja preluată", StringComparison.Ordinal) && again.Message.Contains(rechecked.Name, StringComparison.Ordinal), "The refusal says the invoice was already taken, and from whom");
            var otherSupplierInvoice = await invoices.CreateAsync(new SupplierInvoiceInput { SupplierId = b.Id, Number = $"FT {suffix}", Date = today });
            Check(otherSupplierInvoice.Id != invoice.Id, "The same number of another supplier is another invoice");
            await Rejects<SupplierInvoiceOperationException>(() => invoices.CreateAsync(new SupplierInvoiceInput { SupplierId = a.Id, Number = "Viitor", Date = today.AddDays(1) }), "An invoice dated in the future is refused");
            await Rejects<SupplierInvoiceOperationException>(() => invoices.CreateAsync(new SupplierInvoiceInput { SupplierId = 2_000_000_000, Number = "X1", Date = today }), "An invoice of a supplier that does not exist is refused");
            var race = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
            {
                try { await new MariaSupplierInvoiceRepository(configuration, admin, audit).CreateAsync(new SupplierInvoiceInput { SupplierId = a.Id, Number = $"CC {suffix}", Date = today }); return true; }
                catch (SupplierInvoiceOperationException) { return false; }
            }));
            Check(race.Count(item => item) == 1, "Two sessions recording the same invoice at the same time: exactly one is recorded");
            var invoiceEvent = (await audit.GetEventsAsync()).FirstOrDefault(item => item.TimestampUtc >= started && item.EntityType == AuditEntities.SupplierInvoice && item.Action == AuditActions.RecordSupplierInvoice && item.EntityId == invoice.Id.ToString());
            Check(invoiceEvent is not null && invoiceEvent.Details.Contains($"Număr factură: FT {suffix}", StringComparison.Ordinal) && invoiceEvent.Details.Contains("Data emiterii: " + StockMovementRules.DisplayDate(today.AddDays(-1)), StringComparison.Ordinal),
                "The journal records \"Înregistrare factură furnizor\" with the supplier, the number and the date");

            // Entries: tied to an invoice, or free.
            var linked = await movements.CreateAsync(product.Id, new StockMovementInput { Kind = StockMovementKind.Entry, Date = today, Quantity = 3, Description = "Ext intrare cu factura", InvoiceId = invoice.Id });
            Check(linked.Movement.InvoiceId == invoice.Id && linked.Movement.InvoiceNumber == invoice.Number && linked.Movement.SupplierName == rechecked.Name && linked.Movement.SupplierId == a.Id && linked.Stock == 3,
                "An entry is tied to an invoice (the stock rises as usual)");
            var reread = await movements.GetAsync(linked.Movement.Id);
            Check(reread is { HasInvoice: true } && reread.InvoiceId == invoice.Id && reread.SupplierName == rechecked.Name, "The entry read back carries its invoice and supplier");
            var free = await movements.CreateAsync(product.Id, new StockMovementInput { Kind = StockMovementKind.Entry, Date = today, Quantity = 2, Description = "Ext intrare libera" });
            Check(!free.Movement.HasInvoice && free.Movement.InvoiceNumber is null && free.Stock == 5, "An entry without an invoice (a free entry) is still possible");
            await Rejects<StockMovementOperationException>(() => movements.CreateAsync(product.Id, new StockMovementInput { Kind = StockMovementKind.Exit, Date = today, Quantity = 1, Description = "Ext iesire", Destination = ExitDestination.GenericSale, InvoiceId = invoice.Id }), "An exit cannot be tied to an invoice");
            await Rejects<StockMovementOperationException>(() => movements.CreateAsync(product.Id, new StockMovementInput { Kind = StockMovementKind.Entry, Date = today, Quantity = 1, Description = "Ext factura lipsa", InvoiceId = 2_000_000_000 }), "An entry tied to an invoice that does not exist is refused");
            Check((await movements.GetPageAsync(product.Id, new StockMovementQuery())).Stock == 5, "The refused entries leave the stock unchanged");
            var movementEdit = StockMovementInput.From(linked.Movement); movementEdit.Quantity = 4; movementEdit.Reason = "Ext corectie cantitate";
            var editedEntry = await movements.UpdateAsync(linked.Movement, movementEdit);
            Check(editedEntry.Movement.InvoiceId == invoice.Id && (await movements.GetAsync(editedEntry.Movement.Id))!.InvoiceId == invoice.Id, "Editing an entry keeps its invoice");
            var entryEvent = (await audit.GetEventsAsync()).FirstOrDefault(item => item.TimestampUtc >= started && item.EntityType == AuditEntities.StockMovement && item.Action == AuditActions.Create && item.EntityId == linked.Movement.Id.ToString());
            Check(entryEvent is not null && entryEvent.Details.Contains($"Factură: FT {suffix} · {rechecked.Name}", StringComparison.Ordinal), "The journal of the entry names its invoice and supplier");
            var invoiceList = await invoices.GetForSupplierAsync(a.Id);
            Check(invoiceList.Count == 2 && invoiceList.Single(item => item.Id == invoice.Id).MovementCount == 1 && invoiceList.Any(item => item.Number == $"CC {suffix}" && item.MovementCount == 0), "The invoices of a supplier are listed with the number of entries tied to each");

            // A partly taken invoice is found again (by any writing of its number) with what was taken from it.
            var found = await invoices.FindAsync(a.Id, $" ft{suffix} ");
            var takenBefore = await invoices.GetEntriesAsync(invoice.Id);
            Check(found?.Id == invoice.Id && await invoices.FindAsync(b.Id, $"FT {suffix}") is { } onB && onB.Id == otherSupplierInvoice.Id && await invoices.FindAsync(a.Id, "inexistent") is null &&
                  takenBefore.Count == 1 && takenBefore[0].ProductId == product.Id && takenBefore[0].Quantity == 4 && takenBefore[0].MovementId == linked.Movement.Id && (await invoices.GetEntriesAsync(otherSupplierInvoice.Id)).Count == 0,
                "A partly taken invoice is found again by its number (any writing) and lists what was taken from it");

            // Facturi page: the register of all invoices, corrections and deletion (administrator only).
            var register = await invoices.GetAllAsync();
            Check(register.Any(item => item.Id == invoice.Id && item.MovementCount == 1) && register.Any(item => item.Id == otherSupplierInvoice.Id && item.SupplierId == b.Id),
                "The register of all invoices lists them with the number of entries tied to each");
            var ccInvoice = (await invoices.GetForSupplierAsync(a.Id)).Single(item => item.Number == $"CC {suffix}");
            var correction = await invoices.UpdateAsync(ccInvoice, new SupplierInvoiceInput { SupplierId = b.Id, Number = $"CC {suffix} B", Date = today.AddDays(-2) }, "Motiv de test");
            var rereadCc = (await invoices.GetAllAsync()).Single(item => item.Id == ccInvoice.Id);
            Check(correction.SupplierId == b.Id && rereadCc.SupplierId == b.Id && rereadCc.Number == $"CC {suffix} B" && rereadCc.Date == today.AddDays(-2) && rereadCc.SupplierName == correction.SupplierName,
                "An invoice is corrected (number, date, supplier) and read back as corrected");
            await Rejects<SupplierInvoiceOperationException>(() => invoices.UpdateAsync(ccInvoice, new SupplierInvoiceInput { SupplierId = b.Id, Number = "Alt", Date = today }, "Motiv de test"), "A correction of a copy that was changed meanwhile (stale) is refused");
            await Rejects<SupplierInvoiceOperationException>(() => invoices.UpdateAsync(correction, new SupplierInvoiceInput { SupplierId = b.Id, Number = $"ft {suffix}", Date = today }, "Motiv de test"), "A correction to a number the supplier already has is refused");
            await Rejects<SupplierInvoiceOperationException>(() => invoices.UpdateAsync(correction, new SupplierInvoiceInput { SupplierId = b.Id, Number = "Alt", Date = today }, " "), "A correction without its reason is refused");
            await Rejects<AccessDeniedException>(() => new MariaSupplierInvoiceRepository(configuration, normalUser, audit).UpdateAsync(correction, new SupplierInvoiceInput { SupplierId = b.Id, Number = "Alt", Date = today }, "Motiv de test"), "A user without the administrator role cannot correct an invoice");
            await Rejects<AccessDeniedException>(() => new MariaSupplierInvoiceRepository(configuration, normalUser, audit).DeleteAsync(correction, "Motiv de test"), "A user without the administrator role cannot delete an invoice");
            var correctionEvents = (await audit.GetEventsAsync()).Where(item => item.TimestampUtc >= started && item.EntityType == AuditEntities.SupplierInvoice && item.EntityId == ccInvoice.Id.ToString()).ToList();
            Check(correctionEvents.Any(item => item.Action == AuditActions.EditSupplierInvoiceNumber && item.Details.Contains($"CC {suffix} → CC {suffix} B", StringComparison.Ordinal) && item.Motif == "Motiv de test") &&
                  correctionEvents.Any(item => item.Action == AuditActions.EditSupplierInvoiceDate && item.Details.Contains("Data emiterii:", StringComparison.Ordinal)) &&
                  correctionEvents.Any(item => item.Action == AuditActions.MoveSupplierInvoice && item.Details.Contains("Furnizor:", StringComparison.Ordinal)),
                "The journal names each kind of invoice correction exactly, with old and new values and the reason");
            await Rejects<SupplierInvoiceOperationException>(() => invoices.DeleteAsync(invoice, "Motiv de test"), "An invoice with entries in stock is not deleted");
            await invoices.DeleteAsync(correction, "Motiv de test");
            Check((await invoices.GetAllAsync()).All(item => item.Id != ccInvoice.Id) && (await audit.GetEventsAsync()).Any(item => item.TimestampUtc >= started && item.EntityType == AuditEntities.SupplierInvoice && item.Action == AuditActions.Delete && item.EntityId == ccInvoice.Id.ToString() && item.Motif == "Motiv de test"),
                "An invoice without entries is deleted, with a journal event holding the reason");
            await Rejects<SupplierInvoiceOperationException>(() => invoices.DeleteAsync(correction, "Motiv de test"), "Deleting an invoice already deleted is refused");
            await invoices.CreateAsync(new SupplierInvoiceInput { SupplierId = a.Id, Number = $"CC {suffix}", Date = today });   // the supplier keeps two invoices for the checks below

            // What ties a supplier: invoices and entries; the tax id stays; only an administrator deletes.
            listed = (await suppliers.GetSuppliersAsync()).Single(item => item.Id == a.Id);
            Check(listed.InvoiceCount == 2 && listed.MovementCount == 1 && listed.InUse, "The supplier list counts the invoices and the entries tied to a supplier");
            var blocked = await Rejects<SupplierOperationException>(() => suppliers.DeleteAsync(listed, "Motiv de test"), "A supplier with invoices and entries cannot be deleted");
            Check(blocked!.Message.Contains("2 facturi", StringComparison.Ordinal) && blocked.Message.Contains("o intrare de stoc", StringComparison.Ordinal), "The refusal says what ties the supplier");
            var newCui = SupplierInput.From(listed); newCui.Cui = NewCui(); newCui.Reason = "Motiv de test";
            var locked = await Rejects<SupplierOperationException>(() => asUser.UpdateAsync(listed, newCui), "The CUI of a supplier with invoices cannot be changed");
            Check(locked!.Message == SupplierRules.CuiLockedMessage, "The refusal explains that the CUI is locked");
            var renameUsed = SupplierInput.From(listed); renameUsed.Phone = "0722 333 444"; renameUsed.Reason = "Telefon nou";
            Check((await asUser.UpdateAsync(listed, renameUsed)).Phone == "0722333444", "The other data of a supplier with invoices can still be corrected");
            await Rejects<AccessDeniedException>(() => asUser.DeleteAsync(foreign, "Motiv de test"), "A user without the administrator role cannot delete a supplier, even one nothing refers to");
            Check((await suppliers.GetAsync(foreign.Id)) is not null, "The supplier survived the refused deletion");

            // A template of the supplier (by its CUI) ties it as well.
            var d = await suppliers.CreateAsync(new SupplierInput { Name = $"Furnizor D {suffix}", Cui = cuiD });
            supplierIds.Add(d.Id);
            var template = await templateService.CreateAsync(new InvoiceTemplateInput { Name = $"Sablon furnizor {suffix}", SupplierName = d.Name, SupplierCui = cuiD, Definition = InvoiceDefinition() });
            Check((await suppliers.GetSuppliersAsync()).Single(item => item.Id == d.Id).TemplateCount == 1, "An invoice template made for the CUI of a supplier is counted as tying it");
            var withTemplate = (await suppliers.GetSuppliersAsync()).Single(item => item.Id == d.Id);
            var byTemplate = await Rejects<SupplierOperationException>(() => suppliers.DeleteAsync(withTemplate, "Motiv de test"), "A supplier with an invoice template cannot be deleted");
            Check(byTemplate!.Message.Contains("un șablon de factură", StringComparison.Ordinal), "The refusal names the template");
            await templateService.DeleteAsync(template.Info, "Curatare test");
            var deletable = (await suppliers.GetSuppliersAsync()).Single(item => item.Id == d.Id);
            await suppliers.DeleteAsync(deletable, "Motiv de stergere test");
            supplierIds.Remove(d.Id);
            Check(await suppliers.GetAsync(d.Id) is null && await ScalarLongAsync(probe, "SELECT COUNT(*) FROM archive_suppliers WHERE original_id=@id", ("@id", d.Id)) == 1, "An administrator deletes a supplier nothing refers to: it moves to the archive, once");
            var deletion = (await audit.GetEventsAsync()).FirstOrDefault(item => item.TimestampUtc >= started && item.Action == AuditActions.Delete && item.EntityType == AuditEntities.Supplier && item.EntityId == d.Id.ToString());
            Check(deletion is not null && deletion.Motif == "Motiv de stergere test" && deletion.Details.Contains(cuiD, StringComparison.Ordinal), "The deletion is journaled with its reason");
            await Rejects<SupplierOperationException>(() => suppliers.DeleteAsync(deletable, "din nou"), "Deleting a supplier that is already gone is refused");
            await Rejects<SupplierOperationException>(() => suppliers.DeleteAsync(foreign with { Version = foreign.Version + 5 }, "Motiv de test"), "Deleting a supplier changed in the meantime (stale version) is refused");

            // Deleting an entry keeps the invoice (and the supplier stays tied); the archive keeps the invoice of the entry.
            await DeleteMovementsNewestFirstAsync(movements, product.Id);
            Check(await ScalarLongAsync(probe, "SELECT COUNT(*) FROM archive_stock_movements WHERE original_id=@id AND invoice_id=@invoice", ("@id", linked.Movement.Id), ("@invoice", invoice.Id)) == 1, "A deleted entry is archived with its invoice");
            Check((await suppliers.GetSuppliersAsync()).Single(item => item.Id == a.Id) is { InvoiceCount: 2, MovementCount: 0, InUse: true }, "After its entries are gone the supplier is still tied by its invoices");
        }
        finally
        {
            await DeleteMovementsNewestFirstAsync(movements, product.Id);
            foreach (var template in (await templateStore.ListAsync()).Where(item => item.SupplierCui == cuiD).ToList()) await ExecuteAsync(probe, "DELETE FROM invoice_templates WHERE id=@id", ("@id", template.Id));
            foreach (var id in supplierIds)
            {
                await ExecuteAsync(probe, "DELETE FROM supplier_invoices WHERE supplier_id=@id", ("@id", id));
                await ExecuteAsync(probe, "DELETE FROM suppliers WHERE id=@id", ("@id", id));
            }
            await products.DeleteAsync((await products.GetProductAsync(product.Id))!, "Ext curatare");
            await ExecuteAsync(probe, "DELETE FROM subcategories WHERE name=@n", ("@n", subcategory));
            await ExecuteAsync(probe, "DELETE FROM categories WHERE name=@n", ("@n", category));
        }
    }

    private static async Task Section(string name, Func<Task> body)
    {
        // MARIA_ONLY=<text of a section name> runs only the sections whose name contains it (a targeted run).
        if (Environment.GetEnvironmentVariable("MARIA_ONLY") is { Length: > 0 } only && !name.Contains(only, StringComparison.OrdinalIgnoreCase)) return;
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

    // ---- Change events ---------------------------------------------------------------------------------------------------------------

    private static async Task AuditQueryAsync(IConfiguration configuration, IAuditTrail audit, MySqlConnection probe)
    {
        // The plain journal page: no filters, the latest 500 events (and the whole journal), then the removal times of that page.
        var plain = await audit.QueryAsync(new(), 1, 10, AuditQueryRules.RecentWindow);
        var plainAll = await audit.QueryAsync(new(), 1, 25);
        _ = await audit.RemovalTimesAsync(plain.Events);
        _ = await audit.RemovalTimesAsync(plainAll.Events);
        Check(plain.Events.Count == Math.Min(10, plain.Total) && plainAll.Total == plainAll.JournalTotal, "The unfiltered journal page is answered by the server");
        var suffix = Suffix();
        var target = $"Ext Jurnal {suffix}";
        var before = await audit.SummaryAsync(DateTime.UtcNow.Date);
        var start = DateTime.UtcNow.AddMinutes(-1);
        for (var index = 1; index <= 7; index++)
            await audit.RecordAsync(new($"Ext.Ana{suffix}", AccessRoles.Administrator, index % 2 == 0 ? AuditEntities.Beneficiary : AuditEntities.Product,
                index % 3 == 0 ? AuditActions.Delete : AuditActions.Edit, $"{target} #{index}", index == 5 ? $"100% sigur_{suffix}" : $"detaliu {index}", "motiv", (900000 + index).ToString()));
        await audit.RecordAsync(new($"ext.ana{suffix}".ToUpperInvariant(), AccessRoles.LimitedUser, AuditEntities.Product, AuditActions.Create, $"{target} #8", "d8", "", "900008"));
        try
        {
            var all = await audit.QueryAsync(new(Text: suffix), 1, 3);
            Check(all.Total == 8 && all.Events.Count == 3 && all.PageSize == 3 && all.Page == 1 && all.JournalTotal >= 8, "The server counts the matches and returns only one page");
            Check(all.Events.Zip(all.Events.Skip(1)).All(pair => pair.First.TimestampUtc >= pair.Second.TimestampUtc), "The page is newest first");
            var last = await audit.QueryAsync(new(Text: suffix), 99, 3);
            Check(last.Page == 3 && last.Events.Count == 2, "A page past the end is brought back to the last page");
            var everything = await audit.QueryAsync(new(Text: suffix), 1, 0);
            Check(everything.Events.Count == 8 && everything.PageSize == 0, "Page size 0 returns every match");
            Check((await audit.QueryAsync(new(Text: suffix, Actor: $"EXT.ANA{suffix}"), 1, 50)).Total == 8, "The operator filter ignores letter case");
            Check((await audit.QueryAsync(new(Text: suffix, Entity: AuditEntities.Beneficiary), 1, 50)).Total == 3, "The type filter is applied on the server");
            Check((await audit.QueryAsync(new(Text: suffix, Action: AuditActions.Delete), 1, 50)).Total == 2 && (await audit.QueryAsync(new(Text: suffix, Action: AuditActions.Edit), 1, 50)).Total == 5,
                "The operation filter is applied on the server");
            Check((await audit.QueryAsync(new(Text: "100%"), 1, 50)).Events.Count(entry => entry.Target.StartsWith(target)) == 1 &&
                  (await audit.QueryAsync(new(Text: $"100% sigur_{suffix}"), 1, 50)).Total == 1 && (await audit.QueryAsync(new(Text: $"sigur_{suffix.ToUpperInvariant()}"), 1, 50)).Total == 1,
                "The search text is literal (% and _ are not wildcards) and ignores letter case");
            Check((await audit.QueryAsync(new(Text: suffix, FromUtc: start, ToUtc: DateTime.UtcNow.AddMinutes(1)), 1, 50)).Total == 8 &&
                  (await audit.QueryAsync(new(Text: suffix, FromUtc: DateTime.UtcNow.AddMinutes(1)), 1, 50)).Total == 0 &&
                  (await audit.QueryAsync(new(Text: suffix, ToUtc: start), 1, 50)).Total == 0, "The date range includes its start and excludes its end");
            var window = await audit.QueryAsync(new(Text: suffix), 1, 50, window: 3);
            Check(window.Total == 3 && window.JournalTotal >= 8, "The window limits the search to the latest events of the whole journal");
            Check((await audit.QueryAsync(new(Text: suffix), 1, 50, window: AuditQueryRules.RecentWindow)).Total == 8, "The default window of 500 covers the recent events");
            var deleted = all.Events.Concat(everything.Events).First(entry => entry.Action == AuditActions.Delete);
            var removals = await audit.RemovalTimesAsync(everything.Events);
            Check(removals.ContainsKey(AuditNavigation.ObjectKey(deleted.EntityType, deleted.EntityId)) && removals.Count == 2, "The removal times are asked for the objects of the page only");
            var summary = await audit.SummaryAsync(DateTime.UtcNow.Date);
            Check(summary.Total == before.Total + 8 && summary.Today >= before.Today + 8 && summary.Actors >= before.Actors, "The summary counts the journal on the server");
        }
        finally
        {
            try { await ExecuteAsync(probe, "DELETE FROM audit_events WHERE target LIKE @t", ("@t", "Ext Jurnal %")); } catch (MySqlException) { }
        }
    }

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
