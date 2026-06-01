# Документація API та бібліотек

🇺🇸 [English](api_reference.md) | 🇺🇦 [Українська](api_reference.uk.md)

Цей файл містить актуальні посилання на документацію всіх ключових бібліотек, що використовуються у проєкті, а також зовнішніх AI API. Використовуйте його як єдину точку входу для пошуку інформації щодо інтеграцій.

## 📚 Бібліотеки (NuGet)

### Основний фреймворк та архітектура

- **.NET 10**: [Офіційна документація](https://learn.microsoft.com/uk-ua/dotnet/core/)
- **Microsoft.Extensions.Hosting / DependencyInjection**: [Документація Hosting](https://learn.microsoft.com/uk-ua/dotnet/core/extensions/generic-host)
- **Entity Framework Core**: [Документація EF Core](https://learn.microsoft.com/uk-ua/ef/core/) | [Провайдер SQLite](https://learn.microsoft.com/uk-ua/ef/core/providers/sqlite/) | [Провайдер PostgreSQL (Npgsql)](https://www.npgsql.org/efcore/)

### Інтеграції з сервісами

- **Telegram.Bot**: [GitHub](https://github.com/TelegramBots/Telegram.Bot) | [Telegram Bot API](https://core.telegram.org/bots/api)
- **OpenAI-DotNet**: [GitHub](https://github.com/RageAgainstThePixel/OpenAI-DotNet)

### Обробка тексту та HTML

- **HtmlAgilityPack**: [Офіційний сайт](https://html-agility-pack.net/)
- **Markdig** (обробник Markdown): [GitHub](https://github.com/xoofx/markdig)
- **ReverseMarkdown** (HTML → Markdown): [GitHub](https://github.com/mysticmind/reversemarkdown-net)

### Обробка зображень

- **SixLabors.ImageSharp**: [Офіційна документація](https://docs.sixlabors.com/articles/imagesharp/index.html)

---

## 🤖 Зовнішні AI API (нейронні мережі)

Більшість платформ підтримують формат запитів, сумісний з OpenAI.

### 1. Grok (xAI)

- **Офіційна документація**: [https://docs.x.ai/](https://docs.x.ai/)
- **Особливості**: API від xAI (Grok). Повністю сумісний зі специфікацією OpenAI SDK, працює через власний `Base URL` (наприклад, `https://api.x.ai/v1`).

### 2. Google Gemini

- **Офіційна документація**: [https://ai.google.dev/api](https://ai.google.dev/api)
- **Google AI Studio**: [https://aistudio.google.com/](https://aistudio.google.com/)
- **Особливості**: Надає власні REST API-ендпоінти та SDK, що суттєво відрізняються від стандарту OpenAI. Підтримує мультимодальність (текст, зображення, відео).

### 3. DeepSeek

- **Офіційна документація**: [https://platform.deepseek.com/api-docs](https://platform.deepseek.com/api-docs)
- **Особливості**: Пропонує моделі DeepSeek-Chat (V3) та DeepSeek-Coder. API є повною заміною OpenAI API (той самий формат запитів, лише інший `Base URL` — `https://api.deepseek.com` і власний ключ).

### 4. Z.AI (Zhipu AI / ChatGLM)

- **Офіційна документація (Global)**: [https://z.ai](https://z.ai)
- **Офіційна документація (China)**: [https://open.bigmodel.cn/](https://open.bigmodel.cn/)
- **Особливості**: Надає доступ до сімейства моделей GLM-4. Підтримує як власні REST API та SDK (Java/Python), так і повну сумісність з OpenAI SDK при вказанні їх ендпоінту.
