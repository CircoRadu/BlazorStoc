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
        if (table.closest('details:not([open])') || (table.checkVisibility && !table.checkVisibility())) return false;
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
    // The one rule of place (PROP-0037, BUG-0050..0053): the gear of a table sits in the page ABOVE the header of the table, at the right edge of the holder that holds the table, in a strip of its own
    // that the page reserves (the holder gets the class gear-reserved: a margin above it). Whatever is above the table (a search field, a switch, a note) therefore keeps its place and the gear covers
    // nothing. When the user turns the gears off the class is taken away and the space goes with it.
    var GEAR = 28, RESERVE = 37, LIFT = 31;
    function holderOf(table) { return table.closest('.table-scroll, .table-wrap') || table; }
    function reserve(holder, on) { if (on) { if (!holder.classList.contains('gear-reserved')) holder.classList.add('gear-reserved'); } else holder.classList.remove('gear-reserved'); }
    function place() {
        gears.forEach(function (gear, table) {
            var holder = holderOf(table);
            reserve(holder, shown);
            var rect = holder.getBoundingClientRect();
            var visible = shown && table.isConnected && rect.width > 0 && rect.bottom > 40 && rect.top - LIFT > 56 && rect.top < window.innerHeight;
            gear.button.style.display = visible ? '' : 'none';
            if (!visible) return;
            gear.button.style.left = Math.round(Math.min(rect.right, window.innerWidth - 8) - GEAR) + 'px';
            gear.button.style.top = Math.round(rect.top - LIFT) + 'px';
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
            if (!present.has(table)) { gear.button.remove(); gears.delete(table); reserve(holderOf(table), false); }
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
                observer.observe(document.body, { childList: true, subtree: true, attributes: true, attributeFilter: ['class', 'open'] });
                window.addEventListener('scroll', place, true);
                window.addEventListener('resize', schedule);
            }
            schedule();
        },
        setStyles: function (map) { styles = map || {}; scan(); },
        // PROP-0037: the buttons of the individual style of a table are hidden when the user turned them off (the settings of the tables are kept).
        setVisible: function (value) { shown = value !== false; place(); setTimeout(place, 60); },
        dispose: function () {
            ref = null;
            if (observer) { observer.disconnect(); observer = null; }
            window.removeEventListener('scroll', place, true);
            window.removeEventListener('resize', schedule);
            gears.forEach(function (gear, table) { gear.button.remove(); reserve(holderOf(table), false); });
            gears.clear();
        }
    };
})();
