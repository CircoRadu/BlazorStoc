using BlazorStoc.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

var data = await new DemoProductRepository().GetProductsAsync();
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    Console.WriteLine("PASS: " + message);
}
Microsoft.Data.Sqlite.SqliteCommand TestSqliteCommand(Microsoft.Data.Sqlite.SqliteConnection connection, string sql,
    params (string Name, object? Value)[] parameters)
{
    var command = connection.CreateCommand();
    command.CommandText = sql;
    foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    return command;
}
ProductInput ProductEdit(Product product, string reason = "Test automat") { var input = ProductInput.From(product); input.Reason = reason; return input; }
WebUserInput UserEdit(WebUser user, string reason = "Test automat") { var input = WebUserInput.From(user); input.Reason = reason; return input; }
BeneficiaryInput BeneficiaryEdit(Beneficiary beneficiary, string reason = "Test automat") { var input = BeneficiaryInput.From(beneficiary); input.Reason = reason; return input; }

var archiveAccess = new TestAccessControl(true, "archive.admin");
var archiveService = new ArchiveService(archiveAccess);
ArchiveOperation? firstArchiveOperation = null;
await archiveService.ExecuteAsync(ArchiveRequests.Product(
    new Product(901, "Test", "Contract", "Produs arhivat", "Date complete", 0, 7), "Motiv test"),
    (operation, _) => { firstArchiveOperation = operation; return Task.CompletedTask; });
ArchiveOperation? secondArchiveOperation = null;
await archiveService.ExecuteAsync(ArchiveRequests.Beneficiary(
    new Beneficiary(902, "Beneficiar arhivat", "RO12345678", 3), "Motiv test"),
    (operation, _) => { secondArchiveOperation = operation; return Task.CompletedTask; });
Check(firstArchiveOperation is not null && firstArchiveOperation.Id != Guid.Empty &&
      firstArchiveOperation.TimestampUtc.Kind == DateTimeKind.Utc &&
      firstArchiveOperation.ActorUsername == "archive.admin" &&
      firstArchiveOperation.ActorRole == AccessRoles.Administrator &&
      firstArchiveOperation.Request.Snapshot.OriginalId == "901" &&
      firstArchiveOperation.Request.Snapshot.Version == 7,
    "Archive contract supplies operation id, UTC timestamp, operator, original id and version");
Check(secondArchiveOperation is not null && secondArchiveOperation.Id != firstArchiveOperation!.Id,
    "Every archive operation receives a unique identifier");
var protectedUserSnapshot = ArchiveRequests.User(
    new WebUser(903, "archive.user", "Archive User", AccessRoles.LimitedUser, false, 2), "Motiv test",
    [new ArchiveProtectedValue("PasswordHash", "identity-password-hash")]);
Check(!protectedUserSnapshot.Snapshot.DataJson.Contains("password", StringComparison.OrdinalIgnoreCase) &&
      protectedUserSnapshot.Snapshot.ProtectedValues.Single().Hash == "identity-password-hash",
    "Password hashes are isolated from the public archive snapshot");
try
{
    ArchiveSnapshot.Create("Test", "1", 0, new { Password = "clear-text" });
    throw new Exception("Clear password accepted in public archive data");
}
catch (ArchiveContractException)
{
    Check(true, "Archive contract rejects clear password fields from public data");
}

Check(DeleteConfirmationRules.ValidateReason(string.Empty, string.Empty) is not null,
    "Delete confirmation requires an explicit reason choice");
Check(DeleteConfirmationRules.ResolveReason("Produsul", DeleteConfirmationRules.DefaultChoice, string.Empty) ==
      "Produsul nu va mai fi folosit",
    "The default deletion choice generates the object-specific audit reason");
Check(DeleteConfirmationRules.DefaultReason("Observația") == "Observația nu va mai fi folosită",
    "The default deletion reason agrees in gender with feminine subjects like Observația");
Check(DeleteConfirmationRules.ResolveReason("Beneficiarul", DeleteConfirmationRules.CustomChoice,
          "  Contract incheiat  ") == "Contract incheiat",
    "The custom deletion reason is trimmed and retained for audit");
Check(DeleteConfirmationRules.IsConfirmationValid("  sterge ") &&
      !DeleteConfirmationRules.IsConfirmationValid("Sterge") &&
      !DeleteConfirmationRules.IsConfirmationValid("șterge") &&
      !DeleteConfirmationRules.IsConfirmationValid("sterge acum"),
    "Final deletion confirmation accepts only the exact case-sensitive word sterge");

Check(data.Count == 12, "Demonstration catalogue has 12 fictional products");
Check(ProductSearch.Filter(data, "  POLIZOR  ", "name", "", "").Single().Id == 2, "Search is case-insensitive and trims spaces");
Check(!ProductSearch.Filter(data, "mandrină", "name", "", "").Any(), "Name-only search excludes description matches");
Check(ProductSearch.Filter(data, "mandrină", "description", "", "").Single().Id == 1, "Description-only search works with Romanian text");
Check(ProductSearch.Filter(data, "", "all", "Măsurare", "zero").Single().Id == 5, "Category and stock filters combine");
Check(ProductSearch.Filter(data, "", "all", "", "negative").Single().Id == 8, "Negative stock is distinct from zero stock");
Check(ProductSearch.Filter(data, "", "all", "", "zero").Count() == 2, "Zero stock classification");
Check(!ProductSearch.Filter(data, "' OR 1=1 --", "all", "", "").Any(), "Search text is handled as literal text");
Check(!ProductSearch.Filter(Array.Empty<Product>(), "", "all", "", "").Any(), "Empty database produces an empty list");
using var cancelled = new CancellationTokenSource();
cancelled.Cancel();
try { await new DemoProductRepository().GetProductsAsync(cancelled.Token); throw new Exception("Cancellation ignored"); }
catch (OperationCanceledException) { Console.WriteLine("PASS: Cancellation is respected"); }

var repository = new DemoProductRepository();
async Task Rejected(Func<Task> operation, string message)
{
    try { await operation(); }
    catch (ProductOperationException) { Check(true, message); return; }
    throw new Exception("Expected rejection: " + message);
}
async Task EnsureProductGroupAsync(IProductRepository productRepository, string category, string subcategory)
{
    var available = await productRepository.GetGroupsAsync();
    if (!available.Any(group => TextNormalization.SameUniqueValue(group.Category, category)))
        await productRepository.CreateCategoryAsync(category);
    available = await productRepository.GetGroupsAsync();
    if (!available.Any(group => TextNormalization.SameUniqueValue(group.Category, category) &&
                                TextNormalization.SameUniqueValue(group.Subcategory, subcategory)))
        await productRepository.CreateSubcategoryAsync(category, subcategory);
}
async Task<Product> CreateProductAsync(IProductRepository productRepository, ProductInput input)
{
    await EnsureProductGroupAsync(productRepository, input.Category, input.Subcategory);
    return await productRepository.CreateAsync(input);
}
var created = await CreateProductAsync(repository, new ProductInput { Name = "  Șurub   nou  ", Category = "Categorie nouă", Subcategory = "Subcategorie nouă", Description = "Țeavă și șaibă" });
Check(created.Id == 13 && created.Name == "Surub nou" && created.Description == "Teava si saiba" && (await repository.GetProductsAsync()).Contains(created), "Create removes diacritics before persistence");
Check((await repository.GetGroupsAsync()).Contains(new ProductGroup("Categorie noua", "Subcategorie noua")), "New category and subcategory are available without diacritics");
var selectionRulesRepository = new DemoProductRepository();
await Rejected(() => selectionRulesRepository.CreateAsync(new ProductInput
    { Name = "Produs fara grup", Category = "Categorie inexistenta", Subcategory = "Subcategorie inexistenta" }),
    "Product creation rejects a category that was not created in administration");
await selectionRulesRepository.CreateCategoryAsync("Categorie fara subcategorie");
await Rejected(() => selectionRulesRepository.CreateAsync(new ProductInput
    { Name = "Produs fara subcategorie", Category = "Categorie fara subcategorie", Subcategory = "Subcategorie inexistenta" }),
    "Product creation rejects a subcategory that was not created in administration");
Check(TextNormalization.SameUniqueValue("Categorie nouă", "cATEGORIE NOUA"), "Uniqueness validation ignores case and diacritics");
Check(TextNormalization.SameUniqueValue("Cod   produs", " Cod produs "), "Uniqueness validation ignores repeated and exterior spaces");
try
{
    await repository.CreateAsync(new ProductInput { Name = "șURUB NOU", Category = "Altă categorie", Subcategory = "Altă subcategorie" });
    throw new Exception("Duplicate product accepted");
}
catch (ProductOperationException exception)
{
    Check(exception.Message.Contains("Categorie noua", StringComparison.Ordinal) && exception.Message.Contains("Subcategorie noua", StringComparison.Ordinal),
        "Duplicate product is rejected globally and reports its category and subcategory");
    Check(exception.Message.Contains("Codul produsului «Surub nou» există deja", StringComparison.Ordinal),
        "Duplicate product code message names the existing code");
}
foreach (var variant in new[] { "  surub   NOU ", "ȘURUB NOU", "Șurub nou" })
    await Rejected(() => repository.CreateAsync(new ProductInput
        { Name = variant, Category = "Categorie nouă", Subcategory = "Subcategorie nouă" }),
        $"Product code «{variant}» is a duplicate despite spacing, case or diacritics");
try
{
    new ProductInput { Name = "   ", Category = "Test", Subcategory = "Test" }.Validated();
    throw new Exception("Empty product code accepted");
}
catch (ProductOperationException exception)
{
    Check(exception.Message.Contains("Completează codul produsului.", StringComparison.Ordinal),
        "A product cannot be saved without a product code");
}
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
Check(existingGroupProduct.Category == "Măsurare" && existingGroupProduct.Subcategory == "Nivelare",
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
await Rejected(() => unconfigured.CreateAsync(ProductInput.From(next)), "Missing database actor blocks writes before opening a SQL connection");
var wrongDatabase = new MariaProductRepository(new Microsoft.Extensions.Configuration.ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?> { ["Database:Name"] = "stocesp", ["Database:ApplicationUserId"] = "1" }).Build());
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
var limitedAccess = new TestAccessControl(false, "utilizator.demo");
await RejectedAccess(() => new DemoUserRepository(limitedAccess).GetUsersAsync(), "Limited user cannot list managed user accounts");
var limitedProducts = new DemoProductRepository(limitedAccess);
Check((await CreateProductAsync(limitedProducts, ProductInput.From(next))).Id == 13, "Limited user can operate products");

var auditTrail = new TestAuditTrail();
var auditedProducts = new DemoProductRepository(limitedAccess, auditTrail);
await auditedProducts.CreateCategoryAsync("Test");
await auditedProducts.CreateSubcategoryAsync("Test", "Audit");
var auditedProduct = await auditedProducts.CreateAsync(new ProductInput { Name = "Produs auditat", Category = "Test", Subcategory = "Audit" });
Check(auditTrail.Entries.Count(entry => entry.EntityType == AuditEntities.Category && entry.Action == AuditActions.Create) == 1 &&
      auditTrail.Entries.Count(entry => entry.EntityType == AuditEntities.Subcategory && entry.Action == AuditActions.Create) == 1 &&
      auditTrail.Entries.Count(entry => entry.EntityType == AuditEntities.Product && entry.Action == AuditActions.Create && entry.EntityId == auditedProduct.Id.ToString()) == 1,
    "Explicit group creation and product creation each record one audit event");
var auditedProductSameGroup = await auditedProducts.CreateAsync(new ProductInput { Name = "Al doilea produs auditat", Category = "test", Subcategory = "audit" });
Check(auditTrail.Entries.Count(entry => entry.EntityType is AuditEntities.Category or AuditEntities.Subcategory) == 2 &&
      auditTrail.Entries.Count(entry => entry.EntityType == AuditEntities.Product && entry.Action == AuditActions.Create && entry.EntityId == auditedProductSameGroup.Id.ToString()) == 1,
    "Reusing an existing group does not duplicate category or subcategory events");
