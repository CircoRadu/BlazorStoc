# Autentificare in doi pasi (2FA) cu aplicatie de autentificare - descriere de functionare

Stare: implementat pe branch-ul `claude/autentificare-2fa` (06.11.2026), migrarile 69-71. Documentul descrie ce face aplicatia, cum se configureaza, cum se lucreaza zilnic, fluxurile, regulile de securitate, limitele si unde este codul. Fara diacritice (regula fisierelor `.md`).

## 1. Ce este si ce problema rezolva

Pe langa utilizator si parola, aplicatia poate cere un **cod de 6 cifre** generat de o aplicatie de autentificare de pe telefonul utilizatorului (Google Authenticator, Microsoft Authenticator, Aegis etc.; standard TOTP, RFC 6238). O parola furata sau ghicita nu mai este suficienta pentru acces.

Principii:
- autentificatorul il **asociaza administratorul** (utilizatorul nu se inroleaza singur);
- utilizator, parola si cod se scriu **pe aceeasi pagina**, iar un refuz spune mereu acelasi lucru ("Utilizator, parola sau cod incorect"), fara sa dezvaluie care factor a fost gresit;
- **nu exista "Pastreaza-ma autentificat"**: sesiunea se incheie la inchiderea browserului sau dupa 8 ore, iar dupa orice deconectare urmeaza o autentificare completa;
- modificarile 2FA ale **conturilor de administrator** se fac doar de pe serverul insusi (localhost).

## 2. Moduri de functionare

Setari -> Autentificare -> "Cand se cere codul":

| Mod | Cine introduce codul | Folosire tipica |
|---|---|---|
| Niciodata (implicit) | nimeni; autentificarea ramane utilizator + parola, ca inainte | 2FA nepornit |
| Doar din exterior | vizitatorii care NU sunt in reteaua interna | acces din afara firmei, reteaua interna ramane comoda |
| Pentru toti | toti, din orice adresa, inclusiv de pe server | securitate maxima |

Reteaua interna (relevanta doar la "Doar din exterior") se scrie ca adrese sau retele CIDR (una pe rand sau separate prin virgula / punct si virgula / spatiu), de exemplu `192.168.1.0/24`. Lasata goala, este formata din: `127.0.0.0/8`, `::1/128`, `10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`, `169.254.0.0/16`, `fc00::/7`, `fe80::/10`. Adresa serverului insusi (loopback) este **intotdeauna** interna. Adresele IPv4 mapate in IPv6 (`::ffff:a.b.c.d`) se trateaza ca IPv4.

Sub selector panoul arata adresa ta curenta si daca esti "pe server (localhost)", "retea interna" sau "exterior".

### Protectie la pornire
"Pentru toti" nu se poate salva daca niciun **administrator activ** nu are autentificator confirmat (altfel nimeni nu ar mai putea intra). Mesajul cere asocierea intai a unui autentificator unui administrator, de pe server.

### Unde se salveaza configurarea
Un fisier JSON mic, `access-security.json`, in folderul de date al aplicatiei (`App:DataRoot`, implicit `C:\ProgramData\BlazorStoc`; calea se poate fixa cu setarea `Security:AccessPath`). Este o sursa de configurare incarcata la pornire si reincarcata la salvarea din panou, deci **se aplica de la urmatoarea autentificare**, fara repornire.

```json
{ "Security": { "Access": { "Mode": "Everyone", "InternalNetworks": [ "192.168.1.0/24" ] } } }
```
`Mode` = `Off` / `External` / `Everyone`. Prima versiune a fisierului avea un indicator `ExternalTwoFactor: true`, care se citeste ca `External`.

Salvarea cere **parola contului de administrator** (reconfirmare) si se jurnalizeaza ("Modificare securitate acces extern", cu valorile vechi si noi).

## 3. Cum functioneaza codul (TOTP)

- Secret aleator de 160 de biti pe cont, scris in base 32; algoritm HMAC-SHA1, 6 cifre, pas de 30 de secunde.
- Un cod este acceptat daca apartine pasului curent sau unuia vecin (deriva de ceas de +-30 s).
- **Un cod nu se poate folosi de doua ori**: se retine ultimul pas acceptat, iar un cod dintr-un pas deja folosit este refuzat (si la autentificare, si la confirmare).
- Secretul se pastreaza **criptat** (ASP.NET Data Protection, scopul `BlazorStoc.TwoFactor.Secret`, cheile din folderul `keys/`); nu apare in jurnal, in loguri sau in exporturi de date.
- **Coduri de recuperare**: 10 coduri de forma `XXXXX-XXXXX` (fara caractere confundabile), de unica folosinta, pastrate doar ca hash SHA-256. Se potrivesc fara a conta litera mare/mica sau separatorul. Se afiseaza o singura data (la confirmare sau la regenerare).
- Codul QR contine adresa `otpauth://totp/BlazorStoc:<utilizator>?secret=...&issuer=BlazorStoc&algorithm=SHA1&digits=6&period=30`; aceeasi cheie se poate scrie si manual in aplicatie.

