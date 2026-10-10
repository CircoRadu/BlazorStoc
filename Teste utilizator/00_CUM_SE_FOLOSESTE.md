# Teste utilizator - cum se folosesc

Directorul contine, pentru fiecare componenta a aplicatiei, un fisier cu pasii pe care ii faci tu (utilizatorul) ca sa verifici modificarile si fluxul de lucru. Agentul nu poate verifica singur aspectul, ecranul ingust, alte browsere, ANAF real, doua conturi simultane; de aceea aceste verificari sunt aici.

## Pregatire (o singura data pe sesiune de testare)

1. Cere agentului "preview" (republica previzualizarea) sau porneste `tools/start-preview.ps1`. Adresa: `http://127.0.0.1:5087/`.
2. Autentifica-te ca administrator. Pentru testele cu drepturi limitate ai nevoie si de un cont cu rolul "Utilizator" (se creeaza din Administrare -> Utilizatori).
3. Dupa fiecare republicare apasa Ctrl+F5 (altfel browserul poate tine CSS/JS vechi).
4. Baza folosita de previzualizare este cea de dezvoltare: datele create la teste raman acolo. Foloseste produse/furnizori cu nume de forma "TEST ..." ca sa-i poti sterge usor.

## Ordinea recomandata (cand ai putin timp)

1. `02_Intrari_si_iesiri_produs.md` - cele mai multe modificari recente.
2. `03_Facturi_si_furnizori.md`
3. `04_Preluare_factura_si_sabloane.md`
4. Restul, dupa nevoie.

## Cum notezi rezultatul

- In fiecare fisier, coloana "Rezultat" se completeaza cu: OK / NU / Observatie.
- Ce nu merge: scrie pasul si ce ai vazut; agentul il transforma in task in `TODO.md`.
- Dupa ce ai testat o sectiune, spune-i agentului; el o muta in `docs/TESTE_RAMASE.md` -> "Teste efectuate" si o scoate de aici.

## Intretinere (agentul)

- Inainte de fiecare commit/push agentul actualizeaza aceste fisiere: adauga la componenta atinsa pasii pentru modificarile noi (sub "Modificari de testat", cu data) si scoate pasii confirmati de tine.
- Fisierele se scriu fara diacritice.

## Componente

| Fisier | Componenta | Pagini |
|---|---|---|
| 01_Stoc_si_produse.md | Catalog, produs, categorii | /produse, /categorii |
| 02_Intrari_si_iesiri_produs.md | Intrari/iesiri, stoc negativ, regularizare | /produse/{id}/miscari |
| 03_Facturi_si_furnizori.md | Furnizori si facturi de furnizor | /furnizori, /facturi |
| 04_Preluare_factura_si_sabloane.md | Wizard de preluare factura, sabloane | /produse/preluare-factura, /setari (Facturi) |
| 05_Inventar.md | Situatie inventar, preluare, restaurare | /inventar |
| 06_Beneficiari_si_proiecte.md | Beneficiari, proiecte, observatii | /beneficiari, /proiecte |
| 07_Vehicule.md | Vehicule, stoc din masina, expirari | /vehicule |
| 08_Mentenanta_si_harta.md | Contracte, interventii, harta | /mentenanta |
| 09_Notificari_si_setari.md | Notificari, Setari (ANAF, notificari, harta) | /notificari, /setari |
| 10_Jurnal_activitate.md | Jurnal si filtre | /jurnal |
| 11_Utilizatori_si_acces.md | Autentificare, roluri, blocari de editare | /utilizatori |
| 12_Nomenclator.md | Tipuri de sisteme | /nomenclator |
| 13_Oferte_sabloane.md | Sabloane de devize-oferta (xlsx) | /oferte/sabloane |
| 14_Oferte_preluare.md | Preluarea devizului-oferta | /oferte/preluare |
| 15_Analize_de_risc.md | Analize de risc, reinnoiri, notificare de expirare | /analize-risc, fisa beneficiarului |
| 16_Tipuri_de_utilizatori.md | Tipuri de utilizatori, permisiuni pe module/actiuni, Acces refuzat | /setari (Tipuri de utilizatori), /utilizatori |
