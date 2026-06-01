# API and Library Documentation

🇺🇸 English | 🇺🇦 [Українська](api_reference.uk.md)

> [!SEE-ALSO]
> Related documents: [Core Files](core_files.md) | [Projects Overview](projects.md)

This file contains up-to-date links to the documentation for all key libraries used in the project, as well as external Artificial Intelligence APIs. You can use it as a single entry point for finding integration information.

## 📚 Used Libraries (NuGet)

### Core Framework and Architecture

- **.NET 8**: [Official Documentation](https://learn.microsoft.com/en-us/dotnet/core/)
- **Microsoft.Extensions.Hosting / DependencyInjection**: [Hosting Documentation](https://learn.microsoft.com/en-us/dotnet/core/extensions/generic-host)
- **Entity Framework Core (SQLite)**: [EF Core Docs](https://learn.microsoft.com/en-us/ef/core/) | [SQLite Provider](https://learn.microsoft.com/en-us/ef/core/providers/sqlite/)

### Service Integrations

- **Telegram.Bot** (v19.0.0): [GitHub Repository](https://github.com/TelegramBots/Telegram.Bot) | [Telegram Bot API Docs](https://core.telegram.org/bots/api)
- **OpenAI-DotNet** (v8.3.0): [GitHub Repository](https://github.com/RageAgainstThePixel/OpenAI-DotNet)

### Text and HTML Processing

- **HtmlAgilityPack**: [Official Site](https://html-agility-pack.net/)
- **Markdig** (Markdown processor): [GitHub Repository](https://github.com/xoofx/markdig)
- **ReverseMarkdown** (HTML to Markdown): [GitHub Repository](https://github.com/mysticmind/reversemarkdown-net)

### Image Processing

- **SixLabors.ImageSharp**: [Official Documentation](https://docs.sixlabors.com/articles/imagesharp/index.html)

---

## 🤖 External AI APIs (Neural Networks)

Below are links to official developer documentation for current AI platforms. Most of them support OpenAI-compatible request formats.

### 1. Grok (xAI)

- **Official Documentation**: [https://docs.x.ai/](https://docs.x.ai/)
- **Features**: API from xAI (Grok). Fully compatible with the OpenAI SDK specification, operates through its own `Base URL` (e.g., `https://api.x.ai/v1`).

### 2. Google Gemini

- **Official Documentation**: [https://ai.google.dev/api](https://ai.google.dev/api)
- **Google AI Studio**: [https://aistudio.google.com/](https://aistudio.google.com/)
- **Features**: Provides its own REST API endpoints and SDKs, significantly differing from the OpenAI standard. Supports multimodality (text, images, video).

### 3. DeepSeek

- **Official Documentation**: [https://platform.deepseek.com/api-docs](https://platform.deepseek.com/api-docs)
- **Features**: The platform offers DeepSeek-Chat (V3) and DeepSeek-Coder models. The API is a drop-in replacement for the OpenAI API (same request format, just a different `Base URL` — `https://api.deepseek.com` and a different key).

### 4. Z.AI (Zhipu AI / ChatGLM)

- **Official Documentation (Global)**: [https://z.ai](https://z.ai)
- **Official Documentation (China)**: [https://open.bigmodel.cn/](https://open.bigmodel.cn/)
- **Features**: Provides access to the GLM-4 family of models. The platform supports both its own REST APIs and SDKs (Java/Python), as well as full compatibility with the OpenAI SDK if you point requests to their endpoint with your key.
