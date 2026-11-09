namespace BlazorStoc.Services;

// The rule of the titles of popups: "Operation – Object". The object is the name of the thing the popup works on; without a name only the kind is shown ("beneficiar").
public static class PopupSubject
{
    public static string Of(string kind, string? name) => string.IsNullOrWhiteSpace(name) ? kind : kind + " – " + name.Trim();
}
