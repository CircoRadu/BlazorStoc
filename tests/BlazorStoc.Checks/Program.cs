using BlazorStoc.Checks;
using BlazorStoc.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using PdfSharp.Pdf.IO;

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
Check(AuditNavigation.TargetUrl(productAuditLink) == "/produse/12" &&
      AuditNavigation.TargetUrl(beneficiaryAuditLink) == "/beneficiari/7" &&
      AuditNavigation.TargetUrl(userAuditLink) == "/utilizatori/3" &&
      AuditNavigation.TargetUrl(productAuditLink with { EntityType = AuditEntities.Project, EntityId = "4" }) == "/proiecte/4",
    "Product, beneficiary, user and project audit targets open the read-only object page from entity type and stable identifier");
Check(AuditNavigation.TargetUrl(productAuditLink)!.Contains("edit", StringComparison.Ordinal) == false &&
      AuditNavigation.TargetUrl(userAuditLink)!.Contains('?') == false,
    "Audit target links never carry an edit trigger");
Check(AuditNavigation.TargetUrl(productAuditLink with { Target = "/proiecte/99" }) == "/produse/12" &&
      AuditNavigation.TargetUrl(productAuditLink with { EntityId = "12abc", Target = "#99 · Produs" }) is null &&
      AuditNavigation.TargetUrl(productAuditLink with { EntityId = "" }) is null &&
      AuditNavigation.TargetUrl(productAuditLink with { EntityId = "-3" }) is null,
    "Audit routes ignore the display text of the target and require a valid stable identifier");
Check(AuditNavigation.TargetUrl(userAuditLink with { Action = AuditActions.Login, EntityId = "" }) is null &&
      AuditNavigation.TargetUrl(userAuditLink with { Action = AuditActions.Logout, EntityId = "" }) is null &&
      AuditNavigation.TargetUrl(productAuditLink with { EntityType = AuditEntities.Subcategory }) is null &&
      AuditNavigation.TargetUrl(productAuditLink with { EntityType = AuditEntities.ProjectObservationFile }) is null,
    "Session events and entity types without a page are shown as text");
var removedAt = productAuditLink.TimestampUtc.AddMinutes(5);
var productDeleteAudit = productAuditLink with { Id = Guid.NewGuid(), TimestampUtc = removedAt, Action = AuditActions.Delete };
var removals = AuditNavigation.RemovalTimes([productAuditLink, productDeleteAudit, beneficiaryAuditLink]);
Check(AuditNavigation.TargetUrl(productAuditLink, removals) is null &&
      AuditNavigation.TargetUrl(productAuditLink with { Action = AuditActions.Edit, TimestampUtc = removedAt.AddMinutes(1) }, removals) == "/produse/12" &&
      AuditNavigation.TargetUrl(beneficiaryAuditLink, removals) == "/beneficiari/7" &&
      AuditNavigation.TargetUrl(productAuditLink with { EntityId = "13" }, removals) == "/produse/13" &&
      AuditNavigation.TargetUrl(userAuditLink with { EntityId = "12" }, removals) == "/utilizatori/12",
    "Objects deleted after the event get no link, while other objects, other types and later events keep theirs");

Check(AuditListState.Default.Url() == "/jurnal" &&
      AuditListState.From("  ", null, null, null, null, null, null) == AuditListState.Default &&
      AuditListState.From("cod x", "Produs", "Editare", "admin", "2026-09-25", 50, 3).Url() ==
        "/jurnal?q=cod%20x&tip=Produs&operatie=Editare&operator=admin&data=2026-09-25&pe-pagina=50&pagina=3",
    "The journal state is written to the address only for values that differ from the defaults");
var restoredJournal = AuditListState.From("a&b=c", "Beneficiar", "Adăugare", "op", "2026-09-25", 20, 2);
Check(restoredJournal.Query == "a&b=c" && restoredJournal.PageSize == 20 && restoredJournal.Page == 2 &&
      AuditListState.DateLabel("2026-09-25") == "25.09.2026" &&
      new Uri("http://localhost" + restoredJournal.Url()).Query.Contains("q=a%26b%3Dc", StringComparison.Ordinal),
    "The journal state round-trips through the address and keeps special characters escaped");
Check(AuditListState.From(null, null, null, null, "25/09/2026", 7, 0) == AuditListState.Default &&
      AuditListState.From(null, null, null, null, "2026-13-40", 0, -4).PageSize == 0 &&
      AuditListState.From(null, null, null, null, "2026-13-40", 0, -4).DateKey is null &&
      AuditListState.From(null, null, null, null, null, null, -4).Page == 1,
    "Malformed journal address values fall back to the defaults");
Check(AuditNavigation.TargetUrl(productAuditLink with { Action = AuditActions.Delete }) is null &&
      AuditNavigation.TargetUrl(productAuditLink with { EntityType = AuditEntities.Category }) is null &&
      AuditNavigation.TargetUrl(productAuditLink with { EntityId = "invalid" }) is null,
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
catch (BeneficiaryOperationException exception)
{
    Check(exception.Message == BeneficiaryRules.DuplicateCuiMessage("Beneficiar nou SRL"),
        "Duplicate beneficiary CUI is rejected and names the stored beneficiary, not the typed name");
}
var editedDemoBeneficiary = (await demoBeneficiaries.GetBeneficiariesAsync()).Single(item => item.Cui == "RO10000001");
var duplicateCuiEdit = BeneficiaryEdit(editedDemoBeneficiary); duplicateCuiEdit.Cui = " ro12345678 ";
try { await demoBeneficiaries.UpdateAsync(editedDemoBeneficiary, duplicateCuiEdit); throw new Exception("Duplicate CUI accepted on edit"); }
catch (BeneficiaryOperationException exception)
{
    Check(exception.Message == BeneficiaryRules.DuplicateCuiMessage("Beneficiar nou SRL") && duplicateCuiEdit.Cui == " ro12345678 " &&
          (await demoBeneficiaries.GetBeneficiariesAsync()).Single(item => item.Id == editedDemoBeneficiary.Id) == editedDemoBeneficiary,
        "Editing a beneficiary to an existing CUI names the owner, keeps the form values and changes nothing");
}
var unchangedCuiEdit = BeneficiaryEdit(editedDemoBeneficiary); unchangedCuiEdit.Name = "Construct Demo Actualizat SRL";
var unchangedCuiSaved = await demoBeneficiaries.UpdateAsync(editedDemoBeneficiary, unchangedCuiEdit);
Check(unchangedCuiSaved.Cui == "RO10000001" && unchangedCuiSaved.Name == unchangedCuiEdit.Name,
    "Editing a beneficiary without changing its CUI never reports itself as the duplicate");
Check(BeneficiaryRules.DuplicateCuiMessage(null) == "Există deja un beneficiar cu acest CUI." &&
      BeneficiaryRules.DuplicateCuiMessage("  ") == "Există deja un beneficiar cu acest CUI.",
    "Duplicate CUI message without a known owner falls back to the plain wording");
try { await demoBeneficiaries.CreateAsync(new BeneficiaryInput { Name = "BENEFICIÁR NOU SRL", Cui = "RO87654321" }); throw new Exception("Duplicate beneficiary name accepted"); }
catch (BeneficiaryOperationException exception) { Check(exception.Message.Contains("ro12345678", StringComparison.Ordinal), "Duplicate beneficiary name is rejected and reports its CUI"); }
try { await demoBeneficiaries.CreateAsync(new BeneficiaryInput { Name = "CUI invalid", Cui = "RO-ABC" }); throw new Exception("Invalid CUI accepted"); }
catch (BeneficiaryOperationException) { Check(true, "Invalid beneficiary CUI is rejected"); }
Check(ProductMenuSelection.IsSameSelection("http://localhost:5082/produse?categorie=Scule&subcategorie=Găurire", "/produse?subcategorie=G%C4%83urire&categorie=scule") &&
      ProductMenuSelection.IsSameSelection("http://localhost:5082/produse", "/produse/") &&
      ProductMenuSelection.IsSameSelection("http://localhost:5082/produse?edit=3", "/produse"),
    "The leave guard treats the same category and subcategory as the same selection");
Check(!ProductMenuSelection.IsSameSelection("http://localhost:5082/produse?categorie=Scule", "/produse") &&
      !ProductMenuSelection.IsSameSelection("http://localhost:5082/produse", "/produse?categorie=Scule") &&
      !ProductMenuSelection.IsSameSelection("http://localhost:5082/produse?categorie=Scule", "/produse?categorie=Scule&subcategorie=Găurire") &&
      !ProductMenuSelection.IsSameSelection("http://localhost:5082/produse?categorie=Scule", "/produse?categorie=Altele"),
    "The leave guard detects another category, another subcategory or \"Toate produsele\"");
Check(ReturnNavigation.Safe("/produse/3/miscari") == "/produse/3/miscari" && ReturnNavigation.Safe(" /produse/3 ") == "/produse/3" &&
      ReturnNavigation.Safe(null) is null && ReturnNavigation.Safe("") is null && ReturnNavigation.Safe("produse/3") is null &&
      ReturnNavigation.Safe("//evil.example/x") is null && ReturnNavigation.Safe("https://evil.example/x") is null &&
      ReturnNavigation.Safe("/\\evil.example") is null && ReturnNavigation.Safe("/a\nb") is null,
    "The return address accepts only same-site paths");
Check(ReturnNavigation.EditUrl(3, "/produse/3/miscari") == "/produse?edit=3&inapoi=%2Fproduse%2F3%2Fmiscari" &&
      ReturnNavigation.DeleteUrl(3, "/produse/3/miscari") == "/produse?sterge=3&inapoi=%2Fproduse%2F3%2Fmiscari" &&
      ReturnNavigation.EditUrl(3, "https://evil.example") == "/produse?edit=3",
    "Edit and delete links from the product page carry the page to return to");
var saveSummary = SaveSummary.Changed(new("Nume", "Alfa", "Alfa"), new("Descriere", "", "Text nou"), new("Cod", " A1 ", "B2"), new("Lung", "x", new string('y', 500)));
Check(saveSummary.Select(change => change.Field).SequenceEqual(["Descriere", "Cod", "Lung"]) &&
      saveSummary[0].Before == SaveSummary.Empty && saveSummary[1].Before == "A1" && saveSummary[2].After.Length == SaveSummary.MaximumValueLength + 1,
    "Save summary lists only changed fields, shows empty values and shortens very long ones");
Check(SaveSummary.Changed(new AuditChange("Nume", "Alfa", "Alfa")).Count == 0, "Save summary is empty when no field changed");
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
Check(auditTrail.Entries.Count(entry => entry.EntityType == AuditEntities.Beneficiary) == 4, "Beneficiary changes are written to the audit trail");

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
        Check((string?)await version.ExecuteScalarAsync() == "10", "Archive schema is versioned with the live SQLite schema");
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
                AuditEntities.StockMovement, AuditEntities.Vehicle }.Order()),
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
        Check(exception.Message == BeneficiaryRules.DuplicateCuiMessage(persistentBeneficiary.Name),
            "SQLite duplicate CUI validation reports the existing beneficiary name");
    }

    var otherPersistentBeneficiary = await firstBeneficiaries.CreateAsync(new BeneficiaryInput { Name = "Beneficiar cu alt CUI", Cui = "RO55667788" });
    var cuiTakenEdit = BeneficiaryEdit(otherPersistentBeneficiary); cuiTakenEdit.Cui = persistentBeneficiary.Cui.ToLowerInvariant();
    try
    {
        await firstBeneficiaries.UpdateAsync(otherPersistentBeneficiary, cuiTakenEdit);
        throw new Exception("SQLite duplicate CUI accepted on edit");
    }
    catch (BeneficiaryOperationException exception)
    {
        Check(exception.Message == BeneficiaryRules.DuplicateCuiMessage(persistentBeneficiary.Name) &&
              cuiTakenEdit.Cui == persistentBeneficiary.Cui.ToLowerInvariant() &&
              (await firstBeneficiaries.GetBeneficiariesAsync()).Single(item => item.Id == otherPersistentBeneficiary.Id) == otherPersistentBeneficiary,
            "SQLite edit to an existing CUI names the owner, keeps the form values and changes nothing");
    }
    var sameCuiEdit = BeneficiaryEdit(otherPersistentBeneficiary); sameCuiEdit.Name = "Beneficiar cu alt CUI actualizat";
    Check((await firstBeneficiaries.UpdateAsync(otherPersistentBeneficiary, sameCuiEdit)).Cui == otherPersistentBeneficiary.Cui,
        "SQLite edit that keeps its own CUI is not reported as a duplicate");

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

    var lockedBeneficiaryInput = ProjectInput.From(editedProjectRecord);
    lockedBeneficiaryInput.BeneficiaryId = editedProjectRecord.BeneficiaryId + 1000; lockedBeneficiaryInput.Reason = "Mutare";
    var auditCountBeforeMoveAttempt = (await new SqliteAuditTrail(secondStore).GetEventsAsync()).Count;
    try { await secondProjects.UpdateAsync(editedProjectRecord, lockedBeneficiaryInput); throw new Exception("SQLite project moved to another beneficiary"); }
    catch (ProjectOperationException exception)
    {
        Check(exception.Message == ProjectRules.BeneficiaryLockedMessage &&
              (await secondProjects.GetAsync(editedProjectRecord.Id))! == editedProjectRecord &&
              (await new SqliteAuditTrail(secondStore).GetEventsAsync()).Count == auditCountBeforeMoveAttempt,
            "SQLite rejects moving a project to another beneficiary and leaves the project and audit unchanged");
    }

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
var projectEdit = ProjectInput.From(project); projectEdit.Name = "Hala noua";
ProjectRejected(() => projectEdit.Validated(true), "Project edits require a reason");
projectEdit.Reason = "Corectie denumire";
var editedProject = ProjectRules.Edited(project, projectEdit.Validated(true), projectNow.AddMinutes(5));
Check(editedProject.Version == 1 && editedProject.BeneficiaryId == project.BeneficiaryId && editedProject.CreatedAtUtc == projectNow &&
      editedProject.UpdatedAtUtc == projectNow.AddMinutes(5), "Project edits increment the version and keep the creation timestamp");
var projectMoveAttempt = ProjectInput.From(project); projectMoveAttempt.BeneficiaryId = project.BeneficiaryId + 1; projectMoveAttempt.Reason = "Mutare";
try { ProjectRules.Edited(project, projectMoveAttempt.Validated(true), projectNow.AddMinutes(5)); throw new Exception("Project moved to another beneficiary"); }
catch (ProjectOperationException exception) { Check(exception.Message == ProjectRules.BeneficiaryLockedMessage, "A project edit cannot change the beneficiary chosen at creation"); }
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
// Exits need a destination: unless given, an exit with a beneficiary goes to the beneficiary, any other one is a generic sale.
StockMovementInput MovementInput(StockMovementKind kind, int quantity, string description = "Test", DateOnly? date = null,
    int? beneficiaryId = null, int? projectId = null, string reason = "", ExitDestination? destination = null,
    int? vehicleId = null, int? sourceVehicleId = null, bool noDestination = false) => new()
{
    Kind = kind, Quantity = quantity, Description = description, Date = date ?? new DateOnly(2026, 9, 24),
    BeneficiaryId = beneficiaryId, ProjectId = projectId, Reason = reason,
    Destination = noDestination || kind == StockMovementKind.Entry ? null
        : destination ?? (beneficiaryId is not null ? ExitDestination.Beneficiary : ExitDestination.GenericSale),
    VehicleId = vehicleId, SourceVehicleId = sourceVehicleId
};

var entryRule = StockMovementRules.Validated(MovementInput(StockMovementKind.Entry, 3, "  Factură nouă  "), StockMovementKind.Entry, false);
Check(entryRule.Description == "Factura noua" && entryRule.Quantity == 3, "Movement description is normalized like other stored text");
Check(StockMovementRules.Effect(StockMovementKind.Entry, 5) == 5 && StockMovementRules.Effect(StockMovementKind.Exit, 5) == -5, "Entries add and exits subtract stock");
Check(StockMovementRules.DisplayDate(new DateOnly(2022, 8, 22)) == "22.08.2022" && StockMovementRules.LegacyDate(new DateOnly(2022, 8, 22)) == "22-08-2022" && StockMovementRules.ParseLegacyDate("01-03-2024") == new DateOnly(2024, 3, 1),
    "Movement dates are displayed as dd.MM.yyyy while the legacy column keeps dd-MM-yyyy");
