// "Fereastra separata" of the maintenance map: a click on a link marked data-map-popout opens the map in a popup window with a fixed
// name (so a second click reuses it instead of opening another one) that can stay open on a second monitor. It is a plain link, so
// without this script (or when popups are blocked) it still opens the map in a tab. It has to run in the click itself, not after a
// round trip to the server, or the browser would block the popup.
document.addEventListener("click", event => {
    const link = event.target.closest && event.target.closest("a[data-map-popout]");
    if (!link || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey) return;
    const popup = window.open(link.href, link.target || "blazorstoc-harta", "popup=yes,width=1400,height=900");
    if (!popup) return;
    event.preventDefault();
    popup.focus();
});
