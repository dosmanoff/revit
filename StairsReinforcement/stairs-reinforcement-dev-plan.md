# StairsReinforcement — план разработки (PR-роадмап)

Revit 2025 · .NET 8 · C# · ACI 318-19 · Imperial · без сейсмики (по умолчанию).
Спецификация (*что* делаем) — в `stairs-reinforcement-spec.md`; этот файл — *в каком порядке*.

## Стратегия
- Дробим на PR по **200–500 строк диффа**. Каждый PR: компилируется в Release (как CI —
  `dotnet build` по каждому `*.csproj`), загружается в Revit, проходит короткий ручной smoke-тест.
- Каждый PR — отдельная ветка `claude/stairs-rebar-NN-<slug>`, мёрж в `main` отдельным
  merge-коммитом.
- Smoke-модель: выделенный `test/stairs-smoke.rvt` с двумя лестницами — одна нативная
  (монолитная Stairs), одна «перекрытиями» (наклонный Floor-марш + Floor-площадки).
- **Юнит-тесты — только для чистой логики без Revit** (парсинг конфига/CSV, slope-frame,
  раскладка стержней, split-at-max-length + lap). Код с `Rebar.Create*` тестируется в Revit.
- Порядок фаз — чтобы **AI-агент получил свой JSON как можно раньше** (экспорт → генерация → доки).

## Решения, на которых стоит план
- **Две модели лестниц, один доменный объект.** `StairAssembly = [FlightComponent | LandingComponent]`.
  Источник — адаптер: либо нативная `Stairs` (`GetStairsRuns`/`GetStairsLandings`), либо набор
  выделенных `Floor` (наклонные = марши, горизонтальные = площадки).
- **Хост арматуры решается per-component.** Пытаемся `RebarHostData.GetRebarHostData(stairs)` →
  `IsValidHost()`; если нативная лестница не хостит арматуру — хостим на `Floor` (floor-модель) или
  репортим понятный skip. Это главный технический риск — проверяем в PR-02/PR-07.
- **Каждый набор стержней — `BarSetSpec`** (тип, шаг/кол-во, защитный слой, анкеровка по концам,
  вкл/выкл). Конфиг уже целиком в PR-01.
- **Наклонные стержни:** нормаль для `CreateFromCurves` = `Z × runDirHoriz` (мир), не `BasisZ`.
- **Идемпотентность по тегу** `STR:{config}:{stairId}:{layer}` в `Comments`.

---

## Phase 0 — Скелет
### PR-01 — Проект, лента, конфиг, плумбинг ✅ ГОТОВО
3 проекта (`StairsReinforcement`, `.Geometry`, `.Tests`); `App.cs` (idempotent вкладка
Smart Tools + панель Stairs Reinforcement, 2 кнопки-стаба); `StairsSelectionFilter`
(Stairs + структурные Floor); `.addin` (новый ClientId). Порты: `Length`, `ConfigLoader`,
`FolderStorage` (**новый GUID схемы**), `UnitConv`, `RebarFactory` (+`CreateSet`),
`ExistingRebarCleaner` (тег `STR:`), `StairLayer`. Полный `StairsReinforcementConfig` +
`BarSetSpec`. Гео-примитивы `Pt2`/`Pt3`/`BarSplitter` + 4 теста. `samples/default.json`.
*Acceptance:* все 3 проекта собираются в Release, 4 теста зелёные. *Smoke:* кнопки видны,
выбор элемента работает.

---

## Phase 1 — JSON-экспорт (вход для агента)
### PR-02 — Доменная модель + геометрия компонентов
`Domain/StairAssembly.cs`, `FlightComponent.cs`, `LandingComponent.cs`; адаптеры
`Domain/Source/StairsElementSource.cs` (нативная Stairs) и `FloorStairSource.cs` (модель
перекрытиями). Slope-frame в `.Geometry` (`FlightFrame`: оси U=уклон, W=ширина, N=нормаль
пояса; tread/riser; run/rise). Чистая математика + тесты.
*Acceptance:* по выбранной лестнице получаем список flights/landings с корректными габаритами,
уклоном, шириной. *Smoke:* лог геометрии в TaskDialog.

### PR-03 — Опоры и контекст
`Domain/StairContext.cs`: нижняя/верхняя опора каждого марша и опирания площадок
(slab/beam/wall/foundation) через `BoundingBoxIntersectsFilter`; контур и проёмы площадок.
*Acceptance:* для smoke-лестниц определяются обе опоры марша и опоры площадок.

