(() => {
    // Taskuri utilizator: the order of the active tasks of one user is changed by dragging the handle of a row (span[data-task-handle], draggable) over the other
    // rows (tr[data-task-index]); the upper or lower half of the row under the pointer shows whether the dragged task goes before or after it. The component gets the
    // final index (MoveTask(from, to), counted among the active tasks of the user) and saves it; this script only draws the indicator. Delegated on document.
    let dotnet = null;
    let from = null;

    window.blazorStocTaskOrder = {
        register(reference) { dotnet = reference; },
        unregister() { dotnet = null; }
    };

    const rows = () => [...document.querySelectorAll('tr[data-task-index]')];
    const clear = () => rows().forEach(row => row.classList.remove('drop-before', 'drop-after', 'dragging'));
    const after = (row, event) => {
        const box = row.getBoundingClientRect();
        return event.clientY > box.top + box.height / 2;
    };
    const target = (row, event) => {
        const place = Number(row.dataset.taskIndex) + (after(row, event) ? 1 : 0);
        return from < place ? place - 1 : place;
    };

    document.addEventListener('dragstart', event => {
        const handle = event.target.closest?.('[data-task-handle]');
        if (!handle) return;
        const row = handle.closest('tr[data-task-index]');
        if (!row) return;
        from = Number(row.dataset.taskIndex);
        event.dataTransfer.effectAllowed = 'move';
        event.dataTransfer.setData('text/plain', row.dataset.taskTitle || '');
        event.dataTransfer.setDragImage(row, 24, 16);
        row.classList.add('dragging');
    });

    document.addEventListener('dragover', event => {
        if (from === null) return;
        const row = event.target.closest?.('tr[data-task-index]');
        if (!row) return;
        event.preventDefault();
        event.dataTransfer.dropEffect = 'move';
        rows().forEach(other => other.classList.remove('drop-before', 'drop-after'));
        row.classList.add(after(row, event) ? 'drop-after' : 'drop-before');
    });

    document.addEventListener('drop', event => {
        if (from === null) return;
        const row = event.target.closest?.('tr[data-task-index]');
        if (!row) return;
        event.preventDefault();
        const to = target(row, event);
        const start = from;
        from = null;
        clear();
        if (dotnet && to !== start) dotnet.invokeMethodAsync('MoveTask', start, to).catch(() => { });
    });

    document.addEventListener('dragend', () => { from = null; clear(); });
})();
