using BlazorStoc.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;

// Fills the database blazorstoc_demo (tools/help-demo-db.ps1) with FICTIVE demo data for the screenshots of the help (tools/help-capture.mjs): a small
// installer company's warehouse (security and fire alarm, video, access control, cabling), its suppliers, beneficiaries, vehicles, projects and maintenance
// contracts. Nothing here is a real company or person. It refuses to run on any database but blazorstoc_demo.
//   dotnet run --project tests/BlazorStoc.HelpDemo -c Release [-- --config path]
var repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
// --files <directory>: only writes the sample invoices (PDF and UBL XML, fictive suppliers) that the help captures upload; no database is touched.
var filesIndex = Array.IndexOf(args, "--files");
if (filesIndex >= 0) { InvoiceSamples.Write(args[filesIndex + 1]); return 0; }
var configIndex = Array.IndexOf(args, "--config");
var configPath = configIndex >= 0 ? args[configIndex + 1] : Path.Combine(repo, "local-secrets", "demo-database.private.json");
var configuration = new ConfigurationBuilder().AddJsonFile(configPath, optional: false).Build();
if (configuration["Database:Name"] != "blazorstoc_demo") { Console.WriteLine("Refuz: configuratia nu indica baza blazorstoc_demo."); return 1; }

var admin = new DemoAdministrator("claude.test");
var audit = new MariaAuditTrail(configuration);
var environment = new DemoEnvironment(Path.Combine(Path.GetTempPath(), "help-demo"));
Directory.CreateDirectory(environment.ContentRootPath);

var products = new MariaProductRepository(configuration, admin, audit);
var movements = new MariaStockMovementRepository(configuration, admin, audit);
var suppliers = new MariaSupplierRepository(configuration, admin, audit);
var invoices = new MariaSupplierInvoiceRepository(configuration, admin, audit);
var beneficiaries = new MariaBeneficiaryRepository(configuration, admin, audit);
var workPoints = new MariaWorkPointRepository(configuration, admin, audit);
var vehicles = new MariaVehicleRepository(configuration, admin, audit);
var projects = new MariaProjectRepository(configuration, environment, admin, audit);
var systemTypes = new MariaSystemTypeRepository(configuration, admin, audit);
var components = new MariaProjectComponentRepository(configuration, admin, audit, movements);
var contracts = new MariaServiceContractRepository(configuration, admin, audit);
var interventions = new MariaServiceInterventionRepository(configuration, admin, audit);
var minStock = new MariaProductMinStockRepository(configuration, admin, audit);
var riskAnalyses = new MariaRiskAnalysisRepository(configuration, admin, audit, null, TimeProvider.System);

if ((await products.GetProductsAsync()).Count > 0) { Console.WriteLine("Baza demo are deja produse; ruleaza intai tools/help-demo-db.ps1."); return 1; }
var today = DateOnly.FromDateTime(DateTime.Today);
DateOnly Ago(int days) => today.AddDays(-days);

