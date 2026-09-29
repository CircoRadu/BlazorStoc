// Keyboard support for Components/Shared/SearchableSelect.razor. Blazor cannot decide per key whether to call
// preventDefault, so this delegated listener does it: while the list is open, Up/Down/Enter/Escape must not move the
// caret, submit the form or close a dialog; Up/Down also keep the highlighted option visible in the scrolling list.
(function () {
    'use strict';
    document.addEventListener('keydown', function (event) {
        var input = event.target;
        if (!(input instanceof HTMLInputElement) || !input.hasAttribute('data-searchable-select')) return;
        var expanded = input.getAttribute('aria-expanded') === 'true';
        var navigation = event.key === 'ArrowDown' || event.key === 'ArrowUp';
        if (navigation || (expanded && (event.key === 'Enter' || event.key === 'Escape'))) event.preventDefault();
        if (expanded && event.key === 'Escape') event.stopPropagation();
        if (navigation) {
            window.setTimeout(function () {
                var id = input.getAttribute('aria-activedescendant');
                var option = id ? document.getElementById(id) : null;
                if (option && option.scrollIntoView) option.scrollIntoView({ block: 'nearest' });
            }, 80);
        }
    }, true);
})();
