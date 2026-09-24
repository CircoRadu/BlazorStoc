# Arhivarea și pregătirea restaurării

Ștergerile normale nu elimină definitiv produsele, beneficiarii sau utilizatorii. Aplicația mută datele în tabelele `archive_*`, păstrează evenimentul de audit în aceeași tranzacție și, pentru imaginile produselor, păstrează o copie verificată în directorul configurat prin `App:ArchiveFilesPath`.

## Date păstrate

- `archive_operations`: identificatorul operației, tipul și identificatorul original, versiunea, momentul UTC, operatorul, rolul, motivul, ținta, detaliile și instantaneul JSON.
- `archive_products`, `archive_beneficiaries`, `archive_web_users`: valorile necesare reconstruirii fiecărui obiect.
- `archive_relations`: instantaneele relațiilor dependente introduse de tipurile de obiecte prezente sau viitoare.
- `archive_files`: calea live inițială, calea arhivei, tipul media, numele, dimensiunea și hash-ul SHA-256.
- `audit_events.archive_operation_id`: legătura stabilă dintre ștergere și operația de arhivare.
- Hash-ul parolei unui utilizator este păstrat numai în zona protejată a arhivei și nu este afișat în jurnal sau interfață.

## Verificări obligatorii înaintea unei restaurări viitoare

1. Operația există o singură dată în `archive_operations` și are exact un rând în tabela `archive_*` corespunzătoare.
2. Evenimentul de ștergere indică același `archive_operation_id`.
3. Identificatorul original nu este ocupat de un alt obiect live sau este remapat explicit într-o tranzacție controlată.
4. Cheile normalizate ale obiectului restaurat nu încalcă regulile curente de unicitate.
5. Relațiile din `archive_relations` indică obiecte live valide ori sunt restaurate în aceeași tranzacție.
6. Pentru fiecare rând din `archive_files`, fișierul există, dimensiunea corespunde și hash-ul SHA-256 recalculat este identic cu `content_hash`.
7. Fișierul este copiat în zona live și verificat înainte de confirmarea restaurării; copia din arhivă se păstrează până după confirmarea tranzacției.
8. Restaurarea scrie un eveniment de audit nou și nu modifică evenimentul istoric de ștergere.

Interfața de restaurare nu face parte din această etapă. Orice implementare viitoare trebuie să folosească o singură tranzacție pentru obiect, relații și audit și să urmeze același model sigur folosit la arhivare.
