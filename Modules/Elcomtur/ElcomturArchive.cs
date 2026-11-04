using System.IO.Compression;

namespace BlazorStoc.Modules.Elcomtur;

/// <summary>What is read from an Elcomtur backup (.elc): one firm, with the date of the backup.</summary>
public sealed record ElcomturData(string Firm, DateOnly DataDate, IReadOnlyList<ElcomturClient> Clients, IReadOnlyList<ElcomturInvoice> Invoices, IReadOnlyList<ElcomturReceipt> Receipts);



// The backup of the Elcomtur program is a ZIP (about 340 MB) with the tables of every firm in a folder (firma1, firma2, ...). Only three tables of the chosen firm are read
// (clients, sales invoices, cash register); nothing else of the archive is opened or kept.
public static class ElcomturArchive
{
    private static readonly string[] Tables = ["clienti.dbf", "vinzari.dbf", "casa.dbf"];

    /// <summary>The firms of the archive: the folders firma1, firma2, ... that have the three tables (other folders, like the empty template "baze" or the copy "trimit", are not firms).</summary>
    public static IReadOnlyList<string> Firms(string path)
    {
        using var zip = Open(path);
        return [.. zip.Entries.Select(entry => Split(entry.FullName)).Where(part => System.Text.RegularExpressions.Regex.IsMatch(part.Folder, "^firma[0-9]+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant)).GroupBy(part => part.Folder, StringComparer.OrdinalIgnoreCase)
            .Where(group => Tables.All(table => group.Any(part => string.Equals(part.Name, table, StringComparison.OrdinalIgnoreCase)))).Select(group => group.Key).OrderBy(name => name, StringComparer.OrdinalIgnoreCase)];
    }

    public static ElcomturData Read(string path, string firm)
    {
        using var zip = Open(path);
        byte[] Load(string table)
        {
            var entry = zip.Entries.FirstOrDefault(item => Split(item.FullName) is var part && string.Equals(part.Folder, firm, StringComparison.OrdinalIgnoreCase) && string.Equals(part.Name, table, StringComparison.OrdinalIgnoreCase))
                ?? throw new ElcomturOperationException($"În arhivă lipsește tabelul {table} al firmei {firm}.");
            using var stream = entry.Open();
            using var memory = new MemoryStream(checked((int)Math.Min(entry.Length, int.MaxValue)));
            stream.CopyTo(memory);
            return memory.ToArray();
        }
        try
        {
            var clients = DbfReader.Read(Load("clienti.dbf")).Rows.Select(row => new ElcomturClient(
                (int)DbfReader.Number(row, "COD"), DbfReader.Text_(row, "DEN"), DbfReader.Text_(row, "CODFIS"), DbfReader.Number(row, "PJ_PF") == 1)).ToList();
            var sales = DbfReader.Read(Load("vinzari.dbf"));
            var invoices = new List<ElcomturInvoice>(sales.Rows.Count);
            foreach (var row in sales.Rows)
            {
                if (DbfReader.Date(row, "DATA") is not { } date) continue;
                invoices.Add(new ElcomturInvoice((int)DbfReader.Number(row, "COD_CLI"), DbfReader.Text_(row, "NR"), date, (int)DbfReader.Number(row, "TERMEN"),
                    DbfReader.Number(row, "TOT_FACT"), DbfReader.Number(row, "PLATIT"), DbfReader.Number(row, "REST_PL")));
            }
            // Receipts from clients: kind 1 of the cash register on a client account (4111, 4111.2, ...). Checked on the whole archive: with these the balance equals the rest marked on
            // the invoices for 96% of the clients (with the account 4111 alone only for 44%).
            var receipts = DbfReader.Read(Load("casa.dbf")).Rows
                .Where(row => DbfReader.Text_(row, "FEL") == "1" && DbfReader.Text_(row, "CONT").StartsWith("4111", StringComparison.Ordinal))
                .Select(row => new ElcomturReceipt((int)DbfReader.Number(row, "CLIFUR"), DbfReader.Number(row, "INCASARI"))).ToList();
            var dataDate = sales.LastUpdate ?? invoices.Select(invoice => invoice.Date).DefaultIfEmpty(DateOnly.FromDateTime(DateTime.Today)).Max();
            return new ElcomturData(firm, dataDate, clients, invoices, receipts);
        }
        catch (InvalidDataException exception) { throw new ElcomturOperationException(exception.Message); }
    }

    private static ZipArchive Open(string path)
    {
        try { return ZipFile.OpenRead(path); }
        catch (Exception exception) when (exception is InvalidDataException or IOException) { throw new ElcomturOperationException("Fișierul nu este o arhivă Elcomtur (.elc) validă."); }
    }

    private static (string Folder, string Name) Split(string fullName)
    {
        var parts = fullName.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 ? (parts[0], parts[1]) : ("", "");
    }
}
