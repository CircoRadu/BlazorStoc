using System.Globalization;
using Microsoft.AspNetCore.DataProtection;
using MySqlConnector;

namespace BlazorStoc.Services;

// Setari -> Baza de date -> Conexiune -> Conturile bazei: the predefined accounts of the database (Services/DatabaseAccounts.cs). The page checks that each one connects and
// has exactly its rights, changes their passwords and, with the account that administers the server (admin.private.cnf in the folder of the data), puts them back.
// An account changes its own password (the connection is made with the current one), so the administration account is needed only for the reset and the export.

public sealed record DatabaseAccountState(
    string Key, string Purpose, string User, string CatalogUser, bool Configured, bool Connects, string? Problem, bool UsesDefaultPassword,
    IReadOnlyList<string> MissingRights, IReadOnlyList<string> ExtraRights, DateTime? ChangedUtc)
{
    public bool NameDiffers => !string.Equals(User, CatalogUser, StringComparison.Ordinal);
    public bool RightsMatch => MissingRights.Count == 0 && ExtraRights.Count == 0;
    public bool Healthy => Connects && RightsMatch && !UsesDefaultPassword;
}

public sealed record DatabaseAccountChange(string Key, string User, bool Success, string Message);

public sealed record DatabaseAccountsResult(bool Success, string Message, IReadOnlyList<DatabaseAccountChange> Changes, IReadOnlyList<DatabaseAccountState> States);

public sealed record DatabaseConfigurationExport(string FileName, byte[] Content, bool IncludesAdministration);

public interface IDatabaseAccountService
{
    Task<IReadOnlyList<DatabaseAccountState>> GetStatesAsync(CancellationToken cancellationToken = default);
    Task<DatabaseAccountsResult> VerifyAsync(CancellationToken cancellationToken = default);
    Task<DatabaseAccountsResult> ChangePasswordsAsync(IReadOnlyDictionary<string, string> newPasswords, string administratorPassword, CancellationToken cancellationToken = default);
    Task<DatabaseAccountsResult> ResetAsync(IReadOnlyDictionary<string, string>? passwords, bool useDefaults, string administratorPassword, CancellationToken cancellationToken = default);
    Task<DatabaseConfigurationExport> ExportAsync(string passphrase, bool includeAdministration, string administratorPassword, CancellationToken cancellationToken = default);
    Task<DatabaseAccountsResult> ImportAsync(byte[] file, string passphrase, string administratorPassword, CancellationToken cancellationToken = default);
    /// <summary>True when at least one account still has the password of the catalog (what the instalation gave it): the administrator must change it.</summary>
    Task<bool> AnyDefaultPasswordAsync(CancellationToken cancellationToken = default);
    /// <summary>A password written before the server was changed and not promoted (the application stopped in between) is promoted, or dropped when the old one still works.</summary>
    Task RecoverPendingAsync(CancellationToken cancellationToken = default);
    bool HasAdministrationAccount { get; }
}

