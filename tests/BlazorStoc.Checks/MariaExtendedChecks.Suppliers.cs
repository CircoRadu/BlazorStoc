using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using BlazorStoc.Services;

namespace BlazorStoc.Checks;

public static partial class MariaExtendedChecks
{
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

    private static async Task SupplierProductCodesAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe)
    {
        var suffix = Suffix();
        var started = DateTime.UtcNow.AddSeconds(-1);
        var products = new MariaProductRepository(configuration, admin, audit);
        var suppliers = new MariaSupplierRepository(configuration, admin, audit);
        var codes = new MariaSupplierProductCodes(configuration, admin, audit);
        var category = $"Ext Coduri Cat {suffix}";
        var subcategory = $"Ext Coduri Sub {suffix}";
        await products.CreateCategoryAsync(category);
        await products.CreateSubcategoryAsync(category, subcategory);
        var first = await products.CreateAsync(new ProductInput { Name = $"CF-{suffix}-1", Category = category, Subcategory = subcategory });
        var second = await products.CreateAsync(new ProductInput { Name = $"CF-{suffix}-2", Category = category, Subcategory = subcategory });
        var supplier = await suppliers.CreateAsync(new SupplierInput { Name = $"Furnizor Coduri {suffix} SRL", Cui = SupplierChecks.ValidCui(Random.Shared.Next(1_000_000, 9_999_999)) });
        try
        {
            var code = $"INT-{suffix}";
            Check(await codes.LinkAsync(supplier.Id, code, first.Id) && (await codes.GetAsync(supplier.Id)).GetValueOrDefault(SupplierProductCodeRules.Key(code)) == first.Id,
                "A supplier code is linked to a product and read back by its key (case, dash and spaces do not matter)");
            Check(!await codes.LinkAsync(supplier.Id, $"int {suffix}", first.Id), "Linking the same code to the same product again writes nothing");
            Check(await codes.LinkAsync(supplier.Id, code, second.Id) && (await codes.GetAsync(supplier.Id)).Single().Value == second.Id &&
                  await ScalarLongAsync(probe, "SELECT COUNT(*) FROM supplier_product_codes WHERE supplier_id=@id", ("@id", supplier.Id)) == 1,
                "Linking the code to another product changes the link: still one row for the supplier and code");
            var events = (await audit.GetEventsAsync()).Where(item => item.TimestampUtc >= started && item.EntityType == AuditEntities.Supplier && item.EntityId == supplier.Id.ToString()).ToList();
            Check(events.Any(item => item.Action == AuditActions.LinkSupplierProductCode) && events.FirstOrDefault(item => item.Action == AuditActions.ChangeSupplierProductCode) is { } change &&
                  change.Details.Contains(first.Name, StringComparison.Ordinal) && change.Details.Contains(second.Name, StringComparison.Ordinal),
                "The link and the change of the link are journaled as their own operations, the change with the product before and after");
            Check(!await codes.LinkAsync(supplier.Id, "A", first.Id), "A code of one character is not worth a link");
            await products.DeleteAsync((await products.GetProductAsync(second.Id))!, "Ext curatare");
            Check((await codes.GetAsync(supplier.Id)).Count == 0, "The link goes with the product when the product is deleted");
        }
        finally
        {
            foreach (var id in new[] { first.Id, second.Id })
                if (await products.GetProductAsync(id) is { } leftover) await products.DeleteAsync(leftover, "Ext curatare");
            if (await suppliers.GetAsync(supplier.Id) is { } supplierLeft) await suppliers.DeleteAsync(supplierLeft, "Curatare test");
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
            var withInvoice = await movements.GetPageAsync(product.Id, new StockMovementQuery(StockMovementKind.Entry, PageSize: 50, Source: EntrySource.WithInvoice));
            var onlyFree = await movements.GetPageAsync(product.Id, new StockMovementQuery(StockMovementKind.Entry, PageSize: 50, Source: EntrySource.Free));
            var bySupplier = await movements.GetPageAsync(product.Id, new StockMovementQuery(StockMovementKind.Entry, PageSize: 50, SupplierId: a.Id));
            var byText = await movements.GetPageAsync(product.Id, new StockMovementQuery(PageSize: 50, Text: bySupplier.Items.First().SupplierName));
            var byWildcard = await movements.GetPageAsync(product.Id, new StockMovementQuery(PageSize: 50, Text: "%"));
            var byNothing = await movements.GetPageAsync(product.Id, new StockMovementQuery(PageSize: 50, Text: "text-care-nu-exista-nicaieri"));
            var dayOfEntry = bySupplier.Items.First().Date;
            var byDay = await movements.GetPageAsync(product.Id, new StockMovementQuery(PageSize: 50, Date: dayOfEntry));
            var byOtherDay = await movements.GetPageAsync(product.Id, new StockMovementQuery(PageSize: 50, Date: new DateOnly(2001, 1, 1)));
            Check(byDay.TotalCount > 0 && byDay.Items.All(item => item.Date == dayOfEntry) && byOtherDay.TotalCount == 0, "Movement list: the date filter returns only the movements of that day");
            Check(byText.TotalCount >= bySupplier.TotalCount && bySupplier.TotalCount > 0 && byWildcard.TotalCount == 0 && byNothing.TotalCount == 0 && (await movements.GetPageAsync(product.Id, new StockMovementQuery(PageSize: 50, Text: "  "))).TotalCount == (await movements.GetPageAsync(product.Id, new StockMovementQuery(PageSize: 50))).TotalCount,
                "Movement list: the text filter finds the movements by supplier name, takes % literally, and a blank text filters nothing");
            Check(withInvoice.Items.Count == 1 && withInvoice.TotalCount == 1 && onlyFree.Items.Count == 1 && !onlyFree.Items[0].HasInvoice && bySupplier.Items.Count == 1
                  && withInvoice.Suppliers is { Count: 1 } && withInvoice.Suppliers[0].Id == a.Id, "The entry filters (with invoice / free / by supplier) and the supplier list work");
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
}
