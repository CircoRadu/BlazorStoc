using AngleSharp.Html.Dom;
using BlazorStoc.Components.Pages;
using BlazorStoc.Services;
using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorStoc.Checks;

// Suppliers (Administrare -> Furnizori) and the invoices that stock entries are taken from: the pure rules, the form with ANAF answering in every
// way, and the invoice pickup that records the invoice and ties its entries to it. The database side is in MariaExtendedChecks.
public static class SupplierChecks
{
    // A CUI of seven digits with a correct control digit (the same weights the application checks).
    public static string ValidCui(long body)
    {
        var digits = body.ToString(System.Globalization.CultureInfo.InvariantCulture);
        for (var check = 0; check < 10; check++)
            if (SupplierRules.HasValidCheckDigit(digits + check)) return digits + check;
        throw new InvalidOperationException("No control digit");
    }

    private static Supplier Existing(int id, string name, string cui, string source = SupplierSources.Manual, int invoices = 0, string country = "RO") =>
        new(id, name, cui, country, 0, "Strada Test 1", "0721000111", "", "", "", source, null, invoices, invoices, 0);

    public static void Rules(Action<bool, string> check)
    {
        // The control digit of the CUI: real CUIs pass, a mistyped digit does not.
        check(SupplierRules.HasValidCheckDigit("9178894") && SupplierRules.HasValidCheckDigit("22460883") && !SupplierRules.HasValidCheckDigit("9178895") &&
              !SupplierRules.HasValidCheckDigit("1") && !SupplierRules.HasValidCheckDigit("12345678901") && !SupplierRules.HasValidCheckDigit("91788a4"),
            "Suppliers: the control digit of a Romanian CUI is checked (a mistyped digit, a short or a long code are refused)");
        check(new[] { "RO 9178894", "ro9178894", " 9178894 ", "RO09178894", "R.O. 9178894" }.All(text => SupplierRules.CuiDigits(text) == "9178894") &&
              SupplierRules.IdentityKey("RO", "RO 9178894") == SupplierRules.IdentityKey("RO", "9178894"),
            "Suppliers: a CUI with or without the RO prefix, spaces or leading zeros is the same supplier");
        check(new[] { "RO12345678", "ro 12345678", "12345678", "RO012345678" }.Select(cui => BeneficiaryRules.IdentityKey(new BeneficiaryInput { Kind = BeneficiaryKinds.Legal, Name = "X", Cui = cui })).Distinct().Count() == 1,
            "Beneficiaries: RO123 and 123 are the same key");
        check(SupplierRules.IdentityKey("DE", "de 123 456 789") == "DE123456789" && SupplierRules.IdentityKey("DE", "123456789") == "DE123456789" &&
              SupplierRules.IdentityKey("EL", "123456789") == "EL123456789" && SupplierRules.IdentityKey("RO", "9178894") != SupplierRules.IdentityKey("DE", "9178894"),
            "Suppliers: a foreign VAT identifier is kept with its country prefix (added when missing) and never equals a Romanian CUI");
        check(SupplierRules.CuiProblem("RO", "9178894") is null && SupplierRules.CuiProblem("RO", "9178895")!.Contains("cifra de control") && SupplierRules.CuiProblem("RO", "")!.Contains("CUI") &&
              SupplierRules.CuiProblem("RO", "abc") is not null && SupplierRules.CuiProblem("DE", "123456789") is null && SupplierRules.CuiProblem("DE", "1") is not null && SupplierRules.CuiProblem("DE", "")!.Contains("TVA"),
            "Suppliers: the messages for a missing, mistyped or malformed CUI / VAT identifier");

        var valid = new SupplierInput { Name = "  Furnizor   Test  SRL ", Cui = "RO 9178894", Phone = "0721 000 111", RegistryNumber = "j40/1/2020", Source = SupplierSources.Anaf };
        var normalized = valid.Validated();
        check(normalized.Name == "Furnizor Test SRL" && normalized.Cui == "9178894" && normalized.Phone == "0721000111" && normalized.RegistryNumber == "J40/1/2020" && normalized.Source == SupplierSources.Anaf,
            "Suppliers: the form values are cleaned (name spaces, CUI digits, phone digits, registry number upper case)");
        var foreign = new SupplierInput { Country = "DE", Name = "Lieferant GmbH", Cui = "123456789", Source = SupplierSources.Anaf, AnafRecheck = true }.Validated();
        check(foreign.Cui == "DE123456789" && foreign.Source == SupplierSources.Manual && !foreign.AnafRecheck, "Suppliers: a foreign supplier is always \"introdus manual\" (never looked up in ANAF)");
        check(Throws(() => new SupplierInput { Name = "", Cui = "9178894" }.Validated()) && Throws(() => new SupplierInput { Name = "X", Cui = "9178895" }.Validated()) &&
              Throws(() => new SupplierInput { Country = "US", Name = "X", Cui = "123" }.Validated()) && Throws(() => new SupplierInput { Name = "X", Cui = "9178894", Phone = "12" }.Validated()) &&
              Throws(() => new SupplierInput { Name = "X", Cui = "9178894", PostalCode = "12" }.Validated()) && Throws(() => new SupplierInput { Name = "X", Cui = "9178894", Source = "Z" }.Validated()),
            "Suppliers: no name, a wrong control digit, a country outside the EU, a bad phone, postal code or source are refused");
        check(Throws(() => new SupplierInput { Name = "X", Cui = "9178894" }.Validated(true)) && new SupplierInput { Name = "X", Cui = "9178894", Reason = "Motiv de test" }.Validated(true).Reason == "Motiv de test",
            "Suppliers: an edit needs its reason");
        check(new SupplierInput { Name = "X", Cui = "9178894" }.Validated().Address.Length == 0, "Suppliers: only the name and the CUI are required (address and phone are optional)");

        // The source of the data after an edit (what the form knows about ANAF).
        check(SupplierRules.ResolveSource(false, true, true, null, null) == SupplierSources.Anaf &&
              SupplierRules.ResolveSource(false, true, false, null, null) == SupplierSources.AnafEdited &&
              SupplierRules.ResolveSource(false, false, false, AnafLookupOutcome.Unavailable, null) == SupplierSources.ManualUnavailable &&
              SupplierRules.ResolveSource(false, false, false, AnafLookupOutcome.NotFound, null) == SupplierSources.ManualNotFound &&
              SupplierRules.ResolveSource(false, false, false, null, SupplierSources.ManualUnavailable) == SupplierSources.ManualUnavailable &&
              SupplierRules.ResolveSource(false, false, false, null, SupplierSources.AnafEdited) == SupplierSources.AnafEdited &&
              SupplierRules.ResolveSource(false, false, false, null, SupplierSources.Anaf) == SupplierSources.Manual &&
              SupplierRules.ResolveSource(false, false, false, null, null) == SupplierSources.Manual &&
              SupplierRules.ResolveSource(true, true, true, null, SupplierSources.Anaf) == SupplierSources.Manual,
            "Suppliers: the source is ANAF while unchanged, edited when changed by hand, manual with the reason when ANAF could not answer or does not know the CUI");
        check(SupplierSources.Label(SupplierSources.ManualUnavailable).Contains("ANAF indisponibil") && SupplierSources.Label(SupplierSources.ManualNotFound).Contains("negăsit") &&
              SupplierSources.NeedsCheck(SupplierSources.AnafEdited) && SupplierSources.NeedsCheck(SupplierSources.ManualUnavailable) && !SupplierSources.NeedsCheck(SupplierSources.Anaf) && !SupplierSources.NeedsCheck(SupplierSources.Manual),
            "Suppliers: the labels of the sources and which of them are to be checked again");

        // Search and the filter for the suppliers to check.
        var list = new[] { Existing(1, "Alfa SRL", "9178894", SupplierSources.Anaf), Existing(2, "Beta SRL", "22460883", SupplierSources.ManualUnavailable), Existing(3, "Gamma GmbH", "DE123456789", country: "DE") };
        check(SupplierSearch.Filter(list, "ro 9178").Single().Id == 1 && SupplierSearch.Filter(list, "de123").Single().Id == 3 && SupplierSearch.Filter(list, "beta").Single().Id == 2 &&
              SupplierSearch.Filter(list, "", true).Single().Id == 2 && SupplierSearch.Filter(list, "").Count() == 3 && SupplierSearch.Filter(list, "0721000111").Count() == 3,
            "Suppliers: the search finds by name, CUI (with or without RO), VAT identifier and phone; the filter keeps those to check");
        check(SupplierRules.DeleteBlockedMessage(1, 3, 0) == "Furnizorul are o factură, 3 intrări de stoc legate și nu poate fi șters." && SupplierRules.DeleteBlockedMessage(2, 0, 1).Contains("2 facturi") && SupplierRules.DeleteBlockedMessage(2, 0, 1).Contains("un șablon de factură") &&
              Existing(1, "A", "9178894", invoices: 1).InUse && !Existing(1, "A", "9178894").InUse,
            "Suppliers: the message that names what ties a supplier (invoices, entries, templates), and \"in use\"");
        check(Throws(() => SupplierRules.CheckCurrent(null, list[0])) && Throws(() => SupplierRules.CheckCurrent(list[0] with { Version = 5 }, list[0])) && !Throws(() => SupplierRules.CheckCurrent(list[0] with { InvoiceCount = 4 }, list[0])),
            "Suppliers: a supplier changed or deleted in the meantime is refused, while the counts of its invoices do not count as a change");

        // Invoices.
        var invoice = SupplierInvoiceRules.Validated(new SupplierInvoiceInput { SupplierId = 1, Number = "  FT   1042 ", Date = new DateOnly(2026, 10, 5) }, new DateOnly(2026, 10, 6));
        check(invoice.Number == "FT 1042" && SupplierInvoiceRules.NumberKey("FT 1042") == SupplierInvoiceRules.NumberKey("ft1042") && SupplierInvoiceRules.NumberKey("FT 1042") != SupplierInvoiceRules.NumberKey("FT 1043"),
            "Invoices: a number is cleaned, and writings that differ only in spaces or letter case are the same invoice");
        check(SupplierInvoiceRules.LooksLike("FT 1O42", "FT 1042") && SupplierInvoiceRules.LooksLike("FT I", "FT 1") && SupplierInvoiceRules.LooksLike("FT 1043", "FT 1042") && SupplierInvoiceRules.LooksLike("FT 10422", "FT 1042") &&
              SupplierInvoiceRules.LooksLike("FT 142", "FT 1042") && !SupplierInvoiceRules.LooksLike("ft1042", "FT 1042") && !SupplierInvoiceRules.LooksLike("A1", "A2") && !SupplierInvoiceRules.LooksLike("FT 1042", "GH 7788") && !SupplierInvoiceRules.LooksLike("", "FT 1"),
            "Invoices: a number that looks like another one (OCR look-alikes, one character more, less or different) is told apart from the same number and from different ones");
        var today = new DateOnly(2026, 10, 6);
        check(Throws(() => SupplierInvoiceRules.Validated(new SupplierInvoiceInput { SupplierId = null, Number = "1", Date = today }, today)) && Throws(() => SupplierInvoiceRules.Validated(new SupplierInvoiceInput { SupplierId = 1, Number = " ", Date = today }, today)) &&
              Throws(() => SupplierInvoiceRules.Validated(new SupplierInvoiceInput { SupplierId = 1, Number = "---", Date = today }, today)) && Throws(() => SupplierInvoiceRules.Validated(new SupplierInvoiceInput { SupplierId = 1, Number = "1", Date = null }, today)) &&
              Throws(() => SupplierInvoiceRules.Validated(new SupplierInvoiceInput { SupplierId = 1, Number = "1", Date = today.AddDays(1) }, today)) && Throws(() => SupplierInvoiceRules.Validated(new SupplierInvoiceInput { SupplierId = 1, Number = new string('9', 51), Date = today }, today)),
            "Invoices: no supplier, no or meaningless number, no date, a future date and a number that is too long are refused");

        // Entries: tied to an invoice or free.
        var entry = StockMovementRules.Validated(new StockMovementInput { Kind = StockMovementKind.Entry, Date = today, Quantity = 2, Description = "Intrare", InvoiceId = 7 }, StockMovementKind.Entry, false, today);
        var free = StockMovementRules.Validated(new StockMovementInput { Kind = StockMovementKind.Entry, Date = today, Quantity = 2, Description = "Intrare libera" }, StockMovementKind.Entry, false, today);
        check(entry.InvoiceId == 7 && free.InvoiceId is null &&
              Throws(() => StockMovementRules.Validated(new StockMovementInput { Kind = StockMovementKind.Exit, Date = today, Quantity = 1, Description = "x", Destination = ExitDestination.GenericSale, InvoiceId = 7 }, StockMovementKind.Exit, false, today)) &&
              Throws(() => StockMovementRules.Validated(new StockMovementInput { Kind = StockMovementKind.Entry, Date = today, Quantity = 1, Description = "x", InvoiceId = 0 }, StockMovementKind.Entry, false, today)),
            "Entries: an entry carries its invoice or none (a free entry); an exit cannot carry an invoice");
        var movement = new StockMovement(1, 2, StockMovementKind.Entry, 3, today, "Intrare", null, null, null, null, "test", 0, DateTime.UtcNow, DateTime.UtcNow, false, null, null, null, null, null, 9, "FT 1042", "Furnizor Test SRL", 4);
        check(StockMovementRules.AuditIdentification(movement, "COD").Contains("Factură: FT 1042 · Furnizor Test SRL") && movement.HasInvoice && StockMovementInput.From(movement).InvoiceId == 9,
            "Entries: the journal names the invoice and the supplier of an entry");

        // The journal.
        var filter = AuditFilterOptions.Actions.Select(item => item.Value).ToArray();
        var supplierEvent = new AuditEvent(Guid.NewGuid(), DateTime.UtcNow, "ion", AccessRoles.LimitedUser, AuditEntities.Supplier, AuditActions.CreateSupplier, "#5 · Furnizor", "", "", "5");
        var invoiceEvent = supplierEvent with { EntityType = AuditEntities.SupplierInvoice, Action = AuditActions.RecordSupplierInvoice, EntityId = "11" };
        check(new[] { AuditActions.CreateSupplier, AuditActions.EditSupplier, AuditActions.RecheckSupplier, AuditActions.RecordSupplierInvoice }.All(filter.Contains) &&
              AuditFilterOptions.Entities.Any(item => item.Value == AuditEntities.Supplier) && AuditFilterOptions.Entities.Any(item => item.Value == AuditEntities.SupplierInvoice) &&
              AuditActions.IsCreateOrEdit(AuditActions.RecheckSupplier) && AuditNavigation.TargetUrl(supplierEvent) == "/furnizori/5" && AuditNavigation.TargetUrl(invoiceEvent) is null &&
              AuditActions.CreateSupplier != AuditActions.Create && AuditActions.EditSupplier != AuditActions.Edit,
            "Journal: every supplier operation has its own exact action and a filter, the supplier events link to its page");

        // ANAF: "unknown to ANAF" is told apart from an outage.
        var config = new AnafConfig();
        var request = AnafRules.Prepare(config, "22460883", new DateOnly(2026, 10, 6));
        var unknownNumber = AnafRules.Interpret(config, request, 200, 5, """{"cod":200,"found":[],"notFound":[22460883]}""");
        var unknownObject = AnafRules.Interpret(config, request, 200, 5, """{"cod":200,"found":[],"notFound":[{"cui":22460883,"data":"2026-10-06"}]}""");
        var otherUnknown = AnafRules.Interpret(config, request, 200, 5, """{"cod":200,"found":[],"notFound":[111]}""");
        var garbage = AnafRules.Interpret(config, request, 200, 5, "<html>eroare</html>");
        check(!unknownNumber.Ok && unknownNumber.CuiNotFound && unknownObject.CuiNotFound && !otherUnknown.CuiNotFound && !garbage.CuiNotFound && unknownNumber.Errors.Single().Contains("nu este înregistrat în ANAF"),
            "ANAF: a CUI listed among those unknown to ANAF is \"not found\" (not an outage); another CUI or an unreadable answer is not");
    }

