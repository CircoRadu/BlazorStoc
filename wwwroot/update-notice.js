// The window "Aplicația se actualizează" (Components/Shared/UpdateNoticeDialog.razor) is a native <dialog>: opened with showModal it sits on the top layer, above the page and
// above any other window that is open (the reports, the editors).
window.blazorStocUpdateNotice = {
    show: (id) => {
        const dialog = document.getElementById(id);
        if (dialog instanceof HTMLDialogElement && !dialog.open) dialog.showModal();
    }
};
