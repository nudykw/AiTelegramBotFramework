using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace ServiceLayer.Services.Memory;

public class SemanticMemoryService : ISemanticMemoryService
{
    private const string CollectionName = "user_messages";
    private readonly QdrantClient _qdrantClient;
    private readonly IEmbeddingService _embeddingService;
    private readonly AppSettings _appSettings;
    private readonly ILogger<SemanticMemoryService> _logger;
    private readonly SemaphoreSlim _initSemaphore = new(1, 1);
    private bool _isInitialized = false;

    public SemanticMemoryService(
        QdrantClient qdrantClient,
        IEmbeddingService embeddingService,
        AppSettings appSettings,
        ILogger<SemanticMemoryService> logger)
    {
        _qdrantClient = qdrantClient;
        _embeddingService = embeddingService;
        _appSettings = appSettings;
        _logger = logger;
    }

    private async Task EnsureCollectionExistsAsync()
    {
        if (_isInitialized) return;

        await _initSemaphore.WaitAsync();
        try
        {
            if (_isInitialized) return;

            _logger.LogInformation("Initializing Qdrant connection and checking collections...");
            var collections = await _qdrantClient.ListCollectionsAsync();
            
            ulong expectedSize = 1536UL; // Default OpenAI
            if (string.Equals(_appSettings.MemorySettings.ProviderName, "Gemini", StringComparison.OrdinalIgnoreCase))
            {
                expectedSize = string.Equals(_appSettings.MemorySettings.ModelName, "gemini-embedding-2", StringComparison.OrdinalIgnoreCase) ? 3072UL : 768UL;
            }

            bool exists = collections.Any(c => string.Equals(c, CollectionName, StringComparison.OrdinalIgnoreCase));
            if (exists)
            {
                try
                {
                    var info = await _qdrantClient.GetCollectionInfoAsync(CollectionName);
                    var actualSize = info?.Config?.Params?.VectorsConfig?.Params?.Size;
                    if (actualSize.HasValue && actualSize.Value != expectedSize)
                    {
                        _logger.LogWarning("Qdrant collection '{CollectionName}' dimension mismatch: expected {Expected}, got {Actual}. Recreating collection...", CollectionName, expectedSize, actualSize.Value);
                        await _qdrantClient.DeleteCollectionAsync(CollectionName);
                        exists = false;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to verify Qdrant collection dimension. Proceeding...");
                }
            }

            if (!exists)
            {
                _logger.LogInformation("Creating Qdrant collection '{CollectionName}' with vector size {Size}", CollectionName, expectedSize);
                
                await _qdrantClient.CreateCollectionAsync(
                    collectionName: CollectionName,
                    vectorsConfig: new VectorParams
                    {
                        Size = expectedSize,
                        Distance = Distance.Cosine
                    }
                );
            }
            _isInitialized = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Qdrant collection. RAG features might be unavailable.");
        }
        finally
        {
            _initSemaphore.Release();
        }
    }

    public async Task SaveMessageAsync(long chatId, long userId, string role, string text, long messageId)
    {
        try
        {
            // Sane filtering: skip short messages, commands, and empty texts
            if (string.IsNullOrWhiteSpace(text) || 
                text.Length < _appSettings.MemorySettings.MinMessageLength || 
                text.StartsWith("/"))
            {
                return;
            }

            await EnsureCollectionExistsAsync();
            if (!_isInitialized) return;

            _logger.LogInformation("Generating embedding for background save: ChatId={ChatId}, MessageId={MessageId}", chatId, messageId);
            
            var embedding = await _embeddingService.GenerateEmbeddingAsync(
                text: text,
                providerName: _appSettings.MemorySettings.ProviderName,
                modelName: _appSettings.MemorySettings.ModelName
            );

            var point = new PointStruct
            {
                Id = Guid.NewGuid(), // Generate unique UUID to prevent collisions across chats
                Vectors = embedding,
                Payload =
                {
                    ["chat_id"] = chatId,
                    ["user_id"] = userId,
                    ["role"] = role,
                    ["text"] = text,
                    ["timestamp"] = DateTime.UtcNow.ToString("o")
                }
            };

            await _qdrantClient.UpsertAsync(CollectionName, new[] { point });
            _logger.LogInformation("Successfully saved message embedding to Qdrant: ChatId={ChatId}, MessageId={MessageId}", chatId, messageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save message embedding to Qdrant");
        }
    }

    public async Task<List<SemanticMemoryResult>> SearchMemoryAsync(long chatId, long userId, string queryText)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(queryText) || queryText.Length < _appSettings.MemorySettings.MinMessageLength)
            {
                return new List<SemanticMemoryResult>();
            }

            await EnsureCollectionExistsAsync();
            if (!_isInitialized) return new List<SemanticMemoryResult>();

            _logger.LogInformation("Generating embedding for memory search: ChatId={ChatId}", chatId);
            
            var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(
                text: queryText,
                providerName: _appSettings.MemorySettings.ProviderName,
                modelName: _appSettings.MemorySettings.ModelName
            );

            // Filter points by both chat_id and user_id to enforce data isolation and security
            var filter = new Filter
            {
                Must =
                {
                    new Condition { Field = new FieldCondition { Key = "chat_id", Match = new Match { Integer = chatId } } },
                    new Condition { Field = new FieldCondition { Key = "user_id", Match = new Match { Integer = userId } } }
                }
            };

            var searchResults = await _qdrantClient.SearchAsync(
                collectionName: CollectionName,
                vector: queryEmbedding,
                filter: filter,
                limit: (uint)_appSettings.MemorySettings.TopK,
                scoreThreshold: (float)_appSettings.MemorySettings.SimilarityThreshold
            );

            var results = new List<SemanticMemoryResult>();
            foreach (var hit in searchResults)
            {
                var payload = hit.Payload;
                if (payload.TryGetValue("text", out var textVal) && 
                    payload.TryGetValue("role", out var roleVal))
                {
                    DateTime timestamp = DateTime.UtcNow;
                    if (payload.TryGetValue("timestamp", out var tsVal) && DateTime.TryParse(tsVal.StringValue, out var parsedTs))
                    {
                        timestamp = parsedTs;
                    }

                    results.Add(new SemanticMemoryResult
                    {
                        Text = textVal.StringValue,
                        Role = roleVal.StringValue,
                        Timestamp = timestamp,
                        Score = hit.Score
                    });
                }
            }

            _logger.LogInformation("Found {Count} relevant memory matches in Qdrant.", results.Count);
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to perform semantic memory search in Qdrant");
            return new List<SemanticMemoryResult>();
        }
    }
}
