using System.Globalization;
using MySqlConnector;
using static BlazorStoc.Services.MariaDb;

namespace BlazorStoc.Services;

// Archive, restore and permanent deletion of parameters and families (migration 56; the rules are in ParameterArchive.cs). A family is archived with all its
// members at once; both run through the same steps, which take a list of parameters.
public sealed partial class MariaProductParameterRepository
{
    private sealed record ParamInfo(int Id, string Name, string Unit, long Version, int SubcategoryId, int? FamilyId, string? ArchivedUtc, string Target);
    private sealed record ArchiveStep(int ProductId, string OldName, string NewBase, string NewName, int Quantity, int ParameterId, string ParameterName, string Display, string Node, int Position);
    private sealed record RestoreStep(ParameterRestoreItem Item, int Quantity, IReadOnlyList<string> Displays, int ParameterId, string ParameterName, string Display, string Node);

    private async Task<string> ActorAsync(CancellationToken token) =>
        accessControl is null ? "" : await accessControl.GetUsernameAsync(token).ConfigureAwait(false) ?? "";

    private static async Task<List<ParamInfo>> ReadInfosAsync(MySqlConnection connection, MySqlTransaction? transaction, IReadOnlyList<int> ids, bool lockRows, CancellationToken token)
    {
        var (list, parameters) = InList(ids, "i");
        var infos = new List<ParamInfo>();
        await using var command = Command(connection, transaction, $"""
            SELECT sp.id,sp.name,sp.unit,sp.version,COALESCE(s.id,-c.id),sp.family_id,sp.archived_utc,c.name,COALESCE(s.name,'')
            FROM subcategory_parameters sp LEFT JOIN subcategories s ON s.id=sp.subcategory_id INNER JOIN categories c ON c.id=COALESCE(s.category_id,sp.category_id)
            WHERE sp.id IN ({list}) ORDER BY sp.position,sp.id{(lockRows ? " FOR UPDATE" : "")}
            """, parameters);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
            infos.Add(new ParamInfo(checked((int)reader.GetInt64(0)), reader.GetString(1), reader.GetString(2), reader.GetInt64(3), checked((int)reader.GetInt64(4)),
                reader.IsDBNull(5) ? null : checked((int)reader.GetInt64(5)), reader.IsDBNull(6) ? null : reader.GetString(6), Target(reader.GetString(7), reader.GetString(8))));
        if (infos.Count != ids.Count) throw new ProductOperationException("Parametrul nu mai există. Actualizează lista și reia operația.");
        return infos;
    }

    // One value a product has, in the order of its code.
    private sealed record OrderedLink(int ParameterId, int ValueId, string Unit, string Value)
    {
        public string Display => ProductParameterRules.Display(Unit, Value);
    }

    // The values a product shows in its name, in the order of the code: the category's parameters first, then those of the ancestors from the root down to the
    // product's subcategory; inside one owner by the position of the parameter (a family at the position of its first member). visibleSql chooses the links that count.
    private static async Task<List<OrderedLink>> OrderedLinksAsync(MySqlConnection connection, MySqlTransaction? transaction, int productId, string visibleSql, params (string, object?)[] extra)
    {
        int productSubcategory = 0, productCategory = 0;
        await using (var owner = Command(connection, transaction, "SELECT subcategory_id,category_id FROM products WHERE id=@id", ("@id", productId)))
        await using (var reader = await owner.ExecuteReaderAsync(default).ConfigureAwait(false))
            if (await reader.ReadAsync(default).ConfigureAwait(false)) (productSubcategory, productCategory) = (checked((int)reader.GetInt64(0)), checked((int)reader.GetInt64(1)));
        var parents = await SubcategoryAncestry.ParentsAsync(connection, transaction, default).ConfigureAwait(false);
        List<int> chain = [-productCategory, .. SubcategoryAncestry.Chain(parents, productSubcategory)];
        var rows = new List<(OrderedLink Link, int Owner, int Position, int Order)>();
        await using var command = Command(connection, transaction, $"""
            SELECT sp.id,v.id,sp.unit,v.value,COALESCE(sp.subcategory_id,-sp.category_id),sp.position,
                   COALESCE((SELECT MIN(m.position) FROM subcategory_parameters m WHERE m.family_id=sp.family_id),sp.position)
            FROM product_parameter_values pv
            INNER JOIN subcategory_parameters sp ON sp.id=pv.parameter_id INNER JOIN parameter_values v ON v.id=pv.value_id
            WHERE pv.product_id=@product AND ({visibleSql})
            """, [("@product", productId), .. extra]);
        await using (var reader = await command.ExecuteReaderAsync(default).ConfigureAwait(false))
            while (await reader.ReadAsync(default).ConfigureAwait(false))
                rows.Add((new OrderedLink(checked((int)reader.GetInt64(0)), checked((int)reader.GetInt64(1)), reader.GetString(2), reader.GetString(3)),
                    checked((int)reader.GetInt64(4)), reader.GetInt32(5), reader.GetInt32(6)));
        return rows.OrderBy(row => chain.IndexOf(row.Owner) is var index && index < 0 ? int.MaxValue : index).ThenBy(row => row.Order).ThenBy(row => row.Position).ThenBy(row => row.Link.ParameterId)
            .Select(row => row.Link).ToList();
    }

