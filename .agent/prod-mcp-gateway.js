const crypto = require('crypto');
const fs = require('fs');
const path = require('path');
const net = require('net');
const http = require('http');

const projectRoot = path.resolve(__dirname, '..');

// ==================== CONFIGURATION & ENV PARSING ====================
function loadEnv() {
  const envPath = path.join(projectRoot, '.env');
  if (!fs.existsSync(envPath)) {
    return {};
  }
  const content = fs.readFileSync(envPath, 'utf8');
  const env = {};
  content.split('\n').forEach(line => {
    const trimmed = line.trim();
    if (!trimmed || trimmed.startsWith('#')) return;
    const idx = trimmed.indexOf('=');
    if (idx === -1) return;
    const key = trimmed.substring(0, idx).trim();
    let val = trimmed.substring(idx + 1).trim();
    if ((val.startsWith('"') && val.endsWith('"')) || (val.startsWith("'") && val.endsWith("'"))) {
      val = val.substring(1, val.length - 1);
    }
    env[key] = val;
  });
  return env;
}

const env = loadEnv();

// Extract parameters
const dbConnectionString = env.DB_CONNECTION_STRING || '';
const aspirePublicUrl = env.ASPIRE_PUBLIC_URL || 'http://100.82.239.59:18888';

// Extract Tailscale IP
let tailscaleIp = '100.82.239.59';
try {
  const url = new URL(aspirePublicUrl);
  tailscaleIp = url.hostname;
} catch (e) {
  // Fallback to parsed string if URL fails
  const match = aspirePublicUrl.match(/100\.\d{1,3}\.\d{1,3}\.\d{1,3}/);
  if (match) tailscaleIp = match[0];
}

// Parse Connection String to pg client config
function parsePostgresConnectionString(connStr, hostIp) {
  const parts = connStr.split(';').reduce((acc, part) => {
    const eqIdx = part.indexOf('=');
    if (eqIdx === -1) return acc;
    const key = part.substring(0, eqIdx).trim().toLowerCase();
    const val = part.substring(eqIdx + 1).trim();
    acc[key] = val;
    return acc;
  }, {});

  return {
    host: hostIp,
    port: parseInt(parts.port || '5432', 10),
    database: parts.database || 'db',
    user: parts.username || parts.user || parts['user id'] || 'nudyk',
    password: parts.password || ''
  };
}

// ==================== AUTO-INSTALL pg DRIVER ====================
function ensurePgDriver() {
  try {
    require('pg');
  } catch (e) {
    console.error('[PROD-MCP] "pg" module not found. Installing PostgreSQL driver locally in .agent/ ...');
    const { execSync } = require('child_process');
    try {
      execSync('npm install pg', { cwd: __dirname, stdio: 'inherit' });
      console.log('[PROD-MCP] "pg" module successfully installed.');
    } catch (err) {
      console.error('[PROD-MCP] Failed to automatically install "pg" driver. Please run "npm install pg" in the .agent folder.', err);
      process.exit(1);
    }
  }
}

// Calculate unique port based on project root + prod suffix to prevent collisions
const hash = crypto.createHash('md5').update(projectRoot + '-prod').digest('hex');
const port = 13000 + (parseInt(hash.substring(0, 8), 16) % 1000);

if (process.argv.includes('--daemon')) {
  ensurePgDriver();
  runDaemon(port);
} else {
  runClient(port);
}

// ==================== CLIENT MODE ====================
function runClient(port) {
  const client = net.connect({ port }, () => {
    process.stdin.pipe(client);
    client.pipe(process.stdout);
  });

  client.on('error', () => {
    // Unlike mcp-gateway.js, we do NOT auto-start the daemon here!
    // This gives the developer full control over when production access is enabled.
    console.error(
      `\n========================================================================\n` +
      `[PROD-MCP] Production Telemetry MCP Daemon is NOT running.\n` +
      `To allow agents to query logs/database, start the daemon in your terminal:\n` +
      `  node .agent/prod-mcp-gateway.js --daemon\n` +
      `========================================================================\n`
    );
    process.exit(0); // Exit cleanly so the IDE doesn't crash
  });

  client.on('end', () => {
    process.exit(0);
  });
}

