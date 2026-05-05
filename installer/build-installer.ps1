<#
.SYNOPSIS
    Publishes Firestarter and produces three release artifacts in `dist\`:
      * `Firestarter-setup.exe` — Inno Setup installer (per-user by default; system-wide if elevated).
      * `Firestarter.exe`       — single-file portable (self-extracting, self-contained .NET runtime).
      * `Firestarter.zip`       — same self-contained publish folder, zipped.

.DESCRIPTION
    1. Multi-file self-contained publish to `bin\Release\…\publish\` — feeds the installer and the zip.
    2. `iscc.exe` compiles the Inno Setup script over (1).
    3. Single-file self-contained publish (with embedded Content) to a sibling folder — provides
       the portable exe.
    4. The multi-file publish folder is zipped.
    All three outputs land in `<repo>\dist\`.

.PARAMETER Configuration
    .NET configuration. Defaults to Release.

.PARAMETER Runtime
    .NET runtime identifier. Defaults to win-x64.

.PARAMETER Version
    Override the version. Defaults to the assembly version embedded in the published Firestarter.dll.

.PARAMETER SkipPublish
    Reuse existing publish output (useful when iterating on the .iss script or zip step).

.EXAMPLE
    pwsh installer\build-installer.ps1
    pwsh installer\build-installer.ps1 -Version 1.4.2
    pwsh installer\build-installer.ps1 -SkipPublish
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$Version,
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root        = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$appProject  = Join-Path $root 'src\Firestarter.App\Firestarter.App.csproj'
$publishDir  = Join-Path $root "src\Firestarter.App\bin\$Configuration\net10.0-windows\$Runtime\publish"
$singleDir   = Join-Path $root "src\Firestarter.App\bin\$Configuration\net10.0-windows\$Runtime\publish-singlefile"
$exePath     = Join-Path $publishDir 'Firestarter.exe'
$singleExe   = Join-Path $singleDir 'Firestarter.exe'
$issPath     = Join-Path $PSScriptRoot 'firestarter.iss'
$distDir     = Join-Path $root 'dist'

if (-not (Test-Path $distDir)) { New-Item -ItemType Directory -Path $distDir | Out-Null }

if (-not $SkipPublish) {
    Write-Host "▸ dotnet publish ($Configuration / $Runtime) — multi-file" -ForegroundColor Cyan
    & dotnet publish $appProject -c $Configuration -r $Runtime --self-contained true `
        -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish (multi-file) failed (exit $LASTEXITCODE)" }
}

if (-not (Test-Path $exePath)) {
    throw "Expected publish output not found: $exePath. Did the publish step succeed?"
}

if (-not $Version) {
    # Self-contained publishes produce a native AppHost `Firestarter.exe` with no managed metadata —
    # the managed assembly is `Firestarter.dll`. Prefer the .dll's AssemblyName; fall back to the
    # Win32 file-version resource on the exe; finally fall back to a placeholder.
    $dllPath = [System.IO.Path]::ChangeExtension($exePath, '.dll')
    if (Test-Path $dllPath) {
        $asmName = [System.Reflection.AssemblyName]::GetAssemblyName($dllPath)
        $Version = $asmName.Version.ToString(3)
        Write-Host "▸ Version (from $dllPath): $Version" -ForegroundColor Cyan
    }
    else {
        $info = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exePath)
        $raw  = $info.ProductVersion
        if (-not $raw) { $raw = $info.FileVersion }
        if (-not $raw) { throw "Could not resolve version from $exePath. Pass -Version explicitly." }
        $clean = ($raw -split '[+\-]')[0]
        $parts = $clean -split '\.'
        $Version = ($parts[0..([Math]::Min(2, $parts.Length - 1))] -join '.')
        Write-Host "▸ Version (from $exePath FileVersionInfo): $Version" -ForegroundColor Cyan
    }
}

# ── Installer ─────────────────────────────────────────────────────────────
$iscc = (Get-Command 'iscc.exe' -ErrorAction SilentlyContinue)?.Path
if (-not $iscc) {
    $candidates = @(
        'C:\Program Files (x86)\Inno Setup 6\iscc.exe',
        'C:\Program Files\Inno Setup 6\iscc.exe'
    )
    $iscc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $iscc) {
    throw @"
Inno Setup compiler (iscc.exe) not found.
Install from https://jrsoftware.org/isdl.php and either add it to PATH or accept the default install location.
"@
}

