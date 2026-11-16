using BlazorStoc.Services;

namespace BlazorStoc.Checks;

// The rules that decide what a correction of the inventory take-over teaches (Modules/Inventory/OcrLearning.cs).
internal static class OcrLearningChecks
{
    private static InventoryPickupDigit Digit(int value) => new(value, 0.9, $"{value}:0.90", 1, 1, 10, 20, [1, 2, 3]);

    // The real engine on the sample scan: every row with a handwritten value keeps the picture of its cell and one entry per digit read (box inside the cell, top choices, 28x28 PNG).
    public static async Task ScanAsync(Action<bool, string> check)
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "BlazorStoc.csproj"))) root = Path.GetDirectoryName(root.TrimEnd(Path.DirectorySeparatorChar));
        check(root is not null, "OCR learning: the project folder was found for the sample scan");
        using var engine = new InventoryPickupOcrService(new TessdataPath(new TestWebHostEnvironment(root!)));
        await using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "inventar-proba.pdf"));
        var scan = await engine.ScanAsync(stream);
        var withValue = scan.Rows.Where(row => row.RecognizedValue is not null).ToList();
        static bool IsPng(byte[]? bytes) => bytes is { Length: > 8 } && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47;
        check(withValue.Count > 0 && withValue.All(row => IsPng(row.CellPng) && row.CellWidth > 0 && row.CellHeight > 0
                && row.Digits is { Count: > 0 } digits && string.Concat(digits.Select(digit => digit.Digit)) == row.RecognizedValue!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            "OCR learning: each handwritten value keeps the cell picture and the digits it was read from (their concatenation is the value)");
        check(withValue.All(row => row.Digits!.All(digit => IsPng(digit.Png) && digit.Width > 0 && digit.Height > 0 && digit.X >= 0 && digit.Y >= 0
                && digit.X + digit.Width <= row.CellWidth && digit.Y + digit.Height <= row.CellHeight && digit.Top3.Split(',').Length == 3 && digit.Top3.StartsWith(digit.Digit + ":", StringComparison.Ordinal))),
            "OCR learning: digit pictures are PNG, their boxes lie inside the cell and the top three choices start with the digit read");
        // A user correction of a read value gives the sample the take-over saves: the first row with a value is "corrected" by changing its last digit.
        var first = withValue.First();
        var last = first.RecognizedValue!.Value % 10;
        var corrected = first.RecognizedValue.Value - last + (last + 1) % 10;
        var drafts = OcrLearningRules.Compare(first.RecognizedValue, corrected, first.Digits);
        check(drafts.Count == 1 && drafts[0].Kind == OcrSampleKinds.Digit && drafts[0].DigitIndex == first.Digits!.Count - 1 && drafts[0].ReadDigit == last && drafts[0].Digit == first.Digits[^1],
            "OCR learning: correcting the last digit of a real reading gives one digit sample made of the picture of that digit");
    }

    public static void Run(Action<bool, string> check)
    {
        var digits45 = new[] { Digit(4), Digit(5) };
        var same = OcrLearningRules.Compare(45, 45, digits45);
        check(same.Count == 0, "OCR learning: an unchanged value teaches nothing");

        var oneDigit = OcrLearningRules.Compare(45, 15, digits45);
        check(oneDigit.Count == 1 && oneDigit[0] is { Kind: OcrSampleKinds.Digit, DigitIndex: 0, ReadDigit: 4, RealDigit: 1 } && oneDigit[0].Digit == digits45[0],
            "OCR learning: 45 corrected to 15 is one digit sample 4 -> 1 at position 0 (the 5 was right)");

        var twoDigits = OcrLearningRules.Compare(45, 12, digits45);
        check(twoDigits.Count == 2 && twoDigits[0] is { ReadDigit: 4, RealDigit: 1 } && twoDigits[1] is { DigitIndex: 1, ReadDigit: 5, RealDigit: 2 },
            "OCR learning: every digit that differs is a sample of its own");

        var longer = OcrLearningRules.Compare(45, 145, digits45);
        check(longer.Count == 1 && longer[0] is { Kind: OcrSampleKinds.Segment, ReadDigit: null, RealDigit: null, Digit: null },
            "OCR learning: a different number of digits is a segment sample without digit labels");

        check(OcrLearningRules.Compare(null, 7, null) is [{ Kind: OcrSampleKinds.Segment }], "OCR learning: nothing read is a segment sample");
        check(OcrLearningRules.Compare(45, 15, [Digit(4)]) is [{ Kind: OcrSampleKinds.Segment }], "OCR learning: digit pictures that do not match the digits read are a segment sample");

        check(OcrLearningRules.TryParsePair("4>1", out var read, out var real) && read == 4 && real == 1 && !OcrLearningRules.TryParsePair("41", out _, out _) && !OcrLearningRules.TryParsePair("4>10", out _, out _) && !OcrLearningRules.TryParsePair("a>1", out _, out _),
            "OCR learning: a confusion pair is read as digit>digit and nothing else");
        check(OcrLearningRules.Summarize([(4, 1), (4, 1), (2, 5), (null, null)]) == "4 → 1 (2), 2 → 5 (1), segmentare (1)", "OCR learning: the journal summary counts the confusions and the segment samples");
        check(OcrLearningStates.IsValid("learned") && !OcrLearningStates.IsValid("x"), "OCR learning: only new, learned and ignored are states");

        // Stage 4: calibration of a new reading with the learned corrections.
        static InventoryPickupDigit Unsure(int value, double confidence, string top3) => new(value, confidence, top3, 1, 1, 10, 20, [1]);
        static OcrCalibration Learned(params (int Read, int Real, int Times)[] items) =>
            new(items.GroupBy(item => item.Read).ToDictionary(group => group.Key, group => (IReadOnlyDictionary<int, int>)group.ToDictionary(item => item.Real, item => item.Times)));
        var calibration = Learned((4, 1, 3), (4, 9, 1));
        var row45 = new InventoryPickupScanRow(1, "ABC", 45, false, 1, [Unsure(4, 0.55, "4:0.55,1:0.30,9:0.05"), Unsure(5, 0.99, "5:0.99,6:0.01,3:0.00")], [1], 100, 40);

        var fixedRow = OcrCalibrationRules.Apply(row45, calibration);
        check(fixedRow.RecognizedValue == 15 && fixedRow.Uncertain && fixedRow.LearnedNote == "4 → 1" && fixedRow.Digits![0].Digit == 1 && fixedRow.Digits[1].Digit == 5,
            "OCR learning: an unsure 4 that the user usually turned into 1 is read as 1 (15), stays to be checked and says what was changed");
        check(OcrLearningRules.Compare(fixedRow.RecognizedValue, 15, fixedRow.Digits).Count == 0 && OcrLearningRules.Compare(fixedRow.RecognizedValue, 45, fixedRow.Digits) is [{ ReadDigit: 1, RealDigit: 4 }],
            "OCR learning: after a calibration the take-over compares with the calibrated value (undoing it is a correction 1 > 4)");
        check(ReferenceEquals(OcrCalibrationRules.Apply(row45, OcrCalibration.Empty), row45), "OCR learning: with nothing learned the reading is exactly as the OCR gave it");
        check(OcrCalibrationRules.Apply(row45 with { Digits = [Unsure(4, 0.95, "4:0.95,1:0.03,9:0.01"), row45.Digits![1]] }, calibration).RecognizedValue == 45, "OCR learning: a confident digit is never changed");
        check(OcrCalibrationRules.Apply(row45, Learned((4, 1, 1))).RecognizedValue == 45, "OCR learning: a single learned correction is not enough");
        check(OcrCalibrationRules.Apply(row45, Learned((4, 1, 2), (4, 9, 2), (4, 7, 2))).RecognizedValue == 45, "OCR learning: a replacement that is less than half of what was learned for the digit is not used");
        check(OcrCalibrationRules.Apply(row45 with { Digits = [Unsure(4, 0.55, "4:0.55,9:0.30,7:0.05"), row45.Digits![1]] }, calibration).RecognizedValue == 45, "OCR learning: a replacement outside the top three of the classifier is not used");
        check(OcrCalibrationRules.Apply(row45 with { RecognizedValue = null, Digits = null }, calibration).RecognizedValue is null, "OCR learning: a row without a value or digits is left alone");

        // The scan keeps the learning material through the (JSON) result of the OCR worker.
        var options = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        var row = new InventoryPickupScanRow(1, "ABC", 45, false, 3, digits45, [9, 9], 200, 60);
        var back = System.Text.Json.JsonSerializer.Deserialize<InventoryPickupScanRow>(System.Text.Json.JsonSerializer.Serialize(row, options), options);
        check(back is { CellWidth: 200, CellHeight: 60 } && back.CellPng!.SequenceEqual(new byte[] { 9, 9 }) && back.Digits!.Count == 2 && back.Digits[1].Digit == 5 && back.Digits[1].Png.SequenceEqual(new byte[] { 1, 2, 3 }),
            "OCR learning: the digits and the cell picture survive the JSON result of the OCR worker");
    }
}
