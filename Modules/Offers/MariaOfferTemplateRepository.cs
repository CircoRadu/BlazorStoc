using System.Data;
using MySqlConnector;
using static BlazorStoc.Services.MariaDb;

namespace BlazorStoc.Services;

// MariaDB mode: `offer_templates` (migration 24). Creating, changing and (de)activating needs a product operator (like the invoice templates), deleting
// an administrator; every change is checked against the version read and has its own journal action.
public sealed class MariaOfferTemplateRepository(
    IConfiguration configuration,
    IAccessControl? accessControl = null,
    IAuditTrail? auditTrail = null) : IOfferTemplateRepository
{
    private const string Select = "SELECT id,name,active,definition,version,updated_by,updated_utc FROM offer_templates";

    public async Task<IReadOnlyList<OfferTemplateRecord>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAnyPermissionAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        return await ReadAsync(connection, null, $"{Select} ORDER BY name,id", cancellationToken).ConfigureAwait(false);
    }

    private static async Task<List<OfferTemplateRecord>> ReadAsync(MySqlConnection connection, MySqlTransaction? transaction, string sql, CancellationToken token,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = Command(connection, transaction, sql, parameters);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        var result = new List<OfferTemplateRecord>();
        while (await reader.ReadAsync(token).ConfigureAwait(false))
            result.Add(new OfferTemplateRecord(checked((int)reader.GetInt64(0)), reader.GetString(1), Convert.ToBoolean(reader.GetValue(2)),
                OfferTemplateDefinition.Deserialize(reader.GetString(3)) ?? new OfferTemplateDefinition(), reader.GetInt64(4), reader.GetString(5), reader.GetString(6)));
        return result;
    }

    public async Task<OfferTemplateRecord> CreateAsync(string name, OfferTemplateDefinition definition, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAsync("oferte.sabloane", cancellationToken).ConfigureAwait(false);
        var text = SystemTypeRules.Clean(name);
        var actor = await RepositoryAudit.ActorAsync(accessControl, cancellationToken).ConfigureAwait(false);
        var id = await WriteAsync(async (connection, transaction) =>
        {
            var all = await ReadAsync(connection, transaction, Select, cancellationToken).ConfigureAwait(false);
            if (OfferRules.Problem(text, definition, all.Select(item => (item.Id, item.Name))) is { } problem) throw new OfferTemplateException(problem);
            var now = MariaTimeText.Format(DateTime.UtcNow);
            await using var insert = Command(connection, transaction, """
                INSERT INTO offer_templates(name,name_key,active,definition,version,created_by,created_utc,updated_by,updated_utc) VALUES(@name,@key,1,@definition,0,@by,@now,@by,@now)
                """, ("@name", text), ("@key", OfferRules.Key(text)), ("@definition", definition.Serialize()), ("@by", actor.Username), ("@now", now));
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return checked((int)insert.LastInsertedId);
        }, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.OfferTemplate, AuditActions.CreateOfferTemplate, id.ToString(), text,
            AuditDetails.Identification(("Denumire", text), ("Foaia", OfferRules.Describe(definition, "sheet")), ("Coloane", OfferRules.Describe(definition, "columns")),
                ("Secțiuni importate", OfferRules.Describe(definition, "sections"))), string.Empty, cancellationToken).ConfigureAwait(false);
        return await GetAsync(id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OfferTemplateRecord> SaveAsync(OfferTemplateRecord original, string name, OfferTemplateDefinition definition, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAsync("oferte.sabloane", cancellationToken).ConfigureAwait(false);
        var text = SystemTypeRules.Clean(name);
        var actor = await RepositoryAudit.ActorAsync(accessControl, cancellationToken).ConfigureAwait(false);
        var before = await WriteAsync(async (connection, transaction) =>
        {
            var all = await ReadAsync(connection, transaction, Select, cancellationToken).ConfigureAwait(false);
            var current = all.FirstOrDefault(item => item.Id == original.Id);
            if (current is null || current.Version != original.Version) throw new OfferTemplateException(OfferTemplateMessages.Stale);
            if (OfferRules.Problem(text, definition, all.Select(item => (item.Id, item.Name)), current.Id) is { } problem) throw new OfferTemplateException(problem);
            await using var update = Command(connection, transaction, """
                UPDATE offer_templates SET name=@name,name_key=@key,definition=@definition,version=version+1,updated_by=@by,updated_utc=@now WHERE id=@id AND version=@version
                """, ("@name", text), ("@key", OfferRules.Key(text)), ("@definition", definition.Serialize()), ("@by", actor.Username),
                ("@now", MariaTimeText.Format(DateTime.UtcNow)), ("@id", current.Id), ("@version", current.Version));
            if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1) throw new OfferTemplateException(OfferTemplateMessages.Stale);
            return current;
        }, cancellationToken).ConfigureAwait(false);
        var changes = new List<AuditChange>();
        if (before.Name != text) changes.Add(new AuditChange("Denumire", before.Name, text));
        foreach (var part in OfferRules.DescribedParts)
        {
            var oldText = OfferRules.Describe(before.Definition, part);
            var newText = OfferRules.Describe(definition, part);
            if (oldText != newText) changes.Add(new AuditChange(OfferRules.PartName(part), oldText.Length == 0 ? "—" : oldText, newText.Length == 0 ? "—" : newText));
        }
        if (changes.Count > 0)
            await AuditRecorder.RecordEditAsync(auditTrail, accessControl, AuditEntities.OfferTemplate, before.Id.ToString(), text, changes, string.Empty, cancellationToken,
                AuditActions.EditOfferTemplate).ConfigureAwait(false);
        return await GetAsync(before.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OfferTemplateRecord> SetActiveAsync(OfferTemplateRecord original, bool active, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAsync("oferte.sabloane", cancellationToken).ConfigureAwait(false);
        var actor = await RepositoryAudit.ActorAsync(accessControl, cancellationToken).ConfigureAwait(false);
        var current = await WriteAsync(async (connection, transaction) =>
        {
            var found = (await ReadAsync(connection, transaction, $"{Select} WHERE id=@id", cancellationToken, ("@id", original.Id)).ConfigureAwait(false)).FirstOrDefault();
            if (found is null || found.Version != original.Version) throw new OfferTemplateException(OfferTemplateMessages.Stale);
            if (found.Active == active) return found;
            await using var update = Command(connection, transaction,
                "UPDATE offer_templates SET active=@active,version=version+1,updated_by=@by,updated_utc=@now WHERE id=@id AND version=@version",
                ("@active", active ? 1 : 0), ("@by", actor.Username), ("@now", MariaTimeText.Format(DateTime.UtcNow)), ("@id", found.Id), ("@version", found.Version));
            if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1) throw new OfferTemplateException(OfferTemplateMessages.Stale);
            return found;
        }, cancellationToken).ConfigureAwait(false);
        if (current.Active != active)
            await AuditRecorder.RecordEditAsync(auditTrail, accessControl, AuditEntities.OfferTemplate, current.Id.ToString(), current.Name,
                [new AuditChange("Stare", current.Active ? "Activ" : "Inactiv", active ? "Activ" : "Inactiv")], string.Empty, cancellationToken,
                active ? AuditActions.ActivateOfferTemplate : AuditActions.DeactivateOfferTemplate).ConfigureAwait(false);
        return await GetAsync(current.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(OfferTemplateRecord original, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var removed = await WriteAsync(async (connection, transaction) =>
        {
            var found = (await ReadAsync(connection, transaction, $"{Select} WHERE id=@id", cancellationToken, ("@id", original.Id)).ConfigureAwait(false)).FirstOrDefault();
            if (found is null || found.Version != original.Version) throw new OfferTemplateException(OfferTemplateMessages.Stale);
            await using var delete = Command(connection, transaction, "DELETE FROM offer_templates WHERE id=@id AND version=@version", ("@id", found.Id), ("@version", found.Version));
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return found;
        }, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.OfferTemplate, AuditActions.DeleteOfferTemplate, removed.Id.ToString(), removed.Name,
            AuditDetails.Identification(("Denumire", removed.Name), ("Foaia", OfferRules.Describe(removed.Definition, "sheet")), ("Coloane", OfferRules.Describe(removed.Definition, "columns")),
                ("Secțiuni importate", OfferRules.Describe(removed.Definition, "sections"))), string.Empty, cancellationToken).ConfigureAwait(false);
    }

    private async Task<OfferTemplateRecord> GetAsync(int id, CancellationToken token)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, token).ConfigureAwait(false);
        return (await ReadAsync(connection, null, $"{Select} WHERE id=@id", token, ("@id", id)).ConfigureAwait(false)).FirstOrDefault()
               ?? throw new OfferTemplateException(OfferTemplateMessages.Stale);
    }

    private Task<T> WriteAsync<T>(Func<MySqlConnection, MySqlTransaction, Task<T>> action, CancellationToken token) =>
        MariaDb.WriteAsync(configuration, action, message => new OfferTemplateException(message),
            exception => exception.Number == 1062 ? new OfferTemplateException("Există deja un șablon cu această denumire.") : null, token,
            "Modificările sunt permise numai în baza BlazorStoc. Verifică numele bazei configurate.");

}

public static class OfferTemplateMessages
{
    public const string Stale = "Șablonul a fost modificat între timp. Actualizează lista și reia operația.";
}
