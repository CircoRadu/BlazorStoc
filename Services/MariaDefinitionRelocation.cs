using System.Globalization;
using MySqlConnector;
using static BlazorStoc.Services.MariaDb;

namespace BlazorStoc.Services;

// Executes the plan of RelocationPlanner on MariaDB (see DefinitionRelocation.cs). The preview runs the whole relocation in a serializable transaction and rolls it back, so the
// products that would become incomplete are measured on the real result; the apply commits the same work, after the checks, and journals each definition.
public sealed class MariaDefinitionRelocation(IConfiguration configuration, IAccessControl? accessControl = null, IAuditTrail? auditTrail = null) : IDefinitionRelocation
{
    private sealed class DryRunComplete(RelocationPreview preview) : Exception("Dry run")
    {
        public RelocationPreview Preview { get; } = preview;
    }

    public async Task<RelocationPreview> PreviewAsync(RelocationRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await WriteAsync<bool>(async (connection, transaction) =>
            {
                var (preview, _, _, _) = await RunAsync(connection, transaction, request, "", "", cancellationToken).ConfigureAwait(false);
                throw new DryRunComplete(preview);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (DryRunComplete done) { return done.Preview; }
        throw new InvalidOperationException("Previzualizarea nu s-a încheiat.");
    }

    public async Task<RelocationPreview> ApplyAsync(RelocationRequest request, string reason, CancellationToken cancellationToken = default)
    {
        await EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var motif = ProductGroupManagementRules.Reason(reason);
        var actor = accessControl is null ? "" : await accessControl.GetUsernameAsync(cancellationToken).ConfigureAwait(false) ?? "";
        var (preview, target, snapshot, plan) = await WriteAsync(async (connection, transaction) =>
        {
            var result = await RunAsync(connection, transaction, request, actor, motif, cancellationToken).ConfigureAwait(false);
            if (result.Preview.Blocks.Count > 0) throw new ProductOperationException(result.Preview.Blocks[0]);
            if (result.Preview.Items.FirstOrDefault(item => item.Action == RelocationAction.NeedsDecision) is { } undecided) throw new ProductOperationException(RelocationMessages.Undecided(undecided.Name));
            if (result.Preview.Items.FirstOrDefault(item => item.Action == RelocationAction.Blocked) is { } blocked) throw new ProductOperationException(blocked.Note);
            if (!result.Preview.CanApply) throw new ProductOperationException(RelocationMessages.NothingToDo);
            if (result.Preview.NeedsConfirmation && !request.AcceptIncomplete) throw new ProductOperationException(RelocationMessages.NotAccepted(result.Preview.Incomplete.Count));
            return result;
        }, cancellationToken).ConfigureAwait(false);
        var key = OwnerKey.Of(target);
        foreach (var item in preview.Items)
            await AuditRecorder.RecordActionAsync(auditTrail, accessControl, OwnerKey.Entity(key), item.Action == RelocationAction.Move ? AuditActions.RelocateDefinition : AuditActions.MergeDefinitions,
                OwnerKey.Id(key), target.Target, AuditDetails.Identification(("Definiție", item.Name), ("Tip", item.Ref.Kind switch { DefinitionKind.Parameter => "Parametru", DefinitionKind.Family => "Familie", _ => "Etichetă" }),
                    ("De la", item.From), ("La", item.To), ("Îmbină", item.Absorbs.Count == 0 ? "—" : string.Join("; ", item.Absorbs)), ("Produse incomplete după mutare", preview.Incomplete.Count.ToString(CultureInfo.InvariantCulture))),
                motif, cancellationToken).ConfigureAwait(false);
        _ = snapshot; _ = plan;
        return preview;
    }

    private async Task<(RelocationPreview Preview, DefinitionOwner Target, DefSnapshot Snapshot, RelocationPlan Plan)> RunAsync(MySqlConnection connection, MySqlTransaction transaction,
        RelocationRequest request, string actor, string reason, CancellationToken token)
    {
        var target = await DefinitionOwners.FindAsync(connection, transaction, request.Target, token).ConfigureAwait(false);
        var snapshot = await LoadAsync(connection, transaction, token).ConfigureAwait(false);
        var plan = RelocationPlanner.Plan(snapshot, request, OwnerKey.Of(target));
        if (plan.Blocks.Count > 0 || plan.Items.Any(item => item.Action is RelocationAction.NeedsDecision or RelocationAction.Blocked) || plan.Items.Count == 0)
            return (new RelocationPreview(plan.Items, plan.Blocks, []), target, snapshot, plan);
        var before = await MariaProductParameterRepository.IncompleteIdsAsync(connection, transaction, token).ConfigureAwait(false);
        foreach (var operation in plan.Operations) await ExecuteAsync(connection, transaction, operation, actor, reason, token).ConfigureAwait(false);
        var after = await MariaProductParameterRepository.IncompleteIdsAsync(connection, transaction, token).ConfigureAwait(false);
        var incomplete = new List<IncompleteProduct>();
        var newly = after.Where(id => !before.Contains(id)).ToList();
        if (newly.Count > 0)
        {
            var names = new Dictionary<int, string>();
            await using var select = Command(connection, transaction, $"SELECT id,name FROM products WHERE id IN ({SubcategoryAncestry.InList(newly)}) ORDER BY name");
            await using var reader = await select.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false)) incomplete.Add(new IncompleteProduct(checked((int)reader.GetInt64(0)), reader.GetString(1)));
        }
        return (new RelocationPreview(plan.Items, plan.Blocks, incomplete), target, snapshot, plan);
    }

