# 🔄 CI/CD Deployment via GitHub Actions

🇺🇸 English | 🇺🇦 [Українська](GITHUB-CI-CD.uk.md)

This guide covers **automated deployment** of GPTChatTelegramBot using GitHub Actions to a VPS.

---

## Overview

The deployment pipeline works as follows:

```mermaid
graph LR
    A[Push to prod] --> B[GitHub Actions]
    B --> C[Run Unit Tests]
    C --> D[SSH to VPS]
    D --> E[Update .env from Secrets]
    E --> F[git pull origin prod]
    F --> G[docker compose up -d --build]
    G --> H[Bot Running]
```

---

## Required Secrets

Add the following secrets to your GitHub repository (**Settings → Secrets and variables → Actions**):

| Secret Name | Description | Example |
|---|---|---|
| `DEPLOY_HOST` | VPS IP address or domain | `192.168.1.100` |
| `DEPLOY_USER` | SSH user on VPS | `ubuntu` |
| `DEPLOY_KEY` | SSH private key for authentication | `-----BEGIN OPENSSH PRIVATE KEY-----...` |
| `BOT_PORT` | Port the bot listens on | `8080` |
| `TELEGRAM_BOT_TOKEN` | Telegram Bot Token | `123456:ABC-DEF1234ghIkl-zyx57W2v1u123ew11` |
| `TELEGRAM_OWNER_ID` | Your Telegram User ID | `123456789` |
| `TELEGRAM_BASE_API_URL` | Webhook URL (optional, leave empty for Polling) | `https://yourdomain.com` |
| `DB_PROVIDER` | Database provider | `PostgreSQL` |
| `DB_CONNECTION_STRING` | Database connection string | `Host=postgres;Port=5432;Database=gpt;Username=postgres;Password=pass` |
| `POSTGRES_DB` | PostgreSQL database name | `gpt_chat_bot` |
| `POSTGRES_USER` | PostgreSQL username | `postgres` |
| `POSTGRES_PASSWORD` | PostgreSQL password | `your_strong_password` |
| `CLOUDBEAVER_PORT` | CloudBeaver port | `8978` |
| `CLOUDBEAVER_ADMIN_NAME` | CloudBeaver admin username | `cbadmin` |
| `CLOUDBEAVER_ADMIN_PASSWORD` | CloudBeaver admin password | `your_strong_password` |
| `OPENAI_API_KEY` | OpenAI API key (optional) | `sk-your_key` |
| `GEMINI_API_KEY` | Gemini API key (optional) | `your_gemini_key` |
| `GROK_API_KEY` | Grok API key (optional) | `your_grok_key` |
| `DEEPSEEK_API_KEY` | DeepSeek API key (optional) | `your_deepseek_key` |

> [!IMPORTANT]
> **Never** commit secrets to version control. Always use GitHub Secrets for sensitive values.

---

## Workflow File

Create the file `.github/workflows/deploy.yml` in your repository:

```yaml
name: Deploy via GitHub Actions

on:
  push:
    branches: [prod]

env:
  REGISTRY: ghcr.io
  IMAGE_NAME: ${{ github.repository }}

jobs:
  deploy:
    runs-on: ubuntu-latest

    steps:
      - name: Checkout code
        uses: actions/checkout@v4

      - name: Run Unit Tests
        run: dotnet test

      - name: Deploy to VPS
        uses: appleboy/ssh-action@v1
        with:
          host: ${{ secrets.DEPLOY_HOST }}
          username: ${{ secrets.DEPLOY_USER }}
          key: ${{ secrets.DEPLOY_KEY }}
          script: |
            # Navigate to project directory
            cd /home/nudyk/Sites/GptChatTelegramBot

            # Pull latest changes
            git pull origin prod

            # Write .env file from secrets
            cat > .env <<EOF
            TELEGRAM_BOT_TOKEN=${{ secrets.TELEGRAM_BOT_TOKEN }}
            TELEGRAM_OWNER_ID=${{ secrets.TELEGRAM_OWNER_ID }}
            TELEGRAM_BASE_API_URL=${{ secrets.TELEGRAM_BASE_API_URL }}
            DB_PROVIDER=${{ secrets.DB_PROVIDER }}
            DB_CONNECTION_STRING=${{ secrets.DB_CONNECTION_STRING }}
            POSTGRES_DB=${{ secrets.POSTGRES_DB }}
            POSTGRES_USER=${{ secrets.POSTGRES_USER }}
            POSTGRES_PASSWORD=${{ secrets.POSTGRES_PASSWORD }}
            CLOUDBEAVER_PORT=${{ secrets.CLOUDBEAVER_PORT }}
            CLOUDBEAVER_ADMIN_NAME=${{ secrets.CLOUDBEAVER_ADMIN_NAME }}
            CLOUDBEAVER_ADMIN_PASSWORD=${{ secrets.CLOUDBEAVER_ADMIN_PASSWORD }}
            OPENAI_API_KEY=${{ secrets.OPENAI_API_KEY }}
            GEMINI_API_KEY=${{ secrets.GEMINI_API_KEY }}
            GROK_API_KEY=${{ secrets.GROK_API_KEY }}
            DEEPSEEK_API_KEY=${{ secrets.DEEPSEEK_API_KEY }}
            EOF

            # Stop old containers
            docker compose --profile postgres --profile cloudbeaver --profile aspire down

            # Start new containers
            docker compose --profile postgres --profile cloudbeaver --profile aspire up -d --build

            # Clean up unused images
            docker image prune -f
```

