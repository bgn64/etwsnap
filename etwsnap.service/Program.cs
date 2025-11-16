using ETWSnap.Service;
using ETWSnap.Service.IPC;
using ETWSnap.Service.Recording;
using ETWSnap.Service.Commands;

class Program
{
    static async Task Main(string[] args)
    {
        // Check for verbose flag
        bool verboseMode = args.Contains("--verbose") || args.Contains("-v");
        Logger.VerboseEnabled = verboseMode;

        Logger.Info("ETWSnap Service starting...");

        // Initialize components
        var screenRecorder = new ScreenRecorder();
        var stateManager = new RecordingStateManager(screenRecorder);
        var commandHandler = new CommandHandler(stateManager);
        using var pipeServer = new NamedPipeServer();

        // Set up cancellation for graceful shutdown
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            Logger.Info("\nShutdown requested...");
            cts.Cancel();
        };

        try
        {
            Logger.Info("ETWSnap Service started successfully");
            Logger.Info("Listening for commands on named pipe...");
            Logger.Info("Press Ctrl+C to stop the service");

            // Start listening for commands
            await pipeServer.StartAsync(
                async (request, onProgress) => await commandHandler.HandleCommandAsync(request, onProgress),
                cts.Token);
        }
        catch (OperationCanceledException)
        {
            Logger.Info("Service shutdown completed");
        }
        catch (Exception ex)
        {
            Logger.Error($"Service error: {ex.Message}");
            Environment.Exit(1);
        }
        finally
        {
            // Clean up screen recorder
            screenRecorder.Dispose();
        }
    }
}

