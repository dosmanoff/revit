# Deploy the freshly-built StairsReinforcement plugin to the Revit 2025 add-ins folder.
# Run this with Revit CLOSED (the loaded DLL is locked while Revit runs), then reopen Revit.
$src = Join-Path $PSScriptRoot "StairsReinforcement\bin\Release\net8.0-windows"
$dst = "C:\ProgramData\Autodesk\Revit\Addins\2025"

$files = @("StairsReinforcement.dll", "StairsReinforcement.Geometry.dll")
foreach ($f in $files) {
    $s = Join-Path $src $f
    if (-not (Test-Path $s)) { Write-Host "MISSING build output: $s" -ForegroundColor Red; exit 1 }
}
try {
    foreach ($f in $files) { Copy-Item (Join-Path $src $f) (Join-Path $dst $f) -Force -ErrorAction Stop }
    Write-Host "Deployed to $dst :" -ForegroundColor Green
    Get-ChildItem "$dst\StairsReinforcement*.dll" | Select-Object Name, Length, LastWriteTime | Format-Table -Auto
    Write-Host "Now open Revit and run Smart Tools > Stairs Reinforcement > Generate Stair Rebar." -ForegroundColor Cyan
} catch {
    Write-Host "FAILED (is Revit still open?): $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
