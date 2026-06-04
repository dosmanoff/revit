# SlabReinforcement — план разработки

> Сопровождает [slab-reinforcement-spec.md](slab-reinforcement-spec.md). Спека — **что**
> строим; этот документ — **в каком порядке** и какими кусками.

---

## Стратегия

* **Дробим на PR по 200–500 строк диффа.** Каждый PR компилируется, грузится в Revit
  и проходит короткий smoke-тест. Не сливаем всё разом.
* **Каждый PR — отдельная ветка `claude/slab-rebar-NN-<slug>`**, мерж в `main`
  отдельным merge-commit'ом (как мерджились `WallReinforcement`/`ColumnReinforcement`).
* **Ручной smoke-тест в Revit на каждый PR.** Тестовая модель — `test/slabs-smoke.rvt`
  (создаём в PR-01): плита 20′×20′×10″ с одним прямоугольным отверстием, свободным
  торцом, балкой по одной грани и колонной снизу по центру; плита-сосед по другой
  грани. Чек-лист — короткий, в теле PR.
* **Юнит-тесты — только для чистой логики без Revit API:** парсинг конфига/CSV,
  раскладка стержней по полю, **резка по макс. длине + нахлёст**, клиппинг рельса по
  отверстию, локальный базис. Engine с `Rebar.Create*`/`AreaReinforcement.Create` —
  тестируем в Revit.
* **Порядок фаз выбран так, чтобы AI-агент получил вход (JSON) как можно раньше:**
  сначала экспорт (Phase 1), потом генерация (Phase 2–3), потом документация (Phase 4).

---

## Решения, под которые сделан план (2026-06-01)

1. **Отдельный плагин `SlabReinforcement/`** в монорепо. Существующий `SlabRebar`
   (классификатор готовой арматуры) не трогаем. Все кнопки — на табе **Smart Tools**,
   панель `Slab Reinforcement`.
2. **Поле плиты — два режима представления на выбор:** отдельные `Rebar` (резка по
   макс. длине + нахлёсты) **и** `AreaReinforcement` (нативная система). Выбор —
   поле `FieldMode` в конфиге/CSV. Bars — дефолт (точный контроль длины/перехлёста,
   ровно под требование пользователя).
3. **Слои — независимые маркеры.** Генерация пишет слой в тег `Comments`
   `SR:{config}:{slabId}:{layer}`; `Slab Views` фильтрует по нему. **Не** завязываемся
   на параметр классификатора `SlabRebar` («T/B SLAB»).
4. **ACI 318-19, Imperial, non-seismic, strict bar-type lookup** — как у всех плагинов
   репо (memory `repo-conventions`). Плагин геометрию строит, **не считает** требуемую
   площадь арматуры — это вход от агента/инженера.

---

## Phase 0 — Скелет

### PR-01 — Проект, лента, 3 кнопки-заглушки ⏱ ~2-3 ч
* `SlabReinforcement.csproj` (копия `WallReinforcement.csproj`; правка
  `AssemblyName`/`RootNamespace`).
* `App.cs` (`IExternalApplication`): идемпотентный таб **Smart Tools**, панель
  `Slab Reinforcement`, три push-кнопки → команды `ExportSlabsCommand`,
  `GenerateSlabRebarCommand`, `SlabViewsCommand` (пока `TaskDialog.Show(...)`).
* `Commands/SlabSelectionFilter.cs` — `ISelectionFilter` на `OST_Floors` (+ опц.
  только Structural).
* `SlabReinforcement.addin` — новый GUID.
* `test/slabs-smoke.rvt` (создать вручную, закоммитить). `samples/` со заглушкой
  `default.json`.

**Acceptance:** компилится, грузится в Revit, три кнопки кликабельны.

---

## Phase 1 — JSON-экспорт (задача №1)

### PR-02 — Геометрия плиты ⏱ ~5-7 ч
* `Domain/SlabGeometry.cs` — по `Floor`: толщина, top/bottom elevation, локальный
  базис (longest boundary edge → X), внешний контур (`CurveLoop`), внутренние
  контуры-отверстия из эскиза, bbox, площадь, плоскости слоёв с учётом cover.
