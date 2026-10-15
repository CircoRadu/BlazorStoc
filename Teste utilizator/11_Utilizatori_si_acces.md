# Utilizatori, autentificare, roluri si blocari de editare (/utilizatori)

Fluxul: administratorul creeaza conturi (Administrator / Utilizator) -> autentificare -> meniul se adapteaza rolului -> cand doua persoane editeaza acelasi obiect apare blocarea editarii.

## Modificari de testat

### 15.10.2026 - Mesaj la autentificare cand baza de date nu raspunde

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
