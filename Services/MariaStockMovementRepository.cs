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
               m.destination,m.vehicle_id,dv.plate_number,m.source_vehicle_id,sv.plate_number,
               m.invoice_id,si.`number`,su.name,si.supplier_id,
               m.free_entry_type,m.free_supplier_id,fs.name,m.reference,m.over_stock_cause,m.operation_id,m.voided_utc,m.void_reason,m.voided_by,m.return_of_movement_id
        FROM stock_movements m
        LEFT JOIN beneficiaries b ON b.id=m.beneficiary_id
        LEFT JOIN projects p ON p.id=m.project_id
        LEFT JOIN vehicles dv ON dv.id=m.vehicle_id
        LEFT JOIN vehicles sv ON sv.id=m.source_vehicle_id
        LEFT JOIN supplier_invoices si ON si.id=m.invoice_id
        LEFT JOIN suppliers su ON su.id=si.supplier_id
        LEFT JOIN suppliers fs ON fs.id=m.free_supplier_id
        """;

    public async Task<StockMovementPage> GetPageAsync(int productId, StockMovementQuery query, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var stock = await GetStockAsync(connection, null, productId, cancellationToken).ConfigureAwait(false)
                    ?? throw new StockMovementOperationException(ProductMissingMessage);
        object kind = query.Kind is null ? DBNull.Value : (int)query.Kind.Value;
        // Source and supplier narrow the entries only (an exit never has an invoice).
        object source = query.Source == EntrySource.All ? DBNull.Value : (int)query.Source;
        object supplier = query.SupplierId is { } supplierFilter ? supplierFilter : DBNull.Value;
        var overStock = await OverStockIdsAsync(connection, productId, stock, cancellationToken).ConfigureAwait(false);
        var overStockFilter = query.OverStockOnly ? $" AND m.id IN ({(overStock.Count == 0 ? "0" : string.Join(",", overStock))})" : "";
        var sourceFilter = $"""
            AND (@source IS NULL OR (m.kind=1 AND ((@source=1 AND m.invoice_id IS NOT NULL) OR (@source=2 AND m.invoice_id IS NULL))))
            AND (@supplier IS NULL OR (m.kind=1 AND m.invoice_id IN (SELECT id FROM supplier_invoices WHERE supplier_id=@supplier)))
            AND (@destination IS NULL OR (m.kind=0 AND m.destination=@destination))
            AND (@beneficiary IS NULL OR (m.kind=0 AND m.beneficiary_id=@beneficiary))
            AND (@vehicle IS NULL OR (m.kind=0 AND (m.vehicle_id=@vehicle OR m.source_vehicle_id=@vehicle)))
            AND (@day IS NULL OR m.movement_date=@day)
            AND (@text IS NULL OR m.description LIKE @text OR IFNULL(m.reference,'') LIKE @text
                 OR EXISTS(SELECT 1 FROM supplier_invoices si2 LEFT JOIN suppliers su2 ON su2.id=si2.supplier_id WHERE si2.id=m.invoice_id AND (si2.`number` LIKE @text OR su2.name LIKE @text))
                 OR EXISTS(SELECT 1 FROM suppliers fs2 WHERE fs2.id=m.free_supplier_id AND fs2.name LIKE @text)
                 OR EXISTS(SELECT 1 FROM beneficiaries b2 WHERE b2.id=m.beneficiary_id AND b2.name LIKE @text)
                 OR EXISTS(SELECT 1 FROM projects p2 WHERE p2.id=m.project_id AND p2.name LIKE @text)
                 OR EXISTS(SELECT 1 FROM vehicles v2 WHERE v2.id IN (m.vehicle_id,m.source_vehicle_id) AND v2.plate_number LIKE @text))
            {overStockFilter}
            """;
        object destinationFilter = query.Destination is { } chosenDestination ? (int)chosenDestination : DBNull.Value;
        object beneficiaryFilter = query.BeneficiaryId is { } chosenBeneficiary ? chosenBeneficiary : DBNull.Value;
        object vehicleFilter = query.VehicleId is { } chosenVehicle ? chosenVehicle : DBNull.Value;
        // The text filter: part of a description, reference, invoice number, supplier, beneficiary, project or plate (the characters of LIKE are taken literally).
        object textFilter = string.IsNullOrWhiteSpace(query.Text) ? DBNull.Value
            : "%" + query.Text.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
        object dayFilter = query.Date is { } chosenDay ? StockMovementRules.StorageDate(chosenDay) : DBNull.Value;
        int total;
        await using (var count = Command(connection, null,
            $"SELECT COUNT(*) FROM stock_movements m WHERE m.product_id=@product AND (@kind IS NULL OR m.kind=@kind) {sourceFilter}",
            ("@product", productId), ("@kind", kind), ("@source", source), ("@supplier", supplier),
            ("@destination", destinationFilter), ("@beneficiary", beneficiaryFilter), ("@vehicle", vehicleFilter), ("@text", textFilter), ("@day", dayFilter)))
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
        var items = new List<StockMovement>();
        // The reader must be closed before the next command: MySqlConnector allows one open reader per connection.
        await using (var command = Command(connection, null, $"""
            {SelectMovement}
            WHERE m.product_id=@product AND (@kind IS NULL OR m.kind=@kind) {sourceFilter}
            ORDER BY m.movement_date {direction}, m.id {direction}
            LIMIT @limit OFFSET @offset
            """, ("@product", productId), ("@kind", kind), ("@source", source), ("@supplier", supplier), ("@limit", limit), ("@offset", offset),
            ("@destination", destinationFilter), ("@beneficiary", beneficiaryFilter), ("@vehicle", vehicleFilter), ("@text", textFilter), ("@day", dayFilter)))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) items.Add(ReadMovement(reader));
        var inVehicles = (await VehicleQuantitiesAsync(connection, null, productId, cancellationToken).ConfigureAwait(false))
            .Sum(entry => Math.Max(0, entry.Quantity));
        var suppliers = new List<MovementSupplier>();
        await using (var supplierCommand = Command(connection, null, """
            SELECT DISTINCT su.id, su.name FROM stock_movements m
            INNER JOIN supplier_invoices si ON si.id=m.invoice_id INNER JOIN suppliers su ON su.id=si.supplier_id
            WHERE m.product_id=@product ORDER BY su.name
            """, ("@product", productId)))
        await using (var supplierReader = await supplierCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            while (await supplierReader.ReadAsync(cancellationToken).ConfigureAwait(false))
                suppliers.Add(new(ToInt32(supplierReader.GetInt64(0)), supplierReader.GetString(1)));
        var exitBeneficiaries = new List<MovementSupplier>();
        await using (var beneficiaryCommand = Command(connection, null, """
            SELECT DISTINCT b.id, b.name FROM stock_movements m INNER JOIN beneficiaries b ON b.id=m.beneficiary_id
            WHERE m.product_id=@product AND m.kind=0 ORDER BY b.name
            """, ("@product", productId)))
        await using (var beneficiaryReader = await beneficiaryCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            while (await beneficiaryReader.ReadAsync(cancellationToken).ConfigureAwait(false))
                exitBeneficiaries.Add(new(ToInt32(beneficiaryReader.GetInt64(0)), beneficiaryReader.GetString(1)));
        var exitVehicles = new List<MovementSupplier>();
        await using (var vehicleCommand = Command(connection, null, """
            SELECT DISTINCT v.id, v.plate_number FROM stock_movements m
            INNER JOIN vehicles v ON v.id=m.vehicle_id OR v.id=m.source_vehicle_id
            WHERE m.product_id=@product AND m.kind=0 ORDER BY v.plate_number
            """, ("@product", productId)))
        await using (var vehicleReader = await vehicleCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            while (await vehicleReader.ReadAsync(cancellationToken).ConfigureAwait(false))
                exitVehicles.Add(new(ToInt32(vehicleReader.GetInt64(0)), vehicleReader.GetString(1)));
        return new StockMovementPage(items, total, stock, anyModified, inVehicles, suppliers, overStock, exitBeneficiaries, exitVehicles);
    }

    // Ids of the exits that took more than the warehouse held (see StockMovementRules.OverStockExits).
    private static async Task<IReadOnlySet<int>> OverStockIdsAsync(MySqlConnection connection, int productId, int totalStock, CancellationToken token)
    {
        var inVehicles = (await VehicleQuantitiesAsync(connection, null, productId, token).ConfigureAwait(false)).Sum(entry => Math.Max(0, entry.Quantity));
        var rows = new List<(int, DateOnly, StockMovementKind, ExitDestination?, int?, int, int?)>();
        await using var command = Command(connection, null,
            "SELECT id,movement_date,created_utc,kind,destination,source_vehicle_id,quantity,vehicle_id FROM stock_movements WHERE product_id=@product AND voided_utc IS NULL", ("@product", productId));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            var stored = reader.GetString(1);
            var date = stored.Length == 0 ? DateOnly.FromDateTime(MariaTimeText.Parse(reader.GetString(2))) : StockMovementRules.ParseStorageDate(stored);
            rows.Add((ToInt32(reader.GetInt64(0)), date, (StockMovementKind)ToInt32(reader.GetInt64(3)), reader.IsDBNull(4) ? null : (ExitDestination)ToInt32(reader.GetInt64(4)),
                reader.IsDBNull(5) ? null : ToInt32(reader.GetInt64(5)), ToInt32(reader.GetInt64(6)), reader.IsDBNull(7) ? null : ToInt32(reader.GetInt64(7))));
        }
        return StockMovementRules.OverStockExits(rows, StockMovementRules.WarehouseStock(totalStock, inVehicles));
    }

    public async Task<StockMovementResult> RegularizeNegativeStockAsync(int productId, int realWarehouseQuantity, string context, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        if (realWarehouseQuantity is < 0 or > StockMovementRules.MaxQuantity) throw new StockMovementOperationException(StockMovementRules.RealQuantityMessage);
        var actor = await RepositoryAudit.ActorAsync(accessControl, cancellationToken).ConfigureAwait(false);
        var now = DateTime.UtcNow;
        var nowText = MariaTimeText.Format(now);
        var today = StockMovementRules.Today;
        var (movement, stock, productCode, oldStock) = await WriteAsync(async (connection, transaction) =>
        {
            var productCode = await LockProductAsync(connection, transaction, productId, cancellationToken).ConfigureAwait(false);
            var current = await GetStockAsync(connection, transaction, productId, cancellationToken).ConfigureAwait(false) ?? throw new StockMovementOperationException(ProductMissingMessage);
            if (current >= 0) throw new StockMovementOperationException(StockMovementRules.StockNotNegativeMessage);
            var inVehicles = (await VehicleQuantitiesAsync(connection, transaction, productId, cancellationToken).ConfigureAwait(false)).Sum(entry => Math.Max(0, entry.Quantity));
            var delta = realWarehouseQuantity + inVehicles - current;
            var description = TextNormalization.ForStorage(StockMovementRules.RegularizationDescription(current, realWarehouseQuantity, context));
            if (description.Length > StockMovementRules.MaxDescriptionLength) description = description[..StockMovementRules.MaxDescriptionLength];
            await using var insert = Command(connection, transaction, """
                INSERT INTO stock_movements
                    (product_id,quantity,created_utc,kind,movement_date,description,operator,version,updated_utc,free_entry_type,reference)
                VALUES(@product,@quantity,@created,1,@date,@description,@operator,0,@created,@type,@reference)
                """, ("@product", productId), ("@quantity", delta), ("@created", nowText), ("@date", StockMovementRules.StorageDate(today)),
                ("@description", description), ("@operator", actor.Username), ("@type", (int)FreeEntryType.Adjustment), ("@reference", StockMovementRules.RegularizationReference));
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            var movement = new StockMovement(checked((int)insert.LastInsertedId), productId, StockMovementKind.Entry, delta, today, description, null, null, null, null,
                actor.Username, 0, now, now, FreeType: FreeEntryType.Adjustment, Reference: StockMovementRules.RegularizationReference);
            var stock = await ApplyStockAsync(connection, transaction, productId, delta, cancellationToken).ConfigureAwait(false);
            return (movement, stock, productCode, current);
        }, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.StockMovement, AuditActions.RegularizeNegativeStock, movement.Id.ToString(),
            StockMovementRules.Target(productCode), AuditDetails.Identification((ProductCode.Label, productCode), ("Stoc înainte", oldStock.ToString()),
                ("Cantitate reală în depozit", realWarehouseQuantity.ToString()), ("Corecție", $"+{movement.Quantity}"), ("Stoc după", stock.ToString())),
            string.IsNullOrWhiteSpace(context) ? string.Empty : context, cancellationToken).ConfigureAwait(false);
        return new StockMovementResult(movement, stock);
    }

    public async Task<IReadOnlyList<RegularizationItem>> GetToRegularizeAsync(RegularizationQuery? query = null, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        return StockMovementRules.FilterRegularization(await ReadToRegularizeAsync(connection, cancellationToken).ConfigureAwait(false), query, StockMovementRules.Today);
    }

    // One pass over every movement: the products whose warehouse (or a vehicle) is negative after exits over the stock. Also used, without the
    // operator check, by the notification source (see OverStockSource).
    internal static async Task<IReadOnlyList<RegularizationItem>> ReadToRegularizeAsync(MySqlConnection connection, CancellationToken token)
    {
        var stocks = new Dictionary<int, (string Name, int Stock)>();
        await using (var products = Command(connection, null, "SELECT id,name,quantity FROM products"))
        await using (var reader = await products.ExecuteReaderAsync(token).ConfigureAwait(false))
            while (await reader.ReadAsync(token).ConfigureAwait(false)) stocks[ToInt32(reader.GetInt64(0))] = (reader.GetString(1), ToInt32(reader.GetInt64(2)));
        var vehicleRows = await VehicleQuantitiesAsync(connection, null, null, token).ConfigureAwait(false);
        var inVehicles = vehicleRows.GroupBy(entry => entry.ProductId).ToDictionary(group => group.Key, group => group.Sum(entry => Math.Max(0, entry.Quantity)));
        var negativeVehicles = vehicleRows.Where(entry => entry.Quantity < 0).Select(entry => entry.ProductId).ToHashSet();
        var rows = new Dictionary<int, List<(int, DateOnly, StockMovementKind, ExitDestination?, int?, int, int?, OverStockCause?)>>();
        await using (var command = Command(connection, null, """
            SELECT id,product_id,movement_date,created_utc,kind,destination,source_vehicle_id,quantity,vehicle_id,over_stock_cause FROM stock_movements WHERE voided_utc IS NULL
            """))
        await using (var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                var productId = ToInt32(reader.GetInt64(1));
                var stored = reader.GetString(2);
                var date = stored.Length == 0 ? DateOnly.FromDateTime(MariaTimeText.Parse(reader.GetString(3))) : StockMovementRules.ParseStorageDate(stored);
                if (!rows.TryGetValue(productId, out var list)) rows[productId] = list = [];
                list.Add((ToInt32(reader.GetInt64(0)), date, (StockMovementKind)ToInt32(reader.GetInt64(4)),
                    reader.IsDBNull(5) ? null : (ExitDestination)ToInt32(reader.GetInt64(5)), reader.IsDBNull(6) ? null : ToInt32(reader.GetInt64(6)),
                    ToInt32(reader.GetInt64(7)), reader.IsDBNull(8) ? null : ToInt32(reader.GetInt64(8)),
                    reader.IsDBNull(9) ? null : (OverStockCause)ToInt32(reader.GetInt64(9))));
            }
        var result = new List<RegularizationItem>();
        foreach (var (productId, list) in rows)
        {
            if (!stocks.TryGetValue(productId, out var product)) continue;
            var warehouse = StockMovementRules.WarehouseStock(product.Stock, inVehicles.GetValueOrDefault(productId));
            var open = StockMovementRules.UnresolvedOverStock(list, warehouse);
            if (open.Count == 0) continue;
            result.Add(new RegularizationItem(productId, product.Name, product.Stock, warehouse, negativeVehicles.Contains(productId), open.Count,
                open[0].Date, open.LastOrDefault(item => item.Cause is not null).Cause));
        }
        return result;
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
        var actor = await RepositoryAudit.ActorAsync(accessControl, cancellationToken).ConfigureAwait(false);
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
            int? transferOperation = null;
            foreach (var line in plan)
            {
                var productCode = productCodes.TryGetValue(line.ProductId, out var known) ? known
                    : await LockProductAsync(connection, transaction, line.ProductId, cancellationToken).ConfigureAwait(false);
                var available = held.GetValueOrDefault(line.ProductId);
                if (line.Quantity > available)
                    throw new StockMovementOperationException($"{productCode}: {StockMovementRules.NotEnoughInVehicleMessage(sourcePlate, available)}");
                var snapshot = await VehicleSnapshotAsync(connection, transaction, line.ProductId, cancellationToken).ConfigureAwait(false);
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
                // The lines of one transfer are one operation (the id of the first line).
                transferOperation ??= id;
                await using (var operationUpdate = Command(connection, transaction, "UPDATE stock_movements SET operation_id=@op WHERE id=@id", ("@op", transferOperation), ("@id", id)))
                    await operationUpdate.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                var movement = new StockMovement(id, line.ProductId, StockMovementKind.Exit, line.Quantity, day, description, null, null, null, null,
                    actor.Username, 0, now, now, false, destination, transfer.TargetVehicleId, targetPlate, transfer.SourceVehicleId, sourcePlate, OperationId: transferOperation);
                await EnsureVehicleStocksNotWorseAsync(connection, transaction, line.ProductId, snapshot, cancellationToken).ConfigureAwait(false);
                created.Add(movement);
            }
            return (created, productCodes);
        }, cancellationToken).ConfigureAwait(false);
        foreach (var movement in movements)
            await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.StockMovement,
                transfer.TargetVehicleId is null ? AuditActions.ReturnEquipment : AuditActions.MoveEquipment, movement.Id.ToString(),
                StockMovementRules.Target(productCodes[movement.ProductId]),
                StockMovementRules.AuditIdentification(movement, productCodes[movement.ProductId]), movement.Description, cancellationToken).ConfigureAwait(false);
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
            WHERE m.project_id=@project AND m.kind=0 AND m.voided_utc IS NULL
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
        var actor = await RepositoryAudit.ActorAsync(accessControl, cancellationToken).ConfigureAwait(false);
        var now = DateTime.UtcNow;
        var created = await WriteAsync((connection, transaction) =>
            InsertMovementAsync(connection, transaction, productId, value, actor.Username, now, null, cancellationToken), cancellationToken).ConfigureAwait(false);
        await RecordCreatedAsync(created, value, cancellationToken).ConfigureAwait(false);
        return new StockMovementResult(created.Movement, created.Stock);
    }

    // An exit to a beneficiary that a return can be tied to: its product, quantity and what earlier (not voided) returns already took back.
    private static async Task<(int ProductId, int Remaining)?> ReturnableAsync(MySqlConnection connection, MySqlTransaction? transaction, int exitId, CancellationToken token)
    {
        await using var command = Command(connection, transaction, """
            SELECT e.product_id,e.quantity-(SELECT COALESCE(SUM(r.quantity),0) FROM stock_movements r WHERE r.return_of_movement_id=e.id AND r.voided_utc IS NULL)
            FROM stock_movements e WHERE e.id=@id AND e.kind=0 AND e.destination=@beneficiary AND e.voided_utc IS NULL
            """, ("@id", exitId), ("@beneficiary", (int)ExitDestination.Beneficiary));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        return await reader.ReadAsync(token).ConfigureAwait(false) ? (ToInt32(reader.GetInt64(0)), Convert.ToInt32(reader.GetValue(1))) : null;
    }

    public async Task<IReadOnlyList<ReturnableExit>> GetReturnableExitsAsync(int productId, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, null, """
            SELECT e.id,e.operation_id,e.movement_date,e.reference,b.name,p.name,e.quantity,
                   (SELECT COALESCE(SUM(r.quantity),0) FROM stock_movements r WHERE r.return_of_movement_id=e.id AND r.voided_utc IS NULL)
            FROM stock_movements e LEFT JOIN beneficiaries b ON b.id=e.beneficiary_id LEFT JOIN projects p ON p.id=e.project_id
            WHERE e.product_id=@product AND e.kind=0 AND e.destination=@beneficiary AND e.voided_utc IS NULL
            ORDER BY e.movement_date DESC,e.id DESC
            """, ("@product", productId), ("@beneficiary", (int)ExitDestination.Beneficiary));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<ReturnableExit>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var exit = new ReturnableExit(ToInt32(reader.GetInt64(0)), reader.IsDBNull(1) ? ToInt32(reader.GetInt64(0)) : ToInt32(reader.GetInt64(1)),
                StockMovementRules.ParseStorageDate(reader.GetString(2)), reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5),
                ToInt32(reader.GetInt64(6)), Convert.ToInt32(reader.GetValue(7)));
            if (exit.Remaining > 0) result.Add(exit);
        }
        return result;
    }

    // An entry may be tied to a component of a project (the offer it supplies); the component must be active.
    private static async Task LinkEntryComponentAsync(MySqlConnection connection, MySqlTransaction? transaction, int movementId, int componentId, CancellationToken token)
    {
        await using var check = Command(connection, transaction, "SELECT COUNT(*) FROM project_components WHERE id=@id AND archived_utc IS NULL", ("@id", componentId));
        if (Convert.ToInt32(await check.ExecuteScalarAsync(token).ConfigureAwait(false)) == 0) throw new StockMovementOperationException(StockMovementRules.ComponentInvalidMessage);
        await using var update = Command(connection, transaction, "UPDATE stock_movements SET project_component_id=@component WHERE id=@id", ("@component", componentId), ("@id", movementId));
        await update.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    // Reservations at an exit from the warehouse to a beneficiary or a generic sale: the exit consumes the reservation of its own project first; what is left is
    // compared with the free stock, and pieces reserved by other projects are touched only after a warning (or when the user chose to lower a reservation).
    private static async Task<ReservationOutcome?> ApplyReservationsAsync(MySqlConnection connection, MySqlTransaction transaction, int productId, string productCode,
        StockMovementInput value, CancellationToken token)
    {
        if (value is not { Kind: StockMovementKind.Exit, SourceVehicleId: null } || value.Destination is not (ExitDestination.Beneficiary or ExitDestination.GenericSale)) return null;
        var total = await ReservationSql.TotalAsync(connection, transaction, productId, token).ConfigureAwait(false);
        if (total <= 0) return null;
        var quantity = value.Quantity!.Value;
        var stock = await GetStockAsync(connection, transaction, productId, token).ConfigureAwait(false) ?? 0;
        var events = new List<ReservationEvent>();
        var ownRows = new List<(int Id, int Quantity, string? Component)>();
        string projectName = string.Empty;
        if (value is { Destination: ExitDestination.Beneficiary, ProjectId: { } project })
        {
            await using (var names = Command(connection, transaction, "SELECT name FROM projects WHERE id=@id", ("@id", project)))
                projectName = await names.ExecuteScalarAsync(token).ConfigureAwait(false) as string ?? string.Empty;
            await using var own = Command(connection, transaction, """
                SELECT r.id,r.quantity,t.name FROM project_reservations r LEFT JOIN project_components c ON c.id=r.project_component_id LEFT JOIN system_types t ON t.id=c.system_type_id
                WHERE r.project_id=@project AND r.product_id=@product ORDER BY (r.project_component_id<=>@component) DESC,r.id FOR UPDATE
                """, ("@project", project), ("@product", productId), ("@component", value.ProjectComponentId));
            await using var reader = await own.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false)) ownRows.Add((checked((int)reader.GetInt64(0)), Convert.ToInt32(reader.GetValue(1)), reader.IsDBNull(2) ? null : reader.GetString(2)));
        }
        var ownTotal = ownRows.Sum(row => row.Quantity);
        var touched = ReservationRules.Touched(quantity, stock, total, ownTotal);
        var toConsume = Math.Min(quantity, ownTotal);
        foreach (var row in ownRows)
        {
            if (toConsume <= 0) break;
            var take = Math.Min(toConsume, row.Quantity);
            await ReservationSql.DecreaseAsync(connection, transaction, row.Id, take, token).ConfigureAwait(false);
            toConsume -= take;
            events.Add(new ReservationEvent(AuditActions.ReservationConsumed, value.ProjectId!.Value, projectName,
                AuditDetails.Identification(("Proiect", projectName), ("Produs", productCode), ("Componentă", row.Component ?? "—"), ("Consumat", $"{take} buc.")), string.Empty));
        }
        var holders = (await ReservationSql.HoldersAsync(connection, transaction, productId, token).ConfigureAwait(false)).Where(item => item.ProjectId != value.ProjectId).ToList();
        if (touched > 0)
        {
            if (value.ReduceReservationProjectId is { } reduceProject)
            {
                var holder = holders.FirstOrDefault(item => item.ProjectId == reduceProject) ?? throw new StockMovementOperationException("Proiectul ales nu mai are rezervări pentru acest produs. Reia ieșirea.");
                if (ReservationRules.ReasonError(value.ReduceReservationReason) is { Length: > 0 } reasonError) throw new StockMovementOperationException(reasonError);
                var reduce = Math.Min(value.ReduceReservationQuantity ?? touched, holder.Quantity);
                if (reduce <= 0) throw new StockMovementOperationException(ReservationRules.QuantityMessage);
                var left = reduce;
                var rows = new List<(int Id, int Quantity)>();
                await using (var select = Command(connection, transaction, "SELECT id,quantity FROM project_reservations WHERE project_id=@project AND product_id=@product ORDER BY id FOR UPDATE", ("@project", reduceProject), ("@product", productId)))
                await using (var reader = await select.ExecuteReaderAsync(token).ConfigureAwait(false))
                    while (await reader.ReadAsync(token).ConfigureAwait(false)) rows.Add((checked((int)reader.GetInt64(0)), Convert.ToInt32(reader.GetValue(1))));
                foreach (var row in rows)
                {
                    if (left <= 0) break;
                    var take = Math.Min(left, row.Quantity);
                    await ReservationSql.DecreaseAsync(connection, transaction, row.Id, take, token).ConfigureAwait(false);
                    left -= take;
                }
                events.Add(new ReservationEvent(AuditActions.ReservationReducedByExit, reduceProject, holder.ProjectName,
                    AuditDetails.Identification(("Proiect", holder.ProjectName), ("Produs", productCode)) + "; "
                    + AuditDetails.Changes([new AuditChange("Rezervat", $"{holder.Quantity} buc.", $"{holder.Quantity - reduce} buc.")]), value.ReduceReservationReason));
            }
            else if (!value.ReservationAck) throw new ReservationWarningException(touched, holders);
        }
        return new ReservationOutcome(touched, holders, events);
    }

    // Ties an exit to a project to a component: the chosen one (it must be an active component of the project), "in afara ofertei", or decided from the offers:
    // the only component that has the product, "in afara ofertei" when the project has offers but none has it, refused when several have it.
    private static async Task LinkComponentAsync(MySqlConnection connection, MySqlTransaction? transaction, int movementId, int projectId, int productId, StockMovementInput value, CancellationToken token)
    {
        int? component = null;
        var outside = false;
        if (value.ProjectComponentId is { } chosen)
        {
            await using var check = Command(connection, transaction, "SELECT COUNT(*) FROM project_components WHERE id=@id AND project_id=@project AND archived_utc IS NULL", ("@id", chosen), ("@project", projectId));
            if (Convert.ToInt32(await check.ExecuteScalarAsync(token).ConfigureAwait(false)) == 0) throw new StockMovementOperationException(StockMovementRules.ComponentInvalidMessage);
            component = chosen;
        }
        else if (value.OutsideOffer) outside = true;
        else
        {
            var found = new List<int>();
            await using (var command = Command(connection, transaction, ComponentExitSql.ComponentsWithProduct, ("@product", productId), ("@project", projectId)))
            await using (var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                while (await reader.ReadAsync(token).ConfigureAwait(false)) found.Add(checked((int)reader.GetInt64(0)));
            if (found.Count > 1) throw new StockMovementOperationException(StockMovementRules.ComponentChoiceMessage);
            if (found.Count == 1) component = found[0];
            else
            {
                await using var offers = Command(connection, transaction, "SELECT COUNT(*) FROM offers WHERE project_id=@project", ("@project", projectId));
                outside = Convert.ToInt32(await offers.ExecuteScalarAsync(token).ConfigureAwait(false)) > 0;
            }
        }
        if (component is null && !outside) return;
        await using var update = Command(connection, transaction, "UPDATE stock_movements SET project_component_id=@component,outside_offer=@outside WHERE id=@id",
            ("@component", component), ("@outside", outside ? 1 : 0), ("@id", movementId));
        await update.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<NetConsumption>> GetNetConsumptionAsync(int? projectId, int? beneficiaryId, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        if (projectId is null && beneficiaryId is null) return [];
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        // The exits to the project (or beneficiary) per product, and the returns tied to those exits.
        await using var command = Command(connection, null, """
            SELECT e.product_id,p.name,SUM(e.quantity),
                   SUM((SELECT COALESCE(SUM(r.quantity),0) FROM stock_movements r WHERE r.return_of_movement_id=e.id AND r.voided_utc IS NULL))
            FROM stock_movements e INNER JOIN products p ON p.id=e.product_id
            WHERE e.kind=0 AND e.voided_utc IS NULL AND e.destination=@beneficiaryDestination
              AND (@project IS NULL OR e.project_id=@project) AND (@beneficiary IS NULL OR e.beneficiary_id=@beneficiary)
            GROUP BY e.product_id,p.name ORDER BY p.name,e.product_id
            """, ("@project", projectId), ("@beneficiary", beneficiaryId), ("@beneficiaryDestination", (int)ExitDestination.Beneficiary));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<NetConsumption>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new(ToInt32(reader.GetInt64(0)), reader.GetString(1), Convert.ToInt32(reader.GetValue(2)), Convert.ToInt32(reader.GetValue(3))));
        return result;
    }

    public async Task<ExitOperationDetails?> GetOperationAsync(int operationId, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var movements = new List<StockMovement>();
        await using (var command = Command(connection, null, $"{SelectMovement} WHERE m.operation_id=@op AND m.kind=0 ORDER BY m.id", ("@op", operationId)))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) movements.Add(ReadMovement(reader));
        if (movements.Count == 0) return null;
        var first = movements[0];
        string? cui = null;
        if (first.BeneficiaryId is { } beneficiaryId)
        {
            await using var cuiCommand = Command(connection, null, "SELECT cui FROM beneficiaries WHERE id=@id", ("@id", beneficiaryId));
            cui = await cuiCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        }
        var names = new Dictionary<int, string>();
        await using (var namesCommand = Command(connection, null, $"SELECT id,name FROM products WHERE id IN ({string.Join(",", movements.Select(item => item.ProductId).Distinct())})"))
        await using (var namesReader = await namesCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            while (await namesReader.ReadAsync(cancellationToken).ConfigureAwait(false)) names[ToInt32(namesReader.GetInt64(0))] = namesReader.GetString(1);
        return new ExitOperationDetails(operationId, first.Date, first.Destination, first.BeneficiaryName, cui, first.ProjectName, first.VehiclePlate, first.Reference,
            first.Operator, first.VoidedUtc, first.VoidReason,
            movements.Select(item => new ExitOperationLineInfo(item.Id, item.ProductId, names.GetValueOrDefault(item.ProductId) ?? $"#{item.ProductId}", item.Quantity,
                StockMovementRules.SourceLabel(item), item.Description)).ToList());
    }

    public async Task VoidExitOperationAsync(int operationId, string reason, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        if (ChangeReasonRules.ValidationError(reason) is { } problem) throw new StockMovementOperationException(problem);
        reason = TextNormalization.ForStorage(reason);
        var actor = await RepositoryAudit.ActorAsync(accessControl, cancellationToken).ConfigureAwait(false);
        var now = DateTime.UtcNow;
        var nowText = MariaTimeText.Format(now);
        var (voided, codes) = await WriteAsync(async (connection, transaction) =>
        {
            var ids = new List<int>();
            await using (var select = Command(connection, transaction,
                "SELECT id FROM stock_movements WHERE operation_id=@op AND kind=0 AND voided_utc IS NULL ORDER BY id FOR UPDATE", ("@op", operationId)))
            await using (var reader = await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) ids.Add(ToInt32(reader.GetInt64(0)));
            if (ids.Count == 0) throw new StockMovementOperationException(StockMovementRules.OperationMissingMessage);
            await using (var returns = Command(connection, transaction,
                $"SELECT EXISTS(SELECT 1 FROM stock_movements WHERE return_of_movement_id IN ({string.Join(",", ids)}) AND voided_utc IS NULL)"))
                if (Convert.ToInt32(await returns.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) == 1)
                    throw new StockMovementOperationException(StockMovementRules.VoidHasReturnsMessage);
            var movements = new List<StockMovement>();
            foreach (var id in ids) movements.Add((await GetMovementAsync(connection, transaction, id, false, cancellationToken).ConfigureAwait(false))!);
            var codes = new Dictionary<int, string>();
            foreach (var productId in movements.Select(item => item.ProductId).Distinct().Order())
                codes[productId] = await LockProductAsync(connection, transaction, productId, cancellationToken).ConfigureAwait(false);
            foreach (var group in movements.GroupBy(item => item.ProductId))
            {
                var snapshot = await VehicleSnapshotAsync(connection, transaction, group.Key, cancellationToken).ConfigureAwait(false);
                foreach (var movement in group)
                {
                    await using (var update = Command(connection, transaction, """
                        UPDATE stock_movements SET voided_utc=@when,void_reason=@reason,voided_by=@by,version=version+1,updated_utc=@when WHERE id=@id
                        """, ("@when", nowText), ("@reason", reason), ("@by", actor.Username), ("@id", movement.Id)))
                        await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    await using (var history = Command(connection, transaction, """
                        INSERT INTO stock_movement_history(movement_id,actor,timestamp_utc,changes,stock_correction,reason)
                        VALUES(@movement,@actor,@when,@changes,@correction,@reason)
                        """, ("@movement", movement.Id), ("@actor", actor.Username), ("@when", nowText),
                        ("@changes", $"Stornat (operația #{operationId})"), ("@correction", -movement.Effect), ("@reason", reason)))
                        await history.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
                await ApplyStockAsync(connection, transaction, group.Key, -group.Sum(item => item.Effect), cancellationToken).ConfigureAwait(false);
                await EnsureVehicleStocksNotWorseAsync(connection, transaction, group.Key, snapshot, cancellationToken).ConfigureAwait(false);
            }
            return (movements, codes);
        }, cancellationToken).ConfigureAwait(false);
        var lines = string.Join("; ", voided.Select(item => $"{codes[item.ProductId]} x {item.Quantity}"));
        await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.StockMovement, AuditActions.VoidExitOperation, voided[0].Id.ToString(),
            $"Operația #{operationId}", AuditDetails.Identification(("Operație", $"#{operationId}"), ("Ieșiri stornate", voided.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("Produse", lines), ("Data", StockMovementRules.DisplayDate(voided[0].Date)),
                ("Destinație", voided[0].Destination is { } destination ? StockMovementRules.DestinationLabel(destination) : "—")),
            reason, cancellationToken).ConfigureAwait(false);
    }

    private IReadOnlyList<(int ProductId, StockMovementInput Value)> ValidatedOperation(IReadOnlyList<ExitOperationLine> lines)
    {
        if (lines.Count == 0) throw new StockMovementOperationException(StockMovementRules.EmptyOperationMessage);
        if (lines.Count > StockMovementRules.MaxOperationLines) throw new StockMovementOperationException(StockMovementRules.OperationLinesMessage);
        var result = new List<(int ProductId, StockMovementInput Value)>();
        for (var index = 0; index < lines.Count; index++)
        {
            try { result.Add((lines[index].ProductId, StockMovementRules.Validated(lines[index].Input, StockMovementKind.Exit, false))); }
            catch (StockMovementOperationException ex) { throw new StockMovementOperationException(StockMovementRules.LineMessage(index, ex.Message)); }
        }
        var first = result[0].Value;
        if (result.Any(line => line.Value.Date != first.Date || line.Value.Destination != first.Destination || line.Value.BeneficiaryId != first.BeneficiaryId
                               || line.Value.ProjectId != first.ProjectId || line.Value.VehicleId != first.VehicleId || line.Value.Reference != first.Reference))
            throw new StockMovementOperationException(StockMovementRules.OperationMismatchMessage);
        return result;
    }

    // Inserts every line in one transaction (products locked in id order); a failing line prefixes the message with its number and nothing is saved.
    private async Task<List<CreatedMovement>> InsertOperationAsync(MySqlConnection connection, MySqlTransaction transaction,
        IReadOnlyList<(int ProductId, StockMovementInput Value)> values, string operatorName, DateTime now, CancellationToken cancellationToken)
    {
        foreach (var productId in values.Select(line => line.ProductId).Distinct().Order())
        {
            try { await LockProductAsync(connection, transaction, productId, cancellationToken).ConfigureAwait(false); }
            catch (StockMovementOperationException ex) { throw new StockMovementOperationException(StockMovementRules.LineMessage(values.ToList().FindIndex(line => line.ProductId == productId), ex.Message)); }
        }
        var created = new List<CreatedMovement>();
        int? operationId = null;
        for (var index = 0; index < values.Count; index++)
        {
            try
            {
                var line = await InsertMovementAsync(connection, transaction, values[index].ProductId, values[index].Value, operatorName, now, operationId, cancellationToken).ConfigureAwait(false);
                operationId = line.Movement.OperationId;
                created.Add(line);
            }
            catch (DuplicateExitWarningException ex) { throw new DuplicateExitWarningException(StockMovementRules.LineMessage(index, ex.Message)); }
            catch (StockMovementOperationException ex) { throw new StockMovementOperationException(StockMovementRules.LineMessage(index, ex.Message)); }
        }
        return created;
    }

    public async Task<IReadOnlyList<ExitLinePreview>> PreviewExitOperationAsync(IReadOnlyList<ExitOperationLine> lines, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        var values = ValidatedOperation(lines).Select(line => (line.ProductId, Value: WithPreviewReason(line.Value))).ToList();
        var actor = await RepositoryAudit.ActorAsync(accessControl, cancellationToken).ConfigureAwait(false);
        var now = DateTime.UtcNow;
        if (!MariaDatabaseGuard.IsAllowedDatabase(configuration))
            throw new StockMovementOperationException("Modificările sunt permise numai în baza BlazorStoc. Verifică numele bazei configurate.");
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        try
        {
            var created = await InsertOperationAsync(connection, transaction, values, actor.Username, now, cancellationToken).ConfigureAwait(false);
            return created.Select((line, index) => new ExitLinePreview(index, line.Movement.ProductId, line.ProductCode, line.OverStock, line.Confirmed, line.Stock, line.Reservation?.Excess ?? 0, line.Reservation?.Holders)).ToList();
        }
        finally
        {
            // Only a preview: nothing is kept.
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    // A repeated exit throws without a reason; the preview wants to know about it, so it gets a placeholder reason (never saved).
    private static StockMovementInput WithPreviewReason(StockMovementInput value)
    {
        var copy = value.Clone();
        if (string.IsNullOrWhiteSpace(copy.DuplicateReason)) copy.DuplicateReason = "previzualizare";
        copy.ReservationAck = true;
        return copy;
    }

    public async Task<ExitOperationResult> CreateExitOperationAsync(IReadOnlyList<ExitOperationLine> lines, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        var values = ValidatedOperation(lines);
        var actor = await RepositoryAudit.ActorAsync(accessControl, cancellationToken).ConfigureAwait(false);
        var now = DateTime.UtcNow;
        var created = await WriteAsync((connection, transaction) =>
            InsertOperationAsync(connection, transaction, values, actor.Username, now, cancellationToken), cancellationToken).ConfigureAwait(false);
        for (var index = 0; index < created.Count; index++)
            await RecordCreatedAsync(created[index], values[index].Value, cancellationToken).ConfigureAwait(false);
        return new ExitOperationResult(created[0].Movement.OperationId ?? created[0].Movement.Id,
            created.Select(line => new StockMovementResult(line.Movement, line.Stock)).ToList());
    }

    private sealed record ReservationEvent(string Action, int ProjectId, string ProjectName, string Details, string Reason);
    private sealed record ReservationOutcome(int Excess, IReadOnlyList<ReservationHolder> Holders, IReadOnlyList<ReservationEvent> Events);
    private sealed record CreatedMovement(StockMovement Movement, int Stock, string ProductCode, bool Confirmed, int OverStock, ReservationOutcome? Reservation = null);

    // Inserts one movement inside the given transaction (locks the product, checks the vehicle quantities, the repeated exit and the invoice
    // entry). Used by a single movement and, in one transaction, by every line of an exit operation.
    private async Task<CreatedMovement> InsertMovementAsync(MySqlConnection connection, MySqlTransaction transaction, int productId,
        StockMovementInput value, string operatorName, DateTime now, int? operationId, CancellationToken cancellationToken)
    {
        var actor = (Username: operatorName, Role: string.Empty);
        var nowText = MariaTimeText.Format(now);
        var overStock = 0;
            var productCode = await LockProductAsync(connection, transaction, productId, cancellationToken).ConfigureAwait(false);
            var (beneficiaryName, projectName) = await ResolveRelationsAsync(connection, transaction, value, cancellationToken).ConfigureAwait(false);
            var (vehiclePlate, sourcePlate) = await ResolveVehiclesAsync(connection, transaction, value, cancellationToken).ConfigureAwait(false);
            // Moves between vehicles and returns are physical: they cannot take more than the vehicle holds. Using a product from a vehicle
            // (beneficiary, sale, correction) can be recorded above what the vehicle is known to hold (marked "peste stoc").
            var physicalMove = value.Destination is ExitDestination.Vehicle or ExitDestination.WarehouseReturn;
            var vehicleBefore = 0;
            if (value.SourceVehicleId is { } sourceVehicleId)
            {
                var held = (await VehicleQuantitiesAsync(connection, transaction, productId, cancellationToken).ConfigureAwait(false))
                    .Where(entry => entry.VehicleId == sourceVehicleId).Sum(entry => entry.Quantity);
                if (physicalMove && value.Quantity!.Value > held)
                    throw new StockMovementOperationException(StockMovementRules.NotEnoughInVehicleMessage(sourcePlate!, held));
                vehicleBefore = Math.Max(0, held);
            }
            var duplicateExit = false;
            if (value.Kind == StockMovementKind.Exit)
                duplicateExit = await CheckDuplicateExitAsync(connection, transaction, productId, productCode, value, cancellationToken).ConfigureAwait(false);
            var (invoiceNumber, supplierName, supplierId) = await ResolveInvoiceAsync(connection, transaction, value.InvoiceId, cancellationToken).ConfigureAwait(false);
            string? freeSupplierName = null;
            if (value.FreeSupplierId is { } freeSupplierId)
            {
                await using var freeSupplier = Command(connection, transaction, "SELECT name FROM suppliers WHERE id=@id", ("@id", freeSupplierId));
                freeSupplierName = await freeSupplier.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string
                                   ?? throw new StockMovementOperationException("Furnizorul ales nu mai există. Actualizează lista și reia operația.");
            }
            var confirmedDuplicate = duplicateExit;
            if (value.InvoiceId is { } checkedInvoice)
                confirmedDuplicate = await CheckInvoiceEntryAsync(connection, transaction, checkedInvoice, productId, productCode, value, cancellationToken).ConfigureAwait(false);
            if (value.ReturnOfMovementId is { } returnOf)
            {
                var returnable = await ReturnableAsync(connection, transaction, returnOf, cancellationToken).ConfigureAwait(false);
                if (returnable is null || returnable.Value.ProductId != productId) throw new StockMovementOperationException(StockMovementRules.ReturnMissingMessage);
                if (value.Quantity!.Value > returnable.Value.Remaining) throw new StockMovementOperationException(StockMovementRules.ReturnTooMuchMessage(Math.Max(0, returnable.Value.Remaining)));
            }
            await using var insert = Command(connection, transaction, """
                INSERT INTO stock_movements
                    (product_id,beneficiary_id,project_id,quantity,created_utc,kind,movement_date,description,operator,version,updated_utc,
                     destination,vehicle_id,source_vehicle_id,invoice_id,free_entry_type,free_supplier_id,reference,over_stock_cause,operation_id,return_of_movement_id)
                VALUES(@product,@beneficiary,@project,@quantity,@created,@kind,@date,@description,@operator,0,@created,
                       @destination,@vehicle,@sourceVehicle,@invoice,@freeType,@freeSupplier,@reference,@cause,@operation,@returnOf)
                """, ("@product", productId), ("@beneficiary", value.BeneficiaryId), ("@project", value.ProjectId),
                ("@quantity", value.Quantity), ("@created", nowText), ("@kind", (int)value.Kind),
                ("@date", StockMovementRules.StorageDate(value.Date!.Value)), ("@description", value.Description),
                ("@operator", actor.Username), ("@destination", (int?)value.Destination), ("@vehicle", value.VehicleId),
                ("@sourceVehicle", value.SourceVehicleId), ("@invoice", value.InvoiceId), ("@freeType", (int?)value.FreeType),
                ("@freeSupplier", value.FreeSupplierId), ("@reference", value.Reference), ("@cause", (int?)value.OverStockCause), ("@operation", operationId), ("@returnOf", value.ReturnOfMovementId));
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            var id = checked((int)insert.LastInsertedId);
            if (value is { Kind: StockMovementKind.Exit, Destination: ExitDestination.Beneficiary, ProjectId: { } componentProject })
                await LinkComponentAsync(connection, transaction, id, componentProject, productId, value, cancellationToken).ConfigureAwait(false);
            else if (value is { Kind: StockMovementKind.Entry, ProjectComponentId: { } entryComponent })
                await LinkEntryComponentAsync(connection, transaction, id, entryComponent, cancellationToken).ConfigureAwait(false);
            var reservation = await ApplyReservationsAsync(connection, transaction, productId, productCode, value, cancellationToken).ConfigureAwait(false);
            // Every exit belongs to an operation: the first exit of an operation gives it its id (a single exit is an operation of one line).
            if (value.Kind == StockMovementKind.Exit && operationId is null)
            {
                operationId = id;
                await using var own = Command(connection, transaction, "UPDATE stock_movements SET operation_id=@id WHERE id=@id", ("@id", id));
                await own.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            if (value is { InvoiceId: { } lineInvoice, InvoiceQuantity: { } lineQuantity })
                await using (var line = Command(connection, transaction, """
                    INSERT INTO supplier_invoice_lines(invoice_id,product_id,quantity) VALUES(@invoice,@product,@quantity)
                    ON DUPLICATE KEY UPDATE quantity=VALUES(quantity)
                    """, ("@invoice", lineInvoice), ("@product", productId), ("@quantity", lineQuantity)))
                    await line.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            var movement = new StockMovement(id, productId, value.Kind, value.Quantity!.Value, value.Date.Value, value.Description,
                value.BeneficiaryId, beneficiaryName, value.ProjectId, projectName, actor.Username, 0, now, now, false,
                value.Destination, value.VehicleId, vehiclePlate, value.SourceVehicleId, sourcePlate,
                value.InvoiceId, invoiceNumber, supplierName, supplierId, value.FreeType, value.FreeSupplierId, freeSupplierName, value.Reference,
                value.OverStockCause, operationId, ReturnOfMovementId: value.ReturnOfMovementId);
            var warehouseBefore = movement.Kind == StockMovementKind.Exit && movement.SourceVehicleId is null
                ? StockMovementRules.WarehouseStock(await GetStockAsync(connection, transaction, productId, cancellationToken).ConfigureAwait(false) ?? 0,
                    (await VehicleQuantitiesAsync(connection, transaction, productId, cancellationToken).ConfigureAwait(false)).Sum(entry => Math.Max(0, entry.Quantity)))
                : (int?)null;
            var stock = await ApplyStockAsync(connection, transaction, productId, movement.Effect, cancellationToken).ConfigureAwait(false);
            overStock = warehouseBefore is { } before && movement.Quantity > Math.Max(0, before) ? movement.Quantity - Math.Max(0, before)
                : movement.Kind == StockMovementKind.Exit && movement.SourceVehicleId is not null && !physicalMove && movement.Quantity > vehicleBefore ? movement.Quantity - vehicleBefore
                : 0;
            // The cause is kept only for an exit that really went over the stock.
            if (overStock == 0 && movement.OverStockCause is not null)
            {
                await using var clear = Command(connection, transaction, "UPDATE stock_movements SET over_stock_cause=NULL WHERE id=@id", ("@id", movement.Id));
                await clear.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                movement = movement with { OverStockCause = null };
            }
            return new CreatedMovement(movement, stock, productCode, confirmedDuplicate, overStock, reservation);
    }

    private async Task RecordCreatedAsync(CreatedMovement created, StockMovementInput value, CancellationToken cancellationToken)
    {
        var (movement, _, productCode, confirmed, overStock, reservation) = created;
        var target = StockMovementRules.Target(productCode);
        var identification = StockMovementRules.AuditIdentification(movement, productCode);
        foreach (var item in reservation?.Events ?? [])
            await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.Project, item.Action, item.ProjectId.ToString(), item.ProjectName, item.Details, item.Reason, cancellationToken).ConfigureAwait(false);
        var details = overStock > 0 ? identification + $"; Peste stoc: {overStock}" : identification;
        if (confirmed)
            await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.StockMovement,
                movement.Kind == StockMovementKind.Exit ? AuditActions.DuplicateExit : AuditActions.DuplicateEntryOnInvoice,
                movement.Id.ToString(), target, details, value.DuplicateReason, cancellationToken).ConfigureAwait(false);
        else if (movement.Kind == StockMovementKind.Exit)
            await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.StockMovement, StockMovementRules.ExitAuditAction(movement.Destination),
                movement.Id.ToString(), target, details, string.Empty, cancellationToken).ConfigureAwait(false);
        else if (movement.ReturnOfMovementId is not null)
            await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.StockMovement, AuditActions.ReturnFromBeneficiary,
                movement.Id.ToString(), target, identification, string.Empty, cancellationToken).ConfigureAwait(false);
        else if (movement.FreeType is not null)
            await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.StockMovement, AuditActions.RecordFreeEntry,
                movement.Id.ToString(), target, identification, string.Empty, cancellationToken).ConfigureAwait(false);
        else
            await AuditRecorder.RecordCreateAsync(auditTrail, accessControl, AuditEntities.StockMovement, movement.Id.ToString(),
                target, identification, cancellationToken).ConfigureAwait(false);
    }

    // The same exit already recorded the same day (product, quantity, destination, beneficiary, project, vehicles): added only with a reason
    // (then true is returned and the journal records the confirmed exit).
    private static async Task<bool> CheckDuplicateExitAsync(MySqlConnection connection, MySqlTransaction transaction, int productId, string productName,
        StockMovementInput value, CancellationToken token)
    {
        await using var command = Command(connection, transaction, """
            SELECT COUNT(*) FROM stock_movements
            WHERE product_id=@product AND kind=0 AND voided_utc IS NULL AND quantity=@quantity AND movement_date=@date AND destination<=>@destination
              AND beneficiary_id<=>@beneficiary AND project_id<=>@project AND vehicle_id<=>@vehicle AND source_vehicle_id<=>@source
            """, ("@product", productId), ("@quantity", value.Quantity), ("@date", StockMovementRules.StorageDate(value.Date!.Value)),
            ("@destination", (int?)value.Destination), ("@beneficiary", value.BeneficiaryId), ("@project", value.ProjectId),
            ("@vehicle", value.VehicleId), ("@source", value.SourceVehicleId));
        if (Convert.ToInt32(await command.ExecuteScalarAsync(token).ConfigureAwait(false)) == 0) return false;
        if (value.DuplicateReason.Length > 0) return true;
        throw new DuplicateExitWarningException(StockMovementRules.DuplicateExitMessage(productName, value.Quantity!.Value,
            value.Destination is { } destination ? StockMovementRules.DestinationLabel(destination) : "fără destinație", value.Date!.Value));
    }

    // An entry tied to an invoice: the same product already taken from it, or more than the quantity written on its line, is refused unless
    // the user gave a reason (then true is returned and the journal records the confirmed entry).
    private static async Task<bool> CheckInvoiceEntryAsync(MySqlConnection connection, MySqlTransaction transaction, int invoiceId,
        int productId, string productName, StockMovementInput value, CancellationToken token)
    {
        int existing; DateOnly? existingDate = null;
        await using (var taken = Command(connection, transaction,
            "SELECT COALESCE(SUM(quantity),0),MAX(movement_date) FROM stock_movements WHERE invoice_id=@invoice AND product_id=@product AND kind=1",
            ("@invoice", invoiceId), ("@product", productId)))
        await using (var reader = await taken.ExecuteReaderAsync(token).ConfigureAwait(false))
        {
            await reader.ReadAsync(token).ConfigureAwait(false);
            existing = Convert.ToInt32(reader.GetValue(0));
            if (!reader.IsDBNull(1)) existingDate = StockMovementRules.ParseStorageDate(reader.GetString(1));
        }
        int? invoiceQuantity = value.InvoiceQuantity;
        if (invoiceQuantity is null)
            await using (var stored = Command(connection, transaction, "SELECT quantity FROM supplier_invoice_lines WHERE invoice_id=@invoice AND product_id=@product",
                ("@invoice", invoiceId), ("@product", productId)))
                if (await stored.ExecuteScalarAsync(token).ConfigureAwait(false) is { } found and not DBNull) invoiceQuantity = Convert.ToInt32(found);
        var total = existing + value.Quantity!.Value;
        var exceeds = invoiceQuantity is { } known && total > known;
        if (existing == 0 && !exceeds) return false;
        if (value.DuplicateReason.Length > 0) return true;
        throw existing > 0
            ? new InvoiceEntryWarningException(StockMovementRules.RepeatedOnInvoiceMessage(productName, existing, existingDate ?? StockMovementRules.Today), exceeds, existing, existingDate)
            : new InvoiceEntryWarningException(StockMovementRules.ExceedsInvoiceMessage(productName, invoiceQuantity!.Value, total), true, existing, existingDate);
    }

    public async Task<StockMovementResult> UpdateAsync(StockMovement original, StockMovementInput input, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        var value = StockMovementRules.Validated(input, original.Kind, true, allowVehicleTransfer: original.IsVehicleTransfer);
        var actor = await RepositoryAudit.ActorAsync(accessControl, cancellationToken).ConfigureAwait(false);
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
                SourceVehicleId = value.SourceVehicleId, SourceVehiclePlate = sourcePlate,
                Reference = current.Kind == StockMovementKind.Exit ? value.Reference : current.Reference,
                OverStockCause = current.Kind == StockMovementKind.Exit ? value.OverStockCause : current.OverStockCause
            };
            var usesVehicleStock = current.Kind == StockMovementKind.Exit && current.Destination is not (ExitDestination.Vehicle or ExitDestination.WarehouseReturn);
            var snapshot = await VehicleSnapshotAsync(connection, transaction, current.ProductId, cancellationToken).ConfigureAwait(false);
            if (!StockMovementRules.HasChanges(current, updated))
                throw new StockMovementOperationException("Nu ai modificat nicio valoare a mișcării.");
            var correction = updated.Effect - current.Effect;
            await using (var update = Command(connection, transaction, """
                UPDATE stock_movements SET quantity=@quantity,movement_date=@date,description=@description,
                    beneficiary_id=@beneficiary,project_id=@project,version=@version,updated_utc=@updated,
                    destination=@destination,vehicle_id=@vehicle,source_vehicle_id=@sourceVehicle,reference=@reference,over_stock_cause=@cause
                WHERE id=@id AND version=@oldVersion
                """, ("@quantity", updated.Quantity), ("@date", StockMovementRules.StorageDate(updated.Date)),
                ("@description", updated.Description), ("@beneficiary", updated.BeneficiaryId), ("@project", updated.ProjectId),
                ("@version", updated.Version), ("@updated", nowText), ("@id", current.Id), ("@oldVersion", current.Version),
                ("@destination", (int?)updated.Destination), ("@vehicle", updated.VehicleId), ("@sourceVehicle", updated.SourceVehicleId),
                ("@reference", updated.Reference), ("@cause", (int?)updated.OverStockCause)))
                if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                    throw new StockMovementOperationException(StockMovementRules.StaleMessage);
            await using (var history = Command(connection, transaction, """
                INSERT INTO stock_movement_history(movement_id,actor,timestamp_utc,changes,stock_correction,reason)
                VALUES(@movement,@actor,@timestamp,@changes,@correction,@reason)
                """, ("@movement", current.Id), ("@actor", actor.Username), ("@timestamp", nowText),
                ("@changes", StockMovementRules.HistorySummary(current, updated, correction)), ("@correction", correction),
                ("@reason", value.Reason)))
                await history.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            if (!usesVehicleStock)
                await EnsureVehicleStocksNotWorseAsync(connection, transaction, current.ProductId, snapshot, cancellationToken).ConfigureAwait(false);
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
                await LockProductAsync(connection, transaction, current!.ProductId, token, requireComplete: false).ConfigureAwait(false);
                var snapshot =await VehicleSnapshotAsync(connection, transaction, current.ProductId, token).ConfigureAwait(false);
                await MariaArchivePersistence.InsertAsync(connection, transaction, operation, [], token).ConfigureAwait(false);
                await using (var deleteHistory = Command(connection, transaction,
                    "DELETE FROM stock_movement_history WHERE movement_id=@id", ("@id", original.Id)))
                    await deleteHistory.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                await using (var delete = Command(connection, transaction,
                    "DELETE FROM stock_movements WHERE id=@id AND version=@version", ("@id", original.Id), ("@version", original.Version)))
                    if (await delete.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
                        throw new StockMovementOperationException(StockMovementRules.StaleMessage);
                if (current.Kind != StockMovementKind.Exit || current.Destination is ExitDestination.Vehicle or ExitDestination.WarehouseReturn)
                    await EnsureVehicleStocksNotWorseAsync(connection, transaction, current.ProductId, snapshot, token).ConfigureAwait(false);
                var newStock = await ApplyStockAsync(connection, transaction, current.ProductId, -current.Effect, token).ConfigureAwait(false);
                await MariaArchivePersistence.InsertAuditAsync(connection, transaction, operation, token).ConfigureAwait(false);
                return newStock;
            }, token).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
        return stock;
    }

    public async Task<IReadOnlyList<FreeEntry>> GetFreeEntriesAsync(FreeEntryQuery query, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        object cutoff = query.OlderThanDays is { } days ? StockMovementRules.StorageDate(StockMovementRules.Today.AddDays(-days)) : DBNull.Value;
        await using var command = Command(connection, null, """
            SELECT m.id,m.product_id,p.name,m.quantity,m.movement_date,m.free_entry_type,m.free_supplier_id,fs.name,m.reference,m.description,m.operator
            FROM stock_movements m INNER JOIN products p ON p.id=m.product_id LEFT JOIN suppliers fs ON fs.id=m.free_supplier_id
            WHERE m.kind=1 AND m.invoice_id IS NULL
              AND ((@unspecified=1 AND m.free_entry_type IS NULL) OR (@unspecified=0 AND m.free_entry_type IS NOT NULL AND (@type IS NULL OR m.free_entry_type=@type)))
              AND (@supplier IS NULL OR m.free_supplier_id=@supplier) AND (@cutoff IS NULL OR m.movement_date<=@cutoff)
            ORDER BY m.movement_date,m.id
            """, ("@unspecified", query.Unspecified ? 1 : 0), ("@type", query.Type is { } type ? (int)type : null), ("@supplier", query.SupplierId), ("@cutoff", cutoff));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<FreeEntry>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new(ToInt32(reader.GetInt64(0)), ToInt32(reader.GetInt64(1)), reader.GetString(2), ToInt32(reader.GetInt64(3)),
                StockMovementRules.ParseStorageDate(reader.GetString(4)), reader.IsDBNull(5) ? null : (FreeEntryType)ToInt32(reader.GetInt64(5)),
                reader.IsDBNull(6) ? null : ToInt32(reader.GetInt64(6)), reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8), reader.GetString(9), reader.GetString(10)));
        return result;
    }

    public async Task<IReadOnlyList<StockMovement>> AttachToInvoiceAsync(IReadOnlyList<StockMovement> entries, int invoiceId, string reason,
        bool viaPickup = false, CancellationToken cancellationToken = default)
    {
        if (viaPickup) await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        else if (accessControl is not null) await accessControl.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        if (entries.Count == 0) throw new StockMovementOperationException(StockMovementRules.NothingToAttachMessage);
        var motif = ChangeReasonRules.Normalize(reason);
        if (ChangeReasonRules.ValidationError(motif) is { } reasonError) throw new StockMovementOperationException(reasonError);
        var actor = await RepositoryAudit.ActorAsync(accessControl, cancellationToken).ConfigureAwait(false);
        var now = DateTime.UtcNow;
        var nowText = MariaTimeText.Format(now);
        var done = await WriteAsync(async (connection, transaction) =>
        {
            var (invoiceNumber, supplierName, supplierId) = await ResolveInvoiceAsync(connection, transaction, invoiceId, cancellationToken).ConfigureAwait(false);
            var result = new List<(StockMovement Before, StockMovement After, string Product)>();
            foreach (var original in entries.OrderBy(entry => entry.Id))
            {
                var current = await GetMovementAsync(connection, transaction, original.Id, true, cancellationToken).ConfigureAwait(false);
                StockMovementRules.CheckCurrent(current, original);
                if (current!.Kind != StockMovementKind.Entry || current.InvoiceId is not null) throw new StockMovementOperationException(StockMovementRules.AlreadyOnInvoiceMessage);
                var product = await GetProductCodeAsync(connection, transaction, current.ProductId, cancellationToken).ConfigureAwait(false) ?? string.Empty;
                var after = current with
                {
                    InvoiceId = invoiceId, InvoiceNumber = invoiceNumber, SupplierName = supplierName, SupplierId = supplierId,
                    FreeType = null, FreeSupplierId = null, FreeSupplierName = null, Version = current.Version + 1, UpdatedUtc = now, Modified = true
                };
                await using (var update = Command(connection, transaction, """
                    UPDATE stock_movements SET invoice_id=@invoice,free_entry_type=NULL,free_supplier_id=NULL,version=@version,updated_utc=@updated
                    WHERE id=@id AND version=@oldVersion
                    """, ("@invoice", invoiceId), ("@version", after.Version), ("@updated", nowText), ("@id", current.Id), ("@oldVersion", current.Version)))
                    if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                        throw new StockMovementOperationException(StockMovementRules.StaleMessage);
                await InsertHistoryAsync(connection, transaction, current.Id, actor.Username, nowText,
                    AuditDetails.Changes(new AuditChange("Factură", "—", $"{invoiceNumber} · {supplierName}")), motif, cancellationToken).ConfigureAwait(false);
                result.Add((current, after, product));
            }
            return result;
        }, cancellationToken).ConfigureAwait(false);
        foreach (var (before, after, product) in done)
            await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.StockMovement, AuditActions.AttachEntryToInvoice,
                after.Id.ToString(), StockMovementRules.Target(product),
                AuditDetails.Identification((ProductCode.Label, product), ("Cantitate", after.Quantity.ToString()), ("Data", StockMovementRules.DisplayDate(after.Date)),
                    ("Tip intrare", before.FreeType is { } type ? StockMovementRules.FreeTypeLabel(type) : "nespecificat"),
                    ("Furnizor intrare", before.FreeSupplierName ?? "—"), ("Factură", $"{after.InvoiceNumber} · {after.SupplierName}")),
                motif, cancellationToken).ConfigureAwait(false);
        return done.Select(item => item.After).ToList();
    }

    public async Task<StockMovement> DetachFromInvoiceAsync(StockMovement entry, string reason, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var motif = ChangeReasonRules.Normalize(reason);
        if (ChangeReasonRules.ValidationError(motif) is { } reasonError) throw new StockMovementOperationException(reasonError);
        var actor = await RepositoryAudit.ActorAsync(accessControl, cancellationToken).ConfigureAwait(false);
        var now = DateTime.UtcNow;
        var nowText = MariaTimeText.Format(now);
        var (before, after, product) = await WriteAsync(async (connection, transaction) =>
        {
            var current = await GetMovementAsync(connection, transaction, entry.Id, true, cancellationToken).ConfigureAwait(false);
            StockMovementRules.CheckCurrent(current, entry);
            if (current!.InvoiceId is null) throw new StockMovementOperationException(StockMovementRules.NotOnInvoiceMessage);
            var product = await GetProductCodeAsync(connection, transaction, current.ProductId, cancellationToken).ConfigureAwait(false) ?? string.Empty;
            var after = current with { InvoiceId = null, InvoiceNumber = null, SupplierName = null, SupplierId = null, Version = current.Version + 1, UpdatedUtc = now, Modified = true };
            await using (var update = Command(connection, transaction,
                "UPDATE stock_movements SET invoice_id=NULL,version=@version,updated_utc=@updated WHERE id=@id AND version=@oldVersion",
                ("@version", after.Version), ("@updated", nowText), ("@id", current.Id), ("@oldVersion", current.Version)))
                if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                    throw new StockMovementOperationException(StockMovementRules.StaleMessage);
            await InsertHistoryAsync(connection, transaction, current.Id, actor.Username, nowText,
                AuditDetails.Changes(new AuditChange("Factură", $"{current.InvoiceNumber} · {current.SupplierName}", "—")), motif, cancellationToken).ConfigureAwait(false);
            return (current, after, product);
        }, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.StockMovement, AuditActions.DetachEntryFromInvoice,
            after.Id.ToString(), StockMovementRules.Target(product),
            AuditDetails.Identification((ProductCode.Label, product), ("Cantitate", after.Quantity.ToString()), ("Data", StockMovementRules.DisplayDate(after.Date)),
                ("Factură", $"{before.InvoiceNumber} · {before.SupplierName}")), motif, cancellationToken).ConfigureAwait(false);
        return after;
    }

    private static async Task InsertHistoryAsync(MySqlConnection connection, MySqlTransaction transaction, int movementId, string actor,
        string timestamp, string changes, string reason, CancellationToken token)
    {
        await using var history = Command(connection, transaction, """
            INSERT INTO stock_movement_history(movement_id,actor,timestamp_utc,changes,stock_correction,reason)
            VALUES(@movement,@actor,@timestamp,@changes,0,@reason)
            """, ("@movement", movementId), ("@actor", actor), ("@timestamp", timestamp), ("@changes", $"{changes}; Corecție stoc: 0"), ("@reason", reason));
        await history.ExecuteNonQueryAsync(token).ConfigureAwait(false);
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

    private Task<T> WriteAsync<T>(Func<MySqlConnection, MySqlTransaction, Task<T>> action, CancellationToken token) =>
        MariaTransactions.RetryOnDeadlockAsync(() => WriteOnceAsync(action, token), token);

    private async Task<T> WriteOnceAsync<T>(Func<MySqlConnection, MySqlTransaction, Task<T>> action, CancellationToken token)
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
    private static async Task<string> LockProductAsync(MySqlConnection connection, MySqlTransaction transaction, int productId, CancellationToken token,
        bool requireComplete = true)
    {
        await using var command = Command(connection, transaction, "SELECT name FROM products WHERE id=@id FOR UPDATE", ("@id", productId));
        var name = await command.ExecuteScalarAsync(token).ConfigureAwait(false) as string
                   ?? throw new StockMovementOperationException(ProductMissingMessage);
        if (requireComplete) await EnsureParametersCompleteAsync(connection, transaction, productId, name, token).ConfigureAwait(false);
        return name;
    }

    // A product of a subcategory with required parameters that misses a value is blocked until it is completed (migration 37).
    private static async Task EnsureParametersCompleteAsync(MySqlConnection connection, MySqlTransaction transaction, int productId, string name, CancellationToken token)
    {
        await using var command = Command(connection, transaction, """
            SELECT sp.name FROM products p INNER JOIN subcategory_parameters sp ON sp.subcategory_id=p.subcategory_id
            WHERE p.id=@id AND NOT EXISTS(SELECT 1 FROM product_parameter_values pv WHERE pv.product_id=p.id AND pv.parameter_id=sp.id)
            ORDER BY sp.position,sp.id
            """, ("@id", productId));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        var missing = new List<string>();
        while (await reader.ReadAsync(token).ConfigureAwait(false)) missing.Add(reader.GetString(0));
        if (missing.Count > 0) throw new StockMovementOperationException(ProductParameterRules.MissingMessage(name, missing));
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

    // The invoice an entry is taken from, read under the transaction (so it cannot disappear before the entry is written).
    private static async Task<(string? Number, string? SupplierName, int? SupplierId)> ResolveInvoiceAsync(MySqlConnection connection,
        MySqlTransaction transaction, int? invoiceId, CancellationToken token)
    {
        if (invoiceId is not { } id) return (null, null, null);
        await using var command = Command(connection, transaction, """
            SELECT si.`number`,su.name,si.supplier_id FROM supplier_invoices si INNER JOIN suppliers su ON su.id=si.supplier_id WHERE si.id=@id
            """, ("@id", id));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
            throw new StockMovementOperationException("Factura aleasă nu mai există. Actualizează pagina și reia operația.");
        return (reader.GetString(0), reader.GetString(1), ToInt32(reader.GetInt64(2)));
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
                WHERE kind=0 AND voided_utc IS NULL AND destination=2 AND vehicle_id IS NOT NULL AND (@product IS NULL OR product_id=@product)
                UNION ALL
                SELECT product_id,source_vehicle_id,-quantity FROM stock_movements
                WHERE kind=0 AND voided_utc IS NULL AND source_vehicle_id IS NOT NULL AND (@product IS NULL OR product_id=@product)) t
            GROUP BY product_id,vid
            """, ("@product", productId));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        var result = new List<(int, int, int)>();
        while (await reader.ReadAsync(token).ConfigureAwait(false))
            result.Add((ToInt32(reader.GetInt64(0)), ToInt32(reader.GetInt64(1)), Convert.ToInt32(reader.GetValue(2))));
        return result;
    }

    private static async Task<Dictionary<int, int>> VehicleSnapshotAsync(MySqlConnection connection, MySqlTransaction transaction,
        int productId, CancellationToken token) =>
        (await VehicleQuantitiesAsync(connection, transaction, productId, token).ConfigureAwait(false)).ToDictionary(entry => entry.VehicleId, entry => entry.Quantity);

    // After a change that moves or removes pieces: no vehicle may be left with less than before when it is below zero (pieces already used or
    // moved on). A vehicle that was already negative because something was used from it above what it held stays as it is.
    private static async Task EnsureVehicleStocksNotWorseAsync(MySqlConnection connection, MySqlTransaction transaction,
        int productId, IReadOnlyDictionary<int, int> before, CancellationToken token)
    {
        foreach (var entry in await VehicleQuantitiesAsync(connection, transaction, productId, token).ConfigureAwait(false))
        {
            if (entry.Quantity >= 0 || entry.Quantity >= before.GetValueOrDefault(entry.VehicleId)) continue;
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
            reader.IsDBNull(18) ? null : ToInt32(reader.GetInt64(18)), reader.IsDBNull(19) ? null : reader.GetString(19),
            reader.IsDBNull(20) ? null : ToInt32(reader.GetInt64(20)), reader.IsDBNull(21) ? null : reader.GetString(21),
            reader.IsDBNull(22) ? null : reader.GetString(22), reader.IsDBNull(23) ? null : ToInt32(reader.GetInt64(23)),
            reader.IsDBNull(24) ? null : (FreeEntryType)ToInt32(reader.GetInt64(24)), reader.IsDBNull(25) ? null : ToInt32(reader.GetInt64(25)),
            reader.IsDBNull(26) ? null : reader.GetString(26), reader.IsDBNull(27) ? null : reader.GetString(27),
            reader.IsDBNull(28) ? null : (OverStockCause)ToInt32(reader.GetInt64(28)),
            reader.IsDBNull(29) ? null : ToInt32(reader.GetInt64(29)),
            reader.IsDBNull(30) ? null : reader.GetString(30), reader.IsDBNull(31) ? null : reader.GetString(31), reader.IsDBNull(32) ? null : reader.GetString(32),
            reader.IsDBNull(33) ? null : ToInt32(reader.GetInt64(33)));
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
