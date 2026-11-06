namespace BlazorStoc.Services;

public sealed record AccessSecuritySaveResult(bool Success, string Message);

/// <summary>One account in Setari -> Autentificare: the account and what is known about its authenticator. The administrator of the configuration has Id 0.</summary>
public sealed record AuthenticatorRow(int Id, string Username, string DisplayName, string Role, bool AccountActive, TwoFactorStatus Status)
{
    public bool IsAdministrator => Role == AccessRoles.Administrator;
}

public interface IAccessSecuritySettingsService
{
    Task<AccessSecurityOptions> GetAsync(CancellationToken cancellationToken = default);
    Task<AccessSecuritySaveResult> SaveAsync(AccessSecurityOptions options, string administratorPassword, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AuthenticatorRow>> GetOverviewAsync(CancellationToken cancellationToken = default);
    Task<AuthenticatorRow?> GetAsync(int userId, CancellationToken cancellationToken = default);
    /// <summary>True when this request may change the authenticator of the account: any account but an administrator's, which only changes from the server itself (localhost).</summary>
    bool MayManage(AuthenticatorRow row);
    /// <summary>Starts (or shows again) the enrollment of an account: a secret, pending until a first valid code confirms it.</summary>
    Task<string> ProvisionAsync(int userId, string administratorPassword, CancellationToken cancellationToken = default);
    Task<string?> GetPendingSecretAsync(int userId, CancellationToken cancellationToken = default);
    /// <summary>Confirms the pending secret with the first code read on the phone; returns the recovery codes (shown once) or null when the code is wrong.</summary>
    Task<IReadOnlyList<string>?> ConfirmAsync(int userId, string code, CancellationToken cancellationToken = default);
    Task RevokeAsync(int userId, string administratorPassword, CancellationToken cancellationToken = default);
    Task SetSuspendedAsync(int userId, bool suspended, string administratorPassword, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> RegenerateRecoveryCodesAsync(int userId, string administratorPassword, CancellationToken cancellationToken = default);
    /// <summary>Changing the phone: a new secret (base 32) next to the old one, which keeps working until <see cref="ConfirmReplacementAsync"/> gets a valid code from the new phone.</summary>
    Task<string> BeginReplacementAsync(int userId, string administratorPassword, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>?> ConfirmReplacementAsync(int userId, string code, CancellationToken cancellationToken = default);
    Task CancelReplacementAsync(int userId, CancellationToken cancellationToken = default);
    Task UnlockAsync(int userId, CancellationToken cancellationToken = default);
}

/// <summary>Setari -> Autentificare. The mode and the internal network are saved in a small JSON file (a configuration source) and apply at the next sign-in; the authenticators are kept in the database.</summary>
public sealed class AccessSecuritySettingsService(IConfiguration configuration, IAccessControl access, IAuditTrail audit, ITwoFactorStore store, IUserRepository users,
    IHttpContextAccessor httpAccessor, ILogger<AccessSecuritySettingsService> logger, IUserAuthenticator? authenticator = null) : IAccessSecuritySettingsService
{
    public const string ServerOnlyMessage = "Autentificatorul unui cont de administrator se modifică doar de pe server (localhost), nu de la distanță.";

    public async Task<AccessSecurityOptions> GetAsync(CancellationToken cancellationToken = default)
    {
        await access.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        return AccessSecurityRules.Read(configuration);
    }

    public async Task<IReadOnlyList<AuthenticatorRow>> GetOverviewAsync(CancellationToken cancellationToken = default)
    {
        await access.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var statuses = await store.GetStatusesAsync(cancellationToken).ConfigureAwait(false);
        TwoFactorStatus StatusOf(int id) => statuses.TryGetValue(id, out var status) ? status : new(false, false, LockedUntil: null);
        var rows = new List<AuthenticatorRow> { new(0, configuration["Authentication:Username"] ?? "admin", "Administrator configurat", AccessRoles.Administrator, true, StatusOf(0)) };
        foreach (var user in await users.GetUsersAsync(cancellationToken).ConfigureAwait(false))
            rows.Add(new(user.Id, user.Username, user.DisplayName, user.Role, user.IsActive, StatusOf(user.Id)));
        return rows;
    }

    public async Task<AuthenticatorRow?> GetAsync(int userId, CancellationToken cancellationToken = default) =>
        (await GetOverviewAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(row => row.Id == userId);

    public bool MayManage(AuthenticatorRow row) => !row.IsAdministrator || IsServerLocal();

    private bool IsServerLocal() => httpAccessor.HttpContext is { } context && AccessSecurityRules.IsServerLocal(context.Connection.RemoteIpAddress, context.Request.Headers);

    public async Task<AccessSecuritySaveResult> SaveAsync(AccessSecurityOptions options, string administratorPassword, CancellationToken cancellationToken = default)
    {
        await access.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        await new AdministratorReconfirmation(configuration, access, authenticator, logger).ConfirmAsync(administratorPassword, cancellationToken).ConfigureAwait(false);
        var normalized = new AccessSecurityOptions(options.Mode,
            options.InternalNetworks.Select(item => item.Trim()).Where(item => item.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        if (AccessSecurityRules.Validate(normalized) is { } problem) throw new DatabaseSettingsException(problem);
        var current = AccessSecurityRules.Read(configuration);
        if (current.Mode == normalized.Mode && current.InternalNetworks.SequenceEqual(normalized.InternalNetworks, StringComparer.OrdinalIgnoreCase))
            return new(true, "Nu există modificări de salvat.");
        // A code for everybody is only switched on when an administrator can still sign in with one: otherwise nobody could get in (the way back is the command on the server).
        if (normalized.Mode == TwoFactorMode.Everyone && !(await GetOverviewAsync(cancellationToken).ConfigureAwait(false)).Any(row => row.IsAdministrator && row.AccountActive && row.Status.Active))
            throw new DatabaseSettingsException("Codul obligatoriu pentru toți se poate porni doar după ce cel puțin un administrator activ are autentificator confirmat. Asociază întâi un autentificator unui administrator (de pe server).");
        try
        {
            AccessSecurityRules.Write(AccessSecurityRules.PathFor(configuration), normalized);
            (configuration as IConfigurationRoot)?.Reload();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogError("The access security settings could not be saved ({ErrorType}).", exception.GetType().Name);
            throw new DatabaseSettingsException("Setările nu au putut fi salvate. Verifică drepturile asupra folderului de configurare.");
        }
        await TryRecordAsync(AuditEntities.Database, AuditActions.SetAccessSecurity, "", "Securitate acces extern", AuditDetails.Changes(
            new AuditChange("Cod din aplicația de autentificare", ModeText(current.Mode), ModeText(normalized.Mode)),
            new AuditChange("Rețea internă", Show(current.InternalNetworks), Show(normalized.InternalNetworks))), cancellationToken).ConfigureAwait(false);
        return new(true, "Setările au fost salvate și se aplică de la următoarea autentificare.");
    }

    private async Task<AuthenticatorRow> RequireAsync(int userId, CancellationToken cancellationToken)
    {
        var row = await GetAsync(userId, cancellationToken).ConfigureAwait(false) ?? throw new DatabaseSettingsException("Utilizatorul nu mai există. Actualizează lista.");
        if (!MayManage(row)) throw new DatabaseSettingsException(ServerOnlyMessage);
        return row;
    }

    private Task ReconfirmAsync(string administratorPassword, CancellationToken cancellationToken) =>
        new AdministratorReconfirmation(configuration, access, authenticator, logger).ConfirmAsync(administratorPassword, cancellationToken);

    public async Task<string> ProvisionAsync(int userId, string administratorPassword, CancellationToken cancellationToken = default)
    {
        await access.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var row = await RequireAsync(userId, cancellationToken).ConfigureAwait(false);
        if (!row.AccountActive) throw new DatabaseSettingsException("Contul este inactiv: reactivează-l înainte să îi asociezi un autentificator.");
        if (row.Status.Confirmed) throw new DatabaseSettingsException("Utilizatorul are deja un autentificator activ. Revocă-l întâi dacă vrei să asociezi altul.");
        await ReconfirmAsync(administratorPassword, cancellationToken).ConfigureAwait(false);
        var by = await access.GetUsernameAsync(cancellationToken).ConfigureAwait(false) ?? "necunoscut";
        var secret = await store.ProvisionAsync(userId, by, cancellationToken).ConfigureAwait(false) ?? throw new DatabaseSettingsException("Utilizatorul are deja un autentificator activ.");
        await TryRecordAsync(AuditEntities.User, AuditActions.ProvisionTwoFactor, userId.ToString(), row.Username,
            "A fost generat un secret nou pentru autentificator; devine activ după confirmarea cu un cod valid.", cancellationToken).ConfigureAwait(false);
        return secret;
    }

    public async Task<string?> GetPendingSecretAsync(int userId, CancellationToken cancellationToken = default)
    {
        await access.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        await RequireAsync(userId, cancellationToken).ConfigureAwait(false);
        return await store.GetPendingSecretAsync(userId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>?> ConfirmAsync(int userId, string code, CancellationToken cancellationToken = default)
    {
        await access.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var row = await RequireAsync(userId, cancellationToken).ConfigureAwait(false);
        var by = await access.GetUsernameAsync(cancellationToken).ConfigureAwait(false) ?? "necunoscut";
        var codes = await store.ConfirmAsync(userId, code, by, cancellationToken).ConfigureAwait(false);
        if (codes is null) return null;
        await TryRecordAsync(AuditEntities.User, AuditActions.ConfirmTwoFactor, userId.ToString(), row.Username,
            "Autentificatorul a fost confirmat cu un cod valid citit de pe telefonul utilizatorului; au fost generate coduri de recuperare.", cancellationToken).ConfigureAwait(false);
        return codes;
    }

    public async Task RevokeAsync(int userId, string administratorPassword, CancellationToken cancellationToken = default)
    {
        await access.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var row = await RequireAsync(userId, cancellationToken).ConfigureAwait(false);
        await ReconfirmAsync(administratorPassword, cancellationToken).ConfigureAwait(false);
        if (!await store.RevokeAsync(userId, cancellationToken).ConfigureAwait(false)) return;
        await TryRecordAsync(AuditEntities.User, AuditActions.RevokeTwoFactor, userId.ToString(), row.Username,
            "Autentificatorul și codurile de recuperare au fost șterse; utilizatorul nu mai poate intra cu cod până nu i se asociază un autentificator nou.", cancellationToken).ConfigureAwait(false);
    }

    public async Task SetSuspendedAsync(int userId, bool suspended, string administratorPassword, CancellationToken cancellationToken = default)
    {
        await access.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var row = await RequireAsync(userId, cancellationToken).ConfigureAwait(false);
        if (!suspended && !row.AccountActive) throw new DatabaseSettingsException("Contul este inactiv: reactivează-l înainte să reactivezi autentificatorul.");
        await ReconfirmAsync(administratorPassword, cancellationToken).ConfigureAwait(false);
        if (!await store.SetSuspendedAsync(userId, suspended, cancellationToken).ConfigureAwait(false)) return;
        await TryRecordAsync(AuditEntities.User, suspended ? AuditActions.SuspendTwoFactor : AuditActions.ResumeTwoFactor, userId.ToString(), row.Username,
            suspended ? "Autentificatorul a fost suspendat de administrator." : "Autentificatorul existent a fost reactivat (același telefon).", cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> RegenerateRecoveryCodesAsync(int userId, string administratorPassword, CancellationToken cancellationToken = default)
    {
        await access.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var row = await RequireAsync(userId, cancellationToken).ConfigureAwait(false);
        await ReconfirmAsync(administratorPassword, cancellationToken).ConfigureAwait(false);
        var codes = await store.RegenerateRecoveryCodesAsync(userId, cancellationToken).ConfigureAwait(false) ?? throw new DatabaseSettingsException("Utilizatorul nu are un autentificator confirmat.");
        await TryRecordAsync(AuditEntities.User, AuditActions.RegenerateRecoveryCodes, userId.ToString(), row.Username,
            "Codurile de recuperare vechi au fost înlocuite cu 10 coduri noi.", cancellationToken).ConfigureAwait(false);
        return codes;
    }

    public async Task<string> BeginReplacementAsync(int userId, string administratorPassword, CancellationToken cancellationToken = default)
    {
        await access.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var row = await RequireAsync(userId, cancellationToken).ConfigureAwait(false);
        if (!row.Status.Active) throw new DatabaseSettingsException("Schimbarea telefonului se face pe un autentificator activ.");
        await ReconfirmAsync(administratorPassword, cancellationToken).ConfigureAwait(false);
        return await store.BeginReplacementAsync(userId, cancellationToken).ConfigureAwait(false) ?? throw new DatabaseSettingsException("Utilizatorul nu are un autentificator confirmat.");
    }

    public async Task<IReadOnlyList<string>?> ConfirmReplacementAsync(int userId, string code, CancellationToken cancellationToken = default)
    {
        await access.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var row = await RequireAsync(userId, cancellationToken).ConfigureAwait(false);
        var by = await access.GetUsernameAsync(cancellationToken).ConfigureAwait(false) ?? "necunoscut";
        var codes = await store.ConfirmReplacementAsync(userId, code, by, cancellationToken).ConfigureAwait(false);
        if (codes is null) return null;
        await TryRecordAsync(AuditEntities.User, AuditActions.ReplaceTwoFactor, userId.ToString(), row.Username,
            "Secretul autentificatorului a fost înlocuit după confirmarea cu un cod valid de pe telefonul nou; secretul vechi și codurile de recuperare vechi nu mai funcționează.", cancellationToken).ConfigureAwait(false);
        return codes;
    }

    public async Task CancelReplacementAsync(int userId, CancellationToken cancellationToken = default)
    {
        await access.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        await RequireAsync(userId, cancellationToken).ConfigureAwait(false);
        await store.CancelReplacementAsync(userId, cancellationToken).ConfigureAwait(false);
    }

    public async Task UnlockAsync(int userId, CancellationToken cancellationToken = default)
    {
        await access.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var row = await RequireAsync(userId, cancellationToken).ConfigureAwait(false);
        store.Unlock(userId);
        await TryRecordAsync(AuditEntities.User, AuditActions.UnlockTwoFactor, userId.ToString(), row.Username, "Blocarea verificării codului a fost ridicată.", cancellationToken).ConfigureAwait(false);
    }

    private async Task TryRecordAsync(string entity, string action, string id, string target, string details, CancellationToken cancellationToken)
    {
        try { await AuditRecorder.RecordActionAsync(audit, access, entity, action, id, target, details, "", cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("Journal entry '{Action}' could not be written ({ErrorType}).", action, exception.GetType().Name);
        }
    }

    public static string ModeText(TwoFactorMode mode) => mode switch { TwoFactorMode.Everyone => "obligatoriu pentru toți", TwoFactorMode.External => "doar din exterior", _ => "oprit" };
    private static string Show(IReadOnlyList<string> list) => list.Count == 0 ? "implicit (adrese private)" : string.Join(", ", list);
}
