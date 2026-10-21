// Generates the screenshots of the help topics (docs/PROPUNERE_HELP_IMAGINI.md): for every "Captura:" line of a .help file it opens the page in a
// headless Edge/Chrome at 1920x1080, does the steps of the line (a click that opens a window, a tab...), takes the picture and measures, with the same
// matcher the help window uses (wwwroot/help-match.js), where each element of the topic is. Output: Assets/Help/img/<key>.<id>.jpg and <key>.<id>.json.
// The topics are read from Assets/Help/*.help (no running help needed); the pictures are embedded at the next build.
//
// It runs against an instance with the DEMO database (fictive data), never against the real one:
//   1. stop the instance, then .\tools\help-demo-db.ps1                       (creates blazorstoc_demo from the schema of the test database)
//   2. dotnet run --project tests/BlazorStoc.HelpDemo -c Release              (fictive suppliers, products, beneficiaries, vehicles, projects...)
//   3. dotnet run --project tests/BlazorStoc.HelpDemo -c Release -- --files artifacts/help-demo-files   (sample PDF and XML invoices for the upload steps)
//   4. node tools/help-demo-updater.mjs init                                   (a fake installation + a stand-in for the updater: Setari -> Actualizare; also a saved port: Setari -> Baza de date)
//   5. .	oolshelp-demo-start.ps1  (port 5089; -Publish rebuilds artifactshelp-build) and .	oolshelp-demo-start.ps1 -Second (port 5090: the topic whose address starts with http://127.0.0.1:5090)
//   6. node tools/help-capture.mjs --prepare     (resolves one notification, saves templates, one backup, takes over an inventory and an offer, once)   then   dotnet run --project tests/BlazorStoc.HelpDemo -c Release -- --forms
//   7. node tools/help-capture.mjs --all                                       (or --topic produse-catalog,setari  /  --missing  /  --report [--suggest]: measure only, lists the elements without a frame)
// Scenarios that start an update (setari-actualizare.lucreaza) leave the announcement in the memory of the instance until the fake updater's status is reset (node tools/help-demo-updater.mjs reset).
// The user and the password are read from local-secrets/test-login.private.json (never printed); --base changes the address (default http://127.0.0.1:5089).
// Steps (";" separated, 3rd field of "Captura:"): node <script> <args> (a script of the repository) | click <text> | click sel:<css> | jsclick <text> | visit <text> (opens the address of a link) | popup <text> (a link that opens a window: taken in a second tab) | upload <file> | wait <ms> | type <css>=<value> | scroll <css> | hover <text> |
// select <css> = <option text> | key <Enter|Escape|Tab> | sql <statement on the demo database> | drag <css>=x1,y1,x2,y2 | block <pattern> / block off | reload | then (steps after it run after the picture is taken: they put back what the scenario changed) | full (the whole page, up to 3000 px) | height <px> (a taller window for a long form) | anonymous (without the session: Login).
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
const width = 1920, minHeight = 1080, maxHeight = 3600;
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
        "--headless=new", `--remote-debugging-port=${port}`, `--user-data-dir=${profile}`, "--hide-scrollbars", "--disable-popup-blocking", "--force-device-scale-factor=1",
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
        // "css=value": the first "=" ends the selector, except inside an attribute selector ("input[type=number]=3": the "=" after the "]").
        const bracket = /^(.*])=(.*)$/s.exec(argument);
        const [selector, ...valueParts] = bracket ? [bracket[1], bracket[2]] : argument.split("=");
        const value = valueParts.join("=").replace("{PASSWORD}", login.Password);   // {PASSWORD}: the password of the test login (never written in a .help file)
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
    if (verb === "select") {   // select <css> = <part of the option text>: chooses an option of a list (select element) and tells the page
        const [selector, ...textParts] = argument.split("=");
        const wanted = textParts.join("=").trim();
        const done = await cdp.evaluate(`(() => {
            const el = document.querySelector(${JSON.stringify(selector.trim())});
            if (!el || !el.options) return false;
            const index = Array.from(el.options).findIndex((option) => option.text.includes(${JSON.stringify(wanted)}));
            if (index < 0) return false;
            el.selectedIndex = index;
            el.dispatchEvent(new Event("input", { bubbles: true }));
            el.dispatchEvent(new Event("change", { bubbles: true }));
            return true;
        })()`);
        if (!done) fail("lista sau optiunea nu exista");
        await sleep(800);
        return;
    }
    if (verb === "key") {   // key Enter | Escape | Tab: a key pressed on the focused element
        await cdp.send("Input.dispatchKeyEvent", { type: "keyDown", key: argument, code: argument, windowsVirtualKeyCode: { Enter: 13, Escape: 27, Tab: 9 }[argument] || 0 });
        await cdp.send("Input.dispatchKeyEvent", { type: "keyUp", key: argument, code: argument, windowsVirtualKeyCode: { Enter: 13, Escape: 27, Tab: 9 }[argument] || 0 });
        await sleep(600);
        return;
    }
    if (verb === "sql") {   // sql <statement>: runs it on the DEMO database with the administrator account of the local MariaDB (a real fault for a picture: RENAME TABLE ..., put back after "then")
        const root = option("maria-root", "C:/Dev/BlazorStoc-MariaDB");
        const distribution = fs.readdirSync(root).filter((name) => /^mariadb-.*-winx64$/.test(name)).sort().pop();
        const run = spawnSync(path.join(root, distribution, "bin", "mariadb.exe"), ["--defaults-file=" + path.join(root, "admin.private.cnf"), "-D", "blazorstoc_demo", "-e", argument], { encoding: "utf8" });
        if (run.status !== 0) fail("sql: " + (run.stderr || "").trim().slice(0, 200));
        await sleep(300);
        return;
    }
    if (verb === "node") {   // node <script> <args>: runs a script of the repository (tools/help-demo-updater.mjs: the state of the fake installation for Setari -> Actualizare)
        const run = spawnSync(process.execPath, argument.split(/\s+/), { encoding: "utf8", cwd: path.resolve(path.dirname(new URL(import.meta.url).pathname.replace(/^\/(\w:)/, "$1")), "..") });
        if (run.status !== 0) fail("node: " + (run.stderr || run.stdout || "").trim().slice(0, 200));
        await sleep(300);
        return;
    }
    if (verb === "reload") { await navigate(cdp, await cdp.evaluate("location.href")); return; }   // the same address again (after sql: the page loads against the changed database)
    if (verb === "block") {   // block <url pattern> | block off: requests to those addresses fail (the map tiles: the page then shows what it does when the provider does not answer)
        await cdp.send("Network.enable");
        await cdp.send("Network.setBlockedURLs", { urls: argument === "off" ? [] : [argument] });
        return;
    }
    if (verb === "drag") {   // drag <css>=<x1>,<y1>,<x2>,<y2>: the mouse is dragged inside the element, the corners are fractions (0-1) of its box (a selection frame on the invoice page)
        const [selector, ...rest] = argument.split("=");
        const [x1, y1, x2, y2] = rest.join("=").split(",").map(Number);
        const box = await cdp.evaluate(`(() => { const el = document.querySelector(${JSON.stringify(selector.trim())}); if (!el) return null; el.scrollIntoView({ block: "center", behavior: "instant" }); const r = el.getBoundingClientRect(); return { x: r.left, y: r.top, w: r.width, h: r.height }; })()`);
        if (!box) fail("elementul nu exista");
        await sleep(400);
        const box2 = await cdp.evaluate(`(() => { const r = document.querySelector(${JSON.stringify(selector.trim())}).getBoundingClientRect(); return { x: r.left, y: r.top, w: r.width, h: r.height }; })()`);
        const at = (fx, fy) => ({ x: box2.x + box2.w * fx, y: box2.y + box2.h * fy });
        const start = at(x1, y1), end = at(x2, y2);
        await cdp.send("Input.dispatchMouseEvent", { type: "mouseMoved", x: start.x, y: start.y });
        await cdp.send("Input.dispatchMouseEvent", { type: "mousePressed", x: start.x, y: start.y, button: "left", clickCount: 1 });
        for (let step = 1; step <= 12; step++) await cdp.send("Input.dispatchMouseEvent", { type: "mouseMoved", x: start.x + (end.x - start.x) * step / 12, y: start.y + (end.y - start.y) * step / 12, button: "left", buttons: 1 });
        await cdp.send("Input.dispatchMouseEvent", { type: "mouseReleased", x: end.x, y: end.y, button: "left", clickCount: 1 });
        await sleep(900);
        return;
    }
    if (verb === "eval") {   // eval <js>: a script run in the page (to leave a state: restore what the steps changed in the demo data)
        await cdp.evaluate(argument);
        await sleep(500);
        return;
    }
    if (verb === "upload") {   // upload <file>: a sample from artifacts/help-demo-files (dotnet run --project tests/BlazorStoc.HelpDemo -- --files <dir>) goes into the first file field
        const [name, target] = argument.split(" in ");   // "upload file in <css>": the file field of that container (when the page has several)
        const file = path.join(repo, "artifacts", "help-demo-files", name.trim());
        if (!fs.existsSync(file)) fail("lipseste fisierul " + file);
        const document = await cdp.send("DOM.getDocument", { depth: 0 });
        const found = await cdp.send("DOM.querySelectorAll", { nodeId: document.root.nodeId, selector: target ? target.trim() + " input[type=file], input[type=file]" + target.trim() : "input[type=file]" });
        if (!found.nodeIds.length) fail("nu exista camp de fisier");
        await cdp.send("DOM.setFileInputFiles", { files: [file], nodeId: found.nodeIds[0] });
        await sleep(3500);
        return;
    }
    if (verb === "scroll") { await cdp.evaluate(`document.querySelector(${JSON.stringify(argument)})?.scrollIntoView({ block: "center", behavior: "instant" })`); await sleep(400); return; }
    fail("pas necunoscut");
}

