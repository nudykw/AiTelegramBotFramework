# 🗄️ Database Migrations Guide

🇺🇸 English | 🇺🇦 [Українська](MIGRATIONS.uk.md)

> [!SEE-ALSO]
> Related Docs: [Hosting Guide](HOSTING.md) | [Docker Deployment](DOCKER.md) | [Deploy Guide](DEPLOY.md)

This project supports **PostgreSQL** as its sole database provider. Migrations are stored under `DataBaseLayer/Migrations/`.

---

## Two Application Variants

The solution contains **two runnable applications** that both use `DataBaseLayer`:

| Project | Type | Purpose |
|---|---|---|
| `TelegramBotApp/` | Console app | Local development, simple deployment |
| `TelegramBotWebApp/` | ASP.NET Core Web API | Docker deployment, Webhook mode, Prometheus metrics, Scalar API |

Both projects share the **same `DataBaseLayer`** and the **same migrations**. The migration scripts use `TelegramBotApp/` as the startup project by default.

> [!NOTE]
> Migrations are applied **automatically on startup** in both applications via `MigrationConfigurator.ApplyMigrations()`, so you never need to run `database update` manually in production.

---

## Prerequisites

Before working with migrations, make sure the EF Core CLI tool is installed:

```bash
dotnet tool restore
```

---

## How It Works

### Automatic Migrations on Startup

The application calls `Migrate()` automatically at startup. The connection string is loaded from `DB_CONNECTION_STRING` (in `.env` / `appsettings.json`).

### Migrations Folder

- **Migrations folder**: `DataBaseLayer/Migrations/`
- **Namespace**: `DataBaseLayer.Migrations`

---

## Helper Scripts

To simplify migrations, you can use the provided helper scripts:

**Bash (Linux / macOS):**

```bash
# Add a new migration
./migr.sh add MigrationName

# Remove last migration
./migr.sh remove

# Apply migrations manually
./migr.sh update

# List all migrations
./migr.sh list
```

**PowerShell (Windows / cross-platform):**

```powershell
# Add a new migration
./migr.ps1 add MigrationName

# Remove last migration
./migr.ps1 remove

# Apply migrations manually
./migr.ps1 update

# List all migrations
./migr.ps1 list
```

---

## ✏️ Manual Commands (EF Core CLI)

If you prefer to use the standard CLI instead of the helper scripts:

### Add a new migration

```bash
dotnet ef migrations add DescriptiveName \
    --project DataBaseLayer/ \
    --startup-project TelegramBotApp/
```

### Apply migrations manually

```bash
dotnet ef database update \
    --project DataBaseLayer/ \
    --startup-project TelegramBotApp/
```

### Remove the last migration

```bash
dotnet ef migrations remove \
    --project DataBaseLayer/ \
    --startup-project TelegramBotApp/
```
