// The maintenance map (Components/Pages/MaintenanceMap.razor). Leaflet and the marker clustering are served from /lib/leaflet (no CDN) and
// loaded only when the page is opened. This module only draws: the rules (which point is overdue, which contract expired) are decided in C#
// and arrive as plain fields of each marker. Nothing typed by users is ever written as HTML: a marker only carries a class and a short glyph
// chosen here, and its title is set as a text attribute.
const base = "/lib/leaflet/";
let loading = null;

function loadCss(href) {
    if (document.querySelector(`link[href="${href}"]`)) return;
    const link = document.createElement("link");
    link.rel = "stylesheet";
    link.href = href;
    document.head.appendChild(link);
}

function loadScript(src) {
    return new Promise((resolve, reject) => {
        const script = document.createElement("script");
        script.src = src;
        script.onload = resolve;
        script.onerror = () => reject(new Error("Nu s-a putut incarca " + src));
        document.head.appendChild(script);
    });
}

function ensureLibrary() {
    if (!loading) {
        loading = (async () => {
            loadCss(base + "leaflet.css");
            loadCss(base + "MarkerCluster.css");
            loadCss(base + "MarkerCluster.Default.css");
            if (!window.L) await loadScript(base + "leaflet.js");
            if (!window.L.markerClusterGroup) await loadScript(base + "leaflet.markercluster.js");
        })();
        loading.catch(() => { loading = null; });
    }
    return loading;
}

// The look of the pins comes from Setări → Hartă → Overlay: { fills: { key: { color, fg, glyph, name } }, badges: { key: { ... } } }. Colours are
// #rrggbb (checked on the server); glyphs are plain text put into the page as text, never as markup.
const fallback = { color: "#8a969a", fg: "#ffffff", glyph: "" };

function pinStyle(styles, marker) {
    return (styles.fills && styles.fills[marker.fill]) || fallback;
}

function badgeHtml(style, corner) {
    return `<span class="mm-badge mm-badge-${corner}" style="background:${style.color};color:${style.fg}" aria-hidden="true">${escapeText(style.glyph)}</span>`;
}

