# Indexul arhivei

Fisierele de aici sunt istoric, mutat din fisierele active la 07.10.2026 fara pierderi (aceleasi intrari, aceeasi ordine). Nu se citesc integral: se cauta cu `rg -n '<titlu sau cuvant>' docs/arhiva`, apoi se citeste intervalul gasit.

| Fisier | Ce contine | Pana la |
|---|---|---|
| `IMPLEMENTED_pana_la_06.10.2026.md` | taskurile finalizate (95 de intrari, cronologic) | 06.10.2026 |
| `AGENT_CHANGELOG_pana_la_06.10.2026.md` | jurnalul colaborarii Codex-Claude (54 de intrari) | 06.10.2026 |
| `VALIDARE_pana_la_30.09.2026.md` | verificari istorice pe versiuni | 30.09.2026 |
| `PROJECT_STATE_pana_la_07.10.2026.md` | starea proiectului, cu afirmatii istorice contradictorii (de exemplu motor A/B, SQLite); codul actual are prioritate | 07.10.2026 |

Regula de intretinere: cand `IMPLEMENTED.md`, `docs/AGENT_CHANGELOG.md` sau `VALIDARE.md` depasesc ~40 KB, intrarile mai vechi se muta aici (fisier nou `<nume>_pana_la_<data>.md`, plus un rand in acest tabel), ramanand in fisierul activ ultimele 10-15 intrari.
