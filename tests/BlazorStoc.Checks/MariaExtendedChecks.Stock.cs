using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using BlazorStoc.Services;

namespace BlazorStoc.Checks;

public static partial class MariaExtendedChecks
{
    private static async Task CategoryNamesAndDeletionAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit)
    {
        var suffix = Suffix();
        var started = DateTime.UtcNow.AddSeconds(-1);
        var products = new MariaProductRepository(configuration, admin, audit);
        var category = $"Ext Nume Cat {suffix}";
        var subcategory = $"Ext Nume Sub {suffix}";
        async Task<bool> Rejected(Func<Task> action) { try { await action(); return false; } catch (ProductOperationException) { return true; } }
        await products.CreateCategoryAsync(category);
        await products.CreateSubcategoryAsync(category, subcategory);
        Check(await Rejected(() => products.CreateSubcategoryAsync(category, category.ToUpperInvariant())) && await Rejected(() => products.CreateCategoryAsync(subcategory)),
            "A subcategory cannot be named like a category and a category not like a subcategory (case aside)");
        var other = $"Ext Nume Alta {suffix}";
        await products.CreateCategoryAsync(other);
        Check(await Rejected(() => products.RenameCategoryAsync(other, subcategory, "test")) && await Rejected(() => products.UpdateSubcategoryAsync(new ProductGroup(category, subcategory), other, category, "test")),
            "Renaming a category to the name of a subcategory, and a subcategory to the name of a category, is refused");
        var product = await products.CreateAsync(new ProductInput { Name = $"NM-{suffix}", Category = category, Subcategory = subcategory });
        Check(await Rejected(() => products.DeleteCategoryAsync(category, "test")) && await Rejected(() => products.DeleteSubcategoryAsync(new ProductGroup(category, subcategory), "test")),
            "A category with a subcategory and a subcategory with a product cannot be deleted");
        await products.DeleteAsync((await products.GetProductAsync(product.Id))!, "Ext curatare");
        await products.DeleteSubcategoryAsync(new ProductGroup(category, subcategory), "Ext curatare");
        Check(await Rejected(() => products.DeleteSubcategoryAsync(new ProductGroup(category, subcategory), "test")), "A subcategory that is already deleted says that it no longer exists");
        await products.DeleteCategoryAsync(category, "Ext curatare");
        await products.DeleteCategoryAsync(other, "Ext curatare");
        var groups = await products.GetGroupsAsync();
        var events = (await audit.GetEventsAsync()).Where(item => item.TimestampUtc >= started && (item.Action == AuditActions.DeleteSubcategory || item.Action == AuditActions.DeleteCategory)).ToList();
        Check(groups.All(group => group.Category != category && group.Category != other) && events.Count(item => item.Action == AuditActions.DeleteCategory) == 2 && events.Count(item => item.Action == AuditActions.DeleteSubcategory) == 1,
            "The empty subcategory and the empty categories are deleted and journaled as their own operations");
    }

    private static async Task ProductParametersAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe)
    {
        var suffix = Suffix();
        var started = DateTime.UtcNow.AddSeconds(-1);
        var today = DateOnly.FromDateTime(DateTime.Now);
        var user = new TestAccessControl(false, "utilizator.parametri");
        var products = new MariaProductRepository(configuration, admin, audit);
        var parameters = new MariaProductParameterRepository(configuration, admin, audit);
        var userParameters = new MariaProductParameterRepository(configuration, user, audit);
        var movements = new MariaStockMovementRepository(configuration, admin, audit);
        var category = $"Ext Param Cat {suffix}";
        var subcategory = $"Ext Param Sub {suffix}";
        await products.CreateCategoryAsync(category);
        await products.CreateSubcategoryAsync(category, subcategory);
        var group = new ProductGroup(category, subcategory);
        var oldProduct = await products.CreateAsync(new ProductInput { Name = $"VECHI-{suffix}", Category = category, Subcategory = subcategory });

        // Definition: a user adds parameters; the name is unique in the subcategory; the unit exists only for a number.
        var lens = await userParameters.AddParameterAsync(group, "Lentila", "mm", ParameterKind.Number);
        var color = await userParameters.AddParameterAsync(group, "Culoare", "mm", ParameterKind.Text);
        Check(lens.Position == 1 && color.Position == 2 && color.Unit == "" && lens.Unit == "mm", "A parameter is added by a user, in order; a text parameter has no unit");
        await Rejects<ProductOperationException>(() => userParameters.AddParameterAsync(group, "lentilă", "", ParameterKind.Number), "The same parameter name twice in a subcategory is refused (case aside)");
        await Rejects<AccessDeniedException>(() => userParameters.UpdateParameterAsync(lens, "Lentila", "mm", "test"), "A user who is not an administrator cannot edit a parameter");

        // Values: numbers are written one way, a duplicate is refused, a text is not a number.
        var v28 = await userParameters.AddValueAsync(lens.Id, "2,8");
        var v4 = await userParameters.AddValueAsync(lens.Id, " 4.0 ");
        Check(v28.Value == "2.8" && v4.Value == "4", "A number is stored in one form (2,8 → 2.8; 4.0 → 4)");
        await Rejects<ProductOperationException>(() => userParameters.AddValueAsync(lens.Id, "2.80"), "The same number written differently is a duplicate");
        await Rejects<ProductOperationException>(() => userParameters.AddValueAsync(lens.Id, "patru"), "A text is refused for a number parameter");
        var white = await userParameters.AddValueAsync(color.Id, "alb");
        var black = await userParameters.AddValueAsync(color.Id, "negru");
        await Rejects<ProductOperationException>(() => userParameters.AddValueAsync(color.Id, "ALB"), "A text value twice (case aside) is refused");

        // The product that was there before the parameters is blocked until it is completed.
        var incomplete = await parameters.GetIncompleteProductIdsAsync();
        Check(incomplete.Contains(oldProduct.Id), "A product that misses the new parameters is marked incomplete");
        var blocked = await Rejects<StockMovementOperationException>(() => movements.CreateAsync(oldProduct.Id,
            new StockMovementInput { Kind = StockMovementKind.Entry, Date = today, Quantity = 1, Description = "Ext param blocat" }), "A stock entry on a product with missing parameters is refused");
        Check(blocked!.Message.Contains("Lentila", StringComparison.Ordinal) && blocked.Message.Contains("Culoare", StringComparison.Ordinal), "The refusal names the missing parameters");

        // A new product: model + a value for each parameter; the code is composed in the order of the parameters.
        var model = $"CAM-{suffix}";
        ProductInput Input(string? baseModel, params (SubcategoryParameter Parameter, int ValueId)[] choices) => new()
        {
            Name = baseModel ?? "", BaseModel = baseModel ?? "", Category = category, Subcategory = subcategory,
            Parameters = choices.Select(choice => new ProductParameterChoice(choice.Parameter.Id, choice.ValueId)).ToList()
        };
        await Rejects<ProductOperationException>(() => products.CreateAsync(Input(null, (lens, v28.Id), (color, white.Id))), "A product of the subcategory without a model is refused");
        await Rejects<ProductOperationException>(() => products.CreateAsync(Input(model, (lens, v28.Id))), "A product that misses one parameter is refused");
        await Rejects<ProductOperationException>(() => products.CreateAsync(Input(model, (lens, v28.Id), (color, 2_000_000_000))), "A value that does not exist is refused");
        var first = await products.CreateAsync(Input(model, (lens, v28.Id), (color, white.Id)));
        Check(first.Name == $"{model} - 2.8 mm - alb", "The code is \"<model> - <value> - <value>\" in the order of the parameters");
        var state = await parameters.GetProductStateAsync(first.Id);
        Check(state.BaseModel == model && state.Choices.Count == 2 && state.Choices.Any(item => item.ValueId == white.Id), "The model and the chosen values are kept with the product");
        var baseModels = await parameters.GetBaseModelsAsync();
        Check(baseModels.GetValueOrDefault(first.Id) == model && !baseModels.ContainsKey(oldProduct.Id), "The model of each product with parameters is available for grouping the variants (a product without values has none)");
        await Rejects<ProductOperationException>(() => products.CreateAsync(Input(model, (lens, v28.Id), (color, white.Id))), "The same model with the same values is the same code: refused");
        var second = await products.CreateAsync(Input(model, (lens, v4.Id), (color, white.Id)));
        Check(second.Name == $"{model} - 4 mm - alb", "Another value of the lens is another variant of the same model");
        await movements.CreateAsync(first.Id, new StockMovementInput { Kind = StockMovementKind.Entry, Date = today, Quantity = 5, Description = "Ext param intrare" });
        Check((await products.GetProductAsync(first.Id))!.Quantity == 5, "A complete product takes stock entries");

        // Completing the old product removes the block.
        var completed = await products.UpdateAsync(oldProduct, new ProductInput
        {
            Name = oldProduct.Name, BaseModel = oldProduct.Name, Category = category, Subcategory = subcategory,
            Parameters = [new(lens.Id, v28.Id), new(color.Id, black.Id)], Reason = "Ext completare parametri"
        });
        Check(completed.Name == $"{oldProduct.Name} - 2.8 mm - negru" && !(await parameters.GetIncompleteProductIdsAsync()).Contains(oldProduct.Id), "Choosing the values completes the product and lifts the block");
        await movements.CreateAsync(oldProduct.Id, new StockMovementInput { Kind = StockMovementKind.Entry, Date = today, Quantity = 1, Description = "Ext param dupa completare" });

        // Deleting values: only the unused ones, by a user.
        var spare = await userParameters.AddValueAsync(lens.Id, "6");
        await Rejects<ProductOperationException>(() => userParameters.DeleteValueAsync(v28.Id, "test"), "A value attached to products cannot be deleted");
        await userParameters.DeleteValueAsync(spare.Id, "Ext valoare nefolosita");
        Check((await parameters.GetParametersAsync()).Single(item => item.Id == lens.Id).Values.All(item => item.Id != spare.Id), "An unused value is deleted by a user");
        var counts = (await parameters.GetParametersAsync()).Single(item => item.Id == lens.Id).Values.Single(item => item.Id == v28.Id).ProductCount;
        Check(counts == 2, "The list of values shows how many products use each value");

        // Changing a used value: administrators only; a collision with another product blocks everything; otherwise the products follow the value.
        await Rejects<AccessDeniedException>(() => new MariaProductParameterRepository(configuration, user, audit).ChangeValueAsync(v28.Id, "3", "test"), "A user who is not an administrator cannot change a value");
        await Rejects<ProductOperationException>(() => parameters.PreviewValueChangeAsync(v28.Id, "4"), "Changing a value into one that is already in the list is refused");
        var otherSubcategory = $"Ext Param Alta {suffix}";
        await products.CreateSubcategoryAsync(category, otherSubcategory);
        var squatter = await products.CreateAsync(new ProductInput { Name = $"{model} - 2.9 mm - alb", Category = category, Subcategory = otherSubcategory });
        var conflictPreview = await parameters.PreviewValueChangeAsync(v28.Id, "2.9");
        Check(conflictPreview.Affected.Count == 2 && conflictPreview.Conflicts.Count == 1 && !conflictPreview.CanApply, "The preview lists the products that follow the value and the code another product already has");
        var beforeNames = (await products.GetProductsAsync()).Where(item => item.Category == category).ToDictionary(item => item.Id, item => item.Name);
        await Rejects<ProductOperationException>(() => parameters.ChangeValueAsync(v28.Id, "2.9", "Ext conflict"), "A change that would give two products the same code is refused");
        var afterNames = (await products.GetProductsAsync()).Where(item => item.Category == category).ToDictionary(item => item.Id, item => item.Name);
        Check(beforeNames.Count == afterNames.Count && beforeNames.All(item => afterNames[item.Key] == item.Value), "A refused change leaves every product as it was");
        await products.DeleteAsync((await products.GetProductAsync(squatter.Id))!, "Ext curatare");

        var changed = await parameters.ChangeValueAsync(v28.Id, "2.9", "Ext corectie valoare");
        Check(changed.Affected.Count == 2 && changed.Affected.All(item => item.NewName.Contains(" - 2.9 mm - ", StringComparison.Ordinal)), "The products that use the value are renamed with it");
        var renamed = await products.GetProductAsync(first.Id);
        Check(renamed!.Name == $"{model} - 2.9 mm - alb" && renamed.Version > first.Version && renamed.Quantity == 5, "A renamed product keeps its stock and gets a new version");
        Check((await parameters.GetParametersAsync()).Single(item => item.Id == lens.Id).Values.Any(item => item.Id == v28.Id && item.Value == "2.9"), "The value itself is changed once for all");

        // A parameter cannot be deleted while the subcategory has products; the journal names each operation.
        await Rejects<ProductOperationException>(() => parameters.DeleteParameterAsync(lens, "Ext sters"), "A parameter of a subcategory with products cannot be deleted");
        var events = (await audit.GetEventsAsync()).Where(item => item.TimestampUtc >= started).ToList();
        Check(events.Count(item => item.Action == AuditActions.AddSubcategoryParameter) == 2 && events.Count(item => item.Action == AuditActions.AddParameterValue) >= 4
              && events.Any(item => item.Action == AuditActions.DeleteParameterValue && item.Motif == "Ext valoare nefolosita")
              && events.Any(item => item.Action == AuditActions.EditParameterValue && item.Details.Contains("2.8 mm → 2.9 mm", StringComparison.Ordinal) && item.Motif == "Ext corectie valoare")
              && events.Count(item => item.Action == AuditActions.RenameProductByParameter) == 2,
            "The journal names each operation: parameter and value added, value deleted, value changed and every product renamed");
        var createEvent = events.FirstOrDefault(item => item.EntityType == AuditEntities.Product && item.Action == AuditActions.Create && item.Target == first.Name);
        Check(createEvent is not null && createEvent.Details.Contains("Parametri: Lentila: 2.8 mm; Culoare: alb", StringComparison.Ordinal), "The creation of a product records its parameters");
        var editEvent = events.FirstOrDefault(item => item.EntityType == AuditEntities.Product && item.Action == AuditActions.Edit && item.Target == completed.Name);
        Check(editEvent is not null && editEvent.Details.Contains("Parametri:  → Lentila: 2.8 mm; Culoare: negru", StringComparison.Ordinal), "A product completed with its values records the parameters before and after");
    }

    private static async Task SystemTypesAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe)
    {
        var suffix = Suffix();
        var started = DateTime.UtcNow.AddSeconds(-1);
        var repository = new MariaSystemTypeRepository(configuration, admin, audit);
        var created = new List<int>();
        try
        {
            async Task<bool> Journaled(string action, int id) => (await audit.GetEventsAsync()).Any(item => item.TimestampUtc >= started && item.Action == action && item.EntityId == id.ToString());
            var fire = await repository.CreateAsync($"Antiefracție Ext {suffix}");
            var cctv = await repository.CreateAsync($"TVCI Ext {suffix}");
            created.AddRange([fire.Id, cctv.Id]);

            // 1. Uniqueness ignores case, diacritics and separators, for names and alternative names alike.
            var sameName = await Rejects<SystemTypeOperationException>(() => repository.CreateAsync($"ANTIEFRACTIE-ext {suffix}"), "A name that differs only by case, diacritics or separators is refused");
            await repository.AddAliasAsync(cctv, $"CCTV Ext {suffix}");
            var aliasOfName = await Rejects<SystemTypeOperationException>(() => repository.AddAliasAsync(fire, $"cctv ext {suffix}"), "An alternative name already used is refused");
            Check(sameName is not null && aliasOfName is not null, "A name or alternative name equal to another one (ignoring case, diacritics, separators) is refused");

            // 2. An alternative name finds its type (the way an offer says "CCTV" for "TVCI"); removing it forgets it.
            var found = await repository.FindAsync($"cctv ext  {suffix}");
            cctv = (await repository.GetAllAsync()).Single(type => type.Id == cctv.Id);
            await repository.RemoveAliasAsync(cctv, $"CCTV Ext {suffix}");
            Check(found?.Id == cctv.Id && await repository.FindAsync($"CCTV Ext {suffix}") is null, "An alternative name finds its type until it is removed");

            // 3. Deactivating keeps the type (inactive), it can be reactivated and moved; a stale version is refused.
            var stale = fire;
            var inactive = await repository.SetActiveAsync(fire, false);
            var staleRejected = await Rejects<SystemTypeOperationException>(() => repository.RenameAsync(stale, $"Alt nume {suffix}"), "A change on an old version is refused");
            var reactivated = await repository.SetActiveAsync(inactive, true);
            var before = (await repository.GetAllAsync()).Select(type => type.Id).ToList();
            await repository.MoveAsync((await repository.GetAllAsync()).Single(type => type.Id == cctv.Id), -1);
            var after = (await repository.GetAllAsync()).Select(type => type.Id).ToList();
            Check(!inactive.Active && reactivated.Active && staleRejected is not null && after.IndexOf(cctv.Id) == before.IndexOf(cctv.Id) - 1,
                "A type is deactivated and reactivated without being lost, moved in the order, and a stale change is refused");

            // 4. Each operation has its own journal action.
            var renamed = await repository.RenameAsync((await repository.GetAllAsync()).Single(type => type.Id == cctv.Id), $"Televiziune Ext {suffix}");
            Check(await Journaled(AuditActions.AddSystemType, fire.Id) && await Journaled(AuditActions.AddSystemTypeAlias, cctv.Id) && await Journaled(AuditActions.RemoveSystemTypeAlias, cctv.Id)
                  && await Journaled(AuditActions.DeactivateSystemType, fire.Id) && await Journaled(AuditActions.ActivateSystemType, fire.Id)
                  && await Journaled(AuditActions.MoveSystemType, cctv.Id) && await Journaled(AuditActions.RenameSystemType, renamed.Id),
                "The journal names each operation on the types (add, rename, deactivate, activate, move, add and remove alternative name)");
        }
        finally
        {
            foreach (var id in created) await ExecuteAsync(probe, "DELETE FROM system_types WHERE id=@id", ("@id", id));
        }
    }

    private static async Task VehicleTargetsAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe)
    {
        var suffix = Suffix();
        var started = DateTime.UtcNow.AddSeconds(-1);
        var category = $"Ext Tinta Cat {suffix}";
        var subcategory = $"Ext Tinta Sub {suffix}";
        var products = new MariaProductRepository(configuration, admin, audit);
        var vehicles = new MariaVehicleRepository(configuration, admin, audit);
        var targets = new MariaVehicleTargetRepository(configuration, admin, audit);
        await products.CreateCategoryAsync(category);
        await products.CreateSubcategoryAsync(category, subcategory);
        var product = await products.CreateAsync(new ProductInput { Name = $"Ext Tinta Produs {suffix}", Category = category, Subcategory = subcategory });
        var car = await vehicles.CreateAsync(new VehicleInput { PlateNumber = "TS-95-" + Letters(), Description = "Ext tinta", ItpExpiry = Expiry, InsuranceExpiry = Expiry, RovinietaExpiry = Expiry });
        try
        {
            async Task<bool> Journaled(string action) => (await audit.GetEventsAsync()).Any(item => item.TimestampUtc >= started && item.Action == action);
            await targets.SetAsync(car.Id, product.Id, 5);
            await targets.SetAsync(car.Id, product.Id, 8);
            var listed = await targets.GetForVehicleAsync(car.Id);
            var invalid = await Rejects<VehicleTargetException>(() => targets.SetAsync(car.Id, product.Id, 0), "A target below one is refused");
            Check(listed.Count == 1 && listed[0].Target == 8 && listed[0].ProductId == product.Id && invalid is not null && await Journaled(AuditActions.SetVehicleTarget),
                "A vehicle target level is set once per product, changed in place, validated and journaled");
            Check(VehicleTargetRules.Missing(8, 3) == 5 && VehicleTargetRules.Missing(8, 8) == 0 && VehicleTargetRules.Missing(8, 12) == 0 && VehicleTargetRules.Missing(8, -2) == 8,
                "The missing pieces are the target minus what the vehicle holds, never negative");
            await targets.RemoveAsync(car.Id, product.Id);
            Check((await targets.GetForVehicleAsync(car.Id)).Count == 0 && await Journaled(AuditActions.RemoveVehicleTarget), "Removing a target level is journaled");
        }
        finally
        {
            await vehicles.DeleteAsync((await vehicles.GetVehiclesAsync()).First(item => item.Id == car.Id), "Ext curatare");
            await products.DeleteAsync((await products.GetProductAsync(product.Id))!, "Ext curatare");
            await ExecuteAsync(probe, "DELETE FROM subcategories WHERE name=@n", ("@n", subcategory));
            await ExecuteAsync(probe, "DELETE FROM categories WHERE name=@n", ("@n", category));
        }
    }

    private static async Task StockAlertsAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe)
    {
        var suffix = Suffix();
        var started = DateTime.UtcNow.AddSeconds(-1);
        var category = $"Ext Alerta Cat {suffix}";
        var subcategory = $"Ext Alerta Sub {suffix}";
        var products = new MariaProductRepository(configuration, admin, audit);
        var movements = new MariaStockMovementRepository(configuration, admin, audit);
        var reservations = new MariaReservationRepository(configuration, admin, audit);
        var minimums = new MariaProductMinStockRepository(configuration, admin, audit);
        var deadlines = new MariaProjectDeadlineRepository(configuration, admin, audit);
        var alerts = new MariaStockAlertReader(configuration);
        var beneficiaries = new MariaBeneficiaryRepository(configuration, admin, audit);
        var projects = new MariaProjectRepository(configuration, new TestWebHostEnvironment(Path.GetTempPath()), admin, audit);
        var today = DateOnly.FromDateTime(DateTime.Now);
        await products.CreateCategoryAsync(category);
        await products.CreateSubcategoryAsync(category, subcategory);
        var product = await products.CreateAsync(new ProductInput { Name = $"Ext Alerta Produs {suffix}", Category = category, Subcategory = subcategory });
        var beneficiary = await beneficiaries.CreateAsync(Legal($"Ext Alerta Client {suffix} SRL", "RO" + Random.Shared.Next(60000000, 69999999)));
        var project = await projects.CreateAsync(new ProjectInput { BeneficiaryId = beneficiary.Id, Name = $"Ext Alerta Proiect {suffix}" });
        try
        {
            async Task<bool> Journaled(string action) => (await audit.GetEventsAsync()).Any(item => item.TimestampUtc >= started && item.Action == action);
            await movements.CreateAsync(product.Id, new StockMovementInput { Kind = StockMovementKind.Entry, Date = today.AddDays(-2), Quantity = 10, Description = "Ext alerta stoc" });

            // 1. Minimum stock: set/change/validate/journal; below the minimum the product is an instance of the notification, at or above it is not; removing closes it.
            await minimums.SetAsync(product.Id, 4);
            var invalid = await Rejects<StockAlertException>(() => minimums.SetAsync(product.Id, 0), "A minimum below one is refused");
            var noneBelow = (await alerts.GetBelowMinimumAsync()).All(item => item.ProductId != product.Id);
            await movements.CreateAsync(product.Id, new StockMovementInput
            {
                Kind = StockMovementKind.Exit, Date = today, Quantity = 8, Description = "Ext alerta iesire", Destination = ExitDestination.Beneficiary,
                BeneficiaryId = beneficiary.Id, ProjectId = project.Id, Reference = "Aviz alerta"
            });
            var source = new MinStockSource(alerts);
            var below = (await source.GetInstancesAsync()).SingleOrDefault(item => item.ObjectId == product.Id);
            Check(await minimums.GetAsync(product.Id) == 4 && invalid is not null && noneBelow && below is not null && below.Expiry == today && below.Values[MinStockSource.MissingName] == "2"
                  && await Journaled(AuditActions.SetMinStock), "A product below its minimum stock is a notification instance (with what is missing) and the setting is journaled");
            await minimums.RemoveAsync(product.Id);
            Check(await minimums.GetAsync(product.Id) is null && (await source.GetInstancesAsync()).All(item => item.ObjectId != product.Id) && await Journaled(AuditActions.RemoveMinStock),
                "Removing the minimum stock closes the notification and is journaled");

            // 2. A reservation unchanged for more than the stale period is an instance of its notification; a recent one is not.
            var reservation = await reservations.ReserveAsync(project.Id, null, product.Id, 1, capToFree: true);
            var staleSource = new StaleReservationSource(alerts);
            var fresh = (await staleSource.GetInstancesAsync()).All(item => item.ObjectId != reservation.Id);
            await ExecuteAsync(probe, "UPDATE project_reservations SET updated_utc=@old WHERE id=@id", ("@old", MariaTimeText.Format(DateTime.UtcNow.AddDays(-40))), ("@id", reservation.Id));
            var stale = (await staleSource.GetInstancesAsync()).SingleOrDefault(item => item.ObjectId == reservation.Id);
            Check(fresh && stale is not null && stale.Expiry < today && stale.Values[StaleReservationSource.ProjectName] == project.Name,
                "A reservation unchanged for more than 30 days is a notification instance, a recent one is not");

            // 3. Project deadline: not in the past, one per project, replaced in place, removed, journaled.
            var past = await Rejects<StockAlertException>(() => deadlines.SetAsync(project.Id, today.AddDays(-1)), "A deadline in the past is refused");
            await deadlines.SetAsync(project.Id, today.AddDays(10));
            await deadlines.SetAsync(project.Id, today.AddDays(12));
            var listed = (await alerts.GetProjectDeadlinesAsync()).Where(item => item.ProjectId == project.Id).ToList();
            Check(past is not null && listed.Count == 1 && listed[0].Deadline == today.AddDays(12) && await deadlines.GetAsync(project.Id) == today.AddDays(12) && await Journaled(AuditActions.SetProjectDeadline),
                "A project deadline is set once per project, replaced in place, refused in the past and journaled");
            await deadlines.RemoveAsync(project.Id);
            Check(await deadlines.GetAsync(project.Id) is null && await Journaled(AuditActions.RemoveProjectDeadline), "Removing the project deadline is journaled");

            // 4. Consumption export: per product, beneficiary and project; the filters of project and period apply; the CSV has the rows.
            var consumption = new MariaConsumptionReader(configuration, admin);
            var all = await consumption.GetAsync(new ConsumptionQuery(beneficiary.Id, project.Id, null, null));
            var later = await consumption.GetAsync(new ConsumptionQuery(beneficiary.Id, project.Id, today.AddDays(1), null));
            var csv = ConsumptionExportRules.ToCsv(new ConsumptionQuery(beneficiary.Id, project.Id, null, null), all);
            Check(all.Count == 1 && all[0].Exited == 8 && all[0].Net == 8 && all[0].ProjectName == project.Name && later.Count == 0
                  && csv.Contains("Produs;Beneficiar;Proiect;Iesit;Returnat;Consum net") && csv.Contains($"{all[0].ProductName};{beneficiary.Name};{project.Name};8;0;8"),
                "The consumption export lists what was handed over per product and project, honours the period and writes the CSV");
        }
        finally
        {
            await ExecuteAsync(probe, "DELETE FROM stock_movements WHERE product_id=@id", ("@id", product.Id));
            await ExecuteAsync(probe, "UPDATE products SET quantity=0 WHERE id=@id", ("@id", product.Id));
            await ExecuteAsync(probe, "DELETE FROM project_reservations WHERE product_id=@id", ("@id", product.Id));
            await projects.DeleteAsync((await projects.GetAsync(project.Id))!, "Ext curatare");
            await beneficiaries.DeleteAsync((await beneficiaries.GetBeneficiariesAsync()).First(item => item.Id == beneficiary.Id), "Ext curatare");
            await products.DeleteAsync((await products.GetProductAsync(product.Id))!, "Ext curatare");
            await ExecuteAsync(probe, "DELETE FROM subcategories WHERE name=@n", ("@n", subcategory));
            await ExecuteAsync(probe, "DELETE FROM categories WHERE name=@n", ("@n", category));
        }
    }

    // ---- Products ------------------------------------------------------------------------------------------------
    private static async Task ProductsAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe)
    {
        var suffix = Suffix();
        var category = $"Ext Categorie {suffix}";
        var subcategory = $"Ext Subcategorie {suffix}";
        var products = new MariaProductRepository(configuration, admin, audit);
        var movements = new MariaStockMovementRepository(configuration, admin, audit);
        await products.CreateCategoryAsync(category);
        await products.CreateSubcategoryAsync(category, subcategory);
        var movementIds = new List<int>();
        Product? main = null;
        try
        {
            main = await products.CreateAsync(new ProductInput { Name = $"Ext Produs {suffix}", Category = category, Subcategory = subcategory });

            // Two sessions save the same normalized product code: exactly one wins.
            var sameName = $"Ext Cod Dublu {suffix}";
            var attempts = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
            {
                try { return await new MariaProductRepository(configuration, admin, audit).CreateAsync(new ProductInput { Name = sameName, Category = category, Subcategory = subcategory }); }
                catch (ProductOperationException) { return null; }
            }));
            Check(attempts.Count(item => item is not null) == 1, "Two concurrent sessions cannot save the same normalized product code");
            var winner = attempts.Single(item => item is not null)!;

            // Two sessions delete the same snapshot: exactly one wins.
            var deletions = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
            {
                try { await new MariaProductRepository(configuration, admin, audit).DeleteAsync(winner, "Ext stergere concurenta"); return true; }
                catch (ProductOperationException) { return false; }
            }));
            Check(deletions.Count(item => item) == 1, "Two concurrent deletions of the same product: exactly one succeeds");
            Check(await products.GetProductAsync(winner.Id) is null, "The concurrently deleted product is gone");

            var noReason = ProductInput.From(main); noReason.Description = "Fara motiv";
            await Rejects<ProductOperationException>(() => products.UpdateAsync(main, noReason), "A product edit without a reason is rejected");
            var auditBefore = (await audit.GetEventsAsync()).Count;
            Check((await audit.GetEventsAsync()).Count == auditBefore, "A rejected edit writes nothing to the journal");

            // Stock changed after the form was opened is preserved by an edit and blocks the deletion.
            var entry = await movements.CreateAsync(main.Id, new StockMovementInput { Kind = StockMovementKind.Entry, Date = DateOnly.FromDateTime(DateTime.Now), Quantity = 7, Description = "Ext intrare" });
            movementIds.Add(entry.Movement.Id);
            var edit = ProductInput.From(main); edit.Description = "Descriere noua"; edit.Reason = "Ext editare";
            var edited = await new MariaProductRepository(configuration, admin, audit).UpdateAsync(await products.GetProductAsync(main.Id) ?? main, edit);
            Check(edited.Quantity == 7, "A product edit does not overwrite a stock changed by a movement");
            await Rejects<ProductOperationException>(() => products.DeleteAsync(edited with { Quantity = 0 }, "Ext"), "Deletion is blocked by the current stock, not by a stale snapshot");

            // Empty groups survive the deletion of their last product; renaming updates the products.
            var groupOnly = await products.CreateAsync(new ProductInput { Name = $"Ext Ultimul {suffix}", Category = category, Subcategory = subcategory });
            await products.DeleteAsync(groupOnly, "Ext ultimul produs");
            Check((await products.GetGroupsAsync()).Contains(new ProductGroup(category, subcategory)), "The catalogue group is kept after deleting a product");
            var renamed = $"Ext Categorie Noua {suffix}";
            await products.RenameCategoryAsync(category, renamed, "Ext redenumire");
            category = renamed;
            Check((await products.GetProductAsync(main.Id))?.Category == renamed, "Renaming a category updates the associated products");
            var neighbour = $"Ext Categorie Vecina {suffix}";
            await products.CreateCategoryAsync(neighbour);
            try
            {
                await Rejects<ProductOperationException>(() => products.RenameCategoryAsync(renamed, neighbour.ToUpperInvariant(), "Duplicat"), "Renaming a category onto another existing normalized name is rejected");
            }
            finally { await ExecuteAsync(probe, "DELETE FROM categories WHERE name=@n", ("@n", neighbour)); }
            var archived = await ScalarLongAsync(probe, "SELECT COUNT(*) FROM archive_products WHERE original_id=@id", ("@id", winner.Id));
            Check(archived == 1, "The deleted product is archived exactly once");
        }
        finally
        {
            foreach (var id in movementIds)
                if (await movements.GetAsync(id) is { } stored) await movements.DeleteAsync(stored, "Ext curatare");
            if (main is not null && await products.GetProductAsync(main.Id) is { } current) await products.DeleteAsync(current, "Ext curatare");
            await ExecuteAsync(probe, "DELETE FROM subcategories WHERE name=@n", ("@n", subcategory));
            await ExecuteAsync(probe, "DELETE FROM categories WHERE name IN (@a,@b)", ("@a", $"Ext Categorie {suffix}"), ("@b", category));
        }
    }

    // ---- Stock movements & vehicles ----------------------------------------------------------------------------------------
    private static async Task MovementsAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe)
    {
        var suffix = Suffix();
        var category = $"Ext Miscari Cat {suffix}";
        var subcategory = $"Ext Miscari Sub {suffix}";
        var products = new MariaProductRepository(configuration, admin, audit);
        var vehicles = new MariaVehicleRepository(configuration, admin, audit);
        var movements = new MariaStockMovementRepository(configuration, admin, audit);
        await products.CreateCategoryAsync(category);
        await products.CreateSubcategoryAsync(category, subcategory);
        var product = await products.CreateAsync(new ProductInput { Name = $"Ext Miscari {suffix}", Category = category, Subcategory = subcategory });
        var carOne = await vehicles.CreateAsync(new VehicleInput { PlateNumber = "TS-91-" + Letters(), Description = "Ext masina 1", ItpExpiry = Expiry, InsuranceExpiry = Expiry, RovinietaExpiry = Expiry });
        var carTwo = await vehicles.CreateAsync(new VehicleInput { PlateNumber = "TS-92-" + Letters(), Description = "Ext masina 2", ItpExpiry = Expiry, InsuranceExpiry = Expiry, RovinietaExpiry = Expiry });
        var today = DateOnly.FromDateTime(DateTime.Now);
        try
        {
            await Rejects<StockMovementOperationException>(() => movements.CreateAsync(product.Id, new StockMovementInput
            { Kind = StockMovementKind.Entry, Date = today.AddDays(30), Quantity = 1, Description = "Ext viitor" }), "A movement dated in the future is rejected");
            Check((await movements.GetPageAsync(product.Id, new StockMovementQuery())).Stock == 0, "The rejected movement leaves the stock unchanged");

            // Parallel entries from independent sessions all count.
            await Task.WhenAll(Enumerable.Range(0, 8).Select(index => new MariaStockMovementRepository(configuration, admin, audit)
                .CreateAsync(product.Id, new StockMovementInput { Kind = StockMovementKind.Entry, Date = today, Quantity = 1, Description = $"Ext paralel {index}" })));
            var page = await movements.GetPageAsync(product.Id, new StockMovementQuery(PageSize: 50));
            Check(page.Stock == 8 && page.TotalCount == 8, "Eight parallel entries produce stock 8 and eight movements");

            var toCar = await movements.CreateAsync(product.Id, new StockMovementInput
            { Kind = StockMovementKind.Exit, Date = today, Quantity = 6, Destination = ExitDestination.Vehicle, VehicleId = carOne.Id, Description = "Ext spre masina" });
            var edit = StockMovementInput.From(toCar.Movement); edit.Description = "Ext spre masina corectat"; edit.Reason = "Ext corectie";
            var edited = await movements.UpdateAsync(toCar.Movement, edit);
            Check((await movements.GetHistoryAsync(edited.Movement.Id)).Count >= 1, "Editing a movement records its history");

            var move = await movements.TransferFromVehicleAsync(new VehicleTransfer(carOne.Id, carTwo.Id, [new VehicleTransferLine(product.Id, 4)]));
            Check(move.Count == 1, "A vehicle to vehicle transfer creates one movement per product");
            var stocks = await movements.GetVehicleStocksAsync(product.Id);
            Check(stocks.Single(item => item.VehicleId == carOne.Id).Quantity == 2 && stocks.Single(item => item.VehicleId == carTwo.Id).Quantity == 4,
                "After the transfer the vehicles hold 2 and 4 pieces");
            Check((await movements.GetVehicleEquipmentAsync(carTwo.Id)).Single().Quantity == 4, "The vehicle page equipment reflects the transfer");

            // Two sessions try to return more than the vehicle holds: never both.
            var returns = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
            {
                try { await new MariaStockMovementRepository(configuration, admin, audit).TransferFromVehicleAsync(new VehicleTransfer(carTwo.Id, null, [new VehicleTransferLine(product.Id, 3)])); return true; }
                catch (StockMovementOperationException) { return false; }
            }));
            Check(returns.Count(item => item) == 1, "Two concurrent returns of 3 from a vehicle holding 4: exactly one succeeds");
            Check((await movements.GetVehicleStocksAsync(product.Id)).Single(item => item.VehicleId == carTwo.Id).Quantity == 1, "The vehicle keeps the remaining piece");

            await Rejects<VehicleOperationException>(() => vehicles.DeleteAsync(carOne, "Ext"), "A vehicle used by movements cannot be deleted");
            var count = (await movements.GetMovementCountsByVehicleAsync())[carOne.Id];
            Check(count >= 1, "The movement counter per vehicle sees the vehicle's movements");

            await DeleteMovementsNewestFirstAsync(movements, product.Id);
            Check(await ScalarLongAsync(probe, "SELECT COUNT(*) FROM archive_stock_movements WHERE original_id=@id", ("@id", edited.Movement.Id)) == 1, "A deleted movement is archived exactly once");
        }
        finally
        {
            await DeleteMovementsNewestFirstAsync(movements, product.Id);
            await vehicles.DeleteAsync(carOne, "Ext curatare");
            await vehicles.DeleteAsync(carTwo, "Ext curatare");
            await products.DeleteAsync((await products.GetProductAsync(product.Id))!, "Ext curatare");
            await ExecuteAsync(probe, "DELETE FROM subcategories WHERE name=@n", ("@n", subcategory));
            await ExecuteAsync(probe, "DELETE FROM categories WHERE name=@n", ("@n", category));
        }
    }

    // Undoing movements newest first keeps every vehicle's stock non-negative at each step.
    private static async Task DeleteMovementsNewestFirstAsync(IStockMovementRepository movements, int productId)
    {
        var items = (await movements.GetPageAsync(productId, new StockMovementQuery(PageSize: 500))).Items.OrderByDescending(item => item.Id).ToList();
        foreach (var item in items) await movements.DeleteAsync(item, "Ext curatare");
    }

    private static async Task VehiclesAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe)
    {
        var plate = "TS-93-" + Letters();
        var vehicles = new MariaVehicleRepository(configuration, admin, audit);
        var attempts = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            try { return await new MariaVehicleRepository(configuration, admin, audit).CreateAsync(new VehicleInput { PlateNumber = plate, Description = "Ext", ItpExpiry = Expiry, InsuranceExpiry = Expiry, RovinietaExpiry = Expiry }); }
            catch (VehicleOperationException) { return null; }
        }));
        Check(attempts.Count(item => item is not null) == 1, "Two concurrent sessions cannot save the same registration number");
        var vehicle = attempts.Single(item => item is not null)!;
        try
        {
            var edit = VehicleInput.From(vehicle);
            VehicleRules.SetExpiry(edit, VehicleExpiryKind.Itp, new DateOnly(2028, 1, 10));
            edit.Reason = "Modificare data expirare ITP";
            var edited = await vehicles.UpdateAsync(vehicle, edit, default, AuditActions.ExpiryItp);
            Check(edited.ItpExpiry == new DateOnly(2028, 1, 10) && edited.Version == vehicle.Version + 1, "The ITP expiry is changed and the version increments");
            var events = (await audit.GetEventsAsync()).Where(item => item.EntityType == AuditEntities.Vehicle && item.Action == AuditActions.ExpiryItp).ToList();
            Check(events.Count >= 1, "The journal names the exact operation \"Modificare expirare ITP\"");
            await Rejects<VehicleOperationException>(() => vehicles.UpdateAsync(vehicle, edit), "A stale vehicle edit is rejected");
            vehicle = edited;
        }
        finally
        {
            await vehicles.DeleteAsync(vehicle, "Ext curatare");
        }
        Check(await ScalarLongAsync(probe, "SELECT COUNT(*) FROM archive_vehicles WHERE original_id=@id", ("@id", vehicle.Id)) == 1, "The deleted vehicle is archived exactly once");
    }

    // ---- Product locks ---------------------------------------------------------------------------------------------------------
    private static async Task LocksAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe)
    {
        var suffix = Suffix();
        var category = $"Ext Blocari Cat {suffix}";
        var subcategory = $"Ext Blocari Sub {suffix}";
        var products = new MariaProductRepository(configuration, admin, audit);
        await products.CreateCategoryAsync(category);
        await products.CreateSubcategoryAsync(category, subcategory);
        var product = await products.CreateAsync(new ProductInput { Name = $"Ext Blocari {suffix}", Category = category, Subcategory = subcategory });
        var ana = new MariaProductLockRepository(configuration, new TestAccessControl(false, "ana"), audit);
        var bob = new MariaProductLockRepository(configuration, new TestAccessControl(false, "bob"), audit);
        var boss = new MariaProductLockRepository(configuration, admin, audit);
        try
        {
            var first = await ana.AcquireAsync(product.Id, "sesiune-ana");
            Check(first is { Acquired: true, Changed: true }, "The first session takes the lock");
            var second = await bob.AcquireAsync(product.Id, "sesiune-bob");
            Check(second is { Acquired: false } && second.Lock?.Owner == "ana", "A second session is refused and told who holds the lock");
            Check((await bob.RenewAsync(product.Id, "sesiune-bob")).Acquired == false, "A session that does not hold the lock cannot renew it");
            Check((await ana.GetActiveAsync()).Any(item => item.ProductId == product.Id), "The active locks list contains the lock");

            await Rejects<Exception>(() => ana.ForceReleaseAsync(product.Id, "Ext"), "A non-administrator cannot force-release a lock");
            await Rejects<Exception>(() => boss.ForceReleaseAsync(product.Id, " "), "A forced release requires a reason");
            var removed = await boss.ForceReleaseAsync(product.Id, "Ext deblocare fortata");
            Check(removed?.Owner == "ana", "The administrator force-releases the lock and gets the removed lock back");
            Check((await audit.GetEventsAsync()).Any(item => item.Action == AuditActions.Unlock && item.EntityId == product.Id.ToString()), "The forced release is journaled as \"Deblocare\"");
            Check((await ana.RenewAsync(product.Id, "sesiune-ana")).Acquired == false, "A lock released by an administrator is never silently re-acquired by the old holder");

            await ana.AcquireAsync(product.Id, "sesiune-ana");
            await ExecuteAsync(probe, "UPDATE product_locks SET expires_utc=@past WHERE product_id=@p", ("@past", MariaTimeTextForTest(DateTime.UtcNow.AddSeconds(-5))), ("@p", product.Id));
            var takeover = await bob.AcquireAsync(product.Id, "sesiune-bob");
            Check(takeover.Acquired && takeover.Lock?.Owner == "bob", "An expired lock is taken over by another session");

            await products.DeleteAsync((await products.GetProductAsync(product.Id))!, "Ext curatare");
            Check(await ScalarLongAsync(probe, "SELECT COUNT(*) FROM product_locks WHERE product_id=@p", ("@p", product.Id)) == 0, "Deleting the product removes its lock");
        }
        finally
        {
            if (await products.GetProductAsync(product.Id) is { } left) await products.DeleteAsync(left, "Ext curatare");
            await ExecuteAsync(probe, "DELETE FROM subcategories WHERE name=@n", ("@n", subcategory));
            await ExecuteAsync(probe, "DELETE FROM categories WHERE name=@n", ("@n", category));
        }
    }
}
