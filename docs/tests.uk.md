# Стратегія тестування

🇺🇸 [English](tests.md) | 🇺🇦 [Українська](tests.uk.md)

Цей документ описує стратегію тестування, що покривається, і як запускати тести локально з реальними API-ключами.

## Розташування тестів

Усі тести знаходяться у директорії `tests/` у кореневій папці рішення.

---

## [ServiceLayer.UnitTests](../tests/ServiceLayer.UnitTests)

- **Мета**: Ізольоване тестування бізнес-логіки у `ServiceLayer`.
- **Технології**: xUnit, Moq, FluentAssertions.
- **Ключові файли**:
  - `ChatGptServiceTests.cs`: Тести вибору моделі, розрахунку вартості та обробки відповідей OpenAI.
  - `ChatGptPriceUpdateTests.cs`: Тести оновлення цін моделей із зовнішніх джерел.

---

## [ServiceLayer.IntegrationTests](../tests/ServiceLayer.IntegrationTests)

- **Мета**: Наскрізна перевірка коректної роботи інтеграцій з AI-провайдерами.
- **Технології**: xUnit, Moq, Testcontainers (тести БД), InMemory EF Core.
- **Ключові файли**:
  - `Fixtures/TestAppFixture.cs` — Спільний тестовий хост; завантажує конфігурацію, реєструє InMemory БД та всі реальні сервіси.
  - `DatabaseSupportTests.cs` — Перевіряє міграції БД проти реальних контейнерів (PostgreSQL). Потребує Docker.
  - `Services/OpenAI/OpenAIServiceTests.cs` — Реальні запити до OpenAI API: чат, генерація зображень, транскрипція аудіо, білінг.
  - `Services/OpenAI/OpenAIPriceIntegrationTests.cs` — Отримує актуальні ціни моделей з LiteLLM та перевіряє їх парсинг.
  - `Services/ModelsIntegrationTests.cs` — Перелічує та перевіряє моделі для всіх налаштованих провайдерів.
  - `Services/ImageGenerationLogicTests.cs` — Тестує генерацію зображень кожним провайдером та fallback у `ResilientChatService` з реальними API-запитами.

---

## Конфігурація для інтеграційних тестів

Інтеграційні тести, що виконують запити до AI API, потребують реальних API-ключів. Ключі **ніколи не комітяться в git**.

### Налаштування

1. Скопіюйте шаблон:

   ```bash
   cp Configs/appsettings.Test.json.example Configs/appsettings.Test.json
   ```

2. Заповніть свої API-ключі у `Configs/appsettings.Test.json`.

Файл завантажується як другий шар конфігурації поверх `TelegramBotApp/appsettings.json`, тому потрібно вказувати лише ті секції, які ви перевизначаєте.

> **Увага**: `Configs/appsettings.Test.json` є в `.gitignore` і ніколи не буде закомічено.

### Порядок завантаження конфігурації (TestAppFixture)

| Пріоритет | Файл | Розташування | В git |
|-----------|------|--------------|-------|
| 1 (база) | `appsettings.json` | посилання з `TelegramBotApp/` | ✅ так |
| 2 (перевизначення) | `appsettings.Test.json` | `Configs/` | ❌ gitignored |

Тести без налаштованих ключів провалюються з чітким повідомленням `AiProviderException: Unauthorized` — жодних тихих проходжень.

---

## Як запустити тести

Запустити всі тести:

```bash
dotnet test
```

Запустити конкретний проєкт:

```bash
dotnet test tests/ServiceLayer.IntegrationTests
dotnet test tests/ServiceLayer.UnitTests
```

Пропустити тести БД, що потребують Docker (запустити лише AI/сервісні тести):

```bash
dotnet test tests/ServiceLayer.IntegrationTests --filter "FullyQualifiedName!~DatabaseSupport"
```

Запустити лише інтеграційні тести AI-сервісів:

```bash
dotnet test tests/ServiceLayer.IntegrationTests --filter "Service=OpenAIService|Service=ModelVerification|FullyQualifiedName~ImageGeneration"
```

Запустити тести за категорією Trait:

```bash
dotnet test --filter Service=OpenAIService
dotnet test --filter Service=ModelVerification
```
