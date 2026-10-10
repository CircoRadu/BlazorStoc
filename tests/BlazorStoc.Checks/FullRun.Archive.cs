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
    // Lines 12-149 of the former Program.cs.
    internal static async Task<bool> ArchiveAsync(string[] args)
    {

data = await new DemoProductRepository().GetProductsAsync();
        DateOnly? TestExpiry = new DateOnly(2027, 3, 15);
        // CHECKS_ONLY=<group>[,<group>...] runs only the named groups of the checks that live in their own files (a fast loop while working on one
        // area); the checks written below in this file (products, beneficiaries, vehicles, journal...) run only in the full run.
        // Groups: suppliers, pickup, groups, reasons, structure (routes of the pages, pure steps of the long methods), components, invoices, pdf and xml (the two halves of invoices; xml is fast), or "ui" (every group except invoices). Older switches stay as aliases:
        // INVOICE_CHECKS_ONLY=1 = invoices, COMPONENT_CHECKS_ONLY=1 = ui. An unknown group name fails the run.
        var checkGroups = new Dictionary<string, Func<Task>>(StringComparer.OrdinalIgnoreCase)
        {
            // The invoice checks are split by the kind of file: "pdf" (text and scanned PDF: OCR, templates, fields; minutes) and "xml" (XML and the ZIP of
            // e-Factura: reader, templates, wizard; seconds). "invoices" runs both. Run only the one that the change touches (the full run before a commit has all).
            ["invoices"] = async () => { await InvoiceXmlChecks.RunAsync(Check); await InvoiceChecks.RunAsync(Check); },
            ["pdf"] = async () => await InvoiceChecks.RunAsync(Check),
            ["xml"] = async () => await InvoiceXmlChecks.RunAsync(Check),
            ["offers"] = () => { BlazorStoc.Checks.OfferChecks.Run(Check); BlazorStoc.Checks.OfferChecks.RunSituation(Check); return Task.CompletedTask; },
            // Only the extended MariaDB sections (with MARIA_ONLY, one section): needs RUN_MARIA_INTEGRATION_CHECKS=1 and MARIA_TEST_CONFIG_PATH.
            ["maria"] = async () => await BlazorStoc.Checks.MariaExtendedChecks.RunAsync(new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                .AddJsonFile(Environment.GetEnvironmentVariable("MARIA_TEST_CONFIG_PATH") ?? throw new InvalidOperationException("Set MARIA_TEST_CONFIG_PATH.")).Build()),
            ["components"] = async () => await ComponentChecks.RunAsync(Check),
            ["pickup"] = async () =>
            {
                await PickupWizardChecks.RunAsync(Check);
                await PickupWizardChecks.TemplateFlowAsync(Check);
                await PickupWizardChecks.LinkButtonAsync(Check);
            },
            ["suppliers"] = async () =>
            {
                SupplierChecks.Rules(Check);
                await SupplierChecks.ComponentsAsync(Check);
                await SupplierChecks.PickupAsync(Check);
            },
            ["groups"] = async () => await ProductGroupsChecks.RunAsync(Check),
            ["reasons"] = () => { ReasonSummaryChecks.Run(Check); return Task.CompletedTask; },
            ["nas"] = () => NasBackupChecks.RunAsync(Check),
            ["robustness"] = () => RobustnessChecks.RunAsync(Check),
            ["structure"] = () => { StructureChecks.Run(Check); return Task.CompletedTask; }
        };
        var selectedGroups = (Environment.GetEnvironmentVariable("CHECKS_ONLY") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (Environment.GetEnvironmentVariable("INVOICE_CHECKS_ONLY") == "1") selectedGroups.Add("invoices");
        if (Environment.GetEnvironmentVariable("COMPONENT_CHECKS_ONLY") == "1") selectedGroups.Add("ui");
        if (selectedGroups.Count > 0)
        {
            var names = selectedGroups.SelectMany(name => name.Equals("ui", StringComparison.OrdinalIgnoreCase)
                ? checkGroups.Keys.Where(key => !key.Equals("invoices", StringComparison.OrdinalIgnoreCase) && !key.Equals("pdf", StringComparison.OrdinalIgnoreCase) && !key.Equals("maria", StringComparison.OrdinalIgnoreCase)) : [name]).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var unknown = names.Where(name => !checkGroups.ContainsKey(name)).ToList();
            if (unknown.Count > 0) throw new ArgumentException($"CHECKS_ONLY: unknown group(s) {string.Join(", ", unknown)}; known: {string.Join(", ", checkGroups.Keys)}, ui.");
            foreach (var name in names) await checkGroups[name]();
            Console.WriteLine($"Checks finished (groups: {string.Join(", ", names)}).");
            return false;
        }

        var archiveAccess = new TestAccessControl(true, "archive.admin");
        var archiveService = new ArchiveService(archiveAccess);
        ArchiveOperation? firstArchiveOperation = null;
        await archiveService.ExecuteAsync(ArchiveRequests.Product(
            new Product(901, "Test", "Contract", "Produs arhivat", "Date complete", 0, 7), "Motiv test"),
            (operation, _) => { firstArchiveOperation = operation; return Task.CompletedTask; });
        ArchiveOperation? secondArchiveOperation = null;
        await archiveService.ExecuteAsync(ArchiveRequests.Beneficiary(
            new Beneficiary(902, "Beneficiar arhivat", "RO12345678", 3), "Motiv test"),
            (operation, _) => { secondArchiveOperation = operation; return Task.CompletedTask; });
        Check(firstArchiveOperation is not null && firstArchiveOperation.Id != Guid.Empty &&
              firstArchiveOperation.TimestampUtc.Kind == DateTimeKind.Utc &&
              firstArchiveOperation.ActorUsername == "archive.admin" &&
              firstArchiveOperation.ActorRole == AccessRoles.Administrator &&
              firstArchiveOperation.Request.Snapshot.OriginalId == "901" &&
              firstArchiveOperation.Request.Snapshot.Version == 7,
            "Archive contract supplies operation id, UTC timestamp, operator, original id and version");
        Check(secondArchiveOperation is not null && secondArchiveOperation.Id != firstArchiveOperation!.Id,
            "Every archive operation receives a unique identifier");
        var protectedUserSnapshot = ArchiveRequests.User(
            new WebUser(903, "archive.user", "Archive User", AccessRoles.LimitedUser, false, 2), "Motiv test",
            [new ArchiveProtectedValue("PasswordHash", "identity-password-hash")]);
        Check(!protectedUserSnapshot.Snapshot.DataJson.Contains("password", StringComparison.OrdinalIgnoreCase) &&
              protectedUserSnapshot.Snapshot.ProtectedValues.Single().Hash == "identity-password-hash",
            "Password hashes are isolated from the public archive snapshot");
        try
        {
            ArchiveSnapshot.Create("Test", "1", 0, new { Password = "clear-text" });
            throw new Exception("Clear password accepted in public archive data");
        }
        catch (ArchiveContractException)
        {
            Check(true, "Archive contract rejects clear password fields from public data");
        }

        Check(DeleteConfirmationRules.ValidateReason(string.Empty, string.Empty) is not null,
            "Delete confirmation requires an explicit reason choice");
        Check(DeleteConfirmationRules.ResolveReason("Produsul", DeleteConfirmationRules.DefaultChoice, string.Empty) ==
              "Produsul nu va mai fi folosit",
            "The default deletion choice generates the object-specific audit reason");
        Check(DeleteConfirmationRules.DefaultReason("Observația") == "Observația nu va mai fi folosită",
            "The default deletion reason agrees in gender with feminine subjects like Observația");
        Check(DeleteConfirmationRules.ResolveReason("Beneficiarul", DeleteConfirmationRules.CustomChoice,
                  "  Contract incheiat  ") == "Contract incheiat",
            "The custom deletion reason is trimmed and retained for audit");
        Check(DeleteConfirmationRules.IsConfirmationValid("  sterge ") &&
              !DeleteConfirmationRules.IsConfirmationValid("Sterge") &&
              !DeleteConfirmationRules.IsConfirmationValid("șterge") &&
              !DeleteConfirmationRules.IsConfirmationValid("sterge acum"),
            "Final deletion confirmation accepts only the exact case-sensitive word sterge");

        // Subtask 3.3 (Task 3): same strict-comparison rule as the deletion word, but its own word.
        Check(RestoreConfirmationRules.IsConfirmationValid("  confirma ") &&
              !RestoreConfirmationRules.IsConfirmationValid("Confirma") &&
              !RestoreConfirmationRules.IsConfirmationValid("confirmă") &&
              !RestoreConfirmationRules.IsConfirmationValid("confirma acum"),
            "Restore confirmation accepts only the exact case-sensitive word confirma");

        Check(MariaSchemaMigrations.All.Count > 0 && MariaSchemaMigrations.All.Select(m => m.Version).Distinct().Count() == MariaSchemaMigrations.All.Count &&
              MariaSchemaMigrations.All.All(m => m.Statements.All(sql => sql.Split("ADD COLUMN").Length - 1 == sql.Split("ADD COLUMN IF NOT EXISTS").Length - 1 &&
                  !System.Text.RegularExpressions.Regex.IsMatch(sql, @"DROP\s+(TABLE|COLUMN|DATABASE|SCHEMA)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) &&
                  m.ExpectedColumns.All(c => m.Statements.Any(sql => sql.Contains($"`{c.Column}`") && sql.Contains($"`{c.Table}`")))),
            "MariaDB migrations are idempotent (ADD COLUMN IF NOT EXISTS), never drop a table or column, and every expected column is created by its migration");
        Check(MariaSchemaMigrations.All.Single(m => m.Version == 5).ExpectedColumns.Select(c => c.Column).Order().SequenceEqual(
                new[] { "resolved_by", "resolved_utc", "resolved_reason", "resolved_auto", "object_label", "snapshot_values", "snapshot_subject", "snapshot_body", "snapshot_source", "active_source_key" }.Order()) &&
              MariaSchemaMigrations.All.Single(m => m.Version == 5).Statements.Any(sql => sql.Contains("uq_expiry_notifications_event") && sql.Contains("`source_key`, `object_id`, `expiry_date`")) &&
              MariaSchemaMigrations.All.Single(m => m.Version == 5).Statements.Any(sql => sql.Contains("uq_notification_templates_active")),
            "MariaDB migration 5 adds the resolution columns, the unique key per event and the unique active template per event");
        Check(MariaSchemaMigrations.All.Single(m => m.Version == 6).ExpectedColumns.Select(c => c.Column).Order().SequenceEqual(
                new[] { "id", "purge_enabled", "purge_months", "last_purge_utc", "version" }.Order()) &&
              MariaSchemaMigrations.All.Single(m => m.Version == 6).Statements.All(sql => sql.Contains("CREATE TABLE IF NOT EXISTS `notification_settings`")),
            "MariaDB migration 6 creates the notification settings table (one row, versioned)");
        Check(MariaSchemaMigrations.All.Single(m => m.Version == 1).ExpectedColumns.Select(c => c.Column).Order().SequenceEqual(
                new[] { "kind", "address", "phone", "registry_number", "postal_code", "caen_code", "anaf_verified" }.Order()),
            "MariaDB migration 1 covers exactly the beneficiary columns used by MariaBeneficiaryRepository");
        Check(MariaSchemaMigrations.All.Single(m => m.Version == 2).ExpectedColumns.Select(c => c.Column).Order().SequenceEqual(
                new[] { "id", "beneficiary_id", "name", "address", "normalized_address", "phone", "contact_person", "version" }.Order()) &&
              MariaSchemaMigrations.All.Single(m => m.Version == 2).Statements.All(sql => sql.Contains("CREATE TABLE IF NOT EXISTS `beneficiary_work_points`")),
            "MariaDB migration 2 creates the work points table used by MariaWorkPointRepository");
        return true;
    }
}
