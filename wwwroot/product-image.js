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
    if (previous) target.removeEventListener("paste", previous);
    const handler = event => {
        const item = Array.from(event.clipboardData?.items ?? []).find(entry => entry.type.startsWith("image/"));
        if (!item) return;
        const file = item.getAsFile();
        if (!file) return;
        event.preventDefault();
        placeFile(inputId, new File([file], file.name || "imagine-clipboard.png", { type: file.type }));
    };
    target.addEventListener("paste", handler);
    pasteHandlers.set(target, handler);
}

export async function readClipboard(inputId) {
    if (!navigator.clipboard?.read) throw new Error("Clipboard API indisponibil");
    const items = await navigator.clipboard.read();
    for (const item of items) {
        const type = item.types.find(value => value.startsWith("image/"));
        if (!type) continue;
        const blob = await item.getType(type);
        return placeFile(inputId, new File([blob], "imagine-clipboard." + (type.split("/")[1] || "png"), { type }));
    }
    return false;
}

export function detachPaste(target) {
    const handler = pasteHandlers.get(target);
    if (handler) target.removeEventListener("paste", handler);
    pasteHandlers.delete(target);
}
