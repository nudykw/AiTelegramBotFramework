# 🔄 CI/CD Розгортання через GitHub Actions

🇺🇸 [English](GITHUB-CI-CD.md) | 🇺🇦 [Українська](GITHUB-CI-CD.uk.md)

Цей посібник охоплює **автоматизоване розгортання** GPTChatTelegramBot за допомогою GitHub Actions на VPS.

---

## Огляд

Підрозділ CI/CD працює наступним чином:

```mermaid
graph LR
    A[Push до prod] --> B[GitHub Actions]
    B --> C[Запуск Unit Тестів]
    C --> D[SSH на VPS]
    D --> E[Оновлення .env з Secrets]
    E --> F[git pull origin prod]
    F --> G[docker compose up -d --build]
    G --> H[Бот Працює]
```

---

## Необхідні Secrets

Додайте наступні secrets у ваш GitHub репозиторій (**Settings → Secrets and variables → Actions**):

| Ім'я Secret | Опис | Приклад |
|---|---|---|
| `DEPLOY_HOST` | IP адреса VPS або домен | `192.168.1.100` |
| `DEPLOY_USER` | SSH користувач на VPS | `ubuntu` |
| `DEPLOY_KEY` | SSH приватний ключ для автентифікації | `-----BEGIN OPENSSH PRIVATE KEY-----...` |
| `BOT_PORT` | Порт, на якому слухає бот | `8080` |
| `TELEGRAM_BOT_TOKEN` | Токен Telegram бота | `123456:ABC-DEF1234ghIkl-zyx57W2v1u123ew11` |
| `TELEGRAM_OWNER_ID` | Ваш Telegram User ID | `123456789` |
| `TELEGRAM_BASE_API_URL` | Webhook URL (опціонально, залиште порожнім для Polling) | `https://yourdomain.com` |
| `DB_PROVIDER` | Провайдер бази даних | `PostgreSQL` |
| `DB_CONNECTION_STRING` | Рядок підключення до БД | `Host=postgres;Port=5432;Database=gpt;Username=postgres;Password=pass` |
| `POSTGRES_DB` | Ім'я бази даних PostgreSQL | `gpt_chat_bot` |
| `POSTGRES_USER` | Ім'я користувача PostgreSQL | `postgres` |
| `POSTGRES_PASSWORD` | Пароль PostgreSQL | `ваш_надійний_пароль` |
| `CLOUDBEAVER_PORT` | Порт CloudBeaver | `8978` |
| `CLOUDBEAVER_ADMIN_NAME` | Ім'я адміністратора CloudBeaver | `cbadmin` |
| `CLOUDBEAVER_ADMIN_PASSWORD` | Пароль адміністратора CloudBeaver | `ваш_надійний_пароль` |
| `OPENAI_API_KEY` | OpenAI API ключ (опціонально) | `sk-your_key` |
| `GEMINI_API_KEY` | Gemini API ключ (опціонально) | `ваш_gemini_ключ` |
| `GROK_API_KEY` | Grok API ключ (опціонально) | `ваш_grok_ключ` |
| `DEEPSEEK_API_KEY` | DeepSeek API ключ (опціонально) | `ваш_deepseek_ключ` |

> [!IMPORTANT]
> **Ніколи** не фіксуйте secrets у системі контролю версій. Завжди використовуйте GitHub Secrets для чутливих значень.

---

## Файл Workflow

Створіть файл `.github/workflows/deploy.yml` у вашому репозиторії:

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
            # Перейти в директорію проєкту
            cd /home/nudyk/Sites/GptChatTelegramBot

            # Отримати останні зміни
            git pull origin prod

            # Записати .env файл з secrets
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

            # Зупинити старі контейнери
            docker compose --profile postgres --profile cloudbeaver --profile aspire down

            # Запустити нові контейнери
            docker compose --profile postgres --profile cloudbeaver --profile aspire up -d --build

            # Очистити непотрібні образи
            docker image prune -f
