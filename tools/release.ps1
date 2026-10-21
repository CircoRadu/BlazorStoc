<#
.SYNOPSIS
  Builds a release package (.bsu) of BlazorStoc: checks, publish for the server, manifest, signature, verification (docs/PROPUNERE_ACTUALIZARE.md, stage 2).
.DESCRIPTION
  1. runs the whole in-memory suite (tools\run-checks.ps1) unless -SkipChecks;
  2. publishes Release for the server (win-x64, framework-dependent) into artifacts\release\<version>\app; the private files of the accounts, the data of this
     machine and the logs are not part of a publish (BlazorStoc.csproj), and the packer refuses the package if it finds any;
  3. asks the published application to describe itself (BlazorStoc.dll --describe-release: version, schema version, migrations with their checksums);
  4. signs the manifest with the key of THIS machine (%LOCALAPPDATA%\BlazorStoc-release, DPAPI for the current user; made with -NewKey or at the first release);
  5. writes artifacts\release\BlazorStoc-<version>.bsu and verifies it again from the file, the way a server will;
  6. makes artifacts\release\BlazorStoc-Actualizare-<version>.exe, the installer for a server: the launcher (tools\BlazorStoc.Installer, self-contained, one file) with the package and the
     updater of this version appended; its SHA-256 is printed so the copy on the server can be compared (Get-FileHash), and it is checked once here (--check: payload, checksum, signature).
  The installer also carries the installers of what a server may lack: the .NET 9 runtime and the ASP.NET Core 9 runtime (the same 9.0.x as on this build machine) and the Visual C++
  runtime (x64). They are downloaded from Microsoft once into artifacts\prerequisites, refused unless their Authenticode signature is valid and Microsoft's, and run by the installer only
  when the server lacks the runtime (-SkipPrerequisites leaves them out).
  The version is the <Version> of BlazorStoc.csproj unless -Version says otherwise. The key of a machine is never replaced by accident: a server trusts its fingerprint.
  The fingerprint is printed at the end: compare it with the one a server asks you to confirm the first time it sees a package of this machine.
.EXAMPLE
  .\tools\release.ps1
  .\tools\release.ps1 -Version 0.5.1 -MinFromVersion 0.5.0 -SkipChecks
  .\tools\release.ps1 -NewKey          # makes the key of this machine (first release on a new build machine)
