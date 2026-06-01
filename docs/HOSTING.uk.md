# 🚀 Способи запуску та хостингу

🇺🇸 [English](HOSTING.md) | 🇺🇦 [Українська](HOSTING.uk.md)

Застосунок має **дві точки входу** та підтримує кілька способів розгортання.

| | Консольна версія (`TelegramBotApp`) | Веб-версія (`TelegramBotWebApp`) |
|---|---|---|
| **Транспорт** | Тільки Polling | Polling **або** Webhook |
| **HTTP-сервер** | ❌ | ✅ `/health`, `/metrics`, Swagger |
| **Конфігурація** | `Configs/appsettings.json` | `.env` (Docker) або env-змінні |
| **Dockerfile** | `Dockerfile.console` | `Dockerfile.web` |
| **Коли використовувати** | Локальна розробка, простий деплой | Продакшн, Webhook, моніторинг |

---

## 1. 🖥️ Локально — консольна версія

Найпростіший спосіб для розробки та тестування.

### Вимоги

- [.NET 10 SDK](./setup-prerequisites.uk.md)

### Кроки

**1.** Скопіюйте файл конфігурації:

```bash
cp Configs/appsettings.sample.json Configs/appsettings.json
```

**2.** Відкрийте `Configs/appsettings.json` та заповніть обов'язкові поля:

```json
{
  "AppSettings": {
    "TelegramBotConfiguration": {
      "BotToken": "<ВАШ_ТОКЕН_БОТА>",
      "OwnerId": 123456789,
      "AiSettings": {
        "ChatProviders": [
          {
            "Name": "OpenAI",
            "ProviderType": "OpenAI",
            "ApiKey": "<ВАШ_OPENAI_КЛЮЧ>",
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

**3.** Запустіть:

```bash
dotnet run --project TelegramBotApp/TelegramBotApp.csproj
```

> [!NOTE]
> Дані SQLite за замовчуванням зберігаються у файлі `bot.db` в директорії запуску.

---

## 2. 🌐 Локально — веб-версія (ASP.NET Core)

Веб-версія надає HTTP API, що корисно для перегляду метрик та тестування Webhook.

### Вимоги

- [.NET 10 SDK](./setup-prerequisites.uk.md)

### Кроки

**1.** Якщо ще не зробили — налаштуйте `Configs/appsettings.json` (кроки 1–2 з попереднього розділу).

**2.** Запустіть:

```bash
dotnet run --project TelegramBotWebApp/TelegramBotWebApp.csproj
```

**3.** Або з увімкненим Swagger:

```bash
ASPNETCORE_ENVIRONMENT=Development dotnet run --project TelegramBotWebApp/TelegramBotWebApp.csproj
```

Після запуску доступні ендпоінти:

| Метод | URL | Опис |
|---|---|---|
| `GET` | `http://localhost:8080/health` | Перевірка стану |
| `GET` | `http://localhost:8080/api/info` | Інформація про бота |
| `GET` | `http://localhost:8080/scalar` | Swagger UI |
| `GET` | `http://localhost:8080/metrics` | Метрики Prometheus |

