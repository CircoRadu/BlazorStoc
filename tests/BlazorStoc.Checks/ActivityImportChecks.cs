using System.Text.RegularExpressions;
using BlazorStoc.Modules;
using BlazorStoc.Modules.ActivityImport;
using BlazorStoc.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorStoc.Checks;

// The ActivityApp import module, stage 1 (no server needed): the contract against the structure of the source (missing optional/required column, other type, renamed column,
// new column, fingerprint), the rules of the settings, the merge of the types with the administrator's choice, the guarantee that only SELECT statements are sent to ActivityApp,
// the administrator-only permission, and the module wiring.
public static class ActivityImportChecks
{
    private static IReadOnlyList<ActivityColumn> Full()
    {
        var kinds = new Dictionary<ColumnKind, string> { [ColumnKind.Number] = "bigint", [ColumnKind.Text] = "nvarchar", [ColumnKind.Date] = "datetime", [ColumnKind.Flag] = "bit" };
        return [.. ActivityContract.Tables.SelectMany(table => table.Columns.Select(column => new ActivityColumn(table.Name, column.Name, kinds[column.Kind])))];
    }

    private static ActivityImportInput Input(Func<ActivityImportInput, ActivityImportInput>? change = null)
    {
        var input = new ActivityImportInput(true, "SERVER\\ACTIVITYAPP", 1433, "TIPActivity", "citire", "parola", null, 0, 24, 200, false);
        return change?.Invoke(input) ?? input;
    }

    public static async Task RunAsync(Action<bool, string> check)
    {
        Structure(check);
        Rules(check);
        Types(check);
        Planner(check);
        Tickets(check);
        Sync(check);
        Keywords(check);
        SqlScan(check);
        Wiring(check);
        await ActivityImportPanelChecks.RunAsync(check);
        await ActivityKeywordsPanelChecks.RunAsync(check);
        await BeneficiaryChecksSectionChecks.RunAsync(check);
        await LiveAsync(check);
    }

    // Against a real SQL Server, only when ACTIVITYIMPORT_TEST_SERVER (and _DATABASE) name a database with the tables of the contract (the local SQLEXPRESS with a small copy):
    // the real queries, the counts of the types and the answers for a database that cannot be reached. Skipped (no check) when the variable is absent.
    private static async Task LiveAsync(Action<bool, string> check)
    {
        var server = Environment.GetEnvironmentVariable("ACTIVITYIMPORT_TEST_SERVER");
        if (string.IsNullOrWhiteSpace(server)) return;
        var database = Environment.GetEnvironmentVariable("ACTIVITYIMPORT_TEST_DATABASE") ?? "zz_ai_test";
        var source = new SqlActivitySource();
        var connection = new ActivityConnection(server, 1433, database, "", "", Integrated: true);
        var ping = await source.PingAsync(connection, CancellationToken.None);
        check(ping.Ok && !string.IsNullOrEmpty(ping.ServerVersion), "Activity import (SQL Server): the connection opens and reports the server version");
        var report = ActivityStructureChecker.Check(await source.ReadStructureAsync(connection, CancellationToken.None));
        check(report.Status == StructureStatus.Ok && report.NewColumns == 1 && report.Physical("Tickete", "IDEveniment") == "IDEveniment", "Activity import (SQL Server): the structure read from INFORMATION_SCHEMA matches the contract (one extra column noticed)");
        var types = await source.ReadTypesAsync(connection, report, CancellationToken.None);
        check(types.Count == 4 && types.Single(type => type.Id == 4) is { Name: "Service", Category: "Service", ContractService: true, Resolved: 1, Open: 1, Deleted: 1 }
            && types.Single(type => type.Id == 13) is { Resolved: 1, Open: 1, Deleted: 0 } && types.Single(type => type.Id == 25) is { ContractService: false, Resolved: 1 } && types.Single(type => type.Id == 3) is { Resolved: 0, Open: 0, Deleted: 0 },
            "Activity import (SQL Server): the ticket types come with their counts (resolved = has an event, open = none or empty, deleted apart); a type without tickets has zeros");
        var livePartners = await source.ReadPartnersAsync(connection, report, 50, CancellationToken.None);
        check(livePartners.Count == 130 && livePartners.Select(partner => partner.Id).SequenceEqual(Enumerable.Range(1, 130).Select(index => (long)index)) && livePartners[0].Name == "Partener 1 Ă" && livePartners[0].TaxId == "RO1000001" && livePartners[9].TaxId == "" && livePartners[0].City == "Oraș" && livePartners[0].Email == "",
            "Activity import (SQL Server): the partners are read in pages of 50 by ID, none missed or repeated, with accents and empty values intact");
        var livePoints = await source.ReadWorkPointsAsync(connection, report, 50, CancellationToken.None);
        check(livePoints.Count == 3 && livePoints[0] is { Id: 1, PartnerId: 1, Objective: "Sediu", Gps: "45.7489, 21.2087", Active: true, Closed: false } && livePoints[1] is { Active: false, Closed: true } && livePoints[2] is { Active: null, Closed: null, Address: "Orfan" },
            "Activity import (SQL Server): the work points come with the flags active and closed (null when empty)");
        var liveTickets = await source.ReadTicketsAsync(connection, report, [4, 13], new DateTime(2026, 1, 1), null, 50, CancellationToken.None);
        check(liveTickets.Select(ticket => ticket.Id).SequenceEqual([1L, 5L]) && liveTickets[0] is { TypeId: 4, PartnerId: 5, WorkPointId: 7, Number: "T-1", Title: "Titlu Ț 1", Problem: "Problema 1", Executor: "Ion Ț", EventId: 100, EventStatus: 1 }
            && liveTickets[0].EventEnd == new DateTime(2026, 3, 2, 10, 0, 0) && liveTickets[0].Report == "Problema 1\nRouter înlocuit" && liveTickets[1] is { WorkPointId: 0, EventStatus: 0, Executor: "Maria" },
            "Activity import (SQL Server): the resolved tickets of the chosen types come with their event (the open, the deleted, the older and the other types are not read)");
        var olderToo = await source.ReadTicketsAsync(connection, report, [4, 13, 25], new DateTime(2025, 1, 1), null, 50, CancellationToken.None);
        check(olderToo.Select(ticket => ticket.Id).SequenceEqual([1L, 5L, 6L]) && (await source.ReadTicketsAsync(connection, report, [], new DateTime(2025, 1, 1), null, 50, CancellationToken.None)).Count == 0,
            "Activity import (SQL Server): a type and an earlier date widen the reading; no chosen type reads nothing");
        var livePartnerIds = await source.ReadLiveIdsAsync(connection, report, LiveKinds.Partners, [1, 2, 130, 131, 9999], CancellationToken.None);
        var manyIds = await source.ReadLiveIdsAsync(connection, report, LiveKinds.Partners, [.. Enumerable.Range(1, 1200).Select(index => (long)index)], CancellationToken.None);
        check(livePartnerIds.SetEquals([1L, 2L, 130L]) && manyIds.Count == 130, "Activity import (SQL Server): of the IDs asked for, only those that still exist are returned, also when there are more than one group of 500");
        var liveTicketIds = await source.ReadLiveIdsAsync(connection, report, LiveKinds.Tickets, [1, 2, 3, 4, 5, 88], CancellationToken.None);
        var livePointIds = await source.ReadLiveIdsAsync(connection, report, LiveKinds.WorkPoints, [1, 2, 3, 50], CancellationToken.None);
        check(liveTicketIds.SetEquals([1L, 2L, 4L, 5L]) && livePointIds.SetEquals([1L, 2L, 3L]) && (await source.ReadLiveIdsAsync(connection, report, LiveKinds.Tickets, [], CancellationToken.None)).Count == 0,
            "Activity import (SQL Server): a ticket marked deleted does not count as existing; work points are looked up the same way; no IDs ask nothing");
        var partial = ActivityStructureChecker.Check([.. await source.ReadStructureAsync(connection, CancellationToken.None)]);
        var withoutCategory = ActivityStructureChecker.Check([.. (await source.ReadStructureAsync(connection, CancellationToken.None)).Where(column => column.Column != "TipActivitate")]);
        var reduced = await source.ReadTypesAsync(connection, withoutCategory, CancellationToken.None);
        check(partial.Status == StructureStatus.Ok && reduced.Count == 4 && reduced.All(type => type.Category == ""), "Activity import (SQL Server): a column missing from the contract report is left out of the SELECT and the list still comes");
        var wrongDatabase = await source.PingAsync(connection with { Database = "zz_ai_does_not_exist" }, CancellationToken.None);
        var wrongServer = await source.PingAsync(new ActivityConnection("zz-no-such-host.invalid", 1433, database, "u", "p"), CancellationToken.None);
        check(!wrongDatabase.Ok && wrongDatabase.Message.Length > 0 && !wrongServer.Ok && wrongServer.Message.Length > 0, "Activity import (SQL Server): an unknown database or an unreachable server is answered in words, never thrown");
        var blockedColumns = ActivityStructureChecker.Check([]);
        var failed = false;
        try { await source.ReadTypesAsync(connection, blockedColumns, CancellationToken.None); } catch (ActivitySourceException) { failed = true; }
        check(failed, "Activity import (SQL Server): the types are not read when the structure does not allow it");
    }

