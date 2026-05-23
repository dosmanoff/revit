# Wall Reinforcement Suite — плагин Revit

> Плагин для автоматизации армирования железобетонных стен в Autodesk Revit **2025**.

[![GitHub](https://img.shields.io/badge/GitHub-dosmanoff%2Frevit-blue)](https://github.com/dosmanoff/revit)
[![Build](https://github.com/dosmanoff/revit/actions/workflows/build.yml/badge.svg)](https://github.com/dosmanoff/revit/actions)

---

## 📑 Спецификация и архитектура

| Документ | О чём |
|---|---|
| [SPEC.md](SPEC.md) | Функциональная спецификация: 6 модулей, нефункциональные требования, MVP-приоритеты |
| [ARCHITECTURE.md](ARCHITECTURE.md) | Слои, проекты, контракты, транзакции, External Events |
| [MODULES.md](MODULES.md) | Детализация модулей M1–M6 + реестр правил |
| [CONFIG_SCHEMA.md](CONFIG_SCHEMA.md) | JSON-схема `.wrsconfig.json` |
| [PARAMETERS.md](PARAMETERS.md) | Словарь Shared Parameters плагина |
| [UI_FLOWS.md](UI_FLOWS.md) | UX-сценарии и описание диалогов |
| [ROADMAP.md](ROADMAP.md) | Этапы M1–M5 до v1.0 |

> Референс по функциональности — [BeSmart Concrete Collection](https://docs.besmart.software/collections/concrete-collection).

---

## Требования

- Autodesk Revit **2025** (приоритет), опционально 2024
- Windows 10/11 (64-bit)
- .NET 8 Runtime (поставляется с Revit 2024+)

---

## Установка

1. Скачайте последний релиз: [Releases](https://github.com/dosmanoff/revit/releases)
2. Распакуйте архив
3. Скопируйте файлы в папку Revit Addins:
   ```
   %APPDATA%\Autodesk\Revit\Addins\2024\
   ```
   Нужны два файла:
   - `RevitPlugin.dll`
   - `RevitPlugin.addin`
4. Перезапустите Revit

---

## Что делает плагин

Плагин **не создаёт стены** — он работает с уже существующими в модели стенами и автоматизирует:

- **Армирование** монолитных и сборных ЖБ-стен по конфигурации: внешняя/внутренняя сетки, периметр, окантовка проёмов, L-/T-узлы, дополнительные стержни.
- **Выпуски (dowels)** из нижележащих конструкций (фундаментная плита, нижестоящая стена, перекрытие) в стену-приёмник.
- **Назначение параметров** на арматуру (марка стены, ярус, секция, этаж, роль стержня, позиция) — для фильтрации на видах и группировки в спецификациях.
- **Создание видов**: поперечные сечения, продольные развёртки, узлы (фундаментный стык, L-/T-узлы, окантовка проёмов), 3D-проверочный вид.
- **Создание спецификаций**: ведомость деталей арматуры, ВРС, ведомость выпусков, сводная по этажам.
- **Конфигуратор армирования** — все нормативы, диаметры, шаги, формы хранятся в JSON-конфигурациях (`.wrsconfig.json`), которые можно версионировать и переносить между проектами.

Референс по функциональности — [BeSmart Concrete Collection](https://arkance.world/global/products/be-smart/building/concrete-collection).

---

## Быстрый старт

После установки в Revit появится вкладка **WRS — Wall Reinforcement Suite** на Ribbon.

### Армирование стены

1. Откройте план этажа в проекте.
2. Выберите стену (или несколько).
3. Нажмите **WRS → Walls → Link Wall** — привяжите выбранные стены к конфигурации.
4. Нажмите **WRS → Walls → Arm Walls** — плагин разместит арматуру по правилам конфига в одной транзакции (Undo возвращает в исходное состояние).
5. Запустите **WRS → Output → Create Views** и **Create Schedules** — появятся сечения, развёртки и спецификации по марке стены.

Подробнее по UX и состояниям — в [UI_FLOWS.md](UI_FLOWS.md).

---

## Разработка

### Клонирование

```bash
git clone https://github.com/dosmanoff/revit.git
cd revit
```

### Сборка

```bash
# Revit 2024 (по умолчанию)
dotnet build src/RevitPlugin/RevitPlugin.csproj -c Debug2024

# Revit 2025
dotnet build src/RevitPlugin/RevitPlugin.csproj -c Debug2025
```

> Для сборки `RevitPlugin.csproj` требуются `RevitAPI.dll` и `RevitAPIUI.dll` из установленного Revit. Domain-слой (`RevitPlugin.Domain`) и тесты — чистый .NET 8, собираются без Revit.

### Тесты

```bash
dotnet test src/RevitPlugin.Tests/RevitPlugin.Tests.csproj
```

Тесты покрывают доменную логику правил (`ExternalMeshRule`, `RuleEngine`) и сериализацию конфигов (`ConfigStorage`). Revit API не требуется.

### Структура проекта

```
revit/
├── CLAUDE.md                       ← Оркестратор AI-агентов
├── agents/                         ← Специализированные AI-агенты
│   ├── architect.md
│   ├── developer.md
│   ├── tester.md
│   └── docs.md
├── src/
│   ├── RevitPlugin/                ← Revit-add-in (net8.0-windows, требует RevitAPI)
│   ├── RevitPlugin.Domain/         ← Доменная логика (net8.0, без Revit)
│   └── RevitPlugin.Tests/          ← Юнит-тесты (net8.0, без Revit)
├── .github/workflows/
│   ├── build.yml                   ← CI: сборка под Revit 2024 + 2025, тесты
│   └── pr-check.yml                ← PR: форматирование + сборка + тесты
└── docs/
    ├── README.md                   ← Этот файл
    ├── SPEC.md, ARCHITECTURE.md, MODULES.md, ROADMAP.md
    ├── CONFIG_SCHEMA.md, PARAMETERS.md, UI_FLOWS.md
    └── CHANGELOG.md
```

---

## AI-агенты разработки

Проект использует систему специализированных Claude AI-агентов. Подробнее в [CLAUDE.md](../CLAUDE.md).

| Агент | Роль |
|-------|------|
| [Архитектор](../agents/architect.md) | Проектирование структуры |
| [Разработчик](../agents/developer.md) | C# / Revit API код |
| [Тестировщик](../agents/tester.md) | Тесты и валидация |
| [Документатор](../agents/docs.md) | Документация |

---

## Контрибуция

1. Fork репозитория: [github.com/dosmanoff/revit](https://github.com/dosmanoff/revit)
2. Создайте ветку: `git checkout -b feature/my-feature`
3. Commit: `git commit -m "feat: описание изменения"`
4. Push: `git push origin feature/my-feature`
5. Откройте [Pull Request](https://github.com/dosmanoff/revit/pulls)

---

## Лицензия

[MIT License](../LICENSE)
