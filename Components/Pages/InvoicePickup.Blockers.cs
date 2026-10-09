using BlazorStoc.Services;

namespace BlazorStoc.Components.Pages;

// Invoice pickup: why a step button does not work. The reasons are written under the button (as the messages of the save), not only at the top of the page,
// so that a user who has scrolled down knows what is missing.
public partial class InvoicePickup
{
    private IReadOnlyList<string> NextBlockedReasons()
    {
        var reasons = new List<string>();
        if (step == 1 && !CanLeaveStep1)
        {
            if (session is null) reasons.Add("Alege fișierul facturii.");
            else if (reading is null) reasons.Add("Din factură nu s-a citit un tabel cu linii: alege sau creează un șablon.");
            else reasons.Add("Păstrează cel puțin un rând al facturii.");
        }
        else if (step == 2 && !CanLeaveStep2 && !step2Loading && step2Error is null)
        {
            if (!pick.Any(item => item.ProductId is not null || item.Staged is not null)) reasons.Add("Alege sau pregătește un produs pentru cel puțin un rând.");
            if (!NumberValid) reasons.Add("Numărul facturii lipsește."); else if (VerifyNeeded && !numberVerified) reasons.Add("Confirmă numărul facturii (comutatorul „Verificat”).");
            if (ResolvedSupplier is null) reasons.Add("Furnizorul nu este ales: scrie CUI-ul lui sau adaugă-l în registru."); else if (VerifyNeeded && !cuiVerified) reasons.Add("Confirmă CUI-ul furnizorului (comutatorul „Verificat”).");
            if (!DateValid) reasons.Add("Data facturii lipsește."); else if (VerifyNeeded && !dateVerified) reasons.Add("Confirmă data facturii (comutatorul „Verificat”).");
        }
        return reasons;
    }

    private IReadOnlyList<string> FinalizeBlockedReasons()
    {
        var reasons = new List<string>();
        if (finished || CanFinalize) return reasons;
        if (!NewLines.Concat(ExistingLines).Any(line => !Skipped(line))) reasons.Add("Nu există niciun rând de preluat (rândurile deja preluate de pe această factură se sar).");
        if (supplierId is null) reasons.Add(SupplierInvoiceRules.SupplierRequiredMessage);
        if (invoiceNumber.Trim().Length == 0) reasons.Add(SupplierInvoiceRules.NumberRequiredMessage);
        if (invoiceDate is null) reasons.Add(SupplierInvoiceRules.DateRequiredMessage);
        return reasons;
    }
}
