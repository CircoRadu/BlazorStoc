using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BlazorStoc.Services;

// Configurator for the ANAF (VAT register) lookup, ported from Prototip-ANAF: connection, request template,
// field mapping, live test and versioned activation. The mapping policies (missing / update) are stored as
// metadata for the future company update flow; this module never changes company data.

public sealed class AnafException(string message) : Exception(message);

public sealed class AnafHeader
{
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
    public bool Enabled { get; set; } = true;
}

public sealed class AnafMapping
{
    public string Label { get; set; } = "";
    public string Path { get; set; } = "$.";
    public string Type { get; set; } = AnafRules.TypeText;
    public bool Required { get; set; }
    public string Missing { get; set; } = "keep";
    public string Update { get; set; } = "confirm";
}

public sealed class AnafConfig
{
    public string Name { get; set; } = "ANAF – Date contribuabil";
    public bool Enabled { get; set; } = true;
    public string Url { get; set; } = AnafRules.DefaultUrl;
    public string Method { get; set; } = "POST";
    public int Timeout { get; set; } = 30;
    public int Interval { get; set; } = 1000;
    public int MaxBatch { get; set; } = 100;
    public bool StripRO { get; set; } = true;
    public bool Trim { get; set; } = true;
    public string DateFormat { get; set; } = "yyyy-MM-dd";
    public string Found { get; set; } = "$.found";
    public string NotFound { get; set; } = "$.notFound";
    public string Identity { get; set; } = "$.date_generale.cui";
    public List<AnafHeader> Headers { get; set; } =
    [
        new() { Name = "Content-Type", Value = "application/json" },
        new() { Name = "Accept", Value = "application/json" }
    ];
    public string Template { get; set; } = "[\n  {\n    \"cui\": {{cui}},\n    \"data\": \"{{data_interogare}}\"\n  }\n]";
    public List<AnafMapping> Mappings { get; set; } = AnafRules.DefaultMappings();

    public AnafConfig Clone() => JsonSerializer.Deserialize<AnafConfig>(JsonSerializer.Serialize(this))!;
}

public sealed class AnafVersion
{
    public int Id { get; set; }
    public DateTimeOffset At { get; set; }
    public string Admin { get; set; } = "";
    public AnafConfig Config { get; set; } = new();
    public int? RestoredFrom { get; set; }
}

public sealed class AnafState
{
    public AnafConfig Draft { get; set; } = new();
    public AnafVersion? Active { get; set; }
    public List<AnafVersion> History { get; set; } = [];
}

public sealed record AnafRequest(string Url, string Method, IReadOnlyDictionary<string, string> Headers, string Body, string Cui);
public sealed record AnafCompany(string Cui, string Name, string Address, string RegistryNumber, string Phone, string PostalCode, string CaenCode);
// Found: the company data. NotFound: ANAF answered and does not know the CUI (not an outage). InvalidCui: the text is not a CUI.
// Unavailable: ANAF could not answer or the integration cannot be used (not configured, disabled, error status, timeout, an answer the
// configuration cannot read): the data may be typed by hand and is marked as such.
public enum AnafLookupOutcome { Found, NotFound, InvalidCui, Unavailable }

// Updates: the policy "when ANAF gives another value than the form holds" of each mapped field (label -> confirm / empty / overwrite).
public sealed record AnafCompanyResult(AnafCompany? Company, string? Error, string? Warning, AnafLookupOutcome Outcome = AnafLookupOutcome.Found,
    IReadOnlyDictionary<string, string>? Updates = null);
public sealed record AnafMappedValue(string Label, string Path, string Type, string? Value);
// CuiNotFound: ANAF answered normally and listed the requested CUI among those it does not know.
public sealed record AnafTestResult(bool Ok, int? StatusCode, long DurationMs, AnafRequest? Request, string Raw,
    IReadOnlyList<AnafMappedValue> Mapped, IReadOnlyList<string> Errors, DateTimeOffset At, bool CuiNotFound = false);

