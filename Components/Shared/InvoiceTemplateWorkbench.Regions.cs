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
    // Header fields are listed by party: the supplier, then the invoice's own data and anything else.
    private static readonly (string Key, string Title)[] FieldGroups = [("supplier", "Furnizor (vânzător)"), ("other", "Date factură și altele")];

    private void ResetZone(DraftColumn column)
    {
        column.Top = column.Bottom = 0; column.TopAnchor = column.BottomAnchor = ""; column.BottomPage = 0;
        Refresh();
    }

    // The extent of the cell of the table header a column is made from: its own, else the header band of the table.
    private (double Top, double Bottom) CellExtent(DraftColumn column) =>
        column.CellTop > 0 && column.CellBottom > column.CellTop ? (column.CellTop, column.CellBottom) : (draft!.HeaderTop, Math.Max(draft.HeaderBottom, draft.HeaderTop + 1));

    private static string HeaderCellTitle(DraftColumn column) =>
        "Celulă de antet: " + (column.Label.Length > 0 ? column.Label : "fără denumire") + " — lățimea ei este lățimea coloanei" + (column.ZoneDrawn ? "" : " (zona coloanei nu e desenată)");

    // The vertical extent of a column on a page: the data zone the user gave it (its top on the header page, its bottom on the page of the element it
    // ends over), else the body of the table; null for a page after the one where its zone ends.
    private (double Top, double Bottom)? ColumnExtent(DraftColumn column, int page, double bodyTop, double bodyBottom)
    {
        var bottomPage = column.BottomPage > 0 ? column.BottomPage : draft!.HeaderPage;
        if (column.Bottom > 0 && page > bottomPage) return null;
        var top = column.Top > 0 && page == draft!.HeaderPage ? column.Top : bodyTop;
        var bottom = column.Bottom > 0 && page == bottomPage ? column.Bottom : bodyBottom;
        return (top, Math.Max(bottom, top + 1));
    }

    // The vertical extent of the table body on a page, from the rows read there (or from the header down to the page's rows).
    private (double Top, double Bottom) BodyExtent(int page)
    {
        var rows = preview?.Rows.Where(row => row.Page == page).ToList() ?? [];
        var data = session!.Document.Pages.First(item => item.Number == page);
        if (rows.Count > 0) return (rows.Min(row => row.Top), rows.Max(row => row.Bottom));
        // Before any reading (a template opened for editing, not analysed yet) the columns span down to where the rows ended when the template was saved.
        if (preview is null && draft!.HasTable && page == draft.HeaderPage)
            return (draft.HeaderBottom, draft.BodyBottom > draft.HeaderBottom ? draft.BodyBottom : draft.HeaderBottom + Math.Min(data.Height * 0.25, 120));
        return page == draft!.HeaderPage ? (draft.HeaderBottom, draft.HeaderBottom + 1) : (0, 1);
    }

    // Called by invoice-template.js when a zone was marked on the page without a drawing mode: selects the found elements mostly inside it.
    [JSInvokable]
    public async Task OnRegionSelected(int page, double x, double y, double width, double height)
    {
        await InvokeAsync(() =>
        {
            if (draft is null) return;
            popupOpen = false; selectedId = ""; selectedKind = ' ';
            multiIds.Clear();
            foreach (var field in draft.Fields.Where(field => field.Page == page && IsShown(field)))
            {
                var overlapX = Math.Min(x + width, field.X + field.Width) - Math.Max(x, field.X);
                var overlapY = Math.Min(y + height, field.Y + field.Height) - Math.Max(y, field.Y);
                if (overlapX > 0 && overlapY > 0 && overlapX * overlapY >= 0.5 * field.Width * field.Height) multiIds.Add(field.Id);
            }
            StateHasChanged();
        });
    }

    private void Select(char kind, string id)
    {
        multiIds.Clear(); selectedKind = kind; selectedId = id;
        var field = kind == 'f' ? draft!.Fields.FirstOrDefault(item => item.Id == id) : null;
        if (field is not null) currentPage = field.Page;
    }

    private void MoveField(DraftField field, object? x = null, object? y = null, object? width = null, object? height = null)
    {
        field.Mode = InvoiceFieldModes.Region;
        if (Number(x) is { } nx) field.X = nx;
        if (Number(y) is { } ny) field.Y = ny;
        if (Number(width) is { } nw && nw >= 1) field.Width = nw;
        if (Number(height) is { } nh && nh >= 1) field.Height = nh;
        field.Value = InvoiceTemplateEngine.ReadRegion(session!.Document, field.Page, field.X, field.Y, field.Width, field.Height);
        Refresh();
    }

    private void MoveColumn(DraftColumn column, object? left = null, object? right = null)
    {
        if (Number(left) is { } nl) column.Left = nl;
        if (Number(right) is { } nr) column.Right = nr;
        if (column.Right < column.Left + 1) column.Right = column.Left + 1;
        UpdateAnchors(column);
        Refresh();
    }

    private void MoveHeader(object? bottom)
    {
        if (Number(bottom) is { } value && value > draft!.HeaderTop) { draft.HeaderBottom = value; Refresh(); }
    }

    // The selected element as a box on one page (columns: the body of the table), with the label it is tied to.
    private sealed record EditBox(char Kind, string Id, double X, double Y, double W, double H, string Label);

    private EditBox? EditBoxOn(int page)
    {
        if (draft is null || selectedId.Length == 0) return null;
        if (selectedKind == 'f')
        {
            var field = draft.Fields.FirstOrDefault(item => item.Id == selectedId);
            if (field is null || field.Page != page) return null;
            var name = InvoiceTemplateDraft.EffectiveLabel(field);
            var label = name.Length > 0 ? "Etichetă: " + name : field.Manual ? "Câmp desenat de tine (fără etichetă)" : "Fără etichetă (valoare găsită direct)";
            return new EditBox('f', field.Id, field.X, field.Y, field.Width, field.Height, label);
        }
        if (selectedKind == 'H' && draft.HasTable && draft.HeaderPage == page)
            return new EditBox('H', HeaderBandId, TableLeft, draft.HeaderTop, Math.Max(1, TableRight - TableLeft), Math.Max(1, draft.HeaderBottom - draft.HeaderTop), "Capul de tabel (sus și jos: de unde până unde e antetul)");
        if (selectedKind == 'h' && draft.HasTable && draft.HeaderPage == page && draft.Columns.FirstOrDefault(item => item.Id == selectedId) is { } cellColumn)
        {
            var (cellTop, cellBottom) = CellExtent(cellColumn);
            return new EditBox('h', cellColumn.Id, cellColumn.Left, cellTop, Math.Max(1, cellColumn.Right - cellColumn.Left), Math.Max(1, cellBottom - cellTop), "Celulă de antet: " + (cellColumn.Label.Length > 0 ? cellColumn.Label : "fără denumire") + " (lățimea ei = lățimea coloanei)");
        }
        if (selectedKind == 'c' && draft.HasTable)
        {
            var column = draft.Columns.FirstOrDefault(item => item.Id == selectedId);
            if (column is null) return null;
            var (bodyTop, bodyBottom) = BodyExtent(page);
            if (!column.ZoneDrawn || ColumnExtent(column, page, bodyTop, bodyBottom) is not { } zone) return null;
            return new EditBox('c', column.Id, column.Left, zone.Top, Math.Max(1, column.Right - column.Left), Math.Max(1, zone.Bottom - zone.Top),
                "Zona de date a coloanei " + (column.Label.Length > 0 ? column.Label : "fără denumire") + " (doar înălțimea se poate schimba)");
        }
        return null;
    }

    private void DeleteSelected()
    {
        if (draft is null) return;
        if (selectedKind == 'f' && draft.Fields.FirstOrDefault(item => item.Id == selectedId) is { } field) RemoveField(field);
        else if (selectedKind is 'c' or 'h' && draft.Columns.FirstOrDefault(item => item.Id == selectedId) is { } column) RemoveColumn(column);
    }

    // Called by invoice-template.js when the page was pressed outside every element: the selection is cancelled.
    [JSInvokable]
    public async Task OnBackgroundClicked()
    {
        await InvokeAsync(() => { selectedId = ""; selectedKind = ' '; popupOpen = false; multiIds.Clear(); StateHasChanged(); });
    }

    // Called by invoice-template.js when a selected element was dragged or resized (points of the page).
    [JSInvokable]
    public async Task OnRegionAdjusted(string kind, string id, double x, double y, double width, double height, int page)
    {
        await InvokeAsync(() =>
        {
            if (draft is null || session is null || width < 1 || height < 1) return;
            if (kind == "f" && draft.Fields.FirstOrDefault(item => item.Id == id) is { } field)
            {
                field.Mode = InvoiceFieldModes.Region;
                field.X = x; field.Y = y; field.Width = width; field.Height = height;
                field.Value = InvoiceTemplateEngine.ReadRegion(session.Document, field.Page, field.X, field.Y, field.Width, field.Height);
            }
            else if (kind == "H" && draft.HasTable)
            {
                // The table header: where it starts and ends (the rows start under it).
                draft.HeaderTop = y; draft.HeaderBottom = y + height;
            }
            else if (kind == "h" && draft.Columns.FirstOrDefault(item => item.Id == id) is { } cell)
            {
                // A cell of the table header: its width is the width of the column; its text is the label of the column.
                cell.Left = x; cell.Right = Math.Max(x + 1, x + width);
                cell.CellTop = y; cell.CellBottom = y + height;
                var text = InvoiceLayout.TextOf(WordsIn(page, x, y, width, height));
                if (text.Length > 0)
                {
                    var renamed = cell.Label != cell.HeaderText;
                    cell.HeaderText = text;
                    if (!renamed && cell.Label != text) RenameColumn(cell, text);
                }
                UpdateAnchors(cell);
            }
            else if (kind == "c" && draft.Columns.FirstOrDefault(item => item.Id == id) is { } column)
            {
                // The zone of a column: only its height changes (the width belongs to the header cell). Its top and bottom are tied to the element
                // right above and right below them (their text), so that in another file the zone is found from those elements, not from coordinates.
                var (bodyTop, bodyBottom) = BodyExtent(page);
                var before = ColumnExtent(column, page, bodyTop, bodyBottom) ?? (bodyTop, bodyBottom);
                if (page == draft.HeaderPage && Math.Abs(y - before.Top) > 1.5) column.Top = y;
                if (Math.Abs(y + height - before.Bottom) > 1.5) { column.Bottom = y + height; column.BottomPage = page == draft.HeaderPage ? 0 : page; }
                UpdateAnchors(column);
            }
            else return;
            Refresh();
            StateHasChanged();
        });
    }

    // The text of the element right above the top and right below the bottom of the zone of a column, which is what the zone is tied to.
    private void UpdateAnchors(DraftColumn column)
    {
        if (session is null || draft is null) return;
        InvoicePageData? PageOf(int number) => session.Document.Pages.FirstOrDefault(item => item.Number == number);
        if (column.Top > 0 && PageOf(draft.HeaderPage) is { } first) column.TopAnchor = InvoiceTemplateEngine.AnchorAbove(first, column.Left, column.Right, column.Top);
        if (column.Bottom > 0 && PageOf(column.BottomPage > 0 ? column.BottomPage : draft.HeaderPage) is { } last) column.BottomAnchor = InvoiceTemplateEngine.AnchorBelow(last, column.Left, column.Right, column.Bottom);
    }

    // A field or a column taken off the page leaves the template with everything built on it: its label is no longer offered and its marks
    // are taken out of the description of the stock entry.
    private void RemoveField(DraftField field)
    {
        var label = InvoiceTemplateDraft.EffectiveLabel(field);
        draft!.Fields.Remove(field);
        if (label.Length > 0 && !InvoiceProductDescription.Labels(draft).Any(item => InvoiceValues.Normalize(item.Label) == InvoiceValues.Normalize(label)))
            draft.ProductDescription = InvoiceProductDescription.RemoveMark(draft.ProductDescription, label);
        ResetSelection(); Refresh();
    }

    private void RemoveColumn(DraftColumn column)
    {
        var label = InvoiceProductDescription.ColumnLabels(draft!).FirstOrDefault(item => item.Column == column).Label;
        draft!.Columns.Remove(column);
        if (label is { Length: > 0 } && !InvoiceProductDescription.Labels(draft).Any(item => InvoiceValues.Normalize(item.Label) == InvoiceValues.Normalize(label)))
            draft.ProductDescription = InvoiceProductDescription.RemoveMark(draft.ProductDescription, label);
        RefreshIndex(); ResetSelection(); Refresh();
    }

    private void ToggleDraw(string mode) => drawMode = drawMode == mode ? "" : mode;

    // Called by invoice-template.js when the user has dragged a rectangle on the page (points of the page).
    [JSInvokable]
    public async Task OnRegionDrawn(int page, double x, double y, double width, double height)
    {
        await InvokeAsync(() =>
        {
            if (draft is null || session is null || width < 2 || height < 2) return;
            var id = "m" + (++drawCounter).ToString(CultureInfo.InvariantCulture);
            if (drawMode == "column")
            {
                // The rectangle is a cell of the table header: its width is the width of the column, its text the label. The zone of the column is drawn
                // afterwards ("Desenează zona unei coloane"); until then the template cannot be saved.
                var first = !draft.Columns.Any();
                if (first && !draft.HasTable)
                {
                    draft.HasTable = true;
                    draft.HeaderPage = page;
                    draft.HeaderTop = y;
                    draft.HeaderBottom = y + height;
                }
                var text = InvoiceLayout.TextOf(WordsIn(page, x, y, width, height));
                draft.Columns.Add(new DraftColumn { Id = id, Label = text, HeaderText = text, Meaning = InvoiceColumnMeanings.Other, Use = true, Left = x, Right = x + width, Manual = true,
                    CellTop = y, CellBottom = y + height, ZoneDrawn = false });
                InferColumnMeaning(draft.Columns[^1]);
                RefreshIndex();
                OpenEdit('h', id);
            }
            else if (drawMode == "zone")
            {
                // The rectangle is the zone where a column has its values: it belongs to the header cell it is under (by width), and only its height counts.
                var target = draft.Columns.Where(item => InvoiceLayout.Overlap(item.Left, item.Right, x, x + width) > 0).OrderByDescending(item => InvoiceLayout.Overlap(item.Left, item.Right, x, x + width)).FirstOrDefault();
                if (target is null || !draft.HasTable) { error = "Zona unei coloane se desenează sub celula ei din capul de tabel."; drawMode = ""; StateHasChanged(); return; }
                if (page == draft.HeaderPage) target.Top = y;
                target.Bottom = y + height; target.BottomPage = page == draft.HeaderPage ? 0 : page;
                target.ZoneDrawn = true;
                UpdateAnchors(target);
                Select('c', target.Id);
            }
            else if (drawMode == "field")
            {
                var value = InvoiceTemplateEngine.ReadRegion(session.Document, page, x, y, width, height);
                draft.Fields.Add(new DraftField { Id = id, Name = "", Use = true, Page = page, X = x, Y = y, Width = width, Height = height, Value = value, Manual = true, Mode = InvoiceFieldModes.Region, Kind = InvoiceValues.KindOf(value, decimalHint) switch { InvoiceValueKind.Number => "number", InvoiceValueKind.Date => "date", _ => "text" } });
                OpenEdit('f', id);
            }
            drawMode = "";
            Refresh();
            StateHasChanged();
        });
    }
}
