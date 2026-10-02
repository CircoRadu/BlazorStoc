using System.Globalization;
using MySqlConnector;

namespace BlazorStoc.Services;

public sealed class InvoiceTemplateOperationException(string message) : Exception(message);

// A saved template without its definition (for lists). VersionNumber counts the saved versions of the layout (1, 2, 3 ...);
// Version is the row's concurrency counter.
public sealed record InvoiceTemplateInfo(int Id, string Name, string SupplierName, string SupplierCui, string SourceKind, int VersionNumber, long Version,
    string CreatedBy, DateTime CreatedUtc, string UpdatedBy, DateTime UpdatedUtc);

public sealed record InvoiceTemplateRecord(InvoiceTemplateInfo Info, InvoiceTemplateDefinition Definition);

// The invoice a template was made from, kept with it: shown again when the template is edited. Content is the PDF itself.
public sealed record InvoiceTemplateModel(string FileName, byte[] Content, string Sha256, int VersionNumber, string CreatedBy, DateTime CreatedUtc);

public sealed record InvoiceTemplateVersionInfo(int VersionNumber, string Note, string CreatedBy, DateTime CreatedUtc);

public sealed class InvoiceTemplateInput
{
    public string Name { get; set; } = "";
    public string SupplierName { get; set; } = "";
    public string SupplierCui { get; set; } = "";
    public string Note { get; set; } = "";
    public InvoiceTemplateDefinition? Definition { get; set; }
    // The invoice the template was made from (PDF), saved with the template; null keeps the model already saved.
    public string ModelFileName { get; set; } = "";
    public byte[]? ModelContent { get; set; }
}

public static class InvoiceTemplateRules
{
    public const int MaxNameLength = 120;
    public const int MaxSupplierNameLength = 200;
    public const int MaxNoteLength = 300;
    public const string NameRequiredMessage = "Denumirea șablonului este obligatorie.";
    public const string NameTooLongMessage = "Denumirea șablonului poate avea cel mult 120 de caractere.";
    public const string SupplierTooLongMessage = "Denumirea furnizorului poate avea cel mult 200 de caractere.";
    public const string DuplicateMessage = "Există deja un șablon cu această denumire pentru același furnizor.";
    public const string ConcurrentMessage = "Șablonul a fost modificat sau șters între timp. Actualizează lista și reîncearcă.";
    public const string DefinitionRequiredMessage = "Șablonul nu are conținut de salvat.";
    public const int MaxModelBytes = 15 * 1024 * 1024;
    public const string SupplierRequiredMessage = "Denumirea furnizorului (vânzătorului) este obligatorie: șablonul se leagă de furnizor.";
    public const string CuiRequiredMessage = "CUI/CIF-ul furnizorului este obligatoriu și trebuie să fie valid: șablonul se leagă de furnizor.";
    public const string ModelTooLargeMessage = "Factura model depășește 15 MB și nu poate fi salvată cu șablonul.";

    // Normalises and checks the text fields of an input: trimmed name and supplier, the supplier's tax id reduced to its digits.
    public static InvoiceTemplateInput Clean(InvoiceTemplateInput input)
    {
        var name = (input.Name ?? "").Trim();
        if (name.Length == 0) throw new InvoiceTemplateOperationException(NameRequiredMessage);
        if (name.Length > MaxNameLength) throw new InvoiceTemplateOperationException(NameTooLongMessage);
        var supplier = (input.SupplierName ?? "").Trim();
        if (supplier.Length == 0) throw new InvoiceTemplateOperationException(SupplierRequiredMessage);
        if (supplier.Length > MaxSupplierNameLength) throw new InvoiceTemplateOperationException(SupplierTooLongMessage);
        var cui = InvoiceValues.NormalizeCui(input.SupplierCui);
        if (cui.Length == 0) throw new InvoiceTemplateOperationException(CuiRequiredMessage);
        if (input.ModelContent is { Length: > MaxModelBytes }) throw new InvoiceTemplateOperationException(ModelTooLargeMessage);
        var note = (input.Note ?? "").Trim();
        if (note.Length > MaxNoteLength) note = note[..MaxNoteLength];
        return new InvoiceTemplateInput
        {
            Name = name, SupplierName = supplier, SupplierCui = cui, Note = note, Definition = input.Definition,
            ModelFileName = (input.ModelFileName ?? "").Trim() is { Length: > 0 } modelName ? (modelName.Length > 255 ? modelName[..255] : modelName) : "model.pdf", ModelContent = input.ModelContent
        };
    }

