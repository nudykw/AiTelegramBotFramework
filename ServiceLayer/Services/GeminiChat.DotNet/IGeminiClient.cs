using Google.GenAI.Types;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ServiceLayer.Services.GeminiChat.DotNet
{
    public interface IGeminiClient
    {
        IAsyncEnumerable<Model> ListModelsAsync();
        Task<GenerateContentResponse> GenerateContentAsync(string model, string message);
        Task<GenerateContentResponse> GenerateContentAsync(string model, List<Content> content, GenerateContentConfig? config = null);
    }
}
