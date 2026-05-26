# ColumnReinforcement — план разработки

> Сопровождает [column-reinforcement-spec.md](column-reinforcement-spec.md). Спека — что строим; этот документ — в каком порядке и какими кусками.

---

## Стратегия

* **Phase 1 (MVP) разбита на 6 PR-кусков по 200–500 строк диффа каждый.** Каждый PR компилируется, запускается и проходит smoke-тест в Revit. Не сливаем «всё разом».
* **Каждый PR — отдельная feature-ветка `claude/column-rebar-NN-<slug>`**, мержится в `main` отдельным merge-commit'ом. Это согласуется с тем, как мерджились `WallReinforcement` PR'ы #13–#15.
* **Ручные smoke-тесты в Revit для каждого PR**, тестовая модель = `test/columns-smoke.rvt` (создаём в PR-01, дальше переиспользуем). Тестовый чек-лист — короткий, в теле PR.
* **Юнит-тесты — только для чистой логики без Revit API**: парсинг конфига, расчёт раскладки стержней по сечению, ACI-калькулятор (Фаза 2). Engine с `Rebar.Create*` не покрываем — тестируем в Revit.

---

## Phase 1 — MVP

Прямоугольные/квадратные колонны, продольная + одиночный внешний tie, без зон сгущения, без выпусков, без группировки. Цель — за ~6 PR'ов получить плагин, который реально применяет typical reinforcement к выбранным колоннам.

### PR-01 — Скелет проекта и кнопка на ленте  ⏱ ~2-3 ч

**Что сделать:**
* Создать `ColumnReinforcement.csproj` (копия `WallReinforcement.csproj`, изменить `AssemblyName`/`RootNamespace`).
* Создать `App.cs` с `IExternalApplication`:
  * `OnStartup`: создать таб «Reinforcement» **если ещё нет** (общий с будущими wall/slab кнопками); кнопка «Column Rebar».
  * `OnShutdown`: пусто.
