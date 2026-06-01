# SlabReinforcement — функциональная спека

> Сопровождается планом разработки [slab-reinforcement-dev-plan.md](slab-reinforcement-dev-plan.md).
> Эта спека — **что** строим; план — **в каком порядке** и какими PR-кусками.
>
> Revit 2025, .NET 8, C#. US-нормы: **ACI 318-19, Imperial (дюймы), non-seismic by default**
> (см. memory `repo-conventions`). Новый отдельный плагин в монорепо, по образцу
> `ColumnReinforcement` (конвейер) и `WallReinforcement` (движок генерации) и
> `SmartViews`/`ColumnViews` (документация).

---

## 1. Цель

Полный конвейер армирования железобетонных плит (перекрытия, фундаментные плиты,
elevated slabs) — по аналогии с `ColumnReinforcement`, но для `Floor`:

1. **Кнопка «Export Slabs»** — собирает и выгружает JSON-описание выбранных плит:
   геометрия, примыкающие конструкции по торцам, опоры снизу, отверстия, доступные
   типы стержней/крюков, максимальная длина стержня, длина перехлёста, зоны усиления,
   свободные торцы под П-образные стержни. Это вход для внешнего AI-агента.
2. **Внешний AI-агент** (вне плагина) на основе JSON + исходящего задания на
   армирование + инструкции готовит разрежённый `slab-assignments.csv`.
3. **Кнопка «Generate Slab Rebar»** — читает CSV и генерирует арматуру в Revit, с
   возможностью задать максимальную длину стержня (длинные пролёты режутся с
   нахлёстами).
4. **Кнопка «Slab Views»** — создаёт виды слоёв (Layer 1–4), спецификации и листы,
   раскладывает виды по листам — по образцу `SmartViews`/`ColumnViews`.

```
Revit ──[Export Slabs]──► slabs.json ──► [AI-агент + задание] ──► slab-assignments.csv
                                                                          │
                                                  [Generate Slab Rebar] ◄─┘
                                                          │ Rebar / AreaReinforcement
                                                          ▼
                                                  [Slab Views] ──► Layer-виды + спеки + листы
```

---

## 2. Соответствие репозиторным конвенциям

* **Отдельный плагин** `SlabReinforcement/` в монорепо (не трогаем существующий
  `SlabRebar` — он классификатор готовой арматуры). Структура папок копирует
  `ColumnReinforcement`.
* **Лента: общий таб «Smart Tools»**, панель `Slab Reinforcement`, три push-кнопки.
  Таб создаётся идемпотентно (try/catch на повторный `CreateRibbonTab`).
* **`.csproj`** — копия `WallReinforcement.csproj`: `net8.0-windows`,
  `EnableWindowsTargeting`, `Nice3point.Revit.Api.RevitAPI[UI] 2025.4.0`
  (`ExcludeAssets="runtime"`), `System.Text.Json`. CI собирает на Linux.
* **`RebarBarType`/`RebarHookType` lookup — strict** по точному `.Name`; нет — ошибка
  per-slab со списком доступных. Никакого auto-create.
* **Единицы**: Imperial по умолчанию; конфиг/CSV допускают `Imperial`|`Metric` и
  feet-inches строки (`1'-6"`). Внутри — Revit internal feet.
* **Идемпотентность** через тег в `Comments` (см. §12), как в `WallReinforcement`.

---

## 3. Объём

**В Phase 1–2 (MVP):** прямые прямоугольные/полигональные плиты с горизонтальной
верхней гранью; основное поле (нижняя и верхняя сетки X/Y) отдельными стержнями с
резкой по макс. длине и нахлёстами; экспорт JSON; чтение CSV; результат-отчёт.

**Phase 3:** П-образные по свободным торцам, обвязка отверстий (доп. стержни +
П-образные + диагонали), режим `AreaReinforcement` для поля, зоны усиления над
опорами, анкеровка в балки/стены.

**Phase 4:** Slab Views — Layer 1–4, спецификации, листы.

**Вне области (на старте):** наклонные/изогнутые в плане плиты со сложной кривизной,
рампы/пандусы, преднапряжение, расчёт по нагрузкам (плагин не считает требуемую
площадь — это делает агент/инженер), сейсмическое детектирование.

---

## 4. Доменная модель плиты

* **Слой (layer)** — одна из: `BottomX`, `BottomY`, `TopX`, `TopY`, плюс служебные
  `EdgeU`, `OpeningTrim`, `Support` (усиление), `Dowel`. На виды мапятся 4 основных:
  **Layer 1 = BottomX, Layer 2 = BottomY, Layer 3 = TopX, Layer 4 = TopY.**
