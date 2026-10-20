using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BlazorStoc.Services;

// The accounts of the database server that belong to the application: fixed names, fixed purposes, fixed rights. The catalog is a data file shared with the scripts
// (Assets/Data/database-accounts.json); Setari -> Baza de date can only change their passwords, never add, rename or delete an account or change what it may do.

public sealed record AccountGrant(string Database, IReadOnlyList<string> Privileges);

/// <summary>One predefined account. <see cref="ConfigPrefix"/> says under which configuration keys the application reads it ("" = Database:User / Database:Password,
/// "Backup" = Database:BackupUser / Database:BackupPassword, ...).</summary>
public sealed record DatabaseAccountDefinition(string Key, string User, string LegacyUser, string ConfigPrefix, string Purpose, string DefaultPassword, IReadOnlyList<AccountGrant> Grants)
{
    public string UserKey => $"Database:{ConfigPrefix}User";
    public string PasswordKey => $"Database:{ConfigPrefix}Password";
    public bool IsApplication => ConfigPrefix.Length == 0;
}

public static partial class DatabaseAccounts
{
    public const string ApplicationKey = "app";

    private static readonly Lazy<(string Host, IReadOnlyList<DatabaseAccountDefinition> Accounts, IReadOnlySet<string> Development)> Loaded = new(Load);

    /// <summary>The host the accounts are bound to on the server (a connection from this address only), 127.0.0.1 unless the catalog says otherwise.</summary>
    public static string Host => Loaded.Value.Host;

    public static IReadOnlyList<DatabaseAccountDefinition> All => Loaded.Value.Accounts;

    /// <summary>The databases of development and tests (blazorstoc_test, blazorstoc_demo): rights on them are not reported as being in excess, because the scripts of a development machine give them.</summary>
    public static IReadOnlySet<string> DevelopmentDatabases => Loaded.Value.Development;

    public static DatabaseAccountDefinition? Find(string key) => All.FirstOrDefault(account => string.Equals(account.Key, key, StringComparison.OrdinalIgnoreCase));

    private static (string, IReadOnlyList<DatabaseAccountDefinition>, IReadOnlySet<string>) Load()
    {
        using var document = JsonDocument.Parse(EmbeddedData.ReadText("database-accounts.json"));
        var root = document.RootElement;
        var accounts = root.GetProperty("accounts").EnumerateArray().Select(item => new DatabaseAccountDefinition(
            item.GetProperty("key").GetString()!, item.GetProperty("user").GetString()!, item.GetProperty("legacyUser").GetString()!, item.GetProperty("configPrefix").GetString()!,
            item.GetProperty("purpose").GetString()!, item.GetProperty("defaultPassword").GetString()!,
            [.. item.GetProperty("grants").EnumerateArray().Select(grant => new AccountGrant(grant.GetProperty("database").GetString()!,
                [.. grant.GetProperty("privileges").EnumerateArray().Select(privilege => privilege.GetString()!)]))])).ToList();
        foreach (var account in accounts)
            if (!NamePattern().IsMatch(account.User) || !NamePattern().IsMatch(account.LegacyUser))
                throw new InvalidOperationException($"Numele contului {account.Key} din catalog nu este valid.");
        var development = root.TryGetProperty("developmentDatabases", out var list)
            ? list.EnumerateArray().Select(item => item.GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase) : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return (root.TryGetProperty("host", out var host) ? host.GetString()! : "127.0.0.1", accounts, development);
    }

    [GeneratedRegex(@"^[A-Za-z0-9_]{1,80}$")]
    private static partial Regex NamePattern();

    /// <summary>The user name is put in SQL text (it cannot be a parameter), so only what the catalog names, or a legacy name of the same shape, is ever used.</summary>
    public static bool IsSafeUserName(string? name) => name is not null && NamePattern().IsMatch(name);

    // ---- Passwords -----------------------------------------------------------------------------------------------------------

    public const int MinimumPasswordLength = 16;
    public const int MaximumPasswordLength = 100;
    public const string TooShortMessage = "Parola trebuie să aibă cel puțin 16 caractere.";
    public const string TooLongMessage = "Parola poate avea cel mult 100 de caractere.";
    public const string ControlCharactersMessage = "Parola nu poate conține caractere de control.";

