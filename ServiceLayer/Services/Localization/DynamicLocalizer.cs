using System.Globalization;
using DataBaseLayer.Models;
using DataBaseLayer.Repositories;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using ServiceLayer.Resources;
using ServiceLayer.Services;
using ServiceLayer.Utils;
using ServiceLayer.Constans;

namespace ServiceLayer.Services.Localization
{
    public class DynamicLocalizer : IDynamicLocalizer
    {
        private readonly IStringLocalizer<BotMessages> _localizer;
        private readonly IRepository<CachedTranslation> _cacheRepository;
        private readonly IRepository<AIBilingItem> _aiBilingItemRepository;
        private readonly IServiceProvider _serviceProvider;
        private readonly IUserContext _userContext;
        private readonly ILogger<DynamicLocalizer> _logger;

        private static readonly HashSet<string> NativeLanguages = new() { LanguageCode.English, LanguageCode.Ukrainian };

        public DynamicLocalizer(
            IStringLocalizer<BotMessages> localizer,
            IStringLocalizerFactory localizerFactory,
            IRepository<CachedTranslation> cacheRepository,
            IRepository<AIBilingItem> aiBilingItemRepository,
            IServiceProvider serviceProvider,
            IUserContext userContext,
            ILogger<DynamicLocalizer> logger)
        {
            _localizer = localizer;
            _cacheRepository = cacheRepository;
            _aiBilingItemRepository = aiBilingItemRepository;
            _serviceProvider = serviceProvider;
            _userContext = userContext;
            _logger = logger;
        }

        public string this[string key, params object[] arguments] => GetString(key, arguments);

        public string GetString(string key, params object[] arguments)
        {
            var culture = CultureInfo.CurrentUICulture;
            var languageCode = culture.TwoLetterISOLanguageName;

            // 1. If native language, use standard localization
            if (NativeLanguages.Contains(languageCode))
            {
                var localized = _localizer[key];
                if (arguments == null || arguments.Length == 0) return localized.Value;
                return string.Format(localized.Value, arguments);
            }

            // 2. Get English text as source
            var englishValue = GetEnglishText(key);

            // 3. Check cache
            var cached = _cacheRepository.GetAll()
                .FirstOrDefault(p => p.LanguageCode == languageCode && p.ResourceKey == key);

            if (cached != null && cached.OriginalText == englishValue)
            {
                return string.Format(cached.TranslatedText, arguments);
            }

            // 4. Translate via AI
            try
            {
                var translatedText = TranslateWithAi(englishValue, culture.DisplayName, languageCode).GetAwaiter().GetResult();
                
                if (cached == null)
                {
                    cached = new CachedTranslation
                    {
                        LanguageCode = languageCode,
                        ResourceKey = key,
                        OriginalText = englishValue,
                        TranslatedText = translatedText,
                        UpdatedAt = DateTime.UtcNow
                    };
                    _cacheRepository.Add(cached);
                }
                else
                {
                    cached.OriginalText = englishValue;
                    cached.TranslatedText = translatedText;
                    cached.UpdatedAt = DateTime.UtcNow;
                    _cacheRepository.Update(cached);
                }
                
                _cacheRepository.SaveChanges().GetAwaiter().GetResult();
                return RobustFormat(translatedText, arguments);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to translate resource {Key} to {Lang}", key, languageCode);
                return RobustFormat(englishValue, arguments);
            }
        }

        private string RobustFormat(string template, object[] arguments)
        {
            if (arguments == null || arguments.Length == 0) return template;

            try
            {
                // Check if template contains enough placeholders
                bool hasPlaceholders = template.Contains("{0}");
                if (!hasPlaceholders && arguments.Length > 0)
                {
                    // Fallback: append arguments if placeholders are missing
                    var suffix = " [" + string.Join(", ", arguments) + "]";
                    return template + suffix;
                }

                return string.Format(template, arguments);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Format failure for template: {Template}", template);
                return template + " [" + string.Join(", ", arguments) + "]";
            }
        }

        private string GetEnglishText(string key)
        {
            var currentCulture = CultureInfo.CurrentUICulture;
            try {
                CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
                return _localizer[key].Value;
            } finally {
                CultureInfo.CurrentUICulture = currentCulture;
            }
        }

        private async Task<string> TranslateWithAi(string text, string languageName, string languageCode)
        {
            var prompt = $"Translate the following UI message from English to {languageName} ({languageCode}). " +
                         "CRITICAL: Keep placeholders like {0}, {1} exactly as they are. " +
                         "Output ONLY the translated text.\n\n" +
                         $"Text: {text}";
            
            // Resolve IChatService lazily to break circular dependency
            var chatService = _serviceProvider.GetRequiredService<IChatService>();
            
            // Use the current chat and user ID from the scoped context for billing
            if (_userContext.ChatId == 0 || _userContext.UserId == 0)
            {
                _logger.LogWarning("Skipping AI translation for resource {Lang} because ChatId/UserId is 0", languageCode);
                return text;
            }

            var translation = await chatService.Ask(_userContext.ChatId, _userContext.UserId, prompt);
            
            return translation.Trim();
        }
    }
}
