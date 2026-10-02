(() => {
    // Settings -> Facturi: drawing a rectangle on the page picture of the invoice under analysis (InvoiceTemplateWorkbench). The
    // overlay <svg data-invoice-draw="field|column"> shares the page point coordinates (its viewBox), so a drag is converted to
    // points of the page and handed to the component. Delegated on document (nothing to initialise when the component re-renders).
    let dotnet = null;
    let drag = null;
    window.blazorStocInvoiceTemplate = {
        register(reference) { dotnet = reference; },
        unregister() { dotnet = null; }
    };

    function point(svg, event) {
        const matrix = svg.getScreenCTM();
        if (!matrix) return { x: 0, y: 0 };
        const source = svg.createSVGPoint();
        source.x = event.clientX;
        source.y = event.clientY;
        return source.matrixTransform(matrix.inverse());
    }

    function show(rectangle, start, end) {
        const x = Math.min(start.x, end.x), y = Math.min(start.y, end.y);
        rectangle.setAttribute("x", x);
        rectangle.setAttribute("y", y);
        rectangle.setAttribute("width", Math.abs(end.x - start.x));
        rectangle.setAttribute("height", Math.abs(end.y - start.y));
        rectangle.setAttribute("visibility", "visible");
    }

    document.addEventListener("pointerdown", event => {
        const svg = event.target instanceof Element ? event.target.closest("svg[data-invoice-draw]") : null;
        if (!svg || !svg.getAttribute("data-invoice-draw") || event.button !== 0) return;
        const rectangle = svg.querySelector("#invoice-draw-rect");
        if (!rectangle) return;
        event.preventDefault();
        drag = { svg, rectangle, start: point(svg, event) };
        try { svg.setPointerCapture(event.pointerId); } catch (error) { /* a pointer that cannot be captured still draws while it moves over the page */ }
        show(rectangle, drag.start, drag.start);
    });

    document.addEventListener("pointermove", event => {
        if (!drag) return;
        show(drag.rectangle, drag.start, point(drag.svg, event));
    });

    function finish(event, cancelled) {
        if (!drag) return;
        const { svg, rectangle, start } = drag;
        drag = null;
        rectangle.setAttribute("visibility", "hidden");
        try { svg.releasePointerCapture(event.pointerId); } catch (error) { /* already released */ }
        if (cancelled || !dotnet) return;
        const end = point(svg, event);
        const x = Math.min(start.x, end.x), y = Math.min(start.y, end.y);
        const width = Math.abs(end.x - start.x), height = Math.abs(end.y - start.y);
        const page = parseInt(svg.getAttribute("data-invoice-page") || "1", 10);
        if (width < 2 || height < 2) return;
        dotnet.invokeMethodAsync("OnRegionDrawn", page, x, y, width, height).catch(() => { /* the component is gone */ });
    }

    document.addEventListener("pointerup", event => finish(event, false));
    document.addEventListener("pointercancel", event => finish(event, true));

    // Moving and resizing the selected element: <g class="invoice-edit"> carries its box (data-x/y/w/h, page points) and holds the
    // handles (data-invoice-handle = move | n | s | e | w | ne | nw | se | sw). The outline follows the pointer; the component gets the
    // final box when the pointer is released. A column keeps its vertical extent (it spans the table).
    let adjust = null;
    const MINIMUM = 2;

    function adjusted(state, now) {
        const dx = now.x - state.start.x, dy = now.y - state.start.y;
        let { x, y, w, h } = state.box;
        const name = state.handle;
        if (name === "move") { x += dx; y += state.kind === "c" ? 0 : dy; }
        else {
            if (name.includes("e")) w = Math.max(MINIMUM, w + dx);
            if (name.includes("w")) { const right = x + w; x = Math.min(x + dx, right - MINIMUM); w = right - x; }
            if (state.kind !== "c") {
                if (name.includes("s")) h = Math.max(MINIMUM, h + dy);
                if (name.includes("n")) { const bottom = y + h; y = Math.min(y + dy, bottom - MINIMUM); h = bottom - y; }
            }
        }
        return { x, y, w, h };
    }

    document.addEventListener("pointerdown", event => {
        const handle = event.target instanceof Element ? event.target.closest("[data-invoice-handle]") : null;
        if (!handle || event.button !== 0) return;
        const group = handle.closest(".invoice-edit");
        const svg = handle.closest("svg");
        if (!group || !svg) return;
        event.preventDefault();
        event.stopImmediatePropagation();
        const box = { x: parseFloat(group.dataset.x), y: parseFloat(group.dataset.y), w: parseFloat(group.dataset.w), h: parseFloat(group.dataset.h) };
        adjust = { svg, group, handle: handle.getAttribute("data-invoice-handle"), kind: group.dataset.invoiceKind, id: group.dataset.invoiceId,
            page: parseInt(group.dataset.invoicePage || "1", 10), box, start: point(svg, event), result: box, moved: false };
        group.classList.add("adjusting");
        try { svg.setPointerCapture(event.pointerId); } catch (error) { /* still follows while over the page */ }
    }, true);

    document.addEventListener("pointermove", event => {
        if (!adjust) return;
        adjust.result = adjusted(adjust, point(adjust.svg, event));
        adjust.moved = true;
        const outline = adjust.group.querySelector(".invoice-adjust-outline");
        if (outline) {
            outline.setAttribute("x", adjust.result.x);
            outline.setAttribute("y", adjust.result.y);
            outline.setAttribute("width", adjust.result.w);
            outline.setAttribute("height", adjust.result.h);
        }
    });

    function finishAdjust(event, cancelled) {
        if (!adjust) return;
        const state = adjust;
        adjust = null;
        state.group.classList.remove("adjusting");
        try { state.svg.releasePointerCapture(event.pointerId); } catch (error) { /* already released */ }
        if (cancelled || !state.moved || !dotnet) return;
        const { x, y, w, h } = state.result;
        dotnet.invokeMethodAsync("OnRegionAdjusted", state.kind, state.id, x, y, w, h).catch(() => { /* the component is gone */ });
    }

    document.addEventListener("pointerup", event => finishAdjust(event, false));
    document.addEventListener("pointercancel", event => finishAdjust(event, true));

    // Selecting several elements: with no drawing mode on, pressing on the page and dragging marks a zone (press = top left corner, release
    // = bottom right corner); the component selects every found element inside it. A press that does not move stays a plain click.
    let marquee = null;
    const MARQUEE_THRESHOLD = 3;

    document.addEventListener("pointerdown", event => {
        const svg = event.target instanceof Element ? event.target.closest("svg[data-invoice-draw]") : null;
        if (!svg || svg.getAttribute("data-invoice-draw") || event.button !== 0) return;
        if (event.target.closest("[data-invoice-handle], .invoice-delete, .invoice-badge")) return;
        const rectangle = svg.querySelector("#invoice-draw-rect");
        if (!rectangle) return;
        marquee = { svg, rectangle, start: point(svg, event), active: false, onElement: !!event.target.closest(".invoice-field, .invoice-column, .invoice-edit") };
    });

    document.addEventListener("pointermove", event => {
        if (!marquee) return;
        const now = point(marquee.svg, event);
        if (!marquee.active) {
            if (Math.hypot(now.x - marquee.start.x, now.y - marquee.start.y) < MARQUEE_THRESHOLD) return;
            marquee.active = true;
            marquee.rectangle.classList.add("marquee");
            try { marquee.svg.setPointerCapture(event.pointerId); } catch (error) { /* still follows while over the page */ }
        }
        show(marquee.rectangle, marquee.start, now);
    });

    function finishMarquee(event, cancelled) {
        if (!marquee) return;
        const { svg, rectangle, start, active, onElement } = marquee;
        marquee = null;
        if (!active) { if (!cancelled && !onElement && dotnet) dotnet.invokeMethodAsync("OnBackgroundClicked").catch(() => { /* the component is gone */ }); return; }
        rectangle.setAttribute("visibility", "hidden");
        rectangle.classList.remove("marquee");
        try { svg.releasePointerCapture(event.pointerId); } catch (error) { /* already released */ }
        if (cancelled || !dotnet) return;
        const end = point(svg, event);
        const page = parseInt(svg.getAttribute("data-invoice-page") || "1", 10);
        dotnet.invokeMethodAsync("OnRegionSelected", page, Math.min(start.x, end.x), Math.min(start.y, end.y), Math.abs(end.x - start.x), Math.abs(end.y - start.y)).catch(() => { /* the component is gone */ });
    }

    document.addEventListener("pointerup", event => finishMarquee(event, false));
    document.addEventListener("pointercancel", event => finishMarquee(event, true));

    // Ctrl+Z / Ctrl+Y (or Ctrl+Shift+Z) undo and redo the last template action, unless a text field has the focus.
    document.addEventListener("keydown", event => {
        if (!dotnet || !(event.ctrlKey || event.metaKey) || event.altKey) return;
        const key = event.key.toLowerCase();
        if (key !== "z" && key !== "y") return;
        const target = event.target instanceof Element ? event.target : null;
        if (target && target.closest("input, textarea, select, [contenteditable]")) return;
        if (!document.querySelector("svg[data-invoice-draw]")) return;
        event.preventDefault();
        dotnet.invokeMethodAsync("OnUndoRedoKey", key === "y" || event.shiftKey).catch(() => { /* the component is gone */ });
    });

    // The label above the selected element is as wide as its text (a little room around it): the server only estimates the width, the
    // browser knows the real one, so the badge is fitted after it is drawn or its text changes.
    function fitBadges() {
        document.querySelectorAll(".invoice-badge").forEach(badge => {
            const rect = badge.querySelector("rect"), text = badge.querySelector("text");
            if (!rect || !text) return;
            let box;
            try { box = text.getBBox(); } catch (error) { return; }
            const font = parseFloat(text.getAttribute("font-size")) || 0;
            if (!box.width || !font) return;
            const pad = font * 0.35;
            const width = (box.width + 2 * pad).toFixed(2);
            if (rect.getAttribute("width") !== width) rect.setAttribute("width", width);
        });
    }
    let fitQueued = false;
    new MutationObserver(() => {
        if (fitQueued) return;
        fitQueued = true;
        requestAnimationFrame(() => { fitQueued = false; fitBadges(); });
    }).observe(document.documentElement, { childList: true, subtree: true, characterData: true, attributes: true, attributeFilter: ["width", "font-size"] });

    // The Delete key removes the selected element (or the selected group of elements) unless a text field has the focus.
    document.addEventListener("keydown", event => {
        if (!dotnet || event.key !== "Delete" || event.ctrlKey || event.altKey || event.metaKey) return;
        const target = event.target instanceof Element ? event.target : null;
        if (target && target.closest("input, textarea, select, [contenteditable]")) return;
        if (!document.querySelector("svg[data-invoice-draw]")) return;
        event.preventDefault();
        dotnet.invokeMethodAsync("OnDeleteKey").catch(() => { /* the component is gone */ });
    });
})();
