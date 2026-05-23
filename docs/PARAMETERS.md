# Словарь параметров арматуры

> Сопровождает: [SPEC.md](SPEC.md), [CONFIG_SCHEMA.md](CONFIG_SCHEMA.md).
> Содержит перечень shared parameters, используемых плагином, их назначение, типы и области биндинга.

---

## 1. Источник параметров

Все параметры плагина — **Shared Parameters**, описаны в файле
`src/RevitPlugin/Resources/SharedParameters.txt`.

При первом запуске плагин:
1. Проверяет наличие параметров в проекте.
2. Если параметра нет — добавляет его из своего Shared Parameter File и биндит к соответствующим категориям.
3. Существующие параметры (с тем же GUID) не перебиндивает.

Каждый параметр имеет:
- **Имя** — стабильное, начинается с префикса `WRS_`.
- **GUID** — фиксированный (см. таблицу ниже).
- **Тип** — Text / Integer / Length / YesNo и т. п.
- **Группа в свойствах** — `WRS — Wall Reinforcement`.
- **Категории** — `Structural Rebar` (основная), `Walls`, `Views`, `Sheets` (для специфических).
- **Type / Instance**.

---

## 2. Параметры арматуры (Structural Rebar)

| Имя | GUID | Тип | T/I | Назначение |
|---|---|---|---|---|
| `WRS_Config_Id` | `c1b6...0001` | Text | Instance | UUID конфига, по которому создан стержень |
| `WRS_Config_Version` | `c1b6...0002` | Integer | Instance | Версия конфига (`version`) на момент создания |
| `WRS_Rule_Id` | `c1b6...0003` | Text | Instance | Идентификатор правила (например, `wrs.external_mesh`) |
| `WRS_Bar_Role` | `c1b6...0004` | Text | Instance | Роль: `Vertical`, `Horizontal`, `Edge`, `Diagonal`, `Stirrup`, `Dowel`, `Corner`, `Opening`, `Custom` |
| `WRS_Bar_Position` | `c1b6...0005` | Text | Instance | Позиция в маркировке (например, `1`, `2`, `К1`) |
| `WRS_Bar_Group` | `c1b6...0006` | Text | Instance | Логическая группа (для сортировки на видах) |
| `WRS_Wall_Mark` | `c1b6...0010` | Text | Instance | Mark стены-хоста (копия) |
| `WRS_Wall_Type` | `c1b6...0011` | Text | Instance | Type name стены-хоста |
| `WRS_Wall_Tier` | `c1b6...0012` | Text | Instance | Ярус |
| `WRS_Wall_Section` | `c1b6...0013` | Text | Instance | Секция |
| `WRS_Wall_Level` | `c1b6...0014` | Text | Instance | Имя уровня стены |
| `WRS_Wall_Block` | `c1b6...0015` | Text | Instance | Корпус / блок |
| `WRS_Run_Stamp` | `c1b6...0020` | Text | Instance | Timestamp последнего запуска плагина |
| `WRS_Job_Id` | `c1b6...0021` | Text | Instance | GUID операции (для трассировки) |
| `WRS_Modified` | `c1b6...0030` | YesNo | Instance | Помечен ли стержень пользователем как «не пересоздавать» |
| `WRS_Bar_Tag` | `c1b6...0040` | Text | Instance | Готовый ярлык для тегов на видах (`{Mark}_{Position}`) |

GUID — заглушка-шаблон; реальные значения зафиксированы в `SharedParameters.txt` плагина и не меняются между релизами.

---

## 3. Параметры стены (Walls)

| Имя | Тип | T/I | Назначение |
|---|---|---|---|
| `WRS_Config_Id` | Text | Instance | Конфиг, к которому привязана стена |
| `WRS_Wall_Mark` (использует системный `Mark`) | — | — | Существующий `Mark` стены — основа |
| `WRS_Wall_Tier` | Text | Instance | Ярус |
| `WRS_Wall_Section` | Text | Instance | Секция |
| `WRS_Wall_Block` | Text | Instance | Блок / корпус |
| `WRS_Last_Run` | Text | Instance | Timestamp последнего запуска |
| `WRS_Rule_Hash` | Text | Instance | Хеш конфига на момент последнего запуска (для определения «stale») |