// ---- Catalog ----------------------------------------------------------------------------------------------------------------------------------------------
var catalog = new (string Category, string Subcategory, (string Code, string Description, int Stock)[] Items)[]
{
    ("Supraveghere video", "Camere IP", [("CAM-IP-4MP-DOM", "Cameră IP dome 4 MP, IR 30 m, PoE", 18), ("CAM-IP-4MP-BUL", "Cameră IP bullet 4 MP, IR 50 m, PoE", 22), ("CAM-IP-8MP-PTZ", "Cameră PTZ 8 MP, zoom optic 25x, PoE+", 3), ("CAM-IP-2MP-MINI", "Cameră IP mini 2 MP pentru interior", 9)]),
    ("Supraveghere video", "Recordere", [("NVR-16CH-4K", "NVR 16 canale, 4K, 2 hard disk-uri", 4), ("NVR-8CH-POE", "NVR 8 canale cu switch PoE integrat", 6), ("HDD-4TB-SURV", "Hard disk 4 TB pentru supraveghere", 12)]),
    ("Supraveghere video", "Accesorii camere", [("SUPORT-PERETE-ALB", "Suport de perete pentru cameră, aluminiu", 40), ("CUTIE-JONCTIUNE", "Cutie de joncțiune exterior IP66", 35)]),
    ("Detecție efracție", "Centrale de alarmă", [("CENTR-ALARMA-32Z", "Centrală de alarmă 32 de zone, comunicator GSM", 5), ("CENTR-ALARMA-8Z", "Centrală de alarmă 8 zone, expandabilă", 7)]),
    ("Detecție efracție", "Senzori de mișcare", [("PIR-DUAL-WL", "Senzor PIR dual tehnologie, wireless", 25), ("PIR-CURTAIN-EXT", "Senzor tip perdea pentru exterior", 8), ("PIR-AM-FIR", "Senzor PIR cu cablu, 12 m", 30)]),
    ("Detecție efracție", "Contacte magnetice", [("CONTACT-MAG-UA", "Contact magnetic pentru ușă, aplicat", 60), ("CONTACT-MAG-ASC", "Contact magnetic ascuns, încastrat", 28)]),
    ("Detecție efracție", "Sirene", [("SIRENA-EXT-SOL", "Sirenă exterioară cu flash și acumulator", 11), ("SIRENA-INT-110", "Sirenă interioară 110 dB", 14)]),
    ("Detecție incendiu", "Detectoare de fum", [("DET-FUM-ADR", "Detector optic de fum adresabil", 48), ("DET-TEMP-ADR", "Detector termic adresabil", 20)]),
    ("Detecție incendiu", "Centrale de incendiu", [("CENTR-INC-2L", "Centrală de incendiu adresabilă, 2 bucle", 2)]),
    ("Detecție incendiu", "Module și butoane", [("BUTON-ALARMA-ADR", "Buton manual de alarmă adresabil", 16), ("MODUL-INTRARE-ADR", "Modul de intrare adresabil", 12)]),
    ("Control acces", "Cititoare", [("CIT-RFID-125", "Cititor RFID 125 kHz, IP65", 14), ("CIT-BIO-AMPR", "Cititor biometric cu amprentă", 4)]),
    ("Control acces", "Controllere și yale", [("CTRL-ACCES-2U", "Controller de acces pentru 2 uși", 6), ("YALA-ELECTROM-300", "Yală electromagnetică 300 kg", 17), ("BUTON-IESIRE", "Buton de ieșire, inox", 33)]),
    ("Rețea și cabluri", "Switch-uri", [("SW-POE-8P", "Switch PoE 8 porturi + 2 uplink", 10), ("SW-POE-24P", "Switch PoE 24 porturi, administrabil", 3)]),
    ("Rețea și cabluri", "Cabluri și conectică", [("UTP-CAT6-ROLA", "Cablu UTP Cat.6, rolă 305 m", 8), ("CONECTOR-RJ45", "Conector RJ45 Cat.6, pachet 100 buc.", 15), ("CABLU-ALARMA-4X022", "Cablu de alarmă 4 x 0,22 mm², rolă 100 m", 12)]),
    ("Alimentare", "Surse și acumulatori", [("SURSA-12V-5A", "Sursă stabilizată 12 V / 5 A", 19), ("ACUM-12V-7AH", "Acumulator 12 V / 7 Ah", 26), ("ACUM-12V-18AH", "Acumulator 12 V / 18 Ah", 9)])
};
foreach (var category in catalog.Select(item => item.Category).Distinct()) await products.CreateCategoryAsync(category);
foreach (var group in catalog) await products.CreateSubcategoryAsync(group.Category, group.Subcategory);

var created = new List<(Product Product, int Stock)>();
foreach (var group in catalog)
    foreach (var (code, description, stock) in group.Items)
        created.Add((await products.CreateAsync(new ProductInput { Name = code, Description = description, Category = group.Category, Subcategory = group.Subcategory }), stock));
Product Find(string code) => created.First(item => item.Product.Name == code).Product;
Console.WriteLine($"Catalog: {created.Count} produse.");

// ---- Suppliers and their invoices (the entries of the stock) ---------------------------------------------------------------------------------------------------
var supplierData = new (string Name, string Cui, string Address, string Phone, string Registry)[]
{
    ("Alfa Securitate Distribuție SRL", "RO3154870", "Str. Industriilor 14, București", "0212345601", "J40/2145/2011"),
    ("Delta Cabluri și Rețele SRL", "RO2716493", "Bd. Muncii 88, Cluj-Napoca", "0264555102", "J12/880/2013"),
    ("Omega Video Systems SRL", "RO3489021", "Str. Fabricii 3, Brașov", "0268444303", "J08/1920/2015"),
    ("Vega Power Supply SRL", "RO2930748", "Calea Vitan 210, București", "0214445504", "J40/6021/2012"),
    ("Nordic Electronic Trade SRL", "RO3672509", "Str. Portului 5, Constanța", "0241333705", "J13/1004/2016")
};
var supplierRecords = new List<Supplier>();
foreach (var data in supplierData)
    supplierRecords.Add(await suppliers.CreateAsync(new SupplierInput { Name = data.Name, Cui = WithControl(data.Cui), Address = data.Address, Phone = data.Phone, RegistryNumber = data.Registry, Country = "RO" }));

