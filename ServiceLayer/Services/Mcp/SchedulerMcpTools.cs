using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using DataBaseLayer.Models;
using DataBaseLayer.Repositories;
using Microsoft.Extensions.Logging;
using ServiceLayer.Services.Telegram;

namespace ServiceLayer.Services.Mcp
{
    /// <summary>
    /// Native MCP tools for AI-driven management of scheduled newsletters.
    /// ChatId and UserId are resolved implicitly from McpContext (AsyncLocal),
    /// so the AI cannot spoof them via tool arguments.
    /// </summary>
    public class SchedulerMcpTools : INativeMcpTool
    {
        // Each tool is exposed as a separate INativeMcpTool instance via a wrapper.
        // This class acts as a factory that produces all three tool descriptors.
        // We keep one class but use a discriminator to handle multiple tools.

        private readonly IRepository<ScheduledNewsletter> _newsletterRepository;
        private readonly NewsletterSchedulerService _schedulerService;
        private readonly ILogger<SchedulerMcpTools> _logger;
        private readonly string _toolName;

        // ── Tool names ──────────────────────────────────────────────────────────
        public const string ToolCreate = "create_scheduled_newsletter";
        public const string ToolList   = "list_scheduled_newsletters";
        public const string ToolDelete = "delete_scheduled_newsletter";

        // ── INativeMcpTool implementation ───────────────────────────────────────
        public string Name => _toolName;
        public string ServerName => "native-scheduler";


        public string Description => _toolName switch
        {
            ToolCreate => "Creates and schedules a recurring newsletter for the current chat. " +
                          "The AI should convert natural-language time expressions (e.g. 'every morning at 9') " +
                          "into a standard cron expression before calling this tool.",
            ToolList   => "Returns a list of all active scheduled newsletters for the current chat.",
            ToolDelete => "Deletes a scheduled newsletter by its ID. " +
                          "Use list_scheduled_newsletters first to get the ID.",
            _          => string.Empty
        };

        public string JsonSchema => _toolName switch
        {
            ToolCreate => """
                {
                  "type": "object",
                  "properties": {
                    "prompt": {
                      "type": "string",
                      "description": "Topic or instruction for generating the newsletter content, e.g. 'C# news digest'."
                    },
                    "cronExpression": {
                      "type": "string",
                      "description": "Standard 5-field cron expression, e.g. '0 9 * * *' for every day at 09:00 UTC."
                    },
                    "friendlyScheduleName": {
                      "type": "string",
                      "description": "Human-readable description of the schedule, e.g. 'Every morning at 9:00'. Optional."
                    }
                  },
                  "required": ["prompt", "cronExpression"]
                }
                """,
            ToolList   => """
                {
                  "type": "object",
                  "properties": {}
                }
                """,
            ToolDelete => """
                {
                  "type": "object",
                  "properties": {
                    "newsletterId": {
                      "type": "integer",
                      "description": "The ID of the newsletter to delete."
                    }
                  },
                  "required": ["newsletterId"]
                }
                """,
            _ => "{\"type\":\"object\",\"properties\":{}}"
        };

        public async Task<string> ExecuteAsync(string argumentsJson)
        {
            return _toolName switch
            {
                ToolCreate => await CreateNewsletterAsync(argumentsJson),
                ToolList   => await ListNewslettersAsync(),
                ToolDelete => await DeleteNewsletterAsync(argumentsJson),
                _          => $"Error: Unknown tool '{_toolName}'."
            };
        }

        // ── Constructor ─────────────────────────────────────────────────────────
        public SchedulerMcpTools(
            string toolName,
            IRepository<ScheduledNewsletter> newsletterRepository,
            NewsletterSchedulerService schedulerService,
            ILogger<SchedulerMcpTools> logger)
        {
            _toolName             = toolName;
            _newsletterRepository = newsletterRepository;
            _schedulerService     = schedulerService;
            _logger               = logger;
        }

        // ── Tool implementations ────────────────────────────────────────────────

