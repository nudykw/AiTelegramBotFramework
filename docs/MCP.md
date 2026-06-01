# 🛠️ MCP Tools (Model Context Protocol)

The bot supports the **Model Context Protocol (MCP)**, allowing it to dynamically use external tools (like web search or Puppeteer) to answer questions.

## 🚀 How it works

1.  **Tool Hub**: The bot acts as an MCP Client using `McpServerManager`. It can connect to multiple MCP Servers.
2.  **Isolated Execution**: All MCP servers run inside a specialized Docker container (`gpt_mcp_runtime`) to ensure security and dependency isolation (e.g., Chromium for Puppeteer).
3.  **Automatic Seeding**: At startup, the bot reads the `McpSettings:DefaultTools` list from `appsettings.json` and automatically registers/updates these tools in the database.
4.  **Health Checks**: During initialization, the bot performs a connectivity check for every tool and logs the result:
    - ✅ `[HEALTH CHECK PASSED]` — Tool is ready for use.
    - ❌ `[HEALTH CHECK FAILED]` — Tool is unreachable or failed to start.
5.  **Dynamic Discovery**: The bot fetches available tools from active MCP servers.
6.  **Model Integration**: Tools are passed to the AI model (OpenAI, Gemini, etc.) as "functions".
7.  **Tool Execution**: When the AI decides to use a tool, the bot executes it via `docker exec` in the runtime container and sends the result back to the AI.

---

## 🧩 Native MCP Tools (In-Process)

In addition to external servers spawned in Docker, the bot supports **Native MCP Tools** implemented directly in C# within the `ServiceLayer` project. These tools execute in-process within the bot container, allowing highly performant, secure access to dependencies and internal contexts (like current User ID).

### 📁 Google Drive Sandbox Integration

We provide a secure, isolated virtual file system inside Google Drive using the native Google Drive API (v3). It restricts LLM agents to a strict sandbox and requires metadata descriptions.

