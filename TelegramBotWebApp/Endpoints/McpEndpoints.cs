using DataBaseLayer.Models;
using DataBaseLayer.Repositories;
using Microsoft.AspNetCore.Mvc;
using ServiceLayer.Services.Mcp;
using System.Text;
using System.Text.Json;
using System.IO;

namespace TelegramBotWebApp.Endpoints;

public static class McpEndpoints
{
    private static string _uploadsDir = "/app/mcp-uploads";
    private static string UploadsDir => _uploadsDir;

    public static void MapMcpEndpoints(this WebApplication app)
    {
        var config = app.Services.GetRequiredService<IConfiguration>();
        _uploadsDir = config["AppSettings:Mcp:UploadsDir"] 
            ?? Environment.GetEnvironmentVariable("MCP_UPLOADS_DIR") 
            ?? "/app/mcp-uploads";

        if (!Directory.Exists(UploadsDir))
        {
            Directory.CreateDirectory(UploadsDir);
        }

        var group = app.MapGroup("/admin/mcp").WithTags("MCP Admin");

        // MCP Files Management: Standalone Panel Page
        group.MapGet("/files", () =>
        {
            var files = Directory.Exists(UploadsDir) 
                ? Directory.GetFiles(UploadsDir).Select(Path.GetFileName).ToList() 
                : new List<string?>();
            
            var html = BuildFilesManagerHtml(files);
            return Results.Content(html, "text/html; charset=utf-8");
        });

        // Upload MCP File
        group.MapPost("/file", async (HttpRequest request) =>
        {
            if (!request.HasFormContentType) return Results.BadRequest("Invalid form content type");
            
            var form = await request.ReadFormAsync();
            var file = form.Files.GetFile("file");
            if (file == null || file.Length == 0) return Results.BadRequest("No file uploaded");

            if (!Directory.Exists(UploadsDir))
            {
                Directory.CreateDirectory(UploadsDir);
            }

            var safeName = Path.GetFileName(file.FileName);
            var filePath = Path.Combine(UploadsDir, safeName);
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            return Results.Redirect("/admin/mcp/files");
        }).DisableAntiforgery();

        // Delete MCP File
        group.MapDelete("/file/{filename}", (string filename) =>
        {
            if (string.IsNullOrWhiteSpace(filename)) return Results.BadRequest("Filename is required");

            var safeName = Path.GetFileName(filename);
            var filePath = Path.Combine(UploadsDir, safeName);

            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                return Results.Ok(new { success = true });
            }

            return Results.NotFound(new { error = "File not found" });
        }).DisableAntiforgery();

        group.MapGet("/", async (IRepository<McpToolRecord> repo) =>
        {
            var tools = repo.GetAll().ToList();
            var html = BuildAdminHtml(tools);
            return Results.Content(html, "text/html; charset=utf-8");
        });

        group.MapPost("/save", async (IRepository<McpToolRecord> repo, [FromForm] string? id, [FromForm] string? name, [FromForm] string? args, [FromForm] string? envVars) =>
        {
            if (string.IsNullOrWhiteSpace(name)) return Results.BadRequest("Name is required");

            var cleanEnv = string.IsNullOrWhiteSpace(envVars) ? "{}" : envVars;
            var cleanArgs = args?.Trim() ?? "[]";

            if (!string.IsNullOrWhiteSpace(cleanArgs))
            {
                // 1. Strip 'npx ' from the beginning if present
                if (cleanArgs.StartsWith("npx ", StringComparison.OrdinalIgnoreCase))
                {
                    cleanArgs = cleanArgs.Substring(4).Trim();
                }

                // 2. Validate and fallback if it's not a JSON array
                try
                {
                    using var doc = JsonDocument.Parse(cleanArgs);
                    if (doc.RootElement.ValueKind != JsonValueKind.Array)
                    {
                        cleanArgs = ConvertSpaceSeparatedToJsonArray(cleanArgs);
                    }
                }
                catch (JsonException)
                {
                    cleanArgs = ConvertSpaceSeparatedToJsonArray(cleanArgs);
                }
            }
            else
            {
                cleanArgs = "[]";
            }

            long? parsedId = long.TryParse(id, out var val) ? val : null;

            if (parsedId.HasValue && parsedId.Value > 0)
            {
                var tool = repo.GetAll().FirstOrDefault(t => t.Id == parsedId.Value);
                if (tool != null)
                {
                    tool.Name = name;
                    tool.Args = cleanArgs;
                    tool.EnvVars = cleanEnv;
                    repo.Update(tool);
                }
            }
            else
            {
                var tool = new McpToolRecord
                {
                    Name = name,
                    Args = cleanArgs,
                    EnvVars = cleanEnv,
                    IsActive = true
                };
                repo.Add(tool);
            }
            await repo.SaveChanges();
            return Results.Redirect("/admin/mcp");
        }).DisableAntiforgery();