// ==================== DAEMON MODE ====================
function runDaemon(port) {
  console.log(`[PROD-MCP] Starting Production Telemetry & DB MCP Daemon on port ${port}...`);
  console.log(`[PROD-MCP] Tailscale IP: ${tailscaleIp}`);
  console.log(`[PROD-MCP] Aspire UI: ${aspirePublicUrl}`);

  const activeSockets = new Set();
  const server = net.createServer((socket) => {
    activeSockets.add(socket);
    console.log(`[PROD-MCP] IDE Client connected. Total active: ${activeSockets.size}`);

    const handleLine = createLineBuffer(async (line) => {
      try {
        const msg = JSON.parse(line);
        if (msg && typeof msg === 'object' && msg.method) {
          const response = await handleMcpMessage(msg);
          if (response) {
            socket.write(JSON.stringify(response) + '\n');
          }
        }
      } catch (e) {
        console.error('[PROD-MCP] Error processing client message:', e, line);
      }
    });

    socket.on('data', handleLine);

    socket.on('error', (err) => {
      console.error('[PROD-MCP] Socket error:', err.message);
    });

    socket.on('close', () => {
      activeSockets.delete(socket);
      console.log(`[PROD-MCP] IDE Client disconnected. Total active: ${activeSockets.size}`);
    });
  });

  server.listen(port, '127.0.0.1', () => {
    console.log(`[PROD-MCP] Listening successfully on 127.0.0.1:${port}`);
    console.log(`[PROD-MCP] Press Ctrl+C to stop and completely disable production access.`);
  });
}

function createLineBuffer(onLine) {
  let buffer = '';
  return (chunk) => {
    buffer += chunk.toString();
    let lines = buffer.split('\n');
    buffer = lines.pop();
    for (const line of lines) {
      if (line.trim()) {
        onLine(line);
      }
    }
  };
}

// ==================== MCP PROTOCOL HANDLER ====================
async function handleMcpMessage(msg) {
  const { method, params, id } = msg;

  if (method === 'initialize') {
    return {
      jsonrpc: '2.0',
      id,
      result: {
        protocolVersion: '2024-11-05',
        capabilities: {
          tools: {}
        },
        serverInfo: {
          name: 'prod-telemetry',
          version: '1.0.0'
        }
      }
    };
  }

  if (method === 'tools/list') {
    return {
      jsonrpc: '2.0',
      id,
      result: {
        tools: [
          {
            name: 'query_prod_database',
            description: 'Execute a read-only SQL query against the production PostgreSQL database. Supports SELECT, EXPLAIN, and SHOW queries only.',
            inputSchema: {
              type: 'object',
              properties: {
                sql: {
                  type: 'string',
                  description: 'The SQL query to execute (must start with SELECT, EXPLAIN, or SHOW)'
                }
              },
              required: ['sql']
            }
          },
          {
            name: 'get_prod_logs',
            description: 'Fetch structured logs from the production .NET Aspire dashboard. Returns parsed log streams.',
            inputSchema: {
              type: 'object',
              properties: {
                service_name: {
                  type: 'string',
                  description: 'Filter logs by service/resource name (e.g. "DockerContainers", "Databases", or bot container name)'
                },
                limit: {
                  type: 'integer',
                  description: 'Number of logs to retrieve (default: 100, max: 500)'
                },
                filter: {
                  type: 'string',
                  description: 'Text/keyword filter to search inside log messages'
                }
              }
            }
          },
          {
            name: 'get_prod_resources',
            description: 'List all running production services, containers, and databases registered in the Aspire Dashboard with their current state.',
            inputSchema: {
              type: 'object',
              properties: {}
            }
          }
        ]
      }
    };
  }

  if (method === 'tools/call') {
    const { name, arguments: args } = params;
    try {
      let result;
      if (name === 'query_prod_database') {
        result = await executeDatabaseQuery(args.sql);
      } else if (name === 'get_prod_logs') {
        result = await executeGetLogs(args.service_name, args.limit, args.filter);
      } else if (name === 'get_prod_resources') {
        result = await executeGetResources();
      } else {
        return {
          jsonrpc: '2.0',
          id,
          error: {
            code: -32601,
            message: `Tool not found: ${name}`
          }
        };
      }
      return {
        jsonrpc: '2.0',
        id,
        result
      };
    } catch (err) {
      return {
        jsonrpc: '2.0',
        id,
        result: {
          content: [{ type: 'text', text: `Execution error: ${err.message}` }],
          isError: true
        }
      };
    }
  }

  // Unsupported notification or method
  return null;
}

