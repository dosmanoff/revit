# Changelog

Все значимые изменения документируются здесь.  
Формат: [Keep a Changelog](https://keepachangelog.com/ru/1.0.0/) | Версионирование: [SemVer](https://semver.org/).

---

## [Unreleased]

### Добавлено
- Система AI-агентов для разработки: Архитектор, Разработчик, Тестировщик, Документатор
- Базовая структура проекта (.NET 8, Revit API 2024/2025)
- Команда `CreateWallsCommand` — создание стен по линиям модели
- Extension-методы `DocumentExtensions` для упрощения работы с Revit DB
- Логирование через Serilog (ротация по дням, 7 файлов)
- Ribbon-панель "BIM Tools → Automation"

---

## [0.1.0] — 2025-05-20

### Добавлено
- Инициализация репозитория [github.com/dosmanoff/revit](https://github.com/dosmanoff/revit)
- Начальная структура проекта: `src/RevitPlugin`, `src/RevitPlugin.Tests`
- `IExternalApplication` точка входа (`Application.cs`)
- `.addin` манифест для Revit

---

[Unreleased]: https://github.com/dosmanoff/revit/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/dosmanoff/revit/releases/tag/v0.1.0
