using System.Data;
using Microsoft.Data.Sqlite;

namespace BlazorStoc.Services;

// Local persistent store (demo mode): stock movements, the atomic stock update, history, audit and archiving.
public sealed class SqliteStockMovementRepository(SqliteLocalStore store, IAccessControl? accessControl = null,
    IArchiveService? archiveService = null) : IStockMovementRepository
{
    private const string ProductMissingMessage = "Produsul nu mai există. Actualizează catalogul.";
    private readonly IArchiveService archiver = archiveService ?? new ArchiveService(accessControl);

    private const string SelectMovement = """
        SELECT m.id,m.product_id,m.kind,m.quantity,m.movement_date,m.description,m.beneficiary_id,b.name,m.project_id,p.name,
               m.operator,m.version,m.created_utc,m.updated_utc,
               EXISTS(SELECT 1 FROM stock_movement_history h WHERE h.movement_id=m.id)
        FROM stock_movements m
        LEFT JOIN beneficiaries b ON b.id=m.beneficiary_id
        LEFT JOIN projects p ON p.id=m.project_id
        """;

    public async Task<StockMovementPage> GetPageAsync(int productId, StockMovementQuery query, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await store.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var stock = await GetStockAsync(connection, null, productId, cancellationToken).ConfigureAwait(false)
                    ?? throw new StockMovementOperationException(ProductMissingMessage);
        object? kind = query.Kind is null ? null : (int)query.Kind.Value;
        int total;
        await using (var count = SqliteLocalStore.Command(connection, null,
            "SELECT COUNT(*) FROM stock_movements WHERE product_id=@product AND (@kind IS NULL OR kind=@kind)",
            ("@product", productId), ("@kind", kind)))
            total = Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        bool anyModified;
        await using (var modified = SqliteLocalStore.Command(connection, null, """
            SELECT EXISTS(SELECT 1 FROM stock_movement_history h INNER JOIN stock_movements m ON m.id=h.movement_id
                          WHERE m.product_id=@product)
            """, ("@product", productId)))
            anyModified = Convert.ToBoolean(await modified.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        var direction = query.Descending ? "DESC" : "ASC";
        var pageSize = query.PageSize <= 0 ? -1 : query.PageSize;
        var offset = query.PageSize <= 0 ? 0 : (Math.Max(1, query.Page) - 1) * query.PageSize;
        await using var command = SqliteLocalStore.Command(connection, null, $"""
            {SelectMovement}
            WHERE m.product_id=@product AND (@kind IS NULL OR m.kind=@kind)
            ORDER BY m.movement_date {direction}, m.id {direction}
            LIMIT @limit OFFSET @offset
            """, ("@product", productId), ("@kind", kind), ("@limit", pageSize), ("@offset", offset));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var items = new List<StockMovement>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) items.Add(ReadMovement(reader));
        return new StockMovementPage(items, total, stock, anyModified);
    }

    public async Task<StockMovement?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await store.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await GetMovementAsync(connection, null, id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<StockMovementHistoryEntry>> GetHistoryAsync(int movementId, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await store.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await GetHistoryAsync(connection, null, movementId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ProjectStockMovement>> GetForProjectAsync(int projectId, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await store.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = SqliteLocalStore.Command(connection, null, """
            SELECT m.id,m.product_id,p.name,m.quantity,m.movement_date,m.operator
            FROM stock_movements m INNER JOIN products p ON p.id=m.product_id
            WHERE m.project_id=@project AND m.kind=0
            ORDER BY m.movement_date DESC,m.id DESC
            """, ("@project", projectId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<ProjectStockMovement>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new(reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2), reader.GetInt32(3),
                StockMovementRules.ParseStorageDate(reader.GetString(4)), reader.GetString(5)));
        return result;
    }

    public async Task<StockMovementResult> CreateAsync(int productId, StockMovementInput input, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        var value = StockMovementRules.Validated(input, input.Kind, false);
        var actor = await SqliteRepositoryAudit.ActorAsync(accessControl, cancellationToken).ConfigureAwait(false);
        var now = DateTime.UtcNow;
        await using var connection = await store.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        try
        {
            var productCode = await GetProductCodeAsync(connection, transaction, productId, cancellationToken).ConfigureAwait(false)
                              ?? throw new StockMovementOperationException(ProductMissingMessage);
            var (beneficiaryName, projectName) = await ResolveRelationsAsync(connection, transaction, value, cancellationToken).ConfigureAwait(false);
            await using var insert = SqliteLocalStore.Command(connection, transaction, """
                INSERT INTO stock_movements
                    (product_id,beneficiary_id,project_id,quantity,created_utc,kind,movement_date,description,operator,version,updated_utc)
                VALUES(@product,@beneficiary,@project,@quantity,@created,@kind,@date,@description,@operator,0,@created);
                SELECT last_insert_rowid();
                """, ("@product", productId), ("@beneficiary", value.BeneficiaryId), ("@project", value.ProjectId),
                ("@quantity", value.Quantity), ("@created", now.ToString("O")), ("@kind", (int)value.Kind),
                ("@date", StockMovementRules.StorageDate(value.Date!.Value)), ("@description", value.Description),
                ("@operator", actor.Username));
            var id = checked((int)(long)(await insert.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!);
            var movement = new StockMovement(id, productId, value.Kind, value.Quantity!.Value, value.Date.Value, value.Description,
                value.BeneficiaryId, beneficiaryName, value.ProjectId, projectName, actor.Username, 0, now, now);
            var stock = await ApplyStockAsync(connection, transaction, productId, movement.Effect, cancellationToken).ConfigureAwait(false);
            await SqliteLocalStore.InsertAuditAsync(connection, transaction, new(actor.Username, actor.Role,
                AuditEntities.StockMovement, AuditActions.Create, StockMovementRules.Target(productCode),
                StockMovementRules.AuditIdentification(movement, productCode), string.Empty, id.ToString()), cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new StockMovementResult(movement, stock);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<StockMovementResult> UpdateAsync(StockMovement original, StockMovementInput input, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        var value = StockMovementRules.Validated(input, original.Kind, true);
        var actor = await SqliteRepositoryAudit.ActorAsync(accessControl, cancellationToken).ConfigureAwait(false);
        var now = DateTime.UtcNow;
        await using var connection = await store.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        try
        {
            var current = await GetMovementAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false);
            StockMovementRules.CheckCurrent(current, original);
            var productCode = await GetProductCodeAsync(connection, transaction, current!.ProductId, cancellationToken).ConfigureAwait(false)
                              ?? throw new StockMovementOperationException(ProductMissingMessage);
            var (beneficiaryName, projectName) = await ResolveRelationsAsync(connection, transaction, value, cancellationToken).ConfigureAwait(false);
            var updated = current with
            {
                Quantity = value.Quantity!.Value, Date = value.Date!.Value, Description = value.Description,
                BeneficiaryId = value.BeneficiaryId, BeneficiaryName = beneficiaryName, ProjectId = value.ProjectId,
                ProjectName = projectName, Version = current.Version + 1, UpdatedUtc = now, Modified = true
            };
            if (!StockMovementRules.HasChanges(current, updated))
                throw new StockMovementOperationException("Nu ai modificat nicio valoare a mișcării.");
            var correction = updated.Effect - current.Effect;
            await using (var update = SqliteLocalStore.Command(connection, transaction, """
                UPDATE stock_movements SET quantity=@quantity,movement_date=@date,description=@description,
                    beneficiary_id=@beneficiary,project_id=@project,version=@version,updated_utc=@updated
                WHERE id=@id AND version=@oldVersion
                """, ("@quantity", updated.Quantity), ("@date", StockMovementRules.StorageDate(updated.Date)),
                ("@description", updated.Description), ("@beneficiary", updated.BeneficiaryId), ("@project", updated.ProjectId),
                ("@version", updated.Version), ("@updated", now.ToString("O")), ("@id", current.Id), ("@oldVersion", current.Version)))
                if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                    throw new StockMovementOperationException(StockMovementRules.StaleMessage);
            await using (var history = SqliteLocalStore.Command(connection, transaction, """
                INSERT INTO stock_movement_history(movement_id,actor,timestamp_utc,changes,stock_correction,reason)
                VALUES(@movement,@actor,@timestamp,@changes,@correction,@reason)
                """, ("@movement", current.Id), ("@actor", actor.Username), ("@timestamp", now.ToString("O")),
                ("@changes", StockMovementRules.HistorySummary(current, updated, correction)), ("@correction", correction),
                ("@reason", value.Reason)))
                await history.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            var stock = await ApplyStockAsync(connection, transaction, current.ProductId, correction, cancellationToken).ConfigureAwait(false);
            await SqliteLocalStore.InsertAuditAsync(connection, transaction, new(actor.Username, actor.Role,
                AuditEntities.StockMovement, AuditActions.Edit, StockMovementRules.Target(productCode),
                AuditDetails.Changes(StockMovementRules.AuditChanges(current, updated)), value.Reason, current.Id.ToString()),
                cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new StockMovementResult(updated, stock);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<int> DeleteAsync(StockMovement original, string reason, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        var motif = ChangeReasonRules.Normalize(reason);
        if (ChangeReasonRules.ValidationError(motif) is { } reasonError) throw new StockMovementOperationException(reasonError);
        string productCode;
        IReadOnlyList<StockMovementHistoryEntry> history;
        await using (var lookup = await store.OpenConnectionAsync(cancellationToken).ConfigureAwait(false))
        {
            productCode = await GetProductCodeAsync(lookup, null, original.ProductId, cancellationToken).ConfigureAwait(false)
                          ?? throw new StockMovementOperationException(ProductMissingMessage);
            history = await GetHistoryAsync(lookup, null, original.Id, cancellationToken).ConfigureAwait(false);
        }
        var stock = 0;
        await archiver.ExecuteAsync(ArchiveRequests.StockMovement(original, productCode, history, motif), async (operation, token) =>
        {
            await using var connection = await store.OpenConnectionAsync(token).ConfigureAwait(false);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, token).ConfigureAwait(false);
            try
            {
                var current = await GetMovementAsync(connection, transaction, original.Id, token).ConfigureAwait(false);
                StockMovementRules.CheckCurrent(current, original);
                await SqliteArchivePersistence.InsertAsync(connection, transaction, operation, [], token).ConfigureAwait(false);
                await using (var deleteHistory = SqliteLocalStore.Command(connection, transaction,
                    "DELETE FROM stock_movement_history WHERE movement_id=@id", ("@id", original.Id)))
                    await deleteHistory.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                await using (var delete = SqliteLocalStore.Command(connection, transaction,
                    "DELETE FROM stock_movements WHERE id=@id AND version=@version", ("@id", original.Id), ("@version", original.Version)))
                    if (await delete.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
                        throw new StockMovementOperationException(StockMovementRules.StaleMessage);
                stock = await ApplyStockAsync(connection, transaction, current!.ProductId, -current.Effect, token).ConfigureAwait(false);
                await SqliteLocalStore.InsertAuditAsync(connection, transaction, new(operation.ActorUsername, operation.ActorRole,
                    AuditEntities.StockMovement, AuditActions.Delete, operation.Request.Target, operation.Request.Details,
                    operation.Request.Motif, original.Id.ToString(), operation.Id), token).ConfigureAwait(false);
                await transaction.CommitAsync(token).ConfigureAwait(false);
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }, cancellationToken).ConfigureAwait(false);
        return stock;
    }

    // Atomic increment: concurrent movements for the same product cannot overwrite each other's stock change.
    private static async Task<int> ApplyStockAsync(SqliteConnection connection, SqliteTransaction transaction, int productId,
        int delta, CancellationToken token)
    {
        if (delta != 0)
            await using (var update = SqliteLocalStore.Command(connection, transaction,
                "UPDATE products SET quantity=quantity+@delta WHERE id=@id", ("@delta", delta), ("@id", productId)))
                if (await update.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
                    throw new StockMovementOperationException(ProductMissingMessage);
        return await GetStockAsync(connection, transaction, productId, token).ConfigureAwait(false)
               ?? throw new StockMovementOperationException(ProductMissingMessage);
    }

    private static async Task<int?> GetStockAsync(SqliteConnection connection, SqliteTransaction? transaction, int productId, CancellationToken token)
    {
        await using var command = SqliteLocalStore.Command(connection, transaction,
            "SELECT quantity FROM products WHERE id=@id", ("@id", productId));
        var value = await command.ExecuteScalarAsync(token).ConfigureAwait(false);
        return value is null or DBNull ? null : Convert.ToInt32(value);
    }

    private static async Task<string?> GetProductCodeAsync(SqliteConnection connection, SqliteTransaction? transaction, int productId, CancellationToken token)
    {
        await using var command = SqliteLocalStore.Command(connection, transaction,
            "SELECT name FROM products WHERE id=@id", ("@id", productId));
        return await command.ExecuteScalarAsync(token).ConfigureAwait(false) as string;
    }

    private static async Task<(string? BeneficiaryName, string? ProjectName)> ResolveRelationsAsync(SqliteConnection connection,
        SqliteTransaction transaction, StockMovementInput value, CancellationToken token)
    {
        string? beneficiaryName = null, projectName = null;
        if (value.BeneficiaryId is { } beneficiaryId)
        {
            await using var command = SqliteLocalStore.Command(connection, transaction,
                "SELECT name FROM beneficiaries WHERE id=@id", ("@id", beneficiaryId));
            beneficiaryName = await command.ExecuteScalarAsync(token).ConfigureAwait(false) as string
                              ?? throw new StockMovementOperationException("Beneficiarul selectat nu mai există. Actualizează lista și reia operația.");
        }
        if (value.ProjectId is { } projectId)
        {
            await using var command = SqliteLocalStore.Command(connection, transaction,
                "SELECT name,beneficiary_id FROM projects WHERE id=@id", ("@id", projectId));
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false))
                throw new StockMovementOperationException("Proiectul selectat nu mai există. Actualizează lista și reia operația.");
            if (reader.GetInt32(1) != value.BeneficiaryId)
                throw new StockMovementOperationException("Proiectul selectat nu aparține beneficiarului ales.");
            projectName = reader.GetString(0);
        }
        return (beneficiaryName, projectName);
    }

    private static async Task<StockMovement?> GetMovementAsync(SqliteConnection connection, SqliteTransaction? transaction, int id, CancellationToken token)
    {
        await using var command = SqliteLocalStore.Command(connection, transaction, $"{SelectMovement} WHERE m.id=@id", ("@id", id));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        return await reader.ReadAsync(token).ConfigureAwait(false) ? ReadMovement(reader) : null;
    }

    private static async Task<IReadOnlyList<StockMovementHistoryEntry>> GetHistoryAsync(SqliteConnection connection,
        SqliteTransaction? transaction, int movementId, CancellationToken token)
    {
        await using var command = SqliteLocalStore.Command(connection, transaction, """
            SELECT id,movement_id,actor,timestamp_utc,changes,stock_correction,reason
            FROM stock_movement_history WHERE movement_id=@id ORDER BY id
            """, ("@id", movementId));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        var result = new List<StockMovementHistoryEntry>();
        while (await reader.ReadAsync(token).ConfigureAwait(false))
            result.Add(new(reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2), SqliteProjectRepository.ReadUtc(reader, 3),
                reader.GetString(4), reader.GetInt32(5), reader.GetString(6)));
        return result;
    }

    private static StockMovement ReadMovement(SqliteDataReader reader)
    {
        var created = SqliteProjectRepository.ReadUtc(reader, 12);
        var stored = reader.GetString(4);
        var date = stored.Length == 0 ? DateOnly.FromDateTime(created) : StockMovementRules.ParseStorageDate(stored);
        return new StockMovement(reader.GetInt32(0), reader.GetInt32(1), (StockMovementKind)reader.GetInt32(2), reader.GetInt32(3),
            date, reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetInt32(6), reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetInt32(8), reader.IsDBNull(9) ? null : reader.GetString(9),
            reader.GetString(10), reader.GetInt64(11), created, ReadUpdated(reader, created), reader.GetBoolean(14));
    }

    private static DateTime ReadUpdated(SqliteDataReader reader, DateTime fallback) =>
        reader.GetString(13).Length == 0 ? fallback : SqliteProjectRepository.ReadUtc(reader, 13);

    private Task EnsureOperatorAsync(CancellationToken token) =>
        accessControl?.EnsureProductOperatorAsync(token) ?? Task.CompletedTask;
}
