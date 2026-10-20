// Gives the browser a file made by the server (Setari -> Baza de date -> Conturile bazei: the exported configuration). Binary, without a byte order mark.
export async function downloadBinary(fileName, stream, mimeType) {
    const buffer = await stream.arrayBuffer();
    const blob = new Blob([buffer], { type: mimeType });
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 10000);
}