// Each supplier delivers some products, on one invoice: the entries carry the invoice.
var deliveries = new (int Supplier, string Number, int DaysAgo, string[] Codes)[]
{
    (2, "OVS 2026/0412", 58, ["CAM-IP-4MP-DOM", "CAM-IP-4MP-BUL", "CAM-IP-8MP-PTZ", "CAM-IP-2MP-MINI", "NVR-16CH-4K", "NVR-8CH-POE", "HDD-4TB-SURV", "SUPORT-PERETE-ALB", "CUTIE-JONCTIUNE"]),
    (0, "ASD 6619", 44, ["CENTR-ALARMA-32Z", "CENTR-ALARMA-8Z", "PIR-DUAL-WL", "PIR-CURTAIN-EXT", "PIR-AM-FIR", "CONTACT-MAG-UA", "CONTACT-MAG-ASC", "SIRENA-EXT-SOL", "SIRENA-INT-110", "DET-FUM-ADR", "DET-TEMP-ADR", "CENTR-INC-2L", "BUTON-ALARMA-ADR", "MODUL-INTRARE-ADR"]),
    (1, "DCR-0087", 31, ["SW-POE-8P", "SW-POE-24P", "UTP-CAT6-ROLA", "CONECTOR-RJ45", "CABLU-ALARMA-4X022"]),
    (3, "VPS 14502", 22, ["SURSA-12V-5A", "ACUM-12V-7AH", "ACUM-12V-18AH"]),
    (4, "NET/2026/311", 12, ["CIT-RFID-125", "CIT-BIO-AMPR", "CTRL-ACCES-2U", "YALA-ELECTROM-300", "BUTON-IESIRE"])
};
foreach (var delivery in deliveries)
{
    var supplier = supplierRecords[delivery.Supplier];
    var invoice = await invoices.CreateAsync(new SupplierInvoiceInput { SupplierId = supplier.Id, Number = delivery.Number, Date = Ago(delivery.DaysAgo) });
    foreach (var code in delivery.Codes)
    {
        var item = created.First(entry => entry.Product.Name == code);
        await movements.CreateAsync(item.Product.Id, new StockMovementInput
        {
            Kind = StockMovementKind.Entry, Date = Ago(delivery.DaysAgo), Quantity = item.Stock + 12, Description = $"Factura {delivery.Number}", InvoiceId = invoice.Id, InvoiceQuantity = item.Stock + 12
        });
    }
}
Console.WriteLine($"Furnizori: {supplierRecords.Count}, facturi: {deliveries.Length}.");

// ---- Beneficiaries, work points, vehicles, projects -------------------------------------------------------------------------------------------------------------
var beneficiaryData = new (string Name, string Cui, string Address, string Phone, string Kind)[]
{
    ("Panoramic Hotel & Spa SRL", "RO3011225", "Str. Muntelui 21, Brașov", "0268700111", BeneficiaryKinds.Legal),
    ("Clinica Medicală Sănătatea Plus SRL", "RO2877431", "Bd. Eroilor 7, Cluj-Napoca", "0264700222", BeneficiaryKinds.Legal),
    ("Logistic Park Ploiești SRL", "RO3351907", "Șos. Depozitelor 45, Ploiești", "0244700333", BeneficiaryKinds.Legal),
    ("Asociația de Proprietari Bloc Tineretului 12", "RO1984523", "Str. Tineretului 12, București", "0214700444", BeneficiaryKinds.Legal),
    ("Ionescu Mihai", "", "Str. Florilor 9, Sibiu", "0745000555", BeneficiaryKinds.Individual)
};
var beneficiaryRecords = new List<Beneficiary>();
foreach (var data in beneficiaryData)
    beneficiaryRecords.Add(await beneficiaries.CreateAsync(new BeneficiaryInput { Kind = data.Kind, Name = data.Name, Cui = data.Cui.Length == 0 ? "" : WithControl(data.Cui), Address = data.Address, Phone = data.Phone }));