    private static void Structure(Action<bool, string> check)
    {
        var full = Full();
        var ok = ActivityStructureChecker.Check(full);
        check(ok.Status == StructureStatus.Ok && ok.Issues.Count == 0 && ok.BlockedEntities.Count == 0 && ok.Physical("Tickete", "IDActivitate") == "IDActivitate",
            "Activity import: a structure that matches the contract is Ok, with no issue and every column usable");

        var withoutOptional = ActivityStructureChecker.Check([.. full.Where(column => !(column.Table == "ActivitatiContracte" && column.Column == "TipActivitate"))]);
        check(withoutOptional.Status == StructureStatus.Partial && withoutOptional.Issues.Single() is { Required: false, Problem: "coloana lipsește" } && withoutOptional.Physical("ActivitatiContracte", "TipActivitate") is null
            && withoutOptional.Physical("ActivitatiContracte", "Denumire") is not null && withoutOptional.BlockedEntities.Count == 0,
            "Activity import: a missing optional column makes the run partial; the column is left out and nothing is blocked");

        var withoutRequired = ActivityStructureChecker.Check([.. full.Where(column => !(column.Table == "ActivitatiContracte" && column.Column == "Denumire"))]);
        check(withoutRequired.Status == StructureStatus.Changed && withoutRequired.IsBlocked(ActivityContract.TicketTypes) && !withoutRequired.IsBlocked(ActivityContract.Tickets)
            && withoutRequired.Issues.Single().Table == "ActivitatiContracte" && withoutRequired.Issues.Single().Column == "Denumire",
            "Activity import: a missing required column stops only the entity that needs it and names the table and the column");

        var otherKind = ActivityStructureChecker.Check([.. full.Select(column => column is { Table: "Tickete", Column: "IDActivitate" } ? column with { DataType = "nvarchar" } : column)]);
        check(otherKind.Status == StructureStatus.Changed && otherKind.IsBlocked(ActivityContract.Tickets) && otherKind.Issues.Single() is { Problem: "alt tip de date", Found: "nvarchar" } && otherKind.Physical("Tickete", "IDActivitate") is null,
            "Activity import: a column that changed from number to text is reported with the type found and is not used");

        var sameKind = ActivityStructureChecker.Check([.. full.Select(column => column is { Table: "Tickete", Column: "IDActivitate" } ? column with { DataType = "int" } : column)]);
        check(sameKind.Status == StructureStatus.Ok, "Activity import: bigint turned into int (same kind of value) is not a change of structure");

        var flagAsNumber = ActivityStructureChecker.Check([.. full.Select(column => column is { Table: "Tickete", Column: "Sters" } ? column with { DataType = "tinyint" } : column)]);
        check(flagAsNumber.Status == StructureStatus.Ok, "Activity import: a yes/no column kept as a small number is accepted");

        var noTable = ActivityStructureChecker.Check([.. full.Where(column => column.Table != "Tickete")]);
        check(noTable.Status == StructureStatus.Changed && noTable.IsBlocked(ActivityContract.Tickets) && noTable.Issues.Any(issue => issue.Problem == "tabela lipsește") && !noTable.IsBlocked(ActivityContract.Partners),
            "Activity import: a missing table blocks its entity only");

        var caseInsensitive = ActivityStructureChecker.Check([.. full.Select(column => column with { Table = column.Table.ToUpperInvariant(), Column = column.Column.ToUpperInvariant() })]);
        check(caseInsensitive.Status == StructureStatus.Ok && caseInsensitive.Physical("Tickete", "ID") == "ID", "Activity import: names are matched without regard to case (SQL Server collation); the real name is the one used");

        var alias = new List<ContractTable> { new("T", "E", [new("Nou", ColumnKind.Number, true, ["Vechi"])]) };
        var renamed = ActivityStructureChecker.Check([new ActivityColumn("T", "Vechi", "int")], alias);
        check(renamed.Status == StructureStatus.Ok && renamed.Physical("T", "Nou") == "Vechi", "Activity import: a column renamed in ActivityApp is found by its known alias and read under the new name");

        var extra = ActivityStructureChecker.Check([.. full, new ActivityColumn("Tickete", "ColoanaNoua", "nvarchar")]);
        check(extra.Status == StructureStatus.Ok && extra.NewColumns == 1 && extra.Fingerprint != ok.Fingerprint, "Activity import: a new column is ignored (Ok) but changes the fingerprint, so the change is noticed");
        check(ActivityStructureChecker.Check([.. full.Reverse()]).Fingerprint == ok.Fingerprint
            && ActivityStructureChecker.Fingerprint([.. full, new ActivityColumn("AltaTabela", "X", "int")]) == ok.Fingerprint,
            "Activity import: the fingerprint does not depend on the order and ignores tables outside the contract");
        check(ActivityContract.KindOf("uniqueidentifier") is null && !ActivityContract.Compatible(ColumnKind.Text, ActivityContract.KindOf("uniqueidentifier")),
            "Activity import: a data type the module does not know is not compatible with any expected kind");
        FieldMap(check, full);
    }

    private static void FieldMap(Action<bool, string> check, IReadOnlyList<ActivityColumn> full)
    {
        var withNew = new List<ActivityColumn>([.. full, new("Tickete", "Detalii", "nvarchar"), new("Tickete", "Valoare", "int")]);
        var map = ActivityFieldMap.Create(new Dictionary<string, string> { ["Tickete.DescriptionInternal"] = "Detalii", ["Tickete.Subject"] = "Subject", ["Tickete.ProcesatDe"] = "" }, out var error);
        check(error is null && map is not null && map.Columns.Count == 1 && map.Columns["Tickete.DescriptionInternal"] == "Detalii" && map.ToJson() == "{\"Tickete.DescriptionInternal\":\"Detalii\"}"
            && ActivityFieldMap.FromJson(map.ToJson()).Columns["Tickete.DescriptionInternal"] == "Detalii" && ActivityFieldMap.Create(new Dictionary<string, string>(), out _)!.ToJson() == "",
            "Activity import: the map keeps only the fields that differ from the default and round-trips through its JSON");
        check(ActivityFieldMap.Create(new Dictionary<string, string> { ["Tickete.Altceva"] = "X" }, out var unknown) is null && unknown is not null
            && ActivityFieldMap.Create(new Dictionary<string, string> { ["Tickete.Subject"] = "a; DROP TABLE x" }, out var injected) is null && injected is not null
            && ActivityFieldMap.FromJson("not json").Columns.Count == 0 && ActivityFieldMap.FromJson("{\"Tickete.Altceva\":\"X\"}").Columns.Count == 0,
            "Activity import: a field that cannot be mapped or a column name that is not a plain name is refused; a damaged stored map is the default");
        var mapped = ActivityStructureChecker.Check(withNew, null, map);
        check(mapped.Status == StructureStatus.Ok && mapped.Physical("Tickete", "DescriptionInternal") == "Detalii" && mapped.Physical("Tickete", "Subject") == "Subject" && mapped.Physical("EvenimenteAgent", "DescriptionInternal") == "DescriptionInternal",
            "Activity import: the chosen column is read in place of the default for that field only");
        var gone = ActivityStructureChecker.Check(full, null, map);
        check(gone.Status == StructureStatus.Partial && gone.Issues.Single() is { Required: false } issue && issue.Problem.StartsWith("coloana aleasă", StringComparison.Ordinal) && gone.Physical("Tickete", "DescriptionInternal") == "DescriptionInternal"
            && gone.BlockedEntities.Count == 0, "Activity import: a chosen column that is gone is reported and the default stays in use (the run itself stops on it)");
        var notText = ActivityFieldMap.Create(new Dictionary<string, string> { ["Tickete.Subject"] = "Valoare" }, out _);
        check(ActivityStructureChecker.Check(withNew, null, notText).Issues.Single().Problem.Contains("nu are text", StringComparison.Ordinal)
            && ActivityFieldMap.TextColumns(withNew, "tickete").Contains("Detalii") && !ActivityFieldMap.TextColumns(withNew, "Tickete").Contains("Valoare") && !ActivityFieldMap.TextColumns(withNew, "Tickete").Contains("ID"),
            "Activity import: a column without text is refused, and only text columns of the table are offered");
        check(ActivityStructureChecker.Check(full, null, ActivityFieldMap.Empty).Fingerprint == ActivityStructureChecker.Check(full).Fingerprint && map!.Describe().Contains("Detalii", StringComparison.Ordinal),
            "Activity import: the map does not change the fingerprint of the structure");
    }

