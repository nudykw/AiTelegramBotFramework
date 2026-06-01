using OpenAI.Audio;
using OpenAI.Chat;
using OpenAI.Models;

namespace ServiceLayer.Services
{
    public interface IChatService
    {
        Task<string> Ask(long chatId, long userId, string message);
        Task<IReadOnlyList<Model>> GetAvailibleModels(long? userId = null, bool validateModels = true);
        Task<ChatServiceResponse> SendMessages2ChatAsync(long telegramChatId, long telegramUserId, List<AiMessage> messages, string? model = null);
        Task<ChatServiceResponse> GenerateImage(long chatId, long telegramUserId, string prompt, string? modelName = null);
        Task<string> AudioTranscription(long chatId, long telegramUserId, Stream audio, string audioName, 
            string? model = null, string? prompt = null, AudioResponseFormat responseFormat = AudioResponseFormat.Json, 
            int? temperature = null, string? language = null);
        Task<ChatServiceResponse> CreateImageEditAsync(long chatId, long telegramUserId, string filePath, string? messageText);
        Task<ChatServiceResponse> AnalyzeImageAsync(long chatId, long telegramUserId, string? imageUrl, string? filePath = null, string? prompt = null, string? model = null);
        Task<(bool, string)> SetGPTModel(string? modelName, long? userId = null);
        Task<string?> GetSelectedModel(long userId);
        Task<Stream> TextToSpeech(long chatId, long telegramUserId, string text, string? model = null, string voice = "alloy");
        Task RefreshAvailibleModels(bool validate = true);
    }
}