# RevitActionRecorder

Аддин для Autodesk Revit 2025/2026, который полностью фиксирует работу инженера в машиночитаемом JSON: поток событий, полные снапшоты состояния модели «до»/«после» и структурированный дифф между ними. Назначение — чтобы LLM-агент мог восстановить методику работы и воспроизвести её.

Полное ТЗ: [RevitActionRecorder_prompt.md](RevitActionRecorder_prompt.md).

## Статус этапов

| Этап | Содержание | Статус |
|---|---|---|
| 1 | Каркас: solution, конфигурации R2025/R2026, лента (вкладка Smart Tools, панель Action Recorder), манифесты, installer | ✅ |
| 2 | Поток событий `events.jsonl`: все подписки, корреляция команд, undo/redo с пометкой `undone` | ✅ |
| 3 | Снапшот: экземпляры, типы, все параметры, bbox/location, SHA-256 хеш геометрии, потоковый gzip | ✅ |
| 4 | Оформительский слой: виды, переопределения (категории/элементы/фильтры), стили объектов, ресурсы, листы, спецификации, настройки проекта, предупреждения | ✅ |
| 5 | Теневой кэш параметров и дельты `{param, from, to}` в `doc_changed` | ✅ |
| 6 | Дифф-движок (structural JSON diff, сопоставление по uid/id) + unit-тесты | ✅ |
| 7 | PNG-экспорт через ExternalEvent (`shots/before|after|marks`), Mark moment с комментарием | ✅ |
| 8 | Окно Settings + `config.json`, README | ✅ |

Проверено вживую в Revit 2025 (модель 144 234 элемента / 558 видов и малая тестовая): снапшоты, дельты
параметров, `command`-события от ленты, undo/redo с пометкой `undone`, failures, диалоги, PNG, дифф.

## Замеры и поведение на реальной модели

| Параметр | Малая модель (5.9k элементов, 126 видов) | Реальная (144k элементов, 558 видов) |
|---|---|---|
| Снапшот | 22 с, 0.9 МБ (gz) | 442 с, 21 МБ (gz) → 307 МБ распакованного |
| Дифф пары снапшотов | < 1 с | 15 с, пик памяти 106 МБ |

Дифф идёт потоком в три прохода (хеши «до» → классификация «после» → детализация только изменённых),
поэтому память не зависит от размера модели: полное дерево JSON в памяти не строится.

## Структура решения

```
RevitActionRecorder.sln
  src/
    RevitActionRecorder/            — аддин (зависит от Revit API)
      Application/                  — IExternalApplication, лента, команды
      Recording/                    — (этап 2) подписки на события, EventBus, JSONL writer
      Snapshot/                     — (этап 3–4) обход модели, экстракторы
      Imaging/                      — (этап 7) экспорт PNG через ExternalEvent
      Configuration/                — (этап 8) чтение/запись config.json
      Infrastructure/               — логирование, пути, безопасные обёртки
    RevitActionRecorder.Model/      — POCO-модели JSON-схемы, БЕЗ ссылок на Revit API
    RevitActionRecorder.Diff/       — движок сравнения снапшотов, БЕЗ ссылок на Revit API
  tests/
    RevitActionRecorder.Diff.Tests/ — xUnit, чистые unit-тесты диффа
  deploy/
    RevitActionRecorder.2025.addin
    RevitActionRecorder.2026.addin
    install.ps1
```

## Сборка

Требуется .NET SDK 8. Четыре конфигурации: `Debug R2025`, `Release R2025`, `Debug R2026`, `Release R2026`. Константы условной компиляции: `REVIT2025` / `REVIT2026`. Revit API подключается NuGet-пакетами `Nice3point.Revit.Api.*` (`2025.*` / `2026.*`) с `ExcludeAssets="runtime"` — сборки Revit не копируются в выход.

```powershell
dotnet build RevitActionRecorder.sln -c "Debug R2025"
dotnet build RevitActionRecorder.sln -c "Release R2025"
dotnet build RevitActionRecorder.sln -c "Debug R2026"
dotnet build RevitActionRecorder.sln -c "Release R2026"
```

Unit-тесты (без Revit):

```powershell
dotnet test RevitActionRecorder.sln -c "Debug R2025"
```

Выход аддина: `src\RevitActionRecorder\bin\<Configuration> R<год>\RevitActionRecorder.dll`.

## Установка

```powershell
powershell -ExecutionPolicy Bypass -File deploy\install.ps1 -Configuration Debug -RevitVersion 2025
```

- `-RevitVersion 2025,2026` — под какие версии ставить (по умолчанию обе).
- `-Configuration Debug|Release` — какой билд копировать (по умолчанию Release).
- `-Uninstall` — удалить аддин.