* **Локальный базис** плиты: `LocalX` = направление самой длинной граничной кромки
  (как в классификаторе `SlabRebar`), `LocalY = Z × LocalX`. X/Y стержней — проекция
  на этот базис. Угол к world пишется в JSON.
* **Граница (boundary)** — внешний контур (loop) сегментов; каждый сегмент знает свою
  примыкающую конструкцию (свободный торец / балка / стена / соседняя плита).
* **Отверстия (openings)** — внутренние контуры: shaft openings, floor openings,
  by-family проёмы, вырезы в эскизе.
* **Зоны усиления (support zones)** — прямоугольные/полигональные области над
  опорами (column strips) с собственной доп. арматурой.
* **Плоскости** `zBottom = topFace - cover.bottom - …`, `zTop = topFace - cover.top`
  для нижнего/верхнего слоёв (с учётом диаметров перекрёстных стержней).

---

## 5. JSON-экспорт «SlabDump» (задача №1)

Файл `<DocTitle>_slabs.json`, `schema_version: 1`. Структура верхнего уровня
повторяет `columns.json` из `ColumnReinforcement`.

```jsonc
{
  "document":   { "title": "...", "path": "..." },
  "generated_at": "2026-06-01T...",
  "schema_version": 1,
  "units_note": "Section/cover/lengths in inches unless *_ft. Plan coords in feet (Revit internal).",
  "levels": [ { "name": "1ST FLOOR", "elevation_ft": 80.08, "id": 6308706 } ],
  "floor_types_in_use": {
    "<type_id>": { "family": "...", "type": "10\" Concrete", "thickness_in": 10.0,
                   "structural_material": { "name": "...", "id": 0 }, "parameters": { } }
  },
  "available_rebar_bar_types": [ { "name": "#5", "nominal_diameter_in": 0.625 } ],
  "available_rebar_hook_types": [ { "name": "Standard - 90 deg." } ],
  "warnings": [],
  "slabs": [ /* см. ниже */ ],
}
```

Объект **плиты** (`slabs[i]`):

```jsonc
{
  "element_id": 6309475,
  "mark": "S-101",
  "comments": "B: #5@12 EW; T: #5@12 EW over cols",  // первичный источник спеки (как у колонн)
  "family": "Floor", "type": "10\" Concrete", "type_id": 0,
  "level": { "name": "1ST FLOOR", "id": 6308706, "elevation_ft": 80.08 },
  "thickness_in": 10.0,
  "top_elevation_ft": 80.08, "bottom_elevation_ft": 79.25,
  "is_structural": true, "is_foundation": false,
  "rebar_cover": { "top": { "name": "...", "distance_in": 1.5 },
                   "bottom": { "name": "...", "distance_in": 1.5 } },
  "local_basis": { "origin_ft": [x, y, z], "x_dir": [..], "y_dir": [..],
                   "x_world_deg": 0.0 },
  "bbox": { "min_ft": [x, y], "max_ft": [x, y], "area_sf": 420.0 },

  "boundary": [                              // внешний контур, по сегментам
    { "index": 0, "kind": "line", "start_ft": [..], "end_ft": [..], "length_ft": 20.0,
      "mid_normal_world_deg": 90.0,
      "edge": "free",                        // free | beam | wall | slab | grade_beam
      "adjacent": null }                     // или { kind, element_id, mark, type }
  ],
  "openings": [
    { "id": 1, "source": "ShaftOpening|FloorOpening|FamilyInstance|SketchLoop",
      "element_id": 0, "bbox_ft": {..}, "area_sf": 9.0,
      "boundary": [ { "index":0, "kind":"line", "start_ft":[..],"end_ft":[..],"length_ft":3.0 } ] }
  ],

  "context": {
    "supports_below": [                      // колонны/стены/балки под плитой → зоны усиления
      { "kind": "Column", "mark": "C1.3", "element_id": 6309352,
        "footprint_ft": { "center":[..], "width_in":16, "depth_in":16 } } ],
    "walls_bounding": [ { "element_id": 0, "mark": "W1", "thickness_in": 8.0,
                          "boundary_indices": [3] } ],
    "beams": [ { "element_id": 0, "type": "...", "boundary_indices": [0,2] } ],
    "slabs_coplanar": [ { "element_id": 0, "mark": "S-102", "boundary_indices": [1] } ],
    "slab_above": null, "slab_below": null   // для дюбелей/анкеровки между уровнями
  },

  "hints": {                                 // advisory — финальное решение за агентом
    "free_edge_indices": [0, 2],             // торцы под П-образные стержни
    "needs_edge_ubars": true,
    "openings_need_trim": [1],               // отверстия > порога → обвязка+диагонали
    "supports": [ { "mark": "C1.3", "suggested_strip_width_in": 60 } ],
    "max_span_ft": 21.0, "is_two_way": true,
    "recommended_layers": ["BottomX","BottomY","TopX","TopY"]
  },

  "parameters": { /* сырые Revit-параметры: Mark, Comments, Structural Usage, … */ }
}
```

