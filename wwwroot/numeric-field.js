(() => {
    // Fields that hold a number read from an invoice (quantity, prices, taxes: <input data-numeric>) accept only digits and one comma.
    // Delegated on document (nothing to initialise when the component re-renders). A typed or pasted "." becomes ","; every other character
    // is refused, and so is a second comma. The text already in the field (as the invoice was read) is not rewritten here.
    const clean = (text, current, start, end) => {
        const rest = current.slice(0, start) + current.slice(end);
        let hasComma = rest.includes(",");
        let result = "";
        for (const character of text.replace(/\./g, ",")) {
            if (character >= "0" && character <= "9") result += character;
            else if (character === "," && !hasComma) { result += character; hasComma = true; }
        }
        return result;
    };

    const numeric = target => target instanceof HTMLInputElement && target.hasAttribute("data-numeric");

    // Text the field cannot take as it is: the allowed part is put in by script, and the change event the browser would not raise for a
    // scripted edit is raised when the field is left (or Enter is pressed).
    const insert = (input, text) => {
        const start = input.selectionStart ?? input.value.length;
        const end = input.selectionEnd ?? start;
        const accepted = clean(text, input.value, start, end);
        if (accepted.length === 0) return;
        input.setRangeText(accepted, start, end, "end");
        input.dataset.numericDirty = "true";
        input.dispatchEvent(new Event("input", { bubbles: true }));
    };

    const flush = input => {
        if (input.dataset.numericDirty !== "true") return;
        delete input.dataset.numericDirty;
        input.dispatchEvent(new Event("change", { bubbles: true }));
    };

    document.addEventListener("beforeinput", event => {
        const input = event.target;
        if (!numeric(input)) return;
        const type = event.inputType;
        if (type === "insertText" || type === "insertReplacementText") {
            const start = input.selectionStart ?? input.value.length;
            const end = input.selectionEnd ?? start;
            const typed = event.data || "";
            if (clean(typed, input.value, start, end) === typed) return;   // a digit or the first comma: the browser inserts it itself
            event.preventDefault();
            insert(input, typed);
        } else if (type === "insertFromPaste" || type === "insertFromDrop") {
            event.preventDefault();
            insert(input, event.dataTransfer ? event.dataTransfer.getData("text") : (event.data || ""));
        }
    });

    document.addEventListener("focusout", event => { if (numeric(event.target)) flush(event.target); });
    document.addEventListener("keydown", event => { if (event.key === "Enter" && numeric(event.target)) flush(event.target); });
})();