public static partial class AnafRules
{
    public const string DefaultUrl = "https://webservicesp.anaf.ro/api/PlatitorTvaRest/v9/tva";
    public const string ApprovedHost = "webservicesp.anaf.ro";
    public const string TypeText = "text";
    public const string TypeBoolean = "boolean";
    public const int MaxResponseBytes = 2_000_000;
    public const int FormTimeoutSeconds = 12;
    public static readonly string[] Methods = ["POST", "PUT", "GET"];
    public static readonly string[] DateFormats = ["yyyy-MM-dd", "yyyyMMdd", "dd.MM.yyyy"];
    private static readonly string[] ReservedHeaders = ["host", "content-length", "connection", "authorization", "cookie", "transfer-encoding"];

    [GeneratedRegex(@"^\$(?:\.[A-Za-z_][A-Za-z_0-9]*|\[\d+\])*$")] private static partial Regex PathPattern();
    [GeneratedRegex(@"\.([A-Za-z_][A-Za-z_0-9]*)|\[(\d+)\]")] private static partial Regex PathSegment();
    [GeneratedRegex(@"^[A-Za-z0-9-]+$")] private static partial Regex HeaderName();
    [GeneratedRegex(@"^[1-9][0-9]{1,9}$")] private static partial Regex CuiPattern();

    public static List<AnafMapping> DefaultMappings() =>
    [
        Map("CUI", "date_generale.cui", TypeText, true), Map("Denumire", "date_generale.denumire", TypeText, true),
        Map("Adresă fiscală", "date_generale.adresa", TypeText), Map("Nr. Registrul Comerțului", "date_generale.nrRegCom", TypeText, update: "overwrite"),
        Map("Telefon", "date_generale.telefon", TypeText, update: "empty"), Map("Cod poștal", "date_generale.codPostal", TypeText),
        Map("Cod CAEN", "date_generale.cod_CAEN", TypeText, update: "overwrite"), Map("Plătitor TVA", "inregistrare_scop_Tva.scpTVA", TypeBoolean),
        Map("TVA la încasare", "inregistrare_RTVAI.statusTvaIncasare", TypeBoolean), Map("Inactiv fiscal", "stare_inactiv.statusInactivi", TypeBoolean),
        Map("RO e-Factura", "date_generale.statusRO_e_Factura", TypeBoolean)
    ];

    private static AnafMapping Map(string label, string path, string type, bool required = false, string update = "confirm") =>
        new() { Label = label, Path = "$." + path, Type = type, Required = required, Update = update };

    /// <summary>What a mapped field is used for: the value fills a form, raises a warning, or is only shown when the configuration is tested.</summary>
    public static string Usage(string? label) =>
        AnafApplyRules.FormLabels.Contains(label ?? "", StringComparer.OrdinalIgnoreCase) ? "Formular"
        : string.Equals(label, "Inactiv fiscal", StringComparison.OrdinalIgnoreCase) ? "Avertisment"
        : string.Equals(label, "CUI", StringComparison.OrdinalIgnoreCase) ? "Identificare" : "Doar informativ";

    // What the user (and the administrator testing the configuration) reads when ANAF answers with an error status.
    public static string HttpErrorMessage(int status) => status switch
    {
        400 => "ANAF a respins interogarea (HTTP 400): cererea nu este în formatul așteptat sau CUI-ul este invalid. Verifică CUI-ul; dacă este corect, administratorul trebuie să verifice șablonul cererii din Setări → Preluare date ANAF. Datele se pot completa și manual.",
        401 or 403 => $"ANAF a refuzat accesul (HTTP {status}). Serviciul nu poate fi folosit momentan de aplicație; anunță administratorul. Datele se pot completa manual.",
        404 => "Serviciul ANAF nu a fost găsit la adresa configurată (HTTP 404). Nu este vorba despre CUI-ul introdus: adresa serviciului din Setări → Preluare date ANAF este greșită sau ANAF a mutat/retras serviciul. Anunță administratorul; între timp datele se pot completa manual.",
        429 => "ANAF a limitat numărul de interogări (HTTP 429). Așteaptă câteva secunde și încearcă din nou.",
        >= 500 => $"Serviciul ANAF are momentan o problemă (HTTP {status}). Încearcă din nou peste câteva minute sau completează datele manual.",
        _ => $"ANAF a răspuns cu HTTP {status}. Încearcă din nou sau completează datele manual; dacă se repetă, anunță administratorul."
    };