// ==================== TOOL IMPLEMENTATIONS ====================

// Tool 1: Read-only SQL Query
async function executeDatabaseQuery(sql) {
  const sqlTrimmed = sql.trim().toLowerCase();
  const isSafe = sqlTrimmed.startsWith('select') || sqlTrimmed.startsWith('explain') || sqlTrimmed.startsWith('show') || sqlTrimmed.startsWith('describe');
  
  // Double safeguard: block destructive keywords as standalone words anywhere in the query
  const containsDestructive = /\b(insert|update|delete|drop|truncate|alter|create|grant|replace|write|upsert)\b/i.test(sql);

  if (!isSafe || containsDestructive) {
    return {
      content: [{ type: 'text', text: 'Error: Security violation. Only read-only queries (SELECT, EXPLAIN, SHOW, DESCRIBE) are allowed. Destructive statements are strictly blocked.' }],
      isError: true
    };
  }

  const pg = require('pg');
  const dbConfig = parsePostgresConnectionString(dbConnectionString, tailscaleIp);
  const client = new pg.Client(dbConfig);

  try {
    await client.connect();
    const res = await client.query(sql);
    await client.end();

    const markdownTable = formatAsMarkdownTable(res.rows);
    return {
      content: [{ type: 'text', text: markdownTable }],
      isError: false
    };
  } catch (err) {
    try { await client.end(); } catch (e) {}
    return {
      content: [{ type: 'text', text: `Database error: ${err.message}` }],
      isError: true
    };
  }
}

function formatAsMarkdownTable(rows) {
  if (!rows || rows.length === 0) return 'No rows returned (empty set).';
  const headers = Object.keys(rows[0]);
  const headerLine = `| ${headers.join(' | ')} |`;
  const sepLine = `| ${headers.map(() => '---').join(' | ')} |`;
  const rowLines = rows.map(row => {
    return `| ${headers.map(h => {
      const val = row[h];
      if (val === null) return 'NULL';
      if (typeof val === 'object') return JSON.stringify(val);
      return String(val).replace(/\|/g, '\\|').replace(/\n/g, ' ');
    }).join(' | ')} |`;
  });
  return [headerLine, sepLine, ...rowLines].join('\n');
}

// HTTP helper for Aspire REST API queries
function fetchAspireApi(path) {
  return new Promise((resolve, reject) => {
    const url = `${aspirePublicUrl}${path}`;
    const req = http.get(url, (res) => {
      let data = '';
      res.on('data', chunk => { data += chunk; });
      res.on('end', () => {
        if (res.statusCode >= 200 && res.statusCode < 300) {
          try {
            resolve(JSON.parse(data));
          } catch (e) {
            reject(new Error(`Failed to parse Aspire API JSON response: ${e.message}`));
          }
        } else {
          reject(new Error(`Aspire API returned HTTP status ${res.statusCode}: ${data}`));
        }
      });
    });
    req.on('error', err => reject(err));
    req.setTimeout(5000, () => {
      req.destroy();
      reject(new Error('Aspire API request timed out (5s). Check if Tailscale is active and dashboard is running.'));
    });
  });
}

