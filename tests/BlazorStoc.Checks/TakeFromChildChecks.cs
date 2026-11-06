using BlazorStoc.Components.Shared;
using BlazorStoc.Services;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorStoc.Checks;

// "Preia de la copil" (stage 5): the wizard that takes the definitions of a child up to the node, on a fake relocation service that answers like the real one
// (a namesake needs a decision, a merge makes products incomplete, the apply gets what the user chose).
public static class TakeFromChildChecks
{
    private static void Expect(bool condition) { if (!condition) throw new InvalidOperationException("The dialog does not show the expected state yet."); }

    private sealed class FakeTree(IReadOnlyList<SubcategoryNode> nodes) : ISubcategoryTree
    {
        public Task<IReadOnlyList<SubcategoryNode>> GetTreeAsync(CancellationToken cancellationToken = default) => Task.FromResult(nodes);
        public Task<ProductGroup> CreateChildAsync(ProductGroup parent, string name, bool moveProducts, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SubcategoryMoveResult> MoveAsync(ProductGroup node, ProductGroup? newParent, string targetCategory, int position, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeRelocation(Func<DefinitionRef, string> names, HashSet<DefinitionRef> clashes) : IDefinitionRelocation
    {
        public List<RelocationRequest> Previews { get; } = [];
        public List<(RelocationRequest Request, string Reason)> Applied { get; } = [];

        private RelocationPreview Answer(RelocationRequest request)
        {
            var confirmed = request.ConfirmedMerges ?? [];
            var items = request.Definitions.Select(reference => clashes.Contains(reference)
                ? new RelocationItem(reference, names(reference), confirmed.Contains(reference) ? RelocationAction.Absorb : RelocationAction.NeedsDecision, "„Camere IP”", "„Camere”", ["„Camere analog”: Lentila"],
                    confirmed.Contains(reference) ? [new ValueMapping("2.8 mm", true, 1), new ValueMapping("6 mm", false, 1)] : [], 2, "")
                : new RelocationItem(reference, names(reference), RelocationAction.Move, "„Camere IP”", "„Camere”", [], [], 1, "")).ToList();
            var merged = items.Any(item => item.Action == RelocationAction.Absorb);
            return new RelocationPreview(items, [], merged ? [new IncompleteProduct(1, "CAM-3"), new IncompleteProduct(2, "CAM-4")] : []);
        }

        public Task<RelocationPreview> PreviewAsync(RelocationRequest request, CancellationToken cancellationToken = default) { Previews.Add(request); return Task.FromResult(Answer(request)); }

        public Task<RelocationPreview> ApplyAsync(RelocationRequest request, string reason, CancellationToken cancellationToken = default)
        {
            Applied.Add((request, reason));
            return Task.FromResult(Answer(request));
        }
    }

    public static async Task RunAsync(Action<bool, string> check)
    {
        var access = new TestAccessControl(true, "take.admin");
        var parameters = new DemoProductParameterRepository();
        var tags = new DemoProductTagRepository();
        var ip = new ProductGroup("Supraveghere", "Camere IP");
        var lens = await parameters.AddParameterAsync(ip, "Lentila", "mm", ParameterKind.Number);
        var poe = await tags.AddTagAsync(ip, "PoE");
        var other = await parameters.AddParameterAsync(new ProductGroup("Supraveghere", "Camere analog"), "Culoare", "", ParameterKind.Text);
        var lensRef = new DefinitionRef(DefinitionKind.Parameter, lens.Id);
        var poeRef = new DefinitionRef(DefinitionKind.Tag, poe.Id);
        var relocation = new FakeRelocation(reference => reference == lensRef ? "Lentila" : "PoE", [lensRef]);
        var nodes = new List<SubcategoryNode> { new(1, "Supraveghere", "Camere", null, 0, false, 0), new(2, "Supraveghere", "Camere IP", "Camere", 1, true, 0), new(3, "Supraveghere", "Camere analog", "Camere", 1, true, 0) };
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddLogging();
        context.Services.AddSingleton<IAccessControl>(access);
        context.Services.AddSingleton<IProductParameterRepository>(parameters);
        context.Services.AddSingleton<IProductTagRepository>(tags);
        context.Services.AddSingleton<ISubcategoryTree>(new FakeTree(nodes));
        context.Services.AddSingleton<IDefinitionRelocation>(relocation);
        int changed = 0, closed = 0;
        var dialog = context.Render<TakeFromChildDialog>(builder => builder.Add(item => item.Target, new ProductGroup("Supraveghere", "Camere"))
            .Add(item => item.Changed, EventCallback.Factory.Create(new object(), () => changed++)).Add(item => item.Closed, EventCallback.Factory.Create(new object(), () => closed++)));
        dialog.WaitForAssertion(() => Expect(dialog.FindAll("#take-child option").Count == 2), TimeSpan.FromSeconds(10));
        check(dialog.FindAll("#take-child option").Select(option => option.TextContent).SequenceEqual(["Camere IP", "Camere analog"]) && dialog.FindAll(".take-table tbody tr").Count == 2
              && dialog.Find(".take-table").TextContent.Contains("Lentila", StringComparison.Ordinal) && dialog.Find(".take-table").TextContent.Contains("PoE", StringComparison.Ordinal) && !dialog.Find(".take-table").TextContent.Contains("Culoare", StringComparison.Ordinal),
            "Take from child: the dialog lists the children of the node and the definitions of the first one");
        check(dialog.Find(".take-dialog .editor-actions button.primary").HasAttribute("disabled"), "Take from child: nothing can continue until a definition is chosen");

        dialog.Find("#take-child").Change("Camere analog");
        dialog.WaitForAssertion(() => Expect(dialog.Find(".take-table").TextContent.Contains("Culoare", StringComparison.Ordinal)));
        dialog.Find("#take-child").Change("Camere IP");
        dialog.WaitForAssertion(() => Expect(dialog.Find(".take-table").TextContent.Contains("Lentila", StringComparison.Ordinal)));
        dialog.FindAll(".take-table tbody tr").First(row => row.TextContent.Contains("Lentila", StringComparison.Ordinal)).QuerySelector("input[role=switch]")!.Change(true);
        dialog.Find(".take-dialog .editor-actions button.primary").Click();
        dialog.WaitForAssertion(() => Expect(dialog.FindAll(".take-preview").Count == 1), TimeSpan.FromSeconds(10));
        check(relocation.Previews.Count == 1 && relocation.Previews[0].Definitions.SequenceEqual([lensRef]) && relocation.Previews[0].Target == new ProductGroup("Supraveghere", "Camere"),
            "Take from child: the preview is asked for the chosen definitions and the node as target");
        check(dialog.FindAll(".take-preview tr.restore-problem").Count == 1 && dialog.Find(".take-preview").TextContent.Contains("Există deja o definiție", StringComparison.Ordinal)
              && dialog.Find(".take-dialog .editor-actions button.primary").HasAttribute("disabled"),
            "Take from child: a namesake in a sibling branch is shown and the relocation cannot be applied before the user decides");

        // Merge: the preview is asked again with the decision, lists the values and the products that become incomplete, which must be accepted.
        dialog.Find(".take-preview tbody tr input[role=switch]").Change(true);
        dialog.WaitForAssertion(() => Expect(dialog.FindAll(".take-incomplete").Count == 1), TimeSpan.FromSeconds(10));
        check(relocation.Previews.Count == 2 && relocation.Previews[1].ConfirmedMerges!.SequenceEqual([lensRef]) && dialog.Find(".take-values").TextContent.Contains("valoare nouă", StringComparison.Ordinal)
              && dialog.Find(".take-incomplete").TextContent.Contains("2 produse devin incomplete", StringComparison.Ordinal),
            "Take from child: the merge decision is sent, the values and the incomplete products are shown");
        dialog.Find("#take-reason").Input("Parametrii se mostenesc de la parinte");
        check(dialog.Find(".take-dialog .editor-actions button.primary").HasAttribute("disabled"), "Take from child: the incomplete products must be accepted before the apply");
        dialog.FindAll("input[role=switch]").Last().Change(true);
        dialog.WaitForAssertion(() => Expect(!dialog.Find(".take-dialog .editor-actions button.primary").HasAttribute("disabled")));
        dialog.Find(".take-dialog .editor-actions button.primary").Click();
        dialog.WaitForAssertion(() => Expect(relocation.Applied.Count == 1 && closed == 1), TimeSpan.FromSeconds(10));
        var (request, reason) = relocation.Applied[0];
        check(request.AcceptIncomplete && request.ConfirmedMerges!.SequenceEqual([lensRef]) && request.Definitions.SequenceEqual([lensRef]) && reason == "Parametrii se mostenesc de la parinte" && changed == 1 && closed == 1,
            "Take from child: the apply carries the target, the definitions, the merge decision, the acceptance and the reason, then the page reloads and the dialog closes");

        // A definition with no namesake needs no decision and no acceptance.
        using var second = new BunitContext();
        second.JSInterop.Mode = JSRuntimeMode.Loose;
        second.Services.AddLogging();
        second.Services.AddSingleton<IAccessControl>(access);
        second.Services.AddSingleton<IProductParameterRepository>(parameters);
        second.Services.AddSingleton<IProductTagRepository>(tags);
        second.Services.AddSingleton<ISubcategoryTree>(new FakeTree(nodes));
        var plain = new FakeRelocation(_ => "PoE", []);
        second.Services.AddSingleton<IDefinitionRelocation>(plain);
        var simple = second.Render<TakeFromChildDialog>(builder => builder.Add(item => item.Target, new ProductGroup("Supraveghere", "Camere")));
        simple.WaitForAssertion(() => Expect(simple.FindAll(".take-table tbody tr").Count == 2), TimeSpan.FromSeconds(10));
        simple.Find(".take-dialog .editor-actions button.refresh.act-confirm").Click();
        check(simple.FindAll(".take-table tbody tr input[role=switch]").All(toggle => toggle.HasAttribute("checked")), "Take from child: \"Alege tot\" chooses every definition of the child");
        simple.Find(".take-dialog .editor-actions button.primary").Click();
        simple.WaitForAssertion(() => Expect(simple.FindAll(".take-preview").Count == 1), TimeSpan.FromSeconds(10));
        check(simple.FindAll(".take-incomplete").Count == 0 && simple.FindAll(".take-preview tr.restore-problem").Count == 0 && simple.FindAll(".take-preview tbody tr input[role=switch]").Count == 0,
            "Take from child: definitions with no namesake are plain moves, with no decision to make and nothing to accept");
        simple.Find("#take-reason").Input("Mutare simpla");
        simple.Find(".take-dialog .editor-actions button.primary").Click();
        simple.WaitForAssertion(() => Expect(plain.Applied.Count == 1), TimeSpan.FromSeconds(10));
        check(plain.Applied[0].Request.Definitions.Count == 2 && !plain.Applied[0].Request.AcceptIncomplete && poeRef.Id == poe.Id && other.Id > 0, "Take from child: both definitions of the child are moved with no acceptance");
    }
}
