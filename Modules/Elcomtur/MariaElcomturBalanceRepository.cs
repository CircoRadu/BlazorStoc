using MySqlConnector;
using static BlazorStoc.Services.MariaDb;
using BlazorStoc.Services;

namespace BlazorStoc.Modules.Elcomtur;

// MariaDB: `elcomtur_balances` and `elcomtur_invoices` are created by migration 62. Derived data, replaced as a whole by an import; like the other repositories this one never alters the schema.
public sealed class MariaElcomturBalanceRepository(IConfiguration configuration, IAccessControl? accessControl = null) : IElcomturBalanceRepository
{
    private const string ViewKey = "elcomtur.view";

    public async Task<BeneficiaryBalance?> GetAsync(int beneficiaryId, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAsync(ViewKey, cancellationToken).ConfigureAwait(false);
        return (await ReadAsync("WHERE b.beneficiary_id=@id", [("@id", beneficiaryId)], cancellationToken).ConfigureAwait(false)).FirstOrDefault();
    }

    public async Task<IReadOnlyList<BeneficiaryBalance>> GetOwedAsync(CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAsync(ViewKey, cancellationToken).ConfigureAwait(false);
        return [.. (await ReadAsync("WHERE b.balance > 0", [], cancellationToken).ConfigureAwait(false)).OrderByDescending(item => item.Balance).ThenBy(item => item.BeneficiaryName, StringComparer.CurrentCultureIgnoreCase)];
    }

    public async Task<int> CountOwedAsync(CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAsync(ViewKey, cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, "SELECT COUNT(*) FROM elcomtur_balances WHERE balance > 0");
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
    }

    public async Task<ElcomturImportInfo?> GetInfoAsync(CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAsync(ViewKey, cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, "SELECT data_date, firm, imported_utc, imported_by, COUNT(*) FROM elcomtur_balances GROUP BY data_date, firm, imported_utc, imported_by ORDER BY imported_utc DESC LIMIT 1");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        return new ElcomturImportInfo(DateOnly.FromDateTime(reader.GetDateTime(0)), reader.GetString(1), MariaTimeText.Parse(reader.GetString(2)), reader.GetString(3), (int)reader.GetInt64(4));
    }

