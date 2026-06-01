namespace TelegramBotWebApp.Endpoints;

public static class HealthEndpoints
{
    /// <summary>Maps GET /health — returns 200 OK with a brief status payload.</summary>
    public static void MapHealthEndpoints(this WebApplication app)
    {
        app.MapGet("/health", () => Results.Ok(new
        {
            status = "healthy",
            timestamp = DateTimeOffset.UtcNow
        }))
        .WithName("HealthCheck")
        .WithSummary("Health check")
        .WithDescription("Returns 200 OK when the application is running.")
        .WithTags("System")
        .AllowAnonymous();
    }
}