await auditedProducts.DeleteAsync(auditedProduct, "Curățare test");
var auditedUsers = new DemoUserRepository(new TestAccessControl(true, "administrator.demo"), new DemoUserStore(), auditTrail);
var auditedUser = await auditedUsers.CreateAsync(new WebUserInput { Username = "audit.user", DisplayName = "Audit User", Role = AccessRoles.LimitedUser, Password = "secret-demo-123" });
Check(auditTrail.Entries.Count == 6 && auditTrail.Entries.All(entry => entry.ActorUsername.Length > 0), "Product, group and user changes are written once to the audit trail");
Check(auditTrail.Entries.Any(entry => entry.EntityType == AuditEntities.Product && entry.Action == AuditActions.Delete), "Audit trail identifies entity and operation");
Check(auditTrail.Entries.All(entry => !entry.Details.Contains("secret-demo-123", StringComparison.Ordinal)), "Audit trail never records passwords");
var deletedProductEvent = auditTrail.Entries.Single(entry => entry.EntityType == AuditEntities.Product && entry.Action == AuditActions.Delete);
Check(deletedProductEvent.EntityId == auditedProduct.Id.ToString() &&
      deletedProductEvent.Details.Contains(auditedProduct.Name, StringComparison.Ordinal) &&
      deletedProductEvent.Details.Contains(auditedProduct.Category, StringComparison.Ordinal) &&
      deletedProductEvent.Motif == "Curatare test" &&
      deletedProductEvent.ArchiveOperationId is not null,
    "Delete audit details retain object identification and archive operation after removal");

var sessionAuditTrail = new TestAuditTrail();
await AuditRecorder.RecordSessionAsync(sessionAuditTrail, "administrator.demo", AccessRoles.Administrator, true, default);
await AuditRecorder.RecordSessionAsync(sessionAuditTrail, "administrator.demo", AccessRoles.Administrator, false, default);
Check(sessionAuditTrail.Entries.Count == 2 &&
      sessionAuditTrail.Entries.Count(entry => entry.Action == AuditActions.Login) == 1 &&
      sessionAuditTrail.Entries.Count(entry => entry.Action == AuditActions.Logout) == 1,
    "Successful login and confirmed logout are each recorded once");

var productAuditLink = new AuditEvent(Guid.NewGuid(), DateTime.UtcNow, "admin", AccessRoles.Administrator,
    AuditEntities.Product, AuditActions.Create, "#12 · Produs", "Denumire: Produs", EntityId: "12");
Check(AuditNavigation.DisplayTarget(productAuditLink) == "Produs" &&
      AuditNavigation.DisplayTarget(productAuditLink with { Target = "Cod #7" }) == "Cod #7" &&
      AuditNavigation.DisplayTarget(productAuditLink with { Target = "#AB · Cod" }) == "#AB · Cod" &&
      AuditNavigation.DisplayTarget(productAuditLink with { EntityType = AuditEntities.Beneficiary }) == "#12 · Produs",
    "Legacy product audit targets are displayed without the internal identifier");
var beneficiaryAuditLink = productAuditLink with
    { EntityType = AuditEntities.Beneficiary, Action = AuditActions.Edit, EntityId = "7" };
var userAuditLink = productAuditLink with
    { EntityType = AuditEntities.User, Action = AuditActions.Create, EntityId = "3" };
Check(AuditNavigation.EditUrl(productAuditLink) == "/produse?edit=12" &&
      AuditNavigation.EditUrl(beneficiaryAuditLink) == "/beneficiari?edit=7" &&
      AuditNavigation.EditUrl(userAuditLink) == "/utilizatori?edit=3",
    "Product, beneficiary and user audit targets use entity type and stable identifier for navigation");
Check(AuditNavigation.EditUrl(productAuditLink with { Action = AuditActions.Delete }) is null &&
      AuditNavigation.EditUrl(productAuditLink with { EntityType = AuditEntities.Category }) is null &&
      AuditNavigation.EditUrl(productAuditLink with { EntityId = "invalid" }) is null,
    "Deleted objects and entities without edit pages do not receive invalid audit links");

var auditRulesTrail = new TestAuditTrail();
var auditRulesProducts = new DemoProductRepository(limitedAccess, auditRulesTrail);
var auditRulesProduct = await CreateProductAsync(auditRulesProducts, new ProductInput
    { Name = "Produs înainte", Category = "Test", Subcategory = "Reguli", Description = "Descriere înainte" });
var auditRulesInput = ProductInput.From(auditRulesProduct);
auditRulesInput.Name = "Produs după";
auditRulesInput.Description = "Descriere după";
auditRulesInput.Reason = "Inventariere";
var auditRulesUpdated = await auditRulesProducts.UpdateAsync(auditRulesProduct, auditRulesInput);
var productEditEvent = auditRulesTrail.Entries.Single(entry => entry.Action == AuditActions.Edit);
Check(productEditEvent.EntityId == auditRulesUpdated.Id.ToString() && productEditEvent.Target.Contains(auditRulesUpdated.Name, StringComparison.Ordinal),
    "Edit audit target uses the saved object and its stable identifier");
Check(productEditEvent.Target == "Produs dupa" && productEditEvent.EntityId == auditRulesProduct.Id.ToString(),
    "Product audit target shows only the product code while the entity id keeps the internal identifier");
Check(productEditEvent.Details.Contains("Cod produs: Produs inainte → Produs dupa", StringComparison.Ordinal) &&
      productEditEvent.Details.Contains("Descriere: Descriere inainte → Descriere dupa", StringComparison.Ordinal) &&
      !productEditEvent.Details.Contains("Cantitate", StringComparison.Ordinal) &&
      !productEditEvent.Details.Contains("Categorie", StringComparison.Ordinal) && productEditEvent.Motif == "Inventariere",
    "Edit audit details contain only changed before and after values, with the reason stored separately");
var successfulAuditCount = auditRulesTrail.Entries.Count;
try
{
    await auditRulesProducts.CreateAsync(new ProductInput
        { Name = "PRODUS DUPA", Category = "Alta", Subcategory = "Alta" });
    throw new Exception("Duplicate audited product accepted");
}
catch (ProductOperationException)
{
    Check(auditRulesTrail.Entries.Count == successfulAuditCount, "Rejected operations do not produce successful audit events");
}

var sensitiveAuditTrail = new TestAuditTrail();
var sensitiveUsers = new DemoUserRepository(new TestAccessControl(true, "administrator.demo"), new DemoUserStore(), sensitiveAuditTrail);
var sensitiveUser = await sensitiveUsers.CreateAsync(new WebUserInput
    { Username = "audit.sensibil", DisplayName = "Audit Sensibil", Role = AccessRoles.LimitedUser, Password = "secret-initial-123" });
var sensitiveInput = UserEdit(sensitiveUser, "Actualizare cont pentru test");
sensitiveInput.DisplayName = "Audit Actualizat";
sensitiveInput.Password = "secret-schimbat-123";
await sensitiveUsers.UpdateAsync(sensitiveUser, sensitiveInput);
var sensitiveEditEvent = sensitiveAuditTrail.Entries.Single(entry => entry.Action == AuditActions.Edit);
Check(sensitiveEditEvent.Details.Contains("Nume afișat: Audit Sensibil → Audit Actualizat", StringComparison.Ordinal) &&
      !sensitiveEditEvent.Details.Contains("secret", StringComparison.OrdinalIgnoreCase) &&
      !sensitiveEditEvent.Details.Contains("parol", StringComparison.OrdinalIgnoreCase) &&
      sensitiveEditEvent.Motif == "Actualizare cont pentru test",
    "User audit changes exclude passwords and password metadata");

var auditContractPath = Path.Combine(Path.GetTempPath(), $"blazorstoc-audit-{Guid.NewGuid():N}.jsonl");
try
{
    var legacyId = Guid.NewGuid();
    var legacyJson = $$"""{"id":"{{legacyId}}","timestampUtc":"2026-09-16T10:49:17.418572Z","actorUsername":"administrator.demo","actorRole":"Administrator","entityType":"Produs","action":"Modificare","target":"#1 · Produs vechi","details":"Denumire actualizată."}""";
    await File.WriteAllTextAsync(auditContractPath, legacyJson + Environment.NewLine);
    var auditConfiguration = new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["App:AuditPath"] = auditContractPath }).Build();
    var fileAuditTrail = new FileAuditTrail(new TestWebHostEnvironment(Path.GetDirectoryName(auditContractPath)!),
        auditConfiguration, NullLogger<FileAuditTrail>.Instance);

    var legacyEvents = await fileAuditTrail.GetEventsAsync();
    Check(legacyEvents.Count == 1 && legacyEvents[0].Action == AuditActions.Edit &&
          legacyEvents[0].Motif == string.Empty && legacyEvents[0].EntityId == string.Empty,
        "Legacy audit events remain readable and use the current audit contract");
    Check(legacyEvents[0].TimestampUtc.Kind == DateTimeKind.Utc, "Legacy audit timestamps are normalized to UTC");

    await fileAuditTrail.RecordAsync(new AuditWrite("administrator.demo", AccessRoles.Administrator,
        AuditEntities.Product, "Modificare", "#42 · Produs", "Denumire: veche → nouă.", "Corecție", "42"));
    var currentEvents = await fileAuditTrail.GetEventsAsync();
    Check(currentEvents[0].Action == AuditActions.Edit && currentEvents[0].Motif == "Corecție" &&
          currentEvents[0].EntityId == "42" && currentEvents[0].TimestampUtc.Kind == DateTimeKind.Utc,
        "New audit events persist Editare, Motif, entity identifier and UTC timestamp");
}
finally
{
    if (File.Exists(auditContractPath)) File.Delete(auditContractPath);
}

var demoBeneficiaries = new DemoBeneficiaryRepository(limitedAccess, null, auditTrail);
Check((await demoBeneficiaries.GetBeneficiariesAsync()).Count == 3, "Demo beneficiary register is available to limited users");
var beneficiary = await demoBeneficiaries.CreateAsync(new BeneficiaryInput { Name = "  Beneficiár   nou   SRL  ", Cui = "  ro12345678  " });
Check(beneficiary.Name == "Beneficiar nou SRL" && beneficiary.Cui == "ro12345678", "Beneficiary values keep letter case and remove diacritics");
Check(BeneficiarySearch.Filter(await demoBeneficiaries.GetBeneficiariesAsync(), "12345678").Single().Id == beneficiary.Id, "Beneficiaries can be searched by CUI");
try { await demoBeneficiaries.CreateAsync(new BeneficiaryInput { Name = "Duplicat", Cui = "RO12345678" }); throw new Exception("Duplicate CUI accepted"); }
catch (BeneficiaryOperationException) { Check(true, "Duplicate beneficiary CUI is rejected"); }
try { await demoBeneficiaries.CreateAsync(new BeneficiaryInput { Name = "BENEFICIÁR NOU SRL", Cui = "RO87654321" }); throw new Exception("Duplicate beneficiary name accepted"); }
catch (BeneficiaryOperationException exception) { Check(exception.Message.Contains("ro12345678", StringComparison.Ordinal), "Duplicate beneficiary name is rejected and reports its CUI"); }
try { await demoBeneficiaries.CreateAsync(new BeneficiaryInput { Name = "CUI invalid", Cui = "RO-ABC" }); throw new Exception("Invalid CUI accepted"); }
catch (BeneficiaryOperationException) { Check(true, "Invalid beneficiary CUI is rejected"); }
var beneficiaryInput = BeneficiaryEdit(beneficiary); beneficiaryInput.Name = "  Beneficiar   actualizat   SRL  ";
var beneficiaryWithoutReason = BeneficiaryInput.From(beneficiary); beneficiaryWithoutReason.Name = "Fără motiv";
try { await demoBeneficiaries.UpdateAsync(beneficiary, beneficiaryWithoutReason); throw new Exception("Beneficiary edit without reason accepted"); }
catch (BeneficiaryOperationException) { Check(true, "Beneficiary edits require a reason"); }
var updatedBeneficiary = await demoBeneficiaries.UpdateAsync(beneficiary, beneficiaryInput);
Check(updatedBeneficiary.Version == 1 && updatedBeneficiary.Name == "Beneficiar actualizat SRL", "Beneficiary can be edited with optimistic concurrency");
try { await demoBeneficiaries.UpdateAsync(beneficiary, BeneficiaryEdit(beneficiary)); throw new Exception("Stale beneficiary accepted"); }
catch (BeneficiaryOperationException) { Check(true, "Stale beneficiary edit is rejected"); }
try { await demoBeneficiaries.DeleteAsync(updatedBeneficiary, " "); throw new Exception("Beneficiary deletion without reason accepted"); }
catch (BeneficiaryOperationException) { Check(true, "Beneficiary deletion requires a reason"); }
Check((await demoBeneficiaries.GetBeneficiariesAsync()).Any(item => item.Id == updatedBeneficiary.Id), "Rejected beneficiary deletion preserves the object");
await demoBeneficiaries.DeleteAsync(updatedBeneficiary, "Test automat");
Check(!(await demoBeneficiaries.GetBeneficiariesAsync()).Any(item => item.Id == updatedBeneficiary.Id), "Beneficiary can be deleted");
Check(auditTrail.Entries.Count(entry => entry.EntityType == AuditEntities.Beneficiary) == 3, "Beneficiary changes are written to the audit trail");

