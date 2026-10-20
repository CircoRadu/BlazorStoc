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
}
