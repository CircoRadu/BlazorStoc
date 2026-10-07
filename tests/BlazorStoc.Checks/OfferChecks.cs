using System.IO.Compression;
using System.Text;
using BlazorStoc.Services;

namespace BlazorStoc.Checks;

// Module Oferte: the own .xlsx reader and the templates of offer sheets. The workbook is generated here (no real offer is copied into the repository).
public static class OfferChecks
{
    public static XlsxWorkbook Offer(string number = "250700006", bool withUnknownSection = false, string quantityLabel = "Cantitate")
    {
        var rows = new List<(int Row, string[] Cells)>();
        // A1:B1 merged label, C1:J1 merged value; rows as in the offers of the company: header, title, category, beneficiary, then sections.
        rows.Add((1, ["A=s:Societatea:", "C=s:Firma Test SRL"]));
        rows.Add((8, ["A=s:Oferta Nr: ", $"E=i:{number}"]));
        rows.Add((9, ["A=s:Instalare sistem de test"]));
        rows.Add((10, ["A=s:Categoria:", "C=s:CCTV"]));
        rows.Add((11, ["A=s:Beneficiar:", "C=s:COMUNA TEST"]));
        rows.Add((15, ["A=s:Echipamente"]));
        string[] header = ["A=s:Nr.", "B=s:Tip produs", "D=s:Denumire", "F=s:Unitate\r\nmăsură", $"G=s:{quantityLabel}", "H=s:Preț\r\nunitar", "J=s:Valoare"];
        rows.Add((16, header));
        rows.Add((17, ["A=n:1", "B=s:NVR", "D=s:Recorder 64 canale\r\nmodel X", "F=s:Buc", "G=n:2", "H=n:25638.799999999999"]));
        rows.Add((18, ["A=n:2", "B=s:CABLURI", "D=s:Cablu FTP cat 5", "F=s:Metru", "G=n:5000", "H=n:2.98"]));
        rows.Add((19, ["A=n:3", "B=s:DIVERSE", "D=s:Totalizator electronic", "F=s:Bucata", "G=n:3", "H=n:10"]));
        rows.Add((20, ["H=s:Fără TVA", "M=s:TVA"]));
        rows.Add((21, ["A=s:Total echipamente:", "H=n:100", "M=n:19"]));
        rows.Add((22, ["A=s:Manoperă"]));
        rows.Add((23, header));
        rows.Add((24, ["A=n:1", "B=s:SERVICII", "D=s:Instalare camera", "F=s:Ora", "G=n:8", "H=n:50"]));
        rows.Add((25, ["A=s:Total manoperă:", "H=n:400"]));
        if (withUnknownSection)
        {
            rows.Add((26, ["A=s:Altele"]));
            rows.Add((27, header));
            rows.Add((28, ["A=n:1", "D=s:Ceva", "F=s:Buc", "G=n:1"]));
        }
        return Build(rows, ["A1:B1", "C1:J1", "A8:D8", "A9:J9", "D17:E17"]);
    }

    private static XlsxWorkbook Build(List<(int Row, string[] Cells)> rows, string[] merges)
    {
        var shared = new List<string>();
        var sheet = new StringBuilder("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
        foreach (var (row, cells) in rows)
        {
            sheet.Append($"<row r=\"{row}\">");
            foreach (var cell in cells)
            {
                var column = cell[..cell.IndexOf('=')];
                var kind = cell[(cell.IndexOf('=') + 1)..][..1];
                var value = cell[(cell.IndexOf('=') + 3)..];
                var reference = $"{column}{row}";
                if (kind == "n") sheet.Append($"<c r=\"{reference}\"><v>{value}</v></c>");
                else if (kind == "i") sheet.Append($"<c r=\"{reference}\" t=\"inlineStr\"><is><t>{System.Security.SecurityElement.Escape(value)}</t></is></c>");
                else { shared.Add(value); sheet.Append($"<c t=\"s\" r=\"{reference}\"><v>{shared.Count - 1}</v></c>"); }
            }
            sheet.Append("</row>");
        }
        sheet.Append("</sheetData><mergeCells>");
        foreach (var merge in merges) sheet.Append($"<mergeCell ref=\"{merge}\"/>");
        sheet.Append("</mergeCells></worksheet>");
        var strings = new StringBuilder("<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
        // The first string is written as rich text (two runs) to check that the runs are joined.
        for (var i = 0; i < shared.Count; i++)
            strings.Append(i == 0 ? $"<si><r><t>{System.Security.SecurityElement.Escape(shared[i][..4])}</t></r><r><t>{System.Security.SecurityElement.Escape(shared[i][4..])}</t></r></si>"
                : $"<si><t xml:space=\"preserve\">{System.Security.SecurityElement.Escape(shared[i])}</t></si>");
        strings.Append("</sst>");
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string path, string content) { using var writer = new StreamWriter(archive.CreateEntry(path).Open(), new UTF8Encoding(false)); writer.Write(content); }
            Add("xl/workbook.xml", "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Oferta\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            Add("xl/_rels/workbook.xml.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Target=\"worksheets/sheet1.xml\" Type=\"worksheet\"/></Relationships>");
            Add("xl/sharedStrings.xml", strings.ToString());
            Add("xl/worksheets/sheet1.xml", sheet.ToString());
        }
        stream.Position = 0;
        return XlsxReader.Read(stream);
    }

