using Microsoft.Extensions.Configuration;
using BlazorStoc.Services;

namespace BlazorStoc.Checks;

public static partial class MariaExtendedChecks
{
    // The registers (suppliers, vehicles, beneficiaries): everybody with some permission reads them, and adding, editing and deleting each ask for their own key.
    private static async Task ModulePermissionsAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit)
    {
        KeyAccessControl User(params string[] keys) => new("ext.module", keys);
        var suffix = Suffix();

        var suppliers = new MariaSupplierRepository(configuration, User("furnizori.view"), audit);
        await suppliers.GetSuppliersAsync();
        Check(true, "A user who only views suppliers reads the register");
        var supplierInput = new SupplierInput { Name = $"Ext Drepturi Furnizor {suffix} SRL", Cui = "RO" + Random.Shared.Next(70000000, 79999999), Country = "RO" };
        await Rejects<AccessDeniedException>(() => new MariaSupplierRepository(configuration, User("furnizori.view", "furnizori.edit", "furnizori.delete"), audit).CreateAsync(supplierInput),
            "Without \"Adaugare\" a supplier cannot be created");

        var vehicles = new MariaVehicleRepository(configuration, User("vehicule.edit"), audit);
        await vehicles.GetVehiclesAsync();
        var vehicleInput = new VehicleInput { PlateNumber = "TS-93-" + Letters(), Description = "Ext drepturi", ItpExpiry = Expiry, InsuranceExpiry = Expiry, RovinietaExpiry = Expiry };
        await Rejects<AccessDeniedException>(() => vehicles.CreateAsync(vehicleInput), "With only \"Editare\" a vehicle cannot be added");
        var created = await new MariaVehicleRepository(configuration, User("vehicule.add"), audit).CreateAsync(vehicleInput);
        await Rejects<AccessDeniedException>(() => new MariaVehicleRepository(configuration, User("vehicule.add", "vehicule.edit"), audit).DeleteAsync(created, "Ext stergere drepturi"),
            "Without \"Stergere\" a vehicle cannot be deleted");
        await new MariaVehicleRepository(configuration, User("vehicule.delete"), audit).DeleteAsync(created, "Ext curatare drepturi");
        Check(true, "With \"Stergere\" a vehicle is deleted");

        var beneficiaries = new MariaBeneficiaryRepository(configuration, User("beneficiari.delete"), audit);
        await Rejects<AccessDeniedException>(() => beneficiaries.CreateAsync(Legal($"Ext Drepturi Ben {suffix} SRL", "RO" + Random.Shared.Next(80000000, 89999999))),
            "With only \"Stergere\" a beneficiary cannot be created");
    }
}