    public static string? ValidatePassword(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinimumPasswordLength) return TooShortMessage;
        if (password.Length > MaximumPasswordLength) return TooLongMessage;
        if (password.Any(char.IsControl)) return ControlCharactersMessage;
        return null;
    }

    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789-_.";

    /// <summary>A random password (24 characters, no look-alike letters, nothing a shell or SQL would treat specially).</summary>
    public static string GeneratePassword(int length = 24)
    {
        var chars = new char[length];
        for (var i = 0; i < length; i++) chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return new string(chars);
    }

    // ---- The rights of an account, read from SHOW GRANTS --------------------------------------------------------------------

    /// <summary>What <c>SHOW GRANTS FOR CURRENT_USER()</c> says, compared with the catalog: the rights that are missing and the ones that are in excess, per database.</summary>
    public sealed record GrantComparison(IReadOnlyList<string> Missing, IReadOnlyList<string> Extra)
    {
        public bool Matches => Missing.Count == 0 && Extra.Count == 0;
    }

    public static GrantComparison CompareGrants(DatabaseAccountDefinition account, IEnumerable<string> grantLines)
    {
        var actual = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var tableLevel = new List<(string Database, string Table, string Privilege)>();
        foreach (var line in grantLines)
        {
            var match = GrantLine().Match(line);
            if (!match.Success) continue;
            var target = GrantTarget().Match(match.Groups["db"].Value);
            if (!target.Success) continue;
            var database = Unquote(target.Groups["db"].Value);
            var table = target.Groups["tbl"].Value == "*" ? "*" : Unquote(target.Groups["tbl"].Value);
            var privileges = match.Groups["privs"].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(item => item.ToUpperInvariant()).ToList();
            if (database == "*")
            {
                var global = privileges.Where(item => item != "USAGE").ToList();
                if (global.Count > 0) AddAll(actual, "*.*", global);
            }
            else if (table == "*") AddAll(actual, database, privileges);
            else foreach (var privilege in privileges) tableLevel.Add((database, table, privilege));
        }
        var missing = new List<string>();
        var extra = new List<string>();
        foreach (var grant in account.Grants)
        {
            var wanted = Expand(grant.Privileges);
            actual.TryGetValue(grant.Database, out var have);
            have = have is null ? [] : Expand(have);
            foreach (var privilege in wanted.Where(item => !have.Contains(item))) missing.Add($"{privilege} pe {grant.Database}");
            foreach (var privilege in have.Where(item => !wanted.Contains(item))) extra.Add($"{privilege} pe {grant.Database}");
        }
        var known = account.Grants.ToDictionary(grant => grant.Database, grant => Expand(grant.Privileges), StringComparer.OrdinalIgnoreCase);
        // A right on one table of a schema the account has rights on is what a migration gives to a table it creates: fine when the catalog gives that right on the schema.
        foreach (var (database, table, privilege) in tableLevel)
        {
            if (DevelopmentDatabases.Contains(database)) continue;
            if (known.TryGetValue(database, out var allowed) && (allowed.Contains(privilege) || allowed.Contains("ALL PRIVILEGES"))) continue;
            extra.Add($"{privilege} pe {database}.{table}");
        }
        foreach (var (database, privileges) in actual.Where(pair => !known.ContainsKey(pair.Key) && !DevelopmentDatabases.Contains(pair.Key)))
            foreach (var privilege in Expand(privileges)) extra.Add($"{privilege} pe {database}");
        return new(missing, extra);
    }

    private static void AddAll(Dictionary<string, HashSet<string>> map, string database, IEnumerable<string> privileges)
    {
        if (!map.TryGetValue(database, out var set)) map[database] = set = new(StringComparer.OrdinalIgnoreCase);
        foreach (var privilege in privileges) set.Add(privilege.Trim());
    }

    // ALL PRIVILEGES stands for every privilege of a schema: compared as such, not expanded into a list that would change with the server version.
    private static HashSet<string> Expand(IEnumerable<string> privileges)
    {
        var set = new HashSet<string>(privileges.Select(item => item.Trim().ToUpperInvariant()), StringComparer.OrdinalIgnoreCase);
        if (set.Contains("ALL")) { set.Remove("ALL"); set.Add("ALL PRIVILEGES"); }
        return set;
    }

    // `BlazorStoc\_bak` -> BlazorStoc_bak (the underscore of a schema name is escaped in a grant); `*` stays *.
    private static string Unquote(string text) => text == "*" ? "*" : text.Trim('`').Replace("\\_", "_", StringComparison.Ordinal).Replace("\\%", "%", StringComparison.Ordinal);

    [GeneratedRegex(@"^(?<db>`[^`]*`|\*|[^.`]+)\.(?<tbl>`[^`]*`|\*)$")]
    private static partial Regex GrantTarget();

    [GeneratedRegex(@"^GRANT\s+(?<privs>.+?)\s+ON\s+(?<db>\S+)\s+TO\s", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex GrantLine();
}
