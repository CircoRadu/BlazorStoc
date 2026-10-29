# LoginMessage

Pune mesajul "NU OPRI PC" (litere mari, alb pe rosu) pe fundalul ecranului de blocare/login din Windows 11,
cat timp aplicatia a fost pornita. La repornirea PC-ului revine imaginea originala.

Rulare (administrator, cerut automat): `LoginMessage.exe` sau `LoginMessage.exe "alt mesaj"` (`\n` = rand nou).
Revenire manuala fara repornire: `LoginMessage.exe --restore`.

Cum functioneaza: salveaza valorile vechi din `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\PersonalizationCSP`,
seteaza imaginea generata (`C:\ProgramData\LoginMessage\lockscreen.png`) si creeaza sarcina programata SYSTEM
`LoginMessageRestore` (la pornirea Windows) care ruleaza `--restore`: reface valorile originale si se sterge singura.
Necesita Windows 11 Pro/Enterprise/Education (pe Home setarea este ignorata).

Build: `dotnet publish tools/LoginMessage -c Release -r win-x64 --self-contained -p:PublishSingleFile=true`