* `Engine/UnitConv.cs` (`InToFt`/`MmToFt`/`FtToIn`), `Config/Length.cs` — копии.
* **Юнит-тесты:** базис из набора кромок; выбор внешнего loop при нескольких; area.

**Smoke:** временная отладочная команда печатает в TaskDialog толщину/базис/площадь.

### PR-03 — Контекст и отверстия ⏱ ~5-7 ч
* `Domain/SlabContext.cs` — примыкания по сегментам границы (free/beam/wall/slab),
  опоры снизу (колонны/стены/балки через `BoundingBoxIntersectsFilter`), соседние
  плиты, slab above/below. Детект свободных торцов.
* `Domain/SlabOpenings.cs` — `Opening` (`OST_*Opening`/`ShaftOpening`), by-family
  проёмы, вырезы эскиза; порог «нужна обвязка».

**Smoke:** отладочный вывод: N торцов (из них free), N отверстий, опоры снизу.

### PR-04 — Сборка и запись JSON ⏱ ~4-6 ч
* `Export/SlabDump.cs` (POCO схемы §5 спеки) + `Export/SlabDumpBuilder.cs`
  (assemble: levels, floor_types_in_use, available bar/hook types, per-slab, hints).
* `Commands/ExportSlabsCommand.cs` — выбор плит, save-dialog, запись
  `<title>_slabs.json`, сводка. `System.Text.Json`, indented.
* `samples/<test>_slabs.json` — реальный дамп тестовой модели, закоммитить.

**Smoke:** Export на тестовой модели → валидный JSON со всеми секциями.
**Acceptance:** JSON открывается, поля заполнены, координаты осмысленны.

### PR-05 — Документация агента ⏱ ~3-4 ч
* `slab-dump-schema.md` — поле-в-поле описание дампа.
* `agent-config-guide.md` (slab-редакция) — процедура решения: из `comments`/задания
  → сетки; `hints.free_edge_indices` → П-образные; `hints.supports` → зоны усиления;
  `openings_need_trim` → обвязка; strict-имена стержней.
* `slab-assignments-csv-guide.md` — поле-в-поле CSV + `slab-zones.csv`.

**Acceptance:** по гайдам можно вручную собрать корректный CSV из дампа.

---

## Phase 2 — Генерация поля (ядро задачи №3)

### PR-06 — Конфиг, фабрика, strict lookup ⏱ ~4-5 ч
* `Config/SlabReinforcementConfig.cs` (POCO §9), `Config/ConfigLoader.cs`,
  `Config/FolderStorage.cs` (новые GUID), `Engine/RebarFactory.cs`,
  `Engine/ExistingRebarCleaner.cs` (тег `SR:`, §11) — копии из `WallReinforcement` с
  правкой namespace.
* `samples/default.json` — нижняя+верхняя сетка `#5@12"`, `MaxBarLength=40'`,
  `LapFactor=40`.
* **Юнит-тесты:** загрузка конфига, `Length.ToFeet`.

### PR-07 — CSV-загрузчик ⏱ ~4-5 ч
* `Config/AssignmentCsv.cs` + `Domain/AssignmentTable.cs` (ByMark, ExpectedByMark,
  Issues) + парс `slab-zones.csv` → `List<SupportZone>`.
* `samples/slab-assignments-basic.csv`, `samples/slab-zones-demo.csv`.
* **Юнит-тесты:** разрежённый парс, дубликаты Mark, feet-inches, enum/bool, зоны.

### PR-08 — Полевые стержни + резка/нахлёст + оркестратор ⏱ ~7-9 ч ⭐
* `Engine/FieldBarBuilder.cs` (FieldMode=Bars): рельсы X/Y на нижней/верхней
  плоскостях, клип по `boundary − openings`; **резка рельса длиннее `MaxBarLength` на
  сегменты с перекрытием `LapLength`, со смещением стыков (`LapStagger`)**;
  `Rebar.CreateFromCurves` per стержень; тег слоя.
* `Engine/SlabReinforcer.cs` — оркестратор (TransactionGroup/Transaction, dry-run,
  clean-before-place), `Domain/RunResult.cs`.
* `Commands/GenerateSlabRebarCommand.cs` + `UI/SlabRebarGenDialog.cs`
  (Same-for-all + From-CSV, поле Max bar length, dry-run) + `UI/ResultsDialog.cs`.
