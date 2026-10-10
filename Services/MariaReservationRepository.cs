using System.Data;
using MySqlConnector;
using static BlazorStoc.Services.MariaDb;

namespace BlazorStoc.Services;

// MariaDB mode: `project_reservations` (migration 27). A reservation never changes the stock. Changes need a product operator (like the exits); each has its own
// journal action, recorded under the project. The product row is locked while a reservation is added, so two operations on the same product are serialized.
public sealed class MariaReservationRepository(
    IConfiguration configuration,
    IAccessControl? accessControl = null,
    IAuditTrail? auditTrail = null) : IReservationRepository
{

    private const string Select = """
        SELECT r.id,r.project_id,p.name,r.project_component_id,t.name,r.product_id,pr.name,r.quantity
        FROM project_reservations r INNER JOIN projects p ON p.id=r.project_id INNER JOIN products pr ON pr.id=r.product_id
        LEFT JOIN project_components c ON c.id=r.project_component_id LEFT JOIN system_types t ON t.id=c.system_type_id
        """;

    private static Reservation ReadReservation(MySqlDataReader reader) => new(checked((int)reader.GetInt64(0)), checked((int)reader.GetInt64(1)), reader.GetString(2),
        reader.IsDBNull(3) ? null : checked((int)reader.GetInt64(3)), reader.IsDBNull(4) ? null : reader.GetString(4), checked((int)reader.GetInt64(5)), reader.GetString(6),
        Convert.ToInt32(reader.GetValue(7)));

    public async Task<IReadOnlyList<Reservation>> GetForProjectAsync(int projectId, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, null, $"{Select} WHERE r.project_id=@project ORDER BY pr.name,r.id", ("@project", projectId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<Reservation>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(ReadReservation(reader));
        return result;
    }

    public async Task<IReadOnlyList<ReservationHolder>> GetHoldersAsync(int productId, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        return await ReservationSql.HoldersAsync(connection, null, productId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Reservation> ReserveAsync(int projectId, int? componentId, int productId, int quantity, CancellationToken cancellationToken = default, bool capToFree = false)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        if (quantity <= 0) throw new ReservationException(ReservationRules.QuantityMessage);
        if (!MariaDatabaseGuard.IsAllowedDatabase(configuration)) throw new ReservationException("Modificările sunt permise numai în baza BlazorStoc. Verifică numele bazei configurate.");
        string projectName, productName, componentName;
        int id;
        await using (var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false))
        {
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
            try
            {
                await using (var product = Command(connection, transaction, "SELECT name,quantity FROM products WHERE id=@id FOR UPDATE", ("@id", productId)))
                await using (var reader = await product.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) throw new ReservationException(ReservationRules.ProductMissingMessage);
                    productName = reader.GetString(0);
                    var stock = Convert.ToInt32(reader.GetValue(1));
                    await reader.CloseAsync().ConfigureAwait(false);
                    var reserved = await ReservationSql.TotalAsync(connection, transaction, productId, cancellationToken).ConfigureAwait(false);
                    var free = ReservationRules.Free(stock, reserved);
                    if (capToFree) quantity = Math.Min(quantity, free);
                    if (quantity <= 0 || quantity > free) throw new ReservationException(ReservationRules.NotEnoughFreeMessage(free));
                }
                await using (var project = Command(connection, transaction, "SELECT name FROM projects WHERE id=@id", ("@id", projectId)))
                    projectName = await project.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string ?? throw new ReservationException(ReservationRules.ProjectMissingMessage);
                componentName = string.Empty;
                if (componentId is { } component)
                {
                    await using var check = Command(connection, transaction, """
                        SELECT t.name FROM project_components c INNER JOIN system_types t ON t.id=c.system_type_id WHERE c.id=@id AND c.project_id=@project AND c.archived_utc IS NULL
                        """, ("@id", component), ("@project", projectId));
                    componentName = await check.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string ?? throw new ReservationException(ReservationRules.ComponentInvalidMessage);
                }
                var now = MariaTimeText.Format(DateTime.UtcNow);
                var actor = await RepositoryAudit.ActorAsync(accessControl, cancellationToken).ConfigureAwait(false);
                await using var upsert = Command(connection, transaction, """
                    INSERT INTO project_reservations(project_id,project_component_id,component_key,product_id,quantity,version,created_by,created_utc,updated_utc)
                    VALUES(@project,@component,@key,@product,@quantity,0,@actor,@now,@now)
                    ON DUPLICATE KEY UPDATE quantity=quantity+VALUES(quantity),version=version+1,updated_utc=VALUES(updated_utc)
                    """, ("@project", projectId), ("@component", componentId), ("@key", componentId ?? 0), ("@product", productId), ("@quantity", quantity), ("@actor", actor.Username), ("@now", now));
                await upsert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                await using var read = Command(connection, transaction, "SELECT id FROM project_reservations WHERE project_id=@project AND product_id=@product AND component_key=@key",
                    ("@project", projectId), ("@product", productId), ("@key", componentId ?? 0));
                id = Convert.ToInt32(await read.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                if (transaction.Connection is not null) await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }
        await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.Project, AuditActions.ReserveStock, projectId.ToString(), projectName,
            AuditDetails.Identification(("Proiect", projectName), ("Produs", productName), ("Cantitate", $"{quantity} buc."), ("Componentă", componentName.Length == 0 ? "—" : componentName)),
            string.Empty, cancellationToken).ConfigureAwait(false);
        return (await GetForProjectAsync(projectId, cancellationToken).ConfigureAwait(false)).First(item => item.Id == id);
    }

    public async Task ReduceAsync(int reservationId, int quantity, string reason, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        if (quantity <= 0) throw new ReservationException(ReservationRules.QuantityMessage);
        if (ReservationRules.ReasonError(reason) is { Length: > 0 } reasonError) throw new ReservationException(reasonError);
        reason = TextNormalization.ForStorage(reason.Trim());
        if (!MariaDatabaseGuard.IsAllowedDatabase(configuration)) throw new ReservationException("Modificările sunt permise numai în baza BlazorStoc. Verifică numele bazei configurate.");
        Reservation current;
        bool released;
        await using (var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false))
        {
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
            try
            {
                await using (var read = Command(connection, transaction, $"{Select} WHERE r.id=@id FOR UPDATE", ("@id", reservationId)))
                await using (var reader = await read.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) throw new ReservationException(ReservationRules.ReservationMissingMessage);
                    current = ReadReservation(reader);
                }
                if (quantity > current.Quantity) throw new ReservationException(ReservationRules.ReduceTooMuchMessage);
                released = quantity == current.Quantity;
                await ReservationSql.DecreaseAsync(connection, transaction, reservationId, quantity, cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                if (transaction.Connection is not null) await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }
        await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.Project, released ? AuditActions.ReleaseReservation : AuditActions.ReduceReservation,
            current.ProjectId.ToString(), current.ProjectName,
            AuditDetails.Identification(("Proiect", current.ProjectName), ("Produs", current.ProductName), ("Componentă", current.ComponentName ?? "—"))
            + "; " + AuditDetails.Changes([new AuditChange("Rezervat", $"{current.Quantity} buc.", $"{current.Quantity - quantity} buc.")]),
            reason, cancellationToken).ConfigureAwait(false);
    }

    private Task EnsureOperatorAsync(CancellationToken token) => accessControl?.EnsureProductOperatorAsync(token) ?? Task.CompletedTask;
}

