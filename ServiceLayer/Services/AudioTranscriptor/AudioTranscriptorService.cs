using Microsoft.Extensions.Logging;
using OpenAI.Audio;
using ServiceLayer.Services.OpenAI;

namespace ServiceLayer.Services.AudioTranscriptor
{
    public class AudioTranscriptorService : BaseService
    {
        private readonly IChatServiceFactory _chatServiceFactory;

        public AudioTranscriptorService(IServiceProvider serviceProvider,
            ILogger<AudioTranscriptorService> logger,
            IChatServiceFactory chatServiceFactory)
            : base(serviceProvider, logger)
        {
            _chatServiceFactory = chatServiceFactory;
        }

        private OpenAIService GetOpenAIService() => (OpenAIService)_chatServiceFactory.CreateService("OpenAI");

        public async Task<string> AudioTranscription(long chatId, long telegramUserId, Stream audio, string audioName,
            string? model = null, string? prompt = null, int? temperature = null,
            string? language = null)
        {
            var responce = await GetOpenAIService().AudioTranscription(chatId, telegramUserId, audio,
                audioName, model, prompt, AudioResponseFormat.Json, temperature, language);
            if (string.IsNullOrWhiteSpace(responce))
            {
                responce = "bla bla bla.";
            }
            _logger.LogInformation($"Transcripted audio: {responce}");
            return responce;
        }
    }
}
