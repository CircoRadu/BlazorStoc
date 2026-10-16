using System.Data;
using System.Globalization;
using MySqlConnector;
using static BlazorStoc.Services.MariaDb;

namespace BlazorStoc.Services;

// MariaDB mode: `offers`, `offer_lines`, `offer_line_matches` and `beneficiary_aliases` (migration 25). Taking an offer over needs a product operator and a
// beneficiary operator; the offer and its lines are written in one transaction (the project and its component are created or added just before, each with
// its own journal event). The events of an offer are recorded under its project.
public sealed class MariaOfferRepository(
    IConfiguration configuration,
    IProjectRepository projects,
    IProjectComponentRepository components,
    IBeneficiaryRepository beneficiaries,
    IAccessControl? accessControl = null,
    IAuditTrail? auditTrail = null) : IOfferRepository
{

    private const string Select = """
        SELECT o.id,o.number,o.revision,o.title,o.category,o.beneficiary_id,COALESCE(b.name,''),o.project_id,COALESCE(p.name,''),o.system_type_id,t.name,o.template_id,o.file_name,o.created_by,o.created_utc
        FROM offers o LEFT JOIN beneficiaries b ON b.id=o.beneficiary_id LEFT JOIN projects p ON p.id=o.project_id LEFT JOIN system_types t ON t.id=o.system_type_id
        """;

    private static OfferRecord ReadOffer(MySqlDataReader reader) => new(checked((int)reader.GetInt64(0)), reader.GetString(1), Convert.ToInt32(reader.GetValue(2)), reader.GetString(3),
        reader.GetString(4), checked((int)reader.GetInt64(5)), reader.GetString(6), checked((int)reader.GetInt64(7)), reader.GetString(8),
        reader.IsDBNull(9) ? null : checked((int)reader.GetInt64(9)), reader.IsDBNull(10) ? null : reader.GetString(10),
        reader.IsDBNull(11) ? null : checked((int)reader.GetInt64(11)), reader.GetString(12), reader.GetString(13), reader.GetString(14));

    public async Task<OfferRecord?> GetLatestAsync(string number, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAnyPermissionAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        return (await ReadOffersAsync(connection, null, $"{Select} WHERE o.number_key=@key ORDER BY o.revision DESC LIMIT 1", cancellationToken, ("@key", OfferRules.Key(number))).ConfigureAwait(false)).FirstOrDefault();
    }

    public async Task<IReadOnlyList<OfferRecord>> GetForProjectAsync(int projectId, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAnyPermissionAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        return await ReadOffersAsync(connection, null, $"{Select} WHERE o.project_id=@project ORDER BY o.number,o.revision DESC", cancellationToken, ("@project", projectId)).ConfigureAwait(false);
    }

    private static async Task<List<OfferRecord>> ReadOffersAsync(MySqlConnection connection, MySqlTransaction? transaction, string sql, CancellationToken token, params (string Name, object? Value)[] parameters)
    {
        await using var command = Command(connection, transaction, sql, parameters);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        var result = new List<OfferRecord>();
        while (await reader.ReadAsync(token).ConfigureAwait(false)) result.Add(ReadOffer(reader));
        return result;
    }

    public async Task<IReadOnlyList<OfferLineRecord>> GetLinesAsync(int offerId, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAnyPermissionAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        return await ReadLinesAsync(connection, null, offerId, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<List<OfferLineRecord>> ReadLinesAsync(MySqlConnection connection, MySqlTransaction? transaction, int offerId, CancellationToken token)
    {
        await using var command = Command(connection, transaction, """
            SELECT l.id,l.offer_id,l.line_order,l.section,l.number,l.product_type,l.name,l.unit,l.quantity,l.in_stock,l.product_id,p.name
            FROM offer_lines l LEFT JOIN products p ON p.id=l.product_id WHERE l.offer_id=@offer ORDER BY l.line_order
            """, ("@offer", offerId));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        var result = new List<OfferLineRecord>();
        while (await reader.ReadAsync(token).ConfigureAwait(false))
            result.Add(new OfferLineRecord(checked((int)reader.GetInt64(0)), checked((int)reader.GetInt64(1)), Convert.ToInt32(reader.GetValue(2)), reader.GetString(3), reader.GetString(4),
                reader.GetString(5), reader.GetString(6), reader.GetString(7), Convert.ToDecimal(reader.GetValue(8), CultureInfo.InvariantCulture), Convert.ToBoolean(reader.GetValue(9)),
                reader.IsDBNull(10) ? null : checked((int)reader.GetInt64(10)), reader.IsDBNull(11) ? null : reader.GetString(11)));
        return result;
    }

    public async Task<OfferDiff?> DiffAsync(string number, IReadOnlyList<OfferImportLine> lines, CancellationToken cancellationToken = default)
    {
        var latest = await GetLatestAsync(number, cancellationToken).ConfigureAwait(false);
        return latest is null ? null : OfferDiffRules.Compare(latest.Revision, await GetLinesAsync(latest.Id, cancellationToken).ConfigureAwait(false), lines);
    }

    public async Task<IReadOnlyDictionary<string, int>> GetRememberedMatchesAsync(CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAnyPermissionAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, null, "SELECT m.name_key,m.product_id FROM offer_line_matches m INNER JOIN products p ON p.id=m.product_id");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new Dictionary<string, int>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result[reader.GetString(0)] = checked((int)reader.GetInt64(1));
        return result;
    }

    public async Task<int?> FindBeneficiaryByAliasAsync(string text, CancellationToken cancellationToken = default)
    {
        var key = OfferRules.Key(text);
        if (key.Length == 0) return null;
        if (accessControl is not null) await accessControl.EnsureAnyPermissionAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, null, "SELECT beneficiary_id FROM beneficiary_aliases WHERE alias_key=@key", ("@key", key));
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is { } id ? Convert.ToInt32(id) : null;
    }

    public async Task<OfferImportResult> ImportAsync(OfferImportRequest request, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAsync("oferte.add", cancellationToken).ConfigureAwait(false);
        var number = SystemTypeRules.Clean(request.Number);
        if (OfferRules.Key(number).Length == 0) throw new OfferException(OfferMessages.NumberRequired);
        if (request.BeneficiaryId <= 0) throw new OfferException(OfferMessages.BeneficiaryRequired);
        if (request.ProjectId is null && SystemTypeRules.Clean(request.NewProjectName).Length == 0) throw new OfferException(OfferMessages.ProjectRequired);
        var lines = request.Lines.Where(line => line.Name.Trim().Length > 0 && line.Quantity > 0).ToList();
        if (lines.Count == 0) throw new OfferException(OfferMessages.NoLines);
        var beneficiary = (await beneficiaries.GetBeneficiariesAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(item => item.Id == request.BeneficiaryId)
                          ?? throw new OfferException(OfferMessages.BeneficiaryMissing);
        var actor = await RepositoryAudit.ActorAsync(accessControl, cancellationToken).ConfigureAwait(false);

        // 1. The project (existing, or created from the title of the offer) and its component.
        Project project;
        if (request.ProjectId is { } projectId)
        {
            project = await projects.GetAsync(projectId, cancellationToken).ConfigureAwait(false) ?? throw new OfferException(OfferMessages.ProjectMissing);
            if (project.BeneficiaryId != beneficiary.Id) throw new OfferException(OfferMessages.ProjectMissing);
        }
        else project = await projects.CreateAsync(new ProjectInput { BeneficiaryId = beneficiary.Id, Name = SystemTypeRules.Clean(request.NewProjectName) }, cancellationToken).ConfigureAwait(false);
        if (request.SystemTypeId is { } typeId)
        {
            var existing = (await components.GetForProjectAsync(project.Id, true, cancellationToken).ConfigureAwait(false)).FirstOrDefault(item => item.SystemTypeId == typeId);
            if (existing is null) await components.AddAsync(project.Id, [typeId], cancellationToken).ConfigureAwait(false);
            else if (existing.Archived) await components.ReactivateAsync(existing, cancellationToken).ConfigureAwait(false);
        }

        // 2. The offer and its lines, as a new revision when the number is known.
        var (offerId, revision, previous, linked) = await WriteAsync<(int OfferId, int Revision, (int Revision, List<OfferLineRecord> Lines)? Previous, int Linked)>(async (connection, transaction) =>
        {
            await using (var count = Command(connection, transaction, "SELECT COUNT(*) FROM products WHERE id IN (" + (lines.Any(line => line.ProductId is not null) ? string.Join(",", lines.Where(line => line.ProductId is not null).Select(line => line.ProductId!.Value).Distinct()) : "0") + ")"))
            {
                var needed = lines.Where(line => line.ProductId is not null).Select(line => line.ProductId!.Value).Distinct().Count();
                if (Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) != needed)
                    throw new OfferException(OfferMessages.LineProductMissing(lines.FindIndex(line => line.ProductId is not null) + 1));
            }
            var numberKey = OfferRules.Key(number);
            await using var latest = Command(connection, transaction, "SELECT id,revision FROM offers WHERE number_key=@key ORDER BY revision DESC LIMIT 1 FOR UPDATE", ("@key", numberKey));
            int? previousId = null; var nextRevision = 1;
            await using (var reader = await latest.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) { previousId = checked((int)reader.GetInt64(0)); nextRevision = Convert.ToInt32(reader.GetValue(1)) + 1; }
            var previousLines = previousId is { } id ? await ReadLinesAsync(connection, transaction, id, cancellationToken).ConfigureAwait(false) : null;
            var now = MariaTimeText.Format(DateTime.UtcNow);
            await using var insert = Command(connection, transaction, """
                INSERT INTO offers(number,number_key,revision,title,category,beneficiary_id,project_id,system_type_id,template_id,file_name,created_by,created_utc)
                VALUES(@number,@key,@revision,@title,@category,@beneficiary,@project,@type,@template,@file,@by,@now)
                """, ("@number", number), ("@key", numberKey), ("@revision", nextRevision), ("@title", Cut(request.Title, 300)), ("@category", Cut(request.Category, 120)),
                ("@beneficiary", beneficiary.Id), ("@project", project.Id), ("@type", request.SystemTypeId), ("@template", request.TemplateId), ("@file", Cut(request.FileName, 260)),
                ("@by", actor.Username), ("@now", now));
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            var newId = checked((int)insert.LastInsertedId);
            for (var index = 0; index < lines.Count; index++)
            {
                var line = lines[index];
                var productId = line.InStock ? line.ProductId : null;
                await using var lineInsert = Command(connection, transaction, """
                    INSERT INTO offer_lines(offer_id,line_order,section,number,product_type,name,name_key,unit,quantity,in_stock,product_id)
                    VALUES(@offer,@order,@section,@number,@type,@name,@key,@unit,@quantity,@stock,@product)
                    """, ("@offer", newId), ("@order", index + 1), ("@section", Cut(line.Section, 100)), ("@number", Cut(line.Number, 20)), ("@type", Cut(line.ProductType, 100)),
                    ("@name", line.Name.Trim()), ("@key", OfferLineRules.LineKey(line.Name)), ("@unit", Cut(line.Unit, 30)), ("@quantity", line.Quantity), ("@stock", line.InStock ? 1 : 0), ("@product", productId));
                await lineInsert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                if (productId is not null)
                    await using (var remember = Command(connection, transaction, """
                        INSERT INTO offer_line_matches(name_key,product_id,confirmed_by,confirmed_utc) VALUES(@key,@product,@by,@now)
                        ON DUPLICATE KEY UPDATE product_id=VALUES(product_id),confirmed_by=VALUES(confirmed_by),confirmed_utc=VALUES(confirmed_utc)
                        """, ("@key", OfferLineRules.LineKey(line.Name)), ("@product", productId.Value), ("@by", actor.Username), ("@now", now)))
                        await remember.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            return (newId, nextRevision, previousLines is null ? null : (nextRevision - 1, previousLines), lines.Count(line => line.InStock && line.ProductId is not null));
        }, cancellationToken).ConfigureAwait(false);

        // 3. An alternative name of the beneficiary (what the offer calls it), remembered for the next offers.
        var aliasText = SystemTypeRules.Clean(request.BeneficiaryText);
        var aliasAdded = false;
        if (request.RememberBeneficiaryAlias && OfferRules.Key(aliasText).Length > 0 && OfferRules.Key(aliasText) != OfferRules.Key(beneficiary.Name))
            aliasAdded = await TryAddAliasAsync(beneficiary.Id, aliasText, actor.Username, cancellationToken).ConfigureAwait(false);

        var offer = (await GetLatestAsync(number, cancellationToken).ConfigureAwait(false))!;
        var diff = previous is null ? null : OfferDiffRules.Compare(previous.Value.Revision, previous.Value.Lines, lines);
        var toPurchase = lines.Count(line => line.InStock && line.ProductId is null);
        var outOfStock = lines.Count(line => !line.InStock);
        var details = AuditDetails.Identification(("Ofertă", number), ("Revizie", revision.ToString(CultureInfo.InvariantCulture)), ("Titlu", request.Title), ("Beneficiar", beneficiary.Name),
            ("Proiect", project.Name), ("Componentă", offer.SystemTypeName ?? "—"), ("Linii", lines.Count.ToString(CultureInfo.InvariantCulture)), ("Legate de produse", linked.ToString(CultureInfo.InvariantCulture)),
            ("De achiziționat", toPurchase.ToString(CultureInfo.InvariantCulture)), ("În afara stocului", outOfStock.ToString(CultureInfo.InvariantCulture)));
        if (diff is not null) details += $"; Diferențe față de revizia {diff.PreviousRevision}: +{diff.Added} −{diff.Removed} ~{diff.QuantityChanged}";
        await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.Project, previous is null ? AuditActions.ImportOffer : AuditActions.ReviseOffer, project.Id.ToString(), project.Name,
            details, string.Empty, cancellationToken).ConfigureAwait(false);
        var saved = await GetLinesAsync(offerId, cancellationToken).ConfigureAwait(false);
        foreach (var line in saved.Where(item => item.ProductId is not null))
            await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.Project, AuditActions.LinkOfferLine, project.Id.ToString(), line.ProductName ?? $"#{line.ProductId}",
                AuditDetails.Identification(("Ofertă", $"{number} (revizia {revision})"), ("Linia", line.Order.ToString(CultureInfo.InvariantCulture)), ("Produs", line.ProductName ?? $"#{line.ProductId}"),
                    ("Cantitate", OfferDiffRules.Format(line.Quantity))), string.Empty, cancellationToken).ConfigureAwait(false);
        _ = aliasAdded;
        return new OfferImportResult(offer, previous is not null, diff, linked, toPurchase, outOfStock, project.Id);
    }

    private async Task<bool> TryAddAliasAsync(int beneficiaryId, string text, string by, CancellationToken token)
    {
        try
        {
            await using var connection = await MariaDb.OpenAsync(configuration, token).ConfigureAwait(false);
            await using var insert = Command(connection, null, "INSERT INTO beneficiary_aliases(beneficiary_id,alias,alias_key,created_by,created_utc) VALUES(@beneficiary,@alias,@key,@by,@now)",
                ("@beneficiary", beneficiaryId), ("@alias", Cut(text, 200)), ("@key", OfferRules.Key(text)), ("@by", by), ("@now", MariaTimeText.Format(DateTime.UtcNow)));
            await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }
        catch (MySqlException exception) when (exception.Number == 1062) { return false; }
        var name = (await beneficiaries.GetBeneficiariesAsync(token).ConfigureAwait(false)).FirstOrDefault(item => item.Id == beneficiaryId)?.Name ?? $"#{beneficiaryId}";
        await AuditRecorder.RecordEditAsync(auditTrail, accessControl, AuditEntities.Beneficiary, beneficiaryId.ToString(), name,
            [new AuditChange("Denumire alternativă", "—", text)], string.Empty, token, AuditActions.AddBeneficiaryAlias).ConfigureAwait(false);
        return true;
    }

    private static string Cut(string? text, int length)
    {
        var value = (text ?? string.Empty).Trim();
        return value.Length <= length ? value : value[..length];
    }

    private Task<T> WriteAsync<T>(Func<MySqlConnection, MySqlTransaction, Task<T>> action, CancellationToken token) =>
        MariaDb.WriteAsync(configuration, action, message => new OfferException(message), token,
            "Modificările sunt permise numai în baza BlazorStoc. Verifică numele bazei configurate.");

}
