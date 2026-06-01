# Опис ключових файлів

🇺🇸 [English](core_files.md) | 🇺🇦 [Українська](core_files.uk.md)

Цей документ визначає найважливіші файли у репозиторії для розуміння потоку та логіки застосунку.

## [Program.cs](../TelegramBotApp/Program.cs)

**Головний виконуваний файл**

- Розташований у `TelegramBotApp`.
- Налаштовує контейнер впровадження залежностей.
- Конфігурує підключення сервісів: `IChatService` (зареєстрований як `ResilientChatService`) та запуск полінгу бота.

## [ResilientChatService.cs](../ServiceLayer/Services/ResilientChatService.cs)

**Обгортка відмовостійкості**

- Розташований у `ServiceLayer`.
- Реалізує `IChatService`.
- Автоматично перемикається на наступного доступного провайдера у разі збою поточного.
- Забезпечує високу доступність чат-бота.

## [ChatServiceFactory.cs](../ServiceLayer/Services/ChatServiceFactory.cs)

**Фабрика сервісів**

- Розташований у `ServiceLayer`.
- Динамічно створює відповідну реалізацію `IChatService` (OpenAI або Gemini) на основі назви та конфігурації.
- **Інтелектуальна фільтрація**: Автоматично ігнорує конфігурації провайдерів, де `ApiKey` містить заглушку вигляду `[YOUR_...]`, запобігаючи помилкам від неповних налаштувань.

## [MessageProcessor.cs](../ServiceLayer/Services/MessageProcessor/MessageProcessor.cs)

**Мозок бота**

- Розташований у `ServiceLayer`.
- Координує обробку всіх вхідних оновлень (текст, голос, фото).
- **Класифікація намірів**: Використовує спеціалізований AI-класифікатор, щоб визначити, що потрібно: аналіз зображення, текстова відповідь чи транскрипція.
- **Покращення зображень**: Реалізує workflow «Vision-to-Prompt» для якісної регенерації через GPT Image 2.

## [TelegramBotConfiguration.cs](../ServiceLayer/Services/Telegram/Configurations/TelegramBotConfiguration.cs)

**Модульна архітектура налаштувань**

- Визначає ієрархічну структуру конфігурації бота.
- **AiSettings**: Централізує налаштування моделей і провайдерів для спеціалізованих завдань: `Vision`, `Drawing` та `Classification`. Також містить список `ChatProviders` для єдиної конфігурації AI.

## [ChatProviderConfig.cs](../ServiceLayer/Services/ChatProviderConfig.cs)

**Метадані провайдера**

- Визначає структуру для одного AI-провайдера (Name, Type, API Key, Base URL, Model Name).

## [OpenAIService.cs](../ServiceLayer/Services/OpenAI/OpenAIService.cs)

**Серце інтеграції з OpenAI**

- Розташований у `ServiceLayer`.
- Реалізує `IChatService` для моделей OpenAI, а також обслуговує Grok і DeepSeek (OpenAI-сумісні API).
- **Мультимодальна підтримка**: Обробляє текстові діалоги, генерацію зображень (GPT Image), редагування зображень, візуальний аналіз та транскрипцію аудіо.
- **Захист можливостей провайдера**: `GenerateImage` кидає `NotSupportedException` одразу для провайдерів без моделі малювання (Grok, DeepSeek), дозволяючи `ResilientChatService` коректно перемикатися на OpenAI.
- Розраховує вартість і генерує білінгові записи на основі реального використання токенів.

## [AudioTranscriptorService.cs](../ServiceLayer/Services/AudioTranscriptor/AudioTranscriptorService.cs)

**Ядро обробки голосу**

- Розташований у `ServiceLayer`.
- Обробляє транскрипцію голосових повідомлень через Whisper API від OpenAI.
- Конвертує аудіофайли у текст для подальшої обробки сервісами чату.

## [StoreContext.cs](../DataBaseLayer/Contexts/StoreContext.cs)

