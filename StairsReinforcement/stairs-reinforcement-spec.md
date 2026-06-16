# StairsReinforcement — спецификация

Revit 2025 · .NET 8 · C# · ACI 318-19 · Imperial (дюймы) · без сейсмики по умолчанию.
Этот файл — *что* мы строим. Порядок PR — в `stairs-reinforcement-dev-plan.md`.

## 1. Цель и конвейер
Армирование монолитных ж/б лестниц в 3 шага на общей вкладке **Smart Tools**,
панель **Stairs Reinforcement**:

```
Revit ──[Export Stairs]──► stairs.json ─┐
                                         │  ВЫ = агент генерации конфига
                                         ▼  (JSON геометрии + бриф проекта)
                              stairs-assignments.csv  ──[Generate Stair Rebar]──► арматура
                                                                                     │
                                                              [Stair Views] (Phase 4) ▼
                                                              разрезы/планы/спецификации/листы
```
Плагин строит **геометрию** арматуры; **типоразмеры и шаг** задаёт агент вместе с инженером.
Расчёта по нагрузкам нет.

## 2. Соответствие конвенциям репозитория
- Отдельный self-contained плагин (не ссылается на Slab/Column), но логика/скелет — порт из
  **SlabReinforcement**.
- Общая вкладка **Smart Tools** (создаётся идемпотентно), своя панель.
- `.csproj` — копия слабовского (net8.0-windows, WPF, Nice3point Revit API как reference-only).
- **Строгий** поиск `RebarBarType`/`RebarHookType` по точному имени; бросаем со списком доступных.
- Единицы: позиции/отметки — футы (внутренние Revit), сечения/защитный слой — дюймы, углы — градусы.
- Идемпотентность по тегу в `Comments` (см. §10).

## 3. Область
- **MVP (Phase 1–2):** экспорт JSON; продольная (низ+верх) и распределительная арматура маршей;
  оркестратор; диалог; идемпотентность.
- **Phase 3:** сетки площадок; гибы на переломах; выпуски/стартеры; армирование ступеней.
- **Phase 4:** Stair Views (разрезы/планы/спецификации/листы).
- **Вне области:** расчёт по нагрузкам; винтовые/забежные; сборные марши; опалубка.

## 4. Доменная модель лестницы
Логическая лестница `StairAssembly` — упорядоченный снизу вверх список компонентов:
- **Flight (марш)** — наклонный пояс (waist) со ступенями. Геометрия: плоскость пояса, ось
  уклона, ширина, толщина пояса, длина в плане и полный подъём, число проступей/подступёнков,
  размеры проступи/подступёнка, нижняя и верхняя опоры.
- **Landing (площадка)** — горизонтальная плита: контур, толщина, отметка, опоры по граням,
  связанные марши.

### 4.1 Два представления → один объект
| Представление | Источник геометрии | Хост арматуры |
|---|---|---|
| Нативная `Stairs` | `GetStairsRuns()` → `StairsRun` (ширина, высота, путь, footprint, `StairsRunType`/waist); `GetStairsLandings()` → `StairsLanding` | `Stairs`, если `RebarHostData.IsValidHost()`; иначе фолбэк/skip |
| Перекрытиями | выделенные `Floor`: наклонные = марши, горизонтальные = площадки; профиль из `Floor.SketchId` | сам `Floor` (проверенный rebar-host) |

Адаптеры: `StairsElementSource`, `FloorStairSource` → общий `StairAssembly`.

### 4.2 Локальный каркас марша (FlightFrame)
- `U` — ось уклона в плоскости пояса: `U = (cosθ·runDirₓ, cosθ·runDir_y, sinθ)`.
- `W` — поперечная горизонтальная: `W = runDirHoriz.Perp` (лежит в плане).
- `N` — нормаль пояса: `N = U × W` (наружу от пояса).
- `At(u, w, n)` = `Origin + U·u + W·w + N·n` — позиция стержня в мире.
- **Нормаль для гнутого продольного стержня** (фолд по концам) = `W` = `Z × runDirHoriz`
  (мировая, ⟂ вертикальной плоскости марша). Никогда не `BasisZ` для наклонного стержня.

Площадка — как маленькая плита: каркас в горизонтали, сетки X/Y.

