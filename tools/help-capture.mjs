// Generates the screenshots of the help topics (docs/PROPUNERE_HELP_IMAGINI.md): for every "Captura:" line of a .help file it opens the page in a
// headless Edge/Chrome at 1920x1080, does the steps of the line (a click that opens a window, a tab...), takes the picture and measures, with the same
// matcher the help window uses (wwwroot/help-match.js), where each element of the topic is. Output: Assets/Help/img/<key>.<id>.jpg and <key>.<id>.json.
// The topics are read from Assets/Help/*.help (no running help needed); the pictures are embedded at the next build.
//
// It runs against an instance with the DEMO database (fictive data), never against the real one:
//   1. stop the instance, then .\tools\help-demo-db.ps1                       (creates blazorstoc_demo from the schema of the test database)
//   2. dotnet run --project tests/BlazorStoc.HelpDemo -c Release              (fictive suppliers, products, beneficiaries, vehicles, projects...)
//   3. dotnet run --project tests/BlazorStoc.HelpDemo -c Release -- --files artifacts/help-demo-files   (sample PDF and XML invoices for the upload steps)
//   4. start the application on the demo database (port 5089), then:  node tools/help-capture.mjs --prepare     (resolves one notification and saves one invoice template, once)
//   5. node tools/help-capture.mjs --all                                       (or --topic produse-catalog,setari  /  --missing  /  --report: measure only)
// The user and the password are read from local-secrets/test-login.private.json (never printed); --base changes the address (default http://127.0.0.1:5089).
// Steps (";" separated, 3rd field of "Captura:"): click <text> | click sel:<css> | jsclick <text> | visit <text> (opens the address of a link) | popup <text> (a link that opens a window: taken in a second tab) | upload <file> | wait <ms> | type <css>=<value> | scroll <css> | hover <text> |
// full (the whole page, up to 3000 px) | height <px> (a taller window for a long form) | anonymous (without the session: Login).
// Without steps a page is taken whole; with steps (a window opened by a click) the picture is what is on the screen.
import { spawn, spawnSync } from "node:child_process";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";

const repo = path.resolve(path.dirname(new URL(import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, "$1")), "..");
const args = process.argv.slice(2);
const option = (name, fallback) => { const index = args.indexOf("--" + name); return index >= 0 && index + 1 < args.length && !args[index + 1].startsWith("--") ? args[index + 1] : fallback; };
const flag = (name) => args.includes("--" + name);

const base = option("base", "http://127.0.0.1:5089").replace(/\/$/, "");
const onlyTopics = (option("topic", "") || "").split(",").map((item) => item.trim()).filter(Boolean);
const quality = Number(option("quality", "68"));
const reportOnly = flag("report");
const missingOnly = flag("missing");
const width = 1920, minHeight = 1080, maxHeight = 3000;
const outDir = path.join(repo, "Assets", "Help", "img");
const port = Number(option("port", String(9300 + Math.floor(Math.random() * 600))));   // a free random port: a browser left by a crashed run cannot be reached by mistake

const browserPaths = [
    "C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe",
    "C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe",
    "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe",
    "C:\\Program Files (x86)\\Google\\Chrome\\Application\\chrome.exe"
];
const browserPath = browserPaths.find((candidate) => fs.existsSync(candidate));
if (!browserPath) { console.error("Nu am gasit Edge sau Chrome."); process.exit(2); }
const evalCode = option("eval", "");
if (!evalCode && !flag("prepare") && !flag("all") && onlyTopics.length === 0 && !missingOnly) { console.error("Alege --all, --missing sau --topic cheie[,cheie]."); process.exit(2); }

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