Check(StockMovementRules.NormalizeDisplayDates("Data: 22-08-2022 → 01-03-2024; cod 99-99-2020; ref 122-08-2022") == "Data: 22.08.2022 → 01.03.2024; cod 99-99-2020; ref 122-08-2022"
      && StockMovementRules.NormalizeDisplayDates(null) == "", "Older journal texts show real dates as dd.MM.yyyy and leave other text untouched");
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
var movementToday = new DateOnly(2026, 9, 25);
Check(StockMovementRules.Validated(MovementInput(StockMovementKind.Exit, 1, date: movementToday), StockMovementKind.Exit, false, movementToday).Date == movementToday &&
      StockMovementRules.Validated(MovementInput(StockMovementKind.Entry, 1, date: movementToday.AddDays(-1)), StockMovementKind.Entry, false, movementToday).Date == movementToday.AddDays(-1),
    "Movement date may be today or in the past");
foreach (var futureKind in new[] { StockMovementKind.Entry, StockMovementKind.Exit })
    foreach (var futureIsEdit in new[] { false, true })
    {
        try { StockMovementRules.Validated(MovementInput(futureKind, 1, date: movementToday.AddDays(1), reason: "Corectie"), futureKind, futureIsEdit, movementToday); throw new Exception("Future movement date accepted"); }
        catch (StockMovementOperationException exception)
        {
            Check(exception.Message == StockMovementRules.FutureDateMessage,
                $"A future movement date is rejected ({futureKind}, {(futureIsEdit ? "edit" : "create")})");
        }
    }
try { StockMovementRules.Validated(MovementInput(StockMovementKind.Entry, 1, date: new DateOnly(2099, 1, 1)), StockMovementKind.Entry, false); throw new Exception("Distant future date accepted"); }
catch (StockMovementOperationException) { Check(true, "A date far in the future is rejected against the real current day"); }
Check(AuditNavigation.TargetUrl(new AuditEvent(Guid.NewGuid(), DateTime.UtcNow, "a", "r", AuditEntities.StockMovement, AuditActions.Create, "t", "d", "", "5")) == "/miscari/5" &&
      AuditNavigation.TargetUrl(new AuditEvent(Guid.NewGuid(), DateTime.UtcNow, "a", "r", AuditEntities.StockMovement, AuditActions.Delete, "t", "d", "", "5")) is null,
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
    var overdraw = await stockMovements.CreateAsync(product.Id, MovementInput(StockMovementKind.Exit, 8, "Peste stoc", StockMovementRules.Today));
    Check(overdraw.Stock == -2 && (await movementProducts.GetProductAsync(product.Id))!.Quantity == -2, "An exit may take the stock below zero without blocking");
    try { await stockMovements.CreateAsync(product.Id, MovementInput(StockMovementKind.Entry, 5, "Data viitoare", StockMovementRules.Today.AddDays(1))); throw new Exception("Future movement persisted"); }
    catch (StockMovementOperationException exception)
    {
        Check(exception.Message == StockMovementRules.FutureDateMessage && (await movementProducts.GetProductAsync(product.Id))!.Quantity == -2,
            "SQLite rejects a movement dated in the future and leaves the stock unchanged");
    }

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
    Check(ascending.Items.Select(m => m.Date).SequenceEqual(ascending.Items.Select(m => m.Date).Order()) && ascending.Items.Last().Date == StockMovementRules.Today,
        "Movements are ordered by their own date, not by the time they were entered");
    var descendingPage = await stockMovements.GetPageAsync(product.Id, new StockMovementQuery(null, true, 1, 0));
    Check(descendingPage.Items.First().Date == StockMovementRules.Today, "Descending order lists the latest date first");
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
    var kindStays = await stockMovements.UpdateAsync(edited.Movement, MovementInput(StockMovementKind.Exit, 11, "Factura 1 corectata", new DateOnly(2026, 9, 1), reason: "Schimb tip", noDestination: true));
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

// Exit destinations, sources and stock held by vehicles.
{
    StockMovementOperationException? Expect(Action operation)
    {
        try { operation(); }
        catch (StockMovementOperationException exception) { return exception; }
        return null;
    }
    StockMovementOperationException? Refused(StockMovementInput input, StockMovementKind kind = StockMovementKind.Exit) =>
        Expect(() => StockMovementRules.Validated(input, kind, false));
    var day = new DateOnly(2026, 9, 24);
    StockMovementInput Exit(ExitDestination? destination, int? beneficiaryId = null, int? projectId = null, int? vehicleId = null, int? sourceId = null) =>
        new() { Kind = StockMovementKind.Exit, Quantity = 1, Description = "Test", Date = day, Destination = destination,
                BeneficiaryId = beneficiaryId, ProjectId = projectId, VehicleId = vehicleId, SourceVehicleId = sourceId };

    // Rules without a database.
    Check(Refused(Exit(null))?.Message == StockMovementRules.DestinationRequiredMessage, "An exit without a destination is refused");
    Check(Refused(Exit((ExitDestination)9)) is not null, "An unknown destination is refused");
    Check(Refused(Exit(ExitDestination.Beneficiary)) is not null && Refused(Exit(ExitDestination.Beneficiary, beneficiaryId: 3)) is null &&
          Refused(Exit(ExitDestination.Beneficiary, beneficiaryId: 3, projectId: 4)) is null && Refused(Exit(ExitDestination.Beneficiary, beneficiaryId: 3, vehicleId: 2)) is not null,
        "The beneficiary destination requires a beneficiary (the project is optional) and refuses a vehicle");
    Check(Refused(Exit(ExitDestination.Vehicle)) is not null && Refused(Exit(ExitDestination.Vehicle, vehicleId: 2)) is null &&
          Refused(Exit(ExitDestination.Vehicle, vehicleId: 2, beneficiaryId: 3)) is not null,
        "The vehicle destination requires a vehicle and refuses a beneficiary or project");
    Check(Refused(Exit(ExitDestination.GenericSale)) is null && Refused(Exit(ExitDestination.StockCorrection)) is null &&
          Refused(Exit(ExitDestination.GenericSale, vehicleId: 2)) is not null && Refused(Exit(ExitDestination.StockCorrection, beneficiaryId: 3)) is not null,
        "A generic sale and a stock correction have no beneficiary, project or vehicle");
    Check(Refused(Exit(ExitDestination.GenericSale, sourceId: 5)) is null && Refused(Exit(ExitDestination.GenericSale, sourceId: 0)) is not null,
        "The source vehicle is optional for exits (null means the warehouse)");
    Check(Refused(Exit(ExitDestination.Vehicle, vehicleId: 2, sourceId: 5))?.Message == StockMovementRules.TransferBetweenVehiclesMessage,
        "A transfer from one vehicle to another is not done from the exit form");
    Check(Refused(Exit(ExitDestination.GenericSale), StockMovementKind.Entry) is not null &&
          Refused(new StockMovementInput { Kind = StockMovementKind.Entry, Quantity = 1, Description = "Test", Date = day, SourceVehicleId = 5 }, StockMovementKind.Entry) is not null,
        "Destination and source belong to exits only");
    Check(StockMovementRules.Effect(StockMovementKind.Exit, ExitDestination.Vehicle, 5) == 0 && StockMovementRules.Effect(StockMovementKind.Exit, ExitDestination.Beneficiary, 5) == -5 &&
          StockMovementRules.Effect(StockMovementKind.Exit, ExitDestination.GenericSale, 5) == -5 && StockMovementRules.Effect(StockMovementKind.Exit, ExitDestination.StockCorrection, 5) == -5 &&
          StockMovementRules.Effect(StockMovementKind.Exit, null, 5) == -5 && StockMovementRules.Effect(StockMovementKind.Entry, null, 5) == 5,
        "Only a transfer into a vehicle leaves the total stock unchanged");
    var suggestionDay = new DateOnly(2026, 9, 25);
    Check(StockMovementRules.SuggestedDescription(ExitDestination.Vehicle, "HD-01-FDG", suggestionDay) == "Completare stoc mașină HD-01-FDG 25.09.2026" &&
          StockMovementRules.SuggestedDescription(ExitDestination.StockCorrection, null, suggestionDay) == "Corecție stoc 25.09.2026" &&
          StockMovementRules.SuggestedDescription(ExitDestination.Vehicle, null, suggestionDay) is null &&
          StockMovementRules.SuggestedDescription(ExitDestination.Beneficiary, null, suggestionDay) is null &&
          StockMovementRules.SuggestedDescription(ExitDestination.GenericSale, null, suggestionDay) is null,
        "The description is suggested for a vehicle transfer and a stock correction, with today's date as dd.MM.yyyy");
    Check(StockMovementRules.ApplySuggestion("", null, "Corecție stoc 25.09.2026") == "Corecție stoc 25.09.2026" &&
          StockMovementRules.ApplySuggestion("Corecție stoc 25.09.2026", "Corecție stoc 25.09.2026", "Completare stoc mașină B-1-ABC 25.09.2026") == "Completare stoc mașină B-1-ABC 25.09.2026" &&
          StockMovementRules.ApplySuggestion("Corecție stoc 25.09.2026", "Corecție stoc 25.09.2026", null) == "" &&
          StockMovementRules.ApplySuggestion("Text scris de mine", "Corecție stoc 25.09.2026", "Completare stoc mașină B-1-ABC 25.09.2026") == "Text scris de mine" &&
          StockMovementRules.ApplySuggestion("Text scris de mine", null, null) == "Text scris de mine",
        "A suggested description replaces only an empty or unchanged suggestion, never the user's text");
    Check(StockMovementRules.StockBreakdown(10, 4) == "10 produse: 6 în depozit, 4 în vehicule" && StockMovementRules.StockBreakdown(1, 1) == "1 produs: 0 în depozit, 1 în vehicule" &&
          StockMovementRules.StockBreakdown(10, 0) is null && StockMovementRules.WarehouseStock(10, 4) == 6 && StockMovementRules.WarehouseStock(-2, 0) == -2,
        "The stock is split into warehouse and vehicles only when vehicles hold pieces");

    var vehicleStockRoot = Path.Combine(Path.GetTempPath(), "blazorstoc-vehicle-stock-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(vehicleStockRoot);
    try
    {
        var vsConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["App:LocalDatabasePath"] = Path.Combine(vehicleStockRoot, "stock.db"),
            ["App:ProductImagesPath"] = Path.Combine(vehicleStockRoot, "product-images"),
            ["App:ArchiveFilesPath"] = Path.Combine(vehicleStockRoot, "archive-files"),
            ["App:AuditPath"] = Path.Combine(vehicleStockRoot, "legacy-audit.jsonl")
        }).Build();
        var vsEnvironment = new TestWebHostEnvironment(vehicleStockRoot);
        var vsAccess = new TestAccessControl(true, "operator.masini");
        var vsStore = new SqliteLocalStore(vsEnvironment, vsConfiguration, NullLogger<SqliteLocalStore>.Instance);
        var vsProducts = new SqliteProductRepository(vsStore, vsAccess);
        var vsVehicles = new SqliteVehicleRepository(vsStore, vsAccess);
        var vsMovements = new SqliteStockMovementRepository(vsStore, vsAccess);
        var vsBeneficiaries = new SqliteBeneficiaryRepository(vsStore, vsAccess);
        var vsAudit = new SqliteAuditTrail(vsStore);
        var vsProduct = await CreateProductAsync(vsProducts, new ProductInput { Name = "Produs in masini", Category = "Masini", Subcategory = "Test" });
        var otherProduct = await CreateProductAsync(vsProducts, new ProductInput { Name = "Alt produs in masini", Category = "Masini", Subcategory = "Test" });
        var van = await vsVehicles.CreateAsync(new VehicleInput { PlateNumber = "HD-01-FDG", Description = "Dacia Dokker" });
        var truck = await vsVehicles.CreateAsync(new VehicleInput { PlateNumber = "B-123-ABC", Description = "Autoutilitara" });
        var xBeneficiary = await vsBeneficiaries.CreateAsync(new BeneficiaryInput { Name = "Beneficiar masini", Cui = "RO17777771" });
        var allMovements = new StockMovementQuery(null, false, 1, 0);
        StockMovementInput InputOf(StockMovementKind kind, int quantity, ExitDestination? destination = null, int? vehicleId = null, int? sourceId = null,
            string description = "Test", int? beneficiaryId = null, string reason = "") =>
            new() { Kind = kind, Quantity = quantity, Description = description, Date = day, Destination = destination,
                    VehicleId = vehicleId, SourceVehicleId = sourceId, BeneficiaryId = beneficiaryId, Reason = reason };
        async Task<StockMovementOperationException?> RefusedAsync(Func<Task> operation)
        {
            try { await operation(); }
            catch (StockMovementOperationException exception) { return exception; }
            return null;
        }
        await vsMovements.CreateAsync(vsProduct.Id, InputOf(StockMovementKind.Entry, 10));
        Check((await vsMovements.GetVehicleStocksAsync(vsProduct.Id)).Count == 0 && (await vsMovements.GetPageAsync(vsProduct.Id, allMovements)).InVehicles == 0,
            "Before any transfer no vehicle holds the product");

        // Warehouse -> vehicle: the total does not change, the vehicle holds the pieces.
        var transferIn = await vsMovements.CreateAsync(vsProduct.Id, InputOf(StockMovementKind.Exit, 4, ExitDestination.Vehicle, vehicleId: van.Id, description: "Completare stoc masina"));
        Check(transferIn.Stock == 10 && transferIn.Movement.Effect == 0 && transferIn.Movement.VehiclePlate == "HD-01-FDG" && transferIn.Movement.Destination == ExitDestination.Vehicle &&
              transferIn.Movement.SourceVehicleId is null && (await vsProducts.GetProductAsync(vsProduct.Id))!.Quantity == 10,
            "An exit to a vehicle does not lower the total stock");
        var afterTransfer = await vsMovements.GetPageAsync(vsProduct.Id, allMovements);
        Check(afterTransfer.Stock == 10 && afterTransfer.InVehicles == 4 && StockMovementRules.StockBreakdown(afterTransfer.Stock, afterTransfer.InVehicles) == "10 produse: 6 în depozit, 4 în vehicule",
            "The page reports the part of the stock held by vehicles");
        var heldByVan = await vsMovements.GetVehicleStocksAsync(vsProduct.Id);
        Check(heldByVan.Count == 1 && heldByVan[0].VehicleId == van.Id && heldByVan[0].Quantity == 4 && heldByVan[0].PlateNumber == "HD-01-FDG",
            "The vehicle stock lists the vehicle with its quantity");
        var inVehiclesByProduct = await vsMovements.GetQuantitiesInVehiclesAsync();
        Check(inVehiclesByProduct.Count == 1 && inVehiclesByProduct[vsProduct.Id] == 4, "The catalog can read the quantity held by vehicles per product");
        Check((await vsMovements.GetMovementCountsByVehicleAsync())[van.Id] == 1, "The movement count per vehicle is available for the vehicles page");
        try { await vsVehicles.DeleteAsync(van, "Motiv"); throw new Exception("Vehicle with movements deleted"); }
        catch (VehicleOperationException exception) { Check(exception.Message.Contains("mișcări de stoc"), "A vehicle used by a movement cannot be deleted"); }

        // Vehicle -> use: never more than the vehicle holds.
        var tooMuch = await RefusedAsync(() => vsMovements.CreateAsync(vsProduct.Id, InputOf(StockMovementKind.Exit, 5, ExitDestination.GenericSale, sourceId: van.Id)));
        Check(tooMuch?.Message == StockMovementRules.NotEnoughInVehicleMessage("HD-01-FDG", 4) && tooMuch.Message.Contains("numai 4 bucăți"),
            "An exit from a vehicle cannot exceed the quantity held by that vehicle");
        var emptyVehicle = await RefusedAsync(() => vsMovements.CreateAsync(vsProduct.Id, InputOf(StockMovementKind.Exit, 1, ExitDestination.GenericSale, sourceId: truck.Id)));
        Check(emptyVehicle?.Message == StockMovementRules.NotEnoughInVehicleMessage("B-123-ABC", 0), "A vehicle without the product cannot supply it");
        Check((await vsProducts.GetProductAsync(vsProduct.Id))!.Quantity == 10, "Refused exits do not change the stock");
        var usedFromVan = await vsMovements.CreateAsync(vsProduct.Id, InputOf(StockMovementKind.Exit, 3, ExitDestination.Beneficiary, sourceId: van.Id, beneficiaryId: xBeneficiary.Id));
        var afterUse = await vsMovements.GetPageAsync(vsProduct.Id, allMovements);
        Check(usedFromVan.Stock == 7 && usedFromVan.Movement.SourceVehiclePlate == "HD-01-FDG" && afterUse.InVehicles == 1 &&
              (await vsMovements.GetVehicleStocksAsync(vsProduct.Id)).Single().Quantity == 1,
            "Using pieces from a vehicle lowers the total and that vehicle, not the warehouse");
        var toTruck = await vsMovements.CreateAsync(vsProduct.Id, InputOf(StockMovementKind.Exit, 2, ExitDestination.Vehicle, vehicleId: truck.Id));
        var twoVehicles = await vsMovements.GetPageAsync(vsProduct.Id, allMovements);
        Check(toTruck.Stock == 7 && twoVehicles.InVehicles == 3 && (await vsMovements.GetVehicleStocksAsync(vsProduct.Id)).Select(item => item.PlateNumber).SequenceEqual(["B-123-ABC", "HD-01-FDG"]) &&
              StockMovementRules.WarehouseStock(twoVehicles.Stock, twoVehicles.InVehicles) == 4,
            "Several vehicles hold pieces of the same product; the warehouse keeps the rest");

        // Editing and deleting must not leave a vehicle with a negative quantity.
        var shrink = InputOf(StockMovementKind.Exit, 2, ExitDestination.Vehicle, vehicleId: van.Id, reason: "Corectie");
        var shrinkRefused = await RefusedAsync(() => vsMovements.UpdateAsync(transferIn.Movement, shrink));
        Check(shrinkRefused?.Message == StockMovementRules.NegativeVehicleStockMessage("HD-01-FDG"),
            "Lowering a transfer below what was already used from the vehicle is refused");
        var deleteUsed = await RefusedAsync(() => vsMovements.DeleteAsync(transferIn.Movement, "Motiv"));
        Check(deleteUsed?.Message == StockMovementRules.NegativeVehicleStockMessage("HD-01-FDG") && (await vsMovements.GetAsync(transferIn.Movement.Id)) is not null,
            "Deleting a transfer whose pieces were already used is refused and nothing changes");
        // Changing the destination of a transfer recomputes the total: 2 pieces to the truck become a generic sale.
        var toSale = InputOf(StockMovementKind.Exit, 2, ExitDestination.GenericSale, reason: "Nu a fost mutat, a fost vandut");
        var totalBefore = (await vsProducts.GetProductAsync(vsProduct.Id))!.Quantity;
        var changedExit = await vsMovements.UpdateAsync(toTruck.Movement, toSale);
        Check(changedExit.Stock == totalBefore - 2 && changedExit.Movement.Destination == ExitDestination.GenericSale && changedExit.Movement.VehicleId is null &&
              (await vsMovements.GetVehicleStocksAsync(vsProduct.Id)).All(item => item.VehicleId != truck.Id),
            "Changing a transfer into a sale lowers the total and empties the vehicle");
        Check(changedExit.Movement.Modified && (await vsMovements.GetHistoryAsync(changedExit.Movement.Id)).Single().Changes.Contains("Destinație: Autovehicul → Vânzare generică"),
            "The movement history records the changed destination");
        var back = InputOf(StockMovementKind.Exit, 2, ExitDestination.Vehicle, vehicleId: truck.Id, reason: "Revenire la transfer");
        var restored = await vsMovements.UpdateAsync(changedExit.Movement, back);
        Check(restored.Stock == totalBefore, "Changing a sale back into a transfer restores the total");
        Check((await vsMovements.CreateAsync(vsProduct.Id, InputOf(StockMovementKind.Exit, 12, ExitDestination.Vehicle, vehicleId: van.Id))).Stock == 7 &&
              (await vsMovements.GetPageAsync(vsProduct.Id, allMovements)).InVehicles == 15,
            "A transfer beyond the warehouse stock is not blocked (the warehouse stock may become negative)");
        var negativeWarehouse = await vsMovements.GetPageAsync(vsProduct.Id, allMovements);
        Check(StockMovementRules.WarehouseStock(negativeWarehouse.Stock, negativeWarehouse.InVehicles) == -8, "The warehouse quantity is the total minus the vehicles and can be negative");
        // Deleting a use from a vehicle returns the pieces to that vehicle and restores the total.
        var beforeDeleteUse = (await vsProducts.GetProductAsync(vsProduct.Id))!.Quantity;
        var stockAfterDeletingUse = await vsMovements.DeleteAsync(usedFromVan.Movement, "Test automat");
        Check(stockAfterDeletingUse == beforeDeleteUse + 3 && (await vsMovements.GetVehicleStocksAsync(vsProduct.Id)).Single(item => item.VehicleId == van.Id).Quantity == 16,
            "Deleting a use from a vehicle restores the vehicle quantity and the total");

        // Journal, archive, legacy exits.
        var journal = (await vsAudit.GetEventsAsync()).Where(entry => entry.EntityType == AuditEntities.StockMovement && entry.EntityId == transferIn.Movement.Id.ToString()).ToArray();
        Check(journal.Any(entry => entry.Action == AuditActions.Create && entry.Details.Contains("Destinație: Autovehicul") && entry.Details.Contains("Vehicul: HD-01-FDG") && entry.Details.Contains("Sursă: Depozit")),
            "The journal records the destination, the vehicle and the source of a movement");
        var usedEvent = (await vsAudit.GetEventsAsync()).Single(entry => entry.EntityType == AuditEntities.StockMovement && entry.EntityId == usedFromVan.Movement.Id.ToString() && entry.Action == AuditActions.Create);
        Check(usedEvent.Details.Contains("Sursă: Mașina HD-01-FDG") && usedEvent.Details.Contains("Destinație: Beneficiar"), "The journal records a source vehicle");
        await using (var connection = await vsStore.OpenConnectionAsync())
        {
            await using (var archived = TestSqliteCommand(connection,
                "SELECT destination,vehicle_id,source_vehicle_id FROM archive_stock_movements WHERE original_id=@id", ("@id", usedFromVan.Movement.Id)))
            await using (var reader = await archived.ExecuteReaderAsync())
                Check(await reader.ReadAsync() && reader.GetInt32(0) == (int)ExitDestination.Beneficiary && reader.IsDBNull(1) && reader.GetInt32(2) == van.Id,
                    "The archive of a deleted movement keeps its destination and vehicles");
            var legacyProduct = await CreateProductAsync(vsProducts, new ProductInput { Name = "Produs iesire veche", Category = "Masini", Subcategory = "Test" });
            await using var legacyRow = TestSqliteCommand(connection, """
                INSERT INTO stock_movements(product_id,quantity,created_utc,kind,movement_date,description,operator,version,updated_utc)
                VALUES(@product,1,@now,0,'2026-09-20','Iesire veche','sistem',0,@now); SELECT last_insert_rowid();
                """, ("@product", legacyProduct.Id), ("@now", DateTime.UtcNow.ToString("O")));
            var legacyId = Convert.ToInt32(await legacyRow.ExecuteScalarAsync());
            var xLegacy = (await vsMovements.GetAsync(legacyId))!;
            Check(xLegacy.Destination is null && xLegacy.Effect == -1 && StockMovementInput.From(xLegacy).Destination is null &&
                  StockMovementInput.From(xLegacy with { BeneficiaryId = 1 }).Destination == ExitDestination.Beneficiary,
                "An exit recorded before destinations existed keeps its effect and has no destination");
            var xLegacyEdit = StockMovementInput.From(xLegacy); xLegacyEdit.Quantity = 2; xLegacyEdit.Reason = "Completare";
            Check((await RefusedAsync(() => vsMovements.UpdateAsync(xLegacy, xLegacyEdit)))?.Message == StockMovementRules.DestinationRequiredMessage,
                "Editing an older exit requires choosing its destination");
        }

        // Two sessions cannot use the same pieces of a vehicle twice.
        var concurrentVehicle = await vsVehicles.CreateAsync(new VehicleInput { PlateNumber = "CJ-77-XYZ", Description = "Masina concurenta" });
        await vsMovements.CreateAsync(otherProduct.Id, InputOf(StockMovementKind.Entry, 10));
        await vsMovements.CreateAsync(otherProduct.Id, InputOf(StockMovementKind.Exit, 4, ExitDestination.Vehicle, vehicleId: concurrentVehicle.Id));
        var raced = await Task.WhenAll(Enumerable.Range(0, 4).Select(async index =>
        {
            try
            {
                await new SqliteStockMovementRepository(vsStore, vsAccess).CreateAsync(otherProduct.Id,
                    InputOf(StockMovementKind.Exit, 3, ExitDestination.GenericSale, sourceId: concurrentVehicle.Id, description: "Concurent " + index));
                return true;
            }
            catch (Exception exception) when (exception is StockMovementOperationException or Microsoft.Data.Sqlite.SqliteException) { return false; }
        }));
        Check(raced.Count(created => created) == 1 && (await vsMovements.GetVehicleStocksAsync(otherProduct.Id)).Single().Quantity == 1,
            "Two sessions cannot take more from a vehicle than it holds");
        Check((await vsProducts.GetProductAsync(otherProduct.Id))!.Quantity == (await vsMovements.GetPageAsync(otherProduct.Id, allMovements)).Items.Sum(m => m.Effect),
            "The total stock equals the sum of the movement effects");
        await Rejected(async () => await vsProducts.DeleteAsync((await vsProducts.GetProductAsync(otherProduct.Id))!, "Test automat"), "A product with pieces in vehicles cannot be deleted");

        // Restart: vehicle quantities are derived from the stored movements.
        var vsRestarted = new SqliteLocalStore(vsEnvironment, vsConfiguration, NullLogger<SqliteLocalStore>.Instance);
        var restartedStocks = await new SqliteStockMovementRepository(vsRestarted, vsAccess).GetVehicleStocksAsync(vsProduct.Id);
        Check(restartedStocks.Count == 2 && restartedStocks.Sum(item => item.Quantity) == (await vsMovements.GetPageAsync(vsProduct.Id, allMovements)).InVehicles,
            "The quantities held by vehicles survive a restart");
    }
    finally
    {
        try { Directory.Delete(vehicleStockRoot, true); } catch (IOException) { }
    }
}