Ключевые отличия от колонн: вместо «column above/below» — **граница с примыканиями**,
**отверстия** и **опоры снизу**; единицы плановых координат — феты (internal), сечения
и покрытия — дюймы.

---

## 6. CSV-формат «slab-assignments» (задача №2/№3)

Разрежённый CSV, **одна строка на `Mark`** плиты; пустая ячейка = дефолт POCO.
UTF-8 (BOM ок), `#` — комментарии, селекторы — через пробел (не запятую). Совпадение
`Mark` без регистра.

Поля (черновой набор; финал — в `slab-assignments-csv-guide.md`, PR-05):

| Группа | Поля |
|---|---|
| Identity | `Mark`, `ExpectedThickness` (валидация) |
| Top-level | `Units` (Imperial\|Metric), `CleanExisting`, `FieldMode` (**Bars**\|**AreaSystem** — выбор представления поля) |
| Cover | `CoverTop`, `CoverBottom`, `CoverSide` |
| Нижняя сетка | `BottomXBarType`, `BottomXSpacing`, `BottomYBarType`, `BottomYSpacing` |
| Верхняя сетка | `TopMode` (None\|Continuous\|OverSupports\|Edges), `TopXBarType`, `TopXSpacing`, `TopYBarType`, `TopYSpacing` |
| Длина/нахлёст | `MaxBarLength`, `LapMode` (Length\|Factor), `LapLength`, `LapFactor` (× d_b), `LapStagger` (bool) |
| Анкеровка торцов | `EdgeAnchorMode` (Straight\|Hook90\|Hook180\|IntoSupport), `EdgeAnchorLen` |
| П-образные по торцам | `EdgeUBarsEnabled`, `EdgeUBarType`, `EdgeUBarSpacing`, `EdgeUBarLeg`, `EdgeUBarSelector` (free\|all\|`0 2`) |
| Обвязка отверстий | `OpeningTrimEnabled`, `OpeningTrimBarType`, `OpeningExtraEachSide` (int), `OpeningUBars` (bool), `OpeningDiagonals` (bool), `OpeningDiagBarType`, `OpeningSelector` |

**Зоны усиления над опорами** (column strips) — отдельный CSV
`slab-zones.csv` (одна строка на зону), т.к. это пространственные области, плохо
ложащиеся в строку-на-плиту:

| Поле | Смысл |
|---|---|
| `SlabMark` | к какой плите |
| `ZoneName` | метка зоны |
| `Face` | Top\|Bottom |
| `Direction` | X\|Y |
| `BarType`, `Spacing` | арматура зоны |
| `Shape` | `SupportMark`+`StripWidth` (полоса над опорой) **или** `BBox` (x1 y1 x2 y2) **или** `Polygon` (x y x y …) |
| `BarLength`/`Extent` | как далеко от опоры заводить |

