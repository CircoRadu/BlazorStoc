# Propunere: acces HTTPS din orice loc (stoc.esp-solutions.ro)

Stare: propunere, neimplementata. Fisier scris fara diacritice (regula `.md`).

## 1. Cerinta si mediul

- Serverul trebuie sa fie accesibil de oriunde, nu doar din LAN, prin HTTPS.
- Mediu: Windows 11, IP public fix, domeniu disponibil `esp-solutions.ro` (site-ul `www.esp-solutions.ro` ramane neatins; se foloseste un subdomeniu, propus `stoc.esp-solutions.ro`).
- Aplicatia ruleaza direct cu .NET (fara Docker/QNAP in faza curenta); MariaDB ramane local.

## 2. Situatia actuala (analizata in cod)

- Doar HTTP: `tools/start-preview.ps1:74` (`http://127.0.0.1:<port>` sau `0.0.0.0`), `AppHosting` (`Services/DatabaseConnectionSettings.cs:268-289`), `launchSettings.json` (`http://localhost:5080`). Nu exista `UseHttpsRedirection`, `UseHsts`, `UseForwardedHeaders` sau certificate.
- Cookie-ul `BlazorStoc.Session` (`Startup/ServiceRegistration.cs:38-40`) are `HttpOnly` si `SameSite=Lax`, fara `Secure`.
- `AllowedHosts` este `*` (`appsettings.json`).
- Limitarea de rata la login (`LoginRateLimit.Partition`) se face pe IP; in spatele unui proxy ar vedea doar `127.0.0.1`.
- `navigator.clipboard.read` (butonul de preluare imagine din `ProductEditor.razor`, `wwwroot/product-image.js`) cere context securizat: nu merge pe `http://<ip>`.
- Deja HTTPS/TLS: conexiunea la MariaDB (`SslMode: Required`), ANAF, OpenStreetMap, NTP.

## 3. Solutia propusa

**Reverse proxy Caddy cu certificat Let's Encrypt**, aplicatia ramane pe `127.0.0.1`:

```
stoc.esp-solutions.ro {
    reverse_proxy 127.0.0.1:<port aplicatie>
}
```

- Caddy asculta pe 80 (validarea certificatului si redirectul) si 443, obtine si reinnoieste singur certificatul, lasa sa treaca WebSocket-urile circuitului Blazor.
- Doar Caddy este expus; portul aplicatiei si MariaDB raman locale (optiunea „Doar acest calculator" din Setari -> Baza de date).
- Alternative respinse: certificat propriu/autosemnat sau CA locala (nu e acceptat de browserele din exterior); Kestrel direct cu PFX (reinnoire manuala, aplicatia expusa direct).

## 4. Pasi in afara codului (utilizatorul)

1. DNS la `esp-solutions.ro`: inregistrare **A** `stoc` -> IP-ul public fix. Nicio alta inregistrare nu se modifica.
2. Router: redirectionare porturi 80 si 443 spre calculatorul Windows 11.
3. Firewall Windows: 80 si 443 permise spre exterior; portul aplicatiei si portul MariaDB nu.
4. Verificare ca ISP-ul nu blocheaza 80/443 (test din exterior).
5. Instalarea Caddy ca serviciu cu scriptul din sectiunea 5.

## 5. Modificari de cod si documentatie

1. `Program.cs`: `UseForwardedHeaders` (`X-Forwarded-For`, `X-Forwarded-Proto`), cu proxy de incredere doar pe loopback; `UseHsts` cand adresa publica este configurata. Fara `UseHttpsRedirection` in aplicatie (redirectul 80 -> 443 il face Caddy); `/health*` ramane pe HTTP local pentru supervizor.
2. `Startup/ServiceRegistration.cs`: `Cookie.SecurePolicy = SameAsRequest` (cu forwarded headers, cererile prin proxy sunt HTTPS, deci cookie `Secure`; preview-ul local pe HTTP ramane functional).
3. `AllowedHosts` limitat la adresa publica cand aceasta este setata (setare `Hosting:PublicHost`).
4. Setari -> Aplicatie (`ApplicationPanel.razor`): camp „Adresa publica" (host), salvat in fisierul de override ca portul, cu validare si jurnalizare detaliata (actiune noua in `AuditActions`, filtrul paginii Audit, `IsCreateOrEdit`, test pe jurnal; detalii cu valoarea veche si noua). Folosit la linkurile generate (notificari, exporturi) in locul `127.0.0.1`.
5. `tools/install-caddy-service.ps1` (stilul `install-mariadb-service.ps1`): descarca/verifica Caddy, scrie `Caddyfile` din adresa publica si portul aplicatiei, instaleaza serviciul Windows, regula de firewall. `tools/start-preview.ps1` si `supervisor.ps1` raman pe `http://127.0.0.1` (comunica local).
6. Securitate la expunere publica: verificat ca limitarea de rata la login foloseste IP-ul real dupa pasul 1; parola implicita blocata de `UseDefaultPasswordGate`; jurnalizarea incercarilor de login (Audit) existenta; recomandare de parole lungi pentru administratori.
7. Documentatie: `docs/ACCES_PUBLIC.md` (pasii din sectiunea 4, depanare), `docs/SETUP_DEZVOLTARE.md`, `docs/PROJECT_STATE.md`, `TODO.md`/`IMPLEMENTED.md`; topicul `.help` al panoului Aplicatie actualizat si captura refacuta cu `node tools/help-capture.mjs --topic <cheie>`; pasi noi in `Teste utilizator/`.

## 6. Teste

- `tests/BlazorStoc.Checks`: `AppHosting` cu adresa publica, `AllowedHosts`, forwarded headers (IP real in `LoginRateLimit`, schema `https`), cookie `Secure` in spatele proxy-ului, jurnal pentru actiunea noua; suita `help` pentru topic.
- Manual (utilizator, din exterior): deschiderea `https://stoc.esp-solutions.ro`, login, butonul de preluare imagine din clipboard, reconectarea circuitului Blazor, certificat valid, reinnoire dupa ~60 de zile.

## 7. Riscuri

- Aplicatie cu date de stoc, beneficiari si facturi expusa pe internet: actualizari .NET regulate, backup-uri verificate, monitorizare login.
- ISP cu blocare 80/443 sau CG-NAT: alternativa tunel (Cloudflare Tunnel / Tailscale Funnel) sau VPN (Tailscale/WireGuard) daca accesul este doar pentru angajati.
- Reteaua pica sau DNS gresit: Caddy nu obtine certificatul; aplicatia ramane accesibila local pe HTTP, deci nu se pierde accesul administrativ.
- Cerinta Let's Encrypt de a fi gasit pe 80/443: reinnoirea esueaza daca porturile se inchid.

## 8. Decizii deschise

1. Numele subdomeniului (propus `stoc.esp-solutions.ro`).
2. Cine poate accesa din exterior: doar angajati (VPN ar fi alternativa mai sigura) sau oricine?
3. Cine administreaza DNS-ul `esp-solutions.ro` (utilizatorul sau un furnizor de hosting).
