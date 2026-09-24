# Instrucțiuni pentru Codex

Proiectul este momentan în modul `codex_only`; Claude este în standby. Codex lucrează în cicluri succesive de modificare, verificare, documentare și commit. Colaborarea secvențială cu Claude se reactivează numai la cererea explicită a utilizatorului.

## Înainte de orice modificare

1. Citește `docs/SEQUENTIAL_COLLABORATION.md`, `docs/PROJECT_STATE.md`, `TODO.md` și ultimele intrări din `docs/AGENT_CHANGELOG.md`.
2. Rulează `git status --short` și `git log -1 --oneline`.
3. Working tree-ul trebuie să fie curat. Dacă există modificări necomise, nu le elimina și nu începe alt task; urmează procedura de recuperare din protocol.
4. Verifică `.collaboration/state.json`. Codex poate începe doar când `status` este `ready_for_handoff` și `nextAgent` este `codex`.
5. Pornește ciclul cu:

   ```powershell
   .\tools\agent-cycle.ps1 start -Agent codex -Task "descrierea concretă a lucrării"
   ```

## În timpul lucrului

- Respectă prompturile utilizatorului și limitează modificările la taskul activ.
- Nu rescrie și nu anula modificările lui Claude fără o cerere explicită.
- Păstrează implementarea asincronă, fără Docker și fără integrare NAS în această etapă.
- Nu genera fișiere SQL de upgrade separate; modificările de schemă sunt gestionate în proiect.
- După modificări funcționale, actualizează preview-ul local la `http://127.0.0.1:5082/`.
- Actualizează `TODO.md` când starea taskurilor se schimbă.

## Înainte de predare

1. Rulează verificările adecvate și inspectează toate modificările.
2. Actualizează `docs/PROJECT_STATE.md` cu starea efectivă, validările, riscurile și următorul pas.
3. Încheie ciclul cu o descriere completă; scriptul actualizează jurnalul, face commit și pregătește următorul ciclu Codex cât timp modul `codex_only` este activ:

   ```powershell
   .\tools\agent-cycle.ps1 finish -Agent codex `
     -Summary "ce s-a schimbat și de ce" `
     -Tests "verificările executate și rezultatul" `
     -CommitMessage "codex: descriere scurtă"
   ```

4. După commit, oprește lucrul. Nu începe următorul task în același ciclu.
