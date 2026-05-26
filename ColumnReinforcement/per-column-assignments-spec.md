# Per-column assignments — design spec (Phase 4)

> Sibling document to [column-reinforcement-spec.md](column-reinforcement-spec.md) and [dev-plan.md](dev-plan.md). Covers the upcoming feature where each column is associated with its own reinforcement config (via a separate Schedule Analyzer tool), and the existing ColumnReinforcement plugin gets a new mode that batch-applies those per-column configs.

---

## 1. Цель

Расширить плагин с режима «один config на selection» до режима «отдельный config на каждую колонну, выбирается автоматически по параметру `Mark`».

**Поток данных:**

```
Structural document (column schedule)
       │
       │ ▼ Schedule Analyzer (отдельный плагин, не в scope этого PR-набора)
       │
   columns.csv  ◄─── one row per Mark, flat fields, all parameters
       │
       │ ▼ user picks the CSV in our dialog (new "From CSV" mode)
       │
   validation table: Mark in CSV ↔ Mark in Revit, size match check
       │
       │ ▼ user reviews warnings, clicks Run
       │
   per-column engine pass, results per column in ResultsDialog
```

---

## 2. Решения, под которые сделан этот документ (2026-05-26)

| Вопрос | Решение |
|---|---|
| Identity column ↔ row | Mark parameter, case-insensitive (`OrdinalIgnoreCase`). One row per Mark. |
| Storage format | CSV (Phase 4a). Excel (.xlsx) — потенциальный Phase 4b если CSV окажется тесным. |
| Где хранится файл | Любое место на диске. Путь к последнему выбранному CSV запоминается в `ProjectInfo` через ExtensibleStorage (тот же паттерн что для папки configs). |
| Mismatch ожидаемого vs реального размера колонны | **Warning**, не error. Engine использует реальную геометрию; пользователь видит расхождение в validation table и в Notes. |
| Колонна в selection без параметра Mark | **Warning + skip**. Note в ResultsDialog объясняет. |
| Колонна с Mark, которого нет в CSV | **Warning + skip + опциональный fallback**. Checkbox «Fall back to JSON config for unassigned» в диалоге; если включен, используется текущий выбранный JSON-config. По умолчанию выключен. |
| ExtensibleStorage на самих колоннах | **Не используется**. Привязка живёт в CSV, не в .rvt. |

---

## 3. Формат CSV-файла

### Базовая структура

* Заголовок — первая строка, точные имена полей ниже.
* Разделитель — запятая. Поля с запятой в значении — в двойных кавычках (стандарт RFC 4180).
* Кодировка UTF-8 (с BOM или без; парсер обрабатывает оба).
* Пустая ячейка ≡ «использовать default из POCO». Это даёт sparse-friendly формат: указывайте только то, что отличается от дефолта.
* Пустые строки и строки начинающиеся с `#` пропускаются (комментарии).

### Колонки

**Identity:**
* `Mark` — string, **обязательно**. Должно совпадать с параметром `Mark` колонны в Revit (case-insensitive).