* Создать `Commands\ColumnReinforcementCommand.cs` с `IExternalCommand` — пока только `TaskDialog.Show("ColumnReinforcement", "Hello")`.
* Создать `Commands\ColumnSelectionFilter.cs` — `ISelectionFilter`, фильтр по `OST_StructuralColumns` + `OST_Columns(Structural=true)`, отсекает наклонные.
* Создать `ColumnReinforcement.addin` (новый GUID — `[guid]::NewGuid()` в PowerShell).
* Создать `samples\` папку с заглушкой `default.json` (содержимое — пустой шаблон конфига).
* Создать `test/columns-smoke.rvt` через Revit вручную: модель с 1 квадратной колонной 16″×16″ × 12′-0″ высотой, плитой снизу, плитой сверху. Закоммитить.

**Smoke-test:** В тестовой модели запустить плагин из кнопки — должен показать TaskDialog.

**Acceptance:** компилится, грузится в Revit, кнопка на ленте кликабельна.

---

### PR-02 — Конфиг и его загрузка  ⏱ ~3-4 ч

**Что сделать:**
* `Config\Length.cs` — копия из `WallReinforcement` (поддержка mm/in/feet-inches с парсингом).
* `Config\ColumnReinforcementConfig.cs` — POCO по структуре из спеки, секция 3 «Phase 1»:
  * `schemaVersion`, `name`, `units`, `cover`, `longitudinal`, `stirrups`.
  * Все `[JsonPropertyName]` на kebab/camel — как в `WallReinforcement`.
* `Config\ConfigLoader.cs` — `LoadFromFolder(string folder)` возвращает `IEnumerable<(string name, ColumnReinforcementConfig cfg)>`. Идентичен по схеме `WallReinforcement\Config\ConfigLoader.cs`.
* `Config\FolderStorage.cs` — `ExtensibleStorage`-schema **с новым GUID** для хранения пути к папке конфигов в документе. Не переиспользовать GUID из `WallReinforcement`.
* `samples\default.json` — реальный Imperial-конфиг под колонну 16″×16″ #8/#4@8″ (как в примере спеки).
* `samples\large-column.json` — пример: 24″×24″, 4×4 #11 продольные, #5@10″ ties.
* **Юнит-тесты** (новый `ColumnReinforcement.Tests` csproj?): загрузка дефолтного JSON, проверка значений после `Length.ToFeet`.

**Smoke-test:** загрузить sample-конфиг через `JsonSerializer.Deserialize` в простой консольной точке.

**Acceptance:** конфиги читаются, юнит-тесты зелёные.

---

### PR-03 — Геометрия колонны и WPF-диалог  ⏱ ~4-6 ч

**Что сделать:**
* `Domain\ColumnGeometry.cs` — извлекает по `FamilyInstance`:
  * `Curve LocationCurve` или ось из bounding box;
  * `Width`, `Depth` — из `Symbol`-параметров `b` и `h` (или `Family` GeometryElement);
  * `Height` — `ZTop - ZBottom`;
  * `Transform LocalBasis` — учёт rotation в плане через `Location.Rotation`.
* `UI\ColumnReinforcementDialog.xaml(.cs)` — WPF-диалог по эскизу из спеки §5:
  * dropdown'ы заполняются из текущей выбранной папки;
  * кнопка `Browse...` для смены папки конфигов;
  * кнопка `New…` создаёт новый JSON с дефолтами;
  * Imperial unit hints (`in`) в labels.
* `Commands\ColumnReinforcementCommand.cs` — теперь:
  * Получает выбор, фильтрует, если пусто — `PickObjects` с фильтром.
  * Показывает диалог.
  * Если `Run` — пока что только `TaskDialog.Show("Selected N columns, config X")` (engine ещё нет).

**Smoke-test:** выбрать 1 колонну, открыть диалог, выбрать конфиг, нажать Run → увидеть подтверждение.

**Acceptance:** UI работает, выбор и фильтр работают, диалог возвращает конфиг.

---

### PR-04 — `RebarFactory` и продольная арматура  ⏱ ~5-7 ч

**Что сделать:**
* `Engine\UnitConv.cs` — `InToFt`, `MmToFt`, `FtToIn`.
* `Engine\RebarFactory.cs`:
  * `GetBarType(Document, string name)` — ищет `RebarBarType` по имени, кидает понятную ошибку если нет.
  * `GetHookType(Document, string name)` — то же для `RebarHookType` (или `null` если name пуст).
  * `CreateStraightVerticalBar(Document, ColumnGeometry, XYZ planXY, RebarBarType, ...)` — `Rebar.CreateFromCurves` с одним отрезком.
  * Все созданные `Rebar` помечаются `Comments = "ColumnReinforcement:{runId}"`.
* `Engine\LongitudinalBarBuilder.cs`:
  * Алгоритм раскладки: 4 угла обязательно; промежуточные — равномерно по ширине/глубине от `barsAlongWidth`/`barsAlongDepth`. Если `cornerOnly=true` — только 4.
  * Использует `LocalBasis` колонны для трансформации точек XY → world.
  * Учитывает `cover.sides` от грани до **оси стержня** (т.е. центр стержня смещён на `cover + d_tie + d_long/2` от грани; но в Фазе 1 — простая привязка `cover + d_long/2`, ties offset учтём в PR-05).
* `Engine\ColumnReinforcer.cs` — orchestrator:
  * `Run(IList<ElementId> columnIds, ColumnReinforcementConfig cfg, bool dryRun) → RunResult`.
  * `TransactionGroup` на всю операцию; `Transaction` на каждую колонну; `Assimilate`/`RollBack`.
  * Заполняет `Domain\RunResult.cs` (счётчики, ошибки per-column).
* `Commands\ColumnReinforcementCommand.cs` — подключает engine, показывает `ResultsDialog`.
* `UI\ResultsDialog.cs` — копия из `WallReinforcement`.

**Smoke-test:** запустить на тестовой колонне с конфигом 3×3 #8 → 8 продольных стержней (4 угла + 4 промежуточных) появляются в правильных позициях, видны в 3D и на разрезе.

**Acceptance:** продольные стержни создаются, dry-run работает (`RollBack` — арматуры не появилось), errors-report показывает понятные сообщения.

---

### PR-05 — Внешний tie (хомут)  ⏱ ~4-6 ч

**Что сделать:**
* `Engine\StirrupBuilder.cs`:
  * Строит **внешний прямоугольный tie** охватом всех угловых продольных стержней.
  * Высоты стержней от `cover.ends` снизу до `Height - cover.ends` сверху, шагом `stirrups.spacing`.
  * Использует `Rebar.CreateFromRebarShape` если найдено стандартное `RebarShape` "M_T1" / "T1" (Tie), иначе fallback на `Rebar.CreateFromCurves` с замкнутой полилинией.
  * Hook type применяется через `RebarShapeDrivenAccessor.SetHookTypeId(end, hookType)` либо при создании.
* `LongitudinalBarBuilder` — обновить расчёт центра стержня: теперь `cover + d_tie + d_long/2`.
* Обновить `ColumnReinforcer` чтобы вызывать `StirrupBuilder` после `LongitudinalBarBuilder`.
* Sample-конфиг `samples\default.json` обновить — `stirrups.enabled: true`.

**Smoke-test:** запустить, в 3D увидеть продольные стержни плюс ленту хомутов с правильным шагом; разрез — хомуты охватывают все 4 угла; крюки на корректном углу.

**Acceptance:** хомуты создаются, шаг соблюдается, крюк корректный, продольные стержни сдвинуты на `d_tie + cover`.

---

### PR-06 — Cleaner и polish  ⏱ ~3-4 ч

**Что сделать:**
* `Engine\ExistingRebarCleaner.cs`:
  * Находит все `Rebar` в host-колонне с `Comments` начинающимся на `ColumnReinforcement:`.
  * Удаляет их в отдельной транзакции **перед** созданием новой арматуры.
  * Управляется опцией в конфиге (`cleanExisting: true/false` — добавить в схему).
* `RunResult` показывает в `ResultsDialog`: сколько колонн обработано, сколько пропущено, сколько ошибок, детали по каждой.
* Сообщения об ошибках — на английском, понятные пользователю (не stack traces).
* `README.md` плагина (короткий — установка, использование, sample-config). **Спросить пользователя**, нужен ли отдельный README или достаточно spec.
* Финальный smoke-test на тестовой модели с 3 разными колоннами:
  * 16″×16″ standard
  * 24″×24″ large с 4×4 #11
  * 12″×16″ rectangular с асимметричной раскладкой (3 wide × 4 deep)
* Видео или серия скриншотов «before/after» в PR.

**Acceptance:** второй запуск удаляет первый, dry-run по-прежнему работает, отчёт читабельный.

**После PR-06 Phase 1 закрыта. Демо пользователю, собрать feedback, планировать Phase 2.**

---

## Phase 2 (расширение)

После приёмки Phase 1. Грубая разбивка, точные размеры PR оценим после feedback с Phase 1:

* **PR-07** — Confinement zones (top/bottom, fixed length or fraction of height).
* **PR-08** — Stirrup offsets from top/bottom faces.
* **PR-09** — Foundation dowels (выпуски снизу), 6 типовых форм, host-detection.
* **PR-10** — Upper splices (выпуски вверх в перекрытие/колонну), 3 варианта.
* **PR-11** — Round columns (longitudinal + circular tie, no spiral).
* **PR-12** — ACI 318 anchorage calculator dialog (`AciAnchorageCalculator`).

---

## Phase 3 (advanced)

* **PR-13** — Multi-floor host column splitting (split longitudinal на стыках уровней).
* **PR-14** — Grouping UI (две таблицы, авто-группировка по сечению).
* **PR-15** — Preferred-shape mapping dialog + `PreferredShapeResolver`.
* **PR-16** — Inner cross-ties (C/S/cross-tie shapes).
* **PR-17** — Spiral reinforcement для круглых колонн.
* **PR-18** — Couplers (mechanical splices).
* **PR-19** — Export/import settings + local storage.

---

## Что нужно от пользователя перед стартом PR-01

Минимальный список (из открытых вопросов спеки §8):

1. **SDC проекта** — Phase 1 закладывает non-seismic типы (90° hooks допустимы). Если все проекты SDC D+ — заменим default hook на 135° seismic ещё в PR-02.
2. **Ленточная вкладка** — общая «Reinforcement» tab или отдельная per-plugin? PR-01 завязан на ответ.
3. **`RebarBarType`/`RebarHookType` lookup** — strict (как `WallReinforcement`) или auto-create? PR-04 завязан.

Остальные вопросы — для Phase 2/3, не блокируют MVP.