---

## Setup Steps

### 1. Generate SSH Key Pair (if you don't have one)

On your local machine:

```bash
ssh-keygen -t ed25519 -C "github-actions-deploy"
```

### 2. Add Public Key to VPS

```bash
ssh-copy-id -i ~/.ssh/id_ed25519.pub ubuntu@your-vps-ip
```

### 3. Add Secrets to GitHub

Go to **Settings → Secrets and variables → Actions** and add all secrets listed above.

### 4. Create the `prod` Branch

```bash
git checkout -b prod
git push origin prod
```

### 5. Trigger the First Deployment

Push a commit to the `prod` branch:

```bash
git add .
git commit -m "Initial deployment setup"
git push origin prod
```

---

## Monitoring Deployment

### Check GitHub Actions

1. Go to your repository on GitHub
2. Click **Actions** tab
3. Click the latest workflow run
4. Check the logs for any errors

### Check Bot on VPS

```bash
# SSH to your VPS
ssh ubuntu@your-vps-ip

# Check running containers
cd /home/nudyk/Sites/GptChatTelegramBot
docker compose --profile postgres --profile cloudbeaver --profile aspire ps

# Check bot logs
docker compose --profile postgres --profile cloudbeaver --profile aspire logs bot --tail=50

# Health check
curl http://localhost:8080/health
```

---

## Rollback

If something goes wrong, you can rollback to the previous version:

```bash
# SSH to your VPS
ssh ubuntu@your-vps-ip

# Navigate to project directory
cd /home/nudyk/Sites/GptChatTelegramBot

# Check git log
git log --oneline -10

# Checkout previous commit
git checkout <previous-commit-hash>

# Restart containers
docker compose --profile postgres --profile cloudbeaver --profile aspire down
docker compose --profile postgres --profile cloudbeaver --profile aspire up -d
```

---

## Security Best Practices

1. **Use SSH keys** instead of passwords for authentication
2. **Rotate secrets** regularly
3. **Limit access** to the `prod` branch (use branch protection rules)
4. **Monitor** GitHub Actions logs for suspicious activity
5. **Use separate secrets** for staging and production environments

---

## Troubleshooting

### Deployment fails with SSH error

- Verify `DEPLOY_HOST`, `DEPLOY_USER`, and `DEPLOY_KEY` secrets are correct
- Ensure the public SSH key is added to `~/.ssh/authorized_keys` on the VPS
- Check that the VPS firewall allows SSH connections (port 22)

### Bot doesn't start after deployment

- Check the workflow logs for errors
- Verify `.env` file was written correctly on the VPS
- Check container logs: `docker compose logs bot`

### Database connection fails

- Verify `DB_PROVIDER` and `DB_CONNECTION_STRING` secrets are correct
- Ensure the database container is running: `docker compose ps`
- Check database credentials in `.env`

---

## 📚 Related Documentation

- [Manual Deployment Guide](./DEPLOY.md) — Manual deployment methods
- [Hosting & Run Methods](./HOSTING.md) — All available deployment methods
- [Docker Deployment Guide](./DOCKER.md) — Detailed Docker Compose configuration
- [Observability Guide](./OBSERVABILITY.md) — Aspire Dashboard and monitoring
