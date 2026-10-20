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

// The checks of the full in-memory run, in order (the script that used to be Program.cs): every FullRun.<Topic>.cs is a stretch of that script, in the order of RunAsync; what two
// stretches share (variables and helper functions that were locals of the script) is declared here.
public static partial class FullRun
{
    public static async Task RunAsync(string[] args)
    {
        if (!await ArchiveAsync(args)) return;
        await ExitFormAndExportsAsync(args);
        await ProductsAndSearchAsync(args);
        await ImagesCategoriesUsersAsync(args);
        await BeneficiariesAndJournalAsync(args);
        await BeneficiaryScreensAsync(args);
        await StockMovementsAsync(args);
        await LiveRefreshAndUnsavedChangesAsync(args);
        await InventoryReportAsync(args);
        await InventoryOcrAsync(args);
        await BackupAndMaintenanceGateAsync(args);
        await ExpiryAndWorkPointsAsync(args);
        await ContractsAndInterventionsAsync(args);
        await MaintenanceNotificationsAndMapAsync(args);
    }

    internal static global::System.Collections.Generic.IReadOnlyList<global::BlazorStoc.Services.Product>? data;
    internal static global::System.Threading.CancellationTokenSource? cancelled;
    internal static global::BlazorStoc.Services.DemoProductRepository? repository;
    internal static global::BlazorStoc.Services.Product? created;
    internal static global::TestAccessControl? limitedAccess;
    internal static global::TestAuditTrail? auditTrail;
    internal static global::BlazorStoc.Services.DemoProductRepository? auditedProducts;
    internal static global::BlazorStoc.Services.Product? auditedProduct;
    internal static global::BlazorStoc.Services.DemoBeneficiaryRepository? demoBeneficiaries;
    internal static global::BlazorStoc.Services.Beneficiary? beneficiary;


static     void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }

static     ProductInput ProductEdit(Product product, string reason = "Test automat") { var input = ProductInput.From(product); input.Reason = reason; return input; }

static     WebUserInput UserEdit(WebUser user, string reason = "Test automat") { var input = WebUserInput.From(user); input.Reason = reason; return input; }

static     BeneficiaryInput LegalInput(string name, string cui) => new() { Name = name, Cui = cui, Address = "Strada Test 1, Bucuresti", Phone = "0721 000 111" };

static     BeneficiaryInput BeneficiaryEdit(Beneficiary beneficiary, string reason = "Test automat") { var input = BeneficiaryInput.From(beneficiary); input.Reason = reason; return input; }

static     async Task Rejected(Func<Task> operation, string message)
    {
        try { await operation(); }
        catch (ProductOperationException) { Check(true, message); return; }
        throw new Exception("Expected rejection: " + message);
    }

static     async Task EnsureProductGroupAsync(IProductRepository productRepository, string category, string subcategory)
    {
        var available = await productRepository.GetGroupsAsync();
        if (!available.Any(group => TextNormalization.SameUniqueValue(group.Category, category)))
            await productRepository.CreateCategoryAsync(category);
        available = await productRepository.GetGroupsAsync();
        if (!available.Any(group => TextNormalization.SameUniqueValue(group.Category, category) &&
                                    TextNormalization.SameUniqueValue(group.Subcategory, subcategory)))
            await productRepository.CreateSubcategoryAsync(category, subcategory);
    }

static     async Task<Product> CreateProductAsync(IProductRepository productRepository, ProductInput input)
    {
        await EnsureProductGroupAsync(productRepository, input.Category, input.Subcategory);
        return await productRepository.CreateAsync(input);
    }
}
