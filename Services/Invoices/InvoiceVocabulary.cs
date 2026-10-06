namespace BlazorStoc.Services;

public sealed record InvoiceMeaningInfo(string Key, string Title);

// What the words of an invoice mean. The dictionaries below are DATA: Romanian, English and German labels, normalised (see
// InvoiceValues.Normalize), extended by adding a line - never by a condition on a supplier. A label matches a phrase when the
// phrase's words appear in it one after another; a label word matches a phrase word when they are equal, or (for phrase words of four
// letters or more) when the label word starts with it ("pretul" ~ "pret", "cantitatea" ~ "cantitate").
public static class InvoiceVocabulary
{
    // What an invoice says about each of its two parties. The same attributes exist for the supplier ("supplier.<key>") and the buyer
    // ("buyer.<key>"); the label dictionary gives the attribute ("@<key>"), the heading the label stands under gives the party.
    private static readonly (string Key, string Title, string[] Phrases)[] PartyAttributes =
    [
        ("name", "denumire", ["nume", "denumire", "denumirea", "firma", "societate", "company name", "name"]),
        ("cui", "CUI / cod TVA", ["cui", "cif", "cod fiscal", "cod de identificare fiscala", "identificatorul tva", "identificator tva", "identificator", "identificat", "nr tva",
            "cod tva", "vat id", "vat no", "vat number", "tax id", "c i f", "c u i", "nr identificare fiscala"]),
        ("registry", "nr. înregistrare", ["nr inregistrare", "nr reg com", "nr registrul comertului", "reg com", "reg comertului", "registrul comertului", "registration no", "company no", "j"]),
        ("address", "adresa", ["strada", "adresa", "adresa sediu", "adresa sediu social", "sediu social", "sediul", "sediu", "address", "street", "anschrift"]),
        ("city", "localitatea", ["oras", "localitate", "city", "stadt"]),
        ("region", "județul / regiunea", ["judet", "regiune", "regiun", "county", "state", "region"]),
        ("country", "țara", ["tara", "country", "land"]),
        ("postalCode", "cod poștal", ["cod postal", "cod", "postal code", "zip", "zip code", "plz"]),
        ("contact", "persoana de contact", ["persoana de contact", "contact", "contact person", "ansprechpartner"]),
        ("phone", "telefon", ["telefon", "tel", "phone", "telephone", "telefonnummer"]),
        ("email", "e-mail", ["email", "e mail", "adresa electronica", "mail"]),
        ("bank", "banca", ["banca", "bank"]),
        ("iban", "cont IBAN", ["iban", "cont", "cont iban", "cont bancar", "nr cont de plata", "bank account"])
    ];

    // Name, tax code and registry number identify a party and are imported by default; the rest of what is said about a party (address,
    // phone, bank...) is recognised and grouped, but only imported when the user ticks it (it varies too much from one invoice to the next).
    public static bool IsExtraPartyAttribute(string meaning)
    {
        var dot = meaning.IndexOf('.');
        return dot > 0 && meaning[..dot] is SupplierSection or BuyerSection && meaning[(dot + 1)..] is not ("name" or "cui" or "registry");
    }

