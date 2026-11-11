<#
.SYNOPSIS
  Builds (or rebuilds) the permanent test database of the ActivityApp structure on the local SQL Server Express.
.DESCRIPTION
  The ActivityApp backup (the .bak/.zip/.zipp files of C:\ActivityApp\BackupSincronizare\BackUPAutomat) is a .NET DataSet XML (schema + rows of every table), not a SQL Server backup,
  so it cannot be restored with RESTORE. This script reads it and creates the database with every table of the file (the types follow the schema of the file: bigint, nvarchar(max), datetime, bit,
  float ...; the original lengths are not kept) and every row. The database stays on the server after the session and is used by the checks and tools that need the real structure of ActivityApp:
  ACTIVITYAPP_STRUCTURE_SERVER=.\SQLEXPRESS (Windows authentication; the server accepts no SQL logins), ACTIVITYAPP_STRUCTURE_DATABASE=ActivityApp_test.
  The data are real (clients, tickets): keep the database on this machine, never copy it into the repository.
.EXAMPLE
  .\tools\load-activityapp-test-db.ps1                 # newest backup in the automatic backup folder
  .\tools\load-activityapp-test-db.ps1 -Backup C:\ActivityApp\BackUP\backUPActivityApp-04-11-2026-09-39-24.zipp
#>
[CmdletBinding()]
param(
    [string]$Backup,
    [string]$Database = 'ActivityApp_test',
    [string]$Server = '.\SQLEXPRESS',
    [string]$BackupFolder = 'C:\ActivityApp\BackupSincronizare\BackUPAutomat'
)
$ErrorActionPreference = 'Stop'
if (-not $Backup) { $Backup = (Get-ChildItem $BackupFolder -File | Where-Object { $_.Extension -in '.zip', '.zipp', '.bak' } | Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName }
if (-not $Backup -or -not (Test-Path $Backup)) { throw "Backup not found: $Backup" }
Write-Output "Backup: $Backup"

# A zip (or a .zipp, the same zip renamed) holds the .bak; a plain .bak is the XML itself.
$work = Join-Path ([IO.Path]::GetTempPath()) ('activityapp-test-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
try {
    $xml = $Backup
    $head = [byte[]](Get-Content -Path $Backup -Encoding Byte -TotalCount 4)
    if ($head[0] -eq 0x50 -and $head[1] -eq 0x4B) {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $zip = [IO.Compression.ZipFile]::OpenRead($Backup)
        try {
            $entry = $zip.Entries | Where-Object { $_.Name -like '*.bak' } | Select-Object -First 1
            if (-not $entry) { throw 'The archive holds no .bak file.' }
            $xml = Join-Path $work $entry.Name
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $xml)
        } finally { $zip.Dispose() }
    }
    Add-Type -AssemblyName System.Data
    $dataSet = New-Object System.Data.DataSet
    Write-Output 'Reading the XML...'
    $null = $dataSet.ReadXml($xml)
    Write-Output ("Tables in the file: " + $dataSet.Tables.Count)

    $master = New-Object System.Data.SqlClient.SqlConnection "Server=$Server;Database=master;Integrated Security=True;TrustServerCertificate=True"
    $master.Open()
    $command = $master.CreateCommand()
    $command.CommandText = "IF DB_ID('$Database') IS NOT NULL BEGIN ALTER DATABASE [$Database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$Database]; END; CREATE DATABASE [$Database];"
    $null = $command.ExecuteNonQuery()
    $master.Close()

    function Get-SqlType($column) {
        switch ($column.DataType.Name) {
            'Int64' { 'bigint' } 'Int32' { 'int' } 'Int16' { 'smallint' } 'Byte' { 'tinyint' } 'Boolean' { 'bit' } 'DateTime' { 'datetime' }
            'Guid' { 'uniqueidentifier' } 'TimeSpan' { 'time' } 'DateTimeOffset' { 'datetimeoffset' } 'Decimal' { 'decimal(18,4)' } 'Double' { 'float' } 'Single' { 'real' } 'Byte[]' { 'varbinary(max)' } default { 'nvarchar(max)' }
        }
    }
    $connection = New-Object System.Data.SqlClient.SqlConnection "Server=$Server;Database=$Database;Integrated Security=True;TrustServerCertificate=True"
    $connection.Open()
    $rows = 0
    foreach ($table in $dataSet.Tables) {
        $definition = ($table.Columns | ForEach-Object { "[" + $_.ColumnName + "] " + (Get-SqlType $_) }) -join ', '
        $create = $connection.CreateCommand(); $create.CommandText = "CREATE TABLE [$($table.TableName)] ($definition)"; $null = $create.ExecuteNonQuery()
        if ($table.Rows.Count -eq 0) { continue }
        $bulk = New-Object System.Data.SqlClient.SqlBulkCopy $connection
        $bulk.DestinationTableName = "[$($table.TableName)]"; $bulk.BulkCopyTimeout = 600
        foreach ($column in $table.Columns) { $null = $bulk.ColumnMappings.Add($column.ColumnName, $column.ColumnName) }
        $bulk.WriteToServer($table)
        $rows += $table.Rows.Count
    }
    $connection.Close()
    Write-Output ("Database $Database ready on ${Server}: " + $dataSet.Tables.Count + " tables, $rows rows.")
} finally { Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue }
