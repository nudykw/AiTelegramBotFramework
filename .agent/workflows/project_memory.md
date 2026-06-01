---
description: Run the background local semantic memory MCP server (project-memory-mcp.js) to save tokens via SurrealDB
---

# 🧠 Running the Local Memory MCP Server (project-memory-mcp.js)

This workflow is designed to start the background semantic memory daemon of the project, which calculates vector embeddings of files **fully locally** (offline) and caches them in **SurrealDB** to ensure extreme token savings when working with AI agents.

When invoking this workflow, perform the following actions:

1. **Check SurrealDB**: Make sure that the SurrealDB service is running locally on port `3004`. If it is not installed, install it using the command:
   - **Linux / macOS**: `curl -sSf https://install.surrealdb.com | sh`
   - **Windows**: `iwr https://install.surrealdb.com -useb | iex`
   And start the database:
   ```bash
   surreal start --bind 0.0.0.0:3004 --user root --pass root surrealkv:database.db
   ```

2. **Start the Daemon**: Run the memory server script in daemon mode:
   ```bash
   node .agent/project-memory-mcp.js --daemon
   ```
   *(On the very first run, the script will automatically install required Node dependencies and download a lightweight ONNX/Transformers embedding model of ~23 MB. All vector calculations happen locally and are 100% private).*

3. **Check Logs**: Ensure the gateway successfully connected to SurrealDB, initialized the HNSW vector indexes, and started the incremental file scanner:
   - You should see the log: `[MEM-MCP] MCP Gateway listening successfully on 127.0.0.1:13600`

4. **Usage in IDE**: IDE agents will automatically gain access to these tools:
   - `search_project_memory` — Hybrid semantic and keyword search across the entire project (code, configs, docs).
   - `get_file_outline` — Retrieve a structural skeleton of any file to minimize token reads.
   - `read_file_chunk` — Precise reading of selected file fragments.

5. **Stop the Gateway**: To stop the daemon, press **`Ctrl+C`** in the terminal window, or find and terminate the process by port:
   ```bash
   kill $(lsof -t -i:13600)
   ```
