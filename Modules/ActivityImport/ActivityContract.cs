using System.Security.Cryptography;
using System.Text;

namespace BlazorStoc.Modules.ActivityImport;

// What the import module expects of the ActivityApp database (SQL Server): the tables and columns it reads, the kind of value in each, and whether the import can do without it.
// ActivityApp is updated by its own vendor and its structure can change; the module compares the real structure (INFORMATION_SCHEMA.COLUMNS of these tables only) with this contract
// before every use. A missing optional column is left out of the SELECT (the field stays empty and the run is "partial"); a missing required column or a column of another kind
// stops only the entity that needs it, with the exact difference reported to the administrator. Nothing else of ActivityApp is looked at.

public enum ColumnKind { Number, Text, Date, Flag }

/// <summary>A column the import reads. Aliases are other names the same column is known by after a rename in ActivityApp.</summary>
public sealed record ContractColumn(string Name, ColumnKind Kind, bool Required = false, IReadOnlyList<string>? Aliases = null);

/// <summary>A table the import reads; Entity is what the import cannot do without its required columns (shown in the report).</summary>
public sealed record ContractTable(string Name, string Entity, IReadOnlyList<ContractColumn> Columns);

public static class ActivityContract
{
    public const string TicketTypes = "Tipuri de tichete";
    public const string Tickets = "Tichete";
    public const string Partners = "Beneficiari";
    public const string WorkPoints = "Puncte de lucru";
    public const string Events = "Evenimente de rezolvare";
    public const string Version = "Versiune ActivityApp";

    public static IReadOnlyList<ContractTable> Tables { get; } =
    [
        new("ActivitatiContracte", TicketTypes, [Num("ID", true), Txt("Denumire", true), Txt("TipActivitate"), Flag("SeAtasaeazaLaContractService"), Flag("GenerareEvenimentAutomat")]),
        new("Tickete", Tickets, [Num("ID", true), Num("IDActivitate", true), Flag("Sters", true), Num("IDEveniment", true), Dat("DataTicket", true), Num("IDClient", true), Num("IDPunctDeLucru"),
            Txt("Subject"), Txt("Description"), Txt("DescriptionInternal"), Txt("NumeTicket"), Txt("ProcesatDe")]),
        new("EvenimenteAgent", Events, [Num("ID", true), Num("IDTicket", true), Dat("EndTime"), Num("Status"), Txt("DescriptionInternal"), Dat("DataModificareEveniment")]),
        new("NomenclatorParteneri", Partners, [Num("ID", true), Txt("Denumire", true), Txt("CodFiscal", true), Txt("Adresa"), Txt("Localitate"), Txt("Judet"), Txt("Telefon"), Txt("Email")]),
        new("PuncteDeLucruParteneri", WorkPoints, [Num("ID", true), Num("IDPartener", true), Txt("Adresa"), Txt("Localitate"), Txt("Judet"), Txt("Telefon"), Txt("Obiectiv"), Txt("LocatieGPS"), Flag("Activ"), Flag("Sistat")]),
        new("Versiuni", Version, [Num("Id"), Txt("Desktop")])
    ];

    private static ContractColumn Num(string name, bool required = false) => new(name, ColumnKind.Number, required);
    private static ContractColumn Txt(string name, bool required = false) => new(name, ColumnKind.Text, required);
    private static ContractColumn Dat(string name, bool required = false) => new(name, ColumnKind.Date, required);
    private static ContractColumn Flag(string name, bool required = false) => new(name, ColumnKind.Flag, required);

    /// <summary>The kind of an SQL Server data type; null for a type the import does not know.</summary>
    public static ColumnKind? KindOf(string dataType) => dataType.Trim().ToLowerInvariant() switch
    {
        "bigint" or "int" or "smallint" or "tinyint" or "decimal" or "numeric" or "float" or "real" or "money" or "smallmoney" => ColumnKind.Number,
        "bit" => ColumnKind.Flag,
        "nvarchar" or "varchar" or "nchar" or "char" or "ntext" or "text" => ColumnKind.Text,
        "datetime" or "datetime2" or "smalldatetime" or "date" or "datetimeoffset" => ColumnKind.Date,
        _ => null
    };

    // A flag may be kept as a small number and the reverse; the other kinds must match.
    public static bool Compatible(ColumnKind expected, ColumnKind? found) =>
        found is { } kind && (kind == expected || (expected == ColumnKind.Flag && kind == ColumnKind.Number) || (expected == ColumnKind.Number && kind == ColumnKind.Flag));

    public static string KindText(ColumnKind kind) => kind switch { ColumnKind.Number => "număr", ColumnKind.Text => "text", ColumnKind.Date => "dată", _ => "da/nu" };
}

/// <summary>A column of ActivityApp as INFORMATION_SCHEMA reports it.</summary>
public sealed record ActivityColumn(string Table, string Column, string DataType);

public enum StructureStatus { Unchecked, Ok, Partial, Changed }

/// <summary>One difference between the contract and ActivityApp. Required: the entity cannot be imported while it lasts.</summary>
public sealed record StructureIssue(string Table, string Column, string Entity, string Problem, string Expected, string Found, bool Required);

public sealed class StructureReport
{
    private readonly Dictionary<string, string> usable;

