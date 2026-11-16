using System.Globalization;
using MySqlConnector;

namespace BlazorStoc.Services;

// Learning from the corrections of the inventory take-over (docs/PROPUNERE_INVATARE_OCR.md, stages 1-3). The OCR reads the handwritten "Valoare reala" cell digit by digit; when the user
// corrects a value before sending it to the stock, the difference between what was read and what was sent is saved here: per digit when the number of digits is the same (4 read, 1 sent),
// as a "segment" sample otherwise. The administrator then chooses in the learning list (Inventar -> Invatare OCR) which corrections count; the decisions are global (every scan).

public static class OcrLearningStates
{
    public const string New = "new", Learned = "learned", Ignored = "ignored";
    public static bool IsValid(string status) => status is New or Learned or Ignored;
    public static string Label(string status) => status switch { Learned => "Învățat", Ignored => "Ignorat", _ => "Nou" };
}

public static class OcrSampleKinds
{
    public const string Digit = "digit", Segment = "segment";
    public static string Label(string kind) => kind == Digit ? "Cifră" : "Segmentare";
}

/// <summary>One line the user sent to the stock: what the OCR read and what was sent, with the material the scan kept for the line.</summary>
public sealed record OcrCorrectionLine(int Page, string RawCode, int? OriginalValue, int FinalValue,
    IReadOnlyList<InventoryPickupDigit>? Digits, byte[]? CellPng, int CellWidth, int CellHeight);

/// <summary>What a correction teaches: one digit (Digit set, DigitIndex = position from the left) or the whole value (segment).</summary>
public sealed record OcrSampleDraft(string Kind, int? DigitIndex, int? ReadDigit, int? RealDigit, InventoryPickupDigit? Digit);

public sealed record OcrLearningSample(long Id, long SessionId, DateTime CreatedUtc, string FileName, int Page, string RawCode, int? OriginalValue, int FinalValue,
    string Kind, int? DigitIndex, int? ReadDigit, int? RealDigit, string Top3, double? Confidence, int BoxX, int BoxY, int BoxWidth, int BoxHeight, int CellWidth, int CellHeight,
    string? CellPngBase64, string? DigitPngBase64, string Status, string DecidedBy, DateTime? DecidedUtc)
{
    public string Pair => ReadDigit is int read && RealDigit is int real ? OcrLearningRules.Pair(read, real) : "";
}

public sealed record OcrLearningFilter(string Status = "", string Kind = "", string Pair = "");
public sealed record OcrLearningPage(IReadOnlyList<OcrLearningSample> Items, int Total);
public sealed record OcrLearningCounts(int New, int Learned, int Ignored)
{
    public int Total => New + Learned + Ignored;
}

public static class OcrLearningRules
{
    public const string NoSelectionMessage = "Selectează cel puțin o corecție.";
    public const string LearnOnlyDigitsMessage = "Se învață numai din corecțiile de tip cifră; cele de segmentare se pot doar ignora.";
    public const string SaveFailedNotice = "Stocul a fost actualizat, dar corecțiile OCR nu au putut fi salvate pentru învățare.";
    public static readonly IReadOnlyList<int> PageSizes = [10, 25, 50, 100];
    // At most this many samples are kept: when a take-over goes over it, the oldest new or ignored ones go first (a learned sample is never removed by this).
    public const int MaxSamples = 1000;

    public static string Pair(int read, int real) => $"{read}>{real}";
    public static string PairLabel(string pair) => pair.Replace(">", " → ", StringComparison.Ordinal);

