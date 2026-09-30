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
})();
