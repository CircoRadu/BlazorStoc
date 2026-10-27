// The map holder (Components/Shared/MapHolder.razor): a small map with ONE pin that can be dragged, used where a place is chosen (the work point
// editor now, the maintenance map later). Leaflet comes from /lib/leaflet (same loader as the maintenance map); the background uses the tile
// provider of Setări → Hartă. The label of the pin (name, address, coordinates) is built as DOM text, never as markup.
import { ensureLibrary, setBaseLayers, savedBaseIsSatellite } from "./maintenance-map.js";

function labelElement(label) {
    const box = document.createElement("div");
    box.className = "mh-label";
    const name = document.createElement("strong");
    name.textContent = label.name || "";
    box.appendChild(name);
    for (const line of [label.address, label.coordinates]) {
        if (!line) continue;
        const row = document.createElement("span");
        row.textContent = line;
        box.appendChild(row);
    }
    return box;
}

function coordinatesText(latlng) {
    return latlng.lat.toFixed(6) + ", " + latlng.lng.toFixed(6);
}

function pinIcon() {
    return L.divIcon({ className: "mh-icon", html: '<span class="mh-pin" aria-hidden="true"></span>', iconSize: [30, 42], iconAnchor: [15, 40], tooltipAnchor: [16, -28] });
}

export async function create(element, dotnet, options, pin, draggable) {
    await ensureLibrary();
    const map = L.map(element, { minZoom: options.minZoom, maxZoom: options.maxZoom, zoomControl: true })
        .setView([options.centerLatitude, options.centerLongitude], options.startZoom);
    const state = { map, dotnet, draggable, marker: null, label: { name: "", address: "" }, failures: 0, reported: false };
    state.tiles = L.tileLayer(options.tileUrl, { attribution: options.attribution, minZoom: options.minZoom, maxZoom: options.maxZoom, crossOrigin: false });
    state.watchTiles = layer => {
        layer.on("tileerror", () => {
            state.failures += 1;
            if (state.failures >= 4 && !state.reported) { state.reported = true; dotnet.invokeMethodAsync("OnTilesFailed"); }
        });
        layer.on("tileload", () => {
            state.failures = 0;
            if (state.reported) { state.reported = false; dotnet.invokeMethodAsync("OnTilesRecovered"); }
        });
    };
    state.watchTiles(state.tiles);
    // The street map, or the satellite background when the settings have one (the choice is shared with the maintenance map).
    setBaseLayers(state, options, savedBaseIsSatellite());
    // A click on the map puts the pin there (or moves it): useful when the address is not found by the search.
    if (draggable) map.on("click", event => { place(state, event.latlng, true); });
    element.__mapHolder = state;
    if (pin) setPin(element, pin.latitude, pin.longitude, pin.name, pin.address, true);
    setTimeout(() => map.invalidateSize(), 50);
}

function refreshLabel(state) {
    if (!state.marker) return;
    const text = labelElement({ name: state.label.name, address: state.label.address, coordinates: coordinatesText(state.marker.getLatLng()) });
    if (state.marker.getTooltip()) state.marker.setTooltipContent(text);
    else state.marker.bindTooltip(text, { permanent: true, direction: "right", className: "mh-tooltip", interactive: false });
}

function place(state, latlng, notify) {
    if (!state.marker) {
        state.marker = L.marker(latlng, { icon: pinIcon(), draggable: state.draggable, keyboard: true, title: state.label.name || "Punct de lucru" });
        state.marker.addTo(state.map);
        state.marker.on("drag", () => refreshLabel(state));
        state.marker.on("dragend", () => {
            const at = state.marker.getLatLng();
            state.dotnet.invokeMethodAsync("OnPinMoved", at.lat, at.lng);
        });
    } else state.marker.setLatLng(latlng);
    refreshLabel(state);
    if (notify) state.dotnet.invokeMethodAsync("OnPinMoved", latlng.lat, latlng.lng);
}

// Puts or moves the pin from the page (the coordinates typed, a search result chosen). The map follows it only when it is out of view or "center" is set.
export function setPin(element, latitude, longitude, name, address, center) {
    const state = element.__mapHolder;
    if (!state) return;
    state.label = { name: name || "", address: address || "" };
    const latlng = L.latLng(latitude, longitude);
    place(state, latlng, false);
    if (center || !state.map.getBounds().contains(latlng)) state.map.setView(latlng, Math.max(state.map.getZoom(), 16));
}

export function setLabel(element, name, address) {
    const state = element.__mapHolder;
    if (!state) return;
    state.label = { name: name || "", address: address || "" };
    refreshLabel(state);
}

export function clearPin(element) {
    const state = element.__mapHolder;
    if (!state || !state.marker) return;
    state.marker.remove();
    state.marker = null;
}

export function retryTiles(element) {
    const state = element.__mapHolder;
    if (!state) return;
    state.failures = 0; state.reported = false;
    state.tiles.redraw();
    if (state.satellite) state.satellite.redraw();
}

export function invalidate(element) {
    element.__mapHolder?.map.invalidateSize();
}

export function dispose(element) {
    const state = element.__mapHolder;
    if (!state) return;
    state.map.remove();
    delete element.__mapHolder;
}
