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
        const frames = Array.from(document.querySelectorAll("[data-app-dialog-frame]:not([data-app-dialog-floating])")).filter(shown);
        if (frames.length === 0) return;
        let top = frames[0];
        frames.forEach((frame) => { if (layer(frame) >= layer(top)) top = frame; });
        const holder = top.closest("[data-app-dialog]");
        if (holder.hasAttribute("data-locked")) { event.preventDefault(); return; }
        if (holder.hasAttribute("data-own-keys") && holder.contains(event.target)) return;
        if (top.querySelector("[data-app-dialog-close]")) { event.preventDefault(); press(top); }
    });
    // A locked window keeps the keyboard focus: Tab never leaves it for the page behind.
    document.addEventListener("focusin", (event) => {
        const holder = document.querySelector("[data-app-dialog][data-locked]");
        if (!holder || !(event.target instanceof Element) || holder.contains(event.target) || event.target.closest("dialog[open]")) return;
        const target = holder.querySelector(".app-dialog-panel button:not([disabled]), .app-dialog-panel");
        if (target) target.focus();
    });
    // 3) Move and resize (PROP-0007): the head of a window is dragged to move it, the corner handle at the bottom right changes its size. Pointer events only, nothing is observed;
    //    the position and size live in inline styles of the frame / panel, so a window that is opened again starts at its default. Off on narrow screens (windows fill them).
    const narrow = () => window.matchMedia("(max-width: 719px)").matches;
    const MIN_WIDTH = 320, MIN_HEIGHT = 200, MARGIN = 8, DRAG_START = 4;
    let suppressClickUntil = 0;
    const offsetOf = (frame) => ({ x: Number.parseFloat(frame.dataset.dx || "0") || 0, y: Number.parseFloat(frame.dataset.dy || "0") || 0 });
    const place = (frame, x, y) => { frame.dataset.dx = String(x); frame.dataset.dy = String(y); frame.style.transform = x === 0 && y === 0 ? "" : `translate(${x}px, ${y}px)`; };
    const clamp = (value, min, max) => Math.min(Math.max(value, min), Math.max(min, max));
    const interactive = "button, a, input, select, textarea, summary, label, [contenteditable], [data-no-drag]";
    const track = (event, onMove, onEnd) => {
        const target = event.target;
        const id = event.pointerId;
        const startX = event.clientX, startY = event.clientY;
        let moved = false;
        const move = (e) => {
            if (e.pointerId !== id) return;
            const dx = e.clientX - startX, dy = e.clientY - startY;
            if (!moved && Math.hypot(dx, dy) < DRAG_START) return;
            if (!moved) { moved = true; document.documentElement.classList.add("app-dialog-adjusting"); }
            onMove(dx, dy);
        };
        const end = (e) => {
            if (e.pointerId !== id) return;
            target.removeEventListener("pointermove", move);
            target.removeEventListener("pointerup", end);
            target.removeEventListener("pointercancel", end);
            try { target.releasePointerCapture(id); } catch { /* already released */ }
            document.documentElement.classList.remove("app-dialog-adjusting");
            if (moved) suppressClickUntil = Date.now() + 80;   // the click that ends a drag must not reach the backdrop (it would close the window)
            if (onEnd) onEnd(moved);
        };
        try { target.setPointerCapture(id); } catch { /* the pointer is gone */ }
        target.addEventListener("pointermove", move);
        target.addEventListener("pointerup", end);
        target.addEventListener("pointercancel", end);
    };
    document.addEventListener("pointerdown", (event) => {
        if (event.button !== 0 || !(event.target instanceof Element)) return;   // the move works on a phone too; only the resize handle is left out there
        const frame = event.target.closest("[data-app-dialog-frame]");
        const panel = frame && frame.querySelector(".app-dialog-panel");
        if (!frame || !panel) return;
        if (event.target.closest("[data-app-dialog-resize]")) {
            if (narrow()) return;
            const rect = panel.getBoundingClientRect();
            const maxWidth = window.innerWidth - MARGIN - rect.left;
            const maxHeight = window.innerHeight - MARGIN - rect.top;
            event.preventDefault();
            track(event, (dx, dy) => {
                const width = clamp(rect.width + dx, MIN_WIDTH, maxWidth), height = clamp(rect.height + dy, MIN_HEIGHT, maxHeight);
                frame.style.width = `${width}px`; panel.style.width = "100%"; panel.style.height = `${height}px`;   // the panel fills the frame whatever width its own class gives it (BUG-0015)
                // The layout (centred frame) moves the panel when its size changes; measure where it went and bring the top-left corner back, so the corner follows the pointer.
                const now = panel.getBoundingClientRect(), shifted = offsetOf(frame);
                place(frame, shifted.x + (rect.left - now.left), shifted.y + (rect.top - now.top));
            });
            return;
        }
        const head = event.target.closest(".app-dialog-head, .app-dialog-grip, .dialog-head");
        if (!head || !panel.contains(head) || event.target.closest(interactive)) return;
        const start = offsetOf(frame);
        const rect = panel.getBoundingClientRect();
        event.preventDefault();
        track(event, (dx, dy) => {
            // At least a strip of the head stays on the screen, so the window can always be taken back.
            const x = clamp(start.x + dx, start.x - rect.left - rect.width + 80, start.x + window.innerWidth - rect.left - 80);
            const y = clamp(start.y + dy, start.y - rect.top, start.y + window.innerHeight - rect.top - 40);
            place(frame, x, y);
        });
    });
    document.addEventListener("dblclick", (event) => {
        if (narrow() || !(event.target instanceof Element)) return;
        const frame = event.target.closest("[data-app-dialog-frame]");
        const panel = frame && frame.querySelector(".app-dialog-panel");
        if (!frame || !panel) return;
        if (event.target.closest("[data-app-dialog-resize]")) { frame.style.width = ""; panel.style.width = ""; panel.style.height = ""; place(frame, 0, 0); return; }
        const head = event.target.closest(".app-dialog-head, .app-dialog-grip, .dialog-head");
        if (head && panel.contains(head) && !event.target.closest(interactive)) place(frame, 0, 0);
    });
    window.addEventListener("click", (event) => {
        if (Date.now() < suppressClickUntil) { event.stopPropagation(); event.preventDefault(); }
    }, true);
})();

