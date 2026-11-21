#Requires -RunAsAdministrator
<#
  Publishes Sensor Panel to %LOCALAPPDATA%\Programs\SensorPanel and registers the logon task
  (runs elevated at logon, no UAC prompt). Re-run after pulling changes to update.

  Usage:  .\scripts\install.ps1            install / update
          .\scripts\install.ps1 -Uninstall  remove task and files (keeps your layouts in %APPDATA%)
#>
param([switch]$Uninstall)

$ErrorActionPreference = 'Stop'
$repo    = Split-Path $PSScriptRoot -Parent
$target  = Join-Path $env:LOCALAPPDATA 'Programs\SensorPanel'
$exe     = Join-Path $target 'SensorPanel.exe'

# Stop a running instance so files can be replaced.
Get-Process SensorPanel -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

if ($Uninstall) {
    if (Test-Path $exe) { & $exe --uninstall-startup | Out-Null }
    if (Test-Path $target) { Remove-Item $target -Recurse -Force }
    Write-Host "Uninstalled. Layouts and settings remain in $env:APPDATA\SensorPanel"
    return
}

Write-Host "Publishing to $target ..."
dotnet publish (Join-Path $repo 'src\SensorPanel\SensorPanel.csproj') -c Release -o $target --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

$p = Start-Process -FilePath $exe -ArgumentList '--install-startup' -Wait -PassThru
if ($p.ExitCode -ne 0) { throw "Registering the logon task failed - see $env:APPDATA\SensorPanel\panel.log" }

Start-Process -FilePath $exe
Write-Host "Installed and started. It will start automatically at logon."