**Шлюз до даних**

- Розташований у `DataBaseLayer`.
- Розширює `DbContext` та визначає, як все зберігається в базі даних.
- Центральна точка управління історією чату, маппінгом користувачів та білінговою статистикою.

## [AppSettings.cs](../ServiceLayer/AppSettings.cs)

**Структура конфігурації**

- POCO-клас (Plain Old CLR Object), що відображає вміст `appsettings.json` у строго типізований об'єкт.
- Зручний для доступу до секретів і лімітів з будь-якої частини рішення.

## [Configs/](../Configs)

**Централізована директорія конфігурацій**

- Містить усі конфігураційні файли, специфічні для середовища. Жоден файл з реальними секретами не комітиться в git.

| Файл | В git | Призначення |
|------|-------|-------------|
| `appsettings.sample.json` | ✅ так | Повний шаблон із заглушками — довідник усіх доступних налаштувань |
| `appsettings.Test.json.example` | ✅ так | Шаблон для API-ключів інтеграційних тестів |
| `appsettings.json` | ❌ gitignored | Реальні ключі для запуску бота |
| `appsettings.web.json` | ❌ gitignored | Перевизначення для Webhook-режиму |
| `appsettings.console.json` | ❌ gitignored | Перевизначення для Polling-режиму |
| `appsettings.Test.json` | ❌ gitignored | Реальні API-ключі для інтеграційних тестів |

Для налаштування локально: скопіюйте відповідний файл `*.example`, перейменуйте (приберіть `.example`) та заповніть ключі.

## [ChatGeminiService.cs](../ServiceLayer/Services/GeminiChat.DotNet/ChatGeminiService.cs)

**Реалізація Google AI**

- Усуває прогалини між загальним `IChatService` та специфічною структурою API Google Gemini.
- Обробляє конвертацію історії повідомлень у формат, який розуміє Gemini.

## [McpServerManager.cs](../ServiceLayer/Services/Mcp/McpServerManager.cs)

**Центр інструментів та MCP-клієнт**

- Знаходиться в `ServiceLayer`.
- Керує життєвим циклом серверів інструментів MCP.
- **Ізольоване виконання**: Запускає інструменти всередині спеціалізованого Docker-середовища через `docker exec` для безпеки та стабільності.
- **Автоматичний сидінг**: Автоматично синхронізує інструменти з `appsettings.json` із базою даних при старті.
- **Health Checks**: Виконує перевірку зв'язку при старті та логує статус за допомогою маркерів `[HEALTH CHECK]`.

## [BotCommands.cs](../ServiceLayer/Constans/BotCommands.cs)

**Реєстр команд**

- Централізоване сховище всіх головних команд бота (`/model`, `/provider`, `/billing`, `/restart`) та їх описів.
- Використовується для маршрутизації повідомлень у `UpdateHandler` та автоматичної синхронізації меню у `ReceiverServiceBase`.

## [ReceiverServiceBase.cs](../ServiceLayer/Services/Telegram/ReceiverServiceBase.cs)

**Приймач оновлень**

- Викликає `SetMyCommandsAsync` при запуску бота, щоб меню Telegram завжди відображало актуальні команди.

## [ReactionService.cs](../ServiceLayer/Services/Telegram/ReactionService.cs)

**Ядро керування відгуками**

- Знаходиться в `ServiceLayer`.
- Реалізує `IReactionService`.
- Керує логікою перемикання реакцій (Like, Dislike, Laugh тощо) з логікою взаємного виключення в межах груп.
- Надає лічильники реакцій для повідомлень для відображення в інтерфейсі.

## [MessageReaction.cs](../DataBaseLayer/Models/MessageReaction.cs)

**Збереження реакцій**

- Знаходиться в `DataBaseLayer`.
- Зберігає реакції користувачів на повідомлення ШІ.
- Забезпечує цілісність даних через унікальний індекс на `(ChatId, MessageId, UserId)`.