## 4. Starile unui autentificator

| Stare | Inseamna | Poate intra cu cod? |
|---|---|---|
| Neinrolat | contul nu are autentificator | nu |
| In asteptare | secret generat, neconfirmat cu un cod | nu |
| Activ | confirmat | da |
| Suspendat | confirmat, dar contul a fost dezactivat | nu (nici cu cod de recuperare) |
| Blocat | 5 coduri gresite la rand; verificarea e blocata temporar | nu, pana expira blocarea sau o ridica adminul |

Pentru fiecare cont se retin: cine l-a asociat, cine l-a confirmat si cand, ultima folosire (data si IP), numarul codurilor de recuperare ramase.

## 5. Fluxul de autentificare

Pagina `/Account/Login` (o singura forma):

1. Campul **Cod din aplicatia de autentificare** apare pe pagina doar cand modul il cere pentru vizitatorul respectiv (in "Doar din exterior" un vizitator intern nu il vede).
2. La trimitere, cand codul e cerut:
   1. Daca numele scris este blocat (vezi sectiunea 6), cererea este refuzata cu "Prea multe incercari. Autentificarea este blocata inca N min.".
   2. Se verifica parola (administratorul din configurare, apoi conturile din baza de date). Parola se verifica mereu; la un utilizator inexistent sau cu parola gresita se face totusi munca unei verificari de cod, ca timpul raspunsului sa nu dea indicii.
   3. Doar daca parola e buna se verifica codul (sau un cod de recuperare) pe contul respectiv; un cod se "consuma" numai in acest caz.
   4. Orice esec (utilizator inexistent, parola gresita, cont inactiv, cod gresit/lipsa, fara autentificator, autentificator suspendat, blocat) afiseaza **acelasi mesaj**: "Utilizator, parola sau cod incorect." Motivul exact intra doar in Jurnal.
   5. La succes se goleste contorul numelui, se creeaza sesiunea si se scrie "Conectare" in Jurnal (cu IP).
3. Cand codul nu e cerut, fluxul este cel vechi (utilizator + parola; mesajele vechi, inclusiv "Contul este inactiv").
4. **Sesiunea**: cookie de sesiune fara persistenta, expira la 8 ore (fara reinnoire glisanta) sau la inchiderea browserului. Nu exista optiunea "Pastreaza-ma autentificat".

Administratorul din configurare (`Authentication:Username`, implicit `admin`) nu are rand in `web_users`; autentificatorul lui foloseste identificatorul **0**. Are exact aceleasi reguli.

## 6. Blocari si limitari de incercari

Trei mecanisme independente:

1. **Pe numele scris la autentificare** (si pentru nume inexistente, ca blocarea sa nu dezvaluie conturile): 5 incercari gresite blocheaza numele 10 minute; fiecare blocare urmatoare dubleaza durata (pana la 24 de ore); un nume lasat in pace 30 de minute reincepe de la zero. Se aplica doar cand codul e cerut.
2. **Pe contul cu autentificator**: 5 coduri gresite blocheaza verificarea aceluiasi cont (aceeasi scara de durate). Adminul poate ridica blocarea ("Deblocheaza"). La confirmarea unei asocieri, codurile gresite se numara la fel.
3. **Pe adresa IP** (existent): maximum 10 cereri pe minut catre pagina de autentificare, apoi raspuns 429 "Prea multe incercari. Asteapta un minut".

Contoarele 1 si 2 sunt **in memoria aplicatiei**: se reseteaza la repornire. Nu sunt in baza de date.

## 7. Asocierea unui autentificator (de catre admin)

Locuri: **Setari -> Autentificare** (tabelul autentificatoarelor) si cardul **Autentificator** de pe pagina utilizatorului (Setari -> Utilizatori). Aceleasi operatii, aceeasi componenta.

