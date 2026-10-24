// Light / dark theme. The choice of the user is kept in this browser (localStorage "blazorstoc-theme") and applied as data-theme on <html> before the page is painted
// (the small script in the head of App.razor); the toggle in the top bar ([data-theme-toggle]) switches it. The default is the light theme.
(() => {
    const key = "blazorstoc-theme";
    const root = document.documentElement;

    function current() { return root.getAttribute("data-theme") === "dark" ? "dark" : "light"; }

    function reflect() {
        const dark = current() === "dark";
        document.querySelectorAll("[data-theme-toggle]").forEach((button) => {
            button.setAttribute("aria-pressed", dark ? "true" : "false");
            const label = dark ? "Treci la tema luminoasă" : "Treci la tema întunecată";
            button.setAttribute("aria-label", label);
            button.setAttribute("title", label);
        });
    }

    function apply(theme) {
        if (theme === "dark") root.setAttribute("data-theme", "dark"); else root.removeAttribute("data-theme");
        try { localStorage.setItem(key, theme); } catch (error) { /* storage blocked: the choice lasts until the page is closed */ }
        reflect();
    }

    document.addEventListener("click", (event) => {
        if (event.target instanceof Element && event.target.closest("[data-theme-toggle]")) { event.preventDefault(); apply(current() === "dark" ? "light" : "dark"); }
    });
    // The top bar is drawn again by Blazor (navigation, reconnection): the state of the button is written back.
    new MutationObserver(reflect).observe(document.body, { childList: true, subtree: true });
    reflect();
})();
