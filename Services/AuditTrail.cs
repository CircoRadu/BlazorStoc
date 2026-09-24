using System.Text;
using System.Text.Json;

namespace BlazorStoc.Services;

public static class AuditEntities
{
    public const string Product = "Produs";
    public const string User = "Utilizator";
    public const string Beneficiary = "Beneficiar";
    public const string Category = "Categorie";
    public const string Subcategory = "Subcategorie";
    public const string Project = "Proiect";
    public const string ProjectObservation = "Observatie";
    public const string ProjectObservationFile = "FisierObservatie";
}

public static class AuditActions
{
    public const string Create = "Adăugare";
    public const string Edit = "Editare";
    public const string Delete = "Ștergere";
    public const string Login = "Conectare";
    public const string Logout = "Deconectare";

    public static string Normalize(string? action) =>
        string.Equals(action, "Modificare", StringComparison.OrdinalIgnoreCase) ? Edit : action ?? string.Empty;
}

public sealed record AuditChange(string Field, string Before, string After);

public static class AuditDetails
{
    public static string Changes(params AuditChange[] changes) => string.Join("; ", changes
        .Where(change => !string.Equals(change.Before, change.After, StringComparison.Ordinal))
        .Select(change => $"{change.Field}: {change.Before} → {change.After}"));

    public static string Identification(params (string Field, string Value)[] values) =>
        string.Join("; ", values.Select(value => $"{value.Field}: {value.Value}"));
}

public sealed record AuditEvent(Guid Id, DateTime TimestampUtc, string ActorUsername, string ActorRole,
    string EntityType, string Action, string Target, string Details, string Motif = "", string EntityId = "",
    Guid? ArchiveOperationId = null);

public sealed record AuditWrite(string ActorUsername, string ActorRole, string EntityType, string Action,
    string Target, string Details, string Motif = "", string EntityId = "", Guid? ArchiveOperationId = null);

public static class AuditNavigation
{
    // Older product events stored "#<id> · <code>"; the internal identifier is hidden when they are displayed.
    public static string DisplayTarget(AuditEvent entry)
    {
        if (entry.EntityType != AuditEntities.Product || !entry.Target.StartsWith('#')) return entry.Target;
        var separator = entry.Target.IndexOf(" · ", StringComparison.Ordinal);
        return separator > 1 && entry.Target[1..separator].All(char.IsAsciiDigit)
            ? entry.Target[(separator + 3)..]
            : entry.Target;
    }

    public static string? EditUrl(AuditEvent entry)
    {
        if (entry.Action is not (AuditActions.Create or AuditActions.Edit) ||
            !int.TryParse(entry.EntityId, out var entityId) || entityId <= 0)
            return null;

        // Project already has a stable read-only page; the other types still route through the editor's query trigger.
        if (entry.EntityType == AuditEntities.Project) return $"/proiecte/{entityId}";
        var path = entry.EntityType switch
        {
            AuditEntities.Product => "/produse",
            AuditEntities.Beneficiary => "/beneficiari",
            AuditEntities.User => "/utilizatori",
            _ => null
        };
        return path is null ? null : $"{path}?edit={entityId}";
    }
}

public interface IAuditTrail
{
    Task RecordAsync(AuditWrite entry, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AuditEvent>> GetEventsAsync(CancellationToken cancellationToken = default);
}

public sealed class FileAuditTrail(IWebHostEnvironment environment, IConfiguration configuration, ILogger<FileAuditTrail> logger) : IAuditTrail
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string path = Path.GetFullPath(configuration["App:AuditPath"] ??
        Path.Combine(environment.ContentRootPath, "data", "audit-events.jsonl"));

    public async Task RecordAsync(AuditWrite entry, CancellationToken cancellationToken = default)
    {
        var auditEvent = new AuditEvent(Guid.NewGuid(), DateTime.UtcNow, entry.ActorUsername, entry.ActorRole,
            entry.EntityType, AuditActions.Normalize(entry.Action), entry.Target, entry.Details,
            entry.Motif ?? string.Empty, entry.EntityId ?? string.Empty, entry.ArchiveOperationId);
        var line = JsonSerializer.Serialize(auditEvent, JsonOptions) + Environment.NewLine;
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.AppendAllTextAsync(path, line, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogError("Audit event could not be persisted ({ErrorType}).", exception.GetType().Name);
            throw;
        }
        finally { gate.Release(); }
    }

