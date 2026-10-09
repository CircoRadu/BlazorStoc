namespace BlazorStoc.Services;

public sealed partial class DemoProductRepository
{
    public async Task CreateCategoryAsync(string category, CancellationToken cancellationToken = default)
    {
        await EnsureProductOperatorAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var name = ProductGroupManagementRules.Name(category, "Denumire categorie");
        lock (gate)
        {
            var existing = categories.FirstOrDefault(value => TextNormalization.SameUniqueValue(value, name));
            if (existing is not null) throw new ProductOperationException($"Categoria «{existing}» există deja.");
            if (groups.FirstOrDefault(group => TextNormalization.SameUniqueValue(group.Subcategory, name)) is { } sub)
                throw new ProductOperationException(ProductGroupManagementRules.NameTakenBySubcategory(sub.Subcategory, sub.Category));
            categories.Add(name);
        }
        await AuditRecorder.RecordCreateAsync(auditTrail, accessControl, AuditEntities.Category,
            TextNormalization.UniquenessKey(name), name, AuditDetails.Identification(("Denumire", name)), cancellationToken);
    }

    public async Task<ProductGroup> CreateSubcategoryAsync(string category, string subcategory,
        CancellationToken cancellationToken = default)
    {
        await EnsureProductOperatorAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var requestedCategory = ProductGroupManagementRules.Name(category, "Categorie");
        var name = ProductGroupManagementRules.Name(subcategory, "Denumire subcategorie");
        ProductGroup created;
        lock (gate)
        {
            var storedCategory = categories.FirstOrDefault(value => TextNormalization.SameUniqueValue(value, requestedCategory))
                ?? throw new ProductOperationException("Categoria nu mai există. Actualizează lista.");
            var existing = groups.FirstOrDefault(group => TextNormalization.SameUniqueValue(group.Subcategory, name));
            if (existing is not null)
                throw new ProductOperationException($"Subcategoria «{existing.Subcategory}» există deja în categoria «{existing.Category}».");
            if (categories.FirstOrDefault(value => TextNormalization.SameUniqueValue(value, name)) is { } named)
                throw new ProductOperationException(ProductGroupManagementRules.NameTakenByCategory(named));
            created = new(storedCategory, name);
            groups.Add(created);
        }
        await AuditRecorder.RecordCreateAsync(auditTrail, accessControl, AuditEntities.Subcategory,
            $"{TextNormalization.UniquenessKey(created.Category)}:{TextNormalization.UniquenessKey(created.Subcategory)}",
            $"{created.Category} / {created.Subcategory}",
            AuditDetails.Identification(("Denumire", created.Subcategory), ("Categorie", created.Category)), cancellationToken);
        return created;
    }

