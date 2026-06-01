#!/bin/bash
set -e

# Цвета для вывода сообщений
GREEN='\033[0;32m'
BLUE='\033[0;34m'
NC_RESET='\033[0m'

echo -e "${BLUE}=== Setting up Git hooks for this repository ===${NC_RESET}"

# Сделать хуки исполняемыми
chmod +x .githooks/pre-commit
chmod +x .githooks/pre-push
echo "✓ Made hooks executable."

# Настроить Git для использования папки .githooks для хуков
git config core.hooksPath .githooks
echo "✓ Configured git core.hooksPath to '.githooks'."

echo -e "${GREEN}✓ Done! Git hooks are successfully installed and active!${NC_RESET}"
