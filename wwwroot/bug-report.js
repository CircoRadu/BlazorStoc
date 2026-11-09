// Raportare eroare: the menu entry above "Ajutor" ([data-bug-open]) photographs the page BEFORE the popup (#bug-dialog) shows, then opens it; Ctrl+V adds a picture from the
// clipboard. The picture of the page is made from the DOM (the page is serialised into an SVG image and drawn on a canvas), so the browser asks for no permission. Parts the
// DOM cannot carry (a map, a frame) come out incomplete; the user can paste a screenshot instead. Pictures go to the Blazor component as JS stream references (a picture is
// larger than a SignalR message).
(() => {
    const state = { ref: null, proposalRef: null, errors: [] };
    const reportPopups = "#bug-dialog,#proposal-dialog";
    const maxWidth = 1600;

    const remember = (message) => {
        state.errors.push(`${new Date().toISOString()} ${String(message).slice(0, 300)}`);
        if (state.errors.length > 10) state.errors.shift();
    };
    window.addEventListener("error", (event) => remember(`${event.message} (${event.filename || ""}:${event.lineno || 0})`));
    window.addEventListener("unhandledrejection", (event) => remember(`Promise: ${(event.reason && event.reason.message) || event.reason}`));

    // The popups (modal or not) open right now, except the report popup itself: the report must say which window the author was looking at.
    const popupSelector = "dialog[open],[role=dialog],[role=alertdialog]";
    const isShown = (item) => { const box = item.getBoundingClientRect(); return box.width > 0 && box.height > 0; };
    const openPopups = () => Array.from(document.querySelectorAll(popupSelector)).filter((item) => !item.matches(reportPopups) && !item.closest(reportPopups) && !item.hasAttribute("data-no-bug") && isShown(item));
    const popupName = (dialog) => {
        const heading = dialog.querySelector("h1,h2,h3,[aria-labelledby]");
        const labelled = dialog.getAttribute("aria-labelledby") ? document.getElementById(dialog.getAttribute("aria-labelledby")) : null;
        return ((labelled || heading || {}).textContent || dialog.id || "fereastră").trim().replace(/\s+/g, " ").slice(0, 80);
    };

    const environment = () => ({
        url: location.pathname + location.search,
        title: document.title + (openPopups().length ? " · fereastra: " + openPopups().map(popupName).join(" > ") : ""),
        popups: openPopups().map(popupName),
        viewport: `${window.innerWidth}x${window.innerHeight}`,
        screen: `${screen.width}x${screen.height}`,
        theme: document.documentElement.getAttribute("data-theme") || "light",
        language: navigator.language,
        timeZone: Intl.DateTimeFormat().resolvedOptions().timeZone,
        userAgent: navigator.userAgent,
        browserErrors: state.errors.slice()
    });

    const blobToDataUrl = (blob) => new Promise((resolve, reject) => {
        const reader = new FileReader();
        reader.onload = () => resolve(reader.result);
        reader.onerror = () => reject(reader.error);
        reader.readAsDataURL(blob);
    });

    const inlineImage = async (clone, original) => {
        try {
            const response = await fetch(original.currentSrc || original.src, { credentials: "same-origin" });
            if (response.ok) clone.setAttribute("src", await blobToDataUrl(await response.blob()));
        } catch { clone.removeAttribute("src"); }
        clone.removeAttribute("srcset");
    };

    // The media queries are decided here, against the real window (inside the picture the viewport is not the window's): a matching @media is flattened, the others are dropped.
    const ruleText = (rules) => {
        let text = "";
        for (const rule of rules) {
            if (rule instanceof CSSMediaRule) { if (window.matchMedia(rule.media.mediaText).matches) text += ruleText(rule.cssRules); }
            else text += rule.cssText + "\n";
        }
        return text;
    };

    // The computed style of an element as text (cssText of a computed style is empty in current browsers).
    const computedText = (element) => {
        const style = getComputedStyle(element);
        let text = "";
        for (let i = 0; i < style.length; i++) text += `${style[i]}:${style.getPropertyValue(style[i])};`;
        return text;
    };

    const collectCss = () => {
        let css = "";
        for (const sheet of document.styleSheets) {
            try { css += ruleText(sheet.cssRules); } catch { /* a sheet of another origin cannot be read */ }
        }
        // The html element becomes a div of the picture: its selectors move to that div.
        return css.replace(/:root/g, ".bug-cap-root").replace(/(^|[\s,{}>+~])html(?=[\s,.{:\[>+~])/g, "$1.bug-cap-root");
    };

    const capturePage = async () => {
        try {
            const width = document.documentElement.clientWidth;
            const height = window.innerHeight;
            const body = document.body;
            const clone = body.cloneNode(true);
            const sources = body.querySelectorAll("*");
            const targets = clone.querySelectorAll("*");
            const jobs = [];
            const swaps = [];
            const wraps = [];
            let hasPopup = false;
            // Every element carries its computed style, so the picture does not depend on re-running the selectors, the viewport units or the fixed positions of the page.
            clone.setAttribute("style", computedText(body));
            for (let i = 0; i < sources.length; i++) {
                const source = sources[i], target = targets[i];
                if (!(source instanceof SVGElement) || source instanceof SVGSVGElement) target.setAttribute("style", computedText(source));
                // An open popup lives in the top layer, which the picture does not have: it is lifted above everything and a dimming layer is put under it.
                if (source instanceof HTMLDialogElement && source.open && !source.matches(reportPopups)) { target.style.zIndex = "2147483000"; hasPopup = true; }
                if (source instanceof HTMLInputElement) {
                    target.setAttribute("value", source.value);
                    if (source.checked) target.setAttribute("checked", "checked"); else target.removeAttribute("checked");
                } else if (source instanceof HTMLTextAreaElement) target.textContent = source.value;
                else if (source instanceof HTMLSelectElement) Array.from(target.options).forEach((option, index) => { if (index === source.selectedIndex) option.setAttribute("selected", "selected"); else option.removeAttribute("selected"); });
                else if (source instanceof HTMLCanvasElement) { try { swaps.push([target, source.toDataURL("image/png"), source]); } catch { /* tainted */ } }
                else if (source instanceof HTMLImageElement) jobs.push(inlineImage(target, source));
                if ((source.scrollTop > 0 || source.scrollLeft > 0) && source.children.length > 0) wraps.push([target, source.scrollLeft, source.scrollTop]);
            }
            await Promise.all(jobs);
            for (const [target, url, source] of swaps) {
                const img = document.createElement("img");
                img.setAttribute("src", url);
                img.setAttribute("style", `width:${source.clientWidth}px;height:${source.clientHeight}px`);
                target.replaceWith(img);
            }
            for (const [target, left, top] of wraps) {
                const wrapper = document.createElement("div");
                wrapper.setAttribute("style", `transform:translate(${-left}px,${-top}px)`);
                while (target.firstChild) wrapper.appendChild(target.firstChild);
                target.appendChild(wrapper);
            }
            clone.querySelectorAll("script,noscript,dialog:not([open]),#bug-dialog,#proposal-dialog,iframe,#blazor-error-ui").forEach((node) => node.remove());
            if (hasPopup) {
                const dim = document.createElement("div");
                dim.setAttribute("style", "position:fixed;left:0;top:0;right:0;bottom:0;background:rgba(0,0,0,.45);z-index:2147482999");
                clone.appendChild(dim);
            }
            const style = document.createElement("style");
            style.textContent = collectCss();
            const root = document.createElement("div");
            root.className = "bug-cap-root";
            const theme = document.documentElement.getAttribute("data-theme");
            if (theme) root.setAttribute("data-theme", theme);
            root.setAttribute("style", `width:${width}px;height:${height}px;overflow:hidden;position:relative`);
            root.appendChild(style);
            root.appendChild(clone);
            root.setAttribute("xmlns", "http://www.w3.org/1999/xhtml");
            const markup = new XMLSerializer().serializeToString(root);
            const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${width}" height="${height}"><foreignObject x="0" y="0" width="100%" height="100%">${markup}</foreignObject></svg>`;
            const image = new Image();
            await new Promise((resolve, reject) => { image.onload = resolve; image.onerror = reject; image.src = "data:image/svg+xml;charset=utf-8," + encodeURIComponent(svg); });
            const scale = Math.min(1, maxWidth / width);
            const canvas = document.createElement("canvas");
            canvas.width = Math.round(width * scale);
            canvas.height = Math.round(height * scale);
            const context = canvas.getContext("2d");
            context.fillStyle = getComputedStyle(body).backgroundColor || "#fff";
            context.fillRect(0, 0, canvas.width, canvas.height);
            context.drawImage(image, 0, 0, canvas.width, canvas.height);
            return await new Promise((resolve) => canvas.toBlob(resolve, "image/jpeg", 0.85));
        } catch (error) {
            remember(`Captura paginii nu a reusit: ${error && error.message ? error.message : error}`);
            return null;
        }
    };

    // A pasted picture larger than 800 KB is reduced (width at most 1600 px, JPEG) so it travels and is stored light.
    const shrink = async (file) => {
        if (file.size <= 800 * 1024) return file;
        try {
            const bitmap = await createImageBitmap(file);
            const scale = Math.min(1, maxWidth / bitmap.width);
            const canvas = document.createElement("canvas");
            canvas.width = Math.round(bitmap.width * scale);
            canvas.height = Math.round(bitmap.height * scale);
            canvas.getContext("2d").drawImage(bitmap, 0, 0, canvas.width, canvas.height);
            const blob = await new Promise((resolve) => canvas.toBlob(resolve, "image/jpeg", 0.88));
            return blob && blob.size < file.size ? blob : file;
        } catch { return file; }
    };

    // Every popup (a native <dialog> or a div/section with role=dialog) has ONE bar above it, outside its body, on the right: "Raportează o eroare" and the red round close icon. The bar is a
    // manual popover, so it lives in the top layer too and is shown above a modal <dialog>; it is shown again each time another popup becomes the topmost one. Module out (no #bug-dialog):
    // no report button, only the close icon.
    const makeBar = () => {
        const bar = document.createElement("div");
        bar.className = "bug-popup-bar";
        bar.setAttribute("data-popup-bar", "");
        const report = document.createElement("button");
        report.type = "button";
        report.className = "bug-popup-button";
        report.setAttribute("data-bug-open", "");
        report.setAttribute("data-bug-popup", "");
        report.setAttribute("aria-haspopup", "dialog");
        report.title = "Raportează o eroare în această fereastră";
        report.textContent = "⚑ Raportează o eroare";
        const propose = document.createElement("button");
        propose.type = "button";
        propose.className = "bug-popup-button";
        propose.setAttribute("data-proposal-open", "");
        propose.setAttribute("data-proposal-popup", "");
        propose.setAttribute("aria-haspopup", "dialog");
        propose.title = "Propune o modificare sau o funcționalitate nouă";
        propose.textContent = "✎ Propune";
        const close = document.createElement("button");
        close.type = "button";
        close.className = "popup-close-icon";
        close.setAttribute("data-popup-close-icon", "");
        close.setAttribute("aria-label", "Închide fereastra");
        close.title = "Închide fereastra";
        bar.append(report, propose, close);
        return bar;
    };
    const floating = makeBar();
    floating.id = "bug-popup-float";
    floating.classList.add("bug-popup-float");
    const hasPopover = typeof floating.showPopover === "function";
    if (hasPopover) floating.setAttribute("popover", "manual");
    floating.hidden = true;

    // Every popup that is shown, the report popup included (it only gets the close icon).
    const shownPopups = () => Array.from(document.querySelectorAll(popupSelector)).filter((item) => !item.closest("[data-popup-bar]") && !item.hasAttribute("data-no-bug") && isShown(item));

    // The panel of a popup: a backdrop that fills the window is skipped for its first child.
    const panelOf = (popup) => {
        let node = popup;
        for (let guard = 0; guard < 4; guard++) {
            const box = node.getBoundingClientRect();
            if (box.width < innerWidth * 0.9 || box.height < innerHeight * 0.9) break;
            const child = Array.from(node.children).find((item) => isShown(item) && item.getBoundingClientRect().width < box.width - 4);
            if (!child) break;
            node = child;
        }
        return node;
    };

    // The button the form already has for closing: the close icon triggers exactly that action (so an unsaved-changes question, a discard and so on behave the same).
    // Priority: marked with data-popup-close or data-help-close, the .act-close buttons, then the safe words ("Închide", "Anulează", "Nu, păstrează", "Înapoi", "Renunță").
    const closeWords = [/^închide/i, /^anulează/i, /^nu[, ]/i, /^înapoi/i, /^renunță/i];
    const closeButtonOf = (popup) => {
        const buttons = Array.from(popup.querySelectorAll("button")).filter((item) => !item.closest("[data-popup-bar]") && !item.disabled && (isShown(item) || item.classList.contains("popup-close-moved")));
        const text = (item) => (item.textContent || "").trim().replace(/^[^\p{L}]+/u, "");
        return buttons.find((item) => item.hasAttribute("data-popup-close") || item.hasAttribute("data-help-close"))
            || buttons.find((item) => item.classList.contains("act-close") && !item.classList.contains("row-icon"))
            || closeWords.map((word) => buttons.find((item) => word.test(text(item)))).find(Boolean)
            || null;
    };
    const closePopup = (popup) => {
        const button = closeButtonOf(popup);
        if (button) { button.click(); return; }
        if (popup instanceof HTMLDialogElement && popup.open) popup.close();
    };

    const topmost = () => { const list = shownPopups(); return list.length ? list[list.length - 1] : null; };
    document.addEventListener("click", (event) => {
        const icon = event.target instanceof Element ? event.target.closest("[data-popup-close-icon]") : null;
        if (!icon) return;
        event.preventDefault();
        event.stopPropagation();
        const popup = topmost();
        if (popup) closePopup(popup);
    }, true);

    // The close button that sits in the head of the popup (the title row) is hidden: its place is the bar, and the icon triggers it. One placed elsewhere (the foot of a form) stays.
    const headingSelector = ".catalog-heading,.dialog-head,.heading-actions,.map-panel-heading,.help-header,.help-header-actions,header";
    const inHead = (button, popup) => {
        if (button.closest(headingSelector)) return true;
        const top = panelOf(popup).getBoundingClientRect().top;
        return button.getBoundingClientRect().top - top < 70 && !button.closest("form,.editor-actions");
    };
    const moveHeadingClose = () => {
        const wanted = new Set();
        for (const popup of shownPopups()) { const button = closeButtonOf(popup); if (button && inHead(button, popup)) wanted.add(button); }
        for (const button of document.querySelectorAll(".popup-close-moved")) if (!wanted.has(button)) button.classList.remove("popup-close-moved");
        for (const button of wanted) button.classList.add("popup-close-moved");
    };

    let lastTop = null;
    let anchor = null;
    // The bar is anchored to the popup: it follows it when the popup (or the page under it) is scrolled, it is not held at the top of the screen. Above the panel when there is room for it,
    // in the top corner of the panel when there is none (a popup that starts at the top of the window); out of sight with the panel when that scrolls away.
    const reposition = () => {
        if (!anchor || floating.hidden || !anchor.isConnected) return;
        const box = panelOf(anchor).getBoundingClientRect();
        const width = floating.offsetWidth || 200;
        const height = floating.offsetHeight || 32;
        const top = box.top >= height + 12 ? box.top - height - 8 : box.top + 6;
        floating.style.top = top + "px";
        floating.style.left = Math.max(4, Math.min(innerWidth - width - 4, box.right - width - (box.top >= height + 12 ? 0 : 6))) + "px";
        floating.style.visibility = top + height < 0 || top > innerHeight ? "hidden" : "visible";
    };
    const place = () => {
        moveHeadingClose();
        const available = !!document.getElementById("bug-dialog");
        const proposalsAvailable = !!document.getElementById("proposal-dialog");
        const popup = topmost();
        if (!floating.isConnected) document.body.appendChild(floating);
        if (!popup) {
            floating.hidden = true;
            if (hasPopover && floating.matches(":popover-open")) floating.hidePopover();
            lastTop = null;
            return;
        }
        const isReport = popup.matches(reportPopups) || !!popup.closest(reportPopups);
        // Outside a modal <dialog> everything is inert, so over a <dialog> the bar is a child of that dialog (a popover is not clipped by its parent, it is drawn in the top layer, outside the box).
        const host = popup instanceof HTMLDialogElement ? popup : (popup.closest("dialog[open]") || document.body);
        if (floating.parentElement !== host) {
            if (hasPopover && floating.matches(":popover-open")) floating.hidePopover();
            host.appendChild(floating);
        }
        floating.hidden = false;
        // Shown again when another popup became the topmost one, so the bar is above it in the top layer.
        if (hasPopover && (popup !== lastTop || !floating.matches(":popover-open"))) {
            if (floating.matches(":popover-open")) floating.hidePopover();
            floating.showPopover();
        }
        lastTop = popup;
        floating.querySelector("[data-bug-popup]").hidden = !available || isReport;
        floating.querySelector("[data-proposal-popup]").hidden = !proposalsAvailable || isReport;
        floating.querySelector("[data-popup-close-icon]").hidden = !closeButtonOf(popup) && !(popup instanceof HTMLDialogElement);
        anchor = popup;
        reposition();
    };
    let scheduled = false;
    const schedule = () => { if (scheduled) return; scheduled = true; const run = () => { if (!scheduled) return; scheduled = false; place(); }; requestAnimationFrame(run); setTimeout(run, 60); };
    new MutationObserver((records) => { if (records.some((record) => record.target !== floating && !(record.target instanceof Element && record.target.closest("[data-popup-bar]")))) schedule(); }).observe(document.documentElement, { subtree: true, childList: true, attributes: true, attributeFilter: ["open", "class", "style", "disabled", "hidden"] });
    window.addEventListener("resize", schedule);
    // At once, in the same frame as the scroll, so the bar does not lag behind the popup.
    document.addEventListener("scroll", () => reposition(), true);
    document.addEventListener("DOMContentLoaded", schedule);

    // The menu entries and the buttons of the bar: "Raportează o eroare" (data-bug-open) and "Propune" (data-proposal-open). The page is photographed BEFORE the popup shows.
    const kinds = {
        bug: { attribute: "[data-bug-open]", dialog: "bug-dialog", reference: () => state.ref },
        proposal: { attribute: "[data-proposal-open]", dialog: "proposal-dialog", reference: () => state.proposalRef }
    };
    document.addEventListener("click", async (event) => {
        const origin = event.target instanceof Element ? event.target : null;
        const kind = origin && origin.closest(kinds.proposal.attribute) ? kinds.proposal : origin && origin.closest(kinds.bug.attribute) ? kinds.bug : null;
        if (!kind) return;
        const target = origin.closest(kind.attribute);
        event.preventDefault();
        const dialog = document.getElementById(kind.dialog);
        if (!(dialog instanceof HTMLDialogElement) || dialog.open || target.dataset.busy === "1") return;
        target.dataset.busy = "1";
        try {
            const shot = await capturePage();
            const info = environment();
            const reference = kind.reference();
            if (reference) {
                try {
                    await reference.invokeMethodAsync("Opened", shot ? DotNet.createJSStreamReference(shot) : null, info.url, info.title, JSON.stringify(info, null, 2));
                } catch (error) { remember(`Fereastra nu a raspuns: ${error}`); }
            }
            if (!dialog.open) dialog.showModal();
        } finally { delete target.dataset.busy; }
    });

    document.addEventListener("paste", async (event) => {
        const kind = [kinds.bug, kinds.proposal].find((item) => { const dialog = document.getElementById(item.dialog); return dialog instanceof HTMLDialogElement && dialog.open && item.reference(); });
        if (!kind) return;
        const items = Array.from(event.clipboardData ? event.clipboardData.items : []).filter((item) => item.kind === "file" && item.type.startsWith("image/"));
        if (items.length === 0) return;
        event.preventDefault();
        for (const item of items) {
            const file = item.getAsFile();
            if (!file) continue;
            const blob = await shrink(file);
            const extension = blob.type === "image/png" ? ".png" : ".jpg";
            await kind.reference().invokeMethodAsync("AddPasted", DotNet.createJSStreamReference(blob), `lipit-${Date.now()}${extension}`);
        }
    });

    // Escape inside an enlarged picture closes the picture only, not the popup that holds it.
    document.addEventListener("cancel", (event) => {
        const dialog = event.target;
        if (!(dialog instanceof HTMLDialogElement)) return;
        const lightbox = dialog.querySelector(".image-lightbox [data-popup-close]");
        if (lightbox) { event.preventDefault(); lightbox.click(); }
    }, true);

    window.bugReport = {
        register: (reference) => { state.ref = reference; },
        unregister: () => { state.ref = null; },
        close: () => { const dialog = document.getElementById("bug-dialog"); if (dialog instanceof HTMLDialogElement && dialog.open) dialog.close(); },
        // A proposal opened by its id (from the tab "Propuneri" of Notificari): no capture of the page, the popup shows the proposal.
        openProposal: async (id) => {
            const dialog = document.getElementById("proposal-dialog");
            if (!(dialog instanceof HTMLDialogElement) || !state.proposalRef) return;
            try { await state.proposalRef.invokeMethodAsync("OpenById", id); } catch (error) { remember(`Propunerea nu s-a deschis: ${error}`); }
            if (!dialog.open) dialog.showModal();
        },
        registerProposal: (reference) => { state.proposalRef = reference; },
        unregisterProposal: () => { state.proposalRef = null; },
        closeProposal: () => { const dialog = document.getElementById("proposal-dialog"); if (dialog instanceof HTMLDialogElement && dialog.open) dialog.close(); }
    };
})();
