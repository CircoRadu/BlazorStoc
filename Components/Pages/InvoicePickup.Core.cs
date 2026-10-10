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
    private async Task NextStep()
    {
        if (step >= StepTitles.Length) return;
        // A step that cannot be left says why only now, when the user tries to leave it.
        if (NextBlocked) { showBlockers = true; return; }
        showBlockers = false;
        step++;
        if (step == 2) { await BuildPickAsync(); await LoadSuppliersAsync(); }
        else if (step == 3) PrepareSummary();
    }

    private void PreviousStep() { if (step > 1) { step--; showBlockers = false; } }
    protected override void OnInitialized() => reference = DotNetObjectReference.Create(this);

    protected override async Task OnInitializedAsync()
    {
        if (!await Access.CanManageProductsAsync(lifetime.Token)) loadError = "Nu ai dreptul să preiei facturi.";
        loading = false;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;
        try { await JS.InvokeVoidAsync("blazorStocInvoiceSeparators.register", reference); }
        catch (Exception exception) when (exception is JSException or InvalidOperationException or TaskCanceledException or JSDisconnectedException) { }
    }

    private void DiscardSession()
    {
        if (session is not null) Analysis.Discard(session.Id);
    }

    private Task DiscardAsync()
    {
        DiscardSession();
        ClearReading();
        notice = "Fișierul a fost eliminat din memorie.";
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel();
        DiscardSession();
        try { await JS.InvokeVoidAsync("blazorStocInvoiceSeparators.unregister"); }
        catch (Exception exception) when (exception is JSException or InvalidOperationException or TaskCanceledException or JSDisconnectedException) { }
        reference?.Dispose();
        lifetime.Dispose();
    }
}
