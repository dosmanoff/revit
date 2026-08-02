# RebarGeneration — пакетная генерация арматуры

*Проектное решение: как уйти от «час на 500–1000 стержней» к «десятки секунд», и от ~500 k токенов к ~10 k. Составлено 2026-08-02 по коду `revit-context.extension` и плагинов `SlabReinforcement` / `WallReinforcement` / `ColumnReinforcement`.*

---

## 1. Где на самом деле уходит час

Разбор текущего пути `create_rebar` (агент → MCP → pyRevit Routes → Revit). Все ссылки — на живой код.

| # | Стоимость на **каждый** стержень | Где | Порядок |
| --- | --- | --- | --- |
| **1** | **Полный round-trip LLM**: агент печатает координаты сегментов, ждёт, читает результат | сам протокол | **3–10 с × N** |
| 2 | `find_by_key` — коллектор по **всем** `OST_Rebar` + `read_key` (LookupParameter + regex по Comments) на каждом | `actions/keys.py:79` | **O(N²)** |
| 3 | `_resolve_first` ×3 — полный коллектор по `RebarBarType`, `RebarHookType`, `RebarHookType` | `actions/ops.py:369` | O(N·T) |
| 4 | `_summary_fp` — `element_dto` summary: bbox + type_info + placement + relationships + params | `actions/ops.py:45` | ~100 interop-вызовов |
| 5 | **Отдельная `Transaction` + Commit** (регенерация, undo-запись, пересчёт видов) | `actions/txn.py:32` | 20–200 мс |
| 6 | Журнал: `load()` → `append` → `save()` **всего** JSON заново | `actions/journal.py:94` | **O(N²)** по I/O |
| 7 | IronPython-интероп вместо C# | вся ветка | ×10–30 к C# |

Плюс два множителя протокола:
- `dry_run=True` перед записью (так предписывает навык `rebar-create-verify`) — **×2 round-trip'а**;
- каждый ответ оседает в контексте → окно растёт, каждый следующий ход дороже.

