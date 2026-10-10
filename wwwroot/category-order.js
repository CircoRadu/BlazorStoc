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
})();
