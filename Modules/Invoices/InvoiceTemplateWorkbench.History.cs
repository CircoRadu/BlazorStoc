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
    // ---- queue of executed actions (undo / redo): every change of the draft is recorded by Refresh() as a snapshot pair with a name ----
    private sealed record HistoryEntry(string Name, string Before, string After);

    private void ResetHistory() { undoStack.Clear(); redoStack.Clear(); lastSnapshot = null; cuiFieldSeen = false; }

    private void RecordHistory()
    {
        if (replaying) return;
        if (draft is null) { lastSnapshot = null; return; }
        var now = System.Text.Json.JsonSerializer.Serialize(draft, SnapshotOptions);
        if (lastSnapshot is null) { lastSnapshot = now; return; }
        if (now == lastSnapshot) return;
        var name = DescribeChange(lastSnapshot, now);
        undoStack.Add(new HistoryEntry(name, lastSnapshot, now));
        if (undoStack.Count > HistoryLimit) undoStack.RemoveAt(0);
        redoStack.Clear();
        lastSnapshot = now;
    }

    // A readable name for what changed between two snapshots (what the user sees on the Undo / Redo buttons).
    private static string DescribeChange(string before, string after)
    {
        var a = System.Text.Json.JsonSerializer.Deserialize<InvoiceTemplateDraft>(before, SnapshotOptions);
        var b = System.Text.Json.JsonSerializer.Deserialize<InvoiceTemplateDraft>(after, SnapshotOptions);
        if (a is null || b is null) return "Modificare șablon";
        static string FieldName(DraftField field) => field.Name.Length > 0 ? field.Name : field.LabelText.Length > 0 ? field.LabelText : "(fără denumire)";
        static string ColumnName(DraftColumn column) => column.Label.Length > 0 ? column.Label : "(fără denumire)";
        if (b.Fields.Count == a.Fields.Count + 1 && b.Fields.FirstOrDefault(field => a.Fields.All(old => old.Id != field.Id)) is { } addedField) return "Adăugare câmp";
        if (a.Fields.Count == b.Fields.Count + 1 && a.Fields.FirstOrDefault(field => b.Fields.All(now => now.Id != field.Id)) is { } removedField) return $"Ștergere câmp „{FieldName(removedField)}”";
        if (b.Fields.Count < a.Fields.Count) return $"Ștergere a {a.Fields.Count - b.Fields.Count} câmpuri";
        if (b.Columns.Count == a.Columns.Count + 1) return "Adăugare coloană";
        if (a.Columns.Count == b.Columns.Count + 1 && a.Columns.FirstOrDefault(column => b.Columns.All(now => now.Id != column.Id)) is { } removedColumn) return $"Ștergere coloană „{ColumnName(removedColumn)}”";
        foreach (var now in b.Fields)
        {
            var old = a.Fields.FirstOrDefault(field => field.Id == now.Id);
            if (old is null || System.Text.Json.JsonSerializer.Serialize(old, SnapshotOptions) == System.Text.Json.JsonSerializer.Serialize(now, SnapshotOptions)) continue;
            if (old.X != now.X || old.Y != now.Y || old.Width != now.Width || old.Height != now.Height)
                return old.Width != now.Width || old.Height != now.Height ? $"Redimensionare câmp „{FieldName(now)}”" : $"Mutare câmp „{FieldName(now)}”";
            if (old.Use != now.Use) return (now.Use ? "Folosire câmp „" : "Renunțare la câmp „") + FieldName(now) + "”";
            return $"Modificare câmp „{FieldName(now)}”";
        }
        foreach (var now in b.Columns)
        {
            var old = a.Columns.FirstOrDefault(column => column.Id == now.Id);
            if (old is null || System.Text.Json.JsonSerializer.Serialize(old, SnapshotOptions) == System.Text.Json.JsonSerializer.Serialize(now, SnapshotOptions)) continue;
            if (old.Left != now.Left || old.Right != now.Right) return $"Mutare/redimensionare coloană „{ColumnName(now)}”";
            return $"Modificare coloană „{ColumnName(now)}”";
        }
        if (a.ProductDescription != b.ProductDescription) return "Modificare descriere intrare în stoc a produsului";
        return "Modificare opțiuni tabel";
    }

    private void Undo() => Step(undoStack, redoStack, entry => entry.Before);
    private void Redo() => Step(redoStack, undoStack, entry => entry.After);

    private void Step(List<HistoryEntry> from, List<HistoryEntry> to, Func<HistoryEntry, string> target)
    {
        if (draft is null || from.Count == 0) return;
        var entry = from[^1];
        from.RemoveAt(from.Count - 1);
        var restored = System.Text.Json.JsonSerializer.Deserialize<InvoiceTemplateDraft>(target(entry), SnapshotOptions);
        if (restored is null) return;
        to.Add(entry);
        draft = restored;
        lastSnapshot = target(entry);
        multiIds.Clear(); popupOpen = false;
        if (selectedId.Length > 0 && !(selectedKind == 'f' ? draft.Fields.Any(field => field.Id == selectedId) : selectedKind == 'H' || draft.Columns.Any(column => column.Id == selectedId))) { selectedId = ""; selectedKind = ' '; }
        RefreshIndex();
        RefreshPreviewOnly();
    }

    // Called by invoice-template.js for the Delete key.
    [JSInvokable]
    public async Task OnDeleteKey()
    {
        await InvokeAsync(() => { if (multiIds.Count > 0) DeleteMulti(); else DeleteSelected(); StateHasChanged(); });
    }

    // Called by invoice-template.js for Ctrl+Z / Ctrl+Y outside of text fields.
    [JSInvokable]
    public async Task OnUndoRedoKey(bool redo)
    {
        await InvokeAsync(() => { if (redo) Redo(); else Undo(); StateHasChanged(); });
    }
}
