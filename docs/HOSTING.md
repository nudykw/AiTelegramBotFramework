# 🚀 Hosting & Deployment Guide

🇺🇸 English | 🇺🇦 [Українська](HOSTING.uk.md)

The application has **two entry points** and supports multiple deployment methods.

| | Console app (`TelegramBotApp`) | Web app (`TelegramBotWebApp`) |
|---|---|---|
| **Transport** | Polling only | Polling **or** Webhook |
| **HTTP server** | ❌ | ✅ `/health`, `/metrics`, Swagger |
| **Configuration** | `Configs/appsettings.json` | `.env` (Docker) or env vars |
| **Dockerfile** | `Dockerfile.console` | `Dockerfile.web` |
| **Best for** | Local dev, simple deploy | Production, Webhook, monitoring |

---

## 1. 🖥️ Local — Console Version

The simplest way to get started for development and testing.

### Requirements

- [.NET 10 SDK](./setup-prerequisites.md)

### Steps

**1.** Copy the configuration file:

```bash
cp Configs/appsettings.sample.json Configs/appsettings.json
```

**2.** Open `Configs/appsettings.json` and fill in the required fields:

```json
{
  "AppSettings": {
    "TelegramBotConfiguration": {
      "BotToken": "<YOUR_BOT_TOKEN>",
      "OwnerId": 123456789,
      "AiSettings": {
        "ChatProviders": [
          {
            "Name": "OpenAI",
            "ProviderType": "OpenAI",
            "ApiKey": "<YOUR_OPENAI_KEY>",
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

**3.** Run:

```bash
dotnet run --project TelegramBotApp/TelegramBotApp.csproj
```

> [!NOTE]
> SQLite data is stored in `bot.db` in the working directory by default.

---

## 2. 🌐 Local — Web Version (ASP.NET Core)

The web version exposes an HTTP API, useful for metrics and Webhook testing.

### Requirements

- [.NET 10 SDK](./setup-prerequisites.md)

### Steps

**1.** If you haven't already, configure `Configs/appsettings.json` (steps 1–2 above).

**2.** Run:

```bash
dotnet run --project TelegramBotWebApp/TelegramBotWebApp.csproj
```

**3.** Or with Swagger enabled:

```bash
ASPNETCORE_ENVIRONMENT=Development dotnet run --project TelegramBotWebApp/TelegramBotWebApp.csproj
```

After startup, the following endpoints are available:

| Method | URL | Description |
|---|---|---|
| `GET` | `http://localhost:8080/health` | Health check |
| `GET` | `http://localhost:8080/api/info` | Bot information |
| `GET` | `http://localhost:8080/scalar` | Swagger UI |
| `GET` | `http://localhost:8080/metrics` | Prometheus metrics |

> [!TIP]
> To test Webhook locally, use [ngrok](https://ngrok.com/) or [Cloudflare Tunnel](https://developers.cloudflare.com/cloudflare-one/connections/connect-networks/):
>
> ```bash
> cloudflared tunnel --url http://localhost:8080
> ```
>
> Then set `BaseApiUrl` in `appsettings.json` to the generated HTTPS URL.

---

## 3. 🐳 Docker Compose — Web Version (recommended for production)

Full stack via `docker compose` with support for PostgreSQL and an optional CloudBeaver web database manager.

### Requirements

- [Docker](https://docs.docker.com/get-docker/) ≥ 24
- [Docker Compose](https://docs.docker.com/compose/install/) ≥ 2.20

### Steps

**1.** Clone the repository:

```bash
git clone https://github.com/nudykw/GptChatTelegramBot.git
cd GptChatTelegramBot
```

**2.** Create the environment file:

```bash
cp .env.example .env
```

**3.** Open `.env` and fill in the minimum required fields:

```dotenv
TELEGRAM_BOT_TOKEN=your_token
TELEGRAM_OWNER_ID=123456789
POSTGRES_PASSWORD=a_very_strong_password
CLOUDBEAVER_ADMIN_PASSWORD=a_very_strong_password
```

**4.** Select your database via `COMPOSE_PROFILES`:

| `COMPOSE_PROFILES` | What starts |
|---|---|
| `postgres,cloudbeaver` | PostgreSQL + CloudBeaver + Bot *(default)* |
| `postgres` | PostgreSQL + Bot |

**5.** Start the stack:

```bash
docker compose up -d
```

**6.** Verify:

```bash
docker compose ps
curl http://localhost:8080/health
```

Full parameter reference: [Docker Deployment Guide](./DOCKER.md).

---

## 4. 🐳 Docker — Console Version

If you want to avoid the web server overhead, you can run the lighter console bot using `Dockerfile.console`.

### Steps

**1.** Configure `Configs/appsettings.json` (see section 1).

**2.** Build the image:

```bash
docker build -f Dockerfile.console -t gpt-telegram-bot-console .
```

**3.** Run:

```bash
# SQLite — persistent data volume
docker run -d \
  --name gpt_bot_console \
  --restart unless-stopped \
  -v "$(pwd)/data:/app/data" \
  gpt-telegram-bot-console
```

**4.** View logs:

```bash
docker logs -f gpt_bot_console
```

> [!NOTE]
> The console version has no HTTP server. It always runs in **Polling** mode.

> [!TIP]
> To use PostgreSQL or another external database, override the connection string via environment variable:
>
> ```bash
> docker run -d \
>   --name gpt_bot_console \
>   -e "AppSettings__Database__Provider=PostgreSQL" \
>   -e "AppSettings__Database__ConnectionString=Host=yourhost;Port=5432;Database=gpt;Username=user;Password=pass" \
>   gpt-telegram-bot-console
> ```

---

## 5. 🖥️ Linux Server as a System Service (systemd)

For deploying to a VPS or dedicated server without Docker — using a systemd unit.

### Requirements

- Linux server with [.NET 10 Runtime](./setup-prerequisites.md) or a self-contained publish

### Steps

**1.** Build a self-contained publish (no .NET runtime required on the server):

```bash
dotnet publish TelegramBotApp/TelegramBotApp.csproj \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -o ./publish/console
```

Or for the web version:

```bash
dotnet publish TelegramBotWebApp/TelegramBotWebApp.csproj \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -o ./publish/web
```

**2.** Copy files to the server:

```bash
scp -r ./publish/console/* user@your-server:/opt/gpt-bot/
scp Configs/appsettings.json user@your-server:/opt/gpt-bot/Configs/
```

**3.** Connect to the server and make the binary executable:

```bash
chmod +x /opt/gpt-bot/TelegramBotApp
```

**4.** Create a systemd unit at `/etc/systemd/system/gpt-bot.service`:

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

**5.** Enable and start the service:

```bash
sudo systemctl daemon-reload
sudo systemctl enable gpt-bot
sudo systemctl start gpt-bot
```

**6.** Check the logs:

```bash
sudo journalctl -u gpt-bot -f
```

---

## Comparison

| Method | Complexity | Transport | HTTP API | Best for |
|---|---|---|---|---|
| Local (console) | ⭐ | Polling | ❌ | Development |
| Local (web) | ⭐⭐ | Polling / Webhook | ✅ | Dev + Webhook testing |
| Docker Compose (web) | ⭐⭐ | Polling / Webhook | ✅ | **Production** ✅ |
| Docker (console) | ⭐⭐ | Polling only | ❌ | Simple deploy |
| Systemd (Linux) | ⭐⭐⭐ | Polling / Webhook | ✅ | VPS without Docker |
