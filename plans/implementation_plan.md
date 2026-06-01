# Purging Russian Language for License Compliance

This document outlines the detailed plan to fully comply with the **Ethical Peace Protest Clause (Section 3)** of the project's custom license. 

Under the license conditions, the Software, any of its components, derivatives, or modifications **MUST NOT** be translated, localized, or otherwise adapted into the Russian language within any user interface, configuration, resource files, or user-facing documentation. Any public or private deployment must not present a Russian language user interface to its users.

Currently, several files in the repository contain Russian text (documentation, plan files, workflow files, C# comments, resources, and UI strings). This plan details the steps to completely purge all Russian language elements from the repository, replacing them with English or Ukrainian.

## User Review Required

> [!IMPORTANT]
> The changes described in this plan will completely remove Russian language support from the Telegram bot and administrative dashboards, replacing Russian UI elements with English or Ukrainian. All project documentation, workflows, and source code comments written in Russian will be translated to English.

## Proposed Changes

---

### 1. Document Translations (Markdown)

We will translate all Russian-written markdown files in the repository to English.

#### [MODIFY] [implementation_plan.md](file:///home/nudyk/Projects/Dotnet/GptChatTelegramBot_Pub/plans/implementation_plan.md)
* Rewrite the entire implementation plan in English (this file).

#### [MODIFY] [task.md](file:///home/nudyk/Projects/Dotnet/GptChatTelegramBot_Pub/plans/task.md)
* Translate the checklist task list from Russian to English.

#### [MODIFY] [.agent/workflows/mcp_gateway.md](file:///home/nudyk/Projects/Dotnet/GptChatTelegramBot_Pub/.agent/workflows/mcp_gateway.md)
* Translate the entire workflow description and steps from Russian to English.

#### [MODIFY] [.agent/workflows/prod_mcp_gateway.md](file:///home/nudyk/Projects/Dotnet/GptChatTelegramBot_Pub/.agent/workflows/prod_mcp_gateway.md)
* Translate the entire workflow description and steps from Russian to English.

#### [MODIFY] [.agent/workflows/project_memory.md](file:///home/nudyk/Projects/Dotnet/GptChatTelegramBot_Pub/.agent/workflows/project_memory.md)
* Translate the entire workflow description and steps from Russian to English.

---

### 2. Documentation Cleanups (English MDs)

We will fix minor language inconsistencies in the English `.md` files.

#### [MODIFY] [api_reference.md](file:///home/nudyk/Projects/Dotnet/GptChatTelegramBot_Pub/docs/api_reference.md)
* Line 6: Replace Ukrainian `Пов'язані документи:` with English `Related documents:`.

#### [MODIFY] [codegraph_setup.en.md](file:///home/nudyk/Projects/Dotnet/GptChatTelegramBot_Pub/docs/codegraph_setup.en.md)
* Line 26: Replace `рекурсивно` with `recursively`.

---

### 3. C# Codebase Cleanup (UI & Comments)

We will remove Russian localization cases (`"ru" => ...`), translate all UI string literals, and translate source code comments/XML docs to English.

#### [MODIFY] [UpdateHandler.cs](file:///home/nudyk/Projects/Dotnet/GptChatTelegramBot_Pub/ServiceLayer/Services/Telegram/UpdateHandler.cs)
* Remove all `"ru"` switch cases for Telegram messages/menus (e.g. lines 865, 1413, 2272, etc.).
* Fall back to English or map `"ru"` queries to English/Ukrainian as a backup.
* Translate all Russian C# comments to English.

#### [MODIFY] [MessageProcessor.cs](file:///home/nudyk/Projects/Dotnet/GptChatTelegramBot_Pub/ServiceLayer/Services/MessageProcessor/MessageProcessor.cs)
* Remove all `"ru"` cases and translate remaining Russian comments to English.

#### [MODIFY] [DashboardEndpoints.cs](file:///home/nudyk/Projects/Dotnet/GptChatTelegramBot_Pub/TelegramBotWebApp/Endpoints/DashboardEndpoints.cs)
* Remove/translate all Russian UI strings in the Aspire/Web dashboard to English.

#### [MODIFY] [McpEndpoints.cs](file:///home/nudyk/Projects/Dotnet/GptChatTelegramBot_Pub/TelegramBotWebApp/Endpoints/McpEndpoints.cs)
* Translate all Russian labels and guidelines in the MCP endpoints UI to English.

#### [MODIFY] [IStaticStringEnum.cs](file:///home/nudyk/Projects/Dotnet/GptChatTelegramBot_Pub/ServiceLayer/Utils/IStaticStringEnum.cs) & [StaticStringEnumBase.cs](file:///home/nudyk/Projects/Dotnet/GptChatTelegramBot_Pub/ServiceLayer/Utils/StaticStringEnumBase.cs)
* Translate all C# comments and XML documentation from Russian to English.

#### [MODIFY] [ISemanticMemoryService.cs](file:///home/nudyk/Projects/Dotnet/GptChatTelegramBot_Pub/ServiceLayer/Services/Memory/ISemanticMemoryService.cs)
* Translate Russian C# comments to English.

---

### 4. Localization Resources & Tests

#### [MODIFY] [BotMessages.resx](file:///home/nudyk/Projects/Dotnet/GptChatTelegramBot_Pub/ServiceLayer/Resources/BotMessages.resx)
* Remove Russian translation: `Select your language / Виберіть свою мову / Выберите свой язык:` -> `Select your language / Виберіть свою мову:`.

#### [MODIFY] Unit & Integration Tests under [tests/](file:///home/nudyk/Projects/Dotnet/GptChatTelegramBot_Pub/tests)
* Update tests to use English/Ukrainian input instead of Russian where applicable (e.g. `LocalizationCompletenessTests.cs`, `MessageProcessorTests.cs`, `BotReceivesMessageTests.cs`).

---

## Verification Plan

### Automated Tests
1. Verify that the project compiles with 0 errors:
   `dotnet build` in `~/Projects/Dotnet/GptChatTelegramBot_Pub`.
2. Run the integration/unit test suite to ensure all tests pass:
   `dotnet test`.

### Manual Verification
1. Review the git diff to ensure that absolutely no Russian words/phrases remain in any `.md` or source files in the repository.
