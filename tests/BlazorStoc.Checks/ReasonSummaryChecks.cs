using System.Reflection;
using BlazorStoc.Components.Pages;
using BlazorStoc.Services;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorStoc.Checks;

// The reason of an edit in every form of an existing object: radio buttons between the summary generated from the user's changes (one change
// on each line, following the form) and a text of the user's own (ChangeReasonField / ChangeReasonSummary).
public static class ReasonSummaryChecks
{
    // A repository that is never called by these checks: the forms only read their fields until the user saves.
    public class ThrowingProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => throw new NotSupportedException();
    }

    private static T Stub<T>() where T : class => DispatchProxy.Create<T, ThrowingProxy>();

    // A text field raises change (when it is left) or input (fields that follow the typing): the one the field has.
    private static void Set<T>(IRenderedComponent<T> cut, string selector, string value) where T : Microsoft.AspNetCore.Components.IComponent
    {
        var field = cut.Find(selector);
        try { field.Change(value); }
        catch (MissingEventHandlerException) { cut.Find(selector).Input(value); }
    }

    public static void Run(Action<bool, string> check)
    {
        var now = new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddLogging();
        context.Services.AddScoped<UnsavedChanges>();
        context.Services.AddSingleton(Stub<IBeneficiaryRepository>());
        context.Services.AddSingleton(Stub<IAnafService>());
        context.Services.AddSingleton(Stub<IProjectRepository>());
        context.Services.AddSingleton(Stub<IUserRepository>());
        context.Services.AddSingleton(Stub<IVehicleRepository>());
        context.Services.AddSingleton<IAccessControl>(new TestAccessControl(true, "reason.admin"));

        static string Auto(IRenderedComponent<Microsoft.AspNetCore.Components.IComponent> cut, string id) => cut.Find($"#{id}-auto").GetAttribute("value") ?? "";

        // Each form: its own radio group, generated reason first and empty, a change shows on its own line, a second change on a second line,
        // putting a change back removes it, and the written reason can be chosen.
        void Form<T>(string name, string reasonId, Action<IRenderedComponent<T>> firstChange, string firstLabel, Action<IRenderedComponent<T>> secondChange, string secondLabel,
            Action<IRenderedComponent<T>> firstBack, Action<ComponentParameterCollectionBuilder<T>> setup) where T : Microsoft.AspNetCore.Components.IComponent
        {
            var cut = context.Render<T>(setup);
            string Text() => cut.Find($"#{reasonId}-auto").GetAttribute("value") ?? "";
            var radios = cut.FindAll($"input[type=radio][name='{reasonId}-mode']");
            check(radios.Count == 2 && radios[0].HasAttribute("checked") && !radios[1].HasAttribute("checked") && Text() == "" && cut.Find($"#{reasonId}").HasAttribute("disabled"),
                $"{name}: the reason is chosen with radio buttons, the generated one first and empty until something is changed");
            firstChange(cut);
            check(Text().Split('\n') is [var one] && one.StartsWith(firstLabel + ": ") && one.Contains(" → "), $"{name}: the generated reason shows the first change on a line");
            secondChange(cut);
            check(Text().Split('\n') is [var first, var second] && first.StartsWith(firstLabel + ": ") && second.StartsWith(secondLabel + ": "), $"{name}: each change is on its own line");
            firstBack(cut);
            check(Text().Split('\n') is [var left] && left.StartsWith(secondLabel + ": "), $"{name}: a change put back to its original value leaves the generated reason");
            cut.FindAll($"input[type=radio][name='{reasonId}-mode']")[1].Change(true);
            check(!cut.Find($"#{reasonId}").HasAttribute("disabled"), $"{name}: choosing the written reason enables its field");
        }

        var beneficiary = new Beneficiary(1, "Beneficiar Test SRL", "RO12345678", 1, BeneficiaryKinds.Legal, "Strada Test 1", "0721000111", "J40/1/2020", "010101", "4321");
        Form<BeneficiaryEditor>("Beneficiary edit", "beneficiary-change-reason",
            cut => Set(cut, "#beneficiary-address", "Strada Noua 9"), "Adresă",
            cut => Set(cut, "#beneficiary-phone", "0722333444"), "Telefon",
            cut => Set(cut, "#beneficiary-address", "Strada Test 1"), p => p.Add(x => x.Original, beneficiary));

        var project = new Project(2, 1, "Proiect Test", "Observatii initiale", 1, now, now);
        Form<ProjectEditor>("Project edit", "project-change-reason",
            cut => Set(cut, "#project-name", "Proiect redenumit"), "Denumire proiect",
            cut => Set(cut, "#project-observations", "Alte observatii"), "Observații",
            cut => Set(cut, "#project-name", "Proiect Test"), p => p.Add(x => x.Original, project));

        var observation = new ProjectObservation(3, 2, "Observatie Test", "Continut initial", "autor", 1, now, now);
        Form<ProjectObservationEditor>("Observation edit", "observation-change-reason",
            cut => Set(cut, "#observation-name", "Observatie noua"), "Denumire observație",
            cut => Set(cut, "#observation-content", "Continut nou"), "Conținut",
            cut => Set(cut, "#observation-name", "Observatie Test"), p => p.Add(x => x.Original, observation).Add(x => x.ProjectId, 2));

        var user = new WebUser(4, "utilizator.test", "Utilizator Test", AccessRoles.LimitedUser, true, 1);
        Form<UserEditor>("User edit", "user-change-reason",
            cut => Set(cut, "#user-username", "utilizator.nou"), "Nume utilizator",
            cut => Set(cut, "#user-display-name", "Alt Nume"), "Nume afișat",
            cut => Set(cut, "#user-username", "utilizator.test"), p => p.Add(x => x.Original, user));

        var vehicle = new Vehicle(5, "B-123-ABC", "Dacia Duster", 1, new DateOnly(2027, 1, 10), new DateOnly(2027, 2, 10), new DateOnly(2027, 3, 10));
        Form<VehicleEditor>("Vehicle edit", "vehicle-change-reason",
            cut => Set(cut, "#vehicle-plate", "B-456-XYZ"), "Număr de înmatriculare",
            cut => Set(cut, "#vehicle-description", "Dacia Logan"), "Descriere",
            cut => Set(cut, "#vehicle-plate", "B-123-ABC"), p => p.Add(x => x.Original, vehicle));

        // The helper itself: lines in order, equal values left out, long values shortened, the whole text within the limit.
        var text = ChangeReasonSummary.Build([new("A", "x", "y"), new("B", "z", "z"), new("C", "", new string('q', 200))]);
        var lines = text.Split('\n');
        check(lines.Length == 2 && lines[0] == "A: x → y" && lines[1].StartsWith("C: (gol) → ") && lines[1].EndsWith("…") && lines[1].Length < 70, "Reason summary: equal values are left out, empty ones named and long ones shortened");
        check(ChangeReasonSummary.Build(Enumerable.Range(0, 40).Select(index => new AuditChange("Camp " + index, "a", "b"))).Length <= ChangeReasonRules.MaximumLength, "Reason summary: never longer than the limit of a reason");
        check(ChangeReasonSummary.ValidationError(true, "", "scris") == ChangeReasonSummary.NoChangesMessage && ChangeReasonSummary.ValidationError(true, "A: x → y", "") is null &&
              ChangeReasonSummary.ValidationError(false, "A: x → y", "") is not null && ChangeReasonSummary.ValidationError(false, "", "motiv") is null,
            "Reason summary: the generated reason needs a change, the written one needs a text");
    }
}
