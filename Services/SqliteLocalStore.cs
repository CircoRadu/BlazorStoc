using System.Data;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace BlazorStoc.Services;

public sealed class SqliteLocalStore(IWebHostEnvironment environment, IConfiguration configuration,
    ILogger<SqliteLocalStore> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim initializationGate = new(1, 1);
    private volatile bool initialized;

    public string DatabasePath { get; } = Path.GetFullPath(configuration["App:LocalDatabasePath"] ??
        Path.Combine(environment.ContentRootPath, "data", "blazorstoc-local.db"));
    public string ProductImagesPath { get; } = Path.GetFullPath(configuration["App:ProductImagesPath"] ??
        Path.Combine(environment.ContentRootPath, "data", "product-images"));
    public string ProjectFilesPath { get; } = Path.GetFullPath(configuration["App:ProjectFilesPath"] ??
        Path.Combine(environment.ContentRootPath, "data", "project-files"));
    public string ArchiveFilesPath { get; } = Path.GetFullPath(configuration["App:ArchiveFilesPath"] ??
        Path.Combine(environment.ContentRootPath, "data", "archive", "files"));
    private string LegacyAuditPath { get; } = Path.GetFullPath(configuration["App:AuditPath"] ??
        Path.Combine(environment.ContentRootPath, "data", "audit-events.jsonl"));

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (initialized) return;
        await initializationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (initialized) return;
            Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
            Directory.CreateDirectory(ProductImagesPath);
            Directory.CreateDirectory(ProjectFilesPath);
            Directory.CreateDirectory(ArchiveFilesPath);
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await ConfigureConnectionAsync(connection, cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, null, SchemaSql, cancellationToken).ConfigureAwait(false);
            await EnsureAuditArchiveOperationColumnAsync(connection, cancellationToken).ConfigureAwait(false);
            // Individual / legal person beneficiaries: older databases receive the new columns here.
            foreach (var (column, definition) in new[]
                     {
                         ("kind", "TEXT NOT NULL DEFAULT 'PJ'"), ("address", "TEXT NOT NULL DEFAULT ''"), ("phone", "TEXT NOT NULL DEFAULT ''"),
                         ("registry_number", "TEXT NOT NULL DEFAULT ''"), ("postal_code", "TEXT NOT NULL DEFAULT ''"),
                         ("caen_code", "TEXT NOT NULL DEFAULT ''"), ("anaf_verified", "INTEGER NOT NULL DEFAULT 0")
                     })
                await EnsureColumnAsync(connection, "beneficiaries", column,
                    $"ALTER TABLE beneficiaries ADD COLUMN {column} {definition}", cancellationToken).ConfigureAwait(false);
            await EnsureColumnAsync(connection, "stock_movements", "project_id",
                "ALTER TABLE stock_movements ADD COLUMN project_id INTEGER NULL REFERENCES projects(id) ON DELETE RESTRICT",
                cancellationToken).ConfigureAwait(false);
            // Exit destination (1 beneficiary, 2 vehicle, 3 generic sale, 4 stock correction), destination vehicle and source
            // vehicle (null = the warehouse); older databases receive them here.
            foreach (var (column, definition) in new[]
                     {
                         ("destination", "INTEGER NULL"),
                         ("vehicle_id", "INTEGER NULL REFERENCES vehicles(id) ON DELETE RESTRICT"),
                         ("source_vehicle_id", "INTEGER NULL REFERENCES vehicles(id) ON DELETE RESTRICT")
                     })
                await EnsureColumnAsync(connection, "stock_movements", column,
                    $"ALTER TABLE stock_movements ADD COLUMN {column} {definition}", cancellationToken).ConfigureAwait(false);
            foreach (var (column, definition) in new[]
                     {
                         ("destination", "INTEGER NULL"), ("vehicle_id", "INTEGER NULL"), ("source_vehicle_id", "INTEGER NULL")
                     })
                await EnsureColumnAsync(connection, "archive_stock_movements", column,
                    $"ALTER TABLE archive_stock_movements ADD COLUMN {column} {definition}", cancellationToken).ConfigureAwait(false);
            foreach (var (column, definition) in new[]
                     {
                         ("kind", "INTEGER NOT NULL DEFAULT 1"), ("movement_date", "TEXT NOT NULL DEFAULT ''"),
                         ("description", "TEXT NOT NULL DEFAULT ''"), ("operator", "TEXT NOT NULL DEFAULT ''"),
                         ("version", "INTEGER NOT NULL DEFAULT 0"), ("updated_utc", "TEXT NOT NULL DEFAULT ''")
                     })
                await EnsureColumnAsync(connection, "stock_movements", column,
                    $"ALTER TABLE stock_movements ADD COLUMN {column} {definition}", cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, null,
                "CREATE INDEX IF NOT EXISTS ix_stock_movements_product ON stock_movements(product_id,movement_date,id)",
                cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, null, """
                CREATE INDEX IF NOT EXISTS ix_stock_movements_vehicle ON stock_movements(vehicle_id);
                CREATE INDEX IF NOT EXISTS ix_stock_movements_source_vehicle ON stock_movements(source_vehicle_id);
                """, cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, null, """
                INSERT INTO app_metadata(key,value) VALUES('schema_version','10')
                ON CONFLICT(key) DO UPDATE SET value=excluded.value;
                """, cancellationToken).ConfigureAwait(false);
            await SeedIfNeededAsync(connection, cancellationToken).ConfigureAwait(false);
            await ImportLegacyAuditIfNeededAsync(connection, cancellationToken).ConfigureAwait(false);
            await ReconcileLegacyProductStateAsync(connection, cancellationToken).ConfigureAwait(false);
            await NormalizeExistingDiacriticsAsync(connection, cancellationToken).ConfigureAwait(false);
            await CreateStockBaselineMovementsAsync(connection, cancellationToken).ConfigureAwait(false);
            // Created last, so the one-time seeding and migrations above do not produce change events (Task 8).
            await ExecuteAsync(connection, null, ChangeEventTriggers.SqliteSchema(), cancellationToken).ConfigureAwait(false);
            initialized = true;
        }
        finally { initializationGate.Release(); }
    }

    public async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ConfigureConnectionAsync(connection, cancellationToken).ConfigureAwait(false);
        return connection;
    }

    internal static async Task InsertAuditAsync(SqliteConnection connection, SqliteTransaction? transaction,
        AuditWrite entry, CancellationToken cancellationToken)
    {
        await using var command = Command(connection, transaction, """
            INSERT INTO audit_events
                (id,timestamp_utc,actor_username,actor_role,entity_type,action,target,details,motif,entity_id,archive_operation_id)
            VALUES (@id,@timestamp,@actor,@role,@entity,@action,@target,@details,@motif,@entityId,@archiveOperationId)
            """, ("@id", Guid.NewGuid().ToString("D")), ("@timestamp", DateTime.UtcNow.ToString("O")),
            ("@actor", entry.ActorUsername), ("@role", entry.ActorRole), ("@entity", entry.EntityType),
            ("@action", AuditActions.Normalize(entry.Action)), ("@target", entry.Target), ("@details", entry.Details),
            ("@motif", entry.Motif ?? string.Empty), ("@entityId", entry.EntityId ?? string.Empty),
            ("@archiveOperationId", entry.ArchiveOperationId?.ToString("D")));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnsureAuditArchiveOperationColumnAsync(SqliteConnection connection, CancellationToken token)
    {
        var exists = false;
        await using var columns = Command(connection, null, "PRAGMA table_info(audit_events)");
        await using var reader = await columns.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
            if (string.Equals(reader.GetString(1), "archive_operation_id", StringComparison.OrdinalIgnoreCase)) exists = true;
        await reader.DisposeAsync().ConfigureAwait(false);
        if (!exists)
            await ExecuteAsync(connection, null, "ALTER TABLE audit_events ADD COLUMN archive_operation_id TEXT NULL", token)
                .ConfigureAwait(false);
        await ExecuteAsync(connection, null,
            "CREATE INDEX IF NOT EXISTS ix_audit_events_archive_operation ON audit_events(archive_operation_id)", token)
            .ConfigureAwait(false);
    }

    // Adds a column to an existing table when a schema addition (like stock_movements.project_id) predates the
    // table's original creation; CREATE TABLE IF NOT EXISTS alone would not reach databases created before it.
    private static async Task EnsureColumnAsync(SqliteConnection connection, string table, string column,
        string alterSql, CancellationToken token)
    {
        var exists = false;
        await using (var columns = Command(connection, null, $"PRAGMA table_info({table})"))
        await using (var reader = await columns.ExecuteReaderAsync(token).ConfigureAwait(false))
            while (await reader.ReadAsync(token).ConfigureAwait(false))
                if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase)) exists = true;
        if (!exists) await ExecuteAsync(connection, null, alterSql, token).ConfigureAwait(false);
    }

    internal static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction, string sql,
        params (string Name, object? Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }

    private SqliteConnection CreateConnection() => new(new SqliteConnectionStringBuilder
    {
        DataSource = DatabasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Cache = SqliteCacheMode.Shared,
        Pooling = true,
        DefaultTimeout = 5
    }.ToString());

    private static async Task ConfigureConnectionAsync(SqliteConnection connection, CancellationToken token)
    {
        await ExecuteAsync(connection, null, "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;", token).ConfigureAwait(false);
    }

    private static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction? transaction, string sql,
        CancellationToken token)
    {
        await using var command = Command(connection, transaction, sql);
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    // One-time migration: products that already carry stock but have no movements receive an opening movement, so the
    // stock equals the sum of the movements from now on. Runs once (marker in app_metadata), never for later deletions.
    private static async Task CreateStockBaselineMovementsAsync(SqliteConnection connection, CancellationToken token)
    {
        await using (var check = Command(connection, null,
            "SELECT 1 FROM app_metadata WHERE key='stock_movements_baseline' LIMIT 1"))
            if (await check.ExecuteScalarAsync(token).ConfigureAwait(false) is not null) return;
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, token).ConfigureAwait(false);
        try
        {
            var now = DateTime.UtcNow;
            await using (var insert = Command(connection, transaction, """
                INSERT INTO stock_movements(product_id,quantity,created_utc,kind,movement_date,description,operator,version,updated_utc)
                SELECT p.id,ABS(p.quantity),@now,CASE WHEN p.quantity>0 THEN 1 ELSE 0 END,@date,'Stoc initial','sistem',0,@now
                FROM products p
                WHERE p.quantity<>0 AND NOT EXISTS(SELECT 1 FROM stock_movements m WHERE m.product_id=p.id)
                """, ("@now", now.ToString("O")), ("@date", StockMovementRules.StorageDate(DateOnly.FromDateTime(now.ToLocalTime())))))
                await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            await using (var marker = Command(connection, transaction,
                "INSERT INTO app_metadata(key,value) VALUES('stock_movements_baseline',@value)", ("@value", now.ToString("O"))))
                await marker.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            await transaction.CommitAsync(token).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task SeedIfNeededAsync(SqliteConnection connection, CancellationToken token)
    {
        await using (var check = Command(connection, null,
            "SELECT value FROM app_metadata WHERE key='seed_version' LIMIT 1"))
            if (await check.ExecuteScalarAsync(token).ConfigureAwait(false) is not null) return;

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, token).ConfigureAwait(false);
        try
        {
            foreach (var product in DemoProductRepository.InitialProducts())
            {
                var categoryId = await EnsureCategoryAsync(connection, transaction, product.Category, token).ConfigureAwait(false);
                var subcategoryId = await EnsureSubcategoryAsync(connection, transaction, categoryId, product.Subcategory, token).ConfigureAwait(false);
                await using var insert = Command(connection, transaction, """
                    INSERT INTO products(id,category_id,subcategory_id,name,normalized_name,description,quantity,version)
                    VALUES(@id,@category,@subcategory,@name,@normalized,@description,@quantity,@version)
                    """, ("@id", product.Id), ("@category", categoryId), ("@subcategory", subcategoryId),
                    ("@name", product.Name), ("@normalized", TextNormalization.UniquenessKey(product.Name)),
                    ("@description", product.Description), ("@quantity", product.Quantity), ("@version", product.Version));
                await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }

            foreach (var beneficiary in new[]
                     {
                         new Beneficiary(1, "Construct Demo SRL", "RO10000001", 0, BeneficiaryKinds.Legal, "Strada Demo 1, Bucuresti", "0721000001"),
                         new Beneficiary(2, "Atelier Tehnic SRL", "RO10000002", 0, BeneficiaryKinds.Legal, "Strada Demo 2, Cluj-Napoca", "0721000002"),
                         new Beneficiary(3, "Servicii Industriale SA", "10000003", 0, BeneficiaryKinds.Legal, "Strada Demo 3, Timisoara", "0721000003")
                     })
            {
                await using var insert = Command(connection, transaction, """
                    INSERT INTO beneficiaries(id,name,normalized_name,cui,normalized_cui,kind,address,phone,version)
                    VALUES(@id,@name,@normalizedName,@cui,@normalizedCui,@kind,@address,@phone,0)
                    """, ("@id", beneficiary.Id), ("@name", beneficiary.Name), ("@kind", beneficiary.Kind),
                    ("@address", beneficiary.Address), ("@phone", beneficiary.Phone),
                    ("@normalizedName", TextNormalization.UniquenessKey(beneficiary.Name)), ("@cui", beneficiary.Cui),
                    ("@normalizedCui", TextNormalization.UniquenessKey(beneficiary.Cui)));
                await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }

            await InsertUserAsync(connection, transaction, 1, "administrator.demo", "Administrator demonstratie",
                AccessRoles.Administrator, "admin-demo-123", token).ConfigureAwait(false);
            await InsertUserAsync(connection, transaction, 2, "utilizator.demo", "Utilizator demonstratie",
                AccessRoles.LimitedUser, "utilizator-demo-123", token).ConfigureAwait(false);
            await using var marker = Command(connection, transaction,
                "INSERT INTO app_metadata(key,value) VALUES('seed_version','1')");
            await marker.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            await transaction.CommitAsync(token).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<int> EnsureCategoryAsync(SqliteConnection connection, SqliteTransaction transaction,
        string name, CancellationToken token)
    {
        var normalized = TextNormalization.UniquenessKey(name);
        await using (var select = Command(connection, transaction,
            "SELECT id FROM categories WHERE normalized_name=@normalized", ("@normalized", normalized)))
            if (await select.ExecuteScalarAsync(token).ConfigureAwait(false) is long existing) return checked((int)existing);
        await using var insert = Command(connection, transaction,
            "INSERT INTO categories(name,normalized_name) VALUES(@name,@normalized); SELECT last_insert_rowid();",
            ("@name", name), ("@normalized", normalized));
        return checked((int)(long)(await insert.ExecuteScalarAsync(token).ConfigureAwait(false))!);
    }

    private static async Task<int> EnsureSubcategoryAsync(SqliteConnection connection, SqliteTransaction transaction,
        int categoryId, string name, CancellationToken token)
    {
        var normalized = TextNormalization.UniquenessKey(name);
        await using (var select = Command(connection, transaction,
            "SELECT id FROM subcategories WHERE normalized_name=@normalized", ("@normalized", normalized)))
            if (await select.ExecuteScalarAsync(token).ConfigureAwait(false) is long existing) return checked((int)existing);
        await using var insert = Command(connection, transaction,
            "INSERT INTO subcategories(category_id,name,normalized_name) VALUES(@category,@name,@normalized); SELECT last_insert_rowid();",
            ("@category", categoryId), ("@name", name), ("@normalized", normalized));
        return checked((int)(long)(await insert.ExecuteScalarAsync(token).ConfigureAwait(false))!);
    }

    private static async Task InsertUserAsync(SqliteConnection connection, SqliteTransaction transaction, int id,
        string username, string displayName, string role, string password, CancellationToken token)
    {
        await using var insert = Command(connection, transaction, """
            INSERT INTO web_users(id,username,normalized_username,display_name,password_hash,role,is_active,version)
            VALUES(@id,@username,@normalized,@displayName,@passwordHash,@role,1,0)
            """, ("@id", id), ("@username", username), ("@normalized", TextNormalization.UniquenessKey(username)),
            ("@displayName", displayName), ("@passwordHash", DemoUserRepository.HashPassword(password)), ("@role", role));
        await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    private async Task ImportLegacyAuditIfNeededAsync(SqliteConnection connection, CancellationToken token)
    {
        await using (var marker = Command(connection, null,
            "SELECT value FROM app_metadata WHERE key='legacy_audit_imported' LIMIT 1"))
            if (await marker.ExecuteScalarAsync(token).ConfigureAwait(false) is not null) return;

        var lines = File.Exists(LegacyAuditPath)
            ? await File.ReadAllLinesAsync(LegacyAuditPath, token).ConfigureAwait(false)
            : [];
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, token).ConfigureAwait(false);
        try
        {
            foreach (var line in lines)
            {
                AuditEvent? entry;
                try { entry = JsonSerializer.Deserialize<AuditEvent>(line, JsonOptions); }
                catch (JsonException) { continue; }
                if (entry is null) continue;
                await using var insert = Command(connection, transaction, """
                    INSERT OR IGNORE INTO audit_events
                        (id,timestamp_utc,actor_username,actor_role,entity_type,action,target,details,motif,entity_id)
                    VALUES(@id,@timestamp,@actor,@role,@entity,@action,@target,@details,@motif,@entityId)
                    """, ("@id", entry.Id.ToString("D")), ("@timestamp", ToUtc(entry.TimestampUtc).ToString("O")),
                    ("@actor", entry.ActorUsername), ("@role", entry.ActorRole), ("@entity", entry.EntityType),
                    ("@action", AuditActions.Normalize(entry.Action)), ("@target", entry.Target), ("@details", entry.Details),
                    ("@motif", entry.Motif ?? string.Empty), ("@entityId", entry.EntityId ?? string.Empty));
                await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }
            await using var imported = Command(connection, transaction,
                "INSERT INTO app_metadata(key,value) VALUES('legacy_audit_imported',@value)",
                ("@value", DateTime.UtcNow.ToString("O")));
            await imported.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            await transaction.CommitAsync(token).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        if (File.Exists(LegacyAuditPath))
        {
            var archive = LegacyAuditPath + $".migrated-{DateTime.UtcNow:yyyyMMddHHmmss}.bak";
            try { File.Move(LegacyAuditPath, archive, false); }
            catch (IOException exception) { logger.LogWarning("Legacy audit was imported but could not be archived ({ErrorType}).", exception.GetType().Name); }
        }
    }

    private static async Task ReconcileLegacyProductStateAsync(SqliteConnection connection, CancellationToken token)
    {
        await using (var marker = Command(connection, null,
            "SELECT value FROM app_metadata WHERE key='legacy_product_state_reconciled' LIMIT 1"))
            if (await marker.ExecuteScalarAsync(token).ConfigureAwait(false) is not null) return;

        var latest = new Dictionary<int, (string Action, string Target, string Details)>();
        await using (var select = Command(connection, null, """
            SELECT action,target,details,entity_id FROM audit_events
            WHERE entity_type=@entity AND entity_id<>'' ORDER BY timestamp_utc DESC
            """, ("@entity", AuditEntities.Product)))
        await using (var reader = await select.ExecuteReaderAsync(token).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                if (!int.TryParse(reader.GetString(3), out var id) || id <= 0 || latest.ContainsKey(id)) continue;
                latest[id] = (AuditActions.Normalize(reader.GetString(0)), reader.GetString(1), reader.GetString(2));
            }
        }

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, token).ConfigureAwait(false);
        try
        {
            foreach (var (id, entry) in latest)
            {
                if (entry.Action == AuditActions.Delete)
                {
                    await using var delete = Command(connection, transaction, "DELETE FROM products WHERE id=@id", ("@id", id));
                    await delete.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    continue;
                }
                if (entry.Action is not (AuditActions.Create or AuditActions.Edit)) continue;
                var name = TargetName(entry.Target);
                var category = DetailValue(entry.Details, "Categorie");
                var subcategory = DetailValue(entry.Details, "Subcategorie");
                var description = DetailValue(entry.Details, "Descriere");
                var quantityText = DetailValue(entry.Details, "Cantitate");
                await using var current = Command(connection, transaction, """
                    SELECT p.description,p.quantity,c.name,s.name FROM products p
                    INNER JOIN categories c ON c.id=p.category_id INNER JOIN subcategories s ON s.id=p.subcategory_id
                    WHERE p.id=@id
                    """, ("@id", id));
                await using var currentReader = await current.ExecuteReaderAsync(token).ConfigureAwait(false);
                if (!await currentReader.ReadAsync(token).ConfigureAwait(false)) continue;
                var currentDescription = currentReader.GetString(0);
                var currentQuantity = currentReader.GetInt32(1);
                var currentCategory = currentReader.GetString(2);
                var currentSubcategory = currentReader.GetString(3);
                await currentReader.DisposeAsync().ConfigureAwait(false);
                category = string.IsNullOrWhiteSpace(category) ? currentCategory : category;
                subcategory = string.IsNullOrWhiteSpace(subcategory) ? currentSubcategory : subcategory;
                description = string.IsNullOrWhiteSpace(description) ? currentDescription : description;
                var quantity = int.TryParse(quantityText, out var parsedQuantity) ? parsedQuantity : currentQuantity;
                var categoryId = await EnsureCategoryAsync(connection, transaction, category, token).ConfigureAwait(false);
                var subcategoryId = await EnsureSubcategoryAsync(connection, transaction, categoryId, subcategory, token).ConfigureAwait(false);
                await using var update = Command(connection, transaction, """
                    UPDATE products SET category_id=@category,subcategory_id=@subcategory,name=@name,
                        normalized_name=@normalized,description=@description,quantity=@quantity,version=version+1
                    WHERE id=@id
                    """, ("@category", categoryId), ("@subcategory", subcategoryId), ("@name", name),
                    ("@normalized", TextNormalization.UniquenessKey(name)), ("@description", description),
                    ("@quantity", quantity), ("@id", id));
                await update.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }
            await using var marker = Command(connection, transaction,
                "INSERT INTO app_metadata(key,value) VALUES('legacy_product_state_reconciled',@value)",
                ("@value", DateTime.UtcNow.ToString("O")));
            await marker.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            await transaction.CommitAsync(token).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    // One-time migration: diacritics are stripped from text before every save ("Eliminarea diacriticelor la
    // salvare"), but two sources predate or bypass that rule - the SQLite seed data (SeedIfNeededAsync, inserted
    // directly from DemoProductRepository.InitialProducts) and any row a database already held before the rule
    // existed. Runs once (marker in app_metadata) and rewrites only live display text; normalized_* columns are
    // untouched (TextNormalization.UniquenessKey already ignores diacritics, so they are still correct), and
    // audit_events/stock_movement_history/archive_* are left as the historical record they are.
    private static async Task NormalizeExistingDiacriticsAsync(SqliteConnection connection, CancellationToken token)
    {
        await using (var marker = Command(connection, null,
            "SELECT value FROM app_metadata WHERE key='diacritics_normalized' LIMIT 1"))
            if (await marker.ExecuteScalarAsync(token).ConfigureAwait(false) is not null) return;

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, token).ConfigureAwait(false);
        try
        {
            await NormalizeColumnsAsync(connection, transaction, "products", "id",
                [("name", TextNormalization.ForObjectNameOrCode), ("description", TextNormalization.ForStorage)], token).ConfigureAwait(false);
            await NormalizeColumnsAsync(connection, transaction, "categories", "id",
                [("name", TextNormalization.ForObjectNameOrCode)], token).ConfigureAwait(false);
            await NormalizeColumnsAsync(connection, transaction, "subcategories", "id",
                [("name", TextNormalization.ForObjectNameOrCode)], token).ConfigureAwait(false);
            await NormalizeColumnsAsync(connection, transaction, "beneficiaries", "id",
                [("name", TextNormalization.ForObjectNameOrCode), ("cui", TextNormalization.ForObjectNameOrCode)], token).ConfigureAwait(false);
            await NormalizeColumnsAsync(connection, transaction, "web_users", "id",
                [("display_name", TextNormalization.ForObjectNameOrCode)], token).ConfigureAwait(false);
            await NormalizeColumnsAsync(connection, transaction, "vehicles", "id",
                [("description", TextNormalization.ForObjectNameOrCode)], token).ConfigureAwait(false);
            await NormalizeColumnsAsync(connection, transaction, "projects", "id",
                [("name", TextNormalization.ForObjectNameOrCode), ("observations", TextNormalization.ForStorage)], token).ConfigureAwait(false);
            await NormalizeColumnsAsync(connection, transaction, "project_observations", "id",
                [("name", TextNormalization.ForObjectNameOrCode), ("content", TextNormalization.ForStorage)], token).ConfigureAwait(false);
            await NormalizeColumnsAsync(connection, transaction, "stock_movements", "id",
                [("description", TextNormalization.ForStorage)], token).ConfigureAwait(false);

            await using var mark = Command(connection, transaction,
                "INSERT INTO app_metadata(key,value) VALUES('diacritics_normalized',@value)", ("@value", DateTime.UtcNow.ToString("O")));
            await mark.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            await transaction.CommitAsync(token).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    // Rewrites only the rows whose value actually changes once normalized; a diacritics-only hygiene pass is not
    // a user edit, so it does not touch any row's version/updated_utc.
    private static async Task NormalizeColumnsAsync(SqliteConnection connection, SqliteTransaction transaction,
        string table, string idColumn, (string Column, Func<string?, string> Normalize)[] columns, CancellationToken token)
    {
        var columnList = string.Join(",", columns.Select(c => c.Column));
        var rows = new List<(long Id, string?[] Values)>();
        await using (var select = Command(connection, transaction, $"SELECT {idColumn},{columnList} FROM {table}"))
        await using (var reader = await select.ExecuteReaderAsync(token).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                var values = new string?[columns.Length];
                for (var i = 0; i < columns.Length; i++) values[i] = reader.IsDBNull(i + 1) ? null : reader.GetString(i + 1);
                rows.Add((reader.GetInt64(0), values));
            }
        }
        foreach (var (id, values) in rows)
        {
            var setClauses = new List<string>();
            var parameters = new List<(string, object?)> { ("@id", id) };
            for (var i = 0; i < columns.Length; i++)
            {
                var normalized = columns[i].Normalize(values[i]);
                if (normalized == values[i]) continue;
                var paramName = $"@p{i}";
                setClauses.Add($"{columns[i].Column}={paramName}");
                parameters.Add((paramName, normalized));
            }
            if (setClauses.Count == 0) continue;
            await using var update = Command(connection, transaction,
                $"UPDATE {table} SET {string.Join(",", setClauses)} WHERE {idColumn}=@id", parameters.ToArray());
            await update.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }
    }

    private static string TargetName(string target)
    {
        var separator = target.IndexOf('·');
        return TextNormalization.ForStorage(separator >= 0 ? target[(separator + 1)..] : target);
    }

    private static string DetailValue(string details, string field)
    {
        var part = details.Split(';', StringSplitOptions.TrimEntries)
            .LastOrDefault(item => item.StartsWith(field + ":", StringComparison.OrdinalIgnoreCase));
        if (part is null) return string.Empty;
        var value = part[(part.IndexOf(':') + 1)..].Trim();
        var arrow = value.LastIndexOf('→');
        return TextNormalization.ForStorage(arrow >= 0 ? value[(arrow + 1)..] : value);
    }

    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private const string SchemaSql = """
        PRAGMA journal_mode=WAL;
        CREATE TABLE IF NOT EXISTS app_metadata (
            key TEXT PRIMARY KEY,
            value TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS categories (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            name TEXT NOT NULL,
            normalized_name TEXT NOT NULL UNIQUE
        );
        CREATE TABLE IF NOT EXISTS subcategories (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            category_id INTEGER NOT NULL REFERENCES categories(id) ON DELETE RESTRICT,
            name TEXT NOT NULL,
            normalized_name TEXT NOT NULL UNIQUE
        );
        CREATE TABLE IF NOT EXISTS products (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            category_id INTEGER NOT NULL REFERENCES categories(id) ON DELETE RESTRICT,
            subcategory_id INTEGER NOT NULL REFERENCES subcategories(id) ON DELETE RESTRICT,
            name TEXT NOT NULL,
            normalized_name TEXT NOT NULL UNIQUE,
            description TEXT NOT NULL DEFAULT '',
            quantity INTEGER NOT NULL DEFAULT 0,
            version INTEGER NOT NULL DEFAULT 0
        );
        CREATE TABLE IF NOT EXISTS beneficiaries (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            name TEXT NOT NULL,
            normalized_name TEXT NOT NULL UNIQUE,
            cui TEXT NOT NULL,
            normalized_cui TEXT NOT NULL UNIQUE,
            kind TEXT NOT NULL DEFAULT 'PJ',
            address TEXT NOT NULL DEFAULT '',
            phone TEXT NOT NULL DEFAULT '',
            registry_number TEXT NOT NULL DEFAULT '',
            postal_code TEXT NOT NULL DEFAULT '',
            caen_code TEXT NOT NULL DEFAULT '',
            anaf_verified INTEGER NOT NULL DEFAULT 0,
            version INTEGER NOT NULL DEFAULT 0
        );
        CREATE TABLE IF NOT EXISTS beneficiary_work_points (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            beneficiary_id INTEGER NOT NULL REFERENCES beneficiaries(id) ON DELETE RESTRICT,
            name TEXT NOT NULL,
            address TEXT NOT NULL,
            normalized_address TEXT NOT NULL,
            phone TEXT NOT NULL DEFAULT '',
            contact_person TEXT NOT NULL DEFAULT '',
            version INTEGER NOT NULL DEFAULT 0,
            UNIQUE(beneficiary_id, normalized_address)
        );
        CREATE TABLE IF NOT EXISTS vehicles (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            plate_number TEXT NOT NULL,
            normalized_plate TEXT NOT NULL UNIQUE,
            description TEXT NOT NULL,
            version INTEGER NOT NULL DEFAULT 0
        );
        CREATE TABLE IF NOT EXISTS web_users (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            username TEXT NOT NULL,
            normalized_username TEXT NOT NULL UNIQUE,
            display_name TEXT NOT NULL,
            password_hash TEXT NOT NULL,
            role TEXT NOT NULL CHECK(role IN ('Administrator','Utilizator')),
            is_active INTEGER NOT NULL DEFAULT 1,
            version INTEGER NOT NULL DEFAULT 0
        );
        CREATE TABLE IF NOT EXISTS product_locks (
            product_id INTEGER PRIMARY KEY REFERENCES products(id) ON DELETE CASCADE,
            owner_username TEXT NOT NULL,
            session_id TEXT NOT NULL,
            acquired_utc TEXT NOT NULL,
            renewed_utc TEXT NOT NULL,
            expires_utc TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS product_images (
            product_id INTEGER PRIMARY KEY REFERENCES products(id) ON DELETE CASCADE,
            relative_path TEXT NOT NULL,
            content_type TEXT NOT NULL,
            file_name TEXT NOT NULL,
            byte_length INTEGER NOT NULL,
            updated_utc TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS stock_movements (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            product_id INTEGER NOT NULL REFERENCES products(id) ON DELETE RESTRICT,
            beneficiary_id INTEGER REFERENCES beneficiaries(id) ON DELETE RESTRICT,
            project_id INTEGER REFERENCES projects(id) ON DELETE RESTRICT,
            quantity INTEGER NOT NULL,
            created_utc TEXT NOT NULL,
            kind INTEGER NOT NULL DEFAULT 1,
            movement_date TEXT NOT NULL DEFAULT '',
            description TEXT NOT NULL DEFAULT '',
            operator TEXT NOT NULL DEFAULT '',
            version INTEGER NOT NULL DEFAULT 0,
            updated_utc TEXT NOT NULL DEFAULT ''
        );
        CREATE TABLE IF NOT EXISTS stock_movement_history (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            movement_id INTEGER NOT NULL REFERENCES stock_movements(id) ON DELETE RESTRICT,
            actor TEXT NOT NULL,
            timestamp_utc TEXT NOT NULL,
            changes TEXT NOT NULL,
            stock_correction INTEGER NOT NULL,
            reason TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS projects (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            beneficiary_id INTEGER NOT NULL REFERENCES beneficiaries(id) ON DELETE RESTRICT,
            name TEXT NOT NULL,
            normalized_name TEXT NOT NULL,
            observations TEXT NOT NULL DEFAULT '',
            version INTEGER NOT NULL DEFAULT 0,
            created_utc TEXT NOT NULL,
            updated_utc TEXT NOT NULL,
            UNIQUE(beneficiary_id, normalized_name)
        );
        CREATE TABLE IF NOT EXISTS project_observations (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            project_id INTEGER NOT NULL REFERENCES projects(id) ON DELETE RESTRICT,
            name TEXT NOT NULL,
            content TEXT NOT NULL DEFAULT '',
            author TEXT NOT NULL,
            version INTEGER NOT NULL DEFAULT 0,
            created_utc TEXT NOT NULL,
            updated_utc TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS project_observation_files (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            observation_id INTEGER NOT NULL REFERENCES project_observations(id) ON DELETE RESTRICT,
            relative_path TEXT NOT NULL,
            original_name TEXT NOT NULL,
            content_type TEXT NOT NULL,
            byte_length INTEGER NOT NULL,
            sha256 TEXT NOT NULL,
            author TEXT NOT NULL,
            uploaded_utc TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS audit_events (
            id TEXT PRIMARY KEY,
            timestamp_utc TEXT NOT NULL,
            actor_username TEXT NOT NULL,
            actor_role TEXT NOT NULL,
            entity_type TEXT NOT NULL,
            action TEXT NOT NULL,
            target TEXT NOT NULL,
            details TEXT NOT NULL,
            motif TEXT NOT NULL DEFAULT '',
            entity_id TEXT NOT NULL DEFAULT '',
            archive_operation_id TEXT NULL
        );
        CREATE TABLE IF NOT EXISTS archive_operations (
            id TEXT PRIMARY KEY,
            entity_type TEXT NOT NULL,
            original_id TEXT NOT NULL,
            original_version INTEGER NOT NULL CHECK(original_version >= 0),
            deleted_utc TEXT NOT NULL,
            actor_username TEXT NOT NULL,
            actor_role TEXT NOT NULL,
            motif TEXT NOT NULL,
            target TEXT NOT NULL,
            details TEXT NOT NULL,
            data_json TEXT NOT NULL CHECK(json_valid(data_json)),
            protected_data_json TEXT NULL CHECK(protected_data_json IS NULL OR json_valid(protected_data_json))
        );
        CREATE TABLE IF NOT EXISTS archive_products (
            archive_id TEXT PRIMARY KEY REFERENCES archive_operations(id) ON DELETE RESTRICT,
            original_id INTEGER NOT NULL,
            category TEXT NOT NULL,
            subcategory TEXT NOT NULL,
            name TEXT NOT NULL,
            description TEXT NOT NULL,
            quantity INTEGER NOT NULL,
            version INTEGER NOT NULL
        );
        CREATE TABLE IF NOT EXISTS archive_beneficiaries (
            archive_id TEXT PRIMARY KEY REFERENCES archive_operations(id) ON DELETE RESTRICT,
            original_id INTEGER NOT NULL,
            name TEXT NOT NULL,
            cui TEXT NOT NULL,
            version INTEGER NOT NULL
        );
        CREATE TABLE IF NOT EXISTS archive_vehicles (
            archive_id TEXT PRIMARY KEY REFERENCES archive_operations(id) ON DELETE RESTRICT,
            original_id INTEGER NOT NULL,
            plate_number TEXT NOT NULL,
            description TEXT NOT NULL,
            version INTEGER NOT NULL
        );
        CREATE TABLE IF NOT EXISTS archive_web_users (
            archive_id TEXT PRIMARY KEY REFERENCES archive_operations(id) ON DELETE RESTRICT,
            original_id INTEGER NOT NULL,
            username TEXT NOT NULL,
            display_name TEXT NOT NULL,
            role TEXT NOT NULL,
            is_active INTEGER NOT NULL,
            version INTEGER NOT NULL,
            password_hash TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS archive_projects (
            archive_id TEXT PRIMARY KEY REFERENCES archive_operations(id) ON DELETE RESTRICT,
            original_id INTEGER NOT NULL,
            beneficiary_id INTEGER NOT NULL,
            name TEXT NOT NULL,
            observations TEXT NOT NULL,
            version INTEGER NOT NULL
        );
        CREATE TABLE IF NOT EXISTS archive_project_observations (
            archive_id TEXT PRIMARY KEY REFERENCES archive_operations(id) ON DELETE RESTRICT,
            original_id INTEGER NOT NULL,
            project_id INTEGER NOT NULL,
            name TEXT NOT NULL,
            content TEXT NOT NULL,
            author TEXT NOT NULL,
            version INTEGER NOT NULL
        );
        CREATE TABLE IF NOT EXISTS archive_project_observation_files (
            archive_id TEXT PRIMARY KEY REFERENCES archive_operations(id) ON DELETE RESTRICT,
            original_id INTEGER NOT NULL,
            observation_id INTEGER NOT NULL,
            original_name TEXT NOT NULL,
            content_type TEXT NOT NULL,
            byte_length INTEGER NOT NULL,
            sha256 TEXT NOT NULL,
            author TEXT NOT NULL,
            uploaded_utc TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS archive_stock_movements (
            archive_id TEXT PRIMARY KEY REFERENCES archive_operations(id) ON DELETE RESTRICT,
            original_id INTEGER NOT NULL,
            product_id INTEGER NOT NULL,
            kind INTEGER NOT NULL,
            quantity INTEGER NOT NULL,
            movement_date TEXT NOT NULL,
            description TEXT NOT NULL,
            beneficiary_id INTEGER NULL,
            project_id INTEGER NULL,
            operator TEXT NOT NULL,
            version INTEGER NOT NULL
        );
        CREATE TABLE IF NOT EXISTS archive_relations (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            archive_id TEXT NOT NULL REFERENCES archive_operations(id) ON DELETE RESTRICT,
            relation_type TEXT NOT NULL,
            original_relation_id TEXT NOT NULL,
            data_json TEXT NOT NULL CHECK(json_valid(data_json)),
            UNIQUE(archive_id,relation_type,original_relation_id)
        );
        CREATE TABLE IF NOT EXISTS archive_files (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            archive_id TEXT NOT NULL REFERENCES archive_operations(id) ON DELETE RESTRICT,
            relation_type TEXT NOT NULL,
            original_relation_id TEXT NOT NULL,
            live_relative_path TEXT NOT NULL,
            archive_relative_path TEXT NOT NULL,
            content_type TEXT NOT NULL,
            file_name TEXT NOT NULL,
            byte_length INTEGER NOT NULL CHECK(byte_length >= 0),
            content_hash TEXT NOT NULL,
            archived_utc TEXT NOT NULL,
            UNIQUE(archive_id,relation_type,original_relation_id)
        );
        CREATE INDEX IF NOT EXISTS ix_audit_events_timestamp ON audit_events(timestamp_utc DESC);
        CREATE INDEX IF NOT EXISTS ix_products_group ON products(category_id,subcategory_id);
        CREATE INDEX IF NOT EXISTS ix_stock_movements_beneficiary ON stock_movements(beneficiary_id);
        CREATE INDEX IF NOT EXISTS ix_stock_movements_project ON stock_movements(project_id);
        CREATE INDEX IF NOT EXISTS ix_stock_movement_history_movement ON stock_movement_history(movement_id,id);
        CREATE INDEX IF NOT EXISTS ix_archive_stock_movements_original ON archive_stock_movements(original_id);
        CREATE INDEX IF NOT EXISTS ix_projects_beneficiary ON projects(beneficiary_id);
        CREATE INDEX IF NOT EXISTS ix_project_observations_project ON project_observations(project_id,created_utc DESC,id);
        CREATE INDEX IF NOT EXISTS ix_project_observation_files_observation ON project_observation_files(observation_id);
        CREATE INDEX IF NOT EXISTS ix_archive_operations_object ON archive_operations(entity_type,original_id);
        CREATE INDEX IF NOT EXISTS ix_archive_operations_deleted ON archive_operations(deleted_utc DESC);
        CREATE INDEX IF NOT EXISTS ix_archive_operations_actor ON archive_operations(actor_username,deleted_utc DESC);
        CREATE INDEX IF NOT EXISTS ix_archive_products_original ON archive_products(original_id);
        CREATE INDEX IF NOT EXISTS ix_archive_beneficiaries_original ON archive_beneficiaries(original_id);
        CREATE INDEX IF NOT EXISTS ix_archive_web_users_original ON archive_web_users(original_id);
        CREATE INDEX IF NOT EXISTS ix_archive_vehicles_original ON archive_vehicles(original_id);
        CREATE INDEX IF NOT EXISTS ix_archive_projects_original ON archive_projects(original_id);
        CREATE INDEX IF NOT EXISTS ix_archive_project_observations_original ON archive_project_observations(original_id);
        CREATE INDEX IF NOT EXISTS ix_archive_project_observation_files_original ON archive_project_observation_files(original_id);
        CREATE INDEX IF NOT EXISTS ix_archive_relations_object ON archive_relations(relation_type,original_relation_id);
        CREATE INDEX IF NOT EXISTS ix_archive_files_object ON archive_files(relation_type,original_relation_id);
        """;
}

internal static class SqliteRepositoryAudit
{
    public static async Task<(string Username, string Role)> ActorAsync(IAccessControl? access,
        CancellationToken cancellationToken)
    {
        if (access is null) return ("sistem", AccessRoles.LimitedUser);
        var username = await access.GetUsernameAsync(cancellationToken).ConfigureAwait(false) ?? "necunoscut";
        var role = await access.IsAdministratorAsync(cancellationToken).ConfigureAwait(false)
            ? AccessRoles.Administrator : AccessRoles.LimitedUser;
        return (username, role);
    }
}
