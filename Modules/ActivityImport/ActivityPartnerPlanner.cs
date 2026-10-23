using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BlazorStoc.Services;

namespace BlazorStoc.Modules.ActivityImport;

// What the import of partners and work points will do, decided without touching anything (the simulation shows it, the application carries it out). Nothing is guessed: a partner
// whose tax id is invalid or repeated in ActivityApp, or that has no address or phone BlazorStoc accepts, is a problem for the administrator, not an import. A partner that already
// exists in BlazorStoc (same CUI, or same name for a person) is only tied to it, never changed. The main work point of a beneficiary is the row created with the beneficiary:
// the ActivityApp point with the same address is tied to it, the others become additional points.

public enum PlanAction { Create, Link, AlreadyLinked, Problem, Skipped, Waiting }

public sealed record PartnerPlan(long SourceId, string Name, string Identity, PlanAction Action, int? LocalId, BeneficiaryInput? Input, string Message, string Hash, string SourceAddress, string Check = "", string CuiTyped = "", bool Manual = false);

public sealed record PointPlan(long SourceId, long PartnerId, string Name, PlanAction Action, int? LocalId, bool ToMain, WorkPointInput? Input, string Message, string Hash);

/// <summary>What ANAF answered for the CUI of a partner (key: digits of the CUI): the address and the phone it holds, or why it could not answer.</summary>
public sealed record CompanyData(string Address, string Phone, string? Failure = null, bool NotFound = false);

/// <summary>A tie already made (entity "partner" or "workpoint").</summary>
public sealed record ImportMapEntry(string Entity, long SourceId, int LocalId);

public sealed record PlanCounts(int Total, int Create, int Link, int AlreadyLinked, int Problems, int Skipped, int Waiting = 0)
{
    public static PlanCounts Of(IEnumerable<PlanAction> actions)
    {
        var list = actions.ToList();
        int Count(PlanAction action) => list.Count(item => item == action);
        return new PlanCounts(list.Count, Count(PlanAction.Create), Count(PlanAction.Link), Count(PlanAction.AlreadyLinked), Count(PlanAction.Problem), Count(PlanAction.Skipped), Count(PlanAction.Waiting));
    }
}

public static class ActivityPartnerPlanner
{
    public const string PartnerEntity = "partner";
    public const string PointEntity = "workpoint";
    /// <summary>Partners the operator included by hand (from the ignored ones): local id 1 = as a person, 2 = as a company. They are taken without ANAF, marked as not verified.</summary>
    public const string IncludeEntity = "partner-include";
    public const int IncludeAsPerson = 1, IncludeAsCompany = 2;
    public const string ManualNote = "Preluat manual din ActivityApp, neverificat.";
    private static readonly Regex PhoneCandidate = new(@"\+?[0-9][0-9 .\-]{5,}[0-9]", RegexOptions.Compiled);

    public static string Join(params string[] parts) => string.Join(", ", parts.Select(part => part.Trim()).Where(part => part.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase));

    /// <summary>The first phone number in the text, normalized, or "" when there is none that BlazorStoc accepts.</summary>
    public static string Phone(string text)
    {
        foreach (Match match in PhoneCandidate.Matches(text ?? ""))
        {
            var normalized = BeneficiaryRules.NormalizePhone(match.Value);
            if (BeneficiaryRules.PhonePattern.IsMatch(normalized)) return normalized;
        }
        return "";
    }

    public static string Hash(params string[] fields) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\u001f', fields)))).ToLowerInvariant();

