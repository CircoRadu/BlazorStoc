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