public sealed class DatabaseAccountService(IConfiguration configuration, IDataProtectionProvider protection, IAccessControl access, IAuditTrail audit, IOperationLockService locks,
    ILogger<DatabaseAccountService> logger, IUserAuthenticator? authenticator = null, IReadOnlyList<DatabaseAccountDefinition>? catalog = null, TimeProvider? clock = null) : IDatabaseAccountService
{
    public const string BusyMessage = "Rulează o copie de siguranță, o restaurare sau altă operație asupra bazei de date; încearcă după ce se termină.";
    public const string NoAdministrationMessage = "Nu există contul care administrează serverul bazei de date (fișierul admin.private.cnf din folderul de date).";
    private static readonly SemaphoreSlim OneAtATime = new(1, 1);
    private static readonly TimeSpan DefaultCheckLifetime = TimeSpan.FromSeconds(60);
    private static (DateTime At, bool Any)? defaultCheck;
    private readonly IReadOnlyList<DatabaseAccountDefinition> accounts = catalog ?? DatabaseAccounts.All;
    private readonly IDataProtector protector = protection.CreateProtector(DatabaseOverrideFile.ProtectorPurpose);
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private string OverridePath => DatabaseOverrideFile.PathFor(configuration);

    public bool HasAdministrationAccount => DatabaseAdministrationAccount.Read(configuration) is not null;

    // ---- State ------------------------------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<DatabaseAccountState>> GetStatesAsync(CancellationToken cancellationToken = default)
    {
        await access.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        return await ReadStatesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<DatabaseAccountState>> ReadStatesAsync(CancellationToken cancellationToken)
    {
        var states = new List<DatabaseAccountState>();
        foreach (var account in accounts) states.Add(await ReadStateAsync(account, cancellationToken).ConfigureAwait(false));
        return states;
    }

    private async Task<DatabaseAccountState> ReadStateAsync(DatabaseAccountDefinition account, CancellationToken cancellationToken)
    {
        var user = UserOf(account);
        var password = configuration[account.PasswordKey];
        var changed = DateTime.TryParse(configuration[$"Database:Accounts:{account.Key}:ChangedUtc"], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed) ? parsed : (DateTime?)null;
        if (string.IsNullOrEmpty(password))
            return new(account.Key, account.Purpose, user, account.User, false, false, "Parola contului nu este configurată.", false, [], [], changed);
        var (connects, problem, grants) = await TryConnectAsync(user, password, withGrants: true, cancellationToken).ConfigureAwait(false);
        var usesDefault = password == account.DefaultPassword;
        if (!usesDefault && !connects) usesDefault = (await TryConnectAsync(user, account.DefaultPassword, withGrants: false, cancellationToken).ConfigureAwait(false)).Connects;
        var comparison = connects ? DatabaseAccounts.CompareGrants(account, grants) : new DatabaseAccounts.GrantComparison([], []);
        return new(account.Key, account.Purpose, user, account.User, true, connects, problem, usesDefault, comparison.Missing, comparison.Extra, changed);
    }

    public async Task<DatabaseAccountsResult> VerifyAsync(CancellationToken cancellationToken = default)
    {
        await access.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var states = await ReadStatesAsync(cancellationToken).ConfigureAwait(false);
        var problems = states.Count(state => !state.Healthy);
        await RecordAsync(AuditActions.VerifyDatabaseAccounts, "Conturile bazei de date", $"{states.Count - problems} din {states.Count} conturi în regulă", cancellationToken).ConfigureAwait(false);
        InvalidateDefaultCheck();
        return new(problems == 0, problems == 0 ? "Toate conturile se conectează și au exact drepturile din catalog." : $"{problems} din {states.Count} conturi cer atenție.", [], states);
    }

    public async Task<bool> AnyDefaultPasswordAsync(CancellationToken cancellationToken = default)
    {
        if (defaultCheck is { } cached && time.GetUtcNow().UtcDateTime - cached.At < DefaultCheckLifetime) return cached.Any;
        var any = false;
        foreach (var account in accounts)
        {
            var password = configuration[account.PasswordKey];
            if (string.IsNullOrEmpty(password)) continue;
            if (password == account.DefaultPassword) { any = true; continue; }
            // Only an answer from the server counts: a database that does not answer never locks the administrator out.
            var current = await TryConnectAsync(UserOf(account), password, withGrants: false, cancellationToken).ConfigureAwait(false);
            if (!current.Connects && current.Reachable && (await TryConnectAsync(UserOf(account), account.DefaultPassword, withGrants: false, cancellationToken).ConfigureAwait(false)).Connects) any = true;
        }
        defaultCheck = (time.GetUtcNow().UtcDateTime, any);
        return any;
    }

    private static void InvalidateDefaultCheck() => defaultCheck = null;

    // ---- Change of passwords ---------------------------------------------------------------------------------------------------

    public async Task<DatabaseAccountsResult> ChangePasswordsAsync(IReadOnlyDictionary<string, string> newPasswords, string administratorPassword, CancellationToken cancellationToken = default)
    {
        await access.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        await new AdministratorReconfirmation(configuration, access, authenticator, logger).ConfirmAsync(administratorPassword, cancellationToken).ConfigureAwait(false);
        if (newPasswords.Count == 0) throw new DatabaseSettingsException("Alege cel puțin un cont.");
        foreach (var (key, password) in newPasswords)
        {
            if (accounts.All(account => !string.Equals(account.Key, key, StringComparison.OrdinalIgnoreCase))) throw new DatabaseSettingsException("Contul nu face parte din catalog.");
            if (DatabaseAccounts.ValidatePassword(password) is { } problem) throw new DatabaseSettingsException(problem);
        }
        return await WithLockAsync("Schimbare parole conturi bază de date", async () =>
        {
            var changes = new List<DatabaseAccountChange>();
            // The application account last: its pool of connections is the one the page itself uses.
            foreach (var account in accounts.OrderBy(item => item.IsApplication ? 1 : 0))
            {
                var key = newPasswords.Keys.FirstOrDefault(item => string.Equals(item, account.Key, StringComparison.OrdinalIgnoreCase));
                if (key is null) continue;
                var change = await ChangeOneAsync(account, newPasswords[key], cancellationToken).ConfigureAwait(false);
                changes.Add(change);
                await RecordAsync(AuditActions.ChangeDatabaseAccountPassword, account.User, change.Success ? $"Parola contului {change.User} a fost schimbată" : $"Parola contului {change.User} nu a fost schimbată: {change.Message}", cancellationToken).ConfigureAwait(false);
            }
            InvalidateDefaultCheck();
            var states = await ReadStatesAsync(cancellationToken).ConfigureAwait(false);
            var ok = changes.All(change => change.Success);
            return new DatabaseAccountsResult(ok, ok ? "Parolele au fost schimbate și conturile verificate." : "Unele parole nu au putut fi schimbate; vezi rezultatul pe fiecare cont.", changes, states);
        }, cancellationToken).ConfigureAwait(false);
    }

    // The two phases: the new password is saved (protected) as pending, the server is changed with the old connection, a new connection with the new password is checked, and
    // only then the new password becomes the one in use. A stop between the phases is repaired at the next start (RecoverPendingAsync).
    private async Task<DatabaseAccountChange> ChangeOneAsync(DatabaseAccountDefinition account, string newPassword, CancellationToken cancellationToken)
    {
        var user = UserOf(account);
        var current = configuration[account.PasswordKey];
        if (string.IsNullOrEmpty(current)) return new(account.Key, user, false, "Parola actuală nu este configurată; folosește „Resetează conturile”.");
        if (newPassword == current) return new(account.Key, user, false, "Parola nouă este aceeași cu cea actuală.");
        var old = await TryConnectAsync(user, current, withGrants: false, cancellationToken).ConfigureAwait(false);
        if (!old.Connects) return new(account.Key, user, false, $"Contul nu se conectează cu parola actuală ({old.Problem}); folosește „Resetează conturile”.");

        var path = OverridePath;
        var before = DatabaseOverrideFile.ReadRaw(path);
        SaveAccount(path, account, a => a with { PendingPasswordProtected = protector.Protect(newPassword) });
        try
        {
            await using (var connection = Open(user, current, null))
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                await using var command = new MySqlCommand("SET PASSWORD = PASSWORD(@password)", connection);
                command.Parameters.AddWithValue("@password", newPassword);
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            DatabaseOverrideFile.Restore(path, before);
            Reload();
            return new(account.Key, user, false, "Serverul a refuzat schimbarea parolei: " + DatabaseConnectionRules.Describe(exception, Host, Port, Name));
        }
        var verified = await TryConnectAsync(user, newPassword, withGrants: false, cancellationToken).ConfigureAwait(false);
        if (!verified.Connects)
        {
            // The new password does not work: put the old one back on the server (the account is still reachable with the old one if the change did not take effect).
            var revert = await RevertAsync(user, current, newPassword, cancellationToken).ConfigureAwait(false);
            DatabaseOverrideFile.Restore(path, before);
            Reload();
            return new(account.Key, user, false, $"Conexiunea cu parola nouă a eșuat ({verified.Problem}); {revert}");
        }
        SaveAccount(path, account, a => a with { PasswordProtected = protector.Protect(newPassword), PendingPasswordProtected = null, ChangedUtc = time.GetUtcNow().UtcDateTime });
        Reload();
        var final = await TryConnectAsync(user, newPassword, withGrants: false, cancellationToken).ConfigureAwait(false);
        return final.Connects
            ? new(account.Key, user, true, "Parola a fost schimbată și contul se conectează cu ea.")
            : new(account.Key, user, false, $"Parola a fost schimbată pe server, dar aplicația nu se mai conectează ({final.Problem}); folosește „Resetează conturile”.");
    }

    private async Task<string> RevertAsync(string user, string oldPassword, string newPassword, CancellationToken cancellationToken)
    {
        foreach (var password in new[] { newPassword, oldPassword })
        {
            var attempt = await TryConnectAsync(user, password, withGrants: false, cancellationToken).ConfigureAwait(false);
            if (!attempt.Connects) continue;
            if (password == oldPassword) return "parola veche a rămas în vigoare.";
            try
            {
                await using var connection = Open(user, newPassword, null);
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                await using var command = new MySqlCommand("SET PASSWORD = PASSWORD(@password)", connection);
                command.Parameters.AddWithValue("@password", oldPassword);
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                return "s-a revenit la parola veche.";
            }
            catch (Exception exception) when (exception is not OperationCanceledException) { break; }
        }
        return "starea contului pe server este nesigură; folosește „Resetează conturile”.";
    }

    // ---- Reset ------------------------------------------------------------------------------------------------------------------

    public async Task<DatabaseAccountsResult> ResetAsync(IReadOnlyDictionary<string, string>? passwords, bool useDefaults, string administratorPassword, CancellationToken cancellationToken = default)
    {
        await access.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        await new AdministratorReconfirmation(configuration, access, authenticator, logger).ConfirmAsync(administratorPassword, cancellationToken).ConfigureAwait(false);
        var admin = DatabaseAdministrationAccount.Read(configuration) ?? throw new DatabaseSettingsException(NoAdministrationMessage);
        if (passwords is not null)
            foreach (var password in passwords.Values)
                if (DatabaseAccounts.ValidatePassword(password) is { } problem) throw new DatabaseSettingsException(problem);
        return await WithLockAsync("Resetare conturi bază de date", async () =>
        {
            var chosen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var account in accounts)
                chosen[account.Key] = useDefaults ? account.DefaultPassword : passwords?.GetValueOrDefault(account.Key) ?? DatabaseAccounts.GeneratePassword();
            var changes = new List<DatabaseAccountChange>();
            await using (var connection = OpenAdministration(admin))
            {
                try { await connection.OpenAsync(cancellationToken).ConfigureAwait(false); }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    throw new DatabaseSettingsException("Contul de administrare nu s-a putut conecta: " + DatabaseConnectionRules.Describe(exception, admin.Host, admin.Port, Name) + " Verifică datele din admin.private.cnf (folderul de date).");
                }
                foreach (var account in accounts)
                {
                    try { await ApplyAccountAsync(connection, account, chosen[account.Key], cancellationToken).ConfigureAwait(false); changes.Add(new(account.Key, account.User, true, "Contul a fost recreat cu drepturile din catalog.")); }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        changes.Add(new(account.Key, account.User, false, DatabaseConnectionRules.Describe(exception, admin.Host, admin.Port, Name)));
                    }
                }
            }
            // What the server now holds is what the application is told, for the accounts that were put back.
            var path = OverridePath;
            var now = time.GetUtcNow().UtcDateTime;
            foreach (var change in changes.Where(item => item.Success))
            {
                var account = accounts.First(item => item.Key == change.Key);
                SaveAccount(path, account, _ => new AccountOverride(account.User, protector.Protect(chosen[account.Key]), null, now));
            }
            Reload();
            InvalidateDefaultCheck();
            var states = await ReadStatesAsync(cancellationToken).ConfigureAwait(false);
            var ok = changes.All(change => change.Success) && states.All(state => state.Connects && state.RightsMatch);
            await RecordAsync(AuditActions.ResetDatabaseAccounts, "Conturile bazei de date",
                $"Conturi puse la loc: {string.Join(", ", changes.Where(item => item.Success).Select(item => item.User))}; parole {(useDefaults ? "implicite" : passwords is null ? "generate" : "alese")}", cancellationToken).ConfigureAwait(false);
            return new DatabaseAccountsResult(ok, ok ? "Conturile au fost recreate cu drepturile din catalog, iar conexiunea fiecăruia a fost verificată." : "Resetarea nu a reușit pentru toate conturile; vezi rezultatul pe fiecare.", changes, states);
        }, cancellationToken).ConfigureAwait(false);
    }

    // Created when missing, put back to the rights of the catalog (all removed, then granted), the password set, SSL required. The user and the host come from the
    // catalog (checked names), the password and the rest as parameters.
    private static async Task ApplyAccountAsync(MySqlConnection connection, DatabaseAccountDefinition account, string password, CancellationToken cancellationToken)
    {
        if (!DatabaseAccounts.IsSafeUserName(account.User)) throw new InvalidOperationException("Nume de cont nevalid.");
        var identity = $"'{account.User}'@'{DatabaseAccounts.Host}'";
        async Task Run(string sql, string? parameter = null)
        {
            await using var command = new MySqlCommand(sql, connection);
            if (parameter is not null) command.Parameters.AddWithValue("@password", parameter);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await Run($"CREATE USER IF NOT EXISTS {identity} IDENTIFIED BY @password REQUIRE SSL", password).ConfigureAwait(false);
        await Run($"ALTER USER {identity} IDENTIFIED BY @password REQUIRE SSL", password).ConfigureAwait(false);
        await Run($"REVOKE ALL PRIVILEGES, GRANT OPTION FROM {identity}").ConfigureAwait(false);
        foreach (var grant in account.Grants)
        {
            if (!DatabaseAccounts.IsSafeUserName(grant.Database)) throw new InvalidOperationException("Nume de bază nevalid.");
            // A name with an underscore is given with the escaped underscore, so it means that schema and not a pattern.
            await Run($"GRANT {string.Join(", ", grant.Privileges)} ON `{grant.Database.Replace("_", "\\_", StringComparison.Ordinal)}`.* TO {identity}").ConfigureAwait(false);
        }
        await Run("FLUSH PRIVILEGES").ConfigureAwait(false);
    }

    // ---- Export and import ------------------------------------------------------------------------------------------------------

    public async Task<DatabaseConfigurationExport> ExportAsync(string passphrase, bool includeAdministration, string administratorPassword, CancellationToken cancellationToken = default)
    {
        await access.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        await new AdministratorReconfirmation(configuration, access, authenticator, logger).ConfirmAsync(administratorPassword, cancellationToken).ConfigureAwait(false);
        if (DatabaseConfigurationFile.ValidatePassphrase(passphrase) is { } problem) throw new DatabaseSettingsException(problem);
        var parts = new List<DatabaseConfigurationFile.AccountPart>();
        foreach (var account in accounts)
        {
            var password = configuration[account.PasswordKey];
            if (string.IsNullOrEmpty(password)) throw new DatabaseSettingsException($"Contul {account.Purpose.ToLowerInvariant()} nu are parola configurată; folosește „Resetează conturile” înainte de export.");
            var check = await TryConnectAsync(UserOf(account), password, withGrants: false, cancellationToken).ConfigureAwait(false);
            if (!check.Connects) throw new DatabaseSettingsException($"Contul {UserOf(account)} nu se conectează ({check.Problem}); fișierul nu s-a creat.");
            parts.Add(new(account.Key, UserOf(account), account.Purpose, password));
        }
        DatabaseConfigurationFile.AdministrationPart? administration = null;
        if (includeAdministration)
        {
            var admin = DatabaseAdministrationAccount.Read(configuration) ?? throw new DatabaseSettingsException(NoAdministrationMessage);
            var check = await TryConnectAsync(admin.User, admin.Password, withGrants: false, cancellationToken, admin.Host, admin.Port).ConfigureAwait(false);
            if (!check.Connects) throw new DatabaseSettingsException($"Contul de administrare nu se conectează ({check.Problem}); fișierul nu s-a creat. Verifică datele din admin.private.cnf (folderul de date): trebuie să fie cele ale instanței care rulează.");
            administration = new(admin.Host, admin.Port, admin.User, admin.Password);
        }
        var actor = (await RepositoryAudit.ActorAsync(access, cancellationToken).ConfigureAwait(false)).Username;
        var now = time.GetUtcNow().UtcDateTime;
        var content = new DatabaseConfigurationFile.Content(now, actor, new(Host, Port, Name, configuration["Database:SslMode"] ?? "Required"), parts, administration);
        var bytes = DatabaseConfigurationFile.Encrypt(content, passphrase);
        await RecordAsync(AuditActions.ExportDatabaseConfiguration, "Configurarea bazei de date",
            includeAdministration ? "Export cu conturile aplicației și contul de administrare (parole criptate cu fraza-parolă, nimic nu rămâne pe server)" : "Export cu conturile aplicației (parole criptate cu fraza-parolă, nimic nu rămâne pe server)", cancellationToken).ConfigureAwait(false);
        return new(DatabaseConfigurationFile.FileNameFor(now), bytes, includeAdministration);
    }

    public async Task<DatabaseAccountsResult> ImportAsync(byte[] file, string passphrase, string administratorPassword, CancellationToken cancellationToken = default)
    {
        await access.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        await new AdministratorReconfirmation(configuration, access, authenticator, logger).ConfirmAsync(administratorPassword, cancellationToken).ConfigureAwait(false);
        var content = DatabaseConfigurationFile.Decrypt(file, passphrase);
        foreach (var part in content.Accounts)
            if (accounts.All(account => !string.Equals(account.Key, part.Key, StringComparison.OrdinalIgnoreCase)) || !DatabaseAccounts.IsSafeUserName(part.User) || string.IsNullOrEmpty(part.Password))
                throw new DatabaseSettingsException("Fișierul conține un cont care nu face parte din catalog.");
        return await WithLockAsync("Import configurare bază de date", async () =>
        {
            var changes = new List<DatabaseAccountChange>();
            var path = OverridePath;
            var now = time.GetUtcNow().UtcDateTime;
            foreach (var part in content.Accounts)
            {
                var account = accounts.First(item => string.Equals(item.Key, part.Key, StringComparison.OrdinalIgnoreCase));
                var check = await TryConnectAsync(part.User, part.Password, withGrants: false, cancellationToken).ConfigureAwait(false);
                if (!check.Connects) { changes.Add(new(account.Key, part.User, false, $"Contul nu se conectează cu parola din fișier ({check.Problem}); nu s-a importat.")); continue; }
                SaveAccount(path, account, _ => new AccountOverride(part.User, protector.Protect(part.Password), null, now));
                changes.Add(new(account.Key, part.User, true, "Importat; contul se conectează cu parola din fișier."));
            }
            if (content.Administration is { } administration && DatabaseAdministrationAccount.Read(configuration) is null)
            {
                DatabaseAdministrationAccount.Write(configuration, new(administration.Host, administration.Port, administration.User, administration.Password));
                changes.Add(new("administrare", administration.User, true, "Contul de administrare al serverului a fost scris în folderul de date."));
            }
            Reload();
            InvalidateDefaultCheck();
            var states = await ReadStatesAsync(cancellationToken).ConfigureAwait(false);
            var ok = changes.Count > 0 && changes.All(change => change.Success);
            var differs = !string.Equals(content.Server.Host, Host, StringComparison.OrdinalIgnoreCase) || content.Server.Port != Port;
            await RecordAsync(AuditActions.ImportDatabaseConfiguration, "Configurarea bazei de date", $"Conturi importate: {string.Join(", ", changes.Where(item => item.Success).Select(item => item.User))}", cancellationToken).ConfigureAwait(false);
            return new DatabaseAccountsResult(ok, (ok ? "Configurarea a fost importată." : "Importul nu a reușit pentru toate conturile.") +
                (differs ? $" Serverul din fișier ({content.Server.Host}:{content.Server.Port}) este altul decât cel în uz ({Host}:{Port}); host-ul și portul nu s-au schimbat." : ""), changes, states);
        }, cancellationToken).ConfigureAwait(false);
    }

    // ---- A stop in the middle of a change --------------------------------------------------------------------------------------

    public async Task RecoverPendingAsync(CancellationToken cancellationToken = default)
    {
        foreach (var account in accounts)
        {
            var pending = configuration[$"Database:Accounts:{account.Key}:PendingPassword"];
            if (string.IsNullOrEmpty(pending)) continue;
            var user = UserOf(account);
            var current = configuration[account.PasswordKey];
            var currentWorks = !string.IsNullOrEmpty(current) && (await TryConnectAsync(user, current, withGrants: false, cancellationToken).ConfigureAwait(false)).Connects;
            var pendingWorks = !currentWorks && (await TryConnectAsync(user, pending, withGrants: false, cancellationToken).ConfigureAwait(false)).Connects;
            if (!currentWorks && !pendingWorks) continue;   // the server does not answer: nothing is decided now
            SaveAccount(OverridePath, account, a => pendingWorks
                ? a with { PasswordProtected = protector.Protect(pending), PendingPasswordProtected = null, ChangedUtc = time.GetUtcNow().UtcDateTime }
                : a with { PendingPasswordProtected = null });
            Reload();
            logger.LogWarning("The change of the password of the account {Account} had been interrupted; {Outcome}.", account.Key, pendingWorks ? "the new password was put in use" : "the old password was kept");
        }
    }

    // ---- Helpers ------------------------------------------------------------------------------------------------------------------

    private string Host => configuration["Database:Host"] ?? "127.0.0.1";
    private int Port => configuration.GetValue("Database:Port", 3307);
    private string Name => configuration["Database:Name"] ?? "BlazorStoc";

    private string UserOf(DatabaseAccountDefinition account) => configuration[account.UserKey] is { Length: > 0 } user && DatabaseAccounts.IsSafeUserName(user) ? user : account.User;

    private MySqlConnection Open(string user, string password, string? database, string? host = null, int? port = null) => new(new MySqlConnectionStringBuilder
    {
        Server = host ?? Host, Port = (uint)(port ?? Port), UserID = user, Password = password, Database = database ?? "",
        SslMode = Enum.TryParse<MySqlSslMode>(configuration["Database:SslMode"] ?? "Required", true, out var ssl) ? ssl : MySqlSslMode.Required,
        CharacterSet = "utf8mb4", ConnectionTimeout = 10, DefaultCommandTimeout = 20, Pooling = false, AllowLoadLocalInfile = false
    }.ConnectionString);

    private MySqlConnection OpenAdministration(DatabaseAdministrationAccount admin) => new(new MySqlConnectionStringBuilder
    {
        Server = admin.Host, Port = (uint)admin.Port, UserID = admin.User, Password = admin.Password, SslMode = MySqlSslMode.Preferred, CharacterSet = "utf8mb4",
        ConnectionTimeout = 10, DefaultCommandTimeout = 60, Pooling = false, AllowLoadLocalInfile = false
    }.ConnectionString);

    private sealed record Attempt(bool Connects, bool Reachable, string? Problem, IReadOnlyList<string> Grants)
    {
        public void Deconstruct(out bool connects, out string? problem, out IReadOnlyList<string> grants) { connects = Connects; problem = Problem; grants = Grants; }
    }

    // Outside the gate of the application (a check of the accounts must work while the application is in maintenance too) and without a pool.
    private async Task<Attempt> TryConnectAsync(string user, string password, bool withGrants, CancellationToken cancellationToken, string? host = null, int? port = null)
    {
        try
        {
            await using var connection = Open(user, password, null, host, port);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            var grants = new List<string>();
            if (withGrants)
            {
                await using var command = new MySqlCommand("SHOW GRANTS FOR CURRENT_USER()", connection);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) grants.Add(reader.GetString(0));
            }
            return new(true, true, null, grants);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var reachable = exception is MySqlException { Number: 1045 or 1044 or 1130 or 1142 or 1862 };
            return new(false, reachable, DatabaseConnectionRules.Describe(exception, host ?? Host, port ?? Port, Name), []);
        }
    }

    private void SaveAccount(string path, DatabaseAccountDefinition account, Func<AccountOverride, AccountOverride> change)
    {
        var data = DatabaseOverrideFile.Read(path) ?? new DatabaseOverrideData(null, null, null, null, null, null, null);
        var map = new Dictionary<string, AccountOverride>(data.Accounts ?? new Dictionary<string, AccountOverride>(), StringComparer.OrdinalIgnoreCase);
        map[account.Key] = change(map.GetValueOrDefault(account.Key) ?? new AccountOverride());
        DatabaseOverrideFile.Write(path, data with { Accounts = map });
        Reload();
    }

    private void Reload()
    {
        (configuration as IConfigurationRoot)?.Reload();
        MySqlConnection.ClearAllPools();
    }

    // One operation on the accounts at a time; refused while a backup or a restoration runs; the other sessions wait while the passwords change.
    private async Task<DatabaseAccountsResult> WithLockAsync(string operation, Func<Task<DatabaseAccountsResult>> work, CancellationToken cancellationToken)
    {
        if (!await OneAtATime.WaitAsync(0, cancellationToken).ConfigureAwait(false)) throw new DatabaseSettingsException(BusyMessage);
        try
        {
            if (await locks.GetActiveAsync(cancellationToken).ConfigureAwait(false) is not null) throw new DatabaseSettingsException(BusyMessage);
            var actor = (await RepositoryAudit.ActorAsync(access, cancellationToken).ConfigureAwait(false)).Username;
            var handle = await locks.TryAcquireAsync(operation, actor, AccessRoles.Administrator, cancellationToken).ConfigureAwait(false) ?? throw new DatabaseSettingsException(BusyMessage);
            try
            {
                using (MaintenanceGate.EnterOwnerScope()) return await work().ConfigureAwait(false);
            }
            finally { await handle.DisposeAsync().ConfigureAwait(false); }
        }
        finally { OneAtATime.Release(); }
    }

    private async Task RecordAsync(string action, string target, string details, CancellationToken cancellationToken)
    {
        try { await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.Database, action, "", target, details, "", cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("Journal entry '{Action}' could not be written ({ErrorType}): {Details}", action, exception.GetType().Name, details);
        }
    }
}
