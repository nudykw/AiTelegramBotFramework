# Спостережуваність — Aspire Dashboard та CloudBeaver

🇺🇸 [English](OBSERVABILITY.md) | 🇺🇦 [Українська](OBSERVABILITY.uk.md)

Цей документ пояснює, як моніторити GptChatTelegramBot за допомогою вбудованого стеку спостережуваності:

- **Aspire Dashboard** — метрики, трейси та структуровані логи в реальному часі від усіх сервісів
- **CloudBeaver** — веб-менеджер бази даних для перегляду та запитів до БД бота

---

## Зміст

- [Архітектура](#архітектура)
- [Aspire Dashboard](#aspire-dashboard)
  - [Активація Dashboard](#активація-dashboard)
  - [Структуровані логи](#структуровані-логи)
  - [Трейси](#трейси)
  - [Метрики](#метрики)
- [CloudBeaver](#cloudbeaver)
  - [Перший вхід та налаштування](#перший-вхід-та-налаштування)
  - [Підключення до бази даних](#підключення-до-бази-даних)
  - [Перегляд таблиць](#перегляд-таблиць)
  - [SQL запити](#sql-запити)

---

## Архітектура

Коли активний профіль `aspire`, телеметрія від всіх сервісів потрапляє в єдиний дашборд:

```text
GptChatTelegramBot  ──── OTLP/gRPC ────►
PostgreSQL          ──── OTel Collector ►  Aspire Dashboard  :18888
CloudBeaver         ──── Java Agent ────►
MySQL/MSSQL         ──── OTel Collector ►  (помилки якщо не запущені — це нормально)
```

| Сервіс | Що збирається | Протокол |
|---|---|---|
| **GptChatTelegramBot** | .NET Runtime метрики, HTTP трейси, структуровані логи | OTLP/gRPC (порт 18889) |
| **Databases** | PostgreSQL/MySQL/MSSQL метрики (з'єднання, рядки, розмір БД…) | OTel Collector → OTLP |
| **CloudBeaver** | JVM метрики, HTTP трейси | OTel Java Agent → OTLP |

---

## Aspire Dashboard

### Активація Dashboard

Додай `aspire` до `COMPOSE_PROFILES` у файлі `.env`:

```env
COMPOSE_PROFILES=postgres,cloudbeaver,aspire
```

Потім запусти сервіси:

```bash
docker compose up -d
```

Dashboard буде доступний за адресою **<http://localhost:18888>** (порт налаштовується через `ASPIRE_PORT`).

> **Примітка:** Стандартна конфігурація використовує режим `Unsecured` — безпечно для локальної розробки.  
> Для продакшну [налаштуй аутентифікацію](https://learn.microsoft.com/uk-ua/dotnet/aspire/fundamentals/dashboard/configuration#openid-connect-authentication).

---

### Структуровані логи

Сторінка **Structured Logs** показує всі записи логів від усіх сервісів у реальному часі.

![Демо Aspire Dashboard — логи, трейси та метрики](assets/aspire-dashboard-demo.gif)

**Можливості:**

- Фільтрація за **ресурсом** (GptChatTelegramBot, CloudBeaver…)
- Фільтрація за **рівнем логу** (Information, Warning, Error, Critical)
- Повнотекстовий пошук по тексту повідомлень
- Клік на рядок → розкриті структуровані властивості

---

### Трейси

Сторінка **Traces** показує розподілені трейс-спани — корисно для відстеження HTTP запитів.

> 💡 Анімація вище також демонструє сторінки Traces та Metrics.

**Можливості:**

- Фільтрація за ресурсом, тривалістю трейсу та статусом
- Клік на трейс → повне дерево спанів з розбивкою часу
- HTTP клієнт-виклики (до Telegram API, OpenAI) трейсуються автоматично

---

### Метрики

Використовуй **Resource** dropdown для перемикання між:

#### GptChatTelegramBot — .NET Runtime

![Метрики .NET Runtime бота](assets/aspire-metrics-bot.png)

Ключові метрики:

- `dotnet.process.memory.working_set` — використання пам'яті
- `dotnet.process.cpu.time` — навантаження на CPU
- `dotnet.gc.collections` — активність збирача сміття
- `http.client.request.duration` — затримка викликів до Telegram/OpenAI
- `dotnet.thread_pool.queue.length` — навантаження на пул потоків

#### Databases — PostgreSQL

![Метрики PostgreSQL — розмір бази даних](assets/aspire-metrics-postgres.png)

Ключові метрики (збираються OTel Collector кожні 30 секунд):

- `postgresql.db_size` — загальний розмір бази даних у байтах
- `postgresql.rows` — рядки вставлені/оновлені/видалені
- `postgresql.commits` / `postgresql.rollbacks` — частота транзакцій
- `postgresql.connection.max` — стан пулу з'єднань
- `postgresql.bgwriter.*` — активність фонового запису

#### CloudBeaver — JVM

![JVM метрики CloudBeaver](assets/aspire-metrics-cloudbeaver.png)

Ключові метрики (збираються OTel Java Agent):

- `jvm.memory.used` — використання heap та non-heap пам'яті
- `jvm.gc.duration` — паузи збирача сміття
- `http.server.request.duration` — час відповіді UI CloudBeaver

---

## CloudBeaver

CloudBeaver — веб-менеджер БД з попередньо налаштованим підключенням до бази бота.

### Перший вхід та налаштування

1. Переконайся що профіль `cloudbeaver` активний у `COMPOSE_PROFILES`
2. Відкрий **<http://localhost:8978>** (порт налаштовується через `CLOUDBEAVER_PORT`)
3. При першому запуску пройди майстер налаштування:
   - Вкажи назву сервера (будь-яку)
   - Створи адмін-акаунт — запам'ятай логін і пароль!
4. Увійди з даними адміністратора

![Робоча область CloudBeaver після входу](assets/cloudbeaver-workspace.png)

---

### Підключення до бази даних

Підключення **GPT Bot Database (PostgreSQL)** вже попередньо налаштоване.  
При першій спробі розкрити його з'явиться діалог **Database Authentication**:

| Поле | Значення |
|---|---|
| User name | значення `POSTGRES_USER` у `.env` (за замовчуванням: `postgres`) |
| User password | значення `POSTGRES_PASSWORD` у `.env` |

> **Порада:** Постав галочку **"Don't ask again during the session"** щоб не вводити пароль знову.

---

### Перегляд таблиць

Після підключення розкрий дерево:

```text
GPT Bot Database (PostgreSQL)
└── Databases
    └── gpt_chat_bot
        └── Schemas
            └── public
                └── Tables
                    ├── AIBilingItem
                    ├── BalanceHistories
                    ├── CachedAIModels
                    ├── CachedTranslations
                    ├── Messages
                    ├── TelegramChatInfos
                    └── TelegramUserInfos
```

![CloudBeaver: вхід → дерево БД → SQL запит](assets/cloudbeaver-demo.gif)

Подвійний клік на таблицю відкриває **переглядач даних** з пагінацією та фільтрацією.

---

### SQL запити

Натисни кнопку **SQL** на верхній панелі інструментів щоб відкрити SQL-редактор.

> 💡 Анімація вище демонструє повний процес: вхід → розкриття дерева БД → написання та виконання SQL запиту.

**Приклади запитів:**

```sql
-- Список зареєстрованих користувачів
SELECT "Id", "FirstName", "LastName", "Username"
FROM "TelegramUserInfos"
LIMIT 10;

-- Останні повідомлення
SELECT m."Content", m."CreatedAt", u."Username"
FROM "Messages" m
JOIN "TelegramUserInfos" u ON m."TelegramUserInfoId" = u."Id"
ORDER BY m."CreatedAt" DESC
LIMIT 20;

-- Історія балансу
SELECT u."Username", b."Amount", b."CreatedAt"
FROM "BalanceHistories" b
JOIN "TelegramUserInfos" u ON b."TelegramUserInfoId" = u."Id"
ORDER BY b."CreatedAt" DESC
LIMIT 20;
```

**Гарячі клавіші:**

- `Ctrl+Enter` — виконати запит
- `Ctrl+Space` — автодоповнення
- `Ctrl+/` — закоментувати/розкоментувати рядок

---

## Довідник налаштувань

Всі налаштування спостережуваності у `.env` / `.env.example`:

| Змінна | За замовчуванням | Опис |
|---|---|---|
| `OTEL_EXPORTER_OTLP_ENDPOINT` | `http://aspire-dashboard:18889` | OTLP endpoint для бота |
| `ASPIRE_PORT` | `18888` | Порт UI Aspire Dashboard |
| `CLOUDBEAVER_PORT` | `8978` | Порт UI CloudBeaver |
| `CLOUDBEAVER_JAVA_TOOL_OPTIONS` | *(порожнє)* | Встанови `-javaagent:/otel-agent/opentelemetry-javaagent.jar` щоб увімкнути OTel для CloudBeaver |
