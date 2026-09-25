// Leave guard for the "Adaugă produs" form (Task 6). While the page has enabled it, a click on a category,
// subcategory or "Toate produsele" entry of the products menu is stopped and reported to the page, which asks
// the user to confirm before the form is closed. The category headers are <summary> elements that navigate on
// toggle, so their click is intercepted too and the menu keeps its previous position when the user cancels.
(() => {
    let page = null;

    const stop = (event) => {
        event.preventDefault();
        event.stopPropagation();
        event.stopImmediatePropagation();
    };

    document.addEventListener("click", (event) => {
        if (!page || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
        const target = event.target instanceof Element ? event.target : null;
        if (!target) return;

        const link = target.closest(".sidebar a[href]");
        if (link) {
            const url = new URL(link.getAttribute("href"), location.href);
            if (url.origin === location.origin && url.pathname.toLowerCase() === "/produse") {
                stop(event);
                page.invokeMethodAsync("RequestLeave", url.pathname + url.search, false);
            }
            return;
        }

        const summary = target.closest(".sidebar .product-category-menu > details[data-collapsible-section] > summary");
        if (summary) {
            const details = summary.parentElement;
            const destination = details.open ? details.dataset.collapsibleCloseHref : details.dataset.collapsibleOpenHref;
            if (destination) {
                stop(event);
                page.invokeMethodAsync("RequestLeave", destination, true);
            }
        }
    }, true);

    // Answered by the browser only while it is connected: the product edit lock (Task 9) is renewed only when this
    // call succeeds, so a closed tab lets the lease expire even while the server keeps the circuit for a few minutes.
    window.blazorStocPing = () => true;

    window.blazorStocLeaveGuard = {
        enable(reference) { page = reference; },
        disable() { page = null; }
    };
})();
