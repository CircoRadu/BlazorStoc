namespace BlazorStoc.Services;

public sealed record InvoiceMeaningInfo(string Key, string Title);

// What the words of an invoice mean. The dictionaries below are DATA: Romanian, English and German labels, normalised (see
// InvoiceValues.Normalize), extended by adding a line - never by a condition on a supplier. A label matches a phrase when the
// phrase's words appear in it one after another; a label word matches a phrase word when they are equal, or (for phrase words of four
// letters or more) when the label word starts with it ("pretul" ~ "pret", "cantitatea" ~ "cantitate").
public static class InvoiceVocabulary
{
    public static readonly IReadOnlyList<InvoiceMeaningInfo> FieldMeanings =
    [
        new(InvoiceFieldMeanings.SupplierName, "Furnizor — denumire"),
        new(InvoiceFieldMeanings.SupplierCui, "Furnizor — CUI / cod TVA"),
        new(InvoiceFieldMeanings.SupplierRegistry, "Furnizor — nr. înregistrare"),
        new(InvoiceFieldMeanings.BuyerName, "Client — denumire"),
        new(InvoiceFieldMeanings.BuyerCui, "Client — CUI / cod TVA"),
        new(InvoiceFieldMeanings.InvoiceNumber, "Număr factură"),
        new(InvoiceFieldMeanings.InvoiceDate, "Data emiterii"),
        new(InvoiceFieldMeanings.DueDate, "Data scadenței"),
        new(InvoiceFieldMeanings.Currency, "Moneda"),
        new(InvoiceFieldMeanings.TotalNet, "Total fără TVA"),
        new(InvoiceFieldMeanings.TotalVat, "Total TVA"),
        new(InvoiceFieldMeanings.Total, "Total de plată (cu TVA)"),
        new(InvoiceFieldMeanings.OrderNumber, "Număr comandă"),
        new(InvoiceFieldMeanings.Custom, "Alt câmp (denumire proprie)")
    ];

    public static readonly IReadOnlyList<InvoiceMeaningInfo> ColumnMeanings =
    [
        new(InvoiceColumnMeanings.Index, "Nr. crt. / linia"),
        new(InvoiceColumnMeanings.Code, "Cod produs"),
        new(InvoiceColumnMeanings.Name, "Denumire"),
        new(InvoiceColumnMeanings.Unit, "UM"),
        new(InvoiceColumnMeanings.Quantity, "Cantitate"),
        new(InvoiceColumnMeanings.UnitPrice, "Preț unitar"),
        new(InvoiceColumnMeanings.VatRate, "Cota TVA"),
        new(InvoiceColumnMeanings.VatAmount, "Valoare TVA"),
        new(InvoiceColumnMeanings.Value, "Valoare (fără TVA)"),
        new(InvoiceColumnMeanings.ValueWithVat, "Valoare cu TVA"),
        new(InvoiceColumnMeanings.Discount, "Discount"),
        new(InvoiceColumnMeanings.Currency, "Moneda"),
        new(InvoiceColumnMeanings.Ignore, "Nefolosit")
    ];

    public static string FieldTitle(string meaning) => meaning.Length == 0 ? "Câmp propriu" : FieldMeanings.FirstOrDefault(item => item.Key == meaning)?.Title ?? "Nespecificat";
    public static string ColumnTitle(string meaning) => ColumnMeanings.FirstOrDefault(item => item.Key == meaning)?.Title ?? "Nespecificat";