    // ---- snapshot ------------------------------------------------------------------------------------------------------------

    internal static async Task<DefSnapshot> LoadAsync(MySqlConnection connection, MySqlTransaction transaction, CancellationToken token)
    {
        var parents = await SubcategoryAncestry.ParentsAsync(connection, transaction, token).ConfigureAwait(false);
        var categoryOf = new Dictionary<int, int>();
        var names = new Dictionary<int, string>();
        await using (var command = Command(connection, transaction, "SELECT id,category_id,name FROM subcategories"))
        await using (var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                var id = checked((int)reader.GetInt64(0));
                categoryOf[id] = checked((int)reader.GetInt64(1));
                names[id] = reader.GetString(2);
            }
        await using (var command = Command(connection, transaction, "SELECT id,name FROM categories"))
        await using (var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            while (await reader.ReadAsync(token).ConfigureAwait(false)) names[-checked((int)reader.GetInt64(0))] = reader.GetString(1);

        var values = new Dictionary<int, List<SnapValue>>();
        await using (var command = Command(connection, transaction, "SELECT id,parameter_id,value,normalized_value,(SELECT COUNT(*) FROM product_parameter_values pv WHERE pv.value_id=v.id) FROM parameter_values v ORDER BY id"))
        await using (var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                var parameterId = checked((int)reader.GetInt64(1));
                if (!values.TryGetValue(parameterId, out var list)) values[parameterId] = list = [];
                list.Add(new SnapValue(checked((int)reader.GetInt64(0)), reader.GetString(2), reader.GetString(3), checked((int)reader.GetInt64(4))));
            }
        var parameters = new List<SnapParameter>();
        await using (var command = Command(connection, transaction, """
            SELECT id,name,normalized_name,unit,kind,COALESCE(subcategory_id,-category_id),family_id,archived_utc IS NOT NULL,
                   (SELECT COUNT(*) FROM product_parameter_values pv WHERE pv.parameter_id=sp.id)
            FROM subcategory_parameters sp
            """))
        await using (var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                var id = checked((int)reader.GetInt64(0));
                parameters.Add(new SnapParameter(id, reader.GetString(1), reader.GetString(2), reader.GetString(3), (ParameterKind)reader.GetInt32(4), checked((int)reader.GetInt64(5)),
                    reader.IsDBNull(6) ? null : checked((int)reader.GetInt64(6)), reader.GetBoolean(7), checked((int)reader.GetInt64(8)), values.GetValueOrDefault(id) ?? []));
            }
        var families = new List<SnapFamily>();
        await using (var command = Command(connection, transaction, "SELECT id,name,normalized_name,COALESCE(subcategory_id,-category_id),archived_utc IS NOT NULL FROM parameter_families"))
        await using (var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            while (await reader.ReadAsync(token).ConfigureAwait(false))
                families.Add(new SnapFamily(checked((int)reader.GetInt64(0)), reader.GetString(1), reader.GetString(2), checked((int)reader.GetInt64(3)), reader.GetBoolean(4)));
        var tags = new List<SnapTag>();
        await using (var command = Command(connection, transaction, """
            SELECT id,name,normalized_name,COALESCE(subcategory_id,-category_id),archived_utc IS NOT NULL,(SELECT COUNT(*) FROM product_tags pt WHERE pt.tag_id=t.id) FROM subcategory_tags t
            """))
        await using (var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            while (await reader.ReadAsync(token).ConfigureAwait(false))
                tags.Add(new SnapTag(checked((int)reader.GetInt64(0)), reader.GetString(1), reader.GetString(2), checked((int)reader.GetInt64(3)), reader.GetBoolean(4), checked((int)reader.GetInt64(5))));
        return new DefSnapshot(parents, categoryOf, names, parameters, families, tags);
    }

    // ---- execution -------------------------------------------------------------------------------------------------------------

    private static (object? Subcategory, object? Category) Columns(int owner) => owner > 0 ? (owner, null) : (null, -owner);

    private static async Task<int> NextPositionAsync(MySqlConnection connection, MySqlTransaction transaction, string table, int owner, CancellationToken token)
    {
        await using var command = Command(connection, transaction, $"SELECT COALESCE(MAX(position),0)+1 FROM {table} WHERE {(owner > 0 ? "subcategory_id" : "category_id")}=@owner", ("@owner", Math.Abs(owner)));
        return Convert.ToInt32(await command.ExecuteScalarAsync(token).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    internal static async Task ExecuteAsync(MySqlConnection connection, MySqlTransaction transaction, RelocationOp operation, string actor, string reason, CancellationToken token)
    {
        switch (operation)
        {
            case MoveOp { Kind: DefinitionKind.Parameter } move:
            {
                var (sub, cat) = Columns(move.ToOwner);
                var position = await NextPositionAsync(connection, transaction, "subcategory_parameters", move.ToOwner, token).ConfigureAwait(false);
                await using (var update = Command(connection, transaction, "UPDATE subcategory_parameters SET subcategory_id=@sub,category_id=@cat,position=@position,version=version+1 WHERE id=@id",
                                 ("@sub", sub), ("@cat", cat), ("@position", position), ("@id", move.Id)))
                    await update.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                await ParameterUsageLog.WriteRelocationAsync(connection, transaction, move.Id, move.Id, ParameterUsageActions.Relocated, actor, reason, token).ConfigureAwait(false);
                break;
            }
            case MoveOp { Kind: DefinitionKind.Family } move:
            {
                var (sub, cat) = Columns(move.ToOwner);
                var position = await NextPositionAsync(connection, transaction, "parameter_families", move.ToOwner, token).ConfigureAwait(false);
                await using (var update = Command(connection, transaction, "UPDATE parameter_families SET subcategory_id=@sub,category_id=@cat,position=@position,version=version+1 WHERE id=@id",
                                 ("@sub", sub), ("@cat", cat), ("@position", position), ("@id", move.Id)))
                    await update.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                var members = new List<int>();
                await using (var select = Command(connection, transaction, "SELECT id FROM subcategory_parameters WHERE family_id=@id ORDER BY position,id FOR UPDATE", ("@id", move.Id)))
                await using (var reader = await select.ExecuteReaderAsync(token).ConfigureAwait(false))
                    while (await reader.ReadAsync(token).ConfigureAwait(false)) members.Add(checked((int)reader.GetInt64(0)));
                foreach (var member in members)
                {
                    var memberPosition = await NextPositionAsync(connection, transaction, "subcategory_parameters", move.ToOwner, token).ConfigureAwait(false);
                    await using (var update = Command(connection, transaction, "UPDATE subcategory_parameters SET subcategory_id=@sub,category_id=@cat,position=@position,version=version+1 WHERE id=@id",
                                     ("@sub", sub), ("@cat", cat), ("@position", memberPosition), ("@id", member)))
                        await update.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    await ParameterUsageLog.WriteRelocationAsync(connection, transaction, member, member, ParameterUsageActions.Relocated, actor, reason, token).ConfigureAwait(false);
                }
                break;
            }
            case MoveOp { Kind: DefinitionKind.Tag } move:
            {
                var (sub, cat) = Columns(move.ToOwner);
                var position = await NextPositionAsync(connection, transaction, "subcategory_tags", move.ToOwner, token).ConfigureAwait(false);
                await using var update = Command(connection, transaction, "UPDATE subcategory_tags SET subcategory_id=@sub,category_id=@cat,position=@position,version=version+1 WHERE id=@id",
                    ("@sub", sub), ("@cat", cat), ("@position", position), ("@id", move.Id));
                await update.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                break;
            }
            case MergeParameterOp merge:
                await MergeParameterAsync(connection, transaction, merge, actor, reason, token).ConfigureAwait(false);
                break;
            case JoinFamilyOp join:
            {
                var (sub, cat) = Columns(join.ToOwner);
                var position = await NextPositionAsync(connection, transaction, "subcategory_parameters", join.ToOwner, token).ConfigureAwait(false);
                await using (var update = Command(connection, transaction, "UPDATE subcategory_parameters SET family_id=@family,subcategory_id=@sub,category_id=@cat,position=@position,version=version+1 WHERE id=@id",
                                 ("@family", join.FamilyId), ("@sub", sub), ("@cat", cat), ("@position", position), ("@id", join.ParameterId)))
                    await update.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                await ParameterUsageLog.WriteRelocationAsync(connection, transaction, join.ParameterId, join.ParameterId, ParameterUsageActions.Relocated, actor, reason, token).ConfigureAwait(false);
                break;
            }
            case DropFamilyOp drop:
            {
                await using var delete = Command(connection, transaction, "DELETE FROM parameter_families WHERE id=@id", ("@id", drop.FamilyId));
                await delete.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                break;
            }
            case MergeTagOp merge:
            {
                await using (var copy = Command(connection, transaction, "INSERT IGNORE INTO product_tags(product_id,tag_id) SELECT product_id,@survivor FROM product_tags WHERE tag_id=@absorbed",
                                 ("@survivor", merge.SurvivorId), ("@absorbed", merge.AbsorbedId)))
                    await copy.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                await using (var bump = Command(connection, transaction, "UPDATE subcategory_tags SET version=version+1 WHERE id=@id", ("@id", merge.SurvivorId)))
                    await bump.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                await using var delete = Command(connection, transaction, "DELETE FROM subcategory_tags WHERE id=@id", ("@id", merge.AbsorbedId));
                await delete.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                break;
            }
            default:
                throw new InvalidOperationException($"Operație de mutare necunoscută: {operation.GetType().Name}.");
        }
    }


    // ---- moving a branch while keeping its definitions (stage 6): the operations of BranchMovePlanner; the products concerned are those of the branch (the subtree) ----------

    internal static async Task ExecuteBranchAsync(MySqlConnection connection, MySqlTransaction transaction, RelocationOp operation, IReadOnlyList<int> subtree, string actor, string reason, CancellationToken token)
    {
        switch (operation)
        {
            case CopyParameterOp copy:
                await CopyParameterAsync(connection, transaction, copy.SourceId, copy.NodeId, null, subtree, actor, reason, token).ConfigureAwait(false);
                break;
            case CopyFamilyOp copy:
            {
                int position;
                await using (var next = Command(connection, transaction, "SELECT COALESCE(MAX(position),0)+1 FROM parameter_families WHERE subcategory_id=@node", ("@node", copy.NodeId)))
                    position = Convert.ToInt32(await next.ExecuteScalarAsync(token).ConfigureAwait(false), CultureInfo.InvariantCulture);
                await using var insert = Command(connection, transaction, """
                    INSERT INTO parameter_families(subcategory_id,name,normalized_name,position,version) SELECT @node,name,normalized_name,@position,0 FROM parameter_families WHERE id=@source
                    """, ("@node", copy.NodeId), ("@position", position), ("@source", copy.SourceId));
                await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                var familyId = checked((int)insert.LastInsertedId);
                var members = new List<int>();
                await using (var select = Command(connection, transaction, "SELECT id FROM subcategory_parameters WHERE family_id=@id ORDER BY position,id", ("@id", copy.SourceId)))
                await using (var reader = await select.ExecuteReaderAsync(token).ConfigureAwait(false))
                    while (await reader.ReadAsync(token).ConfigureAwait(false)) members.Add(checked((int)reader.GetInt64(0)));
                foreach (var member in members) await CopyParameterAsync(connection, transaction, member, copy.NodeId, familyId, subtree, actor, reason, token).ConfigureAwait(false);
                break;
            }
            case RelinkParameterOp relink:
            {
                var survivorValues = await ValueKeysAsync(connection, transaction, relink.SurvivorId, token).ConfigureAwait(false);
                foreach (var (id, value, key) in await ValuesOfAsync(connection, transaction, relink.SourceId, token).ConfigureAwait(false))
                {
                    if (!survivorValues.TryGetValue(key, out var survivorValue))
                    {
                        await using var insert = Command(connection, transaction, "INSERT INTO parameter_values(parameter_id,value,normalized_value) VALUES(@parameter,@value,@key)",
                            ("@parameter", relink.SurvivorId), ("@value", value), ("@key", key));
                        await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                        survivorValue = checked((int)insert.LastInsertedId);
                        survivorValues[key] = survivorValue;
                    }
                    await RelinkValueAsync(connection, transaction, relink.SourceId, id, relink.SurvivorId, survivorValue, subtree, token).ConfigureAwait(false);
                }
                await using var bump = Command(connection, transaction, "UPDATE subcategory_parameters SET version=version+1 WHERE id=@id", ("@id", relink.SurvivorId));
                await bump.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                await ParameterUsageLog.WriteRelocationAsync(connection, transaction, relink.SurvivorId, relink.SurvivorId, ParameterUsageActions.Merged, actor, reason, token, subtree).ConfigureAwait(false);
                break;
            }
            case CopyTagOp copy:
            {
                int position;
                await using (var next = Command(connection, transaction, "SELECT COALESCE(MAX(position),0)+1 FROM subcategory_tags WHERE subcategory_id=@node", ("@node", copy.NodeId)))
                    position = Convert.ToInt32(await next.ExecuteScalarAsync(token).ConfigureAwait(false), CultureInfo.InvariantCulture);
                await using var insert = Command(connection, transaction, """
                    INSERT INTO subcategory_tags(subcategory_id,name,normalized_name,position,version) SELECT @node,name,normalized_name,@position,0 FROM subcategory_tags WHERE id=@source
                    """, ("@node", copy.NodeId), ("@position", position), ("@source", copy.SourceId));
                await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                await MoveTagLinksAsync(connection, transaction, copy.SourceId, checked((int)insert.LastInsertedId), subtree, token).ConfigureAwait(false);
                break;
            }
            case RelinkTagOp relink:
                await MoveTagLinksAsync(connection, transaction, relink.SourceId, relink.SurvivorId, subtree, token).ConfigureAwait(false);
                await using (var bump = Command(connection, transaction, "UPDATE subcategory_tags SET version=version+1 WHERE id=@id", ("@id", relink.SurvivorId)))
                    await bump.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                break;
            default:
                await ExecuteAsync(connection, transaction, operation, actor, reason, token).ConfigureAwait(false);
                break;
        }
    }

    private static async Task<Dictionary<string, int>> ValueKeysAsync(MySqlConnection connection, MySqlTransaction transaction, int parameterId, CancellationToken token)
    {
        var keys = new Dictionary<string, int>(StringComparer.Ordinal);
        await using var select = Command(connection, transaction, "SELECT id,normalized_value FROM parameter_values WHERE parameter_id=@id", ("@id", parameterId));
        await using var reader = await select.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false)) keys[reader.GetString(1)] = checked((int)reader.GetInt64(0));
        return keys;
    }

    private static async Task<List<(int Id, string Value, string Key)>> ValuesOfAsync(MySqlConnection connection, MySqlTransaction transaction, int parameterId, CancellationToken token)
    {
        var values = new List<(int, string, string)>();
        await using var select = Command(connection, transaction, "SELECT id,value,normalized_value FROM parameter_values WHERE parameter_id=@id ORDER BY id", ("@id", parameterId));
        await using var reader = await select.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false)) values.Add((checked((int)reader.GetInt64(0)), reader.GetString(1), reader.GetString(2)));
        return values;
    }

    // The links of the products of the branch that pointed to a value of the source now point to a value of the other parameter.
    private static async Task RelinkValueAsync(MySqlConnection connection, MySqlTransaction transaction, int sourceParameter, int sourceValue, int targetParameter, int targetValue, IReadOnlyList<int> subtree, CancellationToken token)
    {
        await using var relink = Command(connection, transaction, $"""
            UPDATE product_parameter_values pv INNER JOIN products p ON p.id=pv.product_id
            SET pv.parameter_id=@target,pv.value_id=@targetValue
            WHERE pv.parameter_id=@source AND pv.value_id=@sourceValue AND p.subcategory_id IN ({SubcategoryAncestry.InList(subtree)})
            """, ("@target", targetParameter), ("@targetValue", targetValue), ("@source", sourceParameter), ("@sourceValue", sourceValue));
        await relink.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    // The parameter (with its values) is copied onto the moved node, in the family given when it is the member of a copied family; the products of the branch point to the copy.
    private static async Task CopyParameterAsync(MySqlConnection connection, MySqlTransaction transaction, int sourceId, int nodeId, int? familyId, IReadOnlyList<int> subtree, string actor, string reason, CancellationToken token)
    {
        var position = await NextPositionAsync(connection, transaction, "subcategory_parameters", nodeId, token).ConfigureAwait(false);
        await using var insert = Command(connection, transaction, """
            INSERT INTO subcategory_parameters(subcategory_id,name,normalized_name,unit,kind,position,version,family_id)
            SELECT @node,name,normalized_name,unit,kind,@position,0,@family FROM subcategory_parameters WHERE id=@source
            """, ("@node", nodeId), ("@position", position), ("@family", familyId is null ? DBNull.Value : familyId.Value), ("@source", sourceId));
        await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        var copyId = checked((int)insert.LastInsertedId);
        foreach (var (id, value, key) in await ValuesOfAsync(connection, transaction, sourceId, token).ConfigureAwait(false))
        {
            await using var copyValue = Command(connection, transaction, "INSERT INTO parameter_values(parameter_id,value,normalized_value) VALUES(@parameter,@value,@key)",
                ("@parameter", copyId), ("@value", value), ("@key", key));
            await copyValue.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            await RelinkValueAsync(connection, transaction, sourceId, id, copyId, checked((int)copyValue.LastInsertedId), subtree, token).ConfigureAwait(false);
        }
        await ParameterUsageLog.WriteRelocationAsync(connection, transaction, copyId, copyId, ParameterUsageActions.Relocated, actor, reason, token, subtree).ConfigureAwait(false);
    }

    // The On/Off of the products of the branch moves from one tag to another (a product that has both keeps one link).
    private static async Task MoveTagLinksAsync(MySqlConnection connection, MySqlTransaction transaction, int sourceTag, int targetTag, IReadOnlyList<int> subtree, CancellationToken token)
    {
        var list = SubcategoryAncestry.InList(subtree);
        await using (var copy = Command(connection, transaction, $"""
            INSERT IGNORE INTO product_tags(product_id,tag_id) SELECT pt.product_id,@target FROM product_tags pt INNER JOIN products p ON p.id=pt.product_id
            WHERE pt.tag_id=@source AND p.subcategory_id IN ({list})
            """, ("@target", targetTag), ("@source", sourceTag)))
            await copy.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        await using var drop = Command(connection, transaction, $"""
            DELETE pt FROM product_tags pt INNER JOIN products p ON p.id=pt.product_id WHERE pt.tag_id=@source AND p.subcategory_id IN ({list})
            """, ("@source", sourceTag));
        await drop.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }
    // The values of the absorbed parameter join the survivor's (a value of the same text is the same value), the links of the products follow their values, and the absorbed parameter
    // is deleted. A product that already has a value for the survivor keeps it.
    private static async Task MergeParameterAsync(MySqlConnection connection, MySqlTransaction transaction, MergeParameterOp merge, string actor, string reason, CancellationToken token)
    {
        var survivorValues = new Dictionary<string, int>(StringComparer.Ordinal);
        await using (var select = Command(connection, transaction, "SELECT id,normalized_value FROM parameter_values WHERE parameter_id=@id", ("@id", merge.SurvivorId)))
        await using (var reader = await select.ExecuteReaderAsync(token).ConfigureAwait(false))
            while (await reader.ReadAsync(token).ConfigureAwait(false)) survivorValues[reader.GetString(1)] = checked((int)reader.GetInt64(0));
        var absorbedValues = new List<(int Id, string Value, string Key)>();
        await using (var select = Command(connection, transaction, "SELECT id,value,normalized_value FROM parameter_values WHERE parameter_id=@id", ("@id", merge.AbsorbedId)))
        await using (var reader = await select.ExecuteReaderAsync(token).ConfigureAwait(false))
            while (await reader.ReadAsync(token).ConfigureAwait(false)) absorbedValues.Add((checked((int)reader.GetInt64(0)), reader.GetString(1), reader.GetString(2)));
        await ParameterUsageLog.WriteRelocationAsync(connection, transaction, merge.AbsorbedId, merge.SurvivorId, ParameterUsageActions.Merged, actor, reason, token).ConfigureAwait(false);
        // A product with a value of both keeps the survivor's.
        await using (var duplicates = Command(connection, transaction, """
            DELETE a FROM product_parameter_values a INNER JOIN product_parameter_values s ON s.product_id=a.product_id AND s.parameter_id=@survivor WHERE a.parameter_id=@absorbed
            """, ("@survivor", merge.SurvivorId), ("@absorbed", merge.AbsorbedId)))
            await duplicates.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        foreach (var (id, value, key) in absorbedValues)
        {
            if (!survivorValues.TryGetValue(key, out var survivorValue))
            {
                await using var insert = Command(connection, transaction, "INSERT INTO parameter_values(parameter_id,value,normalized_value) VALUES(@parameter,@value,@key)",
                    ("@parameter", merge.SurvivorId), ("@value", value), ("@key", key));
                await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                survivorValue = checked((int)insert.LastInsertedId);
                survivorValues[key] = survivorValue;
            }
            await using var relink = Command(connection, transaction, "UPDATE product_parameter_values SET parameter_id=@survivor,value_id=@newValue WHERE parameter_id=@absorbed AND value_id=@oldValue",
                ("@survivor", merge.SurvivorId), ("@newValue", survivorValue), ("@absorbed", merge.AbsorbedId), ("@oldValue", id));
            await relink.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }
        await using (var bump = Command(connection, transaction, "UPDATE subcategory_parameters SET version=version+1 WHERE id=@id", ("@id", merge.SurvivorId)))
            await bump.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        await using var delete = Command(connection, transaction, "DELETE FROM subcategory_parameters WHERE id=@id", ("@id", merge.AbsorbedId));
        await delete.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    private Task<T> WriteAsync<T>(Func<MySqlConnection, MySqlTransaction, Task<T>> action, CancellationToken token) =>
        MariaDb.WriteAsync(configuration, action, message => new ProductOperationException(message), token,
            "Modificările sunt permise numai în baza BlazorStoc. Verifică numele bazei configurate.");

    private Task EnsureAdministratorAsync(CancellationToken token) => accessControl?.EnsureAdministratorAsync(token) ?? Task.CompletedTask;
}
