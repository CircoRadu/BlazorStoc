// BUG-0038: the sections of a page that is shown as tabs (.section-tab-panel, e.g. the beneficiary sheet) are <details> that stay open: the tab decides what is visible.
(() => {
    const reopen = () => document.querySelectorAll(".section-tab-panel details.catalog-collapse:not([open])").forEach((details) => { details.open = true; });
    // A closed section (keyboard, or a page that renders it closed) opens again at once.
    document.addEventListener("toggle", (event) => {
        const details = event.target;
        if (details instanceof HTMLDetailsElement && !details.open && details.closest(".section-tab-panel")) details.open = true;
    }, true);
    // The sections of the modules appear when their data is loaded: they are opened as they come.
    let queued = false;
    new MutationObserver(() => {
        if (queued) return;
        queued = true;
        setTimeout(() => { queued = false; reopen(); }, 0);
    }).observe(document.documentElement, { childList: true, subtree: true });
    reopen();
})();