// Tool 2: Get Aspire logs
async function executeGetLogs(serviceName, limit = 100, textFilter = '') {
  limit = Math.min(Math.max(limit, 1), 500);
  
  console.log(`[PROD-MCP] Querying Aspire Dashboard Telemetry API logs...`);
  let data;
  try {
    data = await fetchAspireApi('/api/telemetry/logs');
  } catch (err) {
    return {
      content: [{ type: 'text', text: `Failed to fetch logs from Aspire: ${err.message}` }],
      isError: true
    };
  }

  // Parse OTLP ResourceLogs structure
  const logs = [];
  const resourceLogs = (data && data.data && data.data.resourceLogs) || (data && data.resourceLogs) || [];
  if (Array.isArray(resourceLogs)) {
    for (const rLog of resourceLogs) {
      const resourceAttrs = parseOtlpAttributes(rLog.resource ? rLog.resource.attributes : []);
      const rServiceName = resourceAttrs['service.name'] || 'Unknown';
      const rInstanceId = resourceAttrs['service.instance.id'] || '';

      if (rLog.scopeLogs) {
        for (const sLog of rLog.scopeLogs) {
          const scopeName = sLog.scope ? sLog.scope.name : '';
          if (sLog.logRecords) {
            for (const record of sLog.logRecords) {
              const severity = record.severityText || 'INFO';
              const message = getLogRecordMessage(record.body);
              const timeNano = record.timeUnixNano || record.observedTimeUnixNano || '0';
              const date = new Date(parseInt(timeNano.slice(0, -6) || '0', 10));

              logs.push({
                timestamp: date,
                service: rServiceName,
                instance: rInstanceId,
                level: severity,
                scope: scopeName,
                message: message
              });
            }
          }
        }
      }
    }
  }

  // Sort logs chronologically (newest first for displaying, or oldest first)
  logs.sort((a, b) => b.timestamp - a.timestamp);

  // Apply filters
  let filteredLogs = logs;
  if (serviceName) {
    const cleanService = serviceName.toLowerCase();
    filteredLogs = filteredLogs.filter(l => l.service.toLowerCase().includes(cleanService) || l.instance.toLowerCase().includes(cleanService));
  }
  if (textFilter) {
    const cleanFilter = textFilter.toLowerCase();
    filteredLogs = filteredLogs.filter(l => l.message.toLowerCase().includes(cleanFilter) || l.scope.toLowerCase().includes(cleanFilter));
  }

  // Apply limit
  const sliced = filteredLogs.slice(0, limit);

  if (sliced.length === 0) {
    return {
      content: [{ type: 'text', text: 'No matching logs found.' }],
      isError: false
    };
  }

  // Format into a human-readable stream
  const formattedStream = sliced.map(l => {
    const timeStr = l.timestamp.toISOString().replace('T', ' ').substring(0, 19);
    return `[${timeStr}] [${l.level}] [${l.service}] (${l.scope}): ${l.message}`;
  }).join('\n');

  return {
    content: [{ type: 'text', text: formattedStream }],
    isError: false
  };
}

function parseOtlpAttributes(attrs) {
  if (!Array.isArray(attrs)) return {};
  return attrs.reduce((acc, attr) => {
    const val = attr.value;
    let finalVal = val;
    if (val && typeof val === 'object') {
      finalVal = val.stringValue !== undefined ? val.stringValue :
                 val.intValue !== undefined ? val.intValue :
                 val.boolValue !== undefined ? val.boolValue :
                 val.doubleValue !== undefined ? val.doubleValue :
                 JSON.stringify(val);
    }
    acc[attr.key] = finalVal;
    return acc;
  }, {});
}

function getLogRecordMessage(body) {
  if (!body) return '';
  if (typeof body === 'string') return body;
  if (body.stringValue !== undefined) return body.stringValue;
  if (body.value !== undefined) {
    return typeof body.value === 'object' ? JSON.stringify(body.value) : String(body.value);
  }
  return JSON.stringify(body);
}

// Tool 3: Get Aspire active resources
async function executeGetResources() {
  console.log(`[PROD-MCP] Querying Aspire Dashboard Telemetry API resources...`);
  let resources;
  try {
    resources = await fetchAspireApi('/api/telemetry/resources');
  } catch (err) {
    return {
      content: [{ type: 'text', text: `Failed to fetch resources from Aspire: ${err.message}` }],
      isError: true
    };
  }

  if (!Array.isArray(resources) || resources.length === 0) {
    return {
      content: [{ type: 'text', text: 'No registered resources found in Aspire Dashboard.' }],
      isError: false
    };
  }

  // Format resource attributes and state into a beautiful table
  const formattedRows = resources.map(res => {
    const name = res.name || 'Unknown';
    const type = res.resourceType || 'Unknown';
    const state = res.state || 'Unknown';
    const endpoints = Array.isArray(res.urls) ? res.urls.map(u => u.name + ': ' + u.url).join(', ') : '';
    
    return {
      Name: name,
      Type: type,
      State: state,
      Endpoints: endpoints
    };
  });

  const markdownTable = formatAsMarkdownTable(formattedRows);
  return {
    content: [{ type: 'text', text: markdownTable }],
    isError: false
  };
}
