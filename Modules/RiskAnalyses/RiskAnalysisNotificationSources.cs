using MySqlConnector;

namespace BlazorStoc.Services;

// The notification source of the risk analyses: one instance per ACTIVE analysis, dated at its expiry (last renewal + validity in months).
// An analysis switched Off, a deleted one, or a renewal / validity change that moves the expiry closes the notification at the next evaluation
// (the engine writes the reason from the changed date).

public sealed record RiskAnalysisExpiryItem(int AnalysisId, int BeneficiaryId, string Beneficiary, string WorkPoint, string Address, string Number,
    string Author, DateOnly InitialDate, DateOnly LastRenewalDate, int ValidityMonths)
{
    public DateOnly Expiry => RiskAnalysisRules.ExpiryDate(LastRenewalDate, ValidityMonths);
}

// Read-only and without the operator check of the analysis repository: the evaluation of the notifications runs for whoever opens the
// application first, and the notifications must not depend on that person being an operator of beneficiaries.
public interface IRiskAnalysisNotificationReader
{
    Task<IReadOnlyList<RiskAnalysisExpiryItem>> GetActiveAsync(CancellationToken cancellationToken = default);
}

public sealed class MariaRiskAnalysisNotificationReader(IConfiguration configuration) : IRiskAnalysisNotificationReader
{
    public async Task<IReadOnlyList<RiskAnalysisExpiryItem>> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("""
            SELECT a.id, a.beneficiary_id, b.name, w.name, w.address, a.registration_number, a.author, a.initial_date, a.last_renewal_date, a.validity_months
            FROM risk_analyses a
            JOIN beneficiaries b ON b.id = a.beneficiary_id
            JOIN beneficiary_work_points w ON w.id = a.work_point_id
            WHERE a.is_active = 1
            ORDER BY a.id
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<RiskAnalysisExpiryItem>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new(checked((int)reader.GetInt64(0)), checked((int)reader.GetInt64(1)), reader.GetString(2), reader.GetString(3), reader.GetString(4),
                reader.GetString(5), reader.GetString(6), ReadDate(reader, 7), ReadDate(reader, 8), reader.GetInt32(9)));
        return result;
    }

    private static DateOnly ReadDate(MySqlDataReader reader, int ordinal) => DateOnly.FromDateTime(reader.GetDateTime(ordinal));
}

public sealed class RiskAnalysisExpirySource(IRiskAnalysisNotificationReader reader, string key = ExpirySourceKeys.RiskAnalysisExpiry) : IExpirySource
{
    public const string BeneficiaryName = "beneficiar";
    public const string WorkPointName = "punct de lucru";
    public const string AddressName = "adresa punct de lucru";
    public const string NumberName = "numar inregistrare";
    public const string InitialDateName = "data inregistrare initiala";
    public const string LastRenewalName = "data ultima reinnoire";
    public const string ValidityName = "valabilitate luni";
    public const string AuthorName = "intocmit de";

    public string Key => key;
    public string Category => "Analize de risc";
    public string EventName => "Expirare analiză de risc";
    public string DateLabel => "expirării";
    // Two months before the expiry: a new analysis takes time to be made.
    public int DefaultThresholdDays => RiskAnalysisRules.DefaultThresholdDays;
    public string RemovedReason => "Analiza de risc a fost dezactivată sau ștearsă.";
    public string DefaultSubject => "Expirare analiză de risc – <beneficiar>, <punct de lucru>";
    public string DefaultBody =>
        "Analiza de risc nr. <numar inregistrare> a punctului de lucru <punct de lucru> (<adresa punct de lucru>) al beneficiarului <beneficiar>, " +
        "întocmită de <intocmit de>, expiră la data de <data expirare> (ultima reînnoire: <data ultima reinnoire>; valabilitate: <valabilitate luni> luni; " +
        "zile rămase: <zile ramase>; zile de depășire: <zile depasire>).";
    public IReadOnlyList<ExpiryPlaceholder> Placeholders { get; } =
    [
        new(BeneficiaryName, "Numele beneficiarului", "Demo Puncte SRL"),
        new(WorkPointName, "Numele punctului de lucru", "Sediu central"),
        new(AddressName, "Adresa punctului de lucru", "Strada Demo 10, Timișoara"),
        new(NumberName, "Numărul de înregistrare al analizei", "AR-14"),
        new(InitialDateName, "Data la care a fost înregistrată analiza, dd.mm.yyyy", "12.03.2023"),
        new(LastRenewalName, "Data ultimei reînnoiri (la o analiză nereînnoită, data înregistrării), dd.mm.yyyy", "12.03.2023"),
        new(ValidityName, "Valabilitatea analizei, în luni", "36"),
        new(AuthorName, "Numele persoanei care a întocmit analiza", "Ion Popescu")
    ];

    public string DateChangedReason(DateOnly from, DateOnly to, ExpiryInstance current) =>
        $"Data expirării analizei de risc s-a modificat de la {StockMovementRules.DisplayDate(from)} la {StockMovementRules.DisplayDate(to)}" +
        (current.Values.TryGetValue(LastRenewalName, out var renewal) ? $" (ultima reînnoire: {renewal})." : ".");

    public async Task<IReadOnlyList<ExpiryInstance>> GetInstancesAsync(CancellationToken cancellationToken = default) =>
        (await reader.GetActiveAsync(cancellationToken).ConfigureAwait(false)).Select(item => new ExpiryInstance(item.AnalysisId,
            $"{item.Beneficiary} · {item.WorkPoint} · Analiză de risc {item.Number}", item.Expiry,
            new Dictionary<string, string>
            {
                [BeneficiaryName] = item.Beneficiary, [WorkPointName] = item.WorkPoint, [AddressName] = item.Address, [NumberName] = item.Number,
                [InitialDateName] = StockMovementRules.DisplayDate(item.InitialDate), [LastRenewalName] = StockMovementRules.DisplayDate(item.LastRenewalDate),
                [ValidityName] = item.ValidityMonths.ToString(System.Globalization.CultureInfo.InvariantCulture), [AuthorName] = item.Author
            }, $"/beneficiari/{item.BeneficiaryId}")).ToList();
}