## 5. JSON-экспорт «StairsDump»
snake_case, единицы — в суффиксах имён. Верхний уровень (по образцу SlabDump):
```jsonc
{
  "document": { "title": "...", "path": "..." },
  "generated_at": "2026-06-16T12:00:00",
  "schema_version": 1,
  "units_note": "Sections & cover in inches; positions/elevations in feet; angles in degrees.",
  "levels": [ { "name": "L1", "elevation_ft": 0.0, "id": 123 } ],
  "stair_types_in_use": { "<type_id>": { "family": "...", "type": "...", "waist_in": 6.0 } },
  "available_rebar_bar_types": [ { "name": "#5", "nominal_diameter_in": 0.625 } ],
  "available_rebar_hook_types": [ { "name": "Standard - 90 deg" } ],
  "warnings": [ "..." ],
  "stairs": [ {
    "element_id": 555, "mark": "ST-1", "comments": "<бриф армирования>",
    "source": "stairs" | "floors", "base_level": "L1", "top_level": "L2",
    "rebar_host_ok": true,
    "flights": [ {
      "index": 0, "component_id": 556, "source": "run",
      "waist_in": 6.0, "width_ft": 3.5, "run_length_ft": 9.0, "total_rise_ft": 6.0,
      "slope_deg": 33.7, "tread_count": 12, "riser_count": 13, "tread_in": 11, "riser_in": 7,
      "local_basis": { "origin_ft": [x,y,z], "u_dir": [x,y,z], "w_dir": [x,y], "n_dir": [x,y,z], "run_world_deg": 0.0 },
      "bbox": { "min_ft": [x,y,z], "max_ft": [x,y,z] },
      "lower_support": { "kind": "slab|beam|wall|foundation|landing|none", "element_id": 99, "elevation_ft": 0.0 },
      "upper_support": { "kind": "...", "element_id": 77, "elevation_ft": 6.0 },
      "rebar_cover": { "top": { "name": "...", "distance_in": 1.5 }, "bottom": { "...": 1.5 } }
    } ],
    "landings": [ {
      "index": 0, "component_id": 600, "source": "landing",
      "thickness_in": 6.0, "elevation_ft": 6.0, "area_sf": 20.0,
      "local_basis": { ... }, "bbox": { ... }, "boundary": [ { "start_ft": [x,y], "end_ft": [x,y], "support": "wall|beam|free" } ],
      "connects_flights": [0, 1]
    } ],
    "context": { "supports": [ ... ] },
    "hints": { "two_way_landing": true, "needs_top_over_supports": true, "reentrant_corners": [ ... ] }
  } ]
}
```
`comments` — основной источник брифа армирования (как в Slab/Column). Подтверждать поле у
пользователя (иногда кастомный параметр).

## 6. CSV-формат (одна строка на Mark, разрежённо)
Поле пишется, только если отличается от дефолта конфига. Группы колонок (полный список — в
`stairs-assignments-csv-guide.md`):
- **Идентификация:** `Mark` (обяз.), `Units`, `CleanExisting`, `Expected*` (валидаторы).
- **Защитный слой:** `CoverTop/Bottom/Side`.
- **Марш низ:** `FlightBottomMain{BarType,Spacing|Count,StartAnchor,EndAnchor,…}`, `FlightBottomDist{…}`.
- **Марш верх:** `FlightTopMode`, `FlightTopMain{…}`, `FlightTopDist{…}`, `FlightTopSupportExtent`.
- **Ступени:** `StepsMode`, `StepsBarType`, `StepsLeg`.
- **Площадки:** `LandingMode`, `LandingBottomX/Y{…}`, `LandingTopMode`, `LandingTopX/Y{…}`, `LandingTopSupportExtent`.
- **Стыки/выпуски:** `KneeEnabled/Mode/BarType/Spacing/Leg`, `Starters{Enabled,Host,Form,BarType,Spacing,Embed,Projection}`.
- **Длины/нахлёст:** `MaxBarLength`, `LapMode`, `LapLength`, `LapFactor`, `LapStagger`.

Селекторы (если будут) — через пробел, не запятую. UTF-8 с BOM. Строки `#` — комментарии-обоснования.

