# Teste utilizator rulate de pe telefon (prin VPN), fara a atinge baza live

Principiu: testele care scriu date se fac pe o instanta de test pornita pe calculatorul de dezvoltare (baza de dezvoltare, nu serverul live 192.168.100.126:5087). Telefonul ajunge la ea prin VPN. Serverul live ramane neatins.

## 1. Pregatire pe calculatorul de dezvoltare (192.168.100.20)

1. Pagina cu pasii: `node tools/teste-utilizator.mjs html` (toate fisierele) sau `html 06 18` (doar anumite fisiere, dupa prefix). Rezultat: `artifacts/teste-utilizator.html` (un singur fisier, fara server).
2. Instanta de test, in PowerShell (de la radacina proiectului):

```powershell
dotnet publish BlazorStoc.csproj -c Release -o artifacts\qa-build
$repo = (Get-Location).Path
$env:Database__PrivateConfigPath = "$repo\local-secrets\application-connection.private.json"
$env:ASPNETCORE_URLS = 'http://0.0.0.0:5090'
$env:ASPNETCORE_WEBROOT = "$repo\artifacts\qa-build\wwwroot"
Start-Process dotnet -ArgumentList "`"$repo\artifacts\qa-build\BlazorStoc.dll`"" -WorkingDirectory $repo -WindowStyle Hidden
Remove-Item Env:\Database__PrivateConfigPath, Env:\ASPNETCORE_URLS, Env:\ASPNETCORE_WEBROOT
```

3. Firewall (PowerShell ca administrator), pentru reteaua locala si subreteaua VPN (de ex. 10.8.0.0/24; vezi `tools/allow-wireguard.ps1` pentru cum se afla):

```powershell
New-NetFirewallRule -DisplayName "BlazorStoc test 5090" -Direction Inbound -Protocol TCP -LocalPort 5090 -RemoteAddress LocalSubnet,10.8.0.0/24 -Action Allow
```

4. Verificare de pe calculator: `http://127.0.0.1:5090/health` raspunde `healthy`.

## 2. Pe telefon

1. Conecteaza VPN-ul. Deschide `http://192.168.100.20:5090` (adresa calculatorului de dezvoltare) si autentifica-te ca administrator (contul din `local-secrets`, nu il trimite in chat).
2. Deschide pagina cu pasii: copiaza `teste-utilizator.html` pe telefon (Drive, e-mail, cablu) si deschide-l in Chrome/Brave; sau cere agentului sa o publice ca link privat.
3. Urmeaza pasii in aplicatie; in pagina alegi OK / Esuat / Sarit si scrii nota (la Esuat: ce s-a intamplat). Progresul ramane in browserul telefonului.
4. La sfarsit apasa „Exporta rezultate”: se descarca `rezultate-teste.json` si textul se copiaza in clipboard.

## 3. Dupa testare

1. Da agentului JSON-ul (lipit in chat sau fisierul), sau pe calculator: `node tools/teste-utilizator.mjs apply rezultate-teste.json` scrie `OK`, `ESUAT - nota`sau `SARIT` in coloana Rezultat a fisierelor din `Teste utilizator/`.
2. Opreste instanta si scoate regula de firewall:

```powershell
Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" | Where-Object { $_.CommandLine -like '*qa-build*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
Remove-NetFirewallRule -DisplayName "BlazorStoc test 5090"
```

## Observatii

- Nu exista HTTPS: acces numai prin VPN / reteaua locala.
- Datele create la teste raman in baza de dezvoltare: foloseste nume cu prefix „TEST …”.
- Pe telefon se pot completa campurile de data (nu si din automatizarea agentului), deci pasii cu vehicule, intrari si facturi se fac de acolo.
- Pasii pentru ecran ingust si telefon sunt cei din fisierele de teste cu „ecran ingust” / „telefon”.
- Pasii care cer ActivityApp real (fisierul 18) nu merg fara un SQL Server ActivityApp cu cont SQL de citire.

## Pagina cu pasii pe serverul live (de la 0.5.15)

Pachetul 0.5.15 aduce pagina cu pasii in aplicatie: `http://192.168.100.126:5087/teste/teste-utilizator.html` (fisier static, se deschide fara autentificare, doar in retea/VPN; nu contine date sau parole). Progresul se salveaza in browserul telefonului pe acest origin. Se regenereaza cu `node tools/teste-utilizator.mjs html --site` inainte de un release (arata pasii nerezolvati la acel moment). Pasii care scriu date se fac pe instanta de test (sectiunile de mai sus), nu pe serverul live.