// ---- Minimal CDP client ------------------------------------------------------------------------------------------------------------------------------------
class Cdp {
    constructor(url) { this.url = url; this.id = 0; this.pending = new Map(); this.waiters = []; }
    async open() {
        this.socket = new WebSocket(this.url);
        await new Promise((resolve, reject) => { this.socket.onopen = resolve; this.socket.onerror = () => reject(new Error("CDP: conexiune esuata")); });
        this.socket.onmessage = (message) => {
            const data = JSON.parse(message.data);
            if (data.id && this.pending.has(data.id)) {
                const { resolve, reject } = this.pending.get(data.id);
                this.pending.delete(data.id);
                if (data.error) reject(new Error(data.error.message)); else resolve(data.result);
            } else if (data.method) {
                (this.handlers || []).filter((item) => item.method === data.method).forEach((item) => item.handler(data.params));
                this.waiters = this.waiters.filter((waiter) => { if (waiter.method === data.method) { waiter.resolve(data.params); return false; } return true; });
            }
        };
    }
    send(method, params = {}) {
        const id = ++this.id;
        this.socket.send(JSON.stringify({ id, method, params }));
        return new Promise((resolve, reject) => this.pending.set(id, { resolve, reject }));
    }
    on(method, handler) { this.handlers = [...(this.handlers || []), { method, handler }]; }
    once(method, timeout = 30000) {
        return new Promise((resolve, reject) => {
            const timer = setTimeout(() => reject(new Error("timeout " + method)), timeout);
            this.waiters.push({ method, resolve: (params) => { clearTimeout(timer); resolve(params); } });
        });
    }
    async evaluate(expression) {
        const result = await this.send("Runtime.evaluate", { expression, awaitPromise: true, returnByValue: true });
        if (result.exceptionDetails) throw new Error(result.exceptionDetails.exception?.description || result.exceptionDetails.text);
        return result.result.value;
    }
}

async function launch() {
    const profile = fs.mkdtempSync(path.join(os.tmpdir(), "help-capture-"));
    const child = spawn(browserPath, [
        "--headless=new", `--remote-debugging-port=${port}`, `--user-data-dir=${profile}`, "--hide-scrollbars", "--force-device-scale-factor=1",
        "--no-first-run", "--disable-extensions", "--disable-gpu", `--window-size=${width},${minHeight}`, "about:blank"
    ], { stdio: "ignore" });
    for (let attempt = 0; attempt < 60; attempt++) {
        try {
            const targets = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
            const page = targets.find((target) => target.type === "page");
            if (page) return { child, profile, url: page.webSocketDebuggerUrl };
        } catch { /* not ready */ }
        await sleep(500);
    }
    child.kill();
    throw new Error("Browserul nu a pornit.");
}

