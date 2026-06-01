using System.Net;
using System.Security.Authentication;
using ServiceLayer.Utils;
using ServiceLayer.Services.Localization;
using Xunit;
using Microsoft.Extensions.Logging;

namespace ServiceLayer.UnitTests;

public class AiErrorHelperTests
{
    private static readonly IDynamicLocalizer NullLocalizer = new NullLocalizer();

    [Fact]
    public void GetErrorDetails_HttpRequestException_Unauthorized_ReturnsCorrectionMessage()
    {
        // Arrange
        var ex = new HttpRequestException("Unauthorized", null, HttpStatusCode.Unauthorized);
        var provider = "TestProvider";

        // Act
        var result = AiErrorHelper.HandleAndGetException(null, ex, provider, "TestMethod", NullLocalizer);

        // Assert
        var aiEx = Assert.IsType<AiProviderException>(result);
        Assert.Contains("AiError_InvalidApiKey", aiEx.Message);
        Assert.Contains("TestProvider", aiEx.ProviderName);
        Assert.Contains("401", aiEx.TechnicalDetails);
    }

    [Fact]
    public void GetErrorDetails_HttpRequestException_TooManyRequests_ReturnsCorrectMessage()
    {
        // Arrange
        var ex = new HttpRequestException("Rate limit", null, HttpStatusCode.TooManyRequests);
        var provider = "TestProvider";

        // Act
        var result = AiErrorHelper.HandleAndGetException(null, ex, provider, "TestMethod", NullLocalizer);

        // Assert
        var aiEx = Assert.IsType<AiProviderException>(result);
        Assert.Contains("AiError_RateLimit", aiEx.Message);
    }

    [Fact]
    public void GetErrorDetails_AuthenticationException_ReturnsCorrectMessage()
    {
        // Arrange
        var ex = new AuthenticationException("Invalid API Key");
        var provider = "TestProvider";

        // Act
        var result = AiErrorHelper.HandleAndGetException(null, ex, provider, "TestMethod", NullLocalizer);

        // Assert
        var aiEx = Assert.IsType<AiProviderException>(result);
        Assert.Contains("AiError_InvalidApiKey", aiEx.Message);
    }

    [Fact]
    public void GetErrorDetails_GeneralException_ContainsGenericMessage()
    {
        // Arrange
        var ex = new Exception("Something went wrong");
        var provider = "TestProvider";

        // Act
        var result = AiErrorHelper.HandleAndGetException(null, ex, provider, "TestMethod", NullLocalizer);

        // Assert
        var aiEx = Assert.IsType<AiProviderException>(result);
        Assert.Contains("AiError_Default", aiEx.Message);
        Assert.Equal("Something went wrong", aiEx.TechnicalDetails);
    }
}
