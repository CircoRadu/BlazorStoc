// Prints a consumption note (a PDF): it is loaded in a hidden frame and the print dialog of the browser is opened on it; the frame is removed afterwards.
window.consumptionNotes = {
    print: function (url) {
        return new Promise(function (resolve, reject) {
            var frame = document.createElement("iframe");
            frame.setAttribute("aria-hidden", "true");
            frame.style.cssText = "position:fixed;right:0;bottom:0;width:0;height:0;border:0;visibility:hidden";
            var cleanup = function () { setTimeout(function () { frame.remove(); }, 60000); };
            frame.onload = function () {
                try { frame.contentWindow.focus(); frame.contentWindow.print(); resolve(); }
                catch (error) { frame.remove(); reject(error); return; }
                cleanup();
            };
            frame.src = url;
            document.body.appendChild(frame);
        });
    },

    // True when the browser lets the page write into a folder chosen by the user (Chrome / Edge on https or localhost).
    canPickFolder: function () {
        return typeof window.showDirectoryPicker === "function" && window.isSecureContext === true;
    },

    // Asks for a folder and saves every note in it (the names come from the server, ConsumptionNoteFiles). Returns the number of files saved, 0 when the user
    // cancelled the choice, -1 when the folder could not be used (the page then offers the zip).
    saveAll: async function (urls, names) {
        var directory;
        try { directory = await window.showDirectoryPicker({ mode: "readwrite" }); }
        catch (error) { return error && error.name === "AbortError" ? 0 : -1; }
        var saved = 0;
        try {
            for (var index = 0; index < urls.length; index++) {
                var response = await fetch(urls[index], { credentials: "same-origin" });
                if (!response.ok) return -1;
                var file = await directory.getFileHandle(names[index], { create: true });
                var writable = await file.createWritable();
                await writable.write(await response.blob());
                await writable.close();
                saved++;
            }
        } catch (error) { return -1; }
        return saved;
    },

    // Starts the download of an address (the zip) through a temporary link.
    downloadUrl: function (url) {
        var link = document.createElement("a");
        link.href = url; link.download = "";
        document.body.appendChild(link); link.click(); link.remove();
    }
};
