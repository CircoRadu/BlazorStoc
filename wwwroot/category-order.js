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

    // The subcategories of a category: the handle of a table row (span[data-subcategory-handle], draggable) is dragged over the other rows of the same
    // category (tr[data-subcategory-index] with data-subcategory-category); the component gets MoveSubcategory(category, from, to).
    let subFrom = null;
    let subCategory = null;
    const rows = () => [...document.querySelectorAll('[data-subcategory-index]')];
    const clearRows = () => rows().forEach(row => row.classList.remove('drop-before', 'drop-after', 'dragging'));
    const rowOf = event => {
        const row = event.target.closest?.('[data-subcategory-index]');
        return row && row.dataset.subcategoryCategory === subCategory ? row : null;
    };

    document.addEventListener('dragstart', event => {
        const handle = event.target.closest?.('[data-subcategory-handle]');
        if (!handle) return;
        const row = handle.closest('[data-subcategory-index]');
        subFrom = Number(row.dataset.subcategoryIndex);
        subCategory = row.dataset.subcategoryCategory;
        event.stopPropagation();
        event.dataTransfer.effectAllowed = 'move';
        event.dataTransfer.setData('text/plain', row.dataset.subcategoryName || '');
        event.dataTransfer.setDragImage(row, 24, 16);
        row.classList.add('dragging');
    }, true);

    document.addEventListener('dragover', event => {
        if (subFrom === null) return;
        const row = rowOf(event);
        if (!row) return;
        event.preventDefault();
        event.dataTransfer.dropEffect = 'move';
        rows().forEach(other => other.classList.remove('drop-before', 'drop-after'));
        row.classList.add(after(row, event) ? 'drop-after' : 'drop-before');
    });

    document.addEventListener('drop', event => {
        if (subFrom === null) return;
        const row = rowOf(event);
        if (!row) return;
        event.preventDefault();
        const place = Number(row.dataset.subcategoryIndex) + (after(row, event) ? 1 : 0);
        const to = subFrom < place ? place - 1 : place;
        const start = subFrom;
        const category = subCategory;
        subFrom = null; subCategory = null;
        clearRows();
        if (dotnet && to !== start) dotnet.invokeMethodAsync('MoveSubcategory', category, start, to).catch(() => { });
    });

    document.addEventListener('dragend', () => { subFrom = null; subCategory = null; clearRows(); });
})();
