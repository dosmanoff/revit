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

## Быстрый старт

После установки в Revit появится вкладка **BIM Tools** на Ribbon.

### Создание стен по линиям

1. Откройте план этажа
2. Нарисуйте линии модели (Model Line)
3. Выберите линии
4. Нажмите **BIM Tools → Automation → Create Walls**
5. Стены создадутся вдоль выбранных линий

---

## Разработка

### Клонирование

```bash
git clone https://github.com/dosmanoff/revit.git
cd revit
```

### Сборка

```bash
# Revit 2024
dotnet build src/RevitPlugin/RevitPlugin.csproj -c Debug2024

# Revit 2025
dotnet build src/RevitPlugin/RevitPlugin.csproj -c Debug2025
```

### Тесты

```bash
dotnet test src/RevitPlugin.Tests/
```

### Структура проекта

```
revit/
├── CLAUDE.md              ← Оркестратор AI-агентов
├── agents/                ← Специализированные AI-агенты
│   ├── architect.md
│   ├── developer.md
│   ├── tester.md
│   └── docs.md
├── src/
│   ├── RevitPlugin/       ← Основной проект
│   └── RevitPlugin.Tests/ ← Тесты
└── docs/
    ├── README.md          ← Этот файл
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
