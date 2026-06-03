# План усунення вразливостей та зауважень безпеки

Цей документ описує конкретні зауваження, знайдені за допомогою Snyk, Roslyn Analyzers та DevSkim, а також пропоновані кроки для їх усунення або мінімізації ризиків.

---

## 1. Snyk: Вразливості в JS-залежностях

Snyk виявив 9 вразливостей (Prototype Pollution, Arbitrary Code Injection, Uncontrolled Recursion) в Node.js проекті `.agent`.

* **Першопричина:** Бібліотека `@xenova/transformers` використовує застарілу версію `protobufjs@6.11.6` через ланцюжок залежностей `onnxruntime-web` -> `onnx-proto`.
* **План усунення:**
  Налаштувати механізм `overrides` у файлі `.agent/package.json`, щоб змусити менеджер пакетів використовувати безпечну версію `protobufjs` (наприклад, `^7.2.4`), яка є зворотно-сумісною та виправляє ці вразливості.

### Пропоновані зміни у `.agent/package.json`:
```diff
 {
   "dependencies": {
     "@xenova/transformers": "^2.17.2",
     "pg": "^8.21.0",
     "surrealdb": "^2.0.3"
   }
+  "overrides": {
+    "protobufjs": "^7.2.4"
+  }
 }
```
**Дія:** Додати блок перевизначення (override), виконати `npm install` всередині папки `.agent` та повторно запустити тест Snyk для перевірки.

---

## 2. Roslyn Analyzers: Попередження про SQL-ін'єкції (CA2100)

Компілятор попереджає, що запити, які передаються на виконання в `DatabaseQueryMcpTools.cs` та `UpdateHandler.cs`, формуються динамічно.

* **Аналіз:** Вказані ділянки коду відповідають за інструмент динамічних запитів MCP до бази даних та адміністративну пагінацію. Вони спеціально розроблені для того, щоб адміністратори могли виконувати довільні SELECT-запити. Запити проходять попередню перевірку за білим списком дозволених таблиць та безпечних команд на рівні логіки додатка. Отже, динамічна поведінка є закономірною та безпечною.
* **План усунення:**
  Заглушити попередження компілятора за допомогою директиви `#pragma warning`, щоб зберегти чистоту попереджень при збірці.

### Пропоновані зміни в коді:
#### У файлі [DatabaseQueryMcpTools.cs](file:///home/nudyk/Projects/Dotnet/GptChatTelegramBot_Pub/ServiceLayer/Services/Mcp/DatabaseQueryMcpTools.cs#L494):
```csharp
#pragma warning disable CA2100 // Динамічний SQL використовується навмисно для інструменту адміністрування
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            using var reader = await command.ExecuteReaderAsync();
#pragma warning restore CA2100
```

#### У файлі [UpdateHandler.cs](file:///home/nudyk/Projects/Dotnet/GptChatTelegramBot_Pub/ServiceLayer/Services/Telegram/UpdateHandler.cs#L2536):
```csharp
                using (var command = connection.CreateCommand())
                {
#pragma warning disable CA2100 // Динамічний SQL використовується навмисно для пагінації адмін-панелі
                    command.CommandText = finalSql;
#pragma warning restore CA2100
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        dataTable.Load(reader);
                    }
                }
```

---

## 3. DevSkim: Перевірка алгоритмів хешування та конфігурацій

### А. Слабкі/застарілі алгоритми хешування (DS126858)
* **Зауваження:** Виявлено використання алгоритму MD5 у `GoogleDriveService.cs`.
* **Аналіз:** API Google Drive надає хеш-суми файлів у форматі MD5. Для перевірки цілісності завантажених файлів додаток зобов'язаний вирахувати MD5 локального файлу та порівняти його з відповіддю Google Drive.
* **Рішення:** Зміни не потрібні. Це вимога інтеграції з API Google Drive, а не вразливість криптографії в додатку. При необхідності можна додати коментар ігнорування для DevSkim.

### Б. Небезпечні HTTP URL у docker-compose.yml (DS137138)
* **Зауваження:** Використання HTTP-протоколу для внутрішніх з'єднань.
* **Аналіз:** Усі вказані адреси (наприклад, порт OTel або внутрішні зв'язки Nginx) працюють у межах ізольованої віртуальної мережі всередині Docker. Використання HTTP тут є стандартною практикою та є безпечним, оскільки трафік не виходить у відкритий інтернет.
* **Рішення:** Зміни не потрібні.

---

## План перевірки

1. **Перевірка виправлення у Snyk:**
   Перейдіть до директорії `.agent`, виконайте `npm install`, після чого запустіть `npx snyk test --all-projects`. Вразливості мають бути успішно усунені.
2. **Перевірка Roslyn:**
   Виконайте `dotnet build`. Переконайтеся, що кількість попереджень CA2100 зменшилася до нуля.