var points = new (int Beneficiary, string Name, string Address, string Phone, string Contact, string Coordinates)[]
{
    (0, "Recepție și hol principal", "Corp A, Str. Muntelui 21, Brașov", "0268700112", "Elena Popa", "45.6427, 25.5887"),
    (0, "Parcare subterană", "Corp B, Str. Muntelui 21, Brașov", "0268700113", "Radu Marin", "45.6431, 25.5879"),
    (1, "Clinica Eroilor", "Parter, Bd. Eroilor 7, Cluj-Napoca", "0264700223", "Dr. Ana Pavel", "46.7712, 23.5897"),
    (1, "Laborator analize", "Str. Observatorului 4, Cluj-Napoca", "0264700224", "Mihai Rus", "46.7549, 23.5743"),
    (2, "Hala A", "Hala A, Șos. Depozitelor 45, Ploiești", "0244700334", "Vlad Stan", "44.9366, 26.0302"),
    (2, "Hala B", "Șos. Depozitelor 47, Ploiești", "0244700335", "Vlad Stan", "44.9371, 26.0315"),
    (3, "Scara A", "Scara A, Str. Tineretului 12, București", "0214700445", "Maria Dobre", "44.4056, 26.1124"),
    (4, "Locuință", "Locuință, Str. Florilor 9, Sibiu", "0745000555", "Ionescu Mihai", "45.7983, 24.1256")
};
var workPointRecords = new List<WorkPoint>();
foreach (var point in points)
    workPointRecords.Add(await workPoints.CreateAsync(beneficiaryRecords[point.Beneficiary].Id,
        new WorkPointInput { Name = point.Name, Address = point.Address, Phone = point.Phone, ContactPerson = point.Contact, UseCoordinates = true, CoordinatesText = point.Coordinates }));

var vehicleRecords = new List<Vehicle>();
foreach (var (plate, description, itp, insurance, rovinieta) in new (string, string, int, int, int)[]
{
    ("BV-45-ESP", "Dacia Dokker – echipa Brașov", 220, 140, 300),
    ("B-77-ESP", "Ford Transit Connect – echipa București", 25, 330, 18),
    ("CJ-08-ESP", "Renault Kangoo – echipa Cluj", 410, 90, 200),
    ("PH-12-ESP", "Dacia Duster – inspecții", 12, 250, 340)
})
    vehicleRecords.Add(await vehicles.CreateAsync(new VehicleInput { PlateNumber = plate, Description = description, ItpExpiry = today.AddDays(itp), InsuranceExpiry = today.AddDays(insurance), RovinietaExpiry = today.AddDays(rovinieta) }));

var typeRecords = new List<SystemType>();
foreach (var name in new[] { "Supraveghere video", "Detecție efracție", "Detecție incendiu", "Control acces" }) typeRecords.Add(await systemTypes.CreateAsync(name));

var projectData = new (int Beneficiary, string Name, string Note, int[] Types)[]
{
    (0, "Supraveghere video și control acces – hotel", "Montaj camere pe fațadă și în holuri, controllere la accesele de serviciu.", [0, 3]),
    (1, "Sistem de alarmă și incendiu – clinică", "Zonele de laborator cer detectoare termice; centrala se montează în holul tehnic.", [1, 2]),
    (2, "Supraveghere perimetrală – depozit", "Camere PTZ pe colțuri, NVR în camera de pază.", [0, 1]),
    (3, "Control acces scară – bloc", "Cititoare RFID la intrări și butoane de ieșire.", [3])
};
var projectRecords = new List<Project>();
foreach (var data in projectData)
{
    var project = await projects.CreateAsync(new ProjectInput { BeneficiaryId = beneficiaryRecords[data.Beneficiary].Id, Name = data.Name, Observations = data.Note });
    await components.AddAsync(project.Id, [.. data.Types.Select(index => typeRecords[index].Id)]);
    projectRecords.Add(project);
}
await projects.CreateObservationAsync(projectRecords[0].Id, new ProjectObservationInput { Name = "Acces la fațadă", Content = "Pentru camerele de pe fațadă este nevoie de nacelă; se programează cu administratorul hotelului." }, "claude.test");
await projects.CreateObservationAsync(projectRecords[1].Id, new ProjectObservationInput { Name = "Program de lucru", Content = "Intervenții doar după ora 18:00, în zonele cu pacienți." }, "claude.test");

