namespace BlazorStoc.Services;

/// <summary>
/// One button of the bar above every window (<c>Components/Shared/AppDialog.razor</c>): registered by a module ("Raportează o eroare", "Propune"), shown only while that module
/// works (Setari -> Module), also in the windows that are already open. Lower <see cref="Order"/> comes first. The action is declarative: <see cref="Attributes"/> are written on
/// the button (the script of the module reads them, e.g. data-bug-open) and <see cref="OnClick"/> is called when the button is pressed in an interactive window.
/// </summary>
public sealed record DialogBarAction(string Id, string ModuleId, int Order, string Label, string Title, string Icon,
    IReadOnlyDictionary<string, object>? Attributes = null, Func<IServiceProvider, Task>? OnClick = null);

/// <summary>The actions registered by the modules (each module adds its own with <c>services.AddSingleton(new DialogBarAction(...))</c>), in the order they are shown.</summary>
public static class DialogBarActions
{
    public static IReadOnlyList<DialogBarAction> Ordered(IEnumerable<DialogBarAction> registered, Func<string, bool> moduleWorks) =>
        registered.Where(action => moduleWorks(action.ModuleId)).OrderBy(action => action.Order).ThenBy(action => action.Id, StringComparer.Ordinal).ToList();

    public static IServiceCollection AddDialogBarAction(this IServiceCollection services, DialogBarAction action)
    {
        if (!services.Any(item => item.ServiceType == typeof(DialogBarAction) && item.ImplementationInstance is DialogBarAction known && known.Id == action.Id))
            services.AddSingleton(action);
        return services;
    }
}
