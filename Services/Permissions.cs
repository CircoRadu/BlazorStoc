namespace BlazorStoc.Services;

// The permission catalog: every module of the application and the actions a user type can be given on it. A permission key is "module.action".
// The configuration screen (Setari -> Tipuri de utilizatori) is generated from this list, so a new module appears there by being added here.
// UserDefault marks what the system type "Utilizator" has (what every non-administrator could do before user types existed); the
// administrator type always has every key.
public sealed record PermissionAction(string Id, string Label, bool UserDefault = true);

public sealed record PermissionModule(string Id, string Title, string Group, IReadOnlyList<PermissionAction> Actions)
{
    public string Key(string action) => Permissions.Key(Id, action);
}

public static class Permissions
{
    public const string View = "view";
    public const string Add = "add";
    public const string Edit = "edit";
    public const string Delete = "delete";
    public const string Export = "export";

    public static string Key(string module, string action) => $"{module}.{action}";

    // noun: what is added, edited or deleted, when the module name alone would be ambiguous (the products, not their stock movements).
    private static PermissionAction[] Crud(bool export = false, bool admin = false, string noun = "")
    {
        var suffix = noun.Length == 0 ? "" : " " + noun;
        var actions = new List<PermissionAction>
        {
            new(View, "Vizualizare", !admin), new(Add, "Adaugare" + suffix, !admin), new(Edit, "Editare" + suffix, !admin), new(Delete, "Stergere" + suffix, !admin)
        };
        if (export) actions.Add(new(Export, "Export", !admin));
        return [.. actions];
    }

    private static PermissionAction[] Plus(PermissionAction[] actions, PermissionAction extra) => [.. actions, extra];

    public static readonly IReadOnlyList<PermissionModule> Modules =
    [
        new("stoc", "Stoc (lista de produse, cantitati, intrari si iesiri)", "Produse", [new(View, "Vizualizare stoc"), new("intrare", "Intrare in stoc"), new("iesire", "Iesire din stoc"), new("modificare", "Modificare / stergere miscare"), new("stornare", "Stornare / returnare"), new("minim", "Setare stoc minim"), new("rezervare", "Rezervare produse pentru proiect")]),
        new("produse", "Produse (adaugare / modificare in lista de stoc)", "Produse", [new(Add, "Adaugare produs"), new(Edit, "Editare produs"), new(Delete, "Stergere produs"), new(Export, "Export")]),
        new("preluare-factura", "Preluare factura", "Produse", [new(View, "Vizualizare"), new(Add, "Preluare in stoc")]),
        new("iesire-multipla", "Iesire multipla", "Produse", [new(View, "Vizualizare"), new(Add, "Efectuare iesire")]),
        new("export-consum", "Export consum", "Produse", [new(View, "Vizualizare"), new(Export, "Export")]),
        new("inventar", "Generare situatie inventar", "Inventar", [new(View, "Vizualizare"), new(Export, "Export")]),
        new("inventar-preluare", "Preluare inventar", "Inventar", [new(View, "Vizualizare"), new(Add, "Preluare inventar")]),
        new("categorii", "Categorii", "Administrare", Crud()),
        new("beneficiari", "Beneficiari", "Administrare", Crud(export: true)),
        new("furnizori", "Furnizori", "Administrare", Crud()),
        new("facturi", "Facturi", "Administrare", Crud(export: true)),
        new("vehicule", "Vehicule", "Administrare", Crud()),
        new("oferte", "Oferte", "Oferte", Plus(Crud(export: true), new PermissionAction("sabloane", "Sabloane de oferta"))),
        new("mentenanta", "Mentenanta", "Mentenanta", Plus(Crud(export: true), new PermissionAction("contracte", "Activare contracte"))),
        new("analize-risc", "Analize de risc", "Analize de risc", Crud()),
        new("notificari", "Notificari", "Notificari", [new(View, "Vizualizare"), new(Edit, "Marcare / rezolvare")]),
        new("setari-facturi", "Setari: Facturi (sabloane)", "Setari", [new(View, "Vizualizare"), new(Add, "Creare sablon"), new(Edit, "Modificare sablon"), new(Delete, "Stergere sablon", false)]),
        new("setari-anaf", "Setari: Preluare date ANAF", "Setari", [new(View, "Vizualizare", false), new(Edit, "Modificare", false)]),
        new("setari-notificari", "Setari: Notificari", "Setari", [new(View, "Vizualizare", false), new(Edit, "Modificare", false)]),
        new("setari-harta", "Setari: Harta", "Setari", [new(View, "Vizualizare", false), new(Edit, "Modificare", false)]),
        new("setari-backup", "Setari: Backup", "Setari", [new(View, "Vizualizare", false), new(Edit, "Modificare setari", false), new("backup", "Efectuare backup", false), new("restore", "Restaurare", false)]),
        new("utilizatori", "Utilizatori", "Administrare sistem", Crud(admin: true)),
        new("tipuri-utilizatori", "Tipuri de utilizatori", "Administrare sistem", Crud(admin: true)),
        new("nomenclator", "Nomenclator", "Administrare sistem", Crud(admin: true)),
        new("jurnal", "Jurnal activitate", "Administrare sistem", [new(View, "Vizualizare", false), new(Export, "Export", false)])
    ];

    public static IReadOnlyList<string> AllKeys { get; } = [.. Modules.SelectMany(module => module.Actions.Select(action => module.Key(action.Id)))];

    // What the system type "Utilizator" starts with: the current rights of a non-administrator.
    public static IReadOnlyList<string> DefaultUserKeys { get; } =
        [.. Modules.SelectMany(module => module.Actions.Where(action => action.UserDefault).Select(action => module.Key(action.Id)))];

    // The keys of a module that change or hand out something (every action but viewing).
    public static IReadOnlyList<string> WriteKeys(string module) =>
        Modules.Single(item => item.Id == module).Actions.Where(action => action.Id != View).Select(action => Key(module, action.Id)).ToArray();

    public static bool IsKnown(string key) => AllKeys.Contains(key, StringComparer.Ordinal);

    // Consistency: any action on a module implies viewing it, and unknown keys are dropped. Returns the keys in catalog order.
    public static IReadOnlyList<string> Normalize(IEnumerable<string> keys)
    {
        var set = keys.Where(IsKnown).ToHashSet(StringComparer.Ordinal);
        foreach (var module in Modules)
            if (module.Actions.Any(action => set.Contains(module.Key(action.Id))) && module.Actions.Any(action => action.Id == View))
                set.Add(module.Key(View));
        return [.. AllKeys.Where(set.Contains)];
    }
}