1. Adminul alege **Asociaza autentificator** pe cont si scrie parola proprie (reconfirmare). Contul trebuie sa fie activ.
2. Aplicatia genereaza un secret nou (stare *in asteptare*) si arata codul QR si cheia manuala. Adminul sta langa utilizator (sau la telefon): utilizatorul scaneaza QR-ul. **Nu se trimite QR-ul prin mesaje sau e-mail.**
3. Utilizatorul citeste codul de 6 cifre afisat de aplicatie, iar adminul il scrie in dialog si confirma. Cod gresit = eroare, QR-ul ramane afisat.
4. La confirmare: stare *activ*, se genereaza 10 coduri de recuperare afisate o singura data, adminul le preda utilizatorului. Jurnal: "Asociere autentificator" si "Confirmare autentificator".
5. Daca dialogul se inchide fara confirmare, contul ramane *in asteptare*; "Reia asocierea" genereaza alt secret.

Adminul vede secretul in momentul asocierii (inerent acestui model); de aceea secretul se afiseaza o singura data, nu se mai poate citi dupa confirmare, iar operatia este jurnalizata.

## 8. Operatii ulterioare (toate cu parola adminului, jurnalizate)

| Operatie | Efect |
|---|---|
| **Schimba telefonul** | Genereaza un secret nou care asteapta langa cel vechi. **Telefonul vechi merge mai departe** pana cand adminul scrie in acelasi dialog un cod valid de pe telefonul nou; abia atunci secretul nou ia locul celui vechi, intr-o singura tranzactie, si apar 10 coduri de recuperare noi (cele vechi nu mai merg). Inchiderea dialogului fara cod renunta la secretul nou: nu se schimba nimic. Jurnal: "Inlocuire autentificator". |
| **Revoca** | Sterge imediat autentificatorul si codurile de recuperare. Pentru telefon pierdut sau furat. Utilizatorul nu mai poate intra cu cod pana nu i se asociaza altul. Jurnal: "Revocare autentificator". |
| **Reactiveaza** | Scoate din suspendare un autentificator (acelasi telefon). Necesita cont activ. Jurnal: "Reactivare autentificator". |
| **Coduri de recuperare noi** | Inlocuieste cele 10 coduri; cele vechi nu mai merg. Jurnal: "Regenerare coduri de recuperare". |
| **Deblocheaza** | Ridica blocarea verificarii codului (nu cere parola). Jurnal: "Deblocare autentificator". |

Utilizatorul isi vede doar starea la **Autentificatorul meu** (link in bara de sus, `/Account/TwoFactor`): stare, coduri de recuperare ramase, ultima folosire. Nu se poate inrola singur.

## 9. Legatura cu contul de utilizator

- **Inactivarea contului** suspenda autentificatorul (il pastreaza, dar nu se poate folosi, nici cu cod de recuperare). **Reactivarea contului nu il reporneste automat**: cardul arata starea "Suspendat" si adminul alege **Reactiveaza** (acelasi telefon) sau **Revoca** (si asociaza altul).
- **Stergerea utilizatorului** sterge, in aceeasi tranzactie, autentificatorul si codurile de recuperare. Un utilizator restaurat din arhiva trebuie asociat din nou.
- Redenumirea contului nu afecteaza autentificatorul (legatura este prin identificatorul numeric al contului, nu prin nume).

## 10. Conturile de administrator: doar de pe server

Orice modificare 2FA pentru un cont **Administrator** (asociere, confirmare, schimbarea telefonului, revocare, reactivare, coduri de recuperare, deblocare, inclusiv contul din configurare) se accepta numai daca cererea vine **de pe serverul insusi**:
- adresa clientului este loopback (`127.0.0.1` / `::1`), **si**
- cererea nu poarta antete de proxy (`X-Forwarded-For`, `X-Original-For`, `Forwarded`), ca un proxy de pe aceeasi masina sa nu faca traficul extern sa para local.

Verificarea este facuta in serviciu, nu doar prin butoane dezactivate. Din alt calculator butoanele sunt inactive cu explicatia "doar de pe server", iar o cerere directa primeste refuz. Conectarea la server prin Remote Desktop si deschiderea browserului acolo conteaza ca localhost. Conturile obisnuite se pot gestiona de un admin de oriunde (cu parola reconfirmata).

## 11. Acces de urgenta (nimeni nu mai poate intra)

Pe server, in contextul unui cont cu drepturi asupra aplicatiei:

