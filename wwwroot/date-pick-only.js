// Read-only date field (PickOnlyDate): the visible text box cannot be typed into, and the value comes only from
// the calendar of the hidden native date input that sits right under it. A click, Enter, Space, F4 or Alt+Down
// opens the calendar; Tab, Shift+Tab and Escape keep working, so the field stays usable without a mouse.
(() => {
    const selector = "input[data-pick-date]";
    const field = (event) => event.target instanceof Element ? event.target.closest(selector) : null;

    const openPicker = (text) => {
        const native = text.parentElement?.querySelector("input[type=date]");
        if (!native || text.disabled || native.disabled) return;
        try { native.showPicker(); } catch { /* already open, or the browser refuses without a user gesture */ }
    };

    document.addEventListener("click", (event) => {
        const text = field(event);
        if (text) openPicker(text);
    }, true);

    document.addEventListener("keydown", (event) => {
        const text = field(event);
        if (!text) return;
        if (event.key === "Enter" || event.key === " " || event.key === "F4" || event.altKey && event.key === "ArrowDown") {
            event.preventDefault();
            openPicker(text);
        }
    }, true);
})();
