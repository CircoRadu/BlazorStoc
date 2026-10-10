#nullable enable
#pragma warning disable CS1998, CS8600, CS8601, CS8602, CS8603, CS8604, CS8605, CS8618, CS8619, CS8620, CS8625, CS8629, CS8714
using BlazorStoc.Checks;
using BlazorStoc.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PdfSharp.Pdf.IO;
using System.IO.Compression;
using System.Text.Json;

public static partial class FullRun
{
    // Lines 1026-1160 of the former Program.cs.
    internal static async Task BeneficiaryScreensAsync(string[] args)
    {
        Check(ReturnNavigation.EditUrl(3, "/produse/3/miscari") == "/produse?edit=3&inapoi=%2Fproduse%2F3%2Fmiscari" &&
              ReturnNavigation.DeleteUrl(3, "/produse/3/miscari") == "/produse?sterge=3&inapoi=%2Fproduse%2F3%2Fmiscari" &&
              ReturnNavigation.EditUrl(3, "https://evil.example") == "/produse?edit=3",
            "Edit and delete links from the product page carry the page to return to");
        var saveSummary = SaveSummary.Changed(new("Nume", "Alfa", "Alfa"), new("Descriere", "", "Text nou"), new("Cod", " A1 ", "B2"), new("Lung", "x", new string('y', 500)));
        Check(saveSummary.Select(change => change.Field).SequenceEqual(["Descriere", "Cod", "Lung"]) &&
              saveSummary[0].Before == SaveSummary.Empty && saveSummary[1].Before == "A1" && saveSummary[2].After.Length == SaveSummary.MaximumValueLength + 1,
            "Save summary lists only changed fields, shows empty values and shortens very long ones");
        Check(SaveSummary.Changed(new AuditChange("Nume", "Alfa", "Alfa")).Count == 0, "Save summary is empty when no field changed");
        var beneficiaryInput = BeneficiaryEdit(beneficiary); beneficiaryInput.Name = "  Beneficiar   actualizat   SRL  ";
        var beneficiaryWithoutReason = BeneficiaryInput.From(beneficiary); beneficiaryWithoutReason.Name = "Fără motiv";
        try { await demoBeneficiaries.UpdateAsync(beneficiary, beneficiaryWithoutReason); throw new Exception("Beneficiary edit without reason accepted"); }
        catch (BeneficiaryOperationException) { Check(true, "Beneficiary edits require a reason"); }
        var updatedBeneficiary = await demoBeneficiaries.UpdateAsync(beneficiary, beneficiaryInput);
        Check(updatedBeneficiary.Version == 1 && updatedBeneficiary.Name == "Beneficiar actualizat SRL", "Beneficiary can be edited with optimistic concurrency");
        try { await demoBeneficiaries.UpdateAsync(beneficiary, BeneficiaryEdit(beneficiary)); throw new Exception("Stale beneficiary accepted"); }
        catch (BeneficiaryOperationException) { Check(true, "Stale beneficiary edit is rejected"); }
        try { await demoBeneficiaries.DeleteAsync(updatedBeneficiary, " "); throw new Exception("Beneficiary deletion without reason accepted"); }
        catch (BeneficiaryOperationException) { Check(true, "Beneficiary deletion requires a reason"); }
        Check((await demoBeneficiaries.GetBeneficiariesAsync()).Any(item => item.Id == updatedBeneficiary.Id), "Rejected beneficiary deletion preserves the object");
        await demoBeneficiaries.DeleteAsync(updatedBeneficiary, "Test automat");
        Check(!(await demoBeneficiaries.GetBeneficiariesAsync()).Any(item => item.Id == updatedBeneficiary.Id), "Beneficiary can be deleted");
        Check(auditTrail.Entries.Count(entry => entry.EntityType == AuditEntities.Beneficiary) == 6, "Beneficiary changes are written to the audit trail");


        void ProjectRejected(Action operation, string message)
        {
            try { operation(); }
            catch (ProjectOperationException) { Check(true, message); return; }
            throw new Exception("Expected project rejection: " + message);
        }
        var projectNow = new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);
        var projectInput = new ProjectInput { BeneficiaryId = 1, Name = "  Hală   producție  ", Observations = "  Montaj în două etape  " }.Validated();
        Check(projectInput.Name == "Hala productie" && projectInput.Observations == "Montaj in doua etape",
            "Project name and general observations use the existing trimming, space and diacritic rules");
        ProjectRejected(() => new ProjectInput { BeneficiaryId = 1, Name = "   " }.Validated(), "Project name is required");
        ProjectRejected(() => new ProjectInput { BeneficiaryId = 0, Name = "Proiect" }.Validated(), "Project requires a beneficiary");
        ProjectRejected(() => new ProjectInput { BeneficiaryId = 1, Name = new string('P', 201) }.Validated(), "Project name is limited to 200 characters");
        var project = ProjectRules.Create(10, projectInput, projectNow);
        Check(project.Version == 0 && project.CreatedAtUtc == projectNow && project.UpdatedAtUtc == projectNow &&
              project.CreatedAtUtc.Kind == DateTimeKind.Utc, "New projects start at version 0 with UTC creation and update timestamps");
        try { ProjectRules.Create(11, projectInput, DateTime.SpecifyKind(projectNow, DateTimeKind.Local)); throw new Exception("Local project timestamp accepted"); }
        catch (ArgumentException) { Check(true, "Project timestamps must be UTC"); }
        var existingProjects = new[] { project, ProjectRules.Create(12, new ProjectInput { BeneficiaryId = 2, Name = "Alt proiect" }.Validated(), projectNow) };
        ProjectRejected(() => ProjectRules.EnsureUniqueName(existingProjects, 1, "  HALĂ  PRODUCȚIE ", null, "Construct Demo SRL"),
            "Project names are unique per beneficiary regardless of case, diacritics and spacing");
        ProjectRules.EnsureUniqueName(existingProjects, 2, "Hala productie", null, "Atelier Tehnic SRL");
        Check(true, "The same project name is allowed for a different beneficiary");
        ProjectRules.EnsureUniqueName(existingProjects, 1, "Hala productie", project.Id, "Construct Demo SRL");
        Check(true, "A project keeps its own name when edited");
        try { ProjectRules.EnsureUniqueName(existingProjects, 1, "hala productie", null, "Construct Demo SRL"); throw new Exception("Duplicate project name accepted"); }
        catch (ProjectOperationException exception)
        {
            Check(exception.Message.Contains("Construct Demo SRL", StringComparison.Ordinal) && exception.Message.Contains("Hala productie", StringComparison.Ordinal),
                "Duplicate project message names the beneficiary and the existing project");
        }
        Check(ProjectRules.NormalizedName("  hală   PRODUCȚIE ") == ProjectRules.NormalizedName("Hala productie"),
            "Normalized project name key is stable for the per-beneficiary unique index");
        var projectEdit = ProjectInput.From(project); projectEdit.Name = "Hala noua";
        ProjectRejected(() => projectEdit.Validated(true), "Project edits require a reason");
        projectEdit.Reason = "Corectie denumire";
        var editedProject = ProjectRules.Edited(project, projectEdit.Validated(true), projectNow.AddMinutes(5));
        Check(editedProject.Version == 1 && editedProject.BeneficiaryId == project.BeneficiaryId && editedProject.CreatedAtUtc == projectNow &&
              editedProject.UpdatedAtUtc == projectNow.AddMinutes(5), "Project edits increment the version and keep the creation timestamp");
        var projectMoveAttempt = ProjectInput.From(project); projectMoveAttempt.BeneficiaryId = project.BeneficiaryId + 1; projectMoveAttempt.Reason = "Mutare";
        try { ProjectRules.Edited(project, projectMoveAttempt.Validated(true), projectNow.AddMinutes(5)); throw new Exception("Project moved to another beneficiary"); }
        catch (ProjectOperationException exception) { Check(exception.Message == ProjectRules.BeneficiaryLockedMessage, "A project edit cannot change the beneficiary chosen at creation"); }
        ProjectRejected(() => ProjectRules.CheckCurrent(editedProject, project), "Stale project version is rejected");
        ProjectRejected(() => ProjectRules.CheckCurrent(null, project), "Deleted project is rejected on edit");
        ProjectRejected(() => ProjectRules.CheckBeneficiaryExists(null), "Project save is rejected when the beneficiary no longer exists");
        var observationInput = new ProjectObservationInput { Name = "  Verificare   șantier ", Content = " Fundația este turnată " }.Validated();
        var observation = ProjectRules.CreateObservation(1, project.Id, observationInput, "operator", projectNow);
        Check(observation.Name == "Verificare santier" && observation.Content == "Fundatia este turnata" &&
              observation.Author == "operator" && observation.Version == 0 && observation.CreatedAtUtc.Kind == DateTimeKind.Utc,
            "Project observations normalize text and keep author, version and UTC timestamps");
        ProjectRejected(() => new ProjectObservationInput { Name = " " }.Validated(), "Observation name is required");
        ProjectRejected(() => ProjectRules.CreateObservation(2, project.Id, observationInput, " ", projectNow), "Observation author is required");
        var observationEdit = ProjectObservationInput.From(observation); observationEdit.Content = "Actualizat";
        ProjectRejected(() => observationEdit.Validated(true), "Observation edits require a reason");
        observationEdit.Reason = "Completare";
        var editedObservation = ProjectRules.EditedObservation(observation, observationEdit.Validated(true), projectNow.AddMinutes(1));
        Check(editedObservation.Version == 1 && editedObservation.CreatedAtUtc == projectNow, "Observation edits increment the version");
        ProjectRejected(() => ProjectRules.CheckCurrent(editedObservation, observation), "Stale observation version is rejected");
        var projectFile = ProjectFileRules.Create(1, observation.Id, @"..\..\C:\secret\Plan  fațadă.PDF", "application/pdf", 1024,
            new string('A', 64), "operator", projectNow);
        Check(projectFile.OriginalName == "Plan  fațadă.PDF" && projectFile.StoredName.EndsWith(".pdf", StringComparison.Ordinal) &&
              projectFile.StoredName.Length == 36 && !projectFile.StoredName.Contains("Plan", StringComparison.Ordinal) &&
              projectFile.Sha256 == new string('a', 64) && projectFile.UploadedAtUtc.Kind == DateTimeKind.Utc,
            "Observation file metadata keeps a safe original name, a generated internal name and a normalized hash");
        Check(ProjectFileRules.NewStoredName(".exe/../x") is { Length: 32 } && ProjectFileRules.NewStoredName(".pdf") != ProjectFileRules.NewStoredName(".pdf"),
            "Internal file names are unique and reject unsafe extensions");
        ProjectRejected(() => ProjectFileRules.Create(2, observation.Id, "gol.txt", "text/plain", 0, new string('a', 64), "operator", projectNow),
            "Empty observation files are rejected");
        ProjectRejected(() => ProjectFileRules.Create(2, observation.Id, "a.txt", "text/plain", 1, "nu-este-hash", "operator", projectNow),
            "Observation file metadata requires a SHA-256 hash");
        ProjectRejected(() => ProjectFileRules.SafeOriginalName("../.."), "Path-only file names are rejected");
        try { BeneficiaryRules.CheckNoLiveProjects(2); throw new Exception("Beneficiary with live projects accepted for deletion"); }
        catch (BeneficiaryOperationException exception) { Check(exception.Message.Contains("2 proiecte", StringComparison.Ordinal), "Beneficiary deletion is blocked while live projects exist"); }
        BeneficiaryRules.CheckNoLiveProjects(0);
        Check(true, "Beneficiary without live projects passes the project deletion rule");