## 7. Движок генерации
`StairsReinforcer.Run(perStair, dryRun) → RunResult`. Один `TransactionGroup` на прогон, один
`Transaction` на лестницу: `clean (по тегу) → построить наборы → commit/rollback`. dry-run
откатывает всё. Последовательность билдеров на компонент:

| # | Билдер | Слой(и) |
|---|---|---|
| 1 | `FlightLongitudinalBuilder` | FlightBottomMain, FlightTopMain |
| 2 | `FlightDistributionBuilder` | FlightBottomDist, FlightTopDist |
| 3 | `StepBarBuilder` | Steps |
| 4 | `LandingMatBuilder` | LandingBottom/Top X/Y |
| 5 | `KneeBarBuilder` | Knee |
| 6 | `StarterBarBuilder` | Starter |

Каждый билдер возвращает `int created` (или `Result(int, string? skipReason)` для зависящих от
контекста). Хост и опоры резолвятся один раз на лестницу и передаются билдерам.

## 8. Стержни: как кладём
- `RebarFactory.Create` — обёртка `Rebar.CreateFromCurves` (стиль, нормаль, хуки, тег).
- `RebarFactory.CreateSet` — один представитель + `SetLayoutAsNumberWithSpacing(count, step, true,
  true, true)` для распределённых наборов (распред. марша, сетки площадок).
- Защитный слой — геометрический инсет осей (cover + dᵇ/2), не через cover-type.
- Анкеровка — прямое удлинение/хук по `BarSetSpec` (`StartAnchor/EndAnchor`, `*AnchorLen`, хуки).
- Длинные прогоны режутся `BarSplitter.Split(len, maxLen, lap)` с нахлёстом (фактор ACI ≈ 40·dᵇ).

## 9. Конфиг и хранение
- POCO `StairsReinforcementConfig` + `BarSetSpec` (см. `Config/`). camelCase, enum по имени.
- `ConfigLoader` (порт), `FolderStorage` (ExtensibleStorage, **новый GUID**, поля ConfigFolder/CsvPath).
- `AssignmentCsv`/`AssignmentTable` (per-Mark, разрежённо) — PR-06.
- `samples/*.json` рядом с DLL для пикера в диалоге.

## 10. Идемпотентность и теги
Каждый элемент: `Comments = STR:{configName}:{stairId}:{layer}`, `layer ∈` `StairLayer`. Повторный
прогон того же конфига сначала удаляет элементы с префиксом `STR:{config}:{stairId}:` в категориях
`OST_Rebar`, `OST_AreaRein`, `OST_PathRein`. Stair Views (Phase 4) изолируют слой по суффиксу тега.

## 11. Краевые случаи
- Нативная `Stairs` не хостит арматуру → фолбэк/понятный skip с причиной (`rebar_host_ok=false`).
- Не та категория / немонолитная → skip.
- Дуги/винтовые → хорда-аппроксимация или skip (Phase 5).
- Несколько маршей в одной сборке → каждый со своими опорами; общий Mark.
- Короткий `MaxBarLength` / отсутствует тип стержня → понятная ошибка, батч продолжается.
- Перекрытие наборов (верх над опорой + сплошной верх) → приоритет явных зон.

## 12. Внешние зависимости / API
`Stairs`/`StairsRun`/`StairsLanding`, `Floor.SketchId`, `Rebar`/`RebarShapeDrivenAccessor`/
`AreaReinforcement`, `BoundingBoxIntersectsFilter`, `RebarHostData`, `ExtensibleStorage`,
`System.Text.Json`, виды/спецификации/листы (Phase 4).

## 13. Что переиспользуем из монорепо
| Источник | Что | Назначение |
|---|---|---|
| SlabReinforcement | `Length`, `ConfigLoader`, `FolderStorage`, `RebarFactory`, `ExistingRebarCleaner`, `AssignmentCsv`, диалог/`RunResult`, селектор | порт с правкой namespace/GUID/категории |
| SlabReinforcement.Geometry | split-at-max-length + lap, scan-line рейки | основа `.Geometry` (+ slope-frame — новое) |
| ColumnReinforcement | гнутые/cranked стержни, дюбели L-формы, нормаль для bend-plane | марш-продольная, гибы, стартеры |
| SmartViews/SlabReinforcement | планы/разрезы/спеки/листы | Stair Views (Phase 4) |
