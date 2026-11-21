// Browser scan of the open windows (BUG-0016): on a page, paste this file in the console (or serve it) and run `await __scan(15)`: it opens each "add / edit / open" button, measures the gap between the bar of the window and its first content and whether the help icon is in the bar, then closes the window. A gap above ~20 px or `h:false` is a window to look at.
window.__scan = async (limit = 14) => {
  const sleep = ms => new Promise(r => setTimeout(r, ms));
  const gap = () => Array.from(document.querySelectorAll('.app-dialog-panel')).filter(p => p.getBoundingClientRect().height > 0).map(p => {
    const bar = p.querySelector(':scope > .app-dialog-grip, :scope > .app-dialog-head');
    const body = p.querySelector(':scope > .app-dialog-body') || p;
    const barBottom = bar ? bar.getBoundingClientRect().bottom : p.getBoundingClientRect().top;
    let first = null, ft = 1e9;
    for (const el of body.querySelectorAll('*')) {
      const r = el.getBoundingClientRect(); if (r.width < 4 || r.height < 4) continue;
      const cs = getComputedStyle(el); if (cs.visibility === 'hidden' || cs.display === 'none') continue;
      const own = Array.from(el.childNodes).some(n => n.nodeType === 3 && n.textContent.trim()) || /^(INPUT|SELECT|TEXTAREA|BUTTON|IMG|TABLE)$/i.test(el.tagName);
      if (own && r.top < ft) { ft = r.top; first = el; }
    }
    return { t: (bar && bar.textContent.trim().slice(0, 40)) || p.getAttribute('aria-label') || '?', g: first ? Math.round(ft - barBottom) : null, f: first ? first.tagName + ':' + first.textContent.trim().slice(0, 20) : null, bar: !!bar, h: !!(bar && bar.querySelector('.help-icon')), dup: Array.from(document.querySelectorAll('.app-dialog-panel')).filter(x=>x.getBoundingClientRect().height>0).reduce((n,x)=>n+x.querySelectorAll('.help-icon').length,0) };
  });
  const okText = /(adaug|creeaz|editeaz|modific|nou\b|nouă|\+ |deschide|vezi|detalii|setari|alege|schimb|redenum|reînnoiește|istoric|mută|preia|import|propune|raportează)/i;
  const bad = /(șterge|sterge|elimin|export|descarc|deconect|ieși din|salvează|trimite|confirm|arhiv|anulează|închide|restaur)/i;
  const vis = () => Array.from(document.querySelectorAll('.app-dialog-panel')).filter(x => x.getBoundingClientRect().height > 0).length;
  const out = [];
  const label = b => (b.getAttribute('aria-label') || b.title || b.textContent || '').trim();
  const cands = () => Array.from(document.querySelectorAll('main button, .app-shell button, button')).filter(b => !b.disabled && b.getBoundingClientRect().height > 0 && !b.closest('.app-dialog-panel, .sidebar, .topbar, nav') && okText.test(label(b)) && !bad.test(label(b)));
  const seen = new Set();
  for (let i = 0; i < limit; i++) {
    const b = cands().find(x => !seen.has(label(x)));
    if (!b) break;
    seen.add(label(b));
    b.click(); await sleep(1100);
    const w = gap();
    if (w.length) out.push({ btn: label(b).slice(0, 40), w });
    for (let k = 0; k < 3 && vis(); k++) {
      const lv = Array.from(document.querySelectorAll('.app-dialog-panel button')).filter(x => x.getBoundingClientRect().height > 0 && /(părăsește|renunț|fără salvare|abandon)/i.test(x.textContent)).pop(); if (lv) { lv.click(); await sleep(600); continue; }
      const c = Array.from(document.querySelectorAll('[data-app-dialog-close]')).filter(x => x.getBoundingClientRect().height > 0).pop(); if (c) c.click(); else document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
      await sleep(500);
    }
    if (vis()) { out.push({ stuck: label(b) }); break; }
  }
  return out;
};
