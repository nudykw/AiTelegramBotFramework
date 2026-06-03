# Покроковий план впровадження сканування безпеки (Локально та CI/CD)

Цей план описує поетапний підхід до впровадження статичного аналізу безпеки коду (SAST) та аналізу залежностей (SCA) для проекту, починаючи з локальних перевірок та закінчуючи інтеграцією з GitHub.

---

## Етап 1: Локальне сканування безпеки (Попередня перевірка)

Перед відправкою (push) змін до віддаленого репозиторію переконайтеся, що всі залежності та вихідний код перевірені локально.

### Крок 1.1: Вбудоване сканування залежностей NuGet
.NET SDK надає вбудовану функцію аудиту вразливостей для підключених пакетів.

1. Запустіть наступну команду в директорії рішення:
   ```bash
   dotnet list package --vulnerable --include-transitive
   ```
2. Якщо виявлено вразливі пакети, оновіть їх до безпечних версій у відповідних файлах `.csproj`.

### Крок 1.2: Roslyn Security Analyzers
Roslyn Analyzers аналізують C# код під час компіляції. Ми налаштуємо проект для забезпечення стандартів безпеки.

1. Переконайтеся, що аналізатори SDK активовані, додавши або перевіривши такі властивості у файлах `.csproj`:
   ```xml
   <PropertyGroup>
     <EnableNETAnalyzers>true</EnableNETAnalyzers>
     <AnalysisLevel>latest</AnalysisLevel>
     <AnalysisMode>AllEnabledByDefault</AnalysisMode>
   </PropertyGroup>
   ```
2. Налаштуйте правила аналізу через файл `.editorconfig` в корені проекту, щоб трактувати проблеми безпеки як попередження або помилки:
   ```ini
   # Увімкнути всі правила безпеки як попередження або помилки
   dotnet_analyzer_diagnostic.category-Security.severity = warning
   ```

### Крок 1.3: Налаштування Microsoft DevSkim
DevSkim забезпечує аналіз безпеки безпосередньо у середовищі розробки та через інтерфейс командного рядка (CLI).

1. Встановіть інструмент командного рядка DevSkim глобально:
   ```bash
   dotnet tool install --global Microsoft.DevSkim.CLI
   ```
2. Запустіть сканування кодової бази для пошуку вразливостей шифрування, захардкодних токенів та небезпечних викликів API:
   ```bash
   devskim analyze -s .
   ```
3. (Опціонально) Ознайомтеся з результатами та налаштуйте правила виключення для тестових проектів, якщо це необхідно.

### Крок 1.4: Локальне сканування через Snyk CLI
Snyk CLI дозволяє перевірити пакети NuGet та статичну логіку коду C#.

1. Встановіть Snyk CLI через npm (необхідно мати встановлений Node.js):
   ```bash
   npm install -g snyk
   ```
2. Авторизуйте CLI за допомогою вашого облікового запису Snyk:
   ```bash
   snyk auth
   ```
3. Перевірте пакети NuGet на наявність уразливостей:
   ```bash
   snyk test
   ```
4. Перевірте логіку вихідного коду (SAST):
   ```bash
   snyk code test
   ```

---

## Етап 2: Інтеграція безпеки з GitHub та бейджі статусів

Після успішного запуску локальних перевірок автоматизуйте їх за допомогою інструментів GitHub.

### Крок 2.1: Аналіз GitHub CodeQL
Вбудований рушій стаTickого аналізу GitHub (SAST) скануватиме код під час кожного push або pull request.

1. Створіть файл процесу GitHub Actions: `.github/workflows/codeql.yml`
2. Заповніть файл стандартною конфігурацією CodeQL для C#:
   ```yaml
   name: "CodeQL"

   on:
     push:
       branches: [ "main", "master" ]
     pull_request:
       branches: [ "main", "master" ]
     schedule:
       - cron: '0 0 * * 1' # Запуск щопонеділка

   jobs:
     analyze:
       name: Analyze C#
       runs-on: ubuntu-latest
       permissions:
         actions: read
         contents: read
         security-events: write

       steps:
       - name: Checkout repository
         uses: actions/checkout@v4

       - name: Initialize CodeQL
         uses: github/codeql-action/init@v3
         with:
           languages: 'csharp'

       - name: Autobuild
         uses: github/codeql-action/autobuild@v3

       - name: Perform CodeQL Analysis
         uses: github/codeql-action/analyze@v3
   ```
3. Додайте бейдж статусу збірки GitHub Actions до `README.md`:
   ```markdown
   [![CodeQL Status](https://github.com/<OWNER>/<REPO>/actions/workflows/codeql.yml/badge.svg)](https://github.com/<OWNER>/<REPO>/actions/workflows/codeql.yml)
   ```

### Крок 2.2: Інтеграція додатка Snyk GitHub
Для отримання динамічного бейджа вразливостей від Snyk підключіть додаток Snyk GitHub.

1. Увійдіть у свій профіль на [snyk.io](https://snyk.io/) та перейдіть у розділ **Integrations -> GitHub**.
2. Надайте доступ до цільового репозиторію.
3. Імпортуйте репозиторій у панель управління Snyk. Сервіс автоматично скануватиме репозиторій та відстежуватиме файли `.csproj` при кожному коміті.
4. Отримайте markdown-код бейджа в налаштуваннях проекту на Snyk (**Settings -> Badges**) та додайте його до `README.md`:
   ```markdown
   [![Known Vulnerabilities](https://snyk.io/test/github/<OWNER>/<REPO>/badge.svg)](https://snyk.io/test/github/<OWNER>/<REPO>)
   ```

---

## План перевірки

### Локальна перевірка
- [ ] Виконання команди `dotnet list package --vulnerable --include-transitive` повертає 0 уразливостей або показує пакети, що потребують оновлення.
- [ ] Команда `devskim analyze -s .` завершується успішно без критичних зауважень.
- [ ] Команди `snyk test` та `snyk code test` проходять без критичних повідомлень про безпеку.

### Перевірка в CI/CD
- [ ] Відправте тестову гілку в репозиторій для тригеру дії CodeQL. Переконайтеся, що завдання завершилося успішно.
- [ ] Перевірте наявність результатів аналізу у вкладці репозиторію **Security -> Code scanning**.
- [ ] Переконайтеся, що панель Snyk успішно обробляє коміти та коректно оновлює статус бейджа у README.