    // The identity of a partner as BlazorStoc keeps it (see BeneficiaryRules.IdentityKey): digits of the CUI for a company, "PF:" + name for a person.
    private static (string Identity, bool Individual, string Cui, string? Problem, bool FormValid) Identify(ActivityPartner partner)
    {
        var raw = BeneficiaryRules.CompactValue(partner.TaxId);
        var name = TextNormalization.ForObjectNameOrCode(partner.Name);
        var upper = raw.ToUpperInvariant();
        var body = upper.StartsWith("RO", StringComparison.Ordinal) ? upper[2..] : upper;
        var digits = new string(body.Where(char.IsAsciiDigit).ToArray());
        // A person (not imported): no tax id (empty, zeros, dashes), a personal numeric code (13 digits, never stored), or letters besides the RO prefix (in ActivityApp the field is often filled
        // with the name of a person, or holds a foreign code).
        var personal = raw.Length == 0 || digits.Trim('0').Length == 0 || (raw.Length == 13 && raw.All(char.IsAsciiDigit)) || body.Any(char.IsLetter);
        if (personal) return ("PF:" + TextNormalization.UniquenessKey(name), true, "", null, false);
        // Only digits (and the RO prefix): a company. A CUI of the wrong form (too long, a dot inside) is still taken, to be verified by a human; it is not asked of ANAF.
        var formValid = BeneficiaryRules.CuiPattern.IsMatch(raw) && SupplierRules.CuiDigits(raw).Length > 0;
        return formValid ? (SupplierRules.CuiDigits(raw), false, SupplierRules.CuiDigits(raw), null, true) : (digits, false, (upper.StartsWith("RO", StringComparison.Ordinal) ? "RO" : "") + digits, null, false);
    }

    private static string KeyOf(Beneficiary beneficiary) => beneficiary.IsIndividual
        ? "PF:" + TextNormalization.UniquenessKey(beneficiary.Name)
        : SupplierRules.CuiDigits(beneficiary.Cui) is { Length: > 0 } digits ? digits : TextNormalization.UniquenessKey(beneficiary.Cui);

    public const string MissingAddress = "Partener fără adresă în ActivityApp; BlazorStoc cere adresa.";
    public const string AnafFilledPrefix = "Completat din ANAF";
    public const string AnafUnverifiedPrefix = "Neverificat în ANAF";
    public const string MissingAddressCheck = "missing_address";
    public const string PersonNotTaken = "Persoană fizică (fără CUI valid): nu se preia; se preiau numai persoanele juridice cu CUI.";

    /// <summary>
    /// The companies (partners with a CUI; never a person) that would be created, to verify them in ANAF (and complete the address or the phone they lack), and those refused only for a missing
    /// address (ANAF may know it): the import asks ANAF for these before it decides.
    /// Returns the digits of their CUI, each once.
    /// </summary>
    public static IReadOnlyList<string> NeedsAnaf(IReadOnlyList<ActivityPartner> partners, IReadOnlyList<Beneficiary> existing, IReadOnlyList<ImportMapEntry> map, IReadOnlySet<long>? only = null) =>
        [.. PlanPartners(partners, existing, map)
            .Where(plan => !plan.Manual && (only is null || only.Contains(plan.SourceId)) && plan.Identity.Length > 0 && !plan.Identity.StartsWith("PF:", StringComparison.Ordinal) && plan.Check != "cui_invalid_form"
                && (plan.Action == PlanAction.Create || plan.Check == MissingAddressCheck))
            .Select(plan => plan.Identity).Distinct(StringComparer.Ordinal)];

