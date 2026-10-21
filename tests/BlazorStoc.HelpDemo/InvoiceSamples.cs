using BlazorStoc.Checks;

// Sample invoices of the fictive suppliers, uploaded by the help captures in Setari -> Facturi (template of a PDF and of an XML) and in Preluare factura.
static class InvoiceSamples
{
    public static void Write(string directory)
    {
        Directory.CreateDirectory(directory);
        var rows = new List<FixtureRow>
        {
            new("CAM-IP-4MP-DOM Camera IP dome 4 MP, IR 30 m, PoE", 6, 410.00m, 2460.00m, "buc"),
            new("PIR-DUAL-WL Senzor PIR dual tehnologie, wireless", 10, 95.50m, 955.00m, "buc"),
            new("SURSA-12V-5A Sursa stabilizata 12 V / 5 A", 4, 58.25m, 233.00m, "buc"),
            new("UTP-CAT6-ROLA Cablu UTP Cat.6, rola 305 m", 2, 520.00m, 1040.00m, "buc"),
            new("SW-POE-8P Switch PoE 8 porturi + 2 uplink", 3, 365.00m, 1095.00m, "buc")
        };
        var spec = new InvoiceSpec("ro-lines", 5, 1, SupplierName: "Alfa Securitate Distributie SRL", SupplierCui: "RO31548705", Number: "ASD 7001", Date: DateOnly.FromDateTime(DateTime.Today));
        File.WriteAllBytes(Path.Combine(directory, "factura-demo.pdf"), InvoiceFixtures.Make(spec, rows).Pdf);

        // The same kind of invoice on two pages (the tabs of the pages of the invoice).
        var longRows = new List<FixtureRow>();
        var catalog = new (string Name, decimal Price)[] { ("CAM-IP-4MP-DOM Camera IP dome 4 MP", 410.00m), ("CAM-IP-4MP-BUL Camera IP bullet 4 MP", 445.00m), ("NVR-8CH-POE NVR 8 canale cu switch PoE", 980.00m), ("HDD-4TB-SURV Hard disk 4 TB", 330.00m),
            ("SW-POE-24P Switch PoE 24 porturi", 1450.00m), ("UTP-CAT6-ROLA Cablu UTP Cat.6, rola 305 m", 520.00m), ("CONECTOR-RJ45 Conector RJ45 Cat.6", 95.00m), ("SURSA-12V-5A Sursa stabilizata 12 V / 5 A", 58.25m),
            ("ACUM-12V-7AH Acumulator 12 V / 7 Ah", 74.00m), ("CIT-RFID-125 Cititor RFID 125 kHz", 165.00m), ("YALA-ELECTROM-300 Yala electromagnetica 300 kg", 210.00m), ("BUTON-IESIRE Buton de iesire, inox", 38.00m),
            ("CTRL-ACCES-2U Controller acces 2 usi", 640.00m), ("SUPORT-PERETE-ALB Suport de perete pentru camera", 22.00m) };
        for (var i = 0; i < catalog.Length; i++) longRows.Add(new(catalog[i].Name, i + 2, catalog[i].Price, (i + 2) * catalog[i].Price, "buc"));
        var longSpec = new InvoiceSpec("ro-lines", longRows.Count, 1, RowsFirstPage: 8, SupplierName: "Alfa Securitate Distributie SRL", SupplierCui: "RO31548705", Number: "ASD 7002", Date: DateOnly.FromDateTime(DateTime.Today));
        File.WriteAllBytes(Path.Combine(directory, "factura-demo-2p.pdf"), InvoiceFixtures.Make(longSpec, longRows).Pdf);
        File.WriteAllBytes(Path.Combine(directory, "oferta-demo.xlsx"), OfferSample());

        var today = DateTime.Today.ToString("yyyy-MM-dd");
        var xml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <Invoice xmlns="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2" xmlns:cac="urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2" xmlns:cbc="urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2">
            <cbc:ID>NET 8812</cbc:ID><cbc:IssueDate>{today}</cbc:IssueDate><cbc:DocumentCurrencyCode>RON</cbc:DocumentCurrencyCode>
            <cac:AccountingSupplierParty><cac:Party><cac:PartyTaxScheme><cbc:CompanyID>RO36725093</cbc:CompanyID></cac:PartyTaxScheme><cac:PartyLegalEntity><cbc:RegistrationName>Nordic Electronic Trade SRL</cbc:RegistrationName></cac:PartyLegalEntity></cac:Party></cac:AccountingSupplierParty>
            <cac:LegalMonetaryTotal><cbc:PayableAmount>1850.00</cbc:PayableAmount></cac:LegalMonetaryTotal>
            <cac:InvoiceLine><cbc:ID>1</cbc:ID><cbc:InvoicedQuantity unitCode="H87">4.0000</cbc:InvoicedQuantity><cbc:LineExtensionAmount>1160.00</cbc:LineExtensionAmount><cac:Item><cbc:Name>Controller de acces pentru 2 usi</cbc:Name><cac:SellersItemIdentification><cbc:ID>CTRL-ACCES-2U</cbc:ID></cac:SellersItemIdentification></cac:Item><cac:Price><cbc:PriceAmount>290.00</cbc:PriceAmount></cac:Price></cac:InvoiceLine>
            <cac:InvoiceLine><cbc:ID>2</cbc:ID><cbc:InvoicedQuantity unitCode="H87">10.0000</cbc:InvoicedQuantity><cbc:LineExtensionAmount>690.00</cbc:LineExtensionAmount><cac:Item><cbc:Name>Cititor RFID 125 kHz, IP65</cbc:Name><cac:SellersItemIdentification><cbc:ID>CIT-RFID-125</cbc:ID></cac:SellersItemIdentification></cac:Item><cac:Price><cbc:PriceAmount>69.00</cbc:PriceAmount></cac:Price></cac:InvoiceLine>
            </Invoice>
            """;
        File.WriteAllText(Path.Combine(directory, "factura-demo.xml"), xml);
        Console.WriteLine("Facturi demo scrise in " + directory);
    }

    private static byte[] OfferSample()
    {
        var rows = new List<(int Row, string[] Cells)>
        {
            (1, ["A=s:Societatea:", "C=s:Instalari Securitate Demo SRL"]), (8, ["A=s:Oferta Nr: ", "E=i:250700110"]), (9, ["A=s:Extindere sistem de supraveghere video"]),
            (10, ["A=s:Categoria:", "C=s:CCTV"]), (11, ["A=s:Beneficiar:", "C=s:Panoramic Hotel & Spa SRL"]), (15, ["A=s:Echipamente"])
        };
        string[] header = ["A=s:Nr.", "B=s:Tip produs", "D=s:Denumire", "F=s:Unitate\r\nmasura", "G=s:Cantitate", "H=s:Pret\r\nunitar", "J=s:Valoare"];
        rows.Add((16, header));
        rows.Add((17, ["A=n:1", "B=s:Camere", "D=s:Camera IP dome 4 MP IR 30 m PoE", "F=s:Buc", "G=n:6", "H=n:410"]));
        rows.Add((18, ["A=n:2", "B=s:NVR", "D=s:NVR 8 canale cu switch PoE", "F=s:Buc", "G=n:1", "H=n:980"]));
        rows.Add((19, ["A=n:3", "B=s:Cabluri", "D=s:Cablu UTP Cat.6 rola 305 m", "F=s:Buc", "G=n:3", "H=n:520"]));
        rows.Add((20, ["A=n:4", "B=s:Retea", "D=s:Switch PoE 8 porturi + 2 uplink", "F=s:Buc", "G=n:2", "H=n:365"]));
        rows.Add((21, ["A=n:5", "B=s:Diverse", "D=s:Suport special pentru camera de exterior", "F=s:Buc", "G=n:6", "H=n:45"]));
        rows.Add((22, ["A=n:6", "B=s:Cabluri", "D=s:Cablu alimentare camere exterior", "F=s:m", "G=n:120", "H=n:4"]));
        rows.Add((23, ["A=s:Total echipamente:", "H=n:9230", "M=n:19"]));
        rows.Add((24, ["A=s:Manopera"]));
        rows.Add((25, header));
        rows.Add((26, ["A=n:1", "B=s:Servicii", "D=s:Montaj si configurare camere", "F=s:Ora", "G=n:16", "H=n:60"]));
        rows.Add((27, ["A=s:Total manopera:", "H=n:960"]));
        var shared = new List<string>();
        var sheet = new System.Text.StringBuilder("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
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
        foreach (var merge in new[] { "A1:B1", "C1:J1", "A8:D8", "A9:J9", "D17:E17" }) sheet.Append($"<mergeCell ref=\"{merge}\"/>");
        sheet.Append("</mergeCells></worksheet>");
        var strings = new System.Text.StringBuilder("<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
        foreach (var text in shared) strings.Append($"<si><t xml:space=\"preserve\">{System.Security.SecurityElement.Escape(text)}</t></si>");
        strings.Append("</sst>");
        using var stream = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string path, string content) { using var writer = new StreamWriter(archive.CreateEntry(path).Open(), new System.Text.UTF8Encoding(false)); writer.Write(content); }
            Add("xl/workbook.xml", "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Oferta\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            Add("xl/_rels/workbook.xml.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Target=\"worksheets/sheet1.xml\" Type=\"worksheet\"/></Relationships>");
            Add("xl/sharedStrings.xml", strings.ToString());
            Add("xl/worksheets/sheet1.xml", sheet.ToString());
        }
        return stream.ToArray();
    }
}
