# 💡 Інтеграція локального аналізу коду з Codegraph

Цей документ описує високопродуктивну, безконфліктну інтеграцію `codegraph` у проект `GptChatTelegramBot`. Це налаштування дозволяє кільком AI-агентам (наприклад, Zoo Code всередині VS Code та gravity CLI) одночасно звертатися до графа коду без помилок блокування бази даних.

## 📌 Важлива інформація про проект та версію

> [!IMPORTANT]
> Для цієї інтеграції використовується проект **`Cleboost/codegraph-rs`** на базі **SQLite** (файл бази даних `.codegraph/db.sqlite` усередині проекту), а не схожий за назвою проект `Jakedismo/codegraph-rust` (який використовує SurrealDB).

* **Репозиторій джерела:** [Cleboost/codegraph-rs](https://github.com/Cleboost/codegraph-rs)
* **Версія AUR-пакета:** `codegraph-rs-git` (версія `r341.g0fcb09c-1` з фіксом C# та сумісністю з Tree-sitter ABI 14).
* **Сумісність з C#:** Забезпечена за допомогою фіксації `tree-sitter-c-sharp = "=0.23.1"` у `Cargo.toml`.
* **Інтеграція з IDE:** Інтегровано з розширенням **Zoo Code** для VS Code та AI-клієнтом **gravity** за допомогою мультиплексора Stdio-to-TCP.

---

## 🏗️ Архітектура: JSON-RPC Stdio-to-TCP Мультиплексор

Стандартний MCP-сервер `codegraph serve` обмінюється даними лише через **stdio** та ексклюзивно блокує базу даних (використовуючи RocksDB/SQLite). Якщо кілька агентів намагаються запустити `codegraph serve` одночасно, виникають помилки блокування БД.

Для вирішення цієї проблеми ми створили інтелектуальний шлюз на Node.js у `.agent/mcp-gateway.js`:

1. **Детермінований розрахунок TCP-порту:** При запуску шлюз обчислює MD5-хеш шляху до проекту, щоб призначити унікальний TCP-порт у діапазоні `12000–13000` (унікальний для кожного проекту, що виключає системні конфлікти портів).
2. **Єдиний фоновий демон:** Перший агент, що підключається, виявляє, що сервер на розрахованому порту не запущений, створює єдиний фоновий процес демона (`node mcp-gateway.js --daemon`) та підключається до нього.
3. **JSON-RPC Мультиплексування:** Демон запускає рівно один дочірній процес `/usr/bin/codegraph serve` і проксує запити від усіх підключених клієнтів (TCP сокетів), підміняючи та відстежуючи глобальні Request ID.
4. **Нативний моніторинг Linux (`inotifywait`):** Демон використовує `inotifywait` для миттєвого рекурсивного відстеження змін файлів із практично 0% навантаженням на CPU, налаштовуючи debounce-таймер і запускаючи `codegraph sync` у фоновому режимі.
5. **Обхід блокування БД (Lock Bypass):** Під час швидкої синхронізації `codegraph sync` (триває ~100мс) демон тимчасово призупиняє роботу сервера `serve` і буферизує вхідні запити клієнтів у пам'яті, миттєво надсилаючи їх після перезапуску.
6. **Автоматичне вимкнення при простої:** Якщо протягом 15 хвилин немає активних підключень від клієнтів, фоновий демон коректно завершує всі дочірні процеси та вимикається для економії системних ресурсів.

---

## 🛠️ Системні вимоги

Переконайтеся, що у вашій системі встановлені необхідні залежності.

### 1. Codegraph & Спеціальна збірка для підтримки C#
Команда `codegraph` має бути встановлена і доступна за шляхом `/usr/bin/codegraph`.

#### ⚠️ Важливий контекст сумісності (Tree-sitter C# ABI 14):
Стандартний оригінальний репозиторій `codegraph` за замовчуванням не підтримував парсинг C# через конфлікт версій ABI:
* За замовчуванням оригінальний `tree-sitter-c-sharp` версії `0.23.x` використовував **ABI 15**.
* Головне ядро `tree-sitter = "0.24.7"` у коді `codegraph` очікує максимум **ABI 14**. Це викликало помилку розбору `set_language: Incompatible language version 15` та тихий пропуск усіх `.cs` файлів.
* Додатково, на CachyOS (Arch Linux) глобальні опції LTO (`-flto`) в `/etc/makepkg.conf` викликали помилки лінкувальника Rust: `rust-lld: error: undefined symbol: sqlite3_finalize`.

#### 🛠️ Покрокова інструкція повної збірки та встановлення сумісної версії:

1. **Отримання коду:**
   Клонуйте репозиторій та підготуйте AUR-пакет у папці `scratch`:
   ```bash
   mkdir -p scratch && cd scratch
   git clone https://github.com/Cleboost/codegraph-rs
   git clone https://aur.archlinux.org/codegraph-rs-git.git
   ```

2. **Виправлення сумісності C#:**
   * У файлі `scratch/codegraph-rs/Cargo.toml` змініть залежність:
     ```toml
     tree-sitter-c-sharp = "=0.23.1"  # Ця версія генерирує ABI 14 і сумісна з tree-sitter 0.24
     ```
   * У файлі `scratch/codegraph-rs/crates/codegraph-extract/src/languages/csharp.rs` змініть ініціалізацію мови (рядки 6-8):
     ```rust
     fn ts_language() -> tree_sitter::Language {
         tree_sitter_c_sharp::LANGUAGE.into() // Замість .language()
     }
     ```
   * Зробіть комміт змін у локальному клоні `scratch/codegraph-rs`:
     ```bash
     cd scratch/codegraph-rs
     git add -A
     git commit -m "feat: csharp compatibility fix (ABI 14)"
     cd ../..
     ```

3. **Налаштування PKGBUILD:**
   У файлі `scratch/codegraph-rs-git/PKGBUILD` внесіть такі зміни:
   * Вимкніть LTO для запобігання помилкам лінкування:
     ```bash
     options=('!lto')
     ```
   * Замініть джерело (`source`), щоб воно дивилося на ваш локальний клон з фіксом C#:
     ```bash
     source=("$pkgname::git+file:///home/nudyk/Projects/Dotnet/GptChatTelegramBot/scratch/codegraph-rs")
     ```

4. **Складання та встановлення пакета:**
   ```bash
   cd scratch/codegraph-rs-git
   # Видаліть старий кеш клонування (якщо збираєте повторно):
   rm -rf codegraph-rs-git
   # Складіть пакет:
   makepkg -Cfc --nodeps
   # Встановіть згенерований пакет у систему:
   sudo pacman -U codegraph-rs-git-r341.g0fcb09c-1-x86_64.pkg.tar.zst
   ```

5. **Перевірка встановлення:**
   ```bash
   which codegraph
   codegraph --version  # Має повернути 1.0.0
   ```

### 2. Inotify Tools
Для відстеження подій файлової системи в реальному часі потрібен `inotifywait`.
* Встановлення на Linux (Ubuntu/Debian):
  ```bash
  sudo apt-get install inotify-tools
  ```

---

## 📂 Конфігураційні файли

Проект містить готові конфігурації для безшовної інтеграції.

### Налаштування VS Code & Zoo Code
У файлі `.vscode/settings.json` Zoo Code налаштований на використання локального відносного скрипта шлюзу:
```json
"zoo.mcpServers": {
  "codegraph": {
    "command": "node",
    "args": ["${workspaceFolder}/.agent/mcp-gateway.js"]
  }
}
```

### Конфігурація агента Gravity
У файлі `.mcp.json` в корені проекту gravity використовує відносний шлях до шлюзу:
```json
{
  "mcpServers": {
    "codegraph": {
      "command": "node",
      "args": ["./.agent/mcp-gateway.js"]
    }
  }
}
```

---

## 🔄 Автоматичні Git Hooks

Щоб підтримувати індекс у актуальному стані після перемикання гілок або злиття коду (наприклад, під час деплою через `publish2prod.sh` або зміни гілки):
* **`.git/hooks/post-checkout`**
* **`.git/hooks/post-merge`**

Ці хуки налаштовані на автоматичний беззвучний запуск синхронізації `/usr/bin/codegraph sync` у фоновому режимі.

---

## 🔍 Логи та діагностика

Фоновий демон записує всі операційні логи у файл:
* `.agent/daemon.log`

Ви можете відстежувати логи в реальному часі для перевірки індексації БД, подій відстеження файлів та підключення клієнтів:
```bash
tail -f .agent/daemon.log
```

Якщо вам колись знадобиться перезапустити демон вручну, просто закрийте активні сесії агентів в IDE або завершіть процеси node. Демон автоматично запуститься знову при наступному запиті агента.

---

## 🚀 Перенесення в інший проект (наприклад, у Python-проект)

Завдяки переносній архітектурі нашого шлюзу, налаштований `codegraph` можна перенести в будь-який інший проект (включаючи Python-проекти) за кілька простих команд. Глобально встановлений бінарний файл `/usr/bin/codegraph` вже містить підтримку всіх мов.

Виконайте у вашому терміналі (fish/bash) такі команди:

1. **Перейдіть у новий проект:**
   ```bash
   cd /$HOME/Projects/Python/MyProject
   ```

2. **Скопіюйте структуру шлюзу та хуків:**
   ```bash
   # Створення директорії для шлюзу
   mkdir -p .agent docs

   # Копіювання мультиплексора
   cp /$HOME/Projects/Dotnet/GptChatTelegramBot/.agent/mcp-gateway.js .agent/

   # Копіювання документації
   cp /$HOME/Projects/Dotnet/GptChatTelegramBot/docs/codegraph_setup.* docs/

   # Копіювання та надання прав Git-хукам
   cp /$HOME/Projects/Dotnet/GptChatTelegramBot/.git/hooks/post-checkout .git/hooks/
   cp /$HOME/Projects/Dotnet/GptChatTelegramBot/.git/hooks/post-merge .git/hooks/
   chmod +x .git/hooks/post-checkout .git/hooks/post-merge
   ```

3. **Скопіюйте або налаштуйте конфігурації:**
   * **Для gravity:**
     ```bash
     cp /$HOME/Projects/Dotnet/GptChatTelegramBot/.mcp.json .
     ```
   * **Для Zoo Code (VS Code):** Переконайтеся, що у вашому локальному `.vscode/settings.json` додано блок:
     ```json
     "zoo.mcpServers": {
       "codegraph": {
         "command": "node",
         "args": ["${workspaceFolder}/.agent/mcp-gateway.js"]
       }
     }
     ```

4. **Додайте винятки в `.gitignore`:**
   ```gitignore
   # Local AI Agent (codegraph & mcp-gateway)
   .agent/*.log
   .codegraph/
   ```

5. **Ініціалізуйте та проіндексуйте новий проект:**
   ```bash
   codegraph init
   codegraph index
   ```

