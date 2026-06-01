const crypto = require('crypto');
const fs = require('fs');
const path = require('path');
const net = require('net');
const http = require('http');

const projectRoot = path.resolve(__dirname, '..');

// ==================== CONFIGURATION & PORT CALCULATION ====================
// Calculate unique TCP port for this MCP server based on project path + 'memory' suffix
const hash = crypto.createHash('md5').update(projectRoot + '-memory').digest('hex');
const port = 13600 + (parseInt(hash.substring(0, 8), 16) % 1000);

// ==================== AUTO-INSTALL DEPENDENCIES ====================
function ensureDependencies() {
  const deps = ['surrealdb', '@xenova/transformers'];
  let missing = false;
  for (const dep of deps) {
    try {
      require(dep);
    } catch (e) {
      missing = true;
      break;
    }
  }

  if (missing) {
    console.log('[MEM-MCP] Missing required Node.js libraries. Bootstrapping dependencies inside .agent/ ...');
    const { execSync } = require('child_process');
    try {
      execSync('npm install surrealdb @xenova/transformers', { cwd: __dirname, stdio: 'inherit' });
      console.log('[MEM-MCP] Dependencies successfully installed.');
    } catch (err) {
      console.error('[MEM-MCP] Failed to automatically install dependencies. Please run "npm install surrealdb @xenova/transformers" in the .agent/ directory.', err);
      process.exit(1);
    }
  }
}

if (process.argv.includes('--daemon')) {
  ensureDependencies();
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
    console.error(
      `\n========================================================================\n` +
      `[MEM-MCP] Local Project Memory MCP Daemon is NOT running.\n` +
      `To allow agents to search docs & code semantics, start the daemon:\n` +
      `  node .agent/project-memory-mcp.js --daemon\n` +
      `========================================================================\n`
    );
    process.exit(0); // Exit cleanly
  });

  client.on('end', () => {
    process.exit(0);
  });
}

