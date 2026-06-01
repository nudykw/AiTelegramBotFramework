# Project Documentation Index

🇺🇸 English | 🇺🇦 [Українська](README.uk.md)

Welcome to the **GPTChatTelegramBot** documentation. This folder contains detailed information about the project's structure, core files, and testing strategy.

## Documentation Sections

1. **[Setup Prerequisites](./setup-prerequisites.md)** ← *new*
   - .NET SDK installation on Windows, macOS, Linux (Debian, Arch, Fedora)
   - Docker & Docker Compose installation on all platforms
   - Adding user to Docker group on Linux
   - Quick start checklist

2. **[Project Descriptions](./projects.md)**
   - Detailed overview of each project in the solution and their responsibilities.
   - Includes **TelegramBotApp**, **ServiceLayer**, **DataBaseLayer**, and **GeminiChat.DotNet**.

2. **[Core Files](./core_files.md)**
   - Explanation of the most important files across the repository, such as `Program.cs`, main services, and database context.

3. **[Testing Strategy](./tests.md)**
   - Information about where to find unit and integration tests.
   - Instructions on how to run tests.

4. **[Database Migrations](./MIGRATIONS.md)**
   - Step-by-step guide: first migration, subsequent migrations, troubleshooting.

5. **[Docker Deployment](./DOCKER.md)**
   - Complete deployment guide with all database providers.

6. **[Hosting & Run Methods](./HOSTING.md)** ← *all ways to run the app*
   - Local runs (console and web), Docker Compose, Docker console, systemd service on Linux.
   - Comparison table by complexity and feature set.

7. **[Observability — Aspire Dashboard & CloudBeaver](./OBSERVABILITY.md)**
   - Real-time metrics, traces, and structured logs via Aspire Dashboard.
   - CloudBeaver: first login, database connection, SQL queries.
   - Animated demos included.

8. **[MCP Tools — Native Model Context Protocol](./MCP.md)**
   - Dynamic tool loading for AI.
   - Managing tools via Database.
   - Command `/tools` to see active integrations.

9. **[API & Libraries Reference](./api_reference.md)**
   - Links to documentation for all libraries used and external AI APIs.

---

## Getting Started

If you are new to this codebase, we recommend starting with the **[Project Descriptions](./projects.md)** to get a high-level understanding of how the different components interact.
