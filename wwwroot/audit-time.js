export function formatAuditTimestamps(entries) {
    const dateFormatter = new Intl.DateTimeFormat("ro-RO", {
        day: "2-digit",
        month: "2-digit",
        year: "numeric"
    });
    const timeFormatter = new Intl.DateTimeFormat("ro-RO", {
        hour: "2-digit",
        minute: "2-digit",
        second: "2-digit",
        hour12: false
    });
    const keyFormatter = new Intl.DateTimeFormat("en-CA", {
        year: "numeric",
        month: "2-digit",
        day: "2-digit"
    });

    return entries.map(entry => {
        const timestamp = new Date(entry.timestampUtc);
        const keyParts = Object.fromEntries(keyFormatter.formatToParts(timestamp)
            .filter(part => part.type !== "literal")
            .map(part => [part.type, part.value]));
        return {
            id: entry.id,
            date: dateFormatter.format(timestamp),
            time: timeFormatter.format(timestamp),
            dateKey: `${keyParts.year}-${keyParts.month}-${keyParts.day}`
        };
    });
}

// The chosen local days (yyyy-MM-dd) as UTC instants: from the start of the first day, up to (excluding) the start of the day after the
// last one. The journal is stored in UTC and shown in the browser's time zone, so the day boundaries are the browser's.
export function localRangeUtc(fromKey, toKey) {
    const start = key => { const [y, m, d] = key.split("-").map(Number); return new Date(y, m - 1, d); };
    return {
        fromUtc: fromKey ? start(fromKey).toISOString() : null,
        toUtc: toKey ? (day => { day.setDate(day.getDate() + 1); return day.toISOString(); })(start(toKey)) : null
    };
}

// Saves a text file received as a stream (the CSV export of the complete journal); UTF-8 with a byte order mark, so Excel reads the diacritics.
export async function downloadFile(fileName, stream, mimeType) {
    const buffer = await stream.arrayBuffer();
    const blob = new Blob([new Uint8Array([0xEF, 0xBB, 0xBF]), buffer], { type: mimeType });
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 10000);
}
