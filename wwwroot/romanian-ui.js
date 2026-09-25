// Romanian texts for the parts of the interface that the browser and the Blazor script write themselves (Task 1:
// every message shown to the user is in Romanian). 1) Native form validation ("Value must be greater than or equal
// to 1.") is replaced with Romanian messages. 2) The built-in reconnection dialog of Blazor is translated.
(() => {
    // ---- 1. Native form validation ---------------------------------------------------------------------------------
    const isoToDisplay = (value) => {
        const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value || "");
        return match ? `${match[3]}.${match[2]}.${match[1]}` : value;
    };

    const bound = (element, value) => element.type === "date" ? isoToDisplay(value) : value;

    const messageFor = (element) => {
        const validity = element.validity;
        if (validity.valueMissing) {
            if (element.type === "checkbox") return "Bifează această casetă pentru a continua.";
            if (element.type === "radio") return "Alege una dintre opțiuni.";
            if (element.type === "file") return "Alege un fișier.";
            if (element.tagName === "SELECT") return "Alege o valoare din listă.";
            return "Completează acest câmp.";
        }
        if (validity.typeMismatch) return element.type === "email" ? "Introdu o adresă de e-mail validă." : "Introdu o valoare validă.";
        if (validity.badInput) return element.type === "number" ? "Introdu un număr valid." : "Introdu o valoare validă.";
        if (validity.rangeUnderflow) return element.type === "date"
            ? `Data trebuie să fie cel puțin ${bound(element, element.min)}.`
            : `Valoarea trebuie să fie cel puțin ${element.min}.`;
        if (validity.rangeOverflow) return element.type === "date"
            ? `Data poate fi cel mult ${bound(element, element.max)}.`
            : `Valoarea poate fi cel mult ${element.max}.`;
        if (validity.stepMismatch) return "Introdu un număr întreg.";
        if (validity.tooShort) return `Textul trebuie să aibă cel puțin ${element.minLength} caractere.`;
        if (validity.tooLong) return `Textul poate avea cel mult ${element.maxLength} caractere.`;
        if (validity.patternMismatch) return element.title || "Valoarea nu are formatul cerut.";
        return null;
    };

    // The message must be set while the "invalid" event is being handled: the browser shows its bubble afterwards.
    document.addEventListener("invalid", (event) => {
        const element = event.target;
        if (!(element instanceof HTMLInputElement || element instanceof HTMLSelectElement || element instanceof HTMLTextAreaElement)) return;
        const message = messageFor(element);
        if (message) element.setCustomValidity(message);
    }, true);

    // A custom message would keep the field invalid: it is cleared as soon as the user changes the value.
    const clear = (event) => {
        const element = event.target;
        if (element && typeof element.setCustomValidity === "function") element.setCustomValidity("");
    };
    document.addEventListener("input", clear, true);
    document.addEventListener("change", clear, true);

    // ---- 2. Blazor reconnection dialog -----------------------------------------------------------------------------
    const translate = (text) => {
        const value = text.trim();
        if (value === "Rejoining the server...") return "Reconectare la server…";
        if (value === "Retry") return "Reîncearcă";
        if (/^Failed to rejoin\.\s*(<br\s*\/?>)?\s*Please retry or reload the page\.$/i.test(value))
            return "Reconectarea a eșuat.<br>Reîncearcă sau reîncarcă pagina.";
        const retry = /^Rejoin failed\.\.\. trying again in (\d+) (second|seconds)$/.exec(value);
        if (retry) return retry[1] === "1" ? "Reconectarea a eșuat… se reîncearcă într-o secundă" : `Reconectarea a eșuat… se reîncearcă în ${retry[1]} secunde`;
        return null;
    };

    const translateDialog = (root) => {
        for (const element of root.querySelectorAll("p, button")) {
            const translated = translate(element.innerHTML);
            if (translated !== null && translated !== element.innerHTML) element.innerHTML = translated;
        }
    };

    const watchDialog = (host) => {
        const root = host.shadowRoot;
        if (!root || host.__romanian) return;
        host.__romanian = true;
        translateDialog(root);
        new MutationObserver(() => translateDialog(root)).observe(root, { childList: true, subtree: true, characterData: true });
    };

    const find = () => {
        const host = document.getElementById("components-reconnect-modal");
        if (host) watchDialog(host);
    };
    new MutationObserver(find).observe(document.body, { childList: true });
    find();

    // Exposed for the automated browser checks.
    window.blazorStocRomanianUi = { messageFor, translate };
})();
