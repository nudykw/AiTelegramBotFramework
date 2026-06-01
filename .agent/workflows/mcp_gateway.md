---
description: Run the background MCP gateway (mcp-gateway.js) to synchronize the codebase with codegraph
---

# 🚀 Running the MCP Gateway (mcp-gateway.js)

This workflow is designed to quickly start the background MCP gateway daemon, which coordinates the work of `codegraph` and monitors file changes in real-time.

When invoking this workflow, perform the following actions:

1. **Start the Daemon**: Run the gateway script in the background:
   ```bash
   node .agent/mcp-gateway.js
   ```

2. **Check Logs**: Check the log file `.agent/daemon.log` to confirm a successful startup.

3. **Synchronization**: Verify that the daemon has successfully started indexing and launched the `codegraph` MCP server.
