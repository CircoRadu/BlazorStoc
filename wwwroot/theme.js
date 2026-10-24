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
    new MutationObserver(() => { enforce(); reflect(); }).observe(root, { attributes: true, attributeFilter: ["data-theme"] });
    // The top bar is drawn again by Blazor (navigation, reconnection): the state of the button is written back.
    new MutationObserver(reflect).observe(document.body, { childList: true, subtree: true });
    document.addEventListener("DOMContentLoaded", () => { enforce(); reflect(); });
    if (window.Blazor && Blazor.addEventListener) Blazor.addEventListener("enhancedload", () => { enforce(); reflect(); });
    enforce();
    reflect();
})();
