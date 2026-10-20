<#
.SYNOPSIS
  Creates (or recreates) the database blazorstoc_demo used for the help screenshots (tools\help-capture.mjs): the schema and the triggers of
  blazorstoc_test, the users and the user types of the test database, and no other data. tests\BlazorStoc.HelpDemo then fills it with fictive demo data.
.DESCRIPTION
  Needs the local MariaDB running (tools\dev-mariadb.ps1 -Action start). Uses the root account of the local MariaDB (admin.private.cnf), the test application
  account (blazorstoc_test_app) gets the data rights on the demo database, and local-secrets\demo-database.private.json (same account, other database) is written.
  The demo database is disposable: running the script again drops it and builds it again.
.EXAMPLE
  .\tools\help-demo-db.ps1
#>
[CmdletBinding()]
param(
    [string]$MariaRoot = 'C:\Dev\BlazorStoc-MariaDB',
    [string]$Source = 'blazorstoc_test',
    [string]$Target = 'blazorstoc_demo'
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$distribution = Get-ChildItem $MariaRoot -Directory -Filter 'mariadb-*-winx64' | Sort-Object Name -Descending | Select-Object -First 1
$bin = Join-Path $distribution.FullName 'bin'
$client = Join-Path $bin 'mariadb.exe'
$dump = Join-Path $bin 'mariadb-dump.exe'
$cnf = Join-Path $MariaRoot 'admin.private.cnf'
$work = Join-Path ([IO.Path]::GetTempPath()) 'help-demo-db'
$migrator = (Get-Content (Join-Path $repo 'Assets\Data\database-accounts.json') -Raw -Encoding UTF8 | ConvertFrom-Json).accounts | Where-Object key -eq 'migrator' | ForEach-Object user
New-Item -ItemType Directory -Force $work | Out-Null

function Invoke-Sql([string]$Sql, [string]$Database = '') {
    $arguments = @("--defaults-extra-file=$cnf", '--default-character-set=utf8mb4', '--batch')
    if ($Database) { $arguments += "--database=$Database" }
    & $client @arguments -e $Sql
    if ($LASTEXITCODE -ne 0) { throw "SQL failed: $Sql" }
}
function Invoke-SqlFile([string]$Path, [string]$Database) {
    & $client "--defaults-extra-file=$cnf" '--default-character-set=utf8mb4' '--batch' "--database=$Database" '--init-command=SET FOREIGN_KEY_CHECKS=0, UNIQUE_CHECKS=0' "--execute=source $($Path.Replace('\', '/'))"
    if ($LASTEXITCODE -ne 0) { throw "Loading $Path failed." }
}

Write-Output "Recreating $Target from the schema of $Source"
Invoke-Sql "DROP DATABASE IF EXISTS ``$Target``; CREATE DATABASE ``$Target`` CHARACTER SET utf8mb4 COLLATE utf8mb4_nopad_bin;"
Invoke-Sql "GRANT SELECT, INSERT, UPDATE, DELETE ON ``$Target``.* TO 'blazorstoc_test_app'@'127.0.0.1'; GRANT CREATE, DROP, REFERENCES, INDEX, ALTER, CREATE VIEW, TRIGGER ON ``$Target``.* TO '$migrator'@'127.0.0.1';"

$structure = Join-Path $work 'structure.sql'
& $dump "--defaults-extra-file=$cnf" '--no-data' '--routines' '--triggers' '--default-character-set=utf8mb4' "--result-file=$structure" $Source
if ($LASTEXITCODE -ne 0) { throw 'Dumping the structure failed.' }
Invoke-SqlFile $structure $Target

# What the application needs to start and to sign in: the users, their types and permissions, the seeds of the notification templates.
$data = Join-Path $work 'data.sql'
& $dump "--defaults-extra-file=$cnf" '--no-create-info' '--skip-triggers' '--default-character-set=utf8mb4' "--result-file=$data" $Source web_users user_types user_type_permissions notification_templates notification_template_seeds app_metadata
if ($LASTEXITCODE -ne 0) { throw 'Dumping the users failed.' }
Invoke-SqlFile $data $Target
Invoke-Sql 'DELETE FROM change_events' $Target

$config = Join-Path $repo 'local-secrets\demo-database.private.json'
$test = Get-Content (Join-Path $repo 'local-secrets\test-database.private.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$test.Database.Name = $Target
$test.Database.ExpectedName = $Target
$test.Purpose = 'Baza MariaDB cu date demonstrative fictive pentru capturile din ajutor (tools\help-capture.mjs); se reface cu tools\help-demo-db.ps1 si tests\BlazorStoc.HelpDemo.'
[IO.File]::WriteAllText($config, ($test | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding($false)))
Write-Output "Ready: $Target (configuration: local-secrets\demo-database.private.json)"