    // Columns of the line table, by header text.
    private static readonly (string Meaning, string[] Phrases)[] ColumnPhrases =
    [
        (InvoiceColumnMeanings.Index, ["nr crt", "crt", "linia", "linie", "line", "line no", "pos", "poz", "pozitie", "pozitia", "no", "nr", "#", "item no", "nr linie", "item"]),
        (InvoiceColumnMeanings.Code, ["cod", "cod articol", "cod produs", "cod furnizor", "cod intern", "cod marfa", "sku", "code", "item code", "product code",
            "art no", "artikelnummer", "ref", "referinta", "part number", "part no", "cod bare", "ean"]),
        (InvoiceColumnMeanings.Name, ["denumire", "denumire produs", "denumire produse", "denumire produse servicii", "denumirea produselor sau serviciilor",
            "denumirea produselor si a serviciilor", "nume articol", "nume articol descriere articol", "descriere articol", "descriere", "produs", "produse", "articol",
            "servicii", "description", "item description", "product", "name", "bezeichnung", "detalii", "explicatii"]),
        (InvoiceColumnMeanings.Unit, ["um", "u m", "unitate", "unitate de masura", "unit", "uom", "unit of measure", "einheit"]),
        (InvoiceColumnMeanings.Quantity, ["cantitate", "cant", "qty", "quantity", "cantitate facturata", "buc", "bucati", "menge"]),
        (InvoiceColumnMeanings.UnitPrice, ["pret", "pret unitar", "pretul net", "pret net", "pret fara tva", "pret unitar fara tva", "unit price", "price", "pret unit",
            "pret buc", "valoare unitara", "einzelpreis", "preis"]),
        (InvoiceColumnMeanings.VatRate, ["cota tva", "tva", "vat", "vat rate", "cota", "tva %", "mwst", "taxa"]),
        (InvoiceColumnMeanings.VatAmount, ["valoare tva", "suma tva", "vat amount", "tva valoare", "tva lei", "tva ron"]),
        (InvoiceColumnMeanings.Value, ["valoare", "valoare neta", "valoare fara tva", "valoare linie", "total", "total linie", "suma", "amount", "line total",
            "net amount", "val", "valoare lei", "valoare ron", "gesamtpreis", "betrag", "total fara tva", "valoare totala"]),
        (InvoiceColumnMeanings.ValueWithVat, ["valoare cu tva", "total cu tva", "valoare bruta", "gross amount", "total linie cu tva"]),
        (InvoiceColumnMeanings.Discount, ["discount", "reducere", "rabat", "disc"]),
        (InvoiceColumnMeanings.Currency, ["moneda", "valuta", "currency", "wahrung"]),
        // Recognised so that they are not mistaken for something else; the user sees them as unused columns.
        (InvoiceColumnMeanings.Ignore, ["cantitate de baza", "tara provenient", "tara", "country", "tara de origine", "cod nc8", "cn", "nr comanda", "observatii"])
    ];

    // Words that show that a line of text is the header of a table of goods (used to tell the header band from the rest).
    private static readonly Dictionary<string, double> ColumnWeights = new()
    {
        [InvoiceColumnMeanings.Name] = 2, [InvoiceColumnMeanings.Quantity] = 2, [InvoiceColumnMeanings.Value] = 2,
        [InvoiceColumnMeanings.UnitPrice] = 1.5, [InvoiceColumnMeanings.Index] = 1, [InvoiceColumnMeanings.Unit] = 1, [InvoiceColumnMeanings.Code] = 1,
        [InvoiceColumnMeanings.VatRate] = 1, [InvoiceColumnMeanings.VatAmount] = 0.5, [InvoiceColumnMeanings.ValueWithVat] = 0.5,
        [InvoiceColumnMeanings.Discount] = 0.3, [InvoiceColumnMeanings.Currency] = 0.3, [InvoiceColumnMeanings.Ignore] = 0
    };

    public static double ColumnWeight(string meaning) => ColumnWeights.GetValueOrDefault(meaning, 0);

