using System.Threading.Tasks;

namespace ServiceLayer.Services.Memory;

public interface IEmbeddingService
{
    Task<float[]> GenerateEmbeddingAsync(string text, string providerName, string modelName);
}
