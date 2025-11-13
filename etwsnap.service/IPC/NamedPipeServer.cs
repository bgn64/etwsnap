using System.IO.Pipes;
using System.Text.Json;
using ETWSnap.IPC;

namespace ETWSnap.Service.IPC;

/// <summary>
/// Interface for named pipe server
/// </summary>
public interface IPipeServer : IDisposable
{
    Task StartAsync(Func<Request, Task<Response>> commandHandler, CancellationToken cancellationToken = default);
    void Stop();
}

/// <summary>
/// Named pipe server for receiving commands from clients
/// </summary>
public class NamedPipeServer : IPipeServer
{
    private const string PipeName = "etwsnap-service";
    private readonly object _lockObj = new();
    private bool _isRunning;
    private CancellationTokenSource? _cts;

    public async Task StartAsync(Func<Request, Task<Response>> commandHandler, CancellationToken cancellationToken = default)
    {
        lock (_lockObj)
        {
            if (_isRunning)
            {
                throw new InvalidOperationException("Server is already running");
            }
            _isRunning = true;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        }

        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                await AcceptClientAsync(commandHandler, _cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when stopping
        }
        finally
        {
            lock (_lockObj)
            {
                _isRunning = false;
            }
        }
    }

    private async Task AcceptClientAsync(Func<Request, Task<Response>> commandHandler, CancellationToken cancellationToken)
    {
        var server = new NamedPipeServerStream(
            PipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        try
        {
            await server.WaitForConnectionAsync(cancellationToken);
            
            // Handle the client - the handler will dispose the server when done
            _ = Task.Run(async () =>
            {
                try
                {
                    await HandleClientAsync(server, commandHandler, cancellationToken);
                }
                finally
                {
                    server.Dispose();
                }
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Expected when stopping
            server.Dispose();
            throw;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error accepting client: {ex.Message}");
            server.Dispose();
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream server, Func<Request, Task<Response>> commandHandler, CancellationToken cancellationToken)
    {
        try
        {
            using var reader = new StreamReader(server, leaveOpen: true);
            using var writer = new StreamWriter(server, leaveOpen: true);

            var requestJson = await reader.ReadLineAsync(cancellationToken);
            
            if (string.IsNullOrEmpty(requestJson))
            {
                return;
            }

            // Deserialize request
            var request = JsonSerializer.Deserialize<Request>(requestJson);
            if (request == null)
            {
                var errorResponse = new Response
                {
                    Status = ResponseStatus.Error,
                    Message = "Invalid request format"
                };
                await SendResponseAsync(writer, errorResponse);
                return;
            }

            // Handle command
            var response = await commandHandler(request);

            // Send response
            await SendResponseAsync(writer, response);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error handling client: {ex.Message}");
        }
    }

    private async Task SendResponseAsync(StreamWriter writer, Response response)
    {
        var responseJson = JsonSerializer.Serialize(response);
        await writer.WriteLineAsync(responseJson);
        await writer.FlushAsync();
    }

    public void Stop()
    {
        lock (_lockObj)
        {
            _cts?.Cancel();
        }
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}
