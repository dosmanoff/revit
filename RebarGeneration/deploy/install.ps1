<#
.SYNOPSIS
    Развернуть RebarGeneration в папку надстроек Revit.

.DESCRIPTION
    Раскладка повторяет соседние плагины репозитория: DLL и её зависимости —
    в подпапке %APPDATA%\Autodesk\Revit\Addins\<год>\RebarGeneration\, а сам
    .addin лежит уровнем выше и ссылается внутрь. Плоская раскладка в общей
    папке Addins мешала бы соседним плагинам.

    Копируется ВЕСЬ рантайм: RebarGeneration.dll, RebarGeneration.Contracts.dll
    и WallReinforcement.Geometry.dll (на неё опирается математика раскладки).
    Без последней плагин упадёт на первом же задании.

    Revit при запуске держит загруженную DLL заблокированной. Если плагин ещё
    ни разу не ставился, копировать можно и на работающем Revit — блокировать
    нечего; но подхватит он .addin только при следующем старте.

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

$binDir = Join-Path $root "src/RebarGeneration/bin/$Configuration"
if (-not (Test-Path $binDir)) {
    throw "Не найдена сборка: $binDir`nСобери: dotnet build src/RebarGeneration/RebarGeneration.csproj -c `"$Configuration`""
}

$addinsRoot = Join-Path $env:APPDATA "Autodesk/Revit/Addins/$RevitVersion"
$target     = Join-Path $addinsRoot 'RebarGeneration'

$revitRunning = [bool](Get-Process -Name 'Revit' -ErrorAction SilentlyContinue)
$alreadyThere = Test-Path (Join-Path $target 'RebarGeneration.dll')

if ($revitRunning -and $alreadyThere -and -not $Force) {
    throw "Revit запущен, а плагин уже установлен — DLL заблокирована. Закрой Revit (или -Force, если уверен)."
}
if ($revitRunning -and -not $alreadyThere) {
    Write-Host "Revit запущен, но плагин ещё не установлен — копирование безопасно." -ForegroundColor Yellow
    Write-Host "Подхватит его Revit при следующем запуске." -ForegroundColor Yellow
}

New-Item -ItemType Directory -Force -Path $target | Out-Null

# Полный рантайм плагина. Явный список, а не маска: маска RebarGeneration*.dll
# молча пропустила бы WallReinforcement.Geometry.dll, и падение всплыло бы
# только на первом задании.
$runtime = @(
    'RebarGeneration.dll',
    'RebarGeneration.Contracts.dll',
    'WallReinforcement.Geometry.dll',      # математика раскладки набора
    'SlabReinforcement.Geometry.dll',      # сканлайн-клиппинг по контуру с отверстиями
    'RebarGeneration.deps.json'
)

foreach ($name in $runtime) {
    $src = Join-Path $binDir $name
    if (-not (Test-Path $src)) { throw "В сборке нет обязательного файла: $name ($binDir)" }
    Copy-Item $src -Destination $target -Force
    Write-Host "  → $name"
}

# Отладочные символы — по возможности, их отсутствие не повод падать.
Get-ChildItem -Path $binDir -Filter '*.pdb' -ErrorAction SilentlyContinue |
    ForEach-Object { Copy-Item $_.FullName -Destination $target -Force }

$samples = Join-Path $root 'samples'
if (Test-Path $samples) {
    Copy-Item $samples -Destination $target -Recurse -Force
    Write-Host "  → samples\"
}

$addin = Join-Path $PSScriptRoot "RebarGeneration.$RevitVersion.addin"
Copy-Item $addin -Destination (Join-Path $addinsRoot 'RebarGeneration.addin') -Force
Write-Host "  → RebarGeneration.addin"

Write-Host ""
Write-Host "Установлено: $target" -ForegroundColor Green
Write-Host "Манифест:    $(Join-Path $addinsRoot 'RebarGeneration.addin')" -ForegroundColor Green
Write-Host ""
Write-Host "Перезапусти Revit. После этого:"
Write-Host "  • лента: вкладка 'Smart Tools' → панель 'Rebar Generation';"
Write-Host "  • агенту доступен тип RebarGeneration.Api из /exec по имени, без clr.AddReference."
