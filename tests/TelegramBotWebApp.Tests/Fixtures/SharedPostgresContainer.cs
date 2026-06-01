using Testcontainers.PostgreSql;
using Npgsql;

namespace TelegramBotWebApp.Tests.Fixtures;

public static class SharedPostgresContainer
{
    private static readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:15-alpine")
        .Build();

    private static readonly Task _startTask;

    static SharedPostgresContainer()
    {
        _startTask = Task.Run(async () =>
        {
            await _container.StartAsync();
        });
    }

    public static string ConnectionString
    {
        get
        {
            _startTask.GetAwaiter().GetResult();
            return _container.GetConnectionString();
        }
    }

    public static string GetUniqueConnectionString()
    {
        var baseConn = ConnectionString;
        var builder = new NpgsqlConnectionStringBuilder(baseConn)
        {
            Database = $"test_db_{Guid.NewGuid():N}"
        };
        return builder.ToString();
    }
}
