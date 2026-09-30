// Keeps the red warning of the "Notificări" menu entry up to date while a page stays open. The layout is rendered on the
// server at every navigation (it shows the current state then); between navigations this script asks a tiny endpoint
// for the number of notifications that warn now, so a session left open still sees new ones (and one taken over by
// another user stops warning).
(function () {
    "use strict";
    const visibleInterval = 60000;
    const hiddenInterval = 180000;

    function apply(count) {
        const nav = document.querySelector("[data-notification-nav]");
        if (!nav) return;
        const warn = count > 0;
        nav.classList.toggle("nav-alert", warn);
        const triangle = nav.querySelector("[data-notification-triangle]");
        if (triangle) triangle.hidden = !warn;
    }

    async function poll() {
        if (location.pathname.toLowerCase().startsWith("/account")) return true;
        try {
            const response = await fetch("/api/notifications/alerts", { credentials: "same-origin", cache: "no-store", redirect: "manual" });
            if (response.status === 401 || response.status === 403 || response.type === "opaqueredirect") return false;
            if (response.ok) {
                const data = await response.json();
                if (typeof data.count === "number" && data.count >= 0) apply(data.count);
            }
        } catch { /* a network blip: try again on the next tick */ }
        return true;
    }

    async function loop() {
        while (true) {
            await new Promise(resolve => setTimeout(resolve, document.hidden ? hiddenInterval : visibleInterval));
            if (!(await poll())) return;
        }
    }

    // Called by the notifications page right after a notification was taken over or postponed.
    window.refreshNotificationAlerts = poll;

    loop();
})();
