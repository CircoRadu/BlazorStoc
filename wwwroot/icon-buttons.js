// Turns text action buttons into icon buttons without touching Blazor-owned markup:
// only data-* attributes, title/aria-label and a CSS variable are set; the label text stays in the DOM (hidden by CSS).
(function () {
    const p = (d) => '<path d="' + d + '"/>';
    const I = {
        save: p('M19 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11l5 5v11a2 2 0 0 1-2 2z') + p('M17 21v-8H7v8M7 3v5h8'),
        cancel: p('M18 6 6 18M6 6l12 12'),
        close: p('M18 6 6 18M6 6l12 12'),
        retry: p('M21 12a9 9 0 1 1-3-6.7L21 8') + p('M21 3v5h-5'),
        refresh: p('M21 12a9 9 0 1 1-3-6.7L21 8') + p('M21 3v5h-5'),
        edit: p('M12 20h9') + p('M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4z'),
        del: p('M3 6h18M8 6V4h8v2M19 6l-1 14H6L5 6M10 11v6M14 11v6'),
        add: p('M12 5v14M5 12h14'),
        back: p('M19 12H5M12 19l-7-7 7-7'),
        next: p('M5 12h14M12 5l7 7-7 7'),
        ok: p('M20 6 9 17l-5-5'),
        test: p('M9 3h6M10 3v6L4 20h16L14 9V3') + p('M7 15h10'),
        logout: p('M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4M16 17l5-5-5-5M21 12H9'),
        print: p('M6 9V2h12v7M6 18H4a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2h-2') + p('M6 14h12v8H6z'),
        download: p('M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4M7 10l5 5 5-5M12 15V3'),
        upload: p('M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4M17 8l-5-5-5 5M12 3v12'),
        backup: p('M12 3v12M7 10l5 5 5-5') + p('M4 19h16'),
        search: p('M21 21l-4.3-4.3') + '<circle cx="11" cy="11" r="7"/>',
        filter: p('M3 4h18l-7 8v6l-4 2v-8z'),
        clear: p('M3 4h18l-7 8v6l-4 2v-8z') + p('M16 16l5 5M21 16l-5 5'),
        lock: p('M5 11h14v10H5z') + p('M8 11V7a4 4 0 0 1 8 0v4'),
        unlock: p('M5 11h14v10H5z') + p('M8 11V7a4 4 0 0 1 7.5-2'),
        key: '<circle cx="8" cy="15" r="4"/>' + p('M11 12l9-9M16 7l3 3'),
        copy: p('M9 9h11v11H9z') + p('M5 15H4V4h11v1'),
        eye: p('M2 12s4-7 10-7 10 7 10 7-4 7-10 7S2 12 2 12z') + '<circle cx="12" cy="12" r="3"/>',
        play: p('M6 4l14 8-14 8z'),
        reserve: p('M19 21l-7-4-7 4V5a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2z'),
        calendar: p('M3 5h18v16H3zM16 3v4M8 3v4M3 10h18'),
        cal1: p('M3 5h18v16H3zM16 3v4M8 3v4M3 10h18') + p('M10.5 13.5 12.5 12.5v6'),
        cal3: p('M3 5h18v16H3zM16 3v4M8 3v4M3 10h18') + p('M10 13h4l-2 2.5a2 2 0 1 1-2 2.8'),
        all: p('M4 6c0-1.7 3.6-3 8-3s8 1.3 8 3-3.6 3-8 3-8-1.3-8-3zM4 6v6c0 1.7 3.6 3 8 3s8-1.3 8-3V6M4 12v6c0 1.7 3.6 3 8 3s8-1.3 8-3v-6'),
        csv: p('M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8zM14 2v6h6') + p('M8 13h8M8 17h8M12 11v8'),
        clock: '<circle cx="12" cy="12" r="9"/>' + p('M12 7v5l3 2'),
        undo: p('M3 7v6h6') + p('M3 13a9 9 0 1 0 3-7.7L3 8'),
        check: p('M9 11l3 3L22 4') + p('M21 12v7a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11'),
        stock: p('M21 8l-9-5-9 5v8l9 5 9-5z') + p('M3 8l9 5 9-5M12 13v8'),
        wand: p('M15 4l5 5L9 20l-5-5z') + p('M13 6l5 5'),
        list: p('M8 6h13M8 12h13M8 18h13M3 6h.01M3 12h.01M3 18h.01'),
        link: p('M10 13a5 5 0 0 0 7 0l3-3a5 5 0 0 0-7-7l-1 1') + p('M14 11a5 5 0 0 0-7 0l-3 3a5 5 0 0 0 7 7l1-1'),
        file: p('M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z') + p('M14 2v6h6')
    };
    // [regex on the normalised label, icon]; first match wins.
    const RULES = [
        [/^(sterge|elimina|revoca)/, 'del'], [/^renunt.*elimina/, 'del'],
        [/^(anuleaza|renunt|fara |nu,)/, 'cancel'], [/^inchide/, 'close'], [/^gata/, 'ok'],
        [/^(reincearca|reincarca|reimprospateaza|recitește|recitește|recite)/, 'retry'], [/actualizeaza/, 'refresh'],
        [/^(salveaza|seteaza|trimite modificari)/, 'save'], [/^(da, aplica|aplica|continua|confirma|adauga totusi)/, 'ok'],
        [/^(editeaza|modifica|modific)/, 'edit'], [/^(\+|adauga|creeaza)/, 'add'],
        [/^(←|pasul precedent|inapoi)/, 'back'], [/(pasul urmator|→)/, 'next'],
        [/^(testeaza|simuleaza|verifica|previzualizeaza)/, 'test'], [/^deconectare/, 'logout'], [/^imprima/, 'print'],
        [/csv/, 'csv'], [/^(exporta|descarca)/, 'download'], [/^(importa|incarca|preia|citeste)/, 'upload'],
        [/(backup|copiaza pe nas)/, 'backup'], [/^(restaureaza)/, 'undo'], [/^(revino|reseteaza)/, 'undo'],
        [/(filtrele|sterge filtr)/, 'clear'], [/^(cauta)/, 'search'], [/^deblocheaza/, 'unlock'], [/^genereaza parola/, 'key'],
        [/^copiaza/, 'copy'], [/^(arata|vezi)/, 'eye'], [/^(rezerva)/, 'reserve'],
        [/^ultima luna/, 'cal1'], [/^ultimele/, 'cal3'], [/^(tot jurnalul|fara perioada)/, 'all'], [/(termen)/, 'clock'],
        [/(regularizeaza|stocul|stoc)/, 'stock'], [/^(completeaza|liniile|incadreaza)/, 'wand'], [/(selectia|selectate)/, 'list']
    ];
    const norm = (s) => s.toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '').replace(/^[^a-z+←‹]+/, (m) => m.includes('+') ? '+' : '').trim();
    const SEL = 'button.primary,button.danger,button.refresh,button.secondary,button.reload,a.refresh,a.primary,a.secondary'.split(',').map((s) => s + ':not(.row-icon):not([data-ico-skip]):not(.movement-layout *)').join(',');

    function decorate(el) {
        const raw = (el.dataset.icoLabel !== undefined && el.dataset.icoText === undefined ? el.dataset.icoLabel : el.textContent).trim();
        if (!raw || raw.length > 60 || el.dataset.icoSkip !== undefined) return;
        if (el.dataset.icoLabel === raw) return;
        const n = norm(raw);
        let icon = null;
        for (const [re, name] of RULES) if (re.test(n)) { icon = name; break; }
        el.dataset.icoLabel = raw;
        if (!icon) { el.removeAttribute('data-ico'); return; }
        const svg = '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="black" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' + I[icon] + '</svg>';
        el.style.setProperty('--ico', 'url("data:image/svg+xml,' + encodeURIComponent(svg) + '")');
        el.setAttribute('data-ico', icon);
        if (!el.hasAttribute('title') || el.dataset.icoTitle !== undefined) { el.setAttribute('title', raw); el.dataset.icoTitle = ''; }
        if (!el.hasAttribute('aria-label')) el.setAttribute('aria-label', raw);
    }
    let queued = false;
    function scan() { queued = false; document.querySelectorAll(SEL).forEach(decorate); }
    function queue() { if (!queued) { queued = true; requestAnimationFrame(scan); } }
    new MutationObserver(queue).observe(document.documentElement, { childList: true, subtree: true, characterData: true });
    document.addEventListener('DOMContentLoaded', queue);
    queue();
})();
