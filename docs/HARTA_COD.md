# Harta codului (pentru cautari tintite)

Scop: sa gasesti fisierele unui modul fara cautari exploratorii. Directoare ignorate la cautare: `bin`, `obj`, `artifacts`, `bin_verify*`, `data`, `local-secrets`, `keys*`, `database/dev-data`, `wwwroot/lib`, `Assets`. Se completeaza cand apare un modul nou.

| Modul | Pagini si componente (`Components/`) | Servicii (`Services/`) | Teste (`tests/BlazorStoc.Checks/`) |
|---|---|---|---|
| Produse, stoc, miscari | `Pages/Inventory`, `ProductEditor`, `ProductMovements`, `ProductGroups*` | `Products`, `ProductInput`, `StockMovements`, `MariaStockMovementRepository`, `MariaProductRepository.Crud`, `ProductLocks`, `OperationLock` | `Program.cs`, `ProductGroupsChecks`, `MariaExtendedChecks` |
| Inventar (generare, preluare, restaurare) | `Pages/Inventory*`, `InventoryPickup`, `BackupSettingsPanel` (sectiunile backup si ora), `BackupPackagesTable`, `BackupNasSettings`, `Pages/Settings` (tab Backup cu sub-taburi) | `Inventory*`, `InventoryPickupOcr`, `DatabaseBackup`, `DatabaseRestore`, `BackupSettings`, `BackupCatalog`, `TrustedClock`, `BackupAlerts`, `NasBackup` | `Program.cs`, `GridChecks` |
| Beneficiari, proiecte | `Pages/Beneficiar*`, `Project*`, `Shared/AnafConfigurator`, `Shared/AnafDifferencesDialog` | `Beneficiaries`, `Projects`, `Maria*Beneficiary*`, `MariaProject*`, `AnafLookup`, `AnafApply` | `Program.cs`, `MariaExtendedChecks` |
| Furnizori si facturi de furnizor | `Pages/Suppliers`, `SupplierDetail`, `SupplierEditor`, `Invoices` | `Suppliers`, `SupplierInvoices`, `MariaSupplier*`, `SupplierRecognitionLog`, `CachedSupplierRepository` | `SupplierChecks`, `MariaExtendedChecks` (sectiunea „Suppliers and invoices”) |
| Preluare factura PDF/OCR, sabloane | `Pages/InvoicePickup`, `InvoiceViewer`, `Shared/InvoiceTemplateWorkbench`, `InvoiceTemplatesList`, `InvoiceXmlTemplateEditor`, `InvoiceXmlLinker`, `Pickup*TemplateBar`, `PickupProductCell`, `Pages/InvoicePickup.*.cs` | `Invoices/` (`InvoiceXml` = facturi XML/UBL, `InvoiceTableReader`, `InvoicePdfReader`, `InvoiceTemplates`, `InvoiceVocabulary`, `FileInvoiceTemplateStore`) | `InvoiceChecks`, `InvoiceXmlChecks`, `InvoiceFixtures`, `OcrLabChecks`, `PickupWizardChecks`; `tests/BlazorStoc.InvoiceCorpus` |
| Vehicule | `Pages/Vehicle*` | `Vehicles`, `MariaVehicleRepository` | `Program.cs`, `MariaExtendedChecks` |
| Mentenanta (contracte, interventii, harta) | `Pages/Maintenance*`, `Service*`, `WorkPointEditor` | `ServiceContracts`, `ServiceInterventions`, `WorkPoints`, `Maria*Service*`, `MaintenanceMap`, `MapConfiguration` | `MariaExtendedChecks` |
| Oferte, componente, rezervari | `Pages/Offer*`, `ProjectSituationPage`, `ExitOperation`, `Shared/ProjectComponentsSection`, `ExitComponentChoice`, `EntryComponentChoice`, `ReservationWarningChoice`, `ReserveSuggestions` | `Offers/` (`XlsxReader`, `OfferTemplates`, `MariaOfferRepository`), `ProjectComponents`, `MariaProjectComponentRepository`, `ProjectSituation`, `MariaProjectSituationReader`, `Reservations`, `MariaReservationRepository` | `OfferChecks`, `MariaExtendedChecks` |
| Notificari | `Pages/Notifications` | `ExpiryNotifications`, `NotificationSettings`, `MaintenanceNotificationSources`, `MariaExpiryNotificationRepository` | `Program.cs`, `MariaExtendedChecks` |
| Jurnal (audit) si arhivare | `Pages/Audit*`, `Shared/Audit*` | `AuditTrail` (actiuni, entitati, `IsCreateOrEdit`), `AuditFilters`, `MariaAuditTrail`, `Archiving`, `ArchivePersistence` | `Program.cs`, `MariaExtendedChecks` |
| Utilizatori, acces | `Pages/Users`, `UserEditor`, `UserDetail` | `AccessControl`, `WebUsers`, `MariaUserRepository` | `Program.cs`, `MariaExtendedChecks` |
| Meniu, pagina principala, comune | `Layout/MainLayout.razor`, `Pages/Dashboard.razor`, `Shared/*` (`ToggleSwitch`, `SearchableSelect`, `PickOnlyDate`, `ChangeReasonField`, `DeleteConfirmationDialog`, `MarkedTextField`) | `ChangeReasonRules`, `ChangeReasonSummary`, `TextNormalization`, `MariaSchemaMigrations` (schema) | `ComponentChecks`, `ReasonSummaryChecks` |

## Comenzi uzuale (din `BlazorStoc/`)

- Teste cu rezumat: `tools\run-checks.ps1` (`-Group suppliers|pickup|groups|reasons|components|invoices|ui` pentru o zona, `-Mode maria -Section '<sectiune MariaDB>'`, `-Show '<text>'`); logul complet ramane in `artifacts\check-logs`.
- Preview: `tools\start-preview.ps1` (5087, MariaDB 3307); doar la „review”/„preview”.
- Documentarea unui task: `tools\log-task.ps1 -Title ... -Details ... [-Remaining ...]` (append in `IMPLEMENTED.md` si `docs/TESTE_RAMASE.md`).
- Istoric: `docs/arhiva/INDEX.md`.