// ==================== DAEMON MODE ====================
function runDaemon(port) {
  const { Surreal } = require('surrealdb');
  
  let extractor = null;
  const db = new Surreal();
  let isIndexing = false;

  console.log(`[MEM-MCP] Starting Local Graph-RAG Project Memory Daemon on port ${port}...`);

  // Initialize and run
  async function init() {
    // 1. Verify SurrealDB on port 3004
    console.log('[MEM-MCP] Connecting to SurrealDB at 127.0.0.1:3004...');
    try {
      await db.connect('http://127.0.0.1:3004/rpc');
      await db.signin({ username: 'root', password: 'root' });
      
      // Connect & define namespace
      try {
        await db.query('DEFINE NAMESPACE ai_telegram_bot_framework;');
      } catch (e) {}
      await db.use({ namespace: 'ai_telegram_bot_framework' });

      // Connect & define database
      try {
        await db.query('DEFINE DATABASE graph_rag;');
      } catch (e) {}
      await db.use({ namespace: 'ai_telegram_bot_framework', database: 'graph_rag' });

      console.log('[MEM-MCP] Connected to SurrealDB successfully!');
    } catch (err) {
      console.error(
        `\n========================================================================\n` +
        `[MEM-MCP] ERROR: Could not connect to SurrealDB on port 3004.\n` +
        `Please ensure SurrealDB is installed and running:\n` +
        `  surreal start --bind 0.0.0.0:3004 --user root --pass root surrealkv:database.db\n` +
        `========================================================================\n`,
        err
      );
      process.exit(1);
    }

    // 2. Define schema and HNSW vector index
    try {
      await db.query('DEFINE TABLE IF NOT EXISTS file SCHEMALESS;');
      await db.query('DEFINE INDEX IF NOT EXISTS path ON file FIELDS path UNIQUE;');
      await db.query('DEFINE TABLE IF NOT EXISTS chunk SCHEMALESS;');
      // Define HNSW vector index for 384-dimensional cosine distance vectors (MiniLM L6 v2 format)
      await db.query('DEFINE INDEX IF NOT EXISTS chunk_vector_hnsw ON TABLE chunk FIELDS embedding HNSW DIMENSION 384 DIST COSINE;');
      await db.query('DEFINE TABLE IF NOT EXISTS contains SCHEMALESS;');
      console.log('[MEM-MCP] SurrealDB Schema and HNSW Vector Index initialized.');
    } catch (err) {
      console.warn('[MEM-MCP] Warning during Schema definition (might already be initialized):', err.message);
    }

    // 3. Initialize local transformers pipeline
    console.log('[MEM-MCP] Loading local vector embedding model (MiniLM-L6-v2, ~23MB)...');
    try {
      const { pipeline } = require('@xenova/transformers');
      extractor = await pipeline('feature-extraction', 'Xenova/all-MiniLM-L6-v2');
      console.log('[MEM-MCP] Local embedding model successfully loaded!');
    } catch (err) {
      console.error('[MEM-MCP] Error loading local embedding model:', err.message);
      process.exit(1);
    }

    // 4. Perform initial full/incremental indexing in background
    triggerIncrementalIndexing();

    // 5. Start TCP MCP server
    const activeSockets = new Set();
    const server = net.createServer((socket) => {
      activeSockets.add(socket);
      console.log(`[MEM-MCP] IDE Client connected. Active clients: ${activeSockets.size}`);

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
          console.error('[MEM-MCP] Error processing message:', e, line);
        }
      });

      socket.on('data', handleLine);
      socket.on('error', (err) => {
        console.error('[MEM-MCP] Socket error:', err.message);
      });
      socket.on('close', () => {
        activeSockets.delete(socket);
        console.log(`[MEM-MCP] IDE Client disconnected. Active clients: ${activeSockets.size}`);
      });
    });

    server.listen(port, '127.0.0.1', () => {
      console.log(`[MEM-MCP] MCP Gateway listening successfully on 127.0.0.1:${port}`);
      console.log(`[MEM-MCP] Server ready! Start your IDE agent to use token-efficient tools.`);
    });
  }

  // Helper: compute local embeddings
  async function getEmbedding(text) {
    if (!extractor) throw new Error('Embedding extractor is not loaded.');
    const output = await extractor(text, { pooling: 'mean', normalize: true });
    return Array.from(output.data);
  }

  // Helper: MD5 calculation for incremental index
  function getFileMd5(filePath) {
    try {
      const data = fs.readFileSync(filePath);
      return crypto.createHash('md5').update(data).digest('hex');
    } catch (e) {
      return '';
    }
  }

  // ==================== INCREMENTAL FILE INDEXER ====================
  async function triggerIncrementalIndexing() {
    if (isIndexing) return;
    isIndexing = true;
    console.log('[MEM-MCP] Starting incremental file scanner...');

    try {
      const filesToIndex = [];
      const excludedDirs = ['bin', 'obj', '.git', '.codegraph', 'node_modules', '.secrets', 'mcp-uploads', 'scratch'];
      const allowedExtensions = ['.cs', '.md', '.json', '.yml', '.yaml', '.sh', '.ps1', '.sql', '.txt', 'dockerfile'];

      function scan(dir) {
        const list = fs.readdirSync(dir);
        for (const item of list) {
          const fullPath = path.join(dir, item);
          const relativePath = path.relative(projectRoot, fullPath);
          const stat = fs.statSync(fullPath);

          if (stat.isDirectory()) {
            if (!excludedDirs.includes(item)) {
              scan(fullPath);
            }
          } else {
            const ext = path.extname(item).toLowerCase();
            const isAllowed = allowedExtensions.includes(ext) || item.toLowerCase().includes('dockerfile');
            if (isAllowed && stat.size < 500 * 1024) { // Ignore files > 500KB to prevent memory limits
              filesToIndex.push({ fullPath, relativePath });
            }
          }
        }
      }

      scan(projectRoot);
      console.log(`[MEM-MCP] Scanned ${filesToIndex.length} candidate files.`);

      let indexedCount = 0;
      let skippedCount = 0;

      for (const fileInfo of filesToIndex) {
        const md5 = getFileMd5(fileInfo.fullPath);
        
        // Check if file exists in SurrealDB and if MD5 hash matches
        const existing = await db.query('SELECT * FROM file WHERE path = $path;', { path: fileInfo.relativePath });
        const fileNode = existing[0] && existing[0][0];

        if (fileNode && fileNode.md5 === md5) {
          skippedCount++;
          continue; // File is unchanged, skip indexing!
        }

        // Delete old chunks and relations for this file if it was modified
        if (fileNode) {
          console.log(`[MEM-MCP] File changed: ${fileInfo.relativePath}. Updating index...`);
          await db.query('DELETE chunk WHERE file_path = $path;', { path: fileInfo.relativePath });
          await db.query('DELETE contains WHERE in = $fileId;', { fileId: fileNode.id });
        } else {
          console.log(`[MEM-MCP] New file detected: ${fileInfo.relativePath}. Indexing...`);
        }

        // Read content and chunk
        const content = fs.readFileSync(fileInfo.fullPath, 'utf8');
        const chunks = chunkFile(fileInfo.relativePath, content);

        // Store file record
        let fileRecordId;
        if (fileNode) {
          fileRecordId = fileNode.id;
          await db.query('UPDATE file SET md5 = $md5 WHERE id = $id;', { id: fileRecordId, md5 });
        } else {
          const res = await db.query('INSERT INTO file $data;', { data: { path: fileInfo.relativePath, md5 } });
          fileRecordId = res[0][0].id;
        }

        // Create embeddings and store chunks
        for (let i = 0; i < chunks.length; i++) {
          const chunk = chunks[i];
          const embedding = await getEmbedding(chunk.text);

          // Create chunk node
          const resChunk = await db.query('INSERT INTO chunk $data;', {
            data: {
              file_path: fileInfo.relativePath,
              chunk_index: i,
              text: chunk.text,
              embedding: embedding,
              symbol: chunk.symbol || ''
            }
          });
          const chunkNode = resChunk[0][0];

          // Create graph relation: file -> contains -> chunk
          await db.query('RELATE $fileId->contains->$chunkId;', { fileId: fileRecordId, chunkId: chunkNode.id });
        }

        indexedCount++;
      }

      console.log(`[MEM-MCP] Incremental indexing complete. Indexed: ${indexedCount}, Skipped (unchanged): ${skippedCount}`);
    } catch (err) {
      console.error('[MEM-MCP] Error during incremental indexing:', err);
    } finally {
      isIndexing = false;
    }
  }

  // Chunker implementation
  function chunkFile(relativePath, content) {
    const ext = path.extname(relativePath).toLowerCase();
    const chunks = [];

    if (ext === '.cs') {
      // C# Smart chunker: chunk by methods, classes, namespaces
      const lines = content.split('\n');
      let currentChunk = [];
      let currentSymbol = '';
      
      for (let i = 0; i < lines.length; i++) {
        const line = lines[i];
        const trimmed = line.trim();

        // Detect class/method boundaries
        if (trimmed.startsWith('public') || trimmed.startsWith('private') || trimmed.startsWith('protected') || trimmed.startsWith('internal')) {
          if (trimmed.includes('class') || trimmed.includes('interface') || trimmed.includes('struct') || trimmed.includes('record')) {
            if (currentChunk.length > 0) {
              chunks.push({ text: currentChunk.join('\n'), symbol: currentSymbol });
              currentChunk = [];
            }
            currentSymbol = trimmed.split('{')[0].trim();
          } else if (trimmed.includes('(') && !trimmed.startsWith('new')) {
            // Method signature
            if (currentChunk.length > 0) {
              chunks.push({ text: currentChunk.join('\n'), symbol: currentSymbol });
              currentChunk = [];
            }
            currentSymbol = trimmed.split('{')[0].trim();
          }
        }

        currentChunk.push(line);

        // Keep chunks below ~1500 chars to maintain token efficiency
        if (currentChunk.join('\n').length > 1500) {
          chunks.push({ text: currentChunk.join('\n'), symbol: currentSymbol });
          currentChunk = [];
        }
      }

      if (currentChunk.length > 0) {
        chunks.push({ text: currentChunk.join('\n'), symbol: currentSymbol });
      }
    } else if (ext === '.md') {
      // Markdown smart chunker: split by headings
      const lines = content.split('\n');
      let currentChunk = [];
      let currentHeader = '';

      for (const line of lines) {
        if (line.startsWith('#')) {
          if (currentChunk.length > 0) {
            chunks.push({ text: currentChunk.join('\n'), symbol: currentHeader });
            currentChunk = [];
          }
          currentHeader = line.replace(/#/g, '').trim();
        }
        currentChunk.push(line);

        if (currentChunk.join('\n').length > 1500) {
          chunks.push({ text: currentChunk.join('\n'), symbol: currentHeader });
          currentChunk = [];
        }
      }

      if (currentChunk.length > 0) {
        chunks.push({ text: currentChunk.join('\n'), symbol: currentHeader });
      }
    } else {
      // Standard sliding-window text chunker for configurations, scripts, and logs
      const words = content.split(/\s+/);
      const chunkSize = 150; // ~150 words per chunk
      const overlap = 30;

      for (let i = 0; i < words.length; i += (chunkSize - overlap)) {
        const chunkWords = words.slice(i, i + chunkSize);
        if (chunkWords.length > 10) {
          chunks.push({ text: chunkWords.join(' '), symbol: '' });
        }
      }
    }

    return chunks;
  }

  // ==================== MCP MESSAGE HANDLER ====================
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
            name: 'project-memory',
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
              name: 'search_project_memory',
              description: 'Perform a hybrid semantic and keyword search across all code, config, and documentation files in the repository. Extremely token-efficient.',
              inputSchema: {
                type: 'object',
                properties: {
                  query: {
                    type: 'string',
                    description: 'The natural language query describing what you want to find (e.g. "how does billing and balance work?")'
                  },
                  limit: {
                    type: 'integer',
                    description: 'Max number of highly relevant snippets to return (default: 5, max: 10)'
                  }
                },
                required: ['query']
              }
            },
            {
              name: 'get_file_outline',
              description: 'Retrieve a low-cost high-level outline/skeleton of a file (lists classes/methods for code, headings for docs, keys for configs). Prevents reading full files.',
              inputSchema: {
                type: 'object',
                properties: {
                  file_path: {
                    type: 'string',
                    description: 'Relative path to the file in the workspace (e.g. "ServiceLayer/Services/BillingService.cs" or "docs/OBSERVABILITY.md")'
                  }
                },
                required: ['file_path']
              }
            },
            {
              name: 'read_file_chunk',
              description: 'Retrieve a specific, precise chunk of text/code from a file by index. Extremely token-efficient.',
              inputSchema: {
                type: 'object',
                properties: {
                  file_path: {
                    type: 'string',
                    description: 'Relative path of the file'
                  },
                  chunk_index: {
                    type: 'integer',
                    description: 'The 0-based index of the chunk to read'
                  }
                },
                required: ['file_path', 'chunk_index']
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
        if (name === 'search_project_memory') {
          result = await executeSearch(args.query, args.limit);
        } else if (name === 'get_file_outline') {
          result = await executeGetOutline(args.file_path);
        } else if (name === 'read_file_chunk') {
          result = await executeReadChunk(args.file_path, args.chunk_index);
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
            content: [{ type: 'text', text: `Error calling tool: ${err.message}` }],
            isError: true
          }
        };
      }
    }

    return null;
  }

  // Tool 1: Hybrid Search
  async function executeSearch(query, limit = 5) {
    limit = Math.min(Math.max(limit, 1), 10);
    const queryVector = await getEmbedding(query);

    // Perform HNSW vector KNN search via SurrealQL
    // Syntax uses the vector KNN operator `<|K, EF|>`
    const queryStr = `
      SELECT *, vector::distance::knn() AS dist 
      FROM chunk 
      WHERE embedding <|${limit}, 50|> $queryVector 
      ORDER BY dist ASC;
    `;

    try {
      const rawRes = await db.query(queryStr, { queryVector });
      const hits = rawRes[0] || [];

      if (hits.length === 0) {
        return {
          content: [{ type: 'text', text: 'No semantically matching files or code blocks found.' }],
          isError: false
        };
      }

      const formatted = hits.map((hit, idx) => {
        const score = (1 - (hit.dist || 0)).toFixed(4);
        return `### Hit #${idx + 1} | File: [${hit.file_path}](file://${path.join(projectRoot, hit.file_path)}) (Index: ${hit.chunk_index}) | Match: ${score}\n` +
               `Symbol/Context: \`${hit.symbol || 'General content'}\`\n` +
               `\`\`\`\n` +
               `${hit.text}\n` +
               `\`\`\``;
      }).join('\n\n');

      return {
        content: [{ type: 'text', text: formatted }],
        isError: false
      };
    } catch (err) {
      return {
        content: [{ type: 'text', text: `Search database error: ${err.message}` }],
        isError: true
      };
    }
  }

  // Tool 2: Get File Outline
  async function executeGetOutline(filePath) {
    const cleanPath = path.normalize(filePath).replace(/^(\.\.(\/|\\))+/, '');
    
    // Retrieve all chunks from SurrealDB for this file
    const queryStr = 'SELECT chunk_index, symbol, string::len(text) as len FROM chunk WHERE file_path = $filePath ORDER BY chunk_index ASC;';
    
    try {
      const rawRes = await db.query(queryStr, { filePath: cleanPath });
      const chunks = rawRes[0] || [];

      if (chunks.length === 0) {
        return {
          content: [{ type: 'text', text: `No outline found for file: "${cleanPath}". It may not be indexed yet.` }],
          isError: true
        };
      }

      let outlineMarkdown = `## Outline of: \`${cleanPath}\`\n\n` +
                            `This file consists of **${chunks.length} chunks**. Use \`read_file_chunk\` to retrieve any specific chunk body by index.\n\n` +
                            `| Chunk Index | Symbol / Header | Chunk Size (Chars) |\n` +
                            `|---|---|---|\n`;

      chunks.forEach(c => {
        const symbol = c.symbol ? `\`${c.symbol}\`` : '*General text/code*';
        outlineMarkdown += `| **${c.chunk_index}** | ${symbol} | ${c.len} chars |\n`;
      });

      return {
        content: [{ type: 'text', text: outlineMarkdown }],
        isError: false
      };
    } catch (err) {
      return {
        content: [{ type: 'text', text: `Error generating outline: ${err.message}` }],
        isError: true
      };
    }
  }

  // Tool 3: Read File Chunk
  async function executeReadChunk(filePath, chunkIndex) {
    const cleanPath = path.normalize(filePath).replace(/^(\.\.(\/|\\))+/, '');
    
    const queryStr = 'SELECT text, symbol FROM chunk WHERE file_path = $filePath AND chunk_index = $chunkIndex LIMIT 1;';
    
    try {
      const rawRes = await db.query(queryStr, { filePath: cleanPath, chunkIndex });
      const chunk = rawRes[0] && rawRes[0][0];

      if (!chunk) {
        return {
          content: [{ type: 'text', text: `Chunk index ${chunkIndex} not found in file: "${cleanPath}".` }],
          isError: true
        };
      }

      const formatted = `### [${cleanPath}](file://${path.join(projectRoot, cleanPath)}) | Chunk Index: ${chunkIndex}\n` +
                        `Symbol/Context: \`${chunk.symbol || 'General'}\`\n\n` +
                        `\`\`\`\n` +
                        `${chunk.text}\n` +
                        `\`\`\``;

      return {
        content: [{ type: 'text', text: formatted }],
        isError: false
      };
    } catch (err) {
      return {
        content: [{ type: 'text', text: `Error reading chunk: ${err.message}` }],
        isError: true
      };
    }
  }

  // Kick off initialization
  init().catch(err => {
    console.error('[MEM-MCP] Initialization fatal error:', err);
    process.exit(1);
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