    public static bool TryParsePair(string? pair, out int read, out int real)
    {
        read = real = 0;
        var parts = (pair ?? "").Split('>');
        return parts.Length == 2 && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out read) && read is >= 0 and <= 9
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out real) && real is >= 0 and <= 9;
    }

    /// <summary>
    /// Compares what was read with what the user sent. An equal value teaches nothing. The same number of digits (and a digit picture for each) gives one sample per digit that differs;
    /// a different number of digits (a stroke lost or split, a digit seen twice) or nothing read at all is a segment sample without digit labels.
    /// </summary>
    public static IReadOnlyList<OcrSampleDraft> Compare(int? original, int final, IReadOnlyList<InventoryPickupDigit>? digits)
    {
        if (original == final) return [];
        var finalText = final.ToString(CultureInfo.InvariantCulture);
        if (original is not int read || digits is null || digits.Count == 0)
            return [new OcrSampleDraft(OcrSampleKinds.Segment, null, null, null, null)];
        var readText = read.ToString(CultureInfo.InvariantCulture);
        if (readText.Length != finalText.Length || digits.Count != readText.Length)
            return [new OcrSampleDraft(OcrSampleKinds.Segment, null, null, null, null)];
        var drafts = new List<OcrSampleDraft>();
        for (var index = 0; index < readText.Length; index++)
            if (readText[index] != finalText[index])
                drafts.Add(new OcrSampleDraft(OcrSampleKinds.Digit, index, readText[index] - '0', finalText[index] - '0', digits[index]));
        return drafts;
    }

    /// <summary>"4 → 1 (2), 2 → 5 (1)" for the journal; segment samples are counted apart.</summary>
    public static string Summarize(IEnumerable<(int? Read, int? Real)> samples)
    {
        var list = samples.ToList();
        var pairs = list.Where(sample => sample.Read is not null && sample.Real is not null)
            .GroupBy(sample => Pair(sample.Read!.Value, sample.Real!.Value)).OrderByDescending(group => group.Count()).ThenBy(group => group.Key)
            .Select(group => $"{PairLabel(group.Key)} ({group.Count()})").ToList();
        var segments = list.Count(sample => sample.Read is null || sample.Real is null);
        if (segments > 0) pairs.Add($"segmentare ({segments})");
        return string.Join(", ", pairs);
    }
}

/// <summary>What the learned corrections say: for a digit the OCR read, how many times the user turned it into each other digit (only samples marked "Învățat").</summary>
public sealed record OcrCalibration(IReadOnlyDictionary<int, IReadOnlyDictionary<int, int>> Counts)
{
    public static OcrCalibration Empty { get; } = new(new Dictionary<int, IReadOnlyDictionary<int, int>>());
    public bool IsEmpty => Counts.Count == 0;
}

/// <summary>
/// Calibration of a new reading (stage 4, option A): an unsure digit is replaced by the digit the user usually put in its place when the learned history is clear enough.
/// Deliberately conservative: the digit must be unsure (below MaxConfidence), the replacement must be one of the classifier's own top three, it must have been learned at least
/// MinSupport times and be at least MinShare of everything learned for that digit. A changed row stays "de verificat" and says what was changed.
/// </summary>
public static class OcrCalibrationRules
{
    public const double MaxConfidence = 0.90;
    public const int MinSupport = 2;
    public const double MinShare = 0.5;

    public static IReadOnlyList<InventoryPickupScanRow> Apply(IReadOnlyList<InventoryPickupScanRow> rows, OcrCalibration calibration)
    {
        if (calibration.IsEmpty) return rows;
        return [.. rows.Select(row => Apply(row, calibration))];
    }

    public static InventoryPickupScanRow Apply(InventoryPickupScanRow row, OcrCalibration calibration)
    {
        if (calibration.IsEmpty || row.RecognizedValue is null || row.Digits is not { Count: > 0 } digits) return row;
        var changed = new List<InventoryPickupDigit>(digits);
        var notes = new List<string>();
        for (var index = 0; index < digits.Count; index++)
        {
            var digit = digits[index];
            if (digit.Confidence >= MaxConfidence || !calibration.Counts.TryGetValue(digit.Digit, out var learned) || learned.Count == 0) continue;
            var total = learned.Values.Sum();
            var best = learned.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key).First();
            if (best.Value < MinSupport || (double)best.Value / total < MinShare || !InTop3(digit.Top3, best.Key)) continue;
            changed[index] = digit with { Digit = best.Key };
            notes.Add(OcrLearningRules.PairLabel(OcrLearningRules.Pair(digit.Digit, best.Key)));
        }
        if (notes.Count == 0) return row;
        var text = string.Concat(changed.Select(digit => digit.Digit));
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value)) return row;
        return row with { RecognizedValue = value, Uncertain = true, Digits = changed, LearnedNote = string.Join(", ", notes) };
    }

    private static bool InTop3(string top3, int digit) =>
        top3.Split(',', StringSplitOptions.RemoveEmptyEntries).Any(item => item.Split(':')[0] == digit.ToString(CultureInfo.InvariantCulture));
}

