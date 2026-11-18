using System.Globalization;
using MySqlConnector;
using static BlazorStoc.Services.MariaDb;

namespace BlazorStoc.Services;

// MariaDB mode: `consumption_notes` and `consumption_note_lines` (migration 93). A note is generated on request from the exits to one project (net of the returns tied to them),
// is never edited and can be deleted. Reading, generating and deleting need the right to see the stock (stoc.view); every generation, deactivation and deletion has its own
// journal action.
public sealed class MariaConsumptionNoteRepository(
    IConfiguration configuration,
    IAccessControl? accessControl = null,
    IAuditTrail? auditTrail = null) : IConsumptionNoteRepository
{
    private const string Permission = "stoc.view";

    private const string NoteColumns = """
        n.id,n.beneficiary_id,n.beneficiary_name,n.beneficiary_cui,n.project_id,n.project_name,n.note_date,n.active,n.created_by,n.created_utc,
        (SELECT COUNT(*) FROM consumption_note_lines l WHERE l.note_id=n.id)
        """;

    public async Task<IReadOnlyList<ConsumptionNote>> GetAllAsync(int? beneficiaryId = null, int? projectId = null, CancellationToken cancellationToken = default)
    {
        await EnsureAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, null, $"""
            SELECT {NoteColumns} FROM consumption_notes n
            WHERE (@beneficiary IS NULL OR n.beneficiary_id=@beneficiary) AND (@project IS NULL OR n.project_id=@project)
            ORDER BY n.id DESC
            """, ("@beneficiary", beneficiaryId), ("@project", projectId));
        return await ReadNotesAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ConsumptionNoteDetails?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        await EnsureAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, null, $"SELECT {NoteColumns} FROM consumption_notes n WHERE n.id=@id", ("@id", id));
        var note = (await ReadNotesAsync(command, cancellationToken).ConfigureAwait(false)).FirstOrDefault();
        if (note is null) return null;
        var lines = new List<ConsumptionNoteLine>();
        await using var linesCommand = Command(connection, null,
            "SELECT position,movement_date,product_id,product_name,subcategory,quantity FROM consumption_note_lines WHERE note_id=@id ORDER BY position", ("@id", id));
        await using var reader = await linesCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            lines.Add(new ConsumptionNoteLine(Convert.ToInt32(reader.GetValue(0)), StockMovementRules.ParseStorageDate(reader.GetString(1)),
                reader.IsDBNull(2) ? null : checked((int)reader.GetInt64(2)), reader.GetString(3), reader.GetString(4), Convert.ToInt32(reader.GetValue(5))));
        return new ConsumptionNoteDetails(note, lines);
    }

    public async Task<ConsumptionNoteGenerated> GenerateAsync(int projectId, bool deletePrevious, CancellationToken cancellationToken = default)
    {
        await EnsureAsync(cancellationToken).ConfigureAwait(false);
        var actor = (accessControl is null ? null : await accessControl.GetUsernameAsync(cancellationToken).ConfigureAwait(false)) ?? "necunoscut";
        var (note, previous) = await WriteAsync(async (connection, transaction) =>
        {
            string beneficiaryName, project, cui;
            int beneficiaryId;
            await using (var read = Command(connection, transaction,
                "SELECT b.id,b.name,b.cui,p.name FROM projects p INNER JOIN beneficiaries b ON b.id=p.beneficiary_id WHERE p.id=@project", ("@project", projectId)))
            await using (var reader = await read.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) throw new ConsumptionNoteException(ConsumptionNoteRules.ProjectMissingMessage);
                beneficiaryId = checked((int)reader.GetInt64(0)); beneficiaryName = reader.GetString(1); cui = reader.GetString(2); project = reader.GetString(3);
            }
            var lines = await ReadEquipmentAsync(connection, transaction, projectId, cancellationToken).ConfigureAwait(false);
            if (lines.Count == 0) throw new ConsumptionNoteException(ConsumptionNoteRules.NoEquipmentMessage);

            var older = new List<int>();
            await using (var olderCommand = Command(connection, transaction, "SELECT id FROM consumption_notes WHERE project_id=@project AND (@all=1 OR active=1) ORDER BY id", ("@project", projectId), ("@all", deletePrevious ? 1 : 0)))
            await using (var olderReader = await olderCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                while (await olderReader.ReadAsync(cancellationToken).ConfigureAwait(false)) older.Add(checked((int)olderReader.GetInt64(0)));
            if (older.Count > 0)
            {
                await using var change = Command(connection, transaction, deletePrevious
                    ? "DELETE FROM consumption_notes WHERE project_id=@project"
                    : "UPDATE consumption_notes SET active=0 WHERE project_id=@project AND active=1", ("@project", projectId));
                await change.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            var now = DateTime.UtcNow;
            var today = StockMovementRules.Today;
            await using var insert = Command(connection, transaction, """
                INSERT INTO consumption_notes(beneficiary_id,project_id,beneficiary_name,beneficiary_cui,project_name,note_date,active,created_by,created_utc)
                VALUES(@beneficiary,@project,@beneficiaryName,@cui,@projectName,@date,1,@by,@now)
                """, ("@beneficiary", beneficiaryId), ("@project", projectId), ("@beneficiaryName", beneficiaryName), ("@cui", cui), ("@projectName", project),
                ("@date", StockMovementRules.StorageDate(today)), ("@by", actor), ("@now", MariaTimeText.Format(now)));
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            var id = checked((int)insert.LastInsertedId);
            foreach (var line in lines)
            {
                await using var lineInsert = Command(connection, transaction, """
                    INSERT INTO consumption_note_lines(note_id,position,movement_date,product_id,product_name,subcategory,quantity)
                    VALUES(@note,@position,@date,@product,@name,@subcategory,@quantity)
                    """, ("@note", id), ("@position", line.Position), ("@date", StockMovementRules.StorageDate(line.MovementDate)), ("@product", line.ProductId),
                    ("@name", line.ProductName), ("@subcategory", line.Subcategory), ("@quantity", line.Quantity));
                await lineInsert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            return (new ConsumptionNote(id, beneficiaryId, beneficiaryName, cui, projectId, project, today, true, actor, now, lines.Count), older);
        }, cancellationToken).ConfigureAwait(false);

        foreach (var id in previous)
            await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.ConsumptionNote, deletePrevious ? AuditActions.DeleteConsumptionNote : AuditActions.DeactivateConsumptionNote,
                id.ToString(CultureInfo.InvariantCulture), ConsumptionNoteRules.Title(id),
                AuditDetails.Identification(("Beneficiar", note.BeneficiaryName), ("Proiect", note.ProjectName), ("La generarea bonului", note.Id.ToString(CultureInfo.InvariantCulture))),
                string.Empty, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.ConsumptionNote, AuditActions.GenerateConsumptionNote, note.Id.ToString(CultureInfo.InvariantCulture),
            ConsumptionNoteRules.Title(note.Id),
            AuditDetails.Identification(("Beneficiar", note.BeneficiaryName), ("Proiect", note.ProjectName), ("Linii", note.LineCount.ToString(CultureInfo.InvariantCulture)),
                (deletePrevious ? "Bonuri anterioare șterse" : "Bonuri anterioare dezactivate", previous.Count.ToString(CultureInfo.InvariantCulture))),
            string.Empty, cancellationToken).ConfigureAwait(false);
        return new ConsumptionNoteGenerated(note, deletePrevious ? 0 : previous.Count, deletePrevious ? previous.Count : 0);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await EnsureAsync(cancellationToken).ConfigureAwait(false);
        var note = await WriteAsync(async (connection, transaction) =>
        {
            await using var read = Command(connection, transaction, $"SELECT {NoteColumns} FROM consumption_notes n WHERE n.id=@id", ("@id", id));
            var found = (await ReadNotesAsync(read, cancellationToken).ConfigureAwait(false)).FirstOrDefault() ?? throw new ConsumptionNoteException(ConsumptionNoteRules.NoteMissingMessage);
            await using var delete = Command(connection, transaction, "DELETE FROM consumption_notes WHERE id=@id", ("@id", id));
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return found;
        }, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.ConsumptionNote, AuditActions.DeleteConsumptionNote, note.Id.ToString(CultureInfo.InvariantCulture),
            ConsumptionNoteRules.Title(note.Id),
            AuditDetails.Identification(("Beneficiar", note.BeneficiaryName), ("Proiect", note.ProjectName), ("Linii", note.LineCount.ToString(CultureInfo.InvariantCulture))),
            string.Empty, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlySet<int>> GetProjectsWithEquipmentAsync(int beneficiaryId, CancellationToken cancellationToken = default)
    {
        await EnsureAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, null, """
            SELECT x.project_id FROM (
                SELECT t.project_id,t.product_id,SUM(t.q) AS net FROM (
                    SELECT e.project_id,e.product_id,e.quantity AS q FROM stock_movements e
                      WHERE e.kind=0 AND e.voided_utc IS NULL AND e.destination=@destination AND e.beneficiary_id=@beneficiary AND e.project_id IS NOT NULL
                    UNION ALL
                    SELECT e.project_id,r.product_id,-r.quantity FROM stock_movements r INNER JOIN stock_movements e ON e.id=r.return_of_movement_id
                      WHERE r.voided_utc IS NULL AND e.beneficiary_id=@beneficiary AND e.project_id IS NOT NULL
                    UNION ALL
                    SELECT project_id,product_id,-quantity FROM stock_movements
                      WHERE kind=1 AND free_entry_type=@returnType AND voided_utc IS NULL AND beneficiary_id=@beneficiary AND project_id IS NOT NULL
                ) t GROUP BY t.project_id,t.product_id HAVING SUM(t.q)>0) x GROUP BY x.project_id
            """, ("@beneficiary", beneficiaryId), ("@destination", (int)ExitDestination.Beneficiary), ("@returnType", (int)FreeEntryType.ReturnFromProject));
        var result = new HashSet<int>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(checked((int)reader.GetInt64(0)));
        return result;
    }

    // The equipment of the project at this moment: one line per product with the quantity left at the project (exits minus returns) and the date of the last exit of that product.
    private static async Task<List<ConsumptionNoteLine>> ReadEquipmentAsync(MySqlConnection connection, MySqlTransaction? transaction, int projectId, CancellationToken token)
    {
        var rows = new List<(DateOnly Date, int ProductId, string Name, string Subcategory, int Net)>();
        await using var command = Command(connection, transaction, """
            SELECT t.product_id,p.name,COALESCE(sc.name,''),SUM(t.q) AS net,MAX(t.last_date) AS last_date
            FROM (
                SELECT e.product_id,e.quantity AS q,COALESCE(NULLIF(e.movement_date,''),SUBSTR(e.created_utc,1,10)) AS last_date FROM stock_movements e
                  WHERE e.kind=0 AND e.voided_utc IS NULL AND e.destination=@destination AND e.project_id=@project
                UNION ALL
                SELECT r.product_id,-r.quantity,'' FROM stock_movements r INNER JOIN stock_movements e ON e.id=r.return_of_movement_id
                  WHERE r.voided_utc IS NULL AND e.project_id=@project
                UNION ALL
                SELECT product_id,-quantity,'' FROM stock_movements
                  WHERE kind=1 AND free_entry_type=@returnType AND voided_utc IS NULL AND project_id=@project
            ) t
            INNER JOIN products p ON p.id=t.product_id
            LEFT JOIN subcategories sc ON sc.id=p.subcategory_id
            GROUP BY t.product_id,p.name,sc.name HAVING SUM(t.q)>0
            ORDER BY MAX(t.last_date),p.name
            """, ("@project", projectId), ("@destination", (int)ExitDestination.Beneficiary), ("@returnType", (int)FreeEntryType.ReturnFromProject));
        await using (var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            while (await reader.ReadAsync(token).ConfigureAwait(false))
                rows.Add((StockMovementRules.ParseStorageDate(reader.GetString(4)), checked((int)reader.GetInt64(0)), reader.GetString(1), reader.GetString(2), Convert.ToInt32(reader.GetValue(3))));
        return rows.Select((row, index) => new ConsumptionNoteLine(index + 1, row.Date, row.ProductId, row.Name, row.Subcategory, row.Net)).ToList();
    }

    private static async Task<List<ConsumptionNote>> ReadNotesAsync(MySqlCommand command, CancellationToken token)
    {
        var result = new List<ConsumptionNote>();
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
            result.Add(new ConsumptionNote(checked((int)reader.GetInt64(0)), checked((int)reader.GetInt64(1)), reader.GetString(2), reader.GetString(3), checked((int)reader.GetInt64(4)), reader.GetString(5),
                StockMovementRules.ParseStorageDate(reader.GetString(6)), Convert.ToBoolean(reader.GetValue(7)), reader.GetString(8), MariaTimeText.Parse(reader.GetString(9)), Convert.ToInt32(reader.GetValue(10))));
        return result;
    }

    private Task<T> WriteAsync<T>(Func<MySqlConnection, MySqlTransaction, Task<T>> action, CancellationToken token) =>
        MariaDb.WriteAsync(configuration, action, message => new ConsumptionNoteException(message), token, "Modificările sunt permise numai în baza BlazorStoc. Verifică numele bazei configurate.");

    private Task EnsureAsync(CancellationToken token) => accessControl?.EnsureAsync(Permission, token) ?? Task.CompletedTask;
}
