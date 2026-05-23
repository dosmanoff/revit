# Схема конфигурации `.wrsconfig.json`

> Сопровождает: [MODULES.md](MODULES.md).
> Цель — единый, версионируемый формат описания армирования стены.

---

## 1. Версионирование

Файл начинается с поля `schema_version`. При несовпадении версии загрузчик применяет цепочку миграций (см. ARCHITECTURE.md §7.3).

Текущая версия: `schema_version: 1` (MVP).

---

## 2. Верхний уровень

```jsonc
{
  "schema_version": 1,
  "id": "550e8400-e29b-41d4-a716-446655440000",   // GUID, генерируется при создании
  "name": "СТ-Монолит-200-Внутренняя",
  "description": "Армирование монолитных внутренних стен 200 мм...",
  "author": "dosmanov",
  "created_at": "2026-05-23T10:00:00Z",
  "updated_at": "2026-05-23T10:00:00Z",
  "version": 1,                                    // версия пользовательского контента
  "units": {
    "length": "mm",
    "rebar_diameter": "mm",
    "area": "mm2"
  },
  "applicability": {
    "wall_kinds": ["Basic"],                       // фильтр по WallKind
    "wall_mark_pattern": "^СТ-.*",                 // regex по Mark стены
    "min_thickness": 150,
    "max_thickness": 500
  },
  "common": { /* §3 */ },
  "external_reinforcement": { /* §4.1 */ },
  "internal_reinforcement": { /* §4.1 */ },
  "perimeter": { /* §4.2 */ },
  "opening": { /* §4.3 */ },
  "l_corner": { /* §4.4 */ },
  "t_connection": { /* §4.5 */ },
  "additional_edge": [ /* §4.6 */ ],
  "additional_face": [ /* §4.7 */ ],
  "dowels": { /* §4.8 */ },
  "parameter_mapping": { /* §5 */ },
  "views": { /* §6 */ },
  "schedules": [ /* §7 */ ],
  "rebar_types": { /* §8 — реестр типов арматуры */ },
  "hooks": { /* §9 — реестр типов хуков */ }
}
```

---

## 3. Секция `common`

```jsonc
"common": {
  "min_cover_around_openings": 30,    // минимальный cover при пересечении с проёмом
  "min_rebar_length": 100,            // меньше — стержень удаляется
  "min_distance_between_bars": 50,    // мин. расстояние между смежными стержнями
  "set_solid_in_view": true,
  "set_unobscured": true,
  "exclude_inserts": true,            // игнор Hosted families с типовым параметром "Remove Family" = Yes
  "partition": "АР",                  // имя партиции для нумерации
  "create_rebar_sets": {
    "vertical_mesh": true,
    "horizontal_mesh": true,
    "perimeter_edge": false,
    "opening_edge": false,
    "stirrups": true,
    "dowels": false
  },
  "wall_rebar_cover": {
    "enabled": true,
    "name": "WRS_FabricCover_25"      // имя Rebar Cover Settings в проекте
  }
}
```

---

## 4. Правила армирования

### 4.1 External / Internal mesh
```jsonc
"external_reinforcement": {
  "enabled": true,
  "bar_type_vertical": "WRS_Ø12_A500C",
  "bar_type_horizontal": "WRS_Ø10_A500C",
  "spacing_vertical": 200,
  "spacing_horizontal": 200,
  "covers": {
    "face_cover": 25,                 // от External/Internal face слоя стены
    "vertical_top_offset": 50,
    "vertical_bottom_offset": 50,
    "horizontal_start_offset": 50,
    "horizontal_end_offset": 50
  },
  "major": "Vertical",                 // ближе к грани
  "wall_end_offset": {
    "distance": 50,
    "mode": "FromStartEnd"             // FromStart | FromEnd | FromStartEnd | Centered
  },
  "hooks": {
    "vertical_start": "None",
    "vertical_end": "None",
    "horizontal_start": "None",
    "horizontal_end": "None",
    "hook_rotation_at_start": 0
  }
}
```