    // OFFER_SAMPLES_DIR = a directory with real offers (.xlsx, not in the repository): one template suggested from the first file must read all of them.
    private static void RealSamples(Action<bool, string> check)
    {
        if (Environment.GetEnvironmentVariable("OFFER_SAMPLES_DIR") is not { Length: > 0 } directory || !Directory.Exists(directory)) return;
        var files = Directory.EnumerateFiles(directory, "*.xlsx", SearchOption.AllDirectories).OrderBy(file => file).ToList();
        if (files.Count == 0) return;
        XlsxWorkbook Open(string file) { using var stream = File.OpenRead(file); return XlsxReader.Read(stream); }
        var sampleDefinition = OfferTemplateEngine.Suggest(Open(files[0]).Sheets[0]);
        foreach (var file in files)
        {
            var reading = OfferTemplateEngine.Apply(sampleDefinition, Open(file));
            Console.WriteLine($"  sample {Path.GetFileName(file)}: number={reading.Field(OfferHeaderField.NumberKey)} category={reading.Field(OfferHeaderField.CategoryKey)} sections={string.Join(",", reading.Sections.Select(section => $"{section.Name}:{section.Lines.Count}{(section.Import ? "+" : "")}"))} stock-lines={reading.StockLines.Count()}");
            check(reading.Field(OfferHeaderField.NumberKey).Length > 0 && reading.Sections.Count > 0 && reading.Sections.All(section => section.Lines.Count > 0) && OfferTemplateEngine.Score(sampleDefinition, Open(file)) >= 0.999,
                $"The real offer {Path.GetFileName(file)} is read by the template suggested from the first sample");
        }
    }

