using MySqlConnector;

namespace BlazorStoc.Services;

// MariaDB mode: `product_min_stock` and `project_deadlines` (migration 29). Changes need a product operator and are journaled under the product / the project.
internal static class StockAlertSql
{
    public static MySqlCommand Command(MySqlConnection connection, string sql, params (string Name, object? Value)[] parameters) =>
        MariaDb.Command(connection, sql, parameters);

    public static async Task<string?> TextAsync(MySqlConnection connection, string sql, int id, CancellationToken token) =>
        await Command(connection, sql, ("@id", id)).ExecuteScalarAsync(token).ConfigureAwait(false) as string;

    public static async Task<string?> ValueAsync(MySqlConnection connection, string sql, int id, CancellationToken token) =>
        (await Command(connection, sql, ("@id", id)).ExecuteScalarAsync(token).ConfigureAwait(false)) is { } value and not DBNull ? Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) : null;

    public static string Today => StockMovementRules.StorageDate(StockMovementRules.Today);
    public static DateOnly LocalDate(string utcText) => DateOnly.FromDateTime(MariaTimeText.Parse(utcText).ToLocalTime());
}

public sealed class MariaProductMinStockRepository(IConfiguration configuration, IAccessControl? accessControl = null, IAuditTrail? auditTrail = null) : IProductMinStockRepository
{
    private Task EnsureOperatorAsync(CancellationToken token) => accessControl?.EnsureAsync("stoc.minim", token) ?? Task.CompletedTask;
    private Task EnsureViewAsync(CancellationToken token) => accessControl?.EnsureAsync("stoc.view", token) ?? Task.CompletedTask;

    public async Task<int?> GetAsync(int productId, CancellationToken cancellationToken = default)
    {
        await EnsureViewAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        var value = await StockAlertSql.ValueAsync(connection, "SELECT min_quantity FROM product_min_stock WHERE product_id=@id", productId, cancellationToken).ConfigureAwait(false);
        return value is null ? null : int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    public async Task SetAsync(int productId, int minimum, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        if (minimum is < 1 or > StockMovementRules.MaxQuantity) throw new StockAlertException(StockAlertRules.MinimumMessage);
        if (!MariaDatabaseGuard.IsAllowedDatabase(configuration)) throw new StockAlertException("Modificările sunt permise numai în baza BlazorStoc. Verifică numele bazei configurate.");
        var actor = (await RepositoryAudit.ActorAsync(accessControl, cancellationToken).ConfigureAwait(false)).Username;
        string product;
        string? old;
        await using (var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false))
        {
            product = await StockAlertSql.TextAsync(connection, "SELECT name FROM products WHERE id=@id", productId, cancellationToken).ConfigureAwait(false)
                ?? throw new StockAlertException("Produsul nu mai există.");
            old = await StockAlertSql.ValueAsync(connection, "SELECT min_quantity FROM product_min_stock WHERE product_id=@id", productId, cancellationToken).ConfigureAwait(false);
            await StockAlertSql.Command(connection, """
                INSERT INTO product_min_stock (product_id,min_quantity,set_date,updated_by,updated_utc) VALUES (@p,@m,@date,@by,@now)
                ON DUPLICATE KEY UPDATE min_quantity=@m,set_date=@date,updated_by=@by,updated_utc=@now
                """, ("@p", productId), ("@m", minimum), ("@date", StockAlertSql.Today), ("@by", actor), ("@now", MariaTimeText.Format(DateTime.UtcNow)))
                .ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.Product, AuditActions.SetMinStock, productId.ToString(), product,
            AuditDetails.Identification(("Produs", product)) + "; " + AuditDetails.Changes([new AuditChange("Stoc minim", old is null ? "—" : $"{old} buc.", $"{minimum} buc.")]),
            "", cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveAsync(int productId, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        if (!MariaDatabaseGuard.IsAllowedDatabase(configuration)) throw new StockAlertException("Modificările sunt permise numai în baza BlazorStoc. Verifică numele bazei configurate.");
        string? product, old;
        await using (var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false))
        {
            old = await StockAlertSql.ValueAsync(connection, "SELECT min_quantity FROM product_min_stock WHERE product_id=@id", productId, cancellationToken).ConfigureAwait(false);
            if (old is null) return;
            product = await StockAlertSql.TextAsync(connection, "SELECT name FROM products WHERE id=@id", productId, cancellationToken).ConfigureAwait(false);
            await StockAlertSql.Command(connection, "DELETE FROM product_min_stock WHERE product_id=@id", ("@id", productId)).ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        var name = product ?? $"#{productId}";
        await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.Product, AuditActions.RemoveMinStock, productId.ToString(), name,
            AuditDetails.Identification(("Produs", name)) + "; " + AuditDetails.Changes([new AuditChange("Stoc minim", $"{old} buc.", "—")]),
            "", cancellationToken).ConfigureAwait(false);
    }
}

public sealed class MariaProjectDeadlineRepository(IConfiguration configuration, IAccessControl? accessControl = null, IAuditTrail? auditTrail = null) : IProjectDeadlineRepository
{
    private Task EnsureOperatorAsync(CancellationToken token) => accessControl?.EnsureModuleAsync("beneficiari", token) ?? Task.CompletedTask;