```
BlazorStoc.dll --reset-authenticator <nume-utilizator>
```
Sterge autentificatorul si codurile de recuperare ale contului (pentru administratorul din configurare se scrie numele lui, de obicei `admin`). Raspunsuri: `AUTH-OK` (iesire 0), `AUTH-NOUSER` (4, cont inexistent), `AUTH-INVALID` (5, lipseste numele). Cere acces la sistemul de operare al serverului, nu exista cale web. Se scrie in Jurnal ca operatie a utilizatorului `sistem` ("Revocare autentificator"). Dupa resetare, din Setari se poate muta modul pe "Niciodata" sau se asociaza un autentificator nou.

## 12. Procedura recomandata de introducere

1. Pe **server**, intra ca administrator si asociaza un autentificator contului tau (Setari -> Autentificare). Noteaza codurile de recuperare intr-un loc sigur.
2. Asociaza autentificatoare celorlalti administratori (tot de pe server) si apoi utilizatorilor (de oriunde).
3. Verifica in Setari -> Autentificare ca toti cei care trebuie sa intre au starea **Activ**.
4. Alege modul: intai "Doar din exterior" (verifica cu un telefon pe date mobile), apoi, daca vrei, "Pentru toti".
5. Pentru acces din exterior prin proxy, configureaza intai "HTTPS prin proxy" (Setari -> Baza de date -> Conexiune), altfel adresa clientului nu ajunge corect (vezi sectiunea 14).

## 13. Jurnal (Jurnal activitate)

Operatii cu nume exact, filtrabile: Modificare securitate acces extern, Asociere autentificator, Confirmare autentificator, Inlocuire autentificator, Revocare autentificator, Reactivare autentificator, Regenerare coduri de recuperare, Deblocare autentificator (exista in aplicatie si denumirile "Suspendare autentificator" si "Stergere autentificator", dar nu sunt scrise de niciun cod deocamdata, vezi nota de mai jos). Conectarile refuzate apar ca "Conectare esuata" cu motivul exact si IP, de exemplu: parola gresita, cont inactiv, cod de autentificator gresit, fara autentificator inrolat, autentificator suspendat, autentificator blocat dupa prea multe coduri, autentificare blocata dupa prea multe incercari. Parola, codurile si secretele nu se scriu niciodata.

Nota: suspendarea automata la inactivare si stergerea in cascada la stergerea utilizatorului apar in Jurnal ca schimbare de stare / stergere a contului, nu ca intrari separate ale autentificatorului. Folosirea unui cod de recuperare ajunge in logul aplicatiei, nu ca intrare de jurnal proprie.

## 14. Dependenta de adresa clientului (proxy)

Decizia "intern / exterior" si regula "doar de pe server" citesc `RemoteIpAddress` al conexiunii.
- **Direct, fara proxy**: adresa reala a clientului; totul functioneaza cum e descris.
- **Prin proxy (nginx) pe alta masina**: fara "HTTPS prin proxy" configurat toti vizitatorii apar cu adresa proxy-ului (deci "interni" daca e in reteaua privata). Configureaza proxy-ul in Setari -> Baza de date -> Conexiune -> HTTPS prin proxy; atunci se foloseste adresa din `X-Forwarded-For`, trimisa doar de proxy-ul de incredere.
- **Proxy pe aceeasi masina**: cererile vin de pe 127.0.0.1; regula "doar de pe server" tine cont de antetele de proxy, deci un client extern nu poate trece drept local. Totusi, in modul "Doar din exterior", un proxy fara antete ar face traficul sa para intern: foloseste "HTTPS prin proxy".

## 15. Datele din baza de date

- `web_user_totp` (cheie `user_id`; 0 = administratorul din configurare): `secret_protected` (criptat), `created_utc`, `confirmed_utc`, `last_step`, `suspended_utc`, `last_used_utc`, `last_used_ip`, `enrolled_by`, `confirmed_by`, `pending_secret_protected` (secret nou in asteptare la schimbarea telefonului).
- `web_user_recovery_codes`: `id`, `user_id`, `code_hash`, `used_utc`.
- Migrari: 69 (tabelele), 70 (suspendare, ultima folosire, cine a asociat/confirmat), 71 (inlocuire atomica). Se aplica automat la pornire. Tabelele fac parte din baza de date, deci intra in copiile ei de siguranta; secretele raman criptate cu cheile din `keys/`, care nu sunt in baza de date: o restaurare pe alt server cere si cheile (altfel secretele nu se mai pot citi si autentificatoarele trebuie asociate din nou). Neverificat printr-o restaurare reala.

## 16. Proprietati de securitate si limite cunoscute

