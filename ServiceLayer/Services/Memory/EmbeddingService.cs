using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ServiceLayer.Services.Memory;

public class EmbeddingService : IEmbeddingService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AppSettings _appSettings;
    private readonly ILogger<EmbeddingService> _logger;

    public EmbeddingService(IHttpClientFactory httpClientFactory, AppSettings appSettings, ILogger<EmbeddingService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _appSettings = appSettings;
        _logger = logger;
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text, string providerName, string modelName)
    {
        var providers = _appSettings.TelegramBotConfiguration?.AiSettings?.ChatProviders;
        var provider = providers?.FirstOrDefault(p => string.Equals(p.Name, providerName, StringComparison.OrdinalIgnoreCase));
        if (provider == null)
        {
            throw new ArgumentException($"AI provider '{providerName}' was not found in ChatProviders configuration.");
        }

        string apiKey = provider.ApiKey;
        string baseUrl = provider.BaseUrl ?? "";

        if (string.Equals(provider.ProviderType, "Gemini", StringComparison.OrdinalIgnoreCase))
        {
            return await GetGeminiEmbeddingAsync(text, apiKey, modelName);
        }
        else
        {
            return await GetOpenAIEmbeddingAsync(text, apiKey, baseUrl, modelName);
        }
    }

    private async Task<float[]> GetOpenAIEmbeddingAsync(string text, string apiKey, string baseUrl, string modelName)
    {
        if (string.IsNullOrEmpty(baseUrl))
        {
            baseUrl = "https://api.openai.com/v1";
        }
        else if (!baseUrl.EndsWith("/v1") && !baseUrl.Contains("/v1/"))
        {
            baseUrl = baseUrl.TrimEnd('/') + "/v1";
        }

        var endpoint = $"{baseUrl.TrimEnd('/')}/embeddings";
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

        var payload = new
        {
            input = text,
            model = modelName
        };

        _logger.LogInformation("Requesting OpenAI embedding: Endpoint={Endpoint}, Model={Model}", endpoint, modelName);
        var response = await client.PostAsJsonAsync(endpoint, payload);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            _logger.LogError("OpenAI embedding request failed: Status={Status}, Error={Error}", response.StatusCode, error);
            throw new Exception($"OpenAI embedding generation failed: {response.StatusCode} - {error}");
        }

        var json = await response.Content.ReadFromJsonAsync<OpenAIEmbeddingResponse>();
        if (json?.Data == null || json.Data.Count == 0 || json.Data[0].Embedding == null)
        {
            throw new Exception("OpenAI embedding API returned an empty or invalid response.");
        }

        return json.Data[0].Embedding;
    }

    private async Task<float[]> GetGeminiEmbeddingAsync(string text, string apiKey, string modelName)
    {
        if (string.IsNullOrEmpty(modelName))
        {
            modelName = "gemini-embedding-2";
        }

        var endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{modelName}:embedContent?key={apiKey}";
        var client = _httpClientFactory.CreateClient();

        var payload = new
        {
            content = new
            {
                parts = new[]
                {
                    new { text = text }
                }
            }
        };

        _logger.LogInformation("Requesting Gemini embedding: Model={Model}", modelName);
        var response = await client.PostAsJsonAsync(endpoint, payload);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            _logger.LogError("Gemini embedding request failed: Status={Status}, Error={Error}", response.StatusCode, error);
            throw new Exception($"Gemini embedding generation failed: {response.StatusCode} - {error}");
        }

        var json = await response.Content.ReadFromJsonAsync<GeminiEmbeddingResponse>();
        if (json?.Embedding?.Values == null)
        {
            throw new Exception("Gemini embedding API returned an empty or invalid response.");
        }

        return json.Embedding.Values;
    }

    private class OpenAIEmbeddingResponse
    {
        [JsonPropertyName("data")]
        public List<OpenAIEmbeddingData>? Data { get; set; }
    }

    private class OpenAIEmbeddingData
    {
        [JsonPropertyName("embedding")]
        public float[]? Embedding { get; set; }
    }

    private class GeminiEmbeddingResponse
    {
        [JsonPropertyName("embedding")]
        public GeminiEmbeddingData? Embedding { get; set; }
    }

    private class GeminiEmbeddingData
    {
        [JsonPropertyName("values")]
        public float[]? Values { get; set; }
    }
}
