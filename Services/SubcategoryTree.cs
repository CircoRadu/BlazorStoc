using System.Globalization;
using MySqlConnector;
using static BlazorStoc.Services.MariaDb;

namespace BlazorStoc.Services;

// The tree of subcategories (migration 54): a subcategory may have child subcategories on any number of levels. The categories (tier 1) hold no
// parameters and no tags; a child inherits the parameters, families and tags of every ancestor (computed when read) and can add its own.
// Products are attached to leaves only. Names stay unique across the whole tree, so a ProductGroup (category + name) still identifies a node.
// Depth is 0 for a subcategory directly under a category.
public sealed record SubcategoryNode(int Id, string Category, string Name, string? Parent, int Depth, bool IsLeaf, int ProductCount);

// The outcome of a move: the nodes moved (the node and everything under it), the products left incomplete by newly inherited parameters, and whether it was only a new place among the same siblings.
public sealed record SubcategoryMoveResult(int NodesMoved, int IncompleteProducts, bool OnlyReordered);

public interface ISubcategoryTree
{
    // Every subcategory in tree order (category order, then each parent followed by its children).
    Task<IReadOnlyList<SubcategoryNode>> GetTreeAsync(CancellationToken cancellationToken = default);
    // Adds a child under the parent. A parent that has products is a leaf turning into a parent: its products must move into the new child
    // (moveProducts), otherwise the operation is refused. Everything runs in one transaction.
    Task<ProductGroup> CreateChildAsync(ProductGroup parent, string name, bool moveProducts, CancellationToken cancellationToken = default);
    // Moves a node together with everything under it to another branch (newParent) or to the first level of a category (newParent null, targetCategory), at the
    // given place among its new siblings (0 = first; a place past the end means last). Administrator only. Refused, with nothing changed, when it would move a node
    // into its own subtree, exceed the maximum depth, put a child under a subcategory that has products, repeat a parameter or tag name of the new ancestors, or
    // cut products from parameter values or tags they have from their present ancestors.
    Task<SubcategoryMoveResult> MoveAsync(ProductGroup node, ProductGroup? newParent, string targetCategory, int position, CancellationToken cancellationToken = default);
    // A branch that changes its ancestors keeps the parameters, families and tags it inherited (they are copied onto the moved node, or merged into the namesakes of the new ancestors),
    // and gets those of the new ancestors. MoveAsync above throws MoveNeedsVerificationException when that is the case: the preview (a rolled-back run) lists what happens, the move
    // with the choices applies it. Without definitions involved the preview is plain and the plain MoveAsync does the work.
    Task<BranchMovePreview> PreviewMoveAsync(ProductGroup node, ProductGroup? newParent, string targetCategory, int position, BranchMoveChoices? choices, CancellationToken cancellationToken = default) =>
        Task.FromResult(new BranchMovePreview([], [], []));
    Task<SubcategoryMoveResult> MoveAsync(ProductGroup node, ProductGroup? newParent, string targetCategory, int position, BranchMoveChoices choices, CancellationToken cancellationToken = default) =>
        MoveAsync(node, newParent, targetCategory, position, cancellationToken);
    // A leaf (a subcategory with no children) becomes a category (stage 7): the category takes the leaf's name, the leaf stays under it as the only subcategory with the new name
    // (the products, parameters and tags stay on it; what it inherited from the old parents is copied onto it as in a branch move). The new category goes at the given place
    // among the categories (0 = first). Everything runs in one transaction; the preview is the same run rolled back. Administrator only.
    Task<BranchMovePreview> PreviewPromotionAsync(ProductGroup leaf, string newName, int categoryPosition, BranchMoveChoices? choices, CancellationToken cancellationToken = default) =>
        throw new ProductOperationException(SubcategoryTreeRules.PromotionUnavailableMessage);
    Task<SubcategoryMoveResult> PromoteAsync(ProductGroup leaf, string newName, int categoryPosition, BranchMoveChoices choices, CancellationToken cancellationToken = default) =>
        throw new ProductOperationException(SubcategoryTreeRules.PromotionUnavailableMessage);
}

