using System.IO.Compression;

namespace BlazorStoc.Modules.Elcomtur;

/// <summary>A firm of the archive: its folder (firma1, ...) and its name from the Elcomtur program (empty when the archive does not say it).</summary>
public sealed record ElcomturFirmChoice(string Folder, string Name)
{
    public string Label => Name.Length > 0 ? $"{Folder} — {Name}" : Folder;
}

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

    /// <summary>The firms of the archive with their names (the DEN field of the FIRMA.DBF table of the folder), so a firm is told by name and not only as firma1, firma2.</summary>
    public static IReadOnlyList<ElcomturFirmChoice> FirmChoices(string path)
    {
        using var zip = Open(path);
        var folders = zip.Entries.Select(entry => Split(entry.FullName)).Where(part => System.Text.RegularExpressions.Regex.IsMatch(part.Folder, "^firma[0-9]+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant))
            .GroupBy(part => part.Folder, StringComparer.OrdinalIgnoreCase).Where(group => Tables.All(table => group.Any(part => string.Equals(part.Name, table, StringComparison.OrdinalIgnoreCase))))
            .Select(group => group.Key).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
        var result = new List<ElcomturFirmChoice>(folders.Count);
        foreach (var folder in folders)
        {
            var name = "";
            try
            {
                var entry = zip.Entries.FirstOrDefault(item => Split(item.FullName) is var part && string.Equals(part.Folder, folder, StringComparison.OrdinalIgnoreCase) && string.Equals(part.Name, "firma.dbf", StringComparison.OrdinalIgnoreCase));
                if (entry is not null)
                {
                    using var stream = entry.Open();
                    using var memory = new MemoryStream();
                    stream.CopyTo(memory);
                    name = DbfReader.Text_(DbfReader.Read(memory.ToArray()).Rows.FirstOrDefault() ?? new Dictionary<string, string>(), "DEN").Trim();
                }
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException) { name = ""; }
            result.Add(new ElcomturFirmChoice(folder, name));
        }
        return result;
    }

    /// <summary>
    /// The integrity of the whole archive: every file inside is decompressed and its CRC-32 is compared with the one stored in the archive (the .NET reader does not check it), so a file
    /// damaged in the copy or on the disk is found before anything is imported. Throws <see cref="ElcomturOperationException"/> naming the damaged file.
    /// </summary>
    public static int Verify(string path, CancellationToken cancellationToken = default)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
        var stored = ReadStoredCrcs(file);
        ZipArchive zip;
        try { zip = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: true); }
        catch (Exception exception) when (exception is InvalidDataException or IOException) { throw new ElcomturOperationException("Fișierul nu este o arhivă Elcomtur (.elc) validă."); }
        using (zip)
        {
            var compare = stored is not null && stored.Count == zip.Entries.Count;
            var buffer = new byte[1 << 16];
            var checkedEntries = 0;
            for (var index = 0; index < zip.Entries.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = zip.Entries[index];
                if (entry.FullName.EndsWith('/')) continue;
                try
                {
                    using var stream = entry.Open();
                    var crc = 0xFFFFFFFFu;
                    int read;
                    while ((read = stream.Read(buffer, 0, buffer.Length)) > 0) crc = Crc32Update(crc, buffer, read);
                    if (compare && ~crc != stored![index]) throw new InvalidDataException("crc");
                    checkedEntries++;
                }
                catch (Exception exception) when (exception is InvalidDataException or IOException or NotSupportedException)
                {
                    throw new ElcomturOperationException($"Backup deteriorat: fișierul „{entry.FullName}” din arhivă este corupt (nu se poate citi sau suma de control nu coincide).");
                }
            }
            return checkedEntries;
        }
    }

    // The CRC-32 values of the central directory, in the order of the entries; null for a ZIP64 archive (the files are then still read to the end, only the sum is not compared).
    private static List<uint>? ReadStoredCrcs(FileStream file)
    {
        var tail = (int)Math.Min(file.Length, 65557);
        var data = new byte[tail];
        file.Seek(-tail, SeekOrigin.End);
        file.ReadExactly(data);
        var eocd = -1;
        for (var position = tail - 22; position >= 0; position--)
            if (data[position] == 0x50 && data[position + 1] == 0x4B && data[position + 2] == 5 && data[position + 3] == 6) { eocd = position; break; }
        if (eocd < 0) return null;
        int entries = BitConverter.ToUInt16(data, eocd + 10);
        var size = BitConverter.ToUInt32(data, eocd + 12);
        var offset = BitConverter.ToUInt32(data, eocd + 16);
        if (entries == 0xFFFF || size == 0xFFFFFFFF || offset == 0xFFFFFFFF || offset + (long)size > file.Length) return null;
        var directory = new byte[size];
        file.Seek(offset, SeekOrigin.Begin);
        file.ReadExactly(directory);
        var crcs = new List<uint>(entries);
        var at = 0;
        for (var index = 0; index < entries; index++)
        {
            if (at + 46 > directory.Length || BitConverter.ToUInt32(directory, at) != 0x02014B50) return null;
            crcs.Add(BitConverter.ToUInt32(directory, at + 16));
            at += 46 + BitConverter.ToUInt16(directory, at + 28) + BitConverter.ToUInt16(directory, at + 30) + BitConverter.ToUInt16(directory, at + 32);
        }
        return crcs;
    }

    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(value =>
    {
        var crc = (uint)value;
        for (var bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
        return crc;
    }).ToArray();

    private static uint Crc32Update(uint crc, byte[] buffer, int count)
    {
        for (var index = 0; index < count; index++) crc = CrcTable[(crc ^ buffer[index]) & 0xFF] ^ (crc >> 8);
        return crc;
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
