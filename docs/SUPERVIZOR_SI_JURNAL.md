# Supervizor, jurnal in fisier si /health

Scris la 08.10.2026 (Task "Robustete la erori"). Fara diacritice.

## Ce exista in aplicatie
- **Jurnal in fisier:** `logs/blazorstoc-yyyyMMdd.log` (in directorul aplicatiei; `Logging:File:Directory` il schimba, `Logging:File:RetentionDays`, implicit 14 zile; fisierele mai vechi se sterg). Se scriu avertismentele, erorile si exceptiile necaptate, cu stack trace; utilizatorii nu vad niciodata stack trace (`DetailedErrors` ramane oprit). `logs/` este in `.gitignore`.
- **Exceptii in afara oricarui try** (`AppDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException`): se scriu in jurnal inainte de oprirea procesului.
- **Sarcini lansate fara asteptare:** `Task.FireAndForget(logger, "descriere")` (`Services/Robustness.cs`) prinde si logheaza exceptia; folosit la sondajele periodice ale paginilor, bataia de inima a blocarii produsului, layout si `UnsavedChangesHost`.
- **`ErrorBoundary`** in jurul continutului paginii (`MainLayout.razor`): o eroare dintr-o pagina arata „Eroare in aceasta pagina" cu butonul „Reincarca pagina", fara sa inchida circuitul; la schimbarea paginii bariera se reface.
- **`/health`** (anonim): 200 `{"status":"healthy"}` daca raspunde procesul si baza de date, 503 daca baza nu raspunde (fara detalii). `/health/live` verifica doar procesul.

## Repornirea automata (supervizor)
Aplicatia nu se reporneste singura: o instanta oprita de o exceptie ramane oprita. Reteta depinde de locul instalarii.

### Windows (cel curent): `tools\supervisor.ps1` (15.10.2026)
Un supervizor scris in PowerShell (nu un serviciu Windows), care porneste aplicatia publicata si o tine in picioare:
- aplicatia se opreste (exceptie, memorie, proces omorat): repornire dupa o pauza care se dubleaza (2 s, 4 s ... maximum 60 s) si revine la 2 s dupa ce aplicatia a mers 5 minute;
- aplicatia e vie dar `/health/live` nu raspunde de 3 ori la rand (dupa 60 s de la pornire): se omoara tot arborele de procese si se reporneste;
- `/health` da 503 (baza nu raspunde): doar se noteaza in jurnal, FARA repornire (aplicatia merge si fara baza si o regaseste singura);
- mai mult de 10 caderi in 10 minute: supervizorul renunta (stare `gave-up`, cod de iesire 2) in loc sa reporneasca la nesfarsit;
- `-WithMariaDb`: baza locala de dezvoltare (`tools\dev-mariadb.ps1`) este repornita cand portul ei nu mai raspunde (la fiecare verificare, nu doar la pornire).

