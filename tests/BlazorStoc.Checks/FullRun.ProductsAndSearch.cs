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
    // Lines 303-542 of the former Program.cs.
    internal static async Task ProductsAndSearchAsync(string[] args)
    {

        // Maintenance map: the separate window and the refresh compare the rows by value.
        {
            var mapDay = new DateOnly(2026, 9, 30);
            ServiceDueRow MapRow(int id, DateOnly due) => new(5, "Ben", new ServiceContract(1, 5, "C1", mapDay, 12, null, true, "", 0),
                new ServiceContractPointView(new ServiceContractPoint(id, 1, 7, null, due, 0), "Punct", "Adresa", true), 45.1m, 25.6m, null);
            var shownRows = new[] { MapRow(1, mapDay), MapRow(2, mapDay) };
            Check(MaintenanceMapRules.ChangedRows(shownRows, [MapRow(1, mapDay), MapRow(2, mapDay)]) == 0 &&
                  MaintenanceMapRules.ChangedRows(shownRows, [MapRow(1, mapDay), MapRow(2, mapDay.AddDays(1))]) == 1 &&
                  MaintenanceMapRules.ChangedRows(shownRows, [MapRow(1, mapDay), MapRow(2, mapDay), MapRow(3, mapDay)]) == 1 &&
                  MaintenanceMapRules.ChangedRows(shownRows, [MapRow(1, mapDay)]) == 0,
                "The automatic refresh counts the rows that are new or changed, and none when nothing differs");
            Check(MaintenanceMapRules.WindowUrl == "/mentenanta/harta/fereastra" && MaintenanceMapRules.WindowName == "blazorstoc-harta" && MaintenanceMapRules.AutoRefreshSeconds is >= 15 and <= 120,
                "The map window has a fixed address and name (one reused window) and refreshes at a moderate pace");
        }

        // Settings → Hartă: the tile provider and the pin types of the overlay.
        {
            var engine = new MapEngineSettings();
            Check(MapEngineRules.Validate(engine) is null && MapEngineRules.Validate(new MapEngineSettings { TileUrl = "http://t.example/{z}/{x}/{y}.png" }) is not null &&
                  MapEngineRules.Validate(new MapEngineSettings { TileUrl = "https://t.example/{z}/{x}.png" }) is not null && MapEngineRules.Validate(new MapEngineSettings { TileUrl = "https://u:p@t.example/{z}/{x}/{y}.png" }) is not null &&
                  MapEngineRules.Validate(new MapEngineSettings { TileUrl = "https://t.example/{z}/{x}/{y}.png?k={key}" }) is { } missingKey && missingKey.Contains("Cheie furnizor") &&
                  MapEngineRules.Validate(new MapEngineSettings { TileUrl = "https://t.example/{z}/{x}/{y}.png?k={key}", ApiKey = "abc" }) is null,
                "The tile address must be https with {z}/{x}/{y}, no credentials, and {key} needs a key");
            Check(MapEngineRules.Validate(new MapEngineSettings { MinZoom = 10, MaxZoom = 5 }) is not null && MapEngineRules.Validate(new MapEngineSettings { StartZoom = 1, MinZoom = 3 }) is not null &&
                  MapEngineRules.Validate(new MapEngineSettings { CenterLatitude = 100 }) is not null && MapEngineRules.Validate(new MapEngineSettings { AttributionText = " " }) is not null &&
                  MapEngineRules.Validate(new MapEngineSettings { AttributionUrl = "http://x.example" }) is not null, "The zoom range, the centre and the attribution are validated");
            var keyed = new MapEngineSettings { TileUrl = "https://t.example/{z}/{x}/{y}.png?k={key}", ApiKey = "a b&c" }.ToOptions();
            Check(keyed.TileUrl == "https://t.example/{z}/{x}/{y}.png?k=a%20b%26c", "The key is put into the address (escaped) for the map page only");
            var attribution = MapEngineRules.AttributionHtml("<b>x</b> & y", "https://example.org/a?b=1&c=2");
            Check(!attribution.Contains("<b>") && attribution.Contains("&lt;b&gt;x&lt;/b&gt; &amp; y") && attribution.StartsWith("<a href=\"https://example.org/a?b=1&amp;c=2\"") && MapEngineRules.AttributionHtml("x", "javascript:alert(1)") == "x",
                "The attribution is text that becomes safe markup (an https link, everything else encoded)");
            var fromDefaults = MapEngineSettings.FromOptions(new MapOptions());
            Check(fromDefaults.AttributionText == "© OpenStreetMap contributors" && fromDefaults.AttributionUrl == "https://www.openstreetmap.org/copyright" && fromDefaults.ToOptions().TileUrl == MapOptions.DefaultTileUrl,
                "The starting values are those of appsettings.json");
            var keyChanges = MapEngineRules.Changes(new MapEngineSettings(), new MapEngineSettings { ApiKey = "secret-key-1" }).Where(change => change.Before != change.After).ToList();
            Check(keyChanges.Count == 1 && keyChanges[0].Field == "Cheie furnizor" && !keyChanges[0].After.Contains("secret") && keyChanges[0].After == "schimbată",
                "The journal never holds the key itself");

            var defaults = MapPinRules.Defaults();
            Check(defaults.Count == 6 && defaults.Count(type => type.IsFill) == 4 && defaults.All(type => type.BuiltIn) && defaults.Select(type => type.Id).Distinct().Count() == 6 && defaults.All(type => type.Id < MapPinRules.FirstCustomId),
                "Six built-in types: four fills and two badges, with ids below the ones of custom types");
            Check(MapPinRules.Foreground("#ffffff") == "#1d2b2e" && MapPinRules.Foreground("#000000") == "#ffffff" && MapPinRules.Foreground("#d99a1f") == "#1d2b2e" && MapPinRules.Foreground("#c0392b") == "#ffffff" && MapPinRules.Foreground("nope") == "#ffffff",
                "The glyph colour is the one that reads better on the pin colour");
            var edited = defaults.Select(type => type.Clone()).ToList();
            edited[0].Name = "Depășit"; edited[0].Color = "#FF0000"; edited[4].Active = false; edited[3].Active = false;
            var merged = MapPinRules.Effective(edited);
            Check(merged.Count == 6 && merged.Single(type => type.Key == "overdue").Name == "Depășit" && merged.Single(type => type.Key == "overdue").Color == "#ff0000" && !merged.Single(type => type.Kind == "badge" && type.Key == "expired").Active &&
                  merged.Single(type => type.Key == "off").Active && MapPinRules.Effective(null).Count == 6, "Stored edits are merged over the built-in types; a fill type can never be switched off");
            var custom = new MapPinType { Id = 1000, Key = "c1000", Kind = "badge", Rule = MapPinType.RuleNoIntervention, Months = 6, Name = "Fără vizită", Color = "#7a4fb5", Glyph = "V", Active = true };
            Check(MapPinRules.Validate(custom, defaults) is null && MapPinRules.Validate(custom.With(type => type.Name = "La zi"), defaults) is { } dup && dup.Contains("Există deja") &&
                  MapPinRules.Validate(defaults[0].With(type => type.Color = defaults[2].Color), defaults.Skip(1)) is { } sameColor && sameColor.Contains("folosită deja") &&
                  MapPinRules.Validate(custom.With(type => type.Glyph = "<b"), defaults) is not null && MapPinRules.Validate(custom.With(type => type.Color = "red"), defaults) is not null &&
                  MapPinRules.Validate(custom.With(type => type.Months = 0), defaults) is not null && MapPinRules.Validate(custom.With(type => { type.Id = 0; type.Name = "D"; }), defaults.Concat([custom.With(t => t.Id = 1001), custom.With(t => { t.Id = 1002; t.Name = "B"; }), custom.With(t => { t.Id = 1003; t.Name = "C"; })])) is { } tooMany && tooMany.Contains("cel mult"),
                "Pin types are validated: name, colour, glyph, months, unique names and fill colours, at most three custom badges");

            var day = new DateOnly(2026, 9, 30);
            ServiceDueRow PinRow(DateOnly? last, bool active = true) => new(5, "Ben", new ServiceContract(1, 5, "C1", day, 12, null, active, "", 0),
                new ServiceContractPointView(new ServiceContractPoint(1, 1, 7, null, day.AddDays(40), 0), "Punct", "Adresa", true), 45.1m, 25.6m, last);
            var customTypes = merged.Concat([custom]).ToList();
            Check(MapPinRules.ExtraBadges(PinRow(null), day, customTypes).SequenceEqual(["c1000"]) && MapPinRules.ExtraBadges(PinRow(day.AddMonths(-6)), day, customTypes).Count == 1 &&
                  MapPinRules.ExtraBadges(PinRow(day.AddMonths(-5)), day, customTypes).Count == 0 && MapPinRules.ExtraBadges(PinRow(null, false), day, customTypes).Count == 0 &&
                  MapPinRules.ExtraBadges(PinRow(null), day, customTypes.Select(type => type.Id == 1000 ? type.With(t => t.Active = false) : type).ToList()).Count == 0 && MapPinRules.ExtraBadges(PinRow(null), day, null).Count == 0,
                "A custom badge follows its rule (N months or never), only for contracts that are On and only while active");
            Check(MaintenanceMapRules.Marker(PinRow(null), day, 30, 30, customTypes)?.Extra?.Single() == "c1000" && MaintenanceMapRules.Marker(PinRow(null), day, 30, 30)?.Extra is { Count: 0 },
                "The marker carries the keys of its custom badges");
            Check(SettingsNavigation.MapEngineUrl == "/setari?tab=harta&subtab=motor" && SettingsNavigation.MapPinTypeUrl(1000) == "/setari?tab=harta&subtab=overlay&pin=1000" &&
                  new[] { AuditActions.EditMapEngine, AuditActions.ResetMapEngine, AuditActions.CreateMapPinType, AuditActions.EditMapPinType }.All(AuditActions.IsCreateOrEdit) && !AuditActions.IsCreateOrEdit(AuditActions.DeleteMapPinType) &&
                  new[] { AuditActions.EditMapEngine, AuditActions.ResetMapEngine, AuditActions.CreateMapPinType, AuditActions.EditMapPinType, AuditActions.DeleteMapPinType }.Distinct().Count() == 5 &&
                  AuditNavigation.TargetUrl(new AuditEvent(Guid.NewGuid(), DateTime.UtcNow, "ana", AccessRoles.Administrator, AuditEntities.MapPinType, AuditActions.EditMapPinType, "Tip pin: X", "d", "", "1000")) == "/setari?tab=harta&subtab=overlay&pin=1000" &&
                  AuditNavigation.TargetUrl(new AuditEvent(Guid.NewGuid(), DateTime.UtcNow, "ana", AccessRoles.Administrator, AuditEntities.MapEngine, AuditActions.EditMapEngine, "Furnizor hartă", "d", "", "1")) == "/setari?tab=harta&subtab=motor" &&
                  AuditFilterOptions.Actions.Any(option => option.Value == AuditActions.CreateMapPinType) && AuditFilterOptions.Entities.Any(option => option.Value == AuditEntities.MapEngine),
                "Each map settings operation has its own journal action, linked to its sub-tab (the pin type highlighted)");

            var mapDirectory = Path.Combine(Path.GetTempPath(), "blazorstoc-map-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(mapDirectory);
            try
            {
                var mapConfig = new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Map:ConfigurationPath"] = Path.Combine(mapDirectory, "map.json") }).Build();
                var mapTrail = new TestAuditTrail();
                MapConfigurationService MapService(bool admin = true) => new(new MapConfigStore(mapConfig), Microsoft.Extensions.Options.Options.Create(new MapOptions()), new TestAccessControl(admin, "ana"), mapTrail);
                var mapService = MapService();
                var viewBefore = await mapService.GetViewAsync();
                Check(viewBefore.Version == 0 && viewBefore.Engine.TileUrl == MapOptions.DefaultTileUrl && viewBefore.PinTypes.Count == 6, "Without saved settings the map uses appsettings.json and the built-in pin types");
                var stateBefore = await mapService.GetStateAsync();
                Check(!stateBefore.EngineSaved && stateBefore.Version == 0, "The settings page starts from the defaults");
                var newEngine = stateBefore.Engine.Clone(); newEngine.TileUrl = "https://tiles.example/{z}/{x}/{y}.png"; newEngine.MaxZoom = 17;
                await mapService.SaveEngineAsync(newEngine, 0);
                var afterEngine = await mapService.GetViewAsync();
                Check(afterEngine.Version == 1 && afterEngine.Engine.TileUrl == "https://tiles.example/{z}/{x}/{y}.png" && afterEngine.Engine.MaxZoom == 17 &&
                      mapTrail.Entries.Last() is { Action: var engineAction, EntityType: var engineEntity, Details: var engineDetails } && engineAction == AuditActions.EditMapEngine && engineEntity == AuditEntities.MapEngine &&
                      engineDetails.Contains("Adresa dalelor: https://tile.openstreetmap.org/{z}/{x}/{y}.png → https://tiles.example/{z}/{x}/{y}.png") && engineDetails.Contains("Zoom maxim: 19 → 17"),
                    "Saving the engine is journaled with the exact operation and the old and the new values");
                await RejectedMap(() => mapService.SaveEngineAsync(newEngine, 0), "A save on an outdated version is refused");
                await RejectedMap(() => mapService.SaveEngineAsync(newEngine, 1), "A save without changes is refused");
                await RejectedMap(() => mapService.SaveEngineAsync(new MapEngineSettings { TileUrl = "http://x/{z}/{x}/{y}" }, 1), "An invalid address is refused");
                await RejectedMap(() => MapService(false).SaveEngineAsync(newEngine, 1), "Only an administrator changes the engine");
                var createdPin = await mapService.SavePinTypeAsync(new MapPinType { Name = "Fără vizită", Color = "#7a4fb5", Glyph = "V", Months = 6, Active = true, Order = 5 }, 1);
                Check(createdPin.Id == MapPinRules.FirstCustomId && createdPin.Key == "c1000" && createdPin.Kind == "badge" && createdPin.Rule == "no-intervention" && !createdPin.BuiltIn &&
                      mapTrail.Entries.Last().Action == AuditActions.CreateMapPinType && mapTrail.Entries.Last().EntityId == "1000", "A custom badge is added with its own id, key and journal action");
                var builtIn = (await mapService.GetStateAsync()).PinTypes.Single(type => type.Key == "overdue").Clone();
                builtIn.Name = "Depășită"; builtIn.Color = "#AA0000"; builtIn.Active = false; builtIn.Kind = "badge"; builtIn.Key = "hacked";
                await mapService.SavePinTypeAsync(builtIn, 2);
                var afterBuiltIn = (await mapService.GetViewAsync()).PinTypes;
                Check(afterBuiltIn.Single(type => type.Key == "overdue") is { Name: "Depășită", Color: "#aa0000", Active: true, Kind: "fill" } && afterBuiltIn.All(type => type.Key != "hacked") &&
                      mapTrail.Entries.Last().Action == AuditActions.EditMapPinType && mapTrail.Entries.Last().Details.Contains("Culoare: #c0392b → #aa0000"),
                    "A built-in type keeps its kind, key and rule (and stays on when it is a fill); the edit is journaled with old and new values");
                await RejectedMap(() => mapService.SavePinTypeAsync(new MapPinType { Name = "fără vizită", Color = "#123456", Months = 3, Active = true }, 3), "Pin type names are unique without regard to letter case");
                await RejectedMap(() => mapService.DeletePinTypeAsync(1, "motiv", 3), "A built-in type cannot be deleted");
                await RejectedMap(() => mapService.DeletePinTypeAsync(MapPinRules.FirstCustomId, "", 3), "A deletion needs a reason");
                await mapService.DeletePinTypeAsync(MapPinRules.FirstCustomId, "nu mai e nevoie", 3);
                Check((await mapService.GetViewAsync()).PinTypes.All(type => type.BuiltIn) && mapTrail.Entries.Last().Action == AuditActions.DeleteMapPinType && mapTrail.Entries.Last().Motif == "nu mai e nevoie", "A custom badge can be deleted with a reason, and the deletion is journaled");
                await mapService.ResetEngineAsync(4);
                var afterReset = await mapService.GetViewAsync();
                Check(afterReset.Engine.TileUrl == MapOptions.DefaultTileUrl && !(await mapService.GetStateAsync()).EngineSaved && mapTrail.Entries.Last().Action == AuditActions.ResetMapEngine && afterReset.PinTypes.Single(type => type.Key == "overdue").Name == "Depășită",
                    "Going back to appsettings.json resets the engine only (journaled); the pin types stay");
                File.WriteAllText(Path.Combine(mapDirectory, "map.json"), "{ not json");
                Check((await mapService.GetViewAsync()).PinTypes.Count == 6, "A damaged file falls back to the defaults instead of breaking the map");
            }
            finally { Directory.Delete(mapDirectory, true); }
            async Task RejectedMap(Func<Task> operation, string message)
            {
                try { await operation(); Check(false, message); }
                catch (MapOperationException) { Check(true, message); }
                catch (AccessDeniedException) { Check(true, message); }
            }
        }

        var anafConfig = new AnafConfig();
        Check(AnafRules.Validate(anafConfig) is null, "ANAF default configuration is valid");
        Check(AnafRules.NormalizeCui(anafConfig, " ro9178894 ") == "9178894" && AnafRules.NormalizeCui(anafConfig, "28996610") == "28996610" &&
              AnafRules.NormalizeCui(anafConfig, "0123") is null && AnafRules.NormalizeCui(anafConfig, "RO") is null && AnafRules.NormalizeCui(anafConfig, "12345678901") is null,
            "ANAF CUI normalization strips RO and spaces and rejects invalid values");
        var anafRequest = AnafRules.Prepare(anafConfig, "RO9178894", new DateOnly(2026, 9, 29));
        Check(anafRequest.Cui == "9178894" && anafRequest.Body.Replace(" ", "") == "[{\"cui\":9178894,\"data\":\"2026-09-29\"}]",
            "ANAF request template substitutes CUI and ISO date");
        var badTemplate = anafConfig.Clone(); badTemplate.Template = "[{\"cui\": {{cui}}, \"x\": {{altceva}}}]";
        var brokenJson = anafConfig.Clone(); brokenJson.Template = "[{\"cui\": {{cui}}";
        var otherHost = anafConfig.Clone(); otherHost.Url = "https://example.com/api";
        var httpUrl = anafConfig.Clone(); httpUrl.Url = "http://webservicesp.anaf.ro/api/PlatitorTvaRest/v9/tva";
        var fastInterval = anafConfig.Clone(); fastInterval.Interval = 200;
        var badPath = anafConfig.Clone(); badPath.Mappings[1].Path = "$.a[*].b";
        var reservedHeader = anafConfig.Clone(); reservedHeader.Headers.Add(new AnafHeader { Name = "Authorization", Value = "x" });
        Check(AnafRules.Validate(otherHost) is not null && AnafRules.Validate(httpUrl) is not null && AnafRules.Validate(fastInterval) is not null &&
              AnafRules.Validate(badPath) is not null && AnafRules.Validate(reservedHeader) is not null,
            "ANAF validation rejects foreign hosts, plain HTTP, too-fast intervals, unsupported paths and reserved headers");
        foreach (var broken in new[] { badTemplate, brokenJson })
        {
            try { AnafRules.Prepare(broken, "9178894", new DateOnly(2026, 9, 29)); Check(false, "ANAF invalid template must be rejected"); }
            catch (AnafException) { Check(true, "ANAF unknown variable and invalid JSON templates are rejected"); }
        }
        var anafSample = AnafRules.Interpret(anafConfig, anafRequest, 200, 10,
            "{\"found\":[{\"date_generale\":{\"cui\":9178894,\"denumire\":\"FIRMA SRL\",\"statusRO_e_Factura\":true},\"inregistrare_scop_Tva\":{\"scpTVA\":false}}],\"notFound\":[]}");
        string? AnafValue(string label) => anafSample.Mapped.Single(m => m.Label == label).Value;
        Check(anafSample.Ok && AnafValue("Denumire") == "FIRMA SRL" && AnafValue("Plătitor TVA") == "Nu" && AnafValue("RO e-Factura") == "Da" &&
              AnafValue("Telefon") is null && AnafValue("Inactiv fiscal") is null,
            "ANAF response mapping keeps false distinct from absent fields");
        Check(!AnafRules.Interpret(anafConfig, anafRequest, 200, 1, "{\"found\":[],\"notFound\":[9178894]}").Ok &&
              !AnafRules.Interpret(anafConfig, anafRequest, 200, 1, "not json").Ok,
            "ANAF unknown CUI and invalid JSON are reported as failures");
        var requiredMissing = anafConfig.Clone(); requiredMissing.Mappings[2].Missing = "error";
        Check(!AnafRules.Interpret(requiredMissing, anafRequest, 200, 1,
            "{\"found\":[{\"date_generale\":{\"cui\":9178894,\"denumire\":\"X\"}}],\"notFound\":[]}").Ok,
            "ANAF missing-field policy 'error' blocks a successful test");
        Check(AnafRules.Fingerprint(anafConfig) == AnafRules.Fingerprint(anafConfig.Clone()) && AnafRules.Fingerprint(anafConfig) != AnafRules.Fingerprint(fastInterval),
            "ANAF configuration fingerprint follows content");

        Check(data.Count == 12, "Demonstration catalogue has 12 fictional products");
        Check(ProductSearch.Filter(data, "  POLIZOR  ", "name", "", "").Single().Id == 2, "Search is case-insensitive and trims spaces");
        Check(!ProductSearch.Filter(data, "mandrina", "name", "", "").Any(), "Name-only search excludes description matches");
        Check(ProductSearch.Filter(data, "mandrina", "description", "", "").Single().Id == 1, "Description-only search works with normalized (diacritic-free) text");
        Check(ProductSearch.Filter(data, "", "all", "Masurare", "zero").Single().Id == 5, "Category and stock filters combine");
        Check(ProductSearch.Filter(data, "", "all", "", "negative").Single().Id == 8, "Negative stock is distinct from zero stock");
        Check(ProductSearch.Filter(data, "", "all", "", "zero").Count() == 2, "Zero stock classification");
        Check(!ProductSearch.Filter(data, "' OR 1=1 --", "all", "", "").Any(), "Search text is handled as literal text");
        Check(!ProductSearch.Filter(Array.Empty<Product>(), "", "all", "", "").Any(), "Empty database produces an empty list");
cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try { await new DemoProductRepository().GetProductsAsync(cancelled.Token); throw new Exception("Cancellation ignored"); }
        catch (OperationCanceledException) { Console.WriteLine("PASS: Cancellation is respected"); }

repository = new DemoProductRepository();
created = await CreateProductAsync(repository, new ProductInput { Name = "  Șurub   nou  ", Category = "Categorie nouă", Subcategory = "Subcategorie nouă", Description = "Țeavă și șaibă" });
        Check(created.Id == 13 && created.Name == "Surub nou" && created.Description == "Teava si saiba" && (await repository.GetProductsAsync()).Contains(created), "Create removes diacritics before persistence");
        Check((await repository.GetGroupsAsync()).Contains(new ProductGroup("Categorie noua", "Subcategorie noua")), "New category and subcategory are available without diacritics");
        var selectionRulesRepository = new DemoProductRepository();
        await Rejected(() => selectionRulesRepository.CreateAsync(new ProductInput
            { Name = "Produs fara grup", Category = "Categorie inexistenta", Subcategory = "Subcategorie inexistenta" }),
            "Product creation rejects a category that was not created in administration");
        await selectionRulesRepository.CreateCategoryAsync("Categorie fara subcategorie");
        await Rejected(() => selectionRulesRepository.CreateAsync(new ProductInput
            { Name = "Produs fara subcategorie", Category = "Categorie fara subcategorie", Subcategory = "Subcategorie inexistenta" }),
            "Product creation rejects a subcategory that was not created in administration");
        Check(TextNormalization.SameUniqueValue("Categorie nouă", "cATEGORIE NOUA"), "Uniqueness validation ignores case and diacritics");
        Check(TextNormalization.SameUniqueValue("Cod   produs", " Cod produs "), "Uniqueness validation ignores repeated and exterior spaces");
        try
        {
            await repository.CreateAsync(new ProductInput { Name = "șURUB NOU", Category = "Altă categorie", Subcategory = "Altă subcategorie" });
            throw new Exception("Duplicate product accepted");
        }
        catch (ProductOperationException exception)
        {
            Check(exception.Message.Contains("Categorie noua", StringComparison.Ordinal) && exception.Message.Contains("Subcategorie noua", StringComparison.Ordinal),
                "Duplicate product is rejected globally and reports its category and subcategory");
            Check(exception.Message.Contains("Codul produsului «Surub nou» există deja", StringComparison.Ordinal),
                "Duplicate product code message names the existing code");
        }
        foreach (var variant in new[] { "  surub   NOU ", "ȘURUB NOU", "Șurub nou" })
            await Rejected(() => repository.CreateAsync(new ProductInput
                { Name = variant, Category = "Categorie nouă", Subcategory = "Subcategorie nouă" }),
                $"Product code «{variant}» is a duplicate despite spacing, case or diacritics");
        try
        {
            new ProductInput { Name = "   ", Category = "Test", Subcategory = "Test" }.Validated();
            throw new Exception("Empty product code accepted");
        }
        catch (ProductOperationException exception)
        {
            Check(exception.Message.Contains("Completează codul produsului.", StringComparison.Ordinal),
                "A product cannot be saved without a product code");
        }
    }
}
