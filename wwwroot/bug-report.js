// Raportare eroare: the menu entry above "Ajutor" ([data-bug-open]) photographs the page BEFORE the popup (#bug-dialog) shows, then opens it; Ctrl+V adds a picture from the
// clipboard. The picture of the page is made from the DOM (the page is serialised into an SVG image and drawn on a canvas), so the browser asks for no permission. Parts the
// DOM cannot carry (a map, a frame) come out incomplete; the user can paste a screenshot instead. Pictures go to the Blazor component as JS stream references (a picture is
// larger than a SignalR message).
(() => {
    const state = { ref: null, errors: [] };
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
    const openPopups = () => Array.from(document.querySelectorAll(popupSelector)).filter((item) => item.id !== "bug-dialog" && !item.closest("#bug-dialog") && !item.hasAttribute("data-no-bug") && isShown(item));
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
                if (source instanceof HTMLDialogElement && source.open && source.id !== "bug-dialog") { target.style.zIndex = "2147483000"; hasPopup = true; }
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
            clone.querySelectorAll("script,noscript,dialog:not([open]),#bug-dialog,iframe,#blazor-error-ui").forEach((node) => node.remove());
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

    // Every popup (a native <dialog> or a div/section with role=dialog) gets a small bar: "Raportează o eroare" and a close icon. A <dialog> lives in the top layer, so its bar sits inside
    // it; the other popups share ONE floating bar (outside the Blazor tree) placed above the topmost popup. Module out (no #bug-dialog): no report button at all.
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
        const close = document.createElement("button");
        close.type = "button";
        close.className = "popup-close-icon";
        close.setAttribute("data-popup-close-icon", "");
        close.setAttribute("aria-label", "Închide fereastra");
        close.title = "Închide fereastra";
        close.textContent = "✕";
        bar.append(report, close);
        return bar;
    };
    const floating = makeBar();
    floating.id = "bug-popup-float";
    floating.classList.add("bug-popup-float");
    floating.hidden = true;

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
    // Priority: marked with data-popup-close, the .act-close buttons, then the safe words ("Închide", "Anulează", "Nu, păstrează", "Înapoi", "Renunță").
    const closeWords = [/^închide/i, /^anulează/i, /^nu[, ]/i, /^înapoi/i, /^renunță/i];
    const closeButtonOf = (popup) => {
        const buttons = Array.from(popup.querySelectorAll("button")).filter((item) => !item.closest("[data-popup-bar]") && !item.disabled && (isShown(item) || item.classList.contains("popup-close-moved")));
        const text = (item) => (item.textContent || "").trim().replace(/^[^\p{L}]+/u, "");
        return buttons.find((item) => item.hasAttribute("data-popup-close"))
            || buttons.find((item) => item.classList.contains("act-close") && !item.classList.contains("row-icon"))
            || closeWords.map((word) => buttons.find((item) => word.test(text(item)))).find(Boolean)
            || null;
    };
    const closePopup = (popup) => {
        const button = closeButtonOf(popup);
        if (button) { button.click(); return; }
        if (popup instanceof HTMLDialogElement && popup.open) popup.close();
    };

    const topmost = () => { const list = openPopups(); return list.length ? list[list.length - 1] : null; };
    document.addEventListener("click", (event) => {
        const icon = event.target instanceof Element ? event.target.closest("[data-popup-close-icon]") : null;
        if (!icon) return;
        event.preventDefault();
        event.stopPropagation();
        const own = icon.closest("dialog");
        const popup = own ? (own.open ? own : null) : topmost();
        if (popup) closePopup(popup);
    }, true);

    // The close button of a popup lives in the bar above the popup (next to "Raportează o eroare"): a close button that sits in the heading is hidden, and the icon of the bar triggers it.
    // One placed elsewhere in the popup (the foot of a form) stays, and the icon is added in the bar as well.
    const headingCloseOf = (popup) => {
        const button = closeButtonOf(popup);
        return button && button.closest(".catalog-heading,.dialog-head,.heading-actions,.map-panel-heading") && !button.closest("[data-popup-bar]") ? button : null;
    };
    const moveHeadingClose = () => {
        const wanted = new Set(openPopups().map(headingCloseOf).filter(Boolean));
        for (const button of document.querySelectorAll(".popup-close-moved")) if (!wanted.has(button)) button.classList.remove("popup-close-moved");
        for (const button of wanted) if (!button.classList.contains("popup-close-moved")) button.classList.add("popup-close-moved");
    };

    const place = () => {
        moveHeadingClose();
        const available = !!document.getElementById("bug-dialog");
        if (!floating.isConnected) document.body.appendChild(floating);
        const bugDialog = document.getElementById("bug-dialog");
        // Native dialogs carry a bar inside.
        for (const dialog of document.querySelectorAll("dialog[open]")) {
            let bar = dialog.querySelector(":scope [data-popup-bar]");
            if (!bar) { bar = makeBar(); (dialog.querySelector(".dialog-head") || dialog).appendChild(bar); }
            // The report popup keeps only the close icon (it is the report itself).
            bar.querySelector("[data-bug-popup]").hidden = !available || dialog.id === "bug-dialog" || dialog.hasAttribute("data-no-bug");
            bar.querySelector("[data-popup-close-icon]").hidden = !closeButtonOf(dialog) && !(dialog instanceof HTMLDialogElement);
        }
        const popups = !bugDialog?.open ? openPopups().filter((item) => !(item instanceof HTMLDialogElement)) : [];
        if (popups.length === 0) { floating.hidden = true; return; }
        const popup = popups[popups.length - 1];
        const box = panelOf(popup).getBoundingClientRect();
        floating.hidden = false;
        floating.querySelector("[data-bug-popup]").hidden = !available;
        floating.querySelector("[data-popup-close-icon]").hidden = !closeButtonOf(popup);
        const width = floating.offsetWidth || 200;
        floating.style.top = Math.max(4, box.top - 36) + "px";
        floating.style.left = Math.max(4, Math.min(innerWidth - width - 4, box.right - width)) + "px";
    };
    let scheduled = false;
    const schedule = () => { if (scheduled) return; scheduled = true; const run = () => { if (!scheduled) return; scheduled = false; place(); }; requestAnimationFrame(run); setTimeout(run, 60); };
    new MutationObserver((records) => { if (records.some((record) => record.target !== floating && !(record.target instanceof Element && record.target.closest("[data-popup-bar]")))) schedule(); }).observe(document.documentElement, { subtree: true, childList: true, attributes: true, attributeFilter: ["open", "class", "style", "disabled", "hidden"] });
    window.addEventListener("resize", schedule);
    document.addEventListener("scroll", schedule, true);
    document.addEventListener("DOMContentLoaded", schedule);

    document.addEventListener("click", async (event) => {
        const target = event.target instanceof Element ? event.target.closest("[data-bug-open]") : null;
        if (!target) return;
        event.preventDefault();
        const dialog = document.getElementById("bug-dialog");
        if (!(dialog instanceof HTMLDialogElement) || dialog.open || target.dataset.busy === "1") return;
        target.dataset.busy = "1";
        try {
            const shot = await capturePage();
            const info = environment();
            if (state.ref) {
                try {
                    await state.ref.invokeMethodAsync("Opened", shot ? DotNet.createJSStreamReference(shot) : null, info.url, info.title, JSON.stringify(info, null, 2));
                } catch (error) { remember(`Fereastra de raportare nu a raspuns: ${error}`); }
            }
            if (!dialog.open) dialog.showModal();
        } finally { delete target.dataset.busy; }
    });

    document.addEventListener("paste", async (event) => {
        const dialog = document.getElementById("bug-dialog");
        if (!(dialog instanceof HTMLDialogElement) || !dialog.open || !state.ref) return;
        const items = Array.from(event.clipboardData ? event.clipboardData.items : []).filter((item) => item.kind === "file" && item.type.startsWith("image/"));
        if (items.length === 0) return;
        event.preventDefault();
        for (const item of items) {
            const file = item.getAsFile();
            if (!file) continue;
            const blob = await shrink(file);
            const extension = blob.type === "image/png" ? ".png" : ".jpg";
            await state.ref.invokeMethodAsync("AddPasted", DotNet.createJSStreamReference(blob), `lipit-${Date.now()}${extension}`);
        }
    });

    window.bugReport = {
        register: (reference) => { state.ref = reference; },
        unregister: () => { state.ref = null; },
        close: () => { const dialog = document.getElementById("bug-dialog"); if (dialog instanceof HTMLDialogElement && dialog.open) dialog.close(); }
    };
})();
