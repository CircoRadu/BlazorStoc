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
    // Lines 1163-1307 of the former Program.cs.
    internal static async Task StockMovementsAsync(string[] args)
    {

        // ---- Stock movements (Task 1) ----
        async Task RejectedMovement(Func<Task> operation, string message)
        {
            try { await operation(); }
            catch (StockMovementOperationException) { Check(true, message); return; }
            throw new Exception("Expected rejection: " + message);
        }
        // Exits need a destination: unless given, an exit with a beneficiary goes to the beneficiary, any other one is a generic sale.
        StockMovementInput MovementInput(StockMovementKind kind, int quantity, string description = "Test", DateOnly? date = null,
            int? beneficiaryId = null, int? projectId = null, string reason = "", ExitDestination? destination = null,
            int? vehicleId = null, int? sourceVehicleId = null, bool noDestination = false) => new()
        {
            Kind = kind, Quantity = quantity, Description = description, Date = date ?? new DateOnly(2026, 9, 24),
            BeneficiaryId = beneficiaryId, ProjectId = projectId, Reason = reason,
            Destination = noDestination || kind == StockMovementKind.Entry ? null
                : destination ?? (beneficiaryId is not null ? ExitDestination.Beneficiary : ExitDestination.GenericSale),
            VehicleId = vehicleId, SourceVehicleId = sourceVehicleId
        };

        var entryRule = StockMovementRules.Validated(MovementInput(StockMovementKind.Entry, 3, "  Factură nouă  "), StockMovementKind.Entry, false);
        Check(entryRule.Description == "Factura noua" && entryRule.Quantity == 3, "Movement description is normalized like other stored text");
        Check(StockMovementRules.Effect(StockMovementKind.Entry, 5) == 5 && StockMovementRules.Effect(StockMovementKind.Exit, 5) == -5, "Entries add and exits subtract stock");
        Check(StockMovementRules.DisplayDate(new DateOnly(2022, 8, 22)) == "22.08.2022" && StockMovementRules.LegacyDate(new DateOnly(2022, 8, 22)) == "22-08-2022" && StockMovementRules.ParseLegacyDate("01-03-2024") == new DateOnly(2024, 3, 1),
            "Movement dates are displayed as dd.MM.yyyy while the legacy column keeps dd-MM-yyyy");
        Check(StockMovementRules.NormalizeDisplayDates("Data: 22-08-2022 → 01-03-2024; cod 99-99-2020; ref 122-08-2022") == "Data: 22.08.2022 → 01.03.2024; cod 99-99-2020; ref 122-08-2022"
              && StockMovementRules.NormalizeDisplayDates(null) == "", "Older journal texts show real dates as dd.MM.yyyy and leave other text untouched");
        try { StockMovementRules.Validated(MovementInput(StockMovementKind.Exit, 1, "  "), StockMovementKind.Exit, false); throw new Exception("Blank description accepted"); }
        catch (StockMovementOperationException) { Check(true, "Movement description is mandatory"); }
        try { StockMovementRules.Validated(MovementInput(StockMovementKind.Exit, 0), StockMovementKind.Exit, false); throw new Exception("Zero quantity accepted"); }
        catch (StockMovementOperationException) { Check(true, "Movement quantity must be at least 1"); }
        try { StockMovementRules.Validated(MovementInput(StockMovementKind.Exit, StockMovementRules.MaxQuantity + 1), StockMovementKind.Exit, false); throw new Exception("Oversize quantity accepted"); }
        catch (StockMovementOperationException) { Check(true, "Movement quantity has an upper limit"); }
        try { StockMovementRules.Validated(MovementInput(StockMovementKind.Entry, 1, beneficiaryId: 1), StockMovementKind.Entry, false); throw new Exception("Entry with beneficiary accepted"); }
        catch (StockMovementOperationException) { Check(true, "Beneficiary and project are refused for entries"); }
        {
            StockMovementInput Free(Action<StockMovementInput> change) { var input = MovementInput(StockMovementKind.Entry, 2, "Intrare libera"); change(input); return input; }
            bool Refused(StockMovementInput input, StockMovementKind kind = StockMovementKind.Entry) { try { StockMovementRules.Validated(input, kind, false); return false; } catch (StockMovementOperationException) { return true; } }
            var donation = StockMovementRules.Validated(Free(input => { input.FreeType = FreeEntryType.Donation; input.Reference = "  Bon 12  "; }), StockMovementKind.Entry, false);
            var awaited = StockMovementRules.Validated(Free(input => { input.FreeType = FreeEntryType.AwaitedInvoice; input.FreeSupplierId = 4; }), StockMovementKind.Entry, false);
            Check(donation.FreeType == FreeEntryType.Donation && donation.FreeSupplierId is null && donation.Reference == "Bon 12" && awaited.FreeSupplierId == 4,
                "A free entry keeps its reason and reference; the supplier is optional except for an awaited invoice");
            Check(Refused(Free(input => input.FreeType = FreeEntryType.AwaitedInvoice)) && Refused(Free(input => input.FreeSupplierId = 4))
                  && Refused(Free(input => { input.InvoiceId = 3; input.FreeType = FreeEntryType.Other; })) && Refused(Free(input => input.FreeType = (FreeEntryType)99))
                  && Refused(Free(input => input.Reference = new string('x', StockMovementRules.MaxReferenceLength + 1))),
                "An awaited invoice needs its supplier; a supplier needs a reason; an invoice entry has no free reason; the reference has a limit");
            var rows = new List<(int, DateOnly, StockMovementKind, ExitDestination?, int?, int, int?)>
            {
                (1, new DateOnly(2026, 1, 1), StockMovementKind.Exit, ExitDestination.GenericSale, null, 3, null),
                (2, new DateOnly(2026, 1, 5), StockMovementKind.Entry, null, null, 14, null),
                (3, new DateOnly(2026, 1, 6), StockMovementKind.Exit, ExitDestination.Vehicle, null, 4, 7),
                (4, new DateOnly(2026, 1, 7), StockMovementKind.Exit, ExitDestination.GenericSale, 7, 3, null),
                (5, new DateOnly(2026, 1, 8), StockMovementKind.Exit, ExitDestination.GenericSale, 7, 2, null)
            };
            var overIds = StockMovementRules.OverStockExits(rows, 7);
            Check(overIds.SetEquals([1, 5]) && StockMovementRules.OverStockExits(rows, 7 + 3).SetEquals([5]) && StockMovementRules.WarehouseEffect(StockMovementKind.Exit, ExitDestination.WarehouseReturn, 7, 2) == 2,
                "An exit is over the stock when the warehouse could not cover it at its date; an entry dated before heals it; use from a vehicle does not count");
            var exitWithReason = MovementInput(StockMovementKind.Exit, 1, destination: ExitDestination.GenericSale); exitWithReason.FreeType = FreeEntryType.Other;
            Check(Refused(exitWithReason, StockMovementKind.Exit), "An exit has no free-entry reason");
        }
        try { StockMovementRules.Validated(MovementInput(StockMovementKind.Exit, 1, projectId: 1), StockMovementKind.Exit, false); throw new Exception("Project without beneficiary accepted"); }
        catch (StockMovementOperationException) { Check(true, "A project requires a beneficiary"); }
        try { StockMovementRules.Validated(MovementInput(StockMovementKind.Exit, 1), StockMovementKind.Exit, true); throw new Exception("Edit without reason accepted"); }
        catch (StockMovementOperationException) { Check(true, "Editing a movement requires a reason"); }
        var movementToday = new DateOnly(2026, 9, 25);
        Check(StockMovementRules.Validated(MovementInput(StockMovementKind.Exit, 1, date: movementToday), StockMovementKind.Exit, false, movementToday).Date == movementToday &&
              StockMovementRules.Validated(MovementInput(StockMovementKind.Entry, 1, date: movementToday.AddDays(-1)), StockMovementKind.Entry, false, movementToday).Date == movementToday.AddDays(-1),
            "Movement date may be today or in the past");
        foreach (var futureKind in new[] { StockMovementKind.Entry, StockMovementKind.Exit })
            foreach (var futureIsEdit in new[] { false, true })
            {
                try { StockMovementRules.Validated(MovementInput(futureKind, 1, date: movementToday.AddDays(1), reason: "Corectie"), futureKind, futureIsEdit, movementToday); throw new Exception("Future movement date accepted"); }
                catch (StockMovementOperationException exception)
                {
                    Check(exception.Message == StockMovementRules.FutureDateMessage,
                        $"A future movement date is rejected ({futureKind}, {(futureIsEdit ? "edit" : "create")})");
                }
            }
        try { StockMovementRules.Validated(MovementInput(StockMovementKind.Entry, 1, date: new DateOnly(2099, 1, 1)), StockMovementKind.Entry, false); throw new Exception("Distant future date accepted"); }
        catch (StockMovementOperationException) { Check(true, "A date far in the future is rejected against the real current day"); }
        Check(AuditNavigation.TargetUrl(new AuditEvent(Guid.NewGuid(), DateTime.UtcNow, "a", "r", AuditEntities.StockMovement, AuditActions.Create, "t", "d", "", "5")) == "/miscari/5" &&
              AuditNavigation.TargetUrl(new AuditEvent(Guid.NewGuid(), DateTime.UtcNow, "a", "r", AuditEntities.StockMovement, AuditActions.Delete, "t", "d", "", "5")) is null,
            "Journal links movement events to their product page and not after deletion");

        Check(AddressNormalization.Key("Str. Florilor, nr. 5, Bl. A2") == AddressNormalization.Key("strada FLORILOR 5 bloc a2") &&
              AddressNormalization.Key("Șoseaua Nordului 10") == AddressNormalization.Key("Soseaua  nordului, numarul 10") &&
              AddressNormalization.Key("Strada Florilor 5") != AddressNormalization.Key("Strada Florilor 6"),
            "Work point address normalization ignores case, diacritics, punctuation, spacing and common abbreviations");








        // Task 8: the 18 triggers already installed on the real MariaDB database (6 watched tables x 3 operations).
        Check(ChangeEventTriggers.Maria.Count == 6 && ChangeEventTriggers.Suffixes.Count() == 3,
            "6 watched tables x 3 operations = the 18 triggers already installed on the real database");

        // Relay: cursor, grace period, ledger, failures (with an in-memory source and a manual clock).
        {
            var clock = new ManualTimeProvider();
            var source = new FakeChangeSource();
            var relayFeed = new InProcessChangeFeed(null, clock);
            var seen = new List<ChangeEvent>();
            using var relaySubscription = relayFeed.Subscribe(change => { lock (seen) seen.Add(change); return Task.CompletedTask; });
            source.Add("Produs", AuditActions.Edit, "1");
            var relay = new ChangeEventRelay(source, relayFeed, NullLogger<ChangeEventRelay>.Instance,
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Sync:GraceMilliseconds"] = "1000" }).Build(), clock);
            Check(await relay.PollOnceAsync(default) == 0 && relay.Cursor == 1, "The relay starts after the newest stored event instead of replaying history");
            source.Add("Produs", AuditActions.Edit, "2");
            source.Add("Utilizator", AuditActions.Create, "7");
            Check(await relay.PollOnceAsync(default) == 0 && seen.Count == 0, "A new event is held back during the grace period");
            clock.Advance(TimeSpan.FromMilliseconds(1100));
            Check(await relay.PollOnceAsync(default) == 2 && seen.Count == 2 && seen[0].EntityId == "2" && seen[1].EntityType == "Utilizator" &&
                  seen.All(change => change.Origin == Guid.Empty) && relay.Cursor == 3,
                "After the grace period each event is published once, in order, without an origin");
            clock.Advance(TimeSpan.FromSeconds(5));
            Check(await relay.PollOnceAsync(default) == 0 && seen.Count == 2, "Polling again never publishes an event twice");

            var ownOrigin = Guid.NewGuid();
            relayFeed.Publish(new("Proiect", AuditActions.Edit, "5", ownOrigin, clock.GetUtcNow().UtcDateTime, ProjectId: 5));
            source.Add("Proiect", AuditActions.Edit, "5", projectId: 5);
            source.Add("Proiect", AuditActions.Edit, "5", projectId: 5);
            seen.Clear();
            await relay.PollOnceAsync(default);
            clock.Advance(TimeSpan.FromMilliseconds(1100));
            await relay.PollOnceAsync(default);
            Check(seen.Count == 1 && seen[0].Origin == Guid.Empty, "The trigger's copy of a change already published by a session is dropped once; a second change is still announced");

            relayFeed.Publish(new("Proiect", AuditActions.Delete, "9", ownOrigin, clock.GetUtcNow().UtcDateTime, ProjectId: 9));
            clock.Advance(TimeSpan.FromSeconds(40));
            Check(!relayFeed.TryConsumeLocal("Proiect", AuditActions.Delete, "9"), "A locally published change is forgotten after the ledger lifetime");

            source.FailNextRead = true;
            var cursorBeforeFailure = relay.Cursor;
            source.Add("Produs", AuditActions.Edit, "11");
            try { await relay.PollOnceAsync(default); throw new Exception("Failing source did not throw"); }
            catch (InvalidOperationException) { }
            seen.Clear();
            clock.Advance(TimeSpan.FromMilliseconds(1100));
            await relay.PollOnceAsync(default);
            clock.Advance(TimeSpan.FromMilliseconds(1100));
            await relay.PollOnceAsync(default);
            Check(relay.Cursor > cursorBeforeFailure && seen.Any(change => change.EntityId == "11"), "After a failed read the relay resumes from its cursor and loses nothing");
        }
    }
}
