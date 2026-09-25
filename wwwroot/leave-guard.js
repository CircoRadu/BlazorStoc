// Leave guard for edit forms (Task 1). While an editor is open the page enables it (UnsavedChangesHost). A click on a
// link, a product-menu header or "Deconectare" is stopped and reported to the page, which either lets the action
// continue (nothing was modified) or asks the user to confirm leaving first. The server decides, never this script,
// because a typed value reaches the server only when the field loses focus and that message always arrives before
// the click that follows. The browser's Back/Forward buttons are handled here too (the page router does not report
// them to NavigationLock): the move is undone at once and repeated only after the page allowed it. Programmatic
// navigation is handled by NavigationLock on the page; closing the tab uses the browser's own confirmation.
(() => {
    let page = null;
    let owner = null;
    let remembered = null;
    // The router does not always number history entries, so every entry created in this document is tagged with an
    // identifier of its own, and the order of the entries is kept here: that gives the distance of a Back/Forward move.
    const session = Math.random().toString(36).slice(2);
    let counter = 0;
    let stack = [];
    let cursor = -1;
    let undoing = false;
    let allowing = false;

    const tag = (state) => {
        const id = `${session}:${++counter}`;
        return [id, { ...(state && typeof state === "object" ? state : {}), __leaveGuard: id }];
    };
    const originalPush = history.pushState.bind(history);
    const originalReplace = history.replaceState.bind(history);
    history.pushState = function (state, title, url) {
        const [id, tagged] = tag(state);
        originalPush(tagged, title, url);
        stack = stack.slice(0, cursor + 1);
        stack.push(id);
        cursor = stack.length - 1;
    };
    history.replaceState = function (state, title, url) {
        const [id, tagged] = tag(state);
        originalReplace(tagged, title, url);
        if (cursor < 0) { stack = [id]; cursor = 0; } else stack[cursor] = id;
    };
    // The entry the document was loaded on (or reloaded on) is tagged too.
    (() => {
        const existing = history.state && history.state.__leaveGuard;
        if (existing) { stack = [existing]; cursor = 0; }
        else history.replaceState(history.state, "");
    })();

    window.addEventListener("popstate", (event) => {
        const id = event.state && event.state.__leaveGuard;
        const position = id ? stack.indexOf(id) : -1;
        if (undoing) { undoing = false; if (position >= 0) cursor = position; stop(event); return; }
        if (allowing) { allowing = false; if (position >= 0) cursor = position; return; }
        if (position < 0) { if (id) { stack = [id]; cursor = 0; } return; }
        const moved = position - cursor;
        if (!page || moved === 0) { cursor = position; return; }
        stop(event);
        undoing = true;
        history.go(-moved);
        ask("RequestHistory", moved).then((proceed) => { if (proceed) window.blazorStocLeaveGuard.goHistory(moved); })
            .catch(() => window.blazorStocLeaveGuard.goHistory(moved));
    }, true);

    const stop = (event) => {
        event.preventDefault();
        event.stopPropagation();
        event.stopImmediatePropagation();
    };

    // Server endpoints that are not application pages leave the editor alone (files, images, sign-in).
    const isPage = (url) => url.origin === location.origin && !/^\/(media|Account|hubs|health|_)/i.test(url.pathname);

    // If the page cannot answer (connection lost), the action simply goes ahead as if the guard were off.
    const ask = (method, ...args) => page.invokeMethodAsync(method, ...args);

    document.addEventListener("click", (event) => {
        if (!page || event.defaultPrevented || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
        const target = event.target instanceof Element ? event.target : null;
        if (!target) return;

        if (target.closest("[data-logout-open]")) {
            stop(event);
            ask("RequestLogout").catch(() => document.getElementById("logout-dialog")?.showModal?.());
            return;
        }

        const link = target.closest("a[href]");
        if (link) {
            const targetName = link.getAttribute("target");
            if (link.hasAttribute("download") || (targetName && targetName !== "_self")) return;
            const href = link.getAttribute("href");
            if (!href || href.startsWith("#")) return;
            const url = new URL(href, location.href);
            if (!isPage(url)) return;
            stop(event);
            ask("RequestLeave", url.pathname + url.search + url.hash, false).catch(() => location.assign(url.href));
            return;
        }

        const summary = target.closest(".sidebar .product-category-menu > details[data-collapsible-section] > summary");
        if (summary) {
            const details = summary.parentElement;
            const destination = details.open ? details.dataset.collapsibleCloseHref : details.dataset.collapsibleOpenHref;
            if (destination) {
                stop(event);
                ask("RequestLeave", destination, true).catch(() => location.assign(destination));
            }
        }
    }, true);

    // Answered by the browser only while it is connected: the product edit lock is renewed only when this call
    // succeeds, so a closed tab lets the lease expire even while the server keeps the circuit for a few minutes.
    window.blazorStocPing = () => true;

    window.blazorStocLeaveGuard = {
        // Each page has its own host; a page that is being replaced must not switch off the guard of the new page.
        enable(reference, id) { page = reference; owner = id; },
        disable(id) { if (owner === id) { page = null; owner = null; } },
        // The dialog moves the focus; the element that had it is restored when the user goes back to editing.
        rememberFocus() { remembered = document.activeElement instanceof HTMLElement ? document.activeElement : null; },
        restoreFocus() {
            const element = remembered;
            remembered = null;
            if (element && element.isConnected) element.focus();
        },
        // Repeats a Back/Forward move that was undone while the question was open.
        goHistory(delta) { allowing = true; history.go(delta); },
        // Opens the sign-out dialog once the leave question was answered (the same as pressing "Deconectare").
        openLogout() {
            const dialog = document.getElementById("logout-dialog");
            if (dialog instanceof HTMLDialogElement && !dialog.open) dialog.showModal();
        }
    };
})();
