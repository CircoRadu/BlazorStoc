// Windows of the application (Components/Shared/AppDialog.razor), two small things, no observer and no change of the page:
// 1) Escape closes the top window; a window that answers keys by itself (data-own-keys, Blazor @onkeydown) keeps doing it when the focus is inside it; a native <dialog> that is open
//    handles Escape itself.
// 2) The round close button of a window that did not give OnClose presses the button the window already has for closing (so an unsaved-changes question, a discard and so on behave
//    exactly as with that button). Priority: data-popup-close / data-help-close / data-head-close (the closing button of the head, hidden because the bar has the round one), the .act-close buttons, then the safe words.
(() => {
    const layer = (frame) => Number.parseInt(getComputedStyle(frame.closest("[data-app-dialog]")).zIndex, 10) || 0;
    const closeWords = [/^închide/i, /^anulează/i, /^nu[, ]/i, /^înapoi/i, /^renunță/i];
    const shown = (item) => item.getClientRects().length > 0;
    const closeButtonOf = (panel) => {
        const buttons = Array.from(panel.querySelectorAll("button")).filter((item) => !item.disabled && (shown(item) || item.hasAttribute("data-head-close")));
        const text = (item) => (item.textContent || "").trim().replace(/^[^\p{L}]+/u, "");
        return buttons.find((item) => item.hasAttribute("data-popup-close") || item.hasAttribute("data-help-close") || item.hasAttribute("data-head-close"))
            || buttons.find((item) => item.classList.contains("act-close") && !item.classList.contains("row-icon"))
            || closeWords.map((word) => buttons.find((item) => word.test(text(item)))).find(Boolean)
            || null;
    };
    const press = (frame) => {
        const close = frame.querySelector("[data-app-dialog-close]");
        if (!close) return;
        close.click();
    };
    document.addEventListener("click", (event) => {
        const icon = event.target instanceof Element ? event.target.closest("[data-app-dialog-proxy]") : null;
        if (!icon) return;
        const frame = icon.closest("[data-app-dialog-frame]");
        const panel = frame && frame.querySelector(".app-dialog-panel");
        const button = panel ? closeButtonOf(panel) : null;
        if (button) { event.preventDefault(); button.click(); return; }
        const native = icon.closest("dialog");
        if (native && native.open) native.close();
    });
    document.addEventListener("keydown", (event) => {
        if (event.key !== "Escape" || event.defaultPrevented || event.isComposing) return;
        if (document.querySelector("dialog[open]")) return;
        const frames = Array.from(document.querySelectorAll("[data-app-dialog-frame]")).filter(shown);
        if (frames.length === 0) return;
        let top = frames[0];
        frames.forEach((frame) => { if (layer(frame) >= layer(top)) top = frame; });
        const holder = top.closest("[data-app-dialog]");
        if (holder.hasAttribute("data-own-keys") && holder.contains(event.target)) return;
        if (top.querySelector("[data-app-dialog-close]")) { event.preventDefault(); press(top); }
    });
})();