Ce asigura:
- un singur mesaj de refuz si munca egala indiferent de factorul gresit (fara oracol de parola/utilizator);
- blocare crescatoare pe nume si pe cont, plus limita pe IP;
- cod refolosit si cod de recuperare folosit refuzate;
- secrete criptate, coduri de recuperare doar ca hash;
- reconfirmare cu parola a administratorului pentru operatiile sensibile, jurnal complet;
- conturile de administrator nu se pot modifica de la distanta;
- schimbarea telefonului nu lasa utilizatorul fara acces si nu lasa doua secrete valide dupa confirmare.

Limite:
- TOTP nu rezista la phishing in timp real (o pagina falsa poate retransmite codul); cea mai sigura varianta ar fi chei de securitate / passkeys (WebAuthn), care nu sunt implementate.
- Adminul vede secretul la asociere.
- Contoarele de blocare sunt in memorie (se reseteaza la repornire).
- Blocarea pe nume poate fi folosita de un atacator pentru a incurca un utilizator (poate bloca numele lui 10 minute); adminul vede incercarile in Jurnal.
- La revocare nu se inchid sesiunile deja deschise ale utilizatorului (nu exista inca inchidere de sesiune dupa nume); sesiunea expira oricum la 8 ore.
- Verificat cu coduri calculate de un script si cu valorile de test din RFC 6238; neverificat cu o aplicatie de autentificare reala pe telefon si cu un proxy real.

## 17. Depanare

| Situatie | Ce faci |
|---|---|
| Utilizatorul scrie codul corect si e refuzat | ceasul telefonului sau al serverului e decalat cu peste 30 s (activeaza ora automata); verifica ca nu e blocat (stare "Blocat" in tabel) si ca autentificatorul e *Activ*, nu *Suspendat* |
| "Autentificarea este blocata inca N min." | asteapta sau, pentru cont cu autentificator blocat, adminul foloseste "Deblocheaza"; blocarea pe nume expira singura |
| Utilizator fara autentificator vrea sa intre din exterior | adminul ii asociaza unul (sectiunea 7) |
| Telefon pierdut / furat | adminul face **Revoca** si asociaza altul; utilizatorul poate intra intre timp cu un cod de recuperare |
| Telefon nou, cel vechi inca merge | **Schimba telefonul** |
| Admin fara acces la autentificator | pe server: `BlazorStoc.dll --reset-authenticator <nume>` |
| Butoanele conturilor de admin sunt inactive | esti conectat de la distanta: deschide aplicatia pe server (localhost) |
| Toti apar "interni" sau "externi" gresit | adresa clientului nu e cea reala: configureaza "HTTPS prin proxy" |
| Contul reactivat nu poate intra cu cod | autentificatorul ramane suspendat: adminul alege Reactiveaza sau Revoca |

## 18. Unde este codul

| Subiect | Fisiere |
|---|---|
| TOTP, coduri de recuperare, moduri, retea interna, blocare, magazin de date | `Services/TwoFactor.cs` |
| Serviciul de administrare (salvare setari, operatii, regula "doar de pe server") | `Services/AccessSecuritySettings.cs` |
| Comanda de urgenta | `Services/AuthenticatorReset.cs`, intrarea din `Program.cs` |
| Pagina de autentificare | `Pages/Account/Login.cshtml`, `Login.cshtml.cs`, `LoginMessages.cs` |
| Pagina "Autentificatorul meu" (doar stare) | `Pages/Account/TwoFactor.cshtml`, `TwoFactor.cshtml.cs` |
| Sectiunea Setari -> Autentificare | `Components/Shared/AccessSecurityPanel.razor`, fila in `Components/Pages/Settings.razor` |
| Operatiile pe un cont (dialoguri, QR) | `Components/Shared/AuthenticatorActions.razor`, cardul din `Components/Pages/UserDetail.razor` |
| Inactivare / stergere cont | `Services/MariaUserRepository.cs` (`TwoFactorSql`) |
| Migrari | `Assets/Migrations/069.sql`, `070.sql`, `071.sql` |
| Fisier de setari | `access-security.json` (`Startup/PrivateConfiguration.cs`) |
| Operatii de jurnal | `Services/AuditTrail.cs` (`AuditActions`), `Modules/Journal/AuditFilters.cs` |
| Teste | `tests/BlazorStoc.Checks/AccessSecurityChecks.cs` (grupul `dbsettings`), sectiunea MariaDB "Users" din `MariaExtendedChecks.Projects.cs`; pasii pentru utilizator in `Teste utilizator/11_Utilizatori_si_acces.md` |

Pachetul `QRCoder` genereaza codul QR (SVG, fara servicii externe).
