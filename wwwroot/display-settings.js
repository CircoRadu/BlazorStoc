// Applies the user's display scale (PROP-0034) on the application shell at once, without reloading the page: the page "Setari afisare" calls it after a save.
window.displaySettings = {
    apply: function (names, values) {
        var shell = document.querySelector('.app-shell');
        if (!shell) return;
        ['table', 'header', 'images', 'icons', 'titles', 'tabs'].forEach(function (name) { shell.style.removeProperty('--ui-' + name); });
        for (var i = 0; i < names.length; i++) shell.style.setProperty('--ui-' + names[i], values[i]);
    }
};
