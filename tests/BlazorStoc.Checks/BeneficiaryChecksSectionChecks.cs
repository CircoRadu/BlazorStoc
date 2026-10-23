using BlazorStoc.Components.Shared;
using BlazorStoc.Services;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorStoc.Checks;

// Beneficiari -> "De verificat": the partners of ActivityApp that need a human look (retry in ANAF, accept by hand, discard with a confirmation). The service is a fake (the real one is checked
// against MariaDB with the import).
public static class BeneficiaryChecksSectionChecks
{
    private sealed class FakeChecks : IBeneficiaryCheckService
    {
        public List<BeneficiaryCheck> Items =
        [
            new(1, 501, 11, "Alfa SRL", "RO123456", BeneficiaryCheckReasons.AnafUnavailable, BeneficiaryCheckReasons.Text(BeneficiaryCheckReasons.AnafUnavailable), DateTime.UtcNow, 2),
            new(2, 502, null, "Beta SRL", "7654321", BeneficiaryCheckReasons.MissingAddress, BeneficiaryCheckReasons.Text(BeneficiaryCheckReasons.MissingAddress), DateTime.UtcNow, 0),
            new(3, 503, 13, "Gama SRL", "123456789012", BeneficiaryCheckReasons.CuiInvalidForm, BeneficiaryCheckReasons.Text(BeneficiaryCheckReasons.CuiInvalidForm), DateTime.UtcNow, 1)
        ];
        public List<long> Retried = [], Accepted = [], Discarded = [];
        public Task<IReadOnlyList<BeneficiaryCheck>> GetPendingAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<BeneficiaryCheck>>([.. Items]);
        public Task<string> RetryAsync(long id, CancellationToken cancellationToken = default) { Retried.Add(id); Items.RemoveAll(item => item.Id == id); return Task.FromResult("ANAF a confirmat CUI-ul: beneficiarul este verificat."); }
        public Task AcceptAsync(long id, CancellationToken cancellationToken = default) { Accepted.Add(id); Items.RemoveAll(item => item.Id == id); return Task.CompletedTask; }
        public Task DiscardAsync(long id, CancellationToken cancellationToken = default) { Discarded.Add(id); Items.RemoveAll(item => item.Id == id); return Task.CompletedTask; }
    }

    public static Task RunAsync(Action<bool, string> check)
    {
        var service = new FakeChecks();
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddLogging();
        context.Services.AddSingleton<IBeneficiaryCheckService>(service);
        context.Services.AddSingleton<IAccessControl>(new TestAccessControl(true, "admin"));
        var changed = 0;
        var cut = context.Render<BeneficiaryChecksSection>(parameters => parameters.Add(component => component.Changed, () => changed++));
        cut.WaitForAssertion(() => { if (cut.FindAll("#checks-table tbody tr").Count == 0) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
        check(cut.FindAll("#checks-table tbody tr").Count == 3 && cut.Find("#check-1").TextContent.Contains("ANAF nu a putut răspunde", StringComparison.Ordinal) && cut.Find("#check-1").TextContent.Contains('2')
            && cut.Find("#check-1 a").GetAttribute("href") == "/beneficiari/11" && cut.FindAll("#check-2 a").Count == 0, "Beneficiaries to verify: each partner is listed with the reason, the tickets that wait and the link to the beneficiary (none while there is no beneficiary)");
        check(cut.Find("#check-3 button.renew").HasAttribute("disabled") && cut.Find("#check-2 button.check").HasAttribute("disabled"), "Beneficiaries to verify: a CUI of the wrong form cannot be retried and a partner without beneficiary cannot be accepted");
        cut.Find("#check-1 button.renew").Click();
        cut.WaitForAssertion(() => { if (cut.FindAll("#check-1").Count != 0) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
        check(service.Retried.SequenceEqual([1L]) && changed == 1 && cut.Markup.Contains("ANAF a confirmat CUI-ul", StringComparison.Ordinal), "Beneficiaries to verify: the retry shows what ANAF answered, leaves the list and tells the page to reload");
        cut.Find("#check-3 button.check").Click();
        cut.WaitForAssertion(() => { if (cut.FindAll("#check-3").Count != 0) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
        check(service.Accepted.SequenceEqual([3L]) && cut.Markup.Contains("a fost acceptat", StringComparison.Ordinal), "Beneficiaries to verify: accepting by hand takes the beneficiary out of the list");
        cut.Find("#check-2 button.delete").Click();
        check(service.Discarded.Count == 0 && cut.Markup.Contains("Nici partenerul, nici tichetele lui nu se mai preiau", StringComparison.Ordinal), "Beneficiaries to verify: discarding asks for a confirmation first");
        cut.Find("#check-2 button[aria-label^='Confirmă discard']").Click();
        cut.WaitForAssertion(() => { if (cut.FindAll("#beneficiary-checks").Count != 0) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
        check(service.Discarded.SequenceEqual([2L]), "Beneficiaries to verify: after the confirmation the partner is discarded and, with nothing left, the card disappears");
        return Task.CompletedTask;
    }
}
