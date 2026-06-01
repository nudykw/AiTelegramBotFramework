using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ServiceLayer.Services.Memory;

public interface ISemanticMemoryService
{
    /// <summary>
    /// Background asynchronous saving of a message into the Qdrant vector database.
    /// </summary>
    Task SaveMessageAsync(long chatId, long userId, string role, string text, long messageId);

    /// <summary>
    /// Semantic search of relevant messages by a query vector.
    /// </summary>
    Task<List<SemanticMemoryResult>> SearchMemoryAsync(long chatId, long userId, string queryText);
}

public class SemanticMemoryResult
{
    public string Text { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public double Score { get; set; }
}
