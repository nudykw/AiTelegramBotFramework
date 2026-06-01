using System.Collections.Generic;

namespace ServiceLayer.Services;

public interface IChatServiceFactory
{
    IEnumerable<ChatProviderConfig> GetAvailableProviders();
    IChatService CreateService(string providerName);
    IChatService CreateService(string providerName, string modelName);
}
