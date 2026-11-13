using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using BlazorStoc.Services;
using MySqlConnector;

namespace BlazorStoc.Modules.BugReports;

// Raportare erori: every signed-in user reports a problem from the menu entry above "Ajutor" (title, description, severity, the automatic capture of the page under the
// popup and the pictures pasted with Ctrl+V); the reports stay in the application. The administrator (Setari -> Rapoarte erori) follows them, changes their state and
// exports them as a ZIP the browser downloads. Nothing is sent anywhere.

public static class BugReportRules
{
    public const string SeverityBlocking = "Blocant", SeverityImportant = "Important", SeverityMinor = "Minor", SeveritySuggestion = "Sugestie";
    public static readonly IReadOnlyList<string> Severities = [SeverityBlocking, SeverityImportant, SeverityMinor, SeveritySuggestion];

    public const string StateNew = "Nou", StateAnalysis = "În analiză", StateResolved = "Rezolvat", StateRejected = "Respins";
    public static readonly IReadOnlyList<string> States = [StateNew, StateAnalysis, StateResolved, StateRejected];

    public const int MaxTitle = 200, MaxDescription = 8000, MaxNote = 4000, MaxTech = 60_000, MaxImages = 12, MaxImageBytes = 5 * 1024 * 1024;

    public const string KindScreenshot = "screenshot", KindPasted = "pasted", KindFile = "file";

    public static string Number(int id) => "BUG-" + id.ToString("0000", CultureInfo.InvariantCulture);

    public static string Display(DateTime utc) => utc.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);

    /// <summary>The content type is taken from the bytes (a picture), never from what the browser declared.</summary>
    public static string? DetectImageType(byte[] bytes)
    {
        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) return "image/png";
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return "image/jpeg";
        if (bytes.Length >= 6 && bytes[0] == 'G' && bytes[1] == 'I' && bytes[2] == 'F') return "image/gif";
        if (bytes.Length >= 12 && bytes[0] == 'R' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == 'F' && bytes[8] == 'W' && bytes[9] == 'E' && bytes[10] == 'B' && bytes[11] == 'P') return "image/webp";
        return null;
    }

    public static string Extension(string contentType) => contentType switch { "image/png" => ".png", "image/jpeg" => ".jpg", "image/gif" => ".gif", "image/webp" => ".webp", _ => ".bin" };

    public static string SafeName(string? name, string fallback)
    {
        var cleaned = new string((name ?? "").Where(c => !Path.GetInvalidFileNameChars().Contains(c) && c != ' ').ToArray());
        return cleaned.Length == 0 ? fallback : cleaned.Length > 80 ? cleaned[..80] : cleaned;
    }
}

public sealed record BugReport(int Id, string Number, string Title, string Description, string Severity, string State, string Route, string PageTitle, string AppVersion,
    string UserName, string UserRole, string TechJson, string AdminNote, DateTime CreatedUtc, string UpdatedBy, DateTime? UpdatedUtc, DateTime? ResolvedUtc, int FileCount);

public sealed record BugReportFile(int Id, int ReportId, string Kind, string FileName, string ContentType, int SizeBytes, DateTime CreatedUtc);

public sealed record BugReportFileContent(byte[] Content, string ContentType, string FileName);

public sealed record BugReportImage(string Kind, string FileName, byte[] Content);

public sealed record BugReportInput(string Title, string Description, string Severity, string Route, string PageTitle, string TechJson, IReadOnlyList<BugReportImage> Images);

public sealed record BugReportQuery(string? State = null, string? Severity = null, string? User = null, DateOnly? From = null, DateOnly? To = null, bool? Closed = null);

public sealed record BugReportPage(IReadOnlyList<BugReport> Items, int Total);

public sealed record BugReportExportResult(string FileName, byte[] Content, int Reports);

/// <summary>A report that needs the attention of a developer (the tab Bugs of Notificari) with the reason.</summary>
public sealed record BugReportAlert(BugReport Report, string Reason);

/// <summary>An answer of the module for the user (a missing title, a picture that is not one), not a failure of the module.</summary>
public sealed class BugReportException(string message) : Exception(message), IExpectedAnswer;

