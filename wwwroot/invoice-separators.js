(() => {
    // Produse -> Preluare factura: the demarcation lines of the line table, drawn over the page picture of the invoice
    // (<svg data-sep-svg>, coordinates = page points, its viewBox). A line is <g class="sep-line" data-sep-id data-sep-y data-sep-left data-sep-right>;
    // the handles inside it are data-sep-handle = move (drag the line up or down) | left | right (change where it starts or ends). The line
    // follows the pointer; the component gets the final position when the pointer is released. With data-sep-add="true" a press on the page
    // adds a line there. Delegated on document, like invoice-template.js (nothing to initialise when the component re-renders).
    let dotnet = null;
    let drag = null;
    const MINIMUM_LENGTH = 10;

    window.blazorStocInvoiceSeparators = {
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

    function bounds(svg) {
        const box = svg.viewBox.baseVal;
        return { width: box.width, height: box.height };
    }

    function place(group, state) {
        group.querySelectorAll("line").forEach(line => {
            line.setAttribute("x1", state.left);
            line.setAttribute("x2", state.right);
            line.setAttribute("y1", state.y);
            line.setAttribute("y2", state.y);
        });
        const handles = { left: group.querySelector("[data-sep-handle=left]"), right: group.querySelector("[data-sep-handle=right]") };
        [["left", state.left], ["right", state.right]].forEach(([name, x]) => {
            const handle = handles[name];
            if (!handle) return;
            const size = parseFloat(handle.getAttribute("width")) || 0;
            handle.setAttribute("x", x - size / 2);
            handle.setAttribute("y", state.y - size / 2);
        });
    }

    document.addEventListener("pointerdown", event => {
        if (event.button !== 0 || !(event.target instanceof Element)) return;
        const handle = event.target.closest("[data-sep-handle]");
        const svg = event.target.closest("svg[data-sep-svg]");
        if (!svg) return;
        if (!handle) {
            if (svg.getAttribute("data-sep-add") !== "true" || !dotnet) return;
            event.preventDefault();
            const at = point(svg, event);
            const page = parseInt(svg.getAttribute("data-sep-page") || "1", 10);
            dotnet.invokeMethodAsync("OnSeparatorAdded", page, at.y).catch(() => { /* the component is gone */ });
            return;
        }
        const group = handle.closest(".sep-line");
        if (!group) return;
        event.preventDefault();
        const state = { y: parseFloat(group.dataset.sepY), left: parseFloat(group.dataset.sepLeft), right: parseFloat(group.dataset.sepRight) };
        drag = { svg, group, handle: handle.getAttribute("data-sep-handle"), id: group.dataset.sepId, start: point(svg, event), origin: state, result: state, moved: false };
        group.classList.add("adjusting");
        try { svg.setPointerCapture(event.pointerId); } catch (error) { /* still follows while over the page */ }
    });

    document.addEventListener("pointermove", event => {
        if (!drag) return;
        const now = point(drag.svg, event);
        const { width, height } = bounds(drag.svg);
        const origin = drag.origin;
        const result = { ...origin };
        if (drag.handle === "move") result.y = Math.min(height, Math.max(0, origin.y + now.y - drag.start.y));
        else if (drag.handle === "left") result.left = Math.min(origin.right - MINIMUM_LENGTH, Math.max(0, origin.left + now.x - drag.start.x));
        else if (drag.handle === "right") result.right = Math.max(origin.left + MINIMUM_LENGTH, Math.min(width, origin.right + now.x - drag.start.x));
        drag.result = result;
        drag.moved = true;
        place(drag.group, result);
    });

    function finish(event, cancelled) {
        if (!drag) return;
        const state = drag;
        drag = null;
        state.group.classList.remove("adjusting");
        try { state.svg.releasePointerCapture(event.pointerId); } catch (error) { /* already released */ }
        if (!state.moved) return;
        if (cancelled || !dotnet) { place(state.group, state.origin); return; }
        const { y, left, right } = state.result;
        dotnet.invokeMethodAsync("OnSeparatorChanged", state.id, y, left, right).catch(() => { /* the component is gone */ });
    }

    document.addEventListener("pointerup", event => finish(event, false));
    document.addEventListener("pointercancel", event => finish(event, true));

    // The Delete key removes the selected line unless a text field has the focus.
    document.addEventListener("keydown", event => {
        if (!dotnet || event.key !== "Delete" || event.ctrlKey || event.altKey || event.metaKey) return;
        const target = event.target instanceof Element ? event.target : null;
        if (target && target.closest("input, textarea, select, [contenteditable]")) return;
        if (!document.querySelector("svg[data-sep-svg]")) return;
        event.preventDefault();
        dotnet.invokeMethodAsync("OnSeparatorDeleteKey").catch(() => { /* the component is gone */ });
    });
})();