    public static readonly IReadOnlyList<InvoiceMeaningInfo> FieldMeanings =
    [
        .. PartyAttributes.Select(attribute => new InvoiceMeaningInfo(SupplierSection + "." + attribute.Key, "Furnizor — " + attribute.Title)),
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
        new(InvoiceColumnMeanings.Other, "Altă coloană (cu denumirea ei)"),
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
        (InvoiceColumnMeanings.VatRate, ["cota tva", "tva", "vat", "vat rate", "cota", "tva %", "mwst", "taxa tva"]),
        (InvoiceColumnMeanings.VatAmount, ["valoare tva", "suma tva", "vat amount", "tva valoare", "tva lei", "tva ron"]),
        (InvoiceColumnMeanings.Value, ["valoare", "valoare neta", "valoare fara tva", "valoare linie", "total", "total linie", "suma", "amount", "line total",
            "net amount", "val", "valoare lei", "valoare ron", "gesamtpreis", "betrag", "total fara tva", "valoare totala"]),
        (InvoiceColumnMeanings.ValueWithVat, ["valoare cu tva", "total cu tva", "valoare bruta", "gross amount", "total linie cu tva"]),
        (InvoiceColumnMeanings.Discount, ["discount", "reducere", "rabat", "disc"]),
        (InvoiceColumnMeanings.Currency, ["moneda", "valuta", "currency", "wahrung"]),
        // Recognised so that they are not mistaken for something else; the user sees them as unused columns.
        (InvoiceColumnMeanings.Ignore, ["cantitate de baza", "tara provenient", "tara", "country", "tara de origine", "cod nc8", "cn", "nr comanda", "observatii", "timbru verde", "timbru verde fara tva"])
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
                var start = FindPhrase(tokens, 0, phraseTokens, fuzzy: true);
                if (start < 0) continue;
                var score = phraseTokens.Length + (phraseTokens.Length == tokens.Length ? 0.5 : 0) - 0.05 * (tokens.Length - phraseTokens.Length);
                // A phrase that only covers a small part of a long label says little ("Nume articol/Descriere articol" is a name, but
                // a sentence that happens to contain "total" is not a value column).
                // A label that opens with the phrase is its heading, however long ("Denumirea produselor sau a serviciilor", whose middle word an
                // OCR read wrongly): it is not that penalty's case.
                if (tokens.Length > phraseTokens.Length * 3 + 1 && start > 0) score -= 1;
                if (score > bestScore) { best = meaning; bestScore = score; }
            }
        // A label whose words an OCR run together or broke apart ("Pretunitar ret unitar" for "Pret unitar"): the letters of the whole label, closed up, contain a long phrase.
        if (bestScore <= 0)
        {
            var closed = string.Concat(tokens);
            foreach (var (meaning, phrases) in ColumnPhrases)
                foreach (var phrase in phrases)
                {
                    var phraseTokens = PhraseTokens(phrase);
                    var closedPhrase = string.Concat(phraseTokens);
                    if (closedPhrase.Length < 8 || !closed.Contains(closedPhrase, StringComparison.Ordinal)) continue;
                    var score = 0.8 * phraseTokens.Length;
                    if (score > bestScore) { best = meaning; bestScore = score; }
                }
        }
        return (best, bestScore);
    }

    // Index of the first label word where the phrase matches word by word, or -1. from: first label word that may start the phrase.
    public static int FindPhrase(string[] tokens, int from, string[] phrase, bool fuzzy = false)
    {
        for (var start = from; start + phrase.Length <= tokens.Length; start++)
            if (PhraseAt(tokens, start, phrase, fuzzy)) return start;
        return -1;
    }

    // fuzzy: a word that the OCR misread by a letter or two still matches a long word of the phrase ("CANTIITATEA" for "cantitate", "proauseior" for "produselor").
    public static bool PhraseAt(string[] tokens, int start, string[] phrase, bool fuzzy = false)
    {
        if (start + phrase.Length > tokens.Length) return false;
        for (var i = 0; i < phrase.Length; i++)
        {
            var token = tokens[start + i];
            var wanted = phrase[i];
            if (token == wanted) continue;
            if (wanted.Length >= 4 && token.StartsWith(wanted, StringComparison.Ordinal) && token.Length <= wanted.Length + 4) continue;
            if (fuzzy && wanted.Length >= 7 && Math.Abs(token.Length - wanted.Length) <= 2 && EditDistance(token, wanted, wanted.Length >= 9 ? 2 : 1) <= (wanted.Length >= 9 ? 2 : 1)) continue;
            return false;
        }
        return true;
    }

    // The number of single-letter changes (insert, delete, change) that turn one word into the other, or max + 1 when it is more than max.
    private static int EditDistance(string left, string right, int max)
    {
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        for (var i = 1; i <= left.Length; i++)
        {
            var current = new int[right.Length + 1];
            current[0] = i;
            var rowMinimum = current[0];
            for (var j = 1; j <= right.Length; j++)
            {
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + (left[i - 1] == right[j - 1] ? 0 : 1));
                rowMinimum = Math.Min(rowMinimum, current[j]);
            }
            if (rowMinimum > max) return max + 1;
            previous = current;
        }
        return previous[right.Length];
    }

    // Labels of the fields outside the table. A Meaning starting with "@" is a party attribute: it depends on the section (supplier / buyer).
    private static readonly (string Meaning, string[] Phrases)[] FieldPhrases =
    [
        .. PartyAttributes.Select(attribute => ("@" + attribute.Key, attribute.Phrases)),
        (InvoiceFieldMeanings.InvoiceNumber, ["nr factura", "numar factura", "factura nr", "factura numar", "factura seria si numarul", "seria si numarul", "seria", "nr document",
            "numar document", "invoice no", "invoice number", "invoice nr", "rechnungsnummer", "factura", "invoice", "nr factura fiscala"]),
        (InvoiceFieldMeanings.InvoiceDate, ["data emitere", "data emiterii", "data factura", "data facturii", "data emiterii facturii", "invoice date", "issue date",
            "rechnungsdatum", "data", "date"]),
        (InvoiceFieldMeanings.DueDate, ["data scadenta", "data scadentei", "scadenta", "scadent la", "due date", "payment due", "faelligkeit", "termen de plata data"]),
        (InvoiceFieldMeanings.Currency, ["moneda facturii", "moneda", "valuta", "currency", "wahrung"]),
        (InvoiceFieldMeanings.TotalNet, ["total fara tva", "valoare totala fara tva", "total net", "baza de calcul", "subtotal", "total valoare fara tva", "sub total",
            "net total", "total excl vat", "valoare fara tva", "total baza", "total ron", "total lei"]),
        (InvoiceFieldMeanings.TotalVat, ["total tva", "valoare tva", "tva total", "total taxe", "vat total", "total vat"]),
        (InvoiceFieldMeanings.Total, ["total plata", "total de plata", "total general", "valoare totala cu tva", "total cu tva", "de plata", "total factura", "grand total",
            "amount due", "total amount", "suma de plata", "total de achitat", "valoare totala", "total incl vat", "gesamtbetrag"]),
        (InvoiceFieldMeanings.OrderNumber, ["nr comanda", "numar comanda", "comanda", "order no", "order number", "purchase order", "bestellnummer"]),
        // Recognised labels without a meaning of their own: they keep the values next to them from being taken for labels.
        ("", ["fax", "capital social", "informatii juridice", "data de exigibilitate",
            "moneda contabilizare", "codul tipului", "termeni de plata", "termen de plata", "reprezentant", "delegat", "adresa livrare",
            "referinta avizului de expeditie", "numele contului de plata", "nota", "observatii", "cota tva", "total deduceri", "total taxe suplimentare",
            "suma platita", "valoare de rotunjire", "total plata", "pagina"])
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

    // A run that opens with a party word and a colon and goes on with the party's name ("Furnizor: SC TELESYSTEM SRL"): the heading and
    // the name are on the same line. The section it opens, or null.
    public static string? MatchSectionLead(string? text)
    {
        var trimmed = (text ?? "").TrimStart();
        var colon = trimmed.IndexOf(':');
        if (colon <= 0 || colon == trimmed.Length - 1) return null;
        var lead = MatchSection(trimmed[..colon]);
        return lead is not null && Tokens(trimmed[..colon]).Length == 1 ? lead : null;
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
    public static string ResolveFieldMeaning(string? labelMeaning, string? section) =>
        string.IsNullOrEmpty(labelMeaning) ? ""
        : labelMeaning[0] == '@' ? (section is SupplierSection ? section + "." + labelMeaning[1..] : "")
        : labelMeaning;

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
