using Microsoft.Extensions.Configuration;
using BlazorStoc.Services;
using MySqlConnector;

namespace BlazorStoc.Checks;

public static partial class MariaExtendedChecks
{
    private static async Task RiskAnalyzersAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe)
    {
        var suffix = Suffix();
        var since = DateTime.UtcNow;
        await Task.Delay(30);
        var analyzers = new MariaRiskAnalyzerRepository(configuration, admin, audit);
        async Task<int> Events(string action) => (await audit.GetEventsAsync()).Count(item => item.TimestampUtc > since && item.EntityType == AuditEntities.RiskAnalyzer && item.Action == action);
        var created = await analyzers.CreateAsync($"  Ext Analizor {suffix}  ", "Prima nota");
        try
        {
            Check(created.Name == $"Ext Analizor {suffix}" && created.Notes == "Prima nota" && created.UsedBy == 0 && created.Version == 0, "A risk analyzer is added with a cleaned name");
            Check(await Events(AuditActions.AddRiskAnalyzer) == 1, "The journal names the operation \"Adăugare analizor de risc\"");
            await Rejects<RiskAnalyzerOperationException>(() => analyzers.CreateAsync($"ext-analizor {suffix}", ""), "A second analyzer with the same name (case and separators ignored) is refused");
            await Rejects<RiskAnalyzerOperationException>(() => analyzers.CreateAsync("   ", ""), "The name of an analyzer is required");
            await Rejects<RiskAnalyzerOperationException>(() => analyzers.CreateAsync(new string('a', 121), ""), "The name of an analyzer has a maximum length");
            await Rejects<RiskAnalyzerOperationException>(() => analyzers.UpdateAsync(created, created.Name, "Prima nota"), "A change that changes nothing is refused");
            var edited = await analyzers.UpdateAsync(created, $"Ext Analizor {suffix} SRL", "Nota noua");
            Check(edited.Name == $"Ext Analizor {suffix} SRL" && edited.Notes == "Nota noua" && edited.Version == 1 && await Events(AuditActions.EditRiskAnalyzer) == 1, "An analyzer is changed and the edit is journaled");
            await Rejects<RiskAnalyzerOperationException>(() => analyzers.UpdateAsync(created, "Altceva", ""), "A stale version of an analyzer is refused");
            Check((await analyzers.GetAllAsync()).Any(item => item.Id == edited.Id), "The list of analyzers has the new analyzer");

            var owner = await new MariaBeneficiaryRepository(configuration, admin, audit).CreateAsync(Legal($"Ext Analizor risc {suffix} SRL", "RO" + Random.Shared.Next(60000000, 69999999)));
            var point = (await new MariaWorkPointRepository(configuration, admin, audit).GetAsync(owner.Id)).Single();
            var analyses = new MariaRiskAnalysisRepository(configuration, admin, audit);
            try
            {
                var today = DateOnly.FromDateTime(DateTime.Today);
                await Rejects<RiskAnalysisOperationException>(() => analyses.CreateAsync(owner.Id,
                    new() { WorkPointId = point.Id, Number = "AZ-1", Author = "X", InitialDate = today }, false), "An analysis without an analyzer is refused");
                await Rejects<RiskAnalysisOperationException>(() => analyses.CreateAsync(owner.Id,
                    new() { WorkPointId = point.Id, AnalyzerId = 999999999, Number = "AZ-1", Author = "X", InitialDate = today }, false), "An analysis with an analyzer that does not exist is refused");
                var analysis = await analyses.CreateAsync(owner.Id, new() { WorkPointId = point.Id, AnalyzerId = edited.Id, Number = "AZ-1", Author = "X", InitialDate = today }, false);
                Check(analysis.Analysis.AnalyzerId == edited.Id && analysis.AnalyzerName == edited.Name, "The analysis keeps the analyzer chosen from the nomenclator");
                Check((await analyzers.GetAllAsync()).Single(item => item.Id == edited.Id).UsedBy == 1, "The analyzer counts the analyses that use it");
                await Rejects<RiskAnalyzerOperationException>(() => analyzers.DeleteAsync(edited with { UsedBy = 0 }, "Ext"), "An analyzer used by an analysis cannot be deleted");
                await Rejects<RiskAnalyzerOperationException>(() => analyzers.DeleteAsync((analyzers.GetAllAsync().Result).Single(item => item.Id == edited.Id), " "), "The reason of the deletion is required");
                await analyses.DeleteAsync(analysis.Analysis, "Ext curatare");
                var free = (await analyzers.GetAllAsync()).Single(item => item.Id == edited.Id);
                await analyzers.DeleteAsync(free, "Ext curatare");
                Check(!(await analyzers.GetAllAsync()).Any(item => item.Id == edited.Id) && await Events(AuditActions.DeleteRiskAnalyzer) == 1, "An unused analyzer is deleted and the deletion is journaled with its own name");
            }
            finally
            {
                foreach (var view in await analyses.GetForBeneficiaryAsync(owner.Id)) await analyses.DeleteAsync(view.Analysis, "Ext curatare");
                if ((await new MariaBeneficiaryRepository(configuration, admin, audit).GetBeneficiariesAsync()).FirstOrDefault(current => current.Id == owner.Id) is { } live)
                    await new MariaBeneficiaryRepository(configuration, admin, audit).DeleteAsync(live, "Ext curatare");
            }
        }
        finally { await ExecuteAsync(probe, "DELETE FROM risk_analyzers WHERE name_key LIKE @key", ("@key", "extanalizor" + suffix.ToLowerInvariant() + "%")); }
    }
}
