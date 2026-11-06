<#
.SYNOPSIS
  Clears what the ActivityApp ticket import keeps about tickets, so that the tickets are read again from ActivityApp (the queue "Tichete de validat" starts again).
.DESCRIPTION
  Uses the account that administers the server (admin.private.cnf in -MariaRoot). Without -Apply it only counts what would be removed.

  Default (safe): removes the tickets waiting in the validation queue (activity_import_suggestions pending + activity_import_tickets pending_approval) and resets the reading
  mark (activity_import_settings.source_watermark), so the next import reads every ticket since the start date again. Tickets already validated (they have an intervention and a tie in
  activity_import_map), discarded by an operator, or waiting for their beneficiary are KEPT: they are not taken a second time. With the 0.5.35 rule only the tickets of a maintenance type or
  with a keyword of the nomenclator come back to the queue.

  -Everything: also forgets the tickets already validated and discarded: deletes the interventions that were created from tickets (service_intervention_source), the ties
  (activity_import_map, entity 'ticket'), all suggestions and all tickets kept. The next import then takes every ticket again as new (use it only when the interventions from tickets are to be redone).
  The audit journal is never touched.

  Run it with a fresh database backup (Setari -> Backup, or tools\export-dev-data.ps1 for development) and with the import not running (disable it in Setari -> ActivityApp, or stop the application:
  tools\supervisor.ps1 -Action stop -Port 5087). A transaction makes the change all-or-nothing; it cannot be undone afterwards.
.EXAMPLE
  .\tools\reset-activity-tickets.ps1 -MariaRoot D:\BlazorStoc\MariaDB                 # counts only
  .\tools\reset-activity-tickets.ps1 -MariaRoot D:\BlazorStoc\MariaDB -Apply          # clears the queue and the reading mark
  .\tools\reset-activity-tickets.ps1 -MariaRoot D:\BlazorStoc\MariaDB -Apply -Everything
#>
[CmdletBinding()]
param(
    [string]$MariaRoot = 'C:\Dev\BlazorStoc-MariaDB',
    [string]$Database = 'BlazorStoc',
    [switch]$Apply,
    [switch]$Everything
)
$ErrorActionPreference = 'Stop'
$distribution = Get-ChildItem $MariaRoot -Directory -Filter 'mariadb-*-winx64' | Sort-Object Name -Descending | Select-Object -First 1
if (-not $distribution) { throw "No MariaDB distribution under $MariaRoot." }
$client = Join-Path $distribution.FullName 'bin\mariadb.exe'
$cnf = Join-Path $MariaRoot 'admin.private.cnf'
if (-not (Test-Path $cnf)) { throw "$cnf not found; the account that administers the server is needed." }

function Invoke-Sql([string]$Sql) {
    $output = & $client "--defaults-extra-file=$cnf" $Database -t -e $Sql
    if ($LASTEXITCODE -ne 0) { throw 'The statement failed (nothing was changed when a transaction was open).' }
    $output
}

Write-Output 'Now kept by the ticket import:'
Invoke-Sql @"
SELECT
 (SELECT COUNT(*) FROM activity_import_suggestions WHERE status='pending') AS in_queue,
 (SELECT COUNT(*) FROM activity_import_tickets WHERE state='pending_approval') AS tickets_in_queue,
 (SELECT COUNT(*) FROM activity_import_tickets WHERE state='waiting_beneficiary') AS waiting_beneficiary,
 (SELECT COUNT(*) FROM activity_import_tickets WHERE state='discarded') AS discarded,
 (SELECT COUNT(*) FROM activity_import_tickets WHERE state='imported') AS validated,
 (SELECT COUNT(*) FROM service_intervention_source) AS interventions_from_tickets,
 (SELECT COUNT(*) FROM activity_import_map WHERE entity='ticket') AS ticket_ties;
"@

if (-not $Apply) { Write-Output 'Nothing changed (add -Apply to clear).'; return }

$sql = if ($Everything) {
@"
START TRANSACTION;
DELETE si FROM service_interventions si JOIN service_intervention_source s ON s.intervention_id = si.id;
DELETE FROM service_intervention_source;
DELETE FROM activity_import_map WHERE entity='ticket';
DELETE FROM activity_import_suggestions;
DELETE FROM activity_import_tickets;
UPDATE activity_import_settings SET source_watermark=NULL WHERE id=1;
COMMIT;
"@
} else {
@"
START TRANSACTION;
DELETE FROM activity_import_tickets WHERE state='pending_approval';
DELETE FROM activity_import_suggestions WHERE status='pending';
UPDATE activity_import_settings SET source_watermark=NULL WHERE id=1;
COMMIT;
"@
}
Invoke-Sql $sql | Out-Null
Write-Output ('Cleared' + $(if ($Everything) { ' (everything, interventions from tickets included)' } else { ' (the queue and the reading mark)' }) + '. Run the import again (Setari -> ActivityApp -> Importa tichete).')