    private static async Task<List<(int ParameterId, string Display)>> ProductDisplaysAsync(MySqlConnection connection, MySqlTransaction? transaction, int productId,
        string visibleSql, params (string, object?)[] extra) =>
        (await OrderedLinksAsync(connection, transaction, productId, visibleSql, extra).ConfigureAwait(false)).Select(link => (link.ParameterId, link.Display)).ToList();
    private static async Task<string?> NameTakenByAsync(MySqlConnection connection, MySqlTransaction? transaction, string name, int productId, CancellationToken token)
    {
        await using var taken = Command(connection, transaction, "SELECT name FROM products WHERE normalized_name=@key AND id<>@id LIMIT 1",
            ("@key", TextNormalization.UniquenessKey(name)), ("@id", productId));
        return await taken.ExecuteScalarAsync(token).ConfigureAwait(false) as string;
    }

    // ---- archive -------------------------------------------------------------------------------------------------------------

    private static async Task<(List<ArchiveStep> Steps, List<string> Conflicts)> ComputeArchiveAsync(MySqlConnection connection, MySqlTransaction? transaction,
        IReadOnlyList<ParamInfo> infos, bool lockRows, CancellationToken token)
    {
        var ids = infos.Select(info => info.Id).ToHashSet();
        var linked = new List<(ParamInfo Info, int ProductId, string Name, int Quantity, string BaseModel, string Value)>();
        foreach (var info in infos)
        {
            await using var select = Command(connection, transaction, $"""
                SELECT p.id,p.name,p.quantity,COALESCE(p.base_model,p.name),v.value FROM product_parameter_values pv
                INNER JOIN products p ON p.id=pv.product_id INNER JOIN parameter_values v ON v.id=pv.value_id
                WHERE pv.parameter_id=@id AND pv.archived=0 ORDER BY p.name{(lockRows ? " FOR UPDATE" : "")}
                """, ("@id", info.Id));
            await using var reader = await select.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
                linked.Add((info, checked((int)reader.GetInt64(0)), reader.GetString(1), checked((int)reader.GetInt64(2)), reader.GetString(3), reader.GetString(4)));
        }
        var renamed = linked.Select(item => item.Name).ToHashSet(StringComparer.Ordinal);
        var steps = new List<ArchiveStep>();
        var conflicts = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in linked)
        {
            var all = await ProductDisplaysAsync(connection, transaction, item.ProductId, "pv.archived=0").ConfigureAwait(false);
            var display = ProductParameterRules.Display(item.Info.Unit, item.Value);
            var newBase = ParameterArchiveRules.Bake(item.BaseModel, display);
            var newName = ParameterArchiveRules.NewName(newBase, all.Where(entry => !ids.Contains(entry.ParameterId)).Select(entry => entry.Display));
            if (newName.Length > ProductParameterRules.ProductNameMaximumLength) { conflicts.Add($"{newName} (peste {ProductParameterRules.ProductNameMaximumLength} de caractere)"); continue; }
            if (await NameTakenByAsync(connection, transaction, newName, item.ProductId, token).ConfigureAwait(false) is { } clash && !renamed.Contains(clash))
                conflicts.Add($"{newName} (există deja ca «{clash}»)");
            if (!seen.Add(TextNormalization.UniquenessKey(newName))) conflicts.Add($"{newName} (rezultă pentru două produse)");
            steps.Add(new ArchiveStep(item.ProductId, item.Name, newBase, newName, item.Quantity, item.Info.Id, item.Info.Name, display, item.Info.Target,
                Math.Max(0, all.FindIndex(entry => entry.ParameterId == item.Info.Id))));
        }
        return (steps, conflicts);
    }

    private static ParameterArchivePreview ToPreview(List<ArchiveStep> steps, List<string> conflicts) =>
        new(steps.Select(step => new ParameterAffectedProduct(step.ProductId, step.OldName, step.NewName, step.Quantity)).ToList(), conflicts);

    public async Task<ParameterArchivePreview> PreviewParameterArchiveAsync(SubcategoryParameter parameter, CancellationToken cancellationToken = default)
    {
        await EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        var infos = await ReadInfosAsync(connection, null, [parameter.Id], false, cancellationToken).ConfigureAwait(false);
        var (steps, conflicts) = await ComputeArchiveAsync(connection, null, infos, false, cancellationToken).ConfigureAwait(false);
        return ToPreview(steps, conflicts);
    }

    public async Task<ParameterArchivePreview> PreviewFamilyArchiveAsync(ParameterFamily family, CancellationToken cancellationToken = default)
    {
        await EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        var infos = await ReadInfosAsync(connection, null, family.ParameterIds, false, cancellationToken).ConfigureAwait(false);
        var (steps, conflicts) = await ComputeArchiveAsync(connection, null, infos, false, cancellationToken).ConfigureAwait(false);
        return ToPreview(steps, conflicts);
    }

    private async Task<List<ArchiveStep>> ArchiveCoreAsync(MySqlConnection connection, MySqlTransaction transaction, IReadOnlyList<ParamInfo> infos, string actor, string reason, CancellationToken token)
    {
        var (steps, conflicts) = await ComputeArchiveAsync(connection, transaction, infos, true, token).ConfigureAwait(false);
        if (conflicts.Count > 0) throw new ProductOperationException(ProductParameterRules.ConflictMessage(conflicts));
        foreach (var step in steps)
        {
            await using (var rename = Command(connection, transaction, "UPDATE products SET base_model=@model,name=@name,normalized_name=@key,version=version+1 WHERE id=@id",
                             ("@model", step.NewBase), ("@name", step.NewName), ("@key", TextNormalization.UniquenessKey(step.NewName)), ("@id", step.ProductId)))
                await rename.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            await using (var link = Command(connection, transaction, "UPDATE product_parameter_values SET archived=1,baked_text=@text,baked_position=@position WHERE product_id=@product AND parameter_id=@parameter",
                             ("@text", step.Display), ("@position", step.Position), ("@product", step.ProductId), ("@parameter", step.ParameterId)))
                await link.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            await ParameterUsageLog.WriteAsync(connection, transaction, step.ParameterId, step.ParameterName, step.Display, step.ProductId, step.NewName, step.Node,
                ParameterUsageActions.Archived, actor, reason, token).ConfigureAwait(false);
        }
        var now = MariaTimeText.Format(DateTime.UtcNow);
        foreach (var info in infos)
        {
            await using var mark = Command(connection, transaction, "UPDATE subcategory_parameters SET archived_utc=@now,archived_by=@by,version=version+1 WHERE id=@id",
                ("@now", now), ("@by", actor), ("@id", info.Id));
            await mark.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }
        return steps;
    }

    private async Task AuditArchiveAsync(string action, int subcategoryId, string target, IEnumerable<(string, string)> identification, List<ArchiveStep> steps, string motif, CancellationToken token)
    {
        await AuditRecorder.RecordActionAsync(auditTrail, accessControl, OwnerKey.Entity(subcategoryId), action, OwnerKey.Id(subcategoryId), target,
            AuditDetails.Identification([.. identification, ("Produse redenumite", steps.Count.ToString(CultureInfo.InvariantCulture))]), motif, token).ConfigureAwait(false);
        foreach (var step in steps)
            await AuditRecorder.RecordEditAsync(auditTrail, accessControl, AuditEntities.Product, step.ProductId.ToString(CultureInfo.InvariantCulture), step.NewName,
                [new(ProductCode.Label, step.OldName, step.NewName)], motif, token, AuditActions.RenameProductByParameter).ConfigureAwait(false);
    }

    public async Task ArchiveParameterAsync(SubcategoryParameter original, string reason, CancellationToken cancellationToken = default)
    {
        await EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var motif = ProductGroupManagementRules.Reason(reason);
        var actor = await ActorAsync(cancellationToken).ConfigureAwait(false);
        var (info, steps) = await WriteAsync(async (connection, transaction) =>
        {
            var infos = await ReadInfosAsync(connection, transaction, [original.Id], true, cancellationToken).ConfigureAwait(false);
            if (infos[0].Version != original.Version) throw new ProductOperationException("Parametrul a fost modificat între timp. Actualizează lista și reia operația.");
            if (infos[0].ArchivedUtc is not null) throw new ProductOperationException(ParameterArchiveRules.AlreadyArchivedMessage);
            if (infos[0].FamilyId is not null) throw new ProductOperationException(ParameterArchiveRules.FamilyMemberMessage);
            return (infos[0], await ArchiveCoreAsync(connection, transaction, infos, actor, motif, cancellationToken).ConfigureAwait(false));
        }, cancellationToken).ConfigureAwait(false);
        await AuditArchiveAsync(AuditActions.ArchiveSubcategoryParameter, info.SubcategoryId, info.Target, [("Parametru", info.Name)], steps, motif, cancellationToken).ConfigureAwait(false);
    }

    public async Task ArchiveFamilyAsync(ParameterFamily original, string reason, CancellationToken cancellationToken = default)
    {
        await EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var motif = ProductGroupManagementRules.Reason(reason);
        var actor = await ActorAsync(cancellationToken).ConfigureAwait(false);
        var (sub, target, members, steps) = await WriteAsync(async (connection, transaction) =>
        {
            var (subcategoryId, version, archived) = await LockFamilyAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false);
            if (version != original.Version) throw new ProductOperationException("Familia a fost modificată între timp. Actualizează lista și reia operația.");
            if (archived) throw new ProductOperationException(ParameterArchiveRules.AlreadyArchivedMessage);
            var memberIds = await FamilyMemberIdsAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false);
            var infos = await ReadInfosAsync(connection, transaction, memberIds, true, cancellationToken).ConfigureAwait(false);
            var done = await ArchiveCoreAsync(connection, transaction, infos, actor, motif, cancellationToken).ConfigureAwait(false);
            await using (var mark = Command(connection, transaction, "UPDATE parameter_families SET archived_utc=@now,archived_by=@by,version=version+1 WHERE id=@id",
                             ("@now", MariaTimeText.Format(DateTime.UtcNow)), ("@by", actor), ("@id", original.Id)))
                await mark.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return (subcategoryId, infos[0].Target, string.Join(", ", infos.Select(info => info.Name)), done);
        }, cancellationToken).ConfigureAwait(false);
        await AuditArchiveAsync(AuditActions.ArchiveParameterFamily, sub, target, [("Familie", original.Name), ("Parametri", members)], steps, motif, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<(int SubcategoryId, long Version, bool Archived)> LockFamilyAsync(MySqlConnection connection, MySqlTransaction transaction, int id, CancellationToken token)
    {
        await using var command = Command(connection, transaction, "SELECT COALESCE(subcategory_id,-category_id),version,archived_utc FROM parameter_families WHERE id=@id FOR UPDATE", ("@id", id));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false)) throw new ProductOperationException("Familia nu mai există. Actualizează lista și reia operația.");
        return (reader.IsDBNull(0) ? 0 : checked((int)reader.GetInt64(0)), reader.GetInt64(1), !reader.IsDBNull(2));
    }

    private static async Task<List<int>> FamilyMemberIdsAsync(MySqlConnection connection, MySqlTransaction transaction, int familyId, CancellationToken token)
    {
        var ids = new List<int>();
        await using var command = Command(connection, transaction, "SELECT id FROM subcategory_parameters WHERE family_id=@id ORDER BY position,id FOR UPDATE", ("@id", familyId));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false)) ids.Add(checked((int)reader.GetInt64(0)));
        return ids;
    }

    // ---- restore -------------------------------------------------------------------------------------------------------------

    private static async Task<List<RestoreStep>> ComputeRestoreAsync(MySqlConnection connection, MySqlTransaction? transaction, IReadOnlyList<ParamInfo> infos, bool lockRows, CancellationToken token)
    {
        var (list, listParameters) = InList(infos.Select(info => info.Id).ToList(), "r");
        var steps = new List<RestoreStep>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var info in infos)
        {
            var links = new List<(int ProductId, string Name, int Quantity, string BaseModel, string Baked, string Value, int Subcategory, int Category)>();
            await using (var select = Command(connection, transaction, $"""
                SELECT p.id,p.name,p.quantity,COALESCE(p.base_model,p.name),COALESCE(pv.baked_text,''),v.value,p.subcategory_id,p.category_id FROM product_parameter_values pv
                INNER JOIN products p ON p.id=pv.product_id INNER JOIN parameter_values v ON v.id=pv.value_id
                WHERE pv.parameter_id=@id AND pv.archived=1 ORDER BY p.name{(lockRows ? " FOR UPDATE" : "")}
                """, ("@id", info.Id)))
            await using (var reader = await select.ExecuteReaderAsync(token).ConfigureAwait(false))
                while (await reader.ReadAsync(token).ConfigureAwait(false))
                    links.Add((checked((int)reader.GetInt64(0)), reader.GetString(1), checked((int)reader.GetInt64(2)), reader.GetString(3), reader.GetString(4), reader.GetString(5),
                        checked((int)reader.GetInt64(6)), checked((int)reader.GetInt64(7))));
            foreach (var link in links)
            {
                var display = ProductParameterRules.Display(info.Unit, link.Value);
                var chain = await SubcategoryAncestry.ChainAsync(connection, transaction, link.Subcategory, token).ConfigureAwait(false);
                if (info.SubcategoryId < 0 ? link.Category != -info.SubcategoryId : !chain.Contains(info.SubcategoryId))
                {
                    steps.Add(new RestoreStep(new ParameterRestoreItem(link.ProductId, link.Name, link.BaseModel, link.BaseModel, link.Name, RestoreProblem.OutOfScope), link.Quantity, [],
                        info.Id, info.Name, display, info.Target));
                    continue;
                }
                var stripped = link.Baked.Length == 0 ? link.BaseModel : ParameterArchiveRules.StripBaked(link.BaseModel, link.Baked);
                var problem = stripped is null ? RestoreProblem.BakedTextMissing : RestoreProblem.None;
                var proposed = stripped ?? link.BaseModel;
                var displays = (await ProductDisplaysAsync(connection, transaction, link.ProductId, $"pv.archived=0 OR pv.parameter_id IN ({list})", listParameters).ConfigureAwait(false))
                    .Select(entry => entry.Display).ToList();
                var name = ParameterArchiveRules.NewName(proposed, displays);
                if (name.Length > ProductParameterRules.ProductNameMaximumLength)
                    problem = problem == RestoreProblem.None ? RestoreProblem.NameTooLong : problem;
                else
                {
                    var free = false;
                    for (var attempt = 1; attempt < 100 && !free; attempt++)
                    {
                        var candidateBase = attempt == 1 ? proposed : $"{proposed} {attempt}";
                        var candidate = ParameterArchiveRules.NewName(candidateBase, displays);
                        if (candidate.Length > ProductParameterRules.ProductNameMaximumLength) break;
                        var key = TextNormalization.UniquenessKey(candidate);
                        if (seen.Contains(key) || await NameTakenByAsync(connection, transaction, candidate, link.ProductId, token).ConfigureAwait(false) is not null)
                        {
                            if (problem == RestoreProblem.None) problem = RestoreProblem.NameClash;
                            continue;
                        }
                        (proposed, name, free) = (candidateBase, candidate, true);
                    }
                }
                seen.Add(TextNormalization.UniquenessKey(name));
                steps.Add(new RestoreStep(new ParameterRestoreItem(link.ProductId, link.Name, link.BaseModel, proposed, name, problem), link.Quantity, displays,
                    info.Id, info.Name, display, info.Target));
            }
        }
        return steps;
    }

    public async Task<ParameterRestorePreview> PreviewParameterRestoreAsync(ArchivedParameter parameter, CancellationToken cancellationToken = default)
    {
        await EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        var infos = await ReadInfosAsync(connection, null, [parameter.Id], false, cancellationToken).ConfigureAwait(false);
        return new ParameterRestorePreview((await ComputeRestoreAsync(connection, null, infos, false, cancellationToken).ConfigureAwait(false)).Select(step => step.Item).ToList());
    }

    public async Task<ParameterRestorePreview> PreviewFamilyRestoreAsync(ArchivedFamily family, CancellationToken cancellationToken = default)
    {
        await EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        var infos = await ReadInfosAsync(connection, null, family.ParameterIds, false, cancellationToken).ConfigureAwait(false);
        return new ParameterRestorePreview((await ComputeRestoreAsync(connection, null, infos, false, cancellationToken).ConfigureAwait(false)).Select(step => step.Item).ToList());
    }

    private async Task<List<RestoreStep>> RestoreCoreAsync(MySqlConnection connection, MySqlTransaction transaction, IReadOnlyList<ParamInfo> infos,
        IReadOnlyDictionary<int, string> baseModels, string actor, string reason, CancellationToken token)
    {
        var steps = await ComputeRestoreAsync(connection, transaction, infos, true, token).ConfigureAwait(false);
        if (steps.FirstOrDefault(step => step.Item.Problem == RestoreProblem.OutOfScope) is { } outside)
            throw new ProductOperationException($"Produsul «{outside.Item.OldName}» nu mai aparține subcategoriei parametrului. Restaurarea nu se poate face; renunță la ea după perioada de păstrare.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < steps.Count; index++)
        {
            var step = steps[index];
            var item = step.Item;
            string chosen;
            if (baseModels.TryGetValue(item.ProductId, out var typed)) chosen = ProductParameterRules.BaseModel(typed);
            else if (item.Problem != RestoreProblem.None) throw new ProductOperationException($"Produsul «{item.OldName}» cere o decizie: alege redenumirea automată sau scrie modelul.");
            else chosen = item.ProposedBaseModel;
            var name = ProductParameterRules.CheckNameLength(ParameterArchiveRules.NewName(chosen, step.Displays));
            if (await NameTakenByAsync(connection, transaction, name, item.ProductId, token).ConfigureAwait(false) is { } clash)
                throw new ProductOperationException($"Numele «{name}» există deja ca «{clash}». Alege alt model pentru «{item.OldName}».");
            if (!seen.Add(TextNormalization.UniquenessKey(name))) throw new ProductOperationException($"Numele «{name}» rezultă pentru două produse. Alege alt model.");
            await using (var rename = Command(connection, transaction, "UPDATE products SET base_model=@model,name=@name,normalized_name=@key,version=version+1 WHERE id=@id",
                             ("@model", chosen), ("@name", name), ("@key", TextNormalization.UniquenessKey(name)), ("@id", item.ProductId)))
                await rename.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            await using (var link = Command(connection, transaction, "UPDATE product_parameter_values SET archived=0,baked_text=NULL,baked_position=NULL WHERE product_id=@product AND parameter_id=@parameter",
                             ("@product", item.ProductId), ("@parameter", step.ParameterId)))
                await link.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            await ParameterUsageLog.WriteAsync(connection, transaction, step.ParameterId, step.ParameterName, step.Display, item.ProductId, name, step.Node,
                ParameterUsageActions.Restored, actor, reason, token).ConfigureAwait(false);
            steps[index] = step with { Item = item with { NewName = name } };
        }
        foreach (var info in infos)
        {
            await using var mark = Command(connection, transaction, "UPDATE subcategory_parameters SET archived_utc=NULL,archived_by=NULL,version=version+1 WHERE id=@id", ("@id", info.Id));
            await mark.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }
        return steps;
    }

    private async Task AuditRestoreAsync(string action, int subcategoryId, string target, IEnumerable<(string, string)> identification, List<RestoreStep> steps, string motif, CancellationToken token)
    {
        await AuditRecorder.RecordActionAsync(auditTrail, accessControl, OwnerKey.Entity(subcategoryId), action, OwnerKey.Id(subcategoryId), target,
            AuditDetails.Identification([.. identification, ("Produse redenumite", steps.Count.ToString(CultureInfo.InvariantCulture))]), motif, token).ConfigureAwait(false);
        foreach (var step in steps)
            await AuditRecorder.RecordEditAsync(auditTrail, accessControl, AuditEntities.Product, step.Item.ProductId.ToString(CultureInfo.InvariantCulture), step.Item.NewName,
                [new(ProductCode.Label, step.Item.OldName, step.Item.NewName)], motif, token, AuditActions.RenameProductByParameter).ConfigureAwait(false);
    }

    public async Task RestoreParameterAsync(ArchivedParameter original, IReadOnlyDictionary<int, string> baseModels, string reason, CancellationToken cancellationToken = default)
    {
        await EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var motif = ProductGroupManagementRules.Reason(reason);
        var actor = await ActorAsync(cancellationToken).ConfigureAwait(false);
        var (info, steps) = await WriteAsync(async (connection, transaction) =>
        {
            var infos = await ReadInfosAsync(connection, transaction, [original.Id], true, cancellationToken).ConfigureAwait(false);
            if (infos[0].Version != original.Version) throw new ProductOperationException("Parametrul a fost modificat între timp. Actualizează lista și reia operația.");
            if (infos[0].ArchivedUtc is null) throw new ProductOperationException(ParameterArchiveRules.NotArchivedMessage);
            if (infos[0].FamilyId is not null) throw new ProductOperationException(ParameterArchiveRules.FamilyMemberMessage);
            return (infos[0], await RestoreCoreAsync(connection, transaction, infos, baseModels, actor, motif, cancellationToken).ConfigureAwait(false));
        }, cancellationToken).ConfigureAwait(false);
        await AuditRestoreAsync(AuditActions.RestoreSubcategoryParameter, info.SubcategoryId, info.Target, [("Parametru", info.Name)], steps, motif, cancellationToken).ConfigureAwait(false);
    }

    public async Task RestoreFamilyAsync(ArchivedFamily original, IReadOnlyDictionary<int, string> baseModels, string reason, CancellationToken cancellationToken = default)
    {
        await EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var motif = ProductGroupManagementRules.Reason(reason);
        var actor = await ActorAsync(cancellationToken).ConfigureAwait(false);
        var (sub, target, members, steps) = await WriteAsync(async (connection, transaction) =>
        {
            var (subcategoryId, version, archived) = await LockFamilyAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false);
            if (version != original.Version) throw new ProductOperationException("Familia a fost modificată între timp. Actualizează lista și reia operația.");
            if (!archived) throw new ProductOperationException(ParameterArchiveRules.NotArchivedMessage);
            var infos = await ReadInfosAsync(connection, transaction, await FamilyMemberIdsAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false), true, cancellationToken).ConfigureAwait(false);
            var done = await RestoreCoreAsync(connection, transaction, infos, baseModels, actor, motif, cancellationToken).ConfigureAwait(false);
            await using (var mark = Command(connection, transaction, "UPDATE parameter_families SET archived_utc=NULL,archived_by=NULL,version=version+1 WHERE id=@id", ("@id", original.Id)))
                await mark.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return (subcategoryId, infos[0].Target, string.Join(", ", infos.Select(info => info.Name)), done);
        }, cancellationToken).ConfigureAwait(false);
        await AuditRestoreAsync(AuditActions.RestoreParameterFamily, sub, target, [("Familie", original.Name), ("Parametri", members)], steps, motif, cancellationToken).ConfigureAwait(false);
    }

    // ---- giving up the restore, deleting unused definitions ------------------------------------------------------------------------------

    private static async Task<int> GiveUpCoreAsync(MySqlConnection connection, MySqlTransaction transaction, IReadOnlyList<ParamInfo> infos, string actor, string reason, CancellationToken token)
    {
        var retention = await MariaCatalogSettings.ReadRetentionAsync(connection, transaction, token).ConfigureAwait(false);
        var dropped = 0;
        foreach (var info in infos)
        {
            var archivedAt = MariaTimeText.Parse(info.ArchivedUtc ?? throw new ProductOperationException(ParameterArchiveRules.NotArchivedMessage));
            var allowed = ParameterArchiveRules.GiveUpAllowedFrom(archivedAt, retention);
            if (DateTime.UtcNow < allowed) throw new ProductOperationException(ParameterArchiveRules.GiveUpTooEarlyMessage(allowed));
            await using (var select = Command(connection, transaction, """
                SELECT p.id,p.name,COALESCE(pv.baked_text,v.value) FROM product_parameter_values pv
                INNER JOIN products p ON p.id=pv.product_id INNER JOIN parameter_values v ON v.id=pv.value_id
                WHERE pv.parameter_id=@id AND pv.archived=1 FOR UPDATE
                """, ("@id", info.Id)))
            await using (var reader = await select.ExecuteReaderAsync(token).ConfigureAwait(false))
            {
                var rows = new List<(int, string, string)>();
                while (await reader.ReadAsync(token).ConfigureAwait(false)) rows.Add((checked((int)reader.GetInt64(0)), reader.GetString(1), reader.GetString(2)));
                await reader.CloseAsync().ConfigureAwait(false);
                foreach (var (productId, productName, text) in rows)
                    await ParameterUsageLog.WriteAsync(connection, transaction, info.Id, info.Name, text, productId, productName, info.Target,
                        ParameterUsageActions.GivenUp, actor, reason, token).ConfigureAwait(false);
                dropped += rows.Count;
            }
            await using var drop = Command(connection, transaction, "DELETE FROM product_parameter_values WHERE parameter_id=@id AND archived=1", ("@id", info.Id));
            await drop.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }
        return dropped;
    }

    public async Task GiveUpParameterRestoreAsync(ArchivedParameter original, string reason, CancellationToken cancellationToken = default)
    {
        await EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var motif = ProductGroupManagementRules.Reason(reason);
        var actor = await ActorAsync(cancellationToken).ConfigureAwait(false);
        var (info, dropped) = await WriteAsync(async (connection, transaction) =>
        {
            var infos = await ReadInfosAsync(connection, transaction, [original.Id], true, cancellationToken).ConfigureAwait(false);
            if (infos[0].Version != original.Version) throw new ProductOperationException("Parametrul a fost modificat între timp. Actualizează lista și reia operația.");
            if (infos[0].FamilyId is not null) throw new ProductOperationException(ParameterArchiveRules.FamilyMemberMessage);
            return (infos[0], await GiveUpCoreAsync(connection, transaction, infos, actor, motif, cancellationToken).ConfigureAwait(false));
        }, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordActionAsync(auditTrail, accessControl, OwnerKey.Entity(info.SubcategoryId), AuditActions.GiveUpParameterRestore, OwnerKey.Id(info.SubcategoryId),
            info.Target, AuditDetails.Identification(("Parametru", info.Name), ("Legături eliminate", dropped.ToString(CultureInfo.InvariantCulture))), motif, cancellationToken).ConfigureAwait(false);
    }

    public async Task GiveUpFamilyRestoreAsync(ArchivedFamily original, string reason, CancellationToken cancellationToken = default)
    {
        await EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var motif = ProductGroupManagementRules.Reason(reason);
        var actor = await ActorAsync(cancellationToken).ConfigureAwait(false);
        var (sub, target, dropped) = await WriteAsync(async (connection, transaction) =>
        {
            var (subcategoryId, version, archived) = await LockFamilyAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false);
            if (version != original.Version) throw new ProductOperationException("Familia a fost modificată între timp. Actualizează lista și reia operația.");
            if (!archived) throw new ProductOperationException(ParameterArchiveRules.NotArchivedMessage);
            var infos = await ReadInfosAsync(connection, transaction, await FamilyMemberIdsAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false), true, cancellationToken).ConfigureAwait(false);
            return (subcategoryId, infos[0].Target, await GiveUpCoreAsync(connection, transaction, infos, actor, motif, cancellationToken).ConfigureAwait(false));
        }, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordActionAsync(auditTrail, accessControl, OwnerKey.Entity(sub), AuditActions.GiveUpFamilyRestore, OwnerKey.Id(sub),
            target, AuditDetails.Identification(("Familie", original.Name), ("Legături eliminate", dropped.ToString(CultureInfo.InvariantCulture))), motif, cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnsureNoLinksAsync(MySqlConnection connection, MySqlTransaction transaction, IReadOnlyList<int> ids, CancellationToken token)
    {
        var (list, parameters) = InList(ids, "n");
        await using var linked = Command(connection, transaction, $"SELECT COUNT(*) FROM product_parameter_values WHERE parameter_id IN ({list})", parameters);
        if (Convert.ToInt64(await linked.ExecuteScalarAsync(token).ConfigureAwait(false), CultureInfo.InvariantCulture) > 0)
            throw new ProductOperationException(ParameterArchiveRules.StillLinkedMessage);
    }

    public async Task DeleteArchivedParameterAsync(ArchivedParameter original, string reason, CancellationToken cancellationToken = default)
    {
        await EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var motif = ProductGroupManagementRules.Reason(reason);
        var actor = await ActorAsync(cancellationToken).ConfigureAwait(false);
        var info = await WriteAsync(async (connection, transaction) =>
        {
            var infos = await ReadInfosAsync(connection, transaction, [original.Id], true, cancellationToken).ConfigureAwait(false);
            if (infos[0].Version != original.Version) throw new ProductOperationException("Parametrul a fost modificat între timp. Actualizează lista și reia operația.");
            if (infos[0].ArchivedUtc is null) throw new ProductOperationException(ParameterArchiveRules.NotArchivedMessage);
            if (infos[0].FamilyId is not null) throw new ProductOperationException(ParameterArchiveRules.FamilyMemberMessage);
            await EnsureNoLinksAsync(connection, transaction, [original.Id], cancellationToken).ConfigureAwait(false);
            await ParameterUsageLog.WriteAsync(connection, transaction, original.Id, infos[0].Name, "", null, "", infos[0].Target, ParameterUsageActions.Deleted, actor, motif, cancellationToken).ConfigureAwait(false);
            await using var delete = Command(connection, transaction, "DELETE FROM subcategory_parameters WHERE id=@id", ("@id", original.Id));
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return infos[0];
        }, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordActionAsync(auditTrail, accessControl, OwnerKey.Entity(info.SubcategoryId), AuditActions.DeleteUnusedParameter, OwnerKey.Id(info.SubcategoryId),
            info.Target, AuditDetails.Identification(("Parametru", info.Name), ("Valori șterse", original.Values.Count.ToString(CultureInfo.InvariantCulture))), motif, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteArchivedFamilyAsync(ArchivedFamily original, string reason, CancellationToken cancellationToken = default)
    {
        await EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var motif = ProductGroupManagementRules.Reason(reason);
        var actor = await ActorAsync(cancellationToken).ConfigureAwait(false);
        var (sub, target, members) = await WriteAsync(async (connection, transaction) =>
        {
            var (subcategoryId, version, archived) = await LockFamilyAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false);
            if (version != original.Version) throw new ProductOperationException("Familia a fost modificată între timp. Actualizează lista și reia operația.");
            if (!archived) throw new ProductOperationException(ParameterArchiveRules.NotArchivedMessage);
            var memberIds = await FamilyMemberIdsAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false);
            var infos = await ReadInfosAsync(connection, transaction, memberIds, true, cancellationToken).ConfigureAwait(false);
            await EnsureNoLinksAsync(connection, transaction, memberIds, cancellationToken).ConfigureAwait(false);
            foreach (var info in infos)
                await ParameterUsageLog.WriteAsync(connection, transaction, info.Id, info.Name, "", null, "", info.Target, ParameterUsageActions.Deleted, actor, motif, cancellationToken).ConfigureAwait(false);
            var (list, parameters) = InList(memberIds, "d");
            await using (var members = Command(connection, transaction, $"DELETE FROM subcategory_parameters WHERE id IN ({list})", parameters))
                await members.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await using (var family = Command(connection, transaction, "DELETE FROM parameter_families WHERE id=@id", ("@id", original.Id)))
                await family.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return (subcategoryId, infos[0].Target, string.Join(", ", infos.Select(info => info.Name)));
        }, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordActionAsync(auditTrail, accessControl, OwnerKey.Entity(sub), AuditActions.DeleteUnusedFamily, OwnerKey.Id(sub),
            target, AuditDetails.Identification(("Familie", original.Name), ("Parametri șterși", members)), motif, cancellationToken).ConfigureAwait(false);
    }

    // ---- reads -----------------------------------------------------------------------------------------------------------------------

    public async Task<ArchivedParameters> GetArchivedAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        var rows = new List<(int Id, string Category, string Subcategory, string Name, string Unit, ParameterKind Kind, long Version, int? FamilyId, string? FamilyName, string At, string By, int Count)>();
        await using (var command = Command(connection, null, """
            SELECT sp.id,c.name,COALESCE(s.name,''),sp.name,sp.unit,sp.kind,sp.version,sp.family_id,f.name,sp.archived_utc,COALESCE(sp.archived_by,''),
                   (SELECT COUNT(DISTINCT pv.product_id) FROM product_parameter_values pv WHERE pv.parameter_id=sp.id)
            FROM subcategory_parameters sp LEFT JOIN subcategories s ON s.id=sp.subcategory_id INNER JOIN categories c ON c.id=COALESCE(s.category_id,sp.category_id)
            LEFT JOIN parameter_families f ON f.id=sp.family_id
            WHERE sp.archived_utc IS NOT NULL ORDER BY c.name,COALESCE(s.name,''),sp.position,sp.id
            """))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                rows.Add((checked((int)reader.GetInt64(0)), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), (ParameterKind)reader.GetInt32(5), reader.GetInt64(6),
                    reader.IsDBNull(7) ? null : checked((int)reader.GetInt64(7)), reader.IsDBNull(8) ? null : reader.GetString(8), reader.GetString(9), reader.GetString(10), checked((int)reader.GetInt64(11))));
        var values = new Dictionary<int, List<string>>();
        if (rows.Count > 0)
        {
            var (list, parameters) = InList(rows.Select(row => row.Id).ToList(), "g");
            await using var select = Command(connection, null, $"SELECT parameter_id,value FROM parameter_values WHERE parameter_id IN ({list}) ORDER BY value", parameters);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var id = checked((int)reader.GetInt64(0));
                if (!values.TryGetValue(id, out var list2)) values[id] = list2 = [];
                list2.Add(reader.GetString(1));
            }
        }
        var parametersOut = rows.Select(row => new ArchivedParameter(row.Id, row.Category, row.Subcategory, row.Name, row.Unit, row.Kind, row.Version, row.FamilyId, row.FamilyName,
            row.At, row.By, row.Count, values.GetValueOrDefault(row.Id) ?? [])).ToList();
        var families = new List<ArchivedFamily>();
        await using (var command = Command(connection, null, """
            SELECT f.id,c.name,COALESCE(s.name,''),f.name,f.version,f.archived_utc,COALESCE(f.archived_by,''),
                   (SELECT COUNT(DISTINCT pv.product_id) FROM product_parameter_values pv INNER JOIN subcategory_parameters m ON m.id=pv.parameter_id WHERE m.family_id=f.id)
            FROM parameter_families f LEFT JOIN subcategories s ON s.id=f.subcategory_id INNER JOIN categories c ON c.id=COALESCE(s.category_id,f.category_id)
            WHERE f.archived_utc IS NOT NULL ORDER BY c.name,COALESCE(s.name,''),f.position,f.id
            """))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var id = checked((int)reader.GetInt64(0));
                families.Add(new ArchivedFamily(id, reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt64(4), reader.GetString(5), reader.GetString(6),
                    parametersOut.Where(parameter => parameter.FamilyId == id).Select(parameter => parameter.Id).ToList(), checked((int)reader.GetInt64(7))));
            }
        return new ArchivedParameters(parametersOut, families);
    }

    public async Task<IReadOnlyList<ParameterUsageEntry>> GetUsageAsync(IReadOnlyList<int> parameterIds, CancellationToken cancellationToken = default)
    {
        if (parameterIds.Count == 0) return [];
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        var (list, parameters) = InList(parameterIds, "u");
        await using var command = Command(connection, null, $"""
            SELECT id,definition_id,parameter_name,value_text,product_id,product_name,node_path,action,actor,reason,occurred_utc
            FROM parameter_usage_log WHERE definition_kind='parameter' AND definition_id IN ({list}) ORDER BY id DESC
            """, parameters);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<ParameterUsageEntry>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new ParameterUsageEntry(reader.GetInt64(0), checked((int)reader.GetInt64(1)), reader.GetString(2), reader.GetString(3),
                reader.IsDBNull(4) ? null : checked((int)reader.GetInt64(4)), reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetString(8), reader.GetString(9), reader.GetString(10)));
        return result;
    }
}