// A window that writes its title inside its content (LabelledBy = the id of its heading): the text goes into the bar of the window and the heading is hidden, so every window shows its
// name in the same bar (BUG-0015). Called by AppDialog after each render; one call per window, no observer.
window.appDialog = {
    syncTitle(panel, headingId) {
        if (!panel) return;
        const bar = panel.querySelector(":scope > .app-dialog-grip");
        // A window with no id for its title (BUG-0016): the first heading that sits in a head row inside the content.
        const heading = headingId ? document.getElementById(headingId) : panel.querySelector(".app-dialog-body :is(.catalog-heading, .dialog-head, .map-panel-heading) > :is(h1, h2, h3, h4)");
        if (!bar || !heading || !panel.contains(heading)) return;
        const text = (heading.textContent || "").trim();
        let title = bar.querySelector(".app-dialog-grip-title");
        if (!title) { title = document.createElement("span"); title.className = "app-dialog-grip-title"; bar.appendChild(title); }
        if (text) { title.textContent = text; heading.classList.add("app-dialog-title-moved"); }
        // The help icon written next to the heading goes into the bar too (PROP-0016): a copy in the bar (wwwroot/help.js listens for [data-help-topic] on the whole page), the original hidden.
        const holder = heading.parentElement;
        if (!holder) return;
        for (const icon of holder.querySelectorAll(".help-icon[data-help-topic]")) {
            const topic = icon.getAttribute("data-help-topic") || "";
            if (!bar.querySelector(".help-icon[data-moved-for]")) {
                const copy = icon.cloneNode(true);
                copy.classList.remove("app-dialog-help-moved");
                copy.setAttribute("data-moved-for", topic);
                bar.appendChild(copy);
            }
            icon.classList.add("app-dialog-help-moved");
        }
        const spent = (node) => node === heading || node.classList.contains("app-dialog-help-moved") || getComputedStyle(node).display === "none"   // the closing button of the head is hidden by the round one in the bar
            || (node.classList.contains("heading-actions") && node.children.length > 0 && Array.from(node.children).every(spent));
        holder.classList.toggle("app-dialog-head-empty", Array.from(holder.children).every(spent));
    }
};
