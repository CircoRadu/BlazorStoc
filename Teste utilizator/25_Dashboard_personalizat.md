# Dashboard personalizat (meniul numelui -> Personalizare dashboard)

## Modificari de testat (21.11.2026)

**Ce s-a adaugat:** culoare pentru spatiile libere, zone pe randuri diferite ale paginii (camp "Asezare"), slidere cu efect vizibil.
**Ce face acum:** un spatiu din caroiaj poate avea culoare (apare ca banda colorata pe dashboard); o zona poate sta pe rand nou, centrata, la stanga sau la dreapta, cu latimea de care au nevoie tile-urile ei; sliderul "Latime tile" se opreste la latimea disponibila, iar "Inaltime spatiu" regleaza spatiul dintre randuri.

| Nr | Pasi | Asteptat | Rezultat |
|---|---|---|---|
| 1 | In caroiaj apasa "+ Spatiu"; pe randul spatiului alege o culoare in campul "Culoare", salveaza, deschide Dashboard | Spatiul apare ca banda colorata; "Fara culoare" il face din nou transparent | |
| 2 | Apasa "+ Zona" de doua ori; la zona 2 alege "Pe rand nou, centrata", la zona 3 "Pe rand nou, la dreapta"; salveaza | Pe Dashboard zona 2 are latimea tile-urilor ei si sta centrata, zona 3 e la dreapta; prima zona ramane pe toata latimea | |
| 3 | La zona 3 alege "Langa zona dinainte" | Zona 3 trece pe acelasi rand cu zona 2 | |
| 4 | Prima zona nu are campul "Asezare"; muta o zona asezata pe rand nou in prima pozitie | Zona din prima pozitie ramane pe primul rand, pe toata latimea | |
| 5 | Muta sliderul "Latime tile" | Sliderul se opreste la latimea unei coloane; fiecare pas schimba latimea tile-urilor; la capat latimea e automata | |
| 6 | Muta sliderul "Inaltime spatiu" | Se schimba spatiul dintre randurile de tile-uri, in editor si pe Dashboard | |
| 7 | Muta sliderul "Latime zone" (global) | Toate zonele iau latimea aleasa si stau centrate, inclusiv prima; la capatul sliderului latimea e automata | |
| 8 | La o zona muta sliderul "Latime zona" | Doar acea zona isi schimba latimea (peste cea globala); "Latime globala" o aduce inapoi la cea globala | |
