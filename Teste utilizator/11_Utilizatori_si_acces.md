# Utilizatori, autentificare, roluri si blocari de editare (/utilizatori)

Fluxul: administratorul creeaza conturi (Administrator / Utilizator) -> autentificare -> meniul se adapteaza rolului -> cand doua persoane editeaza acelasi obiect apare blocarea editarii.

## Modificari de testat

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