    internal StructureReport(StructureStatus status, string fingerprint, IReadOnlyList<StructureIssue> issues, int newColumns, Dictionary<string, string> usable, IReadOnlySet<string> blockedEntities)
    {
        Status = status; Fingerprint = fingerprint; Issues = issues; NewColumns = newColumns; this.usable = usable; BlockedEntities = blockedEntities;
    }

    public StructureStatus Status { get; }
    /// <summary>SHA-256 of the contract tables' real columns and types: a change (also a new column) is noticed by comparing it with the last saved one.</summary>
    public string Fingerprint { get; }
    public IReadOnlyList<StructureIssue> Issues { get; }
    /// <summary>Columns found in the contract tables that the contract does not know (informative: ignored).</summary>
    public int NewColumns { get; }
    /// <summary>The entities whose required columns are missing or of another kind: not imported until the structure is known again.</summary>
    public IReadOnlySet<string> BlockedEntities { get; }

    /// <summary>The real name of a contract column in ActivityApp (also after a known rename), or null when the column cannot be used.</summary>
    public string? Physical(string table, string column) => usable.TryGetValue(Key(table, column), out var name) ? name : null;
    public bool IsBlocked(string entity) => BlockedEntities.Contains(entity);

    internal static string Key(string table, string column) => $"{table}.{column}".ToLowerInvariant();
}

public static class ActivityStructureChecker
{
    /// <summary>Compares the columns read from ActivityApp (only the contract tables) with the contract. Never throws; a table that is absent gives an issue per required column.</summary>
    public static StructureReport Check(IReadOnlyList<ActivityColumn> actual, IReadOnlyList<ContractTable>? contract = null, ActivityFieldMap? fields = null)
    {
        contract ??= ActivityContract.Tables;
        var byTable = actual.GroupBy(column => column.Table, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
        var issues = new List<StructureIssue>();
        var usable = new Dictionary<string, string>(StringComparer.Ordinal);
        var newColumns = 0;
        foreach (var table in contract)
        {
            var found = byTable.GetValueOrDefault(table.Name) ?? [];
            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var column in table.Columns)
            {
                var names = new[] { column.Name }.Concat(column.Aliases ?? []).ToList();
                var match = found.FirstOrDefault(item => names.Contains(item.Column, StringComparer.OrdinalIgnoreCase));
                if (match is null)
                {
                    issues.Add(new(table.Name, column.Name, table.Entity, found.Count == 0 ? "tabela lipsește" : "coloana lipsește", ActivityContract.KindText(column.Kind), "—", column.Required));
                    continue;
                }
                known.Add(match.Column);
                var kind = ActivityContract.KindOf(match.DataType);
                if (!ActivityContract.Compatible(column.Kind, kind))
                    issues.Add(new(table.Name, column.Name, table.Entity, "alt tip de date", ActivityContract.KindText(column.Kind), match.DataType, column.Required));
                else usable[StructureReport.Key(table.Name, column.Name)] = match.Column;
            }
            newColumns += found.Count(item => !known.Contains(item.Column));
        }
        // The columns the administrator pointed a field at: used instead of the contract column when they exist and hold text; otherwise the default stays and the difference is reported
        // (not required: the ticket is still read, from the default column).
        foreach (var field in fields is null ? Enumerable.Empty<ActivityMappableField>() : ActivityFieldMap.Fields.Where(item => !fields.IsDefault(item)))
        {
            var table = contract.FirstOrDefault(item => item.Name == field.Table);
            var chosen = fields.ColumnOf(field);
            var match = actual.FirstOrDefault(item => item.Table.Equals(field.Table, StringComparison.OrdinalIgnoreCase) && item.Column.Equals(chosen, StringComparison.OrdinalIgnoreCase));
            if (table is not null && match is not null && ActivityContract.KindOf(match.DataType) == ColumnKind.Text) usable[StructureReport.Key(field.Table, field.Column)] = match.Column;
            else if (table is not null)
                issues.Add(new(field.Table, field.Column, table.Entity, match is null ? "coloana aleasă pentru „" + field.Label + "” lipsește" : "coloana aleasă pentru „" + field.Label + "” nu are text",
                    "text", match?.DataType ?? "—", false));
        }
        var blocked = issues.Where(issue => issue.Required).Select(issue => issue.Entity).ToHashSet(StringComparer.Ordinal);
        var status = blocked.Count > 0 ? StructureStatus.Changed : issues.Count > 0 ? StructureStatus.Partial : StructureStatus.Ok;
        return new StructureReport(status, Fingerprint(actual, contract), issues, newColumns, usable, blocked);
    }

    // The columns of the contract tables only, in a fixed order: the same structure always gives the same value.
    public static string Fingerprint(IReadOnlyList<ActivityColumn> actual, IReadOnlyList<ContractTable>? contract = null)
    {
        var tables = new HashSet<string>((contract ?? ActivityContract.Tables).Select(table => table.Name), StringComparer.OrdinalIgnoreCase);
        var lines = actual.Where(column => tables.Contains(column.Table)).Select(column => $"{column.Table}.{column.Column}:{column.DataType}".ToLowerInvariant()).Order(StringComparer.Ordinal);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lines)))).ToLowerInvariant();
    }
}
