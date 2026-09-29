using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MySqlConnector;

namespace BlazorStoc.Services;

// Subtask 2.2 (Task 2): the canonical type-and-hash row comparison the TODO's "Decizie tehnica" says to reuse from
// the MariaDB migration was, per that migration's own notes, only an undocumented scratch console tool - nothing
// reusable exists in Services/ yet. This is that reusable version: every row of every table is turned into a
// canonical string (one "prefix:value" per column, prefix chosen from the SQL type - I/R/T/B/NULL), hashed with
// SHA-256, and rows are combined in primary-key order into one hash per table and one overall hash - never a diff
// on raw SQL text, which can differ in row/statement order without the data itself differing.
public sealed record CanonicalTableHash(string Table, int RowCount, string Hash);
public sealed record CanonicalSnapshot(string OverallHash, IReadOnlyList<CanonicalTableHash> Tables);

public static class CanonicalRowHasher
{
    public static async Task<CanonicalSnapshot> ComputeAsync(MySqlConnection connection, IReadOnlyList<string> tables,
        CancellationToken cancellationToken = default)
    {
        var results = new List<CanonicalTableHash>();
        foreach (var table in tables.OrderBy(name => name, StringComparer.Ordinal))
            results.Add(await HashTableAsync(connection, table, cancellationToken).ConfigureAwait(false));
        var overall = HashLines(results.Select(result => $"{result.Table}:{result.RowCount}:{result.Hash}"));
        return new CanonicalSnapshot(overall, results);
    }

    private static async Task<CanonicalTableHash> HashTableAsync(MySqlConnection connection, string table, CancellationToken token)
    {
        var columns = await GetColumnsAsync(connection, table, token).ConfigureAwait(false);
        if (columns.Count == 0) return new CanonicalTableHash(table, 0, HashLines([]));
        var keyColumns = await GetPrimaryKeyAsync(connection, table, token).ConfigureAwait(false);
        var orderByColumns = keyColumns.Count > 0 ? keyColumns : columns.Select(column => column.Name).ToList();
        var columnList = string.Join(',', columns.Select(column => Quote(column.Name)));
        var orderBy = string.Join(',', orderByColumns.Select(Quote));
        await using var command = new MySqlCommand($"SELECT {columnList} FROM {Quote(table)} ORDER BY {orderBy}", connection);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        var rowHashes = new List<string>();
        var values = new object?[columns.Count];
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            for (var i = 0; i < columns.Count; i++) values[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rowHashes.Add(HashRow(columns, values));
        }
        return new CanonicalTableHash(table, rowHashes.Count, HashLines(rowHashes));
    }

    // Pure and independently testable: given the already-read column metadata and row values (no database access),
    // this is the exact canonicalization the live-query path above feeds into HashLines.
    public static string HashRow(IReadOnlyList<CanonicalColumn> columns, IReadOnlyList<object?> values)
    {
        var parts = new string[columns.Count];
        for (var i = 0; i < columns.Count; i++) parts[i] = CanonicalizeValue(values[i], columns[i].Prefix);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\u001F', parts)))).ToLowerInvariant();
    }

    public static string CanonicalizeValue(object? value, char prefix)
    {
        if (value is null || value is DBNull) return "NULL";
        return prefix switch
        {
            'I' => $"I:{Convert.ToInt64(value, CultureInfo.InvariantCulture)}",
            'R' => $"R:{Convert.ToDecimal(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)}",
            'B' => $"B:{Convert.ToHexString(value as byte[] ?? Encoding.UTF8.GetBytes(value.ToString() ?? string.Empty))}",
            _ => $"T:{Convert.ToString(value, CultureInfo.InvariantCulture)}"
        };
    }

    public static char PrefixForDataType(string dataType) => dataType.ToLowerInvariant() switch
    {
        "tinyint" or "smallint" or "mediumint" or "int" or "bigint" or "year" => 'I',
        "decimal" or "numeric" or "float" or "double" => 'R',
        "binary" or "varbinary" or "tinyblob" or "blob" or "mediumblob" or "longblob" => 'B',
        _ => 'T'
    };

    private static string HashLines(IEnumerable<string> lines) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lines)))).ToLowerInvariant();

    private static async Task<List<CanonicalColumn>> GetColumnsAsync(MySqlConnection connection, string table, CancellationToken token)
    {
        await using var command = new MySqlCommand(
            "SELECT COLUMN_NAME,DATA_TYPE FROM information_schema.COLUMNS " +
            "WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME=@table ORDER BY ORDINAL_POSITION", connection);
        command.Parameters.AddWithValue("@table", table);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        var columns = new List<CanonicalColumn>();
        while (await reader.ReadAsync(token).ConfigureAwait(false))
            columns.Add(new CanonicalColumn(reader.GetString(0), PrefixForDataType(reader.GetString(1))));
        return columns;
    }

    private static async Task<List<string>> GetPrimaryKeyAsync(MySqlConnection connection, string table, CancellationToken token)
    {
        await using var command = new MySqlCommand(
            "SELECT k.COLUMN_NAME FROM information_schema.KEY_COLUMN_USAGE k " +
            "JOIN information_schema.TABLE_CONSTRAINTS c ON c.CONSTRAINT_NAME=k.CONSTRAINT_NAME AND c.TABLE_SCHEMA=k.TABLE_SCHEMA AND c.TABLE_NAME=k.TABLE_NAME " +
            "WHERE k.TABLE_SCHEMA=DATABASE() AND k.TABLE_NAME=@table AND c.CONSTRAINT_TYPE='PRIMARY KEY' ORDER BY k.ORDINAL_POSITION", connection);
        command.Parameters.AddWithValue("@table", table);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        var columns = new List<string>();
        while (await reader.ReadAsync(token).ConfigureAwait(false)) columns.Add(reader.GetString(0));
        return columns;
    }

    // Identifiers here always come from information_schema (never from user input), so backtick-quoting is purely
    // defensive syntax, not an injection boundary.
    private static string Quote(string identifier) => $"`{identifier.Replace("`", "``")}`";
}

public sealed record CanonicalColumn(string Name, char Prefix);
