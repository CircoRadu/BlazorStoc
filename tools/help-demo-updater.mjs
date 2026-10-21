// A stand-in for the updater, only for the help pictures of Setari -> Actualizare (see the header of tools/help-capture.mjs).
//   node tools/help-demo-updater.mjs init      makes the fake installation (artifacts/help-demo-install), two sample packages in artifacts/help-demo-files and the scheduled task
//   node tools/help-demo-updater.mjs run       what the scheduled task runs: answers the check requests of the application and starts the "update" (it only writes its status)
//   node tools/help-demo-updater.mjs working   writes a status of an update in progress (phase: backup)
//   node tools/help-demo-updater.mjs failure   appends a rolled-back update as the last entry of the history (the red band of the page)
//   node tools/help-demo-updater.mjs unmanaged  hides the releases folder: the page then says that the application is not installed (reset puts it back)
//   node tools/help-demo-updater.mjs reset     back to the quiet state: status done, no failure entry, staged files removed
// The demo instance is started with App__InstallRoot=artifacts/help-demo-install (scratchpad start-demo.ps1); without it the page says that the application is not installed.
import fs from "node:fs";
import path from "node:path";
import crypto from "node:crypto";
import { execFileSync } from "node:child_process";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const install = path.join(root, "artifacts", "help-demo-install");
const state = path.join(install, "state");
const staged = path.join(install, "updates", "staged");
const files = path.join(root, "artifacts", "help-demo-files");
const dataRoot = path.join(root, "artifacts", "help-demo-data");
const history = path.join(state, "update-history.jsonl");
const statusFile = path.join(state, "update-status.json");
const now = () => new Date().toISOString();
const ago = (days, hours = 0) => new Date(Date.now() - days * 86400000 - hours * 3600000).toISOString();

// The name of the scheduled task, the same rule as UpdateInstallation.TaskNameFor.
function taskName() {
    const normalised = path.resolve(install).replace(/[\\/]+$/, "").toLowerCase();
    return "BlazorStocUpdater-" + crypto.createHash("sha256").update(normalised, "utf8").digest("hex").slice(0, 8);
}

const entries = () => (fs.existsSync(history) ? fs.readFileSync(history, "utf8").split("\n").filter((line) => line.trim()).map((line) => JSON.parse(line)) : []);
const writeEntries = (list) => fs.writeFileSync(history, list.map((entry) => JSON.stringify(entry)).join("\n") + "\n");
const writeStatus = (phase, extra = {}) => fs.writeFileSync(statusFile, JSON.stringify({ phase, from: "0.5.2", to: "0.5.3", actor: "admin", reason: "Corecții din versiunea 0.5.3", startedUtc: now(), updatedUtc: now(), outcome: null, message: "", backup: null, ...extra }, null, 2));

// A sample package: an "archive" only by its first two bytes; the fake updater reads the lines after them.
function samplePackage(name, lines) { fs.writeFileSync(path.join(files, name), "PK\u0003\u0004\n" + lines.join("\n") + "\n" + "x".repeat(900000)); }

function check(request) {
    const file = path.join(staged, request.package);
    const bytes = fs.readFileSync(file);
    const text = bytes.subarray(0, 4096).toString("latin1");
    const value = (key) => text.match(new RegExp("^" + key + "=(.*)$", "m"))?.[1];
    const good = value("kind") === "ok";
    const report = {
        id: request.id, package: request.package, sha256: crypto.createHash("sha256").update(bytes).digest("hex"), sizeBytes: bytes.length, ok: value("kind") !== "invalid",
        problems: [], version: value("version") ?? null, running: "0.5.2", schemaVersion: 41, runningSchemaVersion: 40, newMigrations: good ? 1 : 0, builtUtc: ago(1),
        minFromVersion: good ? "0.5.0" : null, signer: "5de2344e9b61c1f0a4d37c82e56b09aa17f3d4c68b25e0c1943f7a82d6b0c5e3", trusted: true, canInstall: good, blocking: good ? null : "version",
        detail: good ? null : "Versiunea " + (value("version") ?? "?") + " nu este mai nouă decât cea instalată (0.5.2): nu se instalează.", fileCount: 1520, totalBytes: 96000000,
        notes: good ? "Preluarea ofertelor: mesajul de revizie și diferențele față de revizia anterioară.\nSetări → Aplicație: portul propriu rămâne valabil la schimbarea interfeței." : null, checkedUtc: now()
    };
    fs.writeFileSync(path.join(state, "check-" + request.id + ".json"), JSON.stringify(report, null, 2));
}

