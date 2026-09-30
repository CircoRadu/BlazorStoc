// Settings → Hartă → Motor hartă: loads one tile in the browser, like the map does, and reports whether the provider answered with an image.
export function testTile(url, timeoutMs) {
    return new Promise(resolve => {
        const started = performance.now();
        const image = new Image();
        let done = false;
        const finish = ok => {
            if (done) return;
            done = true;
            clearTimeout(timer);
            resolve({ ok, milliseconds: Math.round(performance.now() - started) });
        };
        const timer = setTimeout(() => finish(false), timeoutMs);
        image.onload = () => finish(image.naturalWidth > 0);
        image.onerror = () => finish(false);
        image.referrerPolicy = "strict-origin-when-cross-origin";
        image.src = url;
    });
}