    // Situatia proiectului: pure calculation (handed-over pieces fill the lines in order, the stock covers the rest, leftovers are outside the offer, purchase list by supplier).
    public static void RunSituation(Action<bool, string> check)
    {
        SituationInputLine Line(int? type, string component, string name, decimal quantity, int? product, bool inStock = true) =>
            new(type, component, "Echipamente", name, inStock ? "Buc" : "Metru", quantity, inStock, product, null);
        var input = new SituationInput(
            [Line(1, "CCTV", "Camera", 5, 10), Line(1, "CCTV", "Recorder", 2, 20), Line(1, "CCTV", "Cablu", 100, null, false), Line(2, "Alarma", "Camera", 3, 10), Line(2, "Alarma", "Sirena", 1, null)],
            new Dictionary<int, int> { [10] = 6, [30] = 2 }, new Dictionary<int, string> { [10] = "Camera X", [20] = "Recorder Y", [30] = "Intrerupator" },
            new Dictionary<int, int> { [10] = 1, [20] = -4 }, new Dictionary<int, string> { [10] = "Alfa SRL" });
        var situation = ProjectSituationRules.Compute(1, "Proiect test", input);
        var cctv = situation.Components.Single(item => item.SystemTypeId == 1);
        var alarm = situation.Components.Single(item => item.SystemTypeId == 2);
        check(cctv.Needed == 7 && cctv.Delivered == 5 && alarm.Needed == 4 && alarm.Delivered == 1
              && cctv.Lines[0].State == SituationLineState.Covered && cctv.Lines[2].State == SituationLineState.OutOfStock && cctv.Lines[2].Deficit == 0
              && alarm.Lines[0] is { Delivered: 1, FromStock: 1, Deficit: 1, State: SituationLineState.Partial }
              && cctv.Lines[1] is { Deficit: 2, State: SituationLineState.Missing } && alarm.Lines[1].State == SituationLineState.Missing,
            "Situation: handed-over pieces fill the lines in order, the stock covers the rest (negative stock counts as zero), meters stay out of the stock");
        check(situation.OutsideOffer.Count == 1 && situation.OutsideOffer[0] is { ProductId: 30, Quantity: 2 } && situation.Purchase.Count == 2 && situation.Purchase[0].Supplier == "Alfa SRL"
              && situation.Purchase[0].Items.Single() is { Name: "Camera X", Quantity: 1 } && situation.Purchase[1].Supplier == ProjectSituationRules.UnknownSupplier
              && situation.Purchase[1].Items.Sum(item => item.Quantity) == 3,
            "Situation: what was handed over beyond the offer is outside the offer; the purchase list groups the deficit by the last supplier, the unknown last");
        // Reservations: the project counts on its own reservation plus the free stock; what other projects reserved is not available to it.
        var reserved = ProjectSituationRules.Compute(1, "Proiect test", new SituationInput([Line(1, "CCTV", "Camera", 5, 10)], new Dictionary<int, int>(), new Dictionary<int, string> { [10] = "Camera X" },
            new Dictionary<int, int> { [10] = 8 }, new Dictionary<int, string>()) { ProjectReserved = new Dictionary<int, int> { [10] = 2 }, TotalReserved = new Dictionary<int, int> { [10] = 6 } });
        var reservedLine = reserved.Components.Single().Lines.Single();
        check(reservedLine is { FromStock: 4, Reserved: 2, Deficit: 1 } && ReservationRules.Touched(3, 7, 6, 0) == 2 && ReservationRules.Touched(3, 7, 6, 3) == 0 && ReservationRules.Touched(2, -2, 5, 0) == 0,
            "Situation: the project counts on its own reservation plus the free stock; an exit touches others' reservations only beyond its own reservation and the free stock");
        var csv = ProjectSituationRules.ToCsv(situation);
        check(csv.Contains("Lista de achizitie") && csv.Contains(";Alfa SRL;Camera X;Buc;1") && csv.Contains("In afara ofertei;Cantitate predata") && csv.Split('\n').Length > 10,
            "Situation: the CSV carries the lines, the leftovers and the purchase list");
    }

