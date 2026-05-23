# Детализация модулей

> Сопровождает: [SPEC.md](SPEC.md), [ARCHITECTURE.md](ARCHITECTURE.md).
> Каждый раздел описывает: входы/выходы, алгоритм, ключевые классы, граничные случаи, тесты.

---

## M1. Армирование стен

### M1.1 Внешняя / Внутренняя сетка (External / Internal Reinforcement)

**Входы.**
- `WallContext` (геометрия, проёмы, сопряжения).
- Конфиг секции `external_reinforcement` / `internal_reinforcement`:
  - `enabled: bool`
  - `bar_type_vertical`, `bar_type_horizontal` — имя `RebarBarType` в проекте.
  - `spacing_vertical`, `spacing_horizontal` — мм.
  - `cover` — защитный слой от внешней / внутренней грани слоя стены.
  - `major: "Vertical" | "Horizontal"` — какая сетка ближе к грани.
  - `vertical_offset_top`, `vertical_offset_bottom`, `horizontal_offset_start`, `horizontal_offset_end`.
  - `wall_end_offset_distance`, `wall_end_offset_mode: "FromStart" | "FromEnd" | "FromStartEnd" | "Centered"`.
  - `hook_start`, `hook_end` — типы хуков (`None`, `Standard180`, `Standard135`, …).

**Выходы.**
- Набор `RebarPlacement` для вертикальных и горизонтальных стержней.

**Алгоритм.**
1. Определить плоскость сетки (внешняя или внутренняя грань целевого слоя ± cover).
2. По длине стены вычислить позиции вертикальных стержней (с учётом offset / mode).
3. По высоте стены вычислить позиции горизонтальных стержней.
4. Для каждой позиции построить `RebarPlacement` с прямой геометрией.
5. Учесть `major`: один из массивов «утоплен» на диаметр другого.
6. Возвращать также роль (`Vertical` / `Horizontal`) для последующего параметра `WRS_Bar_Role`.

**Граничные случаи.**
- Стена короче двойного offset → создавать минимум 1 стержень в центре.
- Очень короткий шаг → валидатор должен вернуть warning и предложить >= 2×diameter.
- Хуки выходят за грань стены → автоматически сократить, если возможно, иначе предупредить.

**Тесты (MVP).**
- Прямая стена 5×3 м, шаг 200 — должно получиться 25 вертикальных и 14 горизонтальных стержней (с учётом offset).
- Стена 0.5×3 м — fallback на минимум.
- Centered mode — равные отступы от обоих концов.

---

### M1.2 Периметр (Perimeter Reinforcement)

**Подразделы.**

#### Perimeter Edge Rebar
- 1–2 «ниток» краевой арматуры вдоль периметра.
- Параметры: `count` (1 или 2), `anchorage_length`, `l_leg_length`, `position`, `edge_cover`, `interior_cover`, `exterior_cover`, `end_cover`.

#### Perimeter Diagonal Rebar
- Диагональные стержни в углах.
- Параметры: `bar_length`, `edge_cover`, ориентация.

#### Perimeter U/O Stirrups
- Вертикальные U-хомуты по горизонтальным граням.
- Горизонтальные U-хомуты по вертикальным граням.
- O-хомуты создаются автоматически если для U не хватает места.

**Алгоритм.**
1. Получить периметр стены как замкнутый список рёбер (включая внутренние, если в стене вырезаны проёмы по контуру).
2. Для каждого ребра / угла применить соответствующий тип правила.
3. В углах построить L-форму с длиной `l_leg_length`.
4. Для U/O-хомутов рассчитать позиции вдоль ребра с учётом `start_offset`, `end_offset`, `step`, `use_for_first_last`.

**Граничные случаи.**
- Узкая стена → U-хомут не помещается → переключение на O.
- Проём впритык к краю → анкеровка изгибается вдоль перпендикулярного ребра.

---

### M1.3 Окантовка проёмов (Opening / Door / Window Reinforcement)

**Параметры (на каждый проём).**
- `edge_rebar` — массив строк (multi-row):
  - `count` (1/2), `min_width`, `max_width`, `bar_type`,
  - `side: "Left" | "Right" | "Top" | "Bottom" | "All"`,
  - `length_mode: "Extension" | "ToTop" | "ToBottom" | "TopToBottom" | "ToLeft" | "ToRight" | "LeftToRight"`,
  - `anchorage_length`, `position`, `covers`, `end_cover`, `bend_at_edges`.
- `diagonal_rebar`:
  - `side: "Top" | "Bottom" | "TopBottom"`, `bar_length`, `edge_cover`.
