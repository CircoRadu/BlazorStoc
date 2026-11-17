// Display scale of the user (PROP-0034).
// displaySettings.apply: puts the global scale on the application shell at once, without reloading the page (the page "Setari afisare" calls it after a save).
window.displaySettings = {
    apply: function (names, values) {
        var shell = document.querySelector('.app-shell');
        if (!shell) return;
        ['table', 'header', 'images', 'icons', 'titles', 'tabs', 'menu'].forEach(function (name) { shell.style.removeProperty('--ui-' + name); });
        for (var i = 0; i < names.length; i++) shell.style.setProperty('--ui-' + names[i], values[i]);
    }
};

// tableGears: a gear on EVERY data table of the application (the generic form of the gear of the products table). The tables are found in the page, each gets a stable scope id
// (page + the heading of its card), the buttons live in a layer of their own outside the page markup (the page is drawn by Blazor, which must not meet foreign nodes), and the
// table settings of the user are put on the tables as CSS custom properties. The panel itself is drawn by the Blazor component TableGearHost.
window.tableGears = (function () {
    var NAMES = ['table', 'header', 'images', 'icons', 'titles', 'tabs'];
    var ref = null, styles = {}, layer = null, observer = null, scheduled = false, shown = true;
    var gears = new Map();   // table element -> { scope, label, button }

    function slug(text, max) {
        return (text || '').normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().replace(/\d+/g, '').replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '').slice(0, max);
    }
    function pathSlug() {
        var path = location.pathname.split('/').filter(Boolean).map(function (part) { return /^\d+$/.test(part) ? 'x' : part; }).join('-');
        return slug(path, 28) || 'home';
    }
    function headingOf(table) {
        var holder = table.closest('.catalog, section, .card, .editor-card, .movement-card') || table.parentElement;
        var heading = holder && holder.querySelector('.catalog-heading h2, h2, h3');
        return heading ? heading.textContent.replace(/\s+/g, ' ').replace(/(\s+\d+)+\s*$/, '').trim() : '';
    }
    function eligible(table) {
        if (table.closest('dialog, #display-sample, .display-gear-panel, .table-gear-panel, [data-display-own], .table-gear-layer')) return false;
        if (table.parentElement && table.parentElement.closest('table')) return false;
        if (!table.querySelector('thead th')) return false;
        return table.offsetWidth >= 260 && table.offsetHeight > 0;
    }
    function applyStyle(table, scope) {
        NAMES.forEach(function (name) { table.style.removeProperty('--ui-' + name); });
        var text = styles[scope];
        if (!text) return;
        text.split(';').forEach(function (part) {
            var at = part.indexOf(':');
            if (at > 0) table.style.setProperty(part.slice(0, at).trim(), part.slice(at + 1).trim());
        });
    }
    function ensureLayer() {
        if (layer && layer.isConnected) return layer;
        layer = document.createElement('div');
        layer.className = 'table-gear-layer';
        document.body.appendChild(layer);
        return layer;
    }
    // The one rule of place: the gear sits on the heading row of the card (or collapsible section) the table is in, 15 px from the right edge of the card; when the row has
    // other buttons or icons (the actions of the card) the gear is on the same row, in the first position (left of them, 8 px apart). A table with no heading row keeps the gear at its own top right.
    var GEAR = 28, EDGE = 15, SPACE = 8;
    function headingRow(table) {
        for (var card = table.parentElement; card && card !== document.body; card = card.parentElement) {
            var heading = card.querySelector(':scope > summary, :scope > .catalog-heading, :scope > details > summary, :scope > details > .catalog-heading, :scope > .collapsible-details > .catalog-heading');
            if (heading && !heading.contains(table)) return { card: card, heading: heading };
        }
        return null;
    }
    // Something under the place of a gear that must not be covered: a field, a button, a link, or text. The layer of the gears does not take part (it lets the mouse through, and the buttons are skipped).
    function blocked(left, top) {
        var m = 6, points = [[left - m, top - m], [left + GEAR + m, top - m], [left - m, top + GEAR + m], [left + GEAR + m, top + GEAR + m], [left + GEAR / 2, top + GEAR / 2], [left - m, top + GEAR / 2], [left + GEAR + m, top + GEAR / 2]];
        return points.some(function (point) {
            var hit = document.elementsFromPoint(point[0], point[1]).find(function (item) { return !item.closest('.table-gear-layer'); });
            if (!hit) return false;
            if (hit.closest('input, select, textarea, button, a, label, [role=button]:not(summary), .searchable-select, .search-box')) return true;
            // Text: only when a line of text of the element is really under the point (a cell wider than its text is free).
            return Array.prototype.some.call(hit.childNodes, function (node) {
                if (node.nodeType !== 3 || node.textContent.trim().length === 0) return false;
                var range = document.createRange();
                range.selectNodeContents(node);
                return Array.prototype.some.call(range.getClientRects(), function (box) { return point[0] >= box.left - 2 && point[0] <= box.right + 2 && point[1] >= box.top - 2 && point[1] <= box.bottom + 2; });
            });
        });
    }
    function place() {
        gears.forEach(function (gear, table) {
            var rect = table.getBoundingClientRect();
            var visible = table.isConnected && rect.width > 0 && rect.bottom > 40 && rect.top < window.innerHeight;
            var row = visible ? headingRow(table) : null;
            var head = row ? row.heading.getBoundingClientRect() : null;
            if (row && (head.width === 0 || head.bottom < 40 || head.top > window.innerHeight)) visible = false;
            if (!shown) visible = false;
            gear.button.style.display = visible ? '' : 'none';
            if (!visible) return;
            var holder = table.closest('.table-scroll, .table-wrap') || table;
            var edge = holder.getBoundingClientRect();
            var candidates = [];
            if (row) {
                // The card is the outer one when the heading is inside a collapsible section of it (the actions of the card are its own children, not the section's).
                var scope = row.card.parentElement && row.card.parentElement.matches('.catalog, .card, .editor-card') ? row.card.parentElement : row.card;
                var card = scope.getBoundingClientRect();
                var middle = head.top + head.height / 2;
                var left = card.right - EDGE - GEAR;
                // Only the controls on the right half of the card count (the handle to drag a card on the left is not "the icons of the row").
                scope.querySelectorAll('button, a.row-icon, [role=button]').forEach(function (control) {
                    if (control === row.heading || control.contains(row.heading) || control.closest('table, .table-gear-layer') || control.offsetWidth === 0) return;
                    var box = control.getBoundingClientRect();
                    if (box.left + box.width / 2 < card.left + card.width / 2) return;
                    if (Math.abs(box.top + box.height / 2 - middle) <= Math.max(head.height, box.height) / 2 && box.right <= card.right + 1) left = Math.min(left, box.left - SPACE - GEAR);
                });
                candidates.push([left, middle - GEAR / 2]);
            }
            // Zone 2: just above the header of the table, at its right edge; then the corner of the table itself.
            var right = Math.min(edge.right, window.innerWidth - 8) - EDGE - GEAR;
            candidates.push([right, Math.max(edge.top - GEAR - 6, 64)]);
            candidates.push([right, Math.max(rect.top + 4, 64)]);
            var chosen = candidates.find(function (item) { return !blocked(item[0], item[1]); }) || candidates[0];
            gear.button.style.left = Math.round(chosen[0]) + 'px';
            gear.button.style.top = Math.round(chosen[1]) + 'px';
        });
    }
    function scan() {
        scheduled = false;
        if (!ref) return;
        var seen = {}, present = new Set();
        document.querySelectorAll('.workspace table').forEach(function (table) {
            if (!eligible(table)) return;
            var label = headingOf(table);
            var base = ('t.' + pathSlug() + '.' + (slug(label, 30) || 'tabel')).slice(0, 72);
            var n = seen[base] = (seen[base] || 0) + 1;
            var scope = n > 1 ? base + '-' + n : base;
            present.add(table);
            var gear = gears.get(table);
            if (!gear) {
                var button = document.createElement('button');
                button.type = 'button';
                button.className = 'gear-button table-gear-button';
                button.setAttribute('aria-haspopup', 'dialog');
                button.addEventListener('click', function () {
                    var g = gears.get(table);
                    if (!g || !ref) return;
                    var r = button.getBoundingClientRect();
                    ref.invokeMethodAsync('Open', g.scope, g.label || 'tabel', Math.max(8, Math.min(r.right - 300, window.innerWidth - 316)), Math.max(8, Math.min(r.bottom + 6, window.innerHeight - 430)));
                });
                ensureLayer().appendChild(button);
                gear = { scope: scope, label: label, button: button };
                gears.set(table, gear);
            }
            gear.scope = scope; gear.label = label;
            var title = 'Setări de afișare pentru ' + (label || 'acest tabel');
            gear.button.title = title; gear.button.setAttribute('aria-label', title);
            applyStyle(table, scope);
        });
        gears.forEach(function (gear, table) {
            if (!present.has(table)) { gear.button.remove(); gears.delete(table); }
        });
        place();
    }
    function schedule() { if (!scheduled) { scheduled = true; setTimeout(scan, 150); } }

    return {
        init: function (dotNetRef) {
            ref = dotNetRef;
            if (!observer) {
                observer = new MutationObserver(function (mutations) {
                    for (var i = 0; i < mutations.length; i++) {
                        var target = mutations[i].target;
                        if (!(target.closest && target.closest('.table-gear-layer'))) { schedule(); return; }
                    }
                });
                observer.observe(document.body, { childList: true, subtree: true });
                window.addEventListener('scroll', place, true);
                window.addEventListener('resize', schedule);
            }
            schedule();
        },
        setStyles: function (map) { styles = map || {}; scan(); },
        // PROP-0037: the buttons of the individual style of a table are hidden when the user turned them off (the settings of the tables are kept).
        setVisible: function (value) { shown = value !== false; place(); },
        dispose: function () {
            ref = null;
            if (observer) { observer.disconnect(); observer = null; }
            window.removeEventListener('scroll', place, true);
            window.removeEventListener('resize', schedule);
            gears.forEach(function (gear) { gear.button.remove(); });
            gears.clear();
        }
    };
})();
