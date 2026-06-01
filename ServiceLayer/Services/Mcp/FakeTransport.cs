using System;
using System.Threading.Tasks;
using System.Threading.Channels;
using ModelContextProtocol.Protocol;

namespace ServiceLayer.Services.Mcp
{
    internal class FakeTransport : ITransport
    {
        public string SessionId => "fake-native-session";
        
        public ChannelReader<JsonRpcMessage> MessageReader { get; } = 
            Channel.CreateBounded<JsonRpcMessage>(1).Reader;

        public Task SendMessageAsync(JsonRpcMessage message, System.Threading.CancellationToken cancellationToken) => 
            Task.CompletedTask;

        public ValueTask DisposeAsync() => 
            ValueTask.CompletedTask;
    }
}
