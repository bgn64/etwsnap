using ETWSnap.Client;
using ETWSnap.IPC;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var parser = new CommandParser();
        var serviceManager = new ServiceManager();
        
        // Parse command-line arguments
        var parsedCommand = parser.Parse(args);
        
        if (!parsedCommand.IsValid)
        {
            Console.Error.WriteLine($"Error: {parsedCommand.ErrorMessage}");
            Console.WriteLine();
            parser.PrintUsage();
            return 1;
        }

        try
        {
            // Ensure service is running
            if (!await EnsureServiceRunningAsync(serviceManager))
            {
                Console.Error.WriteLine("Failed to start or connect to service");
                return 1;
            }

            // Execute command
            using var pipeClient = new NamedPipeClient();
            var exitCode = await ExecuteCommandAsync(pipeClient, parsedCommand);
            return exitCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Unexpected error: {ex.Message}");
            return 1;
        }
    }

    private static async Task<bool> EnsureServiceRunningAsync(IServiceManager serviceManager)
    {
        // Check if service is already running
        if (await serviceManager.IsServiceRunningAsync())
        {
            return true;
        }

        Console.WriteLine("Service not running, starting...");
        
        if (!await serviceManager.StartServiceAsync())
        {
            return false;
        }

        Console.WriteLine("Service started successfully");
        return true;
    }

    private static async Task<int> ExecuteCommandAsync(IPipeClient pipeClient, ParsedCommand parsedCommand)
    {
        // Create request based on parsed command
        var request = new Request
        {
            Command = parsedCommand.Type,
            FilePath = parsedCommand.FilePath
        };

        // Send command to service
        var response = await pipeClient.SendCommandAsync(request);

        // Handle response
        return HandleResponse(response, parsedCommand.Type);
    }

    private static int HandleResponse(Response response, CommandType commandType)
    {
        switch (response.Status)
        {
            case ResponseStatus.Success:
                Console.WriteLine(response.Message);
                if (commandType == CommandType.CheckStatus)
                {
                    Console.WriteLine($"Recording status: {(response.IsRecording ? "Active" : "Idle")}");
                }
                return 0;

            case ResponseStatus.AlreadyRecording:
                Console.Error.WriteLine("Error: A recording is already in progress");
                Console.Error.WriteLine("Use 'etwsnap stop <filepath>' to stop and save, or 'etwsnap cancel' to discard");
                return 1;

            case ResponseStatus.NotRecording:
                Console.Error.WriteLine("Error: No recording is currently active");
                return 1;

            case ResponseStatus.Error:
                Console.Error.WriteLine($"Error: {response.Message}");
                return 1;

            default:
                Console.Error.WriteLine($"Unknown response status: {response.Status}");
                return 1;
        }
    }
}

