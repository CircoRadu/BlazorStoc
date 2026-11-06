using BlazorStoc.Components.Pages;
using BlazorStoc.Components.Shared;
using BlazorStoc.Services;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorStoc.Checks;

// The verification dialog of a move that changes what a branch inherits (stage 6), on a fake tree that answers like the real one.
public static class BranchMoveChecks
{
    private static void Expect(bool condition) { if (!condition) throw new InvalidOperationException("The dialog does not show the expected state yet."); }

    private sealed class VerifyingTree(IReadOnlyList<SubcategoryNode> nodes, DefinitionRef clash) : ISubcategoryTree
    {
        public List<BranchMoveChoices?> Previews { get; } = [];
        public List<(ProductGroup Node, ProductGroup? Parent, string Category, int Position, BranchMoveChoices Choices)> Moves { get; } = [];
        public int PlainMoves { get; private set; }
        public Task<IReadOnlyList<SubcategoryNode>> GetTreeAsync(CancellationToken cancellationToken = default) => Task.FromResult(nodes);
        public Task<ProductGroup> CreateChildAsync(ProductGroup parent, string name, bool moveProducts, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        // The plain move asks for the verification, as the real service does when definitions are involved.
        public Task<SubcategoryMoveResult> MoveAsync(ProductGroup node, ProductGroup? newParent, string targetCategory, int position, CancellationToken cancellationToken = default)
        {
            PlainMoves++;
            throw new MoveNeedsVerificationException();
        }

        public Task<BranchMovePreview> PreviewMoveAsync(ProductGroup node, ProductGroup? newParent, string targetCategory, int position, BranchMoveChoices? choices, CancellationToken cancellationToken = default)
        {
            Previews.Add(choices);
            var merged = choices?.ConfirmedMerges?.Contains(clash) == true;
            BranchMoveItem[] items =
            [
                new(new DefinitionRef(DefinitionKind.Tag, 7), "PoE", BranchMoveAction.Copy, "„Camere”", "„Camere IP”", [], "Eticheta se copiază pe nodul mutat."),
                new(clash, "Lentila", merged ? BranchMoveAction.CopyMerged : BranchMoveAction.NeedsDecision, "„Camere”", "„Noul părinte”", merged ? [new ValueMapping("2.8 mm", true, 1)] : [], "Există și la noii părinți.")
            ];
            return Task.FromResult(new BranchMovePreview(items, [], merged ? [new IncompleteProduct(9, "CAM-9")] : []));
        }

        public Task<SubcategoryMoveResult> MoveAsync(ProductGroup node, ProductGroup? newParent, string targetCategory, int position, BranchMoveChoices choices, CancellationToken cancellationToken = default)
        {
            Moves.Add((node, newParent, targetCategory, position, choices));
            return Task.FromResult(new SubcategoryMoveResult(1, 1, false));
        }

        // The promotion of a leaf to a category (stage 7): the preview answers like the move preview, the name and the place are recorded.
        public List<(string Name, int Position, BranchMoveChoices? Choices)> PromotionPreviews { get; } = [];
        public List<(ProductGroup Leaf, string Name, int Position, BranchMoveChoices Choices)> Promotions { get; } = [];

        public Task<BranchMovePreview> PreviewPromotionAsync(ProductGroup leaf, string newName, int categoryPosition, BranchMoveChoices? choices, CancellationToken cancellationToken = default)
        {
            PromotionPreviews.Add((newName, categoryPosition, choices));
            return PreviewMoveAsync(leaf, null, "", 0, choices, cancellationToken);
        }

        public Task<SubcategoryMoveResult> PromoteAsync(ProductGroup leaf, string newName, int categoryPosition, BranchMoveChoices choices, CancellationToken cancellationToken = default)
        {
            Promotions.Add((leaf, newName, categoryPosition, choices));
            return Task.FromResult(new SubcategoryMoveResult(1, 1, false));
        }
    }

    public static async Task RunAsync(Action<bool, string> check)
    {
        var access = new TestAccessControl(true, "branch.admin");
        var lensRef = new DefinitionRef(DefinitionKind.Parameter, 3);
        var node = new ProductGroup("Supraveghere", "Camere IP");
        var parent = new ProductGroup("Supraveghere", "Alt parinte");
        var tree = new VerifyingTree([new SubcategoryNode(1, "Supraveghere", "Camere IP", null, 0, true, 0)], lensRef);
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddLogging();
        context.Services.AddSingleton<IAccessControl>(access);
        context.Services.AddSingleton<ISubcategoryTree>(tree);
        int closed = 0;
        SubcategoryMoveResult? done = null;
        var dialog = context.Render<BranchMoveDialog>(builder => builder.Add(item => item.Node, node).Add(item => item.NewParent, parent).Add(item => item.Destination, "Supraveghere").Add(item => item.Position, 2)
            .Add(item => item.Closed, EventCallback.Factory.Create(new object(), () => closed++)).Add(item => item.Moved, EventCallback.Factory.Create<SubcategoryMoveResult>(new object(), (SubcategoryMoveResult result) => done = result)));
        dialog.WaitForAssertion(() => Expect(dialog.FindAll(".take-preview tbody tr").Count == 2), TimeSpan.FromSeconds(10));
        check(tree.Previews.Count == 1 && dialog.Find(".take-preview").TextContent.Contains("Se copiază pe subcategoria mutată", StringComparison.Ordinal)
              && dialog.FindAll(".take-preview tr.restore-problem").Count == 1 && dialog.Find(".take-dialog .editor-actions button.primary").HasAttribute("disabled"),
            "Branch move dialog: the preview lists what is copied and the namesake that needs a decision; the move waits");

        dialog.Find(".take-preview tbody tr.restore-problem input[role=switch]").Change(true);
        dialog.WaitForAssertion(() => Expect(dialog.FindAll(".take-incomplete").Count == 1), TimeSpan.FromSeconds(10));
        check(tree.Previews.Count == 2 && tree.Previews[1]!.ConfirmedMerges!.SequenceEqual([lensRef]) && dialog.Find(".take-values").TextContent.Contains("există deja", StringComparison.Ordinal),
            "Branch move dialog: the merge decision is sent with the preview and the values and the incomplete products are shown");
        dialog.Find("#branch-reason").Input("Mutare cu verificari");
        check(dialog.Find(".take-dialog .editor-actions button.primary").HasAttribute("disabled"), "Branch move dialog: the incomplete products must be accepted first");
        dialog.FindAll("input[role=switch]").Last().Change(true);
        dialog.WaitForAssertion(() => Expect(!dialog.Find(".take-dialog .editor-actions button.primary").HasAttribute("disabled")));
        dialog.Find(".take-dialog .editor-actions button.primary").Click();
        dialog.WaitForAssertion(() => Expect(tree.Moves.Count == 1 && closed == 1), TimeSpan.FromSeconds(10));
        var move = tree.Moves[0];
        check(move.Node == node && move.Parent == parent && move.Category == "Supraveghere" && move.Position == 2 && move.Choices.AcceptIncomplete && move.Choices.ConfirmedMerges!.SequenceEqual([lensRef]) && move.Choices.Reason == "Mutare cu verificari" && done is { IncompleteProducts: 1 },
            "Branch move dialog: the move carries the node, the new place, the merge decision, the acceptance and the reason");

        // The categories page: a drag whose move asks for the verification opens the dialog instead of showing an error.
        var repository = new DemoProductRepository(access);
        var groups = (await repository.GetGroupsAsync()).Where(group => group.Subcategory.Length > 0).ToList();
        var category = groups[0].Category;
        var names = groups.Where(group => group.Category == category).Select(group => group.Subcategory).ToList();
        if (names.Count < 2) { check(false, "Branch move dialog: the demo catalog has a category with two subcategories"); return; }
        var nodes = names.Select((name, index) => new SubcategoryNode(index + 1, category, name, null, 0, true, 0)).ToList();
        var pageTree = new VerifyingTree(nodes, lensRef);
        using var pageContext = new BunitContext();
        pageContext.JSInterop.Mode = JSRuntimeMode.Loose;
        pageContext.Services.AddLogging();
        pageContext.Services.AddSingleton<IAccessControl>(access);
        pageContext.Services.AddSingleton<IProductRepository>(repository);
        pageContext.Services.AddSingleton<IProductParameterRepository>(new DemoProductParameterRepository());
        pageContext.Services.AddSingleton<IProductTagRepository>(new DemoProductTagRepository());
        pageContext.Services.AddSingleton<ISubcategoryTree>(pageTree);
        pageContext.Services.AddSingleton<ICatalogSettings, DemoCatalogSettings>();
        pageContext.Services.AddScoped<UnsavedChanges>();
        var page = pageContext.Render<ProductGroups>();
        page.WaitForAssertion(() => Expect(page.FindAll("tr[data-node-name]").Count >= 2), TimeSpan.FromSeconds(10));
        await page.InvokeAsync(() => page.Instance.DropSubcategory(category, names[1], category, names[0], "inside"));
        page.WaitForAssertion(() => Expect(page.FindAll(".take-dialog").Count == 1), TimeSpan.FromSeconds(10));
        check(pageTree.PlainMoves == 1 && page.FindAll(".error-banner").Count == 0 && page.Find(".take-dialog h2").TextContent.Contains(names[1], StringComparison.Ordinal),
            "Branch move dialog: a drop that changes what the branch inherits opens the verification instead of an error");

        // Promotion of a leaf to a category (stage 7): the dialog asks for the new name first, nothing is checked before, and another name drops the previous checks.
        var promotionTree = new VerifyingTree([new SubcategoryNode(1, "Supraveghere", "Camere IP", null, 0, true, 0)], lensRef);
        using var promotionContext = new BunitContext();
        promotionContext.JSInterop.Mode = JSRuntimeMode.Loose;
        promotionContext.Services.AddLogging();
        promotionContext.Services.AddSingleton<IAccessControl>(access);
        promotionContext.Services.AddSingleton<ISubcategoryTree>(promotionTree);
        int promotionClosed = 0;
        SubcategoryMoveResult? promoted = null;
        var promotion = promotionContext.Render<BranchMoveDialog>(builder => builder.Add(item => item.Node, node).Add(item => item.Promote, true).Add(item => item.CategoryPosition, 3).Add(item => item.Destination, "")
            .Add(item => item.Closed, EventCallback.Factory.Create(new object(), () => promotionClosed++)).Add(item => item.Moved, EventCallback.Factory.Create<SubcategoryMoveResult>(new object(), (SubcategoryMoveResult result) => promoted = result)));
        check(promotionTree.PromotionPreviews.Count == 0 && promotion.FindAll(".take-preview").Count == 0 && promotion.Find("h2").TextContent.Contains("Transformare în categorie", StringComparison.Ordinal)
              && promotion.Find("#promote-name").GetAttribute("value") == "" && promotion.Find(".take-dialog .editor-actions button.primary").HasAttribute("disabled"),
            "Promotion dialog: it opens with the name field and no check, the check waits for a name");
        promotion.Find("#promote-name").Input("Camere IP general");
        promotion.Find(".take-dialog .editor-actions button.primary").Click();
        promotion.WaitForAssertion(() => Expect(promotion.FindAll(".take-preview tbody tr").Count == 2), TimeSpan.FromSeconds(10));
        check(promotionTree.PromotionPreviews.Count == 1 && promotionTree.PromotionPreviews[0].Name == "Camere IP general" && promotionTree.PromotionPreviews[0].Position == 3 && promotionTree.Moves.Count == 0
              && promotion.Find(".take-dialog .editor-actions button.primary").HasAttribute("disabled"),
            "Promotion dialog: the checks run with the chosen name and place; the structure waits for the decisions");
        promotion.Find("#promote-name").Input("Camere IP general 2");
        check(promotion.FindAll(".take-preview").Count == 0, "Promotion dialog: another name drops the previous checks");
        promotion.Find(".take-dialog .editor-actions button.primary").Click();
        promotion.WaitForAssertion(() => Expect(promotion.FindAll(".take-preview tbody tr").Count == 2), TimeSpan.FromSeconds(10));
        promotion.Find(".take-preview tbody tr.restore-problem input[role=switch]").Change(true);
        promotion.WaitForAssertion(() => Expect(promotion.FindAll(".take-incomplete").Count == 1), TimeSpan.FromSeconds(10));
        promotion.Find("#branch-reason").Input("Frunza devine categorie");
        promotion.FindAll("input[role=switch]").Last().Change(true);
        promotion.WaitForAssertion(() => Expect(!promotion.Find(".take-dialog .editor-actions button.primary").HasAttribute("disabled")));
        promotion.Find(".take-dialog .editor-actions button.primary").Click();
        promotion.WaitForAssertion(() => Expect(promotionTree.Promotions.Count == 1 && promotionClosed == 1), TimeSpan.FromSeconds(10));
        var made = promotionTree.Promotions[0];
        check(made.Leaf == node && made.Name == "Camere IP general 2" && made.Position == 3 && made.Choices.AcceptIncomplete && made.Choices.ConfirmedMerges!.SequenceEqual([lensRef]) && made.Choices.Reason == "Frunza devine categorie" && promoted is { IncompleteProducts: 1 },
            "Promotion dialog: the promotion carries the leaf, the new name, the place, the merge decision, the acceptance and the reason");

        // The page: a leaf dropped on a strip between the categories opens the promotion dialog (and writes nothing); a node with children is refused with a message.
        var stripTree = new VerifyingTree(nodes, lensRef);
        using var stripContext = new BunitContext();
        stripContext.JSInterop.Mode = JSRuntimeMode.Loose;
        stripContext.Services.AddLogging();
        stripContext.Services.AddSingleton<IAccessControl>(access);
        stripContext.Services.AddSingleton<IProductRepository>(repository);
        stripContext.Services.AddSingleton<IProductParameterRepository>(new DemoProductParameterRepository());
        stripContext.Services.AddSingleton<IProductTagRepository>(new DemoProductTagRepository());
        stripContext.Services.AddSingleton<ISubcategoryTree>(stripTree);
        stripContext.Services.AddSingleton<ICatalogSettings, DemoCatalogSettings>();
        stripContext.Services.AddScoped<UnsavedChanges>();
        var stripPage = stripContext.Render<ProductGroups>();
        stripPage.WaitForAssertion(() => Expect(stripPage.FindAll("tr[data-node-name]").Count >= 2), TimeSpan.FromSeconds(10));
        check(stripPage.FindAll(".category-drop-strip").Count == stripPage.FindAll(".category-drag-item").Count + 1 && stripPage.Find("tr[data-node-name]").HasAttribute("data-node-leaf") && stripPage.Find(".category-management-list").HasAttribute("data-max-depth"),
            "Promotion on the page: a strip between the categories and the data the tooltip reads are on the page");
        await stripPage.InvokeAsync(() => stripPage.Instance.DropSubcategory(category, names[0], "1", "", "newcategory"));
        stripPage.WaitForAssertion(() => Expect(stripPage.FindAll(".take-dialog").Count == 1), TimeSpan.FromSeconds(10));
        check(stripPage.Find(".take-dialog h2").TextContent.Contains("Transformare în categorie", StringComparison.Ordinal) && stripPage.Find(".take-dialog h2").TextContent.Contains(names[0], StringComparison.Ordinal)
              && stripTree.PromotionPreviews.Count == 0 && stripTree.Promotions.Count == 0,
            "Promotion on the page: a leaf dropped on a strip opens the dialog and nothing is checked or written yet");
    }
}
