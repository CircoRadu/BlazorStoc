using System.Text.Json;

namespace BlazorStoc.Services;

public sealed record ArchiveEntitySchema(string EntityType, string TableName, bool SupportsRelations, bool SupportsFiles);

public static class ArchiveSchemaRegistry
{
    private static readonly IReadOnlyDictionary<string, ArchiveEntitySchema> Schemas =
        new[]
        {
            new ArchiveEntitySchema(AuditEntities.Product, "archive_products", true, true),
            new ArchiveEntitySchema(AuditEntities.Beneficiary, "archive_beneficiaries", true, false),
            new ArchiveEntitySchema(AuditEntities.User, "archive_web_users", true, false),
            new ArchiveEntitySchema(AuditEntities.Project, "archive_projects", true, true),
            new ArchiveEntitySchema(AuditEntities.ProjectObservation, "archive_project_observations", true, true),
            new ArchiveEntitySchema(AuditEntities.ProjectObservationFile, "archive_project_observation_files", false, true),
            new ArchiveEntitySchema(AuditEntities.StockMovement, "archive_stock_movements", true, false)
        }.ToDictionary(schema => schema.EntityType, StringComparer.Ordinal);

    public static IReadOnlyCollection<ArchiveEntitySchema> All { get; } = Schemas.Values.ToArray();

    public static ArchiveEntitySchema Require(string entityType) => Schemas.TryGetValue(entityType, out var schema)
        ? schema
        : throw new ArchiveContractException(
            $"Tipul «{entityType}» nu are schemă de arhivare. Adaugă tabela archive_*, relațiile, fișierele și verificările înainte de a permite ștergerea.");
}

public sealed record ArchiveRelationSnapshot(string RelationType, string RelationId, string DataJson)
{
    public static ArchiveRelationSnapshot Create<T>(string relationType, string relationId, T data) =>
        new(Required(relationType, nameof(relationType)), Required(relationId, nameof(relationId)),
            ArchiveSnapshot.SerializePublicData(data));

    private static string Required(string? value, string name) => string.IsNullOrWhiteSpace(value)
        ? throw new ArgumentException("Valoarea este obligatorie.", name)
        : value.Trim();
}

public sealed record ArchiveProtectedValue(string Name, string Hash)
{
    public ArchiveProtectedValue Validated()
    {
        if (string.IsNullOrWhiteSpace(Name) || !Name.EndsWith("Hash", StringComparison.OrdinalIgnoreCase))
            throw new ArchiveContractException("Arhiva protejată acceptă numai valori hash identificate explicit.");
        if (string.IsNullOrWhiteSpace(Hash))
            throw new ArchiveContractException("Valoarea hash protejată este obligatorie.");
        return this with { Name = Name.Trim(), Hash = Hash.Trim() };
    }
}

