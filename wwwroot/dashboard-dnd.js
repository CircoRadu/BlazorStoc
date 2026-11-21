(() => {
    // Personalizare dashboard: the tiles, the rows and the zones of the arrangement are dragged with the native drag and drop, handled here in the browser (like the order of the categories, category-order.js);
    // a Blazor handler cannot do it: the browser needs dragover cancelled at once, and Blazor listens for dragover only for a handler that goes to the server on every move.
    // A tile (div[data-tile], draggable) is dropped on a cell (div[data-cell="zone,row,col"]) or on the buffer (div[data-buffer], zone -1); a row is dragged by its handle (span[data-row-handle]) and dropped
    // on another row (div[data-row][data-zone]) of the same zone or of another, or on a zone (section[data-zonebox]: the row goes to its end); a zone is dragged by its handle (span[data-zone-handle]) and
    // dropped on another zone. The component gets DropTile(key, zone, row, col), DropRow(fromZone, from, toZone, to) or DropZone(from, to). Delegated on document: nothing to set up again when the page re-renders.
    let dotnet = null;
    let tile = null;
    let row = null;
    let zone = null;

    window.blazorStocDashboardDnd = {
        register(reference) { dotnet = reference; },
        unregister() { dotnet = null; }
    };

    const inGrid = event => event.target instanceof Element && event.target.closest('[data-dashboard-grid]') !== null;
    const idle = () => tile === null && row === null && zone === null;
    const clear = () => {
        document.querySelectorAll('.dragging, .dragging-row, .dragging-zone, .drop-over').forEach(item => item.classList.remove('dragging', 'dragging-row', 'dragging-zone', 'drop-over'));
    };
    const reset = () => { tile = null; row = null; zone = null; clear(); };

    document.addEventListener('dragstart', event => {
        if (!inGrid(event) || !event.dataTransfer) return;
        const rowHandle = event.target.closest('[data-row-handle]');
        const zoneHandle = event.target.closest('[data-zone-handle]');
        const dragged = event.target.closest('[data-tile]');
        tile = null; row = null; zone = null;
        if (rowHandle) {
            const box = rowHandle.closest('[data-row]');
            row = { zone: Number(box.dataset.zone), index: Number(rowHandle.dataset.rowHandle) };
            box.classList.add('dragging-row');
        } else if (zoneHandle) {
            zone = Number(zoneHandle.dataset.zoneHandle);
            zoneHandle.closest('[data-zonebox]')?.classList.add('dragging-zone');
        } else if (dragged) {
            tile = dragged.dataset.tile;
            dragged.classList.add('dragging');
        } else return;
        event.dataTransfer.effectAllowed = 'move';
        try { event.dataTransfer.setData('text/plain', tile ?? (row ? 'rand' : 'zona')); } catch (error) { /* some browsers refuse; the drag still works */ }
    });

    // The element a drag ends on: a cell or the buffer for a tile; a row, else the zone, for a row; a zone for a zone.
    const targetOf = event => {
        if (tile !== null) return event.target.closest('[data-cell], [data-buffer]');
        if (row !== null) return event.target.closest('[data-row]') ?? event.target.closest('[data-zonebox]');
        return event.target.closest('[data-zonebox]');
    };

    document.addEventListener('dragover', event => {
        if (!inGrid(event) || idle()) return;
        const target = targetOf(event);
        if (!target) return;
        event.preventDefault();
        event.dataTransfer.dropEffect = 'move';
        document.querySelectorAll('.drop-over').forEach(item => { if (item !== target) item.classList.remove('drop-over'); });
        target.classList.add('drop-over');
    });

    document.addEventListener('drop', event => {
        if (!inGrid(event) || idle()) return;
        const target = targetOf(event);
        if (!target) return;
        event.preventDefault();
        if (tile !== null) {
            // The buffer is zone -1, row -1.
            const [z, r, c] = target.dataset.cell ? target.dataset.cell.split(',').map(Number) : [-1, -1, 0];
            const key = tile;
            reset();
            if (dotnet) dotnet.invokeMethodAsync('DropTile', key, z, r, c).catch(() => { });
        } else if (row !== null) {
            const from = row;
            // Dropped on a row: its place; dropped on the zone itself: the end of the zone (-1).
            const toZone = Number(target.dataset.zone ?? target.dataset.zonebox);
            const to = target.dataset.row !== undefined ? Number(target.dataset.row) : -1;
            reset();
            if (dotnet && !(from.zone === toZone && from.index === to)) dotnet.invokeMethodAsync('DropRow', from.zone, from.index, toZone, to).catch(() => { });
        } else {
            const from = zone;
            const to = Number(target.dataset.zonebox);
            reset();
            if (dotnet && from !== to) dotnet.invokeMethodAsync('DropZone', from, to).catch(() => { });
        }
    });

    document.addEventListener('dragend', reset);
})();
