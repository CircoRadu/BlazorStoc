// In-application help (docs/PROPUNERE_HELP.md). The icon of the top bar ([data-help-open]) opens the help of the window the user is in; a button
// [data-help-topic="key"] (the icon in the head of a dialog, a link inside the help) opens a topic by its key. The text comes ready as HTML from
// /ajutor/... (Startup/HelpEndpoints.cs) and goes into a native modal dialog: it stays above every other window, Escape closes it, and the window
// underneath is left untouched. Delegated listeners, so it works in the pages rendered without interactivity, in the pop-out windows and on the
// pages outside Blazor. A topic with screenshots (docs/PROPUNERE_HELP_IMAGINI.md): a row of the table of elements shows a pulsing frame around the element on
// the picture (only while the row is chosen). The icon "Arată elementul pe imagine" of a row does the same as a click on the row: the element is always shown on the picture of the topic, never on the live page. From the main page ("/") the help window has a search over all the topics; elsewhere it is limited to the active window.
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
        return show("/ajutor/fereastra?" + params.toString(), true);
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

    // ---- Screenshots: the frame of the chosen row -------------------------------------------------------------------------------------------------------------
    function clearPick(dialog) {
        dialog.querySelectorAll(".help-frame").forEach((frame) => frame.remove());
        dialog.querySelectorAll(".help-row.help-selected").forEach((row) => row.classList.remove("help-selected"));
        dialog.querySelectorAll(".help-shot.help-shot-active").forEach((shot) => shot.classList.remove("help-shot-active"));
    }

    function showFrames(dialog, row) {
        const shot = dialog.querySelector('.help-shot[data-shot="' + CSS.escape(row.getAttribute("data-shot") || "") + '"]');
        if (!shot) return;
        let rects = {};
        try { rects = JSON.parse(shot.getAttribute("data-rects") || "{}"); } catch (error) { rects = {}; }
        const boxes = rects[row.getAttribute("data-n") || ""];
        const width = Number(shot.getAttribute("data-w")) || 1920;
        const height = Number(shot.getAttribute("data-h")) || 1080;
        const inner = shot.querySelector(".help-shot-inner");
        if (!inner || !Array.isArray(boxes) || boxes.length === 0) return;
        const pad = 4;
        boxes.forEach((box) => {
            const frame = document.createElement("div");
            frame.className = "help-frame";
            frame.setAttribute("aria-hidden", "true");
            const left = Math.max(0, box[0] - pad), top = Math.max(0, box[1] - pad);
            const right = Math.min(width, box[0] + box[2] + pad), bottom = Math.min(height, box[1] + box[3] + pad);
            frame.style.left = (left / width * 100) + "%";
            frame.style.top = (top / height * 100) + "%";
            frame.style.width = ((right - left) / width * 100) + "%";
            frame.style.height = ((bottom - top) / height * 100) + "%";
            inner.appendChild(frame);
        });
        shot.classList.add("help-shot-active");
        row.classList.add("help-selected");
        scrollToFrame(shot);
        // The picture sticks under the header while the table scrolls: the chosen row must not stay behind it.
        const header = dialog.querySelector(".help-header");
        const stuckBottom = (header ? header.getBoundingClientRect().bottom : 0) + shot.offsetHeight;
        const gap = row.getBoundingClientRect().top - stuckBottom - 8;
        if (gap < 0) dialog.scrollBy({ top: gap, behavior: "smooth" });
    }

    // The first frame goes to the middle of the scrolling area of the picture.
    function scrollToFrame(shot) {
        const frame = shot.querySelector(".help-frame");
        const area = shot.querySelector(".help-shot-scroll");
        if (!frame || !area) return;
        const target = frame.offsetTop + frame.offsetHeight / 2;
        area.scrollTo({ top: Math.max(0, target - area.clientHeight / 2), left: Math.max(0, frame.offsetLeft + frame.offsetWidth / 2 - area.clientWidth / 2), behavior: "smooth" });
    }

    function togglePick(dialog, row) {
        const same = row.classList.contains("help-selected");
        clearPick(dialog);
        if (!same) showFrames(dialog, row);
    }

    function toggleZoom(button) {
        const shot = button.closest(".help-shot");
        if (!shot) return;
        const zoomed = !shot.classList.contains("zoomed");
        shot.classList.toggle("zoomed", zoomed);
        shot.style.setProperty("--shot-width", (shot.getAttribute("data-w") || "1920") + "px");
        button.setAttribute("aria-pressed", String(zoomed));
        button.textContent = zoomed ? "Micșorează imaginea" : "Mărește imaginea";
        scrollToFrame(shot);
    }

    document.addEventListener("click", (event) => {
        const target = event.target instanceof Element ? event.target : null;
        if (!target) return;
        const dialog = document.getElementById(dialogId);

        if (target.closest("[data-help-open]")) { event.preventDefault(); openForWindow(); return; }
        if (target.closest("[data-help-index]")) { event.preventDefault(); show("/ajutor/cuprins", true); return; }

        const topic = target.closest("[data-help-topic]");
        if (topic instanceof HTMLElement) {
            event.preventDefault();
            const key = topic.getAttribute("data-help-topic");
            if (key) show("/ajutor/subiect/" + encodeURIComponent(key), false);
            return;
        }

        if (!(dialog instanceof HTMLDialogElement) || !dialog.open) return;
        if (target.closest("[data-help-close]") || target === dialog) { dialog.close(); return; }
        const zoom = target.closest("[data-help-zoom]");
        if (zoom) { toggleZoom(zoom); return; }
        const pick = target.closest("[data-help-pick]");
        if (pick instanceof HTMLElement) { togglePick(dialog, pick); return; }
        if (target.closest("[data-help-back]") && history.length > 1) {
            history.pop();
            const previous = history.pop();
            show(previous, false);
        }
    });

    document.addEventListener("keydown", (event) => {
        const target = event.target instanceof Element ? event.target : null;
        if ((event.key !== "Enter" && event.key !== " ") || !target || !target.matches("[data-help-pick]")) return;
        const dialog = document.getElementById(dialogId);
        if (!(dialog instanceof HTMLDialogElement)) return;
        event.preventDefault();
        togglePick(dialog, target);
    });

    // Escape first takes the frame away (the row is no longer chosen), and only then closes the window.
    document.addEventListener("cancel", (event) => {
        const dialog = event.target;
        if (!(dialog instanceof HTMLDialogElement) || dialog.id !== dialogId || !dialog.querySelector(".help-row.help-selected")) return;
        event.preventDefault();
        clearPick(dialog);
    }, true);

    document.addEventListener("input", (event) => {
        const target = event.target instanceof Element ? event.target : null;
        if (!target || !target.classList.contains("help-search-input")) return;
        const dialog = document.getElementById(dialogId);
        if (!(dialog instanceof HTMLDialogElement)) return;
        window.clearTimeout(searchTimer);
        searchTimer = window.setTimeout(() => runSearch(dialog), 250);
    });
})();