* **Юнит-тесты (ключевые):** длина→число сегментов, длины сегментов и нахлёстов,
  стаггер, клиппинг по отверстию.

**Smoke:** генерация на тестовой плите → нижняя+верхняя сетки, длинные рельсы порезаны
с нахлёстами, видно в 3D/разрезе; повторный запуск заменяет; dry-run ничего не создаёт.
**Acceptance:** сетки корректны, макс. длина и нахлёст соблюдены, отчёт читабелен.

---

## Phase 3 — Торцы, отверстия, area-режим, зоны

### PR-09 — П-образные по свободным торцам ⏱ ~4-6 ч
* `Engine/EdgeUBarBuilder.cs` — на торцах из `EdgeUBarSelector` (по умолч. free):
  3-сегментная кривая (верхняя полка → вертикаль → нижняя полка), шаг вдоль торца;
  тег `EdgeU`.

### PR-10 — Обвязка отверстий ⏱ ~5-7 ч
* `Engine/OpeningTrimBuilder.cs` — доп. прямые по граням отверстия (верх/низ),
  П-образные обвязки грани, диагонали в углах; тег `OpeningTrim`.

### PR-11 — Режим AreaReinforcement ⏱ ~5-7 ч
* `Engine/FieldMeshBuilder.cs` (FieldMode=AreaSystem): `AreaReinforcement.Create`
  на Floor, активны верх/низ, шаг/типы через `REBAR_SYSTEM_*` (образец
  `WallReinforcement.FaceMeshBuilder`); тег. Переключатель `FieldMode` в движке.

**Smoke:** та же плита в двух режимах; AreaSystem даёт нативную арматурную систему.

### PR-12 — Зоны усиления + анкеровка торцов ⏱ ~6-8 ч
* `Engine/SupportZoneBuilder.cs` — доп. верх/низ стержни в зонах над опорами
  (`slab-zones.csv`): полоса по `SupportMark`+`StripWidth` / BBox / Polygon; тег
  `Support`.
* `Engine/EdgeAnchorBuilder.cs` — заводка концов полевых стержней в балки/стены
  (крюк/L/`EdgeAnchorLen`).

**Acceptance:** над колонной появляется верхнее усиление в заданной полосе; концы
заведены по торцам.

---

## Phase 4 — Документация: Slab Views (задача №4)

### PR-13 — Виды слоёв Layer 1–4 ⏱ ~6-8 ч
* `Config/SlabViewsConfig.cs` + `Config/SlabViewsConfigStore.cs` (ExtensibleStorage,
  новый GUID) — образец `ColumnViewsConfig*`.
* `Engine/SlabViewsEngine.cs` — на плиту 4 `ViewPlan` (Layer 1=BottomX … 4=TopY),
  view-range под слой, crop=bbox+padding, изоляция слоя по тегу `SR:…:{layer}`
  (foreign — hide/halftone); токен-имена `{Mark} - Layer {N} {Layer}`.
* `Commands/SlabViewsCommand.cs` + `UI/SlabViewsDialog.cs` (наименования, масштабы,
  detail level, foreign-rebar, тогглы).

**Smoke:** на армированной плите создаются 4 плана; в каждом виден только свой слой.

### PR-14 — Спецификации и листы ⏱ ~6-8 ч
* `Engine/SlabScheduleBuilder.cs` — `ViewSchedule` (OST_Rebar) по host-плите/слою,
  группировка по форме/длине; опц. из шаблона (`Duplicate`).
* `Engine/SlabSheetBuilder.cs` — `ViewSheet.Create` + `Viewport.Create` (2×2 для 4
  планов) + `ScheduleSheetInstance.Create` сбоку; title block; токен-имена; дубликаты
  (Skip/Overwrite/AppendSuffix).

**Acceptance:** на плиту — лист с 4 планами слоёв и спецификацией; имена/листы
уникальны.

**После PR-14 конвейер замкнут:** Export → (агент) → CSV → Generate → Slab Views.

---

## Phase 5 — Ниши (после feedback, отложено)