### 4.2 Perimeter
```jsonc
"perimeter": {
  "enabled": true,
  "edge_rebar": [
    {
      "count": 2,
      "bar_type": "WRS_Ø16_A500C",
      "anchorage_length": 300,
      "l_leg_length": 200,
      "position": "Center",
      "covers": {
        "edge": 40,
        "interior": 30,
        "exterior": 30,
        "end": 30
      }
    }
  ],
  "diagonal_rebar": {
    "enabled": false,
    "bar_type": "WRS_Ø14_A500C",
    "bar_length": 800,
    "edge_cover": 40
  },
  "u_o_stirrups": {
    "enabled": true,
    "vertical": {
      "enabled": true,
      "rebar_style": "StirrupTie",
      "bar_type": "WRS_Ø8_A500C",
      "hook_type": "None",
      "hook_orientation": "Right",
      "edge_cover_1": 40,
      "edge_cover_2": 40,
      "external_cover": 25,
      "internal_cover": 25,
      "a_segment_length": 300,
      "step": 300,
      "first_last_spacing": {
        "distance": 100,
        "use_for": "Both"             // First | Last | Both
      }
    },
    "horizontal": { /* аналогично */ },
    "o_shape": {
      "enabled_vertical": true,
      "enabled_horizontal": true,
      "hook_type": "Standard135",
      "hook_orientation": "Down"
    }
  }
}
```

### 4.3 Opening
```jsonc
"opening": {
  "enabled": true,
  "categories": {
    "Opening": { "enabled": true },
    "Door":    { "enabled": true, "copy_from": "Opening" },
    "Window":  { "enabled": true, "copy_from": "Opening" }
  },
  "edge_rebar": [
    {
      "count": 1,
      "min_width": 0,
      "max_width": 99999,
      "bar_type": "WRS_Ø12_A500C",
      "side": "All",
      "length_mode": "Extension",
      "anchorage_length": 300,
      "position": "External",
      "covers": {
        "edge": 50,
        "interior": 30,
        "exterior": 30,
        "end": 30
      },
      "bend_at_edges": true
    }
  ],
  "diagonal_rebar": {
    "enabled": false,
    "bar_type": "WRS_Ø12_A500C",
    "side": "TopBottom",
    "bar_length": 1000,
    "edge_cover": 40
  },
  "u_o_stirrups": {
    "enabled": false,
    "bar_shape": "U_90",
    "bar_type": "WRS_Ø8_A500C",
    "min_length": 200,
    "bar_length": 400,
    "spacing": 200,
    "equal_offsets": true,
    "start_offset": 50,
    "end_offset": 50,
    "create_o_stirrup": true,
    "width_for_o": 250,
    "hook_type": "Standard135"
  }
}
```

### 4.4 L-Corner
```jsonc
"l_corner": {
  "enabled": false,
  "outer_corners": {
    "bars": [
      {
        "shape": "Bar1",               // Bar1..Bar5
        "bar_type": "WRS_Ø12_A500C",
        "CI": 30, "CE": 30, "LI": 200, "LE": 200, "a": 300, "b": 300,
        "layout_direction": "StartEnd",
        "start_offset": 100,
        "end_offset": 100,
        "spacing": 200,
        "max_gap": 50
      }
    ]
  },
  "inner_corners": { "bars": [] }
}
```

### 4.5 T-Connection
```jsonc
"t_connection": {
  "enabled": false,
  "max_distance_between_walls": 50,
  "bars": [
    {
      "shape": "Bar5",
      "bar_type": "WRS_Ø12_A500C",
      "CI": 30, "CE": 30, "LI": 200, "LE": 200, "a": 300, "b": 300,
      "layout_direction": "Start",
      "start_offset": 100,
      "end_offset": 100,
      "spacing": 200
    }
  ]
}
```

### 4.6 Additional edge rebar
```jsonc
"additional_edge": [
  {
    "count": 2,
    "bar_type": "WRS_Ø10_A500C",
    "wall_edge": "Vertical",           // Vertical | Horizontal
    "min_search_distance": 0,
    "max_search_distance": 9999,
    "shape": "Straight",               // Straight | L | U | L_CW | L_CCW
    "anchorage_distance": 250,
    "l_leg_length": 200,
    "position": "External",
    "covers": {
      "edge": 40,
      "interior": 30,
      "exterior": 30,
      "end": 30
    }
  }
]
```