public interface IBugReportService
{
    /// <summary>Any signed-in user. Saves the report with its pictures and records it in the journal.</summary>
    Task<BugReport> CreateAsync(BugReportInput input, CancellationToken cancellationToken = default);
    /// <summary>The reports of the signed-in user, the newest first.</summary>
    Task<IReadOnlyList<BugReport>> GetMineAsync(CancellationToken cancellationToken = default);
    /// <summary>Administrator (setari-bug-raport.view): the reports that match the filters, the newest first.</summary>
    Task<BugReportPage> QueryAsync(BugReportQuery query, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<int> CountNewAsync(CancellationToken cancellationToken = default);
    /// <summary>How many reports are not resolved or rejected (the badge of the tab Erori of Setari -> Dezvoltare); 0 without the right to see the reports.</summary>
    Task<int> CountOpenAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
    /// <summary>The pictures of a report; the author and the administrator only.</summary>
    Task<IReadOnlyList<BugReportFile>> GetFilesAsync(int reportId, CancellationToken cancellationToken = default);
    Task<BugReportFileContent?> GetFileContentAsync(int fileId, CancellationToken cancellationToken = default);
    Task<BugReport> ChangeStateAsync(int reportId, string state, string note, CancellationToken cancellationToken = default);
    /// <summary>Administrator (setari-bug-raport.edit): corrects the title and the description of a report and adds more pictures to it (the pictures already there stay). Journaled as its own action.</summary>
    Task<BugReport> UpdateContentAsync(int reportId, string title, string description, IReadOnlyList<BugReportImage> addedImages, CancellationToken cancellationToken = default);
    Task DeleteAsync(int reportId, CancellationToken cancellationToken = default);
    /// <summary>Administrator (setari-bug-raport.edit): deletes every report that is resolved or rejected (the table of the resolved ones), with their pictures; returns how many. One journal line.</summary>
    Task<int> DeleteClosedAsync(CancellationToken cancellationToken = default);
    /// <summary>The reports that wait for a developer (new ones; the tab Bugs of Notificari); empty for an account that is not a developer.</summary>
    Task<IReadOnlyList<BugReportAlert>> GetAlertsAsync(CancellationToken cancellationToken = default);
    /// <summary>Administrator (setari-bug-raport.export): the reports as a ZIP (a folder for each, an index.csv when there are several).</summary>
    Task<BugReportExportResult> ExportAsync(IReadOnlyList<int> reportIds, CancellationToken cancellationToken = default);
}

public sealed class MariaBugReportService(IConfiguration configuration, IAccessControl access, IAuditTrail audit, IChangeFeed? changeFeed = null, ChangeOrigin? origin = null) : IBugReportService
{
    private const string Columns = """
        r.id, r.number, r.title, r.description, r.severity, r.state, r.route, r.page_title, r.app_version, r.user_name, r.user_role, COALESCE(r.tech_json,''), r.admin_note,
        r.created_utc, r.updated_by, r.updated_utc, r.resolved_utc, (SELECT COUNT(*) FROM bug_report_files f WHERE f.report_id=r.id)
        """;

    private async Task<string> RequireUserAsync(CancellationToken cancellationToken) =>
        await access.GetUsernameAsync(cancellationToken).ConfigureAwait(false) is { Length: > 0 } user ? user : throw new AccessDeniedException("Trebuie să fii autentificat pentru a raporta o eroare.");

