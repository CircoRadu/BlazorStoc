// The session reports, once, the address and the browser it connects from (the server cannot read them from the circuit): see Startup/SessionEndpoints.cs.
export async function report(circuitId) {
    try {
        await fetch("/api/session/ping", {
            method: "POST",
            credentials: "same-origin",
            headers: { "Content-Type": "application/json", "X-BlazorStoc-Session": "1" },
            body: JSON.stringify({ circuitId })
        });
    } catch (error) { /* the details stay empty; nothing else depends on them */ }
}
