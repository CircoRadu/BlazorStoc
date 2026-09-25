using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace BlazorStoc.Services;

// Task 1: warns before an edit form with unsaved changes is left. One scoped instance per session (circuit); every
// open editor registers a tracker, and any action that would leave the page first asks this service whether a
// tracker is modified. The check is lazy (evaluated when the user acts), because text fields report their value to
// the server only when they lose focus, and that message always reaches the server before the click that follows.
public sealed class UnsavedChanges
{
    private readonly object gate = new();
    private readonly List<EditTracker> trackers = [];
    private PendingLeave? pending;
    private bool lastHasChanges;

    // Raised when a leave request is opened or closed, and when the registered editors or their state change.
    public event Action? Changed;

    // True while discarded editors are being closed, so a page does not navigate away from its own "close" handler.
    public bool IsDiscarding { get; private set; }

    public PendingLeave? Pending { get { lock (gate) return pending; } }

    public int TrackerCount { get { lock (gate) return trackers.Count; } }

    public bool HasUnsavedChanges { get { lock (gate) return trackers.Any(tracker => tracker.IsModified); } }

    // `adding` = the form creates a new object (the question then says "adăugare" instead of "editare").
    public EditTracker Track(Func<string> snapshot, Func<Task> discard, bool adding = false)
    {
        var tracker = new EditTracker(this, snapshot, discard, adding);
        lock (gate) trackers.Add(tracker);
        RaiseChanged(true);
        return tracker;
    }

    internal void Remove(EditTracker tracker)
    {
        bool removed;
        lock (gate)
        {
            removed = trackers.Remove(tracker);
            if (pending?.Only == tracker) pending = null;
        }
        if (removed) RaiseChanged(true);
    }

    // Called by an editor after each render: raises Changed only when "has unsaved changes" flipped.
    internal void Rendered()
    {
        var now = HasUnsavedChanges;
        bool flipped;
        lock (gate) { flipped = now != lastHasChanges; lastHasChanges = now; }
        if (flipped) Changed?.Invoke();
    }

    private void RaiseChanged(bool includeState)
    {
        if (includeState) lock (gate) lastHasChanges = trackers.Any(tracker => tracker.IsModified);
        Changed?.Invoke();
    }

    // Opens the "editing not finished" question. `only` limits the discarded editors to one (closing a single form);
    // without it every modified editor is discarded (leaving the page).
    public void Request(Func<Task> continuation, EditTracker? only = null)
    {
        lock (gate)
        {
            var affected = only is not null ? [only] : trackers.Where(tracker => tracker.IsModified).ToArray();
            pending = new PendingLeave(continuation, only, affected.Length > 0 && affected.All(tracker => tracker.Adding));
        }
        Changed?.Invoke();
    }

    public void Cancel()
    {
        lock (gate) pending = null;
        Changed?.Invoke();
    }

    // "Părăsește editarea": discards the affected editors without saving, then performs the interrupted action.
    public async Task ConfirmAsync()
    {
        PendingLeave? leave;
        EditTracker[] affected;
        lock (gate)
        {
            leave = pending;
            pending = null;
            affected = leave is null ? [] : leave.Only is { } one ? [one] : [.. trackers.Where(tracker => tracker.IsModified)];
        }
        if (leave is null) return;
        IsDiscarding = true;
        try
        {
            foreach (var tracker in affected) await tracker.DiscardAsync().ConfigureAwait(true);
        }
        finally { IsDiscarding = false; }
        Changed?.Invoke();
        await leave.Continue().ConfigureAwait(true);
    }
}

// Adding: every form that would be discarded creates a new object (the dialog wording differs from editing).
public sealed record PendingLeave(Func<Task> Continue, EditTracker? Only, bool Adding);

// One open editor: a snapshot of its values taken when it opened (kept as a hash, so typed values such as passwords are
// not kept twice), a way to read its current values and a way to close it without saving.
public sealed class EditTracker : IDisposable
{
    private readonly UnsavedChanges owner;
    private readonly Func<string> snapshot;
    private readonly Func<Task> discard;
    private string baseline;
    private bool discarded, disposed;

    internal EditTracker(UnsavedChanges owner, Func<string> snapshot, Func<Task> discard, bool adding)
    {
        this.owner = owner; this.snapshot = snapshot; this.discard = discard; Adding = adding;
        baseline = Hash(snapshot());
    }

    public bool Adding { get; }

    // Modified = the current values differ from the values the editor opened with (or from the last save).
    public bool IsModified
    {
        get
        {
            if (discarded || disposed) return false;
            try { return Hash(snapshot()) != baseline; }
            catch (Exception) { return false; }
        }
    }

    // A successful save: the current values become the new "unmodified" state.
    public void Rebase() { baseline = Hash(snapshot()); discarded = false; owner.Rendered(); }

    public void NotifyRendered() { if (!disposed) owner.Rendered(); }

    // Runs `action` at once when the editor is unmodified; otherwise asks the user first, discarding only this editor.
    public Task RunAfterConfirmAsync(Func<Task> action)
    {
        if (!IsModified) return action();
        owner.Request(action, this);
        return Task.CompletedTask;
    }

    internal async Task DiscardAsync()
    {
        discarded = true;
        try { await discard().ConfigureAwait(true); }
        catch (ObjectDisposedException) { }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        owner.Remove(this);
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

// Builds the comparable text of an editor's values from its input model. The change reason is not a value of the
// object, so typing only a reason does not count as a modification.
public static class FormSnapshot
{
    public static string Of(object model, params object?[] extras)
    {
        var text = new StringBuilder();
        foreach (var property in model.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(property => property.CanRead && property.GetIndexParameters().Length == 0 && property.Name != "Reason")
                     .OrderBy(property => property.Name, StringComparer.Ordinal))
            text.Append(property.Name).Append('=').Append(Format(property.GetValue(model))).Append('\u001f');
        foreach (var extra in extras) text.Append(Format(extra)).Append('\u001f');
        return text.ToString();
    }

    public static string Values(params object?[] values) => string.Join('\u001f', values.Select(Format));

    private static string Format(object? value) => value switch
    {
        null => "",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? ""
    };
}
