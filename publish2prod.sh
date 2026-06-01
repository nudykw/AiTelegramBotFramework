#!/bin/bash
set -e

# Гарантируем возврат в main при любом завершении скрипта (успех или ошибка)
trap 'git checkout main' EXIT

# ── Stop docker compose if running (failure is non-fatal) ──────────────────────
echo "→ Stopping docker compose (if running)..."
docker compose down 2>/dev/null || true

# ── Push current main branch ───────────────────────────────────────────────────
echo "→ Pushing main..."
git push origin main

# ── Merge main → prod and push ─────────────────────────────────────────────────
echo "→ Switching to prod..."
git checkout prod

echo "→ Merging main into prod..."
git merge main --no-edit

echo "→ Pushing prod..."
git push origin prod

# ── Return to main and merge prod back ───────────────────────────────────────
echo "→ Switching back to main..."
git checkout main

echo "→ Merging prod back into main (if needed)..."
git merge prod --no-edit

echo ""
echo "✓ Done! prod is up to date with main."
