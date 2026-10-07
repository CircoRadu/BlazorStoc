# Notificari si Setari (/notificari, /setari)

Fluxul: Setari (administrator: ANAF, notificari, harta, Facturi) -> sabloane de notificari -> /notificari arata alertele active, rezolvate si ordonate dupa urgenta; meniul are un contor.

## Modificari de testat

### 07.10.2026 - Ieșiri peste stoc nerezolvate

**Ce s-a adaugat:** sursa de notificari "Ieșiri peste stoc nerezolvate" (cheie `stoc.iesiri-peste-stoc`): o notificare pe produs cu stoc negativ dupa iesiri peste stoc; termen = data primei iesiri neacoperite + 14 zile; prag implicit 7 zile; se inchide singura cand stocul nu mai e negativ. Marcajele din sablon: produs, data prima iesire, numar iesiri, cauza.

**Ce face acum:** Notificarile si contorul de pe pagina principala te anunta cand exista iesiri peste stoc nerezolvate.

| Pas | Ce faci | Ce trebuie sa vezi | Rezultat |
|---|---|---|---|
| 1 | Setari -> Notificari: sablon nou pentru sursa "Ieșiri peste stoc nerezolvate" | Marcajele <produs>, <data prima iesire>, <numar iesiri>, <cauza> sunt disponibile (inserate la cursor) | |
| 2 | Un produs negativ cu prima iesire peste stoc mai veche de 7 zile | Apare pe /notificari cu link catre pagina de miscari a produsului | |
| 3 | Regularizeaza produsul | Notificarea se inchide singura (apare la rezolvate) | |

### 07.10.2026 - Factura asteptata

**Ce s-a adaugat:** Sablonul de notificare „Factura asteptata" cu prag in zile.

**Ce face acum:** Notificarea apare cand o intrare asteapta factura peste prag; sablonul se poate modifica din Setari.


| Pas | Ce faci | Ce trebuie sa vezi | Rezultat |
|---|---|---|---|
| 1 | Setari -> notificari: sablonul "Factura asteptata" (prag 7 zile) | Se poate modifica; comutatoarele On/Off functioneaza | |
| 2 | Intrari libere vechi de la un furnizor | Notificarea apare pe /notificari si contorul de pe pagina principala | |
| 3 | Atasezi intrarile la o factura | Notificarea se rezolva singura | |

### 06.10.2026 - Comutatoare On/Off

**Ce s-a adaugat:** Orice bifare din Setari si notificari este comutatorul On/Off, nu o caseta de bifat.

**Ce face acum:** ANAF, sabloanele de notificari si contractele folosesc comutatoare On/Off; nu mai exista casete de bifat.


| Pas | Ce faci | Ce trebuie sa vezi | Rezultat |
|---|---|---|---|
| 1 | ANAF, sabloane de notificari, contracte ("Arata si contractele Off") | Nu exista casete de bifat, doar comutatoare | |
| 2 | Cont "Utilizator" in Setari | Numai tabul Facturi | |

### Verificari anterioare (docs/TESTE_RAMASE.md N1-N6, N17-N18)

**Ce s-a adaugat:** Verificari anterioare ale notificarilor si setarilor.

**Ce face acum:** Pagina /notificari si Setari notificari functioneaza si pe ecran ingust, fara depasiri.


| Pas | Ce faci | Ce trebuie sa vezi | Rezultat |
|---|---|---|---|
| 1 | /notificari si Setari notificari pe ecran ingust | Fara depasiri | |
| 2 | Curatarea rezolvatelor | Dispar dupa interval | |
| 3 | Rulare zilnica pe ceas real | Notificari noi la ora setata | |

## Fluxul de baza

1. Data de expirare apropiata -> notificare -> rezolvare -> dispare automat.
