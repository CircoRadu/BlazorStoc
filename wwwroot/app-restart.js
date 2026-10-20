// Setari -> Baza de date -> Conexiune -> Aplicatia: after the application was asked to restart (for example to listen on another port), the page waits until the application
// answers on the address it will have and then opens it. On the same address the old process still answers for a moment, so the first answer counts only after the
// address was seen down; on another port any answer is the new process.
export function waitAndOpen(baseUrl, path) {
    const target = new URL(path || "/", baseUrl).toString();
    const sameOrigin = new URL(baseUrl, location.href).origin === location.origin;
    let seenDown = !sameOrigin;
    const started = Date.now();
    const timer = setInterval(async () => {
        if (Date.now() - started > 180000) { clearInterval(timer); return; }
        try {
            await fetch(new URL("/health/live", baseUrl).toString(), { mode: "no-cors", cache: "no-store" });
            if (seenDown) { clearInterval(timer); location.href = target; }
        }
        catch { seenDown = true; }
    }, 1500);
}
