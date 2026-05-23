# Архитектура плагина «Wall Reinforcement Suite»

> Версия документа: 0.1 (draft)
> Сопровождает: [SPEC.md](SPEC.md), [MODULES.md](MODULES.md)

---

## 1. Принципы

1. **Тонкие команды, толстые сервисы.** `IExternalCommand` оркестрирует, сервисы — реализуют логику.
2. **Чистая доменная модель.** Доменные классы (`WallReinforcementJob`, `RebarConfig`, `RebarRule`) не зависят от Revit API.
3. **Адаптер над Revit API.** Всё взаимодействие с Revit DB — через интерфейсы (`IRevitDocument`, `IWallRepository`, `IRebarFactory`), что делает доменную логику тестируемой.
4. **Конфигурация как код.** JSON-конфиги версионируются и переносятся между проектами; загрузка через сериализатор с миграциями схем.
5. **Идемпотентность.** Перед запуском операции собирается «отпечаток» уже созданной арматуры (по `WRS_Config_Id` + `WRS_Rule_Id`), сравнивается с целевым состоянием — Revit-объекты создаются/обновляются/удаляются точечно.
6. **Транзакции — внешний контур.** Сервисы работают с `Document`, но не открывают транзакции сами; их открывает оркестрационный сервис верхнего уровня (`WallReinforcementOrchestrator`).

---

## 2. Слои

```
+--------------------------------------------------+
|              UI (WPF + MVVM)                     |
|  Views/  ViewModels/  Resources/                 |
+--------------------------------------------------+
|              Commands (IExternalCommand)         |
|  ArmWallCommand, DowelCommand, ConfigCommand     |
+--------------------------------------------------+
|              Application Services                |
|  WallReinforcementOrchestrator                   |
|  DowelOrchestrator                               |
|  ViewGenerationOrchestrator                      |
|  ScheduleOrchestrator                            |
|  ConfigService                                   |
+--------------------------------------------------+
|              Domain                              |
|  RebarConfig, RebarRule, RebarPlacement,         |
|  WallContext, JobReport, RuleResult, DTOs        |
+--------------------------------------------------+
|              Revit Adapters                      |
|  IRevitDocument, IWallRepository, IRebarFactory, |
|  IViewFactory, IScheduleFactory, IParameterStore |
+--------------------------------------------------+
|              Infrastructure                      |
|  ConfigStorage (JSON), Logger (Serilog),         |
|  ExtensibleStorage, SharedParameters             |
+--------------------------------------------------+
```

Зависимости направлены сверху вниз. Domain не знает о Revit API; адаптеры реализуют интерфейсы, объявленные в Domain.

---

## 3. Структура проекта в репозитории

