using System.Text.Json;
using ETWSnap.IPC;

namespace ETWSnap.Client;

/// <summary>
/// Interface for named pipe client communication
/// </summary>
public interface IPipeClient : IDisposable
{
    Task<Response> SendCommandAsync(Request request, CancellationToken cancellationToken = default);
    Task<bool> IsServiceAvailableAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Named pipe client for communicating with the etwsnap service
/// </summary>
public class NamedPipeClient : IPipeClient
{
    private const string PipeName = "etwsnap-service";
    private const int ConnectTimeoutMs = 2000;
    private readonly string _pipePath;

    public NamedPipeClient()
    {
        _pipePath = $"etwsnap-service";
    }

    public async Task<bool> IsServiceAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = new System.IO.Pipes.NamedPipeClientStream(".", _pipePath, System.IO.Pipes.PipeDirection.InOut);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(ConnectTimeoutMs);
            
            await client.ConnectAsync(cts.Token);
            return client.IsConnected;
        }
        catch
        {
            return false;
        }
    }

    public async Task<Response> SendCommandAsync(Request request, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = new System.IO.Pipes.NamedPipeClientStream(".", _pipePath, System.IO.Pipes.PipeDirection.InOut);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(ConnectTimeoutMs);
            
            await client.ConnectAsync(cts.Token);

            // Serialize and send request
            var requestJson = JsonSerializer.Serialize(request);
            using var writer = new StreamWriter(client, leaveOpen: true);
            await writer.WriteLineAsync(requestJson);
            await writer.FlushAsync();

            // Read response
            using var reader = new StreamReader(client, leaveOpen: true);
            var responseJson = await reader.ReadLineAsync(cancellationToken);
            
            if (string.IsNullOrEmpty(responseJson))
            {
                return new Response 
                { 
                    Status = ResponseStatus.Error, 
                    Message = "No response from service" 
                };
            }

            return JsonSerializer.Deserialize<Response>(responseJson) ?? new Response 
            { 
                Status = ResponseStatus.Error, 
                Message = "Invalid response format" 
            };
        }
        catch (TimeoutException)
        {
            return new Response 
            { 
                Status = ResponseStatus.Error, 
                Message = "Service connection timeout" 
            };
        }
        catch (Exception ex)
        {
            return new Response 
            { 
                Status = ResponseStatus.Error, 
                Message = $"Communication error: {ex.Message}" 
            };
        }
    }

    public void Dispose()
    {
        // No persistent resources to dispose
    }
}
