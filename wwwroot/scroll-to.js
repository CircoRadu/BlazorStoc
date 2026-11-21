// Scrolls the element with the given id to the middle of the window (used by deep links from the Journal).
window.blazorStocScrollTo = id => {
    const element = document.getElementById(id);
    if (element && element.scrollIntoView) element.scrollIntoView({ block: 'center' });
};

// Opens the collapsible section of a page whose heading starts with the given title and scrolls to it (the links of the file table of a beneficiary).
window.blazorStocOpenSection = title => {
    const heading = [...document.querySelectorAll('details.catalog-collapse > summary h2')].find(item => item.textContent.trim().startsWith(title));
    const details = heading && heading.closest('details');
    if (!details) return;
    details.open = true;
    // On a page with tabs (the beneficiary sheet) the tab of the section is selected first.
    const panel = details.closest('.section-tab-panel');
    const tab = panel && document.querySelector('[data-tab-target="' + panel.dataset.tab + '"]');
    if (tab && panel.hidden) { tab.click(); setTimeout(() => details.scrollIntoView({ block: 'start' }), 250); return; }
    details.scrollIntoView({ block: 'start' });
};