- `u_o_stirrups`:
  - `bar_shape`, `min_length`, `bar_length`, `spacing`, `equal_offsets`, `start_offset`, `end_offset`, `create_o_stirrup`, `width_for_o`, `hook_type`.

**Алгоритм.**
1. Для каждого `Opening` в `WallContext`:
   - Применить только те строки `edge_rebar`, у которых `min_width ≤ opening.Width ≤ max_width` и подходящая сторона.
   - Построить геометрию стержней на каждой стороне.
   - Для диагональных — построить под 45° от угла проёма.
   - Для U/O-хомутов — пройти по верхнему/нижнему/боковому ребру проёма.

**Граничные случаи.**
- Проём у края стены → bar bend at edges или удаление, по флагу.
- Проёмы с непрямоугольной формой (вырез сложной формы) → MVP: bounding box; v1.0: следование контуру.

---

### M1.4 L-Corner и T-Connection

#### L-Corner
- Условия: 90° соединение двух стен.
- Определяются «внешний» / «внутренний» углы по внешним граням.
- Bar1, Bar2 — расширение горизонтальных стержней основной сетки.
- Bar3, Bar4 — новые угловые стержни (L-, U-форма).
- Bar5 — специальная форма для T-узлов.
- Параметры: `CI`, `CE`, `LI`, `LE`, `a`, `b`, `layout_direction` (`Start`, `End`, `StartEnd`, `Center`), `start_offset`, `end_offset`, `spacing`, `max_gap`.

#### T-Connection
- Условия: T-соединение двух стен.
- Bar5: специальная форма, параметр `max_distance_between_walls`.
- Аналогичная раскладка по высоте.

**Граничные случаи.**
- Угол ≠ 90° (например, 60° или 120°) → MVP: не обрабатывается, warning; v1.0: общий случай.

---

### M1.5 Дополнительная арматура на рёбрах (Additional Edge Rebar / Additional Reinforcement)

- Прямые, L, U стержни на выбранных вертикальных/горизонтальных рёбрах.
- Параметры: `min_search_distance`, `max_search_distance` (фильтр глубины cut), `anchorage_distance`, `l_leg_length`, `position`, `covers`, `end_cover`.
- Layout: `Center`, `Start`, `End`, `Start/End` + `spacing`, `start_offset`, `end_offset`, `wall_cover`.

---

### M1.6 Реестр правил M1

| RuleId | Класс | Категория |
|---|---|---|
| `wrs.external_mesh` | `ExternalMeshRule` | M1.1 |
| `wrs.internal_mesh` | `InternalMeshRule` | M1.1 |
| `wrs.perimeter_edge` | `PerimeterEdgeRule` | M1.2 |
| `wrs.perimeter_diagonal` | `PerimeterDiagonalRule` | M1.2 |
| `wrs.perimeter_u_stirrups` | `PerimeterUStirrupsRule` | M1.2 |
| `wrs.opening_edge` | `OpeningEdgeRule` | M1.3 |
| `wrs.opening_diagonal` | `OpeningDiagonalRule` | M1.3 |
| `wrs.opening_u_stirrups` | `OpeningUStirrupsRule` | M1.3 |
| `wrs.l_corner` | `LCornerRule` | M1.4 |
| `wrs.t_connection` | `TConnectionRule` | M1.4 |
| `wrs.additional_edge` | `AdditionalEdgeRule` | M1.5 |
| `wrs.additional_face` | `AdditionalFaceRule` | M1.5 |

---

## M2. Выпуски (Dowels)

### M2.1 Конфиг
```jsonc
"dowels": {
  "enabled": true,
  "source_categories": ["Foundations", "Walls", "Floors"],
  "auto_detect": true,            // искать источник автоматически
  "vertical_pattern": {
    "bar_type": "Ø12 A500C",
    "spacing": 200,
    "anchor_into_source": 400,
    "lap_into_wall": 600,
    "form": "Straight",           // или "LShape"
    "l_leg_length": 200,
    "side_layout": "Both",        // External / Internal / Both / Match
    "cover": 30
  },
  "edge_overrides": [
    {
      "filter": { "edge_role": "Corner" },
      "spacing": 100
    }
  ]
}
```

### M2.2 Алгоритм
1. Найти кандидатов-источников ниже стены (плоскость низа ± tolerance, BoundingBoxIntersectsFilter).
2. Для каждой стены определить «зоны» — отрезки нижнего ребра, под которыми разные источники.
3. По каждой зоне разложить позиции выпусков с шагом `spacing`.
4. Для каждой позиции построить `RebarPlacement`:
   - Прямая часть: от `anchor_into_source` ниже верха источника до `lap_into_wall` выше низа стены.
   - При `form = "LShape"`: добавить загиб с длиной `l_leg_length`.
