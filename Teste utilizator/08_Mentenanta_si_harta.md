# Mentenanta, contracte, interventii si harta (/mentenanta, /mentenanta/harta)

Fluxul: Mentenanta -> puncte de lucru (adresa, poze, coordonate) -> contracte de mentenanta si acoperire -> registru de interventii -> harta cu pinuri.

## Modificari de testat

### 26.10.2026 - Fundal satelit pe harta (comutator Harta / Satelit)

**Ce s-a adaugat:** in Setari -> Harta -> Motor harta, sectiunea „Fundal satelit (optional)”: adresa dalelor satelit, text si adresa atribuirii, „Cheie satelit” si butonul „Testeaza satelitul”.

**Ce face acum:** daca adresa satelit este completata, harta mentenantei (si fereastra ei separata) si harta din editorul punctului de lucru au in dreapta-sus un comutator „Harta / Satelit”; alegerea ramane in browserul local. Atribuirea furnizorului se vede pe harta cat timp stratul e activ. Cheia satelit nu se afiseaza si nu apare in jurnal.

Pasi:

1. Setari -> Harta -> Motor harta, la „Fundal satelit”: adresa `https://ibasemaps-api.arcgis.com/arcgis/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}?token={key}`, atribuire „Esri, Maxar, Earthstar Geographics, and the GIS User Community”, cheia ArcGIS in „Cheie satelit” (in contul ArcGIS restrictioneaz-o la domeniul aplicatiei). „Testeaza satelitul” trebuie sa dea mesaj verde cu o dala; salveaza.
2. Mentenanta -> Harta si editorul unui punct de lucru (Coordonate pornit): apare comutatorul; la „Satelit” se vad cladirile; trage pinul pe cladire.
3. Goleste adresa satelit si salveaza: comutatorul dispare in cel mult 30 de secunde pe hartile deschise.

### 26.10.2026 - Harta cu pin in editorul punctului de lucru (cautarea dupa adresa Geoapify a fost scoasa la 27.10.2026; ignora pasii 1, 3 si 5)

**Ce s-a adaugat:** in Setari -> Harta -> Motor hartă, sectiunea „Cautare adrese (Geoapify)” (comutator + cheie, buton „Testeaza cheia”). In editorul punctului de lucru (fisa beneficiarului), cu comutatorul „Coordonate” pornit: butonul „Cauta coordonate dupa adresa” (doar daca cautarea e activa) si o harta cu pin.

**Ce face acum:** butonul deschide un panou cu Strada, Numar, Localitate, Judet, precompletate din adresa punctului. „Cauta” trimite serviciului Geoapify (de pe server) numai aceste patru valori; nu se trimit numele punctului, telefonul, contactul sau descrierea. Rezultatele arata adresa gasita si cat de exacta e potrivirea; „Foloseste” completeaza coordonatele si pune pinul. Pinul se poate trage sau se poate face clic pe harta; coordonatele din camp se actualizeaza, iar eticheta pinului arata numele punctului, adresa si coordonatele. Cheia nu se afiseaza si nu apare in jurnal; salvarea setarii e jurnalizata ca „Modificare cautare adrese harta”.

Pasi:

1. Setari -> Harta -> Motor hartă: la „Cautare adrese” pui cheia Geoapify, apesi „Testeaza cheia” (mesaj verde), activezi si salvezi. Verifica in Jurnal intrarea „Modificare cautare adrese harta” fara cheia in text.
2. Fisa unui beneficiar -> Adauga punct de lucru (sau editeaza unul) cu adresa de forma „Str. Exemplu 12, Timisoara, Timis”; porneste „Coordonate”: apar butonul de cautare si harta.
3. Apasa „Cauta coordonate dupa adresa”: cele 4 campuri sunt precompletate; corecteaza daca trebuie, apasa „Cauta”, alege un rezultat cu „Foloseste”: campul de coordonate se completeaza si pinul apare pe harta cu eticheta (nume, adresa, coordonate).
4. Trage pinul in alt loc sau fa clic pe harta: coordonatele din camp se schimba, iar eticheta le arata pe cele noi. Scrie coordonate in camp: pinul se muta. Salveaza punctul si redeschide-l: pinul e la pozitia salvata.
5. Cu cautarea dezactivata (sau fara cheie) butonul „Cauta coordonate dupa adresa” ramane vizibil dar inactiv, cu un mesaj rosu dedesubt (si in titlul butonului) care spune ca nu e configurata si ca un administrator trebuie sa puna cheia in Setari -> Harta -> Motor harta; harta cu pin si campul de coordonate merg. O adresa inexistenta da mesajul „Nu s-a gasit nicio adresa” si butonul „Cauta ca text liber”.