    // Two names are the same when they differ only in letter case, spaces or diacritics.
    public static bool SameName(string left, string right) => InvoiceValues.Normalize(left) == InvoiceValues.Normalize(right);

    public static string Describe(InvoiceTemplateDefinition definition) =>
        $"sursă: {(definition.SourceKind == InvoiceSources.Ocr ? "OCR" : "text")}; câmpuri folosite: {definition.UsedFieldCount}; coloane folosite: {definition.UsedColumnCount}" + ((definition.ProductDescription ?? "").Length > 0 ? $"; descriere produs: {definition.ProductDescription}" : "");
}

public interface IInvoiceTemplateStore
{
    Task<IReadOnlyList<InvoiceTemplateInfo>> ListAsync(CancellationToken cancellationToken = default);
    Task<InvoiceTemplateRecord?> GetAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InvoiceTemplateRecord>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InvoiceTemplateVersionInfo>> GetVersionsAsync(int id, CancellationToken cancellationToken = default);
    Task<InvoiceTemplateModel?> GetModelAsync(int id, CancellationToken cancellationToken = default);
    Task<InvoiceTemplateRecord> CreateAsync(InvoiceTemplateInput input, string actor, CancellationToken cancellationToken = default);
    Task<InvoiceTemplateRecord> SaveNewVersionAsync(InvoiceTemplateInfo original, InvoiceTemplateInput input, string actor, CancellationToken cancellationToken = default);
    Task<InvoiceTemplateInfo> UpdateDetailsAsync(InvoiceTemplateInfo original, InvoiceTemplateInput input, string actor, CancellationToken cancellationToken = default);
    Task DeleteAsync(InvoiceTemplateInfo original, CancellationToken cancellationToken = default);
}

// Tables `invoice_templates` (the current definition of each template) and `invoice_template_versions` (every saved version), created by
// migration 10 (MariaSchemaMigrations); this class never alters the schema.
public sealed class MariaInvoiceTemplateStore(IConfiguration configuration) : IInvoiceTemplateStore
{
    private const string InfoColumns = "id, name, supplier_name, supplier_cui, source_kind, version_number, version, created_by, created_utc, updated_by, updated_utc";

    private MySqlConnection CreateConnection() => DatabaseConnections.Create(configuration);

    public async Task<IReadOnlyList<InvoiceTemplateInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand($"SELECT {InfoColumns} FROM invoice_templates ORDER BY supplier_name, name, id", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<InvoiceTemplateInfo>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(ReadInfo(reader));
        return result;
    }

    public async Task<InvoiceTemplateRecord?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand($"SELECT {InfoColumns}, definition FROM invoice_templates WHERE id=@id", connection);
        command.Parameters.AddWithValue("@id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadRecord(reader) : null;
    }

    public async Task<IReadOnlyList<InvoiceTemplateRecord>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand($"SELECT {InfoColumns}, definition FROM invoice_templates ORDER BY supplier_name, name, id", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<InvoiceTemplateRecord>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(ReadRecord(reader));
        return result;
    }

    public async Task<IReadOnlyList<InvoiceTemplateVersionInfo>> GetVersionsAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("SELECT version_number, note, created_by, created_utc FROM invoice_template_versions WHERE template_id=@id ORDER BY version_number DESC", connection);
        command.Parameters.AddWithValue("@id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<InvoiceTemplateVersionInfo>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new InvoiceTemplateVersionInfo(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), MariaTimeText.Parse(reader.GetString(3))));
        return result;
    }

    public async Task<InvoiceTemplateModel?> GetModelAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("SELECT file_name, content, sha256, version_number, created_by, created_utc FROM invoice_template_models WHERE template_id=@id ORDER BY id DESC LIMIT 1", connection);
        command.Parameters.AddWithValue("@id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        return new InvoiceTemplateModel(reader.GetString(0), (byte[])reader["content"], reader.GetString(2), reader.GetInt32(3), reader.GetString(4), MariaTimeText.Parse(reader.GetString(5)));
    }