#>
[CmdletBinding()]
param(
    [string]$Version,
    [string]$MinFromVersion,
    [string]$NotesFile,                   # plain text shown to the administrator before the update (Setari -> Actualizare): what the version brings
    [string]$KeyFolder,
    [switch]$NewKey,
    [switch]$SkipChecks,
    [switch]$PackageOnly,                 # only the update package (.bsu) for servers that already run BlazorStoc: no installer, no prerequisites
    [switch]$SkipPrerequisites            # an installer without the .NET and Visual C++ runtime installers (smaller; the server must have them)
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $repo 'BlazorStoc.csproj'
$updater = Join-Path $repo 'tools\BlazorStoc.Updater\BlazorStoc.Updater.csproj'
if (-not $Version) {
    $match = [regex]::Match((Get-Content $project -Raw), '<Version>\s*([0-9]+\.[0-9]+\.[0-9]+)\s*</Version>')
    if (-not $match.Success) { throw 'No <Version> in BlazorStoc.csproj; pass -Version.' }
    $Version = $match.Groups[1].Value
}
if ($Version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$') { throw "The version '$Version' is not major.minor.patch." }
$root = Join-Path $repo "artifacts\release\$Version"
$app = Join-Path $root 'app'
$package = Join-Path $repo "artifacts\release\BlazorStoc-$Version.bsu"
$installer = Join-Path $repo "artifacts\release\BlazorStoc-Actualizare-$Version.exe"
$launcherProject = Join-Path $repo 'tools\BlazorStoc.Installer\BlazorStoc.Installer.csproj'
function Step([string]$Text) { Write-Output ("== {0}" -f $Text) }
function Run-Native([string]$What, [scriptblock]$Command) { & $Command; if ($LASTEXITCODE -ne 0) { throw "$What failed (exit code $LASTEXITCODE)." } }

if (-not $SkipChecks) {
    Step 'Checks (whole in-memory suite)'
    & (Join-Path $PSScriptRoot 'run-checks.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'The checks failed; no package was made.' }
}

Step "Publish $Version (win-x64)"
if (Test-Path $root) { Remove-Item $root -Recurse -Force }
New-Item -ItemType Directory -Force $app | Out-Null
Run-Native 'dotnet publish' { dotnet publish $project -c Release -r win-x64 --self-contained false "-p:Version=$Version" -o $app --nologo -v q }

Step 'Description of the release'
$describe = Join-Path $root 'describe.json'
$text = (& dotnet (Join-Path $app 'BlazorStoc.dll') --describe-release) -join "`n"
if ($LASTEXITCODE -ne 0 -or -not $text) { throw 'BlazorStoc.dll --describe-release failed.' }
[IO.File]::WriteAllText($describe, $text, (New-Object Text.UTF8Encoding($false)))
$described = ($text | ConvertFrom-Json)
if ($described.version -ne $Version) { throw "The published application says version $($described.version), not $Version." }

Step 'Updater tool'
Run-Native 'dotnet build (updater)' { dotnet build $updater -c Release --nologo -v q }
$tool = Join-Path $repo 'tools\BlazorStoc.Updater\bin\Release\net9.0\BlazorStoc.Updater.dll'
$keyArguments = @(); if ($KeyFolder) { $keyArguments = @('--key-dir', $KeyFolder) }

Step 'Signing key of this machine'
$existing = (& dotnet $tool fingerprint @keyArguments) 2>$null
if ($NewKey -or $LASTEXITCODE -ne 0) {
    if ($NewKey -and $LASTEXITCODE -eq 0) { throw 'This machine already has a signing key. Replacing it makes every server that trusts it refuse the next release; delete the key folder by hand if that is really wanted.' }
    Run-Native 'keygen' { dotnet $tool keygen @keyArguments }
}

Step 'Package'
$pack = @('pack', '--app', $app, '--describe', $describe, '--out', $package)
if ($MinFromVersion) { $pack += @('--min-from', $MinFromVersion) }
if ($NotesFile) { $pack += @('--notes-file', (Resolve-Path $NotesFile).Path) }
Run-Native 'pack' { dotnet $tool @pack @keyArguments }

Step 'Verification of the package from the file'
$fingerprint = ((& dotnet $tool fingerprint @keyArguments) -join '').Replace(' ', '').ToLowerInvariant()
Run-Native 'verify' { dotnet $tool verify $package --trusted $fingerprint }

$prerequisites = $null
if (-not $SkipPrerequisites -and -not $PackageOnly) {
    Step 'Prerequisites for the installer (.NET 9 runtimes, Visual C++ runtime)'
    $runtimeVersion = $null
    foreach ($line in (& dotnet --list-runtimes)) {
        if ($line -match '^Microsoft\.AspNetCore\.App (9\.\d+\.\d+) ') { $candidate = [version]$Matches[1]; if (-not $runtimeVersion -or $candidate -gt $runtimeVersion) { $runtimeVersion = $candidate } }
    }
    if (-not $runtimeVersion) { throw 'No Microsoft.AspNetCore.App 9.x runtime on this machine to take the version from (install it, or use -SkipPrerequisites).' }
    $cache = Join-Path $repo 'artifacts\prerequisites'
    $prerequisites = Join-Path $root 'prerequisites'
    New-Item -ItemType Directory -Force $cache, $prerequisites | Out-Null
    $wanted = @(
        @{ Name = "dotnet-runtime-$runtimeVersion-win-x64.exe"; Url = "https://builds.dotnet.microsoft.com/dotnet/Runtime/$runtimeVersion/dotnet-runtime-$runtimeVersion-win-x64.exe" },
        @{ Name = "aspnetcore-runtime-$runtimeVersion-win-x64.exe"; Url = "https://builds.dotnet.microsoft.com/dotnet/aspnetcore/Runtime/$runtimeVersion/aspnetcore-runtime-$runtimeVersion-win-x64.exe" },
        @{ Name = 'vc_redist.x64.exe'; Url = 'https://aka.ms/vs/17/release/vc_redist.x64.exe' })
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    foreach ($item in $wanted) {
        $file = Join-Path $cache $item.Name
        if (-not (Test-Path $file)) {
            Write-Output "  downloading $($item.Url)"
            $ProgressPreference = 'SilentlyContinue'
            Invoke-WebRequest -Uri $item.Url -OutFile "$file.part" -UseBasicParsing
            Move-Item "$file.part" $file -Force
        }
        $signature = Get-AuthenticodeSignature -LiteralPath $file
        if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') {
            throw "$($item.Name) is not signed by Microsoft (status $($signature.Status), signer '$($signature.SignerCertificate.Subject)'); it is NOT put in the installer (delete it from artifacts\prerequisites to download it again)."
        }
        Copy-Item $file $prerequisites
        Write-Output ("  {0}: {1:N1} MB, signed by Microsoft" -f $item.Name, ((Get-Item $file).Length / 1MB))
    }
}

if ($PackageOnly) {
    Write-Output ''
    Write-Output "Package:     $package"
    Write-Output ("Size:        {0:N1} MB" -f ((Get-Item $package).Length / 1MB))
    Write-Output ("Fingerprint: {0}" -f ((& dotnet $tool fingerprint @keyArguments) -join ''))
    Write-Output 'Install: upload the package in Setari -> Actualizare (docs\OPERARE_ACTUALIZARE.md). No installer was made (-PackageOnly).'
    return
}
Step 'Installer for the server'
$updaterOut = Join-Path $root 'updater'
$launcherOut = Join-Path $root 'launcher'
Run-Native 'dotnet publish (updater)' { dotnet publish $updater -c Release -o $updaterOut --nologo -v q }
Run-Native 'dotnet publish (launcher)' { dotnet publish $launcherProject -c Release -o $launcherOut --nologo -v q }
$prerequisiteArguments = @(); if ($prerequisites) { $prerequisiteArguments = @('--prerequisites', $prerequisites) }
Run-Native 'make-installer' { dotnet $tool make-installer --launcher (Join-Path $launcherOut 'BlazorStoc.Installer.exe') --package $package --updater $updaterOut --out $installer --version $Version @prerequisiteArguments }
& $installer --check --trusted $fingerprint --no-pause
if ($LASTEXITCODE -ne 0) { throw "The installer failed its own check (exit code $LASTEXITCODE)." }

Write-Output ''
Write-Output "Package:     $package"
Write-Output ("Size:        {0:N1} MB" -f ((Get-Item $package).Length / 1MB))
Write-Output ("Fingerprint: {0}" -f ((& dotnet $tool fingerprint @keyArguments) -join ''))
Write-Output "Installer:   $installer"
Write-Output ("             {0:N1} MB, SHA-256 {1}" -f ((Get-Item $installer).Length / 1MB), (Get-FileHash $installer -Algorithm SHA256).Hash.ToLowerInvariant())
Write-Output 'Install: upload the package in Setari -> Actualizare, or copy the installer to the server and run it as administrator (docs\OPERARE_ACTUALIZARE.md).'
