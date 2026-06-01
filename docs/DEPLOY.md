# 🚀 Manual Deployment Guide

🇺🇸 English | 🇺🇦 [Українська](DEPLOY.uk.md)

This guide covers **manual deployment** methods for the GPTChatTelegramBot project. Choose the method that best fits your environment.

---

## 📋 Prerequisites

Before deploying, ensure you have the following:

| Item | Description | Where to Get |
|---|---|---|
| **Telegram Bot Token** | Token from [@BotFather](https://t.me/BotFather) | Telegram |
| **Owner ID** | Your Telegram user ID | [@userinfobot](https://t.me/userinfobot) |
| **AI Provider API Key** | OpenAI, Gemini, Grok, or DeepSeek key | Provider's platform |
| **Database Credentials** | PostgreSQL credentials (if using external DB) | Your DB provider |

---

## Method 1: 🐳 Docker Compose (Recommended for Production)

The recommended way to deploy the bot is via Docker Compose with support for PostgreSQL.

### Requirements

- [Docker](https://docs.docker.com/get-docker/) ≥ 24
- [Docker Compose](https://docs.docker.com/compose/install/) ≥ 2.20

### Steps

#### 1. Clone the repository

```bash
git clone https://github.com/nudykw/GptChatTelegramBot.git
cd GptChatTelegramBot
```

#### 2. Create the environment file

```bash
cp .env.example .env
```

#### 3. Configure `.env`

Open `.env` and set the minimum required values:

```dotenv
# Bot Configuration
TELEGRAM_BOT_TOKEN=your_telegram_bot_token
TELEGRAM_OWNER_ID=your_telegram_user_id

# AI Provider (choose at least one)
OPENAI_API_KEY=sk-your_openai_key
GEMINI_API_KEY=your_gemini_key
GROK_API_KEY=your_grok_key
DEEPSEEK_API_KEY=your_deepseek_key

# Database (PostgreSQL example)
POSTGRES_DB=gpt_chat_bot
POSTGRES_USER=postgres
POSTGRES_PASSWORD=your_strong_password

# Optional: CloudBeaver (database manager)
CLOUDBEAVER_PORT=8978
CLOUDBEAVER_ADMIN_NAME=cbadmin
CLOUDBEAVER_ADMIN_PASSWORD=your_strong_password

# Optional: Aspire Dashboard (observability)
# Add 'aspire' to COMPOSE_PROFILES to enable
```

#### 4. Choose your database

Edit the `COMPOSE_PROFILES` variable in `.env`:

| `COMPOSE_PROFILES` | Services Started |
|---|---|
| `postgres,cloudbeaver` | PostgreSQL + CloudBeaver + Bot *(default)* |
| `postgres` | PostgreSQL + Bot |

Also update `DB_PROVIDER` and `DB_CONNECTION_STRING` to match your chosen database.

#### 5. Start the stack

```bash
docker compose up -d
```

#### 6. Verify deployment

```bash
# Check running containers
docker compose ps

# Check bot logs
docker compose logs -f bot

# Health check
curl http://localhost:8080/health
```

### Useful Commands

```bash
# View all service logs
docker compose logs -f

# Restart the bot
docker compose restart bot

# Stop all services
docker compose down

# Stop and remove volumes (⚠️ deletes database data)
docker compose down -v

# Rebuild after code changes
docker compose up -d --build bot
```

### Accessing Services

| Service | URL | Credentials |
|---|---|---|
| **Bot API** | `http://localhost:8080` | — |
| **CloudBeaver** | `http://localhost:${CLOUDBEAVER_PORT}` | `CLOUDBEAVER_ADMIN_NAME` / `CLOUDBEAVER_ADMIN_PASSWORD` |
| **Aspire Dashboard** | `http://localhost:18888` | — |

---

## Method 2: 🖥️ systemd (Linux VPS without Docker)

Deploy the bot as a system service on a Linux VPS without Docker.

### Requirements

- Linux server with [.NET 10 Runtime](https://dotnet.microsoft.com/download) or self-contained publish
- SSH access to the server

### Steps

#### 1. Build a self-contained publish

On your local machine:

```bash
# Console version (no HTTP server)
dotnet publish TelegramBotApp/TelegramBotApp.csproj \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -o ./publish/console

# Or Web version (with HTTP server)
dotnet publish TelegramBotWebApp/TelegramBotWebApp.csproj \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -o ./publish/web
```

#### 2. Copy files to the server

```bash
scp -r ./publish/console/* user@your-server:/opt/gpt-bot/
scp Configs/appsettings.json user@your-server:/opt/gpt-bot/Configs/
```

#### 3. Configure the server

Connect to the server and make the binary executable:

```bash
chmod +x /opt/gpt-bot/TelegramBotApp
```

#### 4. Create a systemd unit

Create `/etc/systemd/system/gpt-bot.service`:

```ini
[Unit]
Description=GPT Chat Telegram Bot
After=network.target

[Service]
Type=simple
User=ubuntu
WorkingDirectory=/opt/gpt-bot
ExecStart=/opt/gpt-bot/TelegramBotApp
Restart=always
RestartSec=5
StandardOutput=journal
StandardError=journal

[Install]
WantedBy=multi-user.target
```

#### 5. Enable and start the service

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now gpt-bot
```

#### 6. Check logs

```bash
sudo journalctl -u gpt-bot -f
```

---

## Method 3: 🖥️ .NET CLI (Local Development)

The simplest way to run the bot locally for development and testing.

### Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)

### Steps

#### 1. Configure `appsettings.json`

```bash
cp Configs/appsettings.sample.json Configs/appsettings.json
```

Open `Configs/appsettings.json` and fill in the required fields:

```json
{
  "AppSettings": {
    "TelegramBotConfiguration": {
      "BotToken": "your_telegram_bot_token",
      "OwnerId": 123456789,
      "AiSettings": {
        "ChatProviders": [
          {
            "Name": "OpenAI",
            "ProviderType": "OpenAI",
            "ApiKey": "your_openai_key",
            "ModelName": "gpt-4o-mini"
          }
        ]
      }
    },
    "Database": {
      "Provider": "Sqlite",
      "ConnectionString": "Data Source=bot.db"
    }
  }
}
```

#### 2. Run the bot

**Console version (Polling mode):**

```bash
dotnet run --project TelegramBotApp/TelegramBotApp.csproj
```

**Web version (Polling or Webhook mode):**

```bash
dotnet run --project TelegramBotWebApp/TelegramBotWebApp.csproj
```

**With Swagger enabled:**

```bash
ASPNETCORE_ENVIRONMENT=Development dotnet run --project TelegramBotWebApp/TelegramBotWebApp.csproj
```

### Available Endpoints (Web Version)

| Method | URL | Description |
|---|---|---|
| `GET` | `http://localhost:8080/health` | Health check |
| `GET` | `http://localhost:8080/api/info` | Bot information |
| `GET` | `http://localhost:8080/scalar` | Swagger UI |
| `GET` | `http://localhost:8080/metrics` | Prometheus metrics |
| `POST` | `http://localhost:8080/aibot` | Telegram Webhook receiver |

---

## 🔑 Configuration Reference

### Required Environment Variables

| Variable | Description | Example |
|---|---|---|
| `TELEGRAM_BOT_TOKEN` | Telegram Bot Token | `123456:ABC-DEF1234ghIkl-zyx57W2v1u123ew11` |
| `TELEGRAM_OWNER_ID` | Your Telegram User ID | `123456789` |
| `DB_PROVIDER` | Database provider | `PostgreSQL` |
| `DB_CONNECTION_STRING` | Database connection string | See database-specific examples below |

### Database Connection Strings

**PostgreSQL:**

```dotenv
DB_PROVIDER=PostgreSQL
DB_CONNECTION_STRING=Host=postgres;Port=5432;Database=${POSTGRES_DB};Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD}
```

**MySQL:**

```dotenv
DB_PROVIDER=MySQL
DB_CONNECTION_STRING=Server=mysql;Port=3306;Database=${MYSQL_DB};User=${MYSQL_USER};Password=${MYSQL_PASSWORD}
```

**SQL Server:**

```dotenv
DB_PROVIDER=SqlServer
DB_CONNECTION_STRING=Server=mssql,1433;Database=${MSSQL_DB};User Id=sa;Password=${MSSQL_SA_PASSWORD};TrustServerCertificate=true
```

### AI Provider Configuration

In `appsettings.json` or via environment variables:

```json
{
  "AiSettings": {
    "ChatProviders": [
      {
        "Name": "OpenAI",
        "ProviderType": "OpenAI",
        "ApiKey": "your_openai_key",
        "ModelName": "gpt-4o-mini"
      },
      {
        "Name": "Gemini",
        "ProviderType": "Gemini",
        "ApiKey": "your_gemini_key",
        "ModelName": "gemini-2.0-flash"
      },
      {
        "Name": "Grok",
        "ProviderType": "OpenAI",
        "BaseUrl": "https://api.x.ai/v1",
        "ApiKey": "your_grok_key",
        "ModelName": "grok-2"
      },
      {
        "Name": "DeepSeek",
        "ProviderType": "OpenAI",
        "BaseUrl": "https://api.deepseek.com",
        "ApiKey": "your_deepseek_key",
        "ModelName": "deepseek-chat"
      }
    ]
  }
}
```

---

## 🛠️ Troubleshooting

### Check running containers

```bash
cd /home/nudyk/Sites/GptChatTelegramBot
docker compose --profile postgres --profile cloudbeaver --profile aspire ps
docker compose --profile postgres --profile cloudbeaver --profile aspire logs bot --tail=50
```

### Re-deploy manually

```bash
docker compose --profile postgres --profile cloudbeaver --profile aspire down
docker compose --profile postgres --profile cloudbeaver --profile aspire up -d --build
```

### Check the generated `.env`

```bash
cat /home/nudyk/Sites/GptChatTelegramBot/.env
```

### Common Issues

**Bot not responding to commands:**

1. Verify `TELEGRAM_BOT_TOKEN` is correct
2. Check `TELEGRAM_OWNER_ID` is set (required for admin commands)
3. Ensure the bot has been started with `/start` in Telegram

**Database connection errors:**

1. Verify `DB_PROVIDER` matches your database type
2. Check `DB_CONNECTION_STRING` format and credentials
3. Ensure the database container is running: `docker compose ps`

**Webhook not working:**

1. Ensure `TELEGRAM_BASE_API_URL` is set to a public HTTPS URL
2. Use [ngrok](https://ngrok.com/) or [Cloudflare Tunnel](https://developers.cloudflare.com/cloudflare-one/connections/connect-networks/) for local testing
3. Check bot logs for webhook registration confirmation

---

## 📚 Related Documentation

- [Docker Deployment Guide](./DOCKER.md) — Detailed Docker Compose configuration
- [Hosting & Run Methods](./HOSTING.md) — All available deployment methods
- [Migrations Documentation](./MIGRATIONS.md) — Database migrations and provider switching
- [Observability Guide](./OBSERVABILITY.md) — Aspire Dashboard and monitoring
