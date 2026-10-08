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

### Windows (cel curent)
Task Scheduler, o sarcina la pornirea sistemului care ruleaza `dotnet BlazorStoc.dll` cu variabilele din `tools\start-preview.ps1`, cu „Repornire la esec: la 1 minut, de 3 ori" (Setari → „Daca sarcina esueaza, reporneste la fiecare"). Alternativa mai curata: NSSM (`nssm install BlazorStoc dotnet C:\...\BlazorStoc.dll`, `nssm set BlazorStoc AppExit Default Restart`, `AppRestartDelay 5000`).

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