// Vehicle page: equipment held by a vehicle, returns to the warehouse and moves between vehicles.
{
    Check(VehicleNavigation.PageUrl(7) == "/vehicule/7" && VehicleNavigation.EquipmentUrl(7) == "/vehicule/7/echipamente",
        "The vehicle page and its equipment page have stable routes");
    Check(StockMovementRules.Effect(StockMovementKind.Exit, ExitDestination.WarehouseReturn, 5) == 0 &&
          StockMovementRules.DestinationLabel(ExitDestination.WarehouseReturn) == "Restituire în depozit" &&
          StockMovementRules.TransferDescription("HD-01-FDG", null, new DateOnly(2026, 9, 25)) == "Restituire în depozit din mașina HD-01-FDG 25.09.2026" &&
          StockMovementRules.TransferDescription("HD-01-FDG", "B-123-ABC", new DateOnly(2026, 9, 25)) == "Mutare din mașina HD-01-FDG în mașina B-123-ABC 25.09.2026",
        "A return to the warehouse leaves the total unchanged and both transfers have a dated description");
    StockMovementOperationException? BadTransfer(VehicleTransfer transfer)
    {
        try { StockMovementRules.ValidateTransfer(transfer); return null; }
        catch (StockMovementOperationException exception) { return exception; }
    }
    Check(BadTransfer(new(1, 1)) is { } sameVehicle && sameVehicle.Message == StockMovementRules.SameVehicleMessage &&
          BadTransfer(new(1, 2)) is null && BadTransfer(new(1, null)) is null && BadTransfer(new(0, null)) is not null &&
          BadTransfer(new(1, null, [])) is not null && BadTransfer(new(1, null, [new(5, 1), new(5, 2)])) is not null &&
          BadTransfer(new(1, null, [new(5, 0)])) is not null && BadTransfer(new(1, null, [new(5, StockMovementRules.MaxQuantity + 1)])) is not null,
        "A transfer needs a valid source, another target vehicle, distinct products and quantities in range");

    var pageRoot = Path.Combine(Path.GetTempPath(), "blazorstoc-vehicle-page-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(pageRoot);
    try
    {
        var pgConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["App:LocalDatabasePath"] = Path.Combine(pageRoot, "page.db"),
            ["App:ProductImagesPath"] = Path.Combine(pageRoot, "product-images"),
            ["App:ArchiveFilesPath"] = Path.Combine(pageRoot, "archive-files"),
            ["App:AuditPath"] = Path.Combine(pageRoot, "legacy-audit.jsonl")
        }).Build();
        var pgStore = new SqliteLocalStore(new TestWebHostEnvironment(pageRoot), pgConfiguration, NullLogger<SqliteLocalStore>.Instance);
        var pgAccess = new TestAccessControl(true, "operator.pagina");
        var pgProducts = new SqliteProductRepository(pgStore, pgAccess);
        var pgVehicles = new SqliteVehicleRepository(pgStore, pgAccess);
        var pgMovements = new SqliteStockMovementRepository(pgStore, pgAccess);
        var pgAudit = new SqliteAuditTrail(pgStore);
        var pgAll = new StockMovementQuery(null, false, 1, 0);
        var productB = await CreateProductAsync(pgProducts, new ProductInput { Name = "B produs vehicul", Category = "Pagina", Subcategory = "Test" });
        var productA = await CreateProductAsync(pgProducts, new ProductInput { Name = "a produs vehicul", Category = "Pagina", Subcategory = "Test" });
        var carOne = await pgVehicles.CreateAsync(new VehicleInput { PlateNumber = "HD-01-FDG", Description = "Prima masina" });
        var carTwo = await pgVehicles.CreateAsync(new VehicleInput { PlateNumber = "B-123-ABC", Description = "A doua masina" });
        StockMovementInput Entry(int quantity) => new() { Kind = StockMovementKind.Entry, Quantity = quantity, Description = "Test", Date = new DateOnly(2026, 9, 24) };
        StockMovementInput ToVehicle(int quantity, int vehicleId) => new()
            { Kind = StockMovementKind.Exit, Quantity = quantity, Description = "Completare", Date = new DateOnly(2026, 9, 24), Destination = ExitDestination.Vehicle, VehicleId = vehicleId };
        async Task<StockMovementOperationException?> Failed(Func<Task> operation)
        {
            try { await operation(); }
            catch (StockMovementOperationException exception) { return exception; }
            return null;
        }
        async Task<int> Total(int productId) => (await pgProducts.GetProductAsync(productId))!.Quantity;
        async Task<int> Held(int productId, int vehicleId) =>
            (await pgMovements.GetVehicleStocksAsync(productId)).FirstOrDefault(item => item.VehicleId == vehicleId)?.Quantity ?? 0;
        await pgMovements.CreateAsync(productA.Id, Entry(10));
        await pgMovements.CreateAsync(productB.Id, Entry(5));
        await pgMovements.CreateAsync(productA.Id, ToVehicle(4, carOne.Id));
        await pgMovements.CreateAsync(productB.Id, ToVehicle(3, carOne.Id));

        Check((await pgVehicles.GetAsync(carOne.Id))?.PlateNumber == "HD-01-FDG" && await pgVehicles.GetAsync(9999) is null, "A vehicle is read by its identifier");
        var equipment = await pgMovements.GetVehicleEquipmentAsync(carOne.Id);
        Check(equipment.Select(item => (item.ProductCode, item.Quantity)).SequenceEqual([("a produs vehicul", 4), ("B produs vehicul", 3)]) &&
              (await pgMovements.GetVehicleEquipmentAsync(carTwo.Id)).Count == 0,
            "The equipment page lists the products of the vehicle by code, with their quantities");

        // Partial return to the warehouse.
        var returned = await pgMovements.TransferFromVehicleAsync(new(carOne.Id, null, [new(productA.Id, 1)]));
        var returnedMovement = returned.Single();
        Check(returnedMovement.Destination == ExitDestination.WarehouseReturn && returnedMovement.SourceVehicleId == carOne.Id && returnedMovement.VehicleId is null &&
              returnedMovement.Effect == 0 && returnedMovement.IsVehicleTransfer && returnedMovement.Quantity == 1 &&
              returnedMovement.Description == "Restituire in depozit din masina HD-01-FDG " + StockMovementRules.DisplayDate(StockMovementRules.Today) &&
              returnedMovement.Operator == "operator.pagina",
            "Returning pieces to the warehouse records one movement with the vehicle as source");
        Check(await Total(productA.Id) == 10 && await Held(productA.Id, carOne.Id) == 3 &&
              (await pgMovements.GetPageAsync(productA.Id, pgAll)).InVehicles == 3,
            "A return keeps the total stock and moves the pieces from the vehicle to the warehouse");
        // Partial move into another vehicle.
        var moved = (await pgMovements.TransferFromVehicleAsync(new(carOne.Id, carTwo.Id, [new(productA.Id, 2)]))).Single();
        Check(moved.Destination == ExitDestination.Vehicle && moved.VehicleId == carTwo.Id && moved.SourceVehicleId == carOne.Id && moved.Effect == 0 &&
              moved.Description == "Mutare din masina HD-01-FDG in masina B-123-ABC " + StockMovementRules.DisplayDate(StockMovementRules.Today) &&
              await Held(productA.Id, carOne.Id) == 1 && await Held(productA.Id, carTwo.Id) == 2 && await Total(productA.Id) == 10 &&
              (await pgMovements.GetPageAsync(productA.Id, pgAll)).InVehicles == 3,
            "Moving pieces changes the vehicles and leaves the total and the sum held by vehicles unchanged");
        // Refusals change nothing.
        var movementCount = (await pgMovements.GetPageAsync(productA.Id, pgAll)).TotalCount + (await pgMovements.GetPageAsync(productB.Id, pgAll)).TotalCount;
        var tooMany = await Failed(() => pgMovements.TransferFromVehicleAsync(new(carOne.Id, null, [new(productA.Id, 5)])));
        Check(tooMany?.Message == "a produs vehicul: " + StockMovementRules.NotEnoughInVehicleMessage("HD-01-FDG", 1), "A quantity above what the vehicle holds is refused");
        Check((await Failed(() => pgMovements.TransferFromVehicleAsync(new(carOne.Id, carOne.Id))))?.Message == StockMovementRules.SameVehicleMessage &&
              (await Failed(() => pgMovements.TransferFromVehicleAsync(new(carOne.Id, 9999))))?.Message == StockMovementRules.VehicleMissingMessage &&
              (await Failed(() => pgMovements.TransferFromVehicleAsync(new(9999, null))))?.Message == StockMovementRules.VehicleMissingMessage &&
              (await Failed(() => pgMovements.TransferFromVehicleAsync(new(carOne.Id, null, [new(9999, 1)])))) is not null,
            "Moving to the same vehicle, from or to a missing vehicle, or a missing product is refused");
        var atomic = await Failed(() => pgMovements.TransferFromVehicleAsync(new(carOne.Id, null, [new(productA.Id, 1), new(productB.Id, 99)])));
        Check(atomic is not null && (await pgMovements.GetPageAsync(productA.Id, pgAll)).TotalCount + (await pgMovements.GetPageAsync(productB.Id, pgAll)).TotalCount == movementCount &&
              await Held(productA.Id, carOne.Id) == 1 && await Held(productB.Id, carOne.Id) == 3,
            "A transfer with one impossible line is refused as a whole and nothing changes");

        // Everything back to the warehouse, atomically.
        var everything = await pgMovements.TransferFromVehicleAsync(new(carOne.Id, null));
        Check(everything.Count == 2 && everything.All(movement => movement.Destination == ExitDestination.WarehouseReturn) &&
              (await pgMovements.GetVehicleEquipmentAsync(carOne.Id)).Count == 0 && await Total(productA.Id) == 10 && await Total(productB.Id) == 5 &&
              (await Failed(() => pgMovements.TransferFromVehicleAsync(new(carOne.Id, null))))?.Message == StockMovementRules.NothingToTransferMessage,
            "Returning everything empties the vehicle, keeps the totals and an empty vehicle has nothing to transfer");
        // Everything into another vehicle.
        var allMoved = await pgMovements.TransferFromVehicleAsync(new(carTwo.Id, carOne.Id));
        Check(allMoved.Count == 1 && allMoved[0].VehicleId == carOne.Id && (await pgMovements.GetVehicleEquipmentAsync(carTwo.Id)).Count == 0 &&
              (await pgMovements.GetVehicleEquipmentAsync(carOne.Id)).Single().Quantity == 2, "Moving everything into another vehicle transfers every product");

        // Editing and deleting these movements keeps the vehicles non-negative.
        var editMove = StockMovementInput.From(allMoved[0]); editMove.Quantity = 1; editMove.Reason = "Corectie cantitate";
        var edited = await pgMovements.UpdateAsync(allMoved[0], editMove);
        Check(edited.Movement.Quantity == 1 && edited.Movement.IsVehicleTransfer && edited.Stock == await Total(productA.Id) &&
              await Held(productA.Id, carOne.Id) == 1 && await Held(productA.Id, carTwo.Id) == 1,
            "A move between vehicles can be edited from the movements page (destination and source stay, quantity changes)");
        Check((await Failed(() => pgMovements.DeleteAsync(returnedMovement, "Motiv"))) is null && await Total(productA.Id) == 10,
            "A return can be deleted; the total stock does not change");
        // Concurrent whole-vehicle moves: the second one finds nothing left.
        var raced = await Task.WhenAll(Enumerable.Range(0, 3).Select(async _ =>
        {
            try { await new SqliteStockMovementRepository(pgStore, pgAccess).TransferFromVehicleAsync(new(carOne.Id, carTwo.Id)); return true; }
            catch (Exception exception) when (exception is StockMovementOperationException or Microsoft.Data.Sqlite.SqliteException) { return false; }
        }));
        Check(raced.Count(done => done) == 1, "Two sessions cannot move the same equipment twice");

        var events = (await pgAudit.GetEventsAsync()).Where(entry => entry.EntityType == AuditEntities.StockMovement && entry.Action == AuditActions.Create).ToArray();
        Check(events.Any(entry => entry.EntityId == returnedMovement.Id.ToString() && entry.Details.Contains("Destinație: Restituire în depozit") &&
                                  entry.Details.Contains("Sursă: Mașina HD-01-FDG") && entry.ActorUsername == "operator.pagina") &&
              events.Any(entry => entry.EntityId == moved.Id.ToString() && entry.Details.Contains("Vehicul: B-123-ABC")),
            "Each return and move is journalled with the operator, the destination, the vehicle and the source");
        try { await pgVehicles.DeleteAsync(carOne, "Motiv"); throw new Exception("Vehicle with transfers deleted"); }
        catch (VehicleOperationException) { Check(true, "A vehicle with transfer movements cannot be deleted"); }
    }
    finally
    {
        try { Directory.Delete(pageRoot, true); } catch (IOException) { }
    }
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

