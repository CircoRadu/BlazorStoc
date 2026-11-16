using BlazorStoc.Services;
using Microsoft.Extensions.Configuration;
using MySqlConnector;

namespace BlazorStoc.Checks;

public static partial class MariaExtendedChecks
{
    // ---- OCR learning of the inventory take-over: corrections saved, list, decisions (administrator), reset, journal ---------------
    private static async Task OcrLearningAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe)
    {
        var service = new MariaOcrLearningService(configuration, audit, admin);
        var user = new MariaOcrLearningService(configuration, audit, new TestAccessControl(false, "operator.ext"));
        var file = "inventar-ext-" + Suffix() + ".pdf";
        InventoryPickupDigit Digit(int value) => new(value, 0.55, $"{value}:0.55,1:0.30", 12, 4, 20, 40, [1, 2, 3]);
        try
        {
            var lines = new List<OcrCorrectionLine>
            {
                new(2, "Cod A", 45, 15, [Digit(4), Digit(5)], [7, 7, 7], 300, 60),   // digit sample 4 > 1
                new(2, "Cod B", 12, 12, [Digit(1), Digit(2)], [7], 300, 60),          // unchanged: nothing
                new(3, "Cod C", 8, 18, [Digit(8)], [7], 300, 60),                     // segment
                new(3, "Cod D", null, 6, null, null, 0, 0)                            // nothing read: segment
            };
            var saved = await service.SaveCorrectionsAsync(file, lines);
            Check(saved == 3, "Corrections are saved per digit and per segment; an unchanged value saves nothing");
            Check(await service.SaveCorrectionsAsync(file, [lines[1]]) == 0, "A take-over without corrections saves no session");

            var all = await service.QueryAsync(new OcrLearningFilter(), 1, 1000);
            var mine = all.Items.Where(sample => sample.FileName == file).ToList();
            var digit = mine.Single(sample => sample.Kind == OcrSampleKinds.Digit);
            Check(mine.Count == 3 && digit is { ReadDigit: 4, RealDigit: 1, DigitIndex: 0, OriginalValue: 45, FinalValue: 15, Status: OcrLearningStates.New, Page: 2, BoxWidth: 20, CellWidth: 300 }
                && digit.CellPngBase64 == Convert.ToBase64String(new byte[] { 7, 7, 7 }) && digit.DigitPngBase64 == Convert.ToBase64String(new byte[] { 1, 2, 3 }) && digit.Top3.StartsWith("4:0.55", StringComparison.Ordinal),
                "A digit sample keeps the digits, the position, the box, the pictures and starts as new");
            Check((await service.QueryAsync(new OcrLearningFilter(Kind: OcrSampleKinds.Segment), 1, 1000)).Items.Count(sample => sample.FileName == file) == 2, "The list filters by kind");
            Check((await service.QueryAsync(new OcrLearningFilter(Pair: "4>1"), 1, 1000)).Items.Any(sample => sample.Id == digit.Id) && (await service.PairsAsync()).Any(pair => pair.Pair == "4>1"), "The list filters by confusion pair and the pairs are listed");
            var small = await service.QueryAsync(new OcrLearningFilter(), 1, 2);
            Check(small.Items.Count == 2 && small.Total >= 3, "The list is paged on the server");

            var segment = mine.First(sample => sample.Kind == OcrSampleKinds.Segment);
            Check((await service.CountsAsync()).New >= 3, "The counts include the new samples");

            // Only the administrator decides.
            var refused = false;
            try { await user.SetStatusAsync([digit.Id], OcrLearningStates.Learned); } catch (AccessDeniedException) { refused = true; }
            Check(refused, "Only an administrator can choose what the OCR learns from");

            Check(await service.SetStatusAsync([digit.Id, segment.Id], OcrLearningStates.Learned) == 1, "Only a digit correction can be learned (the segment one is left alone)");
            var learned = (await service.QueryAsync(new OcrLearningFilter(Status: OcrLearningStates.Learned), 1, 1000)).Items.Single(sample => sample.Id == digit.Id);
            var calibration = await service.GetCalibrationAsync();
            Check(calibration.Counts.TryGetValue(4, out var fours) && fours.GetValueOrDefault(1) >= 1 && !calibration.Counts.ContainsKey(8), "The calibration holds only the learned digit corrections (4 > 1), not the segment or the new ones");
            Check(learned.DecidedBy == "integration.tester" && learned.DecidedUtc is not null, "The decision keeps who and when");
            Check(await service.SetStatusAsync([segment.Id], OcrLearningStates.Ignored) == 1, "A correction can be ignored");
            Check(await service.ResetLearnedAsync() >= 1 && (await service.QueryAsync(new OcrLearningFilter(Status: OcrLearningStates.Learned), 1, 1000)).Items.All(sample => sample.Id != digit.Id), "Reset turns the learned corrections into new ones");

            var journal = await audit.QueryAsync(new AuditQuery(Text: file, Entity: AuditEntities.Inventory), 1, 20);
            var decisions = await audit.QueryAsync(new AuditQuery(Entity: AuditEntities.Inventory), 1, 50);
            Check(journal.Events.Any(entry => entry.Action == AuditActions.SaveOcrCorrections && entry.Details.Contains("4 → 1 (1)", StringComparison.Ordinal))
                && decisions.Events.Any(entry => entry.Action == AuditActions.LearnOcr) && decisions.Events.Any(entry => entry.Action == AuditActions.IgnoreOcrCorrection) && decisions.Events.Any(entry => entry.Action == AuditActions.ResetOcrLearning),
                "The journal names each operation: saving the corrections, learning, ignoring, reset");

            // Retention: the list is kept at 1000 samples, the oldest new ones go first and a learned one stays.
            await using (var fill = new MySqlCommand("""
                INSERT INTO ocr_learning_sessions (created_utc, created_by, file_name, lines_sent, lines_corrected) VALUES (UTC_TIMESTAMP(), 'ret', 'inventar-ext-retentie.pdf', 1000, 1000);
                INSERT INTO ocr_learning_samples (session_id, raw_code, original_value, final_value, kind, status)
                    SELECT LAST_INSERT_ID(), CONCAT('ret-', seq), 1, 2, 'segment', IF(seq = 1, 'learned', 'new') FROM seq_1_to_1000
                """, probe)) await fill.ExecuteNonQueryAsync();
            Check(await service.SaveCorrectionsAsync(file + ".2", [new OcrCorrectionLine(1, "Cod R", 45, 15, [Digit(4), Digit(5)], null, 0, 0)]) == 1, "A take-over over the limit still saves its corrections");
            await using (var total = new MySqlCommand("SELECT COUNT(*), SUM(raw_code='ret-1' AND status='learned'), SUM(raw_code='Cod R') FROM ocr_learning_samples", probe))
            await using (var reader = await total.ExecuteReaderAsync())
            {
                await reader.ReadAsync();
                Check(Convert.ToInt32(reader.GetValue(0)) == OcrLearningRules.MaxSamples && Convert.ToInt32(reader.GetValue(1)) == 1 && Convert.ToInt32(reader.GetValue(2)) >= 1,
                    "Retention: the list stays at 1000 samples, the oldest new ones are removed and the learned and the newest stay");
            }
        }
        finally
        {
            await using var clean = new MySqlCommand("DELETE FROM ocr_learning_sessions WHERE file_name LIKE 'inventar-ext-%'", probe);
            await clean.ExecuteNonQueryAsync();
        }
        await using var left = new MySqlCommand("SELECT COUNT(*) FROM ocr_learning_sessions WHERE file_name LIKE 'inventar-ext-%'", probe);
        Check(Convert.ToInt64(await left.ExecuteScalarAsync()) == 0, "The test corrections are gone after clean-up");
    }
}
