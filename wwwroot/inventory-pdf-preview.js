// Preview of the generated situatia de inventar inside an <iframe> of the page's own dialog (an in-page dialog rather
// than window.open: a popup opened after an async call is blocked by browsers).
let activeUrl = null;

export async function showPdf(iframe, contentStreamReference) {
    const arrayBuffer = await contentStreamReference.arrayBuffer();
    const url = URL.createObjectURL(new Blob([arrayBuffer], { type: "application/pdf" }));
    if (activeUrl) URL.revokeObjectURL(activeUrl);
    activeUrl = url;
    iframe.src = url;
}

// Saves the previewed PDF from the copy already held by the browser: the server keeps no copy after showPdf.
export function savePdf(fileName) {
    if (!activeUrl) return false;
    const anchor = document.createElement("a");
    anchor.href = activeUrl;
    anchor.download = fileName ?? "";
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
    return true;
}

// Prints the previewed PDF through the browser's own PDF viewer; falls back to a separate tab when the frame's
// content cannot be printed from the page (some browsers keep the viewer's print out of reach of the parent).
export function printPdf(iframe) {
    try {
        iframe.contentWindow.focus();
        iframe.contentWindow.print();
        return true;
    } catch { /* fall through to the separate tab */ }
    if (!activeUrl) return false;
    const tab = window.open(activeUrl, "_blank");
    if (!tab) return false;
    tab.addEventListener("load", () => { try { tab.print(); } catch { /* the user can print from the viewer */ } }, { once: true });
    return true;
}

export function release(iframe) {
    if (iframe) iframe.src = "about:blank";
    if (activeUrl) { URL.revokeObjectURL(activeUrl); activeUrl = null; }
}
