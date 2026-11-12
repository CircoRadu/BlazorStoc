(() => {
    // "Tichet exemplu" (Components/Shared/TicketSampleDialog.razor): the lines of the notes of the intervention are put in order by dragging the rows of the "Folosite" table
    // (tr.notes-order-row[data-notes-key]). BUG-0031: the drag and drop was handled by the server on every event (dragover many times a second) and the drop did not arrive;
    // here the browser decides by itself (preventDefault on dragover over a row that can take the dragged one) and the component gets only the final MoveNotes(key, targetKey).
    // Delegated on document, so nothing has to be set up again when the window is drawn again.
    let dotnet = null;
    let dragged = null;

    window.blazorStocNotesOrder = {
        register(reference) { dotnet = reference; },
        unregister() { dotnet = null; }
    };

    const rows = () => [...document.querySelectorAll('tr.notes-order-row[data-notes-key]')];
    const clear = () => rows().forEach(row => row.classList.remove('drop-before', 'drop-after', 'dragging'));
    const rowOf = (event) => event.target.closest?.('tr.notes-order-row[data-notes-key]');

    document.addEventListener('dragstart', event => {
        const row = rowOf(event);
        if (!row || event.target.closest('input, select, button, textarea')) return;
        dragged = row.dataset.notesKey;
        event.dataTransfer.effectAllowed = 'move';
        event.dataTransfer.setData('text/plain', dragged);
        row.classList.add('dragging');
    });

    document.addEventListener('dragover', event => {
        if (dragged === null) return;
        const row = rowOf(event);
        if (!row || row.dataset.notesKey === dragged) return;
        event.preventDefault();
        event.dataTransfer.dropEffect = 'move';
        rows().forEach(other => other.classList.remove('drop-before', 'drop-after'));
        row.classList.add('drop-before');
    });

    document.addEventListener('drop', event => {
        if (dragged === null) return;
        const row = rowOf(event);
        if (!row) return;
        event.preventDefault();
        const from = dragged;
        const to = row.dataset.notesKey;
        dragged = null;
        clear();
        if (dotnet && to && to !== from) dotnet.invokeMethodAsync('MoveNotes', from, to).catch(() => { });
    });

    document.addEventListener('dragend', () => { dragged = null; clear(); });
})();