    public static bool IsValidPath(string? path) => path is not null && PathPattern().IsMatch(path);

    public static JsonElement? Pick(JsonElement element, string path)
    {
        if (!IsValidPath(path)) throw new AnafException($"Cale nevalidă: {path}. Folosește $.camp.subcamp sau [0].");
        var current = element;
        foreach (Match segment in PathSegment().Matches(path))
        {
            if (segment.Groups[1].Success)
            {
                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment.Groups[1].Value, out current)) return null;
            }
            else
            {
                var index = int.Parse(segment.Groups[2].Value, CultureInfo.InvariantCulture);
                if (current.ValueKind != JsonValueKind.Array || index >= current.GetArrayLength()) return null;
                current = current[index];
            }
        }
        return current.ValueKind == JsonValueKind.Null ? null : current;
    }

    /// <summary>Returns the first problem found in the configuration, or null when it is valid.</summary>
    public static string? Validate(AnafConfig c)
    {
        if (string.IsNullOrWhiteSpace(c.Name)) return "Completează denumirea integrării.";
        if (!Uri.TryCreate(c.Url, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(url.Host, ApprovedHost, StringComparison.OrdinalIgnoreCase) || !url.IsDefaultPort ||
            url.UserInfo.Length > 0 || url.Fragment.Length > 0)
            return $"Destinație neaprobată. Sunt acceptate doar adrese HTTPS către {ApprovedHost}, port 443.";
        if (!Methods.Contains(c.Method)) return "Metoda HTTP nu este acceptată.";
        if (c.Timeout is < 1 or > 60) return "Timeout: valoarea trebuie să fie între 1 și 60 de secunde.";
        if (c.Interval is < 1000 or > 60000) return "Interval: valoarea trebuie să fie între 1000 și 60000 ms.";
        if (c.MaxBatch is < 1 or > 100) return "Lot maxim: valoarea trebuie să fie între 1 și 100.";
        if (!DateFormats.Contains(c.DateFormat)) return "Format de dată neacceptat.";
        foreach (var path in new[] { c.Found, c.NotFound, c.Identity })
            if (!IsValidPath(path)) return $"Cale nevalidă: {path}. Folosește $.camp.subcamp sau [0].";
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in c.Headers.Where(h => h.Enabled))
        {
            if (!HeaderName().IsMatch(h.Name ?? "") || (h.Value ?? "").Contains('\r') || (h.Value ?? "").Contains('\n')) return "Antet HTTP nevalid.";
            if (!names.Add(h.Name!) || ReservedHeaders.Contains(h.Name!.ToLowerInvariant())) return "Antet duplicat sau rezervat: " + h.Name;
        }
        if (c.Mappings.Count == 0) return "Adaugă cel puțin o mapare.";
        var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in c.Mappings)
        {
            if (!IsValidPath(m.Path)) return $"Cale nevalidă: {m.Path}. Folosește $.camp.subcamp sau [0].";
            if (string.IsNullOrWhiteSpace(m.Label) || !labels.Add(m.Label.Trim())) return "Denumirile câmpurilor trebuie să fie completate și distincte.";
            if (m.Type is not (TypeText or TypeBoolean) || m.Missing is not ("keep" or "clear" or "error") || m.Update is not ("empty" or "overwrite" or "confirm"))
                return "Politică de mapare nevalidă.";
        }
        return null;
    }

    public static string? NormalizeCui(AnafConfig c, string? input)
    {
        var value = input ?? "";
        if (c.Trim) value = value.Trim();
        if (c.StripRO && value.StartsWith("RO", StringComparison.OrdinalIgnoreCase)) value = value[2..];
        return CuiPattern().IsMatch(value) ? value : null;
    }

    public static AnafRequest Prepare(AnafConfig c, string? cui, DateOnly date)
    {
        if (Validate(c) is { } problem) throw new AnafException(problem);
        var normalized = NormalizeCui(c, cui) ??
            throw new AnafException("CUI-ul trebuie să conțină între 2 și 10 cifre, fără zero inițial. Activează eliminarea prefixului RO dacă este necesar.");
        var formatted = date.ToString(c.DateFormat.Replace("dd.MM.yyyy", "dd'.'MM'.'yyyy"), CultureInfo.InvariantCulture);
        var body = c.Template.Replace("{{cui}}", normalized).Replace("{{data_interogare}}", formatted);
        if (body.Contains("{{") || body.Contains("}}"))
            throw new AnafException("Variabilă necunoscută. Sunt disponibile {{cui}} și {{data_interogare}}.");
        JsonElement parsed;
        try { parsed = JsonDocument.Parse(body).RootElement; }
        catch (JsonException ex) { throw new AnafException($"JSON nevalid la linia {(ex.LineNumber ?? 0) + 1}, coloana {(ex.BytePositionInLine ?? 0) + 1}."); }
        if (parsed.ValueKind != JsonValueKind.Array || parsed.GetArrayLength() < 1 || parsed.GetArrayLength() > c.MaxBatch)
            throw new AnafException("Corpul cererii trebuie să fie o listă JSON în limita configurată.");
        var headers = c.Headers.Where(h => h.Enabled).ToDictionary(h => h.Name, h => h.Value ?? "", StringComparer.OrdinalIgnoreCase);
        return new AnafRequest(c.Url, c.Method, headers, JsonSerializer.Serialize(parsed), normalized);
    }

    public static string Fingerprint(AnafConfig c) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(c))));

    // An entry of the "not found" list: the CUI itself, or an object that carries it.
    private static string? UnknownCui(JsonElement item) => item.ValueKind switch
    {
        JsonValueKind.Number or JsonValueKind.String => item.ToString(),
        JsonValueKind.Object when item.TryGetProperty("cui", out var inner) => inner.ToString(),
        _ => null
    };

    public static AnafTestResult Interpret(AnafConfig c, AnafRequest request, int status, long durationMs, string raw)
    {
        var now = DateTimeOffset.Now;
        JsonDocument document;
        try { document = JsonDocument.Parse(raw); }
        catch (JsonException) { return new(false, status, durationMs, request, raw, [], ["ANAF nu a returnat un răspuns JSON valid."], now); }
        using (document)
        {
            var root = document.RootElement;
            var errors = new List<string>();
            var found = Pick(root, c.Found);
            if (found is not { ValueKind: JsonValueKind.Array }) errors.Add("Calea listei de rezultate nu returnează o listă: " + c.Found);
            if (Pick(root, c.NotFound) is not { ValueKind: JsonValueKind.Array }) errors.Add("Calea CUI-urilor negăsite nu returnează o listă: " + c.NotFound);
            JsonElement? match = null;
            if (found is { ValueKind: JsonValueKind.Array } list)
                foreach (var item in list.EnumerateArray())
                    if (item.ValueKind == JsonValueKind.Object && Pick(item, c.Identity) is { } id && id.ToString() == request.Cui) { match = item; break; }
            // The CUI is among those ANAF reports as unknown (the list holds numbers or objects with the CUI).
            var cuiNotFound = match is null && Pick(root, c.NotFound) is { ValueKind: JsonValueKind.Array } unknown &&
                unknown.EnumerateArray().Any(item => UnknownCui(item) == request.Cui);
            if (match is null) errors.Add(cuiNotFound ? "CUI-ul solicitat nu este înregistrat în ANAF." : "CUI-ul solicitat nu a fost găsit prin maparea configurată.");
            var mapped = new List<AnafMappedValue>();
            if (match is not null)
                foreach (var m in c.Mappings)
                {
                    var value = Pick(match.Value, m.Path);
                    var absent = value is null || value.Value.ValueKind == JsonValueKind.String && value.Value.GetString() == "";
                    if (absent && (m.Required || m.Missing == "error")) errors.Add($"Lipsește câmpul „{m.Label}”: {m.Path}");
                    string? text = null;
                    if (value is { } v)
                    {
                        if (m.Type == TypeBoolean)
                        {
                            if (v.ValueKind is JsonValueKind.True or JsonValueKind.False) text = v.GetBoolean() ? "Da" : "Nu";
                            else errors.Add($"Câmpul „{m.Label}” nu este boolean.");
                        }
                        else if (v.ValueKind is JsonValueKind.Object or JsonValueKind.Array) errors.Add($"Câmpul „{m.Label}” nu este scalar.");
                        else text = v.ToString();
                    }
                    mapped.Add(new AnafMappedValue(m.Label, m.Path, m.Type, text));
                }
            var pretty = JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            return new(errors.Count == 0, status, durationMs, request, pretty, mapped, errors, now, cuiNotFound);
        }
    }
}