### 15.10.2026 - Mentenanta ca modul (oprire fara efect asupra restului aplicatiei)

**Verificat de agent 19.10.2026** (pe instanta de test, baza de test; vezi `docs/TESTE_RAMASE.md`, „Verificare completa a modulelor”).

**Ce s-a adaugat:** Mentenanta (contracte, interventii, harta punctelor de lucru, scadentele si expirarile din Notificari) este un modul al aplicatiei, ca Analizele de risc si Vehiculele. Daca se opreste (din configurare sau dupa erori repetate), restul aplicatiei merge. Punctele de lucru si pozele lor raman la fisa beneficiarului.

**Ce face acum:** cu modulul oprit, /mentenanta, harta si fereastra hartii arata "Modul indisponibil"; intrarea Mentenanta dispare din meniu si de pe pagina principala; fisa beneficiarului se deschide cu punctele de lucru si analizele de risc, fara sectiunile Contracte de mentenanta si Interventii (apare o nota); Setari -> Harta arata nota de modul oprit. Notificarile de mentenanta deja create raman in lista.

Pasi:

1. Cu modulul pornit (normal): Mentenanta, harta, fisa unui beneficiar cu contracte si interventii, Setari -> Harta (motor si overlay) se deschid ca pana acum.
2. Porneste aplicatia cu `Modules__mentenanta__Enabled=false`: /mentenanta arata "Modul indisponibil", meniul si pagina principala nu mai au Mentenanta, Setari -> Module arata Mentenanta "Oprit (oprit din configurare)".
3. Tot cu modulul oprit: fisa unui beneficiar arata Puncte de lucru, Analize de risc si Proiecte, dar nu Contracte de mentenanta si Interventii; Setari -> Harta arata nota; Notificari se deschide fara erori.
4. Un beneficiar sau un punct de lucru acoperit de un contract nu poate fi sters nici cu modulul oprit (mesajul obisnuit).

### 10.10.2026 - Detaliile punctului de pe harta, in fereastra popup

**Ce s-a adaugat:** detaliile unui punct (beneficiar, contract, termen, ciclicitate, scadenta, ultima interventie, butoanele) se deschid intr-o fereastra popup, nu in panoul din dreapta; harta ocupa toata latimea.

**Ce face acum:** apasarea unui pin deschide popup-ul centrat; se inchide cu ×, cu clic pe fundal sau cu Escape; „Alte puncte la aceeasi locatie" schimba continutul popup-ului. Insignele (On, In curand etc.) au padding mai mare (regula 6 px / 12 px, aplicata la toate insignele aplicatiei).

1. La un punct cu alte puncte la aceeasi locatie, apasa unul din lista: popup-ul trece la acel punct.
2. Ecran ingust: popup-ul incape fara depasire orizontala.

## Verificari ramase (fara modificari recente; detalii in docs/TESTE_RAMASE.md, sectiunile N7-N27)

| Pas | Ce faci | Ce trebuie sa vezi | Rezultat |
|---|---|---|---|
| 1 | Punct de lucru nou, incarcare poze reale, tragere cu mouse-ul | Galeria functioneaza | |
| 2 | Contract: mutarea unui punct intre contracte active; stergerea unui contract | Jurnal exact; fara blocaje | |
| 3 | Interventii: poze reale, stergerea unui punct cu interventii (rol limitat) | Mesaj corect, fara pierderi | |
| 4 | Harta: multe puncte, clusterizare, fereastra separata pe doua monitoare | Pinurile si panoul functioneaza; dale incarcate | |
| 5 | Cheie harta: schimba furnizorul pe o harta deschisa | Se reincarca fara eroare | |
| 6 | Ecran ingust: sectiunea de contracte, interventii, harta, tipuri de pinuri | Fara depasiri orizontale | |

## Fluxul de baza

1. Punct nou -> contract activ -> interventie -> apare pe harta si in notificari.