```
src/
├── RevitPlugin/                         ← основной .csproj (Revit Add-in)
│   ├── RevitPlugin.csproj
│   ├── RevitPlugin.addin
│   ├── Application.cs                   ← IExternalApplication, регистрация Ribbon
│   ├── Commands/                        ← тонкие IExternalCommand
│   │   ├── ArmWallCommand.cs
│   │   ├── DowelCommand.cs
│   │   ├── ConfigEditorCommand.cs
│   │   ├── LinkWallCommand.cs
│   │   ├── UpdateRebarCommand.cs
│   │   ├── DeleteRebarCommand.cs
│   │   ├── CreateViewsCommand.cs
│   │   └── CreateSchedulesCommand.cs
│   ├── Application/                     ← Application Services
│   │   ├── WallReinforcementOrchestrator.cs
│   │   ├── DowelOrchestrator.cs
│   │   ├── ViewGenerationOrchestrator.cs
│   │   ├── ScheduleOrchestrator.cs
│   │   └── ConfigService.cs
│   ├── Domain/                          ← чистый C#, без Revit
│   │   ├── Configs/
│   │   │   ├── RebarConfig.cs
│   │   │   ├── RebarRule.cs
│   │   │   ├── ExternalInternalReinforcementConfig.cs
│   │   │   ├── PerimeterReinforcementConfig.cs
│   │   │   ├── OpeningReinforcementConfig.cs
│   │   │   ├── DowelConfig.cs
│   │   │   ├── ParameterMapping.cs
│   │   │   └── ViewConfig.cs
│   │   ├── Placement/
│   │   │   ├── RebarPlacement.cs        ← намерение разместить стержень
│   │   │   ├── RebarShape.cs
│   │   │   └── HookSpec.cs
│   │   ├── Rules/                       ← движок правил
│   │   │   ├── IRule.cs
│   │   │   ├── ExternalMeshRule.cs
│   │   │   ├── PerimeterEdgeRule.cs
│   │   │   ├── OpeningEdgeRule.cs
│   │   │   ├── LCornerRule.cs
│   │   │   ├── TConnectionRule.cs
│   │   │   ├── DowelRule.cs
│   │   │   └── RuleEngine.cs
│   │   ├── Reports/
│   │   │   ├── JobReport.cs
│   │   │   └── RuleResult.cs
│   │   └── Geometry/
│   │       ├── WallGeometry.cs
│   │       ├── OpeningGeometry.cs
│   │       └── JoinGeometry.cs
│   ├── Adapters/                        ← реализации интерфейсов Domain через Revit API
│   │   ├── RevitDocumentAdapter.cs
│   │   ├── WallRepository.cs
│   │   ├── RebarFactory.cs
│   │   ├── ViewFactory.cs
│   │   ├── ScheduleFactory.cs
│   │   ├── ParameterStore.cs
│   │   ├── ExtensibleStorageStore.cs
│   │   └── SharedParameterLoader.cs
│   ├── UI/                              ← WPF
│   │   ├── Views/
│   │   │   ├── ConfigEditorView.xaml
│   │   │   ├── ArmWallView.xaml
│   │   │   ├── DowelView.xaml
│   │   │   └── LinkWallView.xaml
│   │   ├── ViewModels/
│   │   │   ├── ConfigEditorViewModel.cs
│   │   │   ├── ArmWallViewModel.cs
│   │   │   └── ...
│   │   ├── Controls/                    ← общие WPF-контролы
│   │   ├── Converters/
│   │   └── Resources/                   ← локализация, стили
│   ├── Infrastructure/
│   │   ├── ConfigStorage.cs             ← JSON-IO, миграции
│   │   ├── Logger.cs                    ← Serilog + sinks
│   │   ├── ExternalEventManager.cs      ← обёртка над IExternalEvent
│   │   └── SharedParametersRegistry.cs
│   └── Resources/
│       ├── SharedParameters.txt
│       ├── ribbon.png ... иконки
│       └── DefaultConfigs/              ← заводские конфиги
│           ├── default-mvp.wrsconfig.json
│           └── default-precast.wrsconfig.json
└── RevitPlugin.Tests/                   ← xUnit + Moq
    ├── Domain/
    │   ├── RuleEngineTests.cs
    │   ├── ExternalMeshRuleTests.cs
    │   └── PerimeterEdgeRuleTests.cs
    ├── Configs/
    │   └── ConfigSerializationTests.cs
    └── Application/
        └── WallReinforcementOrchestratorTests.cs
```

---

## 4. Ключевые контракты

### 4.1 Domain — интерфейсы адаптеров
```csharp
namespace WallReinforcement.Domain.Abstractions;

public interface IRevitDocument
{
    string PathName { get; }
    string Title { get; }
}

public interface IWallRepository
{
    IReadOnlyList<WallContext> GetWalls(IEnumerable<long> ids);
    WallContext GetWall(long id);
}

public interface IRebarFactory
{
    long Create(RebarPlacement placement);          // создаёт один Rebar
    void Update(long rebarId, RebarPlacement placement);
    void Delete(long rebarId);
}

public interface IViewFactory
{
    long CreateSection(SectionRequest request);
    long CreateElevation(ElevationRequest request);
}

public interface IScheduleFactory
{
    long CreateOrUpdate(ScheduleTemplate template);
}

public interface IParameterStore
{
    void EnsureSharedParameter(string name, ParameterScope scope);
    void Set(long elementId, string paramName, object value);
}
```

### 4.2 Domain — основные модели
```csharp
public sealed record RebarPlacement(
    string RuleId,
    RebarShape Shape,
    Curve3D[] Segments,        // геометрия стержня
    double Diameter,
    string BarTypeName,
    HookSpec? StartHook,
    HookSpec? EndHook,
    IReadOnlyDictionary<string, object> Parameters,
    long HostWallId);

public sealed record WallContext(
    long Id,
    WallGeometry Geometry,
    IReadOnlyList<OpeningGeometry> Openings,
    IReadOnlyList<JoinGeometry> Joins,
    IReadOnlyDictionary<string, object> WallParameters);

public interface IRule
{
    string RuleId { get; }
    RuleResult Apply(WallContext wall, RebarConfig config, RuleContext ctx);
}

public sealed record RuleResult(
    IReadOnlyList<RebarPlacement> Placements,
    IReadOnlyList<RuleWarning> Warnings);
```

### 4.3 Application — оркестратор
```csharp
public sealed class WallReinforcementOrchestrator
{
    public JobReport Execute(
        IReadOnlyList<long> wallIds,
        string configId,
        OrchestrationOptions options);
}
```