    public static IReadOnlyList<PartnerPlan> PlanPartners(IReadOnlyList<ActivityPartner> partners, IReadOnlyList<Beneficiary> existing, IReadOnlyList<ImportMapEntry> map,
        IReadOnlyDictionary<string, CompanyData>? anaf = null)
    {
        var byKey = new Dictionary<string, Beneficiary>(StringComparer.Ordinal);
        foreach (var beneficiary in existing) byKey.TryAdd(KeyOf(beneficiary), beneficiary);
        var byId = existing.ToDictionary(beneficiary => beneficiary.Id);
        var mapped = map.Where(entry => entry.Entity == PartnerEntity).ToDictionary(entry => entry.SourceId, entry => entry.LocalId);
        var included = map.Where(entry => entry.Entity == IncludeEntity).ToDictionary(entry => entry.SourceId, entry => entry.LocalId);
        var identified = partners.Select(partner => (Partner: partner, Info: Identify(partner))).ToList();
        // Repeated identities in ActivityApp: none of them is imported (which one is the right one is not for the import to guess).
        var repeated = identified.Where(item => item.Info.Problem is null && !item.Info.Individual).GroupBy(item => item.Info.Identity).Where(group => group.Count() > 1)
            .ToDictionary(group => group.Key, group => string.Join(", ", group.Select(item => item.Partner.Id)));
        var plans = new List<PartnerPlan>();
        foreach (var (partner, info) in identified)
        {
            var name = TextNormalization.ForObjectNameOrCode(partner.Name);
            var address = Join(partner.Address, partner.City, partner.County);
            var phone = Phone(partner.Phone);
            var manual = included.TryGetValue(partner.Id, out var manualKind) ? manualKind : 0;
            // Included by hand as a person: its identity is the name, whatever the tax id says.
            var identity = manual == IncludeAsPerson ? "PF:" + TextNormalization.UniquenessKey(name) : info.Identity;
            var hash = Hash(name, identity, address, phone);
            PartnerPlan Problem(string message) => new(partner.Id, name, identity, PlanAction.Problem, null, null, message, hash, address);
            if (mapped.TryGetValue(partner.Id, out var local) && (local != 0 || manual == 0))
            {
                if (local == 0) { plans.Add(new PartnerPlan(partner.Id, name, info.Identity, PlanAction.Skipped, null, null, PersonNotTaken, hash, address)); continue; }
                plans.Add(byId.ContainsKey(local)
                    ? new PartnerPlan(partner.Id, name, info.Identity, PlanAction.AlreadyLinked, local, null, "", hash, address)
                    : Problem("Beneficiarul legat la o preluare anterioară nu mai există în BlazorStoc; legătura nu se reface singură."));
                continue;
            }
            // Only companies with a CUI that ANAF can check are taken; a person (no CUI, a personal code, a name in the field) is not imported.
            if (info.Individual && manual == 0) { plans.Add(new PartnerPlan(partner.Id, name, info.Identity, PlanAction.Skipped, null, null, PersonNotTaken, hash, address)); continue; }
            if (manual == IncludeAsCompany && info.Individual) { plans.Add(Problem("Partenerul nu are un CUI cu cifre: poate fi preluat doar ca persoană fizică.")); continue; }
            if (info.Problem is not null) { plans.Add(Problem(info.Problem)); continue; }
            if (name.Length == 0) { plans.Add(Problem("Partener fără denumire în ActivityApp.")); continue; }
            if (manual != IncludeAsPerson && repeated.TryGetValue(info.Identity, out var ids)) { plans.Add(Problem(info.Individual ? $"Nume repetat în ActivityApp (parteneri {ids})." : $"CUI repetat în ActivityApp (parteneri {ids}).")); continue; }
            if (byKey.TryGetValue(identity, out var found)) { plans.Add(new PartnerPlan(partner.Id, name, identity, PlanAction.Link, found.Id, null, $"Există deja în BlazorStoc: «{found.Name}».", hash, address)); continue; }
            if (name.Length > 200) { plans.Add(Problem("Denumirea depășește 200 de caractere.")); continue; }
            // A company is verified in ANAF (and completed there when it lacks the address or the phone); a person is never looked up. A CUI that ANAF does not know is not taken; when ANAF
            // cannot answer (or was not asked), a CUI of a valid form is taken and marked as not verified in ANAF.
            var company = manual == 0 && anaf is not null && anaf.TryGetValue(info.Identity, out var data) ? data : null;
            var data2 = company is { Failure: null } ? company : null;
            // Why the beneficiary needs a human look (it is taken anyway, with its tickets waiting): a CUI of the wrong form; ANAF does not know the CUI; ANAF could not answer (or was not asked).
            // Without any lookup (anaf = null: the plan is only a simulation of the rules) only the form is judged.
            var check = manual != 0 ? "" : !info.FormValid ? "cui_invalid_form" : anaf is null ? "" : company is { NotFound: true } ? "cui_unknown" : data2 is null ? "anaf_unavailable" : "";
            var filled = new List<string>();
            if (address.Length == 0 && data2 is { Address.Length: > 0 }) { address = TextNormalization.ForObjectNameOrCode(data2.Address); if (address.Length > 0) filled.Add("adresa"); }
            if (phone.Length == 0 && data2 is not null && Phone(data2.Phone) is { Length: > 0 } anafPhone) { phone = anafPhone; filled.Add("telefonul"); }
            string Why() => check.Length > 0 ? BeneficiaryCheckReasons.Text(check) : "";
            if (address.Length == 0)
            {
                // No address: no beneficiary can be made; the partner waits in the queue of the ones to verify (its tickets wait too).
                plans.Add(Problem(MissingAddress + (check.Length > 0 ? $" ({Why()})" : "")) with { Check = check.Length > 0 ? check : MissingAddressCheck, CuiTyped = info.Cui });
                continue;
            }
            if (address.Length > 300) address = address[..300];
            var input = manual == IncludeAsPerson ? new BeneficiaryInput { Kind = BeneficiaryKinds.Individual, Name = name, Address = address, Phone = phone }
                : new BeneficiaryInput { Kind = BeneficiaryKinds.Legal, Name = name, Cui = info.Cui.Length > 12 ? info.Cui[..12] : info.Cui, Address = address, Phone = phone, AnafVerified = manual == 0 && anaf is not null && check.Length == 0, NeedsCheck = !info.FormValid };
            var notes = new List<string>();
            if (manual != 0) notes.Add(ManualNote);
            if (filled.Count > 0) notes.Add($"{AnafFilledPrefix}: {string.Join(", ", filled)}.");
            if (check.Length > 0) notes.Add($"De verificat: {Why()}.");
            plans.Add(new PartnerPlan(partner.Id, name, identity, PlanAction.Create, null, input, string.Join(' ', notes), hash, address, check, info.Cui, manual != 0));
        }
        return plans;
    }