// Task 2 / Subtasks 2.3-2.5: change events, file audit, observation route and navigation context.
var feedRoot = Path.Combine(Path.GetTempPath(), "blazorstoc-feed-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(feedRoot);
try
{
    var feedConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["App:LocalDatabasePath"] = Path.Combine(feedRoot, "feed.db"),
        ["App:ProductImagesPath"] = Path.Combine(feedRoot, "product-images"),
        ["App:ArchiveFilesPath"] = Path.Combine(feedRoot, "archive-files"),
        ["App:AuditPath"] = Path.Combine(feedRoot, "legacy-audit.jsonl")
    }).Build();
    var feedStore = new SqliteLocalStore(new TestWebHostEnvironment(feedRoot), feedConfiguration, NullLogger<SqliteLocalStore>.Instance);
    var feedAccess = new TestAccessControl(true, "operator.feed");
    var feed = new InProcessChangeFeed();
    var feedOrigin = new ChangeOrigin();
    var otherOrigin = new ChangeOrigin();
    var feedFiles = new ChangeNotifyingProjectFileStore(new SqliteProjectFileStore(feedStore, feedAccess), feed, feedOrigin);
    var feedProjects = new ChangeNotifyingProjectRepository(new SqliteProjectRepository(feedStore, feedAccess, null, feedFiles), feed, feedOrigin);
    var feedBeneficiaries = new SqliteBeneficiaryRepository(feedStore, feedAccess);
    var received = new List<ChangeEvent>();
    using var subscription = feed.Subscribe(change => { received.Add(change); return Task.CompletedTask; });

    var feedBeneficiary = await feedBeneficiaries.CreateAsync(new BeneficiaryInput { Name = "Beneficiar feed", Cui = "RO18777771" });
    var feedOtherBeneficiary = await feedBeneficiaries.CreateAsync(new BeneficiaryInput { Name = "Beneficiar feed doi", Cui = "RO18777772" });
    var feedProject = await feedProjects.CreateAsync(new ProjectInput { BeneficiaryId = feedBeneficiary.Id, Name = "Proiect feed", Observations = "" });
    Check(received.Count == 1 && received[0].EntityType == AuditEntities.Project && received[0].Action == AuditActions.Create &&
          received[0].EntityId == feedProject.Id.ToString() && received[0].ProjectId == feedProject.Id &&
          received[0].BeneficiaryId == feedBeneficiary.Id && received[0].Origin == feedOrigin.Id && received[0].OccurredUtc.Kind == DateTimeKind.Utc,
        "Creating a project publishes a change event with identifiers, origin and UTC time");

    try { await feedProjects.CreateAsync(new ProjectInput { BeneficiaryId = feedBeneficiary.Id, Name = "  PROIECT   feed ", Observations = "" }); throw new Exception("Duplicate project accepted"); }
    catch (ProjectOperationException) { Check(received.Count == 1, "A rejected project operation publishes no event"); }

    var feedObservation = await feedProjects.CreateObservationAsync(feedProject.Id, new ProjectObservationInput { Name = "Observatie feed", Content = "Text" }, "operator.feed");
    Check(received.Count == 2 && received[1].EntityType == AuditEntities.ProjectObservation && received[1].Action == AuditActions.Create &&
          received[1].ProjectId == feedProject.Id && received[1].ObservationId == feedObservation.Id, "Creating an observation publishes its project and observation ids");

    var feedFile = await feedFiles.SaveAsync(feedObservation.Id, "nota.txt", "text/plain", "Continut secret de fisier"u8.ToArray(), "operator.feed");
    Check(received.Count == 3 && received[2].EntityType == AuditEntities.ProjectObservationFile && received[2].Action == AuditActions.Create &&
          received[2].EntityId == feedFile.Id.ToString() && received[2].ObservationId == feedObservation.Id, "Saving a file publishes the file and its observation");

    var feedObservationEdit = ProjectObservationInput.From(feedObservation); feedObservationEdit.Content = "Text nou"; feedObservationEdit.Reason = "Corectie";
    var editedFeedObservation = await feedProjects.UpdateObservationAsync(feedObservation, feedObservationEdit);
    Check(received.Count == 4 && received[3].Action == AuditActions.Edit && received[3].ObservationId == feedObservation.Id, "Editing an observation publishes an edit event");

    var projectMove = ProjectInput.From(feedProject); projectMove.BeneficiaryId = feedOtherBeneficiary.Id; projectMove.Reason = "Mutare";
    try { await feedProjects.UpdateAsync(feedProject, projectMove); throw new Exception("Project moved to another beneficiary"); }
    catch (ProjectOperationException exception)
    {
        Check(exception.Message == ProjectRules.BeneficiaryLockedMessage && received.Count == 4 &&
              (await feedProjects.GetAsync(feedProject.Id))!.BeneficiaryId == feedBeneficiary.Id,
            "The server rejects moving a project to another beneficiary, leaves it unchanged and publishes no event");
    }
    var projectRename = ProjectInput.From(feedProject); projectRename.Name = "Proiect feed redenumit"; projectRename.Reason = "Corectie";
    var movedProject = await feedProjects.UpdateAsync(feedProject, projectRename);
    var moveEvents = received.Skip(4).ToList();
    Check(moveEvents.Count == 1 && moveEvents[0].Action == AuditActions.Edit && moveEvents[0].ProjectId == feedProject.Id && moveEvents[0].BeneficiaryId == feedBeneficiary.Id,
        "Editing a project notifies its beneficiary");
    Check(ProjectChanges.AffectsBeneficiary(moveEvents[0], moveEvents[0].BeneficiaryId!.Value) && !ProjectChanges.AffectsBeneficiary(received[1], feedBeneficiary.Id),
        "Only project events concern a beneficiary page");

    await feedFiles.DeleteAsync(feedFile.Id, "Fisier inlocuit");
    Check(received.Count == 6 && received[5].EntityType == AuditEntities.ProjectObservationFile && received[5].Action == AuditActions.Delete &&
          received[5].EntityId == feedFile.Id.ToString() && received[5].ObservationId is null, "Removing a file publishes a deletion whose observation is unknown");
    await feedProjects.DeleteObservationAsync(editedFeedObservation, "Observatie stearsa");
    Check(received.Count == 7 && received[6].EntityType == AuditEntities.ProjectObservation && received[6].Action == AuditActions.Delete &&
          received[6].ObservationId == feedObservation.Id, "Deleting an observation publishes a deletion");
    await feedProjects.DeleteAsync(movedProject, "Proiect sters");
    Check(received.Count == 8 && received[7].EntityType == AuditEntities.Project && received[7].Action == AuditActions.Delete &&
          received[7].BeneficiaryId == feedBeneficiary.Id && ProjectChanges.IsDeletionOf(received[7], AuditEntities.Project, feedProject.Id),
        "Deleting a project publishes a deletion for its current beneficiary");

    var serialized = string.Join('|', received.Select(change => System.Text.Json.JsonSerializer.Serialize(change)));
    Check(!serialized.Contains("Continut secret") && !serialized.Contains("nota.txt") && !serialized.Contains("Proiect feed") && !serialized.Contains("Observatie feed"),
        "Change events carry identifiers only, never names, texts or file contents");

    // Subscriptions: isolation of failing subscribers and unsubscribing.
    var stable = new List<ChangeEvent>();
    var failingFeed = new InProcessChangeFeed();
    using (failingFeed.Subscribe(_ => throw new InvalidOperationException("Subscriber failure")))
    using (failingFeed.Subscribe(_ => Task.FromException(new InvalidOperationException("Async subscriber failure"))))
    using (failingFeed.Subscribe(change => { stable.Add(change); return Task.CompletedTask; }))
        failingFeed.Publish(new(AuditEntities.Project, AuditActions.Create, "1", Guid.NewGuid(), DateTime.UtcNow, ProjectId: 1));
    Check(stable.Count == 1, "A failing subscriber affects neither the publisher nor the other subscribers");
    failingFeed.Publish(new(AuditEntities.Project, AuditActions.Create, "2", Guid.NewGuid(), DateTime.UtcNow, ProjectId: 2));
    Check(stable.Count == 1, "A disposed subscription no longer receives events");
    var doubleDisposed = feed.Subscribe(_ => Task.CompletedTask);
    doubleDisposed.Dispose(); doubleDisposed.Dispose();
    Check(true, "Disposing a subscription twice is harmless");

    // Which pages an event concerns.
    var observationFileEvent = new ChangeEvent(AuditEntities.ProjectObservationFile, AuditActions.Create, "5", otherOrigin.Id, DateTime.UtcNow, ObservationId: 7);
    var unknownFileEvent = observationFileEvent with { ObservationId = null };
    var observationEvent = new ChangeEvent(AuditEntities.ProjectObservation, AuditActions.Edit, "7", otherOrigin.Id, DateTime.UtcNow, ProjectId: 3, ObservationId: 7);
    var projectEvent = new ChangeEvent(AuditEntities.Project, AuditActions.Delete, "3", otherOrigin.Id, DateTime.UtcNow, ProjectId: 3, BeneficiaryId: 2);
    Check(ProjectChanges.AffectsProject(observationFileEvent, 3, new[] { 7 }) && !ProjectChanges.AffectsProject(observationFileEvent, 3, new[] { 8 }) &&
          ProjectChanges.AffectsProject(unknownFileEvent, 3, Array.Empty<int>()), "The project page refreshes for its own observations' files and for unattributed file removals");
    Check(ProjectChanges.AffectsProject(observationEvent, 3, Array.Empty<int>()) && !ProjectChanges.AffectsProject(observationEvent, 4, Array.Empty<int>()) &&
          ProjectChanges.AffectsProject(projectEvent, 3, Array.Empty<int>()) && !ProjectChanges.AffectsProject(projectEvent, 4, Array.Empty<int>()),
        "The project page refreshes only for its own project and observations");
    Check(ProjectChanges.AffectsObservation(observationEvent, 3, 7) && !ProjectChanges.AffectsObservation(observationEvent, 3, 8) &&
          ProjectChanges.AffectsObservation(projectEvent, 3, 7) && !ProjectChanges.AffectsObservation(projectEvent with { Action = AuditActions.Edit }, 3, 7) &&
          ProjectChanges.AffectsObservation(observationFileEvent, 3, 7) && !ProjectChanges.AffectsObservation(observationFileEvent, 3, 8),
        "The observation page refreshes for its own changes and when its project is deleted");
    Check(!ProjectChanges.AffectsBeneficiary(new ChangeEvent(AuditEntities.StockMovement, AuditActions.Create, "1", otherOrigin.Id, DateTime.UtcNow, BeneficiaryId: 2), 2),
        "Events of other entity types never refresh a beneficiary page");

    // File uploads are audited (creation) without exposing the content.
    var auditedFile = await new SqliteProjectFileStore(feedStore, feedAccess).SaveAsync(
        (await new SqliteProjectRepository(feedStore, feedAccess).CreateObservationAsync(
            (await new SqliteProjectRepository(feedStore, feedAccess).CreateAsync(new ProjectInput { BeneficiaryId = feedBeneficiary.Id, Name = "Proiect audit fisier" })).Id,
            new ProjectObservationInput { Name = "Observatie audit fisier" }, "operator.feed")).Id,
        "plan.pdf", "application/pdf", "%PDF-1.4\n%continut\n"u8.ToArray(), "operator.feed");
    await using (var feedConnection = await feedStore.OpenConnectionAsync())
    {
        await using var fileAudit = TestSqliteCommand(feedConnection, """
            SELECT COUNT(*) FROM audit_events WHERE entity_type='FisierObservatie' AND action='Adăugare' AND target='plan.pdf' AND entity_id=@id
              AND details LIKE '%Nume fișier: plan.pdf%' AND details LIKE '%Observație: Observatie audit fisier%' AND details NOT LIKE '%continut%'
            """, ("@id", auditedFile.Id.ToString()));
        Check(Convert.ToInt32(await fileAudit.ExecuteScalarAsync()) == 1, "Uploading an observation file is audited with name, observation and type, without its content");
    }
    try { await new SqliteProjectFileStore(feedStore, feedAccess).SaveAsync(987654, "orfan.txt", "text/plain", "x"u8.ToArray(), "operator.feed"); throw new Exception("File for a missing observation saved"); }
    catch (ProjectOperationException) { Check(Directory.Exists(feedStore.ProjectFilesPath) && Directory.GetFiles(feedStore.ProjectFilesPath).Length == 1, "A file for a missing observation is rejected and leaves no file behind"); }

    // Observation route in the journal registry (Subtask 2.3).
    AuditEvent ObservationAudit(string action, string entityId) =>
        new(Guid.NewGuid(), DateTime.UtcNow, "a", "r", AuditEntities.ProjectObservation, action, "t", "d", "", entityId);
    Check(AuditNavigation.TargetUrl(ObservationAudit(AuditActions.Create, "12")) == "/observatii/12" &&
          AuditNavigation.TargetUrl(ObservationAudit(AuditActions.Edit, "12")) == "/observatii/12" &&
          AuditNavigation.TargetUrl(ObservationAudit(AuditActions.Delete, "12")) is null &&
          AuditNavigation.TargetUrl(ObservationAudit(AuditActions.Create, "abc")) is null &&
          AuditNavigation.TargetUrl(ObservationAudit(AuditActions.Create, "0")) is null,
        "Observation events link to the stable /observatii/{id} route; deleted or invalid ones do not");
    Check(ProjectNavigation.ObservationPageUrl(3, 12) == "/proiecte/3/observatii/12" && ProjectNavigation.ProjectUrl(3) == "/proiecte/3" &&
          AuditNavigation.TargetUrl(new AuditEvent(Guid.NewGuid(), DateTime.UtcNow, "a", "r", AuditEntities.ProjectObservationFile, AuditActions.Create, "t", "d", "", "4")) is null,
        "The observation route resolves under its project; files have no page of their own");

    // Return to the project list of a beneficiary (Subtask 2.3).
    var listContext = new ListNavigationContext();
    Check(listContext.BeneficiaryUrl(5) == "/beneficiari/5", "A beneficiary with no remembered list state links to the plain page");
    listContext.RememberBeneficiaryProjects(5, BeneficiaryProjectListState.From("  hala nord ", 3));
    Check(listContext.BeneficiaryUrl(5) == "/beneficiari/5?q=hala%20nord&pagina=3" && listContext.BeneficiaryUrl(6) == "/beneficiari/6",
        "The remembered filter and page are restored per beneficiary");
    listContext.RememberBeneficiaryProjects(5, BeneficiaryProjectListState.From("hala", 1));
    Check(listContext.BeneficiaryUrl(5) == "/beneficiari/5?q=hala", "The first page is not written to the address");
    listContext.RememberBeneficiaryProjects(5, BeneficiaryProjectListState.From("", null));
    Check(listContext.BeneficiaryUrl(5) == "/beneficiari/5", "Clearing the filter and returning to the first page forgets the state");
    var special = BeneficiaryProjectListState.From("șantier & hală=1?", 0);
    Check(special.Page == 1 && special.Url(9).StartsWith("/beneficiari/9?q=") && !special.Url(9).Contains("&hală") && !special.Url(9).Contains(' ') && !special.Url(9).Contains("pagina"),
        "Special characters are escaped and an invalid page falls back to the first");
}
finally
{
    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    try { Directory.Delete(feedRoot, true); } catch (IOException) { }
}


