using System.IO.Pipes;
using EtwSnap.Contracts.Models;
using EtwSnap.Contracts.Protocol;
using EtwSnap.Host.IPC;

namespace EtwSnap.IntegrationTests.IPC;

public sealed class PipeServerTests
{
    [Fact]
    public async Task ConcurrentClientsReceiveCorrelatedResponses()
    {
        var pipeName = $"etwsnap-test-{Guid.NewGuid():N}";
        using var cancellation = new CancellationTokenSource();
        await using var server = new PipeServer(pipeName, new PingDispatcher());
        var serverTask = server.RunAsync(cancellation.Token);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => SendPingAsync(pipeName)));

        Assert.All(results, result =>
        {
            Assert.True(result.Success);
            Assert.Equal(MessageKind.Response, result.MessageKind);
            Assert.Equal(CommandKind.Ping, result.Command);
            Assert.Equal(ProtocolConstants.CurrentVersion, result.ReadPayload<PingResult>().ProtocolVersion);
        });

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => serverTask);
    }

    private static async Task<WireMessage> SendPingAsync(string pipeName)
    {
        await using var pipe = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await pipe.ConnectAsync(timeout.Token);
        var request = WireMessage.CreateRequest(CommandKind.Ping, new EmptyRequest());
        await MessageFraming.WriteAsync(pipe, request, timeout.Token);
        var response = await MessageFraming.ReadAsync(pipe, timeout.Token);
        Assert.Equal(request.RequestId, response.RequestId);
        return response;
    }

    private sealed class PingDispatcher : ICommandDispatcher
    {
        public Task<WireMessage> DispatchAsync(
            WireMessage request,
            Func<WireMessage, ValueTask> progress,
            CancellationToken cancellationToken) => Task.FromResult(
                WireMessage.CreateResponse(
                    request,
                    true,
                    new PingResult(ProtocolConstants.CurrentVersion, "test")));
    }
}