    private static BugReport Read(MySqlDataReader reader) => new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5),
        reader.GetString(6), reader.GetString(7), reader.GetString(8), reader.GetString(9), reader.GetString(10), reader.GetString(11), reader.GetString(12),
        MariaTimeText.Parse(reader.GetString(13)), reader.GetString(14), MariaTimeText.ParseOrNull(reader.GetString(15)), MariaTimeText.ParseOrNull(reader.GetString(16)), Convert.ToInt32(reader.GetValue(17), CultureInfo.InvariantCulture));

    public async Task<BugReport> CreateAsync(BugReportInput input, CancellationToken cancellationToken = default)
    {
        var user = await RequireUserAsync(cancellationToken).ConfigureAwait(false);
        var title = (input.Title ?? "").Trim();
        var description = (input.Description ?? "").Trim();
        if (title.Length == 0) throw new BugReportException("Scrie un titlu scurt al problemei.");
        if (title.Length > BugReportRules.MaxTitle) throw new BugReportException($"Titlul poate avea cel mult {BugReportRules.MaxTitle} de caractere.");
        if (description.Length == 0) throw new BugReportException("Descrie ce ai făcut, ce ai așteptat și ce s-a întâmplat.");
        if (description.Length > BugReportRules.MaxDescription) throw new BugReportException($"Descrierea poate avea cel mult {BugReportRules.MaxDescription:N0} de caractere.");
        if (!BugReportRules.Severities.Contains(input.Severity)) throw new BugReportException("Alege gravitatea problemei.");
        if (input.Images.Count > BugReportRules.MaxImages) throw new BugReportException($"Poți atașa cel mult {BugReportRules.MaxImages} imagini.");
        var images = new List<(BugReportImage Image, string Type)>();
        foreach (var image in input.Images)
        {
            if (image.Content.Length == 0 || image.Content.Length > BugReportRules.MaxImageBytes)
                throw new BugReportException($"Imaginea „{image.FileName}” trebuie să aibă cel mult {BugReportRules.MaxImageBytes / 1024 / 1024} MB.");
            var type = BugReportRules.DetectImageType(image.Content) ?? throw new BugReportException($"Fișierul „{image.FileName}” nu este o imagine (PNG, JPEG, GIF sau WebP).");
            images.Add((image, type));
        }
        var tech = input.TechJson ?? "";
        if (tech.Length > BugReportRules.MaxTech) tech = tech[..BugReportRules.MaxTech];
        var role = await access.IsAdministratorAsync(cancellationToken).ConfigureAwait(false) ? "Administrator" : "Utilizator";
        var now = DateTime.UtcNow;
        int id;
        await using (var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false))
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false))
        {
            await using (var insert = new MySqlCommand("""
                INSERT INTO bug_reports (title, description, severity, state, route, page_title, app_version, user_name, user_role, tech_json, admin_note, created_utc)
                VALUES (@title,@description,@severity,@state,@route,@pageTitle,@version,@user,@role,@tech,'',@now)
                """, connection, transaction))
            {
                insert.Parameters.AddWithValue("@title", title);
                insert.Parameters.AddWithValue("@description", description);
                insert.Parameters.AddWithValue("@severity", input.Severity);
                insert.Parameters.AddWithValue("@state", BugReportRules.StateNew);
                insert.Parameters.AddWithValue("@route", Trim(input.Route, 500));
                insert.Parameters.AddWithValue("@pageTitle", Trim(input.PageTitle, 300));
                insert.Parameters.AddWithValue("@version", AppVersion.Current);
                insert.Parameters.AddWithValue("@user", user);
                insert.Parameters.AddWithValue("@role", role);
                insert.Parameters.AddWithValue("@tech", tech);
                insert.Parameters.AddWithValue("@now", MariaTimeText.Format(now));
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                id = (int)insert.LastInsertedId;
            }
            await using (var number = new MySqlCommand("UPDATE bug_reports SET number=@number WHERE id=@id", connection, transaction))
            {
                number.Parameters.AddWithValue("@number", BugReportRules.Number(id));
                number.Parameters.AddWithValue("@id", id);
                await number.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            var index = 0;
            foreach (var (image, type) in images)
            {
                index++;
                await using var file = new MySqlCommand("""
                    INSERT INTO bug_report_files (report_id, kind, file_name, content_type, size_bytes, content, created_utc) VALUES (@report,@kind,@name,@type,@size,@content,@now)
                    """, connection, transaction);
                file.Parameters.AddWithValue("@report", id);
                file.Parameters.AddWithValue("@kind", image.Kind is BugReportRules.KindScreenshot or BugReportRules.KindFile ? image.Kind : BugReportRules.KindPasted);
                file.Parameters.AddWithValue("@name", BugReportRules.SafeName(image.FileName, $"imagine-{index}{BugReportRules.Extension(type)}"));
                file.Parameters.AddWithValue("@type", type);
                file.Parameters.AddWithValue("@size", image.Content.Length);
                file.Parameters.AddWithValue("@content", image.Content);
                file.Parameters.AddWithValue("@now", MariaTimeText.Format(now));
                await file.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.BugReport, AuditActions.CreateBugReport, id.ToString(CultureInfo.InvariantCulture), BugReportRules.Number(id),
            $"Gravitate: {input.Severity}; pagina: {Trim(input.Route, 200)}; imagini: {images.Count}; titlu: {Trim(title, 120)}", "", cancellationToken).ConfigureAwait(false);
        Publish(AuditActions.CreateBugReport, id);
        return (await GetAsync(id, cancellationToken).ConfigureAwait(false))!;
    }

    private static string Trim(string? text, int max) => (text ?? "").Length > max ? text![..max] : text ?? "";

    private async Task<BugReport?> GetAsync(int id, CancellationToken cancellationToken)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand($"SELECT {Columns} FROM bug_reports r WHERE r.id=@id", connection);
        command.Parameters.AddWithValue("@id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? Read(reader) : null;
    }

    public async Task<IReadOnlyList<BugReport>> GetMineAsync(CancellationToken cancellationToken = default)
    {
        var user = await RequireUserAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand($"SELECT {Columns} FROM bug_reports r WHERE r.user_name=@user ORDER BY r.id DESC LIMIT 200", connection);
        command.Parameters.AddWithValue("@user", user);
        var items = new List<BugReport>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) items.Add(Read(reader));
        return items;
    }

    private static (string Where, List<MySqlParameter> Parameters) Filter(BugReportQuery query)
    {
        var clauses = new List<string>();
        var parameters = new List<MySqlParameter>();
        if (!string.IsNullOrEmpty(query.State)) { clauses.Add("r.state=@state"); parameters.Add(new("@state", query.State)); }
        if (query.Closed is { } closed)
        {
            clauses.Add(closed ? "r.state IN (@sResolved,@sRejected)" : "r.state NOT IN (@sResolved,@sRejected)");
            parameters.Add(new("@sResolved", BugReportRules.StateResolved)); parameters.Add(new("@sRejected", BugReportRules.StateRejected));
        }
        if (!string.IsNullOrEmpty(query.Severity)) { clauses.Add("r.severity=@severity"); parameters.Add(new("@severity", query.Severity)); }
        if (!string.IsNullOrWhiteSpace(query.User)) { clauses.Add("LOCATE(LOWER(@user), LOWER(r.user_name))>0"); parameters.Add(new("@user", query.User.Trim())); }
        if (query.From is { } from) { clauses.Add("r.created_utc>=@from"); parameters.Add(new("@from", from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))); }
        if (query.To is { } to) { clauses.Add("r.created_utc<@to"); parameters.Add(new("@to", to.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))); }
        return (clauses.Count == 0 ? "" : "WHERE " + string.Join(" AND ", clauses), parameters);
    }

