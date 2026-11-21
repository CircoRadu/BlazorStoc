using Microsoft.Extensions.Configuration;
using MySqlConnector;
using BlazorStoc.Services;

namespace BlazorStoc.Checks;

public static partial class MariaExtendedChecks
{
    // Nomenclator -> Proiecte obligatorii: every beneficiary has the project of each active type (made at creation, when a type is added or activated, and by the check at start-up).
    private static async Task MandatoryProjectsAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe)
    {
        var suffix = Suffix();
        var started = DateTime.UtcNow.AddSeconds(-1);
        var types = new MariaBeneficiaryProjectTypeRepository(configuration, admin, audit);
        var beneficiaries = new MariaBeneficiaryRepository(configuration, admin, audit);
        var projects = new MariaProjectRepository(configuration, new TestWebHostEnvironment(Path.GetTempPath()), admin, audit);
        await types.EnsureProjectsAsync();   // what the application does when it starts: the first type, and the projects of the beneficiaries that exist
        var seeded = (await types.GetAllAsync()).FirstOrDefault(type => BeneficiaryProjectTypeRules.Key(type.Name) == "reparatiisistemeexistente");
        var typeId = 0;
        try
        {
            // 1. The first type is seeded and a new beneficiary gets its project at creation (marked, with the name of the type).
            var first = await beneficiaries.CreateAsync(Legal($"Ext Obl Beneficiar {suffix} SRL", "RO" + Random.Shared.Next(60000000, 69999999)));
            var firstProjects = await projects.GetForBeneficiaryAsync(first.Id);
            Check(seeded is { Active: true } && firstProjects.Any(project => project.Name == seeded.Name),
                "The seeded type \"Reparații sisteme existente\" is active and a new beneficiary gets its project at creation");

            // 2. Adding a type makes its project at the beneficiaries that exist (once each); a repeated name is refused; the journal has its own action.
            var added = await types.CreateAsync($"Ext Obligatoriu {suffix}");
            typeId = added.Id;
            var duplicate = await Rejects<BeneficiaryProjectTypeOperationException>(() => types.CreateAsync($"ext  OBLIGATORIU {suffix}"), "A repeated type name is refused");
            var afterAdd = await projects.GetForBeneficiaryAsync(first.Id);
            Check(afterAdd.Count(project => project.Name == added.Name) == 1 && duplicate is not null
                  && (await audit.GetEventsAsync()).Any(item => item.TimestampUtc >= started && item.Action == AuditActions.AddMandatoryProjectType && item.EntityId == added.Id.ToString()),
                "A new type makes its project at the existing beneficiaries (once each); a repeated name is refused; the addition is journaled");

            // 3. The check is idempotent; the project of a type cannot be renamed or deleted on its own, its observations can still be edited.
            await types.EnsureProjectsAsync();   // beneficiaries made by other checks since the first run may still lack the project
            var again = await types.EnsureProjectsAsync();
            var mandatory = afterAdd.First(project => project.Name == added.Name);
            var rename = await Rejects<ProjectOperationException>(() => projects.UpdateAsync(mandatory, new ProjectInput { BeneficiaryId = first.Id, Name = mandatory.Name + " x", Reason = "test" }), "A mandatory project cannot be renamed");
            var delete = await Rejects<ProjectOperationException>(() => projects.DeleteAsync(mandatory, "test"), "A mandatory project cannot be deleted on its own");
            var edited = await projects.UpdateAsync(mandatory, new ProjectInput { BeneficiaryId = first.Id, Name = mandatory.Name, Observations = "Nota", Reason = "test" });
            Check(again == 0, $"The start-up check changes nothing when everything exists (made {again})");
            Check(rename is not null && delete is not null && edited.Observations == "Nota",
                $"A mandatory project keeps its name and is not deleted on its own; its observations can be edited (observations '{edited.Observations}')");

            // 4. A deactivated type makes no project for a new beneficiary; activated again it makes the missing ones.
            var inactive = await types.SetActiveAsync(added, false);
            var second = await beneficiaries.CreateAsync(Legal($"Ext Obl Doi {suffix} SRL", "RO" + Random.Shared.Next(60000000, 69999999)));
            var withoutType = !(await projects.GetForBeneficiaryAsync(second.Id)).Any(project => project.Name == added.Name);
            var reactivated = await types.SetActiveAsync(inactive, true);
            var withType = (await projects.GetForBeneficiaryAsync(second.Id)).Any(project => project.Name == added.Name);
            Check(!inactive.Active && withoutType && reactivated.Active && withType, "A deactivated type makes no project for a new beneficiary; activated again it makes the missing ones");

            // 5. A beneficiary whose only projects are unused mandatory ones can be deleted (they go with it); an used one blocks the deletion.
            await beneficiaries.DeleteAsync(second, "test");
            var gone = !(await beneficiaries.GetBeneficiariesAsync()).Any(item => item.Id == second.Id);
            var blocked = await Rejects<BeneficiaryOperationException>(() => beneficiaries.DeleteAsync(first, "test"), "A beneficiary with a mandatory project that has observations is not deleted");
            Check(gone && blocked is not null, "A beneficiary with only unused mandatory projects is deleted with them; one whose mandatory project has observations is not");
        }
        finally
        {
            // The probe connection cleans what the test made: the project and the type (the beneficiaries are named with the suffix).
            await ExecuteAsync(probe, "DELETE FROM project_observations WHERE project_id IN (SELECT id FROM projects WHERE beneficiary_id IN (SELECT id FROM beneficiaries WHERE name LIKE @name))", ("@name", $"Ext Obl % {suffix} SRL"));
            await ExecuteAsync(probe, "DELETE FROM projects WHERE beneficiary_id IN (SELECT id FROM beneficiaries WHERE name LIKE @name)", ("@name", $"Ext Obl % {suffix} SRL"));
            await ExecuteAsync(probe, "DELETE FROM beneficiary_work_points WHERE beneficiary_id IN (SELECT id FROM beneficiaries WHERE name LIKE @name)", ("@name", $"Ext Obl % {suffix} SRL"));
            await ExecuteAsync(probe, "DELETE FROM beneficiaries WHERE name LIKE @name", ("@name", $"Ext Obl % {suffix} SRL"));
            if (typeId > 0)
            {
                await ExecuteAsync(probe, "DELETE FROM projects WHERE mandatory_type_id=@id", ("@id", typeId));
                await ExecuteAsync(probe, "DELETE FROM beneficiary_project_types WHERE id=@id", ("@id", typeId));
            }
        }
    }
}