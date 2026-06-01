---
description: Run the background production MCP gateway (prod-mcp-gateway.js) to access logs and DB on production via Tailscale
---

# 🔒 Running the Production MCP Gateway (prod-mcp-gateway.js)

This workflow is designed to start the background MCP gateway daemon, which allows IDE agents to securely read .NET Aspire dashboard logs and send SELECT queries to the production PostgreSQL database over the Tailscale network.

When invoking this workflow, perform the following actions:

1. **Start the Daemon**: Run the gateway script in the background:
   ```bash
   node .agent/prod-mcp-gateway.js --daemon
   ```
   *(On first run, the script will automatically check and install the Node.js PostgreSQL driver `pg` locally into the `.agent` directory).*

2. **Check Connection**: Verify that the gateway started successfully and is listening on port `13585` (or the calculated port for your project). You should see logs like:
   - `[PROD-MCP] Tailscale IP: 100.82.239.59`
   - `[PROD-MCP] Listening successfully on 127.0.0.1:13585`

3. **Usage in IDE**: Agents in your IDE will now have access to three secure API tools:
   - `query_prod_database` — Run SQL read queries (SELECT, EXPLAIN, SHOW) with injection protection.
   - `get_prod_logs` — Retrieve structured real-time .NET Aspire logs.
   - `get_prod_resources` — List active containers and services on production.

4. **Stop the Gateway**: When debugging is complete, stop the gateway by pressing **`Ctrl+C`** in the terminal window, or find and terminate the process by port:
   ```bash
   kill $(lsof -t -i:13585)
   ```
