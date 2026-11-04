namespace BlazorStoc.Modules.Elcomtur;

public sealed record ElcomturClient(int Code, string Name, string Cui, bool IsLegalEntity);

/// <summary>An invoice issued to a client; Rest is what Elcomtur still marks unpaid on it (partial payments included).</summary>
public sealed record ElcomturInvoice(int ClientCode, string Number, DateOnly Date, int TermDays, decimal Total, decimal Paid, decimal Rest)
{
    public DateOnly DueDate => Date.AddDays(TermDays);
    public bool IsOverdue(DateOnly today) => DueDate < today;
}

/// <summary>A receipt from a client (an entry of the cash register / bank of the client accounts 4111...).</summary>
public sealed record ElcomturReceipt(int ClientCode, decimal Amount);

/// <summary>The balance of a client: what was invoiced minus what was collected, with the invoices still marked unpaid.</summary>
public sealed record ElcomturBalance(ElcomturClient Client, decimal Balance, IReadOnlyList<ElcomturInvoice> OpenInvoices);

public static class ElcomturCalculator
{
    /// <summary>
    /// Balance of every client that has invoices: the sum of the invoices minus the sum of the receipts (the full history up to the date of the backup), the logic of the Elcomtur
    /// program (checked on Spitalul Zam at 30.09.2026: 3.595.888,35 - 3.477.810,75 = 118.077,60). The invoices still unpaid are those with a rest above zero.
    /// </summary>
    public static IReadOnlyList<ElcomturBalance> Calculate(IReadOnlyList<ElcomturClient> clients, IReadOnlyList<ElcomturInvoice> invoices, IReadOnlyList<ElcomturReceipt> receipts)
    {
        var invoiced = invoices.GroupBy(invoice => invoice.ClientCode).ToDictionary(group => group.Key, group => group.ToList());
        var collected = receipts.GroupBy(receipt => receipt.ClientCode).ToDictionary(group => group.Key, group => group.Sum(receipt => receipt.Amount));
        var result = new List<ElcomturBalance>();
        foreach (var client in clients)
        {
            if (!invoiced.TryGetValue(client.Code, out var own)) continue;
            var balance = Math.Round(own.Sum(invoice => invoice.Total) - collected.GetValueOrDefault(client.Code), 2, MidpointRounding.AwayFromZero);
            var open = own.Where(invoice => invoice.Rest > 0.005m).OrderBy(invoice => invoice.DueDate).ThenBy(invoice => invoice.Number, StringComparer.Ordinal).ToList();
            result.Add(new ElcomturBalance(client, balance, open));
        }
        return result;
    }
}
