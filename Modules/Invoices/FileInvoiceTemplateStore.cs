using System.Security.Cryptography;
using System.Text.Json;

namespace BlazorStoc.Services;

// Invoice templates kept in a folder, one JSON file per template (<id>.template.json, readable and editable by hand) and the invoice the
// template was made from next to it (<id>.model.pdf). It is the store of the OCR laboratory (tests, the corpus tool): no database, no
// account. It follows the same rules as the MariaDB store: a template is named per supplier, a change is accepted only against the
// version that was read, and a template can be switched off without being deleted.
public sealed class FileInvoiceTemplateStore(string directory) : IInvoiceTemplateStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly object gate = new();

    private sealed class Stored
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string SupplierName { get; set; } = "";
        public string SupplierCui { get; set; } = "";
        public string SourceKind { get; set; } = InvoiceSources.Text;
        public bool Active { get; set; } = true;
        public long Version { get; set; }
        public string CreatedBy { get; set; } = "";
        public DateTime CreatedUtc { get; set; }
        public string UpdatedBy { get; set; } = "";
        public DateTime UpdatedUtc { get; set; }
        public string ModelFileName { get; set; } = "";
        public string ModelSha256 { get; set; } = "";
        public JsonElement Definition { get; set; }
    }

    private string TemplatePath(int id) => Path.Combine(directory, $"{id}.template.json");
    private string ModelPath(int id) => Path.Combine(directory, $"{id}.model.pdf");

    private List<Stored> ReadAll()
    {
        if (!Directory.Exists(directory)) return [];
        var result = new List<Stored>();
        foreach (var file in Directory.GetFiles(directory, "*.template.json"))
            result.Add(JsonSerializer.Deserialize<Stored>(File.ReadAllText(file), Options) ?? throw new InvalidOperationException($"Sablonul {Path.GetFileName(file)} nu poate fi citit."));
        return [.. result.OrderBy(item => item.SupplierName, StringComparer.CurrentCultureIgnoreCase).ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(item => item.Id)];
    }

    private static InvoiceTemplateInfo InfoOf(Stored item) =>
        new(item.Id, item.Name, item.SupplierName, item.SupplierCui, item.SourceKind, item.Active, item.Version, item.CreatedBy, item.CreatedUtc, item.UpdatedBy, item.UpdatedUtc);

    private static InvoiceTemplateRecord RecordOf(Stored item) => new(InfoOf(item), InvoiceTemplateJson.Deserialize(item.Definition.GetRawText()));

    private void Write(Stored item)
    {
        Directory.CreateDirectory(directory);
        var path = TemplatePath(item.Id);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(item, Options));
        File.Move(temporary, path, overwrite: true);
    }

    private static JsonElement DefinitionElement(InvoiceTemplateDefinition definition)
    {
        using var document = JsonDocument.Parse(InvoiceTemplateJson.Serialize(definition));
        return document.RootElement.Clone();
    }

    // The same name for the same supplier cannot be saved twice.
    private static void EnsureUnique(IEnumerable<Stored> all, InvoiceTemplateInput clean, int? exceptId)
    {
        if (all.Any(item => item.Id != exceptId && item.SupplierCui == clean.SupplierCui && InvoiceTemplateRules.SameName(item.Name, clean.Name)))
            throw new InvoiceTemplateOperationException(InvoiceTemplateRules.DuplicateMessage);
    }

    private void SaveModel(int id, Stored item, InvoiceTemplateInput clean)
    {
        if (clean.ModelContent is not { Length: > 0 } content) return;
        var hash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        if (hash == item.ModelSha256) return;   // the same file saved again adds nothing
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(ModelPath(id), content);
        item.ModelFileName = clean.ModelFileName;
        item.ModelSha256 = hash;
    }

    public Task<IReadOnlyList<InvoiceTemplateInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        lock (gate) return Task.FromResult<IReadOnlyList<InvoiceTemplateInfo>>([.. ReadAll().Select(InfoOf)]);
    }

    public Task<InvoiceTemplateRecord?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        lock (gate) return Task.FromResult(ReadAll().FirstOrDefault(item => item.Id == id) is { } found ? RecordOf(found) : null);
    }

    public Task<IReadOnlyList<InvoiceTemplateRecord>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        lock (gate) return Task.FromResult<IReadOnlyList<InvoiceTemplateRecord>>([.. ReadAll().Select(RecordOf)]);
    }

    public Task<InvoiceTemplateModel?> GetModelAsync(int id, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            var item = ReadAll().FirstOrDefault(found => found.Id == id);
            if (item is null || !File.Exists(ModelPath(id))) return Task.FromResult<InvoiceTemplateModel?>(null);
            return Task.FromResult<InvoiceTemplateModel?>(new InvoiceTemplateModel(item.ModelFileName, File.ReadAllBytes(ModelPath(id)), item.ModelSha256, 1, item.CreatedBy, item.CreatedUtc));
        }
    }

    public Task<InvoiceTemplateRecord> CreateAsync(InvoiceTemplateInput input, string actor, CancellationToken cancellationToken = default)
    {
        var clean = InvoiceTemplateRules.Clean(input);
        var definition = clean.Definition ?? throw new InvoiceTemplateOperationException(InvoiceTemplateRules.DefinitionRequiredMessage);
        lock (gate)
        {
            var all = ReadAll();
            EnsureUnique(all, clean, null);
            var now = DateTime.UtcNow;
            var item = new Stored
            {
                Id = all.Count == 0 ? 1 : all.Max(found => found.Id) + 1, Name = clean.Name, SupplierName = clean.SupplierName, SupplierCui = clean.SupplierCui,
                SourceKind = definition.SourceKind, Active = true, Version = 0, CreatedBy = actor, CreatedUtc = now, UpdatedBy = actor, UpdatedUtc = now,
                Definition = DefinitionElement(definition)
            };
            SaveModel(item.Id, item, clean);
            Write(item);
            return Task.FromResult(RecordOf(item));
        }
    }

    private Stored Current(InvoiceTemplateInfo original, List<Stored> all) =>
        all.FirstOrDefault(item => item.Id == original.Id && item.Version == original.Version) ?? throw new InvoiceTemplateOperationException(InvoiceTemplateRules.ConcurrentMessage);

    public Task<InvoiceTemplateRecord> SaveAsync(InvoiceTemplateInfo original, InvoiceTemplateInput input, string actor, CancellationToken cancellationToken = default)
    {
        var clean = InvoiceTemplateRules.Clean(input);
        var definition = clean.Definition ?? throw new InvoiceTemplateOperationException(InvoiceTemplateRules.DefinitionRequiredMessage);
        lock (gate)
        {
            var all = ReadAll();
            var item = Current(original, all);
            EnsureUnique(all, clean, item.Id);
            item.Name = clean.Name; item.SupplierName = clean.SupplierName; item.SupplierCui = clean.SupplierCui; item.SourceKind = definition.SourceKind;
            item.Definition = DefinitionElement(definition);
            item.Version++; item.UpdatedBy = actor; item.UpdatedUtc = DateTime.UtcNow;
            SaveModel(item.Id, item, clean);
            Write(item);
            return Task.FromResult(RecordOf(item));
        }
    }

    public Task<InvoiceTemplateInfo> SetActiveAsync(InvoiceTemplateInfo original, bool active, string actor, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            var item = Current(original, ReadAll());
            item.Active = active; item.Version++; item.UpdatedBy = actor; item.UpdatedUtc = DateTime.UtcNow;
            Write(item);
            return Task.FromResult(InfoOf(item));
        }
    }

    public Task<InvoiceTemplateInfo> UpdateDetailsAsync(InvoiceTemplateInfo original, InvoiceTemplateInput input, string actor, CancellationToken cancellationToken = default)
    {
        var clean = InvoiceTemplateRules.Clean(input);
        lock (gate)
        {
            var all = ReadAll();
            var item = Current(original, all);
            EnsureUnique(all, clean, item.Id);
            item.Name = clean.Name; item.SupplierName = clean.SupplierName; item.SupplierCui = clean.SupplierCui;
            item.Version++; item.UpdatedBy = actor; item.UpdatedUtc = DateTime.UtcNow;
            Write(item);
            return Task.FromResult(InfoOf(item));
        }
    }

    public Task DeleteAsync(InvoiceTemplateInfo original, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            var item = Current(original, ReadAll());
            File.Delete(TemplatePath(item.Id));
            if (File.Exists(ModelPath(item.Id))) File.Delete(ModelPath(item.Id));
            return Task.CompletedTask;
        }
    }
}