function escapeText(text) {
    return String(text ?? "").replace(/[&<>"']/g, ch => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[ch]));
}

function iconFor(state, marker, selected, flash) {
    const styles = state.styles || {};
    const fill = pinStyle(styles, marker);
    const badges = [];
    const contract = marker.contract && styles.badges && styles.badges[marker.contract];
    if (contract) badges.push(badgeHtml(contract, "top"));
    (marker.extra || []).slice(0, 3).forEach((key, index) => {
        const style = styles.badges && styles.badges[key];
        if (style) badges.push(badgeHtml(style, "low" + index));
    });
    return L.divIcon({
        className: "mm-icon",
        html: `<span class="mm-pin${selected ? " mm-selected" : ""}${flash ? " mm-flash" : ""}" style="background:${fill.color};color:${fill.fg}"><span class="mm-glyph" aria-hidden="true">${escapeText(fill.glyph)}</span>${badges.join("")}</span>`,
        iconSize: [34, 34],
        iconAnchor: [17, 17]
    });
}

export async function create(element, dotnet, options, styles) {
    await ensureLibrary();
    const map = L.map(element, { minZoom: options.minZoom, maxZoom: options.maxZoom, zoomControl: true })
        .setView([options.centerLatitude, options.centerLongitude], options.startZoom);
    const state = { map, dotnet, styles, markers: new Map(), selected: null, failures: 0, failureTimer: null, reported: false };
    state.tiles = L.tileLayer(options.tileUrl, { attribution: options.attribution, minZoom: options.minZoom, maxZoom: options.maxZoom, crossOrigin: false });
    // A single failed tile is nothing; several in a short while are reported once, and the page offers a retry.
    state.tiles.on("tileerror", () => {
        state.failures += 1;
        clearTimeout(state.failureTimer);
        state.failureTimer = setTimeout(() => { state.failures = 0; }, 10000);
        if (state.failures >= 6 && !state.reported) { state.reported = true; dotnet.invokeMethodAsync("OnTilesFailed"); }
    });
    state.tiles.on("tileload", () => {
        if (state.reported) { state.reported = false; dotnet.invokeMethodAsync("OnTilesRecovered"); }
        state.failures = 0;
    });
    state.attribution = options.attribution;
    state.tiles.addTo(map);
    state.cluster = L.markerClusterGroup({ showCoverageOnHover: false, spiderfyOnMaxZoom: true, maxClusterRadius: 45, disableClusteringAtZoom: options.maxZoom });
    map.addLayer(state.cluster);
    element.__maintenanceMap = state;
    return state;
}

// Replaces the markers (the map itself is never recreated). Each: { id, lat, lng, fill, contract, title }.
export function setMarkers(element, markers, fit, announce) {
    const state = element.__maintenanceMap;
    if (!state) return;
    // When the update comes from the automatic refresh, a marker that is new or whose fill/contract badge/position changed pulses for a few seconds.
    const previous = state.markers;
    state.cluster.clearLayers();
    state.markers = new Map();
    const flashing = [];
    const layers = markers.map(marker => {
        const before = previous.get(marker.id)?.marker;
        const changed = announce && (!before || before.fill !== marker.fill || before.contract !== marker.contract || before.lat !== marker.lat || before.lng !== marker.lng);
        const layer = L.marker([marker.lat, marker.lng], { icon: iconFor(state, marker, marker.id === state.selected, changed), title: marker.title, keyboard: true, riseOnHover: true });
        layer.on("click", () => { state.dotnet.invokeMethodAsync("OnMarkerSelected", marker.id); });
        state.markers.set(marker.id, { layer, marker });
        if (changed) flashing.push(marker.id);
        return layer;
    });
    state.cluster.addLayers(layers);
    if (flashing.length > 0) {
        clearTimeout(state.flashTimer);
        state.flashTimer = setTimeout(() => {
            for (const id of flashing) {
                const item = state.markers.get(id);
                if (item) item.layer.setIcon(iconFor(state, item.marker, id === state.selected, false));
            }
        }, 8000);
    }
    if (fit) fitAll(element);
}

// New engine settings or pin styles (changed in Setări): the tile layer is replaced or re-addressed and every pin is drawn again.
export function configure(element, options, styles) {
    const state = element.__maintenanceMap;
    if (!state) return;
    state.styles = styles;
    state.map.setMinZoom(options.minZoom);
    state.map.setMaxZoom(options.maxZoom);
    state.tiles.options.minZoom = options.minZoom;
    state.tiles.options.maxZoom = options.maxZoom;
    state.tiles.options.attribution = options.attribution;
    state.map.attributionControl.removeAttribution(state.attribution);
    state.attribution = options.attribution;
    state.map.attributionControl.addAttribution(options.attribution);
    if (state.tiles._url !== options.tileUrl) state.tiles.setUrl(options.tileUrl);
    state.reported = false;
    state.failures = 0;
    for (const item of state.markers.values()) item.layer.setIcon(iconFor(state, item.marker, item.marker.id === state.selected, false));
}

export function fitAll(element) {
    const state = element.__maintenanceMap;
    if (!state || state.markers.size === 0) return;
    const bounds = L.latLngBounds([...state.markers.values()].map(item => item.layer.getLatLng()));
    state.map.fitBounds(bounds, { padding: [40, 40], maxZoom: 15 });
}

export function select(element, id, zoomTo) {
    const state = element.__maintenanceMap;
    if (!state) return;
    const previous = state.markers.get(state.selected);
    state.selected = id;
    if (previous) previous.layer.setIcon(iconFor(state, previous.marker, false));
    const current = state.markers.get(id);
    if (!current) return;
    current.layer.setIcon(iconFor(state, current.marker, true));
    if (zoomTo) state.cluster.zoomToShowLayer(current.layer, () => {});
}

export function retryTiles(element) {
    const state = element.__maintenanceMap;
    if (!state) return;
    state.reported = false;
    state.failures = 0;
    state.tiles.redraw();
}

// Automatic refresh, only while the map can be seen: the window or tab is not hidden (or minimized) and the map is on screen. It asks
// the page (OnAutoRefresh) every intervalMs, and at once when the map becomes visible again after more than a few seconds. The tiles
// are not part of this: the map requests them itself when it moves or zooms.
export function watch(element, dotnet, intervalMs) {
    const state = element.__maintenanceMap;
    if (!state) return;
    stopWatch(state);
    let onScreen = true, busy = false, last = Date.now();
    const visible = () => document.visibilityState === "visible" && onScreen;
    const tick = async force => {
        if (busy || !visible()) return;
        if (!force && Date.now() - last < intervalMs) return;
        busy = true; last = Date.now();
        try { await dotnet.invokeMethodAsync("OnAutoRefresh"); } catch { /* the circuit is gone: nothing to refresh */ } finally { busy = false; }
    };
    const resume = () => { if (visible() && Date.now() - last > 5000) tick(true); };
    const observer = "IntersectionObserver" in window
        ? new IntersectionObserver(entries => { onScreen = entries.some(entry => entry.isIntersecting); resume(); })
        : null;
    observer?.observe(element);
    document.addEventListener("visibilitychange", resume);
    window.addEventListener("focus", resume);
    state.watcher = { timer: setInterval(() => tick(false), 5000), observer, resume };
}

function stopWatch(state) {
    const watcher = state.watcher;
    if (!watcher) return;
    clearInterval(watcher.timer);
    watcher.observer?.disconnect();
    document.removeEventListener("visibilitychange", watcher.resume);
    window.removeEventListener("focus", watcher.resume);
    state.watcher = null;
}

// In the separate window the links to the application (beneficiary, new intervention, the list) open in the window of the
// application that opened it, so that the map window stays where it is; when that window is closed, in a new tab.
export function openLinksInMainWindow(root) {
    root.addEventListener("click", event => {
        const link = event.target.closest && event.target.closest("a[href]");
        if (!link || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
        const url = new URL(link.href, location.href);
        if (url.origin !== location.origin) return;
        event.preventDefault();
        event.stopPropagation();
        const main = window.opener;
        if (main && !main.closed) {
            try { main.location.href = url.href; main.focus(); return; } catch { /* fall through to a new tab */ }
        }
        window.open(url.href, "blazorstoc-main");
    }, true);
}

export function invalidate(element) {
    element.__maintenanceMap?.map.invalidateSize();
}

export function dispose(element) {
    const state = element.__maintenanceMap;
    if (!state) return;
    clearTimeout(state.failureTimer);
    clearTimeout(state.flashTimer);
    stopWatch(state);
    state.map.remove();
    delete element.__maintenanceMap;
}
