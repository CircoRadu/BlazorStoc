(() => {
    // FileDropZone (Components/Shared/FileDropZone.razor): files dropped on a [data-file-drop] area are assigned to the hidden <input type=file>
    // inside it and a native "change" event is raised, so Blazor's InputFile.OnChange runs exactly as for a normal file-picker selection.
    // Delegated on document (nothing to initialise or clean up), like inventory-pickup-dropzone.js. A field that does not take several files
    // gets only the first dropped file, and files the field does not accept (its accept list) are ignored.
    const zoneSelector = "[data-file-drop]";

    const zoneOf = event => event.target instanceof Element ? event.target.closest(zoneSelector) : null;
    const inputOf = zone => zone.querySelector("input[type=file]");
    const hasFiles = event => Array.from(event.dataTransfer?.types ?? []).includes("Files");

    function accepts(input, file) {
        const rules = (input.accept || "").split(",").map(rule => rule.trim().toLowerCase()).filter(Boolean);
        if (rules.length === 0) return true;
        const name = file.name.toLowerCase();
        const type = (file.type || "").toLowerCase();
        return rules.some(rule => rule.startsWith(".") ? name.endsWith(rule) : rule.endsWith("/*") ? type.startsWith(rule.slice(0, -1)) : type === rule);
    }

    ["dragenter", "dragover"].forEach(name => document.addEventListener(name, event => {
        const zone = zoneOf(event);
        if (!zone || !hasFiles(event)) return;
        const input = inputOf(zone);
        if (!input || input.disabled) return;
        event.preventDefault();
        event.dataTransfer.dropEffect = "copy";
        zone.classList.add("file-drop-active");
    }));

    document.addEventListener("dragleave", event => {
        const zone = zoneOf(event);
        if (!zone || zone.contains(event.relatedTarget)) return;
        zone.classList.remove("file-drop-active");
    });

    document.addEventListener("drop", event => {
        const zone = zoneOf(event);
        if (!zone) return;
        event.preventDefault();
        zone.classList.remove("file-drop-active");
        const input = inputOf(zone);
        if (!input || input.disabled || !event.dataTransfer?.files?.length) return;
        let files = Array.from(event.dataTransfer.files).filter(file => accepts(input, file));
        if (!input.multiple) files = files.slice(0, 1);
        if (files.length === 0) return;
        const transfer = new DataTransfer();
        files.forEach(file => transfer.items.add(file));
        input.files = transfer.files;
        input.dispatchEvent(new Event("change", { bubbles: true }));
    });

    // A file dropped next to a field must not make the browser open (and leave the page for) the file.
    ["dragover", "drop"].forEach(name => document.addEventListener(name, event => {
        if (hasFiles(event) && !zoneOf(event) && !(event.target instanceof Element && event.target.closest("[data-pickup-dropzone]"))) event.preventDefault();
    }));
})();
