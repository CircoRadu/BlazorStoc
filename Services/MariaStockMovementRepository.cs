using System.Data;
using System.Globalization;
using System.Text.Json;
using MySqlConnector;

namespace BlazorStoc.Services;

// MariaDB mode: maps the movements onto the legacy `io` / `io_history` tables (io_tip_actiune: 1 = entry, 0 = exit,
// id_beneficiar = 0 means none, io_data = dd-MM-yyyy) and adds the columns this module needs at first use.
// Not exercised against a real server in the automated checks.
public sealed class MariaStockMovementRepository(IConfiguration configuration, IAccessControl? accessControl = null,
    IAuditTrail? auditTrail = null, IArchiveService? archiveService = null) : IStockMovementRepository
{
    private const string ProductMissingMessage = "Produsul nu mai există. Actualizează catalogul.";
    private static readonly SemaphoreSlim SchemaGate = new(1, 1);
    private static bool schemaReady;
    private readonly IArchiveService archiver = archiveService ?? new ArchiveService(accessControl);
    private MySqlConnection CreateConnection() => DatabaseConnections.Create(configuration);

    private const string SelectMovement = """
        SELECT io.id_io,io.id_produs,io.io_tip_actiune,io.io_numar_bucati,io.io_data,io.io_descriere,
               io.id_beneficiar,b.beneficiar_denumire,io.id_project,pr.name,COALESCE(u.username,''),io.io_versiune,
               io.io_created_utc,io.io_updated_utc,
               EXISTS(SELECT 1 FROM io_history h WHERE h.id_io=io.id_io),
               io.io_destinatie,io.id_vehicul,dv.vehicul_numar,io.id_vehicul_sursa,sv.vehicul_numar
        FROM io
        LEFT JOIN beneficiar b ON b.id_beneficiar=io.id_beneficiar AND io.id_beneficiar<>0
        LEFT JOIN project pr ON pr.id_project=io.id_project
        LEFT JOIN `user` u ON u.id_user=io.id_user
        LEFT JOIN vehicul dv ON dv.id_vehicul=io.id_vehicul
        LEFT JOIN vehicul sv ON sv.id_vehicul=io.id_vehicul_sursa
        """;

    public async Task<StockMovementPage> GetPageAsync(int productId, StockMovementQuery query, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        int stock;
        await using (var stockCommand = Command(connection, null,
            "SELECT COALESCE(produs_cantitate,0) FROM produs WHERE id_produs=@id", ("@id", productId)))
            stock = await stockCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is { } value and not DBNull
                ? Convert.ToInt32(value)
                : throw new StockMovementOperationException(ProductMissingMessage);
        object kind = query.Kind is null ? DBNull.Value : (int)query.Kind.Value;
        int total;
        await using (var count = Command(connection, null,
            "SELECT COUNT(*) FROM io WHERE id_produs=@product AND (@kind IS NULL OR io_tip_actiune=@kind)",
            ("@product", productId), ("@kind", kind)))
            total = Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        bool anyModified;
        await using (var modified = Command(connection, null, """
            SELECT EXISTS(SELECT 1 FROM io_history h INNER JOIN io ON io.id_io=h.id_io WHERE io.id_produs=@product)
            """, ("@product", productId)))
            anyModified = Convert.ToBoolean(await modified.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        var direction = query.Descending ? "DESC" : "ASC";
        var limit = query.PageSize <= 0 ? long.MaxValue : query.PageSize;
        var offset = query.PageSize <= 0 ? 0 : (long)(Math.Max(1, query.Page) - 1) * query.PageSize;
        await using var command = Command(connection, null, $"""
            {SelectMovement}
            WHERE io.id_produs=@product AND (@kind IS NULL OR io.io_tip_actiune=@kind)
            ORDER BY STR_TO_DATE(io.io_data,'%d-%m-%Y') {direction}, io.id_io {direction}
            LIMIT @limit OFFSET @offset
            """, ("@product", productId), ("@kind", kind), ("@limit", limit), ("@offset", offset));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var items = new List<StockMovement>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) items.Add(ReadMovement(reader));
        var inVehicles = (await VehicleQuantitiesAsync(connection, null, productId, cancellationToken).ConfigureAwait(false))
            .Sum(entry => Math.Max(0, entry.Quantity));
        return new StockMovementPage(items, total, stock, anyModified, inVehicles);
    }

    public async Task<IReadOnlyList<VehicleStock>> GetVehicleStocksAsync(int productId, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var held = (await VehicleQuantitiesAsync(connection, null, productId, cancellationToken).ConfigureAwait(false))
            .Where(entry => entry.Quantity > 0).ToDictionary(entry => entry.VehicleId, entry => entry.Quantity);
        if (held.Count == 0) return [];
        var result = new List<VehicleStock>();
        await using var command = Command(connection, null,
            "SELECT id_vehicul,vehicul_numar,vehicul_descriere FROM vehicul ORDER BY vehicul_numar,id_vehicul");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            if (held.TryGetValue(reader.GetInt32(0), out var quantity))
                result.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), quantity));
        return result;
    }

    public async Task<IReadOnlyDictionary<int, int>> GetQuantitiesInVehiclesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        return (await VehicleQuantitiesAsync(connection, null, null, cancellationToken).ConfigureAwait(false))
            .Where(entry => entry.Quantity > 0).GroupBy(entry => entry.ProductId)
            .ToDictionary(group => group.Key, group => group.Sum(entry => entry.Quantity));
    }

    public async Task<IReadOnlyDictionary<int, int>> GetMovementCountsByVehicleAsync(CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, null, """
            SELECT vid,COUNT(*) FROM (
                SELECT id_vehicul AS vid FROM io WHERE id_vehicul IS NOT NULL
                UNION ALL SELECT id_vehicul_sursa FROM io WHERE id_vehicul_sursa IS NOT NULL) t
            GROUP BY vid
            """);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var counts = new Dictionary<int, int>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) counts[reader.GetInt32(0)] = Convert.ToInt32(reader.GetValue(1));
        return counts;
    }

    public async Task<StockMovement?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        return await GetMovementAsync(connection, null, id, false, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<StockMovementHistoryEntry>> GetHistoryAsync(int movementId, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        return await GetHistoryAsync(connection, null, movementId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ProjectStockMovement>> GetForProjectAsync(int projectId, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, null, """
            SELECT io.id_io,io.id_produs,p.produs_denumire,io.io_numar_bucati,io.io_data,COALESCE(u.username,'')
            FROM io INNER JOIN produs p ON p.id_produs=io.id_produs LEFT JOIN `user` u ON u.id_user=io.id_user
            WHERE io.id_project=@project AND io.io_tip_actiune=0
            ORDER BY STR_TO_DATE(io.io_data,'%d-%m-%Y') DESC,io.id_io DESC
            """, ("@project", projectId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<ProjectStockMovement>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new(reader.GetInt32(0), reader.GetInt32(1), Text(reader, 2), reader.GetInt32(3), ParseDate(reader.GetString(4)), reader.GetString(5)));
        return result;
    }

    public async Task<StockMovementResult> CreateAsync(int productId, StockMovementInput input, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        var value = StockMovementRules.Validated(input, input.Kind, false);
        var now = DateTime.UtcNow;
        var (movement, stock, productCode) = await WriteAsync(async (connection, transaction, userId) =>
        {
            var productCode = await LockProductAsync(connection, transaction, productId, cancellationToken).ConfigureAwait(false);
            var (beneficiaryName, projectName) = await ResolveRelationsAsync(connection, transaction, value, cancellationToken).ConfigureAwait(false);
            var (vehiclePlate, sourcePlate) = await ResolveVehiclesAsync(connection, transaction, value, cancellationToken).ConfigureAwait(false);
            if (value.SourceVehicleId is { } sourceVehicleId)
            {
                var held = (await VehicleQuantitiesAsync(connection, transaction, productId, cancellationToken).ConfigureAwait(false))
                    .Where(entry => entry.VehicleId == sourceVehicleId).Sum(entry => entry.Quantity);
                if (value.Quantity!.Value > held)
                    throw new StockMovementOperationException(StockMovementRules.NotEnoughInVehicleMessage(sourcePlate!, held));
            }
            await using var insert = Command(connection, transaction, """
                INSERT INTO io(id_user,id_produs,id_beneficiar,id_project,io_tip_actiune,io_numar_bucati,io_descriere,io_data,
                               io_versiune,io_created_utc,io_updated_utc,io_destinatie,id_vehicul,id_vehicul_sursa)
                VALUES(@user,@product,@beneficiary,@project,@kind,@quantity,@description,@date,0,@created,@created,
                       @destination,@vehicle,@sourceVehicle)
                """, ("@user", userId), ("@product", productId), ("@beneficiary", value.BeneficiaryId ?? 0),
                ("@project", value.ProjectId), ("@kind", (int)value.Kind), ("@quantity", value.Quantity),
                ("@description", value.Description), ("@date", StockMovementRules.LegacyDate(value.Date!.Value)), ("@created", now),
                ("@destination", (int?)value.Destination), ("@vehicle", value.VehicleId), ("@sourceVehicle", value.SourceVehicleId));
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            var id = checked((int)insert.LastInsertedId);
            var movement = new StockMovement(id, productId, value.Kind, value.Quantity!.Value, value.Date.Value, value.Description,
                value.BeneficiaryId, beneficiaryName, value.ProjectId, projectName, await UsernameAsync(connection, transaction, userId, cancellationToken).ConfigureAwait(false),
                0, now, now, false, value.Destination, value.VehicleId, vehiclePlate, value.SourceVehicleId, sourcePlate);
            await EnsureVehicleStocksNotNegativeAsync(connection, transaction, productId, cancellationToken).ConfigureAwait(false);
            var stock = await ApplyStockAsync(connection, transaction, productId, movement.Effect, cancellationToken).ConfigureAwait(false);
            await LogAsync(connection, transaction, userId, "create", null, movement, string.Empty, cancellationToken).ConfigureAwait(false);
            return (movement, stock, productCode);
        }, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordCreateAsync(auditTrail, accessControl, AuditEntities.StockMovement, movement.Id.ToString(),
            StockMovementRules.Target(productCode), StockMovementRules.AuditIdentification(movement, productCode), cancellationToken).ConfigureAwait(false);
        return new StockMovementResult(movement, stock);
    }

    public async Task<StockMovementResult> UpdateAsync(StockMovement original, StockMovementInput input, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        var value = StockMovementRules.Validated(input, original.Kind, true);
        var now = DateTime.UtcNow;
        var (current, updated, stock, productCode) = await WriteAsync(async (connection, transaction, userId) =>
        {
            var current = await GetMovementAsync(connection, transaction, original.Id, true, cancellationToken).ConfigureAwait(false);
            StockMovementRules.CheckCurrent(current, original);
            var productCode = await LockProductAsync(connection, transaction, current!.ProductId, cancellationToken).ConfigureAwait(false);
            var (beneficiaryName, projectName) = await ResolveRelationsAsync(connection, transaction, value, cancellationToken).ConfigureAwait(false);
            var (vehiclePlate, sourcePlate) = await ResolveVehiclesAsync(connection, transaction, value, cancellationToken).ConfigureAwait(false);
            var updated = current with
            {
                Quantity = value.Quantity!.Value, Date = value.Date!.Value, Description = value.Description,
                BeneficiaryId = value.BeneficiaryId, BeneficiaryName = beneficiaryName, ProjectId = value.ProjectId,
                ProjectName = projectName, Version = current.Version + 1, UpdatedUtc = now, Modified = true,
                Destination = value.Destination, VehicleId = value.VehicleId, VehiclePlate = vehiclePlate,
                SourceVehicleId = value.SourceVehicleId, SourceVehiclePlate = sourcePlate
            };
            if (!StockMovementRules.HasChanges(current, updated))
                throw new StockMovementOperationException("Nu ai modificat nicio valoare a mișcării.");
            var correction = updated.Effect - current.Effect;
            await using (var update = Command(connection, transaction, """
                UPDATE io SET io_numar_bucati=@quantity,io_data=@date,io_descriere=@description,id_beneficiar=@beneficiary,
                    id_project=@project,io_versiune=@version,io_updated_utc=@updated,
                    io_destinatie=@destination,id_vehicul=@vehicle,id_vehicul_sursa=@sourceVehicle
                WHERE id_io=@id AND io_versiune=@oldVersion
                """, ("@quantity", updated.Quantity), ("@date", StockMovementRules.LegacyDate(updated.Date)),
                ("@description", updated.Description), ("@beneficiary", updated.BeneficiaryId ?? 0), ("@project", updated.ProjectId),
                ("@version", updated.Version), ("@updated", now), ("@id", current.Id), ("@oldVersion", current.Version),
                ("@destination", (int?)updated.Destination), ("@vehicle", updated.VehicleId), ("@sourceVehicle", updated.SourceVehicleId)))
                if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                    throw new StockMovementOperationException(StockMovementRules.StaleMessage);
            await using (var history = Command(connection, transaction, """
                INSERT INTO io_history(id_user,id_io,denumire_produs,modificare,data,motiv,timestamp)
                VALUES(@user,@movement,@product,@changes,@date,@reason,@timestamp)
                """, ("@user", userId), ("@movement", current.Id), ("@product", productCode),
                ("@changes", StockMovementRules.HistorySummary(current, updated, correction)),
                ("@date", StockMovementRules.LegacyDate(DateOnly.FromDateTime(now))), ("@reason", value.Reason),
                ("@timestamp", now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))))
                await history.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await EnsureVehicleStocksNotNegativeAsync(connection, transaction, current.ProductId, cancellationToken).ConfigureAwait(false);
            var stock = await ApplyStockAsync(connection, transaction, current.ProductId, correction, cancellationToken).ConfigureAwait(false);
            await LogAsync(connection, transaction, userId, "update", current, updated, value.Reason, cancellationToken).ConfigureAwait(false);
            return (current, updated, stock, productCode);
        }, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordEditAsync(auditTrail, accessControl, AuditEntities.StockMovement, updated.Id.ToString(),
            StockMovementRules.Target(productCode), StockMovementRules.AuditChanges(current, updated), value.Reason, cancellationToken).ConfigureAwait(false);
        return new StockMovementResult(updated, stock);
    }

    public async Task<int> DeleteAsync(StockMovement original, string reason, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        var motif = ChangeReasonRules.Normalize(reason);
        if (ChangeReasonRules.ValidationError(motif) is { } reasonError) throw new StockMovementOperationException(reasonError);
        string productCode;
        IReadOnlyList<StockMovementHistoryEntry> history;
        await using (var lookup = await OpenAsync(cancellationToken).ConfigureAwait(false))
        {
            await using var codeCommand = Command(lookup, null, "SELECT produs_denumire FROM produs WHERE id_produs=@id", ("@id", original.ProductId));
            productCode = await codeCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string
                          ?? throw new StockMovementOperationException(ProductMissingMessage);
            history = await GetHistoryAsync(lookup, null, original.Id, cancellationToken).ConfigureAwait(false);
        }
        var stock = 0;
        await archiver.ExecuteAsync(ArchiveRequests.StockMovement(original, productCode, history, motif), async (operation, token) =>
        {
            stock = await WriteAsync(async (connection, transaction, userId) =>
            {
                var current = await GetMovementAsync(connection, transaction, original.Id, true, token).ConfigureAwait(false);
                StockMovementRules.CheckCurrent(current, original);
                await LockProductAsync(connection, transaction, current!.ProductId, token).ConfigureAwait(false);
                await MariaArchivePersistence.InsertAsync(connection, transaction, operation, [], token).ConfigureAwait(false);
                await using (var deleteHistory = Command(connection, transaction, "DELETE FROM io_history WHERE id_io=@id", ("@id", original.Id)))
                    await deleteHistory.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                await using (var delete = Command(connection, transaction,
                    "DELETE FROM io WHERE id_io=@id AND io_versiune=@version", ("@id", original.Id), ("@version", original.Version)))
                    if (await delete.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
                        throw new StockMovementOperationException(StockMovementRules.StaleMessage);
                await EnsureVehicleStocksNotNegativeAsync(connection, transaction, current.ProductId, token).ConfigureAwait(false);
                var newStock = await ApplyStockAsync(connection, transaction, current.ProductId, -current.Effect, token).ConfigureAwait(false);
                await LogAsync(connection, transaction, userId, "delete", current, null, motif, token).ConfigureAwait(false);
                await MariaArchivePersistence.InsertAuditAsync(connection, transaction, operation, token).ConfigureAwait(false);
                return newStock;
            }, token).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
        return stock;
    }

    // Used by the project module to block deleting a project that still has movements.
    internal static async Task<bool> ProjectHasMovementsAsync(MySqlConnection connection, MySqlTransaction? transaction, int projectId,
        CancellationToken token)
    {
        await using var columns = new MySqlCommand("""
            SELECT COUNT(*) FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='io' AND COLUMN_NAME='id_project'
            """, connection, transaction);
        if (Convert.ToInt32(await columns.ExecuteScalarAsync(token).ConfigureAwait(false)) == 0) return false;
        await using var command = new MySqlCommand("SELECT EXISTS(SELECT 1 FROM io WHERE id_project=@id)", connection, transaction);
        command.Parameters.AddWithValue("@id", projectId);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(token).ConfigureAwait(false));
    }

    // Used by the vehicle module to block deleting a vehicle that still has movements.
    internal static async Task<bool> VehicleHasMovementsAsync(MySqlConnection connection, MySqlTransaction? transaction, int vehicleId,
        CancellationToken token)
    {
        await using var columns = new MySqlCommand("""
            SELECT COUNT(*) FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='io' AND COLUMN_NAME IN ('id_vehicul','id_vehicul_sursa')
            """, connection, transaction);
        if (Convert.ToInt32(await columns.ExecuteScalarAsync(token).ConfigureAwait(false)) < 2) return false;
        await using var command = new MySqlCommand("SELECT EXISTS(SELECT 1 FROM io WHERE id_vehicul=@id OR id_vehicul_sursa=@id)", connection, transaction);
        command.Parameters.AddWithValue("@id", vehicleId);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(token).ConfigureAwait(false));
    }

    private async Task<MySqlConnection> OpenAsync(CancellationToken token)
    {
        var connection = CreateConnection();
        try
        {
            await connection.OpenAsync(token).ConfigureAwait(false);
            await EnsureSchemaAsync(connection, token).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task<T> WriteAsync<T>(Func<MySqlConnection, MySqlTransaction, int, Task<T>> action, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!string.Equals(configuration["Database:Name"] ?? "BlazorStoc", "BlazorStoc", StringComparison.Ordinal))
            throw new StockMovementOperationException("Modificările sunt permise numai în baza BlazorStoc. Verifică numele bazei configurate.");
        var userId = configuration.GetValue<int>("Database:ApplicationUserId");
        if (userId <= 0)
            throw new StockMovementOperationException("Salvarea nu este configurată. Administratorul trebuie să asocieze contul web cu un utilizator al bazei de date.");
        await using var connection = await OpenAsync(token).ConfigureAwait(false);
        await using (var strict = new MySqlCommand("SET SESSION sql_mode = CONCAT_WS(',', @@sql_mode, 'STRICT_ALL_TABLES')", connection))
            await strict.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, token).ConfigureAwait(false);
        try
        {
            await using (var actor = Command(connection, transaction, "SELECT id_user FROM `user` WHERE id_user=@id FOR UPDATE", ("@id", userId)))
                if (await actor.ExecuteScalarAsync(token).ConfigureAwait(false) is null)
                    throw new StockMovementOperationException("Utilizatorul asociat contului web nu există în baza de date. Verifică configurarea cu administratorul.");
            var result = await action(connection, transaction, userId).ConfigureAwait(false);
            await transaction.CommitAsync(token).ConfigureAwait(false);
            return result;
        }
        catch
        {
            if (transaction.Connection is not null) await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task EnsureSchemaAsync(MySqlConnection connection, CancellationToken token)
    {
        if (schemaReady) return;
        await SchemaGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (schemaReady) return;
            var columns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            await using (var command = new MySqlCommand("""
                SELECT COLUMN_NAME,DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='io'
                """, connection))
            await using (var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                while (await reader.ReadAsync(token).ConfigureAwait(false)) columns[reader.GetString(0)] = reader.GetString(1);
            // The movement queries join the project table, which the project module otherwise creates lazily.
            foreach (var projectStatement in MariaProjectRepository.SchemaStatements)
            {
                await using var create = new MySqlCommand(projectStatement, connection);
                await create.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }
            // The movement queries also join the vehicle table (created by the vehicle module otherwise).
            await using (var createVehicles = new MySqlCommand(MariaVehicleRepository.CreateTableSql, connection))
                await createVehicles.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            var statements = new List<string>();
            // Exit destination (1 beneficiary, 2 vehicle, 3 generic sale, 4 stock correction), destination vehicle and source
            // vehicle (null = the warehouse). The legacy application ignores these columns.
            if (!columns.ContainsKey("io_destinatie")) statements.Add("ALTER TABLE io ADD COLUMN io_destinatie TINYINT NULL");
            if (!columns.ContainsKey("id_vehicul")) statements.Add("ALTER TABLE io ADD COLUMN id_vehicul INT NULL, ADD INDEX ix_io_vehicul(id_vehicul)");
            if (!columns.ContainsKey("id_vehicul_sursa")) statements.Add("ALTER TABLE io ADD COLUMN id_vehicul_sursa INT NULL, ADD INDEX ix_io_vehicul_sursa(id_vehicul_sursa)");
            if (!columns.ContainsKey("id_project")) statements.Add("ALTER TABLE io ADD COLUMN id_project INT NULL, ADD INDEX ix_io_project(id_project)");
            if (!columns.ContainsKey("io_versiune")) statements.Add("ALTER TABLE io ADD COLUMN io_versiune BIGINT UNSIGNED NOT NULL DEFAULT 0");
            if (!columns.ContainsKey("io_created_utc")) statements.Add("ALTER TABLE io ADD COLUMN io_created_utc DATETIME(6) NULL");
            if (!columns.ContainsKey("io_updated_utc")) statements.Add("ALTER TABLE io ADD COLUMN io_updated_utc DATETIME(6) NULL");
            // The legacy column is a tinyint; the quantity limit of this module needs a wider type.
            if (columns.TryGetValue("io_numar_bucati", out var type) && type.Equals("tinyint", StringComparison.OrdinalIgnoreCase))
                statements.Add("ALTER TABLE io MODIFY COLUMN io_numar_bucati INT NOT NULL DEFAULT 0");
            foreach (var statement in statements)
            {
                await using var alter = new MySqlCommand(statement, connection);
                await alter.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }
            schemaReady = true;
        }
        finally { SchemaGate.Release(); }
    }

    private static async Task<string> LockProductAsync(MySqlConnection connection, MySqlTransaction transaction, int productId, CancellationToken token)
    {
        await using var command = Command(connection, transaction,
            "SELECT produs_denumire FROM produs WHERE id_produs=@id FOR UPDATE", ("@id", productId));
        return await command.ExecuteScalarAsync(token).ConfigureAwait(false) as string
               ?? throw new StockMovementOperationException(ProductMissingMessage);
    }

    // Atomic increment under the product row lock: concurrent movements cannot overwrite each other's stock change.
    private static async Task<int> ApplyStockAsync(MySqlConnection connection, MySqlTransaction transaction, int productId,
        int delta, CancellationToken token)
    {
        if (delta != 0)
            await using (var update = Command(connection, transaction,
                "UPDATE produs SET produs_cantitate=COALESCE(produs_cantitate,0)+@delta WHERE id_produs=@id",
                ("@delta", delta), ("@id", productId)))
                await update.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        await using var read = Command(connection, transaction,
            "SELECT COALESCE(produs_cantitate,0) FROM produs WHERE id_produs=@id", ("@id", productId));
        return Convert.ToInt32(await read.ExecuteScalarAsync(token).ConfigureAwait(false));
    }

    private static async Task<(string? BeneficiaryName, string? ProjectName)> ResolveRelationsAsync(MySqlConnection connection,
        MySqlTransaction transaction, StockMovementInput value, CancellationToken token)
    {
        string? beneficiaryName = null, projectName = null;
        if (value.BeneficiaryId is { } beneficiaryId)
        {
            await using var command = Command(connection, transaction,
                "SELECT beneficiar_denumire FROM beneficiar WHERE id_beneficiar=@id", ("@id", beneficiaryId));
            beneficiaryName = await command.ExecuteScalarAsync(token).ConfigureAwait(false) as string
                              ?? throw new StockMovementOperationException("Beneficiarul selectat nu mai există. Actualizează lista și reia operația.");
        }
        if (value.ProjectId is { } projectId)
        {
            await using var command = Command(connection, transaction,
                "SELECT name,id_beneficiar FROM project WHERE id_project=@id", ("@id", projectId));
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false))
                throw new StockMovementOperationException("Proiectul selectat nu mai există. Actualizează lista și reia operația.");
            if (reader.GetInt32(1) != value.BeneficiaryId)
                throw new StockMovementOperationException("Proiectul selectat nu aparține beneficiarului ales.");
            projectName = reader.GetString(0);
        }
        return (beneficiaryName, projectName);
    }

    private static async Task<(string? VehiclePlate, string? SourcePlate)> ResolveVehiclesAsync(MySqlConnection connection,
        MySqlTransaction transaction, StockMovementInput value, CancellationToken token)
    {
        async Task<string> PlateAsync(int id)
        {
            await using var command = Command(connection, transaction, "SELECT vehicul_numar FROM vehicul WHERE id_vehicul=@id", ("@id", id));
            return await command.ExecuteScalarAsync(token).ConfigureAwait(false) as string
                   ?? throw new StockMovementOperationException(StockMovementRules.VehicleMissingMessage);
        }
        return (value.VehicleId is { } vehicleId ? await PlateAsync(vehicleId).ConfigureAwait(false) : null,
            value.SourceVehicleId is { } sourceId ? await PlateAsync(sourceId).ConfigureAwait(false) : null);
    }

    // Quantity held by each vehicle: transfers into it minus what was used from it. Optionally for one product.
    // Calls made inside a write transaction run under the product row lock taken by LockProductAsync.
    private static async Task<List<(int ProductId, int VehicleId, int Quantity)>> VehicleQuantitiesAsync(MySqlConnection connection,
        MySqlTransaction? transaction, int? productId, CancellationToken token)
    {
        await using var command = Command(connection, transaction, """
            SELECT id_produs,vid,SUM(qty) FROM (
                SELECT id_produs,id_vehicul AS vid,io_numar_bucati AS qty FROM io
                WHERE io_tip_actiune=0 AND io_destinatie=2 AND id_vehicul IS NOT NULL AND (@product IS NULL OR id_produs=@product)
                UNION ALL
                SELECT id_produs,id_vehicul_sursa,-io_numar_bucati FROM io
                WHERE io_tip_actiune=0 AND id_vehicul_sursa IS NOT NULL AND (@product IS NULL OR id_produs=@product)) t
            GROUP BY id_produs,vid
            """, ("@product", productId));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        var result = new List<(int, int, int)>();
        while (await reader.ReadAsync(token).ConfigureAwait(false))
            result.Add((reader.GetInt32(0), reader.GetInt32(1), Convert.ToInt32(reader.GetValue(2))));
        return result;
    }

    // After a change: no vehicle may hold a negative quantity of the product (pieces already used or moved on).
    private static async Task EnsureVehicleStocksNotNegativeAsync(MySqlConnection connection, MySqlTransaction transaction,
        int productId, CancellationToken token)
    {
        foreach (var entry in await VehicleQuantitiesAsync(connection, transaction, productId, token).ConfigureAwait(false))
        {
            if (entry.Quantity >= 0) continue;
            await using var command = Command(connection, transaction, "SELECT vehicul_numar FROM vehicul WHERE id_vehicul=@id", ("@id", entry.VehicleId));
            var plate = await command.ExecuteScalarAsync(token).ConfigureAwait(false) as string ?? $"#{entry.VehicleId}";
            throw new StockMovementOperationException(StockMovementRules.NegativeVehicleStockMessage(plate));
        }
    }

    private static async Task<string> UsernameAsync(MySqlConnection connection, MySqlTransaction transaction, int userId, CancellationToken token)
    {
        await using var command = Command(connection, transaction, "SELECT username FROM `user` WHERE id_user=@id", ("@id", userId));
        return await command.ExecuteScalarAsync(token).ConfigureAwait(false) as string ?? string.Empty;
    }

    private static async Task<StockMovement?> GetMovementAsync(MySqlConnection connection, MySqlTransaction? transaction, int id,
        bool forUpdate, CancellationToken token)
    {
        await using var command = Command(connection, transaction,
            $"{SelectMovement} WHERE io.id_io=@id{(forUpdate ? " FOR UPDATE" : string.Empty)}", ("@id", id));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        return await reader.ReadAsync(token).ConfigureAwait(false) ? ReadMovement(reader) : null;
    }

    private static async Task<IReadOnlyList<StockMovementHistoryEntry>> GetHistoryAsync(MySqlConnection connection,
        MySqlTransaction? transaction, int movementId, CancellationToken token)
    {
        await using var command = Command(connection, transaction, """
            SELECT h.id,h.id_io,COALESCE(u.username,''),h.timestamp,h.modificare,h.motiv
            FROM io_history h LEFT JOIN `user` u ON u.id_user=h.id_user WHERE h.id_io=@id ORDER BY h.id
            """, ("@id", movementId));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        var result = new List<StockMovementHistoryEntry>();
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            var changes = Text(reader, 4);
            result.Add(new(reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2), ParseTimestamp(Text(reader, 3)),
                changes, ParseCorrection(changes), Text(reader, 5)));
        }
        return result;
    }

    // The correction is stored as text in the legacy `modificare` column: "...; Corecție stoc: +3".
    private static int ParseCorrection(string changes)
    {
        const string marker = "Corecție stoc:";
        var index = changes.LastIndexOf(marker, StringComparison.Ordinal);
        return index >= 0 && int.TryParse(changes[(index + marker.Length)..].Trim(), NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out var value) ? value : 0;
    }

    private static DateTime ParseTimestamp(string value) => DateTime.TryParseExact(value, "yyyy-MM-dd HH:mm:ss",
        CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
        ? parsed : DateTime.MinValue;

    private static DateOnly ParseDate(string value) => DateOnly.TryParseExact(value.Trim(), "dd-MM-yyyy",
        CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : DateOnly.MinValue;

    private static StockMovement ReadMovement(MySqlDataReader reader)
    {
        var created = reader.IsDBNull(12) ? DateTime.MinValue : DateTime.SpecifyKind(reader.GetDateTime(12), DateTimeKind.Utc);
        var updated = reader.IsDBNull(13) ? created : DateTime.SpecifyKind(reader.GetDateTime(13), DateTimeKind.Utc);
        var beneficiaryId = reader.GetInt32(6);
        return new StockMovement(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2) == 1 ? StockMovementKind.Entry : StockMovementKind.Exit,
            reader.GetInt32(3), ParseDate(reader.GetString(4)), Text(reader, 5), beneficiaryId == 0 ? null : beneficiaryId,
            reader.IsDBNull(7) ? null : reader.GetString(7), reader.IsDBNull(8) ? null : reader.GetInt32(8),
            reader.IsDBNull(9) ? null : reader.GetString(9), reader.GetString(10), reader.GetInt64(11), created, updated,
            Convert.ToBoolean(reader.GetValue(14)),
            reader.IsDBNull(15) ? null : (ExitDestination)Convert.ToInt32(reader.GetValue(15)),
            reader.IsDBNull(16) ? null : reader.GetInt32(16), reader.IsDBNull(17) ? null : reader.GetString(17),
            reader.IsDBNull(18) ? null : reader.GetInt32(18), reader.IsDBNull(19) ? null : reader.GetString(19));
    }

    private static async Task LogAsync(MySqlConnection connection, MySqlTransaction transaction, int userId, string operation,
        StockMovement? before, StockMovement? after, string reason, CancellationToken token)
    {
        var entry = JsonSerializer.Serialize(new { Source = "BlazorStoc", Entity = "io", Operation = operation, Before = before, After = after, Reason = reason });
        await using var command = Command(connection, transaction, "INSERT INTO log (id_user,log_command) VALUES (@user,@entry)",
            ("@user", userId), ("@entry", entry));
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    private static string Text(MySqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);

    private static MySqlCommand Command(MySqlConnection connection, MySqlTransaction? transaction, string sql,
        params (string Name, object? Value)[] parameters)
    {
        var command = new MySqlCommand(sql, connection, transaction);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }

    private Task EnsureOperatorAsync(CancellationToken token) =>
        accessControl?.EnsureProductOperatorAsync(token) ?? Task.CompletedTask;
}
