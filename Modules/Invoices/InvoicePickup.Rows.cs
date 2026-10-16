using System.Globalization;
using BlazorStoc.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Microsoft.Extensions.Logging;

namespace BlazorStoc.Components.Pages;

public partial class InvoicePickup
{
    // The rows of step 2 the user keeps taking (the selector of step 1 and the one of step 2 are the same choice).
    private bool IsOff(PickRow item) => excluded.Contains(item.RowIndex);

    // Rows whose product was already taken from this invoice are left out unless the user chose to take them again.
    private int? TakenQuantity(PickRow line) => line.Staged is null && line.ProductId is { } id && takenEntries.Where(entry => entry.ProductId == id).Sum(entry => entry.Quantity) is > 0 and var sum ? sum : null;

    // Stock of the existing product when it is negative (an operating error to be settled before the entry), else null.
    private int? NegativeStock(PickRow line) =>
        line.Staged is null && !line.Regularized && catalog.FirstOrDefault(product => product.Id == line.ProductId) is { Quantity: < 0 } found ? found.Quantity : null;

    private bool Skipped(PickRow line) => !line.Done && !line.RetakeAnyway && TakenQuantity(line) is not null;

    // The quantity of a row (a whole number, as stock is counted in pieces) or why it cannot be used.
    private (int? Quantity, string? Error) QuantityOf(PickRow line)
    {
        if (QuantityColumnId is not { } id) return (null, "Șablonul nu citește o coloană cu cantitatea.");
        var text = line.Cells.GetValueOrDefault(id, "");
        if (InvoiceValues.ParseNumber(text, NumberHint) is not { } number) return (null, "Cantitatea „" + text + "” nu este un număr.");
        if (number != decimal.Truncate(number) || number < 1 || number > StockMovementRules.MaxQuantity)
            return (null, $"Cantitatea trebuie să fie un număr întreg între 1 și {StockMovementRules.MaxQuantity:N0} (este „{text}”). Corecteaz-o în pasul 2.");
        return ((int)number, null);
    }

    // The cells of the row, with the product code as the pickup found it (so that <Cod produs> has a value whether or not the template has a code column).
    private IReadOnlyDictionary<string, string> WithCode(PickRow line)
    {
        var cells = new Dictionary<string, string>(line.Cells);
        if (!cells.TryGetValue("code", out var code) || code.Length == 0) cells["code"] = line.Match.Code;
        return cells;
    }

    private static string SourceKey(InvoiceTableRow row) =>
        row.Page.ToString(CultureInfo.InvariantCulture) + "|" + string.Join("\u001f", row.Cells.OrderBy(cell => cell.Key, StringComparer.Ordinal).Select(cell => cell.Key + "=" + cell.Value));

    private void EditCell(int index, string columnId, string value)
    {
        if (ShownColumns.FirstOrDefault(column => column.Id == columnId) is { } column && IsNumeric(column)) value = NumbersOnly(value);
        pick[index].Cells[columnId] = value;
        Rematch(pick[index]);
    }

    // Quantity, prices and taxes are read as numbers, so only numbers can be typed in them: digits and one comma (numeric-field.js refuses the
    // rest as it is typed; this is the same rule for what reaches the server in another way). A dot of the text as it was read stays.
    private static bool IsNumeric(ShownColumn column) => column.Meaning is InvoiceColumnMeanings.Quantity or InvoiceColumnMeanings.UnitPrice or InvoiceColumnMeanings.Value
        or InvoiceColumnMeanings.ValueWithVat or InvoiceColumnMeanings.VatAmount or InvoiceColumnMeanings.VatRate or InvoiceColumnMeanings.Discount;

    internal static string NumbersOnly(string value)
    {
        var comma = false;
        var result = new System.Text.StringBuilder();
        foreach (var character in value)
        {
            if (char.IsAsciiDigit(character) || character == '.') result.Append(character);
            else if (character == ',' && !comma) { result.Append(character); comma = true; }
        }
        return result.ToString();
    }

    private void OpenNewProduct(int index) { newProductFor = pick[index]; newProductKey++; }
    private Task CloseNewProduct() { newProductFor = null; return Task.CompletedTask; }

    // The product was prepared, not saved: the row keeps it for the last step (and no existing product is chosen for it).
    private Task NewProductStagedAsync(StagedProduct staged)
    {
        if (newProductFor is { } item) { item.Staged = staged; item.ProductId = null; }
        newProductFor = null;
        return Task.CompletedTask;
    }

    private sealed record ShownColumn(string Id, string Title, string Meaning);

    // Lines needed by a name in its field (about 40 characters a line; browsers that size the field to its text ignore this).
    private static int NameRows(string text) => Math.Clamp((text.Length + 39) / 40, 1, 8);

