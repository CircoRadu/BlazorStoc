namespace BlazorStoc.Services;

public sealed partial class DemoProductRepository
{
    public Task<IReadOnlyList<ProductGroup>> GetGroupsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate) return Task.FromResult<IReadOnlyList<ProductGroup>>(categories
            .SelectMany(category =>
            {
                var children = groups.Where(group => TextNormalization.SameUniqueValue(group.Category, category)).ToArray();
                return children.Length == 0 ? [new ProductGroup(category, string.Empty)] : children;
            }).ToArray());
    }
    public async Task<Product> CreateAsync(ProductInput input, CancellationToken cancellationToken = default)
    {
        await EnsureProductOperatorAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var value = input.Validated();
        Product product;
        bool categoryCreated, subcategoryCreated;
        lock (gate)
        {
            EnsureUniqueProductName(value.Name, null);
            var group = ResolveGroup(value.Category, value.Subcategory);
            var (category, subcategory) = (group.Category, group.Subcategory);
            (categoryCreated, subcategoryCreated) = (group.CategoryCreated, group.SubcategoryCreated);
            product = new Product(store.NextId++, category, subcategory, value.Name, value.Description, value.Quantity);
            products.Add(product);
        }
        await RecordCreatedGroupsAsync(product.Category, product.Subcategory, categoryCreated, subcategoryCreated, cancellationToken);
        await AuditRecorder.RecordCreateAsync(auditTrail, accessControl, AuditEntities.Product, product.Id.ToString(),
            $"#{product.Id} · {product.Name}", AuditDetails.Identification(
                ("Denumire", product.Name), ("Categorie", product.Category), ("Subcategorie", product.Subcategory),
                ("Cantitate", product.Quantity.ToString())), cancellationToken);
        return product;
    }
    public async Task<Product> UpdateAsync(Product original, ProductInput input, CancellationToken cancellationToken = default)
    {
        await EnsureProductOperatorAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var value = input.Validated(original);
        Product product;
        bool categoryCreated, subcategoryCreated;
        lock (gate)
        {
            ProductRules.CheckCurrent(products.SingleOrDefault(p => p.Id == original.Id), original);
            EnsureUniqueProductName(value.Name, original.Id);
            var group = ResolveGroup(value.Category, value.Subcategory);
            var (category, subcategory) = (group.Category, group.Subcategory);
            (categoryCreated, subcategoryCreated) = (group.CategoryCreated, group.SubcategoryCreated);
            product = new Product(original.Id, category, subcategory, value.Name, value.Description, value.Quantity, checked(original.Version + 1));
            products[products.FindIndex(p => p.Id == original.Id)] = product;
        }
        await RecordCreatedGroupsAsync(product.Category, product.Subcategory, categoryCreated, subcategoryCreated, cancellationToken);
        await AuditRecorder.RecordEditAsync(auditTrail, accessControl, AuditEntities.Product, product.Id.ToString(),
            $"#{product.Id} · {product.Name}", ProductAuditChanges(original, product), value.Reason, cancellationToken);
        return product;
    }
    public async Task DeleteAsync(Product original, string reason, CancellationToken cancellationToken = default)
    {
        await EnsureProductOperatorAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var motif = ChangeReasonRules.Normalize(reason);
        if (ChangeReasonRules.ValidationError(motif) is { } reasonError)
            throw new ProductOperationException(reasonError);
        await archiver.ExecuteAsync(ArchiveRequests.Product(original, motif), async (operation, token) =>
        {
            lock (gate)
            {
                ProductRules.CheckCurrent(products.SingleOrDefault(p => p.Id == original.Id), original);
                ProductRules.CheckDelete(original, false);
                products.RemoveAll(p => p.Id == original.Id);
            }
            await AuditRecorder.RecordDeleteAsync(auditTrail, operation, token);
        }, cancellationToken);
    }

    private static AuditChange[] ProductAuditChanges(Product before, Product after) =>
    [
        new("Denumire", before.Name, after.Name),
        new("Categorie", before.Category, after.Category),
        new("Subcategorie", before.Subcategory, after.Subcategory),
        new("Descriere", before.Description, after.Description),
        new("Cantitate", before.Quantity.ToString(), after.Quantity.ToString())
    ];

    private Task EnsureProductOperatorAsync(CancellationToken token) => accessControl?.EnsureProductOperatorAsync(token) ?? Task.CompletedTask;

    private (string Category, string Subcategory, bool CategoryCreated, bool SubcategoryCreated) ResolveGroup(string category, string subcategory)
    {
        var existingCategory = categories
            .FirstOrDefault(current => TextNormalization.SameUniqueValue(current, category));
        if (existingCategory is null)
            throw new ProductOperationException("Categoria selectată nu mai există. Actualizează lista și reia salvarea.");
        category = existingCategory;
        var existingSubcategory = groups
            .Where(group => TextNormalization.SameUniqueValue(group.Category, category))
            .Select(group => group.Subcategory)
            .FirstOrDefault(current => TextNormalization.SameUniqueValue(current, subcategory));
        if (existingSubcategory is not null) return (category, existingSubcategory, false, false);
        var duplicateSubcategory = groups.FirstOrDefault(group => TextNormalization.SameUniqueValue(group.Subcategory, subcategory));
        if (duplicateSubcategory is not null)
            throw new ProductOperationException($"Subcategoria «{duplicateSubcategory.Subcategory}» există deja în categoria «{duplicateSubcategory.Category}».");
        throw new ProductOperationException("Subcategoria selectată nu mai există în categoria aleasă. Actualizează lista și reia salvarea.");
    }

    private async Task RecordCreatedGroupsAsync(string category, string subcategory, bool categoryCreated,
        bool subcategoryCreated, CancellationToken cancellationToken)
    {
        if (categoryCreated)
            await AuditRecorder.RecordCreateAsync(auditTrail, accessControl, AuditEntities.Category,
                TextNormalization.UniquenessKey(category), category,
                AuditDetails.Identification(("Denumire", category)), cancellationToken);
        if (subcategoryCreated)
            await AuditRecorder.RecordCreateAsync(auditTrail, accessControl, AuditEntities.Subcategory,
                $"{TextNormalization.UniquenessKey(category)}:{TextNormalization.UniquenessKey(subcategory)}",
                $"{category} / {subcategory}", AuditDetails.Identification(
                    ("Denumire", subcategory), ("Categorie", category)), cancellationToken);
    }

    private void EnsureUniqueProductName(string name, int? excludedId)
    {
        var existing = products.FirstOrDefault(product => product.Id != excludedId && TextNormalization.SameUniqueValue(product.Name, name));
        if (existing is not null)
            throw new ProductOperationException($"Produsul «{existing.Name}» există deja în categoria «{existing.Category}», subcategoria «{existing.Subcategory}».");
    }
}
