using Microsoft.AspNetCore.Identity;

namespace BlazorStoc.Services;

public sealed class DemoUserStore
{
    internal sealed record Account(WebUser User, string PasswordHash);
    internal readonly object Gate = new();
    internal readonly List<Account> Accounts;
    internal int NextId = 3;

    public DemoUserStore()
    {
        Accounts =
        [
            new(new WebUser(1, "administrator.demo", "Administrator demonstrație", AccessRoles.Administrator, true, 0), DemoUserRepository.HashPassword("admin-demo-123")),
            new(new WebUser(2, "utilizator.demo", "Utilizator demonstrație", AccessRoles.LimitedUser, true, 0), DemoUserRepository.HashPassword("utilizator-demo-123"))
        ];
    }
}

public sealed class DemoUserRepository(IAccessControl? accessControl = null, DemoUserStore? sharedStore = null,
    IAuditTrail? auditTrail = null, IArchiveService? archiveService = null) : IUserRepository, IUserAuthenticator
{
    private readonly IArchiveService archiver = archiveService ?? new ArchiveService(accessControl);
    private static readonly object PasswordSubject = new();
    private static readonly PasswordHasher<object> PasswordHasher = new();
    private readonly DemoUserStore store = sharedStore ?? new DemoUserStore();

    internal static string HashPassword(string password) => PasswordHasher.HashPassword(PasswordSubject, password);

    public Task<AuthenticationResult> AuthenticateAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        username = TextNormalization.ForStorage(username);
        password ??= "";
        lock (store.Gate)
        {
            var account = store.Accounts.SingleOrDefault(item => string.Equals(item.User.Username, username, StringComparison.OrdinalIgnoreCase));
            if (account is null || PasswordHasher.VerifyHashedPassword(PasswordSubject, account.PasswordHash, password) == PasswordVerificationResult.Failed)
                return Task.FromResult(new AuthenticationResult(AuthenticationStatus.InvalidCredentials));
            if (!account.User.IsActive)
                return Task.FromResult(new AuthenticationResult(AuthenticationStatus.Inactive));
            var user = account.User;
            return Task.FromResult(new AuthenticationResult(AuthenticationStatus.Success, new(user.Id, user.Username, user.DisplayName, user.Role)));
        }
    }

    public async Task<IReadOnlyList<WebUser>> GetUsersAsync(CancellationToken cancellationToken = default)
    {
        await EnsureAdministratorAsync(cancellationToken);
        lock (store.Gate) return store.Accounts.Select(account => account.User).OrderBy(user => user.Username).ToArray();
    }

    public async Task<WebUser> CreateAsync(WebUserInput input, CancellationToken cancellationToken = default)
    {
        await EnsureAdministratorAsync(cancellationToken);
        var value = input.Validated(true);
        var passwordHash = HashPassword(value.Password);
        WebUser user;
        lock (store.Gate)
        {
            EnsureUniqueUsername(value.Username, null);
            user = new WebUser(store.NextId++, value.Username, value.DisplayName, value.Role, true, 0);
            store.Accounts.Add(new(user, passwordHash));
        }
        await AuditRecorder.RecordCreateAsync(auditTrail, accessControl, AuditEntities.User, user.Id.ToString(),
            $"#{user.Id} · {user.Username}", AuditDetails.Identification(
                ("Nume utilizator", user.Username), ("Nume afișat", user.DisplayName),
                ("Rol", user.Role), ("Stare", "activ")), cancellationToken);
        return user;
    }

    public async Task<WebUser> UpdateAsync(WebUser original, WebUserInput input, CancellationToken cancellationToken = default)
    {
        await EnsureAdministratorAsync(cancellationToken);
        var value = input.Validated(false);
        var currentUsername = await GetCurrentUsernameAsync(cancellationToken);
        var passwordHash = value.Password.Length == 0 ? null : HashPassword(value.Password);
        WebUser updated;
        lock (store.Gate)
        {
            var index = store.Accounts.FindIndex(account => account.User.Id == original.Id);
            var current = index < 0 ? null : store.Accounts[index].User;
            WebUserRules.CheckCurrent(current, original);
            EnsureUniqueUsername(value.Username, original.Id);
            EnsureSelfRemainsAdministrator(original, value, currentUsername);
            EnsureLastAdministratorRemains(original, value.Role, value.IsActive);
            updated = new WebUser(original.Id, value.Username, value.DisplayName, value.Role, value.IsActive, checked(original.Version + 1));
            store.Accounts[index] = new(updated, passwordHash ?? store.Accounts[index].PasswordHash);
        }
        await AuditRecorder.RecordEditAsync(auditTrail, accessControl, AuditEntities.User, updated.Id.ToString(),
            $"#{updated.Id} · {updated.Username}", UserAuditChanges(original, updated), value.Reason, cancellationToken);
        return updated;
    }

    public async Task DeleteAsync(WebUser original, string reason, CancellationToken cancellationToken = default)
    {
        await EnsureAdministratorAsync(cancellationToken);
        var motif = ChangeReasonRules.Normalize(reason);
        if (ChangeReasonRules.ValidationError(motif) is { } reasonError)
            throw new UserOperationException(reasonError);
        var currentUsername = await GetCurrentUsernameAsync(cancellationToken);
        string passwordHash;
        lock (store.Gate)
            passwordHash = store.Accounts.SingleOrDefault(account => account.User.Id == original.Id)?.PasswordHash
                ?? throw new UserOperationException("Utilizatorul nu mai există. Actualizează lista.");
        await archiver.ExecuteAsync(ArchiveRequests.User(original, motif,
            [new ArchiveProtectedValue("PasswordHash", passwordHash)]), async (operation, token) =>
        {
            lock (store.Gate)
            {
                var current = store.Accounts.SingleOrDefault(account => account.User.Id == original.Id)?.User;
                WebUserRules.CheckCurrent(current, original);
                if (string.Equals(original.Username, currentUsername, StringComparison.OrdinalIgnoreCase))
                    throw new UserOperationException("Nu îți poți șterge propriul cont.");
                EnsureLastAdministratorRemains(original, AccessRoles.LimitedUser, false);
                store.Accounts.RemoveAll(account => account.User.Id == original.Id);
            }
            await AuditRecorder.RecordDeleteAsync(auditTrail, operation, token);
        }, cancellationToken);
    }

    private static AuditChange[] UserAuditChanges(WebUser before, WebUser after) =>
    [
        new("Nume utilizator", before.Username, after.Username),
        new("Nume afișat", before.DisplayName, after.DisplayName),
        new("Rol", before.Role, after.Role),
        new("Stare", before.IsActive ? "activ" : "inactiv", after.IsActive ? "activ" : "inactiv")
    ];

    private Task EnsureAdministratorAsync(CancellationToken token) => accessControl?.EnsureAdministratorAsync(token) ?? Task.CompletedTask;
    private Task<string?> GetCurrentUsernameAsync(CancellationToken token) => accessControl?.GetUsernameAsync(token) ?? Task.FromResult<string?>(null);
    private void EnsureUniqueUsername(string username, int? excludedId)
    {
        if (store.Accounts.Any(account => account.User.Id != excludedId && TextNormalization.SameUniqueValue(account.User.Username, username)))
            throw new UserOperationException("Există deja un utilizator cu acest nume.");
    }
    private static void EnsureSelfRemainsAdministrator(WebUser original, WebUserInput value, string? currentUsername)
    {
        if (string.Equals(original.Username, currentUsername, StringComparison.OrdinalIgnoreCase) && (!value.IsActive || value.Role != AccessRoles.Administrator))
            throw new UserOperationException("Nu îți poți dezactiva propriul cont și nu îți poți elimina drepturile de administrator.");
    }
    private void EnsureLastAdministratorRemains(WebUser original, string newRole, bool newActive)
    {
        if (original.Role == AccessRoles.Administrator && original.IsActive &&
            (newRole != AccessRoles.Administrator || !newActive) &&
            store.Accounts.Count(account => account.User.Id != original.Id && account.User.Role == AccessRoles.Administrator && account.User.IsActive) == 0)
            throw new UserOperationException("Sistemul trebuie să păstreze cel puțin un administrator activ.");
    }
}
