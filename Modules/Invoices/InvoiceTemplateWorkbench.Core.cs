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
    // What the leave warning compares: the draft with everything typed beside it. A template being edited is "modified" when this differs
    // from what it was when the file was opened.
    private string CurrentSnapshot() => FormSnapshot.Values(System.Text.Json.JsonSerializer.Serialize(draft, SnapshotOptions), templateName, supplierName, supplierCui);

    private Task RequestCloseAsync() => tracker is null ? CloseEditingAsync() : tracker.RunAfterConfirmAsync(CloseEditingAsync);

    private async Task CloseEditingAsync()
    {
        await DiscardSessionAsync();
        session = null; draft = null; preview = null; existing = null; suggestions = [];
        await Closed.InvokeAsync();
    }

    protected override void OnInitialized() => reference = DotNetObjectReference.Create(this);

    protected override async Task OnInitializedAsync()
    {
        if (ExistingSession is not { } shared) return;
        try
        {
            session = shared;
            await LoadRegistryAsync();
            decimalHint = InvoiceValues.DecimalStyle(session.Document.AllWords.Select(word => word.Text));
            suggestions = InvoiceTemplateSuggestions.Rank(await Templates.GetAllAsync(lifetime.Token), session.Document);
            analyzed = true;
            existing = null;
            currentPage = session.Document.Pages[0].Number;
            UseAutomaticProposal();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // A file opened for the template being edited is the new "unmodified" state (the template as saved, shown on that file).
        if (rebaseAfterRender) { rebaseAfterRender = false; tracker?.Rebase(); }
        if (!firstRender) return;
        try { await JS.InvokeVoidAsync("blazorStocInvoiceTemplate.register", reference, ownerId); }
        catch (Exception exception) when (exception is JSException or InvalidOperationException or TaskCanceledException) { }
    }

    // Editing a saved template starts from its own model (the invoice it was made from, saved with it); a template saved before models
    // existed asks for a file instead.
    protected override async Task OnParametersSetAsync()
    {
        if (EditTemplateId is not { } id || EditRequest == loadedRequest || busy) return;
        loadedRequest = EditRequest;
        error = notice = null;
        try
        {
            var model = await Templates.GetModelAsync(id, lifetime.Token);
            if (model is null) { notice = "Șablonul nu are salvată o factură model: încarcă o factură PDF."; return; }
            await RunAnalysisAsync(() => Task.FromResult<Stream>(new MemoryStream(model.Content, writable: false)), model.FileName);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception exception) when (exception is AccessDeniedException or InvoiceTemplateOperationException) { error = exception.Message; }
    }

    private async Task DiscardAsync()
    {
        // In a window over another page the file belongs to that page: giving up only closes the window.
        if (Embedded) { await Closed.InvokeAsync(); return; }
        await DiscardSessionAsync();
        session = null; draft = null; preview = null; existing = null; suggestions = [];
        error = null; notice = "Analiza a fost abandonată, iar fișierul a fost eliminat din memorie.";
    }

    private Task DiscardSessionAsync()
    {
        if (session is not null && !Embedded) Analysis.Discard(session.Id);
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel();
        if (session is not null && !Embedded) Analysis.Discard(session.Id);
        try { await JS.InvokeVoidAsync("blazorStocInvoiceTemplate.unregister", ownerId); }
        catch (Exception exception) when (exception is JSException or InvalidOperationException or TaskCanceledException or JSDisconnectedException) { }
        reference?.Dispose();
        lifetime.Dispose();
    }
}
