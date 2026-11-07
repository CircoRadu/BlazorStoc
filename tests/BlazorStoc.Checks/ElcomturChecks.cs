using Bunit;
using Microsoft.Extensions.DependencyInjection;
using System.IO.Compression;
using System.Text;
using BlazorStoc.Modules.Elcomtur;
using BlazorStoc.Services;

namespace BlazorStoc.Checks;

// The Elcomtur module (no server needed): the reader of the dBase tables, the balance (invoiced minus collected), the matching of the clients with the beneficiaries by CUI, the reading of
// the backup archive and the permissions.
public static class ElcomturChecks
{
    // A dBase table in memory: columns (name, type, length), rows as text (a row starting with '*' is deleted).
    private static byte[] Dbf(DateOnly updated, (string Name, char Type, int Length)[] columns, params string[][] rows)
    {
        var recordLength = 1 + columns.Sum(column => column.Length);
        var headerLength = 32 + columns.Length * 32 + 1;
        using var stream = new MemoryStream();
        var header = new byte[32];
        header[0] = 0x30; header[1] = (byte)(updated.Year - 2000); header[2] = (byte)updated.Month; header[3] = (byte)updated.Day;
        BitConverter.GetBytes((uint)rows.Length).CopyTo(header, 4);
        BitConverter.GetBytes((ushort)headerLength).CopyTo(header, 8);
        BitConverter.GetBytes((ushort)recordLength).CopyTo(header, 10);
        stream.Write(header);
        foreach (var (name, type, length) in columns)
        {
            var field = new byte[32];
            Encoding.ASCII.GetBytes(name).CopyTo(field, 0);
            field[11] = (byte)type; field[16] = (byte)length;
            stream.Write(field);
        }
        stream.WriteByte(0x0D);
        foreach (var row in rows)
        {
            var deleted = row[0] == "*";
            stream.WriteByte(deleted ? (byte)0x2A : (byte)0x20);
            for (var index = 0; index < columns.Length; index++)
            {
                var text = row[index + 1];
                var padded = columns[index].Type == 'N' ? text.PadLeft(columns[index].Length) : text.PadRight(columns[index].Length);
                stream.Write(Encoding.ASCII.GetBytes(padded[..columns[index].Length]));
            }
        }
        return stream.ToArray();
    }

    private static readonly (string, char, int)[] ClientColumns = [("COD", 'N', 5), ("DEN", 'C', 30), ("CODFIS", 'C', 16), ("PJ_PF", 'N', 1)];
    private static readonly (string, char, int)[] SalesColumns = [("COD_CLI", 'N', 5), ("NR", 'C', 7), ("DATA", 'D', 8), ("TOT_FACT", 'N', 12), ("PLATIT", 'N', 12), ("REST_PL", 'N', 12), ("TERMEN", 'N', 3)];
    private static readonly (string, char, int)[] CashColumns = [("DATA", 'D', 8), ("INCASARI", 'N', 14), ("CONT", 'C', 7), ("CLIFUR", 'N', 5), ("FEL", 'N', 1)];