```

---

## Кроки налаштування

### 1. Згенеруйте пару SSH ключів (якщо у вас їх немає)

На вашому локальному комп'ютері:

```bash
ssh-keygen -t ed25519 -C "github-actions-deploy"
```

### 2. Додайте публічний ключ на VPS

```bash
ssh-copy-id -i ~/.ssh/id_ed25519.pub ubuntu@your-vps-ip
```

### 3. Додайте Secrets до GitHub

Перейдіть у **Settings → Secrets and variables → Actions** та додайте всі secrets, перелічені вище.

### 4. Створіть гілку `prod`

```bash
git checkout -b prod
git push origin prod
```

### 5. Запустіть перше розгортання

Зробіть push коміту в гілку `prod`:

```bash
git add .
git commit -m "Початкове налаштування розгортання"
git push origin prod
```

---

## Моніторинг розгортання

### Перевірка GitHub Actions

1. Перейдіть у ваш репозиторій на GitHub
2. Натисніть вкладку **Actions**
3. Натисніть на останній запуск workflow
4. Перевірте логи на наявність помилок

### Перевірка бота на VPS

```bash
# SSH на ваш VPS
ssh ubuntu@your-vps-ip

# Перевірка запущених контейнерів
cd /home/nudyk/Sites/GptChatTelegramBot
docker compose --profile postgres --profile cloudbeaver --profile aspire ps

# Перевірка логів бота
docker compose --profile postgres --profile cloudbeaver --profile aspire logs bot --tail=50

# Перевірка здоров'я
curl http://localhost:8080/health
```

---

## Відкат

Якщо щось пішло не так, ви можете відкатитися до попередньої версії:

```bash
# SSH на ваш VPS
ssh ubuntu@your-vps-ip

# Перейти в директорію проєкту
cd /home/nudyk/Sites/GptChatTelegramBot

# Перевірити git лог
git log --oneline -10

# Перейти на попередній коміт
git checkout <hash-попереднього-коміту>

# Перезапустити контейнери
docker compose --profile postgres --profile cloudbeaver --profile aspire down
docker compose --profile postgres --profile cloudbeaver --profile aspire up -d
```

---

## Практики безпеки

1. **Використовуйте SSH ключі** замість паролів для автентифікації
2. **Регулярно оновлюйте secrets**
3. **Обмежте доступ** до гілки `prod` (використовуйте branch protection rules)
4. **Моніторьте** логи GitHub Actions на підозрілу активність
5. **Використовуйте окремі secrets** для staging та production середовищ

---

## Усунення несправностей

### Розгортання не вдається з помилкою SSH

- Перевірте, що secrets `DEPLOY_HOST`, `DEPLOY_USER` та `DEPLOY_KEY` правильні
- Переконайтеся, що публічний SSH ключ додано до `~/.ssh/authorized_keys` на VPS
- Перевірте, що фаєрвол VPS дозволяє SSH з'єднання (порт 22)

### Бот не запускається після розгортання

- Перевірте логи workflow на наявність помилок
- Переконайтеся, що `.env` файл було правильно записано на VPS
- Перевірте логи контейнерів: `docker compose logs bot`

### Помилка підключення до бази даних

- Перевірте, що secrets `DB_PROVIDER` та `DB_CONNECTION_STRING` правильні
- Переконайтеся, що контейнер БД запущено: `docker compose ps`
- Перевірте облікові дані бази даних у `.env`

---

## 📚 Пов'язана документація

- [Посібник з ручного розгортання](./DEPLOY.md) — Методи ручного розгортання
- [Способи хостингу та запуску](./HOSTING.md) — Усі доступні методи розгортання
- [Посібник з Docker розгортання](./DOCKER.md) — Детальна конфігурація Docker Compose
- [Посібник зі спостережуваності](./OBSERVABILITY.md) — Aspire Dashboard та моніторинг