    private static void Rules(Action<bool, string> check)
    {
        check(ActivityImportRules.Validate(Input()) is null, "Activity import: the usual settings are valid");
        check(ActivityImportRules.Validate(Input(i => i with { User = "sa" })) is not null && ActivityImportRules.Validate(Input(i => i with { User = "SA" })) is not null,
            "Activity import: the \"sa\" account is refused (a dedicated read-only account is required)");
        check(ActivityImportRules.Validate(Input(i => i with { Server = "a;Password=x" })) is not null && ActivityImportRules.Validate(Input(i => i with { Server = "a b" })) is not null,
            "Activity import: a server address with separators is refused (no way to inject connection string options)");
        check(ActivityImportRules.Validate(Input(i => i with { Port = 0 })) is not null && ActivityImportRules.Validate(Input(i => i with { Port = 70000 })) is not null, "Activity import: the port is 1-65535");
        check(ActivityImportRules.Validate(Input(i => i with { Database = "TIP;Activity" })) is not null && ActivityImportRules.Validate(Input(i => i with { Database = "" })) is not null, "Activity import: the database name has no special characters");
        check(ActivityImportRules.Validate(Input(i => i with { WindowFromHour = 5, WindowToHour = 5 })) is not null && ActivityImportRules.Validate(Input(i => i with { WindowFromHour = 24 })) is not null
            && ActivityImportRules.Validate(Input(i => i with { WindowFromHour = 22, WindowToHour = 6 })) is null, "Activity import: the time window is not empty and its hours are valid (it may cross midnight)");
        check(ActivityImportRules.Validate(Input(i => i with { PageSize = 10 })) is not null && ActivityImportRules.Validate(Input(i => i with { PageSize = 5000 })) is not null, "Activity import: the page size is 50-1000");
        check(ActivityImportRules.Validate([new ActivityTypeChoice(4, "Service", true, "Ceva")]) is not null && ActivityImportRules.Validate([new ActivityTypeChoice(4, "Service", false, "Ceva")]) is null
            && ActivityImportRules.Validate([new ActivityTypeChoice(4, "Service", true, ActivityDestination.Maintenance)]) is null, "Activity import: a chosen type has a known destination");
        var settings = new ActivityImportSettings(true, "s", 1433, "d", "u", true, null, 0, 24, 200, false, null, null, "", StructureStatus.Unchecked, "", [], null, "", null);
        check(settings.EffectiveSince(new DateOnly(2026, 10, 23)) == new DateOnly(2026, 1, 1) && (settings with { SinceDate = new DateOnly(2025, 6, 1) }).EffectiveSince(new DateOnly(2026, 10, 23)) == new DateOnly(2025, 6, 1),
            "Activity import: without a chosen date the tickets are taken from 1 January of the current year");
        var connectionString = SqlActivitySource.ConnectionString(new ActivityConnection("SERVER\\ACTIVITYAPP", 1434, "TIPActivity", "citire", "p;a=ss"));
        check(connectionString.Contains("BlazorStoc-Import", StringComparison.Ordinal) && connectionString.Contains("SERVER\\ACTIVITYAPP,1434", StringComparison.Ordinal) && connectionString.Contains("Connect Timeout=8", StringComparison.Ordinal),
            "Activity import: the connection is named BlazorStoc-Import, carries the port and a short connect timeout (a password with separators is quoted by the builder)");
    }

    private static void Types(Action<bool, string> check)
    {
        ActivityTypeRow Row(long id, string name) => new(id, name, "", null, null, 1, 0, 0);
        var rows = new[] { Row(4, "Service"), Row(13, "Mentenanță"), Row(25, "Intervenție la cerere") };
        var none = MariaActivityImportService.Merge(rows, []);
        check(none.All(item => !item.Selected && item.State == "") && none.Count == 3, "Activity import: before any choice no type is selected and none is announced as new");
        var chosen = new List<(long, string, bool, string)> { (4, "Service", true, ActivityDestination.OnDemand), (13, "Mentenanță", true, ActivityDestination.Maintenance) };
        var merged = MariaActivityImportService.Merge(rows, chosen);
        check(merged.Single(item => item.Id == 4) is { Selected: true, State: ActivityImportRules.StateSelected } && merged.Single(item => item.Id == 13).Destination == ActivityDestination.Maintenance
            && merged.Single(item => item.Id == 25) is { Selected: false, State: ActivityImportRules.StateNew }, "Activity import: chosen types come back selected with their destination and a type that appeared later is \"new, not selected\"");
        var renamed = MariaActivityImportService.Merge([Row(4, "Service tehnic"), .. rows.Skip(1)], chosen);
        check(renamed.Single(item => item.Id == 4) is { Selected: false, State: ActivityImportRules.StateChanged }, "Activity import: a type whose name changed in ActivityApp is \"changed in source\" and is not selected (its import stops until confirmed)");
        var missing = MariaActivityImportService.Merge(rows.Skip(1).ToList(), chosen);
        check(missing.Single(item => item.Id == 4) is { Selected: false, State: ActivityImportRules.StateMissing, Name: "Service" }, "Activity import: a chosen type that is no longer in ActivityApp is shown with the name it had, as missing from the source");
    }

    private static ActivityPartner Partner(long id, string name, string taxId, string address = "Str. Florilor 5", string city = "Timisoara", string phone = "0722 111 222") => new(id, name, taxId, address, city, address.Length + city.Length == 0 ? "" : "Timis", phone, "");

    private static ActivityWorkPointRow Point(long id, long partner, string address, string objective = "", string gps = "", bool? closed = null) => new(id, partner, address, address.Length == 0 ? "" : "Timisoara", address.Length == 0 ? "" : "Timis", "", objective, gps, true, closed);

