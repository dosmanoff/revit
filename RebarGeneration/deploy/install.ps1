<#
.SYNOPSIS
    Развернуть RebarGeneration в папку надстроек Revit.

.DESCRIPTION
    Копирует собранные DLL и .addin в %APPDATA%\Autodesk\Revit\Addins\<год>.
    Revit при запуске держит DLL заблокированной, поэтому перед установкой его
    надо закрыть — скрипт это проверяет и отказывается работать вслепую.

.EXAMPLE
    pwsh deploy/install.ps1 -RevitVersion 2025
    pwsh deploy/install.ps1 -RevitVersion 2025 -Configuration "Debug R2025"
#>
[CmdletBinding()]
param(
    [ValidateSet('2025', '2026')]
    [string]$RevitVersion = '2025',

    [string]$Configuration = '',

    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

if (-not $Configuration) { $Configuration = "Release R$RevitVersion" }

if (Get-Process -Name 'Revit' -ErrorAction SilentlyContinue) {
    if (-not $Force) {
        throw "Revit запущен — он держит DLL заблокированной. Закрой Revit (или запусти с -Force, если уверен)."
    }
    Write-Warning "Revit запущен; -Force задан, продолжаю. Копирование может упасть на блокировке файла."
}

$binDir = Join-Path $root "src/RebarGeneration/bin/$Configuration"
if (-not (Test-Path $binDir)) {
    throw "Не найдена сборка: $binDir`nСобери: dotnet build src/RebarGeneration/RebarGeneration.csproj -c `"$Configuration`""
}

$target = Join-Path $env:APPDATA "Autodesk/Revit/Addins/$RevitVersion"
New-Item -ItemType Directory -Force -Path $target | Out-Null

$dlls = Get-ChildItem -Path $binDir -Filter 'RebarGeneration*.dll'
if (-not $dlls) { throw "В $binDir нет ни одной RebarGeneration*.dll" }

foreach ($dll in $dlls) {
    Copy-Item $dll.FullName -Destination $target -Force
    Write-Host "  → $($dll.Name)"
}

$addin = Join-Path $PSScriptRoot "RebarGeneration.$RevitVersion.addin"
Copy-Item $addin -Destination (Join-Path $target 'RebarGeneration.addin') -Force
Write-Host "  → RebarGeneration.addin"

Write-Host ""
Write-Host "Установлено в $target" -ForegroundColor Green
Write-Host "Запусти Revit: вкладка 'Smart Tools' → панель 'Rebar Generation'."
Write-Host "Агенту после этого доступен тип RebarGeneration.Api из /exec без clr.AddReference."
