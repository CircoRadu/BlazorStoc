// The page where the first password is chosen: the conditions of the password and the match of the repetition are checked as the user types (green when met, red otherwise),
// the password can be shown, and the button waits until everything is met. The server checks the same rules again; without this script the page still works.
(() => {
    const form = document.querySelector("[data-password-form]");
    if (!form) return;
    const first = form.querySelector("[data-password-new]");
    const second = form.querySelector("[data-password-confirm]");
    const show = form.querySelector("[data-password-show]");
    const submit = form.querySelector("[data-password-submit]");
    const items = new Map([...form.querySelectorAll("[data-password-rules] [data-rule]")].map((item) => [item.dataset.rule, item]));
    // The same tests as PasswordPolicy.Rules (Services/WebUsers.cs): characters by their Unicode category.
    const tests = {
        length: (p) => p.length >= 8,
        lower: (p) => /\p{Ll}/u.test(p),
        upper: (p) => /\p{Lu}/u.test(p),
        digit: (p) => /\p{Nd}/u.test(p),
        special: (p) => /[^\p{L}\p{Nd}\s]/u.test(p),
        match: (p, r) => p.length > 0 && p === r
    };
    const mark = (item, met) => {
        item.classList.toggle("ok", met);
        item.classList.toggle("bad", !met);
        const sign = item.querySelector(".rule-mark");
        if (sign) sign.textContent = met ? "\u2713" : "\u2715";
    };
    const refresh = () => {
        let all = true;
        for (const [key, item] of items) {
            const met = tests[key] ? tests[key](first.value, second.value) : true;
            mark(item, met);
            all = all && met;
        }
        if (submit) submit.disabled = !all;
    };
    show?.addEventListener("change", () => {
        const type = show.checked ? "text" : "password";
        first.type = type;
        second.type = type;
    });
    first.addEventListener("input", refresh);
    second.addEventListener("input", refresh);
    refresh();
})();