const command = process.argv[2];
if (command === "init") {
    for (const version of ["0.5.0", "0.5.1", "0.5.2"]) {
        const app = path.join(install, "releases", version, "app");
        fs.mkdirSync(app, { recursive: true });
        fs.writeFileSync(path.join(app, "BlazorStoc.dll"), "demo");
    }
    fs.mkdirSync(state, { recursive: true });
    fs.mkdirSync(staged, { recursive: true });
    fs.writeFileSync(path.join(install, "current.txt"), "0.5.2\n");
    fs.writeFileSync(path.join(install, "previous.txt"), "0.5.1\n");
    writeEntries([
        { atUtc: ago(40), from: "0.4.8", to: "0.4.9", outcome: "Succeeded", actor: "installer", reason: "Instalare inițială", signer: null, backup: null, seconds: 96, message: "Versiunea 0.4.9 funcționează.", package: null, via: "installer", kind: "install" },
        { atUtc: ago(18), from: "0.4.9", to: "0.5.0", outcome: "Succeeded", actor: "admin", reason: "Funcția de actualizare din aplicație", signer: "5de2344e9b61c1f0", backup: "backup-0.4.9.zip", seconds: 71, message: "Versiunea 0.5.0 funcționează.", package: "9a4f0c11", via: "aplicatie", kind: "install" },
        { atUtc: ago(9), from: "0.5.0", to: "0.5.1", outcome: "Refused", actor: "admin", reason: "Încercare cu un pachet modificat", signer: null, backup: null, seconds: 3, message: "Sumele de control nu corespund: pachetul a fost modificat după semnare.", package: "c2b77e90", via: "aplicatie", kind: "install" },
        { atUtc: ago(8, 2), from: "0.5.0", to: "0.5.1", outcome: "Succeeded", actor: "admin", reason: "Corecția selecției Interfață", signer: "5de2344e9b61c1f0", backup: "backup-0.5.0.zip", seconds: 84, message: "Versiunea 0.5.1 funcționează.", package: "1fd02a6b", via: "aplicatie", kind: "install" },
        { atUtc: ago(2), from: "0.5.1", to: "0.5.2", outcome: "Succeeded", actor: "admin", reason: "Selecția Interfață și portul propriu", signer: "5de2344e9b61c1f0", backup: "backup-0.5.1.zip", seconds: 79, message: "Versiunea 0.5.2 funcționează.", package: "e83a5d47", via: "aplicatie", kind: "install" }
    ]);
    writeStatus("done", { from: "0.5.1", to: "0.5.2", startedUtc: ago(2), updatedUtc: ago(2), outcome: "Succeeded", message: "Versiunea 0.5.2 funcționează." });
    fs.mkdirSync(files, { recursive: true });
    samplePackage("actualizare-noua.bsu", ["kind=ok", "version=0.5.3"]);
    samplePackage("actualizare-veche.bsu", ["kind=old", "version=0.5.1"]);
    // the entries above were never written to the journal of activity: the marker says they were, so the application does not add them to the Jurnal pictures
    fs.mkdirSync(dataRoot, { recursive: true });
    fs.writeFileSync(path.join(dataRoot, "update-journal.json"), JSON.stringify({ lastJournaledUtc: now(), acknowledgedUtc: null }));
    // a saved port of the application (the same as the running one): Setari -> Baza de date shows "Revino la configuratia de baza", which exists only when something is saved
    fs.writeFileSync(path.join(dataRoot, "database-override.json"), JSON.stringify({ appPort: 5089, appBindAll: false }));
    try { execFileSync("schtasks.exe", ["/Delete", "/TN", taskName(), "/F"], { stdio: "ignore" }); } catch { /* there was none */ }
    execFileSync("schtasks.exe", ["/Create", "/TN", taskName(), "/TR", `"${process.execPath}" "${fileURLToPath(import.meta.url)}" run`, "/SC", "ONCE", "/ST", "23:59", "/F"], { stdio: "inherit" });
    console.log("Gata. Porneste aplicatia demo cu App__InstallRoot=" + install);
} else if (command === "run") {
    for (const name of fs.existsSync(staged) ? fs.readdirSync(staged).filter((item) => /^request-.*\.json$/.test(item)) : []) {
        const request = JSON.parse(fs.readFileSync(path.join(staged, name), "utf8"));
        if (request.kind === "check") { check(request); fs.unlinkSync(path.join(staged, name)); }
        else if (request.kind === "apply") { writeStatus("backup", { actor: request.actor, reason: request.reason }); fs.unlinkSync(path.join(staged, name)); }
    }
} else if (command === "working") writeStatus("backup");
else if (command === "failure") {
    writeEntries([...entries().filter((entry) => entry.message !== "Versiunea 0.5.3 nu a pornit: s-a revenit la 0.5.2."), { atUtc: now(), from: "0.5.2", to: "0.5.3", outcome: "RolledBack", actor: "admin", reason: "Corecții din versiunea 0.5.3", signer: "5de2344e9b61c1f0", backup: "backup-0.5.2.zip", seconds: 142, message: "Versiunea 0.5.3 nu a pornit: s-a revenit la 0.5.2.", package: "7b10c9d2", via: "aplicatie", kind: "install" }]);
} else if (command === "unmanaged") fs.renameSync(path.join(install, "releases"), path.join(install, "releases.off"));
else if (command === "reset") {
    if (fs.existsSync(path.join(install, "releases.off"))) fs.renameSync(path.join(install, "releases.off"), path.join(install, "releases"));
    writeEntries(entries().filter((entry) => entry.message !== "Versiunea 0.5.3 nu a pornit: s-a revenit la 0.5.2."));
    writeStatus("done", { from: "0.5.1", to: "0.5.2", startedUtc: ago(2), updatedUtc: now(), outcome: "Succeeded", message: "Versiunea 0.5.2 funcționează." });
    for (const item of fs.existsSync(staged) ? fs.readdirSync(staged) : []) fs.rmSync(path.join(staged, item), { force: true });
    for (const item of fs.readdirSync(state).filter((entry) => entry.startsWith("check-"))) fs.rmSync(path.join(state, item), { force: true });
    fs.writeFileSync(path.join(dataRoot, "update-journal.json"), JSON.stringify({ lastJournaledUtc: now(), acknowledgedUtc: null }));
} else console.log("init | run | working | failure | reset");
