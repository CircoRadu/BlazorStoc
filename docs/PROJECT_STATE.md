# Starea curentă a proiectului

Actualizat de: **Claude**
Data: **24 septembrie 2026**
Stare ciclu: **Task 2 — modelul de date și regulile de domeniu pentru proiecte finalizat; pregătit pentru predarea următorului ciclu către Codex după commitul curent**

## Rezumat

BlazorStoc este o aplicație Blazor Web App .NET 9, cu mod local persistent SQLite și suport MariaDB. Rulează direct cu .NET, fără Docker. Integrarea și publicarea NAS/QNAP sunt în afara fazei curente.

Sunt implementate CRUD-urile pentru produse, beneficiari și utilizatori, autentificarea pe roluri, administrarea categoriilor/subcategoriilor, imaginile produselor pe server, auditul persistent, arhivarea obiectelor șterse, componenta comună `CollapsibleSection`, identificarea produselor prin „Cod produs” și, nou, modelul de domeniu al proiectelor (fără persistență și fără interfață).

Proiectul folosește un repository Git local și cicluri strict secvențiale Codex–Claude. Următorul ciclu îi este predat lui Codex.

## Ultimele modificări funcționale (ciclul Claude — Task 2, modelul de date)

Ciclul a fost pornit la cererea explicită a utilizatorului („implementează 2.1”), deși `nextAgent` era `codex`; working tree-ul era curat și nu exista un ciclu Codex deschis. Tranziția `start` a fost aplicată manual în `.collaboration/state.json`, deoarece scriptul refuză schimbarea ordinii.

- `Services/Projects.cs` (nou):
  - Entitățile `Project` (beneficiar obligatoriu, denumire, „Observații” generale, versiune, `CreatedAtUtc`/`UpdatedAtUtc`), `ProjectObservation` (proiect, denumire, conținut, autor, versiune, timestampuri UTC) și `ProjectObservationFile` (observație, nume original, nume intern, tip media, dimensiune, SHA-256, autor, `UploadedAtUtc`). Identificatorii sunt `int`, la fel ca la celelalte entități.
  - `ProjectInput`/`ProjectObservationInput` cu `Validated(requiresReason)`: denumiri prin `TextNormalization.ForObjectNameOrCode`, texte libere prin `ForStorage`, limite 200 (denumiri), 4.000 (observații generale), 8.000 (conținut observație), motivare obligatorie la editare.
  - `ProjectRules`: `EnsureUniqueName` (unicitate numai în cadrul beneficiarului, cheie `TextNormalization.UniquenessKey`), `NormalizedName` pentru viitorul index unic `(beneficiar, denumire normalizată)`, `ConcurrentDuplicateMessage`, fabricile `Create`/`Edited`/`CreateObservation`/`EditedObservation` (versiune 0 la creare, +1 la editare, `CreatedAtUtc` păstrat, respingerea timestampurilor non-UTC), `CheckCurrent` pentru concurență optimistă și `CheckBeneficiaryExists`.
  - `ProjectFileRules`: `SafeOriginalName` (păstrează doar numele fișierului, elimină segmente de cale și caractere invalide, limită 255), `NewStoredName` (GUID + extensie alfanumerică validată), `Create` (respinge fișiere goale, tip lipsă, hash non-SHA-256).
- `Services/Beneficiaries.cs`: `BeneficiaryRules.CheckNoLiveProjects(int)` respinge ștergerea beneficiarului cu proiecte live.
- `TODO.md`: subtaskul „Modelul de date și regulile de domeniu” a fost mutat în grupul finalizat al Task 2; subtaskurile rămase au fost renumerotate (persistența este acum **Subtask 2.1**) și completate cu obligațiile de integrare a regulilor noi.

## Decizii și limitări

- Regula de blocare a ștergerii beneficiarului nu este încă apelată de repository-uri: tabelele/structurile proiectelor nu există încă, deci în prezent nu pot exista proiecte live. Aplicarea în demo, SQLite și MariaDB este un punct explicit din noul Subtask 2.1.
- Numele original al fișierului își păstrează diacriticele (este destinat afișării și descărcării); numele intern nu derivă din inputul utilizatorului.
- Limitele de dimensiune, număr și tipuri acceptate pentru fișiere rămân pentru subtaskul „Fișierele observațiilor”.
- Nu există modificări de interfață sau de schemă, deci preview-ul nu a necesitat repornire.
- Constantele de audit (`AuditEntities`) și arhivarea pentru proiecte nu au fost adăugate; aparțin subtaskului „Autorizare, audit și arhivare”.

## Validare

- Build Release (`BlazorStoc.Checks` cu proiectul principal): 0 avertismente, 0 erori.
- `BlazorStoc.Checks`: 205 verificări trecute (176 anterioare + 29 noi pentru proiecte, observații, metadatele fișierelor și regula de ștergere a beneficiarului).

## Reguli active ale proiectului

- Operațiile rămân asincrone.
- Nu se introduce Docker în această etapă.
- Nu se implementează integrare NAS/QNAP în această etapă.
- Nu se generează fișiere SQL de upgrade separate.
- După fiecare modificare funcțională se actualizează preview-ul local.
- Administratorul are acces complet; utilizatorul limitat operează produse și beneficiari, fără meniul Utilizatori.
- Modificările persistente trebuie jurnalizate și entitățile șterse trebuie arhivate conform contractului existent.

## Următorul pas

Următorul agent este **Codex**. Dacă utilizatorul nu stabilește altă prioritate, următorul element activ este Task 2 / **Subtask 2.1 — Persistența și repository-urile asincrone** din `TODO.md`, construit peste `Services/Projects.cs`.

## Fișiere de orientare

- `TODO.md` — backlog și criterii de acceptare.
- `README.md` — configurare și comportament general.
- `VALIDARE.md` — verificări istorice, inclusiv secțiunile „Cod produs” și „Proiecte — modelul de date”.
- `ARCHIVE_RECOVERY.md` — contractul de arhivare și recuperare.
- `docs/SEQUENTIAL_COLLABORATION.md` — protocolul Codex–Claude.
- `docs/AGENT_CHANGELOG.md` — istoricul handoff-urilor.
- `.collaboration/state.json` — agentul activ și agentul care poate prelua următorul ciclu.
- `tools/agent-cycle.ps1` — verifică și execută tranzițiile `start`, `finish` și `status`.