public async Task<IReadOnlyList<BugReportAlert>> GetAlertsAsync(CancellationToken cancellationToken = default)    {        if (!await access.HasAsync("setari-bug-raport.view", cancellationToken).ConfigureAwait(false)) return [];        var page = await QueryAsync(new BugReportQuery(Closed: false), 1, 100, cancellationToken).ConfigureAwait(false);        return [.. page.Items.Select(report => new BugReportAlert(report, report.State == BugReportRules.StateNew ? "Raport nou" : $"Stare: {report.State}"))];    }
    public async Task<BugReportPage> QueryAsync(BugReportQuery query, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync("setari-bug-raport.view", cancellationToken).ConfigureAwait(false);
        var (where, parameters) = Filter(query);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        int total;
        await using (var count = new MySqlCommand($"SELECT COUNT(*) FROM bug_reports r {where}", connection))
        {
            foreach (var parameter in parameters) count.Parameters.Add(Clone(parameter));
            total = Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
        }
        var limit = pageSize <= 0 ? "" : $"LIMIT {pageSize} OFFSET {Math.Max(0, page - 1) * pageSize}";
        await using var command = new MySqlCommand($"SELECT {Columns} FROM bug_reports r {where} ORDER BY r.id DESC {limit}", connection);
        foreach (var parameter in parameters) command.Parameters.Add(Clone(parameter));
        var items = new List<BugReport>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) items.Add(Read(reader));
        return new BugReportPage(items, total);
    }

    private static MySqlParameter Clone(MySqlParameter parameter) => new(parameter.ParameterName, parameter.Value);

    // The tab badges and the open lists of the other sessions follow (the identifiers only).
    private void Publish(string action, int id) =>
        changeFeed?.Publish(new ChangeEvent(AuditEntities.BugReport, action, id.ToString(CultureInfo.InvariantCulture), origin?.Id ?? Guid.Empty, DateTime.UtcNow));

    public async Task<int> CountOpenAsync(CancellationToken cancellationToken = default)
    {
        if (!await access.HasAsync("setari-bug-raport.view", cancellationToken).ConfigureAwait(false)) return 0;
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("SELECT COUNT(*) FROM bug_reports WHERE state NOT IN (@resolved, @rejected)", connection);
        command.Parameters.AddWithValue("@resolved", BugReportRules.StateResolved);
        command.Parameters.AddWithValue("@rejected", BugReportRules.StateRejected);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    public async Task<int> CountNewAsync(CancellationToken cancellationToken = default)
    {
        if (!await access.HasAsync("setari-bug-raport.view", cancellationToken).ConfigureAwait(false)) return 0;
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("SELECT COUNT(*) FROM bug_reports WHERE state=@state", connection);
        command.Parameters.AddWithValue("@state", BugReportRules.StateNew);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    // The author of the report and who may see the reports (the administrator) can read its pictures; nobody else.
    private async Task EnsureCanSeeAsync(int reportId, CancellationToken cancellationToken)
    {
        var user = await RequireUserAsync(cancellationToken).ConfigureAwait(false);
        if (await access.HasAsync("setari-bug-raport.view", cancellationToken).ConfigureAwait(false)) return;
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("SELECT COUNT(*) FROM bug_reports WHERE id=@id AND user_name=@user", connection);
        command.Parameters.AddWithValue("@id", reportId);
        command.Parameters.AddWithValue("@user", user);
        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture) == 0)
            throw new AccessDeniedException("Nu ai dreptul să vezi acest raport.");
    }

    public async Task<IReadOnlyList<BugReportFile>> GetFilesAsync(int reportId, CancellationToken cancellationToken = default)
    {
        await EnsureCanSeeAsync(reportId, cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("SELECT id, report_id, kind, file_name, content_type, size_bytes, created_utc FROM bug_report_files WHERE report_id=@id ORDER BY id", connection);
        command.Parameters.AddWithValue("@id", reportId);
        var files = new List<BugReportFile>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            files.Add(new(reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetInt32(5), MariaTimeText.Parse(reader.GetString(6))));
        return files;
    }

    public async Task<BugReportFileContent?> GetFileContentAsync(int fileId, CancellationToken cancellationToken = default)
    {
        int reportId;
        await using (var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false))
        await using (var owner = new MySqlCommand("SELECT report_id FROM bug_report_files WHERE id=@id", connection))
        {
            owner.Parameters.AddWithValue("@id", fileId);
            if (await owner.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not int found) return null;
            reportId = found;
        }
        await EnsureCanSeeAsync(reportId, cancellationToken).ConfigureAwait(false);
        await using var second = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("SELECT content, content_type, file_name FROM bug_report_files WHERE id=@id", second);
        command.Parameters.AddWithValue("@id", fileId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? new BugReportFileContent((byte[])reader.GetValue(0), reader.GetString(1), reader.GetString(2)) : null;
    }

    public async Task<BugReport> ChangeStateAsync(int reportId, string state, string note, CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync("setari-bug-raport.edit", cancellationToken).ConfigureAwait(false);
        if (!BugReportRules.States.Contains(state)) throw new BugReportException("Alege o stare validă.");
        note = (note ?? "").Trim();
        if (note.Length > BugReportRules.MaxNote) throw new BugReportException($"Nota poate avea cel mult {BugReportRules.MaxNote:N0} de caractere.");
        var before = await GetAsync(reportId, cancellationToken).ConfigureAwait(false) ?? throw new BugReportException("Raportul nu mai există.");
        var user = await access.GetUsernameAsync(cancellationToken).ConfigureAwait(false) ?? "";
        var now = DateTime.UtcNow;
        await using (var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false))
        await using (var command = new MySqlCommand("""
            UPDATE bug_reports SET resolved_version=CASE WHEN @state='Rezolvat' THEN IF(state='Rezolvat' AND resolved_version<>'', resolved_version, @version) ELSE '' END,
              state=@state, admin_note=@note, updated_by=@by, updated_utc=@now,
              resolved_utc=CASE WHEN @state IN ('Rezolvat','Respins') THEN @now ELSE '' END WHERE id=@id
            """, connection))
        {
            command.Parameters.AddWithValue("@version", AppVersion.Current);
            command.Parameters.AddWithValue("@state", state);
            command.Parameters.AddWithValue("@note", note);
            command.Parameters.AddWithValue("@by", user);
            command.Parameters.AddWithValue("@now", MariaTimeText.Format(now));
            command.Parameters.AddWithValue("@id", reportId);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        if (before.State != state || before.AdminNote != note)
            await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.BugReport, AuditActions.ChangeBugReportState, reportId.ToString(CultureInfo.InvariantCulture), before.Number,
                AuditDetails.Changes([new AuditChange("Stare", before.State, state), new AuditChange("Notă administrator", before.AdminNote, note)]), "", cancellationToken).ConfigureAwait(false);
        Publish(AuditActions.ChangeBugReportState, reportId);
        return (await GetAsync(reportId, cancellationToken).ConfigureAwait(false))!;
    }

    public async Task<BugReport> UpdateContentAsync(int reportId, string title, string description, IReadOnlyList<BugReportImage> addedImages, CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync("setari-bug-raport.edit", cancellationToken).ConfigureAwait(false);
        title = (title ?? "").Trim();
        description = (description ?? "").Trim();
        if (title.Length == 0) throw new BugReportException("Scrie un titlu scurt al problemei.");
        if (title.Length > BugReportRules.MaxTitle) throw new BugReportException($"Titlul poate avea cel mult {BugReportRules.MaxTitle} de caractere.");
        if (description.Length == 0) throw new BugReportException("Descrierea nu poate fi goală.");
        if (description.Length > BugReportRules.MaxDescription) throw new BugReportException($"Descrierea poate avea cel mult {BugReportRules.MaxDescription:N0} de caractere.");
        var before = await GetAsync(reportId, cancellationToken).ConfigureAwait(false) ?? throw new BugReportException("Raportul nu mai există.");
        addedImages ??= [];
        if (before.FileCount + addedImages.Count > BugReportRules.MaxImages) throw new BugReportException($"Un raport poate avea cel mult {BugReportRules.MaxImages} imagini (acum are {before.FileCount}).");
        var images = new List<(BugReportImage Image, string Type)>();
        foreach (var image in addedImages)
        {
            if (image.Content.Length == 0 || image.Content.Length > BugReportRules.MaxImageBytes)
                throw new BugReportException($"Imaginea „{image.FileName}” trebuie să aibă cel mult {BugReportRules.MaxImageBytes / 1024 / 1024} MB.");
            var type = BugReportRules.DetectImageType(image.Content) ?? throw new BugReportException($"Fișierul „{image.FileName}” nu este o imagine (PNG, JPEG, GIF sau WebP).");
            images.Add((image, type));
        }
        var user = await access.GetUsernameAsync(cancellationToken).ConfigureAwait(false) ?? "";
        var now = DateTime.UtcNow;
        await using (var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false))
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false))
        {
            await using (var update = new MySqlCommand("UPDATE bug_reports SET title=@title, description=@description, updated_by=@by, updated_utc=@now WHERE id=@id", connection, transaction))
            {
                update.Parameters.AddWithValue("@title", title);
                update.Parameters.AddWithValue("@description", description);
                update.Parameters.AddWithValue("@by", user);
                update.Parameters.AddWithValue("@now", MariaTimeText.Format(now));
                update.Parameters.AddWithValue("@id", reportId);
                await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            var index = before.FileCount;
            foreach (var (image, type) in images)
            {
                index++;
                await using var file = new MySqlCommand("""
                    INSERT INTO bug_report_files (report_id, kind, file_name, content_type, size_bytes, content, created_utc) VALUES (@report,@kind,@name,@type,@size,@content,@now)
                    """, connection, transaction);
                file.Parameters.AddWithValue("@report", reportId);
                file.Parameters.AddWithValue("@kind", image.Kind is BugReportRules.KindScreenshot or BugReportRules.KindFile ? image.Kind : BugReportRules.KindPasted);
                file.Parameters.AddWithValue("@name", BugReportRules.SafeName(image.FileName, $"imagine-{index}{BugReportRules.Extension(type)}"));
                file.Parameters.AddWithValue("@type", type);
                file.Parameters.AddWithValue("@size", image.Content.Length);
                file.Parameters.AddWithValue("@content", image.Content);
                file.Parameters.AddWithValue("@now", MariaTimeText.Format(now));
                await file.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        if (before.Title != title || before.Description != description || images.Count > 0)
        {
            var changes = new List<AuditChange>();
            if (before.Title != title) changes.Add(new AuditChange("Titlu", before.Title, title));
            if (before.Description != description) changes.Add(new AuditChange("Descriere", Trim(before.Description, 200), Trim(description, 200)));
            if (images.Count > 0) changes.Add(new AuditChange("Imagini adăugate", "0", images.Count.ToString(CultureInfo.InvariantCulture)));
            await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.BugReport, AuditActions.EditBugReportContent, reportId.ToString(CultureInfo.InvariantCulture), before.Number,
                AuditDetails.Changes([.. changes]), "", cancellationToken).ConfigureAwait(false);
        }
        Publish(AuditActions.EditBugReportContent, reportId);
        return (await GetAsync(reportId, cancellationToken).ConfigureAwait(false))!;
    }

    public async Task<int> DeleteClosedAsync(CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync("setari-bug-raport.edit", cancellationToken).ConfigureAwait(false);
        int deleted;
        await using (var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false))
        await using (var command = new MySqlCommand("DELETE FROM bug_reports WHERE state IN (@resolved,@rejected)", connection))
        {
            command.Parameters.AddWithValue("@resolved", BugReportRules.StateResolved);
            command.Parameters.AddWithValue("@rejected", BugReportRules.StateRejected);
            deleted = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        if (deleted > 0)
            await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.BugReport, AuditActions.DeleteAllClosedBugReports, "*", "rapoarte rezolvate sau respinse",
                $"Rapoarte șterse: {deleted}", "", cancellationToken).ConfigureAwait(false);
        Publish(AuditActions.DeleteAllClosedBugReports, 0);
        return deleted;
    }

    public async Task DeleteAsync(int reportId, CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync("setari-bug-raport.edit", cancellationToken).ConfigureAwait(false);
        var before = await GetAsync(reportId, cancellationToken).ConfigureAwait(false) ?? throw new BugReportException("Raportul nu mai există.");
        await using (var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false))
        await using (var command = new MySqlCommand("DELETE FROM bug_reports WHERE id=@id", connection))
        {
            command.Parameters.AddWithValue("@id", reportId);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.BugReport, AuditActions.DeleteBugReport, reportId.ToString(CultureInfo.InvariantCulture), before.Number,
            $"Titlu: {Trim(before.Title, 120)}; autor: {before.UserName}; stare: {before.State}", "", cancellationToken).ConfigureAwait(false);
        Publish(AuditActions.DeleteBugReport, reportId);
    }

    public async Task<BugReportExportResult> ExportAsync(IReadOnlyList<int> reportIds, CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync("setari-bug-raport.export", cancellationToken).ConfigureAwait(false);
        if (reportIds.Count == 0) throw new BugReportException("Nu există rapoarte de exportat.");
        var reports = new List<BugReport>();
        foreach (var id in reportIds.Distinct())
            if (await GetAsync(id, cancellationToken).ConfigureAwait(false) is { } report) reports.Add(report);
        if (reports.Count == 0) throw new BugReportException("Rapoartele alese nu mai există.");
        reports.Sort((a, b) => a.Id.CompareTo(b.Id));
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var report in reports)
            {
                var folder = report.Number + "/";
                Add(zip, folder + "raport.json", Encoding.UTF8.GetBytes(Json(report)));
                Add(zip, folder + "raport.txt", Encoding.UTF8.GetBytes(Text(report)));
                foreach (var file in await GetFilesAsync(report.Id, cancellationToken).ConfigureAwait(false))
                    if (await GetFileContentAsync(file.Id, cancellationToken).ConfigureAwait(false) is { } content)
                        Add(zip, folder + $"{file.Id:000}-{BugReportRules.SafeName(file.FileName, "imagine" + BugReportRules.Extension(file.ContentType))}", content.Content);
            }
            if (reports.Count > 1) Add(zip, "index.csv", Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(Index(reports))).ToArray());
        }
        var fileName = reports.Count == 1 ? $"{reports[0].Number}.zip" : $"rapoarte-erori-{DateTime.Now:yyyyMMdd-HHmm}.zip";
        await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.BugReport, AuditActions.ExportBugReport, reports.Count == 1 ? reports[0].Id.ToString(CultureInfo.InvariantCulture) : "",
            reports.Count == 1 ? reports[0].Number : $"{reports.Count} rapoarte", $"Fișier: {fileName}; rapoarte: {string.Join(", ", reports.Select(item => item.Number))}", "", cancellationToken).ConfigureAwait(false);
        return new BugReportExportResult(fileName, buffer.ToArray(), reports.Count);
    }

    private static void Add(ZipArchive zip, string path, byte[] content)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(content, 0, content.Length);
    }

    private static string Json(BugReport report) => JsonSerializer.Serialize(new
    {
        report.Number, report.Title, report.Description, report.Severity, report.State, report.Route, report.PageTitle, report.AppVersion, User = report.UserName, Role = report.UserRole,
        Created = BugReportRules.Display(report.CreatedUtc), AdminNote = report.AdminNote, Resolved = report.ResolvedUtc is { } resolved ? BugReportRules.Display(resolved) : "",
        Technical = ParseTech(report.TechJson)
    }, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    private static object? ParseTech(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonDocument.Parse(json).RootElement.Clone(); }
        catch (JsonException) { return json; }
    }

    private static string Text(BugReport report)
    {
        var text = new StringBuilder();
        text.AppendLine($"{report.Number} · {report.Title}");
        text.AppendLine($"Gravitate: {report.Severity} · Stare: {report.State}");
        text.AppendLine($"Autor: {report.UserName} ({report.UserRole}) · {BugReportRules.Display(report.CreatedUtc)} · versiunea {report.AppVersion}");
        text.AppendLine($"Pagina: {report.PageTitle} ({report.Route})");
        text.AppendLine().AppendLine("Descriere:").AppendLine(report.Description);
        if (report.AdminNote.Length > 0) text.AppendLine().AppendLine("Nota administratorului:").AppendLine(report.AdminNote);
        if (report.TechJson.Length > 0) text.AppendLine().AppendLine("Detalii tehnice (JSON):").AppendLine(report.TechJson);
        return text.ToString();
    }

    private static string Index(IEnumerable<BugReport> reports)
    {
        static string Cell(string value) => "\"" + value.Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + "\"";
        var csv = new StringBuilder("Numar;Titlu;Gravitate;Stare;Autor;Creat;Pagina\r\n");
        foreach (var report in reports)
            csv.Append(string.Join(';', Cell(report.Number), Cell(report.Title), Cell(report.Severity), Cell(report.State), Cell(report.UserName), Cell(BugReportRules.Display(report.CreatedUtc)), Cell(report.Route))).Append("\r\n");
        return csv.ToString();
    }
}