// ---- Task 8: change events from database triggers, relay, ledger and live refresh ----
var syncRoot = Path.Combine(Path.GetTempPath(), $"blazorstoc-sync-{Guid.NewGuid():N}");
Directory.CreateDirectory(syncRoot);
try
{
    var syncConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["App:LocalDatabasePath"] = Path.Combine(syncRoot, "sync.db"),
        ["App:ProductImagesPath"] = Path.Combine(syncRoot, "product-images"),
        ["App:ProjectFilesPath"] = Path.Combine(syncRoot, "project-files"),
        ["App:ArchiveFilesPath"] = Path.Combine(syncRoot, "archive-files"),
        ["App:AuditPath"] = Path.Combine(syncRoot, "legacy-audit.jsonl")
    }).Build();
    var syncStore = new SqliteLocalStore(new TestWebHostEnvironment(syncRoot), syncConfiguration, NullLogger<SqliteLocalStore>.Instance);
    await syncStore.InitializeAsync();
    var syncSource = new SqliteChangeEventSource(syncStore);
    var startId = await syncSource.LatestIdAsync(default);
    Check((await syncSource.ReadAfterAsync(0, 1000, default)).All(change => change.Id <= startId),
        "The one-time seeding and migrations do not create change events");

    // What an external application would do: plain SQL against the tables, no application code involved.
    await using (var external = await syncStore.OpenConnectionAsync())
    {
        async Task Run(string sql, params (string Name, object? Value)[] parameters)
        {
            await using var command = TestSqliteCommand(external, sql, parameters);
            await command.ExecuteNonQueryAsync();
        }
        var stamp = DateTime.UtcNow.ToString("O");
        await Run("INSERT INTO categories(name,normalized_name) VALUES('Sync','sync')");
        await Run("INSERT INTO subcategories(category_id,name,normalized_name) VALUES((SELECT id FROM categories WHERE normalized_name='sync'),'Sync sub','sync sub')");
        await Run("INSERT INTO products(category_id,subcategory_id,name,normalized_name) VALUES((SELECT id FROM categories WHERE normalized_name='sync'),(SELECT id FROM subcategories WHERE normalized_name='sync sub'),'P-SYNC','p-sync')");
        await Run("UPDATE products SET description='nou' WHERE normalized_name='p-sync'");
        await Run("INSERT INTO stock_movements(product_id,quantity,created_utc) VALUES((SELECT id FROM products WHERE normalized_name='p-sync'),3,@t)", ("@t", stamp));
        await Run("UPDATE stock_movements SET quantity=4 WHERE product_id=(SELECT id FROM products WHERE normalized_name='p-sync')");
        await Run("DELETE FROM stock_movements WHERE product_id=(SELECT id FROM products WHERE normalized_name='p-sync')");
        await Run("INSERT INTO web_users(username,normalized_username,display_name,password_hash,role) VALUES('sync.user','sync.user','Sync','SECRET-HASH-VALUE','Utilizator')");
        await Run("UPDATE web_users SET is_active=0 WHERE normalized_username='sync.user'");
        await Run("INSERT INTO beneficiaries(name,normalized_name,cui,normalized_cui) VALUES('Sync SRL','sync srl','RO99000001','ro99000001')");
        await Run("INSERT INTO projects(beneficiary_id,name,normalized_name,created_utc,updated_utc) VALUES((SELECT id FROM beneficiaries WHERE normalized_name='sync srl'),'Proiect sync','proiect sync',@t,@t)", ("@t", stamp));
        await Run("INSERT INTO project_observations(project_id,name,author,created_utc,updated_utc) VALUES((SELECT id FROM projects WHERE normalized_name='proiect sync'),'Obs sync','ext',@t,@t)", ("@t", stamp));
        await Run("INSERT INTO project_observation_files(observation_id,relative_path,original_name,content_type,byte_length,sha256,author,uploaded_utc) VALUES((SELECT id FROM project_observations WHERE name='Obs sync'),'x','secret-name.txt','text/plain',1,'aa','ext',@t)", ("@t", stamp));
        await Run("DELETE FROM project_observation_files WHERE original_name='secret-name.txt'");
        await Run("UPDATE project_observations SET content='nou' WHERE name='Obs sync'");
        await Run("DELETE FROM project_observations WHERE name='Obs sync'");
        await Run("DELETE FROM projects WHERE normalized_name='proiect sync'");
        await Run("DELETE FROM web_users WHERE normalized_username='sync.user'");
        await Run("DELETE FROM products WHERE normalized_name='p-sync'");
    }
    var recorded = await syncSource.ReadAfterAsync(startId, 1000, default);
    var summary = string.Join("|", recorded.Select(change => $"{change.EntityType}:{change.Action}"));
    Check(summary == string.Join("|", new[]
        {
            "Produs:Adăugare", "Produs:Editare", "Produs:Editare", "Produs:Editare", "Produs:Editare",
            "Utilizator:Adăugare", "Utilizator:Editare", "Proiect:Adăugare", "Observatie:Adăugare", "FisierObservatie:Adăugare",
            "FisierObservatie:Ștergere", "Observatie:Editare", "Observatie:Ștergere", "Proiect:Ștergere", "Utilizator:Ștergere", "Produs:Ștergere"
        }),
        "Triggers record every insert, update and delete of products, movements, users, projects, observations and files");
    var recordedProduct = recorded[0];
    Check(recorded.Take(5).All(change => change.EntityId == recordedProduct.EntityId) && recorded[4].EntityType == AuditEntities.Product,
        "A stock movement is reported as a change of its product");
    var recordedProject = recorded.Single(change => change.EntityType == AuditEntities.Project && change.Action == AuditActions.Create);
    var recordedObservation = recorded.Single(change => change.EntityType == AuditEntities.ProjectObservation && change.Action == AuditActions.Create);
    var recordedFile = recorded.Single(change => change.EntityType == AuditEntities.ProjectObservationFile && change.Action == AuditActions.Create);
    Check(recordedProject.ProjectId == int.Parse(recordedProject.EntityId) && recordedProject.BeneficiaryId is not null &&
          recordedObservation.ProjectId == recordedProject.ProjectId && recordedObservation.ObservationId == int.Parse(recordedObservation.EntityId) &&
          recordedFile.ObservationId == recordedObservation.ObservationId,
        "Events carry the project, observation and beneficiary identifiers that pages filter on");
    var serializedEvents = System.Text.Json.JsonSerializer.Serialize(recorded);
    Check(!serializedEvents.Contains("SECRET-HASH-VALUE") && !serializedEvents.Contains("secret-name") && !serializedEvents.Contains("Proiect sync") && !serializedEvents.Contains("sync.user"),
        "Recorded events never contain passwords, names, texts or file names");
    Check(recorded.Zip(recorded.Skip(1)).All(pair => pair.First.Id < pair.Second.Id) && recorded.All(change => change.CreatedUtc.Kind == DateTimeKind.Utc),
        "Events are ordered by identifier and timestamped in UTC");

    await syncSource.PurgeAsync(startId + 5, DateTime.UtcNow.AddMinutes(1), default);
    var afterPurge = await syncSource.ReadAfterAsync(startId, 1000, default);
    Check(afterPurge.Count == recorded.Count - 5 && afterPurge[0].Id == recorded[5].Id, "Purging removes only processed events up to the given identifier");
    await syncSource.PurgeAsync(long.MaxValue, DateTime.UtcNow.AddDays(-1), default);
    Check((await syncSource.ReadAfterAsync(startId, 1000, default)).Count == afterPurge.Count, "Purging keeps events newer than the retention period");

    var triggerSql = ChangeEventTriggers.MariaTriggerSql(ChangeEventTriggers.Maria.Single(table => table.Table == "project"), "d");
    Check(triggerSql.Contains("AFTER DELETE ON project FOR EACH ROW") && triggerSql.Contains("OLD.id_project") && triggerSql.Contains("OLD.id_beneficiar") &&
          triggerSql.Contains("UTC_TIMESTAMP(6)") && !ChangeEventTriggers.Maria.Any(table => MariaSql(table).Contains("password")),
        "MariaDB triggers use the project table's columns and the server's UTC time and read no secret column");
    string MariaSql(WatchedTable table) => string.Join(' ', ChangeEventTriggers.Suffixes.Select(suffix => ChangeEventTriggers.MariaTriggerSql(table, suffix)));
    Check(ChangeEventTriggers.Maria.Count == ChangeEventTriggers.Sqlite.Count && ChangeEventTriggers.Maria.Select(table => table.EntityType).SequenceEqual(ChangeEventTriggers.Sqlite.Select(table => table.EntityType)),
        "SQLite and MariaDB watch the same entities");
}
finally
{
    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    try { Directory.Delete(syncRoot, true); } catch (IOException) { }
}

