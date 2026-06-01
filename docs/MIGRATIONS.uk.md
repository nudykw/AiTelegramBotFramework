# 🗄️ Посібник з міграцій бази даних

🇺🇸 [English](MIGRATIONS.md) | 🇺🇦 Українська

> [!SEE-ALSO]
> Пов'язані документи: [Hosting Guide](HOSTING.md) | [Docker Deployment](DOCKER.md) | [Deploy Guide](DEPLOY.md)

Цей проєкт використовує **PostgreSQL** як єдиного провайдера бази даних. Міграції зберігаються в каталозі `DataBaseLayer/Migrations/`.

---

## Два варіанти застосунку

Рішення містить **два запускаємих проєкти**, які спільно використовують `DataBaseLayer`:

| Проєкт | Тип | Призначення |
|---|---|---|
| `TelegramBotApp/` | Консольний застосунок | Локальна розробка, простий запуск |
| `TelegramBotWebApp/` | ASP.NET Core Web API | Розгортання в Docker, режим Webhook, Prometheus метрики, Scalar API |

Обидва проєкти ділять **однаковий `DataBaseLayer`** та **спільні міграції**. Скрипти міграцій використовують `TelegramBotApp/` як проєкт запуску за замовчуванням.

> [!NOTE]
> Міграції застосовуються **автоматично при запуску** в обох додатках через `MigrationConfigurator.ApplyMigrations()`, тому в продакшені вручну запускати оновлення бази даних немає потреби.

---

## Попередні вимоги

Перед роботою з міграціями переконайтеся, що інструмент EF Core CLI встановлено:

```bash
dotnet tool restore
```

---

## Як це працює

### Автоматичні міграції при запуску

Застосунок автоматично викликає `Migrate()` при старті. Строка підключення завантажується з `DB_CONNECTION_STRING` (в `.env` / `appsettings.json`).

### Структура міграцій

- **Каталог міграцій**: `DataBaseLayer/Migrations/`
- **Простір імен (Namespace)**: `DataBaseLayer.Migrations`

---

## Допоміжні скрипти

Для спрощення роботи ви можете використовувати готові допоміжні скрипти:

**Bash (Linux / macOS):**

```bash
# Додати нову міграцію
./migr.sh add MigrationName

# Видалити останню міграцію
./migr.sh remove

# Оновити БД вручну
./migr.sh update

# Показати список міграцій
./migr.sh list
```

**PowerShell (Windows / кросплатформний):**

```powershell
# Додати нову міграцію
./migr.ps1 add MigrationName

# Видалити останню міграцію
./migr.ps1 remove

# Оновити БД вручну
./migr.ps1 update

# Показати список міграцій
./migr.ps1 list
```

---

## ✏️ Ручні команди (EF Core CLI)

Якщо ви віддаєте перевагу стандартному інтерфейсу командного рядка:

### Додати нову міграцію

```bash
dotnet ef migrations add DescriptiveName \
    --project DataBaseLayer/ \
    --startup-project TelegramBotApp/
```

### Застосувати міграції вручну

```bash
dotnet ef database update \
    --project DataBaseLayer/ \
    --startup-project TelegramBotApp/
```

### Видалити останню міграцію

```bash
dotnet ef migrations remove \
    --project DataBaseLayer/ \
    --startup-project TelegramBotApp/
```