### 4.7 Additional face / arbitrary
```jsonc
"additional_face": [
  {
    "name": "Custom-A",
    "face_position": "External",       // External | Internal | Both
    "min_search_distance": 0,
    "max_search_distance": 9999,
    "bar_shape": "U_90",               // Straight | L_90 | U_90 | O
    "bar_type": "WRS_Ø8_A500C",
    "bar_length": 400,
    "extend_distance": 100,
    "anchorage_length": 250,
    "edge_cover": 40,
    "interior_cover": 30,
    "exterior_cover": 30,
    "end_cover": 30,
    "flip": false,
    "rebar_plane_reference": "Center",
    "layout": {
      "rule": "Center",                // Center | Start | End | StartEnd
      "spacing": 200,
      "start_offset": 100,
      "end_offset": 100,
      "wall_cover": false,
      "create_o_stirrup": true,
      "delete_if_no_space": false,
      "hook_type": "Standard135",
      "hook_position": "TopLeft"
    }
  }
]
```

### 4.8 Dowels
```jsonc
"dowels": {
  "enabled": true,
  "source_categories": ["Foundations", "Walls", "Floors"],
  "auto_detect": true,
  "manual_override": false,
  "default_pattern": {
    "bar_type": "WRS_Ø12_A500C",
    "spacing": 200,
    "anchor_into_source": 400,
    "lap_into_wall": 600,
    "form": "Straight",                // Straight | LShape
    "l_leg_length": 200,
    "side_layout": "Match",            // External | Internal | Both | Match (повторять схему mesh)
    "cover": 30
  },
  "auto_l_shape_on_thin_source": true,
  "edge_overrides": [
    {
      "filter": { "edge_role": "Corner" },
      "pattern": {
        "spacing": 100,
        "form": "LShape"
      }
    }
  ]
}
```

---

## 5. Маппинг параметров

```jsonc
"parameter_mapping": {
  "shared_parameters_file": "Resources/SharedParameters.txt",
  "auto_bind_missing": true,
  "from_wall": {
    "Mark":         "WRS_Wall_Mark",
    "WRS_Tier":     "WRS_Wall_Tier",
    "WRS_Section":  "WRS_Wall_Section",
    "Level":        "WRS_Wall_Level"
  },
  "from_rule": {
    "RuleId":       "WRS_Rule_Id",
    "Role":         "WRS_Bar_Role",
    "Position":     "WRS_Bar_Position",
    "Group":        "WRS_Bar_Group"
  },
  "constants": {
    "WRS_Config_Id":      "{config.id}",
    "WRS_Config_Version": "{config.version}",
    "WRS_Run_Stamp":      "{job.timestamp}"
  },
  "computed": {
    "WRS_Bar_Tag": "{config.name}_{wall.Mark}_{rule.Position}"
  }
}
```

Подстановки в значениях: `{config.*}`, `{wall.*}`, `{rule.*}`, `{job.*}`.

---

## 6. Конфиг видов

```jsonc
"views": {
  "section_per_wall": {
    "enabled": true,
    "view_family_type": "Structural Section",
    "view_template": "WRS_StructuralSection",
    "name_pattern": "АР-{wall.Mark}_Section",
    "scale": 50,
    "view_group": "АР_Армирование_Стен",
    "crop_offset": { "x": 200, "y": 200, "z": 200 }
  },
  "elevation_per_wall": {
    "enabled": true,
    "view_family_type": "Wall Elevation",
    "view_template": "WRS_WallElevation",
    "name_pattern": "АР-{wall.Mark}_Elevation",
    "side": "External",                // External | Internal | Both
    "scale": 50,
    "view_group": "АР_Армирование_Стен"
  },
  "detail_nodes": {
    "enabled": false,
    "nodes": ["FoundationJoint", "LCorner", "TConnection", "Opening"],
    "template": "WRS_Node",
    "scale": 20
  },
  "check_3d_view": {
    "enabled": false,
    "name_pattern": "WRS_3D_{wall.Mark}",
    "filter_other_walls": true
  }
}
```

---

## 7. Спецификации (schedules)

Массив шаблонов. Каждый элемент — описание одного `ViewSchedule`.

