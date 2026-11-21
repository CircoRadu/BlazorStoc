using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using BlazorStoc.Services;
using MySqlConnector;

namespace BlazorStoc.Modules.Announcements;

public sealed class MariaAnnouncementService(IConfiguration configuration, IAccessControl access, IAuditTrail audit, IChangeFeed? changeFeed = null, ChangeOrigin? origin = null) : IAnnouncementService
{
    private const string ViewKey = "setari-anunturi.view", EditKey = "setari-anunturi.edit";

    private async Task<string> RequireUserAsync(CancellationToken token) =>
        await access.GetUsernameAsync(token).ConfigureAwait(false) is { Length: > 0 } user ? user : throw new AccessDeniedException("Trebuie să fii autentificat.");

    private static string Today() => DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string Trim(string? text, int max) => (text ?? "").Length > max ? text![..max] : text ?? "";

    // ---------------------------------------------------------------- the user's side

    public async Task<IReadOnlyList<AnnouncementView>> GetPendingAsync(CancellationToken cancellationToken = default)
    {
        var user = await RequireUserAsync(cancellationToken).ConfigureAwait(false);
        var hide = await GetHideNormalAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = MariaDb.Command(connection, """
            SELECT a.id, a.title, a.body_html, a.mandatory, COALESCE(NULLIF(a.published_by,''), a.updated_by), a.published_utc FROM announcements a
            WHERE a.state=@active AND (a.valid_until='' OR a.valid_until>=@today)
              AND (a.audience=@all OR EXISTS (SELECT 1 FROM announcement_users u WHERE u.announcement_id=a.id AND u.user_name=@user))
              AND NOT EXISTS (SELECT 1 FROM announcement_seen s WHERE s.announcement_id=a.id AND s.user_name=@user AND (s.acknowledged=1 OR a.mandatory=0))
            ORDER BY a.mandatory DESC, a.published_utc, a.id
            """, ("@active", AnnouncementRules.StateActive), ("@today", Today()), ("@all", AnnouncementRules.AudienceAll), ("@user", user));
        var items = new List<AnnouncementView>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var mandatory = reader.GetBoolean(3);
            if (hide && !mandatory) continue;
            // The body was cleaned when it was saved; it is cleaned again on the way out (older rows, a hand-edited database).
            items.Add(new AnnouncementView(reader.GetInt32(0), reader.GetString(1), AnnouncementHtml.Clean(reader.GetString(2)), mandatory, reader.GetString(4), MariaTimeText.ParseOrNull(reader.GetString(5))));
        }
        return items;
    }

    public async Task MarkSeenAsync(int announcementId, CancellationToken cancellationToken = default)
    {
        var user = await RequireUserAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = MariaDb.Command(connection, """
            INSERT INTO announcement_seen (announcement_id, user_name, seen_utc, acknowledged)
            SELECT a.id, @user, @now, 0 FROM announcements a WHERE a.id=@id AND a.mandatory=0
            ON DUPLICATE KEY UPDATE seen_utc=VALUES(seen_utc)
            """, ("@id", announcementId), ("@user", user), ("@now", MariaTimeText.Format(DateTime.UtcNow)));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task AcknowledgeAsync(int announcementId, CancellationToken cancellationToken = default)
    {
        var user = await RequireUserAsync(cancellationToken).ConfigureAwait(false);
        string title;
        await using (var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false))
        {
            await using var find = MariaDb.Command(connection, "SELECT title FROM announcements WHERE id=@id AND mandatory=1", ("@id", announcementId));
            title = await find.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string ?? throw new AnnouncementException("Anunțul nu este obligatoriu sau nu mai există.");
            await using var command = MariaDb.Command(connection, """
                INSERT INTO announcement_seen (announcement_id, user_name, seen_utc, acknowledged) VALUES (@id, @user, @now, 1)
                ON DUPLICATE KEY UPDATE seen_utc=VALUES(seen_utc), acknowledged=1
                """, ("@id", announcementId), ("@user", user), ("@now", MariaTimeText.Format(DateTime.UtcNow)));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.Announcement, AuditActions.ConfirmAnnouncementRead, announcementId.ToString(CultureInfo.InvariantCulture), Trim(title, 120),
            $"Utilizator: {user}; anunț obligatoriu: {Trim(title, 120)}", "", cancellationToken).ConfigureAwait(false);
        Publish(AuditActions.ConfirmAnnouncementRead, announcementId);
    }

    public async Task<bool> GetHideNormalAsync(CancellationToken cancellationToken = default)
    {
        var user = await RequireUserAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = MariaDb.Command(connection, "SELECT hide_normal FROM user_announcement_settings WHERE user_name=@user", ("@user", user));
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is { } value && Convert.ToInt32(value, CultureInfo.InvariantCulture) != 0;
    }

    public async Task SetHideNormalAsync(bool hide, CancellationToken cancellationToken = default)
    {
        var user = await RequireUserAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = MariaDb.Command(connection, """
            INSERT INTO user_announcement_settings (user_name, hide_normal) VALUES (@user, @hide) ON DUPLICATE KEY UPDATE hide_normal=VALUES(hide_normal)
            """, ("@user", user), ("@hide", hide ? 1 : 0));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<AnnouncementImageContent?> GetImageAsync(int imageId, CancellationToken cancellationToken = default)
    {
        await RequireUserAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = MariaDb.Command(connection, "SELECT content_type, content FROM announcement_images WHERE id=@id", ("@id", imageId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? new AnnouncementImageContent(reader.GetString(0), (byte[])reader.GetValue(1)) : null;
    }

    // ---------------------------------------------------------------- the administrator's side

    public async Task<IReadOnlyList<AnnouncementRow>> ListAsync(CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync(ViewKey, cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = MariaDb.Command(connection, """
            SELECT a.id, a.title, a.mandatory, a.audience, (SELECT COUNT(*) FROM announcement_users u WHERE u.announcement_id=a.id), a.state, a.valid_until, a.publication_no,
                   a.created_by, a.created_utc, a.published_utc, a.updated_utc,
                   (SELECT COUNT(*) FROM announcement_seen s WHERE s.announcement_id=a.id), (SELECT COUNT(*) FROM announcement_seen s WHERE s.announcement_id=a.id AND s.acknowledged=1)
            FROM announcements a ORDER BY a.id DESC
            """);
        var rows = new List<AnnouncementRow>();
        var now = DateTime.Now;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var state = reader.GetString(5);
            var until = reader.GetString(6);
            rows.Add(new AnnouncementRow(reader.GetInt32(0), reader.GetString(1), reader.GetBoolean(2), reader.GetString(3), Convert.ToInt32(reader.GetValue(4), CultureInfo.InvariantCulture), state, until,
                AnnouncementRules.IsExpired(state, until, now), reader.GetInt32(7), reader.GetString(8), MariaTimeText.Parse(reader.GetString(9)), MariaTimeText.ParseOrNull(reader.GetString(10)), MariaTimeText.ParseOrNull(reader.GetString(11)),
                Convert.ToInt32(reader.GetValue(12), CultureInfo.InvariantCulture), Convert.ToInt32(reader.GetValue(13), CultureInfo.InvariantCulture)));
        }
        return rows;
    }

    public async Task<IReadOnlyList<AnnouncementSeen>> GetSeenByAsync(int id, CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync(ViewKey, cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = MariaDb.Command(connection, """
            SELECT s.user_name, COALESCE(u.display_name,''), s.seen_utc, s.acknowledged FROM announcement_seen s
            LEFT JOIN web_users u ON u.username=s.user_name COLLATE utf8mb4_nopad_bin
            WHERE s.announcement_id=@id ORDER BY s.seen_utc DESC, s.user_name
            """, ("@id", id));
        var list = new List<AnnouncementSeen>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            list.Add(new AnnouncementSeen(reader.GetString(0), reader.GetString(1), MariaTimeText.ParseOrNull(reader.GetString(2)), reader.GetBoolean(3)));
        return list;
    }

    public async Task<AnnouncementDetail?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync(ViewKey, cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = MariaDb.Command(connection, "SELECT title, body_html, mandatory, audience, valid_until, state, publication_no, COALESCE(NULLIF(published_by,''), updated_by), published_utc FROM announcements WHERE id=@id", ("@id", id));
        string title, body, audience, until, state, publishedBy; bool mandatory; int publication; DateTime? publishedUtc;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
            title = reader.GetString(0); body = reader.GetString(1); mandatory = reader.GetBoolean(2); audience = reader.GetString(3); until = reader.GetString(4); state = reader.GetString(5); publication = reader.GetInt32(6); publishedBy = reader.GetString(7); publishedUtc = MariaTimeText.ParseOrNull(reader.GetString(8));
        }
        var users = new List<string>();
        await using var usersCommand = MariaDb.Command(connection, "SELECT user_name FROM announcement_users WHERE announcement_id=@id ORDER BY user_name", ("@id", id));
        await using var usersReader = await usersCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await usersReader.ReadAsync(cancellationToken).ConfigureAwait(false)) users.Add(usersReader.GetString(0));
        return new AnnouncementDetail(id, title, AnnouncementHtml.Clean(body), mandatory, audience, users, AnnouncementRules.DisplayDate(until), state, publication, publishedBy, publishedUtc);
    }

    public Task<int> SaveAsync(AnnouncementInput input, CancellationToken cancellationToken = default) => WriteAsync(input, publish: false, cancellationToken);
    public Task<int> PublishAsync(AnnouncementInput input, CancellationToken cancellationToken = default) => WriteAsync(input, publish: true, cancellationToken);

    private async Task<int> WriteAsync(AnnouncementInput input, bool publish, CancellationToken cancellationToken)
    {
        await access.EnsureAsync(EditKey, cancellationToken).ConfigureAwait(false);
        var user = await RequireUserAsync(cancellationToken).ConfigureAwait(false);
        var title = (input.Title ?? "").Trim();
        if (title.Length == 0) throw new AnnouncementException("Scrie titlul anunțului.");
        if (title.Length > AnnouncementRules.MaxTitle) throw new AnnouncementException($"Titlul poate avea cel mult {AnnouncementRules.MaxTitle} de caractere.");
        if ((input.BodyHtml ?? "").Length > AnnouncementRules.MaxBodyChars * 3) throw new AnnouncementException("Textul anunțului este prea lung.");
        var body = AnnouncementHtml.Clean(input.BodyHtml);
        if (body.Length > AnnouncementRules.MaxBodyChars) throw new AnnouncementException($"Textul anunțului poate avea cel mult {AnnouncementRules.MaxBodyChars:N0} de caractere.");
        if (publish && AnnouncementHtml.PlainText(body).Length == 0 && AnnouncementHtml.ImageIds(body).Count == 0) throw new AnnouncementException("Anunțul nu are conținut.");
        if (input.Audience is not (AnnouncementRules.AudienceAll or AnnouncementRules.AudienceSelected)) throw new AnnouncementException("Alege audiența: toți utilizatorii sau utilizatori selectați.");
        if (!AnnouncementRules.TryParseDisplayDate(input.ValidUntil, out var until)) throw new AnnouncementException("Data „valabil până” trebuie scrisă dd.mm.yyyy.");
        var users = input.Audience == AnnouncementRules.AudienceSelected
            ? input.Users.Select(name => (name ?? "").Trim()).Where(name => name.Length > 0).Distinct(StringComparer.Ordinal).ToList() : [];
        if (users.Count > AnnouncementRules.MaxUsers) throw new AnnouncementException($"Se pot selecta cel mult {AnnouncementRules.MaxUsers} de utilizatori.");
        if (publish)
        {
            if (input.Audience == AnnouncementRules.AudienceSelected && users.Count == 0) throw new AnnouncementException("Alege cel puțin un utilizator sau audiența „toți utilizatorii”.");
            if (until.Length > 0 && string.CompareOrdinal(until, Today()) < 0) throw new AnnouncementException("Data „valabil până” a trecut: alege o dată viitoare sau las-o goală.");
        }
        var now = MariaTimeText.Format(DateTime.UtcNow);
        int id = input.Id;
        string previousState = "", action;
        var changes = new List<string>();
        await using (var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false))
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false))
        {
            string oldTitle = "", oldBody = "", oldAudience = "", oldUntil = ""; bool oldMandatory = false; int publication = 0;
            if (id > 0)
            {
                await using var find = MariaDb.Command(connection, transaction, "SELECT title, body_html, mandatory, audience, valid_until, state, publication_no FROM announcements WHERE id=@id FOR UPDATE", ("@id", id));
                await using var reader = await find.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) throw new AnnouncementException("Anunțul nu mai există.");
                oldTitle = reader.GetString(0); oldBody = reader.GetString(1); oldMandatory = reader.GetBoolean(2); oldAudience = reader.GetString(3); oldUntil = reader.GetString(4); previousState = reader.GetString(5); publication = reader.GetInt32(6);
            }
            var newState = publish ? AnnouncementRules.StateActive : id == 0 ? AnnouncementRules.StateDraft : previousState;
            if (publish) publication++;
            if (id == 0)
            {
                await using var insert = MariaDb.Command(connection, transaction, """
                    INSERT INTO announcements (title, body_html, mandatory, audience, valid_until, state, publication_no, created_by, created_utc, updated_by, updated_utc, published_utc, published_by)
                    VALUES (@title,@body,@mandatory,@audience,@until,@state,@publication,@user,@now,@user,@now,@published,@publisher)
                    """, ("@title", title), ("@body", body), ("@mandatory", input.Mandatory ? 1 : 0), ("@audience", input.Audience), ("@until", until), ("@state", newState), ("@publication", publication),
                    ("@user", user), ("@now", now), ("@published", publish ? now : ""), ("@publisher", publish ? user : ""));
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                id = (int)insert.LastInsertedId;
            }
            else
            {
                await using var update = MariaDb.Command(connection, transaction, """
                    UPDATE announcements SET title=@title, body_html=@body, mandatory=@mandatory, audience=@audience, valid_until=@until, state=@state, publication_no=@publication, updated_by=@user, updated_utc=@now,
                        published_utc=IF(@publish=1,@now,published_utc), published_by=IF(@publish=1,@user,published_by) WHERE id=@id
                    """, ("@id", id), ("@title", title), ("@body", body), ("@mandatory", input.Mandatory ? 1 : 0), ("@audience", input.Audience), ("@until", until), ("@state", newState), ("@publication", publication),
                    ("@user", user), ("@now", now), ("@publish", publish ? 1 : 0));
                await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                if (oldTitle != title) changes.Add($"titlu: „{Trim(oldTitle, 60)}” → „{Trim(title, 60)}”");
                if (oldBody != body) changes.Add("text modificat");
                if (oldMandatory != input.Mandatory) changes.Add($"tip: {(oldMandatory ? "obligatoriu" : "general")} → {(input.Mandatory ? "obligatoriu" : "general")}");
                if (oldAudience != input.Audience) changes.Add($"audiență: {oldAudience} → {input.Audience}");
                if (oldUntil != until) changes.Add($"valabil până: {AnnouncementRules.DisplayDate(oldUntil)} → {AnnouncementRules.DisplayDate(until)}");
            }
            await using (var clear = MariaDb.Command(connection, transaction, "DELETE FROM announcement_users WHERE announcement_id=@id", ("@id", id))) await clear.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            foreach (var name in users)
            {
                await using var add = MariaDb.Command(connection, transaction, "INSERT INTO announcement_users (announcement_id, user_name) VALUES (@id, @user)", ("@id", id), ("@user", name));
                await add.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            // Republishing: everybody targeted receives it again.
            if (publish && previousState != AnnouncementRules.StateDraft && id == input.Id)
                await using (var forget = MariaDb.Command(connection, transaction, "DELETE FROM announcement_seen WHERE announcement_id=@id", ("@id", id))) await forget.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await AttachImagesAsync(connection, transaction, id, body, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            action = publish ? (previousState is "" or AnnouncementRules.StateDraft ? AuditActions.PublishAnnouncement : AuditActions.RepublishAnnouncement)
                : input.Id == 0 ? AuditActions.CreateAnnouncement : AuditActions.EditAnnouncement;
        }
        var details = $"Titlu: {Trim(title, 120)}; tip: {(input.Mandatory ? "obligatoriu" : "general")}; audiență: {(input.Audience == AnnouncementRules.AudienceAll ? "toți" : $"{users.Count} utilizatori")}; valabil până: {(until.Length == 0 ? "—" : AnnouncementRules.DisplayDate(until))}"
            + (changes.Count > 0 ? "; " + string.Join("; ", changes) : "");
        await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.Announcement, action, id.ToString(CultureInfo.InvariantCulture), Trim(title, 120), details, "", cancellationToken).ConfigureAwait(false);
        Publish(action, id);
        return id;
    }

    // The pictures the body shows belong to the announcement; the ones of it that the body no longer shows are removed.
    private static async Task AttachImagesAsync(MySqlConnection connection, MySqlTransaction transaction, int id, string body, CancellationToken cancellationToken)
    {
        var ids = AnnouncementHtml.ImageIds(body);
        if (ids.Count > 0)
        {
            await using var attach = MariaDb.Command(connection, transaction,
                $"UPDATE announcement_images SET announcement_id=@id WHERE id IN ({string.Join(',', ids)}) AND (announcement_id IS NULL OR announcement_id=@id)", ("@id", id));
            await attach.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        var keep = ids.Count == 0 ? "0" : string.Join(',', ids);
        await using var drop = MariaDb.Command(connection, transaction, $"DELETE FROM announcement_images WHERE announcement_id=@id AND id NOT IN ({keep})", ("@id", id));
        await drop.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        // Pictures added in an editor that was never saved.
        await using var orphans = MariaDb.Command(connection, transaction, "DELETE FROM announcement_images WHERE announcement_id IS NULL AND created_utc<@limit", ("@limit", MariaTimeText.Format(DateTime.UtcNow.AddDays(-2))));
        await orphans.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ActivateAsync(int id, CancellationToken cancellationToken = default)
    {
        var detail = await GetAsync(id, cancellationToken).ConfigureAwait(false) ?? throw new AnnouncementException("Anunțul nu mai există.");
        await PublishAsync(new AnnouncementInput(id, detail.Title, detail.BodyHtml, detail.Mandatory, detail.Audience, detail.Users, detail.ValidUntil), cancellationToken).ConfigureAwait(false);
    }

    public async Task ArchiveAsync(int id, CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync(EditKey, cancellationToken).ConfigureAwait(false);
        var user = await RequireUserAsync(cancellationToken).ConfigureAwait(false);
        string title;
        await using (var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false))
        {
            await using var find = MariaDb.Command(connection, "SELECT title FROM announcements WHERE id=@id AND state=@active", ("@id", id), ("@active", AnnouncementRules.StateActive));
            title = await find.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string ?? throw new AnnouncementException("Doar un anunț activ poate fi arhivat.");
            await using var update = MariaDb.Command(connection, "UPDATE announcements SET state=@archived, updated_by=@user, updated_utc=@now WHERE id=@id",
                ("@id", id), ("@archived", AnnouncementRules.StateArchived), ("@user", user), ("@now", MariaTimeText.Format(DateTime.UtcNow)));
            await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.Announcement, AuditActions.ArchiveAnnouncement, id.ToString(CultureInfo.InvariantCulture), Trim(title, 120),
            $"Titlu: {Trim(title, 120)}; stare: activ → arhivat", "", cancellationToken).ConfigureAwait(false);
        Publish(AuditActions.ArchiveAnnouncement, id);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync(EditKey, cancellationToken).ConfigureAwait(false);
        string title, state; int seen;
        await using (var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false))
        {
            await using (var find = MariaDb.Command(connection, "SELECT title, state, (SELECT COUNT(*) FROM announcement_seen WHERE announcement_id=@id) FROM announcements WHERE id=@id", ("@id", id)))
            await using (var reader = await find.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) throw new AnnouncementException("Anunțul nu mai există.");
                title = reader.GetString(0); state = reader.GetString(1); seen = Convert.ToInt32(reader.GetValue(2), CultureInfo.InvariantCulture);
            }
            await using var delete = MariaDb.Command(connection, "DELETE FROM announcements WHERE id=@id", ("@id", id));
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.Announcement, AuditActions.DeleteAnnouncement, id.ToString(CultureInfo.InvariantCulture), Trim(title, 120),
            $"Titlu: {Trim(title, 120)}; stare: {state}; utilizatori care îl văzuseră: {seen}", "", cancellationToken).ConfigureAwait(false);
        Publish(AuditActions.DeleteAnnouncement, id);
    }

    public async Task<AnnouncementUploaded> UploadImageAsync(ReportImage image, CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync(EditKey, cancellationToken).ConfigureAwait(false);
        var user = await RequireUserAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        return await StoreImageAsync(connection, null, image, user, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<AnnouncementUploaded> StoreImageAsync(MySqlConnection connection, MySqlTransaction? transaction, ReportImage image, string user, CancellationToken cancellationToken)
    {
        if (image.Content.Length == 0 || image.Content.Length > ReportImageRules.MaxImageBytes)
            throw new AnnouncementException($"Imaginea „{image.FileName}” trebuie să aibă cel mult {ReportImageRules.MaxImageBytes / 1024 / 1024} MB.");
        var type = ReportImageRules.DetectImageType(image.Content) ?? throw new AnnouncementException($"Fișierul „{image.FileName}” nu este o imagine (PNG, JPEG, GIF sau WebP).");
        await using var count = MariaDb.Command(connection, transaction, "SELECT COUNT(*) FROM announcement_images WHERE announcement_id IS NULL AND created_by=@user", ("@user", user));
        if (Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture) >= ReportImageRules.MaxImages * 4)
            throw new AnnouncementException("Ai adăugat prea multe poze nesalvate: salvează anunțul.");
        await using var insert = MariaDb.Command(connection, transaction, """
            INSERT INTO announcement_images (announcement_id, file_name, content_type, size_bytes, content, created_by, created_utc) VALUES (NULL,@name,@type,@size,@content,@user,@now)
            """, ("@name", ReportImageRules.SafeName(image.FileName, "imagine" + ReportImageRules.Extension(type))), ("@type", type), ("@size", image.Content.Length), ("@content", image.Content), ("@user", user), ("@now", MariaTimeText.Format(DateTime.UtcNow)));
        await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        var id = (int)insert.LastInsertedId;
        return new AnnouncementUploaded(id, AnnouncementRules.ImageUrl(id));
    }

    public async Task<IReadOnlyList<AnnouncementUserChoice>> GetUserChoicesAsync(CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync(EditKey, cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = MariaDb.Command(connection, "SELECT username, display_name FROM web_users WHERE is_active=1 ORDER BY username");
        var list = new List<AnnouncementUserChoice>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) list.Add(new AnnouncementUserChoice(reader.GetString(0), reader.GetString(1)));
        return list;
    }

    // ---------------------------------------------------------------- ZIP (anunt.json, continut.html, imagini/)

    private sealed class ZipManifest
    {
        public int Versiune { get; set; } = 1;
        public string Titlu { get; set; } = "";
        public bool Obligatoriu { get; set; }
        public string Audienta { get; set; } = "toti";
        public List<string>? Utilizatori { get; set; }
        public string? ValidPana { get; set; }
    }

    private static readonly JsonSerializerOptions ZipJson = new() { PropertyNameCaseInsensitive = true, WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public async Task<AnnouncementImportResult> ImportZipAsync(Stream zip, CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync(EditKey, cancellationToken).ConfigureAwait(false);
        var user = await RequireUserAsync(cancellationToken).ConfigureAwait(false);
        var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await zip.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > AnnouncementRules.MaxZipBytes) throw new AnnouncementException($"Arhiva poate avea cel mult {AnnouncementRules.MaxZipBytes / 1024 / 1024} MB.");
        }
        buffer.Position = 0;
        ZipArchive archive;
        try { archive = new ZipArchive(buffer, ZipArchiveMode.Read); }
        catch (InvalidDataException) { throw new AnnouncementException("Fișierul nu este o arhivă ZIP validă."); }
        var warnings = new List<string>();
        string manifestText = "", html = "";
        var pictures = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        using (archive)
        {
            if (archive.Entries.Count > AnnouncementRules.MaxZipEntries) throw new AnnouncementException($"Arhiva poate avea cel mult {AnnouncementRules.MaxZipEntries} de fișiere.");
            long unpacked = 0;
            foreach (var entry in archive.Entries)
            {
                var path = entry.FullName.Replace('\\', '/');
                if (path.EndsWith('/')) continue;
                if (path.StartsWith('/') || path.Split('/').Any(part => part is ".." or "." || part.Contains(':'))) throw new AnnouncementException($"Calea „{Trim(path, 80)}” din arhivă nu este permisă.");
                unpacked += entry.Length;
                if (unpacked > AnnouncementRules.MaxZipUnpackedBytes || entry.Length > AnnouncementRules.MaxZipUnpackedBytes) throw new AnnouncementException("Conținutul arhivei este prea mare.");
                if (path.Equals("anunt.json", StringComparison.OrdinalIgnoreCase)) manifestText = await ReadEntryAsync(entry, 1_000_000, cancellationToken).ConfigureAwait(false);
                else if (path.Equals("continut.html", StringComparison.OrdinalIgnoreCase)) html = await ReadEntryAsync(entry, AnnouncementRules.MaxBodyChars * 3, cancellationToken).ConfigureAwait(false);
                else if (path.StartsWith("imagini/", StringComparison.OrdinalIgnoreCase))
                {
                    if (entry.Length > ReportImageRules.MaxImageBytes) throw new AnnouncementException($"Imaginea „{Trim(path, 80)}” depășește {ReportImageRules.MaxImageBytes / 1024 / 1024} MB.");
                    await using var stream = entry.Open();
                    using var memory = new MemoryStream();
                    await stream.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
                    pictures[path] = memory.ToArray();
                }
                else warnings.Add($"Fișierul „{Trim(path, 80)}” nu face parte din format și a fost ignorat.");
            }
        }
        if (manifestText.Length == 0) throw new AnnouncementException("Arhiva nu conține anunt.json.");
        if (html.Length == 0) throw new AnnouncementException("Arhiva nu conține continut.html.");
        if (pictures.Count > ReportImageRules.MaxImages * 4) throw new AnnouncementException("Arhiva conține prea multe imagini.");
        ZipManifest manifest;
        try { manifest = JsonSerializer.Deserialize<ZipManifest>(manifestText, ZipJson) ?? throw new JsonException(); }
        catch (JsonException) { throw new AnnouncementException("anunt.json nu este valid."); }
        if (manifest.Versiune != 1) throw new AnnouncementException("Versiunea formatului din anunt.json nu este cunoscută.");
        var audience = manifest.Audienta.Trim().ToLowerInvariant() switch
        {
            "toti" or "all" => AnnouncementRules.AudienceAll,
            "selectati" or "selected" => AnnouncementRules.AudienceSelected,
            _ => throw new AnnouncementException("În anunt.json, „audienta” trebuie să fie „toti” sau „selectati”.")
        };
        var requested = (manifest.Utilizatori ?? []).Select(name => (name ?? "").Trim()).Where(name => name.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        var known = (await GetUserChoicesAsync(cancellationToken).ConfigureAwait(false)).Select(choice => choice.UserName).ToHashSet(StringComparer.Ordinal);
        var users = new List<string>();
        foreach (var name in requested)
            if (known.Contains(name)) users.Add(name); else warnings.Add($"Utilizatorul „{Trim(name, 60)}” nu există sau este inactiv și a fost omis.");
        if (audience == AnnouncementRules.AudienceSelected && users.Count == 0) { warnings.Add("Niciun utilizator selectat valid: audiența a rămas „toți utilizatorii” și trebuie verificată în editor."); audience = AnnouncementRules.AudienceAll; }
        var until = manifest.ValidPana ?? "";
        if (!AnnouncementRules.TryParseDisplayDate(until, out _)) { warnings.Add("Data „validPana” nu este dd.mm.yyyy și a fost omisă."); until = ""; }

        // The pictures first (they get identifiers), then the body with the addresses of the pictures replaced, then the draft.
        var uploaded = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using (var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false))
            foreach (var (path, bytes) in pictures)
            {
                try { uploaded[path] = (await StoreImageAsync(connection, null, new ReportImage(ReportImageRules.KindFile, Path.GetFileName(path), bytes), user, cancellationToken).ConfigureAwait(false)).Url; }
                catch (AnnouncementException exception) { warnings.Add(exception.Message + " Imaginea a fost omisă."); }
            }
        var body = AnnouncementHtml.Clean(html, source =>
        {
            var key = source.Replace('\\', '/').TrimStart('.', '/');
            if (uploaded.TryGetValue(key, out var url)) return url;
            warnings.Add($"Imaginea „{Trim(source, 80)}” din continut.html nu este în arhivă și a fost omisă.");
            return null;
        });
        var id = await SaveAsync(new AnnouncementInput(0, manifest.Titlu, body, manifest.Obligatoriu, audience, users, until), cancellationToken).ConfigureAwait(false);
        return new AnnouncementImportResult(id, warnings);
    }

    private static async Task<string> ReadEntryAsync(ZipArchiveEntry entry, int maxChars, CancellationToken cancellationToken)
    {
        await using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        var text = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        if (text.Length > maxChars) throw new AnnouncementException($"Fișierul „{entry.FullName}” este prea mare.");
        return text.TrimStart('﻿');
    }

    public async Task<AnnouncementZip> ExportZipAsync(int id, CancellationToken cancellationToken = default)
    {
        var detail = await GetAsync(id, cancellationToken).ConfigureAwait(false) ?? throw new AnnouncementException("Anunțul nu mai există.");
        var html = detail.BodyHtml;
        var entries = new List<(string Path, byte[] Content)>();
        var index = 0;
        foreach (var imageId in AnnouncementHtml.ImageIds(html))
        {
            var image = await GetImageAsync(imageId, cancellationToken).ConfigureAwait(false);
            if (image is null) continue;
            index++;
            var path = $"imagini/poza{index}{ReportImageRules.Extension(image.ContentType)}";
            html = html.Replace(AnnouncementRules.ImageUrl(imageId) + "\"", path + "\"", StringComparison.Ordinal);
            entries.Add((path, image.Content));
        }
        var manifest = new ZipManifest
        {
            Titlu = detail.Title, Obligatoriu = detail.Mandatory, Audienta = detail.Audience == AnnouncementRules.AudienceAll ? "toti" : "selectati",
            Utilizatori = detail.Audience == AnnouncementRules.AudienceAll ? [] : [.. detail.Users], ValidPana = detail.ValidUntil
        };
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            Add(archive, "anunt.json", Encoding.UTF8.GetBytes(JsonSerializer.Serialize(manifest, ZipJson)));
            Add(archive, "continut.html", Encoding.UTF8.GetBytes(html));
            foreach (var (path, content) in entries) Add(archive, path, content);
        }
        var safe = ReportImageRules.SafeName(detail.Title, "anunt");
        return new AnnouncementZip($"anunt-{id}-{safe}.zip", output.ToArray());
    }

    private static void Add(ZipArchive archive, string path, byte[] content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(content);
    }

    private void Publish(string action, int id) =>
        changeFeed?.Publish(new ChangeEvent(AuditEntities.Announcement, action, id.ToString(CultureInfo.InvariantCulture), origin?.Id ?? Guid.Empty, DateTime.UtcNow));
}
