# Mentenanta, contracte, interventii si harta (/mentenanta, /mentenanta/harta)

Fluxul: Mentenanta -> puncte de lucru (adresa, poze, coordonate) -> contracte de mentenanta si acoperire -> registru de interventii -> harta cu pinuri.

## Modificari de testat

### 07.11.2026 - Scadente pe componente, interventii cu componente si acte aditionale (migrarile 77, 78, 79; branch claude/acte-aditionale-componente-mentenanta)

**Ce s-a adaugat:** (1) fiecare punct acoperit are o scadenta pentru fiecare familie / componenta a contractului (scadenta punctului ramane cea mai apropiata); lista Scadente si fisa beneficiarului arata scadentele pe componente, iar notificarile de scadenta sunt cate una pe punct si componenta. (2) Interventia de mentenanta alege componentele acoperite (formularul din fisa beneficiarului si fereastra tichetului preluat de operator): scadentele se muta numai pentru componentele alese. (3) Actul aditional se adauga din fisa beneficiarului (iconita + de pe randul contractului) si poate modifica valabilitatea, ciclicitatea, data de referinta, componentele (On/Off), punctele sau alte clauze; actele apar in randul expandat al contractului, cu anulare (iconita) si stergere.

**Ce face acum:** o schimbare de componente / referinta / ciclicitate printr-un act propune scadentele pe baza ultimei interventii care a acoperit fiecare componenta; interventiile fara componente pot fi verificate in fereastra actului.

1. Fisa beneficiarului -> Contracte -> extinde un contract complet: tabelul punctelor are coloana "PE COMPONENTE" cu chip-uri `Componenta · data` (rosu depasita, galben in curand). Mentenanta -> Scadente: la un punct cu mai multe componente, sub data apar chip-urile lor.
2. Notificari -> Active: o scadenta de mentenanta apare pentru fiecare pereche punct si componenta (titlul are si componenta).
3. Fisa beneficiarului -> Intervenție nouă -> Mentenanta, alege un punct dintr-un contract complet: apare "Componente acoperite *" cu componentele contractului; fara nicio componenta alesa salvarea este refuzata. Alege una (de exemplu TVCI): se muta scadenta numai a componentei alese, celelalte raman. La editarea intervenției se vad componentele (doar citire).
4. La un punct fara contract complet (sau contract inghetat) lista contine toate componentele din nomenclator (nu sunt obligatorii) si scadentele nu se muta.
5. Notificari -> Tichete de validat -> Editeaza si preia: la categoria Mentenanta apare aceeasi lista de componente (obligatorie sub contract complet) si se schimba cand schimbi punctul de lucru. "Preia ca propus" muta scadentele tuturor componentelor punctului (nu are alegere).
6. Corectarea datei ultimei interventii si stergerea ei readuc exact scadentele componentelor mutate de ea.
7. Fisa beneficiarului -> randul contractului -> iconita + (Adauga act aditional): alege Data de referinta si/sau Componente (On/Off, adauga familii sau componente din nomenclator, ciclicitate si referinta pe familie / componenta); sub "Intervenții de mentenanță de verificat" alege componentele intervențiilor vechi; tabelul punctelor propune scadentele noi, pe care le poti modifica; rezumatul "Inainte -> Dupa" arata schimbarile. Salveaza si aplica.
8. Dupa act, contractul are componentele si data de referinta noi, randul expandat arata actul (Aplicat) cu "componente: +N -M" / "data de referinta: ... -> ..."; iconita de anulare (doar la ultimul act aplicat) readuce componentele si referinta de dinainte; un act anulat se poate sterge.
9. Jurnal activitate: actul, anularea lui si modificarile de componente apar cu actiunile proprii.

### 07.11.2026 - Contracte de mentenanta cu componente, data de referinta si contracte inghetate (branch claude/acte-aditionale-componente-mentenanta, migrarea 76)

**Ce s-a adaugat:** formularul de contract (fisa beneficiarului -> Contracte de mentenanta) are data de referinta (obligatorie), ciclicitate globala (optionala) si sectiunea "Componente acoperite": familii (cu elementele On/Off) si componente discrete din Nomenclator -> Componente mentenanta, fiecare cu ciclicitate si data de referinta proprii sau globale. Un contract careia ii lipsesc date este INGHETAT: fara scadente (lista, harta), fara notificari de scadenta, iar interventiile lui nu muta scadente. Notificari are un tab nou "Contracte incomplete" (rosu cand exista contracte On inghetate).

**Ce face acum:** toate contractele existente au devenit inghetate la actualizare (nu au data de referinta si nici componente); fiecare apare in tabul Notificari -> Contracte incomplete cu ce lipseste si se dezgheata singur dupa ce il completezi.

1. Nomenclator -> Componente mentenanta: ai o familie (de exemplu Securitate fizica cu Efractie, TVCI, Control acces) si cateva componente discrete (Incendiu, Retelistica). Daca nu, adauga-le.
2. Notificari -> tabul "Contracte incomplete": apare rosu, cu numarul contractelor On inghetate; lista arata beneficiarul, contractul si "Data de referinta (...)" / "Nicio componenta sau familie activa in contract"; iconita de pe rand deschide beneficiarul.
3. Mentenanta -> Scadente: un contract inghetat nu are scadente in lista si pe harta; in fisa beneficiarului randul lui are insigna "Inghetat" (indicatia arata ce lipseste) si "inghetat: lipsesc date" la urmatoarea scadenta.
4. Editeaza un contract inghetat: formularul arata "Contractul va fi inghetat ... pana se completeaza: ...". Alege data de referinta, adauga familia Securitate fizica: apar elementele ei cu comutatoare; opreste TVCI; alege pentru familie ciclicitatea 4 luni; adauga Incendiu (ciclicitatea ramane "Globala"). Nota despre inghetare dispare. Salveaza: contractul nu mai este inghetat, coloana COMPONENTE arata insignele (indicatia insignei familiei spune "TVCI (Off)" si ciclicitatea), scadenta reapare in lista.
5. In formular, o componenta dintr-o familie nu se poate alege separat (lista de adaugare are doar familii si componente discrete); o componenta inactiva in nomenclator nu apare. Poti scoate o componenta cu iconita cos si poti reveni la data de referinta globala cu iconita X de langa data unitatii.
6. Salvarea fara data de referinta este refuzata cu mesaj. Dupa ce opresti singura componenta activa, contractul se ingheata din nou.
7. Jurnal activitate: operatiile apar cu actiuni proprii: "Modificare componente contract mentenanta", "Modificare data de referinta contract mentenanta", "Inghetare contract mentenanta" (cu ce lipseste) si "Dezghetare contract mentenanta".
8. Inregistreaza o interventie de mentenanta pe un punct dintr-un contract inghetat: se salveaza sub contract, iar mesajul din jurnal spune ca scadenta nu s-a modificat (contract inghetat).

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

### 10.11.2026 - Tichete decise cat timp Mentenanta e oprita (Maintenance 1.11.1)

**Ce face acum:** Mentenanta are un worker care, la fiecare minut cat modulul ruleaza, inregistreaza interventiile tichetelor ActivityApp decise cat timp era oprita, in ordinea datelor efectuate; scadentele contractelor urmeaza ultimele tichete preluate. Pasii sunt in 18_Preluare_tichete_ActivityApp.md (modificari 10.11.2026).

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
