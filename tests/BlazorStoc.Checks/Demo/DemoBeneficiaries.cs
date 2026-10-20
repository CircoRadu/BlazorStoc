// Test double of the in-memory suite (tests/BlazorStoc.Checks): an in-memory implementation of the repository used instead of MariaDB.
namespace BlazorStoc.Services;

public sealed class DemoBeneficiaryStore
{
    internal readonly object Gate = new();
    internal readonly List<Beneficiary> Beneficiaries =
    [
        new(1, "Construct Demo SRL", "RO10000001", 0, BeneficiaryKinds.Legal, "Strada Demo 1, Bucuresti", "0721000001"),
        new(2, "Atelier Tehnic SRL", "RO10000002", 0, BeneficiaryKinds.Legal, "Strada Demo 2, Cluj-Napoca", "0721000002"),
        new(3, "Servicii Industriale SA", "10000003", 0, BeneficiaryKinds.Legal, "Strada Demo 3, Timisoara", "0721000003")
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
            beneficiary = Build(store.NextId++, value, 0);
            store.Beneficiaries.Add(beneficiary);
        }
        await AuditRecorder.RecordCreateAsync(auditTrail, accessControl, AuditEntities.Beneficiary, beneficiary.Id.ToString(),
            $"#{beneficiary.Id} · {beneficiary.Name}", BeneficiaryRules.Identification(beneficiary), cancellationToken);
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
            updated = Build(original.Id, value, checked(original.Version + 1));
            store.Beneficiaries[index] = updated;
        }
        await AuditRecorder.RecordEditAsync(auditTrail, accessControl, AuditEntities.Beneficiary, updated.Id.ToString(),
            $"#{updated.Id} · {updated.Name}",
            BeneficiaryRules.Changes(original, updated), value.Reason, cancellationToken);
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

    private static Beneficiary Build(int id, BeneficiaryInput value, long version) => new(id, value.Name, value.Cui, version, value.Kind,
        value.Address, value.Phone, value.RegistryNumber, value.PostalCode, value.CaenCode, value.AnafVerified);

    private void EnsureUniqueBeneficiary(BeneficiaryInput value, int? excludedId)
    {
        var key = BeneficiaryRules.IdentityKey(value);
        var duplicate = store.Beneficiaries.FirstOrDefault(beneficiary => beneficiary.Id != excludedId &&
            string.Equals(BeneficiaryRules.IdentityKey(BeneficiaryInput.From(beneficiary)), key, StringComparison.Ordinal));
        if (duplicate is not null) throw BeneficiaryRules.DuplicateIdentity(value, duplicate.Name);
        var duplicateName = store.Beneficiaries.FirstOrDefault(beneficiary =>
            beneficiary.Id != excludedId && TextNormalization.SameUniqueValue(beneficiary.Name, value.Name));
        if (duplicateName is not null)
            throw new BeneficiaryOperationException(BeneficiaryRules.DuplicateNameMessage(duplicateName.Name, duplicateName.Cui));
    }

    private Task EnsureOperatorAsync(CancellationToken token) => accessControl?.EnsureBeneficiaryOperatorAsync(token) ?? Task.CompletedTask;
}
