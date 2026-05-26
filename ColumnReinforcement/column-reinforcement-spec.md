# ColumnReinforcement — спецификация плагина

> **Источник материала.** Покадровый разбор видеоролика [«ModPlus for Revit. Армирование колонн 2.0»](https://rutube.ru/video/da3eb0da203b892cbfd6eca855ededb1/) (3:58, 24 кадра с шагом 10 с) + официальная документация ModPlus `modplus.org/ru/revitplugins/mprcolumnsreinforcement`.
>
> Спецификация описывает **функционально аналогичный** плагин под Revit 2025 (.NET 8, C#), не клон. **Нормативная база — US (ACI 318-19/25, ASTM A615/A706)**, не СП 63.13330. Архитектурно повторяет соседний плагин `WallReinforcement` (тот же репозиторий) — общие конвенции хранения настроек, диалогов и движка.
>
> **Ключевые находки из видео сверх документации:**
> 1. Глобальный диалог **«Предпочтительные формы арматурных стержней»** — карта «роль → `RebarShape` семейство» с 14 ролями: прямые продольные, Г-образные продольные (при загибах), для выпусков из ФП — Г, Г-с-большим-загибом, П; для поперечного — прямые, С-образные (с крюками-эскизом или с Revit-hooks), S-образные (с крюками-эскизом или с Revit-hooks), треугольные, прямоугольные, круглые, спиралевидные. Значения «Авто» или явное семейство. Спираль использует семейство с параметром радиуса (в ролике — `O_77.rfa`, параметр `ADSK_R`).
> 2. Multi-floor колонны: одна колонна-host проходит сквозь несколько уровней, плагин ищет вышележащую колонну/плиту для расчёта выпусков и длины анкеровки автоматически.
> 3. Опция в зоне сгущения снизу: «**При наличии входящих выпусков установить расстояние сгущения нижней зоны по длине выпусков**» (видно во фрейме 7).
> 4. Превью-окно справа в зоне настроек группы показывает **схему сечения колонны с точками продольных стержней + добавленными формами поперечки + развёрткой по высоте со сгущениями**. Это обязательный визуальный фидбэк, без него UX не работает.

---

## 1. Цель

Плагин для Revit 2025, выполняющий **армирование железобетонных колонн** по группам с одинаковыми настройками:
* анализ выбранных колонн и группировка по сечению/уровню/высоте/параметру;
* создание продольного армирования с настраиваемой раскладкой стержней;
* создание поперечного армирования (хомуты + доп. формы) с зонами сгущения сверху/снизу;
* создание выпусков: dowels вверх (в вышележащую колонну/перекрытие), dowels вниз (из плиты/фундамента);
* сохранение/экспорт/импорт настроек армирования.

Эталон по UX — ModPlus «Армирование колонн». Эталон по технике реализации в этом репозитории — `WallReinforcement`.

**Нормативная база:** ACI 318-19/25, ASTM A615/A706 (марки арматуры), стандартные ACI hook types. Единицы — Imperial по умолчанию (inches/feet), Metric — опционально (через существующий `UnitSystem` enum из `WallReinforcement.Config.Length`).

**ACI-параметры, которые надо знать движку:**
* Минимальный защитный слой (ACI 318 §20.5.1.3): 1.5″ для колонн в помещении (по хомуту), 1.5″ exposed to weather (#5 и крупнее), 2″ cast against earth.
* Минимум хомутов (ACI 318 §25.7.2): не реже чем `min(16·d_long, 48·d_tie, least_column_dim)`. Минимальный размер хомута — #3 при продольной #10 и меньше; #4 при #11 и больше (включая bundled).
* Hook angles (ACI 318 §25.3.2): standard tie hook — 90° или 135°; для seismic — 135° с шестикратным dia extension.
* Lap splice length (ACI 318 §25.5): функция f'c, fy, dia, top-bar factor — реализуется отдельным калькулятором (Фаза 2).
* Confinement length `lo` для SMRF (ACI 318 §18.7.5.1): `max(h_col, 1/6·clear_height, 18″)` — опциональная фича Фазы 2.

---

## 2. Соответствие репозиторным конвенциям

| Аспект | Значение (как в `WallReinforcement`) |
|---|---|
| Платформа | Revit 2025, .NET 8 (`net8.0-windows`), x64 |
| Тип плагина | `IExternalApplication` + `IExternalCommand` |
| API-пакеты | `Nice3point.Revit.Api.RevitAPI` 2025.4.0, `Nice3point.Revit.Api.RevitAPIUI` 2025.4.0 (`ExcludeAssets="runtime"`) |
| UI | WPF-диалог (не Forms), один `TransactionGroup` на запуск, `Assimilate`/`RollBack` по результату |
| Конфиг | JSON, `System.Text.Json`, версионированная схема (`schemaVersion`) |
| Хранение пути к папке конфигов | `ExtensibleStorage` в документе (`FolderStorage`-аналог) |
| `.addin` | `Type="Application"`, уникальный `ClientId` (новый GUID) |
| Образцы | Папка `samples\*.json`, копируется в выход через `CopyToOutputDirectory="PreserveNewest"` |
| Длины | Класс-обёртка `Length` (mm/inches/`"1'-3\""`), `UnitConv`-хелпер. По умолчанию для нового конфига — `UnitSystem.Imperial` → значения трактуются как inches; в API уходят как feet. |
| Bar designations | `#3..#11, #14, #18` по ASTM A615 (dia: 0.375, 0.500, 0.625, 0.750, 0.875, 1.000, 1.128, 1.270, 1.410, 1.693, 2.257 in). В JSON-конфиге как строка `"#5"`. Lookup на `RebarBarType.Name` в документе. |

**Новый `ClientId` (AddInId GUID)** сгенерировать при создании `.addin`-файла — нельзя переиспользовать GUID `WallReinforcement` (см. недавний коммит `WallReinforcement: assign a unique AddInId GUID`).

---

## 3. Объём (фазы)

Полный ModPlus-функционал — это 6 лет инкрементальных релизов. Делим на фазы. **Что входит** — фиксировано здесь; **что не входит** — явно перечислено в конце каждой фазы.

### Фаза 1 (MVP) — прямоугольные/квадратные колонны, базовое армирование

Минимальный жизнеспособный плагин: одна команда, один диалог, прямоугольные/квадратные сечения, продольное + поперечное армирование без сгущения, без выпусков, без группировки.

**Входит:**
* Кнопка на ленте через `IExternalApplication` (вкладка "Reinforcement" или общая для этого репозитория).
* Команда `ColumnReinforcementCommand`:
  * Берёт текущий выбор; если не выбрано — `PickObjects` с фильтром `ColumnSelectionFilter`.
  * Фильтр: `Category == OST_StructuralColumns` **или** `OST_Columns` со `Structural == true`; вертикальные (`IsSlantedColumn == false`); прямоугольный профиль.
* WPF-диалог `ColumnReinforcementDialog`:
  * Выбор/создание JSON-конфига из папки (как в `WallReinforcement`).
  * Кнопка `Dry run` — прогон с `TransactionGroup.RollBack`.
  * Превью количества выбранных колонн.
* Движок:
  * `ColumnGeometry` — извлекает по BoundingBox + Location/AnalyticalModel: ось колонны, высоту, ширину/глубину, локальный базис XY.
  * `LongitudinalBarBuilder` — продольные стержни по 4 углам + промежуточные по сторонам с равным шагом, по `count` или по `spacing` (что задано).
  * `StirrupBuilder` — простой прямоугольный хомут с заданным шагом и крюком (использует `Rebar.CreateFromRebarShape` или `Rebar.CreateFromCurves` с замкнутой полилинией).
  * `ExistingRebarCleaner` — опционально удаляет ранее созданные плагином `Rebar` в host-колонне (по `Comments`-маркеру `ColumnReinforcement:run-id`).
* Конфиг (JSON, Imperial defaults):
  ```jsonc
  {
    "schemaVersion": 1,
    "name": "default",
    "units": "Imperial",                  // значения ниже — в inches
    "cover": { "sides": 1.5, "ends": 1.5 },
    "longitudinal": {
      "barType": "#8",                    // 1.0" dia, обычная вертикальная арматура колонны
      "cornerOnly": false,
      "barsAlongWidth": 3,
      "barsAlongDepth": 3,
      "hookTopType": null,                // обрезка по верху колонны (без выпуска) — Фаза 1
      "hookBottomType": null
    },
    "stirrups": {
      "enabled": true,
      "barType": "#4",                    // 0.5" dia tie
      "spacing": 8,                       // 8" max — по ACI 318 §25.7.2 для #8 long: min(16·1=16, 48·0.5=24, h_col)
      "hookType": "T1 - 135 deg seismic", // имя RebarHookType из документа; lookup по имени
      "rotate45": false                   // Фаза 2
    }
  }
  ```
  Defaults подобраны под колонну 16″×16″ с #8 продольной и #4 хомутом — типовой случай. Spacing 8″ — компромисс между ACI-минимумом для seismic-зон и не-seismic.
* Результирующий диалог `ResultsDialog` — список колонн с числом созданных стержней и ошибками.

**Не входит в Фазу 1 (явно):**
* Круглые колонны, наклонные колонны, L/T-сечения.
* Выпуски (ни вверх, ни вниз).
* Группировка и многотабличный UI.
* Зоны сгущения хомутов.
* Дополнительные внутренние формы (С-, S-образные хомуты, крестовые связи).
* Соединители (couplers).
* Расчёт длины анкеровки/нахлестки по СП 63.13330.
* Экспорт/импорт настроек в стороннее хранилище.

### Фаза 2 — сгущение, выпуски (dowels), круглые колонны

* Зоны сгущения хомутов сверху/снизу (confinement zone `lo`):
  * шаг и длина зоны (абсолют в inches или доля высоты);
  * флажки «включить верхнюю»/«нижнюю» отдельно;
  * опция «при наличии входящих выпусков снизу — длина нижней зоны = длина dowels» (видна в видео).
* Отступы от верхней/нижней грани колонны (флажки + значения).
* Dowels вверх (в вышележащую колонну/перекрытие):
  * «не создавать» / «прямые dowels» / «с отгибом»;
  * длина — от верха вышележащего перекрытия (или верха колонны, если перекрытия нет);
  * 6 направлений отгиба для рекомендованных форм;
  * флажок «игнорировать вышележащее перекрытие» (длина считается от верха колонны напрямую).
* Dowels из нижележащей плиты (foundation dowels):
  * включение/выключение для группы;
  * опция «только из категории Structural Foundation»;
  * 6 типовых форм выпуска (L, L-с-большим-загибом, U, прямые с/без couplers и пр.);
  * lap splice length (Class A/B per ACI 318 §25.5.2) — отдельный inputs или калькулятор.
* Круглые колонны:
  * продольное армирование по окружности с заданным числом стержней;
  * круглый хомут (`Rebar.CreateFromCurves` с окружностью; шаг + закрытый/открытый);
  * спираль — отдельный пункт Фазы 3.
* **Калькулятор длины анкеровки/lap splice по ACI 318** (отдельный диалог, не зависит от env проекта):
  * Inputs: f'c, fy, bar size, location (top bar — ψt), epoxy coating (ψe), cover, ratio As_required/As_provided.
  * Outputs: `ℓd` (development length, §25.4), `ℓst` (tension lap splice, §25.5), `ℓsc` (compression splice, §25.5.5).
  * Кнопка-калькулятор рядом с полем «длина перепуска» в dowels-секции.

**Не входит в Фазу 2:** спиральное армирование, группировка, couplers (соединители), preferred-shapes mapping, экспортируемое хранилище.

### Фаза 3 — группировка, доп. формы, preferred-shapes, продвинутые опции

* Кнопка `Group` и две таблицы (группы / колонны в группе):
  * автогруппировка по типу+размеру сечения;
  * опциональные критерии: по уровню / по высоте / по пользовательскому параметру колонны.
* **Диалог «Preferred rebar shape families»** (по образцу видео, frame 19):
  * Карта 14 ролей → `RebarShape` (или «Auto»):
    * Longitudinal: Straight, L-bent (for splice hooks)
    * Foundation dowels: L, L-with-large-bend, U
    * Transverse: Straight (single-leg), C-tie (sketch hook), C-tie (Revit hook), S-tie (sketch), S-tie (Revit), Triangular, Rectangular, Circular, Spiral
  * «Auto» = плагин подбирает shape по геометрии. Явное семейство = плагин гарантированно использует именно его.
  * Сохраняется в `ExtensibleStorage` документа.
* Доп. формы поперечного армирования внутри хомута:
  * С-, S-образные cross-ties между крайними стержнями (одиночная нога с крюками);
  * 45° rectangular ties для прямоугольных колонн (ACI 318 §25.7.2.3 — допустимо при правильной геометрии);
  * редактор форм в UI (добавить/изменить/удалить/сместить — drag handles на превью сечения).
* Спиральное армирование круглых колонн (ACI 318 §25.7.3):
  * `Rebar.CreateFromCurves` со спиральной кривой (вычисляется на лету) **или** instance семейства с параметром радиуса (как в ролике: `O_77.rfa` + `ADSK_R`);
  * по умолчанию — генерация кривой; mapping на семейство — опционально через preferred-shapes.
* Couplers (mechanical splices, ACI 318 §25.5.7):
  * отдельный диалог правил «по bar size / по типу»;
  * подстановка `RebarCoupler` на стыках прямых выпусков.
* Экспорт/импорт настроек:
  * в JSON-файл;
  * в локальное хранилище плагина (`%AppData%\ColumnReinforcement\store\*.json`).
* Опция «использовать геометрию без учёта соединений» — игнорировать примыкающие балки/стены/плиты при расчёте границ колонны (от видео: frame 2 «учёт балок между колонной и плитой перекрытия»). Влияет на: куда «упирается» хомут, где обрезаются продольные стержни.
* Multi-floor host колонна:
  * если одна колонна-host проходит сквозь несколько уровней — определять промежуточные перекрытия и автоматически разрезать продольную арматуру на стыках с lap-splice;
  * вышележащая колонна определяется поиском следующего колонна-host со пересекающейся осью.
* Текстовые/ключевые параметры арматуры (`Comments`, custom shared parameters) — задаются в отдельном диалоге `Parameters` и проставляются на каждый созданный `Rebar`.

---

## 4. Архитектура (повторяет `WallReinforcement`)

```
ColumnReinforcement/
├── App.cs                           # IExternalApplication, регистрация кнопки на ленте
├── ColumnReinforcement.addin        # AddIn manifest (новый ClientId GUID)
├── ColumnReinforcement.csproj       # net8.0-windows, x64, WPF, Nice3point refs
├── Commands/
│   ├── ColumnReinforcementCommand.cs        # IExternalCommand
│   └── ColumnSelectionFilter.cs             # ISelectionFilter
├── Config/
│   ├── ColumnReinforcementConfig.cs         # POCO с JsonPropertyName + schemaVersion
│   ├── ConfigLoader.cs                      # System.Text.Json load/save
│   ├── FolderStorage.cs                     # ExtensibleStorage с путём к папке конфигов
│   └── Length.cs                            # Метрика/дюймы/feet-inches → feet
├── Domain/
│   ├── ColumnGeometry.cs                    # ось/высота/W/D/локальный базис/сечение
│   ├── ColumnGroupKey.cs                    # ключ группировки (Фаза 3)
│   ├── RunResult.cs                         # successes/skipped/errors per column
│   └── HostContext.cs                       # нижележащая колонна, плита снизу/сверху
├── Engine/
│   ├── ColumnReinforcer.cs                  # Orchestrator: Run(IList<ElementId>, Config, DryRun)
│   ├── ExistingRebarCleaner.cs              # удаление ранее созданной арматуры по маркеру
│   ├── LongitudinalBarBuilder.cs            # продольные стержни (4 угла + промежуточные)
│   ├── StirrupBuilder.cs                    # внешний tie (Фаза 1)
│   ├── ConfinementZoneBuilder.cs            # сгущения сверху/снизу (Фаза 2, ACI 318 §18.7.5 lo)
│   ├── FoundationDowelBuilder.cs            # dowels снизу из плиты (Фаза 2)
│   ├── UpperSpliceBuilder.cs                # dowels вверх в перекрытие/колонну (Фаза 2)
│   ├── InnerTieBuilder.cs                   # внутренние cross-ties C/S/45° (Фаза 3)
│   ├── SpiralBuilder.cs                     # спираль для круглых (Фаза 3, ACI 318 §25.7.3)
│   ├── PreferredShapeResolver.cs            # роль → RebarShape mapping (Фаза 3)
│   ├── AciAnchorageCalculator.cs            # ℓd / lap-splice по ACI 318 §25.4-25.5 (Фаза 2)
│   ├── RebarFactory.cs                      # обёртка над Rebar.Create*
│   └── UnitConv.cs
└── UI/
    ├── ColumnReinforcementDialog.xaml(.cs)  # главный диалог
    ├── ResultsDialog.cs
    └── (Фаза 3) GroupingPanel, FormsEditor, CouplerRulesDialog, AnchorageCalculator
```

Соглашения:
* Все длины в конфиге — через `Length` (по умолчанию мм). В Revit-API уходят только футы через `UnitConv.MmToFeet` / `Length.ToFeet(UnitSystem)`.
* Каждый `Rebar` помечается параметром `Comments = "ColumnReinforcement:<runId>"` для последующего поиска и очистки.
* Один `TransactionGroup` на весь запуск. Внутри — отдельные `Transaction` на колонну, чтобы частичный сбой не валил всю операцию.
* `Dry run` = `TransactionGroup.RollBack()` — позволяет просмотреть отчёт без изменения модели.

---

## 5. UX

Минимальный диалог Фазы 1 (по образцу `WallReinforcementDialog`):

```
┌────────────────────────────────────────────────────┐
│  Column Reinforcement                              │
├────────────────────────────────────────────────────┤
│  Selected: 3 column(s)                             │
│                                                    │
│  Config folder: [L:\...\configs]      [Browse...]  │
│  Config:        [▼ default.json    ]  [New…]       │
│                                                    │
│  ──── Cover (in) ────                              │
│   Sides: [1.5]    Ends: [1.5]                      │
│                                                    │
│  ──── Longitudinal ────                            │
│   Bar type: [▼ #8 (1.000")]                        │
│   Along width:  [3] bars                           │
│   Along depth:  [3] bars                           │
│   ☐ Corners only                                   │
│                                                    │
│  ──── Ties (transverse) ────                       │
│   ☑ Enabled                                        │
│   Bar type: [▼ #4 (0.500")]   Spacing: [8] in      │
│   Hook: [▼ T1 - 135 deg seismic]                   │
│   ☐ Rotate 45° (Phase 2)                           │
│                                                    │
│  ☐ Dry run (preview only, do not commit)           │
│                                                    │
│             [  Cancel  ]    [  Run  ]              │
└────────────────────────────────────────────────────┘
```

Диалог Фазы 2/3 разрастается до структуры ModPlus (три зоны: выбор+анализ / группы / настройки группы), но в Фазе 1 хватает плоской формы.

---

## 6. Граничные случаи и валидация

| Сценарий | Поведение |
|---|---|
| Выбран элемент не той категории | Фильтр выбора отсекает; в `PickObjects` — недоступен для клика |
| Колонна наклонная (`IsSlantedColumn`) | Фаза 1 — пропустить с записью в `RunResult.Skipped`; Фаза 2 — поддержать |
| Колонна имеет ненулевой Rotation в плане | Учитывать через локальный базис XY (как уже сделано в `SlabRebar` коммите) |
| Сечение не прямоугольное (профильное семейство) | Фаза 1 — пропустить; Фаза 2 — круг; Фаза 3 — L/T |
| `barType` из конфига не найден в документе | Ошибка для конкретной колонны → попадает в отчёт |
| Высота колонны меньше шага хомута | Создать хотя бы 2 хомута (верх и низ от защитного слоя), записать предупреждение |
| Колонна без верхнего/нижнего привязочного уровня (вручную заданная высота) | Использовать BoundingBox по оси Z |
| `Dry run` | `TransactionGroup.RollBack()` после прогона; отчёт показать |
| Уже есть арматура с маркером плагина | По опции: удалить / удалить всю / не трогать (как в ModPlus) |

---

## 7. Внешние зависимости и ссылки

**Revit API:**
* [Revit 2025 API docs](https://help.autodesk.com/view/RVT/2025/ENU/) — справочник по `Rebar`, `RebarShape`, `RebarHookType`, `RebarBarType`, `RebarStyle`, `RebarCoupler`.
* [revitapidocs.com (2025)](https://www.revitapidocs.com/2025/) — поиск по классам.

**ACI нормативка (нужно держать под рукой при реализации):**
* ACI 318-19 (или 318-25 если доступна) — §20.5 cover, §25.3 hooks, §25.4 development length, §25.5 splices, §25.7 transverse reinforcement, §18.7 special moment frames.
* ASTM A615/A615M, A706/A706M — bar designations и dia.
* Любой US-handbook (CRSI Manual / PCA Notes on ACI 318) — для проверочных примеров anchorage calculator.

**Референсы UX:**
* [ModPlus: Армирование колонн (ru)](https://modplus.org/ru/revitplugins/mprcolumnsreinforcement) — функциональный референс.
* [ModPlus: Columns reinforcement (en)](https://modplus.org/en/revitplugins/mprcolumnsreinforcement) — то же, короче.
* [Видео ModPlus 2.0](https://rutube.ru/video/da3eb0da203b892cbfd6eca855ededb1/) — первоисточник запроса. Локально: `C:\Users\Vic\Downloads\video_da3eb0da203b892cbfd6eca855ededb1_1080_20260526164415.mp4`. Кадры: `C:\Users\Vic\Downloads\video_frames\frame_001..024.jpg`.

**Соседние плагины в репо:**
* `..\WallReinforcement\` — повторяемые шаблоны кода и структура (Config/Domain/Engine/UI).
* `..\SlabRebar\` — пример обработки local basis (X/Y rotation) для колонн с поворотом в плане.

---

## 8. Открытые вопросы к владельцу

1. **Сейсмическая категория проектов.** Если основной workflow — SDC D/E/F (special moment frames), то confinement zones и 135° hooks становятся обязательной частью Фазы 1, а не Фазы 2. Если SDC A-C — Фаза 1 как описана.
2. **Источник `RebarBarType` и `RebarHookType`** — берём из активного документа (как в ModPlus, безопаснее) или создаём при отсутствии (быстрее для пустых шаблонов)? В `WallReinforcement` — берётся, валидируется, ошибка если нет. Продолжим так?
3. **Ленточная вкладка** — отдельная Tab «Reinforcement» с тремя кнопками (Wall/Slab/Column) или каждый плагин в свою вкладку? В `.addin` у `WallReinforcement` пока ленты нет — нужно зафиксировать общий подход.
4. **Группировка нужна сразу?** Большинство US-практик — одно армирование на проект, или часто требуется деление по этажам/типам сечения? От ответа зависит, стоит ли поднимать Фазу 3 раньше Фазы 2.
5. **Калькулятор ℓd/lap-splice по ACI 318** — встроенный в Фазу 2 или достаточно вводить число руками (рассчитывая по таблицам ACI/CRSI отдельно)?
6. **`#14` и `#18`** — обычно для крупных колонн mat foundations; реально ли встречаются в ваших проектах? Это влияет на min/max ranges в UI dropdowns.