public static class SubcategoryTreeRules
{
    // The depth cap itself is a setting (Setari -> Catalog, at most CatalogSettingsRules.MaxSubcategoryDepth); this is only the guard of the walks in memory.
    public const int WalkGuard = CatalogSettingsRules.MaxSubcategoryDepth;
    public const string LeafOnlyProductsMessage = "Produsele se asociază numai subcategoriilor fără subcategorii copil (frunze). Alege o subcategorie de la ultimul nivel.";
    public const string PromotionUnavailableMessage = "Transformarea unei subcategorii în categorie nu este disponibilă în acest catalog.";
    public const string PromoteLeafOnlyMessage = "Doar subcategoriile fără subcategorii copil pot deveni categorii. Mută întâi copiii sau trage o frunză.";
    public static string PromotionSameNameMessage(string category) => $"Subcategoria nouă trebuie să aibă alt nume decât categoria «{category}»: numele categoriilor și ale subcategoriilor sunt unice împreună.";
    public const string HasChildrenMessage = "Subcategoria are subcategorii copil și nu poate fi ștearsă. Șterge sau mută întâi copiii.";
    public const string CategoryChangeNotRootMessage = "O subcategorie copil rămâne în categoria părintelui ei. Categoria se schimbă numai la o subcategorie de la primul nivel.";
    public static string TooDeepMessage(int maximum) => $"Arborele de subcategorii poate avea cel mult {maximum} niveluri (Setări → Catalog).";
    public static string ParentHasProductsMessage(string parent, int products) =>
        $"Subcategoria «{parent}» are {products} produse. Ca să primească subcategorii copil, produsele ei trebuie mutate în prima subcategorie copil nou creată; confirmă mutarea.";
}

// Ancestry helpers over the parent_id column. The tree is small, so it is read whole and walked in memory.
internal static class SubcategoryAncestry
{
    internal static async Task<Dictionary<int, int?>> ParentsAsync(MySqlConnection connection, MySqlTransaction? transaction, CancellationToken token)
    {
        var parents = new Dictionary<int, int?>();
        await using var command = Command(connection, transaction, "SELECT id,parent_id FROM subcategories");
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
            parents[checked((int)reader.GetInt64(0))] = reader.IsDBNull(1) ? null : checked((int)reader.GetInt64(1));
        return parents;
    }

    // The node and its ancestors, root first.
    internal static List<int> Chain(IReadOnlyDictionary<int, int?> parents, int id)
    {
        var chain = new List<int>();
        for (int? current = id; current is { } value && parents.ContainsKey(value) && chain.Count <= SubcategoryTreeRules.WalkGuard + 1; current = parents[value])
            chain.Insert(0, value);
        return chain;
    }

    internal static List<int> Descendants(IReadOnlyDictionary<int, int?> parents, int id)
    {
        var result = new List<int>();
        var queue = new Queue<int>([id]);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var pair in parents.Where(item => item.Value == current))
            {
                result.Add(pair.Key);
                queue.Enqueue(pair.Key);
            }
        }
        return result;
    }

    internal static async Task<List<int>> ChainAsync(MySqlConnection connection, MySqlTransaction? transaction, int id, CancellationToken token) =>
        Chain(await ParentsAsync(connection, transaction, token).ConfigureAwait(false), id);

    // Where a parameter or tag name must be unique: the node, its ancestors and its descendants.
    internal static async Task<List<int>> NameScopeAsync(MySqlConnection connection, MySqlTransaction? transaction, int id, CancellationToken token)
    {
        var parents = await ParentsAsync(connection, transaction, token).ConfigureAwait(false);
        return [.. Chain(parents, id), .. Descendants(parents, id)];
    }

    internal static string InList(IEnumerable<int> ids) => string.Join(",", ids.Select(item => item.ToString(CultureInfo.InvariantCulture)));

    internal static async Task<bool> HasChildrenAsync(MySqlConnection connection, MySqlTransaction? transaction, int id, CancellationToken token)
    {
        await using var command = Command(connection, transaction, "SELECT EXISTS(SELECT 1 FROM subcategories WHERE parent_id=@id)", ("@id", id));
        return Convert.ToBoolean(await command.ExecuteScalarAsync(token).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }
}