### PR-04 — Сборка и запись JSON
`Export/StairsDump.cs` (POCO + `JsonOptions`, snake_case), `Export/StairsDumpBuilder.cs`,
реальный `ExportStairsCommand` (выбор → дамп → SaveFileDialog → итоговый TaskDialog). Коммит
реального примера дампа `samples/example_stairs.json`.
*Acceptance:* валидный JSON по smoke-модели; per-element warnings не роняют батч.

### PR-05 — Документация агента
`stairs-dump-schema.md`, `agent-config-guide.md`, `stairs-assignments-csv-guide.md`.
*Acceptance:* по дампу агент вручную собирает корректный CSV (проверка на примере).

---

## Phase 2 — Генерация: марши (ядро)
### PR-06 — CSV-загрузчик
`Config/AssignmentCsv.cs` + `Domain/AssignmentTable.cs` (одна строка на Mark, разрежённо;
`Expected*`-валидаторы). Примеры CSV. Юнит-тесты парсинга.

### PR-07 ⭐ — Продольная нижняя арматура марша + оркестратор
`Engine/FlightLongitudinalBuilder.cs` (нижняя рабочая по уклону, анкеровка по концам,
split/lap), `Engine/StairsReinforcer.cs` (TransactionGroup на прогон, Transaction на лестницу,
clean→build→commit, dry-run), `Domain/RunResult.cs`, реальный `GenerateStairsRebarCommand`,
`UI/StairsRebarGenDialog.cs` (code-only WPF: Same-for-all JSON / From-CSV, dry-run).
*Acceptance:* в smoke-модели на марше появляется нижняя рабочая арматура с анкеровкой;
повторный прогон заменяет её. *Smoke:* и нативная лестница, и floor-марш.

### PR-08 — Распределительная + верхняя арматура марша
`Engine/FlightDistributionBuilder.cs` (низ/верх поперечная, `CreateSet`), верхняя рабочая
по `TopMode` (Continuous/OverSupports/EndsOnly, `TopSupportExtent`).

---

## Phase 3 — Площадки, стыки, ступени
### PR-09 — Сетки площадок
`Engine/LandingMatBuilder.cs`: низ/верх X/Y (режим `Bars` со split/lap) + режим `AreaSystem`
(нативная `AreaReinforcement`).

### PR-10 — Гибы/косынки на переломе
`Engine/KneeBarBuilder.cs`: `ContinuousBent` / `LappedHairpin` / `CrossedAtReentrant` в зоне
марш↔площадка (учёт входящего угла на верхней грани).

### PR-11 — Выпуски/стартеры в опоры
`Engine/StarterBarBuilder.cs`: `Straight`/`L`, хост `Auto/SlabBelow/Beam/Wall/Foundation`,
embed + projection, нахлёст с рабочей.

### PR-12 — Армирование ступеней
`Engine/StepBarBuilder.cs`: `PerStepLBar` (Г-стержень на ступень) / `NosingBar` (по носику).

---

## Phase 4 — Stair Views (документация)
### PR-13 — Разрезы и планы лестниц
`Config/StairViewsConfig.cs` (+ store, **новый GUID**), `Engine/StairViewsEngine.cs`,
`Commands/StairViewsCommand.cs`: продольный разрез + план на лестницу, спецификация арматуры
(фильтр по тегу слоя).

### PR-14 — Спецификации и листы
`Engine/StairScheduleBuilder.cs`, `Engine/StairSheetBuilder.cs`. Замыкает конвейер:
Export → агент → CSV → Generate → Stair Views.

---

## Phase 5 — Ниши (отложено)
Винтовые/криволинейные и забежные ступени; «ножничные» (scissor) и многомаршевые сборки;
муфты/каплеры; дедуп нахлёстов соседних компонентов; импорт/экспорт настроек; объединение
кнопок плагинов в один аддин; уточнение хостинга арматуры на нативных Stairs.

---

## Build/test
`dotnet build -c Release` по каждому `*.csproj` (как CI). `dotnet test` для `.Tests` локально.
Ручной smoke — в `test/stairs-smoke.rvt`.

## Открытые вопросы
- Хостинг арматуры на нативной `Stairs` в 2025 — подтвердить на smoke-модели (PR-02). Если
  невалиден — стратегия фолбэка на структурный элемент/скрытый Floor-«пояс».
- Деталь верхнего входящего угла (re-entrant) — какой именно стандартный узел по умолчанию.

## Вне области (явно)
Расчёт арматуры по нагрузкам; винтовые лестницы (Phase 5); немонолитные/сборные марши;
автогенерация опалубки.