// Relay: cursor, grace period, ledger, failures (with an in-memory source and a manual clock).
{
    var clock = new ManualTimeProvider();
    var source = new FakeChangeSource();
    var relayFeed = new InProcessChangeFeed(null, clock);
    var seen = new List<ChangeEvent>();
    using var relaySubscription = relayFeed.Subscribe(change => { lock (seen) seen.Add(change); return Task.CompletedTask; });
    source.Add("Produs", AuditActions.Edit, "1");
    var relay = new ChangeEventRelay(source, relayFeed, NullLogger<ChangeEventRelay>.Instance,
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Sync:GraceMilliseconds"] = "1000" }).Build(), clock);
    Check(await relay.PollOnceAsync(default) == 0 && relay.Cursor == 1, "The relay starts after the newest stored event instead of replaying history");
    source.Add("Produs", AuditActions.Edit, "2");
    source.Add("Utilizator", AuditActions.Create, "7");
    Check(await relay.PollOnceAsync(default) == 0 && seen.Count == 0, "A new event is held back during the grace period");
    clock.Advance(TimeSpan.FromMilliseconds(1100));
    Check(await relay.PollOnceAsync(default) == 2 && seen.Count == 2 && seen[0].EntityId == "2" && seen[1].EntityType == "Utilizator" &&
          seen.All(change => change.Origin == Guid.Empty) && relay.Cursor == 3,
        "After the grace period each event is published once, in order, without an origin");
    clock.Advance(TimeSpan.FromSeconds(5));
    Check(await relay.PollOnceAsync(default) == 0 && seen.Count == 2, "Polling again never publishes an event twice");

    var ownOrigin = Guid.NewGuid();
    relayFeed.Publish(new("Proiect", AuditActions.Edit, "5", ownOrigin, clock.GetUtcNow().UtcDateTime, ProjectId: 5));
    source.Add("Proiect", AuditActions.Edit, "5", projectId: 5);
    source.Add("Proiect", AuditActions.Edit, "5", projectId: 5);
    seen.Clear();
    await relay.PollOnceAsync(default);
    clock.Advance(TimeSpan.FromMilliseconds(1100));
    await relay.PollOnceAsync(default);
    Check(seen.Count == 1 && seen[0].Origin == Guid.Empty, "The trigger's copy of a change already published by a session is dropped once; a second change is still announced");

    relayFeed.Publish(new("Proiect", AuditActions.Delete, "9", ownOrigin, clock.GetUtcNow().UtcDateTime, ProjectId: 9));
    clock.Advance(TimeSpan.FromSeconds(40));
    Check(!relayFeed.TryConsumeLocal("Proiect", AuditActions.Delete, "9"), "A locally published change is forgotten after the ledger lifetime");

    source.FailNextRead = true;
    var cursorBeforeFailure = relay.Cursor;
    source.Add("Produs", AuditActions.Edit, "11");
    try { await relay.PollOnceAsync(default); throw new Exception("Failing source did not throw"); }
    catch (InvalidOperationException) { }
    seen.Clear();
    clock.Advance(TimeSpan.FromMilliseconds(1100));
    await relay.PollOnceAsync(default);
    clock.Advance(TimeSpan.FromMilliseconds(1100));
    await relay.PollOnceAsync(default);
    Check(relay.Cursor > cursorBeforeFailure && seen.Any(change => change.EntityId == "11"), "After a failed read the relay resumes from its cursor and loses nothing");
}

// Live refresh: bursts, own origin, busy forms, fallback.
{
    var liveFeed = new InProcessChangeFeed();
    var own = Guid.NewGuid();
    var refreshes = 0; var renders = 0; var busy = false;
    using var live = new LiveRefresh(liveFeed, own, change => change.EntityType == "Produs" && change.EntityId == "1",
        () => { Interlocked.Increment(ref refreshes); return Task.CompletedTask; }, () => busy,
        () => { Interlocked.Increment(ref renders); return Task.CompletedTask; }, TimeSpan.FromMilliseconds(60));
    for (var i = 0; i < 3; i++) liveFeed.Publish(new("Produs", AuditActions.Edit, "1", Guid.Empty, DateTime.UtcNow));
    liveFeed.Publish(new("Produs", AuditActions.Edit, "2", Guid.Empty, DateTime.UtcNow));
    liveFeed.Publish(new("Produs", AuditActions.Edit, "1", own, DateTime.UtcNow));
    await Task.Delay(400);
    Check(refreshes == 1 && !live.HasPendingChange, "A burst of relevant events refreshes once; other entities and the session's own changes are ignored");

    busy = true;
    liveFeed.Publish(new("Produs", AuditActions.Edit, "1", Guid.Empty, DateTime.UtcNow));
    await Task.Delay(300);
    Check(refreshes == 1 && live.HasPendingChange && renders >= 1, "While a form is open nothing is refreshed and the page is asked to show a notice");
    busy = false;
    await live.ReloadPendingAsync();
    Check(refreshes == 2 && !live.HasPendingChange, "The user's reload applies the pending change and clears the notice");
}
{
    var fallbackRefreshes = 0; var fallbackBusy = true;
    using var fallback = new LiveRefresh(new InProcessChangeFeed(), Guid.NewGuid(), _ => true,
        () => { Interlocked.Increment(ref fallbackRefreshes); return Task.CompletedTask; }, () => fallbackBusy,
        () => Task.CompletedTask, fallbackInterval: TimeSpan.FromMilliseconds(80));
    await Task.Delay(300);
    Check(fallbackRefreshes == 0, "The periodic fallback does not refresh while a form is open");
    fallbackBusy = false;
    await Task.Delay(400);
    Check(fallbackRefreshes >= 1, "The periodic fallback refreshes when the page is idle even if no notification arrived");
}



// ---- Task 9: product edit locks ----
var lockRoot = Path.Combine(Path.GetTempPath(), $"blazorstoc-lock-{Guid.NewGuid():N}");
Directory.CreateDirectory(lockRoot);
try
{
    var lockConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["App:LocalDatabasePath"] = Path.Combine(lockRoot, "locks.db"),
        ["App:ProductImagesPath"] = Path.Combine(lockRoot, "product-images"),
        ["App:ProjectFilesPath"] = Path.Combine(lockRoot, "project-files"),
        ["App:ArchiveFilesPath"] = Path.Combine(lockRoot, "archive-files"),
        ["App:AuditPath"] = Path.Combine(lockRoot, "legacy-audit.jsonl")
    }).Build();
    var lockStore = new SqliteLocalStore(new TestWebHostEnvironment(lockRoot), lockConfiguration, NullLogger<SqliteLocalStore>.Instance);
    await lockStore.InitializeAsync();
    await using (var seedConnection = await lockStore.OpenConnectionAsync())
    {
        foreach (var sql in new[]
                 {
                     "INSERT INTO categories(name,normalized_name) VALUES('Blocari','blocari')",
                     "INSERT INTO subcategories(category_id,name,normalized_name) VALUES((SELECT id FROM categories WHERE normalized_name='blocari'),'Test','blocari test')",
                     "INSERT INTO products(category_id,subcategory_id,name,normalized_name) VALUES((SELECT id FROM categories WHERE normalized_name='blocari'),(SELECT id FROM subcategories WHERE normalized_name='blocari test'),'P-LOCK','p-lock')"
                 })
        {
            await using var command = TestSqliteCommand(seedConnection, sql);
            await command.ExecuteNonQueryAsync();
        }
    }
    int lockedProduct;
    await using (var idConnection = await lockStore.OpenConnectionAsync())
    {
        await using var command = TestSqliteCommand(idConnection, "SELECT id FROM products WHERE normalized_name='p-lock'");
        lockedProduct = Convert.ToInt32(await command.ExecuteScalarAsync());
    }
    async Task ExpireLockAsync(int productId)
    {
        await using var connection = await lockStore.OpenConnectionAsync();
        await using var command = TestSqliteCommand(connection, "UPDATE product_locks SET expires_utc=strftime('%Y-%m-%dT%H:%M:%fZ','now','-5 seconds') WHERE product_id=@p", ("@p", productId));
        await command.ExecuteNonQueryAsync();
    }

    var lockAudit = new SqliteAuditTrail(lockStore);
    var lockAdmin = new TestAccessControl(true, "administrator.demo");
    var locksAna = new SqliteProductLockRepository(lockStore, new TestAccessControl(false, "ana"), lockAudit);
    var locksBob = new SqliteProductLockRepository(lockStore, new TestAccessControl(false, "bob"), lockAudit);
    var locksAdmin = new SqliteProductLockRepository(lockStore, lockAdmin, lockAudit);

    var first = await locksAna.AcquireAsync(lockedProduct, "sesiune-ana");
    Check(first.Acquired && first.Changed && first.Lock!.Owner == "ana" && first.Lock.SessionId == "sesiune-ana" &&
          first.Lock.RemainingSeconds is > 80 and <= ProductLockRules.LeaseSeconds &&
          Math.Abs((first.Lock.ExpiresUtc - DateTime.UtcNow).TotalSeconds - ProductLockRules.LeaseSeconds) < 5 &&
          first.Lock.AcquiredUtc.Kind == DateTimeKind.Utc,
        "Entering edit mode takes a lease lock identified by product, user, session, acquisition and expiry (database UTC time)");
    var refused = await locksBob.AcquireAsync(lockedProduct, "sesiune-bob");
    Check(!refused.Acquired && !refused.Changed && refused.Lock!.Owner == "ana" && refused.Lock.AcquiredUtc == first.Lock!.AcquiredUtc,
        "A second user cannot take the lock and learns who holds it and since when");
    var renewed = await locksAna.AcquireAsync(lockedProduct, "sesiune-ana");
    Check(renewed.Acquired && !renewed.Changed && renewed.Lock!.AcquiredUtc == first.Lock!.AcquiredUtc && renewed.Lock.RenewedUtc >= first.Lock!.RenewedUtc,
        "The heartbeat renews the same lock without changing who holds it");
    Check((await locksBob.GetAsync(lockedProduct))!.Owner == "ana" && (await locksBob.GetActiveAsync()).Count == 1,
        "The product stays readable and its lock is visible to other users");
    Check(!await locksBob.ReleaseAsync(lockedProduct, "sesiune-bob") && (await locksAna.GetAsync(lockedProduct)) is not null,
        "Only the holding session can release a lock");
    Check(await locksAna.ReleaseAsync(lockedProduct, "sesiune-ana") && await locksAna.GetAsync(lockedProduct) is null,
        "Saving, cancelling or closing releases the lock");
    Check((await locksBob.AcquireAsync(lockedProduct, "sesiune-bob")) is { Acquired: true, Changed: true },
        "The product is available for editing right after a release");
    await ExpireLockAsync(lockedProduct);
    Check(await locksAna.GetAsync(lockedProduct) is null && (await locksAna.GetActiveAsync()).Count == 0,
        "A lease that expired (browser closed, connection lost) no longer blocks or shows");
    var takeover = await locksAna.AcquireAsync(lockedProduct, "sesiune-ana");
    Check(takeover is { Acquired: true, Changed: true } && takeover.Lock!.Owner == "ana",
        "An expired lock is taken over by the next editor");
    var staleRenewal = await locksBob.AcquireAsync(lockedProduct, "sesiune-bob");
    Check(!staleRenewal.Acquired && staleRenewal.Lock!.Owner == "ana",
        "A session whose lock was taken over learns on its next heartbeat that it lost the lock");
    await locksAna.ReleaseAsync(lockedProduct, "sesiune-ana");
    await locksBob.AcquireAsync(lockedProduct, "sesiune-bob");
    await ExpireLockAsync(lockedProduct);
    Check((await locksBob.AcquireAsync(lockedProduct, "sesiune-bob")) is { Acquired: true, Changed: false },
        "A lapsed lock that nobody took over is simply renewed by its own session");
    await ExpireLockAsync(lockedProduct);
    Check((await locksBob.RenewAsync(lockedProduct, "sesiune-bob")) is { Acquired: true, Changed: false } && await locksBob.GetAsync(lockedProduct) is not null,
        "The heartbeat renews a lapsed lock that nobody has taken over");
    Check(!(await locksAna.RenewAsync(lockedProduct, "sesiune-ana")).Acquired, "A session that never held the lock cannot renew it");
    await locksBob.ReleaseAsync(lockedProduct, "sesiune-bob");
    Check(!(await locksAna.AcquireAsync(lockedProduct + 999, "sesiune-ana")).Acquired, "A missing product cannot be locked");

    // Two simultaneous requests: exactly one wins.
    var contenders = await Task.WhenAll(Enumerable.Range(1, 24).Select(index =>
        Task.Run(() => (index % 2 == 0 ? locksAna : locksBob).AcquireAsync(lockedProduct, $"sesiune-{index}"))));
    Check(contenders.Count(attempt => attempt.Acquired) == 1 && (await locksAna.GetActiveAsync()).Count == 1,
        "Two simultaneous lock requests can never both obtain the right to edit");
    var winner = contenders.Single(attempt => attempt.Acquired).Lock!;

    // Administrative release.
    try { await locksBob.ForceReleaseAsync(lockedProduct, "Motiv"); throw new Exception("Non-administrator forced a release"); }
    catch (AccessDeniedException) { Check(true, "Only administrators can force a release"); }
    try { await locksAdmin.ForceReleaseAsync(lockedProduct, "  "); throw new Exception("Forced release without reason accepted"); }
    catch (ProductLockException) { Check((await locksAdmin.GetAsync(lockedProduct)) is not null, "A forced release requires a reason and leaves the lock untouched without one"); }
    var auditBefore = (await lockAudit.GetEventsAsync()).Count;
    var removed = await locksAdmin.ForceReleaseAsync(lockedProduct, "Colegul a plecat, produsul trebuie corectat urgent");
    Check(!(await locksAdmin.RenewAsync(lockedProduct, winner.SessionId)).Acquired && await locksAdmin.GetAsync(lockedProduct) is null,
        "After a forced release the former editor's heartbeat does not silently take the lock back");
    var unlockEvent = (await lockAudit.GetEventsAsync()).OrderBy(entry => entry.TimestampUtc).Last();
    Check(removed is not null && removed.SessionId == winner.SessionId && await locksAdmin.GetAsync(lockedProduct) is null &&
          (await lockAudit.GetEventsAsync()).Count == auditBefore + 1 && unlockEvent.Action == AuditActions.Unlock &&
          unlockEvent.EntityType == AuditEntities.Product && unlockEvent.EntityId == lockedProduct.ToString() &&
          unlockEvent.Motif.Contains("urgent") && unlockEvent.Target == "P-LOCK" && unlockEvent.Details.Contains(winner.Owner) &&
          unlockEvent.ActorUsername == "administrator.demo",
        "A forced release removes the lock and is written to the journal with actor, product, previous editor and reason");
    Check(await locksAdmin.ForceReleaseAsync(lockedProduct, "Din nou") is null && (await lockAudit.GetEventsAsync()).Count == auditBefore + 1,
        "Forcing a release when there is no lock changes nothing and writes no journal entry");

    // Notifications to waiting sessions.
    var lockFeed = new InProcessChangeFeed();
    var lockEvents = new List<ChangeEvent>();
    using var lockSubscription = lockFeed.Subscribe(change => { lock (lockEvents) lockEvents.Add(change); return Task.CompletedTask; });
    var lockOrigin = new ChangeOrigin();
    var notifying = new ChangeNotifyingProductLockRepository(new SqliteProductLockRepository(lockStore, new TestAccessControl(false, "ana"), lockAudit), lockFeed, lockOrigin);
    var otherNotifying = new ChangeNotifyingProductLockRepository(new SqliteProductLockRepository(lockStore, new TestAccessControl(false, "bob"), lockAudit), lockFeed, new ChangeOrigin());
    await notifying.AcquireAsync(lockedProduct, "sesiune-ana");
    await notifying.AcquireAsync(lockedProduct, "sesiune-ana");
    await otherNotifying.AcquireAsync(lockedProduct, "sesiune-bob");
    Check(lockEvents.Count == 1 && lockEvents[0].EntityType == ChangeEntities.ProductLock && lockEvents[0].Action == AuditActions.Create &&
          lockEvents[0].EntityId == lockedProduct.ToString() && lockEvents[0].Origin == lockOrigin.Id,
        "Taking a lock is announced once; a renewal or a refused request is not");
    await otherNotifying.ReleaseAsync(lockedProduct, "sesiune-bob");
    Check(lockEvents.Count == 1, "A release by a session that does not hold the lock is not announced");
    await notifying.ReleaseAsync(lockedProduct, "sesiune-ana");
    Check(lockEvents.Count == 2 && lockEvents[1].Action == AuditActions.Delete && lockEvents[1].EntityId == lockedProduct.ToString(),
        "A release is announced at once so waiting users are notified");
    await notifying.AcquireAsync(lockedProduct, "sesiune-ana");
    lockEvents.Clear();
    await new ChangeNotifyingProductLockRepository(locksAdmin, lockFeed, new ChangeOrigin()).ForceReleaseAsync(lockedProduct, "Test automat pentru notificare");
    Check(lockEvents.Count == 1 && lockEvents[0].Action == AuditActions.Delete, "A forced release is announced like a normal one");

    await using (var cascadeConnection = await lockStore.OpenConnectionAsync())
    {
        await locksAna.AcquireAsync(lockedProduct, "sesiune-ana");
        await using var delete = TestSqliteCommand(cascadeConnection, "DELETE FROM products WHERE id=@p", ("@p", lockedProduct));
        await delete.ExecuteNonQueryAsync();
    }
    Check((await locksAna.GetActiveAsync()).Count == 0, "Deleting a product removes its lock");
}
finally
{
    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    try { Directory.Delete(lockRoot, true); } catch (IOException) { }
}
Check(ProductLockRules.LeaseSeconds >= 60 && ProductLockRules.LeaseSeconds <= 120 && ProductLockRules.HeartbeatSeconds * 2 < ProductLockRules.LeaseSeconds,
    "The lease expires within 1–2 minutes and the heartbeat renews it well before that");
{
    var sample = new ProductLock(3, "ana", "s", new DateTime(2026, 9, 25, 10, 5, 0, DateTimeKind.Utc), DateTime.UtcNow, DateTime.UtcNow.AddSeconds(90), 90);
    var message = ProductLockRules.HeldMessage(sample);
    Check(message.Contains("ana") && message.Contains(sample.AcquiredUtc.ToLocalTime().ToString("HH:mm")) && message.Contains("consulta"),
        "The read-only message says who edits the product, since when, and that it can still be consulted");
}