public sealed record ArchiveSnapshot(
    string EntityType,
    string OriginalId,
    long Version,
    string DataJson,
    IReadOnlyList<ArchiveRelationSnapshot> Relations,
    IReadOnlyList<ArchiveProtectedValue> ProtectedValues)
{
    private static readonly string[] SensitiveNames = ["password", "parola", "secret", "token", "credential"];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static ArchiveSnapshot Create<T>(string entityType, string originalId, long version, T data,
        IEnumerable<ArchiveRelationSnapshot>? relations = null,
        IEnumerable<ArchiveProtectedValue>? protectedValues = null) =>
        new ArchiveSnapshot(entityType, originalId, version, SerializePublicData(data),
            relations?.ToArray() ?? [], protectedValues?.Select(value => value.Validated()).ToArray() ?? []).Validated();

    internal static string SerializePublicData<T>(T data)
    {
        var json = JsonSerializer.Serialize(data, JsonOptions);
        using var document = JsonDocument.Parse(json);
        RejectSensitivePublicData(document.RootElement);
        return json;
    }

    public ArchiveSnapshot Validated()
    {
        if (string.IsNullOrWhiteSpace(EntityType)) throw new ArchiveContractException("Tipul obiectului este obligatoriu.");
        ArchiveSchemaRegistry.Require(EntityType.Trim());
        if (string.IsNullOrWhiteSpace(OriginalId)) throw new ArchiveContractException("Identificatorul original este obligatoriu.");
        if (Version < 0) throw new ArchiveContractException("Versiunea obiectului nu poate fi negativă.");
        try
        {
            using var document = JsonDocument.Parse(DataJson);
            RejectSensitivePublicData(document.RootElement);
        }
        catch (JsonException exception)
        {
            throw new ArchiveContractException("Datele publice ale arhivei nu sunt JSON valid.", exception);
        }
        foreach (var relation in Relations)
        {
            if (string.IsNullOrWhiteSpace(relation.RelationType) || string.IsNullOrWhiteSpace(relation.RelationId))
                throw new ArchiveContractException("Relațiile arhivate trebuie să aibă tip și identificator.");
            try
            {
                using var document = JsonDocument.Parse(relation.DataJson);
                RejectSensitivePublicData(document.RootElement);
            }
            catch (JsonException exception)
            {
                throw new ArchiveContractException("Datele relației arhivate nu sunt JSON valid.", exception);
            }
        }
        foreach (var value in ProtectedValues) value.Validated();
        return this with { EntityType = EntityType.Trim(), OriginalId = OriginalId.Trim() };
    }

    private static void RejectSensitivePublicData(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject())
            {
                var name = property.Name.Replace("_", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);
                if (SensitiveNames.Any(part => name.Contains(part, StringComparison.OrdinalIgnoreCase)))
                    throw new ArchiveContractException($"Câmpul sensibil «{property.Name}» trebuie păstrat numai în zona protejată a arhivei.");
                RejectSensitivePublicData(property.Value);
            }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RejectSensitivePublicData(item);
    }
}

public sealed record ArchiveRequest(ArchiveSnapshot Snapshot, string Target, string Details, string Motif)
{
    public ArchiveRequest Validated()
    {
        Snapshot.Validated();
        var motif = ChangeReasonRules.Normalize(Motif);
        if (ChangeReasonRules.ValidationError(motif) is { } reasonError) throw new ArchiveContractException(reasonError);
        if (string.IsNullOrWhiteSpace(Target)) throw new ArchiveContractException("Ținta operației de arhivare este obligatorie.");
        if (string.IsNullOrWhiteSpace(Details)) throw new ArchiveContractException("Datele de identificare sunt obligatorii.");
        return this with { Target = Target.Trim(), Details = Details.Trim(), Motif = motif };
    }
}

public sealed record ArchiveOperation(
    Guid Id,
    DateTime TimestampUtc,
    string ActorUsername,
    string ActorRole,
    ArchiveRequest Request);

public static class ArchiveRequests
{
    public static ArchiveRequest Product(Product value, string motif)
    {
        return new(ArchiveSnapshot.Create(AuditEntities.Product, value.Id.ToString(), value.Version, value),
            ProductCode.AuditTarget(value), ProductCode.AuditIdentification(value), motif);
    }

    public static ArchiveRequest Beneficiary(Beneficiary value, string motif)
    {
        var details = AuditDetails.Identification(("Denumire", value.Name), ("CUI", value.Cui));
        return new(ArchiveSnapshot.Create(AuditEntities.Beneficiary, value.Id.ToString(), value.Version, value),
            $"#{value.Id} · {value.Name}", details, motif);
    }

    public static ArchiveRequest User(WebUser value, string motif,
        IEnumerable<ArchiveProtectedValue>? protectedValues = null)
    {
        var details = AuditDetails.Identification(("Nume utilizator", value.Username),
            ("Nume afișat", value.DisplayName), ("Rol", value.Role), ("Stare", value.IsActive ? "activ" : "inactiv"));
        return new(ArchiveSnapshot.Create(AuditEntities.User, value.Id.ToString(), value.Version, value,
                protectedValues: protectedValues),
            $"#{value.Id} · {value.Username}", details, motif);
    }

