using System.Net;
using System.Security.Authentication;
using Microsoft.Extensions.Logging;
using ServiceLayer.Services.Localization;

namespace ServiceLayer.Utils;

public record AiErrorDetails(string UserMessage, string TechnicalDetails);

public class AiProviderException : Exception
{
    public string TechnicalDetails { get; }
    public string ProviderName { get; }

    public AiProviderException(string userMessage, string technicalDetails, string providerName, Exception innerException)
        : base(userMessage, innerException)
    {
        TechnicalDetails = technicalDetails;
        ProviderName = providerName;
    }
}

public static class AiErrorHelper
{
    public static AiErrorDetails GetErrorDetails(IDynamicLocalizer localizer, Exception ex, string providerName)
    {
        string userMessage = localizer["AiError_Default"];
        string technicalDetails = ex.Message;

        if (ex is OperationCanceledException)
        {
            userMessage = localizer["AiError_Cancelled"];
            technicalDetails = "Operation was cancelled by the user or timeout";
            return new AiErrorDetails(userMessage, technicalDetails);
        }

        if (ex is HttpRequestException httpEx)
        {
            technicalDetails = $"HTTP Error {(int?)httpEx.StatusCode}: {httpEx.Message}";
            
            switch (httpEx.StatusCode)
            {
                case HttpStatusCode.Unauthorized:
                    userMessage = localizer["AiError_InvalidApiKey"];
                    break;
                case HttpStatusCode.Forbidden:
                    userMessage = localizer["AiError_Forbidden"];
                    break;
                case HttpStatusCode.TooManyRequests:
                    userMessage = localizer["AiError_RateLimit"];
                    break;
                case HttpStatusCode.BadRequest:
                    userMessage = localizer["AiError_BadRequest", providerName];
                    break;
                case HttpStatusCode.NotFound:
                    userMessage = localizer["AiError_ModelNotFound", providerName];
                    break;
                case HttpStatusCode.ServiceUnavailable:
                    userMessage = localizer["AiError_ProviderUnavailable", providerName];
                    break;
                default:
                    if ((int?)httpEx.StatusCode >= 500)
                    {
                        userMessage = localizer["AiError_ServerError", (int?)httpEx.StatusCode, providerName];
                    }
                    break;
            }
        }
        else if (ex is AuthenticationException authEx)
        {
            userMessage = localizer["AiError_InvalidApiKey"];
            technicalDetails = authEx.Message;
        }
        else if (ex.GetType().Name.Contains("ApiException") || ex.GetType().Name.Contains("GoogleApiException"))
        {
            // Handling Google.GenAI or similar API-specific exceptions
            technicalDetails = $"API Specific Error: {ex.Message}";
            
            var msg = ex.Message.ToLowerInvariant();
            if (msg.Contains("quota") || msg.Contains("limit") || msg.Contains("429"))
            {
                userMessage = localizer["AiError_RateLimit"];
            }
            else if (msg.Contains("key") || msg.Contains("auth") || msg.Contains("401"))
            {
                userMessage = localizer["AiError_InvalidApiKey"];
            }
            else if (msg.Contains("not found") || msg.Contains("404"))
            {
                userMessage = localizer["AiError_ModelNotFound", providerName];
            }
        }

        return new AiErrorDetails(userMessage, technicalDetails);
    }

    public static void LogDetailedError(ILogger? logger, Exception ex, string providerName, string methodName, IDynamicLocalizer? localizer = null)
    {
        var localizerToUse = localizer ?? new NullLocalizer();
        var details = GetErrorDetails(localizerToUse, ex, providerName);
        logger?.LogError(ex, "Provider {Provider} failed in {Method}. UserMessage: {UserMessage}. TechnicalDetails: {TechnicalDetails}",
            providerName, methodName, details.UserMessage, details.TechnicalDetails);
    }
    public static Exception HandleAndGetException(ILogger? logger, Exception ex, string providerName, string methodName, IDynamicLocalizer localizer)
    {
        LogDetailedError(logger, ex, providerName, methodName, localizer);
        var details = GetErrorDetails(localizer, ex, providerName);
        return new AiProviderException(details.UserMessage, details.TechnicalDetails, providerName, ex);
    }
}
