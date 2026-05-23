# Дорожная карта

> Сопровождает: [SPEC.md](SPEC.md), [MODULES.md](MODULES.md).
> Этапы M1–M5 ведут от пустого скелета к v1.0.

---

## Принципы планирования

- **Вертикальные срезы** на каждом этапе: пользовательский сценарий от UI до Revit DB работает end-to-end, даже если в нём только 1 правило.
- **MVP — это не урезанная архитектура.** Архитектурные слои (Domain / Adapters / Application) — на месте с M1. Сокращается набор правил, а не структура.
- **Конфиг — раньше кода.** Сначала JSON-схема и сериализация, потом правила. Это позволяет писать тесты на правилах с фикстурами без Revit.
- **Тесты — параллельно фичам.** На каждом этапе появляются unit-тесты Domain + 1–2 интеграционных через RTF.

---

## M1. Скелет и MVP-сетки (2–3 недели)

**Цель.** End-to-end сценарий: «выбрал стену → запустил Arm Walls → получил основные сетки + базовые параметры».

**Скоуп.**
- Проектная структура (см. ARCHITECTURE.md §3): csproj, addin, Application.cs, Ribbon с одной кнопкой `Arm Walls`.
- Domain: `RebarConfig`, `WallContext`, `RebarPlacement`, `RuleEngine`, `ExternalMeshRule`, `InternalMeshRule`.
- Adapters: `WallRepository`, `RebarFactory` (только прямые стержни), `ParameterStore`.
- Infrastructure: `ConfigStorage` (read JSON), `Logger` (Serilog к файлу).
- UI: минимальный диалог `ArmWallView` с выбором конфига и кнопкой Apply.
- Конфиг: один заводский `default-mvp.wrsconfig.json` (только `external_reinforcement` и `internal_reinforcement`).
- Параметры: `WRS_Config_Id`, `WRS_Rule_Id`, `WRS_Bar_Role`, `WRS_Wall_Mark`, `WRS_Run_Stamp`, `WRS_Job_Id` (создаются автоматически).
- Тесты: unit-тесты `ExternalMeshRule` (3–5 сценариев), `ConfigSerializationTests`.

**Definition of done.**
- На тестовом проекте `tests/fixtures/M1.rvt` стена 5×3 м получает 25 вертикальных + 14 горизонтальных стержней, все имеют корректные параметры.
- `dotnet build` и `dotnet test` зелёные.
- CI workflow `build.yml` собирает плагин под Revit 2025.

**Риски.**
- Особенности `Rebar.CreateFromCurves` в Revit 2025 — нужны эксперименты с типами хуков и shape recognition. Митигация: первый итерационный спайк в первую неделю.

---

## M2. Периметр, проёмы, выпуски (2–3 недели)

**Цель.** Добавить второй большой набор правил + первый версионируемый поток «выпусков».

**Скоуп.**
- Domain: `PerimeterEdgeRule`, `OpeningEdgeRule` (только Edge, без Diagonal/Stirrup), `DowelRule` (только Straight).
- Adapters: расширение `RebarFactory` под Г-образные стержни (через `RebarShape` lookup) — нужно для выпусков.
- Команды: `DowelCommand`, `DeleteRebarCommand`.
- UI: `DowelView`, доработка `ArmWallView` (checkboxes по разделам).
- Конфиг: добавлены секции `perimeter`, `opening`, `dowels` (только straight + auto-detect фундамента).
- `SourceDetector` — поиск источников среди `Foundations` и `Floors`.
- Extensible Storage: `WRS.WallLink`, `WRS.DowelLink`.
- Тесты: `PerimeterEdgeRuleTests`, `OpeningEdgeRuleTests`, `DowelRuleTests` (фикстуры геометрии).

**Definition of done.**
- На тестовом проекте `M2.rvt` (стена + фундамент + проём) — генерируются: сетки, периметральные edge, edge проёма, выпуски из фундамента.
- Запуск повторно идемпотентен (не дублирует стержни).

**Риски.**
- Изгиб стержней при коллизии с проёмом — нетривиальная геометрия. Митигация: в M2 — bend skip с warning; полноценный bend — в M3.

---

## M3. Виды, размеры, теги, спецификации (3 недели)

**Цель.** Закрыть модули M4 (Smart Views + Smart Dimensions + Smart Tags) и M5 на уровне MVP.

**Скоуп.**
- Domain: `ViewSpec`, `DimensionSpec`, `TagSpec`, `ScheduleSpec` + `SmartNumberingService` (партиция → Mark).
- Adapters: `ViewFactory` (Section + Elevation), `DimensionFactory` (Smart Dimensions), `TagFactory` (Smart Tags), `ScheduleFactory`.
- Application: `ViewGenerationOrchestrator`, `ScheduleOrchestrator`.
- Команды: `CreateViewsCommand`, `CreateSchedulesCommand`, `RenumberRebarCommand` (Smart Numbering re-run).
- UI: `CreateViewsView` (чекбоксы: Section, Elevation, Dimensions, Tags), `CreateSchedulesView`.
- Конфиг: секции `views.section_per_wall`, `views.elevation_per_wall`, `views.dimensions`, `views.tags`, `schedules`.
- Шаблон вида `WRS_StructuralSection`, `WRS_WallElevation` — поставляются в `Resources/`.
- Шаблоны тегов: `WRS_RebarTag_Vertical.rfa`, `WRS_RebarTag_Horizontal.rfa` (placeholder в Resources).
- Один шаблон спецификации: `rebar_by_wall_mark`.
- Доработать `parameter_mapping` для view-параметров.
- Extensible Storage: `WRS.DimensionLink`, `WRS.TagLink` (для идемпотентности).