    private static void Planner(Action<bool, string> check)
    {
        var existing = new List<Beneficiary> { new(7, "Firma Existenta SRL", "9178894", Address: "Str. Veche 1", Phone: "0744111222"), new(8, "Ion Popescu", "", Kind: BeneficiaryKinds.Individual, Address: "Str. X 2", Phone: "0733000111") };
        var partners = new[]
        {
            Partner(1, "Firma Noua SRL", "RO 1234567"),
            Partner(2, "Firma Existenta SRL", "RO9178894"),
            Partner(3, "Maria Ionescu", ""),
            Partner(4, "Ion Popescu", "-"),
            Partner(5, "Cui Prost SRL", "ABC123"),
            Partner(6, "Dublu A", "5555555"), Partner(7, "Dublu B", "RO5555555"),
            Partner(8, "Fara Adresa SRL", "7777777", address: "", city: ""),
            Partner(9, "Fara Telefon SRL", "8888888", phone: "n/a"),
            Partner(10, "Persoana CNP", "1800101123456"),
            Partner(11, "Doua Telefoane SRL", "9999991", phone: "0256 123 456 / 0722 000 111"),
            Partner(12, "Georgel Vasile", "Georgel Vasile"),
            Partner(13, "Cui Lung SRL", "12345678901234")
        };
        var plans = ActivityPartnerPlanner.PlanPartners(partners, existing, []);
        PartnerPlan Of(long id) => plans.Single(plan => plan.SourceId == id);
        check(Of(1) is { Action: PlanAction.Create, Input: { Cui: "1234567", Kind: BeneficiaryKinds.Legal } } && Of(1).Input!.Address == "Str. Florilor 5, Timisoara, Timis" && Of(1).Input!.Phone == "0722111222",
            "Activity import (partners): a company with a valid tax id is created with the CUI without RO, the address put together and the first phone number");
        check(Of(2) is { Action: PlanAction.Link, LocalId: 7 }, "Activity import (partners): a company that exists in BlazorStoc (same CUI, with or without RO) is only linked");
        check(Of(3) is { Action: PlanAction.Skipped, Input: null } && Of(4).Action == PlanAction.Skipped && Of(12).Action == PlanAction.Skipped && Of(3).Message == ActivityPartnerPlanner.PersonNotTaken,
            "Activity import (partners): a partner without tax id, or with a name in the tax id field, is a person and is not imported (only companies with a CUI are)");
        check(Of(5).Action == PlanAction.Skipped && Of(5).Message == ActivityPartnerPlanner.PersonNotTaken, "Activity import (partners): a tax id with letters (other than RO) is not a CUI: the partner is not imported");
        check(Of(13) is { Action: PlanAction.Create, Check: "cui_invalid_form", Input.NeedsCheck: true } && Of(13).Input!.Cui == "123456789012",
            "Activity import (partners): a CUI of only digits but of the wrong form is still taken, to be verified by a human (the CUI as typed, cut to the field)");
        check(Of(6).Action == PlanAction.Problem && Of(7).Action == PlanAction.Problem && Of(6).Message.Contains("repetat", StringComparison.Ordinal), "Activity import (partners): a tax id repeated in ActivityApp makes every partner with it a problem");
        check(Of(8).Action == PlanAction.Problem && Of(8).Message.Contains("adres", StringComparison.Ordinal) && Of(9) is { Action: PlanAction.Create, Input.Phone: "" },
            "Activity import (partners): no address is a problem; no valid phone is not (the phone is optional)");
        // ANAF: a company that lacks the address or the phone is asked of ANAF (never a person); its answer completes the plan.
        var asked = ActivityPartnerPlanner.NeedsAnaf(partners, existing, []);
        check(asked.OrderBy(cui => cui).SequenceEqual(new[] { "1234567", "7777777", "8888888", "9999991" }), "Activity import (partners): the companies to create (to verify them) and the one refused for a missing address are asked of ANAF; never a person, a company that exists or one with an invalid or repeated CUI");
        // The verification: a CUI ANAF knows is verified, one it does not know is not taken, and when ANAF cannot answer (or was not asked) a CUI of a valid form is taken as not verified.
        var verification = ActivityPartnerPlanner.PlanPartners(partners, existing, [], new Dictionary<string, CompanyData>
        {
            ["1234567"] = new("", ""), ["8888888"] = new("", "", "ANAF nu răspunde."), ["9999991"] = new("", "", "CUI-ul nu este înregistrat în ANAF.", true)
        });
        PartnerPlan Checked(long id) => verification.Single(plan => plan.SourceId == id);
        check(Checked(1) is { Action: PlanAction.Create, Input.AnafVerified: true, Message: "", Check: "" } && Checked(9) is { Action: PlanAction.Create, Input.AnafVerified: false, Check: "anaf_unavailable" } && Checked(9).Message.Contains("De verificat", StringComparison.Ordinal)
                && Checked(11) is { Action: PlanAction.Create, Check: "cui_unknown", Input.AnafVerified: false },
            "Activity import (partners): a company ANAF knows is created as verified; one that ANAF does not know, or when ANAF cannot answer, is created to be verified by a human");
        check(ActivityPartnerPlanner.PlanPartners(partners, existing, []).Where(plan => plan.Action == PlanAction.Create && plan.Check.Length == 0).All(plan => !plan.Input!.AnafVerified && plan.Message == ""), "Activity import (partners): without any lookup nothing is marked verified and nothing is said about ANAF");
        var viaAnaf = ActivityPartnerPlanner.PlanPartners(partners, existing, [], new Dictionary<string, CompanyData> { ["7777777"] = new("Str. ANAF 1, Cluj", ""), ["8888888"] = new("", "+40 744 100 200") });
        PartnerPlan Filled(long id) => viaAnaf.Single(plan => plan.SourceId == id);
        check(Filled(8) is { Action: PlanAction.Create } && Filled(8).Input!.Address == "Str. ANAF 1, Cluj" && Filled(8).Message.StartsWith(ActivityPartnerPlanner.AnafFilledPrefix, StringComparison.Ordinal) && Filled(9).Input!.Phone == "+40744100200",
            "Activity import (partners): the address and the phone that ANAF gives complete the plan, and the plan says so");
        check(ActivityPartnerPlanner.PlanPartners(partners, existing, [], new Dictionary<string, CompanyData> { ["7777777"] = new("", "", "ANAF nu răspunde.") }).Single(plan => plan.SourceId == 8) is { Action: PlanAction.Problem, Check: "anaf_unavailable" } noAddress && noAddress.Message.Contains("(ANAF nu a putut răspunde)", StringComparison.Ordinal),
            "Activity import (partners): a company without address that ANAF cannot answer for stays out, in the queue to verify, with the reason");
        check(Of(10) is { Action: PlanAction.Skipped, Input: null }, "Activity import (partners): a 13-digit personal code is not a CUI: the partner is a person and is not imported");
        check(Of(11).Input!.Phone == "0256123456", "Activity import (partners): of several phone numbers the first valid one is taken");
        var tied = ActivityPartnerPlanner.PlanPartners(partners.Take(2).ToList(), existing, [new ImportMapEntry("partner", 1, 7), new ImportMapEntry("partner", 2, 999)]);
        check(tied.Single(plan => plan.SourceId == 1).Action == PlanAction.AlreadyLinked && tied.Single(plan => plan.SourceId == 2) is { Action: PlanAction.Problem } p && p.Message.Contains("nu mai există", StringComparison.Ordinal),
            "Activity import (partners): a tie made before is kept; a tie to a beneficiary deleted since is a problem, not recreated by itself");
        check(ActivityPartnerPlanner.PlanPartners(partners.Take(2).ToList(), [], []).Select(plan => plan.Hash).Distinct().Count() == 2 && ActivityPartnerPlanner.Phone("tel 0722-111-222, fax") == "0722111222" && ActivityPartnerPlanner.Phone("abc") == "",
            "Activity import (partners): the hash differs per partner and phone numbers are picked out of free text");

        var points = new[]
        {
            Point(100, 1, "Str. Florilor 5", "Sediu"),
            Point(101, 1, "Bd. Libertatii 9", "Depozit", "45.7489, 21.2087"),
            Point(102, 1, "Bulevardul Libertatii nr. 9"),
            Point(103, 2, "Str. Veche 1"),
            Point(104, 2, "Str. Noua 3", closed: true),
            Point(105, 5, "Str. Oarecare 1"),
            Point(106, 999, "Str. Orfana 1"),
            Point(107, 1, "", "Fara adresa")
        };
        var locals = new Dictionary<int, IReadOnlyList<WorkPoint>> { [7] = [new WorkPoint(70, 7, "Punct de lucru principal", "Str. Veche 1", IsPrimary: true), new WorkPoint(71, 7, "Magazin", "Str. Noua 3")], [8] = [new WorkPoint(80, 8, "Punct de lucru principal", "Str. X 2", IsPrimary: true)] };
        var pointPlans = ActivityPartnerPlanner.PlanPoints(points, plans, [], locals);
        PointPlan P(long id) => pointPlans.Single(plan => plan.SourceId == id);
        check(P(100) is { Action: PlanAction.Link, ToMain: true, LocalId: null }, "Activity import (points): the point with the address of a partner to be created is tied to the main point created with it");
        check(P(101) is { Action: PlanAction.Create, Input: { UseCoordinates: true } } && P(101).Input!.CoordinatesText == "45.7489, 21.2087" && P(101).Input!.Name == "Depozit",
            "Activity import (points): another address becomes an additional point named by its objective, with the coordinates when they are valid");
        check(P(102).Action == PlanAction.Problem && P(102).Message.Contains("aceeași adresă", StringComparison.Ordinal), "Activity import (points): two points of a partner with the same address (also written differently) are not both created");
        check(P(103) is { Action: PlanAction.Link, ToMain: true, LocalId: 70 }, "Activity import (points): for an existing beneficiary the point with the address of its main point is tied to that one");
        check(P(104) is { Action: PlanAction.Link, ToMain: false, LocalId: 71 }, "Activity import (points): a point whose address an additional point of the beneficiary already has is tied to it, not created again");
        check(P(105).Action == PlanAction.Skipped && P(106).Action == PlanAction.Skipped && P(106).Message.Contains("orfan", StringComparison.Ordinal), "Activity import (points): points of a partner with a problem, and orphan points, are skipped");
        check(P(107).Action == PlanAction.Problem, "Activity import (points): a point without address is a problem");
        var closed = ActivityPartnerPlanner.PlanPoints([Point(110, 1, "Str. Inchisa 4", closed: true)], plans, [], locals);
        check(closed[0].Input!.Description.Contains("sistat", StringComparison.Ordinal), "Activity import (points): a point marked closed in ActivityApp is kept with a note, not dropped");
        var second = ActivityPartnerPlanner.PlanPoints(points, plans, [new ImportMapEntry("workpoint", 100, 55), new ImportMapEntry("workpoint", 103, 70)], locals);
        check(second.Single(plan => plan.SourceId == 100).Action == PlanAction.AlreadyLinked && second.Single(plan => plan.SourceId == 103).Action == PlanAction.AlreadyLinked, "Activity import (points): points tied by an earlier run are not processed again");
    }

