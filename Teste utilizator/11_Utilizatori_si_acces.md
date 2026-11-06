# Utilizatori, autentificare, roluri si blocari de editare (/utilizatori)

Fluxul: administratorul creeaza conturi (Administrator / Utilizator) -> autentificare -> meniul se adapteaza rolului -> cand doua persoane editeaza acelasi obiect apare blocarea editarii.

## Modificari de testat

### 06.11.2026 - Autentificare cu cod din aplicatia de autentificare (Setari -> Autentificare)

**Ce s-a adaugat:** sectiunea Setari -> Autentificare (mod: niciodata / doar din exterior / pentru toti, reteaua interna, tabelul autentificatoarelor cu stare, confirmare, ultima folosire si coduri de recuperare ramase), cardul "Autentificator" pe pagina utilizatorului, codul pe aceeasi pagina cu utilizatorul si parola, comanda de urgenta `BlazorStoc.dll --reset-authenticator nume`.

**Ce face acum:** autentificatorul il asociaza administratorul (QR + cod de pe telefonul utilizatorului + 10 coduri de recuperare). Cand modul cere cod, utilizator, parola si cod se scriu pe aceeasi pagina, iar un refuz spune mereu "Utilizator, parola sau cod incorect". 5 incercari gresite pe un nume il blocheaza 10 minute (dublu la fiecare blocare). Inactivarea contului suspenda autentificatorul; la reactivare administratorul alege Reactiveaza sau Revoca. Stergerea utilizatorului sterge si autentificatorul. Pentru conturile de administrator toate modificarile se fac doar de pe server (localhost). Nu mai exista "Pastreaza-ma autentificat": sesiunea se incheie la inchiderea browserului sau dupa 8 ore, iar dupa orice deconectare urmeaza o autentificare completa.

| Nr | Pas | Rezultat asteptat | Rezultat obtinut |
|---|---|---|---|
| 1 | Pe server (localhost): Setari -> Autentificare, "Asociaza autentificator" pe un cont, parola de administrator | Apar QR-ul si cheia manuala; cod gresit = eroare, QR-ul ramane; cod corect de pe telefon = 10 coduri de recuperare, stare Activ | |
| 2 | De pe alt calculator, ca administrator: butoanele de pe randurile conturilor de administrator | Dezactivate, cu explicatia "doar de pe server"; conturile obisnuite raman gestionabile | |
| 3 | Alege "Pentru toti" si Salveaza (parola de administrator) | Refuzat daca niciun administrator activ nu are autentificator; altfel salvat si jurnalizat | |
| 4 | Autentifica-te cu utilizator, parola si cod pe aceeasi pagina, din retea si de pe server | Intri; cod/parola/utilizator gresit sau lipsa = acelasi mesaj generic | |
| 5 | Refoloseste acelasi cod, apoi un cod de recuperare de doua ori | Codul refolosit si codul de recuperare spendat sunt refuzate | |
| 6 | Dezactiveaza un utilizator cu autentificator, apoi reactiveaza-l | Starea devine Suspendat; dupa reactivare cardul cere alegerea Reactiveaza / Revoca | |
| 7 | Sterge un utilizator cu autentificator | Dispare si din tabelul autentificatoarelor | |
| 8 | Pe server: `BlazorStoc.dll --reset-authenticator nume` | Autentificatorul contului este sters; Jurnal "Revocare autentificator" (sistem) | |
| 9 | Pagina "Autentificatorul meu" (bara de sus) | Arata doar starea si instructiuni; utilizatorul nu mai inroleaza singur | |
| 10 | Pe un autentificator activ: "Schimba telefonul", parola de administrator, scaneaza QR-ul cu telefonul nou, scrie codul de pe el | Pana la confirmare telefonul vechi merge; inchisa fereastra fara cod nu schimba nimic; cu cod valid telefonul vechi si codurile de recuperare vechi nu mai merg, apar 10 coduri noi; Jurnal "Inlocuire autentificator" | |

### 20.10.2026 - Utilizatorii se muta din Administrare in Setari

