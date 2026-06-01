using System;

namespace ServiceLayer.Services.Memory
{
    public class BotMetadata
    {
        public string BotName { get; set; } = string.Empty;
        public string BotUsername { get; set; } = string.Empty;
        public string ActiveModel { get; set; } = string.Empty;
        public string ActiveProvider { get; set; } = string.Empty;
        public int ContextWindow { get; set; }
        public int OutputLimit { get; set; }
        public string[] SupportedFeatures { get; set; } = Array.Empty<string>();
        public UserBalanceInfo? UserBalance { get; set; }
    }

    public class UserBalanceInfo
    {
        public decimal Balance { get; set; }
        public bool IsUnlimited { get; set; }
    }
}