**Definition of done.**
- На `M3.rvt`: после Arm Walls + Create Views + Create Schedules в проекте появляются:
  - вид сечения и вид развёртки;
  - на развёртке проставлены габариты стены + цепочка размеров шага вертикальных стержней;
  - на сечении стоят теги с маркой и количеством стержней;
  - спецификация с группировкой по `WRS_Wall_Mark`, корректные `Mark` через Smart Numbering.
- Повторный запуск Create Views — не дублирует размеры/теги (фильтр по `WRS.DimensionLink`).
- Bar bend in opening реализован (доработка из M2).

---

## M4. Углы, T-узлы, окантовки полностью (2–3 недели)

**Цель.** Закрыть оставшиеся правила M1.

**Скоуп.**
- Domain: `LCornerRule`, `TConnectionRule`, `OpeningDiagonalRule`, `OpeningUStirrupsRule`, `PerimeterDiagonalRule`, `PerimeterUStirrupsRule`, `AdditionalEdgeRule`, `AdditionalFaceRule`.
- Расширение `JoinGeometry` — определение L и T узлов по геометрии стен.
- Полная поддержка `Bar1..Bar5` (специальная форма для T).
- Конфиг: секции `l_corner`, `t_connection`, `additional_edge`, `additional_face`, и подсекции `diagonal_rebar`, `u_o_stirrups` в `opening` и `perimeter`.
- Тесты: интеграционные с реальными join-сценариями.

**Definition of done.**
- На `M4.rvt` (L+T узлы, проём с U/O-хомутами, прямые рёбра) — все ожидаемые стержни на месте.

**Риски.**
- Сложность распознавания «inner» vs «outer» угла. Митигация: спайк в начале этапа.

---

## M5. Полировка, Smart Sheets, Partial Update, релиз v1.0 (2–3 недели)

**Цель.** Готовый к продакшену продукт.

**Скоуп.**
- Partial Update: API + UI (выбор правил для пересборки).
- Modify Rebar: пометка `WRS_Modified = Yes` и защита от пересоздания (распространяется и на размеры / теги).
- Полный набор спецификаций (`dowels_only`, `steel_usage`, `pivot_by_level`).
- Detail Section узлов (FoundationJoint, LCorner, TConnection, Opening).
- 3D Check View.
- **Smart Sheets** — авто-раскладка созданных видов на листы по шаблону (`sheets.layout` в конфиге).
- **Полные Smart Dimensions** — проёмы, защитные слои, узлы.
- **Полные Smart Tags** — multi-leader для групп стержней, теги выпусков и узловых стержней.
- **Bar laps** — стыки стержней по высоте для стен > 12 м (анкеровка по нормативу).
- Импорт/экспорт конфигов из/в файл.
- JSON Schema валидация в редакторе.
- Превью правил (SVG) в редакторе.
- Локализация ru-RU полная, en-US частичная.
- Документация для пользователя: `docs/USER_GUIDE.md` + screenshots.
- CI: `release.yml` — выпуск релиза v1.0.

**Definition of done.**
- v1.0 на GitHub Releases (zip + addin).
- USER_GUIDE с 8+ скриншотами.
- 60%+ покрытие Domain unit-тестами.
- В отчёте `JobReport` < 1% Warning на тестовых сценариях.
- На демо-проекте сценарий «pick walls → Arm Walls → Create Views → Create Schedules → Create Sheets» проходит < 60 секунд для 10 стен.

---

## Дополнительные риски и митигации

| Риск | Вероятность | Влияние | Митигация |
|---|---|---|---|
| Revit 2025 API меняется в hot-fix | низкая | высокое | Pin версии RevitAPI; CI собирает на конкретном билде |
| Производительность на проектах с 1000+ стен | средняя | среднее | Прогресс-бар, батчинг, профилирование на этапе M4 |
| Сложность L-Corner / T-узлов | высокая | среднее | Раздельные спайки в M4, отложенный fallback («skip + warning») |
| Конфликт с Shared Parameters пользователя | низкая | среднее | `parameter_mapping`, fallback к существующим параметрам |
| Геометрия `RebarShape` не подходит для нестандартных форм | средняя | высокое | Заранее протестировать `Rebar.CreateFromCurves` и `RebarShape` API на спайке M1; задокументировать ограничения |

---

## Зависимости между этапами

```
M1 ──► M2 ──► M3 ──► M4 ──► M5
        │       │
        └──► (Wall Link Storage)
                │
                └──► (используется в M3, M4 для diff)
```

M3 может стартовать параллельно с концом M2 (после стабилизации `WRS.WallLink`).

---

## Критерии готовности «MVP»

MVP = успешное завершение M1 + M2 + M3.

**По функциональности.** End-to-end:
1. Открыт тестовый проект.
2. Стене присвоен Mark.
3. Запущен Arm Walls с дефолтным конфигом → сетки.
4. Запущен Dowels → выпуски из фундамента.
5. Запущен Create Views → сечение и развёртка.
6. Запущен Create Schedules → ведомость арматуры с группировкой.
7. Параметры стены и арматуры корректны.

**По качеству.**
- CI зелёный.
- Логи без `Error`.
- Undo возвращает проект в исходное состояние.

---

## Связанные документы

- [SPEC.md](SPEC.md) — что считается MVP-scope.
- [MODULES.md](MODULES.md) — как разделены модули.
- [ARCHITECTURE.md](ARCHITECTURE.md) — что строится в M1.
