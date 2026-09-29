# DDL MariaDB

- `schema-mariadb.sql`: schema MariaDB actuala (copie a livrarii `Livrare-DDL-MariaDB`), cu coloanele noi ale tabelului `beneficiaries`.
- `triggers-mariadb.sql`: triggerele bazei.

Fisierele cu credentiale (`admin.private.cnf`, `connection.private.json`) NU se pun in repository (`*.private.*` este ignorat).
Schema SQLite este in `Services/SqliteLocalStore.cs`.

Schimbarile de schema pe o baza existenta se aplica prin `Services/MariaSchemaMigrations.cs` cu contul `blazorstoc_migrator`
(la pornirea in mod MariaDB sau cu `dotnet BlazorStoc.dll --migrate-schema`). O schimbare noua = o migrare in lista + o actualizare a `schema-mariadb.sql`.