    // The name gets the room; the code a medium field; the unit is the shortest; quantity, prices and taxes are short.
    private static string ColumnClass(ShownColumn column) => column.Meaning switch
    {
        InvoiceColumnMeanings.Name => "pickup-col-name",
        InvoiceColumnMeanings.Code => "pickup-col-code",
        InvoiceColumnMeanings.Unit => "pickup-col-unit",
        _ => "pickup-col-short"
    };

    // Quantities and amounts must be numbers (missing is not acceptable for the first three); discounts and VAT may be empty.
    // The characters of a cell that text recognition makes out of a stroke or a stain (see InvoiceAmbiguity): the cell is to be checked against the invoice.
    private string OcrDoubt(string text) => session is { IsXml: false } && session.Document.Pages.Any(page => page.Source == InvoiceSources.Ocr) ? InvoiceAmbiguity.Found(text) : "";

    private bool NotANumber(ShownColumn column, string text)
    {
        switch (column.Meaning)
        {
            case InvoiceColumnMeanings.Quantity or InvoiceColumnMeanings.UnitPrice or InvoiceColumnMeanings.Value or InvoiceColumnMeanings.ValueWithVat:
                return InvoiceValues.ParseNumber(text, NumberHint) is null;
            case InvoiceColumnMeanings.VatAmount or InvoiceColumnMeanings.Discount:
                return text.Trim().Length > 0 && InvoiceValues.ParseNumber(text, NumberHint) is null;
            default:
                return false;
        }
    }

    private static string F(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private void QueueTakenPreview()
    {
        takenPreview = [];
        if (session is null || reading is null || rows.Count == 0) { takenPreviewVersion++; takenChecking = false; return; }
        takenChecking = true;
        _ = RefreshTakenPreviewAsync(++takenPreviewVersion);
    }

    private async Task RefreshTakenPreviewAsync(int version)
    {
        try
        {
            await takenPreviewGate.WaitAsync(lifetime.Token);
            try
            {
                if (version != takenPreviewVersion || session is null || reading is null) return;   // a newer reading of the rows is queued
                var firstLoad = headerPrefilledFor != session.Id || catalog.Count == 0;
                if (firstLoad)
                {
                    await LoadSuppliersAsync();   // the number, date and supplier read from the invoice, and what was taken from that invoice before
                    catalog = await ProductsRepository.GetProductsAsync(lifetime.Token);
                    await LoadVariantsAsync();
                }
                if (firstLoad || takenPreviewSupplier != supplierId) { await LoadSupplierCodesAsync(); takenPreviewSupplier = supplierId; }
                if (version != takenPreviewVersion) return;
                var preview = new Dictionary<int, (string Product, int Quantity)>();
                if (existingInvoice is not null && takenEntries.Count > 0)
                {
                    for (var index = 0; index < rows.Count; index++)
                    {
                        var probe = new PickRow { Cells = new Dictionary<string, string>(rows[index].Cells) };
                        Rematch(probe);
                        if (TakenQuantity(probe) is { } quantity) preview[index] = (catalog.FirstOrDefault(product => product.Id == probe.ProductId)?.Name ?? "", quantity);
                    }
                }
                takenPreview = preview;
                foreach (var index in preview.Keys.Where(index => index < rows.Count))
                    if (takenAutoDone.Add(SourceKey(rows[index]))) excluded.Add(index);   // products already taken start unselected
            }
            finally
            {
                takenPreviewGate.Release();
                if (version == takenPreviewVersion) takenChecking = false;
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return; }
        catch (Exception exception)
        {
            Logger.LogWarning("Checking the rows already taken from the invoice failed ({ErrorType}).", exception.GetType().Name);
            takenPreview = [];
            takenChecking = false;
        }
        await InvokeAsync(StateHasChanged);
    }

    private void ToggleRow(int index, bool take) { if (take) excluded.Remove(index); else excluded.Add(index); }

    private void ToggleAll(bool take)
    {
        excluded.Clear();
        if (!take) for (var index = 0; index < rows.Count; index++) excluded.Add(index);
    }

    // Where the rows are on a page: between its first and its last demarcation line (the overlay of the columns stays inside the table).
    private (double Top, double Bottom)? BodyRange(InvoicePageData page)
    {
        var lines = separators.Where(item => item.Page == page.Number).Select(item => item.Y).ToList();
        return lines.Count < 2 ? null : (lines.Min(), lines.Max());
    }

    private void Select(string id) => selectedId = selectedId == id ? "" : id;

    private void DeleteSelected()
    {
        if (selectedId.Length == 0) return;
        separators.RemoveAll(item => item.Id == selectedId);
        selectedId = "";
        RereadRows();
    }
}
