namespace BlazorStoc.Services;

// In-memory stand-in of the tag store (component checks): the same rules for names, tags kept per product.
public sealed class DemoProductTagRepository : IProductTagRepository
{
    private readonly object gate = new();
    private readonly List<SubcategoryTag> tags = [];
    private readonly Dictionary<int, HashSet<int>> byProduct = [];
    private int nextId = 1;

    private int CountOf(int tagId) => byProduct.Values.Count(set => set.Contains(tagId));

    public Task<IReadOnlyList<SubcategoryTag>> GetTagsAsync(CancellationToken cancellationToken = default)
    {
        lock (gate) return Task.FromResult<IReadOnlyList<SubcategoryTag>>(tags.Select(tag => tag with { ProductCount = CountOf(tag.Id) }).ToArray());
    }

    public Task<SubcategoryTag> AddTagAsync(ProductGroup group, string name, CancellationToken cancellationToken = default)
    {
        var cleanName = ProductTagRules.Name(name);
        lock (gate)
        {
            if (tags.Any(item => item.Category == group.Category && item.Subcategory == group.Subcategory && TextNormalization.SameUniqueValue(item.Name, cleanName)))
                throw new ProductOperationException($"Subcategoria «{group.Subcategory}» are deja o etichetă «{cleanName}».");
            var added = new SubcategoryTag(nextId++, group.Category, group.Subcategory, cleanName, tags.Count(item => item.Category == group.Category && item.Subcategory == group.Subcategory) + 1, 0, 0);
            tags.Add(added);
            return Task.FromResult(added);
        }
    }

    public Task<SubcategoryTag> RenameTagAsync(SubcategoryTag original, string name, string reason, CancellationToken cancellationToken = default)
    {
        var cleanName = ProductTagRules.Name(name);
        lock (gate)
        {
            var index = tags.FindIndex(item => item.Id == original.Id);
            if (index < 0) throw new ProductOperationException("Eticheta nu mai există. Actualizează lista și reia operația.");
            tags[index] = tags[index] with { Name = cleanName, Version = original.Version + 1 };
            return Task.FromResult(tags[index]);
        }
    }

    public Task DeleteTagAsync(SubcategoryTag original, string reason, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            if (CountOf(original.Id) > 0) throw new ProductOperationException(ParameterArchiveRules.UsedCannotDeleteMessage);
            tags.RemoveAll(item => item.Id == original.Id);
        }
        return Task.CompletedTask;
    }

    // ---- archive (migration 56): an archived tag leaves the list but its links stay.
    private readonly List<ArchivedTag> archivedTags = [];
    private readonly List<SubcategoryTag> archivedSource = [];

    public Task<IReadOnlyList<ArchivedTag>> GetArchivedTagsAsync(CancellationToken cancellationToken = default)
    {
        lock (gate) return Task.FromResult<IReadOnlyList<ArchivedTag>>(archivedTags.Select(tag => tag with { ProductCount = CountOf(tag.Id) }).ToArray());
    }

    public Task ArchiveTagAsync(SubcategoryTag original, string reason, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            var tag = tags.FirstOrDefault(item => item.Id == original.Id) ?? throw new ProductOperationException("Eticheta nu mai există. Actualizează lista și reia operația.");
            archivedTags.Add(new ArchivedTag(tag.Id, tag.Category, tag.Subcategory, tag.Name, tag.Version + 1, MariaTimeText.Format(DateTime.UtcNow), "demo", CountOf(tag.Id)));
            archivedSource.Add(tag);
            tags.Remove(tag);
        }
        return Task.CompletedTask;
    }

    public Task RestoreTagAsync(ArchivedTag original, string reason, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            tags.Add(archivedSource.First(item => item.Id == original.Id));
            archivedSource.RemoveAll(item => item.Id == original.Id);
            archivedTags.RemoveAll(item => item.Id == original.Id);
        }
        return Task.CompletedTask;
    }

    public Task GiveUpTagRestoreAsync(ArchivedTag original, string reason, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            var allowed = ParameterArchiveRules.GiveUpAllowedFrom(MariaTimeText.Parse(archivedTags.First(item => item.Id == original.Id).ArchivedUtc), ParameterArchiveRules.DefaultRetentionDays);
            if (DateTime.UtcNow < allowed) throw new ProductOperationException(ParameterArchiveRules.GiveUpTooEarlyMessage(allowed));
            foreach (var set in byProduct.Values) set.Remove(original.Id);
        }
        return Task.CompletedTask;
    }

    public Task DeleteArchivedTagAsync(ArchivedTag original, string reason, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            if (CountOf(original.Id) > 0) throw new ProductOperationException(ParameterArchiveRules.StillLinkedMessage);
            archivedSource.RemoveAll(item => item.Id == original.Id);
            archivedTags.RemoveAll(item => item.Id == original.Id);
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<int>> GetProductTagIdsAsync(int productId, CancellationToken cancellationToken = default)
    {
        lock (gate) return Task.FromResult<IReadOnlyList<int>>(byProduct.TryGetValue(productId, out var set) ? set.Order().ToList() : []);
    }

    public Task<IReadOnlyDictionary<int, IReadOnlyList<int>>> GetAllProductTagsAsync(CancellationToken cancellationToken = default)
    {
        lock (gate) return Task.FromResult<IReadOnlyDictionary<int, IReadOnlyList<int>>>(byProduct.Where(pair => pair.Value.Count > 0)
            .ToDictionary(pair => pair.Key, pair => (IReadOnlyList<int>)pair.Value.Order().ToList()));
    }

    public Task SetProductTagsAsync(int productId, IReadOnlyCollection<int> tagIds, CancellationToken cancellationToken = default)
    {
        lock (gate) byProduct[productId] = tagIds.Where(id => tags.Any(tag => tag.Id == id)).ToHashSet();
        return Task.CompletedTask;
    }
}
