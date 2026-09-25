using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace BlazorStoc.Services;

public sealed record Beneficiary(int Id, string Name, string Cui, long Version = 0);

public sealed class BeneficiaryInput
{
    [Required(ErrorMessage = "Completează numele beneficiarului.")]
    [StringLength(200, ErrorMessage = "Numele poate avea cel mult 200 de caractere.")]
    public string Name { get; set; } = "";

    [Required(ErrorMessage = "Completează CUI-ul beneficiarului.")]
    [StringLength(12, ErrorMessage = "CUI-ul poate avea cel mult 12 caractere.")]
    [RegularExpression(@"^(?i:RO)?[0-9]{2,10}$", ErrorMessage = "CUI-ul trebuie să conțină 2–10 cifre, opțional precedate de RO.")]
    public string Cui { get; set; } = "";

    [StringLength(ChangeReasonRules.MaximumLength, ErrorMessage = ChangeReasonRules.TooLongMessage)]
    public string Reason { get; set; } = "";

    public BeneficiaryInput Validated(bool requiresReason = false)
    {
        var normalized = new BeneficiaryInput
        {
            Name = TextNormalization.ForObjectNameOrCode(Name),
            Cui = TextNormalization.ForObjectNameOrCode(Cui),
            Reason = ChangeReasonRules.Normalize(Reason)
        };
        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(normalized, new ValidationContext(normalized), results, true))
            throw new BeneficiaryOperationException(string.Join(" ", results.Select(result => result.ErrorMessage)));
        if (requiresReason && ChangeReasonRules.ValidationError(normalized.Reason) is { } reasonError)
            throw new BeneficiaryOperationException(reasonError);
        return normalized;
    }

    public static BeneficiaryInput From(Beneficiary beneficiary) => new() { Name = beneficiary.Name, Cui = beneficiary.Cui };
}

public sealed class BeneficiaryOperationException(string message) : Exception(message);

public interface IBeneficiaryRepository
{
    Task<IReadOnlyList<Beneficiary>> GetBeneficiariesAsync(CancellationToken cancellationToken = default);
    Task<Beneficiary> CreateAsync(BeneficiaryInput input, CancellationToken cancellationToken = default);
    Task<Beneficiary> UpdateAsync(Beneficiary original, BeneficiaryInput input, CancellationToken cancellationToken = default);
    Task DeleteAsync(Beneficiary original, string reason, CancellationToken cancellationToken = default);
}

public static class BeneficiarySearch
{
    public static IEnumerable<Beneficiary> Filter(IEnumerable<Beneficiary> beneficiaries, string query)
    {
        query = query.Trim();
        return query.Length == 0 ? beneficiaries : beneficiaries.Where(beneficiary =>
            beneficiary.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            beneficiary.Cui.Contains(query.Replace(" ", ""), StringComparison.OrdinalIgnoreCase));
    }
}

public static class BeneficiaryRules
{
    public static void CheckCurrent(Beneficiary? current, Beneficiary original)
    {
        if (current is null || current != original)
            throw new BeneficiaryOperationException("Beneficiarul a fost modificat sau șters între timp. Actualizează lista și reia operația.");
    }

    // Single wording for every storage mode; the name is the one stored in the database, not the one just typed.
    public static string DuplicateCuiMessage(string? existingName) =>
        string.IsNullOrWhiteSpace(existingName)
            ? "Există deja un beneficiar cu acest CUI."
            : $"Există deja un beneficiar cu acest CUI: «{existingName}».";

    public static void CheckDelete(bool hasStockMovements)
    {
        if (hasStockMovements)
            throw new BeneficiaryOperationException("Beneficiarul are mișcări de stoc asociate și nu poate fi șters.");
    }

    // Live projects must be moved to another beneficiary or archived first; archived projects do not block deletion.
    public static void CheckNoLiveProjects(int liveProjectCount)
    {
        if (liveProjectCount > 0)
            throw new BeneficiaryOperationException(liveProjectCount == 1
                ? "Beneficiarul are un proiect asociat și nu poate fi șters. Mută sau arhivează mai întâi proiectul."
                : $"Beneficiarul are {liveProjectCount} proiecte asociate și nu poate fi șters. Mută sau arhivează mai întâi proiectele.");
    }
}

