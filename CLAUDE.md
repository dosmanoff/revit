# Revit Plugin — Система AI-агентов

> **GitHub:** https://github.com/dosmanoff/revit  
> **Clone:** `git clone https://github.com/dosmanoff/revit.git`

## Описание проекта
Набор специализированных агентов для совместной разработки плагина Autodesk Revit.  
Платформа: **.NET 8** | Revit API **2024/2025** | Язык: **C#** | Домен: **Автоматизация моделирования BIM**

---

## Состав команды агентов

| Агент | Файл | Роль |
|-------|------|------|
| Оркестратор | `CLAUDE.md` (этот файл) | Координация, маршрутизация задач |
| Архитектор | `agents/architect.md` | Проектирование структуры и API |
| Разработчик | `agents/developer.md` | Написание C# / Revit API кода |
| Тестировщик | `agents/tester.md` | Тесты, отладка, валидация |
| Документатор | `agents/docs.md` | XML-доки, README, changelog |

---

## Рабочий процесс

```
Задача от пользователя
        │
        ▼
  [Оркестратор]
  Анализирует задачу и маршрутизирует
        │
   ┌────┴────┐
   ▼         ▼
[Архитектор] [Разработчик]
Проектирует   Реализует
   │              │
   └────┬─────────┘
        ▼
  [Тестировщик]
  Проверяет и валидирует
        │
        ▼
  [Документатор]
  Документирует изменения
```

---

## Правила оркестратора

### Когда вызывать агентов

**Архитектора** (`agents/architect.md`) — при:
- Создании новой фичи или модуля
- Вопросах о структуре проекта
- Выборе паттернов и подходов
- Рефакторинге крупных компонентов

**Разработчика** (`agents/developer.md`) — при:
- Написании/изменении C# кода
- Работе с Revit API (элементы, транзакции, семейства)
- Реализации команд и обработчиков событий
- Интеграции с внешними системами

**Тестировщика** (`agents/tester.md`) — при:
- Написании xUnit/NUnit тестов
- Воспроизведении и отладке багов
- Проверке граничных случаев
- Code review на корректность

**Документатора** (`agents/docs.md`) — при:
- Создании/обновлении README
- Написании XML-документации
- Составлении changelog
- Документировании Revit API-специфики

---

## Структура проекта

```
RevitPlugin/
├── CLAUDE.md                    ← Вы здесь (оркестратор)
├── agents/
│   ├── architect.md             ← Агент-архитектор
│   ├── developer.md             ← Агент-разработчик
│   ├── tester.md                ← Агент-тестировщик
│   └── docs.md                  ← Агент-документатор
├── src/
│   ├── RevitPlugin/
│   │   ├── RevitPlugin.csproj
│   │   ├── Application.cs       ← Точка входа IExternalApplication
│   │   ├── Commands/            ← IExternalCommand реализации
│   │   ├── Services/            ← Бизнес-логика
│   │   ├── Models/              ← Доменные модели
│   │   ├── UI/                  ← WPF-окна и панели
│   │   └── Helpers/             ← Утилиты Revit API
│   └── RevitPlugin.Tests/
│       ├── RevitPlugin.Tests.csproj
│       └── Commands/
├── .github/
│   └── workflows/
│       ├── build.yml       ← CI: сборка + тесты (push/PR)
│       ├── release.yml     ← CD: создание релиза по тегу v*.*.*
│       └── pr-check.yml    ← PR: форматирование, сборка, тесты
└── docs/
    ├── README.md
    └── CHANGELOG.md
```

---

## Технический стек

- **Revit API**: `RevitAPI.dll`, `RevitAPIUI.dll` (2024/2025)
- **Фреймворк**: .NET 8
- **UI**: WPF + MVVM
- **Тесты**: xUnit + Moq
- **Логирование**: Serilog
- **Сборка**: MSBuild / `dotnet build`

---

## Соглашения по коду

- Namespace: `RevitPlugin.*`
- Все Revit-операции — внутри `using (Transaction t = ...)`
- Команды реализуют `IExternalCommand`
- Приложение реализует `IExternalApplication`
- Обработка ошибок через `TaskDialog` + Serilog
- Строки UI — в ресурсных файлах

---

## Быстрый старт для агентов

```bash
# Клонировать репозиторий
git clone https://github.com/dosmanoff/revit.git
cd Revit

# Сборка
dotnet build src/RevitPlugin/RevitPlugin.csproj

# Тесты
dotnet test src/RevitPlugin.Tests/

# Добавить зависимость
dotnet add package <PackageName>
```

## CI/CD (GitHub Actions)

| Workflow | Триггер | Что делает |
|----------|---------|------------|
| `build.yml` | push в `main`/`develop`, любой PR | Сборка под Revit 2024 + 2025, юнит-тесты |
| `pr-check.yml` | любой PR в `main`/`develop` | Форматирование кода, сборка, тесты |
| `release.yml` | push тега `v*.*.*` | Сборка Release, пакует `.zip`, создаёт GitHub Release |

### Создать релиз
```bash
git tag v1.0.0
git push origin v1.0.0
# → GitHub Actions автоматически соберёт и опубликует релиз
```

---

## Git workflow

```bash
# Создать ветку для фичи
git checkout -b feature/<название-фичи>

# Commit с описанием
git commit -m "feat(M1): добавить ExternalMeshRule"

# Push и Pull Request
git push origin feature/<название-фичи>
# → открыть PR на https://github.com/dosmanoff/revit/pulls
```

### Формат коммитов (Conventional Commits)
- `feat:` — новая функциональность
- `fix:` — исправление бага
- `refactor:` — рефакторинг без изменения функциональности
- `test:` — добавление/изменение тестов
- `docs:` — документация
- `chore:` — инфраструктура, зависимости

---

## Память агентов

Каждый агент сохраняет контекст в своём `.md`-файле.  
Оркестратор агрегирует решения в `CLAUDE.md` (этот файл).  
При конфликте решений — приоритет у Архитектора.
