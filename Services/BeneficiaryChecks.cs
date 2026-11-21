namespace BlazorStoc.Services;

// The beneficiaries that the import from ActivityApp took but that need a human look (the CUI could not be verified in ANAF, ANAF does not know it, it has the wrong form, or the address is
// missing). The queue is kept by the ActivityApp import module; the Beneficiari page reads and works it through this contract, without depending on that module. While the module is out the
// list is empty (the page shows nothing) and the operations answer that the module is out.

public static class BeneficiaryCheckReasons
{
    public const string AnafUnavailable = "anaf_unavailable", CuiUnknown = "cui_unknown", CuiInvalidForm = "cui_invalid_form", MissingAddress = "missing_address";
    // The same CUI (or the same name, for a person) on several partners of ActivityApp; data BlazorStoc does not accept (no name, a name too long, a rule of the beneficiary form).
    public const string RepeatedIdentity = "repeated_identity", InvalidData = "invalid_data";
    // Import automat oprit: a clean partner waits here until the operator accepts it (it is then made a beneficiary).
    public const string AwaitingAcceptance = "awaiting_acceptance";

    public static string Text(string reason) => reason switch
    {
        AnafUnavailable => "ANAF nu a putut răspunde",
        CuiUnknown => "ANAF nu cunoaște CUI-ul",
        CuiInvalidForm => "CUI cu formă invalidă",
        MissingAddress => "adresă lipsă",
        RepeatedIdentity => "același CUI sau nume apare la mai mulți parteneri din ActivityApp",
        InvalidData => "datele nu sunt acceptate de BlazorStoc",
        AwaitingAcceptance => "așteaptă acceptarea manuală",
        _ => reason
    };
}

/// <summary>A partner to verify. BeneficiaryId is empty while no beneficiary was made (missing address); WaitingTickets are the tickets that wait for the clarification.</summary>
public sealed record BeneficiaryCheck(long Id, long PartnerId, int? BeneficiaryId, string Name, string Cui, string Reason, string ReasonText, DateTime CreatedUtc, int WaitingTickets);

/// <summary>Where a beneficiary came from in ActivityApp: created by the import (Created) or already in BlazorStoc and only tied to its partner (otherwise); SourceDeleted = the partner was deleted in ActivityApp since.</summary>
public sealed record BeneficiaryOrigin(bool Created, bool SourceDeleted)
{
    /// <summary>The note shown with the beneficiary: "Preluat din ActivityApp" (created by the import) or "Legat cu ActivityApp" (an existing beneficiary tied to its partner).</summary>
    public string Text => (Created ? "Preluat din ActivityApp" : "Legat cu ActivityApp") + (SourceDeleted ? " (șters ulterior în ActivityApp)" : "");
}

public interface IBeneficiaryCheckService
{
    /// <summary>Per beneficiary id: where it came from in ActivityApp (computed from the ties of the import, nothing is stored on the beneficiary). A beneficiary that did not come from there is absent; empty while the module is out.</summary>
    Task<IReadOnlyDictionary<int, BeneficiaryOrigin>> GetOriginsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyDictionary<int, BeneficiaryOrigin>>(new Dictionary<int, BeneficiaryOrigin>());

    /// <summary>The beneficiaries (partners) to verify, oldest first.</summary>
    Task<IReadOnlyList<BeneficiaryCheck>> GetPendingAsync(CancellationToken cancellationToken = default);
    /// <summary>Asks ANAF again about the CUI: when it knows it, the beneficiary is verified and its tickets are taken at the next import. Returns the message to show.</summary>
    Task<string> RetryAsync(long id, CancellationToken cancellationToken = default);
    /// <summary>The operator checked the beneficiary by hand: it is accepted and its tickets are taken at the next import. A partner that waits for acceptance (automatic import off) is made a beneficiary now.</summary>
    Task AcceptAsync(long id, CancellationToken cancellationToken = default);
    /// <summary>The partner is not taken: its waiting tickets are discarded and its next ones are not read.</summary>
    Task DiscardAsync(long id, CancellationToken cancellationToken = default);
}
