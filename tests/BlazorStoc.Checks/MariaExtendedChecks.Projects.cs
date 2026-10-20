using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using BlazorStoc.Services;

namespace BlazorStoc.Checks;

public static partial class MariaExtendedChecks
{
    private static async Task ProjectComponentsAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe)
    {
        var suffix = Suffix();
        var started = DateTime.UtcNow.AddSeconds(-1);
        var types = new MariaSystemTypeRepository(configuration, admin, audit);
        var components = new MariaProjectComponentRepository(configuration, admin, audit);
        var beneficiaries = new MariaBeneficiaryRepository(configuration, admin, audit);
        var projects = new MariaProjectRepository(configuration, new TestWebHostEnvironment(Path.GetTempPath()), admin, audit);
        var beneficiary = await beneficiaries.CreateAsync(Legal($"Ext Comp Beneficiar {suffix} SRL", "RO" + Random.Shared.Next(60000000, 69999999)));
        var project = await projects.CreateAsync(new ProjectInput { BeneficiaryId = beneficiary.Id, Name = $"Ext Comp Proiect {suffix}" });
        var alarm = await types.CreateAsync($"Ext Alarma {suffix}");
        var video = await types.CreateAsync($"Ext Video {suffix}");
        var fire = await types.CreateAsync($"Ext Incendiu {suffix}");
        var typeIds = new[] { alarm.Id, video.Id, fire.Id };
        try
        {
            async Task<bool> Journaled(string action) => (await audit.GetEventsAsync()).Any(item => item.TimestampUtc >= started && item.Action == action && item.EntityId == project.Id.ToString());
            await types.SetActiveAsync(fire, false);

            // 1. Components are chosen when the project is created: several at once, each in the state Offered; an inactive type or a repeated one is refused.
            var added = await components.AddAsync(project.Id, [alarm.Id, video.Id]);
            var inactive = await Rejects<ProjectComponentException>(() => components.AddAsync(project.Id, [fire.Id]), "An inactive system type cannot be added");
            var repeated = await Rejects<ProjectComponentException>(() => components.AddAsync(project.Id, [alarm.Id]), "A system type already in the project cannot be added again");
            Check(added.Count == 2 && added.All(item => item.State == ComponentState.Offered && !item.Archived) && inactive is not null && repeated is not null
                  && (await components.GetForProjectAsync(project.Id)).Count == 2 && await Journaled(AuditActions.AddProjectComponent),
                "A project gets its components (several at once, state Offered); an inactive or repeated type is refused; each addition is journaled");

            // 2. The state of a component changes with its own journal action; an old version is refused.
            var first = added[0];
            var running = await components.SetStateAsync(first, ComponentState.InProgress);
            var stale = await Rejects<ProjectComponentException>(() => components.SetStateAsync(first, ComponentState.Closed), "A change on an old version of a component is refused");
            Check(running.State == ComponentState.InProgress && stale is not null && await Journaled(AuditActions.ChangeProjectComponentState),
                "The state of a component changes with its own journal action and an old version is refused");

            // 3. Taking a component out archives it with a reason (a reason is required); it leaves the active list, keeps its row, and cannot change state.
            var noReason = await Rejects<ProjectComponentException>(() => components.ArchiveAsync(running, " "), "Taking a component out without a reason is refused");
            var archived = await components.ArchiveAsync(running, "Ext clientul renunta la sistem");
            var activeAfter = await components.GetForProjectAsync(project.Id);
            var withArchived = await components.GetForProjectAsync(project.Id, includeArchived: true);
            var archivedState = await Rejects<ProjectComponentException>(() => components.SetStateAsync(archived, ComponentState.Closed), "An archived component cannot change state");
            var archivedAgain = await Rejects<ProjectComponentException>(() => components.AddAsync(project.Id, [alarm.Id]), "A type archived in the project must be reactivated, not added");
            Check(noReason is not null && archived.Archived && archived.ArchiveReason == "Ext clientul renunta la sistem" && activeAfter.Count == 1 && withArchived.Count == 2
                  && archivedState is not null && archivedAgain is not null && await Journaled(AuditActions.ArchiveProjectComponent),
                "A component taken out is archived with its reason, kept in the project and journaled; it cannot be changed or added again until reactivated");

            // 4. Reactivation brings it back with its state, and is journaled.
            var back = await components.ReactivateAsync(archived);
            Check(!back.Archived && back.State == ComponentState.InProgress && (await components.GetForProjectAsync(project.Id)).Count == 2 && await Journaled(AuditActions.ReactivateProjectComponent),
                "A reactivated component is back in the project with its state and the reactivation is journaled");
        }
        finally
        {
            await projects.DeleteAsync(project, "Ext curatare");
            await beneficiaries.DeleteAsync((await beneficiaries.GetBeneficiariesAsync()).First(item => item.Id == beneficiary.Id), "Ext curatare");
            foreach (var id in typeIds) await ExecuteAsync(probe, "DELETE FROM system_types WHERE id=@id", ("@id", id));
        }
    }

    private static async Task OfferTemplatesAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe)
    {
        var suffix = Suffix();
        var started = DateTime.UtcNow.AddSeconds(-1);
        var repository = new MariaOfferTemplateRepository(configuration, admin, audit);
        var created = new List<int>();
        try
        {
            async Task<bool> Journaled(string action, int id) => (await audit.GetEventsAsync()).Any(item => item.TimestampUtc >= started && item.Action == action && item.EntityId == id.ToString());
            var definition = OfferTemplateEngine.Suggest(OfferChecks.Offer().Sheets[0]);

            // 1. A template is saved with its definition (read back identical) and refused when incomplete or when the name is taken (case and diacritics ignored).
            var template = await repository.CreateAsync($"Ext Oferta {suffix}", definition);
            created.Add(template.Id);
            var incomplete = definition.Clone(); incomplete.UnitColumn = "";
            var noColumn = await Rejects<OfferTemplateException>(() => repository.CreateAsync($"Ext Alta {suffix}", incomplete), "A template without the unit column is refused");
            var sameName = await Rejects<OfferTemplateException>(() => repository.CreateAsync($"EXT OFERTĂ {suffix}", definition), "A second template with the same name is refused");
            Check(template.Active && template.Definition.Serialize() == definition.Serialize() && noColumn is not null && sameName is not null && await Journaled(AuditActions.CreateOfferTemplate, template.Id),
                "An offer template is saved with its definition, an incomplete or repeated one is refused, and the creation is journaled");

            // 2. A change journals exactly what changed (before -> after), an old version is refused, deactivation has its own action.
            var changed = definition.Clone(); changed.Sections[0].Import = !changed.Sections[0].Import; changed.IgnoreRows.Add("Subtotal");
            var saved = await repository.SaveAsync(template, template.Name, changed);
            var stale = await Rejects<OfferTemplateException>(() => repository.SaveAsync(template, template.Name, definition), "A save on an old version is refused");
            var inactive = await repository.SetActiveAsync(saved, false);
            var events = await audit.GetEventsAsync();
            Check(saved.Version > template.Version && stale is not null && !inactive.Active && await Journaled(AuditActions.DeactivateOfferTemplate, template.Id)
                  && events.Any(item => item.TimestampUtc >= started && item.Action == AuditActions.EditOfferTemplate && item.EntityId == template.Id.ToString() && item.Details.Contains("Secțiuni importate") && item.Details.Contains("Rânduri ignorate")),
                "A change of a template journals what changed, a stale save is refused and deactivation has its own action");
        }
        finally
        {
            foreach (var id in created) await ExecuteAsync(probe, "DELETE FROM offer_templates WHERE id=@id", ("@id", id));
        }
    }

    private static async Task OfferImportAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe)
    {
        var suffix = Suffix();
        var started = DateTime.UtcNow.AddSeconds(-1);
        var category = $"Ext Ofe Cat {suffix}";
        var subcategory = $"Ext Ofe Sub {suffix}";
        var products = new MariaProductRepository(configuration, admin, audit);
        var types = new MariaSystemTypeRepository(configuration, admin, audit);
        var movements = new MariaStockMovementRepository(configuration, admin, audit);
        var components = new MariaProjectComponentRepository(configuration, admin, audit, movements);
        var beneficiaries = new MariaBeneficiaryRepository(configuration, admin, audit);
        var projects = new MariaProjectRepository(configuration, new TestWebHostEnvironment(Path.GetTempPath()), admin, audit);
        var offers = new MariaOfferRepository(configuration, projects, components, beneficiaries, admin, audit);
        await products.CreateCategoryAsync(category);
        await products.CreateSubcategoryAsync(category, subcategory);
        var recorder = await products.CreateAsync(new ProductInput { Name = $"Recorder{suffix} 64", Category = category, Subcategory = subcategory });
        var beneficiary = await beneficiaries.CreateAsync(Legal($"Ext Oferta Client {suffix} SRL", "RO" + Random.Shared.Next(60000000, 69999999)));
        var type = await types.CreateAsync($"Ext CCTV {suffix}");
        var number = "9" + Random.Shared.Next(10000000, 99999999);
        int? projectId = null, otherId = null, type2Id = null;
        try
        {
            async Task<bool> Journaled(string action, string? contains = null) => (await audit.GetEventsAsync()).Any(item => item.TimestampUtc >= started && item.Action == action && (contains is null || item.Details.Contains(contains)));
            var workbook = OfferChecks.Offer(number);
            var reading = OfferTemplateEngine.Apply(OfferTemplateEngine.Suggest(workbook.Sheets[0]), workbook);
            List<OfferImportLine> Lines(Func<OfferLine, int?> product) => [.. reading.Sections.Where(part => part.Import).SelectMany(part => part.Lines.Select(line => new OfferImportLine
                { Section = part.Name, Number = line.Number, ProductType = line.ProductType, Name = line.Name, Unit = line.Unit, Quantity = line.Quantity, InStock = line.InStock, ProductId = line.InStock ? product(line) : null }))];
            OfferImportRequest Request(List<OfferImportLine> lines, int? existingProject) => new()
            {
                Number = number, Title = reading.Field(OfferHeaderField.TitleKey), Category = reading.Field(OfferHeaderField.CategoryKey), FileName = "oferta.xlsx", BeneficiaryId = beneficiary.Id,
                BeneficiaryText = "Clientul din oferta " + suffix, RememberBeneficiaryAlias = true, ProjectId = existingProject, NewProjectName = $"Ext Proiect Oferta {suffix}", SystemTypeId = type.Id, Lines = lines
            };

            // 1. Taking an offer over creates the project and its component, the offer with its lines (pieces tied to a product, the others de achizitionat / in afara stocului)
            // and the journal events (the offer and the link of each tied line).
            var first = await offers.ImportAsync(Request(Lines(line => line.Name.StartsWith("Recorder") ? recorder.Id : null), null));
            projectId = first.ProjectId;
            var saved = await offers.GetLinesAsync(first.Offer.Id);
            var component = (await components.GetForProjectAsync(first.ProjectId)).Single();
            Check(first.Offer.Revision == 1 && !first.IsRevision && first.Offer.ProjectName == $"Ext Proiect Oferta {suffix}" && component.SystemTypeId == type.Id && saved.Count == 3
                  && saved.Count(line => line.ProductId == recorder.Id) == 1 && first is { LinkedLines: 1, ToPurchaseLines: 1, OutOfStockLines: 1 }
                  && await Journaled(AuditActions.ImportOffer, number) && await Journaled(AuditActions.LinkOfferLine),
                "An offer is taken over with its project, component and lines (tied, to purchase, out of stock) and journaled");

            // 2. The same number again is a revision with the differences shown; the confirmed tie and the beneficiary name are remembered.
            var changedLines = Lines(line => null);
            changedLines[0].Quantity += 2;
            changedLines.RemoveAt(2);
            var revision = await offers.ImportAsync(Request(changedLines, first.ProjectId));
            var remembered = await offers.GetRememberedMatchesAsync();
            var alias = await offers.FindBeneficiaryByAliasAsync("clientul din OFERTA " + suffix);
            Check(revision is { IsRevision: true, Offer.Revision: 2 } && revision.Diff is { PreviousRevision: 1, Removed: 1, QuantityChanged: 1 } && (await offers.GetLatestAsync(number))!.Revision == 2
                  && remembered.Values.Contains(recorder.Id) && alias == beneficiary.Id && await Journaled(AuditActions.ReviseOffer, "Diferențe") && await Journaled(AuditActions.AddBeneficiaryAlias),
                "The same offer number is a revision with the differences, and the tie of a line and the beneficiary name are remembered");

            // 3a. The situation of the project: the latest revision (revision 2 has the recorder with quantity 4 and no tie, so the tie comes from a new revision), exits and returns,
            // the stock and the last supplier come from the database.
            var situationReader = new MariaProjectSituationReader(configuration, projects, components, offers, movements, products, new MariaReservationRepository(configuration, admin, audit), admin);
            var tied = Lines(line => line.Name.StartsWith("Recorder") ? recorder.Id : null);
            var tiedRevision = await offers.ImportAsync(Request(tied, first.ProjectId));
            await movements.CreateAsync(recorder.Id, new StockMovementInput { Kind = StockMovementKind.Entry, Date = DateOnly.FromDateTime(DateTime.Now), Quantity = 3, Description = "Ext situatie stoc" });
            var today = DateOnly.FromDateTime(DateTime.Now);
            var exit = await movements.CreateExitOperationAsync([new ExitOperationLine(recorder.Id, new StockMovementInput { Kind = StockMovementKind.Exit, Date = today, Quantity = 2, Description = "Ext situatie livrare",
                Destination = ExitDestination.Beneficiary, BeneficiaryId = beneficiary.Id, ProjectId = first.ProjectId, Reference = "Aviz sit" })]);
            await movements.CreateAsync(recorder.Id, new StockMovementInput { Kind = StockMovementKind.Entry, Date = today, Quantity = 1, Description = "Ext situatie retur", FreeType = FreeEntryType.FromBeneficiary,
                ReturnOfMovementId = exit.Movements[0].Movement.Id });
            var situation = await situationReader.GetAsync(first.ProjectId);
            var recorderLine = situation.Components.Single().Lines.Single(line => line.ProductId == recorder.Id);
            Check(tiedRevision.Offer.Revision == 3 && situation.Components.Single().Name == type.Name && recorderLine is { Quantity: 2, Delivered: 1, FromStock: 1, Deficit: 0, State: SituationLineState.Covered }
                  && situation.Components.Single().Lines.Count(line => !line.InStock) == 1 && situation.OutsideOffer.Count == 0,
                "The situation of a project reads the latest offer revision, the net handed-over pieces (exit minus return) and the stock from the database");

            // 3c. Exits tied to components: automatic (the only component with the product / "in afara ofertei"), refused when several have it, explicit choice, and taking a
            // component out is blocked until every exit left on it is cleared (move, return, left at the beneficiary, consumed), each cleared exit journaled.
            var type2 = await types.CreateAsync($"Ext Alarma {suffix}"); type2Id = type2.Id;
            var other = await products.CreateAsync(new ProductInput { Name = $"Ext Alt produs {suffix}", Category = category, Subcategory = subcategory }); otherId = other.Id;
            StockMovementInput ExitInput(int quantity, int? component = null) => new()
            {
                Kind = StockMovementKind.Exit, Date = today, Quantity = quantity, Description = "Ext iesire comp", Destination = ExitDestination.Beneficiary,
                BeneficiaryId = beneficiary.Id, ProjectId = first.ProjectId, Reference = "Aviz comp", ProjectComponentId = component
            };
            var comp1 = (await components.GetForProjectAsync(first.ProjectId)).Single();
            await movements.CreateAsync(other.Id, ExitInput(3));
            var net = await components.GetNetByComponentAsync(first.ProjectId);
            Check(net.Any(item => item.ComponentId == comp1.Id && item.ProductId == recorder.Id && item.Net == 1)
                  && net.Any(item => item.ComponentId is null && item.OutsideOffer && item.ProductId == other.Id && item.Net == 3),
                "An exit is tied to the only component whose offer has the product, or marked outside the offer when no offer has it");

            var comp2 = (await components.AddAsync(first.ProjectId, [type2.Id])).Single();
            var secondNumber = number + "2";
            var secondOffer = Request(Lines(line => line.Name.StartsWith("Recorder") ? recorder.Id : null), first.ProjectId);
            secondOffer.Number = secondNumber; secondOffer.SystemTypeId = type2.Id;
            await offers.ImportAsync(secondOffer);
            var ambiguous = await Rejects<StockMovementOperationException>(() => movements.CreateAsync(recorder.Id, ExitInput(4)), "A product in several components needs a choice");
            await movements.CreateAsync(recorder.Id, ExitInput(4, comp2.Id));
            var returned = await movements.CreateAsync(recorder.Id, ExitInput(5, comp2.Id));
            var left = await movements.CreateAsync(recorder.Id, ExitInput(6, comp2.Id));
            var consumed = await movements.CreateAsync(recorder.Id, ExitInput(7, comp2.Id));
            var pendingOnTwo = await components.GetPendingExitsAsync(comp2.Id);
            var blocked = await Rejects<ProjectComponentHasExitsException>(() => components.ArchiveAsync(comp2, "Ext scoatere cu iesiri"), "A component with exits tied to it cannot be taken out");
            Check(ambiguous is not null && pendingOnTwo.Count == 4 && blocked is not null && blocked.Exits.Count == 4,
                "A product in several components needs a choice, and a component with exits tied to it cannot be taken out");

            var stockBefore = (await products.GetProductAsync(recorder.Id))!.Quantity;
            var targetMove = pendingOnTwo.First(item => item.Quantity == 4).MovementId;
            await components.ResolveExitsAsync(comp2, [
                new(targetMove, ComponentExitAction.MoveToComponent, comp1.Id),
                new(returned.Movement.Id, ComponentExitAction.ReturnToWarehouse),
                new(left.Movement.Id, ComponentExitAction.LeftAtBeneficiary),
                new(consumed.Movement.Id, ComponentExitAction.Consumed)]);
            var archivedTwo = await components.ArchiveAsync(comp2, "Ext scoatere dupa lamurire");
            var netAfter = await components.GetNetByComponentAsync(first.ProjectId);
            Check(archivedTwo.Archived && (await components.GetPendingExitsAsync(comp2.Id)).Count == 0 && (await products.GetProductAsync(recorder.Id))!.Quantity == stockBefore + 5
                  && netAfter.Where(item => item.ComponentId == comp1.Id && item.ProductId == recorder.Id).Sum(item => item.Net) == 1 + 4
                  && await Journaled(AuditActions.ComponentExitReturned) && await Journaled(AuditActions.ComponentExitLeft)
                  && await Journaled(AuditActions.ComponentExitMoved) && await Journaled(AuditActions.ComponentExitConsumed),
                "Clearing the exits (move, return to the warehouse, left at the beneficiary, consumed) allows taking the component out; the stock follows the return and each action is journaled");

            // 3d. An entry can be tied to the component whose offer it supplies (only an active component); the pieces show in the situation as "intrat pentru oferta".
            var offerComponents = await components.GetComponentsWithProductAsync(recorder.Id);
            StockMovementInput OfferEntry(int component) => new() { Kind = StockMovementKind.Entry, Date = today, Quantity = 2, Description = "Ext intrare pentru oferta", ProjectComponentId = component };
            await movements.CreateAsync(recorder.Id, OfferEntry(comp1.Id));
            var archivedTie = await Rejects<StockMovementOperationException>(() => movements.CreateAsync(recorder.Id, OfferEntry(comp2.Id)), "An entry cannot be tied to a component taken out");
            var received = await components.GetReceivedAsync(first.ProjectId);
            var withReceived = (await situationReader.GetAsync(first.ProjectId)).Components.Single(item => item.SystemTypeId == type.Id);
            var proposals = await situationReader.GetReserveSuggestionsAsync(recorder.Id);
            Check(offerComponents.Count == 1 && offerComponents[0].ComponentId == comp1.Id && archivedTie is not null && received.GetValueOrDefault((type.Id, recorder.Id)) == 2
                  && withReceived.Lines.Any(line => line.ProductId == recorder.Id && line.Received == 2) && proposals.All(item => item.Quantity > 0),
                "An entry tied to the component of an offer shows as received for that line, a taken-out component is refused and reservation proposals are never empty");

            // 3b. A beneficiary of another project, an unknown product or no lines are refused.
            var noLines = await Rejects<OfferException>(() => offers.ImportAsync(Request([], null)), "An offer without lines is refused");
            var badProduct = Lines(line => null); badProduct[0].ProductId = int.MaxValue;
            var missing = await Rejects<OfferException>(() => offers.ImportAsync(Request(badProduct, first.ProjectId)), "An offer line tied to a missing product is refused");
            Check(noLines is not null && missing is not null && (await offers.GetLatestAsync(number))!.Revision == 3, "An offer without lines or with a missing product is refused and nothing is saved");
        }
        finally
        {
            if (otherId is { } extra)
            {
                await ExecuteAsync(probe, "DELETE FROM stock_movements WHERE product_id=@id", ("@id", extra));
                await ExecuteAsync(probe, "UPDATE products SET quantity=0 WHERE id=@id", ("@id", extra));
                await products.DeleteAsync((await products.GetProductAsync(extra))!, "Ext curatare");
            }
            await ExecuteAsync(probe, "UPDATE stock_movements SET return_of_movement_id=NULL WHERE product_id=@id", ("@id", recorder.Id));
            await ExecuteAsync(probe, "DELETE FROM stock_movements WHERE product_id=@id", ("@id", recorder.Id));
            await ExecuteAsync(probe, "UPDATE products SET quantity=0 WHERE id=@id", ("@id", recorder.Id));
            if (projectId is { } id) await projects.DeleteAsync((await projects.GetAsync(id))!, "Ext curatare");
            await beneficiaries.DeleteAsync((await beneficiaries.GetBeneficiariesAsync()).First(item => item.Id == beneficiary.Id), "Ext curatare");
            await ExecuteAsync(probe, "DELETE FROM offer_line_matches WHERE product_id=@id", ("@id", recorder.Id));
            await ExecuteAsync(probe, "DELETE FROM system_types WHERE id=@id", ("@id", type.Id));
            if (type2Id is { } secondType) await ExecuteAsync(probe, "DELETE FROM system_types WHERE id=@id", ("@id", secondType));
            await products.DeleteAsync((await products.GetProductAsync(recorder.Id))!, "Ext curatare");
            await ExecuteAsync(probe, "DELETE FROM subcategories WHERE name=@n", ("@n", subcategory));
            await ExecuteAsync(probe, "DELETE FROM categories WHERE name=@n", ("@n", category));
        }
    }

    private static async Task BeneficiariesAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe)
    {
        var suffix = Suffix();
        var cuiA = "RO" + Random.Shared.Next(70000000, 79999999);
        var cuiB = "RO" + Random.Shared.Next(80000000, 89999999);
        var beneficiaries = new MariaBeneficiaryRepository(configuration, admin, audit);
        var projects = new MariaProjectRepository(configuration, new TestWebHostEnvironment(Path.GetTempPath()), admin, audit);
        var a = await beneficiaries.CreateAsync(Legal($"Ext Beneficiar A {suffix} SRL", cuiA));
        var b = await beneficiaries.CreateAsync(Legal($"Ext Beneficiar B {suffix} SRL", cuiB));
        Project? project = null;
        try
        {
            var duplicate = await Rejects<BeneficiaryOperationException>(() => beneficiaries.CreateAsync(Legal("Alt Nume SRL", cuiA)), "A duplicate CUI is rejected on creation");
            Check(duplicate!.Message.Contains(a.Name), "The duplicate CUI message names the existing beneficiary");

            var before = (await audit.GetEventsAsync()).Count;
            var toDuplicate = BeneficiaryInput.From(b); toDuplicate.Cui = cuiA; toDuplicate.Reason = "Ext";
            var onEdit = await Rejects<BeneficiaryOperationException>(() => beneficiaries.UpdateAsync(b, toDuplicate), "Editing onto an existing CUI is rejected");
            Check(onEdit!.Message.Contains(a.Name), "The edit rejection names the CUI owner");
            Check((await audit.GetEventsAsync()).Count == before, "A rejected beneficiary edit leaves the journal unchanged");
            var keepsOwn = BeneficiaryInput.From(b); keepsOwn.Reason = "Ext pastreaza CUI";
            b = await beneficiaries.UpdateAsync(b, keepsOwn);
            Check(b.Version == 1, "An edit that keeps its own CUI is accepted and increments the version");
            var noReason = BeneficiaryInput.From(b); noReason.Address = "Alta adresa 5";
            await Rejects<BeneficiaryOperationException>(() => beneficiaries.UpdateAsync(b, noReason), "A beneficiary edit without a reason is rejected");
            await Rejects<BeneficiaryOperationException>(() => beneficiaries.DeleteAsync(b, " "), "A beneficiary deletion without a reason is rejected");

            project = await projects.CreateAsync(new ProjectInput { BeneficiaryId = a.Id, Name = $"Ext Proiect Viu {suffix}" });
            await Rejects<BeneficiaryOperationException>(() => beneficiaries.DeleteAsync(a, "Ext"), "Deleting a beneficiary is blocked while a live project exists");
            await projects.DeleteAsync(project, "Ext curatare proiect"); project = null;
            await beneficiaries.DeleteAsync(a, "Ext stergere");
            Check(await ScalarLongAsync(probe, "SELECT COUNT(*) FROM archive_beneficiaries WHERE original_id=@id", ("@id", a.Id)) == 1, "The deleted beneficiary is archived exactly once");
            Check(!(await beneficiaries.GetBeneficiariesAsync()).Any(item => item.Id == a.Id), "The deleted beneficiary is gone from the live table");
        }
        finally
        {
            if (project is not null) await projects.DeleteAsync(project, "Ext curatare");
            foreach (var item in (await beneficiaries.GetBeneficiariesAsync()).Where(item => item.Id == a.Id || item.Id == b.Id))
                await beneficiaries.DeleteAsync(item, "Ext curatare");
        }
    }

    // ---- Users -------------------------------------------------------------------------------------------------------
    private static async Task UsersAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe)
    {
        var name = "ext." + Suffix();
        var users = new MariaUserRepository(configuration, admin, audit);
        var user = await users.CreateAsync(new WebUserInput { Username = name, DisplayName = "Ext Utilizator", Role = AccessRoles.LimitedUser, Password = "parola-ext-veche-123" });
        try
        {
            var noReason = WebUserInput.From(user); noReason.DisplayName = "Fara motiv";
            await Rejects<UserOperationException>(() => users.UpdateAsync(user, noReason), "A user edit without a reason is rejected");
            var edit = WebUserInput.From(user); edit.DisplayName = "Ext Editat"; edit.Password = "parola-ext-noua-456"; edit.Reason = "Ext schimbare parola";
            var edited = await users.UpdateAsync(user, edit);
            Check((await users.AuthenticateAsync(name, "parola-ext-noua-456")).Status == AuthenticationStatus.Success, "An edited user authenticates with the new password");
            Check((await users.AuthenticateAsync(name, "parola-ext-veche-123")).Status == AuthenticationStatus.InvalidCredentials, "The old password no longer works");
            await Rejects<UserOperationException>(() => users.UpdateAsync(user, WebUserInput.From(user)), "A stale user edit is rejected");
            user = edited;
            var inactive = WebUserInput.From(user); inactive.IsActive = false; inactive.Reason = "Ext dezactivare";
            user = await users.UpdateAsync(user, inactive);
            Check((await users.AuthenticateAsync(name, "parola-ext-noua-456")).Status == AuthenticationStatus.Inactive, "A deactivated user is reported as inactive");
            await users.DeleteAsync(user, "Ext stergere");
            Check(await ScalarLongAsync(probe, "SELECT COUNT(*) FROM archive_web_users WHERE original_id=@id", ("@id", user.Id)) == 1, "The deleted user is archived exactly once");
            user = null!;
        }
        finally
        {
            if (user is not null) await users.DeleteAsync((await users.GetUsersAsync()).First(item => item.Id == user.Id), "Ext curatare");
        }
    }

    // ---- Projects ------------------------------------------------------------------------------------------------------
    private static async Task ProjectsAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe, string assets)
    {
        var suffix = Suffix();
        var environment = new TestWebHostEnvironment(assets);
        var beneficiaries = new MariaBeneficiaryRepository(configuration, admin, audit);
        var one = await beneficiaries.CreateAsync(Legal($"Ext Proiect Ben 1 {suffix} SRL", "RO" + Random.Shared.Next(60000000, 69999999)));
        var two = await beneficiaries.CreateAsync(Legal($"Ext Proiect Ben 2 {suffix} SRL", "RO" + Random.Shared.Next(50000000, 59999999)));
        var projects = new MariaProjectRepository(configuration, environment, admin, audit);
        var files = new MariaProjectFileStore(environment, configuration, admin, null, audit);
        Project? project = null;
        try
        {
            var name = $"Ext Proiect {suffix}";
            var attempts = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
            {
                try { return await new MariaProjectRepository(configuration, environment, admin, audit).CreateAsync(new ProjectInput { BeneficiaryId = one.Id, Name = name }); }
                catch (ProjectOperationException) { return null; }
            }));
            Check(attempts.Count(item => item is not null) == 1, "Two concurrent sessions cannot create the same project name for one beneficiary");
            project = attempts.Single(item => item is not null)!;
            var duplicate = await Rejects<ProjectOperationException>(() => projects.CreateAsync(new ProjectInput { BeneficiaryId = one.Id, Name = name.ToUpperInvariant() }), "A duplicate project name is rejected");
            Check(duplicate!.Message.Contains(one.Name), "The duplicate project message names the beneficiary");
            var other = await projects.CreateAsync(new ProjectInput { BeneficiaryId = two.Id, Name = name });
            await projects.DeleteAsync(other, "Ext acelasi nume alt beneficiar");
            Check(true, "The same project name is allowed for a different beneficiary");

            var noReason = ProjectInput.From(project); noReason.Name = name + " v2";
            await Rejects<ProjectOperationException>(() => projects.UpdateAsync(project, noReason), "A project edit without a reason is rejected");
            var edit = ProjectInput.From(project); edit.Name = name + " v2"; edit.Reason = "Ext redenumire";
            var edited = await projects.UpdateAsync(project, edit);
            Check(edited.Version == project.Version + 1, "A project edit increments the version");
            await Rejects<ProjectOperationException>(() => projects.UpdateAsync(project, edit), "A stale project edit is rejected");
            var move = ProjectInput.From(edited); move.BeneficiaryId = two.Id; move.Reason = "Ext mutare";
            var auditBefore = (await audit.GetEventsAsync()).Count;
            await Rejects<ProjectOperationException>(() => projects.UpdateAsync(edited, move), "Moving a project to another beneficiary is rejected");
            Check((await audit.GetEventsAsync()).Count == auditBefore, "The rejected move leaves the journal unchanged");
            project = edited;

            var observation = await projects.CreateObservationAsync(project.Id, new ProjectObservationInput { Name = "Ext observatie", Content = "Continut" }, "ext.tester");
            var obsEdit = ProjectObservationInput.From(observation); obsEdit.Content = "Continut nou";
            await Rejects<ProjectOperationException>(() => projects.UpdateObservationAsync(observation, obsEdit), "An observation edit without a reason is rejected");
            obsEdit.Reason = "Ext observatie editata";
            var obsEdited = await projects.UpdateObservationAsync(observation, obsEdit);
            Check(obsEdited.Version == observation.Version + 1, "An observation edit increments the version");

            var bytes = "continut fisier ext"u8.ToArray();
            var file = await files.SaveAsync(obsEdited.Id, "plan.txt", "text/plain", bytes, "ext.tester");
            var content = await files.GetContentAsync(file.Id);
            Check(content is not null && content.Content.SequenceEqual(bytes), "An observation file is stored and read back byte for byte");
            Check(file.Sha256.Length == 64, "The stored file has a SHA-256 hash");
            await Rejects<ProjectOperationException>(() => files.SaveAsync(987654, "orfan.txt", "text/plain", "x"u8.ToArray(), "ext.tester"), "A file for a missing observation is rejected");
            await files.DeleteAsync(file.Id, "Ext stergere fisier");
            Check((await files.GetFilesAsync(obsEdited.Id)).Count == 0 && await files.GetContentAsync(file.Id) is null, "A deleted observation file is cleared from the live store");
            Check(await ScalarLongAsync(probe, "SELECT COUNT(*) FROM archive_project_observation_files WHERE original_id=@id", ("@id", file.Id)) == 1, "The deleted file is archived exactly once");

            await projects.DeleteObservationAsync(obsEdited, "Ext curatare observatie");
            await projects.DeleteAsync(project, "Ext stergere proiect"); project = null;
            Check(await ScalarLongAsync(probe, "SELECT COUNT(*) FROM archive_projects WHERE original_id=@id", ("@id", edited.Id)) == 1, "The deleted project is archived exactly once");
        }
        finally
        {
            if (project is not null) await projects.DeleteAsync((await projects.GetAsync(project.Id))!, "Ext curatare");
            foreach (var item in (await beneficiaries.GetBeneficiariesAsync()).Where(item => item.Id == one.Id || item.Id == two.Id))
                await beneficiaries.DeleteAsync(item, "Ext curatare");
        }
    }
}
