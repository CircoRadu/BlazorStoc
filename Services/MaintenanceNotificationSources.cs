using MySqlConnector;

namespace BlazorStoc.Services;

// The two notification sources of the maintenance contracts (docs/PROPUNERE_CONTRACTE_MENTENANTA.md, sections 6.2 and 6.3):
//  - "mentenanta.scadenta": one instance per work point covered by an ACTIVE contract, dated at its next due date;
//  - "contract.expirare": one instance per ACTIVE contract that has an expiry date.
// A contract switched Off, a point taken out of a contract, a deleted contract, or a changed date closes the notification at the next
// evaluation (the engine writes the reason). On-demand interventions never produce or close a notification.

public sealed record MaintenanceDueItem(int CoverageId, int BeneficiaryId, string Beneficiary, string WorkPoint, string Address,
    string ContractLabel, DateOnly NextDue, DateOnly? LastIntervention);

public sealed record ContractExpiryItem(int ContractId, int BeneficiaryId, string Beneficiary, string ContractLabel, DateOnly ContractDate, DateOnly ValidUntil);

// Read-only and without the operator check of the contract repositories: the evaluation of the notifications runs for whoever opens the
// application first, and the notifications must not depend on that person being an operator of beneficiaries.
public interface IMaintenanceNotificationReader
{
    Task<IReadOnlyList<MaintenanceDueItem>> GetDueAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ContractExpiryItem>> GetContractExpiriesAsync(CancellationToken cancellationToken = default);
}

public sealed class MariaMaintenanceNotificationReader(IConfiguration configuration) : IMaintenanceNotificationReader
{
    public async Task<IReadOnlyList<MaintenanceDueItem>> GetDueAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = DatabaseConnections.Create(configuration);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("""
            SELECT p.id, c.beneficiary_id, b.name, w.name, w.address, c.contract_number, c.contract_date, p.next_due,
                   (SELECT MAX(i.performed_on) FROM service_interventions i WHERE i.work_point_id = p.work_point_id AND i.kind = 'M')
            FROM service_contract_points p
            JOIN service_contracts c ON c.id = p.contract_id AND c.is_active = 1
            JOIN beneficiary_work_points w ON w.id = p.work_point_id
            JOIN beneficiaries b ON b.id = c.beneficiary_id
            ORDER BY p.next_due, p.id
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<MaintenanceDueItem>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new(checked((int)reader.GetInt64(0)), checked((int)reader.GetInt64(1)), reader.GetString(2), reader.GetString(3), reader.GetString(4),
                ServiceContractNumber.Format(reader.GetString(5), ReadDate(reader, 6)), ReadDate(reader, 7), reader.IsDBNull(8) ? null : ReadDate(reader, 8)));
        return result;
    }

    public async Task<IReadOnlyList<ContractExpiryItem>> GetContractExpiriesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = DatabaseConnections.Create(configuration);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("""
            SELECT c.id, c.beneficiary_id, b.name, c.contract_number, c.contract_date, c.valid_until
            FROM service_contracts c JOIN beneficiaries b ON b.id = c.beneficiary_id
            WHERE c.is_active = 1 AND c.valid_until IS NOT NULL
            ORDER BY c.valid_until, c.id
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<ContractExpiryItem>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var date = ReadDate(reader, 4);
            result.Add(new(checked((int)reader.GetInt64(0)), checked((int)reader.GetInt64(1)), reader.GetString(2),
                ServiceContractNumber.Format(reader.GetString(3), date), date, ReadDate(reader, 5)));
        }
        return result;
    }

    private static DateOnly ReadDate(MySqlDataReader reader, int ordinal) => DateOnly.FromDateTime(reader.GetDateTime(ordinal));
}

public sealed class MaintenanceDueSource(IMaintenanceNotificationReader reader, string key = ExpirySourceKeys.MaintenanceDue) : IExpirySource
{
    public const string BeneficiaryName = "beneficiar";
    public const string WorkPointName = "punct de lucru";
    public const string AddressName = "adresa punct de lucru";
    public const string ContractName = "numar contract";
    public const string LastInterventionName = "data ultima interventie";
    // Written in place of the date when the point never had a maintenance intervention.
    public const string NoIntervention = "nicio intervenție";

