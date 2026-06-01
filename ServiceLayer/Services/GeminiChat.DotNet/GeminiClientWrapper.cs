using Google.GenAI;
using Google.GenAI.Types;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ServiceLayer.Services.GeminiChat.DotNet
{
    public class GeminiClientWrapper : IGeminiClient
    {
        private readonly Client _client;

        public GeminiClientWrapper(string apiKey)
        {
            _client = new Client(apiKey: apiKey);
        }

        public GeminiClientWrapper(Client client)
        {
            _client = client;
        }

        public async IAsyncEnumerable<Model> ListModelsAsync()
        {
            var models = await _client.Models.ListAsync();
            await foreach (var model in models)
            {
                yield return model;
            }
        }

        public Task<GenerateContentResponse> GenerateContentAsync(string model, string message)
        {
            return _client.Models.GenerateContentAsync(model, message);
        }

        public Task<GenerateContentResponse> GenerateContentAsync(string model, List<Content> content, GenerateContentConfig? config = null)
        {
            return _client.Models.GenerateContentAsync(model, content, config);
        }
    }
}
