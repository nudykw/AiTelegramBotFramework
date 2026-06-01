using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using DataBaseLayer.Models;
using DataBaseLayer.Repositories;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace ServiceLayer.Services.Mcp
{
    public class TelegramContextMcpTools : INativeMcpTool
    {
        private readonly string _toolName;
        private readonly ITelegramBotClient _botClient;
        private readonly TgUserRepo _userInfoRepository;
        private readonly TgChatRepo _chatInfoRepository;
        private readonly IMemoryCache _memoryCache;
        private readonly ILogger<TelegramContextMcpTools> _logger;

        public const string ToolGetCurrentUserInfo = "get_current_user_info";
        public const string ToolGetCurrentGroupInfo = "get_current_group_info";
        public const string ToolGetUserContact = "get_user_contact";
        public const string ToolGetUserLocation = "get_user_location";
        public const string ToolGetAvailableInformationDirectory = "get_available_information_directory";

        public string Name => _toolName;
        public string ServerName => "native-telegram";


        public string Description => _toolName switch
        {
            ToolGetCurrentUserInfo => "Retrieves silent, basic information about the current Telegram user invoking this AI query, " +
                                     "such as user ID, name, username, language, and current billing balance. " +
                                     "This tool runs silently and does NOT require user permission.",
            ToolGetCurrentGroupInfo => "Retrieves silent, basic information about the current Telegram chat/group in which " +
                                      "this conversation is happening, such as group ID, title, type, active admins, member count, " +
                                      "and the current user's role in the group. This tool runs silently and does NOT require user permission.",
            ToolGetUserContact => "IMPORTANT: This tool requires prior user confirmation. Returns the user's phone number if they " +
                                  "have explicitly shared their contact. If this tool returns that the contact is not shared, " +
                                  "you MUST politely explain to the user why you need it and ask them to click the 'Share Contact' " +
                                  "button in the bot interface to provide it. Stored temporarily only.",
            ToolGetUserLocation => "IMPORTANT: This tool requires prior user confirmation. Returns the user's GPS coordinates " +
                                   "(latitude and longitude) if they have explicitly shared their location. If this tool returns " +
                                   "that the location is not shared, you MUST politely explain to the user why you need it and " +
                                   "ask them to click the 'Share Location' button in the bot interface to provide it. Stored temporarily only.",
            ToolGetAvailableInformationDirectory => "Returns a directory listing all available user and group context tools, " +
                                                   "explaining which ones are silent and which ones require prior user confirmation, " +
                                                   "as well as how to trigger confirmation for contact and location sharing.",
            _ => string.Empty
        };

        public string JsonSchema => """
            {
              "type": "object",
              "properties": {}
            }
            """;

        public TelegramContextMcpTools(
            string toolName,
            ITelegramBotClient botClient,
            TgUserRepo userInfoRepository,
            TgChatRepo chatInfoRepository,
            IMemoryCache memoryCache,
            ILogger<TelegramContextMcpTools> logger)
        {
            _toolName = toolName;
            _botClient = botClient;
            _userInfoRepository = userInfoRepository;
            _chatInfoRepository = chatInfoRepository;
            _memoryCache = memoryCache;
            _logger = logger;
        }

        public async Task<string> ExecuteAsync(string argumentsJson)
        {
            var userId = McpContext.UserId;
            var chatId = McpContext.ChatId;

            _logger.LogInformation("Executing context tool '{ToolName}' for UserId={UserId}, ChatId={ChatId}", _toolName, userId, chatId);

            if (userId == null || chatId == null)
            {
                return JsonSerializer.Serialize(new
                {
                    error = "Could not determine current context.",
                    details = "UserId and ChatId must be resolved from current message context."
                });
            }

            try
            {
                return _toolName switch
                {
                    ToolGetCurrentUserInfo => await GetCurrentUserInfoAsync(userId.Value),
                    ToolGetCurrentGroupInfo => await GetCurrentGroupInfoAsync(chatId.Value, userId.Value),
                    ToolGetUserContact => await GetUserContactAsync(userId.Value),
                    ToolGetUserLocation => await GetUserLocationAsync(userId.Value),
                    ToolGetAvailableInformationDirectory => GetAvailableInformationDirectory(),
                    _ => $"Error: Unknown tool '{_toolName}'."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error running tool '{ToolName}'", _toolName);
                return JsonSerializer.Serialize(new
                {
                    error = $"Failed to execute {_toolName}",
                    details = ex.Message
                });
            }
        }

        private async Task<string> GetCurrentUserInfoAsync(long userId)
        {
            var dbUser = await _userInfoRepository.Get(u => u.Id == userId);
            
            var result = new
            {
                userId = userId,
                firstName = dbUser?.FirstName ?? string.Empty,
                lastName = dbUser?.LastName,
                username = dbUser?.Username,
                isPremium = dbUser?.IsPremium ?? false,
                languageCode = dbUser?.LanguageCode ?? "en",
                preferredProvider = dbUser?.PreferredProvider.ToString() ?? "Auto",
                selectedVoice = dbUser?.VoiceId ?? "alloy",
                selectedModel = dbUser?.SelectedModel,
                balance = dbUser?.Balance ?? 0.0m,
                lastAiInteraction = dbUser?.LastAiInteraction
            };

            return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        }

        private async Task<string> GetCurrentGroupInfoAsync(long chatId, long userId)
        {
            var dbChat = await _chatInfoRepository.Get(c => c.Id == chatId);
            
            string? groupTitle = dbChat?.Title;
            string? groupDescription = dbChat?.Description;
            string? groupInviteLink = dbChat?.InviteLink;
            string? groupUsername = dbChat?.Username;
            string chatType = dbChat?.ChatType ?? "private";
            int? memberCount = null;
            string? currentUserRole = null;
            object[]? administrators = null;

            // Attempt to enrich with live data if possible (e.g. if bot has permission/is in the group)
            if (chatType != "Private" && chatType != "private")
            {
                try
                {
                    var liveChat = await _botClient.GetChat(chatId);
                    groupTitle = liveChat.Title ?? groupTitle;
                    groupDescription = liveChat.Description ?? groupDescription;
                    groupInviteLink = liveChat.InviteLink ?? groupInviteLink;
                    groupUsername = liveChat.Username ?? groupUsername;
                    chatType = liveChat.Type.ToString();

                    memberCount = await _botClient.GetChatMemberCount(chatId);
                    
                    var liveMember = await _botClient.GetChatMember(chatId, userId);
                    currentUserRole = liveMember.Status.ToString();

                    var liveAdmins = await _botClient.GetChatAdministrators(chatId);
                    administrators = liveAdmins.Select(a => new
                    {
                        userId = a.User.Id,
                        username = a.User.Username,
                        firstName = a.User.FirstName,
                        lastName = a.User.LastName,
                        status = a.Status.ToString(),
                        customTitle = (a as ChatMemberAdministrator)?.CustomTitle
                    }).ToArray();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not enrich chat info from live Telegram API for ChatId={ChatId}", chatId);
                }
            }
            else
            {
                currentUserRole = "member";
            }

            var result = new
            {
                chatId = chatId,
                type = chatType,
                title = groupTitle,
                description = groupDescription,
                username = groupUsername,
                inviteLink = groupInviteLink,
                memberCount = memberCount,
                currentUserRole = currentUserRole,
                administrators = administrators
            };

            return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        }

        private Task<string> GetUserContactAsync(long userId)
        {
            var cacheKey = $"UserContact:{userId}";
            if (_memoryCache.TryGetValue(cacheKey, out string? cachedJson) && !string.IsNullOrEmpty(cachedJson))
            {
                var cachedObj = JsonSerializer.Deserialize<JsonElement>(cachedJson);
                var successResult = new
                {
                    status = "shared",
                    contact = cachedObj
                };
                return Task.FromResult(JsonSerializer.Serialize(successResult, new JsonSerializerOptions { WriteIndented = true }));
            }

            var missingResult = new
            {
                status = "not_shared",
                message = "The user has not shared their contact (phone number) yet. " +
                          "You MUST politely explain to the user why you need their phone number and ask them " +
                          "to click the 'Share Contact' button in their interface. You can instruct them " +
                          "to send '/request' or '/keyboard' to bring up the button panel if they do not see it. " +
                          "Explain that this info is stored ONLY temporarily in memory and is NOT saved in the database."
            };

            return Task.FromResult(JsonSerializer.Serialize(missingResult, new JsonSerializerOptions { WriteIndented = true }));
        }

        private Task<string> GetUserLocationAsync(long userId)
        {
            var cacheKey = $"UserLocation:{userId}";
            if (_memoryCache.TryGetValue(cacheKey, out string? cachedJson) && !string.IsNullOrEmpty(cachedJson))
            {
                var cachedObj = JsonSerializer.Deserialize<JsonElement>(cachedJson);
                var successResult = new
                {
                    status = "shared",
                    location = cachedObj
                };
                return Task.FromResult(JsonSerializer.Serialize(successResult, new JsonSerializerOptions { WriteIndented = true }));
            }

            var missingResult = new
            {
                status = "not_shared",
                message = "The user has not shared their location (GPS coordinates) yet. " +
                          "You MUST politely explain to the user why you need their location (e.g. for weather, delivery, local info) " +
                          "and ask them to click the 'Share Location' button in their interface. You can instruct them " +
                          "to send '/request' or '/keyboard' to bring up the button panel if they do not see it. " +
                          "Explain that this info is stored ONLY temporarily in memory and is NOT saved in the database."
            };

            return Task.FromResult(JsonSerializer.Serialize(missingResult, new JsonSerializerOptions { WriteIndented = true }));
        }

        private string GetAvailableInformationDirectory()
        {
            var directory = new
            {
                silentTools = new[]
                {
                    new
                    {
                        name = ToolGetCurrentUserInfo,
                        description = "Returns basic user ID, name, username, premium status, current balance, and preferred AI settings.",
                        permissionRequired = false
                    },
                    new
                    {
                        name = ToolGetCurrentGroupInfo,
                        description = "Returns group ID, type, title, description, active administrators, member count, and user's role in this chat.",
                        permissionRequired = false
                    }
                },
                confirmedTools = new[]
                {
                    new
                    {
                        name = ToolGetUserContact,
                        description = "Returns the user's phone number if shared. Otherwise prompts the AI to explain to the user and request it.",
                        permissionRequired = true,
                        storage = "Temporary in-memory (RAM) sliding cache only. Not written to database."
                    },
                    new
                    {
                        name = ToolGetUserLocation,
                        description = "Returns the user's GPS coordinates if shared. Otherwise prompts the AI to explain to the user and request it.",
                        permissionRequired = true,
                        storage = "Temporary in-memory (RAM) sliding cache only. Not written to database."
                    }
                },
                howToRequestConfirmedAccess = "If a confirmed tool returns 'not_shared', the AI should politely explain to the user " +
                                              "why the requested action requires this information (e.g. phone number for booking, location for routing) " +
                                              "and ask them to click the corresponding button in the bot's custom keyboard. " +
                                              "If the keyboard is missing, tell the user to type '/request' or '/keyboard' to show the buttons."
            };

            return JsonSerializer.Serialize(directory, new JsonSerializerOptions { WriteIndented = true });
        }
    }
}