    public string Key => key;
    public string Category => "Mentenanță";
    public string EventName => "Intervenție de mentenanță";
    public string DateLabel => "scadenței";
    public string RemovedReason => "Punctul de lucru nu mai este acoperit de un contract activ (contractul a fost dezactivat sau șters, ori punctul a fost scos din contract).";
    public string DefaultSubject => "Scadență mentenanță – <beneficiar>, <punct de lucru>";
    public string DefaultBody =>
        "Intervenția de mentenanță la punctul de lucru <punct de lucru> (<adresa punct de lucru>) al beneficiarului <beneficiar>, contract <numar contract>, " +
        "este programată pentru <data expirare> (zile rămase: <zile ramase>; zile de depășire: <zile depasire>). Ultima intervenție de mentenanță: <data ultima interventie>.";
    public IReadOnlyList<ExpiryPlaceholder> Placeholders { get; } =
    [
        new(BeneficiaryName, "Numele beneficiarului", "Demo Puncte SRL"),
        new(WorkPointName, "Numele punctului de lucru", "Sediu central"),
        new(AddressName, "Adresa punctului de lucru", "Strada Demo 10, Timișoara"),
        new(ContractName, "Numărul contractului, ca număr/data", "26/23.09.2025"),
        new(LastInterventionName, "Data ultimei intervenții de mentenanță a punctului (cele la cerere nu se numără)", "18.01.2026")
    ];

    public string DateChangedReason(DateOnly from, DateOnly to, ExpiryInstance current) =>
        $"Scadența intervenției de mentenanță s-a modificat de la {StockMovementRules.DisplayDate(from)} la {StockMovementRules.DisplayDate(to)}" +
        (current.Values.TryGetValue(LastInterventionName, out var last) && last != NoIntervention ? $" (ultima intervenție de mentenanță: {last})." : ".");

    public async Task<IReadOnlyList<ExpiryInstance>> GetInstancesAsync(CancellationToken cancellationToken = default) =>
        (await reader.GetDueAsync(cancellationToken).ConfigureAwait(false)).Select(item => new ExpiryInstance(item.CoverageId, $"{item.Beneficiary} · {item.WorkPoint}", item.NextDue,
            new Dictionary<string, string>
            {
                [BeneficiaryName] = item.Beneficiary, [WorkPointName] = item.WorkPoint, [AddressName] = item.Address, [ContractName] = item.ContractLabel,
                [LastInterventionName] = item.LastIntervention is { } last ? StockMovementRules.DisplayDate(last) : NoIntervention
            }, $"/beneficiari/{item.BeneficiaryId}")).ToList();
}

public sealed class ContractExpirySource(IMaintenanceNotificationReader reader, string key = ExpirySourceKeys.ContractExpiry) : IExpirySource
{
    public const string BeneficiaryName = "beneficiar";
    public const string ContractName = "numar contract";
    public const string ContractDateName = "data contract";

    public string Key => key;
    public string Category => "Mentenanță";
    public string EventName => "Expirare contract";
    public string DateLabel => "expirării";
    public string RemovedReason => "Contractul a fost dezactivat, șters sau nu mai are termen de expirare.";
    public string DefaultSubject => "Expirare contract – <beneficiar>";
    public string DefaultBody =>
        "Contractul de mentenanță <numar contract> al beneficiarului <beneficiar> expiră la data de <data expirare> " +
        "(zile rămase: <zile ramase>; zile de depășire: <zile depasire>).";
    public IReadOnlyList<ExpiryPlaceholder> Placeholders { get; } =
    [
        new(BeneficiaryName, "Numele beneficiarului", "Demo Puncte SRL"),
        new(ContractName, "Numărul contractului, ca număr/data", "26/23.09.2025"),
        new(ContractDateName, "Data contractului, dd.mm.yyyy", "23.09.2025")
    ];

    public string DateChangedReason(DateOnly from, DateOnly to, ExpiryInstance current) =>
        $"Data expirării contractului s-a modificat de la {StockMovementRules.DisplayDate(from)} la {StockMovementRules.DisplayDate(to)}.";

    public async Task<IReadOnlyList<ExpiryInstance>> GetInstancesAsync(CancellationToken cancellationToken = default) =>
        (await reader.GetContractExpiriesAsync(cancellationToken).ConfigureAwait(false)).Select(item => new ExpiryInstance(item.ContractId, $"{item.Beneficiary} · Contract {item.ContractLabel}", item.ValidUntil,
            new Dictionary<string, string>
            {
                [BeneficiaryName] = item.Beneficiary, [ContractName] = item.ContractLabel, [ContractDateName] = StockMovementRules.DisplayDate(item.ContractDate)
            }, $"/beneficiari/{item.BeneficiaryId}")).ToList();
}
