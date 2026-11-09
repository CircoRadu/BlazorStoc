# Raport: iconite si butoane in antetul popup-urilor

Data: 09.11.2026. Sursa: scanarea tuturor fisierelor `.razor` (script de inventar; rezultatul brut este in `artifacts/popup-heading-scan.json`).

## Rezultat

- 78 de antete (`catalog-heading`, `dialog-head`, `map-panel-heading`), dintre care 46 apartin unor popup-uri (ferestre cu `role="dialog"` / `alertdialog` sau `<dialog>`).
- **3 popup-uri au in antet butoane intercalate cu iconitele** (ajutor, inchidere). Restul de 43 au doar iconita de ajutor si / sau butonul de inchidere.

| Popup | Fisier | Antet (in ordine) | Problema | Tratare |
|---|---|---|---|---|
| Vizualizare task (utilizator) | `Modules/Tasks/TaskDetailsDialog.razor:18` | ajutor, **Editeaza**, inchidere | butonul Editeaza intre iconita de ajutor si cea de inchidere | Editeaza ramane in antet; inchiderea trece in bara de deasupra |
| Previzualizare fisier | `Components/Shared/EntityFilePreview.razor:4` | **Descarca**, inchidere | butonul Descarca in fata iconitei de inchidere | Descarca ramane in antet; inchiderea trece in bara de deasupra |
| Detaliile punctului de pe harta | `Modules/Maintenance/MaintenanceMapView.razor:97` | ajutor, inchidere ("x" ca text) | butonul de inchidere era un link cu "x", nerecunoscut ca buton de inchidere | marcat cu `data-popup-close`: inchiderea trece in bara de deasupra, ca la celelalte |

## Regula aplicata (actualizata: elementul de inchidere in bara de deasupra popup-ului)

1. Fiecare popup are deasupra, in dreapta, o bara cu "Raporteaza o eroare" si cercul de inchidere (fond rosu, X alb centrat).
2. Butonul de inchidere din antet se ascunde; cercul din bara declanseaza exact aceeasi actiune (inclusiv avertizarea de date necompletate). Un buton de inchidere aflat in alta parte a popup-ului ramane, iar cercul se adauga oricum in bara.
3. Butoanele din antet (Editeaza, Descarca) raman pe loc; nu se mai muta sub titlu, deci cele trei popup-uri de mai sus nu mai au nimic intercalat.
4. Regula se aplica automat oricarui popup nou.

## De verificat in aplicatie

Cele trei popup-uri de mai sus, in tema luminoasa si intunecata, si pe latime de telefon (randul de sub titlu trebuie sa nu iasa din fereastra).

## Anexa: popup-uri fara butoane intercalate (43)

| Fisier:linie | Antet |
|---|---|
| `Components/Pages/BeneficiaryEditor.razor:9` | ajutor, inchidere |
| `Components/Pages/ProductGroups.razor:29` | ajutor |
| `Components/Pages/ProductGroups.razor:341` | ajutor |
| `Components/Pages/ProductMovements.razor:365` | ajutor |
| `Components/Pages/ProductMovements.razor:471` | ajutor |
| `Components/Pages/ProjectEditor.razor:9` | ajutor, inchidere |
| `Components/Pages/SupplierEditor.razor:9` | ajutor, inchidere |
| `Components/Pages/WorkPointEditor.razor:10` | ajutor, inchidere |
| `Components/Shared/AnafDifferencesDialog.razor:6` | ajutor |
| `Components/Shared/BeneficiaryFilesSection.razor:65` | ajutor |
| `Components/Shared/BranchMoveDialog.razor:12` | ajutor |
| `Components/Shared/BugReportDialog.razor:12` | ajutor |
| `Components/Shared/ChangePasswordDialog.razor:9` | ajutor |
| `Components/Shared/EntryInvoiceLinkDialog.razor:9` | ajutor |
| `Components/Shared/InvoiceManualEntryDialog.razor:8` | ajutor |
| `Components/Shared/OpenSessionsDialog.razor:6` | ajutor |
| `Components/Shared/ParameterArchiveDialog.razor:7` | ajutor |
| `Components/Shared/ParameterRestoreDialog.razor:8` | ajutor |
| `Components/Shared/ParameterUsageDialog.razor:6` | ajutor |
| `Components/Shared/SubcategoryParametersPanel.razor:11` | ajutor, inchidere |
| `Components/Shared/TakeFromChildDialog.razor:14` | ajutor |
| `Components/Shared/TicketSampleDialog.razor:7` | ajutor, inchidere |
| `Components/Shared/UpdateChangelogDialog.razor:8` | ajutor |
| `Modules/ActivityImport/ActivityDiscardedSection.razor:44` | inchidere |
| `Modules/ActivityImport/ActivityReviewSection.razor:52` | inchidere |
| `Modules/ActivityImport/ActivityReviewSection.razor:74` | inchidere |
| `Modules/Inventory/Inventory.razor:99` | ajutor |
| `Modules/Inventory/InventoryPickup.razor:142` | ajutor |
| `Modules/Invoices/InvoiceTemplateWorkbench.razor:478` | ajutor |
| `Modules/Maintenance/ServiceContractAddendumEditor.razor:14` | ajutor |
| `Modules/Maintenance/ServiceContractEditor.razor:12` | ajutor, inchidere |
| `Modules/Maintenance/ServiceInterventionEditor.razor:14` | ajutor, inchidere |
| `Modules/Maintenance/ServiceInterventionsSection.razor:22` | inchidere |
| `Modules/Notifications/Notifications.razor:130` | ajutor |
| `Modules/Notifications/Notifications.razor:174` | ajutor |
| `Modules/RiskAnalyses/RiskAnalysisEditor.razor:16` | ajutor, inchidere |
| `Modules/RiskAnalyses/RiskAnalyzerDialog.razor:10` | ajutor |
| `Modules/Suppliers/Invoices.razor:93` | ajutor |
| `Modules/Tasks/TaskDetailsDialog.razor:12` | inchidere |
| `Modules/Tasks/TaskForm.razor:9` | ajutor |
| `Modules/Vehicles/VehicleEditor.razor:9` | ajutor, inchidere |
| `Modules/Vehicles/VehicleExpiryDialog.razor:9` | ajutor |
| `Modules/Vehicles/VehicleTransferDialog.razor:8` | ajutor |

Antete de popup fara niciun buton (doar titlu) nu apar in lista; ele nu pot avea intercalari.
