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
    }
};