**Замер, а не оценка.** Журнал мутаций `%APPDATA%\RevitPlugin\context\sessions\` по модели Terrace View:

| Журнал | Записей | Из них `create_rebar` | Непрерывный отрезок | Темп |
| --- | --- | --- | --- | --- |
| `…f7b2f4c3.json` | 1647 | **1647** | 5685 с | **3.45 с/стержень** |
| `…3625c008.json` | 5421 | **5411** | 2 суток (с перерывами) | — |

**7058 поштучных вызовов `create_rebar`.** 1647 стержней = 1 ч 35 мин непрерывной работы. Это и есть «час на 1000 стержней» — 1000 × 3.45 с ≈ 57 минут. Пункты 2–7 добавляют сверху, но **доминирует пункт 1**: LLM стоит внутри цикла.

> **Отдельно:** если во время генерации включён `RevitActionRecorder`, каждая из 1000 транзакций поднимает `DocumentChanged` с построением per-element карточек и дельт параметров из shadow-cache. На массовой генерации это ощутимый множитель — **запись выключать на время batch-прогона**.

**Главный вывод.** Оптимизировать `create_rebar` бесполезно — даже если сделать его мгновенным, останется 83 минуты на переписку. Надо **вынести цикл из агента**.

---

## 2. Целевая архитектура

Принцип: **агент пишет намерение, плагин разворачивает геометрию, Revit трогаем один раз.**

```
       ┌───────────────────────────────────────────────────────────┐
       │ A. INTENT  —  rebar-plan.json   (агент, 2–10 KB, в git)    │
       │    хосты, марки, шаг, защ. слой, крюки, нахлёсты, зоны     │
       └────────────────────────┬──────────────────────────────────┘
                                │  один вызов, путь к файлу
       ┌────────────────────────▼──────────────────────────────────┐
       │ B. COMPILER  —  детерминированный, БЕЗ LLM                 │
       │    intent + геометрия хоста → таблица наборов/стержней     │
       │    ── может работать и вне Revit (превью на dump'е) ──     │
       └────────────────────────┬──────────────────────────────────┘
                                │  bar table (in-memory)
       ┌────────────────────────▼──────────────────────────────────┐
       │ C. APPLIER  —  C#, ОДНА транзакция на хост                │
       │    кэши типов, key-индекс, rebar SETS, warnings suppressed │
       └────────────────────────┬──────────────────────────────────┘
                                │  compact report + key→id map (в файл)
       ┌────────────────────────▼──────────────────────────────────┐
       │ D. GATE  —  агрегаты, не DTO: counts, clashes, hash        │
       └───────────────────────────────────────────────────────────┘
```

Агент участвует **только в A и D**. Между ними — детерминированный код, который можно тестировать юнит-тестами без Revit.

### Ключевое отличие от сегодняшнего
Сегодня `key` адресует **один стержень**, и агент придумывает 1000 ключей. В целевой схеме `key` адресует **набор** (`SLAB:F2:BY:L2` — весь нижний слой по Y), а раскладка внутри набора — забота компилятора. Идемпотентность сохраняется, но на 1–2 порядка меньшем числе объектов.

---

## 3. Рычаги ускорения и их вклад

| # | Рычаг | Что даёт |
| --- | --- | --- |
| 1 | **Один вызов на весь план** вместо вызова на стержень — LLM вне цикла | **×500–1000** по времени и токенам |
| 2 | **Rebar SETS** (`SetLayoutAsNumberWithSpacing`) вместо отдельных стержней: 1000 стержней → 20–40 элементов | ×25–50 по числу элементов, весу модели, регенерации, спецификациям |
| 3 | **Одна транзакция** (или одна на хост) вместо 1000 | ×10–100 по стоимости коммитов |
| 4 | **Кэши**: типы стержней/крюков/форм — один раз; `key→ElementId` — один проход коллектора | снимает квадратичность (п. 2, 3 раздела 1) |
| 5 | **C# вместо IronPython** на горячем цикле | ×10–30 на интеропе |
| 6 | Журнал — **append-only JSONL**, одна запись на пакет | снимает O(N²) по I/O |
| 7 | `FailuresPreprocessor` + `SetDelayedMiniWarnings(true)`, активный вид — пустой drafting на время прогона | ×2–5 (замерить) |
| 8 | Отчёт — **агрегаты**, `key→id` карта пишется в файл, а не в контекст | ×10–100 по токенам на верификации |

**Ожидаемо:** час → **20–60 секунд**; ~500 k токенов → **~10–20 k**. Рычаг 1 даёт целевые 50–100× сам по себе; 2–7 нужны, чтобы Revit-сторона не стала новым узким местом и чтобы модель осталась лёгкой.

---

## 4. Контракт `rebar-plan.json`

Единицы — **мм** (как в `contracts/model-intent.json`), конвертация в футы только в applier'е. Схема — набросок, не финал.

```jsonc
{
  "schema": "rebar-plan-1.0",
  "document": "095-Terrace.rvt",
  "defaults": { "cover": 40, "barType": "#5", "lapClass": "B", "units": "mm" },

  "groups": [
    {
      "key": "SLAB:F2:BOTTOM:Y",          // адресует НАБОР, не стержень
      "host": { "by": "mark", "value": "F2-SLAB" },   // или {"by":"id","value":123456}
      "kind": "field",                    // field | edge | trim | group | dowel
      "layer": "bottom",  "direction": "y",
      "barType": "#5",  "spacing": 300,
      "extent": { "from": "edge", "to": "edge", "offset": 0 },
      "hooks": { "start": null, "end": null },
      "maxBarLength": 12000,  "lap": "auto",
      "layout": "set"                     // set | bars | area
    },
    {
      "key": "FDN:C6WF30:TIES",
      "host": { "by": "key", "value": "FDN:C6WF30" },
      "kind": "group",
      "shape": "T9", "barType": "#4",
      "spacing": 250, "count": 14,
      "hooks": { "start": "135", "end": "90" }
    }
  ],

  "policy": {
    "onExistingKey": "replace",           // skip | replace | error
    "reuseStandardShapes": true,
    "suppressWarnings": true
  }
}
```

Свойства, которые делают это дешёвым:
- **Компактность**: 20–40 групп вместо 1000 наборов координат. Агент пишет план обычным `Write`, а не через MCP.
- **Диффабельность**: план в git, правка — это дифф плана, а не перегенерация переписки.
- **Детерминизм**: тот же план + та же модель = тот же результат. Повтор идемпотентен по `key`.
- **Проверяемость вне Revit**: компилятор на dump'е геометрии (`SlabDump`, `ExportContext`) выдаёт таблицу стержней, которую можно проверить на защитный слой / нахлёсты / коллизии **до** касания модели.

---

## 5. Плагин: API и модель выполнения

Новый add-in **`RebarGeneration`** (Revit 2025/2026, .NET 8) — по образцу `RevitActionRecorder`.

### Эндпоинты (через pyRevit Routes или сокет-команду MCP)

| Метод | Назначение | Ответ |
| --- | --- | --- |
| `POST /rebar/compile` | план + модель → таблица наборов, **без записи** | сводка: групп, наборов, стержней, суммарная длина, предупреждения |
| `POST /rebar/apply` | применить план | `{ jobId }` **сразу** |
| `GET /rebar/job/{id}` | прогресс/результат | `{ state, done, total, report }` |
| `GET /rebar/gate` | верификация после применения | `{ passed, counts, violations[] }` |

**Job-модель обязательна**: `httpx` в MCP-мосте стоит с `TIMEOUT=120` (`revit_context_mcp.py:24`), а прогон в 40 с на большой модели легко уползёт за минуту. Возврат `jobId` + опрос снимает и таймаут, и блокировку сессии агента.

### Внутри applier'а

```csharp
using var group = new TransactionGroup(doc, "Rebar plan apply");
group.Start();

var barTypes  = Cache.ByName<RebarBarType>(doc);    // один коллектор
var hookTypes = Cache.ByName<RebarHookType>(doc);
var keyIndex  = KeyIndex.Build(doc);                // один проход → Dictionary<string, ElementId>

foreach (var host in plan.HostsGroupedByElement())
{
    using var tx = new Transaction(doc, $"Rebar {host.Mark}");
    var opts = tx.GetFailureHandlingOptions();
    opts.SetFailuresPreprocessor(new SwallowWarnings());
    opts.SetDelayedMiniWarnings(true);
    opts.SetForcedModalHandling(false);
    tx.SetFailureHandlingOptions(opts);
    tx.Start();

    foreach (var band in compiler.Expand(host))      // геометрия — уже посчитана
    {
        var set = Rebar.CreateFromCurves(...);
        set.GetShapeDrivenAccessor()
           .SetLayoutAsNumberWithSpacing(band.Count, band.Spacing, true, true, true);
        KeyIndex.Write(set, band.Key);
    }
    tx.Commit();
}
group.Assimilate();
```

Отдельно:
- **`ExternalEvent`** — Routes-обработчик не в API-контексте (`txn.py:44` уже ловит этот случай); запись должна идти через очередь внешнего события.
- **Активный вид** — на время прогона переключить на пустой drafting-вид: каждый коммит иначе тянет регенерацию видимых видов с арматурой. Замерить — потенциально самый недооценённый множитель.
- **`SetUnobscuredInView` / presentation mode** — применять **пакетно после** генерации, по видам, а не по стержням (правило 5 из `revit-mcp-best-practices.md`).
- **Отчёт** — `key→ElementId` пишется в `%APPDATA%\RevitPlugin\context\plans\<plan>-map.json`. Агенту возвращается только сводка. Карта нужна документации (группа 3), не контексту.

---

## 6. Что переиспользуем, что пишем

**Уже готово и является ровно тем, что нужно** — трогать не надо:
- `SlabReinforcement/Engine/*` — `FieldSetBuilder`, `GroupBuilder`, `EdgeTreatmentBuilder`, `OpeningTrimBuilder`, `RebarFactory`, `SlabReinforcer` (уже `TransactionGroup` + транзакция на плиту, уже `SetLayoutAsNumberWithSpacing`);
- `WallReinforcement`, `ColumnReinforcement` — те же паттерны для стен и колонн;
- `SlabDump` / `ExportContext` — геометрия хоста для компилятора;
- `checks.py` — гейт.

**Чего не хватает — это и есть весь объём работы:**

### Безголовая дверь уже открыта — её просто не использовали

Проверено рефлексией через `/exec` на живом сеансе Revit (2026-08-02):

- **Все плагины уже загружены** в процесс Revit: `SlabReinforcement`, `WallReinforcement`, `ColumnReinforcement`, `SmartViews`, `SlabRebar`. Тип `SlabReinforcement.Engine.SlabReinforcer` резолвится по имени **без** `clr.AddReference`.
- Движок pyRevit — IronPython 2.7.12 **на .NET 8**, то есть интероп с .NET 8-сборками плагинов работает.
- Публичные, UI-free, с встроенным `dryRun` (откатывает всё):

```
SlabReinforcement.Engine.SlabReinforcer
    RunResult Run(IDictionary perSlab, IReadOnlyList zones, bool dryRun,
                  IReadOnlyDictionary briefs, UnitSystem briefUnits)
WallReinforcement.Engine.WallReinforcer
    RunResult Run(IEnumerable wallIds, ReinforcementConfig cfg, bool dryRun)
    RunResult Run(IReadOnlyDictionary perWall, bool dryRun)
ColumnReinforcement.Engine.ColumnReinforcer
    RunResult Run(IEnumerable columnIds, ColumnReinforcementConfig cfg, bool dryRun)
    RunResult Run(IDictionary perColumn, bool dryRun)
```

Плюс загрузчики JSON: `BriefLoader.Load/Parse`, `ConfigLoader.Load`.

**Следствие: ни нового плагина, ни пересборки не требуется.** Агент пишет конфиг JSON и вызывает готовый оттестированный движок из `/exec` в 5 строк. Ровно то, что вручную через кнопку ленты уже работает «моментально».

| Пробел | Решение | Объём |
| --- | --- | --- |
| Агент не знал, что движки достижимы из `/exec` | правило в `CLAUDE.md` + навык `rebar-create-verify` | **сделано** |
| `SlabReinforcer.Run` требует собрать `perSlab`/`briefs` — логика в `GenerateSlabRebarCommand.FromBrief` | вынести в `public static SlabApi.RunFromBrief(doc, briefPath, dryRun)` | ~40 строк |
| Фундаменты/ростверки своего движка не имеют | либо скрипт по шаблону навыка, либо новый движок по образцу Slab | по потребности |
| `find_by_key` квадратичен | `KeyIndex` — один проход; параметр-фильтр по `CTX_Key` | ~20 строк |
| Журнал O(N²) + `/exec` в него не пишет | JSONL, одна запись на пакет; запись из `/exec` | ~30 строк |

Иными словами: **новый движок арматуры писать не нужно, и безголовую дверь строить тоже не нужно.** Нужно ею пользоваться и залатать три мелких шва.

---

## 7. План внедрения

Порядок пересмотрен после находки из §6: сначала пользуемся тем, что есть, и только потом что-то пишем.

**Фаза 0 — правила. Сделано.**
`CLAUDE.md` + `AGENTS.md` + навык `rebar-create-verify` переписаны: массовая запись — только пакетом, поштучный цикл запрещён. Действует с **новой** сессии; уже запущенные держат старые инструкции в контексте.

**Фаза 1 — вызывать готовые движки из `/exec`. Кода ноль.**
Для плит/стен/колонн: конфиг JSON → `ConfigLoader.Load` → `Reinforcer.Run(ids, cfg, dryRun)`. Сначала `dryRun=true` (сам откатывается), потом `false`. Это тот самый путь, который через ленту уже работает моментально. Проверить на одном хосте и записать замер.

**Фаза 2 — три мелких шва** (таблица в §6): `SlabApi.RunFromBrief`, `KeyIndex`, журналирование `/exec`. Дни, не недели.

**Фаза 3 — то, чего движка нет.**
Фундаменты/ростверки Terrace View — по шаблону из навыка (скрипт, наборы, одна транзакция). Если объём повторяется — оформить движком по образцу `SlabReinforcement`.

**Фаза 4 — `rebar-plan.json` и диспетчер.**
Единое намерение поверх трёх движков + гейт на агрегатах. Оправдано, только когда фазы 1–3 упрутся в разнородность конфигов. Не начинать раньше.

---

## 8. Риски и ограничения

- **Наборы применимы не везде.** Набор требует одинаковой формы всех стержней. Ступенчатый фундамент, скошенная плита, переменная длина — либо разбивка на несколько наборов (компилятор это делает: `FieldSetBuilder` уже группирует в полосы), либо free-form (`RebarFreeFormAccessor`), либо честно поштучно. План должен уметь оба режима — `layout: "set" | "bars"`.
- **`reuseStandardShapes`** ускоряет спецификации, но подбор стандартной формы стоит времени на создании. Сделать переключателем плана и замерить.
- **Ошибка в плане теперь дорогая**, потому что применяется целиком. Отсюда `compile` без записи + гейт на нейтральной геометрии **до** apply, и `TransactionGroup`, откатываемая целиком.
- **Смена активного вида** во время прогона видна пользователю. Приемлемо, если возвращать вид обратно в `finally`; но проверить, что это не ломает `RevitActionRecorder`, если он всё-таки включён.
- **Идемпотентность на уровне набора** меняет семантику `key`. Существующие модели, где ключи адресуют отдельные стержни (и уже ловили `AMBIGUOUS_KEY` на связях колонн Terrace View), потребуют миграции ключей — либо сосуществования двух пространств имён.

---

## 9. Что померить перед фазой 1

Чтобы не оптимизировать вслепую, на одном хосте со ~100 стержнями снять:

1. время `create_rebar` ×100 поштучно (базовая линия);
2. то же в одной транзакции, без `find_by_key`/`_summary_fp`/журнала;
3. то же, но 1 набор вместо 100 стержней;
4. пп. 2–3 при активном пустом drafting-виде;
5. пп. 2–3 с выключенным `RevitActionRecorder`.

Пять чисел дают точные веса рычагов 2–7 и показывают, где остановиться.