public interface IAnafService
{
    Task<AnafState> GetStateAsync(CancellationToken cancellationToken = default);
    Task<AnafRequest> PreviewAsync(AnafConfig config, string cui, CancellationToken cancellationToken = default);
    Task<AnafTestResult> TestAsync(AnafConfig config, string cui, CancellationToken cancellationToken = default);
    Task<AnafCompanyResult> LookupCompanyAsync(string cui, CancellationToken cancellationToken = default);
    Task SaveDraftAsync(AnafConfig config, CancellationToken cancellationToken = default);
    Task<AnafState> ActivateAsync(AnafConfig config, CancellationToken cancellationToken = default);
    Task<AnafState> RollbackAsync(CancellationToken cancellationToken = default);
    /// <summary>Makes an older version the active one again (a new version is appended as a copy of it; it was tested when it was first activated).</summary>
    Task<AnafState> ActivateVersionAsync(int versionId, CancellationToken cancellationToken = default);
    /// <summary>Deletes a version of the history; the active version cannot be deleted. The reason is mandatory and the journal names the version.</summary>
    Task<AnafState> DeleteVersionAsync(int versionId, string reason, CancellationToken cancellationToken = default);
    bool WasTestedSuccessfully(AnafConfig config);
}

/// <summary>Single-file JSON store (data/anaf-configuration.json) plus the process-wide test/rate-limit state.</summary>
public sealed class AnafStore(IConfiguration configuration)
{
    public SemaphoreSlim FileGate { get; } = new(1, 1);
    public SemaphoreSlim RateGate { get; } = new(1, 1);
    public long LastCall;
    public HashSet<string> Tested { get; } = [];
    private readonly string path = Path.GetFullPath(configuration["Anaf:ConfigurationPath"] ?? Path.Combine("data", "anaf-configuration.json"));
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public async Task<AnafState> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return new AnafState();
        await using var stream = File.OpenRead(path);
        var state = await JsonSerializer.DeserializeAsync<AnafState>(stream, Options, cancellationToken) ?? new AnafState();
        // The policy "Golește" (a missing field empties the form field) was removed: older files read as "Păstrează".
        foreach (var config in new[] { state.Draft, state.Active?.Config }.Concat(state.History.Select(version => version.Config)))
            foreach (var mapping in config?.Mappings ?? []) if (mapping.Missing == "clear") mapping.Missing = "keep";
        return state;
    }

    public async Task WriteAsync(AnafState state, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(state, Options), new UTF8Encoding(false), cancellationToken);
        File.Move(temp, path, true);
    }
}

