<#
.SYNOPSIS
  Builds out\SensorPanelSetup.exe: publishes the app, adds PresentMon, builds the MSI and the setup bundle.
  Used both locally and by GitHub Actions.
.EXAMPLE
  .\installer\build.ps1                    # version 0.1.0, runs the tests first
  .\installer\build.ps1 -Version 1.2.3 -SkipTests
#>
param(
    [string]$Version = '0.1.0',
    [switch]$SkipTests
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'   # Invoke-WebRequest is very slow with the progress bar on

if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must look like 1.2.3 (got '$Version')." }

$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'out'
$publish = Join-Path $out 'publish'
$cache = Join-Path $out 'cache'

# PresentMon (MIT) — pinned and hash-checked; shipped as presentmon.exe next to SensorPanel.exe.
$presentMon = @{
    Url    = 'https://github.com/GameTechDev/PresentMon/releases/download/v2.6.0/PresentMon-2.6.0-x64.exe'
    File   = 'PresentMon-2.6.0-x64.exe'
    Sha256 = 'b2a706bc6ad475749e3b7e3409263aa1e6906d45bdcf993f6dbc0f660188f1af'
}

function Invoke-Checked([string]$what, [scriptblock]$cmd) {
    Write-Host "==> $what" -ForegroundColor Cyan
    & $cmd
    if ($LASTEXITCODE -ne 0) { throw "$what failed (exit $LASTEXITCODE)." }
}

function Get-Verified([hashtable]$dep) {
    New-Item -ItemType Directory -Force $cache | Out-Null
    $path = Join-Path $cache $dep.File
    $ok = { (Test-Path $path) -and (Get-FileHash $path -Algorithm SHA256).Hash -eq $dep.Sha256 }
    if (-not (& $ok)) {
        Write-Host "==> Downloading $($dep.File)" -ForegroundColor Cyan
        Invoke-WebRequest $dep.Url -OutFile $path -UseBasicParsing
        if (-not (& $ok)) {
            Remove-Item $path -Force -ErrorAction SilentlyContinue
            throw "SHA-256 mismatch for $($dep.File): the download is not the pinned release."
        }
    }
    $path
}

if (-not $SkipTests) {
    Invoke-Checked 'Tests' { dotnet test (Join-Path $root 'tests\SensorPanel.Tests') -c Release }
}

if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
Invoke-Checked 'Publish app (win-x64, self-contained)' {
    dotnet publish (Join-Path $root 'src\SensorPanel') -c Release -r win-x64 --self-contained `
        -p:Version=$Version -o $publish
}
Copy-Item (Get-Verified $presentMon) (Join-Path $publish 'presentmon.exe')

$msiOut = Join-Path $out 'msi'
Invoke-Checked 'Build MSI' {
    dotnet build (Join-Path $PSScriptRoot 'Package\Package.wixproj') -c Release `
        -p:AppDir=$publish -p:ProductVersion=$Version -o $msiOut
}
$bundleOut = Join-Path $out 'bundle'
$msi = Join-Path $msiOut 'SensorPanel.msi'
Invoke-Checked 'Build setup bundle' {
    dotnet build (Join-Path $PSScriptRoot 'Bundle\Bundle.wixproj') -c Release `
        -p:MsiPath=$msi -p:ProductVersion=$Version -o $bundleOut
}

$setup = Join-Path $out 'SensorPanelSetup.exe'
Copy-Item (Join-Path $bundleOut 'SensorPanelSetup.exe') $setup -Force
$mb = [math]::Round((Get-Item $setup).Length / 1MB, 1)
Write-Host "Built $setup ($mb MB, version $Version)" -ForegroundColor Green
