using System.Reflection;
using BlazorStoc.Components.Shared;
using BlazorStoc.Modules.BugReports;
using BlazorStoc.Modules.Proposals;
using BlazorStoc.Services;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorStoc.Checks;

// The badges on the tabs of Setari -> Dezvoltare (the tile, Erori, Propuneri): the number of what is not resolved, hidden at 0, following by itself when something is added or changed.
public static class DevelopmentBadgeChecks
{
    // A stand-in of a service that only answers CountOpenAsync (the rest is not used by the badge).
    public class CountProxy : DispatchProxy
    {
        public int Count { get; set; }
        public bool Fail { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name != "CountOpenAsync") throw new NotSupportedException(targetMethod?.Name);
            return Fail ? Task.FromException<int>(new InvalidOperationException("down")) : Task.FromResult(Count);
        }
    }

    private static (T Service, CountProxy Proxy) Stub<T>(int count) where T : class
    {
        var service = DispatchProxy.Create<T, CountProxy>();
        var proxy = (CountProxy)(object)service;
        proxy.Count = count;
        return (service, proxy);
    }

    public static Task RunAsync(Action<bool, string> check)
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var (bugs, bugCount) = Stub<IBugReportService>(3);
        var (proposals, proposalCount) = Stub<IProposalService>(4);
        var feed = new InProcessChangeFeed();
        context.Services.AddSingleton(bugs);
        context.Services.AddSingleton(proposals);
        context.Services.AddSingleton<IChangeFeed>(feed);

        var errors = context.Render<DevelopmentBadge>(parameters => parameters.Add(p => p.Kind, DevelopmentBadgeKind.Errors).Add(p => p.Id, "b-errors"));
        var proposalsBadge = context.Render<DevelopmentBadge>(parameters => parameters.Add(p => p.Kind, DevelopmentBadgeKind.Proposals).Add(p => p.Id, "b-proposals"));
        var total = context.Render<DevelopmentBadge>(parameters => parameters.Add(p => p.Kind, DevelopmentBadgeKind.Total).Add(p => p.Id, "b-total"));
        check(errors.Find("#b-errors").TextContent == "3" && proposalsBadge.Find("#b-proposals").TextContent == "4" && total.Find("#b-total").TextContent == "7",
            "Development badges: the tab Erori counts the reports not resolved, Propuneri the proposals not closed and the tile Dezvoltare both");

        bugCount.Count = 5;
        feed.Publish(new ChangeEvent(AuditEntities.BugReport, AuditActions.CreateBugReport, "9", Guid.NewGuid(), DateTime.UtcNow));
        errors.WaitForAssertion(() => { if (errors.Find("#b-errors").TextContent != "5") throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
        total.WaitForAssertion(() => { if (total.Find("#b-total").TextContent != "9") throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
        check(proposalsBadge.Find("#b-proposals").TextContent == "4", "Development badges: a new report raises the badge of Erori and the total at once, in every session, and leaves Propuneri as it was");

        proposalCount.Count = 0;
        feed.Publish(new ChangeEvent(AuditEntities.Proposal, AuditActions.ChangeProposalState, "2", Guid.NewGuid(), DateTime.UtcNow));
        proposalsBadge.WaitForAssertion(() => { if (proposalsBadge.FindAll("#b-proposals").Count != 0) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
        check(proposalsBadge.FindAll(".count-badge").Count == 0, "Development badges: at 0 the badge is not shown");

        bugCount.Fail = true;
        feed.Publish(new ChangeEvent(AuditEntities.BugReport, AuditActions.DeleteBugReport, "9", Guid.NewGuid(), DateTime.UtcNow));
        errors.WaitForAssertion(() => { if (errors.FindAll("#b-errors").Count != 0) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
        check(errors.FindAll(".count-badge").Count == 0, "Development badges: when the module is out (or the count fails) the badge is not shown and nothing breaks");
        return Task.CompletedTask;
    }
}
