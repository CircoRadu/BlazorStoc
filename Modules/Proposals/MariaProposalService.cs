using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using BlazorStoc.Services;
using MySqlConnector;

namespace BlazorStoc.Modules.Proposals;

public sealed class MariaProposalService(IConfiguration configuration, IAccessControl access, IAuditTrail audit, IChangeFeed? changeFeed = null, ChangeOrigin? origin = null) : IProposalService
{
    private const string Columns = """
        p.id, p.number, p.kind, p.area_route, p.area_title, p.title, p.description, p.benefit, p.wanted_importance, p.state, p.user_name, p.user_role, p.app_version, p.implemented_version,
        p.created_utc, p.updated_by, p.updated_utc, p.decided_utc, p.author_seen_utc,
        (SELECT COUNT(*) FROM proposal_files f WHERE f.proposal_id=p.id), (SELECT COUNT(*) FROM proposal_notes n WHERE n.proposal_id=p.id)
        """;

    private async Task<string> RequireUserAsync(CancellationToken cancellationToken) =>
        await access.GetUsernameAsync(cancellationToken).ConfigureAwait(false) is { Length: > 0 } user ? user : throw new AccessDeniedException("Trebuie să fii autentificat pentru a face o propunere.");

    private static Proposal Read(MySqlDataReader reader) => new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6),
        reader.GetString(7), reader.GetString(8), reader.GetString(9), reader.GetString(10), reader.GetString(11), reader.GetString(12), reader.GetString(13),
        MariaTimeText.Parse(reader.GetString(14)), reader.GetString(15), MariaTimeText.ParseOrNull(reader.GetString(16)), MariaTimeText.ParseOrNull(reader.GetString(17)), MariaTimeText.ParseOrNull(reader.GetString(18)),
        Convert.ToInt32(reader.GetValue(19), CultureInfo.InvariantCulture), Convert.ToInt32(reader.GetValue(20), CultureInfo.InvariantCulture));

    private static string Trim(string? text, int max) => (text ?? "").Length > max ? text![..max] : text ?? "";

    public async Task<Proposal> CreateAsync(ProposalInput input, CancellationToken cancellationToken = default)
    {
        var user = await RequireUserAsync(cancellationToken).ConfigureAwait(false);
        var title = (input.Title ?? "").Trim();
        var description = (input.Description ?? "").Trim();
        var benefit = (input.Benefit ?? "").Trim();
        if (!ProposalRules.Kinds.Contains(input.Kind)) throw new ProposalException("Alege tipul propunerii.");
        if (title.Length == 0) throw new ProposalException("Scrie un titlu scurt al propunerii.");
        if (title.Length > ProposalRules.MaxTitle) throw new ProposalException($"Titlul poate avea cel mult {ProposalRules.MaxTitle} de caractere.");
        if (description.Length == 0) throw new ProposalException("Descrie ce vrei să se schimbe sau ce vrei să poți face.");
        if (description.Length > ProposalRules.MaxDescription) throw new ProposalException($"Descrierea poate avea cel mult {ProposalRules.MaxDescription:N0} de caractere.");
        if (benefit.Length > ProposalRules.MaxBenefit) throw new ProposalException($"Motivarea poate avea cel mult {ProposalRules.MaxBenefit:N0} de caractere.");
        if (!ProposalRules.Importances.Contains(input.WantedImportance)) throw new ProposalException("Alege importanța propunerii pentru tine.");
        if (input.Images.Count > ReportImageRules.MaxImages) throw new ProposalException($"Poți atașa cel mult {ReportImageRules.MaxImages} imagini.");
        var images = new List<(ReportImage Image, string Type)>();
        foreach (var image in input.Images)
        {
            if (image.Content.Length == 0 || image.Content.Length > ReportImageRules.MaxImageBytes)
                throw new ProposalException($"Imaginea „{image.FileName}” trebuie să aibă cel mult {ReportImageRules.MaxImageBytes / 1024 / 1024} MB.");
            var type = ReportImageRules.DetectImageType(image.Content) ?? throw new ProposalException($"Fișierul „{image.FileName}” nu este o imagine (PNG, JPEG, GIF sau WebP).");
            images.Add((image, type));
        }
        var role = await access.IsAdministratorAsync(cancellationToken).ConfigureAwait(false) ? "Administrator" : "Utilizator";
        var now = DateTime.UtcNow;
        int id;
        await using (var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false))
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false))
        {
            await using (var insert = new MySqlCommand("""
                INSERT INTO proposals (kind, area_route, area_title, title, description, benefit, wanted_importance, state, user_name, user_role, app_version, created_utc, author_seen_utc)
                VALUES (@kind,@route,@areaTitle,@title,@description,@benefit,@importance,@state,@user,@role,@version,@now,@now)
                """, connection, transaction))
            {
                insert.Parameters.AddWithValue("@kind", input.Kind);
                insert.Parameters.AddWithValue("@route", Trim(input.AreaRoute, 500));
                insert.Parameters.AddWithValue("@areaTitle", Trim(input.AreaTitle, 300));
                insert.Parameters.AddWithValue("@title", title);
                insert.Parameters.AddWithValue("@description", description);
                insert.Parameters.AddWithValue("@benefit", benefit);
                insert.Parameters.AddWithValue("@importance", input.WantedImportance);
                insert.Parameters.AddWithValue("@state", ProposalRules.StateNew);
                insert.Parameters.AddWithValue("@user", user);
                insert.Parameters.AddWithValue("@role", role);
                insert.Parameters.AddWithValue("@version", AppVersion.Current);
                insert.Parameters.AddWithValue("@now", MariaTimeText.Format(now));
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                id = (int)insert.LastInsertedId;
            }
            await using (var number = new MySqlCommand("UPDATE proposals SET number=@number WHERE id=@id", connection, transaction))
            {
                number.Parameters.AddWithValue("@number", ProposalRules.Number(id));
                number.Parameters.AddWithValue("@id", id);
                await number.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            var index = 0;
            foreach (var (image, type) in images)
            {
                index++;
                await using var file = new MySqlCommand("""
                    INSERT INTO proposal_files (proposal_id, kind, file_name, content_type, size_bytes, content, created_utc) VALUES (@proposal,@kind,@name,@type,@size,@content,@now)
                    """, connection, transaction);
                file.Parameters.AddWithValue("@proposal", id);
                file.Parameters.AddWithValue("@kind", image.Kind is ReportImageRules.KindScreenshot or ReportImageRules.KindFile ? image.Kind : ReportImageRules.KindPasted);
                file.Parameters.AddWithValue("@name", ReportImageRules.SafeName(image.FileName, $"imagine-{index}{ReportImageRules.Extension(type)}"));
                file.Parameters.AddWithValue("@type", type);
                file.Parameters.AddWithValue("@size", image.Content.Length);
                file.Parameters.AddWithValue("@content", image.Content);
                file.Parameters.AddWithValue("@now", MariaTimeText.Format(now));
                await file.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.Proposal, AuditActions.CreateProposal, id.ToString(CultureInfo.InvariantCulture), ProposalRules.Number(id),
            $"Tip: {input.Kind}; importanță: {input.WantedImportance}; zona: {Trim(input.AreaTitle, 100)}; imagini: {images.Count}; titlu: {Trim(title, 120)}", "", cancellationToken).ConfigureAwait(false);
        Publish(AuditActions.CreateProposal, id);
        return (await GetAsync(id, cancellationToken).ConfigureAwait(false))!;
    }

    public async Task<Proposal?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        await RequireUserAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand($"SELECT {Columns} FROM proposals p WHERE p.id=@id", connection);
        command.Parameters.AddWithValue("@id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? Read(reader) : null;
    }

    public async Task<IReadOnlyList<Proposal>> GetMineAsync(CancellationToken cancellationToken = default)
    {
        var user = await RequireUserAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand($"SELECT {Columns} FROM proposals p WHERE p.user_name=@user ORDER BY p.id DESC LIMIT 300", connection);
        command.Parameters.AddWithValue("@user", user);
        return await ReadAllAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<Proposal>> ReadAllAsync(MySqlCommand command, CancellationToken cancellationToken)
    {
        var items = new List<Proposal>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) items.Add(Read(reader));
        return items;
    }

    public async Task<IReadOnlyList<Proposal>> GetBoardAsync(string? text, bool onlyOpen, CancellationToken cancellationToken = default)
    {
        await RequireUserAsync(cancellationToken).ConfigureAwait(false);
        var clauses = new List<string>();
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("", connection);
        if (onlyOpen)
        {
            clauses.Add("p.state NOT IN (@c1,@c2,@c3)");
            command.Parameters.AddWithValue("@c1", ProposalRules.StateImplemented); command.Parameters.AddWithValue("@c2", ProposalRules.StatePostponed); command.Parameters.AddWithValue("@c3", ProposalRules.StateRejected);
        }
        var index = 0;
        foreach (var word in (text ?? "").Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(word => word.Length >= 3).Take(6))
        {
            var name = "@w" + index++;
            clauses.Add($"(LOCATE(LOWER({name}), LOWER(p.title))>0 OR LOCATE(LOWER({name}), LOWER(p.description))>0)");
            command.Parameters.AddWithValue(name, word);
        }
        command.CommandText = $"SELECT {Columns} FROM proposals p {(clauses.Count == 0 ? "" : "WHERE " + string.Join(" AND ", clauses))} ORDER BY p.id DESC LIMIT 300";
        return await ReadAllAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private static (string Where, List<MySqlParameter> Parameters) Filter(ProposalQuery query)
    {
        var clauses = new List<string>();
        var parameters = new List<MySqlParameter>();
        if (!string.IsNullOrEmpty(query.State)) { clauses.Add("p.state=@state"); parameters.Add(new("@state", query.State)); }
        if (query.Closed is { } closed)
        {
            clauses.Add(closed ? "p.state IN (@c1,@c2,@c3)" : "p.state NOT IN (@c1,@c2,@c3)");
            parameters.Add(new("@c1", ProposalRules.StateImplemented)); parameters.Add(new("@c2", ProposalRules.StatePostponed)); parameters.Add(new("@c3", ProposalRules.StateRejected));
        }
        if (!string.IsNullOrEmpty(query.Kind)) { clauses.Add("p.kind=@kind"); parameters.Add(new("@kind", query.Kind)); }
        if (!string.IsNullOrEmpty(query.Importance)) { clauses.Add("p.wanted_importance=@importance"); parameters.Add(new("@importance", query.Importance)); }
        if (!string.IsNullOrWhiteSpace(query.User)) { clauses.Add("LOCATE(LOWER(@user), LOWER(p.user_name))>0"); parameters.Add(new("@user", query.User.Trim())); }
        if (!string.IsNullOrWhiteSpace(query.Text)) { clauses.Add("(LOCATE(LOWER(@text), LOWER(p.title))>0 OR LOCATE(LOWER(@text), LOWER(p.area_title))>0 OR LOCATE(LOWER(@text), LOWER(p.number))>0)"); parameters.Add(new("@text", query.Text.Trim())); }
        if (query.From is { } from) { clauses.Add("p.created_utc>=@from"); parameters.Add(new("@from", from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))); }
        if (query.To is { } to) { clauses.Add("p.created_utc<@to"); parameters.Add(new("@to", to.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))); }
        return (clauses.Count == 0 ? "" : "WHERE " + string.Join(" AND ", clauses), parameters);
    }

    private static MySqlParameter Clone(MySqlParameter parameter) => new(parameter.ParameterName, parameter.Value);

    public async Task<ProposalPage> QueryAsync(ProposalQuery query, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync("setari-propuneri.view", cancellationToken).ConfigureAwait(false);
        var (where, parameters) = Filter(query);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        int total;
        await using (var count = new MySqlCommand($"SELECT COUNT(*) FROM proposals p {where}", connection))
        {
            foreach (var parameter in parameters) count.Parameters.Add(Clone(parameter));
            total = Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
        }
        var limit = pageSize <= 0 ? "" : $"LIMIT {pageSize} OFFSET {Math.Max(0, page - 1) * pageSize}";
        await using var command = new MySqlCommand($"SELECT {Columns} FROM proposals p {where} ORDER BY p.id DESC {limit}", connection);
        foreach (var parameter in parameters) command.Parameters.Add(Clone(parameter));
        return new ProposalPage(await ReadAllAsync(command, cancellationToken).ConfigureAwait(false), total);
    }

    public async Task<IReadOnlyList<ProposalNote>> GetNotesAsync(int proposalId, CancellationToken cancellationToken = default)
    {
        await RequireUserAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("SELECT id, proposal_id, author, author_role, body, created_utc FROM proposal_notes WHERE proposal_id=@id ORDER BY id", connection);
        command.Parameters.AddWithValue("@id", proposalId);
        var notes = new List<ProposalNote>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            notes.Add(new(reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), MariaTimeText.Parse(reader.GetString(5))));
        return notes;
    }

    public async Task<ProposalNote> AddNoteAsync(int proposalId, string body, CancellationToken cancellationToken = default)
    {
        var user = await RequireUserAsync(cancellationToken).ConfigureAwait(false);
        body = (body ?? "").Trim();
        if (body.Length == 0) throw new ProposalException("Scrie răspunsul.");
        if (body.Length > ProposalRules.MaxNote) throw new ProposalException($"Răspunsul poate avea cel mult {ProposalRules.MaxNote:N0} de caractere.");
        var proposal = await GetAsync(proposalId, cancellationToken).ConfigureAwait(false) ?? throw new ProposalException("Propunerea nu mai există.");
        var isAdmin = await access.HasAsync("setari-propuneri.edit", cancellationToken).ConfigureAwait(false);
        var isAuthor = string.Equals(proposal.UserName, user, StringComparison.Ordinal);
        if (!isAdmin && !isAuthor) throw new AccessDeniedException("Doar autorul propunerii și administratorul pot răspunde.");
        var role = await access.IsAdministratorAsync(cancellationToken).ConfigureAwait(false) ? "Administrator" : "Utilizator";
        var now = DateTime.UtcNow;
        int id;
        await using (var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false))
        await using (var command = new MySqlCommand("INSERT INTO proposal_notes (proposal_id, author, author_role, body, created_utc) VALUES (@id,@author,@role,@body,@now)", connection))
        {
            command.Parameters.AddWithValue("@id", proposalId);
            command.Parameters.AddWithValue("@author", user);
            command.Parameters.AddWithValue("@role", role);
            command.Parameters.AddWithValue("@body", body);
            command.Parameters.AddWithValue("@now", MariaTimeText.Format(now));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            id = (int)command.LastInsertedId;
        }
        // An answer of the administrator is a change the author must see; the author's own answer is already seen by the author.
        if (!isAuthor) await TouchAsync(proposalId, user, now, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.Proposal, AuditActions.AddProposalNote, proposalId.ToString(CultureInfo.InvariantCulture), proposal.Number,
            $"Răspuns de la {user}: {Trim(body, 200)}", "", cancellationToken).ConfigureAwait(false);
        return new ProposalNote(id, proposalId, user, role, body, now);
    }

    private async Task TouchAsync(int proposalId, string by, DateTime now, CancellationToken cancellationToken)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("UPDATE proposals SET updated_by=@by, updated_utc=@now WHERE id=@id", connection);
        command.Parameters.AddWithValue("@by", by);
        command.Parameters.AddWithValue("@now", MariaTimeText.Format(now));
        command.Parameters.AddWithValue("@id", proposalId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    // The author of the proposal and who may follow the proposals (the administrator) can read its pictures; nobody else.
    private async Task EnsureCanSeeFilesAsync(int proposalId, CancellationToken cancellationToken)
    {
        var user = await RequireUserAsync(cancellationToken).ConfigureAwait(false);
        if (await access.HasAsync("setari-propuneri.view", cancellationToken).ConfigureAwait(false)) return;
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("SELECT COUNT(*) FROM proposals WHERE id=@id AND user_name=@user", connection);
        command.Parameters.AddWithValue("@id", proposalId);
        command.Parameters.AddWithValue("@user", user);
        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture) == 0)
            throw new AccessDeniedException("Nu ai dreptul să vezi imaginile acestei propuneri.");
    }

    public async Task<IReadOnlyList<ProposalFile>> GetFilesAsync(int proposalId, CancellationToken cancellationToken = default)
    {
        await EnsureCanSeeFilesAsync(proposalId, cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("SELECT id, proposal_id, kind, file_name, content_type, size_bytes, created_utc FROM proposal_files WHERE proposal_id=@id ORDER BY id", connection);
        command.Parameters.AddWithValue("@id", proposalId);
        var files = new List<ProposalFile>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            files.Add(new(reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetInt32(5), MariaTimeText.Parse(reader.GetString(6))));
        return files;
    }

    public async Task<ProposalFileContent?> GetFileContentAsync(int fileId, CancellationToken cancellationToken = default)
    {
        int proposalId;
        await using (var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false))
        await using (var owner = new MySqlCommand("SELECT proposal_id FROM proposal_files WHERE id=@id", connection))
        {
            owner.Parameters.AddWithValue("@id", fileId);
            if (await owner.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not int found) return null;
            proposalId = found;
        }
        await EnsureCanSeeFilesAsync(proposalId, cancellationToken).ConfigureAwait(false);
        await using var second = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("SELECT content, content_type, file_name FROM proposal_files WHERE id=@id", second);
        command.Parameters.AddWithValue("@id", fileId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? new ProposalFileContent((byte[])reader.GetValue(0), reader.GetString(1), reader.GetString(2)) : null;
    }

    public async Task<Proposal> ChangeStateAsync(int proposalId, string state, string implementedVersion, string note, CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync("setari-propuneri.edit", cancellationToken).ConfigureAwait(false);
        if (!ProposalRules.States.Contains(state)) throw new ProposalException("Alege o stare validă.");
        implementedVersion = (implementedVersion ?? "").Trim();
        if (implementedVersion.Length > ProposalRules.MaxVersion) throw new ProposalException($"Versiunea poate avea cel mult {ProposalRules.MaxVersion} de caractere.");
        if (state != ProposalRules.StateImplemented) implementedVersion = "";
        note = (note ?? "").Trim();
        if (note.Length > ProposalRules.MaxNote) throw new ProposalException($"Răspunsul poate avea cel mult {ProposalRules.MaxNote:N0} de caractere.");
        var before = await GetAsync(proposalId, cancellationToken).ConfigureAwait(false) ?? throw new ProposalException("Propunerea nu mai există.");
        var user = await access.GetUsernameAsync(cancellationToken).ConfigureAwait(false) ?? "";
        var now = DateTime.UtcNow;
        if (before.State != state || before.ImplementedVersion != implementedVersion)
        {
            await using (var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false))
            await using (var command = new MySqlCommand("""
                UPDATE proposals SET state=@state, implemented_version=@version, updated_by=@by, updated_utc=@now,
                  decided_utc=CASE WHEN @state IN ('Implementată','Amânată','Respinsă') THEN @now ELSE '' END WHERE id=@id
                """, connection))
            {
                command.Parameters.AddWithValue("@state", state);
                command.Parameters.AddWithValue("@version", implementedVersion);
                command.Parameters.AddWithValue("@by", user);
                command.Parameters.AddWithValue("@now", MariaTimeText.Format(now));
                command.Parameters.AddWithValue("@id", proposalId);
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.Proposal, AuditActions.ChangeProposalState, proposalId.ToString(CultureInfo.InvariantCulture), before.Number,
                AuditDetails.Changes([new AuditChange("Stare", before.State, state), new AuditChange("Implementată în versiunea", before.ImplementedVersion, implementedVersion)]), "", cancellationToken).ConfigureAwait(false);
        }
        if (note.Length > 0) await AddNoteAsync(proposalId, note, cancellationToken).ConfigureAwait(false);
        Publish(AuditActions.ChangeProposalState, proposalId);
        return (await GetAsync(proposalId, cancellationToken).ConfigureAwait(false))!;
    }

    public async Task DeleteAsync(int proposalId, CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync("setari-propuneri.edit", cancellationToken).ConfigureAwait(false);
        var before = await GetAsync(proposalId, cancellationToken).ConfigureAwait(false) ?? throw new ProposalException("Propunerea nu mai există.");
        await using (var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false))
        await using (var command = new MySqlCommand("DELETE FROM proposals WHERE id=@id", connection))
        {
            command.Parameters.AddWithValue("@id", proposalId);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.Proposal, AuditActions.DeleteProposal, proposalId.ToString(CultureInfo.InvariantCulture), before.Number,
            $"Titlu: {Trim(before.Title, 120)}; autor: {before.UserName}; stare: {before.State}", "", cancellationToken).ConfigureAwait(false);
        Publish(AuditActions.DeleteProposal, proposalId);
    }

    // The tab badges and the open lists of the other sessions follow (the identifiers only).
    private void Publish(string action, int id) =>
        changeFeed?.Publish(new ChangeEvent(AuditEntities.Proposal, action, id.ToString(CultureInfo.InvariantCulture), origin?.Id ?? Guid.Empty, DateTime.UtcNow));

    public async Task<int> CountOpenAsync(CancellationToken cancellationToken = default)
    {
        if (!await access.HasAsync("setari-propuneri.view", cancellationToken).ConfigureAwait(false)) return 0;
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("SELECT COUNT(*) FROM proposals WHERE state NOT IN (@s1, @s2, @s3)", connection);
        for (var index = 0; index < ProposalRules.ClosedStates.Count; index++) command.Parameters.AddWithValue("@s" + (index + 1), ProposalRules.ClosedStates[index]);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    public async Task MarkSeenAsync(int proposalId, CancellationToken cancellationToken = default)
    {
        var user = await RequireUserAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("UPDATE proposals SET author_seen_utc=@now WHERE id=@id AND user_name=@user", connection);
        command.Parameters.AddWithValue("@now", MariaTimeText.Format(DateTime.UtcNow));
        command.Parameters.AddWithValue("@id", proposalId);
        command.Parameters.AddWithValue("@user", user);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ProposalAlert>> GetAlertsAsync(CancellationToken cancellationToken = default)
    {
        var user = await access.GetUsernameAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(user)) return [];
        var alerts = new List<ProposalAlert>();
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        // The author: the state changed or the administrator answered after the author last opened the proposal.
        await using (var mine = new MySqlCommand($"SELECT {Columns} FROM proposals p WHERE p.user_name=@user AND p.updated_utc<>'' AND p.updated_utc>p.author_seen_utc ORDER BY p.updated_utc DESC LIMIT 100", connection))
        {
            mine.Parameters.AddWithValue("@user", user);
            foreach (var proposal in await ReadAllAsync(mine, cancellationToken).ConfigureAwait(false))
                alerts.Add(new ProposalAlert(proposal, proposal.State == ProposalRules.StateNew ? "Răspuns nou" : $"Stare: {proposal.State}"));
        }
        // The administrator: the proposals nobody looked at yet.
        if (await access.HasAsync("setari-propuneri.view", cancellationToken).ConfigureAwait(false))
        {
            await using var fresh = new MySqlCommand($"SELECT {Columns} FROM proposals p WHERE p.state=@state ORDER BY p.id DESC LIMIT 100", connection);
            fresh.Parameters.AddWithValue("@state", ProposalRules.StateNew);
            foreach (var proposal in await ReadAllAsync(fresh, cancellationToken).ConfigureAwait(false))
                if (!alerts.Any(alert => alert.Proposal.Id == proposal.Id)) alerts.Add(new ProposalAlert(proposal, "Propunere nouă"));
        }
        return alerts;
    }

    public async Task<ProposalExportResult> ExportAsync(IReadOnlyList<int> proposalIds, CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync("setari-propuneri.export", cancellationToken).ConfigureAwait(false);
        if (proposalIds.Count == 0) throw new ProposalException("Nu există propuneri de exportat.");
        var items = new List<Proposal>();
        foreach (var id in proposalIds.Distinct())
            if (await GetAsync(id, cancellationToken).ConfigureAwait(false) is { } proposal) items.Add(proposal);
        if (items.Count == 0) throw new ProposalException("Propunerile alese nu mai există.");
        items.Sort((a, b) => a.Id.CompareTo(b.Id));
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var proposal in items)
            {
                var folder = proposal.Number + "/";
                var notes = await GetNotesAsync(proposal.Id, cancellationToken).ConfigureAwait(false);
                Add(zip, folder + "propunere.json", Encoding.UTF8.GetBytes(Json(proposal, notes)));
                Add(zip, folder + "propunere.txt", Encoding.UTF8.GetBytes(Text(proposal, notes)));
                foreach (var file in await GetFilesAsync(proposal.Id, cancellationToken).ConfigureAwait(false))
                    if (await GetFileContentAsync(file.Id, cancellationToken).ConfigureAwait(false) is { } content)
                        Add(zip, folder + $"{file.Id:000}-{ReportImageRules.SafeName(file.FileName, "imagine" + ReportImageRules.Extension(file.ContentType))}", content.Content);
            }
            if (items.Count > 1) Add(zip, "index.csv", Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(Index(items))).ToArray());
        }
        var fileName = items.Count == 1 ? $"{items[0].Number}.zip" : $"propuneri-{DateTime.Now:yyyyMMdd-HHmm}.zip";
        await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.Proposal, AuditActions.ExportProposal, items.Count == 1 ? items[0].Id.ToString(CultureInfo.InvariantCulture) : "",
            items.Count == 1 ? items[0].Number : $"{items.Count} propuneri", $"Fișier: {fileName}; propuneri: {string.Join(", ", items.Select(item => item.Number))}", "", cancellationToken).ConfigureAwait(false);
        return new ProposalExportResult(fileName, buffer.ToArray(), items.Count);
    }

    private static void Add(ZipArchive zip, string path, byte[] content)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(content, 0, content.Length);
    }

    private static string Json(Proposal proposal, IReadOnlyList<ProposalNote> notes) => JsonSerializer.Serialize(new
    {
        proposal.Number, proposal.Kind, proposal.Title, proposal.Description, proposal.Benefit, proposal.WantedImportance, proposal.State, proposal.ImplementedVersion,
        Area = new { proposal.AreaTitle, proposal.AreaRoute }, User = proposal.UserName, Role = proposal.UserRole, proposal.AppVersion, Created = ProposalRules.Display(proposal.CreatedUtc),
        Decided = proposal.DecidedUtc is { } decided ? ProposalRules.Display(decided) : "",
        Notes = notes.Select(note => new { note.Author, note.AuthorRole, Date = ProposalRules.Display(note.CreatedUtc), note.Body })
    }, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    private static string Text(Proposal proposal, IReadOnlyList<ProposalNote> notes)
    {
        var text = new StringBuilder();
        text.AppendLine($"{proposal.Number} · {proposal.Title}");
        text.AppendLine($"Tip: {proposal.Kind} · Importanță: {proposal.WantedImportance} · Stare: {proposal.State}{(proposal.ImplementedVersion.Length > 0 ? " (versiunea " + proposal.ImplementedVersion + ")" : "")}");
        text.AppendLine($"Autor: {proposal.UserName} ({proposal.UserRole}) · {ProposalRules.Display(proposal.CreatedUtc)} · versiunea {proposal.AppVersion}");
        text.AppendLine($"Zona: {proposal.AreaTitle} ({proposal.AreaRoute})");
        text.AppendLine().AppendLine("Descriere:").AppendLine(proposal.Description);
        if (proposal.Benefit.Length > 0) text.AppendLine().AppendLine("De ce:").AppendLine(proposal.Benefit);
        foreach (var note in notes) text.AppendLine().AppendLine($"{note.Author} ({note.AuthorRole}) · {ProposalRules.Display(note.CreatedUtc)}:").AppendLine(note.Body);
        return text.ToString();
    }

    private static string Index(IEnumerable<Proposal> proposals)
    {
        static string Cell(string value) => "\"" + value.Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + "\"";
        var csv = new StringBuilder("Numar;Titlu;Tip;Importanta;Stare;Autor;Creat;Zona\r\n");
        foreach (var proposal in proposals)
            csv.Append(string.Join(';', Cell(proposal.Number), Cell(proposal.Title), Cell(proposal.Kind), Cell(proposal.WantedImportance), Cell(proposal.State), Cell(proposal.UserName), Cell(ProposalRules.Display(proposal.CreatedUtc)), Cell(proposal.AreaTitle))).Append("\r\n");
        return csv.ToString();
    }
}
