---
description: Steps to follow when modifying application services
---

When any service (especially in `ServiceLayer`) is created or modified, you MUST follow these steps:

1.  **Analyze Impact**: Identify which documentation files in `docs/` need to be updated.
2.  **Update Documentation**:
    - Update `docs/core_files.md` if new key classes are added.
    - Update `docs/api_reference.md` if API interfaces or logic changes.
    - Update `docs/projects.md` if the service architecture changes.
3.  **Propose/Write Tests**:
    - Check if the service has existing tests in `tests/ServiceLayer.UnitTests/`.
    - Propose new unit tests for the changes.
    - If the user agrees, implement the tests.
4.  **Verify**: Ensure documentation is consistent with the code and tests pass.
5.  **NO STRING DUPLICATION**: Any string constant (model names, provider names, test prompts, etc.) must be defined only once. Use classes inheriting from `StaticStringEnumBase` (e.g., `AiModel`, `AiProvider`) or local constants for tests.

Remember: This is a MANDATORY rule for this project.
