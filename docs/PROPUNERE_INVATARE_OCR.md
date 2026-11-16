# Propunere: invatare OCR la preluarea inventarului

Data: 16.11.2026. Stare: propunere, neimplementata. Modul afectat: `Modules/Inventory/` (versiune minor) si `Services/InventoryPickupOcr.cs`.

## 1. Problema

Valoarea scrisa de mana din coloana "Valoare reala" se citeste cu modelul ONNX `mnist-12`: fiecare cifra este decupata ca blob, clasificata, iar aplicatia retine doar cifra cea mai probabila (prag de incredere 0.80). Modelul e antrenat pe cifre americane, de aceea apar confuzii dese: 1 citit ca 4, 2 citit ca 5 etc.

Utilizatorul corecteaza valorile in pagina de preluare (`editedValues` in `InventoryPickup.razor`), dar aplicatia nu pastreaza nicio comparatie intre ce a citit OCR si ce a trimis utilizatorul. Corectiile se pierd, deci citirea nu se imbunatateste.

## 2. Scop

1. Se salveaza lista initiala asa cum a propus-o OCR-ul.
2. La preluarea in stoc se compara cu lista finala a utilizatorului si se identifica valorile citite gresit.
3. Se genereaza o lista cu capturi din PDF-ul scanat (caracterul problematic) si valoarea corectata.
4. Utilizatorul selecteaza din lista ce se invata.
5. Citirea unui formular nou tine cont de deciziile salvate.

## 3. Principii

- Corectia unei valori nu spune care cifra a fost gresita: se compara pe cifre doar cand numarul de cifre coincide (citit 45, corectat 15 -> 4 a fost gresit, 5 a fost corect).
- Cand numarul de cifre difera (cifra pierduta sau in plus) cazul este de segmentare si se salveaza separat, fara etichete pe cifre.
- O corectie nu este automat o eroare OCR (utilizatorul poate fi numarat din nou), de aceea invatarea porneste numai din elementele selectate de utilizator.
- Invatarea se poate reseta oricand la modelul de baza.

## 4. Etape

### Etapa 1 - Captura la scanare

La `ScanAsync`, pentru fiecare cifra se retin: decuparea 28x28 (PNG mic), distributia de probabilitati (top 3), pagina, `RawCode`, pozitia cifrei in numar si decuparea intregii celule "Valoare reala" (pentru afisare in lista).

Se salveaza ca snapshot al listei initiale, legat de o sesiune de preluare (id, data, fisier). Imaginile stau in folderul de date al aplicatiei, in baza ramane doar metadata.

### Etapa 2 - Comparatie la preluarea in stoc

In `ApplyAsync`, per rand:

- valoare identica: confirmat corect (nu intra in lista de invatare);
- acelasi numar de cifre: se evidentiaza cifrele diferite, de exemplu `4 -> 1` pe pozitia 1;
- numar diferit de cifre: marcat "segmentare", fara etichete pe cifre.

Rezultatul se scrie in tabele noi `ocr_learning_sessions` si `ocr_learning_samples` (migrare numerotata). Jurnalul primeste actiuni specifice: "Salvare corectii OCR", "Invatare OCR", "Ignorare corectie OCR", "Resetare invatare OCR" (in `AuditActions`, filtrul paginii Audit, `IsCreateOrEdit`, cu test).

### Etapa 3 - Lista "Invatare OCR"

Tab nou in Inventar, tabel paginat (`Paged`, `SortTh`):

- coloane: captura celulei din PDF cu cifra problematica evidentiata, citit de OCR, corectat de utilizator, pagina, data, stare (nou / invatat / ignorat);
- selectie pe randuri cu `RowSelect` si "Selecteaza tot";
- butoane: "Invata din selectie", "Ignora", "Reseteaza invatarea";
- filtru dupa perechea de confuzie (de exemplu 1->4);
- topic `.help` si intrare in `docs/HELP_DE_REGENERAT.md`.

### Etapa 4 - Folosirea invatarii la citire

Doua straturi, primul mai simplu:

- **A. Calibrare pe confuzii.** Din esantioanele invatate se construieste o matrice de confuzie (P(real=1 | citit=4)). Cand OCR citeste o cifra cu incredere scazuta si istoricul arata ca de obicei e alta, randul se marcheaza "de verificat" si se propune valoarea corectata. Functioneaza cu putine exemple si este explicabil.
- **B. Clasificator pe imaginile salvate (k-NN).** Imaginile decupate si etichetate de utilizator formeaza un set de exemple; o cifra noua se compara cu ele (distanta pe vectorul 28x28 sau pe trasaturile modelului ONNX). Daca cel mai apropiat exemplu este sub un prag de distanta, decide acesta, altfel decide modelul de baza. Se adapteaza direct la scrisul utilizatorilor, fara pachete noi si fara reantrenarea modelului ONNX.

Ordinea de citire pe un formular nou: exemple invatate (B), apoi calibrare (A), apoi modelul de baza. Un rand a carui cifra a fost modificata de invatare se poate marca vizibil, ca utilizatorul sa stie de ce s-a schimbat.

### Etapa 5 - Teste (trasee critice)

1. Citit 45, corectat 15 -> esantion `4 -> 1`.
2. Numar diferit de cifre -> marcat segmentare.
3. Invatare dintr-un esantion selectat -> aceeasi imagine se citeste acum corect.
4. Esantion ignorat -> nu influenteaza citirea.
5. Citire fara istoric -> comportament neschimbat (regresie).

Se ruleaza tintit (`tools\run-checks.ps1`, sectiunea/grupul pentru inventar si OCR), plus verificare in browser pe instanta de test.

## 5. Decizii de luat

1. **Mecanism:** A (calibrare + sugestie) mai intai, sau direct B (k-NN)? Recomandare: etapele 1-3 si A, apoi B cand exista suficiente exemple (aprox. 100 pe cifra).
2. **Domeniu:** invatare globala (toate scanarile) sau per utilizator? Recomandare: globala, selectia o face un administrator.
3. **Retentie:** limita de esantioane active (de exemplu 1000) si stergerea celor vechi, ca spatiul sa nu creasca nelimitat.
4. **Confidentialitate:** capturile contin doar celule cu cifre scrise de mana si codul produsului, nu date ale clientilor; se pastreaza local si intra in backup (regula "backup include fisierele").

## 6. Riscuri

- Prea putine exemple: k-NN poate decide gresit; de aceea pragul de distanta si revenirea la modelul de baza.
- Corectii care nu sunt erori OCR polueaza setul: de aceea selectia manuala.
- Scris foarte diferit intre persoane: calibrarea globala poate degrada citirea pentru un scris atipic; se poate adauga ulterior invatare per utilizator.
- Cresterea timpului de scanare: comparatia k-NN pe cateva mii de vectori 784 este neglijabila fata de OCR-ul actual.