    private static ActivityTicket Ticket(long id, long type = 4, long partner = 1, long point = 0, DateTime? end = null, DateTime? opened = null, string problem = "Nu merge internetul", string report = "Nu merge internetul\nS-a schimbat routerul", string number = "T-1", string executor = "Ion") =>
        new(id, type, partner, point, opened ?? new DateTime(2026, 3, 1), number, "Internet", problem, executor, id + 1000, end ?? new DateTime(2026, 3, 2, 10, 0, 0), 1, report);

    private static void Tickets(Action<bool, string> check)
    {
        check(ActivityTicketPlanner.SplitReport("Nu merge internetul", "Nu merge internetul\nS-a schimbat routerul") is ("Nu merge internetul", "S-a schimbat routerul"),
            "Activity import (tickets): the report that begins with the problem gives the resolution as what follows it");
        check(ActivityTicketPlanner.SplitReport("Nu  merge\ninternetul", "Nu merge internetul. Inlocuit router") is ("Nu  merge\ninternetul", "Inlocuit router"),
            "Activity import (tickets): differences of spaces and line breaks do not matter when the report is compared with the problem");
        check(ActivityTicketPlanner.SplitReport("Nu merge", "Nu merge") is ("Nu merge", "") && ActivityTicketPlanner.SplitReport("Nu merge", "Nu merge. ") is ("Nu merge", ""), "Activity import (tickets): when nothing was added the resolution stays empty (none is made up)");
        check(ActivityTicketPlanner.SplitReport("Nu merge", "Am reinstalat tot") is ("Nu merge", "Am reinstalat tot"), "Activity import (tickets): a report the operator rewrote is the resolution as a whole");
        check(ActivityTicketPlanner.SplitReport("", "Raport") is ("", "Raport") && ActivityTicketPlanner.SplitReport("Problema", "") is ("Problema", ""), "Activity import (tickets): with no problem the report is the resolution; with no report there is no resolution");
        var notes = ActivityTicketPlanner.Notes("T-9", "Internet", "Nu merge", "Router nou", "Ion");
        check(notes == "Preluat din ActivityApp, tichet T-9: Internet\nProblema: Nu merge\nRezolvare: Router nou\nExecutant: Ion", "Activity import (tickets): the notes tell the ticket, the problem, the resolution and the executor");
        check(ActivityTicketPlanner.Notes("T", "t", new string('x', 5000), "", "").Length == 2000 && ActivityTicketPlanner.Notes("T", "t", new string('x', 5000), "", "").EndsWith('…'), "Activity import (tickets): notes longer than the 2000 characters of an intervention are cut (the whole text stays in the source detail)");

        var locals = new Dictionary<int, IReadOnlyList<WorkPoint>>
        {
            [7] = [new WorkPoint(70, 7, "Punct de lucru principal", "Str. A 1", IsPrimary: true), new WorkPoint(71, 7, "Depozit", "Str. B 2")],
            [8] = [new WorkPoint(80, 8, "Punct de lucru principal", "Str. C 3", IsPrimary: true)]
        };
        var map = new List<ImportMapEntry>
        {
            new("partner", 1, 7), new("partner", 2, 8), new("partner", 4, 999), new("workpoint", 500, 71), new("workpoint", 501, 80), new("ticket", 15, 321)
        };
        var destinations = new Dictionary<long, string> { [4] = ActivityDestination.OnDemand, [13] = ActivityDestination.Maintenance };
        var tickets = new[]
        {
            Ticket(10), Ticket(11, point: 500), Ticket(12, partner: 3), Ticket(13, type: 13), Ticket(14, point: 501), Ticket(15),
            Ticket(16, end: new DateTime(2999, 1, 1)), Ticket(17, end: DateTime.MinValue.AddYears(1)), Ticket(18, partner: 4), Ticket(19, type: 99), Ticket(20, partner: 2, point: 500)
        };
        // Only the tickets whose text mentions a keyword of their type are taken.
        var routerWords = new Dictionary<long, IReadOnlyList<string>> { [4] = ["router"] };
        var plans = ActivityTicketPlanner.Plan(tickets, destinations, map, locals, new DateOnly(2026, 10, 23), routerWords);
        TicketPlan T(long id) => plans.Single(plan => plan.SourceId == id);
        check(T(10) is { Action: PlanAction.Create, BeneficiaryId: 7, WorkPointId: 70 } && T(10).PerformedOn == new DateOnly(2026, 3, 2) && T(10).Notes.Contains("Rezolvare: S-a schimbat routerul", StringComparison.Ordinal),
            "Activity import (tickets): a resolved ticket becomes an intervention at the main point of its client's beneficiary, dated by the end of the event");
        check(T(11) is { Action: PlanAction.Create, WorkPointId: 71 }, "Activity import (tickets): the work point of the ticket is used when it was tied and belongs to the beneficiary");
        check(T(14).WorkPointId == 70 && T(20).WorkPointId == 80, "Activity import (tickets): a tied point of another beneficiary is not used; the main point is");
        check(T(12).Action == PlanAction.Waiting && T(12).Message.Contains("așteaptă", StringComparison.Ordinal), "Activity import (tickets): a ticket whose client was not imported waits (it is kept and taken when the client is), not an error");
        check(T(13).Action == PlanAction.Skipped && T(13).Message.Contains("mentenanță", StringComparison.Ordinal), "Activity import (tickets): a type with the maintenance destination is said not to be imported yet");
        check(T(15).Action == PlanAction.AlreadyLinked && T(19).Action == PlanAction.Skipped, "Activity import (tickets): a ticket taken before is not taken again; a type that is not chosen is not imported");
        check(T(16).Action == PlanAction.Problem && T(16).Message.Contains("viitor", StringComparison.Ordinal) && T(17).Action == PlanAction.Problem, "Activity import (tickets): a date in the future or out of range is a problem");
        check(T(18).Action == PlanAction.Problem && T(18).Message.Contains("nu mai există", StringComparison.Ordinal), "Activity import (tickets): a client tied to a beneficiary deleted since is a problem");
        var noEnd = ActivityTicketPlanner.Plan([Ticket(30, end: null) with { EventEnd = null, TicketDate = new DateTime(2026, 2, 5) }], destinations, map, locals, new DateOnly(2026, 10, 23), routerWords);
        check(noEnd[0] is { Action: PlanAction.Create } && noEnd[0].PerformedOn == new DateOnly(2026, 2, 5), "Activity import (tickets): without the end of the event the date of the ticket is used");
        check(plans.Select(plan => plan.Hash).Distinct().Count() == plans.Count, "Activity import (tickets): every ticket has its own content hash");
    }

