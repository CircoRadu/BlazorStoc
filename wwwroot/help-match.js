// Finds, on the page that is open, the element a help topic describes (docs/PROPUNERE_HELP_IMAGINI.md). The same code is used by
// tools/help-capture.mjs (to measure the boxes drawn on a screenshot) (the help window no longer frames elements on the live page: the picture of the topic shows them).
// An element of a .help file is described by { name, kind, icon, sel }: "sel" (the "sel:" option of the line) is a CSS selector or text:"...",
// the "iconiță" kind is found by its row-icon, anything else by its name (button, link, tab, column heading, label, field, title).
(() => {
    const dialogId = "help-dialog";
    const maxBoxes = 6;

    const norm = (text) => (text || "")
        .normalize("NFD").replace(/[̀-ͯ]/g, "")
        .toLowerCase()
        .replace(/[„”“«»"'’]/g, "")
        .replace(/[←→↑↓+✓✕×•·⋮…:]/g, " ")
        .replace(/\s+/g, " ")
        .trim();

    // A window over the page (a dialog) hides what is behind it from the help: the elements are looked for inside the topmost one.
    function scope() {
        const open = Array.from(document.querySelectorAll("dialog[open], [role=dialog], [aria-modal=true], .invoice-modal")).filter((el) => el.id !== dialogId && visible(el));
        return open.length ? open[open.length - 1] : document;
    }

    function visible(el) {
        if (!(el instanceof HTMLElement || el instanceof SVGElement)) return false;
        if (el.closest("#" + dialogId) || el.closest("[hidden]")) return false;
        const rect = el.getBoundingClientRect();
        if (rect.width < 2 || rect.height < 2) return false;
        const style = getComputedStyle(el);
        return style.visibility !== "hidden" && style.display !== "none" && style.opacity !== "0";
    }

    const candidateSelector = "button, a, [role=tab], [role=button], th, label, legend, h1, h2, h3, h4, summary, .count-badge, .chip, .badge, .pill, .status, input, select, textarea, [aria-label], [title]";

    function labelsOf(el) {
        const labels = [];
        const own = (el.getAttribute("aria-label") || "").trim();
        if (own) labels.push(own);
        const title = (el.getAttribute("title") || "").trim();
        if (title) labels.push(title);
        if (el instanceof HTMLInputElement || el instanceof HTMLSelectElement || el instanceof HTMLTextAreaElement) {
            if (el.placeholder) labels.push(el.placeholder);
            Array.from(el.labels || []).forEach((label) => labels.push((label.textContent || "").trim()));
            const wrapper = el.closest("label");
            if (wrapper) labels.push((wrapper.textContent || "").trim());
        } else {
            const text = (el.textContent || "").trim();
            if (text && text.length <= 90) labels.push(text);
        }
        return labels.filter(Boolean);
    }

    // 3 exact, 2 one starts with the other, 1 contains; 0 no match.
    function score(label, wanted) {
        const text = norm(label);
        if (!text || !wanted) return 0;
        if (text === wanted) return 3;
        if (text.startsWith(wanted) || (text.length >= 4 && wanted.startsWith(text))) return 2;
        if (wanted.length >= 4 && text.includes(wanted) && text.length <= wanted.length * 2 + 12) return 1;
        return 0;
    }

    function byText(wanted) {
        const key = norm(wanted);
        let best = 0;
        let found = [];
        scope().querySelectorAll(candidateSelector).forEach((el) => {
            if (!visible(el)) return;
            const points = Math.max(0, ...labelsOf(el).map((label) => score(label, key)));
            if (points === 0 || points < best) return;
            if (points > best) { best = points; found = []; }
            found.push(el);
        });
        if (found.length === 0) found = byOwnText(key);
        // The same element reached through its parent: keep the deepest ones.
        return found.filter((el) => !found.some((other) => other !== el && el.contains(other)));
    }

    // The last resort: any element with a short text of its own (the label of a figure on a card, a count). A small card ("article", "li") around it is the box.
    function byOwnText(key) {
        const found = [];
        let best = 0;
        const root = scope();
        const walker = document.createTreeWalker(root === document ? document.body : root, NodeFilter.SHOW_TEXT);
        for (let node = walker.nextNode(); node; node = walker.nextNode()) {
            const text = (node.textContent || "").trim();
            if (text.length < 2 || text.length > 90) continue;
            const points = score(text, key);
            if (points === 0 || points < best) continue;
            let el = node.parentElement;
            if (!el || !visible(el)) continue;
            const card = el.closest("article, li, .card, .metric, .stat");
            if (card && card !== el && (card.textContent || "").length <= 140 && visible(card)) el = card;
            if (points > best) { best = points; found.length = 0; }
            if (!found.includes(el)) found.push(el);
        }
        return found;
    }

    function bySelector(selector) {
        const text = /^text:\s*"(.*)"$/.exec(selector);
        if (text) return byText(text[1]);
        // "page:<css>": looked for in the whole page, also behind an open window (a notice of the page that the window leaves visible, dimmed).
        const whole = selector.startsWith("page:");
        try { return Array.from((whole ? document : scope()).querySelectorAll(whole ? selector.slice(5) : selector)).filter(visible); }
        catch (error) { return []; }
    }

    function find(descriptor) {
        if (!descriptor) return [];
        let found = [];
        if (descriptor.sel) found = bySelector(descriptor.sel);
        else if (descriptor.icon) found = Array.from(scope().querySelectorAll(".row-icon." + CSS.escape(descriptor.icon))).filter(visible);
        else {
            // "Categorie, Subcategorie": each part is an element.
            const parts = String(descriptor.name || "").split(/\s*,\s*/).filter(Boolean);
            parts.forEach((part) => byText(part).forEach((el) => { if (!found.includes(el)) found.push(el); }));
        }
        return found.slice(0, maxBoxes);
    }

    // The boxes of the found elements in the coordinates of the whole document ([x, y, width, height], whole pixels).
    // With "viewport" the box is in the coordinates of the window (a picture of what is on the screen, not of the whole page).
    function boxes(descriptor, viewport) {
        return find(descriptor).map((el) => {
            const rect = el.getBoundingClientRect();
            return [Math.round(rect.left + (viewport ? 0 : scrollX)), Math.round(rect.top + (viewport ? 0 : scrollY)), Math.round(rect.width), Math.round(rect.height)];
        });
    }

    window.HelpMatch = { find, boxes, norm };
})();
