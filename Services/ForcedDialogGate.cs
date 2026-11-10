namespace BlazorStoc.Services;

/// <summary>
/// Per circuit: true while a window the user must answer first is open (new tasks to take over). The layout shows it over every page; the navigation guard of the circuit
/// (Components/Shared/SessionTracker.razor) keeps the user on the page while it is true. Not a security boundary: the window itself covers every page that is loaded.
/// </summary>
public sealed class ForcedDialogGate
{
    public bool Active { get; set; }
}
