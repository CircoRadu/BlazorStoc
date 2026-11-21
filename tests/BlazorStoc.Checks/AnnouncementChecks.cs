using BlazorStoc.Modules.Announcements;
using BlazorStoc.Services;

namespace BlazorStoc.Checks;

// The announcements without a server: the cleaning of the formatted text (the security test: the body reaches every user), the rules of dates and states, the permission key, the journal actions.
public static class AnnouncementChecks
{
    public static Task RunAsync(Action<bool, string> check)
    {
        Console.WriteLine("=== Announcements ===");

        // Allowed formatting survives.
        var good = AnnouncementHtml.Clean("<h2>Titlu</h2><p style=\"text-align: center; color: rgb(10, 20, 30)\"><strong>aldin</strong> <em>cursiv</em> <u>sub</u></p><ul><li>unu</li></ul><a href=\"https://exemplu.ro/x?a=1&b=2\">link</a>");
        check(good.Contains("<h2>Titlu</h2>") && good.Contains("<strong>aldin</strong>") && good.Contains("<em>cursiv</em>") && good.Contains("<u>sub</u>") && good.Contains("<li>unu</li>") &&
              good.Contains("text-align:center") && good.Contains("color:rgb(10, 20, 30)") && good.Contains("href=\"https://exemplu.ro/x?a=1&amp;b=2\"") && good.Contains("rel=\"noopener noreferrer\""),
            "Announcement HTML: titles, bold, italic, underline, lists, alignment, color and https links are kept");

        // Scripts, events, dangerous addresses and styles are gone.
        var attacks = new[]
        {
            "<script>alert(1)</script>ok", "<SCRIPT SRC=//x.y/a.js></SCRIPT>", "<img src=x onerror=alert(1)>", "<p onclick=\"alert(1)\">t</p>", "<a href=\"javascript:alert(1)\">x</a>",
            "<a href=\" jav&#x09;ascript:alert(1)\">x</a>", "<a href=\"data:text/html;base64,PHNjcmlwdD4=\">x</a>", "<iframe src=\"https://x.y\"></iframe>", "<svg onload=alert(1)></svg>",
            "<p style=\"background:url(javascript:alert(1))\">x</p>", "<p style=\"position:fixed;top:0;left:0;width:100%\">x</p>", "<img src=\"https://x.y/track.png\">", "<img src=\"data:image/png;base64,AAAA\">",
            "<style>body{display:none}</style>", "<object data=x></object>", "<form action=x><input name=a></form>", "<math><mi xlink:href=\"javascript:alert(1)\">x</mi></math>", "<!-- <script>alert(1)</script> -->",
            "<div><<script>alert(1)//<</script>", "<a href=\"//evil.example/x\">x</a>", "<a href='vbscript:msgbox(1)'>x</a>", "<img src=\"/media/anunturi/1\" onload=\"alert(1)\">", "<body onload=alert(1)>", "<link rel=stylesheet href=x>",
            "<p style=\"color:red;expression(alert(1))\">x</p>", "<span style=\"color:#fff\" onmouseover=\"x()\">x</span>"
        };
        var leaks = attacks.Select(attack => (Attack: attack, Clean: AnnouncementHtml.Clean(attack))).Where(item =>
        {
            var lower = item.Clean.ToLowerInvariant();
            return lower.Contains("<script") || lower.Contains("onerror") || lower.Contains("onclick") || lower.Contains("onload") || lower.Contains("onmouseover") || lower.Contains("javascript:") || lower.Contains("vbscript:") ||
                   lower.Contains("<iframe") || lower.Contains("<svg") || lower.Contains("<style") || lower.Contains("<object") || lower.Contains("<form") || lower.Contains("<input") || lower.Contains("<math") || lower.Contains("<link") ||
                   lower.Contains("data:") || lower.Contains("position") || lower.Contains("expression") || lower.Contains("url(") || lower.Contains("//evil") || lower.Contains("track.png");
        }).ToList();
        check(leaks.Count == 0, "Announcement HTML: scripts, event attributes, javascript:/data:/vbscript: addresses, iframes, svg, forms, styles other than color/alignment and foreign pictures are removed"
            + (leaks.Count > 0 ? ": " + string.Join(" | ", leaks.Take(3).Select(item => item.Attack + " => " + item.Clean)) : ""));
        check(AnnouncementHtml.Clean("<script>alert(1)</script>text vizibil").Contains("text vizibil") && !AnnouncementHtml.Clean("<script>alert(1)</script>text").Contains("alert"), "Announcement HTML: the content of a script goes with the tag, the text around stays");

        // Pictures only from the announcements' own store; unclosed tags are closed; text is escaped.
        check(AnnouncementHtml.Clean("<img src=\"/media/anunturi/12\" alt=\"poza\">").Contains("src=\"/media/anunturi/12\"") && !AnnouncementHtml.Clean("<img src=\"/media/products/12\">").Contains("<img"), "Announcement HTML: only pictures of the announcements' store are kept");
        check(AnnouncementHtml.Clean("<p><strong>a").EndsWith("</strong></p>", StringComparison.Ordinal) && AnnouncementHtml.Clean("1 < 2 & 3 > 2").Contains("&lt;") && !AnnouncementHtml.Clean("<b>x</i></b>").Contains("</i>"), "Announcement HTML: open tags are closed, stray closers dropped, text escaped");
        check(AnnouncementHtml.Clean(AnnouncementHtml.Clean("<p style=\"color:#a00\">x <a href=\"mailto:a@b.ro\">m</a></p>")) == AnnouncementHtml.Clean("<p style=\"color:#a00\">x <a href=\"mailto:a@b.ro\">m</a></p>"), "Announcement HTML: cleaning twice gives the same text");
        var resolved = AnnouncementHtml.Clean("<img src=\"imagini/a.png\"><img src=\"imagini/lipsa.png\">", source => source == "imagini/a.png" ? "/media/anunturi/7" : null);
        check(resolved == "<img src=\"/media/anunturi/7\" alt=\"\">" && AnnouncementHtml.ImageIds(resolved).SequenceEqual([7]), "Announcement HTML: the addresses of a ZIP are replaced and a missing picture is dropped");

        // Dates and states.
        check(AnnouncementRules.TryParseDisplayDate("25.12.2026", out var stored) && stored == "2026-12-25" && AnnouncementRules.TryParseDisplayDate("", out var empty) && empty == "" && !AnnouncementRules.TryParseDisplayDate("2026-12-25", out _) && AnnouncementRules.DisplayDate("2026-12-25") == "25.12.2026",
            "Announcement rules: the date is written dd.mm.yyyy and stored yyyy-MM-dd");
        check(AnnouncementRules.IsExpired(AnnouncementRules.StateActive, "2026-01-01", new DateTime(2026, 2, 1)) && !AnnouncementRules.IsExpired(AnnouncementRules.StateActive, "2026-02-01", new DateTime(2026, 2, 1)) && !AnnouncementRules.IsExpired(AnnouncementRules.StateDraft, "2026-01-01", new DateTime(2026, 2, 1)) &&
              AnnouncementRules.StateLabel(AnnouncementRules.StateActive, true) == "Arhivat (expirat)", "Announcement rules: an active announcement past its date shows as archived (expired)");

        // Permission and journal.
        check(Permissions.IsKnown("setari-anunturi.view") && Permissions.IsAdminOnly("setari-anunturi.edit") && Permissions.IsDeveloperOnly("setari-anunturi.view") && MenuCatalog.Entries.Any(entry => entry.Key == "setari.anunturi" && entry.Permission == "setari-anunturi.view"), "Announcements: the key setari-anunturi is administrator and developer only, with its menu entry");
        var actions = new[] { AuditActions.CreateAnnouncement, AuditActions.PublishAnnouncement, AuditActions.EditAnnouncement, AuditActions.ArchiveAnnouncement, AuditActions.RepublishAnnouncement, AuditActions.DeleteAnnouncement, AuditActions.ConfirmAnnouncementRead };
        check(actions.Distinct().Count() == actions.Length && actions.Where(action => action != AuditActions.DeleteAnnouncement).All(AuditActions.IsCreateOrEdit) && !AuditActions.IsCreateOrEdit(AuditActions.DeleteAnnouncement), "Announcements: every operation has its own journal action (create, publish, edit, archive, republish, delete, read confirmation)");
        return Task.CompletedTask;
    }
}