* Дуги/кривые торцы и отверстия (полноценные `Arc`, не хорда).
* Mat foundation: нижняя сетка + усиление под колонны/стены, питы/утолщения.
* Bent/cranked анкеровка, couplers, дюбели между уровнями.
* Дедупликация перекрытий поля и зон усиления.
* Экспорт/импорт настроек, мульти-проектные пресеты.
* Миграция кнопок существующих плагинов под единый таб/панель (отдельный PR).

---

## Вне области (явно)

* Расчёт требуемой арматуры по нагрузкам/моментам — делает агент/инженер, не плагин.
* Сейсмическое детектирование/детейлинг.
* Преднапряжённые плиты, пустотные плиты, сборные.
* Автоматическая простановка размеров/тегов в видах (только изоляция слоёв в Phase 4;
  теги — кандидат в Phase 5).

---

## Открытые вопросы (уточнить по ходу)

1. **Источник спеки армирования** в `comments` плиты — формат (`B: #5@12 EW; T: …`)?
   Подтвердить конвенцию проекта перед массовой генерацией (как у колонн).
2. **Зоны усиления**: отдельный `slab-zones.csv` (предложено) vs JSON-в-ячейке vs
   геометрия из самой модели (выделенные регионы). Стартуем с CSV-зон.
3. **Анкеровка по умолчанию** в балки/стены — прямая на `EdgeAnchorLen` или крюк?
   Дефолт = прямая, ACI development length как ориентир.
4. **Max bar length** — глобально на прогон (поле в диалоге) и/или per-Mark в CSV?
   План: оба, CSV переопределяет диалог.
5. **`slabs-smoke.rvt`** — финальный состав тест-сцены (нужны ли изогнутые торцы в
   MVP — пока нет).

---

## Сборка / тест

* `dotnet build SlabReinforcement/SlabReinforcement.csproj -c Release` — собирается на
  Linux CI (`EnableWindowsTargeting`), как остальные плагины.
* Юнит-тесты — отдельный `SlabReinforcement.Tests` (xUnit), только чистая логика.
* Ручной smoke на `test/slabs-smoke.rvt` — чек-лист в каждом PR.

**Блокеров для старта PR-01 нет.** Жду команду «начинай PR-01».

---

## Phase 6 — v2 (после боевого smoke-теста, 2026-06)

Боевой прогон через revit-mcp на реальной плите с отверстием выявил доработки и
новое направление (решения пользователя 2026-06):

1. **Поле — три режима на выбор в задании:** `Bars` | `Sets` | `AreaSystem`.
   `Sets` = один `Rebar` на «рельс» с layout (NumberWithSpacing) → представительный
   (средний) стержень; перехлёст = деление набора по длине. `AreaSystem` обходит
   отверстия добавлением внутренних контуров в граничный набор кривых.
2. **Робастный поиск пустот** — из реальной грани плиты (`Face.GetEdgesAsCurveLoops`
   + тесселяция дуг), а не из эскизных лупов; ловит отверстия любой геометрии/способа
   и не армирует пустоты. **Сделано в PR-16.**
3. **Оформление торца — по-сегментно:** П-образные ИЛИ отгиб 90° ИЛИ заводка в опору;
   задание описывает каждую границу (балка/стена/свободный край) и куда заводить.
4. **Доп. арматура — произвольные детальные группы** (диаметр, длина, ориентация, слой,
   шаг, форма, регион, анкеровка, выпуски в стену/лестницу/плиту выше).

**Формат задания → структурированный JSON** (`SlabBrief`), вместо плоского CSV —
выразительнее под п.3-4. Контракт: `slab-brief-schema.md`.

### PR-роадмап Phase 6
* **PR-16** — робастный поиск пустот из грани плиты. ✅ merged.
* **PR-17** — JSON-бриф: схема `Config/SlabBrief.cs` + `BriefLoader` + `slab-brief-schema.md`
  + sample; `FieldMode += Sets`. (контракт)
* **PR-18** — движок поля: `Bars`/`Sets`/`AreaSystem` (area с внутренними контурами-дырами),
  потребление брифа в `GenerateSlabRebarCommand` (режим «From JSON brief»).
* **PR-19** — по-сегментное оформление торцов (UBar/Bend90/IntoSupport/Straight) из брифа.
* **PR-20** — произвольные группы доп. арматуры + выпуски (стена/лестница/плита выше).
* **PR-21** — виды/спеки под наборы (представительный стержень, количества), Export-дамп v2.
