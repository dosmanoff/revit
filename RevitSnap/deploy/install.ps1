
[CmdletBinding()]
param(
    [string[]]$RevitVersion = @('2025', '2026'),
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

$revitRunning = Get-Process -Name 'Revit' -ErrorAction SilentlyContinue
if ($revitRunning) {
    Write-Warning 'Revit is running. Files will be copied, but Revit picks up the add-in only after restart (and may lock old DLLs).'
}

foreach ($ver in $RevitVersion) {
    $addinsDir = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$ver"
    $targetDir = Join-Path $addinsDir 'RevitActionRecorder'
    $manifestSource = Join-Path $PSScriptRoot "RevitActionRecorder.$ver.addin"
    $manifestTarget = Join-Path $addinsDir 'RevitActionRecorder.addin'

    if ($Uninstall) {
        if (Test-Path $manifestTarget) { Remove-Item $manifestTarget -Force }
        if (Test-Path $targetDir) { Remove-Item $targetDir -Recurse -Force }
        Write-Host "Removed RevitActionRecorder from Revit $ver."
        continue
    }

    $binDir = Join-Path $repoRoot "src\RevitActionRecorder\bin\$Configuration R$ver"
    if (-not (Test-Path (Join-Path $binDir 'RevitActionRecorder.dll'))) {
        Write-Warning "Build output not found: $binDir. Build first: dotnet build -c `"$Configuration R$ver`". Skipping Revit $ver."
        continue
    }

    New-Item -ItemType Directory -Force $targetDir | Out-Null

    # Clean up leftovers from previous locked-file installs.
    Get-ChildItem $targetDir -Filter '*.old_*' -ErrorAction SilentlyContinue | ForEach-Object {
        try { Remove-Item $_.FullName -Force -ErrorAction Stop } catch { }
    }

    $stamp = Get-Date -Format 'yyyyMMddHHmmss'
    foreach ($file in Get-ChildItem $binDir -File) {
        $target = Join-Path $targetDir $file.Name
        try {
            Copy-Item $file.FullName $target -Force -ErrorAction Stop
        } catch {
            # A loaded DLL cannot be overwritten, but it can be renamed; Revit
            # keeps using the renamed file and picks up the new one on restart.
            Move-Item $target "$target.old_$stamp" -Force
            Copy-Item $file.FullName $target -Force
            Write-Host "  $($file.Name) was locked - old file renamed, new one staged."
        }
    }
    Copy-Item $manifestSource $manifestTarget -Force
    Write-Host "Installed RevitActionRecorder ($Configuration) for Revit $ver -> $targetDir"
}