    private static void Keywords(Action<bool, string> check)
    {
        IReadOnlyList<string> words = ["mentenanță", "revizie periodică"];
        KeywordMatch M(string text) => ActivityKeywords.Match(text, words);
        check(ActivityKeywords.Normalize("  MentenanȚa,  ŞI;;Revizie!! ") == "mentenanta si revizie" && ActivityKeywords.Normalize("") == "" && ActivityKeywords.Normalize("...") == "",
            "Activity keywords: the text is compared without case, diacritics or punctuation, with white space as one space");
        check(M("Am facut MENTENANȚA, curatat filtre.") is { Kind: KeywordMatchKind.Exact, Word: "mentenanță" } && ActivityKeywords.Match("s-au facut mentenante", ["mentenan"]).Kind == KeywordMatchKind.Exact && M("s-au facut mentenante").Kind == KeywordMatchKind.Approximate,
            "Activity keywords: the word, in any case, with punctuation or diacritics and as a part of a longer word, is an exact match");
        check(M("mentenanța anuală").Kind == KeywordMatchKind.Exact && M("Revizie-periodica efectuata").Kind == KeywordMatchKind.Exact && M("revizie, periodică").Kind == KeywordMatchKind.Exact,
            "Activity keywords: punctuation inside a phrase does not matter");
        check(M("am facut mentenata").Kind == KeywordMatchKind.Approximate && M("mentnanta").Kind == KeywordMatchKind.Approximate && M("mentenanra").Kind == KeywordMatchKind.Approximate && M("menteananta").Kind == KeywordMatchKind.Approximate
            && M("menttenanta").Kind == KeywordMatchKind.Approximate && M("mentenantaa").Kind == KeywordMatchKind.Exact && M("mentnenanta").Kind == KeywordMatchKind.Approximate,
            "Activity keywords: a missing, extra, changed or swapped letter is an approximate match");
        check(M("mentenan ta").Kind == KeywordMatchKind.Approximate && M("men tenanta").Kind == KeywordMatchKind.Approximate && M("revizi periodica").Kind == KeywordMatchKind.Approximate && M("reviz periodic").Kind == KeywordMatchKind.Approximate,
            "Activity keywords: a space inside the word or in a phrase, or a phrase with a mistake, is an approximate match");
        check(M("mentena").Kind == KeywordMatchKind.Approximate && M("mentenant").Kind == KeywordMatchKind.Approximate && M("mente").Kind == KeywordMatchKind.None,
            "Activity keywords: a word cut short (at least 60%) resembles the keyword, a few letters do not");
        check(M("m3ntenanta") is { Kind: KeywordMatchKind.Approximate, Found: "m3ntenanta" } && M("ment@nanta").Kind == KeywordMatchKind.Approximate && M("m3nt3n@nt@").Kind == KeywordMatchKind.Approximate && M("mentenan$a").Kind == KeywordMatchKind.Approximate
            && M("men7enanta").Kind == KeywordMatchKind.Approximate && M("m3ntenan-ta").Kind == KeywordMatchKind.Approximate && M("mentenanța!!!").Kind == KeywordMatchKind.Exact,
            "Activity keywords: digits or symbols typed in place of letters (phone keyboard) count as mistakes of an approximate match, and a symbol inside a word does not hide it");
        check(M("Am schimbat routerul si cablul").Kind == KeywordMatchKind.None && M("").Kind == KeywordMatchKind.None && M("montaj camera").Kind == KeywordMatchKind.None && ActivityKeywords.Match("mentenanta", []).Kind == KeywordMatchKind.None,
            "Activity keywords: a text without the word (or nothing resembling it) is not a match");
        check(ActivityKeywords.Match("abc", ["abcd"]).Kind == KeywordMatchKind.None && ActivityKeywords.Match("abce", ["abcd"]).Kind == KeywordMatchKind.None && ActivityKeywords.Match("abcd", ["abcd"]).Kind == KeywordMatchKind.Exact,
            "Activity keywords: a short word (under 5 letters) is matched exactly only, so a mistake cannot make it another word");
        check(ActivityKeywords.Distance("mentenanta", "mentenata") == 1 && ActivityKeywords.Distance("ab", "ba") == 1 && ActivityKeywords.Distance("", "abc") == 3 && ActivityKeywords.MaximumDistance(4) == 0 && ActivityKeywords.MaximumDistance(10) == 2 && ActivityKeywords.MaximumDistance(14) == 3,
            "Activity keywords: the distance counts a swap of neighbours as one mistake and the allowance grows with the length");
        check(ActivityKeywordRules.Validate("  mentenanță  ", out var cleaned) is null && cleaned == "mentenanță" && ActivityKeywordRules.Validate("a,b", out _) is not null && ActivityKeywordRules.Validate("x", out _) is not null && ActivityKeywordRules.Key("Mentenanța!") == "mentenanta",
            "Activity keywords: a keyword is one word or phrase of at least 3 letters; its key ignores case, diacritics and punctuation");
        var parsed = ActivityKeywords.Parse("mentenanță; Mentenanta , revizie\nservice anual", out var parseError);
        check(parseError is null && parsed.SequenceEqual(["mentenanță", "revizie", "service anual"]) && ActivityKeywords.Parse("ab", out var tooShort).Count == 0 && tooShort is not null,
            "Activity keywords: a typed list is split by comma, semicolon or line, without repeats (compared normalized)");

        // The planner: the words apply to the chosen types, an exact word counts, an approximate one waits unless it was decided, a point without a contract changes nothing.
        var locals = new Dictionary<int, IReadOnlyList<WorkPoint>> { [7] = [new WorkPoint(70, 7, "Punct de lucru principal", "Str. A 1", IsPrimary: true), new WorkPoint(71, 7, "Depozit", "Str. B 2")] };
        var map = new List<ImportMapEntry> { new("partner", 1, 7), new("workpoint", 500, 71) };
        var destinations = new Dictionary<long, string> { [4] = ActivityDestination.OnDemand };
        var typeWords = new Dictionary<long, IReadOnlyList<string>> { [4] = ["mentenanță"] };
        var batch = new[]
        {
            Ticket(10, report: "Nu merge internetul\nAm facut mentenanta"), Ticket(11, report: "Nu merge internetul\nAm facut mentenata"), Ticket(12, report: "Nu merge internetul\nAm schimbat routerul"),
            Ticket(13, point: 500, report: "Nu merge internetul\nmentenanta"), Ticket(14, point: 500, report: "Nu merge internetul\nm3ntenanta"), Ticket(15, report: "Nu merge internetul\nmentenata")
        };
        var today = new DateOnly(2026, 10, 23);
        var planned = ActivityTicketPlanner.Plan(batch, destinations, map, locals, today, typeWords);
        TicketPlan P(IEnumerable<TicketPlan> list, long id) => list.Single(plan => plan.SourceId == id);
        check(P(planned, 10).Keyword == "mentenanță" && P(planned, 11).Approximate == "mentenanță" && P(planned, 11).Found == "mentenata" && P(planned, 11).Keyword == "" && P(planned, 12).Keyword == "" && P(planned, 12).Approximate == "",
            "Activity maintenance (planner): the exact word, the resembling word and no word are told apart in what was done");
        check(ActivityTicketPlanner.KeywordPoints(planned).OrderBy(pair => pair.WorkPointId).SequenceEqual([(7, 70), (7, 71)]), "Activity maintenance (planner): only the tickets that mention it ask for the coverage of their point, each point once");
        var covered = new HashSet<(int, int)> { (7, 70) };
        var withCoverage = ActivityTicketPlanner.WithCoverage(planned, covered);
        check(P(withCoverage, 10) is { Action: PlanAction.Create, Maintenance: false, NeedsValidation: true, MatchKind: "exact" } && P(withCoverage, 10).Message.Contains("Verificare mentenanță", StringComparison.Ordinal)
            && P(withCoverage, 13) is { Action: PlanAction.Create, Maintenance: false, NeedsValidation: false } && ActivityKeywords.Normalize(P(withCoverage, 13).Notes).Contains("nu are un contract de mentenanta activ", StringComparison.Ordinal),
            "Activity maintenance (planner): an exact word at a covered point is taken on demand and proposed to the operator (it may say that the maintenance was not done); without a contract it is only noted");
        check(P(withCoverage, 11) is { Action: PlanAction.Create, NeedsValidation: true, MatchKind: "approximate" } && P(withCoverage, 14) is { Action: PlanAction.Create, NeedsValidation: false, Approximate: "" }
            && P(withCoverage, 15) is { Action: PlanAction.Create, NeedsValidation: true },
            "Activity maintenance (planner): a resembling word at a covered point is taken and proposed too; at an uncovered point it changes nothing");
        var decided = ActivityTicketPlanner.WithCoverage(ActivityTicketPlanner.Plan(batch, destinations, map, locals, today, typeWords,
            new Dictionary<long, KeywordDecision> { [11] = KeywordDecision.Confirmed, [15] = KeywordDecision.Rejected }), covered);
        check(P(decided, 11) is { Action: PlanAction.Create, Maintenance: true, NeedsValidation: false } && P(decided, 15) is { Action: PlanAction.Create, Maintenance: false, Keyword: "" },
            "Activity maintenance (planner): a confirmed event is taken as maintenance, a rejected one on demand, and neither is proposed again");
        // The client states: a person (or discarded) client is never taken, a client to verify makes the ticket wait, a discarded ticket is skipped for good.
        var states = ActivityTicketPlanner.Plan(batch.Take(3).ToList(), destinations, map, locals, today, null, null, new HashSet<long> { 1 }, null, null);
        var holding = ActivityTicketPlanner.Plan(batch.Take(3).ToList(), destinations, map, locals, today, null, null, null, new HashSet<long> { 1 }, null);
        var dropped = ActivityTicketPlanner.Plan(batch.Take(3).ToList(), destinations, map, locals, today, typeWords, null, null, null, new HashSet<long> { 10 });
        check(states.All(plan => plan.Action == PlanAction.Skipped && plan.Message == ActivityTicketPlanner.ClientNotTaken) && holding.All(plan => plan.Action == PlanAction.Waiting && plan.Message == ActivityTicketPlanner.ClientToVerify)
            && P(dropped, 10) is { Action: PlanAction.Skipped } && P(dropped, 10).Message == ActivityTicketPlanner.TicketDiscarded && P(dropped, 11).Action == PlanAction.Create,
            "Activity maintenance (planner): the tickets of a person are skipped for good, those of a beneficiary to verify wait, a discarded ticket is not taken");
        var none = ActivityTicketPlanner.Plan(batch, destinations, map, locals, today);
        check(none.All(plan => plan.Keyword == "" && plan.Approximate == "") && ActivityTicketPlanner.Plan(batch, destinations, map, locals, today, new Dictionary<long, IReadOnlyList<string>> { [13] = ["mentenanță"] }).All(plan => plan.Keyword == ""),
            "Activity maintenance (planner): without words, or for a type that is not the ticket's, nothing is searched");
        check(P(planned, 12) is { Action: PlanAction.Skipped } && P(planned, 12).Message == ActivityTicketPlanner.NoKeyword && ActivityTicketPlanner.Plan(batch, destinations, map, locals, today).All(plan => plan.Action == PlanAction.Skipped),
            "Activity maintenance (planner): a ticket that mentions no keyword of its type (or a type without keywords) is not taken");
        var problem = ActivityTicketPlanner.Plan([Ticket(20, problem: "Cere mentenanta", report: "Cere mentenanta\nAm schimbat routerul")], destinations, map, locals, today, typeWords);
        check(problem[0].Keyword == "" && problem[0].Approximate == "", "Activity maintenance (planner): the word is searched in what was done, not in the problem (which may only ask for it)");

        // The schedule: fixed hours.
        var settings = new ActivityImportSettings(true, "s", 1433, "d", "u", true, null, 8, 18, 200, false, null, null, "", StructureStatus.Ok, "", [], null, "", null) { SyncEnabled = true, SyncMode = ActivitySyncSchedule.TimesMode, SyncTimes = "13:30, 08:00, 18:00" };
        var times = ActivitySyncSchedule.ParseTimes("13:30, 08:00 8:00 18.00;7", out var timesError);
        check(timesError is null && ActivitySyncSchedule.FormatTimes(times) == "07:00, 08:00, 13:30, 18:00" && ActivitySyncSchedule.ParseTimes("25:00", out var bad).Count == 0 && bad is not null && ActivitySyncSchedule.ParseTimes("08:61", out var bad2).Count == 0 && bad2 is not null && ActivitySyncSchedule.ParseTimes("ab", out _).Count == 0,
            "Activity sync: the fixed hours are read as HH:mm (also H, H.mm), sorted and without repeats; a time that does not exist is refused");
        DateTime At(int hour, int minute) => new(2026, 10, 23, hour, minute, 0);
        var two = ActivitySyncSchedule.ParseTimes("08:00, 13:30", out _);
        check(ActivitySyncSchedule.LatestTime(two, At(12, 0)) == At(8, 0) && ActivitySyncSchedule.LatestTime(two, At(6, 0)) == At(13, 30).AddDays(-1) && ActivitySyncSchedule.NextTime(two, At(12, 0)) == At(13, 30) && ActivitySyncSchedule.NextTime(two, At(20, 0)) == At(8, 0).AddDays(1),
            "Activity sync: the latest fixed hour reached and the next one are found, over midnight too");
        var nowUtc = new DateTime(2026, 10, 23, 10, 0, 0, DateTimeKind.Utc);
        var afternoon = At(13, 0);
        check(ActivitySyncSchedule.Due(settings, afternoon, nowUtc) && !ActivitySyncSchedule.Due(settings with { LastSyncUtc = nowUtc.AddMinutes(-30) }, afternoon, nowUtc) && ActivitySyncSchedule.Due(settings with { LastSyncUtc = nowUtc.AddHours(-6) }, afternoon, nowUtc),
            "Activity sync: at fixed hours a run is due once a fixed hour has passed since the last one (also one missed while the application was off), then waits for the next");
        check(ActivitySyncSchedule.Due(settings, At(3, 0), nowUtc.AddHours(-7)) && !ActivitySyncSchedule.Due(settings with { SyncEnabled = false }, afternoon, nowUtc) && !ActivitySyncSchedule.Due(settings with { Enabled = false }, afternoon, nowUtc) && !ActivitySyncSchedule.Due(settings with { SyncTimes = "" }, afternoon, nowUtc),
            "Activity sync: fixed hours ignore the time window, but not the switches, and without hours nothing is due");
        check(ActivitySyncSchedule.NextRun(settings, At(14, 0)) == At(18, 0) && ActivitySyncSchedule.NextRun(settings with { SyncEnabled = false }, At(14, 0)) is null
            && ActivitySyncSchedule.NextRun(settings with { SyncMode = ActivitySyncSchedule.IntervalMode, SyncIntervalMinutes = 90, LastSyncUtc = nowUtc }, afternoon) == nowUtc.ToLocalTime().AddMinutes(90),
            "Activity sync: the next run shown is the next fixed hour, or the last attempt plus the interval");
        var input = new ActivityImportInput(true, "srv", 1433, "TIPActivity", "citire", null, null, 0, 24, 200, false, true, 90, ActivitySyncSchedule.TimesMode, "08:00, 13:30");
        check(ActivityImportRules.Validate(input) is null && ActivityImportRules.Validate(input with { SyncTimes = "" }) is not null && ActivityImportRules.Validate(input with { SyncTimes = "99:00" }) is not null && ActivityImportRules.Validate(input with { SyncMode = "altceva" }) is not null
            && ActivityImportRules.Validate(input with { SyncMode = ActivitySyncSchedule.IntervalMode, SyncTimes = "" }) is null && ActivityImportRules.Validate(input with { SyncMode = ActivitySyncSchedule.IntervalMode, SyncIntervalMinutes = 3 }) is not null,
            "Activity sync: the settings need at least one valid hour when the schedule is by hours, and a valid interval (5 to 1440 minutes) otherwise");
    }