var sqliteTestRoot = Path.Combine(Path.GetTempPath(), $"blazorstoc-sqlite-{Guid.NewGuid():N}");
Directory.CreateDirectory(sqliteTestRoot);
try
{
    var sqliteConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["App:LocalDatabasePath"] = Path.Combine(sqliteTestRoot, "persistent.db"),
        ["App:ProductImagesPath"] = Path.Combine(sqliteTestRoot, "product-images"),
        ["App:ArchiveFilesPath"] = Path.Combine(sqliteTestRoot, "archive-files"),
        ["App:AuditPath"] = Path.Combine(sqliteTestRoot, "legacy-audit.jsonl")
    }).Build();
    var sqliteEnvironment = new TestWebHostEnvironment(sqliteTestRoot);
    var administrator = new TestAccessControl(true, "administrator.demo");
    var firstStore = new SqliteLocalStore(sqliteEnvironment, sqliteConfiguration, NullLogger<SqliteLocalStore>.Instance);
    await firstStore.InitializeAsync();
    await using (var schemaConnection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={firstStore.DatabasePath}"))
    {
        await schemaConnection.OpenAsync();
        await using var tables = schemaConnection.CreateCommand();
        tables.CommandText = """
            SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN
                ('archive_operations','archive_products','archive_beneficiaries','archive_web_users','archive_relations','archive_files')
            """;
        Check(Convert.ToInt32(await tables.ExecuteScalarAsync()) == 6,
            "SQLite creates the complete archive table family");
        await using var projectTables = schemaConnection.CreateCommand();
        projectTables.CommandText = """
            SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN
                ('projects','project_observations','project_observation_files',
                 'archive_projects','archive_project_observations','archive_project_observation_files')
            """;
        Check(Convert.ToInt32(await projectTables.ExecuteScalarAsync()) == 6,
            "SQLite creates the project, observation and observation-file live and archive tables");
        await using var version = schemaConnection.CreateCommand();
        version.CommandText = "SELECT value FROM app_metadata WHERE key='schema_version'";
        Check((string?)await version.ExecuteScalarAsync() == "6", "Archive schema is versioned with the live SQLite schema");
        await using var indexes = schemaConnection.CreateCommand();
        indexes.CommandText = """
            SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name IN
                ('ix_archive_operations_object','ix_archive_operations_deleted','ix_archive_operations_actor',
                 'ix_archive_products_original','ix_archive_beneficiaries_original','ix_archive_web_users_original',
                 'ix_archive_projects_original','ix_archive_project_observations_original','ix_archive_project_observation_files_original')
            """;
        Check(Convert.ToInt32(await indexes.ExecuteScalarAsync()) == 9,
            "Archive tables index original identity, deletion time and operator");
    }
    Check(ArchiveSchemaRegistry.All.Select(item => item.EntityType).Order()
            .SequenceEqual(new[] { AuditEntities.Beneficiary, AuditEntities.Product, AuditEntities.User,
                AuditEntities.Project, AuditEntities.ProjectObservation, AuditEntities.ProjectObservationFile,
                AuditEntities.StockMovement }.Order()),
        "Every currently deletable entity is registered with an archive table");
    try
    {
        ArchiveSnapshot.Create("TipViitor", "1", 0, new { Name = "Entitate viitoare" });
        throw new Exception("Unregistered future entity accepted by archive contract");
    }
    catch (ArchiveContractException)
    {
        Check(true, "Future deletable entities require archive schema registration before use");
    }

    var firstProducts = new SqliteProductRepository(firstStore, administrator);
    var persistentProduct = await CreateProductAsync(firstProducts, new ProductInput
    {
        Name = "Produs persistent",
        Category = "Categorie persistenta",
        Subcategory = "Subcategorie persistenta",
        Description = "Valoare initiala"
    });
    var persistentEdit = ProductInput.From(persistentProduct);
    persistentEdit.Description = "Valoare salvata dupa repornire";
    persistentEdit.Reason = "Verificare persistenta";
    persistentProduct = await firstProducts.UpdateAsync(persistentProduct, persistentEdit);
    var stockProduct = await CreateProductAsync(firstProducts, new ProductInput
        { Name = "Produs stoc paralel", Category = "Categorie persistenta", Subcategory = "Subcategorie persistenta" });
    Check(stockProduct.Quantity == 0, "SQLite product creation starts with stock 0");
    await using (var stockConnection = await firstStore.OpenConnectionAsync())
    {
        await using var setStock = TestSqliteCommand(stockConnection, "UPDATE products SET quantity=7 WHERE id=@id", ("@id", stockProduct.Id));
        await setStock.ExecuteNonQueryAsync();
    }
    var stockPreservingEdit = ProductInput.From(stockProduct);
    stockPreservingEdit.Description = "Editare cu stoc modificat in paralel";
    stockPreservingEdit.Reason = "Verificare stoc";
    var stockPreserved = await firstProducts.UpdateAsync(stockProduct, stockPreservingEdit);
    Check(stockPreserved.Quantity == 7 && (await firstProducts.GetProductsAsync()).Single(p => p.Id == stockProduct.Id).Quantity == 7,
        "SQLite product edit does not overwrite a stock changed after the form was opened");
    await Rejected(() => firstProducts.DeleteAsync(stockPreserved with { Quantity = 0 }, "Test automat"), "SQLite deletion is blocked by the current stock, not by the stale snapshot");
    await using (var stockResetConnection = await firstStore.OpenConnectionAsync())
    {
        await using var resetStock = TestSqliteCommand(stockResetConnection, "UPDATE products SET quantity=0 WHERE id=@id", ("@id", stockProduct.Id));
        await resetStock.ExecuteNonQueryAsync();
    }

    var firstBeneficiaries = new SqliteBeneficiaryRepository(firstStore, administrator);
    var persistentBeneficiary = await firstBeneficiaries.CreateAsync(new BeneficiaryInput
        { Name = "Beneficiar persistent", Cui = "RO19999999" });
    var firstUsers = new SqliteUserRepository(firstStore, administrator);
    var persistentUser = await firstUsers.CreateAsync(new WebUserInput
    {
        Username = "persistent.user",
        DisplayName = "Utilizator persistent",
        Role = AccessRoles.LimitedUser,
        Password = "parola-persistenta-123"
    });
    var firstImages = new SqliteProductImageStore(firstStore);
    await firstImages.SaveAsync(persistentProduct.Id, new ProductImageData(imageBytes, "image/png", "persistent.png"));

    var firstProjects = new SqliteProjectRepository(firstStore, administrator);
    var persistentProject = await firstProjects.CreateAsync(new ProjectInput
        { BeneficiaryId = persistentBeneficiary.Id, Name = "  Proiect   persistent  ", Observations = "Observatii generale" });
    Check(persistentProject.Name == "Proiect persistent" && persistentProject.Version == 0, "SQLite project creation normalizes the name and starts at version 0");
    var firstProjectFiles = new SqliteProjectFileStore(firstStore);
    var persistentObservation = await firstProjects.CreateObservationAsync(persistentProject.Id,
        new ProjectObservationInput { Name = "Observatie initiala", Content = "Continut initial" }, "operator.persistent");
    var persistentFile = await firstProjectFiles.SaveAsync(persistentObservation.Id, "poza produs.png", "image/png", imageBytes, "operator.persistent");
    Check(persistentFile.OriginalName == "poza produs.png" && persistentFile.Sha256.Length == 64 && persistentFile.ContentType == "image/png",
        "SQLite observation file upload sniffs the content type and stores a SHA-256 hash");

    var secondStore = new SqliteLocalStore(sqliteEnvironment, sqliteConfiguration, NullLogger<SqliteLocalStore>.Instance);
    await secondStore.InitializeAsync();
    var secondImages = new SqliteProductImageStore(secondStore);
    var secondProducts = new SqliteProductRepository(secondStore, administrator, imageStore: secondImages);
    var reloadedProduct = (await secondProducts.GetProductsAsync()).Single(product => product.Id == persistentProduct.Id);
    Check(reloadedProduct.Description == "Valoare salvata dupa repornire" && reloadedProduct.Version == 1,
        "SQLite product edits survive application restart");
    Check((await secondProducts.GetGroupsAsync()).Contains(new ProductGroup("Categorie persistenta", "Subcategorie persistenta")),
        "SQLite categories and subcategories survive application restart");
    var emptyGroupSuffix = Guid.NewGuid().ToString("N")[..8];
    var sqliteProductToMove = await CreateProductAsync(secondProducts, new ProductInput
    {
        Name = $"Produs grup gol {emptyGroupSuffix}",
        Category = $"Categorie sursa {emptyGroupSuffix}",
        Subcategory = $"Subcategorie sursa {emptyGroupSuffix}"
    });
    var sqliteMove = ProductInput.From(sqliteProductToMove);
    sqliteMove.Category = $"Categorie destinatie {emptyGroupSuffix}";
    sqliteMove.Subcategory = $"Subcategorie destinatie {emptyGroupSuffix}";
    sqliteMove.Reason = "Verificare pastrare grup SQLite";
    await EnsureProductGroupAsync(secondProducts, sqliteMove.Category, sqliteMove.Subcategory);
    var sqliteMovedProduct = await secondProducts.UpdateAsync(sqliteProductToMove, sqliteMove);
    var sqliteGroupsAfterMove = await secondProducts.GetGroupsAsync();
    Check(sqliteGroupsAfterMove.Contains(new ProductGroup($"Categorie sursa {emptyGroupSuffix}", $"Subcategorie sursa {emptyGroupSuffix}")) &&
          sqliteGroupsAfterMove.Contains(new ProductGroup($"Categorie destinatie {emptyGroupSuffix}", $"Subcategorie destinatie {emptyGroupSuffix}")),
        "SQLite keeps the source category and subcategory after moving their last product");
    await secondProducts.DeleteAsync(sqliteMovedProduct, "Verificare grup SQLite gol");
    Check((await secondProducts.GetGroupsAsync()).Contains(
            new ProductGroup($"Categorie destinatie {emptyGroupSuffix}", $"Subcategorie destinatie {emptyGroupSuffix}")),
        "SQLite keeps the category and subcategory after deleting their last product");
    var managementSuffix = Guid.NewGuid().ToString("N")[..8];
    var managedProduct = await CreateProductAsync(secondProducts, new ProductInput
    {
        Name = $"Produs administrare {managementSuffix}", Category = $"Categorie administrare {managementSuffix}",
        Subcategory = $"Subcategorie administrare {managementSuffix}"
    });
    var destinationProduct = await CreateProductAsync(secondProducts, new ProductInput
    {
        Name = $"Produs destinatie {managementSuffix}", Category = $"Categorie tinta {managementSuffix}",
        Subcategory = $"Subcategorie tinta {managementSuffix}"
    });
    await secondProducts.RenameCategoryAsync(managedProduct.Category, $"Categorie redenumita {managementSuffix}", "Corectie categorie");
    var renamedCategoryProduct = (await secondProducts.GetProductsAsync()).Single(product => product.Id == managedProduct.Id);
    Check(renamedCategoryProduct.Category == $"Categorie redenumita {managementSuffix}",
        "SQLite category rename updates every associated product through the catalogue relation");
    await Rejected(() => secondProducts.RenameCategoryAsync(renamedCategoryProduct.Category, destinationProduct.Category, "Duplicat"),
        "SQLite category rename rejects an existing normalized name");
    var managedGroup = await secondProducts.UpdateSubcategoryAsync(
        new ProductGroup(renamedCategoryProduct.Category, renamedCategoryProduct.Subcategory),
        $"Subcategorie redenumita {managementSuffix}", destinationProduct.Category, "Mutare subcategorie");
    var movedManagedProduct = (await secondProducts.GetProductsAsync()).Single(product => product.Id == managedProduct.Id);
    Check(movedManagedProduct.Category == managedGroup.Category && movedManagedProduct.Subcategory == managedGroup.Subcategory &&
          (await secondProducts.GetGroupsAsync()).Contains(new ProductGroup($"Categorie redenumita {managementSuffix}", "")),
        "SQLite subcategory move retains the empty source category and moves associated products");
    await Rejected(() => secondProducts.UpdateSubcategoryAsync(managedGroup, destinationProduct.Subcategory,
        destinationProduct.Category, "Duplicat"), "SQLite subcategory update rejects an existing normalized name");
    var managementEvents = await new SqliteAuditTrail(secondStore).GetEventsAsync();
    Check(managementEvents.Any(entry => entry.EntityType == AuditEntities.Category && entry.Action == AuditActions.Edit &&
                                      entry.Target == $"Categorie redenumita {managementSuffix}" && entry.Motif == "Corectie categorie") &&
          managementEvents.Any(entry => entry.EntityType == AuditEntities.Subcategory && entry.Action == AuditActions.Edit &&
                                      entry.Target.Contains($"Subcategorie redenumita {managementSuffix}", StringComparison.Ordinal) &&
                                      entry.Details.Contains("Categorie:", StringComparison.Ordinal) && entry.Motif == "Mutare subcategorie"),
        "Category and subcategory administration writes before/after audit details and reasons");
    var createdCategoryName = $"Categorie creare {managementSuffix}";
    var createdSubcategoryName = $"Subcategorie creare {managementSuffix}";
    await secondProducts.CreateCategoryAsync(createdCategoryName);
    var createdManagedGroup = await secondProducts.CreateSubcategoryAsync(createdCategoryName, createdSubcategoryName);
    Check((await secondProducts.GetGroupsAsync()).Contains(createdManagedGroup),
        "SQLite category administration creates a category and its subcategory without a product");
    var creationEvents = await new SqliteAuditTrail(secondStore).GetEventsAsync();
    Check(creationEvents.Any(entry => entry.EntityType == AuditEntities.Category && entry.Action == AuditActions.Create &&
                                    entry.Target == createdCategoryName) &&
          creationEvents.Any(entry => entry.EntityType == AuditEntities.Subcategory && entry.Action == AuditActions.Create &&
                                    entry.Target == $"{createdCategoryName} / {createdSubcategoryName}"),
        "SQLite category and subcategory creation is audited independently");
    Check((await new SqliteBeneficiaryRepository(secondStore, administrator).GetBeneficiariesAsync())
            .Any(item => item.Id == persistentBeneficiary.Id && item.Name == persistentBeneficiary.Name),
        "SQLite beneficiaries survive application restart");
    Check((await new SqliteUserRepository(secondStore, administrator)
            .AuthenticateAsync(persistentUser.Username, "parola-persistenta-123")).User?.Id == persistentUser.Id,
        "SQLite users and password hashes survive application restart");
    Check((await secondImages.GetAsync(persistentProduct.Id))?.Content.SequenceEqual(imageBytes) == true,
        "Product image files and SQLite metadata survive application restart");

    var persistentEvents = await new SqliteAuditTrail(secondStore).GetEventsAsync();
    Check(persistentEvents.Any(entry => entry.EntityType == AuditEntities.Product && entry.Action == AuditActions.Edit &&
                                      entry.EntityId == persistentProduct.Id.ToString() && entry.Target.Contains(persistentProduct.Name, StringComparison.Ordinal)),
        "SQLite audit stays aligned with the persisted product after restart");

    var rejectedProductEdit = ProductInput.From(reloadedProduct);
    rejectedProductEdit.Description = "Valoare care nu trebuie salvata";
    rejectedProductEdit.Reason = " ";
    var auditCountBeforeRejectedProductEdit = persistentEvents.Count;
    try
    {
        await secondProducts.UpdateAsync(reloadedProduct, rejectedProductEdit);
        throw new Exception("SQLite product edit without reason accepted");
    }
    catch (ProductOperationException)
    {
        Check(rejectedProductEdit.Description == "Valoare care nu trebuie salvata" &&
              (await secondProducts.GetProductsAsync()).Single(item => item.Id == reloadedProduct.Id).Description == reloadedProduct.Description &&
              (await new SqliteAuditTrail(secondStore).GetEventsAsync()).Count == auditCountBeforeRejectedProductEdit,
            "Rejected product edit preserves form values, stored object and audit history");
    }

    var concurrentSuffix = Guid.NewGuid().ToString("N")[..8];
    await EnsureProductGroupAsync(secondProducts, $"Categorie A {concurrentSuffix}", $"Subcategorie A {concurrentSuffix}");
    await EnsureProductGroupAsync(secondProducts, $"Categorie B {concurrentSuffix}", $"Subcategorie B {concurrentSuffix}");
    var concurrentWrites = await Task.WhenAll(
        new SqliteProductRepository(firstStore, administrator).CreateAsync(new ProductInput
        {
            Name = $"Produs concurent A {concurrentSuffix}", Category = $"Categorie A {concurrentSuffix}",
            Subcategory = $"Subcategorie A {concurrentSuffix}"
        }),
        new SqliteProductRepository(secondStore, administrator).CreateAsync(new ProductInput
        {
            Name = $"Produs concurent B {concurrentSuffix}", Category = $"Categorie B {concurrentSuffix}",
            Subcategory = $"Subcategorie B {concurrentSuffix}"
        }));
    var afterConcurrentWrites = await secondProducts.GetProductsAsync();
    var sameCodeSuffix = Guid.NewGuid().ToString("N")[..8];
    async Task<Product?> TryCreateSameCodeAsync(SqliteLocalStore targetStore, string code)
    {
        try
        {
            return await new SqliteProductRepository(targetStore, administrator).CreateAsync(new ProductInput
            {
                Name = code, Category = $"Categorie A {concurrentSuffix}",
                Subcategory = $"Subcategorie A {concurrentSuffix}"
            });
        }
        catch (ProductOperationException exception) when (exception.Message.StartsWith("Codul produsului", StringComparison.Ordinal))
        {
            return null;
        }
    }
    var sameCodeWrites = await Task.WhenAll(
        TryCreateSameCodeAsync(firstStore, $"Cod concurent {sameCodeSuffix}"),
        TryCreateSameCodeAsync(secondStore, $"  COD   concurent {sameCodeSuffix.ToUpperInvariant()} "));
    var sameCodeKey = TextNormalization.UniquenessKey($"Cod concurent {sameCodeSuffix}");
    Check(sameCodeWrites.Count(result => result is not null) == 1 &&
          (await secondProducts.GetProductsAsync()).Count(product =>
              TextNormalization.UniquenessKey(product.Name) == sameCodeKey) == 1,
        "Two concurrent SQLite sessions cannot save the same normalized product code");
    Check(concurrentWrites.All(createdItem => afterConcurrentWrites.Any(saved => saved.Id == createdItem.Id)),
        "Two repository sessions persist concurrent writes without losing products");
    var eventsAfterConcurrentWrites = await new SqliteAuditTrail(secondStore).GetEventsAsync();
    Check(concurrentWrites.All(createdItem => eventsAfterConcurrentWrites.Any(entry =>
            entry.EntityType == AuditEntities.Product && entry.Action == AuditActions.Create &&
            entry.EntityId == createdItem.Id.ToString())),
        "Concurrent SQLite writes retain their matching audit events");

    var liveImagePath = Directory.EnumerateFiles(secondStore.ProductImagesPath,
        $"product-{persistentProduct.Id}-*").Single();
    await secondProducts.DeleteAsync(reloadedProduct, "Curatare test persistent");
    Check(!(await secondProducts.GetProductsAsync()).Any(product => product.Id == persistentProduct.Id) &&
          (await secondProducts.GetGroupsAsync()).Contains(new ProductGroup("Categorie persistenta", "Subcategorie persistenta")),
        "SQLite deletes products while retaining intentionally empty catalogue groups");
    var archivedImagePath = Directory.EnumerateFiles(secondStore.ArchiveFilesPath, "persistent.png",
        SearchOption.AllDirectories).Single();
    Check(!File.Exists(liveImagePath) && File.Exists(archivedImagePath) &&
          (await File.ReadAllBytesAsync(archivedImagePath)).SequenceEqual(imageBytes),
        "Product deletion moves the verified image copy outside the live file structure");
    await using (var archiveConnection = await secondStore.OpenConnectionAsync())
    {
        await using var archiveCheck = TestSqliteCommand(archiveConnection, """
            SELECT COUNT(*)
            FROM archive_products p
            INNER JOIN archive_operations o ON o.id=p.archive_id
            INNER JOIN archive_files f ON f.archive_id=o.id
            INNER JOIN audit_events a ON a.archive_operation_id=o.id
            WHERE p.original_id=@id AND a.action=@action
            """, ("@id", persistentProduct.Id), ("@action", AuditActions.Delete));
        Check(Convert.ToInt32(await archiveCheck.ExecuteScalarAsync()) == 1,
            "Product, file metadata and delete audit are committed under one archive operation");
    }

    try
    {
        await firstBeneficiaries.CreateAsync(new BeneficiaryInput { Name = "Alt beneficiar", Cui = persistentBeneficiary.Cui });
        throw new Exception("Duplicate persistent beneficiary CUI accepted");
    }
    catch (BeneficiaryOperationException exception)
    {
        Check(exception.Message.Contains(persistentBeneficiary.Name, StringComparison.Ordinal),
            "SQLite duplicate CUI validation reports the existing beneficiary name");
    }

    var secondBeneficiaries = new SqliteBeneficiaryRepository(secondStore, administrator);
    var reloadedBeneficiary = (await secondBeneficiaries.GetBeneficiariesAsync()).Single(item => item.Id == persistentBeneficiary.Id);
    var rejectedBeneficiaryEdit = BeneficiaryInput.From(reloadedBeneficiary);
    rejectedBeneficiaryEdit.Name = "Beneficiar nesalvat";
    rejectedBeneficiaryEdit.Reason = " ";
    var auditCountBeforeRejectedBeneficiaryEdit = (await new SqliteAuditTrail(secondStore).GetEventsAsync()).Count;
    try
    {
        await secondBeneficiaries.UpdateAsync(reloadedBeneficiary, rejectedBeneficiaryEdit);
        throw new Exception("SQLite beneficiary edit without reason accepted");
    }
    catch (BeneficiaryOperationException)
    {
        Check(rejectedBeneficiaryEdit.Name == "Beneficiar nesalvat" &&
              (await secondBeneficiaries.GetBeneficiariesAsync()).Single(item => item.Id == reloadedBeneficiary.Id).Name == reloadedBeneficiary.Name &&
              (await new SqliteAuditTrail(secondStore).GetEventsAsync()).Count == auditCountBeforeRejectedBeneficiaryEdit,
            "Rejected beneficiary edit preserves form values, stored object and audit history");
    }
    var beneficiaryUpdateInput = BeneficiaryInput.From(reloadedBeneficiary);
    beneficiaryUpdateInput.Name = "Beneficiar persistent editat";
    beneficiaryUpdateInput.Reason = "Corectie denumire beneficiar";
    var editedBeneficiary = await secondBeneficiaries.UpdateAsync(reloadedBeneficiary, beneficiaryUpdateInput);
    try
    {
        await secondBeneficiaries.DeleteAsync(editedBeneficiary, " ");
        throw new Exception("SQLite beneficiary delete without reason accepted");
    }
    catch (BeneficiaryOperationException)
    {
        Check((await secondBeneficiaries.GetBeneficiariesAsync()).Any(item => item.Id == editedBeneficiary.Id),
            "Rejected beneficiary delete preserves the stored object");
    }

    var secondProjects = new SqliteProjectRepository(secondStore, administrator);
    var secondProjectFiles = new SqliteProjectFileStore(secondStore);
    var reloadedProject = await secondProjects.GetAsync(persistentProject.Id);
    Check(reloadedProject is not null && reloadedProject.Name == "Proiect persistent" && reloadedProject.BeneficiaryId == editedBeneficiary.Id,
        "SQLite project survives application restart");
    var reloadedObservations = await secondProjects.GetObservationsAsync(persistentProject.Id);
    Check(reloadedObservations.Single().Id == persistentObservation.Id, "SQLite project observation survives application restart");
    var reloadedFiles = await secondProjectFiles.GetFilesAsync(persistentObservation.Id);
    Check(reloadedFiles.Single().Id == persistentFile.Id, "SQLite observation file metadata survives application restart");
    var reloadedFileContent = await secondProjectFiles.GetContentAsync(persistentFile.Id);
    Check(reloadedFileContent is not null && reloadedFileContent.Content.SequenceEqual(imageBytes), "SQLite observation file content survives application restart");

    try
    {
        await secondProjects.CreateAsync(new ProjectInput { BeneficiaryId = editedBeneficiary.Id, Name = "  PROIECT   persistent  " });
        throw new Exception("Duplicate project name accepted for the same beneficiary");
    }
    catch (ProjectOperationException exception)
    {
        Check(exception.Message.Contains("Proiect persistent", StringComparison.Ordinal) && exception.Message.Contains(editedBeneficiary.Name, StringComparison.Ordinal),
            "SQLite duplicate project name reports the beneficiary and the existing project");
    }
    var otherBeneficiaryProject = await secondProjects.CreateAsync(new ProjectInput { BeneficiaryId = 1, Name = "Proiect persistent" });
    Check(otherBeneficiaryProject.Name == "Proiect persistent", "The same project name is accepted for a different beneficiary");

    var projectConcurrencySuffix = Guid.NewGuid().ToString("N")[..8];
    async Task<Project?> TryCreateSameProjectNameAsync(SqliteLocalStore store, string name)
    {
        try { return await new SqliteProjectRepository(store, administrator).CreateAsync(new ProjectInput { BeneficiaryId = 2, Name = name }); }
        catch (ProjectOperationException) { return null; }
    }
    var sameProjectNameWrites = await Task.WhenAll(
        TryCreateSameProjectNameAsync(firstStore, $"Proiect concurent {projectConcurrencySuffix}"),
        TryCreateSameProjectNameAsync(secondStore, $"  proiect   CONCURENT {projectConcurrencySuffix} "));
    Check(sameProjectNameWrites.Count(result => result is not null) == 1,
        "Two concurrent SQLite sessions cannot create the same project name for the same beneficiary");

    var projectEditInput = ProjectInput.From(reloadedProject!);
    projectEditInput.Name = "Proiect fara motiv";
    try { await secondProjects.UpdateAsync(reloadedProject!, projectEditInput); throw new Exception("SQLite project edit without reason accepted"); }
    catch (ProjectOperationException) { Check(true, "SQLite project edits require a reason"); }
    projectEditInput.Reason = "Actualizare observatii proiect";
    var editedProjectRecord = await secondProjects.UpdateAsync(reloadedProject!, projectEditInput);
    Check(editedProjectRecord.Version == 1 && editedProjectRecord.Name == "Proiect fara motiv", "SQLite project edit increments the version");
    try { await secondProjects.UpdateAsync(reloadedProject!, projectEditInput); throw new Exception("Stale SQLite project accepted"); }
    catch (ProjectOperationException) { Check(true, "Stale SQLite project edit is rejected"); }

    var observationEditInput = ProjectObservationInput.From(persistentObservation);
    observationEditInput.Content = "Continut fara motiv";
    try { await secondProjects.UpdateObservationAsync(persistentObservation, observationEditInput); throw new Exception("SQLite observation edit without reason accepted"); }
    catch (ProjectOperationException) { Check(true, "SQLite observation edits require a reason"); }
    observationEditInput.Reason = "Completare continut observatie";
    var editedObservationRecord = await secondProjects.UpdateObservationAsync(persistentObservation, observationEditInput);
    Check(editedObservationRecord.Version == 1 && editedObservationRecord.Content == "Continut fara motiv", "SQLite observation edit increments the version");

    await secondProjectFiles.DeleteAsync(persistentFile.Id, "Fisier inlocuit");
    Check((await secondProjectFiles.GetFilesAsync(persistentObservation.Id)).Count == 0, "SQLite observation file removal archives and clears the live file");
    Check(await secondProjectFiles.GetContentAsync(persistentFile.Id) is null, "An archived observation file is no longer served live");

    try
    {
        await secondBeneficiaries.DeleteAsync(editedBeneficiary, "Curatare beneficiar persistent");
        throw new Exception("Beneficiary with a live project was deleted");
    }
    catch (BeneficiaryOperationException exception)
    {
        Check(exception.Message.Contains("proiect", StringComparison.OrdinalIgnoreCase),
            "SQLite beneficiary deletion is blocked while a live project exists");
    }
    var secondObservation = await secondProjects.CreateObservationAsync(editedProjectRecord.Id,
        new ProjectObservationInput { Name = "Observatie suplimentara", Content = "Text suplimentar" }, "operator.persistent");
    await secondProjectFiles.SaveAsync(secondObservation.Id, "nota.txt", "text/plain", "Continut text simplu"u8.ToArray(), "operator.persistent");
    await secondProjects.DeleteAsync(editedProjectRecord, "Curatare proiect persistent");
    Check(await secondProjects.GetAsync(editedProjectRecord.Id) is null, "Deleting a project removes it and its observations/files from the live tables");
    Check((await secondProjectFiles.GetFilesAsync(secondObservation.Id)).Count == 0, "Deleting a project archives its remaining observation files");
    await secondProjects.DeleteAsync(otherBeneficiaryProject, "Curatare proiect suplimentar");

    await secondBeneficiaries.DeleteAsync(editedBeneficiary, "Curatare beneficiar persistent");

    var secondUsers = new SqliteUserRepository(secondStore, administrator);
    var reloadedUser = (await secondUsers.GetUsersAsync()).Single(item => item.Id == persistentUser.Id);
    var rejectedUserEdit = WebUserInput.From(reloadedUser);
    rejectedUserEdit.DisplayName = "Utilizator nesalvat";
    rejectedUserEdit.Reason = " ";
    var auditCountBeforeRejectedUserEdit = (await new SqliteAuditTrail(secondStore).GetEventsAsync()).Count;
    try
    {
        await secondUsers.UpdateAsync(reloadedUser, rejectedUserEdit);
        throw new Exception("SQLite user edit without reason accepted");
    }
    catch (UserOperationException)
    {
        Check(rejectedUserEdit.DisplayName == "Utilizator nesalvat" &&
              (await secondUsers.GetUsersAsync()).Single(item => item.Id == reloadedUser.Id).DisplayName == reloadedUser.DisplayName &&
              (await new SqliteAuditTrail(secondStore).GetEventsAsync()).Count == auditCountBeforeRejectedUserEdit,
            "Rejected user edit preserves form values, stored object and audit history");
    }
    var userUpdateInput = WebUserInput.From(reloadedUser);
    userUpdateInput.DisplayName = "Utilizator persistent editat";
    userUpdateInput.Password = "secret-nou-care-nu-se-jurnalizeaza";
    userUpdateInput.Reason = "Corectie nume utilizator";
    var editedUser = await secondUsers.UpdateAsync(reloadedUser, userUpdateInput);
    Check((await secondUsers.AuthenticateAsync(editedUser.Username, "secret-nou-care-nu-se-jurnalizeaza")).User?.Id == editedUser.Id,
        "SQLite edited user remains authenticatable with the new password");
    try
    {
        await secondUsers.DeleteAsync(editedUser, " ");
        throw new Exception("SQLite user delete without reason accepted");
    }
    catch (UserOperationException)
    {
        Check((await secondUsers.GetUsersAsync()).Any(item => item.Id == editedUser.Id),
            "Rejected user delete preserves the stored object");
    }
    await secondUsers.DeleteAsync(editedUser, "Curatare utilizator persistent");

    await using (var archivedEntitiesConnection = await secondStore.OpenConnectionAsync())
    {
        await using var archivedEntities = TestSqliteCommand(archivedEntitiesConnection, """
            SELECT
                (SELECT COUNT(*) FROM archive_beneficiaries WHERE original_id=@beneficiary),
                (SELECT COUNT(*) FROM archive_web_users WHERE original_id=@user AND length(password_hash)>0),
                (SELECT COUNT(*) FROM archive_operations WHERE original_id=@userText AND protected_data_json LIKE '%PasswordHash%')
            """, ("@beneficiary", persistentBeneficiary.Id), ("@user", persistentUser.Id),
            ("@userText", persistentUser.Id.ToString()));
        await using var reader = await archivedEntities.ExecuteReaderAsync();
        Check(await reader.ReadAsync() && reader.GetInt32(0) == 1 && reader.GetInt32(1) == 1 && reader.GetInt32(2) == 1,
            "Beneficiary and user archives retain restoration data while isolating the password hash");
    }

    var concurrentDeleteProduct = await CreateProductAsync(secondProducts, new ProductInput
    {
        Name = $"Produs stergere concurenta {concurrentSuffix}", Category = $"Categorie concurenta {concurrentSuffix}",
        Subcategory = $"Subcategorie concurenta {concurrentSuffix}"
    });
    async Task<bool> TryConcurrentDeleteAsync(SqliteLocalStore currentStore)
    {
        try
        {
            await new SqliteProductRepository(currentStore, administrator)
                .DeleteAsync(concurrentDeleteProduct, "Test stergere concurenta");
            return true;
        }
        catch (ProductOperationException) { return false; }
    }
    var concurrentDeleteResults = await Task.WhenAll(
        TryConcurrentDeleteAsync(firstStore), TryConcurrentDeleteAsync(secondStore));
    Check(concurrentDeleteResults.Count(result => result) == 1,
        "Two concurrent delete requests allow exactly one successful archive operation");
    await using (var concurrencyConnection = await secondStore.OpenConnectionAsync())
    {
        await using var concurrencyCheck = TestSqliteCommand(concurrencyConnection, """
            SELECT
                (SELECT COUNT(*) FROM archive_products WHERE original_id=@id),
                (SELECT COUNT(*) FROM audit_events WHERE entity_type=@entity AND entity_id=@entityId AND action=@action)
            """, ("@id", concurrentDeleteProduct.Id), ("@entity", AuditEntities.Product),
            ("@entityId", concurrentDeleteProduct.Id.ToString()), ("@action", AuditActions.Delete));
        await using var reader = await concurrencyCheck.ExecuteReaderAsync();
        Check(await reader.ReadAsync() && reader.GetInt32(0) == 1 && reader.GetInt32(1) == 1,
            "Concurrent deletion does not create duplicate archives or success events");
    }

    async Task<Product> CreateFailureProductAsync(string stage)
    {
        var value = await CreateProductAsync(secondProducts, new ProductInput
        {
            Name = $"Produs rollback {stage} {concurrentSuffix}",
            Category = $"Categorie rollback {stage} {concurrentSuffix}",
            Subcategory = $"Subcategorie rollback {stage} {concurrentSuffix}"
        });
        await secondImages.SaveAsync(value.Id, new ProductImageData(imageBytes, "image/png", $"{stage}.png"));
        return value;
    }
    async Task ExecuteSqlAsync(string sql)
    {
        await using var connection = await secondStore.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
    async Task<int> ArchivedProductCountAsync(int id)
    {
        await using var connection = await secondStore.OpenConnectionAsync();
        await using var command = TestSqliteCommand(connection,
            "SELECT COUNT(*) FROM archive_products WHERE original_id=@id", ("@id", id));
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    var archiveFailureProduct = await CreateFailureProductAsync("archive");
    await ExecuteSqlAsync("""
        CREATE TRIGGER test_fail_archive BEFORE INSERT ON archive_operations
        BEGIN SELECT RAISE(ABORT,'forced archive failure'); END;
        """);
    try
    {
        await secondProducts.DeleteAsync(archiveFailureProduct, "Eroare injectata arhiva");
        throw new Exception("Injected archive failure did not stop deletion");
    }
    catch (Microsoft.Data.Sqlite.SqliteException) { }
    finally { await ExecuteSqlAsync("DROP TRIGGER IF EXISTS test_fail_archive;"); }
    Check((await secondProducts.GetProductsAsync()).Any(item => item.Id == archiveFailureProduct.Id) &&
          await secondImages.ExistsAsync(archiveFailureProduct.Id) &&
          await ArchivedProductCountAsync(archiveFailureProduct.Id) == 0 &&
          !Directory.EnumerateFiles(secondStore.ArchiveFilesPath, "archive.png", SearchOption.AllDirectories).Any(),
        "Archive insertion failure preserves the live product and image and removes the prepared copy");

    var deleteFailureProduct = await CreateFailureProductAsync("delete");
    await ExecuteSqlAsync($"""
        CREATE TRIGGER test_fail_live_delete BEFORE DELETE ON products
        WHEN OLD.id={deleteFailureProduct.Id}
        BEGIN SELECT RAISE(ABORT,'forced live delete failure'); END;
        """);
    try
    {
        await secondProducts.DeleteAsync(deleteFailureProduct, "Eroare injectata stergere live");
        throw new Exception("Injected live-delete failure did not stop deletion");
    }
    catch (Microsoft.Data.Sqlite.SqliteException) { }
    finally { await ExecuteSqlAsync("DROP TRIGGER IF EXISTS test_fail_live_delete;"); }
    Check((await secondProducts.GetProductsAsync()).Any(item => item.Id == deleteFailureProduct.Id) &&
          await secondImages.ExistsAsync(deleteFailureProduct.Id) &&
          await ArchivedProductCountAsync(deleteFailureProduct.Id) == 0,
        "Live deletion failure rolls back archive rows and preserves the live file");

    var auditFailureProduct = await CreateFailureProductAsync("audit");
    await ExecuteSqlAsync($"""
        CREATE TRIGGER test_fail_delete_audit BEFORE INSERT ON audit_events
        WHEN NEW.action='{AuditActions.Delete}' AND NEW.entity_id='{auditFailureProduct.Id}'
        BEGIN SELECT RAISE(ABORT,'forced audit failure'); END;
        """);
    try
    {
        await secondProducts.DeleteAsync(auditFailureProduct, "Eroare injectata audit");
        throw new Exception("Injected audit failure did not stop deletion");
    }
    catch (Microsoft.Data.Sqlite.SqliteException) { }
    finally { await ExecuteSqlAsync("DROP TRIGGER IF EXISTS test_fail_delete_audit;"); }
    Check((await secondProducts.GetProductsAsync()).Any(item => item.Id == auditFailureProduct.Id) &&
          await secondImages.ExistsAsync(auditFailureProduct.Id) &&
          await ArchivedProductCountAsync(auditFailureProduct.Id) == 0,
        "Audit failure rolls back the live deletion and all archive rows");

    var unsafePathProduct = await CreateFailureProductAsync("path");
    await using (var unsafePathConnection = await secondStore.OpenConnectionAsync())
    await using (var unsafePathCommand = TestSqliteCommand(unsafePathConnection,
        "UPDATE product_images SET relative_path='../outside.png' WHERE product_id=@id", ("@id", unsafePathProduct.Id)))
        await unsafePathCommand.ExecuteNonQueryAsync();
    try
    {
        await secondProducts.DeleteAsync(unsafePathProduct, "Verificare cale controlata");
        throw new Exception("Unsafe archive path was accepted");
    }
    catch (ArchiveContractException) { }
    Check((await secondProducts.GetProductsAsync()).Any(item => item.Id == unsafePathProduct.Id) &&
          await ArchivedProductCountAsync(unsafePathProduct.Id) == 0,
        "Archive file handling blocks paths outside the controlled live directory");

    var sqliteAudit = new SqliteAuditTrail(secondStore);
    await AuditRecorder.RecordSessionAsync(sqliteAudit, "administrator.demo", AccessRoles.Administrator, true, default);
    await AuditRecorder.RecordSessionAsync(sqliteAudit, "administrator.demo", AccessRoles.Administrator, false, default);
    var integratedEvents = await sqliteAudit.GetEventsAsync();
    static bool HasCycle(IReadOnlyList<AuditEvent> entries, string entityType, int entityId) =>
        new[] { AuditActions.Create, AuditActions.Edit, AuditActions.Delete }.All(operation =>
            entries.Count(entry => entry.EntityType == entityType && entry.EntityId == entityId.ToString() && entry.Action == operation) == 1);
    Check(HasCycle(integratedEvents, AuditEntities.Product, persistentProduct.Id) &&
          HasCycle(integratedEvents, AuditEntities.Beneficiary, persistentBeneficiary.Id) &&
          HasCycle(integratedEvents, AuditEntities.User, persistentUser.Id) &&
          integratedEvents.Where(entry => entry.Action == AuditActions.Delete)
              .All(entry => entry.ArchiveOperationId is not null) &&
          integratedEvents.Any(entry => entry.Action == AuditActions.Login) &&
          integratedEvents.Any(entry => entry.Action == AuditActions.Logout),
        "SQLite audit covers all managed object types and links deletions to archive operations");
    var integratedProductEdit = integratedEvents.Single(entry => entry.EntityType == AuditEntities.Product &&
        entry.EntityId == persistentProduct.Id.ToString() && entry.Action == AuditActions.Edit);
    var integratedBeneficiaryEdit = integratedEvents.Single(entry => entry.EntityType == AuditEntities.Beneficiary &&
        entry.EntityId == persistentBeneficiary.Id.ToString() && entry.Action == AuditActions.Edit);
    var integratedUserEdit = integratedEvents.Single(entry => entry.EntityType == AuditEntities.User &&
        entry.EntityId == persistentUser.Id.ToString() && entry.Action == AuditActions.Edit);
    Check(integratedProductEdit.Details.Contains("Valoare initiala → Valoare salvata dupa repornire", StringComparison.Ordinal) &&
          integratedProductEdit.Motif == "Verificare persistenta" &&
          integratedBeneficiaryEdit.Details.Contains("Beneficiar persistent → Beneficiar persistent editat", StringComparison.Ordinal) &&
          integratedBeneficiaryEdit.Target.Contains("Beneficiar persistent editat", StringComparison.Ordinal) &&
          integratedBeneficiaryEdit.Motif == "Corectie denumire beneficiar" &&
          integratedUserEdit.Details.Contains("Utilizator persistent → Utilizator persistent editat", StringComparison.Ordinal) &&
          integratedUserEdit.Target.Contains(persistentUser.Username, StringComparison.Ordinal) &&
          integratedUserEdit.Motif == "Corectie nume utilizator",
        "Integrated audit keeps before/after values, updated targets and separate reasons");
    Check(integratedEvents.All(entry => !entry.Details.Contains("secret-nou", StringComparison.OrdinalIgnoreCase) &&
                                               !entry.Motif.Contains("secret-nou", StringComparison.OrdinalIgnoreCase)) &&
          integratedEvents.All(entry => entry.TimestampUtc.Kind == DateTimeKind.Utc),
        "Integrated audit excludes passwords and keeps timestamps in UTC");
    Check(integratedEvents.Any(entry => entry.EntityType == AuditEntities.Category && entry.Target.Contains("Categorie persistenta", StringComparison.Ordinal)) &&
          integratedEvents.Any(entry => entry.EntityType == AuditEntities.Subcategory && entry.Target.Contains("Subcategorie persistenta", StringComparison.Ordinal)),
        "SQLite audit includes categories and subcategories created through the dedicated catalogue flow");
    Check(integratedEvents.Count(entry => entry.EntityType == AuditEntities.Project && entry.Action == AuditActions.Delete) >= 1 &&
          integratedEvents.Any(entry => entry.EntityType == AuditEntities.ProjectObservation) &&
          integratedEvents.Any(entry => entry.EntityType == AuditEntities.ProjectObservationFile),
        "SQLite audit records project, observation and observation file operations");
    await using (var archiveConnection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={secondStore.DatabasePath}"))
    {
        await archiveConnection.OpenAsync();
        await using var archivedProjects = archiveConnection.CreateCommand();
        archivedProjects.CommandText = "SELECT COUNT(*) FROM archive_projects";
        Check(Convert.ToInt32(await archivedProjects.ExecuteScalarAsync()) >= 1, "Deleting a project writes a row into archive_projects");
        await using var archivedFiles = archiveConnection.CreateCommand();
        archivedFiles.CommandText = "SELECT COUNT(*) FROM archive_project_observation_files";
        Check(Convert.ToInt32(await archivedFiles.ExecuteScalarAsync()) >= 1, "Removing an observation file writes a row into archive_project_observation_files");
    }
}
finally
{
    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    if (Directory.Exists(sqliteTestRoot)) Directory.Delete(sqliteTestRoot, true);
}

void ProjectRejected(Action operation, string message)
{
    try { operation(); }
    catch (ProjectOperationException) { Check(true, message); return; }
    throw new Exception("Expected project rejection: " + message);
}
var projectNow = new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);
var projectInput = new ProjectInput { BeneficiaryId = 1, Name = "  Hală   producție  ", Observations = "  Montaj în două etape  " }.Validated();
Check(projectInput.Name == "Hala productie" && projectInput.Observations == "Montaj in doua etape",
    "Project name and general observations use the existing trimming, space and diacritic rules");
ProjectRejected(() => new ProjectInput { BeneficiaryId = 1, Name = "   " }.Validated(), "Project name is required");
ProjectRejected(() => new ProjectInput { BeneficiaryId = 0, Name = "Proiect" }.Validated(), "Project requires a beneficiary");
ProjectRejected(() => new ProjectInput { BeneficiaryId = 1, Name = new string('P', 201) }.Validated(), "Project name is limited to 200 characters");
var project = ProjectRules.Create(10, projectInput, projectNow);
Check(project.Version == 0 && project.CreatedAtUtc == projectNow && project.UpdatedAtUtc == projectNow &&
      project.CreatedAtUtc.Kind == DateTimeKind.Utc, "New projects start at version 0 with UTC creation and update timestamps");
try { ProjectRules.Create(11, projectInput, DateTime.SpecifyKind(projectNow, DateTimeKind.Local)); throw new Exception("Local project timestamp accepted"); }
catch (ArgumentException) { Check(true, "Project timestamps must be UTC"); }
var existingProjects = new[] { project, ProjectRules.Create(12, new ProjectInput { BeneficiaryId = 2, Name = "Alt proiect" }.Validated(), projectNow) };
ProjectRejected(() => ProjectRules.EnsureUniqueName(existingProjects, 1, "  HALĂ  PRODUCȚIE ", null, "Construct Demo SRL"),
    "Project names are unique per beneficiary regardless of case, diacritics and spacing");
ProjectRules.EnsureUniqueName(existingProjects, 2, "Hala productie", null, "Atelier Tehnic SRL");
Check(true, "The same project name is allowed for a different beneficiary");
ProjectRules.EnsureUniqueName(existingProjects, 1, "Hala productie", project.Id, "Construct Demo SRL");
Check(true, "A project keeps its own name when edited");
try { ProjectRules.EnsureUniqueName(existingProjects, 1, "hala productie", null, "Construct Demo SRL"); throw new Exception("Duplicate project name accepted"); }
catch (ProjectOperationException exception)
{
    Check(exception.Message.Contains("Construct Demo SRL", StringComparison.Ordinal) && exception.Message.Contains("Hala productie", StringComparison.Ordinal),
        "Duplicate project message names the beneficiary and the existing project");
}
Check(ProjectRules.NormalizedName("  hală   PRODUCȚIE ") == ProjectRules.NormalizedName("Hala productie"),
    "Normalized project name key is stable for the per-beneficiary unique index");
var projectEdit = ProjectInput.From(project); projectEdit.BeneficiaryId = 2; projectEdit.Name = "Hala noua";
ProjectRejected(() => projectEdit.Validated(true), "Project edits require a reason");
projectEdit.Reason = "Mutare la beneficiarul corect";
var editedProject = ProjectRules.Edited(project, projectEdit.Validated(true), projectNow.AddMinutes(5));
Check(editedProject.Version == 1 && editedProject.BeneficiaryId == 2 && editedProject.CreatedAtUtc == projectNow &&
      editedProject.UpdatedAtUtc == projectNow.AddMinutes(5), "Project edits increment the version and keep the creation timestamp");
ProjectRejected(() => ProjectRules.CheckCurrent(editedProject, project), "Stale project version is rejected");
ProjectRejected(() => ProjectRules.CheckCurrent(null, project), "Deleted project is rejected on edit");
ProjectRejected(() => ProjectRules.CheckBeneficiaryExists(null), "Project save is rejected when the beneficiary no longer exists");
var observationInput = new ProjectObservationInput { Name = "  Verificare   șantier ", Content = " Fundația este turnată " }.Validated();
var observation = ProjectRules.CreateObservation(1, project.Id, observationInput, "operator", projectNow);
Check(observation.Name == "Verificare santier" && observation.Content == "Fundatia este turnata" &&
      observation.Author == "operator" && observation.Version == 0 && observation.CreatedAtUtc.Kind == DateTimeKind.Utc,
    "Project observations normalize text and keep author, version and UTC timestamps");
ProjectRejected(() => new ProjectObservationInput { Name = " " }.Validated(), "Observation name is required");
ProjectRejected(() => ProjectRules.CreateObservation(2, project.Id, observationInput, " ", projectNow), "Observation author is required");
var observationEdit = ProjectObservationInput.From(observation); observationEdit.Content = "Actualizat";
ProjectRejected(() => observationEdit.Validated(true), "Observation edits require a reason");
observationEdit.Reason = "Completare";
var editedObservation = ProjectRules.EditedObservation(observation, observationEdit.Validated(true), projectNow.AddMinutes(1));
Check(editedObservation.Version == 1 && editedObservation.CreatedAtUtc == projectNow, "Observation edits increment the version");
ProjectRejected(() => ProjectRules.CheckCurrent(editedObservation, observation), "Stale observation version is rejected");
var projectFile = ProjectFileRules.Create(1, observation.Id, @"..\..\C:\secret\Plan  fațadă.PDF", "application/pdf", 1024,
    new string('A', 64), "operator", projectNow);
Check(projectFile.OriginalName == "Plan  fațadă.PDF" && projectFile.StoredName.EndsWith(".pdf", StringComparison.Ordinal) &&
      projectFile.StoredName.Length == 36 && !projectFile.StoredName.Contains("Plan", StringComparison.Ordinal) &&
      projectFile.Sha256 == new string('a', 64) && projectFile.UploadedAtUtc.Kind == DateTimeKind.Utc,
    "Observation file metadata keeps a safe original name, a generated internal name and a normalized hash");
Check(ProjectFileRules.NewStoredName(".exe/../x") is { Length: 32 } && ProjectFileRules.NewStoredName(".pdf") != ProjectFileRules.NewStoredName(".pdf"),
    "Internal file names are unique and reject unsafe extensions");
ProjectRejected(() => ProjectFileRules.Create(2, observation.Id, "gol.txt", "text/plain", 0, new string('a', 64), "operator", projectNow),
    "Empty observation files are rejected");
ProjectRejected(() => ProjectFileRules.Create(2, observation.Id, "a.txt", "text/plain", 1, "nu-este-hash", "operator", projectNow),
    "Observation file metadata requires a SHA-256 hash");
ProjectRejected(() => ProjectFileRules.SafeOriginalName("../.."), "Path-only file names are rejected");
try { BeneficiaryRules.CheckNoLiveProjects(2); throw new Exception("Beneficiary with live projects accepted for deletion"); }
catch (BeneficiaryOperationException exception) { Check(exception.Message.Contains("2 proiecte", StringComparison.Ordinal), "Beneficiary deletion is blocked while live projects exist"); }
BeneficiaryRules.CheckNoLiveProjects(0);
Check(true, "Beneficiary without live projects passes the project deletion rule");

// Run only against an explicitly started local test instance with the documented test credentials.
if (args.Length == 2 && args[0] == "--http")
{
    var baseUri = new Uri(args[1]);
    if (!baseUri.IsLoopback) throw new Exception("HTTP checks are restricted to localhost");
    using var handler = new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new System.Net.CookieContainer() };
    using var http = new HttpClient(handler) { BaseAddress = baseUri };
    var response = await http.GetAsync("/");
    Check(response.StatusCode == System.Net.HttpStatusCode.Redirect && response.Headers.Location!.ToString().Contains("/Account/Login"), "Unauthenticated catalogue access redirects to login");
    response = await http.GetAsync("/app.css");
    Check(response.IsSuccessStatusCode && response.Content.Headers.ContentType?.MediaType == "text/css" && (await response.Content.ReadAsStringAsync()).Length > 1000, "Styles are served before authentication");
    response = await http.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string,string>{{"Username","admin"},{"Password","local-test-password-123"}}));
    Check(response.StatusCode == System.Net.HttpStatusCode.BadRequest, "Login rejects requests without antiforgery token");
    async Task<string> Token(string path)
    {
        var html = await http.GetStringAsync(path);
        var match = System.Text.RegularExpressions.Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        if (!match.Success) throw new Exception("Missing antiforgery token");
        return System.Net.WebUtility.HtmlDecode(match.Groups[1].Value);
    }
    var token = await Token("/Account/Login");
    response = await http.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string,string>{{"Username","admin"},{"Password","wrong"},{"__RequestVerificationToken",token}}));
    Check(response.StatusCode == System.Net.HttpStatusCode.OK && (await response.Content.ReadAsStringAsync()).Contains("incorect"), "Invalid credentials do not authenticate");
    token = await Token("/Account/Login");
    response = await http.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string,string>{{"Username","admin"},{"Password","local-test-password-123"},{"__RequestVerificationToken",token}}));
    Check(response.StatusCode == System.Net.HttpStatusCode.Redirect, "Valid credentials create a session");
    response = await http.GetAsync("/");
    Check(response.IsSuccessStatusCode, "Authenticated catalogue is accessible");
    token = await Token("/Account/Logout");
    response = await http.PostAsync("/Account/Logout", new FormUrlEncodedContent(new Dictionary<string,string>{{"__RequestVerificationToken",token}}));
    Check(response.StatusCode == System.Net.HttpStatusCode.Redirect, "Logout accepts authenticated antiforgery token");
    response = await http.GetAsync("/");
    Check(response.StatusCode == System.Net.HttpStatusCode.Redirect, "Logout revokes the browser session");
}

