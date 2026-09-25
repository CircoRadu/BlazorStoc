(() => {
    // Remembers the scroll position of a list page when one of its project links is followed, and restores it once
    // when the list is shown again at the same address (TODO Task 2 / Subtask 2.3). Best effort: storage may be unavailable.
    const prefix = "blazorstoc.scroll:";
    const maxAgeMilliseconds = 30 * 60 * 1000;

    function key() {
        return prefix + location.pathname + location.search;
    }

    function attempt(action) {
        try { return action(); } catch { return null; }
    }

    document.addEventListener("click", event => {
        if (event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
        const link = event.target instanceof Element ? event.target.closest("a[data-restore-scroll]") : null;
        if (!link) return;
        attempt(() => sessionStorage.setItem(key(), JSON.stringify({ y: window.scrollY, at: Date.now() })));
    }, true);

    window.blazorStocNavigationContext = {
        restoreScroll() {
            const raw = attempt(() => sessionStorage.getItem(key()));
            if (!raw) return false;
            attempt(() => sessionStorage.removeItem(key()));
            const saved = attempt(() => JSON.parse(raw));
            if (!saved || typeof saved.y !== "number" || Date.now() - saved.at > maxAgeMilliseconds) return false;
            window.scrollTo(0, saved.y);
            return true;
        }
    };
})();
