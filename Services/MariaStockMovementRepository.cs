using System.Data;
using MySqlConnector;

namespace BlazorStoc.Services;

// MariaDB mode (Subtask 2.4): maps the movements onto the real, migrated schema (stock_movements / stock_movement_history,
// FK'd to products/beneficiaries/projects/vehicles - see migration\schema-mariadb.sql). Column names mirror the SQLite
// development schema exactly, so every query here is a near-literal translation of SqliteStockMovementRepository.cs;
// only the connection type, parameter/date-text handling (Subtask 2.6, MariaTimeText) and the locking strategy differ.
public sealed class MariaStockMovementRepository(IConfiguration configuration, IAccessControl? accessControl = null,
    IAuditTrail? auditTrail = null, IArchiveService? archiveService = null) : IStockMovementRepository
{
    private const string ProductMissingMessage = "Produsul nu mai există. Actualizează catalogul.";
    private readonly IArchiveService archiver = archiveService ?? new ArchiveService(accessControl);
    private MySqlConnection CreateConnection() => DatabaseConnections.Create(configuration);

    private const string SelectMovement = """
        SELECT m.id,m.product_id,m.kind,m.quantity,m.movement_date,m.description,m.beneficiary_id,b.name,m.project_id,p.name,
               m.operator,m.version,m.created_utc,m.updated_utc,
               EXISTS(SELECT 1 FROM stock_movement_history h WHERE h.movement_id=m.id),
               m.destination,m.vehicle_id,dv.plate_number,m.source_vehicle_id,sv.plate_number
        FROM stock_movements m
        LEFT JOIN beneficiaries b ON b.id=m.beneficiary_id
        LEFT JOIN projects p ON p.id=m.project_id
        LEFT JOIN vehicles dv ON dv.id=m.vehicle_id
        LEFT JOIN vehicles sv ON sv.id=m.source_vehicle_id
        """;

    public async Task<StockMovementPage> GetPageAsync(int productId, StockMovementQuery query, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var stock = await GetStockAsync(connection, null, productId, cancellationToken).ConfigureAwait(false)
                    ?? throw new StockMovementOperationException(ProductMissingMessage);
        object kind = query.Kind is null ? DBNull.Value : (int)query.Kind.Value;
        int total;
        await using (var count = Command(connection, null,
            "SELECT COUNT(*) FROM stock_movements WHERE product_id=@product AND (@kind IS NULL OR kind=@kind)",
            ("@product", productId), ("@kind", kind)))
            total = Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        bool anyModified;
        await using (var modified = Command(connection, null, """
            SELECT EXISTS(SELECT 1 FROM stock_movement_history h INNER JOIN stock_movements m ON m.id=h.movement_id
                          WHERE m.product_id=@product)
            """, ("@product", productId)))
            anyModified = Convert.ToBoolean(await modified.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        var direction = query.Descending ? "DESC" : "ASC";
        // MySQL/MariaDB LIMIT needs a non-negative bound; long.MaxValue stands in for "no limit" (PageSize <= 0).
        var limit = query.PageSize <= 0 ? long.MaxValue : query.PageSize;
        var offset = query.PageSize <= 0 ? 0 : (long)(Math.Max(1, query.Page) - 1) * query.PageSize;
        await using var command = Command(connection, null, $"""
            {SelectMovement}
            WHERE m.product_id=@product AND (@kind IS NULL OR m.kind=@kind)
            ORDER BY m.movement_date {direction}, m.id {direction}
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
        await using var command = Command(connection, null, "SELECT id,plate_number,description FROM vehicles ORDER BY plate_number,id");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var id = ToInt32(reader.GetInt64(0));
            if (held.TryGetValue(id, out var quantity)) result.Add(new(id, reader.GetString(1), reader.GetString(2), quantity));
        }
        return result;
    }

    public async Task<IReadOnlyList<VehicleEquipment>> GetVehicleEquipmentAsync(int vehicleId, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var held = (await VehicleQuantitiesAsync(connection, null, null, cancellationToken).ConfigureAwait(false))
            .Where(entry => entry.VehicleId == vehicleId && entry.Quantity > 0).ToList();
        var result = new List<VehicleEquipment>();
        foreach (var entry in held)
            result.Add(new(entry.ProductId,
                await GetProductCodeAsync(connection, null, entry.ProductId, cancellationToken).ConfigureAwait(false) ?? $"#{entry.ProductId}",
                entry.Quantity));
        return result.OrderBy(item => item.ProductCode, StringComparer.CurrentCultureIgnoreCase).ThenBy(item => item.ProductId).ToList();
    }

    public async Task<IReadOnlyList<StockMovement>> TransferFromVehicleAsync(VehicleTransfer transfer, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        StockMovementRules.ValidateTransfer(transfer);
        var actor = await SqliteRepositoryAudit.ActorAsync(accessControl, cancellationToken).ConfigureAwait(false);
        var now = DateTime.UtcNow;
        var nowText = MariaTimeText.Format(now);
        var day = StockMovementRules.Today;
        var (movements, productCodes) = await WriteAsync(async (connection, transaction) =>
        {
            async Task<string> PlateAsync(int id)
            {
                await using var command = Command(connection, transaction, "SELECT plate_number FROM vehicles WHERE id=@id", ("@id", id));
                return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string
                       ?? throw new StockMovementOperationException(StockMovementRules.VehicleMissingMessage);
            }
            var sourcePlate = await PlateAsync(transfer.SourceVehicleId).ConfigureAwait(false);
            var targetPlate = transfer.TargetVehicleId is { } targetId ? await PlateAsync(targetId).ConfigureAwait(false) : null;
            // Products are locked in identifier order first, so two concurrent transfers cannot deadlock each other.
            var candidateIds = (await VehicleQuantitiesAsync(connection, transaction, null, cancellationToken).ConfigureAwait(false))
                .Where(entry => entry.VehicleId == transfer.SourceVehicleId && entry.Quantity > 0).Select(entry => entry.ProductId);
            var productIds = (transfer.Lines?.Select(line => line.ProductId) ?? candidateIds).Distinct().Order().ToList();
            var productCodes = new Dictionary<int, string>();
            foreach (var productId in productIds)
                productCodes[productId] = await LockProductAsync(connection, transaction, productId, cancellationToken).ConfigureAwait(false);
            var held = (await VehicleQuantitiesAsync(connection, transaction, null, cancellationToken).ConfigureAwait(false))
                .Where(entry => entry.VehicleId == transfer.SourceVehicleId && entry.Quantity > 0)
                .ToDictionary(entry => entry.ProductId, entry => entry.Quantity);
            var plan = (transfer.Lines ?? held.Select(entry => new VehicleTransferLine(entry.Key, entry.Value)).ToList())
                .OrderBy(line => line.ProductId).ToList();
            if (plan.Count == 0) throw new StockMovementOperationException(StockMovementRules.NothingToTransferMessage);
            var description = TextNormalization.ForStorage(StockMovementRules.TransferDescription(sourcePlate, targetPlate, day));
            var destination = transfer.TargetVehicleId is null ? ExitDestination.WarehouseReturn : ExitDestination.Vehicle;
            var created = new List<StockMovement>();
            foreach (var line in plan)
            {
                var productCode = productCodes.TryGetValue(line.ProductId, out var known) ? known
                    : await LockProductAsync(connection, transaction, line.ProductId, cancellationToken).ConfigureAwait(false);
                var available = held.GetValueOrDefault(line.ProductId);
                if (line.Quantity > available)
                    throw new StockMovementOperationException($"{productCode}: {StockMovementRules.NotEnoughInVehicleMessage(sourcePlate, available)}");
                await using var insert = Command(connection, transaction, """
                    INSERT INTO stock_movements
                        (product_id,beneficiary_id,project_id,quantity,created_utc,kind,movement_date,description,operator,version,updated_utc,
                         destination,vehicle_id,source_vehicle_id)
                    VALUES(@product,NULL,NULL,@quantity,@created,0,@date,@description,@operator,0,@created,@destination,@vehicle,@sourceVehicle)
                    """, ("@product", line.ProductId), ("@quantity", line.Quantity), ("@created", nowText),
                    ("@date", StockMovementRules.StorageDate(day)), ("@description", description), ("@operator", actor.Username),
                    ("@destination", (int)destination), ("@vehicle", transfer.TargetVehicleId), ("@sourceVehicle", transfer.SourceVehicleId));
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                var id = checked((int)insert.LastInsertedId);
                var movement = new StockMovement(id, line.ProductId, StockMovementKind.Exit, line.Quantity, day, description, null, null, null, null,
                    actor.Username, 0, now, now, false, destination, transfer.TargetVehicleId, targetPlate, transfer.SourceVehicleId, sourcePlate);
                await EnsureVehicleStocksNotNegativeAsync(connection, transaction, line.ProductId, cancellationToken).ConfigureAwait(false);
                created.Add(movement);
            }
            return (created, productCodes);
        }, cancellationToken).ConfigureAwait(false);
        foreach (var movement in movements)
            await AuditRecorder.RecordCreateAsync(auditTrail, accessControl, AuditEntities.StockMovement, movement.Id.ToString(),
                StockMovementRules.Target(productCodes[movement.ProductId]),
                StockMovementRules.AuditIdentification(movement, productCodes[movement.ProductId]), cancellationToken).ConfigureAwait(false);
        return movements;
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
                SELECT vehicle_id AS vid FROM stock_movements WHERE vehicle_id IS NOT NULL
                UNION ALL SELECT source_vehicle_id FROM stock_movements WHERE source_vehicle_id IS NOT NULL) t
            GROUP BY vid
            """);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var counts = new Dictionary<int, int>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            counts[ToInt32(reader.GetInt64(0))] = Convert.ToInt32(reader.GetValue(1));
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
            SELECT m.id,m.product_id,p.name,m.quantity,m.movement_date,m.operator
            FROM stock_movements m INNER JOIN products p ON p.id=m.product_id
            WHERE m.project_id=@project AND m.kind=0
            ORDER BY m.movement_date DESC,m.id DESC
            """, ("@project", projectId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<ProjectStockMovement>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new(ToInt32(reader.GetInt64(0)), ToInt32(reader.GetInt64(1)), reader.GetString(2), ToInt32(reader.GetInt64(3)),
                StockMovementRules.ParseStorageDate(reader.GetString(4)), reader.GetString(5)));
        return result;
    }

    public async Task<StockMovementResult> CreateAsync(int productId, StockMovementInput input, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        var value = StockMovementRules.Validated(input, input.Kind, false);
        var actor = await SqliteRepositoryAudit.ActorAsync(accessControl, cancellationToken).ConfigureAwait(false);
        var now = DateTime.UtcNow;
        var nowText = MariaTimeText.Format(now);
        var (movement, stock, productCode) = await WriteAsync(async (connection, transaction) =>
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
                INSERT INTO stock_movements
                    (product_id,beneficiary_id,project_id,quantity,created_utc,kind,movement_date,description,operator,version,updated_utc,
                     destination,vehicle_id,source_vehicle_id)
                VALUES(@product,@beneficiary,@project,@quantity,@created,@kind,@date,@description,@operator,0,@created,
                       @destination,@vehicle,@sourceVehicle)
                """, ("@product", productId), ("@beneficiary", value.BeneficiaryId), ("@project", value.ProjectId),
                ("@quantity", value.Quantity), ("@created", nowText), ("@kind", (int)value.Kind),
                ("@date", StockMovementRules.StorageDate(value.Date!.Value)), ("@description", value.Description),
                ("@operator", actor.Username), ("@destination", (int?)value.Destination), ("@vehicle", value.VehicleId),
                ("@sourceVehicle", value.SourceVehicleId));
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            var id = checked((int)insert.LastInsertedId);
            var movement = new StockMovement(id, productId, value.Kind, value.Quantity!.Value, value.Date.Value, value.Description,
                value.BeneficiaryId, beneficiaryName, value.ProjectId, projectName, actor.Username, 0, now, now, false,
                value.Destination, value.VehicleId, vehiclePlate, value.SourceVehicleId, sourcePlate);
            await EnsureVehicleStocksNotNegativeAsync(connection, transaction, productId, cancellationToken).ConfigureAwait(false);
            var stock = await ApplyStockAsync(connection, transaction, productId, movement.Effect, cancellationToken).ConfigureAwait(false);
            return (movement, stock, productCode);
        }, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordCreateAsync(auditTrail, accessControl, AuditEntities.StockMovement, movement.Id.ToString(),
            StockMovementRules.Target(productCode), StockMovementRules.AuditIdentification(movement, productCode), cancellationToken).ConfigureAwait(false);
        return new StockMovementResult(movement, stock);
    }

    public async Task<StockMovementResult> UpdateAsync(StockMovement original, StockMovementInput input, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        var value = StockMovementRules.Validated(input, original.Kind, true, allowVehicleTransfer: original.IsVehicleTransfer);
        var actor = await SqliteRepositoryAudit.ActorAsync(accessControl, cancellationToken).ConfigureAwait(false);
        var now = DateTime.UtcNow;
        var nowText = MariaTimeText.Format(now);
        var (current, updated, stock, productCode) = await WriteAsync(async (connection, transaction) =>
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
                UPDATE stock_movements SET quantity=@quantity,movement_date=@date,description=@description,
                    beneficiary_id=@beneficiary,project_id=@project,version=@version,updated_utc=@updated,
                    destination=@destination,vehicle_id=@vehicle,source_vehicle_id=@sourceVehicle
                WHERE id=@id AND version=@oldVersion
                """, ("@quantity", updated.Quantity), ("@date", StockMovementRules.StorageDate(updated.Date)),
                ("@description", updated.Description), ("@beneficiary", updated.BeneficiaryId), ("@project", updated.ProjectId),
                ("@version", updated.Version), ("@updated", nowText), ("@id", current.Id), ("@oldVersion", current.Version),
                ("@destination", (int?)updated.Destination), ("@vehicle", updated.VehicleId), ("@sourceVehicle", updated.SourceVehicleId)))
                if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                    throw new StockMovementOperationException(StockMovementRules.StaleMessage);
            await using (var history = Command(connection, transaction, """
                INSERT INTO stock_movement_history(movement_id,actor,timestamp_utc,changes,stock_correction,reason)
                VALUES(@movement,@actor,@timestamp,@changes,@correction,@reason)
                """, ("@movement", current.Id), ("@actor", actor.Username), ("@timestamp", nowText),
                ("@changes", StockMovementRules.HistorySummary(current, updated, correction)), ("@correction", correction),
                ("@reason", value.Reason)))
                await history.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await EnsureVehicleStocksNotNegativeAsync(connection, transaction, current.ProductId, cancellationToken).ConfigureAwait(false);
            var stock = await ApplyStockAsync(connection, transaction, current.ProductId, correction, cancellationToken).ConfigureAwait(false);
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
            productCode = await GetProductCodeAsync(lookup, null, original.ProductId, cancellationToken).ConfigureAwait(false)
                          ?? throw new StockMovementOperationException(ProductMissingMessage);
            history = await GetHistoryAsync(lookup, null, original.Id, cancellationToken).ConfigureAwait(false);
        }
        var stock = 0;
        await archiver.ExecuteAsync(ArchiveRequests.StockMovement(original, productCode, history, motif), async (operation, token) =>
        {
            stock = await WriteAsync(async (connection, transaction) =>
            {
                var current = await GetMovementAsync(connection, transaction, original.Id, true, token).ConfigureAwait(false);
                StockMovementRules.CheckCurrent(current, original);
                await LockProductAsync(connection, transaction, current!.ProductId, token).ConfigureAwait(false);
                await MariaArchivePersistence.InsertAsync(connection, transaction, operation, [], token).ConfigureAwait(false);
                await using (var deleteHistory = Command(connection, transaction,
                    "DELETE FROM stock_movement_history WHERE movement_id=@id", ("@id", original.Id)))
                    await deleteHistory.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                await using (var delete = Command(connection, transaction,
                    "DELETE FROM stock_movements WHERE id=@id AND version=@version", ("@id", original.Id), ("@version", original.Version)))
                    if (await delete.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
                        throw new StockMovementOperationException(StockMovementRules.StaleMessage);
                await EnsureVehicleStocksNotNegativeAsync(connection, transaction, current.ProductId, token).ConfigureAwait(false);
                var newStock = await ApplyStockAsync(connection, transaction, current.ProductId, -current.Effect, token).ConfigureAwait(false);
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
        await using var command = Command(connection, transaction, "SELECT EXISTS(SELECT 1 FROM stock_movements WHERE project_id=@id)", ("@id", projectId));
        return Convert.ToBoolean(await command.ExecuteScalarAsync(token).ConfigureAwait(false));
    }

    // Used by the vehicle module to block deleting a vehicle that still has movements.
    internal static async Task<bool> VehicleHasMovementsAsync(MySqlConnection connection, MySqlTransaction? transaction, int vehicleId,
        CancellationToken token)
    {
        await using var command = Command(connection, transaction,
            "SELECT EXISTS(SELECT 1 FROM stock_movements WHERE vehicle_id=@id OR source_vehicle_id=@id)", ("@id", vehicleId));
        return Convert.ToBoolean(await command.ExecuteScalarAsync(token).ConfigureAwait(false));
    }

    private async Task<MySqlConnection> OpenAsync(CancellationToken token)
    {
        var connection = CreateConnection();
        try
        {
            await connection.OpenAsync(token).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task<T> WriteAsync<T>(Func<MySqlConnection, MySqlTransaction, Task<T>> action, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!MariaDatabaseGuard.IsAllowedDatabase(configuration))
            throw new StockMovementOperationException("Modificările sunt permise numai în baza BlazorStoc. Verifică numele bazei configurate.");
        await using var connection = await OpenAsync(token).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, token).ConfigureAwait(false);
        try
        {
            var result = await action(connection, transaction).ConfigureAwait(false);
            await transaction.CommitAsync(token).ConfigureAwait(false);
            return result;
        }
        catch
        {
            if (transaction.Connection is not null) await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    // Locks the product row for the rest of the transaction: every stock/vehicle-quantity check and the final
    // UPDATE products SET quantity=... below run under this lock, so two concurrent movements on the same product
    // serialize instead of racing (the MariaDB equivalent of SQLite's single-writer Serializable transaction).
    private static async Task<string> LockProductAsync(MySqlConnection connection, MySqlTransaction transaction, int productId, CancellationToken token)
    {
        await using var command = Command(connection, transaction, "SELECT name FROM products WHERE id=@id FOR UPDATE", ("@id", productId));
        return await command.ExecuteScalarAsync(token).ConfigureAwait(false) as string
               ?? throw new StockMovementOperationException(ProductMissingMessage);
    }

    // Atomic increment under the product row lock: concurrent movements cannot overwrite each other's stock change.
    private static async Task<int> ApplyStockAsync(MySqlConnection connection, MySqlTransaction transaction, int productId,
        int delta, CancellationToken token)
    {
        if (delta != 0)
            await using (var update = Command(connection, transaction,
                "UPDATE products SET quantity=quantity+@delta WHERE id=@id", ("@delta", delta), ("@id", productId)))
                if (await update.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
                    throw new StockMovementOperationException(ProductMissingMessage);
        return await GetStockAsync(connection, transaction, productId, token).ConfigureAwait(false)
               ?? throw new StockMovementOperationException(ProductMissingMessage);
    }

    private static async Task<int?> GetStockAsync(MySqlConnection connection, MySqlTransaction? transaction, int productId, CancellationToken token)
    {
        await using var command = Command(connection, transaction, "SELECT quantity FROM products WHERE id=@id", ("@id", productId));
        var value = await command.ExecuteScalarAsync(token).ConfigureAwait(false);
        return value is null or DBNull ? null : Convert.ToInt32(value);
    }

    private static async Task<string?> GetProductCodeAsync(MySqlConnection connection, MySqlTransaction? transaction, int productId, CancellationToken token)
    {
        await using var command = Command(connection, transaction, "SELECT name FROM products WHERE id=@id", ("@id", productId));
        return await command.ExecuteScalarAsync(token).ConfigureAwait(false) as string;
    }

    private static async Task<(string? BeneficiaryName, string? ProjectName)> ResolveRelationsAsync(MySqlConnection connection,
        MySqlTransaction transaction, StockMovementInput value, CancellationToken token)
    {
        string? beneficiaryName = null, projectName = null;
        if (value.BeneficiaryId is { } beneficiaryId)
        {
            await using var command = Command(connection, transaction, "SELECT name FROM beneficiaries WHERE id=@id", ("@id", beneficiaryId));
            beneficiaryName = await command.ExecuteScalarAsync(token).ConfigureAwait(false) as string
                              ?? throw new StockMovementOperationException("Beneficiarul selectat nu mai există. Actualizează lista și reia operația.");
        }
        if (value.ProjectId is { } projectId)
        {
            await using var command = Command(connection, transaction, "SELECT name,beneficiary_id FROM projects WHERE id=@id", ("@id", projectId));
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false))
                throw new StockMovementOperationException("Proiectul selectat nu mai există. Actualizează lista și reia operația.");
            if (ToInt32(reader.GetInt64(1)) != value.BeneficiaryId)
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
            await using var command = Command(connection, transaction, "SELECT plate_number FROM vehicles WHERE id=@id", ("@id", id));
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
            SELECT product_id,vid,SUM(qty) FROM (
                SELECT product_id,vehicle_id AS vid,quantity AS qty FROM stock_movements
                WHERE kind=0 AND destination=2 AND vehicle_id IS NOT NULL AND (@product IS NULL OR product_id=@product)
                UNION ALL
                SELECT product_id,source_vehicle_id,-quantity FROM stock_movements
                WHERE kind=0 AND source_vehicle_id IS NOT NULL AND (@product IS NULL OR product_id=@product)) t
            GROUP BY product_id,vid
            """, ("@product", productId));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        var result = new List<(int, int, int)>();
        while (await reader.ReadAsync(token).ConfigureAwait(false))
            result.Add((ToInt32(reader.GetInt64(0)), ToInt32(reader.GetInt64(1)), Convert.ToInt32(reader.GetValue(2))));
        return result;
    }

    // After a change: no vehicle may hold a negative quantity of the product (pieces already used or moved on).
    private static async Task EnsureVehicleStocksNotNegativeAsync(MySqlConnection connection, MySqlTransaction transaction,
        int productId, CancellationToken token)
    {
        foreach (var entry in await VehicleQuantitiesAsync(connection, transaction, productId, token).ConfigureAwait(false))
        {
            if (entry.Quantity >= 0) continue;
            await using var command = Command(connection, transaction, "SELECT plate_number FROM vehicles WHERE id=@id", ("@id", entry.VehicleId));
            var plate = await command.ExecuteScalarAsync(token).ConfigureAwait(false) as string ?? $"#{entry.VehicleId}";
            throw new StockMovementOperationException(StockMovementRules.NegativeVehicleStockMessage(plate));
        }
    }

    // Locks only the stock_movements row itself (not the joined beneficiary/project/vehicle rows the SELECT below
    // also reads), so an edit/delete on one movement can never deadlock against an edit/delete on another that
    // happens to reference the same beneficiary, project or vehicle.
    private static async Task<StockMovement?> GetMovementAsync(MySqlConnection connection, MySqlTransaction? transaction, int id,
        bool forUpdate, CancellationToken token)
    {
        if (forUpdate)
        {
            await using var lockCommand = Command(connection, transaction, "SELECT id FROM stock_movements WHERE id=@id FOR UPDATE", ("@id", id));
            if (await lockCommand.ExecuteScalarAsync(token).ConfigureAwait(false) is null) return null;
        }
        await using var command = Command(connection, transaction, $"{SelectMovement} WHERE m.id=@id", ("@id", id));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        return await reader.ReadAsync(token).ConfigureAwait(false) ? ReadMovement(reader) : null;
    }

    private static async Task<IReadOnlyList<StockMovementHistoryEntry>> GetHistoryAsync(MySqlConnection connection,
        MySqlTransaction? transaction, int movementId, CancellationToken token)
    {
        await using var command = Command(connection, transaction, """
            SELECT id,movement_id,actor,timestamp_utc,changes,stock_correction,reason
            FROM stock_movement_history WHERE movement_id=@id ORDER BY id
            """, ("@id", movementId));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        var result = new List<StockMovementHistoryEntry>();
        while (await reader.ReadAsync(token).ConfigureAwait(false))
            result.Add(new(ToInt32(reader.GetInt64(0)), ToInt32(reader.GetInt64(1)), reader.GetString(2),
                MariaTimeText.Parse(reader.GetString(3)), reader.GetString(4), ToInt32(reader.GetInt64(5)), reader.GetString(6)));
        return result;
    }

    private static StockMovement ReadMovement(MySqlDataReader reader)
    {
        var created = MariaTimeText.Parse(reader.GetString(12));
        var stored = reader.GetString(4);
        var date = stored.Length == 0 ? DateOnly.FromDateTime(created) : StockMovementRules.ParseStorageDate(stored);
        return new StockMovement(ToInt32(reader.GetInt64(0)), ToInt32(reader.GetInt64(1)), (StockMovementKind)ToInt32(reader.GetInt64(2)),
            ToInt32(reader.GetInt64(3)), date, reader.GetString(5),
            reader.IsDBNull(6) ? null : ToInt32(reader.GetInt64(6)), reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.IsDBNull(8) ? null : ToInt32(reader.GetInt64(8)), reader.IsDBNull(9) ? null : reader.GetString(9),
            reader.GetString(10), reader.GetInt64(11), created, ReadUpdated(reader, created),
            Convert.ToBoolean(reader.GetValue(14)),
            reader.IsDBNull(15) ? null : (ExitDestination)ToInt32(reader.GetInt64(15)),
            reader.IsDBNull(16) ? null : ToInt32(reader.GetInt64(16)), reader.IsDBNull(17) ? null : reader.GetString(17),
            reader.IsDBNull(18) ? null : ToInt32(reader.GetInt64(18)), reader.IsDBNull(19) ? null : reader.GetString(19));
    }

    private static DateTime ReadUpdated(MySqlDataReader reader, DateTime fallback)
    {
        var text = reader.GetString(13);
        return text.Length == 0 ? fallback : MariaTimeText.Parse(text);
    }

    private static int ToInt32(long value) => checked((int)value);

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
