# 🐳 Docker Deployment Guide

🇺🇸 English | 🇺🇦 [Українська](DOCKER.uk.md)

> [!NOTE]
> This guide covers deploying the **web version** of the bot (`TelegramBotWebApp`) via Docker Compose.  
> To see **all** available run methods (local, console, systemd, etc.), see the [Hosting Guide](./HOSTING.md).

---

## Prerequisites

- [.NET 10 SDK](./setup-prerequisites.md) for local development
- [Docker](./setup-prerequisites.md) ≥ 24
- [Docker Compose](./setup-prerequisites.md) ≥ 2.20 (plugin, not standalone)
- A public HTTPS URL for Webhook mode *(optional — Polling works without it)*

---

## Quick Start

### 1. Clone and enter the repository

```bash
git clone https://github.com/nudykw/GptChatTelegramBot.git
cd GptChatTelegramBot
```

### 2. Create your environment file

```bash
cp .env.example .env
```

Then open `.env` and fill in your values. At minimum you must set:

| Variable | Description |
|---|---|
| `TELEGRAM_BOT_TOKEN` | Token from [@BotFather](https://t.me/BotFather) |
| `TELEGRAM_OWNER_ID` | Your Telegram user ID (get it from [@userinfobot](https://t.me/userinfobot)) |
| `POSTGRES_PASSWORD` | A strong password for the database |
| `CLOUDBEAVER_ADMIN_PASSWORD` | A strong password for CloudBeaver UI (≥ 8 chars) |

### 3. Choose your database and services

Edit the `COMPOSE_PROFILES` variable in `.env`:

| Value | Services started |
|---|---|
| `postgres,cloudbeaver` | PostgreSQL + CloudBeaver + Bot *(default)* |
| `postgres` | PostgreSQL + Bot |

Also update `DB_PROVIDER` and `DB_CONNECTION_STRING` to match your chosen database.

### 4. Start the stack

```bash
docker compose up -d
```

### 5. Verify everything is running

```bash
docker compose ps
curl http://localhost:8080/health
```

---

## Bot Modes: Polling vs Webhook

The bot automatically detects which mode to use based on the `TELEGRAM_BASE_API_URL` variable in `.env`.

### Polling Mode *(no public URL required)*

Leave `TELEGRAM_BASE_API_URL` empty:

```dotenv
TELEGRAM_BASE_API_URL=
```

The bot will connect to Telegram and poll for updates every few seconds.

### Webhook Mode *(requires public HTTPS URL)*

Set `TELEGRAM_BASE_API_URL` to your public domain:

```dotenv
TELEGRAM_BASE_API_URL=https://yourdomain.com
```

Trailing slash is optional — both `https://example.com` and `https://example.com/` work.

On startup the bot will automatically register the webhook at:

```text
POST https://yourdomain.com/aibot
```

> [!TIP]
> For local testing with Webhook mode you can use [ngrok](https://ngrok.com/)  
> or [Cloudflare Tunnel](https://developers.cloudflare.com/cloudflare-one/connections/connect-networks/):
>
> ```bash
> cloudflared tunnel --url http://localhost:8080
> ```
>
> Then set `TELEGRAM_BASE_API_URL` to the generated HTTPS URL.

---

## Available Endpoints

Once the bot is running, the following HTTP endpoints are available:

| Method | URL | Description |
|---|---|---|
| `GET` | `/health` | Health check — returns `200 OK` if the app is healthy |
| `GET` | `/api/info` | Bot information (name, mode, version) |
| `POST` | `/aibot` | Telegram Webhook receiver *(Webhook mode only)* |
| `GET` | `/metrics` | Prometheus metrics scraping endpoint |
| `GET` | `/scalar` | Swagger / OpenAPI interactive documentation |
| `GET` | `/openapi/v1.json` | Raw OpenAPI JSON spec |

---

## Database Providers

### PostgreSQL *(profile: `postgres`)*

```dotenv
COMPOSE_PROFILES=postgres
DB_PROVIDER=PostgreSQL
DB_CONNECTION_STRING=Host=postgres;Port=5432;Database=${POSTGRES_DB};Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD}
```

### Microsoft SQL Server *(profile: `mssql`)*

```dotenv
COMPOSE_PROFILES=mssql
DB_PROVIDER=SqlServer
DB_CONNECTION_STRING=Server=mssql,1433;Database=${MSSQL_DB};User Id=sa;Password=${MSSQL_SA_PASSWORD};TrustServerCertificate=true
```

> [!WARNING]
> The MSSQL SA password must satisfy SQL Server complexity rules:  
> ≥ 8 characters, with uppercase, lowercase, digit **and** a special character.

### Connecting CloudBeaver to your database

1. Open CloudBeaver in your browser
2. Log in with `CLOUDBEAVER_ADMIN_NAME` / `CLOUDBEAVER_ADMIN_PASSWORD`
3. Click **New Connection** and choose your database type
4. Use the **container hostname** as the host (e.g. `postgres`, `mysql`, or `mssql`)
5. Fill in the database name and credentials from your `.env`

---

## Aspire Dashboard — Observability

Add `aspire` to `COMPOSE_PROFILES` to enable real-time monitoring of metrics, traces, and logs for all services:

```dotenv
COMPOSE_PROFILES=postgres,cloudbeaver,aspire
```

This activates:

- **Aspire Dashboard** UI at `http://localhost:18888`
- **OTel Collector** — scrapes PostgreSQL / MySQL / MSSQL metrics every 30s
- **CloudBeaver Java Agent** — auto-instruments CloudBeaver (JVM metrics, HTTP traces)

> [!NOTE]
> The `aspire` profile is optional and safe to omit in production if you use an external monitoring solution.

📖 **Full guide with animated demos:** [docs/OBSERVABILITY.md](./OBSERVABILITY.md)

---

## Useful Commands

```bash
# Start all services in background
docker compose up -d

# View logs (all services)
docker compose logs -f

# View logs for the bot only
docker compose logs -f bot

# Restart the bot
docker compose restart bot

# Stop everything
docker compose down

# Stop and remove volumes (⚠️ deletes database data)
docker compose down -v

# Rebuild the bot image after code changes
docker compose up -d --build bot
```

---

## Environment Variable Reference

See [`.env.example`](../.env.example) for a full reference with descriptions of every variable.
