// Marking zones on a picture (Components/Shared/ImageAnnotator.razor), used by the form of a bug report and by the form of a proposal (PROP-0009).
// The picture is shown large in its own floating window (inside the popup, so the description of the popup stays reachable); the user drags rectangles on it, they are numbered 1..N
// in the order they were drawn, can only be deleted (the button outside the top-right corner) and the numbers follow by themselves. After every change the picture is drawn again from
// the ORIGINAL with the zones on it (JPEG) and handed to the component; the original and the zones live here, per picture, until the form is left. Pointer events only, no observers.
window.imageAnnotate = (() => {
    const MAX_ZONES = 10, MIN_SIZE = 12, OUT_WIDTH = 3200;
    const states = new Map();      // picture id -> { source: data URL of the original, zones: [{ x, y, w, h }] as fractions of the picture }
    const editors = new Map();     // root element -> { id, ref, maxBytes, image, layer, stage, cleanup }

    const load = (source) => new Promise((resolve, reject) => { const image = new Image(); image.onload = () => resolve(image); image.onerror = reject; image.src = source; });
    const clamp01 = (value) => Math.min(1, Math.max(0, value));

    // The zones of a picture drawn on the original: red frame, light fill, the number in a red square at the top-left corner inside the zone.
    const render = async (state, maxBytes) => {
        if (state.zones.length === 0) return await (await fetch(state.source)).blob();
        const image = await load(state.source);
        let scale = Math.min(1, OUT_WIDTH / image.naturalWidth);
        for (const quality of [0.88, 0.75, 0.6]) {
            const canvas = document.createElement("canvas");
            canvas.width = Math.round(image.naturalWidth * scale);
            canvas.height = Math.round(image.naturalHeight * scale);
            const context = canvas.getContext("2d");
            context.fillStyle = "#fff";
            context.fillRect(0, 0, canvas.width, canvas.height);
            context.drawImage(image, 0, 0, canvas.width, canvas.height);
            const line = Math.max(2, canvas.width / 350), badge = Math.max(18, canvas.width / 45);
            state.zones.forEach((zone, index) => {
                const x = zone.x * canvas.width, y = zone.y * canvas.height, w = zone.w * canvas.width, h = zone.h * canvas.height;
                context.fillStyle = "rgba(220, 38, 38, 0.12)";
                context.fillRect(x, y, w, h);
                context.strokeStyle = "#dc2626";
                context.lineWidth = line;
                context.strokeRect(x, y, w, h);
                context.fillStyle = "#dc2626";
                context.fillRect(x, y, badge, badge);
                context.fillStyle = "#fff";
                context.font = `bold ${Math.round(badge * 0.72)}px sans-serif`;
                context.textAlign = "center";
                context.textBaseline = "middle";
                context.fillText(String(index + 1), x + badge / 2, y + badge / 2 + badge * 0.04);
            });
            const blob = await new Promise((resolve) => canvas.toBlob(resolve, "image/jpeg", quality));
            if (blob && blob.size <= maxBytes) return blob;
            scale *= 0.8;
        }
        return await new Promise((resolve) => { const canvas = document.createElement("canvas"); canvas.width = image.naturalWidth; canvas.height = image.naturalHeight; canvas.getContext("2d").drawImage(image, 0, 0); canvas.toBlob(resolve, "image/jpeg", 0.6); });
    };

    const drawLayer = (editor, state) => {
        editor.layer.replaceChildren();
        state.zones.forEach((zone, index) => {
            const box = document.createElement("div");
            box.className = "annotator-zone";
            box.style.left = `${zone.x * 100}%`; box.style.top = `${zone.y * 100}%`; box.style.width = `${zone.w * 100}%`; box.style.height = `${zone.h * 100}%`;
            const number = document.createElement("span");
            number.className = "annotator-number";
            number.textContent = String(index + 1);
            const remove = document.createElement("button");
            remove.type = "button";
            remove.className = "annotator-remove";
            remove.dataset.zone = String(index);
            remove.setAttribute("aria-label", `Șterge zona ${index + 1}`);
            remove.title = `Șterge zona ${index + 1}`;
            remove.textContent = "×";
            box.append(number, remove);
            editor.layer.append(box);
        });
        const counter = editor.root.querySelector("[data-annotator-count]");
        if (counter) counter.textContent = `${state.zones.length} / ${MAX_ZONES} zone`;
        const last = editor.root.querySelector("[data-annotator-undo]");
        if (last) last.disabled = state.zones.length === 0;
        const note = editor.root.querySelector("[data-annotator-limit]");
        if (note) note.hidden = state.zones.length < MAX_ZONES;
    };

    const apply = async (editor, state) => {
        try {
            const blob = await render(state, editor.maxBytes);
            await editor.ref.invokeMethodAsync("Applied", DotNet.createJSStreamReference(blob), blob.type || "image/jpeg");
        } catch (error) { console.warn("Image marking failed", error); }
    };

    const open = async (root, ref, id, source, maxBytes) => {
        close(root);
        const state = states.get(id) || { source, zones: [] };
        states.set(id, state);
        const image = root.querySelector("[data-annotator-image]"), layer = root.querySelector("[data-annotator-layer]"), stage = root.querySelector("[data-annotator-stage]");
        const editor = { root, id, ref, maxBytes, image, layer, stage };
        editors.set(root, editor);
        image.src = state.source;
        drawLayer(editor, state);

        let drag = null;
        const point = (event) => {
            const box = image.getBoundingClientRect();
            return { x: clamp01((event.clientX - box.left) / box.width), y: clamp01((event.clientY - box.top) / box.height), box };
        };
        const down = (event) => {
            if (event.button !== 0 || event.target.closest(".annotator-remove") || state.zones.length >= MAX_ZONES) return;
            const start = point(event);
            if (event.clientX < start.box.left || event.clientX > start.box.right || event.clientY < start.box.top || event.clientY > start.box.bottom) return;
            event.preventDefault();
            const preview = document.createElement("div");
            preview.className = "annotator-zone annotator-preview";
            layer.append(preview);
            drag = { start, preview };
            try { stage.setPointerCapture(event.pointerId); } catch { /* the pointer is gone */ }
        };
        const move = (event) => {
            if (!drag) return;
            const now = point(event);
            const x = Math.min(drag.start.x, now.x), y = Math.min(drag.start.y, now.y), w = Math.abs(now.x - drag.start.x), h = Math.abs(now.y - drag.start.y);
            Object.assign(drag.preview.style, { left: `${x * 100}%`, top: `${y * 100}%`, width: `${w * 100}%`, height: `${h * 100}%` });
            drag.rect = { x, y, w, h, box: now.box };
        };
        const up = async (event) => {
            if (!drag) return;
            const { preview, rect } = drag;
            drag = null;
            preview.remove();
            try { stage.releasePointerCapture(event.pointerId); } catch { /* released */ }
            if (!rect || rect.w * rect.box.width < MIN_SIZE || rect.h * rect.box.height < MIN_SIZE || state.zones.length >= MAX_ZONES) return;
            state.zones.push({ x: rect.x, y: rect.y, w: rect.w, h: rect.h });
            drawLayer(editor, state);
            await apply(editor, state);
        };
        const click = async (event) => {
            const button = event.target instanceof Element ? event.target.closest(".annotator-remove") : null;
            if (button) {
                state.zones.splice(Number.parseInt(button.dataset.zone, 10), 1);
                drawLayer(editor, state);
                await apply(editor, state);
                return;
            }
            if (event.target instanceof Element && event.target.closest("[data-annotator-undo]") && state.zones.length > 0) {
                state.zones.pop();
                drawLayer(editor, state);
                await apply(editor, state);
            }
        };
        const keys = async (event) => {
            if (event.key === "Delete" && state.zones.length > 0 && !(event.target instanceof Element && event.target.closest("input, textarea, select, [contenteditable]"))) {
                state.zones.pop();
                drawLayer(editor, state);
                await apply(editor, state);
            }
        };
        stage.addEventListener("pointerdown", down);
        stage.addEventListener("pointermove", move);
        stage.addEventListener("pointerup", up);
        stage.addEventListener("pointercancel", up);
        root.addEventListener("click", click);
        root.addEventListener("keydown", keys);
        editor.cleanup = () => {
            stage.removeEventListener("pointerdown", down); stage.removeEventListener("pointermove", move); stage.removeEventListener("pointerup", up); stage.removeEventListener("pointercancel", up);
            root.removeEventListener("click", click); root.removeEventListener("keydown", keys);
        };
        wireWindow(root);
    };

    // The window moves by its head and changes size by the corner handle (plain pointer events; it is not a frame of AppDialog).
    const wireWindow = (root) => {
        if (root.dataset.wired) return;
        root.dataset.wired = "1";
        const track = (event, onMove) => {
            const target = event.target, id = event.pointerId, startX = event.clientX, startY = event.clientY;
            const move = (e) => { if (e.pointerId === id) onMove(e.clientX - startX, e.clientY - startY); };
            const end = (e) => {
                if (e.pointerId !== id) return;
                target.removeEventListener("pointermove", move); target.removeEventListener("pointerup", end); target.removeEventListener("pointercancel", end);
                try { target.releasePointerCapture(id); } catch { /* released */ }
            };
            try { target.setPointerCapture(id); } catch { /* gone */ }
            target.addEventListener("pointermove", move); target.addEventListener("pointerup", end); target.addEventListener("pointercancel", end);
        };
        root.addEventListener("pointerdown", (event) => {
            if (event.button !== 0 || !(event.target instanceof Element) || window.matchMedia("(max-width: 719px)").matches) return;
            const rect = root.getBoundingClientRect();
            if (event.target.closest("[data-annotator-resize]")) {
                event.preventDefault();
                track(event, (dx, dy) => {
                    root.style.width = `${Math.min(window.innerWidth - rect.left - 8, Math.max(320, rect.width + dx))}px`;
                    root.style.height = `${Math.min(window.innerHeight - rect.top - 8, Math.max(240, rect.height + dy))}px`;
                });
            } else if (event.target.closest("[data-annotator-head]") && !event.target.closest("button")) {
                event.preventDefault();
                track(event, (dx, dy) => {
                    root.style.left = `${Math.min(window.innerWidth - 80, Math.max(80 - rect.width, rect.left + dx))}px`;
                    root.style.top = `${Math.min(window.innerHeight - 40, Math.max(0, rect.top + dy))}px`;
                });
            }
        });
    };

    const close = (root) => {
        const editor = editors.get(root);
        if (!editor) return;
        if (editor.cleanup) editor.cleanup();
        editors.delete(root);
    };
    const forget = (id) => states.delete(id);
    const reset = () => states.clear();
    return { open, close, forget, reset, MAX_ZONES };
})();