// ---- One capture -------------------------------------------------------------------------------------------------------------------------------------------
async function capture(cdp, topic, fullShot) {
    try { return await captureOnce(cdp, topic, fullShot); }
    catch (error) {   // a scenario that failed halfway still puts back what it changed (a renamed table, a stopped module)
        const thenAt = fullShot.steps.indexOf("then");
        if (thenAt >= 0) for (const step of fullShot.steps.slice(thenAt + 1)) { try { await runStep(cdp, step); } catch { /* the demo database can be rebuilt */ } }
        throw error;
    }
}

async function captureOnce(cdp, topic, fullShot) {
    // The steps after "then" run after the picture is taken (they put back what the scenario changed in the demo data).
    const thenAt = fullShot.steps.indexOf("then");
    const shot = thenAt < 0 ? fullShot : { ...fullShot, steps: fullShot.steps.slice(0, thenAt) };
    const afterSteps = thenAt < 0 ? [] : fullShot.steps.slice(thenAt + 1);
    // "height N" in the steps: a taller window for a form or dialog that does not fit in 1080 pixels (the picture is as tall as the window).
    const tall = Number((shot.steps.find((step) => step.startsWith("height ")) || "").slice(7)) || minHeight;
    await runStep(cdp, "sql DELETE FROM product_locks");   // locks left by an earlier picture (the editor of a product keeps its lock for a while) must not show up in this one
    await cdp.send("Emulation.setDeviceMetricsOverride", { width, height: tall, deviceScaleFactor: 1, mobile: false });
    const anonymous = shot.steps.includes("anonymous");   // a page outside the application (Login): without the session
    const wholePage = shot.steps.includes("full") || shot.steps.every((step) => step === "anonymous");
    if (anonymous) await cdp.send("Network.clearBrowserCookies");
    if (/^https?:/.test(shot.url)) { await signIn(cdp, new URL(shot.url).origin); await navigate(cdp, shot.url); }   // another instance (a second demo on 5090 with another configuration)
    else await navigate(cdp, base + shot.url);
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

    if (await page.evaluate('typeof HelpMatch === "undefined"')) await page.evaluate(matcherSource);   // a step that submitted a form loaded a new page
    const mine = topic.elements.filter((element) => (element.img || topic.captures[0].id) === shot.id);
    const rects = {};
    const missing = [];
    for (const element of mine) {
        const boxes = await page.evaluate(`HelpMatch.boxes(${JSON.stringify({ name: element.name, kind: element.kind, icon: element.icon, sel: element.sel })}, ${!wholePage})`);
        const inside = boxes.filter((box) => box[2] > 1 && box[3] > 1 && box[0] < width && box[1] < height).map((box) => [Math.max(0, box[0]), Math.max(0, box[1]), Math.min(box[2], width - Math.max(0, box[0])), Math.min(box[3], height - Math.max(0, box[1]))]);
        if (inside.length) rects[element.n] = inside; else missing.push(element.name);
    }
    if (flag("suggest") && missing.length) {   // --suggest: for every element without a frame, the elements of the page whose text or label looks like its name (a start for "| sel: ...")
        for (const name of missing) {
            const found = await page.evaluate("(" + (function (wanted) {
                const norm = (t) => (t || "").normalize("NFD").replace(/[\u0300-\u036f]/g, "").toLowerCase().replace(/[*:←→]/g, "").replace(/\s+/g, " ").trim();
                const words = norm(wanted).split(/ \/ |, /).filter(Boolean);
                const visible = (el) => { const r = el.getBoundingClientRect(); return r.width > 1 && r.height > 1; };
                const pathOf = (el) => {
                    const part = (node) => node.tagName.toLowerCase() + (node.id && !/\d{3,}/.test(node.id) ? "#" + node.id : "") + Array.from(node.classList).filter((c) => !/^(valid|invalid|modified|on|off|open|active)$/.test(c)).slice(0, 2).map((c) => "." + c).join("");
                    let sel = part(el);
                    let count = document.querySelectorAll(sel).length;
                    if (count > 1 && el.parentElement) { sel = part(el.parentElement) + " > " + sel; count = document.querySelectorAll(sel).length; }
                    return sel + (count > 1 ? " (x" + count + ")" : "");
                };
                const out = [];
                for (const el of document.querySelectorAll("body *")) {
                    if (/^(SCRIPT|STYLE|PATH|SVG)$/i.test(el.tagName) || !visible(el)) continue;
                    const text = norm(el.textContent);
                    const label = norm(el.getAttribute("aria-label") || el.getAttribute("title") || el.getAttribute("placeholder") || "");
                    const hit = words.some((w) => w.length > 2 && ((text.length < w.length + 30 && text.includes(w)) || label.includes(w)));
                    if (hit) out.push(pathOf(el) + " \"" + (el.textContent || "").trim().replace(/\s+/g, " ").slice(0, 40) + "\"");
                }
                return Array.from(new Set(out)).slice(0, 5);
            }).toString() + ")(" + JSON.stringify(name) + ")");
            console.log("    ? " + name + " -> " + (found.length ? found.join("  ||  ") : "(nimic gasit)"));
        }
    }
    // A whole page is cut from the top of the document; what is on the screen (a window opened after scrolling) is taken as the window shows it.
    // The window of the browser is not in front: without a new frame the picture can still show the state before the last step (a section that was just opened).
    await page.send("Page.bringToFront").catch(() => {});
    await page.evaluate("new Promise((resolve) => { requestAnimationFrame(() => requestAnimationFrame(resolve)); setTimeout(resolve, 900); })");
    const shotData = await page.send("Page.captureScreenshot", wholePage ? { format: "jpeg", quality, clip: { x: 0, y: 0, width, height, scale: 1 } } : { format: "jpeg", quality });
    for (const step of afterSteps) { if (process.env.HELP_DEBUG) console.log("    then: " + step); await runStep(page, step); }
    if (popupTarget) { page.socket?.close(); await cdp.send("Target.closeTarget", { targetId: popupTarget }).catch(() => {}); }
    if (anonymous) await signIn(cdp);
    return { jpeg: Buffer.from(shotData.data, "base64"), map: { w: width, h: height, rects }, missing, matched: Object.keys(rects).length, total: mine.length };
}

