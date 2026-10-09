(() => {
    // MarkedTextField (Components/Shared/MarkedTextField.razor): the coloured backdrop scrolls with its input, and a label is inserted at the
    // cursor. The cursor is remembered while the field is used, because a click on a label button takes the focus (and in some browsers the
    // selection) away before the insertion. Delegated on document (nothing to initialise when a component re-renders).
    const selections = new WeakMap();

    function fieldOf(target) {
        if (!(target instanceof Element) || !target.matches("input, textarea")) return null;
        return target.closest("[data-mark-field]") ? target : null;
    }

    function sync(element) {
        const backdrop = element.closest("[data-mark-field]")?.querySelector(".mark-backdrop");
        if (!backdrop) return;
        backdrop.scrollTop = element.scrollTop;
        backdrop.scrollLeft = element.scrollLeft;
    }

    function remember(element) {
        try { selections.set(element, [element.selectionStart, element.selectionEnd]); } catch (error) { /* a field type without selection */ }
    }

    ["select", "keyup", "click", "input", "blur", "focus", "mouseup"].forEach(name => document.addEventListener(name, event => {
        const element = fieldOf(event.target);
        if (!element) return;
        remember(element);
        requestAnimationFrame(() => sync(element));
    }, true));
    document.addEventListener("scroll", event => {
        const element = fieldOf(event.target);
        if (element) sync(element);
    }, true);

    // The Delete key with the cursor right before a label removes the whole label (<...>), and, in a field that takes operations, a whole
    // ADUNARE{...}.
    document.addEventListener("keydown", event => {
        if (event.key !== "Delete" || event.ctrlKey || event.altKey || event.metaKey || event.shiftKey) return;
        const element = fieldOf(event.target);
        if (!element || element.selectionStart !== element.selectionEnd) return;
        const start = element.selectionStart;
        const operations = element.closest("[data-mark-field]")?.getAttribute("data-mark-ops") === "true";
        const found = (operations ? /^(<[^<>\r\n]{1,80}>|(?:ADUNARE|SCADERE|INMULTIRE|IMPARTIRE)\{[^{}]*\})/ : /^<[^<>\r\n]{1,80}>/).exec(element.value.slice(start));
        if (!found) return;
        event.preventDefault();
        element.setRangeText("", start, start + found[0].length, "start");
        remember(element);
        element.dispatchEvent(new Event("input", { bubbles: true }));
    }, true);

    // Backspace with the cursor right after a label removes the whole label (and, in a field that takes operations, a whole ADUNARE{...}).
    document.addEventListener("keydown", event => {
        if (event.key !== "Backspace" || event.ctrlKey || event.altKey || event.metaKey || event.shiftKey) return;
        const element = fieldOf(event.target);
        if (!element || element.selectionStart !== element.selectionEnd) return;
        const end = element.selectionStart;
        const operations = element.closest("[data-mark-field]")?.getAttribute("data-mark-ops") === "true";
        const found = (operations ? /(<[^<>\r\n]{1,80}>|(?:ADUNARE|SCADERE|INMULTIRE|IMPARTIRE)\{[^{}]*\})$/ : /<[^<>\r\n]{1,80}>$/).exec(element.value.slice(0, end));
        if (!found) return;
        event.preventDefault();
        element.setRangeText("", end - found[0].length, end, "end");
        remember(element);
        element.dispatchEvent(new Event("input", { bubbles: true }));
    }, true);

    window.blazorStocMarkField = {
        // Inserts the text where the cursor was (replacing the selection); without a remembered cursor the end of the field. caretBack leaves the
        // cursor that many characters before the end of the inserted text (inside "ADUNARE{}").
        insert(id, text, caretBack = 0, join = false) {
            const element = document.getElementById(id);
            if (!element || !fieldOf(element)) return false;
            const end = element.value.length;
            let [start, stop] = selections.get(element) ?? [end, end];
            start = Math.min(start, end);
            stop = Math.min(Math.max(stop, start), end);
            // Description fields (join): a label or an operation gets a space before and after it (none where a space already is); inside an
            // arithmetic operation there are no spaces, a label after another label gets the symbol of the operation. The ends of the description
            // are trimmed where it is shown (preview) and saved.
            let lead = "", trail = "";
            if (join) {
                const before = element.value.slice(0, start);
                const after = element.value.slice(stop);
                const open = /(ADUNARE|SCADERE|INMULTIRE|IMPARTIRE)\{([^{}]*)$/.exec(before);
                if (open) {
                    if (/^<[^<>\r\n]{1,80}>$/.test(text) && />\s*$/.test(open[2])) text = { ADUNARE: "+", SCADERE: "-", INMULTIRE: "*", IMPARTIRE: "/" }[open[1]] + text;
                } else {
                    if (!/\s$/.test(before)) lead = " ";
                    if (!/^\s/.test(after)) trail = " ";
                }
            }
            const inserted = lead + text + trail;
            if (element.maxLength > 0 && end - (stop - start) + inserted.length > element.maxLength) return false;
            element.focus();
            element.setRangeText(inserted, start, stop, "end");
            // The cursor goes after the label and its space (where the typing goes on), or inside the braces of an operation.
            const caret = caretBack > 0 ? start + lead.length + text.length - Math.min(caretBack, text.length) : start + inserted.length;
            const value = element.value;
            remember(element);
            // Blazor reads the new value from the input event.
            element.dispatchEvent(new Event("input", { bubbles: true }));
            // The cursor goes right after the label and stays there when the component is drawn again (the server answers a moment later and
            // a re-render may move the cursor): it is put back while the field still holds the text that was inserted and nothing was typed.
            const place = () => {
                if (document.activeElement !== element || element.value !== value) return;
                element.setSelectionRange(caret, caret);
                remember(element);
                sync(element);
            };
            place();
            [0, 60, 200, 500].forEach(delay => setTimeout(place, delay));
            return true;
        }
    };
})();
