// What a table remembers while the browser tab stays open (rows per page, sort): sessionStorage only, so nothing outlives the tab and nothing reaches the server.
window.blazorTableState = {
    get: function (key) { try { return sessionStorage.getItem("blazorstoc-table:" + key); } catch (e) { return null; } },
    set: function (key, value) { try { sessionStorage.setItem("blazorstoc-table:" + key, value); } catch (e) { } }
};
