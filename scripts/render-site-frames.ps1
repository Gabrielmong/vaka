<#
.SYNOPSIS
  Renders the website's panel images (site/public/panel) with the app's own renderer on mock sensors.

.DESCRIPTION
  1. Checks the fonts the templates use are installed. Without one, Skia would quietly substitute another
     font and the site would show something the real panel never draws, so it stops instead.
  2. Builds the app (Release) and runs `Vaka.dll --frames`. Running the dll through `dotnet` skips
     the exe's administrator manifest; --frames touches no hardware.
  3. Fails if the first page load (Duo's loop + every template's idle still) is over 3 MB.

  Skips the render when site/public/panel/manifest.json exists, unless -Force.
  Used by `npm run frames` (site/) and the pages job in .github/workflows/build.yml.
#>
param(
    [switch]$Force,
    [string[]]$Fonts = @('Bahnschrift', 'Segoe UI', 'Segoe UI Variable', 'Consolas'),
    [int]$BudgetBytes = 3MB
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'site\public\panel'

# -- Fonts --
$installed = @()
foreach ($key in 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts', 'HKCU:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts') {
    if (Test-Path $key) { $installed += (Get-Item $key).GetValueNames() }
}
foreach ($font in $Fonts) {
    $pattern = '^' + [regex]::Escape($font) + ' \('
    if (-not ($installed | Where-Object { $_ -match $pattern })) {
        Write-Host "Font missing on this machine: $font" -ForegroundColor Red
        exit 1
    }
}
Write-Host "Fonts present: $($Fonts -join ', ')"

# -- Render --
if ((Test-Path (Join-Path $out 'manifest.json')) -and -not $Force) {
    Write-Host "Frames already in $out (use -Force to render again)."
} else {
    $proj = Join-Path $root 'src\Vaka'
    $buildArgs = @('build', $proj, '-c', 'Release', '--nologo', '-v', 'q')
    if (Test-Path (Join-Path $root 'src\editor-ui\dist\index.html')) { $buildArgs += '-p:SkipEditorUi=true' }
    & dotnet @buildArgs
    if ($LASTEXITCODE -ne 0) { Write-Host 'App build failed.' -ForegroundColor Red; exit 1 }

    # The framework-dependent build, never a leftover self-contained publish (bin\Release\...\win-x64).
    $dll = Get-Item (Join-Path $proj 'bin\Release\net10.0-windows\Vaka.dll') -ErrorAction SilentlyContinue
    if (-not $dll) { Write-Host 'Vaka.dll not found after the build.' -ForegroundColor Red; exit 1 }

    if (Test-Path $out) { Remove-Item $out -Recurse -Force }
    New-Item -ItemType Directory -Force $out | Out-Null
    & dotnet $dll.FullName --frames $out
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path (Join-Path $out 'manifest.json'))) {
        Write-Host 'Rendering frames failed.' -ForegroundColor Red; exit 1
    }
}

# -- First-load budget --
$manifest = Get-Content (Join-Path $out 'manifest.json') -Raw | ConvertFrom-Json
$hero = @(Get-ChildItem (Join-Path $out $manifest.hero) -File | Where-Object Name -match '^\d\d\.webp$')
if ($hero.Count -eq 0) { Write-Host "No loop frames for the hero template '$($manifest.hero)'." -ForegroundColor Red; exit 1 }
$stills = $manifest.templates | ForEach-Object { Get-Item (Join-Path $out "$($_.id)\idle.webp") }
$bytes = (($hero + $stills) | Measure-Object Length -Sum).Sum
$all = (Get-ChildItem $out -Recurse -File | Measure-Object Length -Sum).Sum
Write-Host ("First load: {0:N0} KB of images (budget {1:N0} KB); all frames: {2:N1} MB" -f ($bytes / 1KB), ($BudgetBytes / 1KB), ($all / 1MB))
if ($bytes -gt $BudgetBytes) { Write-Host 'Over the first-load budget: lower the WebP quality in FrameRecorder.' -ForegroundColor Red; exit 1 }