Comenzi (din `BlazorStoc\`): `.\tools\supervisor.ps1 -Action run -Port 5087 -WithMariaDb` (porneste si supravegheaza), `-Action status`, `-Action stop` (opreste aplicatia si supervizorul), `-Action install` / `-Action uninstall` (sarcina programata pe utilizator, la autentificare, cu repornirea supervizorului la esec: de 3 ori la 1 minut; `install` NU se ruleaza fara acordul utilizatorului, este o configuratie persistenta).
Fisiere: `logs\supervisor-yyyyMMdd.log` (ce a facut supervizorul), `logs\supervised-<port>-<ora>.out.log`/`.err.log` (iesirea fiecarei porniri a aplicatiei, ultimele 20), `artifacts\supervisor\state-<port>.json`, `stop-<port>.flag`.
Teste: `tools\test-supervisor.ps1` (aplicatie falsa `tests\supervisor\fake-app.ps1` care cade, se blocheaza, pierde baza sau iese imediat; 23 de verificari, circa 3 minute).

Ce a descoperit verificarea pe aplicatia reala (de retinut): (1) la repornirea unui proces omorat in timpul unui backup, blocajul de mentenanta ramane pana expira concesiunea lui (2 minute); pornirea aplicatiei cadea cu `MaintenanceInProgressException` si supervizorul intra in bucla de repornire; acum pornirea o tolereaza (`Program.cs`, pazit in `StructureChecks`); (2) `Start-Process -Wait` in PowerShell 5.1 asteapta si procesele descendente: baza pornita astfel bloca bucla supervizorului; acum se asteapta doar scriptul, cu limita de 90 s (test cu un script care lasa un proces in urma).
Alternativa mai curata ramane NSSM sau un serviciu Windows adevarat (`nssm install BlazorStoc dotnet C:\...\BlazorStoc.dll`), cand aplicatia ajunge pe un server.

### Linux (systemd)
```
[Service]
WorkingDirectory=/opt/blazorstoc
ExecStart=/usr/bin/dotnet /opt/blazorstoc/BlazorStoc.dll
Restart=always
RestartSec=5
```

### Container (instalarea tinta, decizia utilizatorului 08.10.2026)
Repornirea o face containerul, nu aplicatia: politica de restart a Docker/QNAP Container Station.
```
services:
  blazorstoc:
    image: blazorstoc
    restart: unless-stopped          # repornire dupa orice oprire a procesului (exceptie necaptata, OOM, repornirea gazdei)
    environment:
      ASPNETCORE_URLS: http://+:8080
      Logging__File__Directory: /app/logs
    volumes:
      - ./logs:/app/logs             # jurnalul in fisier ramane si dupa repornire
      - ./data:/app/data
    healthcheck:
      test: ["CMD", "curl", "-f", "http://127.0.0.1:8080/health"]   # imaginea aspnet nu are curl: se instaleaza in Dockerfile
      interval: 30s
      timeout: 5s
      retries: 3
      start_period: 40s
```
- `/health` da 503 cand baza nu raspunde: containerul devine „unhealthy" (nu se reporneste singur; `restart` reactioneaza doar la oprirea procesului). Aplicatia porneste si fara baza (verificat), deci nu intra in bucla de repornire cand MariaDB porneste mai tarziu.
- Jurnalul scris si pe consola: `docker logs` arata aceleasi erori.
- Dockerfile-ul si fisierul compose nu sunt in repository (publicarea in containere e in afara fazei curente); de rezolvat la acel moment: pachetul `OpenCvSharp4.runtime.win` este numai pentru Windows (in container Linux trebuie `runtime.ubuntu` sau echivalent) si bibliotecile Tesseract/PDFium pentru Linux.

### Monitorizare
Orice monitor extern (sau `curl -f http://127.0.0.1:5087/health`) poate folosi `/health`: 503 = baza indisponibila, fara raspuns = proces cazut.

## De verificat
Vezi `docs/TESTE_RAMASE.md` (pornire cu baza oprita, test de cadere cu repornire, eliberarea blocarilor).

Supervizorul omorat (verificat 15.10.2026): aplicatia ramane orfana, iar RestartCount al sarcinii programate nu il reporneste. De aceea un supervizor nou opreste intai aplicatia ramasa de la rularea anterioara (pid si ora de pornire din state-<port>.json), si -Action install adauga un declansator repetat la 5 minute pe langa cel de la autentificare (un supervizor viu face repetarea inofensiva, unul mort este inlocuit).

### Marcajul de rulare si Jurnalul (15.10.2026)
Supervizorul seteaza `App__RunMarkerPath` (`artifacts\supervisor\run-<port>.json`): aplicatia scrie acolo un marcaj la pornire, il reimprospateaza la 30 s si il sterge la oprirea normala. Un marcaj gasit la pornirea urmatoare inseamna ca rularea anterioara a fost oprita fara avertisment (omorata, cazuta, masina oprita) si aplicatia scrie in Jurnalul de activitate "Oprire neasteptata aplicatie". Supervizorul adauga langa marcaj `run-<port>.json.exit` (codul de iesire si motivul: `exited with code -1`, `not answering ...`, supervizorul anterior mort) si sterge marcajul cand oprirea a fost ceruta (`-Action stop`), ca o oprire dorita sa nu apara ca o cadere. Modulele oprite sau reveniti apar si ele in Jurnal; ecranul lor este Setari -> Module.

### Serviciile de fundal (16.10.2026)
Daca un serviciu de fundal (planificatorul de backup, releul evenimentelor de schimbare, marcajul de rulare) arunca o eroare necaptata, doar el se opreste: `AddHostResilience` (`Startup/HostResilience.cs`) seteaza `BackgroundServiceExceptionBehavior.Ignore`, deci aplicatia continua sa serveasca paginile si modulele, iar eroarea ramane in log. Implicit .NET ar fi oprit tot host-ul, iar supervizorul l-ar fi repornit in bucla.