    public static string[] Tokens(string? text) => InvoiceValues.Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);

    // The dictionary phrases are fixed text: they are tokenised once (the header search tries every phrase on every candidate cell).
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string[]> PhraseTokenCache = new(StringComparer.Ordinal);
    private static string[] PhraseTokens(string phrase) => PhraseTokenCache.GetOrAdd(phrase, static text => Tokens(text));

    // The meaning of a column header: the dictionary phrase that is the best match (more words, then an exact match wins). Ignore = known
    // but unused; "" = unknown header.
    public static (string Meaning, double Score) MatchColumn(string? label)
    {
        var tokens = Tokens(label);
        if (tokens.Length == 0) return ("", 0);
        var best = "";
        var bestScore = 0.0;
        foreach (var (meaning, phrases) in ColumnPhrases)
            foreach (var phrase in phrases)
            {
                var phraseTokens = PhraseTokens(phrase);
                if (phraseTokens.Length == 0) continue;
                var start = FindPhrase(tokens, 0, phraseTokens);
                if (start < 0) continue;
                var score = phraseTokens.Length + (phraseTokens.Length == tokens.Length ? 0.5 : 0) - 0.05 * (tokens.Length - phraseTokens.Length);
                // A phrase that only covers a small part of a long label says little ("Nume articol/Descriere articol" is a name, but
                // a sentence that happens to contain "total" is not a value column).
                if (tokens.Length > phraseTokens.Length * 3 + 1) score -= 1;
                if (score > bestScore) { best = meaning; bestScore = score; }
            }
        return (best, bestScore);
    }

    // Index of the first label word where the phrase matches word by word, or -1. from: first label word that may start the phrase.
    public static int FindPhrase(string[] tokens, int from, string[] phrase)
    {
        for (var start = from; start + phrase.Length <= tokens.Length; start++)
            if (PhraseAt(tokens, start, phrase)) return start;
        return -1;
    }

    public static bool PhraseAt(string[] tokens, int start, string[] phrase)
    {
        if (start + phrase.Length > tokens.Length) return false;
        for (var i = 0; i < phrase.Length; i++)
        {
            var token = tokens[start + i];
            var wanted = phrase[i];
            if (token == wanted) continue;
            if (wanted.Length >= 4 && token.StartsWith(wanted, StringComparison.Ordinal) && token.Length <= wanted.Length + 4) continue;
            return false;
        }
        return true;
    }

    public const string NameLabel = "@name";
    public const string CuiLabel = "@cui";
    public const string RegistryLabel = "@registry";

    // Labels of the fields outside the table. A Meaning starting with "@" depends on the section (supplier / buyer).
    private static readonly (string Meaning, string[] Phrases)[] FieldPhrases =
    [
        (NameLabel, ["nume", "denumire", "denumirea", "firma", "societate", "company name", "name"]),
        (CuiLabel, ["cui", "cif", "cod fiscal", "cod de identificare fiscala", "identificatorul tva", "identificator tva", "identificator", "identificat", "nr tva",
            "cod tva", "vat id", "vat no", "vat number", "tax id", "c i f", "c u i", "nr identificare fiscala"]),
        (RegistryLabel, ["nr inregistrare", "nr reg com", "nr registrul comertului", "reg com", "registrul comertului", "registration no", "company no", "j"]),
        (InvoiceFieldMeanings.InvoiceNumber, ["nr factura", "numar factura", "factura nr", "factura numar", "factura seria si numarul", "seria si numarul", "nr document",
            "numar document", "invoice no", "invoice number", "invoice nr", "rechnungsnummer", "factura", "invoice", "nr factura fiscala"]),
        (InvoiceFieldMeanings.InvoiceDate, ["data emitere", "data emiterii", "data factura", "data facturii", "data emiterii facturii", "invoice date", "issue date",
            "rechnungsdatum", "data", "date"]),
        (InvoiceFieldMeanings.DueDate, ["data scadenta", "data scadentei", "scadenta", "scadent la", "due date", "payment due", "faelligkeit", "termen de plata data"]),
        (InvoiceFieldMeanings.Currency, ["moneda facturii", "moneda", "valuta", "currency", "wahrung"]),
        (InvoiceFieldMeanings.TotalNet, ["total fara tva", "valoare totala fara tva", "total net", "baza de calcul", "subtotal", "total valoare fara tva", "sub total",
            "net total", "total excl vat", "valoare fara tva", "total baza"]),
        (InvoiceFieldMeanings.TotalVat, ["total tva", "valoare tva", "tva total", "total taxe", "vat total", "total vat"]),
        (InvoiceFieldMeanings.Total, ["total plata", "total de plata", "total general", "valoare totala cu tva", "total cu tva", "de plata", "total factura", "grand total",
            "amount due", "total amount", "suma de plata", "total de achitat", "valoare totala", "total incl vat", "gesamtbetrag"]),
        (InvoiceFieldMeanings.OrderNumber, ["nr comanda", "numar comanda", "comanda", "order no", "order number", "purchase order", "bestellnummer"]),
        // Recognised labels without a meaning of their own: they keep the values next to them from being taken for labels.
        ("", ["strada", "oras", "localitate", "judet", "regiune", "regiun", "tara", "cod postal", "cod", "adresa", "adresa electronica", "telefon", "tel", "fax", "email",
            "e mail", "banca", "iban", "cont", "nr cont de plata", "capital social", "persoana de contact", "informatii juridice", "data de exigibilitate",
            "moneda contabilizare", "codul tipului", "termeni de plata", "termen de plata", "reprezentant", "delegat", "sediul", "sediu", "adresa livrare",
            "referinta avizului de expeditie", "numele contului de plata", "nota", "observatii", "cota tva", "total deduceri", "total taxe suplimentare",
            "suma platita", "valoare de rotunjire", "total plata", "nume", "pagina"])
    ];

    // Headings of the two sides of an invoice; the labels under them belong to the supplier or to the buyer.
    public const string SupplierSection = "supplier";
    public const string BuyerSection = "buyer";
    private static readonly (string Section, string[] Phrases)[] SectionPhrases =
    [
        (SupplierSection, ["vanzator", "furnizor", "emitent", "supplier", "seller", "vendor", "verkaufer", "lieferant"]),
        (BuyerSection, ["cumparator", "client", "beneficiar", "destinatar", "buyer", "customer", "bill to", "sold to", "kaufer", "kunde"])
    ];

    // The section a heading opens: its text must be (almost) only the heading word ("VANZATOR", "Furnizor:").
    public static string? MatchSection(string? text)
    {
        var tokens = Tokens(text);
        if (tokens.Length is 0 or > 3) return null;
        foreach (var (section, phrases) in SectionPhrases)
            foreach (var phrase in phrases)
            {
                var phraseTokens = PhraseTokens(phrase);
                if (tokens.Length == phraseTokens.Length && PhraseAt(tokens, 0, phraseTokens)) return section;
            }
        return null;
    }

    // Longest label phrase at the start of the tokens: (number of words it covers, meaning) - or (0, null) when the text does not
    // start with a known label. Longer phrases win, so "data scadenta" is not read as the label "data".
    public static (int Words, string? Meaning) MatchFieldLabelPrefix(string[] tokens)
    {
        var bestWords = 0;
        string? bestMeaning = null;
        foreach (var (meaning, phrases) in FieldPhrases)
            foreach (var phrase in phrases)
            {
                var phraseTokens = PhraseTokens(phrase);
                if (phraseTokens.Length <= bestWords || !PhraseAt(tokens, 0, phraseTokens)) continue;
                bestWords = phraseTokens.Length;
                bestMeaning = meaning;
            }
        return (bestWords, bestMeaning);
    }

    // The meaning a label has in a section ("@name" in the supplier section is the supplier's name).
    public static string ResolveFieldMeaning(string? labelMeaning, string? section) => labelMeaning switch
    {
        null or "" => "",
        NameLabel => section == SupplierSection ? InvoiceFieldMeanings.SupplierName : section == BuyerSection ? InvoiceFieldMeanings.BuyerName : "",
        CuiLabel => section == SupplierSection ? InvoiceFieldMeanings.SupplierCui : section == BuyerSection ? InvoiceFieldMeanings.BuyerCui : "",
        RegistryLabel => section == SupplierSection ? InvoiceFieldMeanings.SupplierRegistry : "",
        _ => labelMeaning
    };

    // Words that begin the lines under the table (totals, payment instructions, notes): the table ends before them.
    private static readonly string[] FooterPhrases =
    [
        "total", "subtotal", "total plata", "total de plata", "total factura", "instructiuni de plata", "nota", "note", "observatii", "banca", "iban", "cont",
        "semnatura", "semnaturi", "cod nc8", "termen de plata", "conditii de plata", "detalierea tva", "total tva", "total general", "grand total", "payment",
        "amount due", "informatii referitoare la livrare", "intocmit de", "data scadenta", "scadenta"
    ];

    public static bool IsFooterStart(string? text)
    {
        var tokens = Tokens(text);
        if (tokens.Length == 0) return false;
        foreach (var phrase in FooterPhrases)
        {
            var phraseTokens = PhraseTokens(phrase);
            if (PhraseAt(tokens, 0, phraseTokens)) return true;
        }
        return false;
    }
}