// ---- Stock movements (Task 1) ----
async Task RejectedMovement(Func<Task> operation, string message)
{
    try { await operation(); }
    catch (StockMovementOperationException) { Check(true, message); return; }
    throw new Exception("Expected rejection: " + message);
}
StockMovementInput MovementInput(StockMovementKind kind, int quantity, string description = "Test", DateOnly? date = null,
    int? beneficiaryId = null, int? projectId = null, string reason = "") => new()
{
    Kind = kind, Quantity = quantity, Description = description, Date = date ?? new DateOnly(2026, 9, 24),
    BeneficiaryId = beneficiaryId, ProjectId = projectId, Reason = reason
};

var entryRule = StockMovementRules.Validated(MovementInput(StockMovementKind.Entry, 3, "  Factură nouă  "), StockMovementKind.Entry, false);
Check(entryRule.Description == "Factura noua" && entryRule.Quantity == 3, "Movement description is normalized like other stored text");
Check(StockMovementRules.Effect(StockMovementKind.Entry, 5) == 5 && StockMovementRules.Effect(StockMovementKind.Exit, 5) == -5, "Entries add and exits subtract stock");
Check(StockMovementRules.DisplayDate(new DateOnly(2022, 8, 22)) == "22-08-2022" && StockMovementRules.ParseLegacyDate("01-03-2024") == new DateOnly(2024, 3, 1),
    "Movement dates use the legacy dd-MM-yyyy display format");
