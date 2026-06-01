using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataBaseLayer.Models;
using DataBaseLayer.Repositories;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using OpenAI;

namespace ServiceLayer.Services.Telegram
{
    public class NewsletterSchedulerService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<NewsletterSchedulerService> _logger;
        private readonly AppSettings _appSettings;

        public NewsletterSchedulerService(
            IServiceProvider serviceProvider,
            ILogger<NewsletterSchedulerService> logger,
            AppSettings appSettings)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _appSettings = appSettings;
        }

        /// <summary>
        /// Registers or updates a newsletter job in Hangfire.
        /// </summary>
        public void ScheduleJob(ScheduledNewsletter newsletter)
        {
            if (_appSettings.Scheduler?.Enabled != true) return;

            try
            {
                var jobId = GetJobId(newsletter.Id);
                _logger.LogInformation("Scheduling Hangfire job {JobId} with cron '{Cron}'", jobId, newsletter.CronExpression);

                var recurringJobManager = _serviceProvider.GetService<IRecurringJobManager>();
                if (recurringJobManager != null)
                {
                    recurringJobManager.AddOrUpdate<NewsletterSchedulerService>(
                        jobId,
                        service => service.ExecuteNewsletterAsync(newsletter.Id),
                        newsletter.CronExpression);
                }
                else
                {
                    RecurringJob.AddOrUpdate<NewsletterSchedulerService>(
                        jobId,
                        service => service.ExecuteNewsletterAsync(newsletter.Id),
                        newsletter.CronExpression);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to schedule Hangfire job for newsletter {NewsletterId}", newsletter.Id);
            }
        }

        /// <summary>
        /// Removes a newsletter job from Hangfire.
        /// </summary>
        public void UnscheduleJob(int newsletterId)
        {
            if (_appSettings.Scheduler?.Enabled != true) return;

            try
            {
                var jobId = GetJobId(newsletterId);
                _logger.LogInformation("Removing Hangfire job {JobId}", jobId);

                var recurringJobManager = _serviceProvider.GetService<IRecurringJobManager>();
                if (recurringJobManager != null)
                {
                    recurringJobManager.RemoveIfExists(jobId);
                }
                else
                {
                    RecurringJob.RemoveIfExists(jobId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to remove Hangfire job for newsletter {NewsletterId}", newsletterId);
            }
        }

        /// <summary>
        /// Syncs all active newsletters from DB to Hangfire on startup.
        /// </summary>
        public async Task InitializeJobsAsync()
        {
            if (_appSettings.Scheduler?.Enabled != true) return;

            _logger.LogInformation("Initializing all active scheduled newsletters in Hangfire...");
            using var scope = _serviceProvider.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IRepository<ScheduledNewsletter>>();
            
            var activeNewsletters = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(repository.GetAll().Where(n => n.IsActive));
            _logger.LogInformation("Found {Count} active newsletters to schedule.", activeNewsletters.Count);

            foreach (var newsletter in activeNewsletters)
            {
                ScheduleJob(newsletter);
            }
        }

        /// <summary>
        /// Trigger a job immediately (mghovenno).
        /// </summary>
        public void TriggerJobImmediately(int newsletterId)
        {
            if (_appSettings.Scheduler?.Enabled != true) return;

            try
            {
                var jobId = GetJobId(newsletterId);
                _logger.LogInformation("Triggering Hangfire job {JobId} immediately", jobId);
                RecurringJob.TriggerJob(jobId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to trigger Hangfire job immediately for newsletter {NewsletterId}", newsletterId);
            }
        }

        /// <summary>
        /// The background method executed by Hangfire.
        /// </summary>
        [Queue("default")]
        public async Task ExecuteNewsletterAsync(int newsletterId)
        {
            _logger.LogInformation("Executing scheduled newsletter {NewsletterId}", newsletterId);
            
            using var scope = _serviceProvider.CreateScope();
            var newsletterRepository = scope.ServiceProvider.GetRequiredService<IRepository<ScheduledNewsletter>>();
            var chatService = scope.ServiceProvider.GetRequiredService<IChatService>();
            var botClient = scope.ServiceProvider.GetRequiredService<ITelegramBotClient>();

            var newsletter = await newsletterRepository.Get(n => n.Id == newsletterId);
            if (newsletter == null)
            {
                _logger.LogWarning("Newsletter {NewsletterId} not found in DB. Skipping execution.", newsletterId);
                return;
            }

            if (!newsletter.IsActive)
            {
                _logger.LogInformation("Newsletter {NewsletterId} is not active. Skipping execution.", newsletterId);
                return;
            }

            try
            {
                _logger.LogInformation("Generating newsletter content for user {UserId} with prompt: {Prompt}", newsletter.UserId, newsletter.Prompt);
                
                // Show typing indicator in the target chat
                await botClient.SendChatAction(newsletter.ChatId, ChatAction.Typing);

                // Ask the AI to write the newsletter
                var messages = new List<AiMessage>
                {
                    new AiMessage(Role.System, "You are a professional background scheduled briefing assistant. Generate only the requested content (e.g., weather forecast, news summary, etc.) based on the user's prompt. Do NOT talk about regular delivery, subscriptions, or explain that you cannot send scheduled messages. Write the content itself as if you are sending it right now."),
                    new AiMessage(Role.User, newsletter.Prompt)
                };

                var response = await chatService.SendMessages2ChatAsync(newsletter.ChatId, newsletter.UserId, messages);
                var content = string.Join("\n", response.Choices);

                if (string.IsNullOrWhiteSpace(content))
                {
                    _logger.LogWarning("Generated content for newsletter {NewsletterId} was empty. Skipping message.", newsletterId);
                    return;
                }

                _logger.LogInformation("Sending newsletter {NewsletterId} to chat {ChatId}", newsletterId, newsletter.ChatId);

                // Send the generated content to the user
                await botClient.SendMessage(
                    chatId: newsletter.ChatId,
                    text: content,
                    parseMode: ParseMode.Markdown
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing newsletter {NewsletterId}", newsletterId);
            }
        }

        private static string GetJobId(int id) => $"newsletter_{id}";
    }
}