5. Установить `WRS_Bar_Role = "Dowel"`, записать `DowelLink` в Extensible Storage.

### M2.3 Граничные случаи
- Источник тоньше `anchor_into_source` → авто-переключение на `LShape` (если разрешено флагом `auto_l_shape_on_thin_source: true`), иначе warning.
- Стена не пересекается с источниками → warning, выпуски не создаются.
- Несколько источников: создание по зонам без дублей на стыках источников.
- Стена наклонная → выпуски наклоняются перпендикулярно верхней грани источника.

### M2.4 Классы
- `DowelOrchestrator` (Application).
- `DowelRule` (Domain).
- `SourceDetector` (Domain) — алгоритм поиска источников, использует `IElementProvider`.
- `IElementProvider` (адаптер Revit) — выдаёт элементы по категориям и фильтрам.

---

## M3. Назначение параметров

### M3.1 Маппинг
В конфиге секция `parameter_mapping`:
```jsonc
"parameter_mapping": {
  "from_wall": {
    "Mark": "WRS_Wall_Mark",
    "WRS_Tier": "WRS_Wall_Tier",
    "WRS_Section": "WRS_Wall_Section"
  },
  "from_rule": {
    "Role": "WRS_Bar_Role",
    "Position": "WRS_Bar_Position"
  },
  "constants": {
    "WRS_Config_Id": "{config.id}",
    "WRS_Run_Stamp": "{job.timestamp}"
  }
}
```

### M3.2 Алгоритм
1. Перед записью каждого стержня собрать словарь `parameters` для `RebarPlacement`.
2. После создания стержня — для каждого `(name, value)` вызвать `IParameterStore.Set(rebarId, name, value)`.
3. Если параметра нет в проекте — `EnsureSharedParameter(name, ParameterScope.Rebar)` подгружает его из Shared Parameter File плагина и биндит к категории `Structural Rebar`.

### M3.3 Граничные случаи
- Тип параметра не совпадает (например, ожидается `Integer`, в Shared Parameter File `Text`) → конверсия через `Convert.ChangeType`; при провале — warning.
- Параметр уже забиндин с другим GUID → плагин не перебиндивает, использует существующий, но логирует warning.

---

## M4. Виды и детали

### M4.1 Конфигурация (фрагмент)
```jsonc
"views": {
  "section_per_wall": {
    "enabled": true,
    "view_family_type": "Structural Section",
    "view_template": "WRS_StructuralSection",
    "name_pattern": "АР-{wall.Mark}_Section",
    "scale": 50,
    "view_group": "АР_Армирование_Стен"
  },
  "elevation_per_wall": {
    "enabled": true,
    "view_family_type": "Wall Elevation",
    "view_template": "WRS_WallElevation",
    "name_pattern": "АР-{wall.Mark}_Elevation",
    "scale": 50,
    "view_group": "АР_Армирование_Стен"
  },
  "detail_nodes": {
    "enabled": false,
    "nodes": ["FoundationJoint", "LCorner", "TConnection", "Opening"]
  },
  "3d_check_view": {
    "enabled": false,
    "name_pattern": "WRS_3D_{wall.Mark}"
  }
}
```

### M4.2 Алгоритм (Section per Wall)
1. Получить геометрию стены: locationLine, толщина, высота.
2. Построить bounding box сечения перпендикулярно стене в центре.
3. `ViewSection.CreateSection(doc, viewFamilyTypeId, sectionBox)`.
4. Установить имя по `name_pattern`.
5. Назначить шаблон вида.
6. Записать в параметры вида: `WRS_Wall_Mark`, `WRS_View_Type`, `WRS_Run_Stamp`.

### M4.3 Алгоритм (Elevation per Wall)
1. Получить плоскость внешней грани стены.
2. Создать `ViewSection` параллельно плоскости стены, со смещением.
3. Crop region — bounding box стены с отступом.
4. Шаблон вида + параметры.

### M4.4 Граничные случаи
- Имя вида уже существует → суффикс `_2`, `_3`, либо обновление существующего (по флагу `overwrite`).
- Кривая стена → MVP: одно сечение в средней точке; v1.0: развёртка.

---

## M5. Спецификации