    private static bool Throws(Action action)
    {
        try { action(); return false; }
        catch (Exception exception) when (exception is SupplierOperationException or SupplierInvoiceOperationException or StockMovementOperationException) { return true; }
    }

    private static BunitContext EditorContext(MemorySupplierRepository suppliers, ScriptedAnaf anaf)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddLogging();
        context.Services.AddSingleton<ISupplierRepository>(suppliers);
        context.Services.AddSingleton<IAnafService>(anaf);
        context.Services.AddScoped<UnsavedChanges>();
        return context;
    }

    private static AnafCompanyResult FoundResult(string cui) =>
        new(new AnafCompany(cui, "TEST FURNIZOR SRL", "Str. Test 1, Bucuresti", "J40/1/2020", "0721000111", "010101", "4711"), null, null, AnafLookupOutcome.Found);

    public static async Task ComponentsAsync(Action<bool, string> check)
    {
        var cui = ValidCui(917889);

        // ANAF found: the form is filled, "from ANAF" while unchanged, "edited" once a value is changed by hand, back to "from ANAF" when restored.
        {
            var repository = new MemorySupplierRepository();
            var anaf = new ScriptedAnaf(FoundResult);
            using var context = EditorContext(repository, anaf);
            Supplier? saved = null;
            var cut = context.Render<SupplierEditor>(parameters => parameters.Add(p => p.Saved, (Supplier value) => { saved = value; }));
            cut.Find("#supplier-cui").Input(cui);
            cut.FindAll("button").First(button => button.TextContent.Contains("Preia date din ANAF")).Click();
            cut.WaitForAssertion(() => { if (cut.Find("#supplier-name").GetAttribute("value") != "TEST FURNIZOR SRL") throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
            check(cut.Find(".anaf-source").TextContent.Contains("Date preluate din ANAF") && cut.Find("#supplier-address").GetAttribute("value") == "Str. Test 1, Bucuresti" && cut.Find("#supplier-phone").GetAttribute("value") == "0721000111",
                "Supplier form: the data read from ANAF fills the form and is marked as taken from ANAF");
            cut.Find("#supplier-address").Input("Alta adresa 5");
            check(cut.Find(".anaf-source").TextContent.Contains("editate manual"), "Supplier form: changing a value taken from ANAF marks the data as edited by hand");
            cut.Find("#supplier-address").Input("Str. Test 1, Bucuresti");
            check(cut.Find(".anaf-source").TextContent.Contains("Date preluate din ANAF"), "Supplier form: putting the ANAF value back makes the data \"from ANAF\" again");
            cut.Find("#supplier-address").Input("Alta adresa 5");
            cut.Find("form").Submit();
            cut.WaitForAssertion(() => { if (saved is null) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
            check(saved!.Source == SupplierSources.AnafEdited && saved.Cui == cui && repository.Items.Count == 1 && anaf.Calls.Count == 1 && saved.VerifiedUtc is not null,
                "Supplier form: saved as \"taken from ANAF, edited by hand\" without asking ANAF again");
        }

        // A new supplier typed by hand: ANAF is asked at save. Not available / not known -> saved as manual with the reason.
        foreach (var (outcome, expected, label) in new[]
        {
            (AnafLookupOutcome.Unavailable, SupplierSources.ManualUnavailable, "ANAF cannot answer"),
            (AnafLookupOutcome.NotFound, SupplierSources.ManualNotFound, "ANAF does not know the CUI")
        })
        {
            var repository = new MemorySupplierRepository();
            var anaf = new ScriptedAnaf(_ => new(null, "Mesaj de test", null, outcome));
            using var context = EditorContext(repository, anaf);
            Supplier? saved = null;
            var cut = context.Render<SupplierEditor>(parameters => parameters.Add(p => p.Saved, (Supplier value) => { saved = value; }));
            cut.Find("#supplier-cui").Input(cui);
            cut.Find("#supplier-name").Input("Furnizor Manual SRL");
            cut.Find("form").Submit();
            cut.WaitForAssertion(() => { if (saved is null) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
            check(saved!.Source == expected && saved.VerifiedUtc is null && anaf.Calls.Count == 1 && repository.Items.Single().Name == "Furnizor Manual SRL",
                $"Supplier form: when {label}, the supplier is saved as entered by hand with the reason recorded ({SupplierSources.Label(expected)})");
        }

        // ANAF answers at save: the form is filled and shown for a last look, nothing is saved yet; the second save keeps ANAF's data.
        {
            var repository = new MemorySupplierRepository();
            var anaf = new ScriptedAnaf(FoundResult);
            using var context = EditorContext(repository, anaf);
            Supplier? saved = null;
            var cut = context.Render<SupplierEditor>(parameters => parameters.Add(p => p.Saved, (Supplier value) => { saved = value; }));
            cut.Find("#supplier-cui").Input(cui);
            cut.Find("#supplier-name").Input("Nume tastat");
            cut.Find("form").Submit();
            cut.WaitForAssertion(() => { if (!cut.Markup.Contains("Verifică-le și apasă din nou")) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
            check(saved is null && repository.Items.Count == 0 && cut.Find("#supplier-name").GetAttribute("value") == "TEST FURNIZOR SRL",
                "Supplier form: a CUI known to ANAF is not saved with typed data: the form is filled from ANAF and shown first");
            cut.Find("form").Submit();
            cut.WaitForAssertion(() => { if (saved is null) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
            check(saved!.Source == SupplierSources.Anaf && anaf.Calls.Count == 1, "Supplier form: the second save keeps the data from ANAF (ANAF is asked once)");
        }

        // A supplier of another member state: no ANAF, the VAT identifier gets its prefix, the data is manual.
        {
            var repository = new MemorySupplierRepository();
            var anaf = new ScriptedAnaf(FoundResult);
            using var context = EditorContext(repository, anaf);
            Supplier? saved = null;
            var cut = context.Render<SupplierEditor>(parameters => parameters.Add(p => p.Saved, (Supplier value) => { saved = value; }));
            cut.Find("#supplier-country").Change("DE");
            check(!cut.FindAll("button").Any(button => button.TextContent.Contains("ANAF")) && cut.Markup.Contains("Cod de TVA") && cut.FindAll("#supplier-postal").Count == 0,
                "Supplier form: for another member state there is no ANAF button and the tax id is a VAT identifier");
            cut.Find("#supplier-cui").Input("123 456 789");
            cut.Find("#supplier-name").Input("Lieferant GmbH");
            cut.Find("form").Submit();
            cut.WaitForAssertion(() => { if (saved is null) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
            check(saved!.Cui == "DE123456789" && saved.Country == "DE" && saved.Source == SupplierSources.Manual && anaf.Calls.Count == 0 && saved.IsExternal,
                "Supplier form: a foreign supplier is saved with its VAT identifier, as entered by hand, without asking ANAF");
        }

        // A CUI with a wrong control digit is refused before anything else.
        {
            var repository = new MemorySupplierRepository();
            var anaf = new ScriptedAnaf(FoundResult);
            using var context = EditorContext(repository, anaf);
            var cut = context.Render<SupplierEditor>();
            cut.Find("#supplier-cui").Input("9178895");
            cut.Find("#supplier-name").Input("Furnizor cu CUI gresit");
            cut.Find("form").Submit();
            check(cut.Markup.Contains("cifra de control") && repository.Items.Count == 0 && anaf.Calls.Count == 0, "Supplier form: a CUI with a wrong control digit is refused (nothing is saved, ANAF is not asked)");
            var empty = context.Render<SupplierEditor>();
            check(empty.FindAll("button").First(button => button.TextContent.Contains("Preia date din ANAF")).HasAttribute("disabled"), "Supplier form: the ANAF button waits for a CUI");
        }

        // A duplicate is refused with the name of the stored supplier.
        {
            var repository = new MemorySupplierRepository();
            repository.Items.Add(Existing(1, "Furnizor Existent SRL", cui));
            var anaf = new ScriptedAnaf(_ => new(null, "x", null, AnafLookupOutcome.Unavailable));
            using var context = EditorContext(repository, anaf);
            var cut = context.Render<SupplierEditor>();
            cut.Find("#supplier-cui").Input("RO " + cui);
            cut.Find("#supplier-name").Input("Alt nume");
            cut.Find("form").Submit();
            cut.WaitForAssertion(() => { if (!cut.Markup.Contains("Furnizor Existent SRL")) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
            check(cut.Find(".error-banner").TextContent.Contains("Există deja un furnizor cu acest CUI") && repository.Items.Count == 1, "Supplier form: the same CUI is refused with the name of the supplier already stored");
        }

        // Editing: a supplier taken from ANAF starts as such; the tax id of a supplier in use is locked.
        {
            var repository = new MemorySupplierRepository();
            var anaf = new ScriptedAnaf(FoundResult);
            using var context = EditorContext(repository, anaf);
            var verified = Existing(3, "Furnizor Verificat SRL", cui, SupplierSources.Anaf);
            var cut = context.Render<SupplierEditor>(parameters => parameters.Add(p => p.Original, verified));
            check(cut.Find(".anaf-source").TextContent.Contains("Date preluate din ANAF") && cut.FindAll("button").Any(button => button.TextContent.Contains("Preia din nou din ANAF")),
                "Supplier form (edit): a supplier taken from ANAF is shown as such and can be read again");
            cut.Find("#supplier-address").Input("Adresa schimbata");
            check(cut.Find(".anaf-source").TextContent.Contains("editate manual"), "Supplier form (edit): changing the address of a verified supplier marks it as edited by hand");
            var used = Existing(4, "Furnizor Folosit SRL", ValidCui(2246088), SupplierSources.Manual, invoices: 2);
            var locked = context.Render<SupplierEditor>(parameters => parameters.Add(p => p.Original, used));
            check(locked.Find("#supplier-cui").HasAttribute("disabled") && ((IHtmlSelectElement)locked.Find("#supplier-country")).IsDisabled && locked.Markup.Contains("nu mai pot fi schimbate") &&
                  !locked.Find("#supplier-name").HasAttribute("disabled"), "Supplier form (edit): the country and the CUI of a supplier with invoices are locked, the other data can be corrected");
        }
    }

    // ---- the invoice pickup: the invoice is recorded and every entry is tied to it ----

    private sealed class TestTessdata : IWebHostEnvironmentTessdataPath
    {
        public string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Tessdata");
    }

    private sealed class NoTemplates : IInvoiceTemplateService
    {
        public Task<IReadOnlyList<InvoiceTemplateRecord>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<InvoiceTemplateRecord>>([]);
        public Task<IReadOnlyList<InvoiceTemplateInfo>> ListAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InvoiceTemplateRecord?> GetAsync(int id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InvoiceTemplateModel?> GetModelAsync(int id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InvoiceTemplateRecord> CreateAsync(InvoiceTemplateInput input, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InvoiceTemplateRecord> SaveAsync(InvoiceTemplateInfo original, InvoiceTemplateInput input, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InvoiceTemplateInfo> SetActiveAsync(InvoiceTemplateInfo original, bool active, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InvoiceTemplateInfo> UpdateDetailsAsync(InvoiceTemplateInfo original, InvoiceTemplateInput input, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(InvoiceTemplateInfo original, string reason, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    // Drives the pickup (a generated invoice) to step 3: the product prepared, the number, the CUI and the date confirmed in step 2.
    private static async Task<IRenderedComponent<InvoicePickup>> ToStepThreeAsync(BunitContext context, byte[] pdf, string fileName)
    {
        var cut = await ToStepTwoAsync(context, pdf, fileName);
        SupplierTestServices.ConfirmHeader(cut);
        cut.WaitForAssertion(() => { if (cut.FindAll("button").Any(button => button.TextContent.Contains("Pasul următor") && button.HasAttribute("disabled"))) throw new Exception("pending"); }, TimeSpan.FromSeconds(10));
        cut.FindAll("button").First(button => button.TextContent.Contains("Pasul următor")).Click();
        cut.WaitForAssertion(() => cut.Find(".pickup-entry"), TimeSpan.FromSeconds(10));
        await Task.CompletedTask;
        return cut;
    }

    // Step 2 with a product prepared for the first row (the header fields are read and still "neverificat").
    private static async Task<IRenderedComponent<InvoicePickup>> ToStepTwoAsync(BunitContext context, byte[] pdf, string fileName)
    {
        var cut = context.Render<InvoicePickup>();
        cut.WaitForAssertion(() => cut.Find("input[type=file]"), TimeSpan.FromSeconds(10));
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary(pdf, fileName, null, "application/pdf"));
        void Click(string text) => cut.FindAll("button").First(button => button.TextContent.Contains(text)).Click();
        cut.WaitForAssertion(() => { if (!cut.FindAll("button").Any(button => button.TextContent.Contains("Pasul următor") && !button.HasAttribute("disabled"))) throw new Exception("pending"); }, TimeSpan.FromSeconds(60));
        Click("Pasul următor");
        cut.WaitForAssertion(() => { if (cut.FindAll("table.pickup-match").Count == 0) throw new Exception("pending"); }, TimeSpan.FromSeconds(10));
        // A product is prepared for the first row without a match in the catalog (a pickup repeated after the products were created finds them all).
        if (cut.FindAll("button").FirstOrDefault(button => button.TextContent.Contains("Pregătește un produs nou")) is { } prepare)
        {
            prepare.Click();
            cut.WaitForAssertion(() => cut.Find("#product-category"), TimeSpan.FromSeconds(10));
            cut.WaitForAssertion(() => { if (cut.FindAll("#product-subcategory option").Count == 0) throw new Exception("pending"); }, TimeSpan.FromSeconds(10));
            cut.Find("form").Submit();
            cut.WaitForAssertion(() => { if (!cut.Markup.Contains("Produs nou pregătit")) throw new Exception("pending"); }, TimeSpan.FromSeconds(10));
        }
        cut.WaitForAssertion(() => cut.Find("#pickup-supplier-cui"), TimeSpan.FromSeconds(10));
        await Task.CompletedTask;
        return cut;
    }

    private static BunitContext PickupContext(TestAccessControl access, InvoiceAnalysisStore store, DemoProductRepository products, IStockMovementRepository movements,
        MemorySupplierRepository suppliers, MemorySupplierInvoiceRepository invoices)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddLogging();
        context.Services.AddSingleton<IAccessControl>(access);
        context.Services.AddSingleton<IInvoiceAnalysisStore>(store);
        context.Services.AddScoped<IInvoiceAnalysisService>(_ => new InvoiceAnalysisService(new InvoicePdfReader(new TestTessdata()), store, access));
        context.Services.AddSingleton<IInvoiceTemplateService>(new NoTemplates());
        context.Services.AddSingleton<IProductRepository>(products);
        context.Services.AddSingleton(movements);
        context.Services.AddSingleton<IProductImageStore>(new DemoProductImageStore());
        context.Services.AddScoped<UnsavedChanges>();
        context.Services.AddSingleton(new InvoiceLabSettings(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build()));
        context.Services.AddSupplierFakes(suppliers, invoices);
        return context;
    }

    public static async Task PickupAsync(Action<bool, string> check)
    {
        var access = new TestAccessControl(true, "pickup.admin");
        var store = new InvoiceAnalysisStore(TimeProvider.System);
        var products = new DemoProductRepository(access);
        var movements = new FakeInventoryPickupMovementRepository(new Dictionary<int, int>());
        var suppliers = new MemorySupplierRepository();
        suppliers.Items.Add(Existing(1, "Furnizor Test SRL", "12345678", SupplierSources.Anaf));
        suppliers.Items.Add(Existing(2, "Alt Furnizor SRL", ValidCui(2246088), SupplierSources.Manual));
        var invoices = new MemorySupplierInvoiceRepository(suppliers);
        var invoice = InvoiceFixtures.Make(new InvoiceSpec("ro-lines", 4, SupplierName: "Furnizor Test SRL", SupplierCui: "RO12345678", Number: "FT 1", Date: new DateOnly(2026, 10, 5)), InvoiceFixtures.MakeRows(4, 5));

        using (var context = PickupContext(access, store, products, movements, suppliers, invoices))
        {
            var cut = await ToStepTwoAsync(context, invoice.Pdf, "factura-furnizor.pdf");
            bool NextDisabled() => cut.FindAll("button").First(button => button.TextContent.Contains("Pasul următor")).HasAttribute("disabled");
            IReadOnlyList<AngleSharp.Dom.IElement> Switches() => cut.FindAll(".pickup-invoice-head input[role=switch]");
            check(cut.Find("#pickup-supplier-cui").GetAttribute("value") == "12345678" && cut.Find("#pickup-invoice-number").GetAttribute("value") == "FT 1" && cut.Find("#pickup-invoice-date").GetAttribute("value") == "05.10.2026" &&
                  cut.Find("#pickup-supplier-name").TextContent.Contains("Furnizor Test SRL"),
                "Pickup (step 2): the number, the supplier's CUI and the date of issue are read from the invoice into editable fields, and the supplier of the CUI is shown");
            check(cut.FindAll(".pickup-invoice-head .anaf-source.warn").Count == 3 && cut.FindAll(".pickup-invoice-head .anaf-source").All(badge => badge.TextContent.Contains("Neverificat")) && Switches().Count == 3 &&
                  NextDisabled() && cut.Markup.Contains("confirmă numărul facturii, CUI-ul furnizorului și data"),
                "Pickup (step 2): the three fields start as \"neverificat\" and the next step is closed until each one is confirmed");

            // Each field is confirmed on its own.
            Switches()[0].Change(true);
            check(cut.FindAll(".pickup-invoice-head .anaf-source.ok").Count == 1 && NextDisabled(), "Pickup (step 2): confirming the number alone does not open the next step");
            Switches()[1].Change(true);
            Switches()[2].Change(true);
            check(cut.FindAll(".pickup-invoice-head .anaf-source.ok").Count == 3 && !NextDisabled(), "Pickup (step 2): with the number, the CUI and the date confirmed the next step opens");

            // Editing a field puts that field (only) back to \"neverificat\" and closes the step again.
            cut.Find("#pickup-invoice-number").Input("FT 1 ");
            check(cut.FindAll(".pickup-invoice-head .anaf-source.ok").Count == 2 && Switches()[0].GetAttribute("aria-checked") == "false" && NextDisabled(), "Pickup (step 2): editing the number makes it \"neverificat\" again (the other two stay confirmed)");
            Switches()[0].Change(true);
            cut.Find("#pickup-supplier-cui").Input("RO 12345678");
            check(cut.FindAll(".pickup-invoice-head .anaf-source.ok").Count == 2 && Switches()[1].GetAttribute("aria-checked") == "false" && cut.Find("#pickup-supplier-name").TextContent.Contains("Furnizor Test SRL") && NextDisabled(),
                "Pickup (step 2): editing the CUI makes it \"neverificat\" again; the supplier is found by the digits");
            Switches()[1].Change(true);
            cut.Find("#pickup-supplier-cui").Input("99999999");
            check(cut.Find("#pickup-supplier-name").TextContent.Contains("Niciun furnizor din registru") && Switches()[1].HasAttribute("disabled") && NextDisabled() && cut.Markup.Contains("nu este în registru"),
                "Pickup (step 2): a CUI that is not in the register cannot be confirmed (the supplier has to be added first)");
            cut.Find("#pickup-supplier-cui").Input("12345678");
            Switches()[1].Change(true);
            cut.Find("#pickup-invoice-number").Input("");
            check(Switches()[0].HasAttribute("disabled") && NextDisabled(), "Pickup (step 2): an empty number cannot be confirmed");
            cut.Find("#pickup-invoice-number").Input("FT 1");
            Switches()[0].Change(true);
            check(!NextDisabled(), "Pickup (step 2): everything confirmed again, the next step is open");
            var picked = cut.Find("input.pick-date-native");
            picked.Change("2026-10-04");
            check(cut.Find("#pickup-invoice-date").GetAttribute("value") == "04.10.2026" && Switches()[2].GetAttribute("aria-checked") == "false" && NextDisabled(), "Pickup (step 2): editing the date makes it \"neverificat\" again");
            picked = cut.Find("input.pick-date-native");
            picked.Change("2026-10-05");
            Switches()[2].Change(true);

            cut.FindAll("button").First(button => button.TextContent.Contains("Pasul următor")).Click();
            cut.WaitForAssertion(() => cut.Find(".pickup-entry"), TimeSpan.FromSeconds(10));
            check(cut.Markup.Contains("verificate la pasul 2") && cut.Markup.Contains("Furnizor Test SRL") && cut.Markup.Contains("FT 1") && cut.FindAll("#pickup-invoice-number").Count == 0,
                "Pickup (step 3): shows the invoice and the supplier checked in step 2 (they are not edited here)");

            cut.FindAll("button").First(button => button.TextContent.Contains("Finalizează preluarea")).Click();
            cut.WaitForAssertion(() => { if (!cut.Markup.Contains("Preluarea a fost finalizată")) throw new Exception("pending"); }, TimeSpan.FromSeconds(20));
            var recorded = invoices.Items.Single();
            check(recorded.Number == "FT 1" && recorded.SupplierId == 1 && recorded.Date == new DateOnly(2026, 10, 5) && movements.Created.Count >= 1 && movements.Created.All(item => item.Input.InvoiceId == recorded.Id),
                "Pickup: the invoice is recorded once (number, date, supplier) and every entry created is tied to it");
            check(cut.Markup.Contains("legate de factura FT 1 a furnizorului Furnizor Test SRL"), "Pickup: the result names the invoice and the supplier");
        }

        // A partly taken invoice is continued, not refused: what was taken is shown, the products already taken are skipped unless chosen again,
        // and the entries go to the same invoice (also when the number is written differently).
        foreach (var taken in movements.Created) invoices.Entries.Add(new InvoiceEntry(invoices.Entries.Count + 1, taken.ProductId, "Produs preluat", taken.Input.Quantity ?? 1, new DateOnly(2026, 10, 6)));
        using (var context = PickupContext(access, store, products, movements, suppliers, invoices))
        {
            var before = movements.Created.Count;
            var cut = await ToStepTwoAsync(context, invoice.Pdf, "factura-furnizor-din-nou.pdf");
            // The number misread by OCR ("I" for "1"): the invoice with the look-alike number is offered, and chosen by the user.
            cut.Find("#pickup-invoice-number").Input("FT I");
            cut.WaitForAssertion(() => { if (!cut.Markup.Contains("număr asemănător")) throw new Exception("pending"); }, TimeSpan.FromSeconds(10));
            check(!cut.Markup.Contains("a mai fost preluată") && cut.FindAll("button").Any(button => button.TextContent.Contains("Da, este factura FT 1")),
                "Pickup: a number misread by OCR is not taken for a new invoice silently: the invoice with the look-alike number is offered");
            cut.FindAll("button").First(button => button.TextContent.Contains("Da, este factura FT 1")).Click();
            cut.WaitForAssertion(() => { if (!cut.Markup.Contains("a mai fost preluată")) throw new Exception("pending"); }, TimeSpan.FromSeconds(10));
            check(cut.Find("#pickup-invoice-number").GetAttribute("value") == "FT 1", "Pickup: choosing the look-alike invoice puts its number in place and continues on it");
            check(cut.Markup.Contains("Produs preluat · ") && cut.FindAll(".pickup-invoice-head .anaf-source.ok").Count == 0, "Pickup: an invoice taken before is recognized in step 2 and what was taken is listed; the number put in place is to be confirmed again");
            SupplierTestServices.ConfirmHeader(cut);
            cut.FindAll("button").First(button => button.TextContent.Contains("Pasul următor")).Click();
            cut.WaitForAssertion(() => cut.Find(".pickup-entry"), TimeSpan.FromSeconds(10));
            check(cut.Markup.Contains("s-au preluat deja") && cut.FindAll("button").First(button => button.TextContent.Contains("Finalizează preluarea")).HasAttribute("disabled"),
                "Pickup: the products already taken from the invoice are marked and, all skipped, there is nothing to finalize");
            cut.Find(".invoice-warning input[role=switch]").Change(true);
            cut.FindAll("button").First(button => button.TextContent.Contains("Finalizează preluarea")).Click();
            cut.WaitForAssertion(() => { if (!cut.Markup.Contains("Preluarea a fost finalizată")) throw new Exception("pending"); }, TimeSpan.FromSeconds(20));
            check(invoices.Items.Count == 1 && movements.Created.Count == before + 1 && movements.Created[^1].Input.InvoiceId == invoices.Items[0].Id,
                "Pickup: a product taken again on purpose goes to the same invoice (no second invoice is recorded)");
        }

        // A supplier that is not in the register: the invoice's CUI is offered for adding (ANAF is asked at once) and the new supplier is chosen.
        var emptySuppliers = new MemorySupplierRepository();
        var emptyInvoices = new MemorySupplierInvoiceRepository(emptySuppliers);
        var anafAnswer = new ScriptedAnaf(cui => new(new AnafCompany(cui, "FURNIZOR TEST SRL", "Str. Test 1", "", "0721000111", "", ""), null, null, AnafLookupOutcome.Found));
        var realCui = ValidCui(2246088);
        var validInvoice = InvoiceFixtures.Make(new InvoiceSpec("ro-lines", 4, SupplierName: "Furnizor Test SRL", SupplierCui: "RO" + realCui, Number: "FV 9", Date: new DateOnly(2026, 10, 5)), InvoiceFixtures.MakeRows(4, 5));
        using (var context = PickupContext(access, store, products, new FakeInventoryPickupMovementRepository(new Dictionary<int, int>()), emptySuppliers, emptyInvoices))
        {
            context.Services.AddSingleton<IAnafService>(anafAnswer);   // the later registration wins
            var cut = await ToStepTwoAsync(context, validInvoice.Pdf, "factura-furnizor-nou.pdf");
            check(cut.Markup.Contains("nu este în registru") && cut.Find("#pickup-supplier-name").TextContent.Contains("Niciun furnizor"), "Pickup: a supplier that is not in the register is reported");
            cut.FindAll("button").First(button => button.TextContent.Contains("Adaugă furnizorul")).Click();
            cut.WaitForAssertion(() => { if (cut.FindAll("#supplier-cui").Count == 0 || anafAnswer.Calls.Count == 0) throw new Exception("pending"); }, TimeSpan.FromSeconds(10));
            check(cut.Find("#supplier-cui").GetAttribute("value") == realCui && anafAnswer.Calls.Single() == realCui && cut.Find(".invoice-modal .anaf-source").TextContent.Contains("Date preluate din ANAF"),
                "Pickup: the supplier form opens with the CUI of the invoice and asks ANAF at once");
            cut.WaitForAssertion(() => { if (cut.Find("#supplier-name").GetAttribute("value") != "FURNIZOR TEST SRL") throw new Exception("pending"); }, TimeSpan.FromSeconds(10));
            check(emptySuppliers.Items.Count == 0, "Pickup: nothing is added to the register until the supplier form is saved");
            cut.Find("form").Submit();
            cut.WaitForAssertion(() => { if (emptySuppliers.Items.Count != 1 || !cut.Find("#pickup-supplier-name").TextContent.Contains("Furnizor din registru")) throw new Exception("pending"); }, TimeSpan.FromSeconds(10));
            check(emptySuppliers.Items.Single().Source == SupplierSources.Anaf && cut.Find("#pickup-supplier-name").TextContent.Contains("FURNIZOR TEST SRL") && !cut.Markup.Contains("nu este în registru") &&
                  cut.FindAll(".pickup-invoice-head input[role=switch]")[1].GetAttribute("aria-checked") == "false",
                "Pickup: the supplier saved in the window is found by its CUI at once, the report that it was missing is gone, and the CUI is still to be confirmed");
        }

        // A CUI read with a wrong digit (a scan) is said at once in the supplier form and ANAF is not asked about it.
        var unusedAnaf = new ScriptedAnaf(FoundResult);
        using (var context = PickupContext(access, store, products, new FakeInventoryPickupMovementRepository(new Dictionary<int, int>()), new MemorySupplierRepository(), new MemorySupplierInvoiceRepository(new MemorySupplierRepository())))
        {
            context.Services.AddSingleton<IAnafService>(unusedAnaf);
            var cut = await ToStepTwoAsync(context, invoice.Pdf, "factura-cu-cui-gresit.pdf");
            cut.FindAll("button").First(button => button.TextContent.Contains("Adaugă furnizorul")).Click();
            cut.WaitForAssertion(() => { if (!cut.Markup.Contains("nu pare corect")) throw new Exception("pending"); }, TimeSpan.FromSeconds(10));
            check(unusedAnaf.Calls.Count == 0 && cut.Find("#supplier-cui").GetAttribute("value") == "12345678", "Pickup: a CUI with a wrong control digit read from the invoice is flagged in the supplier form and not sent to ANAF");
        }
    }
}

// ---- fakes ----

internal static class SupplierTestServices
{
    // Confirms, one by one, the number, the CUI and the date of the invoice in step 2 of the pickup.
    public static void ConfirmHeader(IRenderedComponent<InvoicePickup> cut)
    {
        for (var index = 0; index < 3; index++)
        {
            var toggle = cut.FindAll(".pickup-invoice-head input[role=switch]")[index];
            if (toggle.GetAttribute("aria-checked") != "true") toggle.Change(true);
        }
    }

    // By default the register holds the supplier of the generated test invoices (CUI 12345678).
    public static IServiceCollection AddSupplierFakes(this IServiceCollection services, MemorySupplierRepository? suppliers = null, MemorySupplierInvoiceRepository? invoices = null)
    {
        if (suppliers is null)
        {
            suppliers = new MemorySupplierRepository();
            suppliers.Items.Add(new Supplier(1, "Furnizor Test SRL", "12345678"));
        }
        services.AddSingleton<ISupplierRepository>(suppliers);
        services.AddSingleton<ISupplierInvoiceRepository>(invoices ?? new MemorySupplierInvoiceRepository(suppliers));
        services.AddSingleton<IAnafService>(new ScriptedAnaf(_ => new(null, "ANAF indisponibil (test)", null, AnafLookupOutcome.Unavailable)));
        return services;
    }
}

internal sealed class MemorySupplierRepository : ISupplierRepository
{
    public List<Supplier> Items { get; } = [];
    private int nextId = 100;

    public Task<IReadOnlyList<Supplier>> GetSuppliersAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Supplier>>([.. Items.OrderBy(item => item.Name)]);
    public Task<Supplier?> GetAsync(int id, CancellationToken cancellationToken = default) => Task.FromResult(Items.FirstOrDefault(item => item.Id == id));

    public Task<Supplier> CreateAsync(SupplierInput input, CancellationToken cancellationToken = default)
    {
        var value = input.Validated();
        var key = SupplierRules.IdentityKey(value.Country, value.Cui);
        if (Items.FirstOrDefault(item => SupplierRules.IdentityKey(item.Country, item.Cui) == key) is { } existing)
            throw new SupplierOperationException(SupplierRules.DuplicateMessage(existing.Name));
        var created = new Supplier(nextId++, value.Name, value.Cui, value.Country, 0, value.Address, value.Phone, value.RegistryNumber, value.PostalCode, value.CaenCode, value.Source,
            value.Source is SupplierSources.Anaf or SupplierSources.AnafEdited ? DateTime.UtcNow : null);
        Items.Add(created);
        return Task.FromResult(created);
    }

    public Task<Supplier> UpdateAsync(Supplier original, SupplierInput input, CancellationToken cancellationToken = default)
    {
        var value = input.Validated(true);
        var current = Items.FirstOrDefault(item => item.Id == original.Id);
        SupplierRules.CheckCurrent(current, original);
        var updated = current! with { Name = value.Name, Cui = value.Cui, Country = value.Country, Address = value.Address, Phone = value.Phone, RegistryNumber = value.RegistryNumber,
            PostalCode = value.PostalCode, CaenCode = value.CaenCode, Source = value.Source, Version = current.Version + 1, VerifiedUtc = value.AnafRecheck ? DateTime.UtcNow : current.VerifiedUtc };
        Items[Items.IndexOf(current)] = updated;
        return Task.FromResult(updated);
    }

    public Task DeleteAsync(Supplier original, string reason, CancellationToken cancellationToken = default)
    {
        var current = Items.FirstOrDefault(item => item.Id == original.Id);
        SupplierRules.CheckCurrent(current, original);
        if (current!.InUse) throw new SupplierOperationException(SupplierRules.DeleteBlockedMessage(current.InvoiceCount, current.MovementCount, current.TemplateCount));
        Items.Remove(current);
        return Task.CompletedTask;
    }
}

internal sealed class MemorySupplierInvoiceRepository(MemorySupplierRepository suppliers) : ISupplierInvoiceRepository
{
    public List<SupplierInvoice> Items { get; } = [];
    public List<InvoiceEntry> Entries { get; } = [];

    public Task<SupplierInvoice?> FindAsync(int supplierId, string number, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.FirstOrDefault(item => item.SupplierId == supplierId && SupplierInvoiceRules.NumberKey(item.Number) == SupplierInvoiceRules.NumberKey(number)));

    public Task<IReadOnlyList<InvoiceEntry>> GetEntriesAsync(int invoiceId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<InvoiceEntry>>([.. Entries]);

    public Task<IReadOnlyList<SupplierInvoice>> GetForSupplierAsync(int supplierId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SupplierInvoice>>([.. Items.Where(item => item.SupplierId == supplierId)]);

    public Task<SupplierInvoice> CreateAsync(SupplierInvoiceInput input, CancellationToken cancellationToken = default)
    {
        var value = SupplierInvoiceRules.Validated(input);
        var supplier = suppliers.Items.FirstOrDefault(item => item.Id == value.SupplierId) ?? throw new SupplierInvoiceOperationException(SupplierInvoiceRules.SupplierMissingMessage);
        if (Items.FirstOrDefault(item => item.SupplierId == supplier.Id && SupplierInvoiceRules.NumberKey(item.Number) == SupplierInvoiceRules.NumberKey(value.Number)) is { } existing)
            throw new SupplierInvoiceOperationException(SupplierInvoiceRules.DuplicateMessage(supplier.Name, value.Number, existing.Date));
        var created = new SupplierInvoice(Items.Count + 1, supplier.Id, supplier.Name, value.Number, value.Date!.Value, "test", DateTime.UtcNow);
        Items.Add(created);
        return Task.FromResult(created);
    }
}

// ANAF answering as the script says; every CUI it was asked about is recorded.
internal sealed class ScriptedAnaf(Func<string, AnafCompanyResult> script) : IAnafService
{
    public List<string> Calls { get; } = [];
    public Task<AnafCompanyResult> LookupCompanyAsync(string cui, CancellationToken cancellationToken = default) { Calls.Add(cui); return Task.FromResult(script(cui)); }
    public Task<AnafState> GetStateAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<AnafRequest> PreviewAsync(AnafConfig config, string cui, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<AnafTestResult> TestAsync(AnafConfig config, string cui, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task SaveDraftAsync(AnafConfig config, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<AnafState> ActivateAsync(AnafConfig config, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<AnafState> RollbackAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public bool WasTestedSuccessfully(AnafConfig config) => false;
}
