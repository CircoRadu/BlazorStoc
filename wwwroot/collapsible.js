(() => {
    const selector = "details[data-collapsible-section]";

    function synchronize(section) {
        const summary = section.querySelector(":scope > summary");
        const expanded = section.open ? "true" : "false";
        section.dataset.expanded = expanded;
        summary?.setAttribute("aria-expanded", expanded);
    }

    function synchronizeAll() {
        document.querySelectorAll(selector).forEach(synchronize);
    }

    document.addEventListener("toggle", event => {
        if (event.target instanceof HTMLDetailsElement && event.target.matches(selector)) {
            synchronize(event.target);
            if (event.isTrusted) {
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