public interface IOcrLearningService
{
    /// <summary>The learned corrections as a table of confusions (read digit -> real digit -> times); empty when nothing is learned.</summary>
    Task<OcrCalibration> GetCalibrationAsync(CancellationToken cancellationToken = default);
    /// <summary>Saves what the user corrected in a confirmed take-over (the lines sent); returns how many samples were saved (0 = nothing was corrected, nothing is saved).</summary>
    Task<int> SaveCorrectionsAsync(string fileName, IReadOnlyList<OcrCorrectionLine> sentLines, CancellationToken cancellationToken = default);
    Task<OcrLearningPage> QueryAsync(OcrLearningFilter filter, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<OcrLearningCounts> CountsAsync(CancellationToken cancellationToken = default);
    /// <summary>The confusions (read digit > real digit) present in the saved samples, most frequent first.</summary>
    Task<IReadOnlyList<(string Pair, int Count)>> PairsAsync(CancellationToken cancellationToken = default);
    /// <summary>Administrator: the chosen samples become learned or ignored (or new again); returns how many changed.</summary>
    Task<int> SetStatusAsync(IReadOnlyCollection<long> ids, string status, CancellationToken cancellationToken = default);
    /// <summary>Administrator: every learned sample becomes new again (the reading goes back to the base model); returns how many changed.</summary>
    Task<int> ResetLearnedAsync(CancellationToken cancellationToken = default);
}

public sealed class MariaOcrLearningService(IConfiguration configuration, IAuditTrail audit, IAccessControl access) : IOcrLearningService
{
    private static Exception Forbidden(string message) => new InventoryOperationException(message);

    public async Task<int> SaveCorrectionsAsync(string fileName, IReadOnlyList<OcrCorrectionLine> sentLines, CancellationToken cancellationToken = default)
    {
        var corrected = sentLines.Select(line => (Line: line, Drafts: OcrLearningRules.Compare(line.OriginalValue, line.FinalValue, line.Digits))).Where(item => item.Drafts.Count > 0).ToList();
        if (corrected.Count == 0) return 0;
        var by = await access.GetUsernameAsync(cancellationToken).ConfigureAwait(false) ?? "";
        var now = DateTime.UtcNow;
        var saved = await MariaDb.WriteAsync(configuration, async (connection, transaction) =>
        {
            long sessionId;
            await using (var insert = MariaDb.Command(connection, transaction,
                "INSERT INTO ocr_learning_sessions (created_utc, created_by, file_name, lines_sent, lines_corrected) VALUES (@now, @by, @file, @sent, @corrected); SELECT LAST_INSERT_ID()",
                ("@now", now), ("@by", by), ("@file", Truncate(fileName, 260)), ("@sent", sentLines.Count), ("@corrected", corrected.Count)))
                sessionId = Convert.ToInt64(await insert.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
            var count = 0;
            foreach (var (line, drafts) in corrected)
                foreach (var draft in drafts)
                {
                    await using var sample = MariaDb.Command(connection, transaction, """
                        INSERT INTO ocr_learning_samples (session_id, page, raw_code, original_value, final_value, kind, digit_index, read_digit, real_digit, top3, confidence,
                            box_x, box_y, box_w, box_h, cell_w, cell_h, cell_png, digit_png, status)
                        VALUES (@session, @page, @code, @original, @final, @kind, @index, @read, @real, @top3, @confidence, @bx, @by, @bw, @bh, @cw, @ch, @cell, @digit, 'new')
                        """,
                        ("@session", sessionId), ("@page", line.Page), ("@code", Truncate(line.RawCode, 300)), ("@original", line.OriginalValue), ("@final", line.FinalValue),
                        ("@kind", draft.Kind), ("@index", draft.DigitIndex), ("@read", draft.ReadDigit), ("@real", draft.RealDigit),
                        ("@top3", draft.Digit?.Top3 ?? ""), ("@confidence", draft.Digit?.Confidence),
                        ("@bx", draft.Digit?.X ?? 0), ("@by", draft.Digit?.Y ?? 0), ("@bw", draft.Digit?.Width ?? 0), ("@bh", draft.Digit?.Height ?? 0),
                        ("@cw", line.CellWidth), ("@ch", line.CellHeight),
                        ("@cell", line.CellPng is { Length: > 0 } ? Convert.ToBase64String(line.CellPng) : null),
                        ("@digit", draft.Digit is { Png.Length: > 0 } ? Convert.ToBase64String(draft.Digit.Png) : null));
                    await sample.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    count++;
                }
            await TrimAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            return count;
        }, Forbidden, cancellationToken).ConfigureAwait(false);

        var summary = OcrLearningRules.Summarize(corrected.SelectMany(item => item.Drafts).Select(draft => (draft.ReadDigit, draft.RealDigit)));
        await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.Inventory, AuditActions.SaveOcrCorrections, "",
            string.IsNullOrWhiteSpace(fileName) ? "Preluare inventar" : $"Preluare inventar {fileName}",
            $"{saved} corecții OCR din {sentLines.Count} linii trimise în stoc: {summary}", "", cancellationToken).ConfigureAwait(false);
        return saved;
    }

