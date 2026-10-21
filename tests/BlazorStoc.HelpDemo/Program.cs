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

if (args.Contains("--forms")) { await WriteInventoryFormsAsync(true); Console.WriteLine("Formulare de inventar scrise."); return 0; }
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
var invoiceIds = new List<long>();
foreach (var delivery in deliveries)
{
    var supplier = supplierRecords[delivery.Supplier];
    var invoice = await invoices.CreateAsync(new SupplierInvoiceInput { SupplierId = supplier.Id, Number = delivery.Number, Date = Ago(delivery.DaysAgo) });
    invoiceIds.Add(invoice.Id);
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
    ("PH-12-ESP", "Dacia Duster – inspecții", 12, 250, 340),
    ("IF-33-ESP", "Fiat Doblò – service", -10, 200, 300)
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
    NumberText = $"7/{Ago(300):dd.MM.yyyy}", CycleMonths = 6, ValidUntil = today.AddMonths(-2), Notes = "Contract suspendat la cererea asociației.",
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

// ---- Scenarios for the pictures of the help (tools/help-capture.mjs): every element of a window needs a state in which it is on the screen -----------------------

// A subcategory with required parameters (the product form then asks for a model and the values; the window of the parameters shows names, types, units and values).
var parameterRepository = new MariaProductParameterRepository(configuration, admin, audit);
var specialCameras = new ProductGroup("Supraveghere video", "Camere speciale");
await products.CreateSubcategoryAsync(specialCameras.Category, specialCameras.Subcategory);
var lensParameter = await parameterRepository.AddParameterAsync(specialCameras, "Lentilă", "mm", ParameterKind.Number);
foreach (var value in new[] { "2,8", "3,6", "6" }) await parameterRepository.AddValueAsync(lensParameter.Id, value);
var colourParameter = await parameterRepository.AddParameterAsync(specialCameras, "Culoare", "", ParameterKind.Text);
foreach (var value in new[] { "alb", "negru" }) await parameterRepository.AddValueAsync(colourParameter.Id, value);

// Risk analyses of the first beneficiary: an old one that was switched off, and a renewal in the history (so the list shows "Arată și analizele Off", the points without an active analysis, the undo of a renewal).
var offAnalysis = await riskAnalyses.CreateAsync(beneficiaryRecords[0].Id, new RiskAnalysisInput { WorkPointId = workPointRecords[1].Id, Number = "RA-0118", Author = "Ing. Cătălin Moraru", InitialDate = Ago(400), ValidityMonths = 24, Notes = "Analiză veche, înlocuită." }, false);
await riskAnalyses.DeactivateAsync(offAnalysis.Analysis);
var hotelAnalysis = (await riskAnalyses.GetForBeneficiaryAsync(beneficiaryRecords[0].Id)).First(view => view.Analysis.Number == "RA-0142");
await riskAnalyses.RenewAsync(hotelAnalysis.Analysis, new RiskAnalysisRenewalInput { Date = Ago(10), Notes = "Reînnoire periodică." });

// Exits over the stock (the products wait in "De regularizat"), with the cause the user chose.
async Task OverStockExitAsync(string code, int quantity, OverStockCause cause)
{
    await movements.CreateAsync(Find(code).Id, new StockMovementInput
    {
        Kind = StockMovementKind.Exit, Date = Ago(2), Quantity = quantity, Destination = ExitDestination.GenericSale, Description = "Vânzare peste stocul din depozit", OverStockCause = cause
    });
}
await OverStockExitAsync("SIRENA-EXT-SOL", 40, OverStockCause.UnrecordedEntry);
await OverStockExitAsync("CONTACT-MAG-ASC", 55, OverStockCause.WrongStock);

// Offers taken over into the projects: the components get lines tied to products (the situation of the project, the component choice at an entry), and reservations of other projects.
var offers = new MariaOfferRepository(configuration, projects, components, beneficiaries, admin, audit);
var reservations = new MariaReservationRepository(configuration, admin, audit);
OfferImportRequest OfferRequest(string number, string title, int project, int typeIndex, List<OfferImportLine> lines) => new()
{
    Number = number, Title = title, Category = "Securitate", FileName = $"oferta-{number}.xlsx", BeneficiaryId = projectRecords[project].BeneficiaryId, BeneficiaryText = beneficiaryRecords.First(item => item.Id == projectRecords[project].BeneficiaryId).Name,
    RememberBeneficiaryAlias = false, ProjectId = projectRecords[project].Id, NewProjectName = "", SystemTypeId = typeRecords[typeIndex].Id, Lines = lines
};
OfferImportLine OfferLine(string section, int number, string name, string unit, int quantity, string? productCode, string productType = "") => new()
{
    Section = section, Number = number.ToString(System.Globalization.CultureInfo.InvariantCulture), ProductType = productType, Name = name, Unit = unit, Quantity = quantity, InStock = productCode is not null, ProductId = productCode is null ? null : Find(productCode).Id
};
await offers.ImportAsync(OfferRequest("250700101", "Supraveghere video hotel", 0, 0,
[
    OfferLine("Echipamente", 1, "Cameră IP dome 4 MP", "buc", 8, "CAM-IP-4MP-DOM", "Camere"), OfferLine("Echipamente", 2, "Cameră IP bullet 4 MP", "buc", 4, "CAM-IP-4MP-BUL", "Camere"),
    OfferLine("Echipamente", 3, "NVR 16 canale 4K", "buc", 1, "NVR-16CH-4K", "NVR"), OfferLine("Echipamente", 4, "Cablu FTP cat 5", "m", 500, null, "Cabluri"),
    OfferLine("Manoperă", 1, "Montaj și configurare camere", "oră", 24, null, "Servicii")
]));
await offers.ImportAsync(OfferRequest("250700102", "Control acces hotel", 0, 3,
[
    OfferLine("Echipamente", 1, "Controller acces 2 uși", "buc", 3, "CTRL-ACCES-2U"), OfferLine("Echipamente", 2, "Cititor RFID 125 kHz", "buc", 6, "CIT-RFID-125"), OfferLine("Echipamente", 3, "Yală electromagnetică 300 kg", "buc", 4, "YALA-ELECTROM-300")
]));
await offers.ImportAsync(OfferRequest("250700103", "Alarmă și incendiu clinică", 1, 2,
[
    OfferLine("Echipamente", 1, "Detector optic de fum adresabil", "buc", 70, "DET-FUM-ADR"), OfferLine("Echipamente", 2, "Centrală de incendiu 2 bucle", "buc", 1, "CENTR-INC-2L"), OfferLine("Echipamente", 3, "Buton manual de alarmă", "buc", 8, "BUTON-ALARMA-ADR")
]));
var clinicComponents = await components.GetForProjectAsync(projectRecords[1].Id);
await reservations.ReserveAsync(projectRecords[1].Id, clinicComponents.First(item => item.SystemTypeId == typeRecords[2].Id).Id, Find("BUTON-ALARMA-ADR").Id, 6);
var depotComponents = await components.GetForProjectAsync(projectRecords[2].Id);
await reservations.ReserveAsync(projectRecords[2].Id, depotComponents.First().Id, Find("CAM-IP-4MP-DOM").Id, 20);

// A component of a project that was archived (the list shows "Arată și arhivate" and the reactivation).
await components.ArchiveAsync(depotComponents.Last(), "Componentă nefolosită în această etapă.");

// Supplier recognition: what the reading proposed for the invoices and what the user chose.
var recognitionLog = new MariaSupplierRecognitionLog(configuration, admin);
var recognitionRows = new (int Invoice, SupplierMatchMethod Method, string Confidence, string ReadName, string ReadCui, int? Recognized, int? Chosen, bool TemplateChanged)[]
{
    (0, SupplierMatchMethod.Cui, "high", "Omega Video Systems", "RO3489021", 2, 2, false), (1, SupplierMatchMethod.Cui, "high", "Alfa Securitate Distributie", "RO3154870", 0, 0, false),
    (2, SupplierMatchMethod.Name, "medium", "Delta Cabluri si Retele", "R02716493", 1, 3, true), (3, SupplierMatchMethod.Similar, "low", "Vega Power Suply", "RO293O748", 4, 3, false),
    (4, SupplierMatchMethod.Alias, "high", "Nordic Electronic", "RO3672509", 4, 4, false)
};
foreach (var row in recognitionRows)
    await recognitionLog.RecordAsync(new SupplierRecognitionEntry(invoiceIds[row.Invoice], row.Method, row.Confidence, row.ReadName, row.ReadCui, row.Recognized is null ? null : supplierRecords[row.Recognized.Value].Id,
        row.Chosen is null ? null : supplierRecords[row.Chosen.Value].Id, row.TemplateChanged));

// Maintenance: the expired contract that was switched off has its point in a new active contract (the reactivation window warns about both); two points at the same place on the map;
// a point without coordinates under a contract; a longer register of interventions (the paging).
var gardenPoint = await workPoints.CreateAsync(beneficiaryRecords[4].Id, new WorkPointInput { Name = "Garaj", Address = "Garaj, Str. Florilor 9, Sibiu", Phone = "0745000556", ContactPerson = "Ionescu Mihai", UseCoordinates = false });
workPointRecords.Add(gardenPoint);
var annexPoint = await workPoints.CreateAsync(beneficiaryRecords[2].Id, new WorkPointInput { Name = "Hala A – birouri", Address = "Birouri, Șos. Depozitelor 45, Ploiești", Phone = "0244700336", ContactPerson = "Vlad Stan", UseCoordinates = true, CoordinatesText = "44.9366, 26.0302" });
workPointRecords.Add(annexPoint);
await ContractAsync(3, $"9/{Ago(30):dd.MM.yyyy}", 12, [6], 30);
await ContractAsync(4, $"41/{Ago(60):dd.MM.yyyy}", 12, [workPointRecords.IndexOf(gardenPoint)], 55);
await ContractAsync(2, $"34/{Ago(60):dd.MM.yyyy}", 12, [workPointRecords.IndexOf(annexPoint)], 70);
for (var i = 1; i <= 30; i++)
{
    var onDemandPoint = new[] { 2, 3, 0, 1 }[i % 4];
    await interventions.RecordAsync(beneficiaryRecords[onDemandPoint <= 1 ? 0 : 1].Id, new ServiceInterventionInput { Kind = ServiceInterventionKind.OnDemand, WorkPointId = workPointRecords[onDemandPoint].Id, PerformedOn = Ago(i * 3), Notes = $"Intervenție la cerere nr. {i}: verificare și reglaj." });
}

// A product with a longer history (the paging of the movements of a product).
for (var i = 1; i <= 12; i++)
{
    if (i % 3 == 0)
        await movements.CreateAsync(Find("CONECTOR-RJ45").Id, new StockMovementInput { Kind = StockMovementKind.Exit, Date = Ago(60 - i * 4), Quantity = 2, Description = $"Mișcare {i} de test pentru istoric", Destination = ExitDestination.GenericSale, Reference = $"REF-{i:00}" });
    else
        await movements.CreateAsync(Find("CONECTOR-RJ45").Id, new StockMovementInput { Kind = StockMovementKind.Entry, Date = Ago(60 - i * 4), Quantity = 5, Description = $"Mișcare {i} de test pentru istoric", FreeType = FreeEntryType.Adjustment, Reference = $"REF-{i:00}" });
}

// More projects of the third beneficiary (the list of projects is paged by 10).
for (var i = 1; i <= 10; i++)
    await projects.CreateAsync(new ProjectInput { BeneficiaryId = beneficiaryRecords[2].Id, Name = $"Extindere sistem – etapa {i}", Observations = $"Lucrări de extindere, etapa {i}." });

// Files: a sketch attached to an observation and two photos of a work point.
var observationFiles = new MariaProjectFileStore(environment, configuration, admin);
var firstObservation = (await projects.GetObservationsAsync(projectRecords[0].Id)).First();
await observationFiles.SaveAsync(firstObservation.Id, "schita-fatada.png", "image/png", DemoImage.Png(640, 360, 70, 120, 160), "claude.test");
var photos = new MariaServicePhotoStore(configuration, admin, audit);
await photos.AddToWorkPointAsync(workPointRecords[1].Id, "intrare-parcare.png", DemoImage.Png(640, 400, 120, 140, 90), "Intrarea în parcare");
await photos.AddToWorkPointAsync(workPointRecords[1].Id, "camera-rampa.png", DemoImage.Png(640, 400, 160, 110, 80), "Camera de la rampă");

// An own user type (the delete icon is shown only for those).
var userTypes = new MariaUserTypeRepository(configuration, admin, audit);
await userTypes.CreateAsync(new UserTypeInput { Name = "Magazioner", Description = "Operează intrările și ieșirile de stoc.", Keys = [.. Permissions.GrantableKeys.Where(key => key.EndsWith(".view", StringComparison.Ordinal))] });

// The inventory forms the help uploads in "Preluare inventar": the printed form (the writer of the application) with the counted values written in the last column.
//  inventar-mic.pdf          one product counted over (taken over once by tools/help-capture.mjs --prepare: it leaves an inventory package and an entry);
//  inventar-completat.pdf    that one too, one product counted short and a code that is not in the catalog any more (the table of differences and of the codes not found);
//  inventar-fara-diferente.pdf  only that first correction (after --prepare nothing differs from the stock).
async Task WriteInventoryFormsAsync(bool afterPrepare)
{
    var allProducts = await products.GetProductsAsync();
    // The inventory counts what is in the warehouse: the pieces loaded into the vehicles (4 of ACUM-12V-7AH) are not counted.
    static int Depot(Product product) => product.Quantity - (product.Name == "ACUM-12V-7AH" ? 4 : 0);
    var groups = new[] { ("Alimentare", "Surse si acumulatori"), ("Control acces", "Cititoare"), ("Control acces", "Controllere si yale") };
    void WriteForm(string fileName, int citDelta, int acumDelta, bool unknownCode)
    {
        var sections = new List<InventoryCategorySection>();
        var counted = new Dictionary<string, int>();
        foreach (var categoryName in groups.Select(group => group.Item1).Distinct())
        {
            var subSections = new List<InventorySubcategorySection>();
            foreach (var group in groups.Where(group => group.Item1 == categoryName))
            {
                var inGroup = allProducts.Where(product => string.Equals(product.Category, group.Item1, StringComparison.OrdinalIgnoreCase) && string.Equals(product.Subcategory, group.Item2, StringComparison.OrdinalIgnoreCase)).OrderBy(product => product.Name, StringComparer.Ordinal).ToList();
                var lines = inGroup.Select(product => new InventoryLine(product.Name, Depot(product))).ToList();
                foreach (var product in inGroup) counted[product.Name] = Depot(product);
                if (unknownCode && group.Item2 == "Cititoare") { lines.Add(new InventoryLine("CIT-RFID-VECHI", 3)); counted["CIT-RFID-VECHI"] = 3; }
                subSections.Add(new InventorySubcategorySection(group.Item2, lines));
            }
            sections.Add(new InventoryCategorySection(categoryName, subSections));
        }
        counted["CIT-BIO-AMPR"] += citDelta;
        counted["ACUM-12V-7AH"] += acumDelta;
        var report = new InventoryReport(DateTime.UtcNow, sections, sections.Count, groups.Length, sections.Sum(section => section.Subcategories.Sum(sub => sub.Lines.Count)), 0);
        var formBytes = new InventoryPdfWriter().Write(report, DateTime.Now);
        using var formInput = new MemoryStream(formBytes);
        using var formDocument = PdfSharp.Pdf.IO.PdfReader.Open(formInput, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Modify);
        var formPage = formDocument.Pages[0];
        using (var graphics = PdfSharp.Drawing.XGraphics.FromPdfPage(formPage, PdfSharp.Drawing.XGraphicsPdfPageOptions.Append))
        {
            PdfFonts.EnsureResolver();
            var handFont = new PdfSharp.Drawing.XFont(PdfFonts.FamilyName, 22, PdfSharp.Drawing.XFontStyleEx.Regular);
            var columns = InventoryPdfLayout.ComputeColumns(formPage.Width.Point);
            double y = 40 + 2 * 16 + 10;
            foreach (var section in sections)
            {
                y += 12 + 20;
                foreach (var sub in section.Subcategories)
                {
                    y += 20 + 16;
                    foreach (var line in sub.Lines)
                    {
                        graphics.DrawString(counted[line.Code].ToString(System.Globalization.CultureInfo.InvariantCulture), handFont, PdfSharp.Drawing.XBrushes.Black, new PdfSharp.Drawing.XRect(columns.RealX + 14, y + 4, columns.RealColumnWidth - 14, 26), PdfSharp.Drawing.XStringFormats.TopLeft);
                        y += 34;
                    }
                    y += 8;
                }
            }
        }
        Directory.CreateDirectory(Path.Combine(repo, "artifacts", "help-demo-files"));
        formDocument.Save(Path.Combine(repo, "artifacts", "help-demo-files", fileName));
    }
    // before --prepare: the small form is the first correction; after it (the stock has taken it over) the form with differences has both, and the other has none
    if (!afterPrepare) { WriteForm("inventar-mic.pdf", 1, 0, false); WriteForm("inventar-completat.pdf", 1, -2, true); WriteForm("inventar-fara-diferente.pdf", 1, 0, false); }
    else { WriteForm("inventar-completat.pdf", 1, -2, true); WriteForm("inventar-fara-diferente.pdf", 0, 0, false); }
}
await WriteInventoryFormsAsync(false);
Console.WriteLine("Scenarii pentru ajutor: parametri, analize, oferte, rezervari, recunoasteri, mentenanta, fisiere, tip de utilizator.");

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

// A small plain image (fictive photo): a gradient of one colour, written as a PNG without any library.
static class DemoImage
{
    public static byte[] Png(int width, int height, int red, int green, int blue)
    {
        var raw = new byte[(width * 3 + 1) * height];
        for (var y = 0; y < height; y++)
        {
            var row = y * (width * 3 + 1);
            for (var x = 0; x < width; x++)
            {
                var shade = 0.55 + 0.45 * x / width;
                raw[row + 1 + x * 3] = (byte)(red * shade); raw[row + 2 + x * 3] = (byte)(green * shade); raw[row + 3 + x * 3] = (byte)(blue * (1.2 - 0.4 * y / height));
            }
        }
        using var compressed = new MemoryStream();
        using (var zlib = new System.IO.Compression.ZLibStream(compressed, System.IO.Compression.CompressionLevel.Fastest, true)) zlib.Write(raw);
        using var output = new MemoryStream();
        output.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        Chunk(output, "IHDR", [.. Int(width), .. Int(height), 8, 2, 0, 0, 0]);
        Chunk(output, "IDAT", compressed.ToArray());
        Chunk(output, "IEND", []);
        return output.ToArray();
    }

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
        }
        return crc ^ 0xFFFFFFFFu;
    }

    private static byte[] Int(int value) => [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];

    private static void Chunk(Stream output, string type, byte[] data)
    {
        output.Write(Int(data.Length));
        var body = System.Text.Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        output.Write(body);
        output.Write(Int((int)Crc32(body)));
    }
}
