# Colaborare secvențială Codex–Claude

## Stare operațională curentă

Colaborarea secvențială Codex–Claude este activă. Următorul ciclu este predat lui Claude după commitul de handoff curent; agenții continuă apoi să alterneze, fără lucru simultan.

## Principiul obligatoriu

La un moment dat lucrează un singur agent. Fluxul este:

```text
Codex → modifică → verifică → documentează → commit → se oprește
Claude → citește handoff-ul → modifică → verifică → documentează → commit → se oprește
Codex → reia ciclul
```

Nu se folosesc agenți în paralel, două sesiuni care scriu simultan, worktree-uri concurente sau commituri suprapuse.

## Sursele de adevăr, în ordine

1. Fișierele și commitul `HEAD` din repository.
2. `.collaboration/state.json`, care indică agentul ce poate prelua următorul ciclu.
3. `docs/PROJECT_STATE.md`, care descrie starea curentă și următorul pas.
4. `TODO.md`, care păstrează prioritățile și criteriile de acceptare.
5. `docs/AGENT_CHANGELOG.md`, jurnal append-only al ciclurilor încheiate.
6. Promptul curent al utilizatorului, care poate schimba prioritatea sau ordinea agenților.

Istoricul unei conversații nu înlocuiește aceste fișiere. Orice informație necesară continuării trebuie transferată în repository înainte de commit.

## Pornirea unui ciclu

Agentul care preia lucrul:

1. Confirmă că celălalt agent s-a oprit.
2. Citește fișierele obligatorii indicate în `AGENTS.md` sau `CLAUDE.md`.
3. Confirmă că `git status --short` este gol.
4. Confirmă că `nextAgent` din `.collaboration/state.json` corespunde agentului său.
5. Rulează comanda `start` din `tools/agent-cycle.ps1` cu taskul concret.
6. Recitește modificările recente relevante înainte să editeze aceleași fișiere.

Comanda `start` marchează ciclul `in_progress`. Dacă agentul sau starea nu corespund, scriptul oprește pornirea.

## Reguli de lucru

- Un ciclu implementează o unitate coerentă și verificabilă.
- Agentul păstrează modificările celuilalt și continuă de la `HEAD`; nu folosește reset, checkout distructiv, rebase sau amend asupra commitului celuilalt.
- Orice schimbare de comportament, date, interfață, configurare, test sau documentație este inclusă în sumarul ciclului.
- Pentru fiecare decizie care afectează continuarea se documentează: problema, comportamentul final, fișierele afectate, validarea și limitările rămase.
- Secretele, parolele, cheile și datele locale nu se introduc în documentație sau commit.
- Fișierele generate (`bin`, `obj`, `artifacts`, `data`, `keys`, loguri) rămân în afara Git.

## Încheierea și handoff-ul

Înainte de predare, agentul:

1. Finalizează implementarea autorizată și rulează verificările relevante.
2. Inspectează diff-ul complet și elimină numai propriile resturi temporare.
3. Actualizează `docs/PROJECT_STATE.md`:
   - rezultatul curent;
   - comportamentul implementat;
   - fișierele importante;
   - verificările și rezultatele;
   - limitările sau riscurile reale;
   - următorul task concret.
4. Rulează comanda `finish`. Aceasta:
   - adaugă o intrare în `docs/AGENT_CHANGELOG.md`;
   - marchează starea `ready_for_handoff`;
   - setează celălalt agent în `nextAgent`;
   - adaugă toate modificările în staging;
   - verifică erorile de whitespace;
   - creează commitul;
   - confirmă că working tree-ul este curat.
5. Se oprește imediat după commit.

Commiturile folosesc prefixul agentului:

- `codex: descriere`
- `claude: descriere`

Nu se face push automat.

## Documentarea fiecărei modificări

`docs/AGENT_CHANGELOG.md` este append-only. Fiecare ciclu conține obligatoriu:

- agentul și timestampul UTC;
- taskul primit;
- rezumatul schimbărilor și motivul lor;
- lista exactă a fișierelor modificate;
- testele/verificările executate;
- mesajul commitului;
- agentul căruia îi este predat proiectul.

`docs/PROJECT_STATE.md` este o fotografie actuală și se rescrie la fiecare ciclu. Changelog-ul păstrează istoricul, iar PROJECT_STATE păstrează doar informația necesară reluării rapide.

## Recuperarea după o întrerupere

Dacă un ciclu se întrerupe înainte de commit:

1. Celălalt agent nu începe lucru nou și nu șterge modificările existente.
2. Agentul care a început ciclul îl reia și îl finalizează, dacă este disponibil.
3. Dacă utilizatorul cere explicit transferul modificărilor necomise, agentul care preia inspectează diff-ul, actualizează `PROJECT_STATE.md`, finalizează sau repară schimbarea și creează un commit de recuperare cu propriul prefix.
4. Situația și proveniența modificărilor se consemnează în changelog.

Un working tree murdar nu este considerat handoff valid.
