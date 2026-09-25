// Sign-out confirmation (Task 7): the topbar "Deconectare" action opens a native modal dialog instead of the old
// confirmation page. Cancel (button or Escape) only closes the dialog, so the current page and its state are untouched.
(() => {
    document.addEventListener("click", (event) => {
        const target = event.target instanceof Element ? event.target : null;
        if (!target) return;
        if (target.closest("[data-logout-open]")) {
            event.preventDefault();
            const dialog = document.getElementById("logout-dialog");
            if (dialog instanceof HTMLDialogElement && !dialog.open) dialog.showModal();
            return;
        }
        if (target.closest("[data-logout-cancel]")) {
            const dialog = target.closest("dialog");
            if (dialog instanceof HTMLDialogElement) dialog.close();
        }
    });
})();
