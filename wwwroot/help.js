// In-application help (docs/PROPUNERE_HELP.md). The icon of the top bar ([data-help-open]) opens the help of the window the user is in; a button
// [data-help-topic="key"] (the icon in the head of a dialog, a link inside the help) opens a topic by its key. The text comes ready as HTML from
// /ajutor/... (Startup/HelpEndpoints.cs) and goes into a native modal dialog: it stays above every other window, Escape closes it, and the window
// underneath is left untouched. Delegated listeners, so it works in the pages rendered without interactivity, in the pop-out windows and on the
// pages outside Blazor. From the main page ("/") the help window has a search over all the topics; elsewhere it is limited to the active window.
(() => {
    const dialogId = "help-dialog";
    let history = [];          // the addresses shown in the dialog, for "Înapoi"
    let searchTimer = 0;
    let searchToken = 0;

    function ensureDialog() {
        let dialog = document.getElementById(dialogId);
        if (dialog instanceof HTMLDialogElement) return dialog;
        dialog = document.createElement("dialog");
        dialog.id = dialogId;
        dialog.className = "delete-dialog help-dialog";
        dialog.setAttribute("aria-labelledby", "help-dialog-title");
        dialog.innerHTML =
            '<header class="help-header"><h2 id="help-dialog-title">Ajutor</h2><span class="help-header-actions">' +
            '<button type="button" class="refresh act-nav" data-help-back hidden>Înapoi</button>' +
            '<button type="button" class="refresh act-close" data-help-close>Închide</button></span></header>' +
            '<div class="help-search" hidden><input type="search" class="help-search-input" placeholder="Caută în tot ajutorul" aria-label="Caută în tot ajutorul" autocomplete="off" />' +
            '<div class="help-search-results" aria-live="polite"></div></div>' +
            '<div class="help-body" tabindex="-1"></div>';
        document.body.appendChild(dialog);
        return dialog;
    }

    // The labels of the tabs selected in the window (the outer one first): the topic of a tab is chosen by its label.
    function selectedTabs() {
        const tabs = [];
        // Blazor writes a true boolean attribute without a value (aria-selected=""), so "selected" is: present and not "false".
        document.querySelectorAll('[role="tab"][aria-selected]').forEach((tab) => {
            if (!(tab instanceof HTMLElement) || tab.getAttribute("aria-selected") === "false" || tab.closest("[hidden]") || tab.closest("#" + dialogId)) return;
            const label = (tab.textContent || "").trim();
            if (label) tabs.push(label);
        });
        return tabs;
    }

    async function load(url, dialog) {
        const body = dialog.querySelector(".help-body");
        body.textContent = "Se încarcă…";
        try {
            const response = await fetch(url, { credentials: "same-origin" });
            if (!response.ok) throw new Error(String(response.status));
            body.innerHTML = await response.text();
            const title = body.querySelector(".help-title");
            dialog.querySelector("#help-dialog-title").textContent = title ? "Ajutor · " + title.textContent : "Ajutor";
            body.scrollTop = 0;
        } catch (error) {
            body.textContent = "Ajutorul nu a putut fi încărcat. Încearcă din nou.";
        }
        dialog.querySelector("[data-help-back]").hidden = history.length < 2;
    }

    function show(url, withSearch) {
        const dialog = ensureDialog();
        if (!dialog.open) {
            history = [];
            dialog.querySelector(".help-search-input").value = "";
            dialog.querySelector(".help-search-results").innerHTML = "";
            dialog.querySelector(".help-search").hidden = !withSearch;
            dialog.showModal();
        }
        history.push(url);
        return load(url, dialog);
    }

    function openForWindow() {
        const params = new URLSearchParams();
        params.set("ruta", location.pathname);
        selectedTabs().forEach((label) => params.append("tab", label));
        return show("/ajutor/fereastra?" + params.toString(), location.pathname === "/");
    }

    async function runSearch(dialog) {
        const input = dialog.querySelector(".help-search-input");
        const results = dialog.querySelector(".help-search-results");
        const query = input.value.trim();
        const token = ++searchToken;
        if (query.length < 2) { results.innerHTML = ""; return; }
        try {
            const response = await fetch("/ajutor/cautare?q=" + encodeURIComponent(query), { credentials: "same-origin" });
            if (token !== searchToken) return;
            results.innerHTML = response.ok ? await response.text() : "";
        } catch (error) { if (token === searchToken) results.innerHTML = ""; }
    }

    document.addEventListener("click", (event) => {
        const target = event.target instanceof Element ? event.target : null;
        if (!target) return;
        const dialog = document.getElementById(dialogId);

        if (target.closest("[data-help-open]")) { event.preventDefault(); openForWindow(); return; }

        const topic = target.closest("[data-help-topic]");
        if (topic instanceof HTMLElement) {
            event.preventDefault();
            const key = topic.getAttribute("data-help-topic");
            if (key) show("/ajutor/subiect/" + encodeURIComponent(key), false);
            return;
        }

        if (!(dialog instanceof HTMLDialogElement) || !dialog.open) return;
        if (target.closest("[data-help-close]") || target === dialog) { dialog.close(); return; }
        if (target.closest("[data-help-back]") && history.length > 1) {
            history.pop();
            const previous = history.pop();
            show(previous, false);
        }
    });

    document.addEventListener("input", (event) => {
        const target = event.target instanceof Element ? event.target : null;
        if (!target || !target.classList.contains("help-search-input")) return;
        const dialog = document.getElementById(dialogId);
        if (!(dialog instanceof HTMLDialogElement)) return;
        window.clearTimeout(searchTimer);
        searchTimer = window.setTimeout(() => runSearch(dialog), 250);
    });
})();
