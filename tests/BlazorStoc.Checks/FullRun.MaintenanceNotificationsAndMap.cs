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
    // Lines 2315-2431 of the former Program.cs.
    internal static async Task MaintenanceNotificationsAndMapAsync(string[] args)
    {

        // Maintenance notification sources (pure): keys, category and event, placeholders, the texts rendered for an instance, the reasons
        // written when a date changes, and the proposed default texts.
        {
            var reader = new FakeMaintenanceReader(
                [new MaintenanceDueItem(7, 3, "Demo Puncte SRL", "Sediu central", "Strada Demo 10, Timișoara", "26/23.09.2025", new DateOnly(2026, 11, 5), new DateOnly(2026, 8, 1)),
                 new MaintenanceDueItem(8, 3, "Demo Puncte SRL", "Depozit", "Str. Depozitului 5", "26/23.09.2025", new DateOnly(2026, 10, 20), null)],
                [new ContractExpiryItem(11, 3, "Demo Puncte SRL", "26/23.09.2025", new DateOnly(2025, 9, 23), new DateOnly(2027, 9, 22))]);
            var dueSource = new MaintenanceDueSource(reader);
            var expirySource = new ContractExpirySource(reader);
            Check(dueSource.Key == "mentenanta.scadenta" && expirySource.Key == "contract.expirare" && ExpirySourceKeys.MaintenanceDue == dueSource.Key && ExpirySourceKeys.ContractExpiry == expirySource.Key &&
                  dueSource.Category == "Mentenanță" && expirySource.Category == "Mentenanță" && dueSource.EventName != expirySource.EventName,
                "The two maintenance sources have their stable keys and share the category \"Mentenanță\"");
            var dueInstances = await dueSource.GetInstancesAsync();
            Check(dueInstances.Count == 2 && dueInstances[0].ObjectId == 7 && dueInstances[0].Expiry == new DateOnly(2026, 11, 5) && dueInstances[0].Label == "Demo Puncte SRL · Sediu central" &&
                  dueInstances[0].Url == "/beneficiari/3" && dueInstances[0].Values["data ultima interventie"] == "01.08.2026" && dueInstances[1].Values["data ultima interventie"] == MaintenanceDueSource.NoIntervention,
                "A due instance is the covered point, dated at its next due date, with the link of the beneficiary and the last maintenance intervention (or none)");
            var today = new DateOnly(2026, 10, 30);
            var subject = ExpiryTemplateRules.Render(dueSource.DefaultSubject, dueSource.EventName, dueInstances[0], today);
            var body = ExpiryTemplateRules.Render(dueSource.DefaultBody, dueSource.EventName, dueInstances[0], today);
            Check(subject == "Scadență mentenanță – Demo Puncte SRL, Sediu central" && body.Contains("Sediu central (Strada Demo 10, Timișoara)") && body.Contains("contract 26/23.09.2025") && body.Contains("05.11.2026") &&
                  body.Contains("zile rămase: 6") && body.Contains("Ultima intervenție de mentenanță: 01.08.2026") && !body.Contains('<'),
                "The proposed text of the due source renders every placeholder");
            var expiryInstances = await expirySource.GetInstancesAsync();
            Check(expiryInstances.Count == 1 && expiryInstances[0].ObjectId == 11 && expiryInstances[0].Expiry == new DateOnly(2027, 9, 22) && expiryInstances[0].Label == "Demo Puncte SRL · Contract 26/23.09.2025" &&
                  ExpiryTemplateRules.Render(expirySource.DefaultBody, expirySource.EventName, expiryInstances[0], new DateOnly(2027, 9, 12)) ==
                      "Contractul de mentenanță 26/23.09.2025 al beneficiarului Demo Puncte SRL expiră la data de 22.09.2027 (zile rămase: 10; zile de depășire: 0).",
                "An expiry instance is the contract dated at its expiry date; the proposed text names the contract, the beneficiary and the date");
            Check(ExpiryTemplateRules.UnknownPlaceholders("<beneficiar> <punct de lucru> <adresa punct de lucru> <numar contract> <data ultima interventie> <data expirare> <zile ramase> <zile depasire>", dueSource).Count == 0 &&
                  ExpiryTemplateRules.UnknownPlaceholders("<beneficiar> <numar contract> <data contract> <data expirare>", expirySource).Count == 0 &&
                  ExpiryTemplateRules.UnknownPlaceholders("<punct de lucru>", expirySource).Count == 1 && ExpiryTemplateRules.UnknownPlaceholders("<numar autovehicul>", dueSource).Count == 1,
                "Each source accepts its own placeholders and refuses the ones of other sources");
            Check(dueSource.DateChangedReason(new DateOnly(2026, 1, 18), new DateOnly(2026, 5, 5), dueInstances[0]) == "Scadența intervenției de mentenanță s-a modificat de la 18.01.2026 la 05.05.2026 (ultima intervenție de mentenanță: 01.08.2026)." &&
                  dueSource.DateChangedReason(new DateOnly(2026, 1, 18), new DateOnly(2026, 5, 5), dueInstances[1]) == "Scadența intervenției de mentenanță s-a modificat de la 18.01.2026 la 05.05.2026." &&
                  expirySource.DateChangedReason(new DateOnly(2027, 9, 22), new DateOnly(2028, 9, 22), expiryInstances[0]) == "Data expirării contractului s-a modificat de la 22.09.2027 la 22.09.2028." &&
                  ((IExpirySource)new TestExpirySource("t", [])).DateChangedReason(new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1), dueInstances[0]) == "Data expirării Eveniment test s-a modificat de la 01.01.2026 la 01.02.2026." &&
                  dueSource.RemovedReason.Contains("contractul a fost dezactivat") && expirySource.RemovedReason.Contains("dezactivat"),
                "The reasons written when a date changes or the object leaves are specific to each source (the other sources keep the generic text)");
            Check(((IExpirySource)new TestExpirySource("t", [])).DefaultSubject == ExpiryTemplateRules.DefaultSubject && dueSource.DefaultSubject != ExpiryTemplateRules.DefaultSubject,
                "A source without its own proposed text keeps the vehicle one");
        }

        // Maintenance map (pure): the state of the contract term, the fill (due state) and the badge (contract state) that never share a colour,
        // the markers and the rows without coordinates, the provider configuration and its fallbacks.
        {
            var today = new DateOnly(2026, 9, 30);
            ServiceContract Contract(DateOnly? validUntil, bool active = true) => new(1, 1, "26", new DateOnly(2025, 9, 23), 3, validUntil, active, "", 0);
            Check(ServiceDueRules.ExpiryState(Contract(null), today) == ServiceExpiryState.NoTerm && ServiceDueRules.ExpiryState(Contract(today.AddDays(-1)), today) == ServiceExpiryState.Expired &&
                  ServiceDueRules.ExpiryState(Contract(today), today) == ServiceExpiryState.ExpiresSoon && ServiceDueRules.ExpiryState(Contract(today.AddDays(30)), today) == ServiceExpiryState.ExpiresSoon &&
                  ServiceDueRules.ExpiryState(Contract(today.AddDays(31)), today) == ServiceExpiryState.Valid && ServiceDueRules.ExpiryState(Contract(today.AddDays(31)), today, 45) == ServiceExpiryState.ExpiresSoon,
                "The contract term is expired before today, expiring within the threshold (30 days by default), valid after it, or without a term");
            Check(ServiceDueRules.ExpiryText(Contract(null), today) == "Fără termen" && ServiceDueRules.ExpiryText(Contract(today.AddDays(-1)), today) == "Expirat de o zi" &&
                  ServiceDueRules.ExpiryText(Contract(today.AddDays(-12)), today) == "Expirat de 12 zile" && ServiceDueRules.ExpiryText(Contract(today), today) == "Expiră astăzi" &&
                  ServiceDueRules.ExpiryText(Contract(today.AddDays(1)), today) == "Expiră mâine" && ServiceDueRules.ExpiryText(Contract(today.AddDays(9)), today) == "Expiră în 9 zile" &&
                  ServiceDueRules.ExpiryText(Contract(new DateOnly(2027, 5, 1)), today) == "Valabil până la 01.05.2027",
                "The contract term is written out (\"Expirat de N zile\", \"Expiră în N zile\", \"Fără termen\")");

            ServiceDueRow Row(int id, DateOnly nextDue, ServiceContract contract, decimal? latitude = 45.75m, decimal? longitude = 21.22m) =>
                new(3, "Demo SRL", contract, new(new(id, 1, 5, null, nextDue, 0), "Sediu", "Str. A 1", false), latitude, longitude, new DateOnly(2026, 8, 1));
            var overdueExpired = Row(1, today.AddDays(-5), Contract(today.AddDays(-2)));
            var soonExpiring = Row(2, today.AddDays(10), Contract(today.AddDays(12)));
            var okNoTerm = Row(3, today.AddDays(90), Contract(null));
            var off = Row(4, today.AddDays(-50), Contract(today.AddDays(-9), active: false));
            Check(MaintenanceMapRules.Fill(overdueExpired, today, 30) == "overdue" && MaintenanceMapRules.Fill(soonExpiring, today, 30) == "soon" && MaintenanceMapRules.Fill(okNoTerm, today, 30) == "ok" &&
                  MaintenanceMapRules.Fill(off, today, 30) == "off" && MaintenanceMapRules.Fill(soonExpiring, today, 5) == "ok",
                "The fill of a marker shows the due state only (grey for a contract switched Off, whatever its dates); the threshold is the one given");
            Check(MaintenanceMapRules.Badge(overdueExpired, today, 30) == "expired" && MaintenanceMapRules.Badge(soonExpiring, today, 30) == "soon" && MaintenanceMapRules.Badge(okNoTerm, today, 30) == "" &&
                  MaintenanceMapRules.Badge(off, today, 30) == "" && MaintenanceMapRules.Badge(soonExpiring, today, 5) == "",
                "The badge shows the contract term only: expired, expiring soon, or nothing (also nothing for a contract Off)");
            Check(MaintenanceMapRules.Fill(overdueExpired, today, 30) != MaintenanceMapRules.Badge(overdueExpired, today, 30) && MaintenanceMapRules.Fill(overdueExpired, today, 30) == "overdue" && MaintenanceMapRules.Badge(overdueExpired, today, 30) == "expired",
                "An overdue point of an expired contract shows both states, each in its own place");
            var marker = MaintenanceMapRules.Marker(overdueExpired, today, 30, 30);
            Check(marker is { Id: 1, Lat: 45.75, Lng: 21.22, Fill: "overdue", Contract: "expired", Title: "Demo SRL · Sediu" } && MaintenanceMapRules.Marker(Row(5, today, Contract(null), null, null), today, 30, 30) is null &&
                  MaintenanceMapRules.Marker(Row(6, today, Contract(null), 45.75m, null), today, 30, 30) is null && !Row(5, today, Contract(null), null, null).HasCoordinates && overdueExpired.HasCoordinates,
                "A row with both coordinates becomes a marker with plain fields; one without them (or with only one) gets none and is listed apart");
            Check(MaintenanceMapRules.CoordinatesText(overdueExpired) == "45.75, 21.22" && MaintenanceMapRules.CoordinatesText(Row(5, today, Contract(null), null, null)) == "" &&
                  MaintenanceMapRules.AddInterventionUrl(3, 17) == "/beneficiari/3?adauga-interventie=17",
                "The panel shows the coordinates and links to the form of a new intervention on the point");
            var sameBuilding = new[] { overdueExpired, Row(7, today.AddDays(3), Contract(null)), Row(8, today.AddDays(3), Contract(null), 46m, 22m), Row(9, today.AddDays(3), Contract(null), null, null) };
            Check(MaintenanceMapRules.SameLocation(sameBuilding, overdueExpired).Select(row => row.Point.Point.Id).SequenceEqual([7]) && MaintenanceMapRules.SameLocation(sameBuilding, sameBuilding[3]).Count == 0,
                "The other points at the same coordinates are listed with the selected one");

            var defaults = new MapOptions().Normalized();
            Check(defaults.TileUrl == MapOptions.DefaultTileUrl && defaults.MinZoom == 3 && defaults.MaxZoom == 19 && defaults.Attribution.Contains("OpenStreetMap") &&
                  new MapOptions { TileUrl = "http://tiles.example/{z}/{x}/{y}.png" }.Normalized().TileUrl == MapOptions.DefaultTileUrl &&
                  new MapOptions { TileUrl = "https://tiles.example/{z}/{x}.png" }.Normalized().TileUrl == MapOptions.DefaultTileUrl &&
                  new MapOptions { TileUrl = "https://tiles.example/{z}/{x}/{y}.png" }.Normalized().TileUrl == "https://tiles.example/{z}/{x}/{y}.png" &&
                  new MapOptions { TileUrl = "https://{s}.tiles.example/{z}/{x}/{y}{r}.png" }.Normalized().TileUrl == "https://{s}.tiles.example/{z}/{x}/{y}{r}.png" &&
                  new MapOptions { Attribution = " " }.Normalized().Attribution == MapOptions.DefaultAttribution &&
                  new MapOptions { MinZoom = 25, MaxZoom = 2 }.Normalized() is { MinZoom: 18, MaxZoom: 18 } && new MapOptions { CenterLatitude = 200 }.Normalized().CenterLatitude == 90,
                "The tile provider comes from the configuration (only https with {z}/{x}/{y}); anything unusable falls back to the default, the zoom range is kept valid");
        }

        await ComponentChecks.RunAsync(Check);
        await PickupWizardChecks.RunAsync(Check);
        await PickupWizardChecks.TemplateFlowAsync(Check);
        await PickupWizardChecks.LinkButtonAsync(Check);
        SupplierChecks.Rules(Check);
        await SupplierChecks.ComponentsAsync(Check);
        await SupplierChecks.PickupAsync(Check);
        await ProductGroupsChecks.RunAsync(Check);
        ReasonSummaryChecks.Run(Check);
        StructureChecks.Run(Check);
        await RobustnessChecks.RunAsync(Check);
        await NasBackupChecks.RunAsync(Check);
        BlazorStoc.Checks.OfferChecks.Run(Check);
        BlazorStoc.Checks.OfferChecks.RunSituation(Check);
        await InvoiceXmlChecks.RunAsync(Check);
        await InvoiceChecks.RunAsync(Check);

        // Subtask 2.11: opt-in real integration checks against the isolated blazorstoc_test MariaDB database. Skipped
        // entirely (no-op, prints nothing extra) unless RUN_MARIA_INTEGRATION_CHECKS=1, so the default dotnet run/CI
        // experience (the checks above, no network, no MariaDB needed) is unchanged.
        if (Environment.GetEnvironmentVariable("RUN_MARIA_INTEGRATION_CHECKS") == "1")
        {
            var mariaConfigPath = Environment.GetEnvironmentVariable("MARIA_TEST_CONFIG_PATH")
                ?? throw new InvalidOperationException("Set MARIA_TEST_CONFIG_PATH to the test database's private config JSON.");
            var mariaConfiguration = new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddJsonFile(mariaConfigPath).Build();
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MARIA_ONLY"))) await BlazorStoc.Checks.MariaIntegrationChecks.RunAsync(mariaConfiguration);
            await BlazorStoc.Checks.MariaExtendedChecks.RunAsync(mariaConfiguration);
        }
    }
}