// Task 1: unsaved-changes tracking and the leave warning.
{
    var guard = new UnsavedChanges();
    var name = "Ciocan";
    var closedOne = 0; var closedTwo = 0; var discardingSeen = false;
    var first = guard.Track(() => FormSnapshot.Values(name), () => { closedOne++; discardingSeen = guard.IsDiscarding; return Task.CompletedTask; });
    var otherText = "Nemodificat";
    var second = guard.Track(() => FormSnapshot.Values(otherText), () => { closedTwo++; return Task.CompletedTask; });
    Check(guard.TrackerCount == 2 && !guard.HasUnsavedChanges && !first.IsModified, "A form that was just opened is not modified");
    name = "Ciocan 2 kg";
    Check(first.IsModified && guard.HasUnsavedChanges, "A changed value marks the form as modified");
    name = "Ciocan";
    Check(!first.IsModified && !guard.HasUnsavedChanges, "Returning to the initial value makes the form unmodified again");

    var ran = 0;
    await first.RunAfterConfirmAsync(() => { ran++; return Task.CompletedTask; });
    Check(ran == 1 && guard.Pending is null, "An unmodified form is closed without asking");
    name = "Ciocan nou";
    await first.RunAfterConfirmAsync(() => { ran++; return Task.CompletedTask; });
    Check(ran == 1 && guard.Pending is not null, "A modified form asks first and does not run the action yet");
    guard.Cancel();
    Check(ran == 1 && guard.Pending is null && first.IsModified, "\"Înapoi la editare\" keeps the values, the form and the warning");
    await first.RunAfterConfirmAsync(() => { ran++; return Task.CompletedTask; });
    await guard.ConfirmAsync();
    Check(ran == 2 && closedOne == 1 && discardingSeen && !guard.IsDiscarding && !first.IsModified && closedTwo == 0,
        "\"Părăsește editarea\" discards only that form and then runs the interrupted action");

    otherText = "Modificat";
    var navigated = "";
    guard.Request(() => { navigated = "/beneficiari"; return Task.CompletedTask; });
    await guard.ConfirmAsync();
    Check(navigated == "/beneficiari" && closedTwo == 1 && closedOne == 1 && !guard.HasUnsavedChanges,
        "Leaving the page discards every modified form (and only those) and then performs the navigation");

    otherText = "Salvat";
    Check(second.IsModified == false, "A discarded form no longer counts as modified");
    var saved = guard.Track(() => FormSnapshot.Values(otherText), () => Task.CompletedTask);
    otherText = "Salvat cu succes";
    Check(saved.IsModified, "A new tracker starts from the values at that moment");
    saved.Rebase();
    Check(!saved.IsModified && !guard.HasUnsavedChanges, "A successful save makes the saved values the unmodified state");
    otherText = "Altă valoare";
    Check(saved.IsModified, "Changes after a save are detected again");

    var flips = 0;
    guard.Changed += () => flips++;
    saved.NotifyRendered(); saved.NotifyRendered();
    otherText = "Salvat cu succes"; // back to the saved value
    saved.Rebase();
    Check(flips >= 1, "The host is notified when the unsaved state flips");

    var addingValue = "";
    var addTracker = guard.Track(() => FormSnapshot.Values(addingValue), () => Task.CompletedTask, adding: true);
    var editValue = "a";
    var editTracker = guard.Track(() => FormSnapshot.Values(editValue), () => Task.CompletedTask);
    addingValue = "nou";
    guard.Request(() => Task.CompletedTask);
    Check(guard.Pending is { Adding: true }, "The question says \"adăugare\" when only forms that add a new object are affected");
    guard.Cancel();
    editValue = "b";
    guard.Request(() => Task.CompletedTask);
    Check(guard.Pending is { Adding: false }, "The question says \"editare\" as soon as an edited (existing) object is affected");
    guard.Cancel();
    guard.Request(() => Task.CompletedTask, addTracker);
    Check(guard.Pending is { Adding: true }, "Closing a single add form asks about the adding");
    guard.Cancel();
    addTracker.Dispose(); editTracker.Dispose();

    saved.Dispose(); first.Dispose(); second.Dispose();
    Check(guard.TrackerCount == 0 && !guard.HasUnsavedChanges, "Closing an editor removes its tracker");

    var product = ProductInput.From(data[0]);
    var before = FormSnapshot.Of(product);
    product.Reason = "Doar un motiv";
    Check(FormSnapshot.Of(product) == before, "Typing only the change reason is not an unsaved value change");
    product.Name += " x";
    Check(FormSnapshot.Of(product) != before, "Changing a product field changes the snapshot");
    Check(FormSnapshot.Of(product, "imagine.png") != FormSnapshot.Of(product), "A selected image counts as a change");
    var user = new WebUserInput { Username = "ana", Password = "abc" };
    var userBefore = FormSnapshot.Of(user);
    user.Password = "abcd";
    Check(FormSnapshot.Of(user) != userBefore, "A typed password counts as a change (compared only through a hash)");
}

// Vehicles administration: registration number mask, normalization, CRUD, archive, journal (SQLite).
{
    foreach (var (typed, expected) in new[]
             {
                 ("HD-01-FDG", "HD-01-FDG"), ("HD-233-VDG", "HD-233-VDG"), ("B-123-ABC", "B-123-ABC"), ("B-12-ABC", "B-12-ABC"),
                 ("hd-01-fdg", "HD-01-FDG"), ("  b 123 abc ", "B-123-ABC"), ("HD01FDG", "HD-01-FDG"), ("hd233vdg", "HD-233-VDG"),
                 ("HĂ-01-FDG", "HA-01-FDG")
             })
        Check(VehiclePlate.TryNormalize(typed, out var plate) && plate == expected, $"Registration number «{typed}» is accepted as {expected}");
    foreach (var badPlate in new[]
             {
                 "", "   ", "HD-1-FDG", "HD-2334-FDG", "HD-01-FD", "HD-01-FDGH", "HDD-01-FDG", "-01-FDG", "H1-01-FDG",
                 "HD-0A-FDG", "HD-01-F1G", "HD_01_FDG", "HD-01-FDG-1", "12-345-ABC"
             })
        Check(!VehiclePlate.TryNormalize(badPlate, out _), $"Registration number «{badPlate}» is rejected");

    var vehicleRoot = Path.Combine(Path.GetTempPath(), "blazorstoc-vehicles-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(vehicleRoot);
    try
    {
        var vehicleConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["App:LocalDatabasePath"] = Path.Combine(vehicleRoot, "vehicles.db"),
            ["App:ProductImagesPath"] = Path.Combine(vehicleRoot, "product-images"),
            ["App:ArchiveFilesPath"] = Path.Combine(vehicleRoot, "archive-files"),
            ["App:AuditPath"] = Path.Combine(vehicleRoot, "legacy-audit.jsonl")
        }).Build();
        var vehicleEnvironment = new TestWebHostEnvironment(vehicleRoot);
        var vehicleAccess = new TestAccessControl(true, "operator.vehicule");
        var vehicleStore = new SqliteLocalStore(vehicleEnvironment, vehicleConfiguration, NullLogger<SqliteLocalStore>.Instance);
        var vehicles = new SqliteVehicleRepository(vehicleStore, vehicleAccess);
        var vehicleAudit = new SqliteAuditTrail(vehicleStore);
        async Task VehicleRejected(Func<Task> operation, string message)
        {
            try { await operation(); }
            catch (VehicleOperationException) { Check(true, message); return; }
            throw new Exception(message + " (accepted)");
        }
        Check((await vehicles.GetVehiclesAsync()).Count == 0, "A new database has no vehicles");

        var dokker = await vehicles.CreateAsync(new VehicleInput { PlateNumber = " hd 01 fdg ", Description = "  Dacia   Dokker albă " });
        Check(dokker.PlateNumber == "HD-01-FDG" && dokker.Description == "Dacia Dokker alba" && dokker.Version == 0 && dokker.Id > 0,
            "A vehicle is saved with the canonical number, a trimmed description without diacritics and version 0");
        var bucharest = await vehicles.CreateAsync(new VehicleInput { PlateNumber = "B-123-ABC", Description = "Autoutilitara Bucuresti" });
        Check((await vehicles.GetVehiclesAsync()).Select(vehicle => vehicle.PlateNumber).SequenceEqual(["B-123-ABC", "HD-01-FDG"]),
            "Vehicles are listed alphabetically by registration number");

        await VehicleRejected(() => vehicles.CreateAsync(new VehicleInput { PlateNumber = "", Description = "Fara numar" }), "A missing registration number is rejected");
        await VehicleRejected(() => vehicles.CreateAsync(new VehicleInput { PlateNumber = "HD-1-FDG", Description = "Numar gresit" }), "An invalid registration number is rejected");
        await VehicleRejected(() => vehicles.CreateAsync(new VehicleInput { PlateNumber = "HD-02-FDG", Description = "   " }), "A missing description is rejected");
        await VehicleRejected(() => vehicles.CreateAsync(new VehicleInput { PlateNumber = "HD-02-FDG", Description = new string('a', 101) }), "An oversize description is rejected");
        try { await vehicles.CreateAsync(new VehicleInput { PlateNumber = "hd01fdg", Description = "Duplicat" }); throw new Exception("Duplicate number accepted"); }
        catch (VehicleOperationException exception)
        {
            Check(exception.Message == VehicleRules.DuplicatePlateMessage("HD-01-FDG", "Dacia Dokker alba"),
                "A duplicate registration number (any letter case, hyphens optional) is rejected naming the stored vehicle");
        }
        Check(VehicleInput.From(dokker).PlateNumber == "HD-01-FDG" && VehiclePlate.FormatMessage.Contains("HD-233-VDG") && VehiclePlate.FormatMessage.Contains("B-123-ABC"),
            "The format message shows the accepted examples");

        var noReason = VehicleInput.From(dokker); noReason.Description = "Dacia Dokker gri";
        await VehicleRejected(() => vehicles.UpdateAsync(dokker, noReason), "A vehicle edit requires a reason");
        var edit = VehicleInput.From(dokker); edit.Description = "Dacia Dokker gri"; edit.Reason = "Culoare corectata";
        var edited = await vehicles.UpdateAsync(dokker, edit);
        Check(edited.Description == "Dacia Dokker gri" && edited.Version == 1 && edited.PlateNumber == "HD-01-FDG", "A vehicle edit changes the description and increments the version");
        await VehicleRejected(() => vehicles.UpdateAsync(dokker, edit), "A stale vehicle edit cannot overwrite newer data");
        var toDuplicate = VehicleInput.From(bucharest); toDuplicate.PlateNumber = "HD-01-FDG"; toDuplicate.Reason = "Test automat";
        await VehicleRejected(() => vehicles.UpdateAsync(bucharest, toDuplicate), "Editing a number to an existing one is rejected");
        var keepNumber = VehicleInput.From(edited); keepNumber.Description = "Dacia Dokker gri inchis"; keepNumber.Reason = "Test automat";
        var keptNumber = await vehicles.UpdateAsync(edited, keepNumber);
        Check(keptNumber.Version == 2, "Saving without changing the number does not report the vehicle itself as a duplicate");

        var afterCreateEvents = await vehicleAudit.GetEventsAsync();
        var vehicleEvents = afterCreateEvents.Where(entry => entry.EntityType == AuditEntities.Vehicle).ToArray();
        Check(vehicleEvents.Count(entry => entry.Action == AuditActions.Create) == 2 && vehicleEvents.Count(entry => entry.Action == AuditActions.Edit) == 2 &&
              vehicleEvents.All(entry => entry.ActorUsername == "operator.vehicule"),
            "Each successful vehicle operation writes exactly one journal event with the operator; rejected ones write nothing");
        var editEvent = vehicleEvents.Where(entry => entry.Action == AuditActions.Edit).OrderBy(entry => entry.TimestampUtc).First();
        Check(editEvent.Details == "Descriere: Dacia Dokker alba → Dacia Dokker gri" && editEvent.Motif == "Culoare corectata" &&
              editEvent.EntityId == dokker.Id.ToString() && editEvent.Target == $"#{dokker.Id} · HD-01-FDG",
            "A vehicle edit journals the changed fields (before → after), the reason and the vehicle as target");
        Check(AuditNavigation.TargetUrl(editEvent) == $"/vehicule/{dokker.Id}", "The journal links a vehicle event to its page");

        // Concurrent creations of the same number: exactly one succeeds.
        var racing = await Task.WhenAll(Enumerable.Range(0, 4).Select(async index =>
        {
            try { await new SqliteVehicleRepository(vehicleStore, vehicleAccess).CreateAsync(new VehicleInput { PlateNumber = "CJ-77-XYZ", Description = "Sesiune " + index }); return true; }
            catch (Exception exception) when (exception is VehicleOperationException or Microsoft.Data.Sqlite.SqliteException) { return false; }
        }));
        Check(racing.Count(created => created) == 1 && (await vehicles.GetVehiclesAsync()).Count(vehicle => vehicle.PlateNumber == "CJ-77-XYZ") == 1,
            "Two sessions creating the same registration number at once produce a single vehicle");

        // Deletion: archive + journal in one transaction; blocked rules; persistence after restart.
        await VehicleRejected(() => vehicles.DeleteAsync(dokker, "Motiv"), "Deleting a stale vehicle snapshot is rejected");
        await VehicleRejected(() => vehicles.DeleteAsync(keptNumber, "  "), "A vehicle deletion requires a reason");
        var eventsBeforeDelete = (await vehicleAudit.GetEventsAsync()).Count;
        await vehicles.DeleteAsync(keptNumber, "Vehiculul nu va mai fi folosit");
        Check((await vehicles.GetVehiclesAsync()).All(vehicle => vehicle.Id != keptNumber.Id), "A deleted vehicle leaves the live list");
        await using (var connection = await vehicleStore.OpenConnectionAsync())
        {
            await using var archived = TestSqliteCommand(connection, """
                SELECT a.plate_number,a.description,a.version,o.motif,o.actor_username,
                       (SELECT COUNT(*) FROM audit_events e WHERE e.archive_operation_id=o.id AND e.action=@delete)
                FROM archive_vehicles a INNER JOIN archive_operations o ON o.id=a.archive_id WHERE a.original_id=@id
                """, ("@id", keptNumber.Id), ("@delete", AuditActions.Delete));
            await using var reader = await archived.ExecuteReaderAsync();
            Check(await reader.ReadAsync() && reader.GetString(0) == "HD-01-FDG" && reader.GetString(1) == "Dacia Dokker gri inchis" &&
                  reader.GetInt64(2) == 2 && reader.GetString(3) == "Vehiculul nu va mai fi folosit" && reader.GetString(4) == "operator.vehicule" &&
                  reader.GetInt32(5) == 1,
                "Deleting a vehicle archives its data with the reason and the operator and writes the journal event in the same operation");
        }
        Check((await vehicleAudit.GetEventsAsync()).Count == eventsBeforeDelete + 1, "A deletion writes exactly one journal event");
        await VehicleRejected(() => vehicles.DeleteAsync(keptNumber, "Motiv"), "A repeated deletion is rejected");
        var reused = await vehicles.CreateAsync(new VehicleInput { PlateNumber = "HD-01-FDG", Description = "Alt vehicul" });
        Check(reused.Id != keptNumber.Id, "The number of an archived vehicle can be registered again");
        VehicleRules.CheckDelete(false);
        try { VehicleRules.CheckDelete(true); throw new Exception("Deleting a vehicle with movements accepted"); }
        catch (VehicleOperationException) { Check(true, "A vehicle with stock movements must not be deleted"); }

        var restartedStore = new SqliteLocalStore(vehicleEnvironment, vehicleConfiguration, NullLogger<SqliteLocalStore>.Instance);
        var restartedList = await new SqliteVehicleRepository(restartedStore, vehicleAccess).GetVehiclesAsync();
        Check(restartedList.Select(vehicle => vehicle.PlateNumber).SequenceEqual(["B-123-ABC", "CJ-77-XYZ", "HD-01-FDG"]),
            "Vehicles survive the restart of the application");
    }
    finally
    {
        try { Directory.Delete(vehicleRoot, true); } catch (IOException) { }
    }

    Check(VehicleSearch.Filter([new Vehicle(1, "HD-01-FDG", "Dacia Dokker alba"), new Vehicle(2, "B-123-ABC", "Autoutilitara")], "hd01").Single().Id == 1 &&
          VehicleSearch.Filter([new Vehicle(1, "HD-01-FDG", "Dacia Dokker alba"), new Vehicle(2, "B-123-ABC", "Autoutilitara")], "auto").Single().Id == 2,
        "Vehicles are searched by registration number (hyphens optional) or description");
}