try { StockMovementRules.Validated(MovementInput(StockMovementKind.Exit, 1, "  "), StockMovementKind.Exit, false); throw new Exception("Blank description accepted"); }
catch (StockMovementOperationException) { Check(true, "Movement description is mandatory"); }
try { StockMovementRules.Validated(MovementInput(StockMovementKind.Exit, 0), StockMovementKind.Exit, false); throw new Exception("Zero quantity accepted"); }
catch (StockMovementOperationException) { Check(true, "Movement quantity must be at least 1"); }
try { StockMovementRules.Validated(MovementInput(StockMovementKind.Exit, StockMovementRules.MaxQuantity + 1), StockMovementKind.Exit, false); throw new Exception("Oversize quantity accepted"); }
catch (StockMovementOperationException) { Check(true, "Movement quantity has an upper limit"); }
try { StockMovementRules.Validated(MovementInput(StockMovementKind.Entry, 1, beneficiaryId: 1), StockMovementKind.Entry, false); throw new Exception("Entry with beneficiary accepted"); }
catch (StockMovementOperationException) { Check(true, "Beneficiary and project are refused for entries"); }
try { StockMovementRules.Validated(MovementInput(StockMovementKind.Exit, 1, projectId: 1), StockMovementKind.Exit, false); throw new Exception("Project without beneficiary accepted"); }
catch (StockMovementOperationException) { Check(true, "A project requires a beneficiary"); }
try { StockMovementRules.Validated(MovementInput(StockMovementKind.Exit, 1), StockMovementKind.Exit, true); throw new Exception("Edit without reason accepted"); }
catch (StockMovementOperationException) { Check(true, "Editing a movement requires a reason"); }
Check(StockMovementRules.Validated(MovementInput(StockMovementKind.Exit, 1, date: new DateOnly(2099, 1, 1)), StockMovementKind.Exit, false).Date == new DateOnly(2099, 1, 1),
    "Movement date may be in the future");