    private static void Sync(Action<bool, string> check)
    {
        check(ActivitySyncSchedule.InWindow(0, 24, 3) && ActivitySyncSchedule.InWindow(8, 18, 8) && !ActivitySyncSchedule.InWindow(8, 18, 18) && !ActivitySyncSchedule.InWindow(8, 18, 7)
            && ActivitySyncSchedule.InWindow(22, 6, 23) && ActivitySyncSchedule.InWindow(22, 6, 5) && !ActivitySyncSchedule.InWindow(22, 6, 12),
            "Activity sync: the time window is respected, also one that crosses midnight, and 0 to 24 is always");
        check(ActivitySyncSchedule.Wait(60, 0) == TimeSpan.FromMinutes(60) && ActivitySyncSchedule.Wait(60, 1) == TimeSpan.FromMinutes(120) && ActivitySyncSchedule.Wait(60, 2) == TimeSpan.FromMinutes(240)
            && ActivitySyncSchedule.Wait(60, 7) == TimeSpan.FromMinutes(240) && ActivitySyncSchedule.Wait(300, 1) == TimeSpan.FromMinutes(300),
            "Activity sync: after each failure the wait doubles, at most 4 hours, and is never shorter than the interval");
        var settings = new ActivityImportSettings(true, "s", 1433, "d", "u", true, null, 8, 18, 200, false, null, null, "", StructureStatus.Ok, "", [], null, "", null) { SyncEnabled = true, SyncIntervalMinutes = 60 };
        var noon = new DateTime(2026, 10, 23, 12, 0, 0);
        var utc = new DateTime(2026, 10, 23, 9, 0, 0, DateTimeKind.Utc);
        check(ActivitySyncSchedule.Due(settings, noon, utc), "Activity sync: a first synchronization is due when everything is on and the hour is in the window");
        check(!ActivitySyncSchedule.Due(settings with { Enabled = false }, noon, utc) && !ActivitySyncSchedule.Due(settings with { SyncEnabled = false }, noon, utc) && !ActivitySyncSchedule.Due(settings, new DateTime(2026, 10, 23, 20, 0, 0), utc),
            "Activity sync: nothing is due while the import is off, the synchronization is off or the hour is outside the window");
        check(!ActivitySyncSchedule.Due(settings with { LastSyncUtc = utc.AddMinutes(-30) }, noon, utc) && ActivitySyncSchedule.Due(settings with { LastSyncUtc = utc.AddMinutes(-61) }, noon, utc)
            && !ActivitySyncSchedule.Due(settings with { LastSyncUtc = utc.AddMinutes(-61), SyncFailures = 1 }, noon, utc) && ActivitySyncSchedule.Due(settings with { LastSyncUtc = utc.AddMinutes(-121), SyncFailures = 1 }, noon, utc),
            "Activity sync: the next one waits for the interval, and longer after a failure");

        var since = new DateTime(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc);
        check(ActivityImportAlertSource.Instances(new ActivityImportAlertState(null, "", 0, null, 0, null, 0, null)).Count == 0, "Activity alerts: with nothing wrong there is no notification");
        var alerts = ActivityImportAlertSource.Instances(new ActivityImportAlertState(since, "ActivityApp nu a răspuns la timp.", 4, since, 2, since, 5, since));
        check(alerts.Select(item => item.ObjectId).SequenceEqual([1, 2, 3, 4]) && alerts[0].Values[ActivityImportAlertSource.DetailsName].Contains("Ultimele 4 sincronizări", StringComparison.Ordinal)
            && alerts[0].Values[ActivityImportAlertSource.DetailsName].Contains("nu a răspuns", StringComparison.Ordinal) && alerts[1].Values[ActivityImportAlertSource.DetailsName].Contains("2 coloane", StringComparison.Ordinal)
            && alerts[2].Values[ActivityImportAlertSource.DetailsName].Contains("5 probleme", StringComparison.Ordinal) && alerts.All(item => item.Url == "/setari?tab=preluare-activityapp" && item.Expiry == DateOnly.FromDateTime(since.ToLocalTime())),
            "Activity alerts: one notification per lasting condition (sync failing, structure changed, open problems, new types), on the day it began, with the cause");
        check(ActivityImportAlertSource.Instances(new ActivityImportAlertState(null, "", 0, null, 0, since, 0, null)).Count == 0, "Activity alerts: open problems that were all resolved no longer raise the notification");
        var source = new ActivityImportAlertSource(null!);
        check(source.Key == "sistem.preluare-activityapp" && source.Category == "Sistem" && source.DefaultThresholdDays == 0 && source.DefaultSubject.Contains("<problema preluare>", StringComparison.Ordinal)
            && source.DefaultBody.Contains("<detalii preluare>", StringComparison.Ordinal) && source.Placeholders.Select(item => item.Name).SequenceEqual(["problema preluare", "detalii preluare"]),
            "Activity alerts: the source has its key, category, default text and placeholders");
    }

