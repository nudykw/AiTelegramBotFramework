# Core Files Description

🇺🇸 English | 🇺🇦 [Українська](core_files.uk.md)

This document highlights the most important files in the repository to help you understand the application's flow and logic.

## [Program.cs](../TelegramBotApp/Program.cs)

**The Main Executable**

- Located in `TelegramBotApp`.
- Sets up the dependency injection container.
- Configures how services like `IChatService` (registered as `ResilientChatService`) and bot polling are wired together and started.

## [ResilientChatService.cs](../ServiceLayer/Services/ResilientChatService.cs)

**The Resilience Wrapper**

- Located in `ServiceLayer`.
- Implements `IChatService`.
- Automatically fails over to the next available provider if the current one fails.
- Ensures high availability of the chat bot.

## [ChatServiceFactory.cs](../ServiceLayer/Services/ChatServiceFactory.cs)

**The Service Creator**

- Located in `ServiceLayer`.
- Dynamically instantiates the appropriate `IChatService` implementation (OpenAI or Gemini) based on name and configuration.
- **Intelligent Configuration Filtering**: Automatically ignores any chat provider configurations where the `ApiKey` contains a placeholder like `[YOUR_...]`, preventing errors from incomplete setups.

## [MessageProcessor.cs](../ServiceLayer/Services/MessageProcessor/MessageProcessor.cs)

**The Brain of the Bot**

- Located in `ServiceLayer`.
- Coordinates all incoming update processing (text, voice, photos).
- **Intent Classification**: Uses a specialized AI classifier to decide whether a message requires image analysis, text response, or transcription.
- **Image Refinement**: Implements the "Vision-to-Prompt" workflow for high-quality GPT Image 2 regenerations.

## [TelegramBotConfiguration.cs](../ServiceLayer/Services/Telegram/Configurations/TelegramBotConfiguration.cs)

**Modular Settings Architecture**

- Defines the hierarchical structure of the bot's configuration.
- **AiSettings**: Centralizes model and provider settings for specialized tasks like `Vision`, `Drawing`, and `Classification`. Now also contains the `ChatProviders` list for a unified AI configuration.

## [ChatProviderConfig.cs](../ServiceLayer/Services/ChatProviderConfig.cs)

**Provider Metadata**

- Defines the structure for a single AI provider (Name, Type, API Key, Base URL, Model Name).

## [OpenAIService.cs](../ServiceLayer/Services/OpenAI/OpenAIService.cs)

**The Heart of OpenAI Integration**

- Located in `ServiceLayer`.
- Implements `IChatService` for OpenAI models, and also serves Grok and DeepSeek (OpenAI-compatible APIs).
- **Multimodal Support**: Handles text completions, image generation (GPT Image), image editing, vision analysis, and audio transcription.
- **Provider Capability Guard**: `GenerateImage` throws `NotSupportedException` immediately for providers without a drawing model (Grok, DeepSeek), enabling `ResilientChatService` to fall back cleanly to OpenAI.
- Calculates prices and generates billing items based on actual token usage.

## [AudioTranscriptorService.cs](../ServiceLayer/Services/AudioTranscriptor/AudioTranscriptorService.cs)

**Voice Processing Core**

- Located in `ServiceLayer`.
- Handles voice message transcription using OpenAI's Whisper API.
- Converts audio files into text to be processed by the chat services.

## [StoreContext.cs](../DataBaseLayer/Contexts/StoreContext.cs)

**The Data Gateway**

- Located in `DataBaseLayer`.
- Extends `DbContext` and defines how everything is persisted in the PostgreSQL database.
- Central point for managing chat history, user mapping, and billing stats.

## [AppSettings.cs](../ServiceLayer/AppSettings.cs)

**Configuration Structure**

- A POCO (Plain Old CLR Object) class that maps the `appsettings.json` content into a strongly-typed object.
- Highly useful for accessing secrets and limits across the entire solution.

## [Configs/](../Configs)

**Centralised Configuration Directory**

- Contains all environment-specific configuration files. None of the files with real secrets are committed to git.

| File | Tracked | Purpose |
|------|---------|---------|
| `appsettings.sample.json` | ✅ yes | Full template with placeholder values — reference for all available settings |
| `appsettings.Test.json.example` | ✅ yes | Template for integration-test API keys |
| `appsettings.json` | ❌ gitignored | Real keys used by the running bot |
| `appsettings.web.json` | ❌ gitignored | Webhook-mode overrides |
| `appsettings.console.json` | ❌ gitignored | Polling-mode overrides |
| `appsettings.Test.json` | ❌ gitignored | Real API keys for integration tests |

To set up locally: copy the relevant `*.example` file, rename it (drop `.example`), and fill in your keys.

## [ChatGeminiService.cs](../ServiceLayer/Services/GeminiChat.DotNet/ChatGeminiService.cs)

**Google AI Implementation**

- Bridges the gaps between the generic `IChatService` and Google's Gemini-specific API structure.
- Handles the conversion of message history into a format Gemini can digest.

## [McpServerManager.cs](../ServiceLayer/Services/Mcp/McpServerManager.cs)

**The Tool Hub & MCP Client**

- Located in `ServiceLayer`.
- Manages the lifecycle of MCP tool servers.
- **Isolated Execution**: Launches tools inside a specialized Docker runtime via `docker exec` for security and consistency.
- **Auto-Seeding**: Automatically synchronizes tools from `appsettings.json` with the database at startup.
- **Health Checks**: Performs connectivity tests on startup and logs status using `[HEALTH CHECK]` markers.

## [BotCommands.cs](../ServiceLayer/Constans/BotCommands.cs)

**Command Registry**

- Centralized storage for all main bot commands (`/model`, `/provider`, `/billing`, `/restart`) and their Russian descriptions.
- Used for both message routing in `UpdateHandler` and automatic menu synchronization in `ReceiverServiceBase`.

## [ReceiverServiceBase.cs](../ServiceLayer/Services/Telegram/ReceiverServiceBase.cs)

**The Update Receiver**

- Calls `SetMyCommandsAsync` upon bot startup to ensure the Telegram menu is always up-to-date with the supported commands.

## [ReactionService.cs](../ServiceLayer/Services/Telegram/ReactionService.cs)

**Feedback Management Core**

- Located in `ServiceLayer`.
- Implements `IReactionService`.
- Manages the logic for toggling reactions (Like, Dislike, Laugh, etc.) with group-based mutual exclusivity.
- Provides reaction counts for messages to be displayed in the UI.

## [MessageReaction.cs](../DataBaseLayer/Models/MessageReaction.cs)

**Feedback Persistence**

- Located in `DataBaseLayer`.
- Stores user reactions to AI messages.
- Ensures data integrity through a unique constraint on `(ChatId, MessageId, UserId)`.

---

## 📚 Related Documentation

- **[Project Descriptions](./projects.md)** — Detailed overview of each project in the solution
- **[Testing Strategy](./tests.md)** — How to run unit and integration tests
- **[Hosting & Deployment](./HOSTING.md)** — All available deployment methods
- **[Docker Deployment](./DOCKER.md)** — Complete Docker Compose configuration guide
- **[Observability](./OBSERVABILITY.md)** — Aspire Dashboard and CloudBeaver monitoring
