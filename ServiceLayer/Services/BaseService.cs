
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ServiceLayer.Services
{
    public abstract class BaseService
    {
        protected readonly IServiceProvider _serviceProvider;
        protected readonly ILogger _logger;

        public BaseService(IServiceProvider serviceProvider, ILogger logger)
        {

            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected void LogInformation(string message, params object?[] args)
        {
            _logger.Log(LogLevel.Information, message, args);
        }
    }
}
