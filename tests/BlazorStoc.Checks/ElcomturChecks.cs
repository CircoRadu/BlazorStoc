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
        return Task.CompletedTask;
    }
}