public sealed partial class MariaProductRepository : ISubcategoryTree
{
    public async Task<IReadOnlyList<SubcategoryNode>> GetTreeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        var rows = new List<(int Id, string Category, int CategoryOrder, string Name, int? Parent, int Order, int Products)>();
        await using (var command = Command(connection, null, """
            SELECT s.id,c.name,c.sort_order,s.name,s.parent_id,s.sort_order,(SELECT COUNT(*) FROM products p WHERE p.subcategory_id=s.id)
            FROM subcategories s INNER JOIN categories c ON c.id=s.category_id
            """))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                rows.Add((checked((int)reader.GetInt64(0)), reader.GetString(1), reader.GetInt32(2), reader.GetString(3),
                    reader.IsDBNull(4) ? null : checked((int)reader.GetInt64(4)), reader.GetInt32(5), checked((int)reader.GetInt64(6))));
        var names = rows.ToDictionary(row => row.Id, row => row.Name);
        var children = rows.ToLookup(row => row.Parent);
        var result = new List<SubcategoryNode>();
        void Walk((int Id, string Category, int CategoryOrder, string Name, int? Parent, int Order, int Products) row, int depth)
        {
            var below = children[row.Id].OrderBy(child => child.Order).ThenBy(child => child.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            result.Add(new SubcategoryNode(row.Id, row.Category, row.Name, row.Parent is { } parent ? names[parent] : null, depth, below.Count == 0, row.Products));
            foreach (var child in below) Walk(child, depth + 1);
        }
        foreach (var root in rows.Where(row => row.Parent is null).OrderBy(row => row.CategoryOrder).ThenBy(row => row.Category, StringComparer.CurrentCultureIgnoreCase)
                     .ThenBy(row => row.Order).ThenBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase))
            Walk(root, 0);
        return result;
    }

    public async Task<ProductGroup> CreateChildAsync(ProductGroup parent, string name, bool moveProducts, CancellationToken cancellationToken = default)
    {
        await EnsureKeyAsync("categorii.add", cancellationToken).ConfigureAwait(false);
        var cleanName = ProductGroupManagementRules.Name(name, "Denumire subcategorie");
        var result = await WriteAsync(async (connection, transaction) =>
        {
            int parentId, categoryId;
            string parentName, categoryName;
            await using (var find = Command(connection, transaction, """
                SELECT s.id,s.name,c.id,c.name FROM subcategories s INNER JOIN categories c ON c.id=s.category_id
                WHERE s.normalized_name=@sub AND c.normalized_name=@cat FOR UPDATE
                """, ("@sub", TextNormalization.UniquenessKey(parent.Subcategory)), ("@cat", TextNormalization.UniquenessKey(parent.Category))))
            await using (var reader = await find.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    throw new ProductOperationException("Subcategoria părinte nu mai există. Actualizează lista.");
                (parentId, parentName, categoryId, categoryName) = (checked((int)reader.GetInt64(0)), reader.GetString(1), checked((int)reader.GetInt64(2)), reader.GetString(3));
            }
            var maximumDepth = await MariaCatalogSettings.ReadAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            if ((await SubcategoryAncestry.ChainAsync(connection, transaction, parentId, cancellationToken).ConfigureAwait(false)).Count >= maximumDepth)
                throw new ProductOperationException(SubcategoryTreeRules.TooDeepMessage(maximumDepth));
            await using (var duplicate = Command(connection, transaction, """
                SELECT s.name,c.name FROM subcategories s INNER JOIN categories c ON c.id=s.category_id WHERE s.normalized_name=@key LIMIT 1
                """, ("@key", TextNormalization.UniquenessKey(cleanName))))
            await using (var reader = await duplicate.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    throw new ProductOperationException($"Subcategoria «{reader.GetString(0)}» există deja în categoria «{reader.GetString(1)}».");
            await using (var asCategory = Command(connection, transaction, "SELECT name FROM categories WHERE normalized_name=@key LIMIT 1",
                             ("@key", TextNormalization.UniquenessKey(cleanName))))
                if (await asCategory.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is string categoryNamed)
                    throw new ProductOperationException(ProductGroupManagementRules.NameTakenByCategory(categoryNamed));
            int products;
            await using (var count = Command(connection, transaction, "SELECT COUNT(*) FROM products WHERE subcategory_id=@id FOR UPDATE", ("@id", parentId)))
                products = Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
            if (products > 0 && !moveProducts) throw new ProductOperationException(SubcategoryTreeRules.ParentHasProductsMessage(parentName, products));
            await using var insert = Command(connection, transaction, """
                INSERT INTO subcategories(category_id,parent_id,name,normalized_name,sort_order)
                SELECT @category,@parent,@name,@key,COALESCE(MAX(sort_order),0)+1 FROM subcategories WHERE parent_id=@parent
                """, ("@category", categoryId), ("@parent", parentId), ("@name", cleanName), ("@key", TextNormalization.UniquenessKey(cleanName)));
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            var childId = checked((int)insert.LastInsertedId);
            if (products > 0)
            {
                // The parameter values and the tags of the products point to parameters and tags of the parent, which the child inherits: nothing is lost.
                await using var move = Command(connection, transaction, "UPDATE products SET subcategory_id=@child,version=version+1 WHERE subcategory_id=@parent",
                    ("@child", childId), ("@parent", parentId));
                await move.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            return (ChildId: childId, ParentName: parentName, CategoryName: categoryName, Products: products);
        }, cancellationToken).ConfigureAwait(false);
        var target = $"{result.CategoryName} / {result.ParentName} / {cleanName}";
        await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.Subcategory, AuditActions.AddChildSubcategory,
            result.ChildId.ToString(CultureInfo.InvariantCulture), target,
            AuditDetails.Identification(("Denumire", cleanName), ("Părinte", result.ParentName), ("Categorie", result.CategoryName)), string.Empty, cancellationToken).ConfigureAwait(false);
        if (result.Products > 0)
            await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.Subcategory, AuditActions.MoveProductsToChildSubcategory,
                result.ChildId.ToString(CultureInfo.InvariantCulture), target,
                AuditDetails.Identification(("Din", result.ParentName), ("În", cleanName), ("Produse mutate", result.Products.ToString(CultureInfo.InvariantCulture))),
                string.Empty, cancellationToken).ConfigureAwait(false);
        return new ProductGroup(result.CategoryName, cleanName);
    }

    private sealed class DryRunMove(BranchMovePreview preview) : Exception("Previzualizare")
    {
        public BranchMovePreview Preview { get; } = preview;
    }

    public Task<SubcategoryMoveResult> MoveAsync(ProductGroup node, ProductGroup? newParent, string targetCategory, int position, CancellationToken cancellationToken = default) =>
        MoveCoreAsync(node, newParent, targetCategory, position, null, false, null, cancellationToken);

    public Task<SubcategoryMoveResult> MoveAsync(ProductGroup node, ProductGroup? newParent, string targetCategory, int position, BranchMoveChoices choices, CancellationToken cancellationToken = default) =>
        MoveCoreAsync(node, newParent, targetCategory, position, choices, false, null, cancellationToken);

    public async Task<BranchMovePreview> PreviewMoveAsync(ProductGroup node, ProductGroup? newParent, string targetCategory, int position, BranchMoveChoices? choices, CancellationToken cancellationToken = default)
    {
        try { await MoveCoreAsync(node, newParent, targetCategory, position, choices, true, null, cancellationToken).ConfigureAwait(false); }
        catch (DryRunMove done) { return done.Preview; }
        return new BranchMovePreview([], [], []);
    }

    // The promotion of a leaf to a category (stage 7) is a branch move into a category created in the same transaction, after the leaf got its new name.
    private sealed record PromotionRequest(string NewName, int CategoryPosition);

    public async Task<BranchMovePreview> PreviewPromotionAsync(ProductGroup leaf, string newName, int categoryPosition, BranchMoveChoices? choices, CancellationToken cancellationToken = default)
    {
        try { await MoveCoreAsync(leaf, null, "", 0, choices, true, new PromotionRequest(newName, categoryPosition), cancellationToken).ConfigureAwait(false); }
        catch (DryRunMove done) { return done.Preview; }
        return new BranchMovePreview([], [], []);
    }

    public Task<SubcategoryMoveResult> PromoteAsync(ProductGroup leaf, string newName, int categoryPosition, BranchMoveChoices choices, CancellationToken cancellationToken = default) =>
        MoveCoreAsync(leaf, null, "", 0, choices, false, new PromotionRequest(newName, categoryPosition), cancellationToken);

    // Before the move: the leaf is renamed (its old name goes to the new category) and the category is created at its place. Returns the leaf under its new name, the new category and the products on the leaf.
    private static async Task<(ProductGroup Leaf, string Category, int Products)> PromoteStepAsync(MySqlConnection connection, MySqlTransaction transaction, ProductGroup leaf, PromotionRequest request, CancellationToken token)
    {
        int id; string oldName, oldCategory;
        await using (var find = Command(connection, transaction, """
            SELECT s.id,s.name,c.name FROM subcategories s INNER JOIN categories c ON c.id=s.category_id
            WHERE s.normalized_name=@sub AND c.normalized_name=@cat FOR UPDATE
            """, ("@sub", TextNormalization.UniquenessKey(leaf.Subcategory)), ("@cat", TextNormalization.UniquenessKey(leaf.Category))))
        await using (var reader = await find.ExecuteReaderAsync(token).ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(token).ConfigureAwait(false)) throw new ProductOperationException("Subcategoria nu mai există. Actualizează lista.");
            (id, oldName, oldCategory) = (checked((int)reader.GetInt64(0)), reader.GetString(1), reader.GetString(2));
        }
        if (await SubcategoryAncestry.HasChildrenAsync(connection, transaction, id, token).ConfigureAwait(false)) throw new ProductOperationException(SubcategoryTreeRules.PromoteLeafOnlyMessage);
        var name = ProductGroupManagementRules.Name(request.NewName, "Denumire subcategorie");
        var key = TextNormalization.UniquenessKey(name);
        if (key == TextNormalization.UniquenessKey(oldName)) throw new ProductOperationException(SubcategoryTreeRules.PromotionSameNameMessage(oldName));
        await using (var sameSubcategory = Command(connection, transaction, """
            SELECT s.name,c.name FROM subcategories s INNER JOIN categories c ON c.id=s.category_id WHERE s.normalized_name=@key AND s.id<>@id LIMIT 1
            """, ("@key", key), ("@id", id)))
        await using (var reader = await sameSubcategory.ExecuteReaderAsync(token).ConfigureAwait(false))
            if (await reader.ReadAsync(token).ConfigureAwait(false)) throw new ProductOperationException(ProductGroupManagementRules.NameTakenBySubcategory(reader.GetString(0), reader.GetString(1)));
        await using (var sameCategory = Command(connection, transaction, "SELECT name FROM categories WHERE normalized_name=@key LIMIT 1", ("@key", key)))
            if (await sameCategory.ExecuteScalarAsync(token).ConfigureAwait(false) is string category) throw new ProductOperationException(ProductGroupManagementRules.NameTakenByCategory(category));
        int products;
        await using (var count = Command(connection, transaction, "SELECT COUNT(*) FROM products WHERE subcategory_id=@id", ("@id", id)))
            products = Convert.ToInt32(await count.ExecuteScalarAsync(token).ConfigureAwait(false), CultureInfo.InvariantCulture);
        await using (var rename = Command(connection, transaction, "UPDATE subcategories SET name=@name,normalized_name=@key WHERE id=@id", ("@name", name), ("@key", key), ("@id", id)))
            await rename.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        int categoryId;
        await using (var insert = Command(connection, transaction, "INSERT INTO categories(name,normalized_name,sort_order) VALUES(@name,@key,0)",
                         ("@name", oldName), ("@key", TextNormalization.UniquenessKey(oldName))))
        {
            await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            categoryId = checked((int)insert.LastInsertedId);
        }
        var order = new List<int>();
        await using (var select = Command(connection, transaction, "SELECT id FROM categories WHERE id<>@id ORDER BY sort_order,name", ("@id", categoryId)))
        await using (var reader = await select.ExecuteReaderAsync(token).ConfigureAwait(false))
            while (await reader.ReadAsync(token).ConfigureAwait(false)) order.Add(checked((int)reader.GetInt64(0)));
        order.Insert(Math.Clamp(request.CategoryPosition, 0, order.Count), categoryId);
        for (var index = 0; index < order.Count; index++)
            await using (var number = Command(connection, transaction, "UPDATE categories SET sort_order=@order WHERE id=@id", ("@order", index + 1), ("@id", order[index])))
                await number.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        return (new ProductGroup(oldCategory, name), oldName, products);
    }

    private async Task<SubcategoryMoveResult> MoveCoreAsync(ProductGroup node, ProductGroup? newParent, string targetCategory, int position, BranchMoveChoices? choices, bool dryRun, PromotionRequest? promotion, CancellationToken cancellationToken)
    {
        if (accessControl is not null) await accessControl.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var actor = accessControl is null ? "" : await accessControl.GetUsernameAsync(cancellationToken).ConfigureAwait(false) ?? "";
        var reasonText = choices is null || choices.Reason.Length == 0 ? "" : ProductGroupManagementRules.Reason(choices.Reason);
        var result = await WriteAsync(async (connection, transaction) =>
        {
            async Task<(int Id, string Name, int CategoryId, string Category)> FindAsync(ProductGroup group, string missing)
            {
                await using var find = Command(connection, transaction, """
                    SELECT s.id,s.name,c.id,c.name FROM subcategories s INNER JOIN categories c ON c.id=s.category_id
                    WHERE s.normalized_name=@sub AND c.normalized_name=@cat FOR UPDATE
                    """, ("@sub", TextNormalization.UniquenessKey(group.Subcategory)), ("@cat", TextNormalization.UniquenessKey(group.Category)));
                await using var reader = await find.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) throw new ProductOperationException(missing);
                return (checked((int)reader.GetInt64(0)), reader.GetString(1), checked((int)reader.GetInt64(2)), reader.GetString(3));
            }
            var promotedProducts = 0;
            var targetName = targetCategory;
            var nodeGroup = node;
            if (promotion is not null)
            {
                var step = await PromoteStepAsync(connection, transaction, node, promotion, cancellationToken).ConfigureAwait(false);
                (nodeGroup, targetName, promotedProducts) = (step.Leaf, step.Category, step.Products);
            }
            var moved = await FindAsync(nodeGroup, "Subcategoria nu mai există. Actualizează lista.").ConfigureAwait(false);
            int? parentId = null;
            string? parentName = null;
            int categoryId;
            string categoryName;
            if (newParent is not null)
            {
                var target = await FindAsync(newParent, "Subcategoria destinație nu mai există. Actualizează lista.").ConfigureAwait(false);
                (parentId, parentName, categoryId, categoryName) = (target.Id, target.Name, target.CategoryId, target.Category);
            }
            else
            {
                await using var findCategory = Command(connection, transaction, "SELECT id,name FROM categories WHERE normalized_name=@key",
                    ("@key", TextNormalization.UniquenessKey(targetName)));
                await using var reader = await findCategory.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) throw new ProductOperationException("Categoria destinație nu mai există. Actualizează lista.");
                (categoryId, categoryName) = (checked((int)reader.GetInt64(0)), reader.GetString(1));
            }
            var parents = await SubcategoryAncestry.ParentsAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            var subtree = new List<int> { moved.Id };
            subtree.AddRange(SubcategoryAncestry.Descendants(parents, moved.Id));
            if (parentId is { } into && subtree.Contains(into))
                throw new ProductOperationException("O subcategorie nu poate fi mutată în propriul ei subarbore.");
            var oldParentId = parents[moved.Id];
            var oldAncestors = SubcategoryAncestry.Chain(parents, moved.Id).Where(id => id != moved.Id).ToList();
            var newAncestors = parentId is { } parent ? SubcategoryAncestry.Chain(parents, parent) : [];
            var sameBranch = oldParentId == parentId && moved.CategoryId == categoryId;   // a root moved to another category is a move, not a new place
            var fromParent = oldParentId is { } oldId ? await NameOfAsync(connection, transaction, oldId, cancellationToken).ConfigureAwait(false) : null;

            BranchMovePlan? plan = null;
            IReadOnlySet<int> incompleteBefore = new HashSet<int>();
            var archivedBlocks = new List<string>();
            if (!sameBranch)
            {
                var maximum = await MariaCatalogSettings.ReadAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
                var height = subtree.Max(id => SubcategoryAncestry.Chain(parents, id).Count) - oldAncestors.Count;
                if (newAncestors.Count + height > maximum) throw new ProductOperationException(SubcategoryTreeRules.TooDeepMessage(maximum));
                if (parentId is { } target)
                {
                    await using var hasProducts = Command(connection, transaction, "SELECT COUNT(*) FROM products WHERE subcategory_id=@id", ("@id", target));
                    if (Convert.ToInt32(await hasProducts.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture) > 0)
                        throw new ProductOperationException($"Subcategoria «{parentName}» are produse și nu poate primi subcategorii copil. Mută întâi produsele ei într-o frunză.");
                }
                // The definitions the branch inherits (stage 6): planned on the tree as it is now, executed after the structure changes.
                var snapshot = await MariaDefinitionRelocation.LoadAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
                plan = BranchMovePlanner.Plan(snapshot, moved.Id, parentId, categoryId, choices?.ConfirmedMerges);
                var lostOwners = oldAncestors.Where(id => !newAncestors.Contains(id)).Select(id => id).ToList();
                var lostOwnerSql = string.Join(",", lostOwners.Select(id => id.ToString(CultureInfo.InvariantCulture)).Concat(moved.CategoryId != categoryId ? [(-moved.CategoryId).ToString(CultureInfo.InvariantCulture)] : []));
                if (lostOwnerSql.Length > 0)
                {
                    // An archived parameter of a parent the branch leaves cannot be restored for the products of the branch afterwards.
                    await using var archived = Command(connection, transaction, $"""
                        SELECT DISTINCT sp.name FROM product_parameter_values pv INNER JOIN subcategory_parameters sp ON sp.id=pv.parameter_id INNER JOIN products p ON p.id=pv.product_id
                        WHERE sp.archived_utc IS NOT NULL AND COALESCE(sp.subcategory_id,-sp.category_id) IN ({lostOwnerSql}) AND p.subcategory_id IN ({SubcategoryAncestry.InList(subtree)})
                        """);
                    await using var archivedReader = await archived.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                    while (await archivedReader.ReadAsync(cancellationToken).ConfigureAwait(false))
                        archivedBlocks.Add($"Parametrul arhivat «{archivedReader.GetString(0)}» al unui părinte pe care ramura îl părăsește are produse în ramură; restaurează-l sau renunță la restaurare înainte de mutare.");
                }
                incompleteBefore = await MariaProductParameterRepository.IncompleteIdsAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            }
            var categoryChanged = moved.CategoryId != categoryId;
            if (!sameBranch)
            {
                await using var reparent = Command(connection, transaction, "UPDATE subcategories SET parent_id=@parent WHERE id=@id",
                    ("@parent", parentId is null ? DBNull.Value : parentId.Value), ("@id", moved.Id));
                await reparent.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            if (categoryChanged)
            {
                await using var recategorize = Command(connection, transaction, $"UPDATE subcategories SET category_id=@category WHERE id IN ({SubcategoryAncestry.InList(subtree)})", ("@category", categoryId));
                await recategorize.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                await using var products = Command(connection, transaction, $"UPDATE products SET category_id=@category,version=version+1 WHERE subcategory_id IN ({SubcategoryAncestry.InList(subtree)})", ("@category", categoryId));
                await products.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            // The place among the new siblings: the others keep their order, the moved node goes at the wanted place and everything is numbered again.
            var siblings = new List<int>();
            await using (var select = Command(connection, transaction,
                "SELECT id FROM subcategories WHERE category_id=@category AND parent_id <=> @parent AND id<>@id ORDER BY sort_order,name",
                ("@category", categoryId), ("@parent", parentId is null ? DBNull.Value : parentId.Value), ("@id", moved.Id)))
            await using (var reader = await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) siblings.Add(checked((int)reader.GetInt64(0)));
            var place = Math.Clamp(position, 0, siblings.Count);
            siblings.Insert(place, moved.Id);
            for (var index = 0; index < siblings.Count; index++)
            {
                await using var number = Command(connection, transaction, "UPDATE subcategories SET sort_order=@order WHERE id=@id", ("@order", index + 1), ("@id", siblings[index]));
                await number.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            // The definitions: copied, merged and measured on the real result (the products that miss values of parameters the branch now inherits are listed).
            var incompleteNow = new List<IncompleteProduct>();
            var copied = 0;
            var merged = 0;
            if (plan is not null)
            {
                var blocks = plan.Blocks.Concat(archivedBlocks).ToList();
                var undecided = plan.Items.Any(item => item.Action is BranchMoveAction.NeedsDecision or BranchMoveAction.Blocked) || blocks.Count > 0;
                if (!undecided)
                {
                    foreach (var operation in plan.Operations) await MariaDefinitionRelocation.ExecuteBranchAsync(connection, transaction, operation, subtree, actor, reasonText, cancellationToken).ConfigureAwait(false);
                    var after = await MariaProductParameterRepository.IncompleteIdsAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
                    var newly = after.Where(id => !incompleteBefore.Contains(id)).ToList();
                    if (newly.Count > 0)
                    {
                        await using var names = Command(connection, transaction, $"SELECT id,name FROM products WHERE id IN ({SubcategoryAncestry.InList(newly)}) ORDER BY name");
                        await using var nameReader = await names.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                        while (await nameReader.ReadAsync(cancellationToken).ConfigureAwait(false)) incompleteNow.Add(new IncompleteProduct(checked((int)nameReader.GetInt64(0)), nameReader.GetString(1)));
                    }
                    copied = plan.Items.Count(item => item.Action == BranchMoveAction.Copy);
                    merged = plan.Items.Count(item => item.Action is BranchMoveAction.CopyMerged or BranchMoveAction.OwnMerged);
                }
                var preview = new BranchMovePreview(plan.Items, blocks, incompleteNow);
                if (dryRun) throw new DryRunMove(preview);
                if (undecided)
                {
                    if (choices is null) throw new MoveNeedsVerificationException();
                    throw new ProductOperationException(blocks.Count > 0 ? blocks[0] : RelocationMessages.Undecided(plan.Items.First(item => item.Action == BranchMoveAction.NeedsDecision).Name));
                }
                if (!preview.Plain && choices is null) throw new MoveNeedsVerificationException();
                if (incompleteNow.Count > 0 && choices is { AcceptIncomplete: false }) throw new ProductOperationException(RelocationMessages.NotAccepted(incompleteNow.Count));
            }
            else if (dryRun) throw new DryRunMove(new BranchMovePreview([], [], []));
            var incomplete = incompleteNow.Count;
            return (Id: moved.Id, Name: moved.Name, FromCategory: moved.Category, FromParent: fromParent, ToCategory: categoryName, ToParent: parentName,
                Size: subtree.Count, SameBranch: sameBranch, Incomplete: incomplete, Place: place + 1, Copied: copied, Merged: merged, Products: promotedProducts);
        }, cancellationToken).ConfigureAwait(false);
        var target = $"{result.ToCategory} / {result.Name}";
        if (promotion is not null)
            await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.Subcategory, AuditActions.PromoteSubcategory,
                result.Id.ToString(CultureInfo.InvariantCulture), target,
                AuditDetails.Identification(("Subcategorie veche", node.Subcategory), ("Din categoria", result.FromCategory), ("Categorie nouă", result.ToCategory), ("Subcategorie nouă", result.Name),
                    ("Poziția categoriei", (Math.Max(promotion.CategoryPosition, 0) + 1).ToString(CultureInfo.InvariantCulture)), ("Produse rămase pe subcategorie", result.Products.ToString(CultureInfo.InvariantCulture)),
                    ("Definiții copiate pe nod", result.Copied.ToString(CultureInfo.InvariantCulture)), ("Definiții îmbinate", result.Merged.ToString(CultureInfo.InvariantCulture)),
                    ("Produse devenite incomplete", result.Incomplete.ToString(CultureInfo.InvariantCulture))),
                reasonText.Length > 0 ? reasonText : "Subcategoria a devenit categorie prin tragere.", cancellationToken).ConfigureAwait(false);
        else if (result.SameBranch)
            await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.Subcategory, AuditActions.ReorderSubcategories,
                result.Id.ToString(CultureInfo.InvariantCulture), target,
                AuditDetails.Identification(("Subcategorie", result.Name), ("Părinte", result.ToParent ?? result.ToCategory), ("Poziția nouă", result.Place.ToString(CultureInfo.InvariantCulture))),
                "Ordinea subcategoriilor a fost schimbată prin tragere.", cancellationToken).ConfigureAwait(false);
        else
            await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.Subcategory, AuditActions.MoveSubcategory,
                result.Id.ToString(CultureInfo.InvariantCulture), target,
                AuditDetails.Identification(("Subcategorie", result.Name), ("Din", result.FromParent is null ? result.FromCategory : $"{result.FromCategory} / {result.FromParent}"),
                    ("În", result.ToParent is null ? result.ToCategory : $"{result.ToCategory} / {result.ToParent}"), ("Noduri mutate (cu tot ce e sub ea)", result.Size.ToString(CultureInfo.InvariantCulture)),
                    ("Definiții copiate pe nod", result.Copied.ToString(CultureInfo.InvariantCulture)), ("Definiții îmbinate", result.Merged.ToString(CultureInfo.InvariantCulture)),
                    ("Produse devenite incomplete", result.Incomplete.ToString(CultureInfo.InvariantCulture))),
                reasonText.Length > 0 ? reasonText : "Subcategoria a fost mutată prin tragere, împreună cu structura de sub ea.", cancellationToken).ConfigureAwait(false);
        return new SubcategoryMoveResult(result.Size, result.Incomplete, result.SameBranch);
    }

    private static async Task<string?> NameOfAsync(MySqlConnection connection, MySqlTransaction transaction, int id, CancellationToken token)
    {
        await using var command = Command(connection, transaction, "SELECT name FROM subcategories WHERE id=@id", ("@id", id));
        return await command.ExecuteScalarAsync(token).ConfigureAwait(false) as string;
    }
}

// "Parent › Child › Leaf" for a subcategory of the tree (just the name when the tree does not know it): how a node is named where only a leaf is chosen.
public static class SubcategoryPath
{
    public static string Label(IReadOnlyList<SubcategoryNode> tree, string category, string subcategory)
    {
        var parts = new List<string>();
        var node = tree.FirstOrDefault(item => string.Equals(item.Category, category, StringComparison.OrdinalIgnoreCase) && string.Equals(item.Name, subcategory, StringComparison.OrdinalIgnoreCase));
        while (node is not null && parts.Count <= SubcategoryTreeRules.WalkGuard)
        {
            parts.Insert(0, node.Name);
            var parent = node.Parent;
            node = parent is null ? null : tree.FirstOrDefault(item => string.Equals(item.Category, category, StringComparison.OrdinalIgnoreCase) && item.Name == parent);
        }
        return parts.Count == 0 ? subcategory : string.Join(" › ", parts);
    }
}