    public async Task<DateOnly?> GetAsync(int projectId, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAnyAsync(["beneficiari.view", "stoc.view"], cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        var value = await StockAlertSql.ValueAsync(connection, "SELECT deadline FROM project_deadlines WHERE project_id=@id", projectId, cancellationToken).ConfigureAwait(false);
        return value is null ? null : StockMovementRules.ParseStorageDate(value);
    }

    public async Task SetAsync(int projectId, DateOnly deadline, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        if (deadline < StockMovementRules.Today) throw new StockAlertException(StockAlertRules.DeadlineBeforeTodayMessage);
        if (!MariaDatabaseGuard.IsAllowedDatabase(configuration)) throw new StockAlertException("Modificările sunt permise numai în baza BlazorStoc. Verifică numele bazei configurate.");
        var actor = (await RepositoryAudit.ActorAsync(accessControl, cancellationToken).ConfigureAwait(false)).Username;
        string project;
        string? old;
        await using (var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false))
        {
            project = await StockAlertSql.TextAsync(connection, "SELECT name FROM projects WHERE id=@id", projectId, cancellationToken).ConfigureAwait(false)
                ?? throw new StockAlertException("Proiectul nu mai există.");
            old = await StockAlertSql.ValueAsync(connection, "SELECT deadline FROM project_deadlines WHERE project_id=@id", projectId, cancellationToken).ConfigureAwait(false);
            await StockAlertSql.Command(connection, """
                INSERT INTO project_deadlines (project_id,deadline,updated_by,updated_utc) VALUES (@p,@d,@by,@now)
                ON DUPLICATE KEY UPDATE deadline=@d,updated_by=@by,updated_utc=@now
                """, ("@p", projectId), ("@d", StockMovementRules.StorageDate(deadline)), ("@by", actor), ("@now", MariaTimeText.Format(DateTime.UtcNow)))
                .ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.Project, AuditActions.SetProjectDeadline, projectId.ToString(), project,
            AuditDetails.Identification(("Proiect", project)) + "; "
            + AuditDetails.Changes([new AuditChange("Termen", old is null ? "—" : StockMovementRules.DisplayDate(StockMovementRules.ParseStorageDate(old)), StockMovementRules.DisplayDate(deadline))]),
            "", cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveAsync(int projectId, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        if (!MariaDatabaseGuard.IsAllowedDatabase(configuration)) throw new StockAlertException("Modificările sunt permise numai în baza BlazorStoc. Verifică numele bazei configurate.");
        string? project, old;
        await using (var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false))
        {
            old = await StockAlertSql.ValueAsync(connection, "SELECT deadline FROM project_deadlines WHERE project_id=@id", projectId, cancellationToken).ConfigureAwait(false);
            if (old is null) return;
            project = await StockAlertSql.TextAsync(connection, "SELECT name FROM projects WHERE id=@id", projectId, cancellationToken).ConfigureAwait(false);
            await StockAlertSql.Command(connection, "DELETE FROM project_deadlines WHERE project_id=@id", ("@id", projectId)).ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        var name = project ?? $"#{projectId}";
        await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.Project, AuditActions.RemoveProjectDeadline, projectId.ToString(), name,
            AuditDetails.Identification(("Proiect", name)) + "; "
            + AuditDetails.Changes([new AuditChange("Termen", StockMovementRules.DisplayDate(StockMovementRules.ParseStorageDate(old)), "—")]),
            "", cancellationToken).ConfigureAwait(false);
    }
}

public sealed class MariaStockAlertReader(IConfiguration configuration) : IStockAlertReader
{
    public async Task<IReadOnlyList<BelowMinimumItem>> GetBelowMinimumAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = StockAlertSql.Command(connection, """
            SELECT p.id,p.name,p.quantity,s.min_quantity,s.set_date,
                   (SELECT MAX(m.movement_date) FROM stock_movements m WHERE m.product_id=p.id AND m.voided_utc IS NULL)
            FROM product_min_stock s INNER JOIN products p ON p.id=s.product_id
            WHERE p.quantity<s.min_quantity ORDER BY p.name,p.id
            """);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<BelowMinimumItem>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var setDate = StockMovementRules.ParseStorageDate(reader.GetString(4));
            var lastMovement = reader.IsDBNull(5) ? setDate : StockMovementRules.ParseStorageDate(reader.GetString(5));
            result.Add(new(checked((int)reader.GetInt64(0)), reader.GetString(1), Convert.ToInt32(reader.GetValue(2)), Convert.ToInt32(reader.GetValue(3)),
                lastMovement > setDate ? lastMovement : setDate));
        }
        return result;
    }

    public async Task<IReadOnlyList<StaleReservationItem>> GetStaleReservationsAsync(int olderThanDays, CancellationToken cancellationToken = default)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = StockAlertSql.Command(connection, """
            SELECT r.id,r.project_id,p.name,r.product_id,pr.name,r.quantity,r.updated_utc
            FROM project_reservations r INNER JOIN projects p ON p.id=r.project_id INNER JOIN products pr ON pr.id=r.product_id
            WHERE r.quantity>0 ORDER BY r.updated_utc,r.id
            """);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<StaleReservationItem>();
        var limit = StockMovementRules.Today.AddDays(-olderThanDays);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var since = StockAlertSql.LocalDate(reader.GetString(6));
            if (since > limit) continue;
            result.Add(new(checked((int)reader.GetInt64(0)), checked((int)reader.GetInt64(1)), reader.GetString(2), checked((int)reader.GetInt64(3)), reader.GetString(4),
                Convert.ToInt32(reader.GetValue(5)), since));
        }
        return result;
    }

    public async Task<IReadOnlyList<ProjectDeadlineItem>> GetProjectDeadlinesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = StockAlertSql.Command(connection, """
            SELECT d.project_id,p.name,d.deadline FROM project_deadlines d INNER JOIN projects p ON p.id=d.project_id ORDER BY d.deadline,d.project_id
            """);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<ProjectDeadlineItem>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new(checked((int)reader.GetInt64(0)), reader.GetString(1), StockMovementRules.ParseStorageDate(reader.GetString(2))));
        return result;
    }
}
