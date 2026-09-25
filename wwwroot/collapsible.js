(() => {
    const selector = "details[data-collapsible-section]";
    const intentLifetimeMilliseconds = 1000;
    // The section whose header the user just pressed (click, Enter or Space). Only such a toggle may navigate: the
    // layout also opens a category programmatically when a subcategory link changes the address, and that must not
    // send the user to the category page and drop the chosen subcategory.
    let userIntent = null;

    function synchronize(section) {
        const summary = section.querySelector(":scope > summary");
        const expanded = section.open ? "true" : "false";
        section.dataset.expanded = expanded;
        summary?.setAttribute("aria-expanded", expanded);
    }

    function synchronizeAll() {
        document.querySelectorAll(selector).forEach(synchronize);
    }

    document.addEventListener("click", event => {
        const summary = event.target instanceof Element ? event.target.closest(`${selector} > summary`) : null;
        if (!summary) return;
        const intent = { section: summary.parentElement, at: performance.now() };
        userIntent = intent;
        // A handler that cancels the click (for example the leave guard) means no toggle follows.
        setTimeout(() => { if (event.defaultPrevented && userIntent === intent) userIntent = null; }, 0);
    }, true);

    function isUserToggle(section) {
        const fresh = userIntent && userIntent.section === section &&
            performance.now() - userIntent.at < intentLifetimeMilliseconds;
        userIntent = null;
        return Boolean(fresh);
    }

    document.addEventListener("toggle", event => {
        if (event.target instanceof HTMLDetailsElement && event.target.matches(selector)) {
            synchronize(event.target);
            if (event.isTrusted && isUserToggle(event.target)) {
                const destination = event.target.open
                    ? event.target.dataset.collapsibleOpenHref
                    : event.target.dataset.collapsibleCloseHref;
                if (destination) window.location.assign(destination);
            }
        }
    }, true);
    document.addEventListener("DOMContentLoaded", synchronizeAll);
    document.addEventListener("enhancedload", synchronizeAll);
})();
