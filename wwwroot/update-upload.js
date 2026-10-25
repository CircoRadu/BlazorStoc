(() => {
    // Settings -> Actualizare: the update package (about 100 MB) goes to the server with one HTTP request (progress shown), not through the
    // connection of the page, which moves a file in small round trips and takes many minutes over a mobile network. The file is the one
    // chosen in the <input type=file> of the FileDropZone; the page is told the progress and the result through the .NET reference.
    window.updateUpload = {
        send(inputId, reference, name) {
            const file = document.getElementById(inputId)?.files?.[0];
            if (!file) { reference.invokeMethodAsync("UploadFinished", null, "Nu s-a găsit fișierul ales. Alege-l din nou."); return; }
            const request = new XMLHttpRequest();
            let last = -1;
            request.open("POST", "/api/actualizare/pachet");
            request.setRequestHeader("X-Requested-With", "BlazorStoc");
            request.setRequestHeader("X-Package-Name", encodeURIComponent(name || file.name));
            request.setRequestHeader("Content-Type", "application/octet-stream");
            request.upload.onprogress = event => {
                if (!event.lengthComputable) return;
                const percent = Math.floor(event.loaded * 100 / event.total);
                if (percent !== last) { last = percent; reference.invokeMethodAsync("UploadProgress", percent); }
            };
            request.onload = () => {
                let body = null;
                try { body = JSON.parse(request.responseText); } catch { }
                if (request.status === 200 && body?.id) reference.invokeMethodAsync("UploadFinished", body.id, null);
                else reference.invokeMethodAsync("UploadFinished", null, body?.error ?? (request.status === 403 ? "Nu ai dreptul să încarci pachete de actualizare." : request.status === 401 ? "Sesiunea a expirat: autentifică-te din nou." : "Încărcarea a eșuat (cod " + request.status + ")."));
            };
            request.onerror = () => reference.invokeMethodAsync("UploadFinished", null, "Conexiunea s-a întrerupt în timpul încărcării. Reîncearcă (ține ecranul aprins).");
            request.ontimeout = request.onerror;
            request.send(file);
        }
    };
})();
