// Ruleaza testele de utilizator din "Teste utilizator/*.md": genereaza o pagina de lucru cu pasii nerezolvati si trece rezultatele inapoi in fisiere.
//
//   node tools/teste-utilizator.mjs list                  numara pasii nerezolvati pe fisier
//   node tools/teste-utilizator.mjs html [--site] [fisier ...]  scrie artifacts/teste-utilizator.html (toate fisierele sau cele date, dupa prefix: 18, 06); cu --site scrie wwwroot/teste/teste-utilizator.html,
//                                                        care ajunge in aplicatie la urmatorul pachet si se deschide la /teste/teste-utilizator.html
//   node tools/teste-utilizator.mjs apply rezultate.json  scrie rezultatele (exportate din pagina) in coloana "Rezultat" a fisierelor
//
// Un pas este orice rand de tabel cu antetul care se termina in "Rezultat"; e nerezolvat daca celula Rezultat este goala.
// Pagina tine starea in browser (localStorage), arata progresul si exporta un JSON [{file, line, status, note}] pe care il aplici cu "apply".
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const dir = path.join(root, 'Teste utilizator');

const cells = line => line.trim().replace(/^\|/, '').replace(/\|$/, '').split('|').map(cell => cell.trim());

function parseFile(name) {
    const text = fs.readFileSync(path.join(dir, name), 'utf8');
    const lines = text.split(/\r?\n/);
    const steps = [];
    let section = '', header = null;
    lines.forEach((line, index) => {
        if (/^#{1,3} /.test(line)) { section = line.replace(/^#+\s*/, ''); header = null; return; }
        if (!line.trim().startsWith('|')) { header = null; return; }
        const parts = cells(line);
        if (header === null) { header = /rezultat$/i.test(parts[parts.length - 1]) ? parts : false; return; }
        if (header === false || /^-+$/.test(parts[0].replace(/:/g, ''))) return;
        if (parts.length !== header.length) return;
        steps.push({ file: name, line: index + 1, section, nr: parts[0], step: parts.slice(1, -2).join(' | ') || parts[1], expected: parts[parts.length - 2], result: parts[parts.length - 1] });
    });
    return steps;
}

const files = () => fs.readdirSync(dir).filter(name => /^\d\d_.*\.md$/.test(name)).sort();
const all = () => files().flatMap(parseFile);

function list() {
    for (const name of files()) {
        const steps = parseFile(name);
        const open = steps.filter(step => step.result === '').length;
        if (steps.length) console.log(`${name}: ${open} nerezolvati din ${steps.length}`);
    }
}

function html(args) {
    const site = args.includes('--site');
    const filter = args.filter(arg => arg !== '--site');
    let steps = all();
    if (filter.length) steps = steps.filter(step => filter.some(prefix => step.file.startsWith(prefix)));
    steps = steps.filter(step => step.result === '');
    const out = site ? path.join(root, 'wwwroot', 'teste', 'teste-utilizator.html') : path.join(root, 'artifacts', 'teste-utilizator.html');
    fs.mkdirSync(path.dirname(out), { recursive: true });
    const data = JSON.stringify(steps).replace(/</g, '\\u003c');
    fs.writeFileSync(out, `<!doctype html><html lang="ro"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Teste utilizator</title>
<style>
:root{--bg:#f5f7f6;--card:#fff;--ink:#192d31;--muted:#748387;--line:#e4eae8;--ok:#13796a;--bad:#b3261e;--skip:#8a6d00}
@media(prefers-color-scheme:dark){:root{--bg:#10191b;--card:#172326;--ink:#e6efee;--muted:#93a5a8;--line:#27393c;--ok:#4cc2ac;--bad:#ff8a80;--skip:#e2c25a}}
body{margin:0;background:var(--bg);color:var(--ink);font:15px/1.5 system-ui,Segoe UI,sans-serif}
main{max-width:900px;margin:0 auto;padding:16px}
header.top{position:sticky;top:0;background:var(--bg);padding:12px 0;border-bottom:1px solid var(--line);z-index:2}
h1{font-size:20px;margin:0 0 6px}.bar{height:8px;background:var(--line);border-radius:4px;overflow:hidden}.bar>i{display:block;height:100%;background:var(--ok);width:0}
.tools{display:flex;gap:8px;flex-wrap:wrap;margin-top:8px;align-items:center}
button,select{font:inherit;padding:6px 12px;border:1px solid var(--line);border-radius:8px;background:var(--card);color:var(--ink);cursor:pointer}
button.main{background:var(--ok);color:#fff;border-color:var(--ok)}
h2{font-size:15px;margin:22px 0 8px;color:var(--muted)}
.step{background:var(--card);border:1px solid var(--line);border-radius:10px;padding:12px 14px;margin:8px 0}
.step.done{opacity:.6}.step b{font-weight:650}.exp{color:var(--muted);margin:4px 0 8px}
.act{display:flex;gap:6px;flex-wrap:wrap;align-items:center}.act label{display:inline-flex;gap:4px;align-items:center;padding:4px 10px;border:1px solid var(--line);border-radius:8px;cursor:pointer}
.act input[type=radio]{margin:0}.act .ok:has(input:checked){border-color:var(--ok);color:var(--ok)}.act .bad:has(input:checked){border-color:var(--bad);color:var(--bad)}.act .skip:has(input:checked){border-color:var(--skip);color:var(--skip)}
.note{flex:1 1 220px;padding:6px 10px;border:1px solid var(--line);border-radius:8px;background:var(--card);color:var(--ink);font:inherit}
</style></head><body><main>
<header class="top"><h1>Teste utilizator <span id="count" style="color:var(--muted);font-weight:400"></span></h1><div class="bar"><i id="fill"></i></div>
<div class="tools"><select id="filter" aria-label="Fisier"></select><label><input type="checkbox" id="hide"> ascunde rezolvate</label><button class="main" id="export">Exporta rezultate</button><span id="msg" role="status" style="color:var(--muted)"></span></div></header>
<div id="list"></div></main>
<script>
const steps=${data};
const KEY='teste-utilizator-v1';
let state={};try{state=JSON.parse(localStorage.getItem(KEY)||'{}')}catch{}
const id=s=>s.file+':'+s.line;
const save=()=>{try{localStorage.setItem(KEY,JSON.stringify(state))}catch{}};
const esc=t=>String(t).replace(/[&<>"]/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;'}[c]));
const filter=document.getElementById('filter');
['(toate)',...new Set(steps.map(s=>s.file))].forEach(f=>filter.add(new Option(f,f)));
function render(){
  const f=filter.value,hide=document.getElementById('hide').checked;let html='',last='',done=0;
  for(const s of steps){const st=state[id(s)]||{};if(st.status)done++;
    if(f!=='(toate)'&&s.file!==f)continue;if(hide&&st.status)continue;
    const head=s.file+' - '+s.section;if(head!==last){html+='<h2>'+esc(head)+'</h2>';last=head}
    html+='<div class="step'+(st.status?' done':'')+'" data-id="'+esc(id(s))+'"><b>'+esc(s.nr)+'.</b> '+esc(s.step)+'<div class="exp">Asteptat: '+esc(s.expected)+'</div><div class="act">'+
      [['ok','OK'],['bad','Esuat'],['skip','Sarit']].map(([v,t])=>'<label class="'+v+'"><input type="radio" name="r'+esc(id(s))+'" value="'+v+'"'+(st.status===v?' checked':'')+'> '+t+'</label>').join('')+
      '<input class="note" placeholder="Nota (la Esuat: ce s-a intamplat)" value="'+esc(st.note||'')+'"></div></div>';}
  document.getElementById('list').innerHTML=html||'<p>Nimic de afisat.</p>';
  document.getElementById('count').textContent=done+' / '+steps.length;
  document.getElementById('fill').style.width=(steps.length?100*done/steps.length:0)+'%';
}
document.getElementById('list').addEventListener('change',e=>{const card=e.target.closest('.step');if(!card)return;const k=card.dataset.id;state[k]=state[k]||{};
  if(e.target.type==='radio')state[k].status=e.target.value;else state[k].note=e.target.value;save();if(e.target.type==='radio')render();});
document.getElementById('list').addEventListener('input',e=>{if(e.target.classList.contains('note')){const k=e.target.closest('.step').dataset.id;state[k]=state[k]||{};state[k].note=e.target.value;save();}});
filter.onchange=render;document.getElementById('hide').onchange=render;
document.getElementById('export').onclick=async()=>{
  const rows=steps.filter(s=>(state[id(s)]||{}).status).map(s=>({file:s.file,line:s.line,status:state[id(s)].status,note:state[id(s)].note||''}));
  const text=JSON.stringify(rows,null,1);const blob=new Blob([text],{type:'application/json'});const a=document.createElement('a');a.href=URL.createObjectURL(blob);a.download='rezultate-teste.json';a.click();
  let copied=false;try{await navigator.clipboard.writeText(text);copied=true}catch{}
  document.getElementById('msg').textContent=rows.length+' rezultate exportate'+(copied?' (si copiate)':'')+'. Da-mi fisierul sau textul, ca sa le trec in fisiere.';};
render();
</script></body></html>`);
    console.log(`${out}: ${steps.length} pasi nerezolvati`);
}

function apply(jsonPath) {
    const rows = JSON.parse(fs.readFileSync(jsonPath, 'utf8'));
    const label = { ok: 'OK', bad: 'ESUAT', skip: 'SARIT' };
    const byFile = new Map();
    for (const row of rows) { if (!byFile.has(row.file)) byFile.set(row.file, []); byFile.get(row.file).push(row); }
    let written = 0;
    for (const [name, list] of byFile) {
        const file = path.join(dir, name);
        const raw = fs.readFileSync(file, 'utf8');
        const eol = raw.includes('\r\n') ? '\r\n' : '\n';
        const lines = raw.split(/\r?\n/);
        for (const row of list) {
            const line = lines[row.line - 1];
            if (!line || !line.trim().startsWith('|')) { console.warn(`${name}:${row.line} nu mai este un rand de tabel, sarit`); continue; }
            const parts = cells(line);
            const note = (row.note || '').replace(/\|/g, '/').replace(/\s+/g, ' ').trim();
            parts[parts.length - 1] = label[row.status] + (note ? ' - ' + note : '') + ' (' + new Date().toLocaleDateString('ro-RO') + ')';
            lines[row.line - 1] = '| ' + parts.join(' | ') + ' |';
            written++;
        }
        fs.writeFileSync(file, lines.join(eol));
    }
    console.log(`${written} rezultate scrise in ${byFile.size} fisiere`);
}

const [command, ...rest] = process.argv.slice(2);
if (command === 'list') list();
else if (command === 'html') html(rest);
else if (command === 'apply' && rest[0]) apply(rest[0]);
else console.log('Folosire: node tools/teste-utilizator.mjs list | html [prefix ...] | apply rezultate.json');
