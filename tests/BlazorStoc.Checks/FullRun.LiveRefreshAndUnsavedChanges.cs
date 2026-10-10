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
    // Lines 1310-1436 of the former Program.cs.
    internal static async Task LiveRefreshAndUnsavedChangesAsync(string[] args)
    {

        // Live refresh: bursts, own origin, busy forms, fallback.
        {
            var liveFeed = new InProcessChangeFeed();
            var own = Guid.NewGuid();
            var refreshes = 0; var renders = 0; var busy = false;
            using var live = new LiveRefresh(liveFeed, own, change => change.EntityType == "Produs" && change.EntityId == "1",
                () => { Interlocked.Increment(ref refreshes); return Task.CompletedTask; }, () => busy,
                () => { Interlocked.Increment(ref renders); return Task.CompletedTask; }, TimeSpan.FromMilliseconds(60));
            for (var i = 0; i < 3; i++) liveFeed.Publish(new("Produs", AuditActions.Edit, "1", Guid.Empty, DateTime.UtcNow));
            liveFeed.Publish(new("Produs", AuditActions.Edit, "2", Guid.Empty, DateTime.UtcNow));
            liveFeed.Publish(new("Produs", AuditActions.Edit, "1", own, DateTime.UtcNow));
            await Task.Delay(400);
            Check(refreshes == 1 && !live.HasPendingChange, "A burst of relevant events refreshes once; other entities and the session's own changes are ignored");

            busy = true;
            liveFeed.Publish(new("Produs", AuditActions.Edit, "1", Guid.Empty, DateTime.UtcNow));
            await Task.Delay(300);
            Check(refreshes == 1 && live.HasPendingChange && renders >= 1, "While a form is open nothing is refreshed and the page is asked to show a notice");
            busy = false;
            await live.ReloadPendingAsync();
            Check(refreshes == 2 && !live.HasPendingChange, "The user's reload applies the pending change and clears the notice");
        }
        {
            var fallbackRefreshes = 0; var fallbackBusy = true;
            using var fallback = new LiveRefresh(new InProcessChangeFeed(), Guid.NewGuid(), _ => true,
                () => { Interlocked.Increment(ref fallbackRefreshes); return Task.CompletedTask; }, () => fallbackBusy,
                () => Task.CompletedTask, fallbackInterval: TimeSpan.FromMilliseconds(80));
            await Task.Delay(300);
            Check(fallbackRefreshes == 0, "The periodic fallback does not refresh while a form is open");
            fallbackBusy = false;
            await Task.Delay(400);
            Check(fallbackRefreshes >= 1, "The periodic fallback refreshes when the page is idle even if no notification arrived");
        }



        Check(ProductLockRules.LeaseSeconds >= 60 && ProductLockRules.LeaseSeconds <= 120 && ProductLockRules.HeartbeatSeconds * 2 < ProductLockRules.LeaseSeconds,
            "The lease expires within 1–2 minutes and the heartbeat renews it well before that");
        {
            var sample = new ProductLock(3, "ana", "s", new DateTime(2026, 9, 25, 10, 5, 0, DateTimeKind.Utc), DateTime.UtcNow, DateTime.UtcNow.AddSeconds(90), 90);
            var message = ProductLockRules.HeldMessage(sample);
            Check(message.Contains("ana") && message.Contains(sample.AcquiredUtc.ToLocalTime().ToString("HH:mm")) && message.Contains("consulta"),
                "The read-only message says who edits the product, since when, and that it can still be consulted");
        }

        // Task 1: unsaved-changes tracking and the leave warning.
        {
            var guard = new UnsavedChanges();
            var name = "Ciocan";
            var closedOne = 0; var closedTwo = 0; var discardingSeen = false;
            var first = guard.Track(() => FormSnapshot.Values(name), () => { closedOne++; discardingSeen = guard.IsDiscarding; return Task.CompletedTask; });
            var otherText = "Nemodificat";
            var second = guard.Track(() => FormSnapshot.Values(otherText), () => { closedTwo++; return Task.CompletedTask; });
            Check(guard.TrackerCount == 2 && !guard.HasUnsavedChanges && !first.IsModified, "A form that was just opened is not modified");
            name = "Ciocan 2 kg";
            Check(first.IsModified && guard.HasUnsavedChanges, "A changed value marks the form as modified");
            name = "Ciocan";
            Check(!first.IsModified && !guard.HasUnsavedChanges, "Returning to the initial value makes the form unmodified again");

            var ran = 0;
            await first.RunAfterConfirmAsync(() => { ran++; return Task.CompletedTask; });
            Check(ran == 1 && guard.Pending is null, "An unmodified form is closed without asking");
            name = "Ciocan nou";
            await first.RunAfterConfirmAsync(() => { ran++; return Task.CompletedTask; });
            Check(ran == 1 && guard.Pending is not null, "A modified form asks first and does not run the action yet");
            guard.Cancel();
            Check(ran == 1 && guard.Pending is null && first.IsModified, "\"Înapoi la editare\" keeps the values, the form and the warning");
            await first.RunAfterConfirmAsync(() => { ran++; return Task.CompletedTask; });
            await guard.ConfirmAsync();
            Check(ran == 2 && closedOne == 1 && discardingSeen && !guard.IsDiscarding && !first.IsModified && closedTwo == 0,
                "\"Părăsește editarea\" discards only that form and then runs the interrupted action");

            otherText = "Modificat";
            var navigated = "";
            guard.Request(() => { navigated = "/beneficiari"; return Task.CompletedTask; });
            await guard.ConfirmAsync();
            Check(navigated == "/beneficiari" && closedTwo == 1 && closedOne == 1 && !guard.HasUnsavedChanges,
                "Leaving the page discards every modified form (and only those) and then performs the navigation");

            otherText = "Salvat";
            Check(second.IsModified == false, "A discarded form no longer counts as modified");
            var saved = guard.Track(() => FormSnapshot.Values(otherText), () => Task.CompletedTask);
            otherText = "Salvat cu succes";
            Check(saved.IsModified, "A new tracker starts from the values at that moment");
            saved.Rebase();
            Check(!saved.IsModified && !guard.HasUnsavedChanges, "A successful save makes the saved values the unmodified state");
            otherText = "Altă valoare";
            Check(saved.IsModified, "Changes after a save are detected again");

            var flips = 0;
            guard.Changed += () => flips++;
            saved.NotifyRendered(); saved.NotifyRendered();
            otherText = "Salvat cu succes"; // back to the saved value
            saved.Rebase();
            Check(flips >= 1, "The host is notified when the unsaved state flips");

            var addingValue = "";
            var addTracker = guard.Track(() => FormSnapshot.Values(addingValue), () => Task.CompletedTask, adding: true);
            var editValue = "a";
            var editTracker = guard.Track(() => FormSnapshot.Values(editValue), () => Task.CompletedTask);
            addingValue = "nou";
            guard.Request(() => Task.CompletedTask);
            Check(guard.Pending is { Adding: true }, "The question says \"adăugare\" when only forms that add a new object are affected");
            guard.Cancel();
            editValue = "b";
            guard.Request(() => Task.CompletedTask);
            Check(guard.Pending is { Adding: false }, "The question says \"editare\" as soon as an edited (existing) object is affected");
            guard.Cancel();
            guard.Request(() => Task.CompletedTask, addTracker);
            Check(guard.Pending is { Adding: true }, "Closing a single add form asks about the adding");
            guard.Cancel();
            addTracker.Dispose(); editTracker.Dispose();

            saved.Dispose(); first.Dispose(); second.Dispose();
            Check(guard.TrackerCount == 0 && !guard.HasUnsavedChanges, "Closing an editor removes its tracker");

            var product = ProductInput.From(data[0]);
            var before = FormSnapshot.Of(product);
            product.Reason = "Doar un motiv";
            Check(FormSnapshot.Of(product) == before, "Typing only the change reason is not an unsaved value change");
            product.Name += " x";
            Check(FormSnapshot.Of(product) != before, "Changing a product field changes the snapshot");
            Check(FormSnapshot.Of(product, "imagine.png") != FormSnapshot.Of(product), "A selected image counts as a change");
            var user = new WebUserInput { Username = "ana", Password = "abc" };
            var userBefore = FormSnapshot.Of(user);
            user.Password = "abcd";
            Check(FormSnapshot.Of(user) != userBefore, "A typed password counts as a change (compared only through a hash)");
        }
    }
}
