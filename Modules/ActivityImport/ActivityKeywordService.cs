using BlazorStoc.Services;
using MySqlConnector;

namespace BlazorStoc.Modules.ActivityImport;

// The nomenclator of the maintenance keywords (Nomenclator -> Cuvinte cheie mentenanță): one list for every ticket type of the import, except the types taken as maintenance (those do not
// need a word). A ticket whose closing text holds one of the active words (exactly, or only resembling it: see ActivityKeywords) is also the maintenance of its point. The list is kept here,
// not in the settings of the import: it is a nomenclator, edited by whoever has the right to the nomenclators (view, add, edit, delete), every change journaled by name.

public sealed record ActivityKeyword(int Id, string Keyword, bool Active, string CreatedBy, long Version);

public interface IActivityKeywordService
{
    Task<IReadOnlyList<ActivityKeyword>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<ActivityKeyword> AddAsync(string keyword, CancellationToken cancellationToken = default);
    Task<ActivityKeyword> RenameAsync(ActivityKeyword original, string keyword, CancellationToken cancellationToken = default);
    Task<ActivityKeyword> SetActiveAsync(ActivityKeyword original, bool active, CancellationToken cancellationToken = default);
    Task DeleteAsync(ActivityKeyword original, CancellationToken cancellationToken = default);
}

public static class ActivityKeywordRules
{
    /// <summary>The word as it is stored (one word or phrase, white space as single spaces); the reason when it is not valid.</summary>
    public static string? Validate(string? text, out string cleaned)
    {
        cleaned = string.Join(' ', (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (cleaned.Length == 0) return "Scrie cuvântul cheie.";
        if (cleaned.IndexOfAny([',', ';']) >= 0) return "Un singur cuvânt (sau o expresie) pe rând; nu folosi virgula sau punct și virgulă.";
        if (cleaned.Length > ActivityKeywords.MaximumLength) return $"Un cuvânt cheie poate avea cel mult {ActivityKeywords.MaximumLength} de caractere.";
        if (ActivityKeywords.Normalize(cleaned).Length < ActivityKeywords.MinimumLength) return $"Cuvântul cheie este prea scurt (cel puțin {ActivityKeywords.MinimumLength} litere sau cifre).";
        return null;
    }

    public static string Key(string cleaned) => ActivityKeywords.Normalize(cleaned);
}

/// <summary>The words the import searches for (the active ones), read without a user: used by the import itself, whoever runs it.</summary>
public static class ActivityKeywordStore
{
    public static async Task<IReadOnlyList<string>> ActiveAsync(IConfiguration configuration, CancellationToken cancellationToken)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("SELECT keyword FROM activity_import_keywords WHERE active = 1 ORDER BY keyword_key", connection);
        var words = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) words.Add(reader.GetString(0));
        return words;
    }
}

public sealed class MariaActivityKeywordService(IConfiguration configuration, IAccessControl access, IAuditTrail audit) : IActivityKeywordService
{
    private const string Select = "SELECT id, keyword, active, created_by, version FROM activity_import_keywords";

    private static ActivityKeyword Read(MySqlDataReader reader) => new(reader.GetInt32(0), reader.GetString(1), reader.GetBoolean(2), reader.GetString(3), reader.GetInt64(4));