        group.MapPost("/toggle/{id:long}", async (IRepository<McpToolRecord> repo, long id) =>
        {
            var tool = repo.GetAll().FirstOrDefault(t => t.Id == id);
            if (tool != null)
            {
                tool.IsActive = !tool.IsActive;
                repo.Update(tool);
                await repo.SaveChanges();
            }
            return Results.Redirect("/admin/mcp");
        }).DisableAntiforgery();

        group.MapPost("/delete/{id:long}", async (IRepository<McpToolRecord> repo, long id) =>
        {
            var tool = repo.GetAll().FirstOrDefault(t => t.Id == id);
            if (tool != null)
            {
                repo.Delete(tool);
                await repo.SaveChanges();
            }
            return Results.Redirect("/admin/mcp");
        }).DisableAntiforgery();

        group.MapPost("/restart", async (McpServerManager mcpManager) =>
        {
            await mcpManager.ReloadServersAsync();
            return Results.Redirect("/admin/mcp");
        }).DisableAntiforgery();

        // Test Connection Endpoint: starts process and captures standard error/output
        group.MapPost("/test/{id:long}", async (IRepository<McpToolRecord> repo, long id) =>
        {
            var tool = repo.GetAll().FirstOrDefault(t => t.Id == id);
            if (tool == null) return Results.NotFound("Tool not found");

            var args = JsonSerializer.Deserialize<string[]>(tool.Args) ?? Array.Empty<string>();
            var env = JsonSerializer.Deserialize<Dictionary<string, string?>>(tool.EnvVars) ?? new();

            var allowedCommands = new[] { "npx", "node", "docker" };
            if (string.IsNullOrWhiteSpace(tool.Command) || !allowedCommands.Contains(tool.Command))
            {
                return Results.BadRequest("Invalid command. Only 'npx', 'node', and 'docker' are allowed.");
            }

            var runtimeContainer = Environment.GetEnvironmentVariable("MCP_RUNTIME_CONTAINER");
            var command = tool.Command;
            var finalArgs = new List<string>(args);

            if (!string.IsNullOrEmpty(runtimeContainer))
            {
                command = "docker";
                var dockerArgs = new List<string> { "exec", "-i" };
                foreach (var kvp in env)
                {
                    if (kvp.Value != null)
                    {
                        dockerArgs.Add("-e");
                        dockerArgs.Add($"{kvp.Key}={kvp.Value}");
                    }
                }
                dockerArgs.Add(runtimeContainer);
                dockerArgs.Add(tool.Command);
                dockerArgs.AddRange(args);
                finalArgs = dockerArgs;
            }

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = command,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (var arg in finalArgs)
            {
                psi.ArgumentList.Add(arg);
            }

            if (string.IsNullOrEmpty(runtimeContainer))
            {
                foreach (var kvp in env)
                {
                    if (kvp.Value != null)
                    {
                        psi.EnvironmentVariables[kvp.Key] = kvp.Value;
                    }
                }
            }

            try
            {
                using var process = new System.Diagnostics.Process { StartInfo = psi };
                
                var outputBuilder = new StringBuilder();
                var errorBuilder = new StringBuilder();

                process.OutputDataReceived += (s, e) => { if (e.Data != null) outputBuilder.AppendLine(e.Data); };
                process.ErrorDataReceived += (s, e) => { if (e.Data != null) errorBuilder.AppendLine(e.Data); };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                // Wait 3 seconds to see if the process exits immediately (crashes)
                bool exited = process.WaitForExit(3000);

                var stdOut = outputBuilder.ToString();
                var stdErr = errorBuilder.ToString();

                if (exited)
                {
                    var exitCode = process.ExitCode;
                    return Results.Json(new { 
                        success = false, 
                        message = $"Process exited immediately with code {exitCode}.",
                        stdout = stdOut,
                        stderr = stdErr
                    });
                }
                
                try { process.Kill(); } catch { }

                return Results.Json(new { 
                    success = true, 
                    message = "Process successfully started and remained active.",
                    stdout = stdOut,
                    stderr = stdErr
                });
            }
            catch (Exception ex)
            {
                return Results.Json(new { 
                    success = false, 
                    message = $"Failed to start process: {ex.Message}",
                    stderr = ex.ToString()
                });
            }
        }).DisableAntiforgery();
    }

    private static string BuildAdminHtml(List<McpToolRecord> tools)
    {
        var toolRows = string.Join("\n", tools.Select(t => {
            var encodedName = System.Net.WebUtility.HtmlEncode(t.Name);
            var encodedArgs = System.Net.WebUtility.HtmlEncode(t.Args);
            var encodedEnv = System.Net.WebUtility.HtmlEncode(t.EnvVars);
            
            return $"""
            <div class="card" style="--accent:{(t.IsActive ? "#10b981" : "#6b6b85")}">
              <div class="card-icon">{(t.IsActive ? "🟢" : "⚪")}</div>
              <div class="card-body">
                <div class="card-tag">ID: {t.Id}</div>
                <div class="card-title">{t.Name}</div>
                <div class="card-desc">
                    <code>npx {t.Args}</code><br/>
                    <small>Env: {t.EnvVars}</small>
                </div>
              </div>
              <div class="card-actions">
                <button type="button" class="btn btn-sm" style="background: #f59e0b" onclick="testTool({t.Id}, '{encodedName}')">Test Connection</button>
                <button type="button" class="btn btn-sm btn-secondary" 
                        data-id="{t.Id}" 
                        data-name="{encodedName}" 
                        data-args="{encodedArgs}" 
                        data-env="{encodedEnv}" 
                        onclick="editTool(this)">Edit</button>
                <form action="/admin/mcp/toggle/{t.Id}" method="POST" style="display:inline">
                    <button type="submit" class="btn btn-sm">{(t.IsActive ? "Disable" : "Enable")}</button>
                </form>
                <form action="/admin/mcp/delete/{t.Id}" method="POST" style="display:inline" onsubmit="return confirm('Delete this tool?')">
                    <button type="submit" class="btn btn-sm btn-danger">Delete</button>
                </form>
              </div>
            </div>
            """;
        }));

        return $$"""
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8"/>
  <meta name="viewport" content="width=device-width, initial-scale=1.0"/>
  <title>MCP Tools Admin</title>
  <link rel="preconnect" href="https://fonts.googleapis.com"/>
  <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin/>
  <link href="https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&display=swap" rel="stylesheet"/>
  <style>
    *, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }
    :root {
      --bg:        #0a0a0f;
      --surface:   #13131a;
      --border:    rgba(255,255,255,.07);
      --text:      #e2e2f0;
      --muted:     #6b6b85;
      --radius:    16px;
      --accent:    #6366f1;
    }
    body {
      font-family: 'Inter', system-ui, sans-serif;
      background: var(--bg);
      color: var(--text);
      min-height: 100vh;
      display: flex;
      flex-direction: column;
      align-items: center;
      padding: 2.5rem 1.25rem;
    }
    .page { width: 100%; max-width: 860px; position: relative; z-index: 1; }
    header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 2rem; }
    h1 { font-size: 1.75rem; }
    .card {
      display: flex;
      align-items: center;
      gap: 1rem;
      background: var(--surface);
      border: 1px solid var(--border);
      border-radius: var(--radius);
      padding: 1.25rem;
      margin-bottom: 1rem;
    }
    .card-icon { font-size: 1.2rem; }
    .card-body { flex: 1; }
    .card-tag { font-size: 0.7rem; color: var(--accent); font-weight: 600; margin-bottom: 0.2rem; }
    .card-title { font-size: 1.1rem; font-weight: 600; margin-bottom: 0.2rem; }
    .card-desc { font-size: 0.85rem; color: var(--muted); line-height: 1.4; }
    code { background: rgba(0,0,0,0.3); padding: 2px 4px; border-radius: 4px; }
    .btn {
        background: var(--accent);
        color: white;
        border: none;
        padding: 0.5rem 1rem;
        border-radius: 8px;
        cursor: pointer;
        font-weight: 500;
        font-size: 0.9rem;
        text-decoration: none;
        display: inline-block;
    }
    .btn-sm { padding: 0.3rem 0.6rem; font-size: 0.8rem; }
    .btn-secondary { background: #4b5563; }
    .btn-danger { background: #ef4444; }
    .form-card {
        background: var(--surface);
        border: 1px solid var(--border);
        border-radius: var(--radius);
        padding: 1.5rem;
        margin-bottom: 2rem;
    }
    .form-group { margin-bottom: 1rem; }
    label { display: block; margin-bottom: 0.4rem; font-size: 0.9rem; color: var(--muted); }
    input, textarea {
        width: 100%;
        background: rgba(255,255,255,0.05);
        border: 1px solid var(--border);
        border-radius: 8px;
        padding: 0.6rem;
        color: white;
        font-family: inherit;
    }
    .nav { margin-bottom: 1rem; }
    .nav a { color: var(--muted); text-decoration: none; font-size: 0.9rem; }
    .nav a:hover { color: var(--text); }
    .hint-link {
        display: inline-block;
        margin-left: 1rem;
        font-size: 0.85rem;
        color: var(--accent);
        text-decoration: none;
    }
    .hint-link:hover { text-decoration: underline; }
    footer {
      margin-top: 3rem;
      text-align: center;
      font-size: .78rem;
      color: var(--muted);
    }
  </style>
  <script>
    function editTool(btn) {
        const ds = btn.dataset;
        document.getElementById('form-title').innerText = 'Edit New Tool';
        document.getElementById('form-id').value = ds.id;
        document.getElementById('field-name').value = ds.name;
        document.getElementById('field-args').value = ds.args;
        document.getElementById('field-env').value = ds.env;
        document.getElementById('submit-btn').innerText = 'Save Changes';
        document.getElementById('cancel-btn').style.display = 'inline-block';
        window.scrollTo({ top: 0, behavior: 'smooth' });
    }

    function cancelEdit() {
        document.getElementById('form-title').innerText = 'Add New Tool';
        document.getElementById('form-id').value = '';
        document.getElementById('tool-form').reset();
        document.getElementById('submit-btn').innerText = 'Add Tool';
        document.getElementById('cancel-btn').style.display = 'none';
    }

    async function testTool(id, name) {
        // Show indicator toast
        if (typeof showToast === 'function') {
            showToast(`Testing connection to "${name}"...`);
        } else {
            console.log(`Testing connection to "${name}"...`);
        }
        
        try {
            const response = await fetch(`/admin/mcp/test/${id}`, { method: 'POST' });
            const result = await response.json();
            
            if (result.success) {
                alert(`✅ Success: ${result.message}\n\nSTDOUT:\n${result.stdout || 'None'}\n\nSTDERR:\n${result.stderr || 'None'}`);
            } else {
                alert(`❌ Error: ${result.message}\n\nSTDERR (Logs):\n${result.stderr || 'None'}\n\nSTDOUT:\n${result.stdout || 'None'}`);
            }
        } catch (err) {
            console.error(err);
            alert('Network error while testing tool connection');
        }
    }
  </script>
</head>
<body>
  <div class="page">
    <div class="nav"><a href="/">← Back to Dashboard</a></div>
    <header>
        <h1>🛠️ MCP Tools Management</h1>
        <div style="display: flex; gap: 0.75rem; align-items: center;">
            <a href="/admin/mcp/files" class="btn" style="background: #10b981">📁 Manage Files</a>
            <form action="/admin/mcp/restart" method="POST" style="margin: 0;">
                <button type="submit" class="btn" style="background: #f59e0b">Restart Processes</button>
            </form>
        </div>
    </header>

    <div class="form-card">
        <h3 id="form-title" style="margin-bottom: 1rem;">Add New Tool</h3>
        <form id="tool-form" action="/admin/mcp/save" method="POST">
            <input type="hidden" name="id" id="form-id" value="" />
            <div class="form-group">
                <label>Name</label>
                <input type="text" name="name" id="field-name" placeholder="e.g. Web Search" required />
            </div>
            <div class="form-group">
                <label>Args (JSON array)</label>
                <input type="text" name="args" id="field-args" placeholder='["-y", "@modelcontextprotocol/server-web-search"]' required />
            </div>
            <div class="form-group">
                <label>Env Vars (JSON dict)</label>
                <textarea name="envVars" id="field-env" placeholder='{"GOOGLE_API_KEY": "..."}'></textarea>
            </div>
            <button type="submit" id="submit-btn" class="btn">Add Tool</button>
            <button type="button" id="cancel-btn" class="btn btn-secondary" style="display:none" onclick="cancelEdit()">Cancel</button>
            <a href="https://www.google.com/search?q=npx+mcp+server+list" target="_blank" class="hint-link">🔍 Find more MCP servers</a>
        </form>
    </div>

    <div class="tool-list">
        {{toolRows}}
    </div>
    <footer>
      {{TelegramBotWebApp.Services.GitInfoProvider.RenderFooterLine()}}
    </footer>
  </div>
</body>
</html>
""";
    }

    private static string BuildFilesManagerHtml(List<string?> files)
    {
        var fileRows = files.Count == 0 
            ? """<div class="empty-state">No files uploaded yet. Upload a JSON credentials or config file below.</div>"""
            : string.Join("\n", files.Select(f => {
                var name = System.Net.WebUtility.HtmlEncode(f ?? string.Empty);
                var fullPath = $"/app/mcp-uploads/{name}";
                return $"""
                <div class="file-card" onclick="handleCardClick(event, 'path-{name}', '{name}')">
                  <div class="file-icon">📄</div>
                  <div class="file-info">
                    <div class="file-name">{name}</div>
                    <div class="file-path-container">
                      <code class="file-path" id="path-{name}">{fullPath}</code>
                      <button class="btn btn-sm btn-copy" onclick="event.stopPropagation(); copyPath('path-{name}', '{name}')">Copy Path</button>
                    </div>
                  </div>
                  <div class="file-actions">
                    <button class="btn btn-sm btn-danger" onclick="event.stopPropagation(); deleteFile('{name}')">Delete</button>
                  </div>
                </div>
                """;
            }));

        return $$"""
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8"/>
  <meta name="viewport" content="width=device-width, initial-scale=1.0"/>
  <title>MCP Files Management</title>
  <link rel="preconnect" href="https://fonts.googleapis.com"/>
  <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin/>
  <link href="https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&display=swap" rel="stylesheet"/>
  <style>
    *, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }
    :root {
      --bg:        #0a0a0f;
      --surface:   #13131a;
      --border:    rgba(255,255,255,.07);
      --text:      #e2e2f0;
      --muted:     #6b6b85;
      --radius:    16px;
      --accent:    #10b981;
      --accent-rgb: 16, 185, 129;
    }
    body {
      font-family: 'Inter', system-ui, sans-serif;
      background: var(--bg);
      color: var(--text);
      min-height: 100vh;
      display: flex;
      flex-direction: column;
      align-items: center;
      padding: 2.5rem 1.25rem;
    }
    .page { width: 100%; max-width: 860px; position: relative; z-index: 1; }
    header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 2rem; }
    h1 { font-size: 1.75rem; display: flex; align-items: center; gap: 0.5rem; }
    .nav { margin-bottom: 1rem; }
    .nav a { color: var(--muted); text-decoration: none; font-size: 0.9rem; }
    .nav a:hover { color: var(--text); }
    
    .template-card {
        background: rgba(var(--accent-rgb), 0.05);
        border: 1px dashed rgba(var(--accent-rgb), 0.3);
        border-radius: var(--radius);
        padding: 1.5rem;
        margin-bottom: 2rem;
    }
    .template-title { font-weight: 600; color: var(--accent); margin-bottom: 0.5rem; display: flex; align-items: center; gap: 0.5rem; }
    .template-desc { font-size: 0.85rem; color: var(--muted); margin-bottom: 1rem; line-height: 1.5; }
    pre {
        background: rgba(0,0,0,0.4);
        padding: 1rem;
        border-radius: 8px;
        font-family: monospace;
        font-size: 0.85rem;
        overflow-x: auto;
        border: 1px solid var(--border);
        color: #38bdf8;
    }

    .form-card {
        background: var(--surface);
        border: 1px solid var(--border);
        border-radius: var(--radius);
        padding: 1.5rem;
        margin-bottom: 2rem;
    }
    .form-group { margin-bottom: 1.25rem; }
    label { display: block; margin-bottom: 0.5rem; font-size: 0.9rem; color: var(--muted); }
    
    .file-input-wrapper {
        position: relative;
        display: flex;
        align-items: center;
        gap: 1rem;
    }
    input[type="file"] {
        background: rgba(255,255,255,0.03);
        border: 1px solid var(--border);
        border-radius: 8px;
        padding: 0.75rem;
        color: white;
        font-family: inherit;
        flex: 1;
        cursor: pointer;
    }
    input[type="file"]::file-selector-button {
        background: rgba(255,255,255,0.1);
        border: none;
        padding: 0.4rem 0.8rem;
        border-radius: 4px;
        color: white;
        cursor: pointer;
        margin-right: 0.75rem;
        font-weight: 500;
        transition: background 0.2s;
    }
    input[type="file"]::file-selector-button:hover {
        background: rgba(255,255,255,0.15);
    }

    .btn {
        background: var(--accent);
        color: white;
        border: none;
        padding: 0.6rem 1.25rem;
        border-radius: 8px;
        cursor: pointer;
        font-weight: 500;
        font-size: 0.9rem;
        text-decoration: none;
        display: inline-block;
        transition: all 0.2s;
    }
    .btn:hover { opacity: 0.9; transform: translateY(-1px); }
    .btn:active { transform: translateY(0); }
    .btn-sm { padding: 0.3rem 0.6rem; font-size: 0.8rem; }
    .btn-danger { background: #ef4444; }
    .btn-copy { background: rgba(255,255,255,0.08); border: 1px solid var(--border); color: var(--text); }
    .btn-copy:hover { background: rgba(255,255,255,0.12); }

    .file-card {
      cursor: pointer;
      display: flex;
      align-items: center;
      gap: 1rem;
      background: var(--surface);
      border: 1px solid var(--border);
      border-radius: var(--radius);
      padding: 1.25rem;
      margin-bottom: 1rem;
      transition: all 0.25s cubic-bezier(0.16, 1, 0.3, 1);
    }
    .file-card:hover { 
      border-color: var(--accent); 
      transform: translateY(-2px);
      box-shadow: 0 8px 24px -6px rgba(16, 185, 129, 0.2);
    }

    /* Floating Balloon/Toast */
    .toast-notification {
        position: fixed;
        bottom: 2rem;
        left: 50%;
        transform: translate(-50%, 100px);
        background: rgba(16, 185, 129, 0.95);
        color: white;
        padding: 0.8rem 1.6rem;
        border-radius: 12px;
        font-weight: 500;
        font-size: 0.9rem;
        box-shadow: 0 10px 30px -5px rgba(16, 185, 129, 0.4);
        opacity: 0;
        transition: all 0.3s cubic-bezier(0.16, 1, 0.3, 1);
        z-index: 10000;
        backdrop-filter: blur(8px);
        display: flex;
        align-items: center;
        gap: 0.5rem;
        border: 1px solid rgba(255, 255, 255, 0.1);
        white-space: nowrap;
        pointer-events: none;
    }
    .toast-notification.show {
        transform: translate(-50%, 0);
        opacity: 1;
    }
    .file-icon { font-size: 1.5rem; }
    .file-info { flex: 1; min-width: 0; }
    .file-name { font-size: 1rem; font-weight: 600; margin-bottom: 0.3rem; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .file-path-container { display: flex; align-items: center; gap: 0.75rem; }
    code.file-path {
        background: rgba(0,0,0,0.3);
        padding: 4px 8px;
        border-radius: 6px;
        font-size: 0.8rem;
        color: #a7f3d0;
        font-family: monospace;
        overflow-x: auto;
        white-space: nowrap;
        flex: 1;
        border: 1px solid rgba(255,255,255,.03);
    }
    .empty-state {
        text-align: center;
        padding: 3rem;
        background: var(--surface);
        border: 1px dashed var(--border);
        border-radius: var(--radius);
        color: var(--muted);
        font-size: 0.95rem;
    }
    footer {
      margin-top: 3rem;
      text-align: center;
      font-size: .78rem;
      color: var(--muted);
    }
  </style>
  <script>
    async function deleteFile(filename) {
        if (!confirm(`Are you sure you want to delete "${filename}"? This cannot be undone.`)) return;
        try {
            const response = await fetch(`/admin/mcp/file/${encodeURIComponent(filename)}`, {
                method: 'DELETE'
            });
            if (response.ok) {
                location.reload();
            } else {
                const errData = await response.json();
                alert('Error: ' + (errData.error || 'Failed to delete file'));
            }
        } catch (err) {
            console.error(err);
            alert('Network error while deleting file');
        }
    }

    function showToast(message) {
        let toast = document.getElementById('toast-notification');
        if (!toast) {
            toast = document.createElement('div');
            toast.id = 'toast-notification';
            toast.className = 'toast-notification';
            document.body.appendChild(toast);
        }
        toast.innerHTML = '<span>📋</span> ' + message;
        toast.classList.add('show');
        
        if (window.toastTimeout) clearTimeout(window.toastTimeout);
        window.toastTimeout = setTimeout(() => {
            toast.classList.remove('show');
        }, 2500);
    }

    function copyPath(elementId, filename) {
        const text = document.getElementById(elementId).innerText;
        
        const successHandler = () => {
            showToast(`Path for "${filename}" copied!`);
            const btn = document.querySelector(`[onclick*="'${elementId}'"]`);
            if (btn) {
                const oldText = btn.innerText;
                btn.innerText = 'Copied!';
                btn.style.color = '#10b981';
                setTimeout(() => {
                    btn.innerText = oldText;
                    btn.style.color = '';
                }, 2000);
            }
        };

        const errorHandler = (err) => {
            console.error('Could not copy text: ', err);
        };

        if (navigator.clipboard && navigator.clipboard.writeText) {
            navigator.clipboard.writeText(text).then(successHandler).catch(errorHandler);
        } else {
            // Fallback for non-secure HTTP contexts (e.g. raw IP HTTP addresses)
            try {
                const textArea = document.createElement("textarea");
                textArea.value = text;
                textArea.style.top = "0";
                textArea.style.left = "0";
                textArea.style.position = "fixed";
                document.body.appendChild(textArea);
                textArea.focus();
                textArea.select();
                const successful = document.execCommand('copy');
                document.body.removeChild(textArea);
                if (successful) {
                    successHandler();
                } else {
                    errorHandler('execCommand failed');
                }
            } catch (err) {
                errorHandler(err);
            }
        }
    }

    function handleCardClick(event, pathElementId, filename) {
        // Ignore click if it originated from the delete action container
        if (event.target.closest('.file-actions')) {
            return;
        }
        copyPath(pathElementId, filename);
    }
  </script>
</head>
<body>
  <div class="page">
    <div class="nav"><a href="/admin/mcp">← Back to MCP Tools</a></div>
    <header>
        <h1>📁 MCP Support Files</h1>
    </header>

    <div class="template-card">
        <div class="template-title">💡 How to use uploaded files:</div>
        <div class="template-desc">
            All uploaded files are automatically available to your MCP servers in <strong>Read-Only</strong> mode at their full path inside the container.
            Copy the file path and specify it in the <strong>Env Vars</strong> block of your MCP tool. Example for Google Drive credentials:
        </div>
        <pre>{
  "GOOGLE_APPLICATION_CREDENTIALS": "/app/mcp-uploads/credentials.json"
}</pre>
    </div>

    <div class="form-card">
        <h3 style="margin-bottom: 1rem;">Upload Support File</h3>
        <form action="/admin/mcp/file" method="POST" enctype="multipart/form-data">
            <div class="form-group">
                <label>Select credentials, keys, or config file (.json, .txt, etc.)</label>
                <div class="file-input-wrapper">
                    <input type="file" name="file" required />
                    <button type="submit" class="btn">Upload File</button>
                </div>
            </div>
        </form>
    </div>

    <h3 style="margin-bottom: 1rem; font-weight: 600;">Uploaded Files</h3>
    <div class="file-list">
        {{fileRows}}
    </div>
    <footer>
      {{TelegramBotWebApp.Services.GitInfoProvider.RenderFooterLine()}}
    </footer>
  </div>
</body>
</html>
""";
    }

    private static string ConvertSpaceSeparatedToJsonArray(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "[]";
        
        var args = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;
        
        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];
            if (c == '\"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ' ' && !inQuotes)
            {
                if (current.Length > 0)
                {
                    args.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }
        if (current.Length > 0)
        {
            args.Add(current.ToString());
        }

        return JsonSerializer.Serialize(args);
    }
}
