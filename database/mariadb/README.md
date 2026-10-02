# DDL MariaDB

- `schema-mariadb.sql`: schema MariaDB actuala (copie a livrarii `Livrare-DDL-MariaDB`), cu coloanele noi ale tabelului `beneficiaries`.
- `triggers-mariadb.sql`: triggerele bazei.

Fisierele cu credentiale ale instantei (`admin.private.cnf`) nu se pun in repository; `local-secrets/` (`*.private.*`) se comite, repository-ul fiind privat (vezi docs/SETUP_DEZVOLTARE.md).
Schema SQLite este in `Services/SqliteLocalStore.cs`.

Schimbarile de schema pe o baza existenta se aplica prin `Services/MariaSchemaMigrations.cs` cu contul `blazorstoc_migrator`
(la pornirea in mod MariaDB sau cu `dotnet BlazorStoc.dll --migrate-schema`). O schimbare noua = o migrare in lista + o actualizare a `schema-mariadb.sql`.