    public async Task<OcrLearningPage> QueryAsync(OcrLearningFilter filter, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var (where, parameters) = Where(filter);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        int total;
        await using (var count = MariaDb.Command(connection, $"SELECT COUNT(*) FROM ocr_learning_samples s WHERE {where}", parameters))
            total = Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
        var size = pageSize <= 0 ? Math.Max(1, total) : pageSize;
        var current = Math.Clamp(page, 1, Math.Max(1, (total + size - 1) / size));
        var items = new List<OcrLearningSample>();
        await using var command = MariaDb.Command(connection, $"""
            SELECT s.id, s.session_id, n.created_utc, n.file_name, s.page, s.raw_code, s.original_value, s.final_value, s.kind, s.digit_index, s.read_digit, s.real_digit, s.top3, s.confidence,
                   s.box_x, s.box_y, s.box_w, s.box_h, s.cell_w, s.cell_h, s.cell_png, s.digit_png, s.status, s.decided_by, s.decided_utc
            FROM ocr_learning_samples s JOIN ocr_learning_sessions n ON n.id = s.session_id
            WHERE {where} ORDER BY n.created_utc DESC, s.id DESC LIMIT {size} OFFSET {(current - 1) * size}
            """, parameters);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            items.Add(new OcrLearningSample(reader.GetInt64(0), reader.GetInt64(1), DateTime.SpecifyKind(reader.GetDateTime(2), DateTimeKind.Utc), reader.GetString(3), reader.GetInt32(4), reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetInt32(6), reader.GetInt32(7), reader.GetString(8), reader.IsDBNull(9) ? null : reader.GetInt32(9),
                reader.IsDBNull(10) ? null : reader.GetInt32(10), reader.IsDBNull(11) ? null : reader.GetInt32(11), reader.GetString(12),
                reader.IsDBNull(13) ? null : Convert.ToDouble(reader.GetDecimal(13), CultureInfo.InvariantCulture),
                reader.GetInt32(14), reader.GetInt32(15), reader.GetInt32(16), reader.GetInt32(17), reader.GetInt32(18), reader.GetInt32(19),
                reader.IsDBNull(20) ? null : reader.GetString(20), reader.IsDBNull(21) ? null : reader.GetString(21), reader.GetString(22), reader.GetString(23),
                reader.IsDBNull(24) ? null : DateTime.SpecifyKind(reader.GetDateTime(24), DateTimeKind.Utc)));
        return new OcrLearningPage(items, total);
    }

