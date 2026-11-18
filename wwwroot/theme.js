// Light / dark theme. The choice of the user is kept in this browser (localStorage "blazorstoc-theme") and applied as data-theme on <html> before the page is painted
// (the small script in the head of App.razor); the toggle in the top bar ([data-theme-toggle]) switches it. The default is the light theme.
// Blazor's navigation between pages (enhanced navigation) patches the document and drops attributes of <html> that the next page does not carry, so the theme is
// guarded: whenever data-theme changes behind our back, the chosen theme is written back.
(() => {
    const key = "blazorstoc-theme";
    const root = document.documentElement;
    let chosen = stored() ?? (root.getAttribute("data-theme") === "dark" ? "dark" : "light");

    function stored() {
        try { const value = localStorage.getItem(key); return value === "dark" || value === "light" ? value : null; } catch (error) { return null; }
    }

    function current() { return root.getAttribute("data-theme") === "dark" ? "dark" : "light"; }

    function enforce() {
        if (chosen === "dark" && root.getAttribute("data-theme") !== "dark") root.setAttribute("data-theme", "dark");
        else if (chosen === "light" && root.hasAttribute("data-theme")) root.removeAttribute("data-theme");
    }

    function reflect() {
        const dark = current() === "dark";
        document.querySelectorAll("[data-theme-toggle]").forEach((button) => {
            button.setAttribute("aria-pressed", dark ? "true" : "false");
            const label = dark ? "Treci la tema luminoasă" : "Treci la tema întunecată";
            if (button.getAttribute("aria-label") !== label) { button.setAttribute("aria-label", label); button.setAttribute("title", label); }
        });
    }

    // Colour palette of the light theme (Setari afisare -> Culori): the account keeps it (the shell carries data-palette), this browser caches it so the first paint is right.
    const paletteKey = "blazorstoc-palette";
    let palette = storedPalette();
    function storedPalette() { try { return localStorage.getItem(paletteKey) || ""; } catch (error) { return ""; } }
    function enforcePalette() {
        if (palette) { if (root.getAttribute("data-palette") !== palette) root.setAttribute("data-palette", palette); }
        else if (root.hasAttribute("data-palette")) root.removeAttribute("data-palette");
    }
    function setPalette(id) {
        palette = id || "";
        try { if (palette) localStorage.setItem(paletteKey, palette); else localStorage.removeItem(paletteKey); } catch (error) { /* storage blocked */ }
        enforcePalette();
        // The shell of the page carries the palette rendered by the server (data-palette): left as it is, it keeps the old colours inside the application until the next page load.
        const shell = document.querySelector(".app-shell");
        if (shell && (shell.getAttribute("data-palette") || "") !== palette) shell.setAttribute("data-palette", palette);
    }
    function adoptPaletteOfAccount() {
        const shell = document.querySelector(".app-shell");
        if (shell && (shell.getAttribute("data-palette") || "") !== palette) setPalette(shell.getAttribute("data-palette") || "");
    }
    window.themePalette = { set: setPalette };

    function apply(theme) {
        chosen = theme;
        try { localStorage.setItem(key, theme); } catch (error) { /* storage blocked: the choice lasts until the page is closed */ }
        enforce();
        reflect();
    }

    document.addEventListener("click", (event) => {
        if (event.target instanceof Element && event.target.closest("[data-theme-toggle]")) { event.preventDefault(); apply(chosen === "dark" ? "light" : "dark"); }
    });
    // The attribute is written back at once when a navigation removes it.
    new MutationObserver(() => { enforce(); enforcePalette(); reflect(); }).observe(root, { attributes: true, attributeFilter: ["data-theme", "data-palette"] });
    // The top bar is drawn again by Blazor (navigation, reconnection): the state of the button is written back.
    new MutationObserver(reflect).observe(document.body, { childList: true, subtree: true });
    document.addEventListener("DOMContentLoaded", () => { enforce(); adoptPaletteOfAccount(); enforcePalette(); reflect(); });
    if (window.Blazor && Blazor.addEventListener) Blazor.addEventListener("enhancedload", () => { enforce(); adoptPaletteOfAccount(); enforcePalette(); reflect(); });
    enforce();
    enforcePalette();
    reflect();
})();