**Ce s-a adaugat:** lista de utilizatori nu mai este in meniul Administrare; este in Setari, fila „Utilizatori”, subtab „Utilizatori” (langa subtab „Tipuri de utilizatori”). Vechiul /utilizatori redirectioneaza acolo (si cu ?edit=<id>); fisa /utilizatori/{id} ramane, iar „Inapoi” duce la lista din Setari.
**Ce face acum:** pasi: (1) meniul Administrare nu mai are „Utilizatori”; (2) Setari -> Utilizatori arata lista, „+ Adauga utilizator” este in antetul cardului „Conturi web”; (3) /utilizatori ajunge in Setari -> Utilizatori; (4) tile-ul „Utilizatori” de pe pagina principala duce in Setari; (5) un cont fara dreptul „Utilizatori” dar cu „Tipuri de utilizatori” vede doar subtab-ul Tipuri. Rezultat: ______

### 15.10.2026 - Mesaj la autentificare cand baza de date nu raspunde

**Verificat de agent 19.10.2026** (pe instanta de test, baza de test; vezi `docs/TESTE_RAMASE.md`, „Verificare completa a modulelor”).

**Ce s-a adaugat:** cand baza de date nu raspunde (sau un backup/o restaurare o tine ocupata), pagina de autentificare spune "Baza de date nu raspunde momentan sau este in curs un backup/o restaurare. Nu este o problema a contului tau: incearca din nou peste cateva momente." in loc de "Utilizator sau parola incorecta.". Si limita de "Prea multe incercari" numara doar trimiterea parolei, nu si deschiderea paginii de login.

**Ce face acum:** nimeni nu mai tasteaza degeaba parola cand baza e oprita; o parola gresita primeste in continuare mesajul obisnuit.

Pasi:

1. Cu baza oprita (`tools\dev-mariadb.ps1 -Action stop`), incearca sa te autentifici cu un cont din baza: apare mesajul despre baza de date, nu cel despre parola.
2. Cu baza pornita, o parola gresita da "Utilizator sau parola incorecta.".
3. Deschide de mai multe ori pagina de login fara sa trimiti formularul: nu apare "Prea multe incercari".

### 07.10.2026 - Reimprospatare fara licarire

**Ce s-a adaugat:** pagina unui utilizator (/utilizatori/{id}) se reimprospateaza automat fara spinner. Pas: lasa pagina deschisa 1-2 minute; nu trebuie sa licareasca. Rezultat: ______


**Ce face acum:** Pagina utilizatorului se actualizeaza singura fara licarire cand se schimba datele.
## Verificari (neschimbate recent; sunt cele pe care agentul nu le poate face)

| Pas | Ce faci | Ce trebuie sa vezi | Rezultat |
|---|---|---|---|
| 1 | Cont "Utilizator": meniul | Fara Jurnal si fara Utilizatori; Setari doar tabul Facturi; fara butoanele de stergere rezervate administratorului | |
| 2 | Doua calculatoare/conturi pe acelasi produs | Banner de blocare; administratorul poate debloca fortat, cu jurnal | |
| 3 | Inchide tabul care editeaza | Blocarea expira singura | |
| 4 | Pierdere conexiune si revenire | Aplicatia se reconecteaza fara pierderi | |
| 5 | Deconectare din popup | Duce la pagina de autentificare | |

## Fluxul de baza

1. Utilizator nou -> autentificare -> operatii permise -> operatii interzise respinse cu mesaj.

## Modificari de testat - 12.10.2026 (pagina de intretinere)

**Ce s-a adaugat:** pagina „Backup sau restaurare in curs" cere doar autentificare (nu mai cere dreptul de la Mentenanta); `/Error` nu mai este redirectionata spre ea.
**Ce face acum:** in timpul unui backup, orice utilizator conectat vede pagina de asteptare, fara bucla de redirectionari si fara eroarea 414.

| Nr | Pas | Rezultat asteptat | Rezultat obtinut |
|---|---|---|---|
| 1 | Administratorul porneste un backup manual; un utilizator fara drept la Mentenanta deschide o pagina | Pagina „Backup sau restaurare in curs"; la final revine singur | |