```jsonc
"schedules": [
  {
    "id": "rebar_by_wall_mark",
    "name": "Ведомость арматуры по марке стены",
    "category": "Structural Rebar",
    "fields": [
      { "parameter": "WRS_Wall_Mark", "header": "Марка стены" },
      { "parameter": "WRS_Bar_Position", "header": "Поз." },
      { "parameter": "Diameter", "header": "Ø, мм" },
      { "parameter": "Length", "header": "Длина, мм" },
      { "parameter": "Quantity", "header": "Кол-во" },
      { "parameter": "TotalLength", "header": "Σ, м", "formula": "Length * Quantity / 1000" },
      { "parameter": "Mass", "header": "Масса, кг" }
    ],
    "filters": [
      { "parameter": "WRS_Config_Id", "op": "equals", "value": "{config.id}" }
    ],
    "sort": [
      { "by": "WRS_Wall_Mark", "dir": "asc" },
      { "by": "Diameter", "dir": "asc" }
    ],
    "grouping": [
      { "by": "WRS_Wall_Mark", "header": true, "footer": true }
    ],
    "totals": ["Quantity", "TotalLength", "Mass"]
  },
  {
    "id": "dowels_only",
    "name": "Спецификация выпусков",
    "category": "Structural Rebar",
    "fields": [ /* ... */ ],
    "filters": [
      { "parameter": "WRS_Bar_Role", "op": "equals", "value": "Dowel" }
    ]
  },
  {
    "id": "steel_usage",
    "name": "Ведомость расхода стали",
    "category": "Structural Rebar",
    "fields": [
      { "parameter": "Diameter", "header": "Ø, мм" },
      { "parameter": "TotalLength", "header": "Σ длина, м" },
      { "parameter": "Mass", "header": "Масса, кг" }
    ],
    "grouping": [
      { "by": "Diameter", "header": true, "footer": true }
    ],
    "totals": ["TotalLength", "Mass"]
  }
]
```

---

## 8. Реестр типов арматуры

Указывает, какие `RebarBarType` в проекте использовать. Если тип не существует — плагин может предложить создать на основе шаблона.

```jsonc
"rebar_types": {
  "WRS_Ø8_A500C":  { "diameter": 8,  "material": "A500C", "auto_create": true },
  "WRS_Ø10_A500C": { "diameter": 10, "material": "A500C", "auto_create": true },
  "WRS_Ø12_A500C": { "diameter": 12, "material": "A500C", "auto_create": true },
  "WRS_Ø14_A500C": { "diameter": 14, "material": "A500C", "auto_create": true },
  "WRS_Ø16_A500C": { "diameter": 16, "material": "A500C", "auto_create": true }
}
```

---

## 9. Реестр хуков

```jsonc
"hooks": {
  "None":          { "revit_name": null },
  "Standard180":   { "revit_name": "Стандартный 180°" },
  "Standard135":   { "revit_name": "Стандартный 135°" },
  "Standard90":    { "revit_name": "Стандартный 90°" }
}
```

---

## 10. JSON Schema (для валидации в редакторе)

Файл `Resources/schemas/wrsconfig.schema.json` — JSON Schema (draft-2020-12). Подключается в редакторе для подсказок и валидации в реальном времени.

Корневая схема (фрагмент):
```jsonc
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "title": "WRSConfig",
  "type": "object",
  "required": ["schema_version", "id", "name", "common"],
  "properties": {
    "schema_version": { "const": 1 },
    "id": { "type": "string", "format": "uuid" },
    "name": { "type": "string", "minLength": 1 },
    "common": { "$ref": "#/$defs/common" },
    "external_reinforcement": { "$ref": "#/$defs/mesh" }
    // ...
  },
  "$defs": {
    "common": { /* ... */ },
    "mesh": { /* ... */ }
  }
}
```

Полная схема разрабатывается параллельно с реализацией (artifact MVP).

---

## 11. Заводские конфиги

Поставляются с плагином, лежат в `Resources/DefaultConfigs/`:

| Файл | Сценарий |
|---|---|
| `default-mvp.wrsconfig.json` | Минимальный MVP: сетки + базовый периметр + edge проёмов + прямые выпуски + одна спецификация. |
| `default-monolith.wrsconfig.json` | Монолитные внутренние стены (полный набор). |
| `default-precast.wrsconfig.json` | Сборные стены с U/O-хомутами и L-Corner. |

Пользователь может скопировать любой заводской и редактировать.

---

## 12. Совместимость со старыми конфигами

- При открытии файла с `schema_version < current`:
  1. Сделать бэкап оригинала: `*.bak`.
  2. Применить миграции по цепочке.
  3. Записать обновлённый файл.
  4. Показать пользователю summary: что изменилось.

- При попытке открыть файл с `schema_version > current`:
  - Редактор показывает ошибку: «Файл создан в более новой версии плагина. Обновите плагин».

---

## 13. Связанные документы

- [MODULES.md](MODULES.md) — как правила потребляют конфиг.
- [PARAMETERS.md](PARAMETERS.md) — словарь параметров, упоминаемых в `parameter_mapping`.
- [ARCHITECTURE.md](ARCHITECTURE.md) — `ConfigStorage`, миграции.