    public async Task<IReadOnlyList<ElcomturBalancePoint>> GetHistoryAsync(DateOnly from, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAsync(ViewKey, cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, "SELECT data_date, total, owed_count, imported_utc FROM elcomtur_balance_history WHERE data_date >= @from ORDER BY data_date, id", ("@from", from.ToString("yyyy-MM-dd")));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<ElcomturBalancePoint>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new ElcomturBalancePoint(DateOnly.FromDateTime(reader.GetDateTime(0)), reader.GetDecimal(1), reader.GetInt32(2), MariaTimeText.Parse(reader.GetString(3))));
        return result;
    }

    public async Task ReplaceAsync(string firm, DateOnly dataDate, IReadOnlyList<BeneficiaryBalance> balances, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAsync("elcomtur.import", cancellationToken).ConfigureAwait(false);
        var actor = accessControl is null ? "sistem" : (await RepositoryAudit.ActorAsync(accessControl, cancellationToken).ConfigureAwait(false)).Username;
        await WriteAsync(configuration, async (connection, transaction) =>
        {
            // PROP-0036: the balances that are about to be replaced are noted once in the history when it is still empty (the first import after the history was introduced), then the total of the new import is noted.
            await using (var seed = Command(connection, transaction, """
                INSERT INTO elcomtur_balance_history(data_date,firm,total,owed_count,imported_utc,imported_by)
                SELECT MAX(data_date), MAX(firm), COALESCE(SUM(CASE WHEN balance > 0.004 THEN balance ELSE 0 END),0), COALESCE(SUM(balance > 0.004),0), MAX(imported_utc), MAX(imported_by)
                FROM elcomtur_balances HAVING COUNT(*) > 0 AND (SELECT COUNT(*) FROM elcomtur_balance_history) = 0
                """)) await seed.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await using (var same = Command(connection, transaction, "DELETE FROM elcomtur_balance_history WHERE data_date=@date AND firm=@firm", ("@date", dataDate.ToString("yyyy-MM-dd")), ("@firm", firm))) await same.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await using (var note = Command(connection, transaction, """
                INSERT INTO elcomtur_balance_history(data_date,firm,total,owed_count,imported_utc,imported_by) VALUES(@date,@firm,@total,@count,@now,@by)
                """, ("@date", dataDate.ToString("yyyy-MM-dd")), ("@firm", firm), ("@total", balances.Where(item => item.IsOwed).Sum(item => item.Balance)), ("@count", balances.Count(item => item.IsOwed)),
                ("@now", MariaTimeText.Format(DateTime.UtcNow)), ("@by", actor))) await note.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await using (var clear = Command(connection, transaction, "DELETE FROM elcomtur_invoices")) await clear.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await using (var clear = Command(connection, transaction, "DELETE FROM elcomtur_balances")) await clear.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            var now = MariaTimeText.Format(DateTime.UtcNow);
            foreach (var balance in balances)
            {
                await using (var insert = Command(connection, transaction, """
                    INSERT INTO elcomtur_balances(beneficiary_id,balance,data_date,firm,imported_utc,imported_by) VALUES(@id,@balance,@date,@firm,@now,@by)
                    """, ("@id", balance.BeneficiaryId), ("@balance", balance.Balance), ("@date", dataDate.ToString("yyyy-MM-dd")), ("@firm", firm), ("@now", now), ("@by", actor)))
                    await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                foreach (var invoice in balance.Invoices)
                {
                    await using var line = Command(connection, transaction, """
                        INSERT INTO elcomtur_invoices(beneficiary_id,number,invoice_date,due_date,total,paid,rest) VALUES(@id,@number,@date,@due,@total,@paid,@rest)
                        """, ("@id", balance.BeneficiaryId), ("@number", Cut(invoice.Number, 30)), ("@date", invoice.Date.ToString("yyyy-MM-dd")), ("@due", invoice.DueDate.ToString("yyyy-MM-dd")),
                        ("@total", invoice.Total), ("@paid", invoice.Paid), ("@rest", invoice.Rest));
                    await line.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            return 0;
        }, message => new ElcomturOperationException(message), cancellationToken).ConfigureAwait(false);
    }

    private async Task<List<BeneficiaryBalance>> ReadAsync(string where, (string Name, object? Value)[] parameters, CancellationToken cancellationToken)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        var heads = new List<(int Id, string Name, decimal Balance, DateOnly Date, string Firm)>();
        await using (var command = Command(connection, $"SELECT b.beneficiary_id, n.name, b.balance, b.data_date, b.firm FROM elcomtur_balances b INNER JOIN beneficiaries n ON n.id=b.beneficiary_id {where}", parameters))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                heads.Add(((int)reader.GetInt64(0), reader.GetString(1), reader.GetDecimal(2), DateOnly.FromDateTime(reader.GetDateTime(3)), reader.GetString(4)));
        var lines = new Dictionary<int, List<BalanceInvoice>>();
        await using (var command = Command(connection, $"SELECT i.beneficiary_id, i.number, i.invoice_date, i.due_date, i.total, i.paid, i.rest FROM elcomtur_invoices i INNER JOIN elcomtur_balances b ON b.beneficiary_id=i.beneficiary_id {where} ORDER BY i.due_date, i.number", parameters))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var id = (int)reader.GetInt64(0);
                if (!lines.TryGetValue(id, out var list)) lines[id] = list = [];
                list.Add(new BalanceInvoice(reader.GetString(1), DateOnly.FromDateTime(reader.GetDateTime(2)), DateOnly.FromDateTime(reader.GetDateTime(3)), reader.GetDecimal(4), reader.GetDecimal(5), reader.GetDecimal(6)));
            }
        return [.. heads.Select(head => new BeneficiaryBalance(head.Id, head.Name, head.Balance, head.Date, head.Firm, lines.TryGetValue(head.Id, out var list) ? list : []))];
    }

    private static string Cut(string value, int length) => value.Length > length ? value[..length] : value;
}