Если у пользователя уже есть свои параметры с теми же смыслами (другое имя), они подключаются через `parameter_mapping.from_wall` в конфиге — плагин не требует переименовывать.

---

## 4. Параметры видов (Views)

| Имя | Тип | T/I | Назначение |
|---|---|---|---|
| `WRS_View_Type` | Text | Instance | `Section` / `Elevation` / `DetailNode` / `Check3D` |
| `WRS_Wall_Mark` | Text | Instance | Стена, к которой относится вид |
| `WRS_Config_Id` | Text | Instance | Конфиг, по которому вид создан |
| `WRS_Run_Stamp` | Text | Instance | Когда создан |

Эти параметры позволяют группировать виды в Project Browser и фильтровать на листах.

---

## 5. Параметры спецификаций (Schedules / Views)

Спецификации `ViewSchedule` принципиально не имеют instance-параметров плагина — но в их полях используются параметры арматуры из §2. Дополнительно для управления:

| Имя | Тип | T/I | Назначение |
|---|---|---|---|
| `WRS_Schedule_Template_Id` | Text | Instance | id шаблона спецификации, из которого она создана |

---

## 6. Маппинг (упрощённо)

| Источник | → | Параметр на стержне |
|---|---|---|
| Wall.Mark | → | WRS_Wall_Mark |
| Wall.Type.Name | → | WRS_Wall_Type |
| Wall.Level.Name | → | WRS_Wall_Level |
| Wall.WRS_Wall_Tier | → | WRS_Wall_Tier |
| Wall.WRS_Wall_Section | → | WRS_Wall_Section |
| RebarRule.Id | → | WRS_Rule_Id |
| RebarRule.Role | → | WRS_Bar_Role |
| RebarRule.Position | → | WRS_Bar_Position |
| Config.Id | → | WRS_Config_Id |
| Config.Version | → | WRS_Config_Version |
| Job.Timestamp | → | WRS_Run_Stamp |
| Job.Id | → | WRS_Job_Id |

Дополнительно через `parameter_mapping.computed` можно собирать вычисляемые поля, например `WRS_Bar_Tag = "{wall.Mark}-{rule.Position}"`.

---

## 7. Сценарии использования параметров

### 7.1 Фильтрация на виде
- На виде создаётся `View Filter` с правилом `WRS_Config_Id = <config-uuid>` И `WRS_Wall_Mark = "СТ-1"`.
- Управляет видимостью / цветом арматуры конкретной стены.

### 7.2 Группировка в спецификациях
- `Group by WRS_Wall_Mark`.
- `Then by Diameter`.
- Подсчёт итогов: масса, длина.

### 7.3 Маркировка
- Тег арматуры показывает `WRS_Bar_Tag` (или `WRS_Bar_Position` + `Mark`).

### 7.4 Поиск «осиротевшей» арматуры
- Фильтр: `WRS_Config_Id != null AND hostWall.WRS_Config_Id == null`.
- Плагин предлагает Delete или Re-link.

---

## 8. Соглашения по именам

- Префикс `WRS_` — все параметры плагина.
- Имена в snake_case_camel — например `WRS_Wall_Mark`.
- В русскоязычных проектах headers в спецификациях задаются через локализуемые заголовки полей, а не через имя параметра.

---

## 9. Совместимость со сторонними параметрами

Плагин **не требует** удалять существующие пользовательские параметры. Если у пользователя уже есть, например, `Марка стены` вместо `WRS_Wall_Mark`, маппинг можно настроить в конфиге:

```jsonc
"parameter_mapping": {
  "from_wall": {
    "Марка стены": "WRS_Wall_Mark"
  }
}
```

Внутренняя бухгалтерия плагина (`WRS_Config_Id`, `WRS_Rule_Id`, `WRS_Run_Stamp`) — обязательны и не подменяются.

---

## 10. Связанные документы

- [CONFIG_SCHEMA.md](CONFIG_SCHEMA.md) — §5 «Маппинг параметров».
- [MODULES.md](MODULES.md) — §M3.