> [!TIP]
> Щоб протестувати Webhook локально, скористайтеся [ngrok](https://ngrok.com/) або [Cloudflare Tunnel](https://developers.cloudflare.com/cloudflare-one/connections/connect-networks/):
>
> ```bash
> cloudflared tunnel --url http://localhost:8080
> ```
>
> Потім встановіть `BaseApiUrl` у `appsettings.json` на згенерований HTTPS URL.

---

## 3. 🐳 Docker — веб-версія (рекомендовано для продакшну)

Повний стек через `docker compose` з підтримкою PostgreSQL та опціональним CloudBeaver.

### Вимоги

- [Docker](https://docs.docker.com/get-docker/) ≥ 24
- [Docker Compose](https://docs.docker.com/compose/install/) ≥ 2.20

### Кроки

**1.** Клонуйте репозиторій:

```bash
git clone https://github.com/nudykw/GptChatTelegramBot.git
cd GptChatTelegramBot
```

**2.** Створіть файл оточення:

```bash
cp .env.example .env
```

**3.** Відкрийте `.env` та заповніть мінімально необхідні поля:

```dotenv
TELEGRAM_BOT_TOKEN=ваш_токен
TELEGRAM_OWNER_ID=123456789
POSTGRES_PASSWORD=дуже_надійний_пароль
CLOUDBEAVER_ADMIN_PASSWORD=дуже_надійний_пароль
```

**4.** Оберіть базу даних через `COMPOSE_PROFILES`:

| `COMPOSE_PROFILES` | Що запускається |
|---|---|
| `postgres,cloudbeaver` | PostgreSQL + CloudBeaver + Бот *(за замовчуванням)* |
| `postgres` | PostgreSQL + Бот |

**5.** Запустіть:

```bash
docker compose up -d
```

**6.** Перевірте:

```bash
docker compose ps
curl http://localhost:8080/health
```

Повний опис усіх параметрів: [Посібник з Docker](./DOCKER.uk.md).

---

## 4. 🐳 Docker — консольна версія

Якщо хочете уникнути веб-сервера і запустити легший контейнер з консольним ботом через `Dockerfile.console`.

### Кроки

**1.** Налаштуйте `Configs/appsettings.json` (як у розділі 1).

**2.** Зберіть образ:

```bash
docker build -f Dockerfile.console -t gpt-telegram-bot-console .
```

**3.** Запустіть:

```bash
# SQLite — том для зберігання даних
docker run -d \
  --name gpt_bot_console \
  --restart unless-stopped \
  -v "$(pwd)/data:/app/data" \
  gpt-telegram-bot-console
```

**4.** Переглядайте логи:

```bash
docker logs -f gpt_bot_console
```

> [!NOTE]
> Консольна версія не має HTTP-сервера. Вона завжди працює в режимі **Polling**.

> [!TIP]
> Щоб використовувати PostgreSQL або іншу зовнішню БД, перевизначте рядок підключення через env-змінну:
>
> ```bash
> docker run -d \
>   --name gpt_bot_console \
>   -e "AppSettings__Database__Provider=PostgreSQL" \
>   -e "AppSettings__Database__ConnectionString=Host=yourhost;Port=5432;Database=gpt;Username=user;Password=pass" \
>   gpt-telegram-bot-console
> ```

---

## 5. 🖥️ Linux-сервер як системний сервіс (systemd)

Для деплою на VPS або виділений сервер без Docker — через systemd-юніт.

### Вимоги

- Linux-сервер із [.NET 10 Runtime](./setup-prerequisites.uk.md) або публікація Self-Contained

### Кроки

**1.** Зберіть Self-Contained публікацію (без потреби в .NET Runtime на сервері):

```bash
dotnet publish TelegramBotApp/TelegramBotApp.csproj \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -o ./publish/console
```

Або для веб-версії:

```bash
dotnet publish TelegramBotWebApp/TelegramBotWebApp.csproj \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -o ./publish/web
```

**2.** Скопіюйте файли на сервер:

```bash
scp -r ./publish/console/* user@your-server:/opt/gpt-bot/
scp Configs/appsettings.json user@your-server:/opt/gpt-bot/Configs/
```

**3.** Підключіться до сервера та зробіть файл виконуваним:

```bash
chmod +x /opt/gpt-bot/TelegramBotApp
```

**4.** Створіть systemd-юніт `/etc/systemd/system/gpt-bot.service`:

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

**5.** Увімкніть і запустіть сервіс:

```bash
sudo systemctl daemon-reload
sudo systemctl enable gpt-bot
sudo systemctl start gpt-bot
```

**6.** Перевірте логи:

```bash
sudo journalctl -u gpt-bot -f
```

---

## Порівняння способів

| Спосіб | Складність | Транспорт | HTTP API | Рекомендовано для |
|---|---|---|---|---|
| Локально (консоль) | ⭐ | Polling | ❌ | Розробка |
| Локально (веб) | ⭐⭐ | Polling / Webhook | ✅ | Розробка + тест Webhook |
| Docker Compose (веб) | ⭐⭐ | Polling / Webhook | ✅ | **Продакшн** ✅ |
| Docker (консоль) | ⭐⭐ | Тільки Polling | ❌ | Простий деплой |
| Systemd (Linux) | ⭐⭐⭐ | Polling / Webhook | ✅ | VPS без Docker |
