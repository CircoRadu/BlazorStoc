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

    const environment = () => ({
        url: location.pathname + location.search,
        title: document.title,
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
            // Every element carries its computed style, so the picture does not depend on re-running the selectors, the viewport units or the fixed positions of the page.
            clone.setAttribute("style", computedText(body));
            for (let i = 0; i < sources.length; i++) {
                const source = sources[i], target = targets[i];
                if (!(source instanceof SVGElement) || source instanceof SVGSVGElement) target.setAttribute("style", computedText(source));
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
            clone.querySelectorAll("script,noscript,dialog,iframe,#blazor-error-ui").forEach((node) => node.remove());
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
