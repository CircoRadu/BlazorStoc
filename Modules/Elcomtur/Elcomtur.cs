using BlazorStoc.Services;

namespace BlazorStoc.Modules.Elcomtur;

/// <summary>An answer for the user (the file is not an Elcomtur backup, nothing matched, ...), not a failure of the module.</summary>
public sealed class ElcomturOperationException(string message) : Exception(message);

/// <summary>An invoice still marked unpaid in Elcomtur, as kept for a beneficiary.</summary>
public sealed record BalanceInvoice(string Number, DateOnly Date, DateOnly DueDate, decimal Total, decimal Paid, decimal Rest)
{
    public bool IsOverdue(DateOnly today) => DueDate < today;
    public int DaysLate(DateOnly today) => Math.Max(0, today.DayNumber - DueDate.DayNumber);
}

/// <summary>The balance of a beneficiary at the date of the Elcomtur backup it came from (positive = payments overdue or due).</summary>
public sealed record BeneficiaryBalance(int BeneficiaryId, string BeneficiaryName, decimal Balance, DateOnly DataDate, string Firm, IReadOnlyList<BalanceInvoice> Invoices)
{
    public bool IsOwed => Balance > 0.004m;
    public IReadOnlyList<BalanceInvoice> Overdue(DateOnly today) => [.. Invoices.Where(invoice => invoice.IsOverdue(today))];
    public IReadOnlyList<BalanceInvoice> InTerm(DateOnly today) => [.. Invoices.Where(invoice => !invoice.IsOverdue(today))];
    /// <summary>Balance minus the rests of the invoices: payments not allocated to an invoice (positive: the balance is not covered by open invoices).</summary>
    public decimal Difference => Math.Round(Balance - Invoices.Sum(invoice => invoice.Rest), 2, MidpointRounding.AwayFromZero);
}

public sealed record ElcomturImportInfo(DateOnly DataDate, string Firm, DateTime ImportedUtc, string ImportedBy, int Beneficiaries);

public interface IElcomturBalanceRepository
{
    /// <summary>The balance of one beneficiary, or null when the last import had no client for it.</summary>
    Task<BeneficiaryBalance?> GetAsync(int beneficiaryId, CancellationToken cancellationToken = default);
    /// <summary>The beneficiaries with a positive balance, the largest first.</summary>
    Task<IReadOnlyList<BeneficiaryBalance>> GetOwedAsync(CancellationToken cancellationToken = default);
    /// <summary>How many beneficiaries have a positive balance (the number on the tab of the Notifications page).</summary>
    Task<int> CountOwedAsync(CancellationToken cancellationToken = default);
    Task<ElcomturImportInfo?> GetInfoAsync(CancellationToken cancellationToken = default);
    /// <summary>Replaces every balance by the ones of a new import.</summary>
    Task ReplaceAsync(string firm, DateOnly dataDate, IReadOnlyList<BeneficiaryBalance> balances, CancellationToken cancellationToken = default);
}

/// <summary>A client of Elcomtur that has a positive balance and no beneficiary with its CUI.</summary>
public sealed record ElcomturUnmatched(int Code, string Name, string Cui, decimal Balance);

public sealed record ElcomturReport(string Firm, DateOnly DataDate, int Clients, int LegalClients, int Matched, int MatchedOwed, decimal OwedTotal,
    int UnmatchedOwed, decimal UnmatchedOwedTotal, IReadOnlyList<ElcomturUnmatched> UnmatchedList, bool Applied);

public interface IElcomturImportService
{
    /// <summary>Keeps the uploaded backup for this import (checked to be an Elcomtur archive) and gives its id.</summary>
    Task<string> UploadAsync(Stream body, string fileName, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetFirmsAsync(string fileId, CancellationToken cancellationToken = default);
    /// <summary>Simulation (apply = false, nothing is written) or application of the import.</summary>
    Task<ElcomturReport> RunAsync(string fileId, string firm, bool apply, CancellationToken cancellationToken = default);
    Task DiscardAsync(string fileId, CancellationToken cancellationToken = default);
}

public sealed record ElcomturPlan(IReadOnlyList<BeneficiaryBalance> Balances, ElcomturReport Report);

public static class ElcomturPlanner
{
    // Largest list of clients without a beneficiary shown in the report.
    public const int UnmatchedShown = 200;

    /// <summary>
    /// Matches the legal-entity clients of Elcomtur with the legal-entity beneficiaries by CUI (digits only: "RO9178894" and "9178894" are the same). Clients with the same CUI are added
    /// together. Nothing is created: a client without a beneficiary is only reported (when it owes something).
    /// </summary>
    public static ElcomturPlan Plan(ElcomturData data, IReadOnlyList<Beneficiary> beneficiaries)
    {
        var balances = ElcomturCalculator.Calculate(data.Clients, data.Invoices, data.Receipts);
        var legal = balances.Where(item => item.Client.IsLegalEntity && InvoiceValues.NormalizeCui(item.Client.Cui).Length > 0).ToList();
        var byCui = legal.GroupBy(item => InvoiceValues.NormalizeCui(item.Client.Cui)).ToDictionary(group => group.Key, group => group.ToList());
        var matched = new List<BeneficiaryBalance>();
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var beneficiary in beneficiaries.Where(item => !item.IsIndividual))
        {
            var cui = InvoiceValues.NormalizeCui(beneficiary.Cui);
            if (cui.Length == 0 || !byCui.TryGetValue(cui, out var group) || !used.Add(cui)) continue;
            var invoices = group.SelectMany(item => item.OpenInvoices).OrderBy(invoice => invoice.DueDate).ThenBy(invoice => invoice.Number, StringComparer.Ordinal)
                .Select(invoice => new BalanceInvoice(invoice.Number, invoice.Date, invoice.DueDate, invoice.Total, invoice.Paid, invoice.Rest)).ToList();
            matched.Add(new BeneficiaryBalance(beneficiary.Id, beneficiary.Name, Math.Round(group.Sum(item => item.Balance), 2, MidpointRounding.AwayFromZero), data.DataDate, data.Firm, invoices));
        }
        var unmatched = byCui.Where(pair => !used.Contains(pair.Key))
            .Select(pair => new ElcomturUnmatched(pair.Value[0].Client.Code, pair.Value[0].Client.Name, pair.Value[0].Client.Cui, Math.Round(pair.Value.Sum(item => item.Balance), 2)))
            .Where(item => item.Balance > 0.004m).OrderByDescending(item => item.Balance).ToList();
        var owed = matched.Where(item => item.IsOwed).ToList();
        var report = new ElcomturReport(data.Firm, data.DataDate, data.Clients.Count, data.Clients.Count(client => client.IsLegalEntity), matched.Count, owed.Count, owed.Sum(item => item.Balance),
            unmatched.Count, unmatched.Sum(item => item.Balance), [.. unmatched.Take(UnmatchedShown)], false);
        return new ElcomturPlan(matched, report);
    }
}

public static class ElcomturFormat
{
    private static readonly System.Globalization.CultureInfo Romanian = System.Globalization.CultureInfo.GetCultureInfo("ro-RO");

    /// <summary>"1.234,50 RON".</summary>
    public static string Money(decimal value) => value.ToString("N2", Romanian) + " RON";
}