    public async Task<InvoiceTemplateRecord> CreateAsync(InvoiceTemplateInput input, string actor, CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        var clean = InvoiceTemplateRules.Clean(input);
        var definition = clean.Definition ?? throw new InvoiceTemplateOperationException(InvoiceTemplateRules.DefinitionRequiredMessage);
        var json = InvoiceTemplateJson.Serialize(definition);
        var now = MariaTimeText.Format(DateTime.UtcNow);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            long id;
            await using (var insert = new MySqlCommand("""
                INSERT INTO invoice_templates (name, supplier_name, supplier_cui, source_kind, version_number, definition, created_by, created_utc, updated_by, updated_utc, version)
                VALUES (@name, @supplier, @cui, @source, 1, @definition, @actor, @now, @actor, @now, 0)
                """, connection, transaction))
            {
                Fill(insert, clean, definition, json);
                insert.Parameters.AddWithValue("@actor", actor);
                insert.Parameters.AddWithValue("@now", now);
                try { await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false); }
                catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry) { throw new InvoiceTemplateOperationException(InvoiceTemplateRules.DuplicateMessage); }
                id = insert.LastInsertedId;
            }
            await InsertVersionAsync(connection, transaction, id, 1, json, clean.Note, actor, now, cancellationToken).ConfigureAwait(false);
            await InsertModelAsync(connection, transaction, id, 1, clean, actor, now, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new InvoiceTemplateRecord(new InvoiceTemplateInfo(checked((int)id), clean.Name, clean.SupplierName, clean.SupplierCui, definition.SourceKind, 1, 0,
                actor, MariaTimeText.Parse(now), actor, MariaTimeText.Parse(now)), definition);
        }
        catch
        {
            if (transaction.Connection is not null) await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<InvoiceTemplateRecord> SaveNewVersionAsync(InvoiceTemplateInfo original, InvoiceTemplateInput input, string actor, CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        var clean = InvoiceTemplateRules.Clean(input);
        var definition = clean.Definition ?? throw new InvoiceTemplateOperationException(InvoiceTemplateRules.DefinitionRequiredMessage);
        var json = InvoiceTemplateJson.Serialize(definition);
        var now = MariaTimeText.Format(DateTime.UtcNow);
        var versionNumber = checked(original.VersionNumber + 1);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using (var update = new MySqlCommand("""
                UPDATE invoice_templates SET name=@name, supplier_name=@supplier, supplier_cui=@cui, source_kind=@source, version_number=@number, definition=@definition,
                    updated_by=@actor, updated_utc=@now, version=version+1
                WHERE id=@id AND version=@oldVersion
                """, connection, transaction))
            {
                Fill(update, clean, definition, json);
                update.Parameters.AddWithValue("@number", versionNumber);
                update.Parameters.AddWithValue("@actor", actor);
                update.Parameters.AddWithValue("@now", now);
                update.Parameters.AddWithValue("@id", original.Id);
                update.Parameters.AddWithValue("@oldVersion", original.Version);
                int changed;
                try { changed = await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false); }
                catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry) { throw new InvoiceTemplateOperationException(InvoiceTemplateRules.DuplicateMessage); }
                if (changed != 1) throw new InvoiceTemplateOperationException(InvoiceTemplateRules.ConcurrentMessage);
            }
            await InsertVersionAsync(connection, transaction, original.Id, versionNumber, json, clean.Note, actor, now, cancellationToken).ConfigureAwait(false);
            await InsertModelAsync(connection, transaction, original.Id, versionNumber, clean, actor, now, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new InvoiceTemplateRecord(original with
            {
                Name = clean.Name, SupplierName = clean.SupplierName, SupplierCui = clean.SupplierCui, SourceKind = definition.SourceKind, VersionNumber = versionNumber,
                Version = original.Version + 1, UpdatedBy = actor, UpdatedUtc = MariaTimeText.Parse(now)
            }, definition);
        }
        catch
        {
            if (transaction.Connection is not null) await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<InvoiceTemplateInfo> UpdateDetailsAsync(InvoiceTemplateInfo original, InvoiceTemplateInput input, string actor, CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        var clean = InvoiceTemplateRules.Clean(input);
        var now = MariaTimeText.Format(DateTime.UtcNow);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var update = new MySqlCommand("""
            UPDATE invoice_templates SET name=@name, supplier_name=@supplier, supplier_cui=@cui, updated_by=@actor, updated_utc=@now, version=version+1
            WHERE id=@id AND version=@oldVersion
            """, connection);
        update.Parameters.AddWithValue("@name", clean.Name);
        update.Parameters.AddWithValue("@supplier", clean.SupplierName);
        update.Parameters.AddWithValue("@cui", clean.SupplierCui);
        update.Parameters.AddWithValue("@actor", actor);
        update.Parameters.AddWithValue("@now", now);
        update.Parameters.AddWithValue("@id", original.Id);
        update.Parameters.AddWithValue("@oldVersion", original.Version);
        int changed;
        try { changed = await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false); }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry) { throw new InvoiceTemplateOperationException(InvoiceTemplateRules.DuplicateMessage); }
        if (changed != 1) throw new InvoiceTemplateOperationException(InvoiceTemplateRules.ConcurrentMessage);
        return original with
        {
            Name = clean.Name, SupplierName = clean.SupplierName, SupplierCui = clean.SupplierCui, Version = original.Version + 1, UpdatedBy = actor, UpdatedUtc = MariaTimeText.Parse(now)
        };
    }

    public async Task DeleteAsync(InvoiceTemplateInfo original, CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        // The versions go with the template (foreign key ON DELETE CASCADE).
        await using var command = new MySqlCommand("DELETE FROM invoice_templates WHERE id=@id AND version=@version", connection);
        command.Parameters.AddWithValue("@id", original.Id);
        command.Parameters.AddWithValue("@version", original.Version);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1) throw new InvoiceTemplateOperationException(InvoiceTemplateRules.ConcurrentMessage);
    }

    private static void Fill(MySqlCommand command, InvoiceTemplateInput clean, InvoiceTemplateDefinition definition, string json)
    {
        command.Parameters.AddWithValue("@name", clean.Name);
        command.Parameters.AddWithValue("@supplier", clean.SupplierName);
        command.Parameters.AddWithValue("@cui", clean.SupplierCui);
        command.Parameters.AddWithValue("@source", definition.SourceKind);
        command.Parameters.AddWithValue("@definition", json);
    }

    private static async Task InsertVersionAsync(MySqlConnection connection, MySqlTransaction transaction, long templateId, int number, string json, string note, string actor,
        string now, CancellationToken cancellationToken)
    {
        await using var command = new MySqlCommand("""
            INSERT INTO invoice_template_versions (template_id, version_number, definition, note, created_by, created_utc)
            VALUES (@id, @number, @definition, @note, @actor, @now)
            """, connection, transaction);
        command.Parameters.AddWithValue("@id", templateId);
        command.Parameters.AddWithValue("@number", number);
        command.Parameters.AddWithValue("@definition", json);
        command.Parameters.AddWithValue("@note", note);
        command.Parameters.AddWithValue("@actor", actor);
        command.Parameters.AddWithValue("@now", now);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    // The model is kept when it is a different file from the last saved one (the same file saved again adds nothing).
    private static async Task InsertModelAsync(MySqlConnection connection, MySqlTransaction transaction, long templateId, int versionNumber, InvoiceTemplateInput clean, string actor,
        string now, CancellationToken cancellationToken)
    {
        if (clean.ModelContent is not { Length: > 0 } content) return;
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content)).ToLowerInvariant();
        await using (var latest = new MySqlCommand("SELECT sha256 FROM invoice_template_models WHERE template_id=@id ORDER BY id DESC LIMIT 1", connection, transaction))
        {
            latest.Parameters.AddWithValue("@id", templateId);
            if (await latest.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is string last && last == hash) return;
        }
        await using var command = new MySqlCommand("""
            INSERT INTO invoice_template_models (template_id, version_number, file_name, byte_length, sha256, content, created_by, created_utc)
            VALUES (@id, @number, @file, @length, @hash, @content, @actor, @now)
            """, connection, transaction);
        command.Parameters.AddWithValue("@id", templateId);
        command.Parameters.AddWithValue("@number", versionNumber);
        command.Parameters.AddWithValue("@file", clean.ModelFileName);
        command.Parameters.AddWithValue("@length", content.LongLength);
        command.Parameters.AddWithValue("@hash", hash);
        command.Parameters.AddWithValue("@content", content);
        command.Parameters.AddWithValue("@actor", actor);
        command.Parameters.AddWithValue("@now", now);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private void EnsureWritable()
    {
        if (!MariaDatabaseGuard.IsAllowedDatabase(configuration))
            throw new InvoiceTemplateOperationException("Modificările sunt permise numai în baza BlazorStoc.");
    }

    private static InvoiceTemplateInfo ReadInfo(MySqlDataReader reader) =>
        new(checked((int)reader.GetInt64(0)), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetInt32(5), reader.GetInt64(6),
            reader.GetString(7), MariaTimeText.Parse(reader.GetString(8)), reader.GetString(9), MariaTimeText.Parse(reader.GetString(10)));

    private static InvoiceTemplateRecord ReadRecord(MySqlDataReader reader) =>
        new(ReadInfo(reader), InvoiceTemplateJson.Deserialize(reader.GetString(11)));
}

