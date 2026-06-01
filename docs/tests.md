# Testing Strategy

🇺🇸 English | 🇺🇦 [Українська](tests.uk.md)

This document describes the testing strategy, what is covered, and how to run tests locally with real API keys.

## Test Location

All tests are located in the `tests/` directory at the root of the solution.

---

## [ServiceLayer.UnitTests](../tests/ServiceLayer.UnitTests)

- **Objective**: Isolated testing of business logic in the `ServiceLayer`.
- **Technologies**: xUnit, Moq, FluentAssertions.
- **Coverage**: ~52 tests covering billing, localization, model selection, and AI services.
- **Key Files**:
  - `OpenAIServiceTests.cs`: Tests for model selection, price calculation, and response handling for OpenAI.
  - `OpenAIPriceUpdateTests.cs`: Tests for model pricing updates from LiteLLM (includes race-condition handling).

---

## [ServiceLayer.IntegrationTests](../tests/ServiceLayer.IntegrationTests)

- **Objective**: End-to-end verification that all AI provider integrations work correctly.
- **Technologies**: xUnit, Moq, Testcontainers (DB tests), InMemory EF Core.
- **Key Files**:
  - `Fixtures/TestAppFixture.cs` — Shared test host; loads config, registers InMemory DB and all real services.
  - `DatabaseSupportTests.cs` — Verifies DB migrations work against real containers (PostgreSQL). Requires Docker.
  - `Services/OpenAI/OpenAIServiceTests.cs` — Real OpenAI API calls: chat, image generation, audio transcription, billing.
  - `Services/OpenAI/OpenAIPriceIntegrationTests.cs` — Fetches live model prices from LiteLLM and verifies parsing.
  - `Services/ModelsIntegrationTests.cs` — Lists and validates models for all configured providers.
  - `Services/ImageGenerationLogicTests.cs` — Tests per-provider image generation and `ResilientChatService` fallback with real API calls.

---

## Configuration for Integration Tests

Integration tests that call AI APIs require real API keys. Keys are **never committed to git**.

### Setup

1. Copy the template:

   ```bash
   cp Configs/appsettings.Test.json.example Configs/appsettings.Test.json
   ```

2. Fill in your API keys in `Configs/appsettings.Test.json`.

The file is loaded as a second configuration layer on top of `TelegramBotApp/appsettings.json`, so only the sections you override need to be present.

> **Note**: `Configs/appsettings.Test.json` is listed in `.gitignore` and will never be committed.

### Configuration Loading Order (TestAppFixture)

| Priority | File | Location | Tracked |
|----------|---|----------|---|
| 1 (base) | `appsettings.json` | linked from `TelegramBotApp/` | ✅ yes |
| 2 (override) | `appsettings.Test.json` | `Configs/` | ❌ gitignored |

Tests that have no keys configured will fail with clear `AiProviderException: Unauthorized` errors — not silent passes.

---

## How to Run Tests

Run all tests:

```bash
dotnet test
```

Run a specific project:

```bash
dotnet test tests/ServiceLayer.IntegrationTests
dotnet test tests/ServiceLayer.UnitTests
```

Skip Docker-dependent DB tests (run only AI/service tests):

```bash
dotnet test tests/ServiceLayer.IntegrationTests --filter "FullyQualifiedName!~DatabaseSupport"
```

Run only AI service integration tests:

```bash
dotnet test tests/ServiceLayer.IntegrationTests --filter "Service=OpenAIService|Service=ModelVerification|FullyQualifiedName~ImageGeneration"
```

Run tests by Trait category:

```bash
dotnet test --filter Service=OpenAIService
dotnet test --filter Service=ModelVerification
```

---

## 📚 Related Documentation

- **[Core Files](./core_files.md)** — Detailed explanation of the most important files
- **[Project Descriptions](./projects.md)** — Overview of each project in the solution
- **[Hosting & Deployment](./HOSTING.md)** — All available deployment methods
- **[Docker Deployment](./DOCKER.md)** — Complete Docker Compose configuration guide
- **[Observability](./OBSERVABILITY.md)** — Aspire Dashboard and CloudBeaver monitoring