**Expected geometry (для validation, не используется engine'ом):**
* `ExpectedSection` — `Rectangular` | `Round`
* `ExpectedW` — number, in (для Rectangular)
* `ExpectedD` — number, in (для Rectangular)
* `ExpectedDia` — number, in (для Round)

**Configuration (см. `ColumnReinforcementConfig` для смысла каждого поля):**
* `Units` — `Imperial` | `Metric`
* `CoverSides`, `CoverEnds`
* `LongBarType`, `LongBarsW`, `LongBarsD`, `LongBarsAround`, `LongCornersOnly`, `LongHookTop`, `LongHookBot`
* `StirrupBarType`, `StirrupSpacing`, `StirrupHookType`, `StirrupOffsetTop`, `StirrupOffsetBot`, `StirrupRotate45`
* `ConfBotEnabled`, `ConfBotSpacing`, `ConfBotZoneFraction`, `ConfBotZoneLength`
* `ConfTopEnabled`, `ConfTopSpacing`, `ConfTopZoneFraction`, `ConfTopZoneLength`
* `DowelsEnabled`, `DowelForm`, `DowelBarType`, `DowelExt`, `DowelEmbed`, `DowelLeg`, `DowelOnlyFoundation`, `DowelHookTop`, `DowelHookBot`
* `SplicesEnabled`, `SpliceForm`, `SpliceBarType`, `SpliceLap`, `SpliceExt`, `SpliceBentLeg`, `SpliceUpperInset`, `SpliceCrankedSlope`, `SpliceLowerBendOffset`, `SpliceIgnoreSlabAbove`, `SpliceHookTop`, `SpliceHookBot`
* `CleanExisting`

Длины можно писать как:
- Число (например `1.5` — интерпретируется по `Units`)
- Feet-inches строку (например `1'-6"`)

### Пример

```csv
Mark, ExpectedSection, ExpectedW, ExpectedD, LongBarType, LongBarsW, LongBarsD, StirrupBarType, StirrupSpacing, DowelsEnabled, DowelForm, SplicesEnabled, SpliceForm, SpliceUpperInset
C1.1, Rectangular,     20,        20,        #11,         4,         4,         #5,             8,              true,          L,         true,           Cranked,    2
C1.2, Rectangular,     16,        16,        #8,          3,         3,         #4,             8,              false,         ,          true,           Cranked,    2
C1.3, Rectangular,     12,        12,        #8,          3,         3,         #4,             8,              false,         ,          true,           Bent,
C2.1, Round,           ,          ,          #9,          ,          ,          #4,             8,              true,          L,         true,           Straight,
```

Поля не указанные в этой таблице — например `CoverSides`, `Units`, `StirrupHookType` — берутся из дефолтов POCO (`Imperial`, `1.5"`, `"Stirrup/Tie - 90 deg."` и т.д.).

---

## 4. Архитектура

### Новые файлы

```
ColumnReinforcement/
├── Config/
│   └── AssignmentCsv.cs                # Parser, POCO, sparse-defaults
├── Domain/
│   └── AssignmentTable.cs              # In-memory mapping Mark → cfg + expected geometry
├── UI/
│   └── AssignmentValidationView.cs     # WPF table view of matches/warnings
└── samples/
    ├── column-assignments-basic.csv    # 3 types, no splices/dowels
    ├── column-assignments-full.csv     # complete schedule with splices/dowels/confinement
    └── column-assignments-mixed.csv    # rect + round + size mismatches (for warning demo)
```

### Изменения существующих

**`Engine/ColumnReinforcer.cs`** — новый overload:

```csharp
public RunResult Run(IDictionary<ElementId, ColumnReinforcementConfig> perColumn, bool dryRun);
```

Существующий `Run(ids, oneConfig, dryRun)` остаётся, переписывается как тонкий wrapper, который строит uniform mapping `{ each id → oneConfig }` и вызывает новый.

**`Config/FolderStorage.cs`** — добавить второе поле в schema:

```csharp
public static string? GetCsvPath(Document doc);
public static void   SetCsvPath(Document doc, string path);
```

Та же схема ExtensibleStorage, +1 simple field. **Schema GUID не меняется** — добавление поля совместимо с существующими entity'ами.

**`UI/ColumnReinforcementDialog.cs`** — переключатель режима + новая секция:

* RadioGroup сверху: `Same for all` (текущий) | `From CSV` (новый)
* В `From CSV`-режиме: pickbox для CSV-файла + `Browse…` + `Reload`, ниже — validation table
* В `Same for all` — текущий UI без изменений
* Кнопка `Run` работает в обоих режимах

**`Commands/ColumnReinforcementCommand.cs`** — определяет режим по dialog'у:
- `Same for all` → собирает uniform mapping из `dialog.Config!`
- `From CSV` → собирает per-column mapping из `dialog.Assignments`
- В обоих случаях → вызывает `ColumnReinforcer.Run(mapping, dryRun)`

### `AssignmentCsv` — публичный API

```csharp
public static class AssignmentCsv
{
    public static AssignmentTable Load(string path);
    public static void Save(AssignmentTable t, string path);   // export для round-trip
}

public class AssignmentTable
{
    public IReadOnlyDictionary<string, ColumnReinforcementConfig> ByMark { get; }
    public IReadOnlyDictionary<string, ExpectedGeometry?>         ExpectedByMark { get; }
    public IReadOnlyList<ParseIssue>                              Issues { get; }    // warnings collected during load
}

public record ExpectedGeometry(ColumnSection Section, double WidthIn, double DepthIn);
public record ParseIssue(int LineNumber, string Field, string Message);
```

Ключи — Mark с уже применённым `ToLowerInvariant()`, сравнение тоже case-insensitive.

---

## 5. UX в диалоге

```
┌──────────────────────────────────────────────────────────────┐
│ Column Reinforcement                                          │
├──────────────────────────────────────────────────────────────┤
│ 3 column(s) selected                            Units: [▼ Imperial]
│                                                              │
│  ◯ Same config for all                                       │
│  ● From CSV (per-Mark)                                       │
│                                                              │
│  CSV file: [L:\proj\columns.csv      ] [Browse...] [Reload]  │
│                                                              │
│  ┌────────────────────────────────────────────────────────┐ │
│  │ Mark  │ In CSV │ In Revit │ CSV size │ Revit │ Status  │ │
│  ├───────┼────────┼──────────┼──────────┼───────┼─────────┤ │
│  │ C1.1  │   ✓    │    ✓     │ 20×20    │ 20×20 │ OK      │ │
│  │ C1.2  │   ✓    │    ✓     │ 16×16    │ 18×18 │ ⚠ size  │ │
│  │ C1.3  │   ✓    │    ✗     │ 12×12    │  —    │ ⚠ no-rvt│ │
│  │ C2.5  │   ✗    │    ✓     │   —      │ 16×16 │ ⚠ no-csv│ │
│  └────────────────────────────────────────────────────────┘ │
│                                                              │
│  ☐ Fall back to selected JSON config for unassigned columns │
│  ☐ Dry run (preview only — no rebar will be committed)      │
│                                                              │
│                                  [  Cancel  ]   [  Run  ]    │
└──────────────────────────────────────────────────────────────┘
```

* Validation table заполняется при выборе CSV + при изменении selection. Не «прозванивает» весь проект, только current selection ∪ rows из CSV.
* «Status» ⚠ статусы — warning, ничего не блокирует Run.
* Если выбран `From CSV` но CSV не выбран — `Run` блокируется с подсказкой.
* Pаздел `Same for all` остаётся доступен (просто `From CSV` отжимается); переключение между режимами не теряет введённое в табах для `Same for all`.

---

## 6. Граничные случаи

| Сценарий | Поведение |
|---|---|
| CSV-файл не существует / не читается | Validation table показывает ошибку парсера; `Run` блокируется. |
| Mark в CSV дублируется (две строки с одним Mark) | `AssignmentCsv.Load` оставляет последнюю строку, добавляет `ParseIssue` со списком дубликатов. Все ParseIssue'ы показываются под таблицей. |
| Mark в Revit пустой | Колонна в `Status: ⚠ no-mark`; пропускается. |
| Mark в Revit заполнен, но в CSV нет такой строки | `Status: ⚠ no-csv`; пропускается (или fallback если включен чекбокс). |
| Mark есть в CSV, но в текущем selection нет такой колонны | `Status: ⚠ no-rvt`; ничего не делается (это нормально — CSV покрывает много колонн, selection поменьше). |
| Несовпадение размера CSV vs Revit | `Status: ⚠ size`; engine использует **реальную** геометрию Revit; Note в ResultsDialog "CSV expected 16×16, Revit is 18×18 — using Revit geometry". |
| Несовпадение `ExpectedSection` (CSV говорит Rectangular, Revit Round) | `Status: ⚠ section`; engine использует реальное сечение Revit. Возможно engine откажется создавать (если для Round пытаются использовать `LongBarsW/D`) — попадает в Failed с обычной ошибкой. |
| CSV-строка не парсится (неверное число, неизвестный enum) | `ParseIssue` под таблицей. Соответствующая строка не попадает в `ByMark`; колонны с этим Mark получат status `no-csv`. |
| Dry-run | Per-column sub-Tx роллбэкается как сейчас; результат показывается с правильными counts. |

---

## 7. Следующие шаги — dev plan

Phase 4 — две PR:

### Phase 4 PR-1 — CSV loader + per-column engine mode  ⏱ ~4-5 ч

* `Config/AssignmentCsv.cs` — парсер CSV → `AssignmentTable`. Нет Revit-зависимостей; unit-тестируемо изолированно (плюс несколько простых тестов в `samples/`).
* `Domain/AssignmentTable.cs` — POCO.
* `Engine/ColumnReinforcer.cs` — новый overload `Run(IDictionary<ElementId, ColumnReinforcementConfig>, bool)`. Существующий `Run(ids, oneConfig, dryRun)` становится тонким враппером.
* `Config/FolderStorage.cs` — добавление поля `CsvPath` в существующую schema.
* 3 sample CSV-файла в `samples/`.

Без UI-изменений — диалог не использует новый код в этом PR. Smoke-test: загрузить sample CSV в коде через `AssignmentCsv.Load`, проверить что POCO заполняется корректно.

### Phase 4 PR-2 — Dialog "From CSV" mode + validation table  ⏱ ~5-7 ч

* `UI/AssignmentValidationView.cs` — WPF table контрол.
* Изменения в `ColumnReinforcementDialog`: radio-switch, CSV-pickbox, validation table, "fall back" чекбокс.
* Изменения в `ColumnReinforcementCommand`: ветвление по режиму, сборка mapping, вызов нового overload'а.
* Persistence: путь к CSV в ExtensibleStorage (через PR-1's `FolderStorage.SetCsvPath`).

Smoke-test в Revit: проект с 3-5 колоннами с Mark'ами, sample CSV, проверка table'а и Run.

### После Phase 4

* Schedule Analyzer тул — отдельный плагин, не часть этого репо (?). Использует тот же CSV-формат как контракт.
* Phase 4b — xlsx support (если CSV окажется тесным).
* Phase 4c — round-trip: после Run обновить CSV с фактическими placement counts / per-column status (audit trail).
* Phase 3 (multi-floor splitting, grouping UI, preferred-shapes, spiral, couplers) — параллельно, не зависит.

---

## 8. Открытые вопросы (минимальные)

Решения уже зафиксированы; единственное, что осталось — **где живёт Schedule Analyzer**:

1. Отдельный Revit плагин в этом же репо (например `ColumnScheduleAnalyzer/`)?
2. Внешний CLI/desktop тул вне репо?
3. Часть существующего `ColumnReinforcement`-плагина (например, отдельная кнопка на ленте)?

От ответа зависит только локация будущего кода analyzer'а — на формат CSV и интеграцию с ColumnReinforcement-плагином (это Phase 4) не влияет.

---

## 9. Ссылки

* [column-reinforcement-spec.md](column-reinforcement-spec.md) — исходная спека плагина, Phase 1-3.
* [dev-plan.md](dev-plan.md) — план разработки по фазам.
* [`memory/repo-conventions.md`](file:///C:/Users/Vic/.claude/projects/L--My-Drive-claude-revit/memory/repo-conventions.md) — общие репо-конвенции (US norms, strict lookup, etc.) — на CSV формат и UI не влияют, но применяются к engine.
