using System.Threading.Tasks;

namespace ServiceLayer.Services.Mcp
{
    public interface INativeMcpTool
    {
        string Name { get; }
        string ServerName { get; }
        string Description { get; }
        string JsonSchema { get; }
        Task<string> ExecuteAsync(string argumentsJson);
    }
}