public sealed class AnafService(AnafStore store, IHttpClientFactory httpClientFactory, IAccessControl access, IAuditTrail auditTrail,
    ILogger<AnafService> logger) : IAnafService
{
    public const string ClientName = "anaf";

    public async Task<AnafState> GetStateAsync(CancellationToken cancellationToken = default)
    {
        await access.EnsureAdministratorAsync(cancellationToken);
        await store.FileGate.WaitAsync(cancellationToken);
        try
        {
            var state = await store.ReadAsync(cancellationToken);
            // Only a configuration that passed the test could be activated, so the saved versions count as tested again after a restart (the set is in memory).
            lock (store.Tested)
                foreach (var version in state.History.Append(state.Active).OfType<AnafVersion>()) store.Tested.Add(AnafRules.Fingerprint(version.Config));
            return state;
        }
        finally { store.FileGate.Release(); }
    }

    public async Task<AnafRequest> PreviewAsync(AnafConfig config, string cui, CancellationToken cancellationToken = default)
    {
        await access.EnsureAdministratorAsync(cancellationToken);
        return AnafRules.Prepare(config, cui, DateOnly.FromDateTime(DateTime.Today));
    }

    public bool WasTestedSuccessfully(AnafConfig config)
    {
        lock (store.Tested) return store.Tested.Contains(AnafRules.Fingerprint(config));
    }

    public async Task<AnafTestResult> TestAsync(AnafConfig config, string cui, CancellationToken cancellationToken = default)
    {
        await access.EnsureAdministratorAsync(cancellationToken);
        var request = AnafRules.Prepare(config, cui, DateOnly.FromDateTime(DateTime.Today));
        if (!config.Enabled) throw new AnafException("Integrarea este dezactivată. Activează comutatorul pentru testare.");
        var result = await SendAsync(config, request, cancellationToken);
        if (result.Ok) lock (store.Tested) store.Tested.Add(AnafRules.Fingerprint(config));
        return result;
    }

    // Beneficiary forms: uses the ACTIVE configuration only (never a draft) and needs the beneficiary-operator role, not
    // the administrator role, so every user who can add a beneficiary can pull the company data.
    public async Task<AnafCompanyResult> LookupCompanyAsync(string cui, CancellationToken cancellationToken = default)
    {
        await access.EnsureBeneficiaryOperatorAsync(cancellationToken);
        AnafState state;
        await store.FileGate.WaitAsync(cancellationToken);
        try { state = await store.ReadAsync(cancellationToken); }
        finally { store.FileGate.Release(); }
        if (state.Active is not { } active)
            return new(null, "Preluarea din ANAF nu este configurată. Un administrator trebuie să activeze o configurație în Setări → Preluare date ANAF.", null, AnafLookupOutcome.Unavailable);
        var config = active.Config;
        if (!config.Enabled) return new(null, "Integrarea ANAF este dezactivată de administrator.", null, AnafLookupOutcome.Unavailable);
        // Someone is waiting in a form: a slow ANAF is given the time of a form, not the (longer) one of the configuration test. The state was
        // just read from the file, so this copy is ours.
        config.Timeout = Math.Min(config.Timeout, AnafRules.FormTimeoutSeconds);
        // A text that is not a CUI is the user's mistake, not an ANAF outage: nothing is sent.
        if (AnafRules.NormalizeCui(config, cui) is null)
            return new(null, "CUI-ul trebuie să conțină între 2 și 10 cifre, fără zero inițial.", null, AnafLookupOutcome.InvalidCui);
        AnafTestResult result;
        try
        {
            var request = AnafRules.Prepare(config, cui, DateOnly.FromDateTime(DateTime.Today));
            result = await SendAsync(config, request, cancellationToken);
        }
        catch (AnafException ex) { return new(null, ex.Message, null, AnafLookupOutcome.Unavailable); }
        if (result.CuiNotFound) return new(null, "CUI-ul nu este înregistrat în ANAF. Datele se pot introduce manual.", null, AnafLookupOutcome.NotFound);
        if (!result.Ok) return new(null, result.Errors.FirstOrDefault() ?? "Interogarea ANAF a eșuat.", null, AnafLookupOutcome.Unavailable);
        string Value(string label) => result.Mapped.FirstOrDefault(m => string.Equals(m.Label, label, StringComparison.OrdinalIgnoreCase))?.Value ?? "";
        var company = new AnafCompany(result.Request!.Cui, Value("Denumire"), Value("Adresă fiscală"), Value("Nr. Registrul Comerțului"),
            Value("Telefon"), Value("Cod poștal"), Value("Cod CAEN"));
        var warning = Value("Inactiv fiscal") == "Da" ? "Atenție: ANAF raportează firma ca inactivă fiscal." : null;
        var updates = config.Mappings.Where(m => m.Label.Length > 0).GroupBy(m => m.Label, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Update, StringComparer.OrdinalIgnoreCase);
        return new(company, null, warning, AnafLookupOutcome.Found, updates);
    }

    private async Task<AnafTestResult> SendAsync(AnafConfig config, AnafRequest request, CancellationToken cancellationToken)
    {
        // ANAF allows about one request per second: calls are serialized process-wide and spaced by the configured interval.
        await store.RateGate.WaitAsync(cancellationToken);
        try
        {
            var last = Interlocked.Read(ref store.LastCall);
            if (last != 0)
            {
                var wait = TimeSpan.FromMilliseconds(config.Interval) - Stopwatch.GetElapsedTime(last);
                if (wait > TimeSpan.Zero) await Task.Delay(wait, cancellationToken);
            }
            var watch = Stopwatch.StartNew();
            try
            {
                using var client = httpClientFactory.CreateClient(ClientName);
                using var message = new HttpRequestMessage(new HttpMethod(request.Method), request.Url);
                var contentType = "application/json";
                foreach (var (name, value) in request.Headers)
                {
                    if (name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)) contentType = value;
                    else message.Headers.TryAddWithoutValidation(name, value);
                }
                message.Content = new StringContent(request.Body, Encoding.UTF8, MediaTypeHeaderValue.TryParse(contentType, out var parsed) ? parsed.MediaType! : "application/json");
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(config.Timeout));
                using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                var status = (int)response.StatusCode;
                await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
                using var buffer = new MemoryStream();
                var chunk = new byte[16 * 1024];
                int read;
                while ((read = await stream.ReadAsync(chunk, timeout.Token)) > 0)
                {
                    buffer.Write(chunk, 0, read);
                    if (buffer.Length > AnafRules.MaxResponseBytes) throw new AnafException("Răspunsul depășește limita de 2 MB.");
                }
                var raw = Encoding.UTF8.GetString(buffer.ToArray()).TrimStart('﻿');
                if (!response.IsSuccessStatusCode)
                {
                    // ANAF answers HTTP 404 WITH its normal body ({"found":[],"notFound":[cui]}) when the CUI does not exist: that is "CUI not registered", not a wrong address.
                    if (status == 404 && AnafRules.Interpret(config, request, status, watch.ElapsedMilliseconds, raw) is { CuiNotFound: true } unknown) return unknown;
                    return new(false, status, watch.ElapsedMilliseconds, request, raw, [], [AnafRules.HttpErrorMessage(status)], DateTimeOffset.Now);
                }
                return AnafRules.Interpret(config, request, status, watch.ElapsedMilliseconds, raw);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new AnafException("ANAF nu a răspuns în timpul configurat.");
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning(ex, "ANAF request failed");
                throw new AnafException("Serviciul ANAF nu este disponibil.");
            }
            finally { Interlocked.Exchange(ref store.LastCall, Stopwatch.GetTimestamp()); }
        }
        finally { store.RateGate.Release(); }
    }

    public async Task SaveDraftAsync(AnafConfig config, CancellationToken cancellationToken = default)
    {
        await access.EnsureAdministratorAsync(cancellationToken);
        AnafRules.Prepare(config, "9178894", DateOnly.FromDateTime(DateTime.Today));
        await store.FileGate.WaitAsync(cancellationToken);
        try
        {
            var state = await store.ReadAsync(cancellationToken);
            state.Draft = config.Clone();
            await store.WriteAsync(state, cancellationToken);
        }
        finally { store.FileGate.Release(); }
    }

    public async Task<AnafState> ActivateAsync(AnafConfig config, CancellationToken cancellationToken = default)
    {
        await access.EnsureAdministratorAsync(cancellationToken);
        if (AnafRules.Validate(config) is { } problem) throw new AnafException(problem);
        if (!WasTestedSuccessfully(config)) throw new AnafException("Testează cu succes această configurație înainte de activare.");
        return await AppendVersionAsync(state => (config.Clone(), null), "Activare configurație", cancellationToken);
    }

    public async Task<AnafState> RollbackAsync(CancellationToken cancellationToken = default)
    {
        await access.EnsureAdministratorAsync(cancellationToken);
        return await AppendVersionAsync(state =>
        {
            if (state.History.Count < 2) throw new AnafException("Nu există o versiune anterioară.");
            var previous = state.History[^2];
            return (previous.Config.Clone(), previous.Id);
        }, "Revenire la versiunea precedentă", cancellationToken);
    }

    public async Task<AnafState> ActivateVersionAsync(int versionId, CancellationToken cancellationToken = default)
    {
        await access.EnsureAdministratorAsync(cancellationToken);
        return await AppendVersionAsync(state =>
        {
            var version = state.History.FirstOrDefault(item => item.Id == versionId) ?? throw new AnafException("Versiunea nu mai există. Actualizează lista.");
            if (state.Active?.Id == versionId) throw new AnafException("Versiunea este deja activă.");
            return (version.Config.Clone(), version.Id);
        }, $"Activare versiune #{versionId}", cancellationToken, AuditActions.ActivateAnafVersion);
    }

    public async Task<AnafState> DeleteVersionAsync(int versionId, string reason, CancellationToken cancellationToken = default)
    {
        await access.EnsureAdministratorAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(reason)) throw new AnafException("Motivul ștergerii este obligatoriu.");
        await store.FileGate.WaitAsync(cancellationToken);
        try
        {
            var state = await store.ReadAsync(cancellationToken);
            var version = state.History.FirstOrDefault(item => item.Id == versionId) ?? throw new AnafException("Versiunea nu mai există. Actualizează lista.");
            if (state.Active?.Id == versionId) throw new AnafException("Versiunea activă nu se poate șterge. Activează mai întâi o altă versiune.");
            state.History.Remove(version);
            await store.WriteAsync(state, cancellationToken);
            await AuditRecorder.RecordEditAsync(auditTrail, access, "IntegrareANAF", versionId.ToString(CultureInfo.InvariantCulture), "Configurație ANAF",
                [new AuditChange("Versiune", $"#{versionId} ({version.At:dd.MM.yyyy HH:mm}, {version.Admin})", "ștearsă"), new AuditChange("Versiune activă", $"#{state.Active?.Id}", $"#{state.Active?.Id}")],
                reason.Trim(), cancellationToken, AuditActions.DeleteAnafVersion);
            return state;
        }
        finally { store.FileGate.Release(); }
    }

    private async Task<AnafState> AppendVersionAsync(Func<AnafState, (AnafConfig Config, int? RestoredFrom)> select, string action, CancellationToken cancellationToken, string? journalAction = null)
    {
        var admin = await access.GetUsernameAsync(cancellationToken) ?? "Administrator";
        await store.FileGate.WaitAsync(cancellationToken);
        try
        {
            var state = await store.ReadAsync(cancellationToken);
            var (config, restoredFrom) = select(state);
            var version = new AnafVersion { Id = state.History.Count == 0 ? 1 : state.History.Max(item => item.Id) + 1, At = DateTimeOffset.Now, Admin = admin, Config = config, RestoredFrom = restoredFrom };
            state.History.Add(version);
            state.Active = version;
            state.Draft = config.Clone();
            await store.WriteAsync(state, cancellationToken);
            await AuditRecorder.RecordEditAsync(auditTrail, access, "IntegrareANAF", version.Id.ToString(CultureInfo.InvariantCulture), "Configurație ANAF",
                [new AuditChange("Versiune activă", (state.History.Count > 1 ? state.History[^2].Id.ToString(CultureInfo.InvariantCulture) : "—"), version.Id.ToString(CultureInfo.InvariantCulture))],
                action, cancellationToken, journalAction);
            return state;
        }
        finally { store.FileGate.Release(); }
    }
}