// Saves, changes and deletes templates for the signed-in administrator and writes the journal: every operation has its own action
// (see AuditActions), with what changed in the details.
public interface IInvoiceTemplateService
{
    Task<IReadOnlyList<InvoiceTemplateInfo>> ListAsync(CancellationToken cancellationToken = default);
    Task<InvoiceTemplateRecord?> GetAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InvoiceTemplateRecord>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InvoiceTemplateVersionInfo>> GetVersionsAsync(int id, CancellationToken cancellationToken = default);
    Task<InvoiceTemplateModel?> GetModelAsync(int id, CancellationToken cancellationToken = default);
    Task<InvoiceTemplateRecord> CreateAsync(InvoiceTemplateInput input, CancellationToken cancellationToken = default);
    Task<InvoiceTemplateRecord> SaveNewVersionAsync(InvoiceTemplateInfo original, InvoiceTemplateInput input, CancellationToken cancellationToken = default);
    Task<InvoiceTemplateInfo> UpdateDetailsAsync(InvoiceTemplateInfo original, InvoiceTemplateInput input, CancellationToken cancellationToken = default);
    Task DeleteAsync(InvoiceTemplateInfo original, string reason, CancellationToken cancellationToken = default);
}

public sealed class InvoiceTemplateService(IInvoiceTemplateStore store, IAccessControl access, IAuditTrail audit) : IInvoiceTemplateService
{
    public Task<IReadOnlyList<InvoiceTemplateInfo>> ListAsync(CancellationToken cancellationToken = default) => store.ListAsync(cancellationToken);
    public Task<InvoiceTemplateRecord?> GetAsync(int id, CancellationToken cancellationToken = default) => store.GetAsync(id, cancellationToken);
    public Task<IReadOnlyList<InvoiceTemplateRecord>> GetAllAsync(CancellationToken cancellationToken = default) => store.GetAllAsync(cancellationToken);
    public Task<IReadOnlyList<InvoiceTemplateVersionInfo>> GetVersionsAsync(int id, CancellationToken cancellationToken = default) => store.GetVersionsAsync(id, cancellationToken);
    public Task<InvoiceTemplateModel?> GetModelAsync(int id, CancellationToken cancellationToken = default) => store.GetModelAsync(id, cancellationToken);