Write-Host "▸ iscc $iscc" -ForegroundColor Cyan
& $iscc "/DMyAppVersion=$Version" $issPath
if ($LASTEXITCODE -ne 0) { throw "iscc failed (exit $LASTEXITCODE)" }

$installer = Join-Path $distDir 'Firestarter-setup.exe'
if (-not (Test-Path $installer)) {
    throw "iscc reported success but $installer is missing. Check OutputDir / OutputBaseFilename in the .iss."
}
Write-Host "✓ $installer" -ForegroundColor Green

# ── Portable zip (full self-contained publish) ───────────────────────────
$zipPath = Join-Path $distDir 'Firestarter.zip'
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Write-Host "▸ zipping full publish → $zipPath" -ForegroundColor Cyan
Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipPath -CompressionLevel Optimal
Write-Host "✓ $zipPath ($([math]::Round((Get-Item $zipPath).Length / 1MB, 1)) MB)" -ForegroundColor Green

# ── Slim auto-update zip ──────────────────────────────────────────────────
# The full Firestarter.zip works as a portable bundle but ships ~80 MB of .NET runtime that
# rarely changes between Firestarter releases. The in-app updater already runs on a working
# .NET runtime, so the update zip can drop the framework files. The remaining set is the host
# exe + apphost shim, our own assemblies, third-party packages, native sqlite, and the wwwroot
# bundle — typically 5–10 MB. Point UpdateConstants.ManifestUrl's downloadUrl at this file.
$updateExcludes = @(
    'System.*.dll',
    'Microsoft.CSharp.dll',
    'Microsoft.VisualBasic.*.dll',
    'Microsoft.Win32.*.dll',
    'coreclr.dll',
    'clr*.dll',                # clrjit, clrcompression, clrgc, clrgcexp, clretwrc
    'mscor*.dll',
    'hostpolicy.dll',
    'hostfxr.dll',
    'msquic.dll',
    'netstandard.dll',
    'Microsoft.DiaSymReader.Native.*.dll',
    'api-ms-*.dll',
    'ucrtbase.dll',
    'vcruntime*.dll',
    'createdump.exe',
    'WindowsBase.dll',
    'PresentationCore.dll',
    'PresentationFramework*.dll',
    'PresentationNative_cor3.dll',
    'PresentationUI.dll',
    'ReachFramework.dll',
    'UIAutomation*.dll',
    'wpfgfx_cor3.dll',
    'D3DCompiler_47_cor3.dll',
    'PenImc_cor3.dll'
)

$stagingDir = Join-Path $distDir '_update-stage'
if (Test-Path $stagingDir) { Remove-Item $stagingDir -Recurse -Force }
New-Item -ItemType Directory -Path $stagingDir | Out-Null

# Mirror only the files that are not excluded into the staging tree (preserves folder structure
# so wwwroot/assets/foo.js lands at the same relative path inside the zip).
$kept = 0
$skipped = 0
Get-ChildItem -Path $publishDir -Recurse -File | ForEach-Object {
    foreach ($pat in $updateExcludes) {
        if ($_.Name -like $pat) {
            $script:skipped++
            return
        }
    }
    $rel = $_.FullName.Substring($publishDir.Length).TrimStart('\', '/')
    $dest = Join-Path $stagingDir $rel
    $destDir = Split-Path -Parent $dest
    if (-not (Test-Path $destDir)) { New-Item -ItemType Directory -Path $destDir -Force | Out-Null }
    Copy-Item $_.FullName $dest
    $script:kept++
}

$updateZip = Join-Path $distDir 'Firestarter-update.zip'
if (Test-Path $updateZip) { Remove-Item $updateZip -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($stagingDir, $updateZip, 'Optimal', $false)
Remove-Item $stagingDir -Recurse -Force

Write-Host "✓ $updateZip ($([math]::Round((Get-Item $updateZip).Length / 1MB, 1)) MB; $kept files kept, $skipped runtime files dropped)" -ForegroundColor Green

# ── Single-file portable exe ──────────────────────────────────────────────
if (-not $SkipPublish) {
    Write-Host "▸ dotnet publish ($Configuration / $Runtime) — single-file" -ForegroundColor Cyan
    & dotnet publish $appProject -c $Configuration -r $Runtime --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:IncludeAllContentForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=embedded `
        -o $singleDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish (single-file) failed (exit $LASTEXITCODE)" }
}

if (-not (Test-Path $singleExe)) {
    throw "Expected single-file output not found: $singleExe."
}

$portable = Join-Path $distDir 'Firestarter.exe'
Copy-Item $singleExe $portable -Force
Write-Host "✓ $portable" -ForegroundColor Green
