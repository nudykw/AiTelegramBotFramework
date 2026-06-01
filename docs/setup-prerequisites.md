# Setup Prerequisites

🇺🇸 English | 🇺🇦 [Українська](setup-prerequisites.uk.md)

This guide covers installing **.NET SDK** and **Docker** on all major operating systems.

---

## 1. .NET Installation

The project requires **.NET 10 SDK**. Choose your operating system below.

### 1.1 Windows

#### Option 1: winget (Recommended)

```powershell
winget install Microsoft.DotNet.SDK.10
```

#### Option 2: Chocolatey

```powershell
choco install dotnet-core-sdk
```

#### Option 3: Scoop

```powershell
scoop install dotnet-core-sdk
```

#### Option 4: Official Installer (.exe)

1. Go to [dotnet.microsoft.com/download](https://dotnet.microsoft.com/download)
2. Download the **.NET 10 SDK** Windows installer (`.exe`)
3. Run the installer and follow the wizard

---

### 1.2 macOS

#### Option 1: Homebrew (Recommended)

```bash
brew install --cask dotnet
```

#### Option 2: Official Installer (.pkg)

1. Go to [dotnet.microsoft.com/download](https://dotnet.microsoft.com/download)
2. Download the **.NET 10 SDK** macOS installer (`.pkg`)
3. Run the installer and follow the wizard

---

### 1.3 Linux — Debian/Ubuntu

#### Option 1: Microsoft APT Repository

```bash
# Install prerequisites
sudo apt-get update
sudo apt-get install -y wget apt-transport-https

# Download Microsoft package signing key
wget https://packages.microsoft.com/config/ubuntu/$(lsb_release -rs)/packages-microsoft-prod.deb -O packages-microsoft-prod.deb

# Install the key
sudo dpkg -i packages-microsoft-prod.deb

# Remove the downloaded package
rm packages-microsoft-prod.deb

# Install .NET SDK
sudo apt-get update
sudo apt-get install -y dotnet-sdk-10.0
```

> [!NOTE]
> Replace `$(lsb_release -rs)` with your Ubuntu version codename (e.g., `22.04`, `24.04`).

#### Option 2: Dotnet Official APT Repository

```bash
# Download the official Microsoft repository key
wget https://packages.microsoft.com/config/ubuntu/$(lsb_release -rs)/packages-microsoft-prod.deb -O packages-microsoft-prod.deb

# Install the key
sudo dpkg -i packages-microsoft-prod.deb

# Install .NET SDK
sudo apt-get update
sudo apt-get install -y dotnet-sdk-10.0

# Clean up
rm packages-microsoft-prod.deb
```

---

### 1.4 Linux — Arch

#### Option 1: Official Repos (pacman)

```bash
sudo pacman -S dotnet-sdk
```

#### Option 2: AUR (yay)

```bash
yay -S dotnet-10-sdk
```

#### Option 3: AUR (paru)

```bash
paru -S dotnet-10-sdk
```

---

### 1.5 Linux — Fedora

#### Option 1: Dotnet Official DNF Repository

```bash
# Download the official Microsoft repository package
sudo dnf install -y https://packages.microsoft.com/config/fedora/$(rpm -E %fedora)/packages-microsoft-prod.rpm

# Install .NET SDK
sudo dnf install -y dotnet-sdk-10.0
```

> [!NOTE]
> Replace `$(rpm -E %fedora)` with your Fedora version number (e.g., `39`, `40`).

---

### 1.6 Microsoft Install Script

#### Linux/macOS

```bash
# Download and run the official install script
curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --channel 10.0 --install-dir $HOME/dotnet

# Add to PATH (add to ~/.bashrc, ~/.zshrc, etc.)
export PATH="$HOME/dotnet:$PATH"
```

#### Windows (PowerShell)

```powershell
# Download and run the official install script
Invoke-WebRequest -Uri https://dot.net/v1/dotnet-install.ps1 -OutFile dotnet-install.ps1
.\dotnet-install.ps1 -Channel 10.0 -InstallDir $env:USERPROFILE\.dotnet

# Add to PATH (add to $PROFILE)
$env:PATH += ";$env:USERPROFILE\.dotnet"
```

#### Official Documentation

For more details, see the official [dotnet-install scripts documentation](https://learn.microsoft.com/dotnet/core/tools/dotnet-install-script).

---

### 1.7 Verification

After installation, verify that .NET is installed correctly:

```bash
dotnet --info
```

You should see output similar to:

```text
.NET SDK:
 Version:           10.0.x
 Commit:            xxxxx
 OS Name:           your-os
 OS Version:        your-version
```

---

## 2. Docker & Docker Compose Installation

### 2.1 Windows

#### Docker Desktop (Recommended)

1. Go to [docker.com/products/docker-desktop](https://www.docker.com/products/docker-desktop/)
2. Download the **Docker Desktop for Windows** installer
3. Run the installer and follow the wizard

> [!IMPORTANT]
> Docker Desktop on Windows requires:
>
> - **WSL 2** backend enabled
> - **Hyper-V** enabled (Windows 10 Pro/Enterprise)
> - Virtualization enabled in BIOS

#### Enable WSL 2

```powershell
# Enable WSL 2 (requires Administrator privileges)
wsl --install

# Restart your computer
restart-computer
```

After restart, install a Linux distribution from the Microsoft Store (e.g., Ubuntu).

---

### 2.2 macOS

#### Docker Desktop (Recommended)

1. Go to [docker.com/products/docker-desktop](https://www.docker.com/products/docker-desktop/)
2. Download the **Docker Desktop for Mac** installer (Intel or Apple Silicon)
3. Run the installer and drag Docker to Applications

> [!IMPORTANT]
> Docker Desktop for Mac requires **macOS 12 Monterey** or later.

---

### 2.3 Linux — Debian/Ubuntu

#### Docker Official Repository

```bash
# Set up the repository
sudo apt-get update

# Install prerequisites
sudo apt-get install -y ca-certificates curl

# Create the directory for keyrings
sudo install -m 0755 -d /etc/apt/keyrings

# Download the Docker GPG key
sudo curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc

# Set permissions for the key
sudo chmod a+r /etc/apt/keyrings/docker.asc

# Add the Docker repository
echo \
  "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu \
  $(. /etc/os-release && echo "$VERSION_CODENAME") stable" | \
  sudo tee /etc/apt/sources.list.d/docker.list > /dev/null

# Install Docker Engine
sudo apt-get update
sudo apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
```

#### Add User to Docker Group

To run Docker without `sudo`, add your user to the `docker` group:

```bash
sudo usermod -aG docker $USER
```

Then choose one of the following methods to apply the changes:

**Option 1: Log out and log back in (Recommended)**

**Log out** and **log back in** (or reboot) for the changes to take effect.

**Option 2: Use `newgrp` (No logout required)**

To apply the changes without logging out, run:

```bash
newgrp docker
```

This starts a new shell session with the `docker` group active. You can verify with:

```bash
groups
```

> [!WARNING]
> The `docker` group has root-level privileges. See [Docker security documentation](https://docs.docker.com/engine/security/#docker-daemon-attack-surface) for more details.

---

### 2.4 Linux — Arch

#### Official Repos

```bash
# Install Docker and Docker Compose plugin
sudo pacman -S docker docker-compose-v2

# Enable and start Docker
sudo systemctl enable --now docker
```

#### Add User to Docker Group

```bash
sudo usermod -aG docker $USER
```

Then choose one of the following methods to apply the changes:

**Option 1: Log out and log back in (Recommended)**

**Log out** and **log back in** (or reboot) for the changes to take effect.

**Option 2: Use `newgrp` (No logout required)**

To apply the changes without logging out, run:

```bash
newgrp docker
```

This starts a new shell session with the `docker` group active.

---

### 2.5 Linux — Fedora

#### Docker Official Repository

```bash
# Set up the repository
sudo dnf install -y dnf-utils

# Add the Docker repository
sudo dnf config-manager --add-repo https://download.docker.com/linux/fedora/docker-ce.repo

# Install Docker Engine
sudo dnf install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin

# Enable and start Docker
sudo systemctl enable --now docker
```

#### Add User to Docker Group

```bash
sudo usermod -aG docker $USER
```

Then choose one of the following methods to apply the changes:

**Option 1: Log out and log back in (Recommended)**

**Log out** and **log back in** (or reboot) for the changes to take effect.

**Option 2: Use `newgrp` (No logout required)**

To apply the changes without logging out, run:

```bash
newgrp docker
```

This starts a new shell session with the `docker` group active.

---

### 2.6 Verification

After installation, verify that Docker is installed correctly:

```bash
# Check Docker version
docker --version

# Check Docker Compose version
docker compose version

# Run a test container
docker run --rm hello-world
```

You should see output similar to:

```text
Docker version 28.x.x, build xxxxxxx
Docker Compose version v2.x.x
Hello from Docker!
```

---

## 3. Quick Start Checklist

Use this checklist to verify your setup:

- [ ] .NET SDK installed: `dotnet --info` shows version 10.x
- [ ] Docker installed: `docker --version` shows version 24+
- [ ] Docker Compose installed: `docker compose version` shows version 2.20+
- [ ] Docker runs without `sudo` (user added to `docker` group on Linux)
- [ ] WSL 2 enabled on Windows (if using Docker Desktop on Windows)

---

## Related Documentation

- [Hosting & Deployment Guide](./HOSTING.md) — All ways to run the application
- [Docker Deployment Guide](./DOCKER.md) — Full Docker Compose setup
- [Official .NET Documentation](https://learn.microsoft.com/dotnet/core/)
- [Official Docker Documentation](https://docs.docker.com/)