    public async Task<InvoiceTemplateRecord> CreateAsync(InvoiceTemplateInput input, CancellationToken cancellationToken = default)
    {
        await access.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var actor = await access.GetUsernameAsync(cancellationToken).ConfigureAwait(false) ?? "necunoscut";
        await CheckUniqueNameAsync(input, null, cancellationToken).ConfigureAwait(false);
        var created = await store.CreateAsync(input, actor, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.InvoiceTemplate, AuditActions.CreateInvoiceTemplate, Id(created.Info), created.Info.Name,
            AuditDetails.Identification(("Furnizor", Supplier(created.Info)), ("Versiune", "1"), ("Conținut", InvoiceTemplateRules.Describe(created.Definition)), ("Factură model", input.ModelContent is null ? "—" : InvoiceTemplateRules.Clean(input).ModelFileName)),
            string.Empty, cancellationToken).ConfigureAwait(false);
        return created;
    }

    public async Task<InvoiceTemplateRecord> SaveNewVersionAsync(InvoiceTemplateInfo original, InvoiceTemplateInput input, CancellationToken cancellationToken = default)
    {
        await access.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var actor = await access.GetUsernameAsync(cancellationToken).ConfigureAwait(false) ?? "necunoscut";
        await CheckUniqueNameAsync(input, original.Id, cancellationToken).ConfigureAwait(false);
        var before = await store.GetAsync(original.Id, cancellationToken).ConfigureAwait(false);
        var modelBefore = await store.GetModelAsync(original.Id, cancellationToken).ConfigureAwait(false);
        var saved = await store.SaveNewVersionAsync(original, input, actor, cancellationToken).ConfigureAwait(false);
        var changes = new List<AuditChange>
        {
            new("Versiune", original.VersionNumber.ToString(CultureInfo.InvariantCulture), saved.Info.VersionNumber.ToString(CultureInfo.InvariantCulture)),
            new("Denumire", original.Name, saved.Info.Name),
            new("Furnizor", Supplier(original), Supplier(saved.Info)),
            new("Conținut", before is null ? "" : InvoiceTemplateRules.Describe(before.Definition), InvoiceTemplateRules.Describe(saved.Definition)),
            new("Factură model", modelBefore?.FileName ?? "—", (await store.GetModelAsync(original.Id, cancellationToken).ConfigureAwait(false))?.FileName ?? "—")
        };
        await AuditRecorder.RecordEditAsync(audit, access, AuditEntities.InvoiceTemplate, Id(saved.Info), saved.Info.Name, changes, string.Empty, cancellationToken,
            AuditActions.EditInvoiceTemplate).ConfigureAwait(false);
        return saved;
    }

