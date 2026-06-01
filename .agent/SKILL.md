---
name: Project Analyzer
description: GPTChatTelegramBot repo analysis via docs/
---
# 🛠️ Rules:
1. **Actualize Docs**: Update `docs/` immediately after any service changes.
2. **Tests Mandatory**: Suggest/implement tests for all business logic changes.
3. **Optimized Testing**: Run only relevant tests via `dotnet test --filter Service=Name`.
4. **Lang**: Russian for UI/User, English for code/docs/comments.
5. **Constants**: Extract recurring strings to `ServiceLayer.Constants`.
6. **Enums**: Use `StaticStringEnumBase` or standard `enum` for type safety.
7. **DB Strings**: Always use `[MaxLength(N)]` for DB entities.
8. **Command Sync**: Sync `BotCommands.cs` <-> README <-> Localized resources.
9. **Localization**: Document command descriptions in `HelpText` resource.
10. **Refactor Comments**: Keep all comments in English; translate old non-English ones.
11. **Engineering Paradigms (TOC, SOLID, Occam's Razor)**: Strictly adhere to:
    - **TOC**: Focus optimization exclusively on the actual bottleneck.
    - **SOLID**: Decompose logic into small, modular, single-responsibility functions.
    - **Occam's Razor**: Implement the simplest, most elegant solution. Avoid over-engineering.
12. **Report First, Commit Later**: Before staging, committing, or merging changes in Git, you MUST first output a concise status report of the completed work, present it to the developer, obtain explicit authorization, and only then proceed (never commit first and report afterwards).
13. **Codegraph Fallback**: Use `codegraph context <Sym>` or `query <Q>` directly via shell if MCP is missing.
14. **Git Automation**: Always use `./git-sinc.sh -f <from> -t <to> -m "<message>" [files]` for staging, committing, and merging changes to the target branch. Remember that running this script still requires prior user approval.