    // Only SELECT (and the two SET options of the session) may be sent to ActivityApp: the text of every command of the source class is scanned.
    private static void SqlScan(Action<bool, string> check)
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "BlazorStoc.csproj"))) root = Path.GetDirectoryName(root.TrimEnd(Path.DirectorySeparatorChar));
        if (root is null) { check(false, "Activity import: the project folder is found"); return; }
        var folder = Path.Combine(root, "Modules", "ActivityImport");
        var source = File.ReadAllText(Path.Combine(folder, "ActivitySource.cs"));
        // The SQL keywords are written in capitals in the commands; the file has none of the statements that change data or structure, and has the SELECTs.
        var forbidden = new Regex(@"\b(INSERT|UPDATE|DELETE|DROP|ALTER|CREATE|TRUNCATE|EXEC|EXECUTE|MERGE|GRANT|REVOKE|BULK|OPENROWSET|OPENQUERY)\b");
        check(Regex.Matches(source, @"\bSELECT\b").Count >= 4 && !forbidden.IsMatch(source), "Activity import: the source class sends only SELECT (and SET session options) to ActivityApp: no statement that changes data or structure");
        check(!Regex.IsMatch(source, @"SELECT\s+\*", RegexOptions.IgnoreCase), "Activity import: every SELECT lists its columns (no SELECT *)");
        check(source.Contains("LOCK_TIMEOUT", StringComparison.Ordinal) && source.Contains("READ UNCOMMITTED", StringComparison.Ordinal),
            "Activity import: the session waits little for a lock and reads without shared locks (it cannot hold up the users of ActivityApp)");
        check(!source.Contains("SqlBulkCopy", StringComparison.Ordinal) && !source.Contains("BeginTransaction", StringComparison.Ordinal), "Activity import: the source never writes (no bulk copy, no transaction)");
    }

    private static void Wiring(Action<bool, string> check)
    {
        check(Permissions.IsAdminOnly("setari-import-activityapp.edit") && Permissions.IsAdminOnly("setari-import-activityapp.view") && !Permissions.GrantableKeys.Any(key => key.StartsWith("setari-import-activityapp.", StringComparison.Ordinal))
            && Permissions.IsKnown("setari-import-activityapp.run") && Permissions.IsKnown("setari-import-activityapp.reset"),
            "Activity import: the permissions view/edit/run/reset exist and belong to the administrator alone (never granted to another user type)");
        check(!Permissions.DefaultUserKeys.Any(key => key.StartsWith("setari-import-activityapp.", StringComparison.Ordinal)), "Activity import: the default user type has none of the import permissions");
        var registry = new ModuleRegistry();
        var services = new ServiceCollection();
        var module = new ActivityImportModule();
        check(registry.Add(module, services, new ConfigurationBuilder().Build()) && registry.StatusOf(ActivityImportModule.ModuleId) == ModuleStatus.Healthy && module.Nav.Count == 0
            && services.Any(d => d.ServiceType == typeof(IActivitySource)) && services.Any(d => d.ServiceType == typeof(IActivityImportService)),
            "Activity import: the module registers its source and service, has no menu entry and starts Healthy");
        var application = new ModuleRegistry();
        BlazorStoc.Startup.ServiceRegistration.AddModules(new ServiceCollection(), new ConfigurationBuilder().Build(), application);
        check(application.StatusOf(ActivityImportModule.ModuleId) == ModuleStatus.Healthy, "Activity import: the application registers the module");
        check(new ActivitySourceException("x") is IExpectedAnswer && !ModuleRegistry.IsFault(new ActivitySourceException("x")) && !ModuleRegistry.IsFault(new ActivityImportException("x")),
            "Activity import: a source that does not answer, and a refused setting, are answers for the administrator, not faults that open the circuit");
        var actions = new[] { AuditActions.SetActivityImport, AuditActions.TestActivityImport, AuditActions.SetActivityImportTypes };
        check(actions.Distinct().Count() == 3 && actions.All(action => action.Contains("ActivityApp", StringComparison.Ordinal)), "Activity import: each operation has its own exact journal action");
    }
}