Check(AuditNavigation.EditUrl(new AuditEvent(Guid.NewGuid(), DateTime.UtcNow, "a", "r", AuditEntities.StockMovement, AuditActions.Create, "t", "d", "", "5")) == "/miscari/5" &&
      AuditNavigation.EditUrl(new AuditEvent(Guid.NewGuid(), DateTime.UtcNow, "a", "r", AuditEntities.StockMovement, AuditActions.Delete, "t", "d", "", "5")) is null,
    "Journal links movement events to their product page and not after deletion");

var movementRoot = Path.Combine(Path.GetTempPath(), "blazorstoc-movements-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(movementRoot);
try
{
    var movementConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["App:LocalDatabasePath"] = Path.Combine(movementRoot, "movements.db"),
        ["App:ProductImagesPath"] = Path.Combine(movementRoot, "product-images"),
        ["App:ArchiveFilesPath"] = Path.Combine(movementRoot, "archive-files"),
        ["App:AuditPath"] = Path.Combine(movementRoot, "legacy-audit.jsonl")
    }).Build();
    var movementEnvironment = new TestWebHostEnvironment(movementRoot);
    var movementAccess = new TestAccessControl(true, "operator.stoc");
    var movementStore = new SqliteLocalStore(movementEnvironment, movementConfiguration, NullLogger<SqliteLocalStore>.Instance);
    var movementProducts = new SqliteProductRepository(movementStore, movementAccess);
    var movementBeneficiaries = new SqliteBeneficiaryRepository(movementStore, movementAccess);
    var movementProjects = new SqliteProjectRepository(movementStore, movementAccess);
    var stockMovements = new SqliteStockMovementRepository(movementStore, movementAccess);
    var all = new StockMovementQuery(null, false, 1, 0);

    // One-time baseline: seeded products with stock receive an opening movement, so stock = sum of movements.
    var seededStock = await stockMovements.GetPageAsync(1, all);
    Check(seededStock.Stock == 12 && seededStock.Items.Count == 1 && seededStock.Items[0].Description == "Stoc initial" &&
          seededStock.Items.Sum(m => m.Effect) == seededStock.Stock, "Seeded stock becomes a single opening entry");
    var seededNegative = await stockMovements.GetPageAsync(8, all);
    Check(seededNegative.Stock == -2 && seededNegative.Items.Count == 1 && seededNegative.Items[0].Kind == StockMovementKind.Exit &&
          seededNegative.Items[0].Quantity == 2, "A negative seeded stock becomes an opening exit");
    Check((await stockMovements.GetPageAsync(5, all)).Items.Count == 0, "Products without stock get no opening movement");

    var product = await CreateProductAsync(movementProducts, new ProductInput { Name = "Produs miscari", Category = "Miscari", Subcategory = "Test" });
    Check(product.Quantity == 0, "A product used for movements starts with stock 0");
    var first = await stockMovements.CreateAsync(product.Id, MovementInput(StockMovementKind.Entry, 10, "Factura 1", new DateOnly(2026, 9, 1)));
    Check(first.Stock == 10 && first.Movement.Version == 0 && !first.Movement.Modified && first.Movement.Operator == "operator.stoc", "An entry increases the stock");
    var exitNoBeneficiary = await stockMovements.CreateAsync(product.Id, MovementInput(StockMovementKind.Exit, 4, "Iesire fara beneficiar", new DateOnly(2026, 9, 2)));
    Check(exitNoBeneficiary.Stock == 6 && exitNoBeneficiary.Movement.BeneficiaryId is null, "An exit without beneficiary decreases the stock");
    var overdraw = await stockMovements.CreateAsync(product.Id, MovementInput(StockMovementKind.Exit, 8, "Peste stoc", new DateOnly(2099, 5, 5)));
    Check(overdraw.Stock == -2 && (await movementProducts.GetProductAsync(product.Id))!.Quantity == -2, "An exit may take the stock below zero without blocking");

    await RejectedMovement(() => stockMovements.CreateAsync(product.Id, MovementInput(StockMovementKind.Entry, 1, "")), "The repository rejects a blank description");
    await RejectedMovement(() => stockMovements.CreateAsync(product.Id, MovementInput(StockMovementKind.Exit, 1, beneficiaryId: 9999)), "An unknown beneficiary is rejected");
    await RejectedMovement(() => stockMovements.CreateAsync(9999, MovementInput(StockMovementKind.Entry, 1)), "A missing product is rejected");
    Check((await movementProducts.GetProductAsync(product.Id))!.Quantity == -2, "Rejected movements do not change the stock");

    var movementBeneficiary = await movementBeneficiaries.CreateAsync(new BeneficiaryInput { Name = "Beneficiar miscari", Cui = "RO18888881" });
    var otherBeneficiary = await movementBeneficiaries.CreateAsync(new BeneficiaryInput { Name = "Alt beneficiar", Cui = "RO18888882" });
    var movementProject = await movementProjects.CreateAsync(new ProjectInput { BeneficiaryId = movementBeneficiary.Id, Name = "Proiect miscari", Observations = "" });
    await RejectedMovement(() => stockMovements.CreateAsync(product.Id, MovementInput(StockMovementKind.Exit, 1, beneficiaryId: otherBeneficiary.Id, projectId: movementProject.Id)),
        "A project must belong to the chosen beneficiary");
    var projectExit = await stockMovements.CreateAsync(product.Id, MovementInput(StockMovementKind.Exit, 1, "Montaj", new DateOnly(2026, 9, 3),
        movementBeneficiary.Id, movementProject.Id));
    Check(projectExit.Stock == -3 && projectExit.Movement.BeneficiaryName == "Beneficiar miscari" && projectExit.Movement.ProjectName == "Proiect miscari",
        "An exit stores the beneficiary and project");
    var projectMovements = await stockMovements.GetForProjectAsync(movementProject.Id);
    Check(projectMovements.Count == 1 && projectMovements[0].ProductCode == "Produs miscari" && projectMovements[0].Quantity == 1 &&
          projectMovements[0].Date == new DateOnly(2026, 9, 3), "Project movements are readable through project_id");
    try { await movementProjects.DeleteAsync(movementProject, "Test automat"); throw new Exception("Project with movements deleted"); }
    catch (ProjectOperationException) { Check(true, "A project with movements cannot be deleted"); }
    await Rejected(() => movementProducts.DeleteAsync(product with { Quantity = 0 }, "Test automat"), "A product with movements cannot be deleted");

    var ascending = await stockMovements.GetPageAsync(product.Id, all);
    Check(ascending.Items.Select(m => m.Date).SequenceEqual(ascending.Items.Select(m => m.Date).Order()) && ascending.Items.Last().Date == new DateOnly(2099, 5, 5),
        "Movements are ordered by their own date, not by the time they were entered");
    var descendingPage = await stockMovements.GetPageAsync(product.Id, new StockMovementQuery(null, true, 1, 0));
    Check(descendingPage.Items.First().Date == new DateOnly(2099, 5, 5), "Descending order lists the latest date first");
    var exitsOnly = await stockMovements.GetPageAsync(product.Id, new StockMovementQuery(StockMovementKind.Exit, false, 1, 0));
    Check(exitsOnly.Items.Count == 3 && exitsOnly.Items.All(m => m.Kind == StockMovementKind.Exit) && exitsOnly.TotalCount == 3, "The kind filter keeps only exits");
    var secondPage = await stockMovements.GetPageAsync(product.Id, new StockMovementQuery(null, false, 2, 3));
    Check(secondPage.TotalCount == 4 && secondPage.Items.Count == 1, "Pagination is applied after filtering");

    // Edit: reason, stock correction, history and the modified marker.
    await RejectedMovement(() => stockMovements.UpdateAsync(first.Movement, MovementInput(StockMovementKind.Entry, 12, "Factura 1")), "Editing requires a reason");
    await RejectedMovement(() => stockMovements.UpdateAsync(first.Movement, MovementInput(StockMovementKind.Entry, 10, "Factura 1", new DateOnly(2026, 9, 1), reason: "Nimic")),
        "An edit that changes nothing is rejected");
    var edited = await stockMovements.UpdateAsync(first.Movement, MovementInput(StockMovementKind.Entry, 12, "Factura 1 corectata", new DateOnly(2026, 9, 1), reason: "Eroare de tastare"));
    Check(edited.Stock == -1 && edited.Movement.Quantity == 12 && edited.Movement.Version == 1 && edited.Movement.Modified, "Editing an entry applies the stock correction and marks the movement");
    var editHistory = await stockMovements.GetHistoryAsync(first.Movement.Id);
    Check(editHistory.Count == 1 && editHistory[0].StockCorrection == 2 && editHistory[0].Reason == "Eroare de tastare" && editHistory[0].Actor == "operator.stoc" &&
          editHistory[0].Changes.Contains("Cantitate: 10 → 12") && editHistory[0].Changes.Contains("Corecție stoc: +2"), "The edit is kept in the movement history");
    await RejectedMovement(() => stockMovements.UpdateAsync(first.Movement, MovementInput(StockMovementKind.Entry, 15, "Factura 1", reason: "Vechi")), "A stale edit cannot overwrite a newer version");
    var kindStays = await stockMovements.UpdateAsync(edited.Movement, MovementInput(StockMovementKind.Exit, 11, "Factura 1 corectata", new DateOnly(2026, 9, 1), reason: "Schimb tip"));
    Check(kindStays.Movement.Kind == StockMovementKind.Entry && kindStays.Stock == -2, "The movement type cannot be changed by an edit");
    var exitEdit = await stockMovements.UpdateAsync(projectExit.Movement, MovementInput(StockMovementKind.Exit, 1, "Montaj", new DateOnly(2026, 9, 3), reason: "Fara beneficiar"));
    Check(exitEdit.Movement.BeneficiaryId is null && exitEdit.Movement.ProjectId is null && exitEdit.Stock == kindStays.Stock, "An exit edit can remove the beneficiary and project");
    Check((await stockMovements.GetPageAsync(product.Id, all)).AnyModified, "The page reports modified movements");

    // Atomic stock under concurrent movements.
    var concurrentBefore = (await movementProducts.GetProductAsync(product.Id))!.Quantity;
    await Task.WhenAll(Enumerable.Range(0, 8).Select(index => new SqliteStockMovementRepository(movementStore, movementAccess)
        .CreateAsync(product.Id, MovementInput(StockMovementKind.Entry, 1, $"Concurent {index}"))));
    var concurrentPage = await stockMovements.GetPageAsync(product.Id, all);
    Check(concurrentPage.Stock == concurrentBefore + 8 && concurrentPage.Items.Sum(m => m.Effect) == concurrentPage.Stock,
        "Concurrent movements keep the stock equal to the sum of the movements");

    // Delete: reason, archive, stock correction, history archived.
    var beforeDelete = concurrentPage.Stock;
    await RejectedMovement(() => stockMovements.DeleteAsync(edited.Movement with { Version = 0 }, "Test automat"), "A stale delete is rejected");
    await RejectedMovement(() => stockMovements.DeleteAsync(kindStays.Movement, " "), "Deleting a movement requires a reason");
    var stockAfterDelete = await stockMovements.DeleteAsync(kindStays.Movement, "Test automat");
    Check(stockAfterDelete == beforeDelete - 11 && (await stockMovements.GetAsync(kindStays.Movement.Id)) is null, "Deleting an entry removes it and corrects the stock");
    await RejectedMovement(() => stockMovements.DeleteAsync(kindStays.Movement, "Test automat"), "A movement cannot be deleted twice");
    await using (var archiveConnection = await movementStore.OpenConnectionAsync())
    {
        await using var archived = TestSqliteCommand(archiveConnection, """
            SELECT COUNT(*) FROM archive_operations o INNER JOIN archive_stock_movements a ON a.archive_id=o.id
            WHERE o.entity_type='MiscareStoc' AND a.original_id=@id AND a.quantity=11 AND a.kind=1 AND o.motif='Test automat'
              AND (SELECT COUNT(*) FROM archive_relations r WHERE r.archive_id=o.id AND r.relation_type='IstoricMiscareStoc')=2
            """, ("@id", kindStays.Movement.Id));
        Check(Convert.ToInt32(await archived.ExecuteScalarAsync()) == 1, "A deleted movement is archived together with its modification history");
        await using var liveHistory = TestSqliteCommand(archiveConnection, "SELECT COUNT(*) FROM stock_movement_history WHERE movement_id=@id", ("@id", kindStays.Movement.Id));
        Check(Convert.ToInt32(await liveHistory.ExecuteScalarAsync()) == 0, "The live history of a deleted movement is moved to the archive");
        await using var audit = TestSqliteCommand(archiveConnection, """
            SELECT
              (SELECT COUNT(*) FROM audit_events WHERE entity_type='MiscareStoc' AND action='Adăugare' AND target='Produs miscari' AND details LIKE '%Cod produs: Produs miscari%'),
              (SELECT COUNT(*) FROM audit_events WHERE entity_type='MiscareStoc' AND action='Editare' AND motif='Eroare de tastare' AND details LIKE '%Cantitate: 10 → 12%'),
              (SELECT COUNT(*) FROM audit_events WHERE entity_type='MiscareStoc' AND action='Ștergere' AND motif='Test automat' AND archive_operation_id IS NOT NULL)
            """);
        await using var auditReader = await audit.ExecuteReaderAsync();
        await auditReader.ReadAsync();
        Check(auditReader.GetInt32(0) >= 12 && auditReader.GetInt32(1) == 1 && auditReader.GetInt32(2) == 1,
            "Movement creation, edit and deletion are audited with target, details, reason and archive link");
    }
    var finalPage = await stockMovements.GetPageAsync(product.Id, all);
    Check(finalPage.Stock == finalPage.Items.Sum(m => m.Effect), "The stock always equals the sum of the remaining movements");

    // The baseline runs once: deleting an opening movement is not undone by a restart.
    var seededDelete = await stockMovements.DeleteAsync(seededStock.Items[0], "Test automat");
    var restartedStore = new SqliteLocalStore(movementEnvironment, movementConfiguration, NullLogger<SqliteLocalStore>.Instance);
    var restartedPage = await new SqliteStockMovementRepository(restartedStore, movementAccess).GetPageAsync(1, all);
    Check(seededDelete == 0 && restartedPage.Items.Count == 0 && restartedPage.Stock == 0, "The opening-movement migration does not run again after a restart");
}
finally
{
    try { Directory.Delete(movementRoot, true); } catch (IOException) { }
}

