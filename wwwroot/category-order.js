(() => {
    // Organizare catalog: the order of the categories is changed by dragging the handle of a category card (button[data-category-handle], draggable)
    // over the other cards (div[data-category-index]); where the pointer is in the upper or lower half of a card shows whether the dragged one goes
    // before or after it. The component gets the final place (MoveCategory(from, to)) and saves it; this script only draws the indicator.
    // Delegated on document, so nothing has to be set up again when the page re-renders.
    let dotnet = null;
    let from = null;

    window.blazorStocCategoryOrder = {
        register(reference) { dotnet = reference; },
        unregister() { dotnet = null; }
    };

    const items = () => [...document.querySelectorAll('[data-category-index]')];
    const clear = () => items().forEach(item => item.classList.remove('drop-before', 'drop-after', 'dragging'));
    const after = (item, event) => {
        const box = item.getBoundingClientRect();
        return event.clientY > box.top + box.height / 2;
    };
    // The index the dragged category ends up at once it is taken out of the list and put before/after the target.
    const target = (item, event) => {
        const index = Number(item.dataset.categoryIndex);
        const place = index + (after(item, event) ? 1 : 0);
        return from < place ? place - 1 : place;
    };

    document.addEventListener('dragstart', event => {
        const handle = event.target.closest?.('[data-category-handle]');
        if (!handle) return;
        const card = handle.closest('[data-category-index]');
        from = Number(card.dataset.categoryIndex);
        event.dataTransfer.effectAllowed = 'move';
        event.dataTransfer.setData('text/plain', card.dataset.categoryName || '');
        event.dataTransfer.setDragImage(card, 24, 24);
        card.classList.add('dragging');
    });

    document.addEventListener('dragover', event => {
        if (from === null) return;
        const item = event.target.closest?.('[data-category-index]');
        if (!item) return;
        event.preventDefault();
        event.dataTransfer.dropEffect = 'move';
        items().forEach(other => other.classList.remove('drop-before', 'drop-after'));
        item.classList.add(after(item, event) ? 'drop-after' : 'drop-before');
    });

    document.addEventListener('drop', event => {
        if (from === null) return;
        const item = event.target.closest?.('[data-category-index]');
        if (!item) return;
        event.preventDefault();
        const to = target(item, event);
        const start = from;
        from = null;
        clear();
        if (dotnet && to !== start) dotnet.invokeMethodAsync('MoveCategory', start, to).catch(() => { });
    });

    document.addEventListener('dragend', () => { from = null; clear(); });

    // The nodes of the tree of subcategories: the handle of a row (span[data-subcategory-handle], draggable) is dragged over any row of any category
    // (tr[data-node-name]). Where the pointer is in the row decides: the upper quarter puts the node before the row, the lower quarter after it, the middle
    // makes it a child of the row (the whole branch under the dragged node goes along). A category title (data-category-drop) puts it last on the first level
    // of that category. The component gets DropSubcategory(category, name, targetCategory, targetName, zone), checks the conflicts and saves.
    let dragNode = null;
    const nodeRows = () => [...document.querySelectorAll('[data-node-name]')];
    const clearNodes = () => {
        nodeRows().forEach(row => row.classList.remove('drop-before', 'drop-after', 'drop-inside', 'dragging'));
        document.querySelectorAll('[data-category-drop]').forEach(item => item.classList.remove('drop-inside'));
    };
    const zoneOf = (row, event) => {
        const box = row.getBoundingClientRect();
        const place = (event.clientY - box.top) / Math.max(box.height, 1);
        return place < 0.25 ? 'before' : place > 0.75 ? 'after' : 'inside';
    };

    document.addEventListener('dragstart', event => {
        const handle = event.target.closest?.('[data-subcategory-handle]');
        if (!handle) return;
        const row = handle.closest('[data-node-name]');
        if (!row) return;
        dragNode = { category: row.dataset.nodeCategory, name: row.dataset.nodeName };
        event.stopPropagation();
        event.dataTransfer.effectAllowed = 'move';
        event.dataTransfer.setData('text/plain', row.dataset.nodeName || '');
        event.dataTransfer.setDragImage(row, 24, 16);
        row.classList.add('dragging');
    }, true);

    document.addEventListener('dragover', event => {
        if (dragNode === null) return;
        const row = event.target.closest?.('[data-node-name]');
        const title = row ? null : event.target.closest?.('[data-category-drop]');
        if (!row && !title) return;
        event.preventDefault();
        event.dataTransfer.dropEffect = 'move';
        clearNodes();
        if (row) row.classList.add('drop-' + zoneOf(row, event));
        else title.classList.add('drop-inside');
    });

    document.addEventListener('drop', event => {
        if (dragNode === null) return;
        const row = event.target.closest?.('[data-node-name]');
        const title = row ? null : event.target.closest?.('[data-category-drop]');
        if (!row && !title) return;
        event.preventDefault();
        const source = dragNode;
        const target = row
            ? { category: row.dataset.nodeCategory, name: row.dataset.nodeName, zone: zoneOf(row, event) }
            : { category: title.dataset.categoryDrop, name: '', zone: 'end' };
        dragNode = null;
        clearNodes();
        if (dotnet) dotnet.invokeMethodAsync('DropSubcategory', source.category, source.name, target.category, target.name, target.zone).catch(() => { });
    });

    document.addEventListener('dragend', () => { dragNode = null; clearNodes(); });
})();
