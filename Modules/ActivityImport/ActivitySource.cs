using Microsoft.Data.SqlClient;

namespace BlazorStoc.Modules.ActivityImport;

// The read side of ActivityApp (SQL Server). Only SELECT statements with explicit column lists are sent (the column names of a statement come from the contract and from the
// metadata the server itself reported, quoted; every value is a parameter), on a short-lived connection that waits little (LOCK_TIMEOUT) and reads without taking shared locks
// (READ UNCOMMITTED), so the import cannot hold up the users of ActivityApp. A failure of the source (network, account, timeout) is an expected answer, not a fault of the module.

/// <summary>What the administrator typed (or the stored values) to reach ActivityApp. The password is never logged or journaled.</summary>
/// <summary>Integrated: Windows authentication instead of the SQL account (used by the checks against a local server; the account of the real import is a SQL login).</summary>
public sealed record ActivityConnection(string Server, int Port, string Database, string User, string Password, bool Integrated = false);

public sealed record ActivityPing(bool Ok, string Message, string? ServerVersion = null, bool WritableAccount = false);

/// <summary>A ticket type of ActivityApp (ActivitatiContracte); the counts are null when the columns they need are not there.</summary>
public sealed record ActivityTypeRow(long Id, string Name, string Category, bool? ContractService, bool? AutoEvent, int? Resolved, int? Open, int? Deleted);

/// <summary>A partner of ActivityApp (NomenclatorParteneri); a column the structure does not allow is read as empty text.</summary>
public sealed record ActivityPartner(long Id, string Name, string TaxId, string Address, string City, string County, string Phone, string Email);

/// <summary>A work point of a partner (PuncteDeLucruParteneri). Active and Closed (Sistat) are null when the columns are not there.</summary>
public sealed record ActivityWorkPointRow(long Id, long PartnerId, string Address, string City, string County, string Phone, string Objective, string Gps, bool? Active, bool? Closed);

/// <summary>
/// A resolved ticket: the ticket and the event that closed it (the event holds the date and the report of the work). Problem = the text of the ticket at its opening
/// (Tickete.DescriptionInternal), Report = the report of the event (EvenimenteAgent.DescriptionInternal, the same text followed by what was done).
/// </summary>
public sealed record ActivityTicket(long Id, long TypeId, long PartnerId, long WorkPointId, DateTime? TicketDate, string Number, string Title, string Problem, string Executor,
    long EventId, DateTime? EventEnd, long? EventStatus, string Report, DateTime? EventModified = null);

/// <summary>The source of ActivityApp is not reachable or refused the account: an answer for the administrator.</summary>
public sealed class ActivitySourceException(string message) : Exception(message), IExpectedAnswer;

public interface IActivitySource
{
    /// <summary>Opens a connection and asks (read only) whether the account could write; never throws.</summary>
    Task<ActivityPing> PingAsync(ActivityConnection connection, CancellationToken cancellationToken);
    /// <summary>The columns of the contract tables (INFORMATION_SCHEMA), one query. Throws <see cref="ActivitySourceException"/> when the source cannot be read.</summary>
    Task<IReadOnlyList<ActivityColumn>> ReadStructureAsync(ActivityConnection connection, CancellationToken cancellationToken);
    /// <summary>The ticket types with their ticket counts, built from the columns the structure report says are usable.</summary>
    Task<IReadOnlyList<ActivityTypeRow>> ReadTypesAsync(ActivityConnection connection, StructureReport structure, CancellationToken cancellationToken);
    /// <summary>Every partner, read in pages of <paramref name="pageSize"/> rows by ID with a short pause between pages. Throws when the structure does not allow it.</summary>
    Task<IReadOnlyList<ActivityPartner>> ReadPartnersAsync(ActivityConnection connection, StructureReport structure, int pageSize, CancellationToken cancellationToken);
    /// <summary>Every work point of every partner, read like the partners.</summary>
    Task<IReadOnlyList<ActivityWorkPointRow>> ReadWorkPointsAsync(ActivityConnection connection, StructureReport structure, int pageSize, CancellationToken cancellationToken);
    /// <summary>
    /// The resolved tickets (not deleted, with the event that closed them) of the given types opened on or after <paramref name="since"/>, in pages by ticket ID. The types are bound as
    /// parameters. Open tickets (no event) are not read.
    /// </summary>
    Task<IReadOnlyList<ActivityTicket>> ReadTicketsAsync(ActivityConnection connection, StructureReport structure, IReadOnlyList<long> typeIds, DateTime since, DateTime? modifiedSince, int pageSize, CancellationToken cancellationToken);
    /// <summary>
    /// Of the given IDs of one kind of row (<see cref="LivePartners"/>, <see cref="LiveWorkPoints"/>, <see cref="LiveTickets"/>), those that still exist in ActivityApp (a ticket also
    /// not deleted): used to signal what was deleted there after it was imported. The IDs are bound as parameters, in groups.
    /// </summary>
    Task<IReadOnlySet<long>> ReadLiveIdsAsync(ActivityConnection connection, StructureReport structure, string kind, IReadOnlyList<long> ids, CancellationToken cancellationToken);
}