    public async Task<OcrCalibration> GetCalibrationAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = MariaDb.Command(connection,
            "SELECT read_digit, real_digit, COUNT(*) FROM ocr_learning_samples WHERE status='learned' AND kind='digit' AND read_digit IS NOT NULL AND real_digit IS NOT NULL GROUP BY read_digit, real_digit");
        var counts = new Dictionary<int, Dictionary<int, int>>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var read = reader.GetInt32(0);
            if (!counts.TryGetValue(read, out var inner)) counts[read] = inner = [];
            inner[reader.GetInt32(1)] = Convert.ToInt32(reader.GetInt64(2), CultureInfo.InvariantCulture);
        }
        return new OcrCalibration(counts.ToDictionary(pair => pair.Key, pair => (IReadOnlyDictionary<int, int>)pair.Value));
    }

    public async Task<OcrLearningCounts> CountsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = MariaDb.Command(connection, "SELECT status, COUNT(*) FROM ocr_learning_samples GROUP BY status");
        int fresh = 0, learned = 0, ignored = 0;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var count = Convert.ToInt32(reader.GetInt64(1), CultureInfo.InvariantCulture);
            switch (reader.GetString(0))
            {
                case OcrLearningStates.Learned: learned = count; break;
                case OcrLearningStates.Ignored: ignored = count; break;
                default: fresh += count; break;
            }
        }
        return new OcrLearningCounts(fresh, learned, ignored);
    }

    public async Task<IReadOnlyList<(string Pair, int Count)>> PairsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = MariaDb.Command(connection,
            "SELECT read_digit, real_digit, COUNT(*) AS n FROM ocr_learning_samples WHERE kind='digit' GROUP BY read_digit, real_digit ORDER BY n DESC, read_digit, real_digit");
        var pairs = new List<(string, int)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            pairs.Add((OcrLearningRules.Pair(reader.GetInt32(0), reader.GetInt32(1)), Convert.ToInt32(reader.GetInt64(2), CultureInfo.InvariantCulture)));
        return pairs;
    }

    public async Task<int> SetStatusAsync(IReadOnlyCollection<long> ids, string status, CancellationToken cancellationToken = default)
    {
        await access.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        if (!OcrLearningStates.IsValid(status)) throw new InventoryOperationException("Starea alesă nu există.");
        if (ids.Count == 0) throw new InventoryOperationException(OcrLearningRules.NoSelectionMessage);
        var by = await access.GetUsernameAsync(cancellationToken).ConfigureAwait(false) ?? "";
        var list = string.Join(',', ids.Distinct().Select(id => id.ToString(CultureInfo.InvariantCulture)));
        // Only a digit correction can be learned (the learning is per digit); ignoring and "new again" are for every kind.
        var kindRule = status == OcrLearningStates.Learned ? " AND kind='digit'" : "";
        var (changed, summary) = await MariaDb.WriteAsync(configuration, async (connection, transaction) =>
        {
            var touched = new List<(int?, int?)>();
            await using (var select = MariaDb.Command(connection, transaction, $"SELECT read_digit, real_digit FROM ocr_learning_samples WHERE id IN ({list}) AND status<>@status{kindRule}", ("@status", status)))
            await using (var reader = await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    touched.Add((reader.IsDBNull(0) ? null : reader.GetInt32(0), reader.IsDBNull(1) ? null : reader.GetInt32(1)));
            await using var update = MariaDb.Command(connection, transaction,
                $"UPDATE ocr_learning_samples SET status=@status, decided_by=@by, decided_utc=@now WHERE id IN ({list}) AND status<>@status{kindRule}",
                ("@status", status), ("@by", status == OcrLearningStates.New ? "" : by), ("@now", status == OcrLearningStates.New ? null : DateTime.UtcNow));
            var rows = await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return (rows, OcrLearningRules.Summarize(touched));
        }, Forbidden, cancellationToken).ConfigureAwait(false);
        if (changed > 0)
        {
            var action = status == OcrLearningStates.Learned ? AuditActions.LearnOcr : status == OcrLearningStates.Ignored ? AuditActions.IgnoreOcrCorrection : AuditActions.ResetOcrLearning;
            await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.Inventory, action, "", "Învățare OCR",
                $"{changed} corecții: {summary}", "", cancellationToken).ConfigureAwait(false);
        }
        return changed;
    }

    public async Task<int> ResetLearnedAsync(CancellationToken cancellationToken = default)
    {
        await access.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var summary = "";
        var changed = await MariaDb.WriteAsync(configuration, async (connection, transaction) =>
        {
            var touched = new List<(int?, int?)>();
            await using (var select = MariaDb.Command(connection, transaction, "SELECT read_digit, real_digit FROM ocr_learning_samples WHERE status='learned'"))
            await using (var reader = await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    touched.Add((reader.IsDBNull(0) ? null : reader.GetInt32(0), reader.IsDBNull(1) ? null : reader.GetInt32(1)));
            summary = OcrLearningRules.Summarize(touched);
            await using var update = MariaDb.Command(connection, transaction, "UPDATE ocr_learning_samples SET status='new', decided_by='', decided_utc=NULL WHERE status='learned'");
            return await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }, Forbidden, cancellationToken).ConfigureAwait(false);
        if (changed > 0)
            await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.Inventory, AuditActions.ResetOcrLearning, "", "Învățare OCR",
                $"{changed} corecții învățate redevin noi: {summary}", "", cancellationToken).ConfigureAwait(false);
        return changed;
    }

    private static (string Where, (string, object?)[] Parameters) Where(OcrLearningFilter filter)
    {
        var conditions = new List<string> { "1=1" };
        var parameters = new List<(string, object?)>();
        if (OcrLearningStates.IsValid(filter.Status)) { conditions.Add("s.status=@status"); parameters.Add(("@status", filter.Status)); }
        if (filter.Kind is OcrSampleKinds.Digit or OcrSampleKinds.Segment) { conditions.Add("s.kind=@kind"); parameters.Add(("@kind", filter.Kind)); }
        if (OcrLearningRules.TryParsePair(filter.Pair, out var read, out var real))
        {
            conditions.Add("s.kind='digit' AND s.read_digit=@read AND s.real_digit=@real");
            parameters.Add(("@read", read)); parameters.Add(("@real", real));
        }
        return (string.Join(" AND ", conditions), [.. parameters]);
    }

    // Keeps the list at OcrLearningRules.MaxSamples: the oldest new / ignored samples are removed, then the sessions left without samples.
    private static async Task TrimAsync(MySqlConnection connection, MySqlTransaction transaction, CancellationToken cancellationToken)
    {
        int total;
        await using (var count = MariaDb.Command(connection, transaction, "SELECT COUNT(*) FROM ocr_learning_samples"))
            total = Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
        var excess = total - OcrLearningRules.MaxSamples;
        if (excess <= 0) return;
        await using (var delete = MariaDb.Command(connection, transaction,
            $"DELETE FROM ocr_learning_samples WHERE status<>'learned' ORDER BY id LIMIT {excess}"))
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await using var sessions = MariaDb.Command(connection, transaction,
            "DELETE FROM ocr_learning_sessions WHERE id NOT IN (SELECT session_id FROM ocr_learning_samples)");
        await sessions.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string Truncate(string text, int length) => text.Length <= length ? text : text[..length];
}