        // Run only against an explicitly started local test instance with the documented test credentials.
        if (args.Length == 2 && args[0] == "--http")
        {
            var baseUri = new Uri(args[1]);
            if (!baseUri.IsLoopback) throw new Exception("HTTP checks are restricted to localhost");
            using var handler = new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new System.Net.CookieContainer() };
            using var http = new HttpClient(handler) { BaseAddress = baseUri };
            var response = await http.GetAsync("/");
            Check(response.StatusCode == System.Net.HttpStatusCode.Redirect && response.Headers.Location!.ToString().Contains("/Account/Login"), "Unauthenticated catalogue access redirects to login");
            response = await http.GetAsync("/app.css");
            Check(response.IsSuccessStatusCode && response.Content.Headers.ContentType?.MediaType == "text/css" && (await response.Content.ReadAsStringAsync()).Length > 1000, "Styles are served before authentication");
            response = await http.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string,string>{{"Username","admin"},{"Password","local-test-password-123"}}));
            Check(response.StatusCode == System.Net.HttpStatusCode.BadRequest, "Login rejects requests without antiforgery token");
            async Task<string> Token(string path)
            {
                var html = await http.GetStringAsync(path);
                var match = System.Text.RegularExpressions.Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
                if (!match.Success) throw new Exception("Missing antiforgery token");
                return System.Net.WebUtility.HtmlDecode(match.Groups[1].Value);
            }
            var token = await Token("/Account/Login");
            response = await http.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string,string>{{"Username","admin"},{"Password","wrong"},{"__RequestVerificationToken",token}}));
            Check(response.StatusCode == System.Net.HttpStatusCode.OK && (await response.Content.ReadAsStringAsync()).Contains("incorect"), "Invalid credentials do not authenticate");
            token = await Token("/Account/Login");
            response = await http.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string,string>{{"Username","admin"},{"Password","local-test-password-123"},{"__RequestVerificationToken",token}}));
            Check(response.StatusCode == System.Net.HttpStatusCode.Redirect, "Valid credentials create a session");
            response = await http.GetAsync("/");
            Check(response.IsSuccessStatusCode, "Authenticated catalogue is accessible");
            token = await Token("/Account/Logout");
            response = await http.PostAsync("/Account/Logout", new FormUrlEncodedContent(new Dictionary<string,string>{{"__RequestVerificationToken",token}}));
            Check(response.StatusCode == System.Net.HttpStatusCode.Redirect, "Logout accepts authenticated antiforgery token");
            response = await http.GetAsync("/");
            Check(response.StatusCode == System.Net.HttpStatusCode.Redirect, "Logout revokes the browser session");
        }
    }
}
