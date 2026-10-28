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
    // of that category; the strips between the categories (data-new-category-at, shown while dragging) turn a leaf into a new category (stage 7).
    // A tooltip next to the pointer says what a drop would do, or why it is refused (a red tooltip and no drop); the server checks again.
    // The component gets DropSubcategory(category, name, targetCategory, targetName, zone), checks the conflicts and saves.
    let dragNode = null;
    let tip = null;
    const nodeRows = () => [...document.querySelectorAll('[data-node-name]')];
    const strips = () => [...document.querySelectorAll('[data-new-category-at]')];
    const hideTip = () => { if (tip) tip.hidden = true; };
    const clearNodes = () => {
        nodeRows().forEach(row => row.classList.remove('drop-before', 'drop-after', 'drop-inside', 'dragging'));
        document.querySelectorAll('[data-category-drop]').forEach(item => item.classList.remove('drop-inside'));
        strips().forEach(strip => strip.classList.remove('drop-inside', 'drop-blocked'));
        document.body.classList.remove('dragging-node');
        hideTip();
    };
    const zoneOf = (row, event) => {
        const box = row.getBoundingClientRect();
        const place = (event.clientY - box.top) / Math.max(box.height, 1);
        return place < 0.25 ? 'before' : place > 0.75 ? 'after' : 'inside';
    };
    const quote = name => '«' + name + '»';
    const maxDepth = () => Number(document.querySelector('[data-max-depth]')?.dataset.maxDepth) || 3;
    const levelsText = height => height > 1 ? ', împreună cu structura de sub ea (' + (height - 1) + (height === 2 ? ' nivel' : ' niveluri') + ')' : '';
    const rowInfo = row => ({
        category: row.dataset.nodeCategory, name: row.dataset.nodeName, depth: Number(row.dataset.depth) || 0,
        leaf: row.dataset.nodeLeaf === '1', products: Number(row.dataset.nodeProducts) || 0, height: Number(row.dataset.nodeHeight) || 1
    });
    // Whether the row is under the dragged one (rows follow their parent in the table, with a greater depth).
    const underSource = row => {
        const source = document.querySelector('tr.dragging');
        if (!source || !dragNode) return false;
        for (let next = source.nextElementSibling; next && Number(next.dataset.depth) > dragNode.depth; next = next.nextElementSibling)
            if (next === row) return true;
        return false;
    };
    const tooDeep = (levels) => 'Depășește adâncimea maximă (' + maxDepth() + ' niveluri, Setări → Catalog).';

    // What a drop on the row, the title or the strip does: { ok, text }.
    const describeRow = (row, zone) => {
        const target = rowInfo(row);
        const me = quote(dragNode.name);
        if (row.classList.contains('dragging')) return { ok: false, text: 'Aceasta este subcategoria trasă.' };
        if (underSource(row)) return { ok: false, text: 'O subcategorie nu poate fi mutată în propriul ei subarbore.' };
        const elsewhere = target.category !== dragNode.category ? ' în categoria ' + quote(target.category) : '';
        if (zone === 'inside') {
            if (target.products > 0) return { ok: false, text: quote(target.name) + ' are ' + target.products + ' produse, deci nu poate primi subcategorii copil. Mută întâi produsele într-o frunză.' };
            if (target.depth + 1 + dragNode.height > maxDepth()) return { ok: false, text: tooDeep() };
            return { ok: true, text: 'Mută ' + me + ' ca subcategorie copil a lui ' + quote(target.name) + elsewhere + ' (nivel ' + (target.depth + 2) + ')' + levelsText(dragNode.height) + '. Dacă se schimbă ce moștenește, urmează fereastra de verificări.' };
        }
        if (target.depth + dragNode.height > maxDepth()) return { ok: false, text: tooDeep() };
        return { ok: true, text: 'Mută ' + me + (zone === 'before' ? ' înainte de ' : ' după ') + quote(target.name) + elsewhere + ', la nivelul ' + (target.depth + 1) + levelsText(dragNode.height) + '.' };
    };
    const describeTitle = title => {
        if (dragNode.height > maxDepth()) return { ok: false, text: tooDeep() };
        const category = title.dataset.categoryDrop;
        return { ok: true, text: 'Mută ' + quote(dragNode.name) + ' pe primul nivel al categoriei ' + quote(category) + ', ca ultima subcategorie' + levelsText(dragNode.height) + '.' };
    };
    const describeStrip = strip => {
        if (!dragNode.leaf) return { ok: false, text: 'Doar subcategoriile fără copii pot deveni categorii (' + quote(dragNode.name) + ' are subcategorii copil).' };
        const before = strip.previousElementSibling?.closest?.('[data-category-name]') ?? strip.previousElementSibling;
        const previous = before?.dataset?.categoryName;
        const place = previous ? 'după ' + quote(previous) : 'pe prima poziție';
        return { ok: true, text: 'Face din ' + quote(dragNode.name) + ' o categorie nouă, ' + place + '. Vei alege numele noii subcategorii (produsele, parametrii și etichetele rămân pe ea) și vei confirma verificările; nimic nu se schimbă până atunci.' };
    };

    const showTip = (result, event) => {
        if (!tip) {
            tip = document.createElement('div');
            tip.className = 'drag-tip';
            tip.setAttribute('role', 'status');
            document.body.appendChild(tip);
        }
        tip.hidden = false;
        tip.textContent = result.text;
        tip.classList.toggle('blocked', !result.ok);
        const width = tip.offsetWidth, height = tip.offsetHeight;
        const left = Math.max(8, Math.min(event.clientX + 18, window.innerWidth - width - 8));
        const top = event.clientY + 22 + height > window.innerHeight ? Math.max(8, event.clientY - height - 14) : event.clientY + 22;
        tip.style.left = left + 'px';
        tip.style.top = top + 'px';
    };

    document.addEventListener('dragstart', event => {
        const handle = event.target.closest?.('[data-subcategory-handle]');
        if (!handle) return;
        const row = handle.closest('[data-node-name]');
        if (!row) return;
        dragNode = rowInfo(row);
        event.stopPropagation();
        event.dataTransfer.effectAllowed = 'move';
        event.dataTransfer.setData('text/plain', row.dataset.nodeName || '');
        event.dataTransfer.setDragImage(row, 24, 16);
        row.classList.add('dragging');
        // The strips between the categories appear after the drag began (changing the layout inside dragstart can cancel the drag).
        setTimeout(() => { if (dragNode) document.body.classList.add('dragging-node'); }, 0);
    }, true);

    // The place the pointer is over: a row (with its zone), a category title or a strip between the categories.
    const placeOf = event => {
        const row = event.target.closest?.('[data-node-name]');
        if (row) { const zone = zoneOf(row, event); return { element: row, zone, result: describeRow(row, zone) }; }
        const title = event.target.closest?.('[data-category-drop]');
        if (title) return { element: title, zone: 'end', result: describeTitle(title) };
        const strip = event.target.closest?.('[data-new-category-at]');
        if (strip) return { element: strip, zone: 'newcategory', result: describeStrip(strip) };
        return null;
    };

    document.addEventListener('dragover', event => {
        if (dragNode === null) return;
        const place = placeOf(event);
        nodeRows().forEach(row => row.classList.remove('drop-before', 'drop-after', 'drop-inside'));
        document.querySelectorAll('[data-category-drop]').forEach(item => item.classList.remove('drop-inside'));
        strips().forEach(strip => strip.classList.remove('drop-inside', 'drop-blocked'));
        if (!place) { hideTip(); return; }
        showTip(place.result, event);
        if (!place.result.ok) {
            event.dataTransfer.dropEffect = 'none';
            if (place.zone === 'newcategory') place.element.classList.add('drop-blocked');
            return;
        }
        event.preventDefault();
        event.dataTransfer.dropEffect = 'move';
        if (place.zone === 'end' || place.zone === 'newcategory') place.element.classList.add('drop-inside');
        else place.element.classList.add('drop-' + place.zone);
    });

    document.addEventListener('drop', event => {
        if (dragNode === null) return;
        const place = placeOf(event);
        if (!place || !place.result.ok) return;
        event.preventDefault();
        const source = dragNode;
        const element = place.element;
        const target = place.zone === 'newcategory'
            ? { category: element.dataset.newCategoryAt, name: '', zone: 'newcategory' }
            : place.zone === 'end'
                ? { category: element.dataset.categoryDrop, name: '', zone: 'end' }
                : { category: element.dataset.nodeCategory, name: element.dataset.nodeName, zone: place.zone };
        dragNode = null;
        clearNodes();
        if (dotnet) dotnet.invokeMethodAsync('DropSubcategory', source.category, source.name, target.category, target.name, target.zone).catch(() => { });
    });

    document.addEventListener('dragend', () => { dragNode = null; clearNodes(); });
})();
