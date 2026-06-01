using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using DataBaseLayer.Contexts;
using DataBaseLayer.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace ServiceLayer.Services.Mcp
{
    public class DatabaseQueryMcpTools : INativeMcpTool
    {
        private readonly StoreContext _dbContext;
        private readonly ITelegramBotClient _botClient;
        private readonly AppSettings _appSettings;
        private readonly ILogger<DatabaseQueryMcpTools> _logger;
        private readonly string _toolName;

        public const string ToolGetSchema = "get_database_schema";
        public const string ToolExecuteQuery = "execute_readonly_query";

        public string Name => _toolName;
        public string ServerName => "native-database";


        public string Description => _toolName switch
        {
            ToolGetSchema => "Returns the database schema information (tables, columns, and types) " +
                             "that you are allowed to query. You must call this tool before generating SQL queries.",
            ToolExecuteQuery => "Executes a read-only dynamic SQL SELECT query against the database " +
                                "and returns the results as a Markdown ASCII table. Strict whitelisting, " +
                                "permission checks, and multi-tenant filters are applied automatically.",
            _ => string.Empty
        };

        public string JsonSchema => _toolName switch
        {
            ToolGetSchema => """
                {
                  "type": "object",
                  "properties": {}
                }
                """,
            ToolExecuteQuery => """
                {
                  "type": "object",
                  "properties": {
                    "sqlQuery": {
                      "type": "string",
                      "description": "The read-only SELECT SQL query to execute. Do not include any semicolon at the end. Make sure to use only the allowed tables and columns from get_database_schema."
                    },
                    "pageSize": {
                      "type": "integer",
                      "description": "Number of rows to return. Default is 10, max is 10.",
                      "default": 10
                    }
                  },
                  "required": ["sqlQuery"]
                }
                """,
            _ => "{\"type\":\"object\",\"properties\":{}}"
        };

        public DatabaseQueryMcpTools(
            string toolName,
            StoreContext dbContext,
            ITelegramBotClient botClient,
            AppSettings appSettings,
            ILogger<DatabaseQueryMcpTools> logger)
        {
            _toolName = toolName;
            _dbContext = dbContext;
            _botClient = botClient;
            _appSettings = appSettings;
            _logger = logger;
        }

        public async Task<string> ExecuteAsync(string argumentsJson)
        {
            return _toolName switch
            {
                ToolGetSchema => await GetSchemaAsync(),
                ToolExecuteQuery => await ExecuteQueryAsync(argumentsJson),
                _ => $"Error: Unknown tool '{_toolName}'."
            };
        }

        // ── 1. GET SCHEMA ───────────────────────────────────────────────────────
        private async Task<string> GetSchemaAsync()
        {
            var userId = McpContext.UserId;
            if (userId == null)
                return "Error: Could not determine current user context.";

            var (isOwner, isAdmin, allowedTables) = await GetUserPermissionsAsync(userId.Value);
            if (!isOwner && !isAdmin)
                return "Error: Access Denied. You do not have permission to query the database.";

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("Database Schema Information:");
                sb.AppendLine("You are strictly allowed to query ONLY the following tables and columns:");
                sb.AppendLine();

                var entityTypes = _dbContext.Model.GetEntityTypes();
                foreach (var entityType in entityTypes)
                {
                    var tableName = entityType.GetTableName();
                    if (string.IsNullOrEmpty(tableName)) continue;

                    // Exclude tables not in whitelist for non-owners (singular/plural insensitive)
                    if (!isOwner && !allowedTables.Any(at => TableNamesMatch(at, tableName)))
                        continue;

                    sb.AppendLine($"Table: {tableName}");
                    var properties = entityType.GetProperties();
                    foreach (var property in properties)
                    {
                        string columnName;
                        try
                        {
                            columnName = property.GetColumnName(StoreObjectIdentifier.Table(tableName, null)) ?? property.Name;
                        }
                        catch
                        {
                            columnName = property.Name;
                        }

                        var columnType = property.GetColumnType();
                        sb.AppendLine($"  - Column: {columnName} (Type: {columnType})");
                    }
                    sb.AppendLine();
                }

                sb.AppendLine("Strict rules for query generation:");
                sb.AppendLine("1. Generate ONLY SELECT statements. Any other SQL keywords (INSERT, UPDATE, DELETE, DROP, ALTER, CREATE) are strictly blocked.");
                sb.AppendLine("2. DO NOT reference any tables that are not listed above. They are treated as completely non-existent.");
                sb.AppendLine("3. If the requested information cannot be found in the allowed tables, inform the user politely that the data is not accessible.");
                sb.AppendLine("4. CRITICAL (PostgreSQL compatibility): All table and column names in the schema are case-sensitive. You MUST always wrap all table names and column names in double quotes in your SQL query (e.g., SELECT * FROM \"ScheduledNewsletters\" or SELECT \"Prompt\" FROM \"ScheduledNewsletters\").");

                return sb.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get database schema");
                return $"Error: Failed to retrieve database schema — {ex.Message}";
            }
        }

        // ── 2. EXECUTE QUERY ────────────────────────────────────────────────────
        private async Task<string> ExecuteQueryAsync(string argumentsJson)
        {
            var userId = McpContext.UserId;
            if (userId == null)
                return "Error: Could not determine current user context.";

            var (isOwner, isAdmin, allowedTables) = await GetUserPermissionsAsync(userId.Value);
            if (!isOwner && !isAdmin)
                return "Error: Access Denied. You do not have permission to query the database.";

            string sqlQuery;
            int pageSize = 10;

            try
            {
                using var doc = JsonDocument.Parse(argumentsJson);
                var root = doc.RootElement;
                sqlQuery = root.GetProperty("sqlQuery").GetString() ?? string.Empty;
                if (root.TryGetProperty("pageSize", out var psVal))
                {
                    pageSize = Math.Min(10, Math.Max(1, psVal.GetInt32()));
                }
            }
            catch (Exception ex)
            {
                return $"Error: Invalid arguments — {ex.Message}";
            }

            if (string.IsNullOrWhiteSpace(sqlQuery))
                return "Error: 'sqlQuery' is required.";

            // 1. Basic Read-Only check on query keywords
            var cleanSql = sqlQuery.Trim().TrimEnd(';');

            // Automatically double-quote unquoted known tables with correct casing (Postgres case-sensitivity)
            cleanSql = EnsureTableNamesQuoted(cleanSql, _dbContext);

            // Remove existing LIMIT and OFFSET clauses to prevent syntax errors on double-appending
            var limitRegex = new System.Text.RegularExpressions.Regex(@"\s+LIMIT\s+\d+(\s+OFFSET\s+\d+)?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            cleanSql = limitRegex.Replace(cleanSql, "");

            var sqlTokens = cleanSql.Split(new[] { ' ', '\t', '\n', '\r', ',', '.', '(', ')', ';', '"', '\'', '`' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim().ToLowerInvariant())
                .ToHashSet();

            var forbiddenKeywords = new[] { "insert", "update", "delete", "drop", "alter", "create", "replace", "truncate" };
            if (forbiddenKeywords.Any(k => sqlTokens.Contains(k)))
            {
                return "Error: Access Denied. Execution is restricted to read-only SELECT queries.";
            }

            // 2. Strict Whitelist validation for admins
            if (!isOwner)
            {
                if (!IsQueryWhitelisted(cleanSql, allowedTables, out string forbiddenTable))
                {
                    return $"Error: Access Denied. You are not authorized to query the table '{forbiddenTable}'.";
                }
            }

            try
            {
                // 3. Dynamic Telegram permission sync for group admins
                var allowedChats = new List<long>();
                if (!isOwner)
                {
                    allowedChats = await SyncAdminChatsAsync(userId.Value);
                }

                // 4. Manage query session for pagination
                await SaveQuerySessionAsync(userId.Value, cleanSql, pageSize);

                // 5. Wrap query in dynamic CTEs for multi-tenant isolation
                var secureSql = cleanSql;
                if (!isOwner)
                {
                    var allowedChatsStr = allowedChats.Any() ? string.Join(",", allowedChats) : "0";
                    var ctes = new List<string>();

                    if (cleanSql.Contains("AIBilingItem", StringComparison.OrdinalIgnoreCase))
                    {
                        ctes.Add($@"""AIBilingItem"" AS (
                            SELECT * FROM ""AIBilingItem"" 
                            WHERE ""TelegramChatInfoId"" IN ({allowedChatsStr})
                        )");
                    }
                    if (cleanSql.Contains("ScheduledNewsletters", StringComparison.OrdinalIgnoreCase))
                    {
                        ctes.Add($@"""ScheduledNewsletters"" AS (
                            SELECT * FROM ""ScheduledNewsletters"" 
                            WHERE ""ChatId"" IN ({allowedChatsStr})
                        )");
                    }
                    if (cleanSql.Contains("MessageReactions", StringComparison.OrdinalIgnoreCase))
                    {
                        ctes.Add($@"""MessageReactions"" AS (
                            SELECT * FROM ""MessageReactions"" 
                            WHERE ""ChatId"" IN ({allowedChatsStr})
                        )");
                    }
                    if (cleanSql.Contains("BalanceHistories", StringComparison.OrdinalIgnoreCase))
                    {
                        ctes.Add($@"""BalanceHistories"" AS (
                            SELECT * FROM ""BalanceHistories"" 
                            WHERE ""UserId"" IN (
                                SELECT ""TelegramUserInfoId"" FROM ""AIBilingItem"" WHERE ""TelegramChatInfoId"" IN ({allowedChatsStr})
                            )
                            AND ""UserId"" NOT IN (
                                SELECT ""TelegramUserInfoId"" FROM ""AIBilingItem"" WHERE ""TelegramChatInfoId"" NOT IN ({allowedChatsStr})
                            )
                        )");
                    }
                    if (cleanSql.Contains("TelegramUserInfos", StringComparison.OrdinalIgnoreCase))
                    {
                        ctes.Add($@"""TelegramUserInfos"" AS (
                            SELECT * FROM ""TelegramUserInfos"" 
                            WHERE ""Id"" IN (
                                SELECT ""TelegramUserInfoId"" FROM ""AIBilingItem"" WHERE ""TelegramChatInfoId"" IN ({allowedChatsStr})
                            )
                            AND ""Id"" NOT IN (
                                SELECT ""TelegramUserInfoId"" FROM ""AIBilingItem"" WHERE ""TelegramChatInfoId"" NOT IN ({allowedChatsStr})
                            )
                        )");
                    }

                    if (ctes.Any())
                    {
                        secureSql = "WITH " + string.Join(",\n", ctes) + "\n" + cleanSql;
                    }
                }

                // Append paged limits
                var finalSql = $"{secureSql} LIMIT {pageSize} OFFSET 0";

                _logger.LogInformation("Executing secure dynamic database query: {FinalSql} (Original query from AI: {OriginalSql})", finalSql, sqlQuery);

                // 6. Execute raw query using DbContext connection
                var dataTable = await ExecuteRawQueryAsync(finalSql);

                // 7. Format results into Markdown ASCII table
                var tableMarkdown = FormatDataTableToMarkdown(dataTable);
                if (tableMarkdown.Length > 4096)
                {
                    return "Warning: Result size exceeds Telegram's 4096 character limit. " +
                           "Please refine your query to request fewer columns or specify a smaller pageSize.";
                }

                // 8. Dynamic inline instructions for pagination buttons
                var instructions = "";
                var hasMore = dataTable.Rows.Count == pageSize;
                if (hasMore || pageSize > 0)
                {
                    var activeSession = await _dbContext.DbQuerySessions
                        .OrderByDescending(s => s.Id)
                        .FirstOrDefaultAsync(s => s.AdminUserId == userId.Value);

                    if (activeSession != null)
                    {
                        instructions = $"\n\n[PAGINATION_METADATA:session={activeSession.Id}:page=1:hasMore={hasMore.ToString().ToLower()}]";
                    }
                }

                // 9. Run combined session cleanup
                await CleanupOldSessionsAsync();

                return tableMarkdown + instructions;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute database query: {Query}", sqlQuery);
                return $"Error: Query execution failed — {ex.Message}";
            }
        }

        // ── 3. DATA ACCESS & PERMISSIONS HELPERS ─────────────────────────────────
        private async Task<(bool IsOwner, bool IsAdmin, List<string> AllowedTables)> GetUserPermissionsAsync(long userId)
        {
            var allowedTables = new List<string>();

            // Check if Owner
            var isOwner = userId == _appSettings.TelegramBotConfiguration.OwnerId;
            if (isOwner)
            {
                return (true, false, allowedTables);
            }

            // Check Roles from DbQueryUserRoles
            var userRoles = await _dbContext.DbQueryUserRoles
                .AsNoTracking()
                .Where(ur => ur.UserId == userId)
                .Select(ur => ur.RoleId)
                .ToListAsync();

            if (!userRoles.Any())
            {
                return (false, false, allowedTables);
            }

            var dbRoles = await _dbContext.DbQueryRoles
                .AsNoTracking()
                .Where(r => userRoles.Contains(r.Id))
                .ToListAsync();

            if (dbRoles.Any(r => r.Name == "bot_owner"))
            {
                return (true, false, allowedTables);
            }

            var isAdmin = dbRoles.Any(r => r.Name == "bot_admin");
            if (isAdmin)
            {
                var adminRoleId = dbRoles.First(r => r.Name == "bot_admin").Id;
                allowedTables = await _dbContext.DbQueryPermissions
                    .AsNoTracking()
                    .Where(p => p.RoleId == adminRoleId)
                    .Select(p => p.TableName)
                    .ToListAsync();
            }

            return (false, isAdmin, allowedTables);
        }

        private async Task<List<long>> SyncAdminChatsAsync(long adminUserId)
        {
            var allowedChats = new List<long>();
            try
            {
                var knownChats = await _dbContext.TelegramChatInfos
                    .AsNoTracking()
                    .Select(c => c.Id)
                    .ToListAsync();

                var oldMappings = await _dbContext.DbQueryAdminChatMappings
                    .Where(m => m.AdminUserId == adminUserId)
                    .ToListAsync();
                _dbContext.DbQueryAdminChatMappings.RemoveRange(oldMappings);

                foreach (var chatId in knownChats)
                {
                    try
                    {
                        var member = await _botClient.GetChatMember(chatId, adminUserId);
                        if (member.Status == ChatMemberStatus.Creator ||
                            member.Status == ChatMemberStatus.Administrator)
                        {
                            allowedChats.Add(chatId);
                            _dbContext.DbQueryAdminChatMappings.Add(new DbQueryAdminChatMapping
                            {
                                AdminUserId = adminUserId,
                                ChatId = chatId
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("Failed to dynamically check chat member status for chat {ChatId} and user {UserId}: {Msg}", chatId, adminUserId, ex.Message);
                    }
                }
                await _dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing admin chats for user {UserId}", adminUserId);
            }
            return allowedChats;
        }

        private bool IsQueryWhitelisted(string sql, List<string> whitelist, out string forbiddenTable)
        {
            forbiddenTable = string.Empty;
            var allTableNames = _dbContext.Model.GetEntityTypes()
                .Select(e => e.GetTableName())
                .Where(t => !string.IsNullOrEmpty(t))
                .ToList();

            var sqlTokens = sql.Split(new[] { ' ', '\t', '\n', '\r', ',', '.', '(', ')', ';', '"', '\'', '`' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim().ToLowerInvariant())
                .ToHashSet();

            foreach (var table in allTableNames)
            {
                // Account for both singular and plural forms when matching sql tokens
                var lowerTable = table.ToLowerInvariant();
                var lowerSingular = lowerTable.EndsWith("ies") 
                    ? lowerTable.Substring(0, lowerTable.Length - 3) + "y" 
                    : (lowerTable.EndsWith("s") && !lowerTable.EndsWith("ss") ? lowerTable.Substring(0, lowerTable.Length - 1) : lowerTable);

                if (sqlTokens.Contains(lowerTable) || sqlTokens.Contains(lowerSingular))
                {
                    var isWhitelisted = whitelist.Any(w => TableNamesMatch(w, table));
                    if (!isWhitelisted)
                    {
                        forbiddenTable = table;
                        return false;
                    }
                }
            }
            return true;
        }

        private async Task SaveQuerySessionAsync(long adminUserId, string sqlQuery, int pageSize)
        {
            try
            {
                var oldSessions = await _dbContext.DbQuerySessions
                    .Where(s => s.AdminUserId == adminUserId)
                    .ToListAsync();
                _dbContext.DbQuerySessions.RemoveRange(oldSessions);

                var newSession = new DbQuerySession
                {
                    AdminUserId = adminUserId,
                    SqlQuery = sqlQuery,
                    PageSize = pageSize,
                    CreatedAt = DateTime.UtcNow
                };
                _dbContext.DbQuerySessions.Add(newSession);
                await _dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save query session for user {UserId}", adminUserId);
            }
        }

        private async Task<DataTable> ExecuteRawQueryAsync(string sql)
        {
            var dataTable = new DataTable();
            var connection = _dbContext.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync();
            }

            using var command = connection.CreateCommand();
            command.CommandText = sql;
            using var reader = await command.ExecuteReaderAsync();
            dataTable.Load(reader);
            return dataTable;
        }

        private string FormatDataTableToMarkdown(DataTable table)
        {
            if (table == null || table.Rows.Count == 0)
            {
                return "*No rows returned.*";
            }

            var sb = new StringBuilder();
            var columns = table.Columns.Cast<DataColumn>().ToList();
            
            // Calculate column widths
            var widths = new Dictionary<string, int>();
            foreach (var col in columns)
            {
                var maxLen = col.ColumnName.Length;
                foreach (DataRow row in table.Rows)
                {
                    var valStr = row[col]?.ToString() ?? "";
                    if (valStr.Length > maxLen) maxLen = valStr.Length;
                }
                widths[col.ColumnName] = Math.Min(30, maxLen); // Caps width at 30 chars for readability
            }

            sb.AppendLine("```text");

            // Render Header
            var header = string.Join(" | ", columns.Select(c => c.ColumnName.PadRight(widths[c.ColumnName]).Substring(0, widths[c.ColumnName])));
            sb.AppendLine(header);

            // Render Divider
            var divider = string.Join("-|-", columns.Select(c => new string('-', widths[c.ColumnName])));
            sb.AppendLine(divider);

            // Render Rows
            foreach (DataRow row in table.Rows)
            {
                var rowStr = string.Join(" | ", columns.Select(c => (row[c]?.ToString() ?? "").PadRight(widths[c.ColumnName]).Substring(0, widths[c.ColumnName])));
                sb.AppendLine(rowStr);
            }

            sb.AppendLine("```");
            return sb.ToString();
        }

        private async Task CleanupOldSessionsAsync()
        {
            try
            {
                var oldThreshold = DateTime.UtcNow.AddHours(-1);
                var expiredSessions = await _dbContext.DbQuerySessions
                    .Where(s => s.CreatedAt < oldThreshold)
                    .ToListAsync();

                if (expiredSessions.Any())
                {
                    _dbContext.DbQuerySessions.RemoveRange(expiredSessions);
                    await _dbContext.SaveChangesAsync();
                    _logger.LogInformation("Cleaned up {Count} expired query sessions.", expiredSessions.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clean up old database query sessions");
            }
        }

        public static bool TableNamesMatch(string name1, string name2)
        {
            if (string.Equals(name1, name2, StringComparison.OrdinalIgnoreCase))
                return true;

            string Normalize(string name)
            {
                var lower = name.ToLowerInvariant();
                if (lower.EndsWith("ies"))
                    return lower.Substring(0, lower.Length - 3) + "y";
                if (lower.EndsWith("s") && !lower.EndsWith("ss"))
                    return lower.Substring(0, lower.Length - 1);
                return lower;
            }

            return Normalize(name1) == Normalize(name2);
        }

        public static string EnsureTableNamesQuoted(string sql, StoreContext dbContext)
        {
            var knownTables = dbContext.Model.GetEntityTypes()
                .Select(e => e.GetTableName())
                .Where(t => !string.IsNullOrEmpty(t))
                .ToList();

            var result = sql;
            foreach (var table in knownTables)
            {
                var patterns = new List<string> { System.Text.RegularExpressions.Regex.Escape(table) };
                var lower = table.ToLowerInvariant();
                if (lower.EndsWith("ies"))
                {
                    patterns.Add(System.Text.RegularExpressions.Regex.Escape(table.Substring(0, table.Length - 3) + "y"));
                }
                else if (lower.EndsWith("s") && !lower.EndsWith("ss"))
                {
                    patterns.Add(System.Text.RegularExpressions.Regex.Escape(table.Substring(0, table.Length - 1)));
                }

                foreach (var pat in patterns)
                {
                    var pattern = @"(?<!"")\b" + pat + @"\b(?!"")";
                    result = System.Text.RegularExpressions.Regex.Replace(
                        result, 
                        pattern, 
                        $@"""{table}""", 
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase
                    );
                }
            }
            return result;
        }
    }
}