// The learning behind the circuit breaker of the inventory module, like the other entry points of the module. The answers of the module (InventoryOperationException, access denied) are not faults.
internal sealed class GuardedOcrLearningService(IOcrLearningService inner, BlazorStoc.Modules.ModuleRegistry modules) : IOcrLearningService
{
    public Task<int> SaveCorrectionsAsync(string fileName, IReadOnlyList<OcrCorrectionLine> sentLines, CancellationToken cancellationToken = default) =>
        modules.RunAsync(BlazorStoc.Modules.Inventory.InventoryModule.ModuleId, () => inner.SaveCorrectionsAsync(fileName, sentLines, cancellationToken));
    public Task<OcrLearningPage> QueryAsync(OcrLearningFilter filter, int page, int pageSize, CancellationToken cancellationToken = default) =>
        modules.RunAsync(BlazorStoc.Modules.Inventory.InventoryModule.ModuleId, () => inner.QueryAsync(filter, page, pageSize, cancellationToken));
    public Task<OcrCalibration> GetCalibrationAsync(CancellationToken cancellationToken = default) =>
        modules.RunAsync(BlazorStoc.Modules.Inventory.InventoryModule.ModuleId, () => inner.GetCalibrationAsync(cancellationToken));
    public Task<OcrLearningCounts> CountsAsync(CancellationToken cancellationToken = default) =>
        modules.RunAsync(BlazorStoc.Modules.Inventory.InventoryModule.ModuleId, () => inner.CountsAsync(cancellationToken));
    public Task<IReadOnlyList<(string Pair, int Count)>> PairsAsync(CancellationToken cancellationToken = default) =>
        modules.RunAsync(BlazorStoc.Modules.Inventory.InventoryModule.ModuleId, () => inner.PairsAsync(cancellationToken));
    public Task<int> SetStatusAsync(IReadOnlyCollection<long> ids, string status, CancellationToken cancellationToken = default) =>
        modules.RunAsync(BlazorStoc.Modules.Inventory.InventoryModule.ModuleId, () => inner.SetStatusAsync(ids, status, cancellationToken));
    public Task<int> ResetLearnedAsync(CancellationToken cancellationToken = default) =>
        modules.RunAsync(BlazorStoc.Modules.Inventory.InventoryModule.ModuleId, () => inner.ResetLearnedAsync(cancellationToken));
}