public static class LiveKinds
{
    public const string Partners = "partner";
    public const string WorkPoints = "workpoint";
    public const string Tickets = "ticket";
}

public sealed class SqlActivitySource : IActivitySource
{
    internal const string ApplicationName = "BlazorStoc-Import";
    private const string Session = "SET LOCK_TIMEOUT 3000; SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;";

    internal static string ConnectionString(ActivityConnection connection) => new SqlConnectionStringBuilder
    {
        DataSource = connection.Port == 1433 ? connection.Server : $"{connection.Server},{connection.Port}",
        InitialCatalog = connection.Database,
        IntegratedSecurity = connection.Integrated,
        UserID = connection.Integrated ? "" : connection.User,
        Password = connection.Integrated ? "" : connection.Password,
        ApplicationName = ApplicationName,
        ConnectTimeout = 8,
        Encrypt = SqlConnectionEncryptOption.Optional,
        TrustServerCertificate = true,
        Pooling = false
    }.ConnectionString;

    private static async Task<SqlConnection> OpenAsync(ActivityConnection connection, CancellationToken cancellationToken)
    {
        var sql = new SqlConnection(ConnectionString(connection));
        try
        {
            await sql.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var session = new SqlCommand(Session, sql) { CommandTimeout = 10 };
            await session.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return sql;
        }
        catch (SqlException exception)
        {
            await sql.DisposeAsync().ConfigureAwait(false);
            throw new ActivitySourceException(Describe(exception));
        }
        catch
        {
            await sql.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>The cause in words; never the account, the password or the text of the server's message (it may carry them).</summary>
    internal static string Describe(SqlException exception) => exception.Number switch
    {
        18456 => "Autentificarea a fost refuzată: contul sau parola nu sunt corecte sau contul nu are acces la baza de date.",
        4060 or 916 => "Contul nu are acces la baza de date indicată sau baza nu există.",
        -2 or 258 => "ActivityApp nu a răspuns la timp. Reîncearcă mai târziu.",
        1222 => "ActivityApp are tabela blocată în acest moment; importul a renunțat fără a aștepta.",
        0 or -1 or 2 or 53 or 10060 or 10061 or 11001 => "Serverul ActivityApp nu poate fi contactat (verifică adresa, portul și rețeaua).",
        _ => $"Citirea din ActivityApp a eșuat (eroare SQL Server {exception.Number})."
    };

    public async Task<ActivityPing> PingAsync(ActivityConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            await using var sql = await OpenAsync(connection, cancellationToken).ConfigureAwait(false);
            // Read only: whether the account is able to write (it should not be). Roles are asked for by name; nothing is changed.
            await using var command = new SqlCommand("SELECT ISNULL(IS_SRVROLEMEMBER('sysadmin'),0), ISNULL(IS_MEMBER('db_owner'),0), ISNULL(IS_MEMBER('db_datawriter'),0)", sql) { CommandTimeout = 10 };
            var writable = false;
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    writable = Convert.ToInt32(reader.GetValue(0)) == 1 || Convert.ToInt32(reader.GetValue(1)) == 1 || Convert.ToInt32(reader.GetValue(2)) == 1;
            return new ActivityPing(true, "Conexiunea funcționează.", sql.ServerVersion, writable);
        }
        catch (ActivitySourceException exception) { return new ActivityPing(false, exception.Message); }
        catch (SqlException exception) { return new ActivityPing(false, Describe(exception)); }
        catch (Exception exception) when (exception is TimeoutException or InvalidOperationException) { return new ActivityPing(false, "ActivityApp nu a răspuns la timp. Reîncearcă mai târziu."); }
    }

    public async Task<IReadOnlyList<ActivityColumn>> ReadStructureAsync(ActivityConnection connection, CancellationToken cancellationToken)
    {
        await using var sql = await OpenAsync(connection, cancellationToken).ConfigureAwait(false);
        var tables = ActivityContract.Tables.Select(table => table.Name).ToList();
        var names = string.Join(", ", tables.Select((_, index) => $"@t{index}"));
        await using var command = new SqlCommand($"SELECT TABLE_NAME, COLUMN_NAME, DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME IN ({names}) ORDER BY TABLE_NAME, ORDINAL_POSITION", sql) { CommandTimeout = 15 };
        for (var index = 0; index < tables.Count; index++) command.Parameters.AddWithValue($"@t{index}", tables[index]);
        var result = new List<ActivityColumn>();
        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                result.Add(new ActivityColumn(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }
        catch (SqlException exception) { throw new ActivitySourceException(Describe(exception)); }
        return result;
    }

    public async Task<IReadOnlyList<ActivityTypeRow>> ReadTypesAsync(ActivityConnection connection, StructureReport structure, CancellationToken cancellationToken)
    {
        const string types = "ActivitatiContracte", tickets = "Tickete";
        var id = structure.Physical(types, "ID");
        var name = structure.Physical(types, "Denumire");
        if (id is null || name is null) throw new ActivitySourceException("Lista tipurilor de tichete nu poate fi citită: structura ActivityApp nu mai corespunde (vezi raportul de structură).");
        var category = structure.Physical(types, "TipActivitate");
        var contract = structure.Physical(types, "SeAtasaeazaLaContractService");
        var auto = structure.Physical(types, "GenerareEvenimentAutomat");
        await using var sql = await OpenAsync(connection, cancellationToken).ConfigureAwait(false);
        var rows = new List<ActivityTypeRow>();
        var counts = new Dictionary<long, (int Resolved, int Open, int Deleted)>();
        var countsRead = false;
        try
        {
            var select = $"SELECT {Q(id)}, {Q(name)}, {(category is null ? "CAST(NULL AS nvarchar(100))" : Q(category))}, {(contract is null ? "CAST(NULL AS bit)" : Q(contract))}, {(auto is null ? "CAST(NULL AS bit)" : Q(auto))} FROM {Q(types)} ORDER BY {Q(name)}";
            await using (var command = new SqlCommand(select, sql) { CommandTimeout = 15 })
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    rows.Add(new ActivityTypeRow(Convert.ToInt64(reader.GetValue(0)), reader.IsDBNull(1) ? "" : reader.GetString(1).Trim(), reader.IsDBNull(2) ? "" : reader.GetValue(2).ToString()!.Trim(),
                        reader.IsDBNull(3) ? null : Convert.ToBoolean(reader.GetValue(3)), reader.IsDBNull(4) ? null : Convert.ToBoolean(reader.GetValue(4)), null, null, null));

            var ticketType = structure.Physical(tickets, "IDActivitate");
            var deleted = structure.Physical(tickets, "Sters");
            var closing = structure.Physical(tickets, "IDEveniment");
            if (ticketType is not null && deleted is not null && closing is not null)
            {
                var gone = $"ISNULL({Q(deleted)},0)=1";
                var done = $"ISNULL({Q(closing)},-1)>0";
                var query = $"SELECT {Q(ticketType)}, SUM(CASE WHEN {gone} THEN 1 ELSE 0 END), SUM(CASE WHEN NOT ({gone}) AND {done} THEN 1 ELSE 0 END), SUM(CASE WHEN NOT ({gone}) AND NOT ({done}) THEN 1 ELSE 0 END) FROM {Q(tickets)} GROUP BY {Q(ticketType)}";
                await using var command = new SqlCommand(query, sql) { CommandTimeout = 20 };
                await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    if (!reader.IsDBNull(0)) counts[Convert.ToInt64(reader.GetValue(0))] = (Convert.ToInt32(reader.GetValue(2)), Convert.ToInt32(reader.GetValue(3)), Convert.ToInt32(reader.GetValue(1)));
                countsRead = true;
            }
        }
        catch (SqlException exception) { throw new ActivitySourceException(Describe(exception)); }
        return [.. rows.Select(row => counts.TryGetValue(row.Id, out var count) ? row with { Resolved = count.Resolved, Open = count.Open, Deleted = count.Deleted } : countsRead ? row with { Resolved = 0, Open = 0, Deleted = 0 } : row)];
    }

    private static readonly TimeSpan PagePause = TimeSpan.FromMilliseconds(150);

    public async Task<IReadOnlyList<ActivityPartner>> ReadPartnersAsync(ActivityConnection connection, StructureReport structure, int pageSize, CancellationToken cancellationToken)
    {
        const string table = "NomenclatorParteneri";
        var id = Required(structure, table, "ID", ActivityContract.Partners);
        string Text(string column) => structure.Physical(table, column) is { } physical ? Q(physical) : "CAST(NULL AS nvarchar(300))";
        var select = $"{Q(id)}, {Text("Denumire")}, {Text("CodFiscal")}, {Text("Adresa")}, {Text("Localitate")}, {Text("Judet")}, {Text("Telefon")}, {Text("Email")}";
        return await ReadPagesAsync(connection, Q(table), Q(id), select, pageSize,
            reader => new ActivityPartner(Convert.ToInt64(reader.GetValue(0)), Str(reader, 1), Str(reader, 2), Str(reader, 3), Str(reader, 4), Str(reader, 5), Str(reader, 6), Str(reader, 7)),
            partner => partner.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ActivityWorkPointRow>> ReadWorkPointsAsync(ActivityConnection connection, StructureReport structure, int pageSize, CancellationToken cancellationToken)
    {
        const string table = "PuncteDeLucruParteneri";
        var id = Required(structure, table, "ID", ActivityContract.WorkPoints);
        var partner = Required(structure, table, "IDPartener", ActivityContract.WorkPoints);
        string Text(string column) => structure.Physical(table, column) is { } physical ? Q(physical) : "CAST(NULL AS nvarchar(300))";
        string Flag(string column) => structure.Physical(table, column) is { } physical ? Q(physical) : "CAST(NULL AS bit)";
        var select = $"{Q(id)}, {Q(partner)}, {Text("Adresa")}, {Text("Localitate")}, {Text("Judet")}, {Text("Telefon")}, {Text("Obiectiv")}, {Text("LocatieGPS")}, {Flag("Activ")}, {Flag("Sistat")}";
        return await ReadPagesAsync(connection, Q(table), Q(id), select, pageSize,
            reader => new ActivityWorkPointRow(Convert.ToInt64(reader.GetValue(0)), reader.IsDBNull(1) ? 0 : Convert.ToInt64(reader.GetValue(1)), Str(reader, 2), Str(reader, 3), Str(reader, 4), Str(reader, 5),
                Str(reader, 6), Str(reader, 7), reader.IsDBNull(8) ? null : Convert.ToBoolean(reader.GetValue(8)), reader.IsDBNull(9) ? null : Convert.ToBoolean(reader.GetValue(9))),
            point => point.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ActivityTicket>> ReadTicketsAsync(ActivityConnection connection, StructureReport structure, IReadOnlyList<long> typeIds, DateTime since, DateTime? modifiedSince, int pageSize, CancellationToken cancellationToken)
    {
        if (typeIds.Count == 0) return [];
        const string tickets = "Tickete", events = "EvenimenteAgent";
        foreach (var entity in new[] { ActivityContract.Tickets, ActivityContract.Events })
            if (structure.IsBlocked(entity)) throw new ActivitySourceException($"„{entity}” nu poate fi citit: structura ActivityApp nu mai corespunde (vezi raportul de structură).");
        string T(string column) => structure.Physical(tickets, column) is { } physical ? "t." + Q(physical) : throw new ActivitySourceException("Tichetele nu pot fi citite: structura ActivityApp nu mai corespunde.");
        string TText(string column) => structure.Physical(tickets, column) is { } physical ? "t." + Q(physical) : "CAST(NULL AS nvarchar(300))";
        string E(string column) => structure.Physical(events, column) is { } physical ? "e." + Q(physical) : throw new ActivitySourceException("Evenimentele nu pot fi citite: structura ActivityApp nu mai corespunde.");
        string EOptional(string column, string empty) => structure.Physical(events, column) is { } physical ? "e." + Q(physical) : empty;
        var id = T("ID");
        var select = string.Join(", ", id, T("IDActivitate"), T("IDClient"), structure.Physical(tickets, "IDPunctDeLucru") is { } point ? "t." + Q(point) : "CAST(NULL AS bigint)", T("DataTicket"),
            TText("NumeTicket"), TText("Subject"), TText("DescriptionInternal"), TText("ProcesatDe"), E("ID"), EOptional("EndTime", "CAST(NULL AS datetime)"), EOptional("Status", "CAST(NULL AS bigint)"), EOptional("DescriptionInternal", "CAST(NULL AS nvarchar(max))"), EOptional("DataModificareEveniment", "CAST(NULL AS datetime)"));
        var from = $"{Q(tickets)} t INNER JOIN {Q(events)} e ON {E("ID")} = {T("IDEveniment")} AND {E("IDTicket")} = {id}";
        var types = string.Join(", ", typeIds.Select((_, index) => $"@type{index}"));
        var deleted = structure.Physical(tickets, "Sters") is { } flag ? $" AND ISNULL(t.{Q(flag)}, 0) = 0" : "";
        var modified = modifiedSince is not null && structure.Physical(events, "DataModificareEveniment") is { } modifiedColumn ? $" AND (e.{Q(modifiedColumn)} >= @modified OR e.{Q(modifiedColumn)} IS NULL)" : "";
        var where = $"{deleted} AND {T("IDActivitate")} IN ({types}) AND {T("DataTicket")} >= @since{modified}";
        return await ReadPagesAsync(connection, from, id, select, pageSize,
            reader => new ActivityTicket(Convert.ToInt64(reader.GetValue(0)), Convert.ToInt64(reader.GetValue(1)), reader.IsDBNull(2) ? 0 : Convert.ToInt64(reader.GetValue(2)), reader.IsDBNull(3) ? 0 : Convert.ToInt64(reader.GetValue(3)),
                reader.IsDBNull(4) ? null : reader.GetDateTime(4), Str(reader, 5), Str(reader, 6), Str(reader, 7), Str(reader, 8), Convert.ToInt64(reader.GetValue(9)), reader.IsDBNull(10) ? null : reader.GetDateTime(10),
                reader.IsDBNull(11) ? null : Convert.ToInt64(reader.GetValue(11)), reader.IsDBNull(12) ? "" : reader.GetString(12).Trim(), reader.IsDBNull(13) ? null : reader.GetDateTime(13)),
            ticket => ticket.Id, cancellationToken, where, command =>
            {
                for (var index = 0; index < typeIds.Count; index++) command.Parameters.AddWithValue($"@type{index}", typeIds[index]);
                command.Parameters.AddWithValue("@since", since);
                if (modifiedSince is not null) command.Parameters.AddWithValue("@modified", modifiedSince.Value);
            }).ConfigureAwait(false);
    }

    public async Task<IReadOnlySet<long>> ReadLiveIdsAsync(ActivityConnection connection, StructureReport structure, string kind, IReadOnlyList<long> ids, CancellationToken cancellationToken)
    {
        var (table, entity) = kind switch
        {
            LiveKinds.Partners => ("NomenclatorParteneri", ActivityContract.Partners),
            LiveKinds.WorkPoints => ("PuncteDeLucruParteneri", ActivityContract.WorkPoints),
            LiveKinds.Tickets => ("Tickete", ActivityContract.Tickets),
            _ => throw new ArgumentException("Unknown kind of row.", nameof(kind))
        };
        var id = Required(structure, table, "ID", entity);
        var deleted = kind == LiveKinds.Tickets && structure.Physical(table, "Sters") is { } flag ? $" AND ISNULL({Q(flag)}, 0) = 0" : "";
        var live = new HashSet<long>();
        if (ids.Count == 0) return live;
        await using var sql = await OpenAsync(connection, cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var group in ids.Chunk(500))
            {
                var names = string.Join(", ", group.Select((_, index) => $"@id{index}"));
                await using var command = new SqlCommand($"SELECT {Q(id)} FROM {Q(table)} WHERE {Q(id)} IN ({names}){deleted}", sql) { CommandTimeout = 30 };
                for (var index = 0; index < group.Length; index++) command.Parameters.AddWithValue($"@id{index}", group[index]);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) live.Add(Convert.ToInt64(reader.GetValue(0)));
                if (group.Length == 500) await Task.Delay(PagePause, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (SqlException exception) { throw new ActivitySourceException(Describe(exception)); }
        return live;
    }

    private static string Required(StructureReport structure, string table, string column, string entity) =>
        structure.IsBlocked(entity) || structure.Physical(table, column) is not { } physical
            ? throw new ActivitySourceException($"„{entity}” nu poate fi citit: structura ActivityApp nu mai corespunde (vezi raportul de structură).")
            : physical;

    private static string Str(SqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? "" : (reader.GetValue(ordinal).ToString() ?? "").Trim();

    // Pages by the key (ID > last), a small pause between pages: the load on ActivityApp stays low however many rows there are.
    private static async Task<IReadOnlyList<T>> ReadPagesAsync<T>(ActivityConnection connection, string table, string key, string select, int pageSize, Func<SqlDataReader, T> read,
        Func<T, long> keyOf, CancellationToken cancellationToken, string extraWhere = "", Action<SqlCommand>? bind = null)
    {
        await using var sql = await OpenAsync(connection, cancellationToken).ConfigureAwait(false);
        var result = new List<T>();
        var last = long.MinValue;
        try
        {
            while (true)
            {
                var page = 0;
                await using (var command = new SqlCommand($"SELECT TOP ({Math.Clamp(pageSize, 50, 1000)}) {select} FROM {table} WHERE {key} > @last{extraWhere} ORDER BY {key}", sql) { CommandTimeout = 30 })
                {
                    command.Parameters.AddWithValue("@last", last);
                    bind?.Invoke(command);
                    await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        var item = read(reader);
                        result.Add(item);
                        last = keyOf(item);
                        page++;
                    }
                }
                if (page < Math.Clamp(pageSize, 50, 1000)) return result;
                await Task.Delay(PagePause, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (SqlException exception) { throw new ActivitySourceException(Describe(exception)); }
    }

    // A name that the server reported, as a quoted identifier.
    internal static string Q(string identifier) => "[" + identifier.Replace("]", "]]", StringComparison.Ordinal) + "]";
}
