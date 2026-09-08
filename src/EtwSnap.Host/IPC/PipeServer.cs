using System.Collections.Concurrent;
using System.IO.Pipes;
using EtwSnap.Contracts.Protocol;

namespace EtwSnap.Host.IPC;

internal sealed class PipeServer(string pipeName, ICommandDispatcher dispatcher) : IAsyncDisposable
{
    private readonly CancellationTokenSource _shutdown = new();
    private readonly ConcurrentDictionary<int, Task> _clients = new();
    private int _nextClientId;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);

        while (!linked.IsCancellationRequested)
        {
            var pipe = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

            try
            {
                await pipe.WaitForConnectionAsync(linked.Token).ConfigureAwait(false);
                var clientId = Interlocked.Increment(ref _nextClientId);
                var task = HandleClientAsync(pipe, linked.Token);
                _clients[clientId] = task;
                _ = task.ContinueWith(
                    _ => _clients.TryRemove(clientId, out Task? _),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
            catch
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        await using (pipe.ConfigureAwait(false))
        {
            try
            {
                var request = await MessageFraming.ReadAsync(pipe, cancellationToken).ConfigureAwait(false);
                var writeGate = new SemaphoreSlim(1, 1);

                async ValueTask SendAsync(WireMessage message)
                {
                    await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        await MessageFraming.WriteAsync(pipe, message, cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        writeGate.Release();
                    }
                }

                var response = await dispatcher.DispatchAsync(request, SendAsync, cancellationToken).ConfigureAwait(false);
                await SendAsync(response).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (IOException)
            {
            }
            catch (InvalidDataException)
            {
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _shutdown.CancelAsync().ConfigureAwait(false);
        await Task.WhenAll(_clients.Values).ConfigureAwait(false);
        _shutdown.Dispose();
    }
}

internal interface ICommandDispatcher
{
    Task<WireMessage> DispatchAsync(
        WireMessage request,
        Func<WireMessage, ValueTask> progress,
        CancellationToken cancellationToken);
}