Принципы наполнения (для агента/инженера): из `comments` плиты вытащить базовую
сетку (`B: #5@12 EW` → BottomX/Y `#5`@12"); по `hints.free_edge_indices` включить
П-образные; по `hints.supports` создать зоны верхнего усиления; по
`hints.openings_need_trim` включить обвязку; имена стержней — только из
`available_rebar_bar_types`.

---

## 7. Движок генерации (задача №3)

Оркестратор `Engine/SlabReinforcer.cs`:
`Run(IDictionary<ElementId, SlabReinforcementConfig> perSlab, IList<SupportZone> zones, bool dryRun) → RunResult`.
`TransactionGroup` на весь прогон, `Transaction` на плиту, `Assimilate`/`RollBack`.

Для каждой плиты:
1. `ExistingRebarCleaner.Clean` — удалить ранее созданное с тегом `SR:`.
2. `Domain/SlabGeometry.For(Floor)` — базис, контуры, отверстия, плоскости слоёв.
3. Поле — по `FieldMode`:
   * **Bars** → `Engine/FieldBarBuilder` — параллельные стержни X и Y на нижней и
     верхней плоскостях, обрезанные по `boundary − openings`; каждый «рельс»
     длиннее `MaxBarLength` режется на сегменты с перекрытием `LapLength`
     (со смещением стыков `LapStagger`); `Rebar.CreateFromCurves` per стержень.
     **Это ядро — чистая геометрия, покрывается юнит-тестами** (раскладка, резка,
     нахлёст, клиппинг по отверстию).
   * **AreaSystem** → `Engine/FieldMeshBuilder` — `AreaReinforcement.Create(doc,
     floor, boundaryCurves, majorDir, areaTypeId, barTypeId, hookTypeId)`; верхний и
     нижний слои активны; шаг/типы через `REBAR_SYSTEM_*` параметры (как
     `FaceMeshBuilder` в `WallReinforcement`, но host = Floor). Резка по макс. длине
     здесь не контролируется (Revit раскладывает систему сам) — компромисс режима.
4. `Engine/EdgeUBarBuilder` — П-образные по свободным торцам: 3-сегментная кривая
   (верхняя полка → вертикаль вниз у грани → нижняя полка), шаг вдоль торца.
5. `Engine/OpeningTrimBuilder` — доп. прямые стержни вдоль каждой грани отверстия
   (верх и низ), П-образные обвязки грани, диагонали в углах.
6. `Engine/SupportZoneBuilder` — доп. верхние (и при необходимости нижние) стержни в
   зонах усиления над опорами, с заводкой по `BarLength/Extent`.
7. `Engine/EdgeAnchorBuilder` — заводка концов полевых стержней в балки/стены
   (крюк/L), либо просто `EdgeAnchorLen` от грани.

Все созданные элементы тегируются (см. §12), слой пишется в тег.

---

## 8. Команда «Slab Views» (задача №4)

`Commands/SlabViewsCommand` → `Engine/SlabViewsEngine.Run(slabIds)`. По образцу
`ColumnViewsEngine`/`ColumnSheetBuilder`:

* На каждую плиту — до 4 планов: `ViewPlan.Create` на уровне плиты, view-range с
  секущей плоскостью под нужный слой, crop = bbox + padding; имена по шаблону
  `{Mark} - Layer {N} {Layer}`.
* **Изоляция слоя**: показываем только стержни своего слоя, остальные —
  hide/halftone. Слой берём из **тега `SR:…:{layer}`** генерации (см. §12; решение —
  не зависим от классификатора `SlabRebar`). Для арматуры без тега — fallback на
  «показывать всё».
* `Engine/SlabScheduleBuilder` — `ViewSchedule` (OST_Rebar), фильтр по host-плите и
  слою, группировка по форме/длине.
* `Engine/SlabSheetBuilder` — `ViewSheet.Create` + `Viewport.Create` (сетка 2×2 для
  4 планов) + `ScheduleSheetInstance.Create` сбоку; title block по имени; токен-имена
  `{Mark}`; обработка дубликатов (Skip\|Overwrite\|AppendSuffix).
* Конфиг `SlabViewsConfig` в ExtensibleStorage (новый GUID) + JSON-пресеты — копия
  паттерна `ColumnViewsConfigStore`.

---

## 9. Конфиг и хранение

* `Config/SlabReinforcementConfig.cs` — POCO (`schemaVersion`, `name`, `units`,
  `cover`, `field` (bottom/top + mode), `lengths` (maxBar/lap), `edges`, `openings`,
  `anchors`). `[JsonPropertyName]` как в `WallReinforcement`.
* `Config/ConfigLoader.cs` — `LoadFromFolder(folder)`.
* `Config/FolderStorage.cs` — ExtensibleStorage, **новые GUID** для пути к папке
  конфигов и к CSV (две схемы), + путь к `slab-zones.csv`.
* `Config/AssignmentCsv.cs` + `Domain/AssignmentTable.cs` — парсер CSV → `ByMark`,
  `ExpectedByMark`, `Issues`, плюс зоны.
* `samples/` — `default.json`, `slab-assignments-basic.csv`, `slab-zones-demo.csv`,
  пример dump'а.

---

## 10. UX (диалоги, code-only WPF)

* **Export Slabs** — выбор плит (или PickObjects-фильтр `OST_Floors`), выбор пути
  сохранения, опции (включать ли отверстия/контекст/hints), кнопка Export → пишет
  JSON, показывает сводку.
* **Generate Slab Rebar** — два режима как у `ColumnReinforcementDialog`:
  «Same for all» (один JSON-конфиг) и «From CSV» (пер-плита + таблица валидации
  `Mark` ✓/⚠, размеры, issues). Поле `Max bar length`, dry-run. ResultsDialog.
* **Slab Views** — наименования, масштабы, detail level, foreign-rebar (Hide/
  Halftone/Show), title block, шаблоны спецификаций, тогглы (создавать спеку/листы),
  как `ColumnViewsDialog`.

---

## 11. Идемпотентность и теги

Тег в параметре `Comments` создаваемых элементов:

```
SR:{configName}:{slabElementId}:{layer}
```

где `layer ∈ {BottomX, BottomY, TopX, TopY, EdgeU, OpeningTrim, Support, Dowel}`.

* `ExistingRebarCleaner.Clean(doc, slabId, configName)` — удаляет все
  `Rebar`/`AreaReinforcement` с тегом `SR:{configName}:{slabId}:*` перед новым
  прогоном (по флагу `CleanExisting`). Категории: `OST_Rebar`, `OST_AreaRein`.
* `Slab Views` фильтрует слои по суффиксу `:{layer}` тега — **генерация и виды
  согласованы через тег, без зависимости от классификатора `SlabRebar`** (решение
  пользователя).

---

## 12. Граничные случаи

* Плита без структурного назначения / без host-категории `OST_Floors` → skip с
  понятной причиной.
* Несколько внешних loop'ов (плита-«бублик») — берём наибольший как внешний, прочие
  трактуем как отверстия.
* Дуги/кривые в границе — в Phase 1 аппроксимируем хордой для клиппинга, помечаем
  warning (полноценные дуги — Phase 5).
* Отверстие у самого края — обвязка может выходить за границу: клиппим, warning.
* `MaxBarLength` меньше шага сетки/короче плиты в одном направлении — без резки.
* Отсутствует `RebarBarType` из CSV → плита падает с сообщением + список доступных,
  остальные продолжают.
* Перекрытие зон усиления и поля — допустимо (доп. арматура поверх), без дедупликации
  в Phase 1.

---

## 13. Внешние зависимости / API

* Геометрия Floor: `Floor.GetAnalyticalModel`? нет — используем `Sketch` /
  `HostObject` грани через `HostObjectUtils.GetTopFaces/GetBottomFaces`,
  `Floor.get_Geometry`, `CurveLoop`; отверстия — `Floor` `Sketch.Profile` loops,
  `Opening` элементы (`FilteredElementCollector` `OST_FloorOpening`/`ShaftOpening`),
  by-family проёмы.
* Арматура: `Rebar.CreateFromCurves`, `AreaReinforcement.Create`, `RebarBarType`,
  `RebarHookType`, `RebarShape`.
* Виды: `ViewPlan.Create`, `View.GetViewRange/SetViewRange`, `ViewSchedule`,
  `ViewSheet.Create`, `Viewport.Create`, `ScheduleSheetInstance.Create`.
* Контекст: `FilteredElementCollector` + `BoundingBoxIntersectsFilter` для опор под
  плитой; `ElementIntersectsElementFilter`/edge-проекции для примыканий по торцам.
* Хранение: `ExtensibleStorage`, `System.Text.Json`.
* Документация API: revitapidocs.com/2025, help.autodesk.com/view/RVT/2025.

---

## 14. Что переиспользуем из монорепо

| Из | Что | Куда |
|---|---|---|
| `WallReinforcement` | `RebarFactory`, `UnitConv`, `ExistingRebarCleaner`, `ConfigLoader`, `FolderStorage`, `Length` | копия с правкой namespace/GUID |
| `WallReinforcement` | `EdgeBarBuilder` (U-bars), `OpeningTrimBuilder` | образец для `EdgeUBarBuilder`/`OpeningTrimBuilder` плиты |
| `ColumnReinforcement` | `AssignmentCsv`, `AssignmentTable`, диалог «From CSV», `RunResult`, `ResultsDialog` | образец конвейера CSV |
| `SmartViews`/`ColumnViews` | `ColumnViewsEngine`, `ColumnScheduleBuilder`, `ColumnSheetBuilder`, `ColumnViewsConfigStore` | образец `SlabViews*` |
| `SlabRebar` | локальный базис плиты (longest sketch edge), Top/Bottom по Z | образец `SlabGeometry` (но генерация **не** зависит от классификатора) |