Оркестратор отвечает за:
- Загрузку конфигурации (`ConfigService`).
- Сбор `WallContext` через `IWallRepository`.
- Прогон правил через `RuleEngine` → коллекция `RebarPlacement`.
- Diff с текущим состоянием арматуры стены (по `WRS_Config_Id` + `WRS_Rule_Id`).
- Открытие транзакции, делегирование `IRebarFactory` для create / update / delete.
- Запись Extensible Storage и параметров.
- Сборка `JobReport` и логирование.

---

## 5. Потоки управления (sequence)

### 5.1 Армирование стены — happy path

```
Пользователь    Ribbon       ArmWallCommand   Orchestrator   RuleEngine   RebarFactory   Revit DB
    |              |                |              |             |             |             |
    | click "Arm"  |                |              |             |             |             |
    |------------->|                |              |             |             |             |
    |              |  Execute()     |              |             |             |             |
    |              |--------------->|              |             |             |             |
    |              |                | open UI -->  |             |             |             |
    |              |                | ArmWallView  |             |             |             |
    |              |                | select walls |             |             |             |
    |              |                | + config     |             |             |             |
    |              |                |              |             |             |             |
    |              |                | Execute(...) |             |             |             |
    |              |                |------------->|             |             |             |
    |              |                |              | Load config |             |             |
    |              |                |              | Build       |             |             |
    |              |                |              | WallContext |             |             |
    |              |                |              |------------>|             |             |
    |              |                |              |             | Apply rules |             |
    |              |                |              |<------------|             |             |
    |              |                |              | placements                |             |
    |              |                |              | Begin Tx ------------------------------->|
    |              |                |              | foreach placement                      |
    |              |                |              |------------------------>| Create Rebar ->
    |              |                |              | Set params                              |
    |              |                |              | Commit Tx ------------------------------>|
    |              |                |              | JobReport                               |
    |              |                |<-------------|                                          |
    |              | TaskDialog OK  |                                                         |
    |<-------------|                                                                          |
```

### 5.2 Edit Config (модальный редактор)
```
User → ConfigEditorCommand → ConfigEditorView (WPF dialog) →
       ConfigService.Load/Save → ConfigStorage (JSON file)
```

Редактор конфига **не** открывает Revit-транзакции — он работает только с файлами и in-memory моделью.

---

## 6. Транзакции

### 6.1 Правила
- Одна пользовательская операция = одна транзакция.
- Транзакцию открывает оркестрационный сервис.
- Каждый сервис, требующий записи, принимает `Document`, но не создаёт `Transaction` сам.
- Для UI, обновляющего модель «на лету» (превью), использовать `TransactionGroup` с `Assimilate()` либо вложенные суб-транзакции с `RollBack()` при отмене.

### 6.2 Шаблон
```csharp
public JobReport Execute(...)
{
    var report = new JobReport();
    using var tx = new Transaction(_doc, "WRS: Arm Walls");
    try
    {
        tx.Start();
        // ... вычисления placements
        // ... вызовы IRebarFactory
        // ... запись параметров
        tx.Commit();
        return report;
    }
    catch (Exception ex)
    {
        if (tx.HasStarted()) tx.RollBack();
        _log.Error(ex, "WRS arm walls failed");
        report.AddError(ex);
        return report;
    }
}
```

### 6.3 Idling и External Events
Длительные операции (>5 с) должны:
- Показывать прогресс через `ProgressIndicator`.
- При необходимости разбивать на пакеты (например, по 10 стен) с собственными вложенными транзакциями внутри `TransactionGroup`.

---

## 7. Конфигурация и сериализация

### 7.1 Формат
- JSON, с явным полем `schema_version`.
- Сериализатор: System.Text.Json с `JsonStringEnumConverter`.
- Полная схема — в `CONFIG_SCHEMA.md`.

### 7.2 Хранилище
| Уровень | Путь | Назначение |
|---|---|---|
| Заводские | `RevitPlugin/Resources/DefaultConfigs/*.wrsconfig.json` | Read-only, поставляются с плагином |
| Пользовательские (локальные) | `%APPDATA%\WRSPlugin\configs\*.wrsconfig.json` | Read/write |
| Командные (сетевые) | Путь из настроек (UNC / SharePoint mounted) | Read/write, опционально |
| Проектные (Extensible Storage) | Хранится копия конфига внутри файла проекта | Гарантия воспроизводимости при передаче проекта |

### 7.3 Миграции
- Каждое изменение схемы — новая версия (`schema_version: 1` → `schema_version: 2`).
- В `ConfigStorage` есть цепочка migrator’ов: `ISchemaMigration` для каждой версии.
- При загрузке файла версии N < current — последовательно применяются миграции.

---

## 8. Extensible Storage