    public static void Run(Action<bool, string> check)
    {
        var workbook = Offer();
        var sheet = workbook.Sheets[0];

        // 1. The reader: sheet name, shared (also rich text) and inline strings, numbers as typed, merged ranges kept at the top-left cell.
        check(sheet.Name == "Oferta" && sheet.Text(1, 1) == "Societatea:" && sheet.Text(8, 5) == "250700006" && sheet.Number(17, 7) == 2 && sheet.Text(17, 8) == "25638.8"
              && sheet.MergeAt(1, 2) is { FirstColumn: 1, LastColumn: 2 } && sheet.Text(1, 2) == "" && XlsxReader.ColumnLetter(28) == "AB" && XlsxReader.ColumnIndex("AB") == 28,
            "The xlsx reader gives shared, rich and inline strings, numbers as typed and merged ranges at their top-left cell");

        // 2. A template reads the header by labels and the sections of the table: equipment imported, labour not, totals ignored, meters out of stock.
        var definition = OfferTemplateEngine.Suggest(sheet);
        var reading = OfferTemplateEngine.Apply(definition, workbook);
        var equipment = reading.Sections.Single(section => section.Name == "Echipamente");
        check(reading.Field(OfferHeaderField.NumberKey) == "250700006" && reading.Field(OfferHeaderField.TitleKey) == "Instalare sistem de test" && reading.Field(OfferHeaderField.CategoryKey) == "CCTV"
              && reading.Field(OfferHeaderField.BeneficiaryKey) == "COMUNA TEST" && reading.Sections.Count == 2 && equipment.Import && !reading.Sections.Single(section => section.Name == "Manoperă").Import
              && equipment.Lines.Count == 3 && equipment.Lines[0].Name == "Recorder 64 canale\nmodel X" && !equipment.Lines[1].InStock && equipment.Lines[2].Name == "Totalizator electronic" && equipment.Lines[2].InStock
              && reading.StockLines.Count() == 2,
            "A template reads the offer header by labels and the lines of the imported sections; totals are ignored and non-piece units are out of stock");

        // 3. A section the template does not know is flagged and not imported; the suggested columns follow the labels of the sheet.
        var unknown = OfferTemplateEngine.Apply(definition, Offer(withUnknownSection: true));
        check(definition is { NameColumn: "D", UnitColumn: "F", QuantityColumn: "G", TypeColumn: "B", NumberColumn: "A" } && unknown.Sections.Single(section => section.Name == "Altele") is { Known: false, Import: false }
              && unknown.Problems.Any(problem => problem.Contains("Altele")),
            "The suggested template follows the labels of the sheet and an unknown section is flagged and not imported");

        // 4. The template is chosen by the labels of the offer: the same labels fit, another label of the table does not; an inactive template is not chosen.
        var templates = new[] { new OfferTemplateRecord(1, "A", true, definition, 0, "test", ""), new OfferTemplateRecord(2, "B", false, definition, 0, "test", "") };
        var chosen = OfferTemplateEngine.Choose(templates, item => item.Definition, item => item.Active, Offer("260600015"));
        var other = OfferTemplateEngine.Choose(templates, item => item.Definition, item => item.Active, Offer(quantityLabel: "Buc."));
        RealSamples(check);

        // 5. Matching: a line is matched to the catalog by the words of the product (model codes weigh more), also when its whole name is inside the text;
        // beneficiaries are matched without legal forms; revisions show added, removed and changed lines.
        var products = new List<Product>
        {
            new(1, "Camere", "IP", "DS-2CD2143G2-I", "", 0), new(2, "Camere", "IP", "Camera IP 8MP", "", 0), new(3, "Retea", "Switch", "Switch PoE 24 porturi", "", 0), new(4, "Diverse", "Diverse", "Cablu UTP", "", 0)
        };
        var ranked = OfferProductMatcher.Rank("Camera IP AcuSense 4MP DS-2CD2143G2-I lentila 2.8mm\nHikvision", products);
        var beneficiaries = new List<Beneficiary> { new(1, "COMUNA VETEL", "123"), new(2, "SC Telesystem SRL", "456"), new(3, "Popescu Ion", "1234567890123", Kind: BeneficiaryKinds.Individual) };
        var similar = OfferBeneficiaryMatcher.Rank("Telesystem", beneficiaries);
        var none = OfferBeneficiaryMatcher.Rank("Alt Client Necunoscut", beneficiaries);
        var before = new List<OfferLineRecord> { new(1, 1, 1, "Echipamente", "1", "", "Camera A", "Buc", 4, true, null, null), new(2, 1, 2, "Echipamente", "2", "", "Switch B", "Buc", 1, true, null, null) };
        var after = new List<OfferImportLine> { new() { Section = "Echipamente", Name = "Camera A", Quantity = 6, InStock = true }, new() { Section = "Echipamente", Name = "Router C", Quantity = 1, InStock = true } };
        var diff = OfferDiffRules.Compare(1, before, after);
        check(ranked.Count > 0 && ranked[0].Product.Id == 1 && ranked[0].Score >= OfferProductMatcher.AutoScore && ranked.All(item => item.Product.Id != 3 && item.Product.Id != 4)
              && similar.Count == 1 && similar[0].Beneficiary.Id == 2 && similar[0].Score >= 0.8 && none.Count == 0
              && diff is { Added: 1, Removed: 1, QuantityChanged: 1, Unchanged: 0 } && diff.Details.Count == 3,
            "Offer lines are matched to the catalog by the words of the product, beneficiaries without legal forms, and a revision shows added, removed and changed lines");
        check(chosen?.Name == "A" && other is null, "The template is chosen by the labels of the offer (the same labels fit, a different table header does not; inactive ones are skipped)");
    }
}