// Databases created before this module keep their stock_movements rows and receive the new columns.
var legacyMovementRoot = Path.Combine(Path.GetTempPath(), "blazorstoc-legacy-movements-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(legacyMovementRoot);
try
{
    var legacyDatabase = Path.Combine(legacyMovementRoot, "legacy.db");
    await using (var legacyConnection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={legacyDatabase}"))
    {
        await legacyConnection.OpenAsync();
        await using var create = legacyConnection.CreateCommand();
        create.CommandText = """
            CREATE TABLE stock_movements (id INTEGER PRIMARY KEY AUTOINCREMENT, product_id INTEGER NOT NULL, beneficiary_id INTEGER,
                project_id INTEGER, quantity INTEGER NOT NULL, created_utc TEXT NOT NULL);
            INSERT INTO stock_movements(product_id,quantity,created_utc) VALUES(1,3,'2026-01-02T10:00:00.0000000Z');
            """;
        await create.ExecuteNonQueryAsync();
    }
    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    var legacyConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["App:LocalDatabasePath"] = legacyDatabase,
        ["App:ProductImagesPath"] = Path.Combine(legacyMovementRoot, "product-images"),
        ["App:ArchiveFilesPath"] = Path.Combine(legacyMovementRoot, "archive-files"),
        ["App:AuditPath"] = Path.Combine(legacyMovementRoot, "legacy-audit.jsonl")
    }).Build();
    var legacyMovementStore = new SqliteLocalStore(new TestWebHostEnvironment(legacyMovementRoot), legacyConfiguration, NullLogger<SqliteLocalStore>.Instance);
    await using var migrated = await legacyMovementStore.OpenConnectionAsync();
    await using var columns = TestSqliteCommand(migrated, """
        SELECT COUNT(*) FROM pragma_table_info('stock_movements') WHERE name IN ('kind','movement_date','description','operator','version','updated_utc')
        """);
    Check(Convert.ToInt32(await columns.ExecuteScalarAsync()) == 6, "A pre-existing stock_movements table receives the movement columns");
    await using var kept = TestSqliteCommand(migrated, "SELECT COUNT(*) FROM stock_movements WHERE product_id=1 AND quantity=3 AND kind=1 AND version=0");
    Check(Convert.ToInt32(await kept.ExecuteScalarAsync()) == 1, "Existing movement rows are kept and default to entries at version 0");
}
finally
{
    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    try { Directory.Delete(legacyMovementRoot, true); } catch (IOException) { }
}