    public async Task<IReadOnlyList<ActivityKeyword>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync("nomenclator.view", cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand(Select + " ORDER BY keyword_key", connection);
        var items = new List<ActivityKeyword>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) items.Add(Read(reader));
        return items;
    }

    private async Task<ActivityKeyword?> FindAsync(int id, CancellationToken cancellationToken)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand(Select + " WHERE id = @id", connection);
        command.Parameters.AddWithValue("@id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? Read(reader) : null;
    }

    private void EnsureDatabase()
    {
        if (!MariaDatabaseGuard.IsAllowedDatabase(configuration)) throw new ActivityImportException("Modificările sunt permise numai în baza BlazorStoc. Verifică numele bazei configurate.");
    }

    public async Task<ActivityKeyword> AddAsync(string keyword, CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync("nomenclator.add", cancellationToken).ConfigureAwait(false);
        if (ActivityKeywordRules.Validate(keyword, out var cleaned) is { } error) throw new ActivityImportException(error);
        EnsureDatabase();
        var actor = (await RepositoryAudit.ActorAsync(access, cancellationToken).ConfigureAwait(false)).Username;
        int id;
        await using (var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false))
        {
            await using var command = new MySqlCommand("INSERT INTO activity_import_keywords (keyword, keyword_key, active, created_by, created_utc, version) VALUES (@keyword, @key, 1, @by, @now, 0)", connection);
            command.Parameters.AddWithValue("@keyword", cleaned);
            command.Parameters.AddWithValue("@key", ActivityKeywordRules.Key(cleaned));
            command.Parameters.AddWithValue("@by", actor);
            command.Parameters.AddWithValue("@now", DateTime.UtcNow);
            try { await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false); }
            catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry) { throw new ActivityImportException("Cuvântul cheie există deja (fără diferență de litere mari sau mici, semne sau diacritice)."); }
            id = checked((int)command.LastInsertedId);
        }
        await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.ActivityImport, AuditActions.AddActivityKeyword, id.ToString(), "Cuvânt cheie mentenanță ActivityApp",
            AuditDetails.Identification(("Cuvânt cheie", cleaned)), "", cancellationToken).ConfigureAwait(false);
        return (await FindAsync(id, cancellationToken).ConfigureAwait(false))!;
    }

    public async Task<ActivityKeyword> RenameAsync(ActivityKeyword original, string keyword, CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync("nomenclator.edit", cancellationToken).ConfigureAwait(false);
        if (ActivityKeywordRules.Validate(keyword, out var cleaned) is { } error) throw new ActivityImportException(error);
        EnsureDatabase();
        await using (var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false))
        {
            await using var command = new MySqlCommand("UPDATE activity_import_keywords SET keyword=@keyword, keyword_key=@key, version=version+1 WHERE id=@id AND version=@version", connection);
            command.Parameters.AddWithValue("@keyword", cleaned);
            command.Parameters.AddWithValue("@key", ActivityKeywordRules.Key(cleaned));
            command.Parameters.AddWithValue("@id", original.Id);
            command.Parameters.AddWithValue("@version", original.Version);
            int changed;
            try { changed = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false); }
            catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry) { throw new ActivityImportException("Cuvântul cheie există deja (fără diferență de litere mari sau mici, semne sau diacritice)."); }
            if (changed != 1) throw new ActivityImportException("Cuvântul cheie s-a schimbat între timp. Actualizează lista și reia operația.");
        }
        await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.ActivityImport, AuditActions.EditActivityKeyword, original.Id.ToString(), "Cuvânt cheie mentenanță ActivityApp",
            AuditDetails.Changes(new AuditChange("Cuvânt cheie", original.Keyword, cleaned)), "Redenumire cuvânt cheie", cancellationToken).ConfigureAwait(false);
        return (await FindAsync(original.Id, cancellationToken).ConfigureAwait(false))!;
    }

    public async Task<ActivityKeyword> SetActiveAsync(ActivityKeyword original, bool active, CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync("nomenclator.edit", cancellationToken).ConfigureAwait(false);
        EnsureDatabase();
        if (original.Active != active)
        {
            await using (var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false))
            {
                await using var command = new MySqlCommand("UPDATE activity_import_keywords SET active=@active, version=version+1 WHERE id=@id AND version=@version", connection);
                command.Parameters.AddWithValue("@active", active);
                command.Parameters.AddWithValue("@id", original.Id);
                command.Parameters.AddWithValue("@version", original.Version);
                if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1) throw new ActivityImportException("Cuvântul cheie s-a schimbat între timp. Actualizează lista și reia operația.");
            }
            await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.ActivityImport, AuditActions.EditActivityKeyword, original.Id.ToString(), "Cuvânt cheie mentenanță ActivityApp",
                AuditDetails.Changes(new AuditChange("Activ", original.Active ? "da" : "nu", active ? "da" : "nu")), active ? "Activare cuvânt cheie" : "Dezactivare cuvânt cheie", cancellationToken).ConfigureAwait(false);
        }
        return (await FindAsync(original.Id, cancellationToken).ConfigureAwait(false))!;
    }

    public async Task DeleteAsync(ActivityKeyword original, CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync("nomenclator.delete", cancellationToken).ConfigureAwait(false);
        EnsureDatabase();
        await using (var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false))
        {
            await using var command = new MySqlCommand("DELETE FROM activity_import_keywords WHERE id=@id AND version=@version", connection);
            command.Parameters.AddWithValue("@id", original.Id);
            command.Parameters.AddWithValue("@version", original.Version);
            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1) throw new ActivityImportException("Cuvântul cheie s-a schimbat sau a fost șters între timp. Actualizează lista.");
        }
        await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.ActivityImport, AuditActions.DeleteActivityKeyword, original.Id.ToString(), "Cuvânt cheie mentenanță ActivityApp",
            AuditDetails.Identification(("Cuvânt cheie", original.Keyword), ("Era activ", original.Active ? "da" : "nu")), "", cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class GuardedActivityKeywordService(IActivityKeywordService inner, ModuleRegistry modules) : IActivityKeywordService
{
    private const string Id = ActivityImportModule.ModuleId;
    public Task<IReadOnlyList<ActivityKeyword>> GetAllAsync(CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.GetAllAsync(cancellationToken));
    public Task<ActivityKeyword> AddAsync(string keyword, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.AddAsync(keyword, cancellationToken));
    public Task<ActivityKeyword> RenameAsync(ActivityKeyword original, string keyword, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.RenameAsync(original, keyword, cancellationToken));
    public Task<ActivityKeyword> SetActiveAsync(ActivityKeyword original, bool active, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.SetActiveAsync(original, active, cancellationToken));
    public Task DeleteAsync(ActivityKeyword original, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.DeleteAsync(original, cancellationToken));
}

internal sealed class UnavailableActivityKeywordService : IActivityKeywordService
{
    private static Task<T> Out<T>() => Task.FromException<T>(new ModuleUnavailableException(ActivityImportModule.ModuleId));
    public Task<IReadOnlyList<ActivityKeyword>> GetAllAsync(CancellationToken cancellationToken = default) => Out<IReadOnlyList<ActivityKeyword>>();
    public Task<ActivityKeyword> AddAsync(string keyword, CancellationToken cancellationToken = default) => Out<ActivityKeyword>();
    public Task<ActivityKeyword> RenameAsync(ActivityKeyword original, string keyword, CancellationToken cancellationToken = default) => Out<ActivityKeyword>();
    public Task<ActivityKeyword> SetActiveAsync(ActivityKeyword original, bool active, CancellationToken cancellationToken = default) => Out<ActivityKeyword>();
    public Task DeleteAsync(ActivityKeyword original, CancellationToken cancellationToken = default) => Out<object>();
}
