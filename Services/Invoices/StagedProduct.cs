namespace BlazorStoc.Services;

// A new product prepared in the invoice pickup (step 2): checked, but not yet in the catalog. It is created, with its image, when the pickup is finished (step 3).
public sealed record StagedProduct(ProductInput Input, ProductImageData? Image);
