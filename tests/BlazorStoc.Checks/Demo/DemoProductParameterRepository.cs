namespace BlazorStoc.Services;

// In-memory stand-in of the parameter store (component checks, like DemoProductRepository): the same rules for names and values, but no
// product is attached to a value, so nothing is blocked and no product is renamed.
public sealed class DemoProductParameterRepository : IProductParameterRepository
{
    private readonly object gate = new();
    private readonly List<SubcategoryParameter> parameters = [];
    private int nextId = 1;

    public Task<IReadOnlyList<SubcategoryParameter>> GetParametersAsync(CancellationToken cancellationToken = default)
    {
        lock (gate) return Task.FromResult<IReadOnlyList<SubcategoryParameter>>(parameters.ToArray());
    }

    private readonly List<ParameterFamily> families = [];

    public Task<IReadOnlyList<ParameterFamily>> GetFamiliesAsync(CancellationToken cancellationToken = default)
    {
        lock (gate) return Task.FromResult<IReadOnlyList<ParameterFamily>>(families.ToArray());
    }

    public Task<ParameterFamily> CreateFamilyAsync(ProductGroup group, string name, IReadOnlyList<int> parameterIds, CancellationToken cancellationToken = default)
    {
        var cleanName = ProductParameterRules.FamilyName(name);
        var ids = parameterIds.Distinct().ToList();
        if (ids.Count < ProductParameterRules.FamilyMinimumMembers) throw new ProductOperationException(ProductParameterRules.FamilyTooSmallMessage);
        lock (gate)
        {
            if (families.Any(item => item.Category == group.Category && item.Subcategory == group.Subcategory && TextNormalization.SameUniqueValue(item.Name, cleanName)))
                throw new ProductOperationException($"Subcategoria «{group.Subcategory}» are deja o familie «{cleanName}».");
            var members = parameters.Where(item => ids.Contains(item.Id)).ToList();
            if (members.Count != ids.Count || members.Any(item => item.Category != group.Category || item.Subcategory != group.Subcategory || item.FamilyId is not null))
                throw new ProductOperationException("Un parametru ales nu mai există, nu aparține subcategoriei sau face deja parte dintr-o familie.");
            var family = new ParameterFamily(nextId++, group.Category, group.Subcategory, cleanName, families.Count + 1, 0, ids);
            families.Add(family);
            foreach (var id in ids) parameters[parameters.FindIndex(item => item.Id == id)] = parameters.First(item => item.Id == id) with { FamilyId = family.Id };
            return Task.FromResult(family);
        }
    }

    public Task<ParameterFamily> UpdateFamilyAsync(ParameterFamily original, string name, IReadOnlyList<int> parameterIds, string reason, CancellationToken cancellationToken = default)
    {
        var cleanName = ProductParameterRules.FamilyName(name);
        var ids = parameterIds.Distinct().ToList();
        if (ids.Count < ProductParameterRules.FamilyMinimumMembers) throw new ProductOperationException(ProductParameterRules.FamilyTooSmallMessage);
        lock (gate)
        {
            var index = families.FindIndex(item => item.Id == original.Id);
            if (index < 0) throw new ProductOperationException("Familia nu mai există. Actualizează lista și reia operația.");
            for (var i = 0; i < parameters.Count; i++)
                if (parameters[i].FamilyId == original.Id && !ids.Contains(parameters[i].Id)) parameters[i] = parameters[i] with { FamilyId = null };
            foreach (var id in ids) parameters[parameters.FindIndex(item => item.Id == id)] = parameters.First(item => item.Id == id) with { FamilyId = original.Id };
            families[index] = families[index] with { Name = cleanName, ParameterIds = ids, Version = original.Version + 1 };
            return Task.FromResult(families[index]);
        }
    }

