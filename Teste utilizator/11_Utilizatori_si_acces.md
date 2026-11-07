# Utilizatori, autentificare, roluri si blocari de editare (/utilizatori)

Fluxul: administratorul creeaza conturi (Administrator / Utilizator) -> autentificare -> meniul se adapteaza rolului -> cand doua persoane editeaza acelasi obiect apare blocarea editarii.

## Modificari de testat

### 07.11.2026 - Fereastra codurilor de recuperare

**Ce s-a adaugat:** fereastra "Coduri de recuperare" (dupa asocierea unui autentificator sau generarea de coduri noi) este mai larga, iar codurile se rup la margine in loc sa iasa din fereastra; buton nou "Salveaza codurile (.txt)".

**Ce face acum:** codurile se vad intregi; butonul descarca un fisier text cu utilizatorul, data si codurile.

| Nr | Pas | Rezultat asteptat | Rezultat obtinut |
|---|---|---|---|
| 1 | Setari -> Autentificare: asociaza un autentificator unui cont de test si confirma codul | Se deschide fereastra cu codurile; textul nu depaseste marginea ferestrei | |
| 2 | Apasa "Salveaza codurile (.txt)" | Se descarca coduri-recuperare-<utilizator>-<data>.txt cu titlu, data si toate codurile, cate unul pe rand | |
| 3 | Apasa "Am notat codurile" | Fereastra se inchide | |

### 06.11.2026 - Schimbarea parolei la prima autentificare si politica de parola

**Ce s-a adaugat:** pagina "Alege o parola noua" (`/Account/ChangePassword`), indicatorul "Parola de schimbat" in lista de utilizatori, politica de parola (minimum 8 caractere, cu litera mica, litera mare, cifra si caracter special), jurnal "Stabilire parola de catre administrator" si "Schimbare parola la prima autentificare".

**Ce face acum:** orice cont creat de un administrator, orice cont a carui parola o seteaza un administrator (altul decat propriul cont) si TOATE conturile care existau la aceasta versiune trebuie sa-si aleaga o parola noua la prima autentificare. Dupa ce utilizator, parola (si codul 2FA, cand se cere) sunt corecte, utilizatorul nu intra in aplicatie ci ajunge pe pagina de alegere a parolei noi; sesiunea se creeaza abia dupa. Parola noua trebuie sa respecte politica si sa fie diferita de cea stabilita de administrator. Politica se aplica oricarei parole: si celei setate de administrator si celei alese de utilizator (inclusiv "Schimba parola" din meniul contului). Administratorul din configurare nu este inclus.

| Nr | Pas | Rezultat asteptat | Rezultat obtinut |
|---|---|---|---|
| 1 | Administrator: creeaza un utilizator cu parola "slaba" | Refuzat, mesajul spune ce lipseste (litera mare, cifra, caracter special...) | |
| 2 | Creeaza utilizatorul cu o parola corecta (ex. Temp-Parola-123!) | In lista apare "Parola de schimbat" langa stare; pe pagina utilizatorului apare explicatia | |
| 3 | Autentifica-te cu utilizatorul nou | Esti dus la "Alege o parola noua", nu in aplicatie; fara pagina nu poti deschide nimic | |
| 4 | Scrie ca parola noua chiar parola primita de la administrator; apoi una slaba; apoi confirmare diferita | Mesaj clar de fiecare data, nimic nu se schimba | |
| 5 | Scrie o parola noua corecta | Intri in aplicatie; indicatorul din lista a disparut; Jurnal "Schimbare parola la prima autentificare" | |
| 6 | Autentifica-te din nou | Intri direct, cu parola noua; vechea nu mai merge | |
| 7 | Administrator: seteaza o parola noua unui utilizator (Editeaza) | Jurnal "Stabilire parola de catre administrator"; utilizatorul isi alege alta parola la urmatoarea autentificare | |
| 8 | Conturile care existau deja inainte de actualizare | La prima autentificare fiecare isi alege o parola noua; ramane valabila parola veche pana atunci | |
| 9 | Autentificare cu 2FA obligatoriu, cont cu parola de schimbat | Intai utilizator + parola + cod, apoi pagina de alegere a parolei | |
| 10 | Administratorul isi schimba propria parola in editor | Nu i se cere schimbarea la autentificarea urmatoare (parola e a lui) | |
| 11 | Pe pagina "Alege o parola noua" si in fereastra "Schimba parola": scrie parola noua litera cu litera | Cele cinci conditii (8 caractere, litera mica, litera mare, cifra, caracter special) sunt pe randuri separate, rosii pana se indeplinesc, verzi dupa; la fel "Parolele introduse coincid" (si "Diferita de parola actuala" in fereastra); butonul se activeaza doar cand toate sunt verzi | |
| 12 | Comutatorul "Arata parola" | Parolele se vad / se ascund (in fereastra, inclusiv parola actuala) | |
| 13 | Setari -> Utilizatori -> Adauga utilizator (si Editeaza cu parola noua): scrie parola litera cu litera | Aceeasi lista de conditii pe randuri separate, rosii/verzi la tastare, "Parolele coincid" si comutatorul "Arata parola" | |
| 14 | Setari -> Baza de date -> Conexiune: "Schimba parola contului ..." si exportul cu fraza-parola | Conditiile (cel putin 16 caractere / 12 pentru fraza-parola, fara caractere de control) pe randuri verzi/rosii; coincidenta ramane ca pana acum | |
| 15 | Campurile "Parola" din Conexiune baza de date, Backup NAS, ActivityApp si Elcomtur | Au comutatorul "Arata parola" (fara conditii: sunt parolele altor sisteme) | |

### 06.11.2026 - Meniul utilizatorului conectat (nume in bara de sus) si schimbarea propriei parole

**Ce s-a adaugat:** numele utilizatorului conectat din bara de sus este acum un buton care deschide un meniu peste pagina, cu: Autentificatorul meu, Schimba parola (fereastra popup) si Deconectare. Legaturile separate "Autentificatorul meu" si "Deconectare" din bara de sus au fost mutate in meniu.

**Ce face acum:** orice utilizator (indiferent de drepturi) isi schimba propria parola: scrie parola actuala, apoi parola noua de doua ori (minimum 8 caractere, diferita de cea actuala). Parola gresita nu schimba nimic; 5 incercari gresite blocheaza schimbarea 10 minute. Sesiunea curenta ramane deschisa. In Jurnal apare "Schimbare parola proprie" (parola nu se scrie). Administratorul din configurare nu isi poate schimba parola de aici.

| Nr | Pas | Rezultat asteptat | Rezultat obtinut |
|---|---|---|---|
| 1 | Apasa numele tau in bara de sus | Se deschide meniul peste pagina (nume afisat, Autentificatorul meu, Schimba parola, Deconectare); clic in afara sau Escape il inchide | |
| 2 | Schimba parola cu parola actuala gresita, apoi cu parola noua prea scurta, apoi cu confirmare diferita | Mesaj clar de fiecare data, nimic nu se schimba | |
| 3 | Schimba parola corect | Mesaj de succes; la urmatoarea autentificare merge parola noua, cea veche nu; Jurnal "Schimbare parola proprie" | |
| 4 | Apasa Deconectare din meniu | Apare confirmarea "Inchizi sesiunea?"; dupa confirmare esti dus la pagina de autentificare | |
| 5 | Conectat ca administratorul din configurare: deschide meniul | "Schimba parola" este dezactivat cu explicatia ca parola e in fisierul de configurare | |

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