// The service behind the circuit breaker of the registry; the answers of the module (BugReportException) and the refusals are not faults.
internal sealed class GuardedBugReportService(IBugReportService inner, ModuleRegistry modules) : IBugReportService
{
    private const string Id = BugReportsModule.ModuleId;
    public Task<BugReport> CreateAsync(BugReportInput input, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.CreateAsync(input, cancellationToken));
    public Task<IReadOnlyList<BugReport>> GetMineAsync(CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.GetMineAsync(cancellationToken));
    public Task<BugReportPage> QueryAsync(BugReportQuery query, int page, int pageSize, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.QueryAsync(query, page, pageSize, cancellationToken));
    public Task<int> CountNewAsync(CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.CountNewAsync(cancellationToken));
    public Task<int> CountOpenAsync(CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.CountOpenAsync(cancellationToken));
    public Task<IReadOnlyList<BugReportFile>> GetFilesAsync(int reportId, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.GetFilesAsync(reportId, cancellationToken));
    public Task<BugReportFileContent?> GetFileContentAsync(int fileId, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.GetFileContentAsync(fileId, cancellationToken));
    public Task<BugReport> ChangeStateAsync(int reportId, string state, string note, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.ChangeStateAsync(reportId, state, note, cancellationToken));
    public Task<BugReport> UpdateContentAsync(int reportId, string title, string description, IReadOnlyList<BugReportImage> addedImages, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.UpdateContentAsync(reportId, title, description, addedImages, cancellationToken));
    public Task DeleteAsync(int reportId, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.DeleteAsync(reportId, cancellationToken));
    public Task<int> DeleteClosedAsync(CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.DeleteClosedAsync(cancellationToken));
    public Task<IReadOnlyList<BugReportAlert>> GetAlertsAsync(CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.GetAlertsAsync(cancellationToken));
    public Task<BugReportExportResult> ExportAsync(IReadOnlyList<int> reportIds, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.ExportAsync(reportIds, cancellationToken));
}