        private async Task<string> CreateNewsletterAsync(string argumentsJson)
        {
            var chatId = McpContext.ChatId;
            var userId = McpContext.UserId;

            if (chatId == null || userId == null)
                return "Error: Could not determine the current chat or user context. Please try again.";

            string prompt, cronExpression, friendlyName;
            try
            {
                using var doc = JsonDocument.Parse(argumentsJson);
                var root = doc.RootElement;
                prompt         = root.GetProperty("prompt").GetString() ?? string.Empty;
                cronExpression = root.GetProperty("cronExpression").GetString() ?? string.Empty;
                friendlyName   = root.TryGetProperty("friendlyScheduleName", out var fn)
                    ? fn.GetString() ?? string.Empty
                    : string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse create_scheduled_newsletter arguments: {Args}", argumentsJson);
                return $"Error: Invalid arguments — {ex.Message}";
            }

            if (string.IsNullOrWhiteSpace(prompt))
                return "Error: 'prompt' is required.";
            if (string.IsNullOrWhiteSpace(cronExpression))
                return "Error: 'cronExpression' is required.";

            var newsletter = new ScheduledNewsletter
            {
                ChatId         = chatId.Value,
                UserId         = userId.Value,
                Prompt         = prompt,
                CronExpression = cronExpression,
                CreatedAt      = DateTime.UtcNow,
                IsActive       = true
            };

            _newsletterRepository.Add(newsletter);
            await _newsletterRepository.SaveChanges();

            _schedulerService.ScheduleJob(newsletter);

            _logger.LogInformation(
                "Newsletter #{Id} created for ChatId={ChatId} via MCP. Cron='{Cron}', Prompt='{Prompt}'",
                newsletter.Id, chatId, cronExpression, prompt);

            var humanSchedule = string.IsNullOrEmpty(friendlyName) ? cronExpression : friendlyName;
            return JsonSerializer.Serialize(new
            {
                success     = true,
                id          = newsletter.Id,
                message     = $"Newsletter created successfully. ID: {newsletter.Id}. Schedule: {humanSchedule}."
            });
        }

        private Task<string> ListNewslettersAsync()
        {
            var chatId = McpContext.ChatId;
            if (chatId == null)
                return Task.FromResult("Error: Could not determine the current chat context.");

            var newsletters = _newsletterRepository
                .GetAll()
                .Where(n => n.ChatId == chatId.Value && n.IsActive)
                .OrderBy(n => n.Id)
                .ToList();

            if (!newsletters.Any())
                return Task.FromResult(JsonSerializer.Serialize(new
                {
                    success     = true,
                    count       = 0,
                    newsletters = Array.Empty<object>(),
                    message     = "No active scheduled newsletters for this chat."
                }));

            var items = newsletters.Select(n => new
            {
                id             = n.Id,
                prompt         = n.Prompt,
                cronExpression = n.CronExpression,
                createdAt      = n.CreatedAt.ToString("yyyy-MM-dd HH:mm") + " UTC"
            }).ToArray();

            return Task.FromResult(JsonSerializer.Serialize(new
            {
                success     = true,
                count       = items.Length,
                newsletters = items
            }));
        }

        private async Task<string> DeleteNewsletterAsync(string argumentsJson)
        {
            var chatId = McpContext.ChatId;
            if (chatId == null)
                return "Error: Could not determine the current chat context.";

            int newsletterId;
            try
            {
                using var doc = JsonDocument.Parse(argumentsJson);
                newsletterId = doc.RootElement.GetProperty("newsletterId").GetInt32();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse delete_scheduled_newsletter arguments: {Args}", argumentsJson);
                return $"Error: Invalid arguments — {ex.Message}";
            }

            var newsletter = _newsletterRepository.GetById(newsletterId);
            if (newsletter == null)
                return $"Error: Newsletter with ID {newsletterId} not found.";

            // Security: ensure the newsletter belongs to this chat
            if (newsletter.ChatId != chatId.Value)
                return "Error: You do not have permission to delete this newsletter.";

            _schedulerService.UnscheduleJob(newsletterId);
            newsletter.IsActive = false;
            _newsletterRepository.Update(newsletter);
            await _newsletterRepository.SaveChanges();

            _logger.LogInformation(
                "Newsletter #{Id} deleted for ChatId={ChatId} via MCP.",
                newsletterId, chatId);

            return JsonSerializer.Serialize(new
            {
                success = true,
                message = $"Newsletter #{newsletterId} has been successfully deleted."
            });
        }
    }
}
