using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using BlazorStoc.Modules.Announcements;
using BlazorStoc.Services;

namespace BlazorStoc.Checks;

public static partial class MariaExtendedChecks
{
    // ---- Anunturi ------------------------------------------------------------------------------------------------------------
    private static async Task AnnouncementsAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe)
    {
        var started = DateTime.UtcNow.AddSeconds(-1);
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
        var editor = new MariaAnnouncementService(configuration, new TaskTestAccess(null, "proba.anunt.ed", false, "setari-anunturi.view", "setari-anunturi.edit"), audit);
        var ana = new MariaAnnouncementService(configuration, new TestAccessControl(false, "proba.anunt.ana"), audit);
        var bob = new MariaAnnouncementService(configuration, new TestAccessControl(false, "proba.anunt.bob"), audit);
        var title = "Proba anunt " + Suffix();
        var createdIds = new List<int>();
        try
        {
            // Guards: an ordinary user cannot read the list, write, delete or upload.
            var deniedList = await Rejects<AccessDeniedException>(() => ana.ListAsync(), "An ordinary user cannot list the announcements");
            var deniedSave = await Rejects<AccessDeniedException>(() => ana.SaveAsync(new AnnouncementInput(0, "x", "<p>x</p>", false, AnnouncementRules.AudienceAll, [], "")), "An ordinary user cannot save an announcement");
            var deniedUpload = await Rejects<AccessDeniedException>(() => ana.UploadImageAsync(new ReportImage(ReportImageRules.KindFile, "a.png", png)), "An ordinary user cannot upload a picture");
            Check(deniedList is not null && deniedSave is not null && deniedUpload is not null, "Announcements: listing, saving and uploading need the key of the page");
            var noTitle = await Rejects<AnnouncementException>(() => editor.SaveAsync(new AnnouncementInput(0, " ", "<p>x</p>", false, AnnouncementRules.AudienceAll, [], "")), "An announcement without a title is refused");
            var badDate = await Rejects<AnnouncementException>(() => editor.SaveAsync(new AnnouncementInput(0, "t", "<p>x</p>", false, AnnouncementRules.AudienceAll, [], "2026-12-31")), "A date not written dd.mm.yyyy is refused");
            var emptyPublish = await Rejects<AnnouncementException>(() => editor.PublishAsync(new AnnouncementInput(0, "t", "<p> </p>", false, AnnouncementRules.AudienceAll, [], "")), "An empty announcement is not published");
            var noUsers = await Rejects<AnnouncementException>(() => editor.PublishAsync(new AnnouncementInput(0, "t", "<p>x</p>", false, AnnouncementRules.AudienceSelected, [], "")), "Selected audience without users is not published");
            var pastDate = await Rejects<AnnouncementException>(() => editor.PublishAsync(new AnnouncementInput(0, "t", "<p>x</p>", false, AnnouncementRules.AudienceAll, [], "01.01.2020")), "A validity date in the past is not published");
            Check(noTitle is not null && badDate is not null && emptyPublish is not null && noUsers is not null && pastDate is not null, "Announcements: missing title, bad date, empty text, empty audience and a past date are refused");

            // Draft with a picture; the body is cleaned on the server; a draft reaches nobody.
            var upload = await editor.UploadImageAsync(new ReportImage(ReportImageRules.KindPasted, "lipit.png", png));
            var notImage = await Rejects<AnnouncementException>(() => editor.UploadImageAsync(new ReportImage(ReportImageRules.KindFile, "a.txt", Encoding.UTF8.GetBytes("nu e imagine"))), "A file that is not a picture is refused");
            Check(upload.Url == AnnouncementRules.ImageUrl(upload.Id) && notImage is not null, "Announcements: a picture is stored and a non-picture refused");
            var draftId = await editor.SaveAsync(new AnnouncementInput(0, title, $"<p onclick=\"x()\">Salut <script>alert(1)</script><strong>lume</strong></p><img src=\"{upload.Url}\">", false, AnnouncementRules.AudienceAll, [], ""));
            createdIds.Add(draftId);
            var draft = await editor.GetAsync(draftId);
            Check(draft is { State: AnnouncementRules.StateDraft } && !draft.BodyHtml.Contains("script") && !draft.BodyHtml.Contains("onclick") && draft.BodyHtml.Contains("<strong>lume</strong>") && draft.BodyHtml.Contains(upload.Url), "Announcements: a new announcement is a draft with the body cleaned on the server");
            Check((await ana.GetPendingAsync()).All(item => item.Id != draftId), "Announcements: a draft is shown to nobody");
            var image = await ana.GetImageAsync(upload.Id);
            Check(image is { ContentType: "image/png" } && image.Content.SequenceEqual(png), "Announcements: the picture saved with the announcement is read back byte for byte by a signed-in user");

            // Publishing: everybody; the user sees it once (normal: marked seen at close).
            await editor.PublishAsync(new AnnouncementInput(draftId, title, draft!.BodyHtml, false, AnnouncementRules.AudienceAll, [], ""));
            Check((await ana.GetPendingAsync()).Any(item => item.Id == draftId && !item.Mandatory) && (await bob.GetPendingAsync()).Any(item => item.Id == draftId), "Announcements: a published announcement for everybody waits for every user");
            var shown = (await ana.GetPendingAsync()).First(item => item.Id == draftId);
            Check(shown.PublishedBy == "proba.anunt.ed" && shown.PublishedUtc is { } at && Math.Abs((DateTime.UtcNow - at).TotalMinutes) < 5 && AnnouncementRules.Capitalize(shown.PublishedBy) == "Proba.anunt.ed", "Announcements: the window shows who published and when (user name with a capital first letter, date and hour of the publishing)");
            await ana.MarkSeenAsync(draftId);
            Check((await ana.GetPendingAsync()).All(item => item.Id != draftId) && (await bob.GetPendingAsync()).Any(item => item.Id == draftId), "Announcements: a normal announcement marked seen no longer comes for that user only");

            // The preference "do not show at start" stops the normal ones, never the mandatory ones.
            var mandatoryId = await editor.PublishAsync(new AnnouncementInput(0, title + " obligatoriu", "<p>Important</p>", true, AnnouncementRules.AudienceSelected, ["proba.anunt.ana"], ""));
            createdIds.Add(mandatoryId);
            Check((await ana.GetPendingAsync()).Any(item => item.Id == mandatoryId && item.Mandatory) && (await bob.GetPendingAsync()).All(item => item.Id != mandatoryId), "Announcements: selected audience reaches only the selected users");
            await bob.SetHideNormalAsync(true);
            var bobPending = await bob.GetPendingAsync();
            Check(await bob.GetHideNormalAsync() && bobPending.All(item => item.Id != draftId) && !(await ana.GetHideNormalAsync()), "Announcements: the preference stops the normal announcements for that user and is kept in the database");
            await ana.SetHideNormalAsync(true);
            await ana.MarkSeenAsync(mandatoryId);
            Check((await ana.GetPendingAsync()).Any(item => item.Id == mandatoryId), "Announcements: the preference and the seen mark do not stop a mandatory announcement");
            await ana.AcknowledgeAsync(mandatoryId);
            Check((await ana.GetPendingAsync()).All(item => item.Id != mandatoryId), "Announcements: \"Am citit\" stops a mandatory announcement for that user");
            var seenBy = await editor.GetSeenByAsync(mandatoryId);
            var deniedSeenBy = await Rejects<AccessDeniedException>(() => ana.GetSeenByAsync(mandatoryId), "An ordinary user cannot see who read an announcement");
            Check(seenBy.Count == 1 && seenBy[0].UserName == "proba.anunt.ana" && seenBy[0].Acknowledged && seenBy[0].SeenUtc is not null && deniedSeenBy is not null, "Announcements: the administrator sees who read a mandatory announcement and when; a user without the key cannot");
            var ackNormal = await Rejects<AnnouncementException>(() => ana.AcknowledgeAsync(draftId), "\"Am citit\" on a normal announcement is refused");
            Check(ackNormal is not null, "Announcements: only a mandatory announcement is confirmed");

            // Republishing with an edit: everybody targeted gets it again (the seen/read state is erased).
            await ana.SetHideNormalAsync(false); await bob.SetHideNormalAsync(false);
            await editor.PublishAsync(new AnnouncementInput(mandatoryId, title + " obligatoriu", "<p>Important, modificat</p>", true, AnnouncementRules.AudienceSelected, ["proba.anunt.ana"], ""));
            Check((await ana.GetPendingAsync()).Any(item => item.Id == mandatoryId && item.BodyHtml.Contains("modificat")), "Announcements: republishing with an edit sends the announcement again to who had confirmed it");
            var rows = await editor.ListAsync();
            Check(rows.Any(row => row.Id == mandatoryId && row.PublicationNo == 2 && row.State == AnnouncementRules.StateActive && row.Mandatory && row.AudienceText == "1 selectați"), "Announcements: the table counts the publications and shows type, audience and state");

            // Expired and archived ones are not shown; the table shows the expiry.
            await using (var expire = new MySqlCommand("UPDATE announcements SET valid_until='2020-01-01' WHERE id=@id", probe)) { expire.Parameters.AddWithValue("@id", draftId); await expire.ExecuteNonQueryAsync(); }
            Check((await bob.GetPendingAsync()).All(item => item.Id != draftId) && (await editor.ListAsync()).First(row => row.Id == draftId) is { Expired: true, StateText: "Arhivat (expirat)" }, "Announcements: an announcement past its date is not shown and appears as archived (expired)");
            await editor.ArchiveAsync(mandatoryId);
            Check((await ana.GetPendingAsync()).All(item => item.Id != mandatoryId) && (await editor.GetAsync(mandatoryId))!.State == AnnouncementRules.StateArchived, "Announcements: an archived announcement is not shown");
            var archiveDraft = await Rejects<AnnouncementException>(() => editor.ArchiveAsync(mandatoryId), "An announcement already archived cannot be archived again");
            Check(archiveDraft is not null, "Announcements: only an active announcement is archived");

            // The On/Off switch of a row: On activates a draft or an archived announcement as saved (the seen/read state is erased), Off archives; a past date refuses the activation.
            await editor.ActivateAsync(mandatoryId);
            Check((await editor.GetAsync(mandatoryId))!.State == AnnouncementRules.StateActive && (await ana.GetPendingAsync()).Any(item => item.Id == mandatoryId), "Announcements: activating an archived announcement shows it again to the users");
            await ana.AcknowledgeAsync(mandatoryId);
            var beforeRepublish = (await editor.ListAsync()).First(row => row.Id == mandatoryId).PublicationNo;
            await editor.ActivateAsync(mandatoryId);
            Check((await ana.GetPendingAsync()).Any(item => item.Id == mandatoryId) && (await editor.ListAsync()).First(row => row.Id == mandatoryId).PublicationNo == beforeRepublish + 1, "Announcements: republishing without editing sends an active announcement again and counts the publication");
            var switchDraft = await editor.SaveAsync(new AnnouncementInput(0, title + " draft", "<p>Draft de activat</p>", false, AnnouncementRules.AudienceAll, [], ""));
            createdIds.Add(switchDraft);
            await editor.ActivateAsync(switchDraft);
            Check((await editor.GetAsync(switchDraft))!.State == AnnouncementRules.StateActive && (await bob.GetPendingAsync()).Any(item => item.Id == switchDraft), "Announcements: activating a draft publishes it as it is saved");
            await editor.ArchiveAsync(switchDraft);
            await using (var past = new MySqlCommand("UPDATE announcements SET valid_until='2020-01-01' WHERE id=@id", probe)) { past.Parameters.AddWithValue("@id", switchDraft); await past.ExecuteNonQueryAsync(); }
            var expiredActivation = await Rejects<AnnouncementException>(() => editor.ActivateAsync(switchDraft), "An announcement whose date has passed is not activated");
            var deniedActivate = await Rejects<AccessDeniedException>(() => ana.ActivateAsync(switchDraft), "An ordinary user cannot activate");
            Check(expiredActivation is not null && deniedActivate is not null, "Announcements: activation is refused after the date and for a user without the key");

            // ZIP: export and import round trip, then the guards.
            var zip = await editor.ExportZipAsync(draftId);
            using (var archive = new ZipArchive(new MemoryStream(zip.Content)))
                Check(archive.GetEntry("anunt.json") is not null && archive.GetEntry("continut.html") is not null && archive.Entries.Any(entry => entry.FullName.StartsWith("imagini/", StringComparison.Ordinal)), "Announcements: the ZIP has anunt.json, continut.html and the pictures");
            var imported = await editor.ImportZipAsync(new MemoryStream(zip.Content));
            createdIds.Add(imported.Id);
            var importedDetail = await editor.GetAsync(imported.Id);
            Check(importedDetail is { State: AnnouncementRules.StateDraft } && importedDetail.Title == title && AnnouncementHtml.ImageIds(importedDetail.BodyHtml).Count == 1 && !importedDetail.BodyHtml.Contains(upload.Url), "Announcements: the uploaded ZIP becomes a new draft with its own copy of the pictures");

            byte[] Zip(Action<ZipArchive> fill) { var memory = new MemoryStream(); using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, true)) fill(archive); return memory.ToArray(); }
            void Put(ZipArchive archive, string path, string text) { using var writer = new StreamWriter(archive.CreateEntry(path).Open(), new UTF8Encoding(false)); writer.Write(text); }
            var manual = Zip(archive =>
            {
                Put(archive, "anunt.json", "{\"versiune\":1,\"titlu\":\"Din ZIP\",\"obligatoriu\":true,\"audienta\":\"selectati\",\"utilizatori\":[\"nu.exista.xyz\"],\"validPana\":\"31.12.2099\"}");
                Put(archive, "continut.html", "<p>Text <img src=\"imagini/lipsa.png\"><img src=\"imagini/p.png\"><script>x()</script></p>");
                using var picture = archive.CreateEntry("imagini/p.png").Open(); picture.Write(png);
            });
            var manualResult = await editor.ImportZipAsync(new MemoryStream(manual));
            createdIds.Add(manualResult.Id);
            var manualDetail = await editor.GetAsync(manualResult.Id);
            Check(manualDetail is { Mandatory: true, ValidUntil: "31.12.2099", Audience: AnnouncementRules.AudienceAll } && manualResult.Warnings.Any(w => w.Contains("nu.exista.xyz")) && manualResult.Warnings.Any(w => w.Contains("lipsa.png")) && !manualDetail.BodyHtml.Contains("script") && AnnouncementHtml.ImageIds(manualDetail.BodyHtml).Count == 1,
                "Announcements: a hand-made ZIP is imported as a draft; unknown users and missing pictures are reported, the text is cleaned");
            var traversal = Zip(archive => { Put(archive, "anunt.json", "{\"versiune\":1,\"titlu\":\"x\"}"); Put(archive, "continut.html", "<p>x</p>"); Put(archive, "imagini/../../evil.png", "x"); });
            var refusedTraversal = await Rejects<AnnouncementException>(() => editor.ImportZipAsync(new MemoryStream(traversal)), "A path with .. in the ZIP is refused");
            var noJson = await Rejects<AnnouncementException>(() => editor.ImportZipAsync(new MemoryStream(Zip(archive => Put(archive, "continut.html", "<p>x</p>")))), "A ZIP without anunt.json is refused");
            var notZip = await Rejects<AnnouncementException>(() => editor.ImportZipAsync(new MemoryStream(Encoding.UTF8.GetBytes("nu e zip"))), "A file that is not a ZIP is refused");
            var deniedImport = await Rejects<AccessDeniedException>(() => ana.ImportZipAsync(new MemoryStream(manual)), "An ordinary user cannot import");
            Check(refusedTraversal is not null && noJson is not null && notZip is not null && deniedImport is not null, "Announcements: a path outside the ZIP, a missing anunt.json, a non-ZIP and a user without the key are refused");

            // Deletion: definitive, with the pictures and the seen state.
            await editor.DeleteAsync(draftId);
            await using (var left = new MySqlCommand("SELECT (SELECT COUNT(*) FROM announcements WHERE id=@id)+(SELECT COUNT(*) FROM announcement_seen WHERE announcement_id=@id)+(SELECT COUNT(*) FROM announcement_images WHERE announcement_id=@id)", probe))
            {
                left.Parameters.AddWithValue("@id", draftId);
                Check(Convert.ToInt32(await left.ExecuteScalarAsync()) == 0 && await ana.GetImageAsync(upload.Id) is null, "Announcements: the definitive deletion removes the announcement, its pictures and the seen state");
            }
            createdIds.Remove(draftId);

            // Journal: each operation has its own action.
            var events = (await audit.GetEventsAsync()).Where(item => item.TimestampUtc >= started && item.EntityType == AuditEntities.Announcement).Select(item => item.Action).ToHashSet();
            Check(new[] { AuditActions.CreateAnnouncement, AuditActions.PublishAnnouncement, AuditActions.RepublishAnnouncement, AuditActions.ArchiveAnnouncement, AuditActions.DeleteAnnouncement, AuditActions.ConfirmAnnouncementRead }.All(events.Contains),
                "Announcements: creation, publishing, republishing, archiving, deletion and the read confirmation are journaled with their own actions");
        }
        finally
        {
            await using var cleanup = new MySqlCommand("DELETE FROM announcements WHERE created_by='proba.anunt.ed'", probe); await cleanup.ExecuteNonQueryAsync();
            await using var images = new MySqlCommand("DELETE FROM announcement_images WHERE created_by='proba.anunt.ed'", probe); await images.ExecuteNonQueryAsync();
            await using var settings = new MySqlCommand("DELETE FROM user_announcement_settings WHERE user_name LIKE 'proba.anunt.%'", probe); await settings.ExecuteNonQueryAsync();
        }
    }
}
