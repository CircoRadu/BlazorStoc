// Date fields marked data-pick-only accept a value only from the calendar picker: typing, pasting and
// dropping text are blocked. Tab, Shift+Tab and Escape keep working, and Enter, Space, Alt+Down or F4
// (and a click anywhere on the field) open the calendar, so the field stays usable without a mouse.
(() => {
    const selector = "input[data-pick-only]";
    const field = (event) => event.target instanceof Element ? event.target.closest(selector) : null;

    const openPicker = (input) => {
        if (input.disabled || input.readOnly) return;
        try { input.showPicker(); } catch { /* already open, or the browser refuses without a user gesture */ }
    };

    document.addEventListener("keydown", (event) => {
        const input = field(event);
        if (!input) return;
        if (event.key === "Tab" || event.key === "Escape" || event.ctrlKey && event.key.toLowerCase() === "r") return;
        event.preventDefault();
        if (event.key === "Enter" || event.key === " " || event.key === "F4" || event.altKey && event.key === "ArrowDown") openPicker(input);
    }, true);

    document.addEventListener("click", (event) => {
        const input = field(event);
        if (input) openPicker(input);
    }, true);

    for (const type of ["paste", "drop", "beforeinput"]) {
        document.addEventListener(type, (event) => { if (field(event)) event.preventDefault(); }, true);
    }
})();
