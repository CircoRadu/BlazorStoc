// Editor of the announcements (Setari -> Anunturi): a contenteditable box written for the application, no external library. The toolbar (AnnouncementsPanel.razor) calls
// exec/block/color/link/insertImage; a picture pasted or dropped is sent to the component as a JS stream, which stores it and answers with its address. Pasted text loses its foreign
// formatting (plain text). Whatever the box holds is cleaned again on the server before it is saved (AnnouncementHtml), so this script is a convenience, not the protection.
(function () {
    const boxes = new Map();

    function box(id) { return document.getElementById(id); }

    async function sendImage(state, file) {
        if (!file || !file.type.startsWith("image/")) return;
        const extension = file.type === "image/png" ? ".png" : file.type === "image/gif" ? ".gif" : file.type === "image/webp" ? ".webp" : ".jpg";
        const url = await state.reference.invokeMethodAsync("OnPastedImage", DotNet.createJSStreamReference(file), "lipit-" + Date.now() + extension);
        if (url) insertImage(state.id, url);
    }

    function init(id, reference, html) {
        const element = box(id);
        if (!element) return;
        dispose(id);
        element.innerHTML = html || "";
        const state = { id, reference };
        state.paste = async (event) => {
            const data = event.clipboardData;
            if (!data) return;
            const files = Array.from(data.items || []).filter((item) => item.kind === "file" && item.type.startsWith("image/")).map((item) => item.getAsFile());
            event.preventDefault();
            if (files.length > 0) { for (const file of files) await sendImage(state, file); return; }
            const text = data.getData("text/plain");
            if (text) document.execCommand("insertText", false, text);
        };
        state.drop = async (event) => {
            const files = Array.from(event.dataTransfer ? event.dataTransfer.files : []).filter((file) => file.type.startsWith("image/"));
            if (files.length === 0) return;
            event.preventDefault();
            for (const file of files) await sendImage(state, file);
        };
        state.input = () => reference.invokeMethodAsync("OnEdited");
        element.addEventListener("paste", state.paste);
        element.addEventListener("drop", state.drop);
        element.addEventListener("input", state.input);
        boxes.set(id, state);
        try { document.execCommand("styleWithCSS", false, true); document.execCommand("defaultParagraphSeparator", false, "p"); } catch { }
    }

    function dispose(id) {
        const state = boxes.get(id);
        const element = box(id);
        if (state && element) {
            element.removeEventListener("paste", state.paste);
            element.removeEventListener("drop", state.drop);
            element.removeEventListener("input", state.input);
        }
        boxes.delete(id);
    }

    function focus(id) { const element = box(id); if (element && document.activeElement !== element) element.focus(); }

    function exec(id, command, value) {
        focus(id);
        document.execCommand(command, false, value === undefined ? null : value);
        const state = boxes.get(id);
        if (state) state.reference.invokeMethodAsync("OnEdited");
    }

    function link(id, address) {
        focus(id);
        if (!address) { document.execCommand("unlink"); return; }
        document.execCommand("createLink", false, address);
        const state = boxes.get(id);
        if (state) state.reference.invokeMethodAsync("OnEdited");
    }

    function insertImage(id, url) {
        focus(id);
        document.execCommand("insertHTML", false, '<img src="' + url + '" alt="">');
        const state = boxes.get(id);
        if (state) state.reference.invokeMethodAsync("OnEdited");
    }

    function getHtml(id) { const element = box(id); return element ? element.innerHTML : ""; }
    function setHtml(id, html) { const element = box(id); if (element) element.innerHTML = html || ""; }

    // The pictures in the window of the announcements: small, with an enlarge icon; a click on the icon or on the picture asks the open window to show it large.
    let zoomRef = null;
    function watchZoom(reference) { zoomRef = reference; }
    function unwatchZoom(reference) { if (zoomRef === reference) zoomRef = null; }
    document.addEventListener("click", (event) => {
        if (!zoomRef || !(event.target instanceof Element)) return;
        const thumb = event.target.closest(".announcement-body .announcement-thumb");
        if (!thumb) return;
        const picture = thumb.querySelector("img");
        if (!picture) return;
        event.preventDefault();
        zoomRef.invokeMethodAsync("OpenZoom", picture.getAttribute("src"), picture.getAttribute("alt") || "");
    });

    window.announcementEditor = { init, dispose, exec, link, insertImage, getHtml, setHtml, focus, watchZoom, unwatchZoom };
})();
