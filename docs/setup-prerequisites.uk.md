# Налаштування середовища

🇺🇸 [English](setup-prerequisites.md) | 🇺🇦 [Українська](setup-prerequisites.uk.md)

Цей посібник охоплює встановлення **.NET SDK** та **Docker** на всіх основних операційних системах.

---

## 1. Встановлення .NET

Проєкт вимагає **.NET 10 SDK**. Оберіть вашу операційну систему нижче.

### 1.1 Windows

#### Варіант 1: winget (Рекомендовано)

```powershell
winget install Microsoft.DotNet.SDK.10
```

#### Варіант 2: Chocolatey

```powershell
choco install dotnet-core-sdk
```

#### Варіант 3: Scoop

```powershell
scoop install dotnet-core-sdk
```

#### Варіант 4: Офіційний інсталятор (.exe)

1. Перейдіть на [dotnet.microsoft.com/download](https://dotnet.microsoft.com/download)
2. Завантажте інсталятор **.NET 10 SDK** для Windows (`.exe`)
3. Запустіть інсталятор і дотримуйтесь інструкцій майстра

---

### 1.2 macOS

#### Варіант 1: Homebrew (Рекомендовано)

```bash
brew install --cask dotnet
```

#### Варіант 2: Офіційний інсталятор (.pkg)

1. Перейдіть на [dotnet.microsoft.com/download](https://dotnet.microsoft.com/download)
2. Завантажте інсталятор **.NET 10 SDK** для macOS (`.pkg`)
3. Запустіть інсталятор і дотримуйтесь інструкцій майстра

---

### 1.3 Linux — Debian/Ubuntu

#### Варіант 1: Репозиторій Microsoft APT

```bash
# Оновіть списки пакетів
sudo apt-get update
sudo apt-get install -y wget apt-transport-https

# Завантажте ключ підпису пакетів Microsoft
wget https://packages.microsoft.com/config/ubuntu/$(lsb_release -rs)/packages-microsoft-prod.deb -O packages-microsoft-prod.deb

# Встановіть ключ
sudo dpkg -i packages-microsoft-prod.deb

# Видаліть завантажений пакет
rm packages-microsoft-prod.deb

# Встановіть .NET SDK
sudo apt-get update
sudo apt-get install -y dotnet-sdk-10.0
```

> [!NOTE]
> Замініть `$(lsb_release -rs)` на код версії Ubuntu (наприклад, `22.04`, `24.04`).

#### Варіант 2: Офіційний репозиторій Dotnet APT

```bash
# Завантажте офіційний ключ репозиторію Microsoft
wget https://packages.microsoft.com/config/ubuntu/$(lsb_release -rs)/packages-microsoft-prod.deb -O packages-microsoft-prod.deb

# Встановіть ключ
sudo dpkg -i packages-microsoft-prod.deb

# Встановіть .NET SDK
sudo apt-get update
sudo apt-get install -y dotnet-sdk-10.0

# Очистіть
rm packages-microsoft-prod.deb
```

---

### 1.4 Linux — Arch

#### Варіант 1: Офіційні репозиторії (pacman)

```bash
sudo pacman -S dotnet-sdk
```

#### Варіант 2: AUR (yay)

```bash
yay -S dotnet-10-sdk
```

#### Варіант 3: AUR (paru)

```bash
paru -S dotnet-10-sdk
```

---

### 1.5 Linux — Fedora

#### Варіант 1: Офіційний репозиторій Dotnet DNF

```bash
# Завантажте офіційний пакет репозиторію Microsoft
sudo dnf install -y https://packages.microsoft.com/config/fedora/$(rpm -E %fedora)/packages-microsoft-prod.rpm

# Встановіть .NET SDK
sudo dnf install -y dotnet-sdk-10.0
```

> [!NOTE]
> Замініть `$(rpm -E %fedora)` на номер версії Fedora (наприклад, `39`, `40`).

---

### 1.6 Скрипт встановлення Microsoft

#### Linux/macOS

```bash
# Завантажте і запустіть офіційний скрипт встановлення
curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --channel 10.0 --install-dir $HOME/dotnet

# Додайте до PATH (додайте в ~/.bashrc, ~/.zshrc тощо)
export PATH="$HOME/dotnet:$PATH"
```

#### Windows (PowerShell)

```powershell
# Завантажте і запустіть офіційний скрипт встановлення
Invoke-WebRequest -Uri https://dot.net/v1/dotnet-install.ps1 -OutFile dotnet-install.ps1
.\dotnet-install.ps1 -Channel 10.0 -InstallDir $env:USERPROFILE\.dotnet

# Додайте до PATH (додайте в $PROFILE)
$env:PATH += ";$env:USERPROFILE\.dotnet"
```

#### Офіційна документація

Для додаткової інформації див. [документацію скриптів dotnet-install](https://learn.microsoft.com/dotnet/core/tools/dotnet-install-script).

---

### 1.7 Перевірка

Після встановлення переконайтеся, що .NET встановлено правильно:

```bash
dotnet --info
```

Ви повинні побачити вивід подібний до:

```text
.NET SDK:
 Version:           10.0.x
 Commit:            xxxxx
 OS Name:           your-os
 OS Version:        your-version
```

---

## 2. Встановлення Docker та Docker Compose

### 2.1 Windows

#### Docker Desktop (Рекомендовано)

1. Перейдіть на [docker.com/products/docker-desktop](https://www.docker.com/products/docker-desktop/)
2. Завантажте інсталятор **Docker Desktop для Windows**
3. Запустіть інсталятор і дотримуйтесь інструкцій

> [!IMPORTANT]
> Docker Desktop на Windows вимагає:
>
> - Бекенд **WSL 2** увімкнено
> - **Hyper-V** увімкнено (Windows 10 Pro/Enterprise)
> - Віртуалізацію увімкнено в BIOS

#### Увімкнення WSL 2

```powershell
# Увімкніть WSL 2 (потрібні права Адміністратора)
wsl --install

# Перезавантажте комп'ютер
restart-computer
```

Після перезавантаження встановіть Linux-дистрибутив з Microsoft Store (наприклад, Ubuntu).

---

### 2.2 macOS

#### Docker Desktop (Рекомендовано)

1. Перейдіть на [docker.com/products/docker-desktop](https://www.docker.com/products/docker-desktop/)
2. Завантажте інсталятор **Docker Desktop для Mac** (Intel або Apple Silicon)
3. Запустіть інсталятор і перетягніть Docker у Applications

> [!IMPORTANT]
> Docker Desktop для Mac вимагає **macOS 12 Monterey** або новішу.

---

### 2.3 Linux — Debian/Ubuntu

#### Офіційний репозиторій Docker

```bash
# Налаштуйте репозиторій
sudo apt-get update

# Встановіть залежності
sudo apt-get install -y ca-certificates curl

# Створіть директорію для ключів
sudo install -m 0755 -d /etc/apt/keyrings

# Завантажте GPG-ключ Docker
sudo curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc

# Встановіть права для ключа
sudo chmod a+r /etc/apt/keyrings/docker.asc

# Додайте репозиторій Docker
echo \
  "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu \
  $(. /etc/os-release && echo "$VERSION_CODENAME") stable" | \
  sudo tee /etc/apt/sources.list.d/docker.list > /dev/null

# Встановіть Docker Engine
sudo apt-get update
sudo apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
```

#### Додавання користувача до групи docker

Щоб запускати Docker без `sudo`, додайте користувача до групи `docker`:

```bash
sudo usermod -aG docker $USER
```

Після цього оберіть один із способів застосування змін:

**Варіант 1: Вийти і знову увійти (Рекомендовано)**

**Вийдіть** і **знову увійдіть** в систему (або перезавантажте), щоб зміни набули чинності.

**Варіант 2: Використати `newgrp` (Без виходу з системи)**

Щоб застосувати зміни без виходу з системи, запустіть:

```bash
newgrp docker
```

Це запустить нову сесію оболонки з активною групою `docker`. Перевірити можна командою:

```bash
groups
```

> [!WARNING]
> Група `docker` має привілеї root. Див. [документацію з безпеки Docker](https://docs.docker.com/engine/security/#docker-daemon-attack-surface) для деталей.

---

### 2.4 Linux — Arch

#### Офіційні репозиторії

```bash
# Встановіть Docker та плагін Docker Compose
sudo pacman -S docker docker-compose-v2

# Увімкніть і запустіть Docker
sudo systemctl enable --now docker
```

#### Додавання користувача до групи docker

```bash
sudo usermod -aG docker $USER
```

Після цього оберіть один із способів застосування змін:

**Варіант 1: Вийти і знову увійти (Рекомендовано)**

**Вийдіть** і **знову увійдіть** в систему (або перезавантажте), щоб зміни набули чинності.

**Варіант 2: Використати `newgrp` (Без виходу з системи)**

Щоб застосувати зміни без виходу з системи, запустіть:

```bash
newgrp docker
```

Це запустить нову сесію оболонки з активною групою `docker`.

---

### 2.5 Linux — Fedora

#### Офіційний репозиторій Docker

```bash
# Налаштуйте репозиторій
sudo dnf install -y dnf-utils

# Додайте репозиторій Docker
sudo dnf config-manager --add-repo https://download.docker.com/linux/fedora/docker-ce.repo

# Встановіть Docker Engine
sudo dnf install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin

# Увімкніть і запустіть Docker
sudo systemctl enable --now docker
```

#### Додавання користувача до групи docker

```bash
sudo usermod -aG docker $USER
```

Після цього оберіть один із способів застосування змін:

**Варіант 1: Вийти і знову увійти (Рекомендовано)**

**Вийдіть** і **знову увійдіть** в систему (або перезавантажте), щоб зміни набули чинності.

**Варіант 2: Використати `newgrp` (Без виходу з системи)**

Щоб застосувати зміни без виходу з системи, запустіть:

```bash
newgrp docker
```

Це запустить нову сесію оболонки з активною групою `docker`.

---

### 2.6 Перевірка

Після встановлення переконайтеся, що Docker встановлено правильно:

```bash
# Перевірте версію Docker
docker --version

# Перевірте версію Docker Compose
docker compose version

# Запустіть тестовий контейнер
docker run --rm hello-world
```

Ви повинні побачити вивід подібний до:

```text
Docker version 28.x.x, build xxxxxxx
Docker Compose version v2.x.x
Hello from Docker!
```

---

## 3. Швидкий чек-лист

Використовуйте цей чек-лист для перевірки налаштування:

- [ ] .NET SDK встановлено: `dotnet --info` показує версію 10.x
- [ ] Docker встановлено: `docker --version` показує версію 24+
- [ ] Docker Compose встановлено: `docker compose version` показує версію 2.20+
- [ ] Docker запускається без `sudo` (користувача додано до групи `docker` на Linux)
- [ ] WSL 2 увімкнено на Windows (якщо використовується Docker Desktop на Windows)

---

## Пов'язана документація

- [Посібник з хостингу](./HOSTING.uk.md) — Усі способи запуску застосунку
- [Посібник з Docker](./DOCKER.uk.md) — Повний налаштування Docker Compose
- [Офіційна документація .NET](https://learn.microsoft.com/dotnet/core/)
- [Офіційна документація Docker](https://docs.docker.com/)
