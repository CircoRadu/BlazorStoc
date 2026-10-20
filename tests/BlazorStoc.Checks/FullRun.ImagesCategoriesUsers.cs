#nullable enable
#pragma warning disable CS1998, CS8600, CS8601, CS8602, CS8603, CS8604, CS8605, CS8618, CS8619, CS8620, CS8625, CS8629, CS8714
using BlazorStoc.Checks;
using BlazorStoc.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PdfSharp.Pdf.IO;
using System.IO.Compression;
using System.Text.Json;

public static partial class FullRun
{
    // Lines 543-782 of the former Program.cs.
    internal static async Task ImagesCategoriesUsersAsync(string[] args)
    {
        try
        {
            new ProductInput { Name = new string('C', 101), Category = "Test", Subcategory = "Test" }.Validated();
            throw new Exception("Overlong product code accepted");
        }
        catch (ProductOperationException exception)
        {
            Check(exception.Message.Contains("Codul produsului poate avea cel mult 100 de caractere.", StringComparison.Ordinal),
                "Product code keeps the existing 100-character limit");
        }
        Check(!ArchiveRequests.Product(created, "Motiv test").Target.Contains('#') &&
              ArchiveRequests.Product(created, "Motiv test").Details.Contains("Cod produs: Surub nou", StringComparison.Ordinal),
            "Product archive target hides the internal identifier and labels the product code");
        var duplicateRename = ProductEdit(created);
        duplicateRename.Name = "MAȘINĂ DE GĂURIT CU ACUMULATOR";
        await Rejected(() => repository.UpdateAsync(created, duplicateRename), "Renaming a product to an existing catalogue name is rejected");
        var groupRulesRepository = new DemoProductRepository();
        var existingGroupProduct = await groupRulesRepository.CreateAsync(new ProductInput
            { Name = "Produs categorie existenta", Category = "mĂSURARE", Subcategory = "NIVÉLARE" });
        Check(existingGroupProduct.Category == "Masurare" && existingGroupProduct.Subcategory == "Nivelare",
            "Existing category and subcategory are reused without case or diacritic differences");
        try
        {
            await groupRulesRepository.CreateCategoryAsync("Categorie complet noua");
            await groupRulesRepository.CreateAsync(new ProductInput
                { Name = "Produs subcategorie duplicata", Category = "Categorie complet noua", Subcategory = "fÍXARE" });
            throw new Exception("Duplicate subcategory accepted in another category");
        }
        catch (ProductOperationException exception)
        {
            Check(exception.Message.Contains("Consumabile", StringComparison.Ordinal),
                "A globally duplicate subcategory is rejected and reports its existing category");
        }
        Check((await new DemoProductRepository().GetProductsAsync()).Count == 12, "Demo changes are isolated to a session");
        var sharedProductStore = new DemoProductStore();
        var sharedWriter = new DemoProductRepository(sharedStore: sharedProductStore);
        await CreateProductAsync(sharedWriter, new ProductInput { Name = "Produs meniu", Category = "Categorie meniu", Subcategory = "Subcategorie meniu" });
        var sharedReader = new DemoProductRepository(sharedStore: sharedProductStore);
        Check((await sharedReader.GetGroupsAsync()).Contains(new ProductGroup("Categorie meniu", "Subcategorie meniu")), "Product menu sees categories created by another repository scope");
        var emptyGroupStore = new DemoProductStore();
        var emptyGroupRepository = new DemoProductRepository(sharedStore: emptyGroupStore);
        await emptyGroupRepository.CreateCategoryAsync("Categorie creata din administrare");
        Check((await emptyGroupRepository.GetGroupsAsync()).Contains(new ProductGroup("Categorie creata din administrare", "")),
            "Category administration creates an empty in-memory category");
        var administeredSubcategory = await emptyGroupRepository.CreateSubcategoryAsync(
            "Categorie creata din administrare", "Subcategorie creata din administrare");
        Check((await emptyGroupRepository.GetGroupsAsync()).Contains(administeredSubcategory),
            "Category administration creates an in-memory subcategory in the selected category");
        await Rejected(() => emptyGroupRepository.CreateCategoryAsync("CATEGORIE CREATĂ DIN ADMINISTRARE"),
            "Category creation rejects normalized duplicates");
        var productToMove = await CreateProductAsync(emptyGroupRepository, new ProductInput
        {
            Name = "Produs pentru grup gol",
            Category = "Categorie pastrata",
            Subcategory = "Subcategorie pastrata"
        });
        var moveProduct = ProductInput.From(productToMove);
        moveProduct.Category = "Categorie destinatie";
        moveProduct.Subcategory = "Subcategorie destinatie";
        moveProduct.Reason = "Verificare pastrare grup";
        await EnsureProductGroupAsync(emptyGroupRepository, moveProduct.Category, moveProduct.Subcategory);
        var movedProduct = await emptyGroupRepository.UpdateAsync(productToMove, moveProduct);
        var groupsAfterMove = await emptyGroupRepository.GetGroupsAsync();
        Check(groupsAfterMove.Contains(new ProductGroup("Categorie pastrata", "Subcategorie pastrata")) &&
              groupsAfterMove.Contains(new ProductGroup("Categorie destinatie", "Subcategorie destinatie")),
            "In-memory catalogue keeps the source group after its last product is moved");
        await emptyGroupRepository.DeleteAsync(movedProduct, "Verificare grup gol");
        var groupsAfterLastDelete = await emptyGroupRepository.GetGroupsAsync();
        Check(groupsAfterLastDelete.Contains(new ProductGroup("Categorie destinatie", "Subcategorie destinatie")),
            "In-memory catalogue keeps a group after its last product is deleted");
        var reusedEmptyGroup = await emptyGroupRepository.CreateAsync(new ProductInput
        {
            Name = "Produs grup refolosit",
            Category = "CATEGORIE PASTRATA",
            Subcategory = "SUBCATEGORIE PASTRATA"
        });
        Check(reusedEmptyGroup.Category == "Categorie pastrata" && reusedEmptyGroup.Subcategory == "Subcategorie pastrata" &&
              (await emptyGroupRepository.GetGroupsAsync()).Count(group =>
                  TextNormalization.SameUniqueValue(group.Category, "Categorie pastrata") &&
                  TextNormalization.SameUniqueValue(group.Subcategory, "Subcategorie pastrata")) == 1,
            "An empty in-memory group is reused without creating case-insensitive duplicates");
        await emptyGroupRepository.RenameCategoryAsync("Categorie pastrata", "Categorie redenumita", "Corectie denumire");
        Check((await emptyGroupRepository.GetProductsAsync()).Single(product => product.Id == reusedEmptyGroup.Id).Category == "Categorie redenumita" &&
              (await emptyGroupRepository.GetGroupsAsync()).Contains(new ProductGroup("Categorie redenumita", "Subcategorie pastrata")),
            "Renaming an in-memory category updates its products and catalogue group");
        var updatedDemoGroup = await emptyGroupRepository.UpdateSubcategoryAsync(
            new ProductGroup("Categorie redenumita", "Subcategorie pastrata"), "Subcategorie redenumita",
            "Categorie destinatie", "Reorganizare catalog");
        Check(updatedDemoGroup == new ProductGroup("Categorie destinatie", "Subcategorie redenumita") &&
              (await emptyGroupRepository.GetProductsAsync()).Single(product => product.Id == reusedEmptyGroup.Id) is var movedDemoProduct &&
              movedDemoProduct.Category == updatedDemoGroup.Category && movedDemoProduct.Subcategory == updatedDemoGroup.Subcategory,
            "Renaming and moving an in-memory subcategory updates all associated products");
        await Rejected(() => emptyGroupRepository.RenameCategoryAsync("Categorie redenumita", "Categorie destinatie", "Duplicat"),
            "Category administration rejects a case-insensitive duplicate");
        var spacingRepository = new DemoProductRepository();
        var spacingProduct = await spacingRepository.CreateAsync(new ProductInput
        {
            Name = "  Produs   cu   spatii  ", Category = "Măsurare", Subcategory = "Nivelare"
        });
        Check(spacingProduct.Name == "Produs cu spatii", "Product creation removes exterior and repeated spaces from its name");
        var spacingProductEdit = ProductInput.From(spacingProduct);
        spacingProductEdit.Name = "  Produs   editat   cu spatii  ";
        spacingProductEdit.Reason = "Verificare normalizare";
        spacingProduct = await spacingRepository.UpdateAsync(spacingProduct, spacingProductEdit);
        Check(spacingProduct.Name == "Produs editat cu spatii", "Product editing removes exterior and repeated spaces from its name");
        await spacingRepository.CreateCategoryAsync("  Categorie   cu   spatii  ");
        await spacingRepository.RenameCategoryAsync("Categorie cu spatii", "  Categorie   editata  ", "Verificare normalizare");
        var spacingSubcategory = await spacingRepository.CreateSubcategoryAsync("Categorie editata", "  Subcategorie   cu   spatii  ");
        spacingSubcategory = await spacingRepository.UpdateSubcategoryAsync(spacingSubcategory,
            "  Subcategorie   editata  ", "Categorie editata", "Verificare normalizare");
        Check(spacingSubcategory == new ProductGroup("Categorie editata", "Subcategorie editata"),
            "Category and subcategory creation and editing remove exterior and repeated spaces");
        await Rejected(() => repository.CreateAsync(new ProductInput()), "Missing fields are rejected");
        await Rejected(() => repository.CreateAsync(new ProductInput { Name = "  ", Category = "A", Subcategory = "B" }), "Whitespace name is rejected by repository");
        await Rejected(() => repository.CreateAsync(new ProductInput { Name = new string('a', 101), Category = "A", Subcategory = "B" }), "Oversize product name is rejected");
        var invalid = ProductEdit(created); invalid.Description = new string('a', 1001);
        await Rejected(() => repository.UpdateAsync(created, invalid), "Oversize description is rejected");
        Check(created.Quantity == 0, "A new product starts with stock 0");
        invalid = ProductInput.From(created); invalid.Description = "Editare nouă";
        await Rejected(() => repository.UpdateAsync(created, invalid), "Product edit requires a reason");
        var metadataWithoutReason = ProductInput.From(created); metadataWithoutReason.Description = "Editare fără motiv";
        await Rejected(() => repository.UpdateAsync(created, metadataWithoutReason), "Every product edit requires a reason");
        invalid.Reason = "Inventariere";
        var updated = await repository.UpdateAsync(created, invalid);
        Check(updated.Quantity == 0 && updated.Version == 1, "Product edit keeps the stock and saves with a new version");
        await Rejected(() => repository.UpdateAsync(created, ProductEdit(created)), "Stale edit cannot overwrite changes");
        await Rejected(() => repository.DeleteAsync(created, "Test automat"), "Stale delete cannot remove changed product");
        var stockedProduct = (await repository.GetProductsAsync()).Single(p => p.Id == 8);
        await Rejected(() => repository.DeleteAsync(stockedProduct, "Test automat"), "Nonzero stock prevents deletion");
        var empty = updated;
        try { ProductRules.CheckDelete(empty, true); throw new Exception("Associated records ignored"); }
        catch (ProductOperationException) { Check(true, "Related stock movements or images prevent deletion"); }
        await Rejected(() => repository.DeleteAsync(empty, " "), "Product deletion requires a reason");
        Check((await repository.GetProductsAsync()).Any(product => product.Id == empty.Id), "Rejected product deletion preserves the object");
        await repository.DeleteAsync(empty, "Test automat");
        Check(!(await repository.GetProductsAsync()).Any(p => p.Id == empty.Id), "Zero-stock product can be deleted");
        await Rejected(() => repository.UpdateAsync(empty, ProductEdit(empty)), "Editing a deleted product is rejected");
        await Rejected(() => repository.DeleteAsync(empty, "Test automat"), "Repeated deletion is rejected");
        var next = await repository.CreateAsync(ProductInput.From(created));
        Check(next.Id > created.Id, "Deleted IDs are not reused");
        var legacy = (await repository.GetProductsAsync()).Single(p => p.Id == 8);
        var legacyEdit = ProductEdit(legacy); legacyEdit.Description = "Descriere corectată";
        Check((await repository.UpdateAsync(legacy, legacyEdit)).Quantity == -2, "Legacy negative stock can be preserved during metadata edits");
        var stockSnapshot = (await repository.GetProductsAsync()).Single(p => p.Id == 8);
        var stockDifferenceEdit = ProductEdit(stockSnapshot); stockDifferenceEdit.Description = "Altă descriere";
        Check((await repository.UpdateAsync(stockSnapshot with { Quantity = 99 }, stockDifferenceEdit)).Quantity == -2,
            "A stock difference in the edit snapshot neither blocks the edit nor overwrites the stored stock");
        ProductRules.CheckCurrent(stockSnapshot, stockSnapshot with { Quantity = 7 });
        await Rejected(() => Task.Run(() => ProductRules.CheckCurrent(stockSnapshot, stockSnapshot with { Name = "Alt cod" })),
            "Concurrency check still rejects a changed product code");
        var imageBytes = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
        Check(ProductImageRules.DetectContentType(imageBytes) == "image/png", "Product image format is detected from file content");
        try { ProductImageRules.DetectContentType("<svg></svg>"u8); throw new Exception("Unsafe image format accepted"); }
        catch (ProductImageException) { Check(true, "Unsupported image formats are rejected"); }
        var demoImages = new DemoProductImageStore();
        await demoImages.SaveAsync(created.Id, new ProductImageData(imageBytes, "application/octet-stream", "test.png"));
        Check((await demoImages.GetAsync(created.Id))?.ContentType == "image/png", "Product image is stored with its detected media type");
        await demoImages.DeleteAsync(created.Id);
        Check(!await demoImages.ExistsAsync(created.Id), "Product image is removed with the product");
        try { await repository.CreateAsync(ProductInput.From(next), cancelled.Token); throw new Exception("Create ignored cancellation"); }
        catch (OperationCanceledException) { Check((await repository.GetProductsAsync()).Count == 13, "Cancelled create does not modify catalogue"); }
        try { await repository.UpdateAsync(next, ProductEdit(next), cancelled.Token); throw new Exception("Update ignored cancellation"); }
        catch (OperationCanceledException) { Check(true, "Cancelled update is respected"); }
        try { await repository.DeleteAsync(next, "Test automat", cancelled.Token); throw new Exception("Delete ignored cancellation"); }
        catch (OperationCanceledException) { Check((await repository.GetProductsAsync()).Contains(next), "Cancelled delete leaves product unchanged"); }
        var stale = next;
        var change = ProductEdit(next); change.Name = "Altă denumire";
        var changed = await repository.UpdateAsync(next, change);
        await repository.UpdateAsync(changed, ProductEdit(next));
        await Rejected(() => repository.UpdateAsync(stale, ProductEdit(stale)), "Version detects a change even when values were restored");
        var unconfigured = new MariaProductRepository(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        await Rejected(() => unconfigured.CreateAsync(ProductInput.From(next)), "A missing database password blocks writes before opening a SQL connection");
        var wrongDatabase = new MariaProductRepository(new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Database:Name"] = "stocesp", ["Database:Password"] = "x" }).Build());
        await Rejected(() => wrongDatabase.CreateAsync(ProductInput.From(next)), "Writes to the original database are rejected before connecting");

        async Task RejectedUser(Func<Task> operation, string message)
        {
            try { await operation(); }
            catch (UserOperationException) { Check(true, message); return; }
            throw new Exception("Expected user rejection: " + message);
        }
        async Task RejectedAccess(Func<Task> operation, string message)
        {
            try { await operation(); }
            catch (AccessDeniedException) { Check(true, message); return; }
            throw new Exception("Expected access rejection: " + message);
        }
        var demoUsers = new DemoUserRepository(new TestAccessControl(true, "administrator-extern"));
        Check((await demoUsers.GetUsersAsync()).Count == 2, "Demo user catalogue starts with administrator and limited user");
        Check((await demoUsers.AuthenticateAsync("administrator.demo", "admin-demo-123")).User?.Role == AccessRoles.Administrator, "Demo administrator can authenticate");
        Check((await demoUsers.AuthenticateAsync("utilizator.demo", "utilizator-demo-123")).User?.Role == AccessRoles.LimitedUser, "Demo product user can authenticate");
        Check((await demoUsers.AuthenticateAsync("utilizator.demo", "parola-gresita")).Status == AuthenticationStatus.InvalidCredentials, "Demo authentication rejects a wrong password");
        var newUser = await demoUsers.CreateAsync(new WebUserInput { Username = "  Ión.Popescu  ", DisplayName = "  Ión   Popéscu  ", Role = AccessRoles.LimitedUser, Password = "parola-demo-123" });
        Check(newUser.Username == "Ion.Popescu" && newUser.DisplayName == "Ion Popescu" && newUser.Role == AccessRoles.LimitedUser && newUser.IsActive, "User values keep letter case and remove diacritics");
        Check((await demoUsers.AuthenticateAsync("ion.popescu", "parola-demo-123")).User?.Id == newUser.Id, "New demo user can authenticate");
        await RejectedUser(() => demoUsers.CreateAsync(new WebUserInput { Username = "ION.POPESCU", DisplayName = "Duplicat", Role = AccessRoles.LimitedUser, Password = "parola-demo-123" }), "Usernames are unique without case sensitivity");
        await RejectedUser(() => demoUsers.CreateAsync(new WebUserInput { Username = "ab", DisplayName = "Invalid", Role = AccessRoles.LimitedUser, Password = "parola-demo-123" }), "Short username is rejected");
        await RejectedUser(() => demoUsers.CreateAsync(new WebUserInput { Username = "user-valid", DisplayName = "Invalid", Role = AccessRoles.LimitedUser, Password = "scurta" }), "Short password is rejected");
        await RejectedUser(() => demoUsers.CreateAsync(new WebUserInput { Username = "user-sapte", DisplayName = "Invalid", Role = AccessRoles.LimitedUser, Password = "1234567" }), "A 7-character password is rejected");
        var eightCharUser = await demoUsers.CreateAsync(new WebUserInput { Username = "user-opt", DisplayName = "Opt Caractere", Role = AccessRoles.LimitedUser, Password = "12345678" });
        Check(WebUserInput.MinimumPasswordLength == 8 && (await demoUsers.AuthenticateAsync("user-opt", "12345678")).User?.Id == eightCharUser.Id, "A password of 8 characters is accepted (the minimum)");
        Check(new WebUserInput { Password = "abcdefgh", PasswordConfirmation = "abcdefgh" }.PasswordConfirmationError() is null &&
              new WebUserInput { Password = "abcdefgh", PasswordConfirmation = "abcdefgH" }.PasswordConfirmationError() is { } mismatch && mismatch.Contains("nu coincide") &&
              new WebUserInput { Password = "abcdefgh" }.PasswordConfirmationError() is not null && new WebUserInput().PasswordConfirmationError() is null,
            "The password must be repeated identically in the form; an unchanged (empty) password on edit needs no confirmation");
        var userEdit = UserEdit(newUser); userEdit.DisplayName = "  Ion   Popescu   Editat  "; userEdit.IsActive = false;
        var userWithoutReason = WebUserInput.From(newUser); userWithoutReason.DisplayName = "Editare fără motiv";
        await RejectedUser(() => demoUsers.UpdateAsync(newUser, userWithoutReason), "User edits require a reason");
        var inactiveUser = await demoUsers.UpdateAsync(newUser, userEdit);
        Check(!inactiveUser.IsActive && inactiveUser.Version == 1 && inactiveUser.DisplayName == "Ion Popescu Editat",
            "User can be edited, space-normalized and deactivated without changing password");
        Check((await demoUsers.AuthenticateAsync("ion.popescu", "parola-demo-123")).Status == AuthenticationStatus.Inactive, "Inactive demo user receives inactive status");
        await RejectedUser(() => demoUsers.UpdateAsync(newUser, UserEdit(newUser)), "Stale user edit is rejected");
        await RejectedUser(() => demoUsers.DeleteAsync(inactiveUser, "  "), "User deletion requires a reason");
        Check((await demoUsers.GetUsersAsync()).Any(user => user.Id == inactiveUser.Id), "Rejected user deletion preserves the account");
        await demoUsers.DeleteAsync(inactiveUser, "Test automat");
        Check(!(await demoUsers.GetUsersAsync()).Any(user => user.Id == inactiveUser.Id), "User can be deleted");
        var onlyAdmin = (await demoUsers.GetUsersAsync()).Single(user => user.Role == AccessRoles.Administrator);
        var demotion = UserEdit(onlyAdmin); demotion.Role = AccessRoles.LimitedUser;
        await RejectedUser(() => demoUsers.UpdateAsync(onlyAdmin, demotion), "Last active administrator cannot be demoted");
        await RejectedUser(() => demoUsers.DeleteAsync(onlyAdmin, "Test automat"), "Last active administrator cannot be deleted");
        var secondAdmin = await demoUsers.CreateAsync(new WebUserInput { Username = "admin.doi", DisplayName = "Administrator Doi", Role = AccessRoles.Administrator, Password = "parola-admin-123" });
        var demoted = await demoUsers.UpdateAsync(onlyAdmin, demotion);
        Check(demoted.Role == AccessRoles.LimitedUser && secondAdmin.Role == AccessRoles.Administrator, "Administrator can be demoted when another active administrator remains");
        var selfUsers = new DemoUserRepository(new TestAccessControl(true, "administrator.demo"));
        var self = (await selfUsers.GetUsersAsync()).Single(user => user.Username == "administrator.demo");
        var selfDemotion = UserEdit(self); selfDemotion.Role = AccessRoles.LimitedUser;
        await RejectedUser(() => selfUsers.UpdateAsync(self, selfDemotion), "Administrator cannot remove own administrator access");
        await RejectedUser(() => selfUsers.DeleteAsync(self, "Test automat"), "Administrator cannot delete own account");
limitedAccess = new TestAccessControl(false, "utilizator.demo");
        await RejectedAccess(() => new DemoUserRepository(limitedAccess).GetUsersAsync(), "Limited user cannot list managed user accounts");
        var limitedProducts = new DemoProductRepository(limitedAccess);
        Check((await CreateProductAsync(limitedProducts, ProductInput.From(next))).Id == 13, "Limited user can operate products");

auditTrail = new TestAuditTrail();
auditedProducts = new DemoProductRepository(limitedAccess, auditTrail);
        await auditedProducts.CreateCategoryAsync("Test");
        await auditedProducts.CreateSubcategoryAsync("Test", "Audit");
auditedProduct = await auditedProducts.CreateAsync(new ProductInput { Name = "Produs auditat", Category = "Test", Subcategory = "Audit" });
    }
}
