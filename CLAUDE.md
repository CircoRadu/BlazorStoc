# Instrucțiuni pentru Claude

Acest proiect folosește colaborare strict secvențială între Claude și Codex. Claude nu lucrează niciodată simultan cu Codex.

## Înainte de orice modificare

1. Citește `docs/SEQUENTIAL_COLLABORATION.md`, `docs/PROJECT_STATE.md`, `TODO.md` și ultimele intrări din `docs/AGENT_CHANGELOG.md`.
2. Rulează `git status --short` și `git log -1 --oneline`.
3. Working tree-ul trebuie să fie curat. Dacă există modificări necomise, nu le elimina și nu începe alt task; urmează procedura de recuperare din protocol.
4. Verifică `.collaboration/state.json`. Claude poate începe doar când `status` este `ready_for_handoff` și `nextAgent` este `claude`, exceptând cazul în care utilizatorul cere explicit schimbarea ordinii.
5. Pornește ciclul cu:

   ```powershell
   .\tools\agent-cycle.ps1 start -Agent claude -Task "descrierea concretă a lucrării"
   ```

## În timpul lucrului

- Respectă prompturile utilizatorului și limitează modificările la taskul activ.
- Nu rescrie și nu anula modificările Codex fără o cerere explicită.
- Păstrează implementarea asincronă, fără Docker și fără integrare NAS în această etapă.
- Nu genera fișiere SQL de upgrade separate; modificările de schemă sunt gestionate în proiect.
- Toate datele calendaristice afișate utilizatorului au forma `dd.mm.yyyy` (de exemplu `25.09.2026`; cu oră: `25.09.2026 14:08`), în orice pagină, dialog, jurnal, mesaj sau document generat. Formatele interne (`yyyy-MM-dd` în SQLite, `dd-MM-yyyy` în coloana existentă `io_data` din MariaDB, adresele URL) nu se afișează; se folosesc `StockMovementRules.DisplayDate` și formatul `dd.MM.yyyy`.
- După modificări funcționale, actualizează preview-ul local la `http://127.0.0.1:5082/`.
- Actualizează `TODO.md` când starea taskurilor se schimbă, respectând „Regula de actualizare a TODO” din el (taskurile active se renumerotează; taskul finalizat se trece la sfârșitul fișierului cu data și ora implementării, detalii și o denumire succintă, fără „Task N”).
- Când o verificare nu poate fi efectuată sau una din `docs/TESTE_RAMASE.md` este efectuată, actualizează `docs/TESTE_RAMASE.md` (motivul, pașii, rezultatul).

## Înainte de predare

1. Rulează verificările adecvate și inspectează toate modificările.
2. Actualizează `docs/PROJECT_STATE.md` cu starea efectivă, validările, riscurile și următorul pas.
3. Încheie ciclul cu o descriere completă; scriptul actualizează jurnalul, face commit și predă lui Codex:

   ```powershell
   .\tools\agent-cycle.ps1 finish -Agent claude `
     -Summary "ce s-a schimbat și de ce" `
     -Tests "verificările executate și rezultatul" `
     -CommitMessage "claude: descriere scurtă"
   ```

4. După commit, oprește lucrul. Nu începe următorul task în același ciclu.