sealed class TestAccessControl(bool administrator, string username) : IAccessControl
{
    public Task<bool> IsAdministratorAsync(CancellationToken cancellationToken = default) => Task.FromResult(administrator);
    public Task<bool> CanManageProductsAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task<bool> CanManageBeneficiariesAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task<string?> GetUsernameAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(username);
    public Task EnsureAdministratorAsync(CancellationToken cancellationToken = default) => administrator
        ? Task.CompletedTask
        : Task.FromException(new AccessDeniedException("Test access denied"));
    public Task EnsureProductOperatorAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task EnsureBeneficiaryOperatorAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

sealed class TestAuditTrail : IAuditTrail
{
    public List<AuditWrite> Entries { get; } = [];
    public Task RecordAsync(AuditWrite entry, CancellationToken cancellationToken = default) { Entries.Add(entry); return Task.CompletedTask; }
    public Task<IReadOnlyList<AuditEvent>> GetEventsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AuditEvent>>(Array.Empty<AuditEvent>());
}

sealed class TestWebHostEnvironment(string contentRootPath) : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "BlazorStoc.Checks";
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    public string WebRootPath { get; set; } = contentRootPath;
    public string EnvironmentName { get; set; } = "Test";
    public string ContentRootPath { get; set; } = contentRootPath;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