    public static ArchiveRequest Project(Project value, string beneficiaryName,
        IEnumerable<ProjectObservation> observations, IEnumerable<ProjectObservationFile> files, string motif)
    {
        var details = AuditDetails.Identification(("Denumire", value.Name), ("Beneficiar", beneficiaryName),
            ("Observații", value.Observations));
        var relations = observations.Select(observation =>
                ArchiveRelationSnapshot.Create(AuditEntities.ProjectObservation, observation.Id.ToString(), observation))
            .Concat(files.Select(file =>
                ArchiveRelationSnapshot.Create(AuditEntities.ProjectObservationFile, file.Id.ToString(), file)));
        return new(ArchiveSnapshot.Create(AuditEntities.Project, value.Id.ToString(), value.Version, value, relations),
            value.Name, details, motif);
    }

    public static ArchiveRequest ProjectObservation(ProjectObservation value, string projectName,
        IEnumerable<ProjectObservationFile> files, string motif)
    {
        var details = AuditDetails.Identification(("Denumire", value.Name), ("Proiect", projectName), ("Autor", value.Author));
        var relations = files.Select(file =>
            ArchiveRelationSnapshot.Create(AuditEntities.ProjectObservationFile, file.Id.ToString(), file));
        return new(ArchiveSnapshot.Create(AuditEntities.ProjectObservation, value.Id.ToString(), value.Version, value, relations),
            value.Name, details, motif);
    }

    public const string StockMovementHistoryRelation = "IstoricMiscareStoc";

    public static ArchiveRequest StockMovement(StockMovement value, string productCode,
        IEnumerable<StockMovementHistoryEntry> history, string motif)
    {
        var relations = history.Select(entry =>
            ArchiveRelationSnapshot.Create(StockMovementHistoryRelation, entry.Id.ToString(), entry));
        return new(ArchiveSnapshot.Create(AuditEntities.StockMovement, value.Id.ToString(), value.Version, value, relations),
            StockMovementRules.Target(productCode), StockMovementRules.AuditIdentification(value, productCode), motif);
    }

    public static ArchiveRequest ProjectObservationFile(ProjectObservationFile value, string motif)
    {
        var details = AuditDetails.Identification(("Nume fișier", value.OriginalName), ("Autor", value.Author));
        return new(ArchiveSnapshot.Create(AuditEntities.ProjectObservationFile, value.Id.ToString(), 0, value),
            value.OriginalName, details, motif);
    }
}

public interface IArchiveService
{
    Task ExecuteAsync(ArchiveRequest request,
        Func<ArchiveOperation, CancellationToken, Task> archiveAndRemove,
        CancellationToken cancellationToken = default);
}

public sealed class ArchiveService(IAccessControl? accessControl = null, TimeProvider? timeProvider = null) : IArchiveService
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task ExecuteAsync(ArchiveRequest request,
        Func<ArchiveOperation, CancellationToken, Task> archiveAndRemove,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(archiveAndRemove);
        cancellationToken.ThrowIfCancellationRequested();
        request = request.Validated();
        var actor = accessControl is null
            ? "sistem"
            : await accessControl.GetUsernameAsync(cancellationToken).ConfigureAwait(false) ?? "necunoscut";
        var role = accessControl is not null &&
                   await accessControl.IsAdministratorAsync(cancellationToken).ConfigureAwait(false)
            ? AccessRoles.Administrator
            : AccessRoles.LimitedUser;
        var timestamp = clock.GetUtcNow().UtcDateTime;
        var operation = new ArchiveOperation(Guid.NewGuid(), timestamp, actor, role, request);
        await archiveAndRemove(operation, cancellationToken).ConfigureAwait(false);
    }
}

public sealed class ArchiveContractException : Exception
{
    public ArchiveContractException(string message) : base(message) { }
    public ArchiveContractException(string message, Exception innerException) : base(message, innerException) { }
}