    public async Task<IReadOnlyList<AuditEvent>> GetEventsAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(path)) return Array.Empty<AuditEvent>();
            var lines = await File.ReadAllLinesAsync(path, cancellationToken).ConfigureAwait(false);
            return lines.Reverse().Select(Parse).Where(entry => entry is not null).Cast<AuditEvent>().Take(2000).ToArray();
        }
        finally { gate.Release(); }
    }

    private static AuditEvent? Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        try
        {
            var entry = JsonSerializer.Deserialize<AuditEvent>(line, JsonOptions);
            return entry is null ? null : entry with
            {
                TimestampUtc = ToUtc(entry.TimestampUtc),
                Action = AuditActions.Normalize(entry.Action),
                Motif = entry.Motif ?? string.Empty,
                EntityId = entry.EntityId ?? string.Empty
            };
        }
        catch (JsonException) { return null; }
    }

    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}

public static class AuditRecorder
{
    public static Task RecordCreateAsync(IAuditTrail? trail, IAccessControl? access, string entityType,
        string entityId, string target, string details, CancellationToken cancellationToken) =>
        RecordEntityAsync(trail, access, entityType, AuditActions.Create, entityId, target, details, string.Empty, cancellationToken);

    public static Task RecordEditAsync(IAuditTrail? trail, IAccessControl? access, string entityType,
        string entityId, string target, IEnumerable<AuditChange> changes, string motif, CancellationToken cancellationToken) =>
        RecordEntityAsync(trail, access, entityType, AuditActions.Edit, entityId, target,
            AuditDetails.Changes(changes.ToArray()), motif, cancellationToken);

    public static Task RecordDeleteAsync(IAuditTrail? trail, IAccessControl? access, string entityType,
        string entityId, string target, string details, string motif, CancellationToken cancellationToken,
        Guid? archiveOperationId = null) =>
        RecordEntityAsync(trail, access, entityType, AuditActions.Delete, entityId, target, details, motif,
            cancellationToken, archiveOperationId);

    public static Task RecordDeleteAsync(IAuditTrail? trail, ArchiveOperation operation,
        CancellationToken cancellationToken) => trail is null
        ? Task.CompletedTask
        : trail.RecordAsync(new(operation.ActorUsername, operation.ActorRole,
            operation.Request.Snapshot.EntityType, AuditActions.Delete, operation.Request.Target,
            operation.Request.Details, operation.Request.Motif, operation.Request.Snapshot.OriginalId,
            operation.Id), cancellationToken);

    public static Task RecordSessionAsync(IAuditTrail? trail, string actorUsername, string actorRole,
        bool connected, CancellationToken cancellationToken) => trail is null
        ? Task.CompletedTask
        : trail.RecordAsync(new(actorUsername, actorRole, AuditEntities.User,
            connected ? AuditActions.Login : AuditActions.Logout, actorUsername,
            connected ? "Sesiune autentificată" : "Sesiune încheiată", string.Empty, string.Empty), cancellationToken);

    private static async Task RecordEntityAsync(IAuditTrail? trail, IAccessControl? access, string entityType,
        string action, string entityId, string target, string details, string motif, CancellationToken cancellationToken,
        Guid? archiveOperationId = null)
    {
        if (trail is null) return;
        var actor = access is null ? "sistem" : await access.GetUsernameAsync(cancellationToken).ConfigureAwait(false) ?? "necunoscut";
        var role = access is not null && await access.IsAdministratorAsync(cancellationToken).ConfigureAwait(false)
            ? AccessRoles.Administrator : AccessRoles.LimitedUser;
        await trail.RecordAsync(new(actor, role, entityType, action, target, details, motif, entityId, archiveOperationId), cancellationToken).ConfigureAwait(false);
    }
}
