// Scrolls the element with the given id to the middle of the window (used by deep links from the Journal).
window.blazorStocScrollTo = id => {
    const element = document.getElementById(id);
    if (element && element.scrollIntoView) element.scrollIntoView({ block: 'center' });
};
