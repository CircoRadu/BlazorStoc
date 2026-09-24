# Starea curentă a proiectului

Actualizat de: **Codex**
Data: **24 septembrie 2026**
Stare ciclu: **Task 0 finalizat; pregătit pentru predarea următorului ciclu către Claude după commitul curent**

## Rezumat

BlazorStoc este o aplicație Blazor Web App .NET 9, cu mod local persistent SQLite și suport MariaDB. Rulează direct cu .NET, fără Docker. Integrarea și publicarea NAS/QNAP sunt în afara fazei curente.

Sunt implementate CRUD-urile pentru produse, beneficiari și utilizatori, autentificarea pe roluri, administrarea categoriilor/subcategoriilor, imaginile produselor pe server, auditul persistent, arhivarea obiectelor șterse și preview-ul local.

Proiectul folosește un repository Git local și cicluri strict secvențiale Codex–Claude de modificare, verificare, documentare și commit. Colaborarea este activă, iar următorul ciclu îi este predat lui Claude.

## Ultimele modificări funcționale

- Task 0 este finalizat prin componenta reutilizabilă `CollapsibleSection`, bazată pe `details/summary`, cu identificatori stabili, `aria-expanded`, `aria-controls`, regiuni etichetate și suport mouse, touch, Enter și Space.
- Secțiunea „Administrare”, categoriile din meniul produselor și categoriile din pagina de administrare folosesc aceeași componentă, păstrând stilul fiecărei zone.
- Ramura activă din meniul produselor se deschide automat; extinderea unei categorii aplică filtrul, iar restrângerea revine la catalogul complet.
- Categoriile din pagina de administrare pornesc restrânse, își păstrează starea la actualizări, iar acțiunile de adăugare și editare funcționează independent de collapse.
- Interfața comună are feedback pentru hover, focus și disabled, iar la lățimi sub 900 px meniul și cardurile se reașază fără depășire orizontală.
- Numărul subcategoriilor este afișat textual în același stil ca numărul produselor asociate.
- Denumirile și codurile obiectelor sunt curățate la creare și editare: spațiile exterioare sunt eliminate, iar secvențele de spații sunt reduse la unul singur.
- Categoriile și subcategoriile pot fi create numai în pagina lor de administrare; formularul produsului selectează doar valori existente.
- `TODO.md` arhivează Task 0 ca finalizat; următoarele priorități active sunt Task 1 pentru „Cod produs” și Task 2 pentru proiecte asociate beneficiarilor.

## Validare cunoscută

- Suita completă `BlazorStoc.Checks` a trecut integral după implementarea componentei comune.
- Buildul Release a reușit cu 0 avertismente și 0 erori.
- Verificarea în browser a confirmat stările inițiale, click, Enter, Space, actualizarea ARIA, navigarea filtrată, acțiunile independente și afișarea fără overflow la 800 px.
- Preview-ul actualizat rulează la `http://127.0.0.1:5082/categorii`.

## Reguli active ale proiectului

- Operațiile rămân asincrone.
- Nu se introduce Docker în această etapă.
- Nu se implementează integrare NAS/QNAP în această etapă.
- Nu se generează fișiere SQL de upgrade separate.
- După fiecare modificare funcțională se actualizează preview-ul local.
- Administratorul are acces complet; utilizatorul limitat operează produse și beneficiari, fără meniul Utilizatori.
- Modificările persistente trebuie jurnalizate și entitățile șterse trebuie arhivate conform contractului existent.

## Următorul pas

Următorul agent este **Claude**. Dacă utilizatorul nu stabilește altă prioritate, următorul element activ este Task 1 — „Cod produs” din `TODO.md`.

## Fișiere de orientare

- `TODO.md` — backlog și criterii de acceptare.
- `README.md` — configurare și comportament general.
- `VALIDARE.md` — verificări istorice.
- `ARCHIVE_RECOVERY.md` — contractul de arhivare și recuperare.
- `docs/SEQUENTIAL_COLLABORATION.md` — protocolul Codex–Claude.
- `docs/AGENT_CHANGELOG.md` — istoricul handoff-urilor.
- `.collaboration/state.json` — agentul activ și agentul care poate prelua următorul ciclu.
- `tools/agent-cycle.ps1` — verifică și execută tranzițiile `start`, `finish` și `status`.