// ---- Exits: to the projects, to the vehicles, one sale ----------------------------------------------------------------------------------------------------------
async Task ExitAsync(string code, int quantity, int daysAgo, ExitDestination destination, int? beneficiary = null, int? project = null, int? vehicle = null, string description = "")
{
    await movements.CreateAsync(Find(code).Id, new StockMovementInput
    {
        Kind = StockMovementKind.Exit, Date = Ago(daysAgo), Quantity = quantity, Destination = destination, Description = description.Length > 0 ? description : destination == ExitDestination.Vehicle ? "Încărcare în vehicul pentru lucrări" : "Montaj la beneficiar",
        BeneficiaryId = beneficiary is null ? null : beneficiaryRecords[beneficiary.Value].Id, ProjectId = project is null ? null : projectRecords[project.Value].Id,
        VehicleId = vehicle is null ? null : vehicleRecords[vehicle.Value].Id, OutsideOffer = project is not null
    });
}
await ExitAsync("CAM-IP-4MP-BUL", 8, 20, ExitDestination.Beneficiary, 0, 0, description: "Fațadă hotel");
await ExitAsync("CAM-IP-4MP-DOM", 6, 20, ExitDestination.Beneficiary, 0, 0, description: "Holuri etaj 1-3");
await ExitAsync("NVR-16CH-4K", 1, 20, ExitDestination.Beneficiary, 0, 0);
await ExitAsync("CTRL-ACCES-2U", 2, 15, ExitDestination.Beneficiary, 0, 0);
await ExitAsync("CIT-RFID-125", 4, 15, ExitDestination.Beneficiary, 0, 0);
await ExitAsync("CENTR-INC-2L", 1, 10, ExitDestination.Beneficiary, 1, 1);
await ExitAsync("DET-FUM-ADR", 18, 10, ExitDestination.Beneficiary, 1, 1);
await ExitAsync("DET-TEMP-ADR", 6, 10, ExitDestination.Beneficiary, 1, 1);
await ExitAsync("CAM-IP-8MP-PTZ", 3, 7, ExitDestination.Beneficiary, 2, 2);
await ExitAsync("NVR-8CH-POE", 2, 7, ExitDestination.Beneficiary, 2, 2);
await ExitAsync("CIT-RFID-125", 6, 4, ExitDestination.Beneficiary, 3, 3);
await ExitAsync("BUTON-IESIRE", 6, 4, ExitDestination.Beneficiary, 3, 3);
await ExitAsync("SW-POE-8P", 3, 3, ExitDestination.Vehicle, vehicle: 0);
await ExitAsync("UTP-CAT6-ROLA", 2, 3, ExitDestination.Vehicle, vehicle: 0);
await ExitAsync("CAM-IP-2MP-MINI", 4, 3, ExitDestination.Vehicle, vehicle: 1);
await ExitAsync("CONECTOR-RJ45", 5, 3, ExitDestination.Vehicle, vehicle: 1);
await ExitAsync("ACUM-12V-7AH", 4, 2, ExitDestination.Vehicle, vehicle: 2);
await ExitAsync("SIRENA-INT-110", 2, 1, ExitDestination.GenericSale, description: "Vânzare către client persoană fizică");
await minStock.SetAsync(Find("CENTR-INC-2L").Id, 3);
await minStock.SetAsync(Find("CAM-IP-8MP-PTZ").Id, 5);

// ---- Maintenance: contracts and interventions -----------------------------------------------------------------------------------------------------------------------
async Task ContractAsync(int beneficiary, string number, int cycle, int[] pointIndexes, int firstDueInDays)
{
    var input = new ServiceContractInput { NumberText = number, CycleMonths = cycle, ValidUntil = today.AddYears(1), Notes = "Verificări periodice ale sistemelor instalate." };
    foreach (var index in pointIndexes) input.Points.Add(new ServiceContractPointInput { WorkPointId = workPointRecords[index].Id, NextDue = today.AddDays(firstDueInDays + index * 9) });
    await contracts.CreateAsync(beneficiaryRecords[beneficiary].Id, input);
}
await ContractAsync(0, $"14/{Ago(200):dd.MM.yyyy}", 6, [0, 1], 25);
await ContractAsync(1, $"21/{Ago(150):dd.MM.yyyy}", 3, [2, 3], -6);
await ContractAsync(2, $"33/{Ago(90):dd.MM.yyyy}", 12, [4, 5], 40);
await interventions.RecordAsync(beneficiaryRecords[0].Id, new ServiceInterventionInput { Kind = ServiceInterventionKind.Maintenance, WorkPointId = workPointRecords[0].Id, PerformedOn = Ago(35), Notes = "Verificare camere și curățare lentile; hard disk înlocuit." });
await interventions.RecordAsync(beneficiaryRecords[1].Id, new ServiceInterventionInput { Kind = ServiceInterventionKind.Maintenance, WorkPointId = workPointRecords[2].Id, PerformedOn = Ago(80), Notes = "Test detectoare de fum și centrală; fără defecte." });
await interventions.RecordAsync(beneficiaryRecords[4].Id, new ServiceInterventionInput { Kind = ServiceInterventionKind.OnDemand, WorkPointId = workPointRecords[7].Id, PerformedOn = Ago(5), Notes = "Intervenție la cerere: sirena exterioară nu mai pornea." });