// Task 1: inventory report (selection logic, the report builder, PDF generation and its journal event).
{
    // Selection logic (InventorySelectionState): category <-> subcategories <-> "select all", independent of the UI.
    var electric = ("Scule electrice", (IReadOnlyList<string>)new List<string> { "Găurire", "Tăiere" });
    var consumables = ("Consumabile", (IReadOnlyList<string>)new List<string> { "Fixare" });
    var emptyCategory = ("Categorie fără subcategorii", (IReadOnlyList<string>)new List<string>());
    var allCategories = new List<(string, IReadOnlyList<string>)> { electric, consumables, emptyCategory };
    var selection = new InventorySelectionState();
    Check(selection.CategoryState("Scule electrice", electric.Item2) is { Checked: false, Indeterminate: false } &&
          selection.AllState(allCategories) is { Checked: false, Indeterminate: false },
        "Nothing selected at the start is neither checked nor indeterminate");
    selection.SetSubcategory("Scule electrice", "Găurire", true);
    Check(selection.CategoryState("Scule electrice", electric.Item2) is { Checked: false, Indeterminate: true },
        "Selecting only one of two subcategories makes the category checkbox indeterminate");
    selection.SetSubcategory("Scule electrice", "Tăiere", true);
    Check(selection.CategoryState("Scule electrice", electric.Item2) is { Checked: true, Indeterminate: false },
        "Selecting every subcategory checks the category checkbox");
    selection.SetCategory("Scule electrice", electric.Item2, false);
    Check(!selection.IsSelected("Scule electrice", "Găurire") && !selection.IsSelected("Scule electrice", "Tăiere"),
        "Deselecting a category propagates to all of its subcategories");
    Check(selection.CategoryState("Categorie fără subcategorii", emptyCategory.Item2) is { Checked: false, Indeterminate: false },
        "A category without subcategories is never checked or indeterminate");
    selection.SetAll(allCategories, true);
    Check(selection.AllState(allCategories) is { Checked: true, Indeterminate: false } &&
          selection.Count(allCategories) == 3 && selection.IsSelected("Consumabile", "Fixare"),
        "\"Select all categories\" selects every subcategory, ignoring categories without subcategories");
    selection.SetSubcategory("Consumabile", "Fixare", false);
    Check(selection.AllState(allCategories) is { Checked: false, Indeterminate: true },
        "\"Select all\" becomes indeterminate once one subcategory is cleared");
    Check(selection.ToItems(allCategories).Count == 2 && selection.ToItems(allCategories)
        .All(item => item.Category == "Scule electrice"), "ToItems returns exactly the selected pairs");

    // Report builder: a small fake catalogue with a negative, a zero and a vehicle-held product.
    var products = new List<Product>
    {
        new(1, "Scule electrice", "Găurire", "Mașină de găurit", "desc", 5, 1),
        new(2, "Scule electrice", "Tăiere", "Polizor", "desc", 0, 1),
        new(3, "Consumabile", "Fixare", "Șurub", "desc", -3, 1),
        new(4, "Consumabile", "Fixare", "Diblu", "desc", 10, 1),
        new(5, "Consumabile", "Ambalare", "Folie", "desc", 2, 1)
    };
    var groups = new List<ProductGroup>
    {
        new("Scule electrice", "Găurire"), new("Scule electrice", "Tăiere"),
        new("Consumabile", "Fixare"), new("Consumabile", "Ambalare")
    };
    var inVehicles = new Dictionary<int, int> { [4] = 4 }; // Diblu: 10 total, 4 in a vehicle -> 6 in the warehouse.
    var builder = new InventoryReportBuilder(new FakeInventoryProductRepository(products, groups), new FakeInventoryStockMovementRepository(inVehicles));

    var report = await builder.BuildAsync(new InventoryRequest(
        [new("Scule electrice", "Găurire"), new("Consumabile", "Fixare")], ExcludeZeroStock: false));
    Check(report.Categories.Count == 2 && report.SelectedCategoryCount == 2 && report.SelectedSubcategoryCount == 2,
        "The report contains only the selected categories and subcategories");
    var electricSection = report.Categories.Single(c => c.Category == "Scule electrice");
    Check(electricSection.Subcategories.Single().Subcategory == "Găurire" &&
          electricSection.Subcategories.Single().Lines.Single() == new InventoryLine("Mașină de găurit", 5),
        "An unselected subcategory (Tăiere) of a partially selected category is left out of the report");
    var consumablesSection = report.Categories.Single(c => c.Category == "Consumabile");
    var fixareLines = consumablesSection.Subcategories.Single().Lines;
    Check(fixareLines.Select(line => line.Code).SequenceEqual(["Diblu", "Șurub"]), "Products are ordered by code, case-insensitively");
    Check(fixareLines.Single(line => line.Code == "Diblu").Quantity == 6, "The warehouse value excludes the quantity held by vehicles");
    Check(fixareLines.Single(line => line.Code == "Șurub") is { Quantity: -3, IsNegative: true }, "Negative stock is kept and flagged");
    Check(report.ProductCount == 3 && report.NegativeCount == 1, "The report totals count every line and the negative ones separately");
    Check(!report.Categories.Any(c => c.Category == "Consumabile" && c.Subcategories.Any(s => s.Subcategory == "Ambalare")),
        "An unselected subcategory does not appear even when its category is otherwise selected");

    var zeroFiltered = await builder.BuildAsync(new InventoryRequest(
        [new("Scule electrice", "Găurire"), new("Scule electrice", "Tăiere")], ExcludeZeroStock: true));
    Check(zeroFiltered.Categories.Single().Subcategories.Single().Subcategory == "Găurire",
        "Excluding zero stock omits an emptied subcategory (Tăiere's only product has quantity 0) but keeps a non-empty one");

    try
    {
        await builder.BuildAsync(new InventoryRequest([new("Scule electrice", "Tăiere")], ExcludeZeroStock: true));
        throw new Exception("Empty result after filtering accepted");
    }
    catch (InventoryOperationException exception) { Check(exception.Message == InventoryRules.NoProductsMessage, "A selection left empty by the zero-stock filter is reported, not silently generated"); }

    try
    {
        await builder.BuildAsync(new InventoryRequest([], ExcludeZeroStock: false));
        throw new Exception("Empty selection accepted");
    }
    catch (InventoryOperationException exception) { Check(exception.Message == InventoryRules.NoSelectionMessage, "An empty selection is rejected before any catalogue access"); }

    try
    {
        await builder.BuildAsync(new InventoryRequest([new("Categorie inexistentă", "Subcategorie inexistentă")], ExcludeZeroStock: false));
        throw new Exception("Stale selection accepted");
    }
    catch (InventoryOperationException exception) { Check(exception.Message == InventoryRules.InvalidSelectionMessage, "A selection that no longer matches the catalogue is reported instead of generating a partial file"); }

    var duplicateSelection = await builder.BuildAsync(new InventoryRequest(
        [new("Consumabile", "Fixare"), new("Consumabile", "Fixare")], ExcludeZeroStock: false));
    Check(duplicateSelection.SelectedSubcategoryCount == 1, "A repeated selection entry is counted once");

    Check(InventoryRules.FileName(new DateTime(2026, 9, 25, 14, 8, 0)) == "Inventar_2026-09-25_1408.pdf",
        "The proposed file name uses the local moment of generation");

    // PDF generation: the file starts with %PDF, has the expected texts (including a diacritic code, extracted
    // through the embedded font's own ToUnicode map), the negative row is red, and headings use the bold 14pt font.
    var pdfWriter = new InventoryPdfWriter();
    var generatedLocal = new DateTime(2026, 9, 25, 14, 8, 0);
    var pdfBytes = pdfWriter.Write(report, generatedLocal);
    Check(pdfBytes.Length > 4 && pdfBytes[0] == (byte)'%' && pdfBytes[1] == (byte)'P' && pdfBytes[2] == (byte)'D' && pdfBytes[3] == (byte)'F',
        "The generated file starts with the PDF signature");
    using (var pdfStream = new MemoryStream(pdfBytes))
    {
        var pdfDocument = PdfReader.Open(pdfStream, PdfDocumentOpenMode.Import);
        Check(pdfDocument.PageCount >= 1, "The generated PDF has at least one page");
        var pageText = PdfTextExtractor.ExtractText(pdfDocument.Pages[0]);
        Check(pageText.Contains("Inventar") && pageText.Contains("Generat la: 25.09.2026 14:08"),
            "The first page has the title and the local generation moment in the dd.MM.yyyy HH:mm form");
        Check(pageText.Contains("Scule electrice") && pageText.Contains("Găurire") && pageText.Contains("Consumabile") && pageText.Contains("Fixare"),
            "The selected category and subcategory names appear on the page");
        Check(pageText.Contains("Cod produs") && pageText.Contains("Valoare stoc") && pageText.Contains("Valoare reală"),
            "The table header has the three required columns");
        Check(pageText.Contains("Șurub"), "A product code with Romanian diacritics is extracted correctly from the embedded font");
        var fontNames = PdfTextExtractor.FontBaseNames(pdfDocument.Pages[0]);
        var fontUsage = PdfTextExtractor.FontUsage(pdfDocument.Pages[0]);
        Check(fontUsage.Any(usage => usage.Size == 14 && fontNames[usage.FontKey].Contains("Bold", StringComparison.OrdinalIgnoreCase)),
            "Category and subcategory names use a bold 14pt font");
        Check(fontUsage.Any(usage => usage.Size == 12 && !fontNames[usage.FontKey].Contains("Bold", StringComparison.OrdinalIgnoreCase)),
            "The rest of the text uses a normal 12pt font");
        Check(PdfTextExtractor.ExtractRawContent(pdfDocument.Pages[0]).Contains("1 0 0 rg"),
            "The page sets the red fill colour for the negative-stock row");
    }

    var noNegativeReport = await builder.BuildAsync(new InventoryRequest([new("Scule electrice", "Găurire")], ExcludeZeroStock: false));
    var noNegativeBytes = pdfWriter.Write(noNegativeReport, generatedLocal);
    using (var pdfStream = new MemoryStream(noNegativeBytes))
    {
        var pdfDocument = PdfReader.Open(pdfStream, PdfDocumentOpenMode.Import);
        Check(!PdfTextExtractor.ExtractRawContent(pdfDocument.Pages[0]).Contains("1 0 0 rg"),
            "A report with no negative stock never sets the red fill colour");
    }

    // A large catalogue produces more than one page, with the table header repeated on the following pages.
    var manyLines = Enumerable.Range(1, 300).Select(index => new InventoryLine($"Produs {index:000}", 1)).ToArray();
    var largeReport = new InventoryReport(DateTime.UtcNow,
        [new InventoryCategorySection("Categorie mare", [new InventorySubcategorySection("Subcategorie mare", manyLines)])],
        1, 1, manyLines.Length, 0);
    var largeBytes = pdfWriter.Write(largeReport, generatedLocal);
    using (var pdfStream = new MemoryStream(largeBytes))
    {
        var pdfDocument = PdfReader.Open(pdfStream, PdfDocumentOpenMode.Import);
        Check(pdfDocument.PageCount > 1, "A catalogue of 300 products spans more than one page");
        Check(Enumerable.Range(0, pdfDocument.PageCount).Count(index => PdfTextExtractor.ExtractText(pdfDocument.Pages[index]).Contains("Cod produs")) > 1,
            "The table header is repeated on the pages that continue the table");
        Check($"Pagina {pdfDocument.PageCount} din {pdfDocument.PageCount}" is { } lastPageLabel &&
              PdfTextExtractor.ExtractText(pdfDocument.Pages[pdfDocument.PageCount - 1]).Contains(lastPageLabel),
            "Each page has a \"Pagina x din y\" footer");
    }

    // The journal event: written only after a successful generation, with a summary but no product data.
    var inventoryAudit = new TestAuditTrail();
    var inventoryAccess = new TestAccessControl(false, "gestionar.stoc");
    var auditDetails = AuditDetails.Identification(
        ("Categorii selectate", report.SelectedCategoryCount.ToString()), ("Subcategorii selectate", report.SelectedSubcategoryCount.ToString()),
        ("Stoc 0 exclus", "nu"), ("Produse în situație", report.ProductCount.ToString()),
        ("Din care cu stoc negativ", report.NegativeCount.ToString()), ("Fișier", "Inventar_2026-09-25_1408.pdf"));
    await AuditRecorder.RecordGenerateAsync(inventoryAudit, inventoryAccess, AuditEntities.Inventory, "Situație de inventar", auditDetails, CancellationToken.None);
    var inventoryEvent = inventoryAudit.Entries.Single();
    Check(inventoryEvent.ActorUsername == "gestionar.stoc" && inventoryEvent.EntityType == AuditEntities.Inventory &&
          inventoryEvent.EntityId.Length == 0 && !inventoryEvent.Details.Contains("Mașină de găurit") && !inventoryEvent.Details.Contains("Șurub") &&
          inventoryEvent.Details.Contains("Produse în situație: 3") && inventoryEvent.Details.Contains("Din care cu stoc negativ: 1"),
        "A successful generation writes exactly one event with the user, a summary and no product data or link");
    Check(AuditActions.Normalize(AuditActions.Generate) == "Generare", "The generation action is named \"Generare\" in the journal");
}

// Task 1: every message shown to the user is in Romanian. The literals that become user-visible messages (validation
// attributes, operation exceptions, error/notice fields, ...Message constants) must not contain common English words.
{
    var projectRoot = AppContext.BaseDirectory;
    while (projectRoot is not null && !File.Exists(Path.Combine(projectRoot, "BlazorStoc.csproj"))) projectRoot = Path.GetDirectoryName(projectRoot.TrimEnd(Path.DirectorySeparatorChar));
    Check(projectRoot is not null, "The project folder was found for the message scan");
    var files = new[] { "Services", "Components", "Pages" }
        .SelectMany(folder => Directory.EnumerateFiles(Path.Combine(projectRoot!, folder), "*.*", SearchOption.AllDirectories))
        .Where(file => file.EndsWith(".cs") || file.EndsWith(".razor") || file.EndsWith(".cshtml"))
        .Append(Path.Combine(projectRoot!, "Program.cs")).ToArray();
    var patterns = new[]
    {
        @"(?:ErrorMessage|ParsingErrorMessage)\s*=\s*""([^""]*)""",
        @"\w+Exception\(\s*\$?""([^""]*)""",
        @"\b(?:error|Error|formError|editError|deleteError|historyError|notice|lockMessage|unlockError|reasonError|imageError|filesError|message|Message)\s*=\s*\$?""([^""]*)""",
        @"\b\w*Message\s*(?:=|=>)\s*\$?""([^""]*)""",
        @"\b[eE]rrors\.Add\(\s*\$?""([^""]*)""",
    };
    var english = new HashSet<string>(["the", "is", "must", "cannot", "failed", "invalid", "required", "please", "error", "already",
        "exists", "found", "unable", "could", "should", "will", "your", "been", "denied", "missing", "expected", "value", "field", "not"],
        StringComparer.OrdinalIgnoreCase);
    var scanned = 0; var offenders = new List<string>();
    foreach (var file in files)
    {
        var text = File.ReadAllText(file);
        foreach (var pattern in patterns)
            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(text, pattern))
            {
                var literal = match.Groups[1].Value;
                scanned++;
                var words = System.Text.RegularExpressions.Regex.Matches(literal, @"[A-Za-z']+").Select(word => word.Value);
                var found = words.Where(english.Contains).ToArray();
                if (found.Length > 0) offenders.Add($"{Path.GetFileName(file)}: \"{literal}\" ({string.Join(", ", found)})");
            }
    }
    Check(scanned > 100, $"The message scan reads the message literals ({scanned} found)");
    Check(offenders.Count == 0, "Message literals shown to users contain no English words" + (offenders.Count == 0 ? "" : ": " + string.Join(" | ", offenders.Take(5))));
}

sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(TimeSpan span) => now += span;
}

sealed class FakeChangeSource : IChangeEventSource
{
    private readonly List<StoredChange> events = [];
    public bool FailNextRead { get; set; }
    public void Add(string type, string action, string id, int? projectId = null) =>
        events.Add(new(events.Count + 1, type, action, id, projectId, null, null, DateTime.UtcNow));
    public Task EnsureAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<long> LatestIdAsync(CancellationToken cancellationToken) => Task.FromResult<long>(events.Count);
    public Task<IReadOnlyList<StoredChange>> ReadAfterAsync(long afterId, int limit, CancellationToken cancellationToken)
    {
        if (FailNextRead) { FailNextRead = false; throw new InvalidOperationException("Source unavailable"); }
        return Task.FromResult<IReadOnlyList<StoredChange>>(events.Where(change => change.Id > afterId).Take(limit).ToList());
    }
    public Task PurgeAsync(long throughId, DateTime olderThanUtc, CancellationToken cancellationToken) => Task.CompletedTask;
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

sealed class FakeInventoryProductRepository(IReadOnlyList<Product> products, IReadOnlyList<ProductGroup> groups) : IProductRepository
{
    public Task<IReadOnlyList<Product>> GetProductsAsync(CancellationToken cancellationToken = default) => Task.FromResult(products);
    public Task<Product?> GetProductAsync(int id, CancellationToken cancellationToken = default) => Task.FromResult(products.FirstOrDefault(product => product.Id == id));
    public Task<IReadOnlyList<ProductGroup>> GetGroupsAsync(CancellationToken cancellationToken = default) => Task.FromResult(groups);
    public Task CreateCategoryAsync(string category, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ProductGroup> CreateSubcategoryAsync(string category, string subcategory, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task RenameCategoryAsync(string originalCategory, string newCategory, string reason, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ProductGroup> UpdateSubcategoryAsync(ProductGroup original, string newSubcategory, string targetCategory, string reason, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<Product> CreateAsync(ProductInput input, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<Product> UpdateAsync(Product original, ProductInput input, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task DeleteAsync(Product original, string reason, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

sealed class FakeInventoryStockMovementRepository(IReadOnlyDictionary<int, int> inVehicles) : IStockMovementRepository
{
    public Task<IReadOnlyDictionary<int, int>> GetQuantitiesInVehiclesAsync(CancellationToken cancellationToken = default) => Task.FromResult(inVehicles);
    public Task<StockMovementPage> GetPageAsync(int productId, StockMovementQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<StockMovement?> GetAsync(int id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<StockMovementHistoryEntry>> GetHistoryAsync(int movementId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<ProjectStockMovement>> GetForProjectAsync(int projectId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<VehicleStock>> GetVehicleStocksAsync(int productId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<VehicleEquipment>> GetVehicleEquipmentAsync(int vehicleId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<StockMovement>> TransferFromVehicleAsync(VehicleTransfer transfer, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyDictionary<int, int>> GetMovementCountsByVehicleAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<StockMovementResult> CreateAsync(int productId, StockMovementInput input, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<StockMovementResult> UpdateAsync(StockMovement original, StockMovementInput input, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<int> DeleteAsync(StockMovement original, string reason, CancellationToken cancellationToken = default) => throw new NotSupportedException();
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