// ---- The topics --------------------------------------------------------------------------------------------------------------------------------------------
// The topics are read from Assets/Help/*.help here (the same order of elements as Services/Help/HelpParser.cs: the number of an element is its place in the file).
function topicsFromFiles() {
    const dir = path.join(repo, "Assets", "Help");
    const topics = [];
    for (const file of fs.readdirSync(dir).filter((name) => name.endsWith(".help")).sort()) {
        const lines = fs.readFileSync(path.join(dir, file), "utf8").replace(/^\uFEFF/, "").split(/\r?\n/);
        const topic = { key: file.slice(0, -5), title: lines[0].replace(/^#\s*/, ""), captures: [], elements: [] };
        let section = "header";
        for (const raw of lines.slice(1)) {
            const line = raw.trim();
            if (line.startsWith("## ")) { section = line.startsWith("## Elemente") ? "elements" : "other"; continue; }
            if (section === "header" && /^Captura:/i.test(line)) {
                const fields = line.slice(8).split("|", 4).map((item) => item.trim());
                const parts = line.slice(8).split("|").map((item) => item.trim());
                const caption = parts.length > 3 ? parts.slice(3).join("|").trim() : "";
                topic.captures.push({ id: fields[0], url: fields[1], steps: (fields[2] || "").split(";").map((item) => item.trim()).filter(Boolean), caption });
            } else if (section === "header" && line === "") section = "summary";
            else if (section === "elements" && line.startsWith("- ")) {
                const parts = line.slice(2).split("|").map((item) => item.trim());
                let tail = parts.slice(2);
                let sel = null, img = null;
                while (tail.length > 1 && /^(sel|img):/.test(tail[tail.length - 1])) {
                    const last = tail.pop();
                    if (last.startsWith("sel:")) sel = last.slice(4).trim(); else img = last.slice(4).trim();
                }
                const kind = parts[1] || "";
                topic.elements.push({ n: topic.elements.length, name: parts[0], kind: kind.startsWith("iconita:") ? "iconiță" : kind, icon: kind.startsWith("iconita:") ? kind.slice(8).trim() : null, sel, img });
            }
        }
        if (topic.captures.length) topics.push(topic);
    }
    return topics;
}

// ---- Page steps --------------------------------------------------------------------------------------------------------------------------------------------
const matcherSource = fs.readFileSync(path.join(repo, "wwwroot", "help-match.js"), "utf8");

async function navigate(cdp, url) {
    const loaded = cdp.once("Page.loadEventFired");
    await cdp.send("Page.navigate", { url });
    await loaded;
    await sleep(2200);   // the interactive circuit connects after the static render
    await cdp.evaluate(matcherSource);
}

async function runStep(cdp, step) {
    const [verb, ...rest] = step.trim().split(/\s+/);
    const argument = rest.join(" ");
    const fail = (why) => { throw new Error(`pas "${step}": ${why}`); };
    if (verb === "wait") { await sleep(Number(argument) || 500); return; }
    if (verb === "visit") {   // opens the address of a link in this window (a link that the application opens in a new window)
        const href = await cdp.evaluate(`(() => {
            const text = ${JSON.stringify(argument)};
            const els = text.startsWith("sel:") ? HelpMatch.find({ sel: text.slice(4) }) : HelpMatch.find({ name: text });
            return els.length && els[0].getAttribute("href");
        })()`);
        if (!href) fail("legatura nu exista");
        await navigate(cdp, base + href);
        return;
    }
    if (verb === "jsclick") {   // a click made by the page itself (el.click()), for a control that moves when the page lays out again
        const done = await cdp.evaluate(`(() => {
            const text = ${JSON.stringify(argument)};
            const els = text.startsWith("sel:") ? HelpMatch.find({ sel: text.slice(4) }) : HelpMatch.find({ name: text });
            if (!els.length) return false;
            els[0].click();
            return true;
        })()`);
        if (!done) fail("elementul nu exista");
        await sleep(900);
        return;
    }
    if (verb === "click" || verb === "hover") {
        const found = await cdp.evaluate(`(() => {
            const text = ${JSON.stringify(argument)};
            const pick = (text) => {
                if (text.startsWith("sel:")) return HelpMatch.find({ sel: text.slice(4) });
                const found = HelpMatch.find({ name: text });
                const tab = found.find((el) => el.matches("[role=tab]")) || Array.from(document.querySelectorAll("[role=tab]")).find((el) => HelpMatch.norm(el.textContent).startsWith(HelpMatch.norm(text)));
                return tab ? [tab] : found;
            };
            const els = pick(text);
            if (!els.length) return null;
            const rect = els[0].getBoundingClientRect();
            window.scrollBy({ top: rect.top - innerHeight / 2 + rect.height / 2, behavior: "instant" });   // scrollIntoView does not scroll some pages (the invoice template editor)
            return true;
        })()`);
        if (!found) fail("elementul nu exista");
        await sleep(450);   // the page may scroll smoothly: the place of the element is read after it stops
        // The mouse event goes to the real place of the element (a Blazor handler or a native dialog both see a real click).
        const point = await cdp.evaluate(`(() => {
            const text = ${JSON.stringify(argument)};
            const pick = (text) => {
                if (text.startsWith("sel:")) return HelpMatch.find({ sel: text.slice(4) });
                const found = HelpMatch.find({ name: text });
                const tab = found.find((el) => el.matches("[role=tab]")) || Array.from(document.querySelectorAll("[role=tab]")).find((el) => HelpMatch.norm(el.textContent).startsWith(HelpMatch.norm(text)));
                return tab ? [tab] : found;
            };
            const els = pick(text);
            const rect = els[0].getBoundingClientRect();
            return { x: rect.left + rect.width / 2, y: rect.top + rect.height / 2 };
        })()`);
        await cdp.send("Input.dispatchMouseEvent", { type: "mouseMoved", x: point.x, y: point.y });
        if (verb === "click") {
            await cdp.send("Input.dispatchMouseEvent", { type: "mousePressed", x: point.x, y: point.y, button: "left", clickCount: 1 });
            await cdp.send("Input.dispatchMouseEvent", { type: "mouseReleased", x: point.x, y: point.y, button: "left", clickCount: 1 });
        }
        await sleep(900);
        return;
    }
    if (verb === "type") {
        const [selector, ...valueParts] = argument.split("=");
        const value = valueParts.join("=");
        const done = await cdp.evaluate(`(() => {
            const el = document.querySelector(${JSON.stringify(selector)});
            if (!el) return false;
            el.focus();
            const proto = el instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
            Object.getOwnPropertyDescriptor(proto, "value").set.call(el, ${JSON.stringify(value)});
            el.dispatchEvent(new Event("input", { bubbles: true }));
            el.dispatchEvent(new Event("change", { bubbles: true }));
            return true;
        })()`);
        if (!done) fail("campul nu exista");
        await sleep(700);
        return;
    }
    if (verb === "upload") {   // upload <file>: a sample from artifacts/help-demo-files (dotnet run --project tests/BlazorStoc.HelpDemo -- --files <dir>) goes into the first file field
        const file = path.join(repo, "artifacts", "help-demo-files", argument);
        if (!fs.existsSync(file)) fail("lipseste fisierul " + file);
        const document = await cdp.send("DOM.getDocument", { depth: 0 });
        const found = await cdp.send("DOM.querySelectorAll", { nodeId: document.root.nodeId, selector: "input[type=file]" });
        if (!found.nodeIds.length) fail("nu exista camp de fisier");
        await cdp.send("DOM.setFileInputFiles", { files: [file], nodeId: found.nodeIds[0] });
        await sleep(3500);
        return;
    }
    if (verb === "scroll") { await cdp.evaluate(`document.querySelector(${JSON.stringify(argument)})?.scrollIntoView({ block: "center", behavior: "instant" })`); await sleep(400); return; }
    fail("pas necunoscut");
}

// ---- One capture -------------------------------------------------------------------------------------------------------------------------------------------
async function capture(cdp, topic, shot) {
    // "height N" in the steps: a taller window for a form or dialog that does not fit in 1080 pixels (the picture is as tall as the window).
    const tall = Number((shot.steps.find((step) => step.startsWith("height ")) || "").slice(7)) || minHeight;
    await cdp.send("Emulation.setDeviceMetricsOverride", { width, height: tall, deviceScaleFactor: 1, mobile: false });
    const anonymous = shot.steps.includes("anonymous");   // a page outside the application (Login): without the session
    const wholePage = shot.steps.includes("full") || shot.steps.every((step) => step === "anonymous");
    if (anonymous) await cdp.send("Network.clearBrowserCookies");
    await navigate(cdp, base + shot.url);
    let page = cdp;
    let popupTarget = null;
    for (const step of shot.steps.filter((step) => step !== "full" && step !== "anonymous" && !step.startsWith("height "))) {
        if (!step.startsWith("popup ")) { await runStep(page, step); continue; }
        // A link that the application opens in a window of its own (the session of the invoice lives as long as this page): the window is opened as a tab of the same browser.
        const argument = step.slice(6).trim();
        const href = await cdp.evaluate(`(() => {
            const els = ${JSON.stringify(argument)}.startsWith("sel:") ? HelpMatch.find({ sel: ${JSON.stringify(argument)}.slice(4) }) : HelpMatch.find({ name: ${JSON.stringify(argument)} });
            return els.length && els[0].getAttribute("href");
        })()`);
        if (!href) throw new Error(`pas "${step}": legatura nu exista`);
        const created = await cdp.send("Target.createTarget", { url: base + href });
        popupTarget = created.targetId;
        let socketUrl = null;
        for (let attempt = 0; attempt < 20 && !socketUrl; attempt++) {
            const list = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
            socketUrl = list.find((target) => target.id === popupTarget)?.webSocketDebuggerUrl;
            if (!socketUrl) await sleep(250);
        }
        page = new Cdp(socketUrl);
        await page.open();
        await page.send("Page.enable");
        await page.send("Runtime.enable");
        await page.send("Emulation.setDeviceMetricsOverride", { width, height: tall, deviceScaleFactor: 1, mobile: false });
        await sleep(3500);
        await page.evaluate(matcherSource);
    }
    await page.evaluate(`document.activeElement && document.activeElement.blur && document.activeElement.blur()`);

    // A page is taken whole (up to a limit); a window opened by a step is taken as it is on the screen.
    let height = tall;
    if (wholePage) {
        const measured = await page.evaluate(`Math.max(document.documentElement.scrollHeight, document.body ? document.body.scrollHeight : 0, ...Array.from(document.querySelectorAll("main, .app-shell, .content")).map((el) => el.scrollHeight))`);
        height = Math.min(maxHeight, Math.max(minHeight, Math.ceil(measured)));
        if (height !== minHeight) { await page.send("Emulation.setDeviceMetricsOverride", { width, height, deviceScaleFactor: 1, mobile: false }); await sleep(700); }
    }

    const mine = topic.elements.filter((element) => (element.img || topic.captures[0].id) === shot.id);
    const rects = {};
    const missing = [];
    for (const element of mine) {
        const boxes = await page.evaluate(`HelpMatch.boxes(${JSON.stringify({ name: element.name, kind: element.kind, icon: element.icon, sel: element.sel })}, ${!wholePage})`);
        const inside = boxes.filter((box) => box[2] > 1 && box[3] > 1 && box[0] < width && box[1] < height).map((box) => [Math.max(0, box[0]), Math.max(0, box[1]), Math.min(box[2], width - Math.max(0, box[0])), Math.min(box[3], height - Math.max(0, box[1]))]);
        if (inside.length) rects[element.n] = inside; else missing.push(element.name);
    }
    // A whole page is cut from the top of the document; what is on the screen (a window opened after scrolling) is taken as the window shows it.
    const shotData = await page.send("Page.captureScreenshot", wholePage ? { format: "jpeg", quality, clip: { x: 0, y: 0, width, height, scale: 1 } } : { format: "jpeg", quality });
    if (popupTarget) { page.socket?.close(); await cdp.send("Target.closeTarget", { targetId: popupTarget }).catch(() => {}); }
    if (anonymous) await signIn(cdp);
    return { jpeg: Buffer.from(shotData.data, "base64"), map: { w: width, h: height, rects }, missing, matched: Object.keys(rects).length, total: mine.length };
}

async function signIn(cdp) {
        const opened = cdp.once("Page.loadEventFired");
    await cdp.send("Page.navigate", { url: base + "/Account/Login" });
    await opened;
    await sleep(800);
    const hasForm = await cdp.evaluate(`!!document.querySelector('input[name="Username"]')`);
    if (hasForm) {
        const after = cdp.once("Page.loadEventFired", 60000);
        await cdp.evaluate(`(() => {
            document.querySelector('input[name="Username"]').value = ${JSON.stringify(login.Username)};
            document.querySelector('input[name="Password"]').value = ${JSON.stringify(login.Password)};
            document.querySelector('form button[type="submit"]').click();
        })()`);
        await after;
    }
    await sleep(1200);
    if ((await cdp.evaluate("location.pathname")).includes("Login")) throw new Error("Autentificarea a esuat (verifica local-secrets/test-login.private.json si ca instanta e cea de test).");
}

// ---- Main --------------------------------------------------------------------------------------------------------------------------------------------------
const login = JSON.parse(fs.readFileSync(path.join(repo, "local-secrets", "test-login.private.json"), "utf8"));
const { child, profile, url } = await launch();
const stopBrowser = () => spawnSync("taskkill", ["/pid", String(child.pid), "/T", "/F"], { stdio: "ignore" });
for (const event of ["unhandledRejection", "uncaughtException"]) process.on(event, (error) => { console.error(error?.message || error); stopBrowser(); process.exit(1); });
const cdp = new Cdp(url);
let failures = 0;
try {
    await cdp.open();
    await cdp.send("Page.enable");
    // A page with unsaved changes asks before it is left ("beforeunload"): in a capture the answer is always yes.
    cdp.on("Page.javascriptDialogOpening", () => { void cdp.send("Page.handleJavaScriptDialog", { accept: true }).catch(() => {}); });
    await cdp.send("Runtime.enable");
    await cdp.send("Emulation.setDeviceMetricsOverride", { width, height: minHeight, deviceScaleFactor: 1, mobile: false });

    await signIn(cdp);

    if (flag("prepare")) {   // once, after tests/BlazorStoc.HelpDemo: one notification is resolved (the topics of the resolved notifications are photographed from it)
        await navigate(cdp, base + "/notificari");
        const resolved = await cdp.evaluate(`document.body.textContent.includes("Rezolvate 1")`);
        if (!resolved) for (const step of ["click sel:tr.notification-row[aria-label*='Rovinieta'], tr.notification-row[aria-label*='Rovinietă']", "click Marchează ca rezolvată", "click Da, rezolvată", "wait 1500"]) await runStep(cdp, step);
        console.log("Pregatit: o notificare rezolvata.");
        // and one saved invoice template (made from the sample PDF written by: dotnet run --project tests/BlazorStoc.HelpDemo -- --files artifacts/help-demo-files)
        await navigate(cdp, base + "/setari");
        for (const step of ["click Facturi", "click Șabloane salvate"]) await runStep(cdp, step);
        if (!(await cdp.evaluate(`!!document.querySelector(".row-icon.edit")`))) {
            await navigate(cdp, base + "/setari");
            for (const step of ["click Facturi", "upload factura-demo.pdf", "click Salvează șablonul", "wait 3000"]) await runStep(cdp, step);
        }
        console.log("Pregatit: un sablon de factura salvat.");
        throw new Error("eval");
    }
    if (evalCode) {   // debugging: open --url and print the value of --eval
        await navigate(cdp, base + option("url", "/"));
        for (const step of option("steps", "").split(";").map((item) => item.trim()).filter(Boolean)) await runStep(cdp, step);
        console.log(JSON.stringify(await cdp.evaluate(evalCode), null, 1));
        const shotFile = option("shot", "");
        if (shotFile) {
            const data = await cdp.send("Page.captureScreenshot", { format: "png" });
            fs.writeFileSync(shotFile, Buffer.from(data.data, "base64"));
        }
        process.exitCode = 0;
        throw new Error("eval");
    }
    const topics = topicsFromFiles().filter((topic) => onlyTopics.length === 0 || onlyTopics.includes(topic.key));
    fs.mkdirSync(outDir, { recursive: true });
    let totalBytes = 0;
    for (const topic of topics) {
        for (const shot of topic.captures) {
            const name = `${topic.key}.${shot.id}`;
            if (missingOnly && fs.existsSync(path.join(outDir, name + ".jpg"))) continue;
            try {
                const result = await capture(cdp, topic, shot);
                totalBytes += result.jpeg.length;
                console.log(`${name}: ${result.matched}/${result.total} elemente, ${Math.round(result.jpeg.length / 1024)} KB, ${result.map.h}px` + (result.missing.length ? `  FARA LOC: ${result.missing.join(" | ")}` : ""));
                if (!reportOnly) {
                    fs.writeFileSync(path.join(outDir, name + ".jpg"), result.jpeg);
                    fs.writeFileSync(path.join(outDir, name + ".json"), JSON.stringify(result.map));
                }
            } catch (error) {
                failures++;
                console.log(`${name}: EROARE ${error.message}`);
            }
        }
    }
    console.log(`Total ${Math.round(totalBytes / 1024)} KB, ${failures} erori.`);
} catch (error) {
    if (error.message !== "eval") { console.error(error.message); failures++; }
} finally {
    try { cdp.socket?.close(); } catch { /* closed */ }
    stopBrowser();   // the browser and all its processes
    await sleep(500);
    try { fs.rmSync(profile, { recursive: true, force: true }); } catch { /* the browser still holds it */ }
}
process.exit(failures ? 1 : 0);
