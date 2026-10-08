namespace BlazorStoc.Services;

// What happens with the values ANAF returns when a form already holds some: per field (Setari -> Preluare date ANAF, column "Cand ANAF da alta valoare"):
//   confirm   - the user chooses, field by field (default);
//   empty     - the user's value is kept, only empty fields are filled;
//   overwrite - the value from ANAF replaces the one in the form without asking.
// A field that is empty in the form is always filled; a value equal to the one in the form (ignoring case and the usual normalization) changes nothing;
// a field ANAF did not return is left as it is (the "Raporteaza eroare" policy of the mapping stops the lookup earlier, in AnafRules.Interpret).

public sealed record AnafFieldProposal(string Label, string Current, string Incoming);

/// <summary>Direct: applied at once; Ask: shown to the user; KeptMine: labels whose value the user's own was kept by the policy.</summary>
public sealed record AnafApplyPlan(IReadOnlyList<AnafFieldProposal> Direct, IReadOnlyList<AnafFieldProposal> Ask, IReadOnlyList<string> KeptMine);

public static class AnafApplyRules
{
    public const string Confirm = "confirm", KeepMine = "empty", Overwrite = "overwrite";
    public static readonly IReadOnlyList<string> FormLabels = ["Denumire", "Adresă fiscală", "Nr. Registrul Comerțului", "Telefon", "Cod poștal", "Cod CAEN"];

    /// <summary>The value as the form stores it (the same normalization the forms apply to what they receive).</summary>
    public static string Normalize(string label, string? value)
    {
        var text = value ?? "";
        return label switch
        {
            "Denumire" or "Adresă fiscală" => TextNormalization.ForObjectNameOrCode(text),
            "Nr. Registrul Comerțului" => TextNormalization.ForObjectNameOrCode(text).ToUpperInvariant(),
            "Telefon" => BeneficiaryRules.NormalizePhone(text),
            _ => BeneficiaryRules.CompactValue(text)
        };
    }

    public static IReadOnlyList<AnafFieldProposal> Proposals(AnafCompany company, string name, string address, string registry, string phone, string postal, string caen) =>
    [
        new("Denumire", name, Normalize("Denumire", company.Name)),
        new("Adresă fiscală", address, Normalize("Adresă fiscală", company.Address)),
        new("Nr. Registrul Comerțului", registry, Normalize("Nr. Registrul Comerțului", company.RegistryNumber)),
        new("Telefon", phone, Normalize("Telefon", company.Phone)),
        new("Cod poștal", postal, Normalize("Cod poștal", company.PostalCode)),
        new("Cod CAEN", caen, Normalize("Cod CAEN", company.CaenCode))
    ];

    public static AnafApplyPlan Plan(IReadOnlyList<AnafFieldProposal> proposals, IReadOnlyDictionary<string, string>? policies)
    {
        var direct = new List<AnafFieldProposal>();
        var ask = new List<AnafFieldProposal>();
        var kept = new List<string>();
        foreach (var proposal in proposals)
        {
            if (proposal.Incoming.Length == 0) continue;
            var current = Normalize(proposal.Label, proposal.Current);
            if (string.Equals(current, proposal.Incoming, StringComparison.Ordinal)) continue;
            var policy = policies is not null && policies.TryGetValue(proposal.Label, out var chosen) ? chosen : Confirm;
            // A difference of case only is not worth a question: it is applied when the policy takes the value of ANAF, otherwise ignored.
            if (current.Length > 0 && string.Equals(current, proposal.Incoming, StringComparison.OrdinalIgnoreCase)) { if (policy == Overwrite) direct.Add(proposal); continue; }
            if (current.Length == 0) { direct.Add(proposal); continue; }
            if (policy == Overwrite) direct.Add(proposal);
            else if (policy == KeepMine) kept.Add(proposal.Label);
            else ask.Add(proposal);
        }
        return new(direct, ask, kept);
    }
}