### Schema `WRS.WallLink`
| Поле | Тип | Описание |
|---|---|---|
| `ConfigId` | `string` (GUID) | Идентификатор конфига |
| `ConfigVersion` | `int` | Версия конфига на момент применения |
| `RuleHash` | `string` | Хеш правил для быстрого diff |
| `RunStamp` | `long` (DateTime ticks) | Когда применён |
| `RuleIds` | `string[]` | Какие правила сработали |

Schema создаётся под фиксированным GUID. На каждой стене — один экземпляр.

### Schema `WRS.DowelLink`
| Поле | Тип | Описание |
|---|---|---|
| `SourceElementId` | `long` | ElementId источника-конструкции |
| `AnchorIntoSource` | `double` | Анкеровка в источник, мм |
| `LapIntoWall` | `double` | Перехлёст в стене, мм |
| `Form` | `string` | `Straight` / `LShape` |

---

## 9. Логирование и диагностика

- **Serilog**, sinks: File (`%APPDATA%\WRSPlugin\logs\wrs-yyyyMMdd.log`), Debug.
- Уровни: `Information` — пользовательские операции; `Debug` — детали правил; `Warning` — пропуски, missing params; `Error` — исключения; `Fatal` — критические сбои.
- В каждой записи — `JobId` (GUID) для трассировки одной операции.
- `JobReport` — структурированный объект с `Created`, `Updated`, `Deleted`, `Skipped`, `Errors`, `Warnings`. Может выгружаться в JSON для отладки.

---

## 10. UI и MVVM

- Каждый диалог — WPF Window.
- ViewModel — POCO, с `INotifyPropertyChanged` (использовать `CommunityToolkit.Mvvm` для генерации, source generators совместимы с .NET 8).
- Команды — `RelayCommand` / `AsyncRelayCommand`.
- Связь с Revit API из ViewModel — только через `IExternalEvent`-обёртку (`ExternalEventManager`), чтобы не блокировать UI-поток и соблюдать однопоточность Revit API.

---

## 11. Многопоточность и External Events

- Revit API не потокобезопасно — все обращения из главного потока.
- `WallReinforcementOrchestrator` запускается в External Event Handler.
- Подсчёты, не трогающие Revit DB (валидация конфигов, чтение JSON, превью геометрии), могут выполняться в фоне.

```csharp
public sealed class WallReinforcementExternalEvent : IExternalEventHandler
{
    private readonly WallReinforcementOrchestrator _orchestrator;
    private readonly Func<OrchestrationOptions> _optionsProvider;

    public void Execute(UIApplication app)
    {
        var options = _optionsProvider();
        var report = _orchestrator.Execute(options.WallIds, options.ConfigId, options);
        ResultBus.Publish(report);
    }
    public string GetName() => "WRS.ArmWalls";
}
```

---

## 12. Версионирование Revit API

- Целевой `TargetFramework` — `net8.0-windows`.
- Условная компиляция: `#if REVIT2025` / `#if REVIT2024`.
- Конфигурации сборки:
  - `Debug2025` / `Release2025` — ссылки на `RevitAPI2025.dll`.
  - (опц.) `Debug2024` / `Release2024` — ссылки на `RevitAPI2024.dll`.
- Каталог поставки: `out/Revit2025/`, `out/Revit2024/`.

---

## 13. Безопасность и отказоустойчивость

- Все пути к конфигам и логам проверяются на существование и права записи при старте.
- Файлы конфигов читаются с защитой от `JsonException` — при ошибке отображается понятное сообщение и предложение восстановить из бэкапа.
- Backup конфига перед сохранением: `*.wrsconfig.json` → `*.wrsconfig.json.bak`.
- Корректная обработка `OperationCanceledException` (отмена пользователем в прогрессбаре).

---

## 14. Стратегия тестирования (кратко)

| Слой | Подход | Инструменты |
|---|---|---|
| Domain (правила, геометрия) | Unit, чистый C# | xUnit, FluentAssertions |
| Конфиги (сериализация, миграция) | Unit | xUnit, snapshot-tests |
| Адаптеры Revit API | Интеграционные через `RevitTestFramework` или `revit-mock` | xUnit + RTF |
| End-to-end | Ручные на тестовых проектах + сценарные | TestProject.rvt в `/tests/fixtures` |

См. также `agents/tester.md` и будущий `docs/TESTING.md`.

---

## 15. Связанные документы

- [SPEC.md](SPEC.md) — функциональная спецификация.
- [MODULES.md](MODULES.md) — детализация по модулям.
- [CONFIG_SCHEMA.md](CONFIG_SCHEMA.md) — JSON-схема конфигурации.
- [UI_FLOWS.md](UI_FLOWS.md) — UX-сценарии.
- [PARAMETERS.md](PARAMETERS.md) — словарь параметров.
- [ROADMAP.md](ROADMAP.md) — дорожная карта.