    public async Task<InvoiceTemplateInfo> UpdateDetailsAsync(InvoiceTemplateInfo original, InvoiceTemplateInput input, CancellationToken cancellationToken = default)
    {
        await access.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var actor = await access.GetUsernameAsync(cancellationToken).ConfigureAwait(false) ?? "necunoscut";
        await CheckUniqueNameAsync(input, original.Id, cancellationToken).ConfigureAwait(false);
        var saved = await store.UpdateDetailsAsync(original, input, actor, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordEditAsync(audit, access, AuditEntities.InvoiceTemplate, Id(saved), saved.Name,
            [new AuditChange("Denumire", original.Name, saved.Name), new AuditChange("Furnizor", Supplier(original), Supplier(saved))], string.Empty, cancellationToken,
            AuditActions.EditInvoiceTemplateDetails).ConfigureAwait(false);
        return saved;
    }

    public async Task DeleteAsync(InvoiceTemplateInfo original, string reason, CancellationToken cancellationToken = default)
    {
        await access.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        await store.DeleteAsync(original, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.InvoiceTemplate, AuditActions.DeleteInvoiceTemplate, Id(original), original.Name,
            AuditDetails.Identification(("Furnizor", Supplier(original)), ("Versiuni salvate", original.VersionNumber.ToString(CultureInfo.InvariantCulture))),
            (reason ?? string.Empty).Trim(), cancellationToken).ConfigureAwait(false);
    }

    // A name is unique per supplier without regard to letter case, spaces or diacritics (the database key is case-sensitive).
    private async Task CheckUniqueNameAsync(InvoiceTemplateInput input, int? ownId, CancellationToken cancellationToken)
    {
        var clean = InvoiceTemplateRules.Clean(input);
        var existing = await store.ListAsync(cancellationToken).ConfigureAwait(false);
        if (existing.Any(item => item.Id != ownId && item.SupplierCui == clean.SupplierCui && InvoiceTemplateRules.SameName(item.Name, clean.Name)))
            throw new InvoiceTemplateOperationException(InvoiceTemplateRules.DuplicateMessage);
    }

    private static string Id(InvoiceTemplateInfo info) => info.Id.ToString(CultureInfo.InvariantCulture);
    private static string Supplier(InvoiceTemplateInfo info) =>
        info.SupplierName.Length == 0 && info.SupplierCui.Length == 0 ? "—" : $"{info.SupplierName} (CUI {(info.SupplierCui.Length == 0 ? "necunoscut" : info.SupplierCui)})".Trim();
}