// A contract that was switched off (the list shows it only with "Arată și contractele Off") and the reactivation dialog that goes with it.
var offContract = await contracts.CreateAsync(beneficiaryRecords[3].Id, new ServiceContractInput
{
    NumberText = $"7/{Ago(300):dd.MM.yyyy}", CycleMonths = 6, ValidUntil = today.AddMonths(8), Notes = "Contract suspendat la cererea asociației.",
    Points = [new ServiceContractPointInput { WorkPointId = workPointRecords[6].Id, NextDue = today.AddDays(60) }]
});
await contracts.DeactivateAsync(offContract.Contract);

// Risk analyses: one valid, one that expires soon (so the list and the renewal window have something to show).
await riskAnalyses.CreateAsync(beneficiaryRecords[0].Id, new RiskAnalysisInput { WorkPointId = workPointRecords[0].Id, Number = "RA-0142", Author = "Ing. Cătălin Moraru", InitialDate = Ago(120), ValidityMonths = 24, Notes = "Analiză de risc la securitate fizică." }, false);
await riskAnalyses.CreateAsync(beneficiaryRecords[1].Id, new RiskAnalysisInput { WorkPointId = workPointRecords[2].Id, Number = "RA-0097", Author = "Ing. Cătălin Moraru", InitialDate = Ago(700), ValidityMonths = 24, Notes = "De reînnoit în curând." }, false);

// Entries without an invoice yet (the supplier will send it): they wait in "Intrări fără factură".
foreach (var (code, quantity, daysAgo, supplier) in new[] { ("PIR-DUAL-WL", 10, 6, 0), ("SIRENA-EXT-SOL", 4, 5, 0), ("ACUM-12V-18AH", 6, 2, 3) })
    await movements.CreateAsync(Find(code).Id, new StockMovementInput
    {
        Kind = StockMovementKind.Entry, Date = Ago(daysAgo), Quantity = quantity, Description = "Livrare în așteptarea facturii",
        FreeType = FreeEntryType.AwaitedInvoice, FreeSupplierId = supplierRecords[supplier].Id
    });

// One movement corrected afterwards: it shows the mark "modificat" and has a history.
var corrected = await movements.CreateAsync(Find("BUTON-ALARMA-ADR").Id, new StockMovementInput { Kind = StockMovementKind.Entry, Date = Ago(9), Quantity = 5, Description = "Intrare fără factură", FreeType = FreeEntryType.Adjustment, Reference = "Inventar" });
var edit = StockMovementInput.From(corrected.Movement);
edit.Quantity = 6; edit.Reason = "Cantitate corectată după numărătoare";
await movements.UpdateAsync(corrected.Movement, edit);

Console.WriteLine("Gata: datele demonstrative sunt in blazorstoc_demo.");
return 0;

// The CUI body gets its control digit (key 753217532), so the fictive numbers pass the validation of the application.
static string WithControl(string cuiBody)
{
    var prefix = cuiBody.StartsWith("RO") ? "RO" : "";
    var digits = cuiBody[prefix.Length..];
    int[] key = [7, 5, 3, 2, 1, 7, 5, 3, 2];
    var sum = 0;
    for (var i = 0; i < digits.Length; i++) sum += (digits[i] - '0') * key[key.Length - digits.Length + i];
    var control = sum * 10 % 11 % 10;
    return prefix + digits + control;
}

sealed class DemoAdministrator(string username) : IAccessControl
{
    public Task<bool> IsAdministratorAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task<bool> CanManageProductsAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task<bool> CanManageBeneficiariesAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task<string?> GetUsernameAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(username);
    public Task EnsureAdministratorAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task EnsureProductOperatorAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task EnsureBeneficiaryOperatorAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

sealed class DemoEnvironment(string root) : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "BlazorStoc.HelpDemo";
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    public string WebRootPath { get; set; } = root;
    public string EnvironmentName { get; set; } = "Demo";
    public string ContentRootPath { get; set; } = root;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
