using ETWSnap.Service.IPC;
using ETWSnap.Service.Recording;
using ETWSnap.Service.Commands;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("ETWSnap Service starting...");

        // Initialize components
        var stateManager = new RecordingStateManager();
        var commandHandler = new CommandHandler(stateManager);
        using var pipeServer = new NamedPipeServer();

        // Set up cancellation for graceful shutdown
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            Console.WriteLine("\nShutdown requested...");
            cts.Cancel();
        };

        try
        {
            Console.WriteLine("ETWSnap Service started successfully");
            Console.WriteLine("Listening for commands on named pipe...");
            Console.WriteLine("Press Ctrl+C to stop the service");

            // Start listening for commands
            await pipeServer.StartAsync(
                async (request) => await commandHandler.HandleCommandAsync(request),
                cts.Token);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Service shutdown completed");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Service error: {ex.Message}");
            Environment.Exit(1);
        }
    }
}