Скрипт копирует сборку в `%APPDATA%\Autodesk\Revit\Addins\<год>\RevitActionRecorder\` и манифест `RevitActionRecorder.addin` рядом. Revit подхватывает аддин при следующем запуске: вкладка **Smart Tools**, панель **Action Recorder** (Start/Stop recording, Snapshot now, Mark moment, Open session folder, Settings).

## Отладка

1. Собери `Debug R2025` и установи (`install.ps1 -Configuration Debug -RevitVersion 2025`).
2. Запусти Revit как внешнюю программу: в `src/RevitActionRecorder/Properties/launchSettings.json` уже есть профили «Revit 2025» / «Revit 2026» (VS/Rider: Start external program), либо запусти Revit вручную и сделай Debug → Attach to Process → `Revit.exe`.
3. Служебный лог аддина (ошибки старта и обработчиков): `%LOCALAPPDATA%\RevitActionRecorder\addin.log`.

Примечание: начиная с Revit 2025 аддины работают на .NET 8; после замены DLL нужен перезапуск Revit (сборки остаются загруженными).

## Формат выходных данных

Каждая сессия записи — папка `<OutputRoot>/<ИмяМодели>/session_<дата>_<время>/` (OutputRoot по умолчанию — `Documents\RevitActionRecorder`, меняется в Settings):

```
manifest.json          — версия Revit, сборка аддина, схема, модель, пользователь, машина, времена, счётчики событий
snapshot_before.json.gz / snapshot_after.json.gz / snapshot_mid_NN.json.gz
diff.json              — структурированный дифф «до»/«после» (считается в фоне после Stop)
events.jsonl           — поток событий, одна строка JSON на событие
shots/before|after|marks/*.png
log.txt                — служебный лог сессии
```

Каждый файл начинается с поля `"schema": 1`.

### events.jsonl

Конверт каждой строки: `schema, seq, t, kind` + секции по виду события (null-поля опущены). Виды:
`session_start`, `command` (нажатие кнопки ленты: `cmd.id`, `cmd.title`), `doc_changed`, `failures`, `dialog`,
`view_activated`, `selection_changed`, `doc_opened|doc_closing|doc_saved|doc_saved_as|doc_synced|doc_printed`,
`snapshot` (промежуточный), `mark` (комментарий пользователя), `session_stop` (счётчики).

`doc_changed`: `op` (UndoOperation), `undone` (проставляется true при финализации, если транзакция отменена
и не возвращена redo), `txn` (имена транзакций), `cmd` (скоррелированная команда + `ageMs` — сколько прошло
от нажатия кнопки; команда «липкая», т.к. один инструмент Revit порождает серию транзакций), `view`,
`added[]`/`modified[]` (карточки элементов: id, uid, cat, cls, family, type, level, workset),
`deleted[]` (id), у modified — `params[]` с дельтами `{name, bip, from, to}` из теневого кэша.
`undoneSeqs`/`redoneSeqs` — какие события отменил/вернул этот undo/redo-шаг.
События чужих документов: `doc_changed`/`selection_changed` игнорируются, жизненный цикл документов и
`view_activated` пишутся с флагом `foreign: true`.

### snapshot_*.json.gz

Один JSON-объект, потоково записанный (gzip отключается в Settings):
`model`, `project` (info, units по всем измеримым spec'ам, levels, grids, phases, worksets, designOptions,
links + linkInstances, position, globalParams, printSettings), `categories` (стили объектов),
`resources` (linePatterns, fillPatterns, materials, appearanceAssets), `types[]` и `instances[]`
(каждый элемент: id/uid/cat/cls/family/type/level/workset/designOption/phase/group/assembly,
все параметры `{pid, name, bip, guid, storage, dataType, raw, display, ro, shared}`, bbox, loc,
`geomHash` — SHA-256 тесселированной геометрии), `views[]` (свойства вида, cropBox, viewRange,
orientation3d, категорийные/элементные/фильтровые переопределения — только не-дефолтные),
`sheets[]` (основная надпись, ревизии, viewports с позициями), `schedules[]` (поля, фильтры,
сортировка/группировка), `legends[]`, `warnings[]`.

Примечания: TextNoteType/DimensionType/типы марок — это ElementType'ы, они лежат в `types[]` со всеми
параметрами; переопределения записываются только заданные (дефолты опущены — отсутствие ключа = дефолт).

Поэлементные переопределения (`GetElementOverrides` по каждому элементу вида) по умолчанию **выключены**:
их стоимость O(виды × элементы), на 558 видах реальной модели это часы. Включается в Settings.
`Document.PrintManager` не читается намеренно — обращение к нему заставляет Revit показать модальный
диалог драйвера печати, а аддин не имеет права порождать диалоги.

### diff.json

`sections.{types|instances|views|sheets|schedules}` → `added[]/removed[]/modified[]`
(modified: карточка + `changes[{path, from, to}]`, путь вида `params[pid:123].raw`),
`summary` со счётчиками и разбивкой по категориям, `project`/`categories`/`resources` —
плоские списки `{path, from, to}`, `warnings` — added/removed по описанию+элементам.
Сопоставление элементов: uid, затем id.

## Жёсткие инварианты

- Аддин **никогда не модифицирует документ** — ни одной транзакции на запись.
- Весь Revit API — только в главном потоке; в фон отдаются только уже извлечённые POCO/строки.
- Каждый обработчик события — целиком в try/catch; ошибки в лог, наружу не пробрасываются.
- Никаких сетевых обращений и телеметрии.
