using System.Globalization;
using BlazorStoc.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Logging;

namespace BlazorStoc.Components.Pages;

// Invoice pickup, step 2: the products of the catalog a row stands for (by code, by the code written in the name, by the link of the supplier's own code) and the new product proposed for it.
public partial class InvoicePickup
{
    private void Rematch(PickRow item)
    {
        var nameColumn = reading!.Extraction.Columns.FirstOrDefault(column => column.Meaning == InvoiceColumnMeanings.Name)?.Id;
        var name = nameColumn is null ? "" : item.Cells.GetValueOrDefault(nameColumn, "");
        item.Match = InvoiceProductMatcher.Match(InvoiceProductMatcher.CodeOf(item.Cells.GetValueOrDefault("code"), name), name, catalog, supplierCodes, item.Cells.GetValueOrDefault("code"));
        // An exact match is the product; else the choice made among the alternatives stays only while that product is still offered.
        item.ProductId = item.Match.Exact?.Id ?? (item.Match.Alternatives.Any(candidate => candidate.Product.Id == item.ProductId) ? item.ProductId : null);
        if (item.Match.Exact is not null) item.Staged = null;   // the code exists in the catalog now: no new product is needed
    }

    private string NewProductDescription(PickRow item)
    {
        var nameColumn = reading?.Extraction.Columns.FirstOrDefault(column => column.Meaning == InvoiceColumnMeanings.Name)?.Id;
        var name = nameColumn is null ? "" : item.Cells.GetValueOrDefault(nameColumn, "");
        var cut = name.IndexOf(" - ", StringComparison.Ordinal);
        var description = (cut > 0 && name[..cut].Trim().Equals(item.Match.Code, StringComparison.OrdinalIgnoreCase) ? name[(cut + 3)..] : name).Trim();
        // The code of the supplier is kept in the description of a product named by another code (the link to it is made when the invoice is taken).
        var supplierCode = item.Cells.GetValueOrDefault("code")?.Trim() ?? "";
        return session?.IsXml == true && supplierCode.Length > 0 && SupplierProductCodeRules.Key(supplierCode) != SupplierProductCodeRules.Key(NewProductName(item))
            ? $"{description} (cod furnizor {supplierCode})" : description;
    }

    // A new product of an XML invoice is named by the code its description calls a code ("... cod GS-778"), as the code of the supplier is its own; the code of the supplier stays in the description.
    private string NewProductName(PickRow item)
    {
        if (session?.IsXml != true) return item.Match.Code;
        var nameColumn = reading?.Extraction.Columns.FirstOrDefault(column => column.Meaning == InvoiceColumnMeanings.Name)?.Id;
        return InvoiceProductMatcher.CodeInText(nameColumn is null ? "" : item.Cells.GetValueOrDefault(nameColumn, "")) ?? item.Match.Code;
    }

    // The links of the supplier's codes to the products, used by the matching of the rows; none when the supplier is not known yet.
    private IReadOnlyDictionary<string, int> supplierCodes = new Dictionary<string, int>();

    private async Task LoadSupplierCodesAsync()
    {
        supplierCodes = new Dictionary<string, int>();
        if (supplierId is not { } id || Services.GetService<ISupplierProductCodes>() is not { } codes) return;
        try { supplierCodes = await codes.GetAsync(id, lifetime.Token); }
        catch (Exception exception) when (exception is not OperationCanceledException) { Logger.LogWarning("Reading the links of the supplier codes failed ({ErrorType}).", exception.GetType().Name); }
    }

    // The code the supplier gave the product on this invoice is linked to the product that was taken, so the next invoice of the supplier proposes it.
    private async Task RememberSupplierCodeAsync(PickRow line, int? productId)
    {
        if (supplierId is not { } id || productId is not { } product || Services.GetService<ISupplierProductCodes>() is not { } codes) return;
        var code = line.Cells.GetValueOrDefault("code") ?? "";
        if (!SupplierProductCodeRules.IsLinkable(code)) return;
        if (catalog.FirstOrDefault(item => item.Id == product) is { } known && SupplierProductCodeRules.Key(known.Name) == SupplierProductCodeRules.Key(code)) return;   // the same code as the product's own
        try { await codes.LinkAsync(id, code, product, lifetime.Token); }
        catch (Exception exception) when (exception is not OperationCanceledException) { Logger.LogWarning("Linking the supplier code failed ({ErrorType}).", exception.GetType().Name); }
    }
}
