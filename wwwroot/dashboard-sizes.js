// Personalizare dashboard: the widest tile a zone can show. The slider "Latime tile" is a cap (--dash-tw): a value above what the zone gives each column changes nothing, so the editor asks
// here how wide a column is now (the narrowest zone, counted without the cap) and the slider ends at that width.
window.dashboardSizes = {
    // The width the zones can have on the page (the editor row of zones, without its padding): the sliders of the width of the zones end there.
    zonesAvailable: function () {
        var el = document.querySelector('.dashboard-editor .dashboard-zones');
        if (!el) return 0;
        var style = getComputedStyle(el);
        return Math.max(0, Math.floor(el.clientWidth - (parseFloat(style.paddingLeft) || 0) - (parseFloat(style.paddingRight) || 0)));
    },
    available: function () {
        var best = 0;
        document.querySelectorAll('.dashboard-cells').forEach(function (cells) {
            var style = getComputedStyle(cells);
            var columns = (style.gridTemplateColumns || '').split(' ').filter(Boolean).length || 1;
            var gap = parseFloat(style.columnGap) || 0;
            var inner = cells.clientWidth - (parseFloat(style.paddingLeft) || 0) - (parseFloat(style.paddingRight) || 0);
            var each = Math.floor((inner - gap * (columns - 1)) / columns);
            if (each > 0 && (best === 0 || each < best)) best = each;
        });
        return best;
    }
};
