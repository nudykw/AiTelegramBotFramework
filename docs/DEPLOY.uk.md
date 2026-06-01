# 🚀 Посібник з ручного розгортання

🇺🇸 [English](DEPLOY.md) | 🇺🇦 [Українська](DEPLOY.uk.md)

Цей посібник охоплює **методи ручного розгортання** проєкту GPTChatTelegramBot. Оберіть метод, який найкраще підходить для вашого середовища.

---

## 📋 Передумови

Перед розгортанням переконайтеся, що у вас є наступне:

| Елемент | Опис | Де отримати |
|---|---|---|
| **Токен Telegram бота** | Токен від [@BotFather](https://t.me/BotFather) | Telegram |
| **ID власника** | Ваш Telegram user ID | [@userinfobot](https://t.me/userinfobot) |
| **API ключ AI провайдера** | OpenAI, Gemini, Grok або DeepSeek ключ | Платформа провайдера |
| **Облікові дані БД** | PostgreSQL облікові дані (якщо використовуєте зовнішню БД) | Ваш провайдер БД |

---

## Спосіб 1: 🐳 Docker Compose (Рекомендовано для продакшену)

Рекомендований спосіб розгортання бота — через Docker Compose з підтримкою PostgreSQL.

### Вимоги

- [Docker](https://docs.docker.com/get-docker/) ≥ 24
- [Docker Compose](https://docs.docker.com/compose/install/) ≥ 2.20

### Кроки

#### 1. Клонуйте репозиторій

```bash
git clone https://github.com/nudykw/GptChatTelegramBot.git
cd GptChatTelegramBot
```

#### 2. Створіть файл середовища

```bash
cp .env.example .env
```

#### 3. Налаштуйте `.env`

Відкрийте `.env` і встановіть мінімально необхідні значення:

```dotenv
# Налаштування бота
TELEGRAM_BOT_TOKEN=ваш_токен_бота
TELEGRAM_OWNER_ID=ваш_telegram_user_id

# AI провайдер (оберіть хоча б одного)
OPENAI_API_KEY=sk-��аш_openai_ключ
GEMINI_API_KEY=ваш_gemini_ключ
GROK_API_KEY=ваш_grok_ключ
DEEPSEEK_API_KEY=ваш_deepseek_ключ

# База даних (приклад PostgreSQL)
POSTGRES_DB=gpt_chat_bot
POSTGRES_USER=postgres
POSTGRES_PASSWORD=ваш_надійний_пароль

# Опціонально: CloudBeaver (менеджер БД)
CLOUDBEAVER_PORT=8978
CLOUDBEAVER_ADMIN_NAME=cbadmin
CLOUDBEAVER_ADMIN_PASSWORD=ваш_надійний_пароль

# Опціонально: Aspire Dashboard (спостережуваність)
# Додайте 'aspire' до COMPOSE_PROFILES для увімкнення
```

#### 4. Оберіть вашу базу даних

Відредагуйте змінну `COMPOSE_PROFILES` у `.env`:

| `COMPOSE_PROFILES` | Запущені сервіси |
|---|---|
| `postgres,cloudbeaver` | PostgreSQL + CloudBeaver + Бот *(за замовчуванням)* |
| `postgres` | PostgreSQL + Бот |

Також оновіть `DB_PROVIDER` та `DB_CONNECTION_STRING` відповідно до вашої обраної бази даних.

#### 5. Запустіть стек

```bash
docker compose up -d
```

#### 6. Перевірте розгортання

```bash
# Перевірка запущених контейнерів
docker compose ps

# Перевірка логів бота
docker compose logs -f bot

# Перевірка здоров'я
curl http://localhost:8080/health
```

### Корисні команди

```bash
# Перегляд логів всіх сервісів
docker compose logs -f

# Перезапуск бота
docker compose restart bot

# Зупинка всіх сервісів
docker compose down

# Зупинка та видалення томів (⚠️ видаляє дані БД)
docker compose down -v

# Перебудова після змін коду
docker compose up -d --build bot
```

### Доступ до сервісів

| Сервіс | URL | Облікові дані |
|---|---|---|
| **Бот API** | `http://localhost:8080` | — |
| **CloudBeaver** | `http://localhost:${CLOUDBEAVER_PORT}` | `CLOUDBEAVER_ADMIN_NAME` / `CLOUDBEAVER_ADMIN_PASSWORD` |
| **Aspire Dashboard** | `http://localhost:18888` | — |

---

## Спосіб 2: 🖥️ systemd (Linux VPS без Docker)

Розгорніть бота як системний сервіс на Linux VPS без Docker.

### Вимоги

- Linux сервер з [.NET 10 Runtime](https://dotnet.microsoft.com/download) або self-contained publish
- SSH до��туп до сервера

### Кроки

#### 1. Зберіть self-contained publish

На вашому локальному комп'ютері:

```bash
# Консольна версія (без HTTP сервера)
dotnet publish TelegramBotApp/TelegramBotApp.csproj \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -o ./publish/console

# Або Web версія (з HTTP сервером)
dotnet publish TelegramBotWebApp/TelegramBotWebApp.csproj \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -o ./publish/web
```

#### 2. Скопіюйте файли на сервер

```bash
scp -r ./publish/console/* user@your-server:/opt/gpt-bot/
scp Configs/appsettings.json user@your-server:/opt/gpt-bot/Configs/
```

#### 3. Налаштуйте сервер

Підключіться до сервера та зробіть бінарний файл виконуваним:

```bash
chmod +x /opt/gpt-bot/TelegramBotApp
```

#### 4. Створіть systemd юніт

Створіть `/etc/systemd/system/gpt-bot.service`:

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

#### 5. Увімкніть та запустіть сервіс

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now gpt-bot
```

#### 6. Перевірте логи

```bash
sudo journalctl -u gpt-bot -f
```

---

## Спосіб 3: 🖥️ .NET CLI (Локальна розробка)

Найпростіший спосіб запустити бота локально для розробки та тестування.

### Вимоги

- [.NET 10 SDK](https://dotnet.microsoft.com/download)

### Кроки

#### 1. Налаштуйте `appsettings.json`

```bash
cp Configs/appsettings.sample.json Configs/appsettings.json
```

Відкрийте `Configs/appsettings.json` і заповніть необхідні поля:

```json
{
  "AppSettings": {
    "TelegramBotConfiguration": {
      "BotToken": "ваш_токен_бота",
      "OwnerId": 123456789,
      "AiSettings": {
        "ChatProviders": [
          {
            "Name": "OpenAI",
            "ProviderType": "OpenAI",
            "ApiKey": "ваш_openai_ключ",
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

#### 2. Запустіть бота

**Консольна версія (режим Polling):**

```bash
dotnet run --project TelegramBotApp/TelegramBotApp.csproj
```

**Web версія (режим Polling або Webhook):**

```bash
dotnet run --project TelegramBotWebApp/TelegramBotWebApp.csproj
```

**З увімкненим Swagger:**

```bash
ASPNETCORE_ENVIRONMENT=Development dotnet run --project TelegramBotWebApp/TelegramBotWebApp.csproj
```

### Доступні ендпоінти (Web версія)

| Метод | URL | Опис |
|---|---|---|
| `GET` | `http://localhost:8080/health` | Перевірка здоров'я |
| `GET` | `http://localhost:8080/api/info` | Інформація про бота |
| `GET` | `http://localhost:8080/scalar` | Swagger UI |
| `GET` | `http://localhost:8080/metrics` | Prometheus метрики |
| `POST` | `http://localhost:8080/aibot` | Приймач Telegram Webhook |

---

## 🔑 Посилання з конфігурації

### Необхідні змінні середовища

| Змінна | Опис | Приклад |
|---|---|---|
| `TELEGRAM_BOT_TOKEN` | Токен Telegram бота | `123456:ABC-DEF1234ghIkl-zyx57W2v1u123ew11` |
| `TELEGRAM_OWNER_ID` | Ваш Telegram User ID | `123456789` |
| `DB_PROVIDER` | Провайдер бази даних | `PostgreSQL` |
| `DB_CONNECTION_STRING` | Рядок підключення до БД | Див. приклади нижче |

### Рядки підключення до БД

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

### Конфігурація AI провайдера

В `appsettings.json` або через змінні середовища:

```json
{
  "AiSettings": {
    "ChatProviders": [
      {
        "Name": "OpenAI",
        "ProviderType": "OpenAI",
        "ApiKey": "ваш_openai_ключ",
        "ModelName": "gpt-4o-mini"
      },
      {
        "Name": "Gemini",
        "ProviderType": "Gemini",
        "ApiKey": "ваш_gemini_ключ",
        "ModelName": "gemini-2.0-flash"
      },
      {
        "Name": "Grok",
        "ProviderType": "OpenAI",
        "BaseUrl": "https://api.x.ai/v1",
        "ApiKey": "ваш_grok_ключ",
        "ModelName": "grok-2"
      },
      {
        "Name": "DeepSeek",
        "ProviderType": "OpenAI",
        "BaseUrl": "https://api.deepseek.com",
        "ApiKey": "ваш_deepseek_ключ",
        "ModelName": "deepseek-chat"
      }
    ]
  }
}
```

---

## 🛠️ Усунення несправностей

### Перевірка запущених контейнерів

```bash
cd /home/nudyk/Sites/GptChatTelegramBot
docker compose --profile postgres --profile cloudbeaver --profile aspire ps
docker compose --profile postgres --profile cloudbeaver --profile aspire logs bot --tail=50
```

### Повторне розгортання вручну

```bash
docker compose --profile postgres --profile cloudbeaver --profile aspire down
docker compose --profile postgres --profile cloudbeaver --profile aspire up -d --build
```

### Перевірка згенерованого `.env`

```bash
cat /home/nudyk/Sites/GptChatTelegramBot/.env
```

### Поширені проблеми

**Бот не відповідає на команди:**

1. Перевірте, що `TELEGRAM_BOT_TOKEN` правильний
2. Переконайтеся, що `TELEGRAM_OWNER_ID` встановлено (необхідно для адмін команд)
3. Переконайтеся, що бот був запущений з `/start` в Telegram

**Помилки підключення до БД:**

1. Перевірте, що `DB_PROVIDER` відповідає вашому типу БД
2. Перевірте формат та облікові дані `DB_CONNECTION_STRING`
3. Переконайтеся, що контейнер БД запущено: `docker compose ps`

**Webhook не працює:**

1. Переконайтеся, що `TELEGRAM_BASE_API_URL` встановлено на публічний HTTPS URL
2. Використовуйте [ngrok](https://ngrok.com/) або [Cloudflare Tunnel](https://developers.cloudflare.com/cloudflare-one/connections/connect-networks/) для локального тестування
3. Перевірте логи бота на підтвердження реєстрації webhook

---

## 📚 Пов'язана документація

- [Посібник з Docker розгортання](./DOCKER.md) — Детальна конфігурація Docker Compose
- [Способи хостингу та запуску](./HOSTING.md) — Усі доступні методи розгортання
- [Документація з міграцій](./MIGRATIONS.md) — Міграції БД та перемикання провайдерів
- [Посібник зі спостережуваності](./OBSERVABILITY.md) — Aspire Dashboard та моніторинг