    /// <summary>
    /// The work points. <paramref name="localPoints"/> holds the existing work points of the beneficiaries that exist already (key: beneficiary id); a beneficiary to be created has none
    /// and its main point is the one created with it (address = the address of the partner).
    /// </summary>
    public static IReadOnlyList<PointPlan> PlanPoints(IReadOnlyList<ActivityWorkPointRow> points, IReadOnlyList<PartnerPlan> partners, IReadOnlyList<ImportMapEntry> map,
        IReadOnlyDictionary<int, IReadOnlyList<WorkPoint>> localPoints)
    {
        var partnerPlans = partners.ToDictionary(plan => plan.SourceId);
        var mapped = map.Where(entry => entry.Entity == PointEntity).ToDictionary(entry => entry.SourceId, entry => entry.LocalId);
        var mainTaken = new HashSet<long>();
        // A main point already tied by an earlier run is not tied again.
        var tiedLocal = map.Where(entry => entry.Entity == PointEntity).Select(entry => entry.LocalId).ToHashSet();
        foreach (var plan in partners.Where(plan => plan.LocalId is not null))
            if (localPoints.TryGetValue(plan.LocalId!.Value, out var tied) && tied.Any(point => point.IsPrimary && tiedLocal.Contains(point.Id))) mainTaken.Add(plan.SourceId);
        var plans = new List<PointPlan>();
        var used = new Dictionary<long, HashSet<string>>();
        foreach (var point in points.OrderBy(item => item.Id))
        {
            var name = ActivityPointName(point);
            var address = Join(point.Address, point.City, point.County);
            var hash = Hash(name, address, point.Phone, point.Gps, point.Active?.ToString() ?? "", point.Closed?.ToString() ?? "");
            PointPlan Make(PlanAction action, string message, int? local = null, bool toMain = false, WorkPointInput? input = null) => new(point.Id, point.PartnerId, name, action, local, toMain, input, message, hash);
            if (mapped.TryGetValue(point.Id, out var alreadyLocal)) { plans.Add(Make(PlanAction.AlreadyLinked, "", alreadyLocal)); continue; }
            if (!partnerPlans.TryGetValue(point.PartnerId, out var partner)) { plans.Add(Make(PlanAction.Skipped, "Partenerul punctului de lucru nu există în ActivityApp (punct orfan).")); continue; }
            if (partner.Action == PlanAction.Skipped) { plans.Add(Make(PlanAction.Skipped, "Partenerul nu se preia (persoană fizică); punctul nu se preia.")); continue; }
            if (partner.Action == PlanAction.Problem) { plans.Add(Make(PlanAction.Skipped, "Partenerul are o problemă de import; punctul se preia după ce se rezolvă.")); continue; }
            var locals = partner.LocalId is { } id && localPoints.TryGetValue(id, out var list) ? list : [];
            var mainLocal = locals.FirstOrDefault(item => item.IsPrimary);
            // The address of the main point: the one stored for an existing beneficiary, the partner's own for one to be created.
            var mainAddress = mainLocal?.Address ?? (partner.Action == PlanAction.Create ? partner.Input!.Address : "");
            // The point's address is compared as written in full (street, place, county) and as the street alone, with the main address of the beneficiary and, for one that exists, the partner's own.
            var sameAsMain = address.Length > 0 && new[] { address, point.Address }.Any(candidate =>
                AddressNormalization.Same(candidate, mainAddress) || (partner.Action != PlanAction.Create && AddressNormalization.Same(candidate, partner.SourceAddress)));
            if (sameAsMain && !mainTaken.Contains(partner.SourceId))
            {
                mainTaken.Add(partner.SourceId);
                plans.Add(Make(PlanAction.Link, "Punctul principal al beneficiarului.", mainLocal?.Id, true));
                continue;
            }
            if (address.Length == 0) { plans.Add(Make(PlanAction.Problem, "Punct de lucru fără adresă în ActivityApp.")); continue; }
            if (locals.FirstOrDefault(item => !item.IsPrimary && (AddressNormalization.Same(item.Address, address) || AddressNormalization.Same(item.Address, point.Address))) is { } same) { plans.Add(Make(PlanAction.Link, $"Există deja în BlazorStoc: «{same.Name}».", same.Id)); continue; }
            var seen = used.TryGetValue(partner.SourceId, out var set) ? set : used[partner.SourceId] = [];
            if (!seen.Add(AddressNormalization.Key(address))) { plans.Add(Make(PlanAction.Problem, "Altă înregistrare a aceluiași partener are aceeași adresă; BlazorStoc nu admite două puncte cu aceeași adresă.")); continue; }
            if (sameAsMain) { plans.Add(Make(PlanAction.Problem, "Adresa coincide cu cea a punctului principal, deja legat de alt punct din ActivityApp.")); continue; }
            var input = new WorkPointInput { Name = name.Length > 200 ? name[..200] : name, Address = address.Length > 300 ? address[..300] : address, Phone = Phone(point.Phone), Description = Description(point) };
            if (WorkPointCoordinates.TryParse(point.Gps, out var latitude, out var longitude, out _))
            { input.UseCoordinates = true; input.CoordinatesText = WorkPointCoordinates.Format(latitude, longitude); }
            plans.Add(Make(PlanAction.Create, "", null, false, input));
        }
        return plans;
    }

    // The name of the point: the objective when it has one, else the place and the street.
    internal static string ActivityPointName(ActivityWorkPointRow point)
    {
        var name = TextNormalization.ForObjectNameOrCode(point.Objective);
        return name.Length > 0 ? name : TextNormalization.ForObjectNameOrCode(Join(point.City, point.Address));
    }

    // Active / closed are kept as a note, never as a deletion.
    internal static string Description(ActivityWorkPointRow point)
    {
        var notes = new List<string> { $"Preluat din ActivityApp (punct {point.Id})." };
        if (point.Closed == true) notes.Add("Marcat „sistat” în ActivityApp.");
        if (point.Active == false) notes.Add("Marcat inactiv în ActivityApp.");
        return string.Join(' ', notes);
    }
}
