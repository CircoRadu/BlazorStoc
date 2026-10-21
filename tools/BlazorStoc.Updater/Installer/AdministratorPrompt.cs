using System.Text;
using System.Text.RegularExpressions;

namespace BlazorStoc.Updater;

/// <summary>The first administrator account of the application that the installer makes in the users of the application (web_users).</summary>
public sealed record AdministratorAccount(string Username, string DisplayName, string Password);

/// <summary>The rules of the application for a user name and a password (Services/WebUsers.cs, WebUserInput): the installer refuses early what the application would refuse.</summary>
public static partial class AdministratorRules
{
    public const int MinimumPasswordLength = 8;
    public const int MaximumPasswordLength = 200;
    public const string ReservedUsername = "admin";

    public static string? UsernameProblem(string? username) =>
        username is null || username.Trim().Length < 3 || username.Trim().Length > 100 ? "The user name has 3 to 100 characters."
        : !UsernameShape().IsMatch(username.Trim()) ? "The user name may contain letters, digits, dot, dash and underscore only."
        // "admin" is the administrator of the configuration: at sign-in that name is checked only against the generated password, so a user of the database with that name could never sign in.
        : string.Equals(username.Trim(), ReservedUsername, StringComparison.OrdinalIgnoreCase) ? $"The user name \"{ReservedUsername}\" is reserved (it is the administrator of the configuration): choose another."
        : null;

    public static string? PasswordProblem(string? password) =>
        password is null || password.Length < MinimumPasswordLength ? $"The password has at least {MinimumPasswordLength} characters."
        : password.Length > MaximumPasswordLength ? $"The password has at most {MaximumPasswordLength} characters."
        : null;

    [GeneratedRegex(@"^[\p{L}0-9._-]+$")]
    private static partial Regex UsernameShape();
}

/// <summary>
/// The questions of the installer for the administrator account: user name, password and the password again. What is typed as a password shows as *; while the second password is typed
/// the line says, after every key, whether it is the same as the first. The keys and the output are given, so the checks type for a person.
/// </summary>
public sealed class AdministratorPrompt(Func<ConsoleKeyInfo> readKey, TextWriter output)
{
    public const string Match = "[the passwords match]";
    public const string Differ = "[the passwords do not match yet]";

    public AdministratorAccount Ask()
    {
        output.WriteLine();
        output.WriteLine("Administrator account of the application (the first user; more are made later in Utilizatori).");
        string username;
        while (true)
        {
            username = Read("User name: ", masked: false, _ => "").Trim();
            if (AdministratorRules.UsernameProblem(username) is { } problem) { output.WriteLine(problem); continue; }
            break;
        }
        var displayName = Read("Display name (Enter = the user name): ", masked: false, _ => "").Trim();
        while (true)
        {
            var password = Read("Password: ", masked: true, typed => typed.Length == 0 ? "" : $"[{typed.Length} characters, at least {AdministratorRules.MinimumPasswordLength}]");
            if (AdministratorRules.PasswordProblem(password) is { } problem) { output.WriteLine(problem); continue; }
            var again = Read("Repeat the password: ", masked: true, typed => typed.Length == 0 ? "" : string.Equals(typed, password, StringComparison.Ordinal) ? Match : Differ);
            if (!string.Equals(again, password, StringComparison.Ordinal)) { output.WriteLine("The passwords are not the same: type them again."); continue; }
            return new AdministratorAccount(username, displayName.Length == 0 ? username : displayName, password);
        }
    }

    // One line typed key by key: Enter ends it, Backspace removes the last character, Escape clears it. After every key the line is written again with the text of status(typed).
    private string Read(string label, bool masked, Func<string, string> status)
    {
        var typed = new StringBuilder();
        var written = 0;
        output.Write(label);
        while (true)
        {
            var key = readKey();
            if (key.Key == ConsoleKey.Enter) { output.WriteLine(); return typed.ToString(); }
            if (key.Key == ConsoleKey.Backspace) { if (typed.Length > 0) typed.Length--; }
            else if (key.Key == ConsoleKey.Escape) typed.Clear();
            else if (!char.IsControl(key.KeyChar)) typed.Append(key.KeyChar);
            else continue;
            var shown = masked ? new string('*', typed.Length) : typed.ToString();
            var text = label + shown + (status(typed.ToString()) is { Length: > 0 } note ? "  " + note : "");
            output.Write("\r" + text + new string(' ', Math.Max(0, written - text.Length)) + new string('\b', Math.Max(0, written - text.Length)));
            written = text.Length;
        }
    }
}
