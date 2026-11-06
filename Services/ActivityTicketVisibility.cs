namespace BlazorStoc.Services;

// Which pieces of information of an imported ticket are shown (Setari -> Preluare ActivityApp, sectiunea 6): a piece that was switched off is hidden in every window that shows a ticket. The windows
// of other modules ask this contract, so they need nothing from the import module; without it (the module is out) everything is shown.

/// <summary>An extra field of the ticket as the windows show it: the key under which its text is kept and the name the administrator gave it.</summary>
public sealed record TicketExtraLabel(string Key, string Label);

/// <summary>The pieces of information of a ticket that are on (title and number are always shown) and the extra fields in the order of the settings.</summary>
public sealed record TicketVisibility(bool Problem, bool Resolution, bool Report, bool Executor, IReadOnlyList<TicketExtraLabel> Extras)
{
    public static TicketVisibility All { get; } = new(true, true, true, true, []);
}

public interface IActivityTicketVisibility
{
    Task<TicketVisibility> GetAsync(CancellationToken cancellationToken = default);
}

/// <summary>Used when the import module is out: nothing is hidden and no extra field is known.</summary>
public sealed class AllTicketFieldsVisible : IActivityTicketVisibility
{
    public Task<TicketVisibility> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(TicketVisibility.All);
}
