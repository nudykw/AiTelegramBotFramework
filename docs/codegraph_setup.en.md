# 💡 Local Code Intelligence Integration with Codegraph

This document describes the high-performance, conflict-free integration of `codegraph` into the `GptChatTelegramBot` project. This setup allows multiple AI agents (e.g., Zoo Code inside VS Code and gravity CLI) to query the local code graph concurrently without database locking issues.

## 📌 Important Project & Version Context

> [!IMPORTANT]
> This integration exclusively utilizes the **`Cleboost/codegraph-rs`** project which relies on **SQLite** (storing the graph in `.codegraph/db.sqlite` in your workspace), and should **not** be confused with the similarly named `Jakedismo/codegraph-rust` project (which is built on SurrealDB).

* **Upstream Source Repository:** [Cleboost/codegraph-rs](https://github.com/Cleboost/codegraph-rs)
* **AUR Package Version:** `codegraph-rs-git` (version `r341.g0fcb09c-1`, compiled with our custom C# parsing and Tree-sitter ABI 14 compatibility patches).
* **C# Grammar Compatibility:** Achieved by pinning `tree-sitter-c-sharp = "=0.23.1"` inside `Cargo.toml`.
* **IDE & Agent Integration:** Fully integrated with the **Zoo Code** VS Code extension and the **gravity** AI client through our custom Stdio-to-TCP multiplexer gateway.

---

## 🏗️ Architecture: JSON-RPC Stdio-to-TCP Multiplexer

The standard `codegraph serve` MCP server communicates only over **stdio** and locks the database exclusively (using RocksDB/SQLite). If multiple agents try to spawn `codegraph serve` simultaneously, it causes database locking errors.

To solve this, we implemented a smart, zero-dependency Node.js gateway in `.agent/mcp-gateway.js`:

1. **Deterministic Port Calculation:** When an agent invokes the gateway, it hashes the project path (using MD5) to assign a deterministic TCP port in the `12000–13000` range (unique per project, avoiding system-wide collisions).
2. **Unified Background Daemon:** The first connecting agent detects that no server is running on the calculated port, spawns a single background daemon process (`node mcp-gateway.js --daemon`), and connects to it.
3. **JSON-RPC Multiplexing:** The daemon launches exactly one child process of `/usr/bin/codegraph serve` and proxies requests from all concurrent clients (TCP sockets) by tracking and rewriting JSON-RPC Request IDs.
4. **Real-Time Linux-Native Watching (`inotifywait`):** The daemon spawns `inotifywait` to monitor file changes recursively with virtually 0% CPU overhead, debouncing and triggering `codegraph sync` in the background.
5. **Database Lock Bypass:** During the quick `codegraph sync` (takes ~100ms), the daemon briefly suspends the `serve` process and buffers incoming client requests in memory, resuming and flushing them immediately afterwards with zero impact to the IDEs.
6. **Idle Auto-Shutdown:** If no clients are connected for 15 minutes, the background daemon automatically kills all child processes and shuts down to save system resources.

---

## 🛠️ Prerequisites

Ensure you have the required system dependencies installed.

### 1. Codegraph & Custom Build for C# Support
The `codegraph` binary must be installed and executable at `/usr/bin/codegraph`.

#### ⚠️ Key Compatibility Context (Tree-sitter C# ABI 14):
The upstream `codegraph-rs` repository did not parse C# out-of-the-box due to an ABI mismatch:
* By default, upstream used `tree-sitter-c-sharp` version `0.23.x`, which targets **ABI 15**.
* The core `tree-sitter = "0.24.7"` library compiled inside `codegraph` only supports up to **ABI 14**. This runtime discrepancy caused a silent parsing rejection: `Parse("set_language: Incompatible language version 15")` on all `.cs` files.
* Additionally, on CachyOS (Arch Linux), global Link-Time Optimization (`-flto`) flags in `/etc/makepkg.conf` caused Rust link errors during the build: `rust-lld: error: undefined symbol: sqlite3_finalize`.

#### 🛠️ Step-by-Step Custom Compilation and Installation Guide:

1. **Clone the Repositories:**
   Create a `scratch/` directory and grab the upstream source code alongside the AUR package configuration:
   ```bash
   mkdir -p scratch && cd scratch
   git clone https://github.com/Cleboost/codegraph-rs
   git clone https://aur.archlinux.org/codegraph-rs-git.git
   ```

2. **Fix C# Parser Compatibility:**
   * In `scratch/codegraph-rs/Cargo.toml`, pin the C# grammar to a version using ABI 14:
     ```toml
     tree-sitter-c-sharp = "=0.23.1"  # ABI 14 grammar compatible with tree-sitter 0.24
     ```
   * In `scratch/codegraph-rs/crates/codegraph-extract/src/languages/csharp.rs`, modify language initialization (lines 6-8):
     ```rust
     fn ts_language() -> tree_sitter::Language {
         tree_sitter_c_sharp::LANGUAGE.into() // Instead of .language()
     }
     ```
   * Commit these compatibility fixes in your local git clone:
     ```bash
     cd scratch/codegraph-rs
     git add -A
     git commit -m "feat: csharp compatibility fix (ABI 14)"
     cd ../..
     ```

3. **Configure the AUR PKGBUILD:**
   In `scratch/codegraph-rs-git/PKGBUILD`, make the following modifications:
   * Disable compiler LTO to prevent linking conflicts:
     ```bash
     options=('!lto')
     ```
   * Point the `source` variable to your patched local repository clone:
     ```bash
     source=("$pkgname::git+file:///home/nudyk/Projects/Dotnet/GptChatTelegramBot/scratch/codegraph-rs")
     ```

4. **Build and Install the Package:**
   ```bash
   cd scratch/codegraph-rs-git
   # Remove stale cloning caches (if rebuilding):
   rm -rf codegraph-rs-git
   # Compile and package:
   makepkg -Cfc --nodeps
   # Install the generated Arch package:
   sudo pacman -U codegraph-rs-git-r341.g0fcb09c-1-x86_64.pkg.tar.zst
   ```

5. **Verify the Installation:**
   ```bash
   which codegraph
   codegraph --version  # Should output 1.0.0
   ```

### 2. Inotify Tools
`inotifywait` is required for real-time filesystem events.
* Install on Linux (Ubuntu/Debian):
  ```bash
  sudo apt-get install inotify-tools
  ```

---

## 📂 Configuration Files

The project includes pre-configured settings to make integration seamless.

### VS Code & Zoo Code Settings
Located at `.vscode/settings.json`, Zoo Code is configured to use the local relative gateway script:
```json
"zoo.mcpServers": {
  "codegraph": {
    "command": "node",
    "args": ["${workspaceFolder}/.agent/mcp-gateway.js"]
  }
}
```

### Gravity Agent Configuration
Located at `.mcp.json` in the root of the project, gravity utilizes the relative gateway script:
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

## 🔄 Automated Git Hooks

To keep the index fresh after checking out branches or merging code (e.g., during deployments or branch switches):
* **`.git/hooks/post-checkout`**
* **`.git/hooks/post-merge`**

These hooks are configured to trigger a silent background `/usr/bin/codegraph sync` automatically.

---

## 🔍 Troubleshooting & Logs

The background daemon writes all operational logs to:
* `.agent/daemon.log`

You can tail the logs to verify database indexing, watcher events, and client connections:
```bash
tail -f .agent/daemon.log
```

If you ever need to manually restart the daemon, simply close the active IDE extensions or kill the node processes. The daemon will automatically spin up on the next agent request.

---

## 🚀 Porting to Another Project (e.g., a Python Project)

Thanks to the portable and self-contained architecture of our gateway multiplexer, you can port `codegraph` to any other project (including Python projects) using a few simple commands. The globally installed `/usr/bin/codegraph` binary already supports all programming languages.

Execute the following commands in your terminal (fish/bash):

1. **Navigate to your new project:**
   ```bash
   cd /$HOME/Projects/Python/MyProject
   ```

2. **Copy the gateway and Git hooks structure:**
   ```bash
   # Create the agent directory
   mkdir -p .agent docs

   # Copy the TCP multiplexer gateway
   cp /$HOME/Projects/Dotnet/GptChatTelegramBot/.agent/mcp-gateway.js .agent/

   # Copy the setup documentation files
   cp /$HOME/Projects/Dotnet/GptChatTelegramBot/docs/codegraph_setup.* docs/

   # Copy and set executable permissions for Git hooks
   cp /$HOME/Projects/Dotnet/GptChatTelegramBot/.git/hooks/post-checkout .git/hooks/
   cp /$HOME/Projects/Dotnet/GptChatTelegramBot/.git/hooks/post-merge .git/hooks/
   chmod +x .git/hooks/post-checkout .git/hooks/post-merge
   ```

3. **Copy or configure the client settings:**
   * **For gravity:**
     ```bash
     cp /$HOME/Projects/Dotnet/GptChatTelegramBot/.mcp.json .
     ```
   * **For Zoo Code (VS Code):** Ensure that your local `.vscode/settings.json` contains this server definition block:
     ```json
     "zoo.mcpServers": {
       "codegraph": {
         "command": "node",
         "args": ["${workspaceFolder}/.agent/mcp-gateway.js"]
       }
     }
     ```

4. **Add directories to your `.gitignore`:**
   ```gitignore
   # Local AI Agent (codegraph & mcp-gateway)
   .agent/*.log
   .codegraph/
   ```

5. **Initialize and perform the first index pass:**
   ```bash
   codegraph init
   codegraph index
   ```