    public async Task DeleteCategoryAsync(string category, string reason, CancellationToken cancellationToken = default)
    {
        await EnsureProductOperatorAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var motif = ProductGroupManagementRules.Reason(reason);
        string stored;
        lock (gate)
        {
            stored = categories.FirstOrDefault(value => TextNormalization.SameUniqueValue(value, category))
                ?? throw new ProductOperationException("Categoria nu mai există. Actualizează lista.");
            if (groups.Any(group => TextNormalization.SameUniqueValue(group.Category, stored)) || products.Any(product => TextNormalization.SameUniqueValue(product.Category, stored)))
                throw new ProductOperationException(ProductGroupManagementRules.CategoryNotEmptyMessage);
            categories.Remove(stored);
        }
        await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.Category, AuditActions.DeleteCategory,
            string.Empty, stored, AuditDetails.Identification(("Denumire", stored)), motif, cancellationToken);
    }

    public async Task DeleteSubcategoryAsync(ProductGroup group, string reason, CancellationToken cancellationToken = default)
    {
        await EnsureProductOperatorAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var motif = ProductGroupManagementRules.Reason(reason);
        ProductGroup stored;
        lock (gate)
        {
            stored = groups.FirstOrDefault(item => TextNormalization.SameUniqueValue(item.Category, group.Category) && TextNormalization.SameUniqueValue(item.Subcategory, group.Subcategory))
                ?? throw new ProductOperationException("Subcategoria nu mai există. Actualizează lista.");
            if (products.Any(product => TextNormalization.SameUniqueValue(product.Category, stored.Category) && TextNormalization.SameUniqueValue(product.Subcategory, stored.Subcategory)))
                throw new ProductOperationException(ProductGroupManagementRules.SubcategoryNotEmptyMessage);
            groups.Remove(stored);
        }
        await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.Subcategory, AuditActions.DeleteSubcategory,
            string.Empty, $"{stored.Category} / {stored.Subcategory}", AuditDetails.Identification(("Denumire", stored.Subcategory), ("Categorie", stored.Category)), motif, cancellationToken);
    }

    public async Task RenameCategoryAsync(string originalCategory, string newCategory, string reason,
        CancellationToken cancellationToken = default)
    {
        await EnsureProductOperatorAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var name = ProductGroupManagementRules.Name(newCategory, "Denumire categorie");
        var motif = ProductGroupManagementRules.Reason(reason);
        string before;
        lock (gate)
        {
            before = categories.FirstOrDefault(value => TextNormalization.SameUniqueValue(value, originalCategory))
                ?? throw new ProductOperationException("Categoria nu mai există. Actualizează lista.");
            if (categories.Any(value => !TextNormalization.SameUniqueValue(value, before) &&
                                        TextNormalization.SameUniqueValue(value, name)))
                throw new ProductOperationException($"Categoria «{name}» există deja.");
            if (groups.FirstOrDefault(group => TextNormalization.SameUniqueValue(group.Subcategory, name)) is { } sub)
                throw new ProductOperationException(ProductGroupManagementRules.NameTakenBySubcategory(sub.Subcategory, sub.Category));
            categories[categories.IndexOf(before)] = name;
            for (var index = 0; index < groups.Count; index++)
                if (TextNormalization.SameUniqueValue(groups[index].Category, before))
                    groups[index] = groups[index] with { Category = name };
            for (var index = 0; index < products.Count; index++)
                if (TextNormalization.SameUniqueValue(products[index].Category, before))
                    products[index] = products[index] with { Category = name };
        }
        await AuditRecorder.RecordEditAsync(auditTrail, accessControl, AuditEntities.Category,
            TextNormalization.UniquenessKey(originalCategory), name,
            [new("Denumire", before, name)], motif, cancellationToken);
    }

    public async Task<ProductGroup> UpdateSubcategoryAsync(ProductGroup original, string newSubcategory,
        string targetCategory, string reason, CancellationToken cancellationToken = default)
    {
        await EnsureProductOperatorAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var name = ProductGroupManagementRules.Name(newSubcategory, "Denumire subcategorie");
        var category = ProductGroupManagementRules.Name(targetCategory, "Categorie");
        var motif = ProductGroupManagementRules.Reason(reason);
        ProductGroup before;
        ProductGroup after;
        lock (gate)
        {
            before = groups.FirstOrDefault(group => TextNormalization.SameUniqueValue(group.Category, original.Category) &&
                                                    TextNormalization.SameUniqueValue(group.Subcategory, original.Subcategory))
                ?? throw new ProductOperationException("Subcategoria nu mai există. Actualizează lista.");
            category = categories.FirstOrDefault(existing => TextNormalization.SameUniqueValue(existing, category))
                ?? throw new ProductOperationException("Categoria destinație nu mai există. Actualizează lista.");
            if (groups.Any(group => !TextNormalization.SameUniqueValue(group.Subcategory, before.Subcategory) &&
                                    TextNormalization.SameUniqueValue(group.Subcategory, name)))
                throw new ProductOperationException($"Subcategoria «{name}» există deja.");
            if (categories.FirstOrDefault(existing => TextNormalization.SameUniqueValue(existing, name)) is { } named)
                throw new ProductOperationException(ProductGroupManagementRules.NameTakenByCategory(named));
            after = new(category, name);
            var groupIndex = groups.IndexOf(before);
            groups[groupIndex] = after;
            for (var index = 0; index < products.Count; index++)
                if (TextNormalization.SameUniqueValue(products[index].Category, before.Category) &&
                    TextNormalization.SameUniqueValue(products[index].Subcategory, before.Subcategory))
                    products[index] = products[index] with { Category = after.Category, Subcategory = after.Subcategory };
        }
        await AuditRecorder.RecordEditAsync(auditTrail, accessControl, AuditEntities.Subcategory,
            $"{TextNormalization.UniquenessKey(original.Category)}:{TextNormalization.UniquenessKey(original.Subcategory)}",
            $"{after.Category} / {after.Subcategory}",
            [new("Denumire", before.Subcategory, after.Subcategory), new("Categorie", before.Category, after.Category)],
            motif, cancellationToken);
        return after;
    }
}