public sealed class DemoBeneficiaryStore
{
    internal readonly object Gate = new();
    internal readonly List<Beneficiary> Beneficiaries =
    [
        new(1, "Construct Demo SRL", "RO10000001"),
        new(2, "Atelier Tehnic SRL", "RO10000002"),
        new(3, "Servicii Industriale SA", "10000003")
    ];
    internal int NextId = 4;
}

public sealed class DemoBeneficiaryRepository(
    IAccessControl? accessControl = null,
    DemoBeneficiaryStore? sharedStore = null,
    IAuditTrail? auditTrail = null,
    IArchiveService? archiveService = null) : IBeneficiaryRepository
{
    private readonly DemoBeneficiaryStore store = sharedStore ?? new DemoBeneficiaryStore();
    private readonly IArchiveService archiver = archiveService ?? new ArchiveService(accessControl);

    public async Task<IReadOnlyList<Beneficiary>> GetBeneficiariesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken);
        lock (store.Gate) return store.Beneficiaries.OrderBy(beneficiary => beneficiary.Name).ToArray();
    }

    public async Task<Beneficiary> CreateAsync(BeneficiaryInput input, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var value = input.Validated();
        Beneficiary beneficiary;
        lock (store.Gate)
        {
            EnsureUniqueBeneficiary(value, null);
            beneficiary = new(store.NextId++, value.Name, value.Cui);
            store.Beneficiaries.Add(beneficiary);
        }
        await AuditRecorder.RecordCreateAsync(auditTrail, accessControl, AuditEntities.Beneficiary, beneficiary.Id.ToString(),
            $"#{beneficiary.Id} · {beneficiary.Name}", AuditDetails.Identification(
                ("Denumire", beneficiary.Name), ("CUI", beneficiary.Cui)), cancellationToken);
        return beneficiary;
    }

    public async Task<Beneficiary> UpdateAsync(Beneficiary original, BeneficiaryInput input, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var value = input.Validated(true);
        Beneficiary updated;
        lock (store.Gate)
        {
            var index = store.Beneficiaries.FindIndex(beneficiary => beneficiary.Id == original.Id);
            var current = index < 0 ? null : store.Beneficiaries[index];
            BeneficiaryRules.CheckCurrent(current, original);
            EnsureUniqueBeneficiary(value, original.Id);
            updated = new(original.Id, value.Name, value.Cui, checked(original.Version + 1));
            store.Beneficiaries[index] = updated;
        }
        await AuditRecorder.RecordEditAsync(auditTrail, accessControl, AuditEntities.Beneficiary, updated.Id.ToString(),
            $"#{updated.Id} · {updated.Name}",
            [new("Denumire", original.Name, updated.Name), new("CUI", original.Cui, updated.Cui)],
            value.Reason, cancellationToken);
        return updated;
    }

    public async Task DeleteAsync(Beneficiary original, string reason, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var motif = ChangeReasonRules.Normalize(reason);
        if (ChangeReasonRules.ValidationError(motif) is { } reasonError)
            throw new BeneficiaryOperationException(reasonError);
        await archiver.ExecuteAsync(ArchiveRequests.Beneficiary(original, motif), async (operation, token) =>
        {
            lock (store.Gate)
            {
                BeneficiaryRules.CheckCurrent(store.Beneficiaries.SingleOrDefault(beneficiary => beneficiary.Id == original.Id), original);
                store.Beneficiaries.RemoveAll(beneficiary => beneficiary.Id == original.Id);
            }
            await AuditRecorder.RecordDeleteAsync(auditTrail, operation, token);
        }, cancellationToken);
    }

    private void EnsureUniqueBeneficiary(BeneficiaryInput value, int? excludedId)
    {
        var duplicateCui = store.Beneficiaries.FirstOrDefault(beneficiary =>
            beneficiary.Id != excludedId && TextNormalization.SameUniqueValue(beneficiary.Cui, value.Cui));
        if (duplicateCui is not null)
            throw new BeneficiaryOperationException(BeneficiaryRules.DuplicateCuiMessage(duplicateCui.Name));
        var duplicateName = store.Beneficiaries.FirstOrDefault(beneficiary =>
            beneficiary.Id != excludedId && TextNormalization.SameUniqueValue(beneficiary.Name, value.Name));
        if (duplicateName is not null)
            throw new BeneficiaryOperationException($"Beneficiarul «{duplicateName.Name}» există deja și are CUI «{duplicateName.Cui}».");
    }

    private Task EnsureOperatorAsync(CancellationToken token) => accessControl?.EnsureBeneficiaryOperatorAsync(token) ?? Task.CompletedTask;
}
