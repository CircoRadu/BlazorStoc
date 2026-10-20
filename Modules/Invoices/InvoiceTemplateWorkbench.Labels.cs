using System.Globalization;
using BlazorStoc.Services;
using BlazorStoc.Components.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Microsoft.Extensions.Logging;

namespace BlazorStoc.Components.Shared;

public partial class InvoiceTemplateWorkbench
{
    private static string F(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    private static string ShortValue(string value) => value.Length <= 60 ? value : value[..57] + "…";

    private static bool IsNumericMeaning(string meaning) => meaning is InvoiceColumnMeanings.Quantity or InvoiceColumnMeanings.UnitPrice or InvoiceColumnMeanings.Value or InvoiceColumnMeanings.VatRate
        or InvoiceColumnMeanings.VatAmount or InvoiceColumnMeanings.ValueWithVat or InvoiceColumnMeanings.Discount;

    // A field is shown in the list and on the page when it means something, is used, was drawn by hand, or "show all" is on.
    private bool IsShown(DraftField field) => ConflictIds.Contains(field.Id) || showAllFields || field.Use || field.Manual || field.Meaning.Length > 0 || field.Confidence >= 0.85 || (selectedKind == 'f' && selectedId == field.Id);

    // A field without a meaning of its own (or a custom one) is grouped by the heading it was found under (VANZATOR / CUMPARATOR).
    private static string FieldGroupOf(DraftField field) =>
        field.Meaning.Length > 0 && field.Meaning != InvoiceFieldMeanings.Custom ? PreviewGroupOf(field.Meaning)
        : field.Section is InvoiceVocabulary.SupplierSection ? field.Section : "other";

    private static string PreviewGroupOf(string? meaning) =>
        meaning is not null && meaning.StartsWith("supplier.", StringComparison.Ordinal) ? "supplier" : "other";

    // Each column has its own colour (golden-angle hues), so that where columns overlap the mix of colours shows it.
    private string ColumnColor(DraftColumn column) => $"hsl({(int)(Math.Max(0, draft!.Columns.IndexOf(column)) * 137.5 % 360)} 70% 42%)";

    private string FieldClass(DraftField field) => (field.Use ? "used" : "unused") + (multiIds.Contains(field.Id) ? " multi" : "") + (ConflictIds.Contains(field.Id) ? " conflict" : "") + (selectedKind == 'f' && selectedId == field.Id ? " selected" : "");
    private string ColumnClass(DraftColumn column) => (column.Use && column.Meaning != InvoiceColumnMeanings.Ignore ? "used" : "unused") + (selectedKind == 'c' && selectedId == column.Id ? " selected" : "");
    private static string FieldTitle(DraftField field) => field.Name.Length > 0 ? field.Name : "Câmp";
    private static string ColumnTitle(DraftColumn column) => column.Label.Length > 0 ? column.Label : "Coloană";

    // The data zone of a column in words: what it is anchored to (the element above its start, the element under its end).
    private static string ZoneText(DraftColumn column)
    {
        if (column.Top <= 0 && column.Bottom <= 0) return "automată (de la antetul tabelului până la sfârșitul rândurilor).";
        var start = column.Top > 0 ? (column.TopAnchor.Length > 0 ? $"începe sub elementul „{column.TopAnchor}”" : "începe sub antet") : "începe sub antetul tabelului";
        var end = column.Bottom > 0 ? (column.BottomAnchor.Length > 0 ? $"se termină deasupra elementului „{column.BottomAnchor}”" : "se termină la marginea aleasă (nu are un element dedesubt, deci citirea nu o limitează)") : "se termină la sfârșitul rândurilor";
        return start + " și " + end + ".";
    }

    private void UseFreeLabel(DraftField field)
    {
        var label = draft!.FreeLabelFor(field);
        field.Meaning = InvoiceFieldMeanings.Custom;
        field.Name = label;
        Refresh();
    }

    // A used field whose label another used field already has cannot be left: the labels are what the description and the page refer to.
    private bool LabelTaken(DraftField field) => field.Use && draft!.SameLabelAs(field).Any(item => item.Use &&
        InvoiceValues.Normalize(InvoiceTemplateDraft.EffectiveLabel(item)) == InvoiceValues.Normalize(InvoiceTemplateDraft.EffectiveLabel(field)));

    private void TryClosePopup()
    {
        if (PopupField() is { } field && (InvoiceTemplateDraft.EffectiveLabel(field).Length == 0 || LabelTaken(field))) return;
        popupOpen = false;
    }

    private void SetProductDescription(string text)
    {
        draft!.ProductDescription = text;
        Refresh();
    }

    // ---- colours of the labels of the description: a column's value has the colour of the column on the page; the operation has a colour that
    // does not fall on the colour of any of those columns ----
    private int ColumnHue(DraftColumn column) => (int)(Math.Max(0, draft!.Columns.IndexOf(column)) * 137.5 % 360);

    private string ChipStyle(DraftColumn? column) => column is null ? "" : $"color: {ColumnColor(column)}; border-color: {ColumnColor(column)}";

    private string? MarkColor(string name)
    {
        var key = InvoiceValues.Normalize(name);
        return InvoiceProductDescription.ColumnLabels(draft!).FirstOrDefault(item => item.Column is not null && InvoiceValues.Normalize(item.Label) == key).Column is { } column ? ColumnColor(column) : null;
    }

    private bool IsKnownDescriptionMark(string mark)
    {
        var key = InvoiceValues.Normalize(mark);
        return InvoiceProductDescription.Labels(draft!).Any(item => InvoiceValues.Normalize(item.Label) == key);
    }

    // The operation goes where the cursor is, with the cursor left between the braces: the labels clicked next are added inside it.
    private async Task InsertOperation(string name)
    {
        var operation = name + "{}";
        if (descriptionField is not null && await descriptionField.InsertAsync(operation, 1, true)) return;
        var text = draft!.ProductDescription;
        SetProductDescription(text + (text.Length > 0 && !char.IsWhiteSpace(text[^1]) ? " " : "") + operation);
    }

    // The label goes where the cursor is in the description; at its end when that is not possible (the text would not fit, the field is gone).
    private async Task AppendLabel(string label)
    {
        var mark = "<" + label + ">";
        if (descriptionField is not null && await descriptionField.InsertAsync(mark, 0, true)) return;
        var text = draft!.ProductDescription;
        SetProductDescription(text + (text.Length > 0 && !char.IsWhiteSpace(text[^1]) ? " " : "") + mark);
    }

    private void ClearMulti() => multiIds.Clear();

    private void DeleteMulti()
    {
        if (draft is null) return;
        foreach (var field in draft.Fields.Where(field => multiIds.Contains(field.Id)).ToList()) RemoveField(field);
        ResetSelection();
        Refresh();
    }

    private DraftField? PopupField() => selectedKind == 'f' ? draft?.Fields.FirstOrDefault(item => item.Id == selectedId) : null;
    private DraftColumn? PopupColumn() => selectedKind is 'c' or 'h' ? draft?.Columns.FirstOrDefault(item => item.Id == selectedId) : null;
    private void OpenEdit(char kind, string id) { Select(kind, id); popupOpen = true; }
    private void ClosePopup() => popupOpen = false;
    private void BackdropClickField() { var close = downOnBackdrop; downOnBackdrop = false; if (close) TryClosePopup(); }
    private void BackdropClickColumn() { var close = downOnBackdrop; downOnBackdrop = false; if (close) ClosePopup(); }

    private void RenameField(DraftField field, string name)
    {
        var old = field.Use ? InvoiceTemplateDraft.EffectiveLabel(field) : "";
        field.Name = name;
        InferFieldMeaning(field);
        if (old.Length > 0 && field.Use) draft!.ProductDescription = InvoiceProductDescription.RenameMark(draft.ProductDescription, old, InvoiceTemplateDraft.EffectiveLabel(field));
        Refresh();
    }

    private void RenameColumn(DraftColumn column, string name)
    {
        var old = InvoiceProductDescription.ColumnLabels(draft!).FirstOrDefault(item => item.Column == column).Label;
        column.Label = name;
        InferColumnMeaning(column);
        var now = InvoiceProductDescription.ColumnLabels(draft!).FirstOrDefault(item => item.Column == column).Label;
        if (old is { Length: > 0 } && now is { Length: > 0 }) draft!.ProductDescription = InvoiceProductDescription.RenameMark(draft.ProductDescription, old, now);
        Refresh();
    }

    private void SetFieldUse(DraftField field, bool use) { field.Use = use; if (use && field.Meaning.Length == 0 && field.Name.Length == 0) field.Name = field.LabelText; Refresh(); }

    // The name of a field or of a column decides its vocabulary: what the template calls it is what the labels and the page show. The role the engine
    // reads it by (name, quantity, price, invoice number ...) is only a guess from that name, never chosen by the user; a column the dictionary
    // does not know is a column of its own, and a role belongs to one used column.
    private void InferColumnMeaning(DraftColumn column)
    {
        var (meaning, _) = InvoiceVocabulary.MatchColumn(column.Label);
        if (meaning.Length == 0 || meaning == InvoiceColumnMeanings.Ignore) meaning = InvoiceColumnMeanings.Other;
        if (meaning != InvoiceColumnMeanings.Other && draft!.Columns.Any(item => item != column && item.Use && item.Meaning == meaning)) meaning = InvoiceColumnMeanings.Other;
        column.Meaning = meaning;
    }

    // A field renamed to a name the dictionary knows takes that role; with another name it keeps the role it had.
    private static void InferFieldMeaning(DraftField field)
    {
        var tokens = InvoiceVocabulary.Tokens(field.Name);
        var (words, meaning) = InvoiceVocabulary.MatchFieldLabelPrefix(tokens);
        if (meaning is not { Length: > 0 } || words != tokens.Length || meaning == InvoiceFieldMeanings.Custom) return;
        field.Meaning = meaning;
        if (field.Kind == "text") field.Kind = meaning is InvoiceFieldMeanings.InvoiceDate or InvoiceFieldMeanings.DueDate ? "date"
            : meaning is InvoiceFieldMeanings.TotalNet or InvoiceFieldMeanings.TotalVat or InvoiceFieldMeanings.Total ? "number" : "text";
    }

    private void SetColumnUse(DraftColumn column, bool use)
    {
        column.Use = use;
        if (use && column.Meaning == InvoiceColumnMeanings.Ignore) InferColumnMeaning(column);
        RefreshIndex();
        Refresh();
    }

    private static double? Number(object? value) =>
        value is null ? null : double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.CurrentCulture, out number) ? number : null;
}