### M5.1 Шаблоны
В `templates/schedules/*.json`:
```jsonc
{
  "id": "rebar_by_wall_mark",
  "name": "Ведомость арматуры по марке стены",
  "category": "Structural Rebar",
  "fields": [
    {"parameter": "WRS_Wall_Mark", "header": "Марка стены"},
    {"parameter": "WRS_Bar_Position", "header": "Поз."},
    {"parameter": "BarDiameter", "header": "Ø, мм"},
    {"parameter": "RebarShape", "header": "Форма"},
    {"parameter": "Length", "header": "Длина, мм"},
    {"parameter": "Quantity", "header": "Кол-во"},
    {"parameter": "TotalLength", "header": "Σ длина, м", "formula": "Length * Quantity / 1000"},
    {"parameter": "Mass", "header": "Масса, кг"}
  ],
  "filters": [
    {"parameter": "WRS_Config_Id", "op": "equals", "value": "{config.id}"}
  ],
  "sort": [
    {"by": "WRS_Wall_Mark", "dir": "asc"},
    {"by": "BarDiameter", "dir": "asc"},
    {"by": "Length", "dir": "asc"}
  ],
  "grouping": [
    {"by": "WRS_Wall_Mark", "header": true, "footer": true}
  ],
  "formatting": {
    "header_style": "WRS_ScheduleHeader",
    "totals": ["Quantity", "TotalLength", "Mass"]
  }
}
```

### M5.2 Алгоритм
1. Загрузить шаблоны из конфига.
2. Для каждого шаблона: `ViewSchedule.CreateSchedule(doc, categoryId)`.
3. Через `ScheduleDefinition.AddField`, `AddFilter`, `AddSortGroupField` применить настройки.
4. Если такая спецификация уже существует — пересоздать (с предварительной проверкой `Force overwrite`) либо обновить состав полей.

### M5.3 Граничные случаи
- Поле, по которому фильтр, не существует в проекте → `IParameterStore.EnsureSharedParameter`.
- Формула некорректна → ловить `InvalidOperationException`, логировать, продолжать.

---

## M6. Конфигуратор (Config Editor)

### M6.1 Архитектура UI
- `ConfigEditorView.xaml` — основной `Window` с боковой панелью разделов.
- Разделы:
  1. **Общие настройки** (cover, partition, rebar sets, exclude inserts, ...).
  2. **External / Internal mesh.**
  3. **Periметр.**
  4. **Окантовка проёмов.**
  5. **L-Corner.**
  6. **T-Connection.**
  7. **Доп. рёбра.**
  8. **Произвольные правила.**
  9. **Выпуски.**
  10. **Маппинг параметров.**
  11. **Виды.**
  12. **Спецификации.**
- Каждый раздел — отдельный `UserControl`, биндится на свою часть `ConfigViewModel`.

### M6.2 Сценарии
- **New**: создаёт `RebarConfig` из embedded `default-mvp.wrsconfig.json`.
- **Open**: file dialog → `ConfigStorage.Load(path)` → `ConfigViewModel`.
- **Save / Save As**: сериализация в JSON + бэкап предыдущего файла.
- **Import**: копирование из другой папки в `%APPDATA%\WRSPlugin\configs\`.
- **Export**: копия в любое место.
- **Validate**: статический анализ конфига (см. ниже).

### M6.3 Валидация
- Шаг ≥ 2× диаметра (warning, если меньше).
- Cover ≥ диаметра + 5 мм (warning).
- `bar_type_*` существует в проекте (info-предупреждение в редакторе, проверяется при `Apply`).
- `view_template` существует.
- Все ссылки между разделами консистентны (например, `parameter_mapping` ссылается только на параметры, существующие в Shared Parameter File плагина).

### M6.4 Привязка стены к конфигу (Wall Link)
- Команда `LinkWallCommand`:
  1. Выбор стен в Revit.
  2. Открывается диалог: список конфигов + кнопка «Применить».
  3. На каждую стену записывается `WRS.WallLink` в Extensible Storage и параметр стены `WRS_Config_Id`.
  4. После Link можно запускать `ArmWallCommand` без повторного выбора конфига.

---

## Сводная таблица «модуль ↔ команда ↔ оркестратор»

| Модуль | Ribbon-команда | Оркестратор | Доменные правила |
|---|---|---|---|
| M1 | `ArmWallCommand` | `WallReinforcementOrchestrator` | `ExternalMeshRule`, `PerimeterEdgeRule`, ... |
| M2 | `DowelCommand` | `DowelOrchestrator` | `DowelRule` + `SourceDetector` |
| M3 | (встроено в M1/M2) | `ParameterAssignmentService` | — |
| M4 | `CreateViewsCommand` | `ViewGenerationOrchestrator` | — |
| M5 | `CreateSchedulesCommand` | `ScheduleOrchestrator` | — |
| M6 | `ConfigEditorCommand`, `LinkWallCommand` | `ConfigService` | — |
| Update | `UpdateRebarCommand` | `WallReinforcementOrchestrator.Update()` | те же правила в режиме diff |
| Delete | `DeleteRebarCommand` | `WallReinforcementOrchestrator.Delete()` | — |