    internal static string CreateArchive(DateOnly updated, bool withMissingTable = false, string cui = "4373967")
    {
        var path = Path.Combine(Path.GetTempPath(), "elcomtur-check-" + Guid.NewGuid().ToString("N") + ".elc");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            void Add(string name, byte[] data) { var entry = zip.CreateEntry(name); using var stream = entry.Open(); stream.Write(data); }
            Add("firma1/clienti.dbf", Dbf(updated, ClientColumns, [" ", "77", "SPITAL ZAM", cui, "1"], [" ", "5", "ION POPESCU", "", "2"]));
            if (!withMissingTable)
                Add("FIRMA1/VINZARI.DBF", Dbf(updated, SalesColumns,
                    [" ", "77", "0012263", "20260910", "10000.00", "9000.00", "1000.00", "10"],
                    [" ", "77", "0012372", "20261006", "6050.00", "5435.05", "614.95", "60"],
                    [" ", "5", "0000001", "20260101", "100.00", "100.00", "0", "0"],
                    ["*", "77", "9999999", "20260101", "99999.00", "99999.00", "0", "0"]));
            Add("firma1/casa.dbf", Dbf(updated, CashColumns,
                [" ", "20260920", "4000.00", "4111", "77", "1"],
                [" ", "20260925", "100.00", "4111.2", "77", "1"],
                [" ", "20260926", "999.00", "5121", "77", "1"],
                [" ", "20260927", "50.00", "4111", "77", "2"],
                [" ", "20260928", "100.00", "4111", "5", "1"]));
            Add("firma1/FIRMA.DBF", Dbf(updated, [("DEN", (char)67, 30)], [" ", "ELECTRIC TEST SRL"]));
            Add("firma2/clienti.dbf", Dbf(updated, ClientColumns));
            Add("readme.txt", "x"u8.ToArray());
        }
        return path;
    }

    public static Task RunAsync(Action<bool, string> check)
    {
        // The reader of the tables.
        var table = DbfReader.Read(Dbf(new DateOnly(2026, 10, 30), ClientColumns, [" ", "77", "SPITAL ZAM", "4373967", "1"], ["*", "9", "STERS", "1", "1"], [" ", "5", "ION", "", "2"]));
        check(table.Rows.Count == 2 && DbfReader.Text_(table.Rows[0], "DEN") == "SPITAL ZAM" && DbfReader.Number(table.Rows[0], "COD") == 77 && DbfReader.Number(table.Rows[1], "PJ_PF") == 2 && table.LastUpdate == new DateOnly(2026, 10, 30),
            "Elcomtur: the dBase reader gives the text and numbers of the rows, leaves out deleted rows and reads the date of the table");
        var sales = DbfReader.Read(Dbf(new DateOnly(2026, 10, 30), SalesColumns, [" ", "77", "0012263", "20260910", "10000.00", "9000.00", "1000.00", "10"]));
        check(DbfReader.Date(sales.Rows[0], "DATA") == new DateOnly(2026, 9, 10) && DbfReader.Number(sales.Rows[0], "REST_PL") == 1000m && DbfReader.Date(sales.Rows[0], "NR") is null,
            "Elcomtur: a date column is read as a date, an amount as a number, a text that is not a date as none");
        var damaged = false;
        try { DbfReader.Read([1, 2, 3]); } catch (InvalidDataException) { damaged = true; }
        check(damaged, "Elcomtur: a table that is too short is refused");

        // The balance: invoiced minus collected (the logic checked on Spitalul Zam at 30.09.2026: 118.077,60).
        var clients = new[] { new ElcomturClient(77, "SPITAL ZAM", "4373967", true), new ElcomturClient(5, "ION", "", false), new ElcomturClient(9, "FARA FACTURI", "RO1", true) };
        var invoices = new[]
        {
            new ElcomturInvoice(77, "0012263", new DateOnly(2026, 9, 10), 10, 10000m, 9000m, 1000m),
            new ElcomturInvoice(77, "0012372", new DateOnly(2026, 10, 6), 60, 6050m, 5435.05m, 614.95m),
            new ElcomturInvoice(77, "0012000", new DateOnly(2026, 1, 5), 30, 500m, 500m, 0m)
        };
        var receipts = new[] { new ElcomturReceipt(77, 4000m), new ElcomturReceipt(77, 100.5m) };
        var balances = ElcomturCalculator.Calculate(clients, invoices, receipts);
        var zam = balances.Single();
        check(balances.Count == 1 && zam.Balance == 12449.5m && zam.OpenInvoices.Select(item => item.Number).SequenceEqual(["0012263", "0012372"]),
            "Elcomtur: the balance is the invoices minus the receipts, the invoices still unpaid (also the ones paid in part) are kept, ordered by due date; a client without invoices has no balance");
        var mapped = new BeneficiaryBalance(1, "Spital", zam.Balance, new DateOnly(2026, 10, 30), "firma1", [.. zam.OpenInvoices.Select(item => new BalanceInvoice(item.Number, item.Date, item.DueDate, item.Total, item.Paid, item.Rest))]);
        var today = new DateOnly(2026, 11, 4);
        check(mapped.Overdue(today).Count == 1 && mapped.Overdue(today)[0].Number == "0012263" && mapped.Overdue(today)[0].DaysLate(today) == 45 && mapped.InTerm(today).Count == 1 && mapped.InTerm(today)[0].Number == "0012372"
            && mapped.Difference == 10834.55m && mapped.IsOwed,
            "Elcomtur: the invoices with the term passed are told from those still in term at the day, with the days of delay; the difference to the balance is the balance minus the rests");
        check(!new BeneficiaryBalance(1, "X", 0m, today, "firma1", []).IsOwed && !new BeneficiaryBalance(1, "X", -50m, today, "firma1", []).IsOwed, "Elcomtur: a balance of zero or less is no payment due");

        // The matching with the beneficiaries.
        var data = new ElcomturData("firma1", new DateOnly(2026, 10, 30),
            [new(1, "ZAM", "RO4373967", true), new(2, "FIZIC", "", false), new(3, "SRL ALFA", "RO 100", true), new(4, "SRL ALFA FILIALA", "100", true), new(5, "FARA BENEFICIAR", "555", true), new(6, "FARA CUI", "", true), new(7, "ACHITAT", "777", true)],
            [new(1, "A", new DateOnly(2026, 9, 1), 10, 100m, 0m, 100m), new(3, "B", new DateOnly(2026, 9, 1), 10, 50m, 0m, 50m), new(4, "C", new DateOnly(2026, 9, 2), 30, 25m, 0m, 25m), new(5, "D", new DateOnly(2026, 9, 1), 10, 900m, 0m, 900m),
             new(7, "E", new DateOnly(2026, 9, 1), 10, 10m, 10m, 0m), new(6, "F", new DateOnly(2026, 9, 1), 10, 5m, 0m, 5m), new(2, "G", new DateOnly(2026, 9, 1), 10, 7m, 0m, 7m)],
            [new(7, 10m)]);
        var beneficiaries = new[]
        {
            new Beneficiary(10, "Spitalul Zam", "4373967"), new Beneficiary(11, "Alfa", "RO100"), new Beneficiary(12, "Persoana", "RO4373967", Kind: BeneficiaryKinds.Individual),
            new Beneficiary(13, "Achitat", "RO777"), new Beneficiary(14, "Fara date", "888")
        };
        var plan = ElcomturPlanner.Plan(data, beneficiaries);
        check(plan.Balances.Count == 3 && plan.Balances.Single(item => item.BeneficiaryId == 10).Balance == 100m && plan.Balances.Single(item => item.BeneficiaryId == 11).Balance == 75m
            && plan.Balances.Single(item => item.BeneficiaryId == 11).Invoices.Count == 2 && plan.Balances.Single(item => item.BeneficiaryId == 13).Balance == 0m && plan.Balances.All(item => item.BeneficiaryId != 14 && item.BeneficiaryId != 12),
            "Elcomtur: clients and beneficiaries are matched by the digits of the CUI (RO or not); clients with the same CUI are added together; an individual beneficiary and a beneficiary without a client get no balance");
        check(plan.Report.Matched == 3 && plan.Report.MatchedOwed == 2 && plan.Report.OwedTotal == 175m && plan.Report.UnmatchedOwed == 1 && plan.Report.UnmatchedList.Single().Code == 5 && plan.Report.UnmatchedOwedTotal == 900m
            && plan.Report.LegalClients == 6 && !plan.Report.Applied,
            "Elcomtur: the report counts the matched beneficiaries, the ones that owe, and lists the clients that owe but are no beneficiary (nothing is created for them)");

        // The archive.
        var path = CreateArchive(new DateOnly(2026, 10, 30));
        try
        {
            check(ElcomturArchive.Firms(path).SequenceEqual(["firma1"]), "Elcomtur: the firms of the archive are the folders that have the three tables (the folder without them and the loose files do not count; the names of the tables are not case sensitive)");
            var read = ElcomturArchive.Read(path, "firma1");
            check(read.Clients.Count == 2 && read.Clients[0].IsLegalEntity && !read.Clients[1].IsLegalEntity && read.Invoices.Count == 3 && read.DataDate == new DateOnly(2026, 10, 30),
                "Elcomtur: the clients, the invoices (deleted rows left out) and the date of the backup are read from the archive");
            check(read.Receipts.Count == 3 && read.Receipts.Where(item => item.ClientCode == 77).Sum(item => item.Amount) == 4100m,
                "Elcomtur: only the receipts of kind 1 on a client account (4111, 4111.2) are counted, not the payments or other accounts");
            var balance = ElcomturCalculator.Calculate(read.Clients, read.Invoices, read.Receipts).Single(item => item.Client.Code == 77);
            check(balance.Balance == 11950m, "Elcomtur: the balance of the archive is the invoices (16050) minus the receipts (4100)");
            var refused = 0;
            try { ElcomturArchive.Read(path, "firma9"); } catch (ElcomturOperationException) { refused++; }
            var notArchive = Path.Combine(Path.GetTempPath(), "elcomtur-check-" + Guid.NewGuid().ToString("N") + ".elc");
            File.WriteAllText(notArchive, "nu e o arhiva");
            try { ElcomturArchive.Firms(notArchive); } catch (ElcomturOperationException) { refused++; } finally { File.Delete(notArchive); }
            check(refused == 2, "Elcomtur: a firm that is not in the archive and a file that is not a ZIP are refused with a message");
        }
        finally { File.Delete(path); }
        var incomplete = CreateArchive(new DateOnly(2026, 10, 30), withMissingTable: true);
        try { check(ElcomturArchive.Firms(incomplete).Count == 0, "Elcomtur: an archive without the table of invoices has no firm"); }
        finally { File.Delete(incomplete); }

        // A real backup (ELCOMTUR_SAMPLE = path of a .elc; only the author's machine has one): the balance of Spitalul Zam is the one the program shows, and the balances agree with the rests
        // marked on the invoices for almost every client.
        if (Environment.GetEnvironmentVariable("ELCOMTUR_SAMPLE") is { Length: > 0 } sample && File.Exists(sample))
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var real = ElcomturArchive.Read(sample, "firma1");
            var all = ElcomturCalculator.Calculate(real.Clients, real.Invoices, real.Receipts);
            var agree = all.Count(item => Math.Abs(item.Balance - item.OpenInvoices.Sum(invoice => invoice.Rest)) <= 0.01m);
            Console.WriteLine($"Elcomtur sample: {real.Clients.Count} clients, {real.Invoices.Count} invoices, {real.Receipts.Count} receipts, date {real.DataDate:dd.MM.yyyy}, {agree}/{all.Count} balances equal the open rests, {watch.ElapsedMilliseconds} ms");
            check(all.Single(item => item.Client.Code == 77).Balance == 24118.05m && agree * 100 / all.Count >= 95, "Elcomtur: on a real backup Spitalul Zam has the balance of the program and the balances agree with the open invoices for 95% of the clients");
        }

        // The permissions.
        check(Permissions.AllKeys.Contains("elcomtur.view") && Permissions.AllKeys.Contains("elcomtur.import") && !Permissions.DefaultUserKeys.Contains("elcomtur.view")
            && Permissions.Requires["elcomtur.import"].SequenceEqual(["elcomtur.view", "beneficiari.view"]),
            "Elcomtur: the permissions to see the balances and to import are not given to the ordinary user type, and the import needs to see the balances and the beneficiaries");
        AutoChecks(check);
        AutoPanelChecks(check);
        return Task.CompletedTask;
    }

    // The automatic import: the mask, the choice of the newest file, the schedule, the integrity check of an archive, the firms with their names, the notification instances.
    private static void AutoChecks(Action<bool, string> check)
    {
        var plain = new[] { new ElcomturShareFile("Backup_ELCOMTUR_2026-11-01.elc", 100, new DateTime(2026, 11, 1, 3, 0, 0, DateTimeKind.Utc)), new ElcomturShareFile("backup_elcomtur_2026-11-03.elc", 100, new DateTime(2026, 11, 3, 3, 0, 0, DateTimeKind.Utc)),
            new ElcomturShareFile("alt.elc", 100, new DateTime(2026, 11, 4, 3, 0, 0, DateTimeKind.Utc)), new ElcomturShareFile(".tmp-elcomtur.elc", 100, new DateTime(2026, 11, 5, 3, 0, 0, DateTimeKind.Utc)),
            new ElcomturShareFile("~elcomtur.elc", 100, new DateTime(2026, 11, 6, 3, 0, 0, DateTimeKind.Utc)) };
        check(ElcomturAutoRules.Newest(plain, "*elcomtur*")?.Name == "backup_elcomtur_2026-11-03.elc" && ElcomturAutoRules.Newest(plain, "alt.elc")?.Name == "alt.elc" && ElcomturAutoRules.Newest(plain, "alt")?.Name is null
            && ElcomturAutoRules.Newest(plain, "backup_elcomtur_2026-11-0?.elc")?.Name == "backup_elcomtur_2026-11-03.elc" && ElcomturAutoRules.Newest(plain, "*nimic*") is null,
            "Elcomtur automatic: the newest file that fits the mask is chosen (capitals do not matter, * and ? work, a mask without them is the whole name, temporary and hidden files are skipped)");
        var moment = new DateTime(2026, 11, 3, 3, 0, 0, 500, DateTimeKind.Utc);
        check(!ElcomturAutoRules.IsNewer(moment, moment.AddMilliseconds(-400)) && ElcomturAutoRules.IsNewer(moment.AddSeconds(2), moment) && !ElcomturAutoRules.IsNewer(moment.AddHours(-1), moment),
            "Elcomtur automatic: a file is new only when modified after the last import (compared by the second)");
        check(!ElcomturAutoRules.IsDue(new DateTime(2026, 11, 5, 3, 59, 0), null) && ElcomturAutoRules.IsDue(new DateTime(2026, 11, 5, 4, 0, 0), null) && ElcomturAutoRules.IsDue(new DateTime(2026, 11, 5, 15, 0, 0), new DateOnly(2026, 11, 4))
            && !ElcomturAutoRules.IsDue(new DateTime(2026, 11, 5, 4, 30, 0), new DateOnly(2026, 11, 5)), "Elcomtur automatic: the check is due from 04:00, once a day");
        check(!ElcomturAutoRules.IsDue(new DateTime(2026, 11, 5, 5, 59, 0), null, 6) && ElcomturAutoRules.IsDue(new DateTime(2026, 11, 5, 6, 0, 0), null, 6) && !ElcomturAutoRules.IsDue(new DateTime(2026, 11, 5, 23, 0, 0), new DateOnly(2026, 11, 5), 6)
            && ElcomturAutoRules.IsDue(new DateTime(2026, 11, 5, 0, 0, 0), null, 0) && ElcomturAutoRules.HourText(4) == "04:00" && ElcomturAutoRules.HourText(23) == "23:00",
            "Elcomtur automatic: the check is due from the hour chosen (any hour from 00:00 to 23:00), once a day");
        var input = new ElcomturAutoInput(true, @"\\nas\backup\elcomtur", "user", null, "*elcomtur*", "firma1", "X SRL", 3);
        check(ElcomturAutoRules.Validate(input with { ScheduledHour = 0 }) is null && ElcomturAutoRules.Validate(input with { ScheduledHour = 23 }) is null && ElcomturAutoRules.Validate(input with { ScheduledHour = 24 }) == ElcomturAutoRules.HourMessage
              && ElcomturAutoRules.Validate(input with { ScheduledHour = -1 }) == ElcomturAutoRules.HourMessage && ElcomturAutoRules.Validate(input with { Enabled = false, ScheduledHour = 30 }) == ElcomturAutoRules.HourMessage,
            "Elcomtur automatic: the hour must be between 00:00 and 23:00 (also while the import is off)");
        check(ElcomturAutoRules.Validate(input) is null && ElcomturAutoRules.Validate(input with { Path = @"D:\Elcomtur", UserName = "" }) is null && ElcomturAutoRules.Validate(input with { Path = "relativ\\dir" }) is not null
            && ElcomturAutoRules.Validate(input with { UserName = " " }) is not null && ElcomturAutoRules.Validate(input with { Mask = "*" }) is not null && ElcomturAutoRules.Validate(input with { Mask = "a/b" }) is not null
            && ElcomturAutoRules.Validate(input with { FirmFolder = "" }) is not null && ElcomturAutoRules.Validate(input with { AlertDays = 0 }) is not null && ElcomturAutoRules.Validate(input with { Enabled = false, Path = "", Mask = "", FirmFolder = "" }) is null,
            "Elcomtur automatic: the settings are checked (network path with account, or local directory; mask; firm; threshold 1-365), nothing is required while the import is off");

        // The integrity of the archive: a good one passes, one with a damaged byte is refused with the file named.
        var good = CreateArchive(new DateOnly(2026, 10, 30));
        var damaged = good + ".bad";
        try
        {
            check(ElcomturArchive.Verify(good) >= 6, "Elcomtur automatic: every file of a good archive is read and its CRC matches");
            var bytes = File.ReadAllBytes(good);
            var at = 30 + "firma1/clienti.dbf".Length + 3;
            bytes[at] ^= 0xFF;
            File.WriteAllBytes(damaged, bytes);
            string? message = null;
            try { ElcomturArchive.Verify(damaged); } catch (ElcomturOperationException exception) { message = exception.Message; }
            check(message is not null && message.Contains("corupt") && message.Contains("firma1/clienti.dbf"), "Elcomtur automatic: an archive with a damaged byte is refused, naming the damaged file");
            var choices = ElcomturArchive.FirmChoices(good);
            check(choices.Count == 1 && choices[0].Folder == "firma1" && choices[0].Name == "ELECTRIC TEST SRL" && choices[0].Label == "firma1 — ELECTRIC TEST SRL" && new ElcomturFirmChoice("firma2", "").Label == "firma2",
                "Elcomtur automatic: the firms of a backup are told with their names (the FIRMA table), the folder alone when the name is missing");
        }
        finally { File.Delete(good); File.Delete(damaged); }

        // Only the last two imported backups stay on the server.
        var folder = Path.Combine(Path.GetTempPath(), "elcomtur-retention-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            for (var index = 1; index <= 4; index++) { var file = Path.Combine(folder, $"b{index}.elc"); File.WriteAllText(file, "x"); File.SetLastWriteTimeUtc(file, new DateTime(2026, 11, index, 4, 0, 0, DateTimeKind.Utc)); }
            File.WriteAllText(Path.Combine(folder, ".tmp-run.elc"), "x"); File.SetLastWriteTimeUtc(Path.Combine(folder, ".tmp-run.elc"), new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            ElcomturAutoService.Retention(folder);
            check(Directory.GetFiles(folder).Select(Path.GetFileName).Order().SequenceEqual([".tmp-run.elc", "b3.elc", "b4.elc"]), "Elcomtur automatic: the last two imported backups stay on the server, a temporary file of a running import is not touched");
        }
        finally { Directory.Delete(folder, true); }

        // The notification instances.
        var now = new DateOnly(2026, 11, 5);
        ElcomturAutoSettings Settings(bool enabled, DateTime? imported, DateTime? failing = null) => ElcomturAutoSettings.Empty with
        {
            Enabled = enabled, Path = @"\\nas\bk", Mask = "*elcomtur*", FirmFolder = "firma1", FirmName = "Electric", AlertDays = 3, LastImportUtc = imported, FailingSinceUtc = failing, LastCheckUtc = failing, LastMessage = "Fișier corupt",
            LastFileName = "b.elc", LastSummary = "10 beneficiari", UpdatedUtc = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc)
        };
        var old = ElcomturAutoAlertSource.Instances(new ElcomturAutoAlertState(Settings(true, new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc)), null), now);
        check(old.Count == 1 && old[0].ObjectId == ElcomturAutoAlertSource.MissingId && old[0].Expiry == new DateOnly(2026, 11, 4) && old[0].Values[ElcomturAutoAlertSource.DetailsName].Contains("de 3 zile"),
            "Elcomtur automatic: no new backup for the threshold days gives the \"backup lipsă\" notification, dated the day the threshold passed");
        var manual = ElcomturAutoAlertSource.Instances(new ElcomturAutoAlertState(Settings(true, new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc)), new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc)), now);
        check(manual.Single(item => item.ObjectId == ElcomturAutoAlertSource.MissingId).Expiry == new DateOnly(2026, 11, 7), "Elcomtur automatic: a manual import also moves the date of the missing backup");
        var fresh = ElcomturAutoAlertSource.Instances(new ElcomturAutoAlertState(Settings(true, new DateTime(2026, 11, 5, 4, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 5, 4, 1, 0, DateTimeKind.Utc)), null), now);
        check(fresh.Select(item => item.ObjectId).Order().SequenceEqual([1, 2, 3]) && fresh.Single(item => item.ObjectId == ElcomturAutoAlertSource.FailedId).Values[ElcomturAutoAlertSource.DetailsName].Contains("Fișier corupt"),
            "Elcomtur automatic: a failed check and a taken backup are their own notifications, with the cause and the result");
        var off = ElcomturAutoAlertSource.Instances(new ElcomturAutoAlertState(Settings(false, new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 5, 4, 1, 0, DateTimeKind.Utc)), null), now);
        var past = ElcomturAutoAlertSource.Instances(new ElcomturAutoAlertState(Settings(true, new DateTime(2026, 11, 5, 4, 0, 0, DateTimeKind.Utc).AddDays(-4)), null), now);
        check(off.Select(item => item.ObjectId).SequenceEqual([ElcomturAutoAlertSource.DoneId]) && !past.Any(item => item.ObjectId == ElcomturAutoAlertSource.DoneId),
            "Elcomtur automatic: with the import off only the information about a taken backup remains, and that one closes after a few days");
    }

    private sealed class FakeAuto : IElcomturAutoService
    {
        public ElcomturAutoSettings Current = ElcomturAutoSettings.Empty;
        public ElcomturAutoInput? LastSaved;
        public readonly List<bool> Checks = [];
        public Task<ElcomturAutoSettings> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(Current);
        public Task SaveAsync(ElcomturAutoInput input, CancellationToken cancellationToken = default)
        {
            LastSaved = input;
            Current = Current with { Enabled = input.Enabled, Path = input.Path, Mask = input.Mask, FirmFolder = input.FirmFolder, FirmName = input.FirmName, AlertDays = input.AlertDays, ScheduledHour = input.ScheduledHour, UserName = input.UserName, HasPassword = input.Password is not null || Current.HasPassword };
            return Task.CompletedTask;
        }
        public Task<ElcomturAutoResult> TestAsync(ElcomturAutoInput input, CancellationToken cancellationToken = default) => Task.FromResult(new ElcomturAutoResult(true, "Conexiunea funcționează."));
        public Task<ElcomturAutoFirms> ReadFirmsAsync(ElcomturAutoInput input, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ElcomturAutoFirms(true, "Firmele din b.elc.", [new("firma1", "ELECTRIC STANDARD PREST SRL"), new("firma3", "KS DIGITAL ENERGY SRL")], "b.elc"));
        public Task<ElcomturAutoResult> CheckNowAsync(bool force, CancellationToken cancellationToken = default)
        {
            Checks.Add(force);
            Current = Current with { LastCheckUtc = DateTime.UtcNow, LastCheckOk = true, LastMessage = "Backup preluat.", LastFileName = "b.elc", LastImportUtc = DateTime.UtcNow, LastSummary = "3 beneficiari", LastDataDate = new DateOnly(2026, 10, 30) };
            return Task.FromResult(new ElcomturAutoResult(true, "Backup preluat.", true));
        }
        public Task<ElcomturAutoResult?> RunScheduledAsync(DateTime localNow, CancellationToken cancellationToken = default) => Task.FromResult<ElcomturAutoResult?>(null);
    }

    private sealed class FakeAutoAlerts(FakeAuto auto) : IElcomturAutoAlertReader
    {
        public Task<ElcomturAutoAlertState> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(new ElcomturAutoAlertState(auto.Current, null));
    }

    // Setari -> Preluare Elcomtur: the fields, the firms with their names, the save, the manual check.
    private static void AutoPanelChecks(Action<bool, string> check)
    {
        var auto = new FakeAuto();
        using var context = new Bunit.BunitContext();
        context.JSInterop.Mode = Bunit.JSRuntimeMode.Loose;
        context.Services.AddLogging();
        context.Services.AddSingleton<IElcomturAutoService>(auto);
        context.Services.AddSingleton<IElcomturAutoAlertReader>(new FakeAutoAlerts(auto));
        var cut = context.Render<BlazorStoc.Components.Pages.ElcomturAutoPanel>();
        check(cut.FindAll("#elcomtur-auto-path").Count == 1 && cut.FindAll("#elcomtur-auto-user").Count == 1 && cut.FindAll("#elcomtur-auto-password").Count == 1 && cut.FindAll("#elcomtur-auto-mask").Count == 1
            && cut.FindAll("#elcomtur-auto-days").Count == 1 && cut.FindAll("input[role=switch]").Count == 2 && !cut.Find("input[role=switch]").HasAttribute("checked") && cut.Find("#elcomtur-auto-status").TextContent.Contains("Nicio verificare"),
            "Elcomtur automatic panel: the directory, account, password, mask, firm and threshold fields and the On/Off switch are shown (off at first), with no check yet");
        cut.Find("#elcomtur-auto-path").Change(@"\\nas\backup\elcomtur");
        cut.Find("#elcomtur-auto-user").Change("elcomtur");
        cut.Find("#elcomtur-auto-password").Change("secret-1");
        cut.Find("#elcomtur-auto-mask").Change("*elcomtur*");
        cut.Find("#elcomtur-auto-firms").Click();
        cut.WaitForAssertion(() => { if (cut.FindAll("#elcomtur-auto-firm option").Count != 2) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
        var options = cut.FindAll("#elcomtur-auto-firm option").Select(option => option.TextContent.Trim()).ToList();
        check(options.SequenceEqual(["firma1 — ELECTRIC STANDARD PREST SRL", "firma3 — KS DIGITAL ENERGY SRL"]) && cut.Find("#elcomtur-auto-notice").TextContent.Contains("Firmele din b.elc"),
            "Elcomtur automatic panel: the firms read from the newest backup are listed with their names, not only as firma1, firma3");
        cut.Find("#elcomtur-auto-firm").Change("firma3");
        cut.Find("input[role=switch]").Change(true);
        cut.Find("#elcomtur-auto-days").Change("5");
        check(cut.FindAll("#elcomtur-auto-hour option").Count == 24 && cut.Find("#elcomtur-auto-hour").GetAttribute("value") is null or "4" or "", "Elcomtur automatic panel: the hour of the daily check is chosen from the 24 hours of the day, 04:00 by default");
        cut.Find("#elcomtur-auto-hour").Change("7");
        cut.Find("#elcomtur-auto-save").Click();
        cut.WaitForAssertion(() => { if (auto.LastSaved is null) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
        check(auto.LastSaved is { Enabled: true, FirmFolder: "firma3", FirmName: "KS DIGITAL ENERGY SRL", Mask: "*elcomtur*", AlertDays: 5, ScheduledHour: 7, UserName: "elcomtur", Password: "secret-1" } && auto.LastSaved.Path == @"\\nas\backup\elcomtur",
            "Elcomtur automatic panel: the save sends the switch, directory, account, password, mask, the chosen firm with its name, the threshold and the hour");
        cut.WaitForAssertion(() => { if (!cut.Find("#elcomtur-auto-password").GetAttribute("placeholder")!.Contains("salvată")) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
        check(cut.Find("#elcomtur-auto-password").GetAttribute("value") is null or "" && cut.Find("#elcomtur-auto-notice").TextContent.Contains("salvată"), "Elcomtur automatic panel: after the save the password is not shown again (only that one is saved)");
        cut.Find("#elcomtur-auto-check").Click();
        cut.WaitForAssertion(() => { if (auto.Checks.Count != 1) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
        cut.Find("#elcomtur-auto-force").Click();
        cut.WaitForAssertion(() => { if (auto.Checks.Count != 2) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
        check(auto.Checks.SequenceEqual([false, true]) && cut.Find("#elcomtur-auto-status").TextContent.Contains("reușită") && cut.Find("#elcomtur-auto-last-import").TextContent.Contains("b.elc") && cut.Find("#elcomtur-auto-last-import").TextContent.Contains("firma3 — KS DIGITAL ENERGY SRL"),
            "Elcomtur automatic panel: \"Verifică acum\" and \"Preia ultimul backup acum\" run the check (the second forced) and show the result and the last backup taken");
    }
}
