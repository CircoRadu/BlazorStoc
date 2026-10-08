(() => {
    // Preluare inventar: a file dropped on [data-pickup-dropzone] is assigned to the hidden <InputFile> it names
    // (by element id) and a native "change" event is dispatched on it, so Blazor's own InputFile.OnChange pipeline
    // (Components/Pages/InventoryPickup.razor, ProcessFileAsync) runs exactly as it does for a normal file-picker
    // selection - no separate upload path to keep in sync. Delegated on document (not registered per-component), so
    // it needs no init/cleanup call and survives Blazor's enhanced navigation, like collapsible.js/leave-guard.js.
    const zoneSelector = "[data-pickup-dropzone]";

    function zoneOf(event) {
        // A FileDropZone inside the page handles its own drop (file-drop.js): it is not handled twice.
        if (event.target instanceof Element && event.target.closest("[data-file-drop]")) return null;
        return event.target instanceof Element ? event.target.closest(zoneSelector) : null;
    }

    function inputOf(zone) {
        const id = zone.getAttribute("data-pickup-dropzone");
        return id ? document.getElementById(id) : null;
    }

    function isDisabled(zone) {
        return zone.getAttribute("data-pickup-disabled") === "true";
    }

    ["dragenter", "dragover"].forEach(name => document.addEventListener(name, event => {
        const zone = zoneOf(event);
        if (!zone || isDisabled(zone) || !event.dataTransfer?.types?.includes("Files")) return;
        event.preventDefault();
        event.dataTransfer.dropEffect = "copy";
        zone.classList.add("dropzone-active");
    }));

    document.addEventListener("dragleave", event => {
        const zone = zoneOf(event);
        if (!zone) return;
        if (zone.contains(event.relatedTarget)) return;
        zone.classList.remove("dropzone-active");
    });

    document.addEventListener("drop", event => {
        const zone = zoneOf(event);
        if (!zone) return;
        event.preventDefault();
        zone.classList.remove("dropzone-active");
        if (isDisabled(zone)) return;
        const files = event.dataTransfer?.files;
        const input = inputOf(zone);
        if (!input || !files || files.length === 0) return;
        input.files = files;
        input.dispatchEvent(new Event("change", { bubbles: true }));
    });
})();