#### Isolated Sandbox & Security
- **Root Folder Isolation**: All file operations are strictly confined to a single folder named `AppWorkspace_{UserId}` (where `{UserId}` is resolved from the active user context `McpContext.UserId`). If this folder does not exist, it is created automatically on the first request.
- **Path Traversal Protection**: Google Drive files are identified by alphanumeric IDs rather than paths. The tools recursively scan the user's workspace, build a map of valid descendant file/folder IDs, and calculate their virtual paths (e.g., `/docs/spec.md`). Any action requesting a `fileId` or `parentId` not present in the user's workspace map is immediately rejected with an `UnauthorizedAccessException`.
- **Filename Validation**: Filenames cannot contain traversal sequences like `/`, `\`, or `..`.

#### Excluded/Included Files
- The tools only expose **folders** (`application/vnd.google-apps.folder`) and **Markdown files** (`.md` extension or `text/markdown`).

#### Mandatory Descriptions
- LLM agents need context without downloading files. Every file or folder created or listed **must** contain a description, which maps to the native Google Drive metadata `description` field.

#### Exposed Tools
1. `gdrive_list` — Recursively lists the workspace hierarchy and returns item IDs, names, virtual paths, types, and descriptions.
2. `gdrive_read(fileId)` — Downloads and returns the complete text of a markdown file.
3. `gdrive_write(fileId, content)` — Updates the complete text of a markdown file.
4. `gdrive_create(name, type, description, parentId)` — Creates a new folder or empty markdown file with a mandatory description.
5. `gdrive_update_description(id, newDescription)` — Modifies the metadata description of an item.
6. `gdrive_delete(id)` — Trashes the file or folder (moving to trash). The root workspace folder cannot be trashed.

### 🔑 Native SSH Tools (ssh_execute)

We provide a secure, native SSH tool that executes command-line statements on a remote Linux server using a private key file stored directly inside the user's isolated Google Drive sandbox.

#### Multi-Tenant & In-Memory Security
- **No Local Files**: Private keys are never written to the container's local disk or host machine. They are retrieved from the user's Google Drive workspace directly into memory for the duration of the connection and immediately discarded.
- **Strict User Isolation**: Connection sessions and keys are isolated using the active user context (`McpContext.UserId`). One user can never access or hijack another user's active SSH session or key.
- **Allowed Sandbox Extensions**: The Google Drive sandbox allows standard SSH key names (starting with `id_`), configurations (`config`), and key extensions (`.pem`, `.key`) in addition to standard `.md` files.

> [!WARNING]
> **Owner-Only Security Restriction**: Private keys and config files uploaded to Google Drive are technically accessible to the bot owner (since the bot owner controls the application runtime, database, and the Google Drive client configuration). Therefore, using the SSH MCP functionality is **strongly recommended only for the bot owner themselves**.
> 
> **Access Restriction**: The `ssh_execute` tool is dynamically filtered and is **only visible and executable by the configured Bot Owner** (based on `OwnerId` in configuration). Non-owners cannot see or call this tool.
> 
> **Legal Disclaimer & Prohibitions**: The bot owner is strictly prohibited from utilizing this SSH MCP functionality to intercept, retrieve, or steal private keys belonging to other bot users (either by modifying the source code or using other administrative access methods). Doing so constitutes a cybercrime and carries severe **criminal liability** under applicable local and international legislation. The developers/authors of this software assume absolutely **no liability** for any such unauthorized actions, security breaches, or legal violations committed by the bot owner.

#### Exposed Tools
1. `ssh_execute` — Connects to a remote Linux host and runs a shell command in-memory.
   - `host` (string, required): The target server's IP address or hostname.
   - `port` (number, default 22): SSH port.
   - `username` (string, required): SSH username.
   - `keyFileId` (string, required): The Google Drive file ID of the private key.
   - `command` (string, required): The command to run.
   - `timeout` (integer, default 30000): Timeout in milliseconds.

#### 🛡️ SSH Key Generation & Security Best Practices
To ensure absolute security of your remote environments, adhere to these guidelines:
1. **Generate a Dedicated Key**: Always generate a dedicated SSH key pair for the bot. Never reuse your personal keys. Use the modern Ed25519 format with no passphrase (since the bot executes headless):
   ```bash
   ssh-keygen -t ed25519 -f id_mcp_key -N "" -C "tg-bot-mcp-key"
   ```
2. **Dedicated Restricted User**: Create a restricted, non-root user (e.g., `mcp-agent`) on the target server:
   ```bash
   sudo useradd -m -s /bin/bash mcp-agent
   ```
   Install the public key `id_mcp_key.pub` in `~mcp-agent/.ssh/authorized_keys` with standard UNIX permissions (`600`).
3. **Limit Sudo Privileges**: If the bot needs to run privileged commands, configure `/etc/sudoers` to allow `NOPASSWD` *only* for the specific necessary commands, rather than general root access:
   ```text
   mcp-agent ALL=(ALL) NOPASSWD: /usr/bin/systemctl restart my-service
   ```
4. **Google Drive Storage**: Upload your private key `id_mcp_key` and your `config` file directly to your Google Drive workspace folder `.ssh/` so that the AI can discover them via `gdrive_list` and use them dynamically.

### 🔑 Credentials Configuration

Add the `GoogleDriveSettings` block under `AppSettings` in `appsettings.json` or as environment variables (OAuth is used by default):

```json
"AppSettings": {
  "GoogleDriveSettings": {
    "AuthType": "OAuth", 
    "ClientSecretsPath": ".secrets/client_secrets.json",
    "ServiceAccountKeyPath": ".secrets/service_account.json",
    "TokenStorePath": ".secrets/token_store",
    "RootFolderTemplate": "AppWorkspace_{UserId}",
    "DefaultUserId": "default"
  }
}
```

#### Authorization Methods:
1. **OAuth 2.0 (Default)**: Place your client secrets file in `.secrets/client_secrets.json` (configured via `ClientSecretsPath`). The token store will be saved automatically in `.secrets/token_store/`. If `AuthType` is omitted, `OAuth` is selected by default.
2. **ServiceAccount**: Place your JSON key file in `.secrets/service_account.json` (configured via `ServiceAccountKeyPath`) and explicitly set `"AuthType": "ServiceAccount"` in your configuration.

> [!WARNING]
> Keep your secret keys safe! The `.secrets/` directory is automatically added to `.gitignore` and must **never** be committed to the repository.

---

## 🔒 Production Telemetry & DB MCP Gateway (IDE Development Tool)

For IDE-based development (e.g. Cursor, VS Code, Windsurf), we provide a secure, developer-controlled MCP server (`.agent/prod-mcp-gateway.js`) that allows your IDE agents to safely query the production PostgreSQL database and retrieve .NET Aspire logs over Tailscale, purely via structured APIs.

### 🛡️ Core Features & Safeguards
1. **Explicit Daemon Activation**: The gateway will **never** automatically connect to production. You must explicitly start it by running `node .agent/prod-mcp-gateway.js --daemon` in your terminal. When the daemon is off, the IDE client returns zero tools.
2. **Double-Layered SQL Sandbox**: The database query tool only executes `SELECT`, `EXPLAIN`, `SHOW`, or `DESCRIBE` statements, and enforces a word-boundary regex blocklist preventing any chained or nested modification statements (like `INSERT`, `UPDATE`, `DELETE`, `DROP`).
3. **Tailscale Network Dependency**: Connects securely to the private Wireguard Tailscale IP of your server (`100.82.239.59`). If Tailscale is disconnected, all requests immediately fail safely.

### 🛠️ Exposed Tools for IDE Agents
1. `query_prod_database(sql)` — Safely executes a read-only query against the production DB and returns a clean, formatted Markdown table.
2. `get_prod_logs(service_name, limit, filter)` — Connects to .NET Aspire's HTTP REST API (`/api/telemetry/logs`), flattens OTLP JSON logs, and returns a clean chronological stream of logs.
3. `get_prod_resources()` — Lists all running production services and their states registered in the .NET Aspire Dashboard.

### 🚀 Setup & Usage
1. Connect to **Tailscale** on your local machine.
2. Ensure the remote Aspire Dashboard REST API is enabled on your production server by adding `DASHBOARD__API__DISABLED: "false"` to the `aspire-dashboard` environment variables in `docker-compose.yml` and redeploying.
3. Run the gateway daemon in your terminal:
   ```bash
   node .agent/prod-mcp-gateway.js --daemon
   ```
   *(Note: The script will automatically verify and install the PostgreSQL `pg` Node.js driver on first run).*
4. Ask your IDE agent to describe the production database schema or fetch logs.
5. Press `Ctrl+C` in your terminal to stop the daemon and instantly terminate all production access.

---

## 📂 Managing Tools

Tools are managed via the database in the `McpTools` table, with initial state driven by configuration.

### Administrative Command
- `/tools` — Displays a list of currently active tools and their descriptions.

### Configuration (`appsettings.json`)
You can define default tools that will be automatically seeded into the database:

```json
"McpSettings": {
  "DefaultTools": [
    {
      "Name": "Web Search (DDG)",
      "Command": "npx",
      "Args": "[\"-y\", \"@oevortex/ddg_search\"]",
      "EnvVars": "{}"
    }
  ]
}
```

### Database Columns (`McpTools` table)

| Column | Description | Example |
|---|---|---|
| **Name** | Unique display name | `Puppeteer` |
| **Command** | Command to run | `npx` |
| **Args** | JSON array of arguments | `["-y", "@modelcontextprotocol/server-puppeteer"]` |
| **EnvVars** | JSON dictionary of env vars | `{"DEBUG": "mcp:*"}` |
| **IsActive** | Enable/Disable | `true` |

## 🧪 Testing Tools

You can verify that tools are working by:
1. Checking startup logs for `[HEALTH CHECK PASSED]`.
2. Running the `/tools` command in Telegram.
3. Asking the bot a question that requires a tool (e.g., "Search for latest .NET 10 news").

## ⚠️ Infrastructure
- **Dockerfile.mcp**: Defines the environment for MCP tools (Node.js 22 + Chromium).
- **Environment Variable**: `MCP_RUNTIME_CONTAINER` must point to the name of the runtime container (default: `gpt_mcp_runtime`).
