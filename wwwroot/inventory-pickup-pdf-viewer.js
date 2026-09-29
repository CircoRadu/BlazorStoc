let activeObjectUrl = null;
let activeWindow = null;

export async function openPdfViewer(contentStreamReference, fileName) {
    const arrayBuffer = await contentStreamReference.arrayBuffer();
    const blob = new Blob([arrayBuffer], { type: "application/pdf" });
    const url = URL.createObjectURL(blob);

    const width = Math.max(480, Math.round((window.screen.availWidth || 1024) * 0.45));
    const height = Math.max(600, Math.round((window.screen.availHeight || 768) * 0.92));
    const left = Math.max(0, (window.screen.availWidth || width) - width - 16);
    const top = 16;
    const features = `popup=yes,resizable=yes,scrollbars=yes,width=${width},height=${height},left=${left},top=${top}`;

    const target = activeWindow && !activeWindow.closed ? activeWindow : window.open("", "blazorStocPickupPdfViewer", features);
    if (!target) {
        URL.revokeObjectURL(url);
        return false;
    }

    target.location.href = url;
    try { target.focus(); } catch { /* ignore focus failures (e.g. blocked by browser policy) */ }

    const previousUrl = activeObjectUrl;
    activeObjectUrl = url;
    activeWindow = target;
    if (previousUrl) URL.revokeObjectURL(previousUrl);

    target.addEventListener("beforeunload", () => {
        if (activeObjectUrl === url) {
            URL.revokeObjectURL(url);
            activeObjectUrl = null;
        }
        if (activeWindow === target) activeWindow = null;
    }, { once: true });

    return true;
}

export function closePdfViewer() {
    if (activeWindow && !activeWindow.closed) activeWindow.close();
    if (activeObjectUrl) { URL.revokeObjectURL(activeObjectUrl); activeObjectUrl = null; }
    activeWindow = null;
}
