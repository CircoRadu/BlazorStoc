const pasteHandlers = new WeakMap();

function placeFile(inputId, file) {
    const input = document.getElementById(inputId);
    if (!input) return false;
    const transfer = new DataTransfer();
    transfer.items.add(file);
    input.files = transfer.files;
    input.dispatchEvent(new Event("change", { bubbles: true }));
    return true;
}

export function attachPaste(target, inputId) {
    const previous = pasteHandlers.get(target);
    if (previous) document.removeEventListener("paste", previous);
    // Ascultam pe document: Ctrl+V functioneaza oriunde in fereastra, nu doar cu zona selectata.
    const handler = event => {
        const item = Array.from(event.clipboardData?.items ?? []).find(entry => entry.type.startsWith("image/"));
        if (!item) return;
        const file = item.getAsFile();
        if (!file) return;
        event.preventDefault();
        placeFile(inputId, new File([file], file.name || "imagine-clipboard.png", { type: file.type }));
    };
    document.addEventListener("paste", handler);
    pasteHandlers.set(target, handler);
}

export function detachPaste(target) {
    const handler = pasteHandlers.get(target);
    if (handler) document.removeEventListener("paste", handler);
    pasteHandlers.delete(target);
}
