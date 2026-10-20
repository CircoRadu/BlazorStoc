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

    public Task<SubcategoryParameter> AddParameterAsync(ProductGroup group, string name, string unit, ParameterKind kind, CancellationToken cancellationToken = default)
    {
        var cleanName = ProductParameterRules.Name(name);
        lock (gate)
        {
            if (parameters.Any(item => item.Category == group.Category && item.Subcategory == group.Subcategory && TextNormalization.SameUniqueValue(item.Name, cleanName)))
                throw new ProductOperationException($"Subcategoria «{group.Subcategory}» are deja un parametru «{cleanName}».");
            var added = new SubcategoryParameter(nextId++, group.Category, group.Subcategory, cleanName, kind == ParameterKind.Number ? ProductParameterRules.Unit(unit) : "", kind,
                parameters.Count(item => item.Category == group.Category && item.Subcategory == group.Subcategory) + 1, 0, []);
            parameters.Add(added);
            return Task.FromResult(added);
        }
    }

    public Task UpdateParameterAsync(SubcategoryParameter original, string name, string unit, string reason, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            var index = parameters.FindIndex(item => item.Id == original.Id);
            if (index < 0) throw new ProductOperationException("Parametrul nu mai există. Actualizează lista și reia operația.");
            parameters[index] = parameters[index] with { Name = ProductParameterRules.Name(name), Unit = original.Kind == ParameterKind.Number ? ProductParameterRules.Unit(unit) : "", Version = original.Version + 1 };
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
