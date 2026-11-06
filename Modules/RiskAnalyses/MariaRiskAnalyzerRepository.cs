using MySqlConnector;
using static BlazorStoc.Services.MariaDb;

namespace BlazorStoc.Services;

// MariaDB mode: `risk_analyzers` is created by migration 59; like the other repositories this one never alters the schema.
public sealed class MariaRiskAnalyzerRepository(
    IConfiguration configuration,
    IAccessControl? accessControl = null,
    IAuditTrail? auditTrail = null) : IRiskAnalyzerRepository
{
    private const string Select = """
        SELECT a.id, a.name, a.notes, a.version, (SELECT COUNT(*) FROM risk_analyses r WHERE r.analyzer_id = a.id) FROM risk_analyzers a
        """;

    public async Task<IReadOnlyList<RiskAnalyzer>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAnyPermissionAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        return await ReadAsync(connection, null, "", [], false, cancellationToken).ConfigureAwait(false);
    }

    public async Task<RiskAnalyzer> CreateAsync(string name, string notes, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAsync("nomenclator.add", cancellationToken).ConfigureAwait(false);
        var (text, note) = Validate(name, notes);
        var id = await WriteAsync(async (connection, transaction) =>
        {
            var now = MariaTimeText.Format(DateTime.UtcNow);
            await using var insert = Command(connection, transaction, """
                INSERT INTO risk_analyzers(name,name_key,notes,version,created_utc,updated_utc) VALUES(@name,@key,@notes,0,@now,@now)
                """, ("@name", text), ("@key", RiskAnalyzerRules.Key(text)), ("@notes", note), ("@now", now));
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return checked((int)insert.LastInsertedId);
        }, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.RiskAnalyzer, AuditActions.AddRiskAnalyzer, id.ToString(), text,
            RiskAnalyzerRules.Identification(text, note), string.Empty, cancellationToken).ConfigureAwait(false);
        return await GetAsync(id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<RiskAnalyzer> UpdateAsync(RiskAnalyzer original, string name, string notes, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAsync("nomenclator.edit", cancellationToken).ConfigureAwait(false);
        var (text, note) = Validate(name, notes);
        var current = await WriteAsync(async (connection, transaction) =>
        {
            var found = CheckCurrent(await ReadAsync(connection, transaction, "WHERE a.id=@id", [("@id", original.Id)], true, cancellationToken).ConfigureAwait(false), original);
            if (text == found.Name && note == found.Notes) throw new RiskAnalyzerOperationException(RiskAnalyzerRules.NoChangeMessage);
            await using var update = Command(connection, transaction, """
                UPDATE risk_analyzers SET name=@name,name_key=@key,notes=@notes,version=version+1,updated_utc=@now WHERE id=@id AND version=@version
                """, ("@name", text), ("@key", RiskAnalyzerRules.Key(text)), ("@notes", note), ("@now", MariaTimeText.Format(DateTime.UtcNow)),
                ("@id", found.Id), ("@version", found.Version));
            if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1) throw new RiskAnalyzerOperationException(RiskAnalyzerRules.StaleMessage);
            return found;
        }, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordEditAsync(auditTrail, accessControl, AuditEntities.RiskAnalyzer, current.Id.ToString(), text,
            [new AuditChange("Denumire", current.Name, text), new AuditChange("Observații", current.Notes, note)], string.Empty, cancellationToken,
            AuditActions.EditRiskAnalyzer).ConfigureAwait(false);
        return await GetAsync(current.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(RiskAnalyzer original, string reason, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAsync("nomenclator.delete", cancellationToken).ConfigureAwait(false);
        var motif = ChangeReasonRules.Normalize(reason);
        if (ChangeReasonRules.ValidationError(motif) is { } reasonError) throw new RiskAnalyzerOperationException(reasonError);
        var deleted = await WriteAsync(async (connection, transaction) =>
        {
            var found = CheckCurrent(await ReadAsync(connection, transaction, "WHERE a.id=@id", [("@id", original.Id)], true, cancellationToken).ConfigureAwait(false), original);
            if (found.UsedBy > 0) throw RiskAnalyzerRules.InUse(found.Name, found.UsedBy);
            await using var delete = Command(connection, transaction, "DELETE FROM risk_analyzers WHERE id=@id AND version=@version", ("@id", found.Id), ("@version", found.Version));
            if (await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1) throw new RiskAnalyzerOperationException(RiskAnalyzerRules.StaleMessage);
            return found;
        }, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.RiskAnalyzer, AuditActions.DeleteRiskAnalyzer, deleted.Id.ToString(), deleted.Name,
            RiskAnalyzerRules.Identification(deleted.Name, deleted.Notes), motif, cancellationToken).ConfigureAwait(false);
    }

    private async Task<RiskAnalyzer> GetAsync(int id, CancellationToken token)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, token).ConfigureAwait(false);
        return (await ReadAsync(connection, null, "WHERE a.id=@id", [("@id", id)], false, token).ConfigureAwait(false)).FirstOrDefault()
               ?? throw new RiskAnalyzerOperationException(RiskAnalyzerRules.StaleMessage);
    }

    private static async Task<IReadOnlyList<RiskAnalyzer>> ReadAsync(MySqlConnection connection, MySqlTransaction? transaction, string where,
        (string Name, object? Value)[] parameters, bool forUpdate, CancellationToken token)
    {
        var result = new List<RiskAnalyzer>();
        await using var command = Command(connection, transaction, $"{Select} {where} ORDER BY a.name_key, a.id{(forUpdate ? " FOR UPDATE" : "")}", parameters);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
            result.Add(new(checked((int)reader.GetInt64(0)), reader.GetString(1), reader.GetString(2), checked((int)reader.GetInt64(4)), reader.GetInt64(3)));
        return result;
    }

    private static (string Name, string Notes) Validate(string name, string notes)
    {
        var text = RiskAnalyzerRules.Clean(name);
        var note = TextNormalization.ForStorage((notes ?? "").Replace("\r\n", "\n")).Trim();
        if (RiskAnalyzerRules.NameProblem(text) is { } nameProblem) throw new RiskAnalyzerOperationException(nameProblem);
        if (RiskAnalyzerRules.NotesProblem(note) is { } notesProblem) throw new RiskAnalyzerOperationException(notesProblem);
        return (text, note);
    }

    private static RiskAnalyzer CheckCurrent(IReadOnlyList<RiskAnalyzer> found, RiskAnalyzer original)
    {
        var current = found.FirstOrDefault();
        if (current is null || current.Version != original.Version) throw new RiskAnalyzerOperationException(RiskAnalyzerRules.StaleMessage);
        return current;
    }

    private Task<T> WriteAsync<T>(Func<MySqlConnection, MySqlTransaction, Task<T>> action, CancellationToken token) =>
        MariaDb.WriteAsync(configuration, action, message => new RiskAnalyzerOperationException(message),
            exception => exception.Number switch
            {
                1062 => new RiskAnalyzerOperationException(RiskAnalyzerRules.DuplicateMessage),
                1451 => new RiskAnalyzerOperationException("Analizorul este folosit de o analiză de risc și nu poate fi șters."),
                _ => null
            }, token);
}