async function signIn(cdp, origin = base) {
        const opened = cdp.once("Page.loadEventFired");
    await cdp.send("Page.navigate", { url: origin + "/Account/Login" });
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
            for (const step of ["click Facturi", "wait 800", "upload factura-demo.pdf", "wait 1500", "jsclick sel:details.invoice-section:nth-of-type(5) summary", "wait 600", "jsclick sel:details.invoice-section[open] button.primary", "wait 3500"]) await runStep(cdp, step);
        }
        console.log("Pregatit: un sablon de factura salvat.");
        // the copy on a NAS was configured and ran once (the page shows the last operation); the UNC path is built with CHAR(92) (a backslash)
        await runStep(cdp, "sql REPLACE INTO backup_nas_settings (id, unc_path, username, password_protected, copy_enabled, schedule_enabled, schedule_time, last_attempt_utc, last_ok, last_message, updated_by, updated_utc, max_age_days) VALUES (1, CONCAT(CHAR(92), CHAR(92), 'nas-depozit', CHAR(92), 'backup-blazorstoc'), 'backup', NULL, 1, 0, '02:00', DATE_FORMAT(UTC_TIMESTAMP(3), '%Y-%m-%dT%H:%i:%s.000Z'), 1, 'Au fost copiate 2 pachete.', 'claude.test', DATE_FORMAT(UTC_TIMESTAMP(3), '%Y-%m-%dT%H:%i:%s.000Z'), 2)");
        console.log("Pregatit: copierea pe NAS configurata.");
        // one backup on demand
        await navigate(cdp, base + "/setari");
        for (const step of ["click Backup", "wait 1500"]) await runStep(cdp, step);
        if (await cdp.evaluate(`document.body.innerText.includes("Niciun pachet de siguranță")`)) for (const step of ["click Fă backup acum", "wait 12000"]) await runStep(cdp, step);
        console.log("Pregatit: un backup la cerere.");
        // the small inventory form is taken over (one product counted over): it leaves an inventory package and an entry, so the form without differences is really without differences
        await navigate(cdp, base + "/inventar/preluare");
        await runStep(cdp, "upload inventar-mic.pdf");
        await sleep(20000);
        if (await cdp.evaluate(`!!document.querySelector("button.row-select[aria-label*='CIT-BIO']")`)) {
            for (const step of ["jsclick sel:button.row-select[aria-label*='CIT-BIO']", "jsclick Trimite modificări în stoc", "wait 1200", "click Confirmă", "wait 25000"]) await runStep(cdp, step);
        }
        console.log("Pregatit: inventarul mic a fost preluat.");
        // one offer template, made from the sample workbook (the sheet, the columns and the sections are proposed from the file)
        await navigate(cdp, base + "/oferte/sabloane");
        if (!(await cdp.evaluate(`!!document.querySelector(".row-icon.edit")`))) {
            for (const step of ["click Șablon nou", "wait 1500", "upload oferta-demo.xlsx", "wait 3500", "type #offer-template-name=Deviz Instalări Securitate Demo", "click Salvează șablonul", "wait 3000"]) await runStep(cdp, step);
        }
        console.log("Pregatit: un sablon de oferta.");
        // the sample offer is taken over once: its number exists afterwards, so the pictures show a revision (the message of the first step, the differences of the summary)
        await navigate(cdp, base + "/oferte/preluare");
        await runStep(cdp, "upload oferta-demo.xlsx");
        await sleep(4000);
        const taken = await cdp.evaluate(`document.body.innerText.includes("revizia")`);
        if (!taken) {
            for (const step of ["click Pasul următor", "wait 1800", "click Pasul următor", "wait 1800", "click Pasul următor", "wait 1800", "click Pasul următor", "wait 1800", "click Preia oferta", "wait 5000"]) await runStep(cdp, step);
        }
        console.log("Pregatit: oferta de proba a fost preluata.");
        throw new Error("eval");
    }
    if (evalCode) {   // debugging: open --url and print the value of --eval
        const evalSteps = option("steps", "").split(";").map((item) => item.trim()).filter(Boolean);
        if (evalSteps.includes("anonymous")) await cdp.send("Network.clearBrowserCookies");
        await navigate(cdp, base + option("url", "/"));
        for (const step of evalSteps.filter((item) => item !== "anonymous")) await runStep(cdp, step);
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