    public Task DeleteFamilyAsync(ParameterFamily original, string reason, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            for (var i = 0; i < parameters.Count; i++) if (parameters[i].FamilyId == original.Id) parameters[i] = parameters[i] with { FamilyId = null };
            families.RemoveAll(item => item.Id == original.Id);
        }
        return Task.CompletedTask;
    }

    public Task<SubcategoryParameter> AddParameterAsync(ProductGroup group, string name, string unit, ParameterKind kind, CancellationToken cancellationToken = default)
    {
        var cleanName = ProductParameterRules.Name(name);
        lock (gate)
        {
            if (parameters.Any(item => item.Category == group.Category && item.Subcategory == group.Subcategory && TextNormalization.SameUniqueValue(item.Name, cleanName)))
                throw new ProductOperationException($"Subcategoria «{group.Subcategory}» are deja un parametru «{cleanName}».");
            var added = new SubcategoryParameter(nextId++, group.Category, group.Subcategory, cleanName, ProductParameterRules.UsesUnit(kind) ? ProductParameterRules.Unit(unit) : "", kind,
                parameters.Count(item => item.Category == group.Category && item.Subcategory == group.Subcategory) + 1, 0,
                kind == ParameterKind.Fixed ? [new ParameterListValue(nextId++, cleanName, 0)] : []);
            parameters.Add(added);
            return Task.FromResult(added);
        }
    }

    public Task UpdateParameterAsync(SubcategoryParameter original, string name, string unit, ParameterKind kind, string reason, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            var index = parameters.FindIndex(item => item.Id == original.Id);
            if (index < 0) throw new ProductOperationException("Parametrul nu mai există. Actualizează lista și reia operația.");
            parameters[index] = parameters[index] with { Name = ProductParameterRules.Name(name), Kind = kind, Unit = ProductParameterRules.UsesUnit(kind) ? ProductParameterRules.Unit(unit) : "", Version = original.Version + 1 };
        }
        return Task.CompletedTask;
    }

    public Task DeleteParameterAsync(SubcategoryParameter original, string reason, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            if (uses.TryGetValue(original.Id, out var linked) && linked.Count > 0) throw new ProductOperationException(ParameterArchiveRules.UsedCannotDeleteMessage);
            parameters.RemoveAll(item => item.Id == original.Id);
        }
        return Task.CompletedTask;
    }

    public Task<ParameterListValue> AddValueAsync(int parameterId, string value, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            var index = parameters.FindIndex(item => item.Id == parameterId);
            if (index < 0) throw new ProductOperationException("Parametrul nu mai există. Actualizează lista și reia operația.");
            var parameter = parameters[index];
            var clean = ProductParameterRules.Value(parameter.Kind, parameter.Name, value);
            if (parameter.Values.Any(item => ProductParameterRules.Key(item.Value) == ProductParameterRules.Key(clean)))
                throw new ProductOperationException($"Valoarea «{clean}» există deja la parametrul «{parameter.Name}». Alege-o din listă.");
            var added = new ParameterListValue(nextId++, clean, 0);
            parameters[index] = parameter with { Values = [.. parameter.Values, added] };
            return Task.FromResult(added);
        }
    }

    public Task DeleteValueAsync(int valueId, string reason, CancellationToken cancellationToken = default)
    {
        lock (gate)
            for (var index = 0; index < parameters.Count; index++)
                if (parameters[index].Values.Any(item => item.Id == valueId))
                    parameters[index] = parameters[index] with { Values = parameters[index].Values.Where(item => item.Id != valueId).ToList() };
        return Task.CompletedTask;
    }

    public Task<ParameterValueChange> PreviewValueChangeAsync(int valueId, string newValue, CancellationToken cancellationToken = default) => Task.FromResult(Change(valueId, newValue));

    public Task<ParameterValueChange> ChangeValueAsync(int valueId, string newValue, string reason, CancellationToken cancellationToken = default)
    {
        var change = Change(valueId, newValue);
        lock (gate)
            for (var index = 0; index < parameters.Count; index++)
                if (parameters[index].Values.Any(item => item.Id == valueId))
                    parameters[index] = parameters[index] with { Values = parameters[index].Values.Select(item => item.Id == valueId ? item with { Value = change.NewValue } : item).ToList() };
        return Task.FromResult(change);
    }

    public Task<ProductParameterState> GetProductStateAsync(int productId, CancellationToken cancellationToken = default) => Task.FromResult(new ProductParameterState("", []));

    public Task<IReadOnlySet<int>> GetIncompleteProductIdsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlySet<int>>(new HashSet<int>());

    public Task<IReadOnlyDictionary<int, string>> GetBaseModelsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<int, string>>(new Dictionary<int, string>());

    public Task<IReadOnlyDictionary<int, IReadOnlyList<ProductParameterChoice>>> GetAllChoicesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<int, IReadOnlyList<ProductParameterChoice>>>(new Dictionary<int, IReadOnlyList<ProductParameterChoice>>());

    // ---- archive (migration 56), kept in memory: SeedUse says which products carry a parameter, SeedBroken which of them lost the baked text from their model.
    private readonly Dictionary<int, List<(int ProductId, string Name)>> uses = [];
    private readonly List<ArchivedParameter> archivedParameters = [];
    private readonly List<ArchivedFamily> archivedFamilies = [];
    private readonly HashSet<int> brokenProducts = [];
    private readonly List<ParameterUsageEntry> usageLog = [];

    public void SeedUse(int parameterId, int productId, string name)
    {
        lock (gate)
        {
            if (!uses.TryGetValue(parameterId, out var list)) uses[parameterId] = list = [];
            list.Add((productId, name));
        }
    }

    public void SeedBroken(int productId) { lock (gate) brokenProducts.Add(productId); }

    // Moves the archive date of a parameter or a family back (the retention period passes).
    public void SeedArchivedAt(string archivedUtc)
    {
        lock (gate)
        {
            for (var i = 0; i < archivedParameters.Count; i++) archivedParameters[i] = archivedParameters[i] with { ArchivedUtc = archivedUtc };
            for (var i = 0; i < archivedFamilies.Count; i++) archivedFamilies[i] = archivedFamilies[i] with { ArchivedUtc = archivedUtc };
        }
    }

    private int UseCount(int parameterId) => uses.TryGetValue(parameterId, out var list) ? list.Count : 0;

    private ParameterArchivePreview PreviewArchive(IEnumerable<int> ids)
    {
        var affected = ids.SelectMany(id => uses.GetValueOrDefault(id) ?? []).Select(item => new ParameterAffectedProduct(item.ProductId, item.Name, ParameterArchiveRules.Bake(item.Name, "v"), 1)).ToList();
        return new ParameterArchivePreview(affected, []);
    }

    private void ArchiveIds(IEnumerable<int> ids, int? familyId, string? familyName)
    {
        foreach (var id in ids.ToList())
        {
            var parameter = parameters.First(item => item.Id == id);
            archivedParameters.Add(new ArchivedParameter(parameter.Id, parameter.Category, parameter.Subcategory, parameter.Name, parameter.Unit, parameter.Kind, parameter.Version + 1,
                familyId, familyName, MariaTimeText.Format(DateTime.UtcNow), "demo", UseCount(id), parameter.Values.Select(value => value.Value).ToList()));
            foreach (var use in uses.GetValueOrDefault(id) ?? [])
                usageLog.Add(new ParameterUsageEntry(usageLog.Count + 1, id, parameter.Name, "v", use.ProductId, use.Name, $"{parameter.Category} / {parameter.Subcategory}", ParameterUsageActions.Archived, "demo", "", MariaTimeText.Format(DateTime.UtcNow)));
            parameters.Remove(parameter);
        }
    }

    public Task<ArchivedParameters> GetArchivedAsync(CancellationToken cancellationToken = default)
    {
        lock (gate) return Task.FromResult(new ArchivedParameters(archivedParameters.ToArray(), archivedFamilies.ToArray()));
    }

    public Task<ParameterArchivePreview> PreviewParameterArchiveAsync(SubcategoryParameter parameter, CancellationToken cancellationToken = default)
    {
        lock (gate) return Task.FromResult(PreviewArchive([parameter.Id]));
    }

    public Task ArchiveParameterAsync(SubcategoryParameter original, string reason, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            if (parameters.FirstOrDefault(item => item.Id == original.Id) is not { } current) throw new ProductOperationException("Parametrul nu mai există. Actualizează lista și reia operația.");
            if (current.FamilyId is not null) throw new ProductOperationException(ParameterArchiveRules.FamilyMemberMessage);
            ArchiveIds([original.Id], null, null);
        }
        return Task.CompletedTask;
    }

    public Task<ParameterArchivePreview> PreviewFamilyArchiveAsync(ParameterFamily family, CancellationToken cancellationToken = default)
    {
        lock (gate) return Task.FromResult(PreviewArchive(family.ParameterIds));
    }

    public Task ArchiveFamilyAsync(ParameterFamily original, string reason, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            var family = families.FirstOrDefault(item => item.Id == original.Id) ?? throw new ProductOperationException("Familia nu mai există. Actualizează lista și reia operația.");
            ArchiveIds(family.ParameterIds, family.Id, family.Name);
            archivedFamilies.Add(new ArchivedFamily(family.Id, family.Category, family.Subcategory, family.Name, family.Version + 1, MariaTimeText.Format(DateTime.UtcNow), "demo",
                family.ParameterIds, family.ParameterIds.Sum(UseCount)));
            families.Remove(family);
        }
        return Task.CompletedTask;
    }

    private ParameterRestorePreview PreviewRestore(IEnumerable<int> ids) => new(ids.SelectMany(id => uses.GetValueOrDefault(id) ?? []).Select(item =>
        new ParameterRestoreItem(item.ProductId, item.Name, item.Name, item.Name, item.Name, brokenProducts.Contains(item.ProductId) ? RestoreProblem.BakedTextMissing : RestoreProblem.None)).ToList());

    public Task<ParameterRestorePreview> PreviewParameterRestoreAsync(ArchivedParameter parameter, CancellationToken cancellationToken = default)
    {
        lock (gate) return Task.FromResult(PreviewRestore([parameter.Id]));
    }

    public Task<ParameterRestorePreview> PreviewFamilyRestoreAsync(ArchivedFamily family, CancellationToken cancellationToken = default)
    {
        lock (gate) return Task.FromResult(PreviewRestore(family.ParameterIds));
    }

    private void RestoreIds(IEnumerable<int> ids, IReadOnlyDictionary<int, string> baseModels)
    {
        var preview = PreviewRestore(ids);
        if (preview.Problems.FirstOrDefault(item => !baseModels.ContainsKey(item.ProductId)) is { } open)
            throw new ProductOperationException($"Produsul «{open.OldName}» cere o decizie: alege redenumirea automată sau scrie modelul.");
        foreach (var id in ids.ToList())
        {
            var archived = archivedParameters.First(item => item.Id == id);
            parameters.Add(new SubcategoryParameter(archived.Id, archived.Category, archived.Subcategory, archived.Name, archived.Unit, archived.Kind, parameters.Count + 1, archived.Version + 1,
                archived.Values.Select(value => new ParameterListValue(nextId++, value, 0)).ToList(), archived.FamilyId));
            archivedParameters.Remove(archived);
            foreach (var use in uses.GetValueOrDefault(id) ?? [])
                usageLog.Add(new ParameterUsageEntry(usageLog.Count + 1, id, archived.Name, "v", use.ProductId, use.Name, $"{archived.Category} / {archived.Subcategory}", ParameterUsageActions.Restored, "demo", "", MariaTimeText.Format(DateTime.UtcNow)));
        }
    }

    public IReadOnlyDictionary<int, string> LastRestoreModels { get; private set; } = new Dictionary<int, string>();

    public Task RestoreParameterAsync(ArchivedParameter original, IReadOnlyDictionary<int, string> baseModels, string reason, CancellationToken cancellationToken = default)
    {
        lock (gate) { RestoreIds([original.Id], baseModels); LastRestoreModels = baseModels; }
        return Task.CompletedTask;
    }

    public Task RestoreFamilyAsync(ArchivedFamily original, IReadOnlyDictionary<int, string> baseModels, string reason, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            var family = archivedFamilies.First(item => item.Id == original.Id);
            RestoreIds(family.ParameterIds, baseModels);
            archivedFamilies.Remove(family);
            families.Add(new ParameterFamily(family.Id, family.Category, family.Subcategory, family.Name, families.Count + 1, family.Version + 1, family.ParameterIds));
            LastRestoreModels = baseModels;
        }
        return Task.CompletedTask;
    }

    private static void EnsureGiveUpAllowed(string archivedUtc)
    {
        var allowed = ParameterArchiveRules.GiveUpAllowedFrom(MariaTimeText.Parse(archivedUtc), ParameterArchiveRules.DefaultRetentionDays);
        if (DateTime.UtcNow < allowed) throw new ProductOperationException(ParameterArchiveRules.GiveUpTooEarlyMessage(allowed));
    }

    public Task GiveUpParameterRestoreAsync(ArchivedParameter original, string reason, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            var index = archivedParameters.FindIndex(item => item.Id == original.Id);
            EnsureGiveUpAllowed(archivedParameters[index].ArchivedUtc);
            archivedParameters[index] = archivedParameters[index] with { ProductCount = 0 };
            uses.Remove(original.Id);
        }
        return Task.CompletedTask;
    }

    public Task GiveUpFamilyRestoreAsync(ArchivedFamily original, string reason, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            var index = archivedFamilies.FindIndex(item => item.Id == original.Id);
            EnsureGiveUpAllowed(archivedFamilies[index].ArchivedUtc);
            archivedFamilies[index] = archivedFamilies[index] with { ProductCount = 0 };
            foreach (var id in original.ParameterIds) uses.Remove(id);
        }
        return Task.CompletedTask;
    }

    public Task DeleteArchivedParameterAsync(ArchivedParameter original, string reason, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            if (original.ProductCount > 0 && UseCount(original.Id) > 0) throw new ProductOperationException(ParameterArchiveRules.StillLinkedMessage);
            archivedParameters.RemoveAll(item => item.Id == original.Id);
        }
        return Task.CompletedTask;
    }

    public Task DeleteArchivedFamilyAsync(ArchivedFamily original, string reason, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            if (original.ParameterIds.Any(id => UseCount(id) > 0)) throw new ProductOperationException(ParameterArchiveRules.StillLinkedMessage);
            archivedParameters.RemoveAll(item => original.ParameterIds.Contains(item.Id));
            archivedFamilies.RemoveAll(item => item.Id == original.Id);
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ParameterUsageEntry>> GetUsageAsync(IReadOnlyList<int> parameterIds, CancellationToken cancellationToken = default)
    {
        lock (gate) return Task.FromResult<IReadOnlyList<ParameterUsageEntry>>(usageLog.Where(entry => parameterIds.Contains(entry.ParameterId)).OrderByDescending(entry => entry.Id).ToList());
    }

    private ParameterValueChange Change(int valueId, string newValue)
    {
        lock (gate)
        {
            var parameter = parameters.FirstOrDefault(item => item.Values.Any(value => value.Id == valueId))
                ?? throw new ProductOperationException("Valoarea nu mai există. Actualizează lista și reia operația.");
            var old = parameter.Values.First(value => value.Id == valueId).Value;
            var clean = ProductParameterRules.Value(parameter.Kind, parameter.Name, newValue);
            if (parameter.Values.Any(item => item.Id != valueId && ProductParameterRules.Key(item.Value) == ProductParameterRules.Key(clean)))
                throw new ProductOperationException($"Valoarea «{clean}» există deja la parametrul «{parameter.Name}». Combinarea variantelor nu este disponibilă.");
            return new ParameterValueChange(old, clean, [], []);
        }
    }
}