// SQL shared with the exit repository (inside its transaction).
internal static class ReservationSql
{
    public static async Task<int> TotalAsync(MySqlConnection connection, MySqlTransaction? transaction, int productId, CancellationToken token)
    {
        await using var command = new MySqlCommand("SELECT COALESCE(SUM(quantity),0) FROM project_reservations WHERE product_id=@product", connection, transaction);
        command.Parameters.AddWithValue("@product", productId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(token).ConfigureAwait(false));
    }

    public static async Task<IReadOnlyList<ReservationHolder>> HoldersAsync(MySqlConnection connection, MySqlTransaction? transaction, int productId, CancellationToken token)
    {
        await using var command = new MySqlCommand("""
            SELECT r.project_id,p.name,SUM(r.quantity) FROM project_reservations r INNER JOIN projects p ON p.id=r.project_id
            WHERE r.product_id=@product GROUP BY r.project_id,p.name HAVING SUM(r.quantity)>0 ORDER BY p.name
            """, connection, transaction);
        command.Parameters.AddWithValue("@product", productId);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        var result = new List<ReservationHolder>();
        while (await reader.ReadAsync(token).ConfigureAwait(false)) result.Add(new ReservationHolder(checked((int)reader.GetInt64(0)), reader.GetString(1), Convert.ToInt32(reader.GetValue(2))));
        return result;
    }

    // Takes `quantity` out of one reservation; a reservation that reaches zero is deleted.
    public static async Task DecreaseAsync(MySqlConnection connection, MySqlTransaction transaction, int reservationId, int quantity, CancellationToken token)
    {
        await using var update = new MySqlCommand("UPDATE project_reservations SET quantity=quantity-@quantity,version=version+1,updated_utc=@now WHERE id=@id", connection, transaction);
        update.Parameters.AddWithValue("@quantity", quantity); update.Parameters.AddWithValue("@now", MariaTimeText.Format(DateTime.UtcNow)); update.Parameters.AddWithValue("@id", reservationId);
        await update.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        await using var delete = new MySqlCommand("DELETE FROM project_reservations WHERE id=@id AND quantity<=0", connection, transaction);
        delete.Parameters.AddWithValue("@id", reservationId);
        await delete.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }
}
