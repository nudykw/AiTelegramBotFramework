# 🧠 AI Agent Workflow & Token Optimization Guide

🇺🇸 **English** | 🇺🇦 [Українська](./AI_AGENT_WORKFLOW.uk.md)

Welcome! This guide explains how to set up and run the advanced AI-assisted development workflow in this repository. 

> [!IMPORTANT]
> **Why this is critical for developers**:
> In modern AI-assisted coding (using agents like Cursor, Windsurf, Roo Code, or Claude Desktop), reading entire source code directories or documentation files consumes millions of LLM tokens, hits context limits, raises costs, and slows down response times. 
> To solve this, we provide **two complementary, high-performance token-saving MCP tools** that reduce token usage by **over 90%** while giving the AI agent complete, exact context over the entire project.

---

## 🛠️ Essential MCP Tools for the AI Agent

We run three non-conflicting background MCP daemons designed to assist the AI agent. They specialize in different tasks, run in parallel, and do not clash:

| Tool | Purpose & Specialization | Port | Tech / Driver |
|---|---|---|---|
| **`mcp-gateway.js`** | **Code Structure Analysis (Codegraph)**: classes, methods, types, and call graph navigation | `12666` | Codegraph (Rust / SQLite) |
| **`project-memory-mcp.js`** | **Local Semantic Memory (Graph-RAG)**: semantic project file searches fully offline | `13585` | ONNX Embeddings / Local Storage |
| **`prod-mcp-gateway.js`** | **System Diagnostics Gateway**: secure access to system logs (.NET Aspire) and database queries for the AI agent (including secure production diagnostics over [Tailscale](https://tailscale.com/)) | Dynamic / `13585` | PostgreSQL / .NET Aspire logs |

---

## 🚀 Quick Start (Fresh Clone Setup)

When you first clone this repository on a new machine, follow these steps to bootstrap the environment:

### Step 1: Install SurrealDB (If not installed)
Our Graph-RAG memory runs on **SurrealDB**. It is extremely fast and runs directly on your local machine:
- **Linux / macOS**:
  ```bash
  curl -sSf https://install.surrealdb.com | sh
  ```
- **Windows (PowerShell)**:
  ```powershell
  iwr https://install.surrealdb.com -useb | iex
  ```
Start the SurrealDB service on port `3004` (using default credentials that the scripts expect):
```bash
surreal start --bind 0.0.0.0:3004 --user root --pass root surrealkv:database.db
```

### Step 2: Set up your `.env` file
Copy `.env.example` to `.env` in the root of the project and ensure your API keys or databases are configured:
```bash
cp .env.example .env
```

### Step 3: Run the MCP Daemons
Open separate terminal windows and run the required daemons:

1. **Start the Code Structure Daemon (Codegraph)**:
   ```bash
   node .agent/mcp-gateway.js --daemon
   ```
2. **Start the Semantic Memory Daemon (Graph-RAG)**:
   ```bash
   node .agent/project-memory-mcp.js --daemon
   ```
   *(Note: The first launch will automatically install required Node.js libraries and download the ~23MB local embedding model to run calculations 100% offline).*
3. **Start the System Diagnostics & Log Gateway (Optional)**:
   ```bash
   node .agent/prod-mcp-gateway.js --daemon
   ```
   *(Note: Grants the AI agent the capability to inspect system logs (.NET Aspire) and perform SELECT database queries in a strictly read-only mode for live diagnostics — including secure production environments over [Tailscale](https://tailscale.com/)).*

---

## 🔒 Security & Privacy Guarantees

1. **100% Local Vectors**: Semantic search embeddings are computed fully locally in JavaScript/Wasm via ONNX Runtime. **No code, text, or configurations are ever sent to external APIs for indexing.**
2. **Explicit Daemon Control**: The IDE client will never auto-start the daemons. Production and development connections remain completely offline until you run the `--daemon` commands manually.
3. **Read-Only Database Safeguards**: The database tools strictly enforce read-only operations, blocking any chained or nested `INSERT`, `UPDATE`, `DELETE`, or `DROP` statements.

---

## 📂 Related Documentation
- **[MCP Infrastructure Guide](./MCP.md)** — In-depth overview of Bot-level MCP architecture.
- **[Observability Stack](./OBSERVABILITY.md)** — Monitoring traces and logs.
