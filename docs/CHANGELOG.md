# Changelog

Все значимые изменения документируются здесь.
Формат: [Keep a Changelog](https://keepachangelog.com/ru/1.0.0/) | Версионирование: [SemVer](https://semver.org/).

---

## [Unreleased]

### Удалено
- Команда `CreateWallsCommand` и сервис `WallCreationService` — выходят за рамки скоупа плагина (плагин не создаёт стены, только армирует существующие).

### Изменено
- **Спецификация и ROADMAP** обновлены до версии 0.2:
  - Явно зафиксирован монолитный скоуп; исключённые precast-фичи (lifting hooks,
    embedded inserts, shear connectors, sandwich layers, shop drawings, Smart
    Assemblies) перечислены в SPEC §5.2.
  - Модуль M3 расширен до **Smart Numbering** (`Mark` стержней через партицию).
  - Модуль M4 расширен до **Smart Views + Smart Dimensions + Smart Tags** (+ Smart Sheets на v1.0).
  - Модуль M5 добавляет **Bar laps** (стыки стержней по высоте) и опциональные
    **2D bar bend drawings** (v1.x).
  - Per-layer конфигурация для sandwich-walls перенесена в v1.x.

### Добавлено
- Полный пакет проектной документации:
  [SPEC](SPEC.md), [ARCHITECTURE](ARCHITECTURE.md), [MODULES](MODULES.md),
  [ROADMAP](ROADMAP.md), [CONFIG_SCHEMA](CONFIG_SCHEMA.md),
  [PARAMETERS](PARAMETERS.md), [UI_FLOWS](UI_FLOWS.md).
- Система AI-агентов для разработки: Архитектор, Разработчик, Тестировщик, Документатор.
- Базовая структура проекта (.NET 8, Revit API 2024/2025).
- Логирование через Serilog (ротация по дням, 7 файлов).
- Ribbon-панель `WRS — Wall Reinforcement Suite`.
- Доменный слой `RevitPlugin.Domain` (чистый .NET 8, без Revit) с моделями
  `RebarConfig`, `WallContext`, `RebarPlacement`, абстракциями адаптеров
  (`IRebarFactory`, `IWallRepository`, `IParameterStore`).
- Движок правил `RuleEngine` и реализации правил для MVP:
  `ExternalMeshRule`, `InternalMeshRule`, `PerimeterEdgeRule`, `OpeningEdgeRule`.
- `ConfigValidator` — статический анализ конфигурации (имя, schema_version,
  диапазоны spacing/cover/offset, перекрёстные ограничения min_thickness/max_thickness,
  count/position/sides по периметру и проёмам).
- JSON-загрузка конфигов через `ConfigStorage` (System.Text.Json) с поддержкой `schema_version`.
- Заводская конфигурация `Resources/DefaultConfigs/default-mvp.wrsconfig.json`
  (включает external + internal mesh, perimeter edge, opening edge).
- Реестр Shared Parameters плагина: `Resources/SharedParameters.txt` (16 параметров `WRS_*`).
- Команда `ArmWallCommand` (заглушка UI, оркестратор будет подключён в M1).
- Extension-методы `DocumentExtensions` для упрощения работы с Revit DB.
- CI-воркфлоу: `build.yml` (Revit 2024 + 2025), `pr-check.yml`.
- Юнит-тесты: `ConfigSerializationTests`, `ConfigValidatorTests`,
  `ExternalMeshRuleTests`, `PerimeterEdgeRuleTests`, `OpeningEdgeRuleTests`.

---

## [0.1.0] — 2025-05-20

### Добавлено
- Инициализация репозитория [github.com/dosmanoff/revit](https://github.com/dosmanoff/revit).
- Начальная структура проекта: `src/RevitPlugin`, `src/RevitPlugin.Tests`.
- `IExternalApplication` точка входа (`Application.cs`).
- `.addin` манифест для Revit.

---

[Unreleased]: https://github.com/dosmanoff/revit/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/dosmanoff/revit/releases/tag/v0.1.0
