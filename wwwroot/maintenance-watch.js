// Live notification of a backup/restore for every open page (no SignalR client library is shipped, so the page asks
// a tiny endpoint every few seconds). While an operation runs, a page that is not the one driving it is moved to the
// waiting page, and the waiting page brings the user back as soon as the operation ends. The pages that DRIVE an
// operation (preluare inventar; backup si restaurare din Setari, marcate cu data-operation-driver) stay where they are.
(function () {
    "use strict";
    const waitingPath = "/intretinere";
    const drivingPaths = ["/inventar/preluare"];
    const visibleInterval = 2500;
    const hiddenInterval = 10000;
    const banner = "maintenance-banner";

    // The page can change without a reload (Blazor's enhanced navigation swaps the content and keeps this script), so
    // the location is read on every use, never once at load.
    const currentPath = () => location.pathname.toLowerCase();
    const onWaitingPage = () => currentPath().startsWith(waitingPath);
    // A page can also mark itself as the driver of the running operation (Settings -> Backup NAS while "Fă backup acum" runs): data-operation-driver.
    const drivesOperation = () => drivingPaths.some(p => currentPath().startsWith(p)) || document.querySelector("[data-operation-driver]") !== null;

    function returnTarget() {
        const wait = document.getElementById("maintenance-wait");
        const raw = (wait && wait.dataset.returnUrl) || "/";
        return raw.startsWith("/") && !raw.startsWith("//") && !raw.startsWith("/\\") ? raw : "/";
    }

    function showBanner(visible) {
        let element = document.getElementById(banner);
        if (!visible) { if (element && element.dataset.live === "1") element.remove(); return; }
        if (element || onWaitingPage() || currentPath().startsWith("/setari")) return;
        const workspace = document.querySelector("main.workspace");
        if (!workspace) return;
        element = document.createElement("div");
        element.id = banner;
        element.dataset.live = "1";
        element.className = "error-banner";
        element.setAttribute("role", "alert");
        element.textContent = "Backup/restaurare în curs - datele pot fi temporar indisponibile. Reveniți mai târziu.";
        workspace.parentNode.insertBefore(element, workspace);
    }

    async function poll() {
        if (currentPath().startsWith("/account")) return true; // sign-in pages: nothing to watch
        let active = null;
        try {
            const response = await fetch("/api/maintenance", { credentials: "same-origin", cache: "no-store", redirect: "manual" });
            if (response.status === 401 || response.status === 403 || response.type === "opaqueredirect") return false; // not signed in
            if (response.ok) active = (await response.json()).active === true;
        } catch { /* a network blip: try again on the next tick */ }

        if (active === true) {
            if (!onWaitingPage() && !drivesOperation()) {
                location.replace(waitingPath + "?returnUrl=" + encodeURIComponent(location.pathname + location.search));
                return true;
            }
            showBanner(true);
        } else if (active === false) {
            if (onWaitingPage()) { location.replace(returnTarget()); return true; }
            showBanner(false);
        }
        return true;
    }

    async function loop() {
        while (await poll()) {
            await new Promise(resolve => setTimeout(resolve, document.hidden ? hiddenInterval : visibleInterval));
        }
    }
    loop();
})();