// The stand-in while the module is switched off or did not start: every call says the module is out.
internal sealed class UnavailableBugReportService : IBugReportService
{
    private static Task<T> Out<T>() => Task.FromException<T>(new ModuleUnavailableException(BugReportsModule.ModuleId));
    public Task<BugReport> CreateAsync(BugReportInput input, CancellationToken cancellationToken = default) => Out<BugReport>();
    public Task<IReadOnlyList<BugReport>> GetMineAsync(CancellationToken cancellationToken = default) => Out<IReadOnlyList<BugReport>>();
    public Task<BugReportPage> QueryAsync(BugReportQuery query, int page, int pageSize, CancellationToken cancellationToken = default) => Out<BugReportPage>();
    public Task<int> CountNewAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
    public Task<IReadOnlyList<BugReportFile>> GetFilesAsync(int reportId, CancellationToken cancellationToken = default) => Out<IReadOnlyList<BugReportFile>>();
    public Task<BugReportFileContent?> GetFileContentAsync(int fileId, CancellationToken cancellationToken = default) => Out<BugReportFileContent?>();
    public Task<BugReport> ChangeStateAsync(int reportId, string state, string note, CancellationToken cancellationToken = default) => Out<BugReport>();
    public Task<BugReport> UpdateContentAsync(int reportId, string title, string description, IReadOnlyList<BugReportImage> addedImages, CancellationToken cancellationToken = default) => Out<BugReport>();
    public Task DeleteAsync(int reportId, CancellationToken cancellationToken = default) => Task.FromException(new ModuleUnavailableException(BugReportsModule.ModuleId));
    public Task<int> DeleteClosedAsync(CancellationToken cancellationToken = default) => Out<int>();
    public Task<IReadOnlyList<BugReportAlert>> GetAlertsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<BugReportAlert>>([]);
    public Task<BugReportExportResult> ExportAsync(IReadOnlyList<int> reportIds, CancellationToken cancellationToken = default) => Out<BugReportExportResult>();
}
