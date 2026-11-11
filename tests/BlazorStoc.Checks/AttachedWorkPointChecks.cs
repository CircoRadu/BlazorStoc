using BlazorStoc.Components.Shared;
using BlazorStoc.Modules.ActivityImport;
using BlazorStoc.Modules.Maintenance;
using BlazorStoc.Services;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorStoc.Checks;

// Attached work points (components of a work point): the rules (what a point needs, the last location, names that look alike), the popup with the "+" icons (add, edit and delete each
// piece of information), the list, the extended search of the beneficiaries through them, the pin on the map, the verification notice of the import and the note "Preluat din ActivityApp".
// The repository is a fake here; the real one is checked against MariaDB (section "Attached work points").
public static class AttachedWorkPointChecks
{
    private sealed class FakeRepository : IAttachedWorkPointRepository
    {
        public readonly List<AttachedWorkPoint> Items = [];
        public readonly List<string> Calls = [];
        public Task<IReadOnlyList<AttachedWorkPoint>> GetAsync(int workPointId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AttachedWorkPoint>>([.. Items.Where(item => item.WorkPointId == workPointId)]);
        public Task<IReadOnlyList<AttachedWorkPoint>> GetForBeneficiaryAsync(int beneficiaryId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AttachedWorkPoint>>([.. Items]);
        public Task<AttachedWorkPoint> CreateAsync(WorkPoint parent, AttachedWorkPointInput input, CancellationToken cancellationToken = default)
        {
            var value = input.Validated();
            var created = new AttachedWorkPoint(Items.Count + 1, parent.Id, value.Name, value.Address, value.Phone, value.ContactPerson, 0, value.Latitude, value.Longitude);
            Items.Add(created);
            Calls.Add("create:" + created.Name);
            return Task.FromResult(created);
        }
        public Task<AttachedWorkPoint> UpdateAsync(AttachedWorkPoint original, AttachedWorkPointInput input, CancellationToken cancellationToken = default)
        {
            var value = input.Validated();
            var updated = new AttachedWorkPoint(original.Id, original.WorkPointId, value.Name, value.Address, value.Phone, value.ContactPerson, original.Version + 1, value.Latitude, value.Longitude);
            Items[Items.FindIndex(item => item.Id == original.Id)] = updated;
            Calls.Add("update:" + updated.Name);
            return Task.FromResult(updated);
        }
        public Task DeleteAsync(AttachedWorkPoint original, CancellationToken cancellationToken = default) { Items.RemoveAll(item => item.Id == original.Id); Calls.Add("delete:" + original.Name); return Task.CompletedTask; }
        public Task<IReadOnlyList<PlaceSearchEntry>> GetSearchEntriesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<PlaceSearchEntry>>([]);
        public Task<IReadOnlyList<AttachedWorkPointPlace>> GetAllPlacesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AttachedWorkPointPlace>>([]);
    }

    private sealed class FakeMap : IMapConfigurationService
    {
        public Task<MapView> GetViewAsync(CancellationToken cancellationToken = default) => Task.FromResult(new MapView(new MapOptions().Normalized(), MapPinRules.Effective(null), 0));
        public Task<MapSettingsState> GetStateAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SaveEngineAsync(MapEngineSettings input, long expectedVersion, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ResetEngineAsync(long expectedVersion, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MapPinType> SavePinTypeAsync(MapPinType input, long expectedVersion, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeletePinTypeAsync(int id, string reason, long expectedVersion, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static readonly WorkPoint School = new(7, 3, "Scoala Lunca", "Str. Scolii 1");

    public static async Task RunAsync(Action<bool, string> check)
    {
        Rules(check);
        Search(check);
        Import(check);
        await EditorAsync(check);
        await ListAsync(check);
    }

    private static void Rules(Action<bool, string> check)
    {
        var noLocation = new AttachedWorkPointInput { Name = "Gradinita" };
        check(noLocation.Validate(new(noLocation)).Any(result => result.ErrorMessage == AttachedWorkPointRules.NeedsLocationMessage), "Attached work points: a name alone is not enough, the address or the GPS coordinates are needed");
        var noName = new AttachedWorkPointInput { Address = "Str. A 1" };
        check(noName.Validate(new(noName)).Any(result => result.MemberNames.Contains(nameof(AttachedWorkPointInput.Name))), "Attached work points: the name is required");
        var gpsOnly = new AttachedWorkPointInput { Name = "Cladire primar", UseCoordinates = true, CoordinatesText = "45,7489, 21,2087" }.Validated();
        check(gpsOnly.Address.Length == 0 && gpsOnly.Latitude == 45.7489m && gpsOnly.Longitude == 21.2087m, "Attached work points: GPS coordinates alone are accepted and the address stays empty (it is never taken from the parent)");
        var addressOnly = new AttachedWorkPointInput { Name = "Gradinita", Address = "Str. Florilor 5", Phone = "0744 123 456" }.Validated();
        check(addressOnly.Latitude is null && addressOnly.Longitude is null && addressOnly.Phone == "0744123456", "Attached work points: the address alone is accepted, the phone is normalized");
        var thrown = false;
        try { new AttachedWorkPointInput { Name = "Gradinita" }.Validated(); } catch (WorkPointOperationException exception) { thrown = exception.Message.Contains("adresa sau coordonatele", StringComparison.OrdinalIgnoreCase); }
        check(thrown, "Attached work points: saving without address and coordinates is refused with the reason");
        var badPhone = new AttachedWorkPointInput { Name = "G", Address = "A", Phone = "abc" };
        check(badPhone.Validate(new(badPhone)).Any(result => result.MemberNames.Contains(nameof(AttachedWorkPointInput.Phone))), "Attached work points: a phone that is not a number is refused");
        check(AttachedWorkPointRules.Similar("Grădinița nr. 5", "Gradinita 5") && AttachedWorkPointRules.Similar("Gradinita", "Gradinita cu program prelungit Lunca")
              && !AttachedWorkPointRules.Similar("Gradinita", "Liceul teoretic") && !AttachedWorkPointRules.Similar("", "Gradinita") && !AttachedWorkPointRules.Similar("Cor", "Corp A"),
            "Attached work points: names that look alike (diacritics, punctuation, 'nr', a name inside another) are found; unlike names and very short ones are not");
        check(AttachedWorkPointRules.NameKey("Gradinita ") == AttachedWorkPointRules.NameKey("GRADINITA") && AttachedWorkPointRules.NameKey("Gradinita") != AttachedWorkPointRules.NameKey("Gradinita 2"), "Attached work points: the name is unique inside its work point without regard to case or spaces");
        check(AuditActions.IsCreateOrEdit(AuditActions.CreateAttachedWorkPoint) && AuditActions.IsCreateOrEdit(AuditActions.EditAttachedWorkPoint) && AuditActions.CreateAttachedWorkPoint != AuditActions.EditAttachedWorkPoint
              && ArchiveSchemaRegistry.All.Any(schema => schema.EntityType == AuditEntities.AttachedWorkPoint && schema.TableName == "archive_work_point_attached"),
            "Attached work points: adding and editing are named in the journal and the deletion has its archive");
        var archived = ArchiveRequests.AttachedWorkPoint(new AttachedWorkPoint(9, 7, "Gradinita", "Str. A 1"), 3, "Liceu", "Scoala Lunca", AttachedWorkPointRules.DeleteReason);
        check(archived.Snapshot.EntityType == AuditEntities.AttachedWorkPoint && archived.Target.Contains("punct arondat «Gradinita»", StringComparison.Ordinal), "Attached work points: the archive request names the point, its work point and its beneficiary");
        var workPointArchive = ArchiveRequests.WorkPoint(School, "Liceu", [], "Motiv", [new AttachedWorkPoint(9, 7, "Gradinita", "Str. A 1")]);
        check(workPointArchive.Snapshot.Relations.Any(relation => relation.RelationType == AttachedWorkPointRules.RelationType), "Attached work points: the ones of a deleted work point travel with it in the archive");
        var marker = MaintenanceMapRules.AttachedMarker(new AttachedWorkPointPlace(new AttachedWorkPoint(9, 7, "Gradinita", "", Latitude: 45.5m, Longitude: 21.5m), 3, "Liceu", "Scoala Lunca"));
        check(marker.Id == -9 && marker.Fill == MaintenanceMapRules.AttachedFill && marker.Title == "Liceu · Scoala Lunca · Gradinita" && marker.Lat == 45.5 && marker.Contract.Length == 0, "Attached work points: the pin on the map has its own fill, a negative id and the title linked to the parents");
    }

    private static void Search(Action<bool, string> check)
    {
        var liceu = new Beneficiary(1, "Liceul Teoretic Lunca", "RO111111");
        var gradinita = new Beneficiary(2, "Gradinita Soarelui", "RO222222");
        var firma = new Beneficiary(3, "Firma Alfa SRL", "RO333333");
        var other = new Beneficiary(4, "Beta SRL", "RO444444");
        var all = new[] { liceu, gradinita, firma, other };
        PlaceSearchEntry[] places =
        [
            new(1, false, "Scoala Lunca", "", "Str. Scolii 1", "", ""),
            new(1, true, "Grădiniță", "Scoala Lunca", "Str. Florilor 5", "", ""),
            new(3, false, "Gradinita sat", "", "Str. Mica 2", "", "Ion Popescu"),
            new(4, true, "Corp B", "Depozit", "", "0744123456", "")
        ];
        var found = BeneficiarySearch.Match(all, "gradinita", BeneficiarySearchMode.Extended, places);
        check(found.Select(match => match.Beneficiary.Id).SequenceEqual([1, 2, 3]), "Beneficiary search (extended): 'gradinita' finds the beneficiary titled so, the one with an attached work point and the one with a work point of that name, each once");
        check(found.Single(match => match.Beneficiary.Id == 1).Reasons.Single() == "punct arondat: Grădiniță (la Scoala Lunca)" && found.Single(match => match.Beneficiary.Id == 3).Reasons.Single() == "punct de lucru: Gradinita sat" && found.Single(match => match.Beneficiary.Id == 2).Reasons.Count == 0,
            "Beneficiary search (extended): a result found through a place says which place, one found by its own name says nothing");
        check(BeneficiarySearch.Match(all, "gradinita", BeneficiarySearchMode.Simple, places).Select(match => match.Beneficiary.Id).SequenceEqual([2]), "Beneficiary search: the simple search never looks in the places");
        check(BeneficiarySearch.Match(all, "popescu", BeneficiarySearchMode.Extended, places).Select(match => match.Beneficiary.Id).SequenceEqual([3]) && BeneficiarySearch.Match(all, "0744 123 456", BeneficiarySearchMode.Extended, places).Select(match => match.Beneficiary.Id).SequenceEqual([4])
              && BeneficiarySearch.Match(all, "florilor", BeneficiarySearchMode.Extended, places).Select(match => match.Beneficiary.Id).SequenceEqual([1]),
            "Beneficiary search (extended): the contact person, the phone and the address of a place are searched too");
        check(BeneficiarySearch.Match(all, "", BeneficiarySearchMode.Extended, places).Count == 4 && BeneficiarySearch.Match(all, "gradinita", BeneficiarySearchMode.Extended, null).Select(match => match.Beneficiary.Id).SequenceEqual([2]), "Beneficiary search (extended): without a text everyone is listed and without place data only the own data count");
        check(new BeneficiaryOrigin(true, false).Text == "Preluat din ActivityApp" && new BeneficiaryOrigin(false, false).Text == "Legat cu ActivityApp" && new BeneficiaryOrigin(true, true).Text.Contains("șters", StringComparison.Ordinal), "Beneficiaries: the note says 'Preluat din ActivityApp' for the ones the import created and 'Legat cu ActivityApp' for the ones it only tied");
    }

    private static void Import(Action<bool, string> check)
    {
        PointPlan Point(long id, long partner, string name, PlanAction action = PlanAction.Create) => new(id, partner, name, action, null, false, null, "", "h");
        var points = new[] { Point(100, 1, "Gradinita Lunca"), Point(101, 1, "Magazin"), Point(102, 2, "Gradinita Lunca") };
        var attachedOf = new Dictionary<int, IReadOnlyList<(string Name, string WorkPointName)>> { [10] = [("Gradinita", "Scoala Lunca"), ("Cladire primar", "Scoala Lunca")] };
        var notices = ActivityPartnerPlanner.PlanAttachedChecks(points, new Dictionary<long, int> { [1] = 10, [2] = 20 }, attachedOf);
        check(notices.Count == 1 && notices[0].SourceId == 100 && notices[0].Entity == ActivityPartnerPlanner.AttachedSimilarEntity && notices[0].Message.Contains("«Gradinita»", StringComparison.Ordinal) && notices[0].Message.Contains("importul nu a fost oprit", StringComparison.Ordinal),
            "Activity import: a work point of ActivityApp that looks like an attached work point of the same beneficiary gives a verification notice; unlike names and other beneficiaries give none");
        check(ActivityPartnerPlanner.PlanAttachedChecks(points, new Dictionary<long, int>(), attachedOf).Count == 0 && ActivityPartnerPlanner.PlanAttachedChecks([], new Dictionary<long, int> { [1] = 10 }, attachedOf).Count == 0, "Activity import: nothing is noticed for a partner without beneficiary or without work points");
    }

    private static BunitContext NewContext(FakeRepository repository)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddLogging();
        context.Services.AddScoped<UnsavedChanges>();
        context.Services.AddSingleton<IAttachedWorkPointRepository>(repository);
        context.Services.AddSingleton<IMapConfigurationService>(new FakeMap());
        context.Services.AddSingleton<IAccessControl>(new TestAccessControl(true, "admin"));
        return context;
    }

    private static async Task WaitAsync(Func<bool> done)
    {
        for (var attempt = 0; attempt < 100 && !done(); attempt++) await Task.Delay(20);
    }

    private static async Task EditorAsync(Action<bool, string> check)
    {
        var repository = new FakeRepository();
        using var context = NewContext(repository);
        AttachedWorkPoint? saved = null;
        var closed = 0;
        var cut = context.Render<AttachedWorkPointEditor>(parameters => parameters.Add(component => component.Parent, School)
            .Add(component => component.Saved, point => saved = point).Add(component => component.Closed, () => closed++));
        check(new[] { "gps", "address", "phone", "contact" }.All(key => cut.FindAll($"#attached-add-{key}").Count == 1) && cut.FindAll(".attached-info-value").Count == 0, "Attached work point popup: a new point shows the name and the four '+' icons (GPS, address, phone, contact person), no information yet");
        cut.Find("#attached-name").Input("Gradinita");
        cut.Find("form").Submit();
        check(repository.Calls.Count == 0 && cut.Markup.Contains(AttachedWorkPointRules.NeedsLocationMessage, StringComparison.Ordinal), "Attached work point popup: saving a point without address and GPS is refused with the reason");
        cut.Find("#attached-add-address").Click();
        check(cut.FindAll("#attached-input-address").Count == 1 && cut.Find("#attached-add-gps").HasAttribute("disabled"), "Attached work point popup: '+' opens the field of that information and holds the other icons until it is applied");
        cut.Find("#attached-input-address").Input("Str. Florilor 5");
        cut.Find("#attached-apply-address").Click();
        check(cut.Find("#attached-value-address").TextContent == "Str. Florilor 5" && cut.FindAll("#attached-add-address").Count == 0 && cut.Find("#attached-delete-address").HasAttribute("disabled"),
            "Attached work point popup: the applied address is shown with edit and delete icons, and as the only location it cannot be deleted");
        cut.Find("#attached-add-gps").Click();
        cut.Find("#attached-input-gps").Input("45.7489 21.2087x");
        cut.Find("#attached-apply-gps").Click();
        check(cut.FindAll("#attached-value-gps").Count == 0 && cut.Markup.Contains("latitudine, longitudine", StringComparison.Ordinal), "Attached work point popup: coordinates that are not a pair are refused when applied");
        cut.Find("#attached-input-gps").Input("45.7489, 21.2087");
        cut.Find("#attached-apply-gps").Click();
        check(cut.Find("#attached-value-gps").TextContent == "45.7489, 21.2087" && !cut.Find("#attached-delete-address").HasAttribute("disabled") && !cut.Find("#attached-delete-gps").HasAttribute("disabled"), "Attached work point popup: with the address and the GPS both can be deleted");
        cut.Find("#attached-delete-address").Click();
        check(cut.FindAll("#attached-value-address").Count == 0 && cut.FindAll("#attached-add-address").Count == 1 && cut.Find("#attached-delete-gps").HasAttribute("disabled"), "Attached work point popup: a deleted information gets its '+' back and the last location is then locked");
        cut.Find("#attached-add-phone").Click();
        cut.Find("#attached-input-phone").Input("abc");
        cut.Find("#attached-apply-phone").Click();
        check(cut.FindAll("#attached-value-phone").Count == 0 && cut.Markup.Contains("7–15 cifre", StringComparison.Ordinal), "Attached work point popup: a phone that is not a number is refused when applied");
        cut.Find("#attached-input-phone").Input("0744123456");
        cut.Find("#attached-apply-phone").Click();
        cut.Find("#attached-edit-phone").Click();
        cut.Find("#attached-input-phone").Input("0722333444");
        cut.Find("#attached-apply-phone").Click();
        check(cut.Find("#attached-value-phone").TextContent == "0722333444", "Attached work point popup: an information is edited with its edit icon");
        cut.Find("#attached-add-contact").Click();
        cut.Find("form").Submit();
        check(repository.Calls.Count == 0 && cut.Markup.Contains("Aplică sau renunță", StringComparison.Ordinal), "Attached work point popup: saving while an information is still open asks to apply or give it up");
        cut.Find("#attached-cancel-contact").Click();
        check(cut.FindAll("#attached-value-contact").Count == 0 && cut.FindAll("#attached-add-contact").Count == 1, "Attached work point popup: giving up a new information leaves it out");
        cut.Find("form").Submit();
        await WaitAsync(() => saved is not null);
        check(saved is { Name: "Gradinita", Address: "", Phone: "0722333444", Latitude: 45.7489m } && repository.Calls.SequenceEqual(["create:Gradinita"]), "Attached work point popup: the point is saved with what was added (the address that was deleted stays empty)");

        var existing = repository.Items.Single();
        saved = null;
        var edit = context.Render<AttachedWorkPointEditor>(parameters => parameters.Add(component => component.Parent, School).Add(component => component.Original, existing).Add(component => component.Saved, point => saved = point));
        check(edit.FindAll("#attached-value-gps").Count == 1 && edit.FindAll("#attached-value-phone").Count == 1 && edit.FindAll("#attached-value-address").Count == 0 && edit.Find("#attached-name").GetAttribute("value") == "Gradinita", "Attached work point popup: editing shows what the point has and the '+' of what it lacks");
        edit.Find("#attached-name").Input("Gradinita noua");
        edit.Find("form").Submit();
        await WaitAsync(() => saved is not null);
        check(saved is { Name: "Gradinita noua", Version: 1 } && repository.Calls.Last() == "update:Gradinita noua", "Attached work point popup: an edit saves the point under the same id with a new version");
    }

    private static async Task ListAsync(Action<bool, string> check)
    {
        var repository = new FakeRepository();
        repository.Items.Add(new AttachedWorkPoint(1, 7, "Cladire primar", "Str. A 1", Latitude: 45m, Longitude: 21m));
        repository.Items.Add(new AttachedWorkPoint(2, 7, "Gradinita", "", "0744123456", "Ana", 0, 45.1m, 21.1m));
        using var context = NewContext(repository);
        var changed = 0;
        var added = 0;
        AttachedWorkPoint? toEdit = null;
        var cut = context.Render<AttachedWorkPointsList>(parameters => parameters.Add(component => component.Parent, School).Add(component => component.Items, repository.Items).Add(component => component.CanManage, true)
            .Add(component => component.Changed, () => changed++).Add(component => component.AddRequested, () => added++).Add(component => component.EditRequested, point => toEdit = point));
        check(cut.FindAll("#attached-table tbody tr").Count == 2 && cut.Markup.Contains("Scoala Lunca", StringComparison.Ordinal) && cut.Markup.Contains("45, 21", StringComparison.Ordinal), "Attached work points list: every attached work point of the work point is listed with its address, GPS, phone and contact");
        cut.Find("#attached-add-new").Click();
        cut.FindAll("button.row-icon.edit")[1].Click();
        check(added == 1 && toEdit?.Name == "Gradinita", "Attached work points list: '+' and the edit icon ask the page to open the popup of a new or of that point");
        cut.FindAll("button.row-icon.delete")[0].Click();
        await WaitAsync(() => repository.Calls.Count == 1);
        check(repository.Calls.SequenceEqual(["delete:Cladire primar"]) && changed == 1, "Attached work points list: the delete icon deletes in one step and tells the page to reload");
        var readOnly = context.Render<AttachedWorkPointsList>(parameters => parameters.Add(component => component.Parent, School).Add(component => component.Items, repository.Items).Add(component => component.CanManage, false));
        check(readOnly.FindAll("button.row-icon").Count == 0 && readOnly.FindAll("#attached-add-new").Count == 0, "Attached work points list: without the right to edit there are no icons and no '+'");
        var empty = context.Render<AttachedWorkPointsList>(parameters => parameters.Add(component => component.Parent, School).Add(component => component.Items, new List<AttachedWorkPoint>()).Add(component => component.CanManage, true));
        check(empty.FindAll("#attached-empty").Count == 1 && empty.FindAll("#attached-table").Count == 0, "Attached work points list: a work point with none says so");
    }
}
