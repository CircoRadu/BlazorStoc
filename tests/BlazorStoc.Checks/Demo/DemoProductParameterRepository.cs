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
        lock (gate) parameters.RemoveAll(item => item.Id == original.Id);
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
