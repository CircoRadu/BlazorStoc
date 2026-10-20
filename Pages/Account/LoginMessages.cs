using BlazorStoc.Services;

namespace BlazorStoc.Pages.Account;

// The two things a failed sign-in can mean: the credentials are wrong, or the database that holds the accounts cannot answer right now.
public static class LoginMessages
{
    public const string SessionClosed = "Sesiunea ta a fost închisă de un administrator. Autentifică-te din nou.";
    public const string WrongCredentials = "Utilizator sau parolă incorectă.";
    public const string DatabaseUnavailable = "Baza de date nu răspunde momentan sau este în curs un backup/o restaurare. Nu este o problemă a contului tău: încearcă din nou peste câteva momente.";

    public static bool IsDatabaseUnavailable(Exception exception) =>
        exception is MySqlConnector.MySqlException or MaintenanceInProgressException or TimeoutException or System.Net.Sockets.SocketException or System.IO.IOException;
}
