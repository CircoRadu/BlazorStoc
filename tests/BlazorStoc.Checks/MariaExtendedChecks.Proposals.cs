using System.IO.Compression;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using BlazorStoc.Modules.Proposals;
using BlazorStoc.Services;

namespace BlazorStoc.Checks;

public static partial class MariaExtendedChecks
{
    // ---- Propuneri de modificari ----------------------------------------------------------------------------------------------
    private static async Task ProposalsAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe)
    {
        var started = DateTime.UtcNow.AddSeconds(-1);
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
        var author = new MariaProposalService(configuration, new TestAccessControl(false, "proba.propune"), audit);
        var other = new MariaProposalService(configuration, new TestAccessControl(false, "proba.altul.p"), audit);
        var boss = new MariaProposalService(configuration, admin, audit);
        try
        {
            var proposal = await author.CreateAsync(new ProposalInput(ProposalRules.KindFeature, "/produse", "Produse", "Cautare dupa cod de bare", "Vreau sa caut produsul scanand codul de bare", "Economisesc timp la inventar",
                ProposalRules.ImportanceHigh, [new ReportImage(ReportImageRules.KindScreenshot, "pagina.png", png), new ReportImage(ReportImageRules.KindPasted, "lipit.png", png)]));
            Check(proposal.Number == ProposalRules.Number(proposal.Id) && proposal.State == ProposalRules.StateNew && proposal.FileCount == 2 && proposal.Kind == ProposalRules.KindFeature, "Proposal: saved with its number, the state Nouă and its two pictures");
            var refusedEmpty = await Rejects<ProposalException>(() => author.CreateAsync(new ProposalInput(ProposalRules.KindChange, "", "", " ", "x", "", ProposalRules.ImportanceLow, [])), "A proposal without a title is refused");
            var refusedKind = await Rejects<ProposalException>(() => author.CreateAsync(new ProposalInput("Inventat", "", "", "t", "d", "", ProposalRules.ImportanceLow, [])), "An unknown kind is refused");
            var refusedFile = await Rejects<ProposalException>(() => author.CreateAsync(new ProposalInput(ProposalRules.KindChange, "", "", "t", "d", "", ProposalRules.ImportanceLow, [new ReportImage(ReportImageRules.KindFile, "a.txt", "nu e imagine"u8.ToArray())])), "A file that is not a picture is refused");
            Check(refusedEmpty is not null && refusedKind is not null && refusedFile is not null, "Proposal: a missing title, an unknown kind and a non-picture are refused");

            // Visible to every signed-in user (text), also found by words while the title is written.
            var mine = await author.GetMineAsync();
            var boardOfOther = await other.GetBoardAsync("cod de bare", false);
            Check(mine.Any(item => item.Id == proposal.Id) && (await other.GetMineAsync()).All(item => item.Id != proposal.Id), "Proposal: \"Propunerile mele\" shows only one's own proposals");
            Check(boardOfOther.Any(item => item.Id == proposal.Id) && !(await other.GetBoardAsync("zzzzinexistent", false)).Any(), "Proposal: every user sees the proposals of the others and finds a similar one by words");
            var deniedList = await Rejects<AccessDeniedException>(() => author.QueryAsync(new ProposalQuery(), 1, 10), "An ordinary user cannot use the administrator list");
            var deniedFiles = await Rejects<AccessDeniedException>(() => other.GetFilesAsync(proposal.Id), "Another user cannot read the pictures of a proposal");
            var deniedState = await Rejects<AccessDeniedException>(() => author.ChangeStateAsync(proposal.Id, ProposalRules.StateAccepted, "", ""), "An ordinary user cannot change the state");
            var deniedExport = await Rejects<AccessDeniedException>(() => author.ExportAsync([proposal.Id]), "An ordinary user cannot export");
            var deniedNote = await Rejects<AccessDeniedException>(() => other.AddNoteAsync(proposal.Id, "Ma bag si eu"), "A user who is neither the author nor the administrator cannot answer");
            Check(deniedList is not null && deniedFiles is not null && deniedState is not null && deniedExport is not null && deniedNote is not null, "Proposal: the list, the pictures of others, the state, the export and the answers are for the author and the administrator");

            var files = await author.GetFilesAsync(proposal.Id);
            Check(files.Count == 2 && (await author.GetFileContentAsync(files[0].Id))?.Content.SequenceEqual(png) == true, "Proposal: the author reads back the pictures byte for byte");

            // Notifications: the administrator sees the new proposal; the author sees nothing until something changes.
            Check((await boss.GetAlertsAsync()).Any(alert => alert.Proposal.Id == proposal.Id && alert.Reason == "Propunere nouă") && (await author.GetAlertsAsync()).All(alert => alert.Proposal.Id != proposal.Id), "Proposal: a new proposal is an alert for the administrator only");

            var page = await boss.QueryAsync(new ProposalQuery(State: ProposalRules.StateNew, Kind: ProposalRules.KindFeature, Importance: ProposalRules.ImportanceHigh, User: "proba.propune", Text: "cod de bare"), 1, 10);
            Check(page.Items.Any(item => item.Id == proposal.Id), "Proposal: the administrator filters by state, kind, importance, author and text");

            var openBefore = await boss.CountOpenAsync();
            Check(openBefore >= 1 && await author.CountOpenAsync() == 0, "Proposal: the badge of the tab counts the proposals not closed, for who may see them all (an ordinary user gets 0)");
            var changed = await boss.ChangeStateAsync(proposal.Id, ProposalRules.StateAccepted, "", "Ne place, o punem la plan");
            Check(changed.State == ProposalRules.StateAccepted && changed.NoteCount == 1 && changed.DecidedUtc is null, "Proposal: the administrator changes the state and the answer goes into the thread");
            Check((await author.GetAlertsAsync()).Any(alert => alert.Proposal.Id == proposal.Id && alert.Reason.StartsWith("Stare", StringComparison.Ordinal)), "Proposal: the author is told about the change of state");
            await author.MarkSeenAsync(proposal.Id);
            Check((await author.GetAlertsAsync()).All(alert => alert.Proposal.Id != proposal.Id), "Proposal: opening the proposal clears the alert of the author");
            await author.AddNoteAsync(proposal.Id, "Multumesc!");
            await boss.AddNoteAsync(proposal.Id, "Cu placere");
            var notes = await other.GetNotesAsync(proposal.Id);
            Check(notes.Count == 3 && notes[1].Author == "proba.propune" && (await author.GetAlertsAsync()).Any(alert => alert.Proposal.Id == proposal.Id), "Proposal: the author and the administrator answer in one thread, visible to all; an answer of the administrator is an alert again");

            var implemented = await boss.ChangeStateAsync(proposal.Id, ProposalRules.StateImplemented, "0.5.99", "");
            Check(implemented.ImplementedVersion == "0.5.99" && implemented.DecidedUtc is not null, "Proposal: Implementată records the version and the time of the decision");
            var reopened = await boss.ChangeStateAsync(proposal.Id, ProposalRules.StatePlanned, "0.5.99", "");
            Check(reopened.ImplementedVersion.Length == 0 && reopened.DecidedUtc is null, "Proposal: the version is kept only for Implementată");
            await boss.ChangeStateAsync(proposal.Id, ProposalRules.StateImplemented, "0.5.99", "");
            Check(await boss.CountOpenAsync() == openBefore - 1, "Proposal: a closed proposal leaves the count of the badge");

            var closedPage = await boss.QueryAsync(new ProposalQuery(User: "proba.propune", Closed: true), 1, 10);
            var openPage = await boss.QueryAsync(new ProposalQuery(User: "proba.propune", Closed: false), 1, 10);
            Check(closedPage.Items.Any(item => item.Id == proposal.Id) && openPage.Items.All(item => item.Id != proposal.Id), "Proposal: the tabs split the closed proposals from the active ones");
            Check((await other.GetBoardAsync("cod de bare", true)).All(item => item.Id != proposal.Id), "Proposal: \"Doar cele deschise\" leaves the closed ones out");

            var export = await boss.ExportAsync([proposal.Id]);
            using (var zip = new ZipArchive(new MemoryStream(export.Content)))
            {
                var names = zip.Entries.Select(entry => entry.FullName).ToList();
                Check(export.FileName == proposal.Number + ".zip" && names.Contains(proposal.Number + "/propunere.json") && names.Contains(proposal.Number + "/propunere.txt") && names.Count(name => name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) == 2,
                    "Proposal: the ZIP holds propunere.json, propunere.txt and both pictures");
            }

            var edited = await boss.UpdateContentAsync(proposal.Id, "Titlu corectat", "Descriere corectata", "Motivare corectata", [new ReportImage(ReportImageRules.KindFile, "extra.png", png)]);
            Check(edited.Title == "Titlu corectat" && edited.Description == "Descriere corectata" && edited.Benefit == "Motivare corectata" && (await author.GetFilesAsync(proposal.Id)).Count == 3, "Proposal: the administrator corrects the title, the description and the reason and adds a picture (the pictures already there stay)");
            Check(await Rejects<AccessDeniedException>(() => author.UpdateContentAsync(proposal.Id, "x", "y", "", []), "An ordinary user cannot edit the content of a proposal") is not null, "Proposal: editing the content is for the administrator");
            var events = (await audit.GetEventsAsync()).Where(item => item.TimestampUtc >= started && item.EntityType == AuditEntities.Proposal).Select(item => item.Action).ToList();
            Check(events.Contains(AuditActions.CreateProposal) && events.Contains(AuditActions.ChangeProposalState) && events.Contains(AuditActions.EditProposalContent) && events.Contains(AuditActions.AddProposalNote) && events.Contains(AuditActions.ExportProposal), "Proposal: creation, state change, answers and export are journaled with their own actions");

            await boss.DeleteAsync(proposal.Id);
            await using var left = new MySqlCommand("SELECT (SELECT COUNT(*) FROM proposals WHERE id=@id) + (SELECT COUNT(*) FROM proposal_files WHERE proposal_id=@id) + (SELECT COUNT(*) FROM proposal_notes WHERE proposal_id=@id)", probe);
            left.Parameters.AddWithValue("@id", proposal.Id);
            Check(Convert.ToInt32(await left.ExecuteScalarAsync()) == 0, "Proposal: deleting a proposal removes its pictures and answers too");
            Check((await audit.GetEventsAsync()).Any(item => item.TimestampUtc >= started && item.Action == AuditActions.DeleteProposal), "Proposal: the deletion is journaled with its own action");
var secondP = await author.CreateAsync(new ProposalInput(ProposalRules.KindChange, "/produse", "Produse", "Proba stergere totala", "Descriere", "", ProposalRules.ImportanceLow, [new ReportImage(ReportImageRules.KindFile, "x.png", png)]));            var thirdP = await author.CreateAsync(new ProposalInput(ProposalRules.KindChange, "/produse", "Produse", "Proba ramane deschisa", "Descriere", "", ProposalRules.ImportanceLow, []));            await boss.ChangeStateAsync(secondP.Id, ProposalRules.StateRejected, "", "");            var deniedAllP = await Rejects<AccessDeniedException>(() => author.DeleteClosedAsync(), "An ordinary user cannot delete all the closed proposals");            var removedP = await boss.DeleteClosedAsync();            Check(deniedAllP is not null && removedP >= 1 && await boss.GetAsync(secondP.Id) is null && await boss.GetAsync(thirdP.Id) is not null, "Proposal: the administrator deletes every closed proposal at once, the active ones stay");            await using var leftAllP = new MySqlCommand("SELECT (SELECT COUNT(*) FROM proposals WHERE id=@id) + (SELECT COUNT(*) FROM proposal_files WHERE proposal_id=@id)", probe);            leftAllP.Parameters.AddWithValue("@id", secondP.Id);            Check(Convert.ToInt32(await leftAllP.ExecuteScalarAsync()) == 0 && (await audit.GetEventsAsync()).Any(item => item.TimestampUtc >= started && item.Action == AuditActions.DeleteAllClosedProposals), "Proposal: the bulk deletion removes the pictures too and is journaled with its own action");
        }
        finally { await using var cleanup = new MySqlCommand("DELETE FROM proposals WHERE user_name IN ('proba.propune','proba.altul.p')", probe); await cleanup.ExecuteNonQueryAsync(); }
    }
}